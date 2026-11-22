# Builds the summit camp's lodge in Blender and exports it to Assets/Summit/Resources/Camp/Lodge.fbx.
#   /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/MakeLodge.py -- Assets/Summit/Resources/Camp/Lodge.fbx
#
# A contemporary timber ski lodge after the user's photo (Tools/art/reference/lodge-reference.jpg):
#   - one long shed roof (17 degrees) rising from the low end (+x, the photo's left) to the high end (-x), its deep
#     eaves carried on exposed warm glulam rafters and beams, thin charcoal metal edges and a thick rounded snow load
#   - behind it, a tall volume under a flat roof; between the rising shed roof and that roof, a glazed clerestory, tall
#     at the low end and closing to nothing where the shed reaches it; a dark gabled block on top
#   - a lower wing at the high end with its own snowy roof and rafter tails
#   - dark-stained vertical board cladding, floor-to-ceiling glass in black frames (two storeys at the high end)
#   - a raised concrete terrace across the front and round the low end, wide concrete steps, low concrete planters,
#     chairs, a fire table, a chalkboard sign; at the low end the roof's tail runs on, nearly level, over a covered
#     section eight metres deep: ski and board racks along its back, picnic tables, the side door's way out between them
# Inside: one open hall (22 x 14 m) under the sloping timber ceiling and the tall volume (10 m), glulam columns and
# beams, a polished concrete floor, timber-lined walls, and the wing (5 x 10.5 m) open off its high end. Only the
# game's three stations are fitted (missions board, tune-up bench, wardrobe); the rest is open floor, waiting for the
# layout (lodge.md).
#
# Everything is authored in the game's own axes, like Tools/MakeChalet.py: x right, y up, z out of the front (downhill),
# the pivot the middle of the hall's floor. One mesh per material; the game paints the materials itself, by name
# (CampLodge.cs). Helper boxes, as MakeChalet.py's:
#   COL_ walls (walking and the camera)   CAM_ the camera only   BLK_ furniture (walking only)
#   FLR_ floors   RMP_ steps (top falls by its height from its -z end to its +z end)
#   TAP_/SPOT_ the stations   FX_fire the fire table's flames   LIGHT_ lamps   MARK_ which way it faces
#   AREA_ the inside (for the camera and the footsteps), STEP_ the stairs' centre lines (for the way down)

import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "Lodge.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)

# game axes -> Blender axes (a mirror: every face is turned round as it is copied in; see MakeChalet.py)
TOB = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

# metres a texture's tile spans (its UVs are the face's metres over this). The lodge's PBR textures (CampLodge.cs,
# Resources/Camp/LodgeTex, Tools/MakeLodgeTex.py) at their real size: the oak veneer 1.83 m, the hinoki planks 1.89,
# the black planks 1.6, the concrete and the stacked stone 2.0, the steel 1.0; the fabrics larger than life (a weave
# at its true 27 cm shimmers on a phone): the tweed 0.6, the rug's check 1.3, the leather 0.5
TILE = {"Cladding": 1.6, "Lining": 1.89, "Soffit": 1.89, "FloorBoards": 1.89, "Glulam": 1.83, "Oak": 1.83, "CounterTop": 1.83,
        "Concrete": 2.0, "Floor": 2.0, "Hearth": 2.0, "Steel": 1.0, "Upholstery": 0.6, "Cushion": 0.6, "Runner": 0.6,
        "Rug": 1.3, "Hide": 0.5, "Plaster": 4.0, "Roof": 2.0}
TURN = {"Cladding", "Lining"}   # vertical boards: the texture's boards turned upright
# the textures whose grain (or boards) runs along v (the veneer's, the black planks'): their UVs turned a quarter, so
# the grain runs along a beam, a table top, a tread (and, with TURN, the black planks' boards stand upright)
GRAIN_V = {"Glulam", "Oak", "CounterTop", "Cladding"}
# small coloured things share one material, "Paint", each a cell of a 16-colour strip the game makes (CampLodge.cs):
# the five board colours, the chairs' orange and green, canvas, wool, leather, charcoal, white, leaf, red, navy, cream
PALETTE = {"Swatch1": 0, "Swatch2": 1, "Swatch3": 2, "Swatch4": 3, "Swatch5": 4, "ArtA": 5, "ArtB": 6, "Fabric": 7,
           "Wool": 8, "Leather": 9, "Charcoal": 10, "White": 11, "Leaf": 12, "Red": 13, "Navy": 14, "Cream": 15}
PAL_N = 16
# (and the roof's metal and the mirror are the frames' black steel)
SAME = {"Roof": "Steel", "Mirror": "Steel"}
geo = {}
tris = {}
# Inside or out: the materials used in both are drawn as two meshes ("Glulam" and "Glulam_in", one material), so the
# lamps inside light what's inside and the porch's and the fire table's what's outside (a phone lights each mesh with
# its four most important lamps).
ZONE = [""]
SPLIT = {"Glulam", "Steel", "Paint", "Concrete"}


class inside:
    def __enter__(self):
        ZONE[0] = "_in"

    def __exit__(self, *a):
        ZONE[0] = ""


def mesh_for(mat):
    key = mat + (ZONE[0] if mat in SPLIT else "")
    if key not in geo:
        b = bmesh.new()
        b.loops.layers.uv.new("UVMap")
        geo[key] = b
    return geo[key]


def add(src, mat, m, full_uv=False):
    """Copy a temp bmesh into the material's mesh through matrix m (game space), box-projected UVs (or a palette cell)."""
    point = None
    turn = False
    if "|" in mat:
        mat, turn = mat.split("|")[0], True
    if mat in PALETTE:
        point = ((PALETTE[mat] + 0.5) / PAL_N, 0.5)
        mat = "Paint"
    mat = SAME.get(mat, mat)
    dst = mesh_for(mat)
    uv = dst.loops.layers.uv.active
    xf = TOB @ m
    vmap = {v: dst.verts.new(xf @ v.co) for v in src.verts}
    src_uv = src.loops.layers.uv.active
    tile = TILE.get(mat, 1.0)
    turn = turn or mat in TURN
    for f in src.faces:
        nf = dst.faces.new([vmap[v] for v in reversed(f.verts)])   # reversed: the mirror above
        nf.smooth = f.smooth
        nf.normal_update()
        n = nf.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l, sl in zip(nf.loops, reversed(f.loops)):
            p = l.vert.co
            if point is not None:
                l[uv].uv = point
            elif full_uv and src_uv is not None:
                l[uv].uv = sl[src_uv].uv
            else:
                c = (p.x / tile, p.y / tile) if ax == 2 else (p.y / tile, p.z / tile) if ax == 0 else (p.x / tile, p.z / tile)
                l[uv].uv = (c[1], c[0]) if (turn and ax != 2) != (mat in GRAIN_V) else c
        tris[mat] = tris.get(mat, 0) + len(f.verts) - 2
    # (for the coplanar audit: each face in game space, the way it faces; the palette's cells told apart)
    key = (mat if point is None else "Paint(%s)" % [k for k, v in PALETTE.items() if (v + 0.5) / PAL_N == point[0]][0]) + (ZONE[0] if mat in SPLIT else "")
    for f in src.faces:
        FACES.append((key, [tuple(m @ v.co) for v in f.verts]))
    # (and a closed convex shape, for telling whether a face is buried inside something)
    if src.faces and all(len(e.link_faces) == 2 for e in src.edges):
        pts = [m @ v.co for v in src.verts]
        planes = []
        for f in src.faces:
            vs = [m @ v.co for v in f.verts]
            n = (vs[1] - vs[0]).cross(vs[2] - vs[0])
            if n.length < 1e-9:
                continue
            n.normalize()
            c = sum(vs, Vector()) / len(vs)
            if all(n.dot(p - c) < 1e-5 for p in pts):
                planes.append((n, n.dot(c)))
            elif all(n.dot(p - c) > -1e-5 for p in pts):
                planes.append((-n, -n.dot(c)))   # (wound the other way: the mirror)
            else:
                planes = None
                break
        if planes:
            SOLIDS.append(([min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)], planes))
    src.free()


# ---------------------------------------------------------------- the coplanar audit

# Two surfaces drawn in the same plane, facing the same way and overlapping, fight over which shows (z-fighting: a
# flicker as the camera moves). The audit finds every such pair among the faces that lie flat on an axis (floors,
# walls, tops): those of the shell alone, and of each layout with the shell (they are drawn together).
FACES = []
SOLIDS = []
SHELL_SOLIDS = []
SAME_LOG = True   # (list the same-material pairs by kind too)


def buried(p):
    """Is the point inside something solid (a closed convex shape of the lodge's)?"""
    for lo, hi, planes in SOLIDS + SHELL_SOLIDS:
        if not (lo[0] < p[0] < hi[0] and lo[1] < p[1] < hi[1] and lo[2] < p[2] < hi[2]):
            continue
        if all(n.dot(Vector(p)) < d - 1e-4 for n, d in planes):
            return True
    return False


def _poly_area(P):
    return 0.5 * abs(sum(P[i][0] * P[(i + 1) % len(P)][1] - P[(i + 1) % len(P)][0] * P[i][1] for i in range(len(P))))


def _clip(P, Q):
    """P clipped by the convex polygon Q (Sutherland-Hodgman), both 2D, Q counter-clockwise."""
    out = P
    for i in range(len(Q)):
        a, b = Q[i], Q[(i + 1) % len(Q)]
        inp, out = out, []
        if not inp:
            break
        def side(p):
            return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        for j in range(len(inp)):
            cur, prv = inp[j], inp[j - 1]
            sc, sp = side(cur), side(prv)
            if sc >= 0:
                if sp < 0:
                    t = sp / (sp - sc)
                    out.append((prv[0] + (cur[0] - prv[0]) * t, prv[1] + (cur[1] - prv[1]) * t))
                out.append(cur)
            elif sp >= 0:
                t = sp / (sp - sc)
                out.append((prv[0] + (cur[0] - prv[0]) * t, prv[1] + (cur[1] - prv[1]) * t))
    return out


def _flat(faces):
    """The faces lying flat on an axis: (axis, side, offset, 2D polygon counter-clockwise, material, centre)."""
    out = []
    for key, pts in faces:
        if len(pts) < 3:
            continue
        n = [0.0, 0.0, 0.0]
        for i in range(len(pts)):
            p, q = pts[i], pts[(i + 1) % len(pts)]
            n[0] += (p[1] - q[1]) * (p[2] + q[2]); n[1] += (p[2] - q[2]) * (p[0] + q[0]); n[2] += (p[0] - q[0]) * (p[1] + q[1])
        L = math.sqrt(sum(c * c for c in n))
        if L < 1e-9:
            continue
        n = [c / L for c in n]
        ax = max(range(3), key=lambda i: abs(n[i]))
        if abs(n[ax]) < 0.9995:
            continue
        u, v = [i for i in range(3) if i != ax]
        P = [(p[u], p[v]) for p in pts]
        sgn = sum(P[i][0] * P[(i + 1) % len(P)][1] - P[(i + 1) % len(P)][0] * P[i][1] for i in range(len(P)))
        if sgn < 0:
            P = P[::-1]
        if _poly_area(P) < 1e-6:
            continue
        off = sum(p[ax] for p in pts) / len(pts)
        c = tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))
        out.append((ax, 1 if n[ax] > 0 else -1, off, P, key, c))
    return out


def coplanar(faces, new_from=0, tol=0.003, min_area=2e-4):
    """Pairs of flat faces in one plane (within tol), facing the same way and overlapping by more than min_area m^2;
    only pairs with a face at index >= new_from (the layout's own, when the shell's come first)."""
    fl = _flat(faces[:new_from]) , _flat(faces[new_from:])
    old, new = fl
    allf = [(f, False) for f in old] + [(f, True) for f in new]
    buckets = {}
    for i, (f, isnew) in enumerate(allf):
        k = (f[0], f[1], round(f[2] / tol))
        buckets.setdefault(k, []).append(i)
    hits = []
    seen = set()
    for (ax, sd, ko), idx in buckets.items():
        near = []
        for dk in (-1, 0, 1):
            near += buckets.get((ax, sd, ko + dk), [])
        for i in idx:
            fi, ni = allf[i]
            xs = [p[0] for p in fi[3]]; ys = [p[1] for p in fi[3]]
            for j in near:
                if j <= i or (i, j) in seen:
                    continue
                fj, nj = allf[j]
                if not (ni or nj) and new_from > 0:
                    continue
                if abs(fi[2] - fj[2]) > tol:
                    continue
                xj = [p[0] for p in fj[3]]; yj = [p[1] for p in fj[3]]
                if max(xs) <= min(xj) or max(xj) <= min(xs) or max(ys) <= min(yj) or max(yj) <= min(ys):
                    continue
                seen.add((i, j))
                ov = _clip(fi[3], fj[3])
                a = _poly_area(ov)
                if a > min_area:
                    # where the overlap is: its highest point (a vertical face's), or the plane's height
                    top = fi[2] if ax == 1 else max(p[0] if ax == 0 else p[1] for p in ov)   # (an x plane's 2D is (y, z), a z plane's (x, y))
                    hits.append((a, fi[4], fj[4], ax, fi[2], fi[5], fj[5], fi[1], top, (ov, ax, fi[2])))
    return hits


def _front(h, step=0.01):
    """Points just in front of the overlap (its middle and toward its corners), off the plane the way it faces."""
    ov, ax, off = h[9]
    u, v = [i for i in range(3) if i != ax]
    cu = sum(p[0] for p in ov) / len(ov); cv = sum(p[1] for p in ov) / len(ov)
    out = []
    for p in [(cu, cv)] + [(cu + (q[0] - cu) * 0.8, cv + (q[1] - cv) * 0.8) for q in ov]:
        w = [0.0, 0.0, 0.0]
        w[ax] = off + h[7] * step; w[u] = p[0]; w[v] = p[1]
        out.append(tuple(w))
    return out


def seen(h):
    """Could a player see this pair? Not a face facing down onto a floor or the ground (nobody looks up through the
    floor), nor anything wholly below the terrace's level (inside the base, under the snow)."""
    a, k1, k2, ax, off, c1, c2, side, top, _ = h
    if ax == 1 and side < 0 and off < 0.05:
        return False
    if top <= -0.3:
        return False
    # something solid all over in front of it (a wall's top under the roof, a back against a wall): nobody sees it
    return not all(buried(p) for p in _front(h))


def report_coplanar(name, faces, new_from=0):
    hits = coplanar(faces, new_from)
    vis = [h for h in hits if seen(h)]
    diff = [h for h in vis if h[1] != h[2]]
    same = [h for h in vis if h[1] == h[2]]
    print("LODGE %s coplanar overlaps a player could see: %d between different materials (%.3f m^2), %d within one "
          "material (%.2f m^2: the same texture at the same place, so nothing to see); %d more out of sight" %
          (name, len(diff), sum(h[0] for h in diff), len(same), sum(h[0] for h in same), len(hits) - len(vis)))
    if SAME_LOG:
        kinds = {}
        for h in same:
            kinds[(h[1], "xyz"[h[3]])] = kinds.get((h[1], "xyz"[h[3]]), 0) + 1
        print("LODGE %s   within one material, by kind: %s" % (name, ", ".join("%s %s-plane %d" % (k[0], k[1], v) for k, v in sorted(kinds.items(), key=lambda kv: -kv[1]))))
    for h in sorted(diff, key=lambda h: -h[0])[:60]:
        print("LODGE %s   %s/%s %s=%.3f facing %s, %.4f m^2 at (%.2f, %.2f, %.2f) and (%.2f, %.2f, %.2f)" %
              (name, h[1], h[2], "xyz"[h[3]], h[4], "+" if h[7] > 0 else "-", h[0], *h[5], *h[6]))
    return hits


def mat4(pos, size=(1, 1, 1), rot=(0, 0, 0)):
    return Matrix.Translation(Vector(pos)) @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*size, 1)))


def box(mat, pos, size, rot=(0, 0, 0)):
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    add(b, mat, mat4(pos, size, rot))


def lohi(mat, lo, hi):
    box(mat, [(lo[i] + hi[i]) / 2 for i in range(3)], [abs(hi[i] - lo[i]) for i in range(3)])


def span(mat, a, b_, w, h=None):
    """A beam from a to b, w wide and h deep (square if h is None), its depth kept upright."""
    a, b_ = Vector(a), Vector(b_)
    d = b_ - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    add(b, mat, Matrix.Translation((a + b_) / 2) @ rot @ Matrix.Diagonal(Vector((w, h if h else w, d.length, 1))))


