# Builds the summit camp's chalet in Blender and exports it to Assets/Summit/Resources/Camp/Chalet.fbx.
#   blender --background --python Tools/MakeChalet.py -- Assets/Summit/Resources/Camp/Chalet.fbx
#
# A Swiss chalet outside (honey timber, stone base, a broad low gable with deep eaves on carved stepped
# corbels, a plank balcony, small-paned windows with shutters, French doors), and a modern great room
# inside (double height, sunken lounge, a hanging black fireplace, floating oak stairs to a mezzanine,
# concrete, oak and black steel, warm LED strips). The game's stations live inside.
#
# Everything is authored in the game's own axes: x right, y up, z out of the front door (downhill).
# One mesh per material keeps it to a few draw calls. The game paints the materials itself, by name.
#
# Helper boxes (no material in the game, only their bounds are read):
#   COL_  a wall: blocks walking (at its heights) and the camera;  CAM_  the camera only (slabs, the roof)
#   BLK_  furniture: blocks walking only
#   FLR_  a floor you stand on: its top
#   RMP_  stairs: a floor whose top falls by its height from its -z end to its +z end
#   TAP_  a station's tap target;  SPOT_ where you stand to use it
#   FX_ / LIGHT_  where the game puts the fire and its lamps

import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Euler
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # (Tools/blendkit.py)
from blendkit import mat4, cube, cone

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "Chalet.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)

# game axes -> Blender axes: x stays, game up is Blender +Z, game z is Blender +Y. That is a mirror (Unity's
# axes are left-handed, Blender's right-handed, and Unity's FBX import maps Blender y straight to its z), so
# every face is also turned round to keep it facing out. The game then gets its own axes back exactly.
TOB = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

TILE = {"Timber": 1.6, "TimberDark": 1.6, "Stone": 1.2, "Roof": 1.5, "Oak": 1.4, "Concrete": 3.0, "Plaster": 4.0}
geo = {}
tris = 0


def mesh_for(mat):
    if mat not in geo:
        b = bmesh.new()
        b.loops.layers.uv.new("UVMap")
        geo[mat] = b
    return geo[mat]


def add(src, mat, m, full_uv=False):
    """Copy a temp bmesh into the material's mesh through matrix m (game space), with box-projected UVs."""
    global tris
    dst = mesh_for(mat)
    uv = dst.loops.layers.uv.active
    xf = TOB @ m
    vmap = {}
    for v in src.verts:
        vmap[v] = dst.verts.new(xf @ v.co)
    src_uv = src.loops.layers.uv.active
    tile = TILE.get(mat, 1.0)
    for f in src.faces:
        nf = dst.faces.new([vmap[v] for v in reversed(f.verts)])   # reversed: the mirror above
        nf.normal_update()
        n = nf.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l, sl in zip(nf.loops, reversed(f.loops)):
            p = l.vert.co
            if full_uv and src_uv is not None:
                l[uv].uv = sl[src_uv].uv
            elif ax == 2:
                l[uv].uv = (p.x / tile, p.y / tile)
            elif ax == 0:
                l[uv].uv = (p.y / tile, p.z / tile)
            else:
                l[uv].uv = (p.x / tile, p.z / tile)
        tris += len(f.verts) - 2
    src.free()




def box(mat, pos, size, rot=(0, 0, 0)):
    add(cube(), mat, mat4(pos, size, rot))


def span(mat, a, b_, t):
    """A beam of square section t from point a to point b."""
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    add(cube(), mat, Matrix.Translation((a + b_) / 2) @ rot @ Matrix.Diagonal(Vector((t, t, d.length, 1))))


def cyl(mat, pos, r, h, seg=12, r2=None, rot=(0, 0, 0)):
    """A cylinder (or cone) standing on pos along the game's up axis."""
    b = cone(r, h, seg, r2)
    up = Matrix.Rotation(math.radians(-90), 4, "X")  # its Z along the game's y
    add(b, mat, mat4(pos, (1, 1, 1), rot) @ Matrix.Translation((0, h / 2, 0)) @ up)


def wedge(mat, y0, h, hw, z, th):
    """A triangle (base 2*hw at height y0, apex h above), th thick, centred on z: a gable's inner face."""
    b = bmesh.new()
    pts = [(-hw, y0), (hw, y0), (0, y0 + h)]
    f = [b.verts.new((x, y, z - th / 2)) for x, y in pts]
    k = [b.verts.new((x, y, z + th / 2)) for x, y in pts]
    b.faces.new(f); b.faces.new(k)
    for i in range(3):
        j = (i + 1) % 3
        b.faces.new((f[i], f[j], k[j], k[i]))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    add(b, mat, Matrix.Identity(4))


