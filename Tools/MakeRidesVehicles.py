# The fun rides (rides): a sit-in kayak with its paddle, a small rally car, and what the hero does on them and on a
# bear. Run from Tools/MakeRides.py (exec'd after its toboggan clips, so it has Shape, the solver and every helper);
# its meshes go into Resources/Rider/Rides.fbx with the skis and the sled, its clips into HeroAnim.bytes after theirs.
# The bear itself is the wildlife's (Resources/Animals/Bear.fbx): the game measures its back as it gallops and sits
# the rider on it (RiderBodyVehicle.cs), so here there are only the rider's clips for it.
#
# Axes as in MakeRides: z up, the rider faces -y, their left is +x. The Root (the origin) is the deck's height,
# 5 cm over the snow (RiderBody.BoardTop), so a thing resting on the snow reaches down to z = -0.05.
#
# Where things sit, which the game relies on (RiderBodyVehicle.cs has the same numbers):
#   Kayak      drawn in place: its keel under the seat on the snow, the cockpit round the Root a little forward.
#   Paddle     its middle at the origin, the shaft along x (the +x blade the left hand's), the blades' faces along y.
#              The game holds it between the two hands: each hand's grip is GRIP_HALF along the shaft from the middle.
#   CarBody    drawn in place, the driver's seat over the Root; CarWheelFL/FR/RL/RR each drawn at its own corner (the
#              game turns each about its middle); CarSteer the steering wheel at STEER_AT, its column along STEER_AXIS.

KAYAK_OBS, CAR_OBS = [], []
# The car's paint is its own material (the game gives it a plain bright orange, as it does the sled's wood); everything
# else is the rider's gear material, on palette cells the closet never repaints (not the helmet's, goggles', gloves',
# boots', vents' or pack's: RiderBodyWalk.PaletteCells), so a ride keeps its colours whatever is worn.
material("CarPaint", (1.0, 0.42, 0.05), 0.7)

def lerp3(a, b, t): return a + (b - a) * t
def smoothk(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)

def ring_loop(S_, pts, r, n, mat, uv, up=V(0, 0, 1)):
    """A round tube along a closed loop of points."""
    rs = []
    m = len(pts)
    for i in range(m):
        t = (pts[(i + 1) % m] - pts[i - 1])
        a1, a2 = frame_of(t, up)
        rs.append([S_.vert(pts[i] + (a1 * math.cos(2 * math.pi * k / n) + a2 * math.sin(2 * math.pi * k / n)) * r) for k in range(n)])
    for i in range(m):
        a, b = rs[i], rs[(i + 1) % m]
        for k in range(n):
            k1 = (k + 1) % n
            S_.face((a[k], a[k1], b[k1], b[k]), mat, uv)

def obox(S_, c, ax, ay, az, hx, hy, hz, mat, uv):
    """A box at c along the axes ax, ay, az (unit), half its size along each."""
    pts = [c + ax * (hx * i) + ay * (hy * j) + az * (hz * k) for k in (-1, 1) for j in (-1, 1) for i in (-1, 1)]
    v = [S_.vert(p) for p in pts]
    for q in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (1, 3, 7, 5), (3, 2, 6, 7), (2, 0, 4, 6)):
        S_.face([v[i] for i in q], mat, uv)

def slab_ring(S_, c, nrm, r_out, r_in, thick, n, mat, uv):
    """A flat closed ring (a rim's dish), r_in to r_out, `thick` deep behind c along -nrm."""
    nrm = nrm.normalized()
    a1, a2 = frame_of(nrm, V(0, 0, 1) if abs(nrm.z) < 0.9 else V(0, 1, 0))
    def rg(r, d): return [S_.vert(c - nrm * d + (a1 * math.cos(2 * math.pi * k / n) + a2 * math.sin(2 * math.pi * k / n)) * r) for k in range(n)]
    S_.rings([rg(r_out, 0.0), rg(r_in, 0.0), rg(r_in, thick), rg(r_out, thick), rg(r_out, 0.0)], mat, uv, cap0=False, cap1=False)

def disc(S_, c, nrm, r, n, mat, uv, depth=0.0):
    """A flat round plate (a lamp's glass) facing nrm, standing `depth` proud on a short drum."""
    nrm = nrm.normalized()
    a1, a2 = frame_of(nrm, V(0, 0, 1) if abs(nrm.z) < 0.9 else V(0, 1, 0))
    rim = [c + (a1 * math.cos(2 * math.pi * k / n) + a2 * math.sin(2 * math.pi * k / n)) * r for k in range(n)]
    top = [S_.vert(p + nrm * depth) for p in rim]
    if depth > 0:
        bot = [S_.vert(p) for p in rim]
        for k in range(n):
            k1 = (k + 1) % n
            S_.face((bot[k], bot[k1], top[k1], top[k]), mat, uv)
    S_.face(top, mat, uv)

# ----------------------------------------------------------------------------------------------- the kayak
# A sit-in touring kayak, 3.1 m: a hull with a little rocker (its ends lift off the snow), widest just behind the
# middle, a vertical stem at each end; the deck crowned, bright cyan, the hull white (the hero's palette: the
# kayak, its paddle and the car are each drawn in the one material the rider's gear is). The rider sits in a spray skirt drawn tight over
# the cockpit's coaming: their legs are inside the hull, out of sight. Deck lines across the fore deck, a hatch
# on the aft deck, a grab toggle at each end.
KAYAK_BOW, KAYAK_STERN = -1.62, 1.48
KAYAK_BEAM = 0.64
KAYAK_KEEL = -0.05             # the keel under the seat (on the snow)
KAYAK_SHEER = 0.17             # the gunwale's height at the cockpit
COCKPIT_Y, COCKPIT_HL, COCKPIT_HW = 0.13, 0.43, 0.215   # the cockpit's middle (y), half its length, half its width

def kayak_dims(y):
    u = (y - (KAYAK_BOW + KAYAK_STERN) / 2) / ((KAYAK_STERN - KAYAK_BOW) / 2)   # -1 bow .. 1 stern
    w = u - 0.10
    k = max(0.0, 1 - abs(w / (1.10 if w < 0 else 0.90)) ** 2.1) ** 0.58
    hw = KAYAK_BEAM / 2 * k + 0.003
    keel = KAYAK_KEEL + 0.14 * abs(u) ** 2.6
    sheer = KAYAK_SHEER + 0.11 * abs(u) ** 2.3
    crown = 0.085 * k ** 0.5
    return hw, keel, sheer, crown

KAYAK_N = 20
def kayak_ring(y):
    hw, keel, sheer, crown = kayak_dims(y)
    pts = []
    for i in range(KAYAK_N):
        th = 2 * math.pi * i / KAYAK_N                # 0: the deck's middle, then down the left side (+x)
        s, c = math.sin(th), math.cos(th)
        x = hw * math.copysign(abs(s) ** (2 / 2.5), s)
        z = sheer + crown * abs(c) ** (2 / 1.5) if c >= 0 else sheer - (sheer - keel) * abs(c) ** (2 / 2.3)
        pts.append(V(x, y, z))
    return pts

