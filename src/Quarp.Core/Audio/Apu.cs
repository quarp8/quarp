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
    public void PlayMusic(int pattern = -1)
    {
        if (pattern < 0)
        {
            StopMusic();
            return;
        }
        if (pattern >= AudioBank.PatternCount)
        {
            return;
        }
        _musicPlaying = true;
        _musicLoopStart = pattern;
        StartPattern(pattern);
    }

    /// <summary>Stops the music and silences the channels it was driving. Channels the cartridge started keep playing.</summary>
    public void StopMusic()
    {
        _musicPlaying = false;
        _musicPattern = NoPattern;
        _musicTick = 0;
        _musicLength = 0;
        for (int i = 0; i < _channels.Length; i++)
        {
            if (_channels[i].FromMusic)
            {
                _channels[i].Stop();
            }
        }
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
        uint increment = NoteTable.Increment(Pitch(slot, in channel, step));

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
        int speed = slot.Speed;
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

    /// <summary>The amplitude a step sounds at on this tick, 0..<see cref="PeakAmplitude"/>.</summary>
    private static int Amplitude(SfxSlot slot, in AudioChannel channel, SfxStep step)
    {
        int amplitude = step.Volume * VolumeStep;
        int speed = slot.Speed;
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
        _musicTick++;
        if (_musicTick >= _musicLength)
        {
            NextPattern();
        }
    }

    private void AdvanceChannel(ref AudioChannel channel)
    {
        SfxSlot slot = _bank.GetSfx(channel.SfxId);
        channel.Age = (channel.Age + 1) & AgeMask;
        channel.StepTick++;
        if (channel.StepTick < slot.Speed)
        {
            return;
        }

        channel.StepTick = 0;
        channel.PreviousPitch = NoteTable.ToPitch(slot[channel.Step].Note);
        int next = channel.Step + 1;
        if (slot.Loops && next >= slot.LoopEnd)
        {
            next = slot.LoopStart;
        }
        if (next >= slot.Length)
        {
            channel.Stop();
            return;
        }
        channel.Step = next;
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
    /// Puts a pattern on the channels. A channel the cartridge is using is left alone — the
    /// music misses that voice for this pattern and picks it up at the next one, which is how
    /// a theme ducks under a sound effect without either of them being scheduled against the
    /// other.
    ///
    /// <para><b>How long the pattern lasts: the longest of its active slots, and never less
    /// than <see cref="MinPatternTicks"/>.</b> The longest rather than the shortest, so a voice
    /// is never cut off mid-phrase by a shorter one beside it; short slots simply fall silent
    /// and wait, which is what a tracker does when one channel's instrument ends early. The
    /// length counts every active slot of the pattern, <em>including</em> a voice this call
    /// skipped because the cartridge holds that channel — song timing must not depend on what
    /// sound effects the game happened to be playing, or a rewound run could turn its patterns
    /// over at different ticks. A pattern with nothing active is a rest of exactly
    /// <see cref="MinPatternTicks"/> ticks and then the song goes on: silence is a section, not
    /// the end of the piece, which is what the <see cref="MusicFlags.Stop"/> flag is for.</para>
    /// </summary>
    private void StartPattern(int index)
    {
        _musicPattern = index;
        _musicTick = 0;
        MusicPattern pattern = _bank.GetPattern(index);
        if ((pattern.Flags & MusicFlags.LoopStart) != 0)
        {
            _musicLoopStart = index;
        }

        int length = 0;
        for (int i = 0; i < ChannelCount; i++)
        {
            int sfx = pattern[i];
            ref AudioChannel channel = ref _channels[i];
            if (sfx < 0 || _bank.GetSfx(sfx).IsEmpty)
            {
                // Nothing to play on this voice — including a slot the author left empty, which
                // has to behave the same way Sfx() on it does, or "silent" and "silent" would
                // be two different states.
                if (channel.FromMusic)
                {
                    channel.Stop();
                }
                continue;
            }
            SfxSlot slot = _bank.GetSfx(sfx);
            if (slot.LengthTicks > length)
            {
                length = slot.LengthTicks;
            }
            if (channel.IsIdle || channel.FromMusic)
            {
                StartSfx(ref channel, sfx, fromMusic: true);
            }
        }

        _musicLength = Math.Max(length, MinPatternTicks);
    }

    /// <summary>
    /// What happens when a pattern ends: stop, loop back, or fall through to the next index.
    /// Stop is checked first so a pattern carrying both flags ends the song rather than looping
    /// forever — the reading a composer who wrote both almost certainly meant. Running past
    /// pattern 63 stops too, so a song that forgot its flags ends instead of wrapping to its
    /// own start.
    /// </summary>
    private void NextPattern()
    {
        MusicFlags flags = _bank.GetPattern(_musicPattern).Flags;
        if ((flags & MusicFlags.Stop) != 0)
        {
            StopMusic();
            return;
        }
        if ((flags & MusicFlags.LoopEnd) != 0)
        {
            StartPattern(_musicLoopStart);
            return;
        }
        int next = _musicPattern + 1;
        if (next >= AudioBank.PatternCount)
        {
            StopMusic();
            return;
        }
        StartPattern(next);
    }
}
