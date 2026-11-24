# Makes the camp fire's textures (Assets/Summit/Resources/Camp/Fire/), procedurally (numpy only, no source images):
#   python3 Tools/MakeCampfireTex.py [outdir]
#
#   WoodAtlas.png    the fire's wood, one material for all of it (CampFirePit.cs maps each face into its part):
#                      left half (u 0..0.5)       bark: grey-brown plates and dark furrows along the log, a little
#                                                 lichen; tiles round the log and along it
#                      top right (u, v 0.5..1)    a sawn end: rings round an off-centre pith, a few drying checks,
#                                                 the inner bark's red-brown band and the bark round it
#                      bottom right (u .5..1, v 0..0.5)  a split face: pale wood, the grain and the rings' lines
#                                                 running along it, torn fibres
#   CharLog.png      a burning log's char: black alligator blocks with deep cracks, white ash on them in patches
#                    (tiles both ways; the logs in the fire wear it all over)
#   CharLogGlow.png  its emission: the cracks glowing orange in patches, dim red embers on some blocks
#   FireBed.png      the fire's bed, a disc (the ring's inside): charcoal heaped in the middle with ash between the
#                    pieces, pale powdery ash out to the stones, scorched and wet near them
#   FireBedGlow.png  its emission: the coals' edges and cracks glowing, hottest in the middle, none in the ash
#   Flame.png        four flame tongues, 2 x 2 (a particle flipbook): white, brightest in the core, in alpha their
#                    shape (soft edges, fading at the root and the tip). The particles tint them.
import numpy as np, sys, os, zlib, struct

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/Summit/Resources/Camp/Fire"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(71)


def png(path, a):
    """8-bit RGB or RGBA (rows top first)."""
    a = np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8)
    h, w, c = a.shape
    raw = b"".join(b"\x00" + a[r].tobytes() for r in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6 if c == 4 else 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    print("CAMPFIRETEX wrote", path, a.shape)


def noise(w, h, cx, cy, octaves=4, seed=0):
    """Tiling value noise in -1..1 over a w x h image: cx x cy cells at the first octave."""
    r = np.random.default_rng(seed)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        nx, ny = cx * 2 ** o, cy * 2 ** o
        g = r.uniform(-1, 1, (ny, nx)).astype(np.float32)
        u = xs / w * nx; v = ys / h * ny
        i0 = np.floor(u).astype(int) % nx; j0 = np.floor(v).astype(int) % ny
        i1 = (i0 + 1) % nx; j1 = (j0 + 1) % ny
        fu = u - np.floor(u); fv = v - np.floor(v)
        fu = fu * fu * (3 - 2 * fu); fv = fv * fv * (3 - 2 * fv)
        a = g[j0, i0] * (1 - fu) + g[j0, i1] * fu
        b = g[j1, i0] * (1 - fu) + g[j1, i1] * fu
        out += amp * (a * (1 - fv) + b * fv); tot += amp; amp *= 0.5
    return out / tot


def worley(w, h, n, sx=1.0, sy=1.0, seed=0, blocky=False):
    """Tiling cellular noise: F1, F2 (distances in the image's own units, x scaled by sx, y by sy) and the cell's id.
    blocky: the larger of the two distances (square cells, like charred wood's checks), not the straight line."""
    r = np.random.default_rng(seed)
    pts = r.uniform(0, 1, (n, 2)) * [w, h]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    f1 = np.full((h, w), 1e9, np.float32); f2 = np.full((h, w), 1e9, np.float32); idx = np.zeros((h, w), np.int32)
    for k, (px, py) in enumerate(pts):
        dx = np.abs(xs - px); dx = np.minimum(dx, w - dx) * sx
        dy = np.abs(ys - py); dy = np.minimum(dy, h - dy) * sy
        d = np.maximum(dx, dy) if blocky else np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, k, idx)
        f1 = np.where(closer, d, f1)
    return f1, f2, idx


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    return a * (1 - t[..., None]) + b * t[..., None]


def col(r, g, b):
    return np.array([r, g, b], np.float32)


# ---------------------------------------------------------------- the wood atlas (512 x 512)

A = 512
atlas = np.zeros((A, A, 3), np.float32)

