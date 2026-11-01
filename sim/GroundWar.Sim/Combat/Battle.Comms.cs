using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Core;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// Messages and delay (milestone 4). Orders go from a side's HQ to its squads, and reports come back, by field
/// telephone, radio, runner or flare. Each takes time, and runners can be shot and phone lines cut, so what HQ knows
/// is always late and sometimes missing. A side with no HQ in its scenario works the old way: everything instant.
/// See docs/systems/messages-and-delay.md.
/// </summary>
public sealed partial class Battle
{
    private readonly Headquarters?[] _hq = new Headquarters?[2];
    private readonly List<PhoneLine> _lines = new();
    private readonly List<Message> _messages = new();
    private readonly List<(float time, Group group, Order order, Company? company)> _scheduled = new();
    private int _nextMessageId;

    public Headquarters? Hq(Side side) => _hq[(int)side];
    public IReadOnlyList<PhoneLine> PhoneLines => _lines;
    /// <summary>Every message sent so far, in transit or done.</summary>
    public IReadOnlyList<Message> Messages => _messages;

    private Data.CommsRules Cr => Rules.Comms;

    private void SetUpComms()
    {
        foreach (var sideDef in Scenario.Sides)
        {
            var side = Enum.Parse<Side>(sideDef.Side, ignoreCase: true);
            if (sideDef.Hq == null) continue;
            var hq = new Headquarters
            {
                Side = side, Name = sideDef.Hq.Name, Position = BattleMap.V(sideDef.Hq.Position), HasRadio = sideDef.Hq.Radio,
                Knowledge = new SideKnowledge(side),
            };
            _hq[(int)side] = hq;
            if (sideDef.Hq.Staff.Count > 0) hq.Staff = _groups.FirstOrDefault(g => g.Side == side && g.Name == hq.Name);
            foreach (var ld in sideDef.PhoneLines)
            {
                var pts = new List<Vector2> { hq.Position };
                pts.AddRange(ld.Points.Select(p => BattleMap.V(p)));
                if (pts.Count >= 2) _lines.Add(new PhoneLine { Id = _lines.Count, Side = side, Points = pts });
            }
        }
        foreach (var od in Scenario.Orders)
        {
            var g = _groups.FirstOrDefault(x => x.Name == od.Group) ?? FindCompany(od.Group)?.HqGroup
                ?? throw new InvalidDataException($"Scenario order for unknown group '{od.Group}'");
            var kind = Enum.Parse<OrderKind>(od.Order, ignoreCase: true);
            var order = new Order
            {
                Kind = kind, Objective = od.Objective != null ? BattleMap.V(od.Objective) : g.Objective, Radius = od.ObjectiveRadius,
                SupportPosition = od.SupportPosition != null ? BattleMap.V(od.SupportPosition) : null,
                AttachTo = kind == OrderKind.Attach && od.AttachTo != "" ? FindCompany(od.AttachTo) ?? throw new InvalidDataException($"Scenario attaches to unknown company '{od.AttachTo}'") : null,
                Fire = Enum.Parse<FireKind>(od.Fire, ignoreCase: true), Rounds = od.Rounds,
            };
            _scheduled.Add((od.Time, g, order, null));
        }
        _scheduled.Sort((a, b) => a.time.CompareTo(b.time));
    }

    // ---------------------------------------------------------------- sending

    /// <summary>HQ sends an order to one of its squads. It goes by the fastest way available and arrives later.
    /// Without an HQ the order takes effect at once.</summary>
    public Message? SendOrder(Group g, Order order)
    {
        order = WithMission(g, order);
        var hq = _hq[(int)g.Side];
        if (hq == null)
        {
            if (g.IsCompanyHq) _commander.ReceiveOrder(g.Company!, order);
            else ApplyOrder(g, order);
            return null;
        }
        var method = BestMethod(g, hq, down: true, order);
        var m = NewMessage(g, method, down: true, order.ToString(), order);
        Note(g, $"HQ sends {Addressee(g)} \"{order}\" by {MethodName(method)}{EtaText(m)}");
        return m;
    }

    /// <summary>Battalion HQ orders a company: the order goes to the commander, who makes his own plan.</summary>
    public Message? SendOrder(Company c, Order order) => SendOrder(c.HqGroup, order);

    private static string Addressee(Group g) => g.IsCompanyHq ? g.Company!.Name : g.Name;

