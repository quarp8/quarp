using System.Collections.Immutable;

namespace Quarp.Api;

/// <summary>
/// The cartridge standard library (ADR-019, M4 work order Р27/Р28/Р30/Р31 — "Этап 4.1").
/// Every member below was <em>extracted</em> from code the demo cartridges wrote for
/// themselves, independently, more than once — the "oracle table" of confirmed repeats is
/// the per-member doc comment, each line pointing at a real <c>carts/*/src/main.cs</c> site.
/// Nothing here was designed ahead of that evidence (ADR-019: "библиотека — последней
/// задачей M4, после демо-игр... защита от библиотеки, которой никто не пользуется").
///
/// <para><b>Why a library and not more hardware.</b> <see cref="IConsoleApi"/> is the
/// permanent, tiny cartridge-facing surface (SPEC-8) and stays that way; <see cref="Std"/> is
/// ordinary C# compiled once into <c>Quarp.Api.dll</c> that can grow after ratification
/// without breaking that promise. It also costs a cartridge nothing: a call site is a few
/// bytes against the 64 KB code budget, not a copy of the loop it used to paste in.</para>
///
/// <para><b>Two shapes.</b> The drawing helpers (<see cref="PrintInt"/>,
/// <see cref="PrintCentered(IConsoleApi,string,int,byte,Font)"/>,
/// <see cref="PrintRight(IConsoleApi,string,int,byte,Font)"/>, <see cref="PaintPattern"/>) are
/// <see cref="IConsoleApi"/> extension methods, called the same way the built-in surface is —
/// <c>Q.PrintInt(...)</c> from a <see cref="Cartridge"/> subclass, now that
/// <see cref="Cartridge.Q"/> is <c>protected</c> (Р27). The pure helpers (<c>Clamp</c>,
/// <c>Min</c>, <c>Max</c>) touch no console at all and are plain statics.</para>
///
/// <para><b>The one exception to "call it like drawing."</b> <see cref="PaintPattern"/> writes
/// the sprite sheet through <c>Sset</c> the same way a hand-written loop would, so it carries
/// the same rule every other <c>Sset</c> call does: run it from <c>Init</c> or <c>Update</c>,
/// never <c>Draw</c> (SPEC-8 §7 rule 2), and <c>QRP1004</c> enforces it. <see cref="PaintPattern"/>'s
/// own doc comment records the one real gap this stage found while wiring that enforcement up
/// (a library extension method is not a member of <see cref="IConsoleApi"/> or
/// <see cref="Cartridge"/>, so the analyzer's original member-list walk missed it) and how wave
/// 1.5 closed it the same night — see that comment, and
/// <c>StdPurityTests.PaintPatternFromDrawIsCaughtByQRP1004</c> for the compiled proof that it
/// stays closed.</para>
/// </summary>
public static class Std
{
    // --- text: PrintInt / IntWidth -----------------------------------------------------------

    /// <summary>
    /// One glyph per digit, reused by every call so <c>PrintInt</c> allocates nothing.
    /// <see cref="ImmutableArray{T}"/> rather than a plain <c>string[]</c> on purpose (adversary
    /// review, M4 stage 4.1 fix wave, card З4): a plain array is a mutable container even behind
    /// a <c>readonly</c> field reference — <c>Digits[0] = "oops"</c> compiles — which is exactly
    /// the hidden-static-mutation shape SPEC-8 §7's determinism contract cannot allow into a
    /// library that grows without an ADR (Р31). <c>ImmutableArray&lt;T&gt;</c> has no such
    /// indexer setter, costs the same one-time allocation the literal array did, and indexes in
    /// the same O(1) it always has, so nothing about <see cref="PrintInt"/>'s "allocates
    /// nothing" claim changes.
    /// </summary>
    private static readonly ImmutableArray<string> Digits =
        ImmutableArray.Create("0", "1", "2", "3", "4", "5", "6", "7", "8", "9");

