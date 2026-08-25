using Microsoft.Xna.Framework;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The contract of the host frame (M9 stage 3, the simplification wave):
/// <see cref="EditorChrome"/> measures the bands, the button rows, the margins and the prompt
/// line once, and <see cref="EditorChromeRenderer.ButtonInk"/> decides what colour a button's
/// face is. Two claims are worth a test here — that the frame's own arithmetic is what the shell
/// standard says, and that the screen standing in it really does report THAT frame and not a
/// copy, which is the whole point of the wave and the thing a future review could quietly undo.
///
/// <para><b>By wave R4 that is one screen.</b> ADR-029 has taken the library and four of the
/// five editors onto the console; only the sound screen is left here, and
/// <see cref="TheLastHostFrameScreenStandsInTheFrameItself"/> carries the argument for measuring
/// it against the frame itself now that it has no sibling to be measured against. This file and
/// <see cref="EditorChrome"/> die together on the day that screen moves.</para>
/// </summary>
public class EditorChromeTests
{
    private static readonly EditorButton[] TabsRightToLeft =
    {
        EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
        EditorButton.SpritesTab, EditorButton.CodeTab,
    };

    private static EditorChrome Chrome(int width, int height, params EditorButton?[] status)
    {
        var buttons = new EditorButtonPlace[6 + status.Length];
        int placed = 0;
        EditorChrome chrome = EditorChrome.Compute(width, height, buttons, ref placed, status);
        Assert.Equal(buttons.Length, placed);
        return chrome;
    }

    private static EditorButtonPlace[] Placed(int width, int height, params EditorButton?[] status)
    {
        var buttons = new EditorButtonPlace[6 + status.Length];
        int placed = 0;
        EditorChrome.Compute(width, height, buttons, ref placed, status);
        return buttons;
    }

    /// <summary>
    /// The frame places exit at the left of the top band and then the five editor tabs off the
    /// right corner in the owner's dictated order, and the caller's status row off the right
    /// corner of the bottom band, outermost entry first. Nothing overlaps and everything sits
    /// inside its band.
    ///
    /// <para>Break recipe: swap two entries of <c>_rightTabs</c> in
    /// <see cref="EditorChrome"/>, or change the status loop's <c>- i * (button + gap)</c> to
    /// <c>+ i * (button + gap)</c> — the order assertions go red on both screens at once, which
    /// is the difference this wave bought.</para>
    /// </summary>
    [Fact]
    public void TheFramePlacesTheTabsAndTheStatusRowFromTheRightEdge()
    {
        EditorButtonPlace[] buttons = Placed(1280, 720, EditorButton.Redo, EditorButton.Undo, EditorButton.Save);
        EditorChrome chrome = Chrome(1280, 720, EditorButton.Redo, EditorButton.Undo, EditorButton.Save);

        Assert.Equal(EditorButton.ExitTab, buttons[0].Id);
        Assert.Equal(new Rectangle(chrome.Margin, chrome.Margin, chrome.ButtonSize, chrome.ButtonSize), buttons[0].Rect);
        for (int i = 0; i < TabsRightToLeft.Length; i++)
        {
            Assert.Equal(TabsRightToLeft[i], buttons[1 + i].Id);
            Assert.Equal(
                1280 - chrome.Margin - i * (chrome.ButtonSize + chrome.Gap), buttons[1 + i].Rect.Right);
            Assert.True(chrome.TabStrip.Contains(buttons[1 + i].Rect));
        }

        EditorButton[] status = { EditorButton.Redo, EditorButton.Undo, EditorButton.Save };
        for (int i = 0; i < status.Length; i++)
        {
            EditorButtonPlace place = buttons[6 + i];
            Assert.Equal(status[i], place.Id);
            Assert.Equal(1280 - chrome.Margin - i * (chrome.ButtonSize + chrome.Gap), place.Rect.Right);
            Assert.True(chrome.StatusBar.Contains(place.Rect));
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            for (int j = i + 1; j < buttons.Length; j++)
            {
                Assert.False(buttons[i].Rect.Intersects(buttons[j].Rect));
            }
        }
    }

