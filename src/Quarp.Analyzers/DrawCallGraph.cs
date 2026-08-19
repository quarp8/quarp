using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarp.Analyzers;

/// <summary>
/// Answers one question for QRP1004: <em>can this method run anywhere except inside
/// <c>Draw</c>?</em>
///
/// <para><b>Why an interprocedural rule at all.</b> A direct-call check would catch
/// <c>Rnd()</c> written in the body of <c>Draw</c> and nothing else, and real cartridges do
/// not write <c>Draw</c> that way — <c>carts/snake</c>'s <c>Draw</c> is five calls to
/// <c>DrawHud</c>, <c>DrawField</c>, <c>DrawEndPanel</c> and their own helpers, two levels
/// deep. A sparkle drawn with <c>Rnd</c> inside <c>DrawApple</c> is the realistic shape of
/// this bug, and a rule that misses it protects nobody.</para>
///
/// <para><b>Why "only from Draw" and not "reachable from Draw".</b> A helper that both
/// <c>Update</c> and <c>Draw</c> call is not evidence of anything: the <c>Rnd</c> inside it
/// is legitimate on the <c>Update</c> path, and the analyzer cannot tell which call site the
/// author meant. Reporting there would put an error on correct code, and an error that has
/// to be suppressed teaches authors to suppress it. So the set reported is
/// <c>reachable(Draw) \ reachable(everything else)</c>: methods that exist for drawing and
/// for nothing else, where a write to simulation state has no legitimate reading.</para>
///
/// <para><b>The graph.</b> Nodes are methods declared in this compilation's own source
/// (constructors included; lambdas and local functions are folded into the member that
/// declares them, since that is where they are reachable from). Edges come from every bound
/// method reference in the source — invocations, method groups, <c>new T(...)</c>, and a
/// property read routed to its getter. A method with no in-source caller is a root: the
/// engine's own entry points (<c>Init</c>, <c>Update</c>), constructors, dead code. Every
/// root except a <c>Draw</c> override taints what it reaches.</para>
///
/// <para><b>What it deliberately does not model,</b> all of it failing towards silence
/// rather than towards a false error: delegates stored in a field and invoked elsewhere
/// (the reference site is the edge, not the invocation), property setters, indexers,
/// virtual dispatch through a base-class reference (the edge lands on the declared method,
/// not the override), and anything in another assembly — a cartridge is a single
/// compilation, so there is no other assembly to walk. Every one of those makes a helper
/// look reachable from somewhere other than <c>Draw</c>, which is the direction that stays
/// quiet.</para>
///
/// <para><b>Cost.</b> One pass over every syntax node of the cartridge with a symbol lookup
/// per name, built at most once per compilation and only when a mutating call actually
/// appears outside a <c>Draw</c> override. A cartridge is at most 256 KB of code (SPEC-8 §6),
/// which puts the pass in the tens of milliseconds.</para>
/// </summary>
internal sealed class DrawCallGraph
{
    private readonly HashSet<IMethodSymbol> _drawOnly;

    private DrawCallGraph(HashSet<IMethodSymbol> drawOnly) => _drawOnly = drawOnly;

    /// <summary>
    /// True when every way of reaching <paramref name="method"/> starts at an override of
    /// <c>Cartridge.Draw</c> — so anything it writes is written on drawn frames only.
    /// </summary>
    public bool IsDrawOnly(IMethodSymbol method) => _drawOnly.Contains(Canonical(method));

    /// <summary>Builds the graph and reduces it to the Draw-only set.</summary>
    public static DrawCallGraph Build(Compilation compilation, CancellationToken cancellationToken)
    {
        IMethodSymbol? cartridgeDraw = FindCartridgeDraw(compilation);
        var calls = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
        var called = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var all = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (SyntaxNode node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
                IMethodSymbol? callee = Callee(model, node, cancellationToken);
                if (callee is null)
                {
                    continue;
                }
                IMethodSymbol? caller = EnclosingMethod(model, node.SpanStart, cancellationToken);
                if (caller is null)
                {
                    // A field initializer or an attribute argument: it runs at construction,
                    // never from Draw, and nothing it reaches may be called Draw-only.
                    continue;
                }
                all.Add(caller);
                if (!IsFromSource(callee))
                {
                    // A console call or any other metadata member: it is an edge out of the
                    // graph, but the caller is now known to be a real node.
                    continue;
                }
                all.Add(callee);
                called.Add(callee);
                if (!calls.TryGetValue(caller, out HashSet<IMethodSymbol>? callees))
                {
                    callees = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                    calls.Add(caller, callees);
                }
                callees.Add(callee);
            }
        }

        var drawEntries = new List<IMethodSymbol>();
        var otherRoots = new List<IMethodSymbol>();
        foreach (IMethodSymbol method in all)
        {
            if (IsDrawOverride(method, cartridgeDraw))
            {
                // The engine calls every Draw override once per drawn frame, whether or not
                // the cartridge also calls it itself, so it is always an entry point.
                drawEntries.Add(method);
            }
            else if (!called.Contains(method))
            {
                otherRoots.Add(method);
            }
        }

