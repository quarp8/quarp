using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarp.Analyzers;

/// <summary>
/// QRP1003 — <c>foreach</c> over a hash-ordered collection (SPEC-8 §7.6). .NET randomises
/// string hash codes once per process, so a <c>Dictionary</c> or <c>HashSet</c> enumerates
/// in a different order in every run. Nothing goes wrong on the machine that recorded the
/// replay; it goes wrong on the machine that plays it back, which is the failure this
/// milestone exists to make impossible.
///
/// A warning rather than an error: iterating a dictionary is often perfectly order-insensitive
/// (summing values, drawing every entry at its own coordinates), and the author is in a better
/// position than the analyzer to know. What the rule guarantees is that nobody does it by
/// accident.
///
/// Deliberately silent on the sorted collections — <c>SortedDictionary</c>,
/// <c>SortedSet</c>, <c>SortedList</c> — whose enumeration order is defined by a comparer and
/// is therefore reproducible. They are the recommended fix, so warning on them would be
/// perverse.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnorderedIterationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Metadata names of the collections whose enumeration order is a hash-table artefact.
    /// The nested Key/Value collections matter as much as the dictionary itself:
    /// <c>foreach (var k in map.Keys)</c> has exactly the same problem.
    /// </summary>
    private static readonly string[] UnorderedTypeNames =
    {
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.Dictionary`2+KeyCollection",
        "System.Collections.Generic.Dictionary`2+ValueCollection",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Hashtable",
    };

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(QuarpDiagnostics.UnorderedIteration);

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
            // Resolved once per compilation: a cartridge that never references a dictionary
            // costs nothing beyond this lookup.
            ImmutableArray<INamedTypeSymbol> unordered = Resolve(start.Compilation);
            if (unordered.IsEmpty)
            {
                return;
            }
            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeForEach(nodeContext, unordered),
                SyntaxKind.ForEachStatement,
                SyntaxKind.ForEachVariableStatement);
        });
    }

    private static ImmutableArray<INamedTypeSymbol> Resolve(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>(UnorderedTypeNames.Length);
        foreach (string metadataName in UnorderedTypeNames)
        {
            INamedTypeSymbol? type = compilation.GetTypeByMetadataName(metadataName);
            if (type is not null)
            {
                builder.Add(type);
            }
        }
        return builder.ToImmutable();
    }

    private static void AnalyzeForEach(SyntaxNodeAnalysisContext context, ImmutableArray<INamedTypeSymbol> unordered)
    {
        // ForEachStatementSyntax and ForEachVariableStatementSyntax (deconstructing foreach,
        // the natural way to walk a dictionary) share this base.
        var statement = (CommonForEachStatementSyntax)context.Node;
        ITypeSymbol? collection = context.SemanticModel
            .GetTypeInfo(statement.Expression, context.CancellationToken).Type;
        if (collection is not INamedTypeSymbol named)
        {
            return;
        }
        // The constructed type is Dictionary<string, int>; the ban is on its definition.
        INamedTypeSymbol definition = named.OriginalDefinition;
        foreach (INamedTypeSymbol candidate in unordered)
        {
            if (SymbolEqualityComparer.Default.Equals(definition, candidate))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QuarpDiagnostics.UnorderedIteration,
                    statement.Expression.GetLocation(),
                    named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return;
            }
        }
    }
}
