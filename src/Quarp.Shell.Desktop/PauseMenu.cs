using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// One verb of the pause menu. The list is short on purpose: this is the menu the work order
/// dictates (M9 stage 5 — "продолжить, выйти, номер текущего тика, шаг назад и вперёд,
/// перемотка") and nothing else has been invented to keep it company.
/// </summary>
public enum PauseMenuItem
{
    /// <summary>Back into the game. Reads START instead of RESUME when no cartridge is running (work order Р7).</summary>
    Resume,

    /// <summary>One tick back — the menu's face of the <c>,</c> key.</summary>
    StepBack,

    /// <summary>One tick forward — the menu's face of the <c>.</c> key.</summary>
    StepForward,

    /// <summary><see cref="PauseMenu.JumpTicks"/> ticks back — the menu's face of held Backspace, in one exact leap.</summary>
    Rewind,

    /// <summary><see cref="PauseMenu.JumpTicks"/> ticks forward, resimulated from the recorded log — the exact inverse of <see cref="Rewind"/>.</summary>
    Ahead,

    /// <summary>Leave the cartridge: back to the library, or out of the process on a direct launch (Р5).</summary>
    Exit,
}

/// <summary>
/// The pause menu that stands over the frame of a running cartridge — the door M9 stage 5
/// exists to cut. Before it, <c>Esc</c> in a game meant "destroy this session and go to the
/// library", so the one thing the whole project is built on (ADR-006 rewind, ADR-007
/// continuation: <c>TimeMachine.Rebuild</c> puts the author back on the very tick they left)
/// was reachable from no key at all. Now <c>Esc</c> raises this, the simulation stands still,
/// and the six tabs beside it lead to the editors and back.
///
/// <para><b>It is a model, not a picture.</b> Everything here is arithmetic over
/// <see cref="SystemFont"/>'s metrics and the console's size: which items exist, which one the
/// cursor is on, what the block of text reads, where the box goes and which item a console
/// pixel falls in. <see cref="ShellOverlay"/> fills the box and prints the text and knows
/// nothing else — so the pointer's hit test and the drawn row cannot come to disagree, and the
/// whole menu is testable without a graphics device, like every other screen in this shell.</para>
///
/// <para><b>Why it is drawn in the overlay layer and not into a framebuffer.</b> The layer
/// underneath is the <em>cartridge's</em> framebuffer, which is the project's golden master:
/// <c>quarp sim</c> hashes it and the CI compares those hashes across architectures. A menu
/// printed into it would land inside the hash the moment a player paused. So it rides the same
/// RGBA surface the PAUSE indicator has ridden since M2 — see <see cref="ShellOverlay"/>.</para>
/// </summary>
public sealed class PauseMenu
{
    /// <summary>
    /// How far <see cref="PauseMenuItem.Rewind"/> and <see cref="PauseMenuItem.Ahead"/> travel:
    /// one second of console time at 60 Hz. A named leap rather than "hold the key and see"
    /// because the author's question mid-debugging is "what did the tick before this look like",
    /// and an exact, repeatable distance is what makes going back and coming forward land on the
    /// very same frame — the stage's own acceptance check.
    /// </summary>
    public const int JumpTicks = 60;

    /// <summary>Padding between the box's edge and the text inside it, in console pixels.</summary>
    private const int Padding = 2;

    /// <summary>
    /// Every line of the block is padded to this many characters, so the box is one width
    /// whatever the tick number is doing. Sixteen holds the longest item ("&gt;REWIND 60  BKSP")
    /// and a header up to "PAUSED  T 123456"; a tick too long for that is printed by
    /// <see cref="Header"/> without the word, because a cut number is a wrong number.
    /// </summary>
    private const int LineChars = 16;

    /// <summary>
    /// The item labels, indexed by <see cref="PauseMenuItem"/>. The two travelling rows say how
    /// far they travel by <b>asking</b> <see cref="JumpTicks"/> rather than by spelling the number
    /// again a dozen lines below the constant: the label and the leap are one fact, and the day
    /// they were two the menu could promise sixty and move some other number.
    /// </summary>
    private static readonly string[] _labels =
    {
        "RESUME", "STEP -1", "STEP +1", $"REWIND {JumpTicks}", $"AHEAD {JumpTicks}", "EXIT",
    };

