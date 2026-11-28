# The mountain bike (mtb): a fat-tyred trail bike, and what the hero does on it. Run from Tools/MakeRides.py (exec'd
# after Tools/MakeRidesVehicles.py, so it has Shape, the solver and every helper); its meshes go into
# Resources/Rider/Rides.fbx with the others, its clips into HeroAnim.bytes after theirs.
#
# Axes as in MakeRides: z up, the rider faces -y, their left is +x. The Root (the origin) is 5 cm over the snow
# (RiderBody.BoardTop), so the tyres reach down to z = -0.05.
#
# The pieces, each drawn where it sits on the bike (the game moves each about its own point; RiderBodyBike.cs has the
# same numbers):
#   BikeFrame    the frame, its seatpost and saddle, the chain, the brake calipers: everything that does not move.
#   BikeFork     the fork, the stem, the bar, its grips and levers: turned about the head tube's axis (STEER_AXIS
#                through HT_BOT) to steer, and spun right round in a barspin.
#   BikeWheelF/R a fat tyre on its rim, spokes, hub and disc, drawn at its axle: spun about x through it.
#   BikeCrank    the axle, both arms and the chainring, drawn with the left arm forward (crank angle 0): turned about
#                x through BB. Crank angle a: the left arm points (0, -cos a, -sin a) (0 forward, 90 down: the way
#                it goes round pedalling forward); the right arm the other way.
#   BikePedalL/R a flat pedal, drawn at its arm's end at crank angle 0: the game puts each at its arm's end, level.
# The rider stands on the pedals, the balls of the feet over the pedals' axles, both hands on the grips. A trick
# that throws the bike about (the tabletop, the whip) turns it about BIKE_PIVOT by the turn named for it here
# (TRICK_TURN): the game turns it the same, and the clip's feet are on its pedals where that leaves them.

BIKE_OBS = []
material("BikePaint", (0.79, 0.64, 0.29), 0.6)            # (the game gives it its own gold, as it does the car's paint)

WHEEL_RB = 0.37                    # the fat tyre's outside, a 26 x 4.5 in.
TYRE_W = 0.115
RIM_RB = 0.285
AXLE_Z = WHEEL_RB - 0.05           # the axles' height: the tyres on the snow
FRONT_AXLE = V(0, -0.75, AXLE_Z)
REAR_AXLE = V(0, 0.44, AXLE_Z)
BB = V(0, 0.02, 0.27)              # the bottom bracket: 32 cm over the snow
CRANK_LEN = 0.17
Q_HALF = 0.118                     # each pedal's middle, out from the frame's centre line (a fat bike's wide stance)
HT_TOP, HT_BOT = V(0, -0.47, 0.86), V(0, -0.53, 0.70)
STEER_AXIS = (HT_TOP - HT_BOT).normalized()
ST_TOP = V(0, 0.15, 0.80)          # the seat tube's top (the seatpost goes on up from it)
SADDLE = V(0, 0.19, 0.90)
BAR_C = V(0, -0.50, 0.95)          # the middle of the bar
GRIP_X = 0.345                     # each grip's middle, out from the centre line
GRIP_L = V(GRIP_X, -0.495, 0.955)
BIKE_PIVOT = V(0, -0.42, 0.90)     # what a tabletop or a whip turns the bike about: just behind the bar's middle, so the
                                   # back of the bike swings out and up while the bar stays in the hands
# the turns a trick gives the bike (degrees: yaw about z, then roll about y, then pitch about x), as the game gives them
TRICK_TURN = {"Tabletop": (10.0, -48.0, 0.0), "Whip": (40.0, 22.0, 0.0), "Superman": (0.0, 0.0, 6.0)}

def crank_dir(a):
    """The left crank arm's way at crank angle a (degrees): 0 forward, 90 straight down."""
    r = math.radians(a)
    return V(0, -math.cos(r), -math.sin(r))
def pedal_at(sd, a):
    """The middle of a pedal (its axle) at crank angle a: sd +1 the left, -1 the right."""
    return BB + V(sd * Q_HALF, 0, 0) + crank_dir(a) * (CRANK_LEN * sd)
