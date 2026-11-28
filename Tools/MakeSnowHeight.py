# Makes the height map that goes with the snow's groove textures, so the grooves can be given a little depth
# (parallax: the texture lookup shifts with the view angle, see Snow.mat and Shaders/SnowLayer.shader).
#   python3 Tools/MakeSnowHeight.py [--check]
#
# The groove albedo and normal map are generated in Assets/Editor/SummitImport.cs (Grooves and MakePair). This is the
# same height field, ported line for line with Unity's float rounding, so the ridges land on exactly
# the same pixels. To prove it, the script first rebuilds the normal map from its own height field and compares it
# with Assets/Summit/Materials/SnowGroovesNormal.png; it refuses to write anything if the two differ by more than
# rounding. --check stops after that comparison.
#
# Only the relief the parallax should move is kept: the wind ripples (times how strongly they show in each place) and
# the fine pitting. The broad drifts are left out; they are metres wide and a parallax of them would only make the
# whole texture swim. Mid-grey (0.5) is the mean snow surface, crests above and troughs below, so smooth snow does
# not shift at all and grooved snow shifts evenly either way.
#
# Writes Assets/Summit/Materials/SnowGroovesHeight.png (2048, 8-bit grey; the shader reads the green channel).
# If Grooves() in SummitImport.cs ever changes, change it here too and run this again.

import os, sys
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
DIR = os.path.join(ROOT, "Assets/Summit/Materials")
N = 2048
F = np.float32


def tile(u, v, period, seed):
    """SummitImport.Tile: value noise that wraps every `period` cells."""
    x, y = u * F(period), v * F(period)
    x0, y0 = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    fx, fy = x - x0.astype(F), y - y0.astype(F)
    fx = fx * fx * (F(3) - F(2) * fx)
    fy = fy * fy * (F(3) - F(2) * fy)
    # the hash at every lattice point of one period
    # (the sum is carried in 64 bits and only then rounded to 32, as the editor's runtime does it; summed in 32
    # bits the hash comes out different at most points, and the check below fails)
    D = np.float64
    i = np.arange(period)[None, :].astype(D)
    j = np.arange(period)[:, None].astype(D)
    a = (i * D(F(127.1)) + j * D(F(311.7)) + D(F(seed)) * D(F(74.7))).astype(F)
    t = np.sin(a.astype(D)).astype(F) * F(43758.5453)
    table = np.clip(t - np.floor(t), F(0), F(1)).astype(F)          # Mathf.Repeat(t, 1)
    xa, xb, ya, yb = x0 % period, (x0 + 1) % period, y0 % period, (y0 + 1) % period
    lo = table[ya, xa] + (table[ya, xb] - table[ya, xa]) * fx        # Mathf.Lerp
    hi = table[yb, xa] + (table[yb, xb] - table[yb, xa]) * fx
    return (lo + (hi - lo) * fy).astype(F)


def smooth(a, b, x):
    t = np.clip((x - F(a)) / F(b - a), F(0), F(1))
    return t * t * (F(3) - F(2) * t)


def ridge(phase):
    """A wind ripple: a gentle windward face and a short steep lee side."""
    s = phase - np.floor(phase)
    r = np.where(s < F(0.72), s / F(0.72), (F(1) - s) / F(0.28)).astype(F)
    return r * r * (F(3) - F(2) * r)


def grooves():
    """SummitImport.Grooves, plus its parts: (whole height, ripple, how strongly it shows, fine pitting)."""
    px = np.arange(N, dtype=np.int32).astype(F) / F(N)
    u, v = np.meshgrid(px, px)                                       # u along x (columns), v along y (rows)
    w = F(26) * tile(u, v, 2, 1.3) + F(9) * tile(u, v, 5, 6.6) + F(2.2) * tile(u, v, 13, 3.9) + F(0.4) * tile(u, v, 41, 2.2)
    ripple = ridge(F(164) * u + w)                                   # down the fall line (lines of constant u)
    amount = smooth(0.42, 0.62, tile(u, v, 4, 4.8) * F(0.65) + tile(u, v, 9, 8.1) * F(0.35))
    amount = F(0.05) + F(0.95) * amount * (F(0.55) + F(0.45) * tile(u, v, 13, 5.2))
    drift = F(0.6) * tile(u, v, 5, 1.7) + F(0.3) * tile(u, v, 11, 3.3) + F(0.1) * tile(u, v, 23, 8.8)
    pits = tile(u, v, 256, 5.5)
    h = drift * F(0.55) + ripple * amount * F(0.035) + pits * F(0.004)
    return h.astype(F), ripple, amount, pits


def normal_map(h):
    """SummitImport.MakePair's normal map for the large (not detail) texture, as 8-bit RGB, rows in Unity's order."""
    k = F(0.9 * N / 64.0)
    dx = np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)
    dy = np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)
    n = np.stack([-dx * k, -dy * k, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return np.clip(np.rint((n * 0.5 + 0.5) * 255.0), 0, 255).astype(np.uint8)


h, ripple, amount, pits = grooves()

# Unity's textures start at the bottom row, a PNG at the top one
mine = normal_map(h)[::-1]
theirs = np.asarray(Image.open(os.path.join(DIR, "SnowGroovesNormal.png")).convert("RGB"))
diff = np.abs(mine.astype(np.int16) - theirs.astype(np.int16))
worst, off = int(diff.max()), float((diff > 1).mean())
print("SNOWHEIGHT normal map rebuilt: worst channel difference %d/255, %.4f%% of values off by more than 1" % (worst, off * 100))
if off > 0.001:
    sys.exit("SNOWHEIGHT this is not the height field the normal map was made from; nothing written")
if "--check" in sys.argv:
    sys.exit(0)

# the relief alone, about the mean surface: the ripples where they show, and the pitting in its true proportion
PITS = 0.004 / 0.035
relief = (ripple.astype(np.float64) - 0.5) * amount + (pits.astype(np.float64) - 0.5) * PITS
g = 0.5 + relief / (1.0 + PITS)
img = np.clip(np.rint(g * 255.0), 0, 255).astype(np.uint8)[::-1]
out = os.path.join(DIR, "SnowGroovesHeight.png")
Image.fromarray(img, "L").save(out, optimize=True)
print("SNOWHEIGHT wrote %s: %dx%d, grey %d..%d, mean %.1f" % (os.path.relpath(out, ROOT), N, N, img.min(), img.max(), img.mean()))
