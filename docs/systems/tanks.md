# Tanks

## What it is
One medium tank type: a hull that drives and turns, a turret with a main gun (high-explosive shells), a machine gun,
armour, and a tank commander AI that is separate from the squad and soldier AI. This is the first iteration; the
full tank milestone (M8: platoons, escorts, tank hunting, damage by parts, bail-out) builds on it.
Design: the Tanks design doc. Code: `sim/GroundWar.Sim/AI/TankBrain.cs`, `Combat/Battle.Tanks.cs`, `Data/VehicleDef.cs`;
drawing in `game/TankView.cs`.

## How a tank is put together
In the simulation a tank is a `Soldier` with a `Vehicle` set (`IsTank`). This lets it reuse spotting, the knowledge
system and the machine gun, while its own rules take over for moving, the main gun, morale and damage. A tank
is in a vehicle group of its own (`Group.IsVehicles`), which skips the squad brain. Scenarios add tanks with
`"vehicles": { "tank": 1 }` on a group.

Tuning: `data/vehicles.json` (speed, turn rates, armour, which guns, damage chances, how easy it is to spot and hit,
stand-off distance) and the `tank_gun`, `tank_mg`, `atr` and `bazooka` entries in `data/weapons.json`.

## Moving
The hull turns in place, then drives once it faces within about 30° of where it's going (`Battle.MoveTank`).
It uses a wider path-finding grid than men (`BattleMap.TankNav`), so it doesn't squeeze through gaps it can't fit.
An immobilised tank (broken track) can't move but still fights.

## Guns
- **Main gun** (`Battle.UpdateMainGun`, `FireShell`): the turret turns towards the target at its own rate. The gun
  fires when on target and loaded (7 s). A moving tank first stops for a short halt (about 4 s) to fire.
  Shells land close to the aim point, scattered short or to the side.
- **Blast** (`Battle.Blast`): men within 6 m (`blastRadius`) may be hit, more likely the closer they are, half as likely
  lying down, and less behind cover. Everyone within 14 m gets heavy suppression, friends included.
- **Machine gun**: works like an infantry machine gun, and can fire on the move without turning the hull.
- Shells and the machine gun give the tank away to men near where they land.

## Armour and damage (`Battle.ArmourHit`)
Rifles and machine guns can't hurt a tank. Armour is 50 mm front, 35 side, 25 rear; the side that's hit depends on
the angle from the shooter to the tank's facing. A weapon's `penetration` falls to half at its full range.

| Weapon | Penetration | Against the medium tank |
|---|---|---|
| Rifle, machine gun | 0 | Rings off |
| Anti-tank rifle | 22 mm | Never gets through; 15% chance to break a track (`trackBreakChance`) |
| Bazooka | 100 mm | Through any side within ~100 m; range 150 m; one rocket at a time, slow to reload |
| Tank main gun | 50 mm | Side and rear; front only point-blank (armour-piercing rounds come in M8) |

When a hit gets through: 45% knocked out (it becomes a dark wreck), 30% immobilised, otherwise the crew is hurt
(the gun slows and aims worse). A round that doesn't get through can still break a track (10% unless the weapon says otherwise).

**Bazooka backblast:** firing one throws flame and smoke out behind, so every enemy near the target learns exactly where
it came from. A bazooka gunner never fires at infantry.

**Why these numbers:** the Tanks design doc says anti-tank rifles only trouble light tanks and break tracks, and that
bazookas, satchel charges, mines and AT guns are the real tank killers. Grenade bundles, satchels and mines come with
tank hunting in M8.

## Tank commander AI (`TankBrain`)
- **Advances by bounds**: drives about 40 m, halts briefly to fire, moves on.
- **Main gun targets**: enemy tanks first, then anti-tank weapons and machine guns, then bunches of men (more men
  close together score higher, broken men less).
- **Machine gun targets**: the nearest dangerous man, anti-tank gunners first.
- **Keeps its front armour towards known anti-tank threats** while it fights.
- **Won't charge infantry alone**: if enemy infantry is within 45 m (`standOff`) and none of its own infantry is within
  30 m, it holds back and says so ("Holding back: enemy infantry close and no infantry of our own with us").
- Status and thought show in the HUD like a squad's.

## Effect on infantry
An enemy tank within 100 m drains the morale of men who have nothing that can hurt it (0.3 a second, half that in
good cover; `morale.tankFearPerSecond`, `tankFearRange`). Losing a tank costs nearby friends morale; knocking one out
lifts the side that did it.

## Seeing it in the game
Click the tank: the right panel shows armour, whether it's running, immobilised or knocked out, shells left and
loading, and what the commander is thinking. Shells show as a bright tracer with a flash and a burst of smoke.

## Results (200 battles, "Tank support")
Blue wins 171, Red 29 (without the tank, the same attack wins 31). The tank was knocked out in 31 battles and
immobilised but still fighting in 10. The runner prints this tank summary for any scenario with tanks.

## Tests
`TankTests.cs`: rifles can't get through, the anti-tank rifle only breaks tracks, the bazooka gets through the side;
the tank drives towards its objective; only anti-tank weapons knock it out, and its main gun kills men.
