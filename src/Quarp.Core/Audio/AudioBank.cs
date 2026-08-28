namespace Quarp.Core.Audio;

/// <summary>
/// A cartridge's sound data: 64 SFX slots and 64 music patterns (SPEC-8 §4). The audio
/// counterpart of the sprite sheet and the map — supplied by the cartridge pipeline, read by
/// the <see cref="Apu"/>.
///
/// <para><b>Where the boot image lives, and why it exists.</b> Sheet, map and flags have one
/// because <c>Sset</c>/<c>Mset</c>/<c>Fset</c> let a cartridge edit them and a rewind has to
/// start from the bytes the run started with (<see cref="VirtualConsole.ResetAssets"/>). Until
/// ADR-036 the sound bank needed none: profile 8 gave a cartridge no way to write it — there
/// is still deliberately no per-step <c>Sfxset</c> — so it was constant for a whole run and a
/// second copy would have bought nothing. <c>DataToSfx</c> and <c>DataToMusic</c> ended that:
/// a cartridge can now replace a whole table from one of its own data banks. The boot image is
/// therefore <em>a second instance of this class</em>, kept by the <see cref="Apu"/>
/// (<c>_bootBank</c>), which is why nothing about it appears in this type — a bank is still
/// just a bank, and the one that is authoritative at boot is a matter of who holds it.</para>
///
/// <para><b>Where the file format stops and the core starts.</b> <c>Quarp.CartKit</c> owns
/// <c>sfx.bin</c> and <c>music.bin</c>: their magic, version, validation and the text compiler
/// that produces them (docs/AUDIO-FORMAT.md). What crosses into the core is the header-stripped
/// <em>payload</em>, exactly as decoded pixels rather than PNG chunks cross for graphics, and
/// <see cref="LoadSfxPayload"/> / <see cref="LoadMusicPayload"/> are the only two places in the
/// core that know a byte offset. Everything past them is structs. A cartridge with no audio
/// hands over an all-zero payload — or nothing at all — and that is a legal, silent bank.</para>
/// </summary>
public sealed class AudioBank
{
    /// <summary>SFX slots in a cartridge: 64 (SPEC-8 §4).</summary>
    public const int SfxCount = 64;

    /// <summary>Music patterns in a cartridge: 64 (SPEC-8 §4).</summary>
    public const int PatternCount = 64;

    /// <summary>Bytes of per-slot header in the SFX payload: speed, length, loop start, loop end.</summary>
    public const int SfxSlotHeaderSize = 4;

    /// <summary>Bytes per step in the SFX payload: one little-endian u16.</summary>
    public const int SfxStepSize = 2;

    /// <summary>The slot header table at the front of the SFX payload: 64 x 4 = 256 bytes.</summary>
    public const int SfxSlotTableSize = SfxCount * SfxSlotHeaderSize;

    /// <summary>The step table that follows it: 64 x 32 x 2 = 4096 bytes.</summary>
    public const int SfxStepTableSize = SfxCount * SfxSlot.StepCount * SfxStepSize;

    /// <summary>Bytes of a complete SFX payload: 4352, header already stripped.</summary>
    public const int SfxPayloadSize = SfxSlotTableSize + SfxStepTableSize;

    /// <summary>The channel table at the front of the music payload: 64 x 4 = 256 bytes.</summary>
    public const int MusicChannelTableSize = PatternCount * MusicPattern.ChannelCount;

    /// <summary>Bytes of a complete music payload: 320 — the channel table plus 64 flag bytes.</summary>
    public const int MusicPayloadSize = MusicChannelTableSize + PatternCount;

    private readonly SfxSlot[] _sfx = new SfxSlot[SfxCount];
    private readonly MusicPattern[] _patterns = new MusicPattern[PatternCount];

