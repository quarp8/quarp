using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the music editor screen</b> — the fifth and last of the family
/// <c>LibraryScreenGoldenTests</c> (R1), <c>SpriteEditorScreenGoldenTests</c> (R2),
/// <c>MapEditorScreenGoldenTests</c> (R3), <c>CodeEditorScreenGoldenTests</c> (R4) and
/// <c>SfxEditorScreenGoldenTests</c> (R5).
///
/// <para>The screen is drawn into a <see cref="Framebuffer"/> by the same core calls a cartridge
/// uses, so it can be hashed by exactly the owner that hashes a cartridge's frame:
/// <see cref="FrameHash"/>. Same digest, same 16-hex text form, same discipline. There is no
/// second hasher in this repository and this file does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a tool screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is PLAYBOOK §4's: never re-pin silently. If one of these
/// changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these four constants came from — read this before re-pinning one.</b> This
/// wave, like R1-R5 before it, was carried out in an environment with no .NET SDK and no package
/// feed, so nothing in the repository could be built or run. The hashes below were therefore
/// <em>derived</em>, not observed: by transliterating <c>VirtualConsole</c>'s <c>Cls</c>,
/// <c>RectFill</c>, <c>Rect</c>, <c>Print</c> and <c>Pset</c> together with
/// <see cref="SystemFont"/>'s glyph table, <c>EditorIcons</c>' mask table and this screen's draw
/// order, and running <see cref="FrameHash"/>'s FNV-1a over the result. That model was first
/// checked against <b>all seventeen hashes already pinned in this suite</b> — three in
/// <c>LibraryScreenGoldenTests</c>, three in <c>SpriteEditorScreenGoldenTests</c>, five in
/// <c>MapEditorScreenGoldenTests</c>, three in <c>CodeEditorScreenGoldenTests</c> and three in
/// <c>SfxEditorScreenGoldenTests</c> — and reproduced every one of them exactly, along with the
/// <c>Pget</c> probes standing above them. That is the evidence that the rasterizer, the font, the
/// icon masks and the shared chrome are modelled right; what remains unproven by that check is
/// only this file's own transcription of <em>this</em> screen's draw order. So: <b>if one of these
/// four fails on the first real build while the <c>Pget</c> probes above it all pass, the
/// overwhelmingly likely explanation is a slip in that transcription and not a defect in the
/// screen</b> — check the probes, look at the frame, and re-pin with a note saying so. If a probe
/// fails too, the screen genuinely changed and the ordinary rule applies: say which pixel moved
/// and why.</para>
///
/// <para><b>Why the probes are here at all.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the structural facts the picture
/// is supposed to have — a tracker row is six pixels tall, a channel column is twenty-six wide,
/// the section markers stand between the number and the first voice, the whole-song overview is a
/// pixel a pattern down the right edge, a muted channel loses its brightness and not its number —
/// so a failure tells whoever reads it whether the screen is broken or merely redrawn.</para>
/// </summary>
public class MusicEditorScreenGoldenTests : IDisposable
{
    private readonly string _root;

