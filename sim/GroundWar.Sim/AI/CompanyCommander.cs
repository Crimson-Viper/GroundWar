using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.AI;

/// <summary>
/// The company commander (milestone 5). He gets an order from battalion HQ ("attack the farmhouse"), weighs up a
/// few ways of doing it against what his squads have reported, and picks one to suit his personality. He sends
/// the plan back to HQ (the brief-back), runs it phase by phase by sending his squads their own orders, shifts
/// covering fire onto machine guns that pin his men, and makes a new plan when the old one goes wrong. If every
/// way of doing it looks like a death trap he refuses and says why; if HQ sends the same order again, he obeys.
/// See docs/systems/company-commander.md.
/// </summary>
public sealed class CompanyCommander
{
    private readonly Battle _b;

    public CompanyCommander(Battle battle) { _b = battle; }

    private CompanyRules R => _b.Rules.Company;
    private float Time => _b.Time;

    /// <summary>A good commander takes stock more often: 13 s at competence 1, 5 s at competence 5.</summary>
    public static float ThinkInterval(TraitsDef t) => MathF.Max(5f, 15f - 2f * t.Competence);

    /// <summary>Expected loss above which a plan is a death trap for this commander. Aggressive and steady men
    /// accept more; cautious men less.</summary>
    public static float DeathTrapThreshold(TraitsDef t, CompanyRules r) =>
        r.DeathTrapLoss + 0.07f * (t.Aggression - 3) - 0.05f * (t.Caution - 3) + 0.03f * (t.Composure - 3);

    // ---------------------------------------------------------------- each think

    public void Think(Company c)
    {
        c.NextThink = Time + ThinkInterval(c.Traits) * _b.Rng.Range(0.9f, 1.1f);
        if (c.CommanderDown) return;
        if (c.HqGroup.CountEffective == 0)
        {
            c.CommanderDown = true;
            c.Status = "Commander down: squads carry on with their last orders";
            _b.Note(c.HqGroup, $"{c.CommanderName} of {c.Name} is down: the squads carry on with their last orders", LogKind.Key);
            return;
        }
        // What he and his radio operator see themselves goes straight into his picture.
        foreach (var k in c.HqGroup.Knowledge.Contacts) Merge(c, k.EnemyId, k.LastPosition, k.LastSeen, k.Confidence);
        PlaceHq(c);

        switch (c.State)
        {
            case CompanyState.BriefBack:
                // The brief-back or HQ's answer went astray (a runner shot): don't wait for ever.
                if (Time - c.PlanMadeAt > 150f) StartPlan(c, "no answer from HQ, going ahead");
                break;
            case CompanyState.Executing:
                RunPlan(c);
                break;
            case CompanyState.Refused:
                // He asked for mortar fire when he refused: once it has landed and he has seen what it did, he thinks again.
                if (c.RefusalFire is { Status: FireMissionStatus.Done or FireMissionStatus.Refused } rf && Time - rf.DoneAt > 15f)
                {
                    c.RefusalFire = null;
                    if (rf.Status == FireMissionStatus.Done && c.RefusedOrder != null && c.Traits.Initiative >= 3) Reconsider(c);
                }
                break;
        }
        if (c.State != CompanyState.Executing || c.Plan?.Kind == PlanKind.Defend) DefensiveFire(c);

        if (Time - c.LastSitrep >= _b.Rules.Comms.CompanySitrepInterval && (c.State == CompanyState.Executing || c.Knowledge.Count > 0))
        {
            c.LastSitrep = Time;
            string doing = c.State switch
            {
                CompanyState.Executing when c.Phase != null => $"phase {c.PhaseIndex + 1} of {c.Plan!.Phases.Count} ({c.Phase.Name.ToLowerInvariant()})",
                CompanyState.AwaitingOrders => "waiting for orders",
                CompanyState.BriefBack => "waiting for an answer to the brief-back",
                CompanyState.Refused => "holding, waiting for support",
                _ => "holding",
            };
            _b.SendUp(c, $"situation report: {c.Effective}/{c.StartStrength} fit, {doing}, {c.Knowledge.Count} enemy known", MessageKind.Sitrep, null, withContacts: true);
        }
        c.Status = StatusText(c);
    }

    private static string StatusText(Company c) => c.CommanderDown ? "Commander down" : c.State switch
    {
        CompanyState.AwaitingOrders => "Waiting for orders",
        CompanyState.BriefBack => c.BriefBackHeardAt >= 0f ? "Brief-back with HQ: waiting for the go-ahead" : "Brief-back on its way to HQ",
        CompanyState.Executing => c.Phase != null ? $"Phase {c.PhaseIndex + 1}/{c.Plan!.Phases.Count}: {c.Phase.Name}" : "Carrying out the plan",
        CompanyState.Refused => $"Refused: {c.RefusalReason}",
        _ => c.Plan != null ? $"Holding, plan finished ({PlanName(c.Plan.Kind)})" : "Holding",
    };

    internal void Merge(Company c, int enemyId, Vector2 pos, float seenAt, float confidence)
    {
        var enemy = _b.GetSoldier(enemyId);
        if (enemy == null || enemy.Status is SoldierStatus.Dead) return;
        var known = c.Knowledge.Get(enemyId);
        if (known != null && known.LastSeen >= seenAt) return;
        c.Knowledge.Report(enemy, pos, seenAt, confidence);
    }

