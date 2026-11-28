# Preview pictures of the hamlets' buildings, rendered in Blender's Cycles: run by Tools/MakeCabins.py when it
# is given --render <folder> (it uses that script's models, still in the scene, and its names).
#   python3 Tools/MakeCabins.py -- Assets/Summit/Resources/Cabins --render Tools/art
# One picture of each building seated on a slope as steep as the mountain's (0.72), and a hamlet laid out the
# way Hamlets.cs lays one out, by day and at dusk. The slope here is a stand-in (a plane with a bench in it),
# not the game's mountain; the materials copy the game's (the camp's textures, the same tints).

import random

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 48
try:
    scene.cycles.use_denoising = True
except Exception:
    pass
scene.render.resolution_x, scene.render.resolution_y = 1120, 630
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "JPEG"
scene.render.image_settings.quality = 86
os.makedirs(RENDER, exist_ok=True)


def image_mat(name, tex, tint, rough=0.8, glow=None, closest=False, metallic=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    if tex and os.path.exists(tex):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(tex, check_existing=True)
        if closest:
            t.interpolation = "Closest"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type, mix.blend_type = "RGBA", "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        nt.links.new(t.outputs[0], mix.inputs[6])
        mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*tint, 1)
    if glow and os.path.exists(glow):
        g = nt.nodes.new("ShaderNodeTexImage")
        g.image = bpy.data.images.load(glow, check_existing=True)
        if closest:
            g.interpolation = "Closest"
        nt.links.new(g.outputs[0], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 0.0
    return m


image_mat("Larch", os.path.join(CAMP, "Oak.png"), LARCH_TINT)
image_mat("DarkWood", os.path.join(CAMP, "Wood.png"), DARK_TINT)
image_mat("Stone", os.path.join(CAMP, "Stone.png"), (0.95, 0.95, 0.95), 0.9)
image_mat("Metal", None, METAL, 0.45, metallic=0.6)
image_mat("Snow", None, SNOW, 0.65)
image_mat("Paint", None, PAINT, 0.4)
image_mat("Glass", os.path.join(OUT, "Windows.png"), (1, 1, 1), 0.12, os.path.join(OUT, "WindowsGlow.png"))
image_mat("Far", os.path.join(OUT, "Far.png"), (1, 1, 1), 0.8, os.path.join(OUT, "FarGlow.png"), closest=True)
ground_mat = image_mat("Ground", None, (0.9, 0.93, 0.98), 0.7)
tree_mat = image_mat("Fir", None, (0.06, 0.13, 0.08), 0.9)
fence_mat = bpy.data.materials["DarkWood"]

for o in bpy.data.objects:   # the helper boxes are not drawn
    if "__" in o.name:
        o.hide_render = True


def glow(k):
    for n in ("Glass", "Far"):
        bpy.data.materials[n].node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = k


def show(names, far_too=False):
    """Draw only these models; of each, the near meshes or (far_too) the far one."""
    for name, root in roots.items():
        for o in bpy.data.objects:
            if o.parent == root and "__" not in o.name:
                o.hide_render = name not in names or (o.name.endswith("_Far") != far_too)


def put(root, x, y, z, yaw=0.0):
    """Set a model at a game position, turned yaw degrees (Unity's sense: +z toward +x)."""
    root.location = (x, z, y)
    root.rotation_euler = (0, 0, -math.radians(yaw))


extra = []


def mesh_obj(name, verts, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(v[0], v[2], v[1]) for v in verts], [], faces)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    extra.append(ob)
    return ob


def clear_extra():
    for o in extra:
        bpy.data.objects.remove(o)
    extra.clear()


def ground(h, x0, x1, z0, z1, step=1.5):
    nx, nz = int((x1 - x0) / step) + 1, int((z1 - z0) / step) + 1
    verts = [(x0 + i * step, h(x0 + i * step, z0 + j * step), z0 + j * step) for j in range(nz) for i in range(nx)]
    faces = [(j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i) for j in range(nz - 1) for i in range(nx - 1)]
    return mesh_obj("Ground", verts, faces, ground_mat)


