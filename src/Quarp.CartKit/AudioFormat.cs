using System.Buffers.Binary;
using System.Text;

namespace Quarp.CartKit;

/// <summary>
/// The two binary audio banks of a cartridge — <c>sfx.bin</c> and <c>music.bin</c> — as bytes:
/// their headers, their fixed sizes, the bit layout of one step and every rule the loader
/// enforces. The full specification with a hexdump is docs/AUDIO-FORMAT.md; this type is that
/// document in code, and the two are meant to be read together.
///
/// <para><b>Shape.</b> Both files are a fixed 8-byte header followed by a fixed-size payload:
/// <code>
/// sfx.bin    8 + 256 + 4096 = 4360 bytes    music.bin  8 + 256 + 64 = 328 bytes
///   0   4  magic "QSFX"                       0   4  magic "QMUS"
///   4   2  u16 version                        4   2  u16 version
///   6   1  u8  slot count  (64)               6   1  u8  pattern count (64)
///   7   1  u8  step count  (32)               7   1  u8  channel count (4)
///   8 256  64 slot headers, 4 bytes each      8 256  64 x 4 channel bytes
/// 264 4096 64 x 32 step words (u16 LE)      264  64  64 pattern flag bytes
/// </code>
/// The header is stripped at load time: what reaches <see cref="CartData"/> and, through it,
/// the console is the <b>payload only</b>, the same way <see cref="CartData.Gfx"/> holds decoded
/// pixels rather than PNG chunks. The core then never has to know about magic numbers, and an
/// all-zero payload — the value an absent bank loads as — is a legal, silent bank.</para>
///
/// <para><b>Endianness.</b> The one multi-byte field, the step word, is read and written with
/// <see cref="BinaryPrimitives"/> explicitly, never through <c>BitConverter</c> or a struct
/// overlay: these bytes have to mean the same thing on windows-x64 and linux-arm64, because
/// the payload feeds a synthesizer whose PCM is compared across architectures (M3 work order).</para>
/// </summary>
public static class AudioFormat
{
    /// <summary>Format version written into both banks; see docs/AUDIO-FORMAT.md §7 for the reading rules.</summary>
    public const int Version = 1;

    /// <summary>Bytes before the payload in both files: magic, version, and the two geometry bytes.</summary>
    public const int HeaderSize = 8;

    // --- sfx.bin geometry (SPEC-8 §4: 64 slots x 32 steps) ---

    public const int SfxSlotCount = 64;
    public const int SfxStepCount = 32;

    /// <summary>Bytes of per-slot header: speed, length, loop start, loop end.</summary>
    public const int SfxSlotHeaderSize = 4;

    /// <summary>Bytes per step — one little-endian u16, see <see cref="PackStep"/>.</summary>
    public const int SfxStepSize = 2;

    /// <summary>The slot header table: 64 x 4 = 256 bytes, the payload's table of contents.</summary>
    public const int SfxSlotTableSize = SfxSlotCount * SfxSlotHeaderSize;

    /// <summary>The step table: 64 x 32 x 2 = 4096 bytes.</summary>
    public const int SfxStepTableSize = SfxSlotCount * SfxStepCount * SfxStepSize;

    /// <summary>What the console receives: 4352 bytes, header stripped.</summary>
    public const int SfxPayloadSize = SfxSlotTableSize + SfxStepTableSize;

    /// <summary>Size of <c>sfx.bin</c> on disk — fixed, so any other length is an error.</summary>
    public const int SfxFileSize = HeaderSize + SfxPayloadSize;

    // --- music.bin geometry (SPEC-8 §4: 64 patterns x 4 channels) ---

    public const int MusicPatternCount = 64;
    public const int MusicChannelCount = 4;
    public const int MusicChannelTableSize = MusicPatternCount * MusicChannelCount;
    public const int MusicFlagTableSize = MusicPatternCount;

    /// <summary>What the console receives: 320 bytes, header stripped.</summary>
    public const int MusicPayloadSize = MusicChannelTableSize + MusicFlagTableSize;

    /// <summary>Size of <c>music.bin</c> on disk — fixed.</summary>
    public const int MusicFileSize = HeaderSize + MusicPayloadSize;

    // --- field ranges ---

    /// <summary>Highest note index; 0 is C-2 (65.406 Hz), 63 is D#7 — 64 semitones, 6 bits.</summary>
    public const int MaxNote = 63;

