using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Who owns the fact "this block of the pattern list was copied" — the music editor's answer to
/// <see cref="IMapClipboard"/>, and it exists for the same reason: one seam, one implementation
/// today (<see cref="MusicMemoryClipboard"/>, bytes inside the shell, the machine's clipboard
/// untouched), and the day the OS clipboard question is settled for the sprite and map editors
/// together, an implementation swaps in behind this interface and not one caller changes.
///
/// <para><b>A cell is an <c>int</c>, not a byte, and that is the point.</b> A map cell is a byte
/// of <c>map.bin</c>, so the map's clipboard carries bytes. A music cell is "which SFX slot, or
/// silence", and silence is spelled <c>-1</c> — the spelling
/// <see cref="AudioFormat.PatternChannel"/> already hands out. Carrying the packed channel byte
/// instead would put the bit layout of <c>music.bin</c> (active bit, slot mask, and the rule
/// that a silent channel stores <c>0x00</c>) into a second file; carrying the slot and the
/// silence keeps the format where it belongs and the clipboard readable in a debugger.</para>
/// </summary>
public interface IMusicClipboard
{
    /// <summary>True when something has been copied and a paste has data to place.</summary>
    bool HasBlock { get; }

    /// <summary>Height of the copied block in patterns (tracker rows); 0 when nothing was copied.</summary>
    int Patterns { get; }

    /// <summary>Width of the copied block in channels.</summary>
    int Channels { get; }

    /// <summary>The copied cells, row-major, <see cref="Channels"/> per row; each is a slot 0-63 or -1 for silence.</summary>
    ReadOnlySpan<int> Cells { get; }

    /// <summary>Replace the contents. A width or height below one clears the clipboard rather than storing an empty block.</summary>
    void Write(int patterns, int channels, ReadOnlySpan<int> cells);
}

/// <summary>
/// The internal clipboard: a copy of the cells, living as long as its owner does. Hung on the
/// session here rather than on the screen — the wave that builds the screen may move it, and the
/// interface is what makes that a one-line change.
/// </summary>
public sealed class MusicMemoryClipboard : IMusicClipboard
{
    private int[] _cells = Array.Empty<int>();

    public bool HasBlock => _cells.Length > 0;

    public int Patterns { get; private set; }

    public int Channels { get; private set; }

    public ReadOnlySpan<int> Cells => _cells;

    public void Write(int patterns, int channels, ReadOnlySpan<int> cells)
    {
        if (patterns < 1 || channels < 1 || cells.Length < patterns * channels)
        {
            _cells = Array.Empty<int>();
            Patterns = 0;
            Channels = 0;
            return;
        }
        _cells = cells[..(patterns * channels)].ToArray();
        Patterns = patterns;
        Channels = channels;
    }
}

