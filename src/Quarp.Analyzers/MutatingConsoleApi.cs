using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Quarp.Analyzers;

/// <summary>
/// The console calls that write <em>simulation</em> state, as opposed to the frame.
///
/// The list is derived from <c>Quarp.Api.IConsoleApi</c> and checked against
/// <c>Quarp.Core.VirtualConsole</c>, not copied from a work order. The test is one question:
/// <em>does this call change something a resimulation from tick 0 has to reproduce?</em>
/// SPEC-8 §7 names that state — the RNG, the sprite sheet, the map, sprite flags and the
/// 64 slots of persistent memory — and since M3 the sound chip, whose PCM the
/// cross-architecture CI job compares block by block. The nine members below are the whole of
/// what writes to it:
/// <list type="table">
///   <item><term><c>Rnd</c>, <c>RndInt</c></term><description>consume one xoshiro128** draw
///     each (<c>VirtualConsole.NextRandom</c> advances <c>_rng0.._rng3</c>), and the RNG
///     state is simulation state by SPEC-8 §7.4;</description></item>
///   <item><term><c>Srand</c></term><description>overwrites that state outright;</description></item>
///   <item><term><c>Sset</c></term><description>writes the sprite sheet, which
///     <c>Spr</c>/<c>Map</c>/<c>Sget</c> read back;</description></item>
///   <item><term><c>Mset</c></term><description>writes the map;</description></item>
///   <item><term><c>Fset</c></term><description>writes sprite flags, which also steer
///     <c>Map</c>'s flag filter;</description></item>
///   <item><term><c>Dset</c></term><description>writes persistent memory — the second
///     external input of the simulation (REPLAY-FORMAT §2);</description></item>
///   <item><term><c>Sfx</c>, <c>Music</c></term><description>start a sound, which is to say
///     they write channel and sequencer state inside <c>Quarp.Core.Audio.Apu</c>. Added in M3
///     (organizer decision, M3 work order §"Решения организатора"). Draw runs once per
///     <em>frame</em> while Update runs once per <em>tick</em>, and a rewind resimulates
///     ticks without drawing at all — so a sound started from Draw plays a different number
///     of times on every run, and the PCM hash diverges exactly the way an RNG draw from Draw
///     would diverge the frame hash.</description></item>
/// </list>
///
/// Everything else on the interface stays legal in <c>Draw</c> on purpose:
/// <list type="bullet">
///   <item>the pure drawing calls — <c>Cls</c>, <c>Pset</c>, <c>Line</c>, <c>Rect</c>,
///     <c>RectFill</c>, <c>Circ</c>, <c>CircFill</c>, <c>Spr</c>, <c>Map</c>, <c>Print</c> —
///     touch the framebuffer, which a rewind repaints from scratch on the landing tick;</item>
///   <item><c>Camera</c>, <c>Clip</c>, <c>Pal</c>, <c>Palt</c> change how later drawing is
///     transformed, not what the simulation computes; re-applying them at the top of every
///     <c>Draw</c> is the documented idiom (<c>carts/snake</c> does exactly that);</item>
///   <item>every read — <c>Pget</c>, <c>Mget</c>, <c>Fget</c>, <c>Sget</c>, <c>Dget</c>,
///     <c>Btn</c>, <c>Btnp</c>, <c>Ticks</c> — writes nothing;</item>
/// </list>
/// </summary>
internal sealed class MutatingConsoleApi
{
    /// <summary>Metadata name of the console interface; a cartridge that holds one directly calls it through this.</summary>
    private const string ConsoleApiMetadataName = "Quarp.Api.IConsoleApi";

    /// <summary>
    /// The member names, and also the pre-filter: an invocation whose callee is not spelled
    /// with one of these nine names cannot bind to a mutating member, and skipping it costs
    /// one string comparison instead of a symbol lookup.
    /// </summary>
    private static readonly string[] MemberNames =
    {
        "Rnd", "RndInt", "Srand", "Sset", "Mset", "Fset", "Dset", "Sfx", "Music",
    };

    private readonly ImmutableHashSet<ISymbol> _members;

    private MutatingConsoleApi(ImmutableHashSet<ISymbol> members) => _members = members;

    /// <summary>
    /// Resolves the nine members on both types that expose them — the interface and the
    /// <c>Cartridge</c> base class whose protected wrappers are what cartridge code actually
    /// writes — or <c>null</c> when neither type is in the compilation.
    /// Every overload of every name is taken, so adding one later needs no change here.
    /// </summary>
    public static MutatingConsoleApi? Resolve(Compilation compilation)
    {
        var builder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        Collect(compilation.GetTypeByMetadataName(ConsoleApiMetadataName), builder);
        Collect(compilation.GetTypeByMetadataName(CartridgeScope.CartridgeMetadataName), builder);
        return builder.Count == 0 ? null : new MutatingConsoleApi(builder.ToImmutable());
    }

    /// <summary>Cheap syntactic pre-filter; see <see cref="MemberNames"/>.</summary>
    public static bool CanBeMutating(string identifier)
    {
        foreach (string name in MemberNames)
        {
            if (identifier == name)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True for a bound reference to one of the mutating members. Compares the original
    /// definition, so an explicit interface implementation or a constructed generic still
    /// matches; a cartridge's own method that happens to be called <c>Rnd</c> does not,
    /// because it binds to itself.
    /// </summary>
    public bool Contains(ISymbol symbol) => _members.Contains(symbol.OriginalDefinition);

    private static void Collect(INamedTypeSymbol? type, ImmutableHashSet<ISymbol>.Builder builder)
    {
        if (type is null)
        {
            return;
        }
        foreach (string name in MemberNames)
        {
            foreach (ISymbol member in type.GetMembers(name))
            {
                if (member is IMethodSymbol)
                {
                    builder.Add(member);
                }
            }
        }
    }
}