def cyl(mat, pos, r, h, seg=12, r2=None, smooth=False):
    """A cylinder (or cone) standing on pos along the game's up axis."""
    b = bmesh.new()
    bmesh.ops.create_cone(b, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    if smooth:
        for f in b.faces:
            f.smooth = len(f.verts) == 4
    up = Matrix.Rotation(math.radians(-90), 4, "X")
    add(b, mat, Matrix.Translation(Vector(pos) + Vector((0, h / 2, 0))) @ up)


def sphere(mat, pos, size, seg=12):
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=seg, v_segments=max(4, seg // 2), radius=0.5)
    for f in b.faces:
        f.smooth = True
    add(b, mat, mat4(pos, size))


def solid(mat, bottom, top, smooth_top=False):
    """A closed shape between two rings of points (game space), each listed the same way round."""
    b = bmesh.new()
    lo = [b.verts.new(p) for p in bottom]
    hi = [b.verts.new(p) for p in top]
    n = len(lo)
    b.faces.new(lo)
    ft = b.faces.new(hi)
    for i in range(n):
        j = (i + 1) % n
        b.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    ft.smooth = smooth_top
    add(b, mat, Matrix.Identity(4))


def quad(mat, pos, w, h, yaw=0):
    """A sign or picture: a flat card facing the game's +z (turned by yaw degrees), its texture filling it."""
    b = bmesh.new()
    uv = b.loops.layers.uv.new("UVMap")
    vs = [b.verts.new(c) for c in ((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0))]
    f = b.faces.new(vs)
    for l, c in zip(f.loops, ((1, 0), (0, 0), (0, 1), (1, 1))):   # u runs right to left: the mirror turns it back
        l[uv].uv = c
    add(b, mat, mat4(pos, (w, h, 1), (0, yaw, 0)), full_uv=True)


def samples(a, b, r, step, fracs):
    L = b - a
    r = min(r, L / 2)
    pts = {a, b}
    for f in fracs:
        pts.add(a + f * r)
        pts.add(b - f * r)
    n = max(1, int(round((L - 2 * r) / step)))
    for i in range(1, n):
        pts.add(a + r + (L - 2 * r) * i / n)
    return sorted(pts)


def snow(x0, x1, z0, z1, h, th, r=0.6, lip=0.42, drape=0.05, step=3.0, fine=True, xbreaks=()):
    """Snow lying on a surface whose top is at height h(x, z), over x0..x1, z0..z1: th deep in the middle, its edges
    rounded over r (a lip of lip*th left at the very edge, dropping drape below the surface: a settled, rounded load).
    xbreaks: where the surface kinks (a row of the snow's points there, so it follows the kink)."""
    xs = samples(x0, x1, r, step, (0.1, 0.28, 0.5, 0.75, 1.0) if fine else (0.35, 1.0))
    xs = sorted(set(xs) | {x for x in xbreaks if x0 < x < x1})
    zs = samples(z0, z1, r, step, (0.1, 0.28, 0.5, 0.75, 1.0) if fine else (0.35, 1.0))

    def f(x, z):
        d = min(x - x0, x1 - x, z - z0, z1 - z)
        t = min(1.0, max(0.0, d / r))
        return th * (lip + (1 - lip) * math.sqrt(max(0.0, 1 - (1 - t) ** 2)))

    b = bmesh.new()
    top = [[b.verts.new((x, h(x, z) + f(x, z), z)) for x in xs] for z in zs]
    nx, nz = len(xs), len(zs)
    grid = []
    for j in range(nz - 1):
        for i in range(nx - 1):
            grid.append(b.faces.new((top[j][i], top[j + 1][i], top[j + 1][i + 1], top[j][i + 1])))
    ring = [(0, i) for i in range(nx - 1)] + [(j, nx - 1) for j in range(nz - 1)] + \
           [(nz - 1, i) for i in range(nx - 1, 0, -1)] + [(j, 0) for j in range(nz - 1, 0, -1)]
    bot = []
    for (j, i) in ring:
        p = top[j][i].co
        bot.append(b.verts.new((p.x, h(p.x, p.z) - drape, p.z)))
    for k in range(len(ring)):
        a, c = ring[k], ring[(k + 1) % len(ring)]
        b.faces.new((top[a[0]][a[1]], top[c[0]][c[1]], bot[(k + 1) % len(ring)], bot[k]))
    b.faces.new(list(reversed(bot)))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    for fc in grid:
        fc.smooth = True
    add(b, "Snow", Matrix.Identity(4))


def pillow(pos, size, inset=0.06):
    """A small, chamfered pad of snow (a rail's or a lamp's top)."""
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    kx, kz = max(0.25, (size[0] - 2 * inset) / size[0]), max(0.25, (size[2] - 2 * inset) / size[2])
    for v in b.verts:
        if v.co.z > 0:
            v.co.x *= kx
            v.co.y *= kz
    add(b, "Snow", mat4(pos) @ Matrix(((size[0], 0, 0, 0), (0, 0, size[1], 0), (0, size[2], 0, 0), (0, 0, 0, 1))))


helpers = []


def helper(name, lo, hi):
    c = [(lo[i] + hi[i]) / 2 for i in range(3)]
    s = [max(0.02, abs(hi[i] - lo[i])) for i in range(3)]
    helpers.append((name, c, s))


def marker(name, pos):
    helpers.append((name, pos, (0.1, 0.1, 0.1)))


# ---------------------------------------------------------------- the plan

XL, XH, XW = 11.0, -11.0, -16.0   # the low end, the high end, the wing's far end
D, T = 7.0, 0.3                   # half depth (front wall's outer face at +D), wall thickness
Y0, H = 3.2, 10.0                 # the shed roof's soffit at the low end's wall; the upper roof's soffit
S = (H - Y0) / (XL - XH)          # the shed's pitch (17 degrees, as steep as the photo's): it reaches H at the high end


def ru(x):
    """The shed roof's soffit (underside of its boards) at x: rising from the low end's wall; past it, over the covered
    section, its tail runs on nearly level (TP), so there's headroom out to the posts eight metres out."""
    return Y0 + (XL - x) * S if x <= XL else Y0 - (x - XL) * TP


RT = 0.3              # the roof's build-up over the soffit (deck, insulation, metal)
RD, RW = 0.34, 0.16   # rafters: depth and width, under the soffit
BD, BW = 0.5, 0.28    # beams under the rafters
OF, OB = 3.0, 1.0     # eaves: front and back
TAIL, XHO = 8.0, 1.4  # the shed runs on past the low end (over the covered section) and past the high end
TP = 0.07             # its tail's fall past the low end's wall (4 degrees: the 17 degrees on out would be 1.9 m at its edge)
XB, ZC = 5.0, 0.0     # the tall volume: x from XH to XB, z from -D to ZC
WZ0, WZ1 = -5.5, 5.0  # the wing's back and front
WY = 4.4              # the wing's soffit at its front
ZT = D + 5.6          # the terrace's front edge
STATIONS = [-6.37, -1.73, 2.9, 8.6]   # the glulam frames (beams along z, columns at the glass and at ZC)
DOOR = (3.1, 5.9, 2.9)                # the front door: x0, x1, height
SIDE_DOOR = (3.0, 4.9, 2.7)           # the low end's door to the side terrace: z0, z1, height
RISE, RUN, NSTEP = 0.17, 0.36, 14


# ---------------------------------------------------------------- walls

def wall(axis, fixed, inward, a0, a1, top, holes, outer="Cladding", inner="Lining", y0=0.0):
    """A wall along x (axis 'x', its outer face at z=fixed) or along z (at x=fixed), inward +1/-1 toward the inside,
    from a0 to a1 along it, up to top(a) (linear between the cuts), less rectangular holes (a0, a1, y0, y1)."""
    def P(a, y, d):
        return (a, y, fixed + inward * d) if axis == "x" else (fixed + inward * d, y, a)
    cuts = sorted({a0, a1} | {h[0] for h in holes if a0 < h[0] < a1} | {h[1] for h in holes if a0 < h[1] < a1})
    for a, b in zip(cuts, cuts[1:]):
        if b - a < 1e-3:
            continue
        mid = (a + b) / 2
        ys = [(y0, None)]
        for h in [h for h in holes if h[0] <= mid <= h[1]]:
            nys = []
            for (s0, s1) in ys:
                hi = s1 if s1 is not None else min(top(a), top(b))
                if h[3] <= s0 or h[2] >= hi:
                    nys.append((s0, s1))
                    continue
                if h[2] > s0:
                    nys.append((s0, h[2]))
                if s1 is None or h[3] < s1:
                    nys.append((h[3], s1))
            ys = nys
        for (s0, s1) in ys:
            ta, tb = (s1, s1) if s1 is not None else (top(a), top(b))
            if min(ta, tb) - s0 < 1e-3:
                continue
            for mat, d0, d1 in ((outer, 0.0, T - 0.04), (inner, T - 0.04, T)):
                if mat is None:
                    continue
                # (the inner boards end a centimetre short of the wall's own ends: where another wall meets this one,
                # its outer face and their ends were one plane, fighting)
                aa = a + 0.01 if mat == inner and abs(a - a0) < 1e-6 else a
                bb = b - 0.01 if mat == inner and abs(b - a1) < 1e-6 else b
                taa = ta if aa == a else ta + (tb - ta) * (aa - a) / (b - a)
                tbb = tb if bb == b else ta + (tb - ta) * (bb - a) / (b - a)
                solid(mat, [P(aa, s0, d0), P(bb, s0, d0), P(bb, s0, d1), P(aa, s0, d1)],
                      [P(aa, taa, d0), P(bb, tbb, d0), P(bb, tbb, d1), P(aa, taa, d1)])


def sheet(mat, pts, facing):
    """One side of a pane: a quad through four points (game space), facing along `facing`."""
    b = bmesh.new()
    f = b.faces.new([b.verts.new(p) for p in pts])
    f.normal_update()
    if f.normal.dot(Vector(facing)) < 0:
        f.normal_flip()
    add(b, mat, Matrix.Identity(4))


def pane(axis, fixed, a0, a1, yb, top0, top1, th=0.025, mat="Glass", yb1=None):
    """A pane of glass (or a panel) from a0 to a1 along a wall, from yb up to a top running top0 -> top1. Glass in
    the lodge's outside walls is two one-sided sheets: "Glass" facing out (it glows warm at dusk, as the photo's does)
    and "GlassIn" facing in (clear: from inside you see out); the panes inside are "GlassIn" boxes."""
    yb1 = yb if yb1 is None else yb1
    if mat == "Glass":
        out = 1.0 if fixed >= 0 else -1.0
        for m, side in (("Glass", out), ("GlassIn", -out)):
            d = fixed + side * th / 2
            if axis == "x":
                sheet(m, [(a0, yb, d), (a1, yb1, d), (a1, top1, d), (a0, top0, d)], (0, 0, side))
            else:
                sheet(m, [(d, yb, a0), (d, yb1, a1), (d, top1, a1), (d, top0, a0)], (side, 0, 0))
        return
    if axis == "x":
        solid(mat, [(a0, yb, fixed - th / 2), (a1, yb, fixed - th / 2), (a1, yb, fixed + th / 2), (a0, yb, fixed + th / 2)],
              [(a0, top0, fixed - th / 2), (a1, top1, fixed - th / 2), (a1, top1, fixed + th / 2), (a0, top0, fixed + th / 2)])
    else:
        solid(mat, [(fixed - th / 2, yb, a0), (fixed - th / 2, yb, a1), (fixed + th / 2, yb, a1), (fixed + th / 2, yb, a0)],
              [(fixed - th / 2, top0, a0), (fixed - th / 2, top1, a1), (fixed + th / 2, top1, a1), (fixed + th / 2, top0, a0)])


def mullion(axis, fixed, a, y0, y1, w=0.08, d=0.16):
    if axis == "x":
        lohi("Steel", (a - w / 2, y0, fixed - d / 2), (a + w / 2, y1, fixed + d / 2))
    else:
        lohi("Steel", (fixed - d / 2, y0, a - w / 2), (fixed + d / 2, y1, a + w / 2))


def transom(axis, fixed, a0, a1, y, h=0.08, d=0.16):
    """A horizontal frame member (its ends just inside a0, a1: in a corner's wall face, they fought it)."""
    a0, a1 = a0 + 0.005, a1 - 0.005
    if axis == "x":
        lohi("Steel", (a0, y - h / 2, fixed - d / 2), (a1, y + h / 2, fixed + d / 2))
    else:
        lohi("Steel", (fixed - d / 2, y - h / 2, a0), (fixed + d / 2, y + h / 2, a1))


def glazing(axis, fixed, a0, a1, top, bays, rows, y0=0.0):
    """A curtain wall: glass from y0 up to top(a) between a0 and a1, black mullions at the bay edges and at the
    ends, black transoms at the heights in rows (and along the head)."""
    gz = fixed
    edges = [a0] + bays + [a1]
    for a, b in zip(edges, edges[1:]):
        levels = [y0] + [r for r in rows if r < min(top(a), top(b)) - 0.5] + [None]
        for s0, s1 in zip(levels, levels[1:]):
            yb = s0 + (0.04 if s0 > y0 else 0.0)
            if s1 is None:
                pane(axis, gz, a, b, yb, top(a) - 0.04, top(b) - 0.04)
            else:
                pane(axis, gz, a, b, yb, s1 - 0.04, s1 - 0.04)
    for e in edges:
        mullion(axis, gz, e, y0, top(e))
    for r in rows:
        for a, b in zip(edges, edges[1:]):
            if r < min(top(a), top(b)) - 0.5:
                transom(axis, gz, a, b, r)
    # the head, along the top (sloped where the top is; its ends just inside, as the transoms')
    for a, b in zip(edges, edges[1:]):
        a, b = a + 0.005, b - 0.005
        if axis == "x":
            solid("Steel", [(a, top(a) - 0.09, gz - 0.08), (b, top(b) - 0.09, gz - 0.08), (b, top(b) - 0.09, gz + 0.08), (a, top(a) - 0.09, gz + 0.08)],
                  [(a, top(a), gz - 0.08), (b, top(b), gz - 0.08), (b, top(b), gz + 0.08), (a, top(a), gz + 0.08)])
        else:
            solid("Steel", [(gz - 0.08, top(a) - 0.09, a), (gz - 0.08, top(b) - 0.09, b), (gz + 0.08, top(b) - 0.09, b), (gz + 0.08, top(a) - 0.09, a)],
                  [(gz - 0.08, top(a), a), (gz - 0.08, top(b), b), (gz + 0.08, top(b), b), (gz + 0.08, top(a), a)])
    if y0 <= 0.01:
        if axis == "x":
            lohi("Steel", (a0 + 0.005, 0.0, gz - 0.1), (a1 - 0.005, 0.1, gz + 0.1))
        else:
            lohi("Steel", (gz - 0.1, 0.0, a0 + 0.005), (gz + 0.1, 0.1, a1 - 0.005))


# ---------------------------------------------------------------- the base and the floors

IX0, IX1, IZ0, IZ1 = XH + T, XL - T, -D + T, D - T   # the hall's inside faces
BASE = 3.2   # the concrete base runs this far down into the snow (the game sets the floor on the highest snow)

for (x0, x1, z0, z1) in ((XH, XL, -D, D), (XW, XH, WZ0, WZ1)):
    for s in (-1, 1):
        lohi("Concrete", (x0, -BASE, (z0 if s < 0 else z1 - T)), (x1, 0.0, (z0 + T if s < 0 else z1)))
        xa, xb = (x0, x0 + T) if s < 0 else (x1 - T, x1)
        if XH - T - 0.01 < xa < XH + 0.01:
            # where the wing opens off the hall the wing's floor runs on over the base (the hall's end and the wing's):
            # the concrete's top kept under the floor there, level with it only under the walls (z-fighting otherwise)
            for (za, zb, top) in ((z0, WZ0 + T, 0.0), (WZ0 + T, WZ1 - T, -0.25), (WZ1 - T, z1, 0.0)):
                if zb - za > 1e-3:
                    lohi("Concrete", (xa, -BASE, max(z0, za)), (xb, top, min(z1, zb)))
        else:
            lohi("Concrete", (xa, -BASE, z0), (xb, 0.0, z1))
# the oak floor, the hall's and the wing's (one level, open between them)
lohi("Floor", (IX0, -0.25, IZ0), (IX1, 0.0, IZ1))
lohi("Floor", (XW + T, -0.25, WZ0 + T), (XH + T, 0.0, WZ1 - T))
helper("FLR_hall", (IX0 - 0.2, -0.25, IZ0), (IX1 + 0.2, 0.0, IZ1 + 0.4))
helper("FLR_wing", (XW + T, -0.25, WZ0 + T), (XH + T, 0.0, WZ1 - T))
helper("AREA_hall", (IX0, 0.0, IZ0), (IX1, H, IZ1))
helper("AREA_wing", (XW + T, 0.0, WZ0 + T), (XH + T, WY, WZ1 - T))

# ---------------------------------------------------------------- the front: glass in black frames

# the low end's last few metres are boarded (a tall slit window in them); then a glass bay, the door's bay and two
# more, glass below and dark boards above with a row of windows (as the photo's middle); then the high end glazed
# two storeys to the roof. A dark band across at the first floor's height.
FZ = D - 0.12   # the glass's plane
BAND = (3.2, 3.45)
MID = -1.73     # where the full-height glass begins
wall("x", D, -1, 8.6, XL, ru, [(9.4, 10.1, 0.5, 2.9)])
pane("x", D - 0.15, 9.4, 10.1, 0.5, 2.9, 2.9)
for a in (9.4, 10.1):
    mullion("x", D - 0.15, a, 0.5, 2.9, 0.06, 0.2)
for y in (0.5, 2.9):
    transom("x", D - 0.15, 9.4, 10.1, y, 0.06, 0.2)
bays = [XH + (MID - XH) * i / 4 for i in range(1, 4)]
glazing("x", FZ, XH, MID, ru, bays, [BAND[0], 6.6])
glazing("x", FZ, MID, 2.9, lambda a: BAND[0], [(MID + 2.9) / 2], [])
glazing("x", FZ, 5.9, 8.6, lambda a: BAND[0], [], [])
# the door's bay: an opening 2.8 wide, glass above it
glazing("x", FZ, DOOR[0], DOOR[1], lambda a: BAND[0], [], [], y0=DOOR[2])
# above the band from the middle to the low end: dark boards, a row of windows
UPW = [(-1.4, 0.4), (1.0, 2.7), (3.5, 5.5)]
wall("x", D, -1, MID, 8.6, ru, [(a0, a1, 3.85, min(6.2, ru(a1) - 0.45)) for (a0, a1) in UPW], y0=BAND[1])
for (a0, a1) in UPW:
    glazing("x", D - 0.15, a0, a1, lambda a, a1=a1: min(6.2, ru(a1) - 0.45), [], [], y0=3.85)
for a in (DOOR[0], DOOR[1]):
    mullion("x", FZ, a, 0.0, DOOR[2], 0.14, 0.24)
# the door's two glass leaves, swung open inward against the jambs
for s, a in ((1, DOOR[0]), (-1, DOOR[1])):
    lw = (DOOR[1] - DOOR[0]) / 2 - 0.05
    x = a + s * 0.09
    lohi("Steel", (x - 0.04, 0.02, FZ - 0.1 - lw), (x + 0.04, DOOR[2] - 0.05, FZ - 0.1))
    pane("z", x, FZ - 0.1 - lw + 0.08, FZ - 0.18, 0.1, DOOR[2] - 0.13, DOOR[2] - 0.13, 0.02, "GlassIn")
    lohi("Steel", (x - 0.03, 0.95, FZ - 0.1 - lw + 0.15), (x + 0.03, 1.15, FZ - 0.1 - lw + 0.25))   # a pull
    helper("BLK_leaf", (x - 0.05, 0.0, FZ - 0.1 - lw), (x + 0.05, 2.8, FZ - 0.1))
# the dark band across the front at the first floor (outside the glass), its snow ledge
lohi("Cladding", (XH, BAND[0], FZ + 0.08), (8.6, BAND[1], FZ + 0.2))
# under the glass a black kerb on the concrete; the threshold flush
helper("FLR_door", (DOOR[0], -0.3, D - 0.4), (DOOR[1], 0.0, D + 0.3))

# ---------------------------------------------------------------- the back, the ends

def top_back(x):
    return H if x <= XB + 1e-6 else ru(x)


# the back: a band of high windows under the flat roof (light from the hill), one over the tune-up bench
wall("x", -D, 1, XH, XL, top_back, [(-10.2, 3.8, 7.0, 9.2), (7.8, 10.2, 1.4, 2.7)])
glazing("x", -D + 0.15, -10.2, 3.8, lambda a: 9.2, [-10.2 + 14.0 * i / 6 for i in range(1, 6)], [], y0=7.0)
glazing("x", -D + 0.15, 7.8, 10.2, lambda a: 2.7, [9.0], [], y0=1.4)
# the low end: the side door (to the racks under the roof's tail) and a window beside it
wall("z", XL, -1, -D, D, lambda z: Y0, [(SIDE_DOOR[0], SIDE_DOOR[1], 0.0, SIDE_DOOR[2]), (-2.6, 1.6, 0.9, 2.7)])
glazing("z", XL - 0.15, -2.6, 1.6, lambda a: 2.7, [-0.5], [], y0=0.9)
glazing("z", XL - 0.15, SIDE_DOOR[0], SIDE_DOOR[1], lambda a: Y0 - 0.25, [], [], y0=SIDE_DOOR[2])
for a in SIDE_DOOR[:2]:
    mullion("z", XL - 0.15, a, 0.0, SIDE_DOOR[2], 0.12, 0.24)
lohi("Steel", (XL - 0.2, SIDE_DOOR[2] - 0.05, SIDE_DOOR[0]), (XL - 0.1, SIDE_DOOR[2] + 0.05, SIDE_DOOR[1]))
helper("FLR_sidedoor", (XL - 0.5, -0.3, SIDE_DOOR[0]), (XL + 0.3, 0.0, SIDE_DOOR[1]))
# the high end: open to the wing below its roof, a big window above it, boarded either side
WOPEN = (-4.6, 4.1, 3.8)
wall("z", XH, 1, -D, D, lambda z: H, [(WOPEN[0], WOPEN[1], 0.0, WOPEN[2]), (-5.6, 5.6, 5.4, 9.4)])
glazing("z", XH + 0.15, -5.6, 5.6, lambda a: 9.4, [-1.87, 1.87], [], y0=5.4)
lohi("Glulam", (XH - 0.02, WOPEN[2] - 0.45, WOPEN[0]), (XH + T, WOPEN[2], WOPEN[1]))   # a glulam lintel over the opening (flush inside: an upper floor meets it there)
# the tall volume's side, over the shed roof at XB: boarded, a high window
wall("z", XB, -1, -D, ZC, lambda z: H, [(-6.0, -1.0, 6.6, 9.2)], y0=ru(XB) + RT - 0.05)
glazing("z", XB - 0.15, -6.0, -1.0, lambda a: 9.2, [-3.5], [], y0=6.6)
# the clerestory: glass from the shed roof up to the flat roof, facing the front over the hall
CLX = [XH + (XB - XH) * i / 7 for i in range(1, 7)]
for a, b in zip([XH] + CLX, CLX + [XB]):
    yb0, yb1 = ru(a) + RT, ru(b) + RT
    if H - max(yb0, yb1) < 0.05 and H - min(yb0, yb1) < 0.05:
        continue
    pane("x", ZC, a, b, min(yb0, H), H, H, yb1=min(yb1, H))
for a in CLX + [XB]:
    if H - (ru(a) + RT) > 0.1:
        mullion("x", ZC, a, ru(a) + RT, H)
CB0, CB1 = XH + 0.01, XB - 0.01   # (the clerestory's cap and beam: their ends inside the walls they run between)
solid("Steel", [(CB0, ru(CB0) + RT, ZC - 0.1), (CB1, ru(CB1) + RT, ZC - 0.1), (CB1, ru(CB1) + RT, ZC + 0.1), (CB0, ru(CB0) + RT, ZC + 0.1)],
      [(CB0, ru(CB0) + RT + 0.12, ZC - 0.1), (CB1, ru(CB1) + RT + 0.12, ZC - 0.1), (CB1, ru(CB1) + RT + 0.12, ZC + 0.1), (CB0, ru(CB0) + RT + 0.12, ZC + 0.1)])

# the wing: glass across its front, boarded end with a window, boarded back
glazing("x", WZ1 - 0.12, XW, XH, lambda a: WY - 0.05, [XW + 5.0 / 3, XW + 10.0 / 3], [3.1])
wall("z", XW, 1, WZ0, WZ1, lambda z: WY + (z - WZ1) * 0.02, [(-2.6, 2.4, 0.9, 3.0)])
glazing("z", XW + 0.15, -2.6, 2.4, lambda a: 3.0, [-0.1], [], y0=0.9)
wall("x", WZ0, 1, XW, XH, lambda x: WY + (WZ0 - WZ1) * 0.02, [])

# ---------------------------------------------------------------- the roof: the shed, the flat roof, the wing's

def roof_slab(x0, x1, z0, z1, under, mat_top="Roof"):
    """The roof's boards (warm wood, underneath: the ceiling inside, the soffit under the eaves) and its build-up
    (charcoal metal on top), between x0..x1, z0..z1, its soffit at under(x)."""
    solid("Soffit", [(x0, under(x0), z0), (x1, under(x1), z0), (x1, under(x1), z1), (x0, under(x0), z1)],
          [(x0, under(x0) + 0.05, z0), (x1, under(x1) + 0.05, z0), (x1, under(x1) + 0.05, z1), (x0, under(x0) + 0.05, z1)])
    solid(mat_top, [(x0, under(x0) + 0.05, z0), (x1, under(x1) + 0.05, z0), (x1, under(x1) + 0.05, z1), (x0, under(x0) + 0.05, z1)],
          [(x0, under(x0) + RT, z0), (x1, under(x1) + RT, z0), (x1, under(x1) + RT, z1), (x0, under(x0) + RT, z1)])


def fascia_x(x0, x1, z, under, out):
    """The thin charcoal metal edge along a roof's front or back edge (at z, facing out along z)."""
    solid("Steel", [(x0, under(x0) - 0.06, z), (x1, under(x1) - 0.06, z), (x1, under(x1) - 0.06, z + out * 0.06), (x0, under(x0) - 0.06, z + out * 0.06)],
          [(x0, under(x0) + RT + 0.06, z), (x1, under(x1) + RT + 0.06, z), (x1, under(x1) + RT + 0.06, z + out * 0.06), (x0, under(x0) + RT + 0.06, z + out * 0.06)])


def fascia_z(x, z0, z1, y, out):
    lohi("Steel", (min(x, x + out * 0.06), y - 0.06, z0), (max(x, x + out * 0.06), y + RT + 0.06, z1))


SX0, SX1 = XH - XHO, XL + TAIL   # the shed's ends
SZ0, SZ1 = -D - OB, D + OF       # its back and front edges
# the shed: the front strip all along, and the back strip over the low end (the tall volume has the rest)
for (xa, xb) in ((SX0, XL), (XL, SX1)):   # (in two: it kinks over the low end's wall, onto the tail)
    roof_slab(xa, xb, ZC, SZ1, ru)
    fascia_x(xa, xb, SZ1, ru, 1)
for (xa, xb) in ((XB, XL), (XL, SX1)):
    roof_slab(xa, xb, SZ0, ZC, ru)
    fascia_x(xa, xb, SZ0, ru, -1)
fascia_z(SX1, SZ0, SZ1, ru(SX1), 1)
fascia_z(SX0, ZC, SZ1, ru(SX0), -1)
# the flat roof over the tall volume
FX0, FX1, FZ0, FZ1 = XH - 0.6, XB + 0.6, -D - OB, ZC + 0.7


def flat(x):
    return H


roof_slab(FX0, FX1, FZ0, FZ1, flat)
fascia_x(FX0, FX1, FZ1, flat, 1)
fascia_x(FX0, FX1, FZ0, flat, -1)
fascia_z(FX0, FZ0, FZ1, H, -1)
fascia_z(FX1, FZ0, FZ1, H, 1)
# the wing's: nearly flat, falling a little to the back, deep at the front over the terrace
WRX0, WRZ0, WRZ1 = XW - 1.2, WZ0 - 0.6, D - 0.1


def wing_under(z):
    return WY + (z - WZ1) * 0.02


solid("Soffit", [(WRX0, wing_under(WRZ0), WRZ0), (XH, wing_under(WRZ0), WRZ0), (XH, wing_under(WRZ1), WRZ1), (WRX0, wing_under(WRZ1), WRZ1)],
      [(WRX0, wing_under(WRZ0) + 0.05, WRZ0), (XH, wing_under(WRZ0) + 0.05, WRZ0), (XH, wing_under(WRZ1) + 0.05, WRZ1), (WRX0, wing_under(WRZ1) + 0.05, WRZ1)])
solid("Roof", [(WRX0, wing_under(WRZ0) + 0.05, WRZ0), (XH, wing_under(WRZ0) + 0.05, WRZ0), (XH, wing_under(WRZ1) + 0.05, WRZ1), (WRX0, wing_under(WRZ1) + 0.05, WRZ1)],
      [(WRX0, wing_under(WRZ0) + RT, WRZ0), (XH, wing_under(WRZ0) + RT, WRZ0), (XH, wing_under(WRZ1) + RT, WRZ1), (WRX0, wing_under(WRZ1) + RT, WRZ1)])
lohi("Steel", (WRX0, wing_under(WRZ1) - 0.06, WRZ1), (XH, wing_under(WRZ1) + RT + 0.06, WRZ1 + 0.06))
lohi("Steel", (WRX0 - 0.06, wing_under(WRZ0) - 0.06, WRZ0), (WRX0, wing_under(WRZ1) + RT + 0.06, WRZ1))
lohi("Steel", (WRX0, wing_under(WRZ0) - 0.06, WRZ0 - 0.06), (XH, wing_under(WRZ0) + RT + 0.06, WRZ0))

# ---------------------------------------------------------------- the glulam: rafters, beams, columns

# rafters run up the slope (along x) under the shed's boards, every 1.25 m; their ends stand out past the high end
for z in [SZ0 + 0.35 + i * 1.25 for i in range(int((SZ1 - SZ0 - 0.5) / 1.25) + 1)]:
    z = min(z, SZ1 - 0.12)
    x0 = SX0 - 0.55 if z > ZC else XB + 0.05
    if z < ZC + 0.1 and z > ZC - 0.1:
        continue
    for (xa, xb) in ((x0, XL), (XL, SX1 - 0.04)):   # (in two: the slope kinks over the low end's wall)
        span("Glulam", (xa, ru(xa) - RD / 2, z), (xb, ru(xb) - RD / 2, z), RW, RD)
# the front edge's own rafter, deeper: the warm wood line under the charcoal edge
for (xa, xb) in ((SX0 - 0.55, XL), (XL, SX1 - 0.04)):
    span("Glulam", (xa, ru(xa) - 0.24, SZ1 - 0.1), (xb, ru(xb) - 0.24, SZ1 - 0.1), 0.2, 0.48)
# the tall volume's rafters, level, under its flat roof
for z in [FZ0 + 0.35 + i * 1.25 for i in range(int((FZ1 - FZ0 - 0.5) / 1.25) + 1)]:
    span("Glulam", (FX0 + 0.05, H - RD / 2, z), (FX1 - 0.05, H - RD / 2, z), RW, RD)
# beams along z at the frames (under the rafters), out to the front edge; posts under them at the glass and at ZC
for x in STATIONS:
    yb = ru(x) - RD   # the rafters' underside: the beam's top
    z0 = ZC - 0.3 if x < XB else SZ0 + 0.25
    span("Glulam", (x, yb - BD / 2, z0), (x, yb - BD / 2, SZ1 - 0.15), BW, BD)
    if x < XB:
        span("Glulam", (x, H - RD - BD / 2, -D + 0.1), (x, H - RD - BD / 2, ZC + 0.4), BW, BD)
    for z in (D - T - 0.3, ZC):
        top = yb - BD
        with inside():
            lohi("Glulam|up", (x - 0.18, 0.0, z - 0.18), (x + 0.18, top, z + 0.18))
        helper("BLK_column", (x - 0.2, 0.0, z - 0.2), (x + 0.2, top, z + 0.2))
# the clerestory's beam along ZC (carrying the shed's back edge and the glass above), level with the rafters' undersides
solid("Glulam", [(CB0, ru(CB0) - RD - BD, ZC - 0.16), (CB1, ru(CB1) - RD - BD, ZC - 0.16), (CB1, ru(CB1) - RD - BD, ZC + 0.16), (CB0, ru(CB0) - RD - BD, ZC + 0.16)],
      [(CB0, ru(CB0) + 0.05, ZC - 0.16), (CB1, ru(CB1) + 0.05, ZC - 0.16), (CB1, ru(CB1) + 0.05, ZC + 0.16), (CB0, ru(CB0) + 0.05, ZC + 0.16)])
# posts under the roof's tail, along the covered section's outer edge (on the terrace, clear of the side door's way
# out), carrying a steel beam under the rafters
PX = SX1 - 0.5
TAIL_POSTS = (-6.55, -2.4, 2.6, 6.1, 9.6)
for z in TAIL_POSTS:
    lohi("Glulam|up", (PX - 0.15, 0.0, z - 0.15), (PX + 0.15, ru(PX) - RD - 0.12, z + 0.15))
    helper("BLK_post", (PX - 0.17, 0.0, z - 0.17), (PX + 0.17, ru(PX) - RD - 0.12, z + 0.17))
    lohi("Concrete", (PX - 0.24, 0.0, z - 0.24), (PX + 0.24, 0.35, z + 0.24))
span("Steel", (PX, ru(PX) - RD - 0.06, SZ0 + 0.3), (PX, ru(PX) - RD - 0.06, SZ1 - 0.3), 0.2, 0.12)
# the wing's rafters run front to back, their tails out past its roof's front edge
for x in [WRX0 + 0.3 + i * 1.0 for i in range(int((XH - WRX0 - 0.3) / 1.0) + 1)]:
    span("Glulam", (x, wing_under(WRZ0 + 0.1) - RD / 2, WRZ0 + 0.1), (x, wing_under(WRZ1 + 0.55) - RD / 2, WRZ1 + 0.55), RW, RD)
span("Glulam", (WRX0 + 0.2, wing_under(WZ1) - RD - 0.2, WZ1 + 0.1), (XH - 0.1, wing_under(WZ1) - RD - 0.2, WZ1 + 0.1), 0.26, 0.4)
for x in (WRX0 + 0.35,):
    lohi("Glulam|up", (x - 0.15, 0.0, WRZ1 - 0.45), (x + 0.15, wing_under(WRZ1) - RD, WRZ1 - 0.15))
    helper("BLK_post", (x - 0.17, 0.0, WRZ1 - 0.47), (x + 0.17, 3.0, WRZ1 - 0.13))

# ---------------------------------------------------------------- the dark gabled block on the flat roof

GX0, GX1, GZ0, GZ1 = -7.0, 1.0, -6.0, -1.6
GY0 = H + RT
GY1 = GY0 + 1.1
GP = 1.35   # the gable's rise
lohi("Cladding", (GX0, GY0 - 0.1, GZ0), (GX1, GY1, GZ1))   # (its foot down in the roof: the snow's underside was level with it)
for z, s in ((GZ1, 1), (GZ0, -1)):
    solid("Cladding", [(GX0, GY1, z - 0.3), (GX1, GY1, z - 0.3), ((GX0 + GX1) / 2, GY1 + GP, z - 0.3)],
          [(GX0, GY1, z), (GX1, GY1, z), ((GX0 + GX1) / 2, GY1 + GP, z)])
for s in (-1, 1):
    xe = GX0 - 0.4 if s < 0 else GX1 + 0.4
    xm = (GX0 + GX1) / 2
    ye = GY1 - 0.4 * GP / ((GX1 - GX0) / 2)

    def gh(x, z, xe=xe, xm=xm, ye=ye):
        return ye + (GY1 + GP - ye) * (x - xe) / (xm - xe)

    xa, xb = (xe, xm) if s < 0 else (xm, xe)
    solid("Roof", [(xa, gh(xa, 0) + 0.0, GZ0 - 0.4), (xb, gh(xb, 0), GZ0 - 0.4), (xb, gh(xb, 0), GZ1 + 0.4), (xa, gh(xa, 0), GZ1 + 0.4)],
          [(xa, gh(xa, 0) + 0.22, GZ0 - 0.4), (xb, gh(xb, 0) + 0.22, GZ0 - 0.4), (xb, gh(xb, 0) + 0.22, GZ1 + 0.4), (xa, gh(xa, 0) + 0.22, GZ1 + 0.4)])
    lohi("Steel", (xe - 0.05, ye - 0.05, GZ0 - 0.4), (xe + 0.05, ye + 0.3, GZ1 + 0.4))
    sa, sb = (xe + 0.08, xm - 0.02) if s < 0 else (xm + 0.02, xe - 0.08)
    snow(sa, sb, GZ0 - 0.32, GZ1 + 0.32, lambda x, z, gh=gh: gh(x, z) + 0.22, 0.45, r=0.45, step=4.0)
lohi("Steel", ((GX0 + GX1) / 2 - 0.12, GY1 + GP + 0.15, GZ0 - 0.4), ((GX0 + GX1) / 2 + 0.12, GY1 + GP + 0.3, GZ1 + 0.4))

# ---------------------------------------------------------------- snow on the roofs

# thick, rounded, standing a hand's width proud of the charcoal edges (the photo's heavy loads)
snow(SX0 + 0.05, SX1 + 0.08, ZC + 0.25, SZ1 + 0.1, lambda x, z: ru(x) + RT, 0.62, r=0.7, xbreaks=(XL,))
snow(XB + 0.3, SX1 + 0.08, SZ0 - 0.08, ZC - 0.05, lambda x, z: ru(x) + RT, 0.62, r=0.7, xbreaks=(XL,))
snow(FX0 - 0.08, FX1 + 0.08, FZ0 - 0.08, FZ1 + 0.1, lambda x, z: H + RT, 0.7, r=0.75)
snow(WRX0 - 0.08, XH - 0.05, WRZ0 - 0.08, WRZ1 + 0.1, lambda x, z: wing_under(z) + RT, 0.55, r=0.6)
# banked against the walls where the hill comes up behind
for (x0, x1, z) in ((XH - 1.0, XL + TAIL - 0.6, -D - 0.6),):
    solid("Snow", [(x0, -1.6, z + 0.6), (x1, -1.6, z + 0.6), (x1, -1.6, z - 2.6), (x0, -1.6, z - 2.6)],
          [(x0 + 0.4, 0.1, z + 0.6), (x1 - 0.4, 0.1, z + 0.6), (x1 - 0.4, -0.3, z - 2.4), (x0 + 0.4, -0.3, z - 2.4)])

# ---------------------------------------------------------------- the terrace

# concrete across the front and round the low end (under the roof's tail), its outer walls down into the snow
TERR = [(XW - 1.2, SX1, D, ZT), (XW - 1.2, XH, WZ1, D), (XL, SX1, -D, D)]
for (x0, x1, z0, z1) in TERR:
    lohi("Concrete", (x0, -0.3, z0), (x1, 0.0, z1))
    helper("FLR_terrace", (x0, -0.3, z0), (x1, 0.0, z1))
lohi("Concrete", (XW - 1.2, -BASE, ZT - 0.3), (SX1, -0.3, ZT))           # the front wall
lohi("Concrete", (SX1 - 0.3, -BASE, -D), (SX1, -0.3, ZT))               # the low end's
lohi("Concrete", (XW - 1.2, -BASE, WZ1), (XW - 0.9, -0.3, ZT))          # the wing end's
lohi("Concrete", (XL, -BASE, -D), (SX1, -0.3, -D + 0.3))                 # the side terrace's back

# the wide steps down the front, toward the fire and the gate, and a narrower flight by the wing
STAIRS = [(-5.2, 4.8), (-15.2, -11.6)]   # (the main flight to the left of the door: straight down it, the way to the gate passes the fire pit)
for (x0, x1) in STAIRS:
    for k in range(1, NSTEP + 1):
        z0, z1 = ZT + (k - 1) * RUN, ZT + k * RUN
        lohi("Concrete", (x0, -BASE - 0.3, z0), (x1, -RISE * k, z1 + 0.01))
        lohi("Steel", (x0 + 0.02, -RISE * k - 0.004, z0 + 0.01), (x1 - 0.02, -RISE * k + 0.004, z0 + 0.06))   # a dark nosing
    helper("RMP_stairs", (x0, -RISE * NSTEP, ZT), (x1, 0.0, ZT + NSTEP * RUN))
    marker("STEP_%d" % int(round((x0 + x1) * 5)), ((x0 + x1) / 2, 0.0, ZT))
    # cheek walls either side, falling with the flight, a black handrail along each
    for xc, s in ((x0 - 0.15, -1), (x1 + 0.15, 1)):
        L = NSTEP * RUN
        solid("Concrete", [(xc - 0.15, -BASE, ZT), (xc + 0.15, -BASE, ZT), (xc + 0.15, -BASE, ZT + L), (xc - 0.15, -BASE, ZT + L)],
              [(xc - 0.15, 0.45, ZT), (xc + 0.15, 0.45, ZT), (xc + 0.15, 0.45 - RISE * NSTEP, ZT + L), (xc - 0.15, 0.45 - RISE * NSTEP, ZT + L)])
        span("Steel", (xc, 1.05, ZT - 0.3), (xc, 1.05 - RISE * NSTEP, ZT + L), 0.05)
        for t in (0.0, 0.5, 1.0):
            zz = ZT - 0.2 + (L + 0.2) * t
            lohi("Steel", (xc - 0.025, 0.45 - RISE * NSTEP * t, zz - 0.025), (xc + 0.025, 1.05 - RISE * NSTEP * t, zz + 0.025))
        for k in range(0, NSTEP, 2):   # (in pieces as it falls: the buried end blocks no one standing on the snow beside it)
            helper("BLK_cheek", (xc - 0.17, -BASE, ZT - 0.2 + k * RUN), (xc + 0.17, 1.1 - RISE * k, ZT + (k + 2) * RUN))
        snow(xc - 0.16, xc + 0.16, ZT + 0.02, ZT + L * 0.95, lambda x, z: 0.45 - RISE * NSTEP * (z - ZT) / L, 0.1, r=0.08, fine=False, step=6.0)

# snow banked against the terrace's walls, as in the photo (the game levels the ground under the lodge so that in
# front of the terrace it is GROUND below the floor, rising to GROUND_BACK under the house: CampLodge.LodgePad)
GROUND, GROUND_BACK = -1.12, -0.12


def ground_at(z):
    u = min(1.0, max(0.0, (z - D) / (ZT - D)))
    return GROUND_BACK + (GROUND - GROUND_BACK) * u * u * (3 - 2 * u)


def bank(axis, fixed, out, a0, a1, ground, rise=0.55, reach=2.2, step=1.0):
    """Snow banked against a wall (along x at z=fixed, or along z at x=fixed), out the way it faces, from a0 to a1:
    `rise` above the ground at the wall, falling away over `reach`, its ends sunk into the snow."""
    n = max(2, int((a1 - a0) / step) + 1)
    ds = [-0.05, 0.25, 0.6, 1.0, 1.45, reach]
    b = bmesh.new()
    rows = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        e = min(a - a0, a1 - a) / 1.2
        taper = 1.0 if e >= 1 else e * e * (3 - 2 * e)
        g = ground(a)
        row = []
        for d in ds:
            f = 0.5 * (1 + math.cos(math.pi * min(1.0, max(0.0, d) / reach)))
            y = g - 0.3 + (rise + 0.3) * f * taper
            p = (a, y, fixed + out * d) if axis == "x" else (fixed + out * d, y, a)
            row.append(b.verts.new(p))
        rows.append(row)
    for i in range(n):
        for j in range(len(ds) - 1):
            f = b.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]))
            f.normal_update()
            if f.normal.y < 0:
                f.normal_flip()
            f.smooth = True
    add(b, "Snow", Matrix.Identity(4))


