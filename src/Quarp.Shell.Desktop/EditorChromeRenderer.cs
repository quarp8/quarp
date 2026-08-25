using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The pixels of the shared editor frame, and their <b>single owner</b> (M9 stage 3's
/// simplification wave): the palette roles, the 1x1 white quad, the font and the icon strip,
/// the outline helper, the two tinted bands, one icon-button, the status band's text, the
/// reserved prompt line with its three clickable verbs, and the hover tooltip box.
/// <see cref="SpriteEditorRenderer"/> and <see cref="MapEditorRenderer"/> held line-for-line
/// copies of every one of those; two copies of a widget is two opinions about what the shell
/// looks like, and stage 2.5's reviews landed on the sprite editor's copy alone six times.
///
/// <para>Each renderer constructs and disposes its own instance, exactly as each used to
/// construct its own font and icon atlas: the textures are per-screen, only the arithmetic is
/// shared. Nothing here reads a session — the caller resolves what a widget says, this class
/// decides how it looks.</para>
/// </summary>
public sealed class EditorChromeRenderer : IDisposable
{
    // Palette roles, the cast the library's Palette.cs documents.
    public static readonly Color Ink = PaletteColors.Opaque(0);
    public static readonly Color Dim = PaletteColors.Opaque(1);
    public static readonly Color Text = PaletteColors.Opaque(2);
    public static readonly Color Bright = PaletteColors.Opaque(3);

    /// <summary>Blue: the library's selection bar, reused as "this is on".</summary>
    public static readonly Color ActiveBg = PaletteColors.Opaque(4);

    /// <summary>Yellow: the exit prompt and the modified icon — a decision, not a failure.</summary>
    public static readonly Color Warn = PaletteColors.Opaque(8);

    /// <summary>Red: a save that did not happen.</summary>
    public static readonly Color Error = PaletteColors.Opaque(10);

    /// <summary>
    /// The strips' background (owner's second review). Master32[16] is the ink's own secret twin
    /// — the "twilight lift of near-black" (Palette.cs) — so the bands read as raised chrome one
    /// step lighter than the Ink-cleared window while Text and Dim keep their contrast on it.
    /// </summary>
    public static readonly Color StripBg = PaletteColors.Opaque(16);

    private readonly PixelFontAtlas _font;
    private readonly EditorIconAtlas _icons;
    private readonly Texture2D _white;

    public EditorChromeRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _font = new PixelFontAtlas(device);
        _icons = new EditorIconAtlas(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData(new[] { Color.White });
    }

    /// <summary>The host font, shared with the screen's own text (coordinates, layer digits, notices).</summary>
    public PixelFontAtlas Font => _font;

    /// <summary>The 1x1 white quad every solid rectangle on an editor screen is drawn from.</summary>
    public Texture2D White => _white;

    /// <summary>The icon strip, shared with the screen's own glyph drawing (flyout variants).</summary>
    public EditorIconAtlas Icons => _icons;

    public void Dispose()
    {
        _font.Dispose();
        _icons.Dispose();
        _white.Dispose();
    }

    /// <summary>A rectangle outline of the given thickness drawn <b>outside</b> <paramref name="rect"/>, so content is never covered.</summary>
    public void DrawFrame(SpriteBatch batch, Rectangle rect, int thickness, Color color)
    {
        int t = thickness;
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Y - t, rect.Width + 2 * t, t), color);
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Bottom, rect.Width + 2 * t, t), color);
        batch.Draw(_white, new Rectangle(rect.X - t, rect.Y, t, rect.Height), color);
        batch.Draw(_white, new Rectangle(rect.Right, rect.Y, t, rect.Height), color);
    }

    /// <summary>
    /// What colour a button's face is, in precedence order: an unsaved save is warn yellow, a
    /// dead undo/redo is dim, a stub is dim (visible, honest, dead — and a stub tab stays dim
    /// even on the screen it names), an active button is bright, everything else is plain text
    /// ink. Pure and static so the rule can be pinned by a test with no graphics device, which
    /// is the only way anything in this class can be.
    /// </summary>
    public static Color ButtonInk(EditorButton id, in EditorButtonState state) =>
        id == EditorButton.Save && state.Dirty ? Warn
        : id == EditorButton.Undo && !state.CanUndo ? Dim
        : id == EditorButton.Redo && !state.CanRedo ? Dim
        : EditorIcons.IsStub(id) ? Dim
        : state.Active ? Bright
        : Text;

    /// <summary>The two tinted bands. They go first: everything in the strips sits ON them.</summary>
    public void DrawBands(SpriteBatch batch, in EditorChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(_white, chrome.TabStrip, StripBg);
        batch.Draw(_white, chrome.StatusBar, StripBg);
    }

    /// <summary>
    /// One icon-button through the one mechanism: state decides the ink, hover decides the frame,
    /// stubs are dim, an active button gets the library's blue bar as a background (thickness and
    /// fill carry the signal, not hue alone), and save is also the dirty indicator — the modified
    /// glyph in warn yellow while unsaved work exists. A text-faced button
    /// (<paramref name="text"/> non-null) draws the font instead of a glyph, centred as icons are.
    /// </summary>
    /// <returns>The ink the face was drawn in, so the caller can decorate the slot in the same colour.</returns>
    public Color DrawButton(
        SpriteBatch batch, in EditorChrome chrome, EditorButtonPlace place,
        in EditorButtonState state, EditorIcon? icon, string? text)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Color color = ButtonInk(place.Id, state);
        if (state.Active)
        {
            batch.Draw(_white, place.Rect, ActiveBg);
        }
        DrawFrame(batch, place.Rect, 1, state.Hovered ? Bright : Dim);
        if (text is not null)
        {
            _font.Draw(
                batch, text,
                place.Rect.X + (place.Rect.Width - PixelFontAtlas.MeasureWidth(text, chrome.Ui)) / 2,
                place.Rect.Y + (place.Rect.Height - PixelFontAtlas.LineHeight(chrome.Ui)) / 2,
                chrome.Ui, color);
        }
        else
        {
            _icons.Draw(
                batch,
                place.Id == EditorButton.Save
                    ? state.Dirty ? EditorIcon.Modified : EditorIcon.Saved
                    // Nullable since the crash repair: a text-faced button legitimately
                    // carries no glyph, and the throw below names the real breach — a button
                    // with NEITHER face — instead of letting a wrong glyph draw quietly.
                    : icon ?? throw new ArgumentNullException(
                        nameof(icon), "a button with no text must carry an icon (EditorIcons.Face owns the choice)."),
                chrome.ButtonIconRect(place.Rect),
                color);
        }
        return color;
    }

    /// <summary>
    /// The status band's text half (its buttons are drawn with all the others). The band spans
    /// the whole window, so the text takes the screen margin, not the band's X.
    ///
    /// <para><paramref name="numberInk"/> overrides the right field's colour and defaults to
    /// <see cref="Bright"/>. It exists for one case all three reference consoles share: TIC-80's
    /// <c>drawStatus</c> turns <c>size %i/%i</c> red over <c>MAX_CODE</c>, and a limit reported
    /// in the same ink as every other number is a limit nobody notices crossing. Sprite and map
    /// screens omit it and keep exactly the pixels they had.</para>
    /// </summary>
    public void DrawStatusText(
        SpriteBatch batch, in EditorChrome chrome, string coords, string number, Color? numberInk = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _font.Draw(batch, coords, chrome.Margin, chrome.StatusTextY, chrome.Ui, Text);
        _font.Draw(
            batch, number,
            chrome.Margin + PixelFontAtlas.MeasureWidth(coords + "   ", chrome.Ui),
            chrome.StatusTextY, chrome.Ui, numberInk ?? Bright);
    }

    /// <summary>
    /// The reserved line above the status bar: the dirty-exit prompt when it is up (its three
    /// verbs are <see cref="EditorChrome.PromptVerbRect"/>'s clickable rectangles — mouse
    /// parity for Z/X/Esc), the last save error under it, and the screen's own standing notice
    /// under that (the sprite editor's out-of-sync warning, the map's read-only line). When
    /// several exist each moves one line up rather than being traded away: a failed save is why
    /// the prompt is still up, and hiding any of them would lie.
    /// </summary>
    public void DrawPromptLine(
        SpriteBatch batch, in EditorChrome chrome, bool promptShown, string? saveError, string? notice)
    {
        ArgumentNullException.ThrowIfNull(batch);
        int lineY = chrome.PromptY;
        int lineStep = PixelFontAtlas.LineHeight(chrome.Ui) + chrome.Ui;
        if (promptShown)
        {
            _font.Draw(batch, EditorChrome.PromptHeading, chrome.Margin, chrome.PromptY, chrome.Ui, Warn);
            for (int i = 0; i <= (int)EditorPromptVerb.Stay; i++)
            {
                var verb = (EditorPromptVerb)i;
                Rectangle rect = chrome.PromptVerbRect(verb);
                DrawFrame(batch, rect, 1, Warn);
                _font.Draw(
                    batch, EditorChrome.VerbText(verb),
                    rect.X + chrome.Ui, rect.Y + chrome.Ui, chrome.Ui, Bright);
            }
            lineY -= lineStep;
        }
        if (saveError is string error)
        {
            _font.Draw(batch, $"SAVE FAILED: {error}".ToUpperInvariant(), chrome.Margin, lineY, chrome.Ui, Error);
            lineY -= lineStep;
        }
        if (notice is string standing)
        {
            _font.Draw(batch, standing, chrome.Margin, lineY, chrome.Ui, Warn);
        }
    }

    /// <summary>
    /// The tooltip box, drawn last by both screens so it sits over everything: anchored under
    /// <paramref name="anchor"/>, flipped above it near the bottom of the window, clamped into
    /// the horizontal margins — a label that runs off screen answers nothing.
    /// </summary>
    public void DrawTooltip(
        SpriteBatch batch, in EditorChrome chrome, int width, int height, string text, Rectangle anchor)
    {
        ArgumentNullException.ThrowIfNull(batch);
        int boxWidth = PixelFontAtlas.MeasureWidth(text, chrome.Ui) + 2 * chrome.Ui;
        int boxHeight = PixelFontAtlas.LineHeight(chrome.Ui) + 2 * chrome.Ui;
        int x = Math.Clamp(anchor.X, chrome.Margin, Math.Max(chrome.Margin, width - chrome.Margin - boxWidth));
        int y = anchor.Bottom + 2 * chrome.Ui;
        if (y + boxHeight > height - chrome.Margin)
        {
            y = anchor.Y - 2 * chrome.Ui - boxHeight;
        }
        var box = new Rectangle(x, y, boxWidth, boxHeight);
        batch.Draw(_white, box, Ink);
        DrawFrame(batch, box, 1, Bright);
        _font.Draw(batch, text, box.X + chrome.Ui, box.Y + chrome.Ui, chrome.Ui, Text);
    }
}
