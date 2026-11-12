using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.AI;

/// <summary>
/// Soldier AI by utility scoring: each option (run to that wall, shoot that man, hug the dirt, push forward)
/// gets a score from a few simple formulas, and the best score wins. A small bonus for the current action
/// stops men flickering between choices.
/// </summary>
public sealed class SoldierBrain
{
    private readonly Battle _b;
    private readonly List<Vector2> _threats = new();
    private readonly List<(float d, Vector2 pos)> _nearest = new();
    private const int MaxThreats = 4;

    public SoldierBrain(Battle battle) { _b = battle; }

    private Data.AiRules Ai => _b.Rules.Ai;
    private Data.MoraleRules Mr => _b.Rules.Morale;

    private enum Option { Idle, TakeCover, CrawlToCover, Engage, AreaFire, FollowOrders, HugCover, Dig }

    public void Think(Soldier s)
    {
        bool realThreats = GatherThreats(s);
        float curProt = Protection(s.Position);
        s.LastProtection = realThreats ? curProt : 0f;

        if (s.MoraleState == MoraleState.Broken) { Flee(s); return; }

        long pt = Core.Profile.Start();
        s.Target = PickTarget(s);
        Core.Profile.Lap("think.target", pt);
        bool underFire = s.UnderFire(_b.Time, Ai.UnderFireWindow);

        // Already running somewhere? Keep going unless something makes that a bad idea.
        if (s.IsMoving)
        {
            if (s.Action == SoldierAction.TakeCover) return; // a man dashing for cover doesn't stop to think
            if (s.Action == SoldierAction.Advance && CanFollowMove(s) && s.Destination is Vector2 d &&
                Vector2.Distance(d, s.TaskPosition) <= s.TaskRadius + 2f)
                return;
            if (s.Action == SoldierAction.Flee) { _b.StopMoving(s); }
        }

        bool holding = IsHolding(s);
        bool farFromTask = Vector2.Distance(s.Position, s.TaskPosition) > s.TaskRadius + 1.5f;
        Option best = Option.Idle;
        float bestScore = 10f;
        CoverPoint? bestCover = null;
        Vector2 taskDest = default;
        CoverPoint? taskCover = null;

        void Consider(Option o, float score, SoldierAction asAction)
        {
            if (s.Action == asAction) score += Ai.StickinessBonus;
            if (score > bestScore) { bestScore = score; best = o; }
        }

        // 1. Pinned flat by fire.
        if (s.Suppression >= Mr.SuppressionNoFire)
        {
            Consider(Option.HugCover, 95f, SoldierAction.HugCover);
            if (curProt < 0.3f)
            {
                var c = FindCover(s, s.Position, Ai.SuppressedCoverSearchRadius);
                if (c != null && Protection(c.Position) - curProt > 0.3f)
                {
                    bestCover = c;
                    Consider(Option.CrawlToCover, 97f, SoldierAction.TakeCover);
                }
            }
        }

        long ct = Core.Profile.Start();
        // 2. Get into better cover: around where the squad wants him, or right here if he's under fire on the move.
        if ((realThreats || holding) && best != Option.CrawlToCover && !(s.TaskAssault && !holding))
        {
            bool local = !holding || underFire && farFromTask;
            var center = local ? s.Position : s.TaskPosition;
            float radius = local
                ? (s.Suppression >= Mr.SuppressionShaken ? Ai.SuppressedCoverSearchRadius * 1.5f : Ai.CoverSearchRadius * 0.6f)
                : s.TaskRadius + 1f; // stay inside the area he'd be sent back to (see farFromTask), or he flips between the two
            var c = FindCover(s, center, radius);
            if (c != null)
            {
                float gain = Protection(c.Position) - curProt;
                bool notInPosition = holding && !s.InCover;
                bool worthIt = gain > 0.25f || (notInPosition && gain > 0.05f);
                // Moving under orders and not being shot at: keep going rather than diving for every wall.
                if (!holding && !underFire) worthIt = false;
                if (worthIt && s.Suppression < Mr.SuppressionNoFire)
                {
                    bestCover = c;
                    Consider(Option.TakeCover, 60f + 40f * gain + (underFire ? 15f : 0f), SoldierAction.TakeCover);
                }
            }
        }
        Core.Profile.Lap("think.cover", ct);

        bool hasAmmo = s.Magazine > 0 || s.SpareMagazines > 0;
        // 3. Shoot back.
        if (s.Target != null && hasAmmo && s.Suppression < Mr.SuppressionNoFire)
            Consider(Option.Engage, 55f + 15f * curProt + (s.Weapon.IsMachineGun ? 5f : 0f), SoldierAction.Engage);
        // 3b. Nobody in sight, but the squad wants fire on a spot where the enemy was seen.
        else if (s.FireAt is Vector2 area && hasAmmo && s.Suppression < Mr.SuppressionNoFire && AreaFireOk(s, area))
            Consider(Option.AreaFire, 50f + 10f * curProt + (s.Weapon.IsMachineGun ? 8f : 0f), SoldierAction.Engage);

        // 4. Go where the squad leader wants him.
        if (farFromTask && CanFollowMove(s) && PickTaskSpot(s, out taskDest, out taskCover))
        {
            float score = s.TaskAssault ? 88f
                : s.Task == TaskMode.Move ? (realThreats ? 72f : 66f)
                : (underFire ? 50f : 64f); // drifted away from a hold position
            Consider(Option.FollowOrders, score, SoldierAction.Advance);
        }

        // 5. Told to hold, in position, nobody to shoot and nobody shooting at him: dig in.
        if (s.Task == TaskMode.Hold && s.Group.Order == GroupOrder.Hold && !farFromTask && best is Option.Idle && s.Suppression < Mr.SuppressionShaken &&
            s.MoraleState is MoraleState.Steady or MoraleState.Shaken &&
            _b.Time - s.LastShotAtTime > Ai.DigQuietTime && !(s.InCover && s.Cover!.Kind == CoverKind.Foxhole))
            Consider(Option.Dig, 40f, SoldierAction.Dig);

        long et = Core.Profile.Start();
        Execute(s, best, bestCover, taskDest, taskCover, curProt, realThreats);
        Core.Profile.Lap("think.execute", et);
    }

