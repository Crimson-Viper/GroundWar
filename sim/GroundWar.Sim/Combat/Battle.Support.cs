using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>Where a mortar round came down this tick (Godot draws the burst or the smoke).</summary>
public readonly record struct ImpactEvent(Vector2 Position, bool Smoke, Side Side);

/// <summary>
/// Battalion support (milestone 7): mortar sections, their fire missions, smoke screens, and MG sections the player
/// can attach to a company. A company commander asks battalion for fire; the request travels up like any report,
/// battalion passes it to a mortar section like any order, the crew lay the tubes, and the rounds take time to fly.
/// So the bombs land where the enemy was a minute or two ago. See docs/systems/support-fire.md.
/// </summary>
public sealed partial class Battle
{
    private readonly List<FireMission> _missions = new();
    private readonly List<(float Time, Vector2 At, FireMission Mission, Soldier Crew)> _inFlight = new();
    private readonly List<ImpactEvent> _impacts = new();
    private WeaponDef? _mortarHe;

    /// <summary>Every fire mission asked for so far.</summary>
    public IReadOnlyList<FireMission> FireMissions => _missions;
    /// <summary>Mortar rounds that came down during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<ImpactEvent> Impacts => _impacts;
    /// <summary>Mortar rounds in the air: where and when each will land.</summary>
    public int RoundsInFlight => _inFlight.Count;

    private SupportRules Sr => Rules.Support;
    private WeaponDef MortarHe => _mortarHe ??= Data.Weapons.TryGetValue("mortar_he", out var w) ? w
        : new WeaponDef { Id = "mortar_he", Name = "Mortar bomb", BlastRadius = 9f, Suppression = 45f, SuppressionRadius = 30f };

    /// <summary>Mortar sections still able to fire: enough crew for a tube, and some rounds of this kind left.</summary>
    public IEnumerable<Group> MortarSections(Side side, FireKind? kind = null)
    {
        foreach (var g in _groups)
            if (g.Side == side && g.Mortars is { } ms && UsableTubes(g) > 0 && (kind == null ? ms.AmmoHe + ms.AmmoSmoke > 0 : ms.Ammo(kind.Value) > 0))
                yield return g;
    }

    public bool HasMortars(Side side, FireKind kind) => MortarSections(side, kind).Any();

    private int UsableTubes(Group g) => Math.Min(g.Mortars!.Tubes, g.CountEffective / Math.Max(1, Sr.CrewPerTube));

    private FireMission NewMission(Side side, Vector2 target, FireKind kind, int rounds, Company? by, string reason, bool onCall = false)
    {
        var fm = new FireMission
        {
            Id = _missions.Count, Side = side, Target = Map.Clamp(target, 2f), Kind = kind, Rounds = Math.Max(1, rounds),
            RequestedBy = by, Reason = reason, RequestedAt = Time, OnCall = onCall,
        };
        _missions.Add(fm);
        return fm;
    }

    // ---------------------------------------------------------------- asking for fire

    /// <summary>A company commander asks battalion for mortar fire. The request goes up to battalion HQ like any
    /// report; there it is passed to a mortar section. Returns null if his side has no mortars that could do it.</summary>
    internal FireMission? RequestFire(Company c, Vector2 target, FireKind kind, int rounds, string reason, bool onCall = false)
    {
        if (!HasMortars(c.Side, kind)) return null;
        var fm = NewMission(c.Side, target, kind, rounds, c, reason, onCall);
        var hq = _hq[(int)c.Side];
        Note(c.HqGroup, $"{c.CommanderName} asks for mortar fire{(onCall ? " on call" : "")}: {fm.Describe()} on {reason}", LogKind.Key);
        if (hq == null) { ForwardToMortars(fm); return fm; }
        var method = BestMethod(c.HqGroup, hq, down: false, null);
        NewMessage(c.HqGroup, method, down: false, $"fire request{(onCall ? " (on call)" : "")}: {fm.Describe()} on {reason}", null, null, MessageKind.FireRequest, null, fm);
        return fm;
    }

