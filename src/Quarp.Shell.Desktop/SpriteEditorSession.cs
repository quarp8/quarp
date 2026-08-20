using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Which mouse tool the canvas click means. The eraser is the pencil with color 0 and the
/// eyedropper is the right button in every tool, so these are the only genuine canvas modes.
/// Shape joined in wave 2e (M9 stage 2.5, the owner's verdict), select and stamp in wave 2f;
/// the oval/rectangle and rectangle/brush choices are not tools but variants — the
/// photoshop-style group slots' memory. The state lives in the session, not the window,
/// so "what is active" is provable headless.
/// </summary>
public enum SpriteEditorTool
{
    Pencil,
    Fill,
    Shape,
    Select,
    Stamp,
}

/// <summary>
/// The transform group slot's variants (M9 stage 2.5, owner's second review: flip H, flip V
/// and rotate share ONE toolbar slot). Order is the flyout's left-to-right order and the
/// digit's cycle order; the values double as flyout indices, so the mapping to icons and
/// tooltips in <c>EditorIcons</c> is a cast, not a second table.
/// </summary>
public enum TransformVariant
{
    FlipH,
    FlipV,
    Rotate,
}

/// <summary>The shape group slot's variants — same contract as <see cref="TransformVariant"/>.</summary>
public enum ShapeVariant
{
    Oval,
    Rectangle,
}

/// <summary>
/// The select group slot's variants (wave 2f; the wand joined in 2g — the owner's third
/// review) — same contract as <see cref="TransformVariant"/>: the rectangle drags a box over
/// the mask, the brush strokes the mask the way the pencil strokes pixels, the wand takes the
/// 4-connected area of one color around the click (the bucket's own connectivity and walls —
/// one flood, two tools). The variant decides what a fresh press MARKS; a press over an
/// existing selection grabs and moves regardless of variant.
/// </summary>
public enum SelectionVariant
{
    Rectangle,
    Brush,
    Wand,
}

/// <summary>
/// The sprite editor's whole state and policy, with no window attached (M9 stage 2, waves
/// 2b/2c) — the same split that made <see cref="ShellModeMachine"/> testable: <c>QuarpGame</c>
/// routes keys and mouse hits here, <see cref="SpriteEditorRenderer"/> paints what this says,
/// and every claim the work order makes (one stroke = one undo step, a clean session never
/// touches the disk, nothing above palette index 15 can enter the sheet) is provable headless.
///
/// <para><b>The 0-15 invariant has exactly four doors.</b> Pixels enter the sheet through
/// (1) the load in the constructor — <see cref="PngDecoder.DecodeToPaletteIndices"/> only ever
/// emits matches against the 16 visible palette colors; (2) the pencil, the bucket and the
/// shape commit — which write <see cref="CurrentColor"/> (the shapes through the very same
/// <see cref="Plot"/> the pencil uses), and <see cref="SelectColor"/> throws on anything
/// outside 0-15 while <see cref="PickColor"/> copies a value already in the sheet;
/// (3) undo/redo — which swap whole arrays that were themselves sheets; (4) the region
/// edits — flips, the rotation, the selection move and the stamp only copy values already
/// read out of a sheet, and the clears (whole region, selected pixels, and the holes a move
/// leaves) write the literal 0. There is no fifth setter, so the byte casts in the plot and
/// fill routines can never truncate. The selection mask and the stamp source are session
/// state beside the sheet, not in it — marking and grabbing write no pixels at all, only
/// their commits do, through door (4). <see cref="PngEncoder"/> re-checks on save as the
/// owner of its own input contract; that check is unreachable from here by construction.</para>
///
/// <para><b>Dirty is content, not history.</b> <see cref="IsDirty"/> compares the live sheet
/// against a snapshot of what the disk holds (or held nothing — an all-zero sheet), because
/// the save contract is about bytes: undoing back to the loaded picture makes the session
/// clean again, and even hand-repainting a pixel to its old color counts, since saving then
/// would change nothing. A 16 KB compare per query costs microseconds and cannot drift out of
/// sync the way a depth counter under an undo/redo/new-stroke braid can.</para>
///
/// <para><b>The region can never hang off the sheet.</b> The size cycle (8/16/32 px a side)
/// and the grid click are the only two writers of the region, and both go through the same
/// clamp: the anchor stops at <c>GridCells - RegionCells</c>. That single invariant is what
/// lets every transform below read and write blindly through <see cref="SheetOffset"/> —
/// a rotation at the sheet's edge is exactly as safe as one in the middle, because an edge
/// region that would clip simply cannot be selected.</para>
/// </summary>
public sealed class SpriteEditorSession
{
    /// <summary>Sheet grid side in sprite cells — 16, from the one owner of sheet geometry.</summary>
    public const int GridCells = VirtualConsole.SheetColumns;

    private readonly string _gfxPath;

    /// <summary>The sheet the disk holds: the dirty comparison's baseline, replaced on save.</summary>
    private byte[] _saved;

    /// <summary>The live sheet. Replaced wholesale by undo/redo, mutated in place by the pencil.</summary>
    private byte[] _sheet;

    // Undo is a stack of pre-stroke sheets. At 16 KB a snapshot, a hundred strokes cost less
    // than one game texture, so there is no cap and no delta encoding — simplicity is what
    // keeps "undo restores exactly" beyond doubt.
    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();

    /// <summary>Pre-stroke sheet while the button is down; null between strokes.</summary>
    private byte[]? _strokeBackup;
    private bool _strokeChanged;
    private int _lastPaintX = -1;
    private int _lastPaintY;

    // The shape gesture's whole state: anchor (where the press landed), corner (where the drag
    // is now), the Ctrl-held "filled" flag, and the preview's point set. The points are THE
    // shape — the commit plots this very list, so the preview can never disagree with what
    // lands (one owner of the shape formula, per the playbook). None of it touches _sheet:
    // the preview lives here and is drawn by the renderer as an overlay, which is the whole
    // "предпросмотр в лист не пишется" contract made structural.
    private bool _shapeActive;
    private int _shapeAnchorX;
    private int _shapeAnchorY;
    private int _shapeCornerX;
    private int _shapeCornerY;
    private bool _shapeFilled;
    private readonly List<(int X, int Y)> _shapePoints = new();

