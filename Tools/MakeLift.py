# Builds the chairlift's models in Blender and exports them to Assets/Summit/Resources/Lift/Lift.fbx, with the
# two small palette pictures every part is painted from (LiftPaint.png: colour, LiftGlow.png: what glows at night).
#   blender --background --python Tools/MakeLift.py -- Assets/Summit/Resources/Lift
#   python3 Tools/MakeLift.py -- Assets/Summit/Resources/Lift          (with `pip install bpy`)
#   ... -- Assets/Summit/Resources/Lift --previews Tools/art           also renders the preview pictures
#
# A modern detachable four-seat chairlift, in pieces the game draws in instanced batches (SkiLift.cs):
#   Mast       a plain galvanised tube with a ladder, one metre tall: the game stretches it to each tower's height
#   Head       the top of a tower: T crossarm, a railed catwalk across it, the ladder's cage, work lamps, a beacon
#   Pad        the orange-red padding round a tower's foot
#   Sheaves    one sheave train (eight wheels on rocker beams), set on each end of the crossarm along the cable
#   Chair      a detachable four-seat chair: grip, slim hanger, bail, seat and back, the bar down with its footrests
#   RidersA-D  seated riders for a chair, in varied jackets, skis or a board hanging
#   Station    a terminal on a steel deck on legs: housing, bullwheel, operator's hut, lamps
#   *Lod       plain versions of the above for far away
#
# Everything is authored in the game's own axes: x right, y up, z down the lift's line. Each part is one mesh
# with one material: every face's UVs sit in one cell of the palette, so the whole lift is one material and
# each part one draw call. Origins: Mast, Pad at the foot on the snow; Head and Sheaves where the cable crosses
# the tower; Chair and Riders at the chair's grip on the cable; Station at the middle of its deck.

import bpy, bmesh, math, os, struct, sys, zlib
from mathutils import Vector, Matrix, Euler
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # (Tools/blendkit.py)
from blendkit import mat4, cube, cone
import blendkit

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv and not argv[0].startswith("--") else "."
PREVIEWS = argv[argv.index("--previews") + 1] if "--previews" in argv else None

bpy.ops.wm.read_factory_settings(use_empty=True)

# game axes -> Blender axes, as Tools/MakeChalet.py does it: x stays, game up is Blender +Z, game z is Blender +Y.
# That is a mirror, so every face is also turned round to keep it facing out.
TOB = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

# ---------------------------------------------------------------- the palette

CELLS = 8          # cells a side
CELL_PX = 8        # pixels a cell
PAINT = {          # name: (cell, colour, glow)
    "steel":    (0, (0.66, 0.68, 0.70), None),          # galvanised: pale grey
    "dark":     (1, (0.10, 0.10, 0.11), None),          # rubber, painted black steel
    "liner":    (2, (0.17, 0.20, 0.20), None),          # sheave liners, the bullwheel
    "pad":      (3, (0.90, 0.25, 0.08), None),          # tower padding: orange-red
    "cushion":  (4, (0.09, 0.10, 0.12), None),          # the chair's seat: black padding
    "shell":    (5, (0.90, 0.91, 0.93), None),          # a station's housing
    "accent":   (6, (0.80, 0.11, 0.09), None),          # its stripe
    "deck":     (7, (0.42, 0.43, 0.45), None),          # steel grating
    "glass":    (8, (0.13, 0.20, 0.28), (1.00, 0.80, 0.50)),   # windows: warm light at night
    "lamp":     (9, (0.92, 0.92, 0.86), (1.00, 0.93, 0.78)),   # work lamps
    "beacon":   (10, (0.70, 0.08, 0.06), (1.00, 0.10, 0.06)),  # the red light on top of each tower
    "skin":     (11, (0.86, 0.66, 0.54), None),
    "pantsA":   (12, (0.09, 0.09, 0.11), None),
    "pantsB":   (13, (0.34, 0.36, 0.40), None),
    "pantsC":   (14, (0.45, 0.36, 0.22), None),
    "helmetA":  (15, (0.08, 0.08, 0.09), None),
    "helmetB":  (16, (0.92, 0.92, 0.92), None),
    "goggles":  (17, (0.85, 0.45, 0.10), None),
    "boot":     (18, (0.16, 0.16, 0.18), None),
    "snow":     (19, (0.95, 0.97, 1.00), None),         # the snow on a station's roof
    "plate":    (20, (0.10, 0.38, 0.22), None),         # a tower's number plate
    "jRed":     (24, (0.85, 0.12, 0.10), None),
    "jBlue":    (25, (0.10, 0.32, 0.80), None),
    "jYellow":  (26, (0.97, 0.80, 0.10), None),
    "jGreen":   (27, (0.12, 0.60, 0.28), None),
    "jOrange":  (28, (0.98, 0.45, 0.06), None),
    "jPurple":  (29, (0.48, 0.20, 0.66), None),
    "jTeal":    (30, (0.05, 0.62, 0.62), None),
    "jPink":    (31, (0.95, 0.35, 0.60), None),
    "skiA":     (32, (0.90, 0.90, 0.92), None),
    "skiB":     (33, (0.95, 0.20, 0.25), None),
    "skiC":     (34, (0.15, 0.75, 0.90), None),
    "boardA":   (35, (0.98, 0.55, 0.08), None),
    "boardB":   (36, (0.55, 0.90, 0.20), None),
}


