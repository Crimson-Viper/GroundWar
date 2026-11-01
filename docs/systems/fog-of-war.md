# Fog of war

## What it is
Each side, and each squad, only knows about enemies its own soldiers have seen. The player sees their own troops
live, always, and the enemy only as their side has spotted them.

**Design rule (Jimmy, 1 Nov):** your own troops are always visible live; fog of war hides only the enemy.

## How it works
- **Sightings** (`Units/SideKnowledge.cs`, class `Contact`): enemy id, last seen position, when, and how sure
  (1.0 = seen with own eyes, 0.5 = a guess from a muzzle flash). Not where he is now: where he *was*.
- **Each squad has its own knowledge** (`Group.Knowledge`), filled by its own men's eyes (`Battle.Report`).
- **HQ's picture** (milestone 4): in a scenario with an HQ, the side's commander (and the player) only knows what
  squads' reports have brought in, late. See [Messages and delay](messages-and-delay.md). Without an HQ, the side keeps a
  combined picture of every sighting at once.
- **Sharing:** squads within 80 m swap sightings each time their leader thinks (`SquadBrain.ShareWithNeighbours`):
  they can shout to each other. A squad far away from the fight doesn't know what the squad in contact can see.
- **Forgetting:** a sighting not refreshed for 30 s is dropped (`perception.contactMemory`). A man known to be down is forgotten at once.
- **Nothing in the AI reads the true enemy positions** except soldiers' eyes (`Battle.Perceive`). Squad plans,
  targets and cover choice all work from sightings.

## What the player sees
`game/BattleView.cs` → `KnownToPlayer`: own side always, live; enemy soldiers only while the player's side has a sighting
under 1.5 s old. In a scenario with an HQ, reports always arrive older than that, so the enemy shows only as
"last seen here" markers, exactly as HQ knows him (turn `comms.playerSeesReportsOnly` off to see live sightings).
Where an enemy was seen but isn't visible now, a red disc marks the last known spot, fading as the sighting gets older
(a big disc means a guess). Enemy squad names and decisions are hidden from the panel and log; enemy casualties still
appear in the log. **V** turns fog off to see everything (a debug view).

## Tuning
`data/rules.json` → `perception` (`contactMemory`, `sharedContactWindow`, `muzzleFlashRevealChance`) and
`squad.shareRadius`.

## Tests
`SquadTests.cs` (soldiers only see what is in their sight line; each squad has its own picture of the enemy).
