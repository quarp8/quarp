using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The pixels of the console-scale editor frame, drawn with the same core calls a cartridge
/// makes — <c>RectFill</c>, <c>Rect</c>, <c>Print</c>, and <see cref="ConsoleIcons"/>'
/// <c>Pset</c> loop for the 8x8 masks. It was born as the console twin of a host-resolution
/// painter; that painter died with the boot menu in wave R6, so this is now the only frame
/// painter in the tree (see <see cref="ConsoleChrome"/>'s type comment).
///
/// <para><b>It owns no device, so it is not a renderer in the layering sense</b> — the same
/// move <see cref="LibraryRenderer"/> made in wave R1. Everything here writes into a
/// <see cref="VirtualConsole"/> a headless test can construct, which is the whole point: the
/// frame of a tool screen becomes bytes, and bytes can be hashed.</para>
///
/// <para><b>Sixteen slots, no <c>Pal</c> tricks.</b> Every colour below is one of the console's
/// sixteen visible slots under the identity palette map
/// (<see cref="ShellScreen.Begin"/> resets it). The host frame used master slot 16 — the
/// "twilight lift of near-black" — for its two bands; on the console that slot is unreachable
/// without remapping one of the sixteen, and the sixteen are exactly what the palette swatches
/// must show truthfully. So the bands are not filled at all: they are the cleared ink with a
/// one-pixel rule at their edge, which is how <see cref="LibraryRenderer"/> already separates
/// its header and footer, and it costs no slot.</para>
/// </summary>
public static class ConsoleChromeRenderer
{
    /// <summary>The screen's ground: slot 0, the palette's near-black ink.</summary>
    public const byte Ink = 0;

    /// <summary>Slot 1, grey: rules, dead buttons, idle button frames.</summary>
    public const byte Dim = 1;

    /// <summary>Slot 2, light grey: ordinary text and icons.</summary>
    public const byte Text = 2;

    /// <summary>Slot 3, white: the thing under the pointer, the thing that is on.</summary>
    public const byte Bright = 3;

    /// <summary>Slot 4, blue — the library's selection bar, reused as "this is on".</summary>
    public const byte ActiveBg = 4;

    /// <summary>Slot 8, yellow: a decision to make, and unsaved work. Not a failure.</summary>
    public const byte Warn = 8;

    /// <summary>Slot 10, red: a save that did not happen.</summary>
    public const byte Error = 10;

    /// <summary>
    /// What colour a button's face is, in precedence order: an unsaved save is warn yellow, a
    /// dead undo/redo is dim, a stub is dim, an active button is bright, everything else is text
    /// ink. There were two copies of this ladder for exactly as long as there were two frames;
    /// wave R6 took the host one, and what is left is this one, which answers a palette slot
    /// rather than an unpacked colour.
    /// </summary>
    public static byte ButtonInk(EditorButton id, in EditorButtonState state) =>
        id == EditorButton.Save && state.Dirty ? Warn
        : id == EditorButton.Undo && !state.CanUndo ? Dim
        : id == EditorButton.Redo && !state.CanRedo ? Dim
        : EditorIcons.IsStub(id) ? Dim
        : state.Active ? Bright
        : Text;

    /// <summary>The three rules that cut the screen into its bands. They go first; everything else sits between them.</summary>
    public static void DrawBands(VirtualConsole console, in ConsoleChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.RectFill(0, chrome.HeaderRuleY, chrome.ScreenWidth, 1, Dim);
        console.RectFill(0, chrome.FooterRuleY, chrome.ScreenWidth, 1, Dim);
        console.RectFill(0, chrome.StatusRuleY, chrome.ScreenWidth, 1, Dim);
    }

    /// <summary>
    /// One icon-button: an active one gets the blue plate, the face goes on in its state's ink,
    /// and the frame goes <b>on the cell's own border</b> — bright under the pointer, dim
    /// otherwise. The frame is inside the cell rather than around it because a 10x10 cell that
    /// grew a ring would collide with its neighbour on a screen where the whole tab strip is
    /// six cells wide; the 8x8 mask is inset by one pixel precisely to leave that border free.
    /// A text-faced button (<paramref name="text"/> non-null) prints instead of plotting a mask,
    /// centred by the same rule.
    /// </summary>
    /// <returns>The ink the face was drawn in, so a caller can decorate the slot in the same colour.</returns>
    public static byte DrawButton(
        VirtualConsole console, EditorButtonPlace place, in EditorButtonState state,
        EditorIcon? icon, string? text)
    {
        ArgumentNullException.ThrowIfNull(console);
        byte color = ButtonInk(place.Id, state);
        if (state.Active)
        {
            console.RectFill(place.Rect.X, place.Rect.Y, place.Rect.Width, place.Rect.Height, ActiveBg);
        }
        if (text is not null)
        {
            console.Print(
                text, ConsoleChrome.ButtonTextX(place.Rect, text),
                ConsoleChrome.ButtonTextY(place.Rect), color);
        }
        else
        {
            Rectangle destination = ConsoleChrome.ButtonIconRect(place.Rect);
            ConsoleIcons.Draw(
                console,
                place.Id == EditorButton.Save
                    ? state.Dirty ? EditorIcon.Modified : EditorIcon.Saved
                    : icon ?? throw new ArgumentNullException(
                        nameof(icon), "a button with no text must carry an icon (EditorIcons.Face owns the choice)."),
                destination.X, destination.Y, color);
        }
        console.Rect(
            place.Rect.X, place.Rect.Y, place.Rect.Width, place.Rect.Height,
            state.Hovered ? Bright : Dim);
        return color;
    }

