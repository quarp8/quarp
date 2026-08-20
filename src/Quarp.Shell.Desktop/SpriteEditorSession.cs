using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The sprite editor's whole state and policy, with no window attached (M9 stage 2, wave 2b) —
/// the same split that made <see cref="ShellModeMachine"/> testable: <c>QuarpGame</c> routes
/// keys and mouse hits here, <see cref="SpriteEditorRenderer"/> paints what this says, and
/// every claim the work order makes (one stroke = one undo step, a clean session never touches
/// the disk, nothing above palette index 15 can enter the sheet) is provable headless.
///
/// <para><b>The 0-15 invariant has exactly three doors.</b> Pixels enter the sheet through
/// (1) the load in the constructor — <see cref="PngDecoder.DecodeToPaletteIndices"/> only ever
/// emits matches against the 16 visible palette colors; (2) the pencil — which writes
/// <see cref="CurrentColor"/>, and <see cref="SelectColor"/> throws on anything outside 0-15
/// while <see cref="PickColor"/> copies a value already in the sheet; (3) undo/redo — which
/// swap whole arrays that were themselves sheets. There is no fourth setter, so the byte cast
/// in the plot routine can never truncate. <see cref="PngEncoder"/> re-checks on save as the
/// owner of its own input contract; that check is unreachable from here by construction.</para>
///
/// <para><b>Dirty is content, not history.</b> <see cref="IsDirty"/> compares the live sheet
/// against a snapshot of what the disk holds (or held nothing — an all-zero sheet), because
/// the save contract is about bytes: undoing back to the loaded picture makes the session
/// clean again, and even hand-repainting a pixel to its old color counts, since saving then
/// would change nothing. A 16 KB compare per query costs microseconds and cannot drift out of
/// sync the way a depth counter under an undo/redo/new-stroke braid can.</para>
///
/// <para><b>The region is a concept from day one</b> (work order: wave 2c grows it to 16x16
/// and 32x32 without rebuilding the canvas): the editable area is <see cref="RegionCells"/>
/// sprite cells on a side, anchored at a cell the sheet grid selects, and every pixel
/// coordinate the shell hands in is region-local. Wave 2b pins the size at one cell; the
/// clamps and the canvas already speak in cells.</para>
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

    /// <summary>Region anchor, in sheet cells 0-15.</summary>
    public int RegionCellX { get; private set; }

    /// <summary>Region anchor, in sheet cells 0-15.</summary>
    public int RegionCellY { get; private set; }

    /// <summary>Region side in sprite cells. Pinned to 1 this wave; wave 2c adds the 2/4 steps on top of the same plumbing.</summary>
    public int RegionCells { get; } = 1;

    /// <summary>Region side in pixels — what canvas-local coordinates are validated against.</summary>
    public int RegionPixels => RegionCells * VirtualConsole.SpriteSize;

    /// <summary>Sprite number of the region's anchor cell — the "#NNN" the header shows, same numbering Spr(n) uses.</summary>
    public int SpriteIndex => RegionCellY * GridCells + RegionCellX;

    /// <summary>True while a pencil stroke is open (button held). The shell checks this before feeding drag positions.</summary>
    public bool StrokeActive => _strokeBackup is not null;

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
    /// Ctrl+Z. Ends an open stroke first (committing it), so an undo mid-drag rolls back a
    /// whole gesture instead of tearing one in half. Whole-array swaps, no copying: the arrays
    /// already exist and nothing else holds them.
    /// </summary>
    public void Undo()
    {
        EndStroke();
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
        EndStroke();
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
        EndStroke();
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
        EndStroke();    // Esc mid-drag: the gesture commits, then the prompt judges the session as it stands.
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
