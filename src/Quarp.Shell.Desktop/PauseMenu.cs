using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One row of the pause menu. Three, since the owner saw the seven-row first cut in a live
/// window (M9 stage 5a): a menu that spent four of its seven rows on one verb — "move the
/// tick" — and still needed seventeen presses to travel a thousand ticks.
/// </summary>
public enum PauseMenuItem
{
    /// <summary>Back into the game. Reads START instead of RESUME when no cartridge is running (work order Р7).</summary>
    Resume,

    /// <summary>
    /// The scrubber: the current tick between a <c>&lt;</c> and a <c>&gt;</c>. Its verbs are
    /// those two arrows (held, and accelerating — see <see cref="TickScrubber"/>), not Enter,
    /// which is why this is the one row whose <c>Enter</c> does nothing.
    /// </summary>
    Scrub,

    /// <summary>Leave the cartridge: back to the library, or out of the process on a direct launch (Р5).</summary>
    Exit,
}

/// <summary>
/// The menu that stands over the frame of a running cartridge — the door M9 stage 5 exists to
/// cut. Before it, <c>Esc</c> in a game meant "destroy this session and go to the library", so
/// the one thing the whole project is built on (ADR-006 rewind, ADR-007 continuation:
/// <c>TimeMachine.Rebuild</c> puts the author back on the very tick they left) was reachable
/// from no key at all. Now <c>Esc</c> raises this, the simulation stands still, and the six tabs
/// beside it lead to the editors and back.
///
/// <para><b>Three rows, after the owner looked at seven</b> (stage 5a, 2026-09-02). The header
/// <c>PAUSED  T 93</c> went because the same tick is already on the overlay's own status line at
/// the bottom left; <c>STEP -1</c>, <c>STEP +1</c>, <c>REWIND 60</c> and <c>AHEAD 60</c> went
/// because four rows for one verb is four rows for one verb, and because a menu whose largest
/// leap is sixty ticks is a menu you press seventeen times to travel one thousand. What replaced
/// all five is the <see cref="PauseMenuItem.Scrub"/> row: the tick itself, between two arrows
/// that accelerate while they are held. <b>None of the keys were removed with the rows</b> (Р2):
/// <c>,</c>, <c>.</c>, <c>Home</c> and held <c>Backspace</c> still reach the session exactly as
/// they did — see <see cref="GameScreenInput"/>.</para>
///
/// <para><b>It is a model, not a picture.</b> Everything here is arithmetic over
/// <see cref="SystemFont"/>'s metrics and the console's size: which items exist, which one the
/// cursor is on, what the block of text reads, where the box goes, which item a console pixel
/// falls in and which of the two arrows one falls on. <see cref="ShellOverlay"/> fills the box
/// and prints the text and knows nothing else — so the pointer's hit test and the drawn row
/// cannot come to disagree, and the whole menu is testable without a graphics device, like every
/// other screen in this shell.</para>
///
/// <para><b>Why it is drawn in the overlay layer and not into a framebuffer.</b> The layer
/// underneath is the <em>cartridge's</em> framebuffer, which is the project's golden master:
/// <c>quarp sim</c> hashes it and the CI compares those hashes across architectures. A menu
/// printed into it would land inside the hash the moment a player paused. So it rides the same
/// RGBA surface the PAUSE indicator has ridden since M2 — see <see cref="ShellOverlay"/>.</para>
/// </summary>
public sealed class PauseMenu
{
    /// <summary>Padding between the box's edge and the text inside it, in console pixels.</summary>
    private const int Padding = 2;

    /// <summary>
    /// Every line of the block is padded to this many characters, so the box is one width
    /// whatever the tick number is doing. Twenty is not a taste: it is
    /// <see cref="TickColumn"/> + <see cref="TickColumns"/> + 2, the exact width of the scrub
    /// row with the widest number an <see cref="int"/> tick can be, and the RESUME row's
    /// "&gt;RESUME" and "ESC" fit inside it with room to spare.
    /// </summary>
    private const int LineChars = 20;

    /// <summary>Column of the <c>&lt;</c> arrow on the scrub row — the pointer's target and the printed glyph, one number.</summary>
    private const int LeftArrowColumn = 6;

    /// <summary>Column of the <c>&gt;</c> arrow: the row's last, so the field between the arrows is fixed.</summary>
    private const int RightArrowColumn = LineChars - 1;

    /// <summary>First column of the tick field.</summary>
    private const int TickColumn = LeftArrowColumn + 2;

    /// <summary>
    /// How many columns the tick gets. Ten, because "2147483647" is ten characters and a tick is
    /// an <see cref="int"/>: the number is <b>centred</b> in a field of fixed width rather than
    /// pushed against an arrow, so neither arrow ever moves as the digits grow. That is the same
    /// rule <see cref="ConsoleChrome.PromptVerbRect"/> states for the exit prompt's three verbs —
    /// a clickable word that moves while the author is deciding is the worst kind of button.
    /// </summary>
    private const int TickColumns = 10;

    /// <summary>The left half of the scrub row: what the arrows act on.</summary>
    private const string ScrubLabel = "STEP";