    private bool CanFollowMove(Soldier s)
    {
        if (s.TaskAssault)
            return s.MoraleState != MoraleState.Broken && s.Suppression < Mr.SuppressionNoFire;
        return (s.MoraleState == MoraleState.Steady || s.MoraleState == MoraleState.Shaken) && s.Suppression < Mr.SuppressionPinned;
    }

    private bool AreaFireOk(Soldier s, Vector2 area)
    {
        float d = Vector2.Distance(s.Position, area);
        if (d > s.Weapon.Range || d < 5f) return false;
        if (_b.Map.SightBlocked(s.Position, area)) return false;
        return !_b.FriendNear(s, area, _b.Rules.Squad.FriendlyFireSafety);
    }

    public void OnArrived(Soldier s)
    {
        SetStationaryStance(s);
        s.NextThink = _b.Time; // decide what to do from here straight away
    }

    // ---------------------------------------------------------------- doing it

    private void Execute(Soldier s, Option option, CoverPoint? cover, Vector2 taskDest, CoverPoint? taskCover,
        float curProt, bool realThreats)
    {
        switch (option)
        {
            case Option.TakeCover:
            case Option.CrawlToCover:
            {
                var mode = option == Option.CrawlToCover ? MoveMode.Crawl : MoveMode.Run;
                if (cover != null && Claim(s, cover) && _b.SetMove(s, cover.Position, mode))
                {
                    SetAction(s, SoldierAction.TakeCover,
                        $"{(mode == MoveMode.Crawl ? "Crawling" : "Running")} for cover: {Describe(cover)} {Vector2.Distance(s.Position, cover.Position):0} m away");
                    return;
                }
                break;
            }
            case Option.FollowOrders:
            {
                if (taskCover != null && !Claim(s, taskCover)) break;
                if (taskCover == null) _b.ReleaseCover(s);
                var mode = s.TaskRun || realThreats ? MoveMode.Run : MoveMode.Walk;
                if (_b.SetMove(s, taskDest, mode))
                {
                    string where = taskCover != null ? Describe(taskCover) : "open ground";
                    SetAction(s, SoldierAction.Advance, s.TaskAssault
                        ? $"Assaulting! Rushing the enemy position, {Vector2.Distance(s.Position, taskDest):0} m"
                        : $"Moving with the {s.Team?.Name ?? "squad"} to {where}, {Vector2.Distance(s.Position, taskDest):0} m");
                    return;
                }
                break;
            }
            case Option.AreaFire:
            {
                _b.StopMoving(s);
                s.Target = null;
                s.AreaTarget = s.FireAt;
                s.Facing = Geo.SafeNormalize(s.AreaTarget!.Value - s.Position, s.Facing);
                SetStationaryStance(s);
                SetAction(s, SoldierAction.Engage,
                    $"Firing at where the enemy was seen, {Vector2.Distance(s.Position, s.AreaTarget.Value):0} m (covering fire)");
                return;
            }
            case Option.Engage:
            {
                _b.StopMoving(s);
                s.AreaTarget = null;
                var t = s.Target!;
                s.Facing = Geo.SafeNormalize(t.Position - s.Position, s.Facing);
                SetStationaryStance(s);
                SetAction(s, SoldierAction.Engage,
                    $"Firing at {t.Label} ({t.Role.ToLowerInvariant()}), {Vector2.Distance(s.Position, t.Position):0} m");
                return;
            }
            case Option.Dig:
            {
                _b.StopMoving(s);
                s.AreaTarget = null;
                if (Vector2.DistanceSquared(s.DigSpot, s.Position) > 1f) { s.DigSpot = s.Position; s.DigProgress = 0f; }
                if (_threats.Count > 0) s.Facing = Geo.SafeNormalize(_threats[0] - s.Position, s.Facing);
                s.Stance = Stance.Crouched;
                SetAction(s, SoldierAction.Dig, "Digging a foxhole");
                s.Thought = $"Digging a foxhole ({100f * s.DigProgress / Ai.DigTime:0}%)";
                return;
            }
            case Option.HugCover:
                _b.StopMoving(s);
                s.Stance = Stance.Prone;
                SetAction(s, SoldierAction.HugCover, $"Pinned down, hugging the dirt (suppression {s.Suppression:0})");
                return;
        }

        // Idle: stay put, face the threat.
        _b.StopMoving(s);
        s.AreaTarget = null;
        if (_threats.Count > 0) s.Facing = Geo.SafeNormalize(_threats[0] - s.Position, s.Facing);
        SetStationaryStance(s);
        string why = s.Magazine == 0 && s.SpareMagazines == 0 ? "Out of ammunition"
            : !realThreats ? (IsHolding(s) ? "In position, watching" : "Waiting")
            : s.VisibleEnemies.Count == 0 ? "Can't see the enemy from here"
            : curProt > 0.5f ? "Keeping his head down" : "Holding";
        SetAction(s, SoldierAction.Idle, why);
    }

