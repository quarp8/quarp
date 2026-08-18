using Quarp.Core;
using Quarp.Core.Audio;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Golden-master tests of the <em>sound</em> of carts/snake, pinned next to the frame goldens
/// they travel with: the idle 600-tick run and the committed golden replay, each asserted on
/// both of its hashes at once (docs/PLAYBOOK.md §4 lists all four as anchors).
///
/// <para><b>Why this file exists.</b> Until M4 the two PCM numbers lived in
/// <c>carts/snake/replays/README.md</c> and in the milestone reports, and nothing but
/// <c>.github/workflows/ci.yml</c> ever compared anything against them — which means the
/// earliest a broken APU or a bank that stopped reaching the console could be noticed was
/// after a push, on a runner, in a job whose subject is something else. The frame half of the
/// same pair has been a unit test since M1
/// (<c>Quarp.CartKit.Tests.SnakeCartTests.SixHundredIdleTicksAreBitIdenticalAcrossRuns</c>).
/// This is the missing half.</para>
///
/// <para><b>The digest is computed the way the CLI computes it, on purpose.</b> A test that
/// guarded a number nobody prints would guard nothing: the value these tests assert has to be
/// the value <c>quarp sim</c> and <c>quarp replay play</c> put on their <c>audio &lt;hash&gt;</c>
/// line and CI compares across architectures. So the loops below are the loops in
/// <c>Program.RunSim</c> and <c>ReplayCommands.Play</c> — seed 0, persistent memory zeroed,
/// <c>save.dat</c> untouched, the digest seeded with <see cref="FrameHash.Empty"/> before the
/// first tick and folded once per tick over the console's block. Init is tick 0 and produces no
/// block, so the digest covers ticks 1..N, exactly the ticks the frames come from.</para>
///
/// <para><b>Negative control, kept as a test rather than performed once.</b>
/// <see cref="AMuteCartridgeKeepsTheFrameGoldenAndBreaksTheAudioGolden"/> and its replay twin
/// run the same simulation with the audio banks withheld — the concrete failure "the banks
/// never reached the console" — and assert that the frame goldens survive it untouched while
/// the audio goldens do not. If the audio assertions above could ever pass on a mute console,
/// those two tests fail and say which half went wrong.</para>
///
/// <para><b>Why this file lives in CartKit's test project.</b> The goldens are properties of a
/// whole run — Roslyn compiles carts/snake, the collectible load context hosts it, the console
/// ticks it — so the assertions need <c>Quarp.CartKit</c> as well as <c>Quarp.Core</c>. This
/// project already referenced both, which is why nothing had to be rewired to add these tests;
/// its sibling <c>Quarp.Core.Tests</c> references only <c>Quarp.Api</c> and <c>Quarp.Core</c>
/// and could not have hosted them.</para>
/// </summary>
public class AudioGoldenTests
{
    // --- the anchors (docs/PLAYBOOK.md §4). Moving one of these is never a fix. ---

    /// <summary>Ticks of the idle smoke run: <c>quarp sim carts/snake --ticks 600</c>, SIM_TICKS in CI.</summary>
    private const int SimTicks = 600;

    /// <summary>Ticks the committed golden replay covers: GOLDEN_TICKS in CI.</summary>
    private const int GoldenTicks = 3000;

    private const string SimFrameGolden = "37c481f3e17fab02";
    private const string SimAudioGolden = "f373b5bfd09755b9";
    private const string ReplayFrameGolden = "24a6eb974ff922e4";
    private const string ReplayAudioGolden = "f93bf5cc36b83cba";

    /// <summary>
    /// The digest of <see cref="SimTicks"/> silent blocks — what
    /// <c>quarp audio silence --ticks 600</c> prints and what CI compares the idle run's PCM
    /// against.
    /// </summary>
    private const string SimSilence = "54738d7161a01b25";

