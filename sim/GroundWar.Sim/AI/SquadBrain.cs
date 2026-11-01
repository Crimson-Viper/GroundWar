using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.AI;

/// <summary>
/// The squad leader. Every second or two he looks at what his squad knows, picks a tactic for its order
/// (travel, bounding overwatch, fire and movement, flank, assault, suppress, defend, fall back), and gives each
/// team a place to be and somewhere to shoot. The soldiers then carry that out while trying to stay alive.
/// </summary>
public sealed class SquadBrain
{
    private readonly Battle _b;
    private readonly List<Contact> _contacts = new();

    public SquadBrain(Battle battle) { _b = battle; }

    private Data.SquadRules R => _b.Rules.Squad;

    public void Think(Group g)
    {
        if (g.CountEffective == 0) { g.Status = g.CountActive > 0 ? "Broken" : "Destroyed"; return; }
        ShareWithNeighbours(g);
        GatherContacts(g);

        switch (g.Order)
        {
            case GroupOrder.Hold: Defend(g, g.HoldPosition, g.ObjectiveRadius); break;
            case GroupOrder.Advance:
                if (Vector2.Distance(g.Centroid, g.Objective) <= g.ObjectiveRadius + 5f)
                {
                    // Got there first (or the enemy has gone): the place is ours, so hold it.
                    // (A waypoint is only somewhere to get to: arriving isn't taking anything.)
                    if (!g.MoveOnly && EnemyCenter(g, g.Objective, g.ObjectiveRadius + 25f) == null)
                    {
                        _b.Note(g, $"{g.Name}: reached the objective, holding it");
                        g.TookObjective = true;
                    }
                    g.HoldPosition = g.Objective;
                    g.Order = GroupOrder.Hold;
                    Defend(g, g.Objective, g.ObjectiveRadius);
                }
                else MoveTowards(g, g.Objective);
                break;
            case GroupOrder.Suppress: SuppressOrder(g); break;
            case GroupOrder.Attack: Attack(g); break;
        }
    }

    // ---------------------------------------------------------------- what the squad knows

    internal void ShareWithNeighbours(Group g)
    {
        var leaderPos = g.Leader?.Position ?? g.Centroid;
        foreach (var other in _b.Groups)
        {
            if (other == g || other.Side != g.Side || other.CountEffective == 0) continue;
            var otherPos = other.Leader?.Position ?? other.Centroid;
            if (Vector2.DistanceSquared(leaderPos, otherPos) > R.ShareRadius * R.ShareRadius) continue;
            foreach (var c in other.Knowledge.Contacts)
            {
                var mine = g.Knowledge.Get(c.EnemyId);
                if (mine == null || mine.LastSeen < c.LastSeen)
                {
                    var enemy = _b.GetSoldier(c.EnemyId);
                    if (enemy != null) g.Knowledge.Report(enemy, c.LastPosition, c.LastSeen, c.Confidence);
                }
            }
        }
    }

    private void GatherContacts(Group g)
    {
        _contacts.Clear();
        var center = g.Centroid;
        foreach (var c in g.Knowledge.Contacts)
            if (Vector2.Distance(c.LastPosition, center) <= R.ContactRange) _contacts.Add(c);
    }

    /// <summary>Where the enemy seems to be: the average of recent sightings, preferring ones near the objective.</summary>
    private Vector2? EnemyCenter(Group g, Vector2 near, float radius)
    {
        Vector2 sum = Vector2.Zero;
        float weight = 0f;
        foreach (var c in _contacts)
        {
            if (Vector2.Distance(c.LastPosition, near) > radius) continue;
            float w = c.Confidence * MathF.Max(0.1f, 1f - (_b.Time - c.LastSeen) / 30f);
            sum += c.LastPosition * w;
            weight += w;
        }
        return weight > 0f ? sum / weight : null;
    }

    private Vector2? NearestContact(Vector2 p)
    {
        Vector2? best = null;
        float bd = float.MaxValue;
        foreach (var c in _contacts)
        {
            float d = Vector2.DistanceSquared(c.LastPosition, p);
            if (d < bd) { bd = d; best = c.LastPosition; }
        }
        return best;
    }

    private bool UnderFire(Group g) => g.Members.Any(m => m.IsActive && m.UnderFire(_b.Time, R.EnemyQuietTime));

