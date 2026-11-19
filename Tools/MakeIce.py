#!/usr/bin/env python3
# Makes the crevasse ice textures (Assets/Summit/Resources/Ice) from a photo-scanned ice surface:
# "Ice 004" from ambientCG (CC0), https://ambientcg.com/a/Ice004 - download the 1K-JPG zip and unpack it.
#   IceAlbedo.png: the ice's fracture pattern with most of its green taken out (the shader gives the ice its blue,
#                  by depth), and the scan's height map in the alpha channel (for the shader's parallax step)
#   IceNormal.png: the scan's normal map (OpenGL convention, as Unity wants) with its hairline cracks strengthened,
#                  plus the slopes of the height map, so each fractured block tilts its own way and catches the light
# Both stay 1024 x 1024 and tile, as the scan does.
#   python3 Tools/MakeIce.py <folder with Ice004_1K-JPG_*.jpg> Assets/Summit/Resources/Ice
import sys
import numpy as np
from PIL import Image, ImageFilter

src, out = sys.argv[1], sys.argv[2]
def load(kind, mode): return Image.open(f"{src}/Ice004_1K-JPG_{kind}.jpg").convert(mode)

def blur(img, radius):
    # a blur that wraps round the edges, so the result still tiles
    w, h = img.size
    big = Image.new(img.mode, (w * 3, h * 3))
    for i in range(3):
        for j in range(3): big.paste(img, (i * w, j * h))
    return big.filter(ImageFilter.GaussianBlur(radius)).crop((w, h, 2 * w, 2 * h))

colour = np.asarray(load("Color", "RGB"), np.float32) / 255.0
grey = colour.mean(axis=2, keepdims=True)
albedo = grey + (colour - grey) * 0.2                       # nearly neutral: the blue is the shader's
height = np.asarray(blur(load("Displacement", "L"), 1.5), np.float32) / 255.0
rgba = np.concatenate([albedo, height[..., None]], axis=2)
Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(f"{out}/IceAlbedo.png", optimize=True)

n = np.asarray(load("NormalGL", "RGB"), np.float32) / 255.0 * 2.0 - 1.0
soft = np.asarray(blur(load("Displacement", "L"), 3.0), np.float32) / 255.0
dx = (np.roll(soft, -1, axis=1) - np.roll(soft, 1, axis=1)) * 0.5      # height rising to the right
dy = (np.roll(soft, 1, axis=0) - np.roll(soft, -1, axis=0)) * 0.5      # height rising up the image
relief = 14.0
x = n[..., 0] * 2.5 - dx * relief
y = n[..., 1] * 2.5 - dy * relief
z = np.ones_like(x)
l = np.sqrt(x * x + y * y + z * z)
normal = np.stack([x / l, y / l, z / l], axis=2) * 0.5 + 0.5
Image.fromarray((np.clip(normal, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(f"{out}/IceNormal.png", optimize=True)

linear = np.where(albedo <= 0.04045, albedo / 12.92, ((albedo + 0.055) / 1.055) ** 2.4)
print("mean linear albedo %.3f, normal xy spread %.3f %.3f" % (linear.mean(), (x / l).std(), (y / l).std()))
