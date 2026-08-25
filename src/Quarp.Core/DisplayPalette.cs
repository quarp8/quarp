namespace Quarp.Core;

/// <summary>
/// The console's <b>second, retroactive palette stage</b>: four 32-to-32 lookup tables plus a
/// per-row selector saying which of the four a scanline is shown through. It changes nothing in
/// the framebuffer — it changes what the framebuffer <em>looks like</em> when the presenter
/// unpacks it into RGB.
///
/// <para><b>How it differs from <c>Pal</c>, which is the whole reason it exists.</b>
/// <c>VirtualConsole.Pal</c> is applied at the moment a pixel is written
/// (<c>_pixels[y * w + x] = _palMap[slot &amp; 0x0F]</c>): it decides what a later draw call puts
/// <em>into</em> the buffer, and pixels already on screen keep whatever they were given. That is
/// PICO-8's draw palette and TIC-80's PALETTE MAP (<c>mapColor</c> in <c>src/core/draw.c</c>),
/// and until this type existed it was the only palette stage we had — not by decision but by
/// omission: SPEC-8 §2 specified the palette as <em>data</em> ("which 16 of the 32") and never
/// once as a <em>mechanism</em> ("how many remaps, at which stage"). This type is the other
/// stage: it is read at output time, over the finished frame, so a single call recolours
/// everything already drawn, including the pixels drawn before the call was made.</para>
///
/// <para><b>Why data and not a per-scanline callback.</b> The references offer both shapes:
/// PICO-8 has a secondary display palette with a per-row bitmask, TIC-80 calls a cartridge
/// function (<c>BDR</c>) on each of its 144 scanlines. A callback would mean up to
/// <see cref="ConsoleProfile.Height"/> cartridge calls per frame inside the VM, but the
/// decisive objection is not cost: with a callback a frame stops being describable by the pair
/// (index buffer, output state), and a quantity that is not describable is not hashable at a
/// fixed size. As data, the whole stage is exactly
/// <see cref="HashLength"/> bytes and <see cref="FrameHash.Of(DisplayPalette)"/> answers "how
/// is this coloured" the way <see cref="FrameHash.Of(Framebuffer)"/> answers "what did the
/// cartridge draw".</para>
///
/// <para><b>Cost at output time is one indirection per row, none per pixel.</b> The presenter
/// composes the four sets into RGB once per frame and then picks a 32-entry base with
/// <see cref="SetOffset"/> once per <em>row</em>; the inner loop stays the single indexed load
/// it was before this type existed. <see cref="Resolve"/> is the same rule spelled out for one
/// pixel — for tests and tools, never for a scanline loop.</para>
///
/// <para><b>Not simulation state.</b> Like camera, clip, <c>Pal</c> and <c>Palt</c>, this is
/// drawing state: it is reset when a cartridge is attached, it survives every <c>Cls</c> and
/// every frame boundary, and no resimulation has to reproduce it (SPEC-8 §7). That is precisely
/// why <see cref="FrameHash.Of(Framebuffer)"/> cannot see it and why a second hash was needed.</para>
/// </summary>
public sealed class DisplayPalette
{
    /// <summary>How many display sets exist. Four, and the index is masked to it.</summary>
    public const int SetCount = 4;

    /// <summary>Mask applied to a set index, so an out-of-range set is a set, not an exception.</summary>
    public const int SetMask = SetCount - 1;

    /// <summary>Mask applied to a master colour index (0-31).</summary>
    private const int ColorMask = Palette.MasterCount - 1;

    /// <summary>Layout version stamped into the first hashed byte; see <see cref="WriteHashBytes"/>.</summary>
    public const byte HashVersion = 1;

    /// <summary>Bytes of shape/version header the hashed record starts with.</summary>
    public const int HashHeaderLength = 5;

    // set-major: entry [k * 32 + i] is what set k shows for master colour i.
    private readonly byte[] _sets = new byte[SetCount * Palette.MasterCount];

    // one byte per scanline: which set that row is shown through.
    private readonly byte[] _rows;

    private int _revision;

    /// <param name="profile">
    /// The console this belongs to. The selector is exactly as tall as the screen, so a
    /// QUARP-16 console gets a 180-row selector without a line of profile-specific code.
    /// </param>
    public DisplayPalette(ConsoleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _rows = new byte[profile.Height];
        Reset();
    }

    /// <summary>Rows the selector has — the screen height it was built for.</summary>
    public int Height => _rows.Length;

    /// <summary>
    /// Bumped by every call that changes a byte of this state. The presenter caches its
    /// composed RGB table against this number, which is what keeps composition once per frame
    /// instead of once per row.
    /// </summary>
    public int Revision => _revision;

    /// <summary>The four sets, set-major, 32 entries each: entry [k * 32 + i] is set k's colour for master i.</summary>
    public ReadOnlySpan<byte> Sets => _sets;

    /// <summary>The selector: one set index per scanline, top row first.</summary>
    public ReadOnlySpan<byte> Rows => _rows;