    /// <summary>
    /// What an open select-tool press currently means. Rectangle, Brush and Wand are marking a
    /// NEW mask (the kind is fixed at the press — half a box cannot become half a stroke); Move
    /// is dragging the selected pixels by an offset. Exactly one can be open, like the shape
    /// gesture, and none of them touches <c>_sheet</c> — only <see cref="CommitSelect"/> does.
    /// </summary>
    private enum SelectGesture
    {
        None,
        Rectangle,
        Brush,
        Wand,
        Move,
    }

    // The selection's whole state (wave 2f). The mask is region-local booleans sized to the
    // region at creation and dropped whenever the region moves or resizes — a mask kept
    // across a region change would silently re-aim at foreign pixels. The float of an open
    // move is nothing but the clamped offset below: the renderer draws holes and the riding
    // fragment from it, and the sheet stays untouched until the drop — the same structure
    // that makes the shape preview unable to leak ("маска живёт в сессии, не в листе").
    private bool[]? _selection;
    private int _selectionCount;
    private SelectGesture _selectGesture;
    private int _selectAnchorX;     // rectangle anchor / the brush's last marked point / the wand's last pick / the grab point
    private int _selectAnchorY;
    private int _moveDx;
    private int _moveDy;
    private int _moveMinDx;         // offset bounds: the mask's bounding box may never leave the region,
    private int _moveMaxDx;         // so a drop cannot push pixels off it and lose them
    private int _moveMinDy;
    private int _moveMaxDy;

    // The stamp's source: the LAST committed selection's pixels, copied out at commit time
    // (the order's law — remembered automatically at creation). Bounding-box normalized,
    // 0 = transparent: a masked pixel that held 0 and a box cell outside the mask both print
    // nothing, PICO-8's color-0 pattern. Deliberately a copy and not a live view — it
    // survives region moves, later sheet edits and even Esc dropping the selection, because
    // it is the memory of what was selected, not the selection itself.
    private byte[]? _stampSource;
    private int _stampWidth;
    private int _stampHeight;

