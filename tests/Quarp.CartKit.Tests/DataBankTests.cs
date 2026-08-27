using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// Data banks (ADR-035) as the format sees them: read from a folder, packed, read back from the
/// package, size-limited, and — the part that matters most — invisible to a cartridge that has
/// none.
/// </summary>
public class DataBankTests : IDisposable
{
    private readonly string _root;

    public DataBankTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-banks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }

    private string MakeCartFolder(string name = "cart")
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            """{"name":"bank-cart","author":"tester","profile":8}""");
        File.WriteAllText(
            Path.Combine(folder, "src", "main.cs"),
            "using Quarp.Api;\npublic sealed class TestCart : Cartridge { }\n");
        return folder;
    }

    private static void WriteBank(string folder, int bank, byte[] bytes)
    {
        string dir = Path.Combine(folder, "data");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, CartSource.BankFileName(bank)), bytes);
    }

    [Fact]
    public void ACartWithoutBanksGetsSixtyFourEmptyOnes()
    {
        CartData data = CartSource.Load(MakeCartFolder());

        Assert.Equal(CartData.DataBankCount, data.DataBanks.Count);
        Assert.All(data.DataBanks, bank => Assert.Empty(bank));
    }

    [Fact]
    public void BanksAreReadFromTheFolderByNumber()
    {
        string folder = MakeCartFolder();
        WriteBank(folder, 0, new byte[] { 1, 2, 3 });
        WriteBank(folder, 63, new byte[] { 9 });

        CartData data = CartSource.Load(folder);

        Assert.Equal(new byte[] { 1, 2, 3 }, data.DataBanks[0]);
        Assert.Equal(new byte[] { 9 }, data.DataBanks[63]);
        Assert.Empty(data.DataBanks[1]);
    }

    [Fact]
    public void BanksSurviveThePackRoundTripByteForByte()
    {
        string folder = MakeCartFolder();
        var payload = new byte[5000];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 7);
        }
        WriteBank(folder, 3, payload);

        string package = Path.Combine(_root, "cart.quarp8");
        Quarp8Package.Pack(folder, package);
        CartData packed = CartSource.LoadPackage(package);

        Assert.Equal(payload, packed.DataBanks[3]);
    }

    [Fact]
    public void PackingIsStillDeterministicWithBanks()
    {
        string folder = MakeCartFolder();
        WriteBank(folder, 1, new byte[] { 4, 5, 6, 7 });
        WriteBank(folder, 2, new byte[] { 8 });

        string first = Path.Combine(_root, "a.quarp8");
        string second = Path.Combine(_root, "b.quarp8");
        Quarp8Package.Pack(folder, first);
        Quarp8Package.Pack(folder, second);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void AnOversizedBankIsRefusedByName()
    {
        string folder = MakeCartFolder();
        WriteBank(folder, 7, new byte[CartData.DataBankMaxBytes + 1]);

        CartLoadException error = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));

        Assert.Contains("data/07.bin", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The compatibility promise of the ADR: a cartridge that carries no banks keeps the
    /// identity it had before banks existed, so every replay already recorded stays attached
    /// to it. A cartridge that does carry one is a different cartridge.
    /// </summary>
    [Fact]
    public void EmptyBanksDoNotMoveTheIdentityButRealOnesDo()
    {
        string plain = MakeCartFolder("plain");
        byte[] before = CartIdentity.Compute(CartSource.Load(plain));

        // Same folder, an empty data/ directory added: still no bytes, still the same cart.
        Directory.CreateDirectory(Path.Combine(plain, "data"));
        Assert.Equal(before, CartIdentity.Compute(CartSource.Load(plain)));

        WriteBank(plain, 0, new byte[] { 42 });
        byte[] after = CartIdentity.Compute(CartSource.Load(plain));
        Assert.NotEqual(before, after);

        // And two different bank contents are two different cartridges.
        WriteBank(plain, 0, new byte[] { 43 });
        Assert.NotEqual(after, CartIdentity.Compute(CartSource.Load(plain)));
    }
}
