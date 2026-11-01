# Who wins

## What it is
How a battle ends and which side gets the win. Code: `Battle.CheckEnd`, `Battle.Holder`, `QuietResult`,
`TimeLimitResult` in `sim/GroundWar.Sim/Battle.cs`.

**Note:** the design says the full game is won by destroying the enemy HQ (milestone 7). Until then, these
prototype battles are decided by the objective.

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
3. **Both sides were attacking** (a meeting engagement) and time runs out: the side with at least 20 percentage points
   more of its men still fighting wins ("came off better"). Otherwise it's a draw.

## Why
Early versions gave lots of draws in meeting battles and sometimes awarded wins to a side that had been driven off.
These rules match what a commander would call a win: you hold the ground you were sent for, or the enemy can't fight.

## Seeing it in the game
A banner at the top shows the result and reason ("Time up: Red held on"). R replays, N starts a new battle.

## Tests
`BattleTests.cs` (battles end within the time limit; most end in a break, not a timeout). The runner's results
table shows each battle's winner and reason.
