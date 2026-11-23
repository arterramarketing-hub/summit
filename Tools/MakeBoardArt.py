#!/usr/bin/env python3
"""
The board and ski graphics ("vinyls") sold in the closet's GRAPHICS tab: each one painted here from code, original
work, made in the deck's own measurements (metres) so a pattern has the same scale on a board as on a ski.

    python3 Tools/MakeBoardArt.py [--project .] [--preview DIR]

Writes Assets/Summit/Resources/BoardArt/<Key>.png (512 x 256) for every design, and BoardArtThumbs.png (the closet's
list: each design's deck as a little board, 256 x 48 a cell, two to a row). --preview also writes a flat sheet of them
all (deck top, base, a ski) for looking at.

One texture a design, read by the game's own board material (URP Lit, _BaseMap) through uvs BoardArt.cs lays on the
deck and the skis at load time. Rows, from the bottom (uv v = 0) up, as BoardArt.cs has them:
      0..  7  the rail colour (the sidewall, the poles, the tube's trim; also a gutter)
      8.. 39  a ski's top sheet, tail (u = 0) to tip (u = 1), 1.62 m by 0.104 m
     40.. 47  the rail colour (gutter)
     48..123  the base, tail to nose
    124..127  the rail colour (gutter)
    128..255  the deck's top sheet, tail (u = 0) to nose (u = 1), 1.56 m by 0.292 m (its widest)
A texel's centre maps to the region's edge at either end (u = 0.5/512 is the tail's very end), so nothing samples the
row next door. Needs only numpy.
"""
import math, os, sys, zlib, struct, argparse
import numpy as np

W_TEX, H_TEX = 512, 256
TOP, BASE, SKI = (128, 128), (48, 76), (8, 32)          # (first row from the bottom, rows)
DECK_HL, DECK_HW = 0.78, 0.146                           # the deck's half length, half its widest
SKI_HL, SKI_HW = 0.81, 0.052
SS = 3                                                   # supersampling, each way

# ----------------------------------------------------------------------------------------------- tools

def C(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)

def canvas(X, col):
    return np.broadcast_to(C(col) if isinstance(col, str) else col, X.shape + (3,)).astype(np.float32).copy()

def paint(img, m, col, a=1.0):
    m = (m.astype(np.float32) if m.dtype == bool else m) * a
    c = C(col) if isinstance(col, str) else col
    img *= (1.0 - m)[..., None]
    img += m[..., None] * c
    return img

def mix(a, b, t):
    a = C(a) if isinstance(a, str) else a
    b = C(b) if isinstance(b, str) else b
    t = np.asarray(t, np.float32)
    return a * (1 - t)[..., None] + b * t[..., None] if t.ndim else a * (1 - t) + b * t

def ramp(cols, t):
    """A gradient through several colours, t 0..1."""
    cs = [C(c) for c in cols]
    t = np.clip(t, 0, 1) * (len(cs) - 1)
    i = np.minimum(np.floor(t).astype(int), len(cs) - 2)
    f = (t - i)[..., None]
    A = np.stack(cs)[i]; B = np.stack(cs)[i + 1]
    return A * (1 - f) + B * f

def bands(cols, t):
    """Flat bands of colour (a screen print's), t 0..1."""
    cs = np.stack([C(c) for c in cols])
    i = np.clip((np.clip(t, 0, 0.9999) * len(cols)).astype(int), 0, len(cols) - 1)
    return cs[i]

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

M32 = np.uint64(0xffffffff)
def hash2(ix, iy, seed):
    h = (ix.astype(np.uint64) * np.uint64(374761393) + iy.astype(np.uint64) * np.uint64(668265263) + np.uint64(seed * 1442695041 & 0xffffffff)) & M32
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(1274126177)) & M32
    h = h ^ (h >> np.uint64(16))
    return (h & np.uint64(0xffffff)).astype(np.float32) / float(0x1000000)

def vnoise(x, y, seed=0):
    ix = np.floor(x); iy = np.floor(y)
    fx = (x - ix).astype(np.float32); fy = (y - iy).astype(np.float32)
    ix = ix.astype(np.int64) + 100000; iy = iy.astype(np.int64) + 100000
    ux = fx * fx * (3 - 2 * fx); uy = fy * fy * (3 - 2 * fy)
    a = hash2(ix, iy, seed); b = hash2(ix + 1, iy, seed); c = hash2(ix, iy + 1, seed); d = hash2(ix + 1, iy + 1, seed)
    return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy

def fbm(x, y, octaves=4, seed=0):
    s, amp, norm = 0.0, 0.5, 0.0
    for k in range(octaves):
        s = s + amp * vnoise(x * (2 ** k), y * (2 ** k), seed + 17 * k); norm += amp; amp *= 0.5
    return s / norm

def poly(X, Y, pts):
    inside = np.zeros(X.shape, bool)
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]; x2, y2 = pts[(i + 1) % n]
        if y1 == y2: continue
        cond = ((y1 > Y) != (y2 > Y)) & (X < (x2 - x1) * (Y - y1) / (y2 - y1) + x1)
        inside ^= cond
    return inside

