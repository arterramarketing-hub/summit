# What the model builders share (MakeCabins, MakeChalet, MakeLift, MakeLodge; the PNG writer also MakeBoardArt and
# MakeHero). Each builder keeps its own add(), which puts a piece into its mesh with its own materials and UVs; these
# only make the pieces and place them. Imported from Blender (--python) or plain python3: the Blender parts load
# bpy/mathutils only when asked for.
import math, os, struct, zlib


def mat4(pos, size=(1, 1, 1), rot=(0, 0, 0)):
    """Placed at pos, scaled by size, turned by rot (degrees, XYZ): the game's own axes."""
    from mathutils import Vector, Matrix, Euler
    return Matrix.Translation(Vector(pos)) @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4() @ Matrix.Diagonal(Vector((*size, 1)))


def cube():
    """A unit cube (bmesh), for a box: placed by mat4(pos, size, rot)."""
    import bmesh
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    return b


def cone(r, depth, seg, r2=None, caps=True):
    """A cylinder (or a cone when r2 is given) along its own z, centred (bmesh)."""
    import bmesh
    b = bmesh.new()
    bmesh.ops.create_cone(b, cap_ends=caps, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    return b


def png(w, h, raw, alpha):
    """A PNG's bytes: raw is the rows, each its filter byte (0) then its pixels, RGBA if alpha else RGB, 8 bits."""
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6 if alpha else 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write(path, data, makedirs=True):
    """Write bytes to path (making its folder first, unless makedirs is False)."""
    if makedirs: os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f: f.write(data)
