using System.Text.Json;
using Xunit;

namespace Quarp.Cli.Tests;

/// <summary>
/// The two VS Code files <c>quarp new</c> writes, asserted on <see cref="CartTemplate"/> itself
/// (ADR-019; M4 work order, stage 1).
///
/// <para><b>Why the source of the string matters more than the assertions.</b> Until this
/// project existed, no test referenced <c>Quarp.Cli</c>, so the only test that looked at a
/// launch configuration —
/// <c>Quarp.Shell.Desktop.Tests.VsCodeFolderIsolationTests.TheGeneratedLaunchConfigurationIsParseableJsonWithComments</c>
/// — parsed a private const declared a few lines above itself. It claimed to check "the shape of
/// the file <c>quarp new</c> writes"; deleting <c>justMyCode</c> from the real template left the
/// whole suite green, and the sample had already drifted (the real file also carries
/// <c>preLaunchTask</c>, <c>cwd</c>, <c>console</c>, <c>stopAtEntry</c> and
/// <c>enableStepFiltering</c>). That copy is gone and this is what replaced it.</para>
///
/// <para>The work order required <c>justMyCode: false</c> to be checked on the stand rather than
/// taken on trust, and that is the assertion below with a negative control behind it: removing
/// the key from <c>CartTemplate.LaunchJson</c> now fails this test, which is a sentence that was
/// not true of any test in the repository before.</para>
/// </summary>
public class CartTemplateTests
{
    /// <summary>
    /// The two allowances VS Code makes for its own configuration files. They are the reason the
    /// templates can carry their "these paths are absolute, regenerate after moving" warning
    /// inside themselves instead of in a README nobody opens.
    /// </summary>
    private static readonly JsonDocumentOptions VsCodeJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>A path with the two hazards of a real one: backslashes and a space.</summary>
    private const string WindowsToolPath = @"C:\Program Files\quarp\quarp.exe";

    [Fact]
    public void TheLaunchTemplateParsesAndKeepsItsLoadBearingSwitches()
    {
        using JsonDocument document = JsonDocument.Parse(CartTemplate.LaunchJson, VsCodeJson);

        JsonElement configuration = document.RootElement.GetProperty("configurations")[0];
        // coreclr is the C# extension's .NET debugger; anything else and F5 either does nothing
        // or launches the cart with no debugger attached, which looks identical until a
        // breakpoint is set.
        Assert.Equal("coreclr", configuration.GetProperty("type").GetString());
        Assert.Equal("launch", configuration.GetProperty("request").GetString());

        // The switch the work order called mandatory. A cartridge is an assembly loaded from a
        // byte array into a collectible context - exactly the shape of thing a debugger is
        // entitled to file under "not my code" and then step straight through.
        Assert.False(Switch(configuration, "justMyCode"));

        // And the switch that makes a bound breakpoint mean what it says: the debugger compares
        // the file on disk against the checksum in the PDB. Turning it off would hide a broken
        // checksum - the very failure DebugSymbolsTests exists to prevent - behind breakpoints
        // that bind to the wrong lines and never explain why.
        Assert.True(Switch(configuration, "requireExactSource"));
    }

    /// <summary>
    /// Reads a boolean key, failing with its name when it is not there. A missing key and a key
    /// set the wrong way are the same bug from a debugger's point of view — it falls back to its
    /// own default — but they are told apart very differently by whoever reads the failure.
    /// </summary>
    internal static bool Switch(JsonElement configuration, string name)
    {
        Assert.True(
            configuration.TryGetProperty(name, out JsonElement value),
            $"the launch configuration has no \"{name}\" key, so the debugger will use its own default");
        return value.GetBoolean();
    }

