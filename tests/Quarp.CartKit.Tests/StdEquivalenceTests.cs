using System.Text;
using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// M4 stage 4.1 (Р29's invariant, applied one stage early to a purpose-built stand cartridge
/// rather than to a real demo): a cartridge written against <c>Quarp.Api.Std</c> must draw the
/// exact same frame, pixel for pixel, as one that copy-pastes the equivalent local helpers the
/// way <c>carts/breakout</c>, <c>carts/platformer</c>, <c>carts/digger</c> and
/// <c>carts/shmup</c> did before this stage. Two hand-written cartridges below exercise
/// <c>PaintPattern</c>, <c>PrintInt</c>, <c>PrintCentered</c> and <c>PrintRight</c> — one calling
/// <c>Std</c>, one reimplementing each helper inline — through the real compile-and-run
/// pipeline (<see cref="CartCompiler"/>, <see cref="CartHost"/>, <see cref="VirtualConsole"/>),
/// and their final frame hashes (<see cref="FrameHash"/>, the one formula the whole project
/// hashes a frame with — not reimplemented here) are compared directly.
///
/// <para>The equal-hash assertion alone would be a tautology if nothing in the scene could ever
/// make it fail, so there are two independent negative controls (PLAYBOOK §3), one per drawing
/// path: <see cref="OneCorruptedSpritePixelMovesTheHash"/> flips one hex digit in the sprite
/// pattern text (the <c>Sset</c>/<c>PaintPattern</c> path), and
/// <see cref="OneCorruptedPrintedCharacterMovesTheHash"/> (M4 stage 4.1 fix wave, card З2) flips
/// one character in a string a <c>Print</c>-family call actually draws (the text path
/// <c>PrintInt</c>/<c>PrintCentered</c>/<c>PrintRight</c> exercise) — both only in the
/// <c>Std</c> cartridge, both showing the hash moves. Neither path alone would prove the other
/// live: a scene could paint sprites correctly while drawing every string as blank, or vice
/// versa, and the sprite-only control would not have caught it.</para>
///
/// <para>The three cartridge sources duplicate the same sprite pattern literal on purpose — this
/// is test fixture data, not production logic, and the point of the exercise is that the three
/// literals are byte-identical (or, in a corrupted one, differ by exactly one character).</para>
/// </summary>
public class StdEquivalenceTests
{
    private const string StdCart = """
        using Quarp.Api;

        public sealed class StdStand : Cartridge
        {
            private static readonly string[] Pattern =
            {
                "01234567",
                "89abcdef",
                "fedcba98",
                "76543210",
                "0.1.2.3.",
                ".4.5.6.7",
                "8899aabb",
                "ccddeeff",
            };

            public override void Init()
            {
                Q.PaintPattern(0, 0, Pattern);
            }

            public override void Draw()
            {
                Cls(1);
                Spr(0, 4, 4);
                Q.PrintInt(4207, 4, 20, 7);
                Q.PrintCentered("HELLO", 40, 7);
                Q.PrintRight("BEST", 50, 7);
            }
        }
        """;