    /// <summary>
    /// The item labels, indexed by <see cref="PauseMenuItem"/>. The scrub row's is only the
    /// left half of its line; the arrows and the number are placed by column, not by padding a
    /// string, because those columns are also the pointer's hit test.
    /// </summary>
    private static readonly string[] _labels = { "RESUME", ScrubLabel, "EXIT" };

    /// <summary>
    /// The direct key each item also answers to, or "" when the menu's own Up/Down+Enter is the
    /// only keyboard road to it. Advertised on the row for the same reason every editor tooltip
    /// names its hotkey (M9 stage 2.5's parity law): a verb reachable only by pointing is a verb
    /// half the console cannot do. The scrub row's keys are the arrows it draws, so it names none.
    /// </summary>
    private static readonly string[] _keys = { "ESC", "", "" };

    /// <summary>The whole menu when a cartridge is running.</summary>
    private static readonly PauseMenuItem[] _running =
    {
        PauseMenuItem.Resume, PauseMenuItem.Scrub, PauseMenuItem.Exit,
    };

    /// <summary>
    /// The same menu when there is nothing to step through — the author came into the editor
    /// from the library and pressed F1 (work order Р7). RESUME reads START; the scrub row is not
    /// listed rather than listed dead, because a row that cannot act is a row that teaches the
    /// author the menu is broken.
    /// </summary>
    private static readonly PauseMenuItem[] _idle = { PauseMenuItem.Resume, PauseMenuItem.Exit };

    private int _selected;

    /// <summary>True while the menu is up. The simulation stands still exactly then — see <see cref="ShellModeMachine.HandleEscape"/>.</summary>
    public bool Shown { get; private set; }

    /// <summary>Which item the cursor is on, as an index into <see cref="Items"/>.</summary>
    public int Selected => _selected;

    /// <summary>
    /// Whether a cartridge is behind the menu. Set by the machine whenever the menu goes up,
    /// because the answer decides both the item list and what the first row says, and a menu
    /// that read the session directly would be a second owner of the session's lifetime.
    /// </summary>
    public bool GameRunning { get; private set; }

    /// <summary>The items on show, which depends on <see cref="GameRunning"/> and on nothing else.</summary>
    public IReadOnlyList<PauseMenuItem> Items => GameRunning ? _running : _idle;

    /// <summary>The item under the cursor.</summary>
    public PauseMenuItem Current => Items[_selected];

    /// <summary>
    /// Raises the menu on the first row. Always the first row: RESUME (or START) is the answer
    /// to "I pressed Esc by accident", and a menu that remembered EXIT from last time would put
    /// the destructive verb under the next Enter.
    /// </summary>
    public void Open(bool gameRunning)
    {
        GameRunning = gameRunning;
        Shown = true;
        _selected = 0;
    }

    /// <summary>Lowers the menu. The machine resumes the simulation; this only forgets it was up.</summary>
    public void Close() => Shown = false;

    /// <summary>
    /// Up / Down. Wrapping, like the tab ring and for the same reason: a list that stops at the
    /// ends costs two presses to reach EXIT from RESUME and one to reach it the other way.
    /// </summary>
    public void Move(int direction)
    {
        int count = Items.Count;
        _selected = ((_selected + Math.Sign(direction)) % count + count) % count;
    }