def seg(X, Y, a, b):
    px, py = X - a[0], Y - a[1]; dx, dy = b[0] - a[0], b[1] - a[1]
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy + 1e-12), 0, 1)
    return np.hypot(px - t * dx, py - t * dy)

def polyline(X, Y, pts):
    d = np.full(X.shape, 9.0, np.float32)
    for a, b in zip(pts, pts[1:]): d = np.minimum(d, seg(X, Y, a, b))
    return d

def isolines(f, X, Y, interval, width):
    """Lines along f = k * interval, `width` metres wide (whatever the slope). Returns the mask and the level index."""
    lv = f / interval
    gx = np.gradient(lv, axis=1) / max(1e-9, float(X[0, 1] - X[0, 0]))
    gy = np.gradient(lv, axis=0) / max(1e-9, float(Y[1, 0] - Y[0, 0]))
    g = np.hypot(gx, gy) + 1e-6
    fr = lv - np.round(lv)
    return np.abs(fr) / g < width * 0.5, np.round(lv).astype(int)

def half_width(X):
    """The deck's outline (as Tools/MakeHero.py's make_board cuts it): half its width at X along it."""
    ax = np.abs(X)
    contact, L2, W, waist = 0.62, 0.78, 0.292, 0.262
    inner = waist + (W - waist) * (ax / contact) ** 2
    k = np.clip((ax - contact) / (L2 - contact), 0, 1)
    outer = W * np.sqrt(np.maximum(0.0, 1 - k ** 2.2))
    return np.where(ax <= contact, inner, outer) / 2

def grid(cols, rows, hl, hw, ss=SS):
    """Sample points (metres) for a region cols x rows, supersampled; texel centres at the ends map to +-hl, +-hw."""
    u = (np.arange(cols * ss) + 0.5) / ss - 0.5
    v = (np.arange(rows * ss) + 0.5) / ss - 0.5
    X = (u / (cols - 1) * 2 - 1) * hl
    Y = (v / (rows - 1) * 2 - 1) * hw
    XX, YY = np.meshgrid(X.astype(np.float32), Y.astype(np.float32))
    return XX, YY

def down(img, ss=SS):
    h, w = img.shape[0] // ss, img.shape[1] // ss
    return img.reshape(h, ss, w, ss, -1).mean(axis=(1, 3))

def write_png(path, arr):
    """arr: rows x cols x 3 or 4, floats 0..1, row 0 at the top."""
    h, w, ch = arr.shape
    a = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + a[r].tobytes() for r in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6 if ch == 4 else 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "wb").write(png)

# ----------------------------------------------------------------------------------------------- the bases
# A base is pressed from a few sheets of coloured polyethylene: big, flat shapes in two or three colours.

def base_art(X, Y, style, bg, fg, fg2=None):
    img = canvas(X, bg)
    if style == "stripe":
        paint(img, np.abs(X * 0.32 - Y) < 0.05, fg)
        paint(img, np.abs(X * 0.32 - Y + 0.095) < 0.012, fg2 or fg)
    elif style == "circle":
        r = np.hypot(X, Y * 1.0)
        paint(img, r < 0.115, fg)
        paint(img, (r > 0.13) & (r < 0.142), fg2 or fg)
    elif style == "split":
        paint(img, X * 0.22 + Y > 0, fg)
        paint(img, np.abs(X * 0.22 + Y) < 0.008, fg2 or bg)
    elif style == "chevron":
        v = X - np.abs(Y) * 1.3
        paint(img, ((v / 0.15) % 1.0 < 0.38) & (np.abs(X) < 0.62), fg)
    elif style == "fade":
        img[:] = ramp([bg, fg], sstep(-0.7, 0.7, X))
        paint(img, np.abs(Y) < 0.012, fg2 or bg)
    return img

# ----------------------------------------------------------------------------------------------- the designs
# Each top sheet is a function of (X, Y) in metres: X along (+ toward the nose or the ski's tip), Y across;
# `ski` is True for a ski's (0.104 m wide) and False for the deck's.

def sunset(X, Y, ski):
    img = ramp(["#3B1F4F", "#2B1B4A", "#1C1238"], sstep(-0.1, 0.78, X))
    st = (hash2(np.floor(X / 0.012).astype(np.int64) + 5000, np.floor(Y / 0.012).astype(np.int64) + 5000, 3) > 0.985) & (X > 0.5)
    paint(img, st, "#FFE9C7", 0.8)
    cx, R = 0.24, 0.2
    d = np.hypot(X - cx, Y)
    sun = bands(["#7B2D6B", "#C9356C", "#F25F5C", "#FF8C42", "#FFB347", "#FFD166"], (X - (cx - R)) / (2 * R))
    s = (cx + 0.06 - X) / (R + 0.06)
    slit = (s > 0) & (((s * 7.0) % 1.0) < s * 0.55)
    on = (d < R) & ~slit & (X > 0.0)
    img = np.where(on[..., None], sun, img)
    # the sea: the sun's colours laid back in bars, thinner toward the tail
    below = X < 0.0
    k = np.clip(-X / 0.78, 0, 1)
    bar = ((k * 18) ** 0.8 % 1.0) < (0.62 - 0.45 * k)
    refl = bands(["#FFD166", "#FF8C42", "#F25F5C", "#C9356C", "#7B2D6B"], np.clip(k * 1.4, 0, 1))
    wide = np.abs(Y) < (0.13 - 0.12 * k)
    paint(img, below, "#24163F")
    img = np.where((below & bar & wide)[..., None], refl, img)
    # the tail: two stripes
    paint(img, (X < -0.62) & (X > -0.64), "#FFD166")
    paint(img, (X < -0.655) & (X > -0.665), "#F25F5C")
    return img

