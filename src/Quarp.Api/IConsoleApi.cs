namespace Quarp.Api;

/// <summary>
/// The full QUARP-8 cartridge-facing surface (SPEC-8 §8; signatures are draft until
/// API-8.md is ratified at M4). Implemented by the console core; cartridges call it
/// through the <see cref="Cartridge"/> convenience wrappers.
/// Colors are indices 0-15 (visible palette); <see cref="Pal(byte, byte)"/> can point
/// a slot at a secret counterpart (16-31).
/// Every call is soft: out-of-range coordinates, sizes and indices draw nothing or read
/// zero instead of throwing.
/// </summary>
public interface IConsoleApi
{
    // --- screen (SPEC-8 §1) ---

    /// <summary>
    /// Screen width in pixels — 160 on QUARP-8. Read it instead of writing 160: the number is
    /// a property of the console the cartridge is running on, and the console is chosen at run
    /// time. A cartridge that lays itself out from this value moves with the hardware profile
    /// it is handed; one that spells the number out is pinned to a screen it never checked for.
    /// That stopped being theory in M4: the console's own resolution changed (ADR-021), the five
    /// demos written against this property needed no edit at all, and the one cartridge older
    /// than the rule — the snake — had to be relaid out by hand.
    /// <para>Constant across a whole run — the framebuffer is allocated once, at construction —
    /// so it is safe to cache in a field during <c>Init</c>, though there is no reason to.</para>
    /// <para>A read, not a write: it changes nothing a resimulation has to reproduce, so it is
    /// legal in <c>Draw</c> and, unlike <see cref="Sfx"/> or <see cref="Rnd"/>, invisible to
    /// the determinism analyzer.</para>
    /// </summary>
    int ScreenWidth { get; }

    /// <summary>
    /// Screen height in pixels — 90 on QUARP-8. Same rules as <see cref="ScreenWidth"/>:
    /// read it, never spell the number out. Bottom-anchored HUDs (<c>ScreenHeight - 8</c>) and
    /// centered layouts (<c>ScreenHeight / 2</c>) are the places it earns its keep.
    /// </summary>
    int ScreenHeight { get; }

    // --- graphics ---

    /// <summary>Fills the whole screen with a color slot, ignoring camera and clip but honoring Pal.</summary>
    void Cls(byte color = 0);

    /// <summary>Draws one pixel; camera and clip apply, off-screen writes are dropped.</summary>
    void Pset(int x, int y, byte color);

    /// <summary>
    /// Reads a framebuffer pixel: the master color index 0-31 actually stored there, i.e. after
    /// Pal remapping. Camera applies; anything off-screen reads 0.
    /// </summary>
    byte Pget(int x, int y);

    /// <summary>Bresenham line, both endpoints included. Coordinates are clamped to the Fix range +/-32768.</summary>
    void Line(int x0, int y0, int x1, int y1, byte color);

    /// <summary>Rectangle outline, width/height in pixels; a non-positive size draws nothing.</summary>
    void Rect(int x, int y, int width, int height, byte color);

    /// <summary>Filled rectangle, width/height in pixels; a non-positive size draws nothing.</summary>
    void RectFill(int x, int y, int width, int height, byte color);

    /// <summary>
    /// Midpoint circle outline; radius 0 is a single pixel, a negative radius draws nothing.
    /// Center and radius are clamped to the Fix range +/-32768.
    /// </summary>
    void Circ(int centerX, int centerY, int radius, byte color);

    /// <summary>
    /// Filled midpoint circle; radius 0 is a single pixel, a negative radius draws nothing.
    /// Center and radius are clamped to the Fix range +/-32768.
    /// </summary>
    void CircFill(int centerX, int centerY, int radius, byte color);

    /// <summary>
    /// Draws sprite <paramref name="sprite"/> (0-255) at x, y, optionally as a block of
    /// cellsWide x cellsHigh cells and mirrored. The block is clamped to the sheet, and sheet
    /// colors marked by Palt stay transparent.
    /// </summary>
    void Spr(int sprite, int x, int y, int cellsWide = 1, int cellsHigh = 1, bool flipX = false, bool flipY = false);

    /// <summary>
    /// Draws a region of the map at screenX, screenY. Tile 0 is empty and never drawn; tiles
    /// outside the 256x72 map are skipped. A non-zero flagFilter keeps only tiles whose sprite
    /// has ALL of the filter's flag bits.
    /// </summary>
    void Map(int cellX, int cellY, int screenX, int screenY, int cellsWide, int cellsHigh, byte flagFilter = 0);

    /// <summary>Tile index at a map cell; cells outside the 256x72 map read 0.</summary>
    byte Mget(int cellX, int cellY);

    /// <summary>Writes a tile index into a map cell; cells outside the map are ignored.</summary>
    void Mset(int cellX, int cellY, byte tile);

    /// <summary>Reads sprite flag bit 0-7; an unknown sprite or flag reads false.</summary>
    bool Fget(int sprite, int flag);

    /// <summary>Sets sprite flag bit 0-7; an unknown sprite or flag is ignored.</summary>
    void Fset(int sprite, int flag, bool value);

    /// <summary>Reads a sprite-sheet pixel (color 0-15); outside the 128x128 sheet reads 0.</summary>
    byte Sget(int x, int y);

