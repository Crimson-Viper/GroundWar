using GroundWar.Sim.Data;
using Xunit;

namespace GroundWar.Sim.Tests;

public class FairnessTests
{
    /// <summary>
    /// The meeting engagement compares equal squads, so its map must give both sides the same ground:
    /// every wall and crater needs a twin turned half a turn around the map centre.
    /// </summary>
    [Theory]
    [InlineData("maps/meeting_field.json")]
    [InlineData("maps/meeting_field_b.json")]
    [InlineData("maps/meeting_field_c.json")]
    [InlineData("maps/meeting_field_d.json")]
    [InlineData("maps/meeting_field_e.json")]
    [InlineData("maps/meeting_field_f.json")]
    public void MeetingFieldIsTheSameFromBothSides(string path)
    {
        var map = GameData.LoadMap(TestData.Source, path);
        float cx = map.Width, cy = map.Height;
        static bool Near(float[] p, float x, float y) => MathF.Abs(p[0] - x) < 0.01f && MathF.Abs(p[1] - y) < 0.01f;
        foreach (var w in map.Walls)
            Assert.Contains(map.Walls, o => o.High == w.High &&
                ((Near(o.A, cx - w.A[0], cy - w.A[1]) && Near(o.B, cx - w.B[0], cy - w.B[1])) ||
                 (Near(o.B, cx - w.A[0], cy - w.A[1]) && Near(o.A, cx - w.B[0], cy - w.B[1]))));
        foreach (var c in map.Craters)
            Assert.Contains(map.Craters, o => o.Radius == c.Radius && Near(o.Center, cx - c.Center[0], cy - c.Center[1]));
    }
}