def seat(name, h, x, z, yaw):
    """The game's rule (Hamlets.Seat): the floor goes level on the highest snow under the footprint."""
    i = models[name].info
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    top = -1e9
    for a in range(5):
        for b in range(5):
            lx = -i["hx"] + 2 * i["hx"] * a / 4
            lz = i["zb"] + (i["zf"] - i["zb"]) * b / 4
            top = max(top, h(x + lx * c + lz * s, z - lx * s + lz * c))
    put(roots[name], x, top - 0.15, z, yaw)
    return top - 0.15


def sun(el, az, strength, colour, sky, sky_strength):
    for o in [o for o in bpy.data.objects if o.type == "LIGHT"]:
        bpy.data.objects.remove(o)
    l = bpy.data.lights.new("Sun", "SUN")
    l.energy, l.color, l.angle = strength, colour, math.radians(2)
    o = bpy.data.objects.new("Sun", l)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(90 - el), 0, math.radians(az))
    w = bpy.data.worlds.get("W") or bpy.data.worlds.new("W")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value, bg.inputs[1].default_value = (*sky, 1), sky_strength
    scene.world = w


def shoot(path, eye, look, lens=35):
    cam = bpy.data.objects.get("Cam")
    if cam is None:
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
        coll.objects.link(cam)
    cam.data.lens = lens
    cam.data.clip_end = 2000
    e, t = Vector((eye[0], eye[2], eye[1])), Vector((look[0], look[2], look[1]))
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", path)


def day():
    sun(38, 215, 4.0, (1.0, 0.96, 0.9), (0.55, 0.7, 0.95), 1.0)
    glow(0.0)


def dusk():
    sun(4, 250, 0.25, (1.0, 0.55, 0.4), (0.05, 0.08, 0.2), 0.5)
    glow(7.0)


# ---- each building on the mountain's own pitch, seen from below on the piste's side
SLOPE = 0.72
for name in models:
    if name == "Lamp":
        continue
    clear_extra()
    h = lambda x, z: -SLOPE * z + 0.05 * x
    ground(h, -30, 30, -30, 34)
    show([name])
    for r in roots.values():
        put(r, 0, 0, 0)
    y = seat(name, h, 0, 0, 0)
    day()
    M = models[name]
    r = max(M.hi.x - M.lo.x, M.hi.y + 4, 9)
    shoot(os.path.join(RENDER, "cabins-%s.jpg" % name.lower()), (-r * 1.25, y + M.info["top"] * 0.45 + 1.5 - SLOPE * r * 0.9, r * 1.55), (0, y + M.info["top"] * 0.3 - 1.5, 0.5), 32)

# ---- every building beside its far version, lit as at dusk
clear_extra()
for r in roots.values():
    put(r, 0, 0, 0)
ground(lambda x, z: -9.2, -40, 60, -30, 30, 10)
order = ["Chalet", "Lodge", "CabinA", "CabinB", "CabinC", "CabinD", "CarDeck"]
for k, name in enumerate(order):
    put(roots[name], -30 + k * 11.5, 0, 0)
show(order)
dusk()
shoot(os.path.join(RENDER, "cabins-family-dusk.jpg"), (4, 6, 46), (4, -1, 0), 40)
show(order, far_too=True)
day()
shoot(os.path.join(RENDER, "cabins-family-far.jpg"), (4, 6, 46), (4, -1, 0), 40)

# ---- a hamlet, laid out like Hamlets.cs lays one out on the right of the piste (x = 0 is its centre line)
clear_extra()


def hill(x, z):
    a = abs(x)
    y = -0.72 * z + 0.0035 * a * a / (1 + a * a / 6400) + 0.6 * math.sin(x * 0.11 + 1.0) * math.sin(z * 0.07)
    # a bench like the one beside a jump cliff's in-run, only gentler: the pitch eases off above z = 0
    t = min(1.0, max(0.0, (z + 26) / 26))
    return y + 4.5 * t * t * (1 if z < 0 else max(0.0, 1 - z / 12.0))