def kayak_deck_z(x, y):
    hw, keel, sheer, crown = kayak_dims(y)
    s = min(1.0, abs(x) / hw) ** 1.25
    return sheer + crown * max(0.0, 1 - s * s) ** (0.5 * 2 / 1.5)

def make_kayak():
    S_ = Shape("Kayak")
    ys = [KAYAK_BOW + (KAYAK_STERN - KAYAK_BOW) * (0.5 - 0.5 * math.cos(math.pi * i / 30)) for i in range(31)]
    rs = [[S_.vert(p) for p in kayak_ring(y)] for y in ys]
    # the deck (the ring's top half, over the sheer) painted, the hull white, a dark rub strip at the sheer
    mats, uvs = [], []
    for k in range(KAYAK_N):
        th = 2 * math.pi * (k + 0.5) / KAYAK_N
        c = math.cos(th)
        if abs(c) < 0.09: mats.append("Gear"); uvs.append("deckedge")
        elif c > 0: mats.append("Gear"); uvs.append("logocyan")
        else: mats.append("Gear"); uvs.append("frame2")
    S_.rings(rs, mats, uvs)
    # the coaming (a rim round the cockpit) and the spray skirt stretched over it, domed a little
    loop = []
    for i in range(28):
        a = 2 * math.pi * i / 28
        x, y = COCKPIT_HW * math.cos(a), COCKPIT_Y + COCKPIT_HL * math.sin(a) * (1.0 if math.sin(a) > 0 else 0.92)
        loop.append(V(x, y, kayak_deck_z(x, y) + 0.012))
    ring_loop(S_, loop, 0.016, 6, "Gear", "deckedge")
    cen = V(0, COCKPIT_Y + 0.05, max(p.z for p in loop) + 0.035)
    prev = [S_.vert(p + V(0, 0, 0.004)) for p in loop]
    for j in (0.75, 0.45, 0.18):
        cur = [S_.vert(cen.lerp(p, j) + V(0, 0, 0.004) + V(0, 0, 0.012 * (1 - j))) for p in loop]
        for i in range(len(loop)):
            i1 = (i + 1) % len(loop)
            S_.face((prev[i], prev[i1], cur[i1], cur[i]), "Gear", "strap")
        prev = cur
    c0 = S_.vert(cen)
    for i in range(len(loop)): S_.face((prev[i], prev[(i + 1) % len(loop)], c0), "Gear", "strap")
    # deck lines: two crossed bungees on the fore deck, a perimeter line along each side of it
    def on_deck(x, y, lift=0.008): return V(x, y, kayak_deck_z(x, y) + lift)
    for (x0, y0, x1, y1) in ((0.16, -0.62, -0.16, -0.95), (-0.16, -0.62, 0.16, -0.95), (0.17, -0.55, 0.15, -1.15), (-0.17, -0.55, -0.15, -1.15)):
        path = [on_deck(x0 + (x1 - x0) * t / 6, y0 + (y1 - y0) * t / 6) for t in range(7)]
        S_.tube([(p, 0.006) for p in path], 5, "Gear", "gaiterband")
    # the aft hatch: an oval cover proud of the deck
    hatch = []
    for i in range(20):
        a = 2 * math.pi * i / 20
        x, y = 0.15 * math.cos(a), 0.86 + 0.20 * math.sin(a)
        hatch.append(V(x, y, kayak_deck_z(x, y)))
    top = [S_.vert(p + V(0, 0, 0.022)) for p in hatch]; bot = [S_.vert(p - V(0, 0, 0.01)) for p in hatch]
    for i in range(20):
        i1 = (i + 1) % 20
        S_.face((bot[i], bot[i1], top[i1], top[i]), "Gear", "binding")
    S_.face(top, "Gear", "binding")
    # a grab toggle at each end, through the stem
    for yy, sgn in ((KAYAK_BOW + 0.06, -1), (KAYAK_STERN - 0.06, 1)):
        hw, keel, sheer, crown = kayak_dims(yy)
        z = sheer + crown * 0.5
        S_.tube([(V(0.0, yy - sgn * 0.02, z + 0.01), 0.006), (V(0.0, yy + sgn * 0.05, z - 0.02), 0.006), (V(0.0, yy + sgn * 0.09, z - 0.06), 0.006)], 5, "Gear", "binding")
        S_.tube([(V(-0.05, yy + sgn * 0.09, z - 0.06), 0.012), (V(0.05, yy + sgn * 0.09, z - 0.06), 0.012)], 6, "Gear", "gaiterband", ref=V(0, 1, 0))
    ob = S_.build(smooth=True)
    return ob

# ---- the paddle: a black shaft, a drip ring either side, two spooned yellow blades, feathered
GRIP_HALF = 0.34               # each hand's grip from the shaft's middle (the game holds it there)
PADDLE_HALF = 1.08             # the tip of a blade from the middle
BLADE_ROOT = 0.64
PADDLE_FEATHER = 45.0          # degrees the right blade is turned from the left one about the shaft
def make_paddle():
    S_ = Shape("Paddle")
    S_.tube([(V(-BLADE_ROOT - 0.02, 0, 0), 0.0135), (V(-0.5, 0, 0), 0.015), (V(0.5, 0, 0), 0.015), (V(BLADE_ROOT + 0.02, 0, 0), 0.0135)], 8, "Gear", "binding", ref=V(0, 0, 1))
    for sx in (1, -1):
        x = sx * 0.53
        S_.tube([(V(x - 0.004, 0, 0), 0.017), (V(x, 0, 0), 0.042), (V(x + 0.006, 0, 0), 0.042), (V(x + 0.01, 0, 0), 0.017)], 10, "Gear", "binding", ref=V(0, 0, 1))
        rot = Matrix.Rotation(math.radians(0 if sx > 0 else PADDLE_FEATHER), 3, 'X')
        # the blade: an asymmetric spoon, its face (the power face) toward +y, widest two thirds of the way out
        n = 9; rows = []
        for i in range(n):
            t = i / (n - 1)
            xx = sx * (BLADE_ROOT + (PADDLE_HALF - BLADE_ROOT) * t)
            w = 0.025 + 0.068 * math.sin(math.pi * min(1.0, t * 0.82 + 0.05)) ** 0.7
            zc = 0.012 * math.sin(math.pi * t)                       # the blade's line dips a little off the shaft's
            row = []
            for j, (zz, yy) in enumerate(((w, 0.0), (w * 0.4, -0.006), (-w * 0.4, -0.006), (-w, 0.0), (-w * 0.4, 0.006), (w * 0.4, 0.006))):
                cup = 0.016 * (1 - (zz / max(w, 1e-3)) ** 2)              # spooned: the middle stands back from the edges
                row.append(S_.vert(rot @ V(xx, yy - cup + 0.008, zz + zc)))
            rows.append(row)
        S_.rings(rows, "Gear", "gaiterband")
    return S_.build(smooth=True)