    /// <summary>
    /// The direct key each item also answers to, or "" when the menu's own Up/Down+Enter is the
    /// only keyboard road to it. Advertised on the row for the same reason every editor tooltip
    /// names its hotkey (M9 stage 2.5's parity law): a verb reachable only by pointing is a verb
    /// half the console cannot do.
    /// </summary>
    private static readonly string[] _keys = { "ESC", ",", ".", "BKSP", "", "" };

    /// <summary>The whole menu when a cartridge is running.</summary>
    private static readonly PauseMenuItem[] _running =
    {
        PauseMenuItem.Resume, PauseMenuItem.StepBack, PauseMenuItem.StepForward,
        PauseMenuItem.Rewind, PauseMenuItem.Ahead, PauseMenuItem.Exit,
    };

    /// <summary>
    /// The same menu when there is nothing to step through — the author came into the editor
    /// from the library and pressed F1 (work order Р7). RESUME reads START; the four time verbs
    /// are not listed rather than listed dead, because a row that cannot act is a row that
    /// teaches the author the menu is broken.
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
    /// Up / Down. Wrapping, like the tab ring and for the same reason: with six rows a list that
    /// stops at the ends costs five presses to reach EXIT from RESUME and one to reach it the
    /// other way.
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
    /// the item list plus its header, centred on the screen. Centred rather than parked in a
    /// corner because it is modal — the whole console is waiting on it — and the corner is where
    /// this shell already prints the things that are <em>not</em> waiting on you (the PAUSE and
    /// REC indicators, bottom left).
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
    /// have to find the three-pixel glyphs. The row is <paramref name="index"/> + 1 because row
    /// zero is the header.
    /// </summary>
    public Rectangle ItemRect(int index, int screenWidth, int screenHeight)
    {
        Rectangle box = Box(screenWidth, screenHeight);
        Point origin = TextOrigin(screenWidth, screenHeight);
        return new Rectangle(
            box.X, origin.Y + (index + 1) * SystemFont.CellHeight, box.Width, SystemFont.CellHeight);
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
    /// The block of text the overlay prints: the header, then one row per item with the cursor
    /// on the selected one. Built here rather than in the painter so the row the pointer hits
    /// (<see cref="ItemRect"/>) and the row the eye reads are the same arithmetic.
    /// </summary>
    /// <param name="tick">The live session's tick, or null when nothing is running.</param>
    public string Text(int? tick)
    {
        var lines = new string[LineCount];
        lines[0] = Pad(Header(tick));
        for (int i = 0; i < Items.Count; i++)
        {
            PauseMenuItem item = Items[i];
            string label = item == PauseMenuItem.Resume && !GameRunning ? "START" : _labels[(int)item];
            string key = _keys[(int)item];
            string row = (i == _selected ? ">" : " ") + label;
            lines[i + 1] = Pad(row.PadRight(LineChars - key.Length) + key);
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The first line: what is standing still, and at which tick. The word "PAUSED" is dropped
    /// rather than the digits when the two together do not fit in <see cref="LineChars"/> — a
    /// session past tick 999999 (four and a half hours at 60 Hz, and every session a rewind has
    /// walked twice) used to print "PAUSED  T 100000" for tick 1000000, which is not a shortened
    /// number but a different one, on the one line the author reads to know where they are. The
    /// bare form fits every <see cref="int"/> there is: "T 2147483647" is twelve characters.
    /// </summary>
    private static string Header(int? tick)
    {
        if (tick is not int at)
        {
            return "NO GAME RUNNING";
        }
        string digits = at.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string full = $"PAUSED  T {digits}";
        return full.Length <= LineChars ? full : $"T {digits}";
    }

    /// <summary>Header plus one row per item.</summary>
    private int LineCount => Items.Count + 1;

    private static string Pad(string line) =>
        line.Length >= LineChars ? line[..LineChars] : line.PadRight(LineChars);
}
