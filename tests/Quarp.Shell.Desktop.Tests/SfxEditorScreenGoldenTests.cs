using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the sound editor screen</b> — what wave R5 was worth doing for, and
/// the same instrument <c>LibraryScreenGoldenTests</c> (R1), <c>SpriteEditorScreenGoldenTests</c>
/// (R2) and <c>MapEditorScreenGoldenTests</c> (R3) put on the three screens that moved before
/// this one.
///
/// <para>Until this wave the sound editor was painted at the window's native resolution through a
/// <c>SpriteBatch</c>, and there was no artefact of it a test could look at: no buffer, no
/// pixels, only draw calls into a graphics device no headless runner has. Every layout assertion
/// in <c>SfxEditorTests</c> was therefore about <em>rectangles</em> — where the layout said a
/// panel was — and none about pixels, so a renderer that drew the volume grid in the pitch grid's
/// rectangle would have passed all of them. Now the screen is drawn into a
/// <see cref="Framebuffer"/> by the same core calls a cartridge uses, so it can be hashed by
/// exactly the owner that hashes a cartridge's frame: <see cref="FrameHash"/>. Same digest, same
/// 16-hex text form, same discipline. There is no second hasher in this repository and this file
/// does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a tool screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is PLAYBOOK §4's: never re-pin silently. If one of these
/// changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these three constants came from — read this before re-pinning one.</b> Wave R5,
/// like R1, R2 and R3 before it, was carried out in an environment with no .NET SDK and no
/// package feed, so nothing in the repository could be built or run. The hashes below were
/// therefore <em>derived</em>, not observed: by transliterating <c>VirtualConsole</c>'s
/// <c>Cls</c>, <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Pset</c> together with
/// <see cref="SystemFont"/>'s glyph table, <c>EditorIcons</c>' mask table and this screen's draw
/// order, and running <see cref="FrameHash"/>'s FNV-1a over the result. That model was first
/// checked against <b>all eleven hashes already pinned in this suite</b> — three in
/// <c>LibraryScreenGoldenTests</c>, three in <c>SpriteEditorScreenGoldenTests</c> and five in
/// <c>MapEditorScreenGoldenTests</c> — and reproduced every one of them exactly, along with every
/// <c>Pget</c> probe standing above them. That is the evidence that the rasterizer, the font, the
/// icon masks and the shared chrome are modelled right; what remains unproven by that check is
/// only this file's own transcription of <em>this</em> screen's draw order. So: <b>if one of these
/// three fails on the first real build while the <c>Pget</c> probes above it all pass, the
/// overwhelmingly likely explanation is a slip in that transcription and not a defect in the
/// screen</b> — check the probes, look at the frame, and re-pin with a note saying so. If a probe
/// fails too, the screen genuinely changed and the ordinary rule applies: say which pixel moved
/// and why.</para>
///
/// <para><b>Why the probes are here at all.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the structural facts the picture
/// is supposed to have — the instrument starts at column 64, a step is three pixels wide, a
/// semitone row is two, a note is drawn in the colour of its own waveform, the loop's brackets
/// stand on the columns they mark, the panel's fields sit under the preview — so a failure tells
/// whoever reads it whether the screen is broken or merely redrawn.</para>
/// </summary>
public class SfxEditorScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public SfxEditorScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-sfxscreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no sfx.bin, no sfx.txt.</summary>
    private SfxEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        return new SfxEditorSession(folder);
    }

    /// <summary>
    /// A slot with something to look at: six steps rising by whole tones from C-5, each on the
    /// next waveform of the six and each a step quieter than the last, and a loop over steps 2-5.
    /// Six waveforms means all six of the pitch grid's colours appear, so a renderer that painted
    /// every note in one ink cannot pass by accident; the falling volumes mean the volume grid's
    /// bars are all of different heights, so one drawn at the wrong pitch cannot either.
    /// </summary>
    private SfxEditorSession Sounded()
    {
        SfxEditorSession session = FreshCart();
        for (int step = 0; step < 6; step++)
        {
            session.SetStep(0, step, 36 + step * 2, step % SfxEditorSession.WaveCount, 7 - step, 0);
        }
        session.SetLoop(0, 2, 6);
        return session;
    }

    /// <summary>One frame with nothing hovered and no tooltip due.</summary>
    private static SfxEditorLayout DrawIdle(
        ShellScreen screen, SfxEditorSession session, SfxEditorView view) =>
        SfxEditorRenderer.Draw(screen, session, view, null, false);

    /// <summary>
    /// The screen an author meets on a brand-new cart: an empty slot 0, the cursor on step 0, the
    /// piano standing on octave 3 (C-5), the pen holding waveform 0 and no effect, nothing saved
    /// and nothing to undo.
    /// </summary>
    [Fact]
    public void AFreshCartOpensOnAnEmptySlot()
    {
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);

        // The screen is the console's screen, not a window's — the whole of ADR-029 in four
        // numbers, and the reason every constant below is a fixed console pixel.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(64, 11, 96, 24), layout.Pitch);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(64, 36, 96, 3), layout.Loop);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(64, 40, 96, 16), layout.Volume);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(3, 68, 154, 7), layout.Effects);

        VirtualConsole console = screen.Console;
        // The three rules that cut the screen into bands: under the top bar, above the message
        // line, above the status line.
        Assert.Equal((byte)1, console.Pget(0, 10));
        Assert.Equal((byte)1, console.Pget(159, 10));
        Assert.Equal((byte)1, console.Pget(0, 78));
        Assert.Equal((byte)1, console.Pget(0, 84));
        // The step cursor is a dim band three pixels wide down the whole instrument, and the
        // next step along is not in it. A grid drawn at any other pitch fails the second.
        Assert.Equal((byte)1, console.Pget(64, 11));
        Assert.Equal((byte)0, console.Pget(67, 11));
        // The two staff rules between the three grids, in the one clear row each has.
        Assert.Equal((byte)1, console.Pget(64, 35));
        Assert.Equal((byte)1, console.Pget(64, 39));
        // The selector: slot 0 is open (blue body, bright frame) and slot 1 is empty (dim body).
        Assert.Equal((byte)3, console.Pget(0, 22));
        Assert.Equal((byte)4, console.Pget(1, 23));
        Assert.Equal((byte)1, console.Pget(3, 22));
        // The preview box's frame, and the pulse-12 schematic standing inside it.
        Assert.Equal((byte)1, console.Pget(0, 35));
        Assert.Equal((byte)3, console.Pget(31, 39));
        // The wave row: cell 0 is the pen's, so it wears a bright frame over a blue plate; the
        // second cell's frame is dim.
        Assert.Equal((byte)3, console.Pget(64, 57));
        Assert.Equal((byte)4, console.Pget(78, 58));
        Assert.Equal((byte)1, console.Pget(80, 57));
        // The effect row: OFF is the pen's, so cell 0 is the lit one and cell 1 is not.
        Assert.Equal((byte)3, console.Pget(3, 68));
        Assert.Equal((byte)1, console.Pget(25, 68));
        // A stepper field's frame and its left arrow.
        Assert.Equal((byte)1, console.Pget(0, 44));
        Assert.Equal((byte)3, console.Pget(2, 46));
        // The sound tab is the active one: its plate is the library's blue, showing through the
        // gaps of the speaker glyph.
        Assert.Equal((byte)4, console.Pget(141, 1));
        // The status line's step readout, and no standing notice above it.
        Assert.Equal((byte)2, console.Pget(2, 85));
        Assert.Equal((byte)0, console.Pget(1, 79));
        // An empty slot draws no volume bar anywhere.
        Assert.Equal((byte)0, console.Pget(70, 41));
        Assert.Equal((byte)0, console.Pget(67, 54));
        // Sixteen slots and no more: nothing on this screen reaches a master colour above 15.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)15);
        }

        Assert.Equal("1e6dc7818c1fa32e", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The same screen with a sound on it: six notes, six waveforms, six volumes and a loop over
    /// steps 2-5. Everything this screen exists to show moves at once and all of it is in the
    /// hash — the pitch grid's notes in their waveforms' colours, the loop row's rail and its two
    /// brackets, the volume grid's falling bars, the length field, and the chrome's report that
    /// there is unsaved work.
    /// </summary>
    [Fact]
    public void NotesAndALoopShowOnAllThreeGridsAtOnce()
    {
        SfxEditorSession session = Sounded();
        var view = new SfxEditorView();
        var screen = new ShellScreen();

        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(6, session.SlotLength(0));
        Assert.Equal(2, session.SlotLoopStart(0));
        Assert.Equal(6, session.SlotLoopEnd(0));

        DrawIdle(screen, session, view);

        VirtualConsole console = screen.Console;
        // Step 0's note is C-5 — the bottom of the octave on screen — on waveform 0, so it is
        // drawn in slot 6 on the grid's bottom row. Step 1 is a whole tone up on waveform 1
        // (slot 7) one row higher; step 5 is five whole tones up on waveform 5 (slot 11). A grid
        // drawn at the wrong row pitch fails the second and third; one that painted every note in
        // one ink fails all three.
        Assert.Equal((byte)6, console.Pget(64, 33));
        Assert.Equal((byte)6, console.Pget(65, 34));
        Assert.Equal((byte)7, console.Pget(67, 29));
        Assert.Equal((byte)11, console.Pget(79, 13));
        // The loop row: steps 0 and 1 are played but not looped (the dim rail), step 2 onwards is
        // looped (blue), and the two brackets stand on the columns they mark — the start's on the
        // left edge of step 2, the end's on the right edge of step 5.
        Assert.Equal((byte)2, console.Pget(64, 36));
        Assert.Equal((byte)4, console.Pget(71, 37));
        Assert.Equal((byte)3, console.Pget(70, 36));
        Assert.Equal((byte)3, console.Pget(80, 36));
        // The volume grid: step 0 is at 7 so its bar reaches the top row; step 1 is at 6 so its
        // does not, and starts one row down. The bottom row is volume 0 and is never a bar.
        Assert.Equal((byte)2, console.Pget(64, 40));
        Assert.Equal((byte)0, console.Pget(67, 40));
        Assert.Equal((byte)2, console.Pget(67, 42));
        Assert.Equal((byte)0, console.Pget(67, 54));
        // Unsaved work: the save button's face is the modified floppy in warn yellow.
        Assert.Equal((byte)8, console.Pget(11, 12));
        // Still no standing notice — this bank is writable.
        Assert.Equal((byte)0, console.Pget(1, 79));

        Assert.Equal("8d1eb42f45e31031", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Esc on a dirty bank: the question. The message line carries the prompt — the heading at
    /// the left margin in warn yellow, the three verbs right-aligned to the screen's edge, each
    /// on the very rectangle <see cref="SfxEditorLayout.PromptVerbRect"/> makes clickable, and
    /// all of it measured by <see cref="ConsoleChrome"/> so this screen and the two that moved
    /// before it cannot disagree about where "ESC STAY" is.
    /// </summary>
    [Fact]
    public void TheExitPromptTakesTheMessageLineAndItsVerbsAreWhereTheHitTestSaysTheyAre()
    {
        SfxEditorSession session = Sounded();
        var view = new SfxEditorView();
        Assert.False(view.RequestClose(session));      // dirty: the prompt goes up instead of closing
        Assert.True(view.ExitPromptShown);
        var screen = new ShellScreen();

        SfxEditorLayout layout = DrawIdle(screen, session, view);

        VirtualConsole console = screen.Console;
        // "UNSAVED." at the margin in warn yellow: 'U' fills its top-left pixel.
        Assert.Equal((byte)8, console.Pget(1, 79));
        // The first and last verbs are drawn one pixel inside the rectangles a click is tested
        // against, so a label and its hit target cannot drift apart.
        Microsoft.Xna.Framework.Rectangle save = layout.PromptVerbRect(EditorPromptVerb.SaveAndExit);
        Microsoft.Xna.Framework.Rectangle stay = layout.PromptVerbRect(EditorPromptVerb.Stay);
        Assert.Equal((byte)3, console.Pget(save.X + 1, save.Y));
        Assert.Equal((byte)3, console.Pget(stay.X + 1, stay.Y));
        Assert.True(layout.TryPromptVerb(stay.X + stay.Width / 2, stay.Y + 2, out EditorPromptVerb hit));
        Assert.Equal(EditorPromptVerb.Stay, hit);
        // The instrument is untouched by the question: the sound the author is deciding about is
        // still on screen, which is the whole reason the prompt lives on one reserved line.
        Assert.Equal((byte)6, console.Pget(64, 33));

        Assert.Equal("292bffca0ed78d1f", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Two consoles, two framebuffers. The shell's screen and a cartridge's are separate
    /// instances by construction (<see cref="ShellScreen"/>), and this says so out loud: drawing
    /// the whole editor leaves a console built the same way untouched. It is the property that
    /// keeps anything the shell draws out of the buffer the CI hashes.
    /// </summary>
    [Fact]
    public void DrawingTheEditorTouchesNoOtherConsole()
    {
        SfxEditorSession session = FreshCart();
        var view = new SfxEditorView();
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        DrawIdle(shell, session, view);

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the session and the
    /// view and on nothing else — no window size, no clock, no leftover console state. That is
    /// what makes a pinned hash meaningful rather than lucky, and it is why
    /// <see cref="ShellScreen.Begin"/> resets camera, clip, palette and transparency before every
    /// draw. It is also what says the waveform preview is not animated: its shift register starts
    /// from the same seed on every frame, so a "noise" box that shimmered would fail here.
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSessionAndTheViewState()
    {
        SfxEditorSession session = Sounded();
        var view = new SfxEditorView();
        var screen = new ShellScreen();

        DrawIdle(screen, session, view);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak: another
        // slot, another waveform under the preview, a hovered button with its tooltip up.
        view.SelectSlot(7);
        view.SelectWave(SfxEditorSession.WaveCount - 1);
        SfxEditorRenderer.Draw(screen, session, view, HoverTarget.OfButton(EditorButton.ToolPlay), true);
        view.SelectSlot(0);
        view.SelectWave(0);
        DrawIdle(screen, session, view);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tooltip is TIC-80's, not a popup: hovering a control prints its label into the top
    /// band's free strip instead of covering the instrument with a box, and the label is cut to
    /// what the strip holds. Same mechanism the sprite and map screens use, same single owner of
    /// the cut (<see cref="ConsoleChrome.FitTooltip"/>), so the three console screens cannot grow
    /// three tooltip styles.
    ///
    /// <para>This screen has more controls without buttons than any other — nine of them
    /// (<see cref="SfxRegion"/>) — so the region half of the mechanism is what is probed here:
    /// hovering the pitch grid must put the piano rows' letters in the top band and leave every
    /// pixel of the instrument exactly as it was.</para>
    /// </summary>
    [Fact]
    public void AHoveredControlPrintsItsLabelIntoTheTopBandAndNowhereElse()
    {
        SfxEditorSession session = Sounded();
        var view = new SfxEditorView();
        var screen = new ShellScreen();

        SfxEditorLayout layout = SfxEditorRenderer.Draw(
            screen, session, view, HoverTarget.OfSfxRegion(SfxRegion.Pitch), true);

        Assert.Equal(25, layout.Chrome.TooltipChars);
        Assert.Equal(
            EditorIcons.SfxPitchTooltip[..25],
            layout.Chrome.FitTooltip(EditorIcons.SfxPitchTooltip));
        bool inkInField = false;
        for (int x = layout.Chrome.TooltipField.X; x < layout.Chrome.TooltipField.Right; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                inkInField |= screen.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(inkInField);
        // ...and the instrument is exactly what it was with nothing hovered: no box over it.
        var quiet = new ShellScreen();
        DrawIdle(quiet, session, new SfxEditorView());
        for (int y = layout.Pitch.Y; y < layout.Volume.Bottom; y++)
        {
            for (int x = layout.Pitch.X; x < layout.Pitch.Right; x++)
            {
                Assert.Equal(quiet.Console.Pget(x, y), screen.Console.Pget(x, y));
            }
        }
    }
}
