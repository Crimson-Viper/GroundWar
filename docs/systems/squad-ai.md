# Squad AI

## What it is
The squad leader's brain. Every ~1.5 s (`squad.thinkInterval`) he looks at what his squad knows, picks a
**tactic** for the squad's **order**, and gives each of his two teams a place to be and somewhere to shoot.
The soldiers then carry that out while trying to stay alive ([Soldier AI](soldier-ai.md)).
Code: `sim/GroundWar.Sim/AI/SquadBrain.cs`; squads and teams in `Units/Group.cs`.

## Why
The Build Plan's squad AI is a flowchart per tactic plus a picker. Splitting the work this way means the squad leader
only decides *where* teams go and *what* they shoot at; each man still decides for himself how to get there and
whether to duck. That keeps tactics readable and gives the "messy but believable" look we want.

## Teams
Each squad splits into a **gun team** (the light machine gun and the men around it) and a **rifle team**. Larger
WW2 squads worked this way: the gun team pins the enemy, the rifle team moves.

## Sergeants
Each squad has a sergeant (yellow cap). If he falls, the next man takes over (`Battle.MakeLeader`), but the squad
takes a morale blow (see [Morale and suppression](morale-and-suppression.md)).

## Orders (from the scenario for now; from the company commander from milestone 5)
| Order | Meaning |
|---|---|
| **Hold** | Defend the objective area |
| **Advance** | Go to the objective, fight on the way if needed, then hold it |
| **Attack** | Take the objective from the enemy: fix them with fire, go round, assault |
| **Suppress** | Go to a support position and keep the enemy on the objective pinned with fire |

## Tactics
| Tactic | What the teams do | Used when |
|---|---|---|
| **Travel** | Both teams walk in 40 m legs, side by side | No enemy known and the goal is far |
| **Bounding overwatch** | One team moves 30 m while the other watches, then they swap | Near the goal or enemy, or after contact |
| **Fire and movement** | Like bounding, but 22 m rushes at a run, with the covering team firing at the enemy | Enemy close (under 150 m) |
| **Flank** | Gun team fires to pin the enemy; rifle team swings out 45 m to the side, then round to 50 m off the enemy's flank | Attacking, within 140 m of the enemy, and a covered route exists |
| **Assault** | Rifle team rushes the enemy position; gun team lifts its fire when they get within 25 m | Within 60 m and the enemy has gone quiet (or within 30–35 m regardless) |
| **Suppress** | Everyone fires on the enemy position from the support position | Suppress order |
| **Defend** | Teams spread into the best cover in the area, fire at what they see, and dig in when quiet | Hold order, or after taking an objective |
| **Fall back** | Bounds back to the rear in 40 m steps | Holding, and fewer than 40% of the men can still fight |

### How an attack goes (`Attack`)
1. No enemy known: move towards the objective (travel, then bounding).
2. Enemy known but over 140 m away: close in by bounding overwatch.
3. Within 140 m: try to **plan a flank** (`PlanFlank`). Both sides are tried; each route is scored for cover along it
   and how much of it the enemy can see. If the gun team and rifle team are both strong enough and a route is
   good, flank. Otherwise, fire and movement straight at them.
4. Once close and the enemy is suppressed: **assault**.
5. On the objective with no enemy left near it: "objective taken", switch to Hold.
6. If fewer than 40% of the men can still fight (and they're not mid-assault), the attack is called off; the squad
   holds where it is. This counts as a failed attack for [Who wins](battle-results.md).

**The squad's nerve comes first.** Before any of the above, the squad leader looks at his squad's cohesion (see
[Morale and suppression](morale-and-suppression.md#squad-cohesion-the-squads-nerve-as-a-unit)). A **shaken** squad
on the attack goes to ground and shoots back until it steadies. A **wavering** one gives up the attack and pulls
back about 60 m. A **broken** one is running for its rally point and takes no orders until it rallies. A
defending squad that is wavering falls back to the next position if the enemy is close, on its flank or behind it.
In practice a squad stops being able to attack at about 30% losses, well before the 40% rule in step 6.

If the flank's rifle team is pinned for 20 s (`squad.pinnedAbortTime`), the flank is abandoned.

### Sharing what they see
Squads within 80 m of each other (`squad.shareRadius`) share sightings each time they think. See [Fog of war](fog-of-war.md).

### Using the ground
Each bound ends at the best spot about one step ahead (`NextBound`): cover counts most, then progress. Since the
ground was added, a spot the nearest known enemy **can't see** (behind a crest, a wood, a hedgerow) gets a bonus
(`squad.deadGroundBonus`), so squads work forward through dead ground when there is some. See [The ground](terrain.md).

## Tuning
`data/rules.json` → `squad`: every distance and time above (travel step, bound step, rush step, plan range, assault
range, flank offsets, fall-back strength, share radius, etc.), each with a comment.

## Seeing it in the game
The top-left panel shows each squad's tactic and status ("Flanking: rifle team working round the enemy's side").
**P** draws the plans: orange for where the gun team is going, cyan for the rifle team, purple for the flank route.
The log records each change of tactic.

## Tests
`CohesionTests.cs` (the squad's nerve), `SquadTests.cs` (squads split into gun and rifle teams; an attacking squad flanks and two squads can take the
position; a suppressing squad goes to its support position), `BattleTests.cs` (a fallen sergeant is replaced). The
headless runner's results tables are the main check on how tactics play out (see [Testing and tuning](../tools/testing.md)).
