# Makes the placeholder 3D models in models/: soldier.glb and tank.glb.
# Run: python3 tools/make_models.py   (needs only Python 3, no Blender)
#
# Everything is built from simple shapes (boxes, slanted boxes, cylinders, extruded outlines) and written
# as glTF binary (.glb), which Godot imports directly and Blender can open too.
# Units are metres. Y is up and the models face +Z (that is Blender's "front" view, -Y in Blender).
# Any material called "Team" is painted in the side's colour by the game. See docs/models.md.
import json, math, struct, os

# ------------------------------------------------------------------ tiny vector maths
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def mul(a, s): return (a[0]*s, a[1]*s, a[2]*s)
def dot(a, b): return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def norm(a):
    l = math.sqrt(dot(a, a)) or 1.0
    return (a[0]/l, a[1]/l, a[2]/l)
def lerp(a, b, t): return add(a, mul(sub(b, a), t))

# ------------------------------------------------------------------ meshes
class Mesh:
    """Triangles grouped by material name. Every face gets its own vertices, so it is flat shaded (low-poly look)."""
    def __init__(self): self.parts = {}
    def tri(self, mat, a, b, c):
        n = norm(cross(sub(b, a), sub(c, a)))
        self.parts.setdefault(mat, []).append((a, b, c, n))

    def box(self, mat, center, size, top_scale=(1.0, 1.0), top_shift=(0.0, 0.0)):
        """Axis-aligned box; top_scale/top_shift make the top face smaller or offset it (sloped sides)."""
        cx, cy, cz = center; sx, sy, sz = size[0]/2, size[1]/2, size[2]/2
        tx, tz = sx*top_scale[0], sz*top_scale[1]
        ox, oz = top_shift
        bot = [(cx-sx, cy-sy, cz-sz), (cx-sx, cy-sy, cz+sz), (cx+sx, cy-sy, cz+sz), (cx+sx, cy-sy, cz-sz)]
        top = [(cx-tx+ox, cy+sy, cz-tz+oz), (cx-tx+ox, cy+sy, cz+tz+oz), (cx+tx+ox, cy+sy, cz+tz+oz), (cx+tx+ox, cy+sy, cz-tz+oz)]
        self.hexa(mat, bot + top)

    def limb(self, mat, start, end, w, d, side=(1, 0, 0), w2=None, d2=None):
        """A block running from start to end, w wide (along 'side') and d deep; w2/d2 taper the end."""
        w2 = w if w2 is None else w2; d2 = d if d2 is None else d2
        ax = norm(sub(end, start))
        sv = sub(side, mul(ax, dot(side, ax)))
        if dot(sv, sv) < 1e-6: sv = (0, 1, 0) if abs(ax[1]) < 0.9 else (0, 0, 1)
        sv = norm(sv); dv = cross(ax, sv)
        def ring(p, ww, dd):
            return [add(add(p, mul(sv, -ww/2)), mul(dv, -dd/2)), add(add(p, mul(sv, ww/2)), mul(dv, -dd/2)),
                    add(add(p, mul(sv, ww/2)), mul(dv, dd/2)), add(add(p, mul(sv, -ww/2)), mul(dv, dd/2))]
        self.hexa(mat, ring(start, w, d) + ring(end, w2, d2))

    def cylinder(self, mat, start, end, r, seg=8, r2=None, caps=True):
        r2 = r if r2 is None else r2
        ax = norm(sub(end, start))
        ref = (0, 1, 0) if abs(ax[1]) < 0.9 else (1, 0, 0)
        u = norm(cross(ax, ref)); v = cross(ax, u)
        def pt(c, rr, i):
            t = 2 * math.pi * (i + 0.5) / seg
            return add(c, add(mul(u, math.cos(t) * rr), mul(v, math.sin(t) * rr)))
        a = [pt(start, r, i) for i in range(seg)]; b = [pt(end, r2, i) for i in range(seg)]
        c = lerp(start, end, 0.5)
        for i in range(seg):
            j = (i + 1) % seg
            self.face(mat, [a[i], a[j], b[j], b[i]], c)
        if caps:
            self.face(mat, a, c)
            self.face(mat, b, c)

    def extrude_x(self, mat, profile, x0, x1):
        """A convex outline in the side view (pairs of z, y, going round in order) pushed out sideways from x0 to x1."""
        L = [(x0, y, z) for z, y in profile]; R = [(x1, y, z) for z, y in profile]
        n = len(profile)
        c = ((x0 + x1) / 2, sum(y for _, y in profile) / n, sum(z for z, _ in profile) / n)
        self.face(mat, R, c)
        self.face(mat, L, c)
        for i in range(n):
            j = (i + 1) % n
            self.face(mat, [L[i], L[j], R[j], R[i]], c)

    def face(self, mat, pts, centre):
        """A flat convex face, turned so it points away from 'centre' (the middle of the solid it belongs to).
        Every part here is convex, so this gets the winding right without working it out by hand."""
        n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
        if len(pts) > 3 and dot(n, n) < 1e-12: n = cross(sub(pts[2], pts[0]), sub(pts[3], pts[0]))
        mid = mul(pts[0], 0)
        for p in pts: mid = add(mid, mul(p, 1 / len(pts)))
        if dot(n, sub(mid, centre)) < 0: pts = list(reversed(pts))
        for i in range(1, len(pts) - 1): self.tri(mat, pts[0], pts[i], pts[i+1])

    def hexa(self, mat, c):
        """Any six-sided block from its 8 corners: c[0..3] one end, c[4..7] the other, in the same order."""
        centre = mul(c[0], 0)
        for p in c: centre = add(centre, mul(p, 1 / 8))
        for f in ((0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.face(mat, [c[i] for i in f], centre)


# ------------------------------------------------------------------ glTF writer
class Gltf:
    def __init__(self, materials):
        self.materials = materials  # name -> (r, g, b, roughness, metallic) in sRGB
        self.mat_index = {n: i for i, n in enumerate(materials)}
        self.bin = bytearray(); self.views = []; self.accessors = []; self.meshes = []; self.nodes = []

    def _view(self, data, target):
        while len(self.bin) % 4: self.bin += b'\0'
        self.views.append({"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data), "target": target})
        self.bin += data
        return len(self.views) - 1

    def add_mesh(self, name, mesh):
        prims = []
        for mat in sorted(mesh.parts, key=lambda m: self.mat_index[m]):
            pos, nrm = [], []
            for a, b, c, n in mesh.parts[mat]:
                pos += [a, b, c]; nrm += [n, n, n]
            flat = [x for p in pos for x in p]
            pv = self._view(struct.pack(f'<{len(flat)}f', *flat), 34962)
            nv = self._view(struct.pack(f'<{len(flat)}f', *[x for p in nrm for x in p]), 34962)
            mn = [min(p[k] for p in pos) for k in range(3)]; mx = [max(p[k] for p in pos) for k in range(3)]
            self.accessors.append({"bufferView": pv, "componentType": 5126, "count": len(pos), "type": "VEC3", "min": mn, "max": mx})
            self.accessors.append({"bufferView": nv, "componentType": 5126, "count": len(pos), "type": "VEC3"})
            prims.append({"attributes": {"POSITION": len(self.accessors) - 2, "NORMAL": len(self.accessors) - 1},
                          "material": self.mat_index[mat]})
        self.meshes.append({"name": name, "primitives": prims})
        return len(self.meshes) - 1

    def add_node(self, name, mesh=None, translation=None, children=None):
        n = {"name": name}
        if mesh is not None: n["mesh"] = mesh
        if translation: n["translation"] = list(translation)
        if children: n["children"] = children
        self.nodes.append(n)
        return len(self.nodes) - 1

    def write(self, path, roots):
        def lin(c): return ((c / 1.0) ** 2.2)
        mats = [{"name": n, "pbrMetallicRoughness": {"baseColorFactor": [lin(r), lin(g), lin(b), 1.0],
                 "roughnessFactor": rough, "metallicFactor": metal}}
                for n, (r, g, b, rough, metal) in self.materials.items()]
        doc = {"asset": {"version": "2.0", "generator": "Ground War tools/make_models.py"},
               "scene": 0, "scenes": [{"nodes": roots}], "nodes": self.nodes, "meshes": self.meshes,
               "materials": mats, "accessors": self.accessors, "bufferViews": self.views,
               "buffers": [{"byteLength": len(self.bin)}]}
        js = json.dumps(doc, separators=(',', ':')).encode()
        while len(js) % 4: js += b' '
        while len(self.bin) % 4: self.bin += b'\0'
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(self.bin)))
            f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942)); f.write(self.bin)

