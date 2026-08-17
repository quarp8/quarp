using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// SMath is table-driven Fix math (ADR-014): exact cardinal values, exact mirror
/// symmetries, and measured error bounds against double-precision references.
/// Doubles appear only here, in test expectations — never in the code under test.
/// </summary>
public class SMathTests
{
    private const int TurnRaw = 65536;

    // --- Sin / Cos: exact values ---

    [Fact]
    public void SinCardinalAnglesAreExact()
    {
        Assert.Equal(Fix.Zero, SMath.Sin(Fix.Zero));
        Assert.Equal(Fix.One, SMath.Sin(Fix.Ratio(1, 4)));
        Assert.Equal(Fix.Zero, SMath.Sin(Fix.Ratio(1, 2)));
        Assert.Equal(-Fix.One, SMath.Sin(Fix.Ratio(3, 4)));
        Assert.Equal(Fix.Zero, SMath.Sin(Fix.One));         // full turn wraps to 0
    }

    [Fact]
    public void CosCardinalAnglesAreExact()
    {
        Assert.Equal(Fix.One, SMath.Cos(Fix.Zero));
        Assert.Equal(Fix.Zero, SMath.Cos(Fix.Ratio(1, 4)));
        Assert.Equal(-Fix.One, SMath.Cos(Fix.Ratio(1, 2)));
        Assert.Equal(Fix.Zero, SMath.Cos(Fix.Ratio(3, 4)));
    }

    [Fact]
    public void SinWholeTurnsWrapAway()
    {
        for (int raw = 0; raw < TurnRaw; raw += 257)
        {
            var t = Fix.FromRaw(raw);
            Assert.Equal(SMath.Sin(t), SMath.Sin(t + 3));           // +3 whole turns
            Assert.Equal(SMath.Sin(t), SMath.Sin(t - 5));           // -5 whole turns
        }
        Assert.Equal(-Fix.One, SMath.Sin(Fix.FromRaw(-TurnRaw / 4))); // Sin(-0.25) = -1
    }

    [Fact]
    public void SinMirrorSymmetriesAreExact()
    {
        var half = Fix.Ratio(1, 2);
        for (int raw = 0; raw < TurnRaw; raw++)
        {
            var t = Fix.FromRaw(raw);
            Fix s = SMath.Sin(t);
            Assert.Equal(s, SMath.Sin(half - t));   // sin(0.5 - t) = sin(t)
            Assert.Equal(-s, SMath.Sin(half + t));  // sin(0.5 + t) = -sin(t)
            Assert.Equal(-s, SMath.Sin(-t));        // odd function
        }
    }

    [Fact]
    public void SinMaxErrorStaysInMeasuredBound()
    {
        // ADR-014: measured max error ~2.2e-5, i.e. ~1.5 raw Q16.16 units.
        int maxErrorRaw = 0;
        for (int raw = 0; raw < TurnRaw; raw++)
        {
            int actual = SMath.Sin(Fix.FromRaw(raw)).Raw;
            double exact = Math.Sin(raw * (2 * Math.PI / TurnRaw)) * TurnRaw;
            int error = Math.Abs(actual - (int)Math.Round(exact));
            maxErrorRaw = Math.Max(maxErrorRaw, error);
        }
        Assert.InRange(maxErrorRaw, 0, 2);
    }

    [Fact]
    public void CosMatchesShiftedSinEverywhere()
    {
        for (int raw = -TurnRaw; raw <= TurnRaw; raw += 111)
        {
            var t = Fix.FromRaw(raw);
            Assert.Equal(SMath.Sin(t + Fix.Ratio(1, 4)), SMath.Cos(t));
        }
    }

    // --- Sqrt ---

    [Fact]
    public void SqrtOfExactSquaresIsExact()
    {
        for (int n = 0; n <= 181; n++)      // 181^2 = 32761 is the largest square in range
        {
            Assert.Equal((Fix)n, SMath.Sqrt((Fix)(n * n)));
        }
    }

    [Fact]
    public void SqrtOfExactFractionalSquaresIsExact()
    {
        Assert.Equal(Fix.Half, SMath.Sqrt(Fix.Ratio(1, 4)));
        Assert.Equal(Fix.Ratio(3, 4), SMath.Sqrt(Fix.Ratio(9, 16)));
        Assert.Equal(Fix.Ratio(3, 2), SMath.Sqrt(Fix.Ratio(9, 4)));
    }

