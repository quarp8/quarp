using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

/// <summary>
/// What the music editor looks like right now, as opposed to what the cartridge <em>is</em>:
/// which ten of the sixty-four patterns the grid is showing, which channels the author has
/// silenced for listening, whether the song is sounding and where, the half-typed slot number,
/// the marking gesture in flight, and the footer's exit question. Headless like
/// <see cref="MapEditorView"/>, <see cref="CodeEditorView"/> and <see cref="SfxEditorView"/>, and
/// for the same reason — every claim about it is a plain unit test instead of a mouse at a
/// window.
///
/// <para><b>One owner each, and they do not overlap.</b> The 320 bytes and the cursor belong to
/// <see cref="MusicEditorSession"/> and are never copied here; the geometry belongs to
/// <see cref="MusicEditorLayout"/> and arrives as a parameter; everything the author is
/// <em>looking at or listening past</em> belongs here and nowhere else.</para>
///
/// <para><b>Why mute and solo live here and could live nowhere else.</b>
/// <c>music.bin</c> has no bit for them and cannot grow one: every bit of both its tables is
/// spoken for and the spare ones must be zero (AUDIO-FORMAT §4). That is not a gap in the format
/// — it is the right answer. Muting a channel to hear the bass is an <b>audition control</b>, and
/// if it changed one byte of the cartridge it would change the cartridge's identity and every
/// replay recorded against it (REPLAY-FORMAT §5). So the eight toggles are screen state, they
/// survive exactly as long as this object does, and the only place they reach the outside world
/// is <see cref="AudiblePayload"/> — a <em>copy</em> handed to the preview chip, never the
/// session's own bytes.</para>
///
/// <para><b>Solo wins over mute, which is the tracker convention and not a coin toss.</b> With
/// nothing soloed a channel is audible unless it is muted; with anything soloed only the soloed
/// channels are audible, whatever their mute flags say. That is what makes solo a momentary
/// "listen to this" rather than a second, competing mute — and it is why un-soloing restores the
/// mutes the author set earlier instead of losing them.</para>
///
/// <para><b>Playback is asked for here and performed by the wiring</b>, exactly as on the sound
/// screen. This type owns <see cref="PlayWanted"/> ("the author asked for the song"),
/// <see cref="PlayEpoch"/> ("and this is a different asking from the last one"),
/// <see cref="PlayFrom"/> ("starting at this pattern") and <see cref="Playing"/> /
/// <see cref="PlayingPattern"/> ("and the chip says it is at this one"). It owns no synthesizer
/// and no speaker, because it may own neither: the one owner of synthesis is
/// <c>Quarp.Core.Audio.Apu</c>, the one owner of the speaker is <see cref="AudioOutput"/>, and
/// both live above this layer. <c>QuarpGame</c> reads these members, drives the APU and reports
/// back through <see cref="ReportPlaying"/> — the same shape the boot jingle and the sfx audition
/// already use, and the reason there is no second synthesizer anywhere in the shell.</para>
///
/// <para><b>Follow is not a mode here.</b> TIC-80 spends a button on "follow the playhead"
/// because its tracker shows a small window of a long pattern and an author often wants to keep
/// editing elsewhere while it plays. Our grid shows ten of sixty-four rows and the whole song is
/// on screen anyway in the overview, so a follow switch would buy a choice between "see where the
/// music is" and "see where the music is not". <see cref="Sync"/> therefore scrolls to the
/// playhead while the song sounds and to the cursor when it does not, always, and that decision
/// is written down here rather than left to a button nobody would find.</para>
///
/// <para><b>The read-only bank is refused here, not at the session's door.</b> Every writing verb
/// below returns early when <see cref="MusicEditorSession.BankReadOnly"/> is true, which is
/// exactly where <c>MapEditorPaint</c> and <see cref="SfxEditorView"/> put the same guard: the
/// session still throws (that is its contract and its second lock), but a screen must not throw
/// at an author who pressed a key — it must do nothing and keep saying why on the prompt
/// line.</para>
/// </summary>
public sealed class MusicEditorView
{
    /// <summary>The value <see cref="PlayingPattern"/> holds while the song is not sounding.</summary>
    public const int NoPattern = -1;

    /// <summary>The value <see cref="PendingDigit"/> holds when no digit is half-typed.</summary>
    public const int NoDigit = -1;

    private readonly bool[] _muted = new bool[MusicEditorSession.ChannelCount];
    private readonly bool[] _soloed = new bool[MusicEditorSession.ChannelCount];

    /// <summary>The first of the <see cref="MusicEditorLayout.VisibleRows"/> patterns the grid is showing.</summary>
    public int FirstPattern { get; private set; }

