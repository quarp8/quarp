using System.Text;

namespace Quarp.Cli.Tests;

/// <summary>
/// A throwaway cartridge folder on disk, for the tests of <c>quarp build</c> and
/// <c>quarp map build</c> — both of which are commands about files, so both need real ones.
///
/// <para>Under the system temp directory, never inside the working tree: a stray cartridge in
/// the repository would be found by the code budget, by the packer's tests and eventually by CI
/// (the same reasoning <see cref="QuarpNewTests"/> spells out).</para>
///
/// <para>Shared by two test classes and named for one of them, because the file-ownership split
/// of this milestone stage gives its brigade the names <c>Map*</c> and <c>Build*</c> in this
/// folder and nothing else. Nothing about it is specific to <c>build</c>.</para>
/// </summary>
internal sealed class BuildTestCart : IDisposable
{
    /// <summary>
    /// The string a cartridge shouts when the console runs its <c>Init</c> or <c>Update</c>.
    /// Deliberately not a word that could appear in a compiler message: every assertion that
    /// looks for it is asserting that a tick did or did not happen, and nothing else.
    /// </summary>
    public const string TickMarker = "QUARP-TICK-RAN";

    /// <summary>The smallest cartridge that survives the analyzers, and does something visible.</summary>
    public const string HealthyMainCs = """
        using Quarp.Api;

        namespace Fixture;

        public sealed class FixtureCart : Cartridge
        {
            private int _x;

            public override void Update()
            {
                _x++;
            }

            public override void Draw()
            {
                Cls(0);
                RectFill(_x % 120, 32, 8, 8, 7);
            }
        }
        """;

    /// <summary>
    /// A cartridge that compiles, loads and constructs perfectly, and blows up the instant
    /// anything runs a tick of it. Both overrides throw, because <c>Init</c> is what
    /// <c>AttachCart</c> runs and <c>Update</c> is what a tick runs, and the claim under test —
    /// "<c>quarp build</c> runs neither" — is false if either one ever fires.
    ///
    /// <para>Fully qualified <c>System.InvalidOperationException</c>: cartridge compilations get
    /// no implicit usings (<c>CartCompiler</c> builds its <c>CSharpCompilationOptions</c> without
    /// any), which is why every cart in this repository starts with <c>using Quarp.Api;</c> and
    /// nothing else. The type itself is ordinary — the banned-API surface covers
    /// <c>Random</c>, <c>Guid</c>, <c>Environment</c>, <c>Math</c>, <c>Console</c> and the
    /// <c>System.IO</c>/<c>Net</c>/<c>Threading</c> subtrees, none of which this touches.</para>
    /// </summary>
    public const string ExplosiveMainCs = """
        using Quarp.Api;

        namespace Fixture;

        public sealed class ExplosiveCart : Cartridge
        {
            public override void Init()
            {
                throw new System.InvalidOperationException("QUARP-TICK-RAN in Init");
            }

            public override void Update()
            {
                throw new System.InvalidOperationException("QUARP-TICK-RAN in Update");
            }
        }
        """;

    /// <summary>A C# file that does not parse; the diagnostic names its line and column.</summary>
    public const string BrokenSourceCs = """
        namespace Fixture;

        public static class Broken
        {
            public static int Value = ;
        }
        """;

    public BuildTestCart(string label, string mainCs)
    {
        Root = Path.Combine(
            Path.GetTempPath(), $"quarp-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(Root, "src"));
        Write("manifest.json", """
            {
                "name": "Fixture",
                "author": "Quarp Tests",
                "profile": 8
            }
            """);
        Write("src/main.cs", mainCs);
    }

    /// <summary>Absolute path of the cart folder — what every command under test is handed.</summary>
    public string Root { get; }

    /// <summary>Absolute path of a file inside the cart, named cart-relative with '/'.</summary>
    public string At(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public BuildTestCart Write(string relative, string text)
    {
        File.WriteAllText(At(relative), text);
        return this;
    }

    public BuildTestCart Write(string relative, byte[] bytes)
    {
        File.WriteAllBytes(At(relative), bytes);
        return this;
    }

    public byte[] Read(string relative) => File.ReadAllBytes(At(relative));

    public bool Has(string relative) => File.Exists(At(relative));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a green test over.
        }
    }

    /// <summary>
    /// A map in the shape Tiled's <c>File → Export As… → CSV</c> writes one: 72 rows of 256
    /// 0-based tile ids, <c>-1</c> for an empty cell, no trailing comma, a trailing newline.
    /// Mostly empty and not uniform, so a bank built from it is neither all zeros nor a single
    /// repeated byte — either of which would let a broken compiler pass a byte-count assertion.
    ///
    /// <para>The dimensions are written out rather than read from <c>CartData</c> on purpose:
    /// 256 x 72 = 18432 is the anchor these tests exist to hold, and an anchor restated from the
    /// constant it is checking holds nothing.</para>
    /// </summary>
    public static string MapCsv(int width = 256, int height = 72)
    {
        var text = new StringBuilder(width * height * 3);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x > 0)
                {
                    text.Append(',');
                }
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                text.Append(border ? 1 : x == y ? 2 : x % 37 == 0 && y % 11 == 0 ? 3 : -1);
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// More than the code budget (SPEC-8 §6) of source that still parses, so the cartridge
    /// fails on the code budget and not on anything else. Fields rather than comments: comments
    /// are free by design and would produce a cart that passes. The default field count clears
    /// the 256 KB budget ratified 2026-08-19 (ADR-024) with margin — about 379 KB of generated
    /// source against a 256 KB cap — the same ~1.45x headroom the previous 64 KB-era default
    /// (2500 fields, ~97.5 KB) held over its own cap.
    /// </summary>
    public static string OversizedSource(int fields = 10000)
    {
        var text = new StringBuilder(fields * 24 + 64);
        text.Append("namespace Fixture;\n\npublic static class Bulk\n{\n");
        for (int i = 0; i < fields; i++)
        {
            text.Append("    public const int Pad").Append(i.ToString("D5")).Append(" = ").Append(i).Append(";\n");
        }
        text.Append("}\n");
        return text.ToString();
    }
}
