using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

public class TankTests
{
    private static Battle TankSupport(ulong seed) => Battle.Load(TestData.Source, "scenarios/tank_support.json", seed);

    [Fact]
    public void OnlyAntiTankWeaponsCanGetThroughArmour()
    {
        var data = TestData.Data;
        var tank = data.Vehicles["tank"];
        foreach (var name in new[] { "rifle", "lmg" })
            Assert.False(data.Weapons[name].CanHurtArmour, $"{name} should not hurt a tank");
        var atr = data.Weapons["atr"];
        // Up close an anti-tank rifle gets through the side and rear, but never the front at range.
        Assert.True(atr.PenetrationAt(50) > tank.ArmourSide);
        Assert.True(atr.PenetrationAt(50) > tank.ArmourRear);
        Assert.True(atr.PenetrationAt(atr.Range) < tank.ArmourFront);
    }

    [Fact]
    public void TankDrivesTowardItsObjective()
    {
        var battle = TankSupport(3);
        var tank = battle.Soldiers.Single(s => s.IsTank);
        float startX = tank.Position.X;
        for (int i = 0; i < 600; i++) battle.Step(); // one minute
        Assert.True(tank.Position.X > startX + 30f, $"tank only moved from {startX:0} to {tank.Position.X:0}");
    }

    [Fact]
    public void TanksAreOnlyKnockedOutByAntiTankWeaponsAndTheMainGunKillsMen()
    {
        int tankLosses = 0, gunKills = 0, shellsFired = 0;
        for (ulong seed = 1; seed <= 15; seed++)
        {
            var battle = TankSupport(seed);
            var tank = battle.Soldiers.Single(s => s.IsTank);
            int startAmmo = tank.MainGunAmmo;
            while (battle.Result == null)
            {
                battle.Step();
                foreach (var c in battle.Casualties)
                {
                    var victim = battle.Soldiers[c.SoldierId];
                    var shooter = battle.Soldiers[c.ShooterId];
                    if (victim.IsTank)
                    {
                        tankLosses++;
                        Assert.True(shooter.Weapon.CanHurtArmour, $"tank knocked out by a {shooter.Weapon.Name}");
                    }
                    else if (shooter.IsTank && victim.Side != shooter.Side) gunKills++;
                }
            }
            shellsFired += startAmmo - tank.MainGunAmmo;
        }
        Assert.True(shellsFired > 15, $"the tank fired only {shellsFired} shells in 15 battles");
        Assert.True(gunKills > 0, "the tank never hit anyone");
        Assert.True(tankLosses > 0, "the anti-tank rifles never knocked the tank out: they are not a real threat");
    }
}
