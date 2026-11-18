# Builds Summit's hero in Blender: the snowboarder's body, a game rig, weights, and every animation clip.
#
#   blender --background --factory-startup --python Tools/MakeHero.py -- --project .            (exports)
#   blender --background --factory-startup --python Tools/MakeHero.py -- --render DIR [--what turn,rig,poses,anims,...]
#
# Writes (with --project):
#   Assets/Summit/Resources/Rider/Hero.fbx        the skinned rider (mesh + skeleton) and the board, no animation
#   Assets/Summit/Resources/Rider/HeroPalette.png the gear colours (helmet, goggles, gloves, boots, skin)
#   Assets/Summit/Art/Hero/HeroAnims.fbx           the skeleton with every clip as its own take (not shipped:
#                                                  the editor bakes it into Resources/Rider/HeroAnim.bytes)
#
# Axes are Blender's: z up, the rider faces -y (the toe edge of the board), their left is +x. On the board
# the nose is +x (regular stance, left foot forward) and the deck's top is z = 0 (the boot soles sit there).
#
# The body is box-modelled: a low "cage" of rings (each ring a loop of vertices round a limb or the torso)
# that one level of Catmull-Clark subdivision rounds off. Placing the rings is placing the edge loops, so
# every joint gets loops either side of it, and the weights are painted on the cage ring by ring (the
# subdivision blends them smoothly over the loops in between), the way a rigger would by hand.
#
# Proportions: 1.75 m to the top of the helmet, about 6.3 heads tall; a big helmet and goggles, big hands
# and boots, broad puffy shoulders and baggy trousers, so the figure reads at the size it is on a phone.

import bpy, bmesh, math, sys, os, subprocess
from mathutils import Vector, Matrix, Quaternion, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else default
PROJECT = arg("--project")
RENDER = arg("--render")
WHAT = set((arg("--what", "all") or "all").split(","))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30

FWD = Vector((0, -1, 0))     # the way the rider faces
UP = Vector((0, 0, 1))
def V(*a): return Vector(a)
def sm(e0, e1, x):           # smoothstep
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t

# ----------------------------------------------------------------------------------------------- skeleton
# Humanoid names (Unity maps them by itself). Every bone's local z points the way the body faces where it
# can, so a joint bends about its local x: hips, spine, knees and elbows all hinge the same way.

S = 0.165, 0.02, 1.355                 # left shoulder joint
def arm_points(s):
    sh = V(S[0] * s, S[1], S[2])
    u = V(0.766 * s, -0.05, -0.643).normalized()            # A pose: 40 degrees below level, a touch forward
    el = sh + u * 0.275
    f = V(0.712 * s, -0.2035, -0.672).normalized()         # the elbow a little bent, forearm forward
    wr = el + f * 0.245
    kn = wr + f * 0.095
    return sh, u, el, f, wr, kn

def leg_points(s):
    hip = V(0.095 * s, 0.0, 0.885)
    knee = V(0.100 * s, -0.025, 0.485)                     # knees a touch forward, so IK knows which way they bend
    ankle = V(0.105 * s, 0.015, 0.095)
    ball = V(0.110 * s, -0.105, 0.030)
    toe = V(0.110 * s, -0.190, 0.030)
    return hip, knee, ankle, ball, toe

BONES = []   # name, head, tail, parent, z hint
def bone(n, h, t, p, z=FWD, deform=True): BONES.append((n, Vector(h), Vector(t), p, Vector(z), deform))

bone("Root", (0, 0, 0), (0, 0.2, 0), None, UP)
bone("Hips", (0, 0.01, 0.93), (0, 0.01, 1.02), "Root")
bone("Spine", (0, 0.01, 1.02), (0, 0.0, 1.12), "Hips")
bone("Chest", (0, 0.0, 1.12), (0, 0.005, 1.24), "Spine")
bone("UpperChest", (0, 0.005, 1.24), (0, 0.02, 1.37), "Chest")
bone("Neck", (0, 0.02, 1.37), (0, 0.012, 1.47), "UpperChest")
bone("Head", (0, 0.012, 1.47), (0, 0.012, 1.72), "Neck")
bone("Hood", (0, 0.10, 1.47), (0, 0.17, 1.39), "UpperChest", (0, 1, 0.3))
bone("Backpack", (0, 0.21, 1.32), (0, 0.24, 1.06), "UpperChest", (0, 1, 0))   # hangs on the straps: swings a little (in game)
for s, side in ((1, "Left"), (-1, "Right")):
    sh, u, el, f, wr, kn = arm_points(s)
    bone(side + "Shoulder", (0.03 * s, 0.0, 1.335), sh, "UpperChest")
    bone(side + "UpperArm", sh, el, side + "Shoulder")
    bone(side + "LowerArm", el, wr, side + "UpperArm")
    bone(side + "ForearmTwist", el.lerp(wr, 0.5), wr, side + "LowerArm")   # half the wrist's twist (set in game)
    bone(side + "Hand", wr, kn, side + "LowerArm")
    # a mitten: the four fingers move as one (the "index"), and a thumb
    bone(side + "IndexProximal", kn, kn + f * 0.04, side + "Hand")
    bone(side + "IndexIntermediate", kn + f * 0.04, kn + f * 0.07, side + "IndexProximal")
    bone(side + "IndexDistal", kn + f * 0.07, kn + f * 0.095, side + "IndexIntermediate")
    tb = wr + f * 0.025 + FWD * 0.035
    td = (f * 0.45 + FWD * 0.75 + V(0, 0, -0.35)).normalized()
    bone(side + "ThumbProximal", tb, tb + td * 0.035, side + "Hand")
    bone(side + "ThumbIntermediate", tb + td * 0.035, tb + td * 0.06, side + "ThumbProximal")
    bone(side + "ThumbDistal", tb + td * 0.06, tb + td * 0.08, side + "ThumbIntermediate")
    hip, knee, ankle, ball, toe = leg_points(s)
    bone(side + "UpperLeg", hip, knee, "Hips")
    bone(side + "LowerLeg", knee, ankle, side + "UpperLeg")
    bone(side + "Foot", ankle, ball, side + "LowerLeg", UP)
    bone(side + "Toes", ball, toe, side + "Foot", UP)

arm_data = bpy.data.armatures.new("HeroSkeleton")
arm_data.display_type = 'OCTAHEDRAL'
ARM = bpy.data.objects.new("HeroRig", arm_data)
scene.collection.objects.link(ARM)
bpy.context.view_layer.objects.active = ARM
ARM.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for n, h, t, p, z, d in BONES:
    eb = arm_data.edit_bones.new(n)
    eb.head, eb.tail = h, t
    eb.align_roll(z)
    eb.use_deform = d
    if p:
        eb.parent = arm_data.edit_bones[p]
        eb.use_connect = (eb.parent.tail - h).length < 1e-4 and not n.endswith("Twist")
bpy.ops.object.mode_set(mode='OBJECT')
BONE_NAMES = [b[0] for b in BONES]

# ----------------------------------------------------------------------------------------------- cage
# Swatches in the gear palette (HeroPalette.png, 8 x 2): colour, and smoothness in the alpha.
PAL = [  # name, rgb, smoothness
    ("skin", (0.80, 0.58, 0.45), 0.25), ("helmet", (0.90, 0.91, 0.93), 0.55), ("frame", (0.05, 0.05, 0.06), 0.45),
    ("lens", (1.00, 0.52, 0.08), 0.92), ("glove", (0.11, 0.115, 0.13), 0.20), ("cuff", (0.16, 0.17, 0.19), 0.15),
    ("boot", (0.14, 0.12, 0.11), 0.30), ("sole", (0.05, 0.05, 0.05), 0.10), ("strap", (0.09, 0.09, 0.10), 0.25),
    ("binding", (0.07, 0.07, 0.08), 0.35), ("bstrap", (0.22, 0.23, 0.26), 0.30), ("vent", (0.55, 0.57, 0.60), 0.4),
    ("nose", (0.84, 0.55, 0.45), 0.25), ("deckedge", (0.08, 0.08, 0.09), 0.5), ("base", (0.06, 0.06, 0.07), 0.6),
    ("spare", (1, 1, 1), 0.2),
    ("pack", (0.13, 0.14, 0.16), 0.25), ("packside", (0.20, 0.21, 0.24), 0.2), ("packaccent", (0.92, 0.72, 0.16), 0.35),
    ("seam", (0.10, 0.10, 0.11), 0.3), ("zip", (0.70, 0.71, 0.74), 0.7), ("gaiter", (0.18, 0.19, 0.23), 0.12),
    ("gaiterband", (0.92, 0.72, 0.16), 0.15), ("chinstrap", (0.07, 0.07, 0.08), 0.3)]
