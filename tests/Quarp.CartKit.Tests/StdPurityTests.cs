using System.Reflection;
using Quarp.Api;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// M4 work order Р30, checks run through real Roslyn/reflection machinery rather than by
/// reading the source and trusting it:
/// <list type="bullet">
///   <item>does <c>QRP1004</c> actually catch <c>PaintPattern</c> called from <c>Draw</c>,
///     through the same <see cref="CartCompiler"/> a real cartridge compiles through, and
///     does it stay quiet when the same call is made from <c>Init</c> or <c>Update</c>,
///     where SPEC-8 §7 rule 2 says it belongs;</item>
///   <item>does <c>Quarp.Api</c>'s public surface stay free of float/double/decimal, and does
///     the assembly stay free of mutable static state — the two determinism invariants a
///     library that grows without an ADR (ADR-019) needs enforced by a test, not by review.</item>
/// </list>
/// </summary>
public class StdPurityTests
{
    // --- (б): does QRP1004 catch a call to Std.PaintPattern written straight in Draw? ---

    private const string PaintPatternFromDraw = """
        using Quarp.Api;

        public sealed class BadStand : Cartridge
        {
            private static readonly string[] Pattern = { "0" };

            public override void Draw()
            {
                Q.PaintPattern(0, 0, Pattern);
            }
        }
        """;

