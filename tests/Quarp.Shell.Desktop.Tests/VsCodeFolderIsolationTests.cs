using System.IO.Compression;
using System.Text;
using Quarp.CartKit;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <c>quarp new</c> drops a dev-only <c>.vscode/</c> into the cartridge folder so that opening
/// that folder in VS Code and pressing F5 debugs the cart (ADR-019; M4 work order, stage 1). Like
/// <c>.quarp/</c> before it, that folder has to be invisible to the cartridge itself — and
/// "invisible" is four separate claims that can each break on their own, so each is pinned
/// here, exactly as <c>DevProjectIsolationTests</c> pins them for <c>.quarp/</c>.
///
/// <para>The claims are true <b>by construction</b> rather than by a filter someone remembered
/// to write: the loader globs <c>src/**/*.cs</c> and names its assets one by one, the packer
/// writes only that same list, and the watcher's relevance check is an allow-list. These tests
/// exist so that the day someone replaces one of those allow-lists with a
/// "everything except <c>.quarp</c>" deny-list, the cart stops loading here instead of in
/// someone's game.</para>
///
/// <para>Living in the shell's test project rather than CartKit's is a file-ownership
/// consequence of the M4 work order, not a design statement — everything it touches is
/// CartKit's, and it would sit just as well next to <c>DevProjectIsolationTests</c>.</para>
/// </summary>
public class VsCodeFolderIsolationTests : IDisposable
{
    /// <summary>
    /// A stand-in for whatever is under <c>.vscode/</c>, and deliberately <em>not</em> a copy of
    /// what <c>quarp new</c> writes.
    ///
    /// <para>It used to be one — a private const shaped like the real <c>launch.json</c>, which
    /// a test in this file then parsed and called "the shape of the file <c>quarp new</c>
    /// writes". It was not: it was a string sitting fifteen lines above the assertions. The real
    /// template had grown five keys the copy never heard of, and deleting <c>justMyCode</c> from
    /// <c>CartTemplate.LaunchJson</c> left the whole suite green. The template is now asserted on
    /// itself in <c>Quarp.Cli.Tests.CartTemplateTests</c>, and nothing about the real file is
    /// restated here, because two spellings of one fact drift apart again.</para>
    ///
    /// <para>What these tests need from this string is only that it be a plausible editor file
    /// with a comment in it: every claim below is about <em>where</em> a file sits, never about
    /// what it contains.</para>
    /// </summary>
    private const string VsCodeFileStandIn = """
        {
            // Some file VS Code keeps here. Its contents are not this file's subject.
            "version": "0.2.0"
        }
        """;

    private readonly string _folder;

    public VsCodeFolderIsolationTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "quarp-vscode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "src"));
        Directory.CreateDirectory(Path.Combine(_folder, ".vscode"));

