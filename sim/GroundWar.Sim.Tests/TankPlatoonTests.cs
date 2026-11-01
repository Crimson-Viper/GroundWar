using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Milestone 8: tank platoons with crew AI. See docs/systems/tanks.md.</summary>
public class TankPlatoonTests
{
    private static Battle Platoons(ulong seed = 1) => Battle.Load(TestData.Source, "scenarios/tank_platoons.json", seed);
    private static void Run(Battle b, float seconds) { int n = (int)(seconds / Battle.Dt); for (int i = 0; i < n; i++) b.Step(); }
    private static List<Soldier> BlueTanks(Battle b) => b.Soldiers.Where(s => s.IsTank && s.Side == Side.Blue).ToList();
    private static void Lose(Soldier tank, CrewRole role) => tank.Crew.Single(m => m.CrewRole == role).Status = SoldierStatus.Dead;

    [Fact]
    public void EveryTankHasACrewOfFiveRidingInside()
    {
        var b = Platoons();
        Run(b, 30f);
        foreach (var t in b.Soldiers.Where(s => s.IsTank))
        {
            Assert.Equal(5, t.Crew.Count);
            Assert.Equal(new[] { CrewRole.Commander, CrewRole.Gunner, CrewRole.Loader, CrewRole.Driver, CrewRole.BowGunner }, t.Crew.Select(m => m.CrewRole));
            foreach (var m in t.Crew)
            {
                Assert.Equal(SoldierStatus.Mounted, m.Status);
                Assert.True(m.InTank);
                Assert.True(System.Numerics.Vector2.Distance(m.Position, t.Position) < 1f, "the crew ride with their tank");
            }
        }
        // Men inside a tank are no part of the fight: their group doesn't report or break.
        Assert.DoesNotContain(b.Messages, m => m.Group.IsCrew);
        Assert.DoesNotContain(b.Groups, g => g.IsCrew && g.HasBroken);
    }

    [Fact]
    public void EachManLostMakesTheTankFightWorse()
    {
        var b = Platoons();
        var t = BlueTanks(b)[0];
        Assert.Equal((1f, 1f), Battle.GunCrew(t));
        Assert.Equal(1f, Battle.DriveFactor(t));
        Assert.Equal(1f, Battle.LookoutFactor(t));

        Lose(t, CrewRole.Loader);
        Assert.Equal((1f, 1.8f), Battle.GunCrew(t)); // the gunner loads for himself
        Lose(t, CrewRole.Driver);
        Assert.Equal(0.7f, Battle.DriveFactor(t)); // the bow gunner drives
        Lose(t, CrewRole.Commander);
        Assert.Equal(0.5f, Battle.LookoutFactor(t)); // buttoned up, half blind

        var u = BlueTanks(b)[1];
        Lose(u, CrewRole.Gunner);
        Assert.Equal((0.75f, 1.6f), Battle.GunCrew(u)); // the loader moves up to the gun
    }

    [Fact]
    public void WithTooFewMenLeftTheCrewAbandonTheTankAndBailOut()
    {
        var b = Platoons();
        Run(b, 5f);
        var t = BlueTanks(b)[0];
        var shooter = b.Soldiers.First(s => s.Side == Side.Red);
        b.HitCrew(t, shooter, 3);
        Assert.Equal(SoldierStatus.Dead, t.Status); // a tank given up counts as lost
        Assert.True(t.BailedOut);
        var survivors = t.Crew.Where(m => m.GotOut).ToList();
        Assert.Equal(2, survivors.Count);
        foreach (var m in survivors)
        {
            Assert.Equal(SoldierStatus.Active, m.Status);
            Assert.False(m.InTank);
            Assert.True(m.Suppression > 0f, "they come out shaken");
        }
        // The men hit inside stay inside.
        Assert.All(t.Crew.Where(m => !m.GotOut), m => Assert.True(m.InTank && m.Status is SoldierStatus.Dead or SoldierStatus.Wounded));
        // The platoon's crews group now has someone in charge and a team, and makes its way back.
        var crews = survivors[0].Group;
        Assert.True(crews.IsCrew);
        Assert.NotNull(crews.Leader);
        Assert.Single(crews.Teams);
        Run(b, 20f);
        Assert.All(survivors.Where(m => m.IsActive), m => Assert.False(m.InTank));
    }

