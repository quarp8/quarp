using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The regression net for the project's core promise: a cartridge cannot reach floating
/// point or non-deterministic APIs, whatever spelling it uses (SPEC-8 §7).
///
/// Every case here is a bypass that was tried by hand against the M1 sandbox. Unlike
/// <see cref="CartCompilerTests"/>, which checks the filters case by case, these carts are
/// written the way someone *routing around* the ban would write them: aliases, fully
/// qualified names, and `using System;` so the C# keyword never appears in the source.
///
/// Each case asserts a QRP diagnostic, and — importantly — the last test asserts the
/// opposite direction: an ordinary cart using arrays, string interpolation, auto-properties
/// and Fix.ToString must still compile. An over-eager ban that trips on array initializers
/// (which lower to RuntimeHelpers.InitializeArray) breaks every real cartridge, so the ban
/// and the escape hatch have to be pinned together.
/// </summary>
public class SandboxBypassTests
{
    private static CartCompileResult Compile(string mainCs) =>
        CartCompiler.Compile(new[] { new CartSourceFile("src/main.cs", mainCs) }, "bypasscart");

    private static string Rejects(CartCompileResult result, string code)
    {
        Assert.False(result.Success, "bypass compiled — the sandbox is open");
        return Assert.Single(result.Diagnostics, d => d.Contains(code, StringComparison.Ordinal));
    }

    /// <summary>(a) `using System;` then the CLR type name, so no `double` keyword is present.</summary>
    [Fact]
    public void UsingSystemDoubleIdentifierIsRejected()
    {
        CartCompileResult result = Compile("""
            using System;
            using Quarp.Api;

            public sealed class BypassA : Cartridge
            {
                public override void Update()
                {
                    Double x = 5;
                    x = x / 3;
                    Print(x > 1 ? "big" : "small", 0, 0, 7);
                }
            }
            """);

        // The analyzer catches the spelling with a precise line...
        string syntax = Rejects(result, "QRP1001");
        Assert.Contains("src/main.cs(8,", syntax);
        Assert.Contains("'Double'", syntax);

        // ...and the float scan of the emitted IL catches the arithmetic independently.
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("ldc.r8"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("local variable"));
    }

    /// <summary>(b) A using-alias, so the banned name appears once and never at the use site.</summary>
    [Fact]
    public void UsingAliasForDoubleIsRejected()
    {
        CartCompileResult result = Compile("""
            using D = System.Double;
            using Quarp.Api;

            public sealed class BypassB : Cartridge
            {
                public override void Update()
                {
                    D x = 5;
                    x = x / 3;
                    Print(x > 1 ? "big" : "small", 0, 0, 7);
                }
            }
            """);

        Assert.False(result.Success);
        // Flagged at the alias declaration, which is where the mistake actually is: `D` at
        // the use site carries no banned text at all, and the author fixes line 1, not line 8.
        //
        // M2 re-pin: M1's scan additionally reported the use site, because it bound every
        // simple name looking for an alias target. QRP1001 reports the declaration only, and
        // that second message is not worth keeping both scans (and therefore two ids) alive
        // for — the alias is named once and the IL scan below is unaffected either way.
        Assert.Contains(result.Diagnostics,
            d => d.Contains("QRP1001") && d.Contains("src/main.cs(1,") && d.Contains("'Double'"));

        // The assembly scan holds independently: the type reference and the float IL.
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("System.Double"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("ldc.r8"));
    }

