using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

public sealed partial class Battle
{
    /// <summary>Suppression fades once the shooting stops. Morale slowly recovers in safety,
    /// faster in cover and near a calm sergeant, and drains when a man is alone.</summary>
    private void UpdateMorale(Soldier s)
    {
        var mr = Rules.Morale;
        if (Time - s.LastSuppressedTime > mr.SuppressionDecayDelay)
            s.Suppression = MathF.Max(0f, s.Suppression - mr.SuppressionDecayPerSecond * Dt);

        if (s.UnderFire(Time, Rules.Ai.UnderFireWindow)) s.TimeUnderFire += Dt;
        if (s.InCover) s.TimeInCover += Dt;

        if (s.Suppression < mr.QuietSuppression)
        {
            float rate = mr.RecoverPerSecond;
            if (s.LastProtection >= 0.5f) rate += mr.RecoverInCoverPerSecond;
            var leader = s.Group.Leader;
            if (leader != null && leader != s && leader.IsEffective &&
                Vector2.DistanceSquared(leader.Position, s.Position) < mr.LeaderRadius * mr.LeaderRadius)
                rate += mr.RecoverLeaderNearbyPerSecond;
            s.Morale += rate * Dt;
        }

        if (!HasFriendNear(s, mr.AloneRadius)) s.Morale -= mr.AloneDrainPerSecond * Dt;
        if (s.Magazine == 0 && s.SpareMagazines == 0 && s.ReloadTimer <= 0f) s.Morale -= mr.NoAmmoDrainPerSecond * Dt;
        s.Morale = Math.Clamp(s.Morale, 0f, s.StartMorale);
        UpdateMoraleState(s);
    }

    private bool HasFriendNear(Soldier s, float radius)
    {
        float r2 = radius * radius;
        foreach (var f in _soldiers)
            if (f != s && f.Side == s.Side && f.IsActive && Vector2.DistanceSquared(f.Position, s.Position) < r2) return true;
        return false;
    }

    private void UpdateMoraleState(Soldier s)
    {
        var mr = Rules.Morale;
        var old = s.MoraleState;
        MoraleState next;
        if (old == MoraleState.Broken)
        {
            if (s.Morale <= mr.RallyAbove || NearestKnownEnemy(s) < mr.RallySafeDistance) return;
            next = Classify(s);
            Note(s, "rallies");
        }
        else if (s.Morale < mr.BrokenBelow)
        {
            next = MoraleState.Broken;
            Note(s, $"breaks and runs (morale {s.Morale:0})");
        }
        else next = Classify(s);

        if (next == old) return;
        s.MoraleState = next;
        if (next == MoraleState.Broken) s.NextThink = Time; // react now
    }

    private MoraleState Classify(Soldier s)
    {
        var mr = Rules.Morale;
        if (s.Morale < mr.PinnedBelow || s.Suppression >= mr.SuppressionPinned) return MoraleState.Pinned;
        if (s.Morale < mr.ShakenBelow || s.Suppression >= mr.SuppressionShaken) return MoraleState.Shaken;
        return MoraleState.Steady;
    }

    internal float NearestKnownEnemy(Soldier s)
    {
        float best = float.MaxValue;
        foreach (var c in _knowledge[(int)s.Side].Contacts)
            best = MathF.Min(best, Vector2.Distance(c.LastPosition, s.Position));
        return best;
    }
}