# ----------------------------------------------------------------------------------------------- the rally car
# A small single-seat rally car (no make's: a made-up shape and livery): a short boxy body on big flared arches,
# white rally rims in chunky snow tyres, a light pod of four spots on the bonnet, a roof scoop and a wing on the
# tail, mud flaps, a roll cage inside. Bright orange, a white stripe along its flanks and over the roof, black
# trim (the hero's palette, no team's colours). The driver sits in the middle, over the Root, so the car sits round
# the rider's own point (what the physics hits things with). The side windows and the back are open (rally
# style), so the driver shows from the chase camera; the windscreen is smoked glass.
CAR_FRONT, CAR_REAR = -1.80, 1.66
CAR_HALF = 0.80
CAR_SILL = 0.15                    # the sills' underside over the Root (20 cm off the snow)
AXLE_F, AXLE_R = -1.12, 1.10
TRACK_HALF = 0.70
WHEEL_R, WHEEL_W = 0.31, 0.23
WHEEL_Z = WHEEL_R - 0.05           # the axles' height: the tyres on the snow
ARCH_R = 0.39
CAR_WHEELS = {"FL": (TRACK_HALF, AXLE_F), "FR": (-TRACK_HALF, AXLE_F), "RL": (TRACK_HALF, AXLE_R), "RR": (-TRACK_HALF, AXLE_R)}
STEER_AT = V(0.0, -0.36, 0.78)     # the steering wheel's middle
STEER_TILT = 24.0                  # its face tipped back from upright toward the driver (degrees)
STEER_R = 0.175
def steer_axis():
    a = math.radians(STEER_TILT)
    return V(0, math.cos(a), math.sin(a))          # from the wheel toward the driver's chest
def steer_frame():
    ax = steer_axis(); xa = V(1, 0, 0); ya = ax.cross(xa).normalized()   # in the wheel's face: x across, y its "up"
    return xa, -ya if ya.z < 0 else ya

def car_top(y):
    """The body's top line under the glass: the nose low, the bonnet rising to the scuttle, the tail square."""
    pts = ((CAR_FRONT, 0.50), (CAR_FRONT + 0.10, 0.62), (-1.45, 0.71), (-0.55, 0.84), (0.0, 0.88), (1.25, 0.92), (CAR_REAR - 0.12, 0.88), (CAR_REAR, 0.62))
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        if y <= y1: return lerp(z0, z1, ease((y - y0) / (y1 - y0)) if y1 > y0 else 1.0)
    return pts[-1][1]
def car_bottom(y):
    z = CAR_SILL
    if y < CAR_FRONT + 0.22: z = lerp(0.30, CAR_SILL, ease((y - CAR_FRONT) / 0.22))
    if y > CAR_REAR - 0.18: z = lerp(CAR_SILL, 0.30, ease((y - CAR_REAR + 0.18) / 0.18))
    for ya in (AXLE_F, AXLE_R):
        d = abs(y - ya)
        if d < ARCH_R: z = max(z, WHEEL_Z + math.sqrt(ARCH_R * ARCH_R - d * d))
    return z
def car_half(y):
    u = (y - CAR_FRONT) / (CAR_REAR - CAR_FRONT)
    end = min(u, 1 - u)
    return CAR_HALF * (1 - 0.13 * (1 - smoothk(0.0, 0.10, end)) ** 2)

CAR_N = 24
def car_ring(y):
    zt, zb, hw = car_top(y), car_bottom(y), car_half(y)
    zc, hh = (zt + zb) / 2, (zt - zb) / 2
    pts = []
    for i in range(CAR_N):
        th = 2 * math.pi * i / CAR_N                       # 0: the top's middle, then down the left flank (+x)
        s, c = math.sin(th), math.cos(th)
        z = zc + hh * math.copysign(abs(c) ** (2 / 5.0), c)
        tumble = 0.045 * max(0.0, (z - zc) / max(hh, 1e-3)) ** 2   # the flanks lean in toward the top
        x = (hw - tumble) * math.copysign(abs(s) ** (2 / 4.5), s)
        pts.append(V(x, y, z))
    return pts

