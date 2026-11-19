# Builds Summit's rock textures from a scanned cliff face, for Resources/RockLit.shader:
#   RockAlbedo.png  the scan's colour detail (its cracks, chips and grain), regraded to the game's dark blue-grey
#                   granite: the broad colour patches and the average colour and contrast are those of the granite
#                   Tools/MakeRock.swift paints (kept as Tools/art/rock-procedural.png), so every tint, mask and
#                   snow line the shader derives from this texture stays where it was
#   RockRelief.png  the scan's surface: its normal map's x and y in red and green (OpenGL convention, green up),
#                   its height in blue (0 the deepest crack, 1 the proudest block). Not colour: imported linear
# The scan is "Rock Face 03" from Poly Haven (CC0), https://polyhaven.com/a/rock_face_03 - download its 1K PNG
# maps (diff, nor_gl, disp) into one folder first:
#   https://dl.polyhaven.org/file/ph-assets/Textures/png/1k/rock_face_03/rock_face_03_diff_1k.png
#   https://dl.polyhaven.org/file/ph-assets/Textures/png/1k/rock_face_03/rock_face_03_nor_gl_1k.png
#   https://dl.polyhaven.org/file/ph-assets/Textures/png/1k/rock_face_03/rock_face_03_disp_1k.png
# Run: python3 Tools/MakeRockScan.py <folder with the three maps> <outdir>
#   (needs Pillow and numpy; copy the two PNGs it writes to Assets/Summit/Resources/RockTex/, and if the printed
#   mean height has moved, set RELIEF_MEAN in RockLit.shader to it)
import os
import sys

import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "."
here = os.path.dirname(os.path.abspath(__file__))
N = 1024


def load(name):
    return np.asarray(Image.open(os.path.join(src, "rock_face_03_%s_1k.png" % name))).astype(np.float64)


def blur(a, sigma):
    """Gaussian blur that wraps at the edges (so a tileable image stays tileable), by way of the FFT."""
    f = np.fft.fftfreq(a.shape[0])
    g = np.exp(-2.0 * (np.pi * sigma) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    if a.ndim == 3:
        g = g[:, :, None]
    return np.real(np.fft.ifft2(np.fft.fft2(a, axes=(0, 1)) * g, axes=(0, 1)))


# ---- colour: the old granite's broad patches, carrying the scan's detail
old = Image.open(os.path.join(here, "art", "rock-procedural.png")).convert("RGB")
old_small = np.asarray(old).astype(np.float64) / 255.0
old_big = np.asarray(old.resize((N, N), Image.BICUBIC)).astype(np.float64) / 255.0
patches = blur(old_big, 40.0)                      # (in sRGB tones throughout: the shader's masks work in those)
scan = load("diff") / 255.0
lum = scan @ np.array([0.299, 0.587, 0.114])
detail = lum - blur(lum, 40.0)                     # the scan's cracks and grain, without its own broad shading
chroma = (scan - lum[:, :, None]) - blur(scan - lum[:, :, None], 40.0)   # and a little of its local colour
# the old granite's contrast above the patches (its cracks, streaks and grain) sets how strong the detail is
old_detail = (old_small - blur(old_small, 20.0)).std()
albedo = patches + detail[:, :, None] * (old_detail / detail.std()) + chroma * 0.35
albedo += old_small.mean((0, 1)) - albedo.mean((0, 1))
albedo = np.clip(albedo, 0.0, 1.0)
Image.fromarray((albedo * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(out, "RockAlbedo.png"), optimize=True)

# ---- relief: normal x, y and height
nor = load("nor_gl")[:, :, :2] / 255.0
disp = load("disp")
lo, hi = np.percentile(disp, [0.5, 99.5])          # the odd stray pixel must not squash everything else
height = np.clip((disp - lo) / (hi - lo), 0.0, 1.0)
relief = np.dstack([nor, height])
Image.fromarray((relief * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(out, "RockRelief.png"), optimize=True)

lin = lambda c: np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
print("albedo mean, sRGB tones: old %s  new %s" % (old_small.mean((0, 1)).round(4), albedo.mean((0, 1)).round(4)))
print("albedo mean, linear:     old %s  new %s" % (lin(old_small).mean((0, 1)).round(4), lin(albedo).mean((0, 1)).round(4)))
print("albedo spread (std):     old %s  new %s" % (old_small.std((0, 1)).round(4), albedo.std((0, 1)).round(4)))
print("mean height (RELIEF_MEAN in RockLit.shader): %.3f" % height.mean())
print("normal x, y means: %s" % (nor.mean((0, 1)) * 2.0 - 1.0).round(4))