    /// <summary>The enemy has stopped shooting at us while we keep shooting at them: they're keeping their heads down.</summary>
    private bool EnemySuppressed(Group g)
    {
        float lastIncoming = g.Members.Where(m => m.IsActive).Select(m => m.LastShotAtTime).DefaultIfEmpty(-999f).Max();
        float lastOutgoing = g.Members.Where(m => m.IsActive).Select(m => m.LastFiredTime).DefaultIfEmpty(-999f).Max();
        return _b.Time - lastIncoming >= R.EnemyQuietTime && _b.Time - lastOutgoing < 3f;
    }

    private static float PinnedFraction(Team t)
    {
        int n = 0, pinned = 0;
        foreach (var m in t.Members)
        {
            if (!m.IsEffective) continue;
            n++;
            if (m.MoraleState == MoraleState.Pinned) pinned++;
        }
        return n == 0 ? 1f : pinned / (float)n;
    }

    // ---------------------------------------------------------------- orders

    private void MoveTowards(Group g, Vector2 goal)
    {
        var center = g.Centroid;
        bool contact = _contacts.Count > 0 || UnderFire(g);
        bool close = Vector2.Distance(center, goal) < R.BoundingRange;
        if (!contact && !close) Travel(g, goal);
        else if (contact && EnemyCenter(g, center, R.ContactRange) is Vector2 enemy && Vector2.Distance(center, enemy) < R.AssaultRange * 2.5f)
            FireAndMovement(g, goal, enemy);
        else Bounding(g, goal);
    }

    private void Attack(Group g)
    {
        var center = g.Centroid;
        var enemy = EnemyCenter(g, g.Objective, g.ObjectiveRadius + 60f) ?? EnemyCenter(g, center, R.ContactRange);

        // Too many men lost: stop and hold what we have.
        if (g.CountEffective < g.StartStrength * R.FallBackStrength && g.Tactic != Tactic.Assault)
        {
            _b.Note(g, $"{g.Name}: too many casualties, the attack is called off");
            g.AttackFailed = true;
            g.HoldPosition = center;
            g.Order = GroupOrder.Hold;
            Defend(g, center, R.TeamRadius * 2f);
            return;
        }

        // Objective taken?
        bool onObjective = Vector2.Distance(g.Rifles.Centroid, g.Objective) <= g.ObjectiveRadius;
        bool enemyLeft = EnemyCenter(g, g.Objective, g.ObjectiveRadius + 25f) == null;
        if (onObjective && enemyLeft)
        {
            _b.Note(g, $"{g.Name}: objective taken, consolidating");
            g.TookObjective = true;
            g.HoldPosition = g.Objective;
            g.Order = GroupOrder.Hold;
            Defend(g, g.Objective, g.ObjectiveRadius);
            return;
        }

        if (enemy == null)
        {
            // Nothing known near the objective: approach it carefully, or walk onto it if we're already close.
            if (Vector2.Distance(center, g.Objective) < g.ObjectiveRadius + 30f && g.Tactic is Tactic.Flank or Tactic.Assault or Tactic.FireAndMovement)
                Assault(g, g.Objective, "Nobody seen on the objective: moving onto it");
            else MoveTowards(g, g.Objective);
            return;
        }

        var e = enemy.Value;
        g.EnemyEstimate = e;
        float distRifles = Vector2.Distance(g.Rifles.Centroid, e);

        switch (g.Tactic)
        {
            case Tactic.Assault:
                Assault(g, e, null);
                return;
            case Tactic.Flank:
                ContinueFlank(g, e, distRifles);
                return;
            case Tactic.FireAndMovement:
                if (distRifles < R.AssaultRange && (EnemySuppressed(g) || distRifles < 30f)) { Assault(g, e, null); return; }
                FireAndMovement(g, e, e);
                return;
        }

        // Still far off: close in by bounds, covering each other, until close enough to make a plan.
        if (Vector2.Distance(center, e) > R.PlanRange) { Bounding(g, e); return; }
        if (g.Teams.Count > 1 && g.Gun.CountEffective >= 2 && g.Rifles.CountEffective >= 3 && PlanFlank(g, e))
        {
            SetTactic(g, Tactic.Flank);
            _b.Note(g, $"{g.Name}: gun team fixes the enemy, rifle team flanks {(Geo.Cross(e - center, g.FlankPoint - center) > 0 ? "right" : "left")}");
            ContinueFlank(g, e, distRifles);
        }
        else
        {
            SetTactic(g, Tactic.FireAndMovement);
            _b.Note(g, $"{g.Name}: no way round, fire and movement straight at them");
            FireAndMovement(g, e, e);
        }
    }

