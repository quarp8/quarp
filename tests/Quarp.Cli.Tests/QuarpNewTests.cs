using System.Text.Json;
using Quarp.CartKit;
using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// <c>quarp new</c> end to end: the real command, in a real empty folder, writing real files.
///
/// <para>Everything it does lives in <c>Program.cs</c>'s top-level statements —
/// <c>CreateNewCart</c>, <c>WriteDevProject</c>, <c>WriteVsCodeFiles</c>,
/// <c>FindQuarpExecutable</c> — so a child process is the only way to reach it (see
/// <see cref="CliProcess"/>). It is also the honest way: three of those four answer questions
/// about the machine the tool is installed on (is there an apphost beside me, is
/// <c>Quarp.Analyzers.dll</c> beside me, what is my absolute path), and a test that stubbed
/// those out would be testing the stubs.</para>
///
/// <para>The folder is created under the system temp directory, never inside the working tree:
/// a stray cartridge in the repository would be picked up by the code budget, by the packer's
/// tests and eventually by CI.</para>
/// </summary>
public sealed class QuarpNewTests : IDisposable
{
    private readonly string _root;

    public QuarpNewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ANewCartridgeHasEveryFileTheCommandPromises()
    {
        string cart = Path.Combine(_root, "my-cart");

        CliResult result = CliProcess.Run("new", cart);

        Assert.Equal(0, result.ExitCode);
        // The three warnings WriteDevProject and WriteVsCodeFiles print when they skip
        // themselves: if either fired, the cartridge would still be created and the assertions
        // below would fail with "file not found" instead of naming the cause.
        Assert.Equal(string.Empty, result.StdErr);

        // The cartridge proper.
        Assert.True(File.Exists(Path.Combine(cart, "manifest.json")), "manifest.json");
        Assert.True(File.Exists(Path.Combine(cart, "src", "main.cs")), "src/main.cs");
        // The dev-only project that gives an editor the QRP1001-QRP1004 diagnostics.
        Assert.True(File.Exists(Path.Combine(cart, ".quarp", "cart.csproj")), ".quarp/cart.csproj");
        // The dev-only launch configuration that makes F5 work.
        Assert.True(File.Exists(Path.Combine(cart, ".vscode", "launch.json")), ".vscode/launch.json");
        Assert.True(File.Exists(Path.Combine(cart, ".vscode", "tasks.json")), ".vscode/tasks.json");

        // The manifest names the folder, which is what `quarp run` prints and what the packer
        // puts in the .quarp8.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(cart, "manifest.json")));
        Assert.Equal("my-cart", manifest.RootElement.GetProperty("name").GetString());
        Assert.Equal(8, manifest.RootElement.GetProperty("profile").GetInt32());
    }

    /// <summary>
    /// The half of the launch configuration that only exists once it has been written: the
    /// token is gone, and what replaced it is an absolute path to a file that is really there.
    /// A configuration pointing at nothing fails as a VS Code dialog with no explanation, which
    /// is why <c>WriteVsCodeFiles</c> would rather skip the folder than write a broken one.
    /// </summary>
    [Fact]
    public void TheWrittenLaunchConfigurationPointsAtAnExecutableThatExists()
    {
        string cart = Path.Combine(_root, "launchable");
        Assert.Equal(0, CliProcess.Run("new", cart).ExitCode);

        string text = File.ReadAllText(Path.Combine(cart, ".vscode", "launch.json"));
        Assert.DoesNotContain(CartTemplate.ToolPathToken, text, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(
            text,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement configuration = document.RootElement.GetProperty("configurations")[0];

        string program = configuration.GetProperty("program").GetString()!;
        Assert.True(Path.IsPathFullyQualified(program), $"program is not an absolute path: {program}");
        Assert.True(File.Exists(program), $"program does not exist: {program}");
        // The same switches CartTemplateTests pins on the template, re-read from disk: this is
        // the file a debugger will actually load.
        Assert.False(CartTemplateTests.Switch(configuration, "justMyCode"));
        Assert.True(CartTemplateTests.Switch(configuration, "requireExactSource"));
    }

    /// <summary>
    /// The template is a cartridge, not a wall of text that looks like one: the compiler this
    /// asserts through is the compiler <c>quarp run</c> uses, analyzers included. A template that
    /// stopped compiling would greet every new author with a build error on an empty project.
    /// </summary>
    [Fact]
    public void TheTemplateCartridgeCompilesAndRuns()
    {
        string cart = Path.Combine(_root, "runnable");
        Assert.Equal(0, CliProcess.Run("new", cart).ExitCode);

        CliResult sim = CliProcess.Run("sim", cart, "--ticks", "60");

        Assert.Equal(0, sim.ExitCode);
        string[] lines = sim.OutLines();
        // The two lines every headless run ends with - the labelled PCM digest, then the bare
        // framebuffer hash. This is also the output the generated tasks.json comment describes.
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(Checkpoint.AudioPrefix + " ", lines[0], StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{16}$", lines[1]);
    }

    /// <summary>
    /// Refusing to write into a folder that already holds a cartridge. The check is on
    /// <c>manifest.json</c> rather than on the folder being empty, so that <c>quarp new</c> into
    /// a directory with a README in it still works — but overwriting somebody's <c>src/main.cs</c>
    /// is a data loss no message makes up for.
    /// </summary>
    [Fact]
    public void CreatingACartridgeOverAnExistingOneIsRefused()
    {
        string cart = Path.Combine(_root, "twice");
        Assert.Equal(0, CliProcess.Run("new", cart).ExitCode);
        string original = File.ReadAllText(Path.Combine(cart, "src", "main.cs"));
        File.WriteAllText(Path.Combine(cart, "src", "main.cs"), original + "\n// mine\n");

        CliResult second = CliProcess.Run("new", cart);

        Assert.Equal(1, second.ExitCode);
        Assert.Contains("already contains a cartridge", second.StdErr, StringComparison.Ordinal);
        Assert.EndsWith("// mine\n", File.ReadAllText(Path.Combine(cart, "src", "main.cs")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheCommandNeedsAFolder()
    {
        CliResult result = CliProcess.Run("new");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("usage: quarp new <folder>", result.StdErr, StringComparison.Ordinal);
    }
}
