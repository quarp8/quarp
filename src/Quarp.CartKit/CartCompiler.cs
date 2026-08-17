using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Quarp.Api;

namespace Quarp.CartKit;

/// <summary>
/// Compiles cartridge sources in memory — Roslyn, AllowUnsafe=false, deterministic,
/// embedded portable PDB (line numbers in cart stack traces are a milestone criterion) —
/// then runs the M1 determinism filters (work order "Конвейер картриджа"):
/// <list type="number">
///   <item>a syntax scan banning float/double/decimal spellings and real literals, which
///     buys the author a precise file:line message;</item>
///   <item>a metadata scan of the emitted DLL's TypeRef/MemberRef tables against the
///     banned-API list;</item>
///   <item>a float scan of the emitted DLL — signatures and IL — which is the ban that
///     actually holds: the syntax filter can be routed around by spelling
///     <c>System.Double</c> or aliasing it, but nothing can be written in C# that
///     produces float arithmetic without leaving float element types or float opcodes in
///     the assembly.</item>
/// </list>
/// This is the honest M1 ban; the full Roslyn analyzer arrives in M2. User errors never
/// throw — they come back as <see cref="CartCompileResult.Diagnostics"/> so the shell can
/// keep the previous cartridge running during hot reload.
///
/// Decision (repair pass, D4): cartridges compile with
/// <see cref="OptimizationLevel.Debug"/>. Carts are at most 64 KB of code, so the
/// micro-performance of optimized IL is irrelevant next to the milestone criterion that
/// a cart exception shows an accurate line number.
/// </summary>
public static class CartCompiler
{
    private static readonly object ReferencesGate = new();
    private static ImmutableArray<MetadataReference> _references;

    // Namespaces whose entire subtree is banned in cart code (SPEC-8 §7, ARCHITECTURE §3).
    private static readonly string[] BannedNamespacePrefixes =
    {
        "System.IO",
        "System.Net",
        "System.Threading",
        "System.Runtime.InteropServices",
    };

    // Namespaces banned except for their attribute types: the compiler itself emits
    // assembly-level [Debuggable] and per-member [DebuggerHidden]/[Nullable]-style
    // attributes, and those must stay legal for ordinary cart code to build at all.
    // System.Diagnostics is here because of the Stopwatch/StackTrace/Process family
    // (wall clock and process access in the tick path), System.Reflection because of
    // reflection proper.
    private static readonly string[] AttributeExemptBannedNamespacePrefixes =
    {
        "System.Reflection",
        "System.Diagnostics",
    };

    // Individual banned types in the System namespace; DateTime* is matched by prefix.
    private static readonly string[] BannedSystemTypes =
    {
        "Random",
        "Guid",
        "Environment",
        "Math",
        "MathF",
        "Console",
        "AppDomain",
        "Activator",
        "GC",
        "TimeProvider",
    };

    // Banned single types outside System, given in full. System.Runtime.CompilerServices
    // as a whole cannot be banned: string interpolation lowers to
    // DefaultInterpolatedStringHandler and `init` accessors to an IsExternalInit modreq,
    // both of which live there — so the ban is per type.
    private static readonly string[] BannedFullTypeNames =
    {
        "System.Runtime.CompilerServices.Unsafe",
    };

    // Types the compiler emits by itself for ordinary cart code that happen to sit inside
    // a banned namespace. Both are inert metadata markers — an enum and a signature
    // modifier — with no capability behind them, and both were confirmed live: without
    // this list an auto-property or an `in` parameter fails to build.
    private static readonly string[] AllowedFullTypeNames =
    {
        // Argument of [DebuggerBrowsable(...)], stamped on every auto-property backing field.
        "System.Diagnostics.DebuggerBrowsableState",
        // modreq on `in` parameters and `ref readonly` returns — e.g. indexing a ReadOnlySpan.
        // The rest of System.Runtime.InteropServices stays banned; that ban is what cuts DllImport.
        "System.Runtime.InteropServices.InAttribute",
    };