def trick_matrix(name, k=1.0):
    """The bike's turn in a trick (k of the way there), about BIKE_PIVOT."""
    yaw, roll, pitch = TRICK_TURN.get(name, (0.0, 0.0, 0.0))
    R = Matrix.Rotation(math.radians(yaw * k), 4, 'Z') @ Matrix.Rotation(math.radians(roll * k), 4, 'Y') @ Matrix.Rotation(math.radians(pitch * k), 4, 'X')
    return Matrix.Translation(BIKE_PIVOT) @ R @ Matrix.Translation(-BIKE_PIVOT)

# ----------------------------------------------------------------------------------------------- the bike
def tube_between(S_, a, b, r0, r1, mat, uv, n=8, ref=V(1, 0, 0)):
    S_.tube([(a, r0), (b, r1)], n, mat, uv, ref=ref)

def make_frame():
    S_ = Shape("BikeFrame")
    P, G = "BikePaint", "Gear"
    # the main triangle: fat aluminium tubes, a tapered head tube
    tube_between(S_, HT_BOT - STEER_AXIS * 0.01, HT_TOP + STEER_AXIS * 0.012, 0.030, 0.024, P, "spare", 10, ref=V(1, 0, 0))
    tube_between(S_, BB + V(0, -0.02, 0.02), HT_BOT + V(0, 0.02, 0.02), 0.027, 0.025, P, "spare", 10)       # down tube
    tube_between(S_, ST_TOP + V(0, -0.01, -0.05), HT_TOP + V(0, 0.02, -0.03), 0.020, 0.019, P, "spare", 10)   # top tube
    tube_between(S_, BB, ST_TOP, 0.022, 0.018, P, "spare", 10)                                                # seat tube
    for sx in (1, -1):
        tube_between(S_, BB + V(sx * 0.045, 0.02, 0.0), REAR_AXLE + V(sx * 0.085, -0.03, 0.0), 0.012, 0.010, P, "spare", 8)   # chainstays
        tube_between(S_, ST_TOP + V(sx * 0.02, 0.0, -0.08), REAR_AXLE + V(sx * 0.085, -0.02, 0.02), 0.010, 0.009, P, "spare", 8)  # seatstays
        obox(S_, REAR_AXLE + V(sx * 0.088, 0, 0), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.006, 0.022, 0.022, P, "spare")   # dropouts
    # the bottom bracket shell
    S_.tube([(BB + V(0.05, 0, 0), 0.024), (BB + V(-0.05, 0, 0), 0.024)], 10, P, "spare", ref=V(0, 0, 1))
    # the seatpost (a dropper's black stanchion) and the saddle
    tube_between(S_, ST_TOP - V(0, -0.0, 0.02), SADDLE + V(0, -0.01, -0.03), 0.0155, 0.0155, G, "binding", 8)
    sad = [(-0.12, 0.016), (-0.07, 0.030), (0.0, 0.060), (0.06, 0.072), (0.10, 0.070), (0.12, 0.052)]
    rows = []
    for yy, hw in sad:
        y = SADDLE.y + yy; z = SADDLE.z - 0.004 * (yy / 0.12) ** 2
        rows.append([S_.vert(V(-hw, y, z - 0.012)), S_.vert(V(hw, y, z - 0.012)), S_.vert(V(hw * 0.92, y, z + 0.018)), S_.vert(V(-hw * 0.92, y, z + 0.018))])
    S_.rings(rows, ["Gear", "Gear", "Gear", "Gear"], ["base", "deckedge", "base", "deckedge"])
    # the chain: its top and bottom runs, chainring to the rear cog (on the right, the drive side)
    cx = -0.075
    for dz, r in ((0.075, 0.030), (-0.075, -0.030)):
        tube_between(S_, BB + V(cx, 0.0, dz), REAR_AXLE + V(cx, 0.0, r), 0.005, 0.005, G, "deckedge", 5)
    # the rear derailleur hanging under the cog, the brake calipers by the discs (on the left)
    obox(S_, REAR_AXLE + V(-0.09, 0.03, -0.07), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.012, 0.03, 0.04, G, "binding")
    obox(S_, REAR_AXLE + V(0.075, -0.06, 0.06), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.012, 0.03, 0.018, G, "binding")
    return S_.build(smooth=True)

