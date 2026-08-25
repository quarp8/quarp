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
    /// The one screen still standing in the HOST frame reports that frame and not a copy of it —
    /// the same scale, margin, button size, bands, prompt line, prompt verbs and tab rectangles
    /// <see cref="EditorChrome.Compute"/> produces on its own. This is the simplification wave's
    /// whole claim, and the one a future review could undo without any screen looking wrong on
    /// its own.
    ///
    /// <para><b>Re-pinned three times now, and this paragraph is why.</b> The test began by
    /// comparing four screens with each other. Wave R2 moved the sprite editor onto the console
    /// (ADR-029), R3 the map, R4 the code editor — so the sound screen is the last tenant, and
    /// "compare it with its sibling" has no sibling left to name. Rather than delete the
    /// instrument at the exact moment the frame has one user (which is when a second owner of it
    /// is easiest to introduce and hardest to notice), it now measures that screen against the
    /// frame's own owner: a bare <see cref="EditorChrome"/> computed here from nothing but a
    /// window size. That is strictly the stronger question — two screens can agree with each
    /// other and both be wrong about the chrome — and it is the question that stays askable
    /// until the day the sound screen moves and this test dies with
    /// <see cref="EditorChrome"/> itself.</para>
    ///
    /// <para><b>Save, Undo and Redo left the comparison with the code screen.</b> They are chrome
    /// only in the host frame, where <see cref="EditorChrome.Compute"/> right-aligns them into
    /// the status band from a slot list each screen passes in. The slot list is the screen's, not
    /// the frame's, so a bare chrome cannot produce their rectangles and asserting them here
    /// would mean re-deriving the frame's arithmetic in a test — a second owner in the very file
    /// that exists to forbid one. Their placement stays covered by the sound screen's own
    /// disjointness test.</para>
    ///
    /// <para>Break recipe: give <see cref="SfxEditorLayout"/> back its own copy of the frame
    /// arithmetic (a local <c>ui</c>, <c>margin</c>, <c>tabStrip</c>, <c>statusBar</c>) and then
    /// change one constant in <see cref="EditorChrome"/> — every other test stays green and this
    /// one goes red, naming the second owner.</para>
    /// </summary>
    [Theory]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void TheLastHostFrameScreenStandsInTheFrameItself(int width, int height)
    {
        var sfx = SfxEditorLayout.Compute(width, height);
        // The frame with nothing in it: an empty status-slot list places the exit tab and the
        // five editor tabs and nothing else — this file's own two helpers, unchanged.
        EditorChrome chrome = Chrome(width, height);
        EditorButtonPlace[] buttons = Placed(width, height);

        Assert.Equal(chrome.Ui, sfx.Ui);
        Assert.Equal(chrome.Margin, sfx.Margin);
        Assert.Equal(chrome.ButtonSize, sfx.ButtonSize);
        Assert.Equal(chrome.TabStrip, sfx.TabStrip);
        Assert.Equal(chrome.StatusBar, sfx.StatusBar);
        Assert.Equal(chrome.PromptY, sfx.PromptY);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(chrome.PromptVerbRect(verb), sfx.PromptVerbRect(verb));
        }
        // The exit button and the five editor tabs land where the frame puts them.
        EditorButton[] shared =
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        };
        foreach (EditorButton button in shared)
        {
            Assert.Equal(EditorChrome.ButtonRect(buttons, button), sfx.ButtonRect(button));
        }
        // And the console frame is a DIFFERENT frame, on purpose: the three screens that have
        // moved carry tabs ten console pixels wide wherever the window is, and they agree with
        // each other. Asserting both here is what stops a later hand from "fixing" the
        // difference by dragging one of them back into the host frame.
        var sprite = SpriteEditorLayout.Compute(160, 90, regionCells: 1);
        var map = MapEditorLayout.Compute(160, 90);
        var code = CodeEditorLayout.Compute(160, 90);
        Assert.Equal(ConsoleChrome.ButtonSize, sprite.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, map.ButtonSize);
        Assert.Equal(ConsoleChrome.ButtonSize, code.ButtonSize);
        Assert.Equal(sprite.ButtonRect(EditorButton.ExitTab), map.ButtonRect(EditorButton.ExitTab));
        Assert.Equal(sprite.ButtonRect(EditorButton.ExitTab), code.ButtonRect(EditorButton.ExitTab));
        Assert.NotEqual(sfx.ButtonRect(EditorButton.ExitTab), sprite.ButtonRect(EditorButton.ExitTab));
    }

    /// <summary>
    /// The painter's one rule that can be pinned without a graphics device: what colour a
    /// button's face is, in precedence order. The interesting cases are the precedences — a
    /// stub tab stays dim on the very screen it names, and a dirty save wins over everything.
    ///
    /// <para>Break recipe: move the <c>IsStub</c> arm of
    /// <see cref="EditorChromeRenderer.ButtonInk"/> below the <c>Active</c> arm and the
    /// stub-tab assertion goes red — that reordering is exactly how a dead button starts
    /// looking alive.</para>
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
        // A stub is dim even when the screen calls it active — dead must look dead.
        Assert.True(EditorIcons.IsStub(EditorButton.MusicTab));
        Assert.Equal(
            EditorChromeRenderer.Dim,
            EditorChromeRenderer.ButtonInk(EditorButton.MusicTab, idle with { Active = true }));
        // Hovering changes the frame, never the face.
        Assert.Equal(
            EditorChromeRenderer.ButtonInk(EditorButton.Save, idle),
            EditorChromeRenderer.ButtonInk(EditorButton.Save, idle with { Hovered = true }));
    }
}
