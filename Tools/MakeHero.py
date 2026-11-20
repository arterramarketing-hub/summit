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
# --character buddy: a second rider on the same skeleton (so every clip is shared): the player's friend as he
# stood for his photos: a stocky build, curly dark hair and a moustache, a blue-and-white snap fleece, polar-bear
# pyjama bottoms and white sneakers. Writes Resources/Rider/Buddy.fbx and BuddyPants.png; the clips come from the
# hero's run.
CHAR = arg("--character", "hero")
FEM = CHAR == "herof"                    # the summit rider's kit cut for a woman's build (the same head and gear)
# how her jacket differs, ring by ring: (width, depth) against his. Narrower shoulders and a drawn-in waist, the hips kept
FEMCUT = {"hemfold": (0.94, 0.94), "hem": (0.94, 0.94), "skirt": (0.95, 0.95), "hips": (0.97, 0.97), "waist": (0.82, 0.84), "belly": (0.83, 0.88),
          "a": (0.87, 0.98), "b": (0.89, 1.02), "c": (0.90, 0.95), "d": (0.94, 0.94), "e": (0.95, 0.95), "efold": (0.95, 0.95)}
# --character breanna: Breanna (spelt Breanna at first, and still called that here on the command line), built the
# way Val is with her own shapes: long dark curls under a grey beanie, white goggles with a pink mirror lens, a cream
# half-zip fleece and black trousers. --character breannapj: her first outfit, the Nordic fleece and plaid pyjamas
# (its head is never shown: the game puts the clothes on whoever wears them).
# --character paul: Paul, a skier: short dark hair and a moustache under a cream beanie with his goggles pushed up on
# it, a white turtleneck under a black puffer, red ski trousers, blue ski boots. --character paulb: his second
# outfit, a grey hoodie under a black puffer vest with a white balaclava, charcoal trousers, the same boots.
if CHAR == "brianna": CHAR = "breanna"
VAL = CHAR == "buddy"                    # Pajama Buddy Val
BRI = CHAR in ("breanna", "breannapj")   # Breanna (her head, hair and hands; her clothes by BRI_PJ)
BRI_PJ = CHAR == "breannapj"
PAUL = CHAR in ("paul", "paulb")
PAUL_B = CHAR == "paulb"
BUDDY = VAL or BRI or PAUL               # the riders built from photos: a bare face, their own clothes and hands
SKIN, HAIR = ("skin3", "hair2") if BRI else ("skin4", "hair3") if PAUL else ("skin2", "hair")
# the file each one is written to (Resources/Rider/<name>.fbx), and the name in the pictures it paints
FILE = {"hero": "Hero", "herof": "HeroF", "buddy": "Buddy", "breanna": "Breanna", "breannapj": "BreannaPJ", "paul": "Paul", "paulb": "PaulB"}[CHAR]

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
    ("spare", (1, 1, 1), 0.2),   # (white: Tools/MakeRides.py lays the skis' and the sled's painted parts on it)
    ("pack", (0.13, 0.14, 0.16), 0.25), ("packside", (0.20, 0.21, 0.24), 0.2), ("packaccent", (0.92, 0.72, 0.16), 0.35),
    ("seam", (0.10, 0.10, 0.11), 0.3), ("zip", (0.70, 0.71, 0.74), 0.7), ("gaiter", (0.18, 0.19, 0.23), 0.12),
    ("gaiterband", (0.92, 0.72, 0.16), 0.15), ("chinstrap", (0.07, 0.07, 0.08), 0.3),
    # the buddy: skin and hair, the fleece's snaps and stitched mark, sneakers, a chain
    ("skin2", (0.83, 0.57, 0.45), 0.3), ("hair", (0.045, 0.030, 0.022), 0.0), ("tache", (0.44, 0.28, 0.16), 0.3),
    ("sneaker", (0.93, 0.93, 0.92), 0.4), ("sneakergrey", (0.60, 0.62, 0.66), 0.4), ("snap", (0.06, 0.06, 0.08), 0.6),
    ("gold", (0.90, 0.68, 0.20), 0.85), ("stitch", (0.04, 0.16, 0.50), 0.3),
    ("eyewhite", (0.90, 0.89, 0.86), 0.5), ("iris", (0.34, 0.45, 0.50), 0.6), ("lip", (0.60, 0.30, 0.28), 0.35),
    # (hair has no shine at all: seen at a shallow angle under a bright sky, even a little turns dark hair grey)
    # Breanna: her skin and hair, brown eyes, (once) clear glasses frames, a scrunchie, slipper boots
    # (the game's closet repaints swatches by their place here: Wardrobe / RiderBody's PaletteCells and ShoeCell.
    # Add new ones at the end, and never move helmet, frame, lens, glove, cuff, boot, vent, the pack's three, sneaker or slipper)
    ("skin3", (0.82, 0.62, 0.52), 0.3), ("hair2", (0.030, 0.018, 0.014), 0.0), ("irisb", (0.26, 0.15, 0.08), 0.6), ("lip2", (0.68, 0.30, 0.31), 0.4),
    ("frameclear", (0.70, 0.74, 0.75), 0.9), ("scrunch", (0.30, 0.06, 0.10), 0.2), ("slipper", (0.66, 0.52, 0.37), 0.1), ("slippersole", (0.74, 0.63, 0.47), 0.15),
    ("logocyan", (0.08, 0.68, 0.86), 0.45),
    # Paul: olive skin, near-black hair, his cream beanie, blue ski boots with grey buckles, the balaclava;
    # Breanna's grey beanie and her goggles (a white frame, a pink mirror lens, a pink strap)
    ("skin4", (0.74, 0.53, 0.40), 0.3), ("hair3", (0.035, 0.025, 0.020), 0.0), ("beanie", (0.60, 0.56, 0.47), 0.0),
    ("skiboot", (0.10, 0.26, 0.58), 0.55), ("buckle", (0.62, 0.64, 0.68), 0.7), ("beanie2", (0.20, 0.20, 0.21), 0.0),
    ("frame2", (0.90, 0.90, 0.91), 0.5), ("lens2", (0.95, 0.42, 0.62), 0.95), ("strap2", (0.88, 0.40, 0.56), 0.25),
    ("balaclava", (0.92, 0.92, 0.91), 0.08), ("tache2", (0.10, 0.07, 0.05), 0.2)]