# ------------------------------------------------------------------ soldier
# "Team" is light grey here: the game multiplies it by the side's colour.
SOLDIER_MATS = {
    "Team":  (0.85, 0.85, 0.85, 0.9, 0.0),   # tunic, trousers, helmet
    "Skin":  (0.80, 0.62, 0.48, 0.8, 0.0),
    "Gear":  (0.30, 0.27, 0.20, 0.9, 0.0),   # boots, belt, webbing, pack
    "Wood":  (0.42, 0.26, 0.14, 0.7, 0.0),
    "Metal": (0.16, 0.16, 0.17, 0.5, 0.6),
}

def rifle(m, butt, muzzle, up=(0, 1, 0)):
    """A bolt-action rifle from butt to muzzle (about 1.1 m)."""
    side = norm(cross(norm(sub(muzzle, butt)), up))
    p = lambda t: lerp(butt, muzzle, t)
    m.limb("Wood", p(0.0), p(0.30), 0.045, 0.13, side, d2=0.06)     # stock: deep at the butt, narrow at the wrist
    m.limb("Metal", p(0.28), p(0.46), 0.05, 0.07, side)              # receiver and bolt
    m.limb("Wood", p(0.44), p(0.82), 0.045, 0.055, side)            # fore-stock
    m.limb("Metal", p(0.80), p(1.0), 0.022, 0.022, side)            # barrel
    m.limb("Metal", p(0.36), p(0.36 + 0.01), 0.10, 0.02, side)       # bolt handle sticks out to the right

