using Quarp.Core;
using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp audio silence --ticks N</c>: the digest CI compares every run's PCM against, and the
/// argument handling that decides whether it prints one at all (M4 Р4.7).
///
/// <para><b>Why this is the sharpest hole M4 closed.</b> Nothing called
/// <see cref="AudioSilenceCommand.Digest"/> outside the command itself, and the one test that
/// asserted those three numbers —
/// <c>Quarp.CartKit.Tests.AudioGoldenTests.SilenceIsWhatTheWorkflowThinksItIs</c> — recomputed
/// them with a loop of its own. So the digest the workflow actually uses was unguarded, and the
/// failure that hid there is silent in both directions: change the fold to
/// <c>i &lt; ticks - 1</c> and <c>Digest(0)</c> is still <see cref="FrameHash.Empty"/> (the
/// workflow's rot-proof anchor still passes), the answer still moves with <c>--ticks</c> (the
/// workflow's second guard still passes), and both "the cartridge is not mute" checks are now
/// comparing a real digest against a number no run can produce — which is a comparison that
/// passes forever, including on the day the banks stop reaching the console.</para>
///
/// <para>Hence the theory below, on the values themselves rather than on a property of them:
/// off-by-one in the fold is precisely the mutation a property-shaped test cannot see.</para>
/// </summary>
public class AudioSilenceCommandTests
{
    // The three numbers .github/workflows/ci.yml derives from SIM_TICKS and GOLDEN_TICKS and
    // compares the idle run and the golden replay against. They are anchors: moving one is
    // never a fix (docs/PLAYBOOK.md §4).
    private const string ZeroTickSilence = "cbf29ce484222325";
    private const string SimSilence = "54738d7161a01b25";
    private const string GoldenSilence = "220acbc2c817fb25";

    [Theory]
    [InlineData(0, ZeroTickSilence)]
    [InlineData(600, SimSilence)]        // SIM_TICKS
    [InlineData(3000, GoldenSilence)]    // GOLDEN_TICKS
    public void TheSilentDigestIsWhatCiComparesEveryRunAgainst(int ticks, string expected) =>
        Assert.Equal(expected, FrameHash.Format(AudioSilenceCommand.Digest(ticks)));

    /// <summary>
    /// The structural half of the same guard, stated so it cannot be satisfied by a fold that is
    /// one tick short: one tick of silence is already something, and zero ticks is the seed. An
    /// off-by-one turns the first of these into the second.
    /// </summary>
    [Fact]
    public void OneTickOfSilenceIsAlreadyFoldedIn()
    {
        Assert.Equal(FrameHash.Empty, AudioSilenceCommand.Digest(0));
        Assert.NotEqual(FrameHash.Empty, AudioSilenceCommand.Digest(1));
        Assert.NotEqual(AudioSilenceCommand.Digest(1), AudioSilenceCommand.Digest(2));
    }

    /// <summary>
    /// A negative tick count is a caller's bug, not a run of length zero. Answering it with
    /// <see cref="FrameHash.Empty"/> would be indistinguishable from a correct zero-tick answer.
    /// </summary>
    [Fact]
    public void ANegativeTickCountIsRejectedRatherThanTreatedAsZero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioSilenceCommand.Digest(-1));

    // --- the command around the digest ---

    [Fact]
    public void AValidRunPrintsTheLabelledAudioLineAndNothingElse()
    {
        (int code, string output, string error) = Invoke("silence", "--ticks", "600");

        Assert.Equal(0, code);
        Assert.Equal($"audio {SimSilence}", output.ReplaceLineEndings("\n").TrimEnd('\n'));
        Assert.Equal(string.Empty, error);
        // Labelled, never bare: across this whole tool exactly one line matches ^[0-9a-f]{16}$
        // and it means a framebuffer hash. CI greps for that shape.
        Assert.StartsWith(Checkpoint.AudioPrefix + " ", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything the command refuses, with the message it refuses it in. The point is not that
    /// each spelling is wrong - it is that every one of them ends in exit code 1 and no digest on
    /// stdout, because a caller that got a confidently printed number for arguments it did not
    /// write would compare a real run against it and call the result green.
    /// </summary>
    [Theory]
    // --ticks with no value at all.
    [InlineData(new[] { "silence", "--ticks" }, "--ticks needs a value")]
    // Not a number.
    [InlineData(new[] { "silence", "--ticks", "abc" }, "non-negative whole number")]
    [InlineData(new[] { "silence", "--ticks", "" }, "non-negative whole number")]
    // Negative: rejected while parsing rather than after, so the message names the value.
    [InlineData(new[] { "silence", "--ticks", "-5" }, "non-negative whole number")]
    // NumberStyles.None, invariant culture (SPEC-8 §7): no sign, no padding, no separators.
    [InlineData(new[] { "silence", "--ticks", "+600" }, "non-negative whole number")]
    [InlineData(new[] { "silence", "--ticks", " 600" }, "non-negative whole number")]
    [InlineData(new[] { "silence", "--ticks", "1,000" }, "non-negative whole number")]
    [InlineData(new[] { "silence", "--ticks", "1 000" }, "non-negative whole number")]
    // Larger than int: not a tick count anyone can run, and silently wrapping would be worse.
    [InlineData(new[] { "silence", "--ticks", "99999999999" }, "non-negative whole number")]
    // --ticks is required and deliberately has no default.
    [InlineData(new[] { "silence" }, "--ticks N is required")]
    [InlineData(new[] { "silence", "--every", "20" }, "unknown argument '--every'")]
    // The group's dispatcher hands anything that is not `silence` to `audio build`; reaching
    // Invoke with the wrong subcommand is a programming error, and it says so.
    [InlineData(new[] { "build" }, "expected the 'silence' subcommand")]
    [InlineData(new string[0], "expected the 'silence' subcommand")]
    public void EveryBadSpellingFailsWithoutPrintingADigest(string[] args, string expectedMessage)
    {
        (int code, string output, string error) = Invoke(args);

        Assert.Equal(1, code);
        Assert.Contains(expectedMessage, error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// Runs the command with <see cref="Console"/> pointed at buffers. Process-wide state, which
    /// is why this assembly does not run its tests in parallel (see <c>CliProcess</c>); the
    /// alternative, a child process per assertion, would cost a second each to test a pure
    /// argument table.
    /// </summary>
    private static (int Code, string Out, string Error) Invoke(params string[] args)
    {
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            int code = AudioSilenceCommand.Invoke(args);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