for (x0, x1) in ((XW - 1.2, -15.5), (-11.3, -5.5), (5.1, SX1)):
    bank("x", ZT, 1, x0, x1, lambda a: GROUND)
bank("z", SX1, 1, 3.0, ZT, ground_at)
bank("z", XW - 1.2, -1, WZ1 + 0.5, ZT, ground_at)

# low concrete planters along the terrace's front edge between the flights and at its ends, snow heaped in them
PLANTERS = [(XW - 1.1, -15.4, ZT - 1.3, ZT), (-11.4, -5.4, ZT - 1.3, ZT), (5.2, 9.4, ZT - 1.3, ZT), (9.6, SX1 - 0.1, ZT - 1.3, ZT)]
for (x0, x1, z0, z1) in PLANTERS:
    lohi("Concrete", (x0, 0.0, z0), (x1, 0.5, z1))
    snow(x0 + 0.06, x1 - 0.06, z0 + 0.06, z1 - 0.06, lambda x, z: 0.5, 0.3, r=0.3, step=2.5, fine=False)
    helper("BLK_planter", (x0, 0.0, z0), (x1, 0.9, z1))
# railings (black steel, a snowy top rail) along the terrace's open sides
RAILS = [((XW + 0.05, WZ1 + 0.08), (XW - 1.05, WZ1 + 0.08)), ((XW - 1.05, WZ1 + 0.1), (XW - 1.05, ZT - 1.4)), ((SX1 - 0.15, -D + 0.2), (SX1 - 0.15, ZT - 1.4)), ((XL + 0.1, -D + 0.15), (SX1 - 0.2, -D + 0.15))]
for (a, b_) in RAILS:
    (xa, za), (xb, zb) = a, b_
    span("Steel", (xa, 1.05, za), (xb, 1.05, zb), 0.06)
    span("Steel", (xa, 0.15, za), (xb, 0.15, zb), 0.04)
    L = math.hypot(xb - xa, zb - za)
    n = max(1, int(L / 1.6))
    for i in range(n + 1):
        t = i / n
        x, z = xa + (xb - xa) * t, za + (zb - za) * t
        lohi("Steel", (x - 0.03, 0.0, z - 0.03), (x + 0.03, 1.05, z + 0.03))
    for i in range(int(L / 0.2)):
        t = (i + 0.5) / int(L / 0.2)
        x, z = xa + (xb - xa) * t, za + (zb - za) * t
        lohi("Steel", (x - 0.008, 0.15, z - 0.008), (x + 0.008, 1.05, z + 0.008))
    along_x = abs(xb - xa) > abs(zb - za)
    pillow(((xa + xb) / 2, 1.1, (za + zb) / 2), (L + 0.04 if along_x else 0.1, 0.07, 0.1 if along_x else L + 0.04), 0.02)
    helper("BLK_rail", (min(xa, xb) - 0.06, 0.0, min(za, zb) - 0.06), (max(xa, xb) + 0.06, 1.1, max(za, zb) + 0.06))

