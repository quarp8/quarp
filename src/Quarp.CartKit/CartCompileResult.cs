namespace Quarp.CartKit;

/// <summary>
/// Outcome of one cartridge compilation: either the emitted assembly (embedded portable
/// PDB inside) or user-facing diagnostics ("file(line,col): error ..."), never both.
/// User errors are values, not exceptions — during hot reload the shell prints these
/// and keeps the previous cartridge running (M1 work order).
/// </summary>
public sealed class CartCompileResult
{
    private readonly byte[]? _assemblyBytes;

    private CartCompileResult(byte[]? assemblyBytes, IReadOnlyList<string> diagnostics)
    {
        _assemblyBytes = assemblyBytes;
        Diagnostics = diagnostics;
    }

    public bool Success => _assemblyBytes is not null;

    /// <summary>Empty on success; otherwise one printable line per problem.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>The emitted assembly image; throws when <see cref="Success"/> is false.</summary>
    public byte[] AssemblyBytes => _assemblyBytes
        ?? throw new InvalidOperationException("Compilation failed; check Diagnostics.");

    public static CartCompileResult Ok(byte[] assemblyBytes) => new(assemblyBytes, Array.Empty<string>());

    public static CartCompileResult Failed(IReadOnlyList<string> diagnostics) => new(null, diagnostics);
}
