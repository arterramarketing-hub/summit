# Builds the hamlets' buildings in Blender and exports them to Assets/Summit/Resources/Cabins/.
#   python3 Tools/MakeCabins.py -- Assets/Summit/Resources/Cabins [--render Tools/art]
# (needs `pip install bpy pillow numpy`; or run it inside Blender with --background --python)
#
# A small alpine family, after a photo of a modern chalet village on a steep slope beside a piste:
#   Chalet   the larger traditional one: a stone ground floor, honey timber above, a broad low gable with deep
#            eaves, a wide plank balcony, shutters, a stone chimney
#   Lodge    three storeys of pale vertical larch under a low dark metal roof: glazed fronts, two balconies
#            with slim steel railings, an outside stair
#   CabinA-D small cubes on steel stilts, each with its own roof (flat or single-pitch) and window layout
#   CarDeck  a timber parking deck on stilts with a snowed-in car on it
#   Lamp     a lamp post for the edge of the piste
# Exteriors only. Snow lies on every roof, sill and rail, and is banked against the uphill wall, as part of
# the model. Each building also carries its woodpile, ski rack, bench and wall lamp.
#
# Everything is authored in the game's own axes, like Tools/MakeChalet.py: x right, y up, z out of the front
# (downhill). The pivot is the middle of the ground floor: the game sets the floor level on the highest snow
# under the house, and the stone base or the stilts (modelled well down below the floor) take up the slope.
# One mesh per material keeps a building to a handful of draw calls; every building shares one set of
# materials, which the game makes itself, by name (Hamlets.cs):
#   Larch, DarkWood, Stone   the camp's oak, wood and stone textures
#   Metal, Snow, Paint       plain colours
#   Glass                    Windows.png by day, WindowsGlow.png for what shines at dusk and night
#   Far                      the far version: one small mesh coloured from Far.png's swatches (FarGlow.png at night)
# The four textures are painted here too.
#
# Helper boxes (no material in the game, only their bounds are read):
#   COL_   solid: the rider crashes into it          MARK_front  which way the front faces
#   FX_smoke  a chimney's top                        LIGHT_      where a real lamp may go

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv and not argv[0].startswith("--") else "Assets/Summit/Resources/Cabins"
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
CAMP = os.path.join(os.path.dirname(os.path.abspath(OUT)), "Camp")

bpy.ops.wm.read_factory_settings(use_empty=True)

# game axes -> Blender axes: x stays, game up is Blender +Z, game z is Blender +Y. That is a mirror, so every
# face is also turned round to keep it facing out (see Tools/MakeChalet.py).
TOB = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

TILE = {"Larch": 1.6, "DarkWood": 1.6, "Stone": 1.3}
# the window sheet: 4 x 2 panes. 0-4 are lit at night, 5-6 stay dark, 7 is a lantern's glass
ATLAS_COLS, ATLAS_ROWS = 4, 2
LANTERN = 7
# the far version's swatches, left to right in Far.png
FAR = {"Larch": 0, "DarkWood": 1, "Stone": 2, "Metal": 3, "Snow": 4, "Lit": 5, "Dark": 6, "Paint": 7}


class Model:
    def __init__(self, name):
        self.name, self.geo, self.helpers, self.tris, self.pane = name, {}, [], {}, 0


cur = None       # the model being built
models = {}


def mesh_for(mat):
    if mat not in cur.geo:
        b = bmesh.new()
        b.loops.layers.uv.new("UVMap")
        cur.geo[mat] = b
    return cur.geo[mat]


def add(src, mat, m, full_uv=False, turn=False, point=None):
    """Copy a temp bmesh into the material's mesh through matrix m (game space). UVs: box-projected (turn: the
    boards run upright instead of across), the source's own (full_uv), or one point of the texture (point)."""
    dst = mesh_for(mat)
    uv = dst.loops.layers.uv.active
    xf = TOB @ m
    vmap = {v: dst.verts.new(xf @ v.co) for v in src.verts}
    src_uv = src.loops.layers.uv.active
    tile = TILE.get(mat, 1.0)
    for f in src.faces:
        nf = dst.faces.new([vmap[v] for v in reversed(f.verts)])   # reversed: the mirror above
        nf.normal_update()
        n = nf.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l, sl in zip(nf.loops, reversed(f.loops)):
            p = l.vert.co
            if point is not None:
                l[uv].uv = point
            elif full_uv and src_uv is not None:
                l[uv].uv = sl[src_uv].uv
            else:
                c = (p.x / tile, p.y / tile) if ax == 2 else (p.y / tile, p.z / tile) if ax == 0 else (p.x / tile, p.z / tile)
                l[uv].uv = (c[1], c[0]) if turn else c
        cur.tris[mat] = cur.tris.get(mat, 0) + len(f.verts) - 2
    src.free()


def mat4(pos, size=(1, 1, 1), rot=(0, 0, 0)):
    return Matrix.Translation(Vector(pos)) @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*size, 1)))


def box(mat, pos, size, rot=(0, 0, 0), turn=False, point=None):
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    add(b, mat, mat4(pos, size, rot), turn=turn, point=point)


def lohi(mat, lo, hi, turn=False):
    """A box between two corners."""
    box(mat, [(lo[i] + hi[i]) / 2 for i in range(3)], [abs(hi[i] - lo[i]) for i in range(3)], turn=turn)


def pillow(mat, pos, size, inset=0.12, rot=(0, 0, 0)):
    """A box whose top is drawn in all round: settled snow."""
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    kx, kz = max(0.25, (size[0] - 2 * inset) / size[0]), max(0.25, (size[2] - 2 * inset) / size[2])
    for v in b.verts:
        if v.co.z > 0:   # (the cube's own up, before it is laid along the game's y below)
            v.co.x *= kx
            v.co.y *= kz
    add(b, mat, mat4(pos, (1, 1, 1), rot) @ Matrix(((size[0], 0, 0, 0), (0, 0, size[1], 0), (0, size[2], 0, 0), (0, 0, 0, 1))))


def span(mat, a, b_, t):
    """A beam of square section t from point a to point b."""
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    add(b, mat, Matrix.Translation((a + b_) / 2) @ rot @ Matrix.Diagonal(Vector((t, t, d.length, 1))))


def cyl(mat, pos, r, h, seg=10, r2=None, along="y"):
    """A cylinder (or cone) standing on pos along the game's up axis, or lying along x or z from pos."""
    b = bmesh.new()
    bmesh.ops.create_cone(b, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    lay = {"y": Matrix.Rotation(math.radians(-90), 4, "X"), "x": Matrix.Rotation(math.radians(90), 4, "Y"), "z": Matrix.Identity(4)}[along]
    off = {"y": (0, h / 2, 0), "x": (h / 2, 0, 0), "z": (0, 0, h / 2)}[along]
    add(b, mat, Matrix.Translation(Vector(pos) + Vector(off)) @ lay)


def solid(mat, bottom, top, turn=False, point=None):
    """A closed shape between two rings of points (game space), each listed the same way round."""
    b = bmesh.new()
    lo = [b.verts.new(p) for p in bottom]
    hi = [b.verts.new(p) for p in top]
    n = len(lo)
    b.faces.new(lo)
    b.faces.new(hi)
    for i in range(n):
        j = (i + 1) % n
        b.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    add(b, mat, Matrix.Identity(4), turn=turn, point=point)


def plate(mat, pts, th, inset=0.0, point=None):
    """A slab on four corner points (its underside), th thick along its own normal; inset draws the top in."""
    p = [Vector(q) for q in pts]
    n = (p[1] - p[0]).cross(p[3] - p[0]).normalized()
    if n.y < 0:
        n = -n
    c = sum(p, Vector()) / 4
    top = [q + n * th + ((c - q).normalized() * inset * 1.4 if inset else Vector()) for q in p]
    solid(mat, p, top, point=point)


def roof_plane(x0, x1, z0, z1, h, th):
    """Four corners of a roof whose underside is at height h(x, z), raised th."""
    return [(x, h(x, z) + th, z) for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))]


