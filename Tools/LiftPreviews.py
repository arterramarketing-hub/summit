# Preview pictures of the chairlift, rendered in Blender's Cycles from the parts Tools/MakeLift.py has just built
# (run through it:  python3 Tools/MakeLift.py -- Assets/Summit/Resources/Lift --previews Tools/art).
# The game cannot be run where these were made, so the parts are put together here exactly as SkiLift.cs does it:
# the same frames for towers, sheave trains, chairs and stations, the same sag in the cable, and for the span
# picture a real lift from LiftCheck.py's port of the mountain and of LiftLine.cs.

import bpy, bmesh, math, os
from mathutils import Vector, Matrix
import LiftCheck as LC

HALF = LC.HALF
SEAT_RIDERS = "ABCD"


def render(objs, tex_dir, out_dir, TOB):
    scene = bpy.context.scene
    root = objs["Mast"].parent
    for ob in objs.values():
        ob.hide_render = True
    made = []

    # ---- materials: the palette on every part (nearest filtering, as the game's import), snow, cable
    def palette_mat(glow):
        m = bpy.data.materials.new("Paint")
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.abspath(os.path.join(tex_dir, "LiftPaint.png")))
        tex.interpolation = "Closest"
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.55
        if glow > 0:
            g = nt.nodes.new("ShaderNodeTexImage")
            g.image = bpy.data.images.load(os.path.abspath(os.path.join(tex_dir, "LiftGlow.png")))
            g.interpolation = "Closest"
            nt.links.new(g.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = glow
        return m

    def flat(name, col, rough=0.6):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*col, 1)
        b.inputs["Roughness"].default_value = rough
        return m

    snow = flat("Snow", (0.9, 0.92, 0.96), 0.5)
    cable_mat = flat("Cable", (0.12, 0.12, 0.13), 0.4)
    tree_mat = flat("Fir", (0.05, 0.13, 0.07), 0.8)
    paint = {"m": palette_mat(0)}

    def G(m):   # a game-space matrix as a Blender world matrix for a part built through TOB
        return TOB @ m @ TOB

    def frame(pos, x, y, z, scale=(1, 1, 1)):
        m = Matrix.Identity(4)
        for i, v in enumerate((Vector(x) * scale[0], Vector(y) * scale[1], Vector(z) * scale[2])):
            m[0][i], m[1][i], m[2][i] = v.x, v.y, v.z
        m[0][3], m[1][3], m[2][3] = pos[0], pos[1], pos[2]
        return m

    def put(part, m):
        ob = bpy.data.objects.new(part, objs[part].data)
        ob.matrix_world = G(m)
        ob.data.materials[0] = paint["m"]
        scene.collection.objects.link(ob)
        made.append(ob)
        return ob

    def mesh_obj(name, verts, faces, mat, smooth=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata([TOB @ Vector(v) for v in verts], [], faces)
        me.materials.append(mat)
        for p in me.polygons:
            p.use_smooth = smooth
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        made.append(ob)
        return ob

    def clear():
        for ob in made:
            bpy.data.objects.remove(ob, do_unlink=True)
        made.clear()

    up = Vector((0, 1, 0))

    # ---- a lift put together as the game does it
    class Line:
        pass

    def build(l, height, chairs_t=0.0, riders=True, lod=False, ground_mat=snow, ground_box=None, trees=False):
        d = Vector((l.dx, 0, l.dz)); p = Vector((l.px, 0, l.pz))
        sl, cl = math.sin(l.lean), math.cos(l.lean)
        axis = up * cl + d * sl
        fwd = d * cl - up * sl
        nodes = []   # (s, y) along the centre line
        top = Vector((l.xt, 0, l.zt))
        nodes.append((LC.STATION_HALF_LEN, l.deck_t + LC.STATION_CABLE))
        for i, s in enumerate(l.s):
            back = l.h[i] * sl
            f = top + d * (s - back)
            f.y = height(f.x, f.z)
            put("Pad", frame(f, p, axis, fwd))
            put("Mast", frame(f - axis * 1.5, p, axis, fwd, (1, l.h[i] + 1.5, 1)))
            put("Head", frame(f + axis * l.h[i], p, axis, fwd))
            nodes.append((s, f.y + l.h[i] * cl))
        nodes.append((l.plan - LC.STATION_HALF_LEN, l.deck_b + LC.STATION_CABLE))

        def at(s, side):   # the cable's point at s on one side
            for i in range(len(nodes) - 1):
                if s <= nodes[i + 1][0] or i == len(nodes) - 2:
                    y = LC.cable_y(nodes, i, min(max(s, nodes[i][0]), nodes[i + 1][0]))
                    q = top + d * s + p * (side * HALF)
                    return Vector((q.x, y, q.z))

        # sheave trains along the cable at each tower, under it (or over it where the cable would lift off)
        for i in range(1, len(nodes) - 1):
            s = nodes[i][0]
            len_in, len_out = nodes[i][0] - nodes[i - 1][0], nodes[i + 1][0] - nodes[i][0]
            slope_in = (nodes[i][1] - nodes[i - 1][1] + 4 * LC.sag(len_in)) / len_in
            slope_out = (nodes[i + 1][1] - nodes[i][1] - 4 * LC.sag(len_out)) / len_out
            t = (d + up * (0.5 * (slope_in + slope_out))).normalized()
            hold = slope_out > slope_in + 0.01   # the cable would lift off here: a hold-down train
            n = t.cross(p).normalized()         # square to the cable, upward
            for side in (-1, 1):
                k = -1 if hold else 1           # a hold-down train is the same train turned over, its yoke still inside
                put("Sheaves", frame(at(s, side), p * side, n * k, t * (side * k)))
        # stations
        for (x, z, deck, out) in ((l.xt, l.zt, l.deck_t, 1), (l.xb, l.zb, l.deck_b, -1)):
            put("Station", frame(Vector((x, deck, z)), p * out, up, d * out))
        # the cables: both sides, a tube per span, and the loop round each bullwheel
        for side in (-1, 1):
            pts = []
            for i in range(len(nodes) - 1):
                n = max(2, int((nodes[i + 1][0] - nodes[i][0]) / 4))
                for q in range(n + (1 if i == len(nodes) - 2 else 0)):
                    pts.append(at(nodes[i][0] + (nodes[i + 1][0] - nodes[i][0]) * q / n, side))
            a0 = Vector((l.xt, l.deck_t + LC.STATION_CABLE, l.zt)) + d * -2.4 + p * (side * HALF)
            a1 = Vector((l.xb, l.deck_b + LC.STATION_CABLE, l.zb)) + d * 2.4 + p * (side * HALF)
            tube([a0] + pts + [a1], 0.03, cable_mat)
        # chairs: every SPACING metres round the loop, up on the right of the line looking up it
        loop = Loop(l, nodes, at, d, p)
        n = max(2, round(loop.length / 18.0))
        spacing = loop.length / n
        for i in range(n):
            s = (i * spacing + chairs_t * 5.0) % loop.length
            pos, travel, upside = loop.point(s)
            flat = Vector((travel.x, 0, travel.z)).normalized()
            x = up.cross(flat)   # right = up x forward
            x = Vector((flat.z, 0, -flat.x))
            put("ChairLod" if lod else "Chair", frame(pos, x, up, flat))
            h = LC_hash(i, 7)
            if riders and upside and h < 0.6:
                put("Riders" + SEAT_RIDERS[int(h / 0.6 * 4) % 4] + ("Lod" if lod else ""), frame(pos, x, up, flat))
        # the snow under it
        if ground_box:
            x0, x1, z0, z1, st = ground_box
            nx, nz = int((x1 - x0) / st) + 1, int((z1 - z0) / st) + 1
            verts = [(x0 + i * st, height(x0 + i * st, z0 + j * st), z0 + j * st) for j in range(nz) for i in range(nx)]
            faces = [(j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i) for j in range(nz - 1) for i in range(nx - 1)]
            mesh_obj("Snow", verts, faces, ground_mat)
            if trees:
                # stand-in firs (cones) where the forest would stand: off the piste and out of the lift's swath
                tv, tf = [], []
                for cz in range(int(z0 / 6.5), int(z1 / 6.5)):
                    for cx in range(int(x0 / 6.5), int(x1 / 6.5)):
                        wx, wz = (cx + 0.5) * 6.5, (cz + 0.5) * 6.5
                        off = abs(wx - LC.centre(wz))
                        if off < LC.PISTE_HALF + 2 or LC.hash01(cx, cz, 1) > 0.16 + 0.4 * min(1, (off - 20) / 30):
                            continue
                        if LC.taken([l], wx, wz, 1.0):
                            continue
                        hgt = 9 + 9 * LC.hash01(cx, cz, 4)
                        y = height(wx, wz)
                        k = len(tv)
                        tv.append((wx, y + hgt, wz))
                        for a in range(6):
                            tv.append((wx + math.cos(a * math.pi / 3) * hgt * 0.17, y + 1.0, wz + math.sin(a * math.pi / 3) * hgt * 0.17))
                        for a in range(6):
                            tf.append((k, k + 1 + a, k + 1 + (a + 1) % 6))
                mesh_obj("Firs", tv, tf, tree_mat, smooth=False)
        return loop

    def tube(pts, r, mat):
        verts, faces = [], []
        for i, q in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            a = t.cross(up).normalized(); b = t.cross(a)
            for k in range(5):
                ang = k * 2 * math.pi / 5
                verts.append(q + (a * math.cos(ang) + b * math.sin(ang)) * r)
        for i in range(len(pts) - 1):
            for k in range(5):
                faces.append((i * 5 + k, i * 5 + (k + 1) % 5, (i + 1) * 5 + (k + 1) % 5, (i + 1) * 5 + k))
        mesh_obj("Cable", verts, faces, mat)

    class Loop:
        """The chairs' path: up one side, round the top bullwheel, down the other, round the bottom one."""
        def __init__(self, l, nodes, at, d, p):
            pts = []
            s0, s1 = nodes[0][0], nodes[-1][0]
            n = int((s1 - s0) / 1.0)
            wt = Vector((l.xt, l.deck_t + LC.STATION_CABLE, l.zt)) + d * -2.4
            wb = Vector((l.xb, l.deck_b + LC.STATION_CABLE, l.zb)) + d * 2.4
            upp = [at(s1 - (s1 - s0) * i / n, 1) for i in range(n + 1)]       # up the +p side
            dn = [at(s0 + (s1 - s0) * i / n, -1) for i in range(n + 1)]       # down the -p side
            arc_t = [wt + p * (HALF * math.cos(a)) - d * (HALF * math.sin(a)) for a in [math.pi * k / 12 for k in range(13)]]
            arc_b = [wb - p * (HALF * math.cos(a)) + d * (HALF * math.sin(a)) for a in [math.pi * k / 12 for k in range(13)]]
            self.pts = upp + arc_t + dn + arc_b
            self.up_n = len(upp)
            self.cum = [0.0]
            for i in range(len(self.pts)):
                self.cum.append(self.cum[-1] + (self.pts[(i + 1) % len(self.pts)] - self.pts[i]).length)
            self.length = self.cum[-1]

        def point(self, s):
            for i in range(len(self.pts)):
                if s <= self.cum[i + 1]:
                    a, b = self.pts[i], self.pts[(i + 1) % len(self.pts)]
                    u = (s - self.cum[i]) / max(1e-6, self.cum[i + 1] - self.cum[i])
                    return a.lerp(b, u), (b - a).normalized(), i < self.up_n

    def LC_hash(i, salt):
        return LC.hash01(i, 0, 140 + salt)

    # ---- camera, light, render
    def shoot(name, cam_at, look_at, lens=35, sun=(50, 20), strength=3.5, night=0.0, res=(1280, 720), samples=40):
        if only and not any(o in name for o in only.split(",")):   # LIFT_SHOTS=far,night renders only those
            return
        cam = bpy.data.cameras.new("Cam"); cam.lens = lens; cam.clip_end = 3000
        co = bpy.data.objects.new("Cam", cam); scene.collection.objects.link(co); made.append(co)
        co.location = TOB @ Vector(cam_at)
        dirv = (TOB @ Vector(look_at)) - co.location
        co.rotation_euler = dirv.to_track_quat("-Z", "Y").to_euler()
        scene.camera = co
        sd = bpy.data.lights.new("Sun", "SUN"); sd.energy = strength * (1 - night) + 0.05 * night; sd.angle = math.radians(2)
        sd.color = (1, 0.96, 0.9) if night == 0 else (0.6, 0.7, 1.0)
        so = bpy.data.objects.new("Sun", sd); scene.collection.objects.link(so); made.append(so)
        el, az = math.radians(sun[0]), math.radians(sun[1])
        to_sun = TOB @ Vector((math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az)))
        so.rotation_euler = to_sun.to_track_quat("Z", "Y").to_euler()
        world = bpy.data.worlds.new("World"); world.use_nodes = True
        bg = world.node_tree.nodes["Background"]
        bg.inputs["Color"].default_value = (0.45, 0.62, 0.9, 1) if night == 0 else (0.02, 0.035, 0.08, 1)
        bg.inputs["Strength"].default_value = 1.0 if night == 0 else 0.6
        scene.world = world
        scene.render.engine = "CYCLES"
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.view_settings.view_transform = "Standard"
        scene.render.image_settings.file_format = "JPEG"
        scene.render.image_settings.quality = 88
        scene.render.filepath = os.path.abspath(os.path.join(out_dir, name))
        try:
            bpy.ops.render.render(write_still=True)
        except Exception as e:   # no denoiser in this build
            scene.cycles.use_denoising = False
            bpy.ops.render.render(write_still=True)
        print("LIFT preview", scene.render.filepath)

    # ---- a short lift on a plain slope of the mountain's own pitch, for the close pictures
    def plain(x, z):
        return -0.72 * z + 0.6 * math.sin(z * 0.05) + 0.4 * math.sin(x * 0.08 + 1.0)

    def toy():
        l = Line()
        l.dx, l.dz, l.px, l.pz = 0.0, 1.0, 1.0, 0.0
        l.xt, l.zt, l.xb, l.zb, l.plan = 0.0, 0.0, 0.0, 226.0, 226.0
        l.deck_t = max(plain(0, z) for z in (-6, 0, 6)) + 0.3
        l.deck_b = max(plain(0, z) for z in (220, 226, 232)) + 0.3
        l.lean = 0.5 * math.atan2(l.deck_t - l.deck_b, l.plan)
        l.s = [44.0, 113.0, 182.0]
        l.h = [11.0, 12.0, 11.0]
        return l

    os.makedirs(out_dir, exist_ok=True)
    LC.STRICT = False
    only = os.environ.get('LIFT_SHOTS', '')
    box = (-70, 70, -40, 270, 2.0)

    l = toy()
    build(l, plain, chairs_t=1.2, ground_box=box)
    f = Vector((0, plain(0, 113 - 12 * math.sin(l.lean)), 113))
    shoot("lift-tower.jpg", (17, f.y + 4.5, 140), (0, f.y + 9.5, 112), lens=30)
    shoot("lift-tower-head.jpg", (7.5, f.y + 15.5, 124.5), (0.6, f.y + 11.6, 113), lens=40)
    # a chair with riders, from just above and ahead of it on the way up
    loop = Loop(l, [(6, l.deck_t + 4.6)] + [(s, plain(0, s - h * math.sin(l.lean)) + h * math.cos(l.lean)) for s, h in zip(l.s, l.h)] + [(220, l.deck_b + 4.6)],
                lambda s, side: Vector((0, 0, 0)), Vector((0, 0, 1)), Vector((1, 0, 0)))
    clear()
    paint["m"] = palette_mat(0)
    # (the chair picture: four chairs in a row on a cable, one of each load)
    for i, r in enumerate(("B", "D", "A", "C")):
        pos = Vector((i * 3.4 - 5.1, 10, 0))
        put("Chair", frame(pos, (1, 0, 0), up, (0, 0, 1)))
        put("Riders" + r, frame(pos, (1, 0, 0), up, (0, 0, 1)))
        tube([pos + Vector((0, 0, -3)), pos + Vector((0, 0, 3))], 0.03, cable_mat)
    put("Chair", frame(Vector((8.6, 10, 0)), (1, 0, 0), up, (0, 0, 1)))
    tube([Vector((8.6, 10, -3)), Vector((8.6, 10, 3))], 0.03, cable_mat)
    mesh_obj("Snow", [(-40, 4.5, -40), (40, 4.5, -40), (40, 4.5, 40), (-40, 4.5, 40)], [(0, 1, 2, 3)], snow)
    shoot("lift-chairs.jpg", (4.5, 9.4, 11.5), (1.2, 7.9, 0), lens=42, sun=(45, 40))
    clear()
    for i, r in enumerate(("B", "D", "A", "C")):
        pos = Vector((i * 3.4 - 5.1, 10, 0))
        put("ChairLod", frame(pos, (1, 0, 0), up, (0, 0, 1)))
        put("Riders" + r + "Lod", frame(pos, (1, 0, 0), up, (0, 0, 1)))
    put("HeadLod", frame(Vector((12, 10, 0)), (1, 0, 0), up, (0, 0, 1)))
    put("MastLod", frame(Vector((12, 4, 0)), (1, 0, 0), up, (0, 0, 1), (1, 6, 1)))
    put("SheavesLod", frame(Vector((12 + HALF, 10, 0)), (1, 0, 0), up, (0, 0, 1)))
    put("SheavesLod", frame(Vector((12 - HALF, 10, 0)), (-1, 0, 0), up, (0, 0, -1)))
    mesh_obj("Snow", [(-40, 4.5, -40), (40, 4.5, -40), (40, 4.5, 40), (-40, 4.5, 40)], [(0, 1, 2, 3)], snow)
    shoot("lift-far-versions.jpg", (5.5, 9.4, 16), (3.5, 8.2, 0), lens=42, sun=(45, 40), res=(1280, 560))
    clear()

    l = toy()
    build(l, plain, chairs_t=0.4, ground_box=box)
    shoot("lift-station-top.jpg", (21, l.deck_t + 2.5, 30), (0, l.deck_t + 0.5, 3), lens=28, sun=(40, 60))
    shoot("lift-station-bottom.jpg", (-20, l.deck_b + 6, 250), (0, l.deck_b + 3.0, 222), lens=28, sun=(40, -40))
    clear()
    # dusk: the lamps, windows and beacons glowing
    paint["m"] = palette_mat(9.0)
    build(l, plain, chairs_t=0.4, ground_box=box)
    shoot("lift-night.jpg", (-24, l.deck_b + 9, 262), (0, l.deck_b + 14, 200), lens=30, sun=(25, -60), night=1.0, samples=96)
    clear()
    paint["m"] = palette_mat(0)

    # ---- a real lift on a real mountain: the first that crosses the piste, seen from the piste below the crossing
    for sd in (4242, 7, 11, 23, 5, 3):
        res = LC.check(sd)
        crossing = [r for r in res if r["ok"] and r["crossings"]]
        if crossing:
            break
    r = crossing[0]
    l = r["lift"]
    zc = r["crossings"][0][0]
    xs = [l.xt, l.xb]
    gb = (min(xs + [LC.centre(l.zt), LC.centre(l.zb)]) - 90, max(xs + [LC.centre(l.zt), LC.centre(l.zb)]) + 90, l.zt - 30, l.zb + 30, 3.0)
    build(l, LC.height, chairs_t=2.0, ground_box=gb, trees=True)
    zcam = zc + 85
    xc = LC.centre(zcam)
    shoot("lift-crossing.jpg", (xc, LC.height(xc, zcam) + 2.2, zcam), (LC.centre(zc), LC.height(LC.centre(zc), zc) + 9, zc), lens=26, sun=(48, 150))
    zcam = l.zt + 0.45 * (l.zb - l.zt)
    xc = LC.centre(zcam) + 6
    shoot("lift-span.jpg", (xc, LC.height(xc, zcam) + 2.2, zcam), (l.xt + (l.xb - l.xt) * 0.2, LC.height(l.xt, l.zt + 60) + 8, l.zt + 60), lens=24, sun=(48, 150))
    xm, zm = (l.xt + l.xb) / 2 + 260, (l.zt + l.zb) / 2 + 40
    shoot("lift-whole.jpg", (xm, LC.height(l.xt, l.zt) + 60, zm), ((l.xt + l.xb) / 2, LC.height((l.xt + l.xb) / 2, (l.zt + l.zb) / 2), (l.zt + l.zb) / 2), lens=30, sun=(48, 150))
    print("LIFT span previews: seed %d, lift %d" % (r["seed"], r["k"]))
    clear()
