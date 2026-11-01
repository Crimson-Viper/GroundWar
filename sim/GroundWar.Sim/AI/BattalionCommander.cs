using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.AI;

/// <summary>
/// The battalion commander (milestone 7, part 2): the enemy's, and the player's own staff if he hands the battle
/// over to them. He orders companies, never squads, and only knows what reaches his HQ: the company reports and
/// the enemy sightings in them. An attacker takes the key points one at a time, nearest first, then goes for the
/// enemy HQ. A defender holds his key points, sends his reserve to retake one that falls, and brings it back to
/// guard his HQ if the enemy gets close. Both call the artillery on enemy they know are bunched together.
/// See docs/systems/battalion-commander.md.
/// </summary>
public sealed class BattalionCommander
{
    private readonly Battle _b;

    public BattalionCommander(Battle battle) { _b = battle; }

    private BattalionRules R => _b.Rules.Battalion;
    private float Time => _b.Time;

    public void Think(Battalion bn)
    {
        bn.NextThink = Time + R.ThinkInterval * _b.Rng.Range(0.85f, 1.15f);
        var hq = _b.Hq(bn.Side);
        if (hq == null || hq.Destroyed) return;
        if (!bn.Started) Start(bn);
        CheckTasks(bn);
        UpdateHeld(bn);
        if (bn.Posture == Posture.Attack) Attack(bn);
        else Defend(bn);
        Artillery(bn);
        // Still pressing: an attack or counterattack under way, or (for an attacker) places still to take and a
        // company fit to try. When an attacker has nobody left fit to attack, his attack has run out of steam.
        bn.Busy = bn.Tasks.Values.Any(t => t.Kind is BattalionTaskKind.Attack or BattalionTaskKind.Counterattack)
            || bn.Posture == Posture.Attack && (AnyFresh(bn, reserve: false) || AnyFresh(bn, reserve: true));
        bn.Intent = Describe(bn);
    }

    // ---------------------------------------------------------------- the start

