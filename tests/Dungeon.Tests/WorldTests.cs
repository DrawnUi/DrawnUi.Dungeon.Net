using DungeonRun;
using Xunit;

namespace Dungeon.Tests;

/// <summary>The unit tests of examples/dungeon/src/scene.rs, plus the hash pinned to values of the Rust formula.</summary>
public class WorldTests
{
    /// <summary>The title's player survives by the rules a real player dies by, at the speeds it runs at.</summary>
    [Fact]
    public void ThePilotSurvives()
    {
        var world = new World();
        world.Start();
        for (int frame = 0; frame < 60 * 30; frame++)
        {
            var input = world.Pilot(1.0f / 60.0f);
            world.Step(1.0f / 60.0f, ref input);
            Assert.True(world.Hits == 0, $"hit at {world.Z} (row {World.GetRow((long)(world.Z / 2.0))})");
        }
        Assert.True(world.Orbs > 20, $"orbs {world.Orbs}");
    }

    /// <summary>Pillars never close the lane the orbs before them lead to.</summary>
    [Fact]
    public void PillarsLeaveTheOrbLaneFree()
    {
        for (long i = 0; i < 5000; i++)
        {
            var row = World.GetRow(i * 5);
            if (row.Kind == RowKind.Pillars)
            {
                Assert.Equal(0, row.Mask & (1 << (World.OrbLane(i - 1) + 1)));
                Assert.NotEqual(0b111, row.Mask);
            }
        }
    }

    /// <summary>
    /// hash(i, salt) and hash01 bit for bit: the values come from the Rust formula (u64 wrapping multiplies and
    /// xor-shifts, `(x >> 33) as u32`) evaluated with exact 64-bit integer math.
    /// </summary>
    [Theory]
    [InlineData(0L, 0UL, 0U, 0.0f)]
    [InlineData(1L, 1UL, 2024154872U, 0.1522216796875f)]
    [InlineData(5L, 2UL, 1511060883U, 0.9592742919921875f)]
    [InlineData(-7L, 3UL, 1473460281U, 0.2196197509765625f)]
    [InlineData(123456L, 6UL, 195828540U, 0.10638427734375f)]
    [InlineData(1499L, 9UL, 135605569U, 0.1767730712890625f)]
    [InlineData(-1L, 10UL, 1014960292U, 0.06500244140625f)]
    [InlineData(42L, 5UL, 1156144076U, 0.35858154296875f)]
    public void HashMatchesRust(long i, ulong salt, uint expected, float expected01)
    {
        Assert.Equal(expected, World.Hash(i, salt));
        Assert.Equal(expected01, World.Hash01(i, salt));
    }

    /// <summary>Floor division and modulo for negative numbers, as Rust's div_euclid / rem_euclid.</summary>
    [Theory]
    [InlineData(-1L, -1L, 4L)]
    [InlineData(-5L, -1L, 0L)]
    [InlineData(-6L, -2L, 4L)]
    [InlineData(7L, 1L, 2L)]
    public void EuclidMatchesRust(long a, long div, long rem)
    {
        Assert.Equal(div, World.DivEuclid(a, 5));
        Assert.Equal(rem, World.RemEuclid(a, 5));
    }

    /// <summary>The row cache returns what the rules compute, whatever order rows are asked in.</summary>
    [Fact]
    public void RowCacheIsTransparent()
    {
        var forward = new Row[3000];
        for (long i = 0; i < forward.Length; i++)
            forward[i] = World.GetRow(i);
        for (long i = forward.Length - 1; i >= 0; i -= 7)
            Assert.Equal(forward[i], World.GetRow(i));
    }
}
