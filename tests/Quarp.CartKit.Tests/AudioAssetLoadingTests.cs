using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// How the audio banks reach the console: loaded from a cart folder and from a .quarp8 package,
/// validated with the same <see cref="CartLoadException"/> style as every other asset, absent
/// meaning silence rather than failure — and folded into the cartridge identity, because since
/// M3 the sound is part of what a replay reproduces.
/// </summary>
public class AudioAssetLoadingTests : IDisposable
{
    private readonly string _root;

    public AudioAssetLoadingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-audio-tests-" + Guid.NewGuid().ToString("N"));
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
        File.WriteAllText(Path.Combine(folder, "manifest.json"), """{"name":"audio-cart","author":"","profile":8}""");
        File.WriteAllText(
            Path.Combine(folder, "src", "main.cs"),
            "using Quarp.Api;\npublic sealed class TestCart : Cartridge { }\n");
        return folder;
    }

    private static byte[] BlipPayload()
    {
        byte[] payload = AudioFormat.EmptySfxPayload();
        AudioFormat.WriteSlotHeader(payload, 0, speed: 3, length: 2, loopStart: 0, loopEnd: 0);
        AudioFormat.WriteStep(payload, 0, 0, AudioFormat.PackStep(48, AudioFormat.WavePulse50, 6, 0));
        AudioFormat.WriteStep(payload, 0, 1, AudioFormat.PackStep(52, AudioFormat.WavePulse50, 5, 0));
        return payload;
    }

    private static byte[] SongPayload()
    {
        byte[] payload = AudioFormat.EmptyMusicPayload();
        AudioFormat.WritePatternChannel(payload, 0, 0, 0);
        AudioFormat.WritePatternFlags(payload, 0, AudioFormat.PatternFlagLoopStart);
        return payload;
    }

    // --- folder shape ---

    [Fact]
    public void ACartWithoutAudioIsSilentNotBroken()
    {
        CartData data = CartSource.Load(MakeCartFolder());
        Assert.Equal(AudioFormat.SfxPayloadSize, data.Sfx.Length);
        Assert.Equal(AudioFormat.MusicPayloadSize, data.Music.Length);
        Assert.All(data.Sfx, b => Assert.Equal(0, b));
        Assert.All(data.Music, b => Assert.Equal(0, b));
    }

    [Fact]
    public void BanksLoadFromAFolderWithTheirHeadersStripped()
    {
        string folder = MakeCartFolder();
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), AudioFormat.WriteSfxFile(BlipPayload()));
        File.WriteAllBytes(Path.Combine(folder, "music.bin"), AudioFormat.WriteMusicFile(SongPayload()));

        CartData data = CartSource.Load(folder);

        // What the console gets is the payload, not the file: no magic, no version.
        Assert.Equal(AudioFormat.SfxPayloadSize, data.Sfx.Length);
        Assert.Equal(BlipPayload(), data.Sfx);
        Assert.Equal(2, AudioFormat.SlotLength(data.Sfx, 0));
        Assert.Equal(3, AudioFormat.SlotSpeed(data.Sfx, 0));
        Assert.Equal(0, AudioFormat.PatternChannel(data.Music, 0, 0));
    }

    [Fact]
    public void AWrongSizedBankNamesTheFile()
    {
        string folder = MakeCartFolder();
        byte[] file = AudioFormat.WriteSfxFile(BlipPayload());
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), file[..100]);

        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("sfx.bin", e.Message);

        // Control: the very same bank at its full length loads.
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), file);
        CartSource.Load(folder);
    }

    [Fact]
    public void ACorruptStepInsideTheBankIsCaughtAtLoad()
    {
        string folder = MakeCartFolder();
        byte[] file = AudioFormat.WriteSfxFile(BlipPayload());
        CartSource.Load(folder);                              // control: no bank at all is fine

        file[AudioFormat.HeaderSize + AudioFormat.StepOffset(0, 1) + 1] |= 0x80;
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), file);
        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("slot 0 step 1", e.Message);
    }

    [Fact]
    public void AudioTextWithoutTheCompiledBankIsAnErrorThatNamesTheCommand()
    {
        string folder = MakeCartFolder();
        File.WriteAllText(Path.Combine(folder, "sfx.txt"), "sfx 0\n  00 C-4 tri 5 -\n");

        var e = Assert.Throws<CartLoadException>(() => CartSource.Load(folder));
        Assert.Contains("sfx.txt", e.Message);
        Assert.Contains("quarp audio build", e.Message);

        // Control: once the bank is built, the same folder loads.
        File.WriteAllBytes(
            Path.Combine(folder, "sfx.bin"),
            AudioFormat.WriteSfxFile(AudioTextCompiler.CompileSfx(
                File.ReadAllText(Path.Combine(folder, "sfx.txt")), "sfx.txt")));
        CartData data = CartSource.Load(folder);
        Assert.Equal(1, AudioFormat.SlotLength(data.Sfx, 0));
    }

    // --- package shape ---

    [Fact]
    public void PackagesCarryTheBanks()
    {
        string folder = MakeCartFolder();
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), AudioFormat.WriteSfxFile(BlipPayload()));
        File.WriteAllBytes(Path.Combine(folder, "music.bin"), AudioFormat.WriteMusicFile(SongPayload()));
        string package = Path.Combine(_root, "cart.quarp8");

        Quarp8Package.Pack(folder, package);
        CartData data = CartSource.Load(package);

        Assert.Equal(BlipPayload(), data.Sfx);
        Assert.Equal(SongPayload(), data.Music);
        // Folder and package are the same cartridge, audio included.
        Assert.Equal(CartIdentity.Compute(CartSource.Load(folder)), CartIdentity.Compute(data));
    }

    [Fact]
    public void APackageWithoutBanksIsSilent()
    {
        string folder = MakeCartFolder();
        string package = Path.Combine(_root, "silent.quarp8");
        Quarp8Package.Pack(folder, package);

        CartData data = CartSource.Load(package);
        Assert.All(data.Sfx, b => Assert.Equal(0, b));
        Assert.All(data.Music, b => Assert.Equal(0, b));
    }

    // --- identity ---

    [Fact]
    public void ChangingOneStepChangesTheCartridgeIdentity()
    {
        string folder = MakeCartFolder();
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), AudioFormat.WriteSfxFile(BlipPayload()));
        byte[] before = CartIdentity.Compute(CartSource.Load(folder));

        // Control: reloading the untouched cart gives the same identity.
        Assert.Equal(before, CartIdentity.Compute(CartSource.Load(folder)));

        byte[] altered = BlipPayload();
        AudioFormat.WriteStep(altered, 0, 1, AudioFormat.PackStep(53, AudioFormat.WavePulse50, 5, 0));
        File.WriteAllBytes(Path.Combine(folder, "sfx.bin"), AudioFormat.WriteSfxFile(altered));
        Assert.NotEqual(before, CartIdentity.Compute(CartSource.Load(folder)));
    }

    [Fact]
    public void ChangingOnePatternChangesTheCartridgeIdentity()
    {
        string folder = MakeCartFolder();
        File.WriteAllBytes(Path.Combine(folder, "music.bin"), AudioFormat.WriteMusicFile(SongPayload()));
        byte[] before = CartIdentity.Compute(CartSource.Load(folder));

        byte[] altered = SongPayload();
        AudioFormat.WritePatternChannel(altered, 0, 1, 7);
        File.WriteAllBytes(Path.Combine(folder, "music.bin"), AudioFormat.WriteMusicFile(altered));
        Assert.NotEqual(before, CartIdentity.Compute(CartSource.Load(folder)));
    }

    [Fact]
    public void AnAbsentBankHashesLikeAnEmptyOne()
    {
        // "No sfx.bin" and "an sfx.bin whose 64 slots are all empty" are the same cartridge to a
        // listener, so they must be the same cartridge to the identity too.
        string silent = MakeCartFolder("silent");
        string empty = MakeCartFolder("empty");
        File.WriteAllBytes(
            Path.Combine(empty, "sfx.bin"), AudioFormat.WriteSfxFile(AudioFormat.EmptySfxPayload()));
        File.WriteAllBytes(
            Path.Combine(empty, "music.bin"), AudioFormat.WriteMusicFile(AudioFormat.EmptyMusicPayload()));

        Assert.Equal(
            CartIdentity.Compute(CartSource.Load(silent)),
            CartIdentity.Compute(CartSource.Load(empty)));

        // Control: the same comparison with a bank that is not empty must fail.
        File.WriteAllBytes(Path.Combine(empty, "sfx.bin"), AudioFormat.WriteSfxFile(BlipPayload()));
        Assert.NotEqual(
            CartIdentity.Compute(CartSource.Load(silent)),
            CartIdentity.Compute(CartSource.Load(empty)));
    }
}