def topo(X, Y, ski):
    h = fbm(X * 3.0 + 3.1, Y * 3.0 + 1.7, 5, 21) + 0.15 * X
    img = canvas(X, "#1E3D2F")
    m, lv = isolines(h, X, Y, 0.032, 0.0018)
    paint(img, (lv % 2 == 0) & ~m, "#21432F", 1.0)
    paint(img, m, "#E9E2CC")
    m5, lv5 = isolines(h, X, Y, 0.16, 0.0036)
    paint(img, m5, "#E9E2CC")
    ma, _ = isolines(h - 0.016, X, Y, 0.48, 0.0034)
    paint(img, ma, "#FF7A3D")
    return img

def timber(X, Y, ski):
    g = Y * 55 + fbm(X * 1.4 + 2, Y * 7 + 9, 4, 5) * 7 + 2.5 * np.sin(X * 2.1)
    f = g % 1.0
    late = sstep(0.0, 0.08, f) * (1 - sstep(0.18, 0.34, f))
    streak = vnoise(X * 3 + 7, Y * 420, 9) * 0.12
    img = ramp(["#E0B27A", "#C98E52", "#A8693A"], np.clip(0.25 + late * 0.75 - streak, 0, 1))
    img *= (0.94 + 0.06 * fbm(X * 30, Y * 3, 3, 13))[..., None]
    for a, b in ((0.600, 0.613), (0.626, 0.632)):
        paint(img, (np.abs(X) > a) & (np.abs(X) < b), "#1A1A1A")
    r = np.hypot(X, Y)
    paint(img, (r > 0.046) & (r < 0.052), "#4A2810", 0.85)
    paint(img, poly(X, Y, [(0.0, 0.03), (0.026, 0.0), (0.0, -0.03), (-0.026, 0.0)]), "#4A2810", 0.85)
    return img

def tide(X, Y, ski):
    r = 0.046
    img = canvas(X, "#1B2A57")
    cols = (C("#F1E9DA"), C("#1B2A57"), C("#3A5A9B"))
    dy = r * 0.5
    j0, j1 = int(math.floor(-0.85 / dy)) - 2, int(math.ceil(0.85 / dy)) + 2
    for j in range(j1, j0 - 1, -1):           # from the nose down: each row lies over the one before
        xc = j * dy
        off = r if j % 2 else 0.0
        yc = np.round((Y - off) / (2 * r)) * 2 * r + off
        d = np.hypot(X - xc, Y - yc) / r
        on = d < 1.0
        if not on.any(): continue
        b = np.floor(d * 5.5); f = d * 5.5 - b
        col = np.where((f < 0.3)[..., None], cols[0], np.where((b % 2 == 1)[..., None], cols[1], cols[2]))
        col = np.where((d < 0.16)[..., None], cols[0], col)
        img = np.where(on[..., None], col, img)
    edge = np.abs(X) > 0.6
    paint(img, edge, "#1B2A57")
    paint(img, edge & (np.abs(np.abs(X) - 0.625) < 0.004), "#F1E9DA")
    paint(img, edge & (np.abs(np.abs(X) - 0.68) < 0.002), "#F1E9DA")
    return img

def ridge_profile(Y, base, amp, freq, seed):
    n = fbm(Y * freq + seed, np.full_like(Y, seed * 0.37), 4, seed)
    return base + amp * (1 - np.abs(2 * n - 1)) ** 1.4

def ridgeline(X, Y, ski):
    img = ramp(["#FCE7C8", "#F9C9A0", "#F29E7C"], sstep(0.15, 0.78, X))
    d = np.hypot(X - 0.52, Y + 0.03)
    paint(img, d < 0.055, "#FFF4DA")
    paint(img, (d > 0.066) & (d < 0.07), "#FFF4DA", 0.7)
    layers = [(0.18, 0.18, 9, 3, "#C7A6C6", True), (-0.04, 0.16, 12, 7, "#8C6A9E", True),
              (-0.28, 0.14, 15, 11, "#5A4472", False), (-0.50, 0.12, 19, 19, "#2C2443", False)]
    for base, amp, fr, sd, col, snow in layers:
        top = ridge_profile(Y, base, amp, fr, sd)
        on = X < top
        paint(img, on, col)
        if snow:
            cap = on & (X > top - 0.028) & (top > base + amp * 0.45)
            paint(img, cap, "#F7F1EA", 0.9)
    return img

def camo(X, Y, ski):
    img = canvas(X, "#56633A")
    paint(img, fbm(X * 6, Y * 8, 4, 31) > 0.55, "#6E5638")
    paint(img, fbm(X * 7 + 3, Y * 9, 4, 47) > 0.60, "#24261C")
    paint(img, fbm(X * 6 + 9, Y * 7 + 2, 4, 59) > 0.63, "#9C9A6A")
    return img

