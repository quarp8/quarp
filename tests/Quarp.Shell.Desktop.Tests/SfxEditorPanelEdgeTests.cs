using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The tests that were missing on the SOUND screen, which is why the defect reached the
/// owner's eyes.</b> He opened the demo cart's sound screen and reported the right half — x 65
/// to 159, y 12 to 55, the largest panel this screen has — as empty: one yellow pixel and two
/// grey rails. Nothing was broken. An empty slot draws nothing, nothing is colour 0
/// (<see cref="ConsoleChromeRenderer.Ink"/>), the pitch grid had no border and no markings of
/// its own, and so the instrument read as a hole in the interface. The same sentence, word for
/// word, was written about the sprite screen's canvas the day before — see
/// <see cref="SpriteEditorPanelEdgeTests"/>, whose shape this file follows deliberately.
///
/// <para>The whole suite could not see it. Every layout assertion about this screen is about
/// <em>rectangles</em> — where <see cref="SfxEditorLayout"/> says a panel is — and a panel drawn
/// in exactly the background colour satisfies all of them; the golden hashes next door would
/// have caught a change but not an absence, because the absence was what they were pinned to.
/// So every assertion here is <see cref="VirtualConsole.Pget"/> at a named coordinate, and the
/// property they name is the one the eye was checking: <b>with the slot EMPTY, the pixel on the
/// panel's edge is not the background colour</b>, and <b>with the slot FULL, the panel's own
/// markings are still there and have not covered a note</b>.</para>
///
/// <para>The references are TIC-80's <c>src/studio/editors/sfx.c</c> (REFERENCES-EDITORS §5.1):
/// <c>drawCanvas</c> rings every led panel at <c>x - 1, y - 1, w + 2, h + 2</c> before
/// <c>drawCanvasLeds</c> fills it, and <c>drawCanvasLeds</c> then draws <em>every</em> cell of
/// the panel — the lit ones in its bright colour and the unlit ones in its dark twin — which is
/// what makes a TIC-80 panel legible when it holds nothing. Our palette has one grey and no dark
/// twin per hue, so the lattice is drawn one pixel per cell instead of one block per cell, in
/// the column <see cref="SfxEditorLayout.StepGapX"/> reserves; that is the named divergence and
/// it is the only one.</para>
/// </summary>
public class SfxEditorPanelEdgeTests : IDisposable
{
    private readonly string _root;