# bark (a 256 wide, 512 tall column; rows: along the log)
W, H = 256, 512
f1, f2, ids = worley(W, H, 120, sx=1.0, sy=0.34, seed=3)
edge = f2 - f1
furrow = 1 - smooth(0.6, 4.5, edge + 1.5 * noise(W, H, 24, 12, 2, 7))   # 1 in the furrows between the plates
fine = noise(W, H, 16, 6, 4, 4)
streak = noise(W, H, 48, 3, 3, 5)                         # long fibres along the log
plateTone = (np.random.default_rng(6).uniform(0.82, 1.12, 120)[ids]).astype(np.float32)
ridge = noise(W, H, 90, 5, 2, 17)                          # each plate's own fine ridges along the log
plate = col(0.40, 0.34, 0.28) * (plateTone * (0.9 + 0.1 * fine + 0.1 * streak + 0.08 * ridge) * (0.9 + 0.1 * smooth(0.0, 6.0, f1)))[..., None]
deep = mix(np.broadcast_to(col(0.30, 0.17, 0.10), (H, W, 3)), np.broadcast_to(col(0.10, 0.07, 0.05), (H, W, 3)), smooth(0.4, 1.0, furrow))
bark = mix(plate, deep, smooth(0.2, 0.8, furrow))
# flaking: a lighter, greyer top edge on each plate (the weathered outer scale)
flake = smooth(0.55, 0.95, noise(W, H, 10, 20, 3, 8) * 0.5 + 0.5) * (1 - furrow)
bark = mix(bark, bark * 0.6 + col(0.55, 0.53, 0.5) * 0.4, flake * 0.5)
# lichen, here and there on the plates
lich = smooth(0.62, 0.7, noise(W, H, 6, 10, 4, 9) * 0.5 + 0.5) * smooth(0.2, 0.6, noise(W, H, 40, 60, 2, 10)) * (1 - furrow)
bark = mix(bark, col(0.58, 0.62, 0.52), lich * 0.55)
atlas[:, :256] = bark

# a sawn end (256 x 256, top right: rows 0..255 of the image)
E = 256
ys, xs = np.mgrid[0:E, 0:E].astype(np.float32)
cx, cy = E / 2 + 6, E / 2 - 4                              # (the pith a little off centre)
dx, dy = xs - cx, ys - cy
rr = np.sqrt(dx * dx + dy * dy) / (E / 2 - 3)
ang = np.arctan2(dy, dx)
warp = 0.025 * noise(E, E, 6, 6, 3, 11) + 0.012 * np.sin(ang * 3 + 1.0)
r2 = rr + warp
ring = 0.5 + 0.5 * np.cos(2 * np.pi * r2 * 15)
late = smooth(0.72, 0.98, ring)                            # the late wood's dark line in each ring
heart = mix(np.broadcast_to(col(0.66, 0.48, 0.32), (E, E, 3)), np.broadcast_to(col(0.84, 0.70, 0.50), (E, E, 3)), smooth(0.25, 0.8, r2))
end = heart * (1 - 0.2 * late)[..., None] * (0.94 + 0.06 * noise(E, E, 20, 20, 3, 12))[..., None]
# drying checks: a few cracks out from the middle
for k in range(5):
    a0 = rng.uniform(-np.pi, np.pi); r0 = rng.uniform(0.15, 0.35); r1 = rng.uniform(0.6, 0.92)
    da = np.angle(np.exp(1j * (ang - a0 - 0.03 * np.sin(rr * 7 + k))))
    width = 0.02 + 0.05 * smooth(r0, r1, rr) * (1 - smooth(r1 - 0.12, r1, rr))
    crack = (1 - smooth(0.0, 1.0, np.abs(da) * rr / np.maximum(width, 1e-3))) * (rr > r0) * (rr < r1)
    end = mix(end, col(0.14, 0.09, 0.06), crack * 0.9)
# weathered grey a little, the saw's marks across it faintly
end = mix(end, col(0.62, 0.6, 0.57), np.full((E, E), 0.12, np.float32) + 0.08 * smooth(0.6, 1.0, rr))
end *= (1 + 0.04 * np.sin((xs * 0.9 + ys * 0.35) * 0.35))[..., None]
# the inner bark's band and the bark round it
end = mix(end, col(0.48, 0.28, 0.17), smooth(0.86, 0.9, rr))
outer = np.broadcast_to(col(0.20, 0.15, 0.11), (E, E, 3)) * (0.85 + 0.2 * noise(E, E, 24, 24, 2, 13))[..., None]
end = mix(end, outer, smooth(0.92, 0.95, rr))
atlas[0:256, 256:512] = end

