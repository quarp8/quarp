using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The milestone cart: carts/snake loads through CartKit, compiles clean under both
/// determinism filters, and 600 headless ticks are bit-identical run to run — the same
/// check `quarp sim carts/snake --ticks 600` performs (M1 acceptance).
/// </summary>
public class SnakeCartTests
{
    private static string FindSnakeFolder()
    {
        // Walk up from the test bin folder to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts", "snake");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/snake not found above the test directory");
    }

    private static byte[] CompileSnake()
    {
        CartData data = CartSource.Load(FindSnakeFolder());
        CartCompileResult result = CartCompiler.Compile(data);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes;
    }

    private static ulong Simulate(byte[] assembly, int ticks, Func<int, InputState>? inputAt = null)
    {
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(inputAt?.Invoke(i) ?? default);
        }
        return Fnv.Hash(console.Framebuffer.Pixels);
    }

    [Fact]
    public void SnakeLoadsAndCompilesClean()
    {
        CartData data = CartSource.Load(FindSnakeFolder());
        Assert.Equal("Snake", data.Manifest.Name);
        Assert.Equal(8, data.Manifest.Profile);
        Assert.NotEmpty(data.Sources);
        CartCompileResult result = CartCompiler.Compile(data);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void SnakeIsWithinTheCodeBudget()
    {
        CartData data = CartSource.Load(FindSnakeFolder());
        int bytes = CodeBudget.Measure(data.Sources);
        Assert.InRange(bytes, 1, CodeBudget.MaxBytes);
    }

    [Fact]
    public void SixHundredIdleTicksAreBitIdenticalAcrossRuns()
    {
        byte[] assembly = CompileSnake();
        ulong first = Simulate(assembly, 600);
        ulong second = Simulate(assembly, 600);
        Assert.Equal(first, second);
        Assert.NotEqual(Fnv.Hash(new byte[128 * 72]), first);   // it drew a real frame
        // Golden cross-checked against `quarp sim carts/snake --ticks 600` (M1 acceptance).
        // A conscious snake or rasterizer change updates this constant along with it.
        Assert.Equal("37c481f3e17fab02", first.ToString("x16"));
    }

    [Fact]
    public void TickPathAllocatesNothing()
    {
        // CODESTYLE core rule 1: zero allocations per tick. Warm the JIT and all code
        // paths first, then measure this thread's managed allocations across 300 ticks.
        byte[] assembly = CompileSnake();
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(ConsoleProfile.Profile8);
        console.AttachCart(host.Cartridge);
        for (int i = 0; i < 300; i++)
        {
            console.Tick(default);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            console.Tick(default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"tick path allocated {allocated} bytes over 300 ticks");
    }

    [Fact]
    public void PlayedTicksAreBitIdenticalAcrossRuns()
    {
        // A scripted little game: turn down, right, up over 600 ticks — still deterministic.
        byte[] assembly = CompileSnake();
        static InputState Script(int tick) => tick switch
        {
            50 => default(InputState).With(0, Quarp.Api.Button.Down, true),
            120 => default(InputState).With(0, Quarp.Api.Button.Right, true),
            200 => default(InputState).With(0, Quarp.Api.Button.Up, true),
            300 => default(InputState).With(0, Quarp.Api.Button.Start, true),
            _ => default,
        };
        ulong first = Simulate(assembly, 600, Script);
        ulong second = Simulate(assembly, 600, Script);
        Assert.Equal(first, second);
    }

    [Fact]
    public void InputActuallySteersTheSnake()
    {
        byte[] assembly = CompileSnake();
        ulong idle = Simulate(assembly, 600);
        ulong steered = Simulate(assembly, 600,
            tick => tick == 50 ? default(InputState).With(0, Quarp.Api.Button.Down, true) : default);
        Assert.NotEqual(idle, steered);
    }
}