    /// <summary>The company HQ stays a little behind the middle of its squads: close enough to shout or send a
    /// runner, far enough back not to be in the firefight.</summary>
    private void PlaceHq(Company c)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (var g in c.Squads) if (g.CountEffective > 0) { sum += g.Centroid; n++; }
        if (n == 0) return;
        var center = sum / n;
        var rear = c.HqGroup.Rear;
        var side = new Vector2(-rear.Y, rear.X);
        // Of a few spots behind the company, the one least in view of the enemy he knows about.
        var threats = new List<Threat>();
        foreach (var k in c.Knowledge.Contacts) threats.Add(new Threat { Position = k.LastPosition, Fire = k.IsMachineGun ? 3f : 1f });
        Vector2 want = center + rear * R.HqBehind;
        float best = float.MaxValue;
        foreach (float back in new[] { R.HqBehind * 0.7f, R.HqBehind, R.HqBehind * 1.4f })
        foreach (float across in new[] { -30f, 0f, 30f })
        {
            var p = _b.Map.Nav.NearestWalkable(_b.Map.Clamp(center + rear * back + side * across, 4f));
            float score = (threats.Count > 0 ? FireAt(p, threats) : 0f) + MathF.Abs(back - R.HqBehind) * 0.02f + MathF.Abs(across) * 0.01f;
            if (score < best) { best = score; want = p; }
        }
        if (Vector2.Distance(want, c.HqGroup.HoldPosition) < 20f) return;
        c.HqGroup.Order = GroupOrder.Hold;
        c.HqGroup.HoldPosition = want;
        c.HqGroup.Objective = want;
        c.HqGroup.ObjectiveRadius = 6f;
    }

    // ---------------------------------------------------------------- orders from battalion

    /// <summary>An order from battalion HQ has reached him.</summary>
    public void ReceiveOrder(Company c, Order o)
    {
        if (c.CommanderDown) return;
        bool insisted = c.RefusedOrder != null && c.RefusedOrder.Kind == o.Kind && Vector2.Distance(c.RefusedOrder.Objective, o.Objective) < 25f;
        c.CurrentOrder = o;
        c.Insisted = insisted;
        c.RefusedOrder = null;
        c.RefusalReason = "";
        c.Replans = 0;
        c.MgsEngaged.Clear();
        c.SupportSquad = null;
        c.BriefBackHeardAt = -1f;
        c.BriefBackAnswered = false;
        c.RefusalFire = null;
        c.RefusalFires = 0;
        CancelPlanned(c, "a new order");
        if (insisted) _b.Note(c.HqGroup, $"{c.CommanderName}: HQ insists. \"Understood. We'll do it.\"", LogKind.Key);

        if (o.Kind is OrderKind.Halt or OrderKind.FallBack)
        {
            // Nothing to plan: pass it straight on.
            var plan = new CompanyPlan { Kind = o.Kind == OrderKind.Halt ? PlanKind.Halt : PlanKind.FallBack, Summary = o.ToString() };
            c.Plan = plan;
            c.State = CompanyState.Holding;
            foreach (var g in c.Squads) if (g.CountEffective > 0) _b.SendCompanyOrder(c, g, o);
            c.Status = StatusText(c);
            return;
        }
        MakePlan(c, o, replan: false);
    }

    /// <summary>Battalion approved the brief-back (or the wait ran out): go.</summary>
    public void Approved(Company c)
    {
        if (c.State == CompanyState.BriefBack) StartPlan(c, null);
    }

    /// <summary>Battalion rejected the plan: stand down and wait for a new order.</summary>
    public void Rejected(Company c)
    {
        if (c.State != CompanyState.BriefBack) return;
        c.State = CompanyState.AwaitingOrders;
        c.Plan = null;
        c.CurrentOrder = null;
        CancelPlanned(c, "the plan was rejected");
        _b.Note(c.HqGroup, $"{c.CommanderName}: plan rejected, holding and waiting for orders", LogKind.Key);
        c.Status = StatusText(c);
    }

    private void MakePlan(Company c, Order o, bool replan)
    {
        CancelPlanned(c, "the plan has changed");
        c.Options.Clear();
        c.Options.AddRange(BuildOptions(c, o));
        c.Options.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (c.Options.Count == 0)
        {
            c.State = CompanyState.Holding;
            c.Plan = null;
            _b.SendUp(c, "no squads left fit to carry out the order", MessageKind.Failed, null, withContacts: false);
            return;
        }
        var best = c.Options[0];
        if (o.Kind is OrderKind.Attack or OrderKind.Advance && !c.Insisted && IsDeathTrap(c, best))
        {
            Refuse(c, o, best, replan);
            return;
        }
        c.Plan = best;
        c.PhaseIndex = 0;
        c.PlanMadeAt = Time;
        c.StrengthAtPlan = c.Effective;
        c.MgsPlannedFor.Clear();
        RememberMgs(c, o);
        c.SupportSquad = best.Support;
        string others = c.Options.Count > 1 ? $" (also weighed: {string.Join(", ", c.Options.Skip(1).Select(p => PlanName(p.Kind)))})" : "";
        _b.Note(c.HqGroup, $"{c.CommanderName} plans: {best.Summary} Expects to lose {best.ExpectedLoss * 100:0}%, {best.Success * 100:0}% chance{others}", LogKind.Key);

        if (replan)
        {
            _b.SendUp(c, $"changing plan: {best.Summary}", MessageKind.Note, best, withContacts: true);
            StartPlan(c, null);
            return;
        }
        // The player's commanders brief back and wait; the enemy's just get on with it.
        bool waits = _b.Hq(c.Side) != null && c.Side == _b.PlayerSide;
        if (waits)
        {
            c.State = CompanyState.BriefBack;
            _b.SendUp(c, $"brief-back: {best.Summary} Expect to lose about {best.ExpectedLoss * 100:0}%.", MessageKind.BriefBack, best, withContacts: true);
        }
        else
        {
            _b.SendUp(c, $"going ahead: {best.Summary}", MessageKind.Note, best, withContacts: true);
            StartPlan(c, null);
        }
        c.Status = StatusText(c);
    }

    public static string PlanName(PlanKind k) => k switch
    {
        PlanKind.FrontalAssault => "frontal assault",
        PlanKind.PinAndFlankLeft => "pin and flank left",
        PlanKind.PinAndFlankRight => "pin and flank right",
        PlanKind.WaitForSupport => "wait for support",
        PlanKind.Defend => "defend",
        PlanKind.Advance => "advance",
        PlanKind.Halt => "halt",
        PlanKind.Suppress => "covering fire",
        _ => "fall back",
    };

    private bool IsDeathTrap(Company c, CompanyPlan p) =>
        p.ExpectedLoss > DeathTrapThreshold(c.Traits, R) || p.Success < R.DeathTrapSuccess;

    /// <summary>Every way of doing it looks like a death trap: say so, and why, and hold. If he was already
    /// part-way through an attack, a commander with poor composure pulls his men back rather than just stopping.</summary>
    private void Refuse(Company c, Order o, CompanyPlan best, bool midAttack)
    {
        c.State = CompanyState.Refused;
        c.RefusedOrder = o;
        c.Plan = new CompanyPlan { Kind = PlanKind.WaitForSupport, Summary = "Hold and wait for support" };
        int mgs = MgsNear(c, o);
        c.RefusalReason = $"expect to lose about {best.ExpectedLoss * 100:0}% of the company{(mgs > 0 ? $" to {mgs} machine gun{(mgs > 1 ? "s" : "")}" : "")}, " +
                          $"with a {best.Success * 100:0}% chance of taking it";
        // He asks battalion for the mortars to work the objective over; when they're done he'll think again.
        string asked = "";
        if (c.RefusalFires < 2 && _b.HasMortars(c.Side, FireKind.He))
        {
            var target = NearestMg(c, o, c.Centroid) ?? o.Objective;
            c.RefusalFire = AskForFire(c, target, FireKind.He, S.PrepRounds, mgs > 0 ? "the machine guns at the objective" : "the objective");
            if (c.RefusalFire != null) { c.RefusalFires++; asked = " Have asked for mortar fire on it."; }
        }
        bool withdraw = midAttack && c.Traits.Composure <= 2 && c.Effective < c.StartStrength * 0.6f;
        var squadOrder = new Order { Kind = withdraw ? OrderKind.FallBack : OrderKind.Halt };
        if (midAttack || withdraw)
            foreach (var g in c.Squads) if (g.CountEffective > 0) _b.SendCompanyOrder(c, g, squadOrder);
        string what = midAttack ? (withdraw ? "the attack has failed, falling back" : "the attack has stalled, holding") : $"cannot carry out \"{o}\"";
        _b.SendUp(c, $"{what}: {c.RefusalReason}. Request support.{asked} Send the order again if it must be done.", MessageKind.Refusal, best, withContacts: true);
        _b.Note(c.HqGroup, $"{c.CommanderName} refuses: {c.RefusalReason}", LogKind.Key);
        c.Status = StatusText(c);
    }

    // ---------------------------------------------------------------- running the plan

    private void StartPlan(Company c, string? why)
    {
        if (c.Plan == null) return;
        if (why != null) _b.Note(c.HqGroup, $"{c.CommanderName}: {why}");
        c.State = CompanyState.Executing;
        c.PlanStarted = Time;
        c.PhaseIndex = 0;
        // The fire plan goes to battalion now, as "on call" targets: by the time the assault is ready the mortars
        // are laid on and only need the word.
        CancelPlanned(c, "the plan has changed");
        foreach (var f in c.Plan.Fires)
            if (AskForFire(c, f.Target, f.Kind, f.Rounds, f.Reason, onCall: true) is { } fm) c.PlannedMissions[f] = fm;
        StartPhase(c);
    }

    private void StartPhase(Company c)
    {
        var ph = c.Phase;
        if (ph == null) { c.State = CompanyState.Holding; return; }
        c.PhaseStarted = Time;
        foreach (var t in ph.Tasks)
            if (t.Squad.CountEffective > 0) _b.SendCompanyOrder(c, t.Squad, t.Order);
        foreach (var f in c.Plan!.Fires)
        {
            if (f.Phase != c.PhaseIndex || !c.PlannedMissions.TryGetValue(f, out var fm) || !fm.Waiting) continue;
            if (f.Kind == FireKind.He && DangerClose(c.Side, f.Target))
            {
                _b.CancelFire(fm, "our own men are too close");
                _b.Note(c.HqGroup, $"{c.CommanderName}: our men are too close to {f.Reason}, no mortar fire there");
                continue;
            }
            _b.CallFire(c, fm);
        }
        _b.Note(c.HqGroup, $"{c.Name}: phase {c.PhaseIndex + 1}/{c.Plan!.Phases.Count}, {ph.Name.ToLowerInvariant()}", LogKind.Key);
        if (ph.Kind is PhaseKind.Consolidate or PhaseKind.Hold)
        {
            CancelPlanned(c, "not needed any more");
            c.State = CompanyState.Holding;
            if (ph.Kind == PhaseKind.Consolidate)
                _b.SendUp(c, "objective taken, consolidating", MessageKind.Taken, null, withContacts: true);
        }
        c.Status = StatusText(c);
    }

    private void RunPlan(Company c)
    {
        var ph = c.Phase;
        if (ph == null) { c.State = CompanyState.Holding; return; }
        var o = c.CurrentOrder!;
        float elapsed = Time - c.PhaseStarted;

        // A squad says it has taken the objective: everyone consolidates on it.
        if (c.Squads.Any(g => g.LastOrderReceived?.Kind == OrderKind.Attack && c.Reports.TryGetValue(g, out var r) && r.Taken && r.Time >= c.PlanStarted))
        {
            int consolidate = c.Plan!.Phases.FindIndex(p => p.Kind == PhaseKind.Consolidate);
            if (consolidate > c.PhaseIndex) { c.PhaseIndex = consolidate; StartPhase(c); return; }
        }
        if (c.Effective < c.StrengthAtPlan * (1f - R.ReplanLoss)) { Rethink(c, "heavy losses"); return; }
        if (ph.Kind is PhaseKind.MoveUp or PhaseKind.EstablishFire && NewMgs(c, o))
        {
            Rethink(c, "more machine guns than I planned for");
            return;
        }
        ShiftFire(c, ph);

        var live = ph.Tasks.Where(t => t.Squad.CountEffective > 0).ToList();
        bool done = ph.Kind switch
        {
            PhaseKind.MoveUp => live.All(Arrived),
            PhaseKind.EstablishFire => elapsed >= R.EstablishFireTime && (c.SupportSquad is not { CountEffective: > 0 } s || s.Tactic == Tactic.Suppress || elapsed > 120f),
            PhaseKind.Assault => false,
            _ => true,
        };
        if (ph.Kind == PhaseKind.Assault && live.All(t => c.Reports.TryGetValue(t.Squad, out var r) && (r.Failed || r.Broken) && r.Time >= c.PhaseStarted))
        {
            Rethink(c, "the assault failed");
            return;
        }
        if (done) { c.PhaseIndex++; StartPhase(c); return; }
        if (elapsed > ph.TimeLimit)
        {
            if (ph.Kind == PhaseKind.MoveUp && live.Count(Arrived) * 2 >= live.Count) { c.PhaseIndex++; StartPhase(c); }
            else Rethink(c, $"{ph.Name.ToLowerInvariant()} has stalled");
        }
    }

    /// <summary>The squad has had its order and done it (or is doing it, for orders with no end).</summary>
    private static bool Arrived(SquadTask t)
    {
        var g = t.Squad;
        if (g.LastOrderReceived != t.Order) return false;
        if (g.IsVehicles) return true; // the tanks keep up as best they can; the plan doesn't wait for them
        return t.Order.Kind switch
        {
            OrderKind.Advance => g.Order == GroupOrder.Hold || Vector2.Distance(g.Centroid, t.Order.Objective) <= t.Order.Radius + 15f,
            OrderKind.Hold => Vector2.Distance(g.Centroid, t.Order.Objective) <= t.Order.Radius + 15f,
            OrderKind.Suppress => g.Tactic == Tactic.Suppress,
            _ => true,
        };
    }

    /// <summary>A squad has reported being pinned and he knows of a machine gun near it: he turns the support
    /// squad's fire onto that gun, and if battalion has mortars he asks for fire on it too (smoke if he's a careful
    /// man, high explosive if not). Commanders with little initiative stick to the plan.</summary>
    private void ShiftFire(Company c, PlanPhase ph)
    {
        if (c.Traits.Initiative < 3 || ph.Kind is not (PhaseKind.EstablishFire or PhaseKind.Assault or PhaseKind.MoveUp)) return;
        var support = c.SupportSquad is { CountEffective: > 0 } sq ? sq : null;
        foreach (var (g, rep) in c.Reports)
        {
            if (g == support || !rep.Pinned || Time - rep.PinnedAt > 30f || g.CountEffective == 0) continue;
            // The nearest machine gun to the pinned squad (for the mortars), and the nearest one the support squad
            // isn't already firing at.
            Contact? mg = null, fresh = null;
            float best = 150f, bestFresh = 150f;
            foreach (var k in c.Knowledge.Contacts)
            {
                if (!k.IsMachineGun) continue;
                float d = Vector2.Distance(k.LastPosition, g.Centroid);
                if (d < best) { best = d; mg = k; }
                if (d < bestFresh && !c.MgsEngaged.Contains(k.EnemyId)) { bestFresh = d; fresh = k; }
            }
            if (mg == null) continue;
            bool acted = false;
            if (support != null && fresh != null && ph.Kind != PhaseKind.MoveUp)
            {
                c.MgsEngaged.Add(fresh.EnemyId);
                _b.SendCompanyOrder(c, support, new Order { Kind = OrderKind.Suppress, Objective = fresh.LastPosition, Radius = 12f, SupportPosition = support.Centroid });
                _b.Note(c.HqGroup, $"{c.CommanderName}: {g.Name} is pinned by a machine gun, {support.Name} to switch fire onto it", LogKind.Key);
                acted = true;
            }
            var kind = c.Traits.Caution >= 3 && _b.HasMortars(c.Side, FireKind.Smoke) ? FireKind.Smoke : FireKind.He;
            if (AskForFire(c, mg.LastPosition, kind, S.QuickRounds, $"the machine gun pinning {g.Name}") != null) acted = true;
            if (acted) return;
        }
    }

    // ---------------------------------------------------------------- mortar fire (milestone 7)

    private SupportRules S => _b.Rules.Support;

    /// <summary>Asks battalion for mortar fire, unless it's high explosive that would land on his own men, or he
    /// asked for the same place a short while ago.</summary>
    private FireMission? AskForFire(Company c, Vector2 target, FireKind kind, int rounds, string reason, bool onCall = false, bool quiet = false)
    {
        if (!_b.HasMortars(c.Side, kind)) return null;
        // (A planned on-call target is part of a new plan, so it doesn't wait.)
        if (!onCall)
            foreach (var (t, at) in c.FireRequests)
                if (Time - at < S.RequestCooldown && Vector2.Distance(t, target) < 40f) return null;
        if (kind == FireKind.He && DangerClose(c.Side, target))
        {
            c.FireRequests.Add((target, Time)); // don't keep trying
            if (!quiet) _b.Note(c.HqGroup, $"{c.CommanderName}: can't call mortar fire on {reason}, our own men are too close");
            return null;
        }
        var fm = _b.RequestFire(c, target, kind, rounds, reason, onCall);
        if (fm != null) c.FireRequests.Add((target, Time));
        return fm;
    }

    /// <summary>On-call targets from the old plan that were never called: the mortars can stand down.</summary>
    private void CancelPlanned(Company c, string why)
    {
        foreach (var fm in c.PlannedMissions.Values) if (fm.Waiting) _b.CancelFire(fm, why);
        c.PlannedMissions.Clear();
    }

    private bool DangerClose(Side side, Vector2 target)
    {
        foreach (var g in _b.Groups)
            if (g.Side == side && g.CountEffective > 0 && Vector2.Distance(g.Centroid, target) < S.DangerClose) return true;
        return false;
    }

    /// <summary>The known machine gun near the objective closest to where he's coming from, if any.</summary>
    private Vector2? NearestMg(Company c, Order o, Vector2 from)
    {
        Vector2? best = null;
        float bd = float.MaxValue;
        foreach (var k in c.Knowledge.Contacts)
        {
            if (!k.IsMachineGun || Vector2.Distance(k.LastPosition, o.Objective) > o.Radius + 150f) continue;
            float d = Vector2.Distance(k.LastPosition, from);
            if (d < bd) { bd = d; best = k.LastPosition; }
        }
        return best;
    }

    /// <summary>Holding or defending: if a group of enemy infantry has been seen coming, he asks for mortar fire on
    /// it. A careful man fires on two or three men; a bold one waits for a bigger target.</summary>
    private void DefensiveFire(Company c)
    {
        if (!_b.HasMortars(c.Side, FireKind.He)) return;
        var centre = c.Centroid;
        Contact? best = null;
        int bestN = 0;
        var contacts = c.Knowledge.Contacts.ToList();
        foreach (var k in contacts)
        {
            if (Time - k.LastSeen > 30f || Vector2.Distance(k.LastPosition, centre) > S.DefensiveFireRange) continue;
            if (_b.GetSoldier(k.EnemyId) is { IsTank: true }) continue;
            int n = 0;
            foreach (var o in contacts)
                if (Time - o.LastSeen <= 30f && Vector2.DistanceSquared(o.LastPosition, k.LastPosition) < 35f * 35f) n++;
            if (n > bestN) { bestN = n; best = k; }
        }
        int need = Math.Clamp(4 - (c.Traits.Caution - 3), 2, 6);
        if (best == null || bestN < need) return;
        float d = Vector2.Distance(best.LastPosition, centre);
        AskForFire(c, best.LastPosition, FireKind.He, S.QuickRounds, $"{bestN} enemy seen {d:0} m out", quiet: true);
    }

    /// <summary>After his own mortar fire has landed on an objective he refused to attack, he weighs it up again.</summary>
    private void Reconsider(Company c)
    {
        var o = c.RefusedOrder!;
        c.RefusedOrder = null;
        c.RefusalReason = "";
        c.CurrentOrder = o;
        c.Replans = 0;
        _b.Note(c.HqGroup, $"{c.CommanderName}: the mortars have done their work, thinking again", LogKind.Key);
        MakePlan(c, o, replan: false);
    }

    private void Rethink(Company c, string why)
    {
        c.Replans++;
        if (c.Replans > R.MaxReplans || c.Traits.Initiative <= 2)
        {
            c.State = CompanyState.Holding;
            CancelPlanned(c, "the attack has been called off");
            foreach (var g in c.Squads) if (g.CountEffective > 0) _b.SendCompanyOrder(c, g, new Order { Kind = OrderKind.Halt });
            _b.SendUp(c, $"{why}: holding where we are, waiting for orders", MessageKind.Failed, null, withContacts: true);
            _b.Note(c.HqGroup, $"{c.CommanderName}: {why}, holding and asking for orders", LogKind.Key);
            c.Status = StatusText(c);
            return;
        }
        // Weigh it up again with what he knows now. If the plan he is running is still the best, carry on with it.
        var o = c.CurrentOrder!;
        var options = BuildOptions(c, o);
        options.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (options.Count > 0 && c.Plan != null && options[0].Kind == c.Plan.Kind && !IsDeathTrap(c, options[0]) && why != "the assault failed")
        {
            c.Replans--;
            c.Plan.ExpectedLoss = options[0].ExpectedLoss;
            c.Plan.Success = options[0].Success;
            c.StrengthAtPlan = c.Effective;
            RememberMgs(c, o);
            _b.Note(c.HqGroup, $"{c.CommanderName}: {why}, but the plan still holds (now expects to lose {options[0].ExpectedLoss * 100:0}%)");
            return;
        }
        _b.Note(c.HqGroup, $"{c.CommanderName}: {why}, making a new plan", LogKind.Key);
        MakePlan(c, o, replan: true);
    }

    // ---------------------------------------------------------------- the plans he weighs up

    private readonly struct Threat
    {
        public Vector2 Position { get; init; }
        public float Fire { get; init; }
        /// <summary>An enemy he assumes is there but hasn't seen: anywhere on the edge of the objective.</summary>
        public bool Assumed { get; init; }
        public float Radius { get; init; }
    }

    private bool NewMgs(Company c, Order o) =>
        c.Knowledge.Contacts.Any(k => k.IsMachineGun && !c.MgsPlannedFor.Contains(k.EnemyId) && Vector2.Distance(k.LastPosition, o.Objective) <= o.Radius + 150f);

    private static void RememberMgs(Company c, Order o)
    {
        foreach (var k in c.Knowledge.Contacts)
            if (k.IsMachineGun && Vector2.Distance(k.LastPosition, o.Objective) <= o.Radius + 150f) c.MgsPlannedFor.Add(k.EnemyId);
    }

    private int MgsNear(Company c, Order o) =>
        c.Knowledge.Contacts.Count(k => k.IsMachineGun && Vector2.Distance(k.LastPosition, o.Objective) <= o.Radius + 150f);

    /// <summary>The enemy he expects: what his squads have reported (a machine gun counts as three rifles),
    /// plus, when little is known about the objective, an enemy he assumes is there. A cautious man assumes more.</summary>
    private List<Threat> Threats(Company c, Order o, out float fireAtObjective)
    {
        var list = new List<Threat>();
        float near = 0f;
        foreach (var k in c.Knowledge.Contacts)
        {
            float fire = k.IsMachineGun ? 3f : 1f;
            list.Add(new Threat { Position = k.LastPosition, Fire = fire });
            if (Vector2.Distance(k.LastPosition, o.Objective) <= o.Radius + 100f) near += fire;
        }
        float assumed = R.AssumedEnemyFire + 2f * c.Traits.Caution;
        float extra = near <= 0f ? assumed : MathF.Max(0f, assumed - near) * 0.5f;
        if (extra > 0f) list.Add(new Threat { Position = o.Objective, Fire = extra, Assumed = true, Radius = o.Radius });
        fireAtObjective = near + extra;
        return list;
    }

    /// <summary>Enemy fire a man standing here would be under: each threat that can see the spot, less with range,
    /// and less if there's cover to use.</summary>
    private float FireAt(Vector2 p, List<Threat> threats)
    {
        int idx = _b.Grid.IndexOf(p);
        float cover = idx >= 0 ? _b.Grid.Cover[idx] : 0f;
        float total = 0f;
        foreach (var t in threats)
        {
            var from = t.Position;
            if (t.Assumed)
            {
                var off = p - t.Position;
                float len = off.Length();
                if (len > 0.1f) from = t.Position + off / len * MathF.Min(t.Radius, len);
            }
            float d = Vector2.Distance(from, p);
            if (d > 450f) continue;
            if (d > 3f && _b.Map.SightBlocked(from, p)) continue;
            total += t.Fire * (1f - d / 450f);
        }
        return total * (1f - 0.8f * cover);
    }

    /// <summary>Seconds-under-fire for moving along a route at a given speed.</summary>
    private float RouteExposure(Vector2 from, Vector2 to, float speed, List<Threat> threats, float factor)
    {
        var path = _b.Map.Nav.FindPath(from, to) ?? new List<Vector2> { to };
        float exposure = 0f;
        var a = from;
        foreach (var b in path)
        {
            float len = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)(len / 15f));
            for (int i = 0; i < steps; i++)
            {
                var p = Vector2.Lerp(a, b, (i + 0.5f) / steps);
                exposure += FireAt(p, threats) * (len / steps) / speed;
            }
            a = b;
        }
        return exposure * factor;
    }

    private static float PathLength(Vector2 a, Vector2 b) => Vector2.Distance(a, b) * 1.15f;

    private List<CompanyPlan> BuildOptions(Company c, Order o)
    {
        var squads = c.Squads.Where(g => g.CountEffective >= 2 && !g.HasBroken && g.Nerve < SquadNerve.Wavering && !g.IsVehicles).ToList();
        var plans = new List<CompanyPlan>();
        if (squads.Count == 0) return plans;
        Vector2 start = Vector2.Zero;
        foreach (var g in squads) start += g.Centroid;
        start /= squads.Count;
        var O = o.Objective;
        var dir = Geo.SafeNormalize(O - start, -c.HqGroup.Rear);
        var perp = new Vector2(-dir.Y, dir.X);
        // Left to right across the line of advance, so their routes don't cross.
        squads.Sort((a, b) => Vector2.Dot(a.Centroid - start, perp).CompareTo(Vector2.Dot(b.Centroid - start, perp)));

        switch (o.Kind)
        {
            case OrderKind.Hold: plans.Add(DefendPlan(c, o, squads)); break;
            case OrderKind.Suppress: plans.Add(SuppressPlan(o, squads, start, perp)); break;
            case OrderKind.Advance: plans.Add(AdvancePlan(o, squads, perp)); break;
            case OrderKind.Attack:
                // Mortar support: a careful man always wants the objective worked over first; a bold one only
                // bothers if he knows of machine guns there.
                var t = c.Traits;
                int mgs = MgsNear(c, o);
                bool he = _b.HasMortars(c.Side, FireKind.He) && (t.Caution >= 3 || mgs > 0) && !(t.Aggression >= 4 && t.Caution <= 2 && mgs == 0);
                bool smoke = _b.HasMortars(c.Side, FireKind.Smoke) && (t.Caution >= 3 || mgs > 0);
                var heAt = NearestMg(c, o, start) ?? o.Objective;
                string heWhat = NearestMg(c, o, start) != null ? "the machine gun at the objective" : "the objective";
                plans.Add(FrontalPlan(o, squads, start, dir, perp, he ? heAt : null, heWhat, smoke));
                if (squads.Count >= 2)
                {
                    plans.Add(FlankPlan(o, squads, dir, perp, +1, he ? heAt : null, heWhat));
                    plans.Add(FlankPlan(o, squads, dir, perp, -1, he ? heAt : null, heWhat));
                }
                break;
        }
        var tanks = c.Squads.Where(g => g.IsVehicles && g.Members.Any(m => m.IsActive)).ToList();
        if (tanks.Count > 0) foreach (var p in plans) AddTanks(c, p, tanks, o, dir);
        var threats = Threats(c, o, out float enemyFire);
        foreach (var p in plans) Evaluate(c, p, threats, enemyFire, squads);
        return plans;
    }

    /// <summary>Tanks have joined the company after he made his plan. If he had refused the attack as a death trap,
    /// the tanks are the support he asked for, so he thinks again. Otherwise he gives them their part in the plan he
    /// is running (or about to run) and, if it's under way, sends them their orders for the phase it's in.</summary>
    public void TanksJoined(Company c, Group tanks)
    {
        if (c.State == CompanyState.Refused && c.RefusedOrder != null)
        {
            _b.Note(c.HqGroup, $"{c.CommanderName}: {tanks.Name} has come up, thinking again", LogKind.Key);
            Reconsider(c);
            return;
        }
        if (c.Plan == null || c.CurrentOrder == null || c.Plan.Phases.Count == 0 || c.Plan.Tanks.Contains(tanks)) return;
        var start = c.Centroid;
        var dir = Geo.SafeNormalize(c.CurrentOrder.Objective - start, -c.HqGroup.Rear);
        string summary = c.Plan.Summary;
        AddTanks(c, c.Plan, new List<Group> { tanks }, c.CurrentOrder, dir);
        string part = c.Plan.Summary[summary.Length..].Trim();
        c.Plan.Summary = summary + " " + part;
        _b.Note(c.HqGroup, $"{c.CommanderName}: {tanks.Name} has joined. {part}", LogKind.Key);
        if (c.State is CompanyState.Executing or CompanyState.Holding && c.Phase is { } ph)
            foreach (var t in ph.Tasks)
                if (t.Squad == tanks) _b.SendCompanyOrder(c, tanks, t.Order);
    }

    /// <summary>Attached tanks (milestone 8) go with the infantry: in an attack they move up behind the squads,
    /// give covering fire with the support squad in a pin-and-flank, and go in with the assault; in a defence they
    /// sit behind the line, ready to fire on anything that gets close.</summary>
    private void AddTanks(Company c, CompanyPlan plan, List<Group> tanks, Order o, Vector2 dir)
    {
        PlanPhase? Phase(PhaseKind k) => plan.Phases.FirstOrDefault(p => p.Kind == k);
        string names = JoinNames(tanks);
        foreach (var t in tanks)
        {
            plan.Tanks.Add(t);
            var from = t.Centroid;
            switch (plan.Kind)
            {
                case PlanKind.Defend:
                    var back = Spot(o.Objective - c.HqGroup.Facing * 40f);
                    Task(plan.Phases[0], t, Make(OrderKind.Hold, back, 20f));
                    plan.Arrows.Add((from, back, 0));
                    break;
                case PlanKind.Advance:
                    Task(plan.Phases[0], t, Make(OrderKind.Advance, o.Objective, 20f));
                    if (Phase(PhaseKind.Hold) is { } hold) Task(hold, t, Make(OrderKind.Hold, Spot(o.Objective - dir * 20f), 20f));
                    break;
                case PlanKind.Suppress:
                    Task(plan.Phases[0], t, Make(OrderKind.Suppress, o.Objective, o.Radius, o.SupportPosition ?? from));
                    break;
                case PlanKind.FrontalAssault:
                case PlanKind.PinAndFlankLeft:
                case PlanKind.PinAndFlankRight:
                    var support = plan.Support != null ? plan.Phases[0].Tasks.FirstOrDefault(x => x.Squad == plan.Support)?.Order.Objective : null;
                    if (Phase(PhaseKind.MoveUp) is { } move)
                    {
                        var at = support ?? Spot(o.Objective - dir * (R.StartLineDistance + 15f));
                        Task(move, t, MoveTo(at, 20f));
                        plan.Arrows.Add((from, at, 0));
                        from = at;
                    }
                    if (Phase(PhaseKind.EstablishFire) is { } fire) Task(fire, t, Make(OrderKind.Suppress, o.Objective, o.Radius + 5f, from));
                    if (Phase(PhaseKind.Assault) is { } assault)
                    {
                        Task(assault, t, Make(OrderKind.Attack, o.Objective, o.Radius));
                        plan.Arrows.Add((from, o.Objective, plan.Phases.IndexOf(assault)));
                    }
                    if (Phase(PhaseKind.Consolidate) is { } cons) Task(cons, t, Make(OrderKind.Hold, Spot(o.Objective - dir * 20f), 20f));
                    break;
            }
        }
        plan.Summary += plan.Kind switch
        {
            PlanKind.Defend => $" {names} behind the line.",
            PlanKind.FrontalAssault => $" {names} leads the assault.",
            PlanKind.PinAndFlankLeft or PlanKind.PinAndFlankRight => $" {names} gives covering fire with the support squad, then goes in with the assault.",
            _ => $" {names} goes with them.",
        };
    }

    private static float Offset(int i, int n, float spacing) => (i - (n - 1) * 0.5f) * spacing;

    private Vector2 Spot(Vector2 p) => _b.Map.Nav.NearestWalkable(_b.Map.Clamp(p, 5f));

    private static Order Make(OrderKind k, Vector2 at, float radius, Vector2? support = null) =>
        new() { Kind = k, Objective = at, Radius = radius, SupportPosition = support };

    /// <summary>Move there and wait: a start line, an assembly area or a support position.</summary>
    private static Order MoveTo(Vector2 at, float radius) => new() { Kind = OrderKind.Advance, Objective = at, Radius = radius, IsWaypoint = true };

    private static void Task(PlanPhase ph, Group g, Order o) => ph.Tasks.Add(new SquadTask { Squad = g, Order = o });

    private CompanyPlan DefendPlan(Company c, Order o, List<Group> squads)
    {
        var facing = c.HqGroup.Facing;
        var perp = new Vector2(-facing.Y, facing.X);
        var plan = new CompanyPlan { Kind = PlanKind.Defend, Summary = $"Defend: {squads.Count} squads in a line {40 * (squads.Count - 1)} m wide, facing the enemy." };
        var ph = new PlanPhase { Kind = PhaseKind.Hold, Name = "Defend" };
        for (int i = 0; i < squads.Count; i++)
        {
            var at = DefenceSpot(o.Objective + perp * Offset(i, squads.Count, 40f), facing);
            Task(ph, squads[i], Make(OrderKind.Hold, at, 15f));
            plan.Arrows.Add((squads[i].Centroid, at, 0));
        }
        plan.Phases.Add(ph);
        return plan;
    }

    private CompanyPlan SuppressPlan(Order o, List<Group> squads, Vector2 start, Vector2 perp)
    {
        var from = o.SupportPosition ?? start;
        var plan = new CompanyPlan { Kind = PlanKind.Suppress, Summary = "Whole company gives covering fire on the objective." };
        var ph = new PlanPhase { Kind = PhaseKind.EstablishFire, Name = "Covering fire", TimeLimit = float.MaxValue };
        for (int i = 0; i < squads.Count; i++)
        {
            var at = Spot(from + perp * Offset(i, squads.Count, 30f));
            Task(ph, squads[i], Make(OrderKind.Suppress, o.Objective, o.Radius, at));
            plan.Arrows.Add((squads[i].Centroid, at, 0));
        }
        plan.Phases.Add(ph);
        return plan;
    }

    private CompanyPlan AdvancePlan(Order o, List<Group> squads, Vector2 perp)
    {
        var plan = new CompanyPlan { Kind = PlanKind.Advance, Summary = $"Advance: {squads.Count} squads abreast to the objective, then hold it." };
        var move = new PlanPhase { Kind = PhaseKind.MoveUp, Name = "Advance", TimeLimit = 600f };
        var hold = new PlanPhase { Kind = PhaseKind.Hold, Name = "Hold the objective" };
        for (int i = 0; i < squads.Count; i++)
        {
            var at = Spot(o.Objective + perp * Offset(i, squads.Count, 35f));
            Task(move, squads[i], Make(OrderKind.Advance, at, 12f));
            Task(hold, squads[i], Make(OrderKind.Hold, at, 15f));
            plan.Arrows.Add((squads[i].Centroid, at, 0));
        }
        plan.Phases.Add(move);
        plan.Phases.Add(hold);
        return plan;
    }

    private CompanyPlan FrontalPlan(Order o, List<Group> squads, Vector2 start, Vector2 dir, Vector2 perp, Vector2? heAt, string heWhat, bool smoke)
    {
        int n = squads.Count;
        var plan = new CompanyPlan { Kind = PlanKind.FrontalAssault };
        bool startLine = Vector2.Distance(start, o.Objective) > R.StartLineDistance + 30f;
        var move = new PlanPhase { Kind = PhaseKind.MoveUp, Name = "Move up to the start line", TimeLimit = 300f };
        var assault = new PlanPhase { Kind = PhaseKind.Assault, Name = heAt != null || smoke ? "Assault behind the mortar fire" : "Assault", TimeLimit = 360f };
        var consolidate = new PlanPhase { Kind = PhaseKind.Consolidate, Name = "Consolidate on the objective" };
        for (int i = 0; i < n; i++)
        {
            float off = Offset(i, n, 35f);
            var line = startLine ? Spot(o.Objective - dir * R.StartLineDistance + perp * off) : squads[i].Centroid;
            if (startLine)
            {
                Task(move, squads[i], MoveTo(line, 12f));
                plan.Arrows.Add((squads[i].Centroid, line, 0));
            }
            var target = o.Objective + perp * off * 0.3f;
            Task(assault, squads[i], Make(OrderKind.Attack, target, o.Radius));
            plan.Arrows.Add((line, target, startLine ? 1 : 0));
            Task(consolidate, squads[i], Make(OrderKind.Hold, Spot(o.Objective + perp * off * 0.4f), MathF.Max(12f, o.Radius * 0.7f)));
        }
        if (startLine) plan.Phases.Add(move);
        // The mortars are called as the squads leave the start line: the bombs fall while they cross the open
        // ground and lift as they close in, and the smoke builds up on the near edge of the objective so the
        // defenders can't see them coming.
        int at = plan.Phases.Count;
        if (heAt != null) plan.Fires.Add(new PlannedFire { Phase = at, Target = heAt.Value, Kind = FireKind.He, Rounds = S.PrepRounds, Reason = heWhat });
        if (smoke) plan.Fires.Add(new PlannedFire { Phase = at, Target = Spot(o.Objective - dir * (o.Radius + 12f)), Kind = FireKind.Smoke, Rounds = S.SmokeRounds, Reason = "the near edge of the objective (smoke screen)" });
        plan.Phases.Add(assault);
        plan.Phases.Add(consolidate);
        plan.Summary = $"Frontal assault: all {n} squads {(startLine ? $"form up {R.StartLineDistance:0} m short of the objective and " : "")}go straight in together." +
                       (heAt != null || smoke ? $" Mortars fire as they go: {MortarText(heAt != null, smoke)}." : "");
        return plan;
    }

    private static string MortarText(bool he, bool smoke) =>
        he && smoke ? "high explosive on the objective and a smoke screen in front of it"
        : he ? "high explosive on the objective" : "a smoke screen to cover the assault";

    private CompanyPlan FlankPlan(Order o, List<Group> squads, Vector2 dir, Vector2 perp, int side, Vector2? heAt, string heWhat)
    {
        // The squad with the most machine guns gives covering fire; the rest go round.
        // (Heavy machine guns count for more than light ones.)
        var support = squads.OrderByDescending(g => g.Members.Where(m => m.IsEffective && m.Weapon.IsMachineGun).Sum(m => m.Weapon.Threat))
                            .ThenBy(g => MathF.Abs(Vector2.Dot(g.Centroid - o.Objective, perp))).First();
        var flankers = squads.Where(g => g != support).ToList();
        // Those already on that side go first.
        flankers.Sort((a, b) => (side * Vector2.Dot(b.Centroid, perp)).CompareTo(side * Vector2.Dot(a.Centroid, perp)));
        string sideName = side > 0 ? "right" : "left";
        var plan = new CompanyPlan { Kind = side > 0 ? PlanKind.PinAndFlankRight : PlanKind.PinAndFlankLeft, Support = support };
        var supportAt = SupportSpot(o, dir, perp);
        var assembly = AssemblySpot(o, dir, perp, side);

        var move = new PlanPhase { Kind = PhaseKind.MoveUp, Name = $"Move up: support position and {sideName} flank", TimeLimit = 360f };
        var fire = new PlanPhase { Kind = PhaseKind.EstablishFire, Name = "Covering fire", TimeLimit = 150f };
        var assault = new PlanPhase { Kind = PhaseKind.Assault, Name = $"Assault from the {sideName}", TimeLimit = 360f };
        var consolidate = new PlanPhase { Kind = PhaseKind.Consolidate, Name = "Consolidate on the objective" };

        Task(move, support, MoveTo(supportAt, 10f));
        Task(fire, support, Make(OrderKind.Suppress, o.Objective, o.Radius + 5f, supportAt));
        plan.Arrows.Add((support.Centroid, supportAt, 0));
        plan.Arrows.Add((supportAt, o.Objective, 1));
        for (int i = 0; i < flankers.Count; i++)
        {
            var g = flankers[i];
            var at = Spot(assembly - dir * (i * 20f) + perp * side * (i * 8f));
            var target = o.Objective + perp * side * Offset(i, flankers.Count, 12f);
            Task(move, g, MoveTo(at, 12f));
            Task(fire, g, Make(OrderKind.Hold, at, 12f));
            Task(assault, g, Make(OrderKind.Attack, target, o.Radius));
            Task(consolidate, g, Make(OrderKind.Hold, Spot(o.Objective + perp * side * Offset(i, flankers.Count, 15f)), MathF.Max(12f, o.Radius * 0.7f)));
            plan.Arrows.Add((g.Centroid, at, 0));
            plan.Arrows.Add((at, target, 2));
        }
        Task(consolidate, support, MoveTo(Spot(o.Objective - dir * 25f), 12f));
        plan.Phases.Add(move);
        plan.Phases.Add(fire);
        plan.Phases.Add(assault);
        plan.Phases.Add(consolidate);
        // The mortars are called as the flankers go in: the bombs keep the defenders' heads down while they cross
        // the last stretch, and lift as they close.
        if (heAt != null) plan.Fires.Add(new PlannedFire { Phase = 2, Target = heAt.Value, Kind = FireKind.He, Rounds = S.PrepRounds, Reason = heWhat });
        plan.Summary = $"Pin and flank {sideName}: {support.Name} gives covering fire from {R.SupportDistance:0} m out while " +
                       $"{JoinNames(flankers)} go round the {sideName} and assault." + (heAt != null ? " Mortars fire on the objective as the assault goes in." : "");
        return plan;
    }

    private static string JoinNames(List<Group> groups) => groups.Count == 1 ? groups[0].Name
        : string.Join(", ", groups.Take(groups.Count - 1).Select(g => g.Name)) + " and " + groups[^1].Name;

    /// <summary>Somewhere about <see cref="CompanyRules.SupportDistance"/> short of the objective, with cover and a
    /// clear view of it. Higher ground is better: it sees over the men going in.</summary>
    private Vector2 SupportSpot(Order o, Vector2 dir, Vector2 perp)
    {
        var baseAt = o.Objective - dir * R.SupportDistance;
        var edge = o.Objective - dir * o.Radius * 0.6f;
        float objHeight = _b.Map.Terrain.HeightAt(o.Objective);
        Vector2 best = Spot(baseAt);
        float bestScore = float.MinValue;
        for (int a = -3; a <= 3; a++)
        for (int b = -2; b <= 1; b++)
        {
            var p = Spot(baseAt + perp * (a * 15f) + dir * (b * 15f));
            int idx = _b.Grid.IndexOf(p);
            float score = (idx >= 0 ? _b.Grid.Cover[idx] : 0f) + (_b.Map.SightBlocked(p, edge) ? 0f : 1.5f) - MathF.Abs(a) * 0.05f
                          + Math.Clamp((_b.Map.Terrain.HeightAt(p) - objHeight) * _b.Rules.Terrain.HighGroundBonus, -0.6f, 0.6f);
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    /// <summary>Where the flankers form up before going in: out to the side of the objective, and if the ground
    /// allows it somewhere the defenders can't see (behind a crest, a wood or a hedgerow), with cover.</summary>
    private Vector2 AssemblySpot(Order o, Vector2 dir, Vector2 perp, int side)
    {
        Vector2 best = Spot(o.Objective + perp * side * R.FlankWidth - dir * 40f);
        float bestScore = float.MinValue;
        foreach (float wide in new[] { 1f, 1.4f, 1.8f })
        foreach (float back in new[] { 40f, 15f, 70f })
        {
            var p = Spot(o.Objective + perp * side * R.FlankWidth * wide - dir * back);
            if (Vector2.Distance(p, o.Objective) < o.Radius + 25f) continue;
            int idx = _b.Grid.IndexOf(p);
            float score = (_b.Map.SightBlocked(o.Objective, p) ? 1.5f : 0f) + (idx >= 0 ? _b.Grid.Cover[idx] : 0f)
                          - (wide - 1f) * 0.6f - MathF.Abs(back - 40f) * 0.004f;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    /// <summary>A squad's place in a defence: near its slot in the line, but shifted to the best ground within
    /// a few metres: cover to fight from, a view of the way the enemy will come, and height.</summary>
    private Vector2 DefenceSpot(Vector2 slot, Vector2 facing)
    {
        var perp = new Vector2(-facing.Y, facing.X);
        Vector2 best = Spot(slot);
        float bestScore = float.MinValue;
        float h0 = _b.Map.Terrain.HeightAt(slot);
        for (int a = -1; a <= 1; a++)
        for (int b = -1; b <= 1; b++)
        {
            var p = Spot(slot + perp * (a * 10f) + facing * (b * 12f));
            int idx = _b.Grid.IndexOf(p);
            if (idx < 0) continue;
            int seen = 0;
            foreach (float ang in new[] { -0.5f, 0f, 0.5f })
                if (!_b.Map.SightBlocked(p, p + Geo.Rotate(facing, ang) * 120f)) seen++;
            float score = _b.Grid.Cover[idx] + seen * 0.3f
                          + Math.Clamp((_b.Map.Terrain.HeightAt(p) - h0) * _b.Rules.Terrain.HighGroundBonus, -0.4f, 0.4f)
                          - (MathF.Abs(a) + MathF.Abs(b)) * 0.05f;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    /// <summary>How he thinks the plan will go: expected loss (from the time each squad spends under fire on its
    /// routes and in position), chance of success (who is left to assault against the enemy's fire, better from a
    /// flank) and speed. A less competent commander misjudges more. His personality weights the three into a score.</summary>
    private void Evaluate(Company c, CompanyPlan plan, List<Threat> threats, float enemyFire, List<Group> squads)
    {
        if (plan.Kind is PlanKind.Defend or PlanKind.Suppress)
        {
            plan.ExpectedLoss = 0f; plan.Success = 1f; plan.Speed = 1f; plan.Score = 1f;
            return;
        }
        float k = R.ExposureLethality;
        int men = squads.Sum(g => g.CountEffective);
        float lossMen = 0f, assaultersLeft = 0f, totalTime = 0f;
        var support = plan.Support;
        float supportFire = support == null ? 0f : support.Members.Where(m => m.IsEffective).Sum(m => m.Weapon.IsMachineGun ? 3f : 1f);
        // Covering fire keeps the defenders' heads down during the assault; a frontal rush only has its own fire.
        float suppression = support != null ? 1f - 0.5f * MathF.Min(1f, supportFire / MathF.Max(enemyFire, 1f)) : 0.85f;
        // Tanks with the assault draw fire and shoot the defenders' heads down (milestone 8).
        if (plan.Tanks.Count > 0) suppression *= MathF.Max(0.5f, 1f - 0.3f * plan.Tanks.Count);
        // Mortar fire before the assault: high explosive knocks out or pins some of the defenders near where it
        // lands; smoke hides the assault from everyone on the objective.
        bool he = plan.Fires.Any(f => f.Kind == FireKind.He), smoked = plan.Fires.Any(f => f.Kind == FireKind.Smoke);
        var softened = threats;
        if (plan.Fires.Count > 0)
        {
            softened = new List<Threat>(threats.Count);
            foreach (var th in threats)
            {
                float f = th.Fire;
                foreach (var pf in plan.Fires)
                {
                    float d = Vector2.Distance(th.Position, pf.Target);
                    if (pf.Kind == FireKind.He && d < 50f) f *= 0.6f;
                    else if (pf.Kind == FireKind.Smoke && d < 60f) f *= 0.45f;
                }
                softened.Add(new Threat { Position = th.Position, Fire = f, Assumed = th.Assumed, Radius = th.Radius });
            }
        }

        foreach (var g in squads)
        {
            float exposure = 0f;
            var at = g.Centroid;
            bool assaults = false;
            foreach (var ph in plan.Phases)
            {
                var t = ph.Tasks.FirstOrDefault(x => x.Squad == g);
                if (t == null) continue;
                switch (t.Order.Kind)
                {
                    case OrderKind.Advance when ph.Kind != PhaseKind.Consolidate:
                        exposure += RouteExposure(at, t.Order.Objective, 1.2f, threats, 1f);
                        at = t.Order.Objective;
                        break;
                    case OrderKind.Attack:
                        exposure += RouteExposure(at, t.Order.Objective, 2f, softened, suppression);
                        at = t.Order.Objective;
                        assaults = true;
                        break;
                    case OrderKind.Suppress:
                        // In position and shooting back from cover, through the covering fire and the assault.
                        float time = R.EstablishFireTime + 60f;
                        exposure += FireAt(t.Order.SupportPosition ?? at, softened) * time * 0.35f;
                        break;
                    case OrderKind.Hold when ph.Kind == PhaseKind.EstablishFire:
                        exposure += FireAt(at, threats) * R.EstablishFireTime;
                        break;
                }
            }
            float loss = 1f - MathF.Exp(-k * exposure);
            plan.SquadLosses.Add((g, loss));
            lossMen += loss * g.CountEffective;
            if (assaults) assaultersLeft += (1f - loss) * g.CountEffective;
        }
        foreach (var ph in plan.Phases)
        {
            float longest = 0f;
            foreach (var t in ph.Tasks)
            {
                float speed = t.Order.Kind == OrderKind.Attack ? 2f : 1.2f;
                longest = MathF.Max(longest, PathLength(t.Squad.Centroid, t.Order.Objective) / speed);
            }
            totalTime += ph.Kind == PhaseKind.EstablishFire ? R.EstablishFireTime : ph.Kind == PhaseKind.MoveUp || ph.Kind == PhaseKind.Assault ? longest : 0f;
            if (plan.Fires.Any(f => f.Phase == plan.Phases.IndexOf(ph))) totalTime += 15f; // the word to fire, and the first rounds
        }
        bool fromFlank = plan.Kind is PlanKind.PinAndFlankLeft or PlanKind.PinAndFlankRight;
        if (plan.Kind == PlanKind.Advance) assaultersLeft = men - lossMen;
        float defence = enemyFire * 1.6f * (fromFlank ? 0.75f : 1f) * (he ? 0.8f : 1f) * (smoked ? 0.85f : 1f);
        float success = assaultersLeft / MathF.Max(assaultersLeft + defence, 1f);
        float expectedLoss = men > 0 ? lossMen / men : 1f;

        // Misjudgement: a poor commander's guesses are further off (either way).
        float noise = 0.08f * (5 - c.Traits.Competence);
        expectedLoss = Math.Clamp(expectedLoss * (1f + _b.Rng.Range(-noise, noise)), 0f, 1f);
        success = Math.Clamp(success * (1f + _b.Rng.Range(-noise, noise)), 0f, 1f);
        plan.ExpectedLoss = expectedLoss;
        plan.Success = success;
        plan.Speed = 1f / (1f + totalTime / 300f);
        var t3 = c.Traits;
        plan.Score = success * (1f + 0.15f * (t3.Aggression - 3))
                   - expectedLoss * (1.5f + 0.3f * (t3.Caution - 3))
                   + plan.Speed * (0.3f + 0.1f * (t3.Aggression - 3));
    }
}
