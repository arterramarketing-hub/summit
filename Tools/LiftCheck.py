# Checks the chairlifts' line numerically, since the game cannot be run where this was written.
#   python3 Tools/LiftCheck.py                 a few hundred mountains, a summary of every lift on them
#   python3 Tools/LiftCheck.py 4242            one mountain (the day's seed), every lift, tower and span listed
#   python3 Tools/LiftCheck.py 1 2000          seeds 1..2000 (uses every core)
#   python3 Tools/LiftCheck.py budget 1 60     the triangles and draw calls the lifts add, rider at every 15 m of 60 mountains
# (Tools/LiftHarness runs the same checks on the game's own C#, which is the last word; this is the quick one, kept
# for the preview pictures. It does not know the launch ramps or the cable grind, nor each cycle's second line
# (LiftNetwork.cs): those are checked by the harness only (python3 Tools/LiftHarness/run.py 1 3000). Its budget is of
# the first lines with the first models; what the lifts cost now is counted in the game itself by LiftShot.Run.)
#
# The first half is a port of the mountain (Slope, Course, Ridge, Cliffs: only what a lift's ground needs),
# the second a port of LiftLine.cs, line for line, so what is checked here is what the game lays out. The seed's
# offsets are worked out in 32-bit floats as the game does; the rest is in doubles, which moves the snow by
# less than a millimetre.
#
# What it confirms for every lift: no tower or station on or near the piste, on a cliff, in a ridge's canyons or
# under a deadly wall; the lowest thing on every chair (a rider's skis) clear of the snow by the margins LiftLine
# asks for, along both cables, sampled every 1.5 m; tower heights and spacing; and where the line crosses the piste.

import math, struct, sys

# ---------------------------------------------------------------- the mountain (Slope.cs)

FALL, QUAD, PISTE_HALF = 0.72, 1.5, 20.0
VERTICAL, RUNOUT = 3048.0, 70.0
FINISH = VERTICAL / FALL
DROP_SEG = 95.0
PAD_LIP = 0.0
KICK_PERIOD, KICK_RAMP, KICK_HEIGHT, KICK_DROP, KICK_WIDTH = 130.0, 12.0, 2.4, 5.0, 9.0

seed = 0
STRICT = True   # (the preview pictures switch this off to draw the snow a little past a lift's ends)
ph = ox = oz = 0.0


def f32(x):
    return struct.unpack("f", struct.pack("f", x))[0]


def repeat32(t, length):
    t = f32(t)
    return max(0.0, min(length, f32(t - f32(math.floor(f32(t / length)) * length))))


def init(s):
    global seed, ph, ox, oz
    seed = s
    ph = f32((s % 628) / 100.0)
    ox = repeat32(f32(s) * f32(37.31), 211.0)
    oz = repeat32(f32(s) * f32(19.77), 199.0)
    _memo.clear()


M32 = 0xFFFFFFFF


def hash01(x, y, salt):
    h = ((x + salt * 7919 + seed * 1013) * 374761393 + (y - salt * 104729 + seed * 7) * 668265263) & M32
    h = ((h ^ (h >> 13)) * 1274126177) & M32
    h ^= h >> 16
    return h / 4294967296.0


def noise(x, y):
    x += ox; y += oz
    x0, y0 = math.floor(x), math.floor(y)
    fx, fy = x - x0, y - y0
    sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
    a, b, c, d = hash01(x0, y0, 0), hash01(x0 + 1, y0, 0), hash01(x0, y0 + 1, 0), hash01(x0 + 1, y0 + 1, 0)
    ab = a + (b - a) * sx
    return ab + ((c + (d - c) * sx) - ab) * sy


def ridged(x, y):
    n = 1 - abs(noise(x, y) * 2 - 1)
    return n * n


def clamp01(v):
    return 0.0 if v < 0 else 1.0 if v > 1 else v


def smooth01(t):
    t = clamp01(t)
    return t * t * (3 - 2 * t)


def centre(z):
    return 26 * math.sin(z * 0.0047 + ph) + 11 * math.sin(z * 0.0113 + 1.3 + ph * 1.7) + 5 * math.sin(z * 0.0240 + 0.4 + ph * 2.3)


_memo = {}


def fall_line(z):
    if z > FINISH:
        u = z - FINISH
        return -FINISH * FALL - FALL * RUNOUT * (1 - math.exp(-u / RUNOUT))
    key = ("f", z)
    if key in _memo:
        return _memo[key]
    y = -z * FALL
    k = clamp01((FINISH - z) / 250.0)
    y += k * (6 * (noise(z * 0.014, 13.7) - 0.5) + 2.6 * (noise(z * 0.037, 29.1) - 0.5) + 0.8 * (noise(z * 0.09, 41.3) - 0.5) + drops(z))
    if len(_memo) > 200000:
        _memo.clear()
    _memo[key] = y
    return y


def drops(z):
    if z < 90:
        return 0.0
    s = math.floor(z / DROP_SEG)
    if hash01(s, 11, 41) < 0.25:
        return 0.0
    zs = s * DROP_SEG
    if ridge_has(zs) or ridge_has(zs + DROP_SEG):
        return 0.0
    if jump_near(zs + DROP_SEG * 0.5, DROP_SEG * 0.5 + 20) is not None:
        return 0.0
    u = z / DROP_SEG - s
    H = 2 + 7 * hash01(s, 12, 41)
    u0, w = 0.35 + 0.4 * hash01(s, 13, 41), 0.09 + 0.08 * hash01(s, 14, 41)
    t = clamp01((u - (u0 - w)) / (2 * w))
    return H * (u - t * t * (3 - 2 * t))


