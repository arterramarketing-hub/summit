# Builds Summit's slope rocks from photo-scanned boulders: downloads the scans, cuts each down to a phone
# budget, bakes their colour and relief into one shared atlas, and exports them for Unity.
#
#   pip install bpy numpy pillow                                   (Blender as a Python module; once)
#   python3 Tools/MakeScanRocks.py --project .                     (downloads, bakes, exports)
#   python3 Tools/MakeScanRocks.py --project . --render DIR        (and pictures of the result, to check by eye)
#   blender --background --factory-startup --python Tools/MakeScanRocks.py -- --project .     (the same, in Blender)
# Options: --cache DIR (where the downloads are kept, default the system's temp folder), --size N (atlas side,
# default 1024), --tris N (triangles a rock, default 900).
#
# Writes (with --project):
#   Assets/Summit/Resources/ScanRocks/ScanRock_1..6.fbx     the rocks, one mesh and one material slot each
#   Assets/Summit/Resources/ScanRocks/ScanRocksAlbedo.png   their colour, all six in one atlas
#   Assets/Summit/Resources/ScanRocks/ScanRocksNormal.png   their relief (tangent space, green up as Unity reads it)
# The .meta files beside them are written by hand and kept in the repository (import scale, normal-map type):
# this script never touches them.
#
# The scans are CC0, from Poly Haven (https://polyhaven.com): see ROCKS below and Resources/CREDITS.txt.
#
# What happens to each scan (60,000 to 110,000 triangles, 2K textures as downloaded):
#   - stood the way ROCKS says, set on the ground (z = 0 at its lowest point, centred over the origin) and scaled
#     to 1.3 m tall - the height Forest.cs gives a rock of size 1, so a rock in the file is a rock on the slope
#   - if it is much wider than it is tall, stretched up a little (never more than a fifth): the game's rock
#     collider is a ball sized from the rock's height alone, so a wide flat slab would stick out of it
#   - a copy is collapsed to about 900 triangles and unwrapped afresh; all the copies are packed into one atlas
#     at the same texel size
#   - the scan's colour, its fine relief (as a normal map: everything the 900 triangles lost) and the shade in
#     its cracks are baked from the full scan onto the copy
#   - the colour is graded to the mountain's grey granite: most of its own hue taken out, each rock brought to
#     the same mid tone, the crack shade multiplied in. The scans are of brown and mossy rock; the cliffs here
#     (Tools/MakeRock.swift) are cold grey
# Snow is not baked in: Resources/ScanRockLit.shader lays it on whatever faces up, so it stays on top however a
# rock is turned.
#
# Axes are Blender's: z up. The FBX export turns that into Unity's y up.

import bpy, bmesh, math, sys, os, json, hashlib, tempfile, urllib.request
import numpy as np
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else default
PROJECT = arg("--project")
RENDER = arg("--render")
CACHE = arg("--cache", os.path.join(tempfile.gettempdir(), "summit-scan-rocks"))
SIZE = int(arg("--size", "1024"))        # the atlas as shipped
TRIS = int(arg("--tris", "900"))
BAKE = SIZE * 2                          # baked at twice the size and averaged down
HEIGHT = 1.3                             # metres: Forest.cs's rock of size 1
WIDEST = 1.6                             # the widest a rock may be for its height before it is stretched up
STRETCH = 1.2                            # ... and the most it is stretched
TONE = (0.385, 0.39, 0.40)               # the mid tone every rock is brought to (sRGB): a little lighter and as
                                         # cold as the cliffs' granite (Resources/RockTex/RockAlbedo.png, 0.32)
HUE_KEPT = 0.3                           # how much of the scan's own colour survives the grading

# Poly Haven asset, the object in it (None: its only one), how it is turned before it is set down (degrees, x y z)
ROCKS = [
    ("namaqualand_boulder_04", None, (0, 0, 0)),                        # a big square block
    ("boulder_01", None, (0, 0, 0)),                                    # craggy, deeply cracked
    ("rock_moss_set_02", "rock_moss_set_02_rock11", (0, 0, 0)),         # a split block with a sloping top
    ("rock_moss_set_02", "rock_moss_set_02_rock13", (0, 0, 0)),         # a long wedge
    ("namaqualand_boulder_06", None, (0, 0, 0)),                        # rounded, weathered
    ("namaqualand_boulder_05", None, (0, 55, 0)),                       # a flat stone, tipped up on its end
]

# ---- the scans

