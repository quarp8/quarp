using Quarp.Core.Audio;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The console's greeting — the short rise the boot intro plays, spoken by the same APU
/// every cartridge speaks through (owner's decision on the boot-menu order: with sound).
/// The niche precedent is looser here than usual: none of the three reference consoles
/// documents its boot audio in a primary source, so the jingle leans on the villain everyone
/// actually remembers — a fantasy console announces itself in its own chip voice — and stays
/// under a second so the author who boots five times an hour never learns to hate it.
///
/// <para>This is data about notes, not a device: it builds an <see cref="AudioBank"/> and
/// hands it over. The shell owns the <see cref="Apu"/> that renders it and the speaker it
/// comes out of; the tests own that the slot really sounds (a non-silent first tick) without
/// either.</para>
///
/// <para><b>The tune.</b> A C-major climb over two octaves — the four logo tiles popping in
/// are four steps of it — on the 25% pulse (the brightest of the square family), with a soft
/// low C on the triangle underneath, the two-voice trick every chip tune opens with. Note 0
/// is C2 (<see cref="NoteTable"/>), so C3 is 12 and the climb tops out on the C5 the wordmark
/// lands on.</para>
/// </summary>
public static class BootJingle
{
    /// <summary>The sfx slot the jingle lives in.</summary>
    public const int SfxId = 0;

    /// <summary>The bass drone's slot.</summary>
    public const int BassId = 1;

    /// <summary>
    /// Ticks per step: 10 steps/second. Eight steps land the last note at 0.8 s — just as
    /// the intro's wordmark wipe finishes assembling, so the ear and the eye arrive together.
    /// </summary>
    public const int StepTicks = 6;

    /// <summary>The climb: C3 E3 G3 C4 E4 G4 C5, then C5 held. Two octaves, eight steps, 2/3 of a second.</summary>
    private static readonly byte[] Climb = { 12, 16, 19, 24, 28, 31, 36, 36 };

    /// <summary>The bank the boot Apu loads: the climb in slot 0, the bass root in slot 1.</summary>
    public static AudioBank Build()
    {
        var bank = new AudioBank();

        SfxSlot lead = bank.GetSfx(SfxId);
        lead.Speed = StepTicks;
        lead.Length = Climb.Length;
        for (int i = 0; i < Climb.Length; i++)
        {
            // The last two steps ease off instead of stopping dead — a chip's release envelope.
            int volume = i < Climb.Length - 2 ? 6 : 4;
            lead[i] = new SfxStep(Climb[i], Waveform.Pulse25, volume);
        }

        SfxSlot bass = bank.GetSfx(BassId);
        bass.Speed = StepTicks * 2;
        bass.Length = 4;
        bass[0] = new SfxStep(12, Waveform.Triangle, 5);    // C3 under the climb
        bass[1] = new SfxStep(12, Waveform.Triangle, 4);
        bass[2] = new SfxStep(24, Waveform.Triangle, 4);    // C4 as the lead tops out
        bass[3] = new SfxStep(24, Waveform.Triangle, 2);
        return bank;
    }

    /// <summary>Starts both voices on their own channels; the caller renders ticks and owns the speaker.</summary>
    public static void Start(Apu apu)
    {
        ArgumentNullException.ThrowIfNull(apu);
        apu.LoadBank(Build());
        apu.PlaySfx(SfxId, 0);
        apu.PlaySfx(BassId, 1);
    }
}