/// <summary>
/// The music editing session of one cartridge <b>folder</b> — the headless model behind the MUSIC
/// tab, and the fifth and last member of the family <see cref="SpriteEditorSession"/>,
/// <see cref="MapEditorSession"/>, <see cref="CodeEditorSession"/> and
/// <see cref="SfxEditorSession"/> built. It owns the one payload the console's sequencer reads,
/// <c>music.bin</c>, and nothing else: no window, no renderer, no mode, no speaker, no key
/// bindings. The contract is its neighbour's, repeated deliberately — dirty-against-disk, a save
/// that writes only what changed, a clean session that writes nothing at all, an absent file that
/// is silence rather than an error, and a payload whose length is checked on the way in and again
/// on the way out.
///
/// <para><b>What a "tracker row" is here, because the format decides and not the reference.</b>
/// <c>music.bin</c> is 64 patterns x 4 channels plus 64 flag bytes (AUDIO-FORMAT §4). A channel
/// byte holds a <b>reference to an SFX slot</b> and an active bit — six bits and one — and that
/// is the whole of what a pattern can say. So the grid this session owns is 64 rows by 4 columns,
/// a row is a pattern, and a cell is "slot 0-63, or silent". There is <b>no note</b> in this
/// bank, no octave, no volume and no effect: those live one file over, in <c>sfx.bin</c> and
/// <see cref="SfxEditorSession"/>. That makes this editor PICO-8's pattern navigator rather than
/// TIC-80's note tracker (REFERENCES-EDITORS §6.3 against §6.1) — not a simplification, a
/// consequence: AUDIO-FORMAT §10 item 3 states outright that "nothing third can be put into 4
/// channels without inventing entities beyond the specification".</para>
///
/// <para><b>Hence: how many columns inside a channel.</b> One — <see cref="ColumnsPerChannel"/>.
/// TIC-80's tracker spends eight columns on a cell (note, semitone, octave, two SFX digits, a
/// command and two parameters); profile 8 spends one byte on it, of which one bit is the active
/// flag. The cursor is therefore a pattern and a channel and nothing else, and the constant is
/// here so that the screen asks rather than assumes.</para>
///
/// <para><b>The writer is not an encoder.</b> The bit layout of a channel byte, the flag bits,
/// the offsets, the magic, the version and every rule the bank has to obey belong to
/// <see cref="AudioFormat"/> in <c>Quarp.CartKit</c> (AUDIO-FORMAT §4, §5) — the one owner. This
/// class holds 320 bytes and calls into that owner for every read of a cell, every write of a
/// cell, the parse on load and the wrap on save. In particular it never computes a byte offset:
/// "the channel byte of pattern P lives at <c>4P + C</c>" is a sentence that exists once, in
/// <see cref="AudioFormat.WritePatternChannel"/>, and the change detection below is built on the
/// accessors instead of on arithmetic so that it stays that way.</para>
///
/// <para><b>What this file does NOT own, and why each one is somebody else's.</b>
/// <list type="bullet">
///   <item><b>Pattern length.</b> There is no length field in <c>music.bin</c>: how long a
///     pattern lasts is derived by the APU from the longest active slot, floored at
///     <c>Apu.MinPatternTicks</c> (AUDIO-FORMAT §10). The number lives in <c>sfx.bin</c> as
///     <see cref="SfxEditorSession.SlotLength"/>, and a second setter here would be a second
///     owner of it.</item>
///   <item><b>Mute and solo.</b> Every bit of both tables is spoken for and the spare ones must
///     be zero (bit 7 of a channel byte, bits 3-7 of a flag byte), so mute and solo have nowhere
///     on disk to live — and that is the right answer rather than a limitation. They are an
///     audition control: muting a channel to hear the bass must not change one byte of the
///     cartridge, because those bytes are its identity and every replay recorded against it
///     (REPLAY-FORMAT §5). They belong to the screen, and the screen's wave will hold them.</item>
///   <item><b>The piano rows.</b> <c>zsxdcvgbhnjm</c> / <c>q2w3er5t6y7ui</c> turn a key into a
///     semitone, and a semitone is a field this bank does not have. Nothing here calls
///     <see cref="SfxEditorView.NoteOfPianoKey"/>, and nothing here could: it is layer 2 and
///     reads that view's octave.</item>
/// </list></para>
///
/// <para><b>A cart with music.txt has a read-only bank</b>, the verdict <c>map.csv</c> set and
/// <c>sfx.txt</c> repeated (see <see cref="SfxEditorSession.BankReadOnly"/> for the argument in
/// full): while the text source lies in the folder, the owner of "this cartridge's song" is the
/// <c>quarp audio build</c> path, and a dirty write here would silently stale it. The refusal is
/// at the door rather than at save time, so the author is told before they start typing. Note
/// which cart that covers: snake ships a <c>music.txt</c>, so its song is read-only inside Quarp
/// and the pinned golden gains a second lock.</para>
///
/// <para><b>Undo is one stack over the whole payload, and a step is an operation.</b> A snapshot
/// is 320 bytes — one fourteenth of the sound editor's, which is itself a quarter of a map
/// entry's — so there is no delta encoding and no per-pattern bookkeeping to get wrong. Whole-
/// payload is also the only granularity under which the row operations are honestly <em>one</em>
/// action: inserting a row rewrites up to 63 rows and their flags, and an author who presses
/// Ctrl+Z after it expects the row back, not one shifted cell back. A pointer gesture is likewise
/// one step however many cells it crossed (<see cref="BeginStroke"/>/<see cref="EndStroke"/>);
/// a verb that arrives outside a gesture opens and closes its own, so every public verb below
/// costs exactly one Ctrl+Z whichever hand performed it.</para>
///
/// <para><b>The payload is canonical at every instant, not merely at save time</b> — the same
/// promise the sound bank makes. AUDIO-FORMAT §4 gives a silent channel exactly one spelling,
/// <c>0x00</c>, and forbids reserved bits; the mutators here go through
/// <see cref="AudioFormat.WritePatternChannel"/>, which cannot spell it any other way, and
/// through a flag setter that masks. So these 320 bytes can be handed to a live sequencer for
/// the preview on any frame, and a bank that was only legal at save time would be a bank the
/// author cannot hear.</para>
/// </summary>
public sealed class MusicEditorSession
{
    /// <summary>The binary the console reads. One name owner: the constructor reads it, <see cref="Save"/> writes it, tests point at it.</summary>
    public const string MusicFileName = "music.bin";

    /// <summary>The authoring text source whose presence makes the bank read-only (AUDIO-FORMAT §6).</summary>
    public const string MusicSourceFileName = "music.txt";

    /// <summary>Patterns in the song — 64 tracker rows, from the one owner of the format.</summary>
    public const int PatternCount = AudioFormat.MusicPatternCount;

    /// <summary>Channels in a pattern — 4.</summary>
    public const int ChannelCount = AudioFormat.MusicChannelCount;

