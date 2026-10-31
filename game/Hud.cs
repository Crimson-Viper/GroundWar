using System.Linq;
using System.Text;
using Godot;
using GroundWar.Sim;
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
        "SOLDIER  left-click select · F follow · Esc deselect\n" +
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
                sb.AppendLine($"{g.Name} ({g.CountEffective}/{g.Members.Count}) {g.Tactic}");
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

        var lines = battle.Log.Entries.Skip(System.Math.Max(0, battle.Log.Entries.Count - 7))
            .Select(e => $"{(int)(e.Time / 60)}:{(int)e.Time % 60:00}  {e.Text}");
        _log.Text = string.Join("\n", lines);

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
}