    [Fact]
    public void ArmourPiercingShotGoesThroughAFrontThatHighExplosiveCannot()
    {
        var data = TestData.Data;
        var tank = data.Vehicles["tank"];
        var ap = data.Weapons[tank.MainGunAp!];
        var he = data.Weapons["tank_gun"];
        Assert.True(ap.PenetrationAt(500) > tank.ArmourFront);
        Assert.False(he.PenetrationAt(100) > tank.ArmourFront);
        Assert.False(ap.IsShell); // a solid shot: no blast
        Assert.Equal(20, BlueTanks(Platoons())[0].ApAmmo);
    }

    [Fact]
    public void AttachedTanksGetAPartInTheCompanysAttackPlan()
    {
        var b = Platoons();
        var a = b.Companies.Single(c => c.Name == "A Company");
        var platoon = b.Groups.Single(g => g.Name == "1st Tank Platoon");
        while (b.Time < 200f && (a.Plan == null || platoon.Company != a)) b.Step();
        Assert.Same(a, platoon.Company);
        Assert.NotNull(a.Plan);
        Assert.Contains(platoon, a.Plan!.Tanks);
        Assert.Contains(a.Plan.Phases, ph => ph.Tasks.Any(t => t.Squad == platoon && t.Order.Kind == Comms.OrderKind.Attack));
        Assert.Contains("1st Tank Platoon", a.Plan.Summary);
    }

    [Fact]
    public void TanksThatArriveAfterThePlanIsMadeAreGivenAPartInIt()
    {
        // The attach order goes by radio, so in some battles the company has already planned when the tanks arrive.
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var b = Platoons(seed);
            var a = b.Companies.Single(c => c.Name == "A Company");
            var platoon = b.Groups.Single(g => g.Name == "1st Tank Platoon");
            bool plannedFirst = false;
            while (b.Time < 200f && platoon.Company != a)
            {
                b.Step();
                if (a.Plan != null && platoon.Company != a) plannedFirst = true;
            }
            if (!plannedFirst || platoon.Company != a) continue;
            if (a.Plan is { Kind: not PlanKind.WaitForSupport })
                Assert.Contains(platoon, a.Plan.Tanks);
            Assert.Contains(b.Log.Entries, e => e.Text.Contains("1st Tank Platoon has") && e.Text.Contains("Capt."));
            return;
        }
        Assert.Fail("no battle where the tanks arrived after the plan");
    }

    [Fact]
    public void ThePlatoonBoundsHalfAtATimeWithTheOthersOnOverwatch()
    {
        bool bounded = false;
        for (ulong seed = 1; seed <= 5 && !bounded; seed++)
        {
            var b = Platoons(seed);
            while (b.Time < 400f && b.Result == null && !bounded)
            {
                b.Step();
                var live = BlueTanks(b).Where(t => t.IsActive).ToList();
                bounded = live.Any(t => t.Overwatch) && live.Any(t => !t.Overwatch && t.PlatoonGoal != null);
            }
        }
        Assert.True(bounded, "never saw some tanks moving while others watched");
    }

    [Fact]
    public void AStuckTankWithEnemyInfantryCloseMayBeAbandoned()
    {
        var b = Platoons();
        var t = BlueTanks(b)[0];
        // Not stuck: never.
        for (int i = 0; i < 200; i++) b.MaybeAbandonStuck(t, 10f, false);
        Assert.Equal(SoldierStatus.Active, t.Status);
        t.Immobilised = true;
        // Friendly infantry near, or the enemy far off: they stay.
        for (int i = 0; i < 200; i++) { b.MaybeAbandonStuck(t, 10f, true); b.MaybeAbandonStuck(t, 80f, false); }
        Assert.Equal(SoldierStatus.Active, t.Status);
        for (int i = 0; i < 200 && t.Status == SoldierStatus.Active; i++) b.MaybeAbandonStuck(t, 10f, false);
        Assert.Equal(SoldierStatus.Dead, t.Status);
        Assert.True(t.BailedOut);
    }
}
