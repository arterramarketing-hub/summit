# Offline before/after pictures of the snow groove parallax, for judging it without Unity.
#   python3 Tools/art/previews/snow-parallax/MakePreviews.py
#
# A numpy port of what the shaders do to one flat stretch of snow: the terrain's mapping (metres / 36 per tile), URP's
# one-step parallax shift from SnowGroovesHeight (at the material's own depth, read from Snow.mat), then the albedo,
# normal and detail maps and URP's direct light (Lambert and its specular lobe) with a flat sky colour for ambient.
# The height map is looked up the way the phone will: at 1024, without anisotropic filtering, so it fades with
# distance as in the game. "Before" is the same picture without the shift. An approximation, not the game: the sun,
# sky, exposure and camera are stand-ins, there is no fog, shadow, post-processing or snow sparkle, and the ground is
# a plane.

import os, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
MAT = os.path.join(HERE, "../../../../Assets/Summit/Materials")
W, H, SX, SY = 800, 560, 2, 8          # one side of a picture, and samples per pixel across and up
FOV = 50.0                             # vertical, degrees
TILE = 36.0                            # metres per groove tile

snowmat = open(os.path.join(MAT, "Snow.mat")).read()
DEPTH = float(re.search(r"_Parallax: ([\d.eE+-]+)", snowmat).group(1))
BASE = np.array([0.88, 0.905, 0.95], np.float32)
SMOOTH = 0.32


def load(name, srgb, size=None):
    im = Image.open(os.path.join(MAT, name)).convert("RGB")
    a = np.asarray(im, np.float32)[::-1] / 255.0      # row 0 = v 0, as Unity has it
    if srgb: a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4).astype(np.float32)
    mips = [a]
    while mips[-1].shape[0] > 1:
        m = mips[-1]; mips.append((m[0::2, 0::2] + m[1::2, 0::2] + m[0::2, 1::2] + m[1::2, 1::2]) * 0.25)
    if size: mips = [m for m in mips if m.shape[0] <= size]
    return mips


def bilinear(m, u, v):
    n = m.shape[0]
    x, y = u * n - 0.5, v * n - 0.5
    x0, y0 = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = (x - x0)[..., None].astype(np.float32), (y - y0)[..., None].astype(np.float32)
    xa, xb, ya, yb = x0 % n, (x0 + 1) % n, y0 % n, (y0 + 1) % n
    return (m[ya, xa] * (1 - fx) + m[ya, xb] * fx) * (1 - fy) + (m[yb, xa] * (1 - fx) + m[yb, xb] * fx) * fy


def sample(mips, u, v, lod):
    lod = np.clip(lod, 0, len(mips) - 1.001)
    out = np.zeros(u.shape + (3,), np.float32)
    l0 = np.floor(lod).astype(np.int32); f = (lod - l0).astype(np.float32)
    for l in np.unique(l0):
        k = l0 == l
        out[k] = bilinear(mips[l], u[k], v[k]) * (1 - f[k])[:, None] + bilinear(mips[l + 1], u[k], v[k]) * f[k][:, None]
    return out


ALB, NRM = load("SnowGroovesAlbedo.png", True), load("SnowGroovesNormal.png", False)
DALB, DNRM = load("SnowGrain.png", False), load("SnowGrainNormal.png", False)
HGT = load("SnowGroovesHeight.png", False, 1024)


def norm(a): return a / np.linalg.norm(a, axis=-1, keepdims=True)


def render(cam, pitch, sun_el, sun_az, sun_col, sky_col, parallax):
    """Linear colour of the snow plane y = 0 seen from `cam`, looking down the fall line (+z), and the sky mask."""
    hs, ws = H * SY, W * SX
    t = np.tan(np.radians(FOV) / 2)
    py = (1 - (np.arange(hs) + 0.5) / hs * 2) * t
    px = ((np.arange(ws) + 0.5) / ws * 2 - 1) * t * W / H
    px, py = np.meshgrid(px.astype(np.float32), py.astype(np.float32))
    c, s = np.cos(np.radians(pitch)), np.sin(np.radians(pitch))
    d = norm(np.stack([px, py * c - s, py * s + c], -1))             # camera pitched down by `pitch`
    sky = d[..., 1] > -1e-3
    dist = np.where(sky, 1.0, cam[1] / np.maximum(-d[..., 1], 1e-3))
    p = cam + d * dist[..., None]
    u, v = p[..., 0] / TILE, p[..., 2] / TILE
    # texture footprint of one screen pixel (not of one sample), in tiles
    dux, duy = np.gradient(u, axis=1) * SX, np.gradient(u, axis=0) * SY
    dvx, dvy = np.gradient(v, axis=1) * SX, np.gradient(v, axis=0) * SY
    lx, ly = np.hypot(dux, dvx), np.hypot(duy, dvy)
    major, minor = np.maximum(lx, ly), np.maximum(np.minimum(lx, ly), 1e-12)
    aniso = major / np.minimum(major / minor, 8.0)                   # the colour and normal maps: aniso 8
    view = -d
    if parallax:
        # URP ParallaxOffset1Step, from the height map without anisotropic filtering
        h = sample(HGT, u, v, np.log2(np.maximum(major * 1024, 1e-9)))[..., 1]
        hh = h * DEPTH - DEPTH / 2
        u = u + hh * view[..., 0] / (view[..., 1] + 0.42)
        v = v + hh * view[..., 2] / (view[..., 1] + 0.42)
    alb = sample(ALB, u, v, np.log2(np.maximum(aniso * 2048, 1e-9))) * BASE
    n = sample(NRM, u, v, np.log2(np.maximum(aniso * 2048, 1e-9))) * 2 - 1
    du, dv = u * 3.37 + 0.37, v * 3.37 + 0.71
    dl = np.log2(np.maximum(aniso * 3.37 * 512, 1e-9))
    alb = alb * (2.0 * sample(DALB, du, dv, dl))
    dn = sample(DNRM, du, dv, dl) * 2 - 1
    dn[..., :2] *= 0.5
    # BlendNormalRNM
    a = n + np.array([0, 0, 1], np.float32); b = dn * np.array([-1, -1, 1], np.float32)
    n = norm(a / a[..., 2:3] * np.sum(a * b, -1, keepdims=True) - b)
    nw = np.stack([n[..., 0], n[..., 2], n[..., 1]], -1)             # tangent x, bitangent z, normal y
    L = np.array([np.cos(np.radians(sun_el)) * np.sin(np.radians(sun_az)), np.sin(np.radians(sun_el)),
                  np.cos(np.radians(sun_el)) * np.cos(np.radians(sun_az))], np.float32)
    ndl = np.clip(np.sum(nw * L, -1), 0, 1)[..., None]
    hv = norm(view + L)
    noh = np.clip(np.sum(nw * hv, -1), 0, 1); loh = np.clip(np.sum(hv * L, -1), 0, 1)
    r = (1 - SMOOTH) ** 2; r2 = r * r
    dd = noh * noh * (r2 - 1) + 1.00001
    spec = (r2 / (dd * dd * np.maximum(0.1, loh * loh) * (r * 4 + 2)))[..., None]
    col = (alb * 0.96 + 0.04 * spec) * sun_col * ndl + alb * 0.96 * sky_col
    col = np.where(sky[..., None], sky_col * 1.6, col)
    return col.reshape(H, SY, W, SX, 3).mean((1, 3))


