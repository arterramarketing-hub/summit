# Builds what Summit's hero rides besides the snowboard: a pair of skis with two poles, and a wooden toboggan,
# with every clip the rider needs on them. It runs Tools/MakeHero.py first (for the skeleton, the body and the
# posing code), so the two tools always agree, and it never rewrites the hero itself.
#
#   python3 Tools/MakeRides.py -- --project .                       (exports; needs "pip install bpy")
#   ... --project . --new-clips-only    (only adds the clips the table hasn't got: the ones in it are kept byte for
#                                        byte, so another Blender's solver, a degree off, doesn't touch them)
#   python3 Tools/MakeRides.py -- --render DIR [--what skis,sled,gear]   (preview pictures, rendered with Cycles)
#   blender --background --factory-startup --python Tools/MakeRides.py -- ...      (the same, from Blender)
#
# Writes (with --project):
#   Assets/Summit/Resources/Rider/Rides.fbx       one ski, one pole and the sled, as plain meshes (the game
#                                                 makes the second ski and pole itself)
#   Assets/Summit/Art/Hero/HeroAnims.fbx          every clip, the hero's and the new ones, each its own take
#   Assets/Summit/Art/Hero/HeroClips.json         the list of them for the editor's bake
#   Assets/Summit/Resources/Rider/HeroAnim.bytes  the table the game plays clips from: the clips already in it
#                                                 are kept byte for byte and the new ones are added after them,
#                                                 in the same format Assets/Editor/HeroBake.cs writes, so the
#                                                 game is right without opening Unity. (Summit > Bake Hero
#                                                 Animations still works, and gives the same table.)
#
# Axes are Blender's, as in MakeHero.py: z up, the rider faces -y, their left is +x. On skis and on the sled
# the rider faces the way they go, so the tips of the skis and the curl of the sled point along -y, and the
# game turns the model a quarter turn from the snowboard's side-on stance (RiderBodyRide.cs).
#
# Where things sit, which the game relies on:
#   Ski   its origin is the middle of the boot, on the plate the sole stands on; the tip is toward -y.
#   Pole  its origin is the middle of the grip; the shaft runs down -z to the tip, the top of the handle is +z.
#   Sled  its origin is on its centre line, on top of the cross battens (where the rider sits), the curl at -y.
# A pole is held like this: the top of the handle comes out of the thumb side of the fist. At rest that is
# "forward, squared off against the hand's own length"; a pose's pL / pR says which way the shaft should point
# (from the grip to the tip) and the hand is turned to suit, so the clips carry the poles' directions.

import sys, os, math, struct

HERE = os.path.dirname(os.path.abspath(__file__))
_argv = sys.argv
sys.argv = [sys.argv[0]]                      # MakeHero only builds: no export, no renders
exec(compile(open(os.path.join(HERE, "MakeHero.py")).read(), os.path.join(HERE, "MakeHero.py"), "exec"))
sys.argv = _argv
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PROJECT = arg("--project")
RENDER = arg("--render")
WHAT = set((arg("--what", "all") or "all").split(","))
HERO_CLIPS = len(CLIPS)                       # everything after these is ours
NEW_ONLY = "--new-clips-only" in argv

# ----------------------------------------------------------------------------------------------- the models
for nm, rgb, sm_ in (("Wood", (0.66, 0.47, 0.26), 0.3), ("WoodDark", (0.38, 0.24, 0.12), 0.3),
                     ("Rope", (0.86, 0.80, 0.62), 0.1)):
    material(nm, rgb, sm_)