def height(x, z):
    """Slope.Height for z > 0, outside the ridges' canyons (a lift never goes there: checked)."""
    y = fall_line(z)
    a = x - centre(z); aa = abs(a)
    y += 0.0035 * a * a / (1 + a * a / 6400.0)
    y += max(-80.0, min(80.0, a)) * 0.07 * (noise(z * 0.009, 51.7) - 0.5) * 2
    wild = smooth01((aa - PISTE_HALF - 6) / 55.0)
    if wild > 0:
        y += wild * (30 * (ridged(x * 0.0075 + 3.1, z * 0.0022) - 0.4) + 12 * (noise(x * 0.017 + 9, z * 0.006) - 0.5)
                     + 4 * (noise(x * 0.05, z * 0.03 + 7) - 0.5))
    rough = clamp01((aa - PISTE_HALF) / 10.0)
    y += (noise(x * 0.045, z * 0.045) - 0.5) * 2.6 * rough
    y += (noise(x * 0.011 + 50, z * 0.009) - 0.5) * 7
    y += kicker(x, z)
    y += jump_height(x, z) + wall_cut(x, z)
    if ridge_depth(z) > 0:
        if STRICT:
            raise RuntimeError("height asked for inside a ridge's canyons at z=%.1f (not ported: a lift must not be there)" % z)
    return y


def normal_y(x, z):
    e = 0.75
    gx = (height(x + e, z) - height(x - e, z)) / (2 * e)
    gz = (height(x, z + e) - height(x, z - e)) / (2 * e)
    return 1.0 / math.sqrt(gx * gx + 1 + gz * gz)


def kicker(x, z):
    sec = section(z)
    if sec == "Park":
        zs = cycle_base(z) + PARK_AT
        for at in (78.0, 166.0):
            hk = kick_shape(x, z, zs + at, centre(zs + at), 1.15)
            if hk > 0:
                return hk
        return 0.0
    if sec != "Cruise":
        return 0.0
    cell = math.floor((z - 60) / KICK_PERIOD)
    if cell < 0:
        return 0.0
    if hash01(int(cell), 3, 9) < 0.25:
        return 0.0
    side = (hash01(int(cell), 4, 9) - 0.5) * 18
    z0 = 60 + cell * KICK_PERIOD + 30
    if z0 > FINISH - 260:
        return 0.0
    if section(z0) != "Cruise" or section(z0 + KICK_RAMP + KICK_DROP) != "Cruise":
        return 0.0
    if jump_near(z0, 30) is not None:
        return 0.0
    return kick_shape(x, z, z0, centre(z0) + side, 1.0)


def kick_shape(x, z, z0, cx, scale):
    ramp, drop = KICK_RAMP * scale, KICK_DROP * scale
    u = z - z0
    if u < 0 or u > ramp + drop:
        return 0.0
    w = abs(x - cx) / (KICK_WIDTH * scale)
    if w >= 1:
        return 0.0
    m = 1 - w * w; m *= m
    if u < ramp:
        t = u / ramp; h = t * t
    else:
        t = (u - ramp) / drop; h = 1 - t * t * (3 - 2 * t)
    return h * KICK_HEIGHT * scale * m


# ---------------------------------------------------------------- the course (Course.cs)

FIRST, CYCLE = 60.0, 900.0
PARK_AT, DANGER_AT, PIPE_AT, PIPE_END = 200.0, 380.0, 520.0, 760.0
CLEAR_FROM = FINISH - 260.0


def local(z):
    return (z - FIRST) % CYCLE


def cycle_of(z):
    return math.floor((z - FIRST) / CYCLE)


def cycle_base(z):
    return FIRST + cycle_of(z) * CYCLE


def section(z):
    if z < FIRST:
        return "Start"
    if z > FINISH - 220:
        return "Cruise"
    u = local(z)
    if u < PARK_AT:
        return "Cruise"
    if u < DANGER_AT:
        return "Park"
    if u < PIPE_AT:
        return "Danger"
    if ridge_has(z):
        return "Ridge"
    return "Cruise"


# ---------------------------------------------------------------- ridges (Ridge.cs: where they are, not their shape)

RIDGE_AT, RAMP_IN, FADE_BEFORE, FADE_AFTER = PIPE_AT, 80.0, 140.0, 240.0


def ridge_owner(z):
    return cycle_of(z) if local(z) >= RIDGE_AT else cycle_of(z) - 1


def ridge_local(z):
    return z - (FIRST + ridge_owner(z) * CYCLE)


def ridge_form(cycle):
    h = hash01(cycle, 5, 61)
    return "Plain" if h < 0.1 else "Ridge" if h < 0.32 else "RidgeOneSided" if h < 0.45 else "Valley" if h < 0.65 else "ValleyOneSided" if h < 0.78 else "Ledge"


def ridge_end_of(cycle):
    return 780 + 70 * hash01(cycle, 8, 61)


def ridge_built(cycle):
    return cycle >= 0 and FIRST + cycle * CYCLE + ridge_end_of(cycle) + FADE_AFTER < CLEAR_FROM and ridge_form(cycle) != "Plain"


def ridge_has(z):
    if z < FIRST:
        return False
    c = ridge_owner(z); u = ridge_local(z)
    return u >= RIDGE_AT and u < ridge_end_of(c) and ridge_built(c)


