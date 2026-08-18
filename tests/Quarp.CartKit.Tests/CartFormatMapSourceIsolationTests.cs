using System.IO.Compression;
using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// From stage 2 of M4 a cartridge folder carries a map <em>source</em>, <c>map.csv</c>, which
/// <c>quarp map build</c> compiles into <c>map.bin</c> exactly the way <c>sfx.txt</c> compiles
/// into <c>sfx.bin</c>. Only the compiled bank ships: a <c>.quarp8</c> is the built game, not
/// the working folder (M4 work order Р13, SPEC-8 §6).
///
/// <para>"Does not ship" is four separate claims that can each break on their own, so each is
/// pinned separately here — the same four <c>VsCodeFolderIsolationTests</c> and
/// <c>DevProjectIsolationTests</c> pin for <c>.vscode/</c> and <c>.quarp/</c>: the loader does
/// not see it, the code budget does not count it, the cart identity does not move because of
/// it, and the watcher does not treat it as an edit worth reloading for.</para>
///
/// <para>All four are true <b>by construction</b> rather than by a filter someone remembered to
/// write: the loader globs <c>src/**/*.cs</c> and names its root assets one at a time, the
/// packer writes only that same named list, and the watcher's relevance check is an allow-list.
/// These tests exist so that the day one of those allow-lists is replaced with an "everything
/// except…" deny-list, the failure lands here instead of inside somebody's shipped cart.</para>
///
/// <para>Nothing here parses the CSV or calls the map compiler. The subject is the file's
/// itinerary, never its contents — a test that also compiled the map would start failing for
/// reasons that have nothing to do with what it claims.</para>
/// </summary>
public class CartFormatMapSourceIsolationTests : IDisposable
{
    /// <summary>Marker byte at map cell 0 of <c>map.bin</c>, chosen outside the range the CSV uses.</summary>
    private const byte CompiledMapMarker = 7;

    /// <summary>
    /// Lowest tile id written into the stand-in <c>map.csv</c>. Every value in it is a
    /// three-digit number, which does two jobs at once: it can never be mistaken for
    /// <see cref="CompiledMapMarker"/> if the text ever leaked in as the map, and it makes the
    /// file larger than the 64 KB code budget, so the reverse control below has real teeth.
    /// </summary>
    private const int CsvFirstTile = 100;

    private readonly string _folder;

    public CartFormatMapSourceIsolationTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "quarp-mapsrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "src"));

