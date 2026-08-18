namespace Quarp.Core.Audio;

/// <summary>
/// The six voices of the QUARP-8 sound chip (SPEC-8 §4). The numeric values are part of the
/// on-disk SFX format (see <c>docs/AUDIO-FORMAT.md</c>) and are therefore fixed: adding a
/// waveform means appending, never renumbering.
///
/// <para>Every one of them is generated straight from the phase accumulator with no
/// band-limiting, so high notes alias — exactly as they did on the hardware this profile
/// imitates. That is a sound, not a bug: an anti-aliased pulse would need a filter, a filter
/// needs history, and history in floating point is precisely what cannot be in this chain.</para>
/// </summary>
public enum Waveform : byte
{
    /// <summary>12.5% duty pulse — thin and nasal, the classic "lead" voice.</summary>
    Pulse12 = 0,

    /// <summary>25% duty pulse — the middle pulse tone.</summary>
    Pulse25 = 1,

    /// <summary>50% duty pulse: a square wave, the loudest and roundest of the three.</summary>
    Pulse50 = 2,

    /// <summary>Triangle — soft, flute-like; the traditional bass voice.</summary>
    Triangle = 3,

    /// <summary>Sawtooth — buzzy and bright, richest in harmonics.</summary>
    Saw = 4,

    /// <summary>
    /// Noise from a 15-bit LFSR clocked at the note's frequency: percussion, explosions, wind.
    /// The register is simulation state living in the channel, never a <c>Random</c>
    /// (SPEC-8 §7.4 — a sound built on a real random source would make replays diverge).
    /// </summary>
    Noise = 5,
}