def ridge_in_canyons(z):
    if z < FIRST:
        return False
    c = ridge_owner(z); u = ridge_local(z)
    return u >= RIDGE_AT and u < ridge_end_of(c) + FADE_AFTER and ridge_built(c)


def ridge_depth(z):
    return 1.0 if ridge_in_canyons(z) else 0.0   # (only whether it is zero matters here)


# ---------------------------------------------------------------- cliffs (Cliffs.cs)

JUMP_AT = (44.0, 132.0)
JUMP_JITTER, JUMP_LANDING, JUMP_EXTRA, JUMP_BAND = 5.0, 60.0, 6.5, 26.0


class Jump:
    pass


def jump_of(cycle, k):
    if cycle < 0 or k < 0 or k >= 2:
        return None
    key = ("j", cycle, k)
    if key in _memo:
        return _memo[key]
    j = Jump()
    j.z = FIRST + cycle * CYCLE + JUMP_AT[k] + (hash01(cycle, k, 81) - 0.5) * 2 * JUMP_JITTER
    j.cycle, j.k = cycle, k
    j.drop = 3 + 4.5 * hash01(cycle, k, 82)
    j.rise = j.drop + JUMP_EXTRA
    j.bench = 2 * j.rise / 0.78
    j.face = max(3.0, 1.15 * j.drop)
    j.start, j.end = j.z - j.bench, j.z + j.face + JUMP_LANDING
    ok = not (j.end > CLEAR_FROM - 40) and section(j.start - 10) == "Cruise" and section(j.end + 5) == "Cruise"
    _memo[key] = j if ok else None
    return _memo[key]


def jump_near(z, margin):
    u = (z - FIRST) % CYCLE
    if u > 205 + margin and u < CYCLE - 20 - margin:
        return None
    c = cycle_of(z)
    for cc in (c + 1, c, c - 1):
        for k in range(2):
            j = jump_of(cc, k)
            if j is not None and z > j.start - margin and z < j.end + margin:
                return j
    return None


def lip_z(j, a):
    return j.z + (noise(a * 0.06 + j.k * 13.1, j.cycle * 3.7 + 2.2) - 0.5) * 5


def jump_across(a):
    return 1 - smooth01((abs(a) - PISTE_HALF - 6) / JUMP_BAND)


def jump_height(x, z):
    if z < FIRST:
        return 0.0
    u0 = (z - FIRST) % CYCLE
    if u0 > 205 and u0 < CYCLE - 70:
        return 0.0
    total = 0.0
    c = cycle_of(z)
    for cc in (c + 1, c, c - 1):
        for k in range(2):
            j = jump_of(cc, k)
            if j is not None and z > j.start - 8 and z < j.end + 8:
                total += jump_profile(j, x, z)
    return total


def jump_profile(j, x, z):
    a = x - centre(j.z)
    wx = jump_across(a)
    if wx <= 0:
        return 0.0
    u = z - lip_z(j, a)
    if u < -j.bench:
        return 0.0
    if u < 0:
        s = (u + j.bench) / j.bench; p = j.rise * s * s
    elif u < j.face:
        s = u / j.face; p = j.rise - j.drop * s * s * (3 - 2 * s)
    elif u < j.face + JUMP_LANDING:
        s = (u - j.face) / JUMP_LANDING; p = JUMP_EXTRA * (1 - s * s * (3 - 2 * s))
    else:
        return 0.0
    return p * wx


WALL_IN, WALL_HOLD, WALL_TERRACE, WALL_FAR = 22.0, 60.0, 80.0, 90.0


class Wall:
    pass


def wall_of(cycle):
    if cycle < 0:
        return None
    key = ("w", cycle)
    if key in _memo:
        return _memo[key]
    w = Wall()
    zb = FIRST + cycle * CYCLE + DANGER_AT
    w.side = -1.0 if hash01(cycle, 1, 91) < 0.5 else 1.0
    w.height = 25 + 30 * hash01(cycle, 2, 91)
    w.edge = PISTE_HALF + 5 + 6 * hash01(cycle, 3, 91)
    w.z0 = zb + 12 + 10 * hash01(cycle, 4, 91)
    w.z1 = w.z0 + WALL_IN
    w.z2 = w.z1 + WALL_HOLD + w.height / 0.4
    _memo[key] = None if w.z2 > CLEAR_FROM - 40 else w
    return _memo[key]


def wall_near(z, margin):
    u = (z - FIRST) % CYCLE
    if u < DANGER_AT + 10 - margin or u > DANGER_AT + 270 + margin:
        return None
    c = cycle_of(z)
    for cc in (c, c - 1):
        w = wall_of(cc)
        if w is not None and z > w.z0 - margin and z < w.z2 + margin:
            return w
    return None


def wall_height(w, z):
    if z <= w.z0 or z >= w.z2:
        return 0.0
    if z < w.z1:
        s = (z - w.z0) / WALL_IN
        return w.height * s * s * (3 - 2 * s)
    if z < w.z1 + WALL_HOLD:
        return w.height
    t = (z - w.z1 - WALL_HOLD) / (w.z2 - w.z1 - WALL_HOLD)
    return w.height * (1 - t * t * (3 - 2 * t))