    /// <summary>The top end of a message's journey: battalion HQ, or for messages inside a company, the company commander.</summary>
    public Vector2 UpperEnd(Message m) => m.Company != null ? SquadPost(m.Company.HqGroup) : _hq[(int)m.Side]!.Position;

    /// <summary>A squad reports up: to its company commander if it has one (and he's still in the fight), else to HQ.</summary>
    private void SendReport(Group g, string text, bool withContacts, MessageKind kind)
    {
        Message m;
        if (g.Company is { CommanderDown: false } co && !g.IsCompanyHq)
            m = NewMessage(g, CompanyMethod(co, g), down: false, text, null, co, kind);
        else
        {
            var hq = _hq[(int)g.Side];
            if (hq == null) return;
            if (hq.Staff == g)
            {
                // The HQ's own men: what they see, HQ knows at once (no need to send a report to themselves).
                if (withContacts)
                    foreach (var c in g.Knowledge.Contacts)
                        if (GetSoldier(c.EnemyId) is { Status: not SoldierStatus.Dead } e && !(hq.Knowledge.Get(c.EnemyId)?.LastSeen >= c.LastSeen))
                            hq.Knowledge.Report(e, c.LastPosition, c.LastSeen, c.Confidence);
                return;
            }
            m = NewMessage(g, BestMethod(g, hq, down: false, null), down: false, text, null, null, kind);
        }
        if (withContacts)
            foreach (var c in g.Knowledge.Contacts)
                m.Contacts.Add(new ReportedContact(c.EnemyId, c.LastPosition, c.LastSeen, c.Confidence));
    }

    private Message NewMessage(Group g, MessageMethod method, bool down, string text, Order? order,
        Company? company = null, MessageKind kind = MessageKind.Order, CompanyPlan? plan = null, FireMission? mission = null)
    {
        var m = new Message
        {
            Id = _nextMessageId++, Side = g.Side, Method = method, Group = g, Down = down, Order = order, Text = text, SentTime = Time,
            Company = company, Kind = order != null ? MessageKind.Order : kind, Plan = plan, Strength = g.CountEffective, Mission = mission,
        };
        switch (method)
        {
            case MessageMethod.Phone:
                m.ArriveTime = Time + Cr.PhoneDelay + Cr.PhoneDelayJitter * Rng.Range(-1f, 1f);
                break;
            case MessageMethod.Voice:
                m.ArriveTime = Time + Cr.VoiceDelay;
                break;
            case MessageMethod.Radio:
                float delay = company != null
                    ? Cr.CompanyRadioDelay + Cr.CompanyRadioDelayJitter * Rng.Range(-1f, 1f)
                    : Cr.RadioDelay + Cr.RadioDelayJitter * Rng.Range(-1f, 1f);
                if (Rng.Chance(Cr.RadioRepeatChance)) delay *= 2f; // didn't get through: send it again
                m.ArriveTime = Time + delay;
                break;
            case MessageMethod.Flare:
                m.ArriveTime = Time + Cr.FlareDelay;
                break;
            case MessageMethod.Runner:
                var top = UpperEnd(m);
                m.RunnerPosition = down ? top : SquadPost(g);
                m.ArriveTime = Time + Vector2.Distance(top, SquadPost(g)) / Cr.RunnerSpeed; // first guess, for display
                break;
        }
        _messages.Add(m);
        return m;
    }

    /// <summary>Where a squad's messages go from and arrive at: its sergeant, or the middle of the squad.</summary>
    public static Vector2 SquadPost(Group g) => g.Leader is { IsActive: true } l ? l.Position : g.Centroid;

    /// <summary>The fastest way to get a message between HQ and this squad right now.</summary>
    internal MessageMethod BestMethod(Group g, Headquarters hq, bool down, Order? order)
    {
        if (down && order != null && order.CanGoByFlare && CanSeeFlare(g, hq)) return MessageMethod.Flare;
        if (PhoneFor(g) != null) return MessageMethod.Phone;
        if (hq.HasRadio && g.HasRadio) return MessageMethod.Radio;
        return MessageMethod.Runner;
    }

    /// <summary>An uncut phone line whose end is within reach of the squad's sergeant, if there is one.</summary>
    internal PhoneLine? PhoneFor(Group g)
    {
        var post = SquadPost(g);
        foreach (var l in _lines)
            if (l.Side == g.Side && !l.Cut && Vector2.DistanceSquared(l.End, post) <= Cr.PhoneReach * Cr.PhoneReach) return l;
        return null;
    }