# ---------------------------------------------------------------- on the terrace

def picnic(x, z, along_z=True):
    """A picnic table: a wood top (0.78) and benches (0.44) on black steel A-frames."""
    L = 2.2
    if along_z:
        lohi("Glulam", (x - 0.4, 0.72, z - L / 2), (x + 0.4, 0.78, z + L / 2))
        for s in (-1, 1):
            lohi("Glulam", (x + s * 0.72 - 0.15, 0.39, z - L / 2), (x + s * 0.72 + 0.15, 0.44, z + L / 2))
        for zz in (z - L / 2 + 0.25, z + L / 2 - 0.25):
            for s in (-1, 1):
                span("Steel", (x + s * 0.85, 0.0, zz), (x + s * 0.12, 0.74, zz), 0.05)
            lohi("Steel", (x - 0.87, 0.35, zz - 0.025), (x + 0.87, 0.39, zz + 0.025))
        helper("BLK_table", (x - 0.9, 0.0, z - L / 2), (x + 0.9, 0.8, z + L / 2))
    else:
        lohi("Glulam", (x - L / 2, 0.72, z - 0.4), (x + L / 2, 0.78, z + 0.4))
        for s in (-1, 1):
            lohi("Glulam", (x - L / 2, 0.39, z + s * 0.72 - 0.15), (x + L / 2, 0.44, z + s * 0.72 + 0.15))
        for xx in (x - L / 2 + 0.25, x + L / 2 - 0.25):
            for s in (-1, 1):
                span("Steel", (xx, 0.0, z + s * 0.85), (xx, 0.74, z + s * 0.12), 0.05)
            lohi("Steel", (xx - 0.025, 0.35, z - 0.87), (xx + 0.025, 0.39, z + 0.87))
        helper("BLK_table", (x - L / 2, 0.0, z - 0.9), (x + L / 2, 0.8, z + 0.9))


def chair(x, z, yaw, mat):
    """A low slatted lounge chair (the photo's coloured Adirondacks), facing yaw degrees from +z."""
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def P(u, y, v):   # u across, v forward
        return (x + u * c + v * s, y, z - u * s + v * c)
    for k in range(5):
        v = -0.3 + k * 0.13
        span(mat, P(-0.32, 0.38 - k * 0.02, v), P(0.32, 0.38 - k * 0.02, v), 0.1, 0.03)   # the seat's slats
    for k in range(5):
        u = -0.26 + k * 0.13
        span(mat, P(u, 0.4, -0.38), P(u, 1.0, -0.62), 0.11, 0.03)                       # the back's
    for su in (-1, 1):
        span(mat, P(su * 0.38, 0.62, -0.35), P(su * 0.38, 0.62, 0.35), 0.13, 0.03)     # arms
        span(mat, P(su * 0.36, 0.0, 0.3), P(su * 0.36, 0.62, 0.3), 0.06)                 # legs
        span(mat, P(su * 0.36, 0.0, -0.4), P(su * 0.36, 0.4, -0.35), 0.06)
    xs = [P(u, 0, v) for u in (-0.42, 0.42) for v in (-0.65, 0.4)]
    helper("BLK_chair", (min(p[0] for p in xs), 0.0, min(p[2] for p in xs)), (max(p[0] for p in xs), 1.0, max(p[2] for p in xs)))


def rack(x0, x1, z, face, boards=True):
    """A black steel rack along x at z, skis and boards standing in it (leaning back from the face side)."""
    lohi("Steel", (x0, 0.0, z - 0.25), (x0 + 0.05, 1.1, z + 0.25))
    lohi("Steel", (x1 - 0.05, 0.0, z - 0.25), (x1, 1.1, z + 0.25))
    lohi("Steel", (x0, 0.18, z - 0.25), (x1, 0.24, z + 0.25))
    lohi("Steel", (x0, 1.0, z - 0.03), (x1, 1.06, z + 0.03))
    pillow(((x0 + x1) / 2, 1.09, z), (x1 - x0, 0.05, 0.08), 0.015)
    n = int((x1 - x0 - 0.2) / 0.24)
    for i in range(n):
        x = x0 + 0.15 + i * (x1 - x0 - 0.3) / max(1, n - 1)
        sw = "Swatch%d" % (1 + (i * 3) % 5)
        if boards and i % 3 != 1:
            box(sw, (x, 0.85, z + face * 0.04), (0.27, 1.5, 0.03), (face * -8, 0, 0))   # a board, nose up
        else:
            for s in (-1, 1):   # a pair of skis
                box(sw, (x + s * 0.045, 0.95, z + face * 0.05), (0.075, 1.75, 0.025), (face * -6, 0, 0))
    helper("BLK_rack", (x0, 0.0, z - 0.3), (x1, 1.7, z + 0.3))


def fire_table(x, z, L=2.2, W=1.0):
    """A long low concrete fire table, a black steel top round a bed of embers, a glass screen round the flames."""
    lohi("Concrete", (x - L / 2, 0.0, z - W / 2), (x + L / 2, 0.42, z + W / 2))
    lohi("Steel", (x - L / 2 - 0.04, 0.42, z - W / 2 - 0.04), (x + L / 2 + 0.04, 0.47, z + W / 2 + 0.04))
    lohi("Ember", (x - L / 2 + 0.35, 0.44, z - 0.14), (x + L / 2 - 0.35, 0.5, z + 0.14))
    for s in (-1, 1):
        pane("x", z + s * 0.24, x - L / 2 + 0.3, x + L / 2 - 0.3, 0.47, 0.75, 0.75, 0.012, "GlassIn")
    pane("z", x - L / 2 + 0.3, z - 0.24, z + 0.24, 0.47, 0.75, 0.75, 0.012, "GlassIn")
    pane("z", x + L / 2 - 0.3, z - 0.24, z + 0.24, 0.47, 0.75, 0.75, 0.012, "GlassIn")
    marker("FX_fire", (x, 0.5, z))
    marker("LIGHT_firetable", (x, 1.3, z))
    helper("BLK_firetable", (x - L / 2, 0.0, z - W / 2), (x + L / 2, 0.8, z + W / 2))


def bistro(x, z):
    cyl("Steel", (x, 0.0, z), 0.25, 0.03, 12)
    cyl("Steel", (x, 0.0, z), 0.03, 0.72, 6)
    cyl("Glulam", (x, 0.72, z), 0.38, 0.04, 16)
    helper("BLK_bistro", (x - 0.4, 0.0, z - 0.4), (x + 0.4, 0.8, z + 0.4))
    for a in (0, 180):
        cx, cz = x + 0.7 * math.cos(math.radians(a)), z + 0.7 * math.sin(math.radians(a))
        for dx, dz in ((-0.18, -0.18), (0.18, -0.18), (0.18, 0.18), (-0.18, 0.18)):
            lohi("Steel", (cx + dx - 0.015, 0.0, cz + dz - 0.015), (cx + dx + 0.015, 0.45, cz + dz + 0.015))
        lohi("Steel", (cx - 0.21, 0.45, cz - 0.21), (cx + 0.21, 0.48, cz + 0.21))
        bx = cx + (0.2 if a == 0 else -0.2)
        lohi("Steel", (bx - 0.015, 0.48, cz - 0.21), (bx + 0.015, 0.85, cz + 0.21))
        helper("BLK_bistrochair", (cx - 0.24, 0.0, cz - 0.24), (cx + 0.24, 0.85, cz + 0.24))


# the covered section at the low end, under the roof's tail: ski and board racks in two rows along the back, picnic
# tables in front of them and along the front, the side door's way out (z 3..4.9) clear between them, a way down the
# middle (x 14..15.4) from the racks to the front terrace; a lamp under the roof for the evening (CampLodge.cs: the
# people at the tables and the racks, Camp.LodgeFolkAt)
RACKS = ((11.5, 14.2, -6.2), (15.4, 18.0, -6.2), (11.5, 14.2, -3.3), (15.4, 18.0, -3.3))
TABLES = ((12.9, -0.2), (16.5, -0.2), (12.9, 6.6), (16.5, 6.6), (16.5, 9.2))
for (x0, x1, z) in RACKS:
    rack(x0, x1, z, 1)
for (x, z) in TABLES:
    picnic(x, z, along_z=False)
marker("LIGHT_porch", (14.8, ru(14.8) - RD - 0.25, 2.0))
# the front, by the low end: coloured chairs looking out
chair(11.0, 9.4, 10, "ArtA")
chair(13.0, 9.9, -15, "ArtB")
# the fire table in front of the tall glass, lounge chairs round it
fire_table(-8.8, 10.0)
for (x, z, yaw, m) in ((-10.9, 10.0, 90, "ArtB"), (-6.7, 10.0, -90, "ArtA")):
    chair(x, z, yaw, m)
# bistro tables by the wing
bistro(-13.5, 7.7)
bistro(-13.5, 10.5)
picnic(7.9, 10.2, along_z=False)
# a ski and board rack at the top of the steps, and the chalkboard sign
rack(10.0, 12.0, ZT - 1.65, 1)


def chalkboard(x, z, yaw):
    """An A-frame chalkboard (Resources/Camp/LodgeChalk.png on both faces)."""
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for side in (1, -1):
        lean = side * 12
        ox, oz = s * side * 0.17, c * side * 0.17
        box("Glulam", (x + ox, 0.62, z + oz), (0.66, 1.2, 0.035), (lean, yaw, 0))
        quad("Chalk", (x + ox * 1.12, 0.66, z + oz * 1.12), 0.56, 0.98, yaw + (0 if side > 0 else 180))
    pillow((x, 1.24, z), (0.62, 0.05, 0.1), 0.02)
    helper("BLK_chalk", (x - 0.4, 0.0, z - 0.35), (x + 0.4, 1.2, z + 0.35))


