using Quarp.Api;

namespace Quarp.Core;

/// <summary>
/// The QUARP-8 virtual console: owns every piece of simulation state — framebuffer, sprite
/// sheet, map, sprite flags, camera/clip, palette remaps, RNG, input, tick counter and
/// persistent memory — and implements the whole cartridge-facing API.
/// Deterministic by construction: one <see cref="InputState"/> in per tick, framebuffer out;
/// no clocks, no files, no allocations in the tick path (ARCHITECTURE §2, CODESTYLE).
/// Cartridge exceptions are never swallowed — they propagate to the caller (the shell
/// decides what to show).
/// Drawing model: camera offset first, then clip rectangle, then palette remap; every
/// pixel write funnels through one clipped plot helper.
/// </summary>
public sealed class VirtualConsole : IConsoleApi
{
    // QUARP-8 graphics data limits (SPEC-8 §3). Constants for now; a future Profile16
    // moves them into ConsoleProfile ("profile is data").
    public const int SheetWidth = 128;
    public const int SheetHeight = 128;
    public const int SpriteSize = 8;
    public const int SheetColumns = SheetWidth / SpriteSize;
    public const int SpriteCount = 256;
    public const int MapWidth = 256;
    public const int MapHeight = 72;
    public const int PersistentSlots = 64;

    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;

    private readonly byte[] _sheet = new byte[SheetWidth * SheetHeight];
    private readonly byte[] _map = new byte[MapWidth * MapHeight];
    private readonly byte[] _flags = new byte[SpriteCount];
    private readonly byte[] _palMap = new byte[Palette.VisibleCount];
    private readonly bool[] _palt = new bool[Palette.VisibleCount];
    private readonly int[] _persistent = new int[PersistentSlots];

    private int _cameraX;
    private int _cameraY;
    private int _clipX0;
    private int _clipY0;
    private int _clipX1; // exclusive
    private int _clipY1; // exclusive

    // xoshiro128** state; part of the simulation (SPEC-8 §7). Never all-zero.
    private uint _rng0;
    private uint _rng1;
    private uint _rng2;
    private uint _rng3;

    private InputState _input;
    private InputState _previous;
    private int _ticks;
    private Cartridge? _cart;

    public ConsoleProfile Profile { get; }

    public Framebuffer Framebuffer { get; }

    /// <summary>Set when Dset actually changes a slot; the shell clears it after writing save.dat.</summary>
    public bool PersistentDirty { get; set; }

    /// <summary>Ticks elapsed since Init; Init itself is tick 0, so the first Update sees 1.</summary>
    public int Ticks => _ticks;

    /// <summary>
    /// Creates a console with optional cartridge assets, copied in defensively:
    /// sheet 128x128 bytes (color indices 0-15), map 256x72 bytes (row-major tiles),
    /// flags 256 bytes. Missing assets stay zeroed.
    /// </summary>
    public VirtualConsole(ConsoleProfile profile, byte[]? sheet = null, byte[]? map = null, byte[]? flags = null)
    {
        Profile = profile;
        Framebuffer = new Framebuffer(profile);
        _width = Framebuffer.Width;
        _height = Framebuffer.Height;
        _pixels = Framebuffer.Pixels;
        CopyAsset(sheet, _sheet, "sheet");
        CopyAsset(map, _map, "map");
        CopyAsset(flags, _flags, "flags");
        ResetRuntimeState();
    }

    private static void CopyAsset(byte[]? source, byte[] destination, string name)
    {
        if (source is null)
        {
            return;
        }
        if (source.Length != destination.Length)
        {
            throw new ArgumentException(
                $"Asset '{name}' must be exactly {destination.Length} bytes, got {source.Length}.");
        }
        source.CopyTo(destination, 0);
    }

    // --- lifecycle ---

    /// <summary>
    /// Binds a cartridge and runs its Init as tick 0 (SPEC-8 §7). Resets runtime state
    /// (camera, clip, palettes, RNG seed 0, input, tick counter, framebuffer) but keeps
    /// loaded assets and persistent memory. Init exceptions propagate.
    /// </summary>
    public void AttachCart(Cartridge cart)
    {
        ArgumentNullException.ThrowIfNull(cart);
        _cart = cart;
        ResetRuntimeState();
        cart.Attach(this);
        cart.Init();
    }