def make_car():
    S_ = Shape("CarBody")
    ys = sorted(set([CAR_FRONT + (CAR_REAR - CAR_FRONT) * i / 40 for i in range(41)] +
                    [ya + d for ya in (AXLE_F, AXLE_R) for d in (-ARCH_R, -0.30, -0.2, -0.1, 0.0, 0.1, 0.2, 0.30, ARCH_R)]))
    rs = [[S_.vert(p) for p in car_ring(y)] for y in ys]
    # paint all round, a stripe along each flank (two ring faces a little under the belt), black sills under
    mats, uvs = [], []
    for k in range(CAR_N):
        th = 2 * math.pi * (k + 0.5) / CAR_N; c = math.cos(th)
        if 0.22 < c < 0.42: mats.append("Gear"); uvs.append("frame2")
        elif c < -0.80: mats.append("Gear"); uvs.append("base")
        else: mats.append("CarPaint"); uvs.append("spare")
    S_.rings(rs, mats, uvs)
    # the flares: a black lip round each arch, proud of the flank
    for ya in (AXLE_F, AXLE_R):
        for sx in (1, -1):
            pts = []
            for i in range(13):
                a = math.radians(-8 + 196 * i / 12)
                y = ya - ARCH_R * math.cos(a); z = WHEEL_Z + ARCH_R * math.sin(a)
                pts.append(V(sx * (car_half(y) + 0.015), y, max(z, CAR_SILL + 0.02)))
            S_.tube([(p, 0.035) for p in pts], 6, "Gear", "base", ref=V(1, 0, 0))
    # the glass-house: A pillars, a B pillar each side, C pillars down to the tail, the roof, the smoked windscreen
    roof_f, roof_r, roof_z, roof_w = -0.12, 0.98, 1.31, 0.62
    def belt_pt(sx, y, inset=0.05): return V(sx * (car_half(y) - inset - 0.045), y, car_top(y) - 0.01)
    for sx in (1, -1):
        S_.tube([(belt_pt(sx, -0.56), 0.032), (V(sx * (roof_w + 0.03), roof_f + 0.02, roof_z - 0.02), 0.03)], 6, "CarPaint", "spare")
        S_.tube([(belt_pt(sx, 0.40), 0.035), (V(sx * roof_w, 0.42, roof_z - 0.01), 0.035)], 6, "CarPaint", "spare")
        S_.tube([(V(sx * roof_w, roof_r, roof_z - 0.02), 0.05), (belt_pt(sx, 1.42, 0.02), 0.06)], 6, "CarPaint", "spare")
    # the roof: a domed slab over the pillars' tops, its edge rolled, a stripe down its middle
    def roof_ring(y):
        pts = []
        for i in range(16):
            th = 2 * math.pi * i / 16; sn, c = math.sin(th), math.cos(th)
            x = roof_w * math.copysign(abs(sn) ** (2 / 3.0), sn)
            z = roof_z + (0.035 * (1 - (x / roof_w) ** 2) if c > 0 else -0.03) * abs(c) ** (2 / 6.0)
            pts.append(V(x, y, z))
        return pts
    rr = [[S_.vert(p) for p in roof_ring(lerp(roof_f, roof_r, i / 6))] for i in range(7)]
    mid_x = [abs(roof_ring(0)[k].x + roof_ring(0)[(k + 1) % 16].x) / 2 for k in range(16)]
    top_k = [roof_ring(0)[k].z + roof_ring(0)[(k + 1) % 16].z > 2 * roof_z for k in range(16)]
    stripe = [top_k[k] and mid_x[k] < 0.2 for k in range(16)]
    S_.rings(rr, ["Gear" if stripe[k] else "CarPaint" for k in range(16)], ["frame2" if stripe[k] else "spare" for k in range(16)])
    # the windscreen: smoked glass between the A pillars, from the scuttle to the roof (a thin slab)
    wl = [V(0.70, -0.56, car_top(-0.56)), V(-0.70, -0.56, car_top(-0.56)), V(-roof_w, roof_f + 0.02, roof_z - 0.02), V(roof_w, roof_f + 0.02, roof_z - 0.02)]
    wn = (wl[1] - wl[0]).cross(wl[3] - wl[0]).normalized()
    q0 = [S_.vert(p + wn * 0.006) for p in wl]; q1 = [S_.vert(p - wn * 0.006) for p in wl]
    S_.face(q0, "Gear", "lens3"); S_.face(q1[::-1], "Gear", "lens3")
    for k in range(4):
        k1 = (k + 1) % 4
        S_.face((q0[k1], q0[k], q1[k], q1[k1]), "Gear", "lens3")
    # a roof scoop, a wing on the tail on two stands, the light pod on the bonnet, lamps, a grille, mud flaps
    S_.box((-0.16, 0.10, roof_z + 0.02), (0.16, 0.50, roof_z + 0.10), "CarPaint", "spare")
    S_.box((-0.12, 0.08, roof_z + 0.035), (0.12, 0.11, roof_z + 0.09), "Gear", "binding")
    for sx in (1, -1): S_.box((sx * 0.48 - 0.02, 1.48, car_top(1.48) - 0.01), (sx * 0.48 + 0.02, 1.56, car_top(1.5) + 0.20), "Gear", "binding")
    S_.box((-0.76, 1.42, car_top(1.48) + 0.19), (0.76, 1.70, car_top(1.48) + 0.23), "Gear", "binding")
    for sx in (1, -1): S_.box((sx * 0.765 - 0.012, 1.40, car_top(1.48) + 0.12), (sx * 0.765 + 0.012, 1.72, car_top(1.48) + 0.30), "Gear", "binding")
    pod_y = -1.52
    S_.box((-0.50, pod_y - 0.03, car_top(pod_y) + 0.01), (0.50, pod_y + 0.05, car_top(pod_y) + 0.07), "Gear", "binding")
    for i in range(4):
        x = -0.375 + 0.25 * i
        c = V(x, pod_y - 0.06, car_top(pod_y) + 0.13)
        disc(S_, c + V(0, 0.07, 0), V(0, -1, 0), 0.088, 12, "Gear", "binding", depth=0.07)
        disc(S_, c, V(0, -1, 0), 0.072, 12, "Gear", "eyewhite", depth=0.006)
    for sx in (1, -1):
        hl = V(sx * 0.56, CAR_FRONT + 0.03, 0.56)
        disc(S_, hl, V(0, -1, 0.15), 0.085, 12, "Gear", "eyewhite", depth=0.012)
        S_.box((sx * 0.62 - 0.10, CAR_REAR - 0.02, 0.66), (sx * 0.62 + 0.10, CAR_REAR + 0.015, 0.76), "Gear", "scrunch")
    S_.box((-0.36, CAR_FRONT - 0.005, 0.30), (0.36, CAR_FRONT + 0.08, 0.48), "Gear", "binding")
    S_.box((-0.78, CAR_FRONT - 0.04, 0.24), (0.78, CAR_FRONT + 0.06, 0.32), "Gear", "base")     # the bumper's skid plate
    S_.box((-0.78, CAR_REAR - 0.06, 0.24), (0.78, CAR_REAR + 0.04, 0.34), "Gear", "base")
    for ya, dy in ((AXLE_F, 0.36), (AXLE_R, 0.36)):
        for sx in (1, -1):
            S_.box((sx * TRACK_HALF - 0.13, ya + dy, 0.02), (sx * TRACK_HALF + 0.13, ya + dy + 0.015, CAR_SILL + 0.16), "Gear", "binding")
    S_.tube([(V(-0.42, CAR_REAR - 0.1, 0.26), 0.045), (V(-0.42, CAR_REAR + 0.07, 0.26), 0.045)], 8, "Gear", "zip", ref=V(0, 0, 1))
    # door numbers' plates (blank white roundels), mirrors on stalks
    for sx in (1, -1):
        disc(S_, V(sx * (car_half(0.15) + 0.002), 0.15, 0.58), V(sx, 0, 0), 0.15, 16, "Gear", "eyewhite", depth=0.004)
        S_.tube([(belt_pt(sx, -0.48), 0.012), (V(sx * 0.92, -0.50, 0.95), 0.012)], 5, "Gear", "binding")
        S_.box((sx * 0.92 - 0.06, -0.54, 0.90), (sx * 0.92 + 0.06, -0.47, 1.00), "CarPaint", "spare")
    # inside: the floor, a bucket seat, a dashboard, the column, a roll cage behind the seat
    S_.box((-0.68, -0.90, CAR_SILL + 0.02), (0.68, 1.30, CAR_SILL + 0.05), "Gear", "binding")
    S_.box((-0.26, -0.10, 0.30), (0.26, 0.38, 0.38), "Gear", "base")                    # the cushion
    S_.box((-0.27, 0.32, 0.34), (0.27, 0.44, 1.02), "Gear", "base")                     # the back
    for sx in (1, -1): S_.box((sx * 0.27 - (0.07 if sx > 0 else 0.0), -0.08, 0.36), (sx * 0.27 + (0.0 if sx > 0 else 0.07), 0.40, 0.62), "Gear", "base")   # its wings
    S_.box((-0.27, 0.30, 0.98), (0.27, 0.42, 1.12), "Gear", "base")                     # the headrest
    S_.box((-0.72, -0.72, 0.62), (0.72, -0.46, 0.80), "Gear", "binding")                # the dash
    S_.tube([(V(0, -0.64, 0.68), 0.028), (STEER_AT - steer_axis() * 0.06, 0.024)], 6, "Gear", "binding")
    for sx in (1, -1):
        S_.tube([(V(sx * 0.62, 0.56, CAR_SILL + 0.05), 0.022), (V(sx * 0.60, 0.56, 1.24), 0.022), (V(sx * 0.5, 0.5, 1.28), 0.022)], 6, "Gear", "zip")
        S_.tube([(V(sx * 0.60, 0.56, 1.10), 0.02), (V(sx * 0.62, 1.30, CAR_SILL + 0.3), 0.02)], 6, "Gear", "zip")
    S_.tube([(V(0.52, 0.52, 1.27), 0.022), (V(-0.52, 0.52, 1.27), 0.022)], 6, "Gear", "zip")
    S_.tube([(V(0.60, 0.56, 0.70), 0.02), (V(-0.60, 0.56, 1.05), 0.02)], 6, "Gear", "zip")
    return S_.build()

