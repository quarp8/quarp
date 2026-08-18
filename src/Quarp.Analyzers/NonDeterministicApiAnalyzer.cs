using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarp.Analyzers;

/// <summary>
/// QRP1002 — a cartridge reaching outside the deterministic console surface: ambient
/// randomness (<c>System.Random</c>), wall clocks (<c>DateTime</c>, <c>DateTimeOffset</c>,
/// <c>Stopwatch</c>, <c>TimeProvider</c>), identity (<c>Guid</c>), the process environment,
/// threads and tasks, files and sockets, reflection and interop. Every one of them is an
/// input the replay log does not record, so a session that touches one cannot be replayed —
/// least of all on another machine, which is the whole point of the milestone.
///
/// The surface is <see cref="BannedSurface"/>, which is kept in step with CartCompiler's
/// metadata scan so the editor and the build never disagree.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonDeterministicApiAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(QuarpDiagnostics.NonDeterministicApi);

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
            start.RegisterSyntaxNodeAction(AnalyzeName, NameReference.NameKinds);
        });
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        INamedTypeSymbol? type = NameReference.ResolveType(context.SemanticModel, name, context.CancellationToken);
        if (type is not null && BannedSurface.IsBannedType(type))
        {
            Report(context, name, BannedSurface.Describe(type));
            return;
        }
        // One member is banned on a type that stays legal (RuntimeHelpers has to stay legal:
        // every array initializer in a cartridge goes through it), so it needs its own look.
        // The name comparison is the pre-filter — nothing else pays for the extra binding.
        if (name.Identifier.ValueText != BannedSurface.UninitializedObjectMember)
        {
            return;
        }
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
        if (symbol is not null && BannedSurface.IsBannedMember(symbol))
        {
            Report(context, name, BannedSurface.Describe(symbol.ContainingType!) + "." + symbol.Name);
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, SimpleNameSyntax name, string description)
    {
        SyntaxNode target = NameReference.ReportTarget(name);
        context.ReportDiagnostic(Diagnostic.Create(
            QuarpDiagnostics.NonDeterministicApi, target.GetLocation(), description));
    }
}