    [Fact]
    public void SqrtIsMonotonicNonDecreasing()
    {
        Fix previous = Fix.Zero;
        for (long raw = 0; raw <= int.MaxValue; raw += 65521) // prime step covers the range
        {
            Fix value = SMath.Sqrt(Fix.FromRaw((int)raw));
            Assert.True(value >= previous, $"Sqrt not monotonic at raw {raw}");
            previous = value;
        }
        Assert.True(SMath.Sqrt(Fix.MaxValue) >= previous);
    }

    [Fact]
    public void SqrtIsTheFloorOfTheTrueRoot()
    {
        // result = floor(sqrt(raw << 16)) exactly: r^2 <= raw<<16 < (r+1)^2.
        foreach (int raw in new[] { 1, 2, 3, 65535, 65536, 65537, 123456789, int.MaxValue })
        {
            ulong scaled = (ulong)raw << Fix.FracBits;
            ulong r = (uint)SMath.Sqrt(Fix.FromRaw(raw)).Raw;
            Assert.True(r * r <= scaled, $"raw {raw}: root too big");
            Assert.True((r + 1) * (r + 1) > scaled, $"raw {raw}: root too small");
        }
    }

    [Fact]
    public void SqrtOfNegativeAndZeroIsZero()
    {
        Assert.Equal(Fix.Zero, SMath.Sqrt(Fix.Zero));
        Assert.Equal(Fix.Zero, SMath.Sqrt((Fix)(-4)));
        Assert.Equal(Fix.Zero, SMath.Sqrt(Fix.MinValue));
    }

    // --- Atan2 ---

    [Fact]
    public void Atan2CardinalDirectionsAreExact()
    {
        Assert.Equal(Fix.Zero, SMath.Atan2(Fix.Zero, Fix.One));            // +x
        Assert.Equal(Fix.Ratio(1, 4), SMath.Atan2(Fix.One, Fix.Zero));     // +y
        Assert.Equal(Fix.Ratio(1, 2), SMath.Atan2(Fix.Zero, -Fix.One));    // -x
        Assert.Equal(Fix.Ratio(3, 4), SMath.Atan2(-Fix.One, Fix.Zero));    // -y
    }

    [Fact]
    public void Atan2DiagonalsAreExact()
    {
        Assert.Equal(Fix.Ratio(1, 8), SMath.Atan2(Fix.One, Fix.One));
        Assert.Equal(Fix.Ratio(3, 8), SMath.Atan2(Fix.One, -Fix.One));
        Assert.Equal(Fix.Ratio(5, 8), SMath.Atan2(-Fix.One, -Fix.One));
        Assert.Equal(Fix.Ratio(7, 8), SMath.Atan2(-Fix.One, Fix.One));
    }

    [Fact]
    public void Atan2OriginIsZeroByDefinition()
    {
        Assert.Equal(Fix.Zero, SMath.Atan2(Fix.Zero, Fix.Zero));
    }

    [Fact]
    public void Atan2ResultIsAlwaysInZeroToOneTurn()
    {
        for (int i = -8; i <= 8; i++)
        {
            for (int j = -8; j <= 8; j++)
            {
                if (i == 0 && j == 0)
                {
                    continue;
                }
                int raw = SMath.Atan2(Fix.Ratio(i, 3), Fix.Ratio(j, 5)).Raw;
                Assert.InRange(raw, 0, TurnRaw - 1);
            }
        }
    }

    [Fact]
    public void Atan2MaxErrorStaysUnderOneThousandthTurn()
    {
        int maxErrorRaw = 0;
        for (int i = -40; i <= 40; i++)
        {
            for (int j = -40; j <= 40; j++)
            {
                if (i == 0 && j == 0)
                {
                    continue;
                }
                var y = Fix.Ratio(i, 7);
                var x = Fix.Ratio(j, 9);
                int actual = SMath.Atan2(y, x).Raw;
                double turns = Math.Atan2(i / 7.0, j / 9.0) / (2 * Math.PI);
                if (turns < 0)
                {
                    turns += 1;
                }
                int expected = (int)Math.Round(turns * TurnRaw) & 0xFFFF;
                int error = Math.Abs(actual - expected);
                error = Math.Min(error, TurnRaw - error); // wrap-aware distance
                maxErrorRaw = Math.Max(maxErrorRaw, error);
            }
        }
        // The work order promises ~1e-3 turns; 1e-3 turns = 65.5 raw units.
        Assert.InRange(maxErrorRaw, 0, 65);
    }
}