# a split face (256 x 256, bottom right: rows 256..511), the log's length along the rows
S = 256
ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
lines = 0.5 + 0.5 * np.cos(2 * np.pi * (xs / S * 11 + 0.08 * noise(S, S, 3, 2, 3, 14)))
fib = noise(S, S, 64, 4, 3, 15)
split = col(0.80, 0.66, 0.47) * (0.92 + 0.06 * fib - 0.12 * smooth(0.8, 1.0, lines))[..., None]
torn = smooth(0.55, 0.8, noise(S, S, 30, 3, 3, 16) * 0.5 + 0.5)
split = mix(split, col(0.6, 0.47, 0.32), torn * 0.35)
split = mix(split, col(0.66, 0.64, 0.6), np.full((S, S), 0.1, np.float32))
atlas[256:512, 256:512] = split
png(os.path.join(OUT, "WoodAtlas.png"), atlas)


# ---------------------------------------------------------------- the char (256 x 512, tiling)

W, H = 256, 512
f1, f2, ids = worley(W, H, 300, sx=1.0, sy=0.6, seed=21)
edge = f2 - f1 + 1.2 * noise(W, H, 40, 80, 2, 28)
crack = 1 - smooth(0.6, 3.0, edge)                        # the cracks between the blocks
dome = smooth(0.0, 9.0, f1)                                # each block's middle a little proud
tone = np.random.default_rng(22).uniform(0.75, 1.3, 300)[ids].astype(np.float32)
blk = col(0.085, 0.08, 0.075) * (tone * (1.0 - 0.25 * dome) + 0.25 * noise(W, H, 30, 60, 3, 23))[..., None]
ash = smooth(0.55, 0.75, noise(W, H, 5, 10, 4, 24) * 0.5 + 0.5 + 0.25 * noise(W, H, 64, 128, 2, 29)) * (1 - crack)
blk = mix(blk, col(0.56, 0.54, 0.51) * (0.85 + 0.15 * noise(W, H, 50, 100, 2, 25))[..., None], ash * 0.85)
char = mix(blk, col(0.015, 0.012, 0.01), crack)
png(os.path.join(OUT, "CharLog.png"), char)
hot = smooth(0.58, 0.8, noise(W, H, 4, 8, 4, 26) * 0.5 + 0.5) * (1 - ash)
thin = 1 - smooth(0.3, 1.6, edge)
glow = col(1.0, 0.42, 0.08) * (thin * hot)[..., None] + col(0.55, 0.08, 0.02) * (smooth(0.6, 0.9, noise(W, H, 8, 16, 3, 27) * 0.5 + 0.5) * (1 - ash) * (1 - crack) * 0.35)[..., None]
png(os.path.join(OUT, "CharLogGlow.png"), glow)


# ---------------------------------------------------------------- the fire's bed (512 x 512, a disc)