        HashSet<IMethodSymbol> drawOnly = Reachable(drawEntries, calls, cancellationToken);
        drawOnly.ExceptWith(Reachable(otherRoots, calls, cancellationToken));
        return new DrawCallGraph(drawOnly);
    }

    /// <summary>Breadth-first closure over the call edges, including the roots themselves.</summary>
    private static HashSet<IMethodSymbol> Reachable(
        List<IMethodSymbol> roots,
        Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> calls,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<IMethodSymbol>();
        foreach (IMethodSymbol root in roots)
        {
            if (seen.Add(root))
            {
                queue.Enqueue(root);
            }
        }
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMethodSymbol current = queue.Dequeue();
            if (!calls.TryGetValue(current, out HashSet<IMethodSymbol>? callees))
            {
                continue;
            }
            foreach (IMethodSymbol callee in callees)
            {
                if (seen.Add(callee))
                {
                    queue.Enqueue(callee);
                }
            }
        }
        return seen;
    }

    /// <summary>
    /// The method <paramref name="node"/> refers to, or <c>null</c> when it refers to
    /// something else. Simple names cover invocations and method groups alike (the callee of
    /// <c>DrawHud()</c> is the name <c>DrawHud</c>); object creations carry the constructor
    /// on the whole expression. A property name is routed to its getter, which is what a read
    /// actually runs — an assignment target is skipped, because that runs the setter and the
    /// setter is not modelled (see the class remarks).
    /// </summary>
    private static IMethodSymbol? Callee(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case SimpleNameSyntax:
            case BaseObjectCreationExpressionSyntax:
                break;
            default:
                return null;
        }
        ISymbol? symbol = model.GetSymbolInfo(node, cancellationToken).Symbol;
        switch (symbol)
        {
            case IMethodSymbol method:
                return Canonical(method);
            case IPropertySymbol { IsIndexer: false, GetMethod: { } getter } when !IsAssignmentTarget(node):
                return Canonical(getter);
            default:
                return null;
        }
    }

    /// <summary>
    /// The one symbol a method is filed under. A call site binds to the <em>defining</em>
    /// half of a partial method while the body — and therefore
    /// <see cref="EnclosingMethod"/> — belongs to the implementing half; without this the
    /// two halves would be different nodes and the edge into a partial helper would be lost.
    /// </summary>
    private static IMethodSymbol Canonical(IMethodSymbol method)
    {
        IMethodSymbol definition = method.OriginalDefinition;
        return definition.PartialImplementationPart ?? definition;
    }

    /// <summary>True when the name (or the member access ending at it) is being assigned to.</summary>
    private static bool IsAssignmentTarget(SyntaxNode node)
    {
        SyntaxNode current = node;
        if (current.Parent is MemberAccessExpressionSyntax access && access.Name == current)
        {
            current = access;
        }
        return current.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == current
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
    }

    /// <summary>
    /// The member whose body contains <paramref name="position"/>, with lambdas and local
    /// functions folded into it: a lambda is reachable from wherever its declaring member is,
    /// and cartridges do not build closures that outlive the member that made them.
    /// Returns <c>null</c> outside a method body — a field initializer, an attribute.
    /// </summary>
    public static IMethodSymbol? EnclosingMethod(SemanticModel model, int position, CancellationToken cancellationToken)
    {
        for (ISymbol? symbol = model.GetEnclosingSymbol(position, cancellationToken);
             symbol is not null;
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is IMethodSymbol method)
            {
                if (method.MethodKind == MethodKind.LambdaMethod || method.MethodKind == MethodKind.LocalFunction)
                {
                    continue;
                }
                return Canonical(method);
            }
            if (symbol is INamedTypeSymbol || symbol is INamespaceSymbol)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="method"/> overrides <c>Cartridge.Draw</c>, however deep the
    /// cartridge's own class hierarchy goes.
    /// </summary>
    public static bool IsDrawOverride(IMethodSymbol method, IMethodSymbol? cartridgeDraw)
    {
        if (cartridgeDraw is null)
        {
            return false;
        }
        for (IMethodSymbol? current = method; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, cartridgeDraw))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The virtual <c>Quarp.Api.Cartridge.Draw()</c>, or <c>null</c> if Quarp.Api is not referenced.</summary>
    public static IMethodSymbol? FindCartridgeDraw(Compilation compilation)
    {
        INamedTypeSymbol? cartridge = compilation.GetTypeByMetadataName(CartridgeScope.CartridgeMetadataName);
        if (cartridge is null)
        {
            return null;
        }
        foreach (ISymbol member in cartridge.GetMembers("Draw"))
        {
            if (member is IMethodSymbol { IsVirtual: true, Parameters.Length: 0 } method)
            {
                return method;
            }
        }
        return null;
    }

    /// <summary>True for a method this compilation declares itself, as opposed to one from a reference.</summary>
    private static bool IsFromSource(IMethodSymbol method)
    {
        foreach (Location location in method.Locations)
        {
            if (location.IsInSource)
            {
                return true;
            }
        }
        return false;
    }
}