def arm(m, shoulder, hand, bend):
    """Upper arm, forearm and hand. 'bend' pushes the elbow sideways/down from the straight line."""
    elbow = add(lerp(shoulder, hand, 0.5), bend)
    m.limb("Team", shoulder, elbow, 0.11, 0.11)
    m.limb("Team", elbow, lerp(elbow, hand, 0.85), 0.095, 0.095, w2=0.085, d2=0.085)
    m.limb("Skin", lerp(elbow, hand, 0.82), add(hand, mul(norm(sub(hand, elbow)), 0.04)), 0.075, 0.09)

def leg(m, hip, knee, ankle, toe):
    m.limb("Team", hip, knee, 0.15, 0.16, w2=0.12, d2=0.13)
    m.limb("Team", knee, lerp(knee, ankle, 0.8), 0.12, 0.13, w2=0.1, d2=0.11)
    m.limb("Gear", lerp(knee, ankle, 0.75), ankle, 0.11, 0.12)               # boot shaft / gaiter
    m.limb("Gear", ankle, toe, 0.11, 0.1, side=(1, 0, 0))                   # foot

def head_and_helmet(m, neck, head, fwd, up):
    side = norm(cross(up, fwd))
    m.limb("Skin", neck, lerp(neck, head, 0.6), 0.09, 0.09)
    hb = sub(head, mul(up, 0.11)); ht = add(head, mul(up, 0.1))
    m.limb("Skin", hb, ht, 0.17, 0.2, side=side, w2=0.16, d2=0.19)
    nose = add(add(head, mul(fwd, 0.1)), mul(up, -0.01))
    m.limb("Skin", nose, add(nose, mul(fwd, 0.03)), 0.04, 0.05, side=side)
    # Helmet: a wide brim and a low dome, the classic soup-bowl shape (reads well from above).
    brim = add(head, mul(up, 0.06))
    m.cylinder("Team", brim, add(brim, mul(up, 0.025)), 0.18, seg=10)
    m.cylinder("Team", add(brim, mul(up, 0.025)), add(brim, mul(up, 0.1)), 0.14, seg=10, r2=0.09)

