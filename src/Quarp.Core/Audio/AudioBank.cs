namespace Quarp.Core.Audio;

/// <summary>
/// A cartridge's sound data: 64 SFX slots and one tracker song of 64 patterns (SPEC-8 §4). The audio
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

    // --- music payload geometry (docs/AUDIO-FORMAT.md §4, ADR-041) ---
    //
    // These five offsets and this size are the only place in the core that knows the layout of
    // music.bin. Everything past LoadMusicPayload is structs.

    /// <summary>Rows a pattern can hold: 32.</summary>
    public const int SongRowCount = MusicSong.RowCount;

    /// <summary>Offset of the instrument table in a music payload; the 8 bytes before it are the preamble.</summary>
    public const int SongInstrumentTableOffset = 8;

    /// <summary>Offset of the order table in a music payload.</summary>
    public const int SongOrderTableOffset = SongInstrumentTableOffset + (MusicInstrument.Count * 4);

    /// <summary>Offset of the pattern header table in a music payload.</summary>
    public const int SongPatternTableOffset = SongOrderTableOffset + (MusicOrderEntry.Count * 4);

    /// <summary>Offset of the cell table in a music payload.</summary>
    public const int SongCellTableOffset = SongPatternTableOffset + (PatternCount * 4);

    /// <summary>Bytes of a complete music payload: 33800.</summary>
    public const int SongPayloadSize =
        SongCellTableOffset + (PatternCount * SongRowCount * Apu.ChannelCount * 4);

    private readonly SfxSlot[] _sfx = new SfxSlot[SfxCount];
    private readonly MusicSong _song = new();

    /// <summary>An empty bank: 64 empty slots and an empty song. Playing any of it is legal and silent.</summary>
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

    /// <summary>
    /// The song: patterns of cells, instruments and the order — the whole of a cartridge's
    /// music (ADR-041). This is also the model the tracker of the next wave edits.
    /// </summary>
    public MusicSong Song => _song;

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
        _song.Clear();
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
        _song.CopyFrom(other._song);
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
    /// Fills the song from the payload of <c>music.bin</c> (docs/AUDIO-FORMAT.md §4). An empty
    /// span clears the bank's music half — that is what a cartridge without <c>music.bin</c>
    /// means — and any other length than <see cref="SongPayloadSize"/> throws, for the reason
    /// <see cref="LoadSfxPayload"/> gives: a payload that is nearly the right size is a bug in
    /// the pipeline, and playing the first 320 bytes of it would hide that bug behind a wrong
    /// noise. There is exactly one music layout (ADR-041); the 320-byte pattern list this
    /// console used to read is gone, and a payload of that length is refused by name.
    /// </summary>
    public void LoadMusicPayload(ReadOnlySpan<byte> payload)
    {
        _song.Clear();
        if (payload.IsEmpty)
        {
            return;
        }
        if (payload.Length != SongPayloadSize)
        {
            throw new ArgumentException(
                $"A music payload must be exactly {SongPayloadSize} bytes, got {payload.Length}.", nameof(payload));
        }
        LoadSongPayload(payload);
    }

    /// <summary>
    /// Fills the song from the payload of <c>music.bin</c> (docs/AUDIO-FORMAT.md §4): an 8-byte
    /// preamble, 64 four-byte instruments, 128 four-byte order entries, 64 four-byte pattern
    /// headers, and 64 x 32 x 4 cells of 32 bits, little-endian.
    ///
    /// <para><b>Nothing here throws and nothing here trusts.</b> The loader of
    /// <c>Quarp.CartKit</c> has already refused a malformed file with a sentence naming the
    /// pattern, the row and the channel; but the same bytes can also arrive from a data bank
    /// (ADR-036), which is by definition unvalidated, and at that door the console must do
    /// something harmless and defined. So the preamble's redundant fields are ignored, every
    /// index is clamped by the struct that stores it, and a row past a pattern's length is simply
    /// never read. The result is still deterministic — the same bytes give the same PCM on every
    /// machine — it is merely not canonical.</para>
    ///
    /// <para>Words are assembled from bytes by hand rather than through <c>MemoryMarshal</c> or
    /// <c>BitConverter</c>, for the reason <see cref="LoadSfxPayload"/> gives: a struct overlay
    /// would quietly mean something else on a big-endian host.</para>
    /// </summary>
    private void LoadSongPayload(ReadOnlySpan<byte> payload)
    {
        _song.OrderLength = payload[4] | (payload[5] << 8);

        for (int i = 0; i < MusicInstrument.Count; i++)
        {
            int at = SongInstrumentTableOffset + (i * 4);
            _song.SetInstrument(i, new MusicInstrument(
                payload[at],
                payload[at + 1],
                (payload[at + 2] & MusicInstrument.OnceFlag) != 0,
                payload[at + 3]));
        }

        for (int i = 0; i < MusicOrderEntry.Count; i++)
        {
            int at = SongOrderTableOffset + (i * 4);
            _song.SetOrder(i, new MusicOrderEntry(
                payload[at] & (PatternCount - 1),
                (MusicFlags)(payload[at + 1]
                    & (byte)(MusicFlags.LoopStart | MusicFlags.LoopEnd | MusicFlags.Stop | MusicFlags.Jump)),
                payload[at + 2],
                (sbyte)payload[at + 3]));
        }

        for (int pattern = 0; pattern < PatternCount; pattern++)
        {
            int at = SongPatternTableOffset + (pattern * 4);
            int rows = payload[at + 2];
            _song.SetPatternRows(pattern, rows);
            _song.SetPatternSpeed(pattern, rows == 0 ? 0 : payload[at] | (payload[at + 1] << 8));
        }

        for (int pattern = 0; pattern < PatternCount; pattern++)
        {
            for (int row = 0; row < SongRowCount; row++)
            {
                for (int channel = 0; channel < Apu.ChannelCount; channel++)
                {
                    int at = SongCellTableOffset
                        + ((((pattern * SongRowCount) + row) * Apu.ChannelCount + channel) * 4);
                    uint word = payload[at]
                        | ((uint)payload[at + 1] << 8)
                        | ((uint)payload[at + 2] << 16)
                        | ((uint)payload[at + 3] << 24);
                    _song.SetCell(pattern, row, channel, new MusicCell(word));
                }
            }
        }
    }
}
