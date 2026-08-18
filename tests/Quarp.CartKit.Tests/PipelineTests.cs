using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The full pipeline end to end, headless: in-memory cart source -> CartCompiler ->
/// CartHost -> VirtualConsole ticks -> framebuffer hash. Two independent runs must be
/// bit-identical — the determinism promise of SPEC-8 §7 in miniature.
/// </summary>
public class PipelineTests
{
    private const string PipelineCart = """
        using Quarp.Api;

        public sealed class PipelineCart : Cartridge
        {
            private int _t;
            private Fix _x;
            private int _sparkX;
            private int _sparkY;

            public override void Init()
            {
                Srand(7);
                _x = 10;
            }

            public override void Update()
            {
                _t++;
                _x += Fix.Ratio(3, 2);
                if (Btn(Button.Right))
                {
                    _x += 1;
                }
                // The two draws stay in Update because the RNG is simulation state: a rewind
                // resimulates without Draw, so drawing them there would consume a different
                // number of values and land in a different game (QRP1004, SPEC-8 §7 rule 2).
                // The sequence is unchanged — the same two draws per tick, in the same order.
                _sparkX = RndInt(128);
                _sparkY = RndInt(72);
            }

            public override void Draw()
            {
                Cls(1);
                RectFill((int)_x, 20, 20, 10, 8);
                Circ(64, 40, _t, 10);
                Print("TICK", 2, 2, 3);
                Pset(_sparkX, _sparkY, 7);
            }
        }
        """;

    private static byte[] CompileOk(string source)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", source) }, "pipeline");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes;
    }

    private static ulong RunTicks(byte[] assembly, int ticks)
    {
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(default);
        }
        return FrameHash.Compute(console.Framebuffer);
    }

    [Fact]
    public void TenTicksHashIdenticallyAcrossTwoRuns()
    {
        byte[] assembly = CompileOk(PipelineCart);
        ulong first = RunTicks(assembly, 10);
        ulong second = RunTicks(assembly, 10);
        Assert.Equal(first, second);
        // And the cart actually drew something: an empty framebuffer hashes differently.
        Assert.NotEqual(FrameHash.Compute(new byte[128 * 72]), first);
    }

    [Fact]
    public void RecompilingGivesTheSameSimulation()
    {
        ulong first = RunTicks(CompileOk(PipelineCart), 10);
        ulong second = RunTicks(CompileOk(PipelineCart), 10);
        Assert.Equal(first, second);
    }

    [Fact]
    public void InputChangesTheOutcome()
    {
        byte[] assembly = CompileOk(PipelineCart);
        ulong idle = RunTicks(assembly, 10);

        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        var right = default(InputState).With(0, Quarp.Api.Button.Right, true);
        for (int i = 0; i < 10; i++)
        {
            console.Tick(right);
        }
        Assert.NotEqual(idle, FrameHash.Compute(console.Framebuffer));
    }

    [Fact]
    public void CartExceptionPropagatesWithSourceLineNumber()
    {
        // The embedded portable PDB is a milestone criterion: a cart crash must carry
        // the cartridge source file and line in its stack trace.
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class CrashingCart : Cartridge
            {
                public override void Update()
                {
                    int zero = Ticks - Ticks;
                    _ = 1 / zero;
                }
            }
            """);
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        var e = Assert.Throws<DivideByZeroException>(() => console.Tick(default));
        Assert.Contains("src/main.cs", e.StackTrace);
        Assert.Contains("line 8", e.StackTrace);    // the division sits on line 8
    }

    [Fact]
    public void InitRunsAsTickZeroDuringAttach()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class InitCart : Cartridge
            {
                public override void Init() => Cls(5);
            }
            """);
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);     // Init draws without any Tick
        Assert.All(console.Framebuffer.Pixels, p => Assert.Equal(5, p));
        Assert.Equal(0, console.Ticks);
    }

    [Fact]
    public void PersistentMemorySurvivesReattachButNotSeedOrTicks()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class SaveCart : Cartridge
            {
                public override void Init()
                {
                    if (Dget(0) == Fix.Zero)
                    {
                        Dset(0, 42);
                        Cls(1);
                    }
                    else
                    {
                        Cls(2);     // second boot sees the saved value
                    }
                }
            }
            """);
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        Assert.Equal(1, console.Framebuffer.Pixels[0]);
        Assert.True(console.PersistentDirty);
        console.AttachCart(host.Cartridge);     // persistent memory survives re-attach
        Assert.Equal(2, console.Framebuffer.Pixels[0]);
    }
}
