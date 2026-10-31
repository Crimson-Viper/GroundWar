using System.Numerics;
using GroundWar.Sim.AI;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// One battle. Runs in fixed ticks of <see cref="Dt"/> seconds and knows nothing about Godot.
/// Godot (or the headless runner) calls <see cref="Step"/> and reads the soldiers and this tick's events.
/// </summary>
public sealed partial class Battle
{
    public const float Dt = 0.1f;

    public GameData Data { get; }
    public Rules Rules => Data.Rules;
    public ScenarioDef Scenario { get; }
    public BattleMap Map { get; }
    public BattleGrid Grid { get; }
    public ulong Seed { get; }
    public float Time { get; private set; }
    public int Tick { get; private set; }

    public IReadOnlyList<Soldier> Soldiers => _soldiers;
    public IReadOnlyList<Group> Groups => _groups;
    public SideKnowledge Knowledge(Side side) => _knowledge[(int)side];

    /// <summary>Shots fired during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<ShotEvent> Shots => _shots;
    /// <summary>Men hit during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<CasualtyEvent> Casualties => _casualties;
    public BattleLog Log { get; } = new();
    /// <summary>Set once one side can no longer fight, or time runs out. The battle can keep running after.</summary>
    public BattleResult? Result { get; private set; }

    internal Rng Rng { get; }
    private readonly List<Soldier> _soldiers = new();
    private readonly List<Group> _groups = new();
    private readonly SideKnowledge[] _knowledge = { new(Side.Blue), new(Side.Red) };
    private readonly List<ShotEvent> _shots = new();
    private readonly List<CasualtyEvent> _casualties = new();
    private readonly SoldierBrain _brain;

    public Battle(GameData data, ScenarioDef scenario, MapDef mapDef, ulong seed)
    {
        Data = data;
        Scenario = scenario;
        Seed = seed;
        Rng = new Rng(seed);
        Map = new BattleMap(mapDef, data.Rules.Combat);
        Grid = new BattleGrid(Map);
        _brain = new SoldierBrain(this);
        Spawn();
        UpdateGridControl();
    }

    /// <summary>Load a scenario file (and the map it names) and set up the battle.</summary>
    public static Battle Load(IDataSource source, string scenarioPath, ulong seed)
    {
        var data = GameData.Load(source);
        var scenario = GameData.LoadScenario(source, scenarioPath);
        var map = GameData.LoadMap(source, scenario.Map);
        return new Battle(data, scenario, map, seed);
    }

    public Soldier? GetSoldier(int id) => id >= 0 && id < _soldiers.Count ? _soldiers[id] : null;

    internal void Note(Soldier? s, string text) => Log.Add(Time, s?.Id ?? -1, s == null ? text : $"{s.Label} {text}");

    private void Spawn()
    {
        foreach (var sideDef in Scenario.Sides)
        {
            var side = Enum.Parse<Side>(sideDef.Side, ignoreCase: true);
            foreach (var gd in sideDef.Groups)
            {
                var group = new Group
                {
                    Id = _groups.Count,
                    Side = side,
                    Name = gd.Name,
                    Order = Enum.Parse<GroupOrder>(gd.Order, ignoreCase: true),
                    Objective = BattleMap.V(gd.Objective),
                    ObjectiveRadius = gd.ObjectiveRadius,
                    Facing = Geo.SafeNormalize(BattleMap.V(gd.Facing), Vector2.UnitX),
                    Rear = Geo.SafeNormalize(BattleMap.V(sideDef.Rear), -Vector2.UnitX),
                };
                _groups.Add(group);
                var center = BattleMap.V(gd.SpawnCenter);
                var size = BattleMap.V(gd.SpawnSize);
                // Sorted so the same scenario always spawns in the same order.
                foreach (var (weaponId, count) in gd.Weapons.OrderBy(kv => kv.Key == "rifle" ? 0 : 1).ThenBy(kv => kv.Key))
                {
                    if (!Data.Weapons.TryGetValue(weaponId, out var weapon))
                        throw new InvalidDataException($"Scenario uses unknown weapon '{weaponId}'");
                    for (int i = 0; i < count; i++) AddSoldier(group, weapon, center, size, gd.MoraleBonus);
                }
                var leader = group.Members.FirstOrDefault(m => !m.Weapon.IsMachineGun) ?? group.Members.FirstOrDefault();
                if (leader != null) MakeLeader(group, leader, initial: true);
            }
        }
    }