def wall_cut(x, z):
    if z < FIRST:
        return 0.0
    w = wall_near(z, 0)
    if w is None:
        return 0.0
    a = (x - centre(z)) * w.side
    hgt = wall_height(w, z)
    if hgt <= 0 or a <= w.edge:
        return 0.0
    face = hgt * 0.55
    s = clamp01((a - w.edge) / face)
    far = clamp01((a - w.edge - face - WALL_TERRACE) / WALL_FAR)
    return -hgt * s * s * (3 - 2 * s) * (1 - far * far * (3 - 2 * far))


def cliffs_keep(x, z, margin):
    j = jump_near(z, 10 + margin)
    if j is not None:
        a = x - centre(j.z)
        if jump_across(a) > 0.05 and z > j.start - 12 - margin and z < j.z + j.face + 14 + margin:
            return True
    w = wall_near(z, 6 + margin)
    if w is not None:
        a = (x - centre(z)) * w.side
        hgt = max(wall_height(w, z), wall_height(w, z + 6))
        if hgt > 1 and a > w.edge - 4 - margin and a < w.edge + hgt * 0.55 + 3 + margin:
            return True
    return False


def course_keep(x, z, margin):
    """Course.Keep for ground at least a few metres off the piste (crevasses and park features lie inside it)."""
    if ridge_in_canyons(z):
        return True   # (stricter than Ridge.Keep: a lift stays out of the whole stretch)
    return cliffs_keep(x, z, margin)


# ================================================================ the lifts (LiftLine.cs, line for line)

GAUGE = 5.2                  # between the cable going up and the one coming down
HALF = GAUGE / 2
CHAIR_DROP = 4.0             # from the cable to the lowest thing on a chair: a seated rider's skis
STATION_CABLE = 4.6          # the cable's height over a station's deck
STATION_HALF_LEN, STATION_HALF_WID = 6.0, 5.5
SWATH = 10.0                 # half the cleared swath under the line
TOWER_OFF = PISTE_HALF + 7   # no tower's foot nearer the piste's centre line than this
STATION_OFF = PISTE_HALF + 14
H_MIN, H_MAX = 10.0, 24.0
CLEAR_OFF, CLEAR_PISTE = 5.0, 9.0
STEP = 4.0                   # tower spots are tried this far apart along the line
SPAN_MIN, SPAN_WANT, SPAN_EASY, SPAN_MAX = 39.5, 70.0, 92.0, 140.5
END_MIN, END_WANT, END_MAX = 22.0, 45.0, 80.0
OFFSETS = (38.0, 46.0, 54.0, 62.0, 72.0, 84.0)
BOTTOM_AT = 505.0            # a lift's bottom station, metres into its cycle: where the danger stretch ends
KNOLL = 0.8                  # how much a metre of knoll under a tower is worth, in the chain's cost
SAG_K = 1.0 / 3500.0         # a span's sag at its middle is its length squared times this


def sag(length):
    return length * length * SAG_K


def want_clear(x, z):
    a = abs(x - centre(z))
    return CLEAR_PISTE + (CLEAR_OFF - CLEAR_PISTE) * clamp01((a - (PISTE_HALF + 4)) / 6.0)


class Lift:
    pass


def last_cycle():
    return cycle_of(FINISH)


def lift_range(k):
    """Where lift k's stations go, top and bottom (z), or None if there is no room for it."""
    base = FIRST + k * CYCLE
    zb = FINISH + 15 if k == last_cycle() else base + BOTTOM_AT
    if k == 0:
        zt = 40.0
    elif ridge_built(k - 1):
        zt = base + ridge_end_of(k - 1) + FADE_AFTER - CYCLE + 30
    else:
        zt = base - CYCLE + 640
    if zb - zt < 180:
        return None
    return zt, zb


def ground_ok(x, z, off):
    """Ground that can hold a tower's foot or a station: off the piste, off the cliffs, not too steep."""
    if abs(x - centre(z)) < off:
        return False
    if course_keep(x, z, 3.0):
        return False
    if wall_cut(x, z) < -0.01 or wall_cut(x - 4, z) < -0.01 or wall_cut(x + 4, z) < -0.01:
        return False
    h = height(x, z)
    gx = (height(x + 3, z) - height(x - 3, z)) / 6.0
    gz = (height(x, z + 3) - height(x, z - 3)) / 6.0
    if abs(gx) > 0.55 or gz > 0.3 or gz < -1.25:
        return False
    return h == h


def station_at(l, x, z, out):
    """A station's deck height: just over the highest snow under it. None if the ground will not do."""
    top = -1e9
    for i in range(-2, 3):
        for s in (-STATION_HALF_WID, -HALF, 0.0, HALF, STATION_HALF_WID):
            px, pz = x + l.dx * i * STATION_HALF_LEN * 0.5 + l.px * s, z + l.dz * i * STATION_HALF_LEN * 0.5 + l.pz * s
            if abs(px - centre(pz)) < STATION_OFF - STATION_HALF_WID - 1:
                return None
            if course_keep(px, pz, 3.0) or wall_cut(px, pz) < -0.01:
                return None
            top = max(top, height(px, pz))
    return top + 0.3


