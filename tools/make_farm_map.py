# Makes data/maps/farmhouse.json. Run: python3 tools/make_farm_map.py
# A company-sized field (400 x 300 m). Blue comes from the west; Red holds a farmhouse and barn in a walled yard
# on a low ridge near the east side. Tall hedgerows (they hide what is behind them, but don't stop a bullet) and a
# small wood cover a route round the north (left, seen from Blue); the south is standing wheat with only low
# hedges: it hides a man lying down, and nothing else. A low hedge on a slight rise across the middle makes a
# good place for a support squad to fire from. This is the battle in the "How the AI Works" design doc.
import json, random
r = random.Random(11)
walls = []; craters = []
def W(a, b, high, t=None, hedge=False):
    w = {"a": [round(a[0],1), round(a[1],1)], "b": [round(b[0],1), round(b[1],1)], "thickness": t or (0.7 if high else 0.6), "high": high and not hedge}
    if hedge: w["hedge"] = True
    walls.append(w)
def line(a, b, high, gaps=(), t=None):
    # a wall from a to b with gaps (start, end) given as fractions along it
    cuts = [0.0] + [x for g in gaps for x in g] + [1.0]
    for i in range(0, len(cuts), 2):
        f0, f1 = cuts[i], cuts[i+1]
        W((a[0]+(b[0]-a[0])*f0, a[1]+(b[1]-a[1])*f0), (a[0]+(b[0]-a[0])*f1, a[1]+(b[1]-a[1])*f1), high, t)
def hedge(a, b, high, gaps=()):
    # hedges wander a little: split into short pieces with a small sideways jitter
    n = max(1, int((abs(b[0]-a[0]) + abs(b[1]-a[1])) / 12))
    pts = [(a[0]+(b[0]-a[0])*i/n + (r.uniform(-1.2,1.2) if 0<i<n else 0), a[1]+(b[1]-a[1])*i/n + (r.uniform(-1.2,1.2) if 0<i<n else 0)) for i in range(n+1)]
    for i in range(n):
        f0, f1 = i/n, (i+1)/n
        if any(g0 < (f0+f1)/2 < g1 for g0, g1 in gaps): continue
        W(pts[i], pts[i+1], high, 1.6 if high else 0.9, hedge=high)  # tall ones are real hedgerows

# The farmhouse: stone, with windows on the west side (a low wall you can shoot over) and a door on the east.
line((292,138), (306,138), True)
line((306,138), (306,152), True, gaps=[(0.4,0.58)])
line((306,152), (292,152), True)
line((292,152), (292,138), False, gaps=[(0.42,0.6)])
# The barn: tall timber walls, with broken planks low on the west side where a machine gun can sit.
line((296,165), (316,165), True)
line((316,165), (316,178), True)
line((316,178), (296,178), True, gaps=[(0.4,0.6)])
W((296,178), (296,174), True); W((296,174), (296,169), False); W((296,169), (296,165), True)
# Farmyard wall: low stone, with a gate on the west and the east.
line((280,128), (324,128), False, gaps=[(0.45,0.55)])
line((324,128), (324,188), False, gaps=[(0.45,0.52)])
line((324,188), (280,188), False)
line((280,188), (280,128), False, gaps=[(0.47,0.55)])
# Tall hedgerow along the north of the fields: the covered way round the left.
hedge((150,110), (288,110), True, gaps=[(0.72,0.77)])
hedge((150,110), (150,60), True)
hedge((110,40), (110,104), True, gaps=[(0.45,0.55)])
hedge((200,20), (200,95), True, gaps=[(0.6,0.7)])
# Low hedges: one across the middle (the support position) and field edges.
hedge((170,118), (170,205), False, gaps=[(0.45,0.52)])
# Blue's start line: a tall hedge with gaps, each gap screened by a short hedge just beyond it so the farm can't see in.
blue_gaps = [(0.17,0.21), (0.38,0.42), (0.62,0.66), (0.84,0.88)]
hedge((62,50), (62,250), True, gaps=blue_gaps)
for g0, g1 in blue_gaps:
    W((74, 50 + 200*g0 - 7), (74, 50 + 200*g1 + 7), True, 1.6, hedge=True)
hedge((90,215), (330,215), False, gaps=[(0.3,0.35), (0.7,0.75)])
hedge((230,230), (230,290), False)
# East of the farm: Red's rear, a tall hedge with a lane through it.
hedge((345,95), (345,210), True, gaps=[(0.45,0.55)])
# Shell holes in the wheat
for _ in range(14):
    craters.append({"center": [round(r.uniform(120, 275),1), round(r.uniform(120, 270),1)], "radius": round(r.uniform(1.8, 3.2),1)})
for _ in range(5):
    craters.append({"center": [round(r.uniform(160, 270),1), round(r.uniform(30, 105),1)], "radius": round(r.uniform(1.8, 2.8),1)})

# The ground. The farm sits on a low ridge running north-south; a slight rise carries the middle hedge; a shallow
# dip runs along the north hedgerow (the covered way), and the ground falls away a little behind the farm.
ridges = [
    {"a": [305, 40], "b": [305, 260], "width": 95, "height": 8},
    {"a": [170, 120], "b": [170, 205], "width": 45, "height": 2},
    {"a": [120, 88], "b": [270, 88], "width": 30, "height": -2.5},
]
hills = [{"center": [40, 150], "radius": 70, "height": 1.5}]
def rect(x0, y0, x1, y1): return [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]
areas = [
    # A small wood at the end of the covered way, north of the farm: where a flanking platoon can form up unseen.
    {"kind": "woods", "points": [[236, 40], [300, 34], [318, 62], [296, 98], [244, 100], [228, 70]]},
    # A copse on Blue's left.
    {"kind": "woods", "points": [[116, 12], [190, 10], [194, 52], [156, 56], [118, 38]]},
    # Wheat: the open fields south of the hedgerow, either side of the middle hedge.
    {"kind": "crops", "points": rect(82, 118, 166, 210)},
    {"kind": "crops", "points": rect(176, 118, 272, 210)},
    {"kind": "crops", "points": rect(96, 222, 224, 288)},
]
# An orchard behind the farmhouse and a few trees along the lane.
trees = [[334 + 9*i + r.uniform(-1.5, 1.5), 112 + 10*j + r.uniform(-1.5, 1.5)] for i in range(2) for j in range(3)]
trees += [[262, 222], [300, 224], [86, 230], [244, 150]]
trees = [[round(x,1), round(y,1)] for x, y in trees]
# No shell holes among the trees.
out = {"name": "Farmhouse", "width": 400, "height": 300, "walls": walls, "craters": craters,
       "ridges": ridges, "hills": hills, "areas": areas, "trees": trees}
with open("data/maps/farmhouse.json", "w") as f:
    f.write("// Farmhouse: made by tools/make_farm_map.py (edit that, not this). A walled farmhouse and barn held by Red,\n")
    f.write("// on a low ridge; tall hedgerows and a wood covering a way round the north, standing wheat to the south.\n")
    json.dump(out, f, indent=1)
print(len(walls), "walls,", len(craters), "craters")
