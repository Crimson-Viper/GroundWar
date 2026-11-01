# Who wins

## What it is
How a battle ends and which side gets the win. Code: `Battle.CheckEnd`, `Battle.Holder`, `QuietResult`,
`TimeLimitResult` in `sim/GroundWar.Sim/Battle.cs`.

The design says the full game is won by destroying the enemy HQ. The battalion battle (milestone 7, part 2) is
won that way: see **Battalion battles** below. The smaller scenarios are still decided by the objective.

## When a battle ends
Checked every tick:
1. **A side can no longer fight**: every man is down, broken or fled. The other side wins. Both at once is a draw.
2. **The fighting has died down**: nobody is still attacking and no shot has been fired for 60 s (`squad.quietEndTime`).
   It doesn't end this way while a company commander is still briefing back or running a plan, or orders are on their way.
3. **Time is up**: the scenario's `timeLimit` (usually 10–15 minutes).

## Who gets the win (cases 2 and 3)
In this order:
1. **A side holds an objective it took.** "Holds" means more of its fighting men within the objective area (plus 10 m)
   than the other side has. A squad "took" it when it got there with no enemy left near it (Attack), or got there
   first (Advance). Getting to a waypoint (a start line or support position a company commander sent it to) doesn't count.
2. **Only one side was attacking** (was ever ordered to attack, advance or suppress): if the attack was called off (too many casualties), the defender wins. When time
   runs out, the defender wins ("held on").
   Also (since milestone 8): if a company commander called the attack a death trap and was never sent in again, the
   fighting dies down with the defender still there, so the defender wins ("Blue's attack stalled (called a death
   trap)"). Before, these battles were draws. In the game you can always send the order again (or give the company
   support) before the fighting dies down.
3. **Both sides were attacking** (a meeting engagement) and time runs out: the side with at least 20 percentage points
   more of its men still fighting wins ("came off better"). Otherwise it's a draw.

## Battalion battles (`"victory": "Hq"`)
1. **An HQ is overrun**: enemy men within 30 m of it and none of its own, for 15 s. The side that overran it wins
   at once. See [The battalion battle](battalion-commander.md).
2. **A side can no longer fight**, as above.
3. **The fighting has died down** (as above; it also doesn't end while a battalion commander is pressing an attack,
   or is an attacker with places still to take and a company fit to try) **or time is up**: with both HQs standing,
   the defender has held on and wins. If both battalions were attacking, the side left clearly stronger wins, as above.

Code: `UpdateHqs` and `NoHqResult` in `sim/GroundWar.Sim/Combat/Battle.Battalion.cs`.

## Why
Early versions gave lots of draws in meeting battles and sometimes awarded wins to a side that had been driven off.
These rules match what a commander would call a win: you hold the ground you were sent for, or the enemy can't fight.

## Seeing it in the game
A banner at the top shows the result and reason ("Time up: Red held on"). R replays, N starts a new battle.

## Tests
`BattleTests.cs` (battles end within the time limit; most end in a break, not a timeout). `BattalionTests.cs`
(overrunning an HQ wins; a defended HQ isn't overrun; older scenarios aren't won that way). The runner's results
table shows each battle's winner and reason.
