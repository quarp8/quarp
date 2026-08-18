using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Quarp.Api;

namespace Quarp.Analyzers.Tests;

/// <summary>
/// Runs one analyzer over a snippet through the Roslyn test framework, with the same
/// references a real cartridge gets.
///
/// The reference set is built from <c>TRUSTED_PLATFORM_ASSEMBLIES</c> exactly as
/// <c>CartCompiler.BuildReferences</c> builds it, instead of using one of the framework's
/// <see cref="ReferenceAssemblies"/> presets. Two reasons: a test can then never pass on a
/// type a cartridge could not reference in the first place, and the test run needs no
/// network — the presets download reference-assembly packages from nuget.org on first use.
///
/// Test sources use the framework's markup syntax, so every expectation carries an exact
/// span: <c>{|QRP1001:double|}</c> asserts that the rule fires on exactly those characters.
/// </summary>
internal static class CartVerifier
{
    /// <summary>The BCL closure a cartridge compiles against (M1 work order, "Конвейер картриджа").</summary>
    private static readonly string[] WantedAssemblies =
    {
        "System.Runtime.dll",
        "System.Private.CoreLib.dll",
        "System.Collections.dll",
        "netstandard.dll",
    };

    private static readonly ImmutableArray<MetadataReference> CartReferences = BuildReferences();

    /// <summary>
    /// Verifies <paramref name="source"/> against <typeparamref name="TAnalyzer"/>; the
    /// expected diagnostics are the ones spelled out in the source markup.
    /// </summary>
    public static Task VerifyAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        VerifyManyAsync<TAnalyzer>(source);

    /// <summary>
    /// The same, for a cartridge spread over several files — which is the normal case, since
    /// a cartridge folder is <c>src/*.cs</c>. Worth testing on its own: the analyzers are
    /// scoped per compilation, not per file, so a helper file with no <c>Cartridge</c>
    /// subclass in it is still cartridge code.
    /// </summary>
    public static Task VerifyManyAsync<TAnalyzer>(params string[] sources)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            // An empty preset: every reference comes from AdditionalReferences below.
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
        };
        foreach (string source in sources)
        {
            test.TestState.Sources.Add(source);
        }
        test.TestState.AdditionalReferences.AddRange(CartReferences);
        return test.RunAsync();
    }

    /// <summary>
    /// Wraps cartridge members in a <c>Cartridge</c> subclass — which is also what switches
    /// the analyzers on, since that subclass is how they recognise cartridge code.
    /// </summary>
    public static string Cart(string members) =>
        "using System.Collections.Generic;\n"
        + "using Quarp.Api;\n"
        + "\n"
        + "public sealed class TestCart : Cartridge\n"
        + "{\n"
        + members + "\n"
        + "}\n";

    /// <summary>
    /// The same members in a compilation with no <c>Cartridge</c> subclass — the engine's own
    /// source, or a plain library the author happens to build with the analyzer switched on.
    /// Nothing may fire here.
    /// </summary>
    public static string NotACart(string members) =>
        "using System.Collections.Generic;\n"
        + "using Quarp.Api;\n"
        + "\n"
        + "public sealed class EngineHelper\n"
        + "{\n"
        + members + "\n"
        + "}\n";

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var wanted = new HashSet<string>(WantedAssemblies, StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trusted)
        {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unavailable; cannot build the cartridge reference set.");
        }
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (wanted.Contains(Path.GetFileName(path)))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }
        builder.Add(MetadataReference.CreateFromFile(typeof(Cartridge).Assembly.Location));
        return builder.ToImmutable();
    }
}