    public SfxEditorPanelEdgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-sfxedge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no sfx.bin, no sfx.txt.</summary>
    private SfxEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"edges\",\"author\":\"\",\"profile\":8}");
        return new SfxEditorSession(folder);
    }

    /// <summary>One frame with nothing hovered and no tooltip due.</summary>
    private static SfxEditorLayout DrawIdle(
        ShellScreen screen, SfxEditorSession session, SfxEditorView view) =>
        SfxEditorRenderer.Draw(screen, session, view, null, false);

    /// <summary>The note number of semitone <paramref name="semitone"/> in the octave a fresh view is showing.</summary>
    private static int NoteInOctave(SfxEditorView view, int semitone) =>
        view.Octave * SfxEditorLayout.OctaveRows + semitone;

    /// <summary>
    /// <b>The defect itself, as an assertion.</b> With the open slot empty — no note, no volume,
    /// no loop — the ring of console pixels around the instrument carries something that is not
    /// the background, on all four sides and at every row and column of it. That is what "the
    /// author can see where the sound goes" means when there is no sound yet.
    ///
    /// <para>The ring is not all ours and does not need to be: its top row is the frame's header
    /// rule, and its right column stands on the step gutter rather than a pixel outside the grid
    /// because a pixel outside the grid would be column 160.
    /// <see cref="SfxEditorLayout.InstrumentFrame"/> names each side. What the assertion pins is
    /// the RESULT, not the owner, which is the only thing the eye can check.</para>
    ///
    /// <para><b>Negative control</b>, two of them, because "not the background" is cheap to
    /// satisfy by accident: (1) one pixel further out is still the background, so what is being
    /// seen is a one-pixel border and not a slab of grey creeping across the screen; (2) both
    /// content columns of an ordinary step are still entirely colour 0 down the pitch grid, the
    /// loop row and the volume grid — the border and the ruler did not leak inside and repaint
    /// the surface, which is the mistake rule 3 of the order of 2026-08-25 forbids.</para>
    ///
    /// <para>Break recipe: delete the <c>DrawPanelFrames</c> call from
    /// <c>SfxEditorRenderer.Draw</c> — every side goes back to being background and the positive
    /// assertion goes red. In <see cref="SfxEditorLayout.InstrumentFrame"/> drop the <c>- 1</c>s
    /// so the ring lands on the grids themselves — control (2) goes red at the first step's
    /// column. Widen <c>SfxEditorRenderer.Column</c> back to the full
    /// <see cref="SfxEditorLayout.StepWidth"/> — the ruler then has no column of its own, and
    /// control (2) goes red as soon as a step is filled.</para>
    /// </summary>
    [Fact]
    public void TheInstrumentEdgeIsVisibleEvenWhenTheSlotIsEmpty()
    {
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        // The premise: the slot really is empty, so there is no music propping the edge up.
        Assert.True(session.SlotIsEmpty(view.SelectedSlot));
        Assert.Equal(0, session.SlotLength(view.SelectedSlot));

        Rectangle frame = layout.InstrumentFrame;
        for (int y = frame.Y; y < frame.Bottom; y++)
        {
            Assert.True(
                console.Pget(frame.X, y) != ConsoleChromeRenderer.Ink,
                $"the instrument has no left edge at row {y}");
            Assert.True(
                console.Pget(frame.Right - 1, y) != ConsoleChromeRenderer.Ink,
                $"the instrument has no right edge at row {y}");
        }
        for (int x = frame.X; x < frame.Right; x++)
        {
            Assert.True(
                console.Pget(x, frame.Y) != ConsoleChromeRenderer.Ink,
                $"the instrument has no top edge at column {x}");
            Assert.True(
                console.Pget(x, frame.Bottom - 1) != ConsoleChromeRenderer.Ink,
                $"the instrument has no bottom edge at column {x}");
        }

        // Negative control 1: the border is one pixel. The column left of it and the row under
        // it are ordinary ground, so nothing here passes by flooding the screen.
        Assert.Equal((byte)0, console.Pget(frame.X - 1, layout.Volume.Y + 4));
        Assert.Equal((byte)0, console.Pget(frame.X - 1, layout.Pitch.Y + 4));

        // Negative control 2: nothing was drawn on the surface. Step 1 is neither the cursor's
        // step nor a beat's last, so both of its content columns are still colour 0 in all three
        // grids. The two clear rows BETWEEN the grids are skipped on purpose: they are the staff
        // rules, which are older than this wave and are not part of any grid's surface.
        int left = layout.PitchCellRect(1, 0).X;
        foreach (Rectangle band in new[] { layout.Pitch, layout.Loop, layout.Volume })
        {
            for (int y = band.Y; y < band.Bottom; y++)
            {
                Assert.Equal((byte)0, console.Pget(left, y));
                Assert.Equal((byte)0, console.Pget(left + 1, y));
            }
        }

        // And the border cost the instrument nothing: still 32 steps of three pixels each.
        Assert.Equal(SfxEditorLayout.StepColumns * SfxEditorLayout.StepWidth, layout.Pitch.Width);
        Assert.True(frame.Contains(layout.Pitch));
    }

    /// <summary>
    /// <b>The second half of the same defect: an edge is not enough, the surface has to be
    /// marked.</b> A framed 96x24 rectangle with one note in it still does not say which step
    /// that note is on or which pitch it is at. TIC-80 answers by drawing every led of the panel
    /// (<c>drawCanvasLeds</c>); this screen answers with the same lattice at one pixel per cell,
    /// in the column no cell can paint. Three registers are pinned here — steps, pitches,
    /// volumes — and the fourth fact, that none of it touches the music, is the negative control.
    ///
    /// <para>The pitch dots are the piano's white keys and only those: C, D, E, F, G, A, B carry
    /// a mark and the five sharps do not, which draws the 2-3 grouping of a keyboard down the
    /// grid. That is TIC-80's <c>drawPianoOctave</c> (7 white keys and 5 black, §5.1) read as
    /// rows instead of as keys. C is brighter than the rest because it is the octave's anchor and
    /// the octave is what the OCT stepper moves.</para>
    ///
    /// <para><b>Negative control:</b> a note written on a SHARP row — the one row the ruler
    /// deliberately leaves blank — still fills both of its own pixels, and a note written on a
    /// BEAT step still fills both of its own pixels while the beat line runs beside it
    /// untouched. Without those two a ruler that painted over the music would pass every
    /// assertion above.</para>
    ///
    /// <para>Break recipe: delete the <c>DrawInstrumentRuler</c> call from
    /// <c>SfxEditorRenderer.Draw</c> — every positive assertion goes red. Move that call after
    /// <c>DrawPitchGrid</c> — the two negative controls go red, because the ruler would then be
    /// drawn over the notes rather than beside them. Change <c>IsNaturalKey</c> to return true
    /// unconditionally — the C# row's assertion goes red. Change
    /// <see cref="SfxEditorLayout.StepGapX"/> to <c>Pitch.X + step * StepWidth</c> — the ruler
    /// moves onto the cells and both negative controls go red.</para>
    /// </summary>
    [Fact]
    public void TheRulerMarksStepsAndPitchesInTheColumnNoCellCanPaint()
    {
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        // Step 1 carries a C# — a black key, the row the ruler leaves blank. Step 3 is the last
        // step of the first beat, so its note has to share its column band with a whole line.
        session.SetStep(0, 0, NoteInOctave(view, 0), 0, 7, 0);
        session.SetStep(0, 1, NoteInOctave(view, 1), 0, 7, 0);
        session.SetStep(0, 2, NoteInOctave(view, 0), 0, 7, 0);
        session.SetStep(0, 3, NoteInOctave(view, 0), 0, 7, 0);
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        // Steps: the last step of every beat carries a whole line the height of the instrument,
        // so counting to step 20 is counting five lines rather than twenty columns.
        int beat = layout.StepGapX(SfxEditorLayout.BeatSteps - 1);
        for (int y = layout.Pitch.Y; y < layout.Volume.Bottom; y++)
        {
            Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(beat, y));
        }

        // Pitches: an ordinary step's column is dotted on the seven white keys and blank on the
        // five black ones, and C — the octave's anchor — is the brighter of the dots.
        int gap = layout.StepGapX(1);
        Assert.Equal(ConsoleChromeRenderer.Text, console.Pget(gap, layout.PitchCellRect(1, 0).Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(gap, layout.PitchCellRect(1, 2).Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(gap, layout.PitchCellRect(1, 4).Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(gap, layout.PitchCellRect(1, 11).Y));
        Assert.Equal((byte)0, console.Pget(gap, layout.PitchCellRect(1, 1).Y));
        Assert.Equal((byte)0, console.Pget(gap, layout.PitchCellRect(1, 3).Y));
        Assert.Equal((byte)0, console.Pget(gap, layout.PitchCellRect(1, 10).Y));

        // Volumes: a dot at the top of every one of the eight levels, which is the scale a bar's
        // height is read against.
        for (int level = 0; level < SfxEditorLayout.VolumeLevels; level++)
        {
            Assert.Equal(
                ConsoleChromeRenderer.Dim,
                console.Pget(gap, layout.VolumeCellRect(1, level).Y));
        }

        // Negative control 1: the note on the black key kept both of its pixels, in its
        // waveform's own colour (slot 6 is waveform 0 — see SfxEditorRenderer's type note).
        Rectangle sharp = layout.PitchCellRect(1, 1);
        Assert.Equal((byte)6, console.Pget(sharp.X, sharp.Y));
        Assert.Equal((byte)6, console.Pget(sharp.X + 1, sharp.Y + 1));

        // Negative control 2: the note standing on a beat step kept both of its pixels too, and
        // the beat line runs in the column beside it rather than through it.
        Rectangle onBeat = layout.PitchCellRect(SfxEditorLayout.BeatSteps - 1, 0);
        Assert.Equal((byte)6, console.Pget(onBeat.X, onBeat.Y));
        Assert.Equal((byte)6, console.Pget(onBeat.X + 1, onBeat.Y));
        Assert.Equal(beat, onBeat.X + SfxEditorLayout.StepWidth - 1);
    }

    /// <summary>
    /// <b>The volume row, which the owner read as "one solid light block".</b> Thirty-two steps at
    /// full volume are thirty-two full bars, and with nothing to measure them against a full bar
    /// and a nearly-full one look the same. The two facts pinned here are the two the eye was
    /// after: the bars are separated by a column that belongs to the ruler and not to them, and
    /// all eight levels are marked beside a bar that reaches the top.
    ///
    /// <para><b>Negative control:</b> the very same coordinates on an EMPTY slot — the bar's own
    /// pixels go back to ground while the ruler's column carries exactly what it carried before.
    /// That is what says the separator is the panel's scale and not a gap between bars: a
    /// renderer that produced the striping by simply drawing narrow bars would pass the first
    /// half and fail this one.</para>
    ///
    /// <para>Break recipe: delete the volume half of <c>DrawInstrumentRuler</c> — the level dots
    /// go and the negative control's second assertion goes red. Draw the ruler before
    /// <c>DrawStepCursor</c> instead of after it — the cursor's band repaints the ruler's column
    /// at step 0, which this test does not probe, but the golden hash next door moves; that is
    /// the case the hash is for.</para>
    /// </summary>
    [Fact]
    public void TheVolumeRowStillReadsAsStepsWhenEveryStepIsLoud()
    {
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        for (int step = 0; step < SfxEditorLayout.StepColumns; step++)
        {
            session.SetStep(0, step, NoteInOctave(view, 0), 0, SfxEditorSession.MaxVolume, 0);
        }
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        Assert.Equal(SfxEditorLayout.StepColumns, session.SlotLength(0));

        // Step 1's bar fills its two content columns right up to the top level...
        Rectangle top = layout.VolumeCellRect(1, SfxEditorSession.MaxVolume);
        Assert.Equal(ConsoleChromeRenderer.Text, console.Pget(top.X, top.Y));
        Assert.Equal(ConsoleChromeRenderer.Text, console.Pget(top.X + 1, top.Y));
        // ...and the column between it and step 2's bar is the ruler, not more bar.
        int gap = layout.StepGapX(1);
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(gap, top.Y));
        // Every one of the eight levels stays countable beside a bar that is at the top of them.
        for (int level = 0; level < SfxEditorLayout.VolumeLevels; level++)
        {
            Assert.Equal(
                ConsoleChromeRenderer.Dim,
                console.Pget(gap, layout.VolumeCellRect(1, level).Y));
        }

        // Negative control: same two coordinates, empty slot. The bar is gone, the ruler is not.
        var quiet = new ShellScreen();
        DrawIdle(quiet, FreshCart(), new SfxEditorView());
        Assert.Equal((byte)0, quiet.Console.Pget(top.X, top.Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, quiet.Console.Pget(gap, top.Y));
    }

    /// <summary>
    /// <b>"Which of the sixty-four sounds am I editing?"</b> — which the owner could not answer
    /// from the picture: the open slot read to him as "a tiny blue dot". The cell's own plate is
    /// <see cref="ConsoleChromeRenderer.ActiveBg"/>, which on this palette is the <em>dark</em>
    /// blue #2c3e8c, and its ring is white against neighbours that are light grey — one palette
    /// step apart. Nothing inside a sixteen-by-four field of 3x3 squares can be made to stand out
    /// that way. Two ticks on the selector's own frame can: the frame's ground is colour 0, and
    /// a column tick and a row tick name one square between them. TIC-80 hangs marks off a
    /// panel's frame for the same reason (the sheet's neighbouring-page strokes, §2.1).
    ///
    /// <para>The frame itself is the other half: three sides of it, the fourth being the screen's
    /// own edge, exactly as <see cref="SpriteEditorLayout.SheetFrame"/> resolves the same
    /// problem.</para>
    ///
    /// <para><b>Negative control</b>, twice: the frame one cell away from either tick is the
    /// ordinary dim rule — so a renderer that simply lit the whole frame would fail — and the
    /// ticks move when the slot does, so they are reading the open slot and not a constant.</para>
    ///
    /// <para>Break recipe: delete the two <c>SlotColumnTickRect</c>/<c>SlotRowTickRect</c> fills
    /// from <c>DrawSelector</c> — the tick assertions go red. Delete the
    /// <see cref="SfxEditorLayout.SlotsFrame"/> line from <c>DrawPanelFrames</c> — the frame
    /// sweep goes red and the ticks lose the rule they stand on. Fill the whole frame in
    /// <see cref="ConsoleChromeRenderer.Bright"/> — the first negative control goes red.</para>
    /// </summary>
    [Fact]
    public void TheOpenSlotIsFoundFromOutsideTheSelectorLattice()
    {
        const int open = 37;                 // row 2, column 5 of the sixteen-by-four field
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        view.SelectSlot(open);
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        // The selector has an edge at all — its three drawable sides, at every pixel of them.
        Rectangle frame = layout.SlotsFrame;
        for (int x = layout.Slots.X; x < frame.Right; x++)
        {
            Assert.True(
                console.Pget(x, frame.Y) != ConsoleChromeRenderer.Ink,
                $"the selector has no top rule at column {x}");
            Assert.True(
                console.Pget(x, frame.Bottom - 1) != ConsoleChromeRenderer.Ink,
                $"the selector has no bottom rule at column {x}");
        }
        for (int y = frame.Y; y < frame.Bottom; y++)
        {
            Assert.True(
                console.Pget(frame.Right - 1, y) != ConsoleChromeRenderer.Ink,
                $"the selector has no right rule at row {y}");
        }

        // The two ticks: white, three pixels each, on the column and the row of slot 37.
        Rectangle column = layout.SlotColumnTickRect(open);
        Rectangle row = layout.SlotRowTickRect(open);
        Assert.Equal(layout.SlotCellRect(open).X, column.X);
        Assert.Equal(layout.SlotCellRect(open).Y, row.Y);
        for (int i = 0; i < SfxEditorLayout.SlotCellSize; i++)
        {
            Assert.Equal(ConsoleChromeRenderer.Bright, console.Pget(column.X + i, column.Y));
            Assert.Equal(ConsoleChromeRenderer.Bright, console.Pget(row.X, row.Y + i));
        }

        // Negative control 1: they are TICKS. One cell along either rule is the dim edge again.
        Assert.Equal(
            ConsoleChromeRenderer.Dim,
            console.Pget(layout.SlotColumnTickRect(open - 1).X, column.Y));
        Assert.Equal(
            ConsoleChromeRenderer.Dim,
            console.Pget(row.X, layout.SlotRowTickRect(open - SfxEditorLayout.SlotColumns).Y));

        // Negative control 2: they follow the open slot. With slot 0 open, slot 37's column is
        // grey and slot 0's is white — so the marks are reading state, not a fixed corner.
        var moved = new ShellScreen();
        DrawIdle(moved, session, new SfxEditorView());
        Assert.Equal(ConsoleChromeRenderer.Dim, moved.Console.Pget(column.X, column.Y));
        Assert.Equal(
            ConsoleChromeRenderer.Bright,
            moved.Console.Pget(layout.SlotColumnTickRect(0).X, column.Y));
    }
}