    /// <summary>
    /// The bands span the whole window and the content bracket sits strictly between them, with
    /// the prompt line reserved above the status band whether or not a prompt is up — which is
    /// what stops a canvas from jumping the frame the author is asked about unsaved work.
    ///
    /// <para>Break recipe: delete the <c>- 2 * ui</c> from <c>ContentBottom</c> and the
    /// "content stops above the prompt line" assertion goes red.</para>
    /// </summary>
    [Theory]
    [InlineData(320, 180)]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(2560, 1440)]
    public void TheBandsSpanTheWindowAndBracketTheContent(int width, int height)
    {
        EditorChrome chrome = Chrome(width, height, EditorButton.Save);

        Assert.Equal(new Rectangle(0, 0, width, chrome.TabStrip.Height), chrome.TabStrip);
        Assert.Equal(width, chrome.StatusBar.Width);
        Assert.Equal(height, chrome.StatusBar.Bottom);
        Assert.Equal(chrome.TabStrip.Height, chrome.StatusBar.Height);
        Assert.Equal(chrome.TabStrip.Bottom + 2 * chrome.Ui, chrome.ContentTop);
        Assert.Equal(chrome.PromptY - 2 * chrome.Ui, chrome.ContentBottom);
        Assert.True(chrome.ContentTop < chrome.ContentBottom);
        Assert.True(chrome.PromptY > chrome.TabStrip.Bottom);
        Assert.True(chrome.PromptY < chrome.StatusBar.Y);
        Assert.Equal(4 * chrome.Ui, chrome.Margin);
        Assert.Equal((EditorIcons.IconPixels + 4) * chrome.Ui, chrome.ButtonSize);
    }

    /// <summary>
    /// The prompt's three verbs run left to right after the heading, do not overlap, and the hit
    /// test returns the verb whose rectangle was hit — the mouse half of Z / X / Esc. The
    /// negative control is the point left of the first verb: on the prompt line, in no verb.
    ///
    /// <para>Break recipe: drop the <c>+ 6 * Ui</c> stride in <c>PromptVerbRect</c> and the
    /// disjointness assertion goes red; drop the heading's width and the negative control does.</para>
    /// </summary>
    [Fact]
    public void ThePromptVerbsRunLeftToRightAndHitTestBack()
    {
        EditorChrome chrome = Chrome(1280, 720, EditorButton.Save);
        Rectangle save = chrome.PromptVerbRect(EditorPromptVerb.SaveAndExit);
        Rectangle discard = chrome.PromptVerbRect(EditorPromptVerb.Discard);
        Rectangle stay = chrome.PromptVerbRect(EditorPromptVerb.Stay);

        Assert.True(save.Right < discard.X);
        Assert.True(discard.Right < stay.X);
        Assert.True(stay.Bottom <= chrome.StatusBar.Y);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Rectangle rect = chrome.PromptVerbRect(verb);
            Assert.True(chrome.TryPromptVerb(rect.Center.X, rect.Center.Y, out EditorPromptVerb hit));
            Assert.Equal(verb, hit);
        }
        Assert.False(chrome.TryPromptVerb(chrome.Margin, chrome.PromptY, out _));
    }

    /// <summary>
    /// A button the screen does not place has no rectangle, and a point on no button hits
    /// nothing — the two halves of the shared hit test. Break recipe: return
    /// <c>default</c> instead of throwing in <see cref="EditorChrome.ButtonRect"/> and the
    /// first assertion goes red instead of a button silently landing at 0,0.
    /// </summary>
    [Fact]
    public void AButtonTheScreenDoesNotPlaceHasNoRectangle()
    {
        EditorButtonPlace[] buttons = Placed(1280, 720, EditorButton.Save);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => EditorChrome.ButtonRect(buttons, EditorButton.ToolEraser));
        Assert.True(EditorChrome.TryButton(buttons, buttons[0].Rect.Center.X, buttons[0].Rect.Center.Y, out EditorButton hit));
        Assert.Equal(EditorButton.ExitTab, hit);
        Assert.False(EditorChrome.TryButton(buttons, 640, 360, out _));
    }

    /// <summary>
    /// <b>The host frame has no editor tenant left, and this is where that is written down.</b>
    /// Sound was the last one; it moved to the console in wave R5, so the test that used to
    /// measure a screen against <see cref="EditorChrome.Compute"/> has no screen to measure.
    /// It is not replaced by a weaker version of itself — a comparison with nothing is not a
    /// test — it is replaced by the fact that made it pointless: the five screens all carry the
    /// console frame's button, and none of them carries the host frame's.
    ///
    /// <para><see cref="EditorChrome"/> itself is still alive for one reason and one only: the
    /// boot menu still paints at the window's resolution through the host font path. When the
    /// menu moves too, the type, its renderer, its icon atlas and this file go together — and
    /// that is the whole remaining debt of ADR-029.</para>
    ///
    /// <para>Break recipe: drag any one screen back into the host frame — its button size stops
    /// being <see cref="ConsoleChrome.ButtonSize"/> and this test names it.</para>
    /// </summary>
    [Fact]
    public void EveryEditorScreenStandsInTheConsoleFrameAndNoneInTheHostFrame()
    {
        var sprite = SpriteEditorLayout.Compute(160, 90, regionCells: 1);
        var map = MapEditorLayout.Compute(160, 90);
        var code = CodeEditorLayout.Compute(160, 90);
        var sfx = SfxEditorLayout.Compute(160, 90);

        Assert.Equal(ConsoleChrome.ButtonSize, sprite.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, map.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, code.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, sfx.ButtonSize);

        // One exit button, one place, on all four screens.
        Rectangle exit = sprite.ButtonRect(EditorButton.ExitTab);
        Assert.Equal(exit, map.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(exit, code.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(exit, sfx.ButtonRect(EditorButton.ExitTab));

        // And the host frame really is a different frame, so nobody can "fix" the difference
        // by dragging a screen back: its button is bigger than the console's whole tab.
        EditorChrome host = Chrome(1280, 720);
        Assert.True(host.ButtonSize > ConsoleChrome.ButtonSize);
    }

    /// <summary>
    /// The painter's one rule that can be pinned without a graphics device: what colour a
    /// button's face is, in precedence order. The interesting cases are the precedences — a
    /// stub tab stays dim on the very screen it names, and a dirty save wins over everything.
    ///
    /// <para><b>The stub arm has nothing to demonstrate it on any more</b>, and that is a
    /// fact worth pinning rather than deleting: the music-editor wave emptied
    /// <see cref="EditorIcons.IsStub"/>, so every button of every screen is live and the arm
    /// is unreachable today. What this test asserts about it is therefore the premise: the
    /// list is empty, and the tab that was last on it now paints bright when active like
    /// every other tab. Put a name back into that list and the arm becomes observable again,
    /// together with the break recipe it used to carry (move it below <c>Active</c> and a
    /// dead button starts looking alive).</para>
    /// </summary>
    [Fact]
    public void ButtonInkFollowsStateInPrecedenceOrder()
    {
        var idle = new EditorButtonState(Active: false, Hovered: false, Dirty: false, CanUndo: true, CanRedo: true);

        Assert.Equal(EditorChromeRenderer.Text, EditorChromeRenderer.ButtonInk(EditorButton.Save, idle));
        Assert.Equal(
            EditorChromeRenderer.Warn,
            EditorChromeRenderer.ButtonInk(EditorButton.Save, idle with { Dirty = true }));
        Assert.Equal(
            EditorChromeRenderer.Dim,
            EditorChromeRenderer.ButtonInk(EditorButton.Undo, idle with { CanUndo = false }));
        Assert.Equal(
            EditorChromeRenderer.Dim,
            EditorChromeRenderer.ButtonInk(EditorButton.Redo, idle with { CanRedo = false }));
        Assert.Equal(
            EditorChromeRenderer.Bright,
            EditorChromeRenderer.ButtonInk(EditorButton.TilemapTab, idle with { Active = true }));
        // No stub is left to be dim: the music tab, the last one on the list, is live now
        // and paints bright when its screen calls it active.
        Assert.False(EditorIcons.IsStub(EditorButton.MusicTab));
        Assert.Equal(
            EditorChromeRenderer.Bright,
            EditorChromeRenderer.ButtonInk(EditorButton.MusicTab, idle with { Active = true }));
        // Hovering changes the frame, never the face.
        Assert.Equal(
            EditorChromeRenderer.ButtonInk(EditorButton.Save, idle),
            EditorChromeRenderer.ButtonInk(EditorButton.Save, idle with { Hovered = true }));
    }
}
