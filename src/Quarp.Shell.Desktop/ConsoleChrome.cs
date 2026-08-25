using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>One placed icon-button: identity plus rectangle. Enabled-ness is not stored — <see cref="EditorIcons.IsStub"/> owns it.</summary>
public readonly struct EditorButtonPlace
{
    public EditorButton Id { get; init; }

    public Rectangle Rect { get; init; }
}

/// <summary>The three clickable verbs of the dirty-exit prompt line — mouse parity for Z / X / Esc.</summary>
public enum EditorPromptVerb
{
    SaveAndExit,
    Discard,
    Stay,
}

/// <summary>
/// The frame a tool screen stands in when it is drawn <b>on the console itself</b> — 160x90
/// pixels, 40 columns by 15 rows of the 4x6 system font (SPEC-8 §1). Wave R2's new road, laid
/// beside the old one rather than over it.
///
/// <para><b>It was the second chrome; as of wave R6 it is the only one.</b> This type was born
/// beside a host frame that measured in window pixels through a 320x180 text anchor, because
/// ADR-029 could not move five screens in one afternoon. The last of them moved in R5 and the
/// boot menu — the host font path's last tenant of any kind — in R6, so <c>EditorChrome</c>,
/// <c>EditorChromeRenderer</c>, <c>EditorIconAtlas</c>, <c>PixelFontAtlas</c> and
/// <c>PixelFontMetrics</c> left the tree. Three facts that lived in the old frame because it
/// was the older of the two came here rather than dying with it, and this is the whole list:
/// <see cref="EditorButtonPlace"/>, <see cref="EditorPromptVerb"/> and
/// <see cref="RightTabs"/>. Nothing was copied — <c>ButtonRect</c> and <c>TryButton</c> used to
/// be forwarders from here into there and are now the implementations themselves, so the count
/// of owners went down by the move and not up.</para>
///
/// <para><b>The tab order has one owner and it is now here.</b> <see cref="RightTabs"/> is that
/// owner; every screen places its tabs by walking it, and no screen restates the order. The
/// same discipline the sheet strip already lives under (<see cref="SheetStrip"/>) and the frame
/// placement (<see cref="FramePlacement"/>).</para>
///
/// <para><b>The vertical budget, in full, because on a 90-row screen every row is an
/// argument.</b> Ten rows of top bar (a 10x10 button is an 8x8 icon plus a one-pixel frame on
/// each side — <see cref="EditorIcons.IconPixels"/> is 8 and the masks use every one of those
/// pixels, so a frame drawn on the icon's own border would eat the glyph); one rule; sixty-four
/// rows of content, which is exactly what an 8x8 sprite at zoom 8 needs; three rows of scroll
/// slider; one rule; five rows of message line; one rule; five rows of status text. Ten plus
/// one plus sixty-four plus three plus one plus five plus one plus five is ninety, with nothing
/// spare. Every number below that is measured up from <see cref="ScreenHeight"/> rather than
/// written down as an absolute row exists because it would otherwise mean the wrong thing on a
/// console that is not 90 rows tall — the very mistake ADR-029 exists to stop making.</para>
/// </summary>
public readonly struct ConsoleChrome
{
    /// <summary>Side of every icon-button: an 8x8 mask plus a one-pixel frame each side.</summary>
    public const int ButtonSize = EditorIcons.IconPixels + 2;

    /// <summary>Where the 8x8 mask sits inside its button.</summary>
    public const int IconInset = 1;

    /// <summary>The top band: one button tall, full width.</summary>
    public const int TopBarHeight = ButtonSize;

    /// <summary>Screen-edge inset for text — one pixel, because forty columns is the whole line.</summary>
    public const int Margin = 1;

    /// <summary>Glyph top of the tooltip / screen-name field inside the top band: 5 px of type centred in 10.</summary>
    public const int TooltipTextY = 2;

    /// <summary>Height of the sheet slider's track — thin, but three console pixels is 24 window pixels at the shell's default scale.</summary>
    public const int SliderHeight = 3;

    // Measured up from the bottom edge, newest-first as they appear on screen. At 90 rows they
    // come out as 75 (slider and content bottom), 78 (rule), 79 (message), 84 (rule), 85 (status).
    private const int ContentBottomUp = 15;
    private const int FooterRuleUp = 12;
    private const int MessageUp = 11;
    private const int StatusRuleUp = 6;
    private const int StatusTextUp = 5;

    /// <summary>Gap between a prompt verb and the next, in pixels — two character cells.</summary>
    private const int VerbGap = 2 * SystemFont.CellWidth;

    // The prompt's texts. They were re-cut when this frame was built: the dead host frame's
    // "UNSAVED CHANGES: Z SAVE+EXIT X DISCARD ESC STAY" was 47 characters and the console's line
    // is 39. The same move LibraryRenderer's footer hint made in wave R1, and for the same
    // reason — the line is the screen's real width now.

    // The right-edge tab group in the owner's dictated order: from the right corner leftwards
    // music, sounds, tilemaps, sprites, code. One list, so no two screens can present the tabs
    // in different orders.

    /// <summary>The prompt's heading when nothing has failed yet.</summary>
    public const string PromptHeading = "UNSAVED.";

    /// <summary>
    /// The heading when the last save attempt failed. It replaces the plain one instead of
    /// taking a line of its own: the console's message band is one line, and "a save failed"
    /// and "you are being asked what to do about unsaved work" are the same event told twice.
    /// </summary>
    public const string PromptFailedHeading = "SAVE FAILED.";

    public const string PromptSaveVerb = "Z SAVE";
    public const string PromptDiscardVerb = "X DROP";
    public const string PromptStayVerb = "ESC STAY";

    private static readonly string[] _promptVerbs = { PromptSaveVerb, PromptDiscardVerb, PromptStayVerb };

    private static readonly EditorButton[] _rightTabs =
    {
        EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
        EditorButton.SpritesTab, EditorButton.CodeTab,
    };

    /// <summary>
    /// The right-edge tab group, index 0 hugging the right corner — <b>the one owner of the tab
    /// order</b>. It was published by the host frame in wave R2 because a second frame existed
    /// then; wave R6 killed that frame and the list moved here with its reader, exactly as that
    /// file's own comment said it would ("the day the last screen leaves this frame, this list
    /// goes with the reader and not with the file"). Moved, not copied: there is still one
    /// array and one order.
    /// </summary>
    public static IReadOnlyList<EditorButton> RightTabs => _rightTabs;

    /// <summary>Screen width this frame was measured for — 160 on profile 8.</summary>
    public int ScreenWidth { get; private init; }

    /// <summary>Screen height this frame was measured for — 90 on profile 8.</summary>
    public int ScreenHeight { get; private init; }

    /// <summary>The top band, full width: exit at the left, the five editor tabs off the right corner, the tooltip field between them.</summary>
    public Rectangle TopBar => new(0, 0, ScreenWidth, TopBarHeight);

    /// <summary>The rule under the top band.</summary>
    public int HeaderRuleY => TopBarHeight;

    /// <summary>First row the screen's own content may use.</summary>
    public int ContentTop => TopBarHeight + 1;

    /// <summary>One past the last row the screen's own content may use — 75 on a 90-row console.</summary>
    public int ContentBottom => ScreenHeight - ContentBottomUp;

    /// <summary>Content height in rows — 64, which is an 8x8 sprite at zoom 8 and no more.</summary>
    public int ContentHeight => ContentBottom - ContentTop;

    /// <summary>Top row of the sheet slider's track, directly under the content.</summary>
    public int SliderY => ContentBottom;

    /// <summary>
    /// One past the last row of the slider's track — 78 on a 90-row console, the very row
    /// <see cref="FooterRuleY"/> sits on.
    ///
    /// <para><b>And the content floor of a screen that places no horizontal slider at all.</b>
    /// The three rows between <see cref="ContentBottom"/> and here are reserved for one control
    /// and one only: the horizontal position bar the sprite screen puts under its sheet window
    /// and the map screen under its viewport. A screen that has no such bar — the code screen,
    /// whose long road through a file is the <em>vertical</em> scrollbar at its right edge — may
    /// run its content down to this row instead, and the arithmetic says why it matters: 64 rows
    /// hold ten lines of the 4x6 font and 67 hold eleven, so those three pixels are a tenth of
    /// everything that screen exists to show. The number stays owned here, with the rest of the
    /// vertical budget, rather than being computed as "ContentBottom plus three" by whoever
    /// wants it; the day a screen grows a bar it goes back to <see cref="ContentBottom"/> and
    /// nothing else moves.</para>
    /// </summary>
    public int SliderBottom => SliderY + SliderHeight;

    /// <summary>The rule above the message line.</summary>
    public int FooterRuleY => ScreenHeight - FooterRuleUp;

    /// <summary>Glyph top of the one message line — the standing notice, the save error or the exit prompt.</summary>
    public int MessageY => ScreenHeight - MessageUp;

    /// <summary>The rule above the status line.</summary>
    public int StatusRuleY => ScreenHeight - StatusRuleUp;

    /// <summary>Glyph top of the status line: coordinates at the left, the sprite number at the right.</summary>
    public int StatusTextY => ScreenHeight - StatusTextUp;

    /// <summary>The status band as a rectangle — its rule and its text, for a test that wants one shape to point at.</summary>
    public Rectangle StatusBar => new(0, StatusRuleY, ScreenWidth, ScreenHeight - StatusRuleY);

    /// <summary>Longest full-width line the screen can hold — 39 characters on a 160 px console.</summary>
    public int LineChars => (ScreenWidth - 2 * Margin) / SystemFont.CellWidth;

    /// <summary>
    /// The free strip of the top band, between the exit button and the leftmost editor tab.
    /// This is where a hover label is printed — see <see cref="TooltipChars"/> for why it is
    /// printed here at all and not under the pointer.
    /// </summary>
    public Rectangle TooltipField =>
        new(ButtonSize, 0, ScreenWidth - ButtonSize * (1 + _rightTabs.Length), TopBarHeight);

    /// <summary>
    /// How many characters the tooltip field holds — 25 on a 160 px console.
    ///
    /// <para><b>This is a deliberate divergence from what this editor did yesterday.</b> The
    /// host-resolution screen popped a bordered box under the pointer (the dead host frame's
    /// <c>DrawTooltip</c>), which is fine when the window is 1280 px wide
    /// and impossible when it is 160: a 40-character label in a box is 164 px, wider than the
    /// screen, and even a short one would cover the canvas the label is explaining. TIC-80 hit
    /// the same wall on 240x136 and answered it by printing the label into the toolbar's spare
    /// room instead (<c>studio.c</c>, <c>drawToolbar</c>: <c>tic_api_print(tic,
    /// studio->tooltip.text, TextOffset, 1, ...)</c>, falling back to the editor's name when no
    /// control is hovered). We do the same, with the same fallback. The price is named rather
    /// than hidden: a label longer than 25 characters is cut, and several of ours are — the
    /// sheet slider's is 53. Shortening the label texts themselves would be a second owner of
    /// what a control is called, so the cut happens here, at the one place that knows the
    /// width.</para>
    /// </summary>
    public int TooltipChars => TooltipField.Width / SystemFont.CellWidth;

    /// <summary>
    /// Measures the console frame and places its six shared buttons into
    /// <paramref name="buttons"/> from <paramref name="placed"/> on: the exit button at the top
    /// left, then the five editor tabs from the right corner leftwards in
    /// <see cref="RightTabs"/>' order — the one owner of that order, so no two screens can end
    /// up presenting the tabs differently. The screen fills the rest of the
    /// array itself, which is why the index travels by reference.
    /// </summary>
    public static ConsoleChrome Compute(
        int screenWidth, int screenHeight, EditorButtonPlace[] buttons, ref int placed)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        buttons[placed++] = new EditorButtonPlace
        {
            Id = EditorButton.ExitTab, Rect = new Rectangle(0, 0, ButtonSize, ButtonSize),
        };
        for (int i = 0; i < _rightTabs.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _rightTabs[i],
                Rect = new Rectangle(screenWidth - (i + 1) * ButtonSize, 0, ButtonSize, ButtonSize),
            };
        }
        return new ConsoleChrome { ScreenWidth = screenWidth, ScreenHeight = screenHeight };
    }

    /// <summary>The 8x8 mask's destination inside a button — one pixel in on every side.</summary>
    public static Rectangle ButtonIconRect(Rectangle buttonRect) =>
        new(buttonRect.X + IconInset, buttonRect.Y + IconInset,
            EditorIcons.IconPixels, EditorIcons.IconPixels);

    /// <summary>Where a text face's first glyph starts inside a button, so the string is centred in the cell.</summary>
    public static int ButtonTextX(Rectangle buttonRect, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return buttonRect.X + (buttonRect.Width - text.Length * SystemFont.CellWidth) / 2;
    }

    /// <summary>Glyph top of a text face inside a button — 5 px of type centred in the cell.</summary>
    public static int ButtonTextY(Rectangle buttonRect) =>
        buttonRect.Y + (buttonRect.Height - SystemFont.GlyphHeight) / 2;

    /// <summary>
    /// Clickable area of one prompt verb. The three verbs are right-aligned to the screen's
    /// edge and their positions do <b>not</b> depend on the heading: a failed save lengthens the
    /// heading from "UNSAVED." to "SAVE FAILED.", and a verb that moved under the pointer while
    /// the author was deciding would be the worst possible moment to move a button.
    /// </summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb)
    {
        int x = ScreenWidth - Margin - PromptVerbsWidth();
        for (int i = 0; i < (int)verb; i++)
        {
            x += _promptVerbs[i].Length * SystemFont.CellWidth + VerbGap;
        }
        return new Rectangle(
            x - 1, MessageY,
            _promptVerbs[(int)verb].Length * SystemFont.CellWidth + 1, SystemFont.GlyphHeight);
    }

    /// <summary>Console point to prompt verb, or false. Three rectangles, tested only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb)
    {
        for (int i = 0; i < _promptVerbs.Length; i++)
        {
            if (PromptVerbRect((EditorPromptVerb)i).Contains(x, y))
            {
                verb = (EditorPromptVerb)i;
                return true;
            }
        }
        verb = default;
        return false;
    }

    /// <summary>The verb's label — the renderer's text and the hit test's width come from one list.</summary>
    public static string VerbText(EditorPromptVerb verb) => _promptVerbs[(int)verb];

    /// <summary>How many characters of heading the prompt line has room for, once the verbs have taken theirs.</summary>
    public int PromptHeadingChars =>
        Math.Max(0, (ScreenWidth - 2 * Margin - PromptVerbsWidth() - SystemFont.CellWidth) / SystemFont.CellWidth);

    /// <summary>
    /// The placed rectangle of one button — the tooltip anchors to it. Throws when the screen
    /// does not place it, rather than answering <c>default</c>: a button that silently lands at
    /// 0,0 is a bug that draws itself in the top-left corner and looks deliberate.
    ///
    /// <para>Until wave R6 this was a forwarder into the host frame, which owned the loop. The
    /// host frame is gone and the loop lives here; nothing about what it answers changed.</para>
    /// </summary>
    public static Rectangle ButtonRect(IReadOnlyList<EditorButtonPlace> buttons, EditorButton id)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        foreach (EditorButtonPlace place in buttons)
        {
            if (place.Id == id)
            {
                return place.Rect;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(id), id, "this button is not placed on this editor screen.");
    }

    /// <summary>Console point to the button under it, stubs included — hover needs the dead buttons too; the click routing filters by <see cref="EditorIcons.IsStub"/> itself.</summary>
    public static bool TryButton(
        IReadOnlyList<EditorButtonPlace> buttons, int x, int y, out EditorButton id)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        foreach (EditorButtonPlace place in buttons)
        {
            if (place.Rect.Contains(x, y))
            {
                id = place.Id;
                return true;
            }
        }
        id = default;
        return false;
    }

    /// <summary>Cuts a string to what one full-width console line holds.</summary>
    public string FitLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= LineChars ? text : text[..LineChars];
    }

    /// <summary>Cuts a hover label to what the top band's free strip holds — see <see cref="TooltipChars"/>.</summary>
    public string FitTooltip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= TooltipChars ? text : text[..TooltipChars];
    }

    private static int PromptVerbsWidth()
    {
        int width = 0;
        for (int i = 0; i < _promptVerbs.Length; i++)
        {
            width += _promptVerbs[i].Length * SystemFont.CellWidth;
        }
        return width + VerbGap * (_promptVerbs.Length - 1);
    }
}
