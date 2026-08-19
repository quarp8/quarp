namespace Quarp.Api;

/// <summary>
/// Base class of every Quarp cartridge.
/// <see cref="Init"/> runs once as "tick 0" and is part of the deterministic simulation;
/// <see cref="Update"/> runs exactly 60 times per game second; <see cref="Draw"/> must not
/// mutate game state (SPEC-8 §7). The protected members below mirror <see cref="IConsoleApi"/>
/// so cartridge code reads like PICO-8: <c>Cls(1); Spr(1, x, y);</c>.
/// </summary>
public abstract class Cartridge
{
    private IConsoleApi? _console;

    /// <summary>Called by the runtime before <see cref="Init"/>. Not for cartridge code.</summary>
    public void Attach(IConsoleApi console) => _console = console;

    private IConsoleApi Q => _console
        ?? throw new InvalidOperationException("Cartridge is not attached to a console.");

    /// <summary>Sets up the game once, as tick 0 of the simulation. Override it; the default does nothing.</summary>
    public virtual void Init() { }

    /// <summary>Advances the game one tick — 60 times per game second. All game logic belongs here.</summary>
    public virtual void Update() { }

    /// <summary>Draws the frame. Must not change game state, or replays and rewind break (SPEC-8 §7).</summary>
    public virtual void Draw() { }

    // --- screen ---

    /// <summary>
    /// Screen width in pixels, read from the console the cartridge is attached to — 128 on
    /// QUARP-8. Deliberately a property and not a <c>const</c>: a constant is baked into the
    /// cartridge's IL at compile time, so a cart built against one screen size would keep
    /// laying itself out for that size no matter which console later loaded it, and the whole
    /// point of asking is to find out. Use it wherever the number 128 was about to be typed —
    /// <c>ScreenWidth / 2</c> to center, <c>ScreenWidth - w</c> to right-align.
    /// </summary>
    protected int ScreenWidth => Q.ScreenWidth;

    /// <summary>
    /// Screen height in pixels, read from the console — 72 on QUARP-8, and a property for
    /// exactly the reason <see cref="ScreenWidth"/> is. <c>ScreenHeight - 8</c> anchors a HUD
    /// to the bottom edge on any profile; the literal 72 anchors it to one.
    /// </summary>
    protected int ScreenHeight => Q.ScreenHeight;

    // --- graphics ---

    /// <summary>Fills the whole screen with a color slot, ignoring camera and clip but honoring Pal.</summary>
    protected void Cls(byte color = 0) => Q.Cls(color);

    /// <summary>Draws one pixel; camera and clip apply, off-screen writes are dropped.</summary>
    protected void Pset(int x, int y, byte color) => Q.Pset(x, y, color);

    /// <summary>Reads a framebuffer pixel: the master color index 0-31 stored there, i.e. after Pal remapping. Off-screen reads 0.</summary>
    protected byte Pget(int x, int y) => Q.Pget(x, y);

    /// <summary>Bresenham line, both endpoints included. Coordinates are clamped to the Fix range +/-32768.</summary>
    protected void Line(int x0, int y0, int x1, int y1, byte color) => Q.Line(x0, y0, x1, y1, color);

    /// <summary>Rectangle outline, width/height in pixels; a non-positive size draws nothing.</summary>
    protected void Rect(int x, int y, int width, int height, byte color) => Q.Rect(x, y, width, height, color);

    /// <summary>Filled rectangle, width/height in pixels; a non-positive size draws nothing.</summary>
    protected void RectFill(int x, int y, int width, int height, byte color) => Q.RectFill(x, y, width, height, color);

    /// <summary>Circle outline; radius 0 is one pixel, a negative radius draws nothing. Center and radius are clamped to +/-32768.</summary>
    protected void Circ(int centerX, int centerY, int radius, byte color) => Q.Circ(centerX, centerY, radius, color);

    /// <summary>Filled circle; radius 0 is one pixel, a negative radius draws nothing. Center and radius are clamped to +/-32768.</summary>
    protected void CircFill(int centerX, int centerY, int radius, byte color) => Q.CircFill(centerX, centerY, radius, color);

    /// <summary>
    /// Draws sprite 0-255 at x, y, optionally as a block of cellsWide x cellsHigh cells and
    /// mirrored. The block is clamped to the sheet; colors marked by Palt stay transparent.
    /// </summary>
    protected void Spr(int sprite, int x, int y, int cellsWide = 1, int cellsHigh = 1, bool flipX = false, bool flipY = false)
        => Q.Spr(sprite, x, y, cellsWide, cellsHigh, flipX, flipY);

    /// <summary>
    /// Draws a region of the map at screenX, screenY. Tile 0 is empty and never drawn; cells
    /// outside the map are skipped; a non-zero flagFilter keeps only tiles whose sprite has
    /// ALL of the filter's flag bits.
    /// </summary>
    protected void Map(int cellX, int cellY, int screenX, int screenY, int cellsWide, int cellsHigh, byte flagFilter = 0)
        => Q.Map(cellX, cellY, screenX, screenY, cellsWide, cellsHigh, flagFilter);

