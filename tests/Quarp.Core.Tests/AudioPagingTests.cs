using Quarp.Api;
using Quarp.Core.Audio;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// <c>DataToSfx</c> and <c>DataToMusic</c> — sound paging, ADR-036. The whole point of the pair
/// is that a game with more sound than fits in one cartridge can carry the rest in data banks
/// and swap a table in at a level boundary, so what has to be proved is: paged sound is
/// <em>the same</em> sound the pipeline would have loaded; a second page replaces the first
/// outright rather than merging with it; a bank that cannot supply a whole payload changes
/// nothing at all; the chip goes quiet rather than stepping through a slot that was replaced
/// under it; and — the one that would be a determinism hole rather than a wrong noise — a
/// rewind resimulates from the bank the run <em>started</em> with, not from the episode the
/// last run finished on.
/// </summary>
public class AudioPagingTests
{
    /// <summary>A cartridge that does whatever the test tells it to, from where a cart lives.</summary>
    private sealed class ScriptCart : Cartridge
    {
        private readonly Action<ScriptCart>? _init;
        private readonly Action<ScriptCart>? _update;

        public ScriptCart(Action<ScriptCart>? init = null, Action<ScriptCart>? update = null)
        {
            _init = init;
            _update = update;
        }

        /// <summary>The tick counter as the cartridge sees it; Init is tick 0.</summary>
        public int Now => Ticks;

        public void PageSfx(int bank, int offset) => DataToSfx(bank, offset);

        public void PageMusic(int bank, int offset) => DataToMusic(bank, offset);

        public void Beep(int id, int channel = -1) => Sfx(id, channel);

        public void Song(int pattern) => Music(pattern);

        public override void Init() => _init?.Invoke(this);

        public override void Update() => _update?.Invoke(this);

        public override void Draw()
        {
        }
    }

    // --- fixtures -------------------------------------------------------------------------

    /// <summary>
    /// One episode's worth of sound: a beep in slot 1, a bass in slot 2, and pattern 0 looping
    /// the bass. <paramref name="baseNote"/> is what makes two episodes audibly different, and
    /// <paramref name="withBass"/> is what makes "replaced" distinguishable from "merged" —
    /// an episode without a bass leaves slot 2 empty, and a merge would leave the previous
    /// episode's bass sounding through it.
    /// </summary>
    private static AudioBank Episode(int baseNote, bool withBass = true)
    {
        var bank = new AudioBank();

        SfxSlot beep = bank.GetSfx(1);
        beep.Speed = 3;
        beep.Length = 4;
        for (int i = 0; i < 4; i++)
        {
            beep[i] = new SfxStep(baseNote + i, Waveform.Pulse25, 6);
        }

        if (withBass)
        {
            SfxSlot bass = bank.GetSfx(2);
            bass.Speed = 6;
            bass.Length = 8;
            for (int i = 0; i < 8; i++)
            {
                bass[i] = new SfxStep(baseNote - 12 + (i % 3), Waveform.Triangle, 5);
            }
        }

        bank.SetPattern(0, new MusicPattern(-1, -1, 2, -1, MusicFlags.LoopStart | MusicFlags.LoopEnd));
        return bank;
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

    /// <summary>
    /// Both payloads back to back, which is how a port actually packs an episode: one bank,
    /// the SFX table at 0 and the music table at <see cref="AudioBank.SfxPayloadSize"/>.
    /// </summary>
    private static byte[] EpisodeBank(AudioBank bank)
    {
        byte[] sfx = SfxPayload(bank);
        byte[] music = MusicPayload(bank);
        byte[] together = new byte[sfx.Length + music.Length];
        sfx.CopyTo(together, 0);
        music.CopyTo(together, sfx.Length);
        return together;
    }

    private const int MusicAt = AudioBank.SfxPayloadSize;

    private static byte[][] Banks(params (int Index, byte[] Bytes)[] banks)
    {
        var set = new byte[VirtualConsole.DataBankCount][];
        for (int i = 0; i < set.Length; i++)
        {
            set[i] = Array.Empty<byte>();
        }
        foreach ((int index, byte[] bytes) in banks)
        {
            set[index] = bytes;
        }
        return set;
    }

    /// <summary>Beeps every 17 ticks, so a run exercises slot 1 as well as the song.</summary>
    private static void Beeper(ScriptCart cart)
    {
        if (cart.Now % 17 == 0)
        {
            cart.Beep(1);
        }
    }

    /// <summary>Runs a cart and returns the per-tick audio digests — the thing that must match.</summary>
    private static List<ulong> Audio(VirtualConsole console, Cartridge cart, int ticks)
    {
        console.AttachCart(cart);
        var digests = new List<ulong>(ticks);
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(default);
            digests.Add(FrameHash.Compute(console.AudioBlock));
        }
        return digests;
    }