    /// <summary>A broken man runs for his squad's rally point and lies low there until he (or the squad) rallies.
    /// With no rally point, or with the enemy on top of it, he runs off the field.</summary>
    private void Flee(Soldier s)
    {
        var co = _b.Rules.Cohesion;
        if (_b.RallyPointFor(s.Group) is Vector2 rally)
        {
            bool there = Vector2.DistanceSquared(s.Position, rally) <= co.RallyRadius * co.RallyRadius;
            if (there)
            {
                // Out of it: flat on the ground, getting his breath back.
                _b.StopMoving(s);
                s.Stance = Stance.Prone;
                s.Target = null;
                SetAction(s, SoldierAction.Idle, "Out of the fight, getting his nerve back");
                return;
            }
            if (s.Action == SoldierAction.Flee && s.IsMoving && s.Destination is Vector2 d &&
                Vector2.DistanceSquared(d, rally) <= co.RallyRadius * co.RallyRadius) return;
            _b.ReleaseCover(s);
            var spot = rally + new Vector2(_b.Rng.Range(-1f, 1f), _b.Rng.Range(-1f, 1f)) * co.RallyRadius * 0.6f;
            spot = _b.Map.Nav.NearestWalkable(_b.Map.Clamp(spot, 6f));
            if (_b.SetMove(s, spot, MoveMode.Run))
            {
                SetAction(s, SoldierAction.Flee, "Broken! Running back to rally");
                return;
            }
        }
        if (s.Action == SoldierAction.Flee && s.IsMoving && s.Destination is Vector2 e && _b.DistanceToEdge(e) < 3f) return;
        _b.ReleaseCover(s);
        var dest = _b.RearEdgePoint(s);
        // Spread out a little rather than all running down the same line.
        dest += Geo.Perp(s.Group.Rear) * _b.Rng.Range(-20f, 20f);
        dest = _b.Map.Clamp(dest, 1f);
        if (!_b.SetMove(s, dest, MoveMode.Run)) _b.SetMove(s, _b.Map.Clamp(s.Position + s.Group.Rear * 30f, 1f), MoveMode.Run);
        SetAction(s, SoldierAction.Flee, "Broken! Running for the rear");
    }

