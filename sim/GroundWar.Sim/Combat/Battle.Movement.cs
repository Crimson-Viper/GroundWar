using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

public sealed partial class Battle
{
    private void Move(Soldier s)
    {
        if (s.Path == null || s.MoveMode == MoveMode.None) return;
        var sr = Rules.Soldier;
        float speed = s.MoveMode switch
        {
            MoveMode.Run => sr.RunSpeed,
            MoveMode.Crawl => sr.CrawlSpeed,
            _ => sr.WalkSpeed,
        };
        if (Map.InCrater(s.Position)) speed *= sr.CraterSpeedFactor;
        if (s.MoraleState == MoraleState.Shaken && s.Action != SoldierAction.Flee) speed *= 0.9f;
        speed *= Map.SpeedFactor(s.Position, Core.Geo.SafeNormalize(s.Path[s.PathIndex] - s.Position, s.Facing));
        s.Stance = s.MoveMode == MoveMode.Crawl ? Stance.Prone : Stance.Standing;

        float remaining = speed * Dt;
        while (remaining > 0f && s.Path != null)
        {
            var target = s.Path[s.PathIndex];
            var delta = target - s.Position;
            float dist = delta.Length();
            if (dist <= remaining)
            {
                s.Position = target;
                remaining -= dist;
                s.PathIndex++;
                if (s.PathIndex >= s.Path.Count)
                {
                    s.Path = null;
                    s.MoveMode = MoveMode.None;
                    s.Destination = null;
                    _brain.OnArrived(s);
                }
            }
            else
            {
                s.Facing = delta / dist;
                s.Position += s.Facing * remaining;
                remaining = 0f;
            }
        }

        if (s.Action == SoldierAction.Flee && DistanceToRearEdge(s) <= Rules.Ai.MapEdgeRoutDistance)
        {
            s.Status = SoldierStatus.Routed;
            s.Path = null;
            s.MoveMode = MoveMode.None;
            s.Thought = "Routed off the field";
            ReleaseCover(s);
            Note(s, "has fled the battlefield");
        }
    }

    internal float DistanceToRearEdge(Soldier s)
    {
        var r = s.Group.Rear;
        float best = float.MaxValue;
        if (r.X < -0.3f) best = MathF.Min(best, s.Position.X);
        if (r.X > 0.3f) best = MathF.Min(best, Map.Width - s.Position.X);
        if (r.Y < -0.3f) best = MathF.Min(best, s.Position.Y);
        if (r.Y > 0.3f) best = MathF.Min(best, Map.Height - s.Position.Y);
        return best;
    }

    internal Vector2 RearEdgePoint(Soldier s)
    {
        var r = s.Group.Rear;
        var p = s.Position + r * 1000f;
        return Map.Clamp(p, 1f);
    }

    /// <summary>Send a soldier along a path. Returns false if the spot can't be reached.</summary>
    internal bool SetMove(Soldier s, Vector2 destination, MoveMode mode)
    {
        var path = Map.Nav.FindPath(s.Position, destination);
        if (path == null || path.Count == 0) return false;
        s.Path = path;
        s.PathIndex = 0;
        s.MoveMode = mode;
        s.Destination = path[^1];
        return true;
    }

    internal void StopMoving(Soldier s)
    {
        s.Path = null;
        s.MoveMode = MoveMode.None;
        s.Destination = null;
    }
}