ground(hill, -34, 80, -70, 120, 1.5)
plan = [("Chalet", 35, -8, -20), ("CarDeck", 31, -24, -80), ("Lodge", 32, 16, -18), ("CabinA", 29.5, 34, -24), ("CabinB", 42, 42, -12),
        ("CabinC", 30, 50, -20), ("CabinD", 39.5, 62, -16), ("Lamp", 23.5, 10, -90)]
for name, x, z, yaw in plan:
    if name == "Lamp":
        put(roots[name], x, hill(x, z), z, yaw)
    else:
        seat(name, hill, x, z, yaw)
show([p[0] for p in plan])


def bar(a, b_, t, mat, name="Fence"):
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    side = Vector((0, 1, 0)).cross(d).normalized() * t / 2 if abs(d.normalized().y) < 0.99 else Vector((t / 2, 0, 0))
    up = d.cross(side).normalized() * t / 2
    vs = [p + sx * side + sy * up for p in (a, b_) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    mesh_obj(name, vs, [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], mat)


# the post-and-rail fence along the piste's edge, a gap in it, snow on its posts
last = None
for z in range(-30, 76, 3):
    p = Vector((23.0, hill(23.0, z), z))
    gap = 20 <= z < 26
    if not gap:
        bar(p - Vector((0, 0.5, 0)), p + Vector((0, 1.15, 0)), 0.16, fence_mat)
        bar(p + Vector((0, 1.15, 0)), p + Vector((0, 1.27, 0)), 0.2, bpy.data.materials["Snow"])
        if last is not None:
            for hh in (0.55, 1.0):
                bar(last + Vector((0, hh, 0)), p + Vector((0, hh, 0)), 0.1, fence_mat)
    last = None if gap else p

# firs round the clearing: plain cones with snow on them (the game's own trees are another session's work)
random.seed(4)


def taken(x, z):
    if 17 < x < 27 and -44 < z < 80:
        return True
    for name, bx, bz, yaw in plan:
        i = models[name].info
        if abs(x - bx) < i["hx"] + 4.5 and i["zb"] - 4.5 < z - bz < i["zf"] + 4.5:
            return True
    return False


for gx in range(-5, 13):
    for gz in range(-10, 18):
        x, z = gx * 6.5 + random.uniform(-2.4, 2.4), gz * 6.5 + random.uniform(-2.4, 2.4)
        if abs(x) < 19 or taken(x, z) or random.random() > (0.5 if abs(x) > 30 else 0.25):
            continue
        hgt, y = random.uniform(9, 17), hill(x, z)
        for k, (y0, r, hh, m) in enumerate(((0.0, 0.25, hgt * 0.3, tree_mat), (hgt * 0.15, hgt * 0.19, hgt * 0.5, tree_mat), (hgt * 0.4, hgt * 0.14, hgt * 0.42, bpy.data.materials["Snow"]), (hgt * 0.62, hgt * 0.09, hgt * 0.38, tree_mat))):
            n = 8
            vs = [(x + r * math.cos(i * 2 * math.pi / n), y + y0, z + r * math.sin(i * 2 * math.pi / n)) for i in range(n)] + [(x, y + y0 + hh, z)]
            mesh_obj("Fir", vs, [(i, (i + 1) % n, n) for i in range(n)], m)

day()
shoot(os.path.join(RENDER, "cabins-hamlet-day.jpg"), (6, hill(6, -52) + 7, -52), (33, hill(33, 30) + 4, 30), 30)           # riding down toward it
shoot(os.path.join(RENDER, "cabins-hamlet-passing.jpg"), (12, hill(12, 22) + 2.2, 22), (36, hill(36, 52) + 5, 52), 24)     # passing the cabins
shoot(os.path.join(RENDER, "cabins-hamlet-below.jpg"), (-14, hill(-14, 92) + 9, 92), (34, hill(34, 22) + 2, 22), 34)       # looking back up
dusk()
shoot(os.path.join(RENDER, "cabins-hamlet-dusk.jpg"), (-14, hill(-14, 92) + 9, 92), (34, hill(34, 22) + 2, 22), 34)
