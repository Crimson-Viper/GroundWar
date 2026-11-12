# Makes data/maps/valley.json. Run: python3 tools/make_valley_map.py
# A battalion-sized valley (1000 x 600 m) for milestone 7, part 2. Blue's battalion comes from the west, where its HQ
# sits in a walled farmyard. Red holds a line across the middle: a village in the north and a farm in the south,
# with a walled churchyard behind them and its battalion HQ in a manor house at the east edge.
# The valley runs east-west between two ridges; the village, the farm, the churchyard and the manor each sit on a
# low rise, and a shallow stream bed crosses no man's land. Tall hedgerows (they hide, but don't stop a bullet)
# give covered ways forward; low hedges give fire positions; the woods are real woods (see docs/systems/terrain.md).
import json, random
r = random.Random(23)
import math
walls = []; craters = []; areas = []

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

def house(x, y, w, h, door_side, window_side):
    # stone walls; a door (gap) on one side and windows (a low stretch you can shoot over) on another
    sides = {"n": ((x,y), (x+w,y)), "e": ((x+w,y), (x+w,y+h)), "s": ((x+w,y+h), (x,y+h)), "w": ((x,y+h), (x,y))}
    for k, (a, b) in sides.items():
        if k == door_side: line(a, b, True, gaps=[(0.4, 0.6)])
        elif k == window_side:
            line(a, (a[0]+(b[0]-a[0])*0.3, a[1]+(b[1]-a[1])*0.3), True)
            line((a[0]+(b[0]-a[0])*0.3, a[1]+(b[1]-a[1])*0.3), (a[0]+(b[0]-a[0])*0.7, a[1]+(b[1]-a[1])*0.7), False)
            line((a[0]+(b[0]-a[0])*0.7, a[1]+(b[1]-a[1])*0.7), b, True)
        else: line(a, b, True)

def wood(cx, cy, rx, ry, n=0):
    # a wood: a ragged oval of trees
    pts = []
    for i in range(18):
        ang = 2 * math.pi * i / 18; k = r.uniform(0.85, 1.15)
        pts.append([round(cx + math.cos(ang) * rx * k, 1), round(cy + math.sin(ang) * ry * k, 1)])
    areas.append({"kind": "woods", "points": pts})

def field(x0, y0, x1, y1):
    areas.append({"kind": "crops", "points": [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]})

# ---- Blue's rear: battalion HQ in a walled farmyard at the west edge.
line((20,280), (64,280), False, gaps=[(0.45,0.6)])
line((64,280), (64,322), False, gaps=[(0.42,0.58)])
line((64,322), (20,322), False)
line((20,322), (20,280), False)
house(28, 288, 14, 10, "e", "s")
# Blue's start: a tall hedge with gaps along x = 150.
hedge((150, 40), (150, 560), True, gaps=[(0.12,0.16), (0.3,0.34), (0.47,0.53), (0.66,0.7), (0.84,0.88)])

# ---- No man's land: hedgerows running east-west give covered ways forward, a low hedge across the middle.
hedge((160, 100), (390, 100), True, gaps=[(0.5,0.55)])        # north lane to the village
hedge((160, 520), (400, 520), True, gaps=[(0.45,0.5)])        # south lane to the farm
hedge((300, 230), (300, 370), False, gaps=[(0.45,0.55)])      # low hedge: a fire position facing both
hedge((250, 300), (380, 300), True, gaps=[(0.4,0.5)])         # tall hedge splitting the middle
wood(230, 200, 35, 30, 40)
wood(250, 420, 40, 30, 45)

# ---- The village (Red's north position), around (480, 170).
house(450, 140, 14, 10, "e", "w")
house(472, 130, 12, 12, "s", "w")
house(458, 172, 16, 10, "e", "w")
house(492, 158, 12, 14, "e", "s")
house(480, 200, 14, 10, "n", "w")
house(506, 190, 10, 10, "e", "w")
line((440, 120), (440, 220), False, gaps=[(0.3,0.38), (0.65,0.72)])   # garden walls facing west
line((440, 220), (530, 220), False, gaps=[(0.5,0.58)])
line((440, 120), (520, 120), False, gaps=[(0.6,0.68)])
hedge((530, 110), (530, 230), True, gaps=[(0.45,0.55)])                # behind the village

