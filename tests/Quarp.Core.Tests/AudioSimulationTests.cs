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
    private static byte[] MusicPayload(AudioBank bank)
    {
        byte[] payload = new byte[AudioBank.MusicPayloadSize];
        for (int index = 0; index < AudioBank.PatternCount; index++)
        {
            MusicPattern pattern = bank.GetPattern(index);
            for (int channel = 0; channel < MusicPattern.ChannelCount; channel++)
            {
                payload[(index * MusicPattern.ChannelCount) + channel] = pattern.ChannelByte(channel);
            }
            payload[AudioBank.MusicChannelTableSize + index] = (byte)pattern.Flags;
        }
        return payload;
    }

    [Fact]
    public void AWrongSizedPayloadIsRejectedRatherThanPlayedAsFarAsItGoes()
    {
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        Assert.Throws<ArgumentException>(() => console.LoadAudio(new byte[AudioBank.SfxPayloadSize - 1], null));
        Assert.Throws<ArgumentException>(() => console.LoadAudio(null, new byte[7]));
    }
}