PALI = {p[0]: i for i, p in enumerate(PAL)}
PAL_ROWS = (len(PAL) + 7) // 8
def swatch_uv(name):
    i = PALI[name]
    return ((i % 8 + 0.5) / 8.0, (i // 8 + 0.5) / PAL_ROWS)

class Cage:
    """A low polygon cage: vertices with weights and a uv, quads and triangles."""
    def __init__(self, name, mat):
        self.name, self.mat = name, mat
        self.v, self.w, self.uv, self.f = [], [], [], []
    def add(self, co, w, uv=(0.5, 0.5)):
        self.v.append(Vector(co)); self.w.append(dict(w)); self.uv.append(uv)
        return len(self.v) - 1
    def face(self, *ix): self.f.append(tuple(ix))
    def bridge(self, a, b, closed=True):
        n = len(a)
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            self.face(a[i], a[j], b[j], b[i])
    def cap(self, ring, co, w, uv=(0.5, 0.5)):
        c = self.add(co, w, uv)
        for i in range(len(ring)):
            self.face(ring[i], ring[(i + 1) % len(ring)], c)
        return c

def frame_of(t, ref=FWD):
    """Two directions across a limb (front, and the other way round it) for a limb running along t."""
    t = t.normalized()
    a1 = (ref - t * ref.dot(t))
    if a1.length < 1e-4: a1 = Vector((0, 0, 1)) - t * t.z
    a1.normalize()
    return a1, t.cross(a1)

def superellipse(th, p):
    c, s = math.cos(th), math.sin(th)
    return (math.copysign(abs(c) ** (2 / p), c), math.copysign(abs(s) ** (2 / p), s))

def ring_pts(c, a1, a2, angles, r1, r2, p=2.0, r1b=None):
    """Points round centre c at the given angles; radius r1 along a1 (r1b on the -a1 side), r2 along a2."""
    out = []
    for th in angles:
        x, y = superellipse(th, p)
        out.append(c + a1 * x * (r1 if x >= 0 or r1b is None else r1b) + a2 * y * r2)
    return out

def angles_round(c, a1, a2, pts):
    return [math.atan2((p - c).dot(a2), (p - c).dot(a1)) for p in pts]

def unwrap(angs):
    out = [angs[0]]
    for a in angs[1:]:
        while a < out[-1]: a += 2 * math.pi
        out.append(a)
    return out

def blend_angles(angs, n, k):
    """The ring's angles eased from the shape of an opening (k = 0) to evenly spaced (k = 1)."""
    even = [angs[0] + 2 * math.pi * i / n for i in range(n)]
    return [lerp(a, e, k) for a, e in zip(angs, even)]

SIDE = {1: "Left", -1: "Right"}
CAGES = []

# ---- the jacket: torso, shoulders, sleeves, collar, one surface
def make_jacket():
    J = Cage("HeroBody", "Jacket")
    N = 12
    ang = [k * 2 * math.pi / N for k in range(N)]       # 0 = front (-y), pi/2 = left (+x)
    # rings up the torso: z (front, back), half width, half depth front, back, centre y, superellipse,
    # weights as (bone, share) pairs
    R = [
        ("hemfold", (0.86, 0.82), 0.192, 0.128, 0.142, 0.01, 2.3, {"Hips": 1}),
        ("hem", (0.835, 0.795), 0.232, 0.162, 0.178, 0.012, 2.4, {"Hips": 1}),
        ("skirt", (0.875, 0.85), 0.226, 0.158, 0.172, 0.012, 2.4, {"Hips": 1}),
        ("hips", (0.94, 0.93), 0.214, 0.150, 0.162, 0.01, 2.5, {"Hips": 1}),
        ("waist", (1.02, 1.02), 0.200, 0.140, 0.146, 0.01, 2.5, {"Hips": 0.45, "Spine": 0.55}),
        ("belly", (1.115, 1.115), 0.208, 0.146, 0.146, 0.005, 2.5, {"Spine": 0.55, "Chest": 0.45}),
        ("a", (1.20, 1.20), 0.236, 0.158, 0.154, 0.005, 2.6, {"Chest": 0.75, "UpperChest": 0.25}),
        ("b", (1.285, 1.285), 0.252, 0.150, 0.150, 0.01, 2.7, {"Chest": 0.3, "UpperChest": 0.7}),
        ("c", (1.37, 1.37), 0.226, 0.132, 0.136, 0.02, 2.5, {"UpperChest": 1}),
        ("d", (1.425, 1.425), 0.112, 0.100, 0.108, 0.025, 2.0, {"UpperChest": 0.65, "Neck": 0.35}),
        ("e", (1.532, 1.515), 0.114, 0.124, 0.106, 0.01, 2.0, {"Neck": 0.6, "Head": 0.1, "UpperChest": 0.3}),
        ("efold", (1.52, 1.50), 0.096, 0.106, 0.09, 0.01, 2.0, {"Neck": 0.6, "Head": 0.1, "UpperChest": 0.3}),
    ]
    rings = {}
    for name, (zf, zb), rx, ryf, ryb, cy, p, w in R:
        idx = []
        for th in ang:
            x, y = superellipse(th, p)
            z = zf + (zb - zf) * (1 - math.cos(th)) / 2
            co = V(rx * math.copysign(abs(math.sin(th)) ** (2 / p), math.sin(th)),
                   cy - (ryf if math.cos(th) >= 0 else ryb) * math.copysign(abs(math.cos(th)) ** (2 / p), math.cos(th)), z)
            wv = dict(w)
            idx.append(J.add(co, wv))
        rings[name] = idx
    order = [r[0] for r in R]
    holes = {1: (2, 3, 4), -1: (10, 9, 8)}   # the arm openings: columns at 60/90/120 degrees each side
    def in_hole(lo, hi, k0, k1):
        for s, (c0, c1, c2) in holes.items():
            if lo in ("a", "b") and hi in ("b", "c") and {k0, k1} <= {c0, c1, c2} and abs(k0 - k1) == 1:
                return True
        return False
    for i in range(len(order) - 1):
        lo, hi = order[i], order[i + 1]
        A, B = rings[lo], rings[hi]
        for k in range(N):
            k1 = (k + 1) % N
            if in_hole(lo, hi, k, k1): continue
            J.face(A[k], A[k1], B[k1], B[k])
    # the shoulder line: the side columns at the top of the arm opening move with the arm a little, and the
    # tops of the shoulders with the collarbone
    for s, cols in holes.items():
        sd = SIDE[s]
        for k in cols:
            J.w[rings["c"][k]] = {"UpperChest": 0.45, sd + "Shoulder": 0.4, sd + "UpperArm": 0.15}
            J.w[rings["b"][k]].update({})
        J.w[rings["a"][cols[0]]] = {"Chest": 0.55, "UpperChest": 0.2, sd + "UpperArm": 0.25}
        J.w[rings["a"][cols[2]]] = {"Chest": 0.55, "UpperChest": 0.2, sd + "UpperArm": 0.25}
        J.w[rings["a"][cols[1]]] = {"Chest": 0.6, "UpperChest": 0.15, sd + "UpperArm": 0.25}
        for k in (cols[0], cols[2]):
            J.w[rings["b"][k]] = {"Chest": 0.2, "UpperChest": 0.45, sd + "UpperArm": 0.35}
        J.w[rings["d"][(3 if s > 0 else 9)]] = {"UpperChest": 0.6, sd + "Shoulder": 0.25, "Neck": 0.15}
        # the opening: a loop of 8, round the arm's axis
        hole = [rings["a"][cols[0]], rings["a"][cols[1]], rings["a"][cols[2]], rings["b"][cols[2]],
                rings["c"][cols[2]], rings["c"][cols[1]], rings["c"][cols[0]], rings["b"][cols[0]]]
        sh, u, el, f, wr, kn = arm_points(s)
        hc = sum((J.v[i] for i in hole), Vector()) / len(hole)
        a1, a2 = frame_of(u)
        # go round the opening the same way as the sleeve's rings do (on the right arm the loop as listed
        # runs the other way; bridged as it is, the sleeve would come out twisted)
        raw = angles_round(hc, a1, a2, [J.v[i] for i in hole])
        turn = sum(math.remainder(raw[(i + 1) % 8] - raw[i], 2 * math.pi) for i in range(8))
        if turn < 0:
            hole.reverse(); raw = angles_round(hc, a1, a2, [J.v[i] for i in hole])
        angs = unwrap(raw)
        # sleeve rings: distance from the shoulder joint, radius, weights
        up_, lo_, tw, hd = sd + "UpperArm", sd + "LowerArm", sd + "ForearmTwist", sd + "Hand"
        sleeve = [
            (0.115, 0.108, 0.15, {up_: 0.7, sd + "Shoulder": 0.3}),
            (0.185, 0.099, 0.6, {up_: 1}),
            (0.240, 0.087, 1, {up_: 0.85, lo_: 0.15}),
            (0.278, 0.082, 1, {up_: 0.5, lo_: 0.5}),
            (0.318, 0.080, 1, {up_: 0.12, lo_: 0.88}),
            (0.385, 0.076, 1, {lo_: 0.75, tw: 0.25}),
            (0.455, 0.076, 1, {lo_: 0.3, tw: 0.7}),
            (0.505, 0.076, 1, {tw: 0.85, hd: 0.15}),
            (0.495, 0.058, 1, {tw: 0.85, hd: 0.15}),   # the cuff turned in
        ]
        prev = hole
        for d, r, ease, w in sleeve:
            # the axis bends at the elbow: before it along the upper arm, after it along the forearm
            if d <= 0.275:
                c = sh + u * d; t = u
            else:
                c = el + f * (d - 0.275); t = (u * max(0, 0.32 - d) + f * (d - 0.275)).normalized()
            if d > 0.24 and d < 0.32: t = u.lerp(f, sm(0.24, 0.32, d)).normalized()
            b1, b2 = frame_of(t)
            ra = blend_angles(angs, 8, ease)
            pts = ring_pts(c, b1, b2, ra, r * 1.02, r * 0.96, 2.2)
            idx = [J.add(pt, w) for pt in pts]
            J.bridge(prev, idx)
            prev = idx
    CAGES.append(J)
    return J

# ---- the hood, rolled up behind the collar
def make_hood():
    H = Cage("HeroHood", "Jacket")
    path = [(V(0, 0.10, 1.50), 0.115, 0.05), (V(0, 0.15, 1.475), 0.15, 0.07), (V(0, 0.178, 1.42), 0.145, 0.066),
            (V(0, 0.17, 1.37), 0.10, 0.04)]
    angs = [k * 2 * math.pi / 8 for k in range(8)]
    rings = []
    for i, (c, rx, ry) in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)][0] - path[max(i - 1, 0)][0]).normalized()
        a1, a2 = frame_of(t, V(0, 1, 0))
        w = {"UpperChest": 0.6, "Hood": 0.4} if i == 0 else {"Hood": 1}
        pts = ring_pts(c, a1, a2, angs, ry, rx, 2.4)
        rings.append([H.add(p, w) for p in pts])
    for a, b in zip(rings, rings[1:]): H.bridge(a, b)
    H.cap(rings[0][::-1], path[0][0] + V(0, -0.02, 0.01), {"UpperChest": 0.6, "Hood": 0.4})
    H.cap(rings[-1], path[-1][0] + V(0, 0.0, -0.02), {"Hood": 1})
    CAGES.append(H)

# ---- the trousers: seat and both legs, one surface (the legs branch from the seat at the crotch)
def make_pants():
    P = Cage("HeroLegs", "Pants")
    N = 12
    ang = [k * 2 * math.pi / N for k in range(N)]
    R = [("top", 1.005, 0.172, 0.120, 0.130, 0.01, {"Hips": 0.6, "Spine": 0.4}),
         ("mid", 0.925, 0.186, 0.128, 0.150, 0.01, {"Hips": 1}),
         ("low", 0.845, 0.190, 0.124, 0.146, 0.01, {"Hips": 1})]
    rings = {}
    for name, z, rx, ryf, ryb, cy, w in R:
        idx = []
        for k, th in enumerate(ang):
            co = V(rx * math.copysign(abs(math.sin(th)) ** (2 / 2.5), math.sin(th)),
                   cy - (ryf if math.cos(th) >= 0 else ryb) * math.copysign(abs(math.cos(th)) ** (2 / 2.5), math.cos(th)), z)
            idx.append(P.add(co, w))
        rings[name] = idx
    P.bridge(rings["top"], rings["mid"]); P.bridge(rings["mid"], rings["low"])
    # the low ring's sides start to go with the legs
    for k in range(N):
        s = math.sin(ang[k])
        if abs(s) > 0.3:
            P.w[rings["low"][k]] = {"Hips": 0.65, SIDE[1 if s > 0 else -1] + "UpperLeg": 0.35}
    crotch = P.add(V(0, 0.012, 0.78), {"Hips": 0.6, "LeftUpperLeg": 0.2, "RightUpperLeg": 0.2})
    for s in (1, -1):
        sd = SIDE[s]
        if s > 0: loop = [rings["low"][k] for k in range(0, 7)] + [crotch]
        else: loop = [rings["low"][(N - k) % N] for k in range(0, 7)] + [crotch]
        hip, knee, ankle, ball, toe = leg_points(s)
        def axis_at(z):
            if z >= knee.z:
                t = (z - hip.z) / (knee.z - hip.z); return hip.lerp(knee, t), (knee - hip)
            t = (z - knee.z) / (ankle.z - knee.z); return knee.lerp(ankle, t), (ankle - knee)
        c0, t0 = axis_at(0.80)
        a1, a2 = frame_of(-t0)
        a2 = a2 * s if s < 0 else a2
        raw = angles_round(c0, a1, a2, [P.v[i] for i in loop])
        if sum(math.remainder(raw[(i + 1) % 8] - raw[i], 2 * math.pi) for i in range(8)) < 0:
            loop.reverse(); raw = angles_round(c0, a1, a2, [P.v[i] for i in loop])
        angs = unwrap(raw)
        ul, ll, ft = sd + "UpperLeg", sd + "LowerLeg", sd + "Foot"
        legs = [  # height, radius side, radius front/back, ease to even, weights, knee bulge
            (0.745, 0.136, 0.142, 0.2, {ul: 0.9, "Hips": 0.1}, 0),
            (0.645, 0.132, 0.137, 0.7, {ul: 1}, 0),
            (0.575, 0.126, 0.131, 1, {ul: 0.95, ll: 0.05}, 0.006),
            (0.535, 0.118, 0.121, 1, {ul: 0.85, ll: 0.15}, 0.0),      # a fold pinched in above the knee
            (0.490, 0.120, 0.128, 1, {ul: 0.5, ll: 0.5}, 0.016),
            (0.440, 0.113, 0.117, 1, {ul: 0.15, ll: 0.85}, 0.0),      # and below it
            (0.390, 0.117, 0.121, 1, {ll: 1}, 0.004),
            (0.300, 0.116, 0.120, 1, {ll: 1}, 0),
            (0.235, 0.121, 0.126, 1, {ll: 1}, 0),
            (0.200, 0.114, 0.118, 1, {ll: 0.95, ft: 0.05}, 0),        # the hem stacked in folds on the boot
            (0.168, 0.124, 0.130, 1, {ll: 0.92, ft: 0.08}, 0),
            (0.140, 0.116, 0.121, 1, {ll: 0.9, ft: 0.1}, 0),
            (0.118, 0.128, 0.134, 1, {ll: 0.88, ft: 0.12}, 0),
            (0.136, 0.104, 0.110, 1, {ll: 0.88, ft: 0.12}, 0),   # turned in at the hem
        ]
        prev = loop
        for z, rs, rf, ease, w, bulge in legs:
            c, t = axis_at(z)
            b1, b2 = frame_of(-t)
            if s < 0: b2 = -b2
            ra = blend_angles(angs, 8, ease)
            pts = []
            for th in ra:
                x, y = math.cos(th), math.sin(th)
                rr = rf + bulge * max(0, x) ** 2
                pts.append(c + b1 * (x * rr) + b2 * (y * rs))
            idx = [P.add(p, w) for p in pts]
            P.bridge(prev, idx)
            prev = idx
    CAGES.append(P)

# ---- gear: rigid pieces (each vertex on one or two bones), in the palette material
def loft(G, path, n, uvname, p=2.0, closed_start=True, closed_end=True, ref=FWD, twist=0.0):
    """A tube through (centre, r1 along the front, r2 across, weights) sections; capped ends."""
    angs = [twist + k * 2 * math.pi / n for k in range(n)]
    rings = []
    for i, sec in enumerate(path):
        c, r1, r2, w = sec[:4]
        r1b = sec[4] if len(sec) > 4 else None
        t = (path[min(i + 1, len(path) - 1)][0] - path[max(i - 1, 0)][0])
        a1, a2 = frame_of(t, ref)
        pts = ring_pts(c, a1, a2, angs, r1, r2, p, r1b)
        rings.append([G.add(q, w, swatch_uv(uvname)) for q in pts])
    for a, b in zip(rings, rings[1:]): G.bridge(a, b)
    if closed_start: G.cap(rings[0][::-1], path[0][0], path[0][3], swatch_uv(uvname))
    if closed_end: G.cap(rings[-1], path[-1][0], path[-1][3], swatch_uv(uvname))
    return rings

def shell(G, rows, axis_pt, thick, w, uvname):
    """A curved plate: rows of points (bottom to top) for its outer face, thickened in toward a vertical axis."""
    uv = swatch_uv(uvname)
    def inward(p):
        d = V(axis_pt.x - p.x, axis_pt.y - p.y, 0)
        return p + d.normalized() * thick if d.length > 1e-6 else p
    O = [[G.add(p, w, uv) for p in r] for r in rows]
    I = [[G.add(inward(G.v[i]), w, uv) for i in r] for r in O]
    m, k = len(O), len(O[0])
    for r in range(m - 1):
        for c in range(k - 1):
            G.face(O[r][c], O[r][c + 1], O[r + 1][c + 1], O[r + 1][c])
            G.face(I[r][c + 1], I[r][c], I[r + 1][c], I[r + 1][c + 1])
        G.face(O[r][0], O[r + 1][0], I[r + 1][0], I[r][0])
        G.face(O[r + 1][k - 1], O[r][k - 1], I[r][k - 1], I[r + 1][k - 1])
    for c in range(k - 1):
        G.face(O[0][c + 1], O[0][c], I[0][c], I[0][c + 1])
        G.face(O[m - 1][c], O[m - 1][c + 1], I[m - 1][c + 1], I[m - 1][c])