        File.WriteAllText(Path.Combine(_folder, "manifest.json"),
            "{\"name\":\"mapsrc\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(Path.Combine(_folder, "src", "main.cs"), """
            using Quarp.Api;

            public sealed class MapSourceCart : Cartridge
            {
                public override void Draw() => Map(0, 0, 0, 0, 16, 9);
            }
            """);

        // The compiled bank: all zeros but for one marker cell, so a test can tell "the console
        // got map.bin" from "the console got something derived from the text next to it".
        byte[] compiled = new byte[CartData.MapWidth * CartData.MapHeight];
        compiled[0] = CompiledMapMarker;
        File.WriteAllBytes(Path.Combine(_folder, "map.bin"), compiled);

        File.WriteAllText(Path.Combine(_folder, "map.csv"), BuildMapCsv(), new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }

    /// <summary>
    /// A full-size map export the way Tiled writes one on Windows: 72 rows of 256 comma-separated
    /// tile ids, CRLF line endings, no trailing comma, trailing newline (M4 work order Р11).
    /// Every id is three digits, which puts the file over <see cref="CodeBudget.MaxBytes"/> —
    /// 72 x 1023 characters of payload — and that size is load-bearing: it is what lets the
    /// reverse control prove the four claims above can go red.
    /// </summary>
    private static string BuildMapCsv()
    {
        var builder = new StringBuilder(CartData.MapHeight * (CartData.MapWidth * 4 + 2));
        for (int y = 0; y < CartData.MapHeight; y++)
        {
            for (int x = 0; x < CartData.MapWidth; x++)
            {
                if (x > 0)
                {
                    builder.Append(',');
                }
                builder.Append(CsvFirstTile + ((x + y) % 100));
            }
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    /// <summary>
    /// The stand-in CSV really is bigger than the code budget. Stated as its own assertion
    /// because two later tests are only meaningful while it holds, and a future edit to
    /// <see cref="BuildMapCsv"/> that shrank it would otherwise turn them green and silent.
    /// </summary>
    [Fact]
    public void TheStandInCsvIsLargerThanTheCodeBudget()
    {
        long size = new FileInfo(Path.Combine(_folder, "map.csv")).Length;
        Assert.True(size > CodeBudget.MaxBytes, $"map.csv is {size} bytes, expected more than {CodeBudget.MaxBytes}");
    }

    /// <summary>
    /// Claim 1. The loader compiles <c>src/**/*.cs</c> and reads its assets by name, so the map
    /// source is invisible to both: it is not a source, it does not reach the code budget, and
    /// the map the console runs on is <c>map.bin</c>'s bytes.
    /// </summary>
    [Fact]
    public void LoaderSeesOnlyTheSourcesAndTheCompiledMap()
    {
        CartData data = CartSource.LoadFolder(_folder);

        CartSourceFile only = Assert.Single(data.Sources);
        Assert.Equal("src/main.cs", only.RelativePath);
        Assert.True(
            CodeBudget.Measure(data.Sources) < 1024,
            "map.csv leaked into the code budget");

        Assert.Equal(CartData.MapWidth * CartData.MapHeight, data.Map.Length);
        Assert.Equal(CompiledMapMarker, data.Map[0]);
    }

    /// <summary>
    /// Claim 2. The package is the cart as it ships: the compiled bank travels, the text it was
    /// built from stays home, and the package a player loads holds exactly the same map as the
    /// folder the author works in.
    /// </summary>
    [Fact]
    public void PackerShipsTheCompiledMapAndNotItsSource()
    {
        string package = _folder + ".quarp8";
        try
        {
            Quarp8Package.Pack(_folder, package);

            var names = new List<string>();
            using (ZipArchive zip = ZipFile.OpenRead(package))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    names.Add(entry.FullName.Replace('\\', '/'));
                }
            }
            Assert.DoesNotContain("map.csv", names);
            Assert.Contains("map.bin", names);
            Assert.Contains("manifest.json", names);
            Assert.Contains("src/main.cs", names);

            CartData fromPackage = CartSource.LoadPackage(package);
            Assert.Equal(CartSource.LoadFolder(_folder).Map, fromPackage.Map);
        }
        finally
        {
            File.Delete(package);
        }
    }

    /// <summary>
    /// Claim 3, the anchor that matters most in M4. <c>CartIdentity</c> is computed from relative
    /// paths and source text plus the decoded assets; replays name it in their header and CI
    /// compares a <c>sha256</c> that must not move because an author kept the map source next to
    /// the map. The second half of the test is the control that keeps the first from being
    /// vacuous: the compiled bank <em>is</em> part of the cartridge, so touching it must move the
    /// identity, and the comparison above is therefore one that can go red.
    /// </summary>
    [Fact]
    public void MapSourceDoesNotAffectTheCartIdentityButTheCompiledMapDoes()
    {
        byte[] withCsv = CartIdentity.Compute(CartSource.LoadFolder(_folder));

        File.Delete(Path.Combine(_folder, "map.csv"));
        byte[] withoutCsv = CartIdentity.Compute(CartSource.LoadFolder(_folder));
        Assert.Equal(withoutCsv, withCsv);

        string mapBin = Path.Combine(_folder, "map.bin");
        byte[] compiled = File.ReadAllBytes(mapBin);
        compiled[1] = 1;
        File.WriteAllBytes(mapBin, compiled);
        Assert.NotEqual(withoutCsv, CartIdentity.Compute(CartSource.LoadFolder(_folder)));
    }

    /// <summary>
    /// Claim 4. The console runs the compiled bank, so the reload is due when
    /// <c>quarp map build</c> produces a new <c>map.bin</c> — not on every keystroke in the text.
    /// This is the same rule the watcher already applies to <c>sfx.txt</c> versus <c>sfx.bin</c>,
    /// and the second half is again the control: <c>map.bin</c> does wake the reload, so a
    /// watcher that had stopped reporting anything at all would fail here rather than pass.
    /// </summary>
    [Fact]
    public void WatcherIgnoresTheMapSourceButNotTheCompiledMap()
    {
        using var watcher = new CartWatcher(_folder);
        // Drain anything the constructor latched onto before the window opened.
        Thread.Sleep(CartWatcher.DebounceMilliseconds * 2);
        watcher.ConsumeReloadRequest();

        File.AppendAllText(Path.Combine(_folder, "map.csv"), "# a comment the author just typed\r\n");
        Assert.False(PollForReload(watcher, timeoutMs: 500), "map.csv woke the hot reload");

        byte[] compiled = new byte[CartData.MapWidth * CartData.MapHeight];
        compiled[0] = CompiledMapMarker + 1;
        File.WriteAllBytes(Path.Combine(_folder, "map.bin"), compiled);
        Assert.True(PollForReload(watcher, timeoutMs: 5000), "a rebuilt map.bin was never reported");
    }

    /// <summary>
    /// The reverse control, in the spirit of <c>VsCodeFolderIsolationTests</c>: the very same
    /// bytes, given the extension the loader globs, are compiled, counted and rejected. What the
    /// four claims above rest on is <em>where the file sits and what it is called</em>, not the
    /// file being harmless — an author who renames the map source to <c>src/map.cs</c> gets a
    /// loud failure, which is the correct outcome and proof that the checks are alive.
    /// </summary>
    [Fact]
    public void TheSameBytesCompiledAsASourceAreNotIgnored()
    {
        File.Move(
            Path.Combine(_folder, "map.csv"),
            Path.Combine(_folder, "src", "map.cs"));

        CartLoadException failure = Assert.Throws<CartLoadException>(() => CartSource.LoadFolder(_folder));
        Assert.Contains("code budget exceeded", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the reverse control, and the one that corrects an expectation rather
    /// than confirming it: putting <c>map.csv</c> <em>inside</em> <c>src/</c> changes nothing.
    /// The exclusion is not "the cart root is filtered" — it is that both the loader's glob and
    /// the packer's are <c>*.cs</c>, everywhere they look. Worth pinning because the consequence
    /// is not obvious: a non-<c>.cs</c> file under <c>src/</c> is dropped from the package in
    /// silence, so an author who tucks data files in there will not find them at runtime.
    /// </summary>
    [Fact]
    public void ACsvUnderSrcIsStillNeitherCompiledNorPacked()
    {
        File.Move(
            Path.Combine(_folder, "map.csv"),
            Path.Combine(_folder, "src", "map.csv"));

        CartData data = CartSource.LoadFolder(_folder);
        CartSourceFile only = Assert.Single(data.Sources);
        Assert.Equal("src/main.cs", only.RelativePath);

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
            Assert.DoesNotContain("src/map.csv", names);
            Assert.Contains("src/main.cs", names);
        }
        finally
        {
            File.Delete(package);
        }
    }

    private static bool PollForReload(CartWatcher watcher, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (watcher.ConsumeReloadRequest())
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return false;
    }
}
