using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The deterministic RNG (SPEC-8 §7): xoshiro128** seeded through splitmix64. Verified
/// against an independent reimplementation of both algorithms, including the published
/// splitmix64 test vector, so the console cannot silently drift from the spec.
/// </summary>
public class RngTests
{
    private static VirtualConsole NewConsole() => new(ConsoleProfile.Profile8);

    // --- reference implementations (spec copies, kept separate from production code) ---

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private sealed class ReferenceXoshiro
    {
        private uint _s0, _s1, _s2, _s3;

        public ReferenceXoshiro(int seed)
        {
            ulong state = unchecked((ulong)(long)seed);
            ulong a = SplitMix64(ref state);
            ulong b = SplitMix64(ref state);
            _s0 = (uint)a;
            _s1 = (uint)(a >> 32);
            _s2 = (uint)b;
            _s3 = (uint)(b >> 32);
            if ((_s0 | _s1 | _s2 | _s3) == 0)
            {
                _s3 = 1;
            }
        }

        public uint Next()
        {
            unchecked
            {
                static uint Rotl(uint v, int k) => (v << k) | (v >> (32 - k));
                uint result = Rotl(_s1 * 5, 7) * 9;
                uint t = _s1 << 9;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 11);
                return result;
            }
        }

        public int NextInt(int maxExclusive) => (int)((ulong)Next() * (uint)maxExclusive >> 32);
    }

    [Fact]
    public void SplitMix64ReferenceMatchesPublishedVector()
    {
        // Known first outputs of splitmix64 for state 0 — anchors the reference itself.
        ulong state = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, SplitMix64(ref state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RndIntMatchesReferenceImplementation(int seed)
    {
        var console = NewConsole();
        console.Srand(seed);
        var reference = new ReferenceXoshiro(seed);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(reference.NextInt(1000000), console.RndInt(1000000));
        }
    }

    [Fact]
    public void SameSeedGivesSameSequenceAcrossConsoles()
    {
        var a = NewConsole();
        var b = NewConsole();
        a.Srand(12345);
        b.Srand(12345);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.RndInt(int.MaxValue), b.RndInt(int.MaxValue));
        }
    }

    [Fact]
    public void ReseedingRestartsTheSequence()
    {
        var console = NewConsole();
        console.Srand(7);
        var first = new int[100];
        for (int i = 0; i < first.Length; i++)
        {
            first[i] = console.RndInt(int.MaxValue);
        }
        console.Srand(7);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i], console.RndInt(int.MaxValue));
        }
    }

    [Fact]
    public void SeedZeroProducesAHealthySequence()
    {
        // Srand(0) must not collapse to a degenerate state: splitmix64 expands 0 into
        // non-zero xoshiro state, so draws vary.
        var console = NewConsole();
        console.Srand(0);
        var seen = new HashSet<int>();
        for (int i = 0; i < 100; i++)
        {
            seen.Add(console.RndInt(1 << 30));
        }
        Assert.True(seen.Count > 90, $"seed 0 produced only {seen.Count} distinct values in 100 draws");
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var a = NewConsole();
        var b = NewConsole();
        a.Srand(1);
        b.Srand(2);
        bool anyDifferent = false;
        for (int i = 0; i < 20; i++)
        {
            if (a.RndInt(int.MaxValue) != b.RndInt(int.MaxValue))
            {
                anyDifferent = true;
            }
        }
        Assert.True(anyDifferent);
    }

    [Fact]
    public void RndIntStaysInRangeAndHandlesDegenerateMax()
    {
        var console = NewConsole();
        console.Srand(99);
        for (int max = 1; max <= 10; max++)
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.InRange(console.RndInt(max), 0, max - 1);
            }
        }
        Assert.Equal(0, console.RndInt(1));
        Assert.Equal(0, console.RndInt(0));
        Assert.Equal(0, console.RndInt(-5));
    }

    [Fact]
    public void RndFixStaysInRangeAndHandlesDegenerateMax()
    {
        var console = NewConsole();
        console.Srand(99);
        for (int i = 0; i < 1000; i++)
        {
            Fix value = console.Rnd(Fix.One);
            Assert.True(value >= Fix.Zero && value < Fix.One, $"Rnd(1) returned {value}");
        }
        Assert.Equal(Fix.Zero, console.Rnd(Fix.Zero));
        Assert.Equal(Fix.Zero, console.Rnd((Fix)(-3)));
    }

    [Fact]
    public void RndAndRndIntEachConsumeExactlyOneDraw()
    {
        var a = NewConsole();
        var b = NewConsole();
        a.Srand(555);
        b.Srand(555);
        a.RndInt(10);       // one draw
        b.Rnd(Fix.One);     // one draw
        Assert.Equal(a.RndInt(int.MaxValue), b.RndInt(int.MaxValue)); // streams stay aligned
    }

    [Fact]
    public void RndIntDegenerateMaxConsumesNoDraw()
    {
        var a = NewConsole();
        var b = NewConsole();
        a.Srand(8);
        b.Srand(8);
        a.RndInt(0);
        a.RndInt(-1);
        a.Rnd(Fix.Zero);    // all early-out before touching the state
        Assert.Equal(b.RndInt(int.MaxValue), a.RndInt(int.MaxValue));
    }

    [Fact]
    public void AttachCartResetsSeedToZero()
    {
        var a = NewConsole();
        a.Srand(777);
        a.RndInt(100);
        a.AttachCart(new NopCart());        // reset to Srand(0)
        var b = NewConsole();
        b.Srand(0);
        Assert.Equal(b.RndInt(int.MaxValue), a.RndInt(int.MaxValue));
    }

    private sealed class NopCart : Cartridge
    {
    }
}
