namespace Quarp.Core.Audio;

/// <summary>
/// The QUARP-8 sound chip: four channels, six waveforms, a step sequencer per channel and a
/// pattern sequencer above them, producing exactly <see cref="AudioBlock.SamplesPerTick"/>
/// samples of 16-bit mono PCM per tick (SPEC-8 §4, ARCHITECTURE §2).
///
/// <para><b>Everything here is integer, and that is the whole point of the milestone.</b>
/// Phase accumulators, envelopes, slides, vibrato, the mix and the clamp are all <c>int</c> or
/// <c>uint</c>; frequencies come from a precomputed table (<see cref="NoteTable"/>). There is
/// no rounding mode to disagree about, no fused multiply-add to appear on one architecture and
/// not the other, no denormal to be flushed on one runtime and kept on the next. The PCM is
/// therefore bit-identical by construction, which is what lets it join the framebuffer in the
/// golden-master hash instead of merely being compared "by ear".</para>
///
/// <para><b>The APU is simulation state.</b> It resets with the console, it advances on every
/// tick whether or not anyone draws that tick, and a rewind reproduces it exactly, because
/// <see cref="VirtualConsole.Tick"/> and <see cref="VirtualConsole.TickUpdateOnly"/> call
/// <see cref="RenderTick"/> from the same place, immediately after <c>Update</c> and before
/// <c>Draw</c> ever runs. There is one render path and no "cheap path for resimulation": a
/// second implementation of the same arithmetic is exactly how two machines start disagreeing.
/// The one shortcut taken — filling the block with zeros when all four channels are idle — is
/// not a second implementation but a provable identity, since an idle channel holds a zero
/// phase and contributes nothing.</para>
///
/// <para><b>Register writes land on tick boundaries.</b> The sequencer moves once per tick and
/// the 800 samples in between are generated from frozen registers, exactly the way hardware
/// behaves between two writes from a game loop. That is why <see cref="SfxSlot.Speed"/> is
/// counted in ticks, and it is what keeps the inner loop free of any branch that depends on
/// time.</para>
/// </summary>
public sealed class Apu
{
    /// <summary>Synthesis channels: 4 (SPEC-8 §4). Not to be confused with output channels — the mix is mono.</summary>
    public const int ChannelCount = 4;

    /// <summary>Loudest step volume.</summary>
    public const int MaxVolume = SfxStep.MaxVolume;

    /// <summary>
    /// Peak amplitude one channel contributes per volume level. Seven levels on four channels
    /// come to 32760, just inside a 16-bit sample, so a full mix cannot clip and the clamp in
    /// <see cref="Render"/> is a guard rather than a part of the sound. A louder single channel
    /// would mean four channels together clipping, and clipping that depends on what else
    /// happened to be playing is the kind of thing players hear as "the music breaks when I
    /// shoot".
    /// </summary>
    public const int VolumeStep = 1170;

    /// <summary>Peak amplitude of one channel at full volume: 8190.</summary>
    public const int PeakAmplitude = MaxVolume * VolumeStep;

    /// <summary>
    /// How long a music pattern lasts when it plays nothing at all: half a second of rest. Also
    /// the floor under every pattern, which is what makes it impossible for the sequencer to
    /// spin through 64 zero-length patterns inside one tick.
    /// </summary>
    public const int MinPatternTicks = 32;

    /// <summary>Ticks between arpeggio note changes: 2, i.e. 30 changes a second.</summary>
    public const int ArpeggioTicksPerNote = 2;

    /// <summary>
    /// Steps an arpeggio cycles over: the aligned group of four the current step falls into.
    /// Four is the tracker convention rather than a number of ours — PICO-8 spells its two
    /// arpeggio effects "iterate over groups of 4 notes", and a chord that changed shape
    /// depending on where in the slot it was written would be a dialect nobody else speaks.
    /// </summary>
    public const int ArpeggioGroup = 4;

    /// <summary>The value <see cref="CurrentPattern"/> holds while music is stopped.</summary>
    public const int NoPattern = -1;

    /// <summary>
    /// The <c>id</c> that silences a channel instead of starting a sound: -1, as in
    /// <c>Sfx(-1, 2)</c> (API-8 §5). Only this one value stops; -2 and below stay silent
    /// no-ops, because PICO-8 spends -2 on "release the sound from its loop" and a console
    /// that answered every negative number the same way could never add that later.
    /// </summary>
    public const int StopSfx = -1;

    /// <summary>
    /// Full music volume in the fade's fixed-point scale: amplitudes of music-driven channels
    /// are multiplied by gain/256 (ADR-037). 256 exactly, so that at full gain the
    /// multiply-and-divide is the identity and a run that never fades renders bit-identical
    /// PCM to a build that never heard of fading — which is what keeps every recorded audio
    /// hash where it is.
    /// </summary>
    public const int GainUnity = 256;

    /// <summary>All four channel bits of a music channel mask; the mask argument is ANDed with this.</summary>
    public const int ChannelMaskAll = (1 << ChannelCount) - 1;

    // Waveforms are generated at +/-32768 and scaled by (wave * amplitude) >> 15.
    private const int WaveShift = 15;

    // Duty thresholds on the 32-bit phase: the fraction of a cycle the pulse spends high.
    private const uint Duty12 = 0x2000_0000u;
    private const uint Duty25 = 0x4000_0000u;
    private const uint Duty50 = 0x8000_0000u;

    // Age is masked with this so it can never overflow. 0x00FFFFFF + 1 is 2^24, a multiple of
    // the vibrato period (8 ticks) and of an arpeggio cycle over four or two sounding steps
    // (8 and 4 ticks), so for those the wrap — after 77 hours of one continuous sound — does
    // not even produce a click. A cycle over three sounding steps has a period of 6 and does
    // step once at the wrap; it is the same step on every machine, and buying seamlessness for
    // that one case would cost a modulo in the tick path for a sound nobody will hold for
    // three days.
    private const int AgeMask = 0x00FF_FFFF;

    /// <summary>
    /// Vibrato depth in 1/256 semitones, one entry per tick: a sine over eight ticks, so
    /// 7.5 Hz, +/- a quarter tone. Eight integers rather than a call into <c>SMath</c> because
    /// the shape is fixed and eight numbers are cheaper to read than to explain.
    /// </summary>
    private static readonly int[] VibratoDepth = { 0, 45, 64, 45, 0, -45, -64, -45 };

    private readonly AudioBank _bank = new();

    /// <summary>
    /// The bank exactly as the cartridge pipeline handed it over: the audio boot image, the
    /// counterpart of <c>VirtualConsole</c>'s sheet, map and flag images. It exists because
    /// ADR-036 gave a cartridge a way to write the live bank (<c>DataToSfx</c>,
    /// <c>DataToMusic</c>) and a rewind has to resimulate from the sound the run started with,
    /// not from whichever episode the previous run happened to page in last.
    /// <see cref="ResetBank"/> puts it back, and <c>VirtualConsole.ResetAssets</c> is what
    /// calls it. Before ADR-036 the bank could not drift and this copy would have bought
    /// nothing, which is exactly what <see cref="AudioBank"/> used to say.
    /// </summary>
    private readonly AudioBank _bootBank = new();