    /// <summary>The commander gives the word for an on-call target. He talks to the mortars' fire controller
    /// directly on the radio (battalion already has the details), so it's quick; without a radio it takes a runner's time.</summary>
    internal void CallFire(Company c, FireMission fm)
    {
        if (!fm.Waiting) return;
        float delay = c.HqGroup.HasRadio ? Cr.CompanyRadioDelay + Cr.CompanyRadioDelayJitter * Rng.Range(-1f, 1f)
            : Vector2.Distance(SquadPost(c.HqGroup), fm.FiredBy?.Centroid ?? _hq[(int)c.Side]?.Position ?? fm.Target) / Cr.RunnerSpeed;
        fm.CallAt = Time + delay;
        Note(c.HqGroup, $"{c.CommanderName} calls for the fire on {fm.Reason}", LogKind.Key);
    }

    /// <summary>A target that's no longer wanted (the plan has changed): the mortars stand down from it.</summary>
    internal void CancelFire(FireMission fm, string why)
    {
        if (fm.Status is FireMissionStatus.Done or FireMissionStatus.Refused or FireMissionStatus.Cancelled) return;
        if (fm.RoundsFired > 0) return; // too late: it's coming
        fm.Status = FireMissionStatus.Cancelled;
        fm.DoneAt = Time;
        Log.Add(Time, -1, $"Fire mission on {fm.Reason} cancelled: {why}", fm.Side);
    }

    /// <summary>Battalion has the request (or has its own): it picks a mortar section in range with the right rounds
    /// and the shortest queue, and sends it the fire mission.</summary>
    private void ForwardToMortars(FireMission fm)
    {
        if (fm.Status == FireMissionStatus.Cancelled) return;
        Group? best = null;
        int bestQueue = int.MaxValue;
        foreach (var g in MortarSections(fm.Side, fm.Kind))
        {
            float d = Vector2.Distance(g.Centroid, fm.Target);
            if (d < Sr.MinRange || d > Sr.MaxRange) continue;
            int queue = g.Mortars!.Queue.Count + (g.Mortars.Current != null ? 1 : 0);
            if (queue < bestQueue) { bestQueue = queue; best = g; }
        }
        if (best == null)
        {
            fm.Status = FireMissionStatus.Refused;
            fm.DoneAt = Time;
            Log.Add(Time, -1, $"No mortars can fire {fm.Describe()} on {fm.Reason}", fm.Side);
            return;
        }
        fm.Status = FireMissionStatus.Ordered;
        var order = new Order { Kind = OrderKind.FireMission, Objective = fm.Target, Fire = fm.Kind, Rounds = fm.Rounds, Mission = fm, Radius = 10f };
        if (fm.RequestedBy != null && _hq[(int)fm.Side] != null)
            Note(best, $"HQ passes {fm.RequestedBy.Name}'s fire request to {best.Name}");
        SendOrder(best, order);
    }

    /// <summary>A fire mission order with no mission yet (one the player gave a mortar section himself): make one.</summary>
    private Order WithMission(Group g, Order o)
    {
        if (o.Kind != OrderKind.FireMission || o.Mission != null) return o;
        var fm = NewMission(g.Side, o.Objective, o.Fire, o.Rounds > 0 ? o.Rounds : Sr.PrepRounds, null, "battalion's target");
        fm.Status = FireMissionStatus.Ordered;
        return new Order { Kind = o.Kind, Objective = fm.Target, Fire = o.Fire, Rounds = fm.Rounds, Mission = fm, Radius = o.Radius };
    }

    /// <summary>The fire mission has reached the mortar section: it goes in the queue.</summary>
    private void TakeFireMission(Group g, Order o)
    {
        var fm = o.Mission!;
        if (fm.Status == FireMissionStatus.Cancelled) return;
        if (g.Mortars == null || g.CountEffective == 0) { fm.Status = FireMissionStatus.Refused; fm.DoneAt = Time; return; }
        g.Mortars.Queue.Enqueue(fm);
    }

    // ---------------------------------------------------------------- the mortars at work