def try_line(k, zt, zb, ot, ob):
    """Lay lift k along the straight line from ot beside the piste at zt to ob beside it at zb. None if it will not go."""
    l = Lift()
    l.k = k
    xt, xb = centre(zt) + ot, centre(zb) + ob
    plan = math.hypot(xb - xt, zb - zt)
    l.dx, l.dz = (xb - xt) / plan, (zb - zt) / plan          # down the line
    l.px, l.pz = l.dz, -l.dx                                   # across it (to the right, looking down)
    l.xt, l.zt, l.xb, l.zb, l.plan = xt, zt, xb, zb, plan
    l.ot, l.ob = ot, ob
    l.deck_t = station_at(l, xt, zt, 1)
    if l.deck_t is None:
        return None
    l.deck_b = station_at(l, xb, zb, -1)
    if l.deck_b is None:
        return None
    # the towers lean out from the slope, half way to square with the cable
    l.lean = 0.5 * math.atan2(l.deck_t - l.deck_b, plan)
    sl, cl = math.sin(l.lean), math.cos(l.lean)

    # where a tower may stand: spots every STEP along the line, between the stations' portals
    s0, s1 = STATION_HALF_LEN, plan - STATION_HALF_LEN
    n = int(math.floor((s1 - s0 - 2 * END_MIN) / STEP)) + 1
    if n < 1:
        return None
    first = s0 + END_MIN + ((s1 - s0 - 2 * END_MIN) - (n - 1) * STEP) * 0.5
    spots, ok, knoll = [], [], []
    for i in range(n):
        s = first + i * STEP
        spots.append(s)
        good = True
        for back in (0.0, H_MIN * sl, H_MAX * sl):       # wherever its height puts its foot (it leans)
            if not ground_ok(xt + l.dx * (s - back), zt + l.dz * (s - back), TOWER_OFF):
                good = False
                break
        ok.append(good)
        knoll.append(knoll_at(l, s) if good else 0.0)

    # the cheapest chain of spans from the top portal to the bottom one
    INF = 1e18
    cost, prev = [INF] * n, [-1] * n
    for i in range(n):
        if not ok[i]:
            continue
        g = spots[i] - s0
        if g <= END_MAX:
            cost[i] = end_cost(g) - knoll[i]
        for j in range(i - 1, -1, -1):
            g = spots[i] - spots[j]
            if g > SPAN_MAX:
                break
            if g < SPAN_MIN or cost[j] >= INF:
                continue
            c = cost[j] + span_cost(g) - knoll[i]
            if c < cost[i]:
                cost[i] = c; prev[i] = j
    best, at = INF, -1
    for i in range(n):
        g = s1 - spots[i]
        if cost[i] < INF and g <= END_MAX and cost[i] + end_cost(g) < best:
            best = cost[i] + end_cost(g); at = i
    if at < 0:
        return None
    chain = []
    while at >= 0:
        chain.append(spots[at]); at = prev[at]
    chain.reverse()
    l.s = chain
    l.h = [H_MIN] * len(chain)

    # heights: every tower starts at its least and is raised until the chairs clear the snow everywhere
    for _ in range(14):
        nodes = lift_nodes(l)
        raised = False
        for i in range(len(nodes) - 1):
            d = span_deficit(l, nodes, i, step=2.0, extra=0.15)
            if d <= 0.01:
                continue
            raised = True
            d += 0.25
            if i > 0:
                l.h[i - 1] += d
            if i < len(l.h):
                l.h[i] += d
        if not raised:
            break
        if max(l.h) > H_MAX:
            return None
    else:
        return None
    l.nodes = lift_nodes(l)
    # (and where each foot has ended up, the snow must not be too steep to stand a tower on: about 50 degrees)
    for i in range(len(l.s)):
        f = tower_foot(l, i)[0]
        if normal_y(f[0], f[2]) < 0.64:
            return None
    return l


def knoll_at(l, s):
    """How far the ground at s stands above the line between the ground 35 m up and down the line: a tower on a
    knoll or a bench's lip carries the cable over it; one in a hollow leaves the next rise in the chairs' way."""
    def g(t):
        t = max(0.0, min(l.plan, t))
        return height(l.xt + l.dx * t, l.zt + l.dz * t)
    return KNOLL * max(-3.0, min(3.0, g(s) - 0.5 * (g(s - 35) + g(s + 35))))


def end_cost(g):
    if g < END_MIN:
        return 1e18
    d = (g - END_WANT) / 15.0
    return d * d


def span_cost(g):
    d = (g - SPAN_WANT) / 12.0
    return d * d + (6 + (g - SPAN_EASY) * 0.25 if g > SPAN_EASY else 0.0)


def tower_foot(l, i):
    """Tower i's foot on the snow and the top of its mast (where the cable crosses its line)."""
    back = l.h[i] * math.sin(l.lean)
    fx, fz = l.xt + l.dx * (l.s[i] - back), l.zt + l.dz * (l.s[i] - back)
    fy = height(fx, fz)
    return (fx, fy, fz), (l.xt + l.dx * l.s[i], fy + l.h[i] * math.cos(l.lean), l.zt + l.dz * l.s[i])


def lift_nodes(l):
    """The points the cable's centre line hangs between: top portal, tower tops, bottom portal (s, y)."""
    nodes = [(STATION_HALF_LEN, l.deck_t + STATION_CABLE)]
    for i in range(len(l.s)):
        nodes.append((l.s[i], tower_foot(l, i)[1][1]))
    nodes.append((l.plan - STATION_HALF_LEN, l.deck_b + STATION_CABLE))
    return nodes


def cable_y(nodes, i, s):
    (sa, ya), (sb, yb) = nodes[i], nodes[i + 1]
    u = (s - sa) / (sb - sa)
    return ya + (yb - ya) * u - 4 * sag(sb - sa) * u * (1 - u)