def make_gear():
    G = Cage("HeroGear", "Gear")
    hd = {"Head": 1}
    HC = V(0, -0.004, 1.612)      # the middle of the head
    HS = 1.06                     # head and helmet a size up: it is what you read first at phone size
    def HV(x, y, z): return HC + V(x, y, z) * HS
    # head and face (only the nose and cheeks show, between the goggles and the high collar)
    loft(G, [(HV(0, 0.0, -0.135), 0.03, 0.03, hd), (HV(0, -0.005, -0.10), 0.085, 0.07, hd, 0.08),
             (HV(0, -0.005, -0.04), 0.112, 0.092, hd, 0.098), (HV(0, 0, 0.03), 0.112, 0.096, hd, 0.11),
             (HV(0, 0, 0.09), 0.09, 0.085, hd, 0.095), (HV(0, 0, 0.125), 0.03, 0.03, hd)],
         10, "skin", 2.0, ref=FWD, twist=0.0)
    # a neck warmer pulled up to the goggles: no bare skin, and a band of colour under the lens
    gt = {"Head": 1}; gn = {"Head": 0.6, "Neck": 0.4}
    loft(G, [(HV(0, 0.01, -0.175), 0.108, 0.104, {"Neck": 0.6, "Head": 0.2, "UpperChest": 0.2}, 0.09),
             (HV(0, 0.005, -0.13), 0.118, 0.106, gn, 0.092), (HV(0, 0.0, -0.085), 0.124, 0.108, gt, 0.096),
             (HV(0, 0.0, -0.05), 0.124, 0.110, gt, 0.098), (HV(0, 0.002, -0.032), 0.116, 0.106, gt, 0.094)],
         12, "gaiter", 2.2, closed_start=False, closed_end=False)
    loft(G, [(HV(0, 0.0, -0.068), 0.126, 0.112, gt, 0.099), (HV(0, 0.0, -0.058), 0.126, 0.112, gt, 0.099)],
         12, "gaiterband", 2.2, closed_start=False, closed_end=False)
    # the helmet's chin strap, over the neck warmer
    for sx in (1, -1):
        loft(G, [(HV(0.112 * sx, 0.01, -0.035), 0.004, 0.008, gt), (HV(0.115 * sx, -0.02, -0.09), 0.004, 0.008, gt),
                 (HV(0.07 * sx, -0.07, -0.13), 0.004, 0.008, gt)], 4, "chinstrap", 2.0, ref=V(sx, 0, 0))
    # the helmet: a shell over the top and the back, down over the ears
    hel = []
    for i, (dz, r) in enumerate([(0.150, 0.03), (0.135, 0.085), (0.105, 0.118), (0.06, 0.134), (0.0, 0.140),
                                  (-0.035, 0.137)]):
        hel.append((HV(0, 0.012, dz), r * HS))
    n = 14
    rings = []
    for i, (c, r) in enumerate(hel):
        idx = []
        for k in range(n):
            th = 2 * math.pi * k / n
            x, y = math.sin(th), -math.cos(th)
            # deeper at the back and over the ears than at the brow
            zdrop = 0 if i < len(hel) - 1 else -0.045 * (1 - max(0, -y)) - 0.02 * max(0, y)
            rr = r * (1.0 + 0.04 * max(0, y))
            idx.append(G.add(c + V(x * rr * 0.93, y * rr * 1.05, zdrop), hd, swatch_uv("helmet")))
        rings.append(idx)
    for a, b in zip(rings, rings[1:]): G.bridge(a, b)
    G.cap(rings[0][::-1], hel[0][0] + V(0, 0, 0.004), hd, swatch_uv("helmet"))
    # the helmet's rim turned in
    inner = [G.add(G.v[i] * 1.0 + (HC + V(0, 0.012 * HS, G.v[i].z - HC.z) - G.v[i]) * 0.12 + V(0, 0, 0.012), hd, swatch_uv("frame"))
             for i in rings[-1]]
    G.bridge(rings[-1], inner)
    # vents on top
    for k in (-1, 0, 1):
        loft(G, [(HV(0.035 * k, -0.06, 0.128), 0.006, 0.01, hd), (HV(0.035 * k, 0.03, 0.142), 0.006, 0.011, hd)],
             6, "vent", 2.0, ref=V(0, 0, 1))
    # goggles: a band round the front of the helmet at eye height, a mirrored lens in a black frame, the strap
    def arc(r, z, a0, a1, m):
        return [HV(math.sin(a) * r * 0.93, -math.cos(a) * r * 1.05, z) for a in [a0 + (a1 - a0) * i / m for i in range(m + 1)]]
    gz = 0.012
    frame = arc(0.128, gz, -1.25, 1.25, 8)
    secs = []
    for i, c in enumerate(frame):
        k = abs(i - 4) / 4.0
        secs.append((c, 0.013, 0.048 - 0.012 * k ** 2, hd))
    loft(G, secs, 6, "frame", 2.6, ref=None or (HC - frame[4]).normalized(), twist=math.pi / 6)
    lens = arc(0.141, gz, -1.05, 1.05, 8)
    secs = [(c, 0.006, 0.039 - 0.012 * (abs(i - 4) / 4.0) ** 2, hd) for i, c in enumerate(lens)]
    loft(G, secs, 6, "lens", 2.8, ref=(HC - lens[4]).normalized(), twist=math.pi / 6)
    strap = arc(0.143, gz, 1.2, 2 * math.pi - 1.2, 8)
    loft(G, [(c, 0.006, 0.017, hd) for c in strap], 4, "strap", 2.0, ref=V(0, 0, 1), twist=math.pi / 4)
    # gloves: a palm, a mitten for the fingers, a thumb, and a gauntlet cuff over the sleeve
    for s in (1, -1):
        sd = SIDE[s]
        sh, u, el, f, wr, kn = arm_points(s)
        side = f.cross(FWD).normalized() * s   # across the back of the hand
        H = {sd + "Hand": 1}
        I1, I2, I3 = {sd + "IndexProximal": 1}, {sd + "IndexIntermediate": 1}, {sd + "IndexDistal": 1}
        loft(G, [(wr - f * 0.012, 0.046, 0.036, H), (wr + f * 0.03, 0.058, 0.040, H), (kn - f * 0.005, 0.060, 0.038, H),
                 (kn + f * 0.022, 0.058, 0.035, {sd + "Hand": 0.5, sd + "IndexProximal": 0.5}),
                 (kn + f * 0.05, 0.056, 0.033, I1), (kn + f * 0.078, 0.052, 0.031, I2), (kn + f * 0.100, 0.042, 0.027, I3),
                 (kn + f * 0.114, 0.022, 0.017, I3)], 10, "glove", 2.6, ref=FWD)
        tb = wr + f * 0.025 + FWD * 0.035
        td = (f * 0.45 + FWD * 0.75 + V(0, 0, -0.35)).normalized()
        T1, T2, T3 = {sd + "ThumbProximal": 1}, {sd + "ThumbIntermediate": 1}, {sd + "ThumbDistal": 1}
        loft(G, [(tb - td * 0.012, 0.022, 0.022, {sd + "Hand": 0.6, sd + "ThumbProximal": 0.4}), (tb + td * 0.02, 0.022, 0.024, T1),
                 (tb + td * 0.05, 0.019, 0.021, T2), (tb + td * 0.075, 0.015, 0.017, T3), (tb + td * 0.088, 0.006, 0.008, T3)],
             6, "glove", 2.0, ref=f)
        tw = sd + "ForearmTwist"
        cuff = [(wr - f * 0.095, 0.088, 0.086, {tw: 0.9, sd + "Hand": 0.1}), (wr - f * 0.055, 0.092, 0.09, {tw: 0.75, sd + "Hand": 0.25}),
                (wr - f * 0.012, 0.066, 0.06, {tw: 0.3, sd + "Hand": 0.7})]
        rr = loft(G, cuff, 10, "cuff", 2.2, closed_start=False, closed_end=False)
        inner = [G.add(G.v[i] + (wr - f * 0.095 - G.v[i]) * 0.16 + f * 0.012, {tw: 0.9, sd + "Hand": 0.1}, swatch_uv("glove")) for i in rr[0]]
        G.bridge(inner, rr[0])
        G.cap(rr[-1], wr + f * 0.0, {sd + "Hand": 1}, swatch_uv("glove"))
        # boots: a shaft round the ankle (under the trousers), the foot, a thick sole
        hip, knee, ankle, ball, toe = leg_points(s)
        x = ankle.x
        ft, ts = sd + "Foot", sd + "Toes"
        def fw(y):  # foot or toes by how far forward
            k = sm(-0.07, -0.13, y)
            return {ft: 1 - k, ts: k} if k > 0 else {ft: 1}
        loft(G, [(V(x, 0.02, 0.05), 0.075, 0.068, {ft: 1}), (V(x, 0.02, 0.15), 0.07, 0.064, {ft: 0.8, sd + "LowerLeg": 0.2}),
                 (V(x, 0.022, 0.23), 0.066, 0.062, {ft: 0.4, sd + "LowerLeg": 0.6})], 10, "boot", 2.3, ref=FWD)
        foot = []
        for y, w_, h0, h1 in [(0.088, 0.050, 0.026, 0.15), (0.06, 0.068, 0.024, 0.17), (0.0, 0.074, 0.024, 0.16),
                              (-0.07, 0.074, 0.024, 0.125), (-0.14, 0.070, 0.026, 0.095), (-0.195, 0.058, 0.028, 0.082),
                              (-0.228, 0.036, 0.032, 0.07), (-0.240, 0.012, 0.040, 0.058)]:
            zc, hh = (h0 + h1) / 2, (h1 - h0) / 2
            foot.append((V(x + 0.004 * s, y, zc), hh, w_, fw(y)))
        loft(G, foot, 10, "boot", 2.6, ref=V(0, 0, 1))
        sole = [(V(x + 0.004 * s, y, 0.021), 0.022, w_, fw(y)) for y, w_ in
                [(0.098, 0.05), (0.08, 0.074), (0.0, 0.082), (-0.10, 0.08), (-0.19, 0.068), (-0.235, 0.044), (-0.252, 0.016)]]
        loft(G, sole, 8, "sole", 3.0, ref=V(0, 0, 1))
        # a strap of the boot's laces cover
        loft(G, [(V(x, -0.02, 0.172), 0.012, 0.064, fw(-0.02)), (V(x, -0.085, 0.138), 0.012, 0.07, fw(-0.085))], 6, "strap", 2.4, ref=V(0, 0, 1))
    CAGES.append(G)

# ---- the snowboard (not skinned: the game moves it, and throws it in a crash), with its bindings
def make_board():
    B = Cage("Snowboard", "Board")
    L, contact, W, waist, rise = 1.56, 0.62, 0.292, 0.262, 0.065
    top = -0.012; th = 0.014
    xs = [-0.78, -0.75, -0.70, -0.62, -0.45, -0.25, 0.0, 0.25, 0.45, 0.62, 0.70, 0.75, 0.78]
    def width(x):
        ax = abs(x)
        if ax <= contact: return lerp(waist, W, (ax / contact) ** 2)
        k = (ax - contact) / (L / 2 - contact)
        return W * math.sqrt(max(0.0, 1 - k ** 2.2))
    def lift(x):
        ax = abs(x)
        return 0 if ax <= contact else rise * ((ax - contact) / (L / 2 - contact)) ** 1.6
    rows = []
    for x in xs:
        w = max(width(x), 0.004)
        z = top + lift(x)
        tilt = math.copysign(1, x) * (math.atan2(lift(x + 0.01 * math.copysign(1, x)) - lift(x), 0.01) if abs(x) > contact else 0)
        row = []
        for yy, zz, uvn in [(-w / 2, z, "deckedge"), (-w / 2 + 0.01, z + 0.002, "deckedge"), (w / 2 - 0.01, z + 0.002, "deckedge"),
                            (w / 2, z, "deckedge"), (w / 2, z - th, "base"), (-w / 2, z - th, "base")]:
            row.append(B.add(V(x, yy, zz), {"Root": 1}, (0.5, 0.5)))
        rows.append(row)
    for a, b in zip(rows, rows[1:]): B.bridge(a, b)
    B.cap(rows[0][::-1], sum((B.v[i] for i in rows[0]), Vector()) / 6, {"Root": 1})
    B.cap(rows[-1], sum((B.v[i] for i in rows[-1]), Vector()) / 6, {"Root": 1})
    B.top_faces = None
    CAGES.append(B)
    # bindings (in the gear palette): a base plate, a highback behind the heel, two straps over the boot
    BI = Cage("Bindings", "Gear")
    for s, yaw in ((1, 15.0), (-1, -12.0)):
        cx = 0.265 * s
        R = Matrix.Rotation(math.radians(yaw), 3, 'Z')
        def P(x, y, z): return V(cx, 0, 0) + R @ V(x, y, z)
        w = {"Root": 1}
        loft(BI, [(P(0, 0.11, 0.006), 0.006, 0.05, w), (P(0, 0.06, 0.006), 0.006, 0.064, w), (P(0, -0.06, 0.006), 0.006, 0.064, w),
                  (P(0, -0.12, 0.006), 0.006, 0.05, w)], 6, "binding", 3.0, ref=V(0, 0, 1))
        # highback: a curved plate cupping the heel and the back of the boot
        rows = []
        for i, z in enumerate([0.012, 0.07, 0.14, 0.2, 0.225]):
            r = 0.078 - 0.004 * i
            rows.append([P(math.sin(a) * r, 0.03 + 0.012 * i + math.cos(a) * 0.05, z) for a in [-1.75 + 3.5 * k / 6 for k in range(7)]])
        shell(BI, rows, P(0, 0.0, 0), 0.008, w, "binding")
        # straps over the ankle and the toes
        for y0, z0, rr, hh in ((0.0, 0.10, 0.082, 0.085), (-0.12, 0.05, 0.078, 0.06)):
            pts = [P(math.sin(a) * rr, y0, z0 + math.cos(a) * hh) for a in [-1.45 + 2.9 * k / 6 for k in range(7)]]
            loft(BI, [(q, 0.018, 0.007, w) for q in pts], 4, "bstrap", 2.0, ref=R @ V(0, 1, 0), twist=math.pi / 4)
    CAGES.append(BI)

make_jacket(); make_hood(); make_pants(); make_gear(); make_board()

# ----------------------------------------------------------------------------------------------- meshes
MATS = {}
def material(name, rgb, smooth=0.3, tex=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 1 - smooth
    if tex is not None:
        tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = tex; tn.interpolation = 'Closest'
        m.node_tree.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    MATS[name] = m
    return m

def write_png(path, w, h, rgba_rows):
    """A tiny RGBA PNG, written by hand so the alpha (smoothness) is stored exactly as given."""
    import zlib, struct
    raw = b"".join(b"\x00" + bytes(int(max(0, min(1, c)) * 255 + 0.5) for px_ in row for c in px_) for row in rgba_rows)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "wb").write(png)

def srgb(c): return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
PAL_PATH = os.path.join(PROJECT, "Assets/Summit/Resources/Rider/HeroPalette.png") if PROJECT else os.path.join(bpy.app.tempdir or "/tmp", "HeroPalette.png")
rows_ = []
for row in reversed(range(PAL_ROWS)):   # PNG rows run top down; uv v = 0 is the bottom row
    rows_.append([(srgb(PAL[row * 8 + col][1][0]), srgb(PAL[row * 8 + col][1][1]), srgb(PAL[row * 8 + col][1][2]), PAL[row * 8 + col][2])
                  if row * 8 + col < len(PAL) else (1, 1, 1, 0.2) for col in range(8)])