def torso(m, hips, chest, fwd, pack=True):
    """Body from hips to shoulders, belt, pouches and a small pack on the back."""
    up = norm(sub(chest, hips)); side = norm(cross(up, fwd))
    m.limb("Team", hips, chest, 0.34, 0.22, side=side, w2=0.42, d2=0.24)
    belt = lerp(hips, chest, 0.1)
    m.limb("Gear", sub(belt, mul(up, 0.03)), add(belt, mul(up, 0.03)), 0.36, 0.24, side=side)
    for s in (-1, 1):  # ammo pouches on the belt, front
        pp = add(add(belt, mul(side, 0.1 * s)), mul(fwd, 0.13))
        m.limb("Gear", sub(pp, mul(up, 0.03)), add(pp, mul(up, 0.06)), 0.1, 0.05, side=side)
    # Webbing straps over the shoulders (front).
    for s in (-1, 1):
        a = add(add(belt, mul(side, 0.11 * s)), mul(fwd, 0.115))
        b = add(add(chest, mul(side, 0.12 * s)), mul(fwd, 0.105))
        m.limb("Gear", a, b, 0.04, 0.015, side=side)
    if pack:
        pc = add(lerp(hips, chest, 0.62), mul(fwd, -0.17))
        m.limb("Gear", sub(pc, mul(up, 0.15)), add(pc, mul(up, 0.13)), 0.3, 0.13, side=side, w2=0.28)
        roll = add(add(chest, mul(fwd, -0.12)), mul(up, 0.0))
        m.cylinder("Gear", add(roll, mul(side, -0.17)), add(roll, mul(side, 0.17)), 0.05, seg=6)

def soldier_standing():
    # Standing at the ready: rifle up, muzzle about 1.35 m high (the game draws tracers from there).
    m = Mesh(); fwd = (0, 0, 1); up = (0, 1, 0)
    for s in (-1, 1):
        leg(m, (0.1 * s, 0.94, 0.0), (0.12 * s, 0.5, 0.04), (0.13 * s, 0.1, 0.0), (0.13 * s, 0.05, 0.2))
    hips, chest = (0, 0.9, 0), (0, 1.45, 0.02)
    torso(m, hips, chest, fwd)
    head_and_helmet(m, (0, 1.45, 0.02), (0, 1.62, 0.04), fwd, up)
    butt, muzzle = (-0.17, 1.38, 0.0), (-0.06, 1.35, 1.1)
    rifle(m, butt, muzzle)
    arm(m, (-0.22, 1.4, 0.0), lerp(butt, muzzle, 0.3), (-0.08, -0.12, -0.02))   # right hand on the wrist of the stock
    arm(m, (0.22, 1.4, 0.0), lerp(butt, muzzle, 0.58), (0.1, -0.12, 0.0))       # left hand under the fore-stock
    return m

def soldier_crouched():
    # Kneeling on the right knee, rifle shouldered, muzzle about 0.95 m high.
    m = Mesh(); fwd = (0, 0, 1)
    leg(m, (0.11, 0.55, -0.02), (0.13, 0.52, 0.4), (0.13, 0.1, 0.36), (0.13, 0.05, 0.56))     # left: foot planted
    leg(m, (-0.11, 0.55, -0.02), (-0.13, 0.08, 0.06), (-0.13, 0.13, -0.38), (-0.13, 0.03, -0.5))  # right: knee down
    hips, chest = (0, 0.52, -0.02), (0, 1.03, 0.1)
    torso(m, hips, chest, fwd)
    head_and_helmet(m, (0, 1.03, 0.1), (0, 1.19, 0.14), fwd, (0, 1, 0))
    butt, muzzle = (-0.16, 0.99, 0.12), (-0.06, 0.95, 1.2)
    rifle(m, butt, muzzle)
    arm(m, (-0.22, 0.98, 0.1), lerp(butt, muzzle, 0.3), (-0.08, -0.12, -0.02))
    arm(m, (0.22, 0.98, 0.1), lerp(butt, muzzle, 0.58), (0.1, -0.14, 0.0))
    return m