    /// <summary>
    /// True when every set is the identity map and every row selects set 0 — the state a fresh
    /// console is in, and the state in which this stage cannot change a single pixel of the
    /// picture.
    /// </summary>
    public bool IsIdentity
    {
        get
        {
            for (int k = 0; k < SetCount; k++)
            {
                int baseIndex = k * Palette.MasterCount;
                for (int i = 0; i < Palette.MasterCount; i++)
                {
                    if (_sets[baseIndex + i] != (byte)i)
                    {
                        return false;
                    }
                }
            }
            for (int y = 0; y < _rows.Length; y++)
            {
                if (_rows[y] != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Points master colour <paramref name="color"/> at master colour <paramref name="shown"/>
    /// inside set <paramref name="set"/>. Every argument is masked (set &amp; 3, colours &amp; 31)
    /// rather than rejected, following the API's "indices are masked, not thrown at" rule.
    /// </summary>
    public void Remap(int set, int color, int shown)
    {
        _sets[((set & SetMask) * Palette.MasterCount) + (color & ColorMask)] = (byte)(shown & ColorMask);
        _revision++;
    }

    /// <summary>Puts one set back to the identity map; the selector is untouched.</summary>
    public void ResetSet(int set)
    {
        int baseIndex = (set & SetMask) * Palette.MasterCount;
        for (int i = 0; i < Palette.MasterCount; i++)
        {
            _sets[baseIndex + i] = (byte)i;
        }
        _revision++;
    }

    /// <summary>Puts all four sets back to the identity map; the selector is untouched.</summary>
    public void ResetSets()
    {
        for (int k = 0; k < SetCount; k++)
        {
            ResetSet(k);
        }
    }

    /// <summary>
    /// Shows scanline <paramref name="y"/> through set <paramref name="set"/>. A row outside the
    /// screen is ignored — off-screen writes are dropped, exactly as they are for a pixel.
    /// </summary>
    public void AssignRow(int y, int set)
    {
        if ((uint)y >= (uint)_rows.Length)
        {
            return;
        }
        _rows[y] = (byte)(set & SetMask);
        _revision++;
    }

    /// <summary>
    /// Shows <paramref name="height"/> scanlines starting at <paramref name="y"/> through set
    /// <paramref name="set"/>. Position plus size, the same shape <c>Clip</c> takes, and clamped
    /// to the screen the same way: a non-positive height does nothing, and a band that hangs off
    /// either edge paints only the rows that exist.
    /// </summary>
    public void AssignRows(int y, int height, int set)
    {
        if (height <= 0)
        {
            return;
        }
        long end = (long)y + height;
        int from = Math.Max(y, 0);
        int to = (int)Math.Min(end, _rows.Length);
        byte value = (byte)(set & SetMask);
        for (int row = from; row < to; row++)
        {
            _rows[row] = value;
        }
        _revision++;
    }

    /// <summary>Puts the selector back to all-zero: every row is shown through set 0.</summary>
    public void ResetRows()
    {
        Array.Clear(_rows);
        _revision++;
    }

    /// <summary>Puts the whole stage back to identity — four identity sets, selector all zero.</summary>
    public void Reset()
    {
        ResetSets();
        ResetRows();
    }

    /// <summary>
    /// Which set scanline <paramref name="y"/> is shown through; a row outside the screen reads 0
    /// rather than reading memory. Off-screen reads answer with the default, as everywhere else
    /// in the API.
    /// </summary>
    public byte RowSet(int y) => (uint)y < (uint)_rows.Length ? _rows[y] : (byte)0;

    /// <summary>
    /// Where scanline <paramref name="y"/>'s 32-entry table starts inside <see cref="Sets"/> —
    /// and inside any table the presenter composes in the same set-major order. This is the one
    /// owner of "which table this row uses": the presenter calls it once per row and then indexes
    /// its RGB table with the raw pixel, so no per-pixel work is added by this stage.
    /// </summary>
    public int SetOffset(int y) => RowSet(y) * Palette.MasterCount;

    /// <summary>
    /// The whole rule for one pixel: the master colour that master colour
    /// <paramref name="pixel"/> is shown as on scanline <paramref name="y"/>. Spelled out for
    /// tests, tools and anything that recolours a single sample; a scanline loop must take the
    /// base once with <see cref="SetOffset"/> instead of calling this per pixel.
    /// </summary>
    public byte Resolve(int y, byte pixel) => _sets[SetOffset(y) + (pixel & ColorMask)];

    /// <summary>
    /// Bytes in the hashed record: a 5-byte shape header, the four sets (4 x 32 = 128) and one
    /// byte per scanline (90 on QUARP-8) — 223 bytes, fixed for a given console.
    /// </summary>
    public int HashLength => HashHeaderLength + _sets.Length + _rows.Length;

    /// <summary>
    /// Writes the exact bytes <see cref="FrameHash.Of(DisplayPalette)"/> digests, in this order:
    /// <list type="number">
    ///   <item><description>layout version (<see cref="HashVersion"/>);</description></item>
    ///   <item><description>set count (4);</description></item>
    ///   <item><description>master colour count (32);</description></item>
    ///   <item><description>screen height, low byte first — the same little-endian discipline
    ///     <see cref="FrameHash.Combine(ulong, ReadOnlySpan{short})"/> uses, so the digest cannot
    ///     depend on the machine's byte order;</description></item>
    ///   <item><description>the four sets, set-major;</description></item>
    ///   <item><description>the selector, top row first.</description></item>
    /// </list>
    ///
    /// <para><b>Why the header is hashed at all</b>, when the payload alone would already differ
    /// between two different states: it makes the digest self-describing across changes to the
    /// layout itself. A future stage with five sets, or a console with a different screen height,
    /// must not be able to produce a digest that a reader would take for this one — and a version
    /// byte is cheaper than discovering that collision inside a golden test.</para>
    /// </summary>
    public void WriteHashBytes(Span<byte> destination)
    {
        if (destination.Length != HashLength)
        {
            throw new ArgumentException(
                $"display state hashes {HashLength} bytes, got a {destination.Length}-byte span",
                nameof(destination));
        }
        destination[0] = HashVersion;
        destination[1] = SetCount;
        destination[2] = Palette.MasterCount;
        destination[3] = (byte)(_rows.Length & 0xFF);
        destination[4] = (byte)((_rows.Length >> 8) & 0xFF);
        _sets.CopyTo(destination[HashHeaderLength..]);
        _rows.CopyTo(destination[(HashHeaderLength + _sets.Length)..]);
    }
}
