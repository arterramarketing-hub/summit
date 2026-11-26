# Builds the summit camp's lodge in Blender and exports it to Assets/Summit/Resources/Camp/Lodge.fbx.
#   /Applications/Blender.app/Contents/MacOS/Blender --background --python Tools/MakeLodge.py -- Assets/Summit/Resources/Camp/Lodge.fbx
#
# A contemporary timber ski lodge after the user's photo (Tools/art/reference/lodge-reference.jpg):
#   - one long shed roof (17 degrees) rising from the low end (+x, the photo's left) to the high end (-x), its deep
#     eaves carried on exposed warm glulam rafters and beams, thin charcoal metal edges and a thick rounded snow load
#   - behind it, a tall volume under a flat roof; between the rising shed roof and that roof, a glazed clerestory, tall
#     at the low end and closing to nothing where the shed reaches it; a dark gabled block on top
#   - a lower wing at the high end with its rafter tails, its roof a lookout deck level with the upper floors inside
#     (glass rails with snow along them, a ledge and stools at the front rail, a telescope, a binocular viewer, lounge
#     chairs and a fire bowl), out through sliding doors in the high end's glass
#   - dark-stained vertical board cladding, floor-to-ceiling glass in black frames (two storeys at the high end)
#   - a raised concrete terrace across the front and round the low end, wide concrete steps, low concrete planters,
#     chairs, a fire table, a chalkboard sign; at the low end the roof's tail runs on, nearly level, over a covered
#     section eight metres deep: ski and board racks along its back, picnic tables, the side door's way out between them
# Inside: one open hall (22 x 14 m) under the sloping timber ceiling and the tall volume (10 m), glulam columns and
# beams, a polished concrete floor, timber-lined walls, and the wing (5 x 10.5 m) open off its high end. Its inside (the
# loft, the back stair, the lounge, the outfitters, the café: lodge.md) is a file of its own, LodgeInside.fbx.
#
# Everything is authored in the game's own axes, like Tools/MakeChalet.py: x right, y up, z out of the front (downhill),
# the pivot the middle of the hall's floor. One mesh per material; the game paints the materials itself, by name
# (CampLodge.cs). Helper boxes, as MakeChalet.py's:
#   COL_ walls (walking and the camera)   CAM_ the camera only   BLK_ furniture (walking only)
#   FLR_ floors   RMP_ steps (top falls by its height from its -z end to its +z end)
#   TAP_/SPOT_ the stations   FX_fire the fire table's flames   LIGHT_ lamps   MARK_ which way it faces
#   AREA_ the inside (for the camera and the footsteps), STEP_ the stairs' centre lines (for the way down)
#   LOOK_ the lookout's deck (standing still there the camera takes in the mountain, as at the camp's lookout)

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "Lodge.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)

# game axes -> Blender axes (a mirror: every face is turned round as it is copied in; see MakeChalet.py)
TOB = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

