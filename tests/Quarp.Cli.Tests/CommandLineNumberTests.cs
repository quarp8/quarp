using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// Every number <c>quarp</c> reads off a command line goes through
/// <c>NumberStyles.None, CultureInfo.InvariantCulture</c> (SPEC-8 §7 bans culture-dependent
/// parsing; <c>AudioSilenceCommand</c> has said why in a comment since M4 Р4.7). Until this
/// project existed the rule held in exactly one of the nine places that read a number:
/// <c>run --break-at</c>, <c>sim --ticks</c>, <c>sim --every</c>, the four counts in
/// <c>ReplayCommands</c> and the tick of an <c>InputScript</c> entry all used the
/// <c>int.TryParse(string, out int)</c> overload, which is <c>NumberStyles.Integer</c> over the
/// <em>current</em> culture.
///
/// <para><b>What the strict style actually buys.</b> The repository sets
/// <c>InvariantGlobalization</c>, so on this machine the current culture is already the
/// invariant one and the culture half of the bug is latent rather than live — it wakes up the
/// day a cart tool is hosted somewhere that property does not hold. The style half is visible
/// today: <c>NumberStyles.Integer</c> accepts a sign and surrounding whitespace, so
/// <c>--break-at +5</c> used to be read as 5. That is the difference these tests are written
/// against, because it is the one a test can see.</para>
///
/// <para>Driven through the real executable rather than a parser lifted out of it: the parsing
/// lives in <c>Program.cs</c>'s top-level statements, and only failing arguments can be
/// exercised this way — a <c>run</c> that parsed successfully would open a window. That is not a
/// gap, it is the shape of the property: every case below must be rejected, and the
/// <em>accepted</em> case is pinned by the one argument that gets past the number and fails on
/// something else.</para>
/// </summary>
public class CommandLineNumberTests
{
    /// <summary>
    /// What <c>quarp run</c> says when it could not read <c>--break-at</c>'s value as a tick
    /// number. Distinct from every other refusal in the command, which is what makes the
    /// assertions below about the number rather than about the exit code.
    /// </summary>
    private const string NotATickNumber = "--break-at needs a tick number >= 0";

    /// <summary>What it says when the number was fine and the cartridge was missing.</summary>
    private const string NoCartridge = "--break-at needs a cartridge to run";

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]          // negative: a tick before the first one
    [InlineData("+5")]          // a sign is not part of a tick number
    [InlineData(" 5")]          // nor is padding
    [InlineData("5 ")]
    [InlineData("1,000")]       // nor a group separator, in any culture's spelling of one
    [InlineData("1.000")]
    [InlineData("0x10")]
    [InlineData("99999999999")] // more ticks than an int holds
    public void BreakAtRefusesAnythingThatIsNotARunOfDigits(string value)
    {
        CliResult result = CliProcess.Run("run", "--break-at", value);

        Assert.Equal(1, result.ExitCode);
        // Naming the message, not just the exit code: `+5` and ` 5` were accepted before this
        // change and the command then failed on the *missing cartridge* instead, one line
        // further down and with exit code 1 either way.
        Assert.Contains(NotATickNumber, result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(NoCartridge, result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void BreakAtNeedsAValueAtAll()
    {
        CliResult result = CliProcess.Run("run", "--break-at");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(NotATickNumber, result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The control that keeps the theory above from passing for the wrong reason: a plain run of
    /// digits <em>is</em> accepted, and the command then gets as far as noticing it has no
    /// cartridge to stop inside. If this ever started printing <see cref="NotATickNumber"/>,
    /// every case above would be green while <c>--break-at</c> refused every value on earth.
    /// </summary>
    [Fact]
    public void APlainNumberIsAcceptedAndTheCommandFailsOnSomethingElse()
    {
        CliResult result = CliProcess.Run("run", "--break-at", "5");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(NoCartridge, result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(NotATickNumber, result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule for the headless commands' counts. <c>sim</c> folds its number check into
    /// the argument match, so a value it cannot read comes back as "unknown argument
    /// '--ticks'" — blunt, but unambiguous, and the point here is that it is refused rather than
    /// silently read as something else. The accepted spelling is pinned by the control below.
    /// </summary>
    [Theory]
    [InlineData("--ticks", "+600")]
    [InlineData("--ticks", " 600")]
    [InlineData("--ticks", "1,000")]
    [InlineData("--ticks", "-1")]
    [InlineData("--ticks", "abc")]
    [InlineData("--every", "+20")]
    [InlineData("--every", "1,000")]
    [InlineData("--every", "abc")]
    public void SimRefusesACountItCannotReadAsDigits(string option, string value)
    {
        // A cartridge path that does not exist: the argument loop runs to completion before
        // anything is loaded, so this fails on the argument and never touches the disk.
        CliResult result = CliProcess.Run("sim", "no-such-cart", option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"unknown argument '{option}'", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void SimAcceptsPlainDigitsAndGetsAsFarAsLookingForTheCartridge()
    {
        CliResult result = CliProcess.Run("sim", "no-such-cart", "--ticks", "600", "--every", "20");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cartridge not found", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown argument", result.StdErr, StringComparison.Ordinal);
    }
}