def wedge(mat, y0, h, x0, x1, z, th, turn=False):
    """A gable: a triangle on the base x0..x1 at height y0, its apex h above the middle, th thick, centred on z."""
    solid(mat, [(x0, y0, z - th / 2), (x1, y0, z - th / 2), ((x0 + x1) / 2, y0 + h, z - th / 2)],
          [(x0, y0, z + th / 2), (x1, y0, z + th / 2), ((x0 + x1) / 2, y0 + h, z + th / 2)], turn=turn)


def cell_uv(c, pad=0.006):
    col, row = c % ATLAS_COLS, c // ATLAS_COLS
    return (col / ATLAS_COLS + pad, 1 - (row + 1) / ATLAS_ROWS + pad, (col + 1) / ATLAS_COLS - pad, 1 - row / ATLAS_ROWS - pad)


def card(mat, centre, normal, w, h, uvrect):
    """A flat card facing along normal (level), its texture rectangle filling it."""
    c, n, up = Vector(centre), Vector(normal).normalized(), Vector((0, 1, 0))
    r = up.cross(n)
    b = bmesh.new()
    uv = b.loops.layers.uv.new("UVMap")
    vs = [b.verts.new(c + r * sx * w / 2 + up * sy * h / 2) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f = b.faces.new(vs)
    u0, v0, u1, v1 = uvrect
    for l, t in zip(f.loops, ((u1, v0), (u0, v0), (u0, v1), (u1, v1))):
        l[uv].uv = t
    add(b, mat, Matrix.Identity(4), full_uv=True)


def lantern(pos, size=(0.2, 0.28, 0.2)):
    u0, v0, u1, v1 = cell_uv(LANTERN)
    box("Glass", pos, size, point=((u0 + u1) / 2, (v0 + v1) / 2))


def helper(name, lo, hi):
    c = [(lo[i] + hi[i]) / 2 for i in range(3)]
    s = [max(0.02, abs(hi[i] - lo[i])) for i in range(3)]
    cur.helpers.append((name, c, s))


def marker(name, pos):
    cur.helpers.append((name, pos, (0.1, 0.1, 0.1)))


# ---------------------------------------------------------------- the far version

def far(col):
    return ((FAR[col] + 0.5) / len(FAR), 0.5)


def fbox(col, lo, hi):
    box("Far", [(lo[i] + hi[i]) / 2 for i in range(3)], [abs(hi[i] - lo[i]) for i in range(3)], point=far(col))


def fcard(col, centre, normal, w, h):
    u, v = far(col)
    card("Far", centre, normal, w, h, (u, v, u, v))


# ---------------------------------------------------------------- walls' furniture

LIT = [0, 5, 1, 2, 6, 3, 4, 0, 6, 2, 1, 5, 3, 4]   # which pane each window gets, in the order they are made


def next_cell():
    c = LIT[cur.pane % len(LIT)]
    cur.pane += 1
    return c


class Face:
    """One outside wall: along x at z = at (facing out along z), or along z at x = at (facing out along x)."""
    def __init__(self, axis, at, out):
        self.axis, self.at, self.out = axis, at, out
        self.n = (0, 0, out) if axis == "x" else (out, 0, 0)

    def P(self, u, y, depth=0.0):
        return (u, y, self.at + self.out * depth) if self.axis == "x" else (self.at + self.out * depth, y, u)

    def S(self, su, sy, sd):
        return (su, sy, sd) if self.axis == "x" else (sd, sy, su)


def window(F, a, y, w, h, panes=1, frame="DarkWood", sill=True, shutters=None, snow=True):
    """A window on wall F, centred a along it and y up: a frame, panes off the window sheet, a sill with snow on it."""
    f = 0.07
    box(frame, F.P(a, y + h / 2 + f / 2, 0.03), F.S(w + 2 * f, f, 0.1))
    box(frame, F.P(a, y - h / 2 - f / 2, 0.03), F.S(w + 2 * f, f, 0.1))
    for s in (-1, 1):
        box(frame, F.P(a + s * (w / 2 + f / 2), y, 0.03), F.S(f, h, 0.1))
    pw = w / panes
    for i in range(panes):
        card("Glass", F.P(a - w / 2 + pw * (i + 0.5), y, 0.025), F.n, pw, h, cell_uv(next_cell()))
        if i:
            box(frame, F.P(a - w / 2 + pw * i, y, 0.035), F.S(0.05, h, 0.07))
    if sill:
        box(frame, F.P(a, y - h / 2 - f - 0.03, 0.1), F.S(w + 0.26, 0.06, 0.22))
        if snow:
            pillow("Snow", F.P(a, y - h / 2 - f + 0.05, 0.11), F.S(w + 0.2, 0.11, 0.2), 0.04)
    if shutters:
        for s in (-1, 1):
            box(shutters, F.P(a + s * (w / 2 + f + w * 0.24), y, 0.03), F.S(w * 0.46, h + 0.1, 0.05), turn=True)


def door(F, a, w=1.0, h=2.05, mat="DarkWood", frame="DarkWood", lamp=True):
    """A plank door on wall F with a small light over it and a lantern beside it."""
    f = 0.08
    box(mat, F.P(a, h / 2, 0.03), F.S(w, h, 0.07), turn=True)
    box(frame, F.P(a, h + f / 2, 0.04), F.S(w + 2 * f, f, 0.12))
    for s in (-1, 1):
        box(frame, F.P(a + s * (w / 2 + f / 2), h / 2, 0.04), F.S(f, h, 0.12))
    card("Glass", F.P(a, h * 0.72, 0.07), F.n, w * 0.42, h * 0.26, cell_uv(next_cell()))
    if lamp:
        box("Metal", F.P(a + w / 2 + 0.35, 2.0, 0.06), F.S(0.06, 0.06, 0.12))
        lantern(F.P(a + w / 2 + 0.35, 1.86, 0.15))


def rail(pts, y, h=1.0, post=0.05, bars=2, gap=1.4, snow=True, mat="Metal"):
    """A slim railing along a run of points (x, z) at floor height y: posts, a top rail, thin bars, snow on top."""
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        ln = math.hypot(x1 - x0, z1 - z0)
        n = max(1, round(ln / gap))
        for i in range(n + 1):
            t = i / n
            box(mat, (x0 + (x1 - x0) * t, y + h / 2, z0 + (z1 - z0) * t), (post, h, post))
        span(mat, (x0, y + h, z0), (x1, y + h, z1), post)
        for k in range(bars):
            span(mat, (x0, y + h * (k + 1) / (bars + 1), z0), (x1, y + h * (k + 1) / (bars + 1), z1), 0.025)
        if snow:
            along_x = abs(x1 - x0) > abs(z1 - z0)
            pillow("Snow", ((x0 + x1) / 2, y + h + 0.07, (z0 + z1) / 2), (ln - 0.1, 0.1, 0.12) if along_x else (0.12, 0.1, ln - 0.1), 0.03)


def stilts(w, d, depth, front=0.0, mid=False):
    """Steel legs under a cabin of half size w, d: posts at the corners down to `depth`, crossed bracing on the
    downhill face and the sides, a ring beam under the floor, and a stone footing along the uphill edge."""
    t = 0.16
    xs = (-w + t / 2, 0.0, w - t / 2) if mid else (-w + t / 2, w - t / 2)
    zf = d + front - t / 2
    for x in xs:
        box("Metal", (x, -depth / 2 - 0.1, zf), (t, depth, t))
        box("Metal", (x, -depth / 2 - 0.1, 0.0 if depth > 4 else -d + 0.6), (t, depth, t))
    lohi("Metal", (-w - 0.04, -0.24, -d - 0.04), (w + 0.04, 0.0, d + front + 0.04))        # the floor's steel frame
    zb = 0.0 if depth > 4 else -d + 0.6
    top, low = -0.3, -min(depth - 0.4, 3.6)
    for a, b_ in zip(xs, xs[1:]):                                                       # the downhill face
        span("Metal", (a, top, zf), (b_, low, zf), 0.05)
        span("Metal", (b_, top, zf), (a, low, zf), 0.05)
    for x in (xs[0], xs[-1]):                                                           # the sides
        span("Metal", (x, top, zf), (x, low, zb), 0.05)
        span("Metal", (x, top, zb), (x, low, zf), 0.05)
    lohi("Stone", (-w - 0.1, -3.2, -d - 0.1), (w + 0.1, -0.02, -d + 0.55))               # where it meets the hill


def drift(x0, x1, z, h=0.8, deep=3.0, out=1.5):
    """Snow banked against the uphill wall (at z, facing -z) between x0 and x1: h up the wall, running back up the hill."""
    solid("Snow", [(x0 - 0.4, -deep, z + 0.05), (x1 + 0.4, -deep, z + 0.05), (x1 + 0.4, -deep, z - out - 0.6), (x0 - 0.4, -deep, z - out - 0.6)],
          [(x0, h, z + 0.05), (x1, h, z + 0.05), (x1 - 0.2, h + 0.25, z - out), (x0 + 0.2, h + 0.25, z - out)])


def stoop(F, z0, z1, deep, wide=1.2, mat="Stone", kerb=True):
    """The way in, on a side wall F: a level landing along the wall from the uphill corner (z0, where it runs into
    the hill) to z1, its wall going down `deep` into the snow, a low rail and snow along its outer edge."""
    o = F.out
    xa, xb = F.at, F.at + o * wide
    lohi(mat, (min(xa, xb), -deep, z0), (max(xa, xb), 0.0, z1))
    if kerb:
        xe = F.at + o * (wide - 0.06)
        for z in (z0 + 1.2, (z0 + z1) / 2 + 0.3, z1 - 0.06):
            box("DarkWood", (xe, 0.5, z), (0.09, 1.0, 0.09))
        box("DarkWood", (xe, 0.98, (z0 + 1.2 + z1) / 2), (0.08, 0.08, z1 - z0 - 1.2))
        box("DarkWood", (xe, 0.5, (z0 + 1.2 + z1) / 2), (0.05, 0.08, z1 - z0 - 1.2))
        box("DarkWood", (F.at + o * wide / 2, 0.98, z1 - 0.06), (wide, 0.08, 0.08))
        pillow("Snow", (xe, 1.08, (z0 + 1.2 + z1) / 2), (0.14, 0.12, z1 - z0 - 1.3), 0.03)
    pillow("Snow", (F.at + o * (wide - 0.3), 0.07, z0 + 0.8), (0.6, 0.16, 1.5), 0.08)     # shovelled to the side


def canopy(F, a, w=1.7, y=2.4, deep=0.95, mat="DarkWood"):
    """A little roof over a door on wall F, with its snow."""
    box(mat, F.P(a, y, deep / 2), F.S(w, 0.07, deep))
    pillow("Snow", F.P(a, y + 0.17, deep / 2), F.S(w - 0.08, 0.26, deep - 0.06), 0.08)


def woodpile(F, a0, a1, h=1.1, deep=0.5, y=0.0):
    """Split logs stacked against wall F from a0 to a1 (standing on a floor at y), under a board with snow on it."""
    mid, dc = (a0 + a1) / 2, deep / 2 + 0.02
    box("DarkWood", F.P(mid, y + h / 2, dc), F.S(a1 - a0, h, deep))
    n = int((a1 - a0) / 0.26)
    for row in range(2):   # the top rows as round logs, their pale ends showing
        for i in range(n - row):
            u = a0 + 0.13 + row * 0.13 + i * 0.26
            p = F.P(u, y + h + 0.1 + row * 0.2, 0.01)
            cyl("Larch", p if F.out > 0 else F.P(u, y + h + 0.1 + row * 0.2, deep + 0.03), 0.12, deep + 0.02, 6, along="z" if F.axis == "x" else "x")
    box("DarkWood", F.P(mid, y + h + 0.62, dc), F.S(a1 - a0 + 0.3, 0.05, deep + 0.3))
    pillow("Snow", F.P(mid, y + h + 0.76, dc), F.S(a1 - a0 + 0.26, 0.24, deep + 0.26), 0.08)
    for u in (a0 - 0.05, a1 + 0.05):
        box("DarkWood", F.P(u, y + (h + 0.6) / 2, dc), F.S(0.08, h + 0.6, 0.08))


def ski_rack(F, a, n=4, y=0.0):
    """Skis and a board stood in a rack against wall F."""
    for s in (-1, 1):
        box("DarkWood", F.P(a + s * 0.7, y + 0.55, 0.28), F.S(0.07, 1.1, 0.07))
    box("DarkWood", F.P(a, y + 1.05, 0.28), F.S(1.5, 0.06, 0.07))
    box("DarkWood", F.P(a, y + 0.25, 0.28), F.S(1.5, 0.06, 0.07))
    for i in range(n):
        box("Metal" if i % 2 else "Larch", F.P(a - 0.5 + i * 0.3, y + 0.86, 0.17), F.S(0.09, 1.72, 0.02))
    box("Larch", F.P(a + 0.52, y + 0.78, 0.16), F.S(0.27, 1.5, 0.03))   # a snowboard


def bench(x, y, z, yaw90=False):
    sx, sz = (0.45, 1.5) if yaw90 else (1.5, 0.45)
    box("Larch", (x, y + 0.45, z), (sx, 0.06, sz))
    for s in (-1, 1):
        box("DarkWood", (x + (0 if yaw90 else s * 0.6), y + 0.22, z + (s * 0.6 if yaw90 else 0)), (0.4 if yaw90 else 0.07, 0.44, 0.07 if yaw90 else 0.4))
    pillow("Snow", (x, y + 0.53, z), (sx - 0.06, 0.1, sz - 0.06), 0.05)


def flue(x, z, y0, y1, r=0.11):
    cyl("Metal", (x, y0, z), r, y1 - y0, 8)
    cyl("Metal", (x, y1, z), r * 2.1, 0.08, 8, r2=r * 0.6)
    marker("FX_smoke", (x, y1 + 0.2, z))


def begin(name):
    global cur
    cur = Model(name)
    models[name] = cur
    return cur


# ---------------------------------------------------------------- the chalet

def chalet():
    M = begin("Chalet")
    W, D = 5.5, 3.75
    STONE, EAVE, PITCH = 2.7, 5.3, 22.0
    tanp = math.tan(math.radians(PITCH))
    RIDGE = EAVE + W * tanp
    BASE = 9.0
    FRONT, BACK, RIGHT, LEFT = Face("x", D, 1), Face("x", -D, -1), Face("z", W, 1), Face("z", -W, -1)

    lohi("Stone", (-W - 0.15, -BASE, -D - 0.15), (W + 0.15, 0.0, D + 0.15))              # the base, down into the hill
    lohi("Stone", (-W, 0.0, -D), (W, STONE, D))                                         # a stone ground floor
    lohi("Larch", (-W - 0.06, STONE, -D - 0.06), (W + 0.06, EAVE, D + 0.06))             # timber above
    box("DarkWood", (0, STONE + 0.02, 0), (2 * W + 0.2, 0.14, 2 * D + 0.2))             # the beam they meet on
    for sx in (-1, 1):
        for sz in (-1, 1):
            box("DarkWood", (sx * (W + 0.02), (STONE + EAVE) / 2, sz * (D + 0.02)), (0.2, EAVE - STONE, 0.2))
    for z in (D, -D):
        wedge("Larch", EAVE, RIDGE - EAVE, -W - 0.06, W + 0.06, z - math.copysign(0.07, z), 0.26)

    # the roof: broad, low, deep eaves; snow lying thick on both pitches
    OS, OF, OB = 1.3, 1.9, 1.1
    for s in (-1, 1):
        h = lambda x, z: RIDGE + 0.12 - abs(x) * tanp
        x_in, x_out = 0.0, s * (W + OS)
        a, b_ = (x_in, x_out) if s > 0 else (x_out, x_in)
        plate("DarkWood", roof_plane(a, b_, -D - OB, D + OF, h, 0.0), 0.2)
        sa, sb = (0.0, s * (W + OS - 0.2)) if s > 0 else (s * (W + OS - 0.2), 0.0)
        plate("Snow", roof_plane(sa, sb, -D - OB + 0.15, D + OF - 0.15, h, 0.2), 0.5, 0.22)
        ex = s * (W + OS)
        box("Larch", (ex, h(ex, 0) + 0.02, (OF - OB) / 2), (0.07, 0.3, 2 * D + OF + OB))    # fascia along the eave
        for z in (D + OF, -D - OB):                                                       # and up the gable's edge
            span("Larch", (0, h(0, 0) + 0.06, z), (ex, h(ex, 0) + 0.06, z), 0.24)
    for px in (-4.4, -2.2, 0.0, 2.2, 4.4):   # purlins running out under the front eave, a carved block under each
        yy = RIDGE - abs(px) * tanp - 0.08
        span("DarkWood", (px, yy, -D - OB + 0.2), (px, yy, D + OF - 0.12), 0.22)
        if yy - 0.3 > EAVE - 0.25 or abs(px) > 4:
            box("Larch", (px, yy - 0.22, D + 0.55), (0.2, 0.22, 1.1))

    # the balcony across the front: planks on brackets, a boarded balustrade, snow along its rail
    BY, BD = STONE, 1.6
    lohi("Larch", (-W + 0.1, BY - 0.02, D), (W - 0.1, BY + 0.12, D + BD))
    for x in (-5.0, -2.5, 0.0, 2.5, 5.0):
        box("DarkWood", (x, BY - 0.16, D + BD / 2), (0.2, 0.28, BD + 0.08))
        span("DarkWood", (x, BY - 1.2, D + 0.08), (x, BY - 0.3, D + BD - 0.25), 0.13)
    box("Larch", (0, BY + 0.6, D + BD - 0.04), (2 * W - 0.2, 0.95, 0.06), turn=True)
    box("DarkWood", (0, BY + 1.1, D + BD - 0.04), (2 * W - 0.1, 0.08, 0.16))
    pillow("Snow", (0, BY + 1.21, D + BD - 0.04), (2 * W - 0.3, 0.15, 0.2), 0.04)
    for s in (-1, 1):
        box("Larch", (s * (W - 0.13), BY + 0.6, D + BD / 2), (0.06, 0.95, BD), turn=True)
        box("DarkWood", (s * (W - 0.13), BY + 1.1, D + BD / 2), (0.16, 0.08, BD))
    bench(-3.6, BY + 0.12, D + 0.35)

    # windows: small in the stone, shuttered in the timber; a glazed door onto the balcony
    for x in (-3.6, 0.0, 3.6):
        window(FRONT, x, 1.45, 1.25, 1.15)
    for x in (-3.5, 3.5):
        window(FRONT, x, STONE + 1.45, 1.3, 1.3, panes=2, shutters="DarkWood", snow=False)
    window(FRONT, 0.0, STONE + 1.2, 1.6, 2.1, panes=2, sill=False)
    window(FRONT, 0.0, EAVE + 0.75, 0.9, 0.8, shutters="DarkWood")                       # the attic
    for F in (RIGHT, LEFT):
        for z in (-1.7, 1.7):
            if F is RIGHT:
                window(F, z, 1.45, 1.1, 1.1)
            window(F, z, STONE + 1.45, 1.2, 1.25, panes=2, shutters="DarkWood")
    for x in (-3.4, 0.0, 3.4):
        window(BACK, x, STONE + 1.45, 1.2, 1.2, panes=2, shutters="DarkWood")

    # where the hill falls away under the front, the base shows as a lower floor: a store's doors, two small windows
    LOW = Face("x", D + 0.15, 1)
    box("DarkWood", LOW.P(0.0, -1.55, 0.03), LOW.S(2.6, 2.3, 0.07), turn=True)
    box("DarkWood", LOW.P(0.0, -0.34, 0.05), LOW.S(2.9, 0.14, 0.12))
    for x in (-3.6, 3.6):
        window(LOW, x, -1.3, 1.0, 0.9)

    # the way in, on the left: a stone terrace along the wall from the hill behind, the door under a little roof,
    # skis and the woodpile beside it; snow banked up the uphill wall
    stoop(LEFT, -D - 0.6, 3.1, BASE, 1.5)
    door(LEFT, -2.6, 1.1)
    canopy(LEFT, -2.6)
    ski_rack(LEFT, -0.9)
    woodpile(LEFT, 0.5, 2.8)
    drift(-W, W, -D, 0.8)

    # a stone chimney through the snow
    lohi("Stone", (2.2, EAVE, -1.6), (3.1, RIDGE + 0.9, -0.7))
    box("Stone", (2.65, RIDGE + 0.96, -1.15), (1.15, 0.14, 1.15))
    pillow("Snow", (2.65, RIDGE + 1.13, -1.15), (1.05, 0.2, 1.05), 0.12)
    marker("FX_smoke", (2.65, RIDGE + 1.3, -1.15))

    helper("COL_body", (-W - 0.15, -BASE, -D - 0.15), (W + 0.15, EAVE, D + 0.15))
    helper("COL_roof", (-W - 0.6, EAVE, -D - OB), (W + 0.6, RIDGE + 0.5, D + OF))
    helper("COL_balcony", (-W, BY - 0.4, D), (W, BY + 1.2, D + BD))
    helper("COL_terrace", (-W - 1.5, -BASE, -D - 0.6), (-W, 2.5, 3.1))
    marker("MARK_front", (0, 0, D))
    marker("LIGHT_porch", (-W - 0.8, 2.0, -1.8))

    # far: the stone, the timber, the gables, two snowy pitches, a few windows
    fbox("Stone", (-W - 0.15, -BASE, -D - 0.15), (W + 0.15, STONE, D + 0.15))
    fbox("Larch", (-W, STONE, -D), (W, EAVE, D))
    for z in (D, -D):
        solid("Far", [(-W, EAVE, z - 0.1), (W, EAVE, z - 0.1), (0, RIDGE, z - 0.1)], [(-W, EAVE, z + 0.1), (W, EAVE, z + 0.1), (0, RIDGE, z + 0.1)], point=far("Larch"))
    for s in (-1, 1):
        h = lambda x, z: RIDGE + 0.2 - abs(x) * tanp
        a, b_ = (0.0, s * (W + OS)) if s > 0 else (s * (W + OS), 0.0)
        plate("Far", roof_plane(a, b_, -D - OB, D + OF, h, 0.0), 0.6, point=far("Snow"))
    fbox("Larch", (-W, BY, D + BD - 0.1), (W, BY + 1.1, D + BD))
    fbox("Stone", (2.2, EAVE, -1.6), (3.1, RIDGE + 1.0, -0.7))
    for x, c in ((-3.5, "Lit"), (0.0, "Lit"), (3.5, "Dark")):
        fcard(c, (x, STONE + 1.4, D + 0.03), (0, 0, 1), 1.4, 1.5)
        fcard("Lit" if c == "Dark" else "Dark", (x, 1.45, D + 0.18), (0, 0, 1), 1.25, 1.15)
    for F, cs in ((RIGHT, ("Lit", "Dark")), (LEFT, ("Dark", "Lit"))):
        for z, c in zip((-1.7, 1.7), cs):
            fcard(c, F.P(z, STONE + 1.45, 0.03), F.n, 1.2, 1.25)
    fcard("Lit", (-3.4, STONE + 1.45, -D - 0.03), (0, 0, -1), 1.2, 1.2)
    fcard("Lit", (3.4, STONE + 1.45, -D - 0.03), (0, 0, -1), 1.2, 1.2)
    fbox("Stone", (-W - 1.5, -BASE, -D - 0.6), (-W, 0.0, 3.1))
    M.info = dict(hx=W + 0.15, zb=-D - 0.15, zf=D + 0.15, skirt=BASE, top=RIDGE + 1.3)


# ---------------------------------------------------------------- the lodge

def lodge():
    M = begin("Lodge")
    W, D, FH = 4.0, 3.25, 2.9
    EAVE, PITCH = 3 * FH, 10.0
    tanp = math.tan(math.radians(PITCH))
    RIDGE = EAVE + W * tanp
    BASE = 8.5
    FRONT, BACK, RIGHT, LEFT = Face("x", D, 1), Face("x", -D, -1), Face("z", W, 1), Face("z", -W, -1)

    lohi("Stone", (-W + 0.12, -BASE, -D + 0.12), (W - 0.12, 0.0, D - 0.12))              # a plain plinth, set back
    lohi("Larch", (-W, 0.0, -D), (W, EAVE, D), turn=True)                               # pale upright boards
    for z in (D, -D):
        wedge("Larch", EAVE, RIDGE - EAVE, -W, W, z - math.copysign(0.1, z), 0.2, turn=True)
    for k in (1, 2):                                                                    # a dark band at each floor
        box("DarkWood", (0, k * FH, 0), (2 * W + 0.06, 0.2, 2 * D + 0.06))

    # the roof: low, dark metal, thin at the edge, its snow thick
    OS, OF, OB = 0.7, 1.7, 0.5
    for s in (-1, 1):
        h = lambda x, z: RIDGE + 0.05 - abs(x) * tanp
        a, b_ = (0.0, s * (W + OS)) if s > 0 else (s * (W + OS), 0.0)
        plate("Metal", roof_plane(a, b_, -D - OB, D + OF, h, 0.0), 0.12)
        sa, sb = (0.0, s * (W + OS - 0.18)) if s > 0 else (s * (W + OS - 0.18), 0.0)
        plate("Snow", roof_plane(sa, sb, -D - OB + 0.12, D + OF - 0.12, h, 0.12), 0.42, 0.2)

    # glazed fronts on every floor, behind two balconies with slim steel railings
    for k in range(3):
        window(FRONT, 0.0, k * FH + 1.32, 6.9, 2.3, panes=4, frame="Metal", sill=False)
    BD = 1.5
    for k in (1, 2):
        y = k * FH
        x1 = W + (1.05 if k == 1 else 0.0)                                              # the first one runs on to the stair
        lohi("Larch", (-W, y - 0.02, D), (x1, y + 0.1, D + BD))
        lohi("Metal", (-W, y - 0.2, D + BD - 0.08), (x1, y - 0.02, D + BD))
        for x in (-W + 0.04, x1 - 0.04):
            lohi("Metal", (x - 0.04, y - 0.2, D), (x + 0.04, y - 0.02, D + BD))
        rail([(-W + 0.03, D), (-W + 0.03, D + BD - 0.03), (x1 - 0.03, D + BD - 0.03)] + ([(W - 0.03, D)] if k == 2 else []), y + 0.1)
    for x in (-W + 0.08, W - 0.08):                                                     # steel posts from the hill to the roof
        lohi("Metal", (x - 0.07, -BASE, D + BD - 0.14), (x + 0.07, EAVE + 0.1, D + BD))
    span("Metal", (-W + 0.08, -0.4, D + BD - 0.07), (W - 0.08, -4.2, D + BD - 0.07), 0.05)
    span("Metal", (W - 0.08, -0.4, D + BD - 0.07), (-W + 0.08, -4.2, D + BD - 0.07), 0.05)
    lohi("Metal", (-W, -0.22, D), (W, -0.02, D + 0.14))
    bench(-2.6, FH + 0.1, D + 0.4)

    # the outside stair up the right-hand wall, from the door's side to the first balcony
    SX0, SX1 = W + 0.06, W + 1.0
    z0, z1, N = -D + 0.5, D + 0.1, 15
    for i in range(N):
        t = (i + 0.5) / N
        box("Larch", ((SX0 + SX1) / 2, FH * (i + 1) / (N + 1), z0 + (z1 - z0) * t), (SX1 - SX0, 0.05, (z1 - z0) / N + 0.03))
    for x in (SX0 + 0.03, SX1 - 0.03):
        span("Metal", (x, -0.1, z0 - 0.15), (x, FH - 0.1, z1 + 0.05), 0.09)
    span("Metal", (SX1 - 0.03, 1.0, z0 - 0.15), (SX1 - 0.03, FH + 1.1, z1 + 0.05), 0.05)
    for t in (0.0, 0.33, 0.66, 1.0):
        zz, yy = z0 - 0.15 + (z1 - z0 + 0.2) * t, FH * t
        box("Metal", (SX1 - 0.03, yy + 0.5, zz), (0.05, 1.1, 0.05))
    lohi("Stone", (SX0 - 0.06, -2.6, z0 - 1.0), (SX1 + 0.1, 0.0, z0 - 0.1))               # its foot, on the hill

    # the sides: tall slim windows, staggered
    for k in range(3):
        y = k * FH + 1.4
        if k:
            window(LEFT, -1.4 + 0.5 * (k % 2), y, 0.8, 1.9, frame="Metal")
        window(LEFT, 1.9, y, 0.8 if k else 1.4, 1.9 if k else 1.3, panes=1 if k else 2, frame="Metal")
        window(RIGHT, -1.0 if k else 1.9, y + (0.0 if k else 0.4), 1.5, 1.1, panes=2, frame="Metal")
        if k:
            window(BACK, -2.0, y, 0.8, 1.9, frame="Metal")
            window(BACK, 1.6 + 0.4 * (k - 1), y + 0.2, 1.4, 1.0, panes=2, frame="Metal")

    # the way in, on the left: a landing along the wall from the hill behind, the door under a steel canopy, skis
    stoop(LEFT, -D - 0.6, 0.6, BASE, 1.3)
    door(LEFT, -2.2, 1.1, frame="Metal")
    canopy(LEFT, -2.2, mat="Metal")
    ski_rack(LEFT, -0.5)
    drift(-W, W, -D, 0.8)
    flue(-2.2, -1.2, EAVE + 0.2, RIDGE + 1.2)

    helper("COL_body", (-W, -BASE, -D), (W, RIDGE + 0.5, D))
    helper("COL_balconies", (-W, FH - 0.25, D), (W, 2 * FH + 1.2, D + BD))
    helper("COL_stair", (W, -2.6, -D - 0.6), (W + 1.1, FH + 1.1, D + BD))
    helper("COL_landing", (-W - 1.3, -BASE, -D - 0.6), (-W, 2.5, 0.6))
    helper("COL_postL", (-W, -BASE, D + BD - 0.2), (-W + 0.2, FH, D + BD))
    helper("COL_postR", (W - 0.2, -BASE, D + BD - 0.2), (W, FH, D + BD))
    marker("MARK_front", (0, 0, D))
    marker("LIGHT_porch", (-W - 0.7, 2.0, -1.4))

    fbox("Stone", (-W - 1.3, -BASE, -D - 0.6), (-W, 0.0, 0.6))
    fbox("Stone", (-W + 0.12, -BASE, -D + 0.12), (W - 0.12, 0.0, D - 0.12))
    fbox("Larch", (-W, 0.0, -D), (W, EAVE, D))
    for z in (D, -D):
        solid("Far", [(-W, EAVE, z - 0.1), (W, EAVE, z - 0.1), (0, RIDGE, z - 0.1)], [(-W, EAVE, z + 0.1), (W, EAVE, z + 0.1), (0, RIDGE, z + 0.1)], point=far("Larch"))
    for s in (-1, 1):
        h = lambda x, z: RIDGE + 0.05 - abs(x) * tanp
        a, b_ = (0.0, s * (W + OS)) if s > 0 else (s * (W + OS), 0.0)
        plate("Far", roof_plane(a, b_, -D - OB, D + OF, h, 0.0), 0.5, point=far("Snow"))
    for k in range(3):
        fcard("Lit" if k != 1 else "Dark", (-1.75, k * FH + 1.32, D + 0.03), (0, 0, 1), 3.4, 2.3)
        fcard("Lit" if k == 1 else "Dark", (1.75, k * FH + 1.32, D + 0.03), (0, 0, 1), 3.4, 2.3)
        fcard("Lit" if k == 2 else "Dark", LEFT.P(-1.2, k * FH + 1.4, 0.03), LEFT.n, 0.8, 1.9)
        fcard("Lit" if k == 0 else "Dark", RIGHT.P(-1.0, k * FH + 1.4, 0.03), RIGHT.n, 1.5, 1.1)
        if k:
            fbox("Metal", (-W, k * FH - 0.15, D), (W, k * FH + 0.1, D + BD))
            fcard("Lit" if k == 1 else "Dark", (-2.0, k * FH + 1.4, -D - 0.03), (0, 0, -1), 0.8, 1.9)
    M.info = dict(hx=W + 0.1, zb=-D - 0.1, zf=D + 0.1, skirt=BASE, top=RIDGE + 1.3)


# ---------------------------------------------------------------- the small cabins

def cabin(name, w, d, floors, roof, body, upper=None, upright=False, deck=1.1, depth=7.0, layout=0, smoke=False, jut=0.0):
    """A cube on stilts. roof: 'flat', 'front' (single pitch, high at the front) or 'side' (high on the right).
    body / upper: the cladding of the ground and first floors; jut: how far the first floor hangs out in front."""
    M = begin(name)
    FH = 2.75
    H = floors * FH + 0.15
    upper = upper or body
    stilts(w, d, depth, front=deck)
    FRONT, BACK, RIGHT, LEFT = Face("x", d, 1), Face("x", -d, -1), Face("z", w, 1), Face("z", -w, -1)
    UFRONT = Face("x", d + jut, 1)

    lohi(body, (-w, 0.0, -d), (w, FH if floors > 1 else H, d), turn=upright)
    if floors > 1:
        lohi(upper, (-w - 0.05, FH, -d - 0.05), (w + 0.05, H, d + jut), turn=not upright)
        box("Metal", (0, FH, jut / 2), (2 * w + 0.14, 0.1, 2 * d + jut + 0.14))
    top = upper if floors > 1 else body
    zf = d + (jut if floors > 1 else 0.0)

    # the roof and its snow
    if roof == "flat":
        hr = lambda x, z: H
        rise = 0.0
    elif roof == "front":
        rise = 0.2 * (2 * d)
        hr = lambda x, z: H + rise * (z + d) / (zf + d)
        solid(top, [(-w, H, -d), (w, H, -d), (w, H, zf), (-w, H, zf)], [(-w, H, -d), (w, H, -d), (w, H + rise, zf), (-w, H + rise, zf)], turn=upright)
    else:
        rise = 0.2 * (2 * w)
        hr = lambda x, z: H + rise * (x + w) / (2 * w)
        solid(top, [(-w, H, -d), (w, H, -d), (w, H, zf), (-w, H, zf)], [(-w, H, -d), (w, H + rise, -d), (w, H + rise, zf), (-w, H, zf)], turn=upright)
    ov = 0.3
    plate("Metal", roof_plane(-w - ov, w + ov, -d - ov, zf + ov + 0.3, hr, 0.0), 0.14)
    plate("Snow", roof_plane(-w - ov + 0.12, w + ov - 0.12, -d - ov + 0.12, zf + ov + 0.18, hr, 0.14), 0.55, 0.22)

    # the front: glass, and a deck with a slim railing
    if deck > 0:
        lohi("Larch", (-w, -0.02, d), (w, 0.06, d + deck))
        rail([(-w + 0.03, d), (-w + 0.03, d + deck - 0.03), (w - 0.03, d + deck - 0.03), (w - 0.03, d)], 0.06)
    if layout == 0:      # one big picture window
        window(FRONT, 0.0, 1.3, 2 * w - 1.0, 2.0, panes=2, frame="Metal", sill=False)
        window(LEFT, 0.3, 1.5, 1.0, 1.2, frame="Metal")
        window(RIGHT, -0.6, 1.45, 0.6, 1.8, frame="Metal")
    elif layout == 1:    # tall glass below, a balcony window above, a slit and a square on the sides
        window(FRONT, -0.5, 1.25, 2 * w - 2.0, 2.1, panes=2, frame="Metal", sill=False)
        window(UFRONT, 0.4, FH + 1.3, 2 * w - 1.6, 1.7, panes=2, frame="Metal")
        window(UFRONT, 0.0, H + rise * 0.55, 2 * w - 1.2, max(0.45, rise * 0.55), panes=3, frame="Metal", sill=False)
        window(LEFT, 0.6, FH + 1.6, 1.9, 0.6, panes=2, frame="Metal")
        window(LEFT, -0.9, 1.4, 1.0, 1.0, frame="Metal")
        window(RIGHT, 0.2, 1.5, 0.7, 1.7, frame="Metal")
        window(RIGHT, -0.8, FH + 1.4, 1.1, 1.1, frame="Metal")
        window(BACK, 1.0, FH + 1.5, 0.9, 0.9, frame="Metal")
    elif layout == 2:    # a row of three square windows; a long high strip on the tall side
        for x in (-w + 1.1, 0.0, w - 1.1):
            window(FRONT, x, 1.45, 1.15, 1.25, frame="DarkWood")
        window(RIGHT, 0.0, H + rise - 0.75, 2 * d - 1.4, 0.6, panes=3, frame="DarkWood", sill=False)
        window(LEFT, 0.4, 1.4, 1.2, 1.0, frame="DarkWood")
        window(BACK, w - 1.2, 1.5, 0.9, 0.9, frame="DarkWood")
    else:                # glass doors below, one large square window in the overhanging floor
        window(FRONT, 0.0, 1.15, 2 * w - 1.4, 2.1, panes=2, frame="Metal", sill=False)
        window(UFRONT, -0.4, FH + 1.35, 2.0, 1.8, panes=2, frame="Metal")
        window(LEFT, -0.3, FH + 1.5, 0.7, 1.6, frame="Metal")
        window(RIGHT, 0.5, 1.4, 1.2, 1.0, frame="Metal")
        window(RIGHT, -0.5, FH + 1.5, 1.1, 1.1, frame="Metal")
        window(BACK, -0.9, FH + 1.5, 1.0, 0.7, frame="Metal")

    # the way in, on one side: a landing along the wall from the hill behind, the door under a canopy; skis or a
    # few logs beside it on some; snow banked up the uphill wall
    SIDE = LEFT if layout % 2 == 0 else RIGHT
    dz = -d + 0.95
    stoop(SIDE, -d - 0.5, dz + (2.3 if layout in (1, 2) else 0.8), min(depth, 4.5), 1.1, mat="Larch" if layout % 2 else "Stone")
    door(SIDE, dz, 0.95, frame="Metal" if layout != 2 else "DarkWood", lamp=False)
    lantern(SIDE.P(dz - 0.75, 1.9, 0.12))
    canopy(SIDE, dz, 1.5, mat="Metal" if layout != 2 else "DarkWood")
    if layout == 1:
        ski_rack(SIDE, dz + 1.5, n=3)
    if layout == 2:
        woodpile(SIDE, dz + 0.75, dz + 2.1, h=0.8)
    if layout in (0, 3):
        window(BACK, 0.4 if layout == 0 else 0.9, 1.5, 1.0, 0.9, frame="Metal")
    drift(-w, w, -d, 0.6, out=1.1)
    if deck > 0 and layout == 0:
        bench(-w + 1.0, 0.06, d + 0.35)
    if smoke:
        flue(-w + 0.9, -d + 0.9, hr(-w + 0.9, -d + 0.9) + 0.1, H + rise + 1.3)

    helper("COL_body", (-w - 0.1, -depth, -d - 0.1), (w + 0.1, H + rise + 0.7, zf + 0.1))
    if deck > 0:
        helper("COL_deck", (-w, -depth, d), (w, 1.1, d + deck))
    so = SIDE.out
    helper("COL_landing", (min(so * w, so * (w + 1.1)), -min(depth, 4.5), -d - 0.5), (max(so * w, so * (w + 1.1)), 2.5, dz + 0.8))
    marker("MARK_front", (0, 0, d))
    marker("LIGHT_porch", (so * (w + 0.6), 2.0, dz))

    # far: the body, the roof's snow, legs, and a window or two a side
    fbox(body, (-w, 0.0, -d), (w, FH if floors > 1 else H, d))
    if floors > 1:
        fbox(upper, (-w - 0.05, FH, -d - 0.05), (w + 0.05, H, zf))
    if rise:   # the wall up to the pitched roof
        solid("Far", [(-w, H, -d), (w, H, -d), (w, H, zf), (-w, H, zf)], [(-w, hr(-w, -d), -d), (w, hr(w, -d), -d), (w, hr(w, zf), zf), (-w, hr(-w, zf), zf)], point=far(top))
    plate("Far", roof_plane(-w - ov, w + ov, -d - ov, zf + ov + 0.3, hr, 0.0), 0.6, point=far("Snow"))
    for x in (-w + 0.1, w - 0.1):
        fbox("Metal", (x - 0.1, -depth, d + deck - 0.2), (x + 0.1, 0.0, d + deck))
    fbox("Metal", (-w, -0.25, -d), (w, 0.0, d + deck))
    fcard("Lit" if layout != 2 else "Dark", (0.0, 1.3, d + 0.03), (0, 0, 1), 2 * w - 1.2, 1.9)
    if floors > 1:
        fcard("Lit" if layout == 3 else "Dark", (0.2, FH + 1.3, zf + 0.03), (0, 0, 1), 2 * w - 1.6, 1.7)
        fcard("Lit", RIGHT.P(-0.6, FH + 1.4, 0.08), RIGHT.n, 1.1, 1.1)
    fcard("Lit" if layout in (0, 2) else "Dark", LEFT.P(0.3, 1.45, 0.03), LEFT.n, 1.1, 1.1)
    fcard("Dark" if layout in (0, 2) else "Lit", RIGHT.P(0.0, 1.45, 0.03), RIGHT.n, 0.8, 1.6)
    M.info = dict(hx=w + 0.1, zb=-d - 0.1, zf=d + deck, skirt=depth, top=H + rise + 1.3)


# ---------------------------------------------------------------- a car on its deck, and a lamp post

def car_deck():
    M = begin("CarDeck")
    w, d, depth = 3.0, 1.6, 6.0
    stilts(w, d, depth, mid=True)
    lohi("Larch", (-w, -0.02, -d), (w, 0.1, d))                                          # planks
    lohi("Metal", (-w, 0.1, d - 0.1), (w, 0.45, d - 0.04))                               # a kerb rail on the open sides
    for s in (-1, 1):
        lohi("Metal", (s * w - 0.05, 0.1, -d + 0.4), (s * w + 0.05, 0.45, d))
    pillow("Snow", (0, 0.5, d - 0.07), (2 * w - 0.2, 0.12, 0.16), 0.04)
    # the car, nose to the right: paint showing under a night's snow
    y = 0.1
    for sx in (-1.3, 1.35):
        for sz in (-0.78, 0.78):
            cyl("Metal", (sx, y + 0.32, sz - 0.11), 0.32, 0.22, 10, along="z")
    solid("Paint", [(-2.1, y + 0.28, -0.85), (2.15, y + 0.28, -0.85), (2.15, y + 0.28, 0.85), (-2.1, y + 0.28, 0.85)],
          [(-2.05, y + 0.92, -0.8), (2.0, y + 0.82, -0.8), (2.0, y + 0.82, 0.8), (-2.05, y + 0.92, 0.8)])
    solid("Metal", [(-1.75, y + 0.9, -0.76), (0.75, y + 0.86, -0.76), (0.75, y + 0.86, 0.76), (-1.75, y + 0.9, 0.76)],     # the glasshouse
          [(-1.45, y + 1.42, -0.66), (0.2, y + 1.42, -0.66), (0.2, y + 1.42, 0.66), (-1.45, y + 1.42, 0.66)])
    pillow("Snow", (-0.62, y + 1.56, 0), (1.75, 0.3, 1.4), 0.2)                          # the roof's load
    solid("Snow", [(0.7, y + 0.84, -0.78), (2.0, y + 0.8, -0.78), (2.0, y + 0.8, 0.78), (0.7, y + 0.84, 0.78)],            # the bonnet
          [(0.55, y + 1.12, -0.62), (1.8, y + 0.98, -0.6), (1.8, y + 0.98, 0.6), (0.55, y + 1.12, 0.62)])
    pillow("Snow", (-1.92, y + 0.99, 0), (0.3, 0.16, 1.4), 0.06)
    pillow("Snow", (-2.3, 0.2, -0.3), (0.9, 0.3, 1.6), 0.2)                              # shovelled off, heaped on the deck
    drift(-w, w, -d, 0.25, out=1.0)
    helper("COL_deck", (-w - 0.1, -depth, -d - 0.1), (w + 0.1, 0.5, d + 0.1))
    helper("COL_car", (-2.2, 0.0, -0.95), (2.25, 1.9, 0.95))
    marker("MARK_front", (0, 0, d))
    fbox("Larch", (-w, -0.25, -d), (w, 0.1, d))
    for x in (-w + 0.1, w - 0.1):
        fbox("Metal", (x - 0.1, -depth, d - 0.2), (x + 0.1, 0.0, d))
    fbox("Paint", (-2.1, 0.35, -0.85), (2.1, 0.95, 0.85))
    fbox("Snow", (-1.6, 0.95, -0.75), (0.5, 1.8, 0.75))
    M.info = dict(hx=w + 0.1, zb=-d - 0.1, zf=d + 0.1, skirt=depth, top=2.0)


def lamp():
    M = begin("Lamp")
    box("Metal", (0, 1.3, 0), (0.1, 4.6, 0.1))               # the post, a metre of it in the snow
    box("Metal", (0.3, 3.55, 0), (0.7, 0.06, 0.06))
    box("Metal", (0.62, 3.47, 0), (0.3, 0.06, 0.3))
    lantern((0.62, 3.3, 0), (0.22, 0.28, 0.22))
    pillow("Snow", (0.62, 3.56, 0), (0.34, 0.12, 0.34), 0.06)
    pillow("Snow", (0.0, 3.64, 0), (0.14, 0.08, 0.14), 0.03)
    marker("LIGHT_lamp", (0.62, 3.1, 0))
    marker("MARK_front", (0, 0, 0.3))
    helper("COL_post", (-0.12, -1.0, -0.12), (0.12, 3.6, 0.12))
    M.info = dict(hx=0.2, zb=-0.2, zf=0.2, skirt=1.0, top=3.7)


chalet()
lodge()
cabin("CabinA", 2.3, 2.3, 1, "flat", "Larch", layout=0)
cabin("CabinB", 2.3, 2.5, 2, "front", "DarkWood", upper="Larch", deck=1.2, layout=1, smoke=True)
cabin("CabinC", 2.9, 2.1, 1, "side", "Larch", upright=True, deck=0.9, layout=2, smoke=True)
cabin("CabinD", 2.1, 2.1, 2, "flat", "Larch", upper="DarkWood", deck=0.0, layout=3, jut=0.45)
car_deck()
lamp()


# ---------------------------------------------------------------- the textures

def paint_textures(out):
    import numpy as np
    from PIL import Image
    CW, CH = 64, 128
    W, H = CW * ATLAS_COLS, CH * ATLAS_ROWS
    day = np.zeros((H, W, 3), np.float32)
    glow = np.zeros((H, W, 3), np.float32)
    rng = np.random.RandomState(7)
    yy, xx = np.mgrid[0:CH, 0:CW].astype(np.float32)
    v, u = yy / (CH - 1), xx / (CW - 1)          # v runs down the pane
    for c in range(ATLAS_COLS * ATLAS_ROWS):
        # by day: dark glass with the pale sky in its upper half, a soft streak across it
        sky = np.clip(1.0 - v * 1.5 + 0.25 * np.sin(u * 3.0 + c), 0, 1)[..., None]
        d = (1 - sky) * np.array([0.07, 0.09, 0.12]) + sky * np.array([0.36, 0.45, 0.54])
        streak = np.clip(1 - np.abs((u + v * 0.6) - 0.55 - 0.1 * c) * 7, 0, 1)[..., None]
        d = d + streak * 0.07
        g = np.zeros((CH, CW, 3), np.float32)
        if c <= 4:
            # at night: a warm room, brighter round a lamp, a curtain or a blind in some
            warm = np.array([[1.0, 0.70, 0.36], [1.0, 0.78, 0.48], [0.95, 0.62, 0.30], [1.0, 0.74, 0.42], [0.92, 0.66, 0.40]][c])
            lx, ly = [(0.3, 0.45), (0.7, 0.35), (0.5, 0.6), (0.25, 0.3), (0.65, 0.55)][c]
            lampg = np.exp(-(((u - lx) / 0.45) ** 2 + ((v - ly) / 0.38) ** 2))[..., None]
            g = warm * (0.42 + 0.58 * lampg)
            if c in (1, 4):      # curtains drawn part way from each side
                cur_ = ((u < 0.22) | (u > 0.78))[..., None]
                fold = (0.8 + 0.2 * np.sin(u * 60))[..., None]
                g = np.where(cur_, warm * 0.75 * fold, g)
                d = np.where(cur_, d * 0.6 + np.array([0.5, 0.47, 0.42]) * 0.4, d)
            if c == 2:           # a blind half down
                bl = (v < 0.4)[..., None]
                g = np.where(bl, warm * 0.55 * (0.85 + 0.15 * np.sin(v * 120))[..., None], g)
                d = np.where(bl, d * 0.5 + np.array([0.55, 0.52, 0.46]) * 0.5, d)
            if c == 3:           # someone's shelf and plant against the light
                sh = ((v > 0.74) & (v < 0.78))[..., None] | (((u - 0.72) ** 2 / 0.012 + (v - 0.66) ** 2 / 0.008) < 1)[..., None]
                g = np.where(sh, g * 0.25, g)
            g = g * (1 - 0.35 * np.clip(v - 0.6, 0, 1)[..., None] * 2)
        elif c == LANTERN:
            d = np.ones((CH, CW, 3), np.float32) * np.array([0.86, 0.82, 0.72])
            g = np.ones((CH, CW, 3), np.float32) * np.array([1.0, 0.86, 0.6])
        else:
            g = np.ones((CH, CW, 3), np.float32) * np.array([0.012, 0.016, 0.024])     # an unlit room: the faintest sky
        if c != LANTERN:
            # glazing bars are part of the picture: a dark edge all round, and a cross or a single bar in some
            edge = ((xx < 2) | (xx > CW - 3) | (yy < 2) | (yy > CH - 3))
            bars = edge.copy()
            if c in (0, 5):
                bars |= (np.abs(yy - CH * 0.38) < 1.5)
            if c in (1, 6):
                bars |= (np.abs(xx - CW * 0.5) < 1.5) | (np.abs(yy - CH * 0.5) < 1.5)
            b3 = bars[..., None]
            d = np.where(b3, np.array([0.10, 0.09, 0.08]), d)
            g = np.where(b3, 0.0, g)
        d = d + (rng.rand(CH, CW, 1) - 0.5) * 0.015
        r0, c0 = (c // ATLAS_COLS) * CH, (c % ATLAS_COLS) * CW
        day[r0:r0 + CH, c0:c0 + CW] = d
        glow[r0:r0 + CH, c0:c0 + CW] = g
    to8 = lambda a: (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(to8(day)).save(os.path.join(out, "Windows.png"))
    Image.fromarray(to8(glow)).save(os.path.join(out, "WindowsGlow.png"))

    # the far version's swatches: each material's colour as the game shows it (texture average times its tint)
    def avg(name, tint):
        p = os.path.join(CAMP, name)
        if not os.path.exists(p):
            return np.array(tint) * 0.6
        a = np.asarray(Image.open(p).convert("RGB"), np.float32) / 255.0
        return a.reshape(-1, 3).mean(0) * np.array(tint)
    sw = {"Larch": avg("Oak.png", LARCH_TINT), "DarkWood": avg("Wood.png", DARK_TINT), "Stone": avg("Stone.png", (0.95, 0.95, 0.95)),
          "Metal": np.array(METAL), "Snow": np.array(SNOW), "Lit": np.array([0.17, 0.2, 0.25]), "Dark": np.array([0.13, 0.16, 0.2]), "Paint": np.array(PAINT)}
    n, px = len(FAR), 8
    fa = np.zeros((px, n * px, 3), np.float32)
    fg = np.zeros((px, n * px, 3), np.float32)
    for k, i in FAR.items():
        fa[:, i * px:(i + 1) * px] = sw[k]
    fg[:, FAR["Lit"] * px:(FAR["Lit"] + 1) * px] = np.array([1.0, 0.72, 0.4])
    Image.fromarray(to8(fa)).save(os.path.join(out, "Far.png"))
    Image.fromarray(to8(fg)).save(os.path.join(out, "FarGlow.png"))


# the tints the game gives the shared materials (Hamlets.cs): keep the two in step
LARCH_TINT, DARK_TINT = (1.0, 0.86, 0.66), (0.62, 0.5, 0.42)
METAL, SNOW, PAINT = (0.17, 0.18, 0.2), (0.92, 0.95, 1.0), (0.62, 0.1, 0.08)

os.makedirs(OUT, exist_ok=True)
paint_textures(OUT)


# ---------------------------------------------------------------- write them out

coll = bpy.context.scene.collection
hm = bpy.data.materials.new("Helper")
roots = {}


def bounds(b):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for v in b.verts:   # back to the game's axes
        p = Vector((v.co.x, v.co.z, v.co.y))
        lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
    return lo, hi


def outward(b):
    """The share of a mesh's area that faces away from its own middle: a check that nothing is inside out."""
    lo, hi = bounds(b)
    c = Vector(((lo.x + hi.x) / 2, (lo.z + hi.z) / 2, (lo.y + hi.y) / 2))
    good = total = 0.0
    for f in b.faces:
        a = f.calc_area()
        total += a
        if f.normal.dot(f.calc_center_median() - c) >= -1e-4:
            good += a
    return good / max(total, 1e-9)


print("%-8s %6s %5s %5s  %-28s %-28s  per material" % ("model", "tris", "far", "draws", "min (m)", "max (m)"))
table = []
for name, M in models.items():
    root = bpy.data.objects.new(name, None)
    coll.objects.link(root)
    roots[name] = root
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    snow_up = 1.0
    for mat, b in M.geo.items():
        if mat != "Far":
            l, h = bounds(b)
            lo, hi = Vector(map(min, lo, l)), Vector(map(max, hi, h))
        b.normal_update()
        if mat == "Snow":   # every roof's snow must face the sky somewhere: at least a third of its area
            up = sum(f.calc_area() for f in b.faces if f.normal.z > 0.5) / max(1e-9, sum(f.calc_area() for f in b.faces))
            snow_up = up
        me = bpy.data.meshes.new(name + "_" + mat)
        b.to_mesh(me)
        b.free()
        m = bpy.data.materials.get(mat) or bpy.data.materials.new(mat)
        me.materials.append(m)
        ob = bpy.data.objects.new(name + "_" + mat, me)   # (named for its material alone in the file: see the export below)
        ob["plain"] = mat
        coll.objects.link(ob)
        ob.parent = root
    for i, (hn, c, s) in enumerate(M.helpers):
        b = bmesh.new()
        bmesh.ops.create_cube(b, size=1.0)
        bmesh.ops.transform(b, matrix=TOB @ mat4(c, s), verts=b.verts)
        me = bpy.data.meshes.new(hn)
        b.to_mesh(me)
        b.free()
        me.materials.append(hm)
        ob = bpy.data.objects.new("%s_%s__%d" % (name, hn, i), me)
        ob["plain"] = "%s__%d" % (hn, i)
        coll.objects.link(ob)
        ob.parent = root
    near = sum(t for k, t in M.tris.items() if k != "Far")
    draws = len([k for k in M.tris if k != "Far"])
    print("%-8s %6d %5d %5d  (%6.2f, %6.2f, %6.2f)     (%6.2f, %6.2f, %6.2f)      %s  snow facing up %.0f%%" % (
        name, near, M.tris.get("Far", 0), draws, lo.x, lo.y, lo.z, hi.x, hi.y, hi.z,
        " ".join("%s %d" % kv for kv in sorted(M.tris.items())), snow_up * 100))
    M.lo, M.hi, M.near = lo, hi, near
    i = M.info
    table.append('new Kind { name = "%s", hx = %.2ff, zBack = %.2ff, zFront = %.2ff, skirt = %.1ff, top = %.1ff },' % (name, i["hx"], i["zb"], i["zf"], i["skirt"], i["top"]))
print("\nFor Hamlets.cs (the footprint the floor is levelled over, and how far the base runs down):")
print("\n".join(table))

for name, root in roots.items():
    # in its file a mesh is called by its material (Larch, Glass, Far) and a helper by its meaning (COL_body__0),
    # as the game reads them; in this scene, where every building stands at once, they carry the building's name too
    kids = [o for o in bpy.data.objects if o.parent == root]
    for o in bpy.data.objects:
        o.select_set(o == root or o.parent == root)
    scene_names = {o: o.name for o in kids}
    for o in kids:
        o.name = o["plain"]
        assert o.name == o["plain"], o.name
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                             mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO", embed_textures=False,
                             use_custom_props=False)
    for o in kids:
        o.name = scene_names[o]
    print("CABINS written", path, os.path.getsize(path) // 1024, "KB")


# ---------------------------------------------------------------- preview pictures (nobody can see the game here)

if RENDER:
    exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "MakeCabinsPreview.py")).read())