write_png(PAL_PATH, 8, PAL_ROWS, rows_)
pal_img = bpy.data.images.load(PAL_PATH)
pal_img.alpha_mode = 'NONE'
pal_img.colorspace_settings.name = 'sRGB'
material("Jacket", (0.93, 0.31, 0.14), 0.35)
material("Pants", (0.17, 0.19, 0.24), 0.2)
material("Gear", (1, 1, 1), 0.35, pal_img)
material("Board", (0.12, 0.45, 0.95), 0.5)

SUBDIV = {"HeroBody": 1, "HeroHood": 1, "HeroLegs": 1, "HeroGear": 1, "Snowboard": 0, "Bindings": 0}
OBJS = {}
tri_total = 0
for C in CAGES:
    me = bpy.data.meshes.new(C.name)
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in C.v]
    bm.verts.ensure_lookup_table()
    uvl = bm.loops.layers.uv.new("UVMap")
    for fc in C.f:
        if len(set(fc)) < 3: continue
        try:
            face = bm.faces.new([vs[i] for i in fc])
        except ValueError:
            continue
        for lp, i in zip(face.loops, fc): lp[uvl].uv = C.uv[i]
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = C.name not in ("Bindings",)
    me.materials.append(MATS[C.mat])
    ob = bpy.data.objects.new(C.name, me)
    scene.collection.objects.link(ob)
    # weights on the cage
    names = set()
    for w in C.w: names |= set(w)
    vg = {n: ob.vertex_groups.new(name=n) for n in sorted(names)}
    # remove_doubles may have merged vertices: map by position
    for i, v in enumerate(me.vertices):
        pass
    pos_w = {}
    for co, w in zip(C.v, C.w): pos_w[tuple(round(c, 5) for c in co)] = w
    for v in me.vertices:
        w = pos_w.get(tuple(round(c, 5) for c in v.co))
        if w is None: continue
        tot = sum(w.values())
        for n, x in w.items(): vg[n].add([v.index], x / tot, 'REPLACE')
    if SUBDIV.get(C.name, 0):
        bpy.context.view_layer.objects.active = ob
        md = ob.modifiers.new("Subdivision", 'SUBSURF'); md.levels = SUBDIV[C.name]; md.render_levels = SUBDIV[C.name]
        md.uv_smooth = 'NONE'
        bpy.ops.object.modifier_apply(modifier=md.name)
    OBJS[C.name] = ob
    nt = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if C.name not in ("Snowboard", "Bindings"): tri_total += nt
    print("HERO mesh %-10s %5d tris, %d groups" % (C.name, nt, len(ob.vertex_groups)))
print("HERO rider triangles:", tri_total)

# ----------------------------------------------------------------------------------------------- surface details
# Pockets, the zip, seams, the backpack and its straps are built on the finished (subdivided) clothes: each point
# is found by casting onto the cloth, and each vertex takes the weights of the cloth right under it, so a pocket
# moves exactly as the jacket beneath it does.
from mathutils.bvhtree import BVHTree

class Surface:
    def __init__(self, ob):
        self.ob = ob
        self.bm = bmesh.new(); self.bm.from_mesh(ob.data)
        self.bm.verts.ensure_lookup_table(); self.bm.faces.ensure_lookup_table()
        self.tree = BVHTree.FromBMesh(self.bm)
        names = {g.index: g.name for g in ob.vertex_groups}
        self.w = [{names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in ob.data.vertices]
    def cast(self, origin, d, lift=0.0):
        """Where a ray from outside meets the cloth (and its normal), lifted off it."""
        hit, n, i, dist = self.tree.ray_cast(Vector(origin), Vector(d).normalized())
        if hit is None: return None, None
        if n.dot(Vector(d)) > 0: n = -n
        return hit + n * lift, n
    def weights(self, p):
        loc, n, i, dist = self.tree.find_nearest(Vector(p))
        f = self.bm.faces[i]
        out, tot = {}, 0.0
        for v in f.verts:
            k = 1.0 / max(1e-4, (v.co - loc).length)
            tot += k
            for g, x in self.w[v.index].items(): out[g] = out.get(g, 0.0) + x * k
        return {g: x / tot for g, x in out.items()}

def stuck(C, surf, blend=None):
    """Give every vertex of a detail cage the weights of the cloth under it (optionally blended with others)."""
    for i, co in enumerate(C.v):
        w = surf.weights(co)
        if blend:
            b, k = blend(co)
            if k > 0:
                w = {g: x * (1 - k) for g, x in w.items()}
                for g, x in b.items(): w[g] = w.get(g, 0) + x * k
        C.w[i] = w

def raised(C, grid, lift_mid, lift_edge, uv="spare"):
    """A raised patch (a pocket, a flap) from a grid of points on the cloth with their normals: a lid lifted off
    the surface, walls down to it, and the base tucked a touch under so no edge shows."""
    m, n = len(grid), len(grid[0])
    top, bot = [], []
    for i in range(m):
        tr, br = [], []
        for j in range(n):
            pt, nm = grid[i][j]
            edge = i in (0, m - 1) or j in (0, n - 1)
            tr.append(C.add(pt + nm * (lift_edge if edge else lift_mid), {}, swatch_uv(uv)))
            br.append(C.add(pt - nm * 0.004, {}, swatch_uv(uv)))
        top.append(tr); bot.append(br)
    for i in range(m - 1):
        for j in range(n - 1):
            C.face(top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j])
    ring_t = [top[0][j] for j in range(n)] + [top[i][n - 1] for i in range(1, m)] + [top[m - 1][j] for j in range(n - 2, -1, -1)] + [top[i][0] for i in range(m - 2, 0, -1)]
    ring_b = [bot[0][j] for j in range(n)] + [bot[i][n - 1] for i in range(1, m)] + [bot[m - 1][j] for j in range(n - 2, -1, -1)] + [bot[i][0] for i in range(m - 2, 0, -1)]
    C.bridge(ring_b, ring_t)

def strip(C, pts, width, thick, uv, nrm=None):
    """A flat band laid along points on the cloth (a seam, a zip, a strap)."""
    secs = []
    for i, (pt, nm) in enumerate(pts):
        secs.append((pt + nm * thick * 0.5, thick * 0.5, width * 0.5, {}))
    ref = nrm or pts[len(pts) // 2][1]
    return loft(C, secs, 4, uv, 2.0, ref=ref, twist=math.pi / 4)

def grid_on(surf, origin_fn, d_fn, us, vs, lift=0.0):
    g = []
    for u in us:
        row = []
        for v in vs:
            pt, nm = surf.cast(origin_fn(u, v), d_fn(u, v), lift)
            if pt is None: return None
            row.append((pt, nm))
        g.append(row)
    return g

def lin(a, b, n): return [a + (b - a) * i / (n - 1) for i in range(n)]

JS, LS = Surface(OBJS["HeroBody"]), Surface(OBJS["HeroLegs"])
JD = Cage("JacketDetail", "Jacket"); LD = Cage("LegDetail", "Pants"); GD = Cage("GearDetail", "Gear")
fwd_ray = lambda u, v: V(0, 1, 0)
# chest pocket (left chest), with a zip along its top
g = grid_on(JS, lambda u, v: V(u, -0.6, v), fwd_ray, lin(0.045, 0.155, 4), lin(1.245, 1.13, 4))
if g:
    raised(JD, g, 0.014, 0.006)
    strip(GD, [JS.cast(V(x, -0.6, 1.248), V(0, 1, 0), 0.017) for x in lin(0.05, 0.15, 4)], 0.008, 0.004, "zip")
# hand-warmer pockets low on both sides of the front, zips slanting
for sx in (1, -1):
    g = grid_on(JS, lambda u, v: V(u * sx, -0.6, v + (u - 0.09) * 0.4), fwd_ray, lin(0.06, 0.17, 4), lin(1.03, 0.92, 4))
    if g:
        raised(JD, g, 0.012, 0.005)
        strip(GD, [JS.cast(V(x * sx, -0.6, 1.035 + (x - 0.09) * 0.4), V(0, 1, 0), 0.015) for x in lin(0.07, 0.165, 4)], 0.007, 0.004, "zip")
# the front zip, collar to hem, with a storm flap beside it
zp = [JS.cast(V(0.0, -0.6, z), V(0, 1, 0), 0.006) for z in lin(1.52, 0.84, 12)]
if all(q[0] is not None for q in zp):
    strip(GD, zp, 0.012, 0.006, "zip")
    strip(GD, [JS.cast(V(-0.016, -0.6, z), V(0, 1, 0), 0.004) for z in lin(1.50, 0.85, 10)], 0.008, 0.005, "seam")
# seams: a yoke across the chest and the back, and down each side
for yy in (-0.6, 0.6):
    pts = [JS.cast(V(x, yy, 1.30 - 0.05 * abs(x) / 0.2), V(0, -yy, 0), 0.003) for x in lin(-0.2, 0.2, 9)]
    if all(q[0] is not None for q in pts): strip(GD, pts, 0.006, 0.004, "seam")
for sx in (1, -1):
    pts = [JS.cast(V(sx * 0.30, 0.01, z), V(-sx, 0, 0), 0.003) for z in lin(1.12, 0.86, 6)]   # from inside the sleeves' reach
    if all(q[0] is not None for q in pts): strip(GD, pts, 0.006, 0.004, "seam")
# a pocket on the left upper arm
sh, u_, el, f_, wr, kn = arm_points(1)
up_side = (V(0, 0, 1) - u_ * u_.z).normalized()
outv = (up_side + V(1, 0, 0) * 0.4).normalized()
acr = u_.cross(outv).normalized()
g = grid_on(JS, lambda a, b: sh + u_ * a + acr * b + outv * 0.5, lambda a, b: -outv, lin(0.12, 0.22, 4), lin(-0.04, 0.04, 3))
if g: raised(JD, g, 0.012, 0.005)
# trousers: cargo pockets on the outside of each thigh, with flaps
for sx in (1, -1):
    side_ray = lambda u, v: V(-sx, 0, 0)
    g = grid_on(LS, lambda u, v: V(sx * 0.6, u, v), side_ray, lin(-0.07, 0.06, 4), lin(0.66, 0.50, 5))
    if g: raised(LD, g, 0.020, 0.008)
    g = grid_on(LS, lambda u, v: V(sx * 0.6, u, v), side_ray, lin(-0.075, 0.065, 4), lin(0.685, 0.635, 3))
    if g: raised(LD, g, 0.030, 0.016)
    # a seam down the outside of the leg
    pts = [LS.cast(V(sx * 0.6, 0.012, z), V(-sx, 0, 0), 0.003) for z in lin(0.95, 0.70, 4) + lin(0.48, 0.19, 5)]
    if all(q[0] is not None for q in pts[:4]): strip(GD, pts[:4], 0.005, 0.004, "seam")
# the backpack: a padded body on the upper back, side panels, a top handle, a yellow loop, and straps over the
# shoulders to the chest with a sternum strap; the body hangs on its own bone and the straps from the jacket
back_hit, _ = JS.cast(V(0, 0.6, 1.18), V(0, -1, 0))
by = back_hit.y if back_hit else 0.17
PK = {"Backpack": 1.0}
body = []
for z, hw, d in [(1.035, 0.115, 0.07), (1.06, 0.135, 0.105), (1.16, 0.142, 0.12), (1.26, 0.138, 0.115), (1.315, 0.12, 0.095), (1.335, 0.085, 0.06)]:
    body.append((V(0, by + 0.012 + d * 0.5, z), d * 0.5, hw, PK))
loft(GD, body, 12, "pack", 3.0, ref=V(0, 1, 0))
for sx in (1, -1):   # side panels
    loft(GD, [(V(sx * 0.143, by + 0.07, z), 0.05, 0.006, PK) for z in (1.08, 1.20, 1.29)], 4, "packside", 2.0, ref=V(0, 1, 0))
loft(GD, [(V(-0.045 + 0.09 * k / 4, by + 0.05, 1.34 + 0.025 * math.sin(math.pi * k / 4)), 0.008, 0.008, PK) for k in range(5)],
     4, "packaccent", 2.0, ref=V(0, 1, 0))
loft(GD, [(V(0, by + 0.135, z), 0.006, 0.05, PK) for z in (1.10, 1.25)], 4, "packaccent", 2.0, ref=V(0, 1, 0))   # a compression strap
for sx in (1, -1):
    pts = []
    pts += [(V(sx * 0.085, by + 0.02, 1.325), V(0, 1, 0))]
    for yy in lin(0.10, -0.06, 4):   # over the top of the shoulder
        q = JS.cast(V(sx * 0.125, yy, 1.8), V(0, 0, -1), 0.012)
        if q[0] is not None: pts.append(q)
    for z in lin(1.30, 1.06, 5):     # down the chest
        q = JS.cast(V(sx * 0.115, -0.6, z), V(0, 1, 0), 0.010)
        if q[0] is not None: pts.append(q)
    if len(pts) > 4: strip(GD, pts, 0.042, 0.010, "pack")
sternum = [JS.cast(V(x, -0.6, 1.17), V(0, 1, 0), 0.02) for x in lin(-0.115, 0.115, 5)]
if all(q[0] is not None for q in sternum): strip(GD, sternum, 0.016, 0.006, "packaccent")

# weights: from the cloth underneath; the pack's own body stays on its bone
stuck(JD, JS); stuck(LD, LS)
def pack_or_cloth(co):
    if co.y > by + 0.015 and co.z > 1.02 and abs(co.x) < 0.16: return PK, 1.0
    return {}, 0.0
pts_gear = list(range(len(GD.v)))
for i, co in enumerate(GD.v):
    b, k = pack_or_cloth(co)
    if k > 0: GD.w[i] = dict(b)
    else:
        GD.w[i] = (LS if co.z < 0.86 and abs(co.x) > 0.12 and co.y > -0.3 else JS).weights(co)
for C, target in ((JD, "HeroBody"), (LD, "HeroLegs"), (GD, "HeroGear")):
    me = bpy.data.meshes.new(C.name)
    bm = bmesh.new(); vs = [bm.verts.new(v) for v in C.v]
    uvl = bm.loops.layers.uv.new("UVMap")
    for fc in C.f:
        if len(set(fc)) < 3: continue
        try: face = bm.faces.new([vs[i] for i in fc])
        except ValueError: continue
        for lp, i in zip(face.loops, fc): lp[uvl].uv = C.uv[i]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    for pg in me.polygons: pg.use_smooth = True
    me.materials.append(MATS[C.mat])
    ob = bpy.data.objects.new(C.name, me); scene.collection.objects.link(ob)
    groups = {}
    for i, w in enumerate(C.w):
        tot = sum(w.values()) or 1.0
        for g_, x in w.items():
            if g_ not in groups: groups[g_] = ob.vertex_groups.new(name=g_)
            groups[g_].add([i], x / tot, 'REPLACE')
    nt = sum(len(pg.vertices) - 2 for pg in me.polygons)
    tri_total += nt
    print("HERO detail %-12s %5d tris -> %s" % (C.name, nt, target))
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); OBJS[target].select_set(True)
    bpy.context.view_layer.objects.active = OBJS[target]
    bpy.ops.object.join()
