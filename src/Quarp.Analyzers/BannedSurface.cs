using System.Text;
using Microsoft.CodeAnalysis;

namespace Quarp.Analyzers;

/// <summary>
/// The non-deterministic BCL surface, kept deliberately identical to the metadata scan in
/// <c>Quarp.CartKit.CartCompiler</c> (SPEC-8 §7, ARCHITECTURE §3). The two lists must agree:
/// an analyzer that is stricter than the compiler puts red squiggles under code that builds,
/// which trains authors to ignore it. Where they differ, the compiler wins — it reads the
/// emitted IL and cannot be talked out of a verdict by clever source.
///
/// Namespaces are matched segment by segment rather than through
/// <c>ToDisplayString</c>: this runs on every bound name in the file, on every keystroke.
/// </summary>
internal static class BannedSurface
{
    // Whole subtrees: files, sockets, threads and tasks, interop.
    private static readonly string[][] BannedNamespaces =
    {
        new[] { "System", "IO" },
        new[] { "System", "Net" },
        new[] { "System", "Threading" },
        new[] { "System", "Runtime", "InteropServices" },
    };

    // Banned except for their attribute types. System.Diagnostics holds Stopwatch, StackTrace
    // and Process; System.Reflection holds reflection proper. Their attributes stay legal
    // because the compiler emits some of them by itself and an author may reasonably write
    // [Conditional] or [DebuggerDisplay].
    private static readonly string[][] AttributeExemptNamespaces =
    {
        new[] { "System", "Reflection" },
        new[] { "System", "Diagnostics" },
    };

    // Individual banned types directly in System. DateTime, DateTimeOffset and DateTimeKind
    // are matched by the "DateTime" prefix, exactly as CartCompiler matches them.
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

    // Types inside otherwise-legal namespaces. System.Runtime.CompilerServices as a whole
    // cannot be banned — string interpolation lowers to DefaultInterpolatedStringHandler and
    // `init` accessors to an IsExternalInit modreq, both of which live there.
    private static readonly string[][] BannedFullTypeNames =
    {
        new[] { "System", "Runtime", "CompilerServices", "Unsafe" },
    };

    // Exceptions that outrank their namespace ban, mirroring CartCompiler's allow list.
    private static readonly string[][] AllowedFullTypeNames =
    {
        new[] { "System", "Diagnostics", "DebuggerBrowsableState" },
        new[] { "System", "Runtime", "InteropServices", "InAttribute" },
    };

    /// <summary>
    /// Member of an otherwise-legal type that is banned on its own: it manufactures an object
    /// without running its constructor, which is a hole straight through the field-init rules
    /// the rest of the sandbox relies on.
    /// </summary>
    public const string UninitializedObjectMember = "GetUninitializedObject";

    private static readonly string[] UninitializedObjectOwner =
        { "System", "Runtime", "CompilerServices", "RuntimeHelpers" };

    /// <summary>True when a cartridge may not reference <paramref name="type"/> at all.</summary>
    public static bool IsBannedType(INamedTypeSymbol type)
    {
        INamespaceSymbol? containing = type.ContainingNamespace;
        if (containing is null || containing.IsGlobalNamespace)
        {
            return false;   // The cartridge's own types live here; nothing banned does.
        }
        // Checked first: an allow-list entry outranks the namespace ban it sits inside.
        foreach (string[] allowed in AllowedFullTypeNames)
        {
            if (MatchesFullName(type, allowed))
            {
                return false;
            }
        }
        foreach (string[] banned in BannedNamespaces)
        {
            if (IsInNamespace(containing, banned))
            {
                return true;
            }
        }
        foreach (string[] banned in AttributeExemptNamespaces)
        {
            if (IsInNamespace(containing, banned) && !IsAttributeType(type))
            {
                return true;
            }
        }
        foreach (string[] banned in BannedFullTypeNames)
        {
            if (MatchesFullName(type, banned))
            {
                return true;
            }
        }
        if (IsNamespace(containing, "System"))
        {
            if (type.Name.StartsWith("DateTime", StringComparison.Ordinal))
            {
                return true;
            }
            foreach (string banned in BannedSystemTypes)
            {
                if (type.Name == banned)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>True for <c>System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject</c>.</summary>
    public static bool IsBannedMember(ISymbol member) =>
        member.Name == UninitializedObjectMember
        && member.ContainingType is { } owner
        && MatchesFullName(owner, UninitializedObjectOwner);

    /// <summary>
    /// The name to print. <see cref="ISymbol.ToDisplayString()"/> would spell out generic
    /// arguments, which makes the message about the wrong thing; the qualified name of the
    /// definition is what the author has to go and delete.
    /// </summary>
    public static string Describe(INamedTypeSymbol type)
    {
        var builder = new StringBuilder(type.Name);
        for (INamedTypeSymbol? outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            builder.Insert(0, '.').Insert(0, outer.Name);
        }
        for (INamespaceSymbol? ns = type.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
        {
            builder.Insert(0, '.').Insert(0, ns.Name);
        }
        return builder.ToString();
    }

    /// <summary>An attribute type, i.e. one deriving from <c>System.Attribute</c>, by name as CartCompiler matches it.</summary>
    private static bool IsAttributeType(INamedTypeSymbol type) =>
        type.Name.EndsWith("Attribute", StringComparison.Ordinal);

    /// <summary>True when <paramref name="ns"/> is <paramref name="segments"/> or nested inside it.</summary>
    private static bool IsInNamespace(INamespaceSymbol ns, string[] segments)
    {
        // Walk outward from the innermost namespace until the candidate prefix is reached;
        // anything deeper than the prefix is inside it, which is what the subtree ban means.
        for (INamespaceSymbol? candidate = ns; candidate is not null && !candidate.IsGlobalNamespace; candidate = candidate.ContainingNamespace)
        {
            if (IsNamespace(candidate, segments))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNamespace(INamespaceSymbol ns, params string[] segments)
    {
        INamespaceSymbol? current = ns;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (current is null || current.IsGlobalNamespace || current.Name != segments[i])
            {
                return false;
            }
            current = current.ContainingNamespace;
        }
        return current is not null && current.IsGlobalNamespace;
    }

    /// <summary>Matches a type against a namespace-plus-name path such as System.IO.File.</summary>
    private static bool MatchesFullName(INamedTypeSymbol type, string[] segments)
    {
        if (type.Name != segments[segments.Length - 1] || type.ContainingType is not null)
        {
            return false;
        }
        INamespaceSymbol? current = type.ContainingNamespace;
        for (int i = segments.Length - 2; i >= 0; i--)
        {
            if (current is null || current.IsGlobalNamespace || current.Name != segments[i])
            {
                return false;
            }
            current = current.ContainingNamespace;
        }
        return current is not null && current.IsGlobalNamespace;
    }
}
