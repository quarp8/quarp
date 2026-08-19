using System.Globalization;
using System.Text;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// Pins the one hash the milestone rests on. <see cref="FrameHash"/> replaced three
/// separately-maintained FNV-1a copies (CLI, Core tests, CartKit tests); these tests exist so
/// the survivor is nailed to the published FNV reference instead of to whatever the other two
/// happened to agree on.
///
/// <para>The reference vectors are the endianness proof. They are constants of the FNV-1a 64
/// specification, not of this machine: an implementation that reinterpreted the input as
/// machine words instead of walking bytes would reproduce them on one architecture and miss
/// them on the other — which is exactly the failure the M2 cross-architecture job is meant to
/// catch, discovered here in milliseconds rather than in CI as a fake determinism bug.</para>
/// </summary>
public class FrameHashTests
{
    /// <summary>
    /// FNV-1a 64 of an all-zero QUARP-8 frame — 160 x 90 = 14400 zero bytes since ADR-021 moved
    /// the screen (it read f3fb6a6deb5af325 while the frame was 128 x 72 = 9216 bytes).
    ///
    /// <para>It is neither an anchor of the milestone nor a digest of silence: those are pinned in
    /// docs/PLAYBOOK.md §4 and do not move with the screen. This is the "nothing was drawn" value,
    /// and it is here rather than in two places because <c>AudioBlockTests</c> asserts the same
    /// number to show that teaching <see cref="FrameHash"/> about audio did not disturb the frame
    /// path — one fact, one owner.</para>
    /// </summary>
    internal const string EmptyProfile8Frame = "2642655708b56825";

    [Theory]
    // FNV-1a 64 reference vectors: the offset basis is the hash of the empty input, and the
    // rest are the standard published cases.
    [InlineData("", "cbf29ce484222325")]
    [InlineData("a", "af63dc4c8601ec8c")]
    [InlineData("foobar", "85944171f73967e8")]
    [InlineData("The quick brown fox jumps over the lazy dog", "f3f9b7f5e7e47110")]
    public void MatchesTheFnv1aReferenceVectors(string text, string expected)
    {
        Assert.Equal(expected, FrameHash.Of(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void ReadsBytesInOrder()
    {
        // Not a tautology: a hash that folded four bytes into a word at a time would give the
        // same answer for these two on one architecture and a different one on the other.
        Assert.NotEqual(
            FrameHash.Compute(new byte[] { 1, 2, 3, 4 }),
            FrameHash.Compute(new byte[] { 4, 3, 2, 1 }));
    }

    [Fact]
    public void AnEmptyProfile8FrameHashesToAKnownConstant()
    {
        // 160 x 90 zero bytes. Guards the framebuffer's size as much as the hash: a profile that
        // quietly changed dimensions would land here first — which is precisely what happened when
        // ADR-021 changed it on purpose, and this is the test that said so.
        var framebuffer = new Framebuffer(ConsoleProfile.Profile8);
        Assert.Equal(160 * 90, framebuffer.Pixels.Length);
        Assert.Equal(EmptyProfile8Frame, FrameHash.Of(framebuffer));
    }

    [Fact]
    public void EveryOverloadAgrees()
    {
        var framebuffer = new Framebuffer(ConsoleProfile.Profile8);
        framebuffer.FillRect(3, 5, 17, 11, 9);

        ulong raw = FrameHash.Compute(framebuffer);
        Assert.Equal(raw, FrameHash.Compute(framebuffer.Pixels));
        Assert.Equal(FrameHash.Format(raw), FrameHash.Of(framebuffer));
        Assert.Equal(FrameHash.Format(raw), FrameHash.Of(framebuffer.Pixels));
    }

    [Theory]
    // The shape `.github/workflows/ci.yml` greps for: ^[0-9a-f]{16}$. Leading zeros are kept
    // and there is no 0x prefix — a hash that shrank to 15 digits would vanish from the CI
    // comparison instead of failing it.
    [InlineData(0UL, "0000000000000000")]
    [InlineData(1UL, "0000000000000001")]
    [InlineData(0x37c481f3e17fab02UL, "37c481f3e17fab02")]
    [InlineData(ulong.MaxValue, "ffffffffffffffff")]
    public void TextFormIsSixteenLowercaseHexDigits(ulong hash, string expected)
    {
        string text = FrameHash.Format(hash);
        Assert.Equal(expected, text);
        Assert.Equal(FrameHash.HexLength, text.Length);
        Assert.All(text, c => Assert.True(
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), $"'{c}' is outside [0-9a-f]"));
    }

    [Fact]
    public void TextFormIgnoresTheAmbientCulture()
    {
        // Formatting pins InvariantCulture rather than inheriting the thread's. Rather than
        // ask for a named culture — the solution builds with InvariantGlobalization, so those
        // are not loadable — mangle a clone of the invariant one and check nothing leaks.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            hostile.NumberFormat.NativeDigits =
                new[] { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
            hostile.NumberFormat.DigitSubstitution = DigitShapes.NativeNational;
            hostile.NumberFormat.NegativeSign = "!";
            hostile.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = hostile;

            Assert.Equal("37c481f3e17fab02", FrameHash.Format(0x37c481f3e17fab02UL));
            Assert.Equal("cbf29ce484222325", FrameHash.Of(ReadOnlySpan<byte>.Empty));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void HashingAllocatesNothing()
    {
        // The hash is taken on the frame path in headless runs; it must not add garbage.
        var framebuffer = new Framebuffer(ConsoleProfile.Profile8);
        for (int i = 0; i < 8; i++)
        {
            FrameHash.Compute(framebuffer);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong sink = 0;
        for (int i = 0; i < 64; i++)
        {
            sink ^= FrameHash.Compute(framebuffer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"FrameHash.Compute allocated {allocated} bytes over 64 calls");
        Assert.Equal(0UL, sink);   // 64 identical values xored away; keeps the loop alive
    }
}