    /// <summary>Tile index at a map cell; cells outside the 256x72 map read 0.</summary>
    protected byte Mget(int cellX, int cellY) => Q.Mget(cellX, cellY);

    /// <summary>Writes a tile index into a map cell; cells outside the map are ignored.</summary>
    protected void Mset(int cellX, int cellY, byte tile) => Q.Mset(cellX, cellY, tile);

    /// <summary>Reads sprite flag bit 0-7; an unknown sprite or flag reads false.</summary>
    protected bool Fget(int sprite, int flag) => Q.Fget(sprite, flag);

    /// <summary>Sets sprite flag bit 0-7; an unknown sprite or flag is ignored.</summary>
    protected void Fset(int sprite, int flag, bool value) => Q.Fset(sprite, flag, value);

    /// <summary>Reads a sprite-sheet pixel (color 0-15); outside the 128x128 sheet reads 0.</summary>
    protected byte Sget(int x, int y) => Q.Sget(x, y);

    /// <summary>Writes a sprite-sheet pixel (color masked to 0-15); outside the sheet it is ignored.</summary>
    protected void Sset(int x, int y, byte color) => Q.Sset(x, y, color);

    /// <summary>Draws text with the small 4x6 cell (32 columns on QUARP-8) and returns the x after
    /// the last glyph; '\n' starts a new line.</summary>
    protected int Print(string text, int x, int y, byte color) => Q.Print(text, x, y, color);

    /// <summary>
    /// Draws text with the named font and returns the x after the last glyph, in that font's
    /// advance: <see cref="Font.Small"/> is the 4x6 cell, <see cref="Font.Large"/> the 5x7 one
    /// (25 columns on QUARP-8) — prose wants it, a HUD does not.
    /// </summary>
    protected int Print(string text, int x, int y, byte color, Font font) => Q.Print(text, x, y, color, font);

    /// <summary>Shifts every later draw call by -x, -y; calling it without arguments recenters on the origin.</summary>
    protected void Camera(int x = 0, int y = 0) => Q.Camera(x, y);

    /// <summary>Limits drawing to a screen-space rectangle, intersected with the screen; an empty rectangle hides everything.</summary>
    protected void Clip(int x, int y, int width, int height) => Q.Clip(x, y, width, height);

    /// <summary>Removes the clip rectangle: drawing covers the whole screen again.</summary>
    protected void Clip() => Q.Clip();

    /// <summary>Points a visible slot (0-15) at any master color (0-31): darkness, night palettes, flashes.</summary>
    protected void Pal(byte slot, byte color) => Q.Pal(slot, color);

    /// <summary>Resets all 16 slots to their own master colors.</summary>
    protected void Pal() => Q.Pal();

    /// <summary>Marks a sheet color transparent (or opaque) for Spr and Map; color 0 is transparent by default.</summary>
    protected void Palt(byte color, bool transparent) => Q.Palt(color, transparent);

    /// <summary>Resets transparency to the default: only color 0 is transparent.</summary>
    protected void Palt() => Q.Palt();

    // --- input ---

    /// <summary>True while the button is held on this tick.</summary>
    protected bool Btn(Button button, int player = 0) => Q.Btn(button, player);

    /// <summary>True only on the tick the button went down — the one to use for menus and turns.</summary>
    protected bool Btnp(Button button, int player = 0) => Q.Btnp(button, player);

    // --- audio (SPEC-8 §4) ---

    /// <summary>
    /// Plays sound effect 0-63. The default channel of -1 takes the lowest idle channel, else
    /// the lowest one the music is using, else plays nothing. Out-of-range ids, out-of-range
    /// channels and empty slots are silent.
    /// <para>Changes simulation state: use it in Update or Init, never in Draw, or replays and
    /// rewind diverge on sound (SPEC-8 §7).</para>
    /// </summary>
    protected void Sfx(int id, int channel = -1) => Q.Sfx(id, channel);

    /// <summary>
    /// Starts music pattern 0-63, or stops the music with the default -1. Pattern channel N
    /// plays on chip channel N, yielding to a sound effect already holding it.
    /// <para>Changes simulation state: use it in Update or Init, never in Draw.</para>
    /// </summary>
    protected void Music(int pattern = -1) => Q.Music(pattern);

    // --- random / persistence / time ---

    /// <summary>Uniform Fix in [0, max); a max of 0 or less returns 0.</summary>
    protected Fix Rnd(Fix max) => Q.Rnd(max);

    /// <summary>Uniform int in [0, maxExclusive); a bound of 0 or less returns 0.</summary>
    protected int RndInt(int maxExclusive) => Q.RndInt(maxExclusive);

    /// <summary>Reseeds the RNG; the same seed always replays the same sequence.</summary>
    protected void Srand(int seed) => Q.Srand(seed);

    /// <summary>Reads persistent slot 0-63 (0 when never written); an out-of-range slot reads 0.</summary>
    protected Fix Dget(int slot) => Q.Dget(slot);

    /// <summary>Writes persistent slot 0-63; an out-of-range slot is ignored.</summary>
    protected void Dset(int slot, Fix value) => Q.Dset(slot, value);

    /// <summary>Ticks elapsed since Init (tick 0). 60 per game second.</summary>
    protected int Ticks => Q.Ticks;
}