def make_fork():
    S_ = Shape("BikeFork")
    G = "Gear"
    # the steerer up through the head tube, the crown, two fat stanchions down to the axle, black
    crown = HT_BOT - STEER_AXIS * 0.03
    tube_between(S_, crown, HT_TOP + STEER_AXIS * 0.03, 0.018, 0.018, G, "binding", 8)
    obox(S_, crown, V(1, 0, 0), STEER_AXIS.cross(V(1, 0, 0)).normalized(), STEER_AXIS, 0.095, 0.03, 0.018, G, "binding")
    for sx in (1, -1):
        top = crown + V(sx * 0.085, 0, -0.01)
        low = FRONT_AXLE + V(sx * 0.085, 0, 0)
        mid = top.lerp(low, 0.45)
        S_.tube([(top, 0.019), (mid, 0.019), (mid + (low - mid) * 0.05, 0.024), (low + V(0, 0, 0.02), 0.022)], 8, G, "binding", ref=V(1, 0, 0))
        obox(S_, low, V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.008, 0.02, 0.02, G, "binding")
    # the front caliper on the left leg
    obox(S_, FRONT_AXLE + V(0.075, 0.05, 0.06), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.012, 0.03, 0.018, G, "binding")
    # the stem and the riser bar, the grips and the brake levers
    stem0 = HT_TOP + STEER_AXIS * 0.03
    tube_between(S_, stem0, BAR_C + V(0, 0.0, -0.01), 0.018, 0.017, G, "binding", 8)
    bar = []
    for t in [i / 12 for i in range(13)]:
        x = (t * 2 - 1) * 0.39
        rise = 0.022 * min(1.0, abs(x) / 0.14) if abs(x) > 0.02 else 0.0
        bar.append((BAR_C + V(x, 0.004 * abs(x) / 0.39, rise), 0.0115 if abs(x) < 0.3 else 0.011))
    S_.tube(bar, 8, G, "binding", ref=V(0, 1, 0))
    for sx in (1, -1):
        g0 = V(sx * (GRIP_X - 0.065), GRIP_L.y, GRIP_L.z); g1 = V(sx * (GRIP_X + 0.065), GRIP_L.y, GRIP_L.z)
        S_.tube([(g0, 0.016), (g0 + V(sx * 0.01, 0, 0), 0.017), (g1 - V(sx * 0.01, 0, 0), 0.017), (g1, 0.016)], 8, G, "strap", ref=V(0, 1, 0))
        # the lever: a blade from the clamp inboard of the grip, forward and down
        clamp = V(sx * (GRIP_X - 0.085), GRIP_L.y, GRIP_L.z)
        obox(S_, clamp + V(0, -0.005, 0.012), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.012, 0.016, 0.014, G, "binding")
        obox(S_, clamp + V(sx * 0.045, -0.045, 0.002), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.045, 0.006, 0.004, G, "zip")
    return S_.build(smooth=True)