    /// <summary>The digest of a tick nobody made a sound in — the negative control's yardstick.</summary>
    private static ulong Silence => FrameHash.Compute(new AudioBlock());

    // --- the same sound, whichever door it came through -----------------------------------

    /// <summary>
    /// The headline claim: a payload paged out of a data bank is the same sound as the same
    /// payload handed over by the cartridge pipeline. Sample for sample, for two hundred ticks,
    /// through both tables — because "close enough" is not a thing the PCM hash accepts.
    /// </summary>
    [Fact]
    public void APagedPayloadPlaysExactlyLikeOneLoadedByThePipeline()
    {
        AudioBank episode = Episode(40);
        byte[] sfx = SfxPayload(episode);
        byte[] music = MusicPayload(episode);

        var loaded = new VirtualConsole(ConsoleProfile.Profile8, null, null, null, sfx, music);
        var paged = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, null, null, Banks((3, EpisodeBank(episode))));

        List<ulong> fromPipeline = Audio(loaded, new ScriptCart(c => c.Song(0), Beeper), 200);
        List<ulong> fromBank = Audio(
            paged,
            new ScriptCart(
                c =>
                {
                    c.PageSfx(3, 0);
                    c.PageMusic(3, MusicAt);
                    c.Song(0);
                },
                Beeper),
            200);

        Assert.Equal(fromPipeline, fromBank);