    /// <summary>Waves defined by profile 8: pulse 12.5/25/50, triangle, saw, noise.</summary>
    public const int WaveCount = 6;

    public const int WavePulse12 = 0;
    public const int WavePulse25 = 1;
    public const int WavePulse50 = 2;
    public const int WaveTriangle = 3;
    public const int WaveSaw = 4;
    public const int WaveNoise = 5;

    /// <summary>Volume 0 (silent) to 7 (full) — 3 bits, and 0 is how a rest is spelled.</summary>
    public const int MaxVolume = 7;

    /// <summary>Note effects defined by profile 8, counting "none".</summary>
    public const int EffectCount = 7;

    public const int EffectNone = 0;
    public const int EffectSlide = 1;
    public const int EffectVibrato = 2;
    public const int EffectDrop = 3;
    public const int EffectFadeIn = 4;
    public const int EffectFadeOut = 5;
    public const int EffectArpeggio = 6;

    /// <summary>Bit 6 of a music channel byte: this channel plays in this pattern.</summary>
    public const byte MusicChannelActiveBit = 0x40;

    /// <summary>Bits 0-5 of a music channel byte: which SFX slot it plays.</summary>
    public const byte MusicChannelSlotMask = 0x3F;

    public const byte PatternFlagLoopStart = 0x01;
    public const byte PatternFlagLoopEnd = 0x02;
    public const byte PatternFlagStop = 0x04;

    /// <summary>Every flag bit that has a meaning; the rest must be zero.</summary>
    public const byte PatternFlagMask = PatternFlagLoopStart | PatternFlagLoopEnd | PatternFlagStop;

    /// <summary>File magic of the SFX bank, ASCII "QSFX".</summary>
    public static ReadOnlySpan<byte> SfxMagic => "QSFX"u8;

    /// <summary>File magic of the music bank, ASCII "QMUS".</summary>
    public static ReadOnlySpan<byte> MusicMagic => "QMUS"u8;

    // --- step words ---

