using Quarp.Api;
using Quarp.Core.Audio;

namespace Quarp.Core;

/// <summary>
/// The QUARP-8 virtual console: owns every piece of simulation state — framebuffer, sound
/// chip, sprite sheet, map, sprite flags, camera/clip, palette remaps, RNG, input, tick counter
/// and persistent memory — and implements the whole cartridge-facing API.
/// Deterministic by construction: one <see cref="InputState"/> in per tick, a framebuffer and
/// an <see cref="AudioBlock"/> out; no clocks, no files, no allocations in the tick path
/// (ARCHITECTURE §2, CODESTYLE).
/// Cartridge exceptions are never swallowed — they propagate to the caller (the shell
/// decides what to show).
/// Drawing model: camera offset first, then clip rectangle, then palette remap; every
/// pixel write funnels through one clipped plot helper.
/// Rewind and hot-reload live one layer up, in <see cref="TimeMachine"/>; what the console
/// contributes is the ability to be put back exactly as it started — <see cref="ResetAssets"/>,
/// <see cref="LoadPersistent"/> and the seed on <see cref="AttachCart"/> restore all three
/// simulation inputs — and <see cref="TickUpdateOnly"/>, a tick whose frame nobody needs.
/// <para>Sound is simulation state, not presentation: the <see cref="Apu"/> resets with the
/// console and is advanced from the shared tick path, so <see cref="TickUpdateOnly"/> — the
/// tick a rewind runs thousands of — produces exactly the audio a drawn tick would have. If it
/// did not, a rewound game would come back in the right place playing the wrong note.</para>
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

    // The assets exactly as loaded. Sset/Mset/Fset write to the live copies above; a rewind or
    // a hot-reload resimulation restores from these (SPEC-8 §7: a cart that edits its own map
    // must resimulate from the map it started with, not from the one the last run left behind).
    // The duplicate costs 35 KB per console — cheap next to a determinism hole.
    private readonly byte[] _sheetImage = new byte[SheetWidth * SheetHeight];
    private readonly byte[] _mapImage = new byte[MapWidth * MapHeight];
    private readonly byte[] _flagsImage = new byte[SpriteCount];

    private readonly byte[] _palMap = new byte[Palette.VisibleCount];
    private readonly bool[] _palt = new bool[Palette.VisibleCount];
    private readonly int[] _persistent = new int[PersistentSlots];

    // The sound chip. Its bank is cartridge data (no boot image needed — nothing can write it
    // from a cartridge), its channels and sequencers are simulation state and reset with
    // everything else.
    private readonly Apu _apu = new();

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

    /// <summary>
    /// Screen width in pixels, as the cartridge sees it. The same number three ways —
    /// <see cref="ConsoleProfile.Width"/>, <c>Framebuffer.Width</c> and the
    /// <c>_width</c> every clip and plot already uses — and it is the field that is returned
    /// so the API can never answer something the rasterizer disagrees with.
    /// <para>This is the cartridge-facing half of "the hardware profile is data"
    /// (ARCHITECTURE §2): the console is constructed around a profile, and the only way a
    /// cartridge can learn its screen size is to ask the console it was attached to. That is
    /// what makes the 160x90 spike (<see cref="ConsoleProfile.Profile8Wide"/>) a measurement
    /// rather than a rebuild — the same compiled cartridge, pointed at a different console,
    /// lays itself out differently without recompiling a line.</para>
    /// </summary>
    public int ScreenWidth => _width;

    /// <summary>Screen height in pixels, as the cartridge sees it; see <see cref="ScreenWidth"/>.</summary>
    public int ScreenHeight => _height;

    /// <summary>
    /// The sound chip. Exposed so a shell can read <see cref="Audio.Apu.Block"/> after a tick
    /// and so tests can inspect channels; cartridges reach it only through <see cref="Sfx"/>
    /// and <see cref="Music"/>. Do not call <see cref="Audio.Apu.RenderTick"/> on it — the
    /// console does that, once per tick, from the path both tick methods share.
    /// </summary>
    public Apu Apu => _apu;

    /// <summary>
    /// The audio the last tick produced: exactly 800 samples of 16-bit mono PCM, the sound
    /// counterpart of <see cref="Framebuffer"/>. Its identity never changes, so a shell caches
    /// it once. Before the first tick, and after any reset, it is silence.
    /// </summary>
    public AudioBlock AudioBlock => _apu.Block;

    /// <summary>Set when Dset actually changes a slot; the shell clears it after writing save.dat.</summary>
    public bool PersistentDirty { get; set; }

    /// <summary>Ticks elapsed since Init; Init itself is tick 0, so the first Update sees 1.</summary>
    public int Ticks => _ticks;

    /// <summary>
    /// Creates a console with optional cartridge assets, copied in defensively:
    /// sheet 128x128 bytes (color indices 0-15), map 256x72 bytes (row-major tiles),
    /// flags 256 bytes, and the two audio payloads (see <see cref="LoadAudio"/>).
    /// Missing assets stay zeroed, which for audio means a silent bank.
    /// </summary>
    public VirtualConsole(
        ConsoleProfile profile,
        byte[]? sheet = null,
        byte[]? map = null,
        byte[]? flags = null,
        byte[]? sfx = null,
        byte[]? music = null)
    {
        Profile = profile;
        Framebuffer = new Framebuffer(profile);
        _width = Framebuffer.Width;
        _height = Framebuffer.Height;
        _pixels = Framebuffer.Pixels;
        LoadAssets(sheet, map, flags);
        LoadAudio(sfx, music);
        ResetRuntimeState(0);
    }

    /// <summary>
    /// Replaces the cartridge assets — sheet 128x128, map 256x72, flags 256 bytes — each
    /// optional and copied in defensively; a null asset becomes all zeros (Format spec v1).
    /// The copy also becomes the boot image, so <see cref="ResetAssets"/> and every later
    /// resimulation start from exactly these bytes. Sizes are checked before anything is
    /// written, so a wrong-sized asset leaves the console untouched.
    /// </summary>
    public void LoadAssets(byte[]? sheet, byte[]? map, byte[]? flags)
    {
        ValidateAsset(sheet, _sheetImage.Length, "sheet");
        ValidateAsset(map, _mapImage.Length, "map");
        ValidateAsset(flags, _flagsImage.Length, "flags");
        CopyAsset(sheet, _sheetImage);
        CopyAsset(map, _mapImage);
        CopyAsset(flags, _flagsImage);
        ResetAssets();
    }

    /// <summary>
    /// Replaces the cartridge's sound data from the two header-stripped payloads the cartridge
    /// pipeline produces — <c>sfx.bin</c>'s 4352 bytes and <c>music.bin</c>'s 320
    /// (docs/AUDIO-FORMAT.md; <see cref="AudioBank.LoadSfxPayload"/> documents the layout).
    /// A null or empty payload means an empty bank, so a cartridge without sound is silent
    /// rather than broken. Anything currently playing stops.
    ///
    /// <para>Unlike sheet, map and flags this needs no boot image: profile 8 gives a cartridge
    /// no way to write its own sound data, so the bank cannot drift during a run and a
    /// resimulation reads the same bytes the original run did.</para>
    /// </summary>
    public void LoadAudio(byte[]? sfx, byte[]? music)
    {
        _apu.LoadSfxPayload(sfx);
        _apu.LoadMusicPayload(music);
    }

    /// <summary>
    /// Replaces the cartridge's sound data with an already-decoded bank, copied in defensively.
    /// The structured door into the same room as <see cref="LoadAudio(byte[], byte[])"/> — for
    /// tests and tools that build a bank in memory instead of parsing one.
    /// </summary>
    public void LoadAudio(AudioBank? bank) => _apu.LoadBank(bank);

    /// <summary>
    /// Restores sheet, map and flags to the assets last loaded, undoing every Sset/Mset/Fset the
    /// cartridge made. Resimulation starts here: assets are simulation state like everything
    /// else, and a rewind that skipped them would replay the same inputs against a different
    /// world.
    /// </summary>
    public void ResetAssets()
    {
        _sheetImage.CopyTo(_sheet, 0);
        _mapImage.CopyTo(_map, 0);
        _flagsImage.CopyTo(_flags, 0);
    }

    private static void ValidateAsset(byte[]? source, int expectedLength, string name)
    {
        if (source is not null && source.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Asset '{name}' must be exactly {expectedLength} bytes, got {source.Length}.");
        }
    }

    private static void CopyAsset(byte[]? source, byte[] destination)
    {
        if (source is null)
        {
            Array.Clear(destination);
            return;
        }
        source.CopyTo(destination, 0);
    }

    // --- lifecycle ---

    /// <summary>
    /// Binds a cartridge and runs its Init as tick 0 (SPEC-8 §7). Resets runtime state
    /// (camera, clip, palettes, RNG seeded with <paramref name="seed"/>, input, tick counter,
    /// framebuffer) but keeps loaded assets and persistent memory — a reproducible run restores
    /// those first, with <see cref="ResetAssets"/> and <see cref="LoadPersistent"/>, because
    /// Init already reads them. Init exceptions propagate.
    /// </summary>
    public void AttachCart(Cartridge cart, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(cart);
        _cart = cart;
        ResetRuntimeState(seed);
        cart.Attach(this);
        cart.Init();
    }

    /// <summary>
    /// Advances the simulation one tick: Update, then the tick's audio, then Draw (SPEC-8 §7).
    /// Cartridge exceptions propagate untouched — the shell pauses and reports them.
    /// </summary>
    public void Tick(InputState input)
    {
        Cartridge cart = BeginTick(input);
        cart.Update();
        _apu.RenderTick();
        cart.Draw();
    }

    /// <summary>
    /// Advances the simulation one tick without drawing it. Simulation state moves exactly as
    /// <see cref="Tick"/> moves it — Draw changes nothing a later tick can observe (SPEC-8 §7) —
    /// but the framebuffer is left alone. This is what makes rewinding affordable: a seek
    /// resimulates thousands of ticks this way and draws only the frame someone will look at.
    /// <para>Audio is <em>not</em> skipped here. The frame of a tick nobody watches can be
    /// skipped because nothing reads it back; the audio of that tick cannot, because the chip's
    /// phase accumulators, envelopes and noise register carry into the next tick. Both tick
    /// methods therefore render sound through the same call in the same place, which is also
    /// the reason there is no second, cheaper synthesis path to disagree with the first.</para>
    /// </summary>
    public void TickUpdateOnly(InputState input)
    {
        Cartridge cart = BeginTick(input);
        cart.Update();
        _apu.RenderTick();
    }

    /// <summary>Shared tick prologue: rotate input, count the tick, hand back the live cartridge.</summary>
    private Cartridge BeginTick(InputState input)
    {
        Cartridge cart = _cart
            ?? throw new InvalidOperationException("No cartridge attached; call AttachCart first.");
        _previous = _input;
        _input = input;
        _ticks++;
        return cart;
    }

    private void ResetRuntimeState(int seed)
    {
        Camera();
        Clip();
        Pal();
        Palt();
        Srand(seed);
        _apu.Reset();
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

    // --- audio (SPEC-8 §4; the chip itself is Quarp.Core.Audio.Apu) ---

    /// <summary>
    /// Starts SFX <paramref name="id"/> (0-63). <paramref name="channel"/> 0-3 takes that
    /// channel outright; -1, the default, picks the lowest idle channel, else the lowest one
    /// the music is using, else does nothing. An <paramref name="id"/> of -1 stops instead of
    /// starting: <c>Sfx(-1, 2)</c> silences channel 2 and <c>Sfx(-1)</c> silences all four,
    /// leaving a playing song to refill its voices at its next pattern. Every other
    /// out-of-range argument, -2 included, and every empty slot are silent no-ops.
    /// Simulation state, so never from Draw (QRP1004).
    /// </summary>
    public void Sfx(int id, int channel = -1) => _apu.PlaySfx(id, channel);

    /// <summary>
    /// Starts music pattern <paramref name="pattern"/> (0-63); a negative pattern — including
    /// the default — stops the music, and 64 or more is a no-op. Simulation state, so never
    /// from Draw (QRP1004).
    /// </summary>
    public void Music(int pattern = -1) => _apu.PlayMusic(pattern);

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
