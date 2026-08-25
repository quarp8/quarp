using Microsoft.Xna.Framework;
using Quarp.Core;
using static Quarp.Shell.Desktop.ConsoleChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the sprite editor <b>into the console's own framebuffer</b> (wave R2, ADR-029): the
/// top band with the exit button, the tooltip field and the five editor tabs; the two-wide tool
/// column; the 64x64 canvas with its keyboard cursor, shape preview, marching-ant selection and
/// stamp ghost; the middle column's 4x4 palette, eight flag toggles and five layer tabs; the
/// sheet window with its scroll slider; the status line and the one message line.
///
/// <para><b>What this file used to be.</b> Until this wave it owned a
/// <c>GraphicsDevice</c>, a 128x128 <c>Texture2D</c> of the sheet, a font atlas and an icon
/// atlas, and painted at the window's native resolution through a <c>SpriteBatch</c>. All of
/// that is gone. Every pixel now goes through <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>,
/// <c>Print</c> and <c>Pset</c> on a <see cref="ShellScreen"/> — the same calls a cartridge
/// makes — and the result is presented by the same <see cref="ConsolePresenter"/> the
/// cartridge's frame goes through. The class is static for the same reason
/// <see cref="LibraryRenderer"/> is: with no device resource to own there is nothing to
/// construct and nothing to dispose.</para>
///
/// <para><b>The gain is not cosmetic.</b> A screen drawn into a framebuffer can be hashed by
/// <see cref="FrameHash"/>, exactly as a cartridge's frame is, and that is what
/// <c>SpriteEditorScreenGoldenTests</c> does. Layout regressions on this screen — a panel off by
/// a pixel, a swatch on the wrong row, a status field running off the edge — were previously
/// undetectable by every test in this solution: there was no buffer to look at, only draw calls
/// into a device no headless runner has.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
///
/// <para><b>Cost, measured rather than waved away.</b> The sheet window is 56x64 plotted pixels,
/// the canvas another 64x64, so a full frame is on the order of 8000 plots against the 14400 the
/// <c>Cls</c> on the same frame writes. This is drawing, not simulation: it happens once per
/// rendered frame, never inside a tick, and no rewind ever replays it.</para>
/// </summary>
public static class SpriteEditorRenderer
{
    /// <summary>What the tooltip field says when no control is hovered — TIC-80's <c>Names[mode]</c>.</summary>
    public const string ScreenName = "SPRITES";

    /// <summary>The layout this screen is drawn with; the router asks for the same one, so picture and clicks cannot disagree.</summary>
    public static SpriteEditorLayout LayoutFor(ShellScreen screen, SpriteEditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(editor);
        return SpriteEditorLayout.Compute(screen.Width, screen.Height, editor.RegionCells);
    }

