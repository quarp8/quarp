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
/// <see cref="PrintCentered"/>, <see cref="PrintRight"/>, <see cref="PaintPattern"/>) are
/// <see cref="IConsoleApi"/> extension methods, called the same way the built-in surface is —
/// <c>Q.PrintInt(...)</c> from a <see cref="Cartridge"/> subclass, now that
/// <see cref="Cartridge.Q"/> is <c>protected</c> (Р27). The pure helpers (<c>Clamp</c>,
/// <c>Min</c>, <c>Max</c>) touch no console at all and are plain statics.</para>
///
/// <para><b>The one exception to "call it like drawing."</b> <see cref="PaintPattern"/> writes
/// the sprite sheet through <c>Sset</c> the same way a hand-written loop would, so it carries
/// the same rule every other <c>Sset</c> call does: run it from <c>Init</c> or <c>Update</c>,
/// never <c>Draw</c> (SPEC-8 §7 rule 2). <see cref="PaintPattern"/>'s own doc comment records
/// a real gap found while wiring this up: the <c>QRP1004</c> analyzer currently cannot see
/// that this particular helper mutates state, because it is a library extension method and
/// not a member of <see cref="IConsoleApi"/> or <see cref="Cartridge"/> — see that comment,
/// and the M4 stage 4.1 report, "Расхождения".</para>
/// </summary>
public static class Std
{
    // --- text: PrintInt / IntWidth -----------------------------------------------------------

    /// <summary>One glyph per digit, reused by every call so <c>PrintInt</c> allocates nothing.</summary>
    private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    /// <summary>Pixel advance of <see cref="Font.Small"/> — the 4x6 cell every demo's local <c>PrintInt</c>/<c>GlyphW</c> hardcoded as 4.</summary>
    private const int SmallAdvance = 4;

    /// <summary>Pixel advance of <see cref="Font.Large"/> — the 5x7 cell.</summary>
    private const int LargeAdvance = 5;

    private static int Advance(Font font) => font == Font.Large ? LargeAdvance : SmallAdvance;

    /// <summary>
    /// Prints a non-negative int with the small font, one digit at a time through <c>Print</c>,
    /// and returns the x after the last digit exactly like every <c>Print</c> overload does —
    /// so it chains and right-aligns the same way. One shared digit loop replaces four cartridges'
    /// worth of the same chain, each stopping at its own hand-picked ceiling: <c>carts/digger/src/main.cs:790</c>
    /// (0-99), <c>carts/breakout/src/main.cs:501</c> and <c>carts/platformer/src/main.cs:840</c>
    /// (0-999), <c>carts/shmup/src/main.cs:676</c> (0-9999). <c>carts/snake/src/main.cs:467</c>
    /// is the same shape again and is deliberately not converted (M4 work order Р29: snake is
    /// out of scope for this stage).
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
    /// <c>carts/snake/src/main.cs:367</c>'s right-aligned "BEST n" (kept local, Р29). Same
    /// clamp-to-0 as <see cref="PrintInt"/> for a negative input, so the two never disagree.
    /// <para><b>A real mismatch found while extracting this, for the wave-2 conversion to read
    /// before it touches platformer:</b> <c>carts/platformer/src/main.cs:853</c>'s local
    /// <c>IntWidth</c> returns a <em>digit count</em> (1, 2 or 3), not a pixel width, unlike the
    /// three other demos that defined one (breakout, snake, shmup all return <c>GlyphW * n</c>).
    /// Platformer's one call site (line 762) multiplies the result by <c>GlyphW</c> itself
    /// afterward. This method follows the majority and returns pixels; the platformer call site
    /// needs its own arithmetic adjusted at conversion time, not this method.</para>
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
    /// <paramref name="y"/> — the <c>(ScreenWidth - text.Length * advance) / 2</c> formula that
    /// recurs across all six demos. Directly, on one string, in
    /// <c>carts/shmup/src/main.cs:635,639,656,661,671</c> and
    /// <c>carts/breakout/src/main.cs:487,496</c>. <c>carts/dialogue</c>'s <c>Centered</c>
    /// helper is NOT this formula and stays local on purpose: it centers inside the prose
    /// window (<c>_wideX .. _wideX + _wideCols*GlyphW</c>), and integer truncation of the
    /// window width lands its result 1 px left of a whole-screen center — converting it
    /// moved the cart's frame hash, proven by run during the stage-4.1 conversion. Two demos
    /// apply the same formula in two steps instead of one — a panel is
    /// centered on the screen, then a title is centered again inside the panel
    /// (<c>carts/digger/src/main.cs:776</c>+<c>781</c>, <c>carts/platformer/src/main.cs:812</c>+<c>819</c>)
    /// — which this method does not collapse into one call; it matches the direct, one-string
    /// shape exactly. <c>carts/snake/src/main.cs:457</c> is the direct shape again and stays
    /// local (Р29). Returns the x after the last glyph, like every <c>Print</c> overload.
    /// </summary>
    public static int PrintCentered(this IConsoleApi q, string text, int y, byte color, Font font = Font.Small)
    {
        int width = text.Length * Advance(font);
        int x = (q.ScreenWidth - width) / 2;
        return q.Print(text, x, y, color, font);
    }

