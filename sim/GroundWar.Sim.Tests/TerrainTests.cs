using System.Numerics;
using GroundWar.Sim.Data;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>The ground: hills, woods, hedgerows and crops (docs/systems/terrain.md).</summary>
public class TerrainTests
{
    private static BattleMap Make(Action<MapDef> build)
    {
        var def = new MapDef { Name = "t", Width = 300, Height = 200 };
        build(def);
        return new BattleMap(def, TestData.Data.Rules.Combat, TestData.Data.Rules.Terrain);
    }

    private static AreaDef Rect(string kind, float x0, float y0, float x1, float y1) => new()
    {
        Kind = kind,
        Points = { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } },
    };

    [Fact]
    public void AFlatMapBehavesAsBefore()
    {
        var map = Make(_ => { });
        Assert.True(map.Terrain.Flat);
        Assert.Equal(0f, map.Terrain.HeightAt(new Vector2(150, 100)));
        Assert.False(map.SightBlocked(new Vector2(10, 100), new Vector2(290, 100)));
        Assert.Equal(1f, map.SpeedFactor(new Vector2(150, 100), Vector2.UnitX));
        Assert.Equal(1f, map.Concealment(new Vector2(150, 100), 2));
    }

    [Fact]
    public void ACrestHidesWhatIsBehindItAndStopsBullets()
    {
        var map = Make(d => d.Ridges.Add(new RidgeDef { A = new[] { 150f, 0f }, B = new[] { 150f, 200f }, Width = 40, Height = 6 }));
        Vector2 west = new(60, 100), east = new(240, 100), top = new(150, 100);
        Assert.InRange(map.Terrain.HeightAt(top), 5.9f, 6.1f);
        Assert.True(map.SightBlocked(west, east), "men either side of a ridge can't see each other");
        Assert.True(map.WallBlocked(west, east), "and can't shoot each other");
        Assert.Equal(1f, map.ProtectionAt(east, west));
        Assert.False(map.SightBlocked(top, east), "the man on the crest sees down both sides");
        Assert.False(map.SightBlocked(top, west));
    }

    [Fact]
    public void AHollowIsDeadGround()
    {
        var map = Make(d => d.Hills.Add(new HillDef { Center = new[] { 150f, 100f }, Radius = 30, Height = -4 }));
        Assert.True(map.Terrain.HeightAt(new Vector2(150, 100)) < -3.9f);
        Assert.True(map.SightBlocked(new Vector2(20, 100), new Vector2(150, 100)), "a man in the hollow is out of sight from the level");
        Assert.False(map.SightBlocked(new Vector2(20, 100), new Vector2(280, 100)), "but the hollow doesn't block the view across it");
    }

    [Fact]
    public void WoodsHideAfterAFewTensOfMetresButDontStopBullets()
    {
        var map = Make(d => d.Areas.Add(Rect("woods", 100, 50, 200, 150)));
        Assert.True(map.Terrain.HasWoods);
        Assert.Equal(GroundKind.Woods, map.Terrain.KindAt(new Vector2(150, 100)));
        Assert.True(map.SightBlocked(new Vector2(50, 100), new Vector2(250, 100)), "can't see through 100 m of wood");
        Assert.False(map.WallBlocked(new Vector2(50, 100), new Vector2(250, 100)), "trees hide, they don't stop a bullet");
        Assert.False(map.SightBlocked(new Vector2(50, 100), new Vector2(112, 100)), "a man at the edge of the wood can be seen, and sees out");
        Assert.True(map.SightBlocked(new Vector2(50, 100), new Vector2(160, 100)), "a man deep in the wood can't");
        Assert.False(map.SightBlocked(new Vector2(140, 100), new Vector2(160, 100)), "men close together in the wood see each other");
        Assert.True(map.ProtectionAt(new Vector2(150, 100), new Vector2(50, 100)) >= TestData.Data.Rules.Terrain.WoodsProtection);
        Assert.True(map.Concealment(new Vector2(150, 100), 0) < 1f);
    }

    [Fact]
    public void TreesAreInTheSamePlacesEveryTimeAndGiveCover()
    {
        Action<MapDef> build = d => { d.Areas.Add(Rect("woods", 100, 50, 200, 150)); d.Trees.Add(new[] { 40f, 40f }); };
        var a = Make(build); var b = Make(build);
        Assert.True(a.Terrain.Trees.Count > 300);
        Assert.Equal(a.Terrain.Trees.Count, b.Terrain.Trees.Count);
        for (int i = 0; i < a.Terrain.Trees.Count; i++) Assert.Equal(a.Terrain.Trees[i].Position, b.Terrain.Trees[i].Position);
        Assert.Contains(a.CoverPoints, c => c.Kind == CoverKind.Tree);
        // The lone tree: a man behind it is covered from the far side, not from his own side.
        var tr = TestData.Data.Rules.Terrain;
        Assert.Equal(tr.TreeProtection, a.ProtectionAt(new Vector2(39, 40), new Vector2(200, 40)));
        Assert.Equal(0f, a.ProtectionAt(new Vector2(39, 40), new Vector2(0, 40)));
    }

    [Fact]
    public void AHedgerowHidesUnlessYouAreUpAgainstIt()
    {
        var map = Make(d => d.Walls.Add(new WallDef { A = new[] { 150f, 20f }, B = new[] { 150f, 180f }, Thickness = 1.6f, Hedge = true }));
        Vector2 west = new(100, 100), east = new(200, 100), atHedge = new(148.6f, 100);
        Assert.True(map.SightBlocked(west, east), "a hedgerow hides what is beyond it");
        Assert.False(map.WallBlocked(west, east), "but doesn't stop a bullet");
        Assert.False(map.SightBlocked(atHedge, east), "a man lining the hedge looks through it");
        Assert.False(map.SightBlocked(east, atHedge), "and can be seen, if only just");
        Assert.True(map.Concealment(atHedge, 0) < 1f);
        Assert.Equal(TestData.Data.Rules.Terrain.HedgeProtection, map.ProtectionAt(atHedge, east));
        Assert.True(map.Nav.IsWalkable(new Vector2(150, 100)), "infantry can push through");
        Assert.True(map.SpeedFactor(new Vector2(150, 100), Vector2.UnitX) < 0.5f, "slowly");
        Assert.False(map.TankNav.IsWalkable(new Vector2(150, 100)), "a tank can't");
        Assert.Contains(map.CoverPoints, c => c.Kind == CoverKind.Hedge);
    }

    [Fact]
    public void TanksKeepOutOfWoods()
    {
        var map = Make(d => d.Areas.Add(Rect("woods", 100, 50, 200, 150)));
        Assert.False(map.TankNav.IsWalkable(new Vector2(150, 100)));
        Assert.True(map.TankNav.IsWalkable(new Vector2(50, 100)));
        Assert.True(map.Nav.IsWalkable(new Vector2(150, 100)));
    }

    [Fact]
    public void CropsHideAManLyingDownAndUphillIsSlower()
    {
        var map = Make(d =>
        {
            d.Areas.Add(Rect("crops", 20, 20, 120, 180));
            d.Ridges.Add(new RidgeDef { A = new[] { 250f, 0f }, B = new[] { 250f, 200f }, Width = 50, Height = 10 });
        });
        var inWheat = new Vector2(60, 100);
        Assert.Equal(1f, map.Concealment(inWheat, 0));
        Assert.True(map.Concealment(inWheat, 2) < map.Concealment(inWheat, 1));
        Assert.True(map.Concealment(inWheat, 1) < 1f);
        Assert.Equal(0f, map.ProtectionAt(inWheat, new Vector2(200, 100)));
        var slope = new Vector2(225, 100);
        Assert.True(map.SpeedFactor(slope, Vector2.UnitX) < 0.75f, "climbing");
        Assert.Equal(1f, map.SpeedFactor(slope, -Vector2.UnitX));
    }

    [Fact]
    public void TheGridKnowsHeightAndCountsWoodsAsCover()
    {
        var map = Make(d =>
        {
            d.Areas.Add(Rect("woods", 100, 50, 200, 150));
            d.Hills.Add(new HillDef { Center = new[] { 50f, 50f }, Radius = 40, Height = 8 });
        });
        var grid = new BattleGrid(map);
        Assert.True(grid.GroundHeight[grid.IndexOf(new Vector2(50, 50))] > 7f);
        Assert.True(grid.Cover[grid.IndexOf(new Vector2(150, 100))] >= 0.4f);
        Assert.True(grid.Visibility[grid.IndexOf(new Vector2(150, 100))] < grid.Visibility[grid.IndexOf(new Vector2(250, 180))]);
    }

    [Fact]
    public void TheFarmhouseSitsOnARidgeWithACoveredWayRoundTheNorth()
    {
        var b = Battle.Load(TestData.Source, "scenarios/farmhouse.json", 1);
        var t = b.Map.Terrain;
        Assert.False(t.Flat);
        Assert.True(t.HeightAt(new Vector2(299, 150)) > t.HeightAt(new Vector2(200, 150)) + 4f, "the farm is on high ground");
        // From the farmhouse the open wheat is in view; the lane behind the north hedgerow is not.
        var farm = new Vector2(291, 145);
        Assert.False(b.Map.SightBlocked(farm, new Vector2(200, 160)));
        Assert.True(b.Map.SightBlocked(farm, new Vector2(200, 95)));
    }

    [Fact]
    public void SoldiersStandOnTheGroundAndBattlesStillReplayTheSame()
    {
        var a = Battle.Load(TestData.Source, "scenarios/farmhouse.json", 3).RunToEnd();
        var b = Battle.Load(TestData.Source, "scenarios/farmhouse.json", 3).RunToEnd();
        Assert.Equal(a.Winner, b.Winner);
        Assert.Equal(a.Time, b.Time);
    }
}
