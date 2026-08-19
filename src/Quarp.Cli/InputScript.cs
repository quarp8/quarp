using System.Globalization;
using Quarp.Api;
using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// A scripted button track for <c>quarp replay record</c>: what player 0 holds on each tick,
/// written as <c>tick:buttons</c> entries.
///
/// <para>It exists because a reference replay recorded with no input at all proves less than
/// it looks like it does. A cartridge left alone tends to reach a terminal screen quickly —
/// carts/snake walks into a wall in about ninety ticks — and after that the frames stop
/// depending on most of the simulation. A scripted track keeps the game actually running for
/// the length of the recording, so the cross-architecture hash comparison (REPLAY-FORMAT §6)
/// is comparing live gameplay rather than a game-over screen.</para>
///
/// <para><b>Grammar</b>: <c>tick:buttons</c> separated by commas <em>or newlines</em>, ticks
/// strictly increasing, and <c>#</c> starting a comment that runs to the end of the line.
/// Each entry sets the held mask from that tick until the next entry; letters are
/// <c>L R U D O X S</c> (Start), case-insensitive, and an empty button list releases
/// everything. Player 1 is always idle — a scripted golden is a determinism fixture, not a
/// two-player recording, and adding a second track would double the grammar for nothing.</para>
///
/// <para>Because most carts turn on <see cref="IConsoleApi.Btnp"/> — pressed this tick and
/// not the last — a <em>tap</em> is two entries: <c>"60:D,61:"</c>. Holding a direction from
/// tick 0 to the end therefore produces exactly one turn, which is a real trap and the reason
/// it is spelled out in the usage text.</para>
/// </summary>
public sealed class InputScript
{
    private static readonly InputScript EmptyScript = new(Array.Empty<int>(), Array.Empty<byte>());

    /// <summary>
    /// Entry separators. A comma is the one-liner form used on a command line; newlines are
    /// what make a script file (<c>--input-file</c>) readable, and a script long enough to
    /// keep a cartridge alive for thousands of ticks has to live in a file — the golden
    /// replay's track is a few hundred entries.
    /// </summary>
    private static readonly char[] Separators = { ',', '\n', '\r' };

    private readonly int[] _ticks;
    private readonly byte[] _masks;
    private int _cursor;

    private InputScript(int[] ticks, byte[] masks)
    {
        _ticks = ticks;
        _masks = masks;
    }

    /// <summary>Nothing held, ever — the default for a headless reference recording.</summary>
    public static InputScript Empty => EmptyScript;

    /// <summary>Number of button changes in the script.</summary>
    public int EntryCount => _ticks.Length;

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
        foreach (string entry in entries)
        {
            if (entry.Length == 0)
            {
                continue;   // A trailing comma is not worth an error message.
            }
            int colon = entry.IndexOf(':');
            if (colon < 0)
            {
                throw new FormatException($"input entry '{entry}' has no ':' — write it as tick:buttons.");
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
            if (ticks.Count > 0 && tick <= ticks[^1])
            {
                throw new FormatException(
                    $"input entry '{entry}' goes backwards: ticks must increase, and {tick} follows {ticks[^1]}.");
            }
            ticks.Add(tick);
            masks.Add(ParseMask(entry.AsSpan(colon + 1), entry));
        }
        return ticks.Count == 0 ? EmptyScript : new InputScript(ticks.ToArray(), masks.ToArray());
    }

    /// <summary>
    /// What is held on <paramref name="tick"/>: the mask of the last entry at or before it.
    /// Sequential calls are O(1) — the recorder walks ticks in order.
    /// </summary>
    public InputState At(int tick)
    {
        if (_ticks.Length == 0)
        {
            return default;
        }
        if (_cursor > 0 && _ticks[_cursor] > tick)
        {
            _cursor = 0;    // Someone rewound; start the walk again.
        }
        while (_cursor + 1 < _ticks.Length && _ticks[_cursor + 1] <= tick)
        {
            _cursor++;
        }
        return tick < _ticks[0] ? default : new InputState(_masks[_cursor], 0);
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
                    $"input entry '{entry}' names an unknown button '{c}' — use L R U D O X S."),
            };
            mask |= (byte)(1 << (int)button);
        }
        return mask;
    }
}
