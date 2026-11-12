# Soldier AI

## What it is
Each soldier decides for himself what to do next: run for cover, shoot, go where his squad leader wants him,
hug the dirt, dig in, or flee. Code: `sim/GroundWar.Sim/AI/SoldierBrain.cs`.

## Why this way
The Build Plan calls for **utility scoring**: every option gets a score from a few simple formulas, and the highest
wins. It's cheap enough for 2,000 soldiers, easy to tune, and every decision can be explained in one line (the
"thought" you see when you click a soldier).

## How it works
A man thinks every 0.4 s while in a fight and about every 1 s when nothing is going on (`ai.thinkInterval`).
Each time:

1. **Broken?** He runs for the rear, and nothing else matters. See [Morale and suppression](morale-and-suppression.md).
2. **Pick a target** (`PickTarget`): among enemies he can see and reach, score = how dangerous the enemy is (his weapon's
   `threat`, ×1.5 for a sergeant, ×1.3 if he's firing right now, halved if he's running away) × chance to hit him ×
   a bonus for being close. He sticks with his current target (×1.3), and never fires where a friend is within 4 m of the enemy.
3. **Score the options** (the man's current action gets +10, `ai.stickinessBonus`, so he doesn't flicker between choices):

| Option | Score | When |
|---|---|---|
| Crawl to cover | 97 | Pinned in the open and there's much better cover within 8 m |
| Hug the dirt | 95 | Suppression above 80: too frightened to do anything else |
| Assault (follow orders) | 88 | His squad is assaulting |
| Follow orders (move) | 66–72 | His squad wants him somewhere else |
| Run for cover | 60–115 | Cover nearby is clearly better than where he is, more so under fire |
| Fire at an enemy | 55–70 | He can see one, has ammo and isn't pinned; more if he's already in cover |
| Covering fire | 50–68 | No enemy in sight, but his squad wants fire on a spot where one was seen |
| Dig a foxhole | 40 | His squad is holding, he's in place, nothing is happening (see [Map and cover](map-and-cover.md)) |
| Idle | 10 | Nothing better: stay put, face the threat, watch |

4. **Do it**: set a path, stance, facing, target, and write a one-line thought ("Running for cover: low wall 6 m away").

### Choosing cover
`FindCover` scores free cover points nearby by how well they protect from the threats he knows about, minus
distance. Spots he can fire from get a bonus; hiding spots he can't fire from get a penalty unless he's frightened;
and he won't run towards the enemy to hide. Holding men search within their
squad's area (plus 1 m, so they don't flip between two spots); men under fire on the move search around themselves.

### Stance
Standing to move, crouched behind walls, prone in craters and the open when under fire. Stance changes how easy he
is to hit (see [Spotting and shooting](spotting-and-shooting.md)). In standing crops he fights from one knee,
because a man flat in the wheat can't see (see [The ground](terrain.md)).

## Tuning
`data/rules.json` → `ai`: think interval, cover search radii, stickiness, how long "under fire" lasts, dig time.
The option scores themselves are in `SoldierBrain.Think`.

## Seeing it in the game
Click a soldier: the right panel shows his action, thought, cover, ammo and target. **T** shows every soldier's
thought above his head. **L** shows aim and path lines. The log (bottom left) shows the selected soldier's changes of mind.

## Tests
`SquadTests.cs`, `BattleTests.cs` (soldiers use cover under fire, broken men run for the rear, suppression fades).