    /// <summary>
    /// Advances the simulation one tick: Update, then Draw (SPEC-8 §7).
    /// Cartridge exceptions propagate untouched — the shell pauses and reports them.
    /// </summary>
    public void Tick(InputState input)
    {
        if (_cart is null)
        {
            throw new InvalidOperationException("No cartridge attached; call AttachCart first.");
        }
        _previous = _input;
        _input = input;
        _ticks++;
        _cart.Update();
        _cart.Draw();
    }

    private void ResetRuntimeState()
    {
        Camera();
        Clip();
        Pal();
        Palt();
        Srand(0);
        _input = default;
        _previous = default;
        _ticks = 0;
        Framebuffer.Clear(0);
    }

    // --- persistence plumbing (for the shell; cartridges use Dget/Dset) ---

    /// <summary>Loads saved data (raw Fix per slot); shorter input leaves the tail zeroed. Clears the dirty flag.</summary>
    public void LoadPersistent(ReadOnlySpan<int> data)
    {
        Array.Clear(_persistent);
        int count = Math.Min(data.Length, PersistentSlots);
        for (int i = 0; i < count; i++)
        {
            _persistent[i] = data[i];
        }
        PersistentDirty = false;
    }

    /// <summary>Copies all 64 slots (raw Fix) into the destination for saving.</summary>
    public void CopyPersistentTo(Span<int> destination)
    {
        _persistent.CopyTo(destination);
    }

    // --- the single clipped write path ---

    // Clip window in world coordinates (camera applied), right/bottom exclusive.
    // Long so a camera near int limits cannot overflow.
    private long WorldClipLeft => (long)_clipX0 + _cameraX;
    private long WorldClipTop => (long)_clipY0 + _cameraY;
    private long WorldClipRight => (long)_clipX1 + _cameraX;
    private long WorldClipBottom => (long)_clipY1 + _cameraY;

    // Draw coordinates and radii live in the Fix range [-32768; +32768] (SPEC-8 §7). Anything
    // wilder is clamped at the API boundary, before any Bresenham/midpoint walk: a stray
    // int.MaxValue must not overflow the step math or stall a frame for seconds, and every sane
    // coordinate passes through untouched.
    private const int CoordinateLimit = 32768;

    private static int ClampCoordinate(int value) => Math.Clamp(value, -CoordinateLimit, CoordinateLimit);

    /// <summary>Camera shift, clip test, palette remap, write — every draw call ends here.</summary>
    private void Plot(int x, int y, byte slot)
    {
        x -= _cameraX;
        y -= _cameraY;
        if (x < _clipX0 || x >= _clipX1 || y < _clipY0 || y >= _clipY1)
        {
            return;
        }
        _pixels[y * _width + x] = _palMap[slot & 0x0F];
    }

    /// <summary>Horizontal run [x0, x1] at row y (world coordinates), pre-clamped to the clip window.</summary>
    private void SpanH(long x0, long x1, long y, byte color)
    {
        if (y < WorldClipTop || y >= WorldClipBottom)
        {
            return;
        }
        long from = Math.Max(x0, WorldClipLeft);
        long to = Math.Min(x1, WorldClipRight - 1);
        for (long px = from; px <= to; px++)
        {
            Plot((int)px, (int)y, color);
        }
    }

    /// <summary>Vertical run [y0, y1] at column x (world coordinates), pre-clamped to the clip window.</summary>
    private void SpanV(long y0, long y1, long x, byte color)
    {
        if (x < WorldClipLeft || x >= WorldClipRight)
        {
            return;
        }
        long from = Math.Max(y0, WorldClipTop);
        long to = Math.Min(y1, WorldClipBottom - 1);
        for (long py = from; py <= to; py++)
        {
            Plot((int)x, (int)py, color);
        }
    }

    // --- graphics ---

    /// <summary>Clears the whole screen; ignores camera and clip, honors Pal remapping.</summary>
    public void Cls(byte color = 0)
    {
        Framebuffer.Clear(_palMap[color & 0x0F]);
    }