def span_deficit(l, nodes, i, step=3.0, detail=None, extra=0.0):
    """How far short of its clearance the lowest chair on span i comes (negative: clear by that much)."""
    (sa, ya), (sb, yb) = nodes[i], nodes[i + 1]
    worst = -1e9
    m = max(2, int(math.ceil((sb - sa) / step)))
    last = len(nodes) - 2
    for q in range(m + 1):
        s = sa + (sb - sa) * q / m
        y = cable_y(nodes, i, s) - CHAIR_DROP
        for side in (-1, 1):
            x, z = l.xt + l.dx * s + l.px * side * HALF, l.zt + l.dz * s + l.pz * side * HALF
            need = want_clear(x, z)
            # coming out of a station the chairs start at its deck: the clearance builds over the first metres
            if i == 0:
                need = min(need, 0.4 + 0.2 * (s - sa))
            if i == last:
                need = min(need, 0.4 + 0.2 * (sb - s))
            need += extra
            d = height(x, z) + need - y
            # (a span from a station can only be lifted at its tower end)
            if i == 0 and q > 0:
                d = d / (q / m) if d > 0 else d
            elif i == last and q < m:
                d = d / (1 - q / m) if d > 0 else d
            elif (i == 0 and q == 0) or (i == last and q == m):
                d = min(d, 0.0) if d < 0.3 else 99.0
            if d > worst:
                worst = d
            if detail is not None:
                detail.append((s, x, z, y - height(x, z), need))
    return worst


def lay(k):
    """Lift k of today's mountain, or None."""
    r = lift_range(k)
    if r is None:
        return None
    zt, zb = r
    w = wall_of(k) if k < last_cycle() else None
    sb = -w.side if w is not None else (-1.0 if hash01(k, 1, 171) < 0.5 else 1.0)
    cross = hash01(k, 2, 171) < 0.4
    tries = 0
    for same in ((False, True) if cross else (True, False)):
        st = sb if same else -sb
        pairs = [(a, b) for a in OFFSETS for b in OFFSETS]
        # beside the piste: the nearest first. Across it: from 54 m out either side (nearer, the span over the
        # piste would be too long), then further
        pairs.sort(key=(lambda p: (max(p), p[0] + p[1], p[0])) if same else (lambda p: (min(p) < 54, max(p), p[0] + p[1], p[0])))
        for a, b in pairs[:(36 if same else 10)]:
            tries += 1
            l = try_line(k, zt, zb, st * a, sb * b)
            if l is not None:
                l.tries = tries
                return l
    return None


def taken(lifts, x, z, margin):
    """LiftLine.Taken: under a lift's line or on a station (no trees, rocks or cabins)."""
    for l in lifts:
        s = (x - l.xt) * l.dx + (z - l.zt) * l.dz
        c = (x - l.xt) * l.px + (z - l.zt) * l.pz
        if s > -STATION_HALF_LEN - 6 - margin and s < l.plan + STATION_HALF_LEN + 6 + margin and abs(c) < SWATH + margin:
            return True
    return False


# ================================================================ the check

def check(sd, verbose=False):
    init(sd)
    out = []
    lifts = []
    for k in range(last_cycle() + 1):
        r = lift_range(k)
        l = lay(k)
        if l is None:
            out.append(dict(seed=sd, k=k, ok=False, why="no room" if r is None else "no line found"))
            continue
        lifts.append(l)
        nodes = l.nodes
        spans = [nodes[i + 1][0] - nodes[i][0] for i in range(len(nodes) - 1)]
        clear_off, clear_piste, crossings, problems = 1e9, 1e9, [], []
        for i in range(len(nodes) - 1):
            det = []
            span_deficit(l, nodes, i, step=1.5, detail=det)
            over = [z for (s, x, z, c, need) in det if abs(x - centre(z)) < PISTE_HALF + 4]
            if over:
                crossings.append((round((min(over) + max(over)) / 2), i, round(spans[i])))
            for (s, x, z, c, need) in det:
                on = abs(x - centre(z)) < PISTE_HALF + 4
                near_station = (i == 0 and s - nodes[0][0] < 25) or (i == len(nodes) - 2 and nodes[-1][0] - s < 25)
                if c < need - 0.02:
                    problems.append("span %d s=%.0f clear %.2f < %.2f" % (i, s, c, need))
                if c < 0.3:
                    problems.append("span %d s=%.0f chair in the snow (%.2f)" % (i, s, c))
                if on:
                    clear_piste = min(clear_piste, c)
                elif not near_station:
                    clear_off = min(clear_off, c)
                if ridge_in_canyons(z):
                    problems.append("over a canyon at z=%.0f" % z)
                if wall_cut(x, z) < -0.01:
                    problems.append("over a deadly cliff's terrace at z=%.0f" % z)
        feet = []
        for i in range(len(l.s)):
            foot, top = tower_foot(l, i)
            a = abs(foot[0] - centre(foot[2]))
            feet.append((foot, top, a))
            if a < TOWER_OFF - 0.01:
                problems.append("tower %d is %.1f m from the piste's centre" % (i, a))
            if course_keep(foot[0], foot[2], 2.0):
                problems.append("tower %d on kept ground" % i)
            sec = section(foot[2])
            if sec == "Ridge":
                problems.append("tower %d in a ridge stretch" % i)
        for name, x, z in (("top", l.xt, l.zt), ("bottom", l.xb, l.zb)):
            if abs(x - centre(z)) < STATION_OFF - 0.01:
                problems.append("%s station %.1f m from the piste's centre" % (name, abs(x - centre(z))))
            if section(z) == "Ridge":
                problems.append("%s station in a ridge stretch" % name)
        angle = math.degrees(math.atan2(l.dx, l.dz))
        out.append(dict(seed=sd, k=k, ok=True, zt=l.zt, zb=l.zb, ot=l.ot, ob=l.ob, plan=l.plan, towers=len(l.s), spans=spans, heights=list(l.h),
                        clear_off=clear_off, clear_piste=clear_piste, crossings=crossings, problems=problems, tries=l.tries, angle=angle,
                        rise=l.deck_t - l.deck_b, lean=math.degrees(l.lean),
                        danger_towers=sum(1 for f in feet if section(f[0][2]) == "Danger"), feet=feet, lift=l))
    return out