    /// <summary>The same for <see cref="GoldenTicks"/>, the golden replay's length.</summary>
    private const string GoldenSilence = "220acbc2c817fb25";

    /// <summary>
    /// Silence over zero ticks: the FNV-1a 64 offset basis, i.e. <see cref="FrameHash.Empty"/>
    /// formatted. Unlike the two above it is a property of the hash function rather than of a
    /// tick count, so it cannot go stale when GOLDEN_TICKS or SIM_TICKS move — which is exactly
    /// why the workflow uses it to check that <c>quarp audio silence</c> starts its fold from
    /// the right seed and folds nothing at all for zero ticks.
    /// </summary>
    private const string ZeroTickSilence = "cbf29ce484222325";

    // --- locating and loading the repository's own cartridge, once ---

    private static readonly object CartLock = new();
    private static CartData? _snakeData;
    private static byte[]? _snakeAssembly;

    private static string RepoRoot()
    {
        // Walk up from the test bin folder to the repo root, the way SnakeCartTests does.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "carts", "snake", "manifest.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/snake not found above the test directory");
    }

    private static string GoldenReplayFile() =>
        Path.Combine(RepoRoot(), "carts", "snake", "replays", "golden.qrpr");

    /// <summary>
    /// The cart's data and its compiled assembly, loaded and compiled once for the whole class.
    /// Caching is safe because both are inputs, never state: the console copies the sheet, map
    /// and flags in defensively (<see cref="VirtualConsole.LoadAssets"/>) and the APU copies the
    /// bank, so nothing a run does can reach back into them — and Roslyn is far too slow to
    /// invite eight times into one test class.
    /// </summary>
    private static (CartData Data, byte[] Assembly) Snake()
    {
        lock (CartLock)
        {
            CartData? data = _snakeData;
            byte[]? assembly = _snakeAssembly;
            if (data is null || assembly is null)
            {
                data = CartSource.Load(Path.Combine(RepoRoot(), "carts", "snake"));
                CartCompileResult result = CartCompiler.Compile(data);
                Assert.True(result.Success, string.Join("\n", result.Diagnostics));
                assembly = result.AssemblyBytes;
                _snakeData = data;
                _snakeAssembly = assembly;
            }
            return (data, assembly);
        }
    }

    // --- the two runs, each reproducing exactly what the CLI prints ---

    /// <summary>
    /// <c>quarp sim carts/snake --ticks N</c>, hashes and all. With
    /// <paramref name="withAudioBanks"/> false the sfx and music payloads are withheld and
    /// everything else is identical — the "bank never arrived" failure, used as the negative
    /// control below.
    /// </summary>
    private static (string Frame, string Audio) Sim(int ticks, bool withAudioBanks = true)
    {
        (CartData data, byte[] assembly) = Snake();
        using var host = CartHost.Load(assembly);
        var console = new VirtualConsole(
            ConsoleProfile.Profile8,
            data.Gfx,
            data.Map,
            data.Flags,
            withAudioBanks ? data.Sfx : null,
            withAudioBanks ? data.Music : null);
        console.AttachCart(host.Cartridge);
        ulong audio = FrameHash.Empty;
        for (int i = 0; i < ticks; i++)
        {
            console.Tick(default);
            audio = FrameHash.Combine(audio, console.AudioBlock);
        }
        return (FrameHash.Of(console.Framebuffer), FrameHash.Format(audio));
    }