chalkboard(-4.9, ZT - 1.0, 25)

# ---------------------------------------------------------------- inside: three layouts (lodge.md), each its own file

# The shell above is Lodge.fbx. Each layout's furnishings, stations, lamps and walking are LodgeA.fbx, LodgeB.fbx and
# LodgeC.fbx (written at the end), and the game puts one of them in the shell (Camp.LodgeLayout, the LODGE setting).
# All three have the same things in them: the outfitters (the wardrobe: the closet), the board shop (its counter:
# the upgrades), the wax and tuning bench (the upgrades too), the missions board, the lounge and its fire, the
# hot-chocolate café (the hub session's station: Camp.LodgeCocoaAt) and plants and lamps; only where they are changes.
#   A  gallery hall: clothes up the tall back wall, the lounge and a hanging fire under the big glass
#   B  fireside centre: the lounge round a low see-through fire in the middle; the clothes a boutique at the glass end,
#      a clear way straight through it to the café; a path all round the fire
#   C  two levels: a mezzanine over the back for the clothes, a wide stair beside the door; the lounge below
SIGNS = 4   # rows of Resources/Camp/LodgeSigns.png (Tools/MakeLodgeSigns.swift)
MZ = -D + T   # the back wall's inside face
WX0, WX1, WZB, WZF = XW + T, XH, WZ0 + T, WZ1 - T   # the wing's inside faces


def sign(row, pos, w, h, yaw=0):
    """A sign off the atlas's row (0 outfitters, 1 board shop, 2 wax & tune, 3 the café's menu), facing +z turned by yaw."""
    b = bmesh.new()
    uv = b.loops.layers.uv.new("UVMap")
    vs = [b.verts.new(c) for c in ((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0))]
    f = b.faces.new(vs)
    v0, v1 = 1 - (row + 1) / SIGNS + 0.002, 1 - row / SIGNS - 0.002
    for l, (u, v) in zip(f.loops, ((1, v0), (0, v0), (0, v1), (1, v1))):
        l[uv].uv = (u, v)
    add(b, "Signs", mat4(pos, (w, h, 1), (0, yaw, 0)), full_uv=True)


def board(mat, x, y, z, yaw=0.0, lean=-8.0, h=1.5):
    """A snowboard standing nose up (its base at height y), leaning back from the way it faces."""
    box(mat, (x, y + h / 2, z), (0.28, h, 0.03), (lean, yaw, 0))


def plant(x, z, hgt=1.3, y=0.0):
    cyl("Concrete", (x, y, z), 0.3, 0.55, 14, r2=0.34)
    for k in range(5):
        sphere("Leaf", (x + 0.14 * math.cos(k * 2.3), y + 0.6 + hgt * 0.32 * (k + 1) / 5, z + 0.14 * math.sin(k * 2.3)), (0.62, 0.66, 0.62), 8)
    helper("BLK_plant", (x - 0.38, y, z - 0.38), (x + 0.38, y + 1.6, z + 0.38))


def sofa(x0, x1, z, face, depth=0.95, mat="Upholstery", y=0.0):
    """A low modular sofa along x from x0 to x1 at z, facing +z (face 1) or -z (face -1): seat, back, arms, cushions."""
    zb = z - face * (depth / 2 - 0.13)
    lohi(mat, (x0, y, z - depth / 2), (x1, y + 0.42, z + depth / 2))
    lohi(mat, (x0, y + 0.42, zb - 0.13), (x1, y + 0.85, zb + 0.13))
    for xa in (x0, x1 - 0.22):
        lohi(mat, (xa, y + 0.42, z - depth / 2), (xa + 0.22, y + 0.62, z + depth / 2))
    n = max(2, int((x1 - x0 - 0.44) / 0.75))
    za, zc = (zb + 0.13, z + depth / 2 - 0.05) if face > 0 else (z - depth / 2 + 0.05, zb - 0.13)
    w = (x1 - x0 - 0.44) / n
    for i in range(n):
        cx = x0 + 0.22 + w * (i + 0.5)
        lohi("Cushion", (cx - w / 2 + 0.02, y + 0.42, za), (cx + w / 2 - 0.02, y + 0.52, zc))
    helper("BLK_sofa", (x0, y, z - depth / 2), (x1, y + 0.9, z + depth / 2))


def armchair(x, z, yaw, mat="Hide", y=0.0):
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def P(u, h, v):
        return (x + u * c + v * s_, y + h, z - u * s_ + v * c)
    box(mat, P(0, 0.21, 0), (0.85, 0.42, 0.85), (0, yaw, 0))
    box(mat, P(0, 0.62, -0.33), (0.85, 0.42, 0.2), (0, yaw, 0))
    for su in (-1, 1):
        box(mat, P(su * 0.36, 0.55, 0.02), (0.14, 0.28, 0.8), (0, yaw, 0))
    box("Cushion", P(0, 0.47, 0.05), (0.6, 0.1, 0.55), (0, yaw, 0))
    helper("BLK_armchair", (x - 0.5, y, z - 0.5), (x + 0.5, y + 0.85, z + 0.5))


def low_table(x0, z0, x1, z1, y=0.0):
    lohi("Oak", (x0, y + 0.36, z0), (x1, y + 0.42, z1))
    lohi("Steel", (x0 + 0.1, y, z0 + 0.05), (x1 - 0.1, y + 0.36, z1 - 0.05))
    helper("BLK_lowtable", (x0 - 0.05, y, z0 - 0.05), (x1 + 0.05, y + 0.45, z1 + 0.05))


def stool(x, z, h=0.75):
    cyl("Steel", (x, 0.0, z), 0.2, 0.02, 10)
    cyl("Steel", (x, 0.0, z), 0.025, h, 6)
    cyl("Glulam", (x, h, z), 0.19, 0.05, 12)
    helper("BLK_stool", (x - 0.22, 0.0, z - 0.22), (x + 0.22, h + 0.05, z + 0.22))


def helmet(x, y, z, mat):
    sphere(mat, (x, y + 0.13, z), (0.27, 0.24, 0.3), 10)
    box("Charcoal", (x, y + 0.11, z + 0.12), (0.2, 0.07, 0.04))   # goggles on it


def pendants(pts, ceiling):
    """Globe lamps hung from the ceiling (ceiling(x): the height they hang from)."""
    for (px, py, pz) in pts:
        span("Steel", (px, py + 0.2, pz), (px, ceiling(px), pz), 0.015)
        sphere("LED", (px, py, pz), (0.34, 0.34, 0.34), 12)


def under_shed(x):
    return ru(x) - RD


def under_flat(x):
    return H - RD


def gear_wall(x0, x1, z=MZ, y0=0.0, levels=(2.7, 3.7, 4.7, 5.7), n=12, top=None):
    """A gallery of gear up the back wall: black steel uprights, lit oak shelves of helmets, folded jackets, boots, goggles."""
    xs = [x0 + (x1 - x0) * i / max(1, round((x1 - x0) / 1.7)) for i in range(int(max(1, round((x1 - x0) / 1.7))) + 1)]
    top = y0 + (levels[-1] + 0.5 if top is None else top)
    for x in xs:
        lohi("Steel", (x - 0.03, y0, z), (x + 0.03, top, z + 0.06))
    for k, yl in enumerate(levels):
        y = y0 + yl
        lohi("Oak", (x0, y - 0.04, z + 0.005), (x1, y, z + 0.42))
        lohi("Steel", (x0, y - 0.08, z + 0.42), (x1, y, z + 0.45))   # (the lip in front of the oak: through it, their tops fought)
        lohi("LED", (x0 + 0.05, y - 0.1, z + 0.355), (x1 - 0.05, y - 0.085, z + 0.395))
        for i in range(n):
            x = x0 + 0.35 + i * (x1 - x0 - 0.7) / (n - 1)
            kind = (i + k) % 4
            if kind == 0:
                helmet(x, y, z + 0.22, ("White", "Red", "Navy", "ArtA", "Charcoal")[(i + k) % 5])
            elif kind == 1:
                for j in range(3):
                    box(("Red", "Navy", "ArtB", "Fabric", "ArtA")[(i + j + k) % 5], (x, y + 0.06 + j * 0.11, z + 0.22), (0.38, 0.1, 0.3))
            elif kind == 2:
                box("Charcoal", (x - 0.08, y + 0.12, z + 0.22), (0.13, 0.24, 0.3))
                box("Charcoal", (x + 0.08, y + 0.12, z + 0.22), (0.13, 0.24, 0.3))
            else:
                for j in range(3):
                    box("Steel", (x - 0.12 + j * 0.12, y + 0.08, z + 0.22), (0.02, 0.16, 0.02))
                    box(("ArtA", "Navy", "Red")[j], (x - 0.12 + j * 0.12, y + 0.18, z + 0.24), (0.11, 0.05, 0.03))


JACKETS = ("Red", "Fabric", "Navy", "ArtA", "Charcoal", "ArtB", "White", "Red", "Navy", "ArtA", "Fabric")


def rail_x(x0, x1, z, y0=0.0, two_sided=False, blk=True):
    """A clothes rail along x at z (black steel on two posts), jackets hanging on it."""
    lohi("Steel", (x0, y0 + 2.05 - 0.3 * two_sided, z - 0.03), (x1, y0 + 2.09 - 0.3 * two_sided, z + 0.03))
    for x in (x0, x1):
        lohi("Steel", (x - 0.02, y0, z - 0.02), (x + 0.02, y0 + 2.09 - 0.3 * two_sided, z + 0.02))
        lohi("Steel", (x - 0.02, y0, z - 0.3), (x + 0.02, y0 + 0.04, z + 0.3))
    n = int((x1 - x0) / 0.45)
    for k in range(n):
        box(JACKETS[k % len(JACKETS)], (x0 + 0.22 + k * (x1 - x0 - 0.44) / max(1, n - 1), y0 + 1.5 - 0.3 * two_sided, z), (0.4, 0.95, 0.12))
    if blk:
        helper("BLK_rail", (x0 - 0.05, y0, z - 0.32), (x1 + 0.05, y0 + 2.1, z + 0.32))


def display_table(tx, tz, y0=0.0, along_x=True):
    w, d = (0.8, 0.4) if along_x else (0.4, 0.8)
    lohi("Oak", (tx - w, y0 + 0.72, tz - d), (tx + w, y0 + 0.78, tz + d))
    lohi("Steel", (tx - w + 0.05, y0, tz - d + 0.05), (tx + w - 0.05, y0 + 0.72, tz + d - 0.05))
    for j in range(4):
        o = -0.55 + j * 0.37
        box(("Navy", "Red", "Fabric", "ArtB")[j], (tx + (o if along_x else 0), y0 + 0.84, tz + (0 if along_x else o)), (0.3, 0.12, 0.34) if along_x else (0.34, 0.12, 0.3))
    helper("BLK_table", (tx - w - 0.02, y0, tz - d - 0.02), (tx + w + 0.02, y0 + 0.9, tz + d + 0.02))


def till(x0, z0, x1, z1, y0=0.0):
    lohi("Oak", (x0, y0, z0), (x1, y0 + 1.0, z1))
    lohi("Concrete", (x0 - 0.05, y0 + 1.0, z0 - 0.05), (x1 + 0.05, y0 + 1.05, z1 + 0.05))
    box("Charcoal", ((x0 + x1) / 2, y0 + 1.15, (z0 + z1) / 2), (0.35, 0.22, 0.25))
    helper("BLK_till", (x0 - 0.05, y0, z0 - 0.05), (x1 + 0.05, y0 + 1.2, z1 + 0.05))


def mannequin(mx, mz, col, y0=0.0):
    cyl("Steel", (mx, y0, mz), 0.2, 0.03, 10)
    cyl("Steel", (mx, y0, mz), 0.02, 0.9, 6)
    box(col, (mx, y0 + 1.25, mz), (0.42, 0.62, 0.24))
    box("Charcoal", (mx, y0 + 0.82, mz), (0.36, 0.26, 0.22))
    sphere("White", (mx, y0 + 1.72, mz), (0.24, 0.26, 0.26), 10)
    helper("BLK_mannequin", (mx - 0.28, y0, mz - 0.22), (mx + 0.28, y0 + 1.85, mz + 0.22))


def mirror(z0, z1, y0=0.0):
    """A long mirror on the high end's wall (inside face), between z0 and z1."""
    lohi("Steel", (XH + T, y0, z0 - 0.05), (XH + T + 0.05, y0 + 2.2, z1 + 0.05))
    pane("z", XH + T + 0.07, z0, z1, y0 + 0.05, y0 + 2.15, y0 + 2.15, 0.02, "Mirror")
    helper("BLK_mirror", (XH + T, y0, z0 - 0.05), (XH + T + 0.1, y0 + 2.2, z1 + 0.05))


def hanging_sign(row, x, y, z, w, h, ceiling, yaw=0):
    """A sign hung on two cords (from ceiling(x)), facing +z turned by yaw, and the same on its back."""
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for u in (-w / 2 + 0.15, w / 2 - 0.15):
        px, pz = x + u * c, z - u * s_
        span("Steel", (px, y + h / 2, pz), (px, ceiling(px), pz), 0.012)
    box("Steel", (x - 0.03 * s_, y, z - 0.03 * c), (w + 0.1, h + 0.1, 0.03), (0, yaw, 0))
    sign(row, (x, y, z), w, h, yaw)
    sign(row, (x - 0.06 * s_, y, z - 0.06 * c), w, h, yaw + 180)   # (and on its back: hung in the open, seen both ways)


def missions_board(mx, w=3.2):
    """The missions board on the back wall, facing the door across the hall."""
    hw = w / 2
    lohi("Steel", (mx - hw, 0.7, MZ), (mx + hw, 2.75, MZ + 0.06))
    lohi("Oak", (mx - hw + 0.1, 0.8, MZ + 0.06), (mx + hw - 0.1, 2.65, MZ + 0.09))
    sp = min(0.95, (w - 0.4) / 3)
    for i, x in enumerate((mx - sp, mx, mx + sp)):
        quad("Note%d" % i, (x, 1.62, MZ + 0.1), sp - 0.25 if sp < 0.95 else 0.7, 0.88)
    quad("SignMissions", (mx, 3.05, MZ + 0.05), min(2.8, w), min(2.8, w) / 4)
    lohi("LED", (mx - hw + 0.1, 2.72, MZ + 0.08), (mx + hw - 0.1, 2.74, MZ + 0.12))
    helper("TAP_Missions", (mx - hw - 0.1, 0.5, MZ), (mx + hw + 0.1, 3.5, MZ + 0.6))
    helper("BLK_missions", (mx - hw, 0.0, MZ), (mx + hw, 2.8, MZ + 0.15))
    marker("SPOT_Missions", (mx, 0.0, MZ + 1.6))


def board_shop(back_x0=4.9, counter_z=-1.6):
    """Boards on the back wall from back_x0 and down the low end's wall, bindings under the window, a board tree, and
    the counter facing the door with a glass case of upgrades (it opens the upgrades)."""
    nb = int((7.6 - back_x0 - 0.2) / 0.4) + 1
    for i in range(nb):
        board("Swatch%d" % (1 + (i * 2) % 5), back_x0 + i * 0.4, 0.12, MZ + 0.1, lean=-7)
    lohi("Glulam", (back_x0 - 0.2, 0.0, MZ), (7.6, 0.12, MZ + 0.3))
    helper("BLK_boards", (back_x0 - 0.2, 0.0, MZ), (7.6, 1.7, MZ + 0.35))
    for i in range(12):
        board("Swatch%d" % (1 + (i * 3) % 5), XL - T - 0.1, 0.12, -6.3 + i * 0.42, yaw=-90, lean=-7)
    lohi("Glulam", (XL - T - 0.3, 0.0, -6.55), (XL - T, 0.12, -1.5))
    helper("BLK_boards", (XL - T - 0.35, 0.0, -6.6), (XL - T, 1.7, -1.5))
    lohi("Oak", (7.8, 0.9, MZ), (10.2, 0.94, MZ + 0.35))
    lohi("Steel", (7.8, 0.0, MZ), (10.2, 0.9, MZ + 0.35))
    for i in range(6):
        box(("Charcoal", "Red", "Charcoal", "Navy", "Charcoal", "ArtA")[i], (8.0 + i * 0.4, 1.02, MZ + 0.18), (0.22, 0.16, 0.3))
    helper("BLK_shelf", (7.8, 0.0, MZ), (10.2, 1.1, MZ + 0.4))
    TX, TZ = 6.3, -4.1
    cyl("Steel", (TX, 0.0, TZ), 0.55, 0.08, 16)
    cyl("Steel", (TX, 0.0, TZ), 0.05, 1.6, 8)
    for k in range(6):
        a = k * math.pi / 3
        board("Swatch%d" % (1 + k % 5), TX + 0.42 * math.cos(a), 0.08, TZ + 0.42 * math.sin(a), yaw=90 - math.degrees(a), lean=-10, h=1.45)
    helper("BLK_tree", (TX - 0.65, 0.0, TZ - 0.65), (TX + 0.65, 1.6, TZ + 0.65))
    CZ = counter_z
    lohi("Oak", (5.8, 0.0, CZ - 0.35), (8.2, 1.0, CZ + 0.35))
    lohi("Steel", (5.75, 1.0, CZ - 0.4), (8.25, 1.05, CZ + 0.4))
    pane("x", CZ + 0.36, 5.9, 8.1, 0.25, 0.9, 0.9, 0.02, "GlassIn")
    for i in range(5):
        box(("Leaf", "ArtA", "Red", "Navy", "Cream")[i], (6.2 + i * 0.4, 0.55, CZ + 0.1), (0.18, 0.22, 0.18))
    box("Charcoal", (7.7, 1.17, CZ - 0.1), (0.35, 0.22, 0.25))
    sign(1, (7.0, 3.35, MZ + 0.05), 2.4, 0.6)
    helper("TAP_Upgrades", (5.7, 0.0, CZ - 0.45), (8.3, 2.2, CZ + 0.45))
    helper("BLK_counter", (5.75, 0.0, CZ - 0.4), (8.25, 1.2, CZ + 0.4))
    marker("SPOT_Upgrades", (7.0, 0.0, CZ + 1.15))


