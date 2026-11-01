# Drawing the battle (Godot)

## What it is
The Godot side of the game, in `game/`. It never decides anything about the battle: it reads the simulation each
frame and draws it.

| File | Does |
|---|---|
| `Main.cs` / `Main.tscn` | Starts a battle, runs the simulation at the chosen speed, handles keys, screenshot mode |
| `BattleView.cs` | Soldiers (through `SoldierCrowd`), tracers, impact puffs, last-known enemy markers, plan lines, selection |
| `SoldierCrowd.cs` | Draws every soldier from the low-poly models (one batch per pose), with sergeant's cap, morale bar, name labels |
| `TankView.cs` | Draws each tank from its model, turning the turret; dark wreck when knocked out |
| `ModelViewer.cs` | A separate scene (F6) to look at the models |
| `MapView.cs` | Ground, walls, craters, foxholes, objectives, grid and cover overlays |
| `CameraRig.cs` | The free camera and follow mode |
| `Hud.cs` | Status panel, selected soldier panel, log, help, end banner, order and message panels, company panel with the brief-back buttons |
| `CommsView.cs` | HQ tents, telephone cables, runners, phone pulses, radio rings, flares |
| `CompanyView.cs` | Company commanders' plans as arrows on the ground, a flag over each company HQ |
| `SupportView.cs` | Mortar tubes, and a ring over each target your mortars (or artillery) have been told to fire on |
| `KeyPointView.cs` | The key points of a battalion battle: a ring and a name, blue or red when only that side's men are on it |
| `SoundView.cs` | Battle sounds: a pool of 3D sound players for nearby shots, shell bursts, tank guns, distant battle and radio squelch |
| `SmokeView.cs` | Smoke clouds from shell bursts, black smoke and fire glow over burning wrecks, drifting in the wind |
| `TableView.cs` | The map table: a paper map with unit counters that fades in when you zoom far out |
| `ActionCamera.cs` | The battle cam: picks where the fighting is hottest and flies the camera there |
| `Draw.cs` | Small helpers: sim-to-Godot positions, materials, batching |
| `shaders/` | Ground pattern; morale/suppression bar; soft smoke |

## Why batched drawing
At 1,000 soldiers a side, one Godot node per soldier part was far too slow. Every kind of thing is now drawn as
one **MultiMesh** (one draw call for all bodies, one for all rifles, one for all tracers, and so on). A part that
shouldn't show this frame gets a zero-size transform. The stress test (1,000 a side) draws in about 85 draw calls,
41 nodes and about 4 ms a frame. Name labels are real nodes, but only for the soldiers that need one.

## Models
Soldiers and tanks use simple low-poly models made by `tools/make_models.py` (`.glb` files). If a model is missing
the game falls back to grey boxes. See [Models](../models.md).

## Smooth movement
The simulation moves in 0.1 s steps. Godot draws each soldier part-way between his last two positions, so movement is
smooth at any frame rate and any game speed.

## Coordinates
Sim (x, y) in metres becomes Godot (x, 0, y). Use `Draw.To3`. For scaling a mesh in its own frame use
`basis * Basis.FromScale(...)` (Godot's `Scaled` scales in world space, which skews rotated objects).

More on the sounds, smoke, map table and battle cam in [Spectacle](spectacle.md).

## Overlays and keys
See the controls table in the main README. Overlays are toggled in `Main.cs` and drawn by `MapView` and `BattleView`.

## Testing it
There is no automated test for drawing. Screenshot mode renders a frame headlessly and prints draw calls, node
count and the time to update soldiers; see [Testing and tuning](../tools/testing.md).
