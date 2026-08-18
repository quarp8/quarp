using System.Diagnostics;
using System.Text;
using Xunit;

// The console-capturing tests in AudioSilenceCommandTests replace Console.Out and Console.Error,
// which are process-wide: a test running beside them would have its output swallowed or, worse,
// would land in the buffer being asserted on. This assembly is small and its slowest tests are
// child processes that spend their time in Roslyn anyway, so serialising it costs a few seconds
// and removes the whole class of problem. Nothing here is a shared-state test by design.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Quarp.Cli.Tests;

/// <summary>What one run of the tool said and how it ended.</summary>
internal readonly record struct CliResult(int ExitCode, string StdOut, string StdErr)
{
    /// <summary>Both streams, labelled — what an assertion message should show when it fails.</summary>
    public override string ToString() =>
        $"exit {ExitCode}\n--- stdout ---\n{StdOut}--- stderr ---\n{StdErr}";

    /// <summary>The lines of stdout with the trailing blank one dropped, in order.</summary>
    public string[] OutLines() => StdOut.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}

/// <summary>
/// Runs the real <c>quarp</c> executable as a child process and collects what it printed.
///
/// <para><b>Why a process and not a method call.</b> Everything <c>quarp</c> does with its
/// command line lives in <c>Program.cs</c>'s top-level statements: the <c>new</c> command, the
/// <c>--break-at</c> parser, the dispatch table. Top-level statements compile into a private
/// <c>Main</c> on a compiler-named class, so there is nothing a test can call — and the two
/// pieces of behaviour that matter most here (a cartridge template written to disk, an argument
/// rejected before anything opens) are only true of the tool as it actually starts. Reaching
/// them by reflection would run <c>Main</c> itself, and <c>Main</c> with a valid <c>run</c>
/// opens a window.</para>
///
/// <para>The apphost is the one MSBuild copied next to this test assembly from the
/// <c>Quarp.Cli</c> project reference, so it is the code just built and never a <c>quarp</c>
/// that happens to be installed on the machine. The <c>dotnet quarp.dll</c> fallback exists for
/// a layout published without an apphost; it is not the normal path.</para>
/// </summary>
internal static class CliProcess
{
    /// <summary>
    /// Generous on purpose: the first run pays for JIT and, for anything that compiles a cart,
    /// for Roslyn waking up. It is a deadlock guard, not a performance budget — a run that
    /// takes a minute has hung, and hanging is what this turns into a readable failure.
    /// </summary>
    private const int TimeoutMilliseconds = 120_000;

    public static CliResult Run(params string[] args)
    {
        string[] launcher = Launcher();
        var startInfo = new ProcessStartInfo
        {
            FileName = launcher[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // The tool prints UTF-8 without a BOM (Program.cs says why); decode it the same way
            // rather than through whatever code page this machine's console defaults to.
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            // Deliberately not the repository: nothing these tests run may resolve a relative
            // path into the working tree by accident.
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string argument in launcher[1..])
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {startInfo.FileName}");
        // Both pipes drained concurrently: a child that fills one while the parent blocks on the
        // other deadlocks, and `quarp new` writes to both.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"quarp {string.Join(' ', args)} did not finish in {TimeoutMilliseconds} ms");
        }
        return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// The command that starts the tool: the apphost beside this test assembly when MSBuild
    /// produced one, otherwise the muxer plus <c>quarp.dll</c>.
    /// </summary>
    private static string[] Launcher()
    {
        string apphost = Path.Combine(
            AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "quarp.exe" : "quarp");
        if (File.Exists(apphost))
        {
            return [apphost];
        }
        string library = Path.Combine(AppContext.BaseDirectory, "quarp.dll");
        Assert.True(
            File.Exists(library),
            $"neither {apphost} nor {library} exists — the Quarp.Cli project reference stopped "
            + "copying the tool next to its tests, and every test in this file is now testing nothing");
        // DOTNET_HOST_PATH is set by the SDK for anything it launches, test hosts included.
        string muxer = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host
            : "dotnet";
        return [muxer, library];
    }
}
