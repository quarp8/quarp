namespace Quarp.Core.Audio;

/// <summary>
/// The section flags that decide what happens when a pattern ends. In version 1 they live on the
/// pattern itself (SPEC-8 §4: "зацикливание, флаги секций") and turn a flat list of 64 patterns
/// into a song with an intro and a loop; in version 2 the very same byte, bit for bit, lives on
/// an <see cref="MusicOrderEntry"/> instead, which is what lets one sequencer walk both versions
/// (ADR-040). <see cref="Jump"/> is the one bit version 1 never had.
/// </summary>
[Flags]
public enum MusicFlags : byte
{
    /// <summary>Nothing special: when this pattern ends, the next index plays.</summary>
    None = 0,

    /// <summary>
    /// The loop comes back here. The sequencer remembers the most recent pattern carrying this
    /// flag; if it has passed none, it remembers the pattern <c>Music()</c> started on.
    /// </summary>
    LoopStart = 1,

    /// <summary>Jump back to the remembered <see cref="LoopStart"/> when this pattern ends.</summary>
    LoopEnd = 2,

    /// <summary>Stop the music when this pattern ends. Checked before <see cref="LoopEnd"/>.</summary>
    Stop = 4,

    /// <summary>
    /// Version 2 only: continue at the order entry named by <see cref="MusicOrderEntry.Target"/>.
    /// Checked after <see cref="Stop"/> and before <see cref="LoopEnd"/>, so an entry carrying
    /// both a jump and a loop-back jumps — the explicit target beats the remembered one, which is
    /// the reading a composer who wrote a target almost certainly meant. Version 1 banks can
    /// never carry this bit: bits 3-7 of a v1 flag byte are rejected by the loader.
    /// </summary>
    Jump = 8,
}
