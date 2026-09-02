using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The shell's half of the display stage: <b>the console's own screens are shown through the
/// identity map, and nothing they do can move an editor golden.</b>
///
/// <para><b>Why this file exists at all.</b> The tool screens and the cartridge go to the window
/// through one road (<see cref="ConsolePresenter"/>), and that road now resolves a pair — the
/// index buffer and the output state it is shown through. Every golden test in this suite hashes
/// the first half only. So a tool screen left tinted by leftover output state would hash exactly
/// right in twenty-six goldens and look wrong on the window: the one class of shell defect the
/// existing suite is blind to by construction. These tests are the sight it is missing.</para>
///
/// <para>Nothing here re-pins or re-derives a screen hash. The assertions are about state that
/// did not exist before this wave, plus one deliberate proof that the old numbers do not move.</para>
/// </summary>
public class ShellDisplayStageTests
{
    private const string IdentityDisplay = "808f7dcc6aaacdd9";

    /// <summary>
    /// A fresh shell screen is at identity, and so is one that has just drawn a real screen: the
    /// shell never sets the stage, so the window shows its framebuffer exactly as it stands.
    ///
    /// <para>Break recipe: have <see cref="MainMenuRenderer"/> call <c>Pald</c> or <c>Palr</c> on
    /// the shell console and leave it set. This test reddens; every golden in the suite stays
    /// green, which is the whole point of writing it.</para>
    /// </summary>
    [Fact]
    public void AToolScreenLeavesTheOutputStageAtIdentity()
    {
        var screen = new ShellScreen();
        Assert.True(screen.Display.IsIdentity);
        Assert.Equal(IdentityDisplay, FrameHash.Of(screen.Display));

        var menu = new MainMenuSession();
        menu.SkipIntro();
        MainMenuRenderer.Draw(screen, menu);

        Assert.True(screen.Display.IsIdentity);
        Assert.Equal(IdentityDisplay, FrameHash.Of(screen.Display));
    }

    /// <summary>
    /// <c>ShellScreen.Begin</c> resets the output stage along with camera, clip and the two
    /// palettes — so one screen's leftovers cannot bend the next screen's colours after a mode
    /// switch. This is the shell's answer to "the stage lives until it is changed": screens do not
    /// share it, because each one starts by clearing it.
    ///
    /// <para>Break recipe: delete the <c>Pald()</c> and <c>Palr()</c> calls from
    /// <c>ShellScreen.Begin</c>. The tint set below survives into the next screen and this test
    /// names it; on the window it would show up as "the sprite editor is green, but only if you
    /// came from the map editor".</para>
    /// </summary>
    [Fact]
    public void BeginClearsTheOutputStageTheWayItClearsTheDrawingState()
    {
        var screen = new ShellScreen();
        VirtualConsole console = screen.Console;

        console.Pald(1, 7, 23);
        console.Palr(0, 45, 1);
        Assert.False(screen.Display.IsIdentity);

        screen.Begin();

        Assert.True(screen.Display.IsIdentity);
        Assert.Equal(IdentityDisplay, FrameHash.Of(screen.Display));
    }

    /// <summary>
    /// <b>The proof the editor goldens cannot move.</b> The same screen is drawn twice, once with
    /// the output stage flooded to a different colour; the framebuffer hash — the number every
    /// golden in this suite quotes — is identical, because the stage writes no pixel. The display
    /// hash is the one that moves.
    ///
    /// <para>Break recipe: apply the display set at write time (inside <c>Plot</c>) instead of at
    /// output time. The framebuffer hashes diverge here first, and then in twenty-six goldens at
    /// once — this test says why in one line instead of leaving twenty-six screens to be
    /// re-derived by hand.</para>
    /// </summary>
    [Fact]
    public void TheOutputStageCannotMoveAScreenHash()
    {
        var menu = new MainMenuSession();
        menu.SkipIntro();

        var plain = new ShellScreen();
        MainMenuRenderer.Draw(plain, menu);
        string plainFrame = FrameHash.Of(plain.Framebuffer);

        var tinted = new ShellScreen();
        MainMenuRenderer.Draw(tinted, menu);
        for (int color = 0; color < Palette.MasterCount; color++)
        {
            tinted.Console.Pald(0, (byte)color, 26);
        }

        Assert.Equal(plainFrame, FrameHash.Of(tinted.Framebuffer));
        Assert.Equal(plain.Framebuffer.Pixels, tinted.Framebuffer.Pixels);
        Assert.NotEqual(FrameHash.Of(plain.Display), FrameHash.Of(tinted.Display));
    }
}
