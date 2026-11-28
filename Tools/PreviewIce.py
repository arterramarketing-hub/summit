#!/usr/bin/env python3
# Renders before/after preview pictures of the crevasse ice without Unity (Tools/art/previews/crevasse-ice):
# a small ray tracer over a crevasse of the real proportions (CourseDressing.BuildCrevasse: the lip, the walls
# closing to a narrow floor 26.5 m down, their wobble), shaded on the left as the old plain material (a depth
# gradient on URP Lit, roughly) and on the right by a line-for-line port of Resources/IceWall.shader.
# An approximation, for judging the look only: the sky's light, exposure and colour grade are guesses.
#   python3 Tools/PreviewIce.py Tools/art/previews/crevasse-ice
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1]
HALF, DEPTH, LIP = 2.9, 26.5, 0.75
Z0, Z1 = -22.0, 48.0
A = np.asarray(Image.open("Assets/Summit/Resources/Ice/IceAlbedo.png"), np.float32) / 255
N = np.asarray(Image.open("Assets/Summit/Resources/Ice/IceNormal.png").convert("RGB"), np.float32) / 255 * 2 - 1
lin = lambda c: np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
A[..., :3] = lin(A[..., :3])

def mips(T):
    out = [T]
    while out[-1].shape[0] > 1:
        t = out[-1]; out.append((t[0::2, 0::2] + t[1::2, 0::2] + t[0::2, 1::2] + t[1::2, 1::2]) * 0.25)
    return out
AM, NM = mips(A), mips(N)

def tex(M, u, v, lod):
    # bilinear, repeating, v up; one mip level per pixel
    out = np.zeros(u.shape + (M[0].shape[2],), np.float32)
    lv = np.clip(np.round(lod), 0, len(M) - 1).astype(int)
    for k in np.unique(lv):
        m = lv == k; T = M[k]; n = T.shape[0]
        x = u[m] * n - 0.5; y = -v[m] * n - 0.5
        x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
        g = lambda i, j: T[j % n, i % n]
        out[m] = (g(x0, y0) * (1 - fx) + g(x0 + 1, y0) * fx) * (1 - fy) + (g(x0, y0 + 1) * (1 - fx) + g(x0 + 1, y0 + 1) * fx) * fy
    return out

sat = lambda x: np.clip(x, 0, 1)
lerp = lambda a, b, t: a + (b - a) * t
fr = lambda x: x - np.floor(x)
def ss(a, b, x):
    t = sat((x - a) / (b - a)); return t * t * (3 - 2 * t)
def norm(v): return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-6)
def dot(a, b): return (a * b).sum(-1, keepdims=True)
col = lambda *v: np.array(v, np.float32)

def noise(x, y):
    xi, yi = np.floor(x), np.floor(y); fx, fy = x - xi, y - yi
    h = lambda i, j: fr(np.sin(i * 127.1 + j * 311.7) * 43758.5453)
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    return lerp(lerp(h(xi, yi), h(xi + 1, yi), fx), lerp(h(xi, yi + 1), h(xi + 1, yi + 1), fx), fy)

def opening(z): return HALF * np.sqrt(sat(np.minimum(z - Z0, Z1 - z) / 5.0))
def depth_at(z): return np.maximum(DEPTH * np.sqrt(opening(z) / HALF) + 0.0, 0.05)

def width(d, z, side):
    # half the crevasse's width at d metres below the lip, as BuildCrevasse lays the wall out
    op = opening(z); D = depth_at(z)
    e = np.maximum(0, op + 0.6 * (noise(z * 1.3 + side * 17.1, 77.7 + 0 * z) - 0.5) * np.minimum(1, op))
    f = sat((d - LIP) / np.maximum(D - LIP, 0.01))
    wob = (noise(z * 0.8 + side * 5, f * 8 * 1.7 + 3) - 0.5) * 0.7 * np.minimum(1, op) * sat(f * 8)
    lower = lerp(e + 0.22, op * 0.12, f ** 0.75) + wob
    return np.where(d < LIP, lerp(e, e + 0.22, sat(d / LIP)), lower), e, f

