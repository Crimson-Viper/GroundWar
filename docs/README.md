# Ground War: how the game works

These pages explain every system in the prototype in plain words: what it does, how it works, why it was
built that way, where the code lives, how to tune it and how to test it. They are kept up to date with the
code. **Every pull request that adds or changes a system updates its page here in the same pull request.**

The design (what the game should become) lives in the design docs. These pages describe what is actually built.

## Systems

| Page | What it covers |
|---|---|
| [Architecture](systems/architecture.md) | How the code is split (simulation vs Godot), the battle loop, data files, determinism |
| [Map and cover](systems/map-and-cover.md) | Walls, craters, cover points, protection, foxholes, the battlefield grid, path finding |
| [Soldier AI](systems/soldier-ai.md) | How each man decides what to do: utility scoring, cover, shooting, digging in, fleeing |
| [Spotting and shooting](systems/spotting-and-shooting.md) | Who sees whom, hit chances, suppression from near misses, wounds and deaths |
| [Morale and suppression](systems/morale-and-suppression.md) | Steady, shaken, pinned, broken; what raises and lowers morale; rallying |
| [Squad AI](systems/squad-ai.md) | Orders, teams and tactics: travel, bounding, fire and movement, flank, assault, suppress, defend, fall back |
| [Fog of war](systems/fog-of-war.md) | What each side knows, sightings, sharing between squads, what the player sees |
| [Who wins](systems/battle-results.md) | How a battle ends and who is given the win |
| [Drawing the battle](systems/rendering.md) | The Godot side: batched drawing, overlays, the HUD and log |
| [Tanks](systems/tanks.md) | Armour, guns, anti-tank weapons, the tank commander AI |
| [Models](models.md) | The soldier and tank models, and how to swap in your own from Blender |

## Tools

| Page | What it covers |
|---|---|
| [Testing and tuning](tools/testing.md) | Automated tests, the headless battle runner, screenshots, scenarios and maps |
