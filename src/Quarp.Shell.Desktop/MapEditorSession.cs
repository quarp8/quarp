using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The map editing session of one cartridge <b>folder</b> — the headless model behind the
/// tilemap tab (M9 stage 3, wave 3a). It owns the one payload the console reads for the map,
/// and nothing else: no window, no renderer, no mode.
///
/// <para><b>Sprite flags moved out in wave 3b-1.</b> The owner's verdict (all three reference
/// consoles author flags in the SPRITE editor, not the map editor — PICO-8's row of circles,
/// TIC-80's advanced mode and LIKO-12's icon row all live beside the sheet, not the grid) sent
/// <c>flags.bin</c> to <see cref="SpriteEditorSession"/>, where a flag is a property of the
/// sprite currently open on the canvas. This class never opens <c>flags.bin</c> and holds not
/// one byte of it — see <see cref="SpriteEditorSession"/> for the bank's rules, which moved
/// with it unchanged.</para>
///
/// <para><b>The writer is not an encoder.</b> <c>map.bin</c> is 18 432 flat tile bytes,
/// row-major, cell <c>(x, y)</c> at <c>y * 256 + x</c> (MAP-FORMAT §1–§2). It has no magic, no
/// version and no header, so the payload <em>is</em> the file: this class reads bytes and
/// writes the same bytes back, and there is no new format code anywhere in it. The only thing
/// the payload can be wrong about is its length, and that is checked on the way in and again on
/// the way out, by the one helper <see cref="RequirePayload"/> — break it and the length test
/// goes red.</para>
///
/// <para><b>Absent file = zeros, and that is not dirty.</b> A brand-new cart has no
/// <c>map.bin</c>; it opens as 18 432 zeros, which is exactly what <see cref="CartSource"/>
/// hands the console for the same folder (SPEC-8 §6: missing optional asset = zeros). The file
/// is created only by the first dirty save, so opening the tilemap tab on a cart that has no
/// map cannot leave one behind.</para>
///
/// <para><b>A cart with map.csv has a read-only map.</b> The fact "this cartridge's map" has one
/// owner; while the text source lies in the folder, the owner is the Tiled/`quarp map build`
/// path, and a dirty <c>map.bin</c> write would silently stale it — the same class of lie that
/// <c>CartSource.RequireBuiltAsset</c> already refuses in the other direction. So the map bank
/// refuses edits at the door (<see cref="PaintTile"/> throws) and <see cref="MapReadOnly"/> is a
/// visible property the editor is expected to say out loud, the way the library says
/// "read-only: unpack to a folder to edit" for a .quarp8.</para>
///
/// <para><b>Undo is one stack over the map bank, and a step is an operation.</b> A whole
/// snapshot is 18 432 bytes — cheaper than one sprite-editor undo entry — so there is no delta
/// encoding and no per-cell bookkeeping: a step restores the whole map. A pencil gesture is one
/// step however many cells it crossed (<see cref="BeginStroke"/>/<see cref="EndStroke"/>).</para>
/// </summary>
public sealed class MapEditorSession
{
    /// <summary>The binary the console reads. One name owner: the constructor reads it, <see cref="Save"/> writes it, tests point at it.</summary>
    public const string MapFileName = "map.bin";

    /// <summary>The authoring text source whose presence makes the map read-only (MAP-FORMAT §4).</summary>
    public const string MapSourceFileName = "map.csv";

    /// <summary>Map width in cells — 256, from the one owner of map geometry.</summary>
    public const int MapColumns = CartData.MapWidth;

    /// <summary>Map height in cells — 72.</summary>
    public const int MapRows = CartData.MapHeight;

    /// <summary>
    /// Exactly 18 432 bytes (MAP-FORMAT §2) — borrowed from <see cref="MapTextCompiler.PayloadSize"/>,
    /// which owned the number before this editor existed. The audit of 2026-08-24 caught this
    /// re-deriving it as <c>MapColumns * MapRows</c> under a doc-comment that told everyone else
    /// not to: a second owner of a fact is a second owner even when it agrees today.
    /// </summary>
    public const int MapPayloadSize = MapTextCompiler.PayloadSize;

    /// <summary>256 sprites in the sheet (SPEC-8 §3) — the tile picker's range; no byte value is ever an illegal tile.</summary>
    public const int SpriteCount = VirtualConsole.SpriteCount;