def clearance(p):
    x, y, z = p[..., 0], p[..., 1], p[..., 2]; side = np.where(x < 0, -1.0, 1.0)
    w, e, f = width(-y, z, side)
    return np.minimum(w - np.abs(x), y + depth_at(z))

def trace(cam, target, fov, W, H):
    fwd = norm(np.array(target, np.float32) - cam); right = norm(np.cross([0, 1, 0], fwd)); up = np.cross(fwd, right)
    k = np.tan(np.radians(fov) / 2)
    px, py = np.meshgrid((np.arange(W) + 0.5) / W * 2 - 1, 1 - (np.arange(H) + 0.5) / H * 2)
    rd = norm(fwd + right * (px[..., None] * k * W / H) + up * (py[..., None] * k)).astype(np.float32).reshape(-1, 3)
    n = rd.shape[0]; t = np.zeros(n, np.float32); kind = np.zeros(n, int)   # 0 sky, 1 snow, 2 ice
    live = np.ones(n, bool)
    if cam[1] > 0:
        down = rd[:, 1] < -1e-4
        t0 = np.where(down, -cam[1] / np.where(down, rd[:, 1], -1), 0)
        p = cam + rd * t0[:, None]
        w, e, f = width(0 * p[:, 0], p[:, 2], np.where(p[:, 0] < 0, -1.0, 1.0))
        snow = down & (np.abs(p[:, 0]) >= w)
        kind[snow] = 1; t[:] = t0; live = down & ~snow; t[live] += 1e-3
    for it in range(420):
        idx = np.nonzero(live)[0]
        if idx.size == 0: break
        p = cam + rd[idx] * t[idx, None]
        c = clearance(p)
        out = p[:, 1] > 0.02
        hit = (c <= 0.004 + 0.001 * t[idx]) & ~out
        kind[idx[hit]] = 2
        live[idx[hit | out]] = False
        go = ~(hit | out)
        t[idx[go]] += np.maximum(0.004 + 0.0015 * t[idx[go]], 0.5 * c[go])
    kind[live] = 2
    p = cam + rd * t[:, None]
    eps = 0.03; g = np.zeros((n, 3), np.float32)
    for a in range(3):
        d = np.zeros(3, np.float32); d[a] = eps
        g[:, a] = clearance(p + d) - clearance(p - d)
    nrm = norm(g); nrm[kind == 1] = [0, 1, 0]
    return dict(p=p, rd=rd, t=t, kind=kind, n=nrm, W=W, H=H, cam=np.array(cam, np.float32), fwd=fwd, pix=2 * k / H)

def shadow(p, n, sd):
    lit = np.ones(p.shape[0], np.float32); q = p + n * 0.05
    for s in np.linspace(0.3, 40, 70):
        r = q + sd * s
        lit = np.where((r[:, 1] < 0) & (clearance(r) < 0), 0, lit)
    return lit[:, None]

def lights(g, L):
    """Sky light for a normal, the sun (direction, colour x shadow), and the lamp per point (direction, colour)."""
    lamp = None
    if L.get("lamp") is not None:
        v = g["cam"] - g["p"]; d = np.linalg.norm(v, axis=-1, keepdims=True); ld = v / d
        cone = ss(np.cos(np.radians(34)), np.cos(np.radians(6)), dot(-ld, g["fwd"]))
        lamp = (ld, L["lamp"] * cone / (d * d) * sat(1 - (d / 64) ** 4) ** 2)
    return lamp

def sky_light(L, n): return lerp(L["ground"], L["sky"], n[:, 1:2] * 0.5 + 0.5)

