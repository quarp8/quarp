using Microsoft.CodeAnalysis;

namespace Quarp.Analyzers;

/// <summary>
/// The cartridge-author rule set: one descriptor per QRP id, shared by the analyzers and by
/// anything that needs to recognise the ids (CartCompiler maps them into its own diagnostic
/// list). Ids are stable — they appear in author-facing documentation (API-8.md) and in
/// <c>#pragma warning disable</c> lines inside real cartridges.
/// </summary>
public static class QuarpDiagnostics
{
    /// <summary>Category shared by every Quarp rule; what an .editorconfig severity line targets.</summary>
    public const string Category = "Determinism";

    /// <summary>Id of the float/double/decimal ban.</summary>
    public const string FloatingPointId = "QRP1001";

    /// <summary>Id of the non-deterministic-API ban.</summary>
    public const string NonDeterministicApiId = "QRP1002";

    /// <summary>Id of the unordered-iteration warning.</summary>
    public const string UnorderedIterationId = "QRP1003";

    /// <summary>Id of the Draw-purity ban.</summary>
    public const string DrawPurityId = "QRP1004";

    /// <summary>
    /// QRP1001 — a real type or a real literal in cartridge code. Error: floating point is
    /// what makes a frame differ between x64 and arm64, and the framebuffer hash is the
    /// project's golden master (SPEC-8 §7).
    /// </summary>
    public static readonly DiagnosticDescriptor FloatingPoint = new(
        FloatingPointId,
        title: "Floating point is banned in cartridge code",
        messageFormat: "{0} is banned in cartridge code — use int or Fix instead (SPEC-8 §7)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "float, double and decimal round differently on different hardware, so a cartridge "
            + "that uses them stops being bit-identical across platforms and its replays stop "
            + "reproducing. Use int for whole numbers and the Q16.16 Fix type for fractions; "
            + "write fractional constants as Fix.Ratio(numerator, denominator).");

    /// <summary>
    /// QRP1002 — a reference to the non-deterministic BCL surface. Error, and deliberately
    /// the same surface CartCompiler's metadata scan rejects, so the IDE never disagrees
    /// with the build.
    /// </summary>
    public static readonly DiagnosticDescriptor NonDeterministicApi = new(
        NonDeterministicApiId,
        title: "API is not part of the deterministic console surface",
        messageFormat: "'{0}' is banned in cartridge code — it is not part of the deterministic console surface (SPEC-8 §7)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A cartridge is a pure function of its seed, its persistent memory and its input "
            + "log. Wall clocks, ambient randomness, the environment, threads, files and "
            + "reflection are extra inputs that no replay can reproduce. Use the console "
            + "surface instead: Rnd/RndInt/Srand for randomness, Ticks for time, Dget/Dset "
            + "for persistence and SMath for mathematics.");

    /// <summary>
    /// QRP1003 — foreach over a hash-ordered collection. Warning rather than error: the code
    /// is legal and may well be order-insensitive, but if it is not, the divergence shows up
    /// only when the replay is opened on another machine.
    /// </summary>
    public static readonly DiagnosticDescriptor UnorderedIteration = new(
        UnorderedIterationId,
        title: "Iteration order of this collection is not deterministic",
        messageFormat: "foreach over '{0}' has no stable order — iterate an array, a List or sorted keys instead (SPEC-8 §7)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            ".NET randomises string hash codes once per process, so Dictionary and HashSet "
            + "enumerate in a different order in every run and on every machine. A simulation "
            + "that depends on that order diverges between the recording and the replay. "
            + "Iterate an array, a List, or a sorted key sequence.");

    /// <summary>
    /// QRP1004 — a state-mutating console call reached from Draw. Error: rewind and the
    /// "continuation" hot reload resimulate without Draw (ARCHITECTURE §4), so the write
    /// happens in a straight run and does not happen in a replay of the same session, and
    /// the two diverge with nothing on screen to say so.
    /// </summary>
    public static readonly DiagnosticDescriptor DrawPurity = new(
        DrawPurityId,
        title: "Draw must not change simulation state",
        messageFormat:
            "'{0}' changes simulation state and must not be called from {1} — rewind and hot reload "
            + "resimulate the game without Draw, so the change is lost and the session lands in a "
            + "different state (SPEC-8 §7 rule 2)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Rewinding, stepping back and reloading the cartridge all work by resimulating from "
            + "tick 0 with Draw suppressed for every tick except the one that ends up on screen. "
            + "State written from Draw therefore exists in the live run and not in the "
            + "resimulation of the same session: Rnd and RndInt each consume an RNG draw, Srand "
            + "reseeds it, and Sset, Mset, Fset and Dset write the sprite sheet, the map, sprite "
            + "flags and persistent memory. Move the call into Init or Update and let Draw read "
            + "the result — drawing, camera, palette and every read stay allowed in Draw.");
}