    private void SuppressOrder(Group g)
    {
        var support = g.SupportPosition ?? g.Objective;
        // The squad we were supporting has taken the objective: job done, hold here.
        if (_b.Groups.Any(o => o.Side == g.Side && o.TookObjective && Vector2.Distance(o.Objective, g.Objective) < 30f))
        {
            _b.Note(g, $"{g.Name}: objective taken, ceasing fire");
            g.Order = GroupOrder.Hold;
            g.HoldPosition = g.Centroid;
            Defend(g, g.HoldPosition, R.TeamRadius * 2f);
            return;
        }
        if (Vector2.Distance(g.Centroid, support) > R.TeamRadius * 2f && g.Tactic != Tactic.Suppress)
        {
            MoveTowards(g, support);
            return;
        }
        var target = EnemyCenter(g, g.Objective, g.ObjectiveRadius + 40f);
        SetTactic(g, Tactic.Suppress);
        foreach (var t in g.Teams) HoldTeam(g, t, support + Spread(t, g), R.TeamRadius, target);
        g.Status = target != null ? "Suppressing the objective" : "In support position, watching the objective";
    }

    // ---------------------------------------------------------------- tactics

    private void Travel(Group g, Vector2 goal)
    {
        SetTactic(g, Tactic.Travel);
        // Everyone moves together, a leg at a time.
        bool arrived = g.Teams.All(t => !t.Moving || TeamArrived(t));
        if (arrived)
        {
            var center = g.Centroid;
            var dir = Geo.SafeNormalize(goal - center, g.Facing);
            float step = MathF.Min(R.TravelStep, Vector2.Distance(center, goal));
            var dest = center + dir * step;
            foreach (var t in g.Teams) MoveTeam(g, t, dest + Geo.Perp(dir) * (t.Role == TeamRole.Gun ? -6f : 6f), R.TeamRadius * 1.3f, run: false);
        }
        g.Status = $"Travelling towards {Describe(goal, g)}";
    }

    private void Bounding(Group g, Vector2 goal)
    {
        SetTactic(g, Tactic.Bounding);
        bool contact = _contacts.Count > 0;
        AlternateBounds(g, goal, R.BoundStep, run: contact, label: "Bounding overwatch");
    }

    private void FireAndMovement(Group g, Vector2 goal, Vector2 enemy)
    {
        SetTactic(g, Tactic.FireAndMovement);
        AlternateBounds(g, goal, R.RushStep, run: true, label: "Fire and movement", fireAt: enemy);
    }

    /// <summary>One team moves while the other covers it; when the movers arrive, they swap.</summary>
    private void AlternateBounds(Group g, Vector2 goal, float step, bool run, string label, Vector2? fireAt = null)
    {
        if (g.Teams.Count == 1)
        {
            var only = g.Teams[0];
            if (!only.Moving || TeamArrived(only)) MoveTeam(g, only, NextBound(g, only.Centroid, goal, step), R.TeamRadius, run, fireAt: fireAt);
            g.Status = $"{label} towards {Describe(goal, g)}";
            return;
        }
        var mover = g.Teams[g.MovingTeam % g.Teams.Count];
        var cover = g.Teams[(g.MovingTeam + 1) % g.Teams.Count];
        if (!mover.Moving || TeamArrived(mover))
        {
            // Swap roles: the team that just arrived covers, the other moves past it.
            if (mover.Moving) { g.MovingTeam = (g.MovingTeam + 1) % g.Teams.Count; (mover, cover) = (cover, mover); }
            if (mover.CountEffective > 0)
            {
                var from = Vector2.Distance(cover.Centroid, goal) < Vector2.Distance(mover.Centroid, goal) ? cover.Centroid : mover.Centroid;
                MoveTeam(g, mover, NextBound(g, from, goal, step) + Spread(mover, g), R.TeamRadius, run, fireAt: fireAt);
            }
            HoldTeam(g, cover, cover.Centroid, R.TeamRadius, fireAt ?? NearestContact(cover.Centroid));
        }
        g.Status = $"{label}: {mover.Name} moving, {cover.Name} covering";
    }