def soldier_prone():
    # Lying on his front, head toward +Z, propped on the elbows. Muzzle about 0.3 m high.
    # Laid out around the soldier's position: head about 0.6 m ahead, feet about 1 m behind.
    m = Mesh()
    for s in (-1, 1):
        leg(m, (0.1 * s, 0.13, -0.15), (0.15 * s, 0.09, -0.6), (0.18 * s, 0.09, -1.0), (0.2 * s, 0.0, -1.06))
    hips, chest = (0, 0.14, -0.15), (0, 0.2, 0.38)
    torso(m, hips, chest, (0, -1, 0))      # chest to the ground, so the pack is on top
    head_and_helmet(m, (0, 0.22, 0.4), (0, 0.32, 0.55), (0, 0.15, 1), (0, 1, -0.15))
    butt, muzzle = (-0.13, 0.27, 0.42), (-0.05, 0.3, 1.5)
    rifle(m, butt, muzzle)
    arm(m, (-0.21, 0.22, 0.36), lerp(butt, muzzle, 0.25), (-0.08, -0.14, 0.0))
    arm(m, (0.21, 0.22, 0.36), lerp(butt, muzzle, 0.55), (0.12, -0.16, 0.0))
    return m

def soldier_dead():
    # Fallen: on his back, arms flung out, rifle dropped beside him.
    m = Mesh()
    leg(m, (0.1, 0.12, -0.15), (0.2, 0.1, -0.6), (0.3, 0.1, -1.0), (0.33, 0.2, -1.04))
    leg(m, (-0.1, 0.12, -0.15), (-0.12, 0.2, -0.55), (-0.14, 0.1, -0.98), (-0.17, 0.2, -1.03))
    hips, chest = (0, 0.12, -0.15), (0, 0.13, 0.38)
    torso(m, hips, chest, (0, 1, 0), pack=False)
    m.limb("Gear", (-0.15, 0.04, 0.0), (0.15, 0.04, 0.25), 0.25, 0.08)   # pack under him, squashed
    head_and_helmet(m, (0, 0.13, 0.4), (0.03, 0.13, 0.56), (0.1, 1, 0.1), (0, 0, 1))
    arm(m, (-0.21, 0.14, 0.36), (-0.75, 0.06, 0.5), (0, 0.04, 0.05))
    arm(m, (0.21, 0.14, 0.36), (0.55, 0.06, 0.0), (0.05, 0.04, 0.1))
    rifle(m, (-0.55, 0.035, -0.4), (-0.85, 0.035, 0.65), up=(1, 0, 0))
    return m

def make_soldier(path):
    g = Gltf(SOLDIER_MATS)
    roots = []
    for name, fn in (("Standing", soldier_standing), ("Crouched", soldier_crouched), ("Prone", soldier_prone), ("Dead", soldier_dead)):
        roots.append(g.add_node(name, g.add_mesh(name, fn())))
    g.write(path, roots)

# ------------------------------------------------------------------ tank
# Matches data/vehicles.json: 6 m long, 3 m wide. The turret pivot is 0.3 m behind the middle and the gun
# is about 1.95 m high, where the game starts shell tracers.
TANK_MATS = {
    "Team":   (0.80, 0.80, 0.76, 0.75, 0.15),   # painted armour
    "Track":  (0.16, 0.15, 0.14, 0.95, 0.3),
    "Wheel":  (0.30, 0.30, 0.28, 0.8, 0.3),
    "Metal":  (0.12, 0.12, 0.12, 0.45, 0.7),    # gun, MG, grilles
    "Gear":   (0.36, 0.32, 0.22, 0.95, 0.0),    # canvas stowage, wooden tool handles
}

