# Makes the camp's trampled snow (Assets/Summit/Resources/Camp/SnowPrints.png): a 2 m square of packed snow, boot
# prints pressed into it every which way, overlapping, tiling. Procedural (numpy only), no source images.
#   python3 Tools/MakeSnowPrints.py [out.png]
# Its channels (linear data, not colour):
#   R, G  the surface's slope as a normal map's x and y (0.5 = flat): x along the world's x, y along its z
#   B     how deep it is pressed: 1 on the untouched surface, 0 at the bottom of a 4 cm print (the shader darkens it)
#   A     each print's own number, 0.05..1 (0 between prints): the shader shows the prints whose number is under how
#         trampled the snow is there, so lightly trodden snow has a few of them and the busiest all of them
# 512 x 512 texels of 3.9 mm. The shader (Resources/SnowSurface.shader) lays it over the camp's packed snow.
import numpy as np, sys, zlib, struct

N = 512
SIZE = 2.0                 # metres the tile covers
PX = SIZE / N              # metres a texel
rng = np.random.default_rng(1234)

ys, xs = np.mgrid[0:N, 0:N].astype(np.float32) * PX   # (rows: z, columns: x)


def wrap(d):
    """The shortest way across the tile (it tiles)."""
    return (d + SIZE / 2) % SIZE - SIZE / 2


def noise(scale, octaves=3, seed=0):
    """Tiling value noise in -1..1 (a few octaves), features `scale` metres across."""
    r = np.random.default_rng(seed)
    out = np.zeros((N, N), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = max(1, int(round(SIZE / scale)) * (2 ** o))
        g = r.uniform(-1, 1, (cells, cells)).astype(np.float32)
        u = xs / SIZE * cells
        v = ys / SIZE * cells
        i0 = np.floor(u).astype(int) % cells; j0 = np.floor(v).astype(int) % cells
        i1 = (i0 + 1) % cells; j1 = (j0 + 1) % cells
        fu = u - np.floor(u); fv = v - np.floor(v)
        fu = fu * fu * (3 - 2 * fu); fv = fv * fv * (3 - 2 * fv)
        a = g[j0, i0] * (1 - fu) + g[j0, i1] * fu
        b = g[j1, i0] * (1 - fu) + g[j1, i1] * fu
        out += amp * (a * (1 - fv) + b * fv)
        total += amp
        amp *= 0.5
    return out / total


# the packed snow under it all: a gentle unevenness (a centimetre or so) and crumbs
H = 0.006 * noise(0.5, 3, 1) + 0.0015 * noise(0.06, 2, 2)
ident = np.zeros((N, N), np.float32)


def sole(u, v):
    """0..1 inside a boot's sole (u across, v along it, metres from its middle; the toe toward +v): the forefoot and
    the heel, the arch between them narrower, the edges soft over a few millimetres."""
    fore = ((u / 0.056) ** 2 + ((v - 0.055) / 0.095) ** 2)
    heel = ((u / 0.046) ** 2 + ((v + 0.095) / 0.05) ** 2)
    arch = ((u / 0.038) ** 2 + ((v + 0.03) / 0.07) ** 2)
    d = np.minimum(np.minimum(fore, heel), arch)
    return np.clip((1.0 - d) / 0.25, 0.0, 1.0)


def lugs(u, v):
    """The tread: chevrons of lugs across the sole (1 where a lug stands, so the print is shallower there)."""
    c = np.cos((v * 1.0 + np.abs(u) * 0.55) * 2 * np.pi / 0.018)
    side = np.cos(u * 2 * np.pi / 0.03)
    return ((c > 0.35) & (side > -0.6)).astype(np.float32)


# the prints: pairs and strings of them going every which way, the most along two or three common lines (people
# walk the same ways), overlapping
lines = [rng.uniform(0, 2 * np.pi) for _ in range(3)]
for k in range(58):
    cx, cz = rng.uniform(0, SIZE, 2)
    heading = lines[k % 3] + rng.normal(0, 0.35) if k % 4 != 3 else rng.uniform(0, 2 * np.pi)
    depth = rng.uniform(0.015, 0.038)
    s, c = np.sin(heading), np.cos(heading)
    dx, dz = wrap(xs - cx), wrap(ys - cz)
    u = dx * c - dz * s
    v = dx * s + dz * c
    near = (np.abs(u) < 0.12) & (np.abs(v) < 0.2)
    if not near.any():
        continue
    inside = sole(u, v) * near
    tread = lugs(u, v)
    # the print: down `depth`, the lugs leaving ridges a third of the way up; snow pushed up in a low rim round it
    floor = -depth * (1.0 - 0.35 * tread)
    rim = 0.004 * np.clip(1.0 - np.abs(sole(u * 0.82, v * 0.88) - 0.15) / 0.15, 0, 1) * near
    pressed = H * (1 - inside) + (floor + 0.4 * H) * inside
    H = np.where(near, np.where(inside > 0.02, np.minimum(H, pressed), H + rim), H)
    ident = np.where(inside > 0.15, rng.uniform(0.05, 1.0), ident)

# the normal: from the slope, texel to texel (wrapping)
gx = (np.roll(H, -1, axis=1) - np.roll(H, 1, axis=1)) / (2 * PX)
gz = (np.roll(H, -1, axis=0) - np.roll(H, 1, axis=0)) / (2 * PX)
n = np.stack([-gx, -gz, np.ones_like(gx)], axis=-1)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
depthMap = np.clip(1.0 + np.minimum(H, 0.0) / 0.04, 0.0, 1.0)

img = np.zeros((N, N, 4), np.uint8)
img[..., 0] = np.clip((n[..., 0] * 0.5 + 0.5) * 255 + 0.5, 0, 255)
img[..., 1] = np.clip((n[..., 1] * 0.5 + 0.5) * 255 + 0.5, 0, 255)
img[..., 2] = np.clip(depthMap * 255 + 0.5, 0, 255)
img[..., 3] = np.clip(ident * 255 + 0.5, 0, 255)
# (the PNG's first row is the texture's top: v = 1, the world's +z end of the tile)
img = img[::-1]


def png(path, a):
    h, w = a.shape[:2]
    raw = b"".join(b"\x00" + a[r].tobytes() for r in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


out = sys.argv[1] if len(sys.argv) > 1 else "Assets/Summit/Resources/Camp/SnowPrints.png"
png(out, img)
print("SNOWPRINTS wrote", out, "depth: deepest %.3f m, prints' share %.2f" % (-H.min(), float((H < -0.008).mean())))