    private void SetAction(Soldier s, SoldierAction action, string thought)
    {
        if (s.Action != action)
        {
            s.ActionStarted = _b.Time;
            _b.Note(s, $"{action}: {thought}", LogKind.Thought);
        }
        s.Action = action;
        s.Thought = thought;
    }

    private void SetStationaryStance(Soldier s)
    {
        if (s.IsMoving) return;
        bool threat = s.Group.Knowledge.Contacts.Any() || s.UnderFire(_b.Time, Ai.UnderFireWindow);
        if (!threat) s.Stance = s.InCover ? Stance.Crouched : Stance.Standing;
        else if (s.Suppression >= Mr.SuppressionPinned) s.Stance = Stance.Prone;
        // In standing crops a man flat on his face sees nothing, so he fights from one knee.
        else if (!s.InCover && _b.Map.Terrain.HasCrops && _b.Map.Terrain.KindAt(s.Position) == GroundKind.Crops) s.Stance = Stance.Crouched;
        else s.Stance = s.InCover && s.Cover!.Kind is not CoverKind.Crater ? Stance.Crouched : Stance.Prone;
    }

    private bool Claim(Soldier s, CoverPoint c)
    {
        if (c.ClaimedBy != -1 && c.ClaimedBy != s.Id) return false;
        if (s.Cover != c) _b.ReleaseCover(s);
        c.ClaimedBy = s.Id;
        s.Cover = c;
        return true;
    }

    private static string Describe(CoverPoint c) => c.Kind switch
    {
        CoverKind.LowWall => "low wall",
        CoverKind.HighWall => c.CanFire ? "corner of a ruin" : "behind a ruin",
        CoverKind.Foxhole => "foxhole",
        CoverKind.Hedge => "hedgerow",
        CoverKind.Tree => "tree",
        _ => "crater",
    };

    // ---------------------------------------------------------------- reading the situation