def lip_band(g):
    # the snow round the hole and the darker band along the lip (CrevasseLip), the same before and after
    p = g["p"]; side = np.where(p[:, 0] < 0, -1.0, 1.0)
    w, e, f = width(0 * p[:, 0], p[:, 2], side)
    v = np.interp(np.abs(p[:, 0]) - e, [0, 0.25, 0.7, 1.3, 2.2], [0, 0.25, 0.5, 0.75, 1])[:, None]; fade = sat(opening(p[:, 2]) / 0.8)[:, None]
    v = 1 - fade * (1 - v)
    return lerp(lin(col(.09, .16, .27)), lin(col(.93, .95, .98)), ss(0, 1, sat((v - 0.2) / 0.8)))

def geom(g):
    p = g["p"]; side = np.where(p[:, 0] < 0, -1.0, 1.0); d = -p[:, 1]
    w, e, f = width(d, p[:, 2], side)
    t = np.where(d < LIP, 0.06 * sat(d / LIP), lerp(0.07, 1, f))[:, None]
    return d[:, None], sat(t)

def before(g, L, sunlit):
    """The old material: URP Lit with the depth gradient as albedo and emission, smoothness 0.5 (approximately)."""
    d, t = geom(g); n = g["n"]; V = -g["rd"]
    c = np.where(t < .07, col(.95, .97, 1),
        np.where(t < .2, lerp(col(.8, .92, .98), col(.5, .8, .94), (t - .07) / .13),
        np.where(t < .55, lerp(col(.5, .8, .94), col(.22, .52, .8), (t - .2) / .35),
        np.where(t < .85, lerp(col(.22, .52, .8), col(.09, .28, .55), (t - .55) / .3),
                 lerp(col(.09, .28, .55), col(.03, .09, .22), (t - .85) / .15)))))
    alb = lin(c)
    r2 = 0.25 ** 2
    def brdf(ld, rad):
        ndl = sat(dot(n, ld)); h = norm(ld + V); noh = sat(dot(n, h)); loh = sat(dot(ld, h))
        dd = noh * noh * (r2 - 1) + 1.00001
        spec = r2 / (dd * dd * np.maximum(0.1, loh * loh) * (0.5 * 4 + 2))
        return (alb * 0.96 + 0.04 * spec) * rad * ndl
    out = alb * 0.96 * sky_light(L, n) + alb * lin(col(.30, .46, .58))
    fres = (1 - sat(dot(n, V))) ** 4
    out += L["sky"] * L["reflect"] * lerp(0.04, 0.54, fres) / (r2 + 1)
    out += brdf(L["sund"] * np.ones_like(n), L["sun"] * sunlit)
    lamp = lights(g, L)
    if lamp is not None: out += brdf(*lamp)
    return out