    /// <summary>His first look: the reserve stays back, and a company standing on a key point holds it. A defender
    /// sends each of those a "hold" order, so its commander makes a defence plan.</summary>
    private void Start(Battalion bn)
    {
        bn.Started = true;
        foreach (var p in bn.Points) bn.Held[p] = null;
        bn.Held[bn.OwnHq] = bn.Side;
        foreach (var c in bn.Companies)
        {
            if (bn.Tasks.ContainsKey(c)) continue; // the scenario put it in reserve
            var p = bn.Points.Where(k => !k.IsHq && Vector2.Distance(k.Position, c.Centroid) <= k.Radius + 80f)
                .OrderBy(k => Vector2.Distance(k.Position, c.Centroid)).FirstOrDefault();
            bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Hold, Point = p, Issued = Time };
            if (p == null) continue;
            bn.Held[p] = bn.Side;
            if (bn.Posture == Posture.Defend)
                _b.SendOrder(c, new Order { Kind = OrderKind.Hold, Objective = p.Position, Radius = MathF.Min(p.Radius, 35f) });
        }
        Say(bn, bn.Posture == Posture.Attack
            ? $"Orders received: take {string.Join(", then ", AttackOrder(bn).Select(p => p.Name))}, then the enemy HQ."
            : $"Orders received: hold {string.Join(" and ", bn.Points.Where(p => bn.Held[p] == bn.Side && !p.IsHq).Select(p => p.Name))} and protect HQ.");
    }

    // ---------------------------------------------------------------- what the companies have told him

    /// <summary>Taken, failed, refused, or simply taking too long: the news from each company changes its task.</summary>
    private void CheckTasks(Battalion bn)
    {
        foreach (var c in bn.Companies)
        {
            if (!bn.Tasks.TryGetValue(c, out var t)) continue;
            if (c.CommanderDown && t.Kind != BattalionTaskKind.Hold)
            {
                bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Hold, Issued = Time };
                Say(bn, $"No word from {c.Name}'s commander: it'll have to carry on as best it can.");
                continue;
            }
            if (t.Kind is not (BattalionTaskKind.Attack or BattalionTaskKind.Counterattack) || t.Point is not { } p) continue;
            var news = bn.NewsOf(c);
            if (news.TakenAt >= t.Issued)
            {
                bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Hold, Point = p, Issued = Time };
                bn.Held[p] = bn.Side;
                bn.FailedAt.Remove(p);
                Say(bn, p.IsHq ? $"{c.Name} has reached the enemy HQ." : $"{c.Name} has taken {p.Name}. It holds there.");
            }
            else if (news.FailedAt >= t.Issued || Time - t.Issued > R.AttackTimeLimit)
            {
                bn.FailedAt[p] = Time;
                bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Hold, Issued = Time };
                Say(bn, $"{c.Name}'s attack on {p.Name} has {(news.FailedAt >= t.Issued ? "failed" : "got nowhere")}. It holds where it is.");
            }
            else if (news.RefusedAt >= t.Issued && news.RefusedAt >= news.PlanAt)
            {
                if (t.RefusedAt < 0f)
                {
                    t.RefusedAt = Time;
                    bool fired = FireOn(bn, p.Position, $"{p.Name}, for {c.Name}", force: true);
                    Say(bn, $"{c.Name} says the attack on {p.Name} is a death trap.{(fired ? " Calling the artillery on it." : "")}");
                }
                else if (Time - t.RefusedAt > R.RefusalWait)
                {
                    if (!t.Insisted && bn.Traits.Aggression >= 3 && bn.StrengthOf(c) >= R.FreshStrength)
                    {
                        // Send the same order again: the company commander obeys an order he is given twice.
                        _b.SendOrder(c, new Order { Kind = OrderKind.Attack, Objective = p.Position, Radius = AttackRadius(p) });
                        bn.Tasks[c] = new BattalionTask { Kind = t.Kind, Point = p, Issued = Time, Insisted = true };
                        Say(bn, $"Insists: {c.Name} is to attack {p.Name}.");
                    }
                    else
                    {
                        bn.FailedAt[p] = Time;
                        bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Hold, Issued = Time };
                        Say(bn, $"Calls off {c.Name}'s attack on {p.Name}.");
                    }
                }
            }
        }
    }

    /// <summary>Who he believes holds each key point: his own company holding it, or the enemy if any are known
    /// there and none of his are.</summary>
    private void UpdateHeld(Battalion bn)
    {
        foreach (var p in bn.Points)
        {
            if (p.IsHq) continue;
            var before = bn.Held[p];
            bool ours = bn.Tasks.Any(kv => kv.Value.Kind == BattalionTaskKind.Hold && kv.Value.Point == p && !kv.Key.CommanderDown
                && bn.StrengthOf(kv.Key) >= R.SpentStrength);
            int enemy = EnemyNear(bn, p.Position, p.Radius + R.KeyPointRange);
            if (ours) bn.Held[p] = bn.Side;
            else if (enemy > 0) bn.Held[p] = Battle.Enemy(bn.Side);
            if (before == bn.Side && bn.Held[p] != bn.Side)
            {
                Say(bn, $"{Cap(p.Name)} has fallen.");
                // The company that held it is no longer holding anything.
                foreach (var kv in bn.Tasks.ToList())
                    if (kv.Value.Point == p && kv.Value.Kind == BattalionTaskKind.Hold)
                        bn.Tasks[kv.Key] = new BattalionTask { Kind = BattalionTaskKind.Hold, Issued = Time };
            }
        }
    }

    // ---------------------------------------------------------------- attacking

    private IEnumerable<KeyPoint> AttackOrder(Battalion bn) =>
        bn.Points.Where(p => !p.IsHq).OrderBy(p => Vector2.Distance(p.Position, bn.OwnHq.Position));

    private void Attack(Battalion bn)
    {
        var targets = AttackOrder(bn).Where(p => bn.Held[p] != bn.Side).ToList();
        bool finalAssault = targets.Count == 0;
        if (finalAssault) targets.Add(bn.EnemyHq);
        int maxAttacks = finalAssault ? 3 : bn.Traits.Aggression >= 3 ? 2 : 1;
        int attacking = bn.Tasks.Values.Count(t => t.Kind == BattalionTaskKind.Attack);
        foreach (var p in targets)
        {
            if (attacking >= maxAttacks) break;
            bool underAttack = bn.Tasks.Values.Any(t => t.Kind == BattalionTaskKind.Attack && t.Point == p);
            if (underAttack && !finalAssault) continue;
            if (bn.FailedAt.TryGetValue(p, out float failed) && Time - failed < R.RetryDelay) continue;
            // The reserve goes in where an attack has already failed, or when nobody else is fit to.
            var c = Pick(bn, p, reserve: false) ?? (bn.FailedAt.ContainsKey(p) || finalAssault || !AnyFresh(bn, reserve: false) ? Pick(bn, p, reserve: true) : null);
            if (c == null) continue;
            bool wasReserve = bn.Tasks.TryGetValue(c, out var old) && old.Kind == BattalionTaskKind.Reserve;
            _b.SendOrder(c, new Order { Kind = OrderKind.Attack, Objective = p.Position, Radius = AttackRadius(p) });
            bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Attack, Point = p, Issued = Time };
            attacking++;
            bool fired = EnemyNear(bn, p.Position, p.Radius + R.KeyPointRange) > 0 && FireOn(bn, p.Position, $"{p.Name}, ahead of {c.Name}'s attack");
            Say(bn, $"Orders {c.Name}{(wasReserve ? ", his reserve," : "")} to attack {(p.IsHq ? "the enemy HQ" : p.Name)}.{(fired ? " Artillery on it first." : "")}");
        }
    }

    private static float AttackRadius(KeyPoint p) => MathF.Min(p.Radius * 0.6f, p.IsHq ? 20f : 30f);

    /// <summary>The fittest company free for a new job (not already attacking, not holding a key point the enemy
    /// is near), nearest the target.</summary>
    private Company? Pick(Battalion bn, KeyPoint target, bool reserve)
    {
        Company? best = null;
        float bestScore = float.MaxValue;
        foreach (var c in bn.Companies)
        {
            if (!Free(bn, c, reserve)) continue;
            float score = Vector2.Distance(c.Centroid, target.Position) - 300f * bn.StrengthOf(c);
            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    private bool Free(Battalion bn, Company c, bool reserve)
    {
        if (c.CommanderDown || c.HqGroup.CountEffective == 0) return false;
        if (bn.StrengthOf(c) < R.FreshStrength) return false;
        if (!bn.Tasks.TryGetValue(c, out var t)) return !reserve;
        if (t.Kind == BattalionTaskKind.Reserve) return reserve;
        if (reserve) return false;
        if (t.Kind != BattalionTaskKind.Hold) return false;
        // Holding a key point the enemy is near (or his own HQ): it stays.
        if (t.Point is { } p && (p == bn.OwnHq || EnemyNear(bn, p.Position, p.Radius + 150f) > 0)) return false;
        return true;
    }

    private bool AnyFresh(Battalion bn, bool reserve) => bn.Companies.Any(c => Free(bn, c, reserve));

    // ---------------------------------------------------------------- defending

    private void Defend(Battalion bn)
    {
        // The enemy close to HQ: the reserve comes back to guard it (a cautious man does so at the first sight of them).
        int nearHq = EnemyNear(bn, bn.OwnHq.Position, R.HqThreatRange);
        bool guarded = bn.Tasks.Values.Any(t => t.Point == bn.OwnHq);
        if (!guarded && nearHq >= (bn.Traits.Caution >= 4 ? 1 : 3))
        {
            var guard = Pick(bn, bn.OwnHq, reserve: true) ?? Pick(bn, bn.OwnHq, reserve: false);
            if (guard != null)
            {
                _b.SendOrder(guard, new Order { Kind = OrderKind.Hold, Objective = bn.OwnHq.Position, Radius = 30f });
                bn.Tasks[guard] = new BattalionTask { Kind = BattalionTaskKind.Hold, Point = bn.OwnHq, Issued = Time };
                Say(bn, $"Enemy close to HQ ({nearHq} seen): {guard.Name} is to guard it.");
                return;
            }
        }
        // A key point has fallen: retake it with the reserve if the enemy there aren't too many for it.
        if (bn.Tasks.Values.Any(t => t.Kind == BattalionTaskKind.Counterattack) && bn.Traits.Aggression < 4) return;
        foreach (var p in bn.Points.Where(k => !k.IsHq && bn.Held[k] == Battle.Enemy(bn.Side)).OrderBy(k => Vector2.Distance(k.Position, bn.OwnHq.Position)))
        {
            if (bn.Tasks.Values.Any(t => t.Kind == BattalionTaskKind.Counterattack && t.Point == p)) continue;
            if (bn.FailedAt.TryGetValue(p, out float failed) && Time - failed < R.RetryDelay) continue;
            var c = Pick(bn, p, reserve: true) ?? (bn.Traits.Aggression >= 4 ? Pick(bn, p, reserve: false) : null);
            if (c == null) return;
            int enemy = EnemyNear(bn, p.Position, p.Radius + R.KeyPointRange);
            // A bold man counterattacks against odds; a careful one only when he has the men for it.
            float odds = 0.7f + 0.2f * (bn.Traits.Aggression - 3) - 0.1f * (bn.Traits.Caution - 3);
            if (enemy > bn.NewsOf(c).Strength * odds) continue;
            _b.SendOrder(c, new Order { Kind = OrderKind.Attack, Objective = p.Position, Radius = AttackRadius(p) });
            bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Counterattack, Point = p, Issued = Time };
            bool fired = FireOn(bn, p.Position, $"{p.Name}, before {c.Name}'s counterattack");
            Say(bn, $"Orders {c.Name} to counterattack {p.Name} ({enemy} enemy known there).{(fired ? " Artillery on it first." : "")}");
            return;
        }
    }

    // ---------------------------------------------------------------- artillery

    /// <summary>Enemy he knows are bunched together, away from his own men: worth a fire mission.</summary>
    private void Artillery(Battalion bn)
    {
        if (Time - bn.LastArtillery < R.ArtilleryCooldown || _b.ArtilleryFor(bn.Side, FireKind.He) == null) return;
        var recent = Recent(bn).ToList();
        int best = 0;
        Vector2 at = default;
        foreach (var k in recent)
        {
            int n = 0;
            Vector2 sum = Vector2.Zero;
            foreach (var o in recent)
                if (Vector2.DistanceSquared(o.LastPosition, k.LastPosition) <= 40f * 40f) { n++; sum += o.LastPosition; }
            if (n > best) { best = n; at = sum / n; }
        }
        if (best < R.ArtilleryMinTargets) return;
        string where = bn.Points.OrderBy(p => Vector2.Distance(p.Position, at)).Select(p => Vector2.Distance(p.Position, at) < p.Radius + 120f ? $" near {(p.IsHq ? (p == bn.OwnHq ? "HQ" : "the enemy HQ") : p.Name)}" : "").FirstOrDefault() ?? "";
        if (FireOn(bn, at, $"{best} enemy seen together{where}"))
            Say(bn, $"Artillery on {best} enemy seen together{where}.");
    }

    /// <summary>Call the artillery on a place, unless his own men are too close or the guns fired too recently.</summary>
    private bool FireOn(Battalion bn, Vector2 at, string reason, bool force = false)
    {
        if (!force && Time - bn.LastArtillery < R.ArtilleryCooldown) return false;
        if (_b.ArtilleryFor(bn.Side, FireKind.He) == null) return false;
        // His own men are always on his map (own troops are never hidden), so he knows when it's too close.
        foreach (var s in _b.Hash.Near(bn.Side, at, R.ArtilleryDangerClose))
            if (s.IsActive && Vector2.Distance(s.Position, at) < R.ArtilleryDangerClose) return false;
        if (_b.CallArtillery(bn.Side, at, FireKind.He, _b.Rules.Support.ArtilleryRounds, reason) == null) return false;
        bn.LastArtillery = Time;
        return true;
    }

    // ---------------------------------------------------------------- what he knows

    private IEnumerable<Contact> Recent(Battalion bn)
    {
        var hq = _b.Hq(bn.Side)!;
        foreach (var k in hq.Knowledge.Contacts)
            if (Time - k.LastSeen <= R.ContactAge) yield return k;
    }

    private int EnemyNear(Battalion bn, Vector2 at, float radius)
    {
        int n = 0;
        foreach (var k in Recent(bn))
            if (Vector2.DistanceSquared(k.LastPosition, at) <= radius * radius) n++;
        return n;
    }

    private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    private void Say(Battalion bn, string text) => _b.Log.Add(Time, -1, $"{bn.Commander}: {text}", bn.Side, LogKind.Key);

    /// <summary>His intentions in a line, for the game's battalion panel.</summary>
    private static string Describe(Battalion bn)
    {
        var parts = new List<string>();
        foreach (var kind in new[] { BattalionTaskKind.Attack, BattalionTaskKind.Counterattack, BattalionTaskKind.Hold, BattalionTaskKind.Reserve })
        {
            var cos = bn.Tasks.Where(kv => kv.Value.Kind == kind).ToList();
            if (cos.Count == 0) continue;
            string What(KeyValuePair<Company, BattalionTask> kv) => kv.Value.Point is { } p
                ? $"{kv.Key.Name} ({(p.IsHq ? (p == bn.OwnHq ? "HQ" : "the enemy HQ") : p.Name)})" : kv.Key.Name;
            string verb = kind switch
            {
                BattalionTaskKind.Attack => "attacking",
                BattalionTaskKind.Counterattack => "counterattacking",
                BattalionTaskKind.Hold => "holding",
                _ => "in reserve",
            };
            parts.Add($"{verb}: {string.Join(", ", cos.Select(What))}");
        }
        return string.Join("; ", parts);
    }
}