    /// <summary>
    /// The hover label, printed into the top band's free strip — TIC-80's <c>drawToolbar</c>
    /// behaviour, and a named departure from what this editor did at host resolution; see
    /// <see cref="ConsoleChrome.TooltipChars"/> for the whole argument. With no control hovered
    /// the field carries <paramref name="fallback"/>, the screen's own name, exactly as
    /// TIC-80's <c>Names[mode]</c> does.
    /// </summary>
    public static void DrawTooltipField(
        VirtualConsole console, in ConsoleChrome chrome, string? tooltip, string fallback)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(fallback);
        Rectangle field = chrome.TooltipField;
        console.Print(
            chrome.FitTooltip(tooltip ?? fallback), field.X + ConsoleChrome.Margin,
            ConsoleChrome.TooltipTextY, tooltip is null ? Dim : Text);
    }

    /// <summary>
    /// The status line: the cursor's coordinates at the left, one number at the right. Both
    /// halves are the order's, and the number is right-aligned to the screen's edge so it
    /// stops jumping when it gains a digit.
    ///
    /// <para><b>Why the right half takes a colour and the left one does not.</b> TIC-80's
    /// <c>drawStatus</c> paints the size readout red past the limit
    /// (<c>code->status.color = codeLen > MAX_CODE ? tic_color_red : tic_color_white</c>,
    /// REFERENCES-EDITORS §8 item 13) — that is the one thing on this line any screen has ever
    /// wanted to say in a hue. The parameter is <b>optional and defaults to the colour this
    /// painter always used</b>, which is the shape <see cref="DrawTooltipField"/>'s fallback and
    /// <see cref="DrawButton"/>'s <c>text</c> already have: the four screens that have no opinion
    /// say nothing and keep the picture they had, and the colour therefore still has one owner
    /// per screen rather than five screens each inventing a palette. The one caller that does
    /// pass something owns the choice in exactly one expression
    /// (<see cref="CodeEditorRenderer.BudgetInk"/>), used by both of that screen's surfaces.</para>
    ///
    /// <para>The left half stays <see cref="Text"/> on purpose: "which line am I on" cannot be
    /// wrong, so there is nothing for a colour to mean there, and a second optional parameter
    /// would be an invitation to find one.</para>
    /// </summary>
    /// <param name="numberColor">Ink for the right-hand readout; omit for the plain bright it has always been.</param>
    public static void DrawStatusText(
        VirtualConsole console, in ConsoleChrome chrome, string coords, string number,
        byte numberColor = Bright)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(coords);
        ArgumentNullException.ThrowIfNull(number);
        console.Print(coords, ConsoleChrome.Margin, chrome.StatusTextY, Text);
        console.Print(
            number,
            chrome.ScreenWidth - ConsoleChrome.Margin - number.Length * SystemFont.CellWidth,
            chrome.StatusTextY,
            numberColor);
    }

    /// <summary>
    /// The one message line above the status bar, and the whole of what it can say. In
    /// precedence order, because there is one line and not three:
    ///
    /// <list type="number">
    /// <item>the exit prompt, when it is up — heading plus three clickable verbs
    /// (<see cref="ConsoleChrome.PromptVerbRect"/>, the mouse's half of Z / X / Esc). A pending
    /// save error is folded into the heading rather than being pushed off the screen: the line
    /// reads "SAVE FAILED." instead of "UNSAVED.", so the author is never asked to decide
    /// without being told that the last attempt failed;</item>
    /// <item>the save error on its own, when no prompt is up;</item>
    /// <item>the screen's standing notice.</item>
    /// </list>
    ///
    /// <para><b>The named loss.</b> The host frame stacked all three, each on its own line. The
    /// console has one line, so a standing notice is hidden while the prompt is up and returns
    /// the moment the prompt is lowered. That is a real regression and it is here in writing
    /// rather than in nobody's memory: the alternative was a second message row, and the only
    /// place to take those six rows from was the canvas, which the order names as the screen's
    /// main thing.</para>
    /// </summary>
    public static void DrawMessageLine(
        VirtualConsole console, in ConsoleChrome chrome, bool promptShown, string? saveError, string? notice)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (promptShown)
        {
            string heading = saveError is null ? ConsoleChrome.PromptHeading : ConsoleChrome.PromptFailedHeading;
            console.Print(
                heading[..Math.Min(heading.Length, chrome.PromptHeadingChars)],
                ConsoleChrome.Margin, chrome.MessageY, saveError is null ? Warn : Error);
            for (int i = 0; i <= (int)EditorPromptVerb.Stay; i++)
            {
                var verb = (EditorPromptVerb)i;
                Rectangle rect = chrome.PromptVerbRect(verb);
                console.Print(ConsoleChrome.VerbText(verb), rect.X + 1, rect.Y, Bright);
            }
            return;
        }
        if (saveError is string error)
        {
            console.Print(
                chrome.FitLine($"SAVE FAILED: {error}".ToUpperInvariant()),
                ConsoleChrome.Margin, chrome.MessageY, Error);
            return;
        }
        if (notice is string standing)
        {
            console.Print(chrome.FitLine(standing), ConsoleChrome.Margin, chrome.MessageY, Warn);
        }
    }
}
