namespace Quarp.Core.Tests;

/// <summary>
/// FNV-1a 64-bit — the same hash `quarp sim` prints, reimplemented here so golden
/// values in tests and CLI output stay comparable.
/// </summary>
public static class Fnv
{
    public static ulong Hash(byte[] data)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < data.Length; i++)
        {
            hash = unchecked((hash ^ data[i]) * 1099511628211UL);
        }
        return hash;
    }
}
