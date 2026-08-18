using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The determinism filters (SPEC-8 §7, ARCHITECTURE §3): float/double/decimal are banned
/// with file and line, the metadata scan bans OS-facing BCL APIs in the emitted DLL.
/// User errors are diagnostics, never exceptions.
///
/// M2 re-pin: the float ban is now reported by <c>Quarp.Analyzers</c> as <b>QRP1001</b>
/// rather than by CartCompiler's own syntax scan as QRP0001/QRP0002. The analyzer runs
/// inside CartCompiler so that the ban holds without an IDE (M2 work order), and it covers
/// exactly the same three shapes — keyword, name, real literal — with the same precision,
/// so keeping both would print every violation twice under two ids (API-8 §12).
/// The old scan survives as the fallback for a compilation the analyzer considers out of
/// scope or for an analyzer that fails to run; the IL scan (QRP0004) is unchanged and is
/// still the enforcement point.
/// </summary>
public class CartCompilerTests
{
    private static CartCompileResult Compile(string mainCs) =>
        CartCompiler.Compile(new[] { new CartSourceFile("src/main.cs", mainCs) }, "testcart");

    private const string ValidCart = """
        using Quarp.Api;

        public sealed class ValidCart : Cartridge
        {
            private int _t;
            public override void Update() => _t++;
            public override void Draw() => Cls((byte)(_t & 0x0F));
        }
        """;

    [Fact]
    public void ValidCartCompiles()
    {
        CartCompileResult result = Compile(ValidCart);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.AssemblyBytes);
    }

    [Fact]
    public void FloatLiteralIsBlockedWithLineNumber()
    {
        // `var` keeps the source free of banned type keywords, isolating the literal scan.
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BadCart : Cartridge
            {
                public override void Update()
                {
                    var speed = 1.5f;
                    _ = speed;
                }
            }
            """);
        Assert.False(result.Success);
        string diagnostic = Assert.Single(result.Diagnostics, d => d.Contains("QRP1001"));
        Assert.Contains("src/main.cs(7,", diagnostic);      // the literal sits on line 7
        Assert.Contains("1.5f", diagnostic);
        Assert.Contains("use int or Fix", diagnostic);      // the fix-it hint
    }

    [Fact]
    public void DoubleLiteralIsBlocked()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BadCart : Cartridge
            {
                public override void Update() { var x = 0.25; _ = x; }
            }
            """);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP1001") && d.Contains("0.25"));
    }

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    public void RealTypeKeywordsAreBlockedWithPosition(string keyword)
    {
        CartCompileResult result = Compile($$"""
            using Quarp.Api;

            public sealed class BadCart : Cartridge
            {
                private {{keyword}} _value;
                public override void Update() => _value = default;
            }
            """);
        Assert.False(result.Success);
        string diagnostic = Assert.Single(result.Diagnostics, d => d.Contains("QRP1001"));
        Assert.Contains($"'{keyword}'", diagnostic);
        Assert.Contains("src/main.cs(5,", diagnostic);      // the field sits on line 5
    }

    /// <summary>
    /// The fallback path: a compilation with no <c>Cartridge</c> subclass is out of the
    /// analyzer's scope by design (it must stay inert on engine code), so CartCompiler's own
    /// syntax scan has to keep reporting there. Pinning this is what makes it safe to let
    /// QRP1001 own the message everywhere else.
    /// </summary>
    [Fact]
    public void FloatIsStillReportedWithoutACartridgeSubclassToScopeTheAnalyzer()
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/helper.cs", "public static class Helper { public static double K => 0.5; }") },
            "helpercart");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0001") && d.Contains("'double'"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0002") && d.Contains("0.5"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("QRP1001"));
    }

    /// <summary>
    /// QRP1003 is a warning (API-8 §12): it must be reported without failing the build,
    /// because iterating a dictionary to sum numbers is legal and an error nobody can avoid
    /// suppressing stops being read at all.
    /// </summary>
    [Fact]
    public void DictionaryIterationWarnsButStillCompiles()
    {
        CartCompileResult result = Compile("""
            using System.Collections.Generic;
            using Quarp.Api;

            public sealed class WarnCart : Cartridge
            {
                private readonly Dictionary<int, int> _scores = new();

                public override void Update()
                {
                    int total = 0;
                    foreach (KeyValuePair<int, int> pair in _scores)
                    {
                        total += pair.Value;
                    }
                    _ = total;
                }
            }
            """);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Contains(result.Warnings, w => w.Contains("QRP1003"));
    }

    [Fact]
    public void FileReadAllTextIsBlockedByMetadataScan()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BadCart : Cartridge
            {
                public string Cheat() => System.IO.File.ReadAllText("save.dat");
            }
            """);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics,
            d => d.Contains("QRP0003") && d.Contains("System.IO.File.ReadAllText"));
    }

    [Theory]
    [InlineData("System.Random", "var r = new System.Random(); _ = r.Next();")]
    [InlineData("System.DateTime", "var now = System.DateTime.Now; _ = now;")]
    [InlineData("System.Guid", "var g = System.Guid.NewGuid(); _ = g;")]
    [InlineData("System.Environment", "_ = System.Environment.TickCount;")]
    [InlineData("System.Math", "_ = System.Math.Abs(-1);")]
    [InlineData("System.Threading.Tasks.Task", "_ = System.Threading.Tasks.Task.Delay(1);")]
    public void NondeterministicApisAreBlocked(string bannedType, string statement)
    {
        CartCompileResult result = Compile($$"""
            using Quarp.Api;

            public sealed class BadCart : Cartridge
            {
                public override void Update()
                {
                    {{statement}}
                }
            }
            """);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0003") && d.Contains(bannedType));
    }

    [Fact]
    public void PlainCompileErrorComesBackAsDiagnosticNotException()
    {
        CartCompileResult result = Compile("public sealed class Broken : { this is not C#");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Throws<InvalidOperationException>(() => result.AssemblyBytes);
    }

    [Fact]
    public void CompilationIsDeterministic()
    {
        byte[] first = Compile(ValidCart).AssemblyBytes;
        byte[] second = Compile(ValidCart).AssemblyBytes;
        Assert.Equal(first, second);        // deterministic emit: identical bytes
    }

    [Fact]
    public void IntAndFixMathIsAllowed()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class MathCart : Cartridge
            {
                private Fix _angle;
                public override void Update()
                {
                    _angle += Fix.Ratio(1, 60);
                    Fix s = SMath.Sin(_angle);
                    Fix q = SMath.Sqrt(s * s);
                    _ = SMath.Atan2(s, q);
                }
            }
            """);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }
}
