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

    public SoldierBrain(Battle battle) { _b = battle; }

    private Data.AiRules Ai => _b.Rules.Ai;
    private Data.MoraleRules Mr => _b.Rules.Morale;

    private enum Option { Idle, TakeCover, CrawlToCover, Engage, Advance, HugCover }

    public void Think(Soldier s)
    {
        bool realThreats = GatherThreats(s);
        float curProt = Protection(s.Position);
        s.LastProtection = realThreats ? curProt : 0f;

        if (s.MoraleState == MoraleState.Broken) { Flee(s); return; }

        s.Target = PickTarget(s);
        bool underFire = s.UnderFire(_b.Time, Ai.UnderFireWindow);

        // Already running somewhere? Keep going unless something makes that a bad idea.
        if (s.IsMoving)
        {
            if (s.Action == SoldierAction.TakeCover) return; // a man dashing for cover doesn't stop to think
            if (s.Action == SoldierAction.Advance && s.Suppression < Mr.SuppressionPinned && s.MoraleState != MoraleState.Pinned)
                return;
            if (s.Action == SoldierAction.Flee) { _b.StopMoving(s); }
        }

        bool holding = IsHolding(s);
        Option best = Option.Idle;
        float bestScore = 10f;
        CoverPoint? bestCover = null;
        Vector2 advanceDest = default;
        CoverPoint? advanceCover = null;

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
                var c = FindCover(s, Ai.SuppressedCoverSearchRadius, curProt, holding);
                if (c != null && Protection(c.Position) - curProt > 0.3f)
                {
                    bestCover = c;
                    Consider(Option.CrawlToCover, 97f, SoldierAction.TakeCover);
                }
            }
        }

        // 2. Get into better cover.
        if ((realThreats || holding) && best != Option.CrawlToCover)
        {
            float radius = s.Suppression >= Mr.SuppressionShaken ? Ai.SuppressedCoverSearchRadius * 1.5f : Ai.CoverSearchRadius;
            var c = FindCover(s, radius, curProt, holding);
            if (c != null)
            {
                float gain = Protection(c.Position) - curProt;
                bool notInPosition = holding && !s.InCover;
                if (gain > 0.25f || (notInPosition && gain > 0.05f))
                {
                    float score = 60f + 40f * gain + (underFire ? 15f : 0f);
                    if (s.Suppression >= Mr.SuppressionNoFire) score = 0f; // too scared to get up and run
                    if (score > 0f)
                    {
                        bestCover = c;
                        Consider(Option.TakeCover, score, SoldierAction.TakeCover);
                    }
                }
            }
        }

        // 3. Shoot back.
        if (s.Target != null && (s.Magazine > 0 || s.SpareMagazines > 0) && s.Suppression < Mr.SuppressionNoFire)
        {
            float score = 55f + 15f * curProt + (s.Weapon.IsMachineGun ? 5f : 0f);
            Consider(Option.Engage, score, SoldierAction.Engage);
        }

        // 4. Push on towards the objective.
        if (s.Group.Order == GroupOrder.Advance && !AtObjective(s) &&
            (s.MoraleState == MoraleState.Steady || s.MoraleState == MoraleState.Shaken) &&
            s.Suppression < Mr.SuppressionPinned && _b.Time >= s.DwellUntil)
        {
            if (TooFarAhead(s))
            {
                s.Thought = "Waiting for the others to catch up";
            }
            else if (ChooseBound(s, out advanceDest, out advanceCover))
            {
                float score = realThreats ? 70f - (s.MoraleState == MoraleState.Shaken ? 15f : 0f) : 65f;
                Consider(Option.Advance, score, SoldierAction.Advance);
            }
        }

        Execute(s, best, bestCover, advanceDest, advanceCover, curProt, realThreats);
    }

    public void OnArrived(Soldier s)
    {
        bool contact = _b.Knowledge(s.Side).Contacts.Any() || s.UnderFire(_b.Time, Ai.UnderFireWindow);
        if (s.Action == SoldierAction.Advance)
            s.DwellUntil = _b.Time + (contact
                ? _b.Rng.Range(Ai.DwellUnderFireMin, Ai.DwellUnderFireMax)
                : _b.Rng.Range(Ai.DwellQuietMin, Ai.DwellQuietMax));
        else if (s.Action == SoldierAction.TakeCover && contact)
            s.DwellUntil = MathF.Max(s.DwellUntil, _b.Time + _b.Rng.Range(Ai.DwellUnderFireMin, Ai.DwellUnderFireMax) * 0.5f);
        SetStationaryStance(s);
        s.NextThink = _b.Time; // decide what to do from here straight away
    }

    // ---------------------------------------------------------------- doing it

    private void Execute(Soldier s, Option option, CoverPoint? cover, Vector2 advanceDest, CoverPoint? advanceCover,
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
            case Option.Advance:
            {
                if (advanceCover != null && !Claim(s, advanceCover)) break;
                if (advanceCover == null) _b.ReleaseCover(s);
                var mode = realThreats ? MoveMode.Run : MoveMode.Walk;
                if (_b.SetMove(s, advanceDest, mode))
                {
                    SetAction(s, SoldierAction.Advance,
                        $"Advancing to {(advanceCover != null ? Describe(advanceCover) : "open ground")} {Vector2.Distance(s.Position, advanceDest):0} m ahead");
                    return;
                }
                break;
            }
            case Option.Engage:
            {
                _b.StopMoving(s);
                var t = s.Target!;
                s.Facing = Geo.SafeNormalize(t.Position - s.Position, s.Facing);
                SetStationaryStance(s);
                SetAction(s, SoldierAction.Engage,
                    $"Firing at {t.Label} ({t.Role.ToLowerInvariant()}), {Vector2.Distance(s.Position, t.Position):0} m");
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
        if (_threats.Count > 0) s.Facing = Geo.SafeNormalize(_threats[0] - s.Position, s.Facing);
        SetStationaryStance(s);
        string why = s.Thought.StartsWith("Waiting") ? s.Thought
            : s.Magazine == 0 && s.SpareMagazines == 0 ? "Out of ammunition"
            : !realThreats ? (IsHolding(s) ? "In position, watching" : "Waiting")
            : s.VisibleEnemies.Count == 0 ? "Can't see the enemy from here"
            : curProt > 0.5f ? "Keeping his head down" : "Holding";
        SetAction(s, SoldierAction.Idle, why);
    }

    private void Flee(Soldier s)
    {
        if (s.Action == SoldierAction.Flee && s.IsMoving) return;
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
            _b.Note(s, $"{action}: {thought}");
        }
        s.Action = action;
        s.Thought = thought;
    }

    private void SetStationaryStance(Soldier s)
    {
        if (s.IsMoving) return;
        bool threat = _b.Knowledge(s.Side).Contacts.Any() || s.UnderFire(_b.Time, Ai.UnderFireWindow);
        if (!threat) s.Stance = s.InCover ? Stance.Crouched : Stance.Standing;
        else if (s.Suppression >= Mr.SuppressionPinned) s.Stance = Stance.Prone;
        else s.Stance = s.InCover && s.Cover!.Kind != CoverKind.Crater ? Stance.Crouched : Stance.Prone;
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
        _ => "crater",
    };

    // ---------------------------------------------------------------- reading the situation

    /// <summary>Fills _threats with the nearest known enemy positions. Returns false if nothing real is known
    /// (then the expected enemy direction stands in, so men still take sensible positions).</summary>
    private bool GatherThreats(Soldier s)
    {
        _threats.Clear();
        float range = _b.Rules.Perception.ViewRange * 1.2f;
        foreach (var c in _b.Knowledge(s.Side).Contacts
                     .Select(c => (c, d: Vector2.DistanceSquared(c.LastPosition, s.Position)))
                     .Where(x => x.d < range * range)
                     .OrderBy(x => x.d).Take(6))
            _threats.Add(c.c.LastPosition);
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

    private bool IsHolding(Soldier s) => s.Group.Order == GroupOrder.Hold || AtObjective(s);

    private bool AtObjective(Soldier s) =>
        Vector2.DistanceSquared(s.Position, s.Group.Objective) <= s.Group.ObjectiveRadius * s.Group.ObjectiveRadius;

    private bool TooFarAhead(Soldier s)
    {
        float sum = 0f;
        int n = 0;
        foreach (var m in s.Group.Members)
        {
            if (m == s || !m.IsEffective) continue;
            sum += Vector2.Distance(m.Position, s.Group.Objective);
            n++;
        }
        if (n == 0) return false;
        return Vector2.Distance(s.Position, s.Group.Objective) < sum / n - Ai.CohesionLead;
    }

    private Soldier? PickTarget(Soldier s)
    {
        Soldier? best = null;
        float bestScore = 0f;
        foreach (var e in s.VisibleEnemies)
        {
            if (!e.IsActive) continue;
            float d = Vector2.Distance(s.Position, e.Position);
            if (d > s.Weapon.Range) continue;
            float threat = e.Weapon.Threat * (e.IsLeader ? 1.5f : 1f) * (_b.Time - e.LastFiredTime < 3f ? 1.3f : 1f);
            if (e.MoraleState == MoraleState.Broken) threat *= 0.5f;
            float score = threat * (0.02f + _b.HitChance(s, e)) * (1f + 30f / MathF.Max(d, 10f));
            if (e == s.Target) score *= 1.3f; // keep shooting at the same man
            if (score > bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    private CoverPoint? FindCover(Soldier s, float radius, float curProt, bool holding)
    {
        CoverPoint? best = null;
        float bestScore = float.MinValue;
        float nearestThreat = _threats.Min(t => Vector2.Distance(t, s.Position));
        float distToObj = Vector2.Distance(s.Position, s.Group.Objective);
        float holdRadius = s.Group.ObjectiveRadius + 10f;
        foreach (var c in _b.Map.CoverNear(s.Position, radius))
        {
            if (c.ClaimedBy != -1 && c.ClaimedBy != s.Id) continue;
            if (holding && Vector2.Distance(c.Position, s.Group.Objective) > holdRadius) continue;
            float prot = Protection(c.Position);
            float d = Vector2.Distance(s.Position, c.Position);
            float score = prot - d / radius * 0.3f;
            if (c.CanFire) score += 0.15f; else score -= s.Suppression >= Mr.SuppressionShaken ? 0f : 0.25f;
            float closer = nearestThreat - _threats.Min(t => Vector2.Distance(t, c.Position));
            if (closer > 0f) score -= closer * 0.02f; // don't run towards the enemy to hide
            if (s.Group.Order == GroupOrder.Advance && !holding)
            {
                // An attacking man won't give up ground he has won unless he is badly frightened.
                float back = Vector2.Distance(c.Position, s.Group.Objective) - distToObj;
                if (back > 5f && s.MoraleState == MoraleState.Steady && s.Suppression < Mr.SuppressionPinned) continue;
                if (back > 5f) score -= 0.3f;
            }
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    /// <summary>Pick the next bound forward: a cover point closer to the objective, or open ground if there is none.</summary>
    private bool ChooseBound(Soldier s, out Vector2 dest, out CoverPoint? cover)
    {
        var obj = s.Group.Objective;
        float distToObj = Vector2.Distance(s.Position, obj);
        cover = null;
        float bestScore = float.MinValue;
        foreach (var c in _b.Map.CoverNear(s.Position, Ai.BoundMax))
        {
            if (c.ClaimedBy != -1 && c.ClaimedBy != s.Id) continue;
            float d = Vector2.Distance(s.Position, c.Position);
            float progress = distToObj - Vector2.Distance(c.Position, obj);
            bool closeToObj = Vector2.Distance(c.Position, obj) <= s.Group.ObjectiveRadius;
            if (d < Ai.BoundMin && !closeToObj) continue;
            if (progress < Ai.BoundMinProgress) continue;
            float score = 1.2f * Protection(c.Position) + progress / Ai.BoundMax * 0.8f - d / Ai.BoundMax * 0.3f
                          + _b.Rng.Range(0f, 0.25f) + (c.CanFire ? 0.2f : -0.5f);
            if (score > bestScore) { bestScore = score; cover = c; }
        }
        if (cover != null)
        {
            dest = cover.Position;
            return true;
        }
        var dir = Geo.SafeNormalize(obj - s.Position, s.Group.Facing);
        float step = MathF.Min(20f, distToObj);
        dest = s.Position + dir * step + Geo.Perp(dir) * _b.Rng.Range(-4f, 4f);
        dest = _b.Map.Nav.NearestWalkable(_b.Map.Clamp(dest, 1f));
        return Vector2.Distance(dest, s.Position) > 2f;
    }
}
