using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The two M1 determinism filters (SPEC-8 §7, ARCHITECTURE §3): the syntax scan bans
/// float/double/decimal with file and line, the metadata scan bans OS-facing BCL APIs
/// in the emitted DLL. User errors are diagnostics, never exceptions.
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
        string diagnostic = Assert.Single(result.Diagnostics, d => d.Contains("QRP0002"));
        Assert.Contains("src/main.cs(7,", diagnostic);      // the literal sits on line 7
        Assert.Contains("1.5f", diagnostic);
        Assert.Contains("Fix.Ratio", diagnostic);           // the fix-it hint
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
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0002") && d.Contains("0.25"));
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
        string diagnostic = Assert.Single(result.Diagnostics, d => d.Contains("QRP0001"));
        Assert.Contains($"'{keyword}'", diagnostic);
        Assert.Contains("src/main.cs(5,", diagnostic);      // the field sits on line 5
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