JS.bm.free(); LS.bm.free()
print("HERO rider triangles with details:", tri_total)

# limit to 4 influences and normalise; the skinned meshes go on the skeleton
for name in ("HeroBody", "HeroHood", "HeroLegs", "HeroGear"):
    ob = OBJS[name]
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    ob.parent = ARM
    md = ob.modifiers.new("Armature", 'ARMATURE'); md.object = ARM
# the hood and the jacket are one renderer in the game
bpy.ops.object.select_all(action='DESELECT')
OBJS["HeroHood"].select_set(True); OBJS["HeroBody"].select_set(True)
bpy.context.view_layer.objects.active = OBJS["HeroBody"]
bpy.ops.object.join()
del OBJS["HeroHood"]
OBJS["Bindings"].select_set(False)
bpy.ops.object.select_all(action='DESELECT')
OBJS["Bindings"].select_set(True); OBJS["Snowboard"].select_set(True)
bpy.context.view_layer.objects.active = OBJS["Snowboard"]
bpy.ops.object.join()
del OBJS["Bindings"]
for v in OBJS["Snowboard"].vertex_groups[:]: OBJS["Snowboard"].vertex_groups.remove(v)
print("HERO done building")

# ----------------------------------------------------------------------------------------------- posing
# A pose is a handful of numbers a person would use to describe it: where the hips are and how they're
# turned, how the back bends, where the feet stand and which way the knees point, where the hands go,
# where the head looks. solve() turns that into every bone's rotation (legs and arms by two-bone IK, a
# pure hinge at the knee and the elbow), so each clip is a function from time to such a description.
# Angles are degrees about the rest pose's own axes: x tips the top of a bone forward, y leans it toward
# the rider's left (+x), z turns it to the left (toward the nose on the board).

REST = {b.name: b.matrix_local.copy() for b in arm_data.bones}
PARENT = {b.name: (b.parent.name if b.parent else None) for b in arm_data.bones}
LEN = {b.name: b.length for b in arm_data.bones}
REL = {n: (REST[PARENT[n]].inverted() @ REST[n] if PARENT[n] else REST[n].copy()) for n in BONE_NAMES}

def Q(axis, deg): return Quaternion(Vector(axis).normalized(), math.radians(deg))

class Solver:
    def __init__(self):
        self.basis = {n: Matrix.Identity(4) for n in BONE_NAMES}
        self.M = {}
        for n in BONE_NAMES: self.ev(n)
    def ev(self, n):
        p = PARENT[n]
        self.M[n] = (self.M[p] if p else Matrix.Identity(4)) @ REL[n] @ self.basis[n]
    def ev_from(self, n):
        """Re-evaluate n and everything below it."""
        i = BONE_NAMES.index(n)
        sub = {n}
        for m in BONE_NAMES[i:]:
            if m == n or PARENT[m] in sub:
                sub.add(m); self.ev(m)
    def M0(self, n):
        p = PARENT[n]
        return (self.M[p] if p else Matrix.Identity(4)) @ REL[n]
    def head(self, n): return self.M0(n).translation.copy()
    def rot(self, n, x=0.0, y=0.0, z=0.0):
        """Turn a bone about its rest axes (in its parent's posed frame)."""
        Rr = REST[n].to_quaternion()
        q = Q((0, 0, 1), z) @ Q((1, 0, 0), x) @ Q((0, 1, 0), y)
        # after any move: the bone turns about its own (moved) head
        self.basis[n] = self.basis[n] @ (Rr.inverted() @ q @ Rr).to_matrix().to_4x4()
        self.ev_from(n)
    def rot_axis(self, n, axis, deg):
        Rr = REST[n].to_quaternion()
        self.basis[n] = self.basis[n] @ (Rr.inverted() @ Q(axis, deg) @ Rr).to_matrix().to_4x4()
        self.ev_from(n)
    def move(self, n, d):
        """Move a bone by d (armature space; its parents unturned)."""
        Rr = REST[n].to_3x3()
        self.basis[n] = Matrix.Translation(Rr.inverted() @ Vector(d)) @ self.basis[n]
        self.ev_from(n)
    def orient(self, n, R):
        """Set a bone's armature-space orientation outright (its head stays where its parent puts it)."""
        M = Matrix.Translation(self.head(n)) @ R.to_4x4()
        self.basis[n] = self.M0(n).inverted() @ M
        self.ev_from(n)
    def frame(self, n, ydir, xaxis):
        y = ydir.normalized(); x = (xaxis - y * xaxis.dot(y)).normalized(); z = x.cross(y)
        R = Matrix((x, y, z)).transposed()
        self.orient(n, R)
    def pos(self, n): return self.M[n].translation.copy()
    def tail(self, n): return self.M[n] @ Vector((0, LEN[n], 0))

    def two_bone(self, upper, lower, target, pole, hinge_sign):
        """Two-bone IK: the joint between bends in the plane of the pole, a pure hinge about the bones' x."""
        h = self.head(upper)
        a, b = LEN[upper], LEN[lower]
        d = target - h
        c = max(abs(a - b) + 1e-3, min(a + b - 1e-4, d.length))
        dn = d.normalized()
        p = (pole - dn * pole.dot(dn))
        if p.length < 1e-5: p = Vector((0, -1, 0)) - dn * (-dn.y)
        p.normalize()
        cosA = max(-1, min(1, (a * a + c * c - b * b) / (2 * a * c)))
        knee = h + dn * (cosA * a) + p * (math.sqrt(1 - cosA * cosA) * a)
        hinge = dn.cross(p).normalized() * hinge_sign
        self.frame(upper, knee - h, hinge)
        self.frame(lower, (h + dn * c) - knee, hinge)
        return knee

# which way the hinge axis turns relative to (limb line x pole) at rest, per chain
def _hinge_sign(upper, lower, end_point):
    s = Solver()
    h = s.head(upper); k = s.head(lower); e = end_point
    dn = (e - h).normalized(); p = (k - h) - dn * (k - h).dot(dn)
    rest_x = REST[upper].to_3x3().col[0]
    return 1 if dn.cross(p.normalized()).dot(rest_x) > 0 else -1
HINGE = {}
for sd in ("Left", "Right"):
    s0 = Solver()
    HINGE[sd + "Leg"] = _hinge_sign(sd + "UpperLeg", sd + "LowerLeg", s0.head(sd + "Foot"))
    HINGE[sd + "Arm"] = _hinge_sign(sd + "UpperArm", sd + "LowerArm", s0.head(sd + "Hand"))

# the stance on the board: bindings at +-0.265 m, front foot turned 15 degrees toward the nose, the rear one
# 12 toward the tail; the ankle sits 5 cm behind the binding's middle (the boot is centred on the board)
ANKLE_Z = 0.095
BIND = {"Left": (0.265, 15.0), "Right": (-0.265, -12.0)}
def ankle_on_board(sd):
    x, yaw = BIND[sd]
    return V(x, 0, 0) + Matrix.Rotation(math.radians(yaw), 3, 'Z') @ V(0, 0.05, ANKLE_Z)
ANK = {sd: ankle_on_board(sd) for sd in ("Left", "Right")}

def deep(p): return {k: (v.copy() if isinstance(v, Vector) else v) for k, v in p.items()}

def mixv(a, b, k):
    if isinstance(a, Vector): return a.lerp(b, k)
    if isinstance(a, (int, float)): return a + (b - a) * k
    if isinstance(a, tuple):
        if len(a) and isinstance(a[0], str):
            if a[0] == b[0] and a[0] != "blend": return (a[0], mixv(a[1], b[1], k))
            return ("blend", a, b, k)   # different kinds of target: solve() places both and goes between
        return tuple(mixv(x, y, k) for x, y in zip(a, b))
    return b if k > 0.5 else a

def mix(A, B, k):
    out = {}
    for key in set(A) | set(B):
        if key in A and key in B: out[key] = mixv(A[key], B[key], k)
        else: out[key] = A.get(key, B.get(key))
    return out

def ease(t): return t * t * (3 - 2 * t)

SHOULDER_JOINT = {}
def solve(P, s=None):
    """Pose the skeleton from a description (see above). Returns the solver."""
    s = s or Solver()
    s.move("Hips", P.get("hips", V(0, 0, 0)))
    s.rot("Hips", *P.get("hrot", (0, 0, 0)))
    sx, sy, sz = P.get("spine", (0, 0, 0))
    for n, k in (("Spine", 0.3), ("Chest", 0.35), ("UpperChest", 0.35)):
        s.rot(n, sx * k, sy * k, sz * k)
    # legs: two-bone IK to the ankle, then the foot laid at its own angles
    for sd in ("Left", "Right"):
        f = P.get("f" + sd[0])
        if f is None: continue
        at, yaw, pitch, roll = f
        pole = P.get("k" + sd[0], V(0, -1, 0))
        s.two_bone(sd + "UpperLeg", sd + "LowerLeg", at, pole, HINGE[sd + "Leg"])
        R = (Q((0, 0, 1), yaw) @ Q((1, 0, 0), -pitch) @ Q((0, 1, 0), roll)).to_matrix() @ REST[sd + "Foot"].to_3x3()
        s.orient(sd + "Foot", R)
        s.rot(sd + "Toes", x=-P.get("toe" + sd[0], 0.0))
    # neck and head
    s.rot("Neck", *P.get("neck", (0, 0, 0)))
    s.rot("Head", *P.get("head", (0, 0, 0)))
    # arms: the collarbone lifts with a raised arm, then IK to the hand, then the wrist and the grip
    for sd in ("Left", "Right"):
        hnd = P.get("h" + sd[0])
        if hnd is None: continue
        def where(h_, sh_):
            if h_[0] == "blend": return where(h_[1], sh_).lerp(where(h_[2], sh_), h_[3])
            # a point on the board is where the fingers close: the wrist is a hand's depth above it
            return sh_ + h_[1] if h_[0] == "rel" else Vector(h_[1]) + V(0, 0, 0.065)
        sh = s.head(sd + "UpperArm")
        target = where(hnd, sh)
        lift = max(0.0, (target - sh).normalized().z + 0.2)
        sign = 1 if sd == "Left" else -1
        # reaching down (a grab): the shoulder drops and comes forward into the reach
        dn = (target - sh).normalized()
        depress = max(0.0, -dn.z - 0.3)
        s.rot(sd + "Shoulder", y=-sign * 18 * lift + sign * 26 * depress, z=sign * (P.get("shrug" + sd[0], 0.0) - 14 * depress))
        sh = s.head(sd + "UpperArm")
        target = where(hnd, sh)
        pole = P.get("e" + sd[0], V(0.3 * sign, 0.8, -0.5))
        s.two_bone(sd + "UpperArm", sd + "LowerArm", target, pole, HINGE[sd + "Arm"])
        wx, wy, wz = P.get("w" + sd[0], (0, 0, 0))
        s.rot(sd + "Hand", wx, wy * sign, wz * sign)
        g = P.get("g" + sd[0], 0.3)
        palm = Vector((-sign, 0, 0))
        for n, k in (("IndexProximal", 35), ("IndexIntermediate", 40), ("IndexDistal", 30)):
            b = sd + n
            dirr = REST[b].to_3x3().col[1]
            s.rot_axis(b, dirr.cross(palm), k * g)
        for n, k in (("ThumbProximal", 10), ("ThumbIntermediate", 25), ("ThumbDistal", 25)):
            b = sd + n
            dirr = REST[b].to_3x3().col[1]
            s.rot_axis(b, dirr.cross(V(0, 0.3, 0) + palm * 0.7), k * g)
    return s

def apply(s):
    """Write the solver's pose onto the armature."""
    for n in BONE_NAMES:
        pb = ARM.pose.bones[n]
        pb.rotation_mode = 'QUATERNION'
        m = s.basis[n]
        pb.rotation_quaternion = m.to_quaternion()
        pb.location = m.translation if n in ("Hips",) else Vector((0, 0, 0))
        pb.scale = (1, 1, 1)

# ----------------------------------------------------------------------------------------------- the clips
def side_pole(x, y, z): return V(x, y, z).normalized()

# an athletic stance: knees and hips well bent, the chest up, both hands out in front for balance, the head
# turned down the hill over the front shoulder
STANCE = {
    "hips": V(0.01, 0.03, -0.22), "hrot": (22, 0, 16), "spine": (-6, 0, 12),
    "neck": (6, 0, 16), "head": (10, 0, 48),
    "fL": (ANK["Left"], 15, 0, 0), "fR": (ANK["Right"], -12, 0, 0),
    "kL": side_pole(0.45, -1, 0), "kR": side_pole(0.8, -1, 0),
    "hL": ("rel", V(0.30, -0.30, -0.17)), "eL": V(0.2, 0.5, -0.8), "wL": (0, 0, -10), "gL": 0.45,
    "hR": ("rel", V(-0.02, -0.32, -0.22)), "eR": V(-0.4, 0.5, -0.7), "wR": (0, 0, 0), "gR": 0.4,
}

def P_(**kw):
    p = deep(STANCE)
    for k, v in kw.items(): p[k] = v
    return p

