# Makes data/maps/town.json. Run: python3 tools/make_town_map.py  (change the seed in random.Random(7) for a different town)
import random
r = random.Random(7)
walls = []; craters = []
def W(a, b, high, t=None):
    walls.append((a, b, t or (0.7 if high else 0.6), high))
def side(a, b, high, door):
    # a wall from a to b, with a 2.4 m doorway in the middle if door
    if not door: W(a, b, high); return
    mx, my = (a[0]+b[0])/2, (a[1]+b[1])/2
    L = ((b[0]-a[0])**2 + (b[1]-a[1])**2) ** 0.5
    ux, uy = (b[0]-a[0])/L, (b[1]-a[1])/L
    W(a, (mx-ux*1.2, my-uy*1.2), high); W((mx+ux*1.2, my+uy*1.2), b, high)
def house(x0, y0, w, h, ruined=False):
    c = [(x0,y0),(x0+w,y0),(x0+w,y0+h),(x0,y0+h)]
    doors = r.sample(range(4), 2)
    for i in range(4):
        a, b = c[i], c[(i+1)%4]
        if ruined and r.random() < 0.5:
            # collapsed wall: a low rubble line you can shoot over, with a gap
            side(a, b, False, True)
        else:
            side(a, b, True, i in doors)
# Blocks either side of the main street (y 94-106), split by cross streets.
# Each block has a row of houses facing the main street, a row facing the back lane,
# and yards with low garden walls between them.
blocks_x = [(84, 122), (132, 170), (180, 218), (228, 266)]
for bx0, bx1 in blocks_x:
    for north in (True, False):
        street_y = 94 if north else 106          # the main street edge
        back_y = 18 if north else 182            # the back lane edge
        sgn = -1 if north else 1                 # direction from the street into the block
        for row_edge, depth_dir in ((street_y, sgn), (back_y, -sgn)):
            x = bx0
            while x < bx1 - 6:
                w = min(r.uniform(8, 13), bx1 - x)
                d = r.uniform(10, 14)
                y0 = row_edge + depth_dir * 1.0
                y1 = y0 + depth_dir * d
                if r.random() < 0.9:
                    house(round(x,1), round(min(y0,y1),1), round(w-1,1), round(d,1), ruined=r.random() < 0.3)
                x += w
        # yard walls between the two rows
        ymid = (street_y + back_y) / 2
        # sheds and outbuildings in the yards
        for _ in range(2):
            sx = r.uniform(bx0 + 2, bx1 - 10)
            house(round(sx,1), round(ymid + r.uniform(-9, 3),1), round(r.uniform(5, 8),1), round(r.uniform(5, 7),1), ruined=r.random() < 0.4)
        for _ in range(4):
            x = r.uniform(bx0 + 2, bx1 - 12)
            W((round(x,1), round(ymid + r.uniform(-6, 6),1)), (round(x + r.uniform(6, 11),1), round(ymid + r.uniform(-6, 6),1)), False)
# Rubble and garden walls in the streets
for _ in range(16):
    x, y = r.uniform(80, 275), r.choice([r.uniform(96, 104), r.uniform(10, 190)])
    L = r.uniform(4, 9)
    if r.random() < 0.5: W((round(x,1), round(y,1)), (round(x+L,1), round(y,1)), False)
    else: W((round(x,1), round(y,1)), (round(x,1), round(y+L,1)), False)
for _ in range(10):
    craters.append(((round(r.uniform(80, 280),1), round(r.choice([r.uniform(93,107), r.uniform(10,190)]),1)), round(r.uniform(2,3.5),1)))
# Open fields on the west where Blue starts: hedges (low walls) and shell holes
for (a,b) in [((60,40),(60,75)), ((60,125),(60,165)), ((40,20),(70,20)), ((38,180),(70,180))]: W(a,b,False)
# A ruined barn across the end of the main street screens the fields from the town.
W((68,84),(68,116),True); W((68,84),(60,84),True); W((68,116),(60,116),True)
for c in [(50,100),(66,88),(68,116),(45,60),(47,140)]: craters.append((c, 3.0))
fw=lambda w: f'    {{ "a": [{w[0][0]:g}, {w[0][1]:g}], "b": [{w[1][0]:g}, {w[1][1]:g}], "thickness": {w[2]:g}' + (', "high": true }' if w[3] else ' }')
fc=lambda c: f'    {{ "center": [{c[0][0]:g}, {c[0][1]:g}], "radius": {c[1]:g} }}'
out=['// Town: fields to the west, then a town of houses along a main street (y 94-106) with cross streets.',
     '// Houses are high walls (block sight) with doorways; ruined ones have collapsed sides you can shoot over.',
     '// Rubble lines and shell holes in the streets give more cover. A cover-rich map for testing street fighting.',
     '{','  "name": "Town",','  "width": 300,','  "height": 200,','  "walls": [',',\n'.join(map(fw,walls)),'  ],','  "craters": [',',\n'.join(map(fc,craters)),'  ]','}']
open(__import__('os').path.join(__import__('os').path.dirname(__file__), '..', 'data', 'maps', 'town.json'), 'w').write('\n'.join(out)+'\n')
print(len(walls), 'walls', len(craters), 'craters')