def primary(X, Y, ski):
    img = canvas(X, "#EEE4CF")
    tail = np.hypot(X + 0.50, Y - 0.146)
    paint(img, tail < 0.15, "#1D4E89")
    paint(img, poly(X, Y, [(-0.05, -0.16), (0.20, -0.16), (0.07, 0.10)]), "#F4B400")
    paint(img, (X > 0.12) & (X < 0.15), "#141414")
    d = np.hypot(X - 0.42, Y + 0.02)
    paint(img, d < 0.11, "#D62828")
    paint(img, (d > 0.128) & (d < 0.136), "#141414")
    paint(img, seg(X, Y, (-0.78, -0.10), (0.0, 0.17)) < 0.0025, "#141414")
    paint(img, (np.abs(X + 0.26) < 0.03) & (np.abs(Y + 0.07) < 0.03), "#141414")
    paint(img, (np.abs(X - 0.66) < 0.012), "#1D4E89")
    return img

def checkmate(X, Y, ski):
    s = 0.0365
    cell = (np.floor(X / s) + np.floor(Y / s)) % 2
    img = np.where((cell > 0)[..., None], C("#111111"), C("#F4F4F0"))
    d = np.abs(X - Y * 2.2)
    paint(img, d < 0.085, "#F4F4F0")
    paint(img, d < 0.07, "#E03131")
    paint(img, np.abs(d - 0.035) < 0.003, "#111111")
    return img