def breathe(p, t, amt=1.0):
    p = deep(p)
    b = math.sin(t * 2 * math.pi)
    x, y, z = p["spine"]; p["spine"] = (x + 1.5 * b * amt, y, z)
    p["hips"] = p["hips"] + V(0, 0, 0.004 * b * amt)
    return p

CLIPS = []   # name, frames, loop, function of t in [0, 1]
def clip(name, frames, loop=False):
    def deco(fn): CLIPS.append((name, frames, loop, fn)); return fn
    return deco

# -- on the board ---------------------------------------------------------------------------------------
IDLE = P_(hips=V(0.0, 0.02, -0.07), hrot=(4, 0, 14), spine=(5, 0, 8), head=(-4, 0, 40),
          hL=("rel", V(0.12, -0.10, -0.44)), eL=V(0.2, 0.6, -0.5), hR=("rel", V(-0.08, -0.06, -0.45)), gL=0.35, gR=0.35)

@clip("Ride_Idle", 120, True)
def _(t):
    p = breathe(IDLE, t * 2)
    look = math.sin(t * 2 * math.pi) * 28 + math.sin(t * 4 * math.pi + 1) * 6
    x, y, z = p["head"]; p["head"] = (x + 4 * math.sin(t * 2 * math.pi + 2), y, z + look)
    p["hips"] = p["hips"] + V(0.012 * math.sin(t * 2 * math.pi + 0.5), 0, 0)
    return p

@clip("Ride", 60, True)
def _(t):
    p = breathe(STANCE, t, 0.6)
    sw = math.sin(t * 2 * math.pi)
    p["hL"] = ("rel", p["hL"][1] + V(0.015 * sw, 0, 0.015 * math.cos(t * 2 * math.pi)))
    p["hips"] = p["hips"] + V(0, 0, 0.006 * math.sin(t * 4 * math.pi))
    return p

# toe side: knees driven in and down toward the toe edge, hips forward over them, the chest opened up to the
# hill; the back hand trails low toward the snow
TOE = P_(hips=V(0.02, -0.11, -0.31), hrot=(26, 0, 24), spine=(6, 0, 22), head=(8, 0, 38), neck=(4, 0, 14),
         kL=side_pole(-0.15, -1, -0.15), kR=side_pole(0.5, -1, -0.15),
         hL=("rel", V(0.34, -0.34, -0.24)), eL=V(0.0, 0.6, -0.8),
         hR=("rel", V(-0.04, -0.36, -0.46)), eR=V(-0.3, 0.6, -0.5), gR=0.2)
# heel side: sat back and low over the heel edge, chest up, both arms reaching forward over the toes
HEEL = P_(hips=V(0.0, 0.18, -0.35), hrot=(28, 0, 10), spine=(4, 0, 18), head=(10, 0, 44), neck=(6, 0, 16),
          kL=side_pole(0.6, -1, 0.25), kR=side_pole(0.85, -1, 0.25),
          hL=("rel", V(0.30, -0.44, -0.08)), eL=V(0.2, 0.5, -0.8),
          hR=("rel", V(0.08, -0.44, -0.12)), eR=V(-0.4, 0.4, -0.7), gR=0.4)

@clip("Carve_Toe", 30, True)
def _(t): return breathe(TOE, t, 0.5)

@clip("Carve_Heel", 30, True)
def _(t): return breathe(HEEL, t, 0.5)

TUCK = P_(hips=V(0.02, 0.05, -0.34), hrot=(34, 0, 10), spine=(34, 0, 10), head=(-36, 0, 40), neck=(-8, 0, 14),
          kL=side_pole(0.5, -1, 0.1), kR=side_pole(0.8, -1, 0.1),
          hL=("rel", V(0.14, -0.30, -0.22)), eL=V(0.3, 0.3, -0.9),
          hR=("rel", V(0.02, -0.28, -0.26)), eR=V(-0.3, 0.3, -0.9), gL=0.7, gR=0.7)

@clip("Tuck", 30, True)
def _(t): return breathe(TUCK, t, 0.3)

# the ollie: weight back over the tail, coiled low, arms swung back ready to throw
WIND = P_(hips=V(-0.09, 0.03, -0.34), hrot=(28, 0, 10), spine=(22, 0, 4), head=(4, 0, 50),
          kL=side_pole(0.5, -1, 0), kR=side_pole(0.6, -1, 0),
          hL=("rel", V(0.02, 0.28, -0.30)), eL=V(0.3, 0.3, -0.8), hR=("rel", V(-0.14, 0.28, -0.28)), eR=V(-0.4, 0.3, -0.6))
AIR = P_(hips=V(0.0, 0.03, -0.25), hrot=(14, 0, 14), spine=(12, 0, 10), head=(-2, 0, 44),
         hL=("rel", V(0.40, -0.12, -0.10)), eL=V(0.1, 0.6, -0.7), hR=("rel", V(-0.36, -0.06, -0.14)), eR=V(-0.1, 0.6, -0.7),
         gL=0.3, gR=0.3)
POP = P_(hips=V(0.04, 0.0, -0.02), hrot=(6, 0, 12), spine=(-2, 0, 8), head=(2, 0, 44),
         hL=("rel", V(0.26, -0.30, 0.12)), eL=V(0.1, 0.4, -0.8), hR=("rel", V(-0.10, -0.30, 0.06)), eR=V(-0.3, 0.4, -0.8),
         fL=(ANK["Left"], 15, -8, 0), fR=(ANK["Right"], -12, 6, 0))

@clip("Ollie_Windup", 2)
def _(t): return WIND

def overlap(A, B, k_body, k_arms):
    """Everything goes from A to B, but the arms trail behind the body (overlapping action)."""
    P = mix(A, B, k_body)
    Q = mix(A, B, k_arms)
    for key in ("hL", "hR", "eL", "eR", "gL", "gR", "head", "neck"): P[key] = Q[key]
    return P

@clip("Ollie_Pop", 16)
def _(t):
    # a last dip, the snap up (legs first, arms flung after them), then the knees come up into the air pose
    if t < 0.18: return mix(WIND, dict(WIND, hips=WIND["hips"] + V(0, 0, -0.04)), ease(t / 0.18))
    if t < 0.45:
        k = (t - 0.18) / 0.27
        return overlap(dict(WIND, hips=WIND["hips"] + V(0, 0, -0.04)), POP, ease(min(1, k * 1.3)), ease(k * k))
    k = (t - 0.45) / 0.55
    return overlap(POP, AIR, ease(min(1, k * 1.4)), ease(k))

@clip("Air", 40, True)
def _(t):
    p = breathe(AIR, t, 0.5)
    w = math.sin(t * 2 * math.pi)
    p["hL"] = ("rel", p["hL"][1] + V(0, 0, 0.03 * w)); p["hR"] = ("rel", p["hR"][1] + V(0, 0, -0.03 * w))
    return p

LAND = P_(hips=V(0.0, 0.04, -0.42), hrot=(34, 0, 12), spine=(20, 0, 8), head=(10, 0, 44),
          hL=("rel", V(0.32, -0.34, -0.20)), eL=V(0.1, 0.4, -0.8), hR=("rel", V(-0.04, -0.36, -0.24)), eR=V(-0.3, 0.4, -0.8))
LAND_ARMS = dict(LAND, hL=("rel", V(0.36, -0.38, -0.30)), hR=("rel", V(0.0, -0.40, -0.34)))

@clip("Land", 20)
def _(t):
    # the legs soak it up fast; the arms carry on down a beat later, then everything settles back to riding
    if t < 0.22: return overlap(mix(AIR, STANCE, 0.5), LAND, ease(t / 0.22), ease(t / 0.22) * 0.6)
    if t < 0.45: return overlap(LAND, LAND_ARMS, 0.0, ease((t - 0.22) / 0.23))
    k = (t - 0.45) / 0.55
    return overlap(LAND_ARMS, STANCE, ease(k), ease(max(0, k - 0.15) / 0.85))

# grabs: knees pulled right up, a hand on the board
def board_pt(x, y, z=-0.006): return V(x, y, z)
GRABBED = dict(hips=V(0.0, 0.02, -0.40), hrot=(22, 0, 12), spine=(14, 0, 10), head=(8, 0, 44),
               kL=side_pole(0.5, -1, 0.15), kR=side_pole(0.7, -1, 0.15))
GRABS = {
    "Indy": P_(**GRABBED, hR=("abs", board_pt(-0.04, -0.15)), eR=V(-0.3, 0.5, 0.3), gR=1.0,
               hL=("rel", V(0.32, 0.02, 0.18)), eL=V(0.0, 0.4, -0.9), gL=0.2),
    "Melon": P_(**dict(GRABBED, hrot=(26, 0, 4), spine=(30, 6, -4)), hL=("abs", board_pt(0.05, 0.155)), eL=V(0.6, -0.2, 0.4), gL=1.0,
                hR=("rel", V(-0.36, 0.08, 0.12)), eR=V(0.0, 0.5, -0.8), gR=0.2),
    "Mute": P_(**dict(GRABBED, spine=(30, 4, 2)), hL=("abs", board_pt(0.10, -0.152)), eL=V(0.2, 0.3, 0.8), gL=1.0,
               hR=("rel", V(-0.34, 0.04, 0.16)), eR=V(0.0, 0.5, -0.8), gR=0.2),
    # method: hips pushed out over the toe edge so the board is tweaked up behind, back arched, head over the shoulder
    "Method": P_(**dict(GRABBED, hips=V(0.0, -0.02, -0.40), hrot=(24, 0, 10), spine=(-6, 6, 10), head=(14, 0, 64)),
                 hL=("abs", board_pt(0.03, 0.155)), eL=V(0.5, 0.2, 0.6), gL=1.0,
                 hR=("rel", V(-0.24, 0.10, 0.36)), eR=V(0.0, 0.6, -0.6), gR=0.2,
                 fL=(ANK["Left"] + V(0, 0.0, 0.0), 15, 0, 0)),
    "Stalefish": P_(**dict(GRABBED, hrot=(20, 0, 2)), hR=("abs", board_pt(-0.16, 0.152)), eR=V(-0.6, 0.0, 0.5), gR=1.0,
                    hL=("rel", V(0.30, -0.18, 0.22)), eL=V(0.0, 0.5, -0.8), gL=0.2),
    "Tail Grab": P_(**dict(GRABBED, hips=V(-0.10, 0.02, -0.34), spine=(30, 0, 0)), hR=("abs", board_pt(-0.70, 0.0, 0.02)), eR=V(0.0, 0.4, 0.8), gR=1.0,
                    hL=("rel", V(0.34, -0.10, 0.20)), eL=V(0.0, 0.5, -0.8), gL=0.2),
}
GRAB_LIFT = 0.11   # the game brings the board up this far to the knees in a grab (RiderBodyHero)

def lifted(P, h):
    """The pose as the game shows it: the board (so the feet, and a hand on it) raised by h."""
    P = deep(P)
    for k in ("fL", "fR"):
        at_, yw, pt, rl = P[k]; P[k] = (at_ + V(0, 0, h), yw, pt, rl)
    for k in ("hL", "hR"):
        if P[k][0] == "abs": P[k] = ("abs", Vector(P[k][1]) + V(0, 0, h))
    return P

def fix_reach(P, name):
    """Bring the grabbing hand to its spot the way a rider does: the board comes up to the knees (as the game
    lifts it), the hips drop and shift toward the grab, the back bends to that side and the shoulder drops into
    it. Never folded flat: the chest stays clear of the knees."""
    P = deep(P)
    sd = "L" if P.get("hL", ("rel",))[0] == "abs" else "R"
    side = "Left" if sd == "L" else "Right"
    shift = V(0, 0, 0)
    for it in range(30):
        T = lifted(P, GRAB_LIFT)
        target = Vector(T["h" + sd][1]) + V(0, 0, 0.065)
        s = solve(T)
        sh = s.head(side + "UpperArm")
        err = (s.head(side + "Hand") - target).length
        if err < 0.02: break
        d = target - sh
        flat = V(d.x, d.y, 0)
        heel = target.y > 0.05   # behind the legs: sit back into it rather than folding forward
        if flat.length > 0.05 and shift.length < (0.16 if heel else 0.12):
            step = flat.normalized() * 0.012
            shift += step; P["hips"] = P["hips"] + step
        if P["hips"].z > -0.54: P["hips"] = P["hips"] + V(0, 0, -0.012)
        x, y, z = P["spine"]
        x = min(x + 1.5, 16 if heel else 36)
        y = max(-20, min(20, y + (2.5 if d.x > 0 else -2.5)))   # lean toward the grab side (+x is the nose side)
        z = z + (2 if d.y > 0 else -2) * (1 if sd == "L" else -1)
        P["spine"] = (x, y, max(-20, min(45 if heel else 30, z)))
        px, py, pz = P["hrot"]
        if heel and abs(z) >= 44: pz += 2 if sd == "L" else -2      # the hips turn with it, toward the heel edge
        P["hrot"] = (min(px + 1, 32), py, max(-10, min(40, pz)))
    print("HERO grab %-10s reaches within %.3f m with the board up %.2f (hips %.2f, spine %s)" % (name, err, GRAB_LIFT, P["hips"].z, tuple(round(v) for v in P["spine"])))
    return P
for gname in list(GRABS): GRABS[gname] = fix_reach(GRABS[gname], gname)
for gname, gp in GRABS.items():
    def mk(gp):
        return lambda t: breathe(gp, t, 0.4)
    CLIPS.append(("Grab_" + gname.replace(" ", ""), 30, True, mk(gp)))

# spins: wound up against the turn before take-off, then wrapped tight with the head leading
SPIN_WIND_L = P_(hips=V(0.0, 0.02, -0.26), hrot=(18, 0, -6), spine=(20, 0, -28), head=(-12, 0, 70),
                 hL=("rel", V(0.06, 0.24, -0.30)), eL=V(0.4, 0.3, -0.6), hR=("rel", V(0.10, -0.30, -0.28)), eR=V(-0.4, 0.4, -0.6))
SPIN_AIR_L = P_(hips=V(0.0, 0.02, -0.30), hrot=(16, 0, 30), spine=(14, 0, 30), head=(-6, 0, 50),
                hL=("rel", V(0.06, -0.26, -0.22)), eL=V(0.6, 0.4, -0.6), hR=("rel", V(0.24, -0.24, -0.14)), eR=V(-0.4, 0.5, -0.5),
                gL=0.7, gR=0.7)