    /// <summary>The empty cell (MAP-FORMAT §2) — named, because the eraser button, the right mouse button and <see cref="ClearArea"/> all mean this one fact.</summary>
    public const int EmptyTile = 0;

    private readonly string _mapPath;

    /// <summary>What the disk holds: the dirty comparison's baseline, replaced on save. Never aliases <see cref="_map"/>.</summary>
    private byte[] _savedMap;

    // The live map. Mutated in place by the tools and replaced wholesale by undo/redo, so
    // nothing may cache a reference to it across a step — every access goes through the field.
    private byte[] _map;

    // A snapshot per step: the whole map, cheap enough at 18 432 bytes that delta encoding buys
    // nothing worth the risk of getting it wrong.
    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();

    /// <summary>Pre-stroke map while the paint button is down; null between strokes.</summary>
    private byte[]? _strokeBackup;
    private bool _strokeChanged;

    /// <summary>
    /// Opens the map of a cartridge folder (.quarp8 files never get here — the mode machine
    /// refuses them with the read-only line, exactly as for the sprite editor). The file is
    /// optional: absent means zeros and a clean session. A file of the wrong length is refused
    /// here with <see cref="CartLoadException"/>, the same failure and the same wording
    /// <see cref="CartSource"/> produces for the same file, so the library reports it the way
    /// it reports a broken launch.
    /// </summary>
    public MapEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _mapPath = Path.Combine(cartFolder, MapFileName);
        MapReadOnly = File.Exists(Path.Combine(cartFolder, MapSourceFileName));
        _savedMap = ReadPayload(_mapPath, MapPayloadSize, MapFileName);
        _map = (byte[])_savedMap.Clone();
    }

    /// <summary>Folder name, for the header — the manifest is deliberately not read, same call as <see cref="SpriteEditorSession"/>.</summary>
    public string CartName { get; }

    /// <summary>
    /// True when <c>map.csv</c> lies beside the map: the map bank is then read-only and the
    /// editor must say so before the author starts drawing. Observable on purpose — a surprise
    /// at save time is the thing this property exists to prevent.
    /// </summary>
    public bool MapReadOnly { get; }

    /// <summary>The live map, row-major 256x72 tile bytes — what the canvas draws and what <see cref="Save"/> writes verbatim.</summary>
    public ReadOnlySpan<byte> Map => _map;

    /// <summary>
    /// The sprite the pencil stamps — session state, not panel state, so the tile picker cannot
    /// forget what the author last chose across a redraw. Tile 0 is emptiness rather than
    /// sprite 0 (MAP-FORMAT §2), and selecting it is how the author erases.
    /// </summary>
    public int SelectedSprite { get; private set; }

    /// <summary>
    /// How many picker cells wide the pencil's block is — TIC-80's <c>map->sheet.rect.w</c>
    /// (REFERENCES-EDITORS §3.1: "любой размер N×M, не только 2×2/4×4"), 1 until a drag across
    /// the picker says otherwise. It lives beside <see cref="SelectedSprite"/> because it is
    /// the same fact made bigger — "what the pencil puts down" — and a second home for it
    /// would be a second owner: every writer of the tile (the eyedropper, Shift+arrows, the
    /// empty-tile button) means <b>one</b> tile, and they all go through
    /// <see cref="SelectSprite"/>, which resets this to 1. That is what makes a stale block
    /// size impossible rather than merely unlikely.
    ///
    /// <para><b>The unit is a PICKER cell, not a sheet cell.</b> The author drags a rectangle
    /// across the strip he can see, and the strip is <see cref="SheetStrip"/>'s presentation of
    /// the sheet, not the sheet itself. This class must not know that mapping (it is a view's
    /// business, one layer up), so it keeps the two numbers and nothing else: whoever stamps
    /// the block resolves its cells through <see cref="SheetStrip"/> and hands the tiles down
    /// to <see cref="PaintBlock"/> as plain bytes.</para>
    /// </summary>
    public int BlockWidth { get; private set; } = 1;

    /// <summary>How many picker cells tall the pencil's block is — <see cref="BlockWidth"/>'s other half.</summary>
    public int BlockHeight { get; private set; } = 1;

    /// <summary>True while the paint button is down — the current undo step is open.</summary>
    public bool StrokeActive => _strokeBackup is not null;

    /// <summary>True when the live map differs from what the disk holds.</summary>
    public bool IsMapDirty => !_map.AsSpan().SequenceEqual(_savedMap);

    /// <summary>Alias for <see cref="IsMapDirty"/> — what the save contract asks. Kept as its own name because a future writer of a second bank in this class would need the distinction back.</summary>
    public bool IsDirty => IsMapDirty;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Bumped on every change to the map (paint, undo, redo) so a renderer can re-upload only when something moved.</summary>
    public int Version { get; private set; }

    /// <summary>Why the last save failed, or null. A save the author believes happened but did not is data loss, so it has to be sayable.</summary>
    public string? SaveError { get; private set; }

    /// <summary>Tile byte at a map cell. Out-of-map coordinates throw: the viewport clamps its own loops, and a silent 0 here would hide a windowing bug.</summary>
    public byte TileAt(int cellX, int cellY)
    {
        ValidateCell(cellX, cellY);
        return _map[cellY * MapColumns + cellX];
    }

    /// <summary>
    /// Tile picker click. Throws rather than masks: a value outside 0-255 is a caller bug, and
    /// masking it would stamp a sprite the author never chose. Legal even on a read-only map —
    /// choosing a tile writes nothing.
    /// </summary>
    public void SelectSprite(int sprite)
    {
        ValidateSprite(sprite);
        SelectedSprite = sprite;
        // One tile is one tile: every path that names a single sprite — a click on one picker
        // cell, Shift+arrows, the eyedropper, the empty-tile button — arrives here, so the
        // block cannot survive one of them and scramble the next stroke. Growing it back is
        // SelectSpriteBlock's job and nothing else's.
        BlockWidth = 1;
        BlockHeight = 1;
    }

    /// <summary>
    /// The picker drag's door: the block's top-left tile and its size in picker cells
    /// (TIC-80 <c>map->sheet.rect</c>). The sprite is validated as everywhere else; a size
    /// below one cell is a caller bug rather than an empty block, because the picker's own
    /// rectangle is normalized before it gets here and can never be thinner than the cell it
    /// started on. The strip's far edge is the caller's clamp — this class does not know where
    /// the strip ends (see <see cref="BlockWidth"/>).
    /// </summary>
    public void SelectSpriteBlock(int sprite, int width, int height)
    {
        ValidateSprite(sprite);
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), (width, height), "a picker block is at least one cell by one cell.");
        }
        SelectedSprite = sprite;
        BlockWidth = width;
        BlockHeight = height;
    }

    /// <summary>
    /// Right button on the canvas: the pencil takes the tile under the cursor (the sprite
    /// editor's eyedropper, one bank over). Reads only, so a read-only map allows it — being
    /// unable to copy a tile out of digger's map would be a pointless second punishment.
    /// </summary>
    public void PickTile(int cellX, int cellY)
    {
        ValidateCell(cellX, cellY);
        // Through the one door rather than at the field: a sampled cell is one tile, so it must
        // also drop the block (see SelectSprite). A byte is always a legal sprite, so the
        // validation inside cannot fire.
        SelectSprite(_map[cellY * MapColumns + cellX]);
    }

    /// <summary>
    /// Paint button pressed on the canvas. The pre-operation map is snapshotted here and
    /// becomes the undo entry when the stroke ends — the whole "one gesture = one step"
    /// mechanism: nothing inside the stroke touches the undo stack.
    /// </summary>
    public void BeginStroke()
    {
        if (StrokeActive)
        {
            return;     // A second press without a release (focus-loss glitches) folds into the open stroke.
        }
        _strokeBackup = (byte[])_map.Clone();
        _strokeChanged = false;
    }

    /// <summary>
    /// One pencil sample at a map cell: the selected sprite becomes that cell's tile. The cast
    /// to byte cannot truncate — <see cref="SelectSprite"/> is the only door into
    /// <see cref="SelectedSprite"/> and it refuses anything outside 0-255.
    /// </summary>
    public void PaintTile(int cellX, int cellY) => PaintTile(cellX, cellY, SelectedSprite);

    /// <summary>
    /// One pencil sample with the tile named outright — how the right mouse button erases
    /// (REFERENCES-EDITORS §7.3: LIKO-12 forces the tile to 0 under button 2 and leaves the
    /// picker alone). A parameter and not a second field: an "erase mode" would be a second
    /// owner of what the pencil is putting down.
    /// </summary>
    public void PaintTile(int cellX, int cellY, int tile)
    {
        if (!StrokeActive)
        {
            throw new InvalidOperationException(
                "PaintTile outside a stroke — the shell must call BeginStroke on the press (SpriteEditorSession.Paint's contract).");
        }
        RequireWritableMap();
        ValidateCell(cellX, cellY);
        ValidateSprite(tile);
        WriteCell(cellY * MapColumns + cellX, (byte)tile);
    }

    /// <summary>
    /// The fill tool (wave 3d): the connected run of cells holding the same value as the one
    /// under the cursor becomes <paramref name="tile"/>. TIC-80's <c>fillMap</c> and LIKO-12's
    /// <c>queuedFill</c>, and like both of them an explicit stack rather than recursion — the
    /// map is 18 432 cells and a one-value map fills every one of them, which is a call depth
    /// no stack survives (REFERENCES-EDITORS §3.1: <c>FILL_STACK_SIZE</c> is the whole map).
    ///
    /// <para>Connectivity is four-way, and the map's edges are edges: TIC-80 wraps its map in
    /// <c>normalizeMap</c> and we do not, because <c>Mget</c> here does not wrap either — a
    /// fill that leaked around the seam would paint the far side of the level. Filling with the
    /// value already there returns before touching anything: no bytes, no <see cref="Version"/>
    /// bump and <b>no undo step</b>, the same rule <see cref="EndStroke"/> applies to an idle
    /// pencil click.</para>
    /// </summary>
    public void Fill(int cellX, int cellY, int tile)
    {
        RequireWritableMap();
        ValidateCell(cellX, cellY);
        ValidateSprite(tile);
        int start = cellY * MapColumns + cellX;
        byte target = _map[start];
        byte replacement = (byte)tile;
        if (target == replacement)
        {
            return;
        }
        EndStroke();        // an operation is its own step: whatever gesture was open commits first
        BeginStroke();
        FloodFill(start, target, replacement);
        EndStroke();
    }

    /// <summary>The fill tool with the picker's tile — what the left button means.</summary>
    public void Fill(int cellX, int cellY) => Fill(cellX, cellY, SelectedSprite);

    /// <summary>
    /// <c>Del</c> over a marked rectangle: every cell in it becomes <see cref="EmptyTile"/>,
    /// as one undo step. TIC-80's <c>deleteSelection</c> (REFERENCES-EDITORS §7.3), including
    /// its shape — the editor has no eraser tool, it has this and a tile numbered zero. An
    /// empty rectangle is a no-op rather than a throw (the caller is a key press that may
    /// arrive with nothing marked); a rectangle that leaves the map IS a throw, by
    /// <see cref="ValidateCell"/>, for the same reason painting outside it is.
    /// </summary>
    public void ClearArea(int cellX, int cellY, int width, int height)
    {
        RequireWritableMap();
        if (width <= 0 || height <= 0)
        {
            return;
        }
        ValidateCell(cellX, cellY);
        ValidateCell(cellX + width - 1, cellY + height - 1);
        EndStroke();
        BeginStroke();
        for (int y = cellY; y < cellY + height; y++)
        {
            for (int x = cellX; x < cellX + width; x++)
            {
                WriteCell(y * MapColumns + x, EmptyTile);
            }
        }
        EndStroke();
    }

    /// <summary>
    /// One sample of a pencil gesture that carries a <b>block</b> of tiles instead of one
    /// (wave 3e; TIC-80 <c>setMapSprite</c> under a <c>map->sheet.rect</c> larger than 1x1).
    /// Inside an open stroke like <see cref="PaintTile"/>, and for the same reason: however
    /// many blocks a drag stamps, the gesture is one undo step.
    ///
    /// <para><paramref name="tiles"/> is the block row-major, <paramref name="width"/> bytes
    /// per row. It arrives as plain sprite numbers because resolving the picker's rectangle
    /// into them needs <see cref="SheetStrip"/>, which is a view's business — see
    /// <see cref="BlockWidth"/>.</para>
    /// </summary>
    public void PaintBlock(int cellX, int cellY, int width, int height, ReadOnlySpan<byte> tiles)
    {
        if (!StrokeActive)
        {
            throw new InvalidOperationException(
                "PaintBlock outside a stroke — the shell must call BeginStroke on the press (PaintTile's contract).");
        }
        RequireWritableMap();
        BlitBlock(cellX, cellY, width, height, tiles);
    }

    /// <summary>
    /// The paste (wave 3e): a block of tiles lands at a cell as <b>one</b> undo step, whatever
    /// gesture was open (TIC-80 <c>drawPasteData</c>, which pushes exactly one history entry).
    /// Same shape as <see cref="ClearArea"/> — end, begin, write, end — because both are
    /// operations rather than gestures.
    /// </summary>
    public void PasteBlock(int cellX, int cellY, int width, int height, ReadOnlySpan<byte> tiles)
    {
        RequireWritableMap();
        if (width <= 0 || height <= 0)
        {
            return;
        }
        EndStroke();
        BeginStroke();
        BlitBlock(cellX, cellY, width, height, tiles);
        EndStroke();
    }

    /// <summary>
    /// A rectangle of tiles onto the map, <b>clipped</b> at the map's borders — the one place
    /// this class does not throw on an out-of-map coordinate, and deliberately so. The block's
    /// position comes from a pointer that may stand one cell from the edge while the block is
    /// eight wide: refusing would make the map's own corner unpaintable and unpasteable, and
    /// snapping the block back inside would put tiles where the author did not point. Cells
    /// that fall off the map are simply not written; the rest go through the one writer.
    /// A single cell still goes through <see cref="PaintTile"/>, which still throws — the
    /// pencil's contract is unchanged, and the viewport clamps before calling it.
    /// </summary>
    private void BlitBlock(int cellX, int cellY, int width, int height, ReadOnlySpan<byte> tiles)
    {
        if (tiles.Length < width * height)
        {
            throw new ArgumentException(
                $"a {width}x{height} block needs {width * height} tiles, got {tiles.Length}.", nameof(tiles));
        }
        for (int row = 0; row < height; row++)
        {
            int y = cellY + row;
            if (y is < 0 or >= MapRows)
            {
                continue;
            }
            for (int column = 0; column < width; column++)
            {
                int x = cellX + column;
                if (x is < 0 or >= MapColumns)
                {
                    continue;
                }
                WriteCell(y * MapColumns + x, tiles[row * width + column]);
            }
        }
    }

    /// <summary>
    /// The one hand that writes a map byte. Every tool — pencil, fill, the Del rectangle —
    /// goes through here, so "a cell changed" means exactly one thing: the dirt, the
    /// <see cref="Version"/> the renderers watch and the stroke's changed-flag move together
    /// or not at all. Re-stamping the same value is not a change, which is what keeps an idle
    /// click out of the undo stack.
    /// </summary>
    /// <returns>True when the byte actually moved.</returns>
    private bool WriteCell(int offset, byte tile)
    {
        if (_map[offset] == tile)
        {
            return false;
        }
        _map[offset] = tile;
        _strokeChanged = true;
        Version++;
        return true;
    }

    /// <summary>
    /// Four-way flood fill over an explicit stack. A cell is written the moment it is pushed,
    /// so the map itself is the visited set and no cell can ever be pushed twice — which is
    /// what bounds the stack at <see cref="MapPayloadSize"/> entries instead of hoping.
    /// </summary>
    private void FloodFill(int startOffset, byte target, byte replacement)
    {
        int[] stack = new int[MapPayloadSize];
        int top = 0;
        WriteCell(startOffset, replacement);
        stack[top++] = startOffset;
        while (top > 0)
        {
            int offset = stack[--top];
            int x = offset % MapColumns;
            int y = offset / MapColumns;
            if (x > 0 && _map[offset - 1] == target && WriteCell(offset - 1, replacement))
            {
                stack[top++] = offset - 1;
            }
            if (x < MapColumns - 1 && _map[offset + 1] == target && WriteCell(offset + 1, replacement))
            {
                stack[top++] = offset + 1;
            }
            if (y > 0 && _map[offset - MapColumns] == target && WriteCell(offset - MapColumns, replacement))
            {
                stack[top++] = offset - MapColumns;
            }
            if (y < MapRows - 1 && _map[offset + MapColumns] == target
                && WriteCell(offset + MapColumns, replacement))
            {
                stack[top++] = offset + MapColumns;
            }
        }
    }

    /// <summary>
    /// Button released: the gesture commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z look dead). Safe to call without an open stroke: releases arrive when the press
    /// landed outside the canvas.
    /// </summary>
    public void EndStroke()
    {
        if (_strokeBackup is not byte[] backup)
        {
            return;
        }
        _strokeBackup = null;
        if (!_strokeChanged)
        {
            return;
        }
        _undo.Add(backup);
        _redo.Clear();      // The redone future described a map that no longer exists.
    }

    /// <summary>
    /// Ctrl+Z. Ends an open stroke first (committing it), so an undo mid-drag rolls back a whole
    /// gesture instead of tearing one in half. Whole-map swaps, no copying: the snapshots
    /// already exist and nothing else holds them. History lives in the session only: closing the
    /// tab forgets it, and a fresh session opens with Ctrl+Z honestly dead.
    /// </summary>
    public void Undo()
    {
        EndStroke();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(_map);
        byte[] previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _map = previous;
        Version++;
    }

    /// <summary>Ctrl+Y — the exact mirror of <see cref="Undo"/>.</summary>
    public void Redo()
    {
        EndStroke();
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(_map);
        byte[] next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _map = next;
        Version++;
    }

    /// <summary>
    /// Ctrl+S. The clean guard is the save contract's heart: a session whose map equals the
    /// disk writes <b>nothing</b> — open-and-close leaves the file untouched and, for a cart
    /// that never had one, uncreated, and a repeated Ctrl+S is a no-op. That is what keeps the
    /// pinned demo maps (carts/digger, carts/platformer) byte-identical after the editor has
    /// opened them, and the read-only rule above is the second lock on the same door.
    ///
    /// <para>Disk failures land in <see cref="SaveError"/> instead of throwing, because a full
    /// disk must leave the author their work and a message. A read-only map that is somehow
    /// dirty, or a payload of the wrong length, are contract violations rather than accidents:
    /// those throw.</para>
    /// </summary>
    /// <returns>True when the disk now matches the map (including "already did"), false when a write failed.</returns>
    public bool Save()
    {
        EndStroke();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        if (MapReadOnly)
        {
            // Unreachable while PaintTile is the only writer of the map bank — that door slams
            // first. Kept as the second gate, because "the map file is owned by map.csv" is a
            // save-time promise and the next wave's tools will be new writers.
            throw new InvalidOperationException(
                $"{CartName}: {MapFileName} is read-only while {MapSourceFileName} is present — "
                + $"the text source owns the map. Remove {MapSourceFileName} to edit the map inside Quarp.");
        }
        try
        {
            RequirePayload(_map, MapPayloadSize, MapFileName);
            File.WriteAllBytes(_mapPath, _map);
            _savedMap = (byte[])_map.Clone();
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    private void RequireWritableMap()
    {
        if (MapReadOnly)
        {
            throw new InvalidOperationException(
                $"{CartName}: the map is read-only while {MapSourceFileName} is present — "
                + $"the text source owns it (MAP-FORMAT §4). Remove {MapSourceFileName} to edit the map inside Quarp.");
        }
    }

    private static void ValidateCell(int cellX, int cellY)
    {
        if (cellX is < 0 or >= MapColumns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellX), cellX, $"map columns are 0-{MapColumns - 1} (MAP-FORMAT §2); the viewport clamps before calling in.");
        }
        if (cellY is < 0 or >= MapRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellY), cellY, $"map rows are 0-{MapRows - 1} (MAP-FORMAT §2); the viewport clamps before calling in.");
        }
    }

    private static void ValidateSprite(int sprite)
    {
        if (sprite is < 0 or >= SpriteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sprite), sprite, $"the sheet holds sprites 0-{SpriteCount - 1} (SPEC-8 §3).");
        }
    }

    /// <summary>Absent file = zeros (SPEC-8 §6); present file = its bytes, length-checked by the one helper below.</summary>
    private static byte[] ReadPayload(string path, int expectedLength, string name)
    {
        if (!File.Exists(path))
        {
            return new byte[expectedLength];
        }
        byte[] bytes = File.ReadAllBytes(path);
        RequirePayload(bytes, expectedLength, name);
        return bytes;
    }

    /// <summary>
    /// The only thing a flat payload can be wrong about (MAP-FORMAT §3: there are no illegal
    /// tile values, so length is the whole validator). One owner for both directions — the load
    /// and the save — so the check cannot pass on the way in and be missing on the way out. The
    /// message matches <c>CartSource</c>'s for the same file, because the author reads it in
    /// both places.
    /// </summary>
    private static void RequirePayload(byte[] payload, int expectedLength, string name)
    {
        if (payload.Length != expectedLength)
        {
            throw new CartLoadException($"{name}: {payload.Length} bytes, must be exactly {expectedLength}.");
        }
    }
}