    /// <summary>
    /// The direct answer to the M4 stage 4.1 spec's question — "краснеет ли QRP1004 на
    /// PaintPattern из Draw?" — is <b>yes, as of wave 1.5</b>, and this is the pipeline-level
    /// proof: it compiles a cartridge calling <c>Q.PaintPattern(...)</c> straight from
    /// <c>Draw</c> through the exact <c>CartCompiler.Compile</c> a real cartridge goes through,
    /// and the compile fails with <c>QRP1004</c> naming <c>PaintPattern</c> and <c>Draw</c>.
    /// <para><b>Why it did not fire before wave 1.5.</b> <c>DrawPurityAnalyzer</c> resolves its
    /// mutating-member set (<c>Quarp.Analyzers.MutatingConsoleApi</c>) by walking
    /// <c>IConsoleApi</c> and <c>Cartridge</c> for members whose name is in a fixed list. Wave 1
    /// added <c>"PaintPattern"</c> to that list, which widened the pre-filter but not the
    /// member set itself, because <c>PaintPattern</c> is declared on <c>Std</c> — a type
    /// <c>Collect()</c> never looked at — and even walking <c>Std</c> would not have been
    /// enough on its own: a call written <c>Q.PaintPattern(...)</c> binds to a <em>reduced</em>
    /// extension-method symbol whose <c>OriginalDefinition</c> does not equal the static method
    /// <c>Collect()</c> puts in the set, so <c>Contains()</c> needed a
    /// <c>ReducedFrom</c> unwrap too. Wave 1.5 closes both — see
    /// <c>Quarp.Analyzers.MutatingConsoleApi.Resolve</c> and <c>.Contains</c> for the fix, and
    /// <c>PaintPatternFromInitOrUpdateStaysClean</c> below for the negative control this rule
    /// needs to stay usable.</para>
    /// </summary>
    [Fact]
    public void PaintPatternFromDrawIsCaughtByQRP1004()
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", PaintPatternFromDraw) }, "badstand");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Contains("QRP1004") && d.Contains("PaintPattern") && d.Contains("Draw"));
    }

    private const string PaintPatternFromInitAndUpdate = """
        using Quarp.Api;

        public sealed class GoodStand : Cartridge
        {
            private static readonly string[] Pattern = { "0" };

            public override void Init()
            {
                Q.PaintPattern(0, 0, Pattern);
            }

            public override void Update()
            {
                Q.PaintPattern(1, 0, Pattern);
            }
        }
        """;

    /// <summary>
    /// The negative control the fix above needs: the same call from where SPEC-8 §7 rule 2
    /// actually puts sprite-sheet writes — <c>Init</c> (tick 0) and <c>Update</c> (every later
    /// tick) — must stay clean. Walking <c>Std</c> in <c>Collect()</c> adds
    /// <c>PaintPattern</c> to the mutating set regardless of call site; what keeps <c>Init</c>
    /// and <c>Update</c> quiet is <c>DrawPurityAnalyzer</c>'s existing reachable-only-from-Draw
    /// test (<see cref="Quarp.Analyzers.DrawCallGraph"/>), unchanged by this wave — so this test
    /// is really checking that wave 1.5 did not widen the ban past the one call site SPEC-8
    /// names, which a rule this blunt could easily have done by accident.
    /// </summary>
    [Fact]
    public void PaintPatternFromInitOrUpdateStaysClean()
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", PaintPatternFromInitAndUpdate) }, "goodstand");

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("QRP1004"));
    }

    /// <summary>
    /// The same mutation one level down — a direct <c>Sset</c> call from <c>Draw</c>, which is
    /// exactly what <c>PaintPattern</c>'s body does internally — is still caught, because
    /// <c>Sset</c> <em>is</em> a member of <c>IConsoleApi</c>. This sits next to the test above
    /// on purpose: the gap is specific to library extension methods, not a regression in
    /// <c>Draw</c>-purity checking generally, and not something this stage broke.
    /// </summary>
    [Fact]
    public void TheSameMutationThroughSsetDirectlyIsStillCaught()
    {
        const string ssetFromDraw = """
            using Quarp.Api;

            public sealed class BadStandDirect : Cartridge
            {
                public override void Draw()
                {
                    Sset(0, 0, 1);
                }
            }
            """;

        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", ssetFromDraw) }, "badstanddirect");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("QRP1004"));
    }

    // --- (в): contract purity of Quarp.Api ------------------------------------------------------

    /// <summary>
    /// SPEC-8 §7: float/double/decimal are what makes a frame differ between x64 and arm64.
    /// <c>QRP1001</c> already bans them from cartridge <em>source</em>; this checks the other
    /// side — that <c>Quarp.Api</c> itself, the library every cartridge and console
    /// implementation compiles against, never hands one back through a public method, property,
    /// field or constructor. Reflection over the compiled assembly rather than a source grep:
    /// a signature is what a caller actually sees, and grepping source would miss a type alias
    /// or a generic instantiation grep could not follow.
    /// </summary>
    [Fact]
    public void PublicSurfaceHasNoFloatingPointTypes()
    {
        var offenders = new List<string>();
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in typeof(Cartridge).Assembly.GetExportedTypes())
        {
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.IsSpecialName)
                {
                    // Property accessors: the PropertyInfo pass below covers the same signature.
                    continue;
                }
                if (IsFloatingPoint(method.ReturnType))
                {
                    offenders.Add($"{type.FullName}.{method.Name}() return type");
                }
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (IsFloatingPoint(parameter.ParameterType))
                    {
                        offenders.Add($"{type.FullName}.{method.Name}({parameter.Name})");
                    }
                }
            }
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (IsFloatingPoint(property.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (IsFloatingPoint(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
            foreach (ConstructorInfo ctor in type.GetConstructors(flags))
            {
                foreach (ParameterInfo parameter in ctor.GetParameters())
                {
                    if (IsFloatingPoint(parameter.ParameterType))
                    {
                        offenders.Add($"{type.FullName}..ctor({parameter.Name})");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "floating point on the public surface: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// SPEC-8 §7's determinism contract assumes every run starts from the same state; a mutable
    /// static field is state that survives outside any <see cref="Quarp.Core.VirtualConsole"/>
    /// instance and outside any replay — exactly the kind of hidden input a rewind or a second
    /// console instance cannot see or reset. This scans every type Quarp.Api declares (not just
    /// the public surface — an internal mutable static would be just as real a determinism
    /// leak) for a static field that is neither <c>const</c> (<see cref="FieldInfo.IsLiteral"/>)
    /// nor <c>readonly</c> (<see cref="FieldInfo.IsInitOnly"/>).
    /// </summary>
    [Fact]
    public void NoQuarpApiTypeHasAMutableStaticField()
    {
        var offenders = new List<string>();
        const BindingFlags flags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (Type type in typeof(Cartridge).Assembly.GetTypes())
        {
            if (type.Namespace != "Quarp.Api")
            {
                // Compiler-generated infrastructure (e.g. <PrivateImplementationDetails>) carries
                // no namespace and is not code this library's author wrote.
                continue;
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.IsLiteral || field.IsInitOnly)
                {
                    continue;
                }
                offenders.Add($"{type.FullName}.{field.Name}");
            }
        }

        Assert.True(offenders.Count == 0, "mutable static field(s): " + string.Join(", ", offenders));
    }

    private static bool IsFloatingPoint(Type type)
    {
        if (type.IsByRef || type.IsPointer)
        {
            type = type.GetElementType()!;
        }
        if (type.IsArray)
        {
            return IsFloatingPoint(type.GetElementType()!);
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return true;
        }
        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                if (IsFloatingPoint(argument))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