    /// <summary>
    /// Same picture, same call order, but every helper is a local copy in the style the real
    /// demos used before this stage
    /// (<c>carts/platformer/src/main.cs:862 @ 790ab9a (pre-conversion)</c> for the sprite
    /// stamp, <c>carts/shmup/src/main.cs:676 @ 790ab9a (pre-conversion)</c> for 0-9999
    /// <c>PrintInt</c>, the <c>(ScreenWidth - text.Length * GlyphW) / 2</c> line for centering,
    /// the same formula minus one for right alignment). No reference to <c>Std</c> anywhere in
    /// this source.
    /// </summary>
    private const string LocalCart = """
        using Quarp.Api;

        public sealed class LocalStand : Cartridge
        {
            private const int GlyphW = 4;

            private static readonly string[] Pattern =
            {
                "01234567",
                "89abcdef",
                "fedcba98",
                "76543210",
                "0.1.2.3.",
                ".4.5.6.7",
                "8899aabb",
                "ccddeeff",
            };

            private static readonly string[] Digits =
                { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

            public override void Init()
            {
                PaintSheet();
            }

            public override void Draw()
            {
                Cls(1);
                Spr(0, 4, 4);
                PrintInt(4207, 4, 20, 7);
                const string centered = "HELLO";
                Print(centered, (ScreenWidth - centered.Length * GlyphW) / 2, 40, 7);
                const string right = "BEST";
                Print(right, ScreenWidth - 1 - right.Length * GlyphW, 50, 7);
            }

            private void PaintSheet()
            {
                for (int y = 0; y < Pattern.Length; y++)
                {
                    string row = Pattern[y];
                    for (int x = 0; x < row.Length; x++)
                    {
                        int color = HexValue(row[x]);
                        if (color >= 0)
                        {
                            Sset(x, y, (byte)color);
                        }
                    }
                }
            }

            private static int HexValue(char c)
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

            private int PrintInt(int value, int x, int y, byte color)
            {
                if (value >= 1000)
                {
                    x = Print(Digits[value / 1000 % 10], x, y, color);
                }
                if (value >= 100)
                {
                    x = Print(Digits[value / 100 % 10], x, y, color);
                }
                if (value >= 10)
                {
                    x = Print(Digits[value / 10 % 10], x, y, color);
                }
                return Print(Digits[value % 10], x, y, color);
            }
        }
        """;

    /// <summary>The Std cartridge with one sprite hex digit flipped (row 0, column 0: '0' -> '7').</summary>
    private const string StdCartCorrupted = """
        using Quarp.Api;

        public sealed class StdStandCorrupted : Cartridge
        {
            private static readonly string[] Pattern =
            {
                "71234567",
                "89abcdef",
                "fedcba98",
                "76543210",
                "0.1.2.3.",
                ".4.5.6.7",
                "8899aabb",
                "ccddeeff",
            };

            public override void Init()
            {
                Q.PaintPattern(0, 0, Pattern);
            }

            public override void Draw()
            {
                Cls(1);
                Spr(0, 4, 4);
                Q.PrintInt(4207, 4, 20, 7);
                Q.PrintCentered("HELLO", 40, 7);
                Q.PrintRight("BEST", 50, 7);
            }
        }
        """;

    /// <summary>
    /// The Std cartridge with one character of a <em>printed</em> string flipped ("HELLO" ->
    /// "HELLX", the <c>Q.PrintCentered</c> call) rather than a sprite pixel — the negative
    /// control for the text-drawing path, card З2.
    /// </summary>
    private const string StdCartTextCorrupted = """
        using Quarp.Api;

        public sealed class StdStandTextCorrupted : Cartridge
        {
            private static readonly string[] Pattern =
            {
                "01234567",
                "89abcdef",
                "fedcba98",
                "76543210",
                "0.1.2.3.",
                ".4.5.6.7",
                "8899aabb",
                "ccddeeff",
            };

            public override void Init()
            {
                Q.PaintPattern(0, 0, Pattern);
            }

            public override void Draw()
            {
                Cls(1);
                Spr(0, 4, 4);
                Q.PrintInt(4207, 4, 20, 7);
                Q.PrintCentered("HELLX", 40, 7);
                Q.PrintRight("BEST", 50, 7);
            }
        }
        """;