    /// <summary>(c) System.Single field and System.Decimal local, both fully qualified.</summary>
    [Fact]
    public void QualifiedSingleFieldAndDecimalLocalAreRejected()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BypassC : Cartridge
            {
                private System.Single _speed = 2;

                public override void Update()
                {
                    System.Decimal money = 10;
                    money = money / 4;
                    _speed = _speed * 2;
                    Print(money > 1 ? "rich" : "poor", 0, 0, 7);
                    Print(_speed > 1 ? "fast" : "slow", 0, 8, 7);
                }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP1001") && d.Contains("'Single'"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP1001") && d.Contains("'Decimal'"));

        // float: caught as a field type and as ldc.r4 in both the ctor and Update.
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("_speed (field type)"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("ldc.r4"));

        // decimal has no float element type of its own — it must be caught by name, both as
        // a type reference and through the operators the arithmetic calls.
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("System.Decimal (type reference)"));
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP0004") && d.Contains("System.Decimal.op_Division"));
    }

    /// <summary>(d) Wall-clock access: the tick path must not see real time.</summary>
    [Fact]
    public void StopwatchGetTimestampIsRejected()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BypassD : Cartridge
            {
                public override void Update()
                {
                    long t = System.Diagnostics.Stopwatch.GetTimestamp();
                    Print(((int)(t & 15)).ToString(), 0, 0, 7);
                }
            }
            """);

        string diagnostic = Rejects(result, "QRP0003");
        Assert.Contains("System.Diagnostics.Stopwatch.GetTimestamp", diagnostic);
    }

    /// <summary>
    /// (e) Unsafe.As — type punning would let a cart reinterpret an int as float bits and do
    /// real arithmetic with no float type anywhere in its own signatures.
    /// </summary>
    [Fact]
    public void UnsafeAsIsRejected()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class BypassE : Cartridge
            {
                public override void Update()
                {
                    int bits = 1078530011;
                    ref byte b = ref System.Runtime.CompilerServices.Unsafe.As<int, byte>(ref bits);
                    Print(b.ToString(), 0, 0, 7);
                }
            }
            """);

        string diagnostic = Rejects(result, "QRP0003");
        Assert.Contains("System.Runtime.CompilerServices.Unsafe.As", diagnostic);
    }

    /// <summary>
    /// The other direction: the ban must not swallow ordinary cartridge code. Array
    /// initializers lower to RuntimeHelpers.InitializeArray, `in` parameters emit an
    /// InAttribute modreq, auto-properties carry [DebuggerBrowsable], and string
    /// interpolation goes through DefaultInterpolatedStringHandler — all of which live in
    /// namespaces the sandbox otherwise bans. Fix.ToString is included because the float
    /// scan must judge a cart by the signatures it references, not by what Quarp.Api does
    /// inside them.
    /// </summary>
    [Fact]
    public void OrdinaryCartStillCompiles()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class GoodCart : Cartridge
            {
                private static readonly int[] DirX = { 0, 1, 0, -1 };
                private static readonly int[] DirY = { -1, 0, 1, 0 };

                private int Score { get; set; }
                private Fix _angle;

                private static int Wrap(in int value, int limit) => (value % limit + limit) % limit;

                public override void Update()
                {
                    Score += DirX[Wrap(Ticks, 4)] + DirY[Wrap(Ticks, 4)];
                    _angle += Fix.Ratio(1, 60);
                }

                public override void Draw()
                {
                    Cls(0);
                    Print($"SCORE {Score}", 2, 2, 7);
                    Print(_angle.ToString(), 2, 10, 3);
                }
            }
            """);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// The banned spellings must be judged by what they bind to, not by how they read.
    /// <c>Fix.Half</c> is a documented cartridge-facing constant (API-8.md §Fix) and a cart
    /// may define its own <c>Half</c> helper; neither is <c>System.Half</c>. This was a live
    /// false positive — the spelling-only scan rejected every cart that touched Fix.Half.
    /// </summary>
    [Fact]
    public void HalfSpelledAsAMemberOrOwnHelperIsAllowed()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class HalfCart : Cartridge
            {
                private Fix _v;

                private static Fix Half(Fix value) => value / 2;

                public override void Update()
                {
                    _v += Fix.Half;
                    _v = Half(_v);
                }

                public override void Draw()
                {
                    Cls(0);
                    Print(_v.ToString(), 2, 2, 7);
                }
            }
            """);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    /// <summary>Real System.Half stays banned — the fix above must not have opened a door.</summary>
    [Fact]
    public void SystemHalfIsStillRejected()
    {
        CartCompileResult result = Compile("""
            using Quarp.Api;

            public sealed class HalfBypass : Cartridge
            {
                private System.Half _h;
                public override void Update() => _h = default;
            }
            """);

        string diagnostic = Rejects(result, "QRP1001");
        Assert.Contains("'Half'", diagnostic);
    }
}
