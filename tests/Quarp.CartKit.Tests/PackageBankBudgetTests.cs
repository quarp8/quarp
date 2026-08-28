using Quarp.CartKit;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The package size limit covers everything except the data banks.
///
/// <para><b>Why the exclusion exists.</b> SPEC-8 §6's 320 KB was sized as "256 KB of code plus
/// about 31 KB of assets, raw" so that the code budget is the limit an author actually meets.
/// ADR-035 then ratified up to 4 MiB of data banks and nobody revisited the sentence, which
/// left a cartridge the console explicitly permits unable to satisfy its own package limit.
/// The POOM port reached it with 514 KB of banks for six levels — and only at
/// <c>quarp pack</c>, because <c>quarp build</c> does not package.</para>
/// </summary>
public class PackageBankBudgetTests : IDisposable
{
    private readonly string _root;

    public PackageBankBudgetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart folder that loads: a manifest and one cartridge source.</summary>
    private string WriteCart(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "manifest.json"),
            "{\"name\":\"" + name + "\",\"author\":\"test\",\"profile\":8}");
        File.WriteAllText(Path.Combine(folder, "src", "main.cs"),
            "using Quarp.Api;\npublic sealed class Game : Cartridge\n{\n"
            + "    public override void Update() { }\n"
            + "    public override void Draw() { Cls(0); }\n}\n");
        return folder;
    }

    /// <summary>
    /// Banks big enough to blow the old whole-file limit several times over, and the package
    /// still writes. Incompressible bytes on purpose: a bank of zeros would zip to nothing and
    /// the test would pass without exercising anything.
    /// </summary>
    [Fact]
    public void ACartridgeWhoseBanksDwarfTheBudgetStillPacks()
    {
        string folder = WriteCart("banked");
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        var random = new Random(1);
        long banked = 0;
        for (int bank = 0; bank < 8; bank++)
        {
            byte[] bytes = new byte[128 * 1024];
            random.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(folder, "data", $"{bank:00}.bin"), bytes);
            banked += bytes.Length;
        }
        Assert.True(banked > Quarp8Package.MaxPackageBytes,
            "the banks have to exceed the budget or this proves nothing");

        string package = Path.Combine(_root, "banked.quarp8");
        Quarp8Package.Pack(folder, package);

        long size = new FileInfo(package).Length;
        Assert.True(size > Quarp8Package.MaxPackageBytes,
            $"the package is {size} bytes, which no longer tests the exclusion");
        Assert.True(size <= Quarp8Package.MaxFileBytes);

        // And it loads back — the check is on both forms.
        CartData data = CartSource.Load(package);
        Assert.Equal(8, CountBanks(data));
    }

    private static int CountBanks(CartData data)
    {
        int count = 0;
        for (int i = 0; i < data.DataBanks.Count; i++)
        {
            if (data.DataBanks[i].Length > 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// The budget still bites on what it covers. A cartridge with no banks at all and a
    /// gfx.png over the limit is refused exactly as before — the exclusion is a hole for
    /// <c>data/</c>, not a hole in the limit.
    /// </summary>
    [Fact]
    public void TheBudgetStillRefusesEverythingItCovers()
    {
        string folder = WriteCart("fat");
        var random = new Random(2);
        byte[] noise = new byte[Quarp8Package.MaxPackageBytes + 64 * 1024];
        random.NextBytes(noise);
        // Not a real asset — Pack validates the folder first, so put the weight somewhere the
        // loader accepts any size: a bank is excluded, so use several source files instead.
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        for (int i = 0; i < 8; i++)
        {
            File.WriteAllText(Path.Combine(folder, "src", $"blob{i}.cs"),
                "// " + Convert.ToBase64String(noise, i * 40000, 40000) + "\npublic static class B" + i + " { }\n");
        }

        string package = Path.Combine(_root, "fat.quarp8");
        CartLoadException error = Assert.Throws<CartLoadException>(() => Quarp8Package.Pack(folder, package));

        Assert.Contains("without data banks", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(package), "an over-budget package must not be left on disk");
    }

    /// <summary>The two limits are different numbers, and the file one is the larger.</summary>
    [Fact]
    public void TheFileLimitIsTheBudgetPlusEveryBankTheConsoleAllows()
    {
        Assert.Equal(Quarp8Package.MaxPackageBytes + CartData.DataBanksMaxTotalBytes,
            Quarp8Package.MaxFileBytes);
    }
}