B = 512
ys, xs = np.mgrid[0:B, 0:B].astype(np.float32)
dx, dy = (xs - B / 2) / (B / 2), (ys - B / 2) / (B / 2)
r = np.sqrt(dx * dx + dy * dy) + 0.06 * noise(B, B, 5, 5, 3, 31)
f1, f2, ids = worley(B, B, 420, seed=32)
k = np.random.default_rng(38)
keep = k.uniform(0, 1, 420)[ids].astype(np.float32)
dense = np.clip(1.25 - r * 1.75, 0, 1)                             # a heap of it in the middle, odd bits out to the stones
gap = 1.5 + 16.0 * (1 - dense) ** 1.5                              # the ash between the pieces (px)
edge = f2 - f1 + 3.0 * noise(B, B, 40, 40, 2, 39)
piece = smooth(gap - 1.0, gap + 1.5, edge) * (keep < 0.12 + 0.88 * dense)
pieces = np.clip(piece, 0, 1)
size = f1 + 0.5 * (f2 - f1)                                        # (how far in from its edge, for the rims below)
grain = noise(B, B, 160, 160, 2, 34)
ashc = col(0.60, 0.58, 0.55) * (0.86 + 0.1 * noise(B, B, 30, 30, 3, 35) + 0.08 * grain)[..., None]
ashc = mix(ashc, col(0.34, 0.33, 0.32), smooth(0.45, 0.8, noise(B, B, 10, 10, 3, 36) * 0.5 + 0.5) * 0.45)
specks = smooth(0.55, 0.75, noise(B, B, 200, 200, 1, 37)) * (1 - pieces)
ashc = mix(ashc, col(0.08, 0.075, 0.07), specks * 0.8)
tone = np.random.default_rng(36).uniform(0.6, 1.5, 420)[ids].astype(np.float32)
charc = col(0.055, 0.052, 0.05) * (tone * (0.8 + 0.4 * smooth(gap + 6.0, gap, edge)))[..., None]
bed = mix(ashc, charc, pieces)
bed = mix(bed, col(0.2, 0.19, 0.18), smooth(0.2, 0.9, dense) * (1 - pieces) * 0.6)   # the ash in the heap sooty
coal = smooth(gap + 5.0, gap + 1.0, edge) * pieces                 # each piece's rim
# scorched and damp out to the stones; black under where the logs lie
bed = mix(bed, col(0.16, 0.14, 0.13), smooth(0.72, 0.98, r))
bed = mix(bed, col(0.03, 0.03, 0.03), smooth(0.5, 0.15, r) * 0.25)
png(os.path.join(OUT, "FireBed.png"), bed)
heat = smooth(0.6, 0.12, r) * smooth(0.25, 0.6, noise(B, B, 9, 9, 3, 41) * 0.5 + 0.5)
between = (1 - pieces) * smooth(0.4, 0.9, dense)                  # the hot gaps deep in the heap
fireglow = col(1.0, 0.42, 0.08) * (heat * (coal * 0.8 + between))[..., None] + col(0.75, 0.12, 0.02) * (heat * pieces * 0.3)[..., None]
png(os.path.join(OUT, "FireBedGlow.png"), fireglow)


# ---------------------------------------------------------------- flame tongues (256 x 256: 2 x 2 of 128)

F = 128
fl = np.zeros((256, 256, 4), np.float32)
for k in range(4):
    ys, xs = np.mgrid[0:F, 0:F].astype(np.float32)
    y = 1 - ys / (F - 1)                                   # 0 at the root (the bottom row), 1 at the top
    x = xs / (F - 1) * 2 - 1
    ph = rng.uniform(0, 2 * np.pi); lean = rng.uniform(-0.12, 0.12); licks = rng.uniform(4.0, 6.5)
    sway = (0.18 * np.sin(ph + y * licks) + lean) * y ** 1.4
    n = noise(F, F, 4, 4, 3, 40 + k)
    xx = x - sway - 0.08 * n * y
    width = 0.5 * (1 - y) ** 1.1 * (0.5 + 0.5 * np.sin(np.minimum(y * 6, np.pi / 2))) + 0.015
    body = 1 - smooth(0.55, 1.0, np.abs(xx) / np.maximum(width, 1e-3))
    # a second, smaller tongue licking off one side, part way up
    side = rng.choice([-1, 1])
    y2 = np.clip((y - 0.3) / 0.6, 0, 1)
    w2 = 0.2 * (1 - y2) ** 0.8 * (y > 0.3)
    x2 = xx - side * (0.28 + 0.15 * y2)
    body = np.maximum(body, (1 - smooth(0.5, 1.0, np.abs(x2) / np.maximum(w2, 1e-3))) * (w2 > 0.01))
    a = body * smooth(0.0, 0.18, y) * (1 - smooth(0.75, 1.0, y)) * (0.85 + 0.15 * n)
    core = 1 - smooth(0.2, 0.8, np.abs(xx) / np.maximum(width, 1e-3))
    lum = 0.55 + 0.45 * core * (1 - smooth(0.3, 0.8, y))
    j, i = divmod(k, 2)
    fl[j * F:(j + 1) * F, i * F:(i + 1) * F, :3] = lum[..., None]
    fl[j * F:(j + 1) * F, i * F:(i + 1) * F, 3] = np.clip(a, 0, 1)
png(os.path.join(OUT, "Flame.png"), fl)