def after(g, L, sunlit, parallax=0.07):
    """Resources/IceWall.shader, line for line."""
    d, t = geom(g); p = g["p"]; n = g["n"]; toCam = -g["rd"]; dist = g["t"][:, None]
    tile = (4.5, 23.0); layers = (0.9, 1.0); bump = 1.2; smooth = 0.8
    wall = 1 - ss(0.6, 0.9, np.abs(n[:, 1:2]))
    z, y = p[:, 2] - Z0, p[:, 1]
    foot = dist[:, 0] * g["pix"] / np.maximum(np.abs(dot(n, toCam)[:, 0]), 0.15)      # metres per pixel on the wall
    lod = np.log2(np.maximum(foot / tile[0] * 1024, 1e-3)); lodF = np.log2(np.maximum(foot / tile[1] * 1024, 1e-3))
    broadT = np.sqrt(tex(AM, z / tile[1] + 0.37, y / tile[1] + 0.37, lodF)[:, 1:2])
    layer = (d + (broadT - 0.52) * 0.35 + np.sin(z[:, None] * 0.23) * 0.3 + np.sin(z[:, None] * 0.71 + 1.3) * 0.1) / layers[0]
    lid = np.floor(layer); lf = layer - lid; rnd = fr(np.sin(lid * 12.9898) * 43758.5453)
    u = z / tile[0] + rnd[:, 0] * 7.31; v = y / tile[0] + rnd[:, 0] * 3.17
    if parallax > 0:
        vt = np.stack([toCam[:, 2], toCam[:, 1], np.maximum(np.abs(toCam[:, 0]), 0.35)], -1)
        reach = vt[:, :2] / vt[:, 2:3] * (parallax / tile[0]) * wall * sat(1.5 - dist / 20)
        h1 = tex(AM, u, v, lod)[:, 3:4]
        h2 = tex(AM, u + reach[:, 0] * (h1[:, 0] - 1), v + reach[:, 1] * (h1[:, 0] - 1), lod)[:, 3:4]
        k = ((h1 + h2) * 0.5 - 1)[:, 0]; u = u + reach[:, 0] * k; v = v + reach[:, 1] * k
    ice = tex(AM, u, v, lod); iceT = np.sqrt(ice[:, 1:2])
    snowy = 1 - ss(0.55, 1.5, d + (iceT - 0.52) * 0.9 + (broadT - 0.52) * 0.6)
    blue = lerp(col(.5, .78, .93), col(.21, .6, .87), sat((t - .05) / .15))
    blue = lerp(blue, col(.04, .23, .6), sat((t - .2) / .35))
    blue = lerp(blue, col(.009, .064, .26), sat((t - .55) / .3))
    blue = lerp(blue, col(.002, .009, .04), sat((t - .85) / .15))
    bubbly = ss(.6, .95, rnd) * .22 * layers[1]
    dirt = (1 - ss(0, .05, lf) * (1 - ss(.93, 1, lf))) * .45 * layers[1]
    shade = lerp(1, lerp(.7, 1.25, fr(rnd * 5.77)), layers[1])
    vein = ss(.62, .95, iceT); pale = col(.62, .8, .92)
    alb = blue * (.6 + .8 * iceT) * (.45 + 1.1 * broadT) * shade
    alb = lerp(alb, pale * (.08 + .92 * (1 - t) * (1 - t)), sat(vein * .7 + bubbly))
    alb = alb * (1 - dirt)
    alb = lerp(alb, col(.89, .93, 1.) * (.9 + .2 * iceT), snowy)
    tn = tex(NM, u, v, lod); tn = tn / np.maximum(np.linalg.norm(tn, axis=-1, keepdims=True), 1e-4)
    k = bump * wall * (1 - .6 * snowy); tn = np.concatenate([tn[:, :2] * k, tn[:, 2:3]], -1)
    nb = norm(np.stack([np.abs(tn[:, 2]) * n[:, 0], tn[:, 1] + n[:, 1], tn[:, 0] + n[:, 2]], -1))
    gloss = lerp(smooth, .3, snowy)
    def shine(ld):
        ndh = sat(dot(nb, norm(ld + toCam)))
        return ndh ** lerp(6, 140, gloss) * gloss * (.25 + 1.6 * gloss) * sat(dot(nb, ld) * 4)
    light = sky_light(L, nb) * lerp(1, .3, t) * (.7 + .3 * ice[:, 3:4])
    sd = L["sund"] * np.ones_like(n); sunLit = L["sun"] * sunlit
    light = light + sunLit * sat(dot(nb, sd)); spec = sunLit * shine(sd)
    lamp = lights(g, L)
    if lamp is not None:
        light = light + lamp[1] * sat(dot(nb, lamp[0])); spec = spec + lamp[1] * shine(lamp[0])
    glow = lin(col(.30, .46, .58)) * blue * (.55 + .9 * iceT) * (.75 + .45 * sat(dot(nb, toCam))) * (1 - .5 * snowy)
    return alb * light + spec + glow

