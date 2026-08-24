using Quarp.Core.Audio;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The boot jingle, judged by the APU that will play it — not by reading the note table
/// back. <see cref="BootJingle.Start"/> on a bare <see cref="Apu"/> must actually sound
/// (non-silent samples on the first ticks) and must actually end (silence again before the
/// intro is over), because a slot that looped by accident would hold the speaker into the
/// menu, and a slot left empty would boot the console mute — both invisible to any test
/// that only inspected the bank.
/// </summary>
public class BootJingleTests
{
    private static bool TickHasSound(Apu apu)
    {
        apu.RenderTick();
        foreach (short sample in apu.Block.Samples)
        {
            if (sample != 0)
            {
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void TheJingleSoundsFromTheFirstTick()
    {
        var apu = new Apu();
        BootJingle.Start(apu);

        Assert.True(TickHasSound(apu), "the first tick of the boot jingle is silent");
    }

    [Fact]
    public void TheJingleEndsBeforeTheIntroDoes()
    {
        var apu = new Apu();
        BootJingle.Start(apu);

        int introTicks = (int)(MainMenuSession.IntroDuration * AudioBlock.TicksPerSecond);
        for (int t = 0; t < introTicks; t++)
        {
            apu.RenderTick();
        }

        // Past the intro's length the jingle must be over — a looping slot would hold the
        // channel (SfxSlot.Loops) and the menu would hum forever.
        Assert.False(TickHasSound(apu), "the jingle is still sounding after the intro's duration");
        Assert.False(apu.IsChannelBusy(0));
        Assert.False(apu.IsChannelBusy(1));
    }
}
