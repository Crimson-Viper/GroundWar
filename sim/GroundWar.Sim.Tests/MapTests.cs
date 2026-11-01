using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Map;
using Xunit;

namespace GroundWar.Sim.Tests;

public class MapTests
{
    private static BattleMap Small() => new(TestData.SmallMap(), TestData.Data.Rules.Combat);

    [Fact]
    public void SegmentDistanceIsZeroWhenCrossing()
    {
        float d = Geo.SegmentSegmentDistSq(new(0, 0), new(10, 10), new(0, 10), new(10, 0), out _, out _);
        Assert.True(d < 1e-6f);
    }

    [Fact]
    public void HighWallBlocksSightLowWallDoesNot()
    {
        var map = Small();
        Assert.True(map.SightBlocked(new(40, 70), new(60, 70)));
        Assert.False(map.SightBlocked(new(40, 30), new(60, 30)));
        Assert.False(map.SightBlocked(new(40, 50), new(60, 50)));
    }

    [Fact]
    public void LowWallProtectsOnlyFromTheFarSide()
    {
        var map = Small();
        var behindWall = new Vector2(51f, 30f); // just east of the wall
        Assert.True(map.ProtectionAt(behindWall, new Vector2(10, 30)) > 0.5f);   // fire from the west: covered
        Assert.Equal(0f, map.ProtectionAt(behindWall, new Vector2(90, 30)));     // fire from the east: exposed
    }

    [Fact]
    public void CraterProtectsFromAllSides()
    {
        var map = Small();
        var inCrater = new Vector2(21, 50);
        Assert.True(map.ProtectionAt(inCrater, new Vector2(80, 50)) > 0.5f);
        Assert.True(map.ProtectionAt(inCrater, new Vector2(20, 5)) > 0.5f);
        Assert.Equal(0f, map.ProtectionAt(new Vector2(30, 50), new Vector2(80, 50)));
    }

    [Fact]
    public void CoverPointsAreGeneratedOnBothSidesOfWallsAndInCraters()
    {
        var map = Small();
        Assert.Contains(map.CoverPoints, c => c.Kind == CoverKind.LowWall && c.Position.X < 50);
        Assert.Contains(map.CoverPoints, c => c.Kind == CoverKind.LowWall && c.Position.X > 50);
        Assert.Contains(map.CoverPoints, c => c.Kind == CoverKind.HighWall && !c.CanFire);
        Assert.Contains(map.CoverPoints, c => c.Kind == CoverKind.Crater);
        Assert.All(map.CoverPoints, c => Assert.True(map.Nav.IsWalkable(c.Position)));
    }

    [Fact]
    public void PathGoesAroundWalls()
    {
        var map = Small();
        var path = map.Nav.FindPath(new(45, 30), new(55, 30));
        Assert.NotNull(path);
        var points = new List<Vector2> { new(45, 30) };
        points.AddRange(path!);
        for (int i = 1; i < points.Count; i++)
            Assert.True(map.Nav.StraightLineClear(points[i - 1], points[i]), $"leg {i} cuts through a wall");
        Assert.True(points.Any(p => p.Y < 20f || p.Y > 40f), "path should go round the end of the wall");
    }

    [Fact]
    public void GridScoresCoverAndVisibility()
    {
        var map = Small();
        var grid = new BattleGrid(map);
        int nearWall = grid.IndexOf(new Vector2(51, 30));
        int open = grid.IndexOf(new Vector2(80, 10));
        Assert.True(grid.Cover[nearWall] > 0.5f);
        Assert.Equal(0f, grid.Cover[open]);
        Assert.True(grid.Visibility[open] > grid.Visibility[grid.IndexOf(new Vector2(51, 70))]);
    }
}