def finish(g, L, ice_rgb, sunlit):
    kind = g["kind"][:, None]
    snow = lip_band(g) * (sky_light(L, g["n"]) + L["sun"] * sunlit * sat(g["n"] @ L["sund"])[:, None])
    lamp = lights(g, L)
    if lamp is not None: snow = snow + lip_band(g) * lamp[1] * sat(dot(g["n"], lamp[0]))
    sky = lerp(L["sky"] * 1.6, L["sky"] * 0.9, sat(g["rd"][:, 1:2]))
    c = np.where(kind == 2, ice_rgb, np.where(kind == 1, snow, sky)) * L["exposure"]
    c = c / (1 + 0.35 * np.maximum(c - 0.6, 0))                 # a soft shoulder in place of the game's grade
    c = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055)
    return (sat(c).reshape(g["H"], g["W"], 3) * 255 + 0.5).astype(np.uint8)

def sheet(name, left, right, labels=("BEFORE", "AFTER")):
    h, w = left.shape[:2]
    im = Image.new("RGB", (w * 2 + 8, h), (255, 255, 255)); im.paste(Image.fromarray(left), (0, 0)); im.paste(Image.fromarray(right), (w + 8, 0))
    dr = ImageDraw.Draw(im); font = ImageFont.load_default(size=26)
    for x, s in ((14, labels[0]), (w + 22, labels[1])):
        box = dr.textbbox((x, 12), s, font=font)
        dr.rectangle((box[0] - 8, box[1] - 6, box[2] + 8, box[3] + 6), fill=(0, 0, 0)); dr.text((x, 12), s, fill=(255, 255, 255), font=font)
    q = 90
    while True:
        im.save(f"{OUT}/{name}", quality=q, optimize=True)
        import os
        if os.path.getsize(f"{OUT}/{name}") < 480_000 or q <= 60: break
        q -= 4
    print(name, os.path.getsize(f"{OUT}/{name}") // 1024, "KB q", q)

sund = lambda az, el: np.array([np.cos(np.radians(el)) * np.sin(np.radians(az)), np.sin(np.radians(el)), np.cos(np.radians(el)) * np.cos(np.radians(az))], np.float32)
DAY = dict(sky=col(.42, .55, .80) * 0.9, ground=col(.55, .60, .70) * 0.55, sun=col(1, 1, 1) * 1.15 * 0.8, sund=sund(205, 52), reflect=1.0, exposure=1.0, lamp=None)
DUSK = dict(sky=col(.30, .24, .42) * 0.55, ground=col(.22, .20, .32) * 0.35, sun=col(1, .82, 1.3) * 0.9 * 0.25, sund=sund(20, 8), reflect=0.6, exposure=1.0, lamp=None)
NIGHT = dict(sky=col(.035, .05, .09), ground=col(.02, .03, .05), sun=col(.75, .85, 1) * 0.45 * 0.25, sund=sund(25, 35), reflect=0.5, exposure=1.0, lamp=lin(col(1, .95, .86)) * 48)

W = H = 800
def pair(name, g, L, labels=("BEFORE", "AFTER"), both_after=False):
    s = shadow(g["p"], g["n"], L["sund"])
    a = finish(g, L, after(g, L, s), s)
    b = finish(g, L, after(g, L, s, parallax=0.0), s) if both_after else finish(g, L, before(g, L, s), s)
    sheet(name, b, a, labels)

g1 = trace(np.array([0.4, 2.3, -15.5], np.float32), (0.0, -15.0, 4.0), 84, W, H)
pair("1-from-the-lip-day.jpg", g1, DAY)
g2 = trace(np.array([0.7, -4.6, 5.0], np.float32), (-2.6, -6.4, 10.5), 62, W, H)
pair("2-inside-fall-camera-day.jpg", g2, DAY)
pair("3-inside-dusk.jpg", g2, DUSK)
pair("4-inside-night-headlamp.jpg", g2, NIGHT)
g5 = trace(np.array([-1.35, -4.4, 5.0], np.float32), (-2.5, -4.9, 7.6), 50, W, H)
pair("5-parallax-off-on-close.jpg", g5, DAY, ("PARALLAX OFF", "PARALLAX ON"), both_after=True)