    /// <summary>
    /// <c>quarp replay play carts/snake/replays/golden.qrpr --cart carts/snake</c>: the file's
    /// own header drives the session (its seed and persistent snapshot are inputs of the
    /// simulation being reproduced, REPLAY-FORMAT §2), one tick per step so the digest sees
    /// every block, exactly as <c>ReplayCommands.Play</c> does it.
    /// </summary>
    private static (string Frame, string Audio, int Ticks) PlayGolden(bool withAudioBanks = true)
    {
        (CartData data, byte[] assembly) = Snake();
        ReplayLog log;
        ReplayHeader header;
        using (FileStream stream = File.OpenRead(GoldenReplayFile()))
        {
            log = ReplayLog.ReadFrom(stream, out header);
        }
        using var host = CartHost.Load(assembly);
        var machine = new TimeMachine(
            ConsoleProfile.Profile8,
            host.Cartridge,
            header,
            log,
            data.Gfx,
            data.Map,
            data.Flags,
            withAudioBanks ? data.Sfx : null,
            withAudioBanks ? data.Music : null);
        machine.Boot();
        ulong audio = FrameHash.Empty;
        int played = 0;
        while (played < log.TickCount && machine.ReplayForward(1) != 0)
        {
            played++;
            audio = FrameHash.Combine(audio, machine.Console.AudioBlock);
        }
        return (FrameHash.Of(machine.Framebuffer), FrameHash.Format(audio), played);
    }

    // --- the goldens ---

    [Fact]
    public void IdleSimReproducesBothGoldens()
    {
        (string frame, string audio) = Sim(SimTicks);
        // Both on the same run and in one test: the pair is what a checkpoint line carries and
        // what CI compares, and asserting them apart would allow a change that swaps which run
        // each number came from.
        Assert.Equal(SimFrameGolden, frame);
        Assert.Equal(SimAudioGolden, audio);
    }

    [Fact]
    public void TheGoldenReplayReproducesBothGoldens()
    {
        (string frame, string audio, int ticks) = PlayGolden();
        // The length is asserted too: a truncated golden would still agree with itself and
        // would still produce *some* pair of hashes, just not these ones — and this line says
        // which of the two things went wrong.
        Assert.Equal(GoldenTicks, ticks);
        Assert.Equal(ReplayFrameGolden, frame);
        Assert.Equal(ReplayAudioGolden, audio);
    }

    [Fact]
    public void TheTwoRunsAreDifferentRunsAndDisagreeOnBothHashes()
    {
        // Non-vacuity for the pair above: if the idle run and the golden replay ever produced
        // the same hashes, one of the two tests would be guarding nothing while looking green.
        Assert.NotEqual(SimFrameGolden, ReplayFrameGolden);
        Assert.NotEqual(SimAudioGolden, ReplayAudioGolden);
    }

    [Fact]
    public void BothRunsAreBitIdenticalWhenRepeated()
    {
        // Determinism inside one process, which is the precondition for the cross-architecture
        // claim: if a single machine cannot repeat itself, a mismatch between two machines
        // would be misdiagnosed as an architecture problem (the same order the CI job uses).
        Assert.Equal(Sim(SimTicks), Sim(SimTicks));
        Assert.Equal(PlayGolden(), PlayGolden());
    }

    // --- the negative controls: mute the cartridge and watch which golden notices ---

    [Fact]
    public void AMuteCartridgeKeepsTheFrameGoldenAndBreaksTheAudioGolden()
    {
        // The concrete failure this file is here to catch, performed on purpose: the sfx and
        // music payloads never reach the console. Everything else — code, sheet, map, flags,
        // seed, input — is untouched.
        (string frame, string audio) = Sim(SimTicks, withAudioBanks: false);

        // The frame golden does not move by so much as a pixel, and that is the point: profile 8
        // gives a cartridge no way to read the chip back (IConsoleApi has Sfx and Music, both
        // returning void, and no getters), so silence is invisible to every frame hash in the
        // suite. A frame-only golden master would have stayed green through this.
        Assert.Equal(SimFrameGolden, frame);

        // The audio golden does move, and lands exactly on the digest of 600 silent blocks —
        // which is the value CI computes with `quarp audio silence --ticks 600` and refuses to
        // see in a real run.
        Assert.NotEqual(SimAudioGolden, audio);
        Assert.Equal(SimSilence, audio);
    }

