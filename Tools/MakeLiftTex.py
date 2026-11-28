# The lift stations' own textures (liftlook), drawn here with numpy, tileable, into Assets/Summit/Resources/Lift/Tex/:
#   python3 Tools/MakeLiftTex.py [out]
#   LiftDeck.png    a station's deck boards: seven to the metre, gaps packed with snow, nail heads, scuffed (1 m a tile)
#   LiftSnow.png    packed snow on an unload deck: the groomer's corduroy, a few ski and board tracks, scuffs (4 m a tile)
#   LiftPanel.png   painted steel panels: seams, rows of bolts, chipped paint, grime run down from the seams (2 m a tile;
#                   near white, so a material's colour tints it: the housing white, its stripe red, the bullwheel's cowl dark)
#   LiftRubber.png  black ribbed rubber matting with snow trodden into it (1 m a tile)
#   LiftHazard.png  yellow and black stripes, scuffed (1 m a tile): the painted load and unload lines, the deck's edges
#   LiftNet.png     orange safety netting, strands on a dark ground (1 m a tile)
#   LiftGalv.png    weathered galvanised steel: spangle, dull patches, rust run down in streaks (2 m a tile)
# The game makes plain URP Lit materials of them (base map only: no keywords of their own), drawn through the world's
# far band like the rest of the lift (SkiLift, Farview). No logos, no lettering.
import numpy as np, os, struct, sys, zlib

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/Summit/Resources/Lift/Tex"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(20261127)