    /// <summary>
    /// Prints <paramref name="text"/> right-aligned so its last pixel column lands on
    /// <c>ScreenWidth - 1</c> — <c>carts/digger/src/main.cs:721</c>'s one-string "EXIT
    /// OPEN"/"EXIT SHUT" status line is the direct match. <c>carts/snake/src/main.cs:367</c>
    /// uses the same "-1" bias formula but on a composite label-plus-number layout ("BEST n" —
    /// two <c>Print</c> calls, not one), so it stays local (Р29) rather than being a second
    /// direct call site. Returns the x after the last glyph, like every <c>Print</c> overload.
    /// </summary>
    public static int PrintRight(this IConsoleApi q, string text, int y, byte color, Font font = Font.Small)
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
    /// not the same thing as skipping. Lifted from <c>carts/platformer/src/main.cs:862</c>
    /// (<c>PaintSheet</c> plus its <c>HexValue</c> helper), the canonical dialect per M4 work
    /// order Р28. <c>carts/shmup/src/main.cs:710</c> paints sprites from text too, but in a
    /// different dialect — one fixed color for every non-'.' pixel rather than a color per pixel
    /// — so the wave-2 conversion reformats shmup's two pattern tables into hex digits rather
    /// than this method growing a second dialect to match them.
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
    /// <c>carts/digger/src/main.cs:760</c>, <c>carts/platformer/src/main.cs:705</c> — to an
    /// explicit lower bound. Call it with <c>min: 0</c> to reproduce either one: both are camera
    /// clamps that only ever passed a non-negative <c>max</c>, so nothing changes for either
    /// call site at that argument.
    /// </summary>
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    /// <summary>Smaller of two ints — half of <c>carts/breakout/src/main.cs:516</c>'s local <c>MinInt</c>, used there for AABB overlap.</summary>
    public static int Min(int a, int b) => a < b ? a : b;

    /// <summary>Larger of two ints — <c>carts/breakout/src/main.cs:518</c>'s local <c>MaxInt</c>.</summary>
    public static int Max(int a, int b) => a > b ? a : b;

    // --- clamps: Fix -----------------------------------------------------------------------------

    /// <summary>
    /// The <see cref="Fix"/> counterpart of the int <c>Clamp</c>. No demo repeated a
    /// <see cref="Fix"/> clamp — every clamp in the oracle table bounds an int pixel or tile
    /// coordinate — but M4 work order Р28 asks for it alongside the int form regardless: Fix is
    /// the project's one fractional type (SPEC-8 §7), and a fractional position is exactly what
    /// the next physics-carrying cartridge is likely to need bounded.
    /// </summary>
    public static Fix Clamp(Fix value, Fix min, Fix max) => value < min ? min : value > max ? max : value;

    /// <summary>Smaller of two <see cref="Fix"/> values — see <see cref="Clamp(Fix, Fix, Fix)"/> for why it ships without a demo repeat.</summary>
    public static Fix Min(Fix a, Fix b) => a < b ? a : b;

    /// <summary>Larger of two <see cref="Fix"/> values — see <see cref="Clamp(Fix, Fix, Fix)"/> for why it ships without a demo repeat.</summary>
    public static Fix Max(Fix a, Fix b) => a > b ? a : b;
}