    private bool CanSeeFlare(Group g, Headquarters hq)
    {
        var post = SquadPost(g);
        return Vector2.Distance(post, hq.Position) <= Cr.FlareRange && !Map.SightBlocked(post, hq.Position);
    }

    private static string MethodName(MessageMethod m) => m switch
    {
        MessageMethod.Phone => "telephone",
        MessageMethod.Radio => "radio",
        MessageMethod.Flare => "flare",
        MessageMethod.Voice => "voice",
        _ => "runner",
    };

    private string EtaText(Message m) => m.Method == MessageMethod.Runner
        ? $" (about {(m.ArriveTime - Time) / 60f:0.0} min)" : $" ({m.ArriveTime - Time:0} s)";

    // ---------------------------------------------------------------- each tick

    private void UpdateComms()
    {
        while (_scheduled.Count > 0 && _scheduled[0].time <= Time)
        {
            var (_, g, order, company) = _scheduled[0];
            _scheduled.RemoveAt(0);
            if (company != null) { if (!company.CommanderDown) SendCompanyOrder(company, g, order); }
            else SendOrder(g, order);
        }
        if (_hq[0] == null && _hq[1] == null && _companies.Count == 0) return;

        // Count first: a failed message may send a new one, which starts next tick.
        for (int i = 0, n = _messages.Count; i < n; i++)
        {
            var m = _messages[i];
            if (m.Status != MessageStatus.InTransit) continue;
            if (m.Method == MessageMethod.Runner) MoveRunner(m);
            else if (Time >= m.ArriveTime)
            {
                // A phone message needs the line to still be there when it's spoken.
                if (m.Method == MessageMethod.Phone && PhoneFor(m.Group) == null && !LineStillUp(m)) Resend(m, "the line went dead");
                else Deliver(m);
            }
        }

        if (Tick % 10 == 0) foreach (var g in _groups) WriteReports(g);
    }

    private bool LineStillUp(Message m) => _lines.Any(l => l.Side == m.Side && !l.Cut &&
        Vector2.DistanceSquared(l.End, SquadPost(m.Group)) <= 4f * Cr.PhoneReach * Cr.PhoneReach);

