using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>On-screen text: battle status, selected soldier, recent events, help and the end banner.</summary>
public partial class Hud : CanvasLayer
{
    private Label _status = null!;
    private Label _selected = null!;
    private Label _log = null!;
    private Label _help = null!;
    private Label _banner = null!;
    private PanelContainer _selectedPanel = null!;

    public const string HelpText =
        "CAMERA   WASD / arrows pan · Q E rotate · wheel or + - zoom · right-drag orbit · middle-drag pan · Shift faster\n" +
        "BATTLE   Space pause · . step · 1-6 speed · R restart · N new battle (new seed) · Tab next scenario\n" +
        "SOLDIER  left-click select · F follow · Esc deselect · order panel (bottom right): press an order, then click the map\n" +
        "COMPANY  Enter approve the brief-back · Backspace reject it · selecting any man in a company orders the company\n" +
        "OVERLAYS G grid (cover / visibility / control) · C cover points · L aim and path lines · P squad plans · T thoughts · B morale bars · V fog of war on/off · H hide help";

    public override void _Ready()
    {
        _status = MakePanelLabel(new Vector2(12, 12), out _);
        _selected = MakePanelLabel(new Vector2(0, 12), out _selectedPanel);
        _selectedPanel.AnchorLeft = _selectedPanel.AnchorRight = 1f;
        _selectedPanel.OffsetLeft = -380;
        _selectedPanel.OffsetRight = -12;
        _selectedPanel.Visible = false;

        _log = new Label { Position = new Vector2(12, 0), Modulate = new Color(1, 1, 1, 0.85f) };
        _log.AddThemeFontSizeOverride("font_size", 13);
        _log.AnchorTop = _log.AnchorBottom = 1f;
        _log.OffsetTop = -250;
        _log.OffsetBottom = -92;
        _log.VerticalAlignment = VerticalAlignment.Bottom;
        _log.AddThemeColorOverride("font_outline_color", Colors.Black);
        _log.AddThemeConstantOverride("outline_size", 4);
        AddChild(_log);

        _help = new Label { Text = HelpText };
        _help.AddThemeFontSizeOverride("font_size", 13);
        _help.AnchorTop = _help.AnchorBottom = 1f;
        _help.OffsetTop = -84;
        _help.OffsetLeft = 12;
        _help.AddThemeColorOverride("font_outline_color", Colors.Black);
        _help.AddThemeConstantOverride("outline_size", 4);
        AddChild(_help);

        BuildOrderPanel();
        BuildCompanyPanel();
        _messagesLabel = MakePanelLabel(Vector2.Zero, out _messagesPanel);
        _messagesLabel.AddThemeFontSizeOverride("font_size", 13);
        _messagesPanel.AnchorLeft = _messagesPanel.AnchorRight = 1f;
        _messagesPanel.AnchorTop = _messagesPanel.AnchorBottom = 1f;
        _messagesPanel.OffsetLeft = -460; _messagesPanel.OffsetRight = -12;
        _messagesPanel.OffsetTop = -330; _messagesPanel.OffsetBottom = -170;
        _messagesPanel.Visible = false;

        _banner = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _banner.AddThemeFontSizeOverride("font_size", 30);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 10);
        _banner.AnchorLeft = 0; _banner.AnchorRight = 1;
        _banner.OffsetTop = 70;
        AddChild(_banner);
    }

    private Label MakePanelLabel(Vector2 pos, out PanelContainer panel)
    {
        panel = new PanelContainer { Position = pos };
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.05f, 0.72f) };
        style.SetContentMarginAll(10);
        style.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(label);
        AddChild(panel);
        return label;
    }

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public void UpdateHud(Battle battle, float speed, bool paused, Soldier? selected, string overlays, bool fog, Side playerSide)
    {
        var sb = new StringBuilder();
        int minutes = (int)(battle.Time / 60), seconds = (int)battle.Time % 60;
        sb.AppendLine($"{battle.Scenario.Name}   (seed {battle.Seed})");
        sb.AppendLine($"Time {minutes}:{seconds:00}   Speed {speed}x{(paused ? "   PAUSED" : "")}");
        sb.AppendLine();
        foreach (var side in new[] { Side.Blue, Side.Red })
        {
            var men = battle.Soldiers.Where(s => s.Side == side).ToList();
            int fighting = men.Count(s => s.IsActive && s.MoraleState is MoraleState.Steady or MoraleState.Shaken);
            int pinned = men.Count(s => s.IsActive && s.MoraleState == MoraleState.Pinned);
            int broken = men.Count(s => s.IsActive && s.MoraleState == MoraleState.Broken);
            int down = men.Count(s => s.Status is SoldierStatus.Dead or SoldierStatus.Wounded);
            int fled = men.Count(s => s.Status == SoldierStatus.Routed);
            sb.AppendLine($"{side,-4}  fighting {fighting,2}  pinned {pinned,2}  broken {broken,2}  down {down,2}  fled {fled,2}");
        }
        sb.AppendLine();
        var groups = battle.Groups.Where(g => !fog || g.Side == playerSide).ToList();
        if (groups.Count <= 6)
        {
            foreach (var g in groups)
            {
                sb.AppendLine(g.IsVehicles ? g.Name : $"{g.Name} ({g.CountEffective}/{g.Members.Count}) {g.Tactic}");
                sb.AppendLine($"   {g.Status}");
            }
        }
        else
        {
            // Too many squads to list one by one (the battalion test): count them by what they're doing.
            foreach (var side in new[] { Side.Blue, Side.Red })
            {
                var mine = groups.Where(g => g.Side == side).ToList();
                if (mine.Count == 0) continue;
                var tactics = mine.GroupBy(g => g.Tactic).OrderByDescending(t => t.Count()).Select(t => $"{t.Key} {t.Count()}");
                sb.AppendLine($"{side} squads ({mine.Count}): {string.Join(", ", tactics)}");
            }
            if (selected != null)
            {
                sb.AppendLine($"{selected.Group.Name} ({selected.Group.CountEffective}/{selected.Group.Members.Count}) {selected.Group.Tactic}");
                sb.AppendLine($"   {selected.Group.Status}");
            }
            else sb.AppendLine("Click a soldier to see his squad");
        }
        if (fog) sb.AppendLine($"Fog of war: you see what {playerSide} has spotted");
        if (overlays.Length > 0) { sb.AppendLine(); sb.Append("Overlays: ").Append(overlays); }
        _status.Text = sb.ToString().TrimEnd();

        _selectedPanel.Visible = selected != null;
        if (selected != null)
        {
            var s = selected;
            var t = new StringBuilder();
            t.AppendLine($"{s.Label}  {s.Role}, {s.Group.Name}");
            if (s.IsTank)
            {
                var v = s.Vehicle!;
                t.AppendLine($"Status: {(s.Status == SoldierStatus.Dead ? "Knocked out" : s.Immobilised ? "Immobilised (tracks broken)" : "Running")}");
                t.AppendLine($"Armour: front {v.ArmourFront:0} mm, side {v.ArmourSide:0} mm, rear {v.ArmourRear:0} mm");
                if (s.IsActive)
                {
                    t.AppendLine($"Doing: {s.Group.Status}");
                    t.AppendLine($"Thinking: {s.Thought}");
                    t.AppendLine($"Main gun: {s.MainGunAmmo} shells{(s.MainGunTimer > 0 ? $", loading ({s.MainGunTimer:0} s)" : ", loaded")}");
                    t.AppendLine($"Machine gun: {s.Magazine} + {s.SpareMagazines} x {s.Weapon.MagazineSize}{(s.ReloadTimer > 0 ? "  (reloading)" : "")}");
                    t.AppendLine($"Sees {s.VisibleEnemies.Count} enemies");
                }
                t.Append($"Shots {s.ShotsFired}   Hits {s.Hits}");
                _selected.Text = t.ToString();
            }
            else
            {
            t.AppendLine($"Status: {s.Status}   Stance: {s.Stance}");
            t.AppendLine($"Team: {s.Team?.Name ?? "-"}   Squad: {s.Group.Tactic}");
            if (s.IsActive)
            {
                t.AppendLine($"Morale: {s.Morale:0} / {s.StartMorale:0}  ({s.MoraleState})");
                t.AppendLine($"Suppression: {s.Suppression:0}");
                t.AppendLine($"Action: {s.Action}");
                t.AppendLine($"Thinking: {s.Thought}");
                t.AppendLine($"Cover: {(s.InCover ? s.Cover!.Kind.ToString() : "none")}  protection {s.LastProtection * 100:0}%");
                t.AppendLine($"Ammo: {s.Magazine} + {s.SpareMagazines} x {s.Weapon.MagazineSize}{(s.ReloadTimer > 0 ? "  (reloading)" : "")}");
                t.AppendLine($"Sees {s.VisibleEnemies.Count} enemies{(s.Target != null ? $", target {s.Target.Label}" : "")}");
            }
            t.Append($"Skill {s.Skill:0.00}   Shots {s.ShotsFired}   Hits {s.Hits}");
            _selected.Text = t.ToString();
            }
        }

        _log.Text = RecentLog(battle, selected, fog, playerSide);

        if (battle.Result != null)
        {
            var r = battle.Result;
            string who = r.Winner == null ? "No winner" : $"{r.Winner} wins";
            _banner.Text = $"{r.Reason}. {who} ({(int)(r.Time / 60)}:{(int)r.Time % 60:00})\nR to replay  ·  N for a new battle";
            _banner.Modulate = r.Winner == Side.Blue ? new Color(0.6f, 0.75f, 1f) : r.Winner == Side.Red ? new Color(1f, 0.65f, 0.6f) : Colors.White;
            _banner.Visible = true;
        }
        else _banner.Visible = false;
    }

    // ---------------------------------------------------------------- orders and messages (milestone 4)

    /// <summary>Main listens to this: the player pressed an order button.</summary>
    public System.Action<OrderKind>? OrderRequested { get; set; }
    private PanelContainer _orderPanel = null!;
    private Label _orderTitle = null!, _orderStatus = null!;
    private PanelContainer _messagesPanel = null!;
    private Label _messagesLabel = null!;

    private void BuildOrderPanel()
    {
        _orderPanel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.05f, 0.8f) };
        style.SetContentMarginAll(10);
        style.SetCornerRadiusAll(6);
        _orderPanel.AddThemeStyleboxOverride("panel", style);
        _orderPanel.AnchorLeft = _orderPanel.AnchorRight = 1f;
        _orderPanel.AnchorTop = _orderPanel.AnchorBottom = 1f;
        _orderPanel.OffsetLeft = -460; _orderPanel.OffsetRight = -12;
        _orderPanel.OffsetTop = -160; _orderPanel.OffsetBottom = -92;
        var box = new VBoxContainer();
        _orderTitle = new Label();
        _orderTitle.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_orderTitle);
        var row = new HBoxContainer();
        foreach (var (kind, text) in new[] { (OrderKind.Advance, "Advance"), (OrderKind.Attack, "Attack"), (OrderKind.Suppress, "Suppress"),
                                             (OrderKind.Hold, "Hold"), (OrderKind.Halt, "Halt"), (OrderKind.FallBack, "Fall back") })
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => OrderRequested?.Invoke(kind);
            row.AddChild(b);
        }
        box.AddChild(row);
        _orderStatus = new Label();
        _orderStatus.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_orderStatus);
        _orderPanel.AddChild(box);
        _orderPanel.Visible = false;
        AddChild(_orderPanel);
    }

    /// <summary>The order panel (when one of your squads is selected) and the list of messages on their way.
    /// A squad in a company is ordered through its company commander.</summary>
    public void UpdateOrders(Battle battle, Soldier? selected, Side playerSide, OrderKind? pending)
    {
        var hq = battle.Hq(playerSide);
        bool mine = selected != null && selected.Side == playerSide;
        _orderPanel.Visible = mine;
        if (mine)
        {
            var g = selected!.Group.Company?.HqGroup ?? selected.Group;
            string who = g.IsCompanyHq ? $"{g.Company!.Name} ({g.Company.CommanderName})" : g.Name;
            _orderTitle.Text = hq == null ? $"Orders for {who} (no HQ in this scenario: orders arrive at once)" : $"Orders for {who}, from {hq.Name}";
            if (pending != null) _orderStatus.Text = $"Click the map: where to {pending.Value.ToString().ToLowerInvariant()}. Esc cancels.";
            else
            {
                Message? last = null;
                foreach (var m in battle.Messages) if (m.Down && m.Group == g && m.Company == null) last = m;
                _orderStatus.Text = last == null ? "Advance, Attack, Suppress and Hold need a place: press, then click the map."
                    : last.Status switch
                    {
                        MessageStatus.InTransit => $"\"{last.Text}\" on its way by {last.Method.ToString().ToLowerInvariant()}, about {Mathf.Max(0f, last.ArriveTime - battle.Time):0} s",
                        MessageStatus.Delivered => $"\"{last.Text}\" received after {last.DoneTime - last.SentTime:0} s",
                        _ => $"\"{last.Text}\" lost on the way",
                    };
            }
        }

        _messagesPanel.Visible = hq != null;
        if (hq == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("Messages");
        int shown = 0;
        foreach (var m in battle.Messages)
        {
            // Only battalion's own traffic: what goes on inside a company is the company commander's business.
            if (m.Side != playerSide || m.Status != MessageStatus.InTransit || m.Company != null) continue;
            if (shown++ >= 5) break;
            sb.AppendLine($"  → {Short(m)}  ({m.Method.ToString().ToLowerInvariant()}, {Mathf.Max(0f, m.ArriveTime - battle.Time):0} s)");
        }
        if (shown == 0) sb.AppendLine("  nothing on its way");
        int done = 0;
        for (int i = battle.Messages.Count - 1; i >= 0 && done < 4; i--)
        {
            var m = battle.Messages[i];
            if (m.Side != playerSide || m.Status == MessageStatus.InTransit || m.Company != null) continue;
            done++;
            sb.AppendLine($"  {(m.Status == MessageStatus.Lost ? "✗ lost" : "✓")} {Short(m)}  ({m.DoneTime - m.SentTime:0} s, {m.Method.ToString().ToLowerInvariant()})");
        }
        _messagesLabel.Text = sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------- company commander (milestone 5)

    /// <summary>Main listens to these: the player approved or rejected a brief-back.</summary>
    public System.Action<Company>? ApproveRequested { get; set; }
    public System.Action<Company>? RejectRequested { get; set; }
    private PanelContainer _companyPanel = null!;
    private Label _companyLabel = null!;
    private HBoxContainer _briefBackRow = null!;
    private Label _countdown = null!;
    private Company? _shownCompany;

    private void BuildCompanyPanel()
    {
        _companyPanel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.05f, 0.82f) };
        style.SetContentMarginAll(10);
        style.SetCornerRadiusAll(6);
        _companyPanel.AddThemeStyleboxOverride("panel", style);
        _companyPanel.AnchorLeft = _companyPanel.AnchorRight = 0.5f;
        _companyPanel.OffsetLeft = -280; _companyPanel.OffsetRight = 280;
        _companyPanel.OffsetTop = 12;
        var box = new VBoxContainer();
        _companyLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 0) };
        _companyLabel.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_companyLabel);
        _briefBackRow = new HBoxContainer();
        var approve = new Button { Text = "Approve plan (Enter)", FocusMode = Control.FocusModeEnum.None };
        approve.Pressed += () => { if (_shownCompany != null) ApproveRequested?.Invoke(_shownCompany); };
        var reject = new Button { Text = "Reject (Backspace)", FocusMode = Control.FocusModeEnum.None };
        reject.Pressed += () => { if (_shownCompany != null) RejectRequested?.Invoke(_shownCompany); };
        _countdown = new Label();
        _countdown.AddThemeFontSizeOverride("font_size", 13);
        _briefBackRow.AddChild(approve);
        _briefBackRow.AddChild(reject);
        _briefBackRow.AddChild(_countdown);
        box.AddChild(_briefBackRow);
        _companyPanel.AddChild(box);
        _companyPanel.Visible = false;
        AddChild(_companyPanel);
    }

    /// <summary>The company the panel is about: one waiting for your answer first, then the selected man's,
    /// then your only company if you have just one.</summary>
    public static Company? CompanyToShow(Battle battle, Soldier? selected, Side playerSide)
    {
        foreach (var c in battle.Companies) if (c.Side == playerSide && battle.AwaitingApproval(c)) return c;
        if (selected?.Group.Company is { } sc && (sc.Side == playerSide)) return sc;
        Company? only = null;
        foreach (var c in battle.Companies)
        {
            if (c.Side != playerSide) continue;
            if (only != null) return null;
            only = c;
        }
        return only;
    }

    public void UpdateCompany(Battle battle, Soldier? selected, Side playerSide)
    {
        var c = CompanyToShow(battle, selected, playerSide);
        _shownCompany = c;
        _companyPanel.Visible = c != null;
        if (c == null) return;
        var t = c.Traits;
        var sb = new StringBuilder();
        sb.AppendLine($"{c.Name}, {c.CommanderName}   ({c.Effective}/{c.StartStrength} fit)   aggression {t.Aggression} · caution {t.Caution} · initiative {t.Initiative} · competence {t.Competence} · composure {t.Composure}");
        sb.AppendLine(c.Status);
        if (c.Plan != null && c.State is CompanyState.BriefBack or CompanyState.Executing)
        {
            sb.AppendLine($"Plan: {c.Plan.Summary}");
            sb.AppendLine($"He expects to lose {c.Plan.ExpectedLoss * 100:0}% of the company, with a {c.Plan.Success * 100:0}% chance of success.");
            for (int i = 0; i < c.Plan.Phases.Count; i++)
            {
                string mark = c.State == CompanyState.Executing ? (i < c.PhaseIndex ? "✓" : i == c.PhaseIndex ? "▶" : "·") : "·";
                sb.AppendLine($"  {mark} {i + 1}. {c.Plan.Phases[i].Name}");
            }
            if (c.Options.Count > 1)
                sb.Append("Also weighed: " + string.Join("; ", c.Options.Skip(1).Select(o => $"{GroundWar.Sim.AI.CompanyCommander.PlanName(o.Kind)} (lose {o.ExpectedLoss * 100:0}%, {o.Success * 100:0}% chance)")));
        }
        else if (c.State == CompanyState.Refused)
            sb.Append("He won't do it as things stand. Send the same order again to insist, or give him another.");
        _companyLabel.Text = sb.ToString().TrimEnd();
        bool waiting = battle.AwaitingApproval(c);
        _briefBackRow.Visible = waiting;
        if (waiting) _countdown.Text = $"  goes ahead in {battle.BriefBackCountdown(c):0} s unless you step in";
    }

    public Company? ShownCompany => _shownCompany;

    private static string Short(Message m)
    {
        string s = m.Describe();
        return s.Length > 70 ? s.Substring(0, 67) + "..." : s;
    }

    private readonly List<string> _logLines = new();

    /// <summary>The last few lines worth a commander's attention: casualties, men breaking or rallying, squads
    /// changing tactic. Every soldier's change of action is left out (it buries the rest), except for the
    /// selected soldier's. With fog of war on, the enemy's own decisions are hidden too.</summary>
    private string RecentLog(Battle battle, Soldier? selected, bool fog, Side playerSide)
    {
        _logLines.Clear();
        var entries = battle.Log.Entries;
        for (int i = entries.Count - 1; i >= 0 && _logLines.Count < 7; i--)
        {
            var e = entries[i];
            bool mine = selected != null && e.SoldierId == selected.Id;
            if (e.Kind == LogKind.Thought && !mine) continue;
            if (fog && e.Side != null && e.Side != playerSide && e.Kind != LogKind.Casualty) continue;
            _logLines.Add($"{(int)(e.Time / 60)}:{(int)e.Time % 60:00}  {e.Text}");
        }
        _logLines.Reverse();
        return string.Join("\n", _logLines);
    }
}