def make_wheel(name, at):
    """A chunky snow tyre on a white five-spoke rally rim, drawn at its corner (the game spins it about its middle)."""
    S_ = Shape(name)
    sx = 1 if at.x > 0 else -1
    def ring(r, x, n=18): return [V(at.x + x, at.y + r * math.cos(2 * math.pi * k / n), at.z + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    hw = WHEEL_W / 2
    prof = [(0.20, -hw + 0.01), (0.25, -hw), (WHEEL_R - 0.03, -hw), (WHEEL_R, -hw + 0.04), (WHEEL_R, hw - 0.04), (WHEEL_R - 0.03, hw), (0.25, hw), (0.20, hw - 0.01)]
    rows = [[S_.vert(p) for p in ring(r, x * sx)] for r, x in prof]
    S_.rings(rows + [rows[0]], "Gear", "sole", cap0=False, cap1=False)     # (round to the first again: a closed tyre)
    # the tread: blocks proud of the crown, staggered across it
    ax = V(1, 0, 0)
    for k in range(20):
        a = 2 * math.pi * (k + 0.5) / 20
        rad = V(0, math.cos(a), math.sin(a)); tan = V(0, -math.sin(a), math.cos(a))
        for j, xm in enumerate((-hw * 0.45, hw * 0.45)):
            sh = 0.035 if (k + j) % 2 else -0.035
            obox(S_, at + ax * (sx * xm) + rad * (WHEEL_R + 0.004) + tan * sh, ax, tan, rad, hw * 0.36, 0.034, 0.014, "Gear", "sole")
    # the rim: a white dish with a hub and a cap, five spokes
    face_x = sx * (hw - 0.035)
    out = V(sx, 0, 0)
    slab_ring(S_, at + V(face_x, 0, 0), out, 0.205, 0.06, 0.03, 20, "Gear", "frame2")
    disc(S_, at + V(face_x, 0, 0), out, 0.06, 12, "Gear", "zip", depth=0.035)
    for i in range(5):
        a = 2 * math.pi * i / 5
        d = V(0, math.cos(a), math.sin(a)); t = V(0, -math.sin(a), math.cos(a))
        obox(S_, at + V(face_x + sx * 0.012, 0, 0) + d * 0.13, ax, d, t, 0.012, 0.075, 0.026, "Gear", "frame2")
    return S_.build(smooth=True)

def make_steer():
    S_ = Shape("CarSteer")
    xa, ya = steer_frame(); ax = steer_axis()
    pts = [STEER_AT + (xa * math.cos(2 * math.pi * k / 20) + ya * math.sin(2 * math.pi * k / 20)) * STEER_R for k in range(20)]
    ring_loop(S_, pts, 0.016, 6, "Gear", "strap", up=ax)
    for a in (0, 180, 270):
        d = xa * math.cos(math.radians(a)) + ya * math.sin(math.radians(a))
        S_.tube([(STEER_AT - ax * 0.01, 0.014), (STEER_AT + d * (STEER_R - 0.01), 0.011)], 5, "Gear", "binding", ref=ax)
    disc(S_, STEER_AT - ax * 0.03, ax, 0.05, 10, "Gear", "binding", depth=0.03)
    disc(S_, STEER_AT + ax * 0.002, ax, 0.032, 10, "Gear", "gaiterband", depth=0.004)
    return S_.build(smooth=True)

KAYAK_OB, PADDLE_OB = make_kayak(), make_paddle()
CAR_OB = make_car()
WHEEL_OBS = {k: make_wheel("CarWheel" + k, V(x, y, WHEEL_Z)) for k, (x, y) in CAR_WHEELS.items()}
STEER_OB = make_steer()
KAYAK_OBS = [KAYAK_OB, PADDLE_OB]
CAR_OBS = [CAR_OB, STEER_OB] + list(WHEEL_OBS.values())
for ob in KAYAK_OBS + CAR_OBS: ob.hide_render = True

# ----------------------------------------------------------------------------------------------- in the kayak
# Sat low in the cockpit, the legs out in front inside the hull (out of sight), the back upright and a little
# forward, both hands on the shaft a shoulder's width and a bit apart. The paddle's place is what the clips
# say (centre and direction); the hands go where its grips are (GRIP_HALF either side of the middle), so the
# shaft always runs through both fists. Steering on snow is a rudder: the inside blade trailed back in the snow
# behind the hip with the body leaning over it; a forward stroke on the outside (the game times them) pulls the
# hull round and pushes it on.
K_SEAT = V(0, 0.33, -0.82)
def paddle_hands(c, u):
    """Both hands on the paddle: its middle c, its direction u (toward the left blade)."""
    u = Vector(u).normalized()
    return {"hL": ("abs", c + u * GRIP_HALF - V(0, 0, 0.065)), "hR": ("abs", c - u * GRIP_HALF - V(0, 0, 0.065))}
def K(c, u, **kw):
    p = sym(hips=K_SEAT, hrot=(-6, 0, 0), spine=(14, 0, 0), neck=(-4, 0, 0), head=(-8, 0, 0),
            fL=(V(0.13, -0.55, 0.085), 4, 18, 0), kL=side_pole(0.25, -0.3, 1), eL=V(0.9, 0.3, -0.5), wL=(0, 0, 0), gL=1.0)
    p.update(paddle_hands(V(*c), V(*u)))
    p["eR"] = V(-0.9, 0.3, -0.5)
    p.update(kw)
    return p
K_NEUTRAL = K((0, -0.10, 0.40), (1, 0, -0.02))
KAYAK_POSES = {}                     # name -> (paddle middle, direction) by phase: the game reads none of this, the renders do

@rclip("Kayak_Idle", 120, True)
def _(t):
    # waiting at the gate: the paddle across the cockpit's front, hands resting on it, looking about
    p = K((0, -0.18, 0.31), (1, 0, 0), spine=(8, 0, 0), hrot=(-10, 0, 0))
    p = breathe(p, t * 2)
    look = math.sin(t * 2 * math.pi) * 32 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (-4 + 3 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (-2, 0, look * 0.3)
    return p

@rclip("Kayak_Ride", 60, True)
def _(t):
    # gliding: the paddle held ready, a little rock of the shoulders
    w = math.sin(t * 2 * math.pi)
    p = K((0.0, -0.12, 0.42 + 0.01 * w), (1, 0, -0.03 + 0.05 * w), spine=(16, 3 * w, 0))
    return breathe(p, t, 0.6)

def stroke_pose(t, side):
    """A forward stroke on the left (side 1) or the right (-1), t 0..1: the catch by the toes, the pull back to the
    hip with the body unwinding, the blade out, and back to ready. The top hand punches across."""
    keys = [(0.00, (0.00, -0.12, 0.42), (1, 0.00, -0.03), 0),
            (0.18, (0.10, -0.20, 0.40), (1, -0.55, -0.72), 24),     # the catch: the blade in the snow ahead
            (0.48, (0.12, 0.02, 0.38), (1, 0.15, -0.78), -6),       # the pull: past the hip
            (0.66, (0.08, 0.14, 0.44), (1, 0.62, -0.30), -18),      # the exit: out behind the hip
            (1.00, (0.00, -0.12, 0.42), (1, 0.00, -0.03), 0)]
    for (t0, c0, u0, y0), (t1, c1, u1, y1) in zip(keys, keys[1:]):
        if t <= t1:
            k = ease((t - t0) / (t1 - t0))
            c = V(*c0).lerp(V(*c1), k); u = V(*u0).lerp(V(*u1), k); yaw = lerp(y0, y1, k)
            break
    c = V(c.x * side, c.y, c.z)
    if side < 0: u = V(u.x, -u.y, -u.z)               # (the right blade is the far end: the mirror of the left's)
    # (spine and hips turn about the world's axes: pitch, roll, yaw. The catch winds the stroke side's shoulder forward)
    p = K(c, u, spine=(20, 0, -yaw * side), hrot=(-4, 0, -yaw * side * 0.25))
    p["head"] = (-10, 0, yaw * side * 0.6)
    return p
CLIPS.append(("Kayak_StrokeL", 22, False, lambda t: stroke_pose(t, 1)))
CLIPS.append(("Kayak_StrokeR", 22, False, lambda t: stroke_pose(t, -1)))

def rudder_pose(side):
    """Turning: the inside blade trailed in the snow behind the hip, the body over it (side 1: a left turn)."""
    c = V(0.16 * side, 0.12, 0.40)
    u = V(1, 0.75, -0.62) if side > 0 else V(1, -0.75, 0.62)
    return K(c, u, spine=(10, 14 * side, 10 * side), hrot=(-6, 6 * side, 6 * side), head=(-6, 0, 18 * side), neck=(-2, 0, 6 * side))
CLIPS.append(("Kayak_LeanL", 30, True, lambda t: breathe(rudder_pose(1), t, 0.5)))
CLIPS.append(("Kayak_LeanR", 30, True, lambda t: breathe(rudder_pose(-1), t, 0.5)))

# the tuck: folded forward over the deck, the paddle laid along it; in the air: the paddle up over the head, both
# arms high (and the trick: the paddle twirled up overhead, the game spins it about the hands)
K_TUCK = K((0.0, -0.42, 0.30), (1, 0.0, 0.0), spine=(46, 0, 0), hrot=(14, 0, 0), neck=(-24, 0, 0), head=(-30, 0, 0))
CLIPS.append(("Kayak_Tuck", 30, True, lambda t: breathe(K_TUCK, t, 0.3)))
K_AIR = K((0.0, -0.08, 0.62), (1, 0, 0.10), spine=(4, 0, 0), hrot=(-12, 0, 0), head=(-14, 0, 0))
CLIPS.append(("Kayak_Air", 30, True, lambda t: breathe(K_AIR, t, 0.5)))
K_UP = K((0.0, 0.02, 1.02), (1, 0, 0), spine=(-2, 0, 0), hrot=(-14, 0, 0), head=(-26, 0, 0), eL=V(0.9, 0.2, 0.2), eR=V(-0.9, 0.2, 0.2))
CLIPS.append(("Kayak_PaddleUp", 30, True, lambda t: breathe(K_UP, t, 0.4)))

# ----------------------------------------------------------------------------------------------- driving
# Sat in the bucket seat, legs out to the pedals, leaning back a little, both hands on the wheel at a quarter to
# three. The wheel turns with the steering (the game turns the steering wheel the same way, by the same angle:
# CarSteerTurn), and the hands go round with it.
C_SEAT = V(0, 0.16, -0.50)
CAR_STEER_TURN = 85.0               # degrees the wheel is turned at full lock (the game's CarSteerTurn)
def on_wheel(deg, at_deg):
    """A point on the steering wheel's rim: at_deg round from its top (to the driver's left is +), the wheel turned deg."""
    xa, ya = steer_frame()
    a = math.radians(at_deg + deg)
    return STEER_AT + (ya * math.cos(a) + xa * math.sin(a)) * (STEER_R - 0.005)
def C(deg=0.0, **kw):
    p = sym(hips=C_SEAT, hrot=(-16, 0, 0), spine=(6, 0, 0), neck=(4, 0, 0), head=(6, 0, 0),
            fL=(V(0.15, -0.66, 0.36), 0, 30, 0), kL=side_pole(0.3, -0.4, 1), eL=V(0.8, 0.2, -0.6), wL=(0, 0, 0), gL=1.0)
    p["hL"] = ("abs", on_wheel(deg, 78) - V(0, 0, 0.065) + steer_axis() * 0.01)
    p["hR"] = ("abs", on_wheel(deg, -78) - V(0, 0, 0.065) + steer_axis() * 0.01)
    p["eR"] = V(-0.8, 0.2, -0.6)
    p.update(kw)
    return p

@rclip("Car_Idle", 120, True)
def _(t):
    p = C(0.0)
    # the right hand off the wheel, on the gear lever's knob by the seat; looking about
    p["hR"] = ("abs", V(-0.20, -0.16, 0.50)); p["eR"] = V(-0.9, 0.4, -0.2); p["gR"] = 0.7
    p = breathe(p, t * 2)
    look = math.sin(t * 2 * math.pi) * 34 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (4 + 3 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (2, 0, look * 0.3)
    return p

@rclip("Car_Drive", 60, True)
def _(t):
    p = breathe(C(0.0), t, 0.5)
    j = math.sin(t * 6 * math.pi) * math.sin(t * 2 * math.pi)       # the ruts under the tyres
    x, y, z = p["head"]; p["head"] = (x + 3 * j, y, z)
    return p
CLIPS.append(("Car_SteerL", 30, True, lambda t: breathe(C(CAR_STEER_TURN, spine=(6, 6, 6), head=(6, 0, 16)), t, 0.4)))
CLIPS.append(("Car_SteerR", 30, True, lambda t: breathe(C(-CAR_STEER_TURN, spine=(6, -6, -6), head=(6, 0, -16)), t, 0.4)))
C_AIR = C(0.0, hrot=(-22, 0, 0), spine=(-2, 0, 0), head=(-4, 0, 0))
CLIPS.append(("Car_Air", 30, True, lambda t: breathe(C_AIR, t, 0.5)))
# the trick: the left hand out of the window, waving (the right still on the wheel)
@rclip("Car_Wave", 30, True)
def _(t):
    p = C(0.0, spine=(4, -8, 10), head=(0, 0, 30))
    w = math.sin(t * 4 * math.pi)
    p["hL"] = ("abs", V(0.86, -0.20 + 0.06 * w, 1.20)); p["eL"] = V(0.9, 0.3, -0.4); p["gL"] = 0.0; p["wL"] = (0, 0, 20 * w)
    return p
C_JOLT = C(0.0, hrot=(-4, 0, 0), spine=(22, 0, 0), neck=(10, 0, 0), head=(14, 0, 0))
@rclip("Car_Bump", 14)
def _(t):
    if t < 0.3: return overlap(C(0.0), C_JOLT, ease(t / 0.3), ease(t / 0.3) * 0.5)
    k = (t - 0.3) / 0.7
    return overlap(C_JOLT, C(0.0), ease(k), ease(max(0, k - 0.2) / 0.8))

# ----------------------------------------------------------------------------------------------- on the bear
# Astride the bear's back, a little behind its shoulders, leaning well forward, a fist in the fur either side of
# its neck, the legs hanging round its barrel with the knees gripping. The game puts the bear under the seat
# (BEAR_SADDLE, over the Root) and carries the rider up and down with its back as it gallops. Leaning: the body
# over to the inside, the outside knee clamped in. The tuck: flat along its back, the face in the fur. The trick:
# one hand off and up, like a rodeo rider's.
BEAR_SADDLE = 1.22                  # the top of the bear's back under the seat, over the Root (RiderBodyVehicle.BearSaddle)
B_SEAT = V(0, 0.06, BEAR_SADDLE + 0.10 - 0.93)
def B(**kw):
    p = sym(hips=B_SEAT, hrot=(18, 0, 0), spine=(26, 0, 0), neck=(-16, 0, 0), head=(-22, 0, 0),
            fL=(V(0.42, -0.06, BEAR_SADDLE - 0.54), 2, 30, 0), kL=side_pole(1.0, -0.6, 0.1),
            hL=("abs", V(0.13, -0.50, BEAR_SADDLE + 0.07)), eL=V(0.8, 0.2, -0.6), wL=(0, 0, 0), gL=1.0)
    p.update(kw)
    return p
@rclip("Bear_Idle", 120, True)
def _(t):
    # sat up on the bear at the gate, a hand on its neck, the other on a knee, looking about
    p = B(hrot=(4, 0, 0), spine=(8, 0, 0), neck=(0, 0, 0), head=(0, 0, 0))
    p["hR"] = ("abs", V(-0.22, -0.12, BEAR_SADDLE + 0.20)); p["eR"] = V(-0.9, 0.4, -0.3); p["gR"] = 0.5
    p["hL"] = ("abs", V(0.10, -0.40, BEAR_SADDLE + 0.10))
    p = breathe(p, t * 2)
    look = math.sin(t * 2 * math.pi) * 30 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (2 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (0, 0, look * 0.3)
    return p
@rclip("Bear_Ride", 60, True)
def _(t): return breathe(B(), t, 0.6)
B_LEAN_L = B(hips=B_SEAT + V(0.04, 0, -0.01), hrot=(18, 4, 10), spine=(24, 10, 14), head=(-20, 0, 16),
             fL=(V(0.36, -0.02, BEAR_SADDLE - 0.48), 2, 34, 0), fR=(V(-0.30, -0.12, BEAR_SADDLE - 0.44), -2, 40, 0))
CLIPS.append(("Bear_LeanL", 30, True, lambda t: breathe(B_LEAN_L, t, 0.5)))
CLIPS.append(("Bear_LeanR", 30, True, lambda t: breathe(mirror(B_LEAN_L), t, 0.5)))
B_TUCK = B(hips=B_SEAT + V(0, 0.08, -0.06), hrot=(52, 0, 0), spine=(30, 0, 0), neck=(-30, 0, 0), head=(-34, 0, 0),
           **sym(hL=("abs", V(0.16, -0.58, BEAR_SADDLE + 0.03)), fL=(V(0.32, 0.10, BEAR_SADDLE - 0.40), 2, 10, 0)))
CLIPS.append(("Bear_Tuck", 30, True, lambda t: breathe(B_TUCK, t, 0.3)))
B_AIR = B(hrot=(10, 0, 0), spine=(18, 0, 0), head=(-14, 0, 0), **sym(fL=(V(0.40, -0.10, BEAR_SADDLE - 0.40), 2, 20, 0)))
CLIPS.append(("Bear_Air", 30, True, lambda t: breathe(B_AIR, t, 0.5)))
@rclip("Bear_Rodeo", 30, True)
def _(t):
    p = B(hrot=(0, 0, 0), spine=(-4, -6, 6), head=(-8, 0, 10))
    w = math.sin(t * 4 * math.pi)
    p["hR"] = ("rel", V(-0.20 + 0.10 * w, -0.06, 0.55)); p["eR"] = V(-0.9, 0.3, 0.0); p["gR"] = 0.2; p["wR"] = (0, 0, 25 * w)
    p["fL"] = (V(0.46, -0.10, BEAR_SADDLE - 0.36), 2, 10, 0); p["fR"] = (V(-0.46, -0.10, BEAR_SADDLE - 0.36), -2, 10, 0)
    return p
B_JOLT = B(hrot=(30, 0, 0), spine=(36, 0, 0), neck=(-6, 0, 0), head=(-6, 0, 0))
@rclip("Bear_Bump", 14)
def _(t):
    if t < 0.3: return overlap(B(), B_JOLT, ease(t / 0.3), ease(t / 0.3) * 0.5)
    k = (t - 0.3) / 0.7
    return overlap(B_JOLT, B(), ease(k), ease(max(0, k - 0.2) / 0.8))

def paddle_place(s):
    """Where the paddle is for a solved pose: between the two grips (as the game puts it)."""
    gl = s.head("LeftHand") - V(0, 0, 0.065); gr = s.head("RightHand") - V(0, 0, 0.065)
    gl = s.M["LeftHand"].translation - V(0, 0, 0.065); gr = s.M["RightHand"].translation - V(0, 0, 0.065)
    c = (gl + gr) / 2; u = (gl - gr).normalized()
    f = (FWD - u * FWD.dot(u)).normalized(); up = u.cross(f).normalized()
    # local x along the shaft (to the left blade), local y (the blade's face) toward the back, local z up
    R = Matrix((u, -f, up)).transposed()
    return Matrix.Translation(c) @ R.to_4x4()

# ----------------------------------------------------------------------------------------------- previews
# (--render: Tools/MakeRides.py's sheets, with the kayak, the car and the bear placed as the game places them)
VSHOWN = {}
BEAR_PREVIEW = {}
def vehicle_copies():
    if not VSHOWN:
        for ob in KAYAK_OBS + CAR_OBS:
            VSHOWN[ob.name] = copy_of(ob, ob.name + "Shown")
    return VSHOWN
def bear_preview(frame=11):
    """The wildlife's bear (Resources/Animals/Bear.fbx), scaled so its back is at BEAR_SADDLE under the seat."""
    if "ob" not in BEAR_PREVIEW:
        path = os.path.join(PROJECT or os.path.join(HERE, ".."), "Assets/Summit/Resources/Animals/Bear.fbx")
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
        ob = [o for o in new if o.type == 'MESH'][0]
        # (the file keys the object's own place and scale, x100, as well as the gallop's shapes: only the shapes kept)
        if ob.animation_data: ob.animation_data.action = None
        ob.parent = None
        mat = material("BearFur", (0.30, 0.20, 0.12), 0.15)
        ob.data.materials.clear(); ob.data.materials.append(MATS["BearFur"])
        BEAR_PREVIEW["ob"] = ob
    ob = BEAR_PREVIEW["ob"]
    scene.frame_set(frame)
    return ob
def vehicle_dress(s, kind, at):
    for o in vehicle_copies().values(): o.hide_render = True
    if "ob" in BEAR_PREVIEW: BEAR_PREVIEW["ob"].hide_render = True
    v = vehicle_copies()
    if kind == "kayak":
        v["Kayak"].hide_render = False; v["Kayak"].matrix_world = at
        v["Paddle"].hide_render = False; v["Paddle"].matrix_world = at @ paddle_place(s)
    elif kind == "car":
        for ob in CAR_OBS: v[ob.name].hide_render = False; v[ob.name].matrix_world = at
    elif kind == "bear":
        ob = bear_preview(); ob.hide_render = False
        k = (BEAR_SADDLE + 0.05) / 141.0
        # (its head is toward -y in the file, as the rider faces; the saddle about 25 units behind its middle)
        ob.matrix_world = at @ Matrix.Translation(V(0, 0.06, -0.05)) @ Matrix.Scale(k, 4) @ Matrix.Translation(V(0, -25.0, 0))

def vehicle_sheets(sheet, cam, front, side, back, I4, bank):
    if "kayak" in WHAT or "all" in WHAT:
        sheet("RideKayak", "kayak", [
            ("Kayak Idle", P_at("Kayak_Idle"), 0, I4, cam(front, 0.4, 4.4)),
            ("Kayak Ride", P_at("Kayak_Ride"), 0, I4, cam(front, 0.4, 4.4)),
            ("Ride (side)", P_at("Kayak_Ride"), 0, I4, cam(side, 0.4, 4.8)),
            ("Ride (behind)", P_at("Kayak_Ride"), 0, I4, cam(back, 0.4, 4.8)),
            ("Stroke L: catch", P_at("Kayak_StrokeL", 0.18), 0, I4, cam((40, 14), 0.3, 4.4)),
            ("Stroke L: pull", P_at("Kayak_StrokeL", 0.48), 0, I4, cam((40, 14), 0.3, 4.4)),
            ("Stroke R: catch", P_at("Kayak_StrokeR", 0.18), 0, I4, cam((-40, 14), 0.3, 4.4)),
            ("Rudder left", P_at("Kayak_LeanL"), 0, bank(5), cam((150, 18), 0.3, 4.6)),
            ("Rudder right", P_at("Kayak_LeanR"), 0, bank(-5), cam((-150, 18), 0.3, 4.6)),
            ("Tuck", P_at("Kayak_Tuck"), 0, I4, cam(side, 0.3, 4.4)),
            ("Air", P_at("Kayak_Air"), 0, I4, cam(front, 0.5, 4.4)),
            ("Paddle up", P_at("Kayak_PaddleUp"), 0, I4, cam(front, 0.6, 4.6)),
        ], 4)
    if "car" in WHAT or "all" in WHAT:
        sheet("RideCar", "car", [
            ("Car Idle", P_at("Car_Idle"), 0, I4, cam((35, 22), 0.6, 7.0)),
            ("Car Drive", P_at("Car_Drive"), 0, I4, cam(front, 0.6, 7.0)),
            ("Drive (side)", P_at("Car_Drive"), 0, I4, cam(side, 0.6, 7.5)),
            ("Drive (behind)", P_at("Car_Drive"), 0, I4, cam(back, 0.7, 7.5)),
            ("Steer left (through the side)", P_at("Car_SteerL"), 0, I4, cam((80, 30), 0.8, 3.4)),
            ("Steer right (through the side)", P_at("Car_SteerR"), 0, I4, cam((80, 30), 0.8, 3.4)),
            ("Air", P_at("Car_Air"), 0, I4, cam((150, 20), 0.7, 7.5)),
            ("Wave", P_at("Car_Wave"), 0, I4, cam((60, 15), 0.8, 6.0)),
        ], 4)
    if "bear" in WHAT or "all" in WHAT:
        sheet("RideBear", "bear", [
            ("Bear Idle", P_at("Bear_Idle"), 0, I4, cam((35, 12), 1.0, 6.0)),
            ("Bear Ride", P_at("Bear_Ride"), 0, I4, cam(front, 1.0, 6.0)),
            ("Ride (side)", P_at("Bear_Ride"), 0, I4, cam(side, 1.0, 6.0)),
            ("Ride (behind)", P_at("Bear_Ride"), 0, I4, cam(back, 1.1, 6.0)),
            ("Lean left", P_at("Bear_LeanL"), 0, bank(8), cam((150, 18), 1.0, 6.0)),
            ("Tuck", P_at("Bear_Tuck"), 0, I4, cam(side, 1.0, 6.0)),
            ("Air", P_at("Bear_Air"), 0, I4, cam(front, 1.0, 6.0)),
            ("Rodeo", P_at("Bear_Rodeo"), 0, I4, cam(front, 1.2, 6.4)),
        ], 4)
