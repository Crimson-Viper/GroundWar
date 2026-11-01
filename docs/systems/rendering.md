# Drawing the battle (Godot)

## What it is
The Godot side of the game, in `game/`. It never decides anything about the battle: it reads the simulation each
frame and draws it.

| File | Does |
|---|---|
| `Main.cs` / `Main.tscn` | Starts a battle, runs the simulation at the chosen speed, handles keys, screenshot mode |
| `BattleView.cs` | Soldiers (through `SoldierCrowd`), tracers, impact puffs, last-known enemy markers, plan lines, selection |
| `SoldierCrowd.cs` | Draws every soldier: body, rifle, sergeant's cap, morale bar, name labels |
| `MapView.cs` | Ground, walls, craters, foxholes, objectives, grid and cover overlays |
| `CameraRig.cs` | The free camera and follow mode |
| `Hud.cs` | Status panel, selected soldier panel, log, help, end banner |
| `Draw.cs` | Small helpers: sim-to-Godot positions, materials, batching |
| `shaders/` | Ground pattern; morale/suppression bar |

## Why batched drawing
At 1,000 soldiers a side, one Godot node per soldier part was far too slow. Every kind of thing is now drawn as
one **MultiMesh** (one draw call for all bodies, one for all rifles, one for all tracers, and so on). A part that
shouldn't show this frame gets a zero-size transform. The stress test (1,000 a side) draws in about 85 draw calls,
41 nodes and about 4 ms a frame. Name labels are real nodes, but only for the soldiers that need one.

## Smooth movement
The simulation moves in 0.1 s steps. Godot draws each soldier part-way between his last two positions, so movement is
smooth at any frame rate and any game speed.

## Coordinates
Sim (x, y) in metres becomes Godot (x, 0, y). Use `Draw.To3`. For scaling a mesh in its own frame use
`basis * Basis.FromScale(...)` (Godot's `Scaled` scales in world space, which skews rotated objects).

## Overlays and keys
See the controls table in the main README. Overlays are toggled in `Main.cs` and drawn by `MapView` and `BattleView`.

## Testing it
There is no automated test for drawing. Screenshot mode renders a frame headlessly and prints draw calls, node
count and the time to update soldiers; see [Testing and tuning](../tools/testing.md).
