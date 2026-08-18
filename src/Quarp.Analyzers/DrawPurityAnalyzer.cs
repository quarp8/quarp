using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarp.Analyzers;

/// <summary>
/// QRP1004 — a console call that writes simulation state, reached from <c>Draw</c>.
///
/// <para>SPEC-8 §7 rule 2 says <c>Draw</c> must not change game state and admits in the same
/// breath that nothing enforces it ("соблюдение — конвенция + анализатор"). M2 turns that
/// unenforced sentence into the foundation of the milestone: rewind and the "continuation"
/// hot reload resimulate from tick 0 with <c>Draw</c> suppressed for every tick except the
/// one that ends up on screen (ARCHITECTURE §4, <c>TimeMachine.SeekDrawTail</c>). A
/// cartridge that calls <c>Rnd</c> from <c>Draw</c> therefore consumes one RNG draw per
/// frame in a straight run and none of them in a resimulation, and lands in a different
/// game — silently, with no error and no crash, which is the worst way for a determinism
/// bug to present itself. This rule is what makes the convention hold.</para>
///
/// <para>The banned members are <see cref="MutatingConsoleApi"/>: <c>Rnd</c>, <c>RndInt</c>,
/// <c>Srand</c>, <c>Sset</c>, <c>Mset</c>, <c>Fset</c>, <c>Dset</c>. Drawing, camera,
/// palette and every read stay legal — see that type for the derivation.</para>
///
/// <para>How far it looks: a call written in a <c>Draw</c> override is always reported, and
/// so is one written in a helper that <em>nothing but</em> <c>Draw</c> can reach, at any
/// depth. <see cref="DrawCallGraph"/> carries that decision and the reasoning behind it.
/// A helper shared with <c>Update</c> is left alone on purpose.</para>
///
/// <para>Unlike QRP1001, this rule has no downstream backstop: no scan of the emitted IL can
/// tell "the cartridge called Rnd" from "the cartridge called Rnd <em>from Draw</em>",
/// because <c>Draw</c> is an ordinary virtual method and the call graph is exactly what the
/// analyzer just computed. The analyzer is the enforcement point, which is why it also runs
/// inside <c>CartCompiler</c> and not only in the author's editor.</para>
///
/// <para>Audio landed in M3, and with it <c>Sfx</c> and <c>Music</c> joined the banned list in
/// <see cref="MutatingConsoleApi"/>. They now drive a PCM stream the cross-architecture CI job
/// compares block by block, and Draw is not resimulated — so starting a sound there is the
/// same class of bug as drawing a random number, and it is caught the same way.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DrawPurityAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(QuarpDiagnostics.DrawPurity);

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
            MutatingConsoleApi? mutating = MutatingConsoleApi.Resolve(start.Compilation);
            IMethodSymbol? cartridgeDraw = DrawCallGraph.FindCartridgeDraw(start.Compilation);
            if (mutating is null || cartridgeDraw is null)
            {
                return;
            }
            var graph = new GraphCache();
            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, mutating, cartridgeDraw, graph),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        MutatingConsoleApi mutating,
        IMethodSymbol cartridgeDraw,
        GraphCache graph)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // Pre-filter on the spelling. Only applied when the callee is spelled as a name at
        // all: an exotic shape such as `(this.Dset)(0, v)` skips it and pays for the binding.
        if (InvokedName(invocation.Expression) is { } name
            && !MutatingConsoleApi.CanBeMutating(name.Identifier.ValueText))
        {
            return;
        }
        ISymbol? callee = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (callee is null || !mutating.Contains(callee))
        {
            return;
        }
        IMethodSymbol? enclosing = DrawCallGraph.EnclosingMethod(
            context.SemanticModel, invocation.SpanStart, context.CancellationToken);
        if (enclosing is null)
        {
            return;
        }

        string where;
        if (DrawCallGraph.IsDrawOverride(enclosing, cartridgeDraw))
        {
            // Reported even if the cartridge also calls its own Draw from Update: the engine
            // still calls it once per drawn frame, so the write would happen twice on a drawn
            // tick and once on a resimulated one. That is the same divergence, not an excuse.
            where = "Draw";
        }
        else if (graph.Get(context.Compilation, context.CancellationToken).IsDrawOnly(enclosing))
        {
            where = $"'{enclosing.Name}', which is reachable only from Draw";
        }
        else
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            QuarpDiagnostics.DrawPurity,
            invocation.Expression.GetLocation(),
            callee.Name,
            where));
    }

    /// <summary>
    /// The name of the invoked member — <c>Rnd</c>, <c>this.Rnd</c>, <c>console?.Srand</c> —
    /// or <c>null</c> when the callee is not spelled as a name.
    /// </summary>
    private static SimpleNameSyntax? InvokedName(ExpressionSyntax callee) => callee switch
    {
        SimpleNameSyntax simple => simple,
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        _ => null,
    };

    /// <summary>
    /// Builds <see cref="DrawCallGraph"/> at most once per compilation, on the first call
    /// that actually needs it.
    ///
    /// Deliberately not <c>Lazy&lt;T&gt;</c>: <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
    /// caches the exception from a failed factory run for ever, and the exception this
    /// factory can throw is <see cref="System.OperationCanceledException"/> — the IDE
    /// cancelling one keystroke's analysis would then poison the graph for the rest of the
    /// compilation's life. Here a cancelled build simply caches nothing and the next request
    /// starts over.
    /// </summary>
    private sealed class GraphCache
    {
        private readonly object _gate = new object();
        private DrawCallGraph? _graph;

        public DrawCallGraph Get(Compilation compilation, CancellationToken cancellationToken)
        {
            DrawCallGraph? built = Volatile.Read(ref _graph);
            if (built is not null)
            {
                return built;
            }
            lock (_gate)
            {
                if (_graph is null)
                {
                    _graph = DrawCallGraph.Build(compilation, cancellationToken);
                }
                return _graph;
            }
        }
    }
}