    /// <summary>
    /// Editable columns inside one channel: <b>one</b>, the SFX slot reference. See the type note
    /// — the byte has six bits of slot and one active bit, and there is no second field to put a
    /// cursor on.
    /// </summary>
    public const int ColumnsPerChannel = 1;

    /// <summary>SFX slots a channel may point at — 64, borrowed from the effects bank's geometry rather than from the 6-bit mask.</summary>
    public const int SlotCount = AudioFormat.SfxSlotCount;

    /// <summary>Highest slot number a channel can name: 63.</summary>
    public const int MaxSlot = SlotCount - 1;

    /// <summary>Exactly 320 bytes (AUDIO-FORMAT §4), borrowed rather than re-derived.</summary>
    public const int PayloadSize = AudioFormat.MusicPayloadSize;

    /// <summary>Playback returns here when it meets <see cref="FlagLoopEnd"/>.</summary>
    public const byte FlagLoopStart = AudioFormat.PatternFlagLoopStart;

    /// <summary>End of a section: jump back to the nearest <see cref="FlagLoopStart"/>.</summary>
    public const byte FlagLoopEnd = AudioFormat.PatternFlagLoopEnd;

    /// <summary>The song ends after this pattern.</summary>
    public const byte FlagStop = AudioFormat.PatternFlagStop;

    /// <summary>Every flag bit that has a meaning; bits 3-7 are reserved and must stay zero.</summary>
    public const byte FlagMask = AudioFormat.PatternFlagMask;

    /// <summary>The value a cell holds when its channel is silent — the spelling <see cref="AudioFormat.PatternChannel"/> hands out.</summary>
    public const int SilentSlot = -1;

    private readonly string _musicPath;

    /// <summary>What the disk holds: the dirty comparison's baseline, replaced on save. Never aliases <see cref="_bank"/>.</summary>
    private byte[] _savedBank;

    // The live bank. Mutated in place by the verbs and replaced wholesale by undo/redo, so
    // nothing may cache a reference to it across a step — every access goes through the field.
    private byte[] _bank;

    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();

    /// <summary>Pre-gesture bank while a pointer gesture is open; null between gestures.</summary>
    private byte[]? _strokeBackup;
    private bool _strokeChanged;

    /// <summary>
    /// Opens the song of a cartridge folder (.quarp8 files never get here — the mode machine
    /// refuses them with the read-only line, exactly as for the other four screens). The file is
    /// optional: absent means 64 patterns with no active channel and a clean session. A file that
    /// is not a bank, is the wrong length, or breaks any rule of AUDIO-FORMAT §5 is refused here
    /// by <see cref="AudioFormat.ParseMusicFile"/> with <see cref="CartLoadException"/> — the very
    /// same failure and the very same wording <see cref="CartSource"/> produces for the same file.
    /// </summary>
    public MusicEditorSession(string cartFolder)
        : this(cartFolder, new MusicMemoryClipboard())
    {
    }

    /// <summary>The same, with the clipboard handed in — the seam <see cref="IMusicClipboard"/> exists for.</summary>
    public MusicEditorSession(string cartFolder, IMusicClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        ArgumentNullException.ThrowIfNull(clipboard);
        Clipboard = clipboard;
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _musicPath = Path.Combine(cartFolder, MusicFileName);
        BankReadOnly = File.Exists(Path.Combine(cartFolder, MusicSourceFileName));
        _savedBank = ReadPayload(_musicPath);
        _bank = (byte[])_savedBank.Clone();
    }

    /// <summary>Folder name, for the header — the manifest is deliberately not read, same call as its four siblings.</summary>
    public string CartName { get; }

    /// <summary>Where a copied block lives; see <see cref="IMusicClipboard"/> for why it is a seam and not three fields.</summary>
    public IMusicClipboard Clipboard { get; }

    /// <summary>
    /// True when <c>music.txt</c> lies beside the bank: the song is then read-only and the screen
    /// must say so before the author touches a cell. Observable on purpose — a surprise at save
    /// time is the thing this property exists to prevent.
    /// </summary>
    public bool BankReadOnly { get; }

    /// <summary>
    /// The live payload, 320 bytes — what the grid draws, what a preview sequencer loads, and
    /// what <see cref="Save"/> wraps into <c>music.bin</c>. Canonical at every instant (see the
    /// type note).
    /// </summary>
    public ReadOnlySpan<byte> Payload => _bank;

    /// <summary>True while a pointer gesture is open — the current undo step is still growing.</summary>
    public bool StrokeActive => _strokeBackup is not null;

    /// <summary>True when the live bank differs from what the disk holds.</summary>
    public bool IsDirty => !_bank.AsSpan().SequenceEqual(_savedBank);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Bumped on every change to the <b>bank</b> (edit, undo, redo) so a renderer or a preview can
    /// notice. Deliberately not bumped by the cursor or the selection: those are where the author
    /// is standing, not what the cartridge holds, and a preview that reloaded on every arrow key
    /// would stutter for nothing.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Why the last save failed, or null. A save the author believes happened but did not is data loss, so it has to be sayable.</summary>
    public string? SaveError { get; private set; }

