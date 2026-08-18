namespace Quarp.CartKit;

/// <summary>
/// One cartridge source file: cart-relative path with forward slashes (e.g. "src/main.cs"),
/// used as the file name in compiler diagnostics, plus its text, plus — for a folder cartridge
/// only — the absolute path of the file the text came from.
///
/// <para><see cref="RelativePath"/> and <see cref="Text"/> are what the cartridge <em>is</em>:
/// they are what <see cref="CartIdentity"/> hashes and what <see cref="CodeBudget"/> measures.
/// <see cref="DiskPath"/> is deliberately none of that. It is machine-local — two clones of the
/// same game have different ones — so nothing that decides identity, size or behaviour may read
/// it. It is null for a <c>.quarp8</c> package (which has no files on disk) and null for sources
/// built in memory, and its single job is debugging (M4 Р1): it lets
/// <see cref="CartCompiler"/> record a document in the PDB that the debugger can actually open
/// and checksum, which is the whole contract a breakpoint binds on.</para>
/// </summary>
public sealed record CartSourceFile(string RelativePath, string Text, string? DiskPath = null);