def tuning_bench():
    """The wax and tuning bench by the side door (the racks are just outside it): it opens the upgrades too."""
    BX0, BX1, BZ0, BZ1 = 8.95, 10.6, 5.85, 6.62
    lohi("Steel", (BX0, 0.0, BZ0), (BX1, 0.88, BZ1))
    lohi("Oak", (BX0 - 0.05, 0.88, BZ0 - 0.05), (BX1, 0.94, BZ1))
    box("BoardPaint", (9.75, 1.0, (BZ0 + BZ1) / 2), (1.45, 0.03, 0.3))
    for s in (-1, 1):
        cyl("BoardPaint", (9.75 + s * 0.72, 0.985, (BZ0 + BZ1) / 2), 0.15, 0.03, 12)
        lohi("Steel", (9.75 + s * 0.55 - 0.05, 0.94, (BZ0 + BZ1) / 2 - 0.18), (9.75 + s * 0.55 + 0.05, 1.08, (BZ0 + BZ1) / 2 + 0.18))
    lohi("Steel", (9.1, 0.94, BZ0 + 0.05), (9.4, 1.04, BZ0 + 0.2))
    for k in range(4):
        lohi("Steel", (10.0 + k * 0.12, 0.94, BZ0 + 0.06), (10.08 + k * 0.12, 0.96, BZ0 + 0.3))
    lohi("Oak", (XL - T - 0.25, 1.4, 5.0), (XL - T, 1.44, 6.6))
    for k in range(6):
        box(("ArtA", "Red", "Navy", "Leaf", "Cream", "ArtB")[k], (XL - T - 0.12, 1.5, 5.15 + k * 0.24), (0.12, 0.1, 0.16))
    sign(2, (9.75, 3.08, D - T - 0.03), 1.45, 0.36, 180)
    helper("TAP_Upgrades_2", (BX0 - 0.1, 0.0, BZ0 - 0.4), (BX1, 2.0, D - T))
    helper("BLK_bench", (BX0 - 0.05, 0.0, BZ0 - 0.05), (BX1, 1.1, D - T))
    helper("BLK_wax", (XL - T - 0.27, 1.3, 5.0), (XL - T, 1.7, 6.6))
    marker("SPOT_Upgrades_2", (9.75, 0.0, BZ0 - 0.75))


def cafe():
    """The hot chocolate café in the wing: a counter along its glass with stools, two tables, mugs on shelves, the
    menu on the back wall over where the hot-chocolate station goes (Camp.LodgeCocoaAt, kept clear for it)."""
    lohi("CounterTop", (WX0 + 0.15, 1.02, WZF - 0.5), (WX1 - 0.35, 1.07, WZF))   # (dark: walnut-stained oak)
    lohi("Steel", (WX0 + 0.2, 0.0, WZF - 0.12), (WX1 - 0.4, 1.02, WZF - 0.04))
    helper("BLK_bar", (WX0 + 0.15, 0.0, WZF - 0.52), (WX1 - 0.35, 1.1, WZF))
    for i in range(5):
        stool(WX0 + 0.75 + i * 0.85, WZF - 0.95)
    bistro(-14.4, 0.7)
    bistro(-12.6, -0.9)
    sign(3, (-13.5, 2.6, WZB + 0.04), 3.0, 0.75)
    for (sx0, sx1) in ((WX0 + 0.1, WX0 + 1.0), (WX1 - 1.35, WX1 - 0.4)):
        for y in (1.55, 2.05):
            lohi("Oak", (sx0, y, WZB), (sx1, y + 0.03, WZB + 0.22))
            for k in range(4):
                cyl("Cream", (sx0 + 0.13 + k * 0.22, y + 0.03, WZB + 0.11), 0.05, 0.1, 8)
    for i in range(3):
        px = WX0 + 0.9 + i * 1.5
        span("Steel", (px, 2.4, WZF - 0.25), (px, wing_under(WZF) - 0.05, WZF - 0.25), 0.012)
        sphere("LED", (px, 2.3, WZF - 0.25), (0.24, 0.24, 0.24), 10)
    plant(WX0 + 0.45, -3.0, 1.0)
    marker("LIGHT_wing", (-13.5, 3.2, 0.5))


def hanging_fire(fx, fz, ceiling):
    """A black steel bowl on a hearth plate, open under a hood that hangs on three rods, its flue to the ceiling."""
    cyl("Steel", (fx, 0.0, fz), 0.78, 0.06, 24)
    cyl("Steel", (fx, 0.75, fz), 0.32, 0.55, 20, r2=0.62)
    cyl("Ember", (fx, 1.05, fz), 0.5, 0.06, 16)
    for k in range(3):
        a = k * 2 * math.pi / 3
        span("Steel", (fx + 0.55 * math.cos(a), 1.28, fz + 0.55 * math.sin(a)), (fx + 0.5 * math.cos(a), 1.82, fz + 0.5 * math.sin(a)), 0.03)
    cyl("Steel", (fx, 1.8, fz), 0.62, 0.62, 20, r2=0.16)
    cyl("Steel", (fx, 2.4, fz), 0.12, ceiling(fx) - 2.4, 10)
    marker("FX_fire", (fx, 1.1, fz))
    helper("BLK_fire", (fx - 0.82, 0.0, fz - 0.82), (fx + 0.82, 2.45, fz + 0.82))


def open_fire(fx, fz, L=2.4, W=1.0, ceiling=under_flat):
    """A low see-through fire: a long concrete hearth at knee height, glass all round the flames, a slim black hood
    hung high above it on its flue. You see across it from any side and walk round it. The hearth stacked stone."""
    lohi("Hearth", (fx - L / 2, 0.0, fz - W / 2), (fx + L / 2, 0.45, fz + W / 2))
    lohi("Steel", (fx - L / 2 - 0.04, 0.45, fz - W / 2 - 0.04), (fx + L / 2 + 0.04, 0.5, fz + W / 2 + 0.04))
    lohi("Ember", (fx - L / 2 + 0.3, 0.48, fz - 0.16), (fx + L / 2 - 0.3, 0.54, fz + 0.16))
    for s in (-1, 1):
        pane("x", fz + s * 0.3, fx - L / 2 + 0.2, fx + L / 2 - 0.2, 0.5, 1.0, 1.0, 0.012, "GlassIn")
        pane("z", fx + s * (L / 2 - 0.2), fz - 0.3, fz + 0.3, 0.5, 1.0, 1.0, 0.012, "GlassIn")
    lohi("Steel", (fx - L / 2 + 0.1, 2.6, fz - 0.42), (fx + L / 2 - 0.1, 2.75, fz + 0.42))   # the hood, a thin black slab
    solid("Steel", [(fx - L / 2 + 0.1, 2.75, fz - 0.42), (fx + L / 2 - 0.1, 2.75, fz - 0.42), (fx + L / 2 - 0.1, 2.75, fz + 0.42), (fx - L / 2 + 0.1, 2.75, fz + 0.42)],
          [(fx - 0.2, 3.3, fz - 0.15), (fx + 0.2, 3.3, fz - 0.15), (fx + 0.2, 3.3, fz + 0.15), (fx - 0.2, 3.3, fz + 0.15)])
    cyl("Steel", (fx, 3.3, fz), 0.13, ceiling(fx) - 3.26, 10)   # (up into the beam: its top was level with the beam's)
    for k in range(3):
        marker("FX_fire", (fx - 0.7 + k * 0.7, 0.55, fz))
    helper("BLK_fire", (fx - L / 2 - 0.05, 0.0, fz - W / 2 - 0.05), (fx + L / 2 + 0.05, 1.05, fz + W / 2 + 0.05))


def welcome_mat():
    lohi("Runner", (3.2, 0.0, 4.9), (5.8, 0.012, 6.6))


# ---- A: the gallery hall

def layout_A():
    # the outfitters under the tall volume: a gallery of gear up the back wall, the jackets and the closet below
    gear_wall(-10.3, -1.8)
    RX = -6.4
    rail_x(RX - 3.0, RX + 1.0, MZ + 0.38, blk=False)
    for i in range(5):
        board("Swatch%d" % (i + 1), RX + 1.6 + i * 0.42, 0.0, MZ + 0.08, lean=-6)
    mirror(-6.4, -5.0)
    helper("TAP_Style", (RX - 4.4, 0.0, MZ), (RX + 3.7, 2.6, MZ + 1.2))
    helper("BLK_rack", (RX - 3.1, 0.0, MZ), (RX + 3.7, 2.2, MZ + 0.55))
    marker("SPOT_Style", (RX, 0.0, MZ + 2.0))
    display_table(-8.9, -2.3)
    display_table(-5.6, -2.2)
    till(-3.2, -3.5, -1.9, -2.8)
    mannequin(-9.8, -1.2, "Red"); mannequin(-9.0, -1.4, "Navy")
    hanging_sign(0, -6.0, 5.95, -0.3, 3.8, 0.95, lambda x: ru(x) - RD - BD)
    missions_board(2.0)
    board_shop()
    tuning_bench()
    # the lounge under the tall glass: the hanging fire, a long sofa and two armchairs round it, a rug, a low table
    lohi("Rug", (-9.2, 0.0, 1.85), (-3.6, 0.015, 5.9))
    hanging_fire(-6.4, 3.9, under_shed)
    marker("LIGHT_fire", (-6.4, 1.7, 3.0))
    sofa(-8.6, -4.2, 2.25, 1)
    armchair(-8.75, 4.6, 90)
    armchair(-4.05, 4.6, -90)
    low_table(-7.2, 5.55, -5.6, 6.15)
    plant(-10.2, 6.1); plant(-2.9, 6.15, 1.1); plant(1.6, 6.15, 1.5)
    pendants(((-9.0, 4.4, 3.2), (-8.2, 3.9, 5.2), (-4.6, 4.2, 3.0), (-3.8, 4.7, 5.3), (-5.1, 5.2, 5.6)), under_shed)
    welcome_mat()
    cafe()
    marker("LIGHT_hall", (-4.5, 6.0, -3.0))
    marker("LIGHT_low", (8.0, 2.9, 1.5))


# ---- B: the fireside centre

def layout_B():
    # the lounge in the middle under the clerestory, round a low fire you see through, a clear walk all round it
    FX, FZ = -1.8, -3.2
    lohi("Rug", (-5.2, 0.0, -6.3), (1.6, 0.015, -0.2))
    open_fire(FX, FZ)
    marker("LIGHT_fire", (FX, 1.4, FZ + 1.2))
    sofa(-4.0, 0.4, -5.55, 1)            # behind the fire, facing it and the glass beyond
    sofa(-4.0, 0.4, -0.95, -1)           # in front, facing it
    armchair(-5.5, -3.2, 90)
    armchair(1.9, -3.2, -90)
    for (tx, tz) in ((-4.6, -5.7), (1.0, -0.6)):   # little side tables, at the sofas' ends (the way round the fire stays clear)
        cyl("Oak", (tx, 0.5, tz), 0.25, 0.04, 14); cyl("Steel", (tx, 0.0, tz), 0.03, 0.5, 6)
        helper("BLK_sidetable", (tx - 0.27, 0.0, tz - 0.27), (tx + 0.27, 0.55, tz + 0.27))
    pendants(((-3.6, 4.6, -4.6), (-0.2, 4.2, -4.8), (-2.2, 5.0, -1.4), (0.6, 4.8, -2.0)), under_flat)
    missions_board(4.0, w=1.9)
    # the outfitters at the high end, a boutique behind the tall glass: the back half (the gear wall, the jackets and
    # the closet), the front half (rails and mannequins in the window: the storefront), and a clear way straight
    # through between them from the hall to the café
    gear_wall(-10.4, -6.9, n=7)
    rail_x(-10.2, -7.0, MZ + 0.38, blk=False)
    helper("BLK_rack", (-10.3, 0.0, MZ), (-6.9, 2.2, MZ + 0.55))
    mirror(-6.4, -5.0)
    helper("TAP_Style", (-10.7, 0.0, MZ), (-6.8, 2.6, MZ + 1.2))
    marker("SPOT_Style", (-8.6, 0.0, MZ + 2.0))
    display_table(-8.6, -2.3)
    lohi("Runner", (-10.9, 0.0, -0.4), (-6.5, 0.012, 1.9))          # the runner: the way through to the café
    rail_x(-10.1, -7.2, 3.4, two_sided=True)
    rail_x(-10.2, -7.6, 5.75)
    mannequin(-9.3, 6.35, "Red"); mannequin(-8.3, 6.35, "ArtB"); mannequin(-7.3, 6.35, "Navy")
    till(-7.3, 2.6, -6.6, 3.9)
    hanging_sign(0, -6.55, 4.9, 0.75, 3.0, 0.75, lambda x: ru(x) - RD, yaw=90)
    plant(-2.9, 6.15, 1.1); plant(1.6, 6.15, 1.5); plant(-6.0, -6.1, 1.2)
    board_shop(back_x0=5.3)
    tuning_bench()
    welcome_mat()
    cafe()
    marker("LIGHT_hall", (-1.8, 7.5, -3.2))
    marker("LIGHT_low", (8.0, 2.9, 1.5))


# ---- C: two levels

MY = 3.8                                    # the mezzanine's floor (3.5 m clear under it)
MX0, MX1, MZ0, MZ1 = XH + T, 2.2, MZ, -1.8  # its extent
NS, TREAD = 22, 0.28                        # the stair's steps (17 cm risers) and their depth
CSX0, CSX1, CSZT = 0.0, 2.2, -1.8              # the stair: x0..x1, its top at the mezzanine's front edge
CSZB = CSZT + NS * TREAD                      # its foot (1.8 m clear between it and the front's columns)


def layout_C():
    # the mezzanine: concrete with an oak floor, a black edge and a warm strip under it, slim posts, glass rails
    lohi("Concrete", (MX0, MY - 0.3, MZ0), (MX1, MY - 0.02, MZ1))
    lohi("FloorBoards", (MX0, MY - 0.02, MZ0), (MX1, MY, MZ1))
    lohi("Steel", (MX0, MY - 0.32, MZ1 - 0.02), (MX1, MY + 0.02, MZ1 + 0.04))
    lohi("Steel", (MX1 - 0.02, MY - 0.32, MZ0), (MX1 + 0.04, MY + 0.02, MZ1))
    lohi("LED", (MX0 + 0.2, MY - 0.34, MZ1 - 0.1), (CSX0 - 0.1, MY - 0.32, MZ1 - 0.05))
    for x in (-8.6, -4.6, -1.0):
        lohi("Steel", (x - 0.07, 0.0, MZ1 - 0.2), (x + 0.07, MY - 0.3, MZ1 - 0.06))
        helper("BLK_post", (x - 0.1, 0.0, MZ1 - 0.23), (x + 0.1, MY - 0.3, MZ1 - 0.03))
    for (px, pz) in ((-9.0, -4.6), (-6.0, -4.0), (-3.0, -4.6), (-0.4, -3.6)):   # downlights under the slab
        cyl("LED", (px, MY - 0.33, pz), 0.12, 0.02, 12)
    helper("FLR_mezz", (MX0, MY - 0.3, MZ0), (MX1, MY, MZ1))
    helper("CAM_mezzslab", (MX0, MY - 0.32, MZ0), (MX1 + 0.04, MY - 0.01, MZ1 + 0.04))
    # glass rails: along its front (left of the stair) and down its open side over the hall
    pane("x", MZ1 + 0.02, MX0, CSX0, MY, MY + 1.0, MY + 1.0, 0.02, "GlassIn")
    lohi("Steel", (MX0, MY + 1.0, MZ1 - 0.01), (CSX0, MY + 1.05, MZ1 + 0.05))
    helper("BLK_mezzrail", (MX0, MY, MZ1 - 0.05), (CSX0 + 0.02, MY + 1.1, MZ1 + 0.08))
    pane("z", MX1 + 0.02, MZ0, MZ1 - 0.005, MY, MY + 1.0, MY + 1.0, 0.02, "GlassIn")   # (just short of the edge's corner)
    lohi("Steel", (MX1 - 0.01, MY + 1.0, MZ0), (MX1 + 0.05, MY + 1.05, MZ1))
    helper("BLK_mezzrail", (MX1 - 0.05, MY, MZ0), (MX1 + 0.08, MY + 1.1, MZ1))
    # the stair: wide oak treads on black steel plates either side, rising beside the door to the mezzanine's front
    run = (CSZB - CSZT) / NS
    for i in range(NS):
        yt = MY * (i + 1) / (NS + 0.0)
        zt = CSZB - (i + 0.5) * run
        lohi("Oak", (CSX0 + 0.06, yt - 0.06, zt - run / 2 - 0.02), (CSX1 - 0.06, yt, zt + run / 2 + 0.02))
        lohi("LED", (CSX0 + 0.1, yt - 0.075, zt + run / 2 - 0.04), (CSX1 - 0.1, yt - 0.065, zt + run / 2))
    for x in (CSX0, CSX1):
        solid("Steel", [(x - 0.03, 0.0, CSZB + 0.05), (x + 0.03, 0.0, CSZB + 0.05), (x + 0.03, MY - 0.35, CSZT), (x - 0.03, MY - 0.35, CSZT)],
              [(x - 0.03, 0.25, CSZB + 0.05), (x + 0.03, 0.25, CSZB + 0.05), (x + 0.03, MY + 0.02, CSZT), (x - 0.03, MY + 0.02, CSZT)])
        solid("GlassIn", [(x - 0.01, 0.25, CSZB), (x + 0.01, 0.25, CSZB), (x + 0.01, MY, CSZT + 0.005), (x - 0.01, MY, CSZT + 0.005)],
              [(x - 0.01, 1.2, CSZB), (x + 0.01, 1.2, CSZB), (x + 0.01, MY + 1.0, CSZT + 0.005), (x - 0.01, MY + 1.0, CSZT + 0.005)])   # (just short of the stringers' top ends)
        span("Steel", (x, 1.25, CSZB), (x, MY + 1.05, CSZT), 0.05)
        helper("BLK_stairrail", (x - 0.06, 0.0, CSZT), (x + 0.06, MY + 1.1, CSZB + 0.06))
    # what you walk on: the slope through the treads' middles (the game sets a ramp's top half an outside riser under
    # its hi, so that's added back), from half a riser over the floor at its foot to half one over the mezzanine
    rise, slope = MY / NS, MY / (CSZB - CSZT)
    helper("RMP_stair", (CSX0, rise / 2 + 0.085, CSZT), (CSX1, MY + rise / 2 + 0.085, CSZB))
    ramp = lambda z: rise / 2 + (CSZB - z) * slope
    # under it, closed: oak-lined sides down from the stringers and an end under the mezzanine's edge...
    for x in (CSX0, CSX1):
        solid("Lining", [(x - 0.025, 0.0, CSZB + 0.05), (x + 0.025, 0.0, CSZB + 0.05), (x + 0.025, 0.0, CSZT), (x - 0.025, 0.0, CSZT)],
              [(x - 0.025, 0.0, CSZB + 0.05), (x + 0.025, 0.0, CSZB + 0.05), (x + 0.025, MY - 0.36, CSZT), (x - 0.025, MY - 0.36, CSZT)])
    lohi("Lining", (CSX0, 0.0, CSZT - 0.04), (CSX1, MY - 0.35, CSZT))
    # ...and blocked, in steps each kept under a climber's feet (a body 0.38 round reaching in from below it), as far
    # as where the floor's step up onto the slope takes over
    zs = [CSZT, 1.4, 1.9, 2.4, 2.9, 3.3, CSZB - (0.5 - rise / 2) / slope]
    for k in range(len(zs) - 1):
        helper("BLK_understair_%d" % k, (CSX0, 0.0, zs[k]), (CSX1, min(1.2, ramp(zs[k + 1] + 0.4) - 0.05), zs[k + 1]))
    # upstairs: the outfitters (the gear wall on up the back wall, the jackets, the closet, a mirror on the end wall)
    gear_wall(-10.3, -1.8, y0=MY, levels=(2.3, 2.75), n=12, top=2.8)   # (under the back wall's high windows)
    rail_x(-9.4, -5.4, MZ + 0.38, y0=MY, blk=False)
    helper("BLK_rack", (-9.5, MY, MZ), (-5.3, MY + 2.2, MZ + 0.55))
    rail_x(-9.4, -6.6, -3.6, y0=MY, two_sided=True)
    mirror(-6.6, -5.75, y0=MY)
    helper("TAP_Style", (-10.7, MY, MZ), (-5.0, MY + 2.6, MZ + 1.3))
    marker("SPOT_Style", (-6.4, MY, -4.9))
    display_table(-3.4, -4.4, y0=MY)
    till(-1.4, -6.2, -0.4, -5.2, y0=MY)
    mannequin(-8.0, -2.35, "Red", y0=MY); mannequin(-4.6, -2.35, "ArtB", y0=MY)
    sign(0, (-5.0, MY + 0.55, MZ1 + 0.06), 2.8, 0.7)
    # below: the lounge, double height at the glass, the hanging fire, a quiet corner under the mezzanine
    lohi("Rug", (-8.2, 0.0, 1.0), (-1.8, 0.015, 5.9))
    hanging_fire(-5.0, 3.7, under_shed)
    marker("LIGHT_fire", (-5.0, 1.7, 2.8))
    sofa(-7.4, -2.6, 1.65, 1)
    armchair(-7.75, 4.1, 90)
    armchair(-2.25, 4.1, -90)
    low_table(-5.8, 5.55, -4.2, 6.15)
    sofa(-10.2, -6.8, -5.9, 1, mat="Hide")      # under the mezzanine, by the back wall
    low_table(-9.4, -4.75, -7.6, -4.15)
    armchair(-5.6, -4.6, -90)
    plant(-10.2, 6.1); plant(-2.0, -5.9, 1.0)
    pendants(((-6.6, 4.6, 3.4), (-3.4, 4.3, 3.0), (-5.8, 5.1, 5.4), (-4.2, 4.9, 5.6)), under_shed)
    missions_board(3.6, w=2.0)
    board_shop()
    tuning_bench()
    welcome_mat()
    cafe()
    marker("LIGHT_hall", (-4.0, 7.6, -4.0))   # upstairs
    marker("LIGHT_low", (8.0, 2.9, 1.5))