    // ---- the cursor: a pattern and a channel, because that is all a cell has ----

    /// <summary>The tracker row the keyboard acts on, 0-63.</summary>
    public int CursorPattern { get; private set; }

    /// <summary>The channel column the keyboard acts on, 0-3.</summary>
    public int CursorChannel { get; private set; }

    /// <summary>Puts the cursor somewhere; out-of-grid coordinates are clamped, because a key repeat at the edge is not a caller bug.</summary>
    public void SetCursor(int pattern, int channel)
    {
        CursorPattern = Math.Clamp(pattern, 0, PatternCount - 1);
        CursorChannel = Math.Clamp(channel, 0, ChannelCount - 1);
    }

    /// <summary>Arrow keys: a relative move, clamped at the grid's edges (the tracker does not wrap — a song has a first bar and a last).</summary>
    public void MoveCursor(int patterns, int channels) =>
        SetCursor(CursorPattern + patterns, CursorChannel + channels);

    // ---- reads: every one of them through the format's owner ----

    /// <summary>The SFX slot a channel plays in a pattern, or <see cref="SilentSlot"/> when the channel is off.</summary>
    public int ChannelSlot(int pattern, int channel)
    {
        ValidatePattern(pattern);
        ValidateChannel(channel);
        return AudioFormat.PatternChannel(_bank, pattern, channel);
    }

    /// <summary>True when the channel says nothing in this pattern — the one spelling of which is the zero byte.</summary>
    public bool ChannelIsSilent(int pattern, int channel) => ChannelSlot(pattern, channel) < 0;

    /// <summary>
    /// True when no channel plays in this pattern. That is a <b>bar of rest</b>, not the end of
    /// the song (AUDIO-FORMAT §4): the sequencer holds it and moves on, and what ends a song is
    /// <see cref="FlagStop"/> or running off pattern 63.
    /// </summary>
    public bool PatternIsEmpty(int pattern)
    {
        ValidatePattern(pattern);
        return AudioFormat.PatternIsEmpty(_bank, pattern);
    }

    /// <summary>The section flags of a pattern: loop start, loop end, stop.</summary>
    public byte PatternFlags(int pattern)
    {
        ValidatePattern(pattern);
        return AudioFormat.PatternFlags(_bank, pattern);
    }

    /// <summary>True when the pattern carries this flag; <paramref name="flag"/> is one of the three constants.</summary>
    public bool HasFlag(int pattern, byte flag) => (PatternFlags(pattern) & flag) != 0;

    // ---- gestures ----

    /// <summary>
    /// A pointer went down on the grid. The pre-gesture bank is snapshotted here and becomes the
    /// undo entry when the gesture ends — the whole "one drag = one step" mechanism, borrowed
    /// unchanged from <see cref="MapEditorSession.BeginStroke"/>.
    /// </summary>
    public void BeginStroke()
    {
        if (StrokeActive)
        {
            return;     // a second press without a release (focus-loss glitches) folds into the open gesture
        }
        _strokeBackup = (byte[])_bank.Clone();
        _strokeChanged = false;
    }

    /// <summary>
    /// The pointer came up: the gesture commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z look dead). Safe to call without an open gesture.
    /// </summary>
    public void EndStroke()
    {
        if (_strokeBackup is not byte[] backup)
        {
            return;
        }
        _strokeBackup = null;
        if (!_strokeChanged)
        {
            return;
        }
        _undo.Add(backup);
        _redo.Clear();      // the redone future described a song that no longer exists
    }

    // ---- writing one cell ----

    /// <summary>
    /// Points a channel at an SFX slot, or silences it with <see cref="SilentSlot"/>. The
    /// canonicity rule of AUDIO-FORMAT §4 — "a silent channel stores <c>0x00</c>", never a
    /// remembered slot with the active bit clear — is not enforced here but <em>unspellable</em>:
    /// <see cref="AudioFormat.WritePatternChannel"/> is the only writer and it has no way to
    /// produce the illegal byte.
    ///
    /// <para>A slot outside 0-63 throws rather than clamps. Pointing a channel at slot 64 is a
    /// caller bug, and silently substituting 63 would play a sound the author did not write.
    /// Pointing at an <em>empty</em> slot, on the other hand, is perfectly legal and means
    /// silence in that channel: the two banks are edited independently and AUDIO-FORMAT §4
    /// refuses to cross-check them on purpose.</para>
    /// </summary>
    public void SetChannelSlot(int pattern, int channel, int slot)
    {
        ValidatePattern(pattern);
        ValidateChannel(channel);
        ValidateSlot(slot);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        WriteChannel(pattern, channel, slot);
        CloseOwnStroke(own);
    }