# ---- The farm (Red's south position), around (500, 440).
house(492, 430, 14, 14, "e", "w")                     # farmhouse: windows west, door east
line((500, 458), (520, 458), True); line((520, 458), (520, 471), True)   # barn
line((520, 471), (500, 471), True, gaps=[(0.4,0.6)])
W((500, 471), (500, 467), True); W((500, 467), (500, 462), False); W((500, 462), (500, 458), True)
line((476, 418), (530, 418), False, gaps=[(0.45,0.55)])               # yard wall
line((530, 418), (530, 482), False, gaps=[(0.45,0.52)])
line((530, 482), (476, 482), False)
line((476, 482), (476, 418), False, gaps=[(0.47,0.55)])
hedge((420, 380), (420, 500), False, gaps=[(0.48,0.56)])               # a low hedge in front of the farm
hedge((560, 390), (560, 520), True, gaps=[(0.45,0.55)])                # behind the farm

# ---- Between the line and Red's HQ: the churchyard (Red's reserve) around (720, 300), and a sunken lane.
house(712, 292, 16, 12, "e", "w")                     # the chapel
line((690, 270), (750, 270), False, gaps=[(0.45,0.55)])
line((750, 270), (750, 330), False, gaps=[(0.45,0.55)])
line((750, 330), (690, 330), False, gaps=[(0.45,0.55)])
line((690, 330), (690, 270), False, gaps=[(0.45,0.55)])
hedge((620, 150), (620, 280), True, gaps=[(0.3,0.36)])
hedge((620, 320), (620, 470), True, gaps=[(0.64,0.7)])
wood(640, 300, 18, 15, 14)
wood(820, 160, 40, 35, 40)
wood(820, 450, 40, 35, 40)

# ---- Red's battalion HQ: a manor house in a walled garden at the east edge, around (930, 300).
house(922, 290, 18, 14, "w", "n")
line((890, 270), (975, 270), False, gaps=[(0.45,0.55)])
line((975, 270), (975, 335), False)
line((975, 335), (890, 335), False, gaps=[(0.45,0.55)])
line((890, 335), (890, 270), False, gaps=[(0.42,0.58)])
hedge((860, 220), (860, 380), True, gaps=[(0.47,0.53)])

# Shell holes across the open ground.
for _ in range(40):
    craters.append({"center": [round(r.uniform(170, 620),1), round(r.uniform(30, 570),1)], "radius": round(r.uniform(1.8, 3.4),1)})

# Wheat fields in no man's land.
field(170, 110, 290, 180); field(170, 330, 290, 400); field(320, 430, 410, 510); field(330, 120, 420, 220)

# The ground: a ridge along each long edge, low rises under the village, the farm, the churchyard and the manor,
# and a shallow stream bed running north-south through no man's land.
ridges = [
    {"a": [0, -20], "b": [1000, -20], "width": 170, "height": 16},
    {"a": [0, 620], "b": [1000, 620], "width": 170, "height": 14},
    {"a": [345, 60], "b": [360, 540], "width": 45, "height": -3},
]
hills = [
    {"center": [485, 170], "radius": 120, "height": 6},
    {"center": [500, 448], "radius": 110, "height": 5},
    {"center": [722, 300], "radius": 100, "height": 6},
    {"center": [932, 302], "radius": 120, "height": 7},
    {"center": [45, 300], "radius": 120, "height": 5},
    {"center": [235, 300], "radius": 70, "height": 3},
]
# No shell holes in the woods.
out = {"name": "Valley", "width": 1000, "height": 600, "walls": walls, "craters": craters,
       "ridges": ridges, "hills": hills, "areas": areas}
with open("data/maps/valley.json", "w") as f:
    f.write("// Valley: made by tools/make_valley_map.py (edit that, not this). A battalion battlefield: Blue's HQ in the west,\n")
    f.write("// a village and a farm on low rises across the middle, a churchyard behind them, Red's HQ in a manor house in\n")
    f.write("// the east; ridges along the north and south edges, woods, wheat and a stream bed in no man's land.\n")
    json.dump(out, f, indent=1)
print(len(walls), "walls,", len(craters), "craters")
