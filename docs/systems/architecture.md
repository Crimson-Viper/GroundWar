# Architecture

## What it is
The game is two separate programs that share data:

- **The simulation** (`sim/GroundWar.Sim`): the whole battle. Soldiers, squads, bullets, morale and sight lines,
  in plain C# with no Godot code at all.
- **The Godot game** (`game/`): draws what the simulation says is happening, and handles the camera, mouse and keys.

## Why
- The battle can run without a screen, thousands of times faster than real time. That is how the AI gets tested
  between playtests (see [Testing and tuning](../tools/testing.md)).
- If we move to Unreal later, only the drawing side has to be rewritten.

## How a battle runs
The simulation moves in fixed steps ("ticks") of 0.1 seconds. Each tick, in this order (`Battle.Step` in `sim/GroundWar.Sim/Battle.cs`):

1. **Squad leaders think** (every ~1.5 s each): pick a tactic, give each team a place to go or hold. See [Squad AI](squad-ai.md).
2. **Soldiers look around** (every 0.5 s): spot enemies. See [Spotting and shooting](spotting-and-shooting.md).
3. **Soldiers think** (every 0.4 s in a fight, every ~1 s when quiet): pick an action. See [Soldier AI](soldier-ai.md).
4. **Weapons fire**, and men who are digging carry on digging.
5. **Everyone moves** along their paths.
6. **Morale** updates. See [Morale and suppression](morale-and-suppression.md).
7. Squads update their counts. **Messages** move: runners run, calls arrive, reports are written (see [Messages and delay](messages-and-delay.md)).
   Once a second the battlefield grid and old sightings are refreshed.
8. **Check for the end** of the battle. See [Who wins](battle-results.md).

Godot runs at the screen's frame rate and asks the simulation to step as many ticks as the game speed needs,
then draws soldiers part-way between the last two ticks so movement looks smooth.

## Data files
Everything that is a number to tune lives in `data/`, as JSON with comments:

| File | Holds |
|---|---|
| `data/rules.json` | Spotting, cover, morale, suppression, soldier and squad AI numbers |
| `data/weapons.json` | Each weapon's range, accuracy, rate of fire, magazine, how frightening it is |
| `data/maps/*.json` | Walls (with thickness and low/high) and craters, in metres |
| `data/scenarios/*.json` | Which squads fight, where they start, their weapons and orders |

Edit a file, save, press **R** in the game, and the battle restarts with the new numbers.
Loading code: `sim/GroundWar.Sim/Data/` (`GameData`, `Rules`, `WeaponDef`, `ScenarioDef`).

## Determinism
All randomness comes from one seeded random-number generator (`Core/Rng.cs`, SplitMix64). The same seed and the
same data give exactly the same battle, every time. This lets a test or a bug report point at "seed 42" and get the
same fight. **N** in the game picks a new seed; **R** replays the current one.

## Where things live

| Folder | What |
|---|---|
| `sim/GroundWar.Sim/AI/` | Soldier and squad brains |
| `sim/GroundWar.Sim/Combat/` | `Battle` partial classes: shooting, morale, movement, perception, tanks, comms |
| `sim/GroundWar.Sim/Comms/` | Messages, orders, phone lines, HQ |
| `sim/GroundWar.Sim/Map/` | Map, cover points, grid, path finding |
| `sim/GroundWar.Sim/Units/` | Soldier, group (squad), team, knowledge |
| `sim/GroundWar.Sim/Data/` | Loading the JSON files |
| `sim/GroundWar.Sim.Tests/` | Automated tests |
| `sim/GroundWar.Runner/` | Headless battle runner |
| `game/` | Godot scripts (drawing, camera, HUD) |