def make_bike_wheel(name, at):
    """A fat tyre with a deep knobbly tread on a black rim, 32 spokes, a hub and a disc on the left."""
    S_ = Shape(name)
    hw = TYRE_W / 2
    def ring(r, x, n=28): return [V(at.x + x, at.y + r * math.cos(2 * math.pi * k / n), at.z + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    prof = [(RIM_RB - 0.005, -hw * 0.55), (RIM_RB + 0.03, -hw), (WHEEL_RB - 0.03, -hw - 0.004), (WHEEL_RB - 0.006, -hw * 0.72),
            (WHEEL_RB, -hw * 0.3), (WHEEL_RB, hw * 0.3), (WHEEL_RB - 0.006, hw * 0.72), (WHEEL_RB - 0.03, hw + 0.004),
            (RIM_RB + 0.03, hw), (RIM_RB - 0.005, hw * 0.55)]
    rows = [[S_.vert(p) for p in ring(r, x)] for r, x in prof]
    S_.rings(rows + [rows[0]], "Gear", "sole", cap0=False, cap1=False)
    # the knobs: blocks proud of the crown and the shoulders, staggered
    ax = V(1, 0, 0)
    for k in range(28):
        a = 2 * math.pi * (k + 0.5) / 28
        rad = V(0, math.cos(a), math.sin(a)); tan = V(0, -math.sin(a), math.cos(a))
        for j, xm in enumerate((-hw * 0.62, 0.0, hw * 0.62)):
            if (k + j) % 2: continue
            obox(S_, at + ax * xm + rad * (WHEEL_RB + 0.004 - (0.01 if j != 1 else 0.0)) + tan * 0.0, ax, tan, rad, 0.016, 0.016, 0.011, "Gear", "sole")
    # the rim: a black hoop, the spokes, the hub
    slab_ring(S_, at + V(hw * 0.5, 0, 0), V(1, 0, 0), RIM_RB, RIM_RB - 0.03, hw, 28, "Gear", "base")
    for i in range(16):
        a = 2 * math.pi * i / 16
        d = V(0, math.cos(a), math.sin(a))
        for sx in (1, -1):
            tube_between(S_, at + V(sx * 0.035, 0, 0) + d.cross(ax) * 0.012 * sx, at + d * (RIM_RB - 0.03) + V(sx * 0.01, 0, 0), 0.0022, 0.0022, "Gear", "zip", 4)
    S_.tube([(at + V(0.06, 0, 0), 0.022), (at + V(-0.06, 0, 0), 0.022)], 10, "Gear", "binding", ref=V(0, 0, 1))
    # the disc brake's rotor on the left, a steel ring with a black spider
    slab_ring(S_, at + V(0.07, 0, 0), V(1, 0, 0), 0.09, 0.065, 0.002, 24, "Gear", "zip")
    slab_ring(S_, at + V(0.069, 0, 0), V(1, 0, 0), 0.065, 0.024, 0.002, 12, "Gear", "binding")
    return S_.build(smooth=True)

def make_crank():
    S_ = Shape("BikeCrank")
    G = "Gear"
    S_.tube([(BB + V(Q_HALF - 0.015, 0, 0), 0.012), (BB + V(-Q_HALF + 0.015, 0, 0), 0.012)], 8, G, "binding", ref=V(0, 0, 1))
    for sd in (1, -1):
        a = BB + V(sd * (Q_HALF - 0.02), 0, 0)
        b = pedal_at(sd, 0.0) - V(sd * 0.02, 0, 0)
        obox(S_, (a + b) / 2, V(1, 0, 0), (b - a).normalized(), V(1, 0, 0).cross((b - a).normalized()), 0.009, (b - a).length / 2 + 0.015, 0.016, G, "binding")
    # the chainring (on the right) and its spider
    ringc = BB + V(-0.075, 0, 0)
    slab_ring(S_, ringc, V(-1, 0, 0), 0.085, 0.07, 0.004, 32, G, "gold")
    for i in range(4):
        a = 2 * math.pi * (i + 0.5) / 4
        d = V(0, math.cos(a), math.sin(a))
        obox(S_, ringc + d * 0.04, V(1, 0, 0), d, V(1, 0, 0).cross(d), 0.005, 0.035, 0.008, G, "binding")
    return S_.build(smooth=True)

def make_pedal(sd):
    S_ = Shape("BikePedal" + ("L" if sd > 0 else "R"))
    c = pedal_at(sd, 0.0)
    obox(S_, c, V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.05, 0.052, 0.009, "Gear", "binding")
    for i in range(4):
        for j in range(2):
            obox(S_, c + V((i - 1.5) * 0.026, (j - 0.5) * 0.09, 0.0), V(1, 0, 0), V(0, 1, 0), V(0, 0, 1), 0.003, 0.003, 0.013, "Gear", "zip")
    S_.tube([(c - V(sd * 0.05, 0, 0), 0.006), (c - V(sd * 0.075, 0, 0), 0.006)], 6, "Gear", "zip", ref=V(0, 0, 1))
    return S_.build(smooth=True)

BIKE_FRAME_OB, BIKE_FORK_OB = make_frame(), make_fork()
BIKE_WHEEL_OBS = [make_bike_wheel("BikeWheelF", FRONT_AXLE), make_bike_wheel("BikeWheelR", REAR_AXLE)]
BIKE_CRANK_OB = make_crank()
BIKE_PEDAL_OBS = [make_pedal(1), make_pedal(-1)]
BIKE_OBS = [BIKE_FRAME_OB, BIKE_FORK_OB, BIKE_CRANK_OB] + BIKE_WHEEL_OBS + BIKE_PEDAL_OBS
for ob in BIKE_OBS: ob.hide_render = True

# ----------------------------------------------------------------------------------------------- on the bike
# Stood on the pedals (the attack position): knees and elbows bent, the hips back over the saddle's nose, the chest
# low over the bar, the head up looking down the hill. The feet are on the pedals, the balls of the feet over the
# axles (so the game can read the crank's angle off where the feet are); the hands on the grips (and the game holds
# them there as the bar turns: RiderBodyBike).
PED_BEHIND, PED_ABOVE = 0.085, 0.107       # the ankle from a pedal's axle: behind it (+y) and over it
def on_pedal(sd, a, m=None, pitch=4.0):
    p = pedal_at(sd, a)
    if m is not None: p = m @ p
    return (p + V(0, PED_BEHIND, PED_ABOVE), 0, pitch, 0)
def grip(sd, m=None):
    g = V(sd * GRIP_X, GRIP_L.y, GRIP_L.z) - V(0, 0, 0.012)
    return ("abs", m @ g if m is not None else g)
ATTACK_HIPS = V(0, 0.13, 0.13)

def hand_err(P):
    """How far the solved hands are from the grips the pose holds (m, the worse of the two)."""
    s = solve(P); e = 0.0
    for side in ("Left", "Right"):
        h = P.get("h" + side[0])
        if h is None or h[0] != "abs": continue
        e = max(e, (s.M[side + "Hand"].translation - (Vector(h[1]) + V(0, 0, 0.065))).length)
    return e
FIT_MEMO = {}
def fitted(P, key=None):
    """The pose with the hips moved and the body pitched as little as will put both hands on their grips (the arms only
    reach so far): searched over the hips forward and down and the body leaning further on."""
    if key is not None and key in FIT_MEMO:
        dy, dz, dp = FIT_MEMO[key]
        return over(P, hips=P["hips"] + V(0, dy, dz), hrot=(P["hrot"][0] + dp, P["hrot"][1], P["hrot"][2]))
    best = (hand_err(P) * 4.0, 0.0, 0.0, 0.0)
    if best[0] > 0.06:
        for dp in (0, 6, 12, 18, 26):
            for dy in (0.0, -0.04, -0.08, -0.12, -0.16, -0.20, -0.26):
                for dz in (0.0, -0.04, -0.08, -0.12, 0.04):
                    Q = over(P, hips=P["hips"] + V(0, dy, dz), hrot=(P["hrot"][0] + dp, P["hrot"][1], P["hrot"][2]))
                    c = hand_err(Q) * 4.0 + 0.10 * (abs(dy) + abs(dz)) + 0.002 * dp
                    if c < best[0]: best = (c, dy, dz, dp)
    _, dy, dz, dp = best
    if key is not None: FIT_MEMO[key] = (dy, dz, dp)
    return over(P, hips=P["hips"] + V(0, dy, dz), hrot=(P["hrot"][0] + dp, P["hrot"][1], P["hrot"][2]))
def BK(a=0.0, m=None, **kw):
    """On the bike at crank angle a; m: the bike's own turn (a trick), the feet and hands on it as it is."""
    p = dict(hips=ATTACK_HIPS, hrot=(36, 0, 0), spine=(14, 0, 0), neck=(-16, 0, 0), head=(-22, 0, 0),
             fL=on_pedal(1, a, m), fR=on_pedal(-1, a, m), kL=side_pole(0.35, -1, 0.15), kR=side_pole(-0.35, -1, 0.15),
             hL=grip(1, m), hR=grip(-1, m), eL=V(0.9, 0.35, -0.2), eR=V(-0.9, 0.35, -0.2), wL=(0, 0, 0), wR=(0, 0, 0), gL=1.0, gR=1.0)
    p.update(kw)
    return p

@rclip("Bike_Idle", 120, True)
def _(t):
    # stopped at the gate: sat on the saddle's nose, the left foot down on the snow, the right on its pedal at the
    # top of the stroke ready to push off, a hand on the bar each side, looking about
    p = BK(-60.0, hips=V(0.0, 0.22, 0.02), hrot=(8, 0, -4), spine=(4, 0, 0), neck=(0, 0, 0), head=(0, 0, 0))
    p["fL"] = (V(0.30, 0.02, -0.05 + 0.095), 6, 0, 0); p["kL"] = side_pole(0.6, -1, 0)
    p = fitted(p, "idle")
    p = breathe(p, t * 2)
    look = math.sin(t * 2 * math.pi) * 30 + math.sin(t * 4 * math.pi + 1) * 6
    p["head"] = (2 * math.sin(t * 2 * math.pi + 2), 0, look); p["neck"] = (0, 0, look * 0.3)
    return p

@rclip("Bike_Ride", 60, True)
def _(t): return breathe(fitted(BK(0.0), "ride"), t, 0.6)

@rclip("Bike_Pedal", 30, True)
def _(t):
    # one turn of the cranks stood up, the hips rocking a little over the downstroke's side
    a = 360.0 * t
    p = BK(a, hips=ATTACK_HIPS + V(0.015 * math.sin(math.radians(a)), -0.02, 0.02), hrot=(28, 0, 4 * math.sin(math.radians(a))),
           spine=(12, 0, -3 * math.sin(math.radians(a))))
    return fitted(p, "pedal%d" % int(round(a)))

# turning: the bike leant under the rider, the outside pedal down and weighted, the inside knee out
BK_LEAN_L = BK(-90.0, hips=ATTACK_HIPS + V(0.05, 0.0, -0.02), hrot=(30, 6, 8), spine=(12, 6, 8), head=(-18, 0, 14),
               kL=side_pole(0.9, -0.8, 0.1))
BK_LEAN_L = fitted(BK_LEAN_L, "leanL")
BK_LEAN_R = fitted(over(mirror(BK_LEAN_L), fL=on_pedal(1, 90.0), fR=on_pedal(-1, 90.0)), "leanR")
CLIPS.append(("Bike_LeanL", 30, True, lambda t: breathe(BK_LEAN_L, t, 0.5)))
CLIPS.append(("Bike_LeanR", 30, True, lambda t: breathe(BK_LEAN_R, t, 0.5)))
BK_TUCK = BK(0.0, hips=ATTACK_HIPS + V(0, 0.04, -0.08), hrot=(52, 0, 0), spine=(22, 0, 0), neck=(-24, 0, 0), head=(-30, 0, 0),
             eL=V(0.9, 0.5, -0.1), eR=V(-0.9, 0.5, -0.1))
CLIPS.append(("Bike_Tuck", 30, True, lambda t: breathe(BK_TUCK, t, 0.3)))
BK_AIR = fitted(BK(0.0, hips=ATTACK_HIPS + V(0, 0.02, 0.04), hrot=(24, 0, 0), spine=(8, 0, 0), head=(-16, 0, 0)), "air")
CLIPS.append(("Bike_Air", 30, True, lambda t: breathe(BK_AIR, t, 0.5)))
BK_SQUASH = fitted(BK(0.0, hips=ATTACK_HIPS + V(0, 0.05, -0.16), hrot=(40, 0, 0), spine=(18, 0, 0), head=(-26, 0, 0)), "squash")
BK_BASE = fitted(BK(0.0), "ride")
@rclip("Bike_Bump", 14)
def _(t):
    if t < 0.3: return overlap(BK_BASE, BK_SQUASH, ease(t / 0.3), ease(t / 0.3) * 0.6)
    k = (t - 0.3) / 0.7
    return overlap(BK_SQUASH, BK_BASE, ease(k), ease(max(0, k - 0.2) / 0.8))

# ---- the tricks (the order the ride's Grabs name them: Tabletop, Whip, Superman, No-Hander, Barspin)
# the tabletop: the bike pulled up flat beside the hips, the knees tucked over to that side, the bar to the chest
TT = trick_matrix("Tabletop")
@rclip("Bike_Tabletop", 30, True)
def _(t):
    p = BK(0.0, TT, hips=ATTACK_HIPS + V(-0.06, -0.02, 0.10), hrot=(22, -14, -18), spine=(10, -8, -10), head=(-12, 0, 14),
           kL=side_pole(-0.6, -0.8, 0.3), kR=side_pole(-0.9, -0.6, 0.2), eL=V(0.6, 0.5, -0.4), eR=V(-0.9, 0.3, -0.2))
    return breathe(fitted(p, "tabletop"), t, 0.4)
# the whip: the back of the bike kicked out sideways and laid over, the hips turned with it, the head down the line
WH = trick_matrix("Whip")
@rclip("Bike_Whip", 30, True)
def _(t):
    p = BK(0.0, WH, hips=ATTACK_HIPS + V(-0.04, 0.02, 0.06), hrot=(26, 22, -10), spine=(12, -16, -6), head=(-14, 0, -24),
           kL=side_pole(-0.3, -1, 0.2), kR=side_pole(-0.8, -0.8, 0.1))
    return breathe(fitted(p, "whip"), t, 0.4)
# the superman: both feet off the pedals and the body stretched out flat behind the bar, legs together
SM = trick_matrix("Superman")
@rclip("Bike_Superman", 30, True)
def _(t):
    p = BK(0.0, SM, hips=V(0, 0.42, 0.30), hrot=(78, 0, 0), spine=(4, 0, 0), neck=(-30, 0, 0), head=(-34, 0, 0),
           eL=V(0.9, 0.2, -0.3), eR=V(-0.9, 0.2, -0.3))
    p["fL"] = (V(0.07, 1.30, 0.62), 0, -60, 0); p["fR"] = (V(-0.07, 1.30, 0.64), 0, -60, 0)
    p["kL"] = side_pole(0.1, 0.2, -1); p["kR"] = side_pole(-0.1, 0.2, -1)
    return breathe(fitted(p, "superman"), t, 0.3)
# the no-hander: both hands off the bar and out wide, the knees clamped on the saddle
@rclip("Bike_NoHander", 30, True)
def _(t):
    w = math.sin(t * 2 * math.pi)
    p = BK(0.0, hips=ATTACK_HIPS + V(0, 0.0, 0.08), hrot=(10, 0, 0), spine=(-6, 0, 0), head=(-8, 0, 0),
           kL=side_pole(-0.2, -1, 0.1), kR=side_pole(0.2, -1, 0.1))
    p["hL"] = ("rel", V(0.55, -0.10, 0.25 + 0.03 * w)); p["hR"] = ("rel", V(-0.55, -0.10, 0.25 - 0.03 * w))
    p["eL"] = V(0.4, 0.4, -0.8); p["eR"] = V(-0.4, 0.4, -0.8); p["gL"] = 0.1; p["gR"] = 0.1; p["wL"] = (0, 0, 20); p["wR"] = (0, 0, 20)
    return p
# the barspin: the hands let go just over the grips as the bar spins round under them, the eyes on it
@rclip("Bike_Barspin", 30, True)
def _(t):
    p = BK(0.0, hips=ATTACK_HIPS + V(0, -0.02, 0.06), hrot=(30, 0, 0), spine=(16, 0, 0), neck=(-6, 0, 0), head=(4, 0, 0))
    p["hL"] = ("abs", V(GRIP_X + 0.04, GRIP_L.y + 0.04, GRIP_L.z + 0.09)); p["hR"] = ("abs", V(-GRIP_X - 0.04, GRIP_L.y + 0.04, GRIP_L.z + 0.09))
    p["gL"] = 0.15; p["gR"] = 0.15
    return p

# ----------------------------------------------------------------------------------------------- previews
BIKE_SHOWN = {}
def bike_copies():
    if not BIKE_SHOWN:
        for ob in BIKE_OBS: BIKE_SHOWN[ob.name] = copy_of(ob, ob.name + "Shown")
    return BIKE_SHOWN
def crank_of(s):
    """The crank's angle where the left foot is (the game reads it off the feet the same way)."""
    an = s.M["LeftFoot"].translation - V(0, PED_BEHIND, PED_ABOVE) - BB - V(Q_HALF, 0, 0)
    return math.degrees(math.atan2(-an.z, -an.y))
_vehicle_dress_before_bike = vehicle_dress
def vehicle_dress(s, kind, at):
    _vehicle_dress_before_bike(s, kind, at)
    for o in bike_copies().values(): o.hide_render = True
    if not kind or not kind.startswith("bike"): return
    trick = kind[5:] if len(kind) > 5 else ""
    m = at @ trick_matrix(trick) if trick in TRICK_TURN else at
    v = bike_copies()
    for ob in (BIKE_FRAME_OB, BIKE_FORK_OB) + tuple(BIKE_WHEEL_OBS): v[ob.name].hide_render = False; v[ob.name].matrix_world = m
    a = crank_of(s) if trick not in ("Superman",) else 0.0
    rot = Matrix.Translation(BB) @ Matrix.Rotation(math.radians(-a), 4, 'X') @ Matrix.Translation(-BB)
    v["BikeCrank"].hide_render = False; v["BikeCrank"].matrix_world = m @ rot
    for sd, nm in ((1, "BikePedalL"), (-1, "BikePedalR")):
        v[nm].hide_render = False
        v[nm].matrix_world = m @ Matrix.Translation(pedal_at(sd, a) - pedal_at(sd, 0.0))

_vehicle_sheets_before_bike = vehicle_sheets
def vehicle_sheets(sheet, cam, front, side, back, I4, bank):
    _vehicle_sheets_before_bike(sheet, cam, front, side, back, I4, bank)
    if "bike" in WHAT or "all" in WHAT:
        sheet("RideBike", "bike", [
            ("Bike Idle", P_at("Bike_Idle"), 0, I4, cam((35, 12), 0.6, 4.6)),
            ("Bike Ride", P_at("Bike_Ride"), 0, I4, cam(front, 0.6, 4.6)),
            ("Ride (side)", P_at("Bike_Ride"), 0, I4, cam(side, 0.6, 4.6)),
            ("Pedal: a quarter", P_at("Bike_Pedal", 0.25), 0, I4, cam(side, 0.6, 4.6)),
            ("Pedal: three quarters", P_at("Bike_Pedal", 0.75), 0, I4, cam(side, 0.6, 4.6)),
            ("Lean left", P_at("Bike_LeanL"), 0, bank(14), cam((150, 18), 0.6, 4.6)),
            ("Tuck", P_at("Bike_Tuck"), 0, I4, cam(side, 0.6, 4.6)),
            ("Air", P_at("Bike_Air"), 0, I4, cam(front, 0.6, 4.6)),
        ], 4)
        # the tricks, each with the bike turned as the game turns it for that one
        for ti, P, trick, c in (("Tabletop", P_at("Bike_Tabletop"), "Tabletop", (40, 20)), ("Whip", P_at("Bike_Whip"), "Whip", (150, 20)),
                                ("Superman", P_at("Bike_Superman"), "Superman", (88, 10)), ("NoHander", P_at("Bike_NoHander"), "", (28, 8)),
                                ("Barspin", P_at("Bike_Barspin"), "", (28, 20)), ("Bump", P_at("Bike_Bump", 0.3), "", (88, 5))):
            sheet("RideBike_" + ti, "bike" + (":" + trick if trick else ""), [(ti, P, 0, I4, cam(c, 0.7, 4.8)), (ti + " (front)", P, 0, I4, cam(front, 0.7, 4.8))], 2)

# ---- a check: each clip's hands where they hold the grips (the IK can only reach so far)
if "--bike-check" in sys.argv:
    for nm, fr, lp, fn in CLIPS:
        if not nm.startswith("Bike_") or nm in ("Bike_NoHander", "Bike_Barspin"): continue
        worst = 0.0
        for f in range(0, fr + 1, max(1, fr // 6)):
            P = fn(f / fr); s = solve(P)
            for sd, side in ((1, "Left"), (-1, "Right")):
                h = P["h" + side[0]]
                want = (Vector(h[1]) + V(0, 0, 0.065)) if h[0] == "abs" else None
                if want is None: continue
                worst = max(worst, (s.M[side + "Hand"].translation - want).length)
        fw = 0.0
        for f in range(0, fr + 1, max(1, fr // 6)):
            P = fn(f / fr); s = solve(P)
            for side in ("Left", "Right"):
                fp = P.get("f" + side[0])
                if fp is not None: fw = max(fw, (s.M[side + "Foot"].translation - fp[0]).length)
        print("BIKECHECK %-14s hands off their grips by up to %.3f m, feet off theirs %.3f m" % (nm, worst, fw))