    /// <summary>Writes a sprite-sheet pixel (color masked to 0-15); outside the sheet it is ignored.</summary>
    void Sset(int x, int y, byte color);

    /// <summary>
    /// Draws text with the small system font (3x5 ink in a 4x6 cell) and returns the x
    /// coordinate after the last glyph (decided: yes, API-8 §reviewed) — that is, x + 4 per
    /// printed character, which is what a caller chains or centers with. '\n' starts a new line
    /// at the original x; characters outside ASCII 32-126 draw a hollow box.
    /// <para>This is the call that existed before there was a second font, and it keeps drawing
    /// exactly the pixels it drew. Which font "no font named" means is decided in one place
    /// only — <c>VirtualConsole</c>'s implementation of this overload.</para>
    /// </summary>
    int Print(string text, int x, int y, byte color);

    /// <summary>
    /// Draws text with the named font: <see cref="Font.Small"/> is the 4x6 cell above,
    /// <see cref="Font.Large"/> the 5x7 one — a third fewer characters per line, real
    /// descenders, and the face prose wants. The return value follows the font, so it is
    /// x + 5 per character for <see cref="Font.Large"/>.
    /// <para>Per call, never a mode: the console has no "current font" to set or forget.</para>
    /// </summary>
    int Print(string text, int x, int y, byte color, Font font);

    /// <summary>Shifts every later draw call by -x, -y; calling it without arguments recenters on the origin.</summary>
    void Camera(int x = 0, int y = 0);

    /// <summary>Limits drawing to a screen-space rectangle, intersected with the screen; an empty rectangle hides everything.</summary>
    void Clip(int x, int y, int width, int height);

    /// <summary>Removes the clip rectangle: drawing covers the whole screen again.</summary>
    void Clip();

    /// <summary>Points a visible slot (0-15) at any master color (0-31): darkness, night palettes, flashes.</summary>
    void Pal(byte slot, byte color);

    /// <summary>Resets all 16 slots to their own master colors.</summary>
    void Pal();

    /// <summary>Marks a sheet color transparent (or opaque) for Spr and Map; screen color 0 is transparent by default.</summary>
    void Palt(byte color, bool transparent);

    /// <summary>Resets transparency to the default: only color 0 is transparent (SPEC-8 §2).</summary>
    void Palt();

    // --- input (SPEC-8 §5: 2 players max) ---

    /// <summary>True while the button is held on this tick; an unknown player reads false.</summary>
    bool Btn(Button button, int player = 0);

    /// <summary>True only on the tick the button went down (held now, not held on the previous tick).</summary>
    bool Btnp(Button button, int player = 0);

    // --- audio (SPEC-8 §4: 4 channels, 64 SFX slots, 64 music patterns) ---

    /// <summary>
    /// Plays sound effect <paramref name="id"/> (0-63) from the cartridge's SFX bank.
    /// <paramref name="channel"/> 0-3 plays on that channel, cutting off whatever was there;
    /// -1, the default, picks the lowest channel that is idle, failing that the lowest one the
    /// music is using (the music takes it back at its next pattern), and failing that plays
    /// nothing — all four channels are already busy with the game's own sounds.
    /// An id outside 0-63, a channel outside -1..3 and an empty slot are silent no-ops.
    /// <para><b>Changes simulation state.</b> Call it from Update or Init, never from Draw:
    /// audio is part of what a replay reproduces, so a sound started while drawing would make
    /// a rewind diverge exactly the way a stray Rnd would (SPEC-8 §7; analyzer rule QRP1004).</para>
    /// </summary>
    void Sfx(int id, int channel = -1);

    /// <summary>
    /// Starts music pattern <paramref name="pattern"/> (0-63); a negative value — including the
    /// default — stops the music, and 64 or more does nothing. Each of the pattern's four
    /// channels plays on the chip channel of the same number, except one the cartridge's own
    /// <see cref="Sfx"/> is holding, which the music picks up at its next pattern. Patterns run
    /// in index order until one carries a stop or loop flag, or index 63 ends.
    /// <para><b>Changes simulation state</b>, exactly as <see cref="Sfx"/> does, and for the
    /// same reason must not be called from Draw.</para>
    /// </summary>
    void Music(int pattern = -1);

    // --- deterministic random (xoshiro128**, seeded via splitmix64 — SPEC-8 §7) ---

    /// <summary>Uniform Fix in [0, max); a max of 0 or less returns 0. Consumes exactly one RNG draw.</summary>
    Fix Rnd(Fix max);

    /// <summary>Uniform int in [0, maxExclusive); a bound of 0 or less returns 0. Consumes exactly one RNG draw.</summary>
    int RndInt(int maxExclusive);

    /// <summary>Reseeds the RNG; the same seed always replays the same sequence (part of the simulation).</summary>
    void Srand(int seed);

    // --- persistence (64 slots; second simulation input, snapshotted by replays) ---

    /// <summary>Reads persistent slot 0-63 (0 when never written); an out-of-range slot reads 0.</summary>
    Fix Dget(int slot);

    /// <summary>Writes persistent slot 0-63; an out-of-range slot is ignored. The shell saves it to disk.</summary>
    void Dset(int slot, Fix value);

    /// <summary>Ticks elapsed since Init (tick 0). 60 per game second.</summary>
    int Ticks { get; }
}
