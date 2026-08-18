namespace Quarp.Core.Audio;

/// <summary>
/// One of the 64 sound effects: up to 32 steps, a speed, and an optional loop (SPEC-8 §4).
/// This is the only unit of sound in QUARP-8 — music is nothing but SFX slots triggered by the
/// pattern sequencer, so there is one synthesis engine, not two.
///
/// <para><b>Speed is measured in ticks, not in samples or seconds</b>, and that single choice
/// is what keeps the chip honest. Every sequencer decision — advance a step, start a note, end
/// an SFX, turn a pattern over — lands on a tick boundary, so the 800 samples of a tick are
/// generated from one frozen set of registers, the way real hardware renders between two
/// register writes. Nothing is scheduled at sample resolution, no fractional step counter is
/// carried across a block boundary, and the sample loop contains no branch that depends on
/// time. Effects still get 60 updates a second, finer than any tracker's tempo grid.</para>
///
/// <para>At the default speed of 8 a step lasts 8/60 s ≈ 133 ms and a full 32-step slot runs
/// 4.27 s; speed 1 is one tick per step, the fastest the chip can go.</para>
///
/// <para>The field ranges here are the same ones <c>sfx.bin</c> enforces (docs/AUDIO-FORMAT.md
/// §2), stated twice on purpose: the loader rejects a bad file with a message naming the slot,
/// and these clamps guarantee that whatever survives to the tick path is in range, so the
/// synthesizer never checks anything 60 times a second.</para>
/// </summary>
public sealed class SfxSlot
{
    /// <summary>Steps a slot can hold: 32 (SPEC-8 §4).</summary>
    public const int StepCount = 32;

    /// <summary>Fastest playback: one tick per step.</summary>
    public const int MinSpeed = 1;

    /// <summary>Slowest playback: 255 ticks (4.25 s) per step. Fits a byte on disk.</summary>
    public const int MaxSpeed = 255;

    /// <summary>Speed of a slot nobody configured: 8 ticks per step ≈ 133 ms, a comfortable tracker row.</summary>
    public const int DefaultSpeed = 8;

    private readonly SfxStep[] _steps = new SfxStep[StepCount];
    private int _length;
    private int _speed = DefaultSpeed;
    private int _loopStart;
    private int _loopEnd;

    /// <summary>
    /// Steps actually played, 0-32. <b>Zero means the slot is empty</b> and <c>Sfx(id)</c> on it
    /// does nothing at all — not "plays 32 rests", which would hold a channel hostage for four
    /// seconds of silence. A slot has a length of its own rather than always running to step 31
    /// because most effects are four or five steps long, and padding them with rests would make
    /// every beep occupy its channel eight times longer than it sounds.
    /// </summary>
    public int Length
    {
        get => _length;
        set => _length = Math.Clamp(value, 0, StepCount);
    }

    /// <summary>Ticks each step lasts, clamped to <see cref="MinSpeed"/>..<see cref="MaxSpeed"/>.</summary>
    public int Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    /// <summary>Step the loop jumps back to, 0-31. Meaningless while <see cref="Loops"/> is false.</summary>
    public int LoopStart
    {
        get => _loopStart;
        set => _loopStart = Math.Clamp(value, 0, StepCount - 1);
    }

    /// <summary>
    /// One past the last step of the loop, 0-32; <b>0 means the slot does not loop</b>, matching
    /// the half-open range the file format stores.
    /// </summary>
    public int LoopEnd
    {
        get => _loopEnd;
        set => _loopEnd = Math.Clamp(value, 0, StepCount);
    }

    /// <summary>
    /// True when the loop is armed: a non-zero <see cref="LoopEnd"/> lying past
    /// <see cref="LoopStart"/>. A looping slot never ends by itself — it plays until something
    /// takes its channel, or until the music pattern that started it turns over.
    /// </summary>
    public bool Loops => _loopEnd > _loopStart && _loopEnd > 0;

    /// <summary>True when the slot holds nothing to play; <c>Sfx</c> on it is a no-op.</summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// The slot's steps. Reading past the end gives <see cref="SfxStep.Rest"/> and writing past
    /// the end is ignored, so a loader that miscounts produces silence rather than a crash.
    /// Steps at or beyond <see cref="Length"/> are stored but never played.
    /// </summary>
    public SfxStep this[int step]
    {
        get => (uint)step < StepCount ? _steps[step] : SfxStep.Rest;
        set
        {
            if ((uint)step < StepCount)
            {
                _steps[step] = value;
            }
        }
    }

    /// <summary>
    /// Ticks one pass through the slot takes, loops ignored. This is the ruler the music
    /// sequencer measures a pattern with (<see cref="Apu"/>).
    /// </summary>
    public int LengthTicks => _length * _speed;

    /// <summary>True when the slot plays but every step it plays is a rest.</summary>
    public bool IsSilent
    {
        get
        {
            for (int i = 0; i < _length; i++)
            {
                if (!_steps[i].IsRest)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Back to an empty slot: no steps, default speed, no loop.</summary>
    public void Clear()
    {
        Array.Clear(_steps);
        _length = 0;
        _speed = DefaultSpeed;
        _loopStart = 0;
        _loopEnd = 0;
    }

    /// <summary>Copies another slot's steps and settings into this one, keeping this instance.</summary>
    public void CopyFrom(SfxSlot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other._steps.CopyTo(_steps, 0);
        _length = other._length;
        _speed = other._speed;
        _loopStart = other._loopStart;
        _loopEnd = other._loopEnd;
    }
}