def fetch(asset):
    """Downloads an asset's glTF and what it includes (2K textures) into the cache, checked against Poly Haven's sums."""
    folder = os.path.join(CACHE, asset)
    def get(url):
        return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "summit-rock-build"})).read()
    listing = os.path.join(folder, "files.json")
    os.makedirs(folder, exist_ok=True)
    if not os.path.exists(listing):
        with open(listing, "wb") as f: f.write(get("https://api.polyhaven.com/files/" + asset))
    entry = json.load(open(listing))["gltf"]["2k"]["gltf"]
    for rel, e in [(asset + ".gltf", entry)] + list(entry["include"].items()):
        path = os.path.join(folder, rel)
        if not (os.path.exists(path) and hashlib.md5(open(path, "rb").read()).hexdigest() == e["md5"]):
            os.makedirs(os.path.dirname(path), exist_ok=True)
            data = get(e["url"])
            if hashlib.md5(data).hexdigest() != e["md5"]: raise SystemExit("bad download: " + rel)
            with open(path, "wb") as f: f.write(data)
    return os.path.join(folder, asset + ".gltf")

def only(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def load(asset, name, turn):
    """The full scan, turned, set on the ground and scaled to HEIGHT. Returns the object."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=fetch(asset), merge_vertices=True)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH' and (name is None or o.name == name)]
    if len(meshes) != 1: raise SystemExit("%s: expected one mesh, found %s" % (asset, [o.name for o in new]))
    hi = meshes[0]
    for o in new:
        if o is not hi: bpy.data.objects.remove(o, do_unlink=True)
    me = hi.data
    me.transform(Euler([math.radians(a) for a in turn]).to_matrix().to_4x4() @ hi.matrix_world)
    hi.matrix_world = Matrix.Identity(4)
    co = np.array([v.co[:] for v in me.vertices])
    lo, top = co.min(0), co.max(0)
    size = top - lo
    k = HEIGHT / size[2]
    up = min(STRETCH, max(1.0, max(size[0], size[1]) / size[2] / WIDEST))
    # (stretched up: taller for its footprint, which is to say narrower at the same height)
    me.transform(Matrix.Diagonal((k / up, k / up, k, 1.0)) @ Matrix.Translation((-(lo[0] + top[0]) / 2, -(lo[1] + top[1]) / 2, -lo[2])))
    me.update()
    hi["stretch"] = up
    return hi

def cut_down(hi, name):
    """A copy of the scan with about TRIS triangles, smooth-shaded, without UVs yet."""
    low = hi.copy(); low.data = hi.data.copy(); low.name = low.data.name = name
    bpy.context.scene.collection.objects.link(low)
    me = low.data
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
    me.materials.clear()
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    n = len(bm.faces)
    bm.to_mesh(me); bm.free()
    only(low)
    mod = low.modifiers.new("cut", 'DECIMATE')
    mod.ratio = TRIS / n
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    for f in bm.faces: f.smooth = True
    bm.to_mesh(me); bm.free()
    # collapsing can lift the lowest point a little: back on the ground, exactly HEIGHT tall
    z = [v.co.z for v in me.vertices]
    me.transform(Matrix.Diagonal((1, 1, HEIGHT / (max(z) - min(z)), 1)) @ Matrix.Translation((0, 0, -min(z))))
    return low

def unwrap(lows):
    """Fresh UVs for every rock, all packed into the one atlas at the same texel size."""
    for low in lows:
        only(low)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(72), island_margin=0.0)
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    for low in lows: low.select_set(True)
    bpy.context.view_layer.objects.active = lows[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / SIZE)
    bpy.ops.object.mode_set(mode='OBJECT')

# ---- baking

def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)

def bake(hi, low, kind):
    """One pass (DIFFUSE, NORMAL or AO) from the full scan onto its cut-down copy; returns the picture (rgba, floats)."""
    img = bpy.data.images.new("bake", BAKE, BAKE, alpha=True, float_buffer=True)
    img.generated_color = (0, 0, 0, 0)
    img.colorspace_settings.name = 'Non-Color' if kind == 'NORMAL' else 'sRGB'
    mat = bpy.data.materials.new("bake"); mat.use_nodes = True
    node = mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image = img
    mat.node_tree.nodes.active = node
    low.data.materials.clear(); low.data.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT')
    hi.select_set(True); low.select_set(True)
    bpy.context.view_layer.objects.active = low
    b = bpy.context.scene.render.bake
    b.use_selected_to_active = True; b.cage_extrusion = 0.06; b.max_ray_distance = 0.2
    b.margin = 16; b.use_clear = True; b.use_pass_direct = False; b.use_pass_indirect = False; b.use_pass_color = True
    bpy.context.scene.cycles.samples = 48 if kind == 'AO' else 2
    bpy.ops.object.bake(type=kind)
    out = pixels(img).copy()
    bpy.data.images.remove(img); low.data.materials.clear(); bpy.data.materials.remove(mat)
    return out

def to_srgb(x): return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(np.clip(x, 0.0031308, None), 1 / 2.4) - 0.055)

def halve(a): return a.reshape(a.shape[0] // 2, 2, a.shape[1] // 2, 2, a.shape[2]).mean((1, 3))

def grade(colour, shade, mask):
    """The scan's colour as the mountain's granite: mostly grey, at TONE on average, darker in the cracks. sRGB out."""
    rgb = to_srgb(np.clip(colour[..., :3], 0, 1))
    lum = (rgb * (0.299, 0.587, 0.114)).sum(-1, keepdims=True)
    rgb = lum + (rgb - lum) * HUE_KEPT
    mean = rgb[mask].mean(0)
    rgb = rgb * (np.array(TONE) / mean)
    ao = to_srgb(np.clip(shade[..., :1], 0, 1))
    ao = np.clip(ao / max(1e-3, np.percentile(ao[mask], 90)), 0, 1)      # open faces untouched, cracks darker
    return np.clip(rgb * (0.45 + 0.55 * ao), 0, 1)

def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    world = bpy.data.worlds.new("w"); scene.world = world      # (the crack shade is baked against a plain white sky)
    world.use_nodes = True
    his, lows = [], []
    for i, (asset, name, turn) in enumerate(ROCKS):
        hi = load(asset, name, turn)
        hi.location = (i * 10.0, 0, 0)      # apart, so no scan shades or bakes onto another's copy
        low = cut_down(hi, "ScanRock_%d" % (i + 1))
        low.location = hi.location
        his.append(hi); lows.append(low)
    unwrap(lows)
    albedo = np.zeros((BAKE, BAKE, 3), np.float32); albedo[:] = TONE
    normal = np.zeros((BAKE, BAKE, 3), np.float32); normal[:] = (0.5, 0.5, 1.0)
    for hi, low in zip(his, lows):
        colour, relief, shade = bake(hi, low, 'DIFFUSE'), bake(hi, low, 'NORMAL'), bake(hi, low, 'AO')
        mask = colour[..., 3] > 0.5
        albedo[mask] = grade(colour, shade, mask)[mask]
        normal[mask] = relief[..., :3][mask]
        print("baked %s: %d%% of the atlas" % (low.name, round(100 * mask.mean())))
    albedo, normal = halve(albedo), halve(normal)
    n = normal * 2 - 1
    n /= np.maximum(1e-6, np.linalg.norm(n, axis=-1, keepdims=True))
    normal = n * 0.5 + 0.5
    return his, lows, albedo, normal

# ---- writing

def save_png(a, path):
    from PIL import Image
    Image.fromarray((np.clip(a[::-1], 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB').save(path, optimize=True)

def report(lows, his):
    for low, hi in zip(lows, his):
        me = low.data
        co = np.array([v.co[:] for v in me.vertices])
        lo, top = co.min(0), co.max(0)
        uv = np.array([l.uv[:] for l in me.uv_layers[0].data])
        bm = bmesh.new(); bm.from_mesh(me)
        open_edges = sum(1 for e in bm.edges if not e.is_manifold); bm.free()
        print("%s: %d triangles, %d vertices, %.2f x %.2f x %.2f m (x, y, up), base z %.3f, centre %.2f %.2f, "
              "stretched up x%.2f, open edges %d, uv %.3f..%.3f" %
              (low.name, len(me.polygons), len(me.vertices), top[0] - lo[0], top[1] - lo[1], top[2] - lo[2], lo[2],
               (lo[0] + top[0]) / 2, (lo[1] + top[1]) / 2, hi["stretch"], open_edges, uv.min(), uv.max()))

def export(lows, albedo, normal):
    folder = os.path.join(PROJECT, "Assets/Summit/Resources/ScanRocks")
    os.makedirs(folder, exist_ok=True)
    save_png(albedo, os.path.join(folder, "ScanRocksAlbedo.png"))
    save_png(normal, os.path.join(folder, "ScanRocksNormal.png"))
    mat = bpy.data.materials.new("ScanRock")       # (a name only: Forest.cs gives the rocks their real material)
    for low in lows:
        low.data.materials.clear(); low.data.materials.append(mat)
        low.data.uv_layers[0].name = "UVMap"
        was = low.location.copy(); low.location = (0, 0, 0)
        only(low)
        bpy.ops.export_scene.fbx(filepath=os.path.join(folder, low.name + ".fbx"), use_selection=True, object_types={'MESH'},
                                 bake_anim=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                                 axis_forward='-Z', axis_up='Y', path_mode='AUTO', embed_textures=False,
                                 mesh_smooth_type='OFF', use_tspace=False, use_mesh_modifiers=False)
        low.location = was

def render(lows, his, albedo, normal):
    """Pictures of the cut-down rocks wearing the atlas, with snow laid on as the game's shader does, to check by eye."""
    os.makedirs(RENDER, exist_ok=True)
    save_png(albedo, os.path.join(RENDER, "atlas_albedo.png")); save_png(normal, os.path.join(RENDER, "atlas_normal.png"))
    scene = bpy.context.scene
    for hi in his: hi.hide_render = True
    def picture(path, space):
        img = bpy.data.images.load(path); img.colorspace_settings.name = space; return img
    for snowy in (False, True):
        mat = bpy.data.materials.new("look"); mat.use_nodes = True
        nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
        col = nt.nodes.new('ShaderNodeTexImage'); col.image = picture(os.path.join(RENDER, "atlas_albedo.png"), 'sRGB')
        nor = nt.nodes.new('ShaderNodeTexImage'); nor.image = picture(os.path.join(RENDER, "atlas_normal.png"), 'Non-Color')
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(nor.outputs[0], nm.inputs["Color"])
        nt.links.new(nm.outputs[0], bsdf.inputs["Normal"]); bsdf.inputs["Roughness"].default_value = 0.85
        if snowy:
            sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(nm.outputs[0], sep.inputs[0])
            ramp = nt.nodes.new('ShaderNodeMapRange'); ramp.interpolation_type = 'SMOOTHSTEP'
            ramp.inputs["From Min"].default_value = 0.48; ramp.inputs["From Max"].default_value = 0.66
            nt.links.new(sep.outputs["Z"], ramp.inputs["Value"])
            mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
            nt.links.new(ramp.outputs[0], mix.inputs[0]); nt.links.new(col.outputs[0], mix.inputs[6])
            mix.inputs[7].default_value = (0.86, 0.9, 0.96, 1)
            nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        else: nt.links.new(col.outputs[0], bsdf.inputs["Base Color"])
        for i, low in enumerate(lows):
            low.data.materials.clear(); low.data.materials.append(mat)
            low.location = ((i % 3) * 2.6, -(i // 3) * 2.6, 0)
        bpy.ops.mesh.primitive_plane_add(size=40, location=(2.6, -1.3, 0.35 if snowy else 0.0))
        ground = bpy.context.object
        gm = bpy.data.materials.new("ground"); gm.use_nodes = True
        gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.84, 0.9, 1) if snowy else (0.3, 0.3, 0.3, 1)
        ground.data.materials.append(gm)
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
        sun.data.energy = 3.5; sun.rotation_euler = (math.radians(55), 0, math.radians(35))
        scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.7, 1, 1)
        for view, (at, to) in {"side": ((2.6, -11.5, 2.6), (2.6, -1.3, 0.6)), "top": ((2.6, -6.5, 9.0), (2.6, -1.3, 0.5))}.items():
            cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
            cam.location = at; cam.data.lens = 36
            cam.rotation_euler = (Vector(to) - Vector(at)).to_track_quat('-Z', 'Y').to_euler()
            scene.camera = cam
            scene.cycles.samples = 24; scene.render.resolution_x = 1400; scene.render.resolution_y = 800
            scene.render.filepath = os.path.join(RENDER, "rocks_%s_%s.png" % ("snow" if snowy else "bare", view))
            bpy.ops.render.render(write_still=True)
            bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.objects.remove(ground, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)
    for i, low in enumerate(lows): low.location = (0, 0, 0)

if __name__ == "__main__":
    if not PROJECT and not RENDER: raise SystemExit("usage: MakeScanRocks.py --project . [--render DIR]  (see the header)")
    his, lows, albedo, normal = build()
    report(lows, his)
    if PROJECT: export(lows, albedo, normal)
    if RENDER: render(lows, his, albedo, normal)