    /// <summary>True while the dirty-exit question is on the footer line; the router then gives it the input.</summary>
    public bool ExitPromptShown { get; private set; }

    /// <summary>True between the press and the release of a marking drag across the grid.</summary>
    public bool MarkDragActive { get; private set; }

    /// <summary>Where a marking gesture started — the corner <see cref="MusicEditorSession.SelectRange"/> measures from.</summary>
    public int AnchorPattern { get; private set; }

    /// <summary>The channel half of that corner.</summary>
    public int AnchorChannel { get; private set; }

    /// <summary>
    /// The first digit of a two-digit slot number, or <see cref="NoDigit"/>. A slot is 0-63 and a
    /// digit is 0-9, so typing one is never enough; the half-typed state is shown in the cell (as
    /// <c>7_</c>) rather than kept secret, which is what makes the gesture learnable.
    /// </summary>
    public int PendingDigit { get; private set; } = NoDigit;

    /// <summary>The author has asked for the song to sound and has not asked it to stop.</summary>
    public bool PlayWanted { get; private set; }

    /// <summary>
    /// Bumped by every fresh <see cref="RequestPlay"/>. The wiring compares it with the epoch it
    /// last started, so pressing play twice restarts the song from the cursor instead of being
    /// swallowed as "already playing" — the whole gesture when an author is auditioning one edit
    /// at a time.
    /// </summary>
    public int PlayEpoch { get; private set; }

    /// <summary>Which pattern the current asking starts from — the cursor's, taken at the moment Space was pressed.</summary>
    public int PlayFrom { get; private set; }

    /// <summary>What the chip actually reports, as opposed to what was asked for — the play button's lit state.</summary>
    public bool Playing { get; private set; }

    /// <summary>The pattern the chip is playing, or <see cref="NoPattern"/> — the playhead the grid and the overview mark.</summary>
    public int PlayingPattern { get; private set; } = NoPattern;

    /// <summary>True when this channel has been silenced for listening. Never a byte of the cartridge — see the type note.</summary>
    public bool ChannelMuted(int channel) => _muted[Validate(channel)];

    /// <summary>True when this channel has been soloed.</summary>
    public bool ChannelSoloed(int channel) => _soloed[Validate(channel)];