def nordic(X, Y, ski):
    st = 0.0091
    a = np.floor(X / st + 0.5).astype(int); b = np.floor(Y / st + 0.5).astype(int)
    on = np.zeros(X.shape, bool)
    def star(ca, size):
        p = np.abs(a - ca); q = np.abs(b)
        petal = ((p <= size // 3) & (q <= size)) | ((q <= size // 3) & (p <= size)) | ((np.abs(p - q) <= size // 3) & (p + q <= size + size // 3))
        cut = ((p == 0) | (q == 0) | (p == q)) & (p + q >= 2) & (p + q <= size + size // 3 - 2)
        return petal & ~cut & ~((p + q) <= 1)
    on |= star(0, 11)
    on |= star(33, 6) | star(-33, 6)
    for c0 in (60, -60):
        r = a - c0
        zz = (np.abs(r) == (np.abs((b % 8) - 4) - 2)) & (np.abs(r) <= 2)
        on |= zz
        on |= ((np.abs(r) == 5) & (b % 2 == 0))
    dots = (((a % 6) == 0) & ((b % 6) == 3)) | (((a % 6) == 3) & ((b % 6) == 0))
    clear = (np.abs(a) > 14) & (np.abs(np.abs(a) - 33) > 9) & (np.abs(np.abs(a) - 60) > 7)
    on |= dots & clear
    img = np.where(on[..., None], C("#F5F0E6"), C("#B3202A"))
    fx = X / st + 0.5 - np.floor(X / st + 0.5); fy = Y / st + 0.5 - np.floor(Y / st + 0.5)
    knit = 0.86 + 0.14 * (1 - np.abs(fy - 0.5) * 2) * (1 - 0.6 * np.abs(fx - 0.5))
    return img * knit[..., None]

def popdots(X, Y, ski):
    img = canvas(X, "#F6F1E7")
    def halftone(ang, p, rad):
        ca, sa = math.cos(ang), math.sin(ang)
        u = X * ca + Y * sa; v = -X * sa + Y * ca
        du = u / p - np.round(u / p); dv = v / p - np.round(v / p)
        return np.hypot(du, dv) * p < rad
    t1 = sstep(-0.7, 0.75, X); t2 = 1 - sstep(-0.75, 0.7, X)
    red = halftone(math.radians(45), 0.012, 0.0072 * t1)
    blue = halftone(math.radians(15), 0.012, 0.0072 * t2)
    img *= np.where(red[..., None], C("#E63946"), 1.0)
    img *= np.where(blue[..., None], C("#1D7FC4"), 1.0)
    r = np.hypot(X - 0.0, Y)
    paint(img, (r > 0.10) & (r < 0.112), "#111111")
    return img

def volt(X, Y, ski):
    img = canvas(X, "#0B0B12")
    gl = (np.abs(X / 0.04 - np.round(X / 0.04)) * 0.04 < 0.0008) | (np.abs(Y / 0.04 - np.round(Y / 0.04)) * 0.04 < 0.0008)
    paint(img, gl, "#232046")
    yw = 0.07 if not ski else 0.03
    main = [(-0.80, 0.0), (-0.62, yw), (-0.50, -yw * 0.6), (-0.30, yw * 1.1), (-0.18, -yw * 0.9), (0.02, yw * 0.8),
            (0.14, -yw * 1.1), (0.34, yw * 0.7), (0.46, -yw * 0.7), (0.64, yw), (0.80, 0.0)]
    d = polyline(X, Y, main)
    img += C("#F7E733") * (0.45 * np.exp(-d / 0.02))[..., None]
    paint(img, d < 0.014, "#F7E733")
    paint(img, d < 0.005, "#FFFDE0")
    sec = [(x + 0.07, -y * 0.9 + (0.06 if not ski else 0.0)) for x, y in main[1:-2]]
    d2 = polyline(X, Y, sec)
    img += C("#FF2E88") * (0.3 * np.exp(-d2 / 0.012))[..., None]
    paint(img, d2 < 0.004, "#FF2E88")
    return img

def melt(X, Y, ski):
    s = 2.4
    qx = fbm(X * s, Y * s, 4, 71); qy = fbm(X * s + 5.2, Y * s + 1.3, 4, 83)
    r = fbm(X * s + 4 * qx, Y * s + 4 * qy, 4, 97)
    k = r * 9.0
    pal = ["#2E1A47", "#7B2CBF", "#F15BB5", "#FF6D00", "#FFB703", "#A7C957"]
    i = np.floor(k).astype(int) % len(pal)
    img = np.stack([C(c) for c in pal])[i]
    m, _ = isolines(k, X, Y, 1.0, 0.0022)
    paint(img, m, "#1A0F2B")
    return img

FOX_V = {"P0": (0.62, 0.0), "P1": (0.42, 0.24), "P2": (1.12, 0.62), "P3": (0.26, 0.86), "P4": (-0.10, 0.98),
         "P5": (-0.42, 0.62), "P6": (-0.78, 0.18), "P7": (-0.92, 0.0), "P8": (0.02, 0.30), "P9": (0.10, 0.55),
         "P10": (-0.22, 0.17), "P11": (0.20, 0.0), "P12": (-0.47, 0.0)}
FOX_T = [("P0", "P1", "P11", "#E8742A"), ("P1", "P8", "P11", "#D9661F"), ("P1", "P2", "P3", "#C4541C"),
         ("P1", "P3", "P9", "#F08A3A"), ("P1", "P9", "P8", "#E07020"), ("P3", "P4", "P9", "#F3A65A"),
         ("P4", "P5", "P9", "#F4EDE4"), ("P9", "P5", "P10", "#E9E0D4"), ("P9", "P10", "P8", "#EFE7DC"),
         ("P8", "P10", "P11", "#E07A35"), ("P11", "P10", "P12", "#EB8A44"), ("P10", "P5", "P6", "#F4EDE4"),
         ("P10", "P6", "P12", "#E6DDD0"), ("P12", "P6", "P7", "#DCD2C4")]

def fox(X, Y, ski):
    # the ground: dark teal in big facets
    tri = 0.07
    a = X / tri; b = Y / tri + a * 0.5
    ia, ib = np.floor(a), np.floor(b)
    up = ((a - ia) + (b - ib)) > 1.0
    sh = hash2(ia.astype(np.int64) + 999, ib.astype(np.int64) * 2 + up + 999, 41)
    img = mix("#0E3B43", "#12505A", sh * 0.8)
    S, cx = 0.14, 0.36
    A = (X - cx) / S; B = Y / S
    for side in (1, -1):
        for p, q, r_, col in FOX_T:
            pts = [(FOX_V[n][0], FOX_V[n][1] * side) for n in (p, q, r_)]
            c = C(col) * (1.0 if side > 0 else 0.9)
            paint(img, poly(A, B, pts), c)
        ear = [(0.48, 0.36 * side), (0.98, 0.60 * side), (0.36, 0.74 * side)]
        paint(img, poly(A, B, ear), "#3A2418")
        eye = [(0.10, 0.27 * side), (0.17, 0.40 * side), (0.12, 0.52 * side), (0.05, 0.40 * side)]
        paint(img, poly(A, B, eye), "#1E1410")
        paint(img, np.hypot(A - 0.13, B - 0.36 * side) < 0.025, "#F4EDE4")
    paint(img, poly(A, B, [(-0.80, 0.11), (-0.80, -0.11), (-0.95, 0.0)]), "#1E1410")
    tail = [([(-0.28, -0.13), (-0.42, 0.05), (-0.50, -0.14)], "#E8742A"), ([(-0.42, 0.05), (-0.58, 0.11), (-0.50, -0.14)], "#D9661F"),
            ([(-0.50, -0.14), (-0.58, 0.11), (-0.66, -0.06)], "#F08A3A"), ([(-0.58, 0.11), (-0.74, 0.06), (-0.66, -0.06)], "#F4EDE4"),
            ([(-0.66, -0.06), (-0.74, 0.06), (-0.77, -0.02)], "#E9E0D4"), ([(-0.28, -0.13), (-0.30, 0.0), (-0.42, 0.05)], "#C4541C")]
    sc = 0.4 if ski else 1.0
    for pts, col in tail:
        paint(img, poly(X, Y, [(x, y * sc) for x, y in pts]), col)
    return img

def leopard(X, Y, ski):
    img = mix("#DAD7CE", "#E6DCC8", sstep(-0.8, 0.8, X))
    g = 0.068
    Xw = X + (fbm(X * 20, Y * 20, 3, 5) - 0.5) * 0.012; Yw = Y + (fbm(X * 20 + 4, Y * 20, 3, 6) - 0.5) * 0.012
    gx, gy = np.floor(Xw / g), np.floor(Yw / g)
    best = np.full(X.shape, 9.0, np.float32); bx = np.zeros_like(best); by = np.zeros_like(best); br = np.zeros_like(best)
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            cx_, cy_ = gx + ox, gy + oy
            i1, i2 = cx_.astype(np.int64) + 7000, cy_.astype(np.int64) + 7000
            px = (cx_ + 0.2 + 0.6 * hash2(i1, i2, 1)) * g; py = (cy_ + 0.2 + 0.6 * hash2(i1, i2, 2)) * g
            d = np.hypot(Xw - px, Yw - py)
            closer = d < best
            best = np.where(closer, d, best); bx = np.where(closer, Xw - px, bx); by = np.where(closer, Yw - py, by)
            br = np.where(closer, hash2(i1, i2, 3), br)
    ang = np.arctan2(by, bx)
    gap = np.sin(ang * 3 + br * 6.28) > 0.62
    solid = br < 0.28
    r1, r2 = 0.012 + 0.005 * br, 0.021 + 0.005 * br
    paint(img, ~solid & (best < r1), "#BFAE8E")
    paint(img, ~solid & (best >= r1) & (best < r2) & ~gap, "#2F2C28")
    paint(img, solid & (best < 0.008 + 0.004 * br), "#2F2C28")
    return img

def koi_fish(X, Y, center, R, th0, length, cols, seed):
    """A koi swimming round a circle: its spine an arc, its width a profile along it, a fan tail, two fins."""
    N = 70
    s = np.linspace(0, 1, N)
    th = th0 - s * length / R
    px = center[0] + R * np.cos(th); py = center[1] + R * np.sin(th)
    best = np.full(X.shape, 9.0, np.float32); bs = np.zeros_like(best)
    for k in range(N):
        d = np.hypot(X - px[k], Y - py[k])
        c = d < best
        best = np.where(c, d, best); bs = np.where(c, s[k], bs)
    width = 0.040 * np.sin(np.pi * np.clip(bs * 0.92 + 0.08, 0, 1)) ** 0.7 * (1 - 0.55 * bs)
    body = best < width
    # the fan tail past the end, fins at a quarter
    tx, ty = px[-1], py[-1]
    tdir = np.array([px[-1] - px[-2], py[-1] - py[-2]]); tdir /= np.linalg.norm(tdir)
    rel = np.stack([X - tx, Y - ty], -1)
    along = rel[..., 0] * tdir[0] + rel[..., 1] * tdir[1]; side = -rel[..., 0] * tdir[1] + rel[..., 1] * tdir[0]
    tail = (along > -0.004) & (along < 0.065) & (np.abs(side) < 0.006 + along * 0.65) & (np.abs(side) > along * 0.35 - 0.004)
    k = int(0.26 * N)
    fdir = np.array([px[k + 1] - px[k], py[k + 1] - py[k]]); fdir /= np.linalg.norm(fdir)
    nrm = np.array([-fdir[1], fdir[0]])
    fins = np.zeros(X.shape, bool)
    for sd in (1, -1):
        fc = np.array([px[k], py[k]]) + nrm * sd * 0.046 + fdir * 0.008
        rx, ry = X - fc[0], Y - fc[1]
        a_ = rx * fdir[0] + ry * fdir[1]; b_ = rx * nrm[0] + ry * nrm[1]
        fins |= (a_ / 0.026) ** 2 + (b_ / 0.015) ** 2 < 1
    patch = fbm(bs[..., None][..., 0] * 6 + seed, np.sign(side + 1e-9) * 0.5 + best * 40 + seed, 3, seed) > 0.52
    return body, tail, fins, patch, bs

def koi(X, Y, ski):
    rip = np.abs(np.sin(fbm(X * 6, Y * 6, 4, 12) * 40))
    img = mix("#0D2B3E", "#174A63", (rip < 0.12).astype(np.float32) * 0.7)
    for c, r in (((0.60, 0.07), 0.05), ((-0.55, -0.06), 0.048), ((-0.69, 0.085), 0.03)):
        d = np.hypot(X - c[0], Y - c[1]); ang = np.arctan2(Y - c[1], X - c[0])
        pad = (d < r) & ~((np.abs(ang - 0.6) < 0.25) & (d > r * 0.15))
        paint(img, pad, "#3F7D4E"); paint(img, pad & (np.abs(np.sin(ang * 9)) < 0.08) & (d > r * 0.2), "#2E6340")
    cen = (0.05, 0.0)
    for (th0, cols, seed) in ((math.pi * 0.5, ("#F4F1EA", "#E4572E"), 3), (math.pi * 1.5, ("#F29E38", "#1C1C1C"), 8)):
        body, tail, fins, patch, bs = koi_fish(X, Y, cen, 0.092 if not ski else 0.034, th0, 0.24 if not ski else 0.085, cols, seed)
        paint(img, fins | tail, "#EDEFF2", 0.55)
        paint(img, body, cols[0])
        paint(img, body & patch & (bs > 0.08), cols[1])
    return img

def aurora(X, Y, ski):
    img = ramp(["#13294B", "#0A1630", "#03050F"], sstep(-0.45, 0.75, X))
    ix, iy = np.floor(X / 0.006).astype(np.int64) + 9000, np.floor(Y / 0.006).astype(np.int64) + 9000
    star = hash2(ix, iy, 77) > 0.975
    fx = X / 0.006 - np.floor(X / 0.006) - 0.5; fy = Y / 0.006 - np.floor(Y / 0.006) - 0.5
    paint(img, star & (np.hypot(fx, fy) < 0.18 + 0.2 * hash2(ix, iy, 78)), "#EAF2FF", 0.9)
    def curtain(x0, amp, fq, ph, h, cols, strength):
        xc = x0 + amp * np.sin(Y * fq + ph) + 0.04 * (fbm(Y * 8 + ph, np.full_like(Y, ph), 3, 5) - 0.5)
        up = X - xc
        glow = np.where(up >= 0, np.exp(-up / h), np.exp(-(up / 0.012) ** 2))
        rays = 0.55 + 0.45 * fbm(Y * 70 + ph, X * 1.5, 3, 9)
        col = ramp(cols, np.clip(up / (h * 2.5), 0, 1))
        return col * (glow * rays * strength)[..., None]
    img += curtain(0.02, 0.08, 14, 1.0, 0.16, ["#3DFFB0", "#2EC4B6", "#9B5DE5"], 0.9)
    img += curtain(0.34, 0.05, 9, 2.5, 0.10, ["#7CFFCB", "#9B5DE5", "#6A3FB5"], 0.55)
    top = ridge_profile(Y, -0.50, 0.14, 14, 4)
    paint(img, X < top, "#05070C")
    paint(img, (X < top) & (X > top - 0.012) & (top > -0.42), "#C9D6E8", 0.8)
    return np.clip(img, 0, 1)

def goldleaf(X, Y, ski):
    img = canvas(X, "#111111")
    gold = mix("#B8892A", "#F1D27A", fbm(X * 60, Y * 60, 3, 3))
    on = np.zeros(X.shape, bool)
    th = np.arctan2(Y, X); r = np.hypot(X, Y)
    on |= ((th * 24 / (2 * np.pi)) % 1.0 < 0.12) & (r > 0.07) & (r < 0.30)
    for end in (0.78, -0.78):
        rr = np.hypot(X - end, Y)
        on |= (((rr / 0.024) % 1.0) < 0.16) & (rr < 0.26) & (rr > 0.04)
    p = np.abs(X) + np.abs(Y) * 1.2
    on |= (np.abs(p - 0.055) < 0.0035) | (p < 0.03)
    on |= (np.abs(np.abs(Y) - 0.124) < 0.0016) | (np.abs(np.abs(Y) - 0.116) < 0.0010)
    if ski: on |= (np.abs(np.abs(Y) - 0.044) < 0.0012)
    return np.where(on[..., None], gold, img)

def hsv(h, s, v):
    h = (h % 1.0) * 6; i = np.floor(h).astype(int) % 6; f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    r = np.choose(i, [v, q, p, p, t, v]); g = np.choose(i, [t, v, v, q, p, p]); b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], -1).astype(np.float32)

def prism(X, Y, ski):
    g = 0.06
    gx, gy = np.floor(X / g), np.floor(Y / g)
    best = np.full(X.shape, 9.0, np.float32); second = np.full(X.shape, 9.0, np.float32); rnd = np.zeros_like(best)
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            cx_, cy_ = gx + ox, gy + oy
            i1, i2 = cx_.astype(np.int64) + 3000, cy_.astype(np.int64) + 3000
            px = (cx_ + hash2(i1, i2, 11)) * g; py = (cy_ + hash2(i1, i2, 12)) * g
            d = np.hypot(X - px, Y - py)
            closer = d < best
            second = np.where(closer, best, np.minimum(second, d))
            best = np.where(closer, d, best); rnd = np.where(closer, hash2(i1, i2, 13), rnd)
    hue = X * 0.9 + Y * 2.5 + rnd * 0.35
    img = hsv(hue, 0.42, 1.0)
    ang = rnd * np.pi
    lines = 0.93 + 0.07 * np.sin((X * np.cos(ang) + Y * np.sin(ang)) * 1100)
    img *= lines[..., None]
    paint(img, (second - best) < 0.0018, "#FFFFFF", 0.85)
    ix, iy = np.floor(X / 0.01).astype(np.int64) + 50, np.floor(Y / 0.01).astype(np.int64) + 50
    sp = (hash2(ix, iy, 21) > 0.985)
    fx = np.abs(X / 0.01 - np.floor(X / 0.01) - 0.5); fy = np.abs(Y / 0.01 - np.floor(Y / 0.01) - 0.5)
    paint(img, sp & (((fx < 0.06) & (fy < 0.4)) | ((fy < 0.06) & (fx < 0.4))), "#FFFFFF")
    return img

# key, top, (base style, colours), rail
DESIGNS = [
    ("SunsetStack", sunset, ("circle", "#2B1B4A", "#FF8C42", "#FFD166"), "#2B1B4A"),
    ("Topo", topo, ("stripe", "#E9E2CC", "#1E3D2F", "#FF7A3D"), "#1E3D2F"),
    ("Timber", timber, ("split", "#1A1A1A", "#C98E52", "#E0B27A"), "#1A1A1A"),
    ("Tide", tide, ("stripe", "#F1E9DA", "#1B2A57", "#3A5A9B"), "#1B2A57"),
    ("Ridgeline", ridgeline, ("chevron", "#2C2443", "#F29E7C"), "#2C2443"),
    ("Camo", camo, ("split", "#24261C", "#56633A", "#9C9A6A"), "#24261C"),
    ("Primary", primary, ("split", "#1D4E89", "#D62828", "#F4B400"), "#141414"),
    ("Checkmate", checkmate, ("stripe", "#111111", "#E03131", "#F4F4F0"), "#111111"),
    ("Nordic", nordic, ("stripe", "#B3202A", "#F5F0E6", "#F5F0E6"), "#F5F0E6"),
    ("PopDots", popdots, ("circle", "#E63946", "#F6F1E7", "#1D7FC4"), "#111111"),
    ("Volt", volt, ("chevron", "#0B0B12", "#F7E733"), "#F7E733"),
    ("Melt", melt, ("fade", "#7B2CBF", "#FF6D00", "#FFB703"), "#2E1A47"),
    ("Fox", fox, ("chevron", "#0E3B43", "#E8742A"), "#E8742A"),
    ("Leopard", leopard, ("split", "#2F2C28", "#DAD7CE", "#BFAE8E"), "#2F2C28"),
    ("Koi", koi, ("circle", "#0D2B3E", "#E4572E", "#F4F1EA"), "#F4F1EA"),
    ("Aurora", aurora, ("fade", "#03050F", "#2EC4B6", "#9B5DE5"), "#03050F"),
    ("GoldLeaf", goldleaf, ("stripe", "#111111", "#D4A73A", "#F1D27A"), "#D4A73A"),
    ("Prism", prism, ("fade", "#C8CCD4", "#9B5DE5", "#FFFFFF"), "#E8ECF4"),
]

def region(fn, rows, hl, hw, ski=False, cols=W_TEX):
    X, Y = grid(cols, rows, hl, hw)
    return down(fn(X, Y, ski) if ski is not None else fn(X, Y)).astype(np.float32)

def texture(top, base, rail):
    img = np.zeros((H_TEX, W_TEX, 3), np.float32)
    img[:] = C(rail)
    y0, n = TOP; img[y0:y0 + n] = region(top, n, DECK_HL, DECK_HW, False)
    y0, n = BASE
    style = base
    img[y0:y0 + n] = region(lambda X, Y, s: base_art(X, Y, *style), n, DECK_HL, DECK_HW, False)
    y0, n = SKI; img[y0:y0 + n] = region(top, n, SKI_HL, SKI_HW, True)
    return np.clip(img, 0, 1)

def deck_thumb(top, cw=256, ch=48):
    """The deck seen from above as a little board: the top sheet inside its outline, clear outside."""
    X, Y = grid(cw, ch, DECK_HL, DECK_HW)
    rgb = top(X, Y, False)
    hw = half_width(X)
    a = (np.abs(Y) <= hw).astype(np.float32)
    edge = (np.abs(Y) > hw - 0.006) & (a > 0)
    rgb = np.where(edge[..., None], rgb * 0.55, rgb)
    return down(np.concatenate([rgb, a[..., None]], -1))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", default=".")
    ap.add_argument("--preview", default=None)
    ap.add_argument("--only", default=None)
    a = ap.parse_args()
    out = os.path.join(a.project, "Assets/Summit/Resources/BoardArt")
    thumbs = np.zeros((512, 512, 4), np.float32)
    sheet = []
    for n, (key, top, base, rail) in enumerate(DESIGNS):
        if a.only and key != a.only: continue
        img = texture(top, base, rail)
        write_png(os.path.join(out, key + ".png"), img[::-1])          # (PNG rows run top down; uv v = 0 is the bottom)
        t = deck_thumb(top)
        r, c = n // 2, n % 2
        thumbs[r * 51 + 1: r * 51 + 49, c * 256: c * 256 + 256] = t[::-1][::-1]
        if a.preview: sheet.append((key, img, t))
        print("BOARDART", key, "written")
    if not a.only:
        write_png(os.path.join(out, "BoardArtThumbs.png"), thumbs)
    if a.preview and sheet:
        os.makedirs(a.preview, exist_ok=True)
        rows = []
        for key, img, t in sheet:
            top = img[TOP[0]:TOP[0] + TOP[1]][::-1]
            base = img[BASE[0]:BASE[0] + BASE[1]][::-1]
            ski = img[SKI[0]:SKI[0] + SKI[1]][::-1]
            gap = np.full((6, W_TEX, 3), 0.12, np.float32)
            rows.append(np.concatenate([top, gap, base, gap, ski, np.full((22, W_TEX, 3), 0.06, np.float32)], 0))
        half = (len(rows) + 1) // 2
        colL = np.concatenate(rows[:half], 0); colR = np.concatenate(rows[half:] + [np.zeros((colL.shape[0] - sum(r.shape[0] for r in rows[half:]), W_TEX, 3), np.float32)], 0)
        write_png(os.path.join(a.preview, "boardart_flat.png"), np.concatenate([colL, np.full((colL.shape[0], 16, 3), 0.06, np.float32), colR], 1))
        write_png(os.path.join(a.preview, "boardart_thumbs.png"), thumbs[..., :3] * thumbs[..., 3:] + 0.15 * (1 - thumbs[..., 3:]))

if __name__ == "__main__":
    main()