    /// <summary>Pixel advance of <see cref="Font.Small"/> — the 4x6 cell every demo's local <c>PrintInt</c>/<c>GlyphW</c> hardcoded as 4.</summary>
    private const int SmallAdvance = 4;

    /// <summary>Pixel advance of <see cref="Font.Large"/> — the 5x7 cell.</summary>
    private const int LargeAdvance = 5;

    private static int Advance(Font font) => font == Font.Large ? LargeAdvance : SmallAdvance;

    /// <summary>
    /// Prints a non-negative int with the small font, one digit at a time through <c>Print</c>,
    /// and returns the x after the last digit exactly like every <c>Print</c> overload does —
    /// so it chains and right-aligns the same way. One shared digit loop replaces four cartridges'
    /// worth of the same chain, each stopping at its own hand-picked ceiling:
    /// <c>carts/digger/src/main.cs:790 @ 790ab9a (pre-conversion)</c> (0-99),
    /// <c>carts/breakout/src/main.cs:501 @ 790ab9a (pre-conversion)</c> and
    /// <c>carts/platformer/src/main.cs:840 @ 790ab9a (pre-conversion)</c> (0-999),
    /// <c>carts/shmup/src/main.cs:676 @ 790ab9a (pre-conversion)</c> (0-9999).
    /// <c>carts/snake/src/main.cs:467 @ 790ab9a (pre-conversion)</c> is the same shape again and
    /// is deliberately not converted (M4 work order Р29: snake is out of scope for this stage).
    /// <para>None of the five ever printed a negative value, so there is no repeated behavior to
    /// extract for one — the choice made here is: clamp to 0 before the loop runs, the same
    /// "soft, never throw" rule <see cref="IConsoleApi"/> documents for every other out-of-range
    /// input, rather than let a negative index reach <c>Digits</c> and throw. A HUD that wants a
    /// signed counter prints its own '-' and passes <c>-value</c>.</para>
    /// </summary>
    public static int PrintInt(this IConsoleApi q, int value, int x, int y, byte color)
    {
        int magnitude = value < 0 ? 0 : value;
        int digits = DigitCount(magnitude);
        int place = Pow10(digits - 1);
        for (int i = 0; i < digits; i++)
        {
            x = q.Print(Digits[magnitude / place % 10], x, y, color);
            place /= 10;
        }
        return x;
    }

    /// <summary>
    /// Pixel width a call to <see cref="PrintInt"/> will draw <paramref name="value"/> in —
    /// for reserving space or right-aligning before the text exists, like
    /// <c>carts/snake/src/main.cs:367 @ 790ab9a (pre-conversion)</c>'s right-aligned "BEST n"
    /// (kept local, Р29). Same clamp-to-0 as <see cref="PrintInt"/> for a negative input, so the
    /// two never disagree.
    /// <para><b>A real mismatch found while extracting this, for the wave-2 conversion to read
    /// before it touches platformer:</b>
    /// <c>carts/platformer/src/main.cs:853 @ 790ab9a (pre-conversion)</c>'s local
    /// <c>IntWidth</c> returns a <em>digit count</em> (1, 2 or 3), not a pixel width, unlike the
    /// three other demos that defined one (breakout, snake, shmup all return <c>GlyphW * n</c>).
    /// Platformer's one call site
    /// (<c>carts/platformer/src/main.cs:762 @ 790ab9a</c>, pre-conversion) multiplies the result
    /// by <c>GlyphW</c> itself afterward. This method follows the majority and returns pixels;
    /// the platformer call site needs its own arithmetic adjusted at conversion time, not this
    /// method.</para>
    /// </summary>
    public static int IntWidth(int value)
    {
        int magnitude = value < 0 ? 0 : value;
        return DigitCount(magnitude) * SmallAdvance;
    }

