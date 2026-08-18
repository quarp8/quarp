using Xunit;

namespace Quarp.Analyzers.Tests;

/// <summary>QRP1001 — float, double, decimal and real literals in cartridge code.</summary>
public sealed class FloatBanTests
{
    private static Task VerifyAsync(string source) => CartVerifier.VerifyAsync<FloatBanAnalyzer>(source);

    // --- fires ---

    [Fact]
    public Task DoubleKeywordOnAField() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1001:double|} _speed;"));

    [Fact]
    public Task FloatKeywordOnAParameter() => VerifyAsync(CartVerifier.Cart(
        "    private static int Round({|QRP1001:float|} value) => (int)value;"));

    [Fact]
    public Task DecimalKeywordOnALocal() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                {|QRP1001:decimal|} money = 0;
                _ = money;
            }
        """));

    [Fact]
    public Task RealLiteral() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int steps = (int){|QRP1001:0.5|};
                _ = steps;
            }
        """));

    /// <summary>An exponent literal is a double even though it has no decimal point.</summary>
    [Fact]
    public Task ExponentLiteral() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int big = (int){|QRP1001:1e3|};
                _ = big;
            }
        """));

    [Fact]
    public Task FloatSuffixLiteral() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int one = (int){|QRP1001:1f|};
                _ = one;
            }
        """));

    /// <summary>The keyword is only one spelling; the type name has to be caught as well.</summary>
    [Fact]
    public Task SystemDoubleSpelledOut() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1001:System.Double|} _speed;"));

    /// <summary>System.Half is not a keyword and has no SpecialType — it is matched by name.</summary>
    [Fact]
    public Task SystemHalf() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1001:System.Half|} _small;"));

    /// <summary>A using alias hides the banned name at the use site, so the directive is where it must be caught.</summary>
    [Fact]
    public Task UsingAliasToDouble() => VerifyAsync(
        "using Quarp.Api;\n"
        + "using D = {|QRP1001:System.Double|};\n"
        + "\n"
        + "public sealed class TestCart : Cartridge\n"
        + "{\n"
        + "    private D _speed;\n"
        + "}\n");

    /// <summary>
    /// Reaching a banned type through one of its members still names the type, and the type
    /// is what gets underlined — once, however long the chain after it is.
    /// </summary>
    [Fact]
    public Task StaticMemberOfABannedRealType() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Print({|QRP1001:System.Double|}.MaxValue.ToString(), 2, 2, 7);
            }
        """));

    // --- does not fire ---

    /// <summary>The shape of real cartridge code: int and Fix arithmetic, Fix.Half, Fix.Ratio.</summary>
    [Fact]
    public Task FixArithmeticIsFine() => VerifyAsync(CartVerifier.Cart("""
            private Fix _x;
            private int _tick;

            public override void Update()
            {
                _x += Fix.Half;
                _x = _x * Fix.Ratio(3, 4) - Fix.One;
                _tick = (int)_x + 1;
            }
        """));

    /// <summary>
    /// Fix.Half and a cartridge's own Half helper both spell the banned name, and both are
    /// legal — the rule binds names instead of matching text (mirrors the M1 CartKit test).
    /// </summary>
    [Fact]
    public Task HalfAsAMemberOrOwnHelperIsFine() => VerifyAsync(CartVerifier.Cart("""
            private Fix _v;

            private static Fix Half(Fix value) => value / 2;

            public override void Update()
            {
                _v += Fix.Half;
                _v = Half(_v);
            }
        """));

    [Fact]
    public Task IntegerLiteralsAndArraysAreFine() => VerifyAsync(CartVerifier.Cart("""
            private static readonly int[] DirDx = { 0, 1, 0, -1 };
            private readonly int[] _body = new int[128];

            public override void Update()
            {
                for (int i = 0; i < DirDx.Length; i++)
                {
                    _body[i] = DirDx[i] * 8 + 100000;
                }
            }
        """));

    [Fact]
    public Task StringInterpolationIsFine() => VerifyAsync(CartVerifier.Cart("""
            private int _score;

            public override void Draw()
            {
                Print($"SCORE {_score}", 2, 1, 3);
            }
        """));

    /// <summary>The engine may use double freely; only cartridge code is policed.</summary>
    [Fact]
    public Task DoubleOutsideACartIsFine() => VerifyAsync(CartVerifier.NotACart(
        "    private double _speed;\n"
        + "    private decimal _money;\n"
        + "    public double Scale() => _speed * 1.5;"));
}
