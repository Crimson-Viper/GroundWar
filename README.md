# Ground War

An RTS where you never control soldiers directly. You give orders; AI commanders plan, and AI soldiers fight.
This repository is the **prototype**: grey boxes and simple shapes until the AI is fun to watch.

Design docs: [Design document](https://claude.ai/artifact/Q1UHZQmTfqk9WJ5CzV6YGs) ·
[How the AI works](https://claude.ai/artifact/HHYXkduRrMJMCjKFVfnkoz) ·
[Build plan](https://claude.ai/artifact/RZdiTzT1fSPFsojRV7ECAe)

## Where we are

| Milestone | State |
|---|---|
| 1. Test range: map, battlefield grid, cover points, free camera, overlays | done |
| 2. Soldiers that fight: take cover, shoot, get suppressed, break and run | done |
| 3. Squads with tactics and fog of war | done |
| 4. Messages and delay | next |

![Two squads taking the wall](docs/screenshots/m3-squad-attack.png)

## One-time setup

1. **Godot 4.7, .NET edition.** From [godotengine.org/download](https://godotengine.org/download), pick the version labelled **.NET** (it runs C#). Unzip it anywhere.
2. **.NET SDK 8** (or newer) from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).
3. **Clone this repository** with GitHub Desktop (File → Clone repository → `GroundWar`).

## Play it

1. Start Godot. In the Project Manager click **Import**, browse to the cloned folder and pick `project.godot`, then **Import & Edit**.
2. Press **F5** (or the ▶ button top right). The first time, Godot builds the code, which takes a few seconds.
3. Watch the battle. **Tab** switches between the scenarios:
   - **Squad attack** (starts first): Blue 1st Squad sets up in the middle and pins Red down while Blue 2nd Squad closes in, then its gun team fires and its rifle team goes round the side and assaults. Red has a hidden machine-gun post in the north-east ruin.
   - **Firefight at the wall**: one Blue squad attacks alone. It usually fails, which is the point: compare it with the two-squad attack.
   - **Meeting engagement**: two equal squads advance into the middle. The map is the same from both ends, so each side wins about as often as the other.
   - **Town attack**: street fighting. Two Blue squads fight through houses, yards and rubble to take a crossroads held by a Red squad, with a Red machine gun further down the main street. Lots of cover, short ranges; the defender usually holds.
   - **Stress test**: 1,000 soldiers a side on a big field, to check the game stays smooth at battalion size. Zoom out to see it all.

To get a new version: in GitHub Desktop press **Fetch origin**, then **Pull**, then press F5 in Godot again.
To try a pull request before it is merged: in GitHub Desktop pick its branch from **Current branch**.

### Controls

| | |
|---|---|
| **Camera** | WASD or arrows pan · Q / E rotate · mouse wheel or + / − zoom · right-drag orbit · middle-drag pan · Shift faster |
| **Battle** | Space pause · `.` step one tick · 1–6 speed (¼× to 8×) · R restart · N new battle (different dice) · Tab next scenario |
| **Soldier** | Left-click a soldier to see what he's thinking · F follow him · Esc deselect |
| **Overlays** | G battlefield grid (cover → visibility → control → off) · C cover points · L aim and path lines · P squad plans · T every soldier's thoughts · B morale bars · V fog of war on/off · H hide help |

**Reading a soldier.** Blue and red capsules are the two sides; the stick shows where he faces; a yellow cap marks
the sergeant. The bar above him: top = morale (green steady, yellow shaken, orange pinned, red broken),
bottom = suppression (yellow fills as bullets pass close). Dark lying shapes are wounded, grey are dead.

**Squads.** Each squad splits into a gun team (around the machine gun) and a rifle team. The squad leader picks a tactic:
travel, bounding overwatch (one team moves, the other covers), fire and movement, flank, assault, suppress, defend, or fall back.
The top-left panel shows each Blue squad's tactic and what it is doing. With **P** on, orange lines show where the gun team is going,
cyan lines the rifle team, and purple the flanking route.

**The log (bottom left)** lists what a commander would hear about: casualties, men breaking or rallying, squads changing
tactic. Each soldier's every change of mind is left out, except for the soldier you have selected. With fog of war on,
Red's own decisions are hidden.

**Who wins.** A side that can no longer fight loses. Otherwise, when the fighting dies down or time runs out, the side holding
its objective wins (the one with more men on it, if both got there). If nobody holds one and only one side was attacking, the
defender wins. If both sides attacked, the side with clearly more men still fighting wins (20 points more); otherwise it's a draw.

**Fog of war (V).** You see the battle as Blue does. Red soldiers only appear while Blue can see them; red discs mark where
an enemy was last seen (fading as the sighting gets older; a big disc is a guess from a muzzle flash). Each squad only knows what its
own men have seen, or what a squad close by tells it. Press V to see everything.

**Cover points (C).** Green = low wall, yellow = crater, cyan = corner of a ruin you can shoot from,
blue = hiding spot behind a ruin, orange = someone has claimed it. Bars point at the side they protect from.

## Change how battles feel

All tuning numbers live in plain text files in `data/`. Edit one in any text editor, save, and press **R** in the game.

- `data/rules.json`: spotting, cover, morale and suppression thresholds, how long men stay in a spot before the next rush.
- `data/weapons.json`: accuracy by range, rate of fire, how frightening each weapon is.
- `data/maps/test_range.json`: walls and craters (positions in metres).
- `data/scenarios/*.json`: who fights, how many, with what orders.

## How the code is laid out

| Folder | What it is |
|---|---|
| `sim/GroundWar.Sim` | The battle simulation, plain C# with no Godot code, so it can be tested without a screen and moved to Unreal later. |
| `sim/GroundWar.Sim.Tests` | Automated tests. |
| `sim/GroundWar.Runner` | Plays battles with no screen and prints results, for checking the AI quickly. |
| `game/` | The Godot side: drawing, camera, input, overlays. |
| `data/` | Tuning, maps and scenarios. |

For developers (no Godot needed):

```
dotnet test sim/GroundWar.Sim.Tests
dotnet run --project sim/GroundWar.Runner -- --battles 30                   # results table
dotnet run --project sim/GroundWar.Runner -- --battles 1 --seed 4 --log     # every decision in one battle
dotnet run -c Release --project sim/GroundWar.Runner -- --scenario scenarios/meeting.json --map maps/meeting_field_c.json --battles 300   # same scenario, other ground
dotnet run -c Release --project sim/GroundWar.Runner -- --scenario scenarios/stress_1000.json --battles 1 --profile   # 1,000 a side, timing
```

<!-- auto-merge test (2026-11-01). Safe to ignore. -->
