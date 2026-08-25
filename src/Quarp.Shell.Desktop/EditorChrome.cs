using Microsoft.Xna.Framework;

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
/// The frame both editor screens stand in, and its <b>single owner</b> (M9 stage 3, the
/// simplification wave): the ui scale, the margin, the button side, the full-width tab band
/// with exit at the left and the five editor tabs off the right corner, the mirror status band
/// with its right-aligned button row, the reserved prompt line above it, and the top/bottom
/// edges of the space left for whatever the screen itself draws.
///
/// <para>Before this wave <see cref="SpriteEditorLayout"/> and <see cref="MapEditorLayout"/>
/// each computed this frame, and the map already had to reach into the sprite editor's
/// <c>Compute</c> for the prompt verbs — a second owner admitting it was one. They keep their
/// own structs, because what the frame surrounds is genuinely different; they no longer keep
/// their own frame.</para>
/// </summary>
public readonly struct EditorChrome
{
    // The prompt's three verbs, owned here because the renderer draws them and the hit test
    // measures them — two copies of these strings would be two opinions about where a click lands.
    public const string PromptHeading = "UNSAVED CHANGES:";
    public const string PromptSaveVerb = "Z SAVE+EXIT";
    public const string PromptDiscardVerb = "X DISCARD";
    public const string PromptStayVerb = "ESC STAY";

    private static readonly string[] _promptVerbs = { PromptSaveVerb, PromptDiscardVerb, PromptStayVerb };

    // The right-edge tab group in the owner's dictated order: from the right corner leftwards
    // music, sounds, tilemaps, sprites, code. One list, so the two screens cannot present the
    // tabs in different orders.
    private static readonly EditorButton[] _rightTabs =
    {
        EditorButton.MusicTab, EditorButton.SoundTab, EditorButton.TilemapTab,
        EditorButton.SpritesTab, EditorButton.CodeTab,
    };

    /// <summary>
    /// The right-edge tab group, index 0 hugging the right corner — <b>the one owner of the tab
    /// order</b>, published in wave R2 because a second frame now exists.
    /// <see cref="ConsoleChrome"/> places the same five tabs on the console's own screen and
    /// reads them from here rather than restating them; the day the last screen leaves this
    /// frame, this list goes with the reader and not with the file.
    /// </summary>
    public static IReadOnlyList<EditorButton> RightTabs => _rightTabs;

    /// <summary>Host-UI text scale, same anchor the library uses (<see cref="PixelFontMetrics.UiScale"/>).</summary>
    public int Ui { get; private init; }

    /// <summary>Screen-edge inset, in window pixels — the library's 4 * ui, kept identical so the modes read as one shell.</summary>
    public int Margin { get; private init; }

    /// <summary>The one-pixel-at-scale-1 breathing space between neighbouring buttons and swatches.</summary>
    public int Gap { get; private init; }

    /// <summary>Side of every icon-button: an 8-px icon at scale <see cref="Ui"/> plus 2 * ui padding a side.</summary>
    public int ButtonSize { get; private init; }

    /// <summary>The full-width top band behind the tab icons (owner's second review: the strips get their own background so they stop melting into the window).</summary>
    public Rectangle TabStrip { get; private init; }

    /// <summary>The bottom band that holds the readouts and the save/undo/redo row — full window width like <see cref="TabStrip"/>.</summary>
    public Rectangle StatusBar { get; private init; }

    /// <summary>Baseline for the status bar's text, vertically centred against its buttons.</summary>
    public int StatusTextY { get; private init; }

    /// <summary>Baseline of the reserved prompt / save-error line just above the status bar.</summary>
    public int PromptY { get; private init; }

    /// <summary>First window row the screen's own content may use — just under the tab band.</summary>
    public int ContentTop { get; private init; }

    /// <summary>Last window row the screen's own content may use — just above the reserved prompt line.</summary>
    public int ContentBottom { get; private init; }

    /// <summary>
    /// Measures the frame and places its buttons into <paramref name="buttons"/> from
    /// <paramref name="placed"/> on: the exit tab, the five editor tabs, then
    /// <paramref name="statusSlots"/> from the right edge inwards (slot 0 outermost; an empty
    /// slot stays empty so shared buttons keep their pixels across screens). The
    /// screen fills the rest of the array itself, which is why the index is by reference.
    /// The prompt line is reserved whether or not a prompt is up: a canvas that jumps the frame
    /// the author is asked about unsaved work would move the very pixels they are deciding over.
    /// </summary>
    public static EditorChrome Compute(
        int width, int height, EditorButtonPlace[] buttons, ref int placed, EditorButton?[] statusSlots)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(statusSlots);

        int ui = PixelFontMetrics.UiScale(width, height);
        int margin = 4 * ui;
        int gap = ui;
        int button = (EditorIcons.IconPixels + 4) * ui;

        var tabStrip = new Rectangle(0, 0, width, button + 2 * margin);
        buttons[placed++] = new EditorButtonPlace
        {
            Id = EditorButton.ExitTab, Rect = new Rectangle(margin, margin, button, button),
        };
        for (int i = 0; i < _rightTabs.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _rightTabs[i],
                Rect = new Rectangle(width - margin - button - i * (button + gap), margin, button, button),
            };
        }

        var statusBar = new Rectangle(0, height - button - 2 * margin, width, button + 2 * margin);
        int statusButtonY = statusBar.Y + margin;
        // Slots, not a list: index 0 is the rightmost place at the edge, and a screen that has
        // no button for a slot leaves it EMPTY rather than shifting the rest right. That is what
        // keeps Save, Undo and Redo on the same pixels in every editor — the sprite screen's
        // Clear (the owner's second review put it right of redo) does not push the shared three
        // one step over on the map screen, where there is nothing to clear. Muscle memory is the
        // whole point of one frame for every editor.
        for (int i = 0; i < statusSlots.Length; i++)
        {
            if (statusSlots[i] is not EditorButton id)
            {
                continue;
            }
            buttons[placed++] = new EditorButtonPlace
            {
                Id = id,
                Rect = new Rectangle(width - margin - button - i * (button + gap), statusButtonY, button, button),
            };
        }

        int promptY = statusBar.Y - 2 * ui - PixelFontMetrics.LineHeight(ui);
        return new EditorChrome
        {
            Ui = ui,
            Margin = margin,
            Gap = gap,
            ButtonSize = button,
            TabStrip = tabStrip,
            StatusBar = statusBar,
            StatusTextY = statusButtonY + (button - PixelFontMetrics.LineHeight(ui)) / 2,
            PromptY = promptY,
            ContentTop = tabStrip.Bottom + 2 * ui,
            ContentBottom = promptY - 2 * ui,
        };
    }

    /// <summary>The icon's destination inside a button: centred, at scale <see cref="Ui"/> — a whole multiple of the 8-px mask.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect)
    {
        int side = EditorIcons.IconPixels * Ui;
        int pad = (ButtonSize - side) / 2;
        return new Rectangle(buttonRect.X + pad, buttonRect.Y + pad, side, side);
    }

    /// <summary>The placed rectangle of one button — the tooltip anchors to it. Throws when the screen does not place it.</summary>
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

    /// <summary>Window point → button under it, stubs included — hover needs the dead buttons too; the click routing filters by <see cref="EditorIcons.IsStub"/> itself.</summary>
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

    /// <summary>Clickable area of one prompt verb, ui-padded around its text. Only meaningful while that screen's exit prompt is up — the shell gates the hit test on that, as it gates the keys.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb)
    {
        int x = Margin + PixelFontMetrics.MeasureWidth(PromptHeading, Ui) + 4 * Ui;
        for (int i = 0; i < (int)verb; i++)
        {
            x += PixelFontMetrics.MeasureWidth(_promptVerbs[i], Ui) + 6 * Ui;
        }
        return new Rectangle(
            x - Ui, PromptY - Ui,
            PixelFontMetrics.MeasureWidth(_promptVerbs[(int)verb], Ui) + 2 * Ui,
            PixelFontMetrics.LineHeight(Ui) + 2 * Ui);
    }

    /// <summary>Window point → prompt verb, or false. Three rectangles, checked only while the prompt is up.</summary>
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
}
