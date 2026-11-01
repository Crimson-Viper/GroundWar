# Spotting and shooting

## What it is
How a soldier sees enemies, how likely a shot is to hit, how near misses frighten men, and what a hit does.
Code: `sim/GroundWar.Sim/Combat/Battle.Perception.cs` (seeing) and `Battle.Combat.cs` (shooting).

## Seeing (`Battle.Perceive`)
Every 0.5 s (`perception.interval`) each soldier looks at the nearest enemies within 400 m (`viewRange`), up to 12
at a time (`maxTracked`):

- Closer than 25 m (`autoSpotRange`): always seen.
- A mate saw him in the last 2 s (`sharedContactWindow`): 90% chance (someone shouted it out).
- Otherwise a dice roll: 50% close, falling to 4% at 400 m (`baseSpotChance`, `farSpotChance`), then
  ×4 if the enemy fired in the last 3 s, ×1.5 if he's moving (not crawling), ×0.5 if he's lying down.
- A high wall in the way: never seen.
- An enemy in good cover is a further coin flip to pick out (`coverMultiplier`).

What he sees goes into his squad's and side's knowledge (see [Fog of war](fog-of-war.md)).

## Shooting (`Battle.UpdateWeapon`, `FireRound`)
A man set to "Engage" fires when his weapon is ready: rifles every ~4 s, machine guns in bursts (`data/weapons.json`).
He counts rounds, changes magazines, and eventually runs out. Frightened men fire more slowly (up to twice as slow at
full suppression) and stop firing altogether above 80 suppression (`morale.suppressionNoFire`).

### Chance to hit (`Battle.HitChance`)
Start from the weapon's accuracy table at that range (for an average shooter at a standing man in the open),
then multiply by:

| Factor | Value | Tuning |
|---|---|---|
| Shooter's skill | 0.8–1.2 | `soldier.skillMin/Max` |
| Target standing / crouched / prone | 1.0 / 0.6 / 0.35 | `combat.*HitFactor` |
| Target moving | 0.5 | `combat.movingTargetFactor` |
| Shooter shaken or worse | 0.8 | `combat.shakenAccuracy` |
| Shooter's suppression | down to 0.25 at full fear | `combat.suppressionAccuracyLoss` |
| Target's cover | × (1 − protection) | see [Map and cover](map-and-cover.md) |

Capped at 95%.

**Covering fire** at a spot (not a man) can still hit anyone within 3 m of it, at 30% of the normal chance
(`squad.areaFireAccuracy`).

### Near misses
Every round that misses lands somewhere near the aim point (spread by the weapon's `spread`). Everyone on the
target's side within the weapon's `suppressionRadius` of where it landed gains suppression (rifle 10, machine gun more),
less for men in cover (`combat.coverSuppressionShield`). The man aimed at always feels it. A muzzle flash has a 35%
chance (`perception.muzzleFlashRevealChance`) to give the shooter's rough position away to the men being shot at.

### Hits (`Battle.ApplyHit`)
45% of hits kill (`soldier.killChanceOnHit`), the rest wound. Either way the man is out of the fight. Friends within
25 m lose morale (more if killed, more the closer they are); if he was a sergeant, his whole squad takes a morale
blow (once per 30 s, `morale.leaderShockWindow`, so losing two sergeants in quick succession isn't double-counted);
the shooter's squad gets a small lift. See [Morale and suppression](morale-and-suppression.md).

## Seeing it in the game
Yellow tracers are rifle shots, longer orange ones machine-gun bursts; small puffs show where rounds land. **L** draws
each man's aim line. The log lists every casualty with range, stance and how much cover he had.

## Tests
`BattleTests.cs` (same seed gives the same battle, firefights happen, suppression fades), `SquadTests.cs`.