    /// <summary>True when any channel is soloed — the state in which mute stops deciding anything.</summary>
    public bool AnySolo
    {
        get
        {
            for (int i = 0; i < _soloed.Length; i++)
            {
                if (_soloed[i])
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Whether this channel reaches the speaker: solo wins over mute (see the type note). The one
    /// owner of that rule — the renderer dims a channel by asking this, and
    /// <see cref="AudiblePayload"/> silences one by asking this, so the picture and the sound
    /// cannot disagree.
    /// </summary>
    public bool ChannelAudible(int channel) =>
        AnySolo ? _soloed[Validate(channel)] : !_muted[Validate(channel)];

    /// <summary>The mute toggle's click, and Shift+1..4's key.</summary>
    public void ToggleMute(int channel) => _muted[Validate(channel)] = !_muted[channel];

    /// <summary>The solo toggle's click, and Shift+5..8's key.</summary>
    public void ToggleSolo(int channel) => _soloed[Validate(channel)] = !_soloed[channel];

    // ---- the window ----

    /// <summary>
    /// Puts the window's first pattern somewhere, clamped so the last row is never past the end
    /// of the song — a wheel notch at the bottom is not a caller bug.
    /// </summary>
    public void ScrollTo(in MusicEditorLayout layout, int firstPattern) =>
        FirstPattern = Math.Clamp(firstPattern, 0, layout.MaxFirstPattern);

    /// <summary>The wheel and the page keys: the window by so many patterns.</summary>
    public void ScrollBy(in MusicEditorLayout layout, int patterns) =>
        ScrollTo(layout, FirstPattern + patterns);

    /// <summary>
    /// Brings the window to wherever the author's attention is: the playhead while the song
    /// sounds, the cursor otherwise (see the type note on why that is not a switch). Called once
    /// per frame by the router, and by a test that wants the window a real frame would show.
    /// </summary>
    public void Sync(in MusicEditorLayout layout, MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int anchor = Playing && PlayingPattern >= 0 ? PlayingPattern : session.CursorPattern;
        int first = Math.Clamp(FirstPattern, 0, layout.MaxFirstPattern);
        if (anchor < first)
        {
            first = anchor;
        }
        else if (anchor >= first + layout.VisibleRows)
        {
            first = anchor - layout.VisibleRows + 1;
        }
        FirstPattern = Math.Clamp(first, 0, layout.MaxFirstPattern);
    }

    // ---- the cursor ----

    /// <summary>
    /// The arrows, and the page keys with a whole screenful: the session owns where the cursor
    /// is, this owns that a move abandons a half-typed slot number. Both halves have to happen
    /// together or a digit typed before an arrow would land on the row after it.
    /// </summary>
    public void MoveCursor(MusicEditorSession session, int patterns, int channels)
    {
        ArgumentNullException.ThrowIfNull(session);
        CancelDigit();
        session.MoveCursor(patterns, channels);
    }

    /// <summary>A click on a cell: the cursor goes there, the half-typed digit is forgotten, and the marking is dropped.</summary>
    public void PlaceCursor(MusicEditorSession session, int pattern, int channel)
    {
        ArgumentNullException.ThrowIfNull(session);
        CancelDigit();
        session.SetCursor(pattern, channel);
    }

    /// <summary>Forgets a half-typed slot number — every cursor move and every gesture that changes focus does this.</summary>
    public void CancelDigit() => PendingDigit = NoDigit;

    // ---- writing a cell ----

    /// <summary>
    /// A digit key at the cursor. The first digit is remembered and shown; the second completes a
    /// slot number and writes it, after which the cursor steps to the next pattern
    /// (<see cref="MusicEditorSession.EnterSlot"/> does both, and is the one owner of the gesture
    /// both hands share).
    ///
    /// <para><b>A pair above 63 is refused rather than clamped.</b> Typing 9 then 9 means slot 99,
    /// which does not exist; clamping it to 63 would play a sound the author did not ask for, so
    /// nothing is written and the second digit becomes the new first one — the author is already
    /// typing the number they meant.</para>
    /// </summary>
    /// <returns>True when a slot was written.</returns>
    public bool TypeDigit(MusicEditorSession session, int digit)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (digit is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(digit), digit, "a decimal digit is 0-9.");
        }
        if (session.BankReadOnly)
        {
            return false;
        }
        if (PendingDigit == NoDigit)
        {
            PendingDigit = digit;
            return false;
        }
        int slot = PendingDigit * 10 + digit;
        if (slot > MusicEditorSession.MaxSlot)
        {
            PendingDigit = digit;
            return false;
        }
        PendingDigit = NoDigit;
        session.EnterSlot(slot);
        return true;
    }

    /// <summary>Del at the cursor: the cell falls silent and the cursor steps on, the twin of a completed digit pair.</summary>
    public void EnterRest(MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        CancelDigit();
        if (session.BankReadOnly)
        {
            return;
        }
        session.EnterRest();
    }

    /// <summary>
    /// The wheel over a cell, and the mouse's whole answer to "type a slot number": one step
    /// along the bank. A silent cell steps up to slot 0 and slot 0 steps down to silence, so the
    /// wheel reaches every state a digit pair and Del reach — which is what the input-parity law
    /// asks of it.
    /// </summary>
    public void StepSlot(MusicEditorSession session, int pattern, int channel, int delta)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly || delta == 0)
        {
            return;
        }
        CancelDigit();
        int slot = session.ChannelSlot(pattern, channel);
        if (slot < 0)
        {
            if (delta > 0)
            {
                session.SetChannelSlot(pattern, channel, 0);
            }
            return;
        }
        int wanted = slot + delta;
        if (wanted < 0)
        {
            session.ClearChannel(pattern, channel);
            return;
        }
        session.SetChannelSlot(pattern, channel, Math.Min(wanted, MusicEditorSession.MaxSlot));
    }

    /// <summary>One section flag of the pattern under the cursor, or of the one a click landed on.</summary>
    public void ToggleFlag(MusicEditorSession session, int pattern, MusicFlagColumn flag)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BankReadOnly)
        {
            return;
        }
        CancelDigit();
        session.TogglePatternFlag(pattern, FlagBit(flag));
    }

    /// <summary>Which bit of <c>music.bin</c> a screen column means — the one place the two orders meet.</summary>
    public static byte FlagBit(MusicFlagColumn flag) => flag switch
    {
        MusicFlagColumn.LoopStart => MusicEditorSession.FlagLoopStart,
        MusicFlagColumn.LoopEnd => MusicEditorSession.FlagLoopEnd,
        _ => MusicEditorSession.FlagStop,
    };

    // ---- the marking ----

    /// <summary>A press on a cell opens a marking gesture anchored there; the cursor follows the pointer.</summary>
    public void BeginMark(MusicEditorSession session, int pattern, int channel)
    {
        ArgumentNullException.ThrowIfNull(session);
        MarkDragActive = true;
        AnchorPattern = pattern;
        AnchorChannel = channel;
        PlaceCursor(session, pattern, channel);
        session.SelectRange(pattern, channel, pattern, channel);
    }

    /// <summary>Every later sample of the gesture, and Shift+arrow's verb: the rectangle grows to here.</summary>
    public void ExtendMark(MusicEditorSession session, int pattern, int channel)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.HasSelection)
        {
            AnchorPattern = session.CursorPattern;
            AnchorChannel = session.CursorChannel;
        }
        session.SetCursor(pattern, channel);
        session.SelectRange(AnchorPattern, AnchorChannel, pattern, channel);
    }

    /// <summary>The release. Safe without an open gesture — releases arrive from off the grid.</summary>
    public void EndMark() => MarkDragActive = false;

    // ---- playback ----

    /// <summary>Space, and the play button: start the song at the cursor's pattern. A second ask restarts it (see <see cref="PlayEpoch"/>).</summary>
    public void RequestPlay(MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        PlayFrom = session.CursorPattern;
        PlayWanted = true;
        PlayEpoch++;
    }

    /// <summary>Stop asking for sound. Idempotent — a stop with nothing playing is not an error.</summary>
    public void RequestStop()
    {
        PlayWanted = false;
        Playing = false;
        PlayingPattern = NoPattern;
    }

    /// <summary>Space's whole body, and the play button's: play what is silent, silence what is playing.</summary>
    public void TogglePlay(MusicEditorSession session)
    {
        if (PlayWanted)
        {
            RequestStop();
        }
        else
        {
            RequestPlay(session);
        }
    }

    /// <summary>
    /// The wiring's report of what the chip is doing. A song that has run out of patterns — a
    /// stop flag, or falling off pattern 63 — stops being wanted too, so the button goes dark by
    /// itself and the next Space starts it again rather than having to stop it first.
    /// </summary>
    public void ReportPlaying(bool sounding, int pattern)
    {
        Playing = sounding;
        PlayingPattern = sounding ? pattern : NoPattern;
        if (!sounding)
        {
            PlayWanted = false;
        }
    }

    /// <summary>
    /// The 320 bytes the preview chip should hear: the session's own payload with every
    /// <em>inaudible</em> channel silenced. A copy, always — the session's bytes are the
    /// cartridge and are never touched by a listening decision (see the type note).
    ///
    /// <para>The silencing goes through <see cref="MusicPatternList.WritePatternChannel"/> rather than
    /// through a zeroed byte, for the same reason every writer in
    /// <see cref="MusicEditorSession"/> does: that method is the one hand that can spell a silent
    /// channel, so a muted payload is as canonical as the one on disk and the chip cannot be
    /// handed a byte the loader would refuse.</para>
    /// </summary>
    public byte[] AudiblePayload(MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        byte[] copy = session.Payload.ToArray();
        for (int channel = 0; channel < MusicEditorSession.ChannelCount; channel++)
        {
            if (ChannelAudible(channel))
            {
                continue;
            }
            for (int pattern = 0; pattern < MusicEditorSession.PatternCount; pattern++)
            {
                MusicPatternList.WritePatternChannel(copy, pattern, channel, MusicEditorSession.SilentSlot);
            }
        }
        return copy;
    }

    // ---- the exit ----

    /// <summary>
    /// Escape, or the exit tab. The exact answer table
    /// <see cref="SpriteEditorSession.RequestClose"/>, <see cref="MapEditorView.RequestClose"/>,
    /// <see cref="CodeEditorView.RequestClose"/> and <see cref="SfxEditorView.RequestClose"/> use:
    /// a prompt already up comes down ("stay"), a dirty bank raises it, a clean one lets the shell
    /// leave. The gesture is closed first so an Esc mid-drag judges the bank as it stands rather
    /// than half-way through one.
    /// </summary>
    /// <returns>True when the caller may leave this screen.</returns>
    public bool RequestClose(MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EndStroke();
        EndMark();
        CancelDigit();
        if (ExitPromptShown)
        {
            ExitPromptShown = false;
            return false;
        }
        if (session.IsDirty)
        {
            ExitPromptShown = true;
            return false;
        }
        return true;
    }

    /// <summary>Lowers the prompt after Z or X have been executed — the mode machine's half of the verb.</summary>
    public void CloseExitPrompt() => ExitPromptShown = false;

    private static int Validate(int channel)
    {
        if (channel is < 0 or >= MusicEditorSession.ChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel,
                $"a pattern holds channels 0-{MusicEditorSession.ChannelCount - 1} (SPEC-8 §4).");
        }
        return channel;
    }
}
