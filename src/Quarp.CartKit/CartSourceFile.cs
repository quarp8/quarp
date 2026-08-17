namespace Quarp.CartKit;

/// <summary>
/// One cartridge source file: cart-relative path with forward slashes (e.g. "src/main.cs"),
/// used as the file name in compiler diagnostics and embedded-PDB stack traces, plus its text.
/// </summary>
public sealed record CartSourceFile(string RelativePath, string Text);