# metres a texture's tile spans (its UVs are the face's metres over this). The lodge's PBR textures (CampLodge.cs,
# Resources/Camp/LodgeTex, Tools/MakeLodgeTex.py) at their real size: the oak veneer 1.83 m, the hinoki planks 1.89,
# the black planks 1.6, the concrete and the stacked stone 2.0, the steel 1.0; the fabrics larger than life (a weave
# at its true 27 cm shimmers on a phone): the tweed 0.6, the rug's check 1.3, the leather 0.5
TILE = {"Cladding": 1.6, "Lining": 1.89, "Soffit": 1.89, "FloorBoards": 1.89, "Decking": 1.89, "Throw": 0.5, "Glulam": 1.83, "Oak": 1.83, "CounterTop": 1.83,
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
# walls, tops): those of the shell alone, and of the inside with the shell (they are drawn together).
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
    only pairs with a face at index >= new_from (the inside's own, when the shell's come first)."""
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


# ---------------------------------------------------------------- modelled pieces (lodgeshop)
# The shop's pieces were plain boxes and spheres; these give them bevelled edges that catch the light and real
# outlines (lofted, swept, lathed), in the lodge's own shared materials (no new ones: one mesh a material still).

def rbox(mat, pos, size, rot=(0, 0, 0), r=0.012, seg=1):
    """A box with its edges bevelled r (one segment: a chamfer that catches the light; two or three: rounded)."""
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    r = min(r, 0.45 * min(size))
    if r > 2e-4 and max(size) > 0.12:   # (a small piece: a plain box, the bevel's triangles not worth it on a phone)
        bmesh.ops.bevel(b, geom=list(b.edges), offset=r, segments=seg, profile=0.5, affect='EDGES', clamp_overlap=True)
    add(b, mat, mat4(pos, (1, 1, 1), rot))


def rlohi(mat, lo, hi, r=0.012, seg=1):
    rbox(mat, [(lo[i] + hi[i]) / 2 for i in range(3)], [abs(hi[i] - lo[i]) for i in range(3)], r=r, seg=seg)


def _frame(pts):
    """Parallel-transported frames (tangent, normal, binormal) along a polyline."""
    P = [Vector(p) for p in pts]
    T_ = []
    for i in range(len(P)):
        if i == 0:
            t = P[1] - P[0]
        elif i == len(P) - 1:
            t = P[-1] - P[-2]
        else:
            t = (P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()
        T_.append(t.normalized())
    ref = Vector((0, 0, 1)) if abs(T_[0].z) < 0.9 else Vector((1, 0, 0))
    n = (ref - T_[0] * ref.dot(T_[0])).normalized()
    out = []
    for i, t in enumerate(T_):
        if i > 0:
            n = (n - t * n.dot(t))
            n = n.normalized() if n.length > 1e-6 else (Vector((1, 0, 0)) - t * t.x).normalized()
        out.append((P[i], t, n, t.cross(n)))
    return out


def tube(mat, pts, r, seg=8, caps=True, smooth=True):
    """A round bar along a polyline (game space): r one radius, or one a point."""
    rs = r if isinstance(r, (list, tuple)) else [r] * len(pts)
    b = bmesh.new()
    rings = []
    for (p, t, n, bn), rr in zip(_frame(pts), rs):
        rings.append([b.verts.new(p + (n * math.cos(2 * math.pi * k / seg) + bn * math.sin(2 * math.pi * k / seg)) * rr) for k in range(seg)])
    for i in range(len(rings) - 1):
        for k in range(seg):
            f = b.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
            f.smooth = smooth
    if caps:
        b.faces.new(rings[0])
        b.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    add(b, mat, Matrix.Identity(4))


def loft(mat, rings, m=None, caps=True, smooth=True):
    """A closed shape through rings of points (each the same count, the same way round), in a frame m (game space)."""
    b = bmesh.new()
    R = [[b.verts.new(Vector(p)) for p in ring] for ring in rings]
    n = len(R[0])
    for i in range(len(R) - 1):
        for k in range(n):
            f = b.faces.new((R[i][k], R[i][(k + 1) % n], R[i + 1][(k + 1) % n], R[i + 1][k]))
            f.smooth = smooth
    if caps:
        b.faces.new(R[0])
        b.faces.new(list(reversed(R[-1])))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    add(b, mat, m if m is not None else Matrix.Identity(4))


def ring_y(y, w, d, n=12, e=3.0, cz=0.0, cx=0.0):
    """A level ring at height y, w across (x) and d deep (z): a superellipse (e 2 an ellipse, larger squarer)."""
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s_ = math.cos(a), math.sin(a)
        pts.append((cx + w / 2 * math.copysign(abs(c) ** (2 / e), c), y, cz + d / 2 * math.copysign(abs(s_) ** (2 / e), s_)))
    return pts


def place(pos, yaw=0.0, lean=0.0):
    """A frame at pos turned yaw degrees about the up axis and tipped lean degrees about its own x first."""
    return mat4(pos, (1, 1, 1), (lean, yaw, 0))


def snowboard(mat, pos, yaw=0.0, lean=-8.0, h=1.5, bindings=False):
    """A snowboard standing nose up (pos its middle), leaning back about it: its outline rounded at nose and tail and
    narrower at the waist, the nose and tail kicked up toward its top sheet (which faces the way it faces), 12 mm thick."""
    W, th = 0.29, 0.006
    st = [0.0, 0.03, 0.11, 0.3, 0.5, 0.7, 0.89, 0.97, 1.0]
    rings = []
    for t in st:
        if t < 0.11 or t > 0.89:
            u = (0.11 - t) / 0.11 if t < 0.11 else (t - 0.89) / 0.11
            hw = max(0.025, W / 2 * math.sqrt(max(0.0, 1 - u * u)))
            kz = 0.045 * u * u
        else:
            c = (t - 0.5) / 0.39
            hw = W / 2 - 0.02 * (1 - c * c)   # (the sidecut: 25 cm at the waist)
            kz = 0.0
        y = (t - 0.5) * h
        rings.append([(-hw, y, kz - th), (hw, y, kz - th), (hw, y, kz + th), (-hw, y, kz + th)])
    m = place(pos, yaw, lean)
    loft(mat, rings, m, smooth=False)
    if bindings:
        for yb in (-0.14 * h, 0.12 * h):
            # (a binding: its base plate on the top sheet and a highback standing off it)
            for (c, s) in (((0, yb, 0.03), (0.22, 0.17, 0.035)), ((0, yb - 0.07, 0.11), (0.2, 0.03, 0.15))):
                box("Charcoal", tuple(m @ Vector(c)), s, (lean, yaw, 0))


def ski(mat, pos, yaw=0.0, lean=-6.0, L=1.8, bind=True):
    """A ski standing tip up (pos its middle), the tip curling out toward its top: waisted, its tip rounded."""
    th = 0.007
    st = [0.0, 0.04, 0.5, 0.8, 0.9, 0.96, 1.0]
    rings = []
    for t in st:
        hw = 0.04 if t < 0.04 else 0.033 + 0.008 * abs(t - 0.5) / 0.38 if t <= 0.88 else 0.044 * math.sqrt(max(0.06, 1 - ((t - 0.88) / 0.12) ** 2))
        u = max(0.0, (t - 0.8) / 0.2)
        kz = 0.07 * u * u
        y = (t - 0.5) * L - 0.03 * u * u
        rings.append([(-hw, y, kz - th), (hw, y, kz - th), (hw, y, kz + th), (-hw, y, kz + th)])
    m = place(pos, yaw, lean)
    loft(mat, rings, m, smooth=False)
    if bind:
        for (c, sz, mt) in (((0, -0.08, 0.02), (0.09, 0.12, 0.03), "Steel"), ((0, 0.12, 0.025), (0.088, 0.1, 0.04), "Charcoal")):   # (wider than the ski: not in the planes of its edges)
            rbox(mt, tuple(m @ Vector(c)), sz, (lean, yaw, 0), 0.006)


def jacket(col, hook, yaw=0.0, hood=True, hanger=True, s=1.0):
    """A jacket on a hanger hung from a rail at hook (the rail's middle): its body lofted from collar to hem, sleeves
    hanging at its sides, a hood rolled at its neck, the zip down its front (which faces +z turned by yaw)."""
    m = place(hook, yaw) @ Matrix.Translation(Vector((0, -0.12 * s, 0))) @ Matrix.Diagonal(Vector((s, s, s, 1)))
    rings = [ring_y(y, w, d, 8, 3.2) for (y, w, d) in ((0.035, 0.17, 0.12), (-0.03, 0.40, 0.15), (-0.12, 0.47, 0.18), (-0.42, 0.44, 0.17), (-0.66, 0.45, 0.16), (-0.74, 0.46, 0.15), (-0.755, 0.41, 0.11))]
    loft(col, rings, m)
    for sx in (-1, 1):
        pts = [tuple(m @ Vector(p)) for p in ((sx * 0.205, -0.07, -0.01), (sx * 0.235, -0.34, 0.01), (sx * 0.232, -0.62, 0.035))]
        tube(col, pts, [0.062 * s, 0.052 * s, 0.046 * s], 5)
    if hood:
        b = bmesh.new()
        bmesh.ops.create_uvsphere(b, u_segments=6, v_segments=3, radius=0.5)
        for f in b.faces:
            f.smooth = True
        add(b, col, m @ mat4((0, 0.01, -0.085), (0.24, 0.11, 0.14)))
    box("Charcoal", tuple(m @ Vector((0, -0.4, 0.086))), (0.012 * s, 0.68 * s, 0.006 * s), (0, yaw, 0))
    if hanger:
        pts = [(-0.19, -0.03, 0), (0, 0.05, 0), (0.19, -0.03, 0)]
        tube("Steel", [tuple(m @ Vector(p)) for p in pts], 0.005 * s, 4, smooth=False)
        hookp = [(0, 0.05, 0), (0, 0.098, 0)] + [(0.022 * math.cos(a), 0.12 + 0.022 * math.sin(a), 0) for a in (math.radians(-60), math.radians(0), math.radians(60), math.radians(120), math.radians(170))]
        tube("Steel", [tuple(m @ Vector(p)) for p in hookp], 0.004 * s, 4, smooth=False)


def boot(pos, yaw=0.0, col="Charcoal", strap="Red"):
    """A ski or snowboard boot (pos the middle of its sole), toes the way it faces: a thick sole, a high padded cuff, a strap."""
    m = place(pos, yaw)
    rings = [ring_y(y, w, l, 8, 3.0, cz) for (y, w, l, cz) in ((0.0, 0.11, 0.31, 0.0), (0.035, 0.115, 0.31, 0.0), (0.12, 0.1, 0.22, -0.04), (0.25, 0.105, 0.15, -0.07), (0.3, 0.1, 0.14, -0.075))]
    loft(col, rings, m)
    box(strap, tuple(m @ Vector((0, 0.2, -0.07))), (0.112, 0.035, 0.155), (0, yaw, 0))


def helmet_m(pos, col, yaw=0.0, goggles=True, lens="Navy"):
    """A ski helmet (pos the middle of its rim), its shell rounded and trimmed flat at the rim, three vents on top,
    goggles round it on their strap, the lens to the front (+z turned by yaw)."""
    m = place(pos, yaw)
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=12, v_segments=6, radius=0.5)
    for v in b.verts:
        v.co = Vector((v.co.x * 0.25, max(v.co.y, -0.12) * 0.42 + 0.05, v.co.z * 0.3))   # (its lower third trimmed flat: the rim)
    for f in b.faces:
        f.smooth = True
    add(b, col, m)
    for k in (-1, 0, 1):
        rbox("Charcoal", tuple(m @ Vector((k * 0.045, 0.255, -0.01))), (0.018, 0.012, 0.11), (0, yaw, 0), 0.004)
    if goggles:
        strap = [(0.128 * math.cos(a), 0.1, 0.152 * math.sin(a)) for a in [math.radians(d) for d in range(-60, 241, 50)]]
        tube("Charcoal", [tuple(m @ Vector(p)) for p in strap], 0.012, 4, caps=True, smooth=False)
        b = bmesh.new()
        bmesh.ops.create_uvsphere(b, u_segments=8, v_segments=4, radius=0.5)
        for f in b.faces:
            f.smooth = True
        add(b, lens, m @ mat4((0, 0.105, 0.14), (0.2, 0.075, 0.06)))


def goggles(pos, yaw=0.0, lens="Navy", frame="Charcoal"):
    """Goggles on a little steel stand (pos its foot): the lens round, its frame and strap behind it."""
    m = place(pos, yaw)
    cyl("Steel", tuple(m @ Vector((0, 0, 0))), 0.035, 0.01, 10)
    tube("Steel", [tuple(m @ Vector(p)) for p in ((0, 0.01, 0), (0, 0.12, 0))], 0.004, 4, smooth=False)
    strap = [(0.085 * math.cos(a), 0.15, -0.02 + 0.06 * math.sin(a)) for a in [math.radians(d) for d in range(-10, 191, 50)]]
    tube(frame, [tuple(m @ Vector(p)) for p in strap], 0.009, 4, smooth=False)
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=8, v_segments=4, radius=0.5)
    for f in b.faces:
        f.smooth = True
    add(b, lens, m @ mat4((0, 0.15, 0.03), (0.19, 0.075, 0.05)))


def beanie(pos, col="Red", bobble="Cream"):
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=8, v_segments=4, radius=0.5)
    for v in b.verts:
        v.co = Vector((v.co.x * 0.2, max(v.co.y, 0.0) * 0.26, v.co.z * 0.2))
    for f in b.faces:
        f.smooth = True
    add(b, col, mat4(pos))
    sphere(bobble, (pos[0], pos[1] + 0.135, pos[2]), (0.07, 0.07, 0.07), 6)


def folded(pos, cols, yaw=0.0, w=0.32, d=0.3):
    """A stack of folded tops or fleeces, each rounded at its folds, a little askew."""
    y = pos[1]
    for k, c in enumerate(cols):
        h = 0.055
        rbox(c, (pos[0] + 0.006 * ((k * 7) % 3 - 1), y + h / 2, pos[2] + 0.005 * ((k * 5) % 3 - 1)), (w, h, d), (0, yaw + ((k * 13) % 5 - 2), 0), 0.018, 1)
        y += h


def pos_till(pos, yaw=0.0):
    """A shop's point of sale on a counter (pos on its top): a screen tilted on a steel stand, a card reader, a printer."""
    m = place(pos, yaw)
    cyl("Steel", tuple(m @ Vector((0, 0, 0))), 0.09, 0.012, 14)
    tube("Steel", [tuple(m @ Vector(p)) for p in ((0, 0.01, 0), (0, 0.16, -0.02))], 0.018, 8)
    rbox("Charcoal", tuple(m @ Vector((0, 0.27, 0.0))), (0.34, 0.23, 0.025), (-18, yaw, 0), 0.008)
    rbox("Steel", tuple(m @ Vector((0, 0.27, -0.016))), (0.3, 0.19, 0.01), (-18, yaw, 0), 0.004)
    rbox("Charcoal", tuple(m @ Vector((0.28, 0.035, 0.06))), (0.08, 0.06, 0.15), (-25, yaw, 0), 0.01, 1)
    rbox("White", tuple(m @ Vector((-0.28, 0.05, 0.0))), (0.13, 0.1, 0.16), (0, yaw, 0), 0.015, 1)


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
    # (the cube's top, chamfered, up the game's y; turned, not mirrored: a mirror here turned the pad inside out)
    add(b, "Snow", mat4(pos) @ Matrix(((size[0], 0, 0, 0), (0, 0, size[1], 0), (0, -size[2], 0, 0), (0, 0, 0, 1))))


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
MY = 3.8              # the upper floor's top (the loft's; 3.5 m clear under it)
WY = MY - RT          # the wing's soffit (its ceiling): its roof is the lookout's deck, level with the loft's floor
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
# the high end: open to the wing below its roof (the wing's ceiling level with the upper floors' undersides); above
# it glass from the upper floors' level to the flat roof, onto the lookout's deck on the wing's roof, with sliding
# doors out onto it in the middle (open: both leaves slid aside over the panes beside them); boarded either side
WOPEN = (-4.6, 4.1, WY)
DOORW = (-0.95, 0.95, MY + 2.3)   # the doors out to the deck: z0, z1, their head
wall("z", XH, 1, -D, D, lambda z: H, [(WOPEN[0], WOPEN[1], 0.0, WOPEN[2]), (-5.6, 5.6, MY, 9.4)])
glazing("z", XH + 0.15, -5.6, DOORW[0], lambda a: 9.4, [-3.27], [DOORW[2]], y0=MY)
glazing("z", XH + 0.15, DOORW[1], 5.6, lambda a: 9.4, [3.27], [DOORW[2]], y0=MY)
pane("z", XH + 0.15, DOORW[0], DOORW[1], DOORW[2] + 0.04, 9.4 - 0.04, 9.4 - 0.04)
transom("z", XH + 0.15, DOORW[0], DOORW[1], DOORW[2], 0.1, 0.18)
solid("Steel", [(XH + 0.07, 9.31, DOORW[0] + 0.005), (XH + 0.07, 9.31, DOORW[1] - 0.005), (XH + 0.23, 9.31, DOORW[1] - 0.005), (XH + 0.23, 9.31, DOORW[0] + 0.005)],
      [(XH + 0.07, 9.4, DOORW[0] + 0.005), (XH + 0.07, 9.4, DOORW[1] - 0.005), (XH + 0.23, 9.4, DOORW[1] - 0.005), (XH + 0.23, 9.4, DOORW[0] + 0.005)])
lohi("Steel", (XH - 0.02, MY - 0.03, -5.6), (XH + T + 0.02, MY + 0.01, 5.6))   # the sill all along, the doors' threshold
for s_, a in ((-1, DOORW[0]), (1, DOORW[1])):
    # a leaf slid aside: a black frame round its glass, just outside the fixed panes
    b0, b1 = (a - 0.97, a + 0.02) if s_ < 0 else (a - 0.02, a + 0.97)
    lohi("Steel", (XH + 0.02, MY + 0.01, b0), (XH + 0.08, MY + 2.27, b0 + 0.07))
    lohi("Steel", (XH + 0.02, MY + 0.01, b1 - 0.07), (XH + 0.08, MY + 2.27, b1))
    lohi("Steel", (XH + 0.02, MY + 0.01, b0 + 0.07), (XH + 0.08, MY + 0.1, b1 - 0.07))
    lohi("Steel", (XH + 0.02, MY + 2.19, b0 + 0.07), (XH + 0.08, MY + 2.27, b1 - 0.07))
    pane("z", XH + 0.05, b0 + 0.07, b1 - 0.07, MY + 0.1, MY + 2.19, MY + 2.19, 0.012, "GlassIn")
    lohi("Steel", (XH + 0.08, MY + 0.95, b1 - 0.16 if s_ < 0 else b0 + 0.12), (XH + 0.11, MY + 1.35, b1 - 0.12 if s_ < 0 else b0 + 0.16))   # its pull
lohi("Glulam", (XH - 0.02, WOPEN[2] - 0.45, WOPEN[0]), (XH + T, WOPEN[2], WOPEN[1]))   # a glulam lintel over the opening (flush inside: an upper floor meets it there)


def casing(axis, fixed, inward, a0, a1, y0, y1, glass=0.235):
    """Oak round a window's opening inside: a sill standing a little proud of the wall, a head and two jambs lining the
    opening's sides from the glass's frame to the wall's inside face (a wall along x at z=fixed, or along z at x=fixed,
    inward toward the inside; the opening a0..a1 along it, y0..y1)."""
    def box_ad(ya, yb, aa, ab, da, db):
        p = (aa, ya, fixed + inward * da) if axis == "x" else (fixed + inward * da, ya, aa)
        q = (ab, yb, fixed + inward * db) if axis == "x" else (fixed + inward * db, yb, ab)
        lohi("Oak", tuple(min(p[i], q[i]) for i in range(3)), tuple(max(p[i], q[i]) for i in range(3)))
    box_ad(y0 - 0.03, y0 + 0.025, a0 - 0.06, a1 + 0.06, glass, T + 0.06)
    box_ad(y1 - 0.025, y1, a0, a1, glass, T + 0.02)
    box_ad(y0 + 0.025, y1 - 0.025, a0, a0 + 0.025, glass, T + 0.02)
    box_ad(y0 + 0.025, y1 - 0.025, a1 - 0.025, a1, glass, T + 0.02)


casing("z", XL, -1, -2.6, 1.6, 0.9, 2.7)            # the low end's window by the side door
casing("x", -D, 1, 7.8, 10.2, 1.4, 2.7)             # the back wall's over the tuning bench

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
glazing("x", WZ1 - 0.12, XW, XH, lambda a: WY - 0.05, [XW + 5.0 / 3, XW + 10.0 / 3], [2.7])
wall("z", XW, 1, WZ0, WZ1, lambda z: WY, [(-2.6, 2.4, 0.9, 2.9)])
glazing("z", XW + 0.15, -2.6, 2.4, lambda a: 2.9, [-0.1], [], y0=0.9)
casing("z", XW, 1, -2.6, 2.4, 0.9, 2.9)              # the café's end window
wall("x", WZ0, 1, XW, XH, lambda x: WY, [])

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
# the wing's: level, its top the lookout's deck (boards over the roof, level with the floors inside: MY), deep at the
# front over the terrace
WRX0, WRZ0, WRZ1 = XW - 1.2, WZ0 - 0.6, D - 0.1
DK = 0.03   # the deck's boards


def wing_under(z):
    return WY


lohi("Soffit", (WRX0, WY, WRZ0), (XH, WY + 0.05, WRZ1))
lohi("Roof", (WRX0, WY + 0.05, WRZ0), (XH, MY - DK, WRZ1))
lohi("Decking", (WRX0, MY - DK, WRZ0), (XH, MY, WRZ1))
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


# ---------------------------------------------------------------- the lookout: a deck on the wing's roof

# Out of the loft's doors in the high end's glass: the wing's roof is a deck at the upper floors' level, glass rails round its
# three open sides (black posts, a black top rail with snow along it), the roof's edge outside them under a rounded
# load of snow, drifts in its corners. At the front rail, looking down the mountain past the gate: an oak bar ledge
# with stools, the telescope at its far corner; a binocular viewer on the end rail; two lounge chairs with sheepskins
# and a fire bowl by the back rail; potted firs and lanterns either side of the doors; a shovel in the corner.
# Standing still at the front (LOOK_deck) the camera stands back and takes in the mountain, as at the camp's lookout.
RI = 0.3                                     # the rails set in from the roof's edges
DX0, DZ0, DZ1 = WRX0 + RI, WRZ0 + RI, WRZ1 - RI   # the rails' lines: the end, the back, the front


def glass_rail(axis, fixed, a0, a1, y=0.0, name="deckrail", posts=1.6, snowy=True):
    """A glass balustrade along x (at z=fixed) or along z (at x=fixed): black posts, glass between them, a black top
    rail, snow lying along its top; the walker kept in."""
    n = max(1, int(math.ceil((a1 - a0) / posts)))
    xs = [a0 + (a1 - a0) * i / n for i in range(n + 1)]

    def P(a, yy, d=0.0):
        return (a, yy, fixed + d) if axis == "x" else (fixed + d, yy, a)

    def bx(mat, a, b, ya, yb, d0, d1):
        p, q = P(a, ya, d0), P(b, yb, d1)
        lohi(mat, (min(p[0], q[0]), ya, min(p[2], q[2])), (max(p[0], q[0]), yb, max(p[2], q[2])))
    for i, a in enumerate(xs):
        a = min(max(a, a0 + 0.03), a1 - 0.03)
        bx("Steel", a - 0.03, a + 0.03, y, y + 1.0, -0.03, 0.03)
    for a, b in zip(xs, xs[1:]):
        pane(axis, fixed, a + 0.035, b - 0.035, y + 0.05, y + 0.97, y + 0.97, 0.02, "GlassIn")
    bx("Steel", a0, a1, y + 1.0, y + 1.05, -0.035, 0.035)
    if snowy:
        for k, (a, b) in enumerate(zip(xs, xs[1:])):
            hh = 0.05 + 0.025 * ((k * 7) % 3) / 2
            c = P((a + b) / 2, y + 1.05 + hh / 2)
            sz = (b - a - 0.04, hh, 0.1) if axis == "x" else (0.1, hh, b - a - 0.04)
            pillow(c, sz, 0.035)
    lo, hi = P(a0, y, -0.12), P(a1, y + 1.1, 0.12)
    helper("BLK_" + name, (min(lo[0], hi[0]), y, min(lo[2], hi[2])), (max(lo[0], hi[0]), y + 1.1, max(lo[2], hi[2])))


def lounger(x, z, yaw, y=0.0):
    """An outdoor lounge chair (a slatted oak seat and back on a black frame, low and raked), a sheepskin on it."""
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def P(u, h, v):
        return (x + u * c + v * s_, y + h, z - u * s_ + v * c)
    for su in (-1, 1):
        box("Steel", P(su * 0.33, 0.2, -0.05), (0.04, 0.4, 0.86), (0, yaw, 0))   # (its sides: legs and arm in one)
        box("Steel", P(su * 0.33, 0.6, 0.0), (0.09, 0.03, 0.62), (0, yaw, 0))
    box("Glulam", P(0, 0.34, 0.08), (0.62, 0.05, 0.6), (-6, yaw, 0))
    box("Glulam", P(0, 0.66, -0.36), (0.62, 0.6, 0.05), (18, yaw, 0))
    box("White", P(0, 0.4, 0.02), (0.5, 0.05, 0.62), (-6, yaw, 0))   # the sheepskin over seat
    box("White", P(0, 0.72, -0.32), (0.48, 0.48, 0.05), (18, yaw, 0))   # and back
    helper("BLK_lounger", (x - 0.48, y, z - 0.48), (x + 0.48, y + 0.9, z + 0.48))


def fire_bowl(x, z, y=0.0):
    """A low black steel fire bowl on a stone plinth, its embers glowing (flames in it: FX_fire)."""
    lohi("Hearth", (x - 0.38, y, z - 0.38), (x + 0.38, y + 0.18, z + 0.38))
    cyl("Steel", (x, y + 0.18, z), 0.22, 0.2, 16, r2=0.42)
    cyl("Ember", (x, y + 0.33, z), 0.36, 0.03, 16)
    marker("FX_fire", (x, y + 0.42, z))
    helper("BLK_firebowl", (x - 0.45, y, z - 0.45), (x + 0.45, y + 0.5, z + 0.45))


def viewer(x, z, yaw, y=0.0):
    """A coin-op binocular viewer on its post, looking out the way yaw faces."""
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    cyl("Steel", (x, y, z), 0.2, 0.04, 12)
    cyl("Navy", (x, y + 0.04, z), 0.06, 1.05, 10)
    box("Navy", (x, y + 1.25, z), (0.36, 0.26, 0.3), (-8, yaw, 0))
    for su in (-1, 1):
        cyl("Steel", (x + su * 0.09 * c + 0.17 * s_, y + 1.19, z - su * 0.09 * s_ + 0.17 * c), 0.05, 0.12, 10)
        box("Charcoal", (x + su * 0.09 * c - 0.17 * s_, y + 1.29, z - su * 0.09 * s_ - 0.17 * c), (0.08, 0.06, 0.06), (0, yaw, 0))
    pillow((x, y + 1.4, z), (0.32, 0.05, 0.26), 0.04)
    helper("BLK_viewer", (x - 0.25, y, z - 0.25), (x + 0.25, y + 1.45, z + 0.25))


def potted_fir(x, z, hgt=1.5, y=0.0):
    """A small fir in a black planter, snow on its tiers."""
    lohi("Steel", (x - 0.34, y, z - 0.34), (x + 0.34, y + 0.5, z + 0.34))
    lohi("Snow", (x - 0.3, y + 0.46, z - 0.3), (x + 0.3, y + 0.52, z + 0.3))
    cyl("Bark", (x, y + 0.5, z), 0.04, 0.3, 6)
    for k in range(3):
        yb = y + 0.68 + k * hgt * 0.26
        r = 0.42 * (1 - k * 0.28)
        th = hgt * 0.34
        cyl("Fir", (x, yb, z), r, th, 12, r2=0.02)
        # snow lying on the tier's shoulders: a band standing just proud of its slope
        cyl("Snow", (x, yb + th * 0.42, z), r * 0.6 + 0.02, th * 0.26, 12, r2=r * 0.34 + 0.015)
    helper("BLK_fir", (x - 0.38, y, z - 0.38), (x + 0.38, y + hgt + 0.6, z + 0.38))


def lantern(x, z, y=0.0, h=0.55):
    """A black lantern standing on the deck, a warm light in it."""
    lohi("Steel", (x - 0.13, y, z - 0.13), (x + 0.13, y + 0.04, z + 0.13))
    for (sx, sz) in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        lohi("Steel", (x + sx * 0.12 - 0.015, y, z + sz * 0.12 - 0.015), (x + sx * 0.12 + 0.015, y + h, z + sz * 0.12 + 0.015))
    cyl("Steel", (x, y + h, z), 0.16, 0.08, 4, r2=0.02)
    cyl("LED", (x, y + 0.06, z), 0.05, h * 0.45, 8)
    helper("BLK_lantern", (x - 0.16, y, z - 0.16), (x + 0.16, y + h + 0.1, z + 0.16))


def deck():
    # the edges outside the rails, under snow; the rails; drifts in the corners
    snow(WRX0 - 0.07, XH - 0.02, DZ1 + 0.03, WRZ1 + 0.08, lambda x, z: MY, 0.3, r=0.13, lip=0.55, drape=0.03, step=2.6, fine=False)
    snow(WRX0 - 0.08, DX0 - 0.03, WRZ0 - 0.07, WRZ1 + 0.07, lambda x, z: MY, 0.3, r=0.13, lip=0.55, drape=0.03, step=2.6, fine=False)
    snow(WRX0 - 0.07, XH - 0.02, WRZ0 - 0.08, DZ0 - 0.03, lambda x, z: MY, 0.3, r=0.13, lip=0.55, drape=0.03, step=2.6, fine=False)
    glass_rail("x", DZ1, DX0, XH - 0.02, MY)
    glass_rail("z", DX0, DZ0, DZ1, MY)
    glass_rail("x", DZ0, DX0, XH - 0.02, MY)
    snow(DX0 + 0.06, DX0 + 1.5, DZ0 + 0.06, DZ0 + 1.3, lambda x, z: MY, 0.34, r=0.65, lip=0.12, drape=0.02, step=0.7, fine=False)
    snow(DX0 + 0.06, DX0 + 0.8, DZ1 - 0.9, DZ1 - 0.06, lambda x, z: MY, 0.2, r=0.38, lip=0.12, drape=0.02, step=0.7, fine=False)
    snow(XH - 1.7, XH - 0.04, DZ0 + 0.06, DZ0 + 0.7, lambda x, z: MY, 0.22, r=0.33, lip=0.15, drape=0.02, step=0.8, fine=False)
    helper("BLK_drift", (DX0, MY, DZ0), (DX0 + 1.45, MY + 0.4, DZ0 + 1.25))
    helper("BLK_drift", (XH - 1.7, MY, DZ0), (XH - 0.04, MY + 0.3, DZ0 + 0.72))
    # the front rail: an oak ledge inside it at bar height, black brackets, four stools; the telescope at its far end
    lohi("Oak", (DX0 + 0.9, MY + 1.02, DZ1 - 0.36), (XH - 1.3, MY + 1.07, DZ1 - 0.06))
    for bx_ in (DX0 + 1.1, (DX0 + XH) / 2 - 0.2, XH - 1.5):
        lohi("Steel", (bx_ - 0.02, MY + 0.72, DZ1 - 0.3), (bx_ + 0.02, MY + 1.02, DZ1 - 0.05))
    helper("BLK_ledge", (DX0 + 0.88, MY + 0.85, DZ1 - 0.38), (XH - 1.28, MY + 1.1, DZ1))
    for i in range(4):
        stool(DECK_STOOLS[i], DZ1 - 0.81, y=MY)
    telescope(DX0 + 0.42, DZ1 - 0.5, MY, yaw=-12)
    viewer(DX0 + 0.38, 2.6, -55, MY)   # (aimed out over the front's far corner, at the peak)
    # by the back rail: two lounge chairs with sheepskins, angled to the view, a fire bowl between them
    lounger(DX0 + 1.0, -2.1, 25, MY)
    lounger(DX0 + 2.55, -2.7, 5, MY)
    fire_bowl(DX0 + 1.85, -1.0, MY)
    # either side of the doors: potted firs, lanterns; lights on the wall over them
    for zz in (DOORW[0] - 1.55, DOORW[1] + 1.55):
        potted_fir(XH - 0.45, zz, 1.4, MY)
        lohi("Steel", (XH - 0.08, MY + 2.35, zz - 0.1), (XH, MY + 2.6, zz + 0.1))
        lohi("LED", (XH - 0.1, MY + 2.37, zz - 0.07), (XH - 0.08, MY + 2.58, zz + 0.07))
    lantern(XH - 0.4, DOORW[0] - 0.4, MY)
    lantern(XH - 0.4, DOORW[1] + 0.4, MY)
    # a snow shovel against the back rail, by the shovelled pile
    span("Bark", (XH - 2.1, MY + 0.05, DZ0 + 0.14), (XH - 2.25, MY + 1.15, DZ0 + 0.08), 0.035)
    box("Red", (XH - 2.08, MY + 0.17, DZ0 + 0.17), (0.4, 0.3, 0.03), (-15, 0, 0))
    # the deck: walked on (inside the rails), its lookout at the front, its lamp for the evening
    helper("FLR_deck", (DX0 - 0.1, MY - 0.3, DZ0 - 0.1), (XH, MY, DZ1 + 0.1))
    helper("LOOK_deck", (DX0, MY, 1.2), (XH - 0.6, MY + 2.0, DZ1))
    marker("LIGHT_deck", (XH - 2.5, MY + 2.4, 0.0))


DECK_STOOLS = (DX0 + 1.35, DX0 + 2.2, DX0 + 3.05, DX0 + 3.9)

# ---------------------------------------------------------------- snow on the roofs

# thick, rounded, standing a hand's width proud of the charcoal edges (the photo's heavy loads)
snow(SX0 + 0.05, SX1 + 0.08, ZC + 0.25, SZ1 + 0.1, lambda x, z: ru(x) + RT, 0.62, r=0.7, xbreaks=(XL,))
snow(XB + 0.3, SX1 + 0.08, SZ0 - 0.08, ZC - 0.05, lambda x, z: ru(x) + RT, 0.62, r=0.7, xbreaks=(XL,))
snow(FX0 - 0.08, FX1 + 0.08, FZ0 - 0.08, FZ1 + 0.1, lambda x, z: H + RT, 0.7, r=0.75)
# (the wing's roof: the lookout's deck, its snow along its edges: deck(), below)
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


PILES = []   # each bank's footprint in the lodge's frame (x0, z0, x1, z1): its surface exported for walking (pile_grids)


def bank(axis, fixed, out, a0, a1, ground, rise=0.55, reach=2.2, step=1.0):
    """Snow banked against a wall (along x at z=fixed, or along z at x=fixed), out the way it faces, from a0 to a1:
    `rise` above the ground at the wall, falling away over `reach`, its ends sunk into the snow."""
    d0, d1 = fixed + out * -0.05, fixed + out * reach
    PILES.append((a0, min(d0, d1), a1, max(d0, d1)) if axis == "x" else (min(d0, d1), a0, max(d0, d1), a1))
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
    add(b, "SnowPile", Matrix.Identity(4))   # (a mesh of its own: the game walks you up it, not through it: CampSnow.cs)


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
            snowboard(sw, (x, 0.85, z + face * 0.04), 0 if face > 0 else 180, -8, 1.5)   # a board, nose up
        else:
            for s in (-1, 1):   # a pair of skis
                ski(sw, (x + s * 0.05, 0.95, z + face * 0.05), 0 if face > 0 else 180, -6, 1.75, bind=False)
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

# ---------------------------------------------------------------- inside (lodge.md): its own file, LodgeInside.fbx

# The shell above is Lodge.fbx. The inside's furnishings, stations, lamps and walking are LodgeInside.fbx (written at
# the end), and the game puts it in the shell: the outfitters up in the loft (the wardrobe: the closet), the board shop
# (its counter: the upgrades), the wax and tuning bench (the upgrades too), the missions board, the lounge and its
# fire, the hot-chocolate café in the wing (the hub session's station: Camp.LodgeCocoaAt) and plants and lamps.
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


def board(mat, x, y, z, yaw=0.0, lean=-8.0, h=1.5, bindings=False):
    """A snowboard standing nose up (its base at height y), leaning back from the way it faces (snowboard)."""
    snowboard(mat, (x, y + h / 2, z), yaw, lean, h, bindings)


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


def stool(x, z, h=0.75, y=0.0):
    cyl("Steel", (x, y, z), 0.2, 0.02, 10)
    cyl("Steel", (x, y, z), 0.025, h, 6)
    cyl("Glulam", (x, y + h, z), 0.19, 0.05, 12)
    helper("BLK_stool", (x - 0.22, y, z - 0.22), (x + 0.22, y + h + 0.05, z + 0.22))


def helmet(x, y, z, mat, yaw=0.0, goggles=True):
    """A helmet sitting on its rim at height y (helmet_m: a shell, vents, goggles round it)."""
    helmet_m((x, y, z), mat, yaw, goggles)


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
    """A clothes rail along x at z: a round black steel bar on two posts with flat feet, jackets on hangers along it,
    shoulders across the rail as a shop hangs them, a hood here and there."""
    top = y0 + 2.07 - 0.3 * two_sided
    tube("Steel", [(x0 - 0.02, top, z), (x1 + 0.02, top, z)], 0.016, 8)
    for x in (x0, x1):
        tube("Steel", [(x, y0 + 0.03, z), (x, top, z)], 0.018, 8)
        rlohi("Steel", (x - 0.035, y0, z - 0.3), (x + 0.035, y0 + 0.035, z + 0.3), 0.012)
    n = int((x1 - x0 - 0.3) / 0.23) + 1
    for k in range(n):
        x = x0 + 0.15 + k * (x1 - x0 - 0.3) / max(1, n - 1)
        jacket(JACKETS[(k * 5) % len(JACKETS)], (x, top, z), 90, hood=k % 3 == 0)
    if blk:
        helper("BLK_rail", (x0 - 0.05, y0, z - 0.32), (x1 + 0.05, y0 + 2.1, z + 0.32))


def display_table(tx, tz, y0=0.0, along_x=True):
    """A display table: a thick oak top rounded at its edges on black steel sled legs; stacks of folded fleeces and tops,
    a beanie and goggles on it."""
    w, d = (0.8, 0.4) if along_x else (0.4, 0.8)
    rlohi("Oak", (tx - w, y0 + 0.72, tz - d), (tx + w, y0 + 0.785, tz + d), 0.02, 2)
    for sgn in (-1, 1):
        if along_x:
            ex = tx + sgn * (w - 0.14)
            pts = [(ex, y0 + 0.016, tz - d + 0.08), (ex, y0 + 0.704, tz - d + 0.08), (ex, y0 + 0.704, tz + d - 0.08), (ex, y0 + 0.016, tz + d - 0.08), (ex, y0 + 0.016, tz - d + 0.08)]
        else:
            ez = tz + sgn * (d - 0.14)
            pts = [(tx - w + 0.08, y0 + 0.016, ez), (tx - w + 0.08, y0 + 0.704, ez), (tx + w - 0.08, y0 + 0.704, ez), (tx + w - 0.08, y0 + 0.016, ez), (tx - w + 0.08, y0 + 0.016, ez)]
        tube("Steel", pts, 0.016, 6, caps=False)
    def at(o):
        return (tx + (o if along_x else 0), y0 + 0.785, tz + (0 if along_x else o))
    folded(at(-0.52), ("Navy", "Red", "Navy"), 0 if along_x else 90)
    folded(at(-0.14), ("Fabric", "ArtB"), 0 if along_x else 90)
    beanie(at(0.22), "Red")
    goggles(at(0.52), 0 if along_x else 90, "ArtA")
    helper("BLK_table", (tx - w - 0.02, y0, tz - d - 0.02), (tx + w + 0.02, y0 + 0.9, tz + d + 0.02))


def till(x0, z0, x1, z1, y0=0.0):
    """The outfitters' till: an oak block fluted down its front on a dark recessed plinth, a pale stone top with
    rounded edges, the point of sale on it."""
    rlohi("Steel", (x0 + 0.05, y0, z0 + 0.05), (x1 - 0.05, y0 + 0.1, z1 - 0.05), 0.006)
    rlohi("Oak", (x0, y0 + 0.1, z0), (x1, y0 + 1.0, z1), 0.012)
    n = int((x1 - x0 - 0.08) / 0.07)
    for k in range(n):
        x = x0 + 0.06 + k * (x1 - x0 - 0.12) / max(1, n - 1)
        box("Oak", (x, y0 + 0.55, z0 - 0.012), (0.034, 0.84, 0.026))
    rlohi("CounterTop", (x0 - 0.05, y0 + 1.0, z0 - 0.06), (x1 + 0.05, y0 + 1.05, z1 + 0.05), 0.014, 2)
    pos_till(((x0 + x1) / 2, y0 + 1.05, (z0 + z1) / 2 + 0.05), 0)
    helper("BLK_till", (x0 - 0.05, y0, z0 - 0.06), (x1 + 0.05, y0 + 1.2, z1 + 0.05))


def mannequin(mx, mz, col, y0=0.0, yaw=180.0, strap="Red"):
    """A shop's mannequin in an outfit: a white abstract head and hands, a jacket (col) over its torso and arms, dark
    trousers, boots; on a round steel base, held up by a rod to its calf; facing +z turned by yaw."""
    m = place((mx, y0, mz), yaw)

    def P(x, y, z):
        return tuple(m @ Vector((x, y, z)))
    cyl("Steel", P(0, 0, 0), 0.22, 0.02, 18)
    tube("Steel", [P(0.09, 0.02, -0.03), P(0.09, 0.42, -0.03)], 0.011, 6)
    for sx in (-1, 1):
        tube("Charcoal", [P(sx * 0.09, 0.93, 0.0), P(sx * 0.095, 0.52, 0.012), P(sx * 0.088, 0.16, 0.0)], [0.08, 0.06, 0.05], 8)
        boot(P(sx * 0.09, 0.02, 0.02), yaw, "Charcoal", strap)
    rings = [ring_y(y, w, d, 10, 3.0) for (y, w, d) in ((0.83, 0.40, 0.26), (0.9, 0.41, 0.27), (1.06, 0.37, 0.23), (1.28, 0.43, 0.25), (1.41, 0.45, 0.21), (1.47, 0.30, 0.15), (1.5, 0.15, 0.12))]
    loft(col, rings, m)
    rbox("Charcoal", P(0, 1.14, 0.122), (0.012, 0.56, 0.006), (0, yaw, 0), 0.002)
    for sx in (-1, 1):
        tube(col, [P(sx * 0.215, 1.41, 0.0), P(sx * 0.252, 1.15, 0.02), P(sx * 0.238, 0.93, 0.05)], [0.066, 0.056, 0.049], 8)
        box("White", P(sx * 0.238, 0.87, 0.055), (0.06, 0.1, 0.07), (0, yaw, 0))
    cyl("White", P(0, 1.48, 0), 0.05, 0.1, 10)
    sphere("White", P(0, 1.68, 0.01), (0.19, 0.25, 0.21), 8)
    helper("BLK_mannequin", (mx - 0.28, y0, mz - 0.24), (mx + 0.28, y0 + 1.85, mz + 0.24))


def mirror(z0, z1, y0=0.0):
    """A long mirror on the high end's wall (inside face), between z0 and z1."""
    lohi("Steel", (XH + T, y0, z0 - 0.05), (XH + T + 0.05, y0 + 2.2, z1 + 0.05))
    pane("z", XH + T + 0.07, z0, z1, y0 + 0.05, y0 + 2.15, y0 + 2.15, 0.02, "Mirror")
    helper("BLK_mirror", (XH + T, y0, z0 - 0.05), (XH + T + 0.1, y0 + 2.2, z1 + 0.05))


def hanging_sign(row, x, y, z, w, h, ceiling, yaw=0):
    """A sign hung on two steel cables from the ceiling(x) in a thin rounded steel frame, facing +z turned by yaw, the
    same on its back."""
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for u in (-w / 2 + 0.15, w / 2 - 0.15):
        px, pz = x + u * c, z - u * s_
        tube("Steel", [(px, y + h / 2 + 0.05, pz), (px, ceiling(px) - 0.02, pz)], 0.005, 4, smooth=False)
        cyl("Steel", (px, ceiling(px) - 0.02, pz), 0.03, 0.02, 10)
    rbox("Steel", (x - 0.03 * s_, y, z - 0.03 * c), (w + 0.1, h + 0.1, 0.03), (0, yaw, 0), 0.012)
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
    """Boards on the back wall from back_x0 and down the low end's wall (bindings on every other), a low two-tier stand
    under the window (helmets and goggles on top, bindings under), a board tree, and the counter facing the door: an
    oak counter with a glass case of goggles and wax in its front, a stone top, the point of sale (it opens the
    upgrades)."""
    nb = int((7.6 - back_x0 - 0.2) / 0.4) + 1
    for i in range(nb):
        board("Swatch%d" % (1 + (i * 2) % 5), back_x0 + i * 0.4, 0.12, MZ + 0.1, lean=-7, bindings=i % 2 == 0)
    rlohi("Glulam", (back_x0 - 0.2, 0.0, MZ), (7.6, 0.12, MZ + 0.3), 0.015, 2)
    helper("BLK_boards", (back_x0 - 0.2, 0.0, MZ), (7.6, 1.7, MZ + 0.35))
    for i in range(12):
        board("Swatch%d" % (1 + (i * 3) % 5), XL - T - 0.1, 0.12, -6.3 + i * 0.42, yaw=-90, lean=-7, bindings=i % 3 == 0)
    rlohi("Glulam", (XL - T - 0.3, 0.0, -6.55), (XL - T, 0.12, -1.5), 0.015, 2)
    helper("BLK_boards", (XL - T - 0.35, 0.0, -6.6), (XL - T, 1.7, -1.5))
    # the stand under the window: black steel legs, two rounded oak shelves
    SX0, SX1, SZ = 7.8, 10.2, MZ + 0.2
    for x in (SX0 + 0.04, SX1 - 0.04):
        for zz in (MZ + 0.04, MZ + 0.36):
            tube("Steel", [(x, 0.0, zz), (x, 0.94, zz)], 0.016, 6)
    for yy in (0.42, 0.9):
        rlohi("Oak", (SX0, yy - 0.04, MZ), (SX1, yy, MZ + 0.4), 0.014, 2)
    for i in range(3):
        helmet(SX0 + 0.35 + i * 0.85, 0.9, SZ, ("White", "Red", "Charcoal")[i], 0)
    for i in range(2):
        goggles((SX0 + 0.78 + i * 0.85, 0.9, SZ), 0, ("ArtA", "Navy")[i])
    for i in range(4):
        x = SX0 + 0.35 + i * 0.57
        rbox("Charcoal", (x, 0.43, SZ), (0.2, 0.02, 0.24), (0, 0, 0), 0.008)
        rbox(("Charcoal", "Red", "Charcoal", "Navy")[i], (x, 0.52, SZ - 0.09), (0.19, 0.17, 0.025), (-12, 0, 0), 0.01, 1)
        for sx in (-1, 1):
            rbox("Charcoal", (x + sx * 0.06, 0.465, SZ + 0.02), (0.02, 0.05, 0.16), (0, 0, 0), 0.005)
    helper("BLK_shelf", (SX0, 0.0, MZ), (SX1, 1.25, MZ + 0.4))
    # the board tree: a round steel foot, its pole, boards leaning round it
    TX, TZ = 6.3, -4.1
    cyl("Steel", (TX, 0.0, TZ), 0.55, 0.035, 18)
    cyl("Steel", (TX, 0.035, TZ), 0.5, 0.012, 18)
    tube("Steel", [(TX, 0.04, TZ), (TX, 1.6, TZ)], 0.035, 10)
    cyl("Steel", (TX, 1.6, TZ), 0.05, 0.02, 10)
    for k in range(6):
        a = k * math.pi / 3
        tube("Steel", [(TX, 1.15, TZ), (TX + 0.3 * math.cos(a), 1.15, TZ + 0.3 * math.sin(a))], 0.012, 6)
        board("Swatch%d" % (1 + k % 5), TX + 0.42 * math.cos(a), 0.08, TZ + 0.42 * math.sin(a), yaw=90 - math.degrees(a), lean=-10, h=1.45, bindings=k % 2 == 0)
    helper("BLK_tree", (TX - 0.65, 0.0, TZ - 0.65), (TX + 0.65, 1.6, TZ + 0.65))
    # the counter
    CZ = counter_z
    rlohi("Steel", (5.85, 0.0, CZ - 0.3), (8.15, 0.1, CZ + 0.3), 0.006)
    rlohi("Oak", (5.8, 0.1, CZ - 0.35), (8.2, 1.0, CZ + 0.35), 0.014)
    for k in range(17):   # (fluted down its back and ends: the staff's side, and what you see coming round it)
        box("Oak", (5.86 + k * 0.143, 0.55, CZ - 0.362), (0.04, 0.84, 0.026))
    rlohi("CounterTop", (5.74, 1.0, CZ - 0.41), (8.26, 1.055, CZ + 0.41), 0.016, 2)
    pane("x", CZ + 0.36, 5.9, 8.1, 0.25, 0.9, 0.9, 0.02, "GlassIn")
    for (a, b_) in (((5.88, 0.22, CZ + 0.355), (8.12, 0.25, CZ + 0.375)), ((5.88, 0.9, CZ + 0.355), (8.12, 0.93, CZ + 0.375)),
                    ((5.88, 0.25, CZ + 0.355), (5.91, 0.9, CZ + 0.375)), ((8.09, 0.25, CZ + 0.355), (8.12, 0.9, CZ + 0.375))):
        rlohi("Steel", a, b_, 0.004)
    rlohi("Oak", (5.9, 0.5, CZ - 0.25), (8.1, 0.53, CZ + 0.32), 0.006)   # (the case's shelf)
    for i in range(4):
        goggles((6.25 + i * 0.5, 0.53, CZ + 0.12), 0, ("ArtA", "Navy", "Leaf", "Red")[i])
    for i in range(4):
        rbox(("Cream", "Red", "Leaf", "ArtB")[i], (6.45 + i * 0.5, 0.29, CZ + 0.1), (0.14, 0.05, 0.08), (0, 12 * (i - 1.5), 0), 0.012, 1)   # (wax bars)
    pos_till((7.65, 1.055, CZ - 0.12), 180)
    rbox("Steel", (7.0, 3.35, MZ + 0.022), (2.5, 0.7, 0.025), (0, 0, 0), 0.01)
    sign(1, (7.0, 3.35, MZ + 0.05), 2.4, 0.6)
    helper("TAP_Upgrades", (5.7, 0.0, CZ - 0.45), (8.3, 2.2, CZ + 0.45))
    helper("BLK_counter", (5.75, 0.0, CZ - 0.42), (8.25, 1.2, CZ + 0.42))
    marker("SPOT_Upgrades", (7.0, 0.0, CZ + 1.15))


def tuning_bench():
    """The wax and tuning bench by the side door (the racks are just outside it): a black steel cabinet under a thick
    oak top, a board on its vices base up being waxed, the iron, scrapers and brushes, wax on a shelf; it opens the
    upgrades too."""
    BX0, BX1, BZ0, BZ1 = 8.95, 10.6, 5.85, 6.62
    ZC = (BZ0 + BZ1) / 2
    rlohi("Steel", (BX0, 0.0, BZ0), (BX1, 0.88, BZ1), 0.012)
    for x in (BX0 + 0.41, BX0 + 0.82, BX0 + 1.23):
        rlohi("Charcoal", (x - 0.006, 0.08, BZ0 - 0.005), (x + 0.006, 0.8, BZ0 + 0.002), 0.002)   # (its doors' joints)
    rlohi("Oak", (BX0 - 0.05, 0.88, BZ0 - 0.05), (BX1, 0.94, BZ1), 0.014, 2)
    snowboard("BoardPaint", (9.75, 1.08, ZC), 90, -90, 1.45)
    for sx in (-1, 1):
        rbox("Steel", (9.75 + sx * 0.55, 1.01, ZC), (0.1, 0.13, 0.36), (0, 0, 0), 0.01)
        rbox("Charcoal", (9.75 + sx * 0.55, 1.075, ZC), (0.12, 0.012, 0.3), (0, 0, 0), 0.004)
    rbox("Charcoal", (9.25, 0.975, BZ0 + 0.14), (0.26, 0.07, 0.13), (0, 15, 0), 0.02, 1)    # (the iron)
    tube("Charcoal", [(9.15, 1.03, BZ0 + 0.1), (9.3, 1.07, BZ0 + 0.16)], 0.012, 6)
    for k in range(4):
        rbox(("Navy", "Cream", "Navy", "Cream")[k], (10.05 + k * 0.12, 0.95, BZ0 + 0.18), (0.08, 0.02, 0.24), (0, 0, 0), 0.006)
    rlohi("Oak", (XL - T - 0.25, 1.4, 5.0), (XL - T, 1.44, 6.6), 0.01)
    for k in range(6):
        rbox(("ArtA", "Red", "Navy", "Leaf", "Cream", "ArtB")[k], (XL - T - 0.12, 1.5, 5.15 + k * 0.24), (0.12, 0.1, 0.16), (0, 0, 0), 0.015, 1)
    sign(2, (9.75, 3.08, D - T - 0.03), 1.45, 0.36, 180)
    rbox("Steel", (9.75, 3.08, D - T - 0.015), (1.53, 0.44, 0.02), (0, 0, 0), 0.008)
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


def missions_stand(mx=2.55, z0=2.55, z1=4.65):
    """The missions board just inside the front glass, left of the door (camplayout: it was on the back wall across the
    hall): freestanding on black steel legs, facing the way in (+x), so you meet it as you come in, the way a lodge's
    grooming and conditions board stands by its door. (Its end short of the glass: Val and Breanna's way from the door
    along the glass to their place at it, Camp.LodgeWindow, passes in front of it.)"""
    zc, w = (z0 + z1) / 2, z1 - z0
    for z in (z0 + 0.06, z1 - 0.06):
        lohi("Steel", (mx - 0.05, 0.0, z - 0.04), (mx + 0.01, 3.35, z + 0.04))
        lohi("Steel", (mx - 0.32, 0.0, z - 0.03), (mx + 0.3, 0.04, z + 0.03))   # (its feet)
    lohi("Steel", (mx - 0.04, 0.7, z0 + 0.02), (mx + 0.01, 2.75, z1 - 0.02))
    lohi("Oak", (mx + 0.01, 0.8, z0 + 0.12), (mx + 0.04, 2.65, z1 - 0.12))
    sp = min(0.95, (w - 0.4) / 3)
    for i, z in enumerate((zc - sp, zc, zc + sp)):
        quad("Note%d" % i, (mx + 0.05, 1.62, z), sp - 0.25 if sp < 0.95 else 0.7, 0.88, yaw=90)
    quad("SignMissions", (mx + 0.03, 3.05, zc), min(2.8, w - 0.15), min(2.8, w - 0.15) / 4, yaw=90)
    lohi("Steel", (mx - 0.04, 2.88, z0 + 0.02), (mx + 0.01, 3.22, z1 - 0.02))
    lohi("LED", (mx + 0.04, 2.72, z0 + 0.12), (mx + 0.08, 2.74, z1 - 0.12))
    helper("TAP_Missions", (mx - 0.1, 0.5, z0 - 0.1), (mx + 0.6, 3.5, z1 + 0.1))
    helper("BLK_missions", (mx - 0.32, 0.0, z0 - 0.04), (mx + 0.3, 3.35, z1 + 0.04))
    marker("SPOT_Missions", (mx + 1.35, 0.0, zc))


def welcome_mat():
    lohi("Runner", (3.2, 0.0, 4.9), (5.8, 0.012, 6.6))


def b_rest():
    """The low end and the wing: the board shop, wax & tune (at the front glass), the café. (campstairs: the missions
    board by the door is gone; the one by the gate is the only one, Camp's CampBoard.cs.)"""
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
            pane("x", fixed + side * 0.04, a0 + 0.004, a1 - 0.004, y, y + 1.0, y + 1.0, 0.02, "GlassIn")   # (its ends just short of the edge's: at a wall's glass they were one plane)
            lohi("Steel", (a0, y + 1.0, fixed + side * 0.04 - 0.03), (a1, y + 1.05, fixed + side * 0.04 + 0.03))
            helper("BLK_" + name, (a0, y, fixed - 0.1), (a1, y + 1.1, fixed + 0.1))
    else:
        lohi("Steel", (fixed - 0.02, y - 0.32, a0), (fixed + 0.02, y + 0.02, a1))
        xl = fixed + side * 0.1
        lohi("LED", (min(xl, xl + side * 0.05), y - 0.34, a0 + 0.15), (max(xl, xl + side * 0.05), y - 0.32, a1 - 0.15))
        if rail:
            pane("z", fixed + side * 0.04, a0 + 0.004, a1 - 0.004, y, y + 1.0, y + 1.0, 0.02, "GlassIn")
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


# ---- lodge5: the lounge's and the hall's finishing: lamps, side tables, throws, mugs, books, art, skis

# the art sheet (Resources/Camp/LodgeArt.png, Tools/MakeLodgeSigns.swift): its pieces' rectangles (u0, v0, u1, v1)
ART = {"map": (0.0, 0.5, 1.0, 1.0), "posterA": (0.0, 0.0, 340 / 1024, 510 / 1024), "posterB": (342 / 1024, 0.0, 682 / 1024, 510 / 1024),
       "lookout": (684 / 1024, 386 / 1024, 1.0, 510 / 1024), "topo": (684 / 1024, 0.0, 1.0, 384 / 1024)}


def art(key, pos, w, h, yaw=0.0, frame="Oak", depth=0.03):
    """A piece of the art sheet on a wall, facing +z turned by yaw (its frame behind it, a little bigger)."""
    u0, v0, u1, v1 = ART[key]
    b = bmesh.new()
    uv = b.loops.layers.uv.new("UVMap")
    vs = [b.verts.new(c) for c in ((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0))]
    f = b.faces.new(vs)
    for l, c in zip(f.loops, ((u1, v0), (u0, v0), (u0, v1), (u1, v1))):
        l[uv].uv = c
    add(b, "Art", mat4(pos, (w, h, 1), (0, yaw, 0)), full_uv=True)
    if frame:
        nx, nz = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        d = depth / 2 + 0.004
        box(frame, (pos[0] - nx * d, pos[1], pos[2] - nz * d), (w + 0.09, h + 0.09, depth), (0, yaw, 0))


def side_table(x, z, r=0.26, h=0.52, y=0.0):
    cyl("Oak", (x, y + h - 0.04, z), r, 0.04, 18)
    cyl("Steel", (x, y + 0.02, z), 0.03, h - 0.06, 6)
    cyl("Steel", (x, y, z), r * 0.7, 0.02, 14)
    helper("BLK_sidetable", (x - r - 0.02, y, z - r - 0.02), (x + r + 0.02, y + h + 0.05, z + r + 0.02))


def table_lamp(x, z, y):
    """A lamp on a table (its top at y): a dark ceramic base, a warm-lit linen shade."""
    cyl("Charcoal", (x, y, z), 0.075, 0.24, 12, r2=0.05)
    cyl("Shade", (x, y + 0.24, z), 0.16, 0.2, 16, r2=0.12)


def floor_lamp(x, z, h=1.55, y=0.0):
    """A floor lamp: a black stem on a round foot, a warm-lit drum shade."""
    cyl("Steel", (x, y, z), 0.17, 0.025, 14)
    cyl("Steel", (x, y + 0.025, z), 0.014, h - 0.1, 6)
    cyl("Shade", (x, y + h - 0.12, z), 0.24, 0.3, 18, r2=0.21)
    helper("BLK_lamp", (x - 0.2, y, z - 0.2), (x + 0.2, y + h + 0.2, z + 0.2))


def arc_lamp(x0, z0, x1, z1, top=2.15, y1=1.85):
    """An arc floor lamp: a heavy dark block, a black arm arching over to a dome over (x1, z1) at y1."""
    lohi("Charcoal", (x0 - 0.17, 0.0, z0 - 0.17), (x0 + 0.17, 0.3, z0 + 0.17))
    pts = []
    for k in range(9):
        t = k / 8
        x = x0 + (x1 - x0) * (1 - (1 - t) ** 2)
        z = z0 + (z1 - z0) * (1 - (1 - t) ** 2)
        y = 0.3 + (top - 0.3) * math.sin(min(1.0, t * 1.25) * math.pi / 2) - (top - y1 - 0.12) * max(0.0, (t - 0.7) / 0.3) ** 2
        pts.append((x, y, z))
    for a, b in zip(pts, pts[1:]):
        span("Steel", a, b, 0.025)
    sphere("Steel", (x1, y1 + 0.05, z1), (0.44, 0.26, 0.44), 14)
    cyl("Shade", (x1, y1 - 0.04, z1), 0.17, 0.04, 14)
    helper("BLK_lamp", (x0 - 0.2, 0.0, z0 - 0.2), (x0 + 0.2, 0.35, z0 + 0.2))


def mug(x, z, y, col="Cream"):
    cyl(col, (x, y, z), 0.045, 0.1, 10)
    lohi(col, (x + 0.04, y + 0.03, z - 0.008), (x + 0.07, y + 0.08, z + 0.008))


def books(x, z, y, n=3, yaw=0.0):
    hh = y
    for k in range(n):
        th = 0.035 + 0.012 * (k % 2)
        box(("Navy", "Red", "Cream", "ArtB", "Charcoal")[(k * 3 + int(x * 7)) % 5], (x, hh + th / 2, z), (0.3 - 0.03 * k, th, 0.22 - 0.01 * k), (0, yaw + 9 * k, 0))
        hh += th


def throw_over(x0, x1, z, face, depth=0.95, y=0.0):
    """A wool throw folded over a sofa's back (sofa(): along x at z, facing +z or -z), hanging down its front."""
    zb = z - face * (depth / 2 - 0.13)
    lohi("Throw", (x0, y + 0.85, zb - 0.15), (x1, y + 0.875, zb + 0.15))
    zf = zb + face * 0.135
    lohi("Throw", (x0, y + 0.53, min(zf, zf + face * 0.025)), (x1, y + 0.875, max(zf, zf + face * 0.025)))


def cushions(x0, x1, z, face, cols, depth=0.95, y=0.0):
    """Accent cushions leaning on a sofa's back."""
    zb = z - face * (depth / 2 - 0.13)
    for k, c in enumerate(cols):
        cx = x0 + (x1 - x0) * (k + 0.5) / len(cols)
        box(c, (cx, y + 0.69, zb + face * 0.24), (0.42, 0.4, 0.13), (face * 16, 6 * (k % 2 * 2 - 1), 0))


def log_basket(x, z, r=0.32):
    """A black steel ring of split logs for the fire."""
    cyl("Steel", (x, 0.0, z), r, 0.04, 14)
    for k in range(9):
        a = k * 2.4
        rr = 0.18 * ((k * 5) % 3) / 2
        cyl("Bark", (x + rr * math.cos(a), 0.04 + 0.11 * (k // 3), z + rr * math.sin(a)), 0.065, 0.55 - 0.03 * (k % 3), 7)
    for a in (0.6, 2.7, 4.8):
        span("Steel", (x + r * math.cos(a), 0.0, z + r * math.sin(a)), (x + r * math.cos(a), 0.45, z + r * math.sin(a)), 0.02)
    helper("BLK_logs", (x - r - 0.03, 0.0, z - r - 0.03), (x + r + 0.03, 0.65, z + r + 0.03))


def ski_wall(x0, x1, y0, n):
    """Old wooden and painted skis in pairs up the back wall, tips curling out, bindings on, a strap round each pair,
    each pair on a steel bracket."""
    for i in range(n):
        x = x0 + (x1 - x0) * (i + 0.5) / n
        mat = ("Glulam", "Red", "Glulam", "Navy", "Glulam", "ArtB", "Glulam", "ArtA")[i % 8]
        for sx in (-1, 1):
            ski(mat, (x + sx * 0.06, y0 + 0.97, MZ + 0.035), 0, 0, 1.95)
        rbox("Charcoal", (x, y0 + 1.27, MZ + 0.055), (0.29, 0.045, 0.03), (0, 0, 0), 0.008)
        rbox("Steel", (x, y0 + 0.45, MZ + 0.012), (0.3, 0.04, 0.022), (0, 0, 0), 0.008)


def coat_rack(z0, z1, x=XL - T):
    """By the side door (on the low end's wall, facing in): an oak rail of black pegs, jackets hung on them by their
    hoods' loops, a beanie and a scarf; an oak bench under it with boots beneath and a helmet on it."""
    rlohi("Oak", (x - 0.04, 1.62, z0), (x, 1.78, z1), 0.008)
    n = 5
    for k in range(n):
        zh = z0 + 0.15 + (z1 - z0 - 0.3) * k / (n - 1)
        tube("Steel", [(x - 0.04, 1.7, zh), (x - 0.13, 1.72, zh)], 0.014, 6)
        if k % 2 == 0:
            jacket(JACKETS[(k * 3) % len(JACKETS)], (x - 0.17, 1.84, zh), -90, hood=True, hanger=False, s=0.97)
        elif k == 1:
            beanie((x - 0.16, 1.6, zh), "Red")
            tube("Navy", [(x - 0.13, 1.7, zh - 0.05), (x - 0.12, 1.3, zh - 0.06), (x - 0.11, 1.08, zh - 0.06)], 0.025, 6)
            tube("Navy", [(x - 0.13, 1.7, zh + 0.05), (x - 0.12, 1.35, zh + 0.06), (x - 0.11, 1.15, zh + 0.07)], 0.025, 6)
        else:
            tube("Cream", [(x - 0.13, 1.7, zh), (x - 0.12, 1.25, zh + 0.02), (x - 0.11, 1.0, zh + 0.03)], 0.03, 6)
    rlohi("Oak", (x - 0.42, 0.42, z0 + 0.02), (x - 0.02, 0.47, z1 - 0.02), 0.012, 2)
    for zz in (z0 + 0.08, z1 - 0.08):
        rlohi("Steel", (x - 0.38, 0.0, zz - 0.02), (x - 0.06, 0.42, zz + 0.02), 0.006)
    helmet(x - 0.24, 0.47, z0 + 0.4, "White", -90)
    for k in range(2):
        bz = z0 + 0.4 + k * 0.45
        for sx in (-0.06, 0.06):
            boot((x - 0.24, 0.0, bz + sx), -90, "Charcoal", ("Red", "Navy")[k])
    helper("BLK_coats", (x - 0.45, 0.0, z0), (x, 1.9, z1))


def fire_lounge():
    """The lounge round the fire, bigger: the two sofas across it, a pair of armchairs angled in at each end with a
    side table between them and a floor lamp behind, lamps on the side tables at the back sofa's ends, throws,
    cushions and mugs; a log basket by it; one big rug under all of it."""
    FX, FZ = -1.8, -2.71
    lohi("Rug", (-5.95, 0.0, -5.05), (2.4, 0.015, 0.25))
    open_fire(FX, FZ)
    marker("LIGHT_fire", (FX, 0.8, FZ + 1.35))
    sofa(-3.85, 0.25, -4.62, 1)
    sofa(-3.85, 0.25, -0.8, -1)
    for (ax, az, yaw) in ((-5.35, -3.65, 72), (-5.35, -1.75, 108), (1.75, -3.65, -72), (1.75, -1.75, -108)):
        armchair(ax, az, yaw)
    for (tx, tz) in ((-4.55, -4.62), (0.95, -4.62), (-5.45, -2.7), (1.85, -2.7)):
        side_table(tx, tz)
    table_lamp(-4.55, -4.62, 0.52)
    table_lamp(0.95, -4.62, 0.52)
    mug(-5.5, -2.6, 0.52); books(1.85, -2.72, 0.52, 2, -20)
    mug(1.75, -2.55, 0.6, "Red")
    floor_lamp(-6.1, -2.7)
    floor_lamp(2.45, -2.7)
    floor_lamp(-5.6, -4.55)
    log_basket(-6.25, -3.95)
    throw_over(-3.75, -3.0, -4.62, 1)
    throw_over(-0.6, 0.15, -0.8, -1)
    cushions(-3.5, 0.0, -4.62, 1, ("Cream", "Red", "Cream"))
    cushions(-3.5, 0.0, -0.8, -1, ("Navy", "Cream", "Red"))
    pendants(((-3.6, 4.6, -3.9), (-0.2, 4.2, -4.0), (-2.2, 5.0, -1.5), (0.6, 4.8, -2.0)), under_flat)
    pendants(((-2.6, 3.5, 4.15), (-2.0, 3.1, 3.85), (-1.4, 3.65, 4.3)), under_shed)


def outfitters_front():
    """The outfitters just inside the front glass, beside the door (camplayout: down from the loft, so the closet is on
    the way in and out): a shop window of three mannequins at the glass, a two-sided rail of jackets and a display
    table behind them, the till by the missions board, its sign hung over the rail; the way across the hall in front of
    it (z 0.2 to 2.2) kept clear, and the big glass's right half free for Val and Breanna (Camp.LodgeWindow). The
    wardrobe's station (the closet) at the rail."""
    for (mx, col, yaw, strap) in ((-4.6, "Red", 200, "Navy"), (-3.6, "ArtB", 180, "Red"), (-2.6, "Navy", 160, "ArtA")):
        mannequin(mx, 6.15, col, 0.0, yaw, strap)
    rail_x(-4.8, -2.2, 4.0, two_sided=True)
    display_table(-0.8, 3.2)
    till(0.35, 2.45, 1.05, 3.15)
    hanging_sign(0, -3.5, 2.95, 4.0, 2.4, 0.6, under_shed)
    helper("TAP_Style", (-5.0, 0.0, 3.3), (-2.0, 2.6, 4.7))
    marker("SPOT_Style", (-3.5, 0.0, 2.55))


def loft_lounge():
    """Up in the loft, the lounge's window half (camplayout: it was down at the front glass, where the outfitters are
    now): a sofa facing the two-storey glass and the view over the camp, armchairs angled in at its ends, a low table with
    books and mugs, lamps; on its own rug. And by the loft's back wall a bookcase and a reading chair."""
    y = MY
    lohi("Rug", (-10.45, y, 1.9), (-5.75, y + 0.015, 5.9))
    sofa(-9.85, -6.95, 3.0, 1, y=y)
    armchair(-10.15, 4.55, 64, y=y)
    armchair(-6.4, 4.55, -64, y=y)
    low_table(-9.2, 3.95, -7.6, 4.65, y=y)
    books(-8.85, 4.3, y + 0.42, 3, 8)
    mug(-8.1, 4.2, y + 0.42); mug(-7.85, 4.45, y + 0.42, "Red")
    side_table(-10.3, 2.85, y=y)
    table_lamp(-10.3, 2.85, y + 0.52)
    floor_lamp(-6.1, 2.7, y=y)
    throw_over(-7.85, -7.15, 3.0, 1, y=y)
    cushions(-9.55, -7.25, 3.0, 1, ("Red", "Cream", "Navy"), y=y)
    bookcase(-10.3, -7.2, MZ, h=2.2, y=y)
    armchair(-8.75, -4.7, 0, y=y)
    floor_lamp(-9.9, -5.4, y=y)


# ---- the inside: the B2R design, the one the user kept (the other six layouts and their switch went in campsnow). The
# loft over the high end with the outfitters up there and the snug under it; the stair along the back wall, arriving at
# the loft's back corner by the gear wall; the lounge round the fire and facing the front glass; the loft's doors out to
# the lookout deck on the wing's roof.

def lodge_inside():
    LX = -5.4
    SZ0, SZ1 = MZ + 0.06, MZ + 1.56       # the stair: off the back wall's face, 1.5 m wide
    # the lounge (lodge5: bigger): round the fire, and its second half facing the front glass
    fire_lounge()
    outfitters_front()   # (camplayout: where the window lounge was; the window lounge went up to the loft)
    b_rest()
    # under the loft: the snug and the café's tables
    lohi("Runner", (-10.9, 0.0, -0.4), (-6.5, 0.012, 1.9))
    lohi("Rug", (-10.4, 0.0, -6.0), (-6.4, 0.015, -1.8))
    bookcase(-10.3, -6.9, MZ, h=2.4)
    sofa(-10.0, -7.4, -5.25, 1, mat="Hide")
    low_table(-9.4, -4.2, -8.0, -3.6)
    armchair(-9.6, -2.6, 180); armchair(-7.8, -2.6, 180)
    bistro(-9.2, 3.6); bistro(-8.4, 5.6)
    plant(-6.0, -6.1, 1.2)
    plant(-5.15, 6.15, 1.3); plant(1.6, 6.15, 1.5)   # (the front glass clear again, as B's; the window lounge's view between them)
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
    # upstairs: the lounge's window half, over the camp (camplayout: the outfitters went down to the front glass)
    loft_lounge()
    marker("LIGHT_hall", (-4.6, 7.6, -1.0))
    # the doors out to the lookout: its sign over them (on their head), a runner up to them
    lohi("Steel", (XH + T, MY + 2.36, -0.72), (XH + T + 0.04, MY + 2.86, 0.72))
    art("lookout", (XH + T + 0.045, MY + 2.61, 0.0), 1.36, 0.46, 90, frame=None)
    lohi("Runner", (XH + T + 0.05, MY, -0.8), (XH + T + 3.0, MY + 0.012, 0.8))
    # the hall's details: old skis up the back wall over the stair, the trail map at its foot, art in the snug and the
    # café, the coats by the side door
    ski_wall(-4.9, 0.1, 4.95, 7)
    art("map", (2.0, 1.85, MZ + 0.04), 1.7, 0.85)
    art("topo", (XH + T + 0.04, 1.75, -5.7), 0.95, 1.07, 90)
    art("posterA", (WX0 + 0.04, 1.75, -4.1), 0.62, 0.93, 90)
    art("posterB", (WX0 + 0.04, 1.9, 3.3), 0.62, 0.93, 90)
    coat_rack(1.68, 2.95)



# the lookout's deck on the wing's roof (the shell's; walked onto from the loft)
deck()

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
wcol("high_top_a", (XH, WOPEN[2] - 0.45, WOPEN[0]), (XH + T, H, DOORW[0]))
wcol("high_top_b", (XH, WOPEN[2] - 0.45, DOORW[1]), (XH + T, H, WOPEN[1]))
wcol("high_sill", (XH, WOPEN[2] - 0.45, DOORW[0]), (XH + T, MY - 0.05, DOORW[1]))
wcol("high_head", (XH, DOORW[2], DOORW[0]), (XH + T, H, DOORW[1]))
helper("FLR_deckdoor", (XH - 0.1, MY - 0.3, DOORW[0]), (XH + T + 0.1, MY, DOORW[1]))
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
helper("CAM_wing", (WRX0, WY - RD - 0.2 - 0.25, WRZ0), (XH, MY - 0.02, WRZ1 + 0.6))
marker("MARK_front", (0, 0, D))
marker("MARK_right", (XL, 0, 0))

# ---------------------------------------------------------------- write it out

SHELL_FACES = []


def pile_grids(path, cell=0.2):
    """The lodge's piles of snow (bank()'s drifts, the SnowPile mesh) as grids of their surface's height, cast down onto
    the mesh as drawn: the game walks you up them (Resources/Camp/LodgePiles.txt, CampSnow.cs; the meshes themselves
    aren't readable on the phone). The lodge's frame, metres above its floor; '-' off the snow."""
    b = geo.get("SnowPile")
    if b is None:
        return
    bvh = BVHTree.FromBMesh(b)
    lines = ["# the lodge's piles of snow (MakeLodge.py's bank()): each a grid of its surface's height in the lodge's frame",
             "# (metres above its floor), %.2f m apart; per pile: x0 z0 nx nz, then nz rows of nx heights ('-' off it)" % cell]
    n = 0
    for (x0, z0, x1, z1) in PILES:
        x0, z0, x1, z1 = x0 - cell, z0 - cell, x1 + cell, z1 + cell
        nx, nz = int(math.ceil((x1 - x0) / cell)) + 1, int(math.ceil((z1 - z0) / cell)) + 1
        lines.append("pile %.3f %.3f %d %d" % (x0, z0, nx, nz))
        for j in range(nz):
            row = []
            for i in range(nx):
                x, z = x0 + i * cell, z0 + j * cell
                hit = bvh.ray_cast(Vector((x, z, 20.0)), Vector((0.0, 0.0, -1.0)))   # (Blender's axes: the game's z is its y, the game's y its z)
                row.append("%.3f" % hit[0].z if hit[0] is not None and hit[1].z > 0.05 else "-")
                n += hit[0] is not None
            lines.append(" ".join(row))
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")
    print("LODGE piles: %d grids, %d points on the snow, written %s" % (len(PILES), n, path))


def write(path, name):
    """The meshes and helpers made so far, as one FBX (then cleared, for the next file)."""
    if name == "Lodge":
        pile_grids(os.path.join(os.path.dirname(path) or ".", "LodgePiles.txt"))
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
# the inside: its own file (LodgeInside.fbx), put in the shell by the game
with inside():
    lodge_inside()
marker("MARK_front", (0, 0, D))
write(OUT[:-4] + "Inside.fbx", "LodgeInside")