    private static byte[] CompileSingleFile(string source, string cartName)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", source) }, cartName);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes!;
    }

    private static ulong RunAndHash(byte[] assembly)
    {
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        console.Tick(default);
        return FrameHash.Compute(console.Framebuffer);
    }

    [Fact]
    public void StdAndLocalHelpersDrawTheIdenticalFrame()
    {
        ulong stdHash = RunAndHash(CompileSingleFile(StdCart, "stdstand"));
        ulong localHash = RunAndHash(CompileSingleFile(LocalCart, "localstand"));

        Assert.Equal(localHash, stdHash);
    }

    /// <summary>
    /// Negative control for the assertion above: without this, "the two hashes match" could
    /// pass even if both cartridges secretly drew a blank screen. One flipped sprite hex digit,
    /// in the Std cartridge only, has to move the hash away from both baselines.
    /// </summary>
    [Fact]
    public void OneCorruptedSpritePixelMovesTheHash()
    {
        ulong stdHash = RunAndHash(CompileSingleFile(StdCart, "stdstand2"));
        ulong localHash = RunAndHash(CompileSingleFile(LocalCart, "localstand2"));
        ulong corruptedHash = RunAndHash(CompileSingleFile(StdCartCorrupted, "stdstandcorrupted"));

        Assert.NotEqual(stdHash, corruptedHash);
        Assert.NotEqual(localHash, corruptedHash);
    }

    /// <summary>
    /// Second negative control (M4 stage 4.1 fix wave, card З2): the sprite-pixel control above
    /// only proves the <c>Sset</c>/<c>PaintPattern</c> path is live. This flips a character in a
    /// string the <c>Print</c> family actually draws instead, so the text-drawing path — the one
    /// <c>PrintInt</c>, <c>PrintCentered</c> and <c>PrintRight</c> all funnel through — has its
    /// own proof that the comparison would notice a real difference there, not just in sprite
    /// data.
    /// </summary>
    [Fact]
    public void OneCorruptedPrintedCharacterMovesTheHash()
    {
        ulong stdHash = RunAndHash(CompileSingleFile(StdCart, "stdstand3"));
        ulong localHash = RunAndHash(CompileSingleFile(LocalCart, "localstand3"));
        ulong corruptedHash = RunAndHash(CompileSingleFile(StdCartTextCorrupted, "stdstandtextcorrupted"));

        Assert.NotEqual(stdHash, corruptedHash);
        Assert.NotEqual(localHash, corruptedHash);
    }

    /// <summary>
    /// M4 work order criterion for this stage: "лимит кода стенда не изменился от
    /// использования Std" — <see cref="CodeBudget"/> only ever measures the cartridge's own
    /// <see cref="CartSourceFile"/> text, and <c>Std.cs</c> lives compiled into
    /// <c>Quarp.Api.dll</c>, never passed to <see cref="CodeBudget.Measure"/> in that list.
    /// <b>Adversary review, M4 stage 4.1 fix wave, card З1:</b> the previous version of this test
    /// only compared two <em>different</em> cartridges' byte counts (<c>stdBytes &lt;
    /// localBytes</c>), which shows the Std cartridge is smaller than one particular
    /// hand-written alternative — a relative fact, not a proof that <c>Std.cs</c>'s own bytes
    /// are excluded. This version asserts the budget directly: <c>StdCart</c>'s measured budget
    /// equals an independent recount of <em>only its own source text</em>, computed without
    /// calling into <see cref="CodeBudget"/> at all. <c>StdCart</c> is deliberately
    /// comment-free (it is test fixture data, not a comment-heavy production file), so
    /// <c>CodeBudget</c>'s internal comment-stripping pass has nothing to strip for it, and a
    /// plain UTF-8 byte count with <c>\r\n</c> normalized to <c>\n</c> — the same
    /// normalization <c>Quarp.CartKit.CodeBudget</c>'s doc comment names — is the same number by
    /// construction, not a second implementation of the comment scan. If <c>Std.cs</c>'s own
    /// text were ever counted, or a stray extra byte crept in, this equality would break; it
    /// cannot pass by only proving one cartridge is smaller than another.
    /// </summary>
    [Fact]
    public void StdCartCodeBudgetEqualsExactlyItsOwnSourceBytes()
    {
        int stdBytes = CodeBudget.Measure(new[] { new CartSourceFile("src/main.cs", StdCart) });
        int independentBytes = Encoding.UTF8.GetByteCount(StdCart.Replace("\r\n", "\n"));

        Assert.Equal(independentBytes, stdBytes);
    }
}
