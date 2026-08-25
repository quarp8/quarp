using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>The golden master of the console's first screen</b> — the thing wave R6 was worth doing
/// for.
///
/// <para>Until this wave the boot screen was painted at the window's native resolution through
/// a <c>SpriteBatch</c>, a host font atlas and a <c>Texture2D</c> of the wordmark, and there
/// was no artefact of it a test could look at: no buffer, no pixels, nothing but draw calls
/// into a graphics device no headless runner has. That made the <em>first thing every player
/// ever sees</em> the least tested surface in the solution — a door on the wrong row, a footer
/// running off the edge, a message line landing on top of a door (which is exactly what the
/// host layout did on 90 real rows, see <see cref="MainMenuLayout"/>) were all undetectable by
/// every test here. Now the screen is drawn into a <see cref="Framebuffer"/> by the same core
/// calls a cartridge uses, so it can be hashed by exactly the owner that hashes a cartridge's
/// frame: <see cref="FrameHash"/>. Same digest, same 16-hex text form, same discipline as
/// <c>quarp sim</c>, the replay tests and <c>scripts/check-anchors.sh</c>. There is no second
/// hasher in this repository and this file does not introduce one.</para>
///
/// <para><b>These constants are not determinism anchors.</b> The eight anchors and the twelve
/// demo hashes are facts about the <em>cartridge's</em> console and must never move without a
/// verdict. These are facts about a shell screen's layout, they belong to no cross-architecture
/// promise, and they are expected to move whenever the screen is deliberately redesigned. The
/// discipline that does carry over is the one from PLAYBOOK §4: never re-pin silently. If one
/// of these changes, the commit message says which pixel moved and why.</para>
///
/// <para><b>Where these five constants came from</b> — read this before re-pinning one. Wave R6
/// was carried out in an environment with no .NET SDK and no package feed, so nothing in the
/// repository could be built or run. The hashes below were therefore <em>derived</em>, not
/// observed: by transliterating <c>VirtualConsole</c>'s <c>Cls</c>, <c>RectFill</c>,
/// <c>Print</c>, <c>Pset</c> and <c>Plot</c> together with <see cref="SystemFont"/>'s glyph
/// table, <see cref="MenuArt"/>'s grid and this screen's draw order, and running
/// <see cref="FrameHash"/>'s FNV-1a over the result. That is a model of the rasterizer, and a
/// model can be wrong where the original is right — so before it was used to derive anything
/// new it was made to reproduce <b>all twenty-one screen hashes already pinned in this suite</b>
/// (library 3, sprites 3, tilemap 5, code 3, sound 3, music 4), and it reproduced every one.
/// If one of these five nevertheless fails on the first real build while the probe assertions
/// above it all pass, the likeliest explanation by far is a slip in that derivation and not a
/// defect in the screen: check the probes, look at the frame, and re-pin with a note saying so.
/// If a probe fails too, the screen genuinely changed and the ordinary rule applies — say which
/// pixel moved and why.</para>
///
/// <para><b>Why the probes are here too.</b> A bare hash mismatch says "something moved" and
/// nothing else. The <c>Pget</c> assertions above each hash name the specific structural facts
/// the picture is supposed to have — the bar is on the selected door and only there, the wall's
/// tenth column is exactly ten pixels tall, the message line never reaches a door — so a
/// failure tells whoever reads it whether the screen is broken or merely redrawn.</para>
///
/// <para><b>Why the intro can be pinned at all.</b> It is a pure function of
/// <see cref="MainMenuSession.IntroClock"/>, which is plain elapsed seconds the caller banks —
/// no wall clock, no frame counter, no graphics device. Two moments are pinned: one inside
/// beat 1, where the sixteen palette columns are at sixteen different heights and an off-by-one
/// in the stagger would show; and one after beat 3, which is the frame the menu cuts from. The
/// first is taken at 0.20 s deliberately: every column height there is an exact double or a
/// hair above one (90, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0…), so the truncation to whole
/// pixels is not sitting on a knife edge that a different rounding could tip.</para>
/// </summary>
public class MainMenuScreenGoldenTests
{
    /// <summary>A session parked mid-intro at a chosen clock, with no key ever pressed.</summary>
    private static MainMenuSession Intro(double seconds)
    {
        var menu = new MainMenuSession();
        menu.AdvanceIntro(seconds, anyInputDown: false);
        Assert.Equal(MenuPhase.Intro, menu.Phase);
        Assert.Equal(seconds, menu.IntroClock);
        return menu;
    }