    /// <summary>An empty bank: 64 empty slots and 64 empty patterns. Playing any of it is legal and silent.</summary>
    public AudioBank()
    {
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i] = new SfxSlot();
        }
    }

    /// <summary>
    /// An SFX slot, for reading or for filling in place. Out of range throws rather than
    /// returning a dummy: unlike the cartridge-facing API, this surface is reachable only from
    /// the loader and from tests, where a wrong index is a bug that should be loud.
    /// </summary>
    public SfxSlot GetSfx(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, SfxCount);
        return _sfx[id];
    }

    /// <summary>A music pattern. Out of range throws, for the reason <see cref="GetSfx"/> does.</summary>
    public MusicPattern GetPattern(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PatternCount);
        return _patterns[index];
    }

    /// <summary>Writes a music pattern. Out of range throws, for the reason <see cref="GetSfx"/> does.</summary>
    public void SetPattern(int index, MusicPattern pattern)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PatternCount);
        _patterns[index] = pattern;
    }

    /// <summary>True when nothing in the bank can make a sound — every slot empty or all rests.</summary>
    public bool IsSilent
    {
        get
        {
            for (int i = 0; i < _sfx.Length; i++)
            {
                if (!_sfx[i].IsEmpty && !_sfx[i].IsSilent)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Back to an empty bank.</summary>
    public void Clear()
    {
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i].Clear();
        }
        Array.Clear(_patterns);
    }

    /// <summary>
    /// Copies another bank into this one, keeping this instance. The console loads audio this
    /// way so that whoever built the bank cannot go on mutating data a running simulation is
    /// reading — the same defensive copy <see cref="VirtualConsole.LoadAssets"/> makes for the
    /// sheet and the map, for the same reason. A null bank clears.
    /// </summary>
    public void CopyFrom(AudioBank? other)
    {
        if (other is null)
        {
            Clear();
            return;
        }
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i].CopyFrom(other._sfx[i]);
        }
        other._patterns.CopyTo(_patterns, 0);
    }

    /// <summary>
    /// Fills the 64 SFX slots from the payload of <c>sfx.bin</c> (docs/AUDIO-FORMAT.md §2):
    /// 64 four-byte headers — speed, length, loop start, loop end — followed by 64 x 32 step
    /// words of 16 bits, little-endian, laid out as note 0-5, wave 6-8, volume 9-11,
    /// effect 12-14, bit 15 reserved.
    ///
    /// <para>An empty span clears the bank's SFX half, which is what a cartridge without
    /// <c>sfx.bin</c> means. Any other wrong length throws, because a payload that is nearly the
    /// right size is a bug in the pipeline and silently playing the first 4000 bytes of it would
    /// hide that bug behind a wrong noise.</para>
    ///
    /// <para>The word is assembled from two bytes by hand rather than through
    /// <c>MemoryMarshal</c> or <c>BitConverter</c>: these bytes must mean the same thing on
    /// windows-x64 and linux-arm64, and a struct overlay would quietly mean something else on a
    /// big-endian host — a determinism bug in a reader wearing the costume of one in the chip.</para>
    /// </summary>
    public void LoadSfxPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            for (int i = 0; i < _sfx.Length; i++)
            {
                _sfx[i].Clear();
            }
            return;
        }
        if (payload.Length != SfxPayloadSize)
        {
            throw new ArgumentException(
                $"An SFX payload must be exactly {SfxPayloadSize} bytes, got {payload.Length}.", nameof(payload));
        }

        for (int id = 0; id < SfxCount; id++)
        {
            SfxSlot slot = _sfx[id];
            int header = id * SfxSlotHeaderSize;
            slot.Clear();
            slot.Length = payload[header + 1];
            if (slot.Length == 0)
            {
                // The zero record: an unused slot is all zeros and nothing else, so "no sfx.bin"
                // and "sfx.bin with this slot unused" are the same bytes and the same identity.
                continue;
            }
            slot.Speed = payload[header];
            slot.LoopStart = payload[header + 2];
            slot.LoopEnd = payload[header + 3];

            int steps = SfxSlotTableSize + (id * SfxSlot.StepCount * SfxStepSize);
            for (int step = 0; step < SfxSlot.StepCount; step++)
            {
                int at = steps + (step * SfxStepSize);
                int word = payload[at] | (payload[at + 1] << 8);
                slot[step] = new SfxStep(
                    word & 0x3F,
                    (Waveform)((word >> 6) & 0x07),
                    (word >> 9) & 0x07,
                    (NoteEffect)((word >> 12) & 0x07));
            }
        }
    }

    /// <summary>
    /// Fills the 64 music patterns from the payload of <c>music.bin</c> (docs/AUDIO-FORMAT.md
    /// §4): 64 x 4 channel bytes — bit 6 set means the channel plays, bits 0-5 name the slot —
    /// followed by 64 pattern flag bytes. An empty span clears the bank's music half; any other
    /// wrong length throws, for the reason <see cref="LoadSfxPayload"/> gives.
    /// </summary>
    public void LoadMusicPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            Array.Clear(_patterns);
            return;
        }
        if (payload.Length != MusicPayloadSize)
        {
            throw new ArgumentException(
                $"A music payload must be exactly {MusicPayloadSize} bytes, got {payload.Length}.", nameof(payload));
        }

        for (int index = 0; index < PatternCount; index++)
        {
            int at = index * MusicPattern.ChannelCount;
            _patterns[index] = MusicPattern.FromBytes(
                payload[at], payload[at + 1], payload[at + 2], payload[at + 3],
                payload[MusicChannelTableSize + index]);
        }
    }
}
