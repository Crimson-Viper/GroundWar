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
        "BATTALION  Y / U artillery HE / smoke, then click the target · K hand the battle to your second in command (or take it back)\n" +
        "WATCH    J battle cam · Z map table · O after-action report · M mute · Shift+R replay with your orders · [ ] back / on 30 s\n" +
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
        _log.OffsetTop = -312;
        _log.OffsetBottom = -154;
        _log.VerticalAlignment = VerticalAlignment.Bottom;
        _log.AddThemeColorOverride("font_outline_color", Colors.Black);
        _log.AddThemeConstantOverride("outline_size", 4);
        AddChild(_log);

        _help = new Label { Text = HelpText };
        _help.AddThemeFontSizeOverride("font_size", 13);
        _help.AnchorTop = _help.AnchorBottom = 1f;
        _help.OffsetTop = -150;
        _help.OffsetBottom = -4;
        _help.OffsetLeft = 12;
        _help.VerticalAlignment = VerticalAlignment.Bottom;
        _help.AddThemeColorOverride("font_outline_color", Colors.Black);
        _help.AddThemeConstantOverride("outline_size", 4);
        AddChild(_help);

        BuildOrderPanel();
        BuildCompanyPanel();
        BuildBattalionPanel();
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
        sb.AppendLine($"Time {minutes}:{seconds:00}   Speed {speed}x{(paused ? "   PAUSED" : "")}{(battle.Replaying ? "   REPLAY" : "")}");
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
        // A tank platoon's crews only show up once some of them have bailed out.
        var groups = battle.Groups.Where(g => (!fog || g.Side == playerSide) && (!g.IsCrew || g.CountActive > 0)).ToList();
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
                    string role = s.Group.Leader == s ? "platoon leader" : "tank";
                    if (s.Overwatch) role += ", on overwatch";
                    else if (s.PlatoonGoal != null) role += ", moving up";
                    t.AppendLine($"In the platoon: {role}");
                    t.AppendLine($"Main gun: {s.MainGunAmmo} HE, {s.ApAmmo} AP{(s.MainGunTimer > 0 ? $", loading ({s.MainGunTimer:0} s)" : ", loaded")}");
                    t.AppendLine($"Machine gun: {s.Magazine} + {s.SpareMagazines} x {s.Weapon.MagazineSize}{(s.ReloadTimer > 0 ? "  (reloading)" : "")}");
                    t.AppendLine($"Sees {s.VisibleEnemies.Count} enemies");
                }
                if (s.Crew.Count > 0)
                {
                    var crew = s.Crew.Select(m => $"{CrewName(m.CrewRole)} {CrewState(m)}").ToList();
                    t.AppendLine("Crew: " + string.Join(", ", crew.Take(3)));
                    t.AppendLine("      " + string.Join(", ", crew.Skip(3)));
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
            _banner.Text = $"{r.Reason}. {who} ({(int)(r.Time / 60)}:{(int)r.Time % 60:00})\nO report  ·  Shift+R replay with your orders  ·  R restart  ·  N new battle";
            _banner.Modulate = r.Winner == Side.Blue ? new Color(0.6f, 0.75f, 1f) : r.Winner == Side.Red ? new Color(1f, 0.65f, 0.6f) : Colors.White;
            _banner.Visible = true;
        }
        else _banner.Visible = false;
    }

    // ---------------------------------------------------------------- orders and messages (milestone 4)

    /// <summary>Main listens to this: the player pressed an order button.</summary>
    public System.Action<OrderKind>? OrderRequested { get; set; }
    /// <summary>HE or Smoke pressed with a mortar section selected.</summary>
    public System.Action<FireKind>? FireRequested { get; set; }
    /// <summary>Attach (false) or Back to battalion (true) pressed with an MG section selected.</summary>
    public System.Action<bool>? AttachRequested { get; set; }
    private Button _heButton = null!, _smokeButton = null!, _attachButton = null!, _detachButton = null!;
    private HBoxContainer _orderRow = null!, _supportRow = null!;
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
        var row = _orderRow = new HBoxContainer();
        foreach (var (kind, text) in new[] { (OrderKind.Advance, "Advance"), (OrderKind.Attack, "Attack"), (OrderKind.Suppress, "Suppress"),
                                             (OrderKind.Hold, "Hold"), (OrderKind.Halt, "Halt"), (OrderKind.FallBack, "Fall back") })
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => OrderRequested?.Invoke(kind);
            row.AddChild(b);
        }
        box.AddChild(row);
        // Battalion support (milestone 7): fire missions for a mortar section, attach / detach for an MG section.
        _supportRow = new HBoxContainer();
        Button Support(string text, System.Action act)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += act;
            _supportRow.AddChild(b);
            return b;
        }
        _heButton = Support("Fire HE", () => FireRequested?.Invoke(FireKind.He));
        _smokeButton = Support("Fire smoke", () => FireRequested?.Invoke(FireKind.Smoke));
        _attachButton = Support("Attach to a company", () => AttachRequested?.Invoke(false));
        _detachButton = Support("Back to battalion", () => AttachRequested?.Invoke(true));
        box.AddChild(_supportRow);
        _orderStatus = new Label();
        _orderStatus.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_orderStatus);
        _orderPanel.AddChild(box);
        _orderPanel.Visible = false;
        AddChild(_orderPanel);
    }

    /// <summary>The order panel (when one of your squads is selected) and the list of messages on their way.
    /// A squad in a company is ordered through its company commander.</summary>
    public void UpdateOrders(Battle battle, Soldier? selected, Side playerSide, OrderKind? pending, FireKind pendingFire = FireKind.He)
    {
        var hq = battle.Hq(playerSide);
        bool mine = selected != null && selected.Side == playerSide;
        _orderPanel.Visible = mine;
        if (mine)
        {
            var own = selected!.Group;
            var g = own.Company?.HqGroup ?? own;
            string who = g.IsCompanyHq ? $"{g.Company!.Name} ({g.Company.CommanderName})" : g.Name;
            _orderTitle.Text = hq == null ? $"Orders for {who} (no HQ in this scenario: orders arrive at once)" : $"Orders for {who}, from {hq.Name}";
            var ms = own.Mortars;
            if (ms != null) _orderTitle.Text += $"   ({ms.Tubes} mortars: {ms.AmmoHe} HE, {ms.AmmoSmoke} smoke left)";
            _heButton.Visible = _smokeButton.Visible = ms != null;
            _heButton.Disabled = ms is not { AmmoHe: > 0 };
            _smokeButton.Disabled = ms is not { AmmoSmoke: > 0 };
            _attachButton.Visible = own.IsAsset && ms == null && own.Company == null;
            _detachButton.Visible = own.IsAsset && ms == null && own.Company != null;
            if (_detachButton.Visible) _detachButton.Text = $"Back to battalion (now with {own.Company!.Name})";
            _supportRow.Visible = _heButton.Visible || _attachButton.Visible || _detachButton.Visible;
            _orderPanel.OffsetTop = _supportRow.Visible ? -196 : -160;
            if (pending == OrderKind.FireMission)
                _orderStatus.Text = $"Click the map: where the mortars fire {(pendingFire == FireKind.Smoke ? "smoke" : "high explosive")}. Esc cancels.";
            else if (pending == OrderKind.Attach)
                _orderStatus.Text = $"Click a man of the company {own.Name} is to join. Esc cancels.";
            else if (pending != null) _orderStatus.Text = $"Click the map: where to {pending.Value.ToString().ToLowerInvariant()}. Esc cancels.";
            else
            {
                Message? last = null;
                foreach (var m in battle.Messages) if (m.Down && (m.Group == g || m.Group == own) && m.Company == null) last = m;
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

    // ---------------------------------------------------------------- battalion (milestone 7, part 2)

    /// <summary>Main listens to these: call the artillery (then click the target), and hand the battle to the staff or take it back.</summary>
    public System.Action<FireKind>? ArtilleryRequested { get; set; }
    public System.Action? HandOverRequested { get; set; }
    private PanelContainer _battalionPanel = null!;
    private Label _battalionLabel = null!;
    private Button _artyHeButton = null!, _artySmokeButton = null!, _handOverButton = null!;

    private void BuildBattalionPanel()
    {
        _battalionPanel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.05f, 0.78f) };
        style.SetContentMarginAll(10);
        style.SetCornerRadiusAll(6);
        _battalionPanel.AddThemeStyleboxOverride("panel", style);
        _battalionPanel.AnchorLeft = _battalionPanel.AnchorRight = 1f;
        _battalionPanel.AnchorTop = _battalionPanel.AnchorBottom = 1f;
        _battalionPanel.OffsetLeft = -460; _battalionPanel.OffsetRight = -12;
        // Sits just above the messages panel and grows upwards as its text gets longer.
        _battalionPanel.OffsetTop = -340; _battalionPanel.OffsetBottom = -340;
        _battalionPanel.GrowVertical = Control.GrowDirection.Begin;
        var box = new VBoxContainer();
        _battalionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(430, 0) };
        _battalionLabel.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_battalionLabel);
        var row = new HBoxContainer();
        Button Add(string text, System.Action act)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += act;
            row.AddChild(b);
            return b;
        }
        _artyHeButton = Add("Artillery HE (Y)", () => ArtilleryRequested?.Invoke(FireKind.He));
        _artySmokeButton = Add("Artillery smoke (U)", () => ArtilleryRequested?.Invoke(FireKind.Smoke));
        _handOverButton = Add("Hand over (K)", () => HandOverRequested?.Invoke());
        box.AddChild(row);
        _battalionPanel.AddChild(box);
        _battalionPanel.Visible = false;
        AddChild(_battalionPanel);
    }

    /// <summary>The battalion panel: who is running your battalion, its key points and artillery; with fog off,
    /// what the enemy battalion commander is doing too.</summary>
    public void UpdateBattalion(Battle battle, Side playerSide, bool fog, FireKind? pendingArtillery)
    {
        var bn = battle.BattalionOf(playerSide);
        var bt = battle.Batteries.FirstOrDefault(b => b.Side == playerSide);
        _battalionPanel.Visible = bn != null || bt != null;
        if (!_battalionPanel.Visible) return;
        var sb = new StringBuilder();
        if (bn != null)
        {
            sb.AppendLine(bn.Active ? $"Your second in command, {bn.Commander}, is running the battalion. K takes it back." : $"You command the battalion. K hands it to {bn.Commander}.");
            if (bn.Active && bn.Intent.Length > 0) sb.AppendLine($"  {bn.Intent}");
        }
        var known = battle.Knowledge(playerSide);
        foreach (var p in battle.KeyPoints)
        {
            int enemy = known.Contacts.Count(k => battle.Time - k.LastSeen < 120f && System.Numerics.Vector2.Distance(k.LastPosition, p.Position) < p.Radius + 40f);
            int own = battle.Soldiers.Count(s => s.Side == playerSide && s.IsEffective && System.Numerics.Vector2.Distance(s.Position, p.Position) < p.Radius);
            sb.AppendLine($"  {char.ToUpperInvariant(p.Name[0])}{p.Name[1..]}: {own} of ours there, {enemy} enemy known");
        }
        if (bt != null) sb.AppendLine($"{bt.Name}: {bt.Guns} guns off the map, {bt.AmmoHe} HE and {bt.AmmoSmoke} smoke left");
        if (pendingArtillery != null) sb.AppendLine($"Click the map: where the artillery fires {(pendingArtillery == FireKind.Smoke ? "smoke" : "high explosive")}. Esc cancels.");
        if (!fog)
            foreach (var other in battle.Battalions.Where(b => b.Side != playerSide && b.Active && b.Intent.Length > 0))
                sb.AppendLine($"{other.Side}, {other.Commander}: {other.Intent}");
        _battalionLabel.Text = sb.ToString().TrimEnd();
        _artyHeButton.Visible = _artySmokeButton.Visible = bt != null;
        _artyHeButton.Disabled = bt is not { AmmoHe: > 0 };
        _artySmokeButton.Disabled = bt is not { AmmoSmoke: > 0 };
        _handOverButton.Visible = bn != null;
        _handOverButton.Text = bn is { Active: true } ? "Take command (K)" : $"Hand over to {bn?.Commander} (K)";
    }

    // ---------------------------------------------------------------- after-action report (milestone 6)

    private PanelContainer? _reportPanel;
    private RichTextLabel? _reportText;

    public void ShowReport(string text)
    {
        if (_reportPanel == null)
        {
            _reportPanel = new PanelContainer();
            var style = new StyleBoxFlat { BgColor = new Color(0.86f, 0.81f, 0.66f, 0.97f), BorderColor = new Color(0.3f, 0.22f, 0.12f) };
            style.SetBorderWidthAll(3);
            style.SetContentMarginAll(18);
            style.SetCornerRadiusAll(4);
            _reportPanel.AddThemeStyleboxOverride("panel", style);
            _reportPanel.AnchorLeft = 0.5f; _reportPanel.AnchorRight = 0.5f; _reportPanel.AnchorTop = 0.5f; _reportPanel.AnchorBottom = 0.5f;
            _reportPanel.OffsetLeft = -560; _reportPanel.OffsetRight = 560; _reportPanel.OffsetTop = -290; _reportPanel.OffsetBottom = 330; // clear of the end banner and its hint line
            _reportText = new RichTextLabel { ScrollActive = true, SelectionEnabled = true, FitContent = false, CustomMinimumSize = new Vector2(1080, 620) };
            // A typewriter face, so the report's columns line up.
            var mono = new SystemFont { FontNames = new[] { "Courier New", "DejaVu Sans Mono", "Consolas", "Menlo", "monospace" } };
            _reportText.AddThemeFontOverride("normal_font", mono);
            _reportText.AddThemeFontSizeOverride("normal_font_size", 13);
            _reportText.AddThemeColorOverride("default_color", new Color(0.12f, 0.09f, 0.06f));
            _reportPanel.AddChild(_reportText);
            AddChild(_reportPanel);
        }
        _reportText!.Text = text + "\n\n(O closes this report. Shift+R watches the battle again with your orders.)";
        _reportPanel.Visible = true;
    }

    public void HideReport() { if (_reportPanel != null) _reportPanel.Visible = false; }

    public void ToggleReport(string text)
    {
        if (_reportPanel is { Visible: true }) HideReport();
        else ShowReport(text);
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
            sb.Append("He won't do it as things stand. Send the same order again to insist, or give him another." +
                      (c.RefusalFire != null ? " He has asked for mortar fire on it and will think again once it has landed." : ""));
        // His fire requests still being worked on.
        foreach (var fm in battle.FireMissions)
        {
            if (fm.RequestedBy != c || fm.Status is FireMissionStatus.Done or FireMissionStatus.Refused or FireMissionStatus.Cancelled) continue;
            string state = fm.Status switch
            {
                _ when fm.Waiting && fm.Status == FireMissionStatus.Firing => "on call: the mortars are laid on, waiting for his word",
                _ when fm.Waiting => "on call, " + (fm.Status == FireMissionStatus.Requested ? "on its way to battalion" : "battalion has passed it on"),
                FireMissionStatus.Requested => "asked for, on its way to battalion",
                FireMissionStatus.Ordered => "battalion has ordered it",
                _ => fm.RoundsLanded > 0 ? $"{fm.RoundsLanded} of {fm.Rounds} rounds down" : "the mortars are laying on",
            };
            sb.Append($"\nMortars: {fm.Describe()} on {fm.Reason}: {state}");
        }
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

    private static string CrewName(CrewRole r) => r switch
    {
        CrewRole.Commander => "commander", CrewRole.Gunner => "gunner", CrewRole.Loader => "loader",
        CrewRole.Driver => "driver", CrewRole.BowGunner => "bow gunner", _ => "crewman",
    };

    private static string CrewState(Soldier m) => m.Status switch
    {
        SoldierStatus.Mounted => "ok",
        SoldierStatus.Dead => "killed",
        SoldierStatus.Wounded => "wounded",
        _ => "bailed out",
    };
}
