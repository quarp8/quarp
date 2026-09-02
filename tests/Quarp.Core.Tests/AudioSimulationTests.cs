using Quarp.Api;
using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Audio as <em>simulation</em> state rather than presentation — the claim the whole milestone
/// rests on. Three things have to hold and each is tested with its own failure demonstrated:
/// a tick that skips Draw still makes exactly the audio a drawn tick would; a re-booted console
/// replays the same sound sample for sample; and the audio hash actually notices a change the
/// frame hash cannot see.
///
/// <para>That last one is the milestone's acceptance criterion in miniature, and it is here
/// because of what M2 taught: the flagship determinism check of that milestone was green for
/// weeks while comparing something that had stopped varying. An audio hash that matched because
/// nothing was ever hashed would look exactly like an audio hash that matched because the chip
/// is deterministic.</para>
/// </summary>
public class AudioSimulationTests
{
    /// <summary>Beeps on a schedule and draws something, so a run exercises both halves of a tick.</summary>
    private sealed class BeepingCart : Cartridge
    {
        private readonly int _period;
        private readonly int _sfx;

        public BeepingCart(int period, int sfx = 1)
        {
            _period = period;
            _sfx = sfx;
        }

        public override void Init() => Music(0);

        public override void Update()
        {
            if (Ticks % _period == 0)
            {
                Sfx(_sfx);
            }
        }

        public override void Draw()
        {
            Cls(1);
            RectFill(Ticks % 100, 10, 8, 8, 7);
        }
    }

    private static AudioBank Bank()
    {
        var bank = new AudioBank();

        SfxSlot beep = bank.GetSfx(1);
        beep.Speed = 2;
        beep.Length = 4;
        beep[0] = new SfxStep(45, Waveform.Pulse25, 6);
        beep[1] = new SfxStep(50, Waveform.Pulse25, 6, NoteEffect.Slide);
        beep[2] = new SfxStep(52, Waveform.Triangle, 5, NoteEffect.Vibrato);
        beep[3] = new SfxStep(52, Waveform.Noise, 4, NoteEffect.FadeOut);

        SfxSlot bass = bank.GetSfx(2);
        bass.Speed = 6;
        bass.Length = 8;
        for (int i = 0; i < 8; i++)
        {
            bass[i] = new SfxStep(12 + (i % 3), Waveform.Triangle, 5);
        }

        bank.SetPattern(0, new MusicPattern(-1, -1, 2, -1, MusicFlags.LoopStart | MusicFlags.LoopEnd));
        return bank;
    }