    private bool PlanFlank(Group g, Vector2 enemy)
    {
        var baseCenter = g.Gun.Centroid;
        var axis = Geo.SafeNormalize(enemy - baseCenter, g.Facing);
        var perp = Geo.Perp(axis);
        float bestScore = float.MinValue;
        bool found = false;
        foreach (float side in new[] { 1f, -1f })
        {
            var start = g.Rifles.Centroid;
            var waypoint = _b.Map.Clamp(start + perp * side * R.FlankSideStep + axis * 10f, 3f);
            var flank = _b.Map.Clamp(enemy + perp * side * R.FlankOffset, 3f);
            waypoint = _b.Map.Nav.NearestWalkable(waypoint);
            flank = _b.Map.Nav.NearestWalkable(flank);
            if (Vector2.Distance(flank, enemy) < R.FlankOffset * 0.6f) continue; // map edge in the way
            float score = RouteScore(start, waypoint, enemy) + RouteScore(waypoint, flank, enemy);
            if (score > bestScore) { bestScore = score; g.FlankWaypoint = waypoint; g.FlankPoint = flank; found = true; }
        }
        g.FlankLegTwo = false;
        return found && bestScore > -1.9f;
    }

    /// <summary>How good a route is for sneaking: cover along it is good, being in the enemy's view is bad.</summary>
    private float RouteScore(Vector2 a, Vector2 b, Vector2 enemy)
    {
        float len = Vector2.Distance(a, b);
        int n = Math.Max(2, (int)(len / 10f));
        float cover = 0f, exposed = 0f;
        for (int i = 1; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            int idx = _b.Grid.IndexOf(p);
            if (idx >= 0) cover += _b.Grid.Cover[idx];
            if (!_b.Map.SightBlocked(p, enemy)) exposed += 1f;
        }
        return cover / n - exposed / n;
    }

    private void ContinueFlank(Group g, Vector2 enemy, float distRifles)
    {
        var gun = g.Gun;
        var rifles = g.Rifles;
        float lift = Vector2.Distance(rifles.Centroid, enemy);
        HoldTeam(g, gun, gun.Moving ? gun.Destination : gun.Centroid, R.TeamRadius, lift > R.LiftFireRange ? enemy : null);

        // Rifle team pinned for too long: the flank has failed.
        if (PinnedFraction(rifles) >= 0.5f)
        {
            if (g.PinnedSince < 0f) g.PinnedSince = _b.Time;
            else if (_b.Time - g.PinnedSince > R.PinnedAbortTime)
            {
                _b.Note(g, $"{g.Name}: flank pinned down, switching to fire and movement");
                g.PinnedSince = -1f;
                FireAndMovement(g, enemy, enemy);
                return;
            }
        }
        else g.PinnedSince = -1f;

        if (distRifles < R.AssaultRange && (EnemySuppressed(g) || distRifles < 35f || g.FlankLegTwo && TeamArrived(rifles)))
        {
            Assault(g, enemy, null);
            return;
        }
        if (!g.FlankLegTwo)
        {
            if (!rifles.Moving || rifles.Destination != g.FlankWaypoint) MoveTeam(g, rifles, g.FlankWaypoint, R.TeamRadius, run: true);
            else if (TeamArrived(rifles)) { g.FlankLegTwo = true; MoveTeam(g, rifles, g.FlankPoint, R.TeamRadius, run: true); }
            g.Status = "Flanking: gun team firing, rifle team moving out to the side";
        }
        else
        {
            if (!rifles.Moving || rifles.Destination != g.FlankPoint) MoveTeam(g, rifles, g.FlankPoint, R.TeamRadius, run: true);
            g.Status = "Flanking: rifle team working round the enemy's side";
        }
    }

    private void Assault(Group g, Vector2 enemy, string? why)
    {
        if (g.Tactic != Tactic.Assault) _b.Note(g, $"{g.Name}: {why ?? "assault!"}");
        SetTactic(g, Tactic.Assault);
        var rifles = g.Rifles;
        var gun = g.Gun;
        // Rush onto the enemy position; the gun team keeps them down until the riflemen are nearly there.
        MoveTeam(g, rifles, enemy, 6f, run: true, assault: true, force: true);
        if (g.Teams.Count > 1)
        {
            float lift = Vector2.Distance(rifles.Centroid, enemy);
            HoldTeam(g, gun, gun.Centroid, R.TeamRadius, lift > R.LiftFireRange ? enemy : null);
        }
        g.Status = "Assaulting the enemy position";
    }