    [Fact]
    public void AMuteGoldenReplayKeepsItsFrameGoldenAndBreaksItsAudioGolden()
    {
        (string frame, string audio, int ticks) = PlayGolden(withAudioBanks: false);
        Assert.Equal(GoldenTicks, ticks);
        Assert.Equal(ReplayFrameGolden, frame);
        Assert.NotEqual(ReplayAudioGolden, audio);
        Assert.Equal(GoldenSilence, audio);
    }

    // --- the silence digests themselves, which CI now computes instead of quoting ---

    /// <summary>
    /// The three numbers CI derives with <c>quarp audio silence --ticks N</c>, derived a second
    /// time from the chip alone.
    ///
    /// <para><b>This is a cross-check, not the primary guard.</b> Since M4 the command's own
    /// <c>AudioSilenceCommand.Digest</c> is asserted against these same three values in
    /// <c>Quarp.Cli.Tests.AudioSilenceCommandTests</c> — that is the test that fails if the
    /// <em>command</em> breaks, and before it existed nothing called <c>Digest</c> at all. What
    /// this one adds is independence: <c>Quarp.CartKit.Tests</c> does not reference the CLI, so
    /// the loop below reaches the same three numbers through <see cref="Apu"/> and
    /// <see cref="FrameHash"/> with no code in common with the command beyond the chip itself.
    /// A change to <c>SamplesPerTick</c> or to the mixer's idle path therefore shows up here
    /// even if the command were deleted tomorrow, and a disagreement between the two files says
    /// the command stopped folding what a run folds.</para>
    /// </summary>
    [Theory]
    [InlineData(0, ZeroTickSilence)]
    [InlineData(SimTicks, SimSilence)]
    [InlineData(GoldenTicks, GoldenSilence)]
    public void SilenceIsWhatTheWorkflowThinksItIs(int ticks, string expected)
    {
        var apu = new Apu();
        ulong viaChip = FrameHash.Empty;
        for (int i = 0; i < ticks; i++)
        {
            apu.RenderTick();
            viaChip = FrameHash.Combine(viaChip, apu.Block);
        }
        Assert.Equal(expected, FrameHash.Format(viaChip));

        // And the idle mixer really does produce the same bytes an untouched block holds. It
        // takes a different branch to get there (Render() short-circuits on AllIdle instead of
        // running the mixer), so the equality is a claim about the chip, not a tautology — and
        // it is the one claim in this file that the CLI's own test cannot make, because the
        // command has no reason to construct a bare AudioBlock.
        var untouched = new AudioBlock();
        ulong viaBlock = FrameHash.Empty;
        for (int i = 0; i < ticks; i++)
        {
            viaBlock = FrameHash.Combine(viaBlock, untouched);
        }
        Assert.Equal(expected, FrameHash.Format(viaBlock));
    }

    [Fact]
    public void TheSilenceDigestDependsOnTheTickCount()
    {
        // The property CI leans on when it derives two values instead of quoting two: a digest
        // that ignored the tick count would hand both mute checks the same number, and both
        // would then pass forever. Zero ticks is the seed and is deliberately in the list.
        Assert.NotEqual(SimSilence, GoldenSilence);
        Assert.NotEqual(ZeroTickSilence, SimSilence);
        Assert.NotEqual(ZeroTickSilence, GoldenSilence);
        Assert.Equal(ZeroTickSilence, FrameHash.Format(FrameHash.Empty));
    }

    [Fact]
    public void NeitherGoldenRunIsSilent()
    {
        // The assertion CI makes, made here too so it fails in seconds instead of after a push.
        // It is deliberately spelled out rather than left implicit in the equality tests above:
        // "the audio golden is not the silence digest" is the property that has meaning, and it
        // keeps having meaning if someone ever re-pins the goldens.
        Assert.NotEqual(SimSilence, SimAudioGolden);
        Assert.NotEqual(GoldenSilence, ReplayAudioGolden);
    }
}
