using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Which mouse tool the canvas click means. The eraser is the pencil with color 0 and the
/// eyedropper is the right button in every tool, so these are the only genuine canvas modes.
/// Shape joined in wave 2e (M9 stage 2.5, the owner's verdict); the oval/rectangle choice is
/// not a tool but a <see cref="ShapeVariant"/> — the photoshop-style group slot's memory.
/// The state lives in the session, not the window, so "what is active" is provable headless.
/// </summary>
public enum SpriteEditorTool
{
    Pencil,
    Fill,
    Shape,
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
/// edits — flips and the rotation only permute values already in the sheet, and the clear
/// writes the literal 0. There is no fifth setter, so the byte casts in the plot and fill
/// routines can never truncate. <see cref="PngEncoder"/> re-checks on save as the owner of
/// its own input contract; that check is unreachable from here by construction.</para>
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
    /// </summary>
    public void SelectRegionCell(int cellX, int cellY)
    {
        RegionCellX = Math.Clamp(cellX, 0, GridCells - RegionCells);
        RegionCellY = Math.Clamp(cellY, 0, GridCells - RegionCells);
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
            PlotLine(_lastPaintX, _lastPaintY, localX, localY);
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
    /// B: pencil ↔ bucket, its wave-2c contract untouched by the third tool — from Shape it
    /// lands on the pencil, the opening tool. Interrupts an open gesture first: a stroke that
    /// straddles a tool switch would be two tools in one undo step, and a shape preview
    /// belongs to the tool that opened it.
    /// </summary>
    public void ToggleTool()
    {
        InterruptGesture();
        Tool = Tool == SpriteEditorTool.Pencil ? SpriteEditorTool.Fill : SpriteEditorTool.Pencil;
    }

    /// <summary>
    /// Direct tool selection — the real select the wave-2b comment in <c>QuarpGame.SetTool</c>
    /// promised once a third tool existed. Same gesture discipline as <see cref="ToggleTool"/>;
    /// selecting the tool already active is a visible no-op on purpose, so a toolbar click
    /// cannot eat an open gesture for nothing.
    /// </summary>
    public void SelectTool(SpriteEditorTool tool)
    {
        if (Tool == tool)
        {
            return;
        }
        InterruptGesture();
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

    /// <summary>
    /// What every operation that cuts across an open gesture calls: an open <b>stroke</b>
    /// commits (its pixels are already real), an open <b>shape preview</b> is discarded (its
    /// pixels never were — cancelling is the only reading of "в лист не пишется" that survives
    /// an interruption). One helper instead of two calls at eight sites, so a future
    /// interrupter cannot remember the stroke and forget the preview.
    /// </summary>
    private void InterruptGesture()
    {
        _shapeActive = false;
        _shapePoints.Clear();
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
        RegionCells = RegionCells switch { 1 => 2, 2 => 4, _ => 1 };
        SelectRegionCell(RegionCellX, RegionCellY);
        // A shrink can strand the cursor outside the new region (31,31 in an 8-px region);
        // re-clamping here is what keeps "paint at the cursor" throw-free by construction.
        SetCursor(CursorX, CursorY);
    }

    /// <summary>
    /// The bucket: repaints the 4-connected area of one color around a region-local pixel with
    /// <see cref="CurrentColor"/>, walls at the region's border (work order: the region bounds
    /// the fill). One undo step, like a stroke — and filling a color with itself changes
    /// nothing, so it never happened as far as undo and dirt are concerned.
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
        int n = RegionPixels;
        // An explicit stack instead of recursion: a 32x32 single-color region is a 1024-deep
        // recursion worst case, and the repaint itself marks pixels visited (they stop
        // matching target), so no separate visited set is needed.
        var pending = new Stack<(int X, int Y)>();
        pending.Push((localX, localY));
        while (pending.Count > 0)
        {
            (int x, int y) = pending.Pop();
            if (x < 0 || x >= n || y < 0 || y >= n)
            {
                continue;   // the region border is the fill's wall
            }
            int offset = SheetOffset(x, y);
            if (_sheet[offset] != target)
            {
                continue;
            }
            _sheet[offset] = (byte)CurrentColor;
            pending.Push((x + 1, y));
            pending.Push((x - 1, y));
            pending.Push((x, y + 1));
            pending.Push((x, y - 1));
        }
        Version++;
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

    /// <summary>Delete: the region to color 0 — the sheet's "nothing", same as the eraser writes.</summary>
    public void ClearRegion() => ApplyRegionEdit(static (_, _, _, _) => 0);

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

    /// <summary>Bresenham over region-local pixels — at most 32 steps, exact on diagonals.</summary>
    private void PlotLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Plot(x0, y0);
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