    /// <summary>Silences one cell — Del's verb, and the zero byte is the only spelling it has.</summary>
    public void ClearChannel(int pattern, int channel)
    {
        ValidatePattern(pattern);
        ValidateChannel(channel);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        WriteChannel(pattern, channel, SilentSlot);
        CloseOwnStroke(own);
    }

    /// <summary>
    /// Typing a slot number at the cursor: the cell takes it and the cursor steps to the next
    /// pattern — the tracker gesture both references share (TIC-80's <c>processTrackerKeyboard</c>
    /// and PICO-8's pattern navigator), and the one <see cref="SfxEditorView.PlayPianoKey"/>
    /// already performs on the sound screen. The step is what makes entering a bass line a run of
    /// key presses instead of a run of key presses and arrow keys.
    /// </summary>
    public void EnterSlot(int slot)
    {
        SetChannelSlot(CursorPattern, CursorChannel, slot);
        MoveCursor(1, 0);
    }

    /// <summary>Entering silence at the cursor — <see cref="EnterSlot"/>'s twin, so both hands leave the cursor in the same place.</summary>
    public void EnterRest()
    {
        ClearChannel(CursorPattern, CursorChannel);
        MoveCursor(1, 0);
    }

    // ---- playback order: the three flags, which is all "order" means in this format ----

