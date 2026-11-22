# Builds the lodge's PBR textures (Assets/Summit/Resources/Camp/LodgeTex/) from CC0 material sets (CREDITS.txt):
#   /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/MakeLodgeTex.py -- <downloads> [out]
# <downloads> holds each set in a folder of its own, as downloaded (Poly Haven's 1k JPGs: diff, nor_gl, arm; ambientCG's
# Metal027 1K-JPG zip unpacked into Metal027/x). For each set it writes, at the size the phone needs (1K for the big
# surfaces, 512 for the small ones):
#   <Name>_Albedo.jpg   the colour, graded so the materials' tints set the final colour (greyed where the photo's
#                       colour isn't the lodge's: the concrete, the fabrics; half greyed for the stone)
#   <Name>_Normal.jpg   the normal map (OpenGL, +Y: Unity's)
#   <Name>_Mask.png     packed for URP Lit, at half the size (roughness and occlusion change slowly): R metallic (0
#                       but for the steel: the fabrics' files carry noise there), G ambient occlusion, A smoothness
#                       (1 - roughness); the same texture is the material's metallic-gloss map and its occlusion map
#                       (URP reads its G)
# The grain and the boards run as the files have them (the veneer's along v, the hinoki's along u, the black planks'
# along v); MakeLodge.py's GRAIN_V turns the lodge's UVs to match.
import bpy, numpy as np, os, sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0]
OUT = argv[1] if len(argv) > 1 else "Assets/Summit/Resources/Camp/LodgeTex"
os.makedirs(OUT, exist_ok=True)

# name, folder, colour, normal, (arm) or (roughness, metalness), size, grade, gain (the colour's mean brightness
# wanted), contrast (the colour's spread about its mean kept: calmer for the polished concrete, the stained cladding,
# the rug's check)
SETS = [
    ("Oak", "oak_veneer_01", "oak_veneer_01_diff_1k.jpg", "oak_veneer_01_nor_gl_1k.jpg", ("oak_veneer_01_arm_1k.jpg",), 1024, None, None, 1.0),
    ("Planks", "hinoki_planks", "hinoki_planks_diff_1k.jpg", "hinoki_planks_nor_gl_1k.jpg", ("hinoki_planks_arm_1k.jpg",), 1024, None, None, 1.0),
    ("BlackPlanks", "black_painted_planks", "black_painted_planks_diff_1k.jpg", "black_painted_planks_nor_gl_1k.jpg", ("black_painted_planks_arm_1k.jpg",), 1024, "grey", 0.32, 0.5),
    ("Concrete", "smooth_concrete_floor", "smooth_concrete_floor_diff_1k.jpg", "smooth_concrete_floor_nor_gl_1k.jpg", ("smooth_concrete_floor_arm_1k.jpg",), 1024, "grey", 0.72, 0.4),
    ("Stone", "stacked_stone_wall", "stacked_stone_wall_diff_1k.jpg", "stacked_stone_wall_nor_gl_1k.jpg", ("stacked_stone_wall_arm_1k.jpg",), 512, "half", 0.55, 1.0),
    ("Tweed", "poly_wool_herringbone", "poly_wool_herringbone_diff_1k.jpg", "poly_wool_herringbone_nor_gl_1k.jpg", ("poly_wool_herringbone_arm_1k.jpg",), 512, "grey", 0.7, 1.0),
    ("Check", "wool_boucle", "wool_boucle_diff_1k.jpg", "wool_boucle_nor_gl_1k.jpg", ("wool_boucle_arm_1k.jpg",), 512, "grey", 0.72, 0.45),
    ("Leather", "fabric_leather_02", "fabric_leather_02_diff_1k.jpg", "fabric_leather_02_nor_gl_1k.jpg", ("fabric_leather_02_arm_1k.jpg",), 512, None, None, 1.0),
    ("Steel", "Metal027/x", "Metal027_1K-JPG_Color.jpg", "Metal027_1K-JPG_NormalGL.jpg", ("Metal027_1K-JPG_Roughness.jpg", "Metal027_1K-JPG_Metalness.jpg"), 512, None, None, 1.0),
]


# smoothness lifted (s -> lo + (1 - lo) s) where the lodge's surface is finished more than the scan: the concrete
# polished (the hall's floor takes it as it is, the terrace less: CampLodge's materials scale it), the planks oiled
LIFT = {"Concrete": 0.3, "Planks": 0.15}


def load(path, size):
    """An image's pixels (h, w, 4), raw (no colour conversion), scaled to size x size."""
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = "Non-Color"
    if tuple(img.size) != (size, size):
        img.scale(size, size)
    a = np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)
    bpy.data.images.remove(img)
    return a


def save(a, path, fmt, alpha=False, quality=92):
    h, w = a.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=alpha)
    img.colorspace_settings.name = "Non-Color"
    img.pixels[:] = np.clip(a, 0.0, 1.0).ravel()
    img.filepath_raw = path
    img.file_format = fmt
    img.save(filepath=path, quality=quality)
    bpy.data.images.remove(img)
    print("LODGETEX wrote", path, "%d KB" % (os.path.getsize(path) // 1024))


for name, folder, col, nor, rm, size, grade, gain, contrast in SETS:
    d = os.path.join(SRC, folder)
    c = load(os.path.join(d, col), size)
    rgb = c[..., :3]
    lum = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    if grade == "grey":
        rgb = np.repeat(lum[..., None], 3, axis=2)
    elif grade == "half":
        rgb = 0.5 * rgb + 0.5 * lum[..., None]
    if contrast != 1.0:
        mean = rgb.mean(axis=(0, 1), keepdims=True)
        rgb = mean + (rgb - mean) * contrast
    if gain is not None:
        # (in the files' sRGB values: the mean brought to `gain`)
        rgb = rgb * (gain / max(1e-4, float(rgb.mean())))
    c[..., :3] = rgb
    c[..., 3] = 1.0
    save(c, os.path.join(OUT, name + "_Albedo.jpg"), "JPEG")
    n = load(os.path.join(d, nor), size)
    n[..., 3] = 1.0
    save(n, os.path.join(OUT, name + "_Normal.jpg"), "JPEG", quality=95)
    ms = size // 2
    if len(rm) == 1:
        arm = load(os.path.join(d, rm[0]), ms)
        ao, rough, metal = arm[..., 0], arm[..., 1], np.zeros((ms, ms), dtype=np.float32)
    else:
        # (the steel: a black powder coat over metal, half metallic, so it reads black, not as a dark mirror)
        rough = load(os.path.join(d, rm[0]), ms)[..., 0]
        metal = load(os.path.join(d, rm[1]), ms)[..., 0] * 0.6
        ao = np.ones_like(rough)
    m = np.zeros((ms, ms, 4), dtype=np.float32)
    m[..., 0] = metal
    m[..., 1] = ao
    lo = LIFT.get(name, 0.0)
    m[..., 3] = lo + (1.0 - lo) * (1.0 - rough)
    save(m, os.path.join(OUT, name + "_Mask.png"), "PNG", alpha=True)
    print("LODGETEX %s: albedo mean %.2f, metallic %.2f, ao %.2f, smoothness %.2f" % (name, float(rgb.mean()), float(metal.mean()), float(ao.mean()), float(m[..., 3].mean())))