class Shape:
    """A plain mesh built face by face: each face has its own material and palette swatch."""
    def __init__(self, name):
        self.name, self.v, self.f, self.mats = name, [], [], []
    def vert(self, co): self.v.append(Vector(co)); return len(self.v) - 1
    def face(self, ix, mat, uv="spare"):
        if mat not in self.mats: self.mats.append(mat)
        self.f.append((tuple(ix), self.mats.index(mat), swatch_uv(uv)))
    def box(self, lo, hi, mat, uv="spare"):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        c = [self.vert(p) for p in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                    (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
        for q in ((3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.face([c[i] for i in q], mat, uv)
    def rings(self, rs, mats, uvs, cap0=True, cap1=True):
        """Join rings of vertices (all the same count) into a tube; mats/uvs per side of the ring, or one for all."""
        n = len(rs[0])
        def m(k): return mats[k] if isinstance(mats, (list, tuple)) else mats
        def u(k): return uvs[k] if isinstance(uvs, (list, tuple)) else uvs
        for a, b in zip(rs, rs[1:]):
            for k in range(n):
                k1 = (k + 1) % n
                self.face((a[k], a[k1], b[k1], b[k]), m(k), u(k))
        if cap0: self.face(rs[0][::-1], m(0), u(0))
        if cap1: self.face(rs[-1], m(0), u(0))
    def tube(self, path, n, mat, uv, ref=V(0, 0, 1)):
        """A round tube through (centre, radius) points."""
        rs = []
        for i, (c, r) in enumerate(path):
            t = path[min(i + 1, len(path) - 1)][0] - path[max(i - 1, 0)][0]
            a1, a2 = frame_of(t, ref)
            rs.append([self.vert(Vector(c) + (a1 * math.cos(2 * math.pi * k / n) + a2 * math.sin(2 * math.pi * k / n)) * r) for k in range(n)])
        self.rings(rs, mat, uv)
    def build(self, smooth=False):
        me = bpy.data.meshes.new(self.name)
        bm = bmesh.new()
        vs = [bm.verts.new(v) for v in self.v]
        uvl = bm.loops.layers.uv.new("UVMap")
        for ix, mi, uv in self.f:
            try: fc = bm.faces.new([vs[i] for i in ix])
            except ValueError: continue
            fc.material_index = mi
            for lp in fc.loops: lp[uvl].uv = uv
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = smooth
        for m in self.mats: me.materials.append(MATS[m])
        ob = bpy.data.objects.new(self.name, me)
        scene.collection.objects.link(ob)
        nt = sum(len(p.vertices) - 2 for p in me.polygons)
        print("RIDES mesh %-6s %5d tris, materials %s" % (self.name, nt, ", ".join(self.mats)))
        return ob

# ---- a ski: 1.62 m, a turned-up shovel, a little sidecut, a plate with a toe and a heel piece. The top sheet
# is the "Board" material, which the game paints with the colour picked at the rack.
SKI_TIP, SKI_TAIL, SKI_BASE, SKI_TOP = -0.90, 0.72, -0.045, -0.033
def make_ski():
    S_ = Shape("Ski")
    def width(y):
        if y < -0.76: return 0.104 * math.sqrt(max(0.0, 1 - ((-0.76 - y) / 0.14) ** 2.4))
        if y > 0.66: return 0.096 * math.sqrt(max(0.0, 1 - ((y - 0.66) / 0.06) ** 2.4))
        k = (y + 0.76) / 1.42                         # shovel to tail, the waist under the boot
        return lerp(0.104, 0.096, k) - 0.020 * math.sin(math.pi * k) ** 1.4
    def rise(y):
        if y < -0.70: return 0.105 * ((-0.70 - y) / 0.20) ** 1.8
        if y > 0.62: return 0.030 * ((y - 0.62) / 0.10) ** 1.6
        return 0.0
    ys = [-0.90, -0.885, -0.86, -0.82, -0.76, -0.70, -0.50, -0.25, 0.0, 0.25, 0.50, 0.62, 0.68, 0.71, 0.72]
    rs = []
    for y in ys:
        w = max(width(y), 0.012) / 2; z = rise(y)
        rs.append([S_.vert((-w, y, SKI_TOP + z)), S_.vert((w, y, SKI_TOP + z)), S_.vert((w, y, SKI_BASE + z)), S_.vert((-w, y, SKI_BASE + z))])
    S_.rings(rs, ["Board", "Gear", "Gear", "Gear"], ["spare", "deckedge", "base", "deckedge"])
    # the binding: a plate under the boot, a toe piece in front of it and a taller heel piece behind
    S_.box((-0.034, -0.215, SKI_TOP), (0.034, 0.235, -0.004), "Gear", "binding")
    S_.box((-0.042, -0.245, SKI_TOP), (0.042, -0.178, 0.030), "Gear", "bstrap")
    S_.box((-0.030, -0.262, SKI_TOP), (0.030, -0.245, 0.012), "Gear", "binding")
    S_.box((-0.040, 0.176, SKI_TOP), (0.040, 0.250, 0.052), "Gear", "bstrap")
    S_.box((-0.022, 0.250, 0.010), (0.022, 0.292, 0.036), "Gear", "packaccent")     # the heel lever
    return S_.build()

# ---- a pole: a grip with a flared top, a tapering shaft (painted like the skis), a basket and a steel tip
POLE_LEN = 1.08                                # from the middle of the grip to the tip
def make_pole():
    S_ = Shape("Pole")
    S_.tube([(V(0, 0, 0.082), 0.012), (V(0, 0, 0.074), 0.021), (V(0, 0, 0.058), 0.019), (V(0, 0, 0.04), 0.016),
             (V(0, 0, -0.055), 0.015), (V(0, 0, -0.07), 0.011)], 6, "Gear", "glove", ref=V(0, 1, 0))
    S_.tube([(V(0, 0, -0.07), 0.0095), (V(0, 0, -0.60), 0.008), (V(0, 0, -1.0), 0.006)], 6, "Board", "spare", ref=V(0, 1, 0))
    S_.tube([(V(0, 0, -0.985), 0.008), (V(0, 0, -0.995), 0.046), (V(0, 0, -1.003), 0.046), (V(0, 0, -1.012), 0.008)], 8, "Gear", "binding", ref=V(0, 1, 0))
    S_.tube([(V(0, 0, -1.0), 0.006), (V(0, 0, -POLE_LEN), 0.0025)], 5, "Gear", "vent", ref=V(0, 1, 0))
    return S_.build(smooth=True)

# ---- the toboggan: six ash slats side by side, steamed into a curl at the front, held by cross battens whose
# ends stick out as hand holds, and a rope from the curl lying slack on the deck
SLED_HALF, SLED_REAR, SLED_FLAT, SLED_CURL = 0.2125, 0.75, -0.50, 0.17
SLAT_TOP, SLAT_BOTTOM = -0.02, -0.05
BATTENS = (0.66, 0.30, 0.02, -0.40)            # where the cross battens are (y); the rider holds the third
HOLD_X = 0.235                                 # a hand's place on a batten's end
def curl_point(th, r=SLED_CURL):
    """Along the middle of a slat round the curl: th = 0 where the flat ends, 90 the very front, 180 the top."""
    zc = (SLAT_TOP + SLAT_BOTTOM) / 2 + SLED_CURL
    return V(0, SLED_FLAT - r * math.sin(math.radians(th)), zc - r * math.cos(math.radians(th)))
def make_sled():
    S_ = Shape("Sled")
    half = (SLAT_TOP - SLAT_BOTTOM) / 2
    path = [(V(0, y, (SLAT_TOP + SLAT_BOTTOM) / 2), V(0, 0, 1)) for y in (SLED_REAR, 0.3, -0.1, SLED_FLAT)]
    for th in range(25, 226, 25):
        c = curl_point(th)
        path.append((c, (V(0, SLED_FLAT, (SLAT_TOP + SLAT_BOTTOM) / 2 + SLED_CURL) - c).normalized()))   # "up" is in toward the curl's middle
    for i in range(6):
        x = (i - 2.5) * 0.072; w = 0.0325
        rs = []
        for j, (c, up) in enumerate(path):
            k = 0.75 if j == len(path) - 1 else 1.0   # the tip of the curl thinned a little
            rs.append([S_.vert(c + V(x - w, 0, 0) + up * half * k), S_.vert(c + V(x + w, 0, 0) + up * half * k),
                       S_.vert(c + V(x + w, 0, 0) - up * half * k), S_.vert(c + V(x - w, 0, 0) - up * half * k)])
        S_.rings(rs, "Wood", "spare")
    for y in BATTENS:
        S_.box((-HOLD_X - 0.02, y - 0.022, SLAT_TOP), (HOLD_X + 0.02, y + 0.022, 0.0), "WoodDark")
    # a batten across the end of the curl, and one where the curl begins to rise
    end = curl_point(215, SLED_CURL - half - 0.012)
    S_.box((-SLED_HALF - 0.012, end.y - 0.018, end.z - 0.014), (SLED_HALF + 0.012, end.y + 0.018, end.z + 0.014), "WoodDark")
    # the rope: tied to the ends of the curl's batten, down over the front battens and across in a slack loop
    for sx in (1, -1):
        pts = [V(sx * 0.19, end.y, end.z - 0.012), V(sx * 0.185, -0.42, 0.012), V(sx * 0.17, -0.30, 0.004),
               V(sx * 0.12, -0.16, 0.0), V(0.0, -0.11, 0.0)]
        S_.tube([(p, 0.008) for p in pts], 5, "Rope", "spare")
    return S_.build()

SKI_OB, POLE_OB, SLED_OB = make_ski(), make_pole(), make_sled()
for ob in (SKI_OB, POLE_OB, SLED_OB): ob.hide_render = True

# ----------------------------------------------------------------------------------------------- holding poles
# The pole's handle top, in the hand bone's own frame, at rest (see the header). The game works the same
# direction out from the bones (RiderBodyRide.cs), so the two agree without sharing any axis convention.
GRIP_ALONG = 0.08                              # the grip sits this far along the hand from the wrist
POLE_TOP = {}
for sd in ("Left", "Right"):
    f_ = REST[sd + "Hand"].to_3x3().col[1].normalized()
    top = (FWD - f_ * FWD.dot(f_)).normalized()
    POLE_TOP[sd] = REST[sd + "Hand"].to_3x3().inverted() @ top

_hero_solve = solve
def solve(P, s=None):
    """MakeHero's solve, then each hand turned so the pole in it points the way the pose says (pL, pR)."""
    s = _hero_solve(P, s)
    for sd in ("Left", "Right"):
        d = P.get("p" + sd[0])
        if d is None: continue
        R = s.M[sd + "Hand"].to_3x3()
        cur = (R @ POLE_TOP[sd]).normalized()
        want = -Vector(d).normalized()
        s.orient(sd + "Hand", (cur.rotation_difference(want).to_matrix() @ R))
    return s

def mirror(P):
    """The same pose the other way round (left for right)."""
    sw = {"L": "R", "R": "L"}
    q = {}
    for k, v in P.items():
        if k == "hips": q[k] = V(-v.x, v.y, v.z)
        elif k in ("hrot", "spine", "neck", "head"): q[k] = (v[0], -v[1], -v[2])
        elif len(k) == 2 and k[1] in sw:
            o = k[0] + sw[k[1]]
            if k[0] == "f": q[o] = (V(-v[0].x, v[0].y, v[0].z), -v[1], v[2], -v[3])
            elif k[0] in "kep": q[o] = V(-v[0], v[1], v[2])
            elif k[0] == "h": q[o] = (v[0], V(-v[1][0], v[1][1], v[1][2]))
            else: q[o] = v                      # wrist angles and grip are already written per side
        elif k[-1] in sw and k[:-1] in ("toe", "shrug"): q[k[:-1] + sw[k[-1]]] = v
        else: q[k] = v
    return q

def sym(**kw):
    """A pose given for the left side only: the right side is its mirror, unless it is given too."""
    p = dict(kw)
    for k, v in list(kw.items()):
        if len(k) == 2 and k[1] == "L" and k[0] + "R" not in kw:
            p.update({kk: vv for kk, vv in mirror({k: v}).items()})
    return p

def over(P, **kw):
    q = deep(P); q.update(kw); return q

def rclip(name, frames, loop=False):
    def deco(fn): CLIPS.append((name, frames, loop, fn)); return fn
    return deco

# ----------------------------------------------------------------------------------------------- the skier
# Feet a hip's width apart, both skis along the line; knees, hips and ankles flexed, hands forward and apart
# with the poles trailing back. In the turn clips the legs do different jobs, as they do on snow: the game
# banks the whole rider into the turn, which would lift the outside ski off the snow and bury the inside one,
# so the inside leg is drawn up short and forward and the outside leg is long, and both skis stay on the snow
# on their edges. The body angles the other way: hips in, shoulders level, facing a little down the hill.
SKI_HALF = 0.13
def AK(x, y=0.03, z=0.0): return V(x, y, ANKLE_Z + z)
SKI = sym(hips=V(0, 0.07, -0.17), hrot=(20, 0, 0), spine=(16, 0, 0), neck=(-8, 0, 0), head=(-16, 0, 0),
          fL=(AK(SKI_HALF), 0, 0, 0), kL=V(0.06, -1, 0),
          hL=("rel", V(0.15, -0.30, -0.21)), eL=V(0.7, 0.5, -0.5), wL=(0, 0, 0), gL=1.0, pL=V(0.16, 0.80, -0.58))

SKI_IDLE = over(SKI, hips=V(0, 0.03, -0.05), hrot=(6, 0, 0), spine=(5, 0, 0), neck=(0, 0, 0), head=(0, 0, 0),
                **sym(hL=("rel", V(0.17, -0.20, -0.30)), eL=V(0.6, 0.7, -0.3), pL=V(0.10, -0.16, -1.0)))

@rclip("Ski_Idle", 120, True)
def _(t):
    p = breathe(SKI_IDLE, t * 2)
    look = math.sin(t * 2 * math.pi) * 30 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (2 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (0, 0, look * 0.3)
    p["hips"] = p["hips"] + V(0.012 * math.sin(t * 2 * math.pi + 0.5), 0, 0)
    return p

@rclip("Ski_Ride", 60, True)
def _(t):
    p = breathe(SKI, t, 0.6)
    b = math.sin(t * 4 * math.pi)
    p["hips"] = p["hips"] + V(0, 0, 0.008 * b)
    for k, s_ in (("hL", 1), ("hR", -1)): p[k] = ("rel", p[k][1] + V(0, 0, 0.012 * s_ * math.sin(t * 2 * math.pi)))
    return p

BANK = math.tan(math.radians(41 * 0.75))      # the game's bank at full lean (RiderBody.Apply): what a foot must make up
SKI_TURN_L = over(SKI, hips=V(0.05, 0.08, -0.21), hrot=(22, -5, -8), spine=(14, -17, -12), neck=(-6, -4, 8), head=(-14, -6, 16),
                  fL=(AK(SKI_HALF, -0.05, SKI_HALF * BANK), 0, 0, 0), fR=(AK(-SKI_HALF, 0.05, -SKI_HALF * BANK), 0, 0, 0),
                  kL=V(0.34, -1, 0), kR=V(0.24, -1, 0),
                  hL=("rel", V(0.20, -0.36, -0.12)), eL=V(0.7, 0.4, -0.5), pL=V(0.20, 0.72, -0.66),
                  hR=("rel", V(-0.20, -0.26, -0.27)), eR=V(-0.7, 0.5, -0.5), pR=V(-0.30, 0.78, -0.54))
SKI_TURN_R = mirror(SKI_TURN_L)

@rclip("Ski_TurnL", 30, True)
def _(t): return breathe(SKI_TURN_L, t, 0.5)
@rclip("Ski_TurnR", 30, True)
def _(t): return breathe(SKI_TURN_R, t, 0.5)

# the tuck: back flat, chin up, fists together in front of the face, the poles clamped under the arms
SKI_TUCK = over(SKI, hips=V(0, 0.21, -0.37), hrot=(44, 0, 0), spine=(38, 0, 0), neck=(-22, 0, 0), head=(-42, 0, 0),
                **sym(kL=V(0.04, -1, 0), hL=("rel", V(-0.10, -0.33, -0.05)), eL=V(0.25, 0.1, -1.0), pL=V(0.10, 0.96, 0.24)))
@rclip("Ski_Tuck", 30, True)
def _(t): return breathe(SKI_TUCK, t, 0.3)

# a pole plant: up a little off the edges, the inside hand reaches down the hill and the tip touches the snow
SKI_PLANT_L = over(SKI, hips=V(0.01, 0.05, -0.12), hrot=(18, 0, 3), spine=(14, 0, 4),
                   hL=("rel", V(0.22, -0.42, -0.17)), eL=V(0.7, 0.3, -0.6), pL=V(0.04, -0.40, -0.92))
def plant(P):
    def fn(t):
        if t < 0.4: return overlap(SKI, P, ease(t / 0.4), ease(min(1, t / 0.32)))
        if t < 0.55: return P
        return overlap(P, SKI, ease((t - 0.55) / 0.45), ease(max(0, t - 0.6) / 0.4))
    return fn
CLIPS.append(("Ski_PlantL", 14, False, plant(SKI_PLANT_L)))
CLIPS.append(("Ski_PlantR", 14, False, plant(mirror(SKI_PLANT_L))))

SKI_WIND = over(SKI, hips=V(0, 0.14, -0.36), hrot=(36, 0, 0), spine=(24, 0, 0), head=(-26, 0, 0),
                **sym(hL=("rel", V(0.16, 0.08, -0.36)), eL=V(0.6, 0.6, -0.3), pL=V(0.14, 0.92, -0.25)))
SKI_POP = over(SKI, hips=V(0, 0.0, -0.03), hrot=(8, 0, 0), spine=(2, 0, 0), head=(-6, 0, 0),
               **sym(hL=("rel", V(0.22, -0.32, 0.04)), eL=V(0.7, 0.4, -0.5), pL=V(0.25, 0.70, -0.60)))
SKI_AIR = over(SKI, hips=V(0, 0.08, -0.27), hrot=(24, 0, 0), spine=(14, 0, 0), head=(-18, 0, 0),
               **sym(kL=V(0.10, -1, 0), hL=("rel", V(0.34, -0.20, -0.10)), eL=V(0.6, 0.6, -0.5), pL=V(0.50, 0.72, -0.42)))
SKI_LAND = over(SKI, hips=V(0, 0.16, -0.43), hrot=(40, 0, 0), spine=(22, 0, 0), head=(-30, 0, 0),
                **sym(hL=("rel", V(0.20, -0.38, -0.20)), eL=V(0.7, 0.3, -0.6), pL=V(0.25, 0.80, -0.45)))

@rclip("Ski_Pop", 16)
def _(t):
    dip = over(SKI_WIND, hips=SKI_WIND["hips"] + V(0, 0, -0.04))
    if t < 0.18: return mix(SKI_WIND, dip, ease(t / 0.18))
    if t < 0.45:
        k = (t - 0.18) / 0.27
        return overlap(dip, SKI_POP, ease(min(1, k * 1.3)), ease(k * k))
    k = (t - 0.45) / 0.55
    return overlap(SKI_POP, SKI_AIR, ease(min(1, k * 1.4)), ease(k))

@rclip("Ski_Air", 40, True)
def _(t):
    p = breathe(SKI_AIR, t, 0.5)
    w = math.sin(t * 2 * math.pi)
    p["hL"] = ("rel", p["hL"][1] + V(0, 0, 0.03 * w)); p["hR"] = ("rel", p["hR"][1] + V(0, 0, -0.03 * w))
    return p

@rclip("Ski_Land", 20)
def _(t):
    if t < 0.22: return overlap(mix(SKI_AIR, SKI, 0.5), SKI_LAND, ease(t / 0.22), ease(t / 0.22) * 0.6)
    k = (t - 0.22) / 0.78
    return overlap(SKI_LAND, SKI, ease(k), ease(max(0, k - 0.15) / 0.85))

# grabs. The game lifts the skis GRAB_LIFT toward the body (as it does the board), and each ski follows its
# own foot, so a clip can tip the skis: the tail grab kicks both tails up behind.
def reach(P, hand, name, up=GRAB_LIFT):
    """Bring the hips and the back to where the grabbing hand gets to its spot, a little at a time."""
    P = deep(P); side = "Left" if hand == "L" else "Right"; err = 9.0
    for it in range(60):
        T = lifted(P, up)
        target = Vector(T["h" + hand][1]) + V(0, 0, 0.065)
        s = solve(T)
        e = target - s.head(side + "Hand"); err = e.length
        if err < 0.012: break
        P["hips"] = P["hips"] + V(e.x * 0.25, e.y * 0.2, max(-0.02, min(0.02, e.z * 0.3)))
        x, y, z = P["spine"]
        P["spine"] = (max(-10, min(48, x - e.z * 40)), max(-24, min(24, y + e.x * 50)), z)
    print("RIDES grab %-8s reaches within %.3f m (hips %s, spine %s)" % (name, err, tuple(round(c, 2) for c in P["hips"]), tuple(round(c) for c in P["spine"])))
    return P

SKI_GRABBED = over(SKI, hips=V(0, 0.12, -0.46), hrot=(34, 0, 0), spine=(28, 0, 0), head=(-26, 0, 0), kL=V(0.16, -1, 0.1), kR=V(-0.16, -1, 0.1))
SKI_GRABS = {
    # safety: the right hand on the outside edge of the right ski, under the boot
    "Safety": ("R", over(SKI_GRABBED, spine=(30, -10, -6), hR=("abs", V(-0.185, 0.04, -0.03)), eR=V(-0.8, 0.5, 0.2), pR=V(-0.10, 0.97, 0.10),
                         hL=("rel", V(0.36, -0.12, 0.10)), eL=V(0.5, 0.6, -0.5), pL=V(0.55, 0.62, -0.35))),
    # mute: the left hand across to the right ski, in front of the toe piece
    "Mute": ("L", over(SKI_GRABBED, spine=(34, -6, -16), hrot=(34, 0, -4), hL=("abs", V(-0.085, -0.30, -0.03)), eL=V(0.7, 0.2, 0.4), pL=V(-0.5, 0.80, 0.25),
                       hR=("rel", V(-0.36, 0.06, 0.12)), eR=V(-0.5, 0.6, -0.5), pR=V(-0.55, 0.62, -0.35))),
    # tail: the heels kicked up behind, the right hand back on the tail of the right ski
    "Tail": ("R", over(SKI_GRABBED, hips=V(0, 0.04, -0.42), hrot=(22, 0, -8), spine=(6, -8, -26), head=(-12, 0, 10),
                       fL=(AK(SKI_HALF, 0.16, 0.06), 0, -30, 0), fR=(AK(-SKI_HALF, 0.16, 0.06), 0, -30, 0), kL=V(0.1, -1, -0.1), kR=V(-0.1, -1, -0.1),
                       hR=("abs", V(0, 0, 0)), eR=V(-0.9, 0.3, 0.2), pR=V(-0.2, 0.80, 0.45),
                       hL=("rel", V(0.30, -0.26, 0.10)), eL=V(0.5, 0.6, -0.5), pL=V(0.5, 0.50, -0.55))),
}
def on_ski(P, sd, y):
    """A point on top of a ski, y metres behind the middle of the boot, with the feet where this pose has them."""
    m = solve(P).M[sd + "Foot"] @ REST[sd + "Foot"].inverted() @ ski_rest(sd)
    return m @ V(0, y, SKI_TOP)
def ski_rest(sd):
    a = REST[sd + "Foot"].translation
    return Matrix.Translation(V(a.x, a.y - 0.09, 0.0))        # under the middle of the boot
_tail = SKI_GRABS["Tail"][1]; _tail["hR"] = ("abs", on_ski(_tail, "Right", 0.52))
SKI_GRAB_HAND = {}
for gname, (hand, gp) in SKI_GRABS.items():
    gp = reach(gp, hand, gname); SKI_GRAB_HAND[gname] = hand
    CLIPS.append(("Ski_Grab_" + gname, 30, True, (lambda gp: lambda t: breathe(gp, t, 0.4))(gp)))
    SKI_GRABS[gname] = (hand, gp)

# spins: wound up against the turn, then wrapped tight with the head leading; a flip is a tight ball
SKI_SPIN_L = over(SKI, hips=V(0, 0.08, -0.30), hrot=(24, 0, 12), spine=(16, 0, 28), neck=(-6, 0, 14), head=(-14, 0, 34),
                  hL=("rel", V(0.10, 0.14, -0.28)), eL=V(0.8, 0.5, -0.2), pL=V(0.25, 0.90, -0.2),
                  hR=("rel", V(0.14, -0.28, -0.14)), eR=V(-0.6, 0.3, -0.7), pR=V(0.55, 0.70, -0.3))
SKI_WIND_L = over(SKI, hips=V(0, 0.10, -0.30), hrot=(28, 0, -8), spine=(20, 0, -28), head=(-18, 0, 30),
                  hL=("rel", V(-0.10, -0.30, -0.20)), eL=V(0.6, 0.3, -0.7), pL=V(-0.5, 0.75, -0.3),
                  hR=("rel", V(-0.16, 0.16, -0.26)), eR=V(-0.8, 0.5, -0.2), pR=V(-0.3, 0.88, -0.25))
CLIPS.append(("Ski_WindL", 2, False, lambda t: SKI_WIND_L))
CLIPS.append(("Ski_WindR", 2, False, lambda t: mirror(SKI_WIND_L)))
CLIPS.append(("Ski_SpinL", 30, True, lambda t: breathe(SKI_SPIN_L, t, 0.3)))
CLIPS.append(("Ski_SpinR", 30, True, lambda t: breathe(mirror(SKI_SPIN_L), t, 0.3)))
SKI_FLIP = over(SKI, hips=V(0, 0.14, -0.50), hrot=(42, 0, 0), spine=(40, 0, 0), neck=(4, 0, 0), head=(6, 0, 0),
                **sym(kL=V(0.2, -1, 0.3), hL=("rel", V(0.04, -0.26, -0.30)), eL=V(0.8, 0.3, -0.4), pL=V(0.25, 0.93, 0.15)))
CLIPS.append(("Ski_Flip", 30, True, lambda t: breathe(SKI_FLIP, t, 0.3)))

# ----------------------------------------------------------------------------------------------- on the toboggan
# Sat on the battens with the feet out in front, heels on the slats behind the curl, hunched forward a little
# with a hand on a batten's end either side. Steering is leaning and dragging: the hand on the inside of the
# turn goes down into the snow behind the hip and that heel swings out and digs in.
SEAT = V(0, 0.33, -0.80)
def HOLD(sx, y=BATTENS[2]): return ("abs", V(sx * HOLD_X, y, -0.035))
SLED = sym(hips=SEAT, hrot=(-12, 0, 0), spine=(34, 0, 0), neck=(-8, 0, 0), head=(-12, 0, 0),
           fL=(V(0.13, -0.32, 0.085), 6, 10, 0), kL=side_pole(0.22, -0.3, 1),
           hL=HOLD(1), eL=V(0.9, 0.5, 0.2), wL=(0, 0, 0), gL=1.0)

@rclip("Sled_Idle", 120, True)
def _(t):
    # waiting at the gate: sat up, both feet down in the snow either side, looking about
    p = over(SLED, hrot=(-4, 0, 0), spine=(18, 0, 0), **sym(fL=(V(0.33, -0.02, 0.055), 16, 0, 0), kL=side_pole(0.5, -0.5, 1),
                                                           hL=("rel", V(0.10, -0.24, -0.34)), eL=V(0.8, 0.6, -0.2), gL=0.4))
    p = breathe(p, t * 2)
    look = math.sin(t * 2 * math.pi) * 34 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (-6 + 3 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (-4, 0, look * 0.3)
    return p

@rclip("Sled_Ride", 60, True)
def _(t):
    p = breathe(SLED, t, 0.8)
    j = math.sin(t * 6 * math.pi) * math.sin(t * 2 * math.pi)        # rattling along
    x, y, z = p["spine"]; p["spine"] = (x + 2 * j, y, z)
    x, y, z = p["head"]; p["head"] = (x - 3 * j, y, z)
    return p

SLED_LEAN_L = over(SLED, hips=SEAT + V(0.05, 0, 0), hrot=(-12, 12, 4), spine=(26, 20, 12), neck=(-6, -8, 8), head=(-10, -10, 14),
                   fL=(V(0.37, -0.26, 0.05), 34, 24, 0), kL=side_pole(0.6, -0.4, 1),
                   hL=("abs", V(0.44, 0.50, -0.10)), eL=V(0.9, 0.7, 0.3), gL=0.2)
CLIPS.append(("Sled_LeanL", 30, True, lambda t: breathe(SLED_LEAN_L, t, 0.6)))
CLIPS.append(("Sled_LeanR", 30, True, lambda t: breathe(mirror(SLED_LEAN_L), t, 0.6)))

# the tuck: lying back like a luger, feet first, chin on the chest to see past the toes
SLED_TUCK = over(SLED, hips=V(0, 0.24, -0.80), hrot=(-60, 0, 0), spine=(-10, 0, 0), neck=(22, 0, 0), head=(30, 0, 0),
                 **sym(fL=(V(0.10, -0.46, 0.11), 4, 24, 0), kL=side_pole(0.3, -0.2, 1), hL=HOLD(1, BATTENS[1]), eL=V(1, 0.2, 0.1)))
CLIPS.append(("Sled_Tuck", 30, True, lambda t: breathe(SLED_TUCK, t, 0.3)))

# in the air: knees up, hanging on; and letting go altogether, arms up and legs out (the no-hander)
SLED_AIR = over(SLED, hrot=(-16, 0, 0), spine=(40, 0, 0), head=(-18, 0, 0),
                **sym(fL=(V(0.14, -0.20, 0.27), 4, 22, 0), kL=side_pole(0.3, -0.2, 1)))
SLED_NOHANDS = over(SLED, hrot=(-26, 0, 0), spine=(6, 0, 0), neck=(4, 0, 0), head=(6, 0, 0),
                    **sym(fL=(V(0.24, -0.50, 0.36), 14, -8, 0), kL=side_pole(0.4, -0.2, 1),
                          hL=("rel", V(0.26, -0.06, 0.50)), eL=V(0.9, 0.4, 0.0), gL=0.0))
@rclip("Sled_Air", 30, True)
def _(t):
    p = breathe(SLED_AIR, t, 0.5)
    k = math.sin(t * 2 * math.pi) * 0.02
    for f_ in ("fL", "fR"): at_, a, b, c = p[f_]; p[f_] = (at_ + V(0, 0, k if f_ == "fL" else -k), a, b, c)
    return p
@rclip("Sled_NoHands", 30, True)
def _(t):
    p = breathe(SLED_NOHANDS, t, 0.5)
    w = math.sin(t * 4 * math.pi) * 0.04
    p["hL"] = ("rel", p["hL"][1] + V(w, 0, 0)); p["hR"] = ("rel", p["hR"][1] + V(w, 0, 0))
    return p

# a landing or a bump: the body folds over the knees and the head nods, then sits back up
SLED_JOLT = over(SLED, hrot=(-6, 0, 0), spine=(52, 0, 0), neck=(6, 0, 0), head=(14, 0, 0),
                 **sym(fL=(V(0.15, -0.30, 0.12), 8, 4, 0), eL=V(1.0, 0.2, 0.5)))
@rclip("Sled_Bump", 14)
def _(t):
    if t < 0.3: return overlap(SLED_AIR, SLED_JOLT, ease(t / 0.3), ease(t / 0.3) * 0.7)
    k = (t - 0.3) / 0.7
    return overlap(SLED_JOLT, SLED, ease(k), ease(max(0, k - 0.2) / 0.8))

# ----------------------------------------------------------------------------------------------- more tricks
# (the tricks branch: Rider.cs's grab lists name these, RiderBodyHero / RiderBodyRide play them)
# The snowboard's further grabs, made as MakeHero makes its own (GRABS, fix_reach): which hand, where on the board.
#   nose grab: the front hand on the nose, the back leg drawn up; japan: the front hand to the toe edge in front of
#   the front foot, round the knee, the board pulled up behind; roast beef: the back hand between the legs to the
#   heel edge; crail: the back hand forward to the toe edge by the nose, the front leg straightened; truck driver:
#   both hands at once, indy and melon.
MORE_GRABS = {
    "Nose Grab": P_(**dict(GRABBED, hips=V(0.10, 0.02, -0.34), spine=(30, 0, 0)), hL=("abs", board_pt(0.70, 0.0, 0.02)), eL=V(0.0, 0.4, 0.8), gL=1.0,
                    hR=("rel", V(-0.34, -0.10, 0.20)), eR=V(0.0, 0.5, -0.8), gR=0.2),
    "Japan": P_(**dict(GRABBED, hrot=(24, 0, 20), spine=(36, 8, 16), head=(10, 0, 30), kL=side_pole(0.1, -1, -0.35)),
                hL=("abs", board_pt(0.15, -0.152)), eL=V(0.7, -0.2, 0.5), gL=1.0,
                hR=("rel", V(-0.30, 0.06, 0.30)), eR=V(0.0, 0.6, -0.6), gR=0.2),
    "Roast Beef": P_(**dict(GRABBED, hrot=(26, 0, 6), spine=(34, -4, 4)), hR=("abs", board_pt(-0.03, 0.155)), eR=V(-0.1, -0.9, 0.3), gR=1.0,
                     hL=("rel", V(0.34, -0.02, 0.22)), eL=V(0.0, 0.5, -0.8), gL=0.2),
    "Crail": P_(**dict(GRABBED, hrot=(24, 0, 14), spine=(34, 10, 8), kL=side_pole(0.9, -0.6, 0.1)), hR=("abs", board_pt(0.34, -0.152)), eR=V(-0.2, -0.5, 0.6), gR=1.0,
                hL=("rel", V(0.30, 0.10, 0.30)), eL=V(0.0, 0.6, -0.6), gL=0.2),
    "Truck Driver": P_(**dict(GRABBED, hrot=(28, 0, 8), spine=(36, 0, 6)), hL=("abs", board_pt(0.05, 0.155)), eL=V(0.6, -0.2, 0.4), gL=1.0,
                       hR=("abs", board_pt(-0.04, -0.15)), eR=V(-0.3, 0.5, 0.3), gR=1.0),
}
for gname in list(MORE_GRABS): MORE_GRABS[gname] = fix_reach(MORE_GRABS[gname], gname)
for gname, gp in MORE_GRABS.items():
    CLIPS.append(("Grab_" + gname.replace(" ", ""), 30, True, (lambda gp: lambda t: breathe(gp, t, 0.4))(gp)))

# The skis' further grabs and the old school's no-hand shapes (each ski goes with its own foot, so the legs make them):
#   japan: the right hand across to the left ski in front of the boot, both skis tucked up behind, knees down;
#   octograb: the arms crossed, each hand on the other ski in front of the boot; daffy: one leg straight out in front,
#   the other straight back, arms out; iron cross: the heels kicked up behind and the tips crossed in an X under the
#   skier; spread eagle: legs and skis wide apart, arms up and out (a star).
SKI_OLD = over(SKI, hips=V(0, 0.08, -0.18), hrot=(16, 0, 0), spine=(8, 0, 0), head=(-14, 0, 0), pL=V(0.45, 0.75, -0.45), pR=V(-0.45, 0.75, -0.45))
MORE_SKI = {
    "Japan": ("R", over(SKI_GRABBED, hips=V(0, 0.06, -0.44), hrot=(30, 0, 6), spine=(34, 8, 12),
                        fL=(AK(SKI_HALF, 0.20, 0.10), 0, -24, 0), fR=(AK(-SKI_HALF, 0.20, 0.10), 0, -24, 0), kL=V(0.12, -1, -0.4), kR=V(-0.12, -1, -0.4),
                        hR=("abs", V(0.10, -0.30, -0.03)), eR=V(-0.6, -0.4, 0.4), pR=V(-0.4, 0.85, 0.30),
                        hL=("rel", V(0.36, -0.06, 0.12)), eL=V(0.5, 0.6, -0.5), pL=V(0.55, 0.62, -0.35))),
    "Octograb": ("L", over(SKI_GRABBED, hrot=(36, 0, 0), spine=(34, 0, 0),
                           hL=("abs", V(-0.10, -0.32, -0.03)), eL=V(0.8, -0.2, 0.3), pL=V(-0.6, 0.75, 0.25),
                           hR=("abs", V(0.10, -0.32, -0.03)), eR=V(-0.8, -0.2, 0.3), pR=V(0.6, 0.75, 0.25))),
}
SKI_DAFFY = over(SKI_OLD, hips=V(0, 0.02, -0.12), hrot=(4, 0, 0), spine=(4, 0, 0),
                 fL=(AK(SKI_HALF * 0.6, -0.66, 0.34), 0, 34, 0), kL=V(0.05, -1, 0.2),
                 fR=(AK(-SKI_HALF * 0.6, 0.64, 0.40), 0, -38, 0), kR=V(-0.05, -1, -0.2),
                 hL=("rel", V(0.58, -0.10, 0.10)), eL=V(0.3, 0.6, -0.5), hR=("rel", V(-0.58, -0.04, 0.14)), eR=V(-0.3, 0.6, -0.5))
SKI_IRON = over(SKI_OLD, hips=V(0, 0.02, -0.30), hrot=(22, 0, 0), spine=(16, 0, 0),
                fL=(AK(SKI_HALF * 0.9, 0.34, 0.38), -48, -40, 0), kL=V(0.15, -1, -0.5),
                fR=(AK(-SKI_HALF * 0.9, 0.34, 0.38), 48, -40, 0), kR=V(-0.15, -1, -0.5),
                hL=("rel", V(0.42, -0.14, 0.10)), eL=V(0.4, 0.6, -0.5), hR=("rel", V(-0.42, -0.14, 0.10)), eR=V(-0.4, 0.6, -0.5))
SKI_EAGLE = over(SKI_OLD, hips=V(0, 0.04, -0.10), hrot=(6, 0, 0), spine=(-4, 0, 0), head=(-20, 0, 0),
                 fL=(AK(0.42, 0.0, 0.18), 0, 0, -22), kL=V(1, -0.3, 0), fR=(AK(-0.42, 0.0, 0.18), 0, 0, 22), kR=V(-1, -0.3, 0),
                 hL=("rel", V(0.50, -0.04, 0.46)), eL=V(0.2, 0.6, -0.4), hR=("rel", V(-0.50, -0.04, 0.46)), eR=V(-0.2, 0.6, -0.4),
                 pL=V(0.7, 0.5, -0.5), pR=V(-0.7, 0.5, -0.5))
for gname, (hand, gp) in MORE_SKI.items():
    gp = reach(gp, hand, gname)
    CLIPS.append(("Ski_Grab_" + gname, 30, True, (lambda gp: lambda t: breathe(gp, t, 0.4))(gp)))
for gname, gp in (("Daffy", SKI_DAFFY), ("IronCross", SKI_IRON), ("SpreadEagle", SKI_EAGLE)):
    CLIPS.append(("Ski_Grab_" + gname, 30, True, (lambda gp: lambda t: breathe(gp, t, 0.4))(gp)))

# The toboggan's further airs: superman (hanging on to the third batten, the body flown out flat behind the sled,
# face down, arms straight, legs together); and stood up on it, surfing (side on, knees bent, arms out).
SLED_SUPERMAN = over(SLED, hips=V(0, 1.05, -0.42), hrot=(84, 0, 0), spine=(-6, 0, 0), neck=(-24, 0, 0), head=(-30, 0, 0),
                     **sym(fL=(V(0.10, 1.90, 0.40), 0, -80, 0), kL=V(0.1, 0, 1), hL=HOLD(1), eL=V(1, 0.2, 0.3), gL=1.0))
SLED_STAND = over(SLED, hips=V(0, 0.0, -0.30), hrot=(14, 0, 58), spine=(10, 0, -10), neck=(0, 0, 0), head=(-10, 0, -40),
                  fL=(V(0.0, -0.26, 0.09), 62, 0, 0), kL=side_pole(0.4, -1, 0.1), fR=(V(0.0, 0.26, 0.09), 62, 0, 0), kR=side_pole(-0.4, -1, 0.1),
                  hL=("rel", V(0.48, -0.06, 0.20)), eL=V(0.2, 0.6, -0.5), gL=0.3, hR=("rel", V(-0.48, -0.06, 0.24)), eR=V(-0.2, 0.6, -0.5), gR=0.3)
CLIPS.append(("Sled_Superman", 30, True, lambda t: breathe(SLED_SUPERMAN, t, 0.4)))
CLIPS.append(("Sled_Stand", 30, True, lambda t: breathe(SLED_STAND, t, 0.5)))

# The snow tube's airs (sat in it as MakeHero's Sit): the starfish, arms and legs flung wide; and hands up, both arms
# straight up and the legs out together, like the front car of a roller coaster.
TUBE_SIT = dict(hips=V(0, 0.09, -0.60), hrot=(-30, 0, 0), spine=(-6, 0, 0), neck=(0, 0, 0), head=(24, 0, 0),
                kL=side_pole(0.2, -0.4, 1), kR=side_pole(-0.2, -0.4, 1))
TUBE_STAR = P_(**dict(TUBE_SIT, hrot=(-40, 0, 0), head=(16, 0, 0), kL=side_pole(0.8, -0.2, 1), kR=side_pole(-0.8, -0.2, 1)),
               fL=(V(0.52, -0.62, 0.42), 30, 30, 0), fR=(V(-0.52, -0.62, 0.42), -30, 30, 0),
               hL=("rel", V(0.62, 0.04, 0.30)), eL=V(0.2, 0.6, -0.6), hR=("rel", V(-0.62, 0.04, 0.30)), eR=V(-0.2, 0.6, -0.6), gL=0.0, gR=0.0)
TUBE_UP = P_(**dict(TUBE_SIT, hrot=(-22, 0, 0), spine=(4, 0, 0), head=(10, 0, 0)),
             fL=(V(0.10, -0.80, 0.46), 4, 40, 0), fR=(V(-0.10, -0.80, 0.46), -4, 40, 0),
             hL=("rel", V(0.16, 0.02, 0.64)), eL=V(0.6, 0.4, 0.0), hR=("rel", V(-0.16, 0.02, 0.64)), eR=V(-0.6, 0.4, 0.0), gL=0.1, gR=0.1)
@rclip("Tube_Starfish", 30, True)
def _(t): return breathe(TUBE_STAR, t, 0.5)
@rclip("Tube_HandsUp", 30, True)
def _(t):
    p = breathe(TUBE_UP, t, 0.5)
    w = math.sin(t * 2 * math.pi) * 0.04
    p["hL"] = ("rel", p["hL"][1] + V(w, 0, 0)); p["hR"] = ("rel", p["hR"][1] + V(w, 0, 0))
    return p

# ----------------------------------------------------------------------------------------------- the fun rides
# The kayak, the rally car and the bear (rides): Tools/MakeRidesVehicles.py, with everything above to hand
exec(compile(open(os.path.join(HERE, "MakeRidesVehicles.py")).read(), os.path.join(HERE, "MakeRidesVehicles.py"), "exec"))

print("RIDES clips:", ", ".join(c[0] for c in CLIPS[HERO_CLIPS:]))

# ----------------------------------------------------------------------------------------------- export
# What Unity's importer makes of a bone's rotation and of the hips' place, found by checking every frame of
# every clip already in HeroAnim.bytes against this tool's own solve (see check_table): a bone's rotation
# relative to its parent (x, y, z, w) arrives as (-x, -y, z, w), and the hips' position, in the armature's
# centimetres, as (x, y, -z) / 100.
def unity_pose(s, order):
    out = []
    for n in order:
        p = PARENT[n]
        q = ((s.M[p].inverted() @ s.M[n]) if p else s.M[n]).to_quaternion().normalized()
        out.append((-q.x, -q.y, q.z, q.w))
    t = (s.M["Root"].inverted() @ s.M["Hips"]).translation
    return out, (t.x / 100.0, t.y / 100.0, -t.z / 100.0)

def read_table(data):
    """The clip table as HeroRig reads it: the bones' names, then each clip's (name, first byte, end byte, samples, frames, loop)."""
    p = 0
    def i32():
        nonlocal p; v = struct.unpack_from("<i", data, p)[0]; p += 4; return v
    def string():
        nonlocal p
        n = sh = 0
        while True:
            b = data[p]; p += 1; n |= (b & 0x7f) << sh; sh += 7
            if b < 128: break
        v = data[p:p + n].decode("utf8"); p += n; return v
    if i32() != 0x4F524548 or i32() != 1: raise SystemExit("RIDES: HeroAnim.bytes is not a hero clip table")
    names = [string() for _ in range(i32())]
    head, clips = p, []
    while p < len(data):
        start = p; name = string(); samples = i32(); frames = i32(); loop = data[p] != 0; p += 1 + 4
        body = p; p += samples * (8 * len(names) + 12)
        clips.append((name, start, p, samples, frames, loop, body))
    return names, head, clips

def dotnet_string(s):
    b = s.encode("utf8"); n = len(b); out = bytearray()
    while n >= 0x80: out.append((n & 0x7f) | 0x80); n >>= 7
    out.append(n)
    return bytes(out) + b

def clip_bytes(name, frames, loop, fn, order):
    out = bytearray(dotnet_string(name) + struct.pack("<iiBf", frames + 1, frames, 1 if loop else 0, 30.0))
    for f in range(frames + 1):
        rot, hips = unity_pose(solve(fn(f / frames)), order)
        for q in rot: out += struct.pack("<4h", *(max(-32767, min(32767, int(round(c * 32767)))) for c in q))
        out += struct.pack("<3f", *hips)
    return bytes(out)

def check_table(data):
    """How far this tool's poses are from what Unity baked, over every clip of MakeHero's in the table (degrees)."""
    names, head, clips = read_table(data)
    by = {c[0]: c for c in clips}
    worst, where, n = 0.0, "", 0
    for name, frames, loop, fn in CLIPS[:HERO_CLIPS]:
        if name not in by: continue
        _, _, _, samples, fr, lp, body = by[name]
        if samples != frames + 1: raise SystemExit("RIDES: %s has %d samples in the table, %d here" % (name, samples, frames + 1))
        for f in range(0, frames + 1, 3):
            rot, hips = unity_pose(solve(fn(f / frames)), names)
            row = struct.unpack_from("<%dh" % (4 * len(names)), data, body + f * (8 * len(names) + 12))
            for b, q in enumerate(rot):
                d = abs(sum(q[i] * row[4 * b + i] / 32767.0 for i in range(4)))
                e = math.degrees(2 * math.acos(min(1.0, d))); n += 1
                if e > worst: worst, where = e, "%s frame %d %s" % (name, f, names[b])
    return worst, where, n

def export_rides():
    import json
    table = os.path.join(PROJECT, "Assets/Summit/Resources/Rider/HeroAnim.bytes")
    data = open(table, "rb").read()
    names, head, clips = read_table(data)
    if set(names) != set(BONE_NAMES): raise SystemExit("RIDES: the table's bones are not this skeleton's")
    worst, where, n = check_table(data)
    print("RIDES check: %d rotations of the hero's own clips compared with the table Unity baked: worst %.2f deg (%s)" % (n, worst, where))
    if worst > 2.0: raise SystemExit("RIDES: this tool no longer matches the baked table; not writing it")
    mine = {c[0] for c in CLIPS[HERO_CLIPS:]}
    if NEW_ONLY: mine -= {c[0] for c in clips}                               # (what's there stays as it is)
    keep = b"".join(data[c[1]:c[2]] for c in clips if c[0] not in mine)     # the hero's clips, byte for byte
    out = data[:head] + keep + b"".join(clip_bytes(nm, fr, lp, fn, names) for nm, fr, lp, fn in CLIPS[HERO_CLIPS:] if nm in mine)
    open(table, "wb").write(out)
    print("RIDES wrote %s: %d KB, %d clips kept, %d added" % (table, len(out) // 1024, sum(1 for c in clips if c[0] not in mine), len(mine)))

    # the models: plain meshes, no skeleton
    apply(Solver())
    rides = os.path.join(PROJECT, "Assets/Summit/Resources/Rider/Rides.fbx")
    common = dict(apply_unit_scale=True, add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis='Y',
                  secondary_bone_axis='X', armature_nodetype='NULL', path_mode='AUTO', embed_textures=False, mesh_smooth_type='OFF')
    bpy.ops.object.select_all(action='DESELECT')
    for o in [SKI_OB, POLE_OB, SLED_OB] + KAYAK_OBS + CAR_OBS: o.hide_render = False; o.select_set(True)
    bpy.context.view_layer.objects.active = SKI_OB
    bpy.ops.export_scene.fbx(filepath=rides, use_selection=True, object_types={'MESH'}, bake_anim=False, **common)
    for o in bpy.context.selected_objects: o.hide_render = True          # (the previews show copies of them)
    # every clip as a take, for the editor's own bake (MakeHero's bake_clips, with the poles' hands solved)
    anims = os.path.join(PROJECT, "Assets/Summit/Art/Hero/HeroAnims.fbx")
    bake_clips()
    bpy.ops.object.select_all(action='DESELECT')
    ARM.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.export_scene.fbx(filepath=anims, use_selection=True, object_types={'ARMATURE'}, bake_anim=True,
                             bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0, **common)
    with open(os.path.join(os.path.dirname(anims), "HeroClips.json"), "w") as fh:
        json.dump({"fps": 30, "clips": [{"name": n_, "frames": fr, "loop": lp} for n_, fr, lp, _ in CLIPS]}, fh, indent=1)
    print("RIDES exported", rides, anims)

if PROJECT:
    export_rides()

# ----------------------------------------------------------------------------------------------- renders
# Cycles on the processor, so it runs anywhere bpy does. The gear is placed exactly as the game places it:
# each ski carried by its foot, each pole by its hand, the sled under the seat.
def cycles_setup(w, h, samples=20):
    render_setup(w, h)
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 3
    scene.render.threads_mode = 'AUTO'
    bpy.data.objects["Ground"].location = (0, 0, -0.05)       # the snow is a board's thickness under the feet

def copy_of(ob, name):
    c = bpy.data.objects.new(name, ob.data); scene.collection.objects.link(c); return c

GEAR = {}
def gear():
    if not GEAR:
        GEAR.update(skiL=copy_of(SKI_OB, "SkiL"), skiR=copy_of(SKI_OB, "SkiR"), poleL=copy_of(POLE_OB, "PoleL"), poleR=copy_of(POLE_OB, "PoleR"), sled=copy_of(SLED_OB, "SledShown"))
    return GEAR

def pole_rest(sd):
    Rm = REST[sd + "Hand"].to_3x3()
    grip = REST[sd + "Hand"].translation + Rm.col[1].normalized() * GRIP_ALONG
    top = (Rm @ POLE_TOP[sd]).normalized()
    return Matrix.Translation(grip) @ V(0, 0, 1).rotation_difference(top).to_matrix().to_4x4()

def dress(s, kind, lift=0.0, at=Matrix.Identity(4)):
    """Show the pose with its gear: kind is 'skis', 'sled' or None."""
    g = gear()
    for o in g.values(): o.hide_render = True
    OBJS["Snowboard"].hide_render = True
    ARM.matrix_world = at
    if kind == "skis":
        for sd, key in (("Left", "L"), ("Right", "R")):
            sk = g["ski" + key]; sk.hide_render = False
            sk.matrix_world = at @ s.M[sd + "Foot"] @ REST[sd + "Foot"].inverted() @ ski_rest(sd)
            po = g["pole" + key]; po.hide_render = False
            po.matrix_world = at @ s.M[sd + "Hand"] @ REST[sd + "Hand"].inverted() @ pole_rest(sd)
    elif kind == "sled":
        g["sled"].hide_render = False; g["sled"].matrix_world = at
    vehicle_dress(s, kind, at)

def bank(deg, yaw=0.0):
    """The whole rider tipped about the line of travel through the snow under them, as the game banks a turn."""
    pv = V(0, 0, -0.05)
    return Matrix.Translation(pv) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Rotation(math.radians(deg), 4, 'Y') @ Matrix.Translation(-pv)

def sheet(tag, kind, shots, cols, size=(440, 520)):
    cycles_setup(*size)
    paths = []
    for i, (title, P, up, at, cam) in enumerate(shots):
        s = solve(lifted(P, up) if up else P); apply(s)
        dress(s, kind, up, at)
        yaw, pitch, tgt, dist = cam
        look_from(yaw, pitch, tgt, dist, 50)
        lab = label(title, V(0, 0, 0), 0.09)
        c = bpy.data.objects["Cam"]
        lab.rotation_euler = c.rotation_euler
        lab.location = c.location + c.rotation_euler.to_matrix() @ V(0, 0.19 * dist, -dist)
        pth = os.path.join(RENDER, "%s_%02d.png" % (tag, i)); shot(pth); paths.append(pth)
        bpy.data.objects.remove(lab)
    tile(paths, cols, os.path.join(RENDER, tag + ".png"))
    for pth in paths: os.remove(pth)
    print("RIDES sheet", tag)

def P_at(name, t=0.0): return pose_by_name(name, t)

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    I4 = Matrix.Identity(4)
    front, side, back = (28, 8), (88, 5), (150, 14)
    def cam(v, z=0.72, d=4.4): return (v[0], v[1], V(0, 0, z), d)
    if "skis" in WHAT or "all" in WHAT:
        tb = SKI_HALF * BANK
        sheet("RideSkis", "skis", [
            ("Ski Idle", P_at("Ski_Idle"), 0, I4, cam(front, 0.8)),
            ("Ski Ride", P_at("Ski_Ride"), 0, I4, cam(front)),
            ("Ski Ride (side)", P_at("Ski_Ride"), 0, I4, cam(side)),
            ("Ski Tuck", P_at("Ski_Tuck"), 0, I4, cam(side, 0.6)),
            ("Ski Tuck (front)", P_at("Ski_Tuck"), 0, I4, cam((20, 10), 0.6)),
            ("Turn left, banked", P_at("Ski_TurnL"), 0, bank(31), cam((8, 8))),
            ("Turn right, banked", P_at("Ski_TurnR"), 0, bank(-31), cam((8, 8))),
            ("Turn left (behind)", P_at("Ski_TurnL"), 0, bank(31), cam((172, 16))),
            ("Pole plant", P_at("Ski_PlantL", 0.45), 0, I4, cam(front)),
            ("Ski Air", P_at("Ski_Air"), 0, I4, cam(front)),
            ("Ski Land", P_at("Ski_Land", 0.22), 0, I4, cam(side, 0.6)),
            ("Safety", SKI_GRABS["Safety"][1], GRAB_LIFT, I4, cam((-35, 6), 0.6)),
            ("Mute", SKI_GRABS["Mute"][1], GRAB_LIFT, I4, cam((30, 6), 0.6)),
            ("Tail grab", SKI_GRABS["Tail"][1], GRAB_LIFT, I4, cam((-70, 8), 0.7)),
            ("Spin (left)", P_at("Ski_SpinL"), 0, I4, cam(front)),
            ("Flip tuck", P_at("Ski_Flip"), GRAB_LIFT, I4, cam(side, 0.6)),
        ], 4)
    if "sled" in WHAT or "all" in WHAT:
        sheet("RideToboggan", "sled", [
            ("Toboggan Idle", P_at("Sled_Idle"), 0, I4, cam(front, 0.45, 4.0)),
            ("Toboggan Ride", P_at("Sled_Ride"), 0, I4, cam(front, 0.45, 4.0)),
            ("Ride (side)", P_at("Sled_Ride"), 0, I4, cam(side, 0.45, 4.0)),
            ("Ride (behind)", P_at("Sled_Ride"), 0, I4, cam(back, 0.45, 4.0)),
            ("Lean left, hand and heel dragging", P_at("Sled_LeanL"), 0, bank(6), cam((35, 14), 0.4, 4.0)),
            ("Lean right", P_at("Sled_LeanR"), 0, bank(-6), cam((165, 18), 0.4, 4.0)),
            ("Tuck (luge)", P_at("Sled_Tuck"), 0, I4, cam(side, 0.4, 4.0)),
            ("Air: hang on", P_at("Sled_Air"), 0, I4, cam(front, 0.45, 4.0)),
            ("No-hander", P_at("Sled_NoHands"), 0, I4, cam(front, 0.6, 4.4)),
            ("Bump", P_at("Sled_Bump", 0.3), 0, I4, cam(side, 0.45, 4.0)),
        ], 5)
    vehicle_sheets(sheet, cam, front, side, back, I4, bank)
    if "gear" in WHAT or "all" in WHAT:
        # the models on their own, close up
        cycles_setup(900, 420, 24)
        apply(Solver()); g = gear()
        for o in g.values(): o.hide_render = True
        OBJS["Snowboard"].hide_render = True
        for o in (OBJS["HeroBody"], OBJS["HeroLegs"], OBJS["HeroGear"]): o.hide_render = True
        g["skiL"].hide_render = g["skiR"].hide_render = g["poleL"].hide_render = g["sled"].hide_render = False
        g["skiL"].matrix_world = Matrix.Translation(V(0.55, 0.0, 0.0)); g["skiR"].matrix_world = Matrix.Translation(V(0.75, 0.05, 0.0))
        g["poleL"].matrix_world = Matrix.Translation(V(1.0, 0.45, -0.03)) @ Matrix.Rotation(math.radians(90), 4, 'X')
        g["sled"].matrix_world = Matrix.Translation(V(-0.55, 0.0, 0.0))
        look_from(52, 26, V(0.1, -0.05, 0.0), 4.6, 50)
        shot(os.path.join(RENDER, "RideGear.png"))
        for o in (OBJS["HeroBody"], OBJS["HeroLegs"], OBJS["HeroGear"]): o.hide_render = False
        print("RIDES gear picture")
