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
/// Which of the two inks a canvas verb lays. Every reference console keeps two colours in
/// hand and puts them on the two mouse buttons — TIC-80's <c>color</c>/<c>color2</c>
/// (<c>src/studio/editors/sprite.c</c>, <c>processDrawCanvasMouse</c>), LIKO-12's
/// <c>colsL</c>/<c>colsR</c> (<c>src/OS/DiskOS/Editors/sprite.lua</c>: <c>if isKDown("lshift")
/// or isMDown(2) then b = 2 end</c>), PICO-8's "RMB to select the colour under the cursor"
/// (REFERENCES-EDITORS §8 item 7).
///
/// <para>It is an enum and not a bool because it is what the shell <em>says</em>, not a flag it
/// computes: <c>Paint(x, y)</c> with no ink named is the left button, which keeps every existing
/// caller and every existing test honest, and a call site that means the right button has to
/// spell it out. A bool named <c>secondary</c> would read as a modifier on the verb; this reads
/// as which of two brushes is in the hand, which is what it is.</para>
/// </summary>
public enum SpriteEditorInk
{
    Primary,
    Secondary,
}

/// <summary>
/// The sprite editor's whole state and policy, with no window attached (M9 stage 2, waves
/// 2b/2c) — the same split that made <see cref="ShellModeMachine"/> testable: <c>QuarpGame</c>
/// routes keys and mouse hits here, <see cref="SpriteEditorRenderer"/> paints what this says,
/// and every claim the work order makes (one stroke = one undo step, a clean session never
/// touches the disk, nothing above palette index 15 can enter the sheet) is provable headless.
///
/// <para><b>Real layers since wave 2h (ADR-027).</b> The session holds five fixed full
/// sheets, layer index 0 the base and higher indices covering it, color 0 transparent in the
/// composite. Every tool writes the <b>active</b> layer only; the canvas, the sheet view and
/// the saved gfx.png show the flattened <see cref="Pixels"/> composite; the eyedropper and
/// the wand read the active layer, because they answer "what did I draw here", not "what
/// shows through". The console never learns any of this: gfx-layers.png is an authoring file
/// the loader and packer do not read, and the flattened composite is all a cartridge gets.</para>
///
/// <para><b>The 0-15 invariant has exactly four doors — layers added none.</b> Pixels enter
/// a layer through (1) the load in the constructor — <see cref="PngDecoder.DecodeToPaletteIndices"/>
/// only ever emits matches against the 16 visible palette colors, for gfx.png and
/// gfx-layers.png alike; (2) the pencil, the bucket, the colour replace and the shape commit —
/// which write <see cref="CurrentColor"/> or <see cref="SecondaryColor"/> through the one
/// <see cref="InkColor"/> lookup (the shapes through the very same <see cref="Plot"/> the pencil
/// uses), and <see cref="SelectColor"/> throws on anything outside 0-15 for <b>either</b> ink
/// while <see cref="PickColor"/> copies a value already in a layer; (3) undo/redo — which swap
/// whole layer stacks that were themselves stacks; (4) the region edits — flips, the
/// rotation, the selection move and the stamp only copy values already read out of a layer,
/// and the clears (whole region, selected pixels, and the holes a move leaves) write the
/// literal 0. The composite is a pure read of the stack and the layer switch writes no
/// pixels, so there is still no fifth setter and the byte casts in the plot and fill
/// routines can never truncate. The selection mask and the stamp source are session state
/// beside the stack, not in it — marking and grabbing write no pixels at all, only their
/// commits do, through door (4). <see cref="PngEncoder"/> re-checks on save as the owner of
/// its own input contract; that check is unreachable from here by construction.</para>
///
/// <para><b>Dirty is content, not history.</b> <see cref="IsLayersDirty"/> compares the live
/// <b>stack</b> against a snapshot of what the disk holds (or held nothing — an all-zero
/// stack), because the save contract is about bytes: undoing back to the loaded picture makes
/// the session clean again, and even hand-repainting a pixel to its old color counts, since
/// saving then would change nothing. An 80 KB compare per query costs microseconds and cannot
/// drift out of sync the way a depth counter under an undo/redo/new-stroke braid can.
/// A change buried under an opaque upper layer is honestly dirty even though the composite —
/// and therefore gfx.png — comes out byte-identical: the layers file still has to change.
/// <see cref="IsFlagsDirty"/> is the same kind of compare against flags.bin's own baseline, and
/// <see cref="IsDirty"/> is simply either — the two banks are dirty independently and
/// <see cref="Save"/> writes only the one(s) that are.</para>
///
/// <para><b>Sprite flags moved in from <see cref="MapEditorSession"/>, wave 3b-1.</b> The
/// owner's verdict: all three reference consoles author flags in the sprite editor, not the
/// map editor (PICO-8's row of circles, TIC-80's advanced mode and LIKO-12's icon row all sit
/// beside the sheet). A flag is therefore a property of the sprite currently open on the
/// canvas — <see cref="SpriteIndex"/> — with no separate "which sprite" argument, the same way
/// <see cref="CurrentColor"/> is the pencil's ink without a repeated parameter. The 256-byte
/// bank shares this session's <b>one</b> undo stack rather than a second one: a flag write is
/// one <see cref="Snapshot"/> like a stroke, a fill or a transform, so a sheet edit and a flag
/// edit undo/redo in the true order they happened. Length rules, absent-file-is-zeros, and the
/// per-file dirty/save contract moved with the bank unchanged — see <see cref="ReadFlagsPayload"/>
/// and <see cref="RequirePayload"/>. <b>Wave 3b-2</b> gave the bank the panel it was missing and,
/// with it, the reference consoles' group rule: a toggle acts on the whole selected REGION
/// (<see cref="ToggleRegionFlag"/>, TIC-80's <c>drawFlags</c> over <c>getSpriteIndexes</c>), and
/// the panel reads the block's three states off <see cref="RegionFlagsAll"/> and
/// <see cref="RegionFlagsAny"/>. Both public write doors funnel into one private
/// <see cref="WriteRegionFlags"/>, so the bank still has exactly one writer.</para>
///
/// <para><b>The region can never hang off the sheet.</b> The size setter (8/16/32 px a side)
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

    /// <summary>Fixed layer count (ADR-027): five full sheets, no adding or removing.</summary>
    public const int LayerCount = 5;

    /// <summary>
    /// The authoring stack's file beside gfx.png (ADR-027): the five layers as one
    /// 128x640 indexed PNG, layer 0 (the base) the top strip. One name owner — the
    /// constructor reads it, <see cref="Save"/> writes it, tests point at it.
    /// </summary>
    public const string LayersFileName = "gfx-layers.png";

    /// <summary>
    /// The sprite flags binary (SPEC-8 §6) — 256 flag bytes, one per sprite. Moved here from
    /// <see cref="MapEditorSession"/> in wave 3b-1 (the owner's verdict: all three reference
    /// consoles author flags in the sprite editor, not the map editor). One name owner: the
    /// constructor reads it, <see cref="Save"/> writes it, tests point at it.
    /// </summary>
    public const string FlagsFileName = "flags.bin";

    /// <summary>256 sprites in the sheet (SPEC-8 §3), one flag byte each — the flags payload size.</summary>
    public const int FlagsPayloadSize = CartData.FlagCount;

    /// <summary>Eight flags per sprite — the width of the byte, and of <c>Fget</c>'s bit index.</summary>
    public const int FlagBits = 8;

    private readonly string _gfxPath;
    private readonly string _layersPath;
    private readonly string _flagsPath;

    /// <summary>The stack the disk holds: the dirty comparison's baseline, replaced on save. Never aliases <see cref="_layers"/>.</summary>
    private byte[][] _savedLayers;

    /// <summary>
    /// The live stack, base first. Replaced wholesale by undo/redo; only the active layer's
    /// array is ever mutated in place, and only by the tools.
    /// </summary>
    private byte[][] _layers;

    /// <summary>The flags the disk holds, one byte per sprite: the dirty comparison's baseline for the flags bank, replaced on save. Never aliases <see cref="_flags"/>.</summary>
    private byte[] _savedFlags;

    /// <summary>The live flags. Mutated only by <see cref="SetFlags"/>, replaced wholesale by undo/redo alongside the layer stack.</summary>
    private byte[] _flags;

    // The composite cache: rebuilt lazily when Version says the picture moved. 80 K byte
    // reads per rebuild is microseconds; caching exists so sixty Pixels reads a second on an
    // idle editor cost nothing, not because the rebuild is expensive.
    private readonly byte[] _composite = new byte[CartData.GfxWidth * CartData.GfxHeight];
    private int _compositeVersion = -1;

    /// <summary>
    /// One undo/redo entry: the layer stack and the flag bank together (wave 3b-1). A step
    /// restores whichever of the two the operation actually moved — the same "the step is the
    /// operation, whole" contract <see cref="MapEditorSession.Undo"/> used for its own two
    /// banks before the flags moved out of it.
    /// </summary>
    private readonly record struct Snapshot(byte[][] Layers, byte[] Flags);

    // Undo is a stack of pre-stroke snapshots — the whole 5x16 KB layer stack every time (the
    // wave's order), because a snapshot sharing the untouched layers' arrays would be corrupted
    // the moment a later stroke mutated one of them in place. Wave 3b-1 folds the 256-byte flag
    // bank into the SAME snapshot rather than a second stack: a flag write is one step of this
    // one undo stack, exactly like a stroke, a fill or a transform, so undo/redo walk sheet
    // edits and flag edits in one true chronological order instead of two stacks that could
    // disagree about "what happened when". 256 bytes on top of an already-cloned 80 KB stack
    // is noise, so there is still no delta encoding and no cap.
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    /// <summary>Pre-stroke snapshot while the button is down; null between strokes.</summary>
    private Snapshot? _strokeBackup;
    private bool _strokeChanged;
    private int _lastPaintX = -1;
    private int _lastPaintY;

    /// <summary>Which ink the open stroke lays — fixed at <see cref="BeginStroke"/> and read only by <see cref="Plot"/>, so one mark is one colour whatever the palette does mid-drag.</summary>
    private SpriteEditorInk _strokeInk;

    // The shape gesture's whole state: anchor (where the press landed), corner (where the drag
    // is now), the Ctrl-held "filled" flag, and the preview's point set. The points are THE
    // shape — the commit plots this very list, so the preview can never disagree with what
    // lands (one owner of the shape formula, per the playbook). None of it touches a layer:
    // the preview lives here and is drawn by the renderer as an overlay, which is the whole
    // "предпросмотр в лист не пишется" contract made structural.
    private bool _shapeActive;
    private int _shapeAnchorX;
    private int _shapeAnchorY;
    private int _shapeCornerX;
    private int _shapeCornerY;
    private bool _shapeFilled;
    private SpriteEditorInk _shapeInk;
    private readonly List<(int X, int Y)> _shapePoints = new();

    /// <summary>
    /// What an open select-tool press currently means. Rectangle, Brush and Wand are marking a
    /// NEW mask (the kind is fixed at the press — half a box cannot become half a stroke); Move
    /// is dragging the selected pixels by an offset. Exactly one can be open, like the shape
    /// gesture, and none of them touches a layer — only <see cref="CommitSelect"/> does.
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
    /// machine refuses them with the read-only line). The layer stack loads from
    /// gfx-layers.png when it exists (ADR-027: the authoring file is the source, gfx.png the
    /// flattened artifact); without one, layer 0 fills from gfx.png and the rest stay empty —
    /// and no gfx.png at all is the normal case, not an error: snake has none, and an
    /// all-zero stack is exactly what its cart loads (Format spec v1: absent assets = zeros).
    /// Files are only ever created by the first dirty save. A corrupt PNG — either file —
    /// throws <see cref="CartLoadException"/> out of here so the library can report it the
    /// way it reports a broken launch.
    /// </summary>
    public SpriteEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _gfxPath = Path.Combine(cartFolder, "gfx.png");
        _layersPath = Path.Combine(cartFolder, LayersFileName);
        _flagsPath = Path.Combine(cartFolder, FlagsFileName);
        int sheetSize = CartData.GfxWidth * CartData.GfxHeight;
        byte[]? gfxOnDisk = File.Exists(_gfxPath)
            ? PngDecoder.DecodeToPaletteIndices(
                File.ReadAllBytes(_gfxPath), CartData.GfxWidth, CartData.GfxHeight, "gfx.png")
            : null;
        _savedLayers = new byte[LayerCount][];
        if (File.Exists(_layersPath))
        {
            // The stack file wins over gfx.png (ADR-027: gfx.png is the derived artifact).
            // When gfx.png disagrees — an Aseprite edit of the flattened file, most likely —
            // the divergence is surfaced instead of silently absorbed: the next save will
            // overwrite gfx.png with this stack's composite, and the author deserves to know
            // before that happens, not after.
            byte[] stacked = PngDecoder.DecodeToPaletteIndices(
                File.ReadAllBytes(_layersPath),
                CartData.GfxWidth, CartData.GfxHeight * LayerCount, LayersFileName);
            for (int i = 0; i < LayerCount; i++)
            {
                _savedLayers[i] = stacked[(i * sheetSize)..((i + 1) * sheetSize)];
            }
            var flattened = new byte[sheetSize];
            CompositeInto(_savedLayers, flattened);
            GfxOutOfSyncOnDisk = !flattened.AsSpan().SequenceEqual(gfxOnDisk ?? new byte[sheetSize]);
        }
        else
        {
            _savedLayers[0] = gfxOnDisk ?? new byte[sheetSize];
            for (int i = 1; i < LayerCount; i++)
            {
                _savedLayers[i] = new byte[sheetSize];
            }
        }
        _layers = CloneStack(_savedLayers);
        // flags.bin, wave 3b-1: absent file = zeros (SPEC-8 §6), same rule as the sheet itself.
        // A wrong length is refused here with the same failure and wording CartSource produces
        // for the same file, exactly as gfx.png and gfx-layers.png are refused above.
        _savedFlags = ReadFlagsPayload(_flagsPath);
        _flags = (byte[])_savedFlags.Clone();
    }

    /// <summary>Folder name, for the header. The manifest is deliberately not read — same call as <see cref="CartLibraryEntry"/>.</summary>
    public string CartName { get; }

    /// <summary>
    /// The flattened composite, row-major 128x128, values 0-15 — what the canvas, the sheet
    /// view and a saved gfx.png show (ADR-027: layer 0 under, higher layers cover, 0
    /// transparent). Rebuilt lazily against <see cref="Version"/>; reading it twice between
    /// changes is free.
    /// </summary>
    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            if (_compositeVersion != Version)
            {
                CompositeInto(_layers, _composite);
                _compositeVersion = Version;
            }
            return _composite;
        }
    }

    /// <summary>One layer's own pixels, for the tests that prove strokes land in the active layer and nowhere else.</summary>
    public ReadOnlySpan<byte> LayerPixels(int index)
    {
        if (index is < 0 or >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"layers are 0-{LayerCount - 1}.");
        }
        return _layers[index];
    }

    /// <summary>
    /// The layer every tool writes, 0-based (the tabs and tooltips show 1-based). Written
    /// only by <see cref="SelectLayer"/>.
    /// </summary>
    public int ActiveLayerIndex { get; private set; }

    /// <summary>
    /// True when gfx-layers.png was loaded but its composite is not what gfx.png holds —
    /// someone edited the flattened file outside (Aseprite is a first-class path, ADR-026).
    /// The stack wins by ADR-027, so the next save overwrites gfx.png; the renderer shows
    /// this so that overwrite is announced, not a surprise. Cleared by the save that
    /// reconciles the two files.
    /// </summary>
    public bool GfxOutOfSyncOnDisk { get; private set; }

    /// <summary>
    /// Layer tab click, or PgUp/PgDn. Clamped, not thrown, like the region anchor: PgUp held
    /// at the top layer parks there. Switching mid-gesture is a change of subject, same
    /// discipline as <see cref="SelectTool"/>: an open float parks on the layer it grabbed
    /// from (its pixels belong there), open previews and half-made masks die — but a
    /// <b>committed</b> selection survives, Photoshop's own convention: the mask marks
    /// region positions, not layer pixels, and the ants stay visible under the select tool,
    /// so nothing lingers invisibly (the third review's trap). Selecting the layer already
    /// active is a visible no-op so a repeated tab click cannot eat an open gesture.
    /// </summary>
    public void SelectLayer(int index)
    {
        int next = Math.Clamp(index, 0, LayerCount - 1);
        if (next == ActiveLayerIndex)
        {
            return;
        }
        if (MoveActive)
        {
            CommitSelect();     // parked on the OLD layer — the float's pixels were lifted from it
        }
        InterruptGesture();
        ActiveLayerIndex = next;
        // No Version bump: switching layers repaints nothing — the composite is unchanged.
    }

    /// <summary>The LEFT button's ink, always a visible palette index 0-15. Painting with 0 IS the eraser (work order: no separate tool). TIC-80's <c>color</c>.</summary>
    public int CurrentColor { get; private set; }

    /// <summary>
    /// The RIGHT button's ink — TIC-80's <c>color2</c>, LIKO-12's <c>colsR</c>
    /// (REFERENCES-EDITORS §8 item 7). A second remembered colour and not a modifier: the whole
    /// point of the pair is that the two survive each other, so an author can keep an outline
    /// colour under one finger and the fill under the other.
    ///
    /// <para>It opens on 0, the same index <see cref="CurrentColor"/> opens on, and that is a
    /// choice rather than a default left alone: colour 0 is this editor's eraser (there is no
    /// eraser tool), so a fresh sheet hands the author draw-on-the-left / erase-on-the-right,
    /// which is the use a second button is most often reached for. The references do not settle
    /// this — the survey records their two colours, not their opening values — so the reason is
    /// written here rather than borrowed. The palette marks the two distinguishably even when
    /// they coincide, which is what makes the shared opening value legible: see
    /// <c>SpriteEditorRenderer.DrawSwatches</c>.</para>
    /// </summary>
    public int SecondaryColor { get; private set; }

    /// <summary>
    /// The colour one ink stands for. The <b>one</b> lookup: <see cref="Plot"/>,
    /// <see cref="Fill"/> and <see cref="ReplaceColor"/> all read the sheet's next byte through
    /// it, so no verb can grow a private idea of which button it is serving.
    /// </summary>
    public int InkColor(SpriteEditorInk ink) =>
        ink == SpriteEditorInk.Secondary ? SecondaryColor : CurrentColor;

    /// <summary>
    /// How many brush sizes the ladder offers — four, TIC-80's own
    /// <c>#define BRUSH_SIZES 4</c> (<c>src/studio/editors/sprite.c</c>), and the same count
    /// LIKO-12's Size slider has (REFERENCES-EDITORS §2.1, §2.2).
    /// </summary>
    public const int BrushSizeCount = 4;

    /// <summary>
    /// The ladder itself: 1, 2, 3, 4 pixels a side. <b>TIC-80's ladder, not LIKO-12's.</b> The
    /// two references disagree — LIKO-12's <c>sizes</c> table is <c>{1,2,3,5}</c> — and this
    /// screen follows TIC-80 because TIC-80 is also where the <c>-</c>/<c>=</c> keys and the
    /// panel control come from, and because a 5-px brush covers a third of an 8-px sprite's
    /// width in one dab, which on this console's smallest region is a blunter instrument than
    /// the ladder's top step should be. Named here rather than left as a formula so the
    /// divergence is a fact with an owner instead of an arithmetic accident.
    /// </summary>
    private static readonly int[] _brushSizes = { 1, 2, 3, 4 };

    /// <summary>The ladder in order — the panel's list and the <c>-</c>/<c>=</c> walk read the same array.</summary>
    public static IReadOnlyList<int> BrushSizes => _brushSizes;

    /// <summary>Ladder index → brush side in pixels. Inverse of <see cref="BrushIndexOf"/>.</summary>
    public static int BrushSizeAt(int index)
    {
        if (index is < 0 or >= BrushSizeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"the brush ladder has {BrushSizeCount} steps (TIC-80's BRUSH_SIZES).");
        }
        return _brushSizes[index];
    }

    /// <summary>Brush side in pixels → its ladder index; -1 for a side that is not on the ladder.</summary>
    public static int BrushIndexOf(int size) => Array.IndexOf(_brushSizes, size);

    /// <summary>
    /// The pencil's brush side in region pixels — one dab is a <see cref="BrushSize"/>-square
    /// centred under the cursor. It reaches the pencil and the eraser only (the eraser IS the
    /// pencil holding colour 0 here), never the bucket, the shapes or the selection: TIC-80
    /// runs its <c>brushSize</c> through <c>paintPoint</c>, which only <c>SPRITE_DRAW_MODE</c>
    /// calls, and a fill or a marquee has no stroke width to be.
    /// </summary>
    public int BrushSize { get; private set; } = 1;

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

    /// <summary>
    /// How many sheet cells wide the <b>marked block</b> is — the free N×M rectangle a drag
    /// across the sheet window marks (REFERENCES-EDITORS §8 item 3: TIC-80's
    /// <c>map-&gt;sheet.rect</c>, PICO-8's "shift+drag in the sprite navigator"), the twin of
    /// <see cref="MapEditorSession.BlockWidth"/> one screen over. One until a drag or
    /// Ctrl+Shift+arrows says otherwise.
    ///
    /// <para><b>Why this is a SECOND fact and not a wider <see cref="RegionCells"/>.</b> The
    /// square region is the CANVAS: <see cref="RegionPixels"/> validates every canvas-local
    /// coordinate, <see cref="SpriteEditorLayout"/> divides a fixed 64x64 box by it to get a
    /// whole-integer zoom, the selection mask is allocated <c>RegionPixels * RegionPixels</c>,
    /// and <see cref="RotateClockwise"/> turns it in place — an operation that only exists for a
    /// square. Widening that one field to N×M would touch every one of those, hand the canvas a
    /// non-square box (two zooms, or a fractional one, which ARCHITECTURE §5 forbids), and break
    /// the 8/16/32 ladder Tab and the size toggle walk. The block is a different question —
    /// "how much of the SHEET is marked" — and it gets its own pair of numbers, exactly as the
    /// map screen keeps <c>SelectedSprite</c> and <c>BlockWidth</c>/<c>BlockHeight</c> apart.</para>
    ///
    /// <para><b>The price of that choice, said out loud.</b> Two facts now answer "how many
    /// sprites are in hand", and they must not be allowed to disagree. They are held together by
    /// one invariant, enforced in <see cref="SelectRegionBlock"/> and nowhere else: <b>the block
    /// always contains the region</b> — never narrower or shorter than <see cref="RegionCells"/>,
    /// and reset to exactly <c>RegionCells × RegionCells</c> by every door that names a single
    /// cell (<see cref="SelectRegionCell"/>) or changes the zoom
    /// (<see cref="SelectRegionSize"/>). So the flag row, which folds over the block, still
    /// covers every sprite the canvas is editing, and a screen that never drags a rectangle
    /// behaves exactly as it did before this wave. What the author does NOT get for the extra
    /// fact: the canvas still shows the block's anchor cell and its square region, not the whole
    /// marked rectangle. Marking 5x2 sprites marks them for the flag row and for the eye; it
    /// does not zoom the canvas out to 5x2, because that is the operation the paragraph above
    /// says this shell cannot afford.</para>
    ///
    /// <para><b>The unit is a SHEET cell, unlike the map's.</b> That screen keeps its block in
    /// <em>picker</em> cells and resolves them through <see cref="SheetStrip"/> one layer up,
    /// because a map tile is just a number. Here the block is a rectangle of the sheet itself —
    /// the flag fold and the region anchor are both stated in sheet cells — so the view clamps
    /// the drag inside one strip lane before it arrives (see <c>SpriteEditorView</c>) and this
    /// class never needs to know the strip exists.</para>
    /// </summary>
    public int BlockWidth { get; private set; } = 1;

    /// <summary>How many sheet cells tall the marked block is — <see cref="BlockWidth"/>'s other half.</summary>
    public int BlockHeight { get; private set; } = 1;

    /// <summary>Sprite number of the region's anchor cell — the "#NNN" the header shows, same numbering Spr(n) uses.</summary>
    public int SpriteIndex => RegionCellY * GridCells + RegionCellX;

    /// <summary>
    /// The flags byte of the SELECTED sprite (<see cref="SpriteIndex"/>) — moved here from
    /// <see cref="MapEditorSession"/> in wave 3b-1. A flag is a property of whichever sprite is
    /// open on the canvas, not a value the panel names by number, so this door takes no sprite
    /// argument: it always answers for <see cref="SpriteIndex"/>, the same sprite the region
    /// anchor, the pixels and the eyedropper already answer for.
    /// </summary>
    public byte Flags => _flags[SpriteIndex];

    /// <summary>One flag bit of the selected sprite, the shape <c>Fget</c> has (API-8 §3).</summary>
    public bool IsFlagSet(int bit)
    {
        ValidateBit(bit);
        return (_flags[SpriteIndex] & (1 << bit)) != 0;
    }

    /// <summary>
    /// Bits raised on <b>every</b> sprite of the marked block — TIC-80's <c>and</c> in
    /// <c>drawFlags</c> (REFERENCES-EDITORS §2.1), the fold over the same block
    /// <c>getSpriteIndexes</c> returns there. With one cell in hand this is
    /// <see cref="Flags"/> itself; over a bigger region or a dragged rectangle it is what makes
    /// the panel's three states possible, and it is also what decides which way a click on a
    /// toggle goes (see <see cref="ToggleRegionFlag"/>).
    ///
    /// <para><b>The fold follows <see cref="BlockWidth"/>, not <see cref="RegionCells"/>, since
    /// the free-rectangle wave</b> — and on a screen that never drags a rectangle the two are
    /// the same numbers, because the block is reset to the square region by every door that
    /// names a single cell. That is the whole of what makes the second fact safe here: it can
    /// only ever be <em>wider</em> than what the canvas edits, never narrower, so no sprite the
    /// author is drawing on can fall out of the fold.</para>
    /// </summary>
    public byte RegionFlagsAll
    {
        get
        {
            int all = 0xFF;
            for (int dy = 0; dy < BlockHeight; dy++)
            {
                for (int dx = 0; dx < BlockWidth; dx++)
                {
                    all &= _flags[RegionSprite(dx, dy)];
                }
            }
            return (byte)all;
        }
    }

    /// <summary>Bits raised on <b>at least one</b> sprite of the marked block — TIC-80's <c>or</c>, the panel's "some of them" dot.</summary>
    public byte RegionFlagsAny
    {
        get
        {
            int any = 0;
            for (int dy = 0; dy < BlockHeight; dy++)
            {
                for (int dx = 0; dx < BlockWidth; dx++)
                {
                    any |= _flags[RegionSprite(dx, dy)];
                }
            }
            return (byte)any;
        }
    }

    /// <summary>One bit of <see cref="RegionFlagsAll"/> — the panel's "filled" state.</summary>
    public bool IsFlagSetInAll(int bit)
    {
        ValidateBit(bit);
        return (RegionFlagsAll & (1 << bit)) != 0;
    }

    /// <summary>One bit of <see cref="RegionFlagsAny"/> — with <see cref="IsFlagSetInAll"/> false, the panel's "dot".</summary>
    public bool IsFlagSetInAny(int bit)
    {
        ValidateBit(bit);
        return (RegionFlagsAny & (1 << bit)) != 0;
    }

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

    /// <summary>True when the live layer stack differs from what gfx-layers.png holds — see the type comment for why this is a content compare.</summary>
    public bool IsLayersDirty
    {
        get
        {
            for (int i = 0; i < LayerCount; i++)
            {
                if (!_layers[i].AsSpan().SequenceEqual(_savedLayers[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>True when the live flags differ from what flags.bin holds — its own file, its own compare, wave 3b-1's per-file dirty rule.</summary>
    public bool IsFlagsDirty => !_flags.AsSpan().SequenceEqual(_savedFlags);

    /// <summary>True when either the sheet or the flags differ from the disk — what the exit prompt and Ctrl+S both ask.</summary>
    public bool IsDirty => IsLayersDirty || IsFlagsDirty;

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

    /// <summary>
    /// What the last clipboard verb refused to do, or null — the sentence the screen's message
    /// line shows. Every one of the four editors carries one of these, all in the session, so a
    /// refusal reaches the author by the road <see cref="SaveError"/> already uses instead of
    /// each screen inventing a channel of its own.
    ///
    /// <para><b>Transient by construction.</b> Each clipboard verb clears it on the way in, so
    /// it says what the <em>last</em> Ctrl+C/X/V did and never a stale complaint about one three
    /// gestures ago. A verb that succeeds leaves it null, which is how "nothing to say" is
    /// spelled.</para>
    /// </summary>
    public string? ClipboardNotice { get; private set; }

    /// <summary>
    /// Swatch click, into whichever ink the button that clicked it holds — left button (and
    /// every keyboard path) into <see cref="CurrentColor"/>, right button into
    /// <see cref="SecondaryColor"/>, exactly TIC-80's <c>drawPalette</c>. Throws rather than
    /// masks: a value outside 0-15 here is a caller bug, and masking it would paint the wrong
    /// color silently. The default keeps every pre-two-ink caller meaning what it always meant.
    /// </summary>
    public void SelectColor(int color, SpriteEditorInk ink = SpriteEditorInk.Primary)
    {
        if (color is < 0 or >= Palette.VisibleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color), color, "the pencil takes visible palette indices 0-15 only (SPEC-8 §6).");
        }
        if (ink == SpriteEditorInk.Secondary)
        {
            SecondaryColor = color;
        }
        else
        {
            CurrentColor = color;
        }
    }

    /// <summary>
    /// Direct brush size — a side from <see cref="BrushSizes"/>, and nothing else. Throws on any
    /// other value for the reason <see cref="SelectRegionSize"/> does: the callers are the panel
    /// list and the <c>-</c>/<c>=</c> walk, both of which read the ladder, so a foreign number is
    /// a bug, and clamping it would silently hand the author a brush they never picked.
    ///
    /// <para>It does not end an open stroke, and that is the point of the whole feature: TIC-80's
    /// <c>updateBrushSize</c> writes one field and nothing else, so a size changed mid-drag
    /// simply widens the rest of the same mark, and the stroke stays <b>one</b> undo step.</para>
    /// </summary>
    public void SelectBrushSize(int size)
    {
        if (BrushIndexOf(size) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size), size, "brush sides are 1, 2, 3 or 4 pixels (TIC-80's BRUSH_SIZES ladder).");
        }
        BrushSize = size;
    }

    /// <summary>
    /// The <c>-</c> and <c>=</c> keys' walk along the ladder, TIC-80's <c>updateBrushSize</c>:
    /// <b>cyclic</b>, so the smallest brush is one press away from the largest and neither end
    /// is a dead key. Funnels through <see cref="SelectBrushSize"/> so the keyboard and the panel
    /// list cannot drift about what a step is.
    /// </summary>
    public void CycleBrushSize(int delta)
    {
        int index = BrushIndexOf(BrushSize);
        // Two modulos: C# keeps the sign of the dividend, and -1 % 4 is -1, not 3.
        int next = ((index + delta) % BrushSizeCount + BrushSizeCount) % BrushSizeCount;
        SelectBrushSize(BrushSizeAt(next));
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
        // One cell is one region: every path that names a single sheet cell — a click on the
        // sheet window, Shift+arrows through EditorSheetStep, a size change re-clamping itself —
        // arrives here, so a marked block cannot survive one of them and silently widen the next
        // flag click. Growing it back is SelectRegionBlock's job and nothing else's. This is the
        // map screen's rule verbatim (MapEditorSession.SelectSprite resets its block to 1x1); the
        // only difference is the value it resets to, because on this screen the SQUARE REGION is
        // what a single cell means and the block may never be smaller than it.
        BlockWidth = RegionCells;
        BlockHeight = RegionCells;
    }

    /// <summary>
    /// The sheet drag's door: the block's top-left sheet cell and its size in sheet cells
    /// (REFERENCES-EDITORS §8 item 3, TIC-80's <c>map-&gt;sheet.rect</c>). The anchor travels
    /// through <see cref="SelectRegionCell"/>, so a drag moves the canvas to the block's corner
    /// and drops a stale mask exactly as a click does — and because that door resets the block,
    /// the size is written afterwards and not before.
    ///
    /// <para>Everything is clamped rather than thrown: this is the far end of a mouse drag, and
    /// the pointer is free to leave the window mid-gesture. The floor is
    /// <see cref="RegionCells"/> — see <see cref="BlockWidth"/> for the invariant and its
    /// price — and the ceiling is the sheet's own edge, so a block can never name a cell the
    /// bank does not have.</para>
    /// </summary>
    public void SelectRegionBlock(int cellX, int cellY, int width, int height)
    {
        SelectRegionCell(cellX, cellY);
        BlockWidth = Math.Clamp(width, RegionCells, GridCells - RegionCellX);
        BlockHeight = Math.Clamp(height, RegionCells, GridCells - RegionCellY);
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
    /// A paint button pressed on the canvas. The pre-stroke stack is snapshotted here and becomes
    /// the undo entry when the stroke ends — which is the whole "one stroke = one undo step"
    /// mechanism: nothing inside the stroke ever touches the undo stack. <b>Which ink</b> is
    /// fixed at the press — the button that started the mark is what the mark is made of, so a
    /// right-drag stays the right button's however the left one is used mid-gesture. Which
    /// <em>colour</em> that ink names is read live, per pixel, exactly as it was before there
    /// were two: the palette is a hand, not a snapshot.
    /// </summary>
    public void BeginStroke(SpriteEditorInk ink = SpriteEditorInk.Primary)
    {
        if (StrokeActive)
        {
            return;     // A second press without a release (focus loss glitches) folds into the open stroke.
        }
        _strokeBackup = TakeSnapshot();
        _strokeChanged = false;
        _lastPaintX = -1;
        _strokeInk = ink;
    }

    /// <summary>
    /// One pencil sample at a region-local pixel. Consecutive samples within a stroke are
    /// joined with a Bresenham line, because the mouse reports positions per frame, not per
    /// pixel — a fast drag would otherwise dot the canvas instead of drawing through it
    /// (the "тянущийся мазок" the order demands). Every point of that line is a
    /// <see cref="BrushSize"/>-square <see cref="Dab"/> rather than a single pixel, which is
    /// TIC-80's own arrangement — <c>paintLine</c> walks the segment and calls <c>paintPoint</c>,
    /// and <c>paintPoint</c> is the thing that knows how wide the brush is.
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
            Dab(localX, localY);
        }
        else
        {
            TraceLine(_lastPaintX, _lastPaintY, localX, localY, Dab);
        }
        _lastPaintX = localX;
        _lastPaintY = localY;
    }

    /// <summary>
    /// One dab of the brush: the <see cref="BrushSize"/>-square centred on the given region-local
    /// pixel (TIC-80's <c>paintPoint</c>, REFERENCES-EDITORS §2.1), clipped to the region instead
    /// of wrapping or throwing. The clip is not a nicety: region-local coordinates address the
    /// sheet through <see cref="SheetOffset"/>, so a negative x is not "off the canvas" but
    /// <b>the neighbouring sprite</b> — an unclipped dab at the edge would silently edit a
    /// sprite the author is not looking at.
    ///
    /// <para>The centring offset is <c>size / 2</c>: odd sizes sit truly centred and even sizes
    /// lean up and left by half a pixel, which is the only thing an even-sided square can do
    /// around a point. The references settle that a dab is a centred square and not which way an
    /// even one leans, so the tie-break is ours and is written down here. Nothing in this method
    /// touches the undo stack — the dab writes through <see cref="Plot"/> like every other pixel
    /// of the stroke, so a hundred dabs are still one step.</para>
    /// </summary>
    private void Dab(int localX, int localY)
    {
        int size = BrushSize;
        int origin = size / 2;
        int n = RegionPixels;
        for (int dy = 0; dy < size; dy++)
        {
            int y = localY + dy - origin;
            if (y < 0 || y >= n)
            {
                continue;
            }
            for (int dx = 0; dx < size; dx++)
            {
                int x = localX + dx - origin;
                if (x < 0 || x >= n)
                {
                    continue;
                }
                Plot(x, y);
            }
        }
    }

    /// <summary>
    /// Button released: the stroke commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z appear dead). Safe to call without an open stroke: releases arrive when the
    /// press landed outside the canvas.
    /// </summary>
    public void EndStroke()
    {
        if (_strokeBackup is not Snapshot backup)
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

    /// <summary>
    /// The eyedropper: the color under the cursor becomes an ink — from the <b>active layer</b>,
    /// not the composite (the wave's card): it answers "what did I draw here", and picking a
    /// covering layer's color while standing on a lower one would hand the pencil ink the
    /// author never placed on this layer.
    ///
    /// <para><b>It fills the ink of the button that reached for it</b> (REFERENCES-EDITORS §8
    /// item 7). TIC-80 puts the always-available eyedropper on the middle button and sends it to
    /// <c>color</c> (<c>drawCanvasVBank1</c>: <c>checkMouseDown(..., tic_mouse_middle)</c>),
    /// and its picker tool sends the left button to <c>color</c> and the right to <c>color2</c>.
    /// That is the whole of the parameter: an eyedropper that always filled the left ink would
    /// make the second colour reachable only through the palette.</para>
    /// </summary>
    public void PickColor(int localX, int localY, SpriteEditorInk ink = SpriteEditorInk.Primary)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        // The sheet only ever holds 0-15 (the type comment's four doors), so this needs no
        // range check — and going through SelectColor would add one that can never fire.
        byte picked = ActiveSheet[SheetOffset(localX, localY)];
        if (ink == SpriteEditorInk.Secondary)
        {
            SecondaryColor = picked;
        }
        else
        {
            CurrentColor = picked;
        }
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
    /// The ink is fixed at the press for the reason <see cref="BeginStroke"/> fixes its own: the
    /// button that opened the gesture is what the shape will be drawn in, and the preview shows
    /// that same colour through <see cref="ShapeInk"/> long before the commit.
    /// </summary>
    public void BeginShape(int localX, int localY, SpriteEditorInk ink = SpriteEditorInk.Primary)
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
        _shapeInk = ink;
        RebuildShapePreview();
    }

    /// <summary>Which ink the open shape will commit in — what the renderer paints the preview with, so the preview never lies about the colour.</summary>
    public SpriteEditorInk ShapeInk => _shapeInk;

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
        // The gesture's own ink, not the left button's — and NOT through Dab: a shape's width is
        // its own geometry, so the brush ladder must not thicken its contour (TIC-80 keeps
        // brushSize inside paintPoint, which only the pencil reaches).
        BeginStroke(_shapeInk);
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
    /// straight out of the <b>active layer</b> (the selection marked what the author drew
    /// there, not what shows through from other layers), so they are 0-15 by the layer's own
    /// invariant. Box cells outside the mask stay 0, the same "prints nothing" a masked 0
    /// pixel has.
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
                    _stampSource[(y - minY) * _stampWidth + (x - minX)] = ActiveSheet[SheetOffset(x, y)];
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

    /// <summary>Tab: region side 1 → 2 → 4 → 1 cells — the keyboard's walk over the same setter the size toggle's list picks from.</summary>
    public void CycleRegionSize() =>
        SelectRegionSize(RegionCells switch { 1 => 2, 2 => 4, _ => 1 });

    /// <summary>
    /// Direct region size — 1, 2 or 4 cells a side (the toggle's 8/16/32 list, wave 2h; Tab
    /// funnels here too, so the two paths cannot drift). Throws on any other value: the
    /// callers are a fixed list and Tab's cycle, so anything else is a bug, and clamping it
    /// would silently edit a region size the author never picked. Re-clamps the anchor
    /// through <see cref="SelectRegionCell"/> — growing at the sheet's edge pulls the region
    /// back inside rather than letting it clip, which is the invariant every transform relies
    /// on. Ends an open stroke first: the stroke's last-point memory is in old region
    /// coordinates, and joining a line across a size change could leave the shrunk region's
    /// bounds. Picking the size already current is a visible no-op, like re-picking the
    /// active tool — the list click must not eat an open gesture for nothing.
    /// </summary>
    public void SelectRegionSize(int cells)
    {
        if (cells is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(cells), cells, "region sides are 1, 2 or 4 sprite cells (8/16/32 px).");
        }
        if (cells == RegionCells)
        {
            return;
        }
        InterruptGesture();
        ClearSelection();       // the mask is sized to the region — a resize would misindex it
        RegionCells = cells;
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
    public void Fill(int localX, int localY, SpriteEditorInk ink = SpriteEditorInk.Primary)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        InterruptGesture();     // A stray open gesture commits as its own step before the fill becomes one.
        byte target = ActiveSheet[SheetOffset(localX, localY)];
        int color = InkColor(ink);
        if (target == color)
        {
            return;
        }
        // target != color guarantees at least the seed pixel changes, so the undo
        // snapshot is taken unconditionally — there is no "nothing changed" path from here.
        _undo.Add(TakeSnapshot());
        _redo.Clear();
        VisitConnectedColor(localX, localY, (x, y) => ActiveSheet[SheetOffset(x, y)] = (byte)color);
        Version++;
    }

    /// <summary>
    /// The bucket's second half: <b>replace this colour everywhere in the region</b>, connected
    /// or not. TIC-80 hangs it on Ctrl over the fill tool — <c>processFillCanvasMouse</c> calls
    /// <c>replaceColor</c> instead of <c>floodFill</c> while <c>tic_key_ctrl</c> is down — LIKO-12
    /// gives it its own mode, and PICO-8's manual says the same words on the draw tool ("Hold
    /// CTRL to search and replace colour"); REFERENCES-EDITORS §8 item 6 is where the three meet.
    ///
    /// <para><b>Its border is the fill's border, to the pixel.</b> <see cref="Fill"/> walls its
    /// flood at <see cref="RegionPixels"/> and addresses the sheet through
    /// <see cref="SheetOffset"/>; this scans exactly that square through exactly that offset. So
    /// "this area" means the same thing under both halves of the tool — which is the whole reason
    /// the flood's walls were given one owner in the first place — and a replace can no more
    /// reach a neighbouring sprite than a fill can.</para>
    ///
    /// <para>One undo step, like a fill; and replacing a colour with itself changes nothing, so
    /// it never happened as far as undo and dirt are concerned. Unlike the fill, no scan is
    /// needed before the snapshot: the seed pixel <em>is</em> one of the pixels holding the
    /// target colour, so at least it changes.</para>
    /// </summary>
    public void ReplaceColor(int localX, int localY, SpriteEditorInk ink = SpriteEditorInk.Primary)
    {
        ValidateLocal(localX, nameof(localX));
        ValidateLocal(localY, nameof(localY));
        InterruptGesture();
        byte target = ActiveSheet[SheetOffset(localX, localY)];
        int color = InkColor(ink);
        if (target == color)
        {
            return;
        }
        _undo.Add(TakeSnapshot());
        _redo.Clear();
        int n = RegionPixels;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int offset = SheetOffset(x, y);
                if (ActiveSheet[offset] == target)
                {
                    ActiveSheet[offset] = (byte)color;
                }
            }
        }
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
        byte target = ActiveSheet[SheetOffset(localX, localY)];
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
            if (ActiveSheet[SheetOffset(x, y)] != target)
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

    // ---- the clipboard, as text (REFERENCES-EDITORS §8 item 2) ----

    /// <summary>
    /// <c>Ctrl+C</c>: what is under the author, as one line of <see cref="ClipboardFormat"/>
    /// text. <b>Which pixels</b> is not a new decision — it is the selection model this editor
    /// already has: a committed mask's bounding box if there is one, the whole region if there
    /// is not, which is the same pair <see cref="ClearRegion"/> chooses between and the same
    /// rectangle <c>CaptureStampSource</c> takes.
    ///
    /// <para>Values come out of the <b>active layer</b>, like the stamp's do, and for the same
    /// reason: the author marked what they drew there, not what shows through from underneath.
    /// Cells of the box that the mask does not cover come out as colour 0 — again the stamp's
    /// rule — so a round trip through a <em>rectangular</em> selection is byte-exact and a round
    /// trip through a brush-shaped one squares its corners off. That is a property of the
    /// rectangle a text block has to be, not of this method.</para>
    ///
    /// <para>Never null and never empty in practice: a region always exists, so there is always
    /// something to copy. Whoever hands the string to the machine's clipboard is layer 4's
    /// business (see <see cref="ITextClipboard"/>); this session only produces the string.</para>
    /// </summary>
    public string CopyToText()
    {
        ClipboardNotice = null;
        int n = RegionPixels;
        int minX = 0;
        int minY = 0;
        int maxX = n - 1;
        int maxY = n - 1;
        bool[]? mask = _selection;
        if (mask is not null && TrySelectionBounds(out int sx, out int sy, out int ex, out int ey))
        {
            minX = sx;
            minY = sy;
            maxX = ex;
            maxY = ey;
        }
        else
        {
            mask = null;
        }
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        var pixels = new byte[width * height];
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                pixels[((y - minY) * width) + (x - minX)] =
                    mask is null || mask[(y * n) + x] ? ActiveSheet[SheetOffset(x, y)] : (byte)0;
            }
        }
        return ClipboardFormat.EncodeSprites(width, height, pixels);
    }

    /// <summary>
    /// <c>Ctrl+X</c>: the same text, and then the same pixels emptied — TIC-80's own composition
    /// for this editor (<c>copy + deleteSprite</c>, REFERENCES-EDITORS §2.1). The clearing is
    /// <see cref="ClearRegion"/>, so it is <b>one</b> undo step and it empties exactly what was
    /// copied (the mask if there is one, the region if not) rather than a second rectangle
    /// computed here.
    /// </summary>
    public string CutToText()
    {
        string text = CopyToText();
        ClearRegion();
        return text;
    }

    /// <summary>
    /// <c>Ctrl+V</c>: a block of sprite text laid into the region's top-left corner as <b>one</b>
    /// undo step, opaque — colour 0 in the block writes colour 0, it is not treated as
    /// transparent. That is the difference between this and the stamp tool
    /// (<see cref="StampAt"/>), and it is deliberate: a stamp is a brush and a paste is a
    /// replacement, so only the opaque reading makes "copy, paste, compare" give back the bytes
    /// that were copied. TIC-80 reads it the same way — its sprite paste overwrites the region
    /// wholesale (<c>copyFromClipboard</c>, §2.1).
    ///
    /// <para><b>The corner, not the cursor.</b> TIC-80's sprite paste is size-locked
    /// (<c>sameSize=true</c>, §1) and lands on the region it was taken from; ours accepts a
    /// block <em>no larger than</em> the region — because our own copy may be a sub-region
    /// selection — and lands it at the corner. A block that would not fit is refused outright
    /// with its measurements in the sentence, never clipped: half a pasted sprite looks like a
    /// drawing mistake and would be undone by hand instead of understood.</para>
    /// </summary>
    /// <returns>True when the sheet was written; false with <see cref="ClipboardNotice"/> set otherwise.</returns>
    public bool PasteFromText(string? text)
    {
        ClipboardNotice = null;
        if (!ClipboardFormat.TryDecode(text, ClipboardKind.Sprites, out ClipboardBlock? block, out string reason))
        {
            ClipboardNotice = $"PASTE: {reason}";
            return false;
        }
        int n = RegionPixels;
        int width = block!.Width;
        int height = block.Height;
        if (width > n || height > n)
        {
            ClipboardNotice = $"PASTE: {width}x{height} BLOCK, REGION IS {n}x{n}";
            return false;
        }
        byte[] pixels = block.Bytes.ToArray();
        ApplyRegionEdit((src, side, x, y) =>
            x < width && y < height ? pixels[(y * width) + x] : src[(y * side) + x]);
        return true;
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
                before[y * n + x] = ActiveSheet[SheetOffset(x, y)];
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
        _undo.Add(TakeSnapshot());
        _redo.Clear();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                ActiveSheet[SheetOffset(x, y)] = after[y * n + x];
            }
        }
        Version++;
    }

    /// <summary>
    /// Ctrl+Z. Ends an open stroke first (committing it), so an undo mid-drag rolls back a
    /// whole gesture instead of tearing one in half. Whole-stack swaps, no copying: the
    /// snapshot stacks already exist and nothing else holds them. The stack restores whole —
    /// an undo standing on layer 4 rolls back a stroke made on layer 2, because the step is
    /// the operation, wherever it landed; the active-layer <b>choice</b> is deliberately not
    /// part of the snapshot (it is where the author is looking, not what they did). History
    /// lives in the session only: closing the editor forgets it, and a fresh session opens
    /// with Ctrl+Z honestly dead (pinned by test — silently replaying stale history against
    /// a disk someone else may have touched would be worse than forgetting).
    /// </summary>
    public void Undo()
    {
        InterruptGesture();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(new Snapshot(_layers, _flags));
        Snapshot previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _layers = previous.Layers;
        _flags = previous.Flags;
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
        _undo.Add(new Snapshot(_layers, _flags));
        Snapshot next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _layers = next.Layers;
        _flags = next.Flags;
        Version++;
    }

    /// <summary>
    /// The flag panel's write door: the whole flags byte of the SELECTED sprite
    /// (<see cref="SpriteIndex"/>), one operation, one step of this session's <b>one</b> undo
    /// stack — wave 3b-1's whole point, so a flag edit and a stroke undo/redo in the true order
    /// they happened, never on two stacks that could disagree. An open stroke, shape or select
    /// gesture is closed first through <see cref="InterruptGesture"/> — otherwise the flag step
    /// would land <em>under</em> the pre-gesture snapshot and undo would replay the two
    /// operations backwards; this is the exact ordering <c>MapEditorSession.SetFlags</c>
    /// established first, back when the flag bank still lived there (there via a plain
    /// <c>EndStroke</c>, the map editor's only open gesture) — carried over rather than
    /// reinvented, generalized here to <see cref="InterruptGesture"/> because the sprite editor
    /// has more kinds of open gesture than a stroke. Writing the value already there changes
    /// nothing and is therefore not a step and not dirt, exactly like a stroke that painted the
    /// color already at a pixel.
    ///
    /// <para>Since wave 3b-2 the body is <see cref="WriteRegionFlags"/> over a block of ONE
    /// cell — the same write, the same undo step, the same no-op rule, expressed through the
    /// one private writer this door now shares with <see cref="ToggleRegionFlag"/>. Two public
    /// doors, one owner of the assignment: a second place that touched <c>_flags</c> is exactly
    /// what the wave's order forbade.</para>
    /// </summary>
    public void SetFlags(byte value) => WriteRegionFlags(1, 1, _ => value);

    /// <summary>One checkbox in the flag panel: flips a single bit of the selected sprite, through <see cref="SetFlags"/> so there is one write door.</summary>
    public void ToggleFlag(int bit)
    {
        ValidateBit(bit);
        SetFlags((byte)(_flags[SpriteIndex] ^ (1 << bit)));
    }

    /// <summary>
    /// The flag panel's actual click (wave 3b-2): one bit, applied to <b>every sprite of the
    /// selected region</b>, as one step of this session's one undo stack. Our region is
    /// TIC-80's <c>sprite-&gt;size</c> block, so this is that console's <c>drawFlags</c> rule
    /// carried over rather than reinvented (REFERENCES-EDITORS §2.1) — including which way the
    /// click goes when the block disagrees with itself: <c>and</c> decides. If the bit is
    /// raised on <em>every</em> sprite of the block it comes down on every sprite; in every
    /// other case — none of them, or only some — it goes <em>up</em> on every sprite. So the
    /// mixed state is a state a click leaves, never one a click can produce, and two clicks
    /// from any starting state land on "raised on all, then lowered on all".
    ///
    /// <para>It is not a second owner of the flag bank: it shares
    /// <see cref="WriteRegionFlags"/> with <see cref="SetFlags"/>, which is the one place a
    /// flag byte is ever assigned and the one place a flag undo step is ever pushed. At
    /// <see cref="RegionCells"/> 1 the two are the same operation over the same single sprite.</para>
    /// </summary>
    public void ToggleRegionFlag(int bit)
    {
        ValidateBit(bit);
        int mask = 1 << bit;
        if ((RegionFlagsAll & mask) == 0)
        {
            WriteRegionFlags(BlockWidth, BlockHeight, current => (byte)(current | mask));
        }
        else
        {
            WriteRegionFlags(BlockWidth, BlockHeight, current => (byte)(current & ~mask));
        }
    }

    /// <summary>
    /// Ctrl+S, and the Z of the exit prompt. The clean guard is the save contract's heart:
    /// a session whose stack AND flags equal the disk writes <b>nothing</b> — open-and-close
    /// leaves every file untouched and, for a cart that never had them, uncreated; a repeated
    /// Ctrl+S is a no-op. A dirty save writes only the dirty banks, each independently
    /// (wave 3b-1): the sheet's two files (ADR-027 — the stack into gfx-layers.png, the
    /// flattened composite into gfx.png, layers <b>first</b> because it is the authoring
    /// source: if the disk dies between the two writes, a saved stack with a stale gfx.png
    /// reloads into the edited picture plus the out-of-sync notice, while the opposite order
    /// would resurrect the pre-edit layers and silently roll the work back) only if
    /// <see cref="IsLayersDirty"/>, and flags.bin only if <see cref="IsFlagsDirty"/> — a flag
    /// edit alone never rewrites gfx.png, and a sheet edit alone never creates flags.bin.
    /// Failures land in <see cref="SaveError"/> instead of throwing, because a full disk must
    /// leave the author their picture and a message, not a dead window.
    /// </summary>
    /// <returns>True when the disk now matches the stack and the flags (including "already did"), false when a write failed.</returns>
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
            if (IsLayersDirty)
            {
                var stacked = new byte[LayerCount * CartData.GfxWidth * CartData.GfxHeight];
                for (int i = 0; i < LayerCount; i++)
                {
                    _layers[i].CopyTo(stacked, i * _layers[i].Length);
                }
                File.WriteAllBytes(
                    _layersPath,
                    PngEncoder.EncodeFromPaletteIndices(stacked, CartData.GfxWidth, CartData.GfxHeight * LayerCount));
                File.WriteAllBytes(
                    _gfxPath,
                    PngEncoder.EncodeFromPaletteIndices(Pixels.ToArray(), CartData.GfxWidth, CartData.GfxHeight));
                _savedLayers = CloneStack(_layers);
                GfxOutOfSyncOnDisk = false;     // both files just came from this very stack
            }
            if (IsFlagsDirty)
            {
                // Its own file, independent of the sheet (wave 3b-1) — a flag-only save must
                // never touch gfx.png/gfx-layers.png, and a sheet-only save must never create
                // flags.bin: that is what keeps the pinned demo hashes untouched by this move.
                RequirePayload(_flags, FlagsPayloadSize, FlagsFileName);
                File.WriteAllBytes(_flagsPath, _flags);
                _savedFlags = (byte[])_flags.Clone();
            }
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

    /// <summary>The layer every tool reads and writes — the only shorthand, so no call site can grab a stale array across an undo swap.</summary>
    private byte[] ActiveSheet => _layers[ActiveLayerIndex];

    /// <summary>Deep copy of a whole stack — undo snapshots and the save baseline both need one (see the undo field comment for why sharing arrays would corrupt history).</summary>
    private static byte[][] CloneStack(byte[][] layers)
    {
        var copy = new byte[LayerCount][];
        for (int i = 0; i < LayerCount; i++)
        {
            copy[i] = (byte[])layers[i].Clone();
        }
        return copy;
    }

    /// <summary>One undo entry: a deep copy of the live stack AND the live flags (wave 3b-1) — nothing else may hold these arrays.</summary>
    private Snapshot TakeSnapshot() => new(CloneStack(_layers), (byte[])_flags.Clone());

    /// <summary>
    /// Sprite number of one cell of the marked block, from the region's anchor. Safe without a
    /// bounds check by the type comment's region invariant plus the block's own: the anchor is
    /// clamped to <c>GridCells - RegionCells</c> by its only two writers and the block is
    /// clamped to <c>GridCells - anchor</c> by <see cref="SelectRegionBlock"/>, so neither can
    /// hang off the sheet and <c>dx &lt; BlockWidth</c>, <c>dy &lt; BlockHeight</c> can never
    /// leave the bank.
    /// </summary>
    private int RegionSprite(int dx, int dy) => (RegionCellY + dy) * GridCells + RegionCellX + dx;

    /// <summary>
    /// The <b>one</b> writer of the flag bank, and the one pusher of a flag undo step — both
    /// <see cref="SetFlags"/> (a block of one cell) and <see cref="ToggleRegionFlag"/> (the
    /// whole marked block) come through here, so "one write door" survived the panel gaining a
    /// second public verb. Applies <paramref name="transform"/> to every sprite of the
    /// <paramref name="width"/> x <paramref name="height"/> block anchored at the region, as
    /// <b>one</b> operation: one <see cref="InterruptGesture"/>, one snapshot, one
    /// <see cref="Version"/> bump, one Ctrl+Z. A transform that changes no byte of the block is
    /// not a step and not dirt, exactly like a stroke that painted the color already at every
    /// pixel it touched — which is why the block is scanned before anything is written rather
    /// than after.
    ///
    /// <para>Two dimensions and no longer one square side: the free-rectangle wave made the
    /// marked block N×M (REFERENCES-EDITORS §8 item 3), and a door that still took one number
    /// would have had to pick which of the two to obey.</para>
    /// </summary>
    private void WriteRegionFlags(int width, int height, Func<byte, byte> transform)
    {
        InterruptGesture();
        bool changed = false;
        for (int dy = 0; dy < height && !changed; dy++)
        {
            for (int dx = 0; dx < width && !changed; dx++)
            {
                byte current = _flags[RegionSprite(dx, dy)];
                changed = transform(current) != current;
            }
        }
        if (!changed)
        {
            return;
        }
        _undo.Add(TakeSnapshot());
        _redo.Clear();
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                int sprite = RegionSprite(dx, dy);
                _flags[sprite] = transform(_flags[sprite]);
            }
        }
        Version++;
    }

    private static void ValidateBit(int bit)
    {
        if (bit is < 0 or >= FlagBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bit), bit, $"a sprite has flags 0-{FlagBits - 1} (SPEC-8 §6).");
        }
    }

    /// <summary>Absent flags.bin = zeros (SPEC-8 §6); present file = its bytes, length-checked below.</summary>
    private static byte[] ReadFlagsPayload(string path)
    {
        if (!File.Exists(path))
        {
            return new byte[FlagsPayloadSize];
        }
        byte[] bytes = File.ReadAllBytes(path);
        RequirePayload(bytes, FlagsPayloadSize, FlagsFileName);
        return bytes;
    }

    /// <summary>
    /// The only thing the flat flags payload can be wrong about (SPEC-8 §6: one byte per
    /// sprite, no illegal values) — one owner for both directions, load and save, exactly the
    /// check <see cref="MapEditorSession"/> ran for the same file before this bank moved, so
    /// the wording the author sees has not changed along with the ownership.
    /// </summary>
    private static void RequirePayload(byte[] payload, int expectedLength, string name)
    {
        if (payload.Length != expectedLength)
        {
            throw new CartLoadException($"{name}: {payload.Length} bytes, must be exactly {expectedLength}.");
        }
    }

    /// <summary>
    /// The flatten formula's one owner (ADR-027): start from the base layer, let every higher
    /// layer's non-zero pixels cover — color 0 is transparent in the stack, exactly the
    /// meaning Palt gives it at runtime. The live composite cache and the constructor's
    /// out-of-sync check both call here, so they cannot disagree about what "flattened" means.
    /// </summary>
    private static void CompositeInto(byte[][] layers, byte[] destination)
    {
        layers[0].CopyTo(destination, 0);
        for (int i = 1; i < LayerCount; i++)
        {
            byte[] layer = layers[i];
            for (int p = 0; p < layer.Length; p++)
            {
                if (layer[p] != 0)
                {
                    destination[p] = layer[p];
                }
            }
        }
    }

    private int SheetOffset(int localX, int localY)
    {
        int size = VirtualConsole.SpriteSize;
        return (RegionCellY * size + localY) * CartData.GfxWidth + RegionCellX * size + localX;
    }

    /// <summary>
    /// The single write into the live stack. Both inks are 0-15 by the invariant above, so the
    /// cast cannot truncate whichever <see cref="InkColor"/> hands back; writing the value
    /// already there is skipped so that a stroke which changes nothing stays invisible to undo
    /// and dirt. It writes ONE pixel and knows nothing of <see cref="BrushSize"/> — the brush
    /// lives one level up, in <see cref="Dab"/>, which is why the shapes and the selection
    /// (which call here, or its line, directly) are untouched by it.
    /// </summary>
    private void Plot(int localX, int localY)
    {
        int offset = SheetOffset(localX, localY);
        int color = InkColor(_strokeInk);
        if (ActiveSheet[offset] == color)
        {
            return;
        }
        ActiveSheet[offset] = (byte)color;
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