    public void Pset(int x, int y, byte color) => Plot(x, y, color);

    /// <summary>
    /// Reads the framebuffer: the master color index 0-31 actually stored there, i.e. the slot
    /// after Pal remapping (SPEC-8 §1). Camera applies, off-screen reads 0.
    /// </summary>
    public byte Pget(int x, int y)
    {
        x -= _cameraX;
        y -= _cameraY;
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return 0;
        }
        return _pixels[y * _width + x];
    }

    /// <summary>Bresenham line, both endpoints included; coordinates are clamped to +/-32768 first.</summary>
    public void Line(int x0, int y0, int x1, int y1, byte color)
    {
        x0 = ClampCoordinate(x0);
        y0 = ClampCoordinate(y0);
        x1 = ClampCoordinate(x1);
        y1 = ClampCoordinate(y1);

        // Cheap reject when the segment's bounding box misses the clip window; per-pixel
        // clipping stays in Plot so the Bresenham pattern never depends on the clip state.
        if (Math.Max(x0, x1) < WorldClipLeft || Math.Min(x0, x1) >= WorldClipRight ||
            Math.Max(y0, y1) < WorldClipTop || Math.Min(y0, y1) >= WorldClipBottom)
        {
            return;
        }

        int dx = x1 - x0;
        int dy = y1 - y0;
        int stepX = dx >= 0 ? 1 : -1;
        int stepY = dy >= 0 ? 1 : -1;
        dx = dx >= 0 ? dx : -dx;
        dy = dy >= 0 ? dy : -dy;

        if (dx >= dy)
        {
            int err = dx >> 1;
            for (int i = 0; i <= dx; i++)
            {
                Plot(x0, y0, color);
                x0 += stepX;
                err -= dy;
                if (err < 0)
                {
                    err += dx;
                    y0 += stepY;
                }
            }
        }
        else
        {
            int err = dy >> 1;
            for (int i = 0; i <= dy; i++)
            {
                Plot(x0, y0, color);
                y0 += stepY;
                err -= dx;
                if (err < 0)
                {
                    err += dy;
                    x0 += stepX;
                }
            }
        }
    }

    /// <summary>Outline rectangle; non-positive width or height draws nothing (soft).</summary>
    public void Rect(int x, int y, int width, int height, byte color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        long right = (long)x + width - 1;
        long bottom = (long)y + height - 1;
        SpanH(x, right, y, color);
        if (bottom != y)
        {
            SpanH(x, right, bottom, color);
        }
        SpanV((long)y + 1, bottom - 1, x, color);
        if (right != x)
        {
            SpanV((long)y + 1, bottom - 1, right, color);
        }
    }

    /// <summary>Filled rectangle; non-positive width or height draws nothing (soft).</summary>
    public void RectFill(int x, int y, int width, int height, byte color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        long right = (long)x + width - 1;
        long yFrom = Math.Max(y, WorldClipTop);
        long yTo = Math.Min((long)y + height - 1, WorldClipBottom - 1);
        for (long py = yFrom; py <= yTo; py++)
        {
            SpanH(x, right, py, color);
        }
    }

    /// <summary>
    /// Midpoint circle outline; negative radius draws nothing, radius 0 is one pixel.
    /// Center and radius are clamped to +/-32768 first.
    /// </summary>
    public void Circ(int centerX, int centerY, int radius, byte color)
    {
        if (radius < 0)
        {
            return;
        }
        centerX = ClampCoordinate(centerX);
        centerY = ClampCoordinate(centerY);
        radius = ClampCoordinate(radius);
        if (radius == 0)
        {
            Plot(centerX, centerY, color);
            return;
        }
        if ((long)centerX + radius < WorldClipLeft || (long)centerX - radius >= WorldClipRight ||
            (long)centerY + radius < WorldClipTop || (long)centerY - radius >= WorldClipBottom)
        {
            return;
        }
        int x = radius;
        int y = 0;
        int err = 1 - radius;
        while (x >= y)
        {
            Plot(centerX + x, centerY + y, color);
            Plot(centerX - x, centerY + y, color);
            Plot(centerX + x, centerY - y, color);
            Plot(centerX - x, centerY - y, color);
            Plot(centerX + y, centerY + x, color);
            Plot(centerX - y, centerY + x, color);
            Plot(centerX + y, centerY - x, color);
            Plot(centerX - y, centerY - x, color);
            y++;
            if (err < 0)
            {
                err += 2 * y + 1;
            }
            else
            {
                x--;
                err += 2 * (y - x) + 1;
            }
        }
    }

    /// <summary>
    /// Filled midpoint circle; negative radius draws nothing, radius 0 is one pixel.
    /// Center and radius are clamped to +/-32768 first.
    /// </summary>
    public void CircFill(int centerX, int centerY, int radius, byte color)
    {
        if (radius < 0)
        {
            return;
        }
        centerX = ClampCoordinate(centerX);
        centerY = ClampCoordinate(centerY);
        radius = ClampCoordinate(radius);
        if (radius == 0)
        {
            Plot(centerX, centerY, color);
            return;
        }
        if ((long)centerX + radius < WorldClipLeft || (long)centerX - radius >= WorldClipRight ||
            (long)centerY + radius < WorldClipTop || (long)centerY - radius >= WorldClipBottom)
        {
            return;
        }
        int x = radius;
        int y = 0;
        int err = 1 - radius;
        while (x >= y)
        {
            SpanH((long)centerX - x, (long)centerX + x, (long)centerY + y, color);
            SpanH((long)centerX - x, (long)centerX + x, (long)centerY - y, color);
            SpanH((long)centerX - y, (long)centerX + y, (long)centerY + x, color);
            SpanH((long)centerX - y, (long)centerX + y, (long)centerY - x, color);
            y++;
            if (err < 0)
            {
                err += 2 * y + 1;
            }
            else
            {
                x--;
                err += 2 * (y - x) + 1;
            }
        }
    }

    /// <summary>
    /// Draws sprite cells from the sheet. The region is clamped to the sheet before
    /// flipping, so a run past the sheet edge draws only what exists (soft).
    /// Transparency comes from Palt on the original sheet color; Pal remaps on write.
    /// </summary>
    public void Spr(int sprite, int x, int y, int cellsWide = 1, int cellsHigh = 1, bool flipX = false, bool flipY = false)
    {
        if ((uint)sprite >= SpriteCount || cellsWide <= 0 || cellsHigh <= 0)
        {
            return;
        }
        int sheetX = sprite % SheetColumns * SpriteSize;
        int sheetY = sprite / SheetColumns * SpriteSize;
        int width = (int)Math.Min((long)cellsWide * SpriteSize, SheetWidth - sheetX);
        int height = (int)Math.Min((long)cellsHigh * SpriteSize, SheetHeight - sheetY);
        BlitSheet(sheetX, sheetY, width, height, x, y, flipX, flipY);
    }

    /// <summary>
    /// Draws a map region: tile 0 is empty and never drawn (PICO-8 convention), and tiles
    /// outside the 256x72 map are skipped (soft edges).
    /// A non-zero flagFilter draws only tiles whose sprite has ALL filter bits set.
    /// </summary>
    public void Map(int cellX, int cellY, int screenX, int screenY, int cellsWide, int cellsHigh, byte flagFilter = 0)
    {
        if (cellsWide <= 0 || cellsHigh <= 0)
        {
            return;
        }
        // Only tiles that exist can draw, so walk the intersection with the map.
        long firstY = Math.Max(cellY, 0);
        long lastY = Math.Min((long)cellY + cellsHigh, MapHeight);
        long firstX = Math.Max(cellX, 0);
        long lastX = Math.Min((long)cellX + cellsWide, MapWidth);
        for (long mapY = firstY; mapY < lastY; mapY++)
        {
            for (long mapX = firstX; mapX < lastX; mapX++)
            {
                byte tile = _map[(int)(mapY * MapWidth + mapX)];
                if (tile == 0)
                {
                    // Tile 0 means "empty cell", not "sprite 0" — an empty map draws nothing.
                    continue;
                }
                if (flagFilter != 0 && (_flags[tile] & flagFilter) != flagFilter)
                {
                    continue;
                }
                long drawX = screenX + (mapX - cellX) * SpriteSize;
                long drawY = screenY + (mapY - cellY) * SpriteSize;
                if (drawX + SpriteSize <= WorldClipLeft || drawX >= WorldClipRight ||
                    drawY + SpriteSize <= WorldClipTop || drawY >= WorldClipBottom)
                {
                    continue;
                }
                BlitSheet(
                    tile % SheetColumns * SpriteSize,
                    tile / SheetColumns * SpriteSize,
                    SpriteSize,
                    SpriteSize,
                    (int)drawX,
                    (int)drawY,
                    flipX: false,
                    flipY: false);
            }
        }
    }

    /// <summary>Shared sprite/map pixel path: flips, Palt transparency, then the clipped plot.</summary>
    private void BlitSheet(int sheetX, int sheetY, int width, int height, int x, int y, bool flipX, bool flipY)
    {
        for (int dy = 0; dy < height; dy++)
        {
            int srcRow = (sheetY + (flipY ? height - 1 - dy : dy)) * SheetWidth;
            for (int dx = 0; dx < width; dx++)
            {
                int srcX = sheetX + (flipX ? width - 1 - dx : dx);
                byte color = _sheet[srcRow + srcX];
                if (_palt[color & 0x0F])
                {
                    continue;
                }
                Plot(x + dx, y + dy, color);
            }
        }
    }

    public byte Mget(int cellX, int cellY)
    {
        if ((uint)cellX >= MapWidth || (uint)cellY >= MapHeight)
        {
            return 0;
        }
        return _map[cellY * MapWidth + cellX];
    }

    public void Mset(int cellX, int cellY, byte tile)
    {
        if ((uint)cellX >= MapWidth || (uint)cellY >= MapHeight)
        {
            return;
        }
        _map[cellY * MapWidth + cellX] = tile;
    }

    public bool Fget(int sprite, int flag)
    {
        if ((uint)sprite >= SpriteCount || (uint)flag >= 8)
        {
            return false;
        }
        return ((_flags[sprite] >> flag) & 1) != 0;
    }

    public void Fset(int sprite, int flag, bool value)
    {
        if ((uint)sprite >= SpriteCount || (uint)flag >= 8)
        {
            return;
        }
        byte bit = (byte)(1 << flag);
        _flags[sprite] = value ? (byte)(_flags[sprite] | bit) : (byte)(_flags[sprite] & ~bit);
    }

    public byte Sget(int x, int y)
    {
        if ((uint)x >= SheetWidth || (uint)y >= SheetHeight)
        {
            return 0;
        }
        return _sheet[y * SheetWidth + x];
    }

    public void Sset(int x, int y, byte color)
    {
        if ((uint)x >= SheetWidth || (uint)y >= SheetHeight)
        {
            return;
        }
        _sheet[y * SheetWidth + x] = (byte)(color & 0x0F);
    }

    /// <summary>
    /// Draws text with the 4x6 system font and returns the x after the last glyph.
    /// '\n' starts a new line at the original x; other control characters are skipped;
    /// characters outside ASCII 32-126 draw a hollow box.
    /// </summary>
    public int Print(string text, int x, int y, byte color)
    {
        if (text is null)
        {
            return x;
        }
        int cursorX = x;
        int cursorY = y;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                cursorX = x;
                cursorY += SystemFont.CellHeight;
                continue;
            }
            if (c < ' ')
            {
                continue;
            }
            uint glyph = SystemFont.GetGlyph(c);
            for (int row = 0; row < SystemFont.GlyphHeight; row++)
            {
                for (int col = 0; col < SystemFont.GlyphWidth; col++)
                {
                    if (SystemFont.IsSet(glyph, col, row))
                    {
                        Plot(cursorX + col, cursorY + row, color);
                    }
                }
            }
            cursorX += SystemFont.CellWidth;
        }
        return cursorX;
    }

    public void Camera(int x = 0, int y = 0)
    {
        _cameraX = x;
        _cameraY = y;
    }

    /// <summary>Sets the clip rectangle in screen space, intersected with the screen; an empty rectangle clips everything.</summary>
    public void Clip(int x, int y, int width, int height)
    {
        long x1 = (long)x + Math.Max(width, 0);
        long y1 = (long)y + Math.Max(height, 0);
        _clipX0 = Math.Clamp(x, 0, _width);
        _clipY0 = Math.Clamp(y, 0, _height);
        _clipX1 = (int)Math.Clamp(x1, 0L, _width);
        _clipY1 = (int)Math.Clamp(y1, 0L, _height);
        if (_clipX1 < _clipX0)
        {
            _clipX1 = _clipX0;
        }
        if (_clipY1 < _clipY0)
        {
            _clipY1 = _clipY0;
        }
    }

    public void Clip()
    {
        _clipX0 = 0;
        _clipY0 = 0;
        _clipX1 = _width;
        _clipY1 = _height;
    }

    public void Pal(byte slot, byte color)
    {
        _palMap[slot & 0x0F] = (byte)(color & 0x1F);
    }

    public void Pal()
    {
        for (int i = 0; i < _palMap.Length; i++)
        {
            _palMap[i] = (byte)i;
        }
    }

    public void Palt(byte color, bool transparent)
    {
        _palt[color & 0x0F] = transparent;
    }

    /// <summary>Resets transparency to the default: only color 0 is transparent (SPEC-8 §2).</summary>
    public void Palt()
    {
        Array.Clear(_palt);
        _palt[0] = true;
    }

    // --- input ---

    public bool Btn(Button button, int player = 0) => _input.IsDown(player, button);

    /// <summary>True on the tick the button went down: held now and not held on the previous tick.</summary>
    public bool Btnp(Button button, int player = 0) =>
        _input.IsDown(player, button) && !_previous.IsDown(player, button);

    // --- audio (M3; silent no-op stubs in M1 by design) ---

    public void Sfx(int id, int channel = -1)
    {
    }

    public void Music(int pattern = -1)
    {
    }

    // --- deterministic random ---

    /// <summary>Uniform Fix in [0, max); max &lt;= 0 returns 0. Consumes exactly one RNG draw.</summary>
    public Fix Rnd(Fix max)
    {
        if (max.Raw <= 0)
        {
            return Fix.Zero;
        }
        return Fix.FromRaw((int)((ulong)NextRandom() * (uint)max.Raw >> 32));
    }

    /// <summary>Uniform int in [0, maxExclusive); maxExclusive &lt;= 0 returns 0. Consumes exactly one RNG draw.</summary>
    public int RndInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            return 0;
        }
        return (int)((ulong)NextRandom() * (uint)maxExclusive >> 32);
    }

    /// <summary>Reseeds the RNG: the seed expands through splitmix64 into xoshiro128** state.</summary>
    public void Srand(int seed)
    {
        ulong state = unchecked((ulong)(long)seed);
        ulong a = SplitMix64(ref state);
        ulong b = SplitMix64(ref state);
        _rng0 = (uint)a;
        _rng1 = (uint)(a >> 32);
        _rng2 = (uint)b;
        _rng3 = (uint)(b >> 32);
        if ((_rng0 | _rng1 | _rng2 | _rng3) == 0)
        {
            // xoshiro must never be all-zero; splitmix64 makes this astronomically rare.
            _rng3 = 1;
        }
    }

    private uint NextRandom()
    {
        unchecked
        {
            uint result = RotateLeft(_rng1 * 5, 7) * 9;
            uint t = _rng1 << 9;
            _rng2 ^= _rng0;
            _rng3 ^= _rng1;
            _rng1 ^= _rng2;
            _rng0 ^= _rng3;
            _rng2 ^= t;
            _rng3 = RotateLeft(_rng3, 11);
            return result;
        }
    }

    private static uint RotateLeft(uint value, int count) =>
        (value << count) | (value >> (32 - count));

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    // --- persistence (64 slots; second simulation input — SPEC-8 §7) ---

    public Fix Dget(int slot)
    {
        if ((uint)slot >= PersistentSlots)
        {
            return Fix.Zero;
        }
        return Fix.FromRaw(_persistent[slot]);
    }

    public void Dset(int slot, Fix value)
    {
        if ((uint)slot >= PersistentSlots)
        {
            return;
        }
        if (_persistent[slot] == value.Raw)
        {
            return;
        }
        _persistent[slot] = value.Raw;
        PersistentDirty = true;
    }
}