    // Banned individual members of otherwise-allowed types. RuntimeHelpers must stay
    // allowed: the compiler routes every array initializer through
    // RuntimeHelpers.InitializeArray (and array-range slicing through GetSubArray), which
    // ordinary cart code hits immediately — see carts/snake's DirDx/DirDy tables.
    private static readonly string[] BannedMembers =
    {
        "System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject",
    };

    // Identifier spellings of the banned real types. The C# keywords are caught as
    // keyword tokens; these are the ways around them — `System.Double x`, `Double x`
    // after `using System;`, `using D = System.Double;`.
    private static readonly string[] BannedTypeIdentifiers =
    {
        "Single",
        "Double",
        "Decimal",
        "Half",
    };

    public static CartCompileResult Compile(CartData cart) =>
        Compile(cart.Sources, MakeAssemblyName(cart.Manifest.Name));

    public static CartCompileResult Compile(IReadOnlyList<CartSourceFile> sources, string assemblyName = "cart")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new SyntaxTree[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            // An explicit encoding is required for the embedded portable PDB;
            // the relative path becomes the file name in diagnostics and stack traces.
            trees[i] = CSharpSyntaxTree.ParseText(
                SourceText.From(sources[i].Text, Encoding.UTF8),
                parseOptions,
                path: sources[i].RelativePath);
        }

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Debug,   // D4: accurate stack-trace lines beat micro-perf on a 64 KB cart
            allowUnsafe: false,
            deterministic: true);
        var compilation = CSharpCompilation.Create(assemblyName, trees, References, options);

        using var peStream = new MemoryStream();
        var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded);
        EmitResult emitResult = compilation.Emit(peStream, options: emitOptions);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (Diagnostic diagnostic in emitResult.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diagnostic.ToString());
                }
            }
            return CartCompileResult.Failed(errors);
        }

        var violations = new List<string>();
        foreach (SyntaxTree tree in trees)
        {
            ScanSyntax(compilation, tree, violations);
        }
        byte[] assemblyBytes = peStream.ToArray();
        ScanMetadata(assemblyBytes, violations);
        ScanFloats(assemblyBytes, violations);
        return violations.Count > 0 ? CartCompileResult.Failed(violations) : CartCompileResult.Ok(assemblyBytes);
    }

    /// <summary>
    /// Exercises the whole pipeline once (reference resolution, parse, emit, both scans)
    /// so the first real cartridge compile is not hit by Roslyn's own JIT cost — 1-3 s
    /// cold versus tens of milliseconds warm (ARCHITECTURE §3). The shell calls this at
    /// startup, typically on a background thread.
    /// </summary>
    public static void WarmUp()
    {
        var source = new CartSourceFile(
            "warmup.cs",
            "public sealed class WarmupCart : Quarp.Api.Cartridge { public override void Update() { } }");
        Compile(new[] { source }, "warmup");
    }

    private static ImmutableArray<MetadataReference> References
    {
        get
        {
            lock (ReferencesGate)
            {
                if (_references.IsDefault)
                {
                    _references = BuildReferences();
                }
                return _references;
            }
        }
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // The minimal BCL closure for cart code. The reference list itself enforces
        // nothing (ARCHITECTURE §3: File/Random/DateTime are public in System.Runtime) —
        // the metadata scan below is the actual filter.
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Runtime.dll",
            "System.Private.CoreLib.dll",
            "System.Collections.dll",
            "netstandard.dll",
        };
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trustedAssemblies)
        {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unavailable; cannot resolve BCL references for cart compilation.");
        }
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (wanted.Contains(Path.GetFileName(path)))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }
        builder.Add(MetadataReference.CreateFromFile(typeof(Cartridge).Assembly.Location));
        return builder.ToImmutable();
    }

    // --- post-pass (a): syntax ban on float/double/decimal (SPEC-8 §7) ---

    private static void ScanSyntax(CSharpCompilation compilation, SyntaxTree tree, List<string> violations)
    {
        // Bound lazily: carts with no candidate identifier never pay for binding.
        SemanticModel? model = null;
        foreach (SyntaxToken token in tree.GetRoot().DescendantTokens())
        {
            switch (token.Kind())
            {
                case SyntaxKind.FloatKeyword:
                case SyntaxKind.DoubleKeyword:
                case SyntaxKind.DecimalKeyword:
                    violations.Add(Format(tree, token.Span,
                        $"QRP0001: type '{token.Text}' is banned in cartridge code — use int or Fix (SPEC-8 §7)"));
                    break;
                case SyntaxKind.IdentifierToken:
                    if (IsBannedTypeIdentifier(compilation, tree, token, ref model))
                    {
                        violations.Add(Format(tree, token.Span,
                            $"QRP0001: type '{token.Text}' is banned in cartridge code — use int or Fix (SPEC-8 §7)"));
                    }
                    break;
                case SyntaxKind.NumericLiteralToken:
                    if (token.Value is float or double or decimal)
                    {
                        violations.Add(Format(tree, token.Span,
                            $"QRP0002: real literal '{token.Text}' is banned in cartridge code — "
                            + "write Fix.Ratio(numerator, denominator) instead (SPEC-8 §7)"));
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// True for a name-position use of the banned real types spelled as identifiers rather
    /// than keywords — <c>System.Double x</c>, <c>Double x</c> after <c>using System;</c>,
    /// <c>using D = System.Double;</c>.
    ///
    /// The spelling is only a cheap pre-filter; the decision is made by binding the name,
    /// because the spelling alone is both too broad and too narrow. Too broad: <c>Fix.Half</c>
    /// is a documented cartridge-facing constant (API-8.md §Fix) and a cart may declare its
    /// own <c>Half</c> helper — neither is <c>System.Half</c>, and rejecting them broke
    /// legitimate carts. Too narrow: an alias's use site (<c>D x</c>) carries none of the
    /// banned spellings at all, and binding catches it where matching text cannot.
    ///
    /// This pass remains a convenience that buys a precise file:line; the authority is the
    /// float scan of the emitted assembly, which sees through every alias and every generic.
    /// </summary>
    private static bool IsBannedTypeIdentifier(
        CSharpCompilation compilation, SyntaxTree tree, SyntaxToken token, ref SemanticModel? model)
    {
        if (token.Parent is not IdentifierNameSyntax name)
        {
            return false;
        }
        // Cheap pre-filter: either a banned spelling, or an alias that could resolve to one.
        // Everything else cannot bind to a banned type under any name.
        bool candidate = false;
        foreach (string identifier in BannedTypeIdentifiers)
        {
            if (token.ValueText == identifier)
            {
                candidate = true;
                break;
            }
        }
        if (!candidate && !IsAliasTarget(name))
        {
            return false;
        }
        model ??= compilation.GetSemanticModel(tree);
        ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }
        return symbol is INamedTypeSymbol type && IsBannedRealType(type);
    }

    /// <summary>
    /// True when the name is a use of a using-alias — the one case where a benign spelling
    /// can still be a banned type (<c>using D = System.Double;</c> then <c>D x</c>).
    /// Only simple names in type or expression position qualify; a member name after a dot
    /// never binds to an alias.
    /// </summary>
    private static bool IsAliasTarget(IdentifierNameSyntax name) =>
        name.Parent is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression == name;

    /// <summary>True for System.Single, System.Double, System.Decimal and System.Half.</summary>
    private static bool IsBannedRealType(INamedTypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal)
        {
            return true;
        }
        // System.Half has no SpecialType of its own.
        return type.Name == "Half"
            && type.ContainingNamespace is { Name: "System" } ns
            && ns.ContainingNamespace is { IsGlobalNamespace: true };
    }

    private static string Format(SyntaxTree tree, TextSpan span, string message)
    {
        LinePosition position = tree.GetLineSpan(span).StartLinePosition;
        return $"{tree.FilePath}({position.Line + 1},{position.Character + 1}): error {message}";
    }

    // --- post-pass (b): metadata scan of the emitted DLL (ARCHITECTURE §3) ---

    private static void ScanMetadata(byte[] assemblyBytes, List<string> violations)
    {
        using var peReader = new PEReader(new MemoryStream(assemblyBytes, writable: false));
        MetadataReader metadata = peReader.GetMetadataReader();

        // Banned type full name -> whether a concrete member reference was reported for it.
        var bannedTypes = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference typeRef = metadata.GetTypeReference(handle);
            string ns = metadata.GetString(typeRef.Namespace);
            string name = metadata.GetString(typeRef.Name);
            if (IsBanned(ns, name))
            {
                bannedTypes.TryAdd(ns.Length == 0 ? name : ns + "." + name, false);
            }
        }

        // No early exit on an empty bannedTypes: the member-level bans below live on
        // types that stay allowed as a whole (RuntimeHelpers), so the MemberRef table has
        // to be walked either way.
        var offendingMembers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference memberRef = metadata.GetMemberReference(handle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }
            TypeReference typeRef = metadata.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            string ns = metadata.GetString(typeRef.Namespace);
            string name = metadata.GetString(typeRef.Name);
            string full = ns.Length == 0 ? name : ns + "." + name;
            string member = full + "." + metadata.GetString(memberRef.Name);
            if (bannedTypes.ContainsKey(full))
            {
                bannedTypes[full] = true;
                offendingMembers.Add(member);
            }
            else if (IsBannedMember(member))
            {
                offendingMembers.Add(member);
            }
        }

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> pair in bannedTypes)
        {
            if (!pair.Value)
            {
                offenders.Add(pair.Key); // Type referenced without a specific member (e.g. a field type).
            }
        }
        foreach (string member in offendingMembers)
        {
            offenders.Add(member);
        }
        foreach (string offender in offenders)
        {
            violations.Add(
                $"error QRP0003: cartridge references banned API '{offender}' — "
                + "not part of the deterministic console surface (SPEC-8 §7)");
        }
    }

    private static bool IsBanned(string ns, string name)
    {
        if (ns.Length == 0)
        {
            return false;
        }
        string full = ns + "." + name;
        foreach (string allowed in AllowedFullTypeNames)
        {
            if (full == allowed)
            {
                return false;   // Checked first: an allowlist entry outranks its namespace ban.
            }
        }
        foreach (string prefix in BannedNamespacePrefixes)
        {
            if (IsInNamespace(ns, prefix))
            {
                return true;
            }
        }
        // Banned except attribute types: compiler-emitted attributes ([Debuggable] on the
        // assembly, [DebuggerHidden] and friends on synthesized members) must stay legal.
        foreach (string prefix in AttributeExemptBannedNamespacePrefixes)
        {
            if (IsInNamespace(ns, prefix) && !name.EndsWith("Attribute", StringComparison.Ordinal))
            {
                return true;
            }
        }
        foreach (string banned in BannedFullTypeNames)
        {
            if (full == banned)
            {
                return true;
            }
        }
        if (ns == "System")
        {
            if (name.StartsWith("DateTime", StringComparison.Ordinal))
            {
                return true; // DateTime, DateTimeOffset, DateTimeKind — the work order's DateTime* ban.
            }
            foreach (string banned in BannedSystemTypes)
            {
                if (name == banned)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>True when <paramref name="ns"/> is <paramref name="prefix"/> or nested inside it.</summary>
    private static bool IsInNamespace(string ns, string prefix) =>
        ns == prefix
        || (ns.Length > prefix.Length
            && ns.StartsWith(prefix, StringComparison.Ordinal)
            && ns[prefix.Length] == '.');

    /// <summary>Member-level ban for members of types that are otherwise legal to reference.</summary>
    private static bool IsBannedMember(string fullMemberName)
    {
        foreach (string banned in BannedMembers)
        {
            if (fullMemberName == banned)
            {
                return true;
            }
        }
        return false;
    }

    // --- post-pass (c): float scan of the emitted DLL (the ban that actually holds) ---

    /// <summary>
    /// Rejects floating point wherever it survives into the cartridge assembly, whatever
    /// spelling the source used — <c>double</c>, <c>System.Double</c>,
    /// <c>using D = System.Double;</c>, a generic instantiation, an array element. Two nets:
    /// <list type="bullet">
    ///   <item>every signature in the cart's own metadata — fields, methods (return type
    ///     and parameters), properties, member references, constructed types, generic
    ///     method instantiations and local-variable signatures — decoded through
    ///     <see cref="FloatSignatureProbe"/>, which sees through arrays, byrefs, pointers
    ///     and generics; plus a type-reference check, since Decimal and Half are ordinary
    ///     value types with no primitive element type of their own;</item>
    ///   <item>every IL body walked instruction by instruction for float-only opcodes
    ///     (<see cref="IlFloatScan"/>) — the case where the arithmetic happens but no
    ///     named field or local carries it.</item>
    /// </list>
    /// Only <paramref name="assemblyBytes"/> is opened; referenced assemblies are never
    /// read. A cart is judged by the signatures it references, not by what those methods
    /// do inside — so calling <c>Fix.ToString</c> can never trip this scan regardless of
    /// how Quarp.Api implements it.
    /// </summary>
    private static void ScanFloats(byte[] assemblyBytes, List<string> violations)
    {
        using var peReader = new PEReader(new MemoryStream(assemblyBytes, writable: false));
        MetadataReader metadata = peReader.GetMetadataReader();
        ISignatureTypeProvider<bool, object?> probe = FloatSignatureProbe.Instance;
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference typeRef = metadata.GetTypeReference(handle);
            string ns = metadata.GetString(typeRef.Namespace);
            string name = metadata.GetString(typeRef.Name);
            if (FloatSignatureProbe.IsFloatTypeName(ns, name))
            {
                offenders.Add($"{ns}.{name} (type reference)");
            }
        }

        // Local signatures reached through a method body are reported against that method;
        // remember them so the table sweep below does not report them a second time.
        var ownedLocalSignatures = new HashSet<StandaloneSignatureHandle>();
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            string typeName = FullTypeName(metadata, type);

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = metadata.GetFieldDefinition(fieldHandle);
                if (field.DecodeSignature(probe, null))
                {
                    offenders.Add($"{typeName}.{metadata.GetString(field.Name)} (field type)");
                }
            }

            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = metadata.GetPropertyDefinition(propertyHandle);
                if (FloatSignatureProbe.MentionsFloat(property.DecodeSignature(probe, null)))
                {
                    offenders.Add($"{typeName}.{metadata.GetString(property.Name)} (property type)");
                }
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
                string methodName = $"{typeName}.{metadata.GetString(method.Name)}";
                if (FloatSignatureProbe.MentionsFloat(method.DecodeSignature(probe, null)))
                {
                    offenders.Add($"{methodName} (signature)");
                }
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;   // Abstract or extern: no body to walk.
                }
                MethodBodyBlock body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                if (!body.LocalSignature.IsNil)
                {
                    ownedLocalSignatures.Add(body.LocalSignature);
                    ImmutableArray<bool> locals = metadata
                        .GetStandaloneSignature(body.LocalSignature)
                        .DecodeLocalSignature(probe, null);
                    if (AnyFloat(locals))
                    {
                        offenders.Add($"{methodName} (local variable)");
                    }
                }
                byte[]? il = body.GetILBytes();
                if (il is not null
                    && IlFloatScan.TryFindFloatOpcode(il, out string opcode, out int offset))
                {
                    offenders.Add($"{methodName} (IL '{opcode}' at offset 0x{offset:X4})");
                }
            }
        }

        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference memberRef = metadata.GetMemberReference(handle);
            bool mentionsFloat = memberRef.GetKind() == MemberReferenceKind.Field
                ? memberRef.DecodeFieldSignature(probe, null)
                : FloatSignatureProbe.MentionsFloat(memberRef.DecodeMethodSignature(probe, null));
            if (mentionsFloat)
            {
                offenders.Add($"{MemberReferenceName(metadata, memberRef)} (member reference)");
            }
        }

        // Standalone signatures not owned by a body: calli targets and any orphan rows.
        int standaloneCount = metadata.GetTableRowCount(TableIndex.StandAloneSig);
        for (int row = 1; row <= standaloneCount; row++)
        {
            StandaloneSignatureHandle handle = MetadataTokens.StandaloneSignatureHandle(row);
            if (ownedLocalSignatures.Contains(handle))
            {
                continue;
            }
            StandaloneSignature signature = metadata.GetStandaloneSignature(handle);
            bool mentionsFloat = signature.GetKind() == StandaloneSignatureKind.LocalVariables
                ? AnyFloat(signature.DecodeLocalSignature(probe, null))
                : FloatSignatureProbe.MentionsFloat(signature.DecodeMethodSignature(probe, null));
            if (mentionsFloat)
            {
                offenders.Add($"standalone signature #{row}");
            }
        }

        // Constructed types (List<double>, double[,]) and generic method instantiations
        // (Foo<double>()) can be referenced from IL alone, with no field or local to show for it.
        int typeSpecCount = metadata.GetTableRowCount(TableIndex.TypeSpec);
        for (int row = 1; row <= typeSpecCount; row++)
        {
            TypeSpecificationHandle handle = MetadataTokens.TypeSpecificationHandle(row);
            if (metadata.GetTypeSpecification(handle).DecodeSignature(probe, null))
            {
                offenders.Add($"constructed type #{row}");
            }
        }

        int methodSpecCount = metadata.GetTableRowCount(TableIndex.MethodSpec);
        for (int row = 1; row <= methodSpecCount; row++)
        {
            MethodSpecification spec = metadata.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
            if (AnyFloat(spec.DecodeSignature(probe, null)))
            {
                offenders.Add($"{MethodName(metadata, spec.Method)} (generic type argument)");
            }
        }

        foreach (string offender in offenders)
        {
            violations.Add(
                $"error QRP0004: floating point reaches the cartridge assembly at {offender} — "
                + "float, double and decimal are banned in cartridge code, use int or Fix (SPEC-8 §7)");
        }
    }

    private static bool AnyFloat(ImmutableArray<bool> types)
    {
        foreach (bool type in types)
        {
            if (type)
            {
                return true;
            }
        }
        return false;
    }

    private static string FullTypeName(MetadataReader metadata, TypeDefinition type)
    {
        string ns = metadata.GetString(type.Namespace);
        string name = metadata.GetString(type.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string MemberReferenceName(MetadataReader metadata, MemberReference memberRef)
    {
        string member = metadata.GetString(memberRef.Name);
        if (memberRef.Parent.Kind == HandleKind.TypeReference)
        {
            TypeReference typeRef = metadata.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            string ns = metadata.GetString(typeRef.Namespace);
            string name = metadata.GetString(typeRef.Name);
            return (ns.Length == 0 ? name : ns + "." + name) + "." + member;
        }
        if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition type = metadata.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent);
            return FullTypeName(metadata, type) + "." + member;
        }
        return member;
    }

    private static string MethodName(MetadataReader metadata, EntityHandle method)
    {
        if (method.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition definition = metadata.GetMethodDefinition((MethodDefinitionHandle)method);
            return FullTypeName(metadata, metadata.GetTypeDefinition(definition.GetDeclaringType()))
                + "." + metadata.GetString(definition.Name);
        }
        if (method.Kind == HandleKind.MemberReference)
        {
            return MemberReferenceName(metadata, metadata.GetMemberReference((MemberReferenceHandle)method));
        }
        return "a generic method";
    }

    private static string MakeAssemblyName(string cartName)
    {
        var builder = new StringBuilder(cartName.Length);
        foreach (char c in cartName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }
        return builder.Length == 0 ? "cart" : builder.ToString();
    }
}
