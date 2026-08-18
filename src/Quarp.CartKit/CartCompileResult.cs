namespace Quarp.CartKit;

/// <summary>
/// Outcome of one cartridge compilation: either the emitted assembly (embedded portable
/// PDB inside) or user-facing diagnostics ("file(line,col): error ..."), never both.
/// User errors are values, not exceptions — during hot reload the shell prints these
/// and keeps the previous cartridge running (M1 work order).
/// <see cref="Warnings"/> is carried separately from M2 on: the analyzer's QRP1003 is a
/// warning by design (API-8 §12), and a warning that fails the build is a warning nobody
/// can afford to leave un-suppressed. Warnings are printed and ignored.
/// </summary>
public sealed class CartCompileResult
{
    private static readonly string[] None = Array.Empty<string>();

    private readonly byte[]? _assemblyBytes;

    private CartCompileResult(byte[]? assemblyBytes, IReadOnlyList<string> diagnostics, IReadOnlyList<string> warnings)
    {
        _assemblyBytes = assemblyBytes;
        Diagnostics = diagnostics;
        Warnings = warnings;
    }

    public bool Success => _assemblyBytes is not null;

    /// <summary>Empty on success; otherwise one printable line per problem.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>
    /// Non-fatal findings, in the same printable shape as <see cref="Diagnostics"/>. Present
    /// on success and on failure — a cart that fails on QRP1001 may still deserve to hear
    /// about its QRP1003.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The emitted assembly image; throws when <see cref="Success"/> is false.</summary>
    public byte[] AssemblyBytes => _assemblyBytes
        ?? throw new InvalidOperationException("Compilation failed; check Diagnostics.");

    public static CartCompileResult Ok(byte[] assemblyBytes) => Ok(assemblyBytes, None);

    public static CartCompileResult Ok(byte[] assemblyBytes, IReadOnlyList<string> warnings) =>
        new(assemblyBytes, None, warnings);

    public static CartCompileResult Failed(IReadOnlyList<string> diagnostics) => Failed(diagnostics, None);

    public static CartCompileResult Failed(IReadOnlyList<string> diagnostics, IReadOnlyList<string> warnings) =>
        new(null, diagnostics, warnings);
}