    /// <summary>Fills _threats with the nearest known enemy positions. Returns false if nothing real is known
    /// (then the expected enemy direction stands in, so men still take sensible positions).</summary>
    private bool GatherThreats(Soldier s)
    {
        _threats.Clear();
        // The men he can see right now come first (nearest first), then other reported contacts nearby.
        for (int i = 0; i < s.VisibleEnemies.Count && _threats.Count < MaxThreats; i++)
            _threats.Add(s.VisibleEnemies[i].Position);
        if (_threats.Count < MaxThreats)
        {
            float range = _b.Rules.Perception.ViewRange * 1.2f;
            _nearest.Clear();
            foreach (var c in s.Group.Knowledge.Contacts)
            {
                float d = Vector2.DistanceSquared(c.LastPosition, s.Position);
                if (d > range * range) continue;
                // keep the few nearest without sorting everything
                if (_nearest.Count < MaxThreats) _nearest.Add((d, c.LastPosition));
                else
                {
                    int worst = 0;
                    for (int k = 1; k < _nearest.Count; k++) if (_nearest[k].d > _nearest[worst].d) worst = k;
                    if (d < _nearest[worst].d) _nearest[worst] = (d, c.LastPosition);
                }
            }
            _nearest.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (_, pos) in _nearest)
            {
                if (_threats.Count >= MaxThreats) break;
                _threats.Add(pos);
            }
        }
        if (s.UnderFire(_b.Time, Ai.UnderFireWindow)) _threats.Add(s.LastShotFrom);
        if (_threats.Count > 0) return true;
        _threats.Add(s.Position + s.Group.Facing * 150f);
        return false;
    }

    private float Protection(Vector2 pos)
    {
        if (_threats.Count == 0) return 0f;
        float sum = 0f;
        foreach (var t in _threats) sum += _b.Map.ProtectionAt(pos, t);
        return sum / _threats.Count;
    }

    private static bool IsHolding(Soldier s) =>
        s.Task == TaskMode.Hold || Vector2.DistanceSquared(s.Position, s.TaskPosition) <= (s.TaskRadius + 1.5f) * (s.TaskRadius + 1.5f);

    private Soldier? PickTarget(Soldier s)
    {
        Soldier? best = null;
        float bestScore = 0f;
        foreach (var e in s.VisibleEnemies)
        {
            if (!e.IsActive) continue;
            if (e.IsTank && !s.Weapon.CanHurtArmour) continue; // no point shooting a rifle at a tank
            if (!e.IsTank && s.Weapon.AntiTankOnly) continue;  // a bazooka is saved for tanks
            float d = Vector2.Distance(s.Position, e.Position);
            if (d > s.Weapon.Range) continue;
            if (_b.FriendNear(s, e.Position, 4f)) continue; // don't shoot into a melee with your mates
            float threat = e.Weapon.Threat * (e.IsLeader ? 1.5f : 1f) * (_b.Time - e.LastFiredTime < 3f ? 1.3f : 1f);
            if (e.MoraleState == MoraleState.Broken) threat *= 0.5f;
            // An anti-tank rifleman is there for the tanks: a hit that can go through the armour counts most.
            if (s.Weapon.CanHurtArmour)
                threat *= e.IsTank ? (s.Weapon.PenetrationAt(d) > Battle.ArmourFacing(e, s.Position) ? 20f : 4f) : 0.3f;
            float score = threat * (0.02f + _b.HitChance(s, e)) * (1f + 30f / MathF.Max(d, 10f));
            if (e == s.Target) score *= 1.3f; // keep shooting at the same man
            if (score > bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    private CoverPoint? FindCover(Soldier s, Vector2 center, float radius)
    {
        CoverPoint? best = null;
        float bestScore = float.MinValue;
        float nearestThreat = _threats.Min(t => Vector2.Distance(t, s.Position));
        foreach (var c in _b.Map.CoverNear(center, radius))
        {
            if (c.ClaimedBy != -1 && c.ClaimedBy != s.Id) continue;
            float prot = Protection(c.Position);
            float d = Vector2.Distance(s.Position, c.Position);
            float score = prot - d / MathF.Max(radius, 10f) * 0.3f;
            if (c.CanFire) score += 0.15f; else score -= s.Suppression >= Mr.SuppressionShaken ? 0f : 0.25f;
            float closer = nearestThreat - _threats.Min(t => Vector2.Distance(t, c.Position));
            if (closer > 0f && center == s.Position) score -= closer * 0.02f; // don't run towards the enemy to hide
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    /// <summary>Where to go inside the area the squad gave him: the best cover there, or a spread-out spot if none.</summary>
    private bool PickTaskSpot(Soldier s, out Vector2 dest, out CoverPoint? cover)
    {
        cover = null;
        float bestScore = float.MinValue;
        if (!s.TaskAssault)
        {
            foreach (var c in _b.Map.CoverNear(s.TaskPosition, s.TaskRadius))
            {
                if (c.ClaimedBy != -1 && c.ClaimedBy != s.Id) continue;
                float score = Protection(c.Position) + (c.CanFire ? 0.2f : -0.4f) - Vector2.Distance(s.Position, c.Position) / 150f
                              + _b.Rng.Range(0f, 0.1f);
                if (score > bestScore) { bestScore = score; cover = c; }
            }
        }
        if (cover != null && bestScore > 0.1f)
        {
            dest = cover.Position;
            return true;
        }
        cover = null;
        // Open ground: spread out across the line of advance, a few metres apart.
        var dir = Geo.SafeNormalize(s.TaskPosition - s.Position, s.Group.Facing);
        int slot = s.Team != null ? s.Team.Members.IndexOf(s) : s.Id;
        float offset = ((slot % 2 == 0) ? 1f : -1f) * (1 + slot / 2) * MathF.Min(3f, s.TaskRadius * 0.4f);
        dest = s.TaskPosition + Geo.Perp(dir) * offset;
        dest = _b.Map.Nav.NearestWalkable(_b.Map.Clamp(dest, 1f));
        return Vector2.Distance(dest, s.Position) > 1f;
    }
}
