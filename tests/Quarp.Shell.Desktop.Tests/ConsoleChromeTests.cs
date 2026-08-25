using Microsoft.Xna.Framework;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The contract of the console frame — the one frame in the tree since wave R6.
///
/// <para><b>This file is the surviving half of <c>EditorChromeTests</c>, and the paragraph
/// explaining that is the point of it.</b> That file pinned a <em>host-resolution</em> frame:
/// bands measured in window pixels at a 320x180 text anchor, a button of
/// <c>(8 + 4) * ui</c> px, a prompt line whose three verbs ran left to right after a
/// 16-character heading, and a tooltip drawn as a bordered box under the pointer. Every one of
/// those facts was about a screen that no longer exists — ADR-029 moved the library (R1), the
/// five editors (R2-R5) and finally the boot menu (R6) onto the console, and the host frame,
/// its painter and both host atlases were deleted with the last of them. Four of that file's
/// six tests therefore had no subject left and are gone rather than rewritten against a
/// smaller frame: a test whose object was deleted is not evidence about the object that
/// replaced it. What is <b>kept</b> is the two claims that outlived the move because they were
/// never about window pixels — the shared button hit test and the button-ink ladder — plus the
/// tab-order ownership, restated for its new home. Nothing here is a weaker version of a host
/// assertion; the numbers all come from <see cref="ConsoleChrome"/>, which is where they were
/// already coming from for every screen.</para>
/// </summary>
public class ConsoleChromeTests
{
    private const int ScreenWidth = 160;
    private const int ScreenHeight = 90;

    private static readonly EditorButton[] TabsRightToLeft =
    {
        EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
        EditorButton.SpritesTab, EditorButton.CodeTab,
    };

    private static EditorButtonPlace[] Placed()
    {
        var buttons = new EditorButtonPlace[6];
        int placed = 0;
        ConsoleChrome.Compute(ScreenWidth, ScreenHeight, buttons, ref placed);
        Assert.Equal(buttons.Length, placed);
        return buttons;
    }

