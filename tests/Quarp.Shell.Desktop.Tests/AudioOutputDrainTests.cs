using Quarp.Core.Audio;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The device half of the transition's audio story: <see cref="AudioOutput.Drain"/> must
/// leave the hardware queue empty, and the output must stay usable afterwards. The policy
/// half — that the shell calls Drain on the game → library transition at all — is pinned by
/// <see cref="ModeTransitionTests"/>, headlessly.
///
/// <para>These asserts run only where a sound device exists (a developer's machine); on an
/// audio-less runner <see cref="AudioOutput.IsAvailable"/> is false by design and the test
/// passes vacuously — the same graceful degradation the shell itself promises. That means the
/// negative control for this file is only demonstrable on hardware, which the M9 report
/// states rather than hides.</para>
/// </summary>
public class AudioOutputDrainTests
{
    [Fact]
    public void DrainEmptiesTheDeviceQueueAndTheOutputSurvivesIt()
    {
        using var output = new AudioOutput();
        if (!output.IsAvailable)
        {
            return;     // No device (CI): nothing to drain and nothing to leak.
        }

        var block = new AudioBlock();
        output.Submit(block);
        output.Submit(block);
        Assert.True(output.Queued > 0, "submitting two blocks left nothing queued — the probe is broken");

        output.Drain();

        Assert.Equal(0, output.Queued);

        // Usable after: the next game's first frame pads and restarts the source, so EndFrame
        // must queue silence rather than throw on a stopped instance.
        output.EndFrame();
        Assert.True(output.Queued > 0);
    }
}