    private readonly AudioBlock _block = new();
    private readonly AudioChannel[] _channels = new AudioChannel[ChannelCount];

    // The mix is accumulated at int width and narrowed once, so a future change to VolumeStep
    // cannot make channels wrap around each other silently.
    private readonly int[] _mix = new int[AudioBlock.SamplesPerTick];

    private int _musicPattern = NoPattern;
    private int _musicTick;
    private int _musicLength;
    private int _musicLoopStart;
    private bool _musicPlaying;

    // The order walk (ADR-040): which entry of the song's order table is playing, and the
    // transposition that entry asked for.
    private int _musicOrderIndex;
    private int _musicTranspose;

    // The row clock: _musicRowClock counts in 1/32 ticks and _musicRowSpeed is the row's length
    // in the same units, so a row can be 7.5 ticks long without any accumulated drift.
    private int _musicRow;
    private int _musicRowClock;
    private int _musicRowSpeed;
    private int _musicRows;

    // The instrument each channel has latched, tracker-style: a cell that names no instrument
    // keeps the one the channel was given. Sequencer state rather than a channel register, so a
    // note can restart the voice without losing which instrument the column last named.
    private readonly int[] _channelInstrument = new int[ChannelCount];

    // True while PreviewPattern is playing one pattern for the editor: the order is not walked
    // and the song stops when the pattern ends.
    private bool _musicPreview;

    // The music fade (ADR-037): a linear ramp of the music channels' gain over _fadeTicks
    // ticks, evaluated as a closed formula of the tick counter — never an accumulation, the
    // same rule the per-step effects follow. _fadeFrom is the gain the ramp started at, which
    // matters only for a fade-out begun in the middle of a fade-in: the volume must ramp on
    // from where it is, not jump back to full first. _musicGain is the value the mixer reads;
    // it holds GainUnity whenever no fade is running.
    private int _musicGain = GainUnity;
    private int _fadeTicks;
    private int _fadeTick;
    private int _fadeFrom;
    private bool _fadeOut;

    // Which channels the music may claim (ADR-037): bit i = channel i. Zero — the value every
    // call without a mask lands on — means all four, which is the pre-mask behaviour.
    private int _musicMask;

    /// <summary>A silent chip with an empty bank.</summary>
    public Apu() => Reset();

    /// <summary>
    /// The tick's samples. The instance never changes, so a shell caches it once, exactly the
    /// way it caches the framebuffer.
    /// </summary>
    public AudioBlock Block => _block;

    /// <summary>True while a music pattern is playing.</summary>
    public bool IsMusicPlaying => _musicPlaying;

    /// <summary>The music pattern playing, or <see cref="NoPattern"/>.</summary>
    public int CurrentPattern => _musicPattern;

    /// <summary>
    /// The order entry playing: the index into the song's order table, which is also what
    /// <see cref="PlayMusic(int)"/> takes.
    /// </summary>
    public int CurrentOrder => _musicOrderIndex;

    /// <summary>The row of the current pattern. For the tracker's follow mode.</summary>
    public int CurrentRow => _musicRow;

    /// <summary>The song of the live bank — the model the tracker reads and writes.</summary>
    public MusicSong Song => _bank.Song;

    /// <summary>
    /// Entries <see cref="PlayMusic(int)"/> accepts: the song's order length. Anything at or
    /// above it is the same silent no-op every other out-of-range index on this surface is.
    /// </summary>
    public int MusicEntryCount => _bank.Song.OrderLength;

    /// <summary>The SFX slot a channel is playing, or -1 when it is idle. For tests, HUDs and diagnostics.</summary>
    public int ChannelSfx(int channel) =>
        (uint)channel < ChannelCount ? _channels[channel].SfxId : AudioChannel.Idle;

    /// <summary>True when a channel is sounding. For tests, HUDs and diagnostics.</summary>
    public bool IsChannelBusy(int channel) =>
        (uint)channel < ChannelCount && !_channels[channel].IsIdle;

    /// <summary>True when the music sequencer, rather than the cartridge, started what a channel is playing.</summary>
    public bool IsChannelMusic(int channel) =>
        (uint)channel < ChannelCount && !_channels[channel].IsIdle && _channels[channel].FromMusic;