    private void UpdateSupport()
    {
        foreach (var g in _groups)
        {
            if (g.Mortars is not { } ms) continue;
            // A target that has been cancelled, or whose company commander has fallen before calling for it, is dropped.
            if (ms.Current is { } cur && (cur.Status == FireMissionStatus.Cancelled || cur.Waiting && cur.RequestedBy is { CommanderDown: true }))
            {
                CancelFire(cur, "nobody left to call for it");
                ms.Current = null;
            }
            // Laid on an on-call target that hasn't been called, while someone needs fire now: deal with that first
            // and come back to the on-call target afterwards.
            if (ms.Current is { Waiting: true } idle && ms.Queue.Any(m => !m.Waiting && m.Status != FireMissionStatus.Cancelled))
            {
                var rest = ms.Queue.ToList();
                ms.Queue.Clear();
                foreach (var m in rest.Where(m => !m.Waiting)) ms.Queue.Enqueue(m);
                ms.Queue.Enqueue(idle);
                foreach (var m in rest.Where(m => m.Waiting)) ms.Queue.Enqueue(m);
                ms.Current = null;
            }
            if (ms.Current == null)
            {
                while (ms.Queue.Count > 0 && ms.Queue.Peek().Status == FireMissionStatus.Cancelled) ms.Queue.Dequeue();
                if (ms.Queue.Count == 0) continue;
                var next = ms.Queue.Dequeue();
                if (ms.Ammo(next.Kind) <= 0)
                {
                    next.Status = FireMissionStatus.Refused;
                    next.DoneAt = Time;
                    Note(g, $"{g.Name}: out of {(next.Kind == FireKind.Smoke ? "smoke" : "HE")} rounds, can't fire on {next.Reason}");
                    continue;
                }
                ms.Current = next;
                next.FiredBy = g;
                next.Status = FireMissionStatus.Firing;
                ms.ReadyAt = Time + Sr.SetUpTime;
                ms.NextRound = ms.ReadyAt;
                Note(g, $"{g.Name} lays its mortars on {next.Reason}: {next.Describe()}{(next.OnCall ? ", waiting for the call" : "")}");
            }
            var fm = ms.Current;
            if (Time < ms.NextRound) continue;
            if (fm.OnCall && (fm.CallAt < 0f || Time < fm.CallAt)) continue; // laid on, waiting for the word
            int tubes = UsableTubes(g);
            if (tubes == 0)
            {
                // Not enough crew left to work a tube: the mission (and any waiting) can't be fired.
                foreach (var m in ms.Queue.Prepend(fm)) { m.Status = m.RoundsFired > 0 ? FireMissionStatus.Done : FireMissionStatus.Refused; m.DoneAt = Time; }
                ms.Queue.Clear();
                ms.Current = null;
                Note(g, $"{g.Name} has lost too many men to work the mortars");
                continue;
            }
            // A crew hugging the ground under fire doesn't work the tubes.
            int able = g.Members.Count(m => m.IsEffective && m.Suppression < Rules.Morale.SuppressionPinned);
            tubes = Math.Min(tubes, able / Math.Max(1, Sr.CrewPerTube));
            if (tubes == 0) { ms.NextRound = Time + 1f; continue; }
            bool observed = Observed(g.Side, fm.Target);
            for (int i = 0; i < tubes && fm.RoundsFired < fm.Rounds && ms.Ammo(fm.Kind) > 0; i++) FireRound(g, fm, observed);
            ms.NextRound = Time + Sr.RoundInterval;
            if (fm.RoundsFired >= fm.Rounds || ms.Ammo(fm.Kind) <= 0)
            {
                ms.Current = null;
                if (fm.RoundsFired < fm.Rounds) Note(g, $"{g.Name} has run out of {(fm.Kind == FireKind.Smoke ? "smoke" : "HE")} rounds");
            }
        }

        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            if (_inFlight[i].Time > Time) continue;
            var (_, at, mission, crew) = _inFlight[i];
            _inFlight.RemoveAt(i);
            Land(at, mission, crew);
        }
        if (Map.Smoke.Count > 0) Map.UpdateSmoke(Time, Dt, BattleMap.V(Sr.Wind), Sr.SmokeGrowTime);
    }

    /// <summary>Someone on this side can see the target area, so the fall of shot can be corrected.</summary>
    private bool Observed(Side side, Vector2 target)
    {
        int checks = 0;
        foreach (var s in Hash.Near(side, target, 600f))
        {
            if (!s.IsEffective || s.IsTank) continue;
            if (!Map.WallBlocked(s.Position, target)) return true;
            if (++checks >= 12) break;
        }
        return false;
    }

    private void FireRound(Group g, FireMission fm, bool observed)
    {
        var ms = g.Mortars!;
        if (fm.Kind == FireKind.Smoke) ms.AmmoSmoke--; else ms.AmmoHe--;
        // The first rounds are ranging shots; once someone watching has corrected the fall of shot, they close in.
        float spread = fm.RoundsFired >= ms.Tubes && observed ? Sr.AdjustedSpread : Sr.FirstSpread;
        if (fm.Kind == FireKind.Smoke) spread *= 0.6f; // a smoke screen is laid along a line, not scattered
        var at = Map.Clamp(fm.Target + new Vector2(Rng.Gaussian(), Rng.Gaussian()) * spread, 1f);
        fm.RoundsFired++;
        var crew = g.Members.FirstOrDefault(m => m.IsEffective) ?? g.Members[0];
        crew.LastFiredTime = Time;
        float d = Vector2.Distance(g.Centroid, at);
        _inFlight.Add((Time + Sr.FlightTime + d / Sr.FlightSpeed, at, fm, crew));
        _shots.Add(new ShotEvent(crew.Id, crew.Position, at, false, false, Shell: false, Indirect: true));
        if (FirstShotTime < 0f) FirstShot(crew);
    }

    private void Land(Vector2 at, FireMission fm, Soldier crew)
    {
        fm.RoundsLanded++;
        if (fm.FirstLanded < 0f) fm.FirstLanded = Time;
        _impacts.Add(new ImpactEvent(at, fm.Kind == FireKind.Smoke, fm.Side));
        if (fm.Kind == FireKind.Smoke)
        {
            Map.AddSmoke(at, Sr.SmokeRadius, Time, Sr.SmokeDuration);
        }
        else
        {
            int before = _casualties.Count;
            Blast(crew, at, MortarHe);
            // Whoever asked for it is watching: men seen to go down there are struck off his picture.
            for (int i = before; i < _casualties.Count; i++)
            {
                int id = _casualties[i].SoldierId;
                if (_soldiers[id].Side == fm.Side) continue;
                fm.RequestedBy?.Knowledge.Forget(id);
                _hq[(int)fm.Side]?.Knowledge.Forget(id);
            }
        }
        if (fm.RoundsLanded >= fm.RoundsFired && fm.FiredBy?.Mortars?.Current != fm)
        {
            fm.Status = FireMissionStatus.Done;
            fm.DoneAt = Time;
            Note(fm.FiredBy ?? crew.Group, $"{(fm.FiredBy ?? crew.Group).Name}: fire mission on {fm.Reason} complete ({fm.RoundsLanded} rounds)");
        }
    }

    /// <summary>Fire missions still being worked on (ordered, being fired, or with rounds still to land). A request
    /// still on its way up doesn't count: if its runner is shot, nobody will ever know.</summary>
    private bool SupportBusy() => _inFlight.Count > 0 || _missions.Any(m => m.Status is FireMissionStatus.Ordered or FireMissionStatus.Firing && !m.Waiting);

    // ---------------------------------------------------------------- attaching MG sections

    /// <summary>A battalion asset joins a company (or comes back under battalion when <paramref name="c"/> is null).
    /// It moves to near the company HQ, and the commander uses it from his next plan.</summary>
    private void Attach(Group g, Company? c)
    {
        if (g.Company == c || g.IsCompanyHq || g.Mortars != null) return;
        if (g.Company is { } old)
        {
            old.Squads.Remove(g);
            old.Reports.Remove(g);
            Note(g, $"{g.Name} leaves {old.Name}");
        }
        g.Company = c;
        if (c == null) { Note(g, $"{g.Name} comes back under battalion"); return; }
        c.Squads.Add(g);
        c.Reports[g] = new SquadReport { Strength = g.CountEffective, Time = Time };
        Note(g, $"{g.Name} joins {c.Name}", LogKind.Key);
        var at = Map.Nav.NearestWalkable(Map.Clamp(c.HqGroup.HoldPosition - c.HqGroup.Rear * 20f, 4f));
        ApplyOrder(g, new Order { Kind = OrderKind.Advance, Objective = at, Radius = 15f, IsWaypoint = true });
    }
}