def to_srgb(lin, exposure):
    x = lin * exposure
    x = np.clip(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14), 0, 1)       # a filmic curve
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


FONT = ImageFont.load_default(size=22)


def label(im, x, text):
    d = ImageDraw.Draw(im)
    box = d.textbbox((x + 14, 10), text, font=FONT)
    d.rectangle((box[0] - 6, box[1] - 4, box[2] + 6, box[3] + 4), fill=(0, 0, 0))
    d.text((x + 14, 10), text, fill=(255, 255, 255), font=FONT)


def save(name, left, right, lt, rt):
    im = Image.fromarray((np.hstack([left, right]) * 255 + 0.5).astype(np.uint8))
    label(im, 0, lt); label(im, W, rt)
    ImageDraw.Draw(im).line((W, 0, W, H), fill=(0, 0, 0), width=2)
    for q in (90, 86, 82, 78, 72, 66, 60):
        im.save(os.path.join(HERE, name), quality=q, optimize=True)
        if os.path.getsize(os.path.join(HERE, name)) < 490_000: break
    print("PREVIEW %s: %d KB (quality %d)" % (name, os.path.getsize(os.path.join(HERE, name)) // 1024, q))


# a stretch where the grooves are strong: the 12 m ahead of the camera with the most relief in the height map
h0 = HGT[0][..., 1]
best = max(((h0[np.ix_(np.arange(j, j + 340) % 1024, np.arange(i - 85, i + 85) % 1024)].std(), i, j)
            for j in range(0, 1024, 32) for i in range(0, 1024, 32)))
X0, Z0 = best[1] / 1024 * TILE, best[2] / 1024 * TILE - 1.0
print("PREVIEW depth %.4g of a tile (%.1f cm); camera over x %.1f m, z %.1f m of the tile" % (DEPTH, DEPTH * TILE * 100, X0, Z0))

LOW = dict(sun_el=10.0, sun_az=55.0, sun_col=np.array([1.5, 1.12, 0.78], np.float32), sky_col=np.array([0.26, 0.33, 0.50], np.float32))
NOON = dict(sun_el=62.0, sun_az=55.0, sun_col=np.array([1.25, 1.2, 1.1], np.float32), sky_col=np.array([0.36, 0.44, 0.60], np.float32))
SHOTS = [("1-low-sun-2m.jpg", 2.0, 16.0, LOW, 0.95), ("2-low-sun-1m.jpg", 1.0, 20.0, LOW, 0.95), ("3-midday-2m.jpg", 2.0, 16.0, NOON, 0.62)]
for name, height, pitch, light, exposure in SHOTS:
    cam = np.array([X0, height, Z0], np.float32)
    before = to_srgb(render(cam, pitch, parallax=False, **light), exposure)
    after = to_srgb(render(cam, pitch, parallax=True, **light), exposure)
    save(name, before, after, "BEFORE", "AFTER")
    diff = np.abs(after - before) * 255
    print("PREVIEW   change per pixel, of 255: mean %.2f, 99th percentile %.1f, largest %.1f; over 2/255 in %.1f%% of the picture"
          % (diff.mean(), np.percentile(diff, 99), diff.max(), (diff.max(-1) > 2).mean() * 100))
    if name.startswith("2"):
        AMP = 8
        d = np.clip(np.abs(after - before) * AMP, 0, 1)
        save("4-difference-low-sun-1m.jpg", after, d, "AFTER", "DIFFERENCE x%d (black = no change)" % AMP)