def describe(res):
    for r in res:
        if not r["ok"]:
            print("lift %d: NONE (%s)" % (r["k"], r["why"]))
            continue
        print("lift %d: top z=%.0f (%+.0f m off the piste), bottom z=%.0f (%+.0f m); %.0f m long in plan, %.0f m of rise, %.1f deg off the fall line, towers lean %.1f deg; found on try %d"
              % (r["k"], r["zt"], r["ot"], r["zb"], r["ob"], r["plan"], r["rise"], r["angle"], r["lean"], r["tries"]))
        print("   %d towers; spans %s" % (r["towers"], " ".join("%.0f" % s for s in r["spans"])))
        print("   heights %s" % " ".join("%.1f" % h for h in r["heights"]))
        print("   least clearance under a chair: %.1f m off the piste%s" % (r["clear_off"], ", %.1f m over it" % r["clear_piste"] if r["crossings"] else ""))
        for (z, i, sp) in r["crossings"]:
            print("   crosses the piste at about z=%d (%s, %d m into its cycle), on span %d (%d m)" % (z, section(z), local(z), i, sp))
        for (foot, top, a) in r["feet"]:
            print("      tower at x=%.1f z=%.1f, %.1f m from the piste's centre, %s" % (foot[0], foot[2], a, section(foot[2])))
        for p in r["problems"]:
            print("   PROBLEM:", p)


def summarise(all_res):
    lifts = [r for r in all_res if r["ok"]]
    none = [r for r in all_res if not r["ok"]]
    print("%d lifts on %d mountains; %d not built (%s)" % (len(lifts), len(set(r["seed"] for r in all_res)), len(none),
          ", ".join("%s x%d" % (w, sum(1 for r in none if r["why"] == w)) for w in sorted(set(r["why"] for r in none)))))
    bad = [r for r in lifts if r["problems"]]
    print("lifts with problems: %d" % len(bad))
    for r in bad[:10]:
        print("   seed %d lift %d: %s" % (r["seed"], r["k"], "; ".join(r["problems"][:3])))
    spans = [s for r in lifts for s in r["spans"][1:-1]]
    ends = [s for r in lifts for s in (r["spans"][0], r["spans"][-1])]
    hs = [h for r in lifts for h in r["heights"]]
    def pct(v, p):
        v = sorted(v); return v[min(len(v) - 1, int(p * len(v)))]
    print("towers per lift: min %d, median %d, max %d" % (min(r["towers"] for r in lifts), pct([r["towers"] for r in lifts], 0.5), max(r["towers"] for r in lifts)))
    print("lift length (plan): min %.0f, median %.0f, max %.0f" % (min(r["plan"] for r in lifts), pct([r["plan"] for r in lifts], 0.5), max(r["plan"] for r in lifts)))
    print("spans between towers: min %.0f, 5%% %.0f, median %.0f, 95%% %.0f, max %.0f; over 95 m: %.1f%%" % (min(spans), pct(spans, 0.05), pct(spans, 0.5), pct(spans, 0.95), max(spans), 100.0 * sum(1 for s in spans if s > 95) / len(spans)))
    print("station to first tower: min %.0f, median %.0f, max %.0f" % (min(ends), pct(ends, 0.5), max(ends)))
    print("tower heights: min %.1f, median %.1f, 90%% %.1f, max %.1f; over 15 m: %.1f%%" % (min(hs), pct(hs, 0.5), pct(hs, 0.9), max(hs), 100.0 * sum(1 for h in hs if h > 15) / len(hs)))
    print("least clearance off the piste: %.2f m" % min(r["clear_off"] for r in lifts))
    cr = [r for r in lifts if r["crossings"]]
    print("lifts that cross the piste: %d (%.0f%%); least clearance over it: %s" % (len(cr), 100.0 * len(cr) / len(lifts), "%.2f m" % min(r["clear_piste"] for r in cr) if cr else "-"))
    if cr:
        cs = [c[2] for r in cr for c in r["crossings"]]
        print("crossing spans: median %.0f, max %.0f" % (pct(cs, 0.5), max(cs)))
    print("line's angle off the fall line: median %.1f, max %.1f deg" % (pct([abs(r["angle"]) for r in lifts], 0.5), max(abs(r["angle"]) for r in lifts)))
    print("tries to find a line: median %d, max %d" % (pct([r["tries"] for r in lifts], 0.5), max(r["tries"] for r in lifts)))
    print("towers standing in a danger stretch (on the side away from its cliff): %d of %d" % (sum(r["danger_towers"] for r in lifts), sum(r["towers"] for r in lifts)))


