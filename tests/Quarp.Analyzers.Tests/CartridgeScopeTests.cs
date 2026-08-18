using Xunit;

namespace Quarp.Analyzers.Tests;

/// <summary>
/// The gate itself: the rules apply to a compilation that declares a
/// <c>Quarp.Api.Cartridge</c> subclass, and to nothing else. This is the mechanism the whole
/// design rests on — it is what lets one analyzer DLL be safe both in a cartridge author's
/// IDE project and inside CartCompiler, with no MSBuild property to forget.
/// </summary>
public sealed class CartridgeScopeTests
{
    private const string CartridgeFile = """
        using Quarp.Api;

        public sealed class TestCart : Cartridge
        {
            public override void Update()
            {
            }
        }
        """;

    /// <summary>
    /// A cartridge is a folder of sources, so the file holding the violation usually is not
    /// the file holding the subclass. Scoping per compilation is what makes that work.
    /// </summary>
    [Fact]
    public Task HelperFileInACartridgeCompilationIsAnalyzed() =>
        CartVerifier.VerifyManyAsync<FloatBanAnalyzer>(
            CartridgeFile,
            """
            public static class Geometry
            {
                public static int Scale(int value) => (int)(value * {|QRP1001:1.5|});
            }
            """);

    /// <summary>The same helper on its own is ordinary C# and is left alone.</summary>
    [Fact]
    public Task HelperFileWithoutACartridgeIsNotAnalyzed() =>
        CartVerifier.VerifyManyAsync<FloatBanAnalyzer>(
            """
            public static class Geometry
            {
                public static int Scale(int value) => (int)(value * 1.5);
            }
            """);

    /// <summary>
    /// Referencing Quarp.Api is not enough to be a cartridge: the engine's own projects do
    /// that and must stay free to use double, DateTime and dictionaries.
    /// </summary>
    [Fact]
    public Task ReferencingTheApiWithoutSubclassingCartridgeIsNotACartridge() =>
        CartVerifier.VerifyManyAsync<NonDeterministicApiAnalyzer>(
            """
            using Quarp.Api;

            public static class EngineClock
            {
                public static Fix Now() => Fix.FromRaw(System.Environment.TickCount);
            }
            """);

    /// <summary>An abstract intermediate base still makes the compilation a cartridge.</summary>
    [Fact]
    public Task SubclassThroughAnIntermediateBaseCounts() =>
        CartVerifier.VerifyManyAsync<UnorderedIterationAnalyzer>(
            """
            using System.Collections.Generic;
            using Quarp.Api;

            public abstract class GameBase : Cartridge
            {
            }

            public sealed class TestCart : GameBase
            {
                private readonly HashSet<int> _seen = new HashSet<int>();

                public override void Update()
                {
                    foreach (int cell in {|QRP1003:_seen|})
                    {
                        Pset(cell, 0, 7);
                    }
                }
            }
            """);

    /// <summary>A nested cartridge is unusual but legal, and CartHost loads it — so it counts too.</summary>
    [Fact]
    public Task NestedSubclassCounts() =>
        CartVerifier.VerifyManyAsync<FloatBanAnalyzer>(
            """
            using Quarp.Api;

            public static class Outer
            {
                public sealed class InnerCart : Cartridge
                {
                    private {|QRP1001:double|} _speed;
                }
            }
            """);
}