    /// <summary>
    /// The frame places exit at the left corner of the top band and then the five editor tabs
    /// off the right corner in the owner's dictated order, and nothing overlaps anything.
    ///
    /// <para>Break recipe: swap two entries of <c>_rightTabs</c> in
    /// <see cref="ConsoleChrome"/> and the order assertions go red on every screen at once —
    /// which is what having one list instead of six is for.</para>
    /// </summary>
    [Fact]
    public void TheFramePlacesTheTabsFromTheRightEdgeInTheOwnersOrder()
    {
        EditorButtonPlace[] buttons = Placed();

        Assert.Equal(EditorButton.ExitTab, buttons[0].Id);
        Assert.Equal(new Rectangle(0, 0, ConsoleChrome.ButtonSize, ConsoleChrome.ButtonSize), buttons[0].Rect);
        for (int i = 0; i < TabsRightToLeft.Length; i++)
        {
            Assert.Equal(TabsRightToLeft[i], buttons[1 + i].Id);
            Assert.Equal(ScreenWidth - i * ConsoleChrome.ButtonSize, buttons[1 + i].Rect.Right);
            Assert.Equal(0, buttons[1 + i].Rect.Y);
        }
        Assert.Equal(TabsRightToLeft, ConsoleChrome.RightTabs);

        for (int i = 0; i < buttons.Length; i++)
        {
            for (int j = i + 1; j < buttons.Length; j++)
            {
                Assert.False(buttons[i].Rect.Intersects(buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// A button the screen does not place has no rectangle, and a point on no button hits
    /// nothing — the two halves of the shared hit test, which every screen reaches through its
    /// own <c>ButtonRect</c> / <c>TryButton</c>.
    ///
    /// <para>This is one of the two assertions carried over from <c>EditorChromeTests</c>
    /// unchanged in substance. It could be, because the loop it tests did not change when the
    /// host frame died: <see cref="ConsoleChrome.ButtonRect"/> used to be a forwarder into that
    /// frame and is now the implementation itself. Break recipe: return <c>default</c> instead
    /// of throwing and the first assertion goes red instead of a button silently landing at
    /// 0,0.</para>
    /// </summary>
    [Fact]
    public void AButtonTheScreenDoesNotPlaceHasNoRectangle()
    {
        EditorButtonPlace[] buttons = Placed();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConsoleChrome.ButtonRect(buttons, EditorButton.ToolEraser));
        Assert.True(ConsoleChrome.TryButton(
            buttons, buttons[0].Rect.Center.X, buttons[0].Rect.Center.Y, out EditorButton hit));
        Assert.Equal(EditorButton.ExitTab, hit);
        Assert.False(ConsoleChrome.TryButton(buttons, ScreenWidth / 2, ScreenHeight / 2, out _));
    }

    /// <summary>
    /// The prompt's three verbs are right-aligned to the screen's edge, do not overlap, and the
    /// hit test returns the verb whose rectangle was hit — the mouse half of Z / X / Esc. The
    /// negative control is a point on the message line left of the first verb: on the line, in
    /// no verb.
    ///
    /// <para>Break recipe: drop the <c>VerbGap</c> stride in <c>PromptVerbRect</c> and the
    /// disjointness assertion goes red.</para>
    /// </summary>
    [Fact]
    public void ThePromptVerbsAreRightAlignedAndHitTestBack()
    {
        var buttons = new EditorButtonPlace[6];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(ScreenWidth, ScreenHeight, buttons, ref placed);

        Rectangle save = chrome.PromptVerbRect(EditorPromptVerb.SaveAndExit);
        Rectangle discard = chrome.PromptVerbRect(EditorPromptVerb.Discard);
        Rectangle stay = chrome.PromptVerbRect(EditorPromptVerb.Stay);

        Assert.True(save.Right < discard.X);
        Assert.True(discard.Right < stay.X);
        Assert.Equal(ScreenWidth - ConsoleChrome.Margin, stay.Right);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Rectangle rect = chrome.PromptVerbRect(verb);
            Assert.True(chrome.TryPromptVerb(rect.Center.X, rect.Center.Y, out EditorPromptVerb got));
            Assert.Equal(verb, got);
        }
        Assert.False(chrome.TryPromptVerb(ConsoleChrome.Margin, chrome.MessageY, out _));
    }

    /// <summary>
    /// <b>Every screen stands in this frame, and there is no other frame to stand in.</b> Wave
    /// R6 took the last host-resolution screen (the boot menu), so the assertion that used to
    /// end this file — "the host frame really is a different frame, and its button is bigger
    /// than the console's whole tab" — has nothing left to compare against and is deleted
    /// rather than kept as a comparison with a constant. What replaces it is the fact that made
    /// it pointless: the five editor screens all report the console frame's button and all put
    /// exit on the same pixels.
    ///
    /// <para>Break recipe: give any one screen a button size of its own and this test names
    /// it.</para>
    /// </summary>
    [Fact]
    public void EveryEditorScreenStandsInThisFrameAndPutsExitOnTheSamePixels()
    {
        var sprite = SpriteEditorLayout.Compute(ScreenWidth, ScreenHeight, regionCells: 1);
        var map = MapEditorLayout.Compute(ScreenWidth, ScreenHeight);
        var code = CodeEditorLayout.Compute(ScreenWidth, ScreenHeight);
        var sfx = SfxEditorLayout.Compute(ScreenWidth, ScreenHeight);
        var music = MusicEditorLayout.Compute(ScreenWidth, ScreenHeight);

        Assert.Equal(ConsoleChrome.ButtonSize, sprite.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, map.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, code.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, sfx.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, music.ButtonSize);

        Rectangle exit = sprite.ButtonRect(EditorButton.ExitTab);
        Assert.Equal(exit, map.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(exit, code.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(exit, sfx.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(exit, music.ButtonRect(EditorButton.ExitTab));
    }

    /// <summary>
    /// The painter's one rule that can be pinned without any pixels: what colour a button's
    /// face is, in precedence order. The second assertion carried over from
    /// <c>EditorChromeTests</c> — the ladder is the same ladder, and the only thing wave R6
    /// changed about it is that the host copy that used to sit beside this one is gone, so the
    /// answer is a palette slot rather than an unpacked colour.
    ///
    /// <para><b>The stub arm has nothing to demonstrate it on any more</b>, and that is a fact
    /// worth pinning rather than deleting: the music-editor wave emptied
    /// <see cref="EditorIcons.IsStub"/>, so every button of every screen is live and the arm is
    /// unreachable today. What this test asserts about it is therefore the premise: the list is
    /// empty, and the tab that was last on it now paints bright when active like every other
    /// tab. Put a name back into that list and the arm becomes observable again, together with
    /// the break recipe it used to carry (move it below <c>Active</c> and a dead button starts
    /// looking alive).</para>
    /// </summary>
    [Fact]
    public void ButtonInkFollowsStateInPrecedenceOrder()
    {
        var idle = new EditorButtonState(Active: false, Hovered: false, Dirty: false, CanUndo: true, CanRedo: true);

        Assert.Equal(ConsoleChromeRenderer.Text, ConsoleChromeRenderer.ButtonInk(EditorButton.Save, idle));
        Assert.Equal(
            ConsoleChromeRenderer.Warn,
            ConsoleChromeRenderer.ButtonInk(EditorButton.Save, idle with { Dirty = true }));
        Assert.Equal(
            ConsoleChromeRenderer.Dim,
            ConsoleChromeRenderer.ButtonInk(EditorButton.Undo, idle with { CanUndo = false }));
        Assert.Equal(
            ConsoleChromeRenderer.Dim,
            ConsoleChromeRenderer.ButtonInk(EditorButton.Redo, idle with { CanRedo = false }));
        Assert.Equal(
            ConsoleChromeRenderer.Bright,
            ConsoleChromeRenderer.ButtonInk(EditorButton.TilemapTab, idle with { Active = true }));
        // No stub is left to be dim: the music tab, the last one on the list, is live now
        // and paints bright when its screen calls it active.
        Assert.False(EditorIcons.IsStub(EditorButton.MusicTab));
        Assert.Equal(
            ConsoleChromeRenderer.Bright,
            ConsoleChromeRenderer.ButtonInk(EditorButton.MusicTab, idle with { Active = true }));
        // Hovering changes the frame, never the face.
        Assert.Equal(
            ConsoleChromeRenderer.ButtonInk(EditorButton.Save, idle),
            ConsoleChromeRenderer.ButtonInk(EditorButton.Save, idle with { Hovered = true }));
    }
}