    private void Defend(Group g, Vector2 area, float radius)
    {
        // Losing badly and the enemy is close: fall back by teams.
        bool weak = g.CountEffective < g.StartStrength * R.FallBackStrength;
        var near = NearestContact(g.Centroid);
        bool pressed = near != null && Vector2.Distance(near.Value, g.Centroid) < 40f;
        if (weak && pressed && g.Order == GroupOrder.Hold)
        {
            if (g.Tactic != Tactic.FallBack) _b.Note(g, $"{g.Name}: can't hold, falling back by teams");
            SetTactic(g, Tactic.FallBack);
            var rear = _b.Map.Clamp(g.Centroid + g.Rear * R.FallBackStep * 3f, 3f);
            AlternateBounds(g, rear, R.FallBackStep, run: true, label: "Falling back");
            return;
        }

        SetTactic(g, Tactic.Defend);
        foreach (var t in g.Teams)
            HoldTeam(g, t, area, radius, NearestContact(t.Centroid));
        g.Status = _contacts.Count > 0 ? "Defending: enemy in sight" : "Defending, watching for the enemy";
    }

    // ---------------------------------------------------------------- giving the orders

    private void SetTactic(Group g, Tactic t)
    {
        if (g.Tactic == t) return;
        _b.Note(g, $"{g.Name}: {g.Tactic} -> {t}");
        g.Tactic = t;
        g.TacticStarted = _b.Time;
        foreach (var team in g.Teams) team.Moving = false;
    }

    private void MoveTeam(Group g, Team t, Vector2 dest, float radius, bool run, bool assault = false, Vector2? fireAt = null, bool force = false)
    {
        dest = _b.Map.Nav.NearestWalkable(_b.Map.Clamp(dest, 2f));
        if (!force && t.Moving && Vector2.DistanceSquared(t.Destination, dest) < 1f) return;
        t.Destination = dest;
        t.Moving = true;
        t.MoveStarted = _b.Time;
        foreach (var m in t.Members)
        {
            m.Task = TaskMode.Move;
            m.TaskPosition = dest;
            m.TaskRadius = radius;
            m.TaskRun = run;
            m.TaskAssault = assault;
            m.FireAt = fireAt;
        }
    }

    private void HoldTeam(Group g, Team t, Vector2 area, float radius, Vector2? fireAt)
    {
        t.Moving = false;
        t.Destination = area;
        foreach (var m in t.Members)
        {
            if (m.Task != TaskMode.Hold || Vector2.DistanceSquared(m.TaskPosition, area) > 4f) m.TaskPosition = area;
            m.Task = TaskMode.Hold;
            m.TaskRadius = radius;
            m.TaskRun = true;
            m.TaskAssault = false;
            m.FireAt = fireAt;
        }
    }

    private bool TeamArrived(Team t)
    {
        int n = 0, there = 0;
        float r = t.Members.Count > 0 ? t.Members[0].TaskRadius + 4f : 10f;
        foreach (var m in t.Members)
        {
            if (!m.IsEffective) continue;
            n++;
            if (Vector2.DistanceSquared(m.Position, t.Destination) <= r * r) there++;
        }
        if (n == 0) return true;
        if (there >= MathF.Ceiling(n * 0.7f)) return true;
        // Taking too long, and nobody is still on the way: call it done from wherever they got to.
        return _b.Time - t.MoveStarted > R.MaxBoundTime && t.Members.All(m => !m.IsEffective || !m.IsMoving);
    }

    /// <summary>The next spot to bound to: about one step towards the goal, nudged towards the best cover.</summary>
    private Vector2 NextBound(Group g, Vector2 from, Vector2 goal, float step)
    {
        var dir = Geo.SafeNormalize(goal - from, g.Facing);
        float dist = Vector2.Distance(from, goal);
        if (dist <= step) return goal;
        Vector2 best = from + dir * step;
        float bestScore = float.MinValue;
        for (int i = -3; i <= 3; i++)
        {
            var d = Geo.Rotate(dir, i * 0.2f);
            foreach (float len in new[] { step * 0.75f, step, step * 1.25f })
            {
                var p = _b.Map.Clamp(from + d * len, 3f);
                int idx = _b.Grid.IndexOf(p);
                float cover = idx >= 0 ? _b.Grid.Cover[idx] : 0f;
                float progress = dist - Vector2.Distance(p, goal);
                float score = cover * 1.2f + progress / step * 0.6f - MathF.Abs(i) * 0.05f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
        }
        return best;
    }

    /// <summary>Teams sit side by side rather than on top of each other.</summary>
    private static Vector2 Spread(Team t, Group g) => Geo.Perp(g.Facing) * (t.Role == TeamRole.Gun ? -5f : 5f);

    private static string Describe(Vector2 p, Group g) =>
        Vector2.Distance(p, g.Objective) < 3f ? "the objective" : $"({p.X:0}, {p.Y:0})";
}