    /// <summary>The way it was sent has failed: mark it lost and send it again by the next best way.</summary>
    private void Resend(Message m, string why)
    {
        m.Status = MessageStatus.Lost;
        m.DoneTime = Time;
        Note(m.Group, $"{(m.Down ? "Order to " + m.Group.Name : m.Group.Name + "'s report")} lost: {why}");
        var method = m.Company != null ? CompanyMethod(m.Company, m.Group) : BestMethod(m.Group, _hq[(int)m.Side]!, m.Down, m.Order);
        var again = NewMessage(m.Group, method, m.Down, m.Text, m.Order, m.Company, m.Kind, m.Plan, m.Mission);
        again.Contacts.AddRange(m.Contacts);
    }

    private void MoveRunner(Message m)
    {
        var goal = m.Down ? SquadPost(m.Group) : UpperEnd(m);
        // Re-plan the route now and then: the squad he's looking for keeps moving.
        if (m.RunnerPath == null || Time >= m.RunnerRepath)
        {
            m.RunnerPath = Map.Nav.FindPath(m.RunnerPosition, goal) ?? new List<Vector2> { goal };
            m.RunnerPathIndex = 0;
            m.RunnerRepath = Time + 10f;
        }
        float step = Cr.RunnerSpeed * Dt;
        while (step > 0f && m.RunnerPathIndex < m.RunnerPath.Count)
        {
            var target = m.RunnerPath[m.RunnerPathIndex];
            float d = Vector2.Distance(m.RunnerPosition, target);
            if (d <= step) { m.RunnerPosition = target; m.RunnerPathIndex++; step -= d; }
            else { m.RunnerPosition += (target - m.RunnerPosition) / d * step; step = 0f; }
        }
        float left = Vector2.Distance(m.RunnerPosition, goal);
        m.ArriveTime = Time + left / Cr.RunnerSpeed;
        if (left < 4f) { Deliver(m); return; }

        // Enemies who can see him may shoot him: checked once a second.
        if (Tick % 10 != m.Id % 10) return;
        float danger = 0f;
        foreach (var e in Hash.Near(Enemy(m.Side), m.RunnerPosition, Cr.RunnerDangerRange))
        {
            if (!e.IsEffective || e.IsTank || e.Suppression >= Rules.Morale.SuppressionPinned) continue;
            if (Map.SightBlocked(e.Position, m.RunnerPosition)) continue;
            danger += Cr.RunnerHitPerSecond * (1f - Vector2.Distance(e.Position, m.RunnerPosition) / Cr.RunnerDangerRange);
        }
        if (danger > 0f && Rng.Chance(MathF.Min(danger, 0.15f)))
        {
            m.Status = MessageStatus.Lost;
            m.DoneTime = Time;
            Note(m.Group, $"Runner {(m.Down ? "carrying an order to " + m.Group.Name : "from " + m.Group.Name)} was shot down; the message is lost");
            // HQ doesn't know at once. When the runner hasn't come back after a minute, it sends the order again.
            if (m.Down && m.Order != null) _scheduled.Add((Time + Cr.RunnerLostResend, m.Group, m.Order, m.Company));
            _scheduled.Sort((a, b) => a.time.CompareTo(b.time));
        }
    }

    private void Deliver(Message m)
    {
        m.Status = MessageStatus.Delivered;
        m.DoneTime = Time;
        if (m.Down)
        {
            if (m.Group.CountEffective == 0) return;
            if (m.Kind is MessageKind.Approve or MessageKind.Reject)
            {
                if (m.Group.Company is { } cc)
                {
                    if (m.Kind == MessageKind.Approve) _commander.Approved(cc); else _commander.Rejected(cc);
                }
                return;
            }
            // Orders inside a company are too many to log one by one; battalion's orders are logged.
            if (m.Company == null)
                Note(m.Group, $"{Addressee(m.Group)} receives \"{m.Text}\" by {MethodName(m.Method)}, {Time - m.SentTime:0} s after it was sent");
            if (m.Company == null && m.Group.IsCompanyHq) _commander.ReceiveOrder(m.Group.Company!, m.Order!);
            else ApplyOrder(m.Group, m.Order!);
        }
        else if (m.Company != null)
        {
            DeliverToCompany(m);
        }
        else
        {
            var hq = _hq[(int)m.Side]!;
            Note(m.Group, $"HQ hears from {Addressee(m.Group)} ({Time - m.SentTime:0} s old): {m.Text}");
            if (m.Kind == MessageKind.BriefBack && m.Group.Company is { State: CompanyState.BriefBack } bc && bc.Plan == m.Plan)
                bc.BriefBackHeardAt = Time;
            if (m.Kind == MessageKind.FireRequest && m.Mission != null) ForwardToMortars(m.Mission);
            if (m.Group.IsCompanyHq) HearFromCompany(m);
            foreach (var c in m.Contacts)
            {
                var enemy = GetSoldier(c.EnemyId);
                if (enemy == null || enemy.Status is SoldierStatus.Dead) continue;
                var known = hq.Knowledge.Get(c.EnemyId);
                if (known != null && known.LastSeen >= c.SeenAt) continue; // HQ already has newer news
                hq.Knowledge.Report(enemy, c.Position, c.SeenAt, c.Confidence);
            }
        }
    }

    /// <summary>The order has arrived: the squad leader takes it on.</summary>
    internal void ApplyOrder(Group g, Order o)
    {
        // Orders for battalion's support don't change where the men are or what they're doing.
        if (o.Kind == OrderKind.FireMission) { TakeFireMission(g, WithMission(g, o)); return; }
        if (o.Kind == OrderKind.Attach) { Attach(g, o.AttachTo); return; }
        switch (o.Kind)
        {
            case OrderKind.Halt:
                g.Order = GroupOrder.Hold;
                g.HoldPosition = g.Centroid;
                g.ObjectiveRadius = Rules.Squad.TeamRadius * 2f;
                break;
            case OrderKind.FallBack:
                g.Order = GroupOrder.Hold;
                g.HoldPosition = Map.Clamp(g.Centroid + g.Rear * Rules.Squad.FallBackStep * 2f, 3f);
                g.ObjectiveRadius = Rules.Squad.TeamRadius * 2f;
                break;
            case OrderKind.Hold:
                g.Order = GroupOrder.Hold;
                g.Objective = o.Objective;
                g.HoldPosition = o.Objective;
                g.ObjectiveRadius = o.Radius;
                break;
            case OrderKind.Advance:
            case OrderKind.Attack:
                g.Order = o.Kind == OrderKind.Advance ? GroupOrder.Advance : GroupOrder.Attack;
                g.Objective = o.Objective;
                g.ObjectiveRadius = o.Radius;
                g.TookObjective = false;
                g.AttackFailed = false;
                break;
            case OrderKind.Suppress:
                g.Order = GroupOrder.Suppress;
                g.Objective = o.Objective;
                g.ObjectiveRadius = o.Radius;
                g.SupportPosition = o.SupportPosition ?? g.Centroid;
                g.HoldPosition = g.SupportPosition.Value;
                break;
        }
        g.OriginalOrder = g.Order;
        g.LastOrderReceived = o;
        g.MoveOnly = o.IsWaypoint;
        if (o.Kind is OrderKind.Advance or OrderKind.Attack or OrderKind.Suppress) g.EverAttacked = true;
        g.ReportedTaken = false;
        g.ReportedFailed = false;
        g.Tactic = Tactic.Travel;
        g.NextThink = Time; // the sergeant acts on it now
    }

    // ---------------------------------------------------------------- reports

    /// <summary>A squad sergeant tells HQ what matters: first contact, regular situation reports while in contact,
    /// heavy losses, being pinned or broken, taking the objective, or the attack failing.</summary>
    private void WriteReports(Group g)
    {
        // Tank crews who have bailed out are making their own way back: they don't report.
        if (g.IsVehicles || g.IsCompanyHq || g.IsCrew) return;
        if (_hq[(int)g.Side] == null && g.Company is not { CommanderDown: false }) return;
        int effective = g.CountEffective;
        if (effective == 0)
        {
            if (!g.ReportedBroken && g.CountActive > 0) { g.ReportedBroken = true; SendReport(g, "squad has broken and is running", false, MessageKind.Broken); }
            return;
        }
        int known = g.Knowledge.Count;
        if (known > 0 && !g.ReportedContact)
        {
            g.ReportedContact = true;
            g.LastSitrep = Time;
            SendReport(g, $"contact! {known} enemy seen", true, MessageKind.Contact);
        }
        else if (known > 0 && Time - g.LastSitrep >= Cr.SitrepInterval)
        {
            g.LastSitrep = Time;
            SendReport(g, $"situation report: {effective}/{g.StartStrength} fit, {known} enemy known, {g.Status.ToLowerInvariant()}", true, MessageKind.Sitrep);
        }
        else if (known == 0 && g.ReportedContact && Time - g.LastSitrep >= Cr.SitrepInterval * 2f)
        {
            g.ReportedContact = false; // lost touch: the next sighting is a new contact
        }

        int quarter = (int)(4f * (1f - effective / (float)g.StartStrength));
        if (quarter > g.ReportedLossQuarter)
        {
            g.ReportedLossQuarter = quarter;
            SendReport(g, $"heavy casualties: {g.StartStrength - effective} of {g.StartStrength} down or broken", false, MessageKind.Losses);
        }
        bool pinned = g.Members.Count(m => m.IsEffective && m.MoraleState == MoraleState.Pinned) * 2 >= effective;
        if (pinned && !g.ReportedPinned) { g.ReportedPinned = true; SendReport(g, "pinned down", true, MessageKind.Pinned); }
        else if (!pinned) g.ReportedPinned = false;
        if (g.TookObjective && !g.ReportedTaken) { g.ReportedTaken = true; SendReport(g, "objective taken", true, MessageKind.Taken); }
        if (g.AttackFailed && !g.ReportedFailed) { g.ReportedFailed = true; SendReport(g, "attack failed, holding where we are", true, MessageKind.Failed); }
    }

    // ---------------------------------------------------------------- damage to lines

    /// <summary>A shell burst here: any phone cable close by is cut.</summary>
    internal void CutLinesNear(Vector2 at)
    {
        foreach (var l in _lines)
        {
            if (l.Cut) continue;
            for (int i = 0; i + 1 < l.Points.Count; i++)
                if (Geo.PointSegmentDistSq(at, l.Points[i], l.Points[i + 1], out _) <= Cr.LineCutRadius * Cr.LineCutRadius)
                {
                    l.Cut = true;
                    l.CutTime = Time;
                    Log.Add(Time, -1, $"{l.Side} telephone line cut by shellfire", l.Side);
                    break;
                }
        }
    }
}