# ---------------------------------------------------------------- what it costs to draw (SkiLift.cs's rules)

TRIS = dict(Mast=312, MastLod=10, Pad=136, Head=500, HeadLod=72, Sheaves=480, SheavesLod=24, Chair=336, ChairLod=84,
            Riders=(528, 804, 276, 1068), RidersLod=(72, 108, 36, 144), Station=1008)   # as Tools/MakeLift.py prints them
NEAR, CHAIR_NEAR, FAR, BEHIND, PAD_RANGE = 140.0, 100.0, 650.0, 150.0, 320.0
UP_FULL, DOWN_FULL = 0.56, 0.05


def budget(sd, step=15.0):
    """Triangles and draw calls the lifts ask for with the rider at each point down the piste: (z, tris, draws) rows."""
    init(sd)
    lifts = [l for l in (lay(k) for k in range(last_cycle() + 1)) if l is not None]
    for l in lifts:
        l.feet = [tower_foot(l, i)[0] for i in range(len(l.s))]
        l.rise = (l.deck_t - l.deck_b) / l.plan
        l.gap = 18.0 / math.hypot(1.0, l.rise)     # chairs are 18 m apart along the cable: this far in plan
    rows = []
    z = -60.0
    while z < FINISH + 30:
        x = centre(max(z, 0.0))
        y = height(x, z) if z > 1 and not ridge_in_canyons(z) else -FALL * z
        tris, parts = 0.0, set()
        def inr(pz):
            return pz > z - BEHIND and pz < z + FAR
        for l in lifts:
            if not (l.zb + 6 > z - BEHIND and l.zt - 6 < z + FAR):
                continue
            for i in range(len(l.nodes) - 1):
                za, zb2 = l.zt + l.dz * l.nodes[i][0], l.zt + l.dz * l.nodes[i + 1][0]
                if zb2 > z - BEHIND and za < z + FAR:
                    tris += 16 * max(4, math.ceil((l.nodes[i + 1][0] - l.nodes[i][0]) / 8.0)) + (16 if i in (0, len(l.nodes) - 2) else 0)
                    parts.add(("cable", l.k, i))
            for f in l.feet:
                if not inr(f[2]):
                    continue
                d = math.sqrt((f[0] - x) ** 2 + (f[1] - y) ** 2 + (f[2] - z) ** 2)
                lod = "" if d < NEAR else "Lod"
                tris += TRIS["Mast" + lod] + TRIS["Head" + lod] + 2 * TRIS["Sheaves" + lod]
                parts.update(("Mast" + lod, "Head" + lod, "Sheaves" + lod))
                if d < PAD_RANGE:
                    tris += TRIS["Pad"]; parts.add("Pad")
            for (sx, sz) in ((l.xt, l.zt), (l.xb, l.zb)):
                if inr(sz):
                    tris += TRIS["Station"]; parts.add("Station")
            n = int(l.plan / l.gap)
            for side, full in ((1, UP_FULL), (-1, DOWN_FULL)):
                for c in range(n + 1):
                    s = c * l.gap
                    cx, cz = l.xt + l.dx * s + l.px * side * HALF, l.zt + l.dz * s + l.pz * side * HALF
                    if not inr(cz):
                        continue
                    cy = l.deck_t - l.rise * s + 8
                    near = (cx - x) ** 2 + (cy - y) ** 2 + (cz - z) ** 2 < CHAIR_NEAR ** 2
                    tris += TRIS["Chair" if near else "ChairLod"]
                    parts.add("Chair" if near else "ChairLod")
                    r = TRIS["Riders" if near else "RidersLod"]
                    tris += full * (sum(r) / 4.0 if side > 0 else r[2])
                    if side > 0:
                        parts.update(("Riders%d%s" % (v, "" if near else "Lod") for v in range(4)))
        rows.append((z, tris, len(parts)))
        z += step
    return rows


def _budget(sd):
    return budget(sd)


def _one(sd):
    res = check(sd)
    for r in res:
        r.pop("lift", None); r.pop("feet", None)
    return res


if __name__ == "__main__":
    if "budget" in sys.argv:
        sys.argv.remove("budget")
        import multiprocessing
        lo, hi = (int(sys.argv[1]), int(sys.argv[2])) if len(sys.argv) > 2 else (1, 60)
        with multiprocessing.Pool() as pool:
            rows = [r for res in pool.map(_budget, range(lo, hi + 1)) for r in res]
        t = sorted(r[1] for r in rows); d = sorted(r[2] for r in rows)
        seen = [r for r in rows if r[1] > 0]
        print("rider positions sampled: %d (every 15 m down %d mountains); a lift in range at %.0f%% of them" % (len(rows), hi - lo + 1, 100.0 * len(seen) / len(rows)))
        print("triangles asked for: median %d, 95%% %d, most %d" % (t[len(t) // 2], t[int(len(t) * 0.95)], t[-1]))
        print("draw calls (instanced batches + cable spans): median %d, 95%% %d, most %d" % (d[len(d) // 2], d[int(len(d) * 0.95)], d[-1]))
        sys.exit(0)
    args = [int(a) for a in sys.argv[1:]]
    if len(args) == 1:
        describe(check(args[0]))
    else:
        lo, hi = (args[0], args[1]) if len(args) == 2 else (1, 200)
        import multiprocessing
        with multiprocessing.Pool() as pool:
            results = pool.map(_one, range(lo, hi + 1), chunksize=4)
        summarise([r for res in results for r in res])