def tank_hull():
    m = Mesh()
    L, W = 6.0, 3.0
    tw = 0.56                       # track width
    tx = W / 2 - tw / 2             # track centre line
    for s in (-1, 1):
        x0, x1 = s * tx - tw / 2, s * tx + tw / 2
        # Track run seen from the side: flat on the ground, rising at the sprocket (front) and idler (back).
        m.extrude_x("Track", [(-2.75, 0.03), (2.6, 0.03), (3.0, 0.5), (2.85, 0.82), (-2.85, 0.82), (-3.0, 0.5)], x0, x1)
        ox = s * (W / 2 + 0.01)     # outer face
        wx = s * (W / 2 + 0.06)
        for z in (-1.9, -0.95, 0.0, 0.95, 1.9):      # five road wheels
            m.cylinder("Wheel", (ox, 0.34, z), (wx, 0.34, z), 0.31, seg=10)
            m.cylinder("Metal", (wx, 0.34, z), (wx + s * 0.03, 0.34, z), 0.09, seg=6)
        m.cylinder("Wheel", (ox, 0.52, 2.72), (wx, 0.52, 2.72), 0.27, seg=10)    # drive sprocket (front)
        m.cylinder("Metal", (wx, 0.52, 2.72), (wx + s * 0.04, 0.52, 2.72), 0.12, seg=6)
        m.cylinder("Wheel", (ox, 0.48, -2.72), (wx, 0.48, -2.72), 0.25, seg=10)  # idler (back)
        for z in (-1.4, 0.45, 1.4):                    # return rollers
            m.cylinder("Wheel", (ox, 0.73, z), (wx - s * 0.02, 0.73, z), 0.07, seg=6)
        # Fender over the track, team painted, with a bent-down front.
        m.box("Team", (s * tx, 0.86, -0.05), (tw + 0.08, 0.05, 5.6))
        m.limb("Team", (s * tx, 0.86, 2.75), (s * tx, 0.68, 3.08), tw + 0.08, 0.05)
        # Side skirts / sponson wall above the track.
        m.box("Team", (s * (W / 2 - tw - 0.02), 1.08, -0.05), (0.08, 0.42, 5.4))
    # Lower hull between the tracks, sloped nose plate.
    m.extrude_x("Team", [(-2.8, 0.3), (2.6, 0.3), (2.95, 0.6), (2.95, 0.9), (-2.85, 0.9), (-2.85, 0.5)], -(W / 2 - tw), W / 2 - tw)
    # Upper hull: steep glacis at the front, flat deck, slightly sloped rear plate. Full width over the fenders.
    hw = W / 2 - 0.08
    m.extrude_x("Team", [(-2.85, 0.86), (2.95, 0.86), (2.95, 0.92), (2.05, 1.58), (-2.6, 1.58), (-2.9, 1.25)], -(W / 2 - tw - 0.06), W / 2 - tw - 0.06)
    # Raised sponsons over the inner half of each track.
    for s in (-1, 1):
        a, b = sorted((s * (W / 2 - tw - 0.06), s * (hw - 0.1)))
        m.extrude_x("Team", [(-2.7, 0.86), (2.3, 0.86), (1.75, 1.32), (-2.5, 1.32)], a, b)
    # Driver's visor (front left as the tank faces +Z: +X is the tank's left).
    m.limb("Metal", (0.45, 1.36, 2.36), (0.45, 1.46, 2.22), 0.36, 0.06)
    # Bow machine gun in a ball mount, front right.
    m.cylinder("Team", (-0.45, 1.34, 2.27), (-0.45, 1.38, 2.42), 0.13, seg=8, r2=0.1)
    m.cylinder("Metal", (-0.45, 1.38, 2.42), (-0.45, 1.4, 2.82), 0.025, seg=6)
    # Spare track links on the glacis, tow hooks, headlights.
    for x in (-0.95, 0.95):
        m.limb("Track", (x, 1.02, 2.88), (x, 1.3, 2.5), 0.38, 0.06)
        m.box("Metal", (x * 0.8, 0.6, 3.0), (0.12, 0.14, 0.14))
        m.cylinder("Metal", (x * 0.85, 1.05, 2.86), (x * 0.85, 1.1, 2.96), 0.07, seg=6)
    # Hatches for driver and bow gunner on the front of the deck.
    for x in (-0.5, 0.5):
        m.box("Team", (x, 1.6, 1.75), (0.5, 0.04, 0.42))
    # Engine deck: grilles and an access hatch at the back, exhausts on the rear plate.
    for z in (-1.75, -2.2):
        m.box("Metal", (0, 1.595, z), (1.5, 0.03, 0.3))
    m.box("Team", (0, 1.6, -1.3), (0.9, 0.04, 0.38))
    for x in (-0.6, 0.6):
        m.cylinder("Metal", (x, 1.15, -2.98), (x, 1.15, -3.18), 0.1, seg=8)
        m.cylinder("Metal", (x, 1.15, -3.18), (x, 1.4, -3.18), 0.07, seg=8)
    # Tools on the rear deck sides: a shovel and a pick handle.
    m.limb("Gear", (1.15, 1.6, -0.6), (1.15, 1.6, -1.8), 0.05, 0.05)
    m.limb("Metal", (1.15, 1.6, -1.8), (1.15, 1.6, -2.1), 0.18, 0.03)
    m.limb("Gear", (-1.15, 1.6, -0.5), (-1.15, 1.6, -1.6), 0.05, 0.05)
    # Turret ring.
    m.cylinder("Team", (0, 1.57, -0.3), (0, 1.63, -0.3), 0.95, seg=16)
    return m

