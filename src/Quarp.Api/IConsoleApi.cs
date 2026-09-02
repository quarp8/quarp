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
    /// legal in <c>Draw</c> and, unlike <see cref="Sfx(int, int)"/> or <see cref="Rnd"/>, invisible to
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
    /// Length in bytes of data bank <paramref name="bank"/> (0-63), or 0 for an empty bank and
    /// for a number outside that range (ADR-035).
    /// </summary>
    int DataLength(int bank);

    /// <summary>
    /// Reads one byte of a data bank. Outside the bank — and for a bank number outside 0-63 —
    /// reads 0, the same soft geometry <see cref="Mget"/> and <see cref="Sget"/> have (ADR-035).
    /// Reading is pure, so it is legal in <c>Draw</c>.
    /// </summary>
    byte DataGet(int bank, int offset);

    /// <summary>
    /// Copies <paramref name="count"/> bytes of a data bank into the sprite sheet, starting at
    /// sheet pixel <paramref name="pixel"/> counted row-major from the top-left (ADR-035).
    /// Whatever part of the request falls outside the bank or the sheet is dropped.
    ///
    /// <para>This changes console state, so like <see cref="Mset"/> it is a build error inside
    /// <c>Draw</c> (analyzer rule QRP1004).</para>
    /// </summary>
    void DataToGfx(int bank, int offset, int pixel, int count);

    /// <summary>
    /// Copies <paramref name="count"/> bytes of a data bank into the map, starting at map cell
    /// <paramref name="cell"/> counted row-major (ADR-035). Clipped like
    /// <see cref="DataToGfx"/>, and like it a build error inside <c>Draw</c>.
    /// </summary>
    void DataToMap(int bank, int offset, int cell, int count);

    /// <summary>
    /// Replaces the cartridge's whole SFX table — all 64 slots — with the 4352-byte payload
    /// lying at <paramref name="offset"/> in data bank <paramref name="bank"/>: the sound
    /// counterpart of <see cref="DataToGfx"/>, and what a game too big for one set of banks
    /// pages an episode's sound in with (ADR-036). The bytes are <c>sfx.bin</c>'s payload,
    /// header already stripped — 64 four-byte slot headers followed by 64 x 32 little-endian
    /// step words (docs/AUDIO-FORMAT.md §2, §3).
    ///
    /// <para><b>The whole table, not a range.</b> There is no slot argument and no count: a
    /// payload is one record of a defined length, and a partial copy would let a cartridge
    /// leave a slot torn in half — a four-byte header describing steps that belong to
    /// something else, which is a sound nobody wrote.</para>
    ///
    /// <para><b>It silences the chip first.</b> All four channels stop and the music stops,
    /// as if <c>Sfx(-1)</c> and <c>Music(-1)</c> had been called just before. A channel that
    /// kept going would be stepping through a slot that no longer exists, at a step index the
    /// new slot's length says nothing about.</para>
    ///
    /// <para><b>All or nothing.</b> If the bank does not hold 4352 whole bytes from
    /// <paramref name="offset"/> — a bank number outside 0-63, a negative offset, an empty or
    /// short bank — the call does nothing at all: the table keeps the sound it had, and
    /// nothing is silenced either. Check with <see cref="DataLength"/> when the bank might be
    /// missing. This is the one place the data-bank calls part company with
    /// <see cref="DataToGfx"/>, which copies the intersection and drops the rest: half a
    /// sprite sheet is still a picture, half a sound table is not.</para>
    ///
    /// <para>This changes simulation state — sound is what a resimulation has to reproduce —
    /// so like <see cref="DataToGfx"/> it is a build error inside <c>Draw</c> (analyzer rule
    /// QRP1004). Paging lives in <c>Init</c> and <c>Update</c>.</para>
    /// </summary>
    void DataToSfx(int bank, int offset);

    /// <summary>
    /// Replaces the cartridge's whole music table — all 64 patterns — with the 320-byte
    /// payload at <paramref name="offset"/> in data bank <paramref name="bank"/>: 64 x 4
    /// channel bytes followed by 64 pattern-flag bytes, <c>music.bin</c> without its header
    /// (docs/AUDIO-FORMAT.md §4). The three rules of <see cref="DataToSfx"/> hold word for
    /// word — the whole table, silence first, all or nothing — and so does the ban inside
    /// <c>Draw</c> (ADR-036).
    /// </summary>
    void DataToMusic(int bank, int offset);

    /// <summary>
    /// Draws text with the small system font (3x5 ink in a 4x6 cell) and returns the x
    /// coordinate after the last glyph (decided: yes, API-8 §reviewed) — that is, x + 4 per
    /// printed character, which is what a caller chains or centers with. '\n' starts a new line
    /// at the original x; characters outside ASCII 32-126 and the PICO-8 symbol block
    /// (arrows, buttons, hearts and friends — ADR-038) draw a hollow box.
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

    /// <summary>
    /// Display stage, set part: inside set <paramref name="set"/> (0-3), master colour
    /// <paramref name="color"/> (0-31) is <b>shown</b> as master colour <paramref name="shown"/>
    /// (0-31).
    /// <para><see cref="Pal(byte, byte)"/> changes what colour you <em>draw</em> with from now
    /// on; this changes what colour the pixels already on screen are <em>shown</em> in. Nothing
    /// in the framebuffer moves — one call recolours the whole finished frame, including what was
    /// drawn before the call — so a fade, a night wash or a flash costs one call instead of
    /// redrawing the scene.</para>
    /// </summary>
    void Pald(byte set, byte color, byte shown);

    /// <summary>Resets one display set (0-3) to the identity map; the row selector is untouched.</summary>
    void Pald(byte set);

    /// <summary>Resets all four display sets to the identity map; the row selector is untouched.</summary>
    void Pald();

    /// <summary>
    /// Display stage, row part: scanline <paramref name="y"/> is shown through set
    /// <paramref name="set"/> (0-3). A row outside the screen is ignored, like any off-screen
    /// write. This is what makes a horizon, a heat band or a two-palette split screen a property
    /// of the picture rather than of the drawing code.
    /// </summary>
    void Palr(int y, byte set);

    /// <summary>
    /// Shows <paramref name="height"/> scanlines from <paramref name="y"/> through set
    /// <paramref name="set"/> — position plus size, the shape <see cref="Clip(int, int, int, int)"/> uses, clamped to
    /// the screen; a non-positive height does nothing.
    /// </summary>
    void Palr(int y, int height, byte set);

    /// <summary>Resets the row selector: every scanline is shown through set 0 again.</summary>
    void Palr();

    // --- input (SPEC-8 §5: 2 players max) ---

    /// <summary>True while the button is held on this tick; an unknown player reads false.</summary>
    bool Btn(Button button, int player = 0);

    /// <summary>True only on the tick the button went down (held now, not held on the previous tick).</summary>
    bool Btnp(Button button, int player = 0);

    // --- pointer (SPEC-8 §5, ADR-030: mouse and touch are the same pointer) ---

    /// <summary>
    /// Pointer x in console screen pixels, clamped to 0..<see cref="ScreenWidth"/>-1. The shell
    /// does the window-to-console translation; the cartridge never sees the window's scale or
    /// letterbox. A pointer off the picture reads as the nearest edge pixel (ADR-030 п.6) —
    /// there is no "off screen" answer in v1.
    /// Like <see cref="Btn"/>, this reads the tick's input snapshot: two reads in one tick give
    /// one number, and Draw sees the last completed tick's value.
    /// </summary>
    int MouseX { get; }

    /// <summary>Pointer y in console screen pixels, clamped to 0..<see cref="ScreenHeight"/>-1; see <see cref="MouseX"/>.</summary>
    int MouseY { get; }

    /// <summary>True while the pointer button is held on this tick; an unknown button reads false.</summary>
    bool MouseBtn(MouseButton button);

    /// <summary>True only on the tick the pointer button went down (held now, not held on the previous tick) — <see cref="Btnp"/>'s rule, no auto-repeat.</summary>
    bool MouseBtnp(MouseButton button);

    /// <summary>
    /// Wheel steps turned on this tick, signed: positive is away from the user ("up"), zero
    /// means the wheel did not move. A delta, not an accumulator — a cartridge that wants a
    /// position sums it itself.
    /// </summary>
    int MouseWheel { get; }

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
    /// Plays a <em>segment</em> of sound effect <paramref name="id"/>: steps
    /// <paramref name="offsetSteps"/> up to but not including <paramref name="offsetSteps"/> +
    /// <paramref name="lengthSteps"/>, once, ignoring the slot's loop (ADR-037) — PICO-8's
    /// <c>sfx(n, ch, o, l)</c>, for the cartridge that packs several short sounds into one
    /// 32-step slot because all 64 are spoken for. <paramref name="channel"/> follows the
    /// two-argument call exactly, -1 auto-pick included, and so does <paramref name="id"/> = -1:
    /// it stops, the segment arguments ignored — one rule, not one per overload.
    /// <para>Soft edges like everything here: an offset outside the slot's played steps plays
    /// nothing at all, a non-positive length plays nothing, and a segment overhanging the
    /// slot's end is clipped to it. Within the segment nothing sounds different — effects and
    /// arpeggio groups read the slot exactly as they do mid-slot today.</para>
    /// <para><b>Changes simulation state</b>, exactly as the two-argument call does, and for
    /// the same reason must not be called from Draw (QRP1004).</para>
    /// </summary>
    void Sfx(int id, int channel, int offsetSteps, int lengthSteps);

    /// <summary>
    /// Starts music pattern <paramref name="pattern"/> (0-63); a negative value — including the
    /// default — stops the music, and 64 or more does nothing. Each of the pattern's four
    /// channels plays on the chip channel of the same number, except one the cartridge's own
    /// <see cref="Sfx(int, int)"/> is holding, which the music picks up at its next pattern. Patterns run
    /// in index order until one carries a stop or loop flag, or index 63 ends.
    /// <para><b>Changes simulation state</b>, exactly as <see cref="Sfx(int, int)"/> does, and for the
    /// same reason must not be called from Draw.</para>
    /// </summary>
    void Music(int pattern = -1);

    /// <summary>
    /// <see cref="Music(int)"/> with a fade and an optional channel reservation (ADR-037) —
    /// PICO-8's <c>music(n, fade_ms, channel_mask)</c>, measured in ticks because everything
    /// on this console is. A positive <paramref name="fadeTicks"/> ramps the music's channels
    /// linearly from silence to full volume over that many ticks on a start, and from the
    /// current volume to silence — then stops — on <c>Music(-1, fadeTicks)</c>; the ramp is
    /// part of the PCM, so it is deterministic, replayable and in the audio hash. A non-zero
    /// <paramref name="channelMask"/> (bit i = channel i) reserves only those channels for the
    /// music: voices on the other channels are never started and stay the cartridge's for its
    /// own <see cref="Sfx(int, int)"/>, while still counting toward the pattern's length so song timing
    /// cannot depend on what else is playing. Zero fade and zero mask are
    /// <see cref="Music(int)"/> to the bit.
    /// <para><b>Changes simulation state</b>, exactly as <see cref="Music(int)"/> does, and
    /// for the same reason must not be called from Draw (QRP1004).</para>
    /// </summary>
    void Music(int pattern, int fadeTicks, int channelMask = 0);

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
