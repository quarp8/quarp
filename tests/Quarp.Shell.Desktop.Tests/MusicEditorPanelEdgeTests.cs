using Microsoft.Xna.Framework;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The tests that were missing on the MUSIC screen.</b> The owner opened the demo cart and
/// reported two things the whole suite could not see. The whole-song overview down the right
/// edge was "a tall empty rectangle in a frame with one dot at the top": with no scale of its
/// own it named neither a pattern nor a channel, and with an empty song it drew nothing at all,
/// because a silent channel left the ground alone and the ground is colour 0. And the cursor's
/// row in the tracker was "a solid wide light stripe with the digits almost unreadable on it":
/// the band was <see cref="ConsoleChromeRenderer.Dim"/>, which on this palette is a MID grey
/// (#6e7b8f) — lighter than the ground, lighter than the very ink a resting cell prints its
/// "--" in, and one step from ordinary text.
///
/// <para>Neither could be caught by the assertions this suite had. Layout tests are about
/// <em>rectangles</em>, and an empty rectangle satisfies them; the golden hashes next door would
/// have caught a change but not an absence, because the absence was what they were pinned to. So
/// the assertions here are <see cref="VirtualConsole.Pget"/> at named coordinates, and the two
/// properties they name are the ones the eye was checking: <b>with the song EMPTY, the pixel on a
/// panel's edge is not the background colour</b>, and <b>the row band is darker than every ink
/// printed on it</b>.</para>
///
/// <para>The references are TIC-80's <c>src/studio/editors/music.c</c> (REFERENCES-EDITORS §6.1):
/// <c>drawTrackerChannel</c> grounds each channel on a rectangle before writing a row into it and
/// paints the current row as a <em>dark</em> plate with the glyphs still light on top of it, and
/// <c>drawTrackerFrames</c> draws every one of its sixteen frames as a button that exists whether
/// or not it holds anything. PICO-8's pattern navigator is a permanent list for the same reason
/// (§6.3). Sixteen pixels of width cannot hold sixty-four numbered buttons, so this screen's
/// overview carries the fact as a ruler instead — the named divergence, and the only one.</para>
/// </summary>
public class MusicEditorPanelEdgeTests : IDisposable
{
    private readonly string _root;