def tank_turret():
    # Built around its own pivot (the centre of the turret ring), facing +Z.
    m = Mesh()
    m.box("Team", (0, 0.36, -0.05), (1.9, 0.7, 2.3), top_scale=(0.82, 0.8), top_shift=(0, -0.05))
    m.box("Team", (0, 0.36, -1.45), (1.5, 0.5, 0.6), top_scale=(0.95, 0.9))          # rear bustle
    m.box("Gear", (0, 0.42, -1.85), (1.4, 0.42, 0.25))                                # stowage bin / tarp
    m.box("Team", (0, 0.36, 1.15), (0.9, 0.55, 0.22))                                 # gun mantlet
    # Commander's cupola with vision slits, back left, and a hatch on top.
    cx, cz = 0.42, -0.45
    m.cylinder("Team", (cx, 0.7, cz), (cx, 0.98, cz), 0.32, seg=10, r2=0.3)
    m.cylinder("Metal", (cx, 0.85, cz), (cx, 0.9, cz), 0.31, seg=10)
    m.cylinder("Team", (cx, 0.98, cz), (cx, 1.03, cz), 0.27, seg=10)
    m.box("Team", (-0.4, 0.73, -0.3), (0.5, 0.05, 0.6))                               # loader's hatch
    # Pistol port / vision blocks on the sides.
    for s in (-1, 1):
        m.box("Metal", (s * 0.86, 0.45, 0.2), (0.05, 0.1, 0.25))
    # Smoke dischargers, front corners.
    for s in (-1, 1):
        for k in range(2):
            m.cylinder("Metal", (s * 0.8, 0.6 + 0.09 * k, 0.75), (s * 0.85, 0.66 + 0.09 * k, 0.95), 0.04, seg=6)
    # Radio aerial, back right.
    m.cylinder("Metal", (-0.6, 0.7, -1.1), (-0.6, 2.7, -1.1), 0.012, seg=4, caps=False)
    m.cylinder("Metal", (-0.6, 0.7, -1.1), (-0.6, 0.78, -1.1), 0.05, seg=6)
    return m

def tank_gun():
    # Built around the gun's trunnion (where it would pivot up and down), pointing along +Z.
    m = Mesh()
    m.cylinder("Metal", (0, 0, 0.1), (0, 0, 0.5), 0.13, seg=10, r2=0.11)      # recoil sleeve
    m.cylinder("Metal", (0, 0, 0.5), (0, 0, 3.1), 0.075, seg=10, r2=0.065)    # barrel
    m.cylinder("Metal", (0, 0, 3.1), (0, 0, 3.4), 0.1, seg=10)                # muzzle brake
    m.box("Metal", (0, 0, 3.25), (0.26, 0.08, 0.12))
    m.cylinder("Metal", (0.24, -0.02, 0.1), (0.24, -0.02, 0.62), 0.025, seg=6)  # coaxial machine gun
    return m

def make_tank(path):
    g = Gltf(TANK_MATS)
    gun = g.add_node("Gun", g.add_mesh("Gun", tank_gun()), translation=(0, 0.36, 1.1))
    turret = g.add_node("Turret", g.add_mesh("Turret", tank_turret()), translation=(0, 1.6, -0.3), children=[gun])
    hull = g.add_node("Hull", g.add_mesh("Hull", tank_hull()))
    root = g.add_node("Tank", children=[hull, turret])
    g.write(path, [root])

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "models")
    os.makedirs(out, exist_ok=True)
    make_soldier(os.path.join(out, "soldier.glb"))
    make_tank(os.path.join(out, "tank.glb"))
    print("Wrote models/soldier.glb and models/tank.glb")
