using System.Globalization;
using Quarp.Api;
using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// A scripted input track for <c>quarp sim</c>, <c>quarp replay record</c> and friends: what
/// player 0 holds — and, since ADR-030, where the pointer stands — on each tick, written as
/// <c>tick:spec</c> entries.
///
/// <para>It exists because a reference replay recorded with no input at all proves less than
/// it looks like it does. A cartridge left alone tends to reach a terminal screen quickly —
/// carts/snake walks into a wall in about ninety ticks — and after that the frames stop
/// depending on most of the simulation. A scripted track keeps the game actually running for
/// the length of the recording, so the cross-architecture hash comparison (REPLAY-FORMAT §6)
/// is comparing live gameplay rather than a game-over screen.</para>
///
/// <para><b>Grammar</b>: <c>tick:spec</c> entries separated by commas <em>or newlines</em>,
/// with <c>#</c> starting a comment that runs to the end of the line. A spec is one of two
/// kinds, telling two independent tracks apart by its first character:</para>
///
/// <para><b>Buttons</b> — <c>tick:buttons</c>, letters <c>L R U D O X S</c> (Start),
/// case-insensitive; an empty list releases everything. Sets the held mask from that tick
/// until the next <em>button</em> entry. Player 1 is always idle — a scripted golden is a
/// determinism fixture, not a two-player recording.</para>
///
/// <para><b>Pointer</b> — <c>tick:m&lt;x&gt;.&lt;y&gt;[L][R][M][w&lt;steps&gt;]</c>, e.g.
/// <c>60:m80.45L</c> — pointer at (80,45) with the left button held from tick 60 until the
/// next <em>pointer</em> entry. <c>x.y</c> is the position in console screen pixels (the
/// console clamps to its screen); <c>L R M</c> are the held mouse buttons; <c>w</c> is the
/// wheel in signed whole steps <em>per tick</em>. Before the first pointer entry the pointer
/// is parked at (0,0) with nothing held. The two tracks are independent: their entries
/// interleave freely and may name the same tick, and each track's ticks must increase
/// strictly within itself.</para>
///
/// <para>Because most carts turn on the edge calls — <see cref="IConsoleApi.Btnp"/> and
/// <see cref="IConsoleApi.MouseBtnp"/>, pressed this tick and not the last — a <em>tap</em> or
/// a <em>click</em> is two entries: <c>"60:D,61:"</c>, or <c>"60:m80.45L,61:m80.45"</c>. The
/// wheel has the matching trap in the other direction: <c>w1</c> is a delta repeated
/// <em>every tick</em> until the next pointer entry, so one notch is <c>"60:m80.45w1,61:m80.45"</c>.
/// Both are real traps and the reason they are spelled out in the usage text.</para>
/// </summary>
public sealed class InputScript
{
    private static readonly InputScript EmptyScript = new(
        Array.Empty<int>(), Array.Empty<byte>(), Array.Empty<int>(), Array.Empty<MouseSpec>());

    /// <summary>
    /// Entry separators. A comma is the one-liner form used on a command line; newlines are
    /// what make a script file (<c>--input-file</c>) readable, and a script long enough to
    /// keep a cartridge alive for thousands of ticks has to live in a file — the golden
    /// replay's track is a few hundred entries.
    /// </summary>
    private static readonly char[] Separators = { ',', '\n', '\r' };

    /// <summary>One pointer entry's payload: position, held mouse buttons, per-tick wheel.</summary>
    private readonly record struct MouseSpec(byte X, byte Y, byte Buttons, sbyte Wheel);

    private readonly int[] _ticks;
    private readonly byte[] _masks;
    private readonly int[] _mouseTicks;
    private readonly MouseSpec[] _mouse;
    private int _cursor;
    private int _mouseCursor;

    private InputScript(int[] ticks, byte[] masks, int[] mouseTicks, MouseSpec[] mouse)
    {
        _ticks = ticks;
        _masks = masks;
        _mouseTicks = mouseTicks;
        _mouse = mouse;
    }

    /// <summary>Nothing held, pointer parked, ever — the default for a headless reference recording.</summary>
    public static InputScript Empty => EmptyScript;

    /// <summary>Number of input changes in the script, both tracks together.</summary>
    public int EntryCount => _ticks.Length + _mouseTicks.Length;