def cell_uv(name):
    c = PAINT[name][0]
    return ((c % CELLS + 0.5) / CELLS, 1.0 - (c // CELLS + 0.5) / CELLS)


def write_png(path, pixels, w, h):
    """pixels: rows from the top, each a list of (r, g, b) 0..255."""
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in pixels)
    blendkit.write(path, blendkit.png(w, h, raw, False), makedirs=False)


def srgb(c):
    return tuple(int(round(255 * max(0.0, min(1.0, v)))) for v in c)


def write_palettes(folder):
    n = CELLS * CELL_PX
    paint = [[(128, 128, 128)] * n for _ in range(n)]
    glow = [[(0, 0, 0)] * n for _ in range(n)]
    for name, (c, col, g) in PAINT.items():
        for y in range(CELL_PX):
            for x in range(CELL_PX):
                py, px = (c // CELLS) * CELL_PX + y, (c % CELLS) * CELL_PX + x
                paint[py][px] = srgb(col)
                if g is not None:
                    glow[py][px] = srgb(g)
    write_png(os.path.join(folder, "LiftPaint.png"), paint, n, n)
    write_png(os.path.join(folder, "LiftGlow.png"), glow, n, n)


# ---------------------------------------------------------------- building

geo = {}      # part name -> bmesh (Blender space)
tris = {}     # part name -> triangles
lo, hi = {}, {}   # bounds in game space


def mesh_for(part):
    if part not in geo:
        b = bmesh.new()
        b.loops.layers.uv.new("UVMap")
        geo[part] = b
        tris[part] = 0
        lo[part] = Vector((1e9, 1e9, 1e9)); hi[part] = Vector((-1e9, -1e9, -1e9))
    return geo[part]


PART = "Chair"   # the part being built


# (liftlook) parts drawn with a texture of their own instead of the palette (SkiLift gives each its material): their
# UVs are the game-space metres across each face (box-projected: a level face by x and z, an upright one by its
# width and y, so streaks run down), over this many metres a tile
TEXPARTS = {}


def add(src, paint, m, smooth=False):
    """Copy a temp bmesh into the current part through matrix m (game space), painted one palette colour (or, in a
    textured part, mapped in metres: TEXPARTS)."""
    dst = mesh_for(PART)
    uv = dst.loops.layers.uv.active
    u, v = cell_uv(paint)
    tile = TEXPARTS.get(PART)
    vmap, gco = {}, {}
    for vert in src.verts:
        p = m @ vert.co
        for i in range(3):
            lo[PART][i] = min(lo[PART][i], p[i]); hi[PART][i] = max(hi[PART][i], p[i])
        nv = dst.verts.new(TOB @ p)
        vmap[vert] = nv; gco[nv] = p
    flip = m.to_3x3().determinant() > 0   # the mirror above turns faces round; a mirrored m turns them back
    for f in src.faces:
        vs = [vmap[x] for x in f.verts]
        nf = dst.faces.new(list(reversed(vs)) if flip else vs)
        nf.smooth = smooth and f.smooth
        if tile is None:
            for l in nf.loops:
                l[uv].uv = (u, v)
        else:
            pts = [gco[x] for x in vs]
            nrm = Vector((0.0, 0.0, 0.0))
            for i in range(len(pts)):   # (Newell's normal: any polygon)
                a, b = pts[i], pts[(i + 1) % len(pts)]
                nrm += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
            ax = max(range(3), key=lambda i: abs(nrm[i]))
            ia, ib = {0: (2, 1), 1: (0, 2), 2: (0, 1)}[ax]
            for l in nf.loops:
                q = gco[l.vert]
                l[uv].uv = (q[ia] / tile, q[ib] / tile)
        tris[PART] += len(f.verts) - 2
    src.free()




def box(paint, pos, size, rot=(0, 0, 0)):
    add(cube(), paint, mat4(pos, size, rot))


def tube(paint, a, b_, r, seg=6, r2=None, caps=True):
    """A round bar from a to b (game space)."""
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    b = cone(r, d.length, seg, r2, caps=caps)
    for f in b.faces:
        f.smooth = len(f.verts) == 4
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    add(b, paint, Matrix.Translation((a + b_) / 2) @ rot, smooth=True)


def beam(paint, a, b_, w, t):
    """A box-section beam from a to b, w wide and t deep."""
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    add(cube(), paint, Matrix.Translation((a + b_) / 2) @ rot @ Matrix.Diagonal(Vector((w, t, d.length, 1))))


def strip(paint, pts, across, w):
    """A flat bar through pts (game space), w wide along `across`, seen from both sides."""
    h = Vector(across).normalized() * (w / 2)
    b = bmesh.new()
    for face in (1, -1):
        vs = [(b.verts.new(Vector(p) - h), b.verts.new(Vector(p) + h)) for p in pts]
        for i in range(len(pts) - 1):
            q = [vs[i][0], vs[i + 1][0], vs[i + 1][1], vs[i][1]]
            b.faces.new(q if face > 0 else list(reversed(q)))
    add(b, paint, Matrix.Identity(4))


def wheel(paint, pos, r, w, seg=10):
    """A disc on an axle along x."""
    tube(paint, (pos[0] - w / 2, pos[1], pos[2]), (pos[0] + w / 2, pos[1], pos[2]), r, seg)


def ball(paint, pos, size, seg=8):
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=seg, v_segments=max(4, seg // 2), radius=0.5)
    for f in b.faces:
        f.smooth = True
    add(b, paint, mat4(pos, size), smooth=True)


# ---------------------------------------------------------------- sizes the game shares (SkiLift.cs, LiftLine.cs)

GAUGE = 5.2            # between the two cables
HALF = GAUGE / 2
SEAT = 2.9             # the seat, below the cable
CHAIR_WIDTH = 2.2
STATION_CABLE = 4.6    # the cable above a station's deck
STATION_HALF_LEN, STATION_HALF_WID = 6.0, 5.5
WHEEL_Z = -2.4         # the bullwheel's centre along a station (its portal is at +STATION_HALF_LEN)
LEGS = 14.0            # how far a station's legs reach below its deck

# ---------------------------------------------------------------- tower
# After the lifts at Whistler: a tall plain galvanised tube, a T crossarm with a sheave train under each end, a
# railed catwalk across the head with the ladder coming up through a safety cage, and an orange-red pad at the snow.

MAST_R0, MAST_R1 = 0.36, 0.30
LADDER_Z0, LADDER_Z1 = -(MAST_R0 + 0.09), -(MAST_R1 + 0.09)

PART = "Mast"
# one metre of mast: the game scales it along y from 1.5 m under the snow to the head
tube("steel", (0, 0, 0), (0, 1, 0), MAST_R0, seg=12, r2=MAST_R1, caps=False)
for sx in (-0.2, 0.2):                                    # the ladder up its uphill face
    beam("steel", (sx, 0.12, LADDER_Z0), (sx, 1.0, LADDER_Z1), 0.035, 0.035)
for i in range(22):
    y = 0.14 + i * 0.039
    box("steel", (0, y, LADDER_Z0 + (y - 0.12) / 0.88 * (LADDER_Z1 - LADDER_Z0)), (0.4, 0.0025, 0.03))
for y in (0.3, 0.62, 0.94):                               # the flanges where its lengths are bolted together
    r = MAST_R0 + (MAST_R1 - MAST_R0) * y + 0.035
    tube("steel", (0, y - 0.004, 0), (0, y + 0.004, 0), r, seg=12)

PART = "MastLod"
tube("steel", (0, 0, 0), (0, 1, 0), MAST_R0, seg=5, r2=MAST_R1, caps=False)

PART = "Pad"
# the padding strapped round its foot: orange-red, in two halves, a dark strap round the middle
tube("pad", (0, -0.4, 0), (0, 0.86, 0), 0.74, seg=14, caps=False)
tube("dark", (0, 0.86, 0), (0, 0.96, 0), 0.755, seg=14, caps=False)
tube("pad", (0, 0.96, 0), (0, 1.82, 0), 0.74, seg=14, caps=False)
tube("pad", (0, 1.82, 0), (0, 1.94, 0), 0.74, seg=14, r2=0.46)
for a in (90, 270):                                       # the seam down each side
    x, z = 0.75 * math.cos(math.radians(a)), 0.75 * math.sin(math.radians(a))
    box("dark", (x, 0.9, z), (0.05, 1.84, 0.03))

ARM_Y = 0.95           # the crossarm's middle, above the cable
ARM_HALF = HALF + 0.85
WALK_Y = ARM_Y + 0.18  # the catwalk's deck, on top of the arm
WALK_HALF = HALF - 0.9 # it stops short of the cables: a rider hopping the tower off a cable passes outside it
RAIL = 1.1
TRAIN = 4.2            # a sheave train's length
PART = "Head"
tube("steel", (0, -0.3, 0), (0, ARM_Y + 0.1, 0), MAST_R1, seg=12)                # the mast's last stretch
box("steel", (0, ARM_Y, 0), (2 * ARM_HALF, 0.36, 0.42))                          # the crossarm
for s in (-1, 1):
    box("steel", (s * ARM_HALF, ARM_Y, 0), (0.05, 0.5, 0.6))                     # its end plates
    box("steel", (s * 0.62, ARM_Y - 0.4, 0), (0.85, 0.42, 0.06), rot=(0, 0, s * 40))   # gussets to the mast
    # the yoke each sheave train hangs from, on the inside of the cable so the chairs' hangers pass outside
    box("steel", (s * (HALF - 0.42), ARM_Y - 0.55, 0), (0.14, 0.9, 0.3))
    box("steel", (s * (HALF - 0.21), 0.02 - 0.52, 0), (0.5, 0.12, 0.28))
    # a work platform outside each cable, along it, with a railing
    px = s * (HALF + 0.78)
    box("deck", (px, -0.62, 0), (0.5, 0.04, TRAIN))
    for z in (-TRAIN / 2, 0, TRAIN / 2):
        tube("steel", (px + s * 0.23, -0.62, z), (px + s * 0.23, 0.42, z), 0.02, seg=4, caps=False)
    for z in (-TRAIN / 2 + 0.2, TRAIN / 2 - 0.2):
        beam("steel", (px - s * 0.2, -0.6, z), (s * (ARM_HALF - 0.1), ARM_Y - 0.1, 0), 0.04, 0.04)   # hangers up to the arm
    for y in (-0.1, 0.42):
        tube("steel", (px + s * 0.23, y, -TRAIN / 2), (px + s * 0.23, y, TRAIN / 2), 0.02, seg=4, caps=False)
# the catwalk across the head: grating on the arm, a railing either side leaning out, wider at the top
box("deck", (0, WALK_Y + 0.025, 0), (2 * WALK_HALF, 0.05, 0.95))
TOP_HALF = WALK_HALF + 0.3
for sz in (-1, 1):
    for i in range(5):
        u = -1 + i * 0.5
        tube("steel", (u * WALK_HALF, WALK_Y, sz * 0.46), (u * TOP_HALF, WALK_Y + RAIL, sz * 0.6), 0.022, seg=4, caps=False)
    for k in (0.5, 1.0):
        half = WALK_HALF + (TOP_HALF - WALK_HALF) * k
        tube("steel", (-half, WALK_Y + RAIL * k, sz * (0.46 + 0.14 * k)), (half, WALK_Y + RAIL * k, sz * (0.46 + 0.14 * k)), 0.022, seg=4, caps=False)
    box("steel", (0, WALK_Y + 0.09, sz * 0.47), (2 * WALK_HALF, 0.1, 0.012))       # the kick plate
for s in (-1, 1):
    for k in (0.5, 1.0):
        half = WALK_HALF + (TOP_HALF - WALK_HALF) * k
        z = 0.46 + 0.14 * k
        tube("steel", (s * half, WALK_Y + RAIL * k, -z), (s * half, WALK_Y + RAIL * k, z), 0.022, seg=4, caps=False)
# the ladder comes up the mast's uphill face through a cage of hoops to a gap in the railing
LZ = LADDER_Z1
for sx in (-0.2, 0.2):
    beam("steel", (sx, -0.05, LZ), (sx, WALK_Y + RAIL, LZ), 0.035, 0.035)
for i in range(4):
    box("steel", (0, 0.1 + i * 0.3, LZ), (0.4, 0.025, 0.03))
HOOPS = (-3.3, -2.5, -1.7, -0.9, -0.1, 0.7)
def hoop_pt(a, y):
    return Vector((0.37 * math.cos(a), y, LZ - 0.66 * math.sin(a)))
for y in HOOPS:
    strip("steel", [hoop_pt(math.pi * i / 6, y) for i in range(7)], (0, 1, 0), 0.05)
for i in (1, 3, 5):
    a = math.pi * i / 6
    t = Vector((-math.sin(a), 0, -math.cos(a)))
    strip("steel", [hoop_pt(a, HOOPS[0]), hoop_pt(a, HOOPS[-1])], t, 0.04)
# the red light on its short post over the catwalk, work lamps under the arm looking down each cable, the number plate
tube("steel", (0, WALK_Y, 0.3), (0, WALK_Y + 1.62, 0.3), 0.03, seg=4, caps=False)
box("beacon", (0, WALK_Y + 1.74, 0.3), (0.16, 0.2, 0.16))
box("dark", (0, WALK_Y + 1.63, 0.3), (0.22, 0.06, 0.22))
for s in (-1, 1):
    box("dark", (s * 1.7, ARM_Y - 0.25, 0), (0.3, 0.12, 0.26))
    box("lamp", (s * 1.7, ARM_Y - 0.32, 0), (0.24, 0.04, 0.2))
box("plate", (0, -1.0, MAST_R1 + 0.02), (0.44, 0.44, 0.03))

PART = "HeadLod"
box("steel", (0, ARM_Y / 2, 0), (0.5, ARM_Y + 0.6, 0.5))
box("steel", (0, ARM_Y, 0), (2 * ARM_HALF, 0.36, 0.42))
for s in (-1, 1):
    box("deck", (s * (HALF + 0.78), -0.1, 0), (0.5, 1.05, TRAIN))
    box("steel", (s * (WALK_HALF + 0.15), WALK_Y + RAIL / 2, 0), (0.06, RAIL, 1.1))     # the catwalk's railing, as its outline
for sz in (-1, 1):
    box("steel", (0, WALK_Y + RAIL, sz * 0.6), (2 * TOP_HALF, 0.06, 0.06))
box("steel", (0, WALK_Y - 1.6, LZ - 0.33), (0.7, 3.6, 0.06))                           # the ladder's cage
box("beacon", (0, WALK_Y + 1.74, 0.3), (0.16, 0.2, 0.16))
box("lamp", (0, ARM_Y - 0.32, 0), (3.6, 0.04, 0.2))

PART = "Sheaves"
# a sheave train: eight wheels under the cable in four pairs, each pair on a rocker, the rockers on one beam.
# The cable lies along z through the origin; the yoke that carries the train is toward -x.
for i in range(8):
    z = -2.1 + i * 0.6
    wheel("liner", (0, -0.235, z), 0.21, 0.09, seg=10)
    wheel("steel", (-0.02, -0.235, z), 0.085, 0.16, seg=5)
for i in range(4):
    z = -1.8 + i * 1.2
    for sx in (-0.085, 0.085):
        box("steel", (sx, -0.3, z), (0.02, 0.2, 0.95))                           # the rocker's side plates
    box("steel", (-0.1, -0.44, z), (0.12, 0.16, 0.14))
box("steel", (-0.16, -0.52, 0), (0.14, 0.14, TRAIN - 0.2))                       # the main beam
box("dark", (0, -0.03, -2.46), (0.06, 0.05, 0.3))                                # cable catchers at each end
box("dark", (0, -0.03, 2.46), (0.06, 0.05, 0.3))

PART = "SheavesLod"
box("liner", (0, -0.24, 0), (0.1, 0.42, TRAIN + 0.4))
box("steel", (-0.16, -0.52, 0), (0.14, 0.14, TRAIN - 0.2))

# ---------------------------------------------------------------- chair
# A detachable quad: the grip with its running wheels, a slim hanger cranked back over the riders' heads, a
# light tube frame round a dark slatted seat, and the restraint bar down with its footrests.

BAIL_Y = -1.75
BACK_Z = -0.27
BAR_Y, BAR_Z = SEAT + 0.0, 0.5
HANG_Z = -0.2
PART = "Chair"
box("dark", (0, 0.02, 0), (0.12, 0.17, 0.56))                                     # the grip's jaw on the cable
box("steel", (0.0, -0.13, 0), (0.09, 0.16, 0.3))                                  # its body
for z in (-0.19, 0.19):
    wheel("liner", (0.1, 0.07, z), 0.075, 0.05, seg=6)                            # the wheels it runs on through a station
box("dark", (0.1, -0.03, 0), (0.03, 0.05, 0.5))                                   # and the lever that opens it
tube("steel", (0, -0.2, 0), (0, -0.62, HANG_Z), 0.04, seg=6)                       # the hanger
tube("steel", (0, -0.62, HANG_Z), (0, BAIL_Y + 0.3, HANG_Z), 0.04, seg=6)
tube("steel", (0, BAIL_Y + 0.3, HANG_Z), (0, BAIL_Y, 0), 0.04, seg=6)
CW = CHAIR_WIDTH / 2
tube("steel", (-CW + 0.14, BAIL_Y, 0), (CW - 0.14, BAIL_Y, 0), 0.032, seg=6)       # the bail
for s in (-1, 1):
    x = s * CW
    tube("steel", (s * (CW - 0.14), BAIL_Y, 0), (x, BAIL_Y - 0.16, -0.02), 0.032, seg=5)   # its rounded shoulders
    tube("steel", (x, BAIL_Y - 0.16, -0.02), (x, -SEAT + 0.55, BACK_Z), 0.03, seg=5)        # down to the seat's back corners
    tube("steel", (x, -SEAT + 0.55, BACK_Z), (x, -SEAT - 0.02, BACK_Z + 0.03), 0.03, seg=5)
    tube("steel", (x, -SEAT - 0.02, BACK_Z + 0.03), (x, -SEAT - 0.02, 0.27), 0.03, seg=5)   # under the seat's ends
    tube("steel", (x, -SEAT + 0.2, BACK_Z + 0.02), (x, -SEAT + 0.2, 0.2), 0.022, seg=4)     # armrests
    # the restraint bar's arms, pivoting on the frame
    tube("steel", (x, BAIL_Y - 0.25, -0.02), (x, -SEAT + 0.42, BAR_Z), 0.026, seg=5)
box("cushion", (0, -SEAT + 0.02, 0.0), (CHAIR_WIDTH - 0.06, 0.06, 0.5))            # the seat
for y in (0.2, 0.47):                                                              # its back: two padded slats
    box("cushion", (0, -SEAT + y, BACK_Z + (0.36 - y) * 0.12), (CHAIR_WIDTH - 0.06, 0.2, 0.05), rot=(-7, 0, 0))
tube("steel", (-CW, -SEAT + 0.64, BACK_Z - 0.04), (CW, -SEAT + 0.64, BACK_Z - 0.04), 0.026, seg=5)   # the rail along its top
tube("steel", (-CW, -SEAT + 0.42, BAR_Z), (CW, -SEAT + 0.42, BAR_Z), 0.028, seg=6)   # the bar
for s in (-1, 1):
    # a footrest for each pair of seats: a post down from the bar and a T at its foot
    tube("steel", (s * 0.55, -SEAT + 0.42, BAR_Z), (s * 0.55, -SEAT - 0.5, BAR_Z + 0.14), 0.022, seg=5)
    tube("dark", (s * 0.55 - 0.42, -SEAT - 0.5, BAR_Z + 0.14), (s * 0.55 + 0.42, -SEAT - 0.5, BAR_Z + 0.14), 0.028, seg=5)

PART = "ChairLod"
box("dark", (0, 0.02, 0), (0.12, 0.17, 0.56))
beam("steel", (0, -0.2, 0), (0, BAIL_Y, HANG_Z / 2), 0.08, 0.08)
box("steel", (0, BAIL_Y, 0), (CHAIR_WIDTH, 0.06, 0.06))
for s in (-1, 1):
    beam("steel", (s * CW, BAIL_Y, 0), (s * CW, -SEAT, BACK_Z), 0.06, 0.06)
box("cushion", (0, -SEAT + 0.02, 0.0), (CHAIR_WIDTH, 0.08, 0.5))
box("cushion", (0, -SEAT + 0.36, BACK_Z), (CHAIR_WIDTH, 0.5, 0.05))
box("steel", (0, -SEAT + 0.42, BAR_Z), (CHAIR_WIDTH, 0.055, 0.055))

# ---------------------------------------------------------------- riders

SEATS = (-0.825, -0.275, 0.275, 0.825)


def rider(seat, jacket, pants, helmet, gear, paint, tall=1.0):
    """A rider sitting on seat 0..3 behind the bar: skis on the footrest, or a board hanging from one foot."""
    x = SEATS[seat]
    hip = -SEAT + 0.13
    box(pants, (x, hip - 0.01, 0.0), (0.36, 0.15, 0.3))                            # the seat of the trousers
    for s in (-1, 1):
        lx = x + s * 0.1
        tube(pants, (lx, hip + 0.02, 0.05), (lx, hip + 0.0, 0.46), 0.085, seg=6, r2=0.07)          # thighs
        tube(pants, (lx, hip + 0.0, 0.46), (lx, -SEAT - 0.34, 0.56), 0.07, seg=6, r2=0.06)         # shins
        box("boot", (lx, -SEAT - 0.4, 0.61), (0.125, 0.16, 0.31))
    chest = hip + 0.34 * tall
    ball(jacket, (x, chest, -0.07), (0.45, 0.62 * tall, 0.3), seg=8)               # the body, in a padded jacket
    sh = hip + 0.54 * tall
    for s in (-1, 1):
        # arms forward to the bar
        el = (x + s * 0.24, hip + 0.2, 0.16)
        tube(jacket, (x + s * 0.22, sh, -0.08), el, 0.07, seg=5, r2=0.06)
        tube(jacket, el, (x + s * 0.15, -SEAT + 0.44, BAR_Z - 0.04), 0.06, seg=5, r2=0.05)
        box("dark", (x + s * 0.15, -SEAT + 0.44, BAR_Z - 0.02), (0.09, 0.1, 0.11))  # gloves
    head = hip + 0.64 * tall + 0.19
    box("skin", (x, head - 0.07, -0.075), (0.15, 0.16, 0.16))
    ball(helmet, (x, head, -0.11), (0.27, 0.26, 0.29), seg=8)
    box("goggles", (x, head + 0.0, 0.02), (0.2, 0.08, 0.04))
    if gear == "ski":
        for s in (-1, 1):
            box(paint, (x + s * 0.1, -SEAT - 0.5, 0.82), (0.085, 0.022, 1.62), rot=(-9, 0, 0))     # tips up a little
            box(paint, (x + s * 0.1, -SEAT - 0.345, 1.66), (0.085, 0.022, 0.16), rot=(-40, 0, 0))  # and turned up at the end
    else:
        box(paint, (x + 0.05, -SEAT - 0.56, 0.72), (0.27, 0.025, 1.45), rot=(-24, 38, 0))
        for s in (-1, 1):
            box("dark", (x + 0.05 + s * 0.14, -SEAT - 0.5 + s * 0.06, 0.66 + s * 0.2), (0.14, 0.07, 0.24), rot=(-24, 38, 0))


def rider_lod(seat, jacket, pants, helmet):
    x = SEATS[seat]
    hip = -SEAT + 0.13
    box(pants, (x, hip - 0.12, 0.3), (0.36, 0.5, 0.6))
    box(jacket, (x, hip + 0.36, -0.06), (0.44, 0.58, 0.3))
    box(helmet, (x, hip + 0.8, -0.1), (0.25, 0.25, 0.27))


RIDERS = {
    "A": [(1, "jRed", "pantsA", "helmetA", "ski", "skiA", 1.0), (2, "jBlue", "pantsB", "helmetB", "ski", "skiB", 0.94)],
    "B": [(0, "jOrange", "pantsA", "helmetB", "board", "boardB", 1.0), (2, "jGreen", "pantsC", "helmetA", "ski", "skiC", 1.03),
          (3, "jYellow", "pantsB", "helmetA", "ski", "skiA", 0.9)],
    "C": [(2, "jTeal", "pantsC", "helmetA", "board", "boardA", 1.0)],
    "D": [(0, "jPurple", "pantsB", "helmetB", "ski", "skiB", 0.97), (1, "jPink", "pantsA", "helmetB", "ski", "skiA", 0.88),
          (2, "jBlue", "pantsA", "helmetA", "board", "boardB", 1.02), (3, "jRed", "pantsC", "helmetA", "ski", "skiC", 1.0)],
}
for name, people in RIDERS.items():
    PART = "Riders" + name
    for p in people:
        rider(*p)
    PART = "Riders" + name + "Lod"
    for p in people:
        rider_lod(p[0], p[1], p[2], p[3])

# ---------------------------------------------------------------- station
# (liftlook) Most of a station is drawn with textures of its own (TEXPARTS, SkiLift's materials): the galvanised steel,
# weathered (the deck's slab and frame, the legs and their bracing, the column, the guide rails); the deck's planks;
# the housing's bolted panels, white, with its stripe and the sign over the mouth in red; a dark bolted skirt along the
# housing's sides over the bullwheel; black rubber matting in each lane where riders load and unload; the painted lines
# (a load or unload line across each lane, the deck's ends striped). The palette keeps the glass, the lamps, the
# bullwheel, the snow on the roof. The railings and the operator's hut are parts of their own, one for each side:
# a top station's unload wing (LiftLine.UnloadWing) opens one side, and SkiLift draws neither railing nor hut there.

TEXPARTS.update({"StationGalv": 2.0, "StationDeck": 1.0, "StationShell": 2.0, "StationAccent": 2.0, "StationCowl": 2.0,
                 "StationMat": 1.0, "StationLine": 1.0, "RailR": 2.0, "RailL": 2.0})
W, L = STATION_HALF_WID, STATION_HALF_LEN

PART = "StationDeck"
box("deck", (0, -0.02, 0), (2 * W, 0.04, 2 * L))                                   # the planks on top

PART = "StationGalv"
box("deck", (0, -0.21, 0), (2 * W, 0.34, 2 * L))                                   # the deck's slab under them
box("steel", (0, -0.5, 0), (2 * W - 0.6, 0.3, 2 * L - 0.6))                        # its frame
for sx in (-1, 1):
    for z in (-L + 0.5, 0, L - 0.5):
        box("steel", (sx * (W - 0.5), -LEGS / 2 - 0.3, z), (0.5, LEGS, 0.5))       # legs, down into the snow
    for (z0, z1) in ((-L + 0.5, 0), (0, L - 0.5)):
        for (ya, yb) in ((-0.6, -5.2), (-5.2, -0.6), (-5.2, -9.8), (-9.8, -5.2)):
            beam("steel", (sx * (W - 0.5), ya, z0), (sx * (W - 0.5), yb, z1), 0.14, 0.14)   # bracing
    for y in (-5.2, -9.8):
        beam("steel", (sx * (W - 0.5), y, -L + 0.5), (sx * (W - 0.5), y, L - 0.5), 0.2, 0.2)
for y in (-5.2, -9.8):
    for z in (-L + 0.5, 0, L - 0.5):
        beam("steel", (-(W - 0.5), y, z), (W - 0.5, y, z), 0.2, 0.2)
# the column that carries the terminal, inside the chairs' loop
box("steel", (0, 2.5, -0.2), (0.9, 5.0, 1.1))
box("steel", (0, 0.15, -0.2), (1.5, 0.3, 1.7))
# guide rails the chairs run along through the terminal, either side
for sx in (-1, 1):
    box("steel", (sx * HALF, STATION_CABLE + 0.2, (WHEEL_Z + L) / 2), (0.16, 0.12, L - WHEEL_Z))

# the railing along each side (the ends are open: the chairs come and go at the front, riders leave at the back)
for part, sx in (("RailR", 1), ("RailL", -1)):
    PART = part
    for z in (-L + 0.1, -L / 2, 0, L / 2, L - 0.1):
        box("steel", (sx * (W - 0.1), 0.55, z), (0.07, 1.1, 0.07))
    for y in (0.55, 1.1):
        box("steel", (sx * (W - 0.1), y, 0), (0.06, 0.06, 2 * L - 0.2))
    box("steel", (sx * (W - 0.1), 0.08, 0), (0.04, 0.16, 2 * L - 0.2))             # a kick plate along the foot

# the terminal's housing over the cable: bolted panels between dark ribs, its red stripe, framed windows down each
# side, a service door at the back on a small landing, a roof that overhangs the walls under a cap of snow, the sign
# over the mouth
HY0, HY1 = STATION_CABLE + 0.35, STATION_CABLE + 2.75
HZ0, HZ1 = -L + 0.1, L - 0.4
HW = HALF + 1.15
HZM = (HZ0 + HZ1) / 2
PART = "StationShell"
box("shell", (0, (HY0 + HY1) / 2, HZM), (2 * HW, HY1 - HY0, HZ1 - HZ0))
PART = "StationAccent"
box("accent", (0, HY0 + 0.45, HZM), (2 * HW + 0.06, 0.34, HZ1 - HZ0 + 0.06))
box("accent", (0, HY1 - 0.5, HZ1 + 0.05), (2.6, 0.6, 0.06))                        # the sign over the mouth
PART = "StationCowl"
# a dark bolted skirt along the housing's sides, down over the bullwheel (outside the chairs' way through)
for sx in (-1, 1):
    box("dark", (sx * (HW + 0.03), HY0 - 0.12, HZM), (0.06, 0.5, HZ1 - HZ0))
box("dark", (0, HY0 - 0.03, WHEEL_Z - HALF - 0.35), (2 * HW, 0.32, 0.06))          # and round its back, behind the wheel
# the ribs between its panels: down each side every 1.45 m, across each end every 1.5 m, its corners, a trim along
# its foot and under its eaves
BAYS = [HZ0 + 0.15 + i * (HZ1 - HZ0 - 0.3) / 8 for i in range(9)]
for sx in (-1, 1):
    for z in BAYS:
        box("dark", (sx * (HW + 0.035), (HY0 + HY1) / 2, z), (0.07, HY1 - HY0, 0.09))
    for y in (HY0 + 0.06, HY1 - 0.07):
        box("dark", (sx * (HW + 0.04), y, HZM), (0.08, 0.12, HZ1 - HZ0 + 0.08))
for sz, zf in ((1, HZ1), (-1, HZ0)):
    for x in (-HW + 0.75, -HW + 2.25, HW - 2.25, HW - 0.75):
        box("dark", (x, (HY0 + HY1) / 2, zf + sz * 0.035), (0.09, HY1 - HY0, 0.07))
    for y in (HY0 + 0.06, HY1 - 0.07):
        box("dark", (0, y, zf + sz * 0.04), (2 * HW + 0.08, 0.12, 0.08))
# its roof: a slab out past the walls all round, its edge dark and bolted
box("dark", (0, HY1 + 0.15, HZM), (2 * HW + 0.9, 0.3, HZ1 - HZ0 + 0.9))
# the service door at the back, its landing (grating on brackets, a rail round it)
DX = -(HW - 0.75)
box("dark", (DX, HY0 + 1.0, HZ0 - 0.03), (0.92, 1.95, 0.05))
box("dark", (DX + 0.32, HY0 + 1.0, HZ0 - 0.07), (0.06, 0.06, 0.05))                # its handle
PART = "StationGalv"
box("deck", (DX, HY0 - 0.04, HZ0 - 0.45), (1.5, 0.08, 0.9))
for x in (DX - 0.72, DX + 0.72):
    beam("steel", (x, HY0 - 0.08, HZ0 - 0.85), (x, HY0 - 0.7, HZ0 - 0.02), 0.08, 0.08)
    box("steel", (x, HY0 + 0.5, HZ0 - 0.86), (0.05, 1.0, 0.05))
box("steel", (DX, HY0 + 1.0, HZ0 - 0.86), (1.5, 0.05, 0.05))
box("steel", (DX, HY0 + 0.5, HZ0 - 0.86), (1.5, 0.04, 0.04))

PART = "Station"
# its windows, each in its bay between the ribs: glass in a dark frame with a bar down the middle
for sx in (-1, 1):
    for i in range(1, 7):
        z0, z1 = BAYS[i], BAYS[i + 1]
        zc, wz = (z0 + z1) / 2, (z1 - z0) - 0.36
        yc = HY0 + 1.42
        box("glass", (sx * (HW + 0.01), yc, zc), (0.04, 0.8, wz))
        for dy in (-0.43, 0.43):
            box("dark", (sx * (HW + 0.05), yc + dy, zc), (0.07, 0.07, wz + 0.14))
        for dz in (-(wz / 2 + 0.035), wz / 2 + 0.035, 0.0):
            box("dark", (sx * (HW + 0.05), yc, zc + dz), (0.07, 0.86, 0.06))
# the dark mouth at the front, and the snow on its roof: a cap out to the eaves, drooping over them front and back
box("dark", (0, HY0 + 0.9, HZ1 + 0.02), (2 * HW - 1.0, 1.3, 0.05))
box("snow", (0, HY1 + 0.48, HZM), (2 * HW + 0.75, 0.36, HZ1 - HZ0 + 0.75))
box("snow", (0, HY1 + 0.74, HZM), (2 * HW + 0.2, 0.2, HZ1 - HZ0 + 0.2))
for sz in (-1, 1):
    box("snow", (0, HY1 + 0.36, HZM + sz * ((HZ1 - HZ0) / 2 + 0.47)), (2 * HW + 0.6, 0.3, 0.18))
for sx in (-1, 1):
    box("snow", (sx * (HW + 0.47), HY1 + 0.36, HZM), (0.18, 0.3, HZ1 - HZ0 + 0.6))
# snow drifted round the column's foot
box("snow", (0, 0.08, -0.2), (2.1, 0.16, 2.3))
box("snow", (0, 0.2, -0.2), (1.7, 0.12, 1.9))
# the bullwheel under it, on its shaft
b = bmesh.new()
bmesh.ops.create_cone(b, cap_ends=True, segments=24, radius1=HALF, radius2=HALF, depth=0.2)
for f in b.faces:
    f.smooth = len(f.verts) == 4
add(b, "liner", mat4((0, STATION_CABLE - 0.03, WHEEL_Z), rot=(-90, 0, 0)), smooth=True)
tube("pad", (0, STATION_CABLE - 0.2, WHEEL_Z), (0, STATION_CABLE + 0.12, WHEEL_Z), 0.45, seg=10)
tube("steel", (0, STATION_CABLE, WHEEL_Z), (0, HY0, WHEEL_Z), 0.22, seg=8)
# floodlights under the front edge and along the sides
for sx in (-1, 1):
    box("dark", (sx * (HW - 0.5), HY0 - 0.08, HZ1 - 0.3), (0.5, 0.14, 0.3))
    box("lamp", (sx * (HW - 0.5), HY0 - 0.16, HZ1 - 0.3), (0.42, 0.04, 0.24))
    box("lamp", (sx * (HW + 0.1), HY0 + 0.1, 0), (0.04, 0.1, 1.6))
# (over the door, a lamp)
box("lamp", (DX, HY0 + 2.12, HZ0 - 0.12), (0.3, 0.06, 0.2))

# rubber matting in each lane, where the chairs come in at the top and riders sit down at the bottom; the painted
# lines: a load or unload line across each lane, the deck's ends striped wide, a stripe along each side's edge
PART = "StationMat"
for sx in (-1, 1):
    box("dark", (sx * HALF, 0.012, 1.9), (2.5, 0.024, 6.0))
PART = "StationLine"
for sx in (-1, 1):
    box("accent", (sx * HALF, 0.03, 4.75), (2.5, 0.012, 0.4))
    box("accent", (sx * (W - 0.1), 0.006, 0), (0.18, 0.012, 2 * L - 1.0))
for sz in (-1, 1):
    box("accent", (0, 0.008, sz * (L - 0.26)), (2 * W - 0.1, 0.016, 0.5))

# snow drifted against each railing's foot at the deck's corners (drawn with the railing: none on the open side)
def wedge(paint, x_wall, x_in, z0, z1, h):
    """A drift: a wedge of snow h high against a wall at x_wall, thinning to nothing at x_in, from z0 to z1."""
    bm = bmesh.new()
    pts = [(x_wall, 0, z0), (x_wall, h, z0), (x_in, 0, z0), (x_wall, 0, z1), (x_wall, h, z1), (x_in, 0, z1)]
    vs = [bm.verts.new(Vector(p)) for p in pts]
    # (faced out whichever side the wall is on: add() turns them for the mirror)
    flip = x_in > x_wall
    for f in ((0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (0, 2, 5, 3)):
        q = [vs[i] for i in f]
        bm.faces.new(list(reversed(q)) if flip else q)
    add(bm, paint, Matrix.Identity(4))
for part, sx in (("DriftR", 1), ("DriftL", -1)):
    PART = part
    for (z0, z1) in ((-L + 0.05, -L + 2.4), (L - 1.3, L - 0.05)):   # (the front one clear of the hut)
        wedge("snow", sx * (W - 0.12), sx * (W - 0.8), z0, z1, 0.34)

# the operator's hut on the deck, outside the chairs' path: windows all round, a door facing the deck's front, a lamp
# over it (lit at night with the windows: the palette's glow)
HX = W - 0.85
for part, sx in (("HutR", 1), ("HutL", -1)):
    PART = part
    x = sx * HX
    box("shell", (x, 1.2, 3.2), (1.5, 2.4, 2.4))
    box("glass", (x - sx * 0.02, 1.55, 3.2), (1.52, 0.8, 2.0))
    box("glass", (x, 1.55, 3.2), (1.1, 0.8, 2.44))
    box("accent", (x, 0.35, 3.2), (1.54, 0.3, 2.44))
    box("snow", (x, 2.5, 3.2), (1.8, 0.22, 2.7))
    box("dark", (x - sx * 0.1, 0.95, 4.42), (0.8, 1.9, 0.04))                          # the door
    box("dark", (x - sx * 0.1, 2.12, 4.52), (0.3, 0.08, 0.22))                         # the lamp over it
    box("lamp", (x - sx * 0.1, 2.07, 4.54), (0.22, 0.03, 0.16))

# ---------------------------------------------------------------- write it out

os.makedirs(OUT, exist_ok=True)
write_palettes(OUT)

coll = bpy.context.scene.collection
root = bpy.data.objects.new("Lift", None)
coll.objects.link(root)
paint_mat = bpy.data.materials.new("LiftPaint")
objs = {}
print("LIFT parts (game axes, metres):")
total = 0
for part, b in geo.items():
    me = bpy.data.meshes.new(part)
    b.to_mesh(me)
    b.free()
    me.materials.append(paint_mat)
    ob = bpy.data.objects.new(part, me)
    coll.objects.link(ob)
    ob.parent = root
    objs[part] = ob
    print("  %-11s %5d triangles   x %6.2f..%5.2f   y %6.2f..%5.2f   z %6.2f..%5.2f" %
          (part, tris[part], lo[part].x, hi[part].x, lo[part].y, hi[part].y, lo[part].z, hi[part].z))
    total += tris[part]
print("LIFT triangles in the file: %d, parts: %d" % (total, len(objs)))
fbx = os.path.join(OUT, "Lift.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=False, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                         mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO", embed_textures=False)
print("LIFT written", fbx)

if PREVIEWS:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import LiftPreviews
    LiftPreviews.render(objs, OUT, PREVIEWS, TOB)