    /// <summary>A session on the menu proper, intro skipped the way Esc skips it.</summary>
    private static MainMenuSession Menu()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();
        return menu;
    }

    /// <summary>
    /// Beat 1: the palette introduces itself. Sixteen columns, one per visible slot, each ten
    /// pixels wide, racing down with a two-hundredth-of-a-second stagger. At 0.20 s the first
    /// three are full, the next six are a descending staircase, and the last six have not
    /// started.
    /// </summary>
    [Fact]
    public void TheIntroOpensWithThePaletteFallingInColumns()
    {
        var screen = new ShellScreen();

        MainMenuLayout layout = MainMenuRenderer.Draw(screen, Intro(0.20));

        // The screen is the console's screen, not a window's.
        Assert.Equal(160, screen.Framebuffer.Width);
        Assert.Equal(90, screen.Framebuffer.Height);
        Assert.Equal(10, layout.WallColumnWidth);

        VirtualConsole console = screen.Console;
        // Column 1 (x 10..19) has reached the bottom; column 2 (x 20..29) stops at row 79;
        // column 9 (x 90..99) is ten pixels tall; column 10 has not begun.
        Assert.Equal((byte)1, console.Pget(10, 0));
        Assert.Equal((byte)1, console.Pget(19, 89));
        Assert.Equal((byte)2, console.Pget(29, 79));
        Assert.Equal((byte)0, console.Pget(29, 80));
        Assert.Equal((byte)9, console.Pget(99, 9));
        Assert.Equal((byte)0, console.Pget(99, 10));
        Assert.Equal((byte)0, console.Pget(100, 0));
        // Beat 2 has not started, so no wordmark colour is on screen yet: MenuArt's five slots
        // are 3, 5, 7, 8 and 11, and 11 is reachable only from the art.
        Assert.DoesNotContain((byte)11, screen.Framebuffer.Pixels);

        Assert.Equal("7ff08f860fd53839", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The intro's last frame: the wall is gone, the wordmark is fully wiped in and centred,
    /// the tagline has landed under it. This is the picture the menu cuts from, so a skipped
    /// intro and an expired one land on the same pixels — which the next test's premise
    /// depends on.
    /// </summary>
    [Fact]
    public void TheIntroSettlesOnTheWordmarkAndTheTagline()
    {
        var screen = new ShellScreen();

        MainMenuLayout layout = MainMenuRenderer.Draw(screen, Intro(1.6));

        VirtualConsole console = screen.Console;
        Assert.Equal(43, layout.IntroLogoX);            // (160 - 73) / 2
        Assert.Equal(35, layout.IntroLogoY);
        Assert.Equal(53, layout.IntroTaglineY);
        // The row above the wordmark is untouched ink from edge to edge — the wall has left.
        for (int x = 0; x < screen.Width; x++)
        {
            Assert.Equal((byte)0, console.Pget(x, layout.IntroLogoY - 1));
        }
        // The wordmark's own white and its pink tile are both present; the tagline is green.
        Assert.Contains((byte)3, screen.Framebuffer.Pixels);
        Assert.Contains((byte)11, screen.Framebuffer.Pixels);
        Assert.Equal((byte)7, console.Pget(44, 54));

        Assert.Equal("f9869c243d3a80e1", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The everyday picture: the three doors with the bar on the first, no message, no field.
    /// The two reserved lines below the doors are empty and stay reserved — a screen that
    /// re-flowed when an error appeared would move the doors under the pointer.
    /// </summary>
    [Fact]
    public void TheMenuWithTheBarOnTheFirstDoor()
    {
        var screen = new ShellScreen();

        MainMenuLayout layout = MainMenuRenderer.Draw(screen, Menu());

        VirtualConsole console = screen.Console;
        // Door 1 (rows 41..47) carries the bar; doors 2 and 3 do not, and neither does the
        // two-pixel gutter between them.
        Assert.Equal((byte)5, console.Pget(1, 41));
        Assert.Equal((byte)5, console.Pget(158, 47));
        Assert.Equal((byte)0, console.Pget(0, 41));
        Assert.Equal((byte)0, console.Pget(159, 41));
        Assert.Equal((byte)0, console.Pget(1, 48));
        Assert.Equal((byte)0, console.Pget(1, 50));
        Assert.Equal((byte)0, console.Pget(1, 59));
        // The selected door's digit is painted in ink ON the bar, not in text grey.
        Assert.Equal((byte)0, console.Pget(7, 42));
        // The footer rule runs margin to margin and no further.
        Assert.Equal((byte)1, console.Pget(2, 81));
        Assert.Equal((byte)1, console.Pget(157, 81));
        Assert.Equal((byte)0, console.Pget(158, 81));
        // Both reserved lines are empty.
        for (int x = 0; x < screen.Width; x++)
        {
            Assert.Equal((byte)0, console.Pget(x, layout.MessageY));
            Assert.Equal((byte)0, console.Pget(x, layout.EntryY));
        }

        Assert.Equal("5136925b85fcc754", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The name field, up, with a name half typed. The bar disappears while the field owns the
    /// keyboard — the doors underneath are deliberately deaf, and a bar on a door nobody can
    /// walk through would say otherwise.
    /// </summary>
    [Fact]
    public void TheNameFieldTakesTheKeyboardAndTheBarGoesAway()
    {
        MainMenuSession menu = Menu();
        menu.BeginNameEntry();
        foreach (char c in "newborn")
        {
            menu.TypeChar(c);
        }
        var screen = new ShellScreen();

        MainMenuLayout layout = MainMenuRenderer.Draw(screen, menu);

        VirtualConsole console = screen.Console;
        // No bar on any door.
        Assert.Equal((byte)0, console.Pget(1, 41));
        Assert.Equal((byte)0, console.Pget(1, 50));
        Assert.Equal((byte)0, console.Pget(1, 59));
        // The caption is in the spec labels' yellow, the typed name in white.
        Assert.Equal((byte)8, console.Pget(2, layout.EntryY));
        Assert.Equal(26, layout.NameTextX);
        Assert.Equal((byte)3, console.Pget(26, layout.EntryY + 1));
        // The cursor is the eighth cell: seven letters, then the underscore's bottom rule.
        Assert.Equal((byte)3, console.Pget(54, layout.EntryY + 4));
        Assert.Equal((byte)0, console.Pget(57, layout.EntryY + 4));

        Assert.Equal("34c34ffb3bb8963c", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// A refusal on the message line, with the bar on the second door. The message is red, it
    /// lives on its own row, and <b>it does not touch a door</b> — the one thing the host
    /// layout got wrong on 90 rows and the reason this line was given a row of its own.
    /// </summary>
    [Fact]
    public void AMessageGetsItsOwnRowAndNeverReachesADoor()
    {
        MainMenuSession menu = Menu();
        menu.MoveSelection(+1);
        menu.Message = "THAT NAME ALREADY EXISTS";
        var screen = new ShellScreen();

        MainMenuLayout layout = MainMenuRenderer.Draw(screen, menu);

        VirtualConsole console = screen.Console;
        Assert.Equal((byte)5, console.Pget(1, 50));
        Assert.Equal((byte)0, console.Pget(1, 41));
        // The message row holds ink and the error colour and nothing else.
        var seen = new HashSet<byte>();
        for (int y = layout.MessageY; y < layout.MessageY + SystemFont.GlyphHeight; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                seen.Add(console.Pget(x, y));
            }
        }
        Assert.Equal(new byte[] { 0, 10 }, seen.Order().ToArray());
        // The last door's bar ends two rows above the message: they cannot meet.
        Assert.True(layout.Row(MainMenuSession.ItemCount - 1).Bottom < layout.MessageY);

        Assert.Equal("79f33cb4f8479a55", FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// Two consoles, two framebuffers. The shell's screen and a cartridge's are separate
    /// instances by construction (<see cref="ShellScreen"/>), and this is the assertion that
    /// says so out loud for the boot screen: drawing the whole menu leaves a console built the
    /// same way untouched. It is the property that keeps anything the shell draws out of the
    /// buffer the CI hashes.
    /// </summary>
    [Fact]
    public void DrawingTheBootScreenTouchesNoOtherConsole()
    {
        var shell = new ShellScreen();
        var other = new ShellScreen();
        string before = FrameHash.Of(other.Framebuffer);

        MainMenuRenderer.Draw(shell, Menu());

        Assert.Equal(before, FrameHash.Of(other.Framebuffer));
        Assert.NotEqual(before, FrameHash.Of(shell.Framebuffer));
        Assert.NotSame(shell.Framebuffer, other.Framebuffer);
    }

    /// <summary>
    /// Redrawing the same state gives the same bytes: the screen depends on the session and on
    /// nothing else — no wall clock, no window size, no leftover console state. That is what
    /// makes a pinned hash meaningful rather than lucky, and it is why
    /// <c>ShellScreen.Begin</c> resets camera, clip, palette and transparency before every draw.
    ///
    /// <para>The same assertion also carries the intro's premise: a skipped intro and an expired
    /// one land on the same pixels, because both leave the session in the same phase and the
    /// menu reads no clock at all.</para>
    /// </summary>
    [Fact]
    public void TheScreenIsAPureFunctionOfTheSession()
    {
        var screen = new ShellScreen();

        MainMenuRenderer.Draw(screen, Menu());
        string first = FrameHash.Of(screen.Framebuffer);

        // Different pictures in between, so a stale-state bug has something to leak.
        MainMenuRenderer.Draw(screen, Intro(0.20));
        MainMenuSession noisy = Menu();
        noisy.Message = "boom";
        MainMenuRenderer.Draw(screen, noisy);
        MainMenuRenderer.Draw(screen, Menu());

        Assert.Equal(first, FrameHash.Of(screen.Framebuffer));

        // A session that ran its intro out lands on the very same menu as one that skipped it.
        var expired = new MainMenuSession();
        for (int i = 0; i < 120; i++)
        {
            expired.AdvanceIntro(1.0 / 60, anyInputDown: false);
        }
        Assert.Equal(MenuPhase.Menu, expired.Phase);
        var second = new ShellScreen();
        MainMenuRenderer.Draw(second, expired);
        Assert.Equal(first, FrameHash.Of(second.Framebuffer));
    }
}