        // Negative control: the comparison above would also hold if both runs were silent.
        Assert.Contains(fromBank, digest => digest != Silence);
    }

    /// <summary>
    /// Half a payload is not half a sound: the two calls take a whole table each, so the offset
    /// is the only place a caller can be off by one, and a bank that carries both tables back
    /// to back — which is how a port packs an episode — has to work.
    /// </summary>
    [Fact]
    public void TheMusicTableIsReadFromItsOwnOffsetInTheSameBank()
    {
        AudioBank episode = Episode(40);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, null, null, Banks((0, EpisodeBank(episode))));

        List<ulong> digests = Audio(
            console,
            new ScriptCart(c =>
            {
                c.PageSfx(0, 0);
                c.PageMusic(0, MusicAt);
                c.Song(0);
            }),
            60);

        // The pattern table came from offset 4352 of the same bank, so the sequencer is running
        // a pattern that only exists there; channel 2 is the voice that pattern names.
        Assert.True(console.Apu.IsMusicPlaying);
        Assert.True(console.Apu.IsChannelMusic(2));
        Assert.Contains(digests, digest => digest != Silence);
    }

    // --- a second page replaces the first --------------------------------------------------

    /// <summary>
    /// Loading twice leaves the second table and nothing of the first. The second episode has
    /// no bass at all, so a merge would be audible: slot 2 would keep sounding the first
    /// episode's bass under the second episode's beeps, and the digests would follow neither
    /// reference.
    /// </summary>
    [Fact]
    public void ASecondPageReplacesTheWholeTableRatherThanMergingWithIt()
    {
        AudioBank first = Episode(40);
        AudioBank second = Episode(20, withBass: false);

        List<ulong> Reference(AudioBank bank) => Audio(
            new VirtualConsole(ConsoleProfile.Profile8, null, null, null, SfxPayload(bank), MusicPayload(bank)),
            new ScriptCart(c => c.Song(0), BeepBoth),
            150);

        List<ulong> onlySecond = Reference(second);
        List<ulong> onlyFirst = Reference(first);

        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, null, null,
            Banks((0, EpisodeBank(first)), (1, EpisodeBank(second))));
        List<ulong> bothInTurn = Audio(
            console,
            new ScriptCart(
                c =>
                {
                    c.PageSfx(0, 0);
                    c.PageMusic(0, MusicAt);
                    c.PageSfx(1, 0);
                    c.PageMusic(1, MusicAt);
                    c.Song(0);
                },
                BeepBoth),
            150);

        Assert.Equal(onlySecond, bothInTurn);
        Assert.NotEqual(onlyFirst, bothInTurn);
        Assert.Contains(onlySecond, digest => digest != Silence);
        Assert.Contains(onlyFirst, digest => digest != Silence);

        // Slot 2 is empty in the second episode, so nothing may answer a request for it.
        Assert.False(console.Apu.IsChannelBusy(0));

        // Slot 1 exists in both episodes with different notes, so a run that took the wrong
        // table sounds wrong rather than silent; slot 2 exists only in the first, so a run that
        // merged the two sounds a bass no episode asked for on channel 0.
        static void BeepBoth(ScriptCart cart)
        {
            if (cart.Now % 17 == 0)
            {
                cart.Beep(1, 1);
                cart.Beep(2, 0);
            }
        }
    }

    // --- a bank that cannot supply a whole payload -----------------------------------------

    /// <summary>
    /// The decision of ADR-036 that <see cref="VirtualConsole.DataToGfx"/> answers the other
    /// way: a short, empty or absent bank leaves the table exactly as it was. Not zero-filled,
    /// not partly copied — a payload is one record, and three quarters of it is a slot torn in
    /// half rather than a smaller bank of sound.
    /// </summary>
    [Fact]
    public void ABankTooShortForAWholePayloadLeavesTheTableAlone()
    {
        AudioBank installed = Episode(40);
        AudioBank other = Episode(20);
        byte[] full = EpisodeBank(other);
        byte[] short1 = full[..(AudioBank.SfxPayloadSize - 1)];

        List<ulong> untouched = Audio(
            new VirtualConsole(
                ConsoleProfile.Profile8, null, null, null, SfxPayload(installed), MusicPayload(installed)),
            new ScriptCart(c => c.Song(0), Beeper),
            150);

        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, SfxPayload(installed), MusicPayload(installed),
            Banks((5, short1), (7, full)));

        List<ulong> afterRefusals = Audio(
            console,
            new ScriptCart(
                c =>
                {
                    c.PageSfx(5, 0);                 // the bank is one byte short
                    c.PageSfx(6, 0);                 // an empty bank
                    c.PageSfx(64, 0);                // a bank number outside 0-63
                    c.PageSfx(-1, 0);                // ...and outside it the other way
                    c.PageSfx(7, -1);                // a negative offset
                    // one byte short of a whole SFX table at the end of a bank that is long
                    // enough overall: the length is not what is checked, the fit is.
                    c.PageSfx(7, full.Length - AudioBank.SfxPayloadSize + 1);
                    c.PageMusic(7, full.Length - 1); // a music table one byte from the end
                    c.Song(0);
                },
                Beeper),
            150);

        Assert.Equal(untouched, afterRefusals);
        Assert.Contains(untouched, digest => digest != Silence);
    }

    /// <summary>
    /// A refusal refuses everything, silencing included. A cartridge that names the wrong bank
    /// at a level boundary keeps the sound it had — audibly the previous level's, which is a
    /// bug someone can hear and chase — rather than going quiet, which looks exactly like a
    /// level that was authored without music.
    /// </summary>
    [Fact]
    public void ARefusedPageDoesNotSilenceWhatWasPlaying()
    {
        AudioBank installed = Episode(40);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, SfxPayload(installed), MusicPayload(installed),
            Banks((5, new byte[AudioBank.SfxPayloadSize - 1])));

        Audio(
            console,
            new ScriptCart(
                c =>
                {
                    c.Song(0);
                    c.Beep(1, 0);
                },
                c =>
                {
                    if (c.Now == 3)
                    {
                        c.PageSfx(5, 0);
                        c.PageSfx(9, 0);
                    }
                }),
            4);

        Assert.True(console.Apu.IsMusicPlaying);
        Assert.True(console.Apu.IsChannelBusy(0));
        Assert.True(console.Apu.IsChannelMusic(2));
    }

    // --- what happens to sound already playing ---------------------------------------------

    /// <summary>
    /// A page silences the chip: four channels stopped and the music stopped, exactly what
    /// <c>Sfx(-1)</c> plus <c>Music(-1)</c> would have done first. A channel left running would
    /// be parked on a step index the new slot's length says nothing about, and sliding from a
    /// note the new slot never contained.
    /// </summary>
    [Fact]
    public void PagingSilencesEveryChannelAndStopsTheMusic()
    {
        AudioBank episode = Episode(40);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, SfxPayload(episode), MusicPayload(episode),
            Banks((0, EpisodeBank(Episode(20)))));

        bool soundedBefore = false;
        Audio(
            console,
            new ScriptCart(
                c =>
                {
                    c.Song(0);
                    c.Beep(1, 0);
                },
                c =>
                {
                    if (c.Now == 5)
                    {
                        soundedBefore = console.Apu.IsChannelBusy(0) && console.Apu.IsMusicPlaying;
                        c.PageSfx(0, 0);
                    }
                }),
            5);

        Assert.True(soundedBefore);
        Assert.False(console.Apu.IsMusicPlaying);
        for (int channel = 0; channel < Apu.ChannelCount; channel++)
        {
            Assert.False(console.Apu.IsChannelBusy(channel));
        }
        Assert.Equal(Silence, FrameHash.Compute(console.AudioBlock));
    }

    /// <summary>The music half stops the sequencer for the same reason, and says so separately.</summary>
    [Fact]
    public void PagingTheMusicTableStopsTheSequencer()
    {
        AudioBank episode = Episode(40);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, SfxPayload(episode), MusicPayload(episode),
            Banks((0, EpisodeBank(Episode(20)))));

        Audio(
            console,
            new ScriptCart(
                c => c.Song(0),
                c =>
                {
                    if (c.Now == 5)
                    {
                        c.PageMusic(0, MusicAt);
                    }
                }),
            5);

        Assert.False(console.Apu.IsMusicPlaying);
        Assert.Equal(Apu.NoPattern, console.Apu.CurrentPattern);
    }

    // --- determinism ------------------------------------------------------------------------

    /// <summary>
    /// The reason the sound bank grew a boot image. A rewind cold-boots and resimulates from
    /// tick 0, and the cartridge pages its second episode in only at tick 10 — so between tick
    /// 1 and tick 10 the resimulation must hear the bank the <em>pipeline</em> loaded, not
    /// whichever episode the previous pass finished on. Without
    /// <c>VirtualConsole.ResetAssets</c> putting the bank back, those nine ticks come back in
    /// the wrong episode and the PCM diverges — a rewind that lands in the right place playing
    /// the wrong notes.
    /// </summary>
    [Fact]
    public void ARewindResimulatesFromTheBankTheRunStartedWith()
    {
        AudioBank shipped = Episode(40);
        byte[] second = EpisodeBank(Episode(20));

        TimeMachine Machine(int pageAt) => new(
            ConsoleProfile.Profile8,
            new ScriptCart(
                c => c.Song(0),
                c =>
                {
                    if (c.Now == pageAt)
                    {
                        c.PageSfx(0, 0);
                        c.PageMusic(0, MusicAt);
                        c.Song(0);
                    }
                    Beeper(c);
                }),
            new ReplayHeader(ReplayHeader.UnknownIdentity, seed: 0, ReadOnlySpan<int>.Empty),
            new ReplayLog(),
            sfx: SfxPayload(shipped),
            music: MusicPayload(shipped),
            dataBanks: Banks((0, second)));

        TimeMachine machine = Machine(pageAt: 10);
        machine.Boot();
        var straight = new List<ulong>();
        for (int i = 0; i < 40; i++)
        {
            machine.Advance(default);
            straight.Add(FrameHash.Compute(machine.Console.AudioBlock));
        }

        machine.SeekTo(0);
        var rewound = new List<ulong>();
        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(1, machine.ReplayForward(1));
            rewound.Add(FrameHash.Compute(machine.Console.AudioBlock));
        }

        Assert.Equal(straight, rewound);

        // Negative control: the two episodes really do sound different before tick 10, so the
        // equality above is a claim about the boot image and not about two identical banks.
        // Without ResetAssets restoring the bank, ticks 1-9 of the rewound pass would carry
        // exactly these digests instead of the ones they carry.
        TimeMachine fromTheStart = Machine(pageAt: 1);
        fromTheStart.Boot();
        var early = new List<ulong>();
        for (int i = 0; i < 9; i++)
        {
            fromTheStart.Advance(default);
            early.Add(FrameHash.Compute(fromTheStart.Console.AudioBlock));
        }
        Assert.NotEqual(straight[0], early[0]);
        Assert.NotEqual(straight[8], early[8]);
    }

    /// <summary>
    /// A restart is the same cold boot as a rewind and has to undo paging just as completely;
    /// this is the console-level statement of it, without a replay log in the way.
    /// </summary>
    [Fact]
    public void ResetAssetsPutsThePagedTableBack()
    {
        AudioBank shipped = Episode(40);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, SfxPayload(shipped), MusicPayload(shipped),
            Banks((0, EpisodeBank(Episode(20)))));

        List<ulong> before = Audio(console, new ScriptCart(c => c.Song(0), Beeper), 60);

        // Page the other episode in, then cold-boot the way TimeMachine.Boot does.
        Audio(
            console,
            new ScriptCart(c =>
            {
                c.PageSfx(0, 0);
                c.PageMusic(0, MusicAt);
            }),
            1);
        console.ResetAssets();

        List<ulong> after = Audio(console, new ScriptCart(c => c.Song(0), Beeper), 60);

        Assert.Equal(before, after);
        Assert.Contains(before, digest => digest != Silence);
    }

    /// <summary>
    /// The acceptance criterion of the whole change in miniature: a cartridge that never calls
    /// either new member sounds and looks exactly as it did before data banks were even
    /// installed. The twelve pinned demo hashes say the same thing at full size; this says it
    /// where a failure is readable.
    /// </summary>
    [Fact]
    public void ACartridgeThatNeverPagesIsUnaffectedByHavingBanks()
    {
        AudioBank episode = Episode(40);
        byte[] sfx = SfxPayload(episode);
        byte[] music = MusicPayload(episode);

        var without = new VirtualConsole(ConsoleProfile.Profile8, null, null, null, sfx, music);
        var with = new VirtualConsole(
            ConsoleProfile.Profile8, null, null, null, sfx, music,
            Banks((0, EpisodeBank(Episode(20))), (63, new byte[] { 1, 2, 3 })));

        List<ulong> quiet = Audio(without, new ScriptCart(c => c.Song(0), Beeper), 200);
        List<ulong> loaded = Audio(with, new ScriptCart(c => c.Song(0), Beeper), 200);

        Assert.Equal(quiet, loaded);
        Assert.Contains(quiet, digest => digest != Silence);
    }

    /// <summary>
    /// A data bank is arbitrary bytes — nothing validates <c>data/NN.bin</c> the way the loader
    /// validates <c>sfx.bin</c> — so paging junk must be defined rather than loud. Every field
    /// clamps on the way in (undefined waveform to pulse 50 %, effect to none, speed 0 to 1),
    /// which is the second line of defence <c>SfxStep</c> was written to be, and the result is
    /// the same on every machine: the same bytes twice give the same PCM.
    /// </summary>
    [Fact]
    public void PagingArbitraryBytesIsDefinedRatherThanAnException()
    {
        byte[] junk = new byte[AudioBank.SfxPayloadSize + AudioBank.MusicPayloadSize];
        for (int i = 0; i < junk.Length; i++)
        {
            junk[i] = (byte)(i * 37);
        }

        List<ulong> Play() => Audio(
            new VirtualConsole(ConsoleProfile.Profile8, null, null, null, null, null, Banks((0, junk))),
            new ScriptCart(
                c =>
                {
                    c.PageSfx(0, 0);
                    c.PageMusic(0, MusicAt);
                    c.Song(0);
                },
                Beeper),
            120);

        List<ulong> once = Play();
        Assert.Equal(once, Play());
        Assert.Contains(once, digest => digest != Silence);
    }
}