    /// <summary>
    /// Silences everything and puts every register back to its boot value. Called from
    /// <see cref="VirtualConsole"/>'s runtime reset, which is what makes the chip part of what
    /// a rewind and a hot reload put back (SPEC-8 §7). The bank survives: it is cartridge data,
    /// like the sprite sheet, not state.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i].Stop();
        }
        _musicPattern = NoPattern;
        _musicTick = 0;
        _musicLength = 0;
        _musicLoopStart = 0;
        _musicPlaying = false;
        ClearSongState();
        ClearFade();
        _musicMask = 0;
        _block.Clear();
        Array.Clear(_mix);
    }

    /// <summary>
    /// Installs cartridge audio, copying it in — see <see cref="AudioBank.CopyFrom"/> for why
    /// the copy is defensive. Anything currently playing stops, because a channel halfway
    /// through step 12 of an SFX that has just been replaced by different data is not a state
    /// any rewind could reproduce.
    /// </summary>
    public void LoadBank(AudioBank? bank)
    {
        _bank.CopyFrom(bank);
        _bootBank.CopyFrom(bank);
        Reset();
    }

    /// <summary>
    /// Fills the SFX half of the bank from a cartridge payload; see
    /// <see cref="AudioBank.LoadSfxPayload"/>. This is the <em>loader's</em> door, so it moves
    /// the boot bank too — what it installs is what a rewind comes back to. The cartridge's
    /// own door is <see cref="PageSfx"/>, and the difference between them is that one line.
    /// </summary>
    public void LoadSfxPayload(ReadOnlySpan<byte> payload)
    {
        _bank.LoadSfxPayload(payload);
        _bootBank.LoadSfxPayload(payload);
        Reset();
    }

    /// <summary>The music half of <see cref="LoadSfxPayload"/>; see <see cref="AudioBank.LoadMusicPayload"/>.</summary>
    public void LoadMusicPayload(ReadOnlySpan<byte> payload)
    {
        _bank.LoadMusicPayload(payload);
        _bootBank.LoadMusicPayload(payload);
        Reset();
    }

    /// <summary>
    /// Pages a new SFX table in from the cartridge's own data banks (ADR-036) — what
    /// <c>DataToSfx(bank, offset)</c> reaches. The payload is already exactly
    /// <see cref="AudioBank.SfxPayloadSize"/> bytes: the console checks the bank holds a whole
    /// one before calling, because at the cartridge boundary a short bank has to be a defined
    /// no-op rather than an exception travelling through the shell.
    ///
    /// <para>Two things separate this from <see cref="LoadSfxPayload"/>, and both are the
    /// point. It does <b>not</b> move the boot bank: this is the cartridge writing its own
    /// sound, and a resimulation must start again from the bank the pipeline loaded, exactly
    /// as it starts again from the loaded sprite sheet rather than the one <c>Sset</c> left
    /// behind. And it silences the chip through the same <see cref="Reset"/> the loader uses,
    /// so no channel can go on stepping through a slot whose bytes have just been replaced —
    /// a channel parked on step 12 knows nothing about the new slot's length, and neither
    /// does the new slot know what note the old one was sliding from.</para>
    /// </summary>
    public void PageSfx(ReadOnlySpan<byte> payload)
    {
        _bank.LoadSfxPayload(payload);
        Reset();
    }

    /// <summary>
    /// The music half of <see cref="PageSfx"/> — what <c>DataToMusic(bank, offset)</c>
    /// reaches. Same two rules: the boot bank is left alone, and the chip is silenced, which
    /// here also means the pattern sequencer stops rather than turning over into a pattern
    /// table it never started in.
    /// </summary>
    public void PageMusic(ReadOnlySpan<byte> payload)
    {
        _bank.LoadMusicPayload(payload);
        Reset();
    }

    /// <summary>
    /// Puts the live bank back to the one the cartridge pipeline loaded, undoing every
    /// <see cref="PageSfx"/> and <see cref="PageMusic"/> the run made. The audio half of
    /// <c>VirtualConsole.ResetAssets</c> and called from it, so every cold boot — a rewind, a
    /// restart, a hot reload — resimulates against the sound the original run started with.
    /// Chip state is not touched here; <see cref="Reset"/> owns that, and the console calls
    /// both.
    /// </summary>
    public void ResetBank() => _bank.CopyFrom(_bootBank);

    /// <summary>
    /// Starts a sound effect. This is what <c>Sfx(id, channel)</c> reaches (API-8 §5), and it
    /// changes simulation state — calling it from <c>Draw</c> is what analyzer rule QRP1004
    /// exists to stop.
    ///
    /// <para><b>Channel allocation, which has to be a rule rather than a policy.</b> With
    /// <paramref name="channel"/> 0-3 the sound takes that channel, cutting off whatever was
    /// there. With -1 the chip picks, in this order and no other:</para>
    /// <list type="number">
    ///   <item>the lowest-numbered idle channel;</item>
    ///   <item>failing that, the lowest-numbered channel the music sequencer is using — the
    ///     game's own sounds matter more than one voice of the background theme, and music
    ///     takes the channel back at its next pattern;</item>
    ///   <item>failing that — all four channels busy with the cartridge's own sounds — the call
    ///     does nothing. Stealing at that point would mean the sound you hear depends on
    ///     arrival order, and "lowest index" is a rule anyone can predict, while "oldest" or
    ///     "quietest" is a policy nobody can.</item>
    /// </list>
    ///
    /// <para><b>Id <see cref="StopSfx"/> does the opposite</b>: it silences
    /// <paramref name="channel"/>, or all four when that is -1 as well — see
    /// <see cref="StopChannel"/>.</para>
    ///
    /// <para>Out of range is silence, never an exception, like the rest of the cartridge
    /// surface: an <paramref name="id"/> of -2 or below or of 64 or above, a
    /// <paramref name="channel"/> outside -1..3, and an empty slot (length 0) all do nothing.</para>
    /// </summary>
    public void PlaySfx(int id, int channel = -1)
    {
        if (channel < -1 || channel >= ChannelCount)
        {
            return;
        }
        if (id == StopSfx)
        {
            StopChannel(channel);
            return;
        }
        if ((uint)id >= AudioBank.SfxCount)
        {
            return;
        }
        if (_bank.GetSfx(id).IsEmpty)
        {
            return;
        }
        int target = channel >= 0 ? channel : Allocate();
        if (target < 0)
        {
            return;
        }
        StartSfx(ref _channels[target], id, fromMusic: false);
    }

    /// <summary>
    /// Starts a <em>segment</em> of a sound effect: steps <paramref name="offsetSteps"/> up to
    /// but not including <paramref name="offsetSteps"/> + <paramref name="lengthSteps"/>, on
    /// the same channel rule as <see cref="PlaySfx(int, int)"/>. This is what
    /// <c>Sfx(id, channel, offsetSteps, lengthSteps)</c> reaches (ADR-037), and it exists for
    /// the cartridge that packs several short sounds into one 32-step slot because its bank is
    /// full — PICO-8's <c>sfx(n, ch, offset, length)</c>, the call Terra's <c>ssfx</c> was
    /// written against.
    ///
    /// <para><b>The segment plays once and ignores the slot's loop.</b> The caller named an
    /// exact run of steps; honouring a loop that happens to cross the window would make
    /// <paramref name="lengthSteps"/> a lie and hold the channel forever. A cartridge that
    /// wants the looping whole-slot behaviour has the two-argument call, which is untouched.</para>
    ///
    /// <para><b>Soft edges, like every call on this surface.</b> An
    /// <paramref name="offsetSteps"/> outside the slot's played steps (0..length-1) does
    /// nothing at all; a <paramref name="lengthSteps"/> of 0 or less does nothing (the rule
    /// every non-positive size follows, API-8 §1); a segment overhanging the slot's end is
    /// clipped to it, the way <c>DataToGfx</c> clips — the steps that exist still play.
    /// <paramref name="id"/> = -1 stops exactly as it does on the two-argument call, the
    /// segment arguments ignored: one rule for "-1 stops", not one per overload (ADR-020).
    /// Everything else out of range — id, channel, an empty slot — is the same silent no-op
    /// as on <see cref="PlaySfx(int, int)"/>.</para>
    ///
    /// <para>Within the segment nothing sounds different: effects still run their closed
    /// formulas off the channel's tick counters, and an arpeggio still reads its aligned
    /// group of four from the <em>slot</em>, exactly as it does mid-slot today — the segment
    /// chooses which steps play, never what a step sounds like.</para>
    /// </summary>
    public void PlaySfx(int id, int channel, int offsetSteps, int lengthSteps)
    {
        if (channel < -1 || channel >= ChannelCount)
        {
            return;
        }
        if (id == StopSfx)
        {
            StopChannel(channel);
            return;
        }
        if ((uint)id >= AudioBank.SfxCount)
        {
            return;
        }
        SfxSlot slot = _bank.GetSfx(id);
        if ((uint)offsetSteps >= (uint)slot.Length || lengthSteps <= 0)
        {
            // Covers the empty slot too: with Length 0 no offset is inside it.
            return;
        }
        int end = lengthSteps >= slot.Length - offsetSteps
            ? slot.Length                       // clipped to the slot, overflow-proof
            : offsetSteps + lengthSteps;
        int target = channel >= 0 ? channel : Allocate();
        if (target < 0)
        {
            return;
        }
        _channels[target].StartSegment(id, NoteTable.ToPitch(slot[offsetSteps].Note), offsetSteps, end);
    }

    /// <summary>
    /// Silences one channel (0-3), or all four when <paramref name="channel"/> is -1. This is
    /// what <c>Sfx(-1, channel)</c> reaches (API-8 §5), and it changes simulation state exactly
    /// as <see cref="PlaySfx"/> does — a rewind reproduces it because the call comes from
    /// <c>Update</c>, which resimulation re-runs.
    ///
    /// <para>The channel is silenced whoever filled it. A voice the music sequencer was driving
    /// goes quiet now and comes back at the next pattern, the same way it does when the
    /// cartridge takes that channel for a sound of its own — one rule, not two. Stopping every
    /// channel is <b>not</b> the same as stopping the music: <see cref="PlayMusic"/> with a
    /// negative pattern is what ends a song, and a song still playing refills its voices at its
    /// next pattern.</para>
    ///
    /// <para>Anything outside -1..3 does nothing, like every other out-of-range argument on this
    /// surface.</para>
    /// </summary>
    public void StopChannel(int channel)
    {
        if (channel < -1 || channel >= ChannelCount)
        {
            return;
        }
        if (channel >= 0)
        {
            _channels[channel].Stop();
            return;
        }
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i].Stop();
        }
    }

    /// <summary>
    /// Starts a music pattern, or stops the music. This is what <c>Music(pattern)</c> reaches
    /// (API-8 §5), and it changes simulation state exactly as <see cref="PlaySfx"/> does.
    ///
    /// <para>A negative pattern stops the music, which covers the documented <c>Music(-1)</c>
    /// and the default <c>Music()</c>. A pattern of 64 or more does nothing, like every other
    /// out-of-range index on this surface. The pattern starts on this very tick, so a sound
    /// asked for in <c>Update</c> is audible in the block that same <c>Update</c> produces.</para>
    /// </summary>
    public void PlayMusic(int pattern = -1) => PlayMusic(pattern, 0);

    /// <summary>
    /// Starts a music pattern with a fade-in, stops the music with a fade-out, and optionally
    /// reserves channels for the music — what <c>Music(pattern, fadeTicks, channelMask)</c>
    /// reaches (ADR-037). With <paramref name="fadeTicks"/> 0 and <paramref name="channelMask"/>
    /// 0 this is <see cref="PlayMusic(int)"/> to the bit, which is what the one-argument call
    /// forwards through.
    ///
    /// <para><b>The fade is a linear gain ramp on the music's channels</b>, measured in ticks
    /// like everything else the chip does (SPEC-8 §7): starting a pattern with
    /// <paramref name="fadeTicks"/> &gt; 0 ramps their gain from silence to full over that many
    /// ticks; a negative <paramref name="pattern"/> with <paramref name="fadeTicks"/> &gt; 0
    /// ramps from the current gain to silence and then stops the music — patterns keep turning
    /// over underneath the ramp until it lands. The ramp mirrors the per-step fade effects'
    /// arithmetic ((t+1)/F up, (F-1-t)/F down), is evaluated as a closed formula of the tick
    /// counter, and scales amplitudes before the mix, so it is in the PCM and therefore in the
    /// audio hash — deterministic, replayable, cross-architecture. Channels the cartridge's own
    /// <see cref="PlaySfx(int, int)"/> started are never scaled. A fade-out requested while no
    /// music plays just stops (nothing to ramp); a new pattern started mid-fade replaces the
    /// fade with its own (or with none).</para>
    ///
    /// <para><b>A non-zero <paramref name="channelMask"/> (bit i = channel i, masked to the
    /// four that exist) reserves only those channels for the music.</b> Pattern voices on
    /// channels outside the mask are never started — the cartridge keeps those channels for
    /// its own sounds, and the music does not take them back at the next pattern either. The
    /// skipped voices still count toward the pattern's length, exactly like a voice skipped
    /// because an effect holds its channel: song timing must not depend on what else is
    /// playing, masked or not. Zero means all four channels, which is the behaviour every call
    /// without a mask always had. The mask lives until the music stops or the next
    /// <c>Music(pattern, ...)</c> replaces it.</para>
    /// </summary>
    public void PlayMusic(int pattern, int fadeTicks, int channelMask = 0)
    {
        if (pattern < 0)
        {
            if (fadeTicks > 0 && _musicPlaying)
            {
                BeginFadeOut(fadeTicks);
                return;
            }
            StopMusic();
            return;
        }
        if (pattern >= MusicEntryCount)
        {
            return;
        }
        _musicMask = channelMask & ChannelMaskAll;
        ReleaseMaskedVoices();
        _musicPreview = false;
        if (fadeTicks > 0)
        {
            _fadeOut = false;
            _fadeFrom = 0;
            _fadeTicks = fadeTicks;
            _fadeTick = 0;
            _musicGain = FadeGain();
        }
        else
        {
            ClearFade();
        }
        _musicPlaying = true;
        _musicLoopStart = pattern;
        Array.Clear(_channelInstrument);
        StartOrder(pattern);
    }

    /// <summary>
    /// Plays one pattern of the song once and stops — the tracker's "play frame"
    /// (REFERENCES-EDITORS §6.1). The order is not walked, no loop or stop flag is consulted, and
    /// the transposition is zero: the editor hears the pattern as written, not as the song
    /// arranges it.
    ///
    /// <para>Simulation state like every other call on this surface, which is why the editor may
    /// use it and a cartridge may not: there is no <c>Preview</c> in API-8.</para>
    /// </summary>
    public void PreviewPattern(int pattern)
    {
        if ((uint)pattern >= AudioBank.PatternCount)
        {
            return;
        }
        StopMusic();
        _musicPreview = true;
        _musicPlaying = true;
        _musicOrderIndex = 0;
        _musicLoopStart = 0;
        _musicTranspose = 0;
        Array.Clear(_channelInstrument);
        StartSongPattern(pattern);
    }

    /// <summary>
    /// Puts one row of the song on the channels and leaves them ringing — the tracker's
    /// "audition the row under the cursor". No sequencer runs afterwards, so each voice plays its
    /// instrument to its own end; a second call replaces what the first one started.
    /// </summary>
    public void PreviewRow(int pattern, int row)
    {
        if ((uint)pattern >= AudioBank.PatternCount || (uint)row >= MusicSong.RowCount)
        {
            return;
        }
        StopMusic();
        _musicTranspose = 0;
        ApplyRow(pattern, row);
    }

    /// <summary>Stops the music and silences the channels it was driving. Channels the cartridge started keep playing.</summary>
    public void StopMusic()
    {
        _musicPlaying = false;
        _musicPattern = NoPattern;
        _musicTick = 0;
        _musicLength = 0;
        ClearSongState();
        ClearFade();
        _musicMask = 0;
        for (int i = 0; i < _channels.Length; i++)
        {
            if (_channels[i].FromMusic)
            {
                _channels[i].Stop();
            }
        }
    }

    /// <summary>
    /// Lets go of the music's voices on channels the new mask does not name (ADR-037: "the mask
    /// keeps this channel out of the music's hands, and a voice a previous song left here is let
    /// go"). Channels the cartridge's own <see cref="PlaySfx(int, int)"/> started are never
    /// touched — the mask is about what the <em>music</em> may hold.
    ///
    /// <para>It is a call of its own, made when the mask is set, because a row of a pattern
    /// <em>skips</em> a masked channel rather than writing to it: with nothing reading those
    /// cells there is nowhere else the release could happen. A mask of zero means all four
    /// channels and releases nothing.</para>
    /// </summary>
    private void ReleaseMaskedVoices()
    {
        if (_musicMask == 0)
        {
            return;
        }
        for (int i = 0; i < _channels.Length; i++)
        {
            if ((_musicMask & (1 << i)) == 0 && _channels[i].FromMusic)
            {
                _channels[i].Stop();
            }
        }
    }

    /// <summary>The order walk and the row clock back at rest. The state every non-playing chip sits in.</summary>
    private void ClearSongState()
    {
        _musicOrderIndex = 0;
        _musicTranspose = 0;
        _musicRow = 0;
        _musicRowClock = 0;
        _musicRowSpeed = 0;
        _musicRows = 0;
        _musicPreview = false;
        Array.Clear(_channelInstrument);
    }

    /// <summary>No fade running, music at full gain. The state every non-fading run sits in.</summary>
    private void ClearFade()
    {
        _fadeTicks = 0;
        _fadeTick = 0;
        _fadeFrom = 0;
        _fadeOut = false;
        _musicGain = GainUnity;
    }

    /// <summary>
    /// Arms a fade-out over <paramref name="fadeTicks"/> ticks, ramping from the gain the music
    /// has <em>right now</em> — so a stop requested in the middle of a fade-in goes on down
    /// smoothly instead of jumping back to full volume first.
    /// </summary>
    private void BeginFadeOut(int fadeTicks)
    {
        _fadeFrom = _musicGain;
        _fadeOut = true;
        _fadeTicks = fadeTicks;
        _fadeTick = 0;
        _musicGain = FadeGain();
    }

    /// <summary>
    /// The gain for the current fade tick, 0..<see cref="GainUnity"/> — a closed formula of
    /// (<c>_fadeTick</c>, <c>_fadeTicks</c>, <c>_fadeFrom</c>), never an accumulation, so no
    /// drift can build up and two runs cannot end a long fade a fraction apart. Long
    /// arithmetic because <c>fadeTicks</c> is cartridge input and may be enormous; enormous is
    /// a very slow fade, not an overflow.
    /// </summary>
    private int FadeGain()
    {
        if (_fadeTicks <= 0)
        {
            return GainUnity;
        }
        if (_fadeOut)
        {
            int remaining = _fadeTicks - 1 - _fadeTick;
            return remaining <= 0 ? 0 : (int)((long)_fadeFrom * remaining / _fadeTicks);
        }
        int t = _fadeTick + 1;
        return t >= _fadeTicks ? GainUnity : (int)((long)GainUnity * t / _fadeTicks);
    }

    /// <summary>
    /// Generates the tick's <see cref="Block"/> and moves the sequencers on by one tick.
    ///
    /// <para>Called by <see cref="VirtualConsole"/> once per tick, from the one place both
    /// <see cref="VirtualConsole.Tick"/> and <see cref="VirtualConsole.TickUpdateOnly"/> pass
    /// through. Calling it from anywhere else advances the simulation behind the console's
    /// back, which is a determinism bug with a long fuse; it is public because tests drive the
    /// chip on its own, not because a shell should.</para>
    /// </summary>
    public void RenderTick()
    {
        Render();
        Advance();
    }

    // --- synthesis ---

    private void Render()
    {
        short[] samples = _block.Samples;
        if (AllIdle())
        {
            // Provably the same bytes as the loop below: an idle channel has no amplitude and
            // its phase is parked at zero, so nothing it could carry into the next tick differs.
            // This is the branch a rewind spends most of its life in, which is why it exists.
            Array.Clear(samples);
            return;
        }

        int[] mix = _mix;
        Array.Clear(mix);
        for (int i = 0; i < _channels.Length; i++)
        {
            ref AudioChannel channel = ref _channels[i];
            if (channel.IsIdle)
            {
                continue;
            }
            RenderChannel(ref channel, mix);
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
        }
    }

    private bool AllIdle()
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            if (!_channels[i].IsIdle)
            {
                return false;
            }
        }
        return true;
    }

    private void RenderChannel(ref AudioChannel channel, int[] mix)
    {
        SfxSlot slot = _bank.GetSfx(channel.SfxId);
        SfxStep step = slot[channel.Step];
        int amplitude = Amplitude(slot, in channel, step);
        if (channel.Gain != GainUnity)
        {
            // The volume column of a version 2 cell (ADR-040), in the same 0..256 fixed point the
            // music fade uses. Guarded exactly as the fade is: at GainUnity the multiply is the
            // identity, and the guard makes that a provable no-op rather than a claim about
            // rounding — which is what keeps every version 1 audio hash where it is.
            amplitude = amplitude * channel.Gain / GainUnity;
        }
        if (channel.FromMusic && _musicGain != GainUnity)
        {
            // The music fade (ADR-037). Only music-driven voices are scaled, and only while a
            // fade is actually off unity — at GainUnity the multiply would be the identity, and
            // the guard makes that a provable no-op instead of a claim about rounding.
            amplitude = amplitude * _musicGain / GainUnity;
        }
        uint increment = NoteTable.Increment(Pitch(slot, in channel, step) + CellPitchOffset(in channel));

        // A silent step still runs the loop with amplitude 0 instead of taking a shortcut. The
        // phase and the noise register move exactly as they would under a sounding step, so a
        // rest inside an SFX cannot change what the step after it sounds like — and there is
        // one code path, not two that have to agree.
        uint phase = channel.Phase;
        switch (step.Wave)
        {
            case Waveform.Pulse12:
                RenderPulse(mix, ref phase, increment, amplitude, Duty12);
                break;
            case Waveform.Pulse25:
                RenderPulse(mix, ref phase, increment, amplitude, Duty25);
                break;
            case Waveform.Triangle:
                RenderTriangle(mix, ref phase, increment, amplitude);
                break;
            case Waveform.Saw:
                RenderSaw(mix, ref phase, increment, amplitude);
                break;
            case Waveform.Noise:
                RenderNoise(mix, ref phase, increment, amplitude, ref channel.Noise);
                break;
            default:
                RenderPulse(mix, ref phase, increment, amplitude, Duty50);
                break;
        }
        channel.Phase = phase;
    }

    private static void RenderPulse(int[] mix, ref uint phase, uint increment, int amplitude, uint duty)
    {
        unchecked
        {
            uint p = phase;
            for (int i = 0; i < mix.Length; i++)
            {
                mix[i] += p < duty ? amplitude : -amplitude;
                p += increment;
            }
            phase = p;
        }
    }

    private static void RenderTriangle(int[] mix, ref uint phase, uint increment, int amplitude)
    {
        unchecked
        {
            uint p = phase;
            for (int i = 0; i < mix.Length; i++)
            {
                int t = (int)(p >> 16);
                int wave = (t < 32768 ? t * 2 : 131070 - (t * 2)) - 32767;
                mix[i] += (wave * amplitude) >> WaveShift;
                p += increment;
            }
            phase = p;
        }
    }

    private static void RenderSaw(int[] mix, ref uint phase, uint increment, int amplitude)
    {
        unchecked
        {
            uint p = phase;
            for (int i = 0; i < mix.Length; i++)
            {
                int wave = (int)(p >> 16) - 32768;
                mix[i] += (wave * amplitude) >> WaveShift;
                p += increment;
            }
            phase = p;
        }
    }

    /// <summary>
    /// Noise: a 15-bit maximal LFSR (feedback from bits 0 and 1, period 32767), clocked once
    /// per phase wrap so that the note controls how bright the noise is — low notes rumble,
    /// high notes hiss. Between clocks the output is held, which is what makes it noise at a
    /// pitch rather than white noise. Since the highest note's increment is far below 2^32,
    /// a sample can wrap at most once, so the clock rate is exactly the note's frequency.
    /// </summary>
    private static void RenderNoise(int[] mix, ref uint phase, uint increment, int amplitude, ref ushort noise)
    {
        unchecked
        {
            uint p = phase;
            ushort lfsr = noise;
            for (int i = 0; i < mix.Length; i++)
            {
                mix[i] += (lfsr & 1) != 0 ? amplitude : -amplitude;
                uint next = p + increment;
                if (next < p)
                {
                    lfsr = (ushort)((lfsr >> 1) | (((lfsr ^ (lfsr >> 1)) & 1) << 14));
                }
                p = next;
            }
            phase = p;
            noise = lfsr;
        }
    }

    // --- per-tick effect evaluation ---

    /// <summary>
    /// The pitch a step sounds at on this tick, in 1/256 semitones. Every effect is a closed
    /// formula in (step data, tick inside the step), never an accumulation, so no drift can
    /// build up over a long note and two runs cannot end up a fraction apart.
    /// </summary>
    private static int Pitch(SfxSlot slot, in AudioChannel channel, SfxStep step)
    {
        int pitch = NoteTable.ToPitch(step.Note);
        int speed = StepTicks(slot, in channel);
        int t = channel.StepTick;
        switch (step.Effect)
        {
            case NoteEffect.Slide:
                return channel.PreviousPitch + (((pitch - channel.PreviousPitch) * t) / speed);
            case NoteEffect.Vibrato:
                return pitch + VibratoDepth[channel.Age & (VibratoDepth.Length - 1)];
            case NoteEffect.Drop:
                return (pitch * (speed - t)) / speed;
            case NoteEffect.Arpeggio:
                return ArpeggioPitch(slot, in channel, pitch);
            default:
                return pitch;
        }
    }

    /// <summary>
    /// Arpeggio: the aligned group of <see cref="ArpeggioGroup"/> steps the current step falls
    /// into, one note every <see cref="ArpeggioTicksPerNote"/> ticks — a chord played on one
    /// channel, the oldest trick in tracker music.
    ///
    /// <para><b>Only the steps that sound take part.</b> A step at volume 0 is a rest and has no
    /// note to lend: written canonically it is the zero word, whose note is 0, so until M4 a
    /// pause inside the group came out as a C-2 thump in the middle of the chord. Steps at or
    /// past <see cref="SfxSlot.Length"/> are skipped for the same reason — the slot does not
    /// play them, so they cannot be heard through the back door of an arpeggio either, and that
    /// is what lets <c>sfx.bin</c> require them to be zero (docs/AUDIO-FORMAT.md §2).</para>
    ///
    /// <para>The cycle therefore runs over the sounding steps of the group, in order, and when
    /// the current step is the only one that sounds the effect is a no-op: a chord of one note
    /// is that note, not that note stuttering against three rests.</para>
    /// </summary>
    private static int ArpeggioPitch(SfxSlot slot, in AudioChannel channel, int pitch)
    {
        int group = channel.Step & ~(ArpeggioGroup - 1);
        int sounding = 0;
        for (int i = 0; i < ArpeggioGroup; i++)
        {
            if (StepSounds(slot, group + i))
            {
                sounding++;
            }
        }
        if (sounding <= 1)
        {
            return pitch;
        }

        int index = (channel.Age / ArpeggioTicksPerNote) % sounding;
        for (int i = 0; i < ArpeggioGroup; i++)
        {
            if (!StepSounds(slot, group + i))
            {
                continue;
            }
            if (index == 0)
            {
                return NoteTable.ToPitch(slot[group + i].Note);
            }
            index--;
        }
        return pitch;   // Unreachable: the loop above counted at least two sounding steps.
    }

    /// <summary>True when a step of a slot is played and is not a rest — the two ways a step can have no note.</summary>
    private static bool StepSounds(SfxSlot slot, int step) => step < slot.Length && slot[step].Volume != 0;

    /// <summary>
    /// Ticks one step of the slot lasts on this channel: the slot's own speed, unless a version 2
    /// instrument overrode it (<see cref="MusicInstrument.Speed"/>). The override is 0 for every
    /// voice a version 1 bank or a cartridge <c>Sfx</c> call starts, so that path reads
    /// <c>slot.Speed</c> and nothing else — the same number, through the same expression, as
    /// before ADR-040.
    /// </summary>
    private static int StepTicks(SfxSlot slot, in AudioChannel channel) =>
        channel.SpeedOverride > 0 ? channel.SpeedOverride : slot.Speed;

    /// <summary>
    /// What the effect column of a version 2 cell adds to the pitch, in 1/256 semitones — a
    /// closed formula of (cell, ticks since the cell), never an accumulation, the same rule every
    /// per-step effect follows.
    ///
    /// <para>Zero for every voice that no cell has touched, which is every voice in a version 1
    /// run: <see cref="AudioChannel.PitchOffset"/> and <see cref="AudioChannel.CellEffect"/> both
    /// start at 0 and only <see cref="ApplyCell"/> writes them. Adding zero is exact, so the v1
    /// render path is arithmetically the one it was.</para>
    /// </summary>
    private static int CellPitchOffset(in AudioChannel channel)
    {
        switch ((MusicEffect)channel.CellEffect)
        {
            case MusicEffect.Slide:
                // Portamento: the glide is linear in ticks between the two offsets, and the
                // divisor is the parameter, which validation and MusicCell both guarantee is
                // at least 1.
                return channel.GlideFrom
                    + (((channel.GlideTo - channel.GlideFrom) * channel.EffectTick) / channel.CellParam);
            case MusicEffect.Arpeggio:
            {
                // note, note + high nibble, note + low nibble, one every ArpeggioTicksPerNote
                // ticks — the chord on one channel, spelled the way trackers have spelled it
                // since Soundtracker. Three positions, not four: a tracker arpeggio is a triad.
                // The parentheses around the whole modulo are load-bearing: a switch expression
                // binds tighter than `%`, so `a % 3 switch {...}` would mean `a % (3 switch
                // {...})` and divide by the arm that 3 selects.
                int semitones = ((channel.EffectTick / ArpeggioTicksPerNote) % 3) switch
                {
                    1 => (channel.CellParam >> 4) & 0x0F,
                    2 => channel.CellParam & 0x0F,
                    _ => 0,
                };
                return channel.PitchOffset + (semitones * NoteTable.PitchesPerSemitone);
            }
            default:
                return channel.PitchOffset;
        }
    }

    /// <summary>The amplitude a step sounds at on this tick, 0..<see cref="PeakAmplitude"/>.</summary>
    private static int Amplitude(SfxSlot slot, in AudioChannel channel, SfxStep step)
    {
        int amplitude = step.Volume * VolumeStep;
        int speed = StepTicks(slot, in channel);
        int t = channel.StepTick;
        return step.Effect switch
        {
            NoteEffect.FadeIn => (amplitude * (t + 1)) / speed,
            NoteEffect.FadeOut => (amplitude * (speed - 1 - t)) / speed,
            _ => amplitude,
        };
    }

    // --- sequencing ---

    private void Advance()
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            ref AudioChannel channel = ref _channels[i];
            if (channel.IsIdle)
            {
                continue;
            }
            AdvanceChannel(ref channel);
        }

        if (!_musicPlaying)
        {
            return;
        }
        if (_fadeTicks > 0)
        {
            _fadeTick++;
            if (_fadeTick >= _fadeTicks)
            {
                if (_fadeOut)
                {
                    // The ramp has landed on silence; the song is over. No pattern turnover on
                    // the way out — StopMusic is the same stop a Music(-1) without a fade does.
                    StopMusic();
                    return;
                }
                ClearFade();
            }
            else
            {
                _musicGain = FadeGain();
            }
        }
        AdvanceSong();
    }

    /// <summary>
    /// The row clock of a pattern. The accumulator gains one whole tick
    /// (<see cref="MusicSong.SpeedUnitsPerTick"/>) per tick and spends one row's worth whenever
    /// it can, so a row of 7.5 ticks alternates 7 and 8 ticks and the average is exact for as
    /// long as the pattern runs — that is the whole point of measuring rows in 1/32 of a tick.
    ///
    /// <para>At most one row can fall in a tick, because a row is at least one tick long: the
    /// file format refuses a shorter one and <see cref="MusicSong.SetPatternSpeed"/> clamps one
    /// that arrived from a data bank. So this is an <c>if</c> and not a loop, and no song can
    /// make the sequencer spin.</para>
    /// </summary>
    private void AdvanceSong()
    {
        _musicTick++;
        _musicRowClock += MusicSong.SpeedUnitsPerTick;
        if (_musicRowClock < _musicRowSpeed)
        {
            return;
        }
        _musicRowClock -= _musicRowSpeed;
        int next = _musicRow + 1;
        if (next >= _musicRows)
        {
            NextOrderEntry();
            return;
        }
        _musicRow = next;
        ApplyRow(_musicPattern, next);
    }

    private void AdvanceChannel(ref AudioChannel channel)
    {
        SfxSlot slot = _bank.GetSfx(channel.SfxId);
        channel.Age = (channel.Age + 1) & AgeMask;
        if (channel.CellEffect != 0 && AdvanceCellEffect(ref channel))
        {
            return;
        }
        channel.StepTick++;
        if (channel.StepTick < StepTicks(slot, in channel))
        {
            return;
        }

        channel.StepTick = 0;
        channel.PreviousPitch = NoteTable.ToPitch(slot[channel.Step].Note);
        int next = channel.Step + 1;
        if (channel.SegmentEnd > 0)
        {
            // A segment (ADR-037) plays its named steps once: no loop, and the bound was
            // clipped to the slot when the segment started, so this is the only exit.
            if (next >= channel.SegmentEnd)
            {
                channel.Stop();
                return;
            }
        }
        else
        {
            if (slot.Loops && next >= slot.LoopEnd)
            {
                next = slot.LoopStart;
            }
            if (next >= slot.Length)
            {
                channel.Stop();
                return;
            }
        }
        channel.Step = next;
    }

    /// <summary>
    /// Moves a version 2 cell effect on by one tick and answers whether it silenced the voice.
    /// Only <see cref="MusicEffect.Cut"/> ever does, and only once: it stops the channel the tick
    /// its parameter names, which is why the caller returns immediately afterwards instead of
    /// stepping a slot that is no longer playing.
    /// </summary>
    private static bool AdvanceCellEffect(ref AudioChannel channel)
    {
        channel.EffectTick = (channel.EffectTick + 1) & AgeMask;
        switch ((MusicEffect)channel.CellEffect)
        {
            case MusicEffect.Cut:
                if (channel.EffectTick >= channel.CellParam)
                {
                    channel.Stop();
                    return true;
                }
                break;
            case MusicEffect.Slide:
                if (channel.EffectTick >= channel.CellParam)
                {
                    // The glide has landed. Latching the target and disarming the effect is what
                    // makes the pitch a constant afterwards instead of a formula that keeps
                    // dividing — and what lets the next cell's glide start from a known place.
                    channel.PitchOffset = channel.GlideTo;
                    channel.CellEffect = 0;
                    channel.CellParam = 0;
                    channel.EffectTick = 0;
                }
                break;
        }
        return false;
    }

    private void StartSfx(ref AudioChannel channel, int id, bool fromMusic)
    {
        SfxSlot slot = _bank.GetSfx(id);
        channel.Start(id, NoteTable.ToPitch(slot[0].Note), fromMusic);
    }

    private int Allocate()
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            if (_channels[i].IsIdle)
            {
                return i;
            }
        }
        for (int i = 0; i < _channels.Length; i++)
        {
            if (_channels[i].FromMusic)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Puts an <em>order entry</em> on the channels: which pattern plays, in which key, and what
    /// the entry remembers about looping (ADR-040).
    /// </summary>
    private void StartOrder(int entry)
    {
        _musicOrderIndex = entry;
        MusicOrderEntry order = _bank.Song.Order(entry);
        if ((order.Flags & MusicFlags.LoopStart) != 0)
        {
            _musicLoopStart = entry;
        }
        _musicTranspose = order.Transpose;
        StartSongPattern(order.Pattern);
    }

    /// <summary>
    /// Starts a pattern: the row clock goes back to zero and row 0 is put on the
    /// channels this very tick, so a song asked for in <c>Update</c> is audible in the block that
    /// same <c>Update</c> produces — the promise <see cref="PlayMusic(int)"/> already made.
    ///
    /// <para><b>The row clock restarts at every pattern rather than carrying its remainder
    /// across.</b> The cost is at most 31/32 of a tick per pattern; what it buys is that a
    /// pattern's length is a pure function of the pattern, which is what the tracker's ruler and
    /// <see cref="MusicSong.PatternTicks"/> need it to be.</para>
    ///
    /// <para>An unused pattern (no rows) is a bar of rest of exactly
    /// <see cref="MinPatternTicks"/>: silence is a section, not the end of the piece, which is
    /// what the <see cref="MusicFlags.Stop"/> flag is for.</para>
    /// </summary>
    private void StartSongPattern(int index)
    {
        _musicPattern = index;
        _musicTick = 0;
        _musicRow = 0;
        _musicRowClock = 0;
        MusicSong song = _bank.Song;
        int rows = song.PatternRows(index);
        if (rows == 0)
        {
            _musicRows = 1;
            _musicRowSpeed = MinPatternTicks * MusicSong.SpeedUnitsPerTick;
            _musicLength = MinPatternTicks;
            return;
        }
        _musicRows = rows;
        _musicRowSpeed = Math.Max(song.PatternSpeed(index), MusicSong.MinRowSpeed);
        _musicLength = song.PatternTicks(index);
        ApplyRow(index, 0);
    }

    /// <summary>
    /// Puts one row of a pattern on the channels.
    ///
    /// <para>Three reasons a cell is skipped: an empty cell says nothing; a channel outside the
    /// music's mask is not the music's to take (ADR-037); and a channel the cartridge is using is
    /// left alone, so a theme ducks under a sound effect and picks the voice back up when the
    /// effect ends. What is <em>not</em> a reason is the length of the pattern: a pattern lasts
    /// rows x speed whatever the channels do, which is one fewer way for song timing to depend on
    /// what else is playing.</para>
    /// </summary>
    private void ApplyRow(int pattern, int row)
    {
        MusicSong song = _bank.Song;
        for (int i = 0; i < ChannelCount; i++)
        {
            MusicCell cell = song.Cell(pattern, row, i);
            if (cell.IsEmpty)
            {
                continue;
            }
            if (_musicMask != 0 && (_musicMask & (1 << i)) == 0)
            {
                continue;
            }
            ref AudioChannel channel = ref _channels[i];
            if (!channel.IsIdle && !channel.FromMusic)
            {
                continue;
            }
            ApplyCell(ref channel, i, cell, song);
        }
    }

    /// <summary>
    /// What one cell does to one voice, column by column, in the order a tracker applies them:
    /// the instrument latches first (it decides what a note in the same cell will sound like),
    /// then the note, then the level, then the effect.
    ///
    /// <para><b>A note-on restarts the instrument; a slide does not.</b> That is the whole
    /// difference between the two, and it is why <see cref="MusicEffect.Slide"/> is checked
    /// before the note is started rather than after. A slide on an idle voice has nothing to
    /// glide from, so it starts the note instead and the effect is dropped — the soft-edge rule
    /// the whole audio surface follows.</para>
    ///
    /// <para><b>A note-on without a volume column goes back to full level.</b> The alternative —
    /// keeping the level of the note before it — makes one quiet cell poison every note after it
    /// with nothing on screen to explain why.</para>
    /// </summary>
    private void ApplyCell(ref AudioChannel channel, int index, MusicCell cell, MusicSong song)
    {
        if (cell.HasInstrument)
        {
            _channelInstrument[index] = cell.Instrument;
        }
        if (cell.Kind == MusicNoteKind.Off)
        {
            channel.Stop();
            return;
        }

        MusicInstrument instrument = song.Instrument(_channelInstrument[index]);
        bool glide = cell.Effect == MusicEffect.Slide && !channel.IsIdle && channel.FromMusic;
        if (cell.Kind == MusicNoteKind.On && !glide && !StartCellNote(ref channel, cell.Note, instrument))
        {
            // The instrument names an empty slot: this note is silence, exactly as Sfx() on an
            // empty slot is, and the voice was stopped rather than left ringing the note before.
            return;
        }
        if (channel.IsIdle)
        {
            // Nothing sounding to put a level or an effect on.
            return;
        }
        if (cell.HasVolume)
        {
            channel.Gain = (cell.Volume * GainUnity) / MaxVolume;
        }

        channel.CellEffect = 0;
        channel.CellParam = 0;
        channel.EffectTick = 0;
        switch (cell.Effect)
        {
            case MusicEffect.Arpeggio:
            case MusicEffect.Cut:
                channel.CellEffect = (int)cell.Effect;
                channel.CellParam = cell.Param;
                break;
            case MusicEffect.Slide when glide:
                channel.CellEffect = (int)MusicEffect.Slide;
                channel.CellParam = cell.Param;
                channel.GlideFrom = channel.PitchOffset;
                channel.GlideTo = NoteOffset(cell.Note, instrument);
                break;
        }
    }

    /// <summary>
    /// Starts an instrument's slot on a voice at the cell's note. False when the instrument names
    /// an empty slot, in which case the voice is stopped: "the slot is empty" has to mean the
    /// same thing here as it means to <see cref="PlaySfx(int, int)"/>, or silence would have two
    /// states.
    /// </summary>
    private bool StartCellNote(ref AudioChannel channel, int note, MusicInstrument instrument)
    {
        SfxSlot slot = _bank.GetSfx(instrument.Slot);
        if (slot.IsEmpty)
        {
            channel.Stop();
            return false;
        }
        channel.Start(instrument.Slot, NoteTable.ToPitch(slot[0].Note), fromMusic: true);
        channel.PitchOffset = NoteOffset(note, instrument);
        channel.SpeedOverride = instrument.Speed;
        if (instrument.Once)
        {
            // "Play the slot once, loop and all ignored" is exactly a segment over the whole slot
            // (ADR-037), so it is spelled as one instead of as a second rule in AdvanceChannel.
            channel.SegmentEnd = slot.Length;
        }
        return true;
    }

    /// <summary>
    /// How far the slot's steps move, in 1/256 semitones: the cell's note minus the instrument's
    /// root, plus the transposition the order entry asked for. Both are plain semitone counts, so
    /// this is one multiply and no table.
    /// </summary>
    private int NoteOffset(int note, MusicInstrument instrument) =>
        (note - instrument.Root + _musicTranspose) * NoteTable.PitchesPerSemitone;

    /// <summary>
    /// What happens when a pattern ends: stop, jump, loop back, or fall through to the next
    /// order entry. Stop is checked first so an entry carrying both flags ends the song rather
    /// than looping forever — the reading a composer who wrote both almost certainly meant — and
    /// an explicit jump beats the remembered loop start for the same reason. Running past the
    /// last entry stops too, so a song that forgot its flags ends instead of wrapping to its own
    /// start.
    /// </summary>
    private void NextOrderEntry()
    {
        if (_musicPreview)
        {
            // PreviewPattern plays one pattern and stops: the order belongs to the song, not to
            // the row the editor is auditioning.
            StopMusic();
            return;
        }
        MusicFlags flags = _bank.Song.Order(_musicOrderIndex).Flags;
        if ((flags & MusicFlags.Stop) != 0)
        {
            StopMusic();
            return;
        }
        if ((flags & MusicFlags.Jump) != 0)
        {
            StartOrder(_bank.Song.Order(_musicOrderIndex).Target);
            return;
        }
        if ((flags & MusicFlags.LoopEnd) != 0)
        {
            StartOrder(_musicLoopStart);
            return;
        }
        int next = _musicOrderIndex + 1;
        if (next >= MusicEntryCount)
        {
            StopMusic();
            return;
        }
        StartOrder(next);
    }
}