    private static VirtualConsole Console(AudioBank? bank = null)
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.LoadAudio(bank ?? Bank());
        return console;
    }

    /// <summary>Runs a cart and returns the per-tick (frame, audio) hashes.</summary>
    private static (List<ulong> Frames, List<ulong> Audio) Run(
        int ticks, AudioBank? bank = null, int period = 17, bool draw = true)
    {
        VirtualConsole console = Console(bank);
        console.AttachCart(new BeepingCart(period));
        var frames = new List<ulong>();
        var audio = new List<ulong>();
        for (int i = 0; i < ticks; i++)
        {
            if (draw)
            {
                console.Tick(default);
            }
            else
            {
                console.TickUpdateOnly(default);
            }
            frames.Add(FrameHash.Compute(console.Framebuffer));
            audio.Add(FrameHash.Compute(console.AudioBlock));
        }
        return (frames, audio);
    }

    [Fact]
    public void ATickProducesExactlyOneBlockOfEightHundredSamples()
    {
        VirtualConsole console = Console();
        console.AttachCart(new BeepingCart(1));
        Assert.Equal(AudioBlock.SamplesPerTick, console.AudioBlock.Samples.Length);
        console.Tick(default);
        Assert.Equal(AudioBlock.SamplesPerTick, console.AudioBlock.Samples.Length);
    }

    [Fact]
    public void BeforeTheFirstTickTheConsoleIsSilent()
    {
        // Init is tick 0 and draws no frame; by the same rule it produces no audio block. A
        // sound started in Init is heard from tick 1, alongside the first frame.
        VirtualConsole console = Console();
        console.AttachCart(new BeepingCart(1));
        Assert.All(console.AudioBlock.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void SuppressingDrawDoesNotChangeASingleSample()
    {
        // The rewind path. TimeMachine resimulates thousands of ticks with Draw suppressed and
        // draws only the landing frame; if audio rode along with Draw, a rewound game would come
        // back in the right place playing the wrong note.
        (_, List<ulong> drawn) = Run(200);
        (_, List<ulong> silentlyRun) = Run(200, draw: false);
        Assert.Equal(drawn, silentlyRun);
        Assert.True(drawn.Count == 200);
    }

    [Fact]
    public void AndThoseHashesAreNotAllTheSameToBeginWith()
    {
        // The negative control for the test above. Comparing two lists of identical silence
        // would pass whether or not the chip was running at all.
        (_, List<ulong> audio) = Run(200);
        Assert.True(audio.Distinct().Count() > 20,
            $"only {audio.Distinct().Count()} distinct audio hashes in 200 ticks — is anything playing?");
        Assert.DoesNotContain(FrameHash.Compute(new AudioBlock()), audio.Take(8));
    }

    [Fact]
    public void TwoRunsOfTheSameCartridgeProduceTheSameSoundTickForTick()
    {
        (List<ulong> framesA, List<ulong> audioA) = Run(300);
        (List<ulong> framesB, List<ulong> audioB) = Run(300);
        Assert.Equal(framesA, framesB);
        Assert.Equal(audioA, audioB);
    }

    [Fact]
    public void AttachingACartridgeAgainRewindsTheChipToSilence()
    {
        // What Boot() does on every rewind and every hot reload: the console's runtime reset has
        // to take the APU with it, or a resimulation would start with channels mid-note.
        VirtualConsole console = Console();
        console.AttachCart(new BeepingCart(1));
        for (int i = 0; i < 30; i++)
        {
            console.Tick(default);
        }
        ulong midRun = FrameHash.Compute(console.AudioBlock);

        console.AttachCart(new BeepingCart(1));
        Assert.All(console.AudioBlock.Samples, s => Assert.Equal(0, s));
        Assert.False(console.Apu.IsChannelBusy(0));

        // ...and running the same 30 ticks again lands on the same sound.
        for (int i = 0; i < 30; i++)
        {
            console.Tick(default);
        }
        Assert.Equal(midRun, FrameHash.Compute(console.AudioBlock));
    }

    [Fact]
    public void OneAlteredSfxStepMovesTheAudioHashAndLeavesTheFrameHashAlone()
    {
        // The milestone's acceptance criterion, run here in milliseconds instead of in CI:
        // break one step of one SFX on purpose. If the frame hashes moved, the cartridge is
        // reacting to sound and this test proves nothing; if the audio hashes did not move,
        // audio is not in the golden master at all.
        AudioBank altered = Bank();
        SfxSlot slot = altered.GetSfx(1);
        slot[2] = new SfxStep(slot[2].Note + 5, slot[2].Wave, slot[2].Volume, slot[2].Effect);

        (List<ulong> frames, List<ulong> audio) = Run(200);
        (List<ulong> alteredFrames, List<ulong> alteredAudio) = Run(200, altered);

        Assert.Equal(frames, alteredFrames);
        Assert.NotEqual(audio, alteredAudio);
    }

    [Fact]
    public void SilenceIsFreeButNotSpecial()
    {
        // A cartridge that never makes a sound produces 800 zero samples every tick, and that
        // block hashes to the same constant as an untouched one — the fast path in the mixer
        // takes a different branch and has to reach an identical answer.
        VirtualConsole console = Console();
        console.AttachCart(new SilentCart());
        ulong empty = FrameHash.Compute(new AudioBlock());
        for (int i = 0; i < 10; i++)
        {
            console.Tick(default);
            Assert.Equal(empty, FrameHash.Compute(console.AudioBlock));
        }
    }

    private sealed class SilentCart : Cartridge
    {
        public override void Draw() => Cls(3);
    }

    /// <summary>
    /// Starts two voices, silences one channel at tick 20 and all of them at tick 40 — or does
    /// none of that, which is the control every assertion below is measured against.
    /// </summary>
    private sealed class StoppingCart : Cartridge
    {
        private readonly bool _stops;

        public StoppingCart(bool stops = true) => _stops = stops;

        public override void Update()
        {
            if (Ticks == 1)
            {
                Sfx(2, 0);
                Sfx(2, 1);
            }
            if (!_stops)
            {
                return;
            }
            if (Ticks == 20)
            {
                Sfx(-1, 0);      // one voice
            }
            if (Ticks == 40)
            {
                Sfx(-1);         // and now the rest of them
            }
        }

        public override void Draw() => Cls(2);
    }

    private static List<ulong> RunStopping(bool stops, bool draw)
    {
        VirtualConsole console = Console();
        console.AttachCart(new StoppingCart(stops));
        var audio = new List<ulong>();
        for (int i = 0; i < 60; i++)
        {
            if (draw)
            {
                console.Tick(default);
            }
            else
            {
                console.TickUpdateOnly(default);
            }
            audio.Add(FrameHash.Compute(console.AudioBlock));
        }
        return audio;
    }

    [Fact]
    public void StoppingAChannelIsSimulationStateLikeStartingOne()
    {
        // Sfx(-1, ch) changes what the chip will render for the rest of the run, so it has to be
        // as invisible to Draw as Sfx(id) is: a rewind resimulates with Draw suppressed, and a
        // stop that only happened on drawn ticks would come back as a sound that never ended.
        List<ulong> drawn = RunStopping(stops: true, draw: true);
        Assert.Equal(drawn, RunStopping(stops: true, draw: false));

        // Negative control 1: the stops are audible at all, so the equality above is not the
        // equality of two runs that never made a sound.
        List<ulong> unstopped = RunStopping(stops: false, draw: true);
        Assert.NotEqual(drawn, unstopped);

        // Negative control 2: the silence after tick 40 is the stop's doing and not the sound
        // simply running out — the same tick still sounds on the cartridge that never stops.
        ulong silence = FrameHash.Compute(new AudioBlock());
        Assert.Equal(silence, drawn[44]);          // tick 45
        Assert.NotEqual(silence, unstopped[44]);
        Assert.NotEqual(drawn[24], unstopped[24]); // tick 25: one voice short, not yet silent
        Assert.NotEqual(silence, drawn[24]);
    }

    [Fact]
    public void ARewindLandsOnTheSameSoundAfterAChannelWasStopped()
    {
        // The rewind path of ADR-006: SeekTo cold-boots and resimulates from tick 0 with Draw
        // suppressed. Tick 30 is downstream of the stop at tick 20, so this compares a landing
        // that carries one.
        ulong straight = FrameHash.Compute(Straight(30, stops: true).Console.AudioBlock);

        TimeMachine rewound = Machine(stops: true);
        rewound.Advance(60, default);
        rewound.SeekTo(30);
        Assert.Equal(30, rewound.Tick);
        Assert.Equal(straight, FrameHash.Compute(rewound.Console.AudioBlock));

        // Negative control: the same landing on a cartridge that never stops anything must not
        // match, or the comparison above would hold whether or not the stop was reproduced.
        Assert.NotEqual(straight, FrameHash.Compute(Straight(30, stops: false).Console.AudioBlock));

        static TimeMachine Straight(int ticks, bool stops)
        {
            TimeMachine machine = Machine(stops);
            machine.Advance(ticks, default);
            return machine;
        }

        static TimeMachine Machine(bool stops)
        {
            // Through the constructor's payloads rather than through LoadAudio(bank), so the
            // session is built the way a real replay session is built — and so the bank is in
            // place before the cold boot SeekTo performs, exactly as it is for a .quarp8.
            AudioBank bank = Bank();
            var machine = new TimeMachine(
                ConsoleProfile.Profile8,
                new StoppingCart(stops),
                new ReplayHeader(ReplayHeader.UnknownIdentity, seed: 0, ReadOnlySpan<int>.Empty),
                new ReplayLog(),
                sfx: SfxPayload(bank),
                music: MusicPayload(bank));
            machine.Boot();
            return machine;
        }
    }

    [Fact]
    public void ACartridgeWithNoAudioBankIsSilentRatherThanBroken()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(new BeepingCart(1));
        for (int i = 0; i < 10; i++)
        {
            console.Tick(default);
        }
        Assert.All(console.AudioBlock.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void PayloadsFromTheCartridgePipelineLoadTheSameBankTheStructuredApiDoes()
    {
        // The two doors into the console must reach the same room, or a cart would sound one
        // way from a .quarp8 and another way from a test fixture.
        AudioBank bank = Bank();
        byte[] sfx = SfxPayload(bank);
        byte[] music = MusicPayload(bank);

        var fromBank = new VirtualConsole(ConsoleProfile.Profile8);
        fromBank.LoadAudio(bank);
        var fromBytes = new VirtualConsole(ConsoleProfile.Profile8, null, null, null, sfx, music);

        fromBank.AttachCart(new BeepingCart(17));
        fromBytes.AttachCart(new BeepingCart(17));
        for (int i = 0; i < 120; i++)
        {
            fromBank.Tick(default);
            fromBytes.Tick(default);
            Assert.Equal(
                FrameHash.Compute(fromBank.AudioBlock),
                FrameHash.Compute(fromBytes.AudioBlock));
        }
    }

    /// <summary>Encodes a bank the way docs/AUDIO-FORMAT.md §2 says a cartridge stores it.</summary>
    private static byte[] SfxPayload(AudioBank bank)
    {
        byte[] payload = new byte[AudioBank.SfxPayloadSize];
        for (int id = 0; id < AudioBank.SfxCount; id++)
        {
            SfxSlot slot = bank.GetSfx(id);
            if (slot.IsEmpty)
            {
                continue;
            }
            int header = id * AudioBank.SfxSlotHeaderSize;
            payload[header] = (byte)slot.Speed;
            payload[header + 1] = (byte)slot.Length;
            payload[header + 2] = (byte)slot.LoopStart;
            payload[header + 3] = (byte)slot.LoopEnd;
            for (int step = 0; step < SfxSlot.StepCount; step++)
            {
                SfxStep s = slot[step];
                int word = s.Note | ((int)s.Wave << 6) | (s.Volume << 9) | ((int)s.Effect << 12);
                int at = AudioBank.SfxSlotTableSize + (((id * SfxSlot.StepCount) + step) * AudioBank.SfxStepSize);
                payload[at] = (byte)(word & 0xFF);
                payload[at + 1] = (byte)((word >> 8) & 0xFF);
            }
        }
        return payload;
    }

    /// <summary>Encodes the music half the same way (docs/AUDIO-FORMAT.md §4).</summary>
    private static byte[] MusicPayload(AudioBank bank) => LegacyPatternBank.SongPayload(bank);

    [Fact]
    public void AWrongSizedPayloadIsRejectedRatherThanPlayedAsFarAsItGoes()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        Assert.Throws<ArgumentException>(() => console.LoadAudio(new byte[AudioBank.SfxPayloadSize - 1], null));
        Assert.Throws<ArgumentException>(() => console.LoadAudio(null, new byte[7]));
    }
}
