namespace Quarp.Core.Audio;

/// <summary>
/// The per-step note effects of SPEC-8 §4. Each one is evaluated once per tick, from the
/// step's own position (which tick of the step this is), so an effect is a pure function of
/// (step data, tick index inside the step) — nothing accumulates, nothing drifts, and a step
/// replayed on another machine takes exactly the same path.
///
/// <para>The numeric values are part of the on-disk SFX format (<c>docs/AUDIO-FORMAT.md</c>)
/// and are fixed for the same reason <see cref="Waveform"/>'s are.</para>
///
/// <para>Throughout, <em>speed</em> means the slot's <see cref="SfxSlot.Speed"/> — the number
/// of ticks one step lasts — and <em>t</em> is the tick's index inside the step, 0-based. A
/// slot at speed 1 gives an effect exactly one tick to happen in, which is why
/// <see cref="FadeOut"/> is silent there and <see cref="Slide"/> does not slide.</para>
/// </summary>
public enum NoteEffect : byte
{
    /// <summary>No effect: the step's note at the step's volume, held for the whole step.</summary>
    None = 0,

    /// <summary>
    /// Glide from the previous step's note to this step's note across the step:
    /// pitch = previous + (note - previous) * t / speed. The first step of an SFX has no
    /// predecessor and slides from itself, i.e. does not slide.
    /// </summary>
    Slide = 1,

    /// <summary>
    /// Wobble the pitch by up to a quarter tone at 7.5 Hz (an eight-tick integer sine table,
    /// one entry per tick). The wobble runs off the channel's age, not the step's, so it
    /// stays continuous when a step boundary passes underneath it.
    /// </summary>
    Vibrato = 2,

    /// <summary>
    /// Fall from the step's note to the bottom of the note range across the step:
    /// pitch = note * (speed - t) / speed. The "coin dropped in a well" sound.
    /// </summary>
    Drop = 3,

    /// <summary>Ramp the volume up from silence to the step's volume: amplitude * (t + 1) / speed.</summary>
    FadeIn = 4,

    /// <summary>
    /// Ramp the volume down to silence: amplitude * (speed - 1 - t) / speed. It reaches exactly
    /// zero on the step's last tick, which is what makes a chain of fading steps click-free.
    /// </summary>
    FadeOut = 5,

    /// <summary>
    /// Cycle through the four notes of the aligned group of four steps this step belongs to
    /// (steps 0-3, 4-7, ...), changing note every two ticks — a chord on one channel, the
    /// oldest trick in tracker music. Volume, waveform and everything else stay this step's;
    /// only the note is borrowed. Put the effect on all four steps of the group to hold the
    /// arpeggio for the whole group.
    /// </summary>
    Arpeggio = 6,
}