    /// <summary>
    /// Packs one step into the u16 that lands in the file. The bit budget is exactly 16 and is
    /// spent as: note 6 (64 semitones), wave 3 (six waves), volume 3 (0-7), effect 3 (seven
    /// values), and one bit left over at the top, which must be zero. That last bit is not
    /// "reserved for later" in the sense of a field a future version may quietly start using —
    /// it exists so that a bank written by a newer Quarp fails loudly here instead of playing
    /// a wrong note (the same argument as bit 7 of a replay's button mask).
    /// </summary>
    public static ushort PackStep(int note, int wave, int volume, int effect)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, MaxNote);
        ArgumentOutOfRangeException.ThrowIfNegative(wave);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(wave, WaveCount);
        ArgumentOutOfRangeException.ThrowIfNegative(volume);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, MaxVolume);
        ArgumentOutOfRangeException.ThrowIfNegative(effect);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(effect, EffectCount);
        return (ushort)(note | (wave << 6) | (volume << 9) | (effect << 12));
    }

    /// <summary>Semitone index 0-63 of a step word.</summary>
    public static int Note(ushort step) => step & 0x3F;

    /// <summary>Wave index of a step word; 0-5 in a validated payload.</summary>
    public static int Wave(ushort step) => (step >> 6) & 0x07;

    /// <summary>Volume 0-7 of a step word; 0 means the step is silent whatever the other fields say.</summary>
    public static int Volume(ushort step) => (step >> 9) & 0x07;

    /// <summary>Note effect of a step word; 0-6 in a validated payload.</summary>
    public static int Effect(ushort step) => (step >> 12) & 0x07;

    // --- payload accessors (offsets are the specification, so they live here and nowhere else) ---

    /// <summary>Byte offset of a slot's 4-byte header inside the SFX payload.</summary>
    public static int SlotHeaderOffset(int slot) => slot * SfxSlotHeaderSize;

    /// <summary>Byte offset of one step word inside the SFX payload.</summary>
    public static int StepOffset(int slot, int step) =>
        SfxSlotTableSize + (slot * SfxStepCount + step) * SfxStepSize;

    /// <summary>Console ticks per step, 1-255; 0 marks an empty slot (see <see cref="SlotLength"/>).</summary>
    public static int SlotSpeed(ReadOnlySpan<byte> sfxPayload, int slot) =>
        sfxPayload[SlotHeaderOffset(slot)];

    /// <summary>Steps actually played, 0-32. Zero means the slot is empty and <c>Sfx(id)</c> does nothing.</summary>
    public static int SlotLength(ReadOnlySpan<byte> sfxPayload, int slot) =>
        sfxPayload[SlotHeaderOffset(slot) + 1];

    /// <summary>First step of the loop; meaningful only when <see cref="SlotLoopEnd"/> is non-zero.</summary>
    public static int SlotLoopStart(ReadOnlySpan<byte> sfxPayload, int slot) =>
        sfxPayload[SlotHeaderOffset(slot) + 2];

    /// <summary>One past the last step of the loop (half-open); 0 means the slot does not loop.</summary>
    public static int SlotLoopEnd(ReadOnlySpan<byte> sfxPayload, int slot) =>
        sfxPayload[SlotHeaderOffset(slot) + 3];

    /// <summary>One step word, little-endian, no allocation.</summary>
    public static ushort Step(ReadOnlySpan<byte> sfxPayload, int slot, int step) =>
        BinaryPrimitives.ReadUInt16LittleEndian(sfxPayload.Slice(StepOffset(slot, step), SfxStepSize));

    /// <summary>Writes a slot header; <paramref name="loopEnd"/> 0 means "no loop".</summary>
    public static void WriteSlotHeader(Span<byte> sfxPayload, int slot, int speed, int length, int loopStart, int loopEnd)
    {
        int offset = SlotHeaderOffset(slot);
        sfxPayload[offset] = (byte)speed;
        sfxPayload[offset + 1] = (byte)length;
        sfxPayload[offset + 2] = (byte)loopStart;
        sfxPayload[offset + 3] = (byte)loopEnd;
    }

    /// <summary>Writes one step word little-endian.</summary>
    public static void WriteStep(Span<byte> sfxPayload, int slot, int step, ushort word) =>
        BinaryPrimitives.WriteUInt16LittleEndian(sfxPayload.Slice(StepOffset(slot, step), SfxStepSize), word);

    /// <summary>The SFX slot a music channel plays in this pattern, or -1 when the channel is silent.</summary>
    public static int PatternChannel(ReadOnlySpan<byte> musicPayload, int pattern, int channel)
    {
        byte value = musicPayload[pattern * MusicChannelCount + channel];
        return (value & MusicChannelActiveBit) == 0 ? -1 : value & MusicChannelSlotMask;
    }

    /// <summary>Section flags of a pattern: loop start, loop end, stop.</summary>
    public static byte PatternFlags(ReadOnlySpan<byte> musicPayload, int pattern) =>
        musicPayload[MusicChannelTableSize + pattern];

    /// <summary>True when no channel plays in this pattern — the end of a song.</summary>
    public static bool PatternIsEmpty(ReadOnlySpan<byte> musicPayload, int pattern)
    {
        for (int channel = 0; channel < MusicChannelCount; channel++)
        {
            if (PatternChannel(musicPayload, pattern, channel) >= 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Sets a music channel; <paramref name="slot"/> below zero makes the channel silent.</summary>
    public static void WritePatternChannel(Span<byte> musicPayload, int pattern, int channel, int slot) =>
        musicPayload[pattern * MusicChannelCount + channel] =
            slot < 0 ? (byte)0 : (byte)(MusicChannelActiveBit | (slot & MusicChannelSlotMask));

    /// <summary>Sets a pattern's section flags.</summary>
    public static void WritePatternFlags(Span<byte> musicPayload, int pattern, byte flags) =>
        musicPayload[MusicChannelTableSize + pattern] = flags;

    // --- files ---

    /// <summary>An all-zero SFX payload: 64 empty slots, which is exactly what silence is.</summary>
    public static byte[] EmptySfxPayload() => new byte[SfxPayloadSize];

    /// <summary>An all-zero music payload: 64 patterns with no active channel.</summary>
    public static byte[] EmptyMusicPayload() => new byte[MusicPayloadSize];

    /// <summary>
    /// Validates <c>sfx.bin</c> and returns its payload. <paramref name="sourceName"/> prefixes
    /// every message (it is what the author sees, so it is the file name, not a full path).
    /// </summary>
    public static byte[] ParseSfxFile(ReadOnlySpan<byte> file, string sourceName)
    {
        ReadHeader(file, SfxMagic, sourceName, SfxFileSize, SfxSlotCount, SfxStepCount, "slots", "steps");
        byte[] payload = file[HeaderSize..].ToArray();
        ValidateSfxPayload(payload, sourceName);
        return payload;
    }

    /// <summary>Validates <c>music.bin</c> and returns its payload.</summary>
    public static byte[] ParseMusicFile(ReadOnlySpan<byte> file, string sourceName)
    {
        ReadHeader(file, MusicMagic, sourceName, MusicFileSize, MusicPatternCount, MusicChannelCount,
            "patterns", "channels");
        byte[] payload = file[HeaderSize..].ToArray();
        ValidateMusicPayload(payload, sourceName);
        return payload;
    }

    /// <summary>Wraps a validated payload into the bytes of <c>sfx.bin</c>.</summary>
    public static byte[] WriteSfxFile(ReadOnlySpan<byte> payload)
    {
        ValidateSfxPayload(payload, "sfx.bin");
        return WriteFile(SfxMagic, SfxSlotCount, SfxStepCount, payload);
    }

    /// <summary>Wraps a validated payload into the bytes of <c>music.bin</c>.</summary>
    public static byte[] WriteMusicFile(ReadOnlySpan<byte> payload)
    {
        ValidateMusicPayload(payload, "music.bin");
        return WriteFile(MusicMagic, MusicPatternCount, MusicChannelCount, payload);
    }

    /// <summary>
    /// Every rule a slot table and a step table have to obey. All of them are cheap, and all of
    /// them exist because the payload reaches a synthesizer that runs 60 times a second and must
    /// not be checking ranges there: what the APU reads has already been proven to be in range.
    /// </summary>
    public static void ValidateSfxPayload(ReadOnlySpan<byte> payload, string sourceName)
    {
        if (payload.Length != SfxPayloadSize)
        {
            throw new CartLoadException(
                $"{sourceName}: SFX payload is {payload.Length} bytes, must be exactly {SfxPayloadSize}.");
        }

        for (int slot = 0; slot < SfxSlotCount; slot++)
        {
            int speed = SlotSpeed(payload, slot);
            int length = SlotLength(payload, slot);
            int loopStart = SlotLoopStart(payload, slot);
            int loopEnd = SlotLoopEnd(payload, slot);

            if (length > SfxStepCount)
            {
                throw new CartLoadException(
                    $"{sourceName}: slot {slot}: length {length}, must be 0..{SfxStepCount}.");
            }
            if (length == 0)
            {
                // An empty slot is the zero record and nothing else, so that "no sfx.bin at all"
                // and "an sfx.bin with this slot unused" are the same bytes and the same identity.
                if (speed != 0 || loopStart != 0 || loopEnd != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: slot {slot}: length 0 marks an empty slot, so speed and loop must be 0 "
                        + $"(got speed {speed}, loop {loopStart}..{loopEnd}).");
                }
                continue;
            }
            if (speed == 0)
            {
                throw new CartLoadException(
                    $"{sourceName}: slot {slot}: speed 0 with length {length}; speed is console ticks per step "
                    + "and must be 1..255.");
            }
            if (loopEnd == 0)
            {
                if (loopStart != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: slot {slot}: loop start {loopStart} with no loop end; a slot that does not "
                        + "loop stores 0 in both loop fields.");
                }
            }
            else if (loopStart >= loopEnd || loopEnd > length)
            {
                throw new CartLoadException(
                    $"{sourceName}: slot {slot}: loop {loopStart}..{loopEnd} is not a half-open range inside "
                    + $"0..{length} (start < end <= length).");
            }
        }

        for (int slot = 0; slot < SfxSlotCount; slot++)
        {
            for (int step = 0; step < SfxStepCount; step++)
            {
                ushort word = Step(payload, slot, step);
                if ((word & 0x8000) != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: slot {slot} step {step}: bit 15 of the step word is reserved and must be 0 "
                        + $"(word 0x{word:x4}).");
                }
                int wave = Wave(word);
                if (wave >= WaveCount)
                {
                    throw new CartLoadException(
                        $"{sourceName}: slot {slot} step {step}: wave {wave}, profile 8 defines 0..{WaveCount - 1} "
                        + $"(word 0x{word:x4}).");
                }
                int effect = Effect(word);
                if (effect >= EffectCount)
                {
                    throw new CartLoadException(
                        $"{sourceName}: slot {slot} step {step}: effect {effect}, profile 8 defines "
                        + $"0..{EffectCount - 1} (word 0x{word:x4}).");
                }
            }
        }
    }

    /// <summary>Every rule a channel table and a flag table have to obey.</summary>
    public static void ValidateMusicPayload(ReadOnlySpan<byte> payload, string sourceName)
    {
        if (payload.Length != MusicPayloadSize)
        {
            throw new CartLoadException(
                $"{sourceName}: music payload is {payload.Length} bytes, must be exactly {MusicPayloadSize}.");
        }

        for (int pattern = 0; pattern < MusicPatternCount; pattern++)
        {
            for (int channel = 0; channel < MusicChannelCount; channel++)
            {
                byte value = payload[pattern * MusicChannelCount + channel];
                if ((value & 0x80) != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: pattern {pattern} channel {channel}: bit 7 is reserved and must be 0 "
                        + $"(byte 0x{value:x2}).");
                }
                if ((value & MusicChannelActiveBit) == 0 && (value & MusicChannelSlotMask) != 0)
                {
                    throw new CartLoadException(
                        $"{sourceName}: pattern {pattern} channel {channel}: the channel is off (bit 6 clear) but "
                        + $"still names slot {value & MusicChannelSlotMask}; a silent channel stores 0x00.");
                }
            }

            byte flags = PatternFlags(payload, pattern);
            if ((flags & ~PatternFlagMask) != 0)
            {
                throw new CartLoadException(
                    $"{sourceName}: pattern {pattern}: flag bits 3-7 are reserved and must be 0 (byte 0x{flags:x2}).");
            }
        }
    }

    private static byte[] WriteFile(ReadOnlySpan<byte> magic, int countA, int countB, ReadOnlySpan<byte> payload)
    {
        byte[] file = new byte[HeaderSize + payload.Length];
        magic.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4, 2), Version);
        file[6] = (byte)countA;
        file[7] = (byte)countB;
        payload.CopyTo(file.AsSpan(HeaderSize));
        return file;
    }

    /// <summary>
    /// The header checks, in the order that produces the most useful message: too short, wrong
    /// magic, unreadable version, wrong geometry, wrong length. Checking the size first would
    /// answer "4360 bytes expected" to someone who handed us a PNG.
    /// </summary>
    private static void ReadHeader(
        ReadOnlySpan<byte> file,
        ReadOnlySpan<byte> magic,
        string sourceName,
        int expectedFileSize,
        int expectedCountA,
        int expectedCountB,
        string nameA,
        string nameB)
    {
        if (file.Length < HeaderSize)
        {
            throw new CartLoadException(
                $"{sourceName}: {file.Length} bytes — too short to be a Quarp audio bank (the header alone is "
                + $"{HeaderSize} bytes).");
        }
        if (!file[..4].SequenceEqual(magic))
        {
            throw new CartLoadException(
                $"{sourceName}: magic is {Describe(file[..4])}, expected \"{Encoding.ASCII.GetString(magic)}\" "
                + "— this is not a Quarp audio bank.");
        }
        int version = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(4, 2));
        if (version == 0)
        {
            throw new CartLoadException($"{sourceName}: format version 0 does not exist.");
        }
        if (version > Version)
        {
            throw new CartLoadException(
                $"{sourceName}: written by a newer Quarp (audio format {version}), this build understands up to "
                + $"{Version}.");
        }
        int countA = file[6];
        int countB = file[7];
        if (countA != expectedCountA || countB != expectedCountB)
        {
            throw new CartLoadException(
                $"{sourceName}: header says {countA} {nameA} x {countB} {nameB}, profile 8 is "
                + $"{expectedCountA} x {expectedCountB} (SPEC-8 §4).");
        }
        if (file.Length != expectedFileSize)
        {
            throw new CartLoadException(
                $"{sourceName}: {file.Length} bytes, must be exactly {expectedFileSize} — the banks are fixed-size.");
        }
    }

    /// <summary>Renders the magic bytes for an error message: printable ASCII as text, anything else as hex.</summary>
    private static string Describe(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value < 0x20 || value >= 0x7F)
            {
                return "0x" + Convert.ToHexStringLower(bytes);
            }
        }
        return $"\"{Encoding.ASCII.GetString(bytes)}\"";
    }
}