# ---- B with an upstairs, three ways (B1, B2, B3): B's ground floor, its fire in the middle, with a floor at +3.8

def b_lounge(pend=((-3.6, 4.6, -4.6), (-0.2, 4.2, -4.8), (-2.2, 5.0, -1.4), (0.6, 4.8, -2.0)),
             FZ=-3.2, back=-5.55, front=-0.95, rug=-6.3, tables=((-4.6, -5.7), (1.0, -0.6))):
    """B's lounge: the low open fire in the middle of the hall, sofas facing across it, armchairs at its ends (FZ the
    fire's z, back and front the sofas', rug the rug's back edge: drawn forward in B2R, its stair behind)."""
    FX = -1.8
    lohi("Rug", (-5.2, 0.0, rug), (1.6, 0.015, -0.2))
    open_fire(FX, FZ)
    marker("LIGHT_fire", (FX, 1.4, FZ + 1.2))
    sofa(-4.0, 0.4, back, 1)
    sofa(-4.0, 0.4, front, -1)
    armchair(-5.5, FZ, 90)
    armchair(1.9, FZ, -90)
    for (tx, tz) in tables:
        cyl("Oak", (tx, 0.5, tz), 0.25, 0.04, 14); cyl("Steel", (tx, 0.0, tz), 0.03, 0.5, 6)
        helper("BLK_sidetable", (tx - 0.27, 0.0, tz - 0.27), (tx + 0.27, 0.55, tz + 0.27))
    pendants(pend, under_flat)


def b_boutique(gear_levels=(2.7, 3.7, 4.7, 5.7), gear_top=None, sign_z=0.75, storefront=True):
    """B's outfitters at the high end, on the ground: the gear wall, the jackets and the closet at the back, a clear
    way through to the café, the storefront at the tall glass."""
    gear_wall(-10.4, -6.9, n=7, levels=gear_levels, top=gear_top)
    rail_x(-10.2, -7.0, MZ + 0.38, blk=False)
    helper("BLK_rack", (-10.3, 0.0, MZ), (-6.9, 2.2, MZ + 0.55))
    mirror(-6.4, -5.0)
    helper("TAP_Style", (-10.7, 0.0, MZ), (-6.8, 2.6, MZ + 1.2))
    marker("SPOT_Style", (-8.6, 0.0, MZ + 2.0))
    display_table(-8.6, -2.3)
    lohi("Runner", (-10.9, 0.0, -0.4), (-6.5, 0.012, 1.9))
    if storefront:
        rail_x(-10.1, -7.2, 3.4, two_sided=True)
        rail_x(-10.2, -7.6, 5.75)
        mannequin(-9.3, 6.35, "Red"); mannequin(-8.3, 6.35, "ArtB"); mannequin(-7.3, 6.35, "Navy")
        till(-7.3, 2.6, -6.6, 3.9)
    if sign_z is not None:
        hanging_sign(0, -6.55, 4.9, sign_z, 3.0, 0.75, lambda x: ru(x) - RD, yaw=90)


def b_rest(missions_x=4.0):
    """B's low end and the wing as they are: the missions board, the board shop, wax & tune, the café."""
    missions_board(missions_x, w=1.9)
    board_shop(back_x0=5.3)
    tuning_bench()
    welcome_mat()
    cafe()
    marker("LIGHT_low", (8.0, 2.9, 1.5))


def upper_floor(name, x0, x1, z0, z1, y=MY):
    """A floor upstairs: concrete with floorboards on top; walked on (FLR_), and the camera keeps under or over its slab (CAM_)."""
    lohi("Concrete", (x0, y - 0.3, z0), (x1, y - 0.02, z1))
    lohi("FloorBoards", (x0, y - 0.02, z0), (x1, y, z1))
    helper("FLR_" + name, (x0, y - 0.3, z0), (x1, y, z1))
    helper("CAM_" + name + "slab", (x0, y - 0.32, z0), (x1, y - 0.01, z1))


def open_edge(axis, fixed, a0, a1, y=MY, side=1, rail=True, name="edge"):
    """An upper floor's open edge (along x at z=fixed, or along z at x=fixed): a black fascia, a warm strip under it,
    and a glass rail on it (walls the walker in, not the camera). side: which way the floor lies from the edge."""
    a0, a1 = a0 + 0.005, a1 - 0.005   # (just inside: at the floor's corner its edge and theirs were one plane)
    if axis == "x":
        lohi("Steel", (a0, y - 0.32, fixed - 0.02), (a1, y + 0.02, fixed + 0.02))
        zl = fixed + side * 0.1
        lohi("LED", (a0 + 0.15, y - 0.34, min(zl, zl + side * 0.05)), (a1 - 0.15, y - 0.32, max(zl, zl + side * 0.05)))
        if rail:
            pane("x", fixed + side * 0.04, a0, a1, y, y + 1.0, y + 1.0, 0.02, "GlassIn")
            lohi("Steel", (a0, y + 1.0, fixed + side * 0.04 - 0.03), (a1, y + 1.05, fixed + side * 0.04 + 0.03))
            helper("BLK_" + name, (a0, y, fixed - 0.1), (a1, y + 1.1, fixed + 0.1))
    else:
        lohi("Steel", (fixed - 0.02, y - 0.32, a0), (fixed + 0.02, y + 0.02, a1))
        xl = fixed + side * 0.1
        lohi("LED", (min(xl, xl + side * 0.05), y - 0.34, a0 + 0.15), (max(xl, xl + side * 0.05), y - 0.32, a1 - 0.15))
        if rail:
            pane("z", fixed + side * 0.04, a0, a1, y, y + 1.0, y + 1.0, 0.02, "GlassIn")
            lohi("Steel", (fixed + side * 0.04 - 0.03, y + 1.0, a0), (fixed + side * 0.04 + 0.03, y + 1.05, a1))
            helper("BLK_" + name, (fixed - 0.1, y, a0), (fixed + 0.1, y + 1.1, a1))


def post(x, z, y=MY):
    lohi("Steel", (x - 0.07, 0.0, z - 0.07), (x + 0.07, y - 0.3, z + 0.07))
    helper("BLK_post", (x - 0.1, 0.0, z - 0.1), (x + 0.1, y - 0.3, z + 0.1))


def flight(name, x0, x1, z0, z1, up, y0, y1, glass=(True, True), end=True):
    """A straight flight filling x0..x1, z0..z1, climbing from y0 to y1 toward `up` ('-z', '+z', '-x' or '+x'): oak
    treads (17 cm risers) on black steel stringers with glass sides; the walking slope through the treads' middles
    (RMP_, RMPS_, RMPW_, RMPE_: the game turns the last three); under it closed in timber and blocked in steps that
    stay under a climber's feet; at its top end (end) a panel down to the floor under whatever it climbs onto."""
    along_z = up in ("-z", "+z")
    sgn = -1.0 if up in ("-z", "-x") else 1.0
    s0, s1 = (z0, z1) if along_z else (x0, x1)
    v0, v1 = (x0, x1) if along_z else (z0, z1)
    L = s1 - s0
    n = max(1, int(round((y1 - y0) / 0.1727)))
    rise, run = (y1 - y0) / n, L / n
    foot = s1 if sgn < 0 else s0
    top = foot + sgn * L

    def P(s, y, v):
        return (v, y, s) if along_z else (s, y, v)

    def box_sv(mat, sa, sb, ya, yb, va, vb):
        lohi(mat, P(min(sa, sb), ya, min(va, vb)), P(max(sa, sb), yb, max(va, vb)))

    for i in range(n):
        yt = y0 + rise * (i + 1)
        sc = foot + sgn * (i + 0.5) * run
        far = sc + sgn * (run / 2 + 0.02) if i < n - 1 else top   # (the top tread stops at the floor's edge, not on it)
        box_sv("Oak", sc - sgn * (run / 2 + 0.02), far, yt - 0.06, yt, v0 + 0.06, v1 - 0.06)
        nose = sc - sgn * run / 2
        box_sv("LED", nose, nose + sgn * 0.04, yt - 0.075, yt - 0.065, v0 + 0.1, v1 - 0.1)
    fs = foot - sgn * 0.05
    for k, v in enumerate((v0, v1)):
        solid("Steel", [P(fs, y0, v - 0.03), P(fs, y0, v + 0.03), P(top, y1 - 0.35, v + 0.03), P(top, y1 - 0.35, v - 0.03)],
              [P(fs, y0 + 0.25, v - 0.03), P(fs, y0 + 0.25, v + 0.03), P(top, y1 + 0.02, v + 0.03), P(top, y1 + 0.02, v - 0.03)])
        if glass[k]:
            tg = top - sgn * 0.005   # (the glass ends just short of the stringer's top end: they were one plane there)
            solid("GlassIn", [P(foot, y0 + 0.25, v - 0.01), P(foot, y0 + 0.25, v + 0.01), P(tg, y1, v + 0.01), P(tg, y1, v - 0.01)],
                  [P(foot, y0 + 1.2, v - 0.01), P(foot, y0 + 1.2, v + 0.01), P(tg, y1 + 1.0, v + 0.01), P(tg, y1 + 1.0, v - 0.01)])
            span("Steel", P(foot, y0 + 1.25, v), P(top, y1 + 1.05, v), 0.05)
        lo = P(min(foot, top), y0, v - 0.06); hi = P(max(foot, top), y1 + 1.1, v + 0.06)
        helper("BLK_%srail" % name, lo, hi)
        # the timber side under it, down to the floor
        solid("Lining", [P(fs, 0.0, v - 0.025), P(fs, 0.0, v + 0.025), P(top, 0.0, v + 0.025), P(top, 0.0, v - 0.025)],
              [P(fs, max(0.0, y0 - 0.36), v - 0.025), P(fs, max(0.0, y0 - 0.36), v + 0.025), P(top, y1 - 0.36, v + 0.025), P(top, y1 - 0.36, v - 0.025)])
    if end:
        box_sv("Lining", top, top - sgn * 0.04, 0.0, y1 - 0.35, v0, v1)   # (up to the stringers' undersides, not past)
    # what you walk on (the game sets a ramp's top half an outside riser under its hi: added back)
    prefix = {"-z": "RMP_", "+z": "RMPS_", "-x": "RMPW_", "+x": "RMPE_"}[up]
    yh = y1 + rise / 2 + 0.085
    helper(prefix + name, P(min(foot, top), yh - (y1 - y0), v0), P(max(foot, top), yh, v1))

    def h(d):   # the slope's height d along from the foot
        return y0 + rise / 2 + d / L * (y1 - y0)
    # blocked under it, in steps (each under a climber's feet as their body reaches over it from below), from where
    # the floor's own step up onto the slope gives out to the top
    d_min = max(0.0, (0.5 - y0 - rise / 2) / (y1 - y0) * L) if y0 < 0.5 else 0.0
    ds = [d_min]
    while ds[-1] < L - 1e-3:
        ds.append(min(L, ds[-1] + 0.5))
    for k in range(len(ds) - 1):
        da, db = ds[k], ds[k + 1]
        hi_k = min(1.2, h(max(0.0, da - 0.4)) - 0.05)
        if hi_k < 0.12:
            continue
        sa, sb = foot + sgn * da, foot + sgn * db
        helper("BLK_%sunder%d" % (name, k), P(min(sa, sb), 0.0, v0), P(max(sa, sb), hi_k, v1))
        # (and the camera kept out of its solid body too, up to just under the treads: else, looking across the
        # stair, it can end up inside it, between flights)
        hc = h(da) - rise / 2 - 0.12
        if hc > 0.3:
            helper("CAM_%sbody%d" % (name, k), P(min(sa, sb), 0.0, v0 + 0.03), P(max(sa, sb), hc, v1 - 0.03))


def landing(name, x0, x1, z0, z1, y, closed=("x0", "x1", "z0", "z1"), rails=()):
    """A landing between flights: a slab on its own, closed in timber under it and blocked there."""
    upper_floor(name, x0, x1, z0, z1, y)
    for c in closed:
        if c == "x0": lohi("Lining", (x0, 0.0, z0), (x0 + 0.04, y - 0.3, z1))
        if c == "x1": lohi("Lining", (x1 - 0.04, 0.0, z0), (x1, y - 0.3, z1))
        if c == "z0": lohi("Lining", (x0, 0.0, z0), (x1, y - 0.3, z0 + 0.04))
        if c == "z1": lohi("Lining", (x0, 0.0, z1 - 0.04), (x1, y - 0.3, z1))
    helper("BLK_%sunder" % name, (x0, 0.0, z0), (x1, min(1.2, y - 0.5), z1))
    helper("CAM_%sbody" % name, (x0 + 0.03, 0.0, z0 + 0.03), (x1 - 0.03, y - 0.33, z1 - 0.03))
    for (axis, fixed, a0, a1, side) in rails:
        open_edge(axis, fixed, a0, a1, y, side, True, name + "rail")


def telescope(x, z, y=0.0, yaw=0.0):
    """A brass-and-black telescope on a tripod, aimed out of the glass."""
    for k in range(3):
        a = math.radians(yaw + 120 * k)
        span("Steel", (x + 0.32 * math.sin(a), y, z + 0.32 * math.cos(a)), (x, y + 1.15, z), 0.03)
    box("Steel", (x, y + 1.28, z), (0.12, 0.12, 0.9), (-12, yaw, 0))
    box("Oak", (x, y + 1.28, z), (0.13, 0.13, 0.2), (-12, yaw, 0))
    helper("BLK_telescope", (x - 0.4, y, z - 0.4), (x + 0.4, y + 1.5, z + 0.4))


def bookcase(x0, x1, z, h=2.2, y=0.0):
    """Oak shelves of books against the back wall."""
    lohi("Oak", (x0, y, z), (x1, y + h, z + 0.05))
    for k in range(5):
        yy = y + 0.1 + k * (h - 0.2) / 4.5
        lohi("Oak", (x0, yy, z), (x1, yy + 0.03, z + 0.32))
        if k < 4:
            n = int((x1 - x0) / 0.16)
            for i in range(n):
                hb = 0.2 + 0.08 * ((i * 7 + k * 3) % 3)
                box(("Red", "Navy", "Cream", "ArtA", "Charcoal", "Leaf", "ArtB")[(i + k * 2) % 7], (x0 + 0.1 + i * 0.16, yy + 0.03 + hb / 2, z + 0.18), (0.12, hb, 0.24))
    helper("BLK_bookcase", (x0, y, z), (x1, y + h, z + 0.35))


