# Morale and suppression

## What it is
Two numbers on every soldier that decide how willing he is to fight. Code: `sim/GroundWar.Sim/Combat/Battle.Morale.cs`,
plus the morale hits in `Battle.Combat.cs`. Tuning: `data/rules.json` → `morale`.

- **Suppression** (0–100) is short-term fear: bullets passing close right now. It rises fast and fades fast.
- **Morale** (starts 60–85, sergeants +10) is longer-term nerve. It drops with bad events and recovers slowly.

## Why
The game's battles should be won by breaking the enemy's will, the way real small-unit fights mostly were, not by
killing every man. Suppression is what makes fire and movement work: one team's fire keeps the enemy's heads down
while the other moves.

## States
| State | When | Effect |
|---|---|---|
| Steady | Normal | Fights normally |
| Shaken | Morale < 50 or suppression ≥ 35 | Shoots worse |
| Pinned | Morale < 32 or suppression ≥ 55 | Won't advance; stays put or crawls |
| Broken | Morale < 18 | Runs for the rear and ignores orders; leaves the map if he gets there ("fled") |

Above 80 suppression a man can't fire at all and just hugs the ground.

## What changes them
**Suppression** comes from near misses (see [Spotting and shooting](spotting-and-shooting.md)). After 2 s with no
new near misses it fades by 8 a second.

**Morale goes down:**
| Event | Amount |
|---|---|
| Each point of suppression | −0.05 |
| A friend within 25 m killed / wounded | −12 / −7 (less if further away) |
| His sergeant hit | −15 to the whole squad (once per 30 s) |
| Half his squad broken or down | −12 to the rest, once |
| Alone (no friend within 15 m) | −0.3 a second |
| Out of ammunition | −0.5 a second |

**Morale goes up** (only while suppression is under 10):
| Situation | Per second |
|---|---|
| Always | +0.4 |
| In good cover | +0.3 |
| His sergeant within 25 m | +0.3 |
| Seeing an enemy go down | +3 (once) |

It never goes above where he started.

**Rallying:** a broken man rallies once his morale is back above 40 and no known enemy is within 80 m.

## Seeing it in the game
**B** shows a bar above each man: the top is morale (green steady, yellow shaken, orange pinned, red broken), the
bottom fills yellow with suppression. The top-left panel counts fighting, pinned, broken, down and fled for each side.

## Tests
`BattleTests.cs` (suppression fades when the shooting stops; a broken soldier runs for the rear).