    /// <summary>Puts the cursor on a row by index — the pointer's half of <see cref="Move"/>. Out-of-range indices are ignored.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            _selected = index;
        }
    }

    /// <summary>
    /// The box, in console pixels: as wide as <see cref="LineChars"/> plus padding, as tall as
    /// the item list, centred on the screen. Centred rather than parked in a corner because it
    /// is modal — the whole console is waiting on it — and the corner is where this shell
    /// already prints the things that are <em>not</em> waiting on you (the PAUSE and REC
    /// indicators, bottom left).
    /// </summary>
    public Rectangle Box(int screenWidth, int screenHeight)
    {
        int width = LineChars * SystemFont.CellWidth + 2 * Padding;
        int height = LineCount * SystemFont.CellHeight - (SystemFont.CellHeight - SystemFont.GlyphHeight)
            + 2 * Padding;
        return new Rectangle(
            (screenWidth - width) / 2, (screenHeight - height) / 2, width, height);
    }

    /// <summary>Where the first glyph of the block goes — the box, inset by its padding.</summary>
    public Point TextOrigin(int screenWidth, int screenHeight)
    {
        Rectangle box = Box(screenWidth, screenHeight);
        return new Point(box.X + Padding, box.Y + Padding);
    }

    /// <summary>
    /// The clickable strip of one item, spanning the box's full width so the pointer does not
    /// have to find the three-pixel glyphs. Row zero is the first item: the header row this
    /// arithmetic used to skip went with the header (stage 5a).
    /// </summary>
    public Rectangle ItemRect(int index, int screenWidth, int screenHeight)
    {
        Rectangle box = Box(screenWidth, screenHeight);
        Point origin = TextOrigin(screenWidth, screenHeight);
        return new Rectangle(
            box.X, origin.Y + index * SystemFont.CellHeight, box.Width, SystemFont.CellHeight);
    }

    /// <summary>A console point to the item under it, or false — the whole of the menu's mouse half.</summary>
    public bool TryItem(int x, int y, int screenWidth, int screenHeight, out int index)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemRect(i, screenWidth, screenHeight).Contains(x, y))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// Where the scrub row's <c>&lt;</c> (direction -1) or <c>&gt;</c> (+1) is drawn, as one
    /// character cell — the pointer's half of the two held arrow keys (Р4). Empty when the menu
    /// is not offering the scrub row, so the hit test below simply misses.
    ///
    /// <para>The cell and not a padded target: at the shell's whole-integer scale a 4x6 console
    /// cell is 32x48 window pixels at the default zoom, and a rectangle bigger than the glyph
    /// would be a hit test that stops being the picture — the very thing this class exists to
    /// prevent.</para>
    /// </summary>
    public Rectangle ScrubArrowRect(int direction, int screenWidth, int screenHeight)
    {
        int row = ScrubRow;
        if (row < 0)
        {
            return Rectangle.Empty;
        }
        Point origin = TextOrigin(screenWidth, screenHeight);
        int column = direction < 0 ? LeftArrowColumn : RightArrowColumn;
        return new Rectangle(
            origin.X + (column * SystemFont.CellWidth),
            origin.Y + (row * SystemFont.CellHeight),
            SystemFont.CellWidth,
            SystemFont.CellHeight);
    }

    /// <summary>A console point to the arrow under it (-1 back, +1 forward), or false.</summary>
    public bool TryScrubArrow(int x, int y, int screenWidth, int screenHeight, out int direction)
    {
        if (ScrubArrowRect(-1, screenWidth, screenHeight).Contains(x, y))
        {
            direction = -1;
            return true;
        }
        if (ScrubArrowRect(+1, screenWidth, screenHeight).Contains(x, y))
        {
            direction = +1;
            return true;
        }
        direction = 0;
        return false;
    }

    /// <summary>
    /// The block of text the overlay prints: one row per item, with the cursor on the selected
    /// one. Built here rather than in the painter so the row the pointer hits
    /// (<see cref="ItemRect"/>) and the row the eye reads are the same arithmetic — and so are
    /// the two arrows (<see cref="ScrubArrowRect"/>), which is the whole reason the scrub row is
    /// laid out by column instead of by concatenation.
    /// </summary>
    /// <param name="tick">
    /// The tick to print between the arrows — the scrubber's target, which is where the author
    /// is <em>aiming</em> and therefore what has to answer their key on the frame they press it
    /// (see <see cref="ShellModeMachine.MenuTick"/> for why that is not always the session's own
    /// tick yet). Null when nothing is running, in which case there is no scrub row at all.
    /// </param>
    public string Text(int? tick)
    {
        var lines = new string[LineCount];
        for (int i = 0; i < Items.Count; i++)
        {
            PauseMenuItem item = Items[i];
            lines[i] = item == PauseMenuItem.Scrub
                ? ScrubRowText(i == _selected, tick)
                : PlainRowText(item, i == _selected);
        }
        return string.Join("\n", lines);
    }

    /// <summary>A verb row: the cursor, the label, and the key it also answers to, right-aligned.</summary>
    private string PlainRowText(PauseMenuItem item, bool selected)
    {
        string label = item == PauseMenuItem.Resume && !GameRunning ? "START" : _labels[(int)item];
        string key = _keys[(int)item];
        string row = (selected ? ">" : " ") + label;
        return Pad(row.PadRight(LineChars - key.Length) + key);
    }

    /// <summary>
    /// The scrub row, placed by column: cursor, label, <c>&lt;</c>, the tick centred in its
    /// fixed field, <c>&gt;</c>. A number too long for the field cannot happen — the field holds
    /// every <see cref="int"/> — but a null tick can (the menu was asked for its text with
    /// nothing running), and then the field is blank rather than a lie.
    /// </summary>
    private static string ScrubRowText(bool selected, int? tick)
    {
        Span<char> row = stackalloc char[LineChars];
        row.Fill(' ');
        row[0] = selected ? '>' : ' ';
        ScrubLabel.AsSpan().CopyTo(row[1..]);
        row[LeftArrowColumn] = '<';
        row[RightArrowColumn] = '>';
        if (tick is int at)
        {
            string digits = at.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int start = TickColumn + Math.Max(0, (TickColumns - digits.Length) / 2);
            digits.AsSpan(0, Math.Min(digits.Length, LineChars - start)).CopyTo(row[start..]);
        }
        return new string(row);
    }

    /// <summary>Index of the scrub row in <see cref="Items"/>, or -1 when it is not offered.</summary>
    private int ScrubRow
    {
        get
        {
            IReadOnlyList<PauseMenuItem> items = Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == PauseMenuItem.Scrub)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>One row per item — the header the first cut printed above them is gone (stage 5a).</summary>
    private int LineCount => Items.Count;

    private static string Pad(string line) =>
        line.Length >= LineChars ? line[..LineChars] : line.PadRight(LineChars);
}