# ---- B1: the gallery (a gallery at +3.8 along the back and over the boutique, open to the fire's double height)

def layout_B1():
    b_lounge(pend=((-4.2, 4.6, -2.0), (0.6, 4.8, -2.0), (-2.6, 5.0, -1.2), (-0.4, 4.4, -4.0)))
    b_boutique(gear_levels=(2.1, 2.9), gear_top=3.35, sign_z=1.4)
    plant(-2.9, 6.15, 1.1); plant(1.6, 6.15, 1.5); plant(-6.0, -6.1, 1.2)
    b_rest()
    # the gallery: along the back wall from the high end to past the missions board, and over the boutique's back
    GZ, GX = -4.5, -6.5
    upper_floor("gal", XH + T, 4.6, MZ, GZ)
    upper_floor("galend", XH + T, GX, GZ, -0.5)
    open_edge("x", GZ, GX, 3.2, side=-1, name="galrail")
    open_edge("z", 4.6, MZ, GZ, side=-1, name="galrail")
    open_edge("z", GX, GZ, -0.5, side=-1, name="galrail")
    open_edge("x", -0.5, XH + T, GX, side=-1, name="galrail")
    post(GX - 0.05, -0.55); post(GX - 0.05, GZ - 0.05); post(3.15, GZ - 0.05)
    for (px, pz) in ((-9.0, -5.6), (-5.0, -5.6), (-1.0, -5.6), (2.6, -5.6), (-8.6, -2.6)):   # downlights under it
        cyl("LED", (px, MY - 0.33, pz), 0.12, 0.02, 12)
    # the feature stair: up the hall's east side from by the door, onto the gallery over the missions board
    flight("b1", 3.2, 4.6, GZ, GZ + 6.16, "-z", 0.0, MY)
    # upstairs: the gear gallery along the back wall (under the high windows), and a corner over the boutique with a
    # sofa looking out over the storefront to the tall glass
    gear_wall(-10.3, 2.8, y0=MY, levels=(2.3, 2.75), n=18, top=2.8)
    for (r0, r1) in ((-9.6, -6.8), (-3.6, -0.4)):
        rail_x(r0, r1, MZ + 0.38, y0=MY, blk=False)
        helper("BLK_rack", (r0 - 0.1, MY, MZ), (r1 + 0.1, MY + 2.2, MZ + 0.55))
    sofa(-10.2, -7.2, -1.35, 1, mat="Hide", y=MY)
    armchair(-7.4, -2.9, 90, y=MY)
    low_table(-9.6, -2.9, -8.2, -2.3, y=MY)
    plant(-10.2, -3.9, 1.0, y=MY)
    plant(4.0, -6.2, 1.0, y=MY)
    marker("LIGHT_hall", (-1.8, 7.5, -3.2))


# ---- B2: the loft (a full floor at +3.8 over the high end: the outfitters up there; a snug under it)

def layout_B2():
    b_lounge()
    b_rest()
    # under the loft: the snug (books, a leather sofa, armchairs) and, at the glass, the café's tables spilling out
    lohi("Runner", (-10.9, 0.0, -0.4), (-6.5, 0.012, 1.9))          # the runner: the way through to the café
    lohi("Rug", (-10.4, 0.0, -6.0), (-6.4, 0.015, -1.8))
    bookcase(-10.3, -6.9, MZ, h=2.4)
    sofa(-10.0, -7.4, -5.25, 1, mat="Hide")
    low_table(-9.4, -4.2, -8.0, -3.6)
    armchair(-9.6, -2.6, 180); armchair(-7.8, -2.6, 180)
    bistro(-9.2, 3.6); bistro(-8.4, 5.6)
    plant(-6.0, -6.1, 1.2)
    # the loft
    LX = -5.4
    upper_floor("loft", XH + T, LX, MZ, 6.5)
    open_edge("z", LX, MZ, 4.6, side=-1, name="loftrail")
    open_edge("z", LX, 6.1, 6.5, side=-1, name="loftrail")
    post(LX - 0.05, -6.55); post(LX - 0.05, 4.45)   # (at the back wall and at the stair's top: the column at (-6.37, 0) holds the middle)
    for (px, pz) in ((-9.0, -5.0), (-7.0, -2.0), (-9.0, 1.0), (-7.0, 4.4)):
        cyl("LED", (px, MY - 0.33, pz), 0.12, 0.02, 12)
    # the stair along the front glass, from by the door up to the loft
    flight("b2", LX, LX + 6.16, 4.6, 6.1, "-x", 0.0, MY)
    # upstairs: the outfitters (gear wall, jackets and the closet, a mirror, a table, the till) and their storefront
    # at the glass
    gear_wall(-10.3, -6.0, y0=MY, levels=(2.3, 2.75), n=7, top=2.8)
    rail_x(-10.2, -7.0, MZ + 0.38, y0=MY, blk=False)
    helper("BLK_rack", (-10.3, MY, MZ), (-6.9, MY + 2.2, MZ + 0.55))
    mirror(-6.6, -5.75, y0=MY)
    helper("TAP_Style", (-10.7, MY, MZ), (-6.8, MY + 2.6, MZ + 1.2))
    marker("SPOT_Style", (-8.6, MY, MZ + 2.0))
    display_table(-8.6, -2.3, y0=MY)
    rail_x(-10.1, -7.2, 3.4, y0=MY, two_sided=True)
    mannequin(-9.3, 6.0, "Red", y0=MY); mannequin(-8.3, 6.0, "ArtB", y0=MY); mannequin(-7.3, 6.0, "Navy", y0=MY)
    till(-10.6, 0.3, -9.9, 1.5, y0=MY)
    hanging_sign(0, LX + 0.15, MY + 2.0, 1.0, 3.0, 0.75, lambda x: ru(x) - RD, yaw=90)
    marker("LIGHT_hall", (-4.6, 7.6, -1.0))


# ---- B2R: B2 revised, the user's pick with its stair moved to the back wall (the default lodge). The loft, the
# outfitters up there and the snug under it as B2; the stair is B2's (the same flight, climbing toward the loft) but
# along the back wall, arriving at the loft's back corner by the gear wall; the lounge drawn forward off the wall for it
# (the fire and the sofas a little closer, the ring round the fire between them); the front glass clear again, as B's.

def layout_B2R():
    LX = -5.4
    SZ0, SZ1 = MZ + 0.06, MZ + 1.56       # the stair: off the back wall's face, 1.5 m wide (as B2's)
    b_lounge(pend=((-3.6, 4.6, -3.9), (-0.2, 4.2, -4.0), (-2.2, 5.0, -1.5), (0.6, 4.8, -2.0)),
             FZ=-2.71, back=-4.62, front=-0.8, rug=-5.05, tables=((-4.6, -4.62), (1.0, -0.45)))
    b_rest()
    # under the loft: the snug and the café's tables, as B2
    lohi("Runner", (-10.9, 0.0, -0.4), (-6.5, 0.012, 1.9))
    lohi("Rug", (-10.4, 0.0, -6.0), (-6.4, 0.015, -1.8))
    bookcase(-10.3, -6.9, MZ, h=2.4)
    sofa(-10.0, -7.4, -5.25, 1, mat="Hide")
    low_table(-9.4, -4.2, -8.0, -3.6)
    armchair(-9.6, -2.6, 180); armchair(-7.8, -2.6, 180)
    bistro(-9.2, 3.6); bistro(-8.4, 5.6)
    plant(-6.0, -6.1, 1.2)
    plant(-2.9, 6.15, 1.1); plant(1.6, 6.15, 1.5)   # (the front glass clear again, as B's)
    # the loft, open along its edge but where the stair comes up at its back corner
    upper_floor("loft", XH + T, LX, MZ, 6.5)
    open_edge("z", LX, SZ1, 6.5, side=-1, name="loftrail")
    post(LX - 0.05, SZ1 + 0.1); post(LX - 0.05, 4.45)   # (beside the stair's top, and toward the glass: the column at (-6.37, 0) holds the middle)
    for (px, pz) in ((-9.0, -5.0), (-7.0, -2.0), (-9.0, 1.0), (-7.0, 4.4)):
        cyl("LED", (px, MY - 0.33, pz), 0.12, 0.02, 12)
    # the stair along the back wall, from behind the lounge up to the loft (glass on the hall's side, a handrail on the wall)
    flight("b2r", LX, LX + 6.16, SZ0, SZ1, "-x", 0.0, MY, glass=(False, True))
    span("Steel", (LX + 6.16, 1.0, MZ + 0.045), (LX, MY + 0.95, MZ + 0.045), 0.04)
    for k in range(4):   # (its brackets)
        bx = LX + 6.16 - (k + 0.5) * 6.16 / 4
        by = 1.0 + (LX + 6.16 - bx) / 6.16 * (MY - 0.05)
        lohi("Steel", (bx - 0.02, by - 0.06, MZ), (bx + 0.02, by - 0.02, MZ + 0.05))
    # upstairs: the outfitters as B2's (the stair comes up beside the gear wall)
    gear_wall(-10.3, -6.0, y0=MY, levels=(2.3, 2.75), n=7, top=2.8)
    rail_x(-10.2, -7.0, MZ + 0.38, y0=MY, blk=False)
    helper("BLK_rack", (-10.3, MY, MZ), (-6.9, MY + 2.2, MZ + 0.55))
    mirror(-6.6, -5.75, y0=MY)
    helper("TAP_Style", (-10.7, MY, MZ), (-6.8, MY + 2.6, MZ + 1.2))
    marker("SPOT_Style", (-8.6, MY, MZ + 2.0))
    display_table(-8.6, -2.3, y0=MY)
    rail_x(-10.1, -7.2, 3.4, y0=MY, two_sided=True)
    mannequin(-9.3, 6.0, "Red", y0=MY); mannequin(-8.3, 6.0, "ArtB", y0=MY); mannequin(-7.3, 6.0, "Navy", y0=MY)
    till(-10.6, 0.3, -9.9, 1.5, y0=MY)
    hanging_sign(0, LX + 0.15, MY + 2.0, 1.0, 3.0, 0.75, lambda x: ru(x) - RD, yaw=90)
    marker("LIGHT_hall", (-4.6, 7.6, -1.0))


# ---- B3: the balcony (a floor at +3.8 along the front glass, the stair a dog-leg standing in the hall)

def layout_B3():
    b_lounge()
    b_boutique(sign_z=None)
    sign(0, (-8.6, MY - 0.62, 0.76), 2.4, 0.6, 180)     # (on the balcony's edge, facing the hall)
    plant(-6.0, -6.1, 1.2); plant(2.2, 6.2, 1.5)
    b_rest()
    # the balcony
    BX, BZ = -3.08, 0.8
    upper_floor("balc", XH + T, BX, BZ, 6.5)
    open_edge("x", BZ, XH + T, BX, side=1, name="balcrail")
    open_edge("z", BX, BZ, 3.2, side=-1, name="balcrail")
    open_edge("z", BX, 4.4, 6.5, side=-1, name="balcrail")
    post(BX - 0.05, BZ + 0.05); post(-10.6, BZ + 0.05)
    for (px, pz) in ((-9.0, 2.2), (-6.0, 1.8), (-4.0, 4.0), (-9.0, 4.8), (-6.0, 5.4)):
        cyl("LED", (px, MY - 0.33, pz), 0.12, 0.02, 12)
    # the dog-leg: up toward the door, a landing half way, and back up onto the balcony
    YL = MY / 2
    flight("b3a", BX, 0.0, 4.6, 5.8, "+x", 0.0, YL, end=False)
    landing("b3land", 0.0, 1.4, 3.2, 5.8, YL, closed=("x1", "z0", "z1"),
            rails=(("z", 1.4, 3.2, 5.8, -1), ("x", 3.2, 0.0, 1.4, 1), ("x", 5.8, 0.0, 1.4, -1)))
    flight("b3b", BX, 0.0, 3.2, 4.4, "-x", YL, MY)
    # upstairs: a window lounge along the glass (the camp, the fire pit and the gate below), a telescope, a chair at
    # the big end window
    sofa(-9.6, -6.9, 5.55, 1, y=MY)
    armchair(-5.3, 5.45, 0, y=MY)
    low_table(-6.5, 3.9, -5.3, 4.5, y=MY)
    telescope(-4.1, 5.9, y=MY)
    armchair(-9.8, 2.4, -90, y=MY)
    plant(-10.2, 1.4, 1.0, y=MY)
    marker("LIGHT_hall", (-1.8, 7.5, -3.2))


LAYOUTS = {"A": layout_A, "B": layout_B, "C": layout_C, "B1": layout_B1, "B2": layout_B2, "B3": layout_B3, "B2R": layout_B2R}

# warm light: a strip along the clerestory beam
solid("LED", [(XH + 0.2, ru(XH + 0.2) - RD - BD - 0.03, ZC + 0.15), (XB, ru(XB) - RD - BD - 0.03, ZC + 0.15), (XB, ru(XB) - RD - BD - 0.03, ZC + 0.19), (XH + 0.2, ru(XH + 0.2) - RD - BD - 0.03, ZC + 0.19)],
      [(XH + 0.2, ru(XH + 0.2) - RD - BD, ZC + 0.15), (XB, ru(XB) - RD - BD, ZC + 0.15), (XB, ru(XB) - RD - BD, ZC + 0.19), (XH + 0.2, ru(XH + 0.2) - RD - BD, ZC + 0.19)])
marker("LIGHT_porch", (0.5, 3.0, ZT - 1.8))   # (under the eave's edge: the terrace, the steps and the snow below them)

# ---------------------------------------------------------------- walls for walking and the camera

def wcol(name, lo, hi):
    helper("COL_" + name, lo, hi)


wcol("front_a", (XH, 0.0, D - T - 0.05), (DOOR[0] - 0.02, H, D + 0.02))
wcol("front_b", (DOOR[1] + 0.02, 0.0, D - T - 0.05), (XL, H, D + 0.02))
wcol("front_top", (DOOR[0], DOOR[2], D - T - 0.05), (DOOR[1], H, D + 0.02))
wcol("back", (XH, 0.0, -D), (XL, H, -D + T))
wcol("low_a", (XL - T, 0.0, -D), (XL, Y0, SIDE_DOOR[0] - 0.02))
wcol("low_b", (XL - T, 0.0, SIDE_DOOR[1] + 0.02), (XL, Y0, D))
wcol("low_top", (XL - T, SIDE_DOOR[2], SIDE_DOOR[0]), (XL, Y0, SIDE_DOOR[1]))
wcol("high_a", (XH, 0.0, -D), (XH + T, H, WOPEN[0] - 0.02))
wcol("high_b", (XH, 0.0, WOPEN[1] + 0.02), (XH + T, H, D))
wcol("high_top", (XH, WOPEN[2] - 0.45, WOPEN[0]), (XH + T, H, WOPEN[1]))
wcol("wing_front", (XW, 0.0, WZ1 - T), (XH, WY, WZ1))
wcol("wing_end", (XW, 0.0, WZ0), (XW + T, WY, WZ1))
wcol("wing_back", (XW, 0.0, WZ0), (XH, WY, WZ0 + T))
wcol("box_side", (XB - T, ru(XB), -D), (XB, H, ZC))
# the roofs, for the camera only: the shed in steps along its slope, the flat roof, the wing's
x = SX0
while x < SX1 - 1e-3:
    x1 = min(SX1, x + 2.0)
    helper("CAM_shed", (x, ru(x1) - RD - 0.1, ZC), (x1, ru(x) + RT + 0.4, SZ1 + 0.1))
    if x1 > XB:
        helper("CAM_shed", (max(x, XB), ru(x1) - RD - 0.1, SZ0 - 0.1), (x1, ru(max(x, XB)) + RT + 0.4, ZC))
    x = x1
helper("CAM_flat", (FX0, H - RD - 0.1, FZ0), (FX1, H + RT + 0.5, FZ1))
helper("CAM_wing", (WRX0, WY - RD - 0.2 - 0.25, WRZ0), (XH, WY + RT + 0.5, WRZ1 + 0.6))
marker("MARK_front", (0, 0, D))
marker("MARK_right", (XL, 0, 0))

# ---------------------------------------------------------------- write it out

SHELL_FACES = []


def write(path, name):
    """The meshes and helpers made so far, as one FBX (then cleared, for the next file)."""
    coll = bpy.context.scene.collection
    root = bpy.data.objects.new(name, None)
    coll.objects.link(root)
    for key, b in geo.items():
        mat = key[:-3] if key.endswith("_in") else key
        me = bpy.data.meshes.new(key)
        b.to_mesh(me)
        b.free()
        m = bpy.data.materials.get(mat) or bpy.data.materials.new(mat)
        me.materials.append(m)
        ob = bpy.data.objects.new(key, me)
        coll.objects.link(ob)
        ob.parent = root
    hm = bpy.data.materials.get("Helper") or bpy.data.materials.new("Helper")
    for i, (hname, c, s_) in enumerate(helpers):
        b = bmesh.new()
        bmesh.ops.create_cube(b, size=1.0)
        bmesh.ops.transform(b, matrix=TOB @ mat4(c, s_), verts=b.verts)
        me = bpy.data.meshes.new(hname)
        b.to_mesh(me)
        b.free()
        me.materials.append(hm)
        ob = bpy.data.objects.new("%s__%d" % (hname, i), me)
        coll.objects.link(ob)
        ob.parent = root
    if name == "Lodge":
        SHELL_FACES[:] = FACES
        report_coplanar(name, FACES)
        SHELL_SOLIDS[:] = SOLIDS
        SOLIDS.clear()
    else:
        report_coplanar(name, SHELL_FACES + FACES, len(SHELL_FACES))
    FACES.clear(); SOLIDS.clear()
    print("LODGE %s: %d triangles in %d meshes (%d materials), helpers: %d" % (name, sum(tris.values()), len(geo), len(tris), len(helpers)))
    print("LODGE %s by material: " % name + ", ".join("%s %d" % kv for kv in sorted(tris.items(), key=lambda kv: -kv[1])))
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                             mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO", embed_textures=False)
    print("LODGE written", path)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)
    geo.clear(); tris.clear(); helpers.clear()


write(OUT, "Lodge")
for key, build in LAYOUTS.items():
    with inside():
        build()
    marker("MARK_front", (0, 0, D))
    write(OUT[:-4] + key + ".fbx", "Lodge" + key)
