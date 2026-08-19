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
/// make it fail, so <see cref="OneCorruptedSpritePixelMovesTheHash"/> is the negative control
/// (PLAYBOOK §3): flip one hex digit in the sprite pattern text, in the <c>Std</c> cartridge
/// only, and show the hash moves. The comparison is live, not a tautology.</para>
///
/// <para>The three cartridge sources duplicate the same sprite pattern literal on purpose — this
/// is test fixture data, not production logic, and the point of the exercise is that the three
/// literals are byte-identical (or, in the corrupted one, differ by exactly one character).</para>
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
    /// demos used before this stage (<c>carts/platformer/src/main.cs:862</c> for the sprite
    /// stamp, <c>carts/shmup/src/main.cs:676</c> for 0-9999 <c>PrintInt</c>, the
    /// <c>(ScreenWidth - text.Length * GlyphW) / 2</c> line for centering, the same formula
    /// minus one for right alignment). No reference to <c>Std</c> anywhere in this source.
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
    /// M4 work order criterion for this stage: "лимит кода стенда не изменился от
    /// использования Std" — <see cref="CodeBudget"/> only ever measures the cartridge's own
    /// <see cref="CartSourceFile"/> text, and <c>Std.cs</c> lives compiled into
    /// <c>Quarp.Api.dll</c>, never in that list, so this is true by construction; the assertion
    /// below is the concrete number, not just the argument. The Std cartridge is strictly
    /// smaller because it has no local helper bodies to spend bytes on at all.
    /// </summary>
    [Fact]
    public void UsingStdCostsFewerCodeBudgetBytesThanTheEquivalentLocalHelpers()
    {
        int stdBytes = CodeBudget.Measure(new[] { new CartSourceFile("src/main.cs", StdCart) });
        int localBytes = CodeBudget.Measure(new[] { new CartSourceFile("src/main.cs", LocalCart) });

        Assert.True(
            stdBytes < localBytes,
            $"expected the Std cartridge ({stdBytes} bytes) to cost less budget than the local-helper one ({localBytes} bytes)");
    }
}
