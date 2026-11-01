using System.Numerics;
using GroundWar.Sim.AI;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// Companies (milestone 5). A company is a commander with a personality, his HQ group and his squads. Battalion
/// HQ (the player) orders the company; the commander makes the plan and orders the squads himself, by voice when
/// they're close, otherwise by runner or the company radio. Squads report to him, and he reports to battalion.
/// See docs/systems/company-commander.md.
/// </summary>
public sealed partial class Battle
{
    private readonly List<Company> _companies = new();
    private CompanyCommander _commander = null!;

    public IReadOnlyList<Company> Companies => _companies;
    /// <summary>The side the player commands. Its company commanders brief back and wait for an answer; the
    /// other side's get on with their plans at once.</summary>
    public Side PlayerSide { get; set; } = Side.Blue;

    private void SetUpCompanies()
    {
        _commander = new CompanyCommander(this);
        foreach (var cd in Scenario.Companies)
        {
            var hq = _groups.FirstOrDefault(g => g.Name == cd.Hq)
                ?? throw new InvalidDataException($"Company '{cd.Name}' has unknown HQ group '{cd.Hq}'");
            var c = new Company
            {
                Id = _companies.Count, Name = cd.Name, CommanderName = cd.Commander, Side = hq.Side, HqGroup = hq, Traits = cd.Traits,
                Knowledge = new SideKnowledge(hq.Side), NextThink = Rng.Range(0f, 3f),
            };
            hq.Company = c;
            hq.HoldPosition = hq.Centroid;
            foreach (var name in cd.Squads)
            {
                var g = _groups.FirstOrDefault(x => x.Name == name)
                    ?? throw new InvalidDataException($"Company '{cd.Name}' has unknown squad '{name}'");
                g.Company = c;
                c.Squads.Add(g);
                c.Reports[g] = new SquadReport { Strength = g.StartStrength };
            }
            _companies.Add(c);
        }
    }

    public Company? FindCompany(string name) => _companies.FirstOrDefault(c => c.Name == name);

    private void UpdateCompanies()
    {
        if (_companies.Count == 0) return;
        foreach (var c in _companies)
        {
            if (Time >= c.NextThink) _commander.Think(c);
            // Battalion's half of the brief-back: no word from the player in time means "carry on".
            if (AwaitingApproval(c) && Time >= c.BriefBackHeardAt + Cr.BriefBackWait) AnswerBriefBack(c, approve: true);
        }
        if (Tick % 10 == 0) foreach (var c in _companies) c.Knowledge.Expire(Time, Cr.HqContactMemory);
    }

    /// <summary>Battalion HQ has the company's brief-back and hasn't answered yet: the player can approve or reject it.</summary>
    public bool AwaitingApproval(Company c) =>
        c.State == CompanyState.BriefBack && c.BriefBackHeardAt >= 0f && !c.BriefBackAnswered;

    /// <summary>Seconds before the brief-back plan is approved without the player saying anything.</summary>
    public float BriefBackCountdown(Company c) => MathF.Max(0f, c.BriefBackHeardAt + Cr.BriefBackWait - Time);

    public void ApproveBriefBack(Company c) { if (AwaitingApproval(c)) AnswerBriefBack(c, approve: true); }
    public void RejectBriefBack(Company c) { if (AwaitingApproval(c)) AnswerBriefBack(c, approve: false); }

    /// <summary>HQ answers a brief-back. The commander is waiting on the radio for it, so it gets there quickly;
    /// without a radio it goes by runner like anything else.</summary>
    private void AnswerBriefBack(Company c, bool approve)
    {
        c.BriefBackAnswered = true;
        var hq = _hq[(int)c.Side];
        if (hq == null) { if (approve) _commander.Approved(c); else _commander.Rejected(c); return; }
        var method = hq.HasRadio && c.HqGroup.HasRadio ? MessageMethod.Radio : MessageMethod.Runner;
        var m = NewMessage(c.HqGroup, method, down: true, approve ? "Plan approved, go ahead" : "Plan rejected, hold where you are", null,
            kind: approve ? MessageKind.Approve : MessageKind.Reject);
        if (method == MessageMethod.Radio) m.ArriveTime = Time + Cr.BriefBackReplyDelay;
        Note(c.HqGroup, $"HQ {(approve ? "approves" : "rejects")} {c.Name}'s plan");
    }

    /// <summary>A company commander orders one of his squads.</summary>
    internal Message SendCompanyOrder(Company c, Group g, Order o)
    {
        var m = NewMessage(g, CompanyMethod(c, g), down: true, o.ToString(), o, c, MessageKind.Order);
        return m;
    }

    /// <summary>A company commander reports to battalion HQ. Without a battalion HQ it just goes in the log.</summary>
    internal void SendUp(Company c, string text, MessageKind kind, CompanyPlan? plan, bool withContacts)
    {
        var hq = _hq[(int)c.Side];
        if (hq == null) { Note(c.HqGroup, $"{c.Name}: {text}"); return; }
        var method = BestMethod(c.HqGroup, hq, down: false, null);
        var m = NewMessage(c.HqGroup, method, down: false, text, null, null, kind, plan);
        if (withContacts)
            foreach (var k in c.Knowledge.Contacts)
                m.Contacts.Add(new ReportedContact(k.EnemyId, k.LastPosition, k.LastSeen, k.Confidence));
    }

    /// <summary>Inside a company: shout if he's close, the company radio if both ends have a set, otherwise a runner.</summary>
    internal MessageMethod CompanyMethod(Company c, Group g)
    {
        if (Vector2.Distance(SquadPost(g), SquadPost(c.HqGroup)) <= Cr.VoiceRange) return MessageMethod.Voice;
        if (c.HqGroup.HasRadio && g.HasRadio) return MessageMethod.Radio;
        return MessageMethod.Runner;
    }

    /// <summary>A squad's report has reached its company commander.</summary>
    private void DeliverToCompany(Message m)
    {
        var c = m.Company!;
        if (c.CommanderDown) return;
        foreach (var k in m.Contacts) _commander.Merge(c, k.EnemyId, k.Position, k.SeenAt, k.Confidence);
        if (!c.Reports.TryGetValue(m.Group, out var r)) c.Reports[m.Group] = r = new SquadReport();
        r.Time = Time;
        r.Strength = m.Strength;
        switch (m.Kind)
        {
            case MessageKind.Pinned: r.Pinned = true; r.PinnedAt = Time; break;
            case MessageKind.Taken: r.Taken = true; break;
            case MessageKind.Failed: r.Failed = true; break;
            case MessageKind.Broken: r.Broken = true; break;
        }
        if (m.Kind is not (MessageKind.Contact or MessageKind.Sitrep))
            Note(m.Group, $"{c.CommanderName} hears from {m.Group.Name}: {m.Text}");
    }

    /// <summary>Still working on an order: plans waiting or running, orders on their way. The battle doesn't end
    /// for quiet while this is so.</summary>
    private bool CommandBusy()
    {
        if (_scheduled.Count > 0 || SupportBusy()) return true;
        foreach (var c in _companies)
            if (!c.CommanderDown && c.State is CompanyState.BriefBack or CompanyState.Executing) return true;
        foreach (var m in _messages)
            if (m.Status == MessageStatus.InTransit && m.Down && m.Order != null) return true;
        return false;
    }
}