    public MusicEditorPanelEdgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-musicedge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no music.bin, no music.txt.</summary>
    private MusicEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"edges\",\"author\":\"\",\"profile\":8}");
        return new MusicEditorSession(folder);
    }

    /// <summary>One frame with nothing hovered and no tooltip due.</summary>
    private static MusicEditorLayout DrawIdle(
        ShellScreen screen, MusicEditorSession session, MusicEditorView view) =>
        MusicEditorRenderer.Draw(screen, session, view, null, false);

    /// <summary>
    /// Perceived lightness of one palette slot, in the classic 0.299/0.587/0.114 weights held as
    /// integers so nothing here needs a float. It exists because "the digits are readable on the
    /// band" is a statement about lightness and not about slot numbers: pinning slot 4 by name
    /// would pass just as well if slot 4 were white.
    /// </summary>
    private static int Lightness(byte slot)
    {
        uint rgb = Palette.Master32[slot];
        return (int)(((rgb >> 16) & 0xFF) * 299 + ((rgb >> 8) & 0xFF) * 587 + (rgb & 0xFF) * 114);
    }

    /// <summary>
    /// <b>The defect itself, as an assertion.</b> With the song empty — not one of the sixty-four
    /// patterns holding a voice — the ring of console pixels around the pattern grid and the ring
    /// around the whole-song overview both carry something that is not the background, on all
    /// four sides and at every row and column of them.
    ///
    /// <para>Neither ring is entirely this screen's own doing and neither needs to be: the grid's
    /// top row is the frame's header rule and its bottom row the footer rule
    /// (<see cref="MusicEditorLayout.GridFrame"/> names each side). What the assertion pins is
    /// the RESULT, not the owner, which is the only thing the eye can check.</para>
    ///
    /// <para><b>Negative control</b>, twice: (1) one pixel further out, on both panels, is still
    /// the background — so what is being seen is a one-pixel border and not grey creeping across
    /// the screen; (2) inside the overview, the two columns a channel's mark would use are still
    /// colour 0, because the song really is empty — a "fix" that filled the box with grey would
    /// pass the sweep and fail this, and it would also be a lie about the song.</para>
    ///
    /// <para>Break recipe: delete the <c>DrawGridFrame</c> call from
    /// <c>MusicEditorRenderer.Draw</c> — the grid's two side columns go back to being background
    /// and the first sweep goes red. Delete the <c>DrawOverviewRuler</c> call — the overview's
    /// frame survives (it is older than this wave) but control (2)'s sibling in
    /// <see cref="TheOverviewShowsTheSongTheCursorAndThePlayhead"/> goes red. Change
    /// <c>DrawOverviewRuler</c>'s lane rules to span the whole interior instead of one column
    /// each — control (2) goes red.</para>
    /// </summary>
    [Fact]
    public void TheGridAndTheOverviewHaveEdgesOnAnEmptySong()
    {
        MusicEditorSession session = FreshCart();
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        MusicEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        // The premise: no voice anywhere in the song, so there is nothing propping an edge up.
        for (int pattern = 0; pattern < MusicEditorLayout.PatternCount; pattern++)
        {
            for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
            {
                Assert.True(session.ChannelIsSilent(pattern, channel));
            }
        }

        foreach (Rectangle panel in new[] { layout.GridFrame, layout.Overview })
        {
            for (int y = panel.Y; y < panel.Bottom; y++)
            {
                Assert.True(
                    console.Pget(panel.X, y) != ConsoleChromeRenderer.Ink,
                    $"panel at {panel} has no left edge at row {y}");
                Assert.True(
                    console.Pget(panel.Right - 1, y) != ConsoleChromeRenderer.Ink,
                    $"panel at {panel} has no right edge at row {y}");
            }
            for (int x = panel.X; x < panel.Right; x++)
            {
                Assert.True(
                    console.Pget(x, panel.Y) != ConsoleChromeRenderer.Ink,
                    $"panel at {panel} has no top edge at column {x}");
                Assert.True(
                    console.Pget(x, panel.Bottom - 1) != ConsoleChromeRenderer.Ink,
                    $"panel at {panel} has no bottom edge at column {x}");
            }
        }

        // Negative control 1: both borders are one pixel wide.
        Assert.Equal((byte)0, console.Pget(layout.GridFrame.X - 1, layout.Rows.Y + 20));
        Assert.Equal((byte)0, console.Pget(layout.Overview.X - 1, layout.Rows.Y + 20));

        // Negative control 2: the overview's interior is not a plate. Pattern 1 holds nothing, so
        // channel 0's two mark columns there are untouched ground.
        Rectangle mark = layout.OverviewChannelRect(1, 0);
        Assert.Equal((byte)0, console.Pget(mark.X, mark.Y));
        Assert.Equal((byte)0, console.Pget(mark.X + 1, mark.Y));
    }

    /// <summary>
    /// <b>The row band, and the one property that makes digits readable on it: it has to be
    /// darker than every ink that stands on it.</b> TIC-80 draws the tracker's current row as a
    /// dark plate with white glyphs over it (<c>music.c</c>, <c>drawTrackerChannel</c>,
    /// REFERENCES-EDITORS §6.1). This screen used <see cref="ConsoleChromeRenderer.Dim"/>, which
    /// is a mid grey here, so the row was lighter than the ground it interrupted and equal to the
    /// ink a resting cell prints in — the owner read exactly that: a light stripe with the digits
    /// almost gone.
    ///
    /// <para>The assertion is written about lightness rather than about slot numbers on purpose:
    /// naming slot 4 would pass just as well if slot 4 were repainted white, and the thing being
    /// pinned is a fact about the eye. Every colour actually found on the row is measured against
    /// the band's, so the test needs no list of what the row is allowed to contain and cannot go
    /// stale when the row gains a control.</para>
    ///
    /// <para><b>Negative control</b>, three parts, because "darker than everything" is trivially
    /// true of an absent band: (1) the row has no background pixel left on it, so a band is
    /// really there; (2) <see cref="ConsoleChromeRenderer.Dim"/> is among the inks on it — that
    /// is the resting cells' "--", and it is the very colour the band used to be, so the rule
    /// has real work to do and a regression to Dim fails outright; (3) the row below is NOT
    /// banded, so this is a decision about one row and not a colour the whole grid wears.</para>
    ///
    /// <para>Break recipe: change the <c>Fill(... RowRect ...)</c> in <c>DrawRows</c> back to
    /// <c>Dim</c> — negative control (2) makes the lightness sweep go red on the resting cells'
    /// own ink. Change it to <c>Text</c> or <c>Bright</c> — the sweep goes red on the digits.
    /// Delete the fill — negative control (1) goes red.</para>
    /// </summary>
    [Fact]
    public void TheCursorRowIsDarkerThanEveryInkPrintedOnIt()
    {
        MusicEditorSession session = FreshCart();
        // Something to read on the cursor's own row: one sounding voice, three resting ones, and
        // a section marker, so the row carries every kind of ink this screen can print on it.
        session.SetChannelSlot(0, 0, 12);
        session.SetPatternFlags(0, MusicEditorSession.FlagLoopStart);
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        MusicEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        Assert.Equal(0, session.CursorPattern);
        Rectangle row = layout.RowRect(session.CursorPattern, view.FirstPattern);

        var seen = new HashSet<byte>();
        for (int y = row.Y; y < row.Bottom; y++)
        {
            for (int x = row.X; x < row.Right; x++)
            {
                seen.Add(console.Pget(x, y));
            }
        }

        // Negative control 1: a band is really there — not one pixel of the row is still ground.
        Assert.DoesNotContain(ConsoleChromeRenderer.Ink, seen);
        Assert.Contains(ConsoleChromeRenderer.ActiveBg, seen);
        // Negative control 2: the resting cells' dim "--" is on the row, and dim is what the band
        // used to be — so the rule below is not vacuous and a regression cannot hide from it.
        Assert.Contains(ConsoleChromeRenderer.Dim, seen);

        // The property the eye was checking.
        int band = Lightness(ConsoleChromeRenderer.ActiveBg);
        foreach (byte ink in seen)
        {
            if (ink == ConsoleChromeRenderer.ActiveBg)
            {
                continue;
            }
            Assert.True(
                Lightness(ink) > band,
                $"colour {ink} is not lighter than the band it is printed on");
        }

        // Negative control 3: the next row down is not banded — its left edge is ordinary ground.
        Assert.Equal((byte)0, console.Pget(row.X, row.Bottom));
    }

    /// <summary>
    /// <b>The overview, made to show the song.</b> Four facts are pinned, because the owner asked
    /// for four: the box has a scale at all (lane rules down each channel, so it is a strip and
    /// not a void even on an empty song), the sixty-four rows can be counted (a brighter tick
    /// every <see cref="MusicEditorLayout.OverviewGroup"/> patterns), the cursor is visible, and
    /// the playhead is visible. TIC-80's frame column shows all sixteen of its frames whether or
    /// not they hold anything and highlights the current one (<c>drawTrackerFrames</c>, §6.1);
    /// this is that, at sixteen pixels of width.
    ///
    /// <para><b>Negative control</b>, three parts: (1) the ruler is beside the music, never on
    /// it — the pattern that DOES hold a voice keeps both mark pixels while the lane column
    /// beside them carries the rule; (2) the cursor's mark is a row of lane pixels and not a
    /// fill, so the cursor's own pattern is still empty where the song is empty; (3) the marks
    /// move when the state does — with the cursor one pattern along, the old row reverts to
    /// exactly the ruler tick it would have had.</para>
    ///
    /// <para>Break recipe: delete the <c>DrawOverviewRuler</c> call from <c>DrawOverview</c> —
    /// the lane and tick assertions go red. Delete the <c>MarkOverviewRow</c> calls — the cursor
    /// and playhead assertions go red. Move those calls before the channel-mark loop — negative
    /// control (1) still passes (the columns do not overlap) but the cursor row would be hidden
    /// under nothing, so the assertions stay green; that is deliberate, and it is why the
    /// overlap is guaranteed by <see cref="MusicEditorLayout.OverviewLaneX"/> rather than by draw
    /// order. Change <c>OverviewLaneX</c> to <c>OverviewInterior.X + channel * 3</c> — it moves
    /// onto the marks and negative control (1) goes red.</para>
    /// </summary>
    [Fact]
    public void TheOverviewShowsTheSongTheCursorAndThePlayhead()
    {
        const int sounding = 2;              // the one pattern with a voice in it
        const int cursor = 20;
        const int playhead = 33;
        MusicEditorSession session = FreshCart();
        session.SetChannelSlot(sounding, 2, 3);
        session.SetCursor(cursor, 0);
        var view = new MusicEditorView();
        view.ReportPlaying(true, playhead);
        var screen = new ShellScreen();

        MusicEditorLayout layout = DrawIdle(screen, session, view);
        VirtualConsole console = screen.Console;

        // The lanes: four rules the length of the song, so the box is a strip and not a void.
        // Pattern 41 is neither a group tick, nor the cursor, nor the playhead.
        int plain = layout.OverviewRowRect(41).Y;
        for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
        {
            Assert.Equal(
                ConsoleChromeRenderer.Dim, console.Pget(layout.OverviewLaneX(channel), plain));
        }

        // The ruler: every fourth pattern is ticked brighter, so sixty-four rows are countable.
        Assert.Equal(0, 40 % MusicEditorLayout.OverviewGroup);
        Assert.Equal(
            ConsoleChromeRenderer.Text,
            console.Pget(layout.OverviewLaneX(0), layout.OverviewRowRect(40).Y));

        // The cursor and the playhead, each on its own row, in the lane columns.
        Assert.Equal(
            ConsoleChromeRenderer.Bright,
            console.Pget(layout.OverviewLaneX(0), layout.OverviewRowRect(cursor).Y));
        Assert.Equal(
            ConsoleChromeRenderer.Warn,
            console.Pget(layout.OverviewLaneX(0), layout.OverviewRowRect(playhead).Y));

        // Negative control 1: the ruler is beside the music. The one sounding pattern keeps both
        // of its mark pixels and the lane column beside them carries the rule.
        Rectangle mark = layout.OverviewChannelRect(sounding, 2);
        Assert.Equal(ConsoleChromeRenderer.Text, console.Pget(mark.X, mark.Y));
        Assert.Equal(ConsoleChromeRenderer.Text, console.Pget(mark.X + 1, mark.Y));
        Assert.Equal(ConsoleChromeRenderer.Dim, console.Pget(layout.OverviewLaneX(2), mark.Y));

        // Negative control 2: the cursor's mark is a row of lane pixels, not a plate — its own
        // pattern holds no voice and still shows none.
        Rectangle empty = layout.OverviewChannelRect(cursor, 0);
        Assert.Equal((byte)0, console.Pget(empty.X, empty.Y));

        // Negative control 3: move the cursor one pattern along and the old row reverts to the
        // ruler tick it would have had — so the bright pixel was reading the cursor.
        session.SetCursor(cursor + 1, 0);
        var moved = new ShellScreen();
        DrawIdle(moved, session, view);
        Assert.Equal(
            ConsoleChromeRenderer.Text,
            moved.Console.Pget(layout.OverviewLaneX(0), layout.OverviewRowRect(cursor).Y));
        Assert.Equal(
            ConsoleChromeRenderer.Bright,
            moved.Console.Pget(layout.OverviewLaneX(0), layout.OverviewRowRect(cursor + 1).Y));
    }

    /// <summary>
    /// <b>A renderer may not throw, at any data.</b> The console died once already on this
    /// screen, inside <c>Draw</c>, because a tooltip lookup threw on a region it did not
    /// recognise — and an exception thrown while painting a frame reaches nothing that can
    /// recover, so the author loses unsaved work to a label. This wave added two more places that
    /// read an index the renderer does not own: the overview's cursor row and its playhead row.
    /// Both are swept here with the song in its most awkward states — empty, and with a playhead
    /// reported off the end of the song — together with every hover target either screen can
    /// hand in.
    ///
    /// <para><b>Negative control:</b> the same sweep asserts the frame was actually drawn (the
    /// panel edges are not background), so a <c>Draw</c> that swallowed everything and returned
    /// early would not pass by being quiet.</para>
    ///
    /// <para>Break recipe: make <c>MarkOverviewRow</c> index the lane without its range guard —
    /// the "playhead past the end" case throws and this goes red. Make
    /// <c>MusicEditorRenderer.TooltipText</c> call <c>EditorIcons.MusicRegionTooltip</c> without
    /// its <see cref="MusicRegion.None"/> guard — the hover sweep goes red.</para>
    /// </summary>
    [Fact]
    public void DrawingNeverThrowsWhateverTheSongAndTheHoverAre()
    {
        MusicEditorSession session = FreshCart();
        var view = new MusicEditorView();
        // A playhead the wiring could report while the song is being edited under it.
        view.ReportPlaying(true, MusicEditorLayout.PatternCount + 5);

        foreach (MusicRegion region in Enum.GetValues<MusicRegion>())
        {
            var screen = new ShellScreen();
            MusicEditorLayout layout = MusicEditorRenderer.Draw(
                screen, session, view, HoverTarget.OfMusicRegion(region), true);
            // Negative control: it really drew. A quiet no-op would leave the panel edges blank.
            Assert.True(
                screen.Console.Pget(layout.Overview.X, layout.Overview.Y)
                    != ConsoleChromeRenderer.Ink,
                $"nothing was drawn while hovering {region}");
        }
    }
}