    /// <summary>
    /// Sets a pattern's section flags — the whole of what this format calls playback order,
    /// together with the plain 0..63 march of the patterns themselves. Reserved bits are refused
    /// rather than masked away: a caller that passed 0x08 meant something, and quietly dropping
    /// it would hide the bug until the loader found it.
    /// </summary>
    public void SetPatternFlags(int pattern, byte flags)
    {
        ValidatePattern(pattern);
        if ((flags & ~FlagMask) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flags), flags,
                $"pattern flag bits 3-7 are reserved and must be 0 (AUDIO-FORMAT §4); got 0x{flags:x2}.");
        }
        RequireWritableBank();
        bool own = OpenOwnStroke();
        WriteFlags(pattern, flags);
        CloseOwnStroke(own);
    }

    /// <summary>Flips one flag of a pattern — what a click on a section button means.</summary>
    public void TogglePatternFlag(int pattern, byte flag)
    {
        if (flag != FlagLoopStart && flag != FlagLoopEnd && flag != FlagStop)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flag), flag, "a pattern flag is loop-start, loop-end or stop (AUDIO-FORMAT §4).");
        }
        SetPatternFlags(pattern, (byte)(PatternFlags(pattern) ^ flag));
    }

    // ---- rows: the two insert/delete pairs, and why there are two ----

    /// <summary>
    /// Inserts a bar of rest at <paramref name="pattern"/>: every row below moves down one,
    /// <b>flags and all</b>, and what stood on row 63 falls off the end of the song. This is the
    /// structural edit — an intro bar, a break — and it moves the flags because a section marker
    /// belongs to its bar and not to its ordinal.
    ///
    /// <para>The last row is dropped rather than the song refusing to grow: the bank is a fixed
    /// 64 rows (AUDIO-FORMAT §1), so something has to give, and an author who inserts near the
    /// end can see what leaves. Ctrl+Z brings it back — that is what one snapshot per operation
    /// is for.</para>
    /// </summary>
    public void InsertPatternRow(int pattern)
    {
        ValidatePattern(pattern);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        for (int row = PatternCount - 1; row > pattern; row--)
        {
            CopyRow(row - 1, row);
        }
        ClearRow(pattern);
        CloseOwnStroke(own);
    }

    /// <summary>Removes a bar: every row below moves up one, flags and all, and row 63 becomes an empty bar of rest.</summary>
    public void DeletePatternRow(int pattern)
    {
        ValidatePattern(pattern);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        for (int row = pattern; row < PatternCount - 1; row++)
        {
            CopyRow(row + 1, row);
        }
        ClearRow(PatternCount - 1);
        CloseOwnStroke(own);
    }

    /// <summary>
    /// Inserts a rest into <b>one channel</b> at <paramref name="pattern"/>: that column shifts
    /// down by one and its last cell falls off; the other three channels and every flag byte are
    /// not touched. This is the musical edit — the lead comes in a bar later, the bass keeps its
    /// place — and it is TIC-80's reading of Insert (its <c>Delete</c>/<c>Insert</c> work inside
    /// the current channel), where <see cref="InsertPatternRow"/> is PICO-8's reading of the
    /// pattern navigator. Both exist because they are different operations, not two spellings of
    /// one: the row pair changes the shape of the song, the cell pair changes one voice inside it.
    /// </summary>
    public void InsertChannelCell(int pattern, int channel)
    {
        ValidatePattern(pattern);
        ValidateChannel(channel);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        for (int row = PatternCount - 1; row > pattern; row--)
        {
            WriteChannel(row, channel, AudioFormat.PatternChannel(_bank, row - 1, channel));
        }
        WriteChannel(pattern, channel, SilentSlot);
        CloseOwnStroke(own);
    }

    /// <summary>Removes one cell from one channel: that column shifts up by one and its row-63 cell falls silent. Neighbours and flags stand still.</summary>
    public void DeleteChannelCell(int pattern, int channel)
    {
        ValidatePattern(pattern);
        ValidateChannel(channel);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        for (int row = pattern; row < PatternCount - 1; row++)
        {
            WriteChannel(row, channel, AudioFormat.PatternChannel(_bank, row + 1, channel));
        }
        WriteChannel(PatternCount - 1, channel, SilentSlot);
        CloseOwnStroke(own);
    }

    // ---- the marked rectangle ----

    /// <summary>True when a rectangle of the grid is marked.</summary>
    public bool HasSelection { get; private set; }

    /// <summary>Top row of the marked rectangle. Meaningless while <see cref="HasSelection"/> is false.</summary>
    public int SelectionPattern { get; private set; }

    /// <summary>Left channel of the marked rectangle.</summary>
    public int SelectionChannel { get; private set; }

    /// <summary>Height of the marked rectangle in patterns.</summary>
    public int SelectionPatterns { get; private set; }

    /// <summary>Width of the marked rectangle in channels.</summary>
    public int SelectionChannels { get; private set; }

    /// <summary>
    /// Marks a rectangle by its two corners, in either order — the shape a shift-drag and a
    /// shift-arrow both produce. The anchor itself stays with whoever is holding the mouse or the
    /// shift key: this session owns <em>what is marked</em>, not the gesture that marked it, the
    /// same split <see cref="MapEditorView"/> keeps on the map.
    /// </summary>
    public void SelectRange(int anchorPattern, int anchorChannel, int pattern, int channel)
    {
        int p0 = Math.Clamp(anchorPattern, 0, PatternCount - 1);
        int p1 = Math.Clamp(pattern, 0, PatternCount - 1);
        int c0 = Math.Clamp(anchorChannel, 0, ChannelCount - 1);
        int c1 = Math.Clamp(channel, 0, ChannelCount - 1);
        SelectionPattern = Math.Min(p0, p1);
        SelectionChannel = Math.Min(c0, c1);
        SelectionPatterns = Math.Abs(p1 - p0) + 1;
        SelectionChannels = Math.Abs(c1 - c0) + 1;
        HasSelection = true;
    }

    /// <summary>Ctrl+A: the whole song, all four channels — TIC-80's <c>selectAll</c> widened from one channel to the grid.</summary>
    public void SelectAll() => SelectRange(0, 0, PatternCount - 1, ChannelCount - 1);

    /// <summary>Forgets the marking. Touches no bytes, so it is not an undo step.</summary>
    public void ClearSelection()
    {
        HasSelection = false;
        SelectionPatterns = 0;
        SelectionChannels = 0;
    }

    /// <summary>
    /// Del over the marked rectangle: every cell in it falls silent, as one undo step. An empty
    /// marking is a no-op rather than a throw — the caller is a key press that may arrive with
    /// nothing marked.
    /// </summary>
    public void ClearSelectedCells()
    {
        if (!HasSelection)
        {
            return;
        }
        RequireWritableBank();
        EndStroke();
        BeginStroke();
        for (int row = 0; row < SelectionPatterns; row++)
        {
            for (int column = 0; column < SelectionChannels; column++)
            {
                WriteChannel(SelectionPattern + row, SelectionChannel + column, SilentSlot);
            }
        }
        EndStroke();
    }

    /// <summary>
    /// Moves every <b>sounding</b> cell of the marking by <paramref name="delta"/> slots, clamped
    /// into 0..63. This is the pattern list's answer to a tracker's transpose, and the shape is
    /// deliberately the same: TIC-80 transposes with <c>Ctrl</c>+wheel over a selection and
    /// clamps at the ends of the range, and its <c>Ctrl+Up/Down</c> steps the SFX number of the
    /// cell under the cursor. What it is <em>not</em> is a transpose: there is no note in this
    /// bank to raise (see the type note), so the number that moves is the slot reference, and the
    /// method is named for what it does.
    ///
    /// <para><b>Silent cells stay silent</b> — the exact counterpart of a transpose leaving rests
    /// alone. A silent cell is the zero byte and has no slot to move; giving it one would make an
    /// inaudible edit change the bytes of the cartridge, which is the rule AUDIO-FORMAT §4 exists
    /// to keep.</para>
    ///
    /// <para>Clamping is per cell rather than "shift the block until one cell hits the wall": an
    /// author dragging a block up expects the block to arrive, and a slot number is a reference
    /// rather than a pitch, so the intervals between cells were never a fact worth preserving.</para>
    /// </summary>
    public void ShiftSelectionSlots(int delta)
    {
        if (!HasSelection || delta == 0)
        {
            return;
        }
        RequireWritableBank();
        EndStroke();
        BeginStroke();
        for (int row = 0; row < SelectionPatterns; row++)
        {
            for (int column = 0; column < SelectionChannels; column++)
            {
                int pattern = SelectionPattern + row;
                int channel = SelectionChannel + column;
                int slot = AudioFormat.PatternChannel(_bank, pattern, channel);
                if (slot < 0)
                {
                    continue;
                }
                WriteChannel(pattern, channel, Math.Clamp(slot + delta, 0, MaxSlot));
            }
        }
        EndStroke();
    }

    // ---- copy, cut, paste ----

    /// <summary>
    /// Copies the marked rectangle into <see cref="Clipboard"/>. <b>Flags do not travel.</b> A
    /// flag belongs to a whole bar, not to a channel, so a two-channel block has no flags to
    /// speak of — and making them travel only for full-width blocks would be a rule with a mode
    /// in it, which is exactly the kind of rule an author cannot predict.
    /// </summary>
    /// <returns>True when something was copied.</returns>
    public bool CopySelection()
    {
        if (!HasSelection)
        {
            return false;
        }
        int patterns = SelectionPatterns;
        int channels = SelectionChannels;
        int[] cells = new int[patterns * channels];
        for (int row = 0; row < patterns; row++)
        {
            for (int column = 0; column < channels; column++)
            {
                cells[row * channels + column] =
                    AudioFormat.PatternChannel(_bank, SelectionPattern + row, SelectionChannel + column);
            }
        }
        Clipboard.Write(patterns, channels, cells);
        return true;
    }

    /// <summary>
    /// Copy, then silence what was copied — one undo step for the emptying, as
    /// <see cref="ClearSelectedCells"/> makes it. The read-only check comes <b>first</b>, before
    /// the clipboard is touched: a cut that cannot cut must not half-happen by leaving the block
    /// on the clipboard. Plain <see cref="CopySelection"/> stays legal on a read-only song —
    /// reading a bank the text source owns takes nothing away from it.
    /// </summary>
    public void CutSelection()
    {
        RequireWritableBank();
        if (!CopySelection())
        {
            return;
        }
        ClearSelectedCells();
    }

    /// <summary>
    /// Lands the clipboard's block with its top-left cell at (<paramref name="pattern"/>,
    /// <paramref name="channel"/>), as one undo step. Cells that fall off the grid are simply not
    /// written, rather than the paste being refused or the block snapped back inside: the target
    /// comes from a cursor that may stand one row from the end while the block is eight rows
    /// tall, and both alternatives put cells where the author did not point. Same verdict, same
    /// reasoning as <see cref="MapEditorSession.PasteBlock"/>.
    /// </summary>
    /// <returns>True when the clipboard had a block to place.</returns>
    public bool PasteAt(int pattern, int channel)
    {
        if (!Clipboard.HasBlock)
        {
            return false;
        }
        RequireWritableBank();
        int patterns = Clipboard.Patterns;
        int channels = Clipboard.Channels;
        ReadOnlySpan<int> cells = Clipboard.Cells;
        EndStroke();
        BeginStroke();
        for (int row = 0; row < patterns; row++)
        {
            int target = pattern + row;
            if (target is < 0 or >= PatternCount)
            {
                continue;
            }
            for (int column = 0; column < channels; column++)
            {
                int targetChannel = channel + column;
                if (targetChannel is < 0 or >= ChannelCount)
                {
                    continue;
                }
                int slot = cells[row * channels + column];
                WriteChannel(target, targetChannel, slot < 0 ? SilentSlot : Math.Clamp(slot, 0, MaxSlot));
            }
        }
        EndStroke();
        return true;
    }

    // ---- undo / redo / save ----

    /// <summary>
    /// Ctrl+Z. Ends an open gesture first (committing it), so an undo mid-drag rolls back a whole
    /// gesture instead of tearing one in half. Whole-payload swaps, no copying. History lives in
    /// the session only: closing the tab forgets it, and a fresh session opens with Ctrl+Z
    /// honestly dead.
    /// </summary>
    public void Undo()
    {
        EndStroke();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(_bank);
        byte[] previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _bank = previous;
        Version++;
    }

    /// <summary>Ctrl+Y — the exact mirror of <see cref="Undo"/>.</summary>
    public void Redo()
    {
        EndStroke();
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(_bank);
        byte[] next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _bank = next;
        Version++;
    }

    /// <summary>
    /// Ctrl+S. The clean guard is the save contract's heart: a session whose bank equals the disk
    /// writes <b>nothing</b> — open-and-close leaves the file untouched and, for a cart that never
    /// had one, uncreated, and a repeated Ctrl+S is a no-op. That is what keeps the pinned demo
    /// banks byte-identical after the editor has opened them, and the read-only rule is the second
    /// lock on the same door.
    ///
    /// <para>The bytes go out through <see cref="AudioFormat.WriteMusicFile"/>, which re-validates
    /// the whole payload — length first — before it prepends a header. So the length is checked on
    /// the way in (<see cref="AudioFormat.ParseMusicFile"/>) and again on the way out, by the one
    /// owner, and this class cannot write a bank it could not read back.</para>
    ///
    /// <para>Disk failures land in <see cref="SaveError"/> instead of throwing, because a full
    /// disk must leave the author their work and a message. A read-only bank that is somehow
    /// dirty is a contract violation rather than an accident: that throws.</para>
    /// </summary>
    /// <returns>True when the disk now matches the bank (including "already did"), false when a write failed.</returns>
    public bool Save()
    {
        EndStroke();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        if (BankReadOnly)
        {
            // Unreachable while RequireWritableBank guards every mutator — that door slams
            // first. Kept as the second gate, because "music.bin is owned by music.txt" is a
            // save-time promise and the next wave's screen will be a new writer.
            throw new InvalidOperationException(
                $"{CartName}: {MusicFileName} is read-only while {MusicSourceFileName} is present — "
                + $"the text source owns the bank. Remove {MusicSourceFileName} to edit the song inside Quarp.");
        }
        try
        {
            byte[] file = AudioFormat.WriteMusicFile(_bank);
            File.WriteAllBytes(_musicPath, file);
            _savedBank = (byte[])_bank.Clone();
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    // ---- the private half ----

    /// <summary>Absent file = the zero bank (AUDIO-FORMAT §1); present file = its payload, validated by the format's owner.</summary>
    private static byte[] ReadPayload(string path)
    {
        if (!File.Exists(path))
        {
            return AudioFormat.EmptyMusicPayload();
        }
        return AudioFormat.ParseMusicFile(File.ReadAllBytes(path), MusicFileName);
    }

    /// <summary>A verb that arrived outside a pointer gesture gets a gesture of its own, so it costs exactly one Ctrl+Z.</summary>
    private bool OpenOwnStroke()
    {
        if (StrokeActive)
        {
            return false;
        }
        BeginStroke();
        return true;
    }

    private void CloseOwnStroke(bool own)
    {
        if (own)
        {
            EndStroke();
        }
    }

    /// <summary>
    /// The one hand that writes a channel cell. Every verb goes through here, so "the song
    /// changed" means exactly one thing: the dirt, the <see cref="Version"/> a preview watches and
    /// the gesture's changed-flag move together or not at all. Writing the value that is already
    /// there is not a change, which is what keeps an idle click out of the undo stack.
    ///
    /// <para>Note what this method does <b>not</b> contain: an offset. The comparison reads
    /// through <see cref="AudioFormat.PatternChannel"/> and the write goes through
    /// <see cref="AudioFormat.WritePatternChannel"/>, so <c>4P + C</c> is spelled once in the
    /// tree and this file cannot drift from it.</para>
    /// </summary>
    private void WriteChannel(int pattern, int channel, int slot)
    {
        int normalized = slot < 0 ? SilentSlot : slot;
        if (AudioFormat.PatternChannel(_bank, pattern, channel) == normalized)
        {
            return;
        }
        AudioFormat.WritePatternChannel(_bank, pattern, channel, normalized);
        _strokeChanged = true;
        Version++;
    }

    /// <summary>The same hand for a flag byte; the caller has already refused the reserved bits.</summary>
    private void WriteFlags(int pattern, byte flags)
    {
        if (AudioFormat.PatternFlags(_bank, pattern) == flags)
        {
            return;
        }
        AudioFormat.WritePatternFlags(_bank, pattern, flags);
        _strokeChanged = true;
        Version++;
    }

    /// <summary>A whole tracker row — four cells and the flag byte — moved, for the row shifters.</summary>
    private void CopyRow(int from, int to)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            WriteChannel(to, channel, AudioFormat.PatternChannel(_bank, from, channel));
        }
        WriteFlags(to, AudioFormat.PatternFlags(_bank, from));
    }

    /// <summary>An empty bar: four silent channels and no flags.</summary>
    private void ClearRow(int pattern)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            WriteChannel(pattern, channel, SilentSlot);
        }
        WriteFlags(pattern, 0);
    }

    private void RequireWritableBank()
    {
        if (BankReadOnly)
        {
            throw new InvalidOperationException(
                $"{CartName}: the song is read-only while {MusicSourceFileName} is present — "
                + $"the text source owns it (AUDIO-FORMAT §6). Remove {MusicSourceFileName} to edit the music inside Quarp.");
        }
    }

    private static void ValidatePattern(int pattern)
    {
        if (pattern is < 0 or >= PatternCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pattern), pattern, $"the song holds patterns 0-{PatternCount - 1} (SPEC-8 §4).");
        }
    }

    private static void ValidateChannel(int channel)
    {
        if (channel is < 0 or >= ChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel, $"a pattern holds channels 0-{ChannelCount - 1} (SPEC-8 §4).");
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < SilentSlot or > MaxSlot)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot,
                $"a channel names SFX slot 0-{MaxSlot}, or {SilentSlot} for silence (AUDIO-FORMAT §4).");
        }
    }
}
