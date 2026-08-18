namespace Quarp.Core;

/// <summary>
/// A byte stream is not a replay this build can read: wrong magic, an unknown format version,
/// a truncated body, or a body that contradicts its own header. Every message names the field
/// and the value found, so a CLI or a shell can print it verbatim and the author knows which
/// file to blame.
/// Distinct from <see cref="ArgumentException"/> on purpose: a malformed file is data the
/// caller did not write, not a programming error the caller can fix.
/// </summary>
public sealed class ReplayFormatException : Exception
{
    public ReplayFormatException(string message)
        : base(message)
    {
    }

    public ReplayFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
