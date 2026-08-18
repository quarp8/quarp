using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarp.Analyzers;

/// <summary>
/// Resolves the type a simple name in the source refers to, and picks the span worth
/// underlining. Both type-ban rules (QRP1001, QRP1002) work the same way and differ only in
/// which types they consider banned, so the walking rules live here once.
///
/// The policy is <em>types only</em>: a name reports when it names a type, and members are
/// left alone. That is not a shortcut, it is what makes one mistake produce exactly one
/// diagnostic. Every use of a banned type has to name it somewhere — <c>DateTime.Now</c>,
/// <c>System.Guid.NewGuid()</c>, <c>Stopwatch sw</c> — so pointing at the name and ignoring
/// the member after the dot covers the same ground without ever reporting twice. It is also
/// what keeps <c>Fix.Half</c> and a cartridge's own <c>Half</c> helper legal while
/// <c>System.Half</c> is not: <c>Half</c> after the dot is a member, not a type.
/// </summary>
internal static class NameReference
{
    /// <summary>The syntax kinds <see cref="ResolveType"/> understands.</summary>
    public static SyntaxKind[] NameKinds { get; } = { SyntaxKind.IdentifierName, SyntaxKind.GenericName };

    /// <summary>
    /// The type <paramref name="name"/> names, or <c>null</c> when it names a namespace, a
    /// member, a local or anything else that is not a type. Aliases resolve through to their
    /// target, so <c>using D = System.Double;</c> cannot hide anything.
    /// </summary>
    public static INamedTypeSymbol? ResolveType(
        SemanticModel model, SimpleNameSyntax name, CancellationToken cancellationToken)
    {
        if (IsQualifierOfLongerName(name))
        {
            return null;
        }
        ISymbol? symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }
        return symbol as INamedTypeSymbol;
    }

    /// <summary>
    /// The outermost node that still ends at <paramref name="name"/>, so the squiggle covers
    /// <c>System.Diagnostics.Stopwatch</c> rather than a bare <c>Stopwatch</c>.
    /// </summary>
    public static SyntaxNode ReportTarget(SimpleNameSyntax name)
    {
        SyntaxNode current = name;
        while (true)
        {
            switch (current.Parent)
            {
                case QualifiedNameSyntax qualified when qualified.Right == current:
                    current = qualified;
                    continue;
                case MemberAccessExpressionSyntax access when access.Name == current:
                    current = access;
                    continue;
                case AliasQualifiedNameSyntax aliasQualified when aliasQualified.Name == current:
                    current = aliasQualified;
                    continue;
                default:
                    return current;
            }
        }
    }

    /// <summary>
    /// True when the name is only the left-hand part of a longer name — the <c>System</c> of
    /// <c>System.Random</c>, or the alias being introduced by a using directive. The longer
    /// name is analysed instead, which is what gives the diagnostic its full span.
    /// Deliberately not applied to the receiver of a member access (<c>DateTime</c> in
    /// <c>DateTime.Now</c>): under the types-only policy the receiver <em>is</em> the type
    /// reference, and nothing after the dot would report in its place.
    /// </summary>
    private static bool IsQualifierOfLongerName(SimpleNameSyntax name)
    {
        switch (name.Parent)
        {
            case QualifiedNameSyntax qualified:
                return qualified.Left == name;
            case AliasQualifiedNameSyntax aliasQualified:
                return aliasQualified.Alias == name;
            case NameEqualsSyntax:
                // The alias being declared in `using D = System.Double;`. The right-hand side
                // carries the diagnostic; the new name is not a reference to anything yet.
                return true;
            default:
                return false;
        }
    }
}
