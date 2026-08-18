namespace Quarp.Core.Audio;

/// <summary>
/// The section flags of a music pattern (SPEC-8 §4: "зацикливание, флаги секций"). They are
/// what turns a flat list of 64 patterns into a song with an intro and a loop, without a
/// separate order table: the sequencer walks patterns in index order and only these three bits
/// ever make it do anything else.
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
}
