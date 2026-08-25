using Xunit;

namespace Quarp.Analyzers.Tests;

/// <summary>
/// QRP1004 — a state-mutating console call reached from <c>Draw</c>.
///
/// The two halves of this file matter equally. The first proves the rule fires on the nine
/// members that write simulation state, directly and through Draw-only helpers; the second
/// proves it stays quiet on the code cartridges are actually made of — drawing, reads,
/// helpers shared with <c>Update</c>, and the engine's own source. A determinism rule that
/// cried wolf on <c>carts/snake</c>'s <c>Draw</c> would be turned off within a day.
/// </summary>
public sealed class DrawPurityTests
{
    private static Task VerifyAsync(string source) => CartVerifier.VerifyAsync<DrawPurityAnalyzer>(source);

    private static Task VerifyManyAsync(params string[] sources) =>
        CartVerifier.VerifyManyAsync<DrawPurityAnalyzer>(sources);

    // --- fires: the nine mutating members, written straight into Draw ---

    /// <summary>The realistic case: a sparkle drawn with Rnd advances the RNG, which is simulation state.</summary>
    [Fact]
    public Task RndInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Fix jitter = {|QRP1004:Rnd|}(Fix.One);
                Pset((int)jitter, 0, 7);
            }
        """));

    [Fact]
    public Task RndIntInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Pset({|QRP1004:RndInt|}(128), 0, 7);
            }
        """));

    [Fact]
    public Task SrandInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                {|QRP1004:Srand|}(Ticks);
            }
        """));

    [Fact]
    public Task SsetInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                {|QRP1004:Sset|}(0, 0, 7);
            }
        """));

    [Fact]
    public Task MsetInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                {|QRP1004:Mset|}(0, 0, 1);
            }
        """));

    [Fact]
    public Task FsetInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                {|QRP1004:Fset|}(0, 0, true);
            }
        """));

    /// <summary>Saving the best score from Draw writes persistent memory — the second external input of the simulation.</summary>
    [Fact]
    public Task DsetInDraw() => VerifyAsync(CartVerifier.Cart("""
            private int _score;

            public override void Draw()
            {
                {|QRP1004:Dset|}(0, _score);
            }
        """));

    /// <summary>Qualifying the call changes nothing; the span covers the whole `this.Rnd`.</summary>
    [Fact]
    public Task QualifiedCallInDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                _ = {|QRP1004:this.RndInt|}(4);
            }
        """));

    // --- fires: through helpers that nothing but Draw can reach ---

    [Fact]
    public Task ThroughAHelperCalledOnlyFromDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                DrawSparkle();
            }

            private void DrawSparkle()
            {
                Pset({|QRP1004:RndInt|}(128), 0, 7);
            }
        """));

    /// <summary>
    /// The shape of a real cartridge: snake's Draw calls DrawField, which calls DrawApple.
    /// A one-level check would miss this, which is why the rule walks the whole call graph.
    /// </summary>
    [Fact]
    public Task ThroughTwoLevelsOfDrawOnlyHelpers() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                DrawField();
            }

            private void DrawField()
            {
                DrawApple();
            }

            private void DrawApple()
            {
                Pset({|QRP1004:RndInt|}(128), 0, 10);
            }
        """));

    /// <summary>A helper in another file of the same cartridge is still the same compilation.</summary>
    [Fact]
    public Task ThroughAHelperInAnotherFile() => VerifyManyAsync(
        """
        using Quarp.Api;

        public sealed class TestCart : Cartridge
        {
            public override void Draw()
            {
                Hud.Paint(this);
            }

            internal void PaintHud()
            {
                {|QRP1004:Mset|}(0, 0, 1);
            }
        }
        """,
        """
        internal static class Hud
        {
            public static void Paint(TestCart cart)
            {
                cart.PaintHud();
            }
        }
        """);

    /// <summary>
    /// A partial helper binds to its defining half at the call site and to its implementing
    /// half inside the body. Both have to be one node, or the edge out of Draw is lost and
    /// the helper silently looks unreachable.
    /// </summary>
    [Fact]
    public Task ThroughAPartialDrawOnlyHelper() => VerifyManyAsync(
        """
        using Quarp.Api;

        public sealed partial class TestCart : Cartridge
        {
            public override void Draw()
            {
                PaintSparkle();
            }

            private partial void PaintSparkle();
        }
        """,
        """
        public sealed partial class TestCart
        {
            private partial void PaintSparkle()
            {
                Pset({|QRP1004:RndInt|}(128), 0, 7);
            }
        }
        """);

    /// <summary>Reading a property from Draw runs its getter, so a getter that draws from the RNG is the same bug.</summary>
    [Fact]
    public Task ThroughAPropertyReadOnlyFromDraw() => VerifyAsync(CartVerifier.Cart("""
            private int Sparkle => {|QRP1004:RndInt|}(128);

            public override void Draw()
            {
                Pset(Sparkle, 0, 7);
            }
        """));

    /// <summary>A local function is part of the member that declares it, and Draw is that member.</summary>
    [Fact]
    public Task InsideALocalFunctionOfDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Paint();

                void Paint()
                {
                    Pset({|QRP1004:RndInt|}(128), 0, 7);
                }
            }
        """));

    /// <summary>Draw may be overridden through the cartridge's own base class; the override chain is walked to Cartridge.Draw.</summary>
    [Fact]
    public Task ThroughAnIntermediateBaseClass() => VerifyAsync("""
        using Quarp.Api;

        public abstract class BaseCart : Cartridge
        {
            public override void Draw()
            {
                Cls(0);
            }
        }

        public sealed class TestCart : BaseCart
        {
            public override void Draw()
            {
                base.Draw();
                {|QRP1004:Srand|}(1);
            }
        }
        """);

    /// <summary>Cartridge code holding the console interface directly hits the same members.</summary>
    [Fact]
    public Task ThroughTheConsoleInterface() => VerifyAsync(CartVerifier.Cart("""
            private IConsoleApi _console = null!;

            public override void Draw()
            {
                {|QRP1004:_console.Srand|}(1);
            }
        """));

    // --- does not fire: the same calls where they belong ---

    /// <summary>Init is tick 0 and part of the simulation: seeding and spawning there is exactly right.</summary>
    [Fact]
    public Task MutatingCallsInInitAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Init()
            {
                Srand(12345);
                Sset(0, 0, 7);
                Mset(0, 0, 1);
                Fset(0, 0, true);
                Dset(0, 1);
                _ = Rnd(Fix.One);
                _ = RndInt(16);
            }
        """));

    [Fact]
    public Task MutatingCallsInUpdateAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                Srand(12345);
                Sset(0, 0, 7);
                Mset(0, 0, 1);
                Fset(0, 0, true);
                Dset(0, 1);
                _ = Rnd(Fix.One);
                _ = RndInt(16);
            }
        """));

    /// <summary>A helper on the Update path stays a helper on the Update path, however deep.</summary>
    [Fact]
    public Task MutatingCallsInAnUpdateOnlyHelperAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Update()
            {
                Step();
            }

            private void Step()
            {
                SpawnApple();
            }

            private void SpawnApple()
            {
                _ = RndInt(128);
            }
        """));

    /// <summary>
    /// The deliberate blind spot: a helper both Update and Draw call has a legitimate reading,
    /// and an error the author has to suppress is an error the author stops reading.
    /// </summary>
    [Fact]
    public Task AHelperSharedWithUpdateIsFine() => VerifyAsync(CartVerifier.Cart("""
            private int _cell;

            public override void Update()
            {
                Respawn();
            }

            public override void Draw()
            {
                Respawn();
                Pset(_cell, 0, 7);
            }

            private void Respawn()
            {
                _cell = RndInt(128);
            }
        """));

    // --- does not fire: what Draw is made of ---

    /// <summary>Every pure drawing call, plus the state that only steers drawing. This is snake's Draw.</summary>
    [Fact]
    public Task PureDrawingCallsInDrawAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Pal(6, 23);
                Palt(0, true);
                Camera(0, 0);
                Clip(0, 0, 128, 72);
                Cls(0);
                Pset(1, 1, 7);
                Line(0, 7, 127, 7, 1);
                Rect(0, 0, 8, 8, 3);
                RectFill(8, 8, 8, 8, 7);
                Circ(64, 36, 4, 8);
                CircFill(64, 36, 2, 10);
                Spr(1, 16, 16);
                Map(0, 0, 0, 0, 16, 9);
                _ = Print("SCORE", 1, 1, 3);
                Clip();
                Pal();
                Palt();
            }
        """));

    /// <summary>
    /// The display stage is drawing state, not simulation state, so <c>Pald</c> and <c>Palr</c>
    /// belong in <c>Draw</c> exactly the way <c>Pal</c> and <c>Camera</c> do: they change how the
    /// finished frame is shown and write nothing a resimulation from tick 0 has to reproduce.
    ///
    /// <para>Break recipe: add <c>Pald</c> or <c>Palr</c> to <c>MutatingConsoleApi</c>'s list. This
    /// test reddens with a QRP1004 — and every per-frame recolour, the effect the stage exists for,
    /// becomes uncallable from the only method that runs once per frame.</para>
    /// </summary>
    [Fact]
    public Task TheDisplayStageInDrawIsFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Cls(1);
                Pald(0, 7, 23);
                Pald(1, 5, 22);
                Palr(0, 40, 1);
                Palr(41, 0);
                Pald(1);
                Pald();
                Palr();
            }
        """));

    /// <summary>Every read the console offers. Reading is what Draw is supposed to do.</summary>
    [Fact]
    public Task ReadsInDrawAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                byte pixel = Pget(0, 0);
                byte tile = Mget(0, 0);
                bool flag = Fget(0, 0);
                byte sheet = Sget(0, 0);
                Fix best = Dget(0);
                bool held = Btn(Button.Left);
                bool pressed = Btnp(Button.O);
                int tick = Ticks;
                Pset(pixel + tile + sheet + (int)best + tick, held || pressed ? 1 : 0, flag ? (byte)7 : (byte)8);
            }
        """));

    /// <summary>
    /// Inverted in M3. This asserted the opposite for two milestones, while Sfx and Music were
    /// no-ops. They now write channel and sequencer state inside the APU, and Draw runs once
    /// per frame on a clock that has nothing to do with ticks — at 30 fps it runs half as
    /// often, during a rewind it does not run at all. A beep started here plays a different
    /// number of times on every run and the PCM hash the CI compares stops matching.
    /// </summary>
    [Fact]
    public Task AudioCallsInDrawAreRejected() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                {|QRP1004:Sfx|}(0);
                {|QRP1004:Music|}(1);
            }
        """));

    /// <summary>The other half: sound asked for on the tick path is exactly where it belongs.</summary>
    [Fact]
    public Task AudioCallsInUpdateAndInitAreFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Init()
            {
                Music(0);
            }

            public override void Update()
            {
                Sfx(3);
                Sfx(4, 2);
                Music();
            }
        """));

    /// <summary>Through a Draw-only helper, like every other mutating call.</summary>
    [Fact]
    public Task AudioCallInADrawOnlyHelperIsRejected() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Chime();
            }

            private void Chime()
            {
                {|QRP1004:Sfx|}(1);
            }
        """));

    // --- does not fire: things that only look like Draw ---

    /// <summary>An overload named Draw is not the engine's Draw, and nothing calls it.</summary>
    [Fact]
    public Task AnOverloadNamedDrawIsNotDraw() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Cls(0);
            }

            private void Draw(int layer)
            {
                Srand(layer);
            }
        """));

    /// <summary>A cartridge's own method that happens to be called Rnd binds to itself, not to the console.</summary>
    [Fact]
    public Task ACartridgesOwnRndIsFine() => VerifyAsync(CartVerifier.Cart("""
            public override void Draw()
            {
                Pset(Rnd(3), 0, 7);
            }

            private int Rnd(int step) => Ticks / step;
        """));

    /// <summary>
    /// The engine's own source: no Cartridge subclass, so the analyzer never switches on.
    /// The console implementation calls Srand from wherever it likes.
    /// </summary>
    [Fact]
    public Task MutatingCallsOutsideACartAreFine() => VerifyAsync(CartVerifier.NotACart("""
            private IConsoleApi _console = null!;

            public void Draw()
            {
                _console.Srand(1);
                _console.Sset(0, 0, 7);
                _console.Dset(0, 1);
                _ = _console.RndInt(4);
            }
        """));
}