def turned(p, k=-1):
    """The same pose wound the other way."""
    q = deep(p)
    for key in ("hrot", "spine"):
        x, y, z = q[key]; q[key] = (x, y, 16 + (z - 16) * k if key == "hrot" else 10 + (z - 10) * k)
    x, y, z = q["head"]; q["head"] = (x, y, 46 + (z - 46) * k * 0.5)
    hl, hr = q["hL"][1], q["hR"][1]
    q["hL"] = ("rel", V(hl.x, -hl.y, hl.z)); q["hR"] = ("rel", V(hr.x, -hr.y, hr.z))
    return q
@clip("Spin_WindL", 2)
def _(t): return SPIN_WIND_L
@clip("Spin_WindR", 2)
def _(t): return turned(SPIN_WIND_L)
@clip("Spin_AirL", 30, True)
def _(t): return breathe(SPIN_AIR_L, t, 0.3)
@clip("Spin_AirR", 30, True)
def _(t): return breathe(turned(SPIN_AIR_L), t, 0.3)

FLIP = P_(hips=V(0.0, 0.0, -0.46), hrot=(36, 0, 10), spine=(36, 0, 6), head=(0, 0, 30),
          kL=side_pole(0.3, -1, 0.3), kR=side_pole(0.5, -1, 0.3),
          hL=("rel", V(0.12, -0.34, -0.24)), eL=V(0.5, 0.2, -0.6), hR=("rel", V(0.02, -0.34, -0.28)), eR=V(-0.5, 0.2, -0.6),
          gL=1.0, gR=1.0)
@clip("Flip_Tuck", 30, True)
def _(t): return breathe(FLIP, t, 0.3)

# a fallback for a crash with no ragdoll: arms thrown out
@clip("Flail", 20, True)
def _(t):
    a = t * 2 * math.pi
    return P_(hips=V(0, 0.04, -0.2), spine=(-10, 6 * math.sin(a), 0), hrot=(0, 0, 0), head=(-20, 0, 20),
              hL=("rel", V(0.35, 0.1 * math.sin(a), 0.25 + 0.1 * math.cos(a))), hR=("rel", V(-0.35, -0.1 * math.sin(a), 0.25 + 0.1 * math.sin(a))),
              eL=V(0, 0.5, -0.8), eR=V(0, 0.5, -0.8), gL=0.0, gR=0.0)

# sat in the snow tube: hips down in the ring, leaning back, heels up on the front, hands on the handles
@clip("Sit", 2)
def _(t):
    return P_(hips=V(0, 0.09, -0.60), hrot=(-30, 0, 0), spine=(-6, 0, 0), neck=(0, 0, 0), head=(24, 0, 0),
              fL=(V(0.14, -0.74, 0.30), 6, 34, 0), fR=(V(-0.14, -0.74, 0.30), -6, 34, 0),
              kL=side_pole(0.2, -0.4, 1), kR=side_pole(-0.2, -0.4, 1),
              hL=("abs", V(0.58, 0.04, 0.44)), eL=V(0.6, 0.6, -0.2), hR=("abs", V(-0.58, 0.04, 0.44)), eR=V(-0.6, 0.6, -0.2),
              gL=1.0, gR=1.0)

# -- on foot in the camp: hands free (the board waits at the gate), both arms swinging --------------------
STAND = {
    "hips": V(-0.02, 0.0, -0.012), "hrot": (2, -3, 0), "spine": (2, 2, 0), "neck": (0, 0, 0), "head": (2, 0, 0),
    "fL": (V(0.13, -0.03, ANKLE_Z), 10, 0, 0), "fR": (V(-0.12, 0.02, ANKLE_Z), -8, 0, 0),
    "kL": V(0.2, -1, 0), "kR": V(-0.1, -1, 0),
    "hL": ("rel", V(0.07, -0.04, -0.50)), "eL": V(0.4, 0.8, 0), "wL": (0, 0, 0), "gL": 0.35,
    "hR": ("rel", V(-0.06, -0.02, -0.50)), "eR": V(-0.4, 0.8, 0), "wR": (0, 0, 0), "gR": 0.35,
}

@clip("Stand", 150, True)
def _(t):
    p = breathe(STAND, t * 3)
    look = 34 * math.sin(t * 2 * math.pi) + 8 * math.sin(t * 6 * math.pi + 1)
    p["head"] = (2 + 3 * math.sin(t * 4 * math.pi), 0, look)
    p["neck"] = (0, 0, look * 0.3)
    p["hips"] = p["hips"] + V(0.01 * math.sin(t * 2 * math.pi + 1), 0, 0)
    return p

def gait(t, run):
    """One cycle of walking (run 0) or running (run 1), in place, facing -y. The left heel strikes at t = 0.25."""
    S = lerp(0.36, 0.46, run)               # how far each foot reaches forward and back of the hips
    stance = lerp(0.6, 0.36, run)           # the share of the cycle a foot is on the ground
    lift = lerp(0.11, 0.30, run)
    p = deep(STAND)
    ph = 2 * math.pi * t
    feet = {}
    for sd, off, sx in (("Left", 0.0, 1), ("Right", 0.5, -1)):
        u = (t - 0.25 - off) % 1.0          # 0 at this heel's strike
        if u < stance:
            k = u / stance
            y = lerp(-S, S, k)
            z = ANKLE_Z
            pitch = 14 * (1 - sm(0, 0.18, k)) - 30 * sm(0.7, 1.0, k)   # heel strike toes up; heel peels off at the end
            z += 0.07 * sm(0.72, 1.0, k) * (1 - run * 0.4)
            toe = 28 * sm(0.75, 1.0, k)
        else:
            k = (u - stance) / (1 - stance)
            y = lerp(S, -S, 0.5 - 0.5 * math.cos(math.pi * k))
            # the foot comes up behind (much higher running, kicked toward the seat) then reaches through
            z = ANKLE_Z + lift * math.sin(math.pi * min(1, k * (1.15 + 0.35 * run))) ** 1.2 + 0.04 * (1 - k) * (1 - run)
            y += 0.10 * run * math.sin(math.pi * k) * (1 - k) * 2
            pitch = lerp(-40, 14, sm(0.0, 0.9, k))
            toe = 28 * (1 - sm(0, 0.3, k))
        feet[sd] = (V(sx * 0.11, y, z), 6 * sx, pitch, 0)
        p["toe" + sd[0]] = toe
    p["fL"], p["fR"] = feet["Left"], feet["Right"]
    p["kL"] = V(0.05, -1, 0); p["kR"] = V(-0.05, -1, 0)
    c2 = math.cos(2 * (ph - math.pi / 2))
    # hips: lowest as weight comes onto a leg, highest over it; a sway toward the standing foot
    p["hips"] = V(0.018 * math.sin(ph - 0.3) * (1 - run), -0.03 * run, -0.035 - 0.05 * run - (0.02 + 0.02 * run) * c2)
    p["hrot"] = (4 + 8 * run, 3 * math.sin(ph) * (1 - run), 7 * math.sin(ph))
    p["spine"] = (4 + 10 * run, -2 * math.sin(ph) * (1 - run), -13 * math.sin(ph) - 4 * run * math.sin(ph))
    p["neck"] = (0, 0, 3 * math.sin(ph))
    p["head"] = (6 + 4 * run, 0, 3 * math.sin(ph))
    # the free arm swings against the legs; running it pumps, bent at the elbow
    sw = math.sin(ph)
    walk_h = V(0.08, 0.20 * sw, -0.48 + 0.04 * abs(sw))
    run_h = V(0.07, 0.24 * sw - 0.06, -0.24 + 0.12 * max(0, -sw))
    p["hL"] = ("rel", walk_h.lerp(run_h, run))
    p["eL"] = V(0.3, 1, -0.2 - 0.6 * run)
    p["gL"] = lerp(0.35, 0.8, run)
    # the right arm: the mirror of the left, half a cycle on (forward as the left leg comes through)
    walk_r = V(-0.08, -0.20 * sw, -0.48 + 0.04 * abs(sw))
    run_r = V(-0.07, -0.24 * sw - 0.06, -0.24 + 0.12 * max(0, sw))
    p["hR"] = ("rel", walk_r.lerp(run_r, run))
    p["eR"] = V(-0.3, 1, -0.2 - 0.6 * run)
    p["gR"] = lerp(0.35, 0.8, run)
    return p

@clip("Walk", 30, True)
def _(t): return gait(t, 0.0)

@clip("Run", 30, True)
def _(t): return gait(t, 1.0)

print("HERO clips:", ", ".join(c[0] for c in CLIPS))

# ----------------------------------------------------------------------------------------------- export
def bake_clips():
    """Every clip becomes an action on the skeleton, every frame keyed (IK solved per frame: the feet never slide)."""
    ARM.animation_data_create()
    for name, frames, loop, fn in CLIPS:
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        ARM.animation_data.action = act
        for f in range(frames + 1):
            apply(solve(fn(f / frames)))
            for n in BONE_NAMES:
                pb = ARM.pose.bones[n]
                pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
                if n in ("Hips", "Root"): pb.keyframe_insert("location", frame=f, group=n)
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, frames
        act.use_cyclic = loop
    ARM.animation_data.action = None
    apply(Solver())

def export_all():
    import json
    apply(Solver())
    rider = os.path.join(PROJECT, "Assets/Summit/Resources/Rider/Hero.fbx")
    anims = os.path.join(PROJECT, "Assets/Summit/Art/Hero/HeroAnims.fbx")
    os.makedirs(os.path.dirname(anims), exist_ok=True)
    common = dict(apply_unit_scale=True, add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis='Y',
                  secondary_bone_axis='X', armature_nodetype='NULL', path_mode='AUTO', embed_textures=False, mesh_smooth_type='OFF')
    bpy.ops.object.select_all(action='DESELECT')
    for o in [ARM] + list(OBJS.values()): o.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.export_scene.fbx(filepath=rider, use_selection=True, object_types={'ARMATURE', 'MESH'}, bake_anim=False, **common)
    bake_clips()
    bpy.ops.object.select_all(action='DESELECT')
    ARM.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.export_scene.fbx(filepath=anims, use_selection=True, object_types={'ARMATURE'}, bake_anim=True,
                             bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0, **common)
    # what the editor needs to know about each clip (Assets/Editor/HeroBake.cs reads it)
    with open(os.path.join(os.path.dirname(anims), "HeroClips.json"), "w") as fh:
        json.dump({"fps": 30, "clips": [{"name": n, "frames": fr, "loop": lp} for n, fr, lp, _ in CLIPS]}, fh, indent=1)
    print("HERO exported", rider, anims)

if PROJECT:
    export_all()

# ----------------------------------------------------------------------------------------------- renders
def render_setup(w=640, h=800):
    sc = scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    try:
        sc.eevee.taa_render_samples = 32
        sc.eevee.use_shadows = True
    except Exception: pass
    if sc.world is None:
        wd = bpy.data.worlds.new("Sky"); sc.world = wd; wd.use_nodes = True
        bg = wd.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.62, 0.70, 0.82, 1); bg.inputs[1].default_value = 0.9
    if "Sun" not in bpy.data.objects:
        sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sc.collection.objects.link(sun)
        sun.data.energy = 3.2; sun.data.angle = math.radians(3)
        sun.rotation_euler = (math.radians(48), math.radians(8), math.radians(-35))
        fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", 'SUN')); sc.collection.objects.link(fill)
        fill.data.energy = 0.7; fill.rotation_euler = (math.radians(70), 0, math.radians(150))
        snow = bpy.data.materials.new("Snow"); snow.use_nodes = True
        snow.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.92, 0.94, 0.98, 1)
        snow.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
        bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, -0.03))
        g = bpy.context.active_object; g.name = "Ground"; g.data.materials.append(snow)
    if "Cam" not in bpy.data.objects:
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam)
    sc.camera = bpy.data.objects["Cam"]

