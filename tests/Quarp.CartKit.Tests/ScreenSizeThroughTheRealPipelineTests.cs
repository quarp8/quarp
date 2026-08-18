using Quarp.Core;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The load-bearing proof behind M4's Р12: <c>ScreenWidth</c>/<c>ScreenHeight</c> are read from
/// the console at call time, not compiled into the cartridge.
///
/// <para><b>Why this file exists next to the core-level test.</b>
/// <c>Quarp.Core.Tests.ScreenSizeTests</c> already re-attaches one <c>Cartridge</c> instance to two
/// consoles and gets two answers, which is the tighter experiment — nothing is rebuilt between the
/// readings. What it cannot show is the half the milestone actually depends on: that the number
/// survives <em>Roslyn</em>. A property could read the console perfectly and still be constant-folded
/// out of the emitted IL if it were ever turned into a <c>const</c>, and the core test — which never
/// goes near a compiler — would stay green while the resolution spike quietly measured nothing. So
/// this one compiles a real cartridge once, loads that one assembly once, and runs the same
/// instance on both profiles.</para>
///
/// <para>The stake is the M4 verdict itself. Stage 3 decides 128×72 against 160×90 by looking at the
/// same game on both screens; a cartridge carrying 128 in its IL would draw an identical picture on
/// both and the comparison would be theatre.</para>
/// </summary>
public class ScreenSizeThroughTheRealPipelineTests
{
    /// <summary>
    /// Reports the screen it is running on by plotting one pixel at the far corner and one at a
    /// fixed spot, so the assertions can read the answer out of the framebuffer rather than
    /// trusting a property the test itself calls.
    /// </summary>
    private const string ReporterCart = """
        using Quarp.Api;

        public sealed class Reporter : Cartridge
        {
            public override void Draw()
            {
                Cls(0);
                // The far corner: only reachable if the cart was told the true size.
                Pset(ScreenWidth - 1, ScreenHeight - 1, 7);
                // A witness inside both screens, so "nothing was drawn" cannot pass as a pass.
                Pset(1, 1, 7);
            }
        }
        """;

    private static byte[] CompileReporter()
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", ReporterCart) }, "screencart");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes!;
    }

    private static Framebuffer DrawOn(CartHost host, ConsoleProfile profile)
    {
        var console = new VirtualConsole(profile, null, null, null, null, null);
        console.AttachCart(host.Cartridge);
        console.Tick(default);
        return console.Framebuffer;
    }

    [Fact]
    public void OneCompiledAssemblyDrawsToTheCornerOfWhicheverConsoleItIsGiven()
    {
        byte[] assembly = CompileReporter();

        // One assembly, one load, one cartridge instance — then two consoles.
        using var host = CartHost.Load(assembly);

        Framebuffer narrow = DrawOn(host, ConsoleProfile.Profile8);
        Assert.Equal(128, narrow.Width);
        Assert.Equal(72, narrow.Height);
        Assert.Equal(7, narrow.Pixels[(72 - 1) * 128 + (128 - 1)]);
        Assert.Equal(7, narrow.Pixels[1 * 128 + 1]);

        Framebuffer wide = DrawOn(host, ConsoleProfile.Profile8Wide);
        Assert.Equal(160, wide.Width);
        Assert.Equal(90, wide.Height);

        // The whole milestone in one assertion: the same IL reached a pixel that does not exist
        // on the narrow console. A cart with 128 baked in would leave this corner background.
        Assert.Equal(7, wide.Pixels[(90 - 1) * 160 + (160 - 1)]);
        Assert.Equal(7, wide.Pixels[1 * 160 + 1]);
    }

    /// <summary>
    /// The negative control. A cartridge that hardcodes 128×72 — exactly what every cart in the
    /// repository did before Р12 — compiles and runs happily on the wide console and leaves its
    /// real corner empty. Without this, the test above could pass on a console that simply painted
    /// its corners for reasons of its own.
    /// </summary>
    [Fact]
    public void AHardcodedCartridgeLeavesTheWideConsolesCornerEmpty()
    {
        const string hardcoded = """
            using Quarp.Api;

            public sealed class Hardcoded : Cartridge
            {
                public override void Draw()
                {
                    Cls(0);
                    Pset(127, 71, 7);
                    Pset(1, 1, 7);
                }
            }
            """;

        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", hardcoded) }, "hardcoded");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        using var host = CartHost.Load(result.AssemblyBytes!);

        Framebuffer wide = DrawOn(host, ConsoleProfile.Profile8Wide);

        // It still draws — so the cart is alive and the console is working...
        Assert.Equal(7, wide.Pixels[1 * 160 + 1]);
        // ...but the corner of the screen it is actually on is untouched, and the pixel it did
        // reach is stranded in the middle of the playfield.
        Assert.Equal(0, wide.Pixels[(90 - 1) * 160 + (160 - 1)]);
        Assert.Equal(7, wide.Pixels[71 * 160 + 127]);
    }
}