def png(path, rgb):
    """rgb: h x w x 3 floats 0..1 (sRGB)."""
    a = (np.clip(rgb, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    h, w, _ = a.shape
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)
    print("LIFTTEX", path, w, h)


def noise(n, cells, octaves=4, gain=0.5, sx=1, sy=1):
    """Tileable value noise, n x n, about 0..1: `cells` grid cells a side at the first octave (sx, sy stretch it)."""
    out = np.zeros((n, n))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        cx, cy = max(1, int(cells * sx) << o), max(1, int(cells * sy) << o)
        g = rng.random((cy, cx))
        ys = np.arange(n) * cy / n
        xs = np.arange(n) * cx / n
        y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
        fy = ys - y0; fx = xs - x0
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % cy; x1 = (x0 + 1) % cx
        a = g[y0][:, x0]; b = g[y0][:, x1]; c = g[y1][:, x0]; d = g[y1][:, x1]
        top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
        out += amp * (top + (bot - top) * fy[:, None])
        tot += amp; amp *= gain
    return out / tot


def grid(n):
    y, x = np.mgrid[0:n, 0:n].astype(float)
    return x, y


def wrapdist(a, b, n):
    d = np.abs(a - b) % n
    return np.minimum(d, n - d)


# ---------------------------------------------------------------- packed snow with tracks
def snow():
    n = 512
    x, y = grid(n)
    base = np.array([0.93, 0.95, 0.99])
    v = 1.0 + 0.05 * (noise(n, 4) - 0.5) + 0.03 * (noise(n, 32, 3) - 0.5)
    # corduroy: ribs across the tile (along x), a little wavy, 26 to the tile (about 15 cm)
    wav = 3.0 * np.sin(2 * np.pi * x / n * 2) + 2.0 * (noise(n, 6, 2) - 0.5) * 6
    rib = np.sin(2 * np.pi * (y + wav) / n * 26)
    v += 0.06 * rib * (0.6 + 0.4 * noise(n, 3, 2))
    # tracks along y (the way down the deck): pairs of ski lines and a wider board line, gently curving, wrapping
    for (x0, amp, k, kind, w) in [(70, 18, 1, "ski", 2.2), (205, 30, 1, "board", 7.0), (330, 14, 2, "ski", 2.0), (430, 24, 1, "ski", 2.4)]:
        cx = x0 + amp * np.sin(2 * np.pi * y / n * k + x0)
        if kind == "ski":
            for off in (-5.5, 5.5):
                d = wrapdist(x, cx + off, n)
                v -= 0.15 * np.exp(-(d / w) ** 2)
                v += 0.03 * np.exp(-((d - w * 1.6) / w) ** 2)
        else:
            d = wrapdist(x, cx, n)
            v -= 0.10 * np.exp(-(d / w) ** 4)
            v += 0.04 * np.exp(-((d - w * 1.3) / 2.0) ** 2)
    # scuffs and sparkle
    v -= 0.05 * np.clip((noise(n, 10, 3) - 0.68) * 5, 0, 1)
    v += 0.06 * (rng.random((n, n)) > 0.997)
    rgb = base[None, None, :] * v[..., None]
    rgb[..., 2] += 0.02 * (noise(n, 5, 2) - 0.5)
    return rgb


# ---------------------------------------------------------------- painted steel panels with bolts
def panel():
    n = 256
    x, y = grid(n)
    v = np.ones((n, n)) * 0.93
    v *= 1.0 + 0.04 * (noise(n, 4) - 0.5)
    # seams: one down the middle and at the edges (1 m panels), one across
    for sx in (0, 128):
        d = wrapdist(x, sx, n)
        v -= 0.35 * np.exp(-(d / 1.2) ** 2)
        v += 0.05 * np.exp(-((d - 2.5) / 1.2) ** 2)
    d = wrapdist(y, 0, n)
    v -= 0.3 * np.exp(-(d / 1.2) ** 2)
    # bolts along each seam, every 16 px, either side
    bolt = np.zeros((n, n))
    for sx in (0, 128):
        for side in (-6, 6):
            for by in range(8, n, 16):
                dx = wrapdist(x, sx + side, n); dy = wrapdist(y, by, n)
                r = np.sqrt(dx * dx + dy * dy)
                bolt = np.maximum(bolt, np.clip(2.6 - r, 0, 1))
                v -= 0.25 * np.clip(1.0 - np.abs(r - 2.6), 0, 1) * 0.6     # the rim's shadow
                v += 0.10 * np.clip(1.6 - np.hypot(dx + 0.8, dy + 0.8), 0, 1)   # the head's highlight
    # grime run down from the seam across and from each bolt (y grows downward in the picture)
    streak = noise(n, 4, 3, sx=8, sy=0.5)
    run = np.exp(-((y % n) / n) * 3.0)
    v -= 0.16 * run * np.clip(streak - 0.42, 0, 1) * 3
    # rust and dirt run down from every bolt, longer under the seam across
    for sx in (0, 128):
        for side in (-6, 6):
            for by in range(8, n, 16):
                dx = wrapdist(x, sx + side, n); dy = (y - by) % n
                v -= 0.05 * np.exp(-(dx / 1.6) ** 2) * np.exp(-dy / 22.0) * (dy > 2)
    v *= 1.0 - 0.05 * noise(n, 2, 2)
    # chipped paint: the bare steel under it, darker grey
    chips = np.clip((noise(n, 16, 3) - 0.74) * 12, 0, 1)
    edge = np.exp(-(np.minimum(wrapdist(x, 0, n), wrapdist(x, 128, n)) / 10.0) ** 2)
    chips = np.clip(chips + edge * np.clip((noise(n, 24, 2) - 0.6) * 8, 0, 1), 0, 1)
    v = v * (1 - chips) + 0.52 * chips
    rgb = np.repeat(v[..., None], 3, axis=2)
    rgb[..., 0] += 0.04 * chips      # (a little rust in the chips)
    rgb[..., 2] -= 0.02 * chips
    return rgb


# ---------------------------------------------------------------- rubber matting
def rubber():
    n = 256
    x, y = grid(n)
    v = np.ones((n, n)) * 0.10
    # diamond ribs, 16 to the metre
    a = np.sin(2 * np.pi * (x + y) / n * 16); b = np.sin(2 * np.pi * (x - y) / n * 16)
    rib = np.clip(np.maximum(a, b) - 0.55, 0, 1) * 2.2
    v += 0.07 * rib
    v *= 1.0 + 0.25 * (noise(n, 6) - 0.5)
    # snow trodden in: pale where the ribs hold it, specks
    snow = np.clip((noise(n, 14, 3) - 0.64) * 4, 0, 1) * (1 - rib * 0.7)
    v = v * (1 - snow * 0.5) + 0.85 * snow * 0.5
    v += 0.5 * (rng.random((n, n)) > 0.995)
    rgb = np.repeat(v[..., None], 3, axis=2)
    rgb[..., 2] += 0.03 * snow
    return rgb


# ---------------------------------------------------------------- hazard stripes
def hazard():
    n = 256
    x, y = grid(n)
    t = ((x + y) / n * 4) % 1.0
    yellow = np.array([0.97, 0.76, 0.07]); black = np.array([0.08, 0.08, 0.09])
    m = (t < 0.5).astype(float)
    # (a soft edge, so no shimmer)
    m = np.clip(0.5 + (np.minimum(np.abs(t - 0.5), np.minimum(t, 1 - t)) * np.where(t < 0.5, 1, -1)) * 40, 0, 1)
    rgb = yellow[None, None, :] * m[..., None] + black[None, None, :] * (1 - m[..., None])
    rgb *= (1.0 + 0.08 * (noise(n, 6) - 0.5))[..., None]
    # scuffed: worn to grey and snow-whitened where boots and boards go
    wear = np.clip((noise(n, 10, 4) - 0.6) * 4, 0, 1)
    rgb = rgb * (1 - wear[..., None] * 0.55) + np.array([0.78, 0.78, 0.8])[None, None, :] * wear[..., None] * 0.55
    return rgb


# ---------------------------------------------------------------- safety netting
def net():
    n = 256
    x, y = grid(n)
    orange = np.array([1.0, 0.42, 0.06]); ground = np.array([0.20, 0.16, 0.14])
    # a diamond mesh, 10 to the metre, the strands 18% of the cell
    a = ((x + y) / n * 10) % 1.0; b = ((x - y) / n * 10) % 1.0
    sa = np.exp(-((np.minimum(a, 1 - a)) / 0.06) ** 2); sb = np.exp(-((np.minimum(b, 1 - b)) / 0.06) ** 2)
    s = np.clip(sa + sb, 0, 1)
    shade = 1.0 + 0.15 * (noise(n, 4) - 0.5)
    rgb = (orange[None, None, :] * s[..., None] + ground[None, None, :] * (1 - s[..., None])) * shade[..., None]
    # snow caught on the strands
    snow = np.clip((noise(n, 12, 3) - 0.62) * 5, 0, 1) * s
    rgb = rgb * (1 - snow[..., None] * 0.6) + 0.92 * snow[..., None] * 0.6
    return rgb


# ---------------------------------------------------------------- weathered galvanised steel
def galv():
    n = 256
    x, y = grid(n)
    v = 0.70 + 0.06 * (noise(n, 24, 2) - 0.5)            # spangle
    v += 0.05 * (noise(n, 3, 3) - 0.5)                  # dull patches
    streak = noise(n, 3, 4, sx=10, sy=0.6)              # runs down the steel
    rust = np.clip((streak - 0.55) * 3.5, 0, 1) * (0.5 + 0.5 * noise(n, 2, 2))
    rgb = np.repeat(v[..., None], 3, axis=2)
    rgb = rgb * (1 - rust[..., None] * 0.45) + np.array([0.48, 0.34, 0.24])[None, None, :] * rust[..., None] * 0.45
    dirt = np.clip((noise(n, 6, 3) - 0.6) * 3, 0, 1)
    rgb *= (1 - 0.12 * dirt)[..., None]
    return rgb


# ---------------------------------------------------------------- deck planks
def deck():
    """A station's deck boards, 1 m a tile: seven boards across it, each its own shade and grain, dark gaps between
    them with snow packed into them, the boards' ends staggered, nail heads in rows, snow scuffed across."""
    n = 512
    x, y = grid(n)
    board = n / 7.0
    i = np.floor(x / board).astype(int) % 7
    shade = np.array([0.95, 0.86, 1.0, 0.9, 0.82, 0.97, 0.88])[i]
    base = np.array([0.50, 0.40, 0.31])
    grain = noise(n, 3, 4, sx=6.0, sy=0.15)
    v = shade * (0.85 + 0.3 * grain)
    # (the boards' ends: each board's join at its own place along it)
    ends = np.array([0.1, 0.55, 0.3, 0.8, 0.45, 0.05, 0.65])[i] * n
    join = wrapdist(y, ends, n) < 2.0
    gap = (np.abs((x % board) - board) < 3.0) | ((x % board) < 3.0)
    rgb = base[None, None, :] * v[..., None]
    rgb[join] *= 0.35
    # snow packed into the gaps, the rest of the gap dark
    snowgap = gap & (noise(n, 12, 2) > 0.45)
    rgb[gap] = np.array([0.12, 0.10, 0.09])
    rgb[snowgap] = np.array([0.86, 0.88, 0.92])
    # nail heads, two to a board every 128 px
    for k in range(7):
        for ny in range(32, n, 128):
            for off in (0.25, 0.75):
                d = np.hypot(wrapdist(x, (k + off) * board, n), wrapdist(y, ny + (k * 37) % 64, n))
                rgb[d < 2.2] = np.array([0.18, 0.17, 0.17])
    # snow scuffed across it, and weathering
    scuff = np.clip((noise(n, 6, 4) - 0.6) * 3.5, 0, 1)[..., None]
    rgb = rgb * (1 - scuff * 0.7) + np.array([0.9, 0.92, 0.96])[None, None, :] * scuff * 0.7
    rgb *= (0.9 + 0.2 * noise(n, 4, 2))[..., None]
    return rgb


for name, f in [("LiftDeck", deck), ("LiftSnow", snow), ("LiftPanel", panel), ("LiftRubber", rubber), ("LiftHazard", hazard), ("LiftNet", net), ("LiftGalv", galv)]:
    png(os.path.join(OUT, name + ".png"), f())
