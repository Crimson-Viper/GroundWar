# Testing and tuning

How the game is checked between playtests, and how to try changes.

## Automated tests
`sim/GroundWar.Sim.Tests/` (xUnit). Run with:

```
dotnet test sim/GroundWar.Sim.Tests
```

| File | Checks |
|---|---|
| `MapTests.cs` | Sight lines, wall and crater protection, cover point generation, path finding, the grid |
| `BattleTests.cs` | Same seed = same battle, battles end on time, cover use, suppression fading, broken men flee, sergeants replaced |
| `SquadTests.cs` | Teams, sight lines, each squad's own picture of the enemy, flanking, suppress order, digging in |
| `FairnessTests.cs` | Every meeting map is the same from both ends |
| `TankTests.cs` | Armour, anti-tank weapons, the tank driving and fighting |
| `CommsTests.cs` | Order delay by phone and runner, cut lines, flares, reports reaching HQ |
| `CompanyTests.cs` | The company commander: brief-back, choosing the covered flank, approve and reject, refusing and being overruled, traits, company messages |

GitHub runs these on every pull request (`.github/workflows/ci.yml`), plus 10 headless battles and a build of the
Godot code. A pull request with a red cross has a failing check.

## The headless battle runner
`sim/GroundWar.Runner`. Runs battles with no screen, thousands of times faster than real time, and prints a table:
winner, time, losses, how many broke, shots, hits and time in cover for each battle, then a summary.

```
dotnet run -c Release --project sim/GroundWar.Runner -- --battles 200                                   # firefight, 200 seeds
dotnet run -c Release --project sim/GroundWar.Runner -- --scenario scenarios/town_attack.json --battles 200
dotnet run -c Release --project sim/GroundWar.Runner -- --battles 1 --seed 4 --log                      # every decision in one battle
dotnet run -c Release --project sim/GroundWar.Runner -- --scenario scenarios/meeting.json --map maps/meeting_field_c.json --battles 300
dotnet run -c Release --project sim/GroundWar.Runner -- --scenario scenarios/stress_1000.json --battles 1 --profile   # timing
```

| Option | Does |
|---|---|
| `--scenario <path>` | Which scenario (relative to `data/`); default the firefight |
| `--battles <n>` | How many battles, default 20 (seeds 1..n, or counting up from `--seed`) |
| `--seed <n>` | First seed |
| `--log` | Print every log line (decisions, thoughts, casualties) of the first battle |
| `--map <path>` | Fight the scenario on a different map |
| `--profile` | Print where the time goes each tick |

**Current results** (200 battles each, 1 Nov 2026, after foxholes):

| Scenario | Blue | Red | Draws |
|---|---|---|---|
| Squad attack | 127 | 73 | 0 |
| Firefight at the wall | 31 | 169 | 0 |
| Meeting engagement | 94 | 95 | 11 |
| Town attack | 27 | 153 | 20 |
| Tank support | 171 | 29 | 0 |
| Orders under fire | 117 | 83 | 0 |

## Screenshot mode
The game can render a frame with no window and save it, which is how screenshots for pull requests are made:

```
godot --path . --rendering-driver vulkan -- --screenshot=out.png --scenario=0 --seconds=120 \
      --camera=210,100,30,-60,-40 --overlays=gclp --select=12 --seed=1
```

`--camera=x,y,distance,yaw,pitch`; `--overlays=` letters are the overlay keys (g grid, c cover, l lines, p plans, t thoughts,
b bars, f = fog off); `--select=` a soldier id. It prints draw calls, node count and soldier-update time.
On a headless Linux machine wrap it in `xvfb-run`.

## Scenarios
`data/scenarios/*.json`: map, time limit, and for each side its squads, with order, objective, starting area,
facing and weapons. Tab in the game cycles through them in the order listed in `game/Main.cs`.

| Scenario | Purpose |
|---|---|
| `squad_attack` | Two Blue squads (one suppressing, one attacking) against a Red squad and a hidden MG post |
| `firefight` | One Blue squad attacks alone; should usually fail |
| `meeting` | Two equal squads advance into each other on a fair map; should be about even |
| `town_attack` | Street fighting in dense cover; the defender usually holds |
| `tank_support` | The firefight with a Blue tank; Red has an anti-tank rifle and bazookas |
| `orders_under_fire` | The squad attack with an HQ: orders by phone and runner, late reports |
| `stress_1000` | 1,000 a side, to check speed |

## Maps
Hand-written JSON (see [Map and cover](../systems/map-and-cover.md)). `tools/make_town_map.py` generates `town.json`
from a fixed seed (`python3 tools/make_town_map.py`).

## Tuning workflow
1. Change a number in `data/*.json`.
2. Press R in the game to watch, or run 200 battles in the runner to see what it does to results.
3. If a change is kept, update the results table above and the relevant system page.