    /// <summary>Number of base-10 digits in a non-negative int; 0 counts as one digit ("0").</summary>
    private static int DigitCount(int value)
    {
        int digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }
        return digits;
    }

    private static int Pow10(int exponent)
    {
        int result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10;
        }
        return result;
    }

    // --- text: PrintCentered / PrintRight -----------------------------------------------------

    /// <summary>
    /// Prints <paramref name="text"/> horizontally centered on the screen at row
    /// <paramref name="y"/> with <see cref="Font.Small"/> — the common case, and a plain
    /// overload rather than a default-parameter forward to the 5-argument form below for the
    /// same reason <see cref="IConsoleApi.Print(string,int,int,byte)"/> is a separate overload
    /// and not <c>Print(text, x, y, color, Font.Small = default)</c>: a default argument value is
    /// baked into every caller's call site at compile time, so it cannot be tested by breaking
    /// the default and watching a caller move — the "which font is the default" fact would live
    /// in this method's metadata as many times as there are call sites, not once. This one-line
    /// overload is the guard; see the 5-argument overload just below for the formula itself.
    /// </summary>
    public static int PrintCentered(this IConsoleApi q, string text, int y, byte color) =>
        q.PrintCentered(text, y, color, Font.Small);

    /// <summary>
    /// Prints <paramref name="text"/> horizontally centered on the screen at row
    /// <paramref name="y"/> — the <c>(ScreenWidth - text.Length * advance) / 2</c> formula that
    /// recurs across all six demos. Directly, on one string, in
    /// <c>carts/shmup/src/main.cs:635,639,656,661,671 @ 790ab9a (pre-conversion)</c> and
    /// <c>carts/breakout/src/main.cs:487,496 @ 790ab9a (pre-conversion)</c>. <c>carts/dialogue</c>'s
    /// <c>Centered</c> helper is NOT this formula and stays local on purpose: it centers inside
    /// the prose window (<c>_wideX .. _wideX + _wideCols*GlyphW</c>), and integer truncation of
    /// the window width lands its result 1 px left of a whole-screen center — converting it
    /// moved the cart's frame hash, proven by run during the stage-4.1 conversion. Two demos
    /// apply the same formula in two steps instead of one — a panel is
    /// centered on the screen, then a title is centered again inside the panel
    /// (<c>carts/digger/src/main.cs:776+781 @ 790ab9a (pre-conversion)</c>,
    /// <c>carts/platformer/src/main.cs:812+819 @ 790ab9a (pre-conversion)</c>) — which this
    /// method does not collapse into one call; it matches the direct, one-string shape exactly.
    /// <c>carts/snake/src/main.cs:457 @ 790ab9a (pre-conversion)</c> is the direct shape again
    /// and stays local (Р29). Returns the x after the last glyph, like every <c>Print</c>
    /// overload.
    /// </summary>
    public static int PrintCentered(this IConsoleApi q, string text, int y, byte color, Font font)
    {
        int width = text.Length * Advance(font);
        int x = (q.ScreenWidth - width) / 2;
        return q.Print(text, x, y, color, font);
    }

    /// <summary>
    /// Prints <paramref name="text"/> right-aligned with <see cref="Font.Small"/> — see
    /// <see cref="PrintCentered(IConsoleApi,string,int,byte)"/> for why this is a one-line
    /// overload and not a default parameter on the 5-argument form.
    /// </summary>
    public static int PrintRight(this IConsoleApi q, string text, int y, byte color) =>
        q.PrintRight(text, y, color, Font.Small);

    /// <summary>
    /// Prints <paramref name="text"/> right-aligned, starting at
    /// <c>x = ScreenWidth - 1 - width</c> — the exact expression every hand-written
    /// right-aligned label this method replaces used, "-1" included. That places the last CELL
    /// (the last glyph plus the 1 px of trailing advance every <see cref="Font"/> spends between
    /// characters) at <c>[ScreenWidth - 1 - advance, ScreenWidth - 2]</c>: it <b>ends at
    /// <c>ScreenWidth - 2</c>, not <c>ScreenWidth - 1</c></b>. The glyph's own ink is one column
    /// short of that again — the trailing pixel of the cell is spacing, never drawn — so the
    /// rightmost lit pixel is at <c>ScreenWidth - 3</c>. This is the same "-1 bias" that
    /// <c>carts/digger</c>'s and <c>carts/snake</c>'s own right-aligned labels always drew with,
    /// not a rounding choice made here:
    /// <c>carts/digger/src/main.cs:721 @ 790ab9a (pre-conversion)</c>'s one-string "EXIT
    /// OPEN"/"EXIT SHUT" status line is the direct match.
    /// <c>carts/snake/src/main.cs:367 @ 790ab9a (pre-conversion)</c> uses the same formula but on
    /// a composite label-plus-number layout ("BEST n" — two <c>Print</c> calls, not one), so it
    /// stays local (Р29) rather than being a second direct call site. Returns the x after the
    /// last glyph, like every <c>Print</c> overload.
    /// </summary>
    public static int PrintRight(this IConsoleApi q, string text, int y, byte color, Font font)
    {
        int width = text.Length * Advance(font);
        int x = q.ScreenWidth - 1 - width;
        return q.Print(text, x, y, color, font);
    }

    // --- sprites: PaintPattern -----------------------------------------------------------------

    /// <summary>
    /// Stamps a sprite-sheet block from ASCII rows at (<paramref name="sheetX"/>,
    /// <paramref name="sheetY"/>): <c>'.'</c> skips the pixel — <c>Sset</c> is not called for
    /// it, so whatever color was already on the sheet there is untouched — and any hex digit
    /// <c>0</c>-<c>f</c> writes that color, including <c>0</c> itself, which is a real write and
    /// not the same thing as skipping. Lifted from
    /// <c>carts/platformer/src/main.cs:862 @ 790ab9a (pre-conversion)</c> (<c>PaintSheet</c>
    /// plus its <c>HexValue</c> helper), the canonical dialect per M4 work order Р28.
    /// <c>carts/shmup/src/main.cs:710 @ 790ab9a (pre-conversion)</c> paints sprites from text
    /// too, but in a different dialect — one fixed color for every non-'.' pixel rather than a
    /// color per pixel — so the wave-2 conversion reformats shmup's two pattern tables into hex
    /// digits rather than this method growing a second dialect to match them.
    /// <para><b>Writes the sprite sheet — call it from <c>Init</c> or <c>Update</c>, never
    /// <c>Draw</c></b>, the same rule as a direct <c>Sset</c> call (SPEC-8 §7 rule 2), and the
    /// <c>QRP1004</c> analyzer enforces it here too: <c>MutatingConsoleApi</c> walks this class
    /// alongside <see cref="IConsoleApi"/> and <see cref="Cartridge"/> and unwraps reduced
    /// extension-method symbols, so a call from <c>Draw</c> is a compile error, proven both ways
    /// by <c>StdPurityTests</c> (red from <c>Draw</c>, clean from <c>Init</c>/<c>Update</c>).
    /// The gap this note used to describe was found by stage 4.1's wave 1 and closed by wave
    /// 1.5 the same night.</para>
    /// </summary>
    public static void PaintPattern(this IConsoleApi q, int sheetX, int sheetY, string[] rows)
    {
        for (int y = 0; y < rows.Length; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                int slot = HexDigit(row[x]);
                if (slot >= 0)
                {
                    q.Sset(sheetX + x, sheetY + y, (byte)slot);
                }
            }
        }
    }

    /// <summary>0-15 for a hex digit, -1 for anything else (the pattern dialect's '.').</summary>
    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }
        if (c >= 'a' && c <= 'f')
        {
            return 10 + (c - 'a');
        }
        return -1;
    }

    // --- clamps: int ---------------------------------------------------------------------------

    /// <summary>
    /// Clamps <paramref name="value"/> into [<paramref name="min"/>, <paramref name="max"/>].
    /// Generalizes the two-argument, implicit-zero-floor form two demos wrote for themselves —
    /// <c>carts/digger/src/main.cs:760 @ 790ab9a (pre-conversion)</c>,
    /// <c>carts/platformer/src/main.cs:705 @ 790ab9a (pre-conversion)</c> — to an explicit lower
    /// bound. Call it with <c>min: 0</c> to reproduce either one: both are camera clamps that
    /// only ever passed a non-negative <c>max</c>, so nothing changes for either call site at
    /// that argument.
    /// <para><b>Soft rule for an inverted range (<c>min &gt; max</c>): always returns
    /// <paramref name="min"/>, never throws.</b> <see cref="System.Math.Clamp(int,int,int)"/>
    /// throws <see cref="ArgumentException"/> on <c>min &gt; max</c>; every other out-of-range
    /// input on <see cref="IConsoleApi"/> is documented "soft, never throw" instead (a bad frame
    /// keeps running rather than crashing the console), and this method follows that house rule
    /// rather than the BCL's. <b>Adversary review, M4 stage 4.1 fix wave (card В6):</b> the
    /// naive three-way expression this method used to be —
    /// <c>value &lt; min ? min : value &gt; max ? max : value</c> — does <em>not</em> have this
    /// property: for <c>min &gt; max</c> it returns <paramref name="min"/> only while
    /// <c>value &lt; min</c>, and <paramref name="max"/> (the <em>smaller</em> bound) for every
    /// <c>value &gt;= min</c> — a discontinuous, unreasoned-about result nobody had checked
    /// against, not the constant "pins to min" a caller like a degenerate camera clamp
    /// (<c>carts/digger/src/main.cs</c>'s <c>DrawCave</c>, card В2) actually wants. The explicit
    /// <c>min &gt; max</c> guard below makes the documented rule true; it changes nothing for
    /// any real call site, because every one of them always passes <c>min &lt;= max</c>.</para>
    /// </summary>
    public static int Clamp(int value, int min, int max) =>
        min > max ? min : value < min ? min : value > max ? max : value;

    /// <summary>Smaller of two ints — half of <c>carts/breakout/src/main.cs:516 @ 790ab9a (pre-conversion)</c>'s local <c>MinInt</c>, used there for AABB overlap.</summary>
    public static int Min(int a, int b) => a < b ? a : b;

    /// <summary>Larger of two ints — <c>carts/breakout/src/main.cs:518 @ 790ab9a (pre-conversion)</c>'s local <c>MaxInt</c>.</summary>
    public static int Max(int a, int b) => a > b ? a : b;

    // --- clamps: Fix -----------------------------------------------------------------------------

    /// <summary>
    /// The <see cref="Fix"/> counterpart of the int <c>Clamp</c>. No demo repeated a
    /// <see cref="Fix"/> clamp — every clamp in the oracle table bounds an int pixel or tile
    /// coordinate — but M4 work order Р28 asks for it alongside the int form regardless: Fix is
    /// the project's one fractional type (SPEC-8 §7), and a fractional position is exactly what
    /// the next physics-carrying cartridge is likely to need bounded. Same inverted-range soft
    /// rule as <see cref="Clamp(int,int,int)"/> — see that overload's doc comment for why the
    /// explicit <c>min &gt; max</c> guard is there rather than left to the naive expression.
    /// </summary>
    public static Fix Clamp(Fix value, Fix min, Fix max) =>
        min > max ? min : value < min ? min : value > max ? max : value;

    /// <summary>Smaller of two <see cref="Fix"/> values — see <see cref="Clamp(Fix, Fix, Fix)"/> for why it ships without a demo repeat.</summary>
    public static Fix Min(Fix a, Fix b) => a < b ? a : b;

    /// <summary>Larger of two <see cref="Fix"/> values — see <see cref="Clamp(Fix, Fix, Fix)"/> for why it ships without a demo repeat.</summary>
    public static Fix Max(Fix a, Fix b) => a > b ? a : b;
}