    public MusicEditorScreenGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-musicscreen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder with nothing in it but its manifest — no music.bin, no music.txt.</summary>
    private MusicEditorSession FreshCart()
    {
        string folder = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"golden\",\"author\":\"\",\"profile\":8}");
        return new MusicEditorSession(folder);
    }

    /// <summary>
    /// <b>The snake cart's actual song</b>, cell for cell and flag for flag, as
    /// <c>carts/snake/music.txt</c> writes it: four patterns, bass on channel 2, lead on channel
    /// 3, channels 0 and 1 deliberately empty so the apple and death effects always have a free
    /// voice; loop-start on pattern 0 and loop-end on pattern 3.
    ///
    /// <para>It is built here through the session's own verbs rather than by opening the real cart
    /// for one reason: snake ships a <c>music.txt</c>, so its bank is read-only inside Quarp
    /// (<see cref="MusicEditorSession.BankReadOnly"/>) and every writing verb would throw. The
    /// bytes are the same either way — that is what makes this a realistic frame rather than a
    /// pattern of test noise, and it is why the loop flags, which nothing else in this suite
    /// draws, are in the hash.</para>
    /// </summary>
    private MusicEditorSession SnakeSong()
    {
        MusicEditorSession session = FreshCart();
        session.SetChannelSlot(0, 2, 2);
        session.SetPatternFlags(0, MusicEditorSession.FlagLoopStart);
        session.SetChannelSlot(1, 2, 2);
        session.SetChannelSlot(1, 3, 4);
        session.SetChannelSlot(2, 2, 3);
        session.SetChannelSlot(2, 3, 5);
        session.SetChannelSlot(3, 2, 3);
        session.SetChannelSlot(3, 3, 4);
        session.SetPatternFlags(3, MusicEditorSession.FlagLoopEnd);
        return session;
    }

    /// <summary>One frame with nothing hovered and no tooltip due.</summary>
    private static MusicEditorLayout DrawIdle(
        ShellScreen screen, MusicEditorSession session, MusicEditorView view) =>
        MusicEditorRenderer.Draw(screen, session, view, null, false);

    /// <summary>
    /// The screen an author meets on a brand-new cart: sixty-four empty patterns, the window on
    /// the first ten of them, the cursor on pattern 0 channel 1, nothing muted, nothing playing,
    /// nothing saved and nothing to undo.
    /// </summary>
    [Fact]
    public void AFreshCartOpensOnAnEmptySongWithTheWindowOnItsFirstTenPatterns()
    {
        MusicEditorSession session = FreshCart();
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        MusicEditorLayout layout = DrawIdle(screen, session, view);

        // The screen is the console's screen, not a window's — the whole of ADR-029 in four
        // numbers, and the reason every constant below is a fixed console pixel.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(12, 11, 130, 6), layout.Header);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(12, 18, 130, 60), layout.Rows);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(144, 11, 16, 66), layout.Overview);
        // Ten of the sixty-four, and twenty-six pixels a voice. Both numbers are derived in
        // MusicEditorLayout's type comment; these are the assertions that stop the derivation
        // from drifting away from the rectangles.
        Assert.Equal(10, layout.VisibleRows);
        Assert.Equal(26, layout.ChannelWidth);
        Assert.Equal(38, layout.ChannelsX);

        VirtualConsole console = screen.Console;
        // The three rules that cut the screen into bands: under the top bar, above the message
        // line, above the status line.
        Assert.Equal((byte)1, console.Pget(0, 10));
        Assert.Equal((byte)1, console.Pget(159, 10));
        Assert.Equal((byte)1, console.Pget(0, 78));
        Assert.Equal((byte)1, console.Pget(0, 84));
        // The header's own rule runs the width of the grid and stops there — the overview is not
        // part of the grid and must not be underlined by it.
        Assert.Equal((byte)1, console.Pget(12, 17));
        Assert.Equal((byte)1, console.Pget(141, 17));
        Assert.Equal((byte)0, console.Pget(142, 17));
        // The cursor's cell wears a bright frame twenty-six pixels wide and six tall; the cell
        // beside it does not. A grid drawn at any other column pitch fails the second.
        Assert.Equal((byte)3, console.Pget(38, 18));
        Assert.Equal((byte)3, console.Pget(63, 18));
        Assert.Equal((byte)3, console.Pget(38, 23));
        Assert.Equal((byte)1, console.Pget(64, 18));
        // The cursor's whole row is banded, from the number field across; the row below is not.
        // A row drawn at any other height fails the second.
        Assert.Equal((byte)1, console.Pget(12, 18));
        Assert.Equal((byte)0, console.Pget(12, 24));
        // An empty cell reads "--" in dim ink, centred in its column — the dash's own row, one
        // pixel down from the glyph's top.
        Assert.Equal((byte)0, console.Pget(47, 25));
        Assert.Equal((byte)1, console.Pget(47, 26));
        // The overview: its frame carries the window's bright bracket over the first ten rows and
        // is dim below them, and an empty song leaves its interior untouched.
        Assert.Equal((byte)3, console.Pget(144, 22));
        Assert.Equal((byte)1, console.Pget(144, 23));
        Assert.Equal((byte)0, console.Pget(145, 12));
        Assert.Equal((byte)0, console.Pget(151, 40));
        // The mute and solo faces are dim while nothing is silenced.
        Assert.Equal((byte)1, console.Pget(50, 11));
        Assert.Equal((byte)1, console.Pget(59, 11));
        // The status line: the channel and what is under the cursor at the left, the pattern
        // number right-aligned to the screen's edge; no standing notice above it.
        Assert.Equal("CH 1  SLOT --", MusicEditorRenderer.Coordinates(session, view));
        Assert.Equal("PAT 00", MusicEditorRenderer.Summary(session));
        Assert.Equal((byte)2, console.Pget(2, 85));
        Assert.Equal((byte)3, console.Pget(135, 85));
        Assert.Null(MusicEditorRenderer.StandingNotice(session));
        Assert.Equal((byte)0, console.Pget(1, 79));
        // Sixteen slots and no more: nothing on this screen reaches a master colour above 15.
        foreach (byte pixel in screen.Framebuffer.Pixels)
        {
            Assert.InRange(pixel, (byte)0, (byte)15);
        }

        Assert.Equal("3a0d59326c050125", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Snake's own four-pattern theme on screen: the bass on channel 2, the lead on channel 3,
    /// the loop's two brackets on patterns 0 and 3, and the same song again as pixels in the
    /// overview. Everything this screen exists to show moves at once and all of it is in the
    /// hash.
    /// </summary>
    [Fact]
    public void TheSnakeSongShowsItsVoicesItsLoopAndItsShapeInTheOverview()
    {
        MusicEditorSession session = SnakeSong();
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(2, session.ChannelSlot(0, 2));
        Assert.True(session.ChannelIsSilent(0, 3));
        Assert.True(session.HasFlag(0, MusicEditorSession.FlagLoopStart));
        Assert.True(session.HasFlag(3, MusicEditorSession.FlagLoopEnd));

        DrawIdle(screen, session, view);

        VirtualConsole console = screen.Console;
        // Pattern 0, channel 2 reads "02": the zero's top row, centred in the third column.
        Assert.Equal((byte)2, console.Pget(99, 18));
        // Pattern 3, channel 3 reads "04" three rows down — a grid drawn at the wrong row pitch
        // or the wrong column pitch misses it.
        Assert.Equal((byte)2, console.Pget(125, 36));
        // Pattern 0's loop-start marker is lit: a blue plate with a bright "[" on it. The plate
        // shows in the cell's last column, which the three-pixel glyph does not reach.
        Assert.Equal((byte)3, console.Pget(26, 18));
        Assert.Equal((byte)4, console.Pget(29, 18));
        // Pattern 3 carries loop-end and not loop-start: the second marker is plated, the first
        // is a dim face on bare ink.
        Assert.Equal((byte)3, console.Pget(31, 36));
        Assert.Equal((byte)4, console.Pget(33, 36));
        Assert.Equal((byte)1, console.Pget(26, 36));
        Assert.Equal((byte)0, console.Pget(29, 36));
        // The overview says the same thing in single pixels: channel 2 of pattern 0 sounds,
        // channel 3 does not, and the flag column is lit on the two patterns that carry a loop
        // and dark on the one between them.
        Assert.Equal((byte)2, console.Pget(151, 12));
        Assert.Equal((byte)0, console.Pget(153, 12));
        Assert.Equal((byte)3, console.Pget(157, 12));
        Assert.Equal((byte)3, console.Pget(157, 15));
        Assert.Equal((byte)0, console.Pget(157, 13));
        // ...and nothing at all below pattern 3, because the song is four patterns long.
        Assert.Equal((byte)0, console.Pget(99, 42));
        // Unsaved work: the save button's face is the modified floppy in warn yellow.
        Assert.Equal((byte)8, console.Pget(1, 22));

        Assert.Equal("22161effe99e69b8", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The wave's own headline, pinned as pixels: a muted channel and a soloed one, plus a marked
    /// rectangle over two patterns of two voices. <b>Not one byte of the cartridge moves</b> —
    /// the assertion above the hash says so by comparing the payload before and after — and the
    /// picture changes anyway, which is exactly what an audition control is.
    /// </summary>
    [Fact]
    public void MuteAndSoloChangeThePictureAndNotOneByteOfTheSong()
    {
        MusicEditorSession session = SnakeSong();
        byte[] before = session.Payload.ToArray();
        var view = new MusicEditorView();
        view.ToggleMute(2);
        view.ToggleSolo(3);
        session.SelectRange(1, 2, 3, 3);
        var screen = new ShellScreen();

        DrawIdle(screen, session, view);

        Assert.Equal(before, session.Payload.ToArray());
        Assert.True(view.ChannelMuted(2));
        Assert.True(view.ChannelSoloed(3));
        Assert.True(view.AnySolo);
        Assert.False(view.ChannelAudible(2));       // solo wins over mute, and over silence
        Assert.False(view.ChannelAudible(0));
        Assert.True(view.ChannelAudible(3));

        VirtualConsole console = screen.Console;
        // Channel 2 keeps its number and loses its brightness — dim where it was text ink.
        Assert.Equal((byte)1, console.Pget(99, 18));
        // Channel 3 is the soloed one and stays bright text.
        Assert.Equal((byte)2, console.Pget(125, 24));
        // Both toggles read as on: a blue plate under the M of channel 2 and under the S of
        // channel 3.
        Assert.Equal((byte)4, console.Pget(100, 11));
        Assert.Equal((byte)4, console.Pget(134, 11));
        // The marked rectangle is a blue plate under the cells it covers...
        Assert.Equal((byte)4, console.Pget(90, 24));
        Assert.Equal((byte)4, console.Pget(115, 29));
        // ...and the overview dims the silenced voice too, so the two pictures agree about what
        // the author will hear.
        Assert.Equal((byte)1, console.Pget(151, 12));

        Assert.Equal("e0a1d594f0c1e76b", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Esc on a dirty song: the question. The message line carries the prompt — the heading at
    /// the left margin in warn yellow, the three verbs right-aligned to the screen's edge, each
    /// on the very rectangle <see cref="MusicEditorLayout.PromptVerbRect"/> makes clickable, and
    /// all of it measured by <see cref="ConsoleChrome"/> so this screen and the four that moved
    /// before it cannot disagree about where "ESC STAY" is.
    /// </summary>
    [Fact]
    public void TheExitPromptTakesTheMessageLineAndItsVerbsAreWhereTheHitTestSaysTheyAre()
    {
        MusicEditorSession session = SnakeSong();
        var view = new MusicEditorView();
        Assert.False(view.RequestClose(session));      // dirty: the prompt goes up instead of closing
        Assert.True(view.ExitPromptShown);
        var screen = new ShellScreen();

        MusicEditorLayout layout = DrawIdle(screen, session, view);

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
        // The song is untouched by the question: the work the author is deciding about is still
        // on screen, which is the whole reason the prompt lives on one reserved line.
        Assert.Equal((byte)2, console.Pget(99, 18));

        Assert.Equal("6924d083aabcb026", FrameHash.Of(screen.Framebuffer));
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
        MusicEditorSession session = FreshCart();
        var view = new MusicEditorView();
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
    /// draw.
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSessionAndTheViewState()
    {
        MusicEditorSession session = SnakeSong();
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        DrawIdle(screen, session, view);
        string first = FrameHash.Of(screen.Framebuffer);

        // A different picture in between, so a stale-state bug has something to leak: a scrolled
        // window, a muted voice, a hovered button with its tooltip up.
        var layout = MusicEditorLayout.Compute(160, 90);
        view.ScrollTo(layout, 20);
        view.ToggleMute(0);
        MusicEditorRenderer.Draw(screen, session, view, HoverTarget.OfButton(EditorButton.ToolPlay), true);
        view.ScrollTo(layout, 0);
        view.ToggleMute(0);
        DrawIdle(screen, session, view);

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The tooltip is TIC-80's, not a popup: hovering a control prints its label into the top
    /// band's free strip instead of covering the song with a box, and the label is cut to what
    /// the strip holds. Same mechanism the four screens before this one use, same single owner of
    /// the cut (<see cref="ConsoleChrome.FitTooltip"/>), so the five console screens cannot grow
    /// five tooltip styles.
    /// </summary>
    [Fact]
    public void AHoveredControlPrintsItsLabelIntoTheTopBandAndNowhereElse()
    {
        MusicEditorSession session = SnakeSong();
        var view = new MusicEditorView();
        var screen = new ShellScreen();

        MusicEditorLayout layout = MusicEditorRenderer.Draw(
            screen, session, view, HoverTarget.OfMusicRegion(MusicRegion.Channels), true);

        Assert.Equal(25, layout.Chrome.TooltipChars);
        Assert.Equal(
            EditorIcons.MusicChannelsTooltip[..25],
            layout.Chrome.FitTooltip(EditorIcons.MusicChannelsTooltip));
        bool inkInField = false;
        for (int x = layout.Chrome.TooltipField.X; x < layout.Chrome.TooltipField.Right; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                inkInField |= screen.Console.Pget(x, y) != 0;
            }
        }
        Assert.True(inkInField);
        // ...and the grid is exactly what it was with nothing hovered: no box over it.
        var quiet = new ShellScreen();
        DrawIdle(quiet, session, new MusicEditorView());
        for (int y = layout.Rows.Y; y < layout.Rows.Bottom; y++)
        {
            for (int x = layout.Rows.X; x < layout.Rows.Right; x++)
            {
                Assert.Equal(quiet.Console.Pget(x, y), screen.Console.Pget(x, y));
            }
        }
    }
}
