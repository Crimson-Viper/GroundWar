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
    private readonly SquadBrain _squadBrain;
    private readonly TankBrain _tankBrain;
    internal SoldierHash Hash { get; }

    public Battle(GameData data, ScenarioDef scenario, MapDef mapDef, ulong seed)
    {
        Data = data;
        Scenario = scenario;
        Seed = seed;
        Rng = new Rng(seed);
        Map = new BattleMap(mapDef, data.Rules.Combat);
        Grid = new BattleGrid(Map);
        _brain = new SoldierBrain(this);
        _squadBrain = new SquadBrain(this);
        _tankBrain = new TankBrain(this, _squadBrain);
        Hash = new SoldierHash(Map.Width, Map.Height);
        Spawn();
        Hash.Rebuild(_soldiers);
        UpdateGridControl();
    }

    /// <summary>Load a scenario file (and the map it names, unless <paramref name="mapOverride"/> names another) and set up the battle.</summary>
    public static Battle Load(IDataSource source, string scenarioPath, ulong seed, string? mapOverride = null)
    {
        var data = GameData.Load(source);
        var scenario = GameData.LoadScenario(source, scenarioPath);
        if (mapOverride != null) scenario.Map = mapOverride;
        var map = GameData.LoadMap(source, scenario.Map);
        return new Battle(data, scenario, map, seed);
    }

    public Soldier? GetSoldier(int id) => id >= 0 && id < _soldiers.Count ? _soldiers[id] : null;

    /// <summary>A sighting goes into the squad's picture and the side's picture (what the player sees).
    /// From milestone 4 the side's picture will only be updated when a report reaches HQ.</summary>
    internal void Report(Group g, Soldier enemy, Vector2 position, float confidence)
    {
        g.Knowledge.Report(enemy, position, Time, confidence);
        _knowledge[(int)g.Side].Report(enemy, position, Time, confidence);
    }

    internal static Side Enemy(Side side) => side == Side.Blue ? Side.Red : Side.Blue;

    internal void Note(Soldier? s, string text, LogKind kind = LogKind.Event) =>
        Log.Add(Time, s?.Id ?? -1, s == null ? text : $"{s.Label} {text}", s?.Side, kind);
    internal void Note(Group g, string text) => Log.Add(Time, -1, text, g.Side);

    private void Spawn()
    {
        foreach (var sideDef in Scenario.Sides)
        {
            var side = Enum.Parse<Side>(sideDef.Side, ignoreCase: true);
            foreach (var gd in sideDef.Groups)
            {
                var group = new Group(side)
                {
                    Id = _groups.Count,
                    Name = gd.Name,
                    Order = Enum.Parse<GroupOrder>(gd.Order, ignoreCase: true),
                    Objective = BattleMap.V(gd.Objective),
                    ObjectiveRadius = gd.ObjectiveRadius,
                    Facing = Geo.SafeNormalize(BattleMap.V(gd.Facing), Vector2.UnitX),
                    Rear = Geo.SafeNormalize(BattleMap.V(sideDef.Rear), -Vector2.UnitX),
                    SupportPosition = gd.SupportPosition != null ? BattleMap.V(gd.SupportPosition) : null,
                };
                group.OriginalOrder = group.Order;
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
                foreach (var (vehicleId, count) in gd.Vehicles.OrderBy(kv => kv.Key))
                {
                    if (!Data.Vehicles.TryGetValue(vehicleId, out var vehicle))
                        throw new InvalidDataException($"Scenario uses unknown vehicle '{vehicleId}'");
                    for (int i = 0; i < count; i++) AddVehicle(group, vehicle, center, size);
                }
                var leader = group.Members.FirstOrDefault(m => !m.Weapon.IsMachineGun) ?? group.Members.FirstOrDefault();
                if (leader != null) MakeLeader(group, leader, initial: true);
                SplitIntoTeams(group);
                group.StartStrength = group.Members.Count;
                group.HoldPosition = group.Objective;
                group.NextThink = Rng.Range(0f, Rules.Squad.ThinkInterval);
                foreach (var m in group.Members)
                {
                    m.Task = TaskMode.Hold;
                    m.TaskPosition = m.Position;
                    m.TaskRadius = Rules.Squad.TeamRadius;
                }
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

    /// <summary>Gun team: the machine guns plus enough riflemen to carry ammunition (about 40% of the squad).
    /// Rifle team: the rest, with the sergeant.</summary>
    private static void SplitIntoTeams(Group group)
    {
        var gun = new Team { Id = 0, Role = TeamRole.Gun };
        var rifles = new Team { Id = 1, Role = TeamRole.Rifle };
        int gunSize = Math.Max(1, (int)MathF.Round(group.Members.Count * 0.4f));
        foreach (var m in group.Members.Where(m => m.Weapon.IsMachineGun)) gun.Members.Add(m);
        foreach (var m in group.Members.Where(m => !m.Weapon.IsMachineGun && !m.IsLeader).Reverse())
        {
            if (gun.Members.Count >= gunSize) break;
            gun.Members.Add(m);
        }
        foreach (var m in group.Members) if (!gun.Members.Contains(m)) rifles.Members.Add(m);
        if (rifles.Members.Count == 0) { group.Teams.Add(gun); }
        else { group.Teams.Add(gun); group.Teams.Add(rifles); }
        foreach (var t in group.Teams) foreach (var m in t.Members) m.Team = t;
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
        long t0 = Profile.Start();
        Hash.Rebuild(_soldiers);
        foreach (var g in _groups)
        {
            if (Time < g.NextThink) continue;
            if (g.IsVehicles) { g.NextThink = float.MaxValue; continue; } // each tank thinks for itself
            _squadBrain.Think(g);
            g.NextThink = Time + Rules.Squad.ThinkInterval * Rng.Range(0.85f, 1.15f);
        }
        t0 = Profile.Lap("squads", t0);
        foreach (var s in _soldiers)
        {
            if (!s.IsActive || Time < s.NextPerceive) continue;
            Perceive(s);
            s.NextPerceive = Time + perceptionInterval;
        }
        t0 = Profile.Lap("perceive", t0);
        foreach (var s in _soldiers)
        {
            if (!s.IsActive || Time < s.NextThink) continue;
            if (s.IsTank) _tankBrain.Think(s);
            else _brain.Think(s);
            // Men in a fight think often; men with nothing going on think less often.
            bool busy = s.IsMoving || s.VisibleEnemies.Count > 0 || s.UnderFire(Time, Rules.Ai.UnderFireWindow);
            s.NextThink = Time + thinkInterval * (busy ? 1f : 2.5f) * Rng.Range(0.8f, 1.2f);
        }
        t0 = Profile.Lap("think", t0);
        foreach (var s in _soldiers)
        {
            if (!s.IsActive) continue;
            UpdateWeapon(s);
            if (s.IsTank) UpdateMainGun(s);
        }
        foreach (var s in _soldiers) if (s.IsActive && s.Action == SoldierAction.Dig) Dig(s);
        t0 = Profile.Lap("weapons", t0);
        foreach (var s in _soldiers)
        {
            if (!s.IsActive) continue;
            if (s.IsTank) MoveTank(s);
            else Move(s);
        }
        t0 = Profile.Lap("move", t0);
        foreach (var s in _soldiers) if (s.IsActive && !s.IsTank) UpdateMorale(s);
        t0 = Profile.Lap("morale", t0);
        UpdateGroups();

        if (Tick % 10 == 0)
        {
            UpdateGridControl();
            foreach (var k in _knowledge) k.Expire(Time, Rules.Perception.ContactMemory);
            foreach (var g in _groups) g.Knowledge.Expire(Time, Rules.Perception.ContactMemory);
        }
        CheckEnd();
        Profile.Lap("other", t0);
    }

    /// <summary>Run until a result or the time limit (used by tests and the headless runner).</summary>
    public BattleResult RunToEnd(float extraSeconds = 0f)
    {
        while (Result == null) Step();
        float until = Time + extraSeconds;
        while (Time < until) Step();
        return Result;
    }

    /// <summary>A man digging in: once he has dug long enough, there's a foxhole where he stands and he's in it.</summary>
    private void Dig(Soldier s)
    {
        s.DigProgress += Dt;
        if (s.DigProgress < Rules.Ai.DigTime) return;
        s.DigProgress = 0f;
        ReleaseCover(s);
        var hole = Map.AddFoxhole(s.Position);
        hole.ClaimedBy = s.Id;
        s.Cover = hole;
        s.Action = SoldierAction.Idle;
        s.Thought = "Dug in";
        Note(s, "dug a foxhole", LogKind.Thought);
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
                Note(g, $"{g.Name} ({g.Side}) is breaking!");
                foreach (var m in g.Members)
                    if (m.IsActive && m.MoraleState != MoraleState.Broken) m.Morale -= Rules.Morale.GroupBrokenShock;
            }
        }
    }

    private float LastShotTime()
    {
        float t = 0f;
        foreach (var s in _soldiers) if (s.LastFiredTime > t) t = s.LastFiredTime;
        return t;
    }

    /// <summary>Nobody is attacking any more and the guns have gone quiet: whoever holds the ground has won.</summary>
    /// <summary>Which side holds the objective it took: the one with more fighting men still on it.</summary>
    private Side? Holder()
    {
        int MenOn(Side side)
        {
            int n = 0;
            foreach (var g in _groups)
            {
                if (g.Side != side || !g.TookObjective) continue;
                foreach (var s in _soldiers)
                    if (s.Side == side && s.IsEffective && Vector2.Distance(s.Position, g.Objective) <= g.ObjectiveRadius + 10f) n++;
            }
            return n;
        }
        int blue = MenOn(Side.Blue), red = MenOn(Side.Red);
        if (blue == red) return null;
        return blue > red ? Side.Blue : Side.Red;
    }

    private BattleResult QuietResult()
    {
        if (Holder() is Side holder)
            return new BattleResult { Winner = holder, Time = Time, Reason = $"{holder} took the objective" };
        foreach (var side in new[] { Side.Blue, Side.Red })
            if (_groups.Any(g => g.Side == side && g.AttackFailed))
                return new BattleResult { Winner = Enemy(side), Time = Time, Reason = $"{side}'s attack failed" };
        return new BattleResult { Winner = null, Time = Time, Reason = "The fighting died down" };
    }

    /// <summary>Out of time: whoever holds the objective wins; otherwise the defender held. If both sides attacked,
    /// the side left clearly stronger (20 points more of its men still fighting) wins.</summary>
    private BattleResult TimeLimitResult()
    {
        if (Holder() is Side holder)
            return new BattleResult { Winner = holder, Time = Time, Reason = $"Time up: {holder} holds the objective" };
        bool blueAttacked = _groups.Any(g => g.Side == Side.Blue && g.OriginalOrder != GroupOrder.Hold);
        bool redAttacked = _groups.Any(g => g.Side == Side.Red && g.OriginalOrder != GroupOrder.Hold);
        if (blueAttacked != redAttacked)
        {
            var defender = blueAttacked ? Side.Red : Side.Blue;
            return new BattleResult { Winner = defender, Time = Time, Reason = $"Time up: {defender} held on" };
        }
        // Both sides attacked and nobody holds the ground: the side left clearly stronger came off better.
        float Fit(Side side)
        {
            int all = 0, fit = 0;
            foreach (var s in _soldiers)
            {
                if (s.Side != side) continue;
                all++;
                if (s.IsEffective) fit++;
            }
            return all == 0 ? 0f : fit / (float)all;
        }
        float blueFit = Fit(Side.Blue), redFit = Fit(Side.Red);
        if (MathF.Abs(blueFit - redFit) >= 0.2f)
        {
            var better = blueFit > redFit ? Side.Blue : Side.Red;
            return new BattleResult { Winner = better, Time = Time, Reason = $"Time up: {better} came off better" };
        }
        return new BattleResult { Winner = null, Time = Time, Reason = "Time up: neither side got the upper hand" };
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
        else if (!_groups.Any(g => g.IsAttacking && g.CountEffective > 0) && Time - LastShotTime() > Rules.Squad.QuietEndTime)
            Result = QuietResult();
        else if (Time >= Scenario.TimeLimit) Result = TimeLimitResult();
        if (Result != null) Log.Add(Time, -1, $"Battle over: {Result.Reason}");
    }
}