    /// <summary>
    /// Opens the sheet of a cartridge <b>folder</b> (.quarp8 files never get here — the mode
    /// machine refuses them with the read-only line). No gfx.png is the normal case, not an
    /// error: snake has none, and an all-zero sheet is exactly what its cart loads
    /// (Format spec v1: absent assets = zeros). The file is only ever created by the first
    /// dirty save. A corrupt PNG throws <see cref="CartLoadException"/> out of here so the
    /// library can report it the way it reports a broken launch.
    /// </summary>
    public SpriteEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _gfxPath = Path.Combine(cartFolder, "gfx.png");
        _saved = File.Exists(_gfxPath)
            ? PngDecoder.DecodeToPaletteIndices(
                File.ReadAllBytes(_gfxPath), CartData.GfxWidth, CartData.GfxHeight, "gfx.png")
            : new byte[CartData.GfxWidth * CartData.GfxHeight];
        _sheet = (byte[])_saved.Clone();
    }

    /// <summary>Folder name, for the header. The manifest is deliberately not read — same call as <see cref="CartLibraryEntry"/>.</summary>
    public string CartName { get; }

    /// <summary>The live sheet, row-major 128x128, values 0-15 — see the type comment for why nothing else can appear.</summary>
    public ReadOnlySpan<byte> Pixels => _sheet;

    /// <summary>The pencil's ink, always a visible palette index 0-15. Painting with 0 IS the eraser (work order: no separate tool).</summary>
    public int CurrentColor { get; private set; }

    /// <summary>What a left click on the canvas does — the footer names this, so switching tools is always visible.</summary>
    public SpriteEditorTool Tool { get; private set; } = SpriteEditorTool.Pencil;

    /// <summary>
    /// The transform slot's remembered variant (M9 stage 2.5 group slots). Clicking the slot
    /// applies this; the direct hotkeys F/V/R keep working AND move this highlight, because
    /// <see cref="FlipHorizontal"/> and friends set it themselves — one door, so the slot's
    /// icon can never show a variant the hotkeys just contradicted.
    /// </summary>
    public TransformVariant CurrentTransform { get; private set; }

    /// <summary>The shape slot's remembered variant: what <see cref="BeginShape"/> will draw.</summary>
    public ShapeVariant CurrentShape { get; private set; }

    /// <summary>The select slot's remembered variant: what the next fresh <see cref="BeginSelect"/> marks with.</summary>
    public SelectionVariant CurrentSelection { get; private set; }

    /// <summary>True when any pixels are selected — what Delete, Esc and the grab test consult.</summary>
    public bool HasSelection => _selectionCount > 0;

    /// <summary>True while any select-tool gesture is open (marking or moving) — the shell steers it with the drag clamp and commits it on release.</summary>
    public bool SelectionGestureActive => _selectGesture != SelectGesture.None;

    /// <summary>True while grabbed pixels are floating — the renderer draws the holes and the offset fragment from this.</summary>
    public bool MoveActive => _selectGesture == SelectGesture.Move;

    /// <summary>The float's clamped offset, region pixels. Zero outside a move.</summary>
    public int MoveOffsetX => _moveDx;

    /// <summary>The float's clamped offset, region pixels.</summary>
    public int MoveOffsetY => _moveDy;

    /// <summary>Whether a region-local pixel is in the mask (committed or still being marked). False out of range — hover asks freely.</summary>
    public bool IsSelected(int localX, int localY) =>
        localX >= 0 && localX < RegionPixels && localY >= 0 && localY < RegionPixels
        && _selection is bool[] mask && mask[localY * RegionPixels + localX];

    /// <summary>True once any selection has ever been committed — the stamp has ink. Until then the tooltip says SELECT FIRST.</summary>
    public bool HasStampSource => _stampSource is not null;

    /// <summary>Stamp source width in pixels — the captured selection's bounding box. Meaningless without <see cref="HasStampSource"/>.</summary>
    public int StampWidth => _stampWidth;

    /// <summary>Stamp source height in pixels.</summary>
    public int StampHeight => _stampHeight;

    /// <summary>One source pixel, 0 = transparent — what the ghost preview draws and <see cref="StampAt"/> prints. Callers gate on <see cref="HasStampSource"/>.</summary>
    public byte StampPixelAt(int x, int y) => _stampSource![y * _stampWidth + x];

    /// <summary>
    /// Top-left region-local corner of a stamp printed at a point — the one owner of the
    /// centering arithmetic, shared by <see cref="StampAt"/> and the renderer's ghost so the
    /// preview can never land somewhere the print will not.
    /// </summary>
    public (int X, int Y) StampOrigin(int localX, int localY) =>
        (localX - _stampWidth / 2, localY - _stampHeight / 2);

    /// <summary>Region anchor, in sheet cells 0-15.</summary>
    public int RegionCellX { get; private set; }

    /// <summary>Region anchor, in sheet cells 0-15.</summary>
    public int RegionCellY { get; private set; }

    /// <summary>
    /// Region side in sprite cells — 1, 2 or 4 (the niche's 8/16/32 px "zoom": a bigger slice
    /// of the sheet under the pencil, not a lens). Written only by <see cref="CycleRegionSize"/>,
    /// which re-clamps the anchor, so size and position can never disagree about staying inside
    /// the sheet.
    /// </summary>
    public int RegionCells { get; private set; } = 1;

    /// <summary>Region side in pixels — what canvas-local coordinates are validated against.</summary>
    public int RegionPixels => RegionCells * VirtualConsole.SpriteSize;

    /// <summary>Sprite number of the region's anchor cell — the "#NNN" the header shows, same numbering Spr(n) uses.</summary>
    public int SpriteIndex => RegionCellY * GridCells + RegionCellX;

    /// <summary>True while a pencil stroke is open (button held). The shell checks this before feeding drag positions.</summary>
    public bool StrokeActive => _strokeBackup is not null;

    /// <summary>True while a shape gesture is open (anchor placed, button held) — the preview is on screen.</summary>
    public bool ShapeActive => _shapeActive;

    /// <summary>
    /// The open shape's pixels, region-local — empty between gestures. This list is where the
    /// preview lives: the renderer paints it over the canvas in <see cref="CurrentColor"/>,
    /// and <see cref="CommitShape"/> plots exactly it, so what the author saw is what lands.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> ShapePreview => _shapePoints;

    /// <summary>True when the live sheet differs from what the disk holds — see the type comment for why this is a content compare.</summary>
    public bool IsDirty => !_sheet.AsSpan().SequenceEqual(_saved);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Bumped on every visible sheet change (plot, undo, redo) so the renderer re-uploads its
    /// texture only when the picture moved, not sixty times a second.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// True while the footer shows the unsaved-changes line (Z save and exit / X exit without
    /// saving / Esc stay). A footer line and not a modal window, per the work order — but it
    /// does own the keys while shown: the shell stops routing anything else to the editor.
    /// </summary>
    public bool ExitPromptShown { get; private set; }

    /// <summary>Why the last save failed, or null. Shown in the footer: a save the author believes happened but did not is data loss.</summary>
    public string? SaveError { get; private set; }

    /// <summary>Swatch click. Throws rather than masks: a value outside 0-15 here is a caller bug, and masking it would paint the wrong color silently.</summary>
    public void SelectColor(int color)
    {
        if (color is < 0 or >= Palette.VisibleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color), color, "the pencil takes visible palette indices 0-15 only (SPEC-8 §6).");
        }
        CurrentColor = color;
    }

    /// <summary>
    /// Sheet-grid click: moves the region anchor. Clamped, not thrown — a click near the grid's
    /// edge should select the nearest legal anchor, and once RegionCells grows past 1 the legal
    /// anchors stop at <c>GridCells - RegionCells</c> so the region always lies inside the sheet.
    /// An anchor that actually moves drops the selection: the mask is region-local, and keeping
    /// it would silently re-aim it at foreign pixels (the stamp source survives — see
    /// <see cref="ClearSelection"/>).
    /// </summary>
    public void SelectRegionCell(int cellX, int cellY)
    {
        int nextX = Math.Clamp(cellX, 0, GridCells - RegionCells);
        int nextY = Math.Clamp(cellY, 0, GridCells - RegionCells);
        if (nextX != RegionCellX || nextY != RegionCellY)
        {
            ClearSelection();
        }
        RegionCellX = nextX;
        RegionCellY = nextY;
    }

    /// <summary>Canvas cursor, region-local — see <see cref="SetCursor"/> for why it lives here.</summary>
    public int CursorX { get; private set; }

    /// <summary>Canvas cursor, region-local.</summary>
    public int CursorY { get; private set; }

    /// <summary>
    /// Moves the canvas cursor — the position the keyboard pencil paints at, the eyedropper's
    /// X samples and the status bar reads out (M9 stage 2.5 input parity). It lives in the
    /// session rather than the window because its one invariant — never outside the region,
    /// whatever moved (arrows, mouse hover, a region shrink) — is exactly what lets the shell
    /// call <see cref="Paint"/> at the cursor without a second clamp, and that must be provable
    /// headless. Clamped, not thrown, like the region anchor: an arrow held at the edge should
    /// park the cursor there, not crash. It writes no pixels itself, so the 0-15/undo/dirty
    /// contracts gain no new door.
    /// </summary>
    public void SetCursor(int localX, int localY)
    {
        CursorX = Math.Clamp(localX, 0, RegionPixels - 1);
        CursorY = Math.Clamp(localY, 0, RegionPixels - 1);
    }

    /// <summary>One arrow press: the cursor steps by a pixel, stopping at the region border.</summary>
    public void MoveCursor(int dx, int dy) => SetCursor(CursorX + dx, CursorY + dy);

    /// <summary>
    /// Left button pressed on the canvas. The pre-stroke sheet is snapshotted here and becomes
    /// the undo entry when the stroke ends — which is the whole "one stroke = one undo step"
    /// mechanism: nothing inside the stroke ever touches the undo stack.
    /// </summary>
    public void BeginStroke()
    {
        if (StrokeActive)
        {
            return;     // A second press without a release (focus loss glitches) folds into the open stroke.
        }
        _strokeBackup = (byte[])_sheet.Clone();
        _strokeChanged = false;
        _lastPaintX = -1;
    }

    /// <summary>
    /// One pencil sample at a region-local pixel. Consecutive samples within a stroke are
    /// joined with a Bresenham line, because the mouse reports positions per frame, not per
    /// pixel — a fast drag would otherwise dot the canvas instead of drawing through it
    /// (the "тянущийся мазок" the order demands).
    /// </summary>
    public void Paint(int localX, int localY)
    {
        if (!StrokeActive)
        {
            throw new InvalidOperationException("Paint outside a stroke — the shell must call BeginStroke on the press.");
        }
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        if (_lastPaintX < 0)
        {
            Plot(localX, localY);
        }
        else
        {
            TraceLine(_lastPaintX, _lastPaintY, localX, localY, Plot);
        }
        _lastPaintX = localX;
        _lastPaintY = localY;
    }

    /// <summary>
    /// Button released: the stroke commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z appear dead). Safe to call without an open stroke: releases arrive when the
    /// press landed outside the canvas.
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
        // The redone future described a sheet that no longer exists once a new stroke lands.
        _redo.Clear();
    }

    /// <summary>Right button on the canvas: the pencil takes the color under the cursor (PICO-8's pattern, per the niche survey).</summary>
    public void PickColor(int localX, int localY)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        CurrentColor = _sheet[SheetOffset(localX, localY)];
    }

    /// <summary>
    /// B: pencil ↔ bucket, its wave-2c contract untouched by the later tools — from any of
    /// them it lands on the pencil, the opening tool. Routed through <see cref="SelectTool"/>
    /// so the whole switch discipline (commit the open gesture, park a floating move, drop the
    /// mask) has exactly one owner and B can never mean less than a toolbar click does.
    /// </summary>
    public void ToggleTool() =>
        SelectTool(Tool == SpriteEditorTool.Pencil ? SpriteEditorTool.Fill : SpriteEditorTool.Pencil);

    /// <summary>
    /// Direct tool selection — every path to a tool (toolbar click, digit, B) funnels here.
    /// The gesture discipline of the earlier waves stands, plus the owner's third-review law:
    /// <b>the selection lives only under the select tool</b>. Leaving it parks an open float
    /// as its own committed drop first (a tool switch is a change of subject, not an Esc —
    /// silently snapping the fragment home would throw away an arrangement the author can
    /// see), and then the mask dies with the switch — which is what returns Delete and every
    /// region edit to their whole-region meaning (the review's "очистка не работает" was a
    /// mask surviving invisibly under the pencil and quietly narrowing Clear). The stamp
    /// source stands untouched: it is the memory taken at the selection's commit, not the
    /// selection itself. Selecting the tool already active is a visible no-op on purpose, so
    /// a toolbar click cannot eat an open gesture — or a live selection — for nothing.
    /// </summary>
    public void SelectTool(SpriteEditorTool tool)
    {
        if (Tool == tool)
        {
            return;
        }
        if (MoveActive)
        {
            CommitSelect();     // the float parks where the author left it — one undo step, like any drop
        }
        InterruptGesture();
        ClearSelection();
        Tool = tool;
    }

    /// <summary>Flyout pick (or a direct hotkey's side effect) — remembers which transform the slot applies.</summary>
    public void SelectTransform(TransformVariant variant) => CurrentTransform = variant;

    /// <summary>The digit's repeat-press: highlight walks flip H → flip V → rotate → flip H. Applies nothing.</summary>
    public void CycleTransform() =>
        CurrentTransform = (TransformVariant)(((int)CurrentTransform + 1) % 3);

    /// <summary>
    /// Flyout pick for the shape slot. Redraws an open preview in the new variant rather than
    /// cancelling it — the anchor and drag are the author's work, the variant is just its look.
    /// </summary>
    public void SelectShape(ShapeVariant variant)
    {
        CurrentShape = variant;
        if (_shapeActive)
        {
            RebuildShapePreview();
        }
    }

    /// <summary>The digit's repeat-press on the shape slot: oval ↔ rectangle.</summary>
    public void CycleShape() =>
        SelectShape(CurrentShape == ShapeVariant.Oval ? ShapeVariant.Rectangle : ShapeVariant.Oval);

    /// <summary>
    /// The transform slot's click: applies <see cref="CurrentTransform"/> to the region —
    /// the mouse's half of what F/V/R do directly from the keyboard.
    /// </summary>
    public void ApplyTransform()
    {
        switch (CurrentTransform)
        {
            case TransformVariant.FlipH:
                FlipHorizontal();
                break;
            case TransformVariant.FlipV:
                FlipVertical();
                break;
            default:
                RotateClockwise();
                break;
        }
    }

    /// <summary>
    /// Shape button pressed on the canvas: the anchor lands and the preview starts as a single
    /// point. Throws outside the region like <see cref="Paint"/> — the shell's clamp is the
    /// contract, and since both corners can only ever be in-range, a committed shape cannot
    /// reach a neighbouring sprite by construction (the "фигуры клампятся регионом" law).
    /// </summary>
    public void BeginShape(int localX, int localY)
    {
        if (_shapeActive)
        {
            return;     // A second press without a release folds into the open gesture, like BeginStroke.
        }
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        _shapeActive = true;
        _shapeAnchorX = localX;
        _shapeAnchorY = localY;
        _shapeCornerX = localX;
        _shapeCornerY = localY;
        _shapeFilled = false;
        RebuildShapePreview();
    }

    /// <summary>
    /// One frame of an open shape gesture: the dragged corner and the Ctrl-held filled flag
    /// (PICO-8's pattern: plain drag = outline, Ctrl = filled). Recomputes the preview —
    /// nothing here touches the sheet, so <see cref="Version"/>, undo and dirt all stand still
    /// until <see cref="CommitShape"/>.
    /// </summary>
    public void UpdateShape(int localX, int localY, bool filled)
    {
        if (!_shapeActive)
        {
            throw new InvalidOperationException("UpdateShape outside a gesture — the shell must call BeginShape on the press.");
        }
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        if (localX == _shapeCornerX && localY == _shapeCornerY && filled == _shapeFilled)
        {
            return;     // The per-frame refresh is free when nothing moved.
        }
        _shapeCornerX = localX;
        _shapeCornerY = localY;
        _shapeFilled = filled;
        RebuildShapePreview();
    }

    /// <summary>
    /// Shape button released: the preview's points become sheet pixels in
    /// <see cref="CurrentColor"/> as ONE undo step — by riding the stroke mechanism
    /// (<see cref="BeginStroke"/> / <see cref="Plot"/> / <see cref="EndStroke"/>), which
    /// already owns "one gesture = one step" and "a no-op never happened". A 1x1 gesture is
    /// an honest single point, both variants. Safe without an open gesture, like EndStroke:
    /// releases arrive when the press landed outside the canvas.
    /// </summary>
    public void CommitShape()
    {
        if (!_shapeActive)
        {
            return;
        }
        _shapeActive = false;
        BeginStroke();
        foreach ((int x, int y) in _shapePoints)
        {
            Plot(x, y);
        }
        EndStroke();
        _shapePoints.Clear();
    }

    /// <summary>
    /// Recomputes <see cref="ShapePreview"/> from the gesture's corners, variant and filled
    /// flag. Row-major scan over the inclusive bounding box; the oval is the integer
    /// inclusion test (2x-sx)²b² + (2y-sy)²a² ≤ a²b² (the box's inscribed ellipse, exact in
    /// integers — no floats, no rounding drift between preview and commit), its outline the
    /// inside pixels with at least one 4-neighbour outside. Degenerate boxes fall out for
    /// free: a 1-wide box passes the test on its whole column, a 1x1 box is a point.
    /// </summary>
    private void RebuildShapePreview()
    {
        _shapePoints.Clear();
        int x0 = Math.Min(_shapeAnchorX, _shapeCornerX);
        int x1 = Math.Max(_shapeAnchorX, _shapeCornerX);
        int y0 = Math.Min(_shapeAnchorY, _shapeCornerY);
        int y1 = Math.Max(_shapeAnchorY, _shapeCornerY);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                bool inside = CurrentShape == ShapeVariant.Rectangle || InsideOval(x, y, x0, y0, x1, y1);
                if (!inside)
                {
                    continue;
                }
                bool onContour = CurrentShape == ShapeVariant.Rectangle
                    ? x == x0 || x == x1 || y == y0 || y == y1
                    : !InsideOval(x - 1, y, x0, y0, x1, y1) || !InsideOval(x + 1, y, x0, y0, x1, y1)
                        || !InsideOval(x, y - 1, x0, y0, x1, y1) || !InsideOval(x, y + 1, x0, y0, x1, y1);
                if (_shapeFilled || onContour)
                {
                    _shapePoints.Add((x, y));
                }
            }
        }
    }

    /// <summary>Doubled-coordinate ellipse test — anything outside the inclusive box fails it, so no separate bounds check exists to drift.</summary>
    private static bool InsideOval(int x, int y, int x0, int y0, int x1, int y1)
    {
        long a = x1 - x0 + 1;
        long b = y1 - y0 + 1;
        long dx = 2L * x - (x0 + x1);
        long dy = 2L * y - (y0 + y1);
        return dx * dx * b * b + dy * dy * a * a <= a * a * b * b;
    }

    /// <summary>Flyout pick for the select slot — remembered for the next press. An open gesture keeps the kind it was pressed with.</summary>
    public void SelectSelectionVariant(SelectionVariant variant) => CurrentSelection = variant;

    /// <summary>The digit's repeat-press on the select slot: rectangle → brush → wand → rectangle.</summary>
    public void CycleSelectionVariant() =>
        CurrentSelection = (SelectionVariant)(((int)CurrentSelection + 1) % 3);

    /// <summary>
    /// Select button pressed on the canvas (mouse press or Z/Space — one dispatch in the
    /// shell). Over the selection it grabs: the pixels float and the drag carries them (the
    /// order's "повторное Z над выделением берёт и двигает"). Anywhere else it starts marking
    /// a NEW mask — which is how "клик новым выделением снимает старое" happens: the old mask
    /// dies at the press, not at the release. The rectangle and the brush mark a point and
    /// grow with the drag; the wand marks its whole area right here, through the very flood
    /// the bucket repaints with (<see cref="VisitConnectedColor"/> — one owner of "same color,
    /// 4-connected, walled by the region"). Throws outside the region like <see cref="Paint"/> —
    /// the shell's clamp is the contract.
    /// </summary>
    public void BeginSelect(int localX, int localY)
    {
        if (_selectGesture != SelectGesture.None)
        {
            return;     // A second press without a release folds into the open gesture, like BeginStroke.
        }
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        _selectAnchorX = localX;
        _selectAnchorY = localY;
        if (IsSelected(localX, localY))
        {
            BeginMove();
            return;
        }
        _selection = new bool[RegionPixels * RegionPixels];
        _selectionCount = 0;
        switch (CurrentSelection)
        {
            case SelectionVariant.Rectangle:
                _selectGesture = SelectGesture.Rectangle;
                MarkSelected(localX, localY);
                break;
            case SelectionVariant.Brush:
                _selectGesture = SelectGesture.Brush;
                MarkSelected(localX, localY);
                break;
            default:
                _selectGesture = SelectGesture.Wand;
                VisitConnectedColor(localX, localY, MarkSelected);
                break;
        }
    }

    /// <summary>
    /// The grab: nothing is lifted out of the sheet — the "float" is just the offset the drag
    /// steers, drawn by the renderer and made real only by the drop. The offset bounds pin the
    /// mask's bounding box inside the region, which is the move's clamp law: dragging past the
    /// border parks the fragment against it, so no pixel can be pushed off the region and lost.
    /// </summary>
    private void BeginMove()
    {
        TrySelectionBounds(out int minX, out int minY, out int maxX, out int maxY);
        int n = RegionPixels;
        _moveMinDx = -minX;
        _moveMaxDx = n - 1 - maxX;
        _moveMinDy = -minY;
        _moveMaxDy = n - 1 - maxY;
        _moveDx = 0;
        _moveDy = 0;
        _selectGesture = SelectGesture.Move;
    }

    /// <summary>
    /// One frame of an open select gesture, fed the canvas cursor — both input worlds steer
    /// the cursor, so this is their meeting point, like <see cref="UpdateShape"/>. The
    /// rectangle re-marks its box, the brush strokes the mask through the pencil's own line
    /// tracer, the wand re-picks at the new point (sliding onto another color shows that
    /// color's area live, so the release commits what is on screen, never a stale press), the
    /// move re-clamps its offset. Nothing here touches the sheet.
    /// </summary>
    public void UpdateSelect(int localX, int localY)
    {
        if (_selectGesture == SelectGesture.None)
        {
            throw new InvalidOperationException("UpdateSelect outside a gesture — the shell must call BeginSelect on the press.");
        }
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        switch (_selectGesture)
        {
            case SelectGesture.Rectangle:
                RebuildBoxSelection(localX, localY);
                break;
            case SelectGesture.Brush:
                TraceLine(_selectAnchorX, _selectAnchorY, localX, localY, MarkSelected);
                _selectAnchorX = localX;
                _selectAnchorY = localY;
                break;
            case SelectGesture.Wand:
                if (localX != _selectAnchorX || localY != _selectAnchorY)
                {
                    _selectAnchorX = localX;
                    _selectAnchorY = localY;
                    Array.Clear(_selection!);
                    _selectionCount = 0;
                    VisitConnectedColor(localX, localY, MarkSelected);
                }
                break;
            default:
                _moveDx = Math.Clamp(localX - _selectAnchorX, _moveMinDx, _moveMaxDx);
                _moveDy = Math.Clamp(localY - _selectAnchorY, _moveMinDy, _moveMaxDy);
                break;
        }
    }

    /// <summary>
    /// Select button released. A marking gesture's mask becomes THE selection and its pixels
    /// are copied off as the stamp's source (the order: remembered automatically at creation).
    /// A move parks the fragment: one <see cref="ApplyRegionEdit"/> writes the lifted pixels
    /// at their new home, the literal 0 into the holes they left, and everything else stands —
    /// which makes the whole grab-drag-drop exactly ONE undo step by the same mechanism every
    /// region edit uses, and a zero-distance drop invisible for free. The mask then follows
    /// its pixels. Safe without an open gesture, like <see cref="EndStroke"/>.
    /// </summary>
    public void CommitSelect()
    {
        SelectGesture gesture = _selectGesture;
        // Cleared before the work: ApplyRegionEdit interrupts open gestures, and this one is
        // completing, not dying — the interrupt must not eat the mask it is committing.
        _selectGesture = SelectGesture.None;
        switch (gesture)
        {
            case SelectGesture.Rectangle:
            case SelectGesture.Brush:
            case SelectGesture.Wand:
                CaptureStampSource();
                break;
            case SelectGesture.Move:
                CommitMove();
                break;
        }
    }

    /// <summary>
    /// Esc's verb while a selection exists (and the region-change cleanup): any open select
    /// gesture dies and the mask drops. The sheet is untouched — a cancelled move never wrote
    /// anything to cancel. The stamp source deliberately survives: it is the memory of the
    /// LAST selection, and dropping the marching ants is not forgetting what they held.
    /// </summary>
    public void ClearSelection()
    {
        _selectGesture = SelectGesture.None;
        _selection = null;
        _selectionCount = 0;
    }

    /// <summary>
    /// The mask's inclusive bounding box: the frame the renderer draws, the base of the move
    /// clamp and of the stamp capture — one owner for all three. False when nothing is selected.
    /// </summary>
    public bool TrySelectionBounds(out int minX, out int minY, out int maxX, out int maxY)
    {
        int n = RegionPixels;
        minX = n;
        minY = n;
        maxX = -1;
        maxY = -1;
        if (_selection is not bool[] mask)
        {
            return false;
        }
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (!mask[y * n + x])
                {
                    continue;
                }
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX >= 0;
    }

    /// <summary>The rectangle variant's live mask: the inclusive anchor-to-corner box, remade each drag frame like the shape preview.</summary>
    private void RebuildBoxSelection(int cornerX, int cornerY)
    {
        Array.Clear(_selection!);
        _selectionCount = 0;
        int x0 = Math.Min(_selectAnchorX, cornerX);
        int x1 = Math.Max(_selectAnchorX, cornerX);
        int y0 = Math.Min(_selectAnchorY, cornerY);
        int y1 = Math.Max(_selectAnchorY, cornerY);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                MarkSelected(x, y);
            }
        }
    }

    /// <summary>The mask's one writer — dedups so the brush can re-cross its own track without inflating the count.</summary>
    private void MarkSelected(int x, int y)
    {
        int index = y * RegionPixels + x;
        if (!_selection![index])
        {
            _selection[index] = true;
            _selectionCount++;
        }
    }

    /// <summary>The drop — see <see cref="CommitSelect"/>. Runs with the gesture already closed.</summary>
    private void CommitMove()
    {
        if (_moveDx == 0 && _moveDy == 0)
        {
            return;     // A grab that went nowhere never happened — no step, no dirt.
        }
        bool[] mask = _selection!;
        int dx = _moveDx;
        int dy = _moveDy;
        ApplyRegionEdit((src, n, x, y) =>
        {
            int fromX = x - dx;
            int fromY = y - dy;
            if (fromX >= 0 && fromX < n && fromY >= 0 && fromY < n && mask[fromY * n + fromX])
            {
                return src[fromY * n + fromX];  // a lifted pixel lands here — landings beat holes
            }
            return mask[y * n + x] ? (byte)0 : src[y * n + x];
        });
        // The selection follows its pixels; the shifted indices are in range by the grab's clamp.
        int side = RegionPixels;
        var moved = new bool[mask.Length];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                if (mask[y * side + x])
                {
                    moved[(y + dy) * side + (x + dx)] = true;
                }
            }
        }
        _selection = moved;     // a pure shift — the count is unchanged
    }

    /// <summary>
    /// Copies the just-committed selection's pixels into the stamp source — values read
    /// straight out of the sheet, so they are 0-15 by the sheet's own invariant. Box cells
    /// outside the mask stay 0, the same "prints nothing" a masked 0 pixel has.
    /// </summary>
    private void CaptureStampSource()
    {
        TrySelectionBounds(out int minX, out int minY, out int maxX, out int maxY);
        bool[] mask = _selection!;
        int n = RegionPixels;
        _stampWidth = maxX - minX + 1;
        _stampHeight = maxY - minY + 1;
        _stampSource = new byte[_stampWidth * _stampHeight];
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (mask[y * n + x])
                {
                    _stampSource[(y - minY) * _stampWidth + (x - minX)] = _sheet[SheetOffset(x, y)];
                }
            }
        }
    }

    /// <summary>
    /// The stamp tool's click (mouse or Z — the same dispatch as every canvas press): prints
    /// the source centered at the point, source color 0 transparent, pixels past the region
    /// border clipped away — "с центром у курсора" and "кламп регионом" can only both hold by
    /// clipping, since clamping the position would drag the center off the cursor at the edge.
    /// One <see cref="ApplyRegionEdit"/> = one undo step; with no source ever captured it
    /// honestly does nothing (the tooltip explains SELECT FIRST), and a print that changes
    /// nothing is invisible like every no-op edit.
    /// </summary>
    public void StampAt(int localX, int localY)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        if (_stampSource is not byte[] source)
        {
            return;
        }
        int width = _stampWidth;
        int height = _stampHeight;
        (int destX, int destY) = StampOrigin(localX, localY);
        ApplyRegionEdit((src, n, x, y) =>
        {
            int fromX = x - destX;
            int fromY = y - destY;
            if (fromX >= 0 && fromX < width && fromY >= 0 && fromY < height
                && source[fromY * width + fromX] != 0)
            {
                return source[fromY * width + fromX];
            }
            return src[y * n + x];
        });
    }

    /// <summary>
    /// What every operation that cuts across an open gesture calls: an open <b>stroke</b>
    /// commits (its pixels are already real), an open <b>shape preview</b> is discarded (its
    /// pixels never were — cancelling is the only reading of "в лист не пишется" that survives
    /// an interruption), an open <b>marking gesture</b> discards its half-made mask (it never
    /// was the selection) and an open <b>move</b> just stops floating — nothing was written,
    /// and the committed mask survives at its source. One helper instead of a call chain at
    /// every site, so a future interrupter cannot remember the stroke and forget a preview.
    /// </summary>
    private void InterruptGesture()
    {
        _shapeActive = false;
        _shapePoints.Clear();
        if (_selectGesture is SelectGesture.Rectangle or SelectGesture.Brush or SelectGesture.Wand)
        {
            _selection = null;
            _selectionCount = 0;
        }
        _selectGesture = SelectGesture.None;
        EndStroke();
    }

    /// <summary>
    /// Tab: region side 1 → 2 → 4 → 1 cells. Re-clamps the anchor through
    /// <see cref="SelectRegionCell"/> — growing at the sheet's edge pulls the region back
    /// inside rather than letting it clip, which is the invariant every transform relies on.
    /// Ends an open stroke first: the stroke's last-point memory is in old region coordinates,
    /// and joining a line across a size change could leave the shrunk region's bounds.
    /// </summary>
    public void CycleRegionSize()
    {
        InterruptGesture();
        ClearSelection();       // the mask is sized to the region — a resize would misindex it
        RegionCells = RegionCells switch { 1 => 2, 2 => 4, _ => 1 };
        SelectRegionCell(RegionCellX, RegionCellY);
        // A shrink can strand the cursor outside the new region (31,31 in an 8-px region);
        // re-clamping here is what keeps "paint at the cursor" throw-free by construction.
        SetCursor(CursorX, CursorY);
    }

    /// <summary>
    /// The bucket: repaints the 4-connected area of one color around a region-local pixel with
    /// <see cref="CurrentColor"/>, walls at the region's border (work order: the region bounds
    /// the fill) — the walk itself lives in <see cref="VisitConnectedColor"/>, shared with the
    /// wand. One undo step, like a stroke — and filling a color with itself changes nothing,
    /// so it never happened as far as undo and dirt are concerned.
    /// </summary>
    public void Fill(int localX, int localY)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        InterruptGesture();     // A stray open gesture commits as its own step before the fill becomes one.
        byte target = _sheet[SheetOffset(localX, localY)];
        if (target == CurrentColor)
        {
            return;
        }
        // target != CurrentColor guarantees at least the seed pixel changes, so the undo
        // snapshot is taken unconditionally — there is no "nothing changed" path from here.
        _undo.Add((byte[])_sheet.Clone());
        _redo.Clear();
        VisitConnectedColor(localX, localY, (x, y) => _sheet[SheetOffset(x, y)] = (byte)CurrentColor);
        Version++;
    }

    /// <summary>
    /// The 4-connected same-color walk the bucket repaints with and the wand marks with — one
    /// owner of the flood (playbook: a second copy of "same color, 4-connected, walled by the
    /// region" would let the two tools drift apart about what "this area" means). An explicit
    /// stack instead of recursion: a 32x32 single-color region is a 1024-deep recursion worst
    /// case. The visited set makes the walk independent of what <paramref name="visit"/> does
    /// to the sheet: the bucket repaints mid-walk, the wand writes nothing at all, and both
    /// cover the same pixels because membership is decided by the color as it was read before
    /// any visit could change it. Coordinates are validated by the callers, like every canvas
    /// verb.
    /// </summary>
    private void VisitConnectedColor(int localX, int localY, Action<int, int> visit)
    {
        byte target = _sheet[SheetOffset(localX, localY)];
        int n = RegionPixels;
        var seen = new bool[n * n];
        var pending = new Stack<(int X, int Y)>();
        pending.Push((localX, localY));
        while (pending.Count > 0)
        {
            (int x, int y) = pending.Pop();
            if (x < 0 || x >= n || y < 0 || y >= n || seen[y * n + x])
            {
                continue;   // the region border is the wall — for the fill and the wand alike
            }
            if (_sheet[SheetOffset(x, y)] != target)
            {
                continue;
            }
            seen[y * n + x] = true;
            visit(x, y);
            pending.Push((x + 1, y));
            pending.Push((x - 1, y));
            pending.Push((x, y + 1));
            pending.Push((x, y - 1));
        }
    }

    /// <summary>
    /// F: mirror the region left↔right. One undo step; a symmetric region is a no-op and stays
    /// invisible. Also moves the transform slot's highlight (M9 stage 2.5: the direct hotkeys
    /// keep working and light up their variant) — setting it here rather than in the key
    /// routing means no caller can apply a flip while the slot claims otherwise.
    /// </summary>
    public void FlipHorizontal()
    {
        CurrentTransform = TransformVariant.FlipH;
        ApplyRegionEdit(static (src, n, x, y) => src[y * n + (n - 1 - x)]);
    }

    /// <summary>V: mirror the region top↔bottom.</summary>
    public void FlipVertical()
    {
        CurrentTransform = TransformVariant.FlipV;
        ApplyRegionEdit(static (src, n, x, y) => src[(n - 1 - y) * n + x]);
    }

    /// <summary>
    /// R: rotate the region 90° clockwise — the top row becomes the right column. Always legal
    /// without remapping cells because the region is square by construction (the whole reason
    /// the work order pins it square).
    /// </summary>
    public void RotateClockwise()
    {
        CurrentTransform = TransformVariant.Rotate;
        ApplyRegionEdit(static (src, n, x, y) => src[(n - 1 - x) * n + y]);
    }

    /// <summary>
    /// Delete: to color 0 — the sheet's "nothing", same as the eraser writes. With a selection
    /// only the selected pixels die (wave 2f) and the region around them stands; without one,
    /// the whole region, as always. Both are one region edit — one undo step. The gesture
    /// interrupt runs first so a half-marked mask from an open press can never decide what dies.
    /// </summary>
    public void ClearRegion()
    {
        InterruptGesture();
        if (_selection is bool[] mask)
        {
            ApplyRegionEdit((src, n, x, y) => mask[y * n + x] ? (byte)0 : src[y * n + x]);
        }
        else
        {
            ApplyRegionEdit(static (_, _, _, _) => 0);
        }
    }

    /// <summary>
    /// The one mechanism under all four region edits: read the region out, build its
    /// replacement (<paramref name="source"/> answers "what goes at dest (x, y)" from the old
    /// region, side n), and commit only if the result differs — so an edit that changes
    /// nothing is invisible to undo and dirt, exactly like an idle stroke. Reads and writes go
    /// through <see cref="SheetOffset"/> on region-local coordinates, so pixels outside the
    /// region are unreachable by construction — the transforms cannot touch neighbouring
    /// sprites no matter where the region sits, because the anchor clamp keeps the whole
    /// square inside the sheet.
    /// </summary>
    private void ApplyRegionEdit(Func<byte[], int, int, int, byte> source)
    {
        InterruptGesture();     // A transform mid-drag commits the gesture first — two clean undo steps, no braid.
        int n = RegionPixels;
        var before = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                before[y * n + x] = _sheet[SheetOffset(x, y)];
            }
        }
        var after = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                after[y * n + x] = source(before, n, x, y);
            }
        }
        if (after.AsSpan().SequenceEqual(before))
        {
            return;
        }
        _undo.Add((byte[])_sheet.Clone());
        _redo.Clear();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                _sheet[SheetOffset(x, y)] = after[y * n + x];
            }
        }
        Version++;
    }

    /// <summary>
    /// Ctrl+Z. Ends an open stroke first (committing it), so an undo mid-drag rolls back a
    /// whole gesture instead of tearing one in half. Whole-array swaps, no copying: the arrays
    /// already exist and nothing else holds them.
    /// </summary>
    public void Undo()
    {
        InterruptGesture();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(_sheet);
        _sheet = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Version++;
    }

    /// <summary>Ctrl+Y — the exact mirror of <see cref="Undo"/>.</summary>
    public void Redo()
    {
        InterruptGesture();
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(_sheet);
        _sheet = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Version++;
    }

    /// <summary>
    /// Ctrl+S, and the Z of the exit prompt. The clean guard is the save contract's heart:
    /// a session whose sheet equals the disk writes <b>nothing</b> — open-and-close leaves the
    /// file untouched and, for a cart that never had a gfx.png, uncreated; a repeated Ctrl+S
    /// is a no-op. Failures land in <see cref="SaveError"/> instead of throwing, because a
    /// full disk must leave the author their picture and a message, not a dead window.
    /// </summary>
    /// <returns>True when the disk now matches the sheet (including "already did"), false when the write failed.</returns>
    public bool Save()
    {
        InterruptGesture();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        try
        {
            File.WriteAllBytes(
                _gfxPath, PngEncoder.EncodeFromPaletteIndices(_sheet, CartData.GfxWidth, CartData.GfxHeight));
            _saved = (byte[])_sheet.Clone();
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Esc. Clean session: yes, close — and since nothing was written, the disk is byte-for-byte
    /// what it was. Dirty session: first press raises the footer prompt, a second press lowers
    /// it (Esc means "stay", per the order) — closing then goes through the prompt's Z or X,
    /// which the mode machine drives.
    /// </summary>
    /// <returns>True when the editor may close now.</returns>
    public bool RequestClose()
    {
        InterruptGesture();     // Esc mid-drag: the gesture commits, then the prompt judges the session as it stands.
        if (ExitPromptShown)
        {
            ExitPromptShown = false;
            return false;
        }
        if (IsDirty)
        {
            ExitPromptShown = true;
            return false;
        }
        return true;
    }

    private void ValidateLocal(int value, string name)
    {
        if (value < 0 || value >= RegionPixels)
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"region-local pixel coordinates are 0-{RegionPixels - 1}; the layout clamps drags before calling in.");
        }
    }

    private int SheetOffset(int localX, int localY)
    {
        int size = VirtualConsole.SpriteSize;
        return (RegionCellY * size + localY) * CartData.GfxWidth + RegionCellX * size + localX;
    }

    /// <summary>
    /// The single write into the live sheet. <see cref="CurrentColor"/> is 0-15 by the
    /// invariant above, so the cast cannot truncate; writing the value already there is
    /// skipped so that a stroke which changes nothing stays invisible to undo and dirt.
    /// </summary>
    private void Plot(int localX, int localY)
    {
        int offset = SheetOffset(localX, localY);
        if (_sheet[offset] == CurrentColor)
        {
            return;
        }
        _sheet[offset] = (byte)CurrentColor;
        _strokeChanged = true;
        Version++;
    }

    /// <summary>
    /// Bresenham over region-local pixels — at most 32 steps, exact on diagonals. The one
    /// owner of the line formula: the pencil plots along it and the brush select marks along
    /// it, so the two strokes can never disagree about what "through these points" means.
    /// </summary>
    private static void TraceLine(int x0, int y0, int x1, int y1, Action<int, int> visit)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            visit(x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }
}