    /// <summary>
    /// One frame of the editor. Owns the whole surface: it resets the console's drawing state
    /// and clears, so nothing another screen left behind can bend these pixels.
    /// <paramref name="hover"/> and <paramref name="tooltipVisible"/> come from the shell's
    /// <see cref="IconHoverTracker"/> — the hovered control's frame lights up immediately, the
    /// text label only after the tracker's three seconds, and the label lands in the top band
    /// rather than under the pointer (<see cref="ConsoleChrome.TooltipChars"/> explains why).
    /// <paramref name="flyoutSlot"/> is the shell's <see cref="ToolbarFlyout.OpenSlot"/>: the
    /// flyout draws late so it floats over the canvas. <paramref name="scroll"/> is the shell's
    /// <see cref="SheetScroll"/> — the window's slice and the slider's thumb both come from the
    /// very offset the hit tests use. <paramref name="timeSeconds"/> is the shell's draw clock,
    /// consumed only by the marching ants' phase.
    /// </summary>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static SpriteEditorLayout Draw(
        ShellScreen screen, SpriteEditorSession editor, HoverTarget? hover, bool tooltipVisible,
        EditorButton? flyoutSlot, SheetScroll scroll, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        SpriteEditorLayout layout = LayoutFor(screen, editor);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        DrawBands(console, layout.Chrome);
        DrawPanelFrames(console, layout);
        DrawCanvas(console, layout, editor, timeSeconds);
        DrawButtons(console, layout, editor, hover);
        DrawSwatches(console, layout, editor);
        DrawFlags(console, layout, editor, hover);
        DrawSheet(console, layout, editor, scroll.Offset);
        DrawSlider(console, layout, scroll, hover);
        // The readouts: the cursor in SHEET pixels — the coordinate an author would type into
        // code — and the sprite number, which is Spr(n)'s n for the region's anchor cell.
        DrawStatusText(console, layout.Chrome, SheetCoordinates(editor), $"#{editor.SpriteIndex:D3}");
        DrawMessageLine(
            console, layout.Chrome, editor.ExitPromptShown, editor.SaveError, StandingNotice(editor));
        DrawFlyout(console, layout, editor, flyoutSlot, hover);
        DrawTooltipField(
            console, layout.Chrome,
            tooltipVisible && hover is HoverTarget target ? TooltipText(editor, target) : null,
            ScreenName);
        return layout;
    }

    /// <summary>
    /// The borders of the two panels that hold pictures — the canvas and the sheet window.
    /// They are drawn <b>second</b>, right after the three band rules and before anything else,
    /// for one reason: every pixel they touch is either free ground or a pixel some neighbour
    /// owns, and the neighbour must win. The tool column's buttons repaint the canvas's left
    /// side, the layer tabs repaint the lower half of its right side and the sheet's left side,
    /// the slider repaints the sheet's bottom. Drawing the frames late would have this file
    /// scribbling grey over a hovered button's bright border; drawing them first makes the
    /// frame the <em>ground</em> those controls stand on, which is what it is.
    ///
    /// <para>Both rings sit entirely OUTSIDE their panel — TIC-80's <c>x - 1, y - 1, w + 2,
    /// h + 2</c> (<c>sprite.c</c>, <c>drawCanvas</c> and <c>drawSheetVBank1</c>) — so not one
    /// pixel of the author's drawing is covered by them, which is rule 3 of the order of
    /// 2026-08-25. <see cref="SpriteEditorLayout.CanvasFrame"/> carries the argument about where
    /// the pixels came from; <see cref="SpriteEditorLayout.SheetFrame"/> the one about the side
    /// that falls off the screen.</para>
    /// </summary>
    private static void DrawPanelFrames(VirtualConsole console, in SpriteEditorLayout layout)
    {
        Outline(console, layout.CanvasFrame, Dim);
        Outline(console, layout.SheetFrame, Dim);
    }

    /// <summary>The status line's left half, in <b>sheet</b> pixels — the coordinate an author would type into code, not a screen position.</summary>
    private static string SheetCoordinates(SpriteEditorSession editor)
    {
        int size = VirtualConsole.SpriteSize;
        return $"{editor.RegionCellX * size + editor.CursorX:D3},{editor.RegionCellY * size + editor.CursorY:D3}";
    }

    /// <summary>
    /// The screen's standing line, re-cut for forty columns. Two things can stand here and the
    /// out-of-sync warning (ADR-027) wins, because it changes what saving does: gfx.png was
    /// edited outside while gfx-layers.png stood — the stack wins and the next save will
    /// overwrite it, which must be announced and not sprung.
    ///
    /// <para>The second is the trap this editor could otherwise spring in total silence: a map
    /// cell holding 0 means "empty" and the console skips it, so art drawn on sprite 0 can never
    /// appear on a map. Sprite 0 is a perfectly good sprite for <c>Spr</c>, so this is a notice
    /// and not a block; the map editor's picker carries the matching marker on the same tile.</para>
    ///
    /// <para>Both texts are shorter than the host screen's: the console line holds 39
    /// characters and the old ones ran to 57. Cutting them here, at the one place that knows
    /// what they say, beats truncating them at the one place that knows how wide the line is —
    /// a truncated sentence ends mid-word.</para>
    /// </summary>
    public static string? StandingNotice(SpriteEditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        // The clipboard's refusal wins — see SfxEditorRenderer.StandingNotice for why the
        // transient answer to a keystroke outranks the two standing facts about the folder.
        return editor.ClipboardNotice
            ?? (editor.GfxOutOfSyncOnDisk
                ? "GFX.PNG EDITED OUTSIDE - SAVING WINS"
                : editor.SpriteIndex == 0
                    ? "SPR 000 IS THE MAP'S EMPTY TILE"
                    : null);
    }

    /// <summary>
    /// The hover label for whichever kind of target is under the pointer. Five kinds (button,
    /// flyout variant, slider, flag toggle, swatch), each naming its own text; the cut to the
    /// field's width belongs to <see cref="ConsoleChrome.FitTooltip"/>, which is the only thing
    /// that knows how wide the field is.
    /// </summary>
    public static string TooltipText(SpriteEditorSession editor, in HoverTarget target)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return target.Button is EditorButton button ? EditorIcons.Tooltip(button, editor)
            : target.FlyoutSlot is EditorButton slot ? EditorIcons.VariantTooltip(slot, target.FlyoutVariant)
            : target.Slider ? EditorIcons.SliderTooltip
            : target.Flag >= 0 ? EditorIcons.FlagTooltip(target.Flag)
            : EditorIcons.SwatchTooltip(target.Swatch);
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="ConsoleChromeRenderer.DrawButton"/>
    /// owns. What only this screen can answer is decided here: which buttons read as active (the
    /// sprites tab, the tool in hand, the layer tab you are on), which face a group slot wears —
    /// its CURRENT variant's glyph, with the corner marker over it — and which buttons are
    /// text-faced at all (<see cref="EditorIcons.Face"/> owns that choice).
    /// </summary>
    private static void DrawButtons(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            bool active = place.Id == EditorButton.SpritesTab
                || (place.Id == EditorButton.ToolSelect && editor.Tool == SpriteEditorTool.Select)
                || (place.Id == EditorButton.ToolPencil && editor.Tool == SpriteEditorTool.Pencil)
                || (place.Id == EditorButton.ToolFill && editor.Tool == SpriteEditorTool.Fill)
                || (place.Id == EditorButton.ToolStamp && editor.Tool == SpriteEditorTool.Stamp)
                || (place.Id == EditorButton.ToolShape && editor.Tool == SpriteEditorTool.Shape)
                || (place.Id >= EditorButton.LayerTab1 && place.Id <= EditorButton.LayerTab5
                    && editor.ActiveLayerIndex == place.Id - EditorButton.LayerTab1);
            var state = new EditorButtonState(
                Active: active,
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: editor.IsDirty,
                CanUndo: editor.CanUndo,
                CanRedo: editor.CanRedo);
            (string? text, EditorIcon? icon) = EditorIcons.Face(place.Id, editor);
            byte color = DrawButton(console, place, state, icon, text);
            if (EditorIcons.IsGroupSlot(place.Id))
            {
                DrawGroupMarker(console, place.Rect, color);
            }
        }
    }

    /// <summary>
    /// The corner marker of a group slot: a three-pixel stepped triangle inside the button's
    /// bottom-right corner, drawn over whatever variant icon the slot wears. Same ink as the
    /// icon, so a dim state dims the cue with it. At console scale the host's ui-sized quads
    /// become single pixels, which is what the cue always was at scale 1.
    /// </summary>
    private static void DrawGroupMarker(VirtualConsole console, Rectangle slot, byte color)
    {
        for (int step = 0; step < 3; step++)
        {
            console.RectFill(
                slot.Right - 2 - step, slot.Bottom - 2 - step, 1, step + 1, color);
        }
    }

    /// <summary>
    /// The open group flyout: the slot's variants as ordinary buttons floating right of it, the
    /// remembered variant on the active-blue fill. Drawn on ink plates because the row overlaps
    /// the canvas — a variant icon over sheet pixels would be unreadable. Two slots wear text
    /// rather than glyphs — the sprite size (8/16/32) and the brush size (1-4), the same faces
    /// their slots wear — and which ones those are is <see cref="EditorIcons.VariantText"/>'s
    /// answer, not a name this file matches on: the brush toggle arrived as a second text-faced
    /// slot and a hard-coded <c>slot == SizeToggle</c> here would have asked
    /// <see cref="EditorIcons.VariantIcon"/> for a glyph that throws.
    /// </summary>
    private static void DrawFlyout(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor,
        EditorButton? flyoutSlot, HoverTarget? hover)
    {
        if (flyoutSlot is not EditorButton slot)
        {
            return;
        }
        int current = EditorIcons.CurrentVariant(editor, slot);
        for (int i = 0; i < EditorIcons.GroupVariantCount(slot); i++)
        {
            Rectangle rect = layout.FlyoutVariantRect(slot, i);
            bool hovered = hover is HoverTarget target && target.FlyoutSlot == slot && target.FlyoutVariant == i;
            console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, i == current ? ActiveBg : Ink);
            byte ink = i == current ? Bright : Text;
            if (EditorIcons.VariantText(slot, i) is string label)
            {
                console.Print(
                    label, ConsoleChrome.ButtonTextX(rect, label), ConsoleChrome.ButtonTextY(rect), ink);
            }
            else
            {
                Rectangle destination = ConsoleChrome.ButtonIconRect(rect);
                ConsoleIcons.Draw(console, EditorIcons.VariantIcon(slot, i), destination.X, destination.Y, ink);
            }
            console.Rect(rect.X, rect.Y, rect.Width, rect.Height, hovered ? Bright : Dim);
        }
    }

    /// <summary>
    /// The canvas: the region's pixels, each one a <see cref="SpriteEditorLayout.CanvasScale"/>
    /// square. There is no second pixel store and no texture — the zoom IS the draw, one filled
    /// rectangle per region pixel, straight out of the session's composite (ADR-027: what the
    /// author sees is the flattened stack).
    /// </summary>
    private static void DrawCanvas(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)
    {
        int size = VirtualConsole.SpriteSize;
        int sheetX0 = editor.RegionCellX * size;
        int sheetY0 = editor.RegionCellY * size;
        ReadOnlySpan<byte> pixels = editor.Pixels;
        for (int y = 0; y < layout.RegionPixels; y++)
        {
            for (int x = 0; x < layout.RegionPixels; x++)
            {
                byte color = pixels[(sheetY0 + y) * VirtualConsole.SheetWidth + sheetX0 + x];
                Fill(console, PixelRect(layout, x, y), color);
            }
        }

        // The shape preview, straight from the session's point list — the very pixels the commit
        // will plot, in the colour they will get. The preview never enters the sheet, so
        // cancelling a gesture costs nothing and dirt and undo stay untouched until the release.
        if (editor.ShapeActive)
        {
            foreach ((int px, int py) in editor.ShapePreview)
            {
                // The gesture's OWN ink, not the left button's: a shape dragged with the right
                // button commits in the second colour, and a preview in the first would be a
                // promise the commit breaks.
                Fill(console, PixelRect(layout, px, py), (byte)editor.InkColor(editor.ShapeInk));
            }
        }

        DrawSelection(console, layout, editor, timeSeconds);
        DrawStampGhost(console, layout, editor);

        // The canvas cursor — where the keyboard pencil is and what the status line's
        // coordinates read. A frame around the pixel, not over it: the colour being placed must
        // stay visible under the cursor.
        Rectangle cursor = PixelRect(layout, editor.CursorX, editor.CursorY);
        console.Rect(cursor.X, cursor.Y, cursor.Width, cursor.Height, Bright);
    }

    /// <summary>One region pixel's square on the canvas — the single mapping the shape preview, the selection, the ghost and the cursor all share.</summary>
    private static Rectangle PixelRect(in SpriteEditorLayout layout, int x, int y) =>
        new(layout.Canvas.X + x * layout.CanvasScale,
            layout.Canvas.Y + y * layout.CanvasScale,
            layout.CanvasScale, layout.CanvasScale);

    /// <summary>
    /// The selection made visible — marching ants on the mask's true boundary, rebuilt every
    /// frame with the phase <see cref="SelectionOutline"/> derives from the draw clock. Drawn
    /// only under the select tool. While a move floats, the picture splits honestly into what
    /// the drop would produce: the holes show colour 0, the lifted pixels ride at the offset and
    /// the ants ride with them. All of it is overlay; none of it is in the sheet, which is why
    /// cancelling a move costs nothing and why no outline can ever reach a saved PNG.
    /// </summary>
    private static void DrawSelection(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)
    {
        if (editor.Tool != SpriteEditorTool.Select || !editor.HasSelection)
        {
            return;
        }
        int n = layout.RegionPixels;
        int dx = editor.MoveActive ? editor.MoveOffsetX : 0;
        int dy = editor.MoveActive ? editor.MoveOffsetY : 0;
        // Two passes while moving: every hole first, then every lifted pixel — a fragment pixel
        // may land on another's hole, and landings must win, exactly as the drop writes.
        if (editor.MoveActive)
        {
            int sheetX0 = editor.RegionCellX * VirtualConsole.SpriteSize;
            int sheetY0 = editor.RegionCellY * VirtualConsole.SpriteSize;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (editor.IsSelected(x, y))
                    {
                        Fill(console, PixelRect(layout, x, y), 0);
                    }
                }
            }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (editor.IsSelected(x, y))
                    {
                        byte color = editor.Pixels[(sheetY0 + y) * VirtualConsole.SheetWidth + sheetX0 + x];
                        Fill(console, PixelRect(layout, x + dx, y + dy), color);
                    }
                }
            }
        }
        // The ants outline the mask as shown — shifted while floating, which the predicate's one
        // subtraction handles (out-of-range asks answer false, so borders stay honest). The dash
        // is half a canvas pixel at the console's scale, floored at two so it can still alternate.
        int dashLength = Math.Max(2, layout.CanvasScale / 2);
        var ants = new List<AntDash>();
        SelectionOutline.Collect(
            (x, y) => editor.IsSelected(x - dx, y - dy), n, layout.CanvasScale, dashLength, 1,
            SelectionOutline.Phase(timeSeconds, dashLength), ants);
        foreach (AntDash dash in ants)
        {
            console.RectFill(
                layout.Canvas.X + dash.X, layout.Canvas.Y + dash.Y, dash.Width, dash.Height,
                dash.Bright ? Bright : Ink);
        }
    }

    /// <summary>
    /// The stamp's ghost: the source under the cursor, placed by the very
    /// <see cref="SpriteEditorSession.StampOrigin"/> the print uses and clipped at the region the
    /// way the print will be. The host screen drew it at half alpha; an indexed framebuffer has
    /// no alpha, so the ghost is drawn as a checker — every other pixel — which is the same
    /// "this is not committed yet" reading with the palette we actually have.
    /// </summary>
    private static void DrawStampGhost(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        if (editor.Tool != SpriteEditorTool.Stamp || !editor.HasStampSource)
        {
            return;
        }
        (int destX, int destY) = editor.StampOrigin(editor.CursorX, editor.CursorY);
        for (int sy = 0; sy < editor.StampHeight; sy++)
        {
            for (int sx = 0; sx < editor.StampWidth; sx++)
            {
                byte color = editor.StampPixelAt(sx, sy);
                int x = destX + sx;
                int y = destY + sy;
                if (color == 0 || x < 0 || x >= layout.RegionPixels || y < 0 || y >= layout.RegionPixels)
                {
                    continue;   // transparent source, or the part the border will clip off the print
                }
                Rectangle rect = PixelRect(layout, x, y);
                for (int py = 0; py < rect.Height; py++)
                {
                    for (int px = (py + x + y) % 2; px < rect.Width; px += 2)
                    {
                        console.Pset(rect.X + px, rect.Y + py, color);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The sixteen swatches as a 4x4 grid — PICO-8's own palette shape, and the only one this
    /// column's twenty pixels can hold. The current colour wears a ring <b>inside</b> its own
    /// cell, leaving a 2x2 body of the colour showing: the host screen ringed the gap around
    /// the cell, and here the gap is one pixel wide and belongs to the canvas on one side and
    /// to the sheet window on the other, so a ring drawn out there would scribble on a
    /// neighbour.
    ///
    /// <para>The ring is white except on the white swatch, where it is ink. That one exception
    /// is not decoration: a white ring on a white body is a solid white square, and "which
    /// colour am I holding" would then have no answer at all for exactly one of the sixteen.
    /// The signal stays a shape either way, never a hue, so it survives an author who cannot
    /// separate blue from grey.</para>
    ///
    /// <para><b>Two marks, because there are two inks.</b> TIC-80 frames <c>color</c> and
    /// <c>color2</c> differently in <c>drawPalette</c> (REFERENCES-EDITORS §8 item 7); here the
    /// left ink keeps the ring and the right ink is a single pixel <em>inside</em> the cell, at
    /// the bottom right of the 2x2 body. Inside and not on the border for one reason worth
    /// spelling out: the two inks are allowed to be the same colour, and a second border mark
    /// would then be swallowed whole by the ring — the swatch would say "one ink is here" while
    /// two are. A ring plus an inner dot reads as both, a ring alone as the left one, a bare dot
    /// as the right one; four pixels of body is the most this column can spend and it is enough
    /// for three distinguishable states.</para>
    /// </summary>
    private static void DrawSwatches(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor)
    {
        for (int i = 0; i < Palette.VisibleCount; i++)
        {
            Rectangle rect = layout.SwatchRect(i);
            Fill(console, rect, (byte)i);
            byte mark = i == Bright ? Ink : Bright;
            if (i == editor.CurrentColor)
            {
                console.Rect(rect.X, rect.Y, rect.Width, rect.Height, mark);
            }
            if (i == editor.SecondaryColor)
            {
                console.Pset(rect.Right - 2, rect.Bottom - 2, mark);
            }
        }
    }

    /// <summary>
    /// The eight flag toggles, bit 0 at the top left, four columns by two. Three states, the same
    /// three the reference row carries (REFERENCES-EDITORS §2.1, TIC-80's <c>drawFlags</c>):
    /// raised on every sprite of the region — a solid white cell; raised on some of them — a
    /// solid grey one; raised on none — a hollow dim ring. At region 8 px "every" and "some" are
    /// the same sprite, so grey never appears; at 16 or 32 it is the only honest thing to draw,
    /// because one four-pixel square cannot say "half of these four". Fill and emptiness carry
    /// the signal, not hue.
    ///
    /// <para>The host screen painted a smaller mark inside a bigger cell so the toggle could
    /// look like TIC-80's 5x5 square while staying easy to hit. Here the cell IS four pixels, so
    /// the mark and the hit target are the same thing and the pointer's reach comes from the
    /// console's scale instead — four console pixels are thirty-two window pixels at the shell's
    /// default window.</para>
    /// </summary>
    private static void DrawFlags(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)
    {
        for (int bit = 0; bit < SpriteEditorSession.FlagBits; bit++)
        {
            Rectangle cell = layout.FlagRect(bit);
            if (editor.IsFlagSetInAll(bit))
            {
                Fill(console, cell, Bright);
            }
            else if (editor.IsFlagSetInAny(bit))
            {
                Fill(console, cell, Text);
            }
            else
            {
                console.Rect(cell.X, cell.Y, cell.Width, cell.Height, Dim);
            }
            if (hover is HoverTarget target && target.Flag == bit)
            {
                console.Rect(cell.X, cell.Y, cell.Width, cell.Height, Bright);
            }
        }
    }

    /// <summary>
    /// The sheet window: the presentation strip's pixels, sliced by the scroll offset. Drawn
    /// pixel by pixel through <see cref="SheetStrip"/>'s own mapping rather than by lane blocks,
    /// because the offset is in strip pixels and a slider drag lands on any of them — a
    /// block-wise draw would have to re-derive the lane arithmetic to clip, which is precisely
    /// the second owner the strip type exists to prevent.
    ///
    /// <para>Three marks go on top of those pixels, in this order and for these reasons. An
    /// <b>empty</b> cell gets a dim frame (<see cref="DrawEmptyCellMarks"/>) so the window reads
    /// as cells and not as one black slab — nothing is covered, because an empty cell has
    /// nothing to cover. <b>Sprite 0</b> wears a dim frame wherever the window shows it: it is
    /// the map's empty tile, and the author is told so here — where he draws — and not only
    /// after carrying the art to the map screen and finding it missing. The <b>selected
    /// region</b> wears a bright one, last, so it wins over both.</para>
    /// </summary>
    private static void DrawSheet(
        VirtualConsole console, in SpriteEditorLayout layout, SpriteEditorSession editor, int scroll)
    {
        ReadOnlySpan<byte> pixels = editor.Pixels;
        int size = VirtualConsole.SpriteSize;
        for (int row = 0; row < layout.Sheet.Height / layout.SheetScale; row++)
        {
            for (int column = 0; column < layout.SheetVisiblePixels; column++)
            {
                int stripX = scroll + column;
                if (!SheetStrip.TryStripCellToSheetCell(
                    stripX / size, row / size, out int cellX, out int cellY))
                {
                    continue;
                }
                byte color = pixels[
                    (cellY * size + row % size) * VirtualConsole.SheetWidth + cellX * size + stripX % size];
                console.RectFill(
                    layout.Sheet.X + column * layout.SheetScale,
                    layout.Sheet.Y + row * layout.SheetScale,
                    layout.SheetScale, layout.SheetScale, color);
            }
        }

        DrawEmptyCellMarks(console, layout, pixels, scroll);

        foreach (Rectangle zero in layout.SheetRegionHighlights(0, 0, 1, scroll))
        {
            Outline(console, Rectangle.Intersect(zero, layout.Sheet), Dim);
        }
        foreach (Rectangle highlight in layout.SheetRegionHighlights(
            editor.RegionCellX, editor.RegionCellY, editor.RegionCells, scroll))
        {
            Outline(console, Rectangle.Intersect(highlight, layout.Sheet), Bright);
        }
    }

    /// <summary>
    /// A dim frame inside every visible sheet cell that holds nothing at all — the second half
    /// of the defect of 2026-08-25. A frame around the window says where the sheet is; it says
    /// nothing about where sprite 47 is, and on a fresh cart all 256 cells are colour 0, so the
    /// window was one flat black slab and "select the forty-seventh sprite" meant counting
    /// blind.
    ///
    /// <para><b>This is a named divergence, and it is the only one in this change.</b> None of
    /// the three references rules its sprite sheet: TIC-80 draws the 128x128 sheet as one
    /// <c>tic_api_spr</c> call with a frame around the whole of it (§2.1), LIKO-12 the same for
    /// its bank strip (§2.2), PICO-8 the same for its navigator (§2.3) — all three simply live
    /// with a black slab when the sheet is empty, which they can afford because a cell there is
    /// 8 px on a 128 px sheet the author sees whole, while ours is 8 px in a window that shows
    /// seven columns of a scrolling strip. A grid over the cells is what would match the eye's
    /// need and it is exactly what rule 3 forbids: a line on a cell boundary eats one row of
    /// every eight of somebody's art. So the mark is drawn <b>only where there is no art to
    /// cover</b> — a cell whose sixty-four pixels are all colour 0 — and it disappears the
    /// instant the author puts a pixel in that cell. What the reference does have is the same
    /// idea applied to a different grid: TIC-80's SFX selector paints empty slots differently
    /// from full ones (<c>sfx.c</c>, <c>drawSelector</c>: "пустые сэмплы окрашены темнее",
    /// §5.1), which is the precedent this borrows — an empty slot is allowed to look empty.</para>
    ///
    /// <para>The mark is <see cref="ConsoleChromeRenderer.Dim"/>, the same grey as the panel
    /// frames and as sprite 0's marker below, so an empty sheet reads as a lattice of ground and
    /// never competes with the bright frame the selected region wears. Sprite 0's dim marker is
    /// drawn after this and over it: on an empty sprite 0 the two coincide exactly, which loses
    /// nothing — the standing notice on the message line is what carries that fact in words —
    /// and the moment sprite 0 has art its marker is the only dim frame among its neighbours
    /// again.</para>
    ///
    /// <para>Cells are clipped to the window rather than skipped, the way sprite 0's marker and
    /// the region highlight already are. That is safe here precisely because the cell is empty:
    /// the clipped side of a half-visible cell draws a line down the window's own edge and there
    /// is no art under it to lose.</para>
    /// </summary>
    private static void DrawEmptyCellMarks(
        VirtualConsole console, in SpriteEditorLayout layout, ReadOnlySpan<byte> pixels, int scroll)
    {
        int size = VirtualConsole.SpriteSize;
        int cell = size * layout.SheetScale;
        int firstColumn = scroll / size;
        int lastColumn = (scroll + layout.SheetVisiblePixels - 1) / size;
        for (int stripRow = 0; stripRow < SheetStrip.Rows; stripRow++)
        {
            for (int stripColumn = firstColumn; stripColumn <= lastColumn; stripColumn++)
            {
                if (!SheetStrip.TryStripCellToSheetCell(
                        stripColumn, stripRow, out int cellX, out int cellY)
                    || !IsCellEmpty(pixels, cellX, cellY))
                {
                    continue;
                }
                var rect = new Rectangle(
                    layout.Sheet.X + (stripColumn * size - scroll) * layout.SheetScale,
                    layout.Sheet.Y + stripRow * cell,
                    cell,
                    cell);
                Outline(console, Rectangle.Intersect(rect, layout.Sheet), Dim);
            }
        }
    }

    /// <summary>
    /// Whether one canonical sheet cell holds nothing but colour 0 — read off the very bytes the
    /// window has just plotted (the session's flattened composite, ADR-027), so the mark can
    /// never disagree with the picture under it. Sixty-four byte reads with an early exit, for
    /// at most the fifty-six cells the window shows: the same order of cost as the pixel loop
    /// above it, which plots 56x64 rectangles.
    /// </summary>
    private static bool IsCellEmpty(ReadOnlySpan<byte> pixels, int cellX, int cellY)
    {
        int size = VirtualConsole.SpriteSize;
        for (int y = 0; y < size; y++)
        {
            int row = (cellY * size + y) * VirtualConsole.SheetWidth + cellX * size;
            for (int x = 0; x < size; x++)
            {
                if (pixels[row + x] != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// The sheet window's horizontal scroll slider: the track as a dim outline, the thumb from
    /// the very <see cref="SpriteEditorLayout.SheetThumb"/> the drag inverts. It brightens under
    /// the pointer and while dragging, like every hovered control. The window shows seven of the
    /// strip's thirty-two columns, so the thumb is always live here — unlike the host screen,
    /// where a wide enough window could show the whole strip and the drag became a no-op.
    /// </summary>
    private static void DrawSlider(
        VirtualConsole console, in SpriteEditorLayout layout, SheetScroll scroll, HoverTarget? hover)
    {
        console.Rect(
            layout.SheetSlider.X, layout.SheetSlider.Y,
            layout.SheetSlider.Width, layout.SheetSlider.Height, Dim);
        bool hot = scroll.Dragging || (hover is HoverTarget target && target.Slider);
        Fill(console, layout.SheetThumb(scroll.Offset), hot ? Bright : Text);
    }

    /// <summary>One filled rectangle, a layout rectangle unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);

    /// <summary>One outline, skipped when the clipped rectangle came back empty.</summary>
    private static void Outline(VirtualConsole console, Rectangle rect, byte color)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            console.Rect(rect.X, rect.Y, rect.Width, rect.Height, color);
        }
    }
}