def look_from(yaw_deg, pitch_deg, target, dist, lens=50, ortho=None):
    cam = bpy.data.objects["Cam"]
    a, e = math.radians(yaw_deg), math.radians(pitch_deg)
    d = V(math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    else: cam.data.type = 'PERSP'; cam.data.lens = lens

def shot(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

def label(text, at, size=0.07, name="Label"):
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = text; cu.size = size; cu.align_x = 'CENTER'
    ob = bpy.data.objects.new(name, cu); scene.collection.objects.link(ob)
    ob.location = at; ob.rotation_euler = (math.radians(90), 0, 0)
    m = bpy.data.materials.new("Ink"); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.06, 0.08, 1)
    cu.materials.append(m)
    return ob

def pose_by_name(name, t=0.0):
    for n, fr, loop, fn in CLIPS:
        if n == name: return fn(t)
    raise KeyError(name)

def tile(paths, cols, out):
    rows = []
    for i in range(0, len(paths), cols):
        chunk = paths[i:i + cols]
        while len(chunk) < cols: chunk.append(chunk[-1])
        rows.append(chunk)
    args = ["ffmpeg", "-loglevel", "error", "-y"]
    for r in rows:
        for pth in r: args += ["-i", pth]
    n = len(rows) * cols
    fc = "".join("[%d:v]" % i for i in range(n)) + "xstack=inputs=%d:layout=" % n
    W = scene.render.resolution_x; H = scene.render.resolution_y
    fc += "|".join("%d_%d" % ((i % cols) * W, (i // cols) * H) for i in range(n))
    subprocess.run(args + ["-filter_complex", fc, out], check=True)

if RENDER and ("poses" in WHAT or "all" in WHAT):
    os.makedirs(RENDER, exist_ok=True)
    render_setup(420, 560)
    names = ["Ride_Idle", "Ride", "Carve_Toe", "Carve_Heel", "Tuck", "Ollie_Windup", "Air", "Land",
             "Grab_Indy", "Grab_Melon", "Grab_Mute", "Grab_Method", "Grab_Stalefish", "Grab_TailGrab",
             "Spin_WindL", "Spin_AirL", "Flip_Tuck", "Sit", "Stand", "Walk", "Run"]
    paths = []
    for i, nm in enumerate(names):
        t = 0.3 if nm == "Land" else (0.25 if nm in ("Walk", "Run") else 0.0)
        P = pose_by_name(nm, t)
        up = GRAB_LIFT if nm.startswith("Grab") or nm == "Flip_Tuck" else 0.0
        apply(solve(lifted(P, up) if up else P))
        lab = label(nm.replace("_", " "), V(0, -1.2, -0.02), 0.09)
        board = OBJS["Snowboard"]; board.hide_render = nm in ("Stand", "Walk", "Run", "Sit")
        board.location = V(0, 0, up)
        look_from(22, 8, V(0, 0, 0.74), 4.2, 50)
        lab.location = V(0, 0, 0); lab.rotation_euler = bpy.data.objects["Cam"].rotation_euler; lab.location = V(0.0, -0.1, 1.82)
        pth = os.path.join(RENDER, "pose_%02d.png" % i); shot(pth); paths.append(pth)
        bpy.data.objects.remove(lab)
    OBJS["Snowboard"].hide_render = False; OBJS["Snowboard"].location = V(0, 0, 0)
    tile(paths, 7, os.path.join(RENDER, "HeroPoseSheet.png"))
    print("HERO pose sheet written")

def video(frames_glob, out_base, fps=30):
    """frames -> mp4 (h264) and a small gif"""
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(fps), "-i", frames_glob, "-pix_fmt", "yuv420p",
                    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-crf", "18", out_base + ".mp4"], check=True)
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(fps), "-i", frames_glob, "-vf",
                    "fps=15,scale=360:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse", out_base + ".gif"], check=True)

def place(obj_list, loc=V(0, 0, 0), rot=None):
    for o in obj_list:
        o.location = loc
        o.rotation_euler = rot or Euler((0, 0, 0))

RIDER_OBJS = lambda: [ARM, OBJS["Snowboard"]]

if RENDER and ("turn" in WHAT or "all" in WHAT):
    os.makedirs(RENDER, exist_ok=True)
    render_setup(560, 760)
    for tag, P in (("rest", None), ("stance", STANCE)):
        apply(solve(P) if P else Solver())
        OBJS["Snowboard"].hide_render = P is None
        d = os.path.join(RENDER, "turn_" + tag); os.makedirs(d, exist_ok=True)
        for i in range(36):
            look_from(i * 10, 6, V(0, 0, 0.85 if P is None else 0.72), 4.6, 50)
            shot(os.path.join(d, "%03d.png" % i))
        video(os.path.join(d, "%03d.png"), os.path.join(RENDER, "HeroTurntable_" + tag), fps=12)
        tile([os.path.join(d, "%03d.png" % i) for i in (0, 9, 18, 27)], 4, os.path.join(RENDER, "HeroViews_" + tag + ".png"))
    OBJS["Snowboard"].hide_render = False
    print("HERO turntables written")

if RENDER and ("rig" in WHAT or "all" in WHAT):
    os.makedirs(RENDER, exist_ok=True)
    render_setup(640, 820)
    # the skin see-through, the deform bones drawn as solid octahedra on top (left blue, right red, middle gold)
    ghost = {}
    for name in ("HeroBody", "HeroLegs", "HeroGear"):
        ob = OBJS[name]
        ghost[name] = list(ob.data.materials)
        m = bpy.data.materials.new("Ghost" + name); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (0.85, 0.88, 0.95, 1); b.inputs["Alpha"].default_value = 0.22
        try: m.surface_render_method = 'BLENDED'
        except Exception: m.blend_method = 'BLEND'
        m.use_backface_culling = True
        for i in range(len(ob.data.materials)): ob.data.materials[i] = m
    def bone_mat(rgb):
        m = bpy.data.materials.new("Bone"); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = (*rgb, 1)
        b.inputs["Emission Color"].default_value = (*rgb, 1); b.inputs["Emission Strength"].default_value = 0.35
        return m
    bpy.data.objects["Ground"].hide_render = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.06, 0.08, 1)
    cols = {"L": bone_mat((0.15, 0.45, 1.0)), "R": bone_mat((1.0, 0.25, 0.2)), "C": bone_mat((1.0, 0.75, 0.15)), "X": bone_mat((0.6, 0.3, 1.0))}
    bone_objs = []
    for n in BONE_NAMES:
        if n == "Root": continue
        L = LEN[n]; w = min(0.1 * L, 0.025) if "Index" not in n and "Thumb" not in n else 0.006
        bm_ = bmesh.new()
        pts = [V(0, 0, 0), V(w, 0.15 * L, 0), V(0, 0.15 * L, w), V(-w, 0.15 * L, 0), V(0, 0.15 * L, -w), V(0, L, 0)]
        vs = [bm_.verts.new(p_) for p_ in pts]
        for a, b_ in ((1, 2), (2, 3), (3, 4), (4, 1)):
            bm_.faces.new((vs[0], vs[a], vs[b_])); bm_.faces.new((vs[5], vs[b_], vs[a]))
        me = bpy.data.meshes.new("B_" + n); bm_.to_mesh(me); bm_.free()
        side = "X" if n in ("Hood",) or "Twist" in n else ("L" if n.startswith("Left") else "R" if n.startswith("Right") else "C")
        me.materials.append(cols[side])
        ob = bpy.data.objects.new("B_" + n, me); scene.collection.objects.link(ob)
        ob.parent = ARM; ob.parent_type = 'BONE'; ob.parent_bone = n
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.location = V(0, -L, 0)   # a bone child sits at the tail: back to the head
        ob.show_in_front = True
        bone_objs.append(ob)
    paths = []
    for i, (P, yaw, tgt, dist) in enumerate([(None, 0, V(0, 0, 0.9), 4.4), (None, 90, V(0, 0, 0.9), 4.4),
                                             (STANCE, 25, V(0, 0, 0.72), 3.9), (GRABS["Indy"], 30, V(0, 0, 0.6), 3.6)]):
        apply(solve(P) if P else Solver())
        OBJS["Snowboard"].hide_render = P is None
        look_from(yaw, 6, tgt, dist, 50)
        pth = os.path.join(RENDER, "rig_%d.png" % i); shot(pth); paths.append(pth)
    tile(paths, 4, os.path.join(RENDER, "HeroRigOverlay.png"))
    bpy.data.objects["Ground"].hide_render = False
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.70, 0.82, 1)
    for ob in bone_objs: bpy.data.objects.remove(ob)
    for name, mats in ghost.items():
        for i, m in enumerate(mats): OBJS[name].data.materials[i] = m
    OBJS["Snowboard"].hide_render = False
    print("HERO rig overlay written")

if RENDER and ("weights" in WHAT or "all" in WHAT):
    # the joints pushed to the limits a snowboarder takes them to, close up: does the skin hold its volume?
    os.makedirs(RENDER, exist_ok=True)
    render_setup(480, 560)
    OBJS["Snowboard"].hide_render = True
    tests = [
        ("deep squat", dict(STANCE, hips=V(0.0, 0.06, -0.45), hrot=(30, 0, 10), spine=(30, 0, 10)), 70, V(0, 0, 0.45), 2.0),
        ("arms overhead", dict(STAND, hL=("rel", V(0.1, -0.05, 0.50)), hR=("rel", V(-0.1, -0.05, 0.50)), eL=V(0.5, 0.3, 0), eR=V(-0.5, 0.3, 0)), 20, V(0, 0, 1.25), 2.2),
        ("arm across", dict(STAND, hL=("rel", V(-0.42, -0.25, -0.05)), eL=V(0.3, 0.3, -0.8), hR=("rel", V(0.05, -0.1, -0.5))), 10, V(0, 0, 1.2), 1.9),
        ("twist + look", dict(STAND, spine=(5, 0, 55), neck=(0, 0, 25), head=(-10, 0, 60)), 20, V(0, 0, 1.25), 2.0),
        ("wrists", dict(STAND, hL=("rel", V(0.3, -0.35, -0.1)), wL=(0, 70, 0), hR=("rel", V(-0.3, -0.35, -0.1)), wR=(0, 70, 0)), 0, V(0, -0.2, 1.1), 1.9),
        ("high knee", dict(STAND, fL=(V(0.12, -0.3, 0.55), 0, 20, 0), kL=V(0, -1, 0.3)), 60, V(0, 0, 0.7), 2.2),
    ]
    paths = []
    for i, (nm, P, yaw, tgt, dist) in enumerate(tests):
        apply(solve(P))
        look_from(yaw, 8, tgt, dist, 50)
        lab = label(nm, V(0, 0, 0), 0.06)
        lab.rotation_euler = bpy.data.objects["Cam"].rotation_euler
        cam = bpy.data.objects["Cam"]
        lab.location = cam.location + (cam.rotation_euler.to_matrix() @ V(0, 0.36, -1.0)) * 1.0
        lab.data.size = 0.045
        pth = os.path.join(RENDER, "stress_%d.png" % i); shot(pth); paths.append(pth)
        bpy.data.objects.remove(lab)
    tile(paths, 3, os.path.join(RENDER, "HeroDeformation.png"))
    OBJS["Snowboard"].hide_render = False

def sequence(name, frames, fn, cam_fn, size=(540, 640)):
    """Render a sequence: fn(i) poses the rider (and moves the rig/board), cam_fn(i) places the camera."""
    render_setup(*size)
    d = os.path.join(RENDER, "seq_" + name); os.makedirs(d, exist_ok=True)
    for i in range(frames):
        fn(i); cam_fn(i)
        shot(os.path.join(d, "%03d.png" % i))
    video(os.path.join(d, "%03d.png"), os.path.join(RENDER, "Hero_" + name))

def roll_about_board(angle_deg, lift=0.0, pitch=0.0, yaw=0.0, at=V(0, 0, 0)):
    """Tip the whole rider and board onto an edge (about the board's long axis), as the game does."""
    R = Euler((math.radians(angle_deg), math.radians(pitch), math.radians(yaw)), 'XYZ')
    for o in RIDER_OBJS():
        o.rotation_euler = R; o.location = at + V(0, 0, lift)

if RENDER and ("anims" in WHAT or "all" in WHAT):
    os.makedirs(RENDER, exist_ok=True)
    # 1. carving: heel-side and toe-side turns linked, the board on edge, the body over it
    def carve(i):
        t = i / 30.0
        toe = math.sin(t * math.pi * 0.8)                          # +1 toe side, -1 heel side
        k = min(1.0, abs(toe) ** 0.8)
        base = pose_by_name("Ride", (t % 2) / 2)
        side = pose_by_name("Carve_Toe" if toe > 0 else "Carve_Heel", (t % 1))
        P = mix(base, side, k)
        apply(solve(P))
        roll_about_board(28 * toe, yaw=-12 * math.cos(t * math.pi * 0.8))
    sequence("carve", 150, carve, lambda i: look_from(28, 8, V(0, 0, 0.6), 4.4, 50))
    # 2. a jump: wind up, pop, rise into an indy, hold it, let go, land and absorb
    def jump(i):
        t = i / 30.0
        T0, T1 = 0.6, 2.4    # take-off, landing
        h = lift = 0.0
        if t < 0.45:
            P = mix(pose_by_name("Ride", t / 2), pose_by_name("Ollie_Windup"), ease(t / 0.45))
        elif t < T1:
            P = pose_by_name("Ollie_Pop", min(1, (t - 0.45) / 0.47))
            if t >= T0:
                u = (t - T0) / (T1 - T0)
                h = 4.2 * u * (1 - u)
                if t > 0.92: P = mix(P, pose_by_name("Air", u), sm(0.92, 1.1, t))
                g = sm(0.16, 0.32, u) * (1 - sm(0.72, 0.88, u))
                P = mix(P, GRABS["Indy"], g)
                lift = 0.11 * g
        else:
            P = pose_by_name("Land", min(1, (t - T1) / 0.6))
        if lift > 0:
            # the board comes up to the knees: the feet (and a hand on the board) rise with it
            P = deep(P)
            for k in ("fL", "fR"):
                at_, yw, pt, rl = P[k]; P[k] = (at_ + V(0, 0, lift), yw, pt, rl)
            for k in ("hL", "hR"):
                if P[k][0] == "abs": P[k] = ("abs", P[k][1] + V(0, 0, lift))
                elif P[k][0] == "blend": P[k] = ("blend", P[k][1], ("abs", P[k][2][1] + V(0, 0, lift)) if P[k][2][0] == "abs" else P[k][2], P[k][3])
        apply(solve(P))
        ARM.location = V(0, 0, h); ARM.rotation_euler = Euler((0, 0, 0))
        OBJS["Snowboard"].location = V(0, 0, h + lift); OBJS["Snowboard"].rotation_euler = Euler((0, 0, 0))
    sequence("jump", 105, jump, lambda i: look_from(30, 6, V(0, 0, 1.0), 6.2, 50))
    # 3. the camp: standing, into a walk, into a run, the board in the right hand
    board_carry = OBJS["Snowboard"]
    post = bpy.data.materials.new("Post"); post.use_nodes = True
    post.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.2, 0.25, 0.35, 1)
    for k in range(-2, 22):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.025, depth=0.5, location=(-0.9, -k * 1.0, 0.22))
        bpy.context.active_object.data.materials.append(post)
    def camp(i):
        t = i / 30.0
        amp = sm(0.4, 1.2, t); run = sm(3.0, 4.0, t)
        cad = lerp(lerp(0.6, 1.0, amp), 1.6, run)
        camp.ph = getattr(camp, "ph", 0.0) + cad / 30.0
        P = mix(pose_by_name("Stand", t / 5), mix(gait(camp.ph % 1, 0), gait(camp.ph % 1, 1), run), amp)
        apply(solve(P))
        speed = amp * lerp(1.6, 4.4, run)
        camp.x = getattr(camp, "x", 0.0) + speed / 30.0
        ARM.location = V(0, -camp.x, 0); ARM.rotation_euler = Euler((0, 0, 0))
        board_carry.hide_render = True   # the board waits at the gate
    sequence("camp", 180, camp, lambda i: look_from(80, 6, V(0, -getattr(camp, "x", 0) - 0.4, 0.85), 5.0, 50))
    board_carry.hide_render = False
    ARM.location = V(0, 0, 0)
    print("HERO sequences written")