def sphere(mat, pos, size, seg=10):
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=seg, v_segments=max(4, seg // 2), radius=0.5)
    add(b, mat, mat4(pos, size))


def quad(mat, pos, w, h, yaw=0):
    """A sign or picture: a flat card facing the game's +z (turned by yaw), its texture filling it."""
    b = bmesh.new()
    uv = b.loops.layers.uv.new("UVMap")
    vs = [b.verts.new(c) for c in ((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0))]
    f = b.faces.new(vs)
    for l, c in zip(f.loops, ((1, 0), (0, 0), (0, 1), (1, 1))):   # u runs right to left: the mirror turns it back
        l[uv].uv = c
    add(b, mat, mat4(pos, (w, h, 1), (0, yaw, 0)), full_uv=True)


helpers = []


def helper(name, lo, hi):
    """A named box between corners lo and hi (game space) that only carries its bounds to the game."""
    c = [(lo[i] + hi[i]) / 2 for i in range(3)]
    s = [max(0.02, abs(hi[i] - lo[i])) for i in range(3)]
    helpers.append((name, c, s))


def marker(name, pos):
    helpers.append((name, pos, (0.1, 0.1, 0.1)))


# ---------------------------------------------------------------- the shell

W, D = 8.0, 6.0            # half width (x) and half depth (z), outer faces of the walls
T = 0.3                    # wall thickness
EAVE = 6.4                 # top of the walls
PITCH = 24.0
RISE = (W + 1.6) * math.tan(math.radians(PITCH))
RIDGE = EAVE + W * math.tan(math.radians(PITCH))
MEZZ = 3.6                 # mezzanine floor height
MEZZ_EDGE = -0.5           # it covers the back of the house up to here

# stone base, running well down into the snow (the game sets the house on the highest snow under it)
# (a ring under the walls, its top just under the floor: hollow, so the sunken lounge sits inside it)
for s in (-1, 1):
    box("Stone", (0, -1.17, s * (D - 0.3)), (2 * W + 0.16, 2.2, 0.76))
    box("Stone", (s * (W - 0.3), -1.17, 0), (0.76, 2.2, 2 * D + 0.16))


def wall(axis, fixed, lo, hi, y0, y1, holes, inner_sign):
    """A timber wall along x (axis 'x', at z=fixed) or z (axis 'z', at x=fixed), lined with plaster inside,
    with rectangular holes [(a0, a1, b0, b1)] (a along the wall, b up)."""
    cuts = sorted({lo, hi} | {h[0] for h in holes} | {h[1] for h in holes})
    for a0, a1 in zip(cuts, cuts[1:]):
        if a1 - a0 < 1e-3:
            continue
        mid = (a0 + a1) / 2
        over = [h for h in holes if h[0] <= mid <= h[1]]
        ys = [(y0, y1)]
        for h in over:
            nys = []
            for (s0, s1) in ys:
                if h[3] <= s0 or h[2] >= s1:
                    nys.append((s0, s1))
                    continue
                if h[2] > s0: nys.append((s0, h[2]))
                if h[3] < s1: nys.append((h[3], s1))
            ys = nys
        for (s0, s1) in ys:
            if s1 - s0 < 1e-3:
                continue
            for mat, th, off in (("Timber", T - 0.04, -0.02), ("Plaster", 0.04, (T - 0.04) / 2)):
                if axis == "x":
                    box(mat, (mid, (s0 + s1) / 2, fixed + inner_sign * (T / 2 + off)), (a1 - a0, s1 - s0, th))
                else:
                    box(mat, (fixed + inner_sign * (T / 2 + off), (s0 + s1) / 2, mid), (th, s1 - s0, a1 - a0))


def window(axis, fixed, a, b, w, h, outward, shutters=True, cols=3, rows=3, door=False):
    """Frame, small panes and (outside) a pair of shutters, in a hole centred at (a along the wall, b up)."""
    def P(u, y, depth):  # u along the wall, depth outward from the wall's centre plane
        return (u, y, fixed + outward * depth) if axis == "x" else (fixed + outward * depth, y, u)
    def S(su, sy, sd):
        return (su, sy, sd) if axis == "x" else (sd, sy, su)
    f = 0.09
    for (cu, cy, su, sy) in ((a, b + h / 2 - f / 2, w, f), (a, b - h / 2 + f / 2, w, f), (a - w / 2 + f / 2, b, f, h), (a + w / 2 - f / 2, b, f, h)):
        box("TimberDark", P(cu, cy, 0.02), S(su, sy, T + 0.06))
    for i in range(1, cols):
        box("TimberDark", P(a - w / 2 + w * i / cols, b, 0.05), S(0.04, h - 2 * f, 0.06))
    for j in range(1, rows):
        box("TimberDark", P(a, b - h / 2 + h * j / rows, 0.05), S(w - 2 * f, 0.04, 0.06))
    box("Glass", P(a, b, 0.0), S(w - 2 * f, h - 2 * f, 0.02))
    if not door:
        box("TimberDark", P(a, b - h / 2 - 0.05, T / 2 + 0.06), S(w + 0.2, 0.08, 0.2))   # the sill
    if shutters:
        for s in (-1, 1):
            u = a + s * (w / 2 + w / 4 + 0.06)
            box("TimberDark", P(u, b, T / 2 + 0.05), S(w / 2, h, 0.05))
            for k in range(3):
                box("Timber", P(u, b - h / 2 + 0.25 + k * (h - 0.5) / 2, T / 2 + 0.09), S(w / 2 - 0.06, 0.07, 0.03))


# front (z = +D): French doors across the ground floor, the middle pair open; windows above the balcony
doors = [-5.6, -2.8, 0.0, 2.8, 5.6]
DW, DH = 2.1, 2.7
front_holes = [(x - DW / 2, x + DW / 2, 0, DH) for x in doors]
up_wins = [-4.6, 0.0, 4.6]
UW, UH, UY = 2.6, 1.8, 4.95
front_holes += [(x - UW / 2, x + UW / 2, UY - UH / 2, UY + UH / 2) for x in up_wins]
wall("x", D - T / 2, -W, W, 0, EAVE, front_holes, -1)
for x in doors:
    if x == 0.0:
        # the entrance: the frame, two leaves swung open inside
        window("x", D - T / 2, x, DH / 2, DW, DH, 1, shutters=False, door=True, cols=1, rows=1)
        continue
    window("x", D - T / 2, x, DH / 2, DW, DH, 1, shutters=False, cols=4, rows=4, door=True)
for s in (-1, 1):
    hx = s * (DW / 2 - 0.04)
    lw = DW / 2 - 0.05
    cx, cz = hx - s * 0.04, D - T - lw / 2
    box("TimberDark", (cx, DH / 2, cz), (0.06, DH - 0.1, lw))
    box("Glass", (cx, DH / 2, cz), (0.02, DH - 0.4, lw - 0.2))
for x in up_wins:
    window("x", D - T / 2, x, UY, UW, UH, 1, cols=4, rows=3)

# back (z = -D)
back_holes = [(x - 0.8, x + 0.8, 1.0, 2.4) for x in (-4.5, 4.5)] + [(x - 0.8, x + 0.8, 4.3, 5.6) for x in (-4.5, 4.5)]
wall("x", -D + T / 2, -W, W, 0, EAVE, back_holes, 1)
for x in (-4.5, 4.5):
    window("x", -D + T / 2, x, 1.7, 1.6, 1.4, -1)
    window("x", -D + T / 2, x, 4.95, 1.6, 1.3, -1)

# sides (x = ±W)
side_holes = [(z - 0.8, z + 0.8, 0.9, 2.3) for z in (3.2, -3.0)] + [(z - 0.8, z + 0.8, 4.3, 5.6) for z in (2.6, -3.0)]
for s in (-1, 1):
    wall("z", s * (W - T / 2), -D, D, 0, EAVE, side_holes, -s)
    for z in (3.2, -3.0):
        window("z", s * (W - T / 2), z, 1.6, 1.6, 1.4, s)
    for z in (2.6, -3.0):
        window("z", s * (W - T / 2), z, 4.95, 1.6, 1.3, s)

# the gables: stacked boards narrowing to the ridge, an attic window with shutters in the front one
gh = RIDGE - EAVE
y = EAVE
while y < RIDGE - 0.05:
    h = min(0.3, RIDGE - y)
    w = 2 * W * (1 - (y + h / 2 - EAVE) / gh)
    for zz, sgn in ((D - T / 2, 1), (-D + T / 2, -1)):
        if sgn > 0 and EAVE + 0.5 < y + h / 2 < EAVE + 1.9:
            for s in (-1, 1):  # around the attic window
                a0, a1 = 0.75, w / 2
                if a1 > a0:
                    box("Timber", (s * (a0 + a1) / 2, y + h / 2, zz), ((a1 - a0), h - 0.02, T))
        else:
            box("Timber", (0, y + h / 2, zz), (w, h - 0.02, T))
    y += h
window("x", D - T / 2, 0, EAVE + 1.2, 1.3, 1.25, 1, cols=2, rows=2)
# inside, the gables are smooth plaster (the boards' stepped ends hide under the roof)
wedge("Plaster", EAVE, gh - 0.25, W - T, -D + T + 0.03, 0.04)
for s in (-1, 1):   # the front one round the attic window
    wedge("Plaster", EAVE + 1.9, gh - 2.15, (W - T) * (1 - 1.9 / gh), D - T - 0.03, 0.04)
box("Plaster", (0, EAVE + 0.27, D - T - 0.03), (2 * (W - T) * (1 - 0.54 / gh), 0.5, 0.04))
for s in (-1, 1):
    hw = (W - T) * (1 - 1.9 / gh)
    box("Plaster", (s * (0.75 + hw) / 2, EAVE + 1.2, D - T - 0.03), (hw - 0.75, 1.4, 0.04))

# carved trim bands: a row of small blocks under the balcony and across the gable's foot
for yy, zz in ((3.18, D + 0.05), (EAVE - 0.12, D + 0.05)):
    box("TimberDark", (0, yy + 0.09, zz), (2 * W + 0.2, 0.06, 0.12))
    n = int(2 * W / 0.24)
    for i in range(n):
        box("Timber", (-W + 0.12 + i * 0.24, yy, zz + 0.02), (0.12, 0.12, 0.12))

# ---------------------------------------------------------------- the roof: broad, low, deep eaves

OV_SIDE, OV_FRONT, OV_BACK = 1.6, 2.6, 1.4
for s in (-1, 1):
    run = W + OV_SIDE
    ang = math.radians(PITCH)
    mid_x = s * run / 2
    mid_y = RIDGE + 0.25 - (run / 2) * math.tan(ang)
    depth = 2 * D + OV_FRONT + OV_BACK
    mz = (OV_FRONT - OV_BACK) / 2
    L = run / math.cos(ang)
    box("Roof", (mid_x, mid_y, mz), (L, 0.22, depth), (0, 0, -s * PITCH))
    box("Oak", (mid_x, mid_y - 0.14, mz), (L, 0.04, depth - 0.1), (0, 0, -s * PITCH))   # boarded inside
    # fascia boards along the eave and the gable edge
    ex = s * run
    ey = RIDGE + 0.25 - run * math.tan(ang)
    box("Timber", (ex, ey - 0.1, mz), (0.08, 0.32, depth))
    box("Timber", (mid_x, mid_y - 0.02, D + OV_FRONT), (L, 0.3, 0.08), (0, 0, -s * PITCH))
    # roof shingles: rows of slightly raised boards
    for k in range(1, 9):
        u = k / 9
        box("Roof", (s * run * u, RIDGE + 0.25 - run * u * math.tan(ang) + 0.12, mz), (0.2, 0.05, depth), (0, 0, -s * PITCH))
box("Roof", (0, RIDGE + 0.33, (OV_FRONT - OV_BACK) / 2), (0.5, 0.12, 2 * D + OV_FRONT + OV_BACK))   # ridge cap

# purlins under the roof, running out over the front with stepped carved corbels below their ends
for px in (-6.4, -3.2, 0.0, 3.2, 6.4):
    yy = RIDGE - abs(px) * math.tan(math.radians(PITCH)) - 0.05
    span("TimberDark", (px, yy, -D - OV_BACK + 0.2), (px, yy, D + OV_FRONT - 0.15), 0.26)
    # three stepped blocks, each shorter, under the end where it leaves the wall
    for k, (ln, hh) in enumerate(((1.7, 0.24), (1.15, 0.22), (0.6, 0.2))):
        yk = yy - 0.25 - k * 0.22
        if yk < EAVE - 0.2 and abs(px) > 1:
            continue
        box("Timber", (px, yk, D + ln / 2), (0.24, hh, ln))
        box("TimberDark", (px, yk - hh / 2 + 0.03, D + ln - 0.05), (0.26, 0.06, 0.1))  # the carved lip

# ---------------------------------------------------------------- the balcony across the first floor

BY = 3.3
BD = 1.7
box("Timber", (0, BY, D + BD / 2), (2 * W - 0.4, 0.16, BD))
for x in (-7.4, -4.4, -1.4, 1.4, 4.4, 7.4):
    box("TimberDark", (x, BY - 0.25, D + BD / 2), (0.22, 0.32, BD + 0.1))
    span("TimberDark", (x, 2.75, D + 0.1), (x, BY - 0.35, D + BD - 0.3), 0.14)
# plank balustrade: a solid panel of vertical boards, a cap rail
for (x0, x1, zz) in ((-W + 0.2, W - 0.2, D + BD - 0.05),):
    box("Timber", ((x0 + x1) / 2, BY + 0.6, zz), (x1 - x0, 1.0, 0.06))
    n = int((x1 - x0) / 0.32)
    for i in range(n + 1):
        box("TimberDark", (x0 + i * (x1 - x0) / n, BY + 0.6, zz + 0.04), (0.04, 1.0, 0.03))
    box("TimberDark", ((x0 + x1) / 2, BY + 1.13, zz), (x1 - x0 + 0.1, 0.08, 0.16))
for s in (-1, 1):
    box("Timber", (s * (W - 0.25), BY + 0.6, D + BD / 2), (0.06, 1.0, BD))
    box("TimberDark", (s * (W - 0.25), BY + 1.13, D + BD / 2), (0.16, 0.08, BD))
quad("SignCamp", (-4.6, BY + 0.62, D + BD - 0.005), 3.4, 0.85)

# ---------------------------------------------------------------- inside: floors

IX, IZ = W - T, D - T      # inner faces
LOUNGE = (1.8, 6.6, 0.9, 4.7)   # x0, x1, z0, z1 of the sunken lounge
SUNK = -0.4
# polished concrete floor, in pieces round the lounge
for (x0, x1, z0, z1) in ((-IX, LOUNGE[0], -IZ, IZ), (LOUNGE[1], IX, -IZ, IZ), (LOUNGE[0], LOUNGE[1], -IZ, LOUNGE[2]), (LOUNGE[0], LOUNGE[1], LOUNGE[3], IZ)):
    box("Concrete", ((x0 + x1) / 2, -0.15, (z0 + z1) / 2), (x1 - x0, 0.3, z1 - z0))
    helper("FLR_floor", (x0, -0.3, z0), (x1, 0.0, z1))
box("Concrete", ((LOUNGE[0] + LOUNGE[1]) / 2, SUNK - 0.15, (LOUNGE[2] + LOUNGE[3]) / 2), (LOUNGE[1] - LOUNGE[0], 0.3, LOUNGE[3] - LOUNGE[2]))
helper("FLR_lounge", (LOUNGE[0], SUNK - 0.3, LOUNGE[2]), (LOUNGE[1], SUNK, LOUNGE[3]))
# the lounge's step all round, oak-edged
for (cx, cz, sx, sz) in (((LOUNGE[0] + LOUNGE[1]) / 2, LOUNGE[2], LOUNGE[1] - LOUNGE[0], 0.04), ((LOUNGE[0] + LOUNGE[1]) / 2, LOUNGE[3], LOUNGE[1] - LOUNGE[0], 0.04),
                         (LOUNGE[0], (LOUNGE[2] + LOUNGE[3]) / 2, 0.04, LOUNGE[3] - LOUNGE[2]), (LOUNGE[1], (LOUNGE[2] + LOUNGE[3]) / 2, 0.04, LOUNGE[3] - LOUNGE[2])):
    box("Oak", (cx, SUNK / 2, cz), (sx, -SUNK, sz))
    box("LED", (cx, -0.03, cz), (max(sx, 0.02) if sx > 0.05 else 0.02, 0.02, max(sz, 0.02) if sz > 0.05 else 0.02))

# rugs
box("Rug", (4.2, SUNK + 0.01, 2.8), (3.6, 0.02, 2.6))
box("Rug", (-2.6, 0.01, 3.2), (3.4, 0.02, 2.4))

# mezzanine: a concrete slab over the back of the house, oak on top, a warm strip under its edge
# (full width: the stairs land on it in the left corner)
box("Concrete", (0, MEZZ - 0.18, (-IZ + MEZZ_EDGE) / 2), (2 * IX, 0.36, MEZZ_EDGE + IZ))
box("Oak", (0, MEZZ - 0.01, (-IZ + MEZZ_EDGE) / 2), (2 * IX, 0.02, MEZZ_EDGE + IZ))
box("LED", (0.6, MEZZ - 0.37, MEZZ_EDGE - 0.02), (2 * IX - 1.3, 0.03, 0.05))
helper("FLR_mezz", (-IX, MEZZ - 0.36, -IZ), (IX, MEZZ, MEZZ_EDGE))
helper("CAM_mezzslab", (-IX, MEZZ - 0.36, -IZ), (IX, MEZZ - 0.01, MEZZ_EDGE))
# glass balustrade along its edge, a black steel rail on top
box("Glass", (0.6, MEZZ + 0.5, MEZZ_EDGE), (2 * IX - 1.2, 1.0, 0.03))
box("Steel", (0.6, MEZZ + 1.02, MEZZ_EDGE), (2 * IX - 1.2, 0.05, 0.07))
helper("BLK_mezzrail", (-IX + 1.2, MEZZ, MEZZ_EDGE - 0.06), (IX, MEZZ + 1.1, MEZZ_EDGE + 0.06))

# floating oak stairs up the left wall, a glass balustrade on the open side
SX0, SX1 = -IX, -IX + 1.15
Z_BOT, Z_TOP = 3.3, MEZZ_EDGE
N = 12
for i in range(N):
    zt = Z_BOT - (i + 0.5) * (Z_BOT - Z_TOP) / N
    yt = (i + 1) * MEZZ / N
    box("Oak", ((SX0 + SX1) / 2, yt - 0.04, zt), (SX1 - SX0, 0.08, (Z_BOT - Z_TOP) / N + 0.04))
    box("LED", ((SX0 + SX1) / 2, yt - 0.085, zt + 0.08), (SX1 - SX0 - 0.1, 0.01, 0.03))
helper("RMP_stairs", (SX0, 0.0, Z_TOP), (SX1, MEZZ, Z_BOT))
# glass on the open side: one sloped pane and a steel handrail
mid = Vector(((SX1 + 0.02), MEZZ / 2 + 0.5, (Z_BOT + Z_TOP) / 2))
ln = math.hypot(MEZZ, Z_BOT - Z_TOP)
ang = math.degrees(math.atan2(MEZZ, Z_BOT - Z_TOP))
box("Glass", tuple(mid), (0.03, 1.0, ln), (ang, 0, 0))
span("Steel", (SX1 + 0.02, 1.05, Z_BOT), (SX1 + 0.02, MEZZ + 1.05, Z_TOP), 0.05)
helper("BLK_stairrail", (SX1 - 0.02, 0.0, Z_TOP), (SX1 + 0.08, MEZZ + 1.1, Z_BOT))
# the space under the high end of the stairs: closed off to people on the floor (low, so it never
# catches someone climbing, whose feet are already above it there)
helper("BLK_understair", (SX0, 0.0, Z_TOP), (SX1, 1.5, 1.0))

# ---------------------------------------------------------------- the great room

# a low modular sofa round two sides of the sunken lounge
FAB = "Fabric"
box(FAB, (4.2, SUNK + 0.2, LOUNGE[2] + 0.45), (4.4, 0.4, 0.9))
box(FAB, (4.2, SUNK + 0.55, LOUNGE[2] + 0.12), (4.4, 0.5, 0.24))
box(FAB, (LOUNGE[1] - 0.45, SUNK + 0.2, 2.8), (0.9, 0.4, 2.6))
box(FAB, (LOUNGE[1] - 0.12, SUNK + 0.55, 2.8), (0.24, 0.5, 2.6))
for x in (2.6, 3.6, 4.6, 5.6):
    box("Rug", (x, SUNK + 0.47, LOUNGE[2] + 0.5), (0.9, 0.12, 0.7))   # cushions
helper("BLK_sofa1", (1.9, SUNK, LOUNGE[2]), (6.5, SUNK + 0.8, LOUNGE[2] + 0.95))
helper("BLK_sofa2", (LOUNGE[1] - 0.95, SUNK, 1.5), (LOUNGE[1], SUNK + 0.8, 4.1))
# coffee table: oak slab on black steel
box("Oak", (3.9, SUNK + 0.36, 2.9), (1.6, 0.06, 0.9))
box("Steel", (3.9, SUNK + 0.17, 2.9), (1.4, 0.32, 0.06))
helper("BLK_table", (3.1, SUNK, 2.45), (4.7, SUNK + 0.4, 3.35))

# the hanging black fireplace: a steel bowl on its flue, glowing inside
FX, FZ = 1.0, 2.6
cyl("Steel", (FX, 0.0, FZ), 0.75, 0.06, 24)                    # hearth plate
cyl("Steel", (FX, 0.75, FZ), 0.32, 0.55, 20, r2=0.62)           # the bowl, opening upward
cyl("Ember", (FX, 1.05, FZ), 0.5, 0.06, 16)
cyl("Steel", (FX, 1.3, FZ), 0.62, 0.7, 20, r2=0.18)             # the hood
cyl("Steel", (FX, 2.0, FZ), 0.12, RIDGE - 2.2, 10)              # flue to the roof
marker("FX_fire", (FX, 1.1, FZ))
marker("LIGHT_fire", (FX, 1.6, FZ + 0.6))
helper("BLK_fire", (FX - 0.8, 0.0, FZ - 0.8), (FX + 0.8, 2.0, FZ + 0.8))
helper("CAM_flue", (FX - 0.7, 0.6, FZ - 0.7), (FX + 0.7, RIDGE, FZ + 0.7))   # keeps the camera from sitting behind the flue

# designer pendants: a cluster of globes over the lounge, hung from the roof on black cords
for (px, py, pz) in ((3.4, 3.6, 2.4), (4.4, 3.2, 3.2), (5.2, 3.9, 2.2), (3.9, 4.2, 3.6), (4.9, 3.5, 1.6)):
    span("Steel", (px, py + 0.2, pz), (px, RIDGE - 0.6 - abs(px) * 0.4, pz), 0.015)
    sphere("LED", (px, py, pz), (0.32, 0.32, 0.32), 12)
marker("LIGHT_lounge", (4.3, 4.6, 2.8))

# art: two big canvases high on the left wall, one on the mezzanine back wall
for (cz, mat) in ((3.4, "ArtA"), (0.4, "ArtB")):
    box("Steel", (-IX + 0.04, 4.6, cz), (0.04, 1.9, 2.4))
    box(mat, (-IX + 0.07, 4.6, cz), (0.03, 1.8, 2.3))
box("Steel", (0.5, MEZZ + 1.6, -IZ + 0.04), (2.2, 1.4, 0.04))
box("ArtA", (0.5, MEZZ + 1.6, -IZ + 0.07), (2.1, 1.3, 0.03))
# warm cove lights high along the side walls
for s in (-1, 1):
    box("LED", (s * (IX - 0.06), EAVE - 0.4, 0), (0.03, 0.04, 2 * IZ - 0.4))
    box("Oak", (s * (IX - 0.12), EAVE - 0.34, 0), (0.12, 0.06, 2 * IZ - 0.3))

# plants in concrete pots
for (px, pz, hgt) in ((-6.6, 5.0, 1.4), (7.0, 5.0, 1.1), (-1.2, -1.6, 0.9), (6.9, -5.0, 1.0)):
    cyl("Concrete", (px, 0.0 if pz > -5.5 else MEZZ, pz), 0.28, 0.5, 14, r2=0.32)
    for k in range(4):
        sphere("Plant", (px + 0.12 * math.cos(k * 2.1), 0.55 + hgt * 0.35 * (k + 1) / 4, pz + 0.12 * math.sin(k * 2.1)), (0.55, 0.6, 0.55), 8)
    helper("BLK_plant", (px - 0.35, 0.0, pz - 0.35), (px + 0.35, 1.5, pz + 0.35))

# kitchen under the mezzanine: oak cabinets and a concrete worktop along the back wall, a long island
box("Oak", (-0.2, 0.45, -IZ + 0.33), (6.0, 0.9, 0.66))
box("Concrete", (-0.2, 0.93, -IZ + 0.33), (6.1, 0.06, 0.7))
box("Steel", (-0.2, 2.0, -IZ + 0.18), (6.0, 0.05, 0.36))     # a black shelf
box("LED", (-0.2, 1.96, -IZ + 0.3), (5.8, 0.02, 0.04))
helper("BLK_kitchen", (-3.2, 0.0, -IZ), (2.8, 1.0, -IZ + 0.7))
box("Oak", (-0.2, 0.45, -3.0), (4.0, 0.9, 1.0))
box("Concrete", (-0.2, 0.93, -3.0), (4.3, 0.06, 1.15))
box("Steel", (-0.2, 0.06, -3.0), (4.05, 0.12, 1.05))
helper("BLK_island", (-2.4, 0.0, -3.6), (2.0, 1.0, -2.4))
for x in (-1.6, -0.2, 1.2):   # stools
    cyl("Steel", (x, 0.0, -2.05), 0.03, 0.72, 6)
    cyl("Oak", (x, 0.72, -2.05), 0.2, 0.05, 14)
    cyl("Steel", (x, 0.0, -2.05), 0.2, 0.02, 14)
# pendants over the island, hanging from the mezzanine
for x in (-1.6, -0.2, 1.2):
    span("Steel", (x, 2.3, -3.0), (x, MEZZ - 0.36, -3.0), 0.012)
    cyl("Steel", (x, 2.05, -3.0), 0.22, 0.25, 14, r2=0.04)
    sphere("LED", (x, 2.05, -3.0), (0.14, 0.14, 0.14), 8)
marker("LIGHT_kitchen", (-0.2, 2.6, -2.8))
helper("CAM_pendants", (-2.0, 1.7, -3.4), (1.6, MEZZ - 0.36, -2.6))   # the camera stays out of the pendants

# ---------------------------------------------------------------- the stations

# daily missions: a black steel frame on the right wall under the mezzanine, oak panel, three cards
MX = IX - 0.03
box("Steel", (MX, 1.75, -3.0), (0.06, 1.7, 3.2))
box("Oak", (MX - 0.04, 1.75, -3.0), (0.03, 1.55, 3.05))
for i, z in enumerate((-4.0, -3.0, -2.0)):
    quad("Note%d" % i, (MX - 0.06, 1.72, z), 0.62, 0.78, -90)
quad("SignMissions", (MX - 0.06, 2.92, -3.0), 2.6, 0.65, -90)
box("LED", (MX - 0.1, 2.62, -3.0), (0.03, 0.02, 3.0))
helper("TAP_Missions", (MX - 0.3, 0.6, -4.7), (IX, 3.2, -1.3))
helper("BLK_missions", (MX - 0.15, 0.0, -4.7), (IX, 2.6, -1.3))
marker("SPOT_Missions", (MX - 1.6, 0.0, -3.0))

# tune-up bench on the mezzanine: a black steel bench, oak top, the board clamped on it, tools on a rail
BZ = -IZ + 0.5
box("Steel", (4.6, MEZZ + 0.45, BZ), (3.2, 0.9, 0.8))
box("Oak", (4.6, MEZZ + 0.93, BZ), (3.3, 0.06, 0.9))
box("BoardPaint", (4.3, MEZZ + 1.0, BZ), (1.45, 0.03, 0.3))
for s in (-1, 1):
    cyl("BoardPaint", (4.3 + s * 0.72, MEZZ + 0.985, BZ), 0.15, 0.03, 12)
    box("Steel", (4.3 + s * 0.6, MEZZ + 1.04, BZ), (0.1, 0.1, 0.36))
box("Steel", (5.7, MEZZ + 1.02, BZ - 0.1), (0.3, 0.1, 0.16))   # wax iron
box("Steel", (4.6, MEZZ + 1.9, -IZ + 0.05), (3.2, 0.04, 0.06))  # tool rail
for k in range(6):
    box("Steel", (3.3 + k * 0.5, MEZZ + 1.65, -IZ + 0.08), (0.05, 0.45, 0.03))
quad("SignUpgrades", (4.6, MEZZ + 2.45, -IZ + 0.04), 2.8, 0.7)
box("LED", (4.6, MEZZ + 2.1, -IZ + 0.12), (3.0, 0.02, 0.03))
helper("TAP_Upgrades", (2.9, MEZZ, -IZ), (6.3, MEZZ + 2.8, BZ + 0.6))
helper("BLK_bench", (2.9, MEZZ, -IZ), (6.3, MEZZ + 1.2, BZ + 0.45))
marker("SPOT_Upgrades", (4.6, MEZZ, -3.3))
marker("LIGHT_mezz", (2.0, MEZZ + 2.4, -3.0))

# outfits on the mezzanine: a wall rack of boards, a steel rail with jackets, a long mirror
RX = -3.3
box("Steel", (RX, MEZZ + 0.9, -IZ + 0.1), (2.9, 0.05, 0.2))
box("Steel", (RX, MEZZ + 1.9, -IZ + 0.1), (2.9, 0.05, 0.2))
for i in range(5):
    box("Swatch%d" % (i + 1), (RX - 1.15 + i * 0.57, MEZZ + 1.15, -IZ + 0.22), (0.28, 1.45, 0.03), (-8, 0, 0))
box("Steel", (RX - 2.0, MEZZ + 2.0, -3.2), (0.04, 0.04, 2.2))
for k in range(3):
    box("Fabric", (RX - 2.0, MEZZ + 1.55, -3.9 + k * 0.6), (0.12, 0.8, 0.5))
box("Glass", (RX + 1.9, MEZZ + 1.0, -IZ + 0.06), (0.8, 1.9, 0.02))
helper("TAP_Style", (RX - 2.2, MEZZ, -IZ), (RX + 1.6, MEZZ + 2.4, -3.0))
helper("BLK_rack", (RX - 1.6, MEZZ, -IZ), (RX + 1.6, MEZZ + 2.2, -IZ + 0.4))
helper("BLK_jackets", (RX - 2.2, MEZZ, -4.3), (RX - 1.8, MEZZ + 2.2, -2.1))
marker("SPOT_Style", (RX, MEZZ, -3.4))

# ---------------------------------------------------------------- walls for walking and the camera

def wcol(name, lo, hi):
    helper("COL_" + name, lo, hi)

helper("FLR_door", (-DW / 2, -0.3, D - T - 0.1), (DW / 2, 0.0, D + 0.1))   # across the threshold
wcol("front_l", (-W, 0, D - T), (-DW / 2 + 0.02, EAVE, D))
wcol("front_r", (DW / 2 - 0.02, 0, D - T), (W, EAVE, D))
wcol("front_top", (-DW / 2, DH, D - T), (DW / 2, EAVE, D))
wcol("back", (-W, 0, -D), (W, EAVE, -D + T))
wcol("left", (-W, 0, -D), (-W + T, EAVE, D))
wcol("right", (W - T, 0, -D), (W, EAVE, D))
helper("CAM_roof", (-W - 1.6, RIDGE - 0.6, -D - 1.4), (W + 1.6, RIDGE + 0.4, D + 2.6))
marker("MARK_front", (0, 0, D))
marker("MARK_right", (W, 0, 0))
marker("LIGHT_porch", (-1.6, 2.9, D + 0.5))

# ---------------------------------------------------------------- write it out

coll = bpy.context.scene.collection
root = bpy.data.objects.new("Chalet", None)
coll.objects.link(root)
for mat, b in geo.items():
    me = bpy.data.meshes.new(mat)
    b.to_mesh(me)
    b.free()
    m = bpy.data.materials.get(mat) or bpy.data.materials.new(mat)
    me.materials.append(m)
    ob = bpy.data.objects.new(mat, me)
    coll.objects.link(ob)
    ob.parent = root
hm = bpy.data.materials.new("Helper")
for i, (name, c, s) in enumerate(helpers):
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    bmesh.ops.transform(b, matrix=TOB @ mat4(c, s), verts=b.verts)
    me = bpy.data.meshes.new(name)
    b.to_mesh(me)
    b.free()
    me.materials.append(hm)
    ob = bpy.data.objects.new("%s__%d" % (name, i), me)
    coll.objects.link(ob)
    ob.parent = root

print("CHALET triangles: %d, materials: %d, helpers: %d" % (tris, len(geo), len(helpers)))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                         mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO", embed_textures=False)
print("CHALET written", OUT)