        File.WriteAllText(Path.Combine(_folder, "manifest.json"),
            "{\"name\":\"vscode\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), """
            using Quarp.Api;

            public sealed class VsCodeCart : Cartridge
            {
                public override void Draw() => Cls(0);
            }
            """);
        File.WriteAllText(Path.Combine(_folder, ".vscode", "launch.json"), VsCodeFileStandIn);
        File.WriteAllText(Path.Combine(_folder, ".vscode", "tasks.json"),
            "{ \"version\": \"2.0.0\", \"tasks\": [ { \"label\": \"quarp-build\" } ] }");

        // Not something VS Code writes — it is the sharpest probe available for the claim the
        // loader actually makes. If the glob were ever widened from src/ to the whole folder,
        // this file alone would blow the 64 KB budget *and* add a second Cartridge subclass,
        // so the cart would fail loudly instead of quietly compiling someone's settings.
        var intruder = new StringBuilder(CodeBudget.MaxBytes + 4096);
        intruder.Append("public sealed class VsCodeIntruder : Quarp.Api.Cartridge {\n");
        while (intruder.Length < CodeBudget.MaxBytes + 2048)
        {
            intruder.Append("    private int _padPadPadPadPadPadPadPadPadPadPadPadPadPadPad;\n");
        }
        intruder.Append("}\n");
        File.WriteAllText(Path.Combine(_folder, ".vscode", "intruder.cs"), intruder.ToString());
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// The loader compiles <c>src/**/*.cs</c> and nothing else — so the folder is invisible to
    /// the compilation, and the oversized intruder never reaches the code budget either. Both
    /// halves matter: the budget is measured over exactly the list this returns.
    /// </summary>
    [Fact]
    public void LoaderSeesOnlyTheCartridgeSources()
    {
        CartData data = CartSource.LoadFolder(_folder);

        CartSourceFile only = Assert.Single(data.Sources);
        Assert.Equal("src/main.cs", only.RelativePath);
        Assert.True(
            CodeBudget.Measure(data.Sources) < 1024,
            "the .vscode folder leaked into the code budget");
    }

    /// <summary>
    /// The package is the cart as it ships. A launch configuration full of absolute paths from
    /// the author's machine has no business travelling with it.
    /// </summary>
    [Fact]
    public void PackerLeavesTheVsCodeFolderOut()
    {
        string package = _folder + ".quarp8";
        try
        {
            Quarp8Package.Pack(_folder, package);

            using ZipArchive zip = ZipFile.OpenRead(package);
            var names = new List<string>();
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                names.Add(entry.FullName.Replace('\\', '/'));
            }
            Assert.DoesNotContain(names, n => n.StartsWith(".vscode", StringComparison.Ordinal));
            Assert.Contains("manifest.json", names);
            Assert.Contains("src/main.cs", names);
        }
        finally
        {
            File.Delete(package);
        }
    }

    /// <summary>
    /// The cart hashes the same with and without the folder. This is the anchor that matters
    /// most in M4: <c>CartIdentity</c> is computed from relative paths and source text, replays
    /// name it in their header, and CI compares a <c>sha256</c> that must not move because a
    /// debugging convenience appeared in the folder.
    /// </summary>
    [Fact]
    public void VsCodeFolderDoesNotAffectTheCartIdentity()
    {
        byte[] withVsCode = CartIdentity.Compute(CartSource.LoadFolder(_folder));

        Directory.Delete(Path.Combine(_folder, ".vscode"), recursive: true);
        byte[] without = CartIdentity.Compute(CartSource.LoadFolder(_folder));

        Assert.Equal(without, withVsCode);
    }

    /// <summary>
    /// VS Code rewrites files under <c>.vscode/</c> on its own — breakpoint state, a changed
    /// setting, an extension tidying JSON. None of that is an author's edit, and a hot reload
    /// per keystroke in the settings editor would be its own kind of unusable.
    /// </summary>
    [Fact]
    public void WatcherIgnoresTheVsCodeFolder()
    {
        using var watcher = new CartWatcher(_folder);
        // Drain anything the constructor latched onto before the window opened.
        Thread.Sleep(CartWatcher.DebounceMilliseconds * 2);
        watcher.ConsumeReloadRequest();

        File.WriteAllText(Path.Combine(_folder, ".vscode", "launch.json"),
            VsCodeFileStandIn + "\n// touched by the editor\n");
        Thread.Sleep(CartWatcher.DebounceMilliseconds * 3);

        Assert.False(watcher.ConsumeReloadRequest());
    }

    /// <summary>
    /// The control that keeps the four tests above honest: put the same intruder under
    /// <c>src/</c> and everything they assert flips. The folder being ignored is a property of
    /// <em>where</em> the file is, not of the file being harmless.
    /// </summary>
    [Fact]
    public void TheSameIntruderUnderSrcIsNotIgnored()
    {
        File.Move(
            Path.Combine(_folder, ".vscode", "intruder.cs"),
            Path.Combine(_folder, "src", "intruder.cs"));

        CartLoadException failure = Assert.Throws<CartLoadException>(() => CartSource.LoadFolder(_folder));
        Assert.Contains("code budget exceeded", failure.Message, StringComparison.Ordinal);
    }
}