PALI = {p[0]: i for i, p in enumerate(PAL)}
assert len(PALI) == len(PAL), "two swatches share a name: the later one would take the first one's gear"
PAL_ROWS = 8   # always 8 x 8: a size the game takes as it is. Any other height it rescales, which bleeds each row of colours into the next
assert len(PAL) <= 64
def swatch_uv(name):
    i = PALI[name]
    return ((i % 8 + 0.5) / 8.0, (i // 8 + 0.5) / PAL_ROWS)

class Cage:
    """A low polygon cage: vertices with weights and a uv, quads and triangles."""
    def __init__(self, name, mat):
        self.name, self.mat = name, mat
        self.v, self.w, self.uv, self.f = [], [], [], []
        self.cur, self.part = "gear", []    # which part of the rider is being built now, and each vertex's part
    def add(self, co, w, uv=(0.5, 0.5)):
        self.v.append(Vector(co)); self.w.append(dict(w)); self.uv.append(uv); self.part.append(self.cur)
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

# The rider comes apart into WHO they are and WHAT they wear, so the game can put any rider in any outfit (they all
# share one skeleton): the head (with hair, face and neck) and the bare hands are the person; the jacket, the
# trousers and the rest of the gear (boots, gloves, pack) are the outfit. An outfit with gloves hides the hands.
PARTS = {"head": "HeroHead", "hands": "HeroHands", "gear": "HeroGear"}
def split_parts(G):
    out = []
    for part, name in PARTS.items():
        C = Cage(name, G.mat); remap = {}
        for fc in G.f:
            if G.part[fc[0]] != part: continue
            for i in fc:
                if i not in remap: remap[i] = C.add(G.v[i], G.w[i], G.uv[i])
            C.face(*[remap[i] for i in fc])
        if C.f: out.append(C)
    return out

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
XTRA = []      # extra pieces built to their final shape (not subdivided, faces already turned the right way)
SHAPES = {}    # face shapes the game can blend in (a blink, raised brows): name -> [(where a vertex is, where it goes)]
GEAR_SHAPES = {}   # the same, on the outfit's gear (the balaclava pulled down)
GOGGLES = None     # where the goggles are worn (Breanna's and Paul's): for the shape that pushes them up onto the beanie
FACE_UV = (0.28, 0.30, 0.15)   # the painted face: how wide and tall a piece of the head it covers (m, head units), and how far below the middle it starts

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
    if VAL:
        # a fleece pullover on a stocky build: a full belly and chest, an elastic hem at the hips, a short stand collar
        R = [
            ("hemfold", (0.885, 0.875), 0.215, 0.170, 0.162, 0.0, 2.3, {"Hips": 1}),
            ("hem", (0.862, 0.852), 0.250, 0.204, 0.188, 0.0, 2.4, {"Hips": 1}),
            ("skirt", (0.905, 0.895), 0.268, 0.226, 0.200, 0.0, 2.4, {"Hips": 1}),
            ("hips", (0.96, 0.95), 0.274, 0.236, 0.202, 0.0, 2.5, {"Hips": 1}),
            ("waist", (1.04, 1.04), 0.272, 0.240, 0.194, 0.0, 2.5, {"Hips": 0.45, "Spine": 0.55}),
            ("belly", (1.12, 1.12), 0.270, 0.228, 0.186, 0.0, 2.5, {"Spine": 0.55, "Chest": 0.45}),
            ("a", (1.20, 1.20), 0.270, 0.204, 0.178, 0.005, 2.6, {"Chest": 0.75, "UpperChest": 0.25}),
            ("b", (1.285, 1.285), 0.280, 0.180, 0.172, 0.01, 2.7, {"Chest": 0.3, "UpperChest": 0.7}),
            ("c", (1.37, 1.37), 0.250, 0.150, 0.154, 0.02, 2.5, {"UpperChest": 1}),
            ("d", (1.425, 1.425), 0.126, 0.106, 0.114, 0.025, 2.0, {"UpperChest": 0.65, "Neck": 0.35}),
            ("e", (1.440, 1.442), 0.104, 0.094, 0.100, 0.02, 2.0, {"Neck": 0.4, "UpperChest": 0.6}),
            ("efold", (1.428, 1.430), 0.088, 0.080, 0.086, 0.02, 2.0, {"Neck": 0.4, "UpperChest": 0.6}),
        ]
    sp = {"Hips": 0.35, "Spine": 0.65}; nk = {"Neck": 0.7, "Head": 0.1, "UpperChest": 0.2}
    if BRI_PJ:
        # her pyjama fleece: slimmer, cropped to an elastic hem at the waist that the body blouses over, the shoulders
        # dropped, and a funnel neck standing up under the chin
        R = [
            ("hemfold", (1.000, 1.000), 0.150, 0.114, 0.108, 0.0, 2.3, sp),
            ("hem", (1.006, 1.006), 0.170, 0.130, 0.122, 0.0, 2.3, sp),
            ("skirt", (1.032, 1.032), 0.198, 0.152, 0.140, 0.0, 2.4, {"Hips": 0.15, "Spine": 0.85}),
            ("hips", (1.072, 1.072), 0.206, 0.158, 0.146, 0.0, 2.4, {"Spine": 1}),
            ("waist", (1.115, 1.115), 0.206, 0.156, 0.146, 0.0, 2.4, {"Spine": 0.6, "Chest": 0.4}),
            ("belly", (1.160, 1.160), 0.206, 0.154, 0.146, 0.0, 2.4, {"Spine": 0.3, "Chest": 0.7}),
            ("a", (1.20, 1.20), 0.212, 0.152, 0.146, 0.005, 2.5, {"Chest": 0.75, "UpperChest": 0.25}),
            ("b", (1.285, 1.285), 0.226, 0.148, 0.148, 0.01, 2.6, {"Chest": 0.3, "UpperChest": 0.7}),
            ("c", (1.37, 1.37), 0.214, 0.126, 0.132, 0.02, 2.5, {"UpperChest": 1}),
            ("d", (1.425, 1.425), 0.100, 0.092, 0.096, 0.025, 2.0, {"UpperChest": 0.65, "Neck": 0.35}),
            ("e", (1.512, 1.520), 0.092, 0.094, 0.090, 0.015, 2.0, nk),
            ("efold", (1.498, 1.506), 0.074, 0.076, 0.072, 0.015, 2.0, nk),
        ]
    elif BRI:
        # her half-zip fleece, as in her photo: loose and long, down over the hips, the shoulders dropped, a short stand
        # collar (the zip runs from it down the chest: see the details)
        R = [
            ("hemfold", (0.880, 0.870), 0.178, 0.132, 0.126, 0.0, 2.3, {"Hips": 1}),
            ("hem", (0.856, 0.846), 0.212, 0.164, 0.154, 0.0, 2.4, {"Hips": 1}),
            ("skirt", (0.898, 0.888), 0.222, 0.172, 0.160, 0.0, 2.4, {"Hips": 1}),
            ("hips", (0.952, 0.942), 0.222, 0.172, 0.160, 0.0, 2.5, {"Hips": 1}),
            ("waist", (1.030, 1.030), 0.216, 0.166, 0.154, 0.0, 2.5, {"Hips": 0.45, "Spine": 0.55}),
            ("belly", (1.115, 1.115), 0.216, 0.162, 0.152, 0.0, 2.5, {"Spine": 0.55, "Chest": 0.45}),
            ("a", (1.20, 1.20), 0.218, 0.160, 0.150, 0.005, 2.5, {"Chest": 0.75, "UpperChest": 0.25}),
            ("b", (1.285, 1.285), 0.230, 0.152, 0.150, 0.01, 2.6, {"Chest": 0.3, "UpperChest": 0.7}),
            ("c", (1.37, 1.37), 0.216, 0.130, 0.134, 0.02, 2.5, {"UpperChest": 1}),
            ("d", (1.425, 1.425), 0.100, 0.094, 0.098, 0.025, 2.0, {"UpperChest": 0.65, "Neck": 0.35}),
            ("e", (1.488, 1.494), 0.084, 0.088, 0.082, 0.015, 2.0, nk),
            ("efold", (1.476, 1.482), 0.070, 0.074, 0.068, 0.015, 2.0, nk),
        ]
    elif PAUL:
        # a black puffer on a slim build (the vest of his second outfit is the same body): full and quilted, to the hips,
        # the collar standing open round the white turtleneck (or the hoodie's neck)
        k_ = 0.96 if PAUL_B else 1.0
        R = [
            ("hemfold", (0.86, 0.84), 0.190 * k_, 0.128 * k_, 0.140 * k_, 0.01, 2.3, {"Hips": 1}),
            ("hem", (0.840, 0.815), 0.226 * k_, 0.160 * k_, 0.172 * k_, 0.012, 2.4, {"Hips": 1}),
            ("skirt", (0.878, 0.858), 0.222 * k_, 0.158 * k_, 0.168 * k_, 0.012, 2.4, {"Hips": 1}),
            ("hips", (0.94, 0.93), 0.210 * k_, 0.150 * k_, 0.158 * k_, 0.01, 2.5, {"Hips": 1}),
            ("waist", (1.02, 1.02), 0.198 * k_, 0.142 * k_, 0.146 * k_, 0.01, 2.5, {"Hips": 0.45, "Spine": 0.55}),
            ("belly", (1.115, 1.115), 0.206 * k_, 0.148 * k_, 0.146 * k_, 0.005, 2.5, {"Spine": 0.55, "Chest": 0.45}),
            ("a", (1.20, 1.20), 0.232 * k_, 0.160 * k_, 0.154 * k_, 0.005, 2.6, {"Chest": 0.75, "UpperChest": 0.25}),
            ("b", (1.285, 1.285), 0.248 * k_, 0.152 * k_, 0.150 * k_, 0.01, 2.7, {"Chest": 0.3, "UpperChest": 0.7}),
            ("c", (1.37, 1.37), 0.222 * k_, 0.132 * k_, 0.136 * k_, 0.02, 2.5, {"UpperChest": 1}),
            ("d", (1.425, 1.425), 0.112, 0.102, 0.108, 0.025, 2.0, {"UpperChest": 0.65, "Neck": 0.35}),
            ("e", (1.478, 1.470), 0.110, 0.112, 0.104, 0.015, 2.0, {"Neck": 0.5, "UpperChest": 0.5}),
            ("efold", (1.466, 1.458), 0.096, 0.098, 0.090, 0.015, 2.0, {"Neck": 0.5, "UpperChest": 0.5}),
        ]
    rings = {}
    for name, (zf, zb), rx, ryf, ryb, cy, p, w in R:
        if FEM: rx, ryf, ryb = rx * FEMCUT[name][0], ryf * FEMCUT[name][1], ryb * FEMCUT[name][1]
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
            if VAL and k in (N - 1, 0) and lo in ("c", "d", "e"):
                # the top snap is undone: the front opens in a V from the collar down to just above the second snap
                if lo == "c":
                    sx_ = -1 if k == N - 1 else 1
                    nv = J.add(V(sx_ * 0.030, 0.025 - 0.106 * 0.971, 1.425), {"UpperChest": 0.65, "Neck": 0.35})
                    if sx_ < 0: J.face(A[k], A[k1], nv, B[k])
                    else: J.face(A[k], A[k1], B[k1], nv)
                continue
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
        if FEM: sleeve = [(d, r * 0.88, e_, w) for d, r, e_, w in sleeve]   # slimmer arms
        if PAUL:
            # puffer sleeves, fuller than the summit jacket's; or the hoodie's, under the vest
            sleeve = [(d, r * (1.0 if PAUL_B else 1.08), e_, w) for d, r, e_, w in sleeve]
        if VAL or BRI:
            # thicker arms in a loose fleece sleeve, gathered at an elastic cuff
            k_ = 1.06 if BRI else 1.16
            sleeve = [(d, r * k_, e_, w) for d, r, e_, w in sleeve[:7]] + [(0.500, 0.060 if BRI else 0.066, 1, sleeve[7][3]), (0.492, 0.046 if BRI else 0.052, 1, sleeve[8][3])]
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
def make_hood(mat="Jacket"):
    H = Cage("HeroHood", mat)
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
        if VAL: rx, ryf, ryb = rx * 1.2, ryf * 1.4, ryb * 1.16   # a fuller seat and belly
        if FEM: rx, ryf, ryb = rx * (0.88 if name == "top" else 0.98), ryf * 0.92, ryb * 0.96   # a narrower waist over the same hips
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
        if FEM: legs = [(z, a_ * (0.95 if z > 0.6 else 0.88), b_ * (0.95 if z > 0.6 else 0.88), e_, w_, o_) for z, a_, b_, e_, w_, o_ in legs]   # slimmer legs
        if VAL:
            # pyjama bottoms: soft and very loose, bunched at an elastic ankle over the sneakers
            legs = [
                (0.745, 0.146, 0.152, 0.2, {ul: 0.9, "Hips": 0.1}, 0),
                (0.645, 0.140, 0.146, 0.7, {ul: 1}, 0),
                (0.565, 0.132, 0.138, 1, {ul: 0.92, ll: 0.08}, 0.004),
                (0.490, 0.126, 0.134, 1, {ul: 0.5, ll: 0.5}, 0.012),
                (0.415, 0.122, 0.126, 1, {ul: 0.1, ll: 0.9}, 0.0),
                (0.310, 0.120, 0.124, 1, {ll: 1}, 0),
                (0.225, 0.118, 0.122, 1, {ll: 1}, 0),
                (0.175, 0.122, 0.128, 1, {ll: 0.95, ft: 0.05}, 0),      # the slack gathers above the cuff
                (0.140, 0.104, 0.108, 1, {ll: 0.9, ft: 0.1}, 0),
                (0.118, 0.082, 0.086, 1, {ll: 0.88, ft: 0.12}, 0),
                (0.126, 0.066, 0.070, 1, {ll: 0.88, ft: 0.12}, 0),
            ]
        if BRI and not BRI_PJ:
            # her black trousers: straight and slim-fitting, the hems resting on her boots
            legs = [
                (0.745, 0.118, 0.124, 0.2, {ul: 0.9, "Hips": 0.1}, 0),
                (0.645, 0.110, 0.116, 0.7, {ul: 1}, 0),
                (0.565, 0.102, 0.108, 1, {ul: 0.92, ll: 0.08}, 0.004),
                (0.490, 0.096, 0.102, 1, {ul: 0.5, ll: 0.5}, 0.008),
                (0.415, 0.092, 0.096, 1, {ul: 0.1, ll: 0.9}, 0.0),
                (0.310, 0.090, 0.094, 1, {ll: 1}, 0),
                (0.205, 0.090, 0.094, 1, {ll: 1}, 0),
                (0.120, 0.092, 0.098, 1, {ll: 0.92, ft: 0.08}, 0),
                (0.084, 0.090, 0.096, 1, {ll: 0.88, ft: 0.12}, 0),
            ]
        if BRI_PJ:
            # her pyjamas: straight and wide all the way down, the hems falling over her boots
            legs = [
                (0.745, 0.120, 0.126, 0.2, {ul: 0.9, "Hips": 0.1}, 0),
                (0.645, 0.114, 0.120, 0.7, {ul: 1}, 0),
                (0.565, 0.108, 0.114, 1, {ul: 0.92, ll: 0.08}, 0.004),
                (0.490, 0.104, 0.110, 1, {ul: 0.5, ll: 0.5}, 0.010),
                (0.415, 0.102, 0.106, 1, {ul: 0.1, ll: 0.9}, 0.0),
                (0.310, 0.102, 0.106, 1, {ll: 1}, 0),
                (0.205, 0.104, 0.108, 1, {ll: 1}, 0),
                (0.105, 0.106, 0.112, 1, {ll: 0.92, ft: 0.08}, 0),
                (0.062, 0.100, 0.106, 1, {ll: 0.88, ft: 0.12}, 0),
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

def smooth_tree(C, levels):
    """The surface a cage becomes once it is subdivided, as something to cast rays at (for laying details on it)."""
    from mathutils.bvhtree import BVHTree
    me = bpy.data.meshes.new("tmp")
    me.from_pydata([tuple(v) for v in C.v], [], [list(f) for f in C.f]); me.update()
    ob = bpy.data.objects.new("tmp", me); bpy.context.scene.collection.objects.link(ob)
    md = ob.modifiers.new("s", 'SUBSURF'); md.levels = levels
    bm = bmesh.new(); bm.from_mesh(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).data)
    tree = BVHTree.FromBMesh(bm)
    bpy.data.objects.remove(ob); bpy.data.meshes.remove(me)
    return tree, bm

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

def bare_hand(G, s, SKIN):
    """A bare hand: a broad palm, the fingers together, a thumb."""
    sd = SIDE[s]
    sh, u, el, f, wr, kn = arm_points(s)
    H = {sd + "Hand": 1}
    I1, I2, I3 = {sd + "IndexProximal": 1}, {sd + "IndexIntermediate": 1}, {sd + "IndexDistal": 1}
    loft(G, [(wr - f * 0.03, 0.036, 0.030, {sd + "ForearmTwist": 0.5, sd + "Hand": 0.5}), (wr + f * 0.02, 0.046, 0.030, H), (kn - f * 0.005, 0.050, 0.028, H),
             (kn + f * 0.02, 0.048, 0.025, {sd + "Hand": 0.5, sd + "IndexProximal": 0.5}),
             (kn + f * 0.045, 0.046, 0.022, I1), (kn + f * 0.07, 0.043, 0.020, I2), (kn + f * 0.09, 0.036, 0.017, I3),
             (kn + f * 0.102, 0.018, 0.010, I3)], 10, SKIN, 2.6, ref=FWD)
    tb = wr + f * 0.025 + FWD * 0.032
    td = (f * 0.45 + FWD * 0.75 + V(0, 0, -0.35)).normalized()
    T1, T2, T3 = {sd + "ThumbProximal": 1}, {sd + "ThumbIntermediate": 1}, {sd + "ThumbDistal": 1}
    loft(G, [(tb - td * 0.012, 0.017, 0.017, {sd + "Hand": 0.6, sd + "ThumbProximal": 0.4}), (tb + td * 0.02, 0.017, 0.018, T1),
             (tb + td * 0.045, 0.015, 0.016, T2), (tb + td * 0.066, 0.012, 0.013, T3), (tb + td * 0.076, 0.005, 0.006, T3)],
         6, SKIN, 2.0, ref=f)

def make_gear_buddy():
    """The buddy's head, hands and feet: bare-headed with curly dark hair and a moustache, bare hands, sneakers."""
    G = Cage("HeroGear", "Gear"); G.cur = "head"
    hd = {"Head": 1}
    HC = V(0, -0.004, 1.615)
    HS = 1.07 if BRI else 1.08 if PAUL else 1.13     # a touch big, as the hero's helmet is: the head is what you read first at phone size
    def HV(x, y, z): return HC + V(x, y, z) * HS
    if BRI or PAUL:
        nr = 1.0 if BRI else 1.16     # (his a little thicker)
        loft(G, [(V(0, 0.018, 1.40), 0.058 * nr, 0.060 * nr, {"UpperChest": 0.7, "Neck": 0.3}), (V(0, 0.015, 1.455), 0.050 * nr, 0.052 * nr, {"Neck": 1}),
                 (V(0, 0.008, 1.53), 0.050 * nr, 0.052 * nr, {"Neck": 0.4, "Head": 0.6})], 10, SKIN, 2.0, closed_start=False, closed_end=False, ref=FWD)
    else:
        # a thick neck, a gold chain round its base
        uc = {"UpperChest": 1}
        G.cur = "gear"
        loft(G, [(V(0, 0.012, 1.33), 0.135, 0.130, uc), (V(0, 0.016, 1.375), 0.120, 0.115, uc),
                 (V(0, 0.018, 1.412), 0.090, 0.088, {"UpperChest": 0.7, "Neck": 0.3})], 10, SKIN, 2.0, closed_start=False, closed_end=False, ref=FWD)
        G.cur = "head"
        loft(G, [(V(0, 0.018, 1.395), 0.086, 0.084, {"UpperChest": 0.7, "Neck": 0.3}), (V(0, 0.015, 1.455), 0.070, 0.072, {"Neck": 1}),
                 (V(0, 0.008, 1.52), 0.070, 0.072, {"Neck": 0.4, "Head": 0.6})], 10, SKIN, 2.0, closed_start=False, closed_end=False, ref=FWD)
        chain = [V(math.sin(a) * 0.078, 0.02 - math.cos(a) * (0.078 + 0.022 * max(0.0, math.cos(a))), 1.478 - 0.070 * max(0.0, math.cos(a)) ** 1.5)
                 for a in [-2.2 + 4.4 * k / 12 for k in range(13)]]
        loft(G, [(c, 0.004, 0.004, {"UpperChest": 0.5, "Neck": 0.5}) for c in chain], 4, "gold", 2.0, ref=V(0, 0, 1))
    # the head: a full, round face with a soft jaw (after his portrait)
    head = [(HV(0, 0.0, -0.138), 0.052, 0.058, hd), (HV(0, -0.004, -0.114), 0.092, 0.092, hd, 0.086),
            (HV(0, -0.006, -0.066), 0.110, 0.106, hd, 0.100), (HV(0, -0.004, 0.0), 0.110, 0.103, hd, 0.108),
            (HV(0, 0, 0.07), 0.096, 0.090, hd, 0.100), (HV(0, 0, 0.115), 0.045, 0.045, hd)]
    if PAUL:   # his (after his photos): a lean oval, a firm square jaw and chin
        head = [(HV(0, 0.0, -0.138), 0.040, 0.046, hd), (HV(0, -0.004, -0.115), 0.072, 0.070, hd, 0.070),
                (HV(0, -0.006, -0.066), 0.094, 0.088, hd, 0.092), (HV(0, -0.004, 0.0), 0.104, 0.094, hd, 0.106),
                (HV(0, 0, 0.07), 0.095, 0.088, hd, 0.100), (HV(0, 0, 0.115), 0.045, 0.045, hd)]
    if BRI:   # hers: a longer, slimmer oval with a narrow chin
        head = [(HV(0, 0.0, -0.142), 0.032, 0.034, hd), (HV(0, -0.004, -0.118), 0.066, 0.054, hd, 0.066),
                (HV(0, -0.006, -0.066), 0.094, 0.077, hd, 0.090), (HV(0, -0.004, 0.0), 0.104, 0.088, hd, 0.106),
                (HV(0, 0, 0.07), 0.096, 0.086, hd, 0.100), (HV(0, 0, 0.115), 0.045, 0.045, hd)]
    # The features are part of the face, not things stuck on it: each is laid point by point onto the skin (the
    # head as it is once smoothed), rising a millimetre or a few with its edge sunk under the skin, so nothing
    # floats or casts a gap. Eyes (light grey-blue, under a heavy lid), thick low brows, a full moustache the
    # colour of his hair with its ends turned down, the mouth as one shape, and the tuft under the lip.
    # The head, nose and ears are skin with a painted face on it (face_paint, further down): colour in the cheeks,
    # the shadow of a beard, lids and lips, laid on from the front. They are smoothed here, not later with the
    # rest of the gear, so the features below can be laid on the finished skin.
    XH = Cage("Head", "FaceSkin"); XH.recalc = True; XTRA.append((XH, "HeroHead"))
    def face_uv(q):
        r = (Vector(q) - HC) / HS
        return (0.5 + r.x / FACE_UV[0], (r.z + FACE_UV[2]) / FACE_UV[1])
    def add_smooth(C_):
        tree_, bm_ = smooth_tree(C_, 1); o = len(XH.v)
        for v in bm_.verts: XH.add(v.co.copy(), hd, face_uv(v.co))
        for f in bm_.faces: XH.face(*[o + v.index for v in f.verts])
        return tree_, bm_
    HO = Cage("HeadOnly", "Gear"); loft(HO, head, 12, SKIN, 2.2, ref=FWD)
    skin_tree, skin_bm = add_smooth(HO)
    XF = Cage("Face", "Gear"); XTRA.append((XF, "HeroHead"))
    # the nose: one shape that grows out of the face. Each section sits on the skin where it is (found by a ray),
    # flush at the bridge and standing out most at the tip, and is much broader and deeper than what shows, so
    # its sides meet the cheeks at a shallow angle instead of standing off them
    nose = []
    for z_, proud, deep, wide in ((0.026, 0.0005, 0.014, 0.009), (0.004, 0.0050, 0.018, 0.012), (-0.020, 0.0150, 0.026, 0.019),
                                  (-0.033, 0.0125, 0.026, 0.022), (-0.043, 0.0020, 0.018, 0.019)):
        hit = skin_tree.ray_cast(HV(0, -0.6, z_), V(0, 1, 0))[0]
        if BRI: proud, wide = proud * 0.8, wide * 0.7      # a slimmer, smaller nose
        if PAUL: proud, wide = proud * 1.1, wide * 0.8     # a long, straight nose
        nose.append((V(0, hit.y - proud + deep, hit.z), deep, wide, hd))
    NO = Cage("NoseOnly", "Gear"); loft(NO, nose, 8, SKIN, 2.0, ref=FWD)
    add_smooth(NO)[1].free()
    # the ears grow out of the side of the head the same way: the outline is found on the skin by rays from the
    # side, its root is sunk under the skin, and it stands out from the head as an ear does, close at the front
    # edge and furthest at the back, with a rim and a hollow inside it. A stud in each lobe.
    XE = Cage("Ears", "FaceSkin"); XTRA.append((XE, "HeroHead"))
    for sx in (1, -1):
        f0 = len(XE.f); n_ = 10
        def ear(k_, a, lift):
            dy = 0.015 * k_ * math.cos(a) * (1.0 + 0.22 * math.sin(a)) + 0.004 * math.sin(a)     # broader at the top, leaning back
            hit = skin_tree.ray_cast(HV(sx * 0.6, 0.008 + dy, -0.018 + 0.031 * k_ * math.sin(a)), V(-sx, 0, 0))[0]
            q = hit + V(sx * lift, 0, 0)
            # (the ear takes plain skin from the painted face, a touch pink: laid on from the front like the rest, its
            # rim reached out into the hair's shadow at the edge of the picture and read as a dark ring from afar)
            return XE.add(q, hd, face_uv(HV(sx * 0.085, 0.0, -0.010 - 0.012 * k_)))
        # (it stood out further, with a deep hollow: from afar the pale rim round a dark middle read as a ring worn
        # on the side of the head. Closer to the head now, and the hollow only a shallow dip)
        def flare(a): return 0.008 + 0.015 * (0.5 + 0.5 * math.cos(a))
        angs = [2 * math.pi * i / n_ for i in range(n_)]
        rings_ = [[ear(1.0, a, -0.004) for a in angs], [ear(1.0, a, flare(a) * 0.6) for a in angs], [ear(0.90, a, flare(a)) for a in angs],
                  [ear(0.58, a, flare(a) * 0.68 + 0.001) for a in angs]]
        mid = ear(0.0, 0.0, 0.009)
        for r0, r1 in zip(rings_, rings_[1:]):
            for i in range(n_): XE.face(r0[i], r0[(i + 1) % n_], r1[(i + 1) % n_], r1[i])
        for i in range(n_): XE.face(rings_[-1][i], rings_[-1][(i + 1) % n_], mid)
        for i in range(f0, len(XE.f)):     # every face turned to look outward
            q0, q1, q2 = [XE.v[j_] for j_ in XE.f[i][:3]]
            c_ = sum([XE.v[j_] for j_ in XE.f[i]], Vector()) / len(XE.f[i])
            if (q1 - q0).cross(q2 - q0).dot(c_ - HV(sx * 0.06, 0.008, -0.018)) < 0: XE.f[i] = tuple(reversed(XE.f[i]))
        lobe = XE.v[rings_[2][int(n_ * 0.75)]]
        if BRI:   # a small gold hoop through each lobe
            hoop = [lobe + V(sx * 0.004, -0.002 + 0.010 * math.sin(a), -0.004 - 0.010 * math.cos(a)) for a in [2 * math.pi * k / 8 for k in range(9)]]
            loft(G, [(c, 0.0026, 0.0026, hd) for c in hoop], 4, "gold", 2.0, ref=V(1, 0, 0))
        elif VAL: loft(G, [(lobe + V(sx * 0.001, -0.002, 0.006), 0.0042, 0.0042, hd), (lobe + V(sx * 0.006, -0.002, 0.006), 0.0036, 0.0036, hd)], 6, "zip", 2.0, ref=V(0, 0, 1))
    # The features are part of the face, not things stuck on it: each is laid point by point onto the skin,
    # rising a millimetre or a few with its edge sunk under the skin, so nothing floats or casts a gap.
    # After his photos: narrow, heavy-lidded grey-blue eyes; thick, low, straight brows; a slim moustache that
    # follows the top lip, in the colour of his hair; the lower lip as one shape; a small wedge under it.
    def fpoint(x, z, lift):
        hit, nm, _i, _d = skin_tree.ray_cast(HV(x, -0.6, z), V(0, 1, 0))
        if nm.y > 0: nm = -nm
        return hit + nm * lift
    def fadd(x, z, lift, col): return XF.add(fpoint(x, z, lift), hd, swatch_uv(col))
    def to_front(f0):
        for i in range(f0, len(XF.f)):
            q0, q1, q2 = [XF.v[j] for j in XF.f[i][:3]]
            if (q1 - q0).cross(q2 - q0).y > 0: XF.f[i] = tuple(reversed(XF.f[i]))
    SUNK = 0.0007
    def decal(cx, cz, rx, rz, col, lift, base=0.0, n_=12):   # a rounded patch
        f0 = len(XF.f)
        def ring(k_, l_): return [fadd(cx + rx * k_ * math.cos(2 * math.pi * i / n_), cz + rz * k_ * math.sin(2 * math.pi * i / n_), l_, col) for i in range(n_)]
        r0, r1, c_ = ring(1.0, base - SUNK), ring(0.78, lift), fadd(cx, cz, lift, col)
        for i in range(n_):
            i1 = (i + 1) % n_
            XF.face(r0[i], r0[i1], r1[i1], r1[i]); XF.face(r1[i], r1[i1], c_)
        to_front(f0)
    def band(path, col, lift, base=0.0, tips=(1.2, 1.2), moved=None):
        """A ridge along a line of (x, z, half width) points, pointed at its ends. moved: (shape name, how far up
        each point goes in that shape) to make it one of the face's moving parts."""
        f0 = len(XF.f); rows_ = []; m = len(path)
        def across(i):
            x0, z0 = path[max(i - 1, 0)][:2]; x1, z1 = path[min(i + 1, m - 1)][:2]
            ln = math.hypot(x1 - x0, z1 - z0)
            return (x1 - x0) / ln, (z1 - z0) / ln
        def put(x, z, l_, i):
            ix = fadd(x, z, l_, col)
            if moved: SHAPES.setdefault(moved[0], []).append((XF.v[ix].copy(), fpoint(x, z + moved[1](i), l_)))
            return ix
        for i, (x, z, hw) in enumerate(path):
            tx, tz = across(i)
            top = lift * (0.45 if i in (0, m - 1) else 1.0)
            rows_.append([put(x - tz * hw * k_, z + tx * hw * k_, l_, i) for k_, l_ in
                          ((-1, base - SUNK), (-0.55, base + (top - base) * 0.8), (0, top), (0.55, base + (top - base) * 0.8), (1, base - SUNK))])
        for r0, r1 in zip(rows_, rows_[1:]):
            for k in range(4): XF.face(r0[k], r0[k + 1], r1[k + 1], r1[k])
        for i, sg, tk in ((0, -1, tips[0]), (m - 1, 1, tips[1])):
            tx, tz = across(i); x, z, hw = path[i]
            tip = put(x + tx * hw * tk * sg, z + tz * hw * tk * sg, base - SUNK, i)
            for k in range(4): XF.face(rows_[i][k], rows_[i][k + 1], tip)
        to_front(f0)
    for sx in (1, -1):
        ex, ez, ew = 0.040 * sx, 0.004, 0.0158
        decal(ex, ez, ew, 0.0076 if BRI else 0.0066, "eyewhite", 0.0009)
        decal(ex, ez + 0.0008, 0.0066 if BRI else 0.0062, 0.0066 if BRI else 0.0062, "irisb" if BRI or PAUL else "iris", 0.0016, 0.0009, 10)
        decal(ex, ez + 0.0008, 0.0027, 0.0027, "snap", 0.0022, 0.0016, 8)
        # the upper lid: skin above the eye with the lash line along its edge, sitting low over the iris (his eyes
        # are heavy-lidded). In the "Blink" shape its edge comes down over the eye.
        cols_ = []
        for i in range(7):
            u_ = -1 + 2 * i / 6
            x = ex + u_ * (ew + 0.001)
            arch = max(0.0, 1 - u_ * u_) ** 0.5
            z_open, z_shut = ez + 0.0006 + (0.0064 if BRI else 0.0048) * arch, ez - 0.0006 - (0.0074 if BRI else 0.0064) * arch
            col_ = []
            for dz, l_, c_, moves in ((0.0055, -SUNK, SKIN, 0.0), (0.0026, 0.0027, SKIN, 0.5), (0.0, 0.0031, SKIN, 1.0),
                                      (0.0, 0.0031, HAIR, 1.0), (-0.0030 if BRI else -0.0020, 0.0027, HAIR, 1.0)):
                q = fpoint(x, z_open + dz, l_ if abs(u_) < 0.99 else min(l_, 0.0012))
                ix = XE.add(q, hd, face_uv(q)) if c_ == SKIN else XF.add(q, hd, swatch_uv(c_))   # the lid is painted skin, the lash line hair
                if moves: SHAPES.setdefault("Blink", []).append((q.copy(), fpoint(x, lerp(z_open, z_shut, moves) + dz * (1 if dz < 0 else 1 - moves), max(l_, 0.0027))))
                col_.append(ix)
            cols_.append(col_)
        for c0, c1 in zip(cols_, cols_[1:]):
            for k in (0, 1, 3):
                C_ = XE if k < 2 else XF
                quad = (c0[k], c0[k + 1], c1[k + 1], c1[k])
                q0, q1, q2 = [C_.v[j] for j in quad[:3]]
                C_.face(*(quad if (q1 - q0).cross(q2 - q0).y < 0 else quad[::-1]))
        if BRI:   # hers: finer, higher and arched (her glasses are gone: in her new photo she wears goggles)
            band([(0.014 * sx, 0.0235, 0.0026), (0.029 * sx, 0.0285, 0.0034), (0.044 * sx, 0.0300, 0.0030), (0.055 * sx, 0.0262, 0.0018)], HAIR, 0.0022,
                 moved=("Brows", lambda i: 0.0065 - 0.0008 * i))
        else:
            th_ = 0.8 if PAUL else 1.0   # (his brows a little lighter than Val's)
            band([(0.012 * sx, 0.0205, 0.0042 * th_), (0.028 * sx, 0.0238, 0.0058 * th_), (0.045 * sx, 0.0246, 0.0056 * th_), (0.061 * sx, 0.0196, 0.0036 * th_)], HAIR, 0.0034 * th_,
                 moved=("Brows", lambda i: 0.0075 - 0.0012 * i))
    if VAL: band([(-0.041, -0.0668, 0.0026), (-0.031, -0.0608, 0.0052), (-0.016, -0.0562, 0.0066), (0.0, -0.0550, 0.0062),
                  (0.016, -0.0562, 0.0066), (0.031, -0.0608, 0.0052), (0.041, -0.0668, 0.0026)], HAIR, 0.0042)
    # Paul's moustache: thin, along the top lip, dark
    if PAUL: band([(-0.030, -0.0640, 0.0016), (-0.020, -0.0596, 0.0030), (-0.008, -0.0574, 0.0034), (0.0, -0.0570, 0.0032),
                   (0.008, -0.0574, 0.0034), (0.020, -0.0596, 0.0030), (0.030, -0.0640, 0.0016)], HAIR, 0.0028)
    # the mouth, one shape: the lower lip under the moustache, its top line turning up a little at the corners
    # (he is usually half smiling), full in the middle and thinning to nothing at each end
    f0 = len(XF.f); mw = 0.0240 if BRI else 0.0215; cols_ = []
    for i in range(9):
        u_ = -1 + 2 * i / 8
        x = u_ * mw
        zt = -0.0722 + (0.0048 if BRI else 0.0030) * u_ * u_
        zb = zt - 0.0010 - 0.0082 * max(0.0, 1 - u_ * u_) ** 0.75
        full = 0.0020 * (0.35 + 0.65 * max(0.0, 1 - u_ * u_))
        cols_.append([fadd(x, lerp(zt, zb, k_), l_, "lip2" if BRI else "lip") for k_, l_ in ((0, -SUNK), (0.22, full * 0.7), (0.55, full), (0.85, full * 0.6), (1, -SUNK))])
    for c0, c1 in zip(cols_, cols_[1:]):
        for k in range(4): XF.face(c0[k], c0[k + 1], c1[k + 1], c1[k])
    to_front(f0)
    # the patch under the lip: a small wedge, broad under the lip and running down to a point on the chin
    if VAL: band([(0.0, -0.0850, 0.0068), (0.0, -0.0895, 0.0056), (0.0, -0.0945, 0.0036), (0.0, -0.0990, 0.0014)], HAIR, 0.0018, tips=(0.3, 1.2))
    skin_bm.free()
    if VAL:
        # hair: dark and wavy, swept up and back off the forehead with a lifted front, over the tops of the ears,
        # curling behind them and down to the collar at the back
        import random
        rnd = random.Random(11)
        n = 16
        def hairline(y):   # the lowest the hair comes, round the head (y: -1 the front, +1 the back)
            return lerp(0.088, 0.004, sm(-0.8, -0.05, y)) if y <= 0 else lerp(0.004, -0.108, sm(0.05, 0.6, y))
        rows = []
        for i, (dz, r) in enumerate([(0.132, 0.035), (0.126, 0.078), (0.106, 0.107), (0.074, 0.118), (0.038, 0.118), (0.0, 0.115), (-0.052, 0.108), (-0.108, 0.096)]):
            idx = []
            for k in range(n):
                th = 2 * math.pi * k / n
                x, y = math.sin(th), -math.cos(th)
                front = max(0.0, -y)
                wave = (rnd.random() - 0.4) * 0.014
                rr = (r + wave) * (1.0 + 0.05 * max(0.0, y))
                z = dz + wave * 0.5
                hl = hairline(y)
                if z < hl: z = hl + 0.004 * i; rr = (0.108 - 0.011 * front ** 3 + 0.012 * max(0.0, y)) * (1.0 - 0.01 * i)   # tucked in at the hairline
                lift = front * (0.006 if i in (2, 3) else 0.003 if i == 1 else 0.0)                      # the front, lifted
                idx.append(G.add(HV(x * rr * 0.98, 0.012 + y * rr * (1.04 if y > 0 else 0.94) - lift * 0.7, z + lift), hd, swatch_uv(HAIR)))   # flatter over the forehead than round the back
            rows.append(idx)
        for a, b in zip(rows, rows[1:]): G.bridge(a, b)
        G.cap(rows[0][::-1], HV(0, 0.012, 0.137), hd, swatch_uv(HAIR))
        inner = [G.add(G.v[i] + (HC - G.v[i]) * 0.12, hd, swatch_uv(HAIR)) for i in rows[-1]]
        G.bridge(rows[-1], inner)
        # Over that cap, the hair itself, built the way stylised slicked-back hair is: layered blades. Each is a flat
        # wedge, wide at the root and tapering to a point, with a ridge down its middle; a front tier rises off the
        # hairline and sweeps back, the tiers behind overlap it like shingles, side blades sweep back over the ears,
        # and the back ends in points that flick out at the neck. A wave runs down each blade.
        XG = Cage("HairBlades", "Gear"); XTRA.append((XG, "HeroHead"))
        O = HV(0, 0.012, 0.02)
        def turned_out(f0, centre, inward=False):
            for i in range(f0, len(XG.f)):
                fc = XG.f[i]
                pts_ = [XG.v[j] for j in fc]
                c = sum(pts_, Vector()) / len(pts_)
                nrm = (pts_[1] - pts_[0]).cross(pts_[2] - pts_[0])
                if (nrm.dot(c - centre) < 0) != inward: XG.f[i] = tuple(reversed(fc))
        def blade(pts, width, thick, wave=0.006, phase=0.0, col=HAIR):
            P = [HV(*q) for q in pts]
            m = len(P); f0 = len(XG.f); rings_ = []
            taper = [0.5, 1.0, 0.85, 0.5, 0.2]
            for i, c in enumerate(P):
                t = (P[min(i + 1, m - 1)] - P[max(i - 1, 0)]).normalized()
                nr = (c - O); nr = (nr - t * nr.dot(t)).normalized()
                sd_ = t.cross(nr)
                u_ = i / (m - 1)
                c = c + nr * wave * math.sin(2 * math.pi * (u_ * 1.1 + phase)) * (0.3 + 0.7 * u_)
                if i == m - 1: rings_.append([XG.add(c + nr * thick * 0.3, hd, swatch_uv(col))]); break
                th_ = thick * (0.35 if i == 0 else 0.8 if i == 1 else 1.0)
                w_ = width * taper[min(i, 4)] if m <= 5 else width * lerp(1.0, 0.25, u_)
                rings_.append([XG.add(c + nr * th_, hd, swatch_uv(col)), XG.add(c + sd_ * w_, hd, swatch_uv(col)),
                               XG.add(c - nr * th_ * 0.5, hd, swatch_uv(col)), XG.add(c - sd_ * w_, hd, swatch_uv(col))])
            for a_, b_ in zip(rings_, rings_[1:]):
                if len(b_) == 1:
                    for k in range(4): XG.face(a_[k], a_[(k + 1) % 4], b_[0])
                else:
                    for k in range(4): XG.face(a_[k], a_[(k + 1) % 4], b_[(k + 1) % 4], b_[k])
            XG.face(*rings_[0][::-1])
            turned_out(f0, sum(P, Vector()) / m)
        rnd2 = random.Random(4)
        def j(v): return v + (rnd2.random() - 0.5) * 0.012
        # Where the blades go is worked out on the hair's own dome (the cap above), so from the front the outline is
        # a rounded mass like his, not a row of spikes: a point is a direction from the middle of the head, pushed
        # out to the cap and lifted a few millimetres. Every blade's root is sunk under the cap, so none stands off
        # the head with a gap under it.
        prof = [(90, 0.117), (72.6, 0.117), (53.7, 0.132), (38.8, 0.137), (24.6, 0.130), (8.7, 0.119), (-9.9, 0.117), (-33.7, 0.130), (-53, 0.160)]
        def rho(el):
            for (e0, r0), (e1, r1) in zip(prof, prof[1:]):
                if el >= e1: return lerp(r1, r0, min(1.0, (el - e1) / (e0 - e1)))
            return prof[-1][1]
        def on_hair(d, lift):
            d = d.normalized(); el = math.degrees(math.asin(max(-1.0, min(1.0, d.z))))
            q = d * (rho(el) + lift)
            return (q.x * 0.98, 0.012 + q.y * (1.04 * (1.0 + 0.05 * d.y) if d.y > 0 else 0.94), 0.020 + q.z)
        def over(a_, t, lift):    # on a line running front to back over the head: a_ how far to the side (-1..1), t how far round (0 front, 90 top, 180 back)
            a_ = max(-0.93, min(0.93, a_ + 0.07 * math.sin(math.radians(t))))    # all swept a little to his left
            c = math.sqrt(1 - a_ * a_); tr = math.radians(t)
            return on_hair(V(a_, -c * math.cos(tr), c * math.sin(tr)), lift)
        def around(sx, ps, el, lift):   # round the side of the head: ps degrees round from the front, el degrees up
            pr, er = math.radians(ps), math.radians(el)
            return on_hair(V(sx * math.sin(pr) * math.cos(er), -math.cos(pr) * math.cos(er), math.sin(er)), lift)
        def hairline_t(a_): return math.degrees(math.asin(min(1.0, math.sin(math.radians(31)) / math.sqrt(1 - a_ * a_))))
        def jl(v): return v + (rnd2.random() - 0.5) * 0.004
        for a_ in (-0.80, -0.56, -0.29, 0.0, 0.29, 0.56, 0.80):      # the front: up off the hairline, swept back over the top
            t0 = hairline_t(a_) + (3 if abs(a_) < 0.1 else 0)
            blade([over(a_, t0 + 3, -0.012), over(a_, t0 + 15, jl(0.003)), over(a_, t0 + 33, jl(0.007)), over(a_, t0 + 56, 0.005), over(a_, t0 + 80, 0.001)],
                  0.030, 0.008, wave=0.004, phase=rnd2.random())
        for a_ in (-0.62, -0.33, 0.0, 0.33, 0.62):              # the top
            blade([over(a_, 62, -0.006), over(a_, 84, jl(0.004)), over(a_, 108, jl(0.004)), over(a_, 130, 0.003), over(a_, 150, 0.0)], 0.032, 0.008, phase=rnd2.random())
        for a_ in (-0.80, -0.50, -0.17, 0.17, 0.50, 0.80):      # the crown, down the back of the head
            blade([over(a_, 118, -0.006), over(a_, 140, jl(0.004)), over(a_, 162, jl(0.004)), over(a_, 184, 0.003), over(a_, 202, 0.0)], 0.031, 0.008, phase=rnd2.random())
        for a_ in (-0.72, -0.38, 0.0, 0.38, 0.72):              # the back, curling out at the neck
            blade([over(a_, 168, -0.006), over(a_, 190, jl(0.003)), over(a_, 210, jl(0.004)), over(a_, 226, 0.007), over(a_, 238, 0.016)], 0.030, 0.008, phase=rnd2.random())
        for sx in (1, -1):                                       # the sides: back from the temple, over the ear, and down behind it
            blade([around(sx, 52, 27, -0.010), around(sx, 72, 26, 0.004), around(sx, 96, 22, 0.004), around(sx, 122, 14, 0.003), around(sx, 148, 6, 0.0)], 0.028, 0.008, phase=0.2)
            blade([around(sx, 78, 9, -0.010), around(sx, 96, 12, 0.003), around(sx, 116, 6, 0.003), around(sx, 138, -4, 0.002), around(sx, 158, -16, 0.0)], 0.026, 0.008, phase=0.6)
            blade([around(sx, 118, -4, -0.008), around(sx, 132, -18, 0.003), around(sx, 146, -32, 0.003), around(sx, 156, -44, 0.005), around(sx, 162, -52, 0.012)], 0.024, 0.008, wave=0.004, phase=0.9)
        # curls: his hair is curly, so small hooked locks lie over the smooth ones, along the fringe, over the top
        # and at the neck: each turns most of a circle on the hair, rising as it goes
        def curl(d0, size, lift0, turn=1.0, w=0.010):
            d0 = d0.normalized()
            e1 = d0.cross(V(0, 0, 1))
            if e1.length < 0.05: e1 = V(1, 0, 0)
            e1.normalize(); e2 = d0.cross(e1)
            pts_ = []
            for k in range(5):
                th = turn * (0.4 + 1.2 * k)
                rr = size * (1.0 - 0.13 * k)
                pts_.append(on_hair(d0 + (e1 * math.cos(th) + e2 * math.sin(th)) * rr, lift0 + 0.002 * k))
            blade(pts_, w, 0.007, wave=0.0)
        def dir_over(a_, t):
            c = math.sqrt(1 - a_ * a_); tr = math.radians(t)
            return V(a_, -c * math.cos(tr), c * math.sin(tr))
        for a_, sg in ((-0.66, 1), (-0.40, -1), (-0.12, 1), (0.16, -1), (0.44, 1), (0.68, -1)):          # the fringe
            curl(dir_over(a_, hairline_t(a_) + 9), 0.085, 0.004, sg)
        for a_, t, sg in ((-0.42, 70, 1), (0.05, 64, -1), (0.46, 74, 1), (-0.20, 98, -1), (0.28, 104, 1), (-0.50, 128, 1), (0.02, 134, -1), (0.52, 124, -1)):   # the top and crown
            curl(dir_over(a_, t), 0.10, 0.008, sg, 0.011)
        for sx in (1, -1):                                                                             # behind the ears and at the neck
            for ps, el, sg in ((128, 2, 1), (146, -22, -1), (160, -40, 1)):
                pr, er = math.radians(ps), math.radians(el)
                curl(V(sx * math.sin(pr) * math.cos(er), -math.cos(pr) * math.cos(er), math.sin(er)), 0.09, 0.006, sg * sx)
        # The fleece's collar, as in the photos: a stand collar left unsnapped. It stands round the back of the neck
        # and opens in a V at the throat, and its two front corners fold over outward, showing the dark lining.
        # Built as two skins a few millimetres apart: blue outside, dark inside.
        XJ = Cage("Collar", "Jacket"); XL = Cage("CollarLining", "Gear")
        XTRA.append((XJ, "HeroBody")); XTRA.append((XL, "HeroGear"))
        M = 20
        rows = [  # height (front, back), radius (side, front, back), where the open edge is (degrees off the front), weights
            ((1.408, 1.410), (0.122, 0.102, 0.110), 13, {"UpperChest": 0.75, "Neck": 0.25}),
            ((1.440, 1.448), (0.118, 0.106, 0.104), 20, {"UpperChest": 0.6, "Neck": 0.4}),
            ((1.468, 1.482), (0.123, 0.114, 0.104), 28, {"UpperChest": 0.5, "Neck": 0.5}),
            ((1.490, 1.512), (0.132, 0.124, 0.106), 36, {"UpperChest": 0.4, "Neck": 0.6})]
        grid = []
        for ri, ((zf, zb), (rx, rf, rb), edge, w_) in enumerate(rows):
            row = []
            for i in range(M):
                u_ = i / (M - 1)
                ph = math.radians(edge + (360 - 2 * edge) * (u_ - 0.1 * math.sin(2 * math.pi * u_)))
                cs, sn = math.cos(ph), math.sin(ph)
                ry = rf if cs > 0 else rb
                co = V(rx * sn, 0.02 - ry * cs, lerp(zf, zb, 0.5 - 0.5 * cs))
                if ri >= 2:
                    # the fold: near the open edge the top of the collar turns over, outward and down
                    fold = max(0.0, 1.0 - min(u_, 1 - u_) / 0.26) ** 1.3
                    out = V(sn, -cs, 0)
                    if ri == 2: co += out * 0.010 * fold
                    else:
                        below = grid[2][i]
                        co = (below + V(0, 0, 0.012)).lerp(co, 1 - fold) + out * 0.026 * fold + V(0, 0, -0.030) * fold
                row.append(co)
            grid.append(row)
        def gnorm(ri, i):   # which way the outside faces at a point of the grid
            a_ = grid[ri][min(i + 1, M - 1)] - grid[ri][max(i - 1, 0)]
            b_ = grid[min(ri + 1, len(grid) - 1)][i] - grid[max(ri - 1, 0)][i]
            return a_.cross(b_).normalized()
        outer = [[XJ.add(grid[ri][i], rows[ri][3]) for i in range(M)] for ri in range(len(grid))]
        inner = [[XL.add(grid[ri][i] - gnorm(ri, i) * 0.006, rows[ri][3], swatch_uv("snap")) for i in range(M)] for ri in range(len(grid))]
        rim = [[XL.add(grid[ri][i], rows[ri][3], swatch_uv("snap")) for i in range(M)] for ri in range(len(grid))]
        for ri in range(len(grid) - 1):
            for i in range(M - 1):
                XJ.face(outer[ri][i], outer[ri][i + 1], outer[ri + 1][i + 1], outer[ri + 1][i])
                XL.face(inner[ri][i + 1], inner[ri][i], inner[ri + 1][i], inner[ri + 1][i + 1])
            XL.face(rim[ri][0], rim[ri + 1][0], inner[ri + 1][0], inner[ri][0])                        # the two open edges
            XL.face(rim[ri + 1][M - 1], rim[ri][M - 1], inner[ri][M - 1], inner[ri + 1][M - 1])
        top = len(grid) - 1
        for i in range(M - 1): XL.face(rim[top][i + 1], rim[top][i], inner[top][i], inner[top][i + 1])  # the top edge
    # ---- caps, goggles and long hair (Breanna and Paul)
    def beanie(col, fz, sz, bz, rx, ryf, ryb, slouch=0.0, n=24):
        """A knit beanie pulled down over the head: a turned-up ribbed cuff (its edge fz, sz, bz high at the front, the
        sides and the back, head units), then the crown, level, a little slouched to the back. Returns the cuff's
        radii, for goggles to sit on it."""
        def edge(th):
            c = math.cos(th)
            return lerp(sz, fz, c) if c >= 0 else lerp(sz, bz, -c)
        crown = [(0.080, 0.985), (0.110, 0.93), (0.136, 0.80), (0.156, 0.60), (0.168, 0.34)]
        rows_ = []
        # the cuff, folded up and ribbed (every other column stands out), with a ledge where it ends
        for dz, k in ((0.0, 1.0), (0.018, 1.06), (0.038, 1.065), (0.042, 1.06), (0.046, 0.965), (0.054, 0.97)):
            rows_.append([(edge(2 * math.pi * i / n) + dz, k * (1.0 + (0.026 if dz < 0.043 and i % 2 else 0.0)), dz / 0.2) for i in range(n)])
        for j_, (zc, k) in enumerate(crown):
            rows_.append([(max(zc, edge(2 * math.pi * i / n) + 0.056 + 0.012 * j_), k, 0.3 + 0.7 * j_ / 4) for i in range(n)])
        rings_ = []
        for row in rows_:
            idx = []
            for i, (z, k, sl) in enumerate(row):
                th = 2 * math.pi * i / n
                x, y = math.sin(th), -math.cos(th)
                idx.append(G.add(HV(x * rx * k, 0.012 + y * (ryf if y < 0 else ryb) * k + slouch * sl * sl, z), hd, swatch_uv(col)))
            rings_.append(idx)
        for a_, b_ in zip(rings_, rings_[1:]): G.bridge(a_, b_)
        G.cap(rings_[-1], HV(0, 0.012 + slouch, crown[-1][0] + 0.008), hd, swatch_uv(col))
        inner = [G.add(G.v[i] + (HV(0, 0.012, 0) + V(0, 0, G.v[i].z - HV(0, 0, 0).z) - G.v[i]) * 0.10 + V(0, 0, 0.006), hd, swatch_uv(col)) for i in rings_[0]]
        G.bridge(inner, rings_[0])       # the edge turned under, against the head
        BEANIE.update(rx=rx * 1.03, ryf=ryf * 1.03, ryb=ryb * 1.03, crown=crown)
        return rx * 1.03, ryf * 1.03, ryb * 1.03
    BEANIE = {}
    def goggles(zc, rx, ryf, ryb, cols, a0=1.15, h=0.044, lift=0.010):
        # (worn over the eyes as modelled; in the camp the game blends in "GogglesUp", which pushes them up onto the
        # beanie so the face shows: see the shape keys further down)
        global GOGGLES
        GOGGLES = dict(cols=cols, zc=zc, HC=HC.copy(), HS=HS, beanie=BEANIE, rx=rx, ryf=ryf, ryb=ryb)
        """Goggles round the head at height zc: a frame (rx, ryf, ryb: the head's or the cap's radii there), a mirrored
        lens in front of it, a strap round the back."""
        fcol, lcol, scol = cols
        def at(a, extra): return HV(math.sin(a) * (rx + extra), 0.012 - math.cos(a) * ((ryf if math.cos(a) >= 0 else ryb) + extra), zc)
        m = 8
        def inward(q): d_ = HC - q; d_.z = 0.0; return d_.normalized()     # (level: the head's middle is lower than goggles on a cap)
        fr = [at(-a0 + 2 * a0 * i / m, lift) for i in range(m + 1)]
        loft(G, [(c, 0.010, h * 0.5 * (1 - 0.22 * (abs(i - m / 2) / (m / 2)) ** 2), hd) for i, c in enumerate(fr)], 6, fcol, 2.6, ref=inward(fr[m // 2]), twist=math.pi / 6)
        # (the lens a size smaller than the frame, so a black rim shows all round it)
        ln = [at(-a0 * 0.82 + 2 * a0 * 0.82 * i / m, lift + 0.013) for i in range(m + 1)]
        loft(G, [(c, 0.005, h * 0.34 * (1 - 0.22 * (abs(i - m / 2) / (m / 2)) ** 2), hd) for i, c in enumerate(ln)], 6, lcol, 2.8, ref=inward(ln[m // 2]), twist=math.pi / 6)
        st = [at(a, lift * 0.5) for a in [a0 + (2 * math.pi - 2 * a0) * i / 12 for i in range(13)]]
        loft(G, [(c, 0.004, 0.015, hd) for c in st], 4, scol, 2.0, ref=V(0, 0, 1), twist=math.pi / 4)
    def cap_hair(rows_spec, front_line, back_line, sq=0.93):
        """Hair close to the head (under a cap, or drawn back), down to its hairline."""
        n = 16; rows = []
        for i, (dz, r) in enumerate(rows_spec):
            idx = []
            for k in range(n):
                th = 2 * math.pi * k / n
                x, y = math.sin(th), -math.cos(th)
                front = max(0.0, -y)
                hl = lerp(front_line, -0.004, sm(-0.80, -0.05, y)) if y <= 0 else lerp(-0.004, back_line, sm(0.05, 0.55, y))
                z, rr = dz, r
                if z < hl: z = hl + 0.003 * i; rr = 0.098 - 0.012 * front ** 3 + 0.004 * max(0.0, y)
                idx.append(G.add(HV(x * rr * sq, 0.010 + y * rr * (1.08 if y > 0 else 0.95), z), hd, swatch_uv(HAIR)))
            rows.append(idx)
        for a_, b_ in zip(rows, rows[1:]): G.bridge(a_, b_)
        G.cap(rows[0][::-1], HV(0, 0.010, rows_spec[0][0] + 0.006), hd, swatch_uv(HAIR))
    if PAUL:
        # short dark hair, mostly under the beanie: it shows at the sides over the ears, in short sideburns and at the nape
        cap_hair([(0.110, 0.080), (0.074, 0.110), (0.038, 0.112), (0.0, 0.110), (-0.050, 0.102), (-0.094, 0.082)], 0.082, -0.090, sq=0.95)
        # the cream beanie, its cuff low on the forehead; his goggles pushed up on it (black frame, orange lens)
        crx, cry, crb = beanie("beanie", 0.046, 0.012, -0.044, 0.118, 0.106, 0.122, slouch=0.018)
        goggles(0.010, 0.104, 0.094, 0.120, ("frame", "lens", "strap"), a0=1.18, h=0.090, lift=0.012)
        # outfit pieces that go with the head: the turtleneck's roll collar (outfit one), the balaclava (outfit two)
        def tring(C_, zf, zb, rx_, ry_, cy_, w_, uvn, n_=12):
            return [C_.add(V(rx_ * math.sin(a), cy_ - ry_ * math.cos(a), lerp(zf, zb, 0.5 - 0.5 * math.cos(a))), w_, swatch_uv(uvn)) for a in [2 * math.pi * k / n_ for k in range(n_)]]
        if not PAUL_B:
            XK = Cage("Turtleneck", "Knit"); XK.recalc = True; XTRA.append((XK, "HeroBody"))
            nk_ = {"Neck": 0.7, "Head": 0.1, "UpperChest": 0.2}
            rs = [tring(XK, 1.405, 1.405, 0.074, 0.076, 0.018, {"UpperChest": 0.7, "Neck": 0.3}, "spare"),
                  tring(XK, 1.445, 1.455, 0.066, 0.068, 0.017, {"Neck": 1}, "spare"),
                  tring(XK, 1.470, 1.508, 0.068, 0.070, 0.014, nk_, "spare"),
                  tring(XK, 1.482, 1.522, 0.077, 0.079, 0.014, nk_, "spare"),
                  tring(XK, 1.462, 1.500, 0.079, 0.081, 0.015, nk_, "spare")]
            for a_, b_ in zip(rs, rs[1:]): XK.bridge(a_, b_)
        else:
            # the white balaclava, pulled up over the mouth and nose: laid round the neck and on the face, a centimetre
            # off the skin, and up under the beanie at the back
            XB = Cage("Balaclava", "Gear"); XB.recalc = True; XTRA.append((XB, "HeroGear"))
            nb = 16; rows_b = []
            for t in [0.0, 0.18, 0.36, 0.52, 0.66, 0.78, 0.88, 0.96, 1.0]:
                row = []
                for k in range(nb):
                    a = 2 * math.pi * k / nb; ca, sa = math.cos(a), math.sin(a)
                    ztop = lerp(-0.010, 0.020, 0.5 - 0.5 * ca)         # the top edge: over the nose in front, under the beanie behind
                    zl = lerp(-0.22, ztop, t)                            # head units, from the base of the neck up
                    zabs = HC.z + zl * HS
                    if zl < -0.135:
                        rr = (0.068 + 0.010 * (1 - t)) ; q = V(sa * rr, 0.015 - ca * rr, zabs)
                    else:
                        hit = skin_tree.ray_cast(HV(sa * 0.6, 0.012 - ca * 0.6, zl), V(-sa, ca, 0))[0]
                        q = (hit if hit is not None else HV(sa * 0.10, 0.012 - ca * 0.10, zl)) + V(sa, -ca, 0) * (0.009 + (0.010 if ca > 0.85 and -0.06 < zl < 0.0 else 0.0))
                    w_ = {"UpperChest": 0.4, "Neck": 0.6} if zabs < 1.46 else {"Neck": 0.5, "Head": 0.5} if zabs < 1.51 else hd
                    row.append(XB.add(q, w_, swatch_uv("balaclava")))
                    # pulled down: bunched round the neck below the chin, a roll at its top edge (lower in front)
                    zt_ = lerp(1.452, 1.490, 0.5 - 0.5 * ca)
                    rd = 0.074 + 0.012 * math.sin(math.pi * t) + 0.006 * t
                    GEAR_SHAPES.setdefault("MaskDown", []).append((q.copy(), V(sa * rd, 0.015 - ca * rd, lerp(1.418, zt_, t))))
                rows_b.append(row)
            for a_, b_ in zip(rows_b, rows_b[1:]): XB.bridge(a_, b_)
    if BRI:
        # her hair (after her new photo): long, dark brown and curly, falling from under a grey beanie past her shoulders
        # to her chest in front and down her back. Under the locks, a close cap and a mass behind; the locks themselves
        # are tight curls (each a thin tube wound round its line), hanging from the head's bone and lying on her shoulders
        # and back on the chest's, so they stay on her when she turns her head.
        cap_hair([(0.122, 0.030), (0.116, 0.068), (0.096, 0.093), (0.064, 0.104), (0.030, 0.106), (-0.010, 0.104), (-0.055, 0.096), (-0.092, 0.078)], 0.098, -0.096)
        crx, cry, crb = beanie("beanie2", 0.040, 0.020, -0.034, 0.118, 0.104, 0.124, slouch=0.022)
        # her goggles over her eyes: a white frame, a pink mirror lens, a pink strap round the beanie's edge
        goggles(0.010, 0.106, 0.091, 0.118, ("frame2", "lens2", "strap2"), a0=1.22, h=0.092, lift=0.011)
        def wz(z):
            if z > 1.56: return {"Head": 1.0}
            if z > 1.47: k = (z - 1.47) / 0.09; return {"Head": k, "Neck": 1 - k}
            if z > 1.39: k = (z - 1.39) / 0.08; return {"Neck": k, "UpperChest": 1 - k}
            if z > 1.25: return {"UpperChest": 1.0}
            return {"UpperChest": 0.6, "Chest": 0.4}
        def catmull(ctrl, m):
            out = []
            P_ = [ctrl[0]] + list(ctrl) + [ctrl[-1]]
            for i in range(m):
                u = i / (m - 1) * (len(ctrl) - 1); j = min(int(u), len(ctrl) - 2); t = u - j
                p0, p1, p2, p3 = P_[j], P_[j + 1], P_[j + 2], P_[j + 3]
                out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
            return out
        # the mass behind: a broad soft sheet from under the beanie down her back, and one down each side of the face
        loft(G, [(V(0, 0.100, 1.585), 0.020, 0.100, wz(1.585)), (V(0, 0.132, 1.505), 0.024, 0.130, wz(1.505)), (V(0, 0.168, 1.415), 0.024, 0.150, wz(1.415)),
                 (V(0, 0.182, 1.330), 0.022, 0.150, wz(1.33)), (V(0, 0.186, 1.255), 0.016, 0.118, wz(1.255))], 10, HAIR, 2.2, ref=V(0, 1, 0))
        for sx in (1, -1):
            loft(G, [(V(sx * 0.104, 0.010, 1.630), 0.030, 0.018, wz(1.63)), (V(sx * 0.122, 0.018, 1.545), 0.036, 0.022, wz(1.545)),
                     (V(sx * 0.132, 0.030, 1.470), 0.034, 0.022, wz(1.47)), (V(sx * 0.150, 0.040, 1.420), 0.024, 0.016, wz(1.42))], 8, HAIR, 2.2, ref=V(0, 1, 0))
        XC = Cage("Curls", "Gear"); XC.recalc = True; XTRA.append((XC, "HeroHead"))
        import random
        rc = random.Random(7)
        def lock(ctrl, r=0.011, amp=0.009, turns=3.2, n=5, m=18):
            r, amp = r * 0.85, amp * 1.7     # (springy coils, not twisted rope)
            P_ = catmull([V(*q) for q in ctrl], m)
            rings_ = []; tip = None
            for i in range(m):
                t = (P_[min(i + 1, m - 1)] - P_[max(i - 1, 0)]).normalized()
                a1, a2 = frame_of(t, V(0, 1, 0) if abs(t.y) < 0.9 else V(1, 0, 0))
                u_ = i / (m - 1); ang = 2 * math.pi * (turns * u_ + rc.random() * 0.15)
                c = P_[i] + (a1 * math.cos(ang) + a2 * math.sin(ang)) * amp * (0.35 + 0.65 * u_)
                if i == m - 1: tip = XC.add(c, wz(c.z), swatch_uv(HAIR)); break
                rr = r * (1.0 - 0.5 * u_ * u_)
                rings_.append([XC.add(c + (a1 * math.cos(2 * math.pi * k / n) + a2 * math.sin(2 * math.pi * k / n)) * rr, wz(c.z), swatch_uv(HAIR)) for k in range(n)])
            for a_, b_ in zip(rings_, rings_[1:]):
                for k in range(n): XC.face(a_[k], a_[(k + 1) % n], b_[(k + 1) % n], b_[k])
            for k in range(n): XC.face(rings_[-1][k], rings_[-1][(k + 1) % n], tip)
            XC.face(*rings_[0][::-1])
        for sx in (1, -1):
            # in front of the shoulders, framing the face, down to her chest
            for k, (x0, y0, x1, y1, zend) in enumerate([(0.098, -0.035, 0.122, -0.168, 1.255), (0.106, -0.012, 0.150, -0.162, 1.235), (0.112, 0.010, 0.176, -0.150, 1.27)]):
                lock([(sx * x0, y0, 1.628), (sx * (x0 + 0.020), y0 - 0.006, 1.555), (sx * (x0 + 0.030), y0 - 0.020 - 0.01 * k, 1.478), (sx * (x1 - 0.010), y1 * 0.55, 1.415),
                      (sx * x1, y1 * 0.95, 1.335), (sx * (x1 - 0.004), y1, zend)], r=0.016, amp=0.012, turns=3.4 + 0.3 * k)
            # over the tops of the shoulders, to the back
            for k, ps in enumerate((112, 128, 144)):
                a = math.radians(ps); xs, yc = math.sin(a), -math.cos(a)
                lock([(sx * xs * 0.118, 0.012 + yc * 0.120, 1.600), (sx * xs * 0.135, 0.020 + yc * 0.135, 1.520), (sx * xs * 0.160, 0.050 + yc * 0.130, 1.440),
                      (sx * xs * 0.170, 0.100 + yc * 0.090, 1.380), (sx * xs * 0.160, 0.175, 1.300), (sx * xs * 0.150, 0.190, 1.245)], r=0.016, amp=0.012, turns=3.0 + 0.4 * k)
        for k, xb in enumerate((-0.095, -0.050, -0.010, 0.030, 0.070, 0.105)):
            # down her back
            lock([(xb * 0.9, 0.118, 1.590), (xb, 0.150, 1.515), (xb * 1.05, 0.180, 1.430), (xb * 1.1, 0.196, 1.345), (xb * 1.05, 0.200, 1.265), (xb, 0.198, 1.215)],
                 r=0.017, amp=0.013, turns=3.6 + 0.25 * (k % 3))
        for k, xb in enumerate((-0.072, -0.030, 0.010, 0.050, 0.088)):
            # a second layer over them, a little shorter: the curls are thick
            lock([(xb * 0.9, 0.124, 1.575), (xb, 0.160, 1.505), (xb * 1.05, 0.192, 1.425), (xb * 1.1, 0.206, 1.345), (xb, 0.206, 1.280)],
                 r=0.016, amp=0.012, turns=3.2 + 0.3 * (k % 2), m=12)
    for s in (1, -1):
        sd = SIDE[s]
        sh, u, el, f, wr, kn = arm_points(s)
        G.cur = "hands"; bare_hand(G, s, SKIN); G.cur = "gear"
        # white low-top sneakers: a chunky sole, a grey panel on the side and the heel
        hip, knee, ankle, ball, toe = leg_points(s)
        x = ankle.x
        ft, ts = sd + "Foot", sd + "Toes"
        def fw(y):
            k = sm(-0.07, -0.13, y)
            return {ft: 1 - k, ts: k} if k > 0 else {ft: 1}
        foot = []
        for y, w_, h0, h1 in [(0.092, 0.046, 0.030, 0.105), (0.065, 0.062, 0.028, 0.128), (0.01, 0.066, 0.028, 0.125),
                              (-0.06, 0.068, 0.028, 0.108), (-0.13, 0.068, 0.030, 0.088), (-0.19, 0.060, 0.030, 0.078),
                              (-0.228, 0.040, 0.032, 0.066), (-0.242, 0.014, 0.038, 0.054)]:
            zc, hh = (h0 + h1) / 2, (h1 - h0) / 2
            foot.append((V(x + 0.004 * s, y, zc), hh, w_, fw(y)))
        if PAUL:
            # blue ski boots: a stiff shell to mid-shin round the trouser cuff, a big square-toed foot on a thick sole, grey
            # buckles over the instep and up the shin
            loft(G, [(V(x, 0.020, 0.060), 0.082, 0.074, {ft: 1}), (V(x, 0.022, 0.170), 0.078, 0.070, {ft: 0.7, sd + "LowerLeg": 0.3}),
                     (V(x, 0.024, 0.260), 0.074, 0.068, {ft: 0.25, sd + "LowerLeg": 0.75}), (V(x, 0.026, 0.305), 0.076, 0.070, {sd + "LowerLeg": 1})], 10, "skiboot", 2.4, ref=FWD)
            sfoot = []
            for y, w_, h0, h1 in [(0.095, 0.054, 0.030, 0.17), (0.06, 0.072, 0.028, 0.18), (0.0, 0.078, 0.028, 0.17), (-0.07, 0.078, 0.028, 0.135),
                                  (-0.14, 0.074, 0.030, 0.105), (-0.198, 0.064, 0.032, 0.090), (-0.236, 0.044, 0.036, 0.078), (-0.252, 0.016, 0.044, 0.066)]:
                sfoot.append((V(x + 0.004 * s, y, (h0 + h1) / 2), (h1 - h0) / 2, w_, fw(y)))
            loft(G, sfoot, 10, "skiboot", 2.8, ref=V(0, 0, 1))
            loft(G, [(V(x + 0.004 * s, y, 0.022), 0.022, w_, fw(y)) for y, w_ in
                     [(0.106, 0.054), (0.085, 0.078), (0.0, 0.084), (-0.10, 0.082), (-0.19, 0.074), (-0.242, 0.052), (-0.262, 0.020)]], 8, "sole", 3.4, ref=V(0, 0, 1))
            for zb, yb, rb in ((0.205, 0.022, 0.082), (0.268, 0.024, 0.078)):     # up the shin
                loft(G, [(V(x + math.sin(a) * rb, yb - math.cos(a) * rb, zb), 0.006, 0.011, {sd + "LowerLeg": 0.7, ft: 0.3}) for a in [-1.3 + 2.6 * k / 6 for k in range(7)]],
                     4, "buckle", 2.0, ref=V(0, 0, 1), twist=math.pi / 4)
            for yb in (-0.02, -0.085):                                          # over the instep
                zt = 0.17 if yb > -0.05 else 0.142
                loft(G, [(V(x + 0.004 * s + math.sin(a) * 0.080, yb, 0.09 + math.cos(a) * (zt - 0.09 + 0.006)), 0.006, 0.011, fw(yb)) for a in [-1.35 + 2.7 * k / 6 for k in range(7)]],
                     4, "buckle", 2.0, ref=V(0, 1, 0), twist=math.pi / 4)
        elif BRI:
            # tan slipper boots on a thick sole
            loft(G, [(c_ + V(0, 0, 0.022), r1_ + 0.010, r2_ + 0.004, w_) for c_, r1_, r2_, w_ in foot], 10, "slipper", 2.6, ref=V(0, 0, 1))
            loft(G, [(V(x + 0.004 * s, y, 0.027), 0.028, w_, fw(y)) for y, w_ in
                     [(0.104, 0.050), (0.085, 0.074), (0.0, 0.080), (-0.10, 0.082), (-0.19, 0.072), (-0.240, 0.050), (-0.258, 0.018)]], 8, "slippersole", 3.0, ref=V(0, 0, 1))
        else:
            loft(G, foot, 10, "sneaker", 2.6, ref=V(0, 0, 1))
            sole = [(V(x + 0.004 * s, y, 0.018), 0.019, w_, fw(y)) for y, w_ in
                    [(0.102, 0.046), (0.085, 0.07), (0.0, 0.076), (-0.10, 0.078), (-0.19, 0.068), (-0.238, 0.046), (-0.256, 0.016)]]
            loft(G, sole, 8, "sneaker", 3.0, ref=V(0, 0, 1))
            # the grey side panels and the heel tab; the tongue
            for sx2 in (1, -1):
                loft(G, [(V(x + 0.004 * s + sx2 * 0.068, 0.02, 0.070), 0.012, 0.004, fw(0.02)), (V(x + 0.004 * s + sx2 * 0.070, -0.06, 0.062), 0.016, 0.004, fw(-0.06)),
                         (V(x + 0.004 * s + sx2 * 0.068, -0.12, 0.054), 0.008, 0.004, fw(-0.12))], 4, "sneakergrey", 2.0, ref=V(0, 0, 1), twist=math.pi / 4)
            loft(G, [(V(x + 0.004 * s, 0.094, 0.055), 0.006, 0.022, fw(0.09)), (V(x + 0.004 * s, 0.094, 0.10), 0.006, 0.02, fw(0.09))], 4, "sneakergrey", 2.0, ref=V(0, 1, 0), twist=math.pi / 4)
            loft(G, [(V(x + 0.004 * s, 0.0, 0.132), 0.010, 0.034, fw(0.0)), (V(x + 0.004 * s, -0.07, 0.112), 0.010, 0.036, fw(-0.07))], 6, "sneaker", 2.4, ref=V(0, 0, 1))
    CAGES.extend(split_parts(G))

def make_gear():
    if BUDDY: return make_gear_buddy()
    G = Cage("HeroGear", "Gear"); G.cur = "head"
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
    for s in (1, -1): G.cur = "hands"; bare_hand(G, s, "skin")     # (hidden inside the gloves; they show in pyjamas)
    G.cur = "gear"
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
    CAGES.extend(split_parts(G))

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

make_jacket()
if not BUDDY: make_hood()
if PAUL: make_hood("Hoodie" if PAUL_B else "Jacket")   # (his puffer's hood down behind the collar; the hoodie's, under the vest)
make_pants(); make_gear(); make_board()

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
def plaid_print(path, n=256):
    """Breanna's pyjama bottoms: a red tartan, white bands and fine black lines crossing both ways."""
    red, white, black = (0.62, 0.06, 0.11), (0.90, 0.88, 0.85), (0.05, 0.04, 0.05)
    def thread(t):
        t = (t * 4) % 1.0
        return white if 0.50 <= t < 0.60 or 0.66 <= t < 0.72 else black if 0.76 <= t < 0.80 or 0.20 <= t < 0.23 else red
    rows = []
    for r in range(n):
        a_ = thread((r + 0.5) / n); row = []
        for c in range(n):
            b_ = thread((c + 0.5) / n)
            row.append((srgb((a_[0] + b_[0]) / 2), srgb((a_[1] + b_[1]) / 2), srgb((a_[2] + b_[2]) / 2), 1.0))
        rows.append(row)
    write_png(path, n, n, rows)

def face_paint(path, n=256, her=False, base=None, beard=0.34):
    """Val's face, painted (the picture is laid on his head from the front, see FACE_UV): warm skin, colour in the
    cheeks and the tip of the nose, the shadow of a shaved beard over the jaw and top lip, the hollows round the
    eyes with a crease above each lid, the top lip under the moustache, and the hair's shadow at the hairline.
    The eyes, brows, moustache and lower lip are modelled; this is what lies under and between them."""
    W, H, Z0 = FACE_UV
    beard_k = beard
    def g(dx, dz, rx, rz): return math.exp(-((dx / rx) ** 2 + (dz / rz) ** 2))
    def st(a, b, x):
        t = max(0.0, min(1.0, (x - a) / (b - a)))
        return t * t * (3 - 2 * t)
    rows = []
    for r in range(n):
        z = (1 - (r + 0.5) / n) * H - Z0
        row = []
        for c in range(n):
            x = ((c + 0.5) / n - 0.5) * W; ax = abs(x)
            col = list(base) if base else [0.82, 0.62, 0.52] if her else [0.83, 0.57, 0.45]
            def mix(to, k):
                k = max(0.0, min(1.0, k))
                for i in range(3): col[i] += (to[i] - col[i]) * k
            mix((0.90, 0.66, 0.54), 0.30 * g(x, z - 0.055, 0.075, 0.030))                # the forehead, lighter
            mix((0.86, 0.40, 0.34), (0.26 if her else 0.46) * g(ax - (0.050 if her else 0.058), z + 0.030, 0.030, 0.024))       # cheeks
            mix((0.86, 0.44, 0.37), 0.30 * g(x, z + 0.024, 0.018, 0.016))                # the tip of the nose
            mix((0.64, 0.41, 0.35), 0.40 * g(ax - 0.040, z - 0.006, 0.026, 0.015))       # round the eyes
            mix((0.46, 0.28, 0.24), 0.50 * g(ax - 0.040, z - 0.0150, 0.019, 0.0020))     # the crease above each lid
            mix((0.60, 0.39, 0.35), 0.30 * g(ax - 0.040, z + 0.0080, 0.016, 0.0030))     # under the eyes
            beard = st(-0.040, -0.078, z) * (1 - st(0.070, 0.102, ax))
            beard = max(beard, 0.9 * g(x, z + 0.058, 0.040, 0.010))
            if not her: mix((0.50, 0.39, 0.35), beard_k * beard)                          # the beard's shadow
            lipz = -0.0722 + 0.0030 * (x / 0.0215) ** 2
            if ax < 0.0215:                                                              # the top lip
                mix((0.68, 0.30, 0.31) if her else (0.60, 0.30, 0.28), 0.9 * st(lipz + (0.0075 if her else 0.0058), lipz + 0.0036, z) * st(lipz - 0.012, lipz - 0.009, z) * (1 - (ax / 0.0215) ** 2) ** 0.5)
            mix((0.58, 0.36, 0.31), 0.35 * g(x, z + 0.0840, 0.018, 0.0032))              # the shadow under the bottom lip
            mix((0.66, 0.43, 0.35), 0.5 * st(-0.105, -0.135, z))                         # under the jaw
            hz = (0.102 if her else 0.090) - 0.070 * (ax / 0.1) ** 2
            mix((0.20, 0.15, 0.12), 0.75 * st(hz - 0.018, hz + 0.004, z))                # the hair's shadow at the hairline
            row.append((srgb(col[0]), srgb(col[1]), srgb(col[2]), 1.0))
        rows.append(row)
    write_png(path, n, n, rows)

def bear_print(path, n=512):
    """The pyjama print, drawn after the real fleece: fat cream polar bears sat at all angles on near-black navy,
    each in a red knit hat with a pompom and a long red scarf flying behind, inked in dark outline, with a happy
    face; six-armed snowflakes and snow between. Tiles in both directions."""
    import random
    rnd = random.Random(5)
    navy, cream, red, ink = (0.045, 0.045, 0.10), (0.93, 0.90, 0.83), (0.83, 0.13, 0.10), (0.035, 0.035, 0.06)
    px = [[navy] * n for _ in range(n)]
    def blob(cx, cy, rx, ry, rot, col):
        c, s_ = math.cos(rot), math.sin(rot)
        R = int(max(rx, ry)) + 2
        for dy in range(-R, R + 1):
            for dx in range(-R, R + 1):
                u, v = dx * c + dy * s_, -dx * s_ + dy * c
                if (u / rx) ** 2 + (v / ry) ** 2 <= 1.0: px[int(cy + dy) % n][int(cx + dx) % n] = col
    for _ in range(70): blob(rnd.uniform(0, n), rnd.uniform(0, n), rnd.choice((1.4, 1.8, 2.4)), rnd.choice((1.4, 1.8, 2.4)), 0, cream)
    for _ in range(9):   # snowflakes: three strokes crossed, a dot at each tip
        cx, cy, a0 = rnd.uniform(0, n), rnd.uniform(0, n), rnd.uniform(0, 1)
        for k in range(3):
            a = a0 + k * math.pi / 3
            blob(cx, cy, 10, 1.4, a, cream)
            for sg in (1, -1): blob(cx + sg * 7 * math.cos(a) , cy + sg * 7 * math.sin(a), 3.5, 1.1, a + 0.9, cream)
    # bear: parts in its own frame (x right, y down, about 150 tall), then placed turned, flipped and scaled
    def bear(cx, cy, a, flip, k=0.56):
        c, s_ = math.cos(a), math.sin(a)
        def part(x, y, rx, ry, rot, col, grow=0.0):
            x *= flip
            blob(cx + (x * c - y * s_) * k, cy + (x * s_ + y * c) * k, (rx + grow) * k, (ry + grow) * k, a + rot * flip, col)
        fur = [(0, 6, 50, 60, 0), (14, -64, 36, 31, 0), (52, -14, 22, 13, -0.5), (-20, 62, 18, 12, 0), (28, 64, 18, 12, 0), (44, -86, 9, 9, 0)]
        for x, y, rx, ry, r in fur: part(x, y, rx, ry, r, ink, 3.0)          # the inked outline
        for x, y, rx, ry, r in fur: part(x, y, rx, ry, r, cream)
        part(-6, 30, 16, 1.6, 1.1, ink); part(18, 34, 14, 1.6, 1.9, ink)       # the lines of the haunches
        part(62, -20, 4, 4, 0, ink); part(56, -27, 2, 2, 0, ink); part(66, -27, 2, 2, 0, ink)   # a paw's pads
        # the scarf: round the neck, two tails flying back, with fringes
        for x, y, rx, ry, r in [(-62, -52, 44, 11, 0.12), (-58, -30, 42, 11, -0.16), (8, -38, 40, 10, 0.3)]: part(x, y, rx, ry, r, ink, 2.5)
        for x, y, rx, ry, r in [(-62, -52, 44, 11, 0.12), (-58, -30, 42, 11, -0.16), (8, -38, 40, 10, 0.3)]: part(x, y, rx, ry, r, red)
        for yy, r in ((-58, 0.12), (-52, 0.12), (-46, 0.12), (-36, -0.16), (-30, -0.16), (-24, -0.16)): part(-104, yy + (4 if r > 0 else -6), 5, 1.4, r, ink)
        # the hat: red, a cream brim and a pompom
        part(12, -92, 32, 21, -0.1, ink, 2.5); part(12, -92, 32, 21, -0.1, red)
        part(14, -78, 36, 8, -0.08, ink, 2.0); part(14, -78, 36, 8, -0.08, cream)
        part(-22, -104, 10, 10, 0, ink, 2.0); part(-22, -104, 10, 10, 0, cream)
        # a happy face: closed eyes, a nose, a smile
        part(8, -66, 6, 1.8, -0.3, ink); part(34, -64, 6, 1.8, 0.3, ink)
        part(22, -56, 6, 4.5, 0, ink)
        part(17, -47, 5, 1.6, 0.5, ink); part(27, -47, 5, 1.6, -0.5, ink)
    # eight to a tile, about 7 cm each on the leg (the size and scatter of the real cloth), at all angles
    for cx, cy, a, fl in [(84, 96, 0.15, 1), (300, 64, 1.45, -1), (446, 190, -0.35, -1), (196, 236, 2.85, 1),
                          (64, 344, -1.2, 1), (338, 330, 0.6, -1), (222, 456, -2.4, 1), (454, 440, 3.6, -1)]:
        bear(cx, cy, a, fl)
    write_png(path, n, n, [[(srgb(c[0]), srgb(c[1]), srgb(c[2]), 1.0) for c in row] for row in reversed(px)])

if BUDDY:
    WHO = "Breanna" if BRI else "Paul" if PAUL else "Buddy"     # (whose face and print pictures: one each, whatever they wear)
    RDIR = os.path.join(PROJECT, "Assets/Summit/Resources/Rider") if PROJECT else (bpy.app.tempdir or "/tmp")
    if VAL or BRI_PJ:
        PRINT_PATH = os.path.join(RDIR, "%sPants.png" % WHO)
        (plaid_print if BRI else bear_print)(PRINT_PATH)
        print_img = bpy.data.images.load(PRINT_PATH)
        m_ = material("Pants", (1, 1, 1), 0.15, print_img); m_.name = "PantsPrint"
        for nd in m_.node_tree.nodes:
            if nd.type == 'TEX_IMAGE': nd.interpolation = 'Linear'
    else:
        # plain trousers (the game's snowy cloth, dyed by the closet's PANTS): Paul's red ski trousers or charcoal, her black
        material("Pants", (0.60, 0.06, 0.06) if PAUL and not PAUL_B else (0.09, 0.09, 0.10) if PAUL_B else (0.025, 0.025, 0.03), 0.2)
    if BRI: material("Jacket", (0.93, 0.90, 0.82), 0.12).name = "FleeceWhite"   # her fleece is cream all over
    elif PAUL:
        material("Jacket", (0.03, 0.03, 0.035), 0.4)                           # the black puffer (or vest)
        material("Knit", (0.92, 0.91, 0.88), 0.05)                             # the white turtleneck
        material("Hoodie", (0.50, 0.51, 0.53), 0.05)                           # the grey hoodie
    else:
        material("Jacket", (0.09, 0.25, 0.72), 0.12).name = "FleeceBlue"      # royal blue over cream, as the real fleece
        MATS["FleeceWhite"] = material("FleeceWhite", (0.93, 0.90, 0.82), 0.12)
    FACE_PATH = os.path.join(RDIR, "%sFace.png" % WHO)
    face_paint(FACE_PATH, her=BRI, base=(0.74, 0.53, 0.40) if PAUL else None, beard=0.24 if PAUL else 0.34)
    face_img = bpy.data.images.load(FACE_PATH); face_img.alpha_mode = 'NONE'
    m_ = material("FaceSkin", (1, 1, 1), 0.3, face_img)
    for nd in m_.node_tree.nodes:
        if nd.type == 'TEX_IMAGE': nd.interpolation = 'Linear'; nd.extension = 'EXTEND'
else:
    material("Jacket", (0.93, 0.31, 0.14), 0.35)
    material("Pants", (0.17, 0.19, 0.24), 0.2)
material("Gear", (1, 1, 1), 0.35, pal_img)
material("Board", (0.12, 0.45, 0.95), 0.5)

SUBDIV = {"HeroBody": 1, "HeroHood": 1, "HeroLegs": 1, "HeroGear": 1, "HeroHead": 1, "HeroHands": 1, "Snowboard": 0, "Bindings": 0}
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

def raised(C, grid, lift_mid, lift_edge, uv="skin"):
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
by, PK = 0.17, {"Backpack": 1.0}
if BRI or PAUL:
    fwd_ray = lambda u, v: V(0, 1, 0)
    def line_on(rays, wd, lift, col):
        hits = [JS.cast(o_, d_, lift) for o_, d_ in rays]
        if all(h_[0] is not None for h_ in hits): strip(GD, hits, wd, 0.003, col)
        else: print("HERO detail line missed the cloth")
    def round_on(z, a0, a1, n, wd, lift, col):   # round the body at a height, between two angles (0 = the front)
        line_on([(V(0.7 * math.sin(a), 0.02 - 0.7 * math.cos(a), z), V(-math.sin(a), math.cos(a), 0)) for a in lin(math.radians(a0), math.radians(a1), n)], wd, lift, col)
    if BRI and not BRI_PJ:
        # the half zip: down from the collar to the top of the chest, and its pull
        line_on([(V(0.0, -0.7, z), V(0, 1, 0)) for z in lin(1.488, 1.300, 6)], 0.007, 0.004, "sneakergrey")
        q, nq = JS.cast(V(0.0, -0.7, 1.305), V(0, 1, 0), 0.008)
        if q is not None: loft(GD, [(q, 0.006, 0.004, {}), (q + V(0, 0, -0.03), 0.006, 0.004, {})], 4, "zip", 2.0, ref=V(0, 1, 0))
    if PAUL:
        # the puffer's quilting: stitched lines round the body between the baffles, and the front zip
        for z in ((0.97, 1.07, 1.17, 1.27) if PAUL_B else (0.95, 1.04, 1.13, 1.22, 1.31)):
            round_on(z, -62, 62, 9, 0.005, 0.002, "seam"); round_on(z, 118, 242, 9, 0.005, 0.002, "seam")
        line_on([(V(0.0, -0.7, z), V(0, 1, 0)) for z in lin(1.47, 0.86, 10)], 0.010, 0.004, "seam")
        if PAUL_B:
            # the hoodie's drawstrings, hanging out over the vest
            for sx_ in (1, -1):
                line_on([(V(sx_ * (0.032 + 0.004 * k), -0.7, z), V(0, 1, 0)) for k, z in enumerate(lin(1.45, 1.24, 5))], 0.006, 0.008, "sneakergrey")
        else:
            # the puffer's sleeves quilted too
            for s_ in (1, -1):
                sh_, u_, el_, f_, wr_, kn_ = arm_points(s_)
                for c_, ax_ in ((sh_ + u_ * 0.15, u_), (el_ + f_ * 0.07, f_), (el_ + f_ * 0.17, f_)):
                    a1_, a2_ = frame_of(ax_)
                    line_on([(c_ + (a1_ * math.cos(a) + a2_ * math.sin(a)) * 0.3, -(a1_ * math.cos(a) + a2_ * math.sin(a))) for a in lin(0, 2 * math.pi, 11)], 0.005, 0.002, "seam")
elif VAL:
    fwd_ray = lambda u, v: V(0, 1, 0)
    # the fleece: a kangaroo pocket low on the front, a snap placket from the collar to the colour seam, a small
    # stitched mark on the chest
    g = grid_on(JS, lambda u, v: V(u * (1.0 - 0.25 * (v - 0.93) / 0.16), -0.7, v), fwd_ray, lin(-0.15, 0.15, 6), lin(1.09, 0.93, 4))
    if g: raised(JD, g, 0.016, 0.006)
    g = grid_on(JS, lambda u, v: V(u, -0.7, v), fwd_ray, lin(-0.022, 0.022, 3), lin(1.366, 1.212, 5))
    if g: raised(JD, g, 0.007, 0.004)
    def onto(pts, lift): return [JS.cast(V(x, -0.7, z), V(0, 1, 0), lift) for x, z in pts]
    def add_gear(L):
        o = len(GD.v); GD.v += L.v; GD.w += L.w; GD.uv += L.uv; GD.f += [tuple(i + o for i in f_) for f_ in L.f]
    for z in (1.33, 1.255):   # the snaps (the top one is undone, inside the open collar)
        q, nq = JS.cast(V(0.0, -0.7, z), V(0, 1, 0), 0.011)
        if q is not None: loft(GD, [(q, 0.008, 0.008, {}), (q + nq * 0.004, 0.007, 0.007, {})], 6, "snap", 2.0, ref=V(0, 0, 1))
    # stitching in darker thread: above the pocket and down its slanted openings, along the seam where the blue
    # meets the cream, round the hem, and at each cuff
    def seam_line(rays, lift=0.0025):
        hits = [JS.cast(o_, d_, lift) for o_, d_ in rays]
        if all(h_[0] is not None for h_ in hits): strip(GD, hits, 0.0036, 0.003, "stitch")
        else: print("HERO seam missed the cloth")
    seam_line([(V(x, -0.7, 1.100), V(0, 1, 0)) for x in lin(-0.118, 0.118, 7)])
    for sx_ in (1, -1): seam_line([(V(sx_ * lerp(0.121, 0.160, k), -0.7, lerp(1.094, 0.925, k)), V(0, 1, 0)) for k in lin(0, 1, 5)])
    def ring_line(z, a0, a1, n):   # round the body at a height, between two angles (0 = the front)
        seam_line([(V(0.7 * math.sin(a), 0.02 - 0.7 * math.cos(a), z), V(-math.sin(a), math.cos(a), 0)) for a in lin(math.radians(a0), math.radians(a1), n)])
    ring_line(1.203, -55, 55, 9); ring_line(1.203, 125, 235, 9); ring_line(0.890, 0, 360, 25)
    for s_ in (1, -1):
        sh_, u_, el_, f_, wr_, kn_ = arm_points(s_)
        ax_ = (el_ - wr_).normalized(); c_ = wr_ + ax_ * 0.05
        a1_, a2_ = frame_of(ax_)
        seam_line([(c_ + (a1_ * math.cos(a) + a2_ * math.sin(a)) * 0.3, -(a1_ * math.cos(a) + a2_ * math.sin(a))) for a in lin(0, 2 * math.pi, 11)])
    # the embroidery on the chest, as on his real fleece: two peaks in bright thread, NORTHWINDS in dark thread,
    # an underline, and BREWERY LIMITED small beneath. The letters are real type turned to mesh and laid onto
    # the cloth point by point, so they follow the chest.
    def stitched(text, cx, zb, size, col, squeeze=0.82, slant=0.22, bold=0.00035, deep=0.0009):
        cu = bpy.data.curves.new("Stitch", 'FONT')
        cu.body = text; cu.size = size; cu.align_x = 'CENTER'; cu.shear = slant
        cu.offset = bold; cu.extrude = deep; cu.resolution_u = 1; cu.fill_mode = 'BOTH'; cu.space_character = 0.92
        ob_ = bpy.data.objects.new("Stitch", cu); scene.collection.objects.link(ob_)
        bpy.context.view_layer.update()
        me_ = bpy.data.meshes.new_from_object(ob_.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        base = len(GD.v); ok = True
        for v in me_.vertices:
            q, nq = JS.cast(V(cx + v.co.x * squeeze, -0.7, zb + v.co.y), V(0, 1, 0), 0.0035 + (v.co.z + deep))
            if q is None: ok = False; break
            GD.add(q, {}, swatch_uv(col))
        if ok:
            for pg in me_.polygons: GD.face(*[base + i for i in pg.vertices])
        n_ = sum(len(pg.vertices) - 2 for pg in me_.polygons)
        bpy.data.objects.remove(ob_); bpy.data.meshes.remove(me_); bpy.data.curves.remove(cu)
        return n_
    for pts, wd, col in [([(0.074, 1.304), (0.102, 1.326), (0.130, 1.304)], 0.004, "logocyan"),
                         ([(0.060, 1.301), (0.088, 1.313), (0.112, 1.306), (0.144, 1.300)], 0.003, "logocyan"),
                         ([(0.064, 1.2815), (0.140, 1.2815)], 0.0026, "logocyan")]:
        q = onto(pts, 0.004)
        if all(t_[0] is not None for t_ in q): strip(GD, q, wd, 0.003, col)
    nt_ = stitched("NORTHWINDS", 0.101, 1.2855, 0.0175, "snap")
    nt_ += stitched("BREWERY LIMITED", 0.103, 1.2725, 0.0072, "snap", squeeze=0.9, bold=0.00018)
    print("HERO buddy wordmark: %d tris" % nt_)
else:
    fwd_ray = lambda u, v: V(0, 1, 0)
    FX = 0.84 if FEM else 1.0    # (her jacket is narrower: the pockets move in with it)
    # chest pocket (left chest), with a zip along its top
    g = grid_on(JS, lambda u, v: V(u * FX, -0.6, v), fwd_ray, lin(0.045, 0.155, 4), lin(1.245, 1.13, 4))
    if g:
        raised(JD, g, 0.014, 0.006)
        strip(GD, [JS.cast(V(x * FX, -0.6, 1.248), V(0, 1, 0), 0.017) for x in lin(0.05, 0.15, 4)], 0.008, 0.004, "zip")
    # hand-warmer pockets low on both sides of the front, zips slanting
    for sx in (1, -1):
        g = grid_on(JS, lambda u, v: V(u * FX * sx, -0.6, v + (u - 0.09) * 0.4), fwd_ray, lin(0.06, 0.17, 4), lin(1.03, 0.92, 4))
        if g:
            raised(JD, g, 0.012, 0.005)
            strip(GD, [JS.cast(V(x * FX * sx, -0.6, 1.035 + (x - 0.09) * 0.4), V(0, 1, 0), 0.015) for x in lin(0.07, 0.165, 4)], 0.007, 0.004, "zip")
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
    if not BUDDY and co.y > by + 0.015 and co.z > 1.02 and abs(co.x) < 0.16: return PK, 1.0
    return {}, 0.0
pts_gear = list(range(len(GD.v)))
for i, co in enumerate(GD.v):
    b, k = pack_or_cloth(co)
    if k > 0: GD.w[i] = dict(b)
    else:
        GD.w[i] = (LS if co.z < 0.86 and abs(co.x) > 0.12 and co.y > -0.3 else JS).weights(co)
for C, target in [(JD, "HeroBody"), (LD, "HeroLegs"), (GD, "HeroGear")] + XTRA:
    me = bpy.data.meshes.new(C.name)
    bm = bmesh.new(); vs = [bm.verts.new(v) for v in C.v]
    uvl = bm.loops.layers.uv.new("UVMap")
    for fc in C.f:
        if len(set(fc)) < 3: continue
        try: face = bm.faces.new([vs[i] for i in fc])
        except ValueError: continue
        for lp, i in zip(face.loops, fc): lp[uvl].uv = C.uv[i]
    if C not in [x[0] for x in XTRA] or getattr(C, "recalc", False): bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
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
def shape_keys(ob, shapes):
    """Shapes made of (where a vertex is, where it goes) pairs, as shape keys on an object (matched by position)."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(ob.data.vertices))
    for v in ob.data.vertices: kd.insert(v.co, v.index)
    kd.balance()
    if ob.data.shape_keys is None: ob.shape_key_add(name="Basis", from_mix=False)
    for nm_, moves in shapes.items():
        key = ob.shape_key_add(name=nm_, from_mix=False); n_ = 0
        for at, to in moves:
            for co_, ix, dist in kd.find_range(at, 2e-5): key.data[ix].co = to; n_ += 1
        key.value = 0.0   # at rest as modelled
        print("HERO shape %s on %s: %d vertices" % (nm_, ob.name, n_))
# the face's moving parts become shape keys on the head mesh, the balaclava's on the gear; the game blends them in (RiderBodyHero)
if SHAPES: shape_keys(OBJS["HeroHead"], SHAPES)
if GEAR_SHAPES: shape_keys(OBJS["HeroGear"], GEAR_SHAPES)
if GOGGLES:
    # "GogglesUp": the goggles (every vertex in their swatches: they come through the smoothing whole) tipped up about
    # a hinge at the back of the head onto the front of the beanie, and out just far enough to sit on its knit. In the
    # camp the game shows them like this, so the face shows; on a run they come down over the eyes.
    gob = OBJS["HeroHead"]; me = gob.data
    cells = [swatch_uv(c) for c in GOGGLES["cols"]]
    uvl = me.uv_layers.active
    gv, strap_v = set(), set()
    for li, lp in enumerate(me.loops):
        u, v = uvl.data[li].uv
        for ci, (cu, cv) in enumerate(cells):
            if abs(u - cu) < 1e-3 and abs(v - cv) < 1e-3:
                gv.add(lp.vertex_index)
                if ci == 2: strap_v.add(lp.vertex_index)
    HC_, HS_, bn = GOGGLES["HC"], GOGGLES["HS"], GOGGLES["beanie"]
    pivot = HC_ + V(0, 0.118, GOGGLES["zc"]) * HS_
    R_ = Matrix.Rotation(math.radians(-21), 3, 'X')
    up = {i: pivot + R_ @ (me.vertices[i].co - pivot) for i in gv}
    def k_at(z):   # how wide the beanie is at a height (head units), against its cuff
        if z <= bn["crown"][0][0]: return 1.0
        for (z0, k0), (z1, k1) in zip(bn["crown"], bn["crown"][1:]):
            if z <= z1: return lerp(k0, k1, (z - z0) / (z1 - z0)) / 1.03
        return bn["crown"][-1][1] / 1.03
    # Up there they wrap the beanie, which is wider than the face they were fitted to: each point keeps how far it
    # stood off the face (its depth in the goggles) but now stands that far off the knit, all round.
    def ell(dx, dy, rx_, ryf_, ryb_):
        dl = math.hypot(dx, dy)
        return 1.0 / math.sqrt((dx / dl / rx_) ** 2 + (dy / dl / (ryf_ if dy < 0 else ryb_)) ** 2)
    need = 0.0
    for i, q in up.items():
        r0 = (me.vertices[i].co - HC_) / HS_; d0x, d0y = r0.x, r0.y - 0.012
        if math.hypot(d0x, d0y) < 1e-6: continue
        off = math.hypot(d0x, d0y) - ell(d0x, d0y, GOGGLES["rx"], GOGGLES["ryf"], GOGGLES["ryb"])
        r = (q - HC_) / HS_; dx, dy = r.x, r.y - 0.012; dl = math.hypot(dx, dy)
        if dl < 1e-6: continue
        nr = ell(dx, dy, bn["rx"], bn["ryf"], bn["ryb"]) * k_at(r.z) + max(off, 0.0) + 0.002
        up[i] = HC_ + V(dx / dl * nr, 0.012 + dy / dl * nr, r.z) * HS_
        need = max(need, nr - dl)
    if gob.data.shape_keys is None: gob.shape_key_add(name="Basis", from_mix=False)
    key = gob.shape_key_add(name="GogglesUp", from_mix=False)
    for i, q in up.items(): key.data[i].co = q
    key.value = 0.0
    print("HERO shape GogglesUp: %d vertices, out %.1f mm to clear the beanie" % (len(up), need * HS_ * 1000))
print("HERO rider triangles with details:", tri_total)

# limit to 4 influences and normalise; the skinned meshes go on the skeleton
for name in [n_ for n_ in ("HeroBody", "HeroHood", "HeroLegs", "HeroGear", "HeroHead", "HeroHands") if n_ in OBJS]:
    ob = OBJS[name]
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    ob.parent = ARM
    md = ob.modifiers.new("Armature", 'ARMATURE'); md.object = ARM
# the hood and the jacket are one renderer in the game
if "HeroHood" in OBJS:
    bpy.ops.object.select_all(action='DESELECT')
    OBJS["HeroHood"].select_set(True); OBJS["HeroBody"].select_set(True)
    bpy.context.view_layer.objects.active = OBJS["HeroBody"]
    bpy.ops.object.join()
    del OBJS["HeroHood"]
if VAL:
    # the fleece is blue above the chest seam and down the sleeves, white below (the pocket too)
    ob = OBJS["HeroBody"]; me = ob.data
    while len(me.materials) < 2: me.materials.append(MATS["FleeceWhite"])
    me.materials[0] = MATS["Jacket"]; me.materials[1] = MATS["FleeceWhite"]
    arm_groups = {g.index for g in ob.vertex_groups if "Arm" in g.name or "Twist" in g.name or "Hand" in g.name}
    def arm_w(v): return sum(g.weight for g in v.groups if g.group in arm_groups)
    for pg in me.polygons:
        z = sum(me.vertices[i].co.z for i in pg.vertices) / len(pg.vertices)
        aw = sum(arm_w(me.vertices[i]) for i in pg.vertices) / len(pg.vertices)
        pg.material_index = 1 if (z < 1.2005 and aw < 0.45) else 0
if PAUL_B:
    # the hoodie's grey sleeves under the black vest (the vest's body is the jacket's; its hood is grey already)
    ob = OBJS["HeroBody"]; me = ob.data
    names_ = [m.name for m in me.materials]
    if "Hoodie" not in names_: me.materials.append(MATS["Hoodie"])
    hi_ = [m.name for m in me.materials].index("Hoodie"); ji_ = [m.name for m in me.materials].index("Jacket")
    arm_groups = {g.index for g in ob.vertex_groups if "Arm" in g.name or "Twist" in g.name or "Hand" in g.name}
    def arm_w(v): return sum(g.weight for g in v.groups if g.group in arm_groups)
    for pg in me.polygons:
        if me.materials[pg.material_index].name != "Jacket": continue
        aw = sum(arm_w(me.vertices[i]) for i in pg.vertices) / len(pg.vertices)
        if aw >= 0.45: pg.material_index = hi_
if VAL or BRI_PJ:
    # the pyjama print: laid on flat from the front and back, and from the sides (a box mapping), 0.36 m a tile (a bear about 7 cm)
    ob = OBJS["HeroLegs"]; me = ob.data
    uvl = me.uv_layers.active or me.uv_layers.new(name="UVMap")
    for pg in me.polygons:
        nx, ny = abs(pg.normal.x), abs(pg.normal.y)
        for li in pg.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl.data[li].uv = ((co.y if nx > ny else co.x) / 0.36, co.z / 0.36)
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

@clip("Stand_Val", 150, True)
def _(t):
    """Val standing about in camp: both hands in the fleece's front pocket, rocking on his heels, looking round."""
    p = breathe(STAND, t * 3)
    rock = math.sin(t * 4 * math.pi)
    for sd, sx in (("L", 1), ("R", -1)):
        p["h" + sd] = ("rel", V(sx * -0.085, -0.222 - 0.006 * rock, -0.305))
        p["e" + sd] = V(sx * 0.9, 0.5, -0.3)
        p["g" + sd] = 0.7
    look = 28 * math.sin(t * 2 * math.pi + 0.6) + 6 * math.sin(t * 6 * math.pi)
    p["head"] = (3 + 4 * math.sin(t * 4 * math.pi + 1), 0, look)
    p["neck"] = (0, 0, look * 0.3)
    p["hips"] = p["hips"] + V(0.012 * math.sin(t * 2 * math.pi), 0.016 * rock, 0)
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
    # the arms swing against the legs, from the shoulder: walking they hang nearly straight (a slight bend as each
    # comes forward); running they pump, bent at the elbow, the fist rising in front of the chest and the elbow
    # driving back with the hand at the hip. The elbows stay under the shoulders (they point back and down, never
    # out behind the body).
    sw = math.sin(ph)
    for sd, sx, a in (("L", 1, -sw), ("R", -1, sw)):      # a: +1 this arm fully forward, -1 fully back
        f_ = max(0.0, a); b_ = max(0.0, -a)
        walk_h = V(sx * (0.075 - 0.03 * f_), -0.19 * f_ + 0.13 * b_, -0.512 + 0.05 * f_ ** 1.5 + 0.004 * b_)
        mid, fwd, back = V(sx * 0.06, -0.11, -0.32), V(sx * 0.03, -0.27, -0.20), V(sx * 0.09, 0.05, -0.37)
        run_h = mid.lerp(fwd, f_) if a > 0 else mid.lerp(back, b_)
        p["h" + sd] = ("rel", walk_h.lerp(run_h, run))
        p["e" + sd] = V(sx * 0.18, 1.0 - 0.5 * f_ * run, -0.2 - 0.9 * f_ * run)
        p["g" + sd] = lerp(0.35, 0.8, run)
    return p

@clip("Walk", 30, True)
def _(t): return gait(t, 0.0)

@clip("Run", 30, True)
def _(t): return gait(t, 1.0)

# -- moves in the camp: a jump and a forward roll ----------------------------------------------------------
# Timed to the camp's own physics (Camp.Jump / Camp.Roll): a 0.12 s crouch, off the snow at 3.4 m/s (down again
# 0.69 s later), the landing soaked up; a 0.8 s roll, during which the game turns the whole body head over heels
# round a point a ball's height up while this clip curls it into the ball and out again into a run.
def stand_feet(p, up=0.0, fwd=0.0, pitch=0.0):
    p = deep(p)
    p["fL"] = (V(0.12, -0.02 - fwd, ANKLE_Z + up), 8, pitch, 0); p["fR"] = (V(-0.12, 0.02 - fwd, ANKLE_Z + up), -6, pitch, 0)
    p["kL"] = V(0.15, -1, 0); p["kR"] = V(-0.15, -1, 0)
    return p
J_CROUCH = stand_feet(dict(STAND, hips=V(0, 0.03, -0.22), hrot=(26, 0, 0), spine=(14, 0, 0), head=(-10, 0, 0), neck=(0, 0, 0),
                           hL=("rel", V(0.10, 0.24, -0.38)), hR=("rel", V(-0.10, 0.24, -0.38)), eL=V(0.3, 0.6, -0.6), eR=V(-0.3, 0.6, -0.6)))
J_PUSH = stand_feet(dict(STAND, hips=V(0, 0.0, 0.02), hrot=(4, 0, 0), spine=(-4, 0, 0), head=(-12, 0, 0), neck=(0, 0, 0),
                         hL=("rel", V(0.14, -0.18, 0.32)), hR=("rel", V(-0.14, -0.18, 0.32)), eL=V(0.4, 0.4, -0.5), eR=V(-0.4, 0.4, -0.5)),
                    up=0.03, pitch=-28)
J_AIR = stand_feet(dict(STAND, hips=V(0, 0.0, -0.04), hrot=(10, 0, 0), spine=(4, 0, 0), head=(-6, 0, 0), neck=(0, 0, 0),
                        hL=("rel", V(0.32, -0.06, 0.22)), hR=("rel", V(-0.32, -0.06, 0.22)), eL=V(0.3, 0.6, -0.6), eR=V(-0.3, 0.6, -0.6)),
                   up=0.16, fwd=0.06, pitch=-10)
J_REACH = stand_feet(dict(J_AIR, hips=V(0, 0.0, 0.0), hL=("rel", V(0.30, -0.16, 0.06)), hR=("rel", V(-0.30, -0.16, 0.06))), up=0.03, fwd=0.04, pitch=6)
J_LAND = stand_feet(dict(STAND, hips=V(0, 0.04, -0.26), hrot=(28, 0, 0), spine=(16, 0, 0), head=(6, 0, 0), neck=(0, 0, 0),
                         hL=("rel", V(0.18, -0.34, -0.26)), hR=("rel", V(-0.18, -0.34, -0.26)), eL=V(0.3, 0.5, -0.7), eR=V(-0.3, 0.5, -0.7)))

@clip("CampJump", 31)
def _(t):
    s_ = t * 31 / 30.0   # seconds
    keys = [(0.0, STAND), (0.12, J_CROUCH), (0.20, J_PUSH), (0.42, J_AIR), (0.74, J_REACH), (0.86, J_LAND), (1.04, STAND)]
    for (t0, A), (t1, B) in zip(keys, keys[1:]):
        if s_ <= t1:
            k = (s_ - t0) / (t1 - t0)
            # the arms lag the body on the way up and down (overlap)
            return overlap(A, B, ease(min(1, k * 1.25)), ease(k))
    return STAND

R_DIVE = stand_feet(dict(STAND, hips=V(0, -0.04, -0.28), hrot=(30, 0, 0), spine=(30, 0, 0), head=(28, 0, 0), neck=(10, 0, 0),
                         hL=("rel", V(0.12, -0.40, -0.34)), hR=("rel", V(-0.12, -0.40, -0.34)), eL=V(0.3, 0.3, -0.8), eR=V(-0.3, 0.3, -0.8)), fwd=-0.04)
R_BALL = dict(STAND, hips=V(0, 0.02, -0.48), hrot=(40, 0, 0), spine=(46, 0, 0), head=(40, 0, 0), neck=(14, 0, 0),
              fL=(V(0.11, 0.06, 0.16), 0, 30, 0), fR=(V(-0.11, 0.06, 0.16), 0, 30, 0), kL=V(0.1, -1, 0.7), kR=V(-0.1, -1, 0.7),
              hL=("rel", V(0.06, -0.20, -0.28)), hR=("rel", V(-0.06, -0.20, -0.28)), eL=V(0.5, 0.3, -0.4), eR=V(-0.5, 0.3, -0.4), gL=0.9, gR=0.9)

@clip("CampRoll", 24)
def _(t):
    s_ = t * 0.8
    if s_ < 0.12: return overlap(STAND, R_DIVE, ease(s_ / 0.12), ease(s_ / 0.12))
    if s_ < 0.22: return mix(R_DIVE, R_BALL, ease((s_ - 0.12) / 0.10))
    if s_ < 0.60: return R_BALL
    return overlap(R_BALL, gait(0.15, 1.0), ease((s_ - 0.60) / 0.20), ease(max(0.0, s_ - 0.64) / 0.16))

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
    rider = os.path.join(PROJECT, "Assets/Summit/Resources/Rider/%s.fbx" % FILE)
    anims = os.path.join(PROJECT, "Assets/Summit/Art/Hero/HeroAnims.fbx")
    os.makedirs(os.path.dirname(anims), exist_ok=True)
    common = dict(apply_unit_scale=True, add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis='Y',
                  secondary_bone_axis='X', armature_nodetype='NULL', path_mode='AUTO', embed_textures=False, mesh_smooth_type='OFF')
    bpy.ops.object.select_all(action='DESELECT')
    for o in [ARM] + list(OBJS.values()): o.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.export_scene.fbx(filepath=rider, use_selection=True, object_types={'ARMATURE', 'MESH'}, bake_anim=False, **common)
    if BUDDY or FEM: print("HERO exported", rider, "(the clips are the hero's: same skeleton)"); return
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
    for name in ("HeroBody", "HeroLegs", "HeroGear", "HeroHead", "HeroHands"):
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