    private void AddSoldier(Group group, WeaponDef weapon, Vector2 center, Vector2 size, float moraleBonus)
    {
        var sr = Rules.Soldier;
        Vector2 pos = center + new Vector2(Rng.Range(-0.5f, 0.5f) * size.X, Rng.Range(-0.5f, 0.5f) * size.Y);
        pos = Map.Nav.NearestWalkable(Map.Clamp(pos, 2f));
        float morale = Math.Clamp(Rng.Range(sr.MoraleMin, sr.MoraleMax) + moraleBonus, 1f, 100f);
        var s = new Soldier
        {
            Id = _soldiers.Count,
            Side = group.Side,
            Group = group,
            Weapon = weapon,
            Skill = Rng.Range(sr.SkillMin, sr.SkillMax),
            StartMorale = morale,
            Morale = morale,
            Position = pos,
            Facing = group.Facing,
            Magazine = weapon.MagazineSize,
            SpareMagazines = weapon.Magazines,
            NextThink = Rng.Range(0f, Rules.Ai.ThinkInterval),
            NextPerceive = Rng.Range(0f, Rules.Perception.Interval),
            FireTimer = Rng.Range(0f, weapon.FireInterval),
        };
        _soldiers.Add(s);
        group.Members.Add(s);
    }

    private void MakeLeader(Group group, Soldier s, bool initial)
    {
        if (group.Leader != null) group.Leader.IsLeader = false;
        group.Leader = s;
        s.IsLeader = true;
        if (initial)
        {
            // The sergeant is a little steadier than his men.
            s.StartMorale = Math.Min(100f, s.Morale + Rules.Soldier.LeaderMoraleBonus);
            s.Morale = s.StartMorale;
        }
        else
        {
            Note(s, $"takes command of {group.Name}");
        }
    }

    /// <summary>Advance the battle by one tick.</summary>
    public void Step()
    {
        Tick++;
        Time = Tick * Dt;
        _shots.Clear();
        _casualties.Clear();

        float perceptionInterval = Rules.Perception.Interval;
        float thinkInterval = Rules.Ai.ThinkInterval;
        foreach (var s in _soldiers)
        {
            if (!s.IsActive || Time < s.NextPerceive) continue;
            Perceive(s);
            s.NextPerceive = Time + perceptionInterval;
        }
        foreach (var s in _soldiers)
        {
            if (!s.IsActive || Time < s.NextThink) continue;
            _brain.Think(s);
            s.NextThink = Time + thinkInterval * Rng.Range(0.8f, 1.2f);
        }
        foreach (var s in _soldiers) if (s.IsActive) UpdateWeapon(s);
        foreach (var s in _soldiers) if (s.IsActive) Move(s);
        foreach (var s in _soldiers) if (s.IsActive) UpdateMorale(s);
        UpdateGroups();

        if (Tick % 10 == 0)
        {
            UpdateGridControl();
            foreach (var k in _knowledge) k.Expire(Time, Rules.Perception.ContactMemory);
        }
        CheckEnd();
    }

    /// <summary>Run until a result or the time limit (used by tests and the headless runner).</summary>
    public BattleResult RunToEnd(float extraSeconds = 0f)
    {
        while (Result == null) Step();
        float until = Time + extraSeconds;
        while (Time < until) Step();
        return Result;
    }

    private void UpdateGridControl() =>
        Grid.UpdateControl(_soldiers.Where(s => s.IsEffective).Select(s => (s.Position, s.Side == Side.Blue ? -1 : 1)));

    private void UpdateGroups()
    {
        foreach (var g in _groups)
        {
            if (g.Leader != null && !g.Leader.IsActive)
            {
                var next = g.Members.Where(m => m.IsEffective).OrderByDescending(m => m.Skill).FirstOrDefault();
                if (next != null) MakeLeader(g, next, initial: false);
                else g.Leader = null;
            }
            if (g.HasBroken) continue;
            int active = 0, broken = 0;
            foreach (var m in g.Members)
            {
                if (!m.IsActive) continue;
                active++;
                if (m.MoraleState == MoraleState.Broken) broken++;
            }
            if (active > 0 && broken >= active * Rules.Morale.GroupBrokenFraction && broken > 0)
            {
                g.HasBroken = true;
                Note(null, $"{g.Name} ({g.Side}) is breaking!");
                foreach (var m in g.Members)
                    if (m.IsActive && m.MoraleState != MoraleState.Broken) m.Morale -= Rules.Morale.GroupBrokenShock;
            }
        }
    }

    private void CheckEnd()
    {
        if (Result != null) return;
        int blue = 0, red = 0;
        foreach (var s in _soldiers)
        {
            if (!s.IsEffective) continue;
            if (s.Side == Side.Blue) blue++; else red++;
        }
        if (blue == 0 && red == 0) Result = new BattleResult { Winner = null, Time = Time, Reason = "Both sides broke" };
        else if (blue == 0) Result = new BattleResult { Winner = Side.Red, Time = Time, Reason = "Blue can no longer fight" };
        else if (red == 0) Result = new BattleResult { Winner = Side.Blue, Time = Time, Reason = "Red can no longer fight" };
        else if (Time >= Scenario.TimeLimit) Result = new BattleResult { Winner = null, Time = Time, Reason = "Time limit reached" };
        if (Result != null) Note(null, $"Battle over: {Result.Reason}");
    }
}
