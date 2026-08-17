namespace Quarp.CartKit;

/// <summary>
/// The single user-facing failure type of the cartridge pipeline: bad manifest, missing
/// sources, oversized code, malformed PNG, wrong asset sizes. The message is meant to be
/// shown to the cartridge author as-is, so it always names the offending file and what
/// to fix.
/// </summary>
public sealed class CartLoadException : Exception
{
    public CartLoadException(string message)
        : base(message)
    {
    }

    public CartLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
