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
/// readings. What it cannot show is the half that decides whether any of this is real: that the
/// number survives <em>Roslyn</em>. A property could read the console perfectly and still be
/// constant-folded out of the emitted IL if it were ever turned into a <c>const</c>, and the core
/// test — which never goes near a compiler — would stay green while every cartridge in the
/// repository silently froze to one screen. So this one compiles a real cartridge once, loads that
/// one assembly once, and runs the same instance on two profiles.</para>
///
/// <para><b>The second profile is built here, on purpose.</b> ADR-021 settled QUARP-8 at 160x90
/// and deleted the spike profile that used to supply the contrast; the claim it existed to prove
/// did not go with it, so the contrast is now <see cref="Historic"/> — a plain
/// <c>new ConsoleProfile</c> holding the 128x72 screen this project shipped from M0 to M4 stage 3.
/// A cartridge that carried its screen size in its IL would draw an identical picture on both and
/// the difference between the two runs below would vanish.</para>
/// </summary>
public class ScreenSizeThroughTheRealPipelineTests
{
    /// <summary>
    /// The console QUARP-8 used to be, kept alive as a test fixture rather than as a shipped
    /// profile (ADR-021: one spec, one resolution). Two sizes are all this file needs, and using
    /// the real historic one means the hardcoded cartridge below hardcodes numbers that were once
    /// correct — which is exactly the mistake being guarded against.
    /// </summary>
    private static readonly ConsoleProfile Historic = new()
    {
        Name = "QUARP-8 (historic 128x72)",
        Width = 128,
        Height = 72,
    };

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

    /// <summary>Reads a pixel by coordinates, because an index into a flat buffer of the wrong width is a bug this file is looking for.</summary>
    private static byte PixelAt(Framebuffer fb, int x, int y) => fb.Pixels[y * fb.Width + x];

    [Fact]
    public void OneCompiledAssemblyDrawsToTheCornerOfWhicheverConsoleItIsGiven()
    {
        byte[] assembly = CompileReporter();

        // One assembly, one load, one cartridge instance — then two consoles.
        using var host = CartHost.Load(assembly);

        Framebuffer ratified = DrawOn(host, ConsoleProfile.Profile8);
        Assert.Equal(160, ratified.Width);
        Assert.Equal(90, ratified.Height);
        Assert.Equal(7, PixelAt(ratified, 159, 89));
        Assert.Equal(7, PixelAt(ratified, 1, 1));

        Framebuffer historic = DrawOn(host, Historic);
        Assert.Equal(128, historic.Width);
        Assert.Equal(72, historic.Height);

        // The whole point in one assertion: the same IL reached a pixel that does not exist on the
        // other console. A cart with its size baked in would leave one of these two corners empty.
        Assert.Equal(7, PixelAt(historic, 127, 71));
        Assert.Equal(7, PixelAt(historic, 1, 1));

        // And it did not merely paint the corner it was born with: on QUARP-8 the historic corner
        // is an ordinary interior pixel, and it stayed background.
        Assert.Equal(0, PixelAt(ratified, 127, 71));
    }

    /// <summary>
    /// The negative control, and the reason it is phrased as "one answer on two consoles": a
    /// cartridge that hardcodes 128x72 — exactly what every cart in the repository did before Р12
    /// — compiles and runs happily on both, drawing the very same picture, and on the console it
    /// is actually running on that picture has its corner in the middle of the playfield. Without
    /// this, the test above could pass on a console that simply painted its corners for reasons of
    /// its own.
    /// </summary>
    [Fact]
    public void AHardcodedCartridgeDrawsTheSamePictureOnBothConsoles()
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

        Framebuffer ratified = DrawOn(host, ConsoleProfile.Profile8);

        // It still draws — so the cart is alive and the console is working...
        Assert.Equal(7, PixelAt(ratified, 1, 1));
        // ...but the corner of the screen it is actually on is untouched, and the pixel it did
        // reach is stranded 32 px left of and 18 px above the real edge.
        Assert.Equal(0, PixelAt(ratified, 159, 89));
        Assert.Equal(7, PixelAt(ratified, 127, 71));

        // The same instance on the smaller console puts that pixel in exactly the same place —
        // where it now happens to be the corner. Same IL, same picture, no question asked: that
        // sameness is the failure, and it is what the reporter cart above does not do.
        Framebuffer historic = DrawOn(host, Historic);
        Assert.Equal(7, PixelAt(historic, 1, 1));
        Assert.Equal(7, PixelAt(historic, 127, 71));
    }
}