    /// <summary>
    /// Parses the <c>--input</c> spec. Null or empty gives <see cref="Empty"/>.
    /// Throws <see cref="FormatException"/> with a message meant for a terminal.
    /// </summary>
    public static InputScript Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return EmptyScript;
        }

        string[] entries = StripComments(spec).Split(Separators, StringSplitOptions.TrimEntries);
        var ticks = new List<int>(entries.Length);
        var masks = new List<byte>(entries.Length);
        var mouseTicks = new List<int>();
        var mouse = new List<MouseSpec>();
        foreach (string entry in entries)
        {
            if (entry.Length == 0)
            {
                continue;   // A trailing comma is not worth an error message.
            }
            int colon = entry.IndexOf(':');
            if (colon < 0)
            {
                throw new FormatException($"input entry '{entry}' has no ':' — write it as tick:buttons or tick:mX.Y.");
            }
            // Invariant and NumberStyles.None, like every other number this tool reads
            // (SPEC-8 §7): a track file is an input of a reproduction, so the tick it names has
            // to mean the same integer on the machine that records the replay and on the one
            // that plays it back. The strict style also keeps "60 " and "+60" out of a format
            // whose only legal tick is a run of digits.
            if (!int.TryParse(entry.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int tick)
                || tick < 0)
            {
                throw new FormatException($"input entry '{entry}' does not start with a tick number.");
            }

            ReadOnlySpan<char> body = entry.AsSpan(colon + 1).Trim();
            if (body.Length > 0 && (body[0] == 'm' || body[0] == 'M'))
            {
                // Each track is ordered on its own: a button entry and a pointer entry may
                // share a tick — one hand on the keys, one on the mouse is a legal frame.
                if (mouseTicks.Count > 0 && tick <= mouseTicks[^1])
                {
                    throw new FormatException(
                        $"input entry '{entry}' goes backwards: pointer ticks must increase, "
                        + $"and {tick} follows {mouseTicks[^1]}.");
                }
                mouseTicks.Add(tick);
                mouse.Add(ParseMouse(body[1..], entry));
            }
            else
            {
                if (ticks.Count > 0 && tick <= ticks[^1])
                {
                    throw new FormatException(
                        $"input entry '{entry}' goes backwards: ticks must increase, and {tick} follows {ticks[^1]}.");
                }
                ticks.Add(tick);
                masks.Add(ParseMask(body, entry));
            }
        }
        return ticks.Count == 0 && mouseTicks.Count == 0
            ? EmptyScript
            : new InputScript(ticks.ToArray(), masks.ToArray(), mouseTicks.ToArray(), mouse.ToArray());
    }

    /// <summary>
    /// What is held on <paramref name="tick"/>: each track's last entry at or before it,
    /// combined into one snapshot. Sequential calls are O(1) — the recorder walks ticks in order.
    /// </summary>
    public InputState At(int tick)
    {
        byte mask = 0;
        if (_ticks.Length > 0)
        {
            if (_cursor > 0 && _ticks[_cursor] > tick)
            {
                _cursor = 0;    // Someone rewound; start the walk again.
            }
            while (_cursor + 1 < _ticks.Length && _ticks[_cursor + 1] <= tick)
            {
                _cursor++;
            }
            if (tick >= _ticks[0])
            {
                mask = _masks[_cursor];
            }
        }

        if (_mouseTicks.Length == 0)
        {
            return new InputState(mask, 0);
        }
        if (_mouseCursor > 0 && _mouseTicks[_mouseCursor] > tick)
        {
            _mouseCursor = 0;
        }
        while (_mouseCursor + 1 < _mouseTicks.Length && _mouseTicks[_mouseCursor + 1] <= tick)
        {
            _mouseCursor++;
        }
        if (tick < _mouseTicks[0])
        {
            return new InputState(mask, 0);
        }
        MouseSpec state = _mouse[_mouseCursor];
        return new InputState(mask, 0, state.X, state.Y, state.Buttons, state.Wheel);
    }

    /// <summary>
    /// Drops <c>#</c> comments, keeping line structure. Only meaningful for script files:
    /// a comment is how a generated track explains which lap of the field it is walking.
    /// A spec with no <c>#</c> is returned untouched, so the command-line form never pays
    /// for this.
    /// </summary>
    private static string StripComments(string spec)
    {
        if (!spec.Contains('#', StringComparison.Ordinal))
        {
            return spec;
        }
        var stripped = new System.Text.StringBuilder(spec.Length);
        foreach (ReadOnlySpan<char> line in spec.AsSpan().EnumerateLines())
        {
            int comment = line.IndexOf('#');
            stripped.Append(comment < 0 ? line : line[..comment]).Append('\n');
        }
        return stripped.ToString();
    }

    private static byte ParseMask(ReadOnlySpan<char> buttons, string entry)
    {
        byte mask = 0;
        foreach (char c in buttons)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }
            Button button = char.ToUpperInvariant(c) switch
            {
                'L' => Button.Left,
                'R' => Button.Right,
                'U' => Button.Up,
                'D' => Button.Down,
                'O' => Button.O,
                'X' => Button.X,
                'S' => Button.Start,
                _ => throw new FormatException(
                    $"input entry '{entry}' names an unknown button '{c}' — use L R U D O X S, "
                    + "or start the entry with 'm' for the pointer."),
            };
            mask |= (byte)(1 << (int)button);
        }
        return mask;
    }

    /// <summary>The pointer spec after its <c>m</c>: <c>&lt;x&gt;.&lt;y&gt;</c>, then held buttons, then <c>w&lt;steps&gt;</c>.</summary>
    private static MouseSpec ParseMouse(ReadOnlySpan<char> body, string entry)
    {
        int cursor = 0;
        int x = ParseCoordinate(body, ref cursor, entry, "x");
        if (cursor >= body.Length || body[cursor] != '.')
        {
            throw new FormatException($"input entry '{entry}' needs '.' between the pointer's x and y — write mX.Y.");
        }
        cursor++;
        int y = ParseCoordinate(body, ref cursor, entry, "y");

        byte buttons = 0;
        sbyte wheel = 0;
        while (cursor < body.Length)
        {
            char c = body[cursor];
            if (char.IsWhiteSpace(c))
            {
                cursor++;
                continue;
            }
            char upper = char.ToUpperInvariant(c);
            if (upper == 'W')
            {
                cursor++;
                wheel = ParseWheel(body, ref cursor, entry);
                continue;
            }
            MouseButton button = upper switch
            {
                'L' => MouseButton.Left,
                'R' => MouseButton.Right,
                'M' => MouseButton.Middle,
                _ => throw new FormatException(
                    $"input entry '{entry}' names an unknown pointer flag '{c}' — use L R M for buttons, w<steps> for the wheel."),
            };
            buttons |= (byte)(1 << (int)button);
            cursor++;
        }
        return new MouseSpec((byte)x, (byte)y, buttons, wheel);
    }

    private static int ParseCoordinate(ReadOnlySpan<char> body, ref int cursor, string entry, string axis)
    {
        int start = cursor;
        while (cursor < body.Length && char.IsAsciiDigit(body[cursor]))
        {
            cursor++;
        }
        if (cursor == start
            || !int.TryParse(body[start..cursor], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value > byte.MaxValue)
        {
            // 255 is the format's ceiling (REPLAY-FORMAT §3), not the screen's: the console
            // clamps to its screen deterministically, so 200 on a 160-wide screen is legal
            // input that reads back as 159.
            throw new FormatException(
                $"input entry '{entry}' has no pointer {axis} in 0..255 — write mX.Y, e.g. m80.45.");
        }
        return value;
    }

    private static sbyte ParseWheel(ReadOnlySpan<char> body, ref int cursor, string entry)
    {
        int start = cursor;
        if (cursor < body.Length && body[cursor] == '-')
        {
            cursor++;
        }
        while (cursor < body.Length && char.IsAsciiDigit(body[cursor]))
        {
            cursor++;
        }
        if (cursor == start || (cursor == start + 1 && body[start] == '-')
            || !int.TryParse(
                body[start..cursor], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            || value < sbyte.MinValue || value > sbyte.MaxValue)
        {
            throw new FormatException(
                $"input entry '{entry}' has no wheel step count after 'w' — write w1 or w-2 (whole steps, -128..127).");
        }
        return (sbyte)value;
    }
}
