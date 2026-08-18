using Xunit;

namespace Quarp.Analyzers.Tests;

/// <summary>QRP1002 — the non-deterministic BCL surface in cartridge code.</summary>
public sealed class NonDeterministicApiTests
{
    private static Task VerifyAsync(string source) => CartVerifier.VerifyAsync<NonDeterministicApiAnalyzer>(source);

    // --- fires ---

    [Fact]
    public Task SystemRandom() => VerifyAsync(CartVerifier.Cart("""
            private readonly {|QRP1002:System.Random|} _rng = new {|QRP1002:System.Random|}(1);

            public override void Update()
            {
                _ = _rng.Next();
            }
        """));

    /// <summary>
    /// The type is underlined once, whatever hangs off it: 'Now' and 'Year' after the dot are
    /// members, and reporting them too would just be the same mistake three times.
    /// </summary>
    [Fact]
    public Task DateTimeNow() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int year = {|QRP1002:System.DateTime|}.Now.Year;
                _ = year;
            }
        """));

    [Fact]
    public Task DateTimeOffset() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1002:System.DateTimeOffset|} _stamp;"));

    [Fact]
    public Task Guid() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                _ = {|QRP1002:System.Guid|}.NewGuid();
            }
        """));

    [Fact]
    public Task Environment() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int count = {|QRP1002:System.Environment|}.TickCount;
                _ = count;
            }
        """));

    [Fact]
    public Task Task() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1002:System.Threading.Tasks.Task|} _work;"));

    [Fact]
    public Task Thread() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                {|QRP1002:System.Threading.Thread|}.Sleep(1);
            }
        """));

    [Fact]
    public Task Stopwatch() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1002:System.Diagnostics.Stopwatch|} _watch;"));

    [Fact]
    public Task TimeProvider() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1002:System.TimeProvider|} _time;"));

    /// <summary>System.Math is banned outright — SMath is the deterministic replacement.</summary>
    [Fact]
    public Task SystemMath() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                int a = {|QRP1002:System.Math|}.Abs(-3);
                _ = a;
            }
        """));

    [Fact]
    public Task FileIo() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                _ = {|QRP1002:System.IO.File|}.Exists("save.dat");
            }
        """));

    [Fact]
    public Task Reflection() => VerifyAsync(CartVerifier.Cart(
        "    private {|QRP1002:System.Reflection.Assembly|} _self;"));

    /// <summary>
    /// After 'using System;' the type name stands alone — the rule binds names, so the
    /// shorter spelling is caught just the same.
    /// </summary>
    [Fact]
    public Task ImportedNamespaceShortensTheName() => VerifyAsync(
        "using System;\n"
        + "using Quarp.Api;\n"
        + "\n"
        + "public sealed class TestCart : Cartridge\n"
        + "{\n"
        + "    private readonly {|QRP1002:Random|} _rng = new {|QRP1002:Random|}(1);\n"
        + "\n"
        + "    public override void Update() => _ = _rng.Next();\n"
        + "}\n");

    /// <summary>RuntimeHelpers has to stay legal — every array initializer goes through it — so the hole is banned per member.</summary>
    [Fact]
    public Task GetUninitializedObject() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                _ = {|QRP1002:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject|}(typeof(TestCart));
            }
        """));

    // --- does not fire ---

    /// <summary>Console randomness, time and persistence: the whole point of the ban.</summary>
    [Fact]
    public Task ConsoleSurfaceIsFine() => VerifyAsync(CartVerifier.Cart("""
            private int _apple;

            public override void Init()
            {
                Srand(12345);
                _apple = RndInt(128);
            }

            public override void Update()
            {
                if (Ticks % 8 == 0)
                {
                    _apple = RndInt(128);
                }
                Dset(0, Fix.FromRaw(_apple));
            }

            public override void Draw()
            {
                Cls(0);
                Print($"APPLE {_apple}", 2, 1, 3);
                Pset(_apple % 128, 4, 7);
            }
        """));

    /// <summary>
    /// RuntimeHelpers itself stays legal: an array initializer lowers to
    /// RuntimeHelpers.InitializeArray, which every table-driven cartridge hits at once.
    /// </summary>
    [Fact]
    public Task ArrayTablesAndCollectionsAreFine() => VerifyAsync(CartVerifier.Cart("""
            private static readonly int[] DirDx = { 0, 1, 0, -1 };
            private readonly List<int> _body = new List<int>();

            public override void Update()
            {
                _body.Add(DirDx[Ticks & 3]);
            }
        """));

    /// <summary>An attribute from an otherwise banned namespace stays legal, as in CartCompiler.</summary>
    [Fact]
    public Task DiagnosticsAttributeIsFine() => VerifyAsync(CartVerifier.Cart("""
            [System.Diagnostics.Conditional("DEBUG")]
            private static void Trace()
            {
            }

            public override void Update() => Trace();
        """));

    /// <summary>The engine uses DateTime, files and threads by design; only cartridge code is policed.</summary>
    [Fact]
    public Task BannedApiOutsideACartIsFine() => VerifyAsync(CartVerifier.NotACart("""
            private readonly System.Random _rng = new System.Random(1);

            public int Roll() => _rng.Next() + System.DateTime.Now.Year + System.Environment.TickCount;
        """));
}
