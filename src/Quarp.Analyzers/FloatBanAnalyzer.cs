using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarp.Analyzers;

/// <summary>
/// QRP1001 — floating point in cartridge code, reported as an error while the author types.
/// Three shapes are caught:
/// <list type="bullet">
///   <item>the keywords <c>float</c>, <c>double</c>, <c>decimal</c>;</item>
///   <item>the same types spelled as names — <c>System.Double</c>, <c>Double</c> after
///     <c>using System;</c>, a using alias, a generic argument — plus <c>System.Half</c>,
///     which CartCompiler also rejects;</item>
///   <item>real literals: <c>1.5</c>, <c>1e3</c>, <c>0.5f</c>, <c>2m</c>.</item>
/// </list>
/// This is an authoring aid, not the enforcement point: an author determined to smuggle a
/// double past it still meets CartCompiler's scan of the emitted IL, which sees float
/// opcodes no matter how the source was spelled. The value here is the red squiggle in the
/// editor with the right span, three seconds after the mistake instead of at the next build.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FloatBanAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(QuarpDiagnostics.FloatingPoint);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!CartridgeScope.IsCartridgeCompilation(start.Compilation))
            {
                return;
            }
            start.RegisterSyntaxNodeAction(AnalyzePredefinedType, SyntaxKind.PredefinedType);
            start.RegisterSyntaxNodeAction(AnalyzeName, NameReference.NameKinds);
            start.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.NumericLiteralExpression);
        });
    }

    /// <summary>The keyword spellings. A predefined-type keyword can only mean its own type, so no binding is needed.</summary>
    private static void AnalyzePredefinedType(SyntaxNodeAnalysisContext context)
    {
        var node = (PredefinedTypeSyntax)context.Node;
        switch (node.Keyword.Kind())
        {
            case SyntaxKind.FloatKeyword:
            case SyntaxKind.DoubleKeyword:
            case SyntaxKind.DecimalKeyword:
                context.ReportDiagnostic(Diagnostic.Create(
                    QuarpDiagnostics.FloatingPoint, node.GetLocation(), $"type '{node.Keyword.Text}'"));
                break;
        }
    }

    /// <summary>
    /// The name spellings. The identifier text is only a pre-filter; the verdict comes from
    /// binding, which is what lets <c>Fix.Half</c> and a cartridge's own <c>Half</c> helper
    /// through — those are members, not types — while stopping <c>System.Half</c>.
    /// </summary>
    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        if (!IsRealTypeSpelling(name.Identifier.ValueText))
        {
            return;     // Cheap pre-filter: no other spelling can bind to a banned real type.
        }
        INamedTypeSymbol? type = NameReference.ResolveType(context.SemanticModel, name, context.CancellationToken);
        if (type is null || !IsBannedRealType(type))
        {
            return;
        }
        SyntaxNode target = NameReference.ReportTarget(name);
        context.ReportDiagnostic(Diagnostic.Create(
            QuarpDiagnostics.FloatingPoint, target.GetLocation(), $"type '{type.Name}'"));
    }

    private static void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        if (literal.Token.Value is float or double or decimal)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QuarpDiagnostics.FloatingPoint,
                literal.GetLocation(),
                $"real literal '{literal.Token.Text}'"));
        }
    }

    /// <summary>
    /// The identifier texts that can name a banned real type. An alias
    /// (<c>using D = System.Double;</c>) is caught at the directive, where the target is
    /// spelled out, so alias use sites need no entry here.
    /// </summary>
    private static bool IsRealTypeSpelling(string identifier) =>
        identifier == "Single" || identifier == "Double" || identifier == "Decimal" || identifier == "Half";

    /// <summary>True for System.Single, System.Double, System.Decimal and System.Half.</summary>
    private static bool IsBannedRealType(INamedTypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
        }
        // System.Half has no SpecialType of its own, so it is matched by name and namespace.
        return type.Name == "Half"
            && type.ContainingNamespace is { Name: "System" } ns
            && ns.ContainingNamespace is { IsGlobalNamespace: true };
    }
}
