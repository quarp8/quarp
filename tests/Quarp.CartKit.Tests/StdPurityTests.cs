using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
///   <item>does <c>Quarp.Api</c>'s public surface stay free of float/double/decimal, does the
///     compiled assembly stay free of float/double anywhere at all — including a private
///     helper's local variable, which no signature-only scan can see (M4 stage 4.1 adversary
///     review, card Б2) — and does the assembly stay free of mutable static state, including
///     a <c>readonly</c> field whose declared type is itself a mutable container such as a
///     plain array (card З4): the determinism invariants a library that grows without an ADR
///     (ADR-019) needs enforced by a test, not by review.</item>
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

    /// <summary>
    /// <c>NoQuarpApiTypeHasAMutableStaticField</c> above accepts any <c>readonly</c> field
    /// regardless of its type, including a plain array — but a <c>readonly</c> field reference
    /// only stops the field itself from being reassigned; <c>Digits[0] = "oops"</c> still
    /// compiles against a <c>private static readonly string[] Digits</c>, which is exactly the
    /// hidden cross-run, cross-instance mutation SPEC-8 §7 cannot allow. <b>Adversary review, M4
    /// stage 4.1 fix wave, card З4.</b> Scoped to <see cref="Std"/> itself rather than the whole
    /// assembly: <c>Quarp.Api.SMath</c>'s precomputed sine/atan lookup tables are the same shape
    /// (<c>private static readonly int[]</c>) and are out of this stage's file zone and out of
    /// scope for this rule (SMath is not touched by M4 stage 4.1 — "SMath не трогаем", Р27) — a
    /// whole-assembly version of this check would redden on code nobody asked this stage to
    /// revisit. The permitted shapes are exactly the ones named in the work order: <c>string</c>,
    /// primitives/enums, and other genuinely immutable types such as
    /// <see cref="ImmutableArray{T}"/> (a value type with no indexer setter, unlike a CLR array —
    /// <see cref="Type.IsArray"/> is <c>false</c> for it) or <see cref="Fix"/>.
    /// </summary>
    [Fact]
    public void StdHasNoMutableStaticContainerField()
    {
        var offenders = new List<string>();
        const BindingFlags flags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (FieldInfo field in typeof(Std).GetFields(flags))
        {
            if (field.IsLiteral)
            {
                continue;   // const: baked into callers, no storage to mutate.
            }
            if (!field.IsInitOnly)
            {
                offenders.Add($"{field.Name} (not readonly)");
                continue;
            }
            if (!IsAllowedImmutableStaticFieldType(field.FieldType))
            {
                offenders.Add($"{field.Name} (readonly, but {field.FieldType} is a mutable container)");
            }
        }

        Assert.True(offenders.Count == 0, "Std static field(s) with hidden mutable state: " + string.Join(", ", offenders));
    }

    private static bool IsAllowedImmutableStaticFieldType(Type type)
    {
        if (type.IsArray)
        {
            return false;   // a plain CLR array: readonly reference, mutable contents.
        }
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(Fix))
        {
            return true;
        }
        // ImmutableArray<T> and friends: a value type, no indexer setter, genuinely immutable.
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
    }

    // --- (г): IL-level float scan of the compiled Quarp.Api assembly (card Б2) -----------------

    /// <summary>
    /// <see cref="PublicSurfaceHasNoFloatingPointTypes"/> only sees signatures a caller can
    /// reference — a <em>private</em> helper's <c>double</c> local, or a float computed and
    /// converted away without ever naming a type in a signature, is invisible to it. This is the
    /// same second net <c>Quarp.CartKit.CartCompiler.ScanFloats</c> (<c>CartCompiler.cs</c>
    /// around line 760) runs against every compiled cartridge, aimed here at
    /// <c>Quarp.Api.dll</c> itself: walk every method and constructor body — public and private
    /// alike, via <see cref="BindingFlags.NonPublic"/> — for a <c>float</c>/<c>double</c> local
    /// variable type (<see cref="IsFloatingPoint"/>, the same helper the signature scan above
    /// uses) or a float-only opcode (<c>ldc.r4</c>/<c>ldc.r8</c>/<c>conv.r4</c>/<c>conv.r8</c>).
    /// <para><b>Why this does not just call <c>CartCompiler.ScanFloats</c>.</b> That scanner and
    /// its <c>IlFloatScan</c>/<c>FloatSignatureProbe</c> helpers are <c>internal</c> to
    /// <c>Quarp.CartKit</c>, this test project has no <c>InternalsVisibleTo</c> grant for them,
    /// and adding one is out of this stage's file zone (<c>Quarp.CartKit.csproj</c> is not owned
    /// here) — so, per the work order, this is a second, independent, much smaller reader. It
    /// does not hand-roll an ECMA-335 operand-length table the way <c>IlFloatScan</c> does:
    /// every legal opcode's <see cref="OperandType"/> is already sitting in
    /// <see cref="OpCodes"/> as BCL metadata, so <see cref="OperandLength"/> below is a ~10-case
    /// switch over that enum, not a copy of <c>IlFloatScan</c>'s table — a different, smaller
    /// fact, not a second copy of the same one.</para>
    /// <para><b>Red-form negative control (M4 stage 4.1 fix wave report):</b> a temporary
    /// <c>private static int Half(int v) { double f = v / 2.0; return (int)f; }</c> called from
    /// <see cref="Std.PrintCentered(IConsoleApi,string,int,byte,Font)"/> turned this test red
    /// (both a <c>double</c> local and <c>conv.r8</c>/<c>ldc.r8</c> opcodes), then was removed —
    /// shown with a sha256 of <c>Std.cs</c> before and after in the report, not just asserted.</para>
    /// </summary>
    [Fact]
    public void CompiledAssemblyHasNoFloatOpcodesOrLocalsAnywhereIncludingPrivateMembers()
    {
        var offenders = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in typeof(Cartridge).Assembly.GetTypes())
        {
            IEnumerable<MethodBase> members =
                type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
            foreach (MethodBase method in members)
            {
                MethodBody? body;
                try
                {
                    body = method.GetMethodBody();
                }
                catch (InvalidOperationException)
                {
                    continue;   // e.g. a P/Invoke or otherwise body-less member.
                }
                if (body is null)
                {
                    continue;   // abstract, extern, or an interface member: nothing to walk.
                }

                string methodName = $"{type.FullName}.{method.Name}";

                foreach (LocalVariableInfo local in body.LocalVariables)
                {
                    if (IsFloatingPoint(local.LocalType))
                    {
                        offenders.Add($"{methodName} (local #{local.LocalIndex}: {local.LocalType})");
                    }
                }

                byte[]? il = body.GetILAsByteArray();
                if (il is not null && TryFindFloatOpcode(il, out string opcodeName, out int offset))
                {
                    offenders.Add($"{methodName} (IL '{opcodeName}' at offset 0x{offset:X4})");
                }
            }
        }

        Assert.True(offenders.Count == 0, "float opcode or local reaches Quarp.Api.dll: " + string.Join(", ", offenders));
    }

    /// <summary>Every legal opcode, keyed by its <see cref="OpCode.Value"/>, built once from <see cref="OpCodes"/>'s own fields.</summary>
    private static readonly Dictionary<short, OpCode> OpcodesByValue = BuildOpcodeTable();

    private static Dictionary<short, OpCode> BuildOpcodeTable()
    {
        var table = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
            {
                continue;
            }
            var opcode = (OpCode)field.GetValue(null)!;
            table[opcode.Value] = opcode;
        }
        return table;
    }

    /// <summary>
    /// Walks <paramref name="il"/> instruction by instruction using <see cref="OpcodesByValue"/>
    /// for both opcode identification and operand length, stopping at the first
    /// <c>ldc.r4</c>/<c>ldc.r8</c>/<c>conv.r4</c>/<c>conv.r8</c>. An opcode this table does not
    /// recognize throws rather than silently ending the walk — a desynchronized walk that just
    /// stops is a silent false negative, not a safe default.
    /// </summary>
    private static bool TryFindFloatOpcode(byte[] il, out string opcodeName, out int offset)
    {
        int position = 0;
        while (position < il.Length)
        {
            int start = position;
            short value = il[position];
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[position + 1]);
            }
            if (!OpcodesByValue.TryGetValue(value, out OpCode opcode))
            {
                throw new InvalidOperationException(
                    $"unrecognized IL opcode 0x{value:X4} at offset 0x{start:X4} in a Quarp.Api "
                    + "method -- extend this reader's table rather than trust a silent stop.");
            }
            position = start + opcode.Size;

            if (opcode == OpCodes.Ldc_R4 || opcode == OpCodes.Conv_R4)
            {
                opcodeName = "ldc.r4/conv.r4";
                offset = start;
                return true;
            }
            if (opcode == OpCodes.Ldc_R8 || opcode == OpCodes.Conv_R8)
            {
                opcodeName = "ldc.r8/conv.r8";
                offset = start;
                return true;
            }

            position += OperandLength(opcode.OperandType, il, position);
        }
        opcodeName = string.Empty;
        offset = 0;
        return false;
    }

    /// <summary>Operand byte length for every <see cref="OperandType"/> Roslyn can emit; <c>InlineSwitch</c> is 4 bytes of jump-target count plus 4 bytes per target.</summary>
    private static int OperandLength(OperandType type, byte[] il, int position) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok
            or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position, 4))),
        _ => throw new InvalidOperationException($"unhandled IL operand type {type} -- extend this reader's switch."),
    };

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