    [Fact]
    public void TheTasksTemplateParsesAndDescribesTheCompileCheck()
    {
        using JsonDocument document = JsonDocument.Parse(CartTemplate.TasksJson, VsCodeJson);

        JsonElement task = document.RootElement.GetProperty("tasks")[0];
        Assert.Equal("quarp-build", task.GetProperty("label").GetString());
        Assert.Equal("process", task.GetProperty("type").GetString());

        // `sim --ticks 0` and nothing else: it loads, compiles, runs Init and stops. A task that
        // drifted into `run` would open a window on every F5, twice.
        var arguments = new List<string?>();
        foreach (JsonElement argument in task.GetProperty("args").EnumerateArray())
        {
            arguments.Add(argument.GetString());
        }
        Assert.Equal(new[] { "sim", "${workspaceFolder}", "--ticks", "0" }, arguments);

        // The problem matcher is the whole point of the task - without it a failed compile is a
        // silent non-launch - and its pattern has to survive being read as JSON, where every
        // backslash in the regex is escaped twice over.
        JsonElement pattern = task.GetProperty("problemMatcher").GetProperty("pattern");
        Assert.Matches(
            pattern.GetProperty("regexp").GetString()!,
            "src/main.cs(7,20): error QRP1001: double is banned in cartridge code");
    }

    /// <summary>
    /// The cross-file link F5 depends on: <c>launch.json</c> names a task by label and VS Code
    /// resolves it in <c>tasks.json</c>. Nothing else checks the two spellings against each
    /// other, and a mismatch surfaces as a dialog about a task that does not exist.
    /// </summary>
    [Fact]
    public void TheLaunchConfigurationNamesATaskThatTasksJsonDefines()
    {
        using JsonDocument launch = JsonDocument.Parse(CartTemplate.LaunchJson, VsCodeJson);
        using JsonDocument tasks = JsonDocument.Parse(CartTemplate.TasksJson, VsCodeJson);

        string preLaunch = launch.RootElement.GetProperty("configurations")[0]
            .GetProperty("preLaunchTask").GetString()!;
        var labels = new List<string?>();
        foreach (JsonElement task in tasks.RootElement.GetProperty("tasks").EnumerateArray())
        {
            labels.Add(task.GetProperty("label").GetString());
        }
        Assert.Contains(preLaunch, labels);
    }

    /// <summary>
    /// What <see cref="CartTemplate.ToolPathToken"/> is for, performed on a path with both
    /// hazards. The token carries its own quotes and is replaced by a JSON-encoded string, so a
    /// Windows path arrives escaped; interpolating it raw would produce a file VS Code cannot
    /// read, and the failure would be a dialog with no line number in it.
    /// </summary>
    [Fact]
    public void SubstitutingAWindowsPathLeavesBothFilesValidJson()
    {
        string quoted = JsonSerializer.Serialize(WindowsToolPath);
        string launchText = CartTemplate.LaunchJson.Replace(CartTemplate.ToolPathToken, quoted, StringComparison.Ordinal);
        string tasksText = CartTemplate.TasksJson.Replace(CartTemplate.ToolPathToken, quoted, StringComparison.Ordinal);

        // Non-vacuity: if the token ever stopped appearing in a template, both replacements
        // would be no-ops and the assertions below would pass on the unsubstituted file.
        Assert.DoesNotContain(CartTemplate.ToolPathToken, launchText, StringComparison.Ordinal);
        Assert.DoesNotContain(CartTemplate.ToolPathToken, tasksText, StringComparison.Ordinal);

        using JsonDocument launch = JsonDocument.Parse(launchText, VsCodeJson);
        using JsonDocument tasks = JsonDocument.Parse(tasksText, VsCodeJson);
        Assert.Equal(
            WindowsToolPath,
            launch.RootElement.GetProperty("configurations")[0].GetProperty("program").GetString());
        Assert.Equal(
            WindowsToolPath,
            tasks.RootElement.GetProperty("tasks")[0].GetProperty("command").GetString());
    }

    /// <summary>
    /// The dev-only project is formatted, not JSON, and its one substitution is the tools folder.
    /// <c>EnableDefaultCompileItems=false</c> plus the explicit glob is the load-bearing pair:
    /// the analyzers only wake up for a compilation that declares a <c>Cartridge</c> subclass, so
    /// a glob that missed <c>main.cs</c> would switch all four rules off without a word.
    /// </summary>
    [Fact]
    public void TheDevProjectPointsTheEditorAtTheAnalyzersAndTheSources()
    {
        string project = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            CartTemplate.DevProjectFormat,
            @"C:\tools\quarp\");

        Assert.Contains(@"<Analyzer Include=""C:\tools\quarp\Quarp.Analyzers.dll"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<HintPath>C:\tools\quarp\Quarp.Api.dll</HintPath>", project, StringComparison.Ordinal);
        Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""..\src\**\*.cs"" />", project, StringComparison.Ordinal);
    }
}
