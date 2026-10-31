using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

public class SquadTests
{
    private static Battle SquadAttack(ulong seed) => Battle.Load(TestData.Source, "scenarios/squad_attack.json", seed);

    [Fact]
    public void SquadsSplitIntoGunAndRifleTeams()
    {
        var battle = SquadAttack(1);
        foreach (var g in battle.Groups.Where(g => g.Members.Count >= 6))
        {
            Assert.Equal(2, g.Teams.Count);
            Assert.Contains(g.Gun.Members, m => m.Weapon.IsMachineGun);
            Assert.DoesNotContain(g.Rifles.Members, m => m.Weapon.IsMachineGun);
            Assert.Contains(g.Rifles.Members, m => m.IsLeader);
            Assert.Equal(g.Members.Count, g.Teams.Sum(t => t.Members.Count));
        }
    }

    [Fact]
    public void SoldiersOnlySeeWhatIsInTheirSightLine()
    {
        // Sight is checked when a man looks around (every half second); men move a little after that,
        // so allow a few borderline cases at wall ends, but nobody should see through the middle of a wall.
        var battle = SquadAttack(2);
        int pairs = 0, blocked = 0;
        for (int i = 0; i < 1200; i++)
        {
            battle.Step();
            foreach (var s in battle.Soldiers.Where(s => s.IsActive && s.NextPerceive - battle.Time > battle.Rules.Perception.Interval - 0.01f))
                foreach (var e in s.VisibleEnemies)
                {
                    pairs++;
                    if (battle.Map.SightBlocked(s.Position, e.Position)) blocked++;
                }
        }
        Assert.True(pairs > 100);
        Assert.True(blocked <= pairs * 0.02f, $"{blocked} of {pairs} sightings were through a wall");
    }

    [Fact]
    public void EachSquadHasItsOwnPictureOfTheEnemy()
    {
        var battle = SquadAttack(3);
        for (int i = 0; i < 300; i++) battle.Step();
        // Squads far apart don't magically share what they know.
        var red = battle.Groups.Where(g => g.Side == Side.Red).ToList();
        var a = red[0].Knowledge.Contacts.Select(c => c.EnemyId).ToHashSet();
        var b = red[1].Knowledge.Contacts.Select(c => c.EnemyId).ToHashSet();
        Assert.False(a.SetEquals(b) && a.Count > 0 && Vector2Distance(red[0], red[1]) > battle.Rules.Squad.ShareRadius,
            "two distant squads have exactly the same contacts");
        // Everything a squad knows was seen by one of its men, heard (muzzle flash), or passed on by a squad nearby.
        Assert.All(battle.Groups, g => Assert.All(g.Knowledge.Contacts, c => Assert.True(battle.Time - c.LastSeen <= battle.Rules.Perception.ContactMemory)));
    }

    private static float Vector2Distance(Group a, Group b) => System.Numerics.Vector2.Distance(a.Centroid, b.Centroid);

    [Fact]
    public void AttackingSquadFlanksAndTwoSquadsUsuallyTakeThePosition()
    {
        int blueWins = 0, flanks = 0, coveringFire = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var battle = SquadAttack(seed);
            var result = battle.RunToEnd();
            if (result.Winner == Side.Blue) blueWins++;
            if (battle.Log.Entries.Any(e => e.Text.Contains("rifle team flanks"))) flanks++;
            if (battle.Log.Entries.Any(e => e.Text.Contains("covering fire"))) coveringFire++;
        }
        Assert.True(flanks >= 3, $"only {flanks}/6 battles had a flank");
        Assert.True(coveringFire >= 3, $"only {coveringFire}/6 battles had area fire");
        Assert.True(blueWins >= 4, $"Blue took the position in only {blueWins}/6 battles");
    }

    [Fact]
    public void SuppressingSquadGoesToItsSupportPosition()
    {
        var battle = SquadAttack(4);
        var support = battle.Groups.First(g => g.OriginalOrder == GroupOrder.Suppress);
        float start = System.Numerics.Vector2.Distance(support.Centroid, support.SupportPosition!.Value);
        for (int i = 0; i < 1500 && support.Tactic != Tactic.Suppress; i++) battle.Step();
        float now = System.Numerics.Vector2.Distance(support.Centroid, support.SupportPosition!.Value);
        Assert.True(now < start * 0.5f, $"support squad still {now:0} m from its position (started {start:0} m)");
    }
}
