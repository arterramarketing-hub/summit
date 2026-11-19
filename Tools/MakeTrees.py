# Builds Summit's realistic forest: the pines' card atlas and every kind of tree as a mesh at two levels of
# detail, packed into one file the game reads at start-up (Assets/Summit/Scripts/RealFir.cs).
#
#   pip install bpy numpy pillow                                (Blender as a Python module, for --render; once)
#   python3 Tools/MakeTrees.py --project .                      (downloads the scans, writes the atlas and the trees)
#   python3 Tools/MakeTrees.py --project . --render DIR         (and pictures of the result, to check by eye)
#   blender --background --factory-startup --python Tools/MakeTrees.py -- --project . --render DIR   (the same, in Blender)
# Options: --cache DIR (where the downloads are kept, default the system's temp folder), --what atlas,trees
# (only that part; add census for a count of what the forest draws round the rider, and with --render a map of
# its stands), --shots lineup,near,lods,stand (only those pictures; none for just the map).
#
# Writes (with --project):
#   Assets/Summit/Resources/RealTrees/PineCards.png    the pines' and snags' atlas (1024 x 1024, see LAYOUT below)
#   Assets/Summit/Resources/RealTrees/Trees.bytes      every kind of tree: its measurements and two meshes
# The firs' atlas (RealTrees/FirCards.png) is read, not written: Tools/MakeFirCards.swift builds it.
# The .meta files beside them are written by hand and kept in the repository: this script never touches them.
#
# The scans are CC0, from Poly Haven: "Pine Tree 01" (https://polyhaven.com/a/pine_tree_01) - its twig atlas
# (needle shoots) and its bark. See Resources/CREDITS.txt.
#
# The atlas: needle shoots are cut out of the scan and stamped, dozens at a time, into whole branch ends as a
# pine carries them - seen from above (a round pad of shoots on forking twigs) and from the side (shoots standing
# up off an upswept limb) - then graded to a dark yellow-green and snowed on in clumps. Bare dead branches are
# drawn, and three strips of trunk are cut from the scanned bark: dark and plated (the lower trunk), orange and
# flaky (a pine's upper trunk and limbs), and silver-grey (dead wood).
#
# The trees: each kind is grown once, from a fixed seed, as a list of branches, and that one list is turned into
# two meshes - "rich" for trees close to the rider and "plain" for the middle distance (about what the game drew
# for every near tree before). So the two are the same tree, and one can dissolve into the other. Far trees are
# pictures of the plain mesh, taken by the game.
#   - firs and hemlocks: a trunk with a root flare, a dark core, tiers of branches. Plain: a branch is two cards,
#     one upright, one flat. Rich: the branch bends, its flat card sags either side of the stem, side fronds fork
#     off it, and short fronds fill the crown near the trunk.
#   - pines: a long bare trunk with dead stubs, a few big limbs that sweep up at the ends, each carrying pads of
#     needles (rich: a tented top, crossed uprights and upswept shoots round the rim; plain: one flat and one
#     upright card), and a broad, uneven, flat-topped crown.
#   - dead snags, young trees, old giants and saplings: the same parts in other proportions.
# Every vertex carries a normal that shades the crown (or a pine's pad) as one rounded mass rather than as flat
# cards, and a shade (0..1, in the vertex colour) that darkens the inside of the crown.
#
# Axes are Unity's: y up, metres, the tree standing on the origin. Kinds 0-7 keep the measurements the game has
# always used for its hit capsules (RealFir.cs has the same numbers): do not change one without the other.

import sys, os, math, json, hashlib, random, struct, tempfile, urllib.request
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else default
PROJECT = arg("--project")
RENDER = arg("--render")
CACHE = arg("--cache", os.path.join(tempfile.gettempdir(), "summit-trees"))
WHAT = set((arg("--what", "atlas,trees") or "").split(","))
SHOTS = set((arg("--shots", "lineup,near,lods,stand") or "").split(","))
RES = os.path.join(PROJECT, "Assets/Summit/Resources/RealTrees") if PROJECT else None

# ------------------------------------------------------------------------------------------------ the scans

ASSET = "pine_tree_01"
MAPS = {"twig_diff": "png", "twig_alpha": "png", "bark_diff": "jpg", "bark_ao": "jpg"}   # all at 2K

def fetch():
    """Downloads the scan's maps into the cache, checked against Poly Haven's sums. Returns name -> path."""
    os.makedirs(CACHE, exist_ok=True)
    def get(url):
        return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "summit-tree-build"})).read()
    listing = os.path.join(CACHE, ASSET + ".json")
    if not os.path.exists(listing):
        with open(listing, "wb") as f: f.write(get("https://api.polyhaven.com/files/" + ASSET))
    files = json.load(open(listing))
    out = {}
    for name, fmt in MAPS.items():
        e = files[name]["2k"][fmt]
        path = os.path.join(CACHE, os.path.basename(e["url"]))
        if not (os.path.exists(path) and hashlib.md5(open(path, "rb").read()).hexdigest() == e["md5"]):
            data = get(e["url"])
            if hashlib.md5(data).hexdigest() != e["md5"]: raise SystemExit("bad download: " + name)
            with open(path, "wb") as f: f.write(data)
        out[name] = path
    return out

# ------------------------------------------------------------------------------------------------ the atlas

SIZE = 1024
S = 2                      # drawn at twice the size and averaged down
# LAYOUT: cells in pixels of the finished atlas, (x0, y0, x1, y1) from the top left. In every card cell the branch
# comes in at the middle of the left edge and its tip is to the right.
CELLS = {
    "pad_light": (0, 0, 384, 384),         # a pad of needles from above, snow in a few clumps
    "pad_heavy": (384, 0, 768, 384),       # the same under a heavy load
    "twig_a":    (768, 0, 1024, 128),      # a twig end, its shoots swept up, snow on it
    "twig_b":    (768, 128, 1024, 256),    # another
    "twig_c":    (768, 256, 1024, 384),    # one bare of snow
    "bough_a":   (0, 384, 512, 640),       # a limb's end from the side, snow sitting on the shoots
    "bough_b":   (512, 384, 1024, 640),    # another, more heavily loaded
    "dead_a":    (0, 640, 512, 768),       # a bare dead branch
    "dead_b":    (512, 640, 1024, 768),    # another, a line of snow along it
    # trunks: the trunk runs along each strip; a strip is laid end to end up a trunk, every other one mirrored
    "bark":      (0, 768, 480, 896),       # lower trunk: dark plates
    "bark_mid":  (480, 768, 960, 896),     # where the dark bark gives way to the orange (left end: "bark"'s right end)
    "bark_hi":   (0, 896, 480, 1024),      # upper trunk and limbs: orange, flaky
    "wood":      (480, 896, 960, 1024),    # dead wood: silver-grey, split along the grain
    "core":      (960, 768, 1024, 853),    # flat shadowed green, for the inside of a crown
    "snow":      (960, 853, 1024, 938),    # flat snow
    "hollow":    (960, 938, 1024, 1024),   # flat near-black: the broken end of a snag
}
NEEDLE = (0.66, 0.80, 0.62)                # the scan's needles graded to a dark yellow-green
FILL = (34, 50, 30)                        # under the cut-out (what the edges fade to down the mips)

def value_noise(rng, w, h, cell):
    """Smooth noise 0..1, w x h, with features about `cell` pixels across."""
    gw, gh = max(2, w // cell + 2), max(2, h // cell + 2)
    g = np.array([[rng.random() for _ in range(gw)] for _ in range(gh)], dtype=np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(im, dtype=np.float32) / 255.0

def load_sprites(maps):
    """The two needle shoots in the scan's twig atlas, cut out, laid stem-left and tip-right."""
    diff = Image.open(maps["twig_diff"]).convert("RGB")
    alpha = Image.open(maps["twig_alpha"]).convert("L")
    if diff.size != (2048, 2048): raise SystemExit("expected the 2K twig atlas")
    out = []
    # (box in the atlas, a point on the shoot, turn to lay it stem-left, share of its bare stem cut off)
    for box, seed, turn, trim in [((60, 80, 460, 920), (203, 300), -90, 0.2), ((1320, 680, 1920, 1060), (330, 205), -12, 0.04)]:
        rgb = diff.crop(box)
        a = np.asarray(alpha.crop(box)) > 100
        # only the piece of the cut-out that holds the seed point (its neighbours in the atlas share the box)
        keep = np.zeros_like(a); keep[seed[1] - 3:seed[1] + 4, seed[0] - 3:seed[0] + 4] = True
        keep &= a
        while True:
            grown = np.asarray(Image.fromarray(keep).filter(ImageFilter.MaxFilter(9))) & a
            if (grown == keep).all(): break
            keep = grown
        if keep.sum() < 2000: raise SystemExit("the shoot was not where it was expected in the twig atlas")
        mask = Image.fromarray((np.asarray(alpha.crop(box)) * keep).astype(np.uint8))
        spr = Image.merge("RGBA", (*rgb.split(), mask)).rotate(turn, expand=True, resample=Image.BICUBIC)
        spr = spr.crop(spr.getbbox())
        spr = spr.crop((int(spr.size[0] * trim), 0, spr.size[0], spr.size[1]))
        an = np.asarray(spr)[:, :, 3]
        ys = np.nonzero(an[:, :8].max(axis=1) > 100)[0]
        out.append((spr, (2.0, float(ys.mean()) if len(ys) else spr.size[1] / 2)))
    return out

class Canvas:
    """One cell being drawn, at S times its size."""
    def __init__(self, cell):
        x0, y0, x1, y1 = CELLS[cell]
        self.w, self.h = (x1 - x0) * S, (y1 - y0) * S
        self.im = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))

    def stamp(self, sprite, at, angle, length, shade=1.0, flip=False):
        """A shoot with its stem's foot at `at`, pointing `angle` degrees (0 right, 90 up), `length` pixels long."""
        spr, base = sprite
        k = length / spr.size[0]
        w, h = max(2, int(spr.size[0] * k)), max(2, int(spr.size[1] * k))
        im = spr.resize((w, h), Image.LANCZOS)
        bx, by = base[0] * k, base[1] * k
        if flip:
            im = im.transpose(Image.FLIP_TOP_BOTTOM); by = h - by
        if shade != 1.0:
            r, g, b, a = im.split()
            im = Image.merge("RGBA", (*[c.point(lambda v, s=shade: int(min(255, v * s))) for c in (r, g, b)], a))
        rot = im.rotate(angle, expand=True, resample=Image.BICUBIC)
        dx, dy = bx - w / 2, by - h / 2
        ca, sa = math.cos(math.radians(angle)), math.sin(math.radians(angle))
        rx, ry = dx * ca + dy * sa, -dx * sa + dy * ca
        px, py = int(round(at[0] - (rot.size[0] / 2 + rx))), int(round(at[1] - (rot.size[1] / 2 + ry)))
        # clip to the canvas (alpha_composite takes no negative destination)
        sx0, sy0 = max(0, -px), max(0, -py)
        sx1, sy1 = min(rot.size[0], self.w - px), min(rot.size[1], self.h - py)
        if sx1 <= sx0 or sy1 <= sy0: return
        self.im.alpha_composite(rot.crop((sx0, sy0, sx1, sy1)), dest=(px + sx0, py + sy0))

    def line(self, pts, width, colour):
        d = ImageDraw.Draw(self.im)
        d.line([(float(x), float(y)) for x, y in pts], fill=colour, width=max(1, int(round(width))), joint="curve")

    def snow(self, rng, blobs, side, amount=1.0):
        """Clumps of snow: `blobs` are (x, y, rx, ry). From the side they are lit from above and shaded below."""
        yy, xx = np.mgrid[0:self.h, 0:self.w].astype(np.float32)
        field = np.zeros((self.h, self.w), np.float32)
        for (cx, cy, rx, ry) in blobs:
            field += np.exp(-(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2))
        field += (value_noise(rng, self.w, self.h, 14 * S) - 0.5) * 0.35 + (value_noise(rng, self.w, self.h, 5 * S) - 0.5) * 0.12
        mask = np.clip((field - 0.5) / 0.06, 0, 1) * amount
        if side:
            # the mound's top catches the light; its foot and underside are in blue shade
            up = np.roll(field, 3 * S, axis=0) - np.roll(field, -3 * S, axis=0)
            lit = np.clip(0.62 + up * 2.2 + (field - 0.5) * 0.25, 0, 1)
        else:
            lit = np.clip(0.35 + (field - 0.5) * 1.6, 0, 1) ** 0.6
        lit = np.clip(lit + (value_noise(rng, self.w, self.h, 3 * S) - 0.5) * 0.10, 0, 1)[..., None]
        col = np.array([0.66, 0.73, 0.86], np.float32) * (1 - lit) + np.array([0.97, 0.98, 1.0], np.float32) * lit
        snow = np.dstack([col, mask[..., None]])
        self.im.alpha_composite(Image.fromarray((snow * 255).astype(np.uint8), "RGBA"))

    def done(self):
        """Graded, averaged down to the finished size."""
        a = np.asarray(self.im, dtype=np.float32) / 255.0
        return a

def grade_needles(a):
    """The scan's greens darker and a little cooler; the snow and the wood keep their colour."""
    rgb = a[..., :3]
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    green = (rgb[..., 1] >= rgb[..., 0] * 0.92) & (rgb[..., 1] > rgb[..., 2])
    k = np.clip(sat * 6, 0, 1) * green
    out = rgb * (1 - k[..., None]) + rgb * np.array(NEEDLE, np.float32) * k[..., None]
    return np.dstack([out, a[..., 3:]])

def twig_points(p0, p1, bow, n=8):
    """A gently bowed line from p0 to p1 (bow: sideways offset at the middle, pixels)."""
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(dx, dy) or 1
    nx, ny = -dy / L, dx / L
    return [(p0[0] + dx * t + nx * bow * math.sin(math.pi * t), p0[1] + dy * t + ny * bow * math.sin(math.pi * t)) for t in [i / n for i in range(n + 1)]]

WOOD_DARK, WOOD = (58, 44, 34, 255), (86, 64, 46, 255)

def draw_pad(rng, sprites, heavy):
    """A limb's end from above: the limb comes in from the left, forks, and every twig ends in a spray of shoots."""
    c = Canvas("pad_heavy" if heavy else "pad_light")
    W, H = c.w, c.h
    R = W * 0.47
    centre = (W * 0.52, H * 0.5)
    ends = []
    for ring, n, rr in ((0, 5, 0.26), (1, 10, 0.60)):
        for i in range(n):
            if ring == 1 and rng.random() < 0.15: continue          # a gap in the outline
            ang = -150 + 300 * (i + 0.5) / n + rng.uniform(-10, 10)
            r = R * rr * rng.uniform(0.78, 1.12)
            end = (centre[0] + r * math.cos(math.radians(ang)), centre[1] - r * math.sin(math.radians(ang)))
            ends.append((centre if ring == 0 else (centre[0] + (end[0] - centre[0]) * 0.3, centre[1] + (end[1] - centre[1]) * 0.3), end, ang))
    # the shaded underlayer first: darker shoots filling the middle
    for k in range(46):
        a = rng.uniform(-170, 170); r = R * math.sqrt(rng.random()) * 0.62
        p = (centre[0] + r * math.cos(math.radians(a)), centre[1] - r * math.sin(math.radians(a)))
        c.stamp(rng.choice(sprites), p, a + rng.uniform(-60, 60), R * rng.uniform(0.34, 0.44), shade=rng.uniform(0.36, 0.56), flip=rng.random() < 0.5)
    # the wood
    c.line(twig_points((0, centre[1]), centre, rng.uniform(-6, 6) * S), 9 * S, WOOD_DARK)
    for start, end, ang in ends:
        c.line(twig_points(start, end, rng.uniform(-8, 8) * S), 3.6 * S, WOOD)
    # the shoots in the light: a spray at each twig's end, and some along it
    order = list(range(len(ends))); rng.shuffle(order)
    for i in order:
        start, end, ang = ends[i]
        for k in range(2):
            t = rng.uniform(0.35, 0.8)
            p = (start[0] + (end[0] - start[0]) * t, start[1] + (end[1] - start[1]) * t)
            c.stamp(rng.choice(sprites), p, ang + rng.choice([-1, 1]) * rng.uniform(30, 65), R * rng.uniform(0.30, 0.40), shade=rng.uniform(0.72, 0.92), flip=rng.random() < 0.5)
        for d in (-52, -26, 0, 26, 52):
            c.stamp(rng.choice(sprites), end, ang + d + rng.uniform(-9, 9), R * rng.uniform(0.34, 0.46), shade=rng.uniform(0.9, 1.1), flip=rng.random() < 0.5)
    blobs = []
    for start, end, ang in ends:
        if rng.random() > (0.8 if heavy else 0.45): continue
        k = 1.0 if heavy else 0.72
        tip = (end[0] + R * 0.14 * math.cos(math.radians(ang)), end[1] - R * 0.14 * math.sin(math.radians(ang)))
        blobs.append((tip[0] + rng.uniform(-8, 8) * S, tip[1] + rng.uniform(-8, 8) * S, R * rng.uniform(0.15, 0.22) * k, R * rng.uniform(0.13, 0.2) * k))
    return c, blobs, False

def draw_bough(rng, sprites, heavy):
    """A limb's end from the side: the limb rises to the right, the shoots stand up off it, snow sits on them."""
    c = Canvas("bough_b" if heavy else "bough_a")
    W, H = c.w, c.h
    p0, p1, p2 = (0, H * 0.5), (W * 0.5, H * 0.74), (W * 0.86, H * 0.42)   # dips, then sweeps up to its tip
    def at(t):
        u = 1 - t
        return (u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1])
    def slope(t):
        a, b = at(max(0, t - 0.02)), at(min(1, t + 0.02))
        return math.degrees(math.atan2(-(b[1] - a[1]), b[0] - a[0]))
    L = H * 0.40
    tops = []
    # behind: darker shoots, some hanging under the limb
    for k in range(34):
        t = rng.uniform(0.1, 1.0)
        ang = slope(t) + rng.choice([rng.uniform(20, 100), rng.uniform(20, 100), rng.uniform(-60, -10)])
        c.stamp(rng.choice(sprites), at(t), ang, L * rng.uniform(0.7, 1.0), shade=rng.uniform(0.38, 0.58), flip=rng.random() < 0.5)
    c.line([at(i / 16) for i in range(17)], 8 * S, WOOD_DARK)
    # in front: the brushes standing up along the limb, and a fan at the tip
    n = 24
    for k in range(n):
        t = 0.1 + 0.9 * (k + rng.random()) / n
        ang = slope(t) + rng.uniform(30, 95)
        ln = L * rng.uniform(0.8, 1.08)
        p = at(t)
        c.stamp(rng.choice(sprites), p, ang, ln, shade=rng.uniform(0.82, 1.08), flip=rng.random() < 0.5)
        tops.append((p[0] + ln * 0.78 * math.cos(math.radians(ang)), p[1] - ln * 0.78 * math.sin(math.radians(ang))))
    for d in (-25, -5, 15, 38):
        c.stamp(rng.choice(sprites), at(1.0), slope(1.0) + d, L * rng.uniform(0.8, 1.0), shade=1.0, flip=rng.random() < 0.5)
    blobs = []
    for (x, y) in tops:
        if rng.random() > (0.85 if heavy else 0.4): continue
        blobs.append((x, y + rng.uniform(-4, 6) * S, L * rng.uniform(0.2, 0.36) * (1.2 if heavy else 1.0), L * rng.uniform(0.1, 0.17) * (1.3 if heavy else 1.0)))
    return c, blobs, True

def draw_twig(rng, sprites, name, snowy):
    """A twig's end, its shoots swept upward."""
    c = Canvas(name)
    W, H = c.w, c.h
    p0, p1 = (0, H * 0.5), (W * 0.42, H * 0.56)
    L = W * 0.5
    for k in range(3):
        c.stamp(rng.choice(sprites), (p1[0] * rng.uniform(0.4, 0.9), p1[1]), rng.uniform(-25, 50), L * rng.uniform(0.7, 0.9), shade=0.55, flip=rng.random() < 0.5)
    c.line(twig_points(p0, p1, 3 * S), 4 * S, WOOD)
    tops = []
    for ang in (-14, 6, 24, 44):
        a = ang + rng.uniform(-6, 6); ln = L * rng.uniform(0.85, 1.05)
        q = (p1[0] * rng.uniform(0.75, 1.0), p1[1])
        c.stamp(rng.choice(sprites), q, a, ln, shade=rng.uniform(0.9, 1.08), flip=rng.random() < 0.5)
        tops.append((q[0] + ln * 0.6 * math.cos(math.radians(a)), q[1] - ln * 0.6 * math.sin(math.radians(a))))
    blobs = [(x, y - 2 * S, L * rng.uniform(0.2, 0.3), L * rng.uniform(0.07, 0.11)) for (x, y) in tops[1:]] if snowy else []
    return c, blobs, True

def draw_dead(rng, name, snowy):
    """A bare dead branch: grey, forked, its ends snapped."""
    c = Canvas(name)
    W, H = c.w, c.h
    tops = []
    def grow(p, ang, length, width, depth):
        end = (p[0] + length * math.cos(math.radians(ang)), p[1] - length * math.sin(math.radians(ang)))
        pts = twig_points(p, end, rng.uniform(-0.06, 0.06) * length, 6)
        c.line(pts, width, (92, 86, 80, 255))
        c.line([(x, y - width * 0.25) for x, y in pts], max(1, width * 0.4), (128, 122, 114, 255))   # the light along its top
        if depth == 0: tops.extend(pts[2:5])
        if depth >= 3 or length < 14 * S: return
        for k in range(rng.randint(2, 4) if depth == 0 else rng.randint(1, 2)):
            t = rng.uniform(0.25, 0.9)
            q = pts[int(t * 6)]
            side = 1 if (k + depth) % 2 == 0 else -1
            grow(q, ang + side * rng.uniform(28, 55), length * rng.uniform(0.3, 0.5) * (1.1 - t * 0.4), max(1, width * 0.55), depth + 1)
    grow((0, H * 0.5), rng.uniform(-3, 4), W * 0.94, 8 * S, 0)
    blobs = [(x, y - 3 * S, 34 * S, 3.2 * S) for (x, y) in tops] if snowy else []
    return c, blobs, True

def bark_strip(maps, name, rng):
    """A strip of the scanned bark with its relief shaded in, turned so the trunk runs along it."""
    x0, y0, x1, y1 = CELLS[name]
    w, h = x1 - x0, y1 - y0
    if name == "bark_mid":
        a, b = bark_strip(maps, "bark", rng)[:, ::-1], bark_strip(maps, "bark_hi", rng)[:, ::-1]
        t = np.linspace(0, 1, w, dtype=np.float32)[None, :, None]
        t = np.clip((t - 0.15) / 0.7 + (value_noise(rng, w, h, 20)[..., None] - 0.5) * 0.5, 0, 1)
        return a * (1 - t) + b * t
    diff = np.asarray(Image.open(maps["bark_diff"]).convert("RGB").resize((1024, 1024), Image.LANCZOS), np.float32) / 255
    ao = np.asarray(Image.open(maps["bark_ao"]).convert("L").resize((1024, 1024), Image.LANCZOS), np.float32) / 255
    off = {"bark": 0, "bark_hi": 380, "wood": 700}[name]
    # h pixels round the trunk (the scan's x), w along it (the scan's y)
    sub = diff[0:w * 2, off:off + h * 2].transpose(1, 0, 2); sao = ao[0:w * 2, off:off + h * 2].T
    sub = np.asarray(Image.fromarray((sub * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS), np.float32) / 255
    sao = np.asarray(Image.fromarray((sao * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS), np.float32) / 255
    lum = sub.mean(axis=2, keepdims=True)
    relief = np.clip((sao - sao.mean()) * 3.2 + (lum[..., 0] - lum.mean()) * 2.4, -0.6, 0.6)[..., None]
    if name == "bark":       # dark grey-brown plates, deep cracks
        rgb = (sub * 0.6 + lum * 0.4) * np.array([1.18, 1.0, 0.86], np.float32) * 1.2 * (1 + relief * 0.9)
    elif name == "bark_hi":  # the orange of a pine's upper trunk
        rgb = lum * np.array([1.62, 1.08, 0.70], np.float32) * 1.3 * (1 + relief * 0.8)
    else:                    # weathered wood: silver, with the grain running along the trunk
        grain = np.asarray(Image.fromarray((np.tile(value_noise(rng, 24, h * 4, 2)[:, :1], (1, 24)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
        rgb = (0.50 + (grain[..., None] - 0.5) * 0.30) * np.array([1.0, 0.98, 0.94], np.float32) * (1 + relief * 0.55)
    return np.dstack([np.clip(rgb, 0, 1), np.ones((h, w, 1), np.float32)])

def build_atlas(maps):
    rng = random.Random(0x5EED91)
    sprites = load_sprites(maps)
    atlas = np.zeros((SIZE, SIZE, 4), np.float32)
    atlas[..., :3] = np.array(FILL, np.float32) / 255
    def put(name, a):
        x0, y0, x1, y1 = CELLS[name]
        atlas[y0:y1, x0:x1] = a
    def finish(name, c, blobs, side):
        x0, y0, x1, y1 = CELLS[name]
        im = Image.fromarray((grade_needles(c.done()) * 255).astype(np.uint8), "RGBA")
        c.im = im
        if blobs: c.snow(rng, blobs, side)
        # keep clear of the cell's edge, so nothing bleeds into its neighbour
        a = np.asarray(c.im.resize((x1 - x0, y1 - y0), Image.LANCZOS), np.float32) / 255
        edge = np.ones(a.shape[:2], np.float32)
        edge[:3, :] = 0; edge[-3:, :] = 0; edge[:, -3:] = 0
        a[..., 3] *= edge
        fill = np.array(FILL, np.float32) / 255
        vis = a[..., 3:4]
        a[..., :3] = np.where(vis > 0.02, a[..., :3], fill)
        put(name, a)
    for heavy in (False, True):
        finish("pad_heavy" if heavy else "pad_light", *draw_pad(rng, sprites, heavy))
        finish("bough_b" if heavy else "bough_a", *draw_bough(rng, sprites, heavy))
    finish("twig_a", *draw_twig(rng, sprites, "twig_a", True))
    finish("twig_b", *draw_twig(rng, sprites, "twig_b", True))
    finish("twig_c", *draw_twig(rng, sprites, "twig_c", False))
    finish("dead_a", *draw_dead(rng, "dead_a", False))
    finish("dead_b", *draw_dead(rng, "dead_b", True))
    for name in ("bark", "bark_mid", "bark_hi", "wood"): put(name, bark_strip(maps, name, rng))
    for name, col in (("core", (0.075, 0.115, 0.07)), ("snow", (0.93, 0.95, 0.98)), ("hollow", (0.07, 0.06, 0.05))):
        x0, y0, x1, y1 = CELLS[name]
        atlas[y0:y1, x0:x1] = np.array([*col, 1.0], np.float32)
    return Image.fromarray((np.clip(atlas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")


# ------------------------------------------------------------------------------------------------ meshes

def V(x, y, z): return np.array((x, y, z), np.float64)
UP = V(0, 1, 0)
def norm(v):
    l = math.sqrt(float(v @ v))
    return v / l if l > 1e-9 else V(0, 0, 0)
def lerp(a, b, t): return a + (b - a) * t
def clamp01(x): return max(0.0, min(1.0, x))
def horiz(az): return V(math.cos(az), 0, math.sin(az))
def turn(v, axis, deg):
    """v turned about an axis (Rodrigues)."""
    a = norm(axis); c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return v * c + np.cross(a, v) * s + a * float(a @ v) * (1 - c)
def tri_wave(x): return 1 - abs(1 - 2 * (x % 1.0))

def uv_rect(px, inset=1.5):
    """A cell given in pixels from the top left, as (u0, v0, u1, v1) with v0 its bottom edge."""
    x0, y0, x1, y1 = px
    return ((x0 + inset) / SIZE, 1 - (y1 - inset) / SIZE, (x1 - inset) / SIZE, 1 - (y0 + inset) / SIZE)
PINE = {name: uv_rect(px) for name, px in CELLS.items()}

# the firs' atlas (Tools/MakeFirCards.swift): 2 columns x 4 rows of 512 x 256 cells
# row 0: upright with a snow cap; row 1: flat, a dusting; row 2: flat, heavy snow; row 3: bark (958 px) and a flat green
def fir_cell(col, row):
    px = 1 / 1024
    return (col * 0.5 + px, 1 - (row + 1) * 0.25 + px, col * 0.5 + 0.5 - px, 1 - row * 0.25 - px)
FIR_BARK = (1 / 1024, 1 / 1024, 958 / 1024, 0.25 - 1 / 1024)
FIR_CORE = (966 / 1024, 0.02, 1018 / 1024, 0.23)   # the flat shadowed green at the end of the bark row
STEM_UPRIGHT = 0.44                        # where the stem runs across an upright card (0 bottom .. 1 top)
STEM_START, STEM_END = 12 / 512, 498 / 512 # where a frond's stem starts and ends along its card

class Mesh:
    def __init__(self):
        self.v, self.n, self.uv, self.ao, self.wood, self.t = [], [], [], [], [], []
        self.is_wood = False               # trunk and limbs (left out when the crown is measured)
    def vert(self, p, n, uv, ao):
        self.v.append(p); self.n.append(norm(n)); self.uv.append(uv); self.ao.append(clamp01(ao)); self.wood.append(self.is_wood)
        return len(self.v) - 1
    def quad(self, a, b, c, d):            # a, b along the bottom edge; c, d above them
        self.t.append((a, c, b)); self.t.append((b, c, d))
    def tri(self, a, b, c): self.t.append((a, b, c))

def ribbon(m, pts, across, cell, stem, nrm, shade, rows=1, sag=None, u0=0.0, u1=1.0):
    """A card that follows a spine: `pts` from the trunk end to the tip, `across[i]` from the cell's bottom edge
    to its top at each. `rows` quads across; `sag` is added to each vertex by how far it is from the stem."""
    cu0, cv0, cu1, cv1 = cell
    grid = []
    for i, (p, ac) in enumerate(zip(pts, across)):
        a = i / (len(pts) - 1)
        row = []
        for j in range(rows + 1):
            b = j / rows
            q = p + ac * (b - stem)
            if sag is not None: q = q + sag * (abs(b - stem) * 2)
            row.append(m.vert(q, nrm(q), (lerp(cu0, cu1, lerp(u0, u1, a)), lerp(cv0, cv1, b)), shade(q)))
        grid.append(row)
    for i in range(len(pts) - 1):
        for j in range(rows):
            m.quad(grid[i][j], grid[i + 1][j], grid[i][j + 1], grid[i + 1][j + 1])

def card(m, origin, along, across, cell, stem, nrm, shade):
    """A flat card: `along` runs from the trunk end of the cell to its tip, `across` from its bottom edge to its top."""
    ribbon(m, [origin, origin + along], [across, across], cell, stem, nrm, shade)

def tube(m, centres, radii, cell, sides, tile, shade, upright=False, lobes=None, start=0.0):
    """A trunk or limb through `centres`. The cell is a strip the wood runs along: it repeats every `tile` metres,
    every other time mirrored, and goes once there and back round the girth, so it never shows a seam.
    Returns the length covered, counted from `start`."""
    was = m.is_wood; m.is_wood = True
    cu0, cv0, cu1, cv1 = cell
    d = start
    rows = []
    for k, (c, r) in enumerate(zip(centres, radii)):
        if k > 0: d += math.sqrt(float((c - centres[k - 1]) @ (c - centres[k - 1])))
        tan = norm(centres[min(k + 1, len(centres) - 1)] - centres[max(k - 1, 0)])
        if upright or abs(tan[1]) > 0.95: e1, e2 = V(1, 0, 0), V(0, 0, 1)
        else:
            e1 = norm(np.cross(UP, tan)); e2 = np.cross(tan, e1)
        row = []
        for i in range(sides):
            ang = 2 * math.pi * i / sides
            out = e1 * math.cos(ang) + e2 * math.sin(ang)
            rr = r * (lobes(ang, k) if lobes else 1.0)
            p = c + out * rr
            row.append(m.vert(p, out, (lerp(cu0, cu1, tri_wave(d / tile / 2)), lerp(cv0, cv1, tri_wave(i / sides))), shade(p)))
        rows.append(row)
    for k in range(len(rows) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            m.quad(rows[k][i], rows[k][j], rows[k + 1][i], rows[k + 1][j])
    m.is_wood = was
    return d, rows

# ---- firs and hemlocks

FIR_DEFAULTS = dict(nod=False, broken=0.0, shape=0.0, girth=0.02, tip=0.06)
# H: height (m); reach: longest branch (at the crown's foot) as a share of the height; crownBase: where the branches
# start; droopLo/Hi: branch droop (degrees) at the foot and the top of the crown; flat, heavy: chance of a flat
# snow-loaded card under each branch, and of it being heavily loaded; ragged: branches missing and uneven;
# lopside: one side fuller; nod: the leader droops over (hemlocks); broken: where the top snapped off (share of the
# height); shape: how the branches shorten up the crown (1 a straight cone, less a rounder crown); girth: the
# trunk's radius at its foot as a share of the height; tip: how far a branch's end lifts (hemlocks: droops)
LEGACY = [
    # subalpine firs: tall, narrow spires, branches nearly level, snow along them
    dict(H=24, reach=0.12, crownBase=0.12, droopLo=22, droopHi=8, flat=0.85, heavy=0.7, ragged=0.15, lopside=0.1),
    dict(H=27, reach=0.10, crownBase=0.16, droopLo=18, droopHi=6, flat=0.8, heavy=0.6, ragged=0.25, lopside=0.2),
    # the same firs plastered: high up, where the storms load every branch
    dict(H=20, reach=0.17, crownBase=0.08, droopLo=30, droopHi=14, flat=1.0, heavy=0.97, ragged=0.2, lopside=0.15, shape=0.55),
    dict(H=18, reach=0.19, crownBase=0.06, droopLo=34, droopHi=16, flat=1.0, heavy=0.95, ragged=0.3, lopside=0.25, shape=0.5),
    # mountain hemlocks: drooping branches and a nodding top, ragged, lopsided
    dict(H=24, reach=0.15, crownBase=0.14, droopLo=42, droopHi=24, flat=0.7, heavy=0.65, ragged=0.35, lopside=0.25, nod=True, tip=-0.05),
    dict(H=29, reach=0.13, crownBase=0.18, droopLo=46, droopHi=26, flat=0.65, heavy=0.6, ragged=0.45, lopside=0.35, nod=True, tip=-0.05),
    # a fir whose top has snapped off: a bare spike above the last branches
    dict(H=25, reach=0.12, crownBase=0.15, droopLo=24, droopHi=10, flat=0.8, heavy=0.7, ragged=0.35, lopside=0.2, broken=0.74),
    # a thin, ragged hemlock with gaps in its crown
    dict(H=22, reach=0.17, crownBase=0.2, droopLo=44, droopHi=22, flat=0.6, heavy=0.55, ragged=0.6, lopside=0.4, nod=True, tip=-0.05),
]
FIR_YOUNG = dict(H=9, reach=0.21, crownBase=0.05, droopLo=18, droopHi=4, flat=0.85, heavy=0.6, ragged=0.08, lopside=0.08, shape=0.9, girth=0.014, tip=0.09)
FIR_GIANT = dict(H=38, reach=0.105, crownBase=0.24, droopLo=40, droopHi=12, flat=0.8, heavy=0.7, ragged=0.42, lopside=0.3, girth=0.021, tip=0.04)

def fir_trunk_radius(sp, y):
    H = sp["H"]; broken = sp["broken"]
    y0, y1 = -0.3, H * (broken + 0.1 if broken > 0 else 0.8)
    r0, r1 = H * sp["girth"], H * (0.006 if broken > 0 else 0.004)
    return lerp(r0, r1, clamp01((y - y0) / (y1 - y0)))

def fir_crown_radius(sp, y):
    """The crown's radius (m) at a height: what its branches reach."""
    H = sp["H"]; cb = sp["crownBase"]
    f = clamp01((y / H - cb) / (0.92 - cb))
    return H * sp["reach"] * (1 - f) ** (sp["shape"] if sp["shape"] > 0 else 0.95) + 0.35

def grow_fir(sp, rng):
    """The tree as a list of branches (shared by its two meshes)."""
    H = sp["H"]; cb = sp["crownBase"]
    top = sp["broken"] if sp["broken"] > 0 else 1.0
    tiers = round(H * (0.92 - cb) * 0.95) + rng.randrange(3)
    spacing = H * (0.92 - cb) / tiers
    twist = rng.random() * 360; lop = rng.random() * 2 * math.pi
    out = []
    for i in range(tiers):
        f = i / (tiers - 1)
        y = H * (cb + (0.92 - cb) * f ** 0.92) + rng.uniform(-0.2, 0.2)
        if y > H * top: break
        reach = H * sp["reach"] * (1 - f) ** (sp["shape"] if sp["shape"] > 0 else 0.95) + 0.35
        count = round(lerp(8, 5, f))
        twist += 137.5
        for j in range(count):
            if rng.random() < sp["ragged"] * 0.3: continue        # a missing branch: ragged gaps
            az = math.radians(twist + j * 360 / count + rng.uniform(-18, 18))
            by = y + rng.uniform(-0.5, 0.5) * spacing             # not quite level: no shelves
            droop = lerp(sp["droopLo"], sp["droopHi"], f) + rng.uniform(-10, 10)
            uneven = 1 + sp["ragged"] * rng.uniform(-0.45, 0.35) + sp["lopside"] * math.cos(az - lop)
            length = reach * max(0.35, uneven) * rng.uniform(0.85, 1.12) / math.cos(math.radians(droop))
            b = dict(f=f, az=az, y=by, droop=droop, len=length, col=rng.randrange(2),
                     flat=rng.random() < lerp(sp["flat"], sp["flat"] * 0.55, f), roll=rng.uniform(-15, 15),
                     row=2 if rng.random() < lerp(sp["heavy"], sp["heavy"] * 0.45, f) else 1,
                     plaster=sp["heavy"] > 0.9 and rng.random() < 0.7, proll=rng.uniform(28, 42) * rng.choice((-1, 1)),
                     seed=rng.randrange(1 << 30))
            out.append(b)
    return dict(branches=out, tiers=tiers, spacing=spacing, lop=lop, top=top)

def fir_mesh(sp, tree, rich):
    m = Mesh()
    H = sp["H"]; cb = sp["crownBase"]; top = tree["top"]; spacing = tree["spacing"]
    def nrm(p):   # soft, rounded lighting: the normal leans out from the trunk and up, whatever way the card faces
        r = V(p[0], 0, p[2])
        return (norm(r) if float(r @ r) > 1e-4 else V(1, 0, 0)) * 0.65 + UP * 0.6
    def shade(p):   # darker toward the trunk: the inside of the crown is in its own shadow
        q = clamp01(math.hypot(p[0], p[2]) / fir_crown_radius(sp, p[1]))
        return 0.46 + 0.54 * q ** 0.85
    def wood_shade(p):
        if p[1] > H * cb: return 0.55
        return lerp(0.72, 1.0, clamp01((p[1] - 0.2) / 1.2))       # darker where it meets the snow
    y1 = H * (sp["broken"] + 0.1 if sp["broken"] > 0 else 0.8)
    # ---- the trunk
    if rich:
        ys = [-0.3, 0.0, 0.3, 0.7, 1.4]
        while ys[-1] + 3.2 < y1: ys.append(ys[-1] + 3.2)
        ys.append(y1)
        ph = sp["H"] * 1.7
        def lobes(ang, k):   # the root flare: buttresses that die out a metre up
            y = ys[k]
            return 1 + 0.55 * math.exp(-(y + 0.3) / 0.42) * (1 + 0.32 * math.cos(5 * ang + ph) + 0.18 * math.cos(3 * ang - ph))
        tube(m, [UP * y for y in ys], [fir_trunk_radius(sp, y) for y in ys], FIR_BARK, 10, 3.4, wood_shade, upright=True, lobes=lobes)
    else:
        tube(m, [UP * -0.3, UP * y1], [fir_trunk_radius(sp, -0.3), fir_trunk_radius(sp, y1)], FIR_BARK, 6, y1 + 0.3, wood_shade, upright=True)
    # ---- the core: a dark cone inside the crown, so you never see straight through the tree
    cy0, cy1, cr0 = H * (cb + 0.04), H * top * 0.86, H * sp["reach"] * 0.42
    sides = 9 if rich else 7
    cu0, cv0, cu1, cv1 = FIR_CORE
    if rich: cr0 *= 0.4   # rich: only a thin dark sleeve round the trunk (never see-through); inner fronds do the rest
    rings = [(cy0, cr0), (lerp(cy0, cy1, 0.45), cr0 * 0.66), (cy1, 0.0)] if rich else [(cy0, cr0), (cy1, 0.0)]
    rows = []
    for (y, r) in rings:
        row = []
        for i in range(sides if r > 0 else 1):
            a = i * 2 * math.pi / sides
            d = horiz(a)
            row.append(m.vert(d * r + UP * y, (d + UP * 0.5) if r > 0 else UP, (lerp(cu0, cu1, (y - cy0) / (cy1 - cy0)), lerp(cv0, cv1, tri_wave(i / sides))), 0.4 if rich else 0.62))
        rows.append(row)
    for k in range(len(rows) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            if len(rows[k + 1]) == 1: m.tri(rows[k][i], rows[k + 1][0], rows[k][j])
            else: m.quad(rows[k][i], rows[k][j], rows[k + 1][i], rows[k + 1][j])
    # ---- the branches
    for b in tree["branches"]:
        rb = random.Random(b["seed"])
        out = horiz(b["az"])
        dr = math.radians(b["droop"])
        d = out * math.cos(dr) - UP * math.sin(dr)                # tip down
        length = b["len"]
        clen = length / (STEM_END - STEM_START)
        origin = UP * b["y"] - d * (clen * STEM_START)
        side = norm(np.cross(UP, d)); up = norm(np.cross(d, side))
        wid = clen * 0.5
        tall = max(wid * 0.85, spacing * 1.6)
        col = b["col"]
        if not rich:
            card(m, origin, d * clen, up * tall, fir_cell(col, 0), STEM_UPRIGHT, nrm, shade)             # upright, snow cap on top
            if b["flat"]: card(m, origin, d * clen, turn(side, d, b["roll"]) * wid * 1.15, fir_cell(1 - col, b["row"]), 0.5, nrm, shade)
            if b["plaster"]: card(m, origin + UP * 0.08, d * clen * 0.85, turn(side, d, b["proll"]) * wid, fir_cell(col, 2), 0.5, nrm, shade)
            continue
        # rich: the branch sags along its length and its end lifts (a hemlock's droops); long ones in three pieces
        sag, lift = rb.uniform(0.03, 0.06), sp["tip"] * rb.uniform(0.7, 1.3)
        segs = 3 if length > 1.6 else 2
        def spine(t): return origin + d * (clen * t) + up * (clen * (-sag * math.sin(math.pi * t) + lift * t ** 3))
        pts = [spine(i / segs) for i in range(segs + 1)]
        ribbon(m, pts, [up * tall] * len(pts), fir_cell(col, 0), STEM_UPRIGHT, nrm, shade)
        if b["flat"]:
            ac = turn(side, d, b["roll"]) * wid * 1.15
            ribbon(m, pts, [ac] * len(pts), fir_cell(1 - col, b["row"]), 0.5, nrm, shade, rows=2, sag=-up * wid * 0.13)   # its edges hang
        if b["plaster"]:
            ribbon(m, [p + UP * 0.08 for p in pts], [turn(side, d, b["proll"]) * wid] * len(pts), fir_cell(col, 2), 0.5, nrm, shade, u1=0.85)
        # side fronds forking off it, alternately left and right, hanging a little lower
        if length > 1.5 and b["f"] < 0.8:
            n = 2 + (length > 2.4) + (length > 3.4)
            for k in range(n):
                t = 0.26 + 0.5 * (k + rb.uniform(0.2, 0.8)) / n
                s = 1 if k % 2 == 0 else -1
                fork = math.radians(rb.uniform(38, 58))
                sd = norm(d * math.cos(fork) + side * (s * math.sin(fork)) - up * math.tan(math.radians(rb.uniform(6, 18))))
                sl = max(0.55, length * (1 - t) * rb.uniform(0.75, 1.05))
                scl = sl / (STEM_END - STEM_START)
                ac = turn(norm(np.cross(up, sd)), sd, rb.uniform(-28, 28)) * scl * 0.5
                card(m, spine(t) - sd * (scl * STEM_START), sd * scl, ac, fir_cell(rb.randrange(2), b["row"] if rb.random() < 0.6 else 1), 0.5, nrm, shade)
    # rich: the inside of the crown is needles, not a solid shape: whorls of short dark fronds off the trunk between
    # the tiers, some lying flat and some on edge, turned from one whorl to the next so that from any side one
    # stands across the view
    if rich:
        rb = random.Random(len(tree["branches"]) * 31 + 7)
        y = H * (cb + 0.03)
        az0 = rb.random() * 2 * math.pi
        def inner(p): return shade(p) * 0.78
        while y < H * min(top, 0.9) - 0.4:
            r = fir_crown_radius(sp, y)
            n = 6 if r > 1.2 else 4
            az0 += 0.9
            for k in range(n):
                az = az0 + k * 2 * math.pi / n + rb.uniform(-0.25, 0.25)
                e = math.radians(rb.uniform(-12, 28))
                d = horiz(az) * math.cos(e) + UP * math.sin(e)
                sl = max(0.5, r * rb.uniform(0.5, 0.72)); scl = sl / (STEM_END - STEM_START)
                side = norm(np.cross(UP, d))
                on_edge = k % 2 == 1
                ac = turn(side, d, (90 if on_edge else 0) + rb.uniform(-25, 25)) * (scl * (0.7 if on_edge else 0.55))
                card(m, UP * (y + rb.uniform(-0.3, 0.3)) - d * (scl * STEM_START), d * scl, ac, fir_cell(rb.randrange(2), 0 if on_edge else 1), STEM_UPRIGHT if on_edge else 0.5, nrm, inner)
            y += max(0.8, spacing * 0.9)
    # ---- the leader: crossed fronds pointing up (a hemlock's nods over; a snapped tree has none)
    if sp["broken"] <= 0:
        y, length = H * 0.86, H * 0.14
        lop = tree["lop"]
        lead = norm(UP + horiz(lop) * 0.55) if sp["nod"] else UP
        k = length / (STEM_END - STEM_START)
        first = norm(np.cross(lead, V(1, 0, 0)))
        n = 3 if rich else 2
        for i in range(n):
            side = turn(first, lead, i * 180 / n)
            card(m, UP * y - lead * (k * STEM_START), lead * k, side * length * 0.3, fir_cell(i % 2, 1), 0.5, nrm, shade)
    return m

def fir_tables(sp):
    """Trunk radius (m) and crown radius (share of the height) at 17 heights, for the game's hit capsules."""
    H = sp["H"]; top = sp["broken"] if sp["broken"] > 0 else 0.95
    trunk = [fir_trunk_radius(sp, H * i / 16) for i in range(17)]
    crown = []
    for i in range(17):
        at = i / 16
        crown.append(0.0 if at < sp["crownBase"] or at > top else fir_crown_radius(sp, at * H) / H)
    return trunk, crown

# ---- pines

# H: height (m); girth: the trunk's radius at its foot as a share of the height; bare: how far up the trunk is bare;
# crown: the crown's widest radius as a share of the height; limbs: how many; pad: a pad of needles' radius (m);
# heavy: snow load 0..1; elev0/1: a limb's angle up from level at the foot and the top of the crown; sweep: how
# hard its end curls upward; bend: how far the trunk leans over as it goes up (share of the height);
# lopside: one side fuller; cone: 0 a broad flat-topped crown, 1 a young tree's cone
PINE_A = dict(H=26, girth=0.019, bare=0.48, crown=0.21, limbs=15, pad=2.3, heavy=0.25, elev0=-6, elev1=48, sweep=0.34, bend=0.03, lopside=0.25, cone=0.0)
PINE_B = dict(H=29, girth=0.018, bare=0.58, crown=0.19, limbs=12, pad=2.5, heavy=0.8, elev0=-12, elev1=40, sweep=0.42, bend=0.06, lopside=0.45, cone=0.0)
PINE_GIANT = dict(H=35, girth=0.023, bare=0.44, crown=0.23, limbs=17, pad=3.0, heavy=0.5, elev0=-14, elev1=42, sweep=0.3, bend=0.025, lopside=0.3, cone=0.0)
PINE_YOUNG = dict(H=10, girth=0.014, bare=0.18, crown=0.21, limbs=13, pad=1.25, heavy=0.4, elev0=18, elev1=55, sweep=0.5, bend=0.01, lopside=0.1, cone=1.0)
PINE_TOP = 0.88            # where the trunk ends, share of the height (the last limbs and pads make the top)

def pine_crown(sp, s):
    """The crown's radius (share of the height) at s: 0 its foot .. 1 its top."""
    broad = (0.55 + 0.45 * math.sin(math.pi * min(1.0, s * 1.3))) * (1.0 if s < 0.75 else lerp(1.0, 0.5, (s - 0.75) / 0.25))
    cone = 1.0 - 0.72 * s
    return sp["crown"] * lerp(broad, cone, sp["cone"])

def pine_trunk_radius(sp, y):
    H = sp["H"]
    return H * sp["girth"] * (1 - 0.74 * clamp01((y + 0.3) / (H * PINE_TOP + 0.3)) ** 1.1)

def grow_pine(sp, rng):
    H = sp["H"]
    lop = rng.random() * 2 * math.pi
    lean = horiz(rng.random() * 2 * math.pi); wig = horiz(rng.random() * 2 * math.pi)
    def axis(y):   # the trunk is never dead straight: it leans as it goes up, with a slow wave
        t = max(0.0, y / H)
        return lean * (sp["bend"] * H * t * t) + wig * (sp["bend"] * 0.35 * H * math.sin(math.pi * 1.5 * t) * t) + UP * y
    limbs = []
    az = rng.random() * 360
    n = sp["limbs"]
    for i in range(n):
        s = (i + rng.uniform(0.15, 0.85)) / n
        az += 137.5 + rng.uniform(-28, 28)
        a = math.radians(az)
        y = H * lerp(sp["bare"], PINE_TOP, s)
        reach = pine_crown(sp, s) * H * rng.uniform(0.72, 1.12) * (1 + sp["lopside"] * math.cos(a - lop))
        reach = max(reach, sp["pad"] * 0.9)
        elev = lerp(sp["elev0"], sp["elev1"], s) + rng.uniform(-9, 9)
        sweep = sp["sweep"] * lerp(1.0, 0.5, s) * rng.uniform(0.8, 1.2)
        base, out = axis(y), horiz(a)
        def point(t, base=base, out=out, reach=reach, elev=elev, sweep=sweep):
            return base + out * (reach * t) + UP * (math.tan(math.radians(elev)) * reach * t + sweep * reach * t * t)
        pads = []
        j, t = 0, 1.0
        while t > 0.3 and j < 4:
            c = point(t)
            r = sp["pad"] * rng.uniform(0.82, 1.15) * (1.0 if j == 0 else 0.86)
            sidev = norm(np.cross(UP, out))
            if j > 0: c = c + sidev * (r * 0.55 * (1 if j % 2 else -1)) + UP * (r * 0.1)
            fwd = turn(out, UP, rng.uniform(-25, 25) + (0 if j == 0 else 35 * (1 if j % 2 else -1)))
            tilt = rng.uniform(8, 24) + max(0.0, elev) * 0.25
            fwd = norm(fwd * math.cos(math.radians(tilt)) + UP * math.sin(math.radians(tilt)))
            pads.append(dict(c=c, r=r, fwd=fwd, out=out, heavy=rng.random() < sp["heavy"], deep=t, seed=rng.randrange(1 << 30)))
            t -= 1.25 * sp["pad"] / reach
            j += 1
        limbs.append(dict(point=point, y=y, reach=reach, pads=pads, seed=rng.randrange(1 << 30)))
    # the top: a pad sitting on the end of the trunk
    c = axis(H * PINE_TOP) + UP * (sp["pad"] * 0.35)
    fwd = norm(horiz(rng.random() * 6.28) + UP * 0.2)
    top = dict(c=c, r=sp["pad"] * 1.05, fwd=fwd, out=norm(V(fwd[0], 0, fwd[2])), heavy=rng.random() < sp["heavy"], deep=1.0, seed=rng.randrange(1 << 30))
    # dead stubs on the bare trunk, where limbs were shed
    stubs = []
    for k in range(rng.randint(3, 6)):
        y = H * sp["bare"] * rng.uniform(0.35, 0.98)
        stubs.append(dict(y=y, az=rng.random() * 2 * math.pi, len=rng.uniform(0.35, 1.5) * (H / 26) ** 0.5, elev=rng.uniform(-25, 15), card=rng.random() < 0.45))
    return dict(axis=axis, limbs=limbs, top=top, stubs=stubs)

def pine_pad(m, pad, rich):
    c, r, fwd = pad["c"], pad["r"], pad["fwd"]
    rb = random.Random(pad["seed"])
    side = norm(np.cross(UP, fwd)); up = norm(np.cross(fwd, side))
    def nrm(p):   # each pad is lit as one rounded lump
        return (p - c) * (0.8 / r) + UP * 0.8 + pad["out"] * 0.25
    def shade(p):   # its underside and the pads deep in the crown are darker
        return (0.58 + 0.42 * clamp01(float((p - c) @ UP) / r * 0.9 + 0.55)) * lerp(0.8, 1.0, pad["deep"])
    top = PINE["pad_heavy" if pad["heavy"] else "pad_light"]
    b1, b2 = (PINE["bough_b"], PINE["bough_a"]) if pad["heavy"] else (PINE["bough_a"], PINE["bough_b"])
    if not rich:
        card(m, c - fwd * r, fwd * (2 * r), side * (2 * r), top, 0.5, nrm, shade)
        card(m, c - fwd * (r * 1.05), fwd * (2.1 * r), up * (1.05 * r), b1, 0.42, nrm, shade)
        return
    # the top, tented: its middle stands proud, its edges hang, its tip is swept up
    pts = [c - fwd * r - up * (0.08 * r), c + up * (0.15 * r), c + fwd * r + up * (0.12 * r)]
    ribbon(m, pts, [side * (2 * r)] * 3, top, 0.5, nrm, shade, rows=2, sag=-up * (0.17 * r))
    # two uprights, crossed
    card(m, c - fwd * (r * 1.05), fwd * (2.1 * r), up * (1.05 * r), b1, 0.42, nrm, shade)
    s2 = turn(side, up, rb.uniform(-20, 20))
    card(m, c - s2 * (r * 0.95), s2 * (1.9 * r), up * (0.95 * r), b2, 0.42, nrm, shade)
    # twig ends round the rim, swept upward
    start = rb.uniform(0, 90)
    for k in range(4):
        a = math.radians(start + k * 90 + rb.uniform(-20, 20))
        d = fwd * math.cos(a) + side * math.sin(a)
        e = math.radians(rb.uniform(28, 50))
        td = norm(d * math.cos(e) + up * math.sin(e))
        ln = r * rb.uniform(0.8, 1.05)
        ac = norm(up - td * float(td @ up)) * (ln * 0.5)
        card(m, c + d * (0.42 * r) - up * (0.05 * r), td * ln, ac, PINE[rb.choice(("twig_a", "twig_b", "twig_c") if pad["heavy"] else ("twig_a", "twig_c", "twig_c"))], 0.5, nrm, shade)

def trunk_rings(y0, y1, step, marks):
    ys = [y0]
    while ys[-1] + step * 1.3 < y1: ys.append(ys[-1] + step)
    ys.append(y1)
    return sorted(set(ys + [y for y in marks if y0 < y < y1]))

def pine_mesh(sp, tree, rich):
    m = Mesh()
    H = sp["H"]; axis = tree["axis"]
    ytop = H * PINE_TOP
    crown_y = H * sp["bare"]
    def wood_shade(p):
        if p[1] > crown_y + 1: return 0.7
        return lerp(0.72, 1.0, clamp01((p[1] - 0.2) / 1.2))
    # ---- the trunk: dark plated bark below, giving way to orange about half way up the bare part
    yd = crown_y * 0.42                                           # the dark bark ends here
    nd = 1 if (yd + 0.3) < 3.2 else 3                             # ... after an odd number of strips (see bark_mid)
    tile_d = (yd + 0.3) / nd
    ym = min(yd + 3.2, ytop - 1.0)
    sides = 10 if rich else 6
    step = 2.4 if rich else H / 4.5
    ph = H * 1.3
    def run(y0, y1, cell, tile, flare=False, start=0.0):
        ys = trunk_rings(y0, y1, step, [0.0, 0.3, 0.7, 1.4] if (rich and flare) else [])
        def lobes(ang, k):
            y = ys[k]
            return 1 + 0.5 * math.exp(-(y + 0.3) / 0.42) * (1 + 0.3 * math.cos(5 * ang + ph) + 0.2 * math.cos(3 * ang - ph))
        tube(m, [axis(y) for y in ys], [pine_trunk_radius(sp, y) for y in ys], cell, sides, tile, wood_shade, upright=True, lobes=lobes if (rich and flare) else None, start=start)
    run(-0.3, yd, PINE["bark"], tile_d, flare=True)
    run(yd, ym, PINE["bark_mid"], ym - yd)
    run(ym, ytop, PINE["bark_hi"], 2.6)
    # ---- dead stubs
    for st in tree["stubs"]:
        if not rich and st["len"] < 0.8: continue
        out = horiz(st["az"]); e = math.radians(st["elev"])
        d = out * math.cos(e) + UP * math.sin(e)
        base = axis(st["y"]); r = min(0.09, pine_trunk_radius(sp, st["y"]) * 0.3)
        tube(m, [base, base + d * st["len"]], [r, r * 0.35], PINE["wood"], 4 if rich else 3, 2.0, lambda p: 0.9)
        if st["card"] and st["len"] > 0.7:
            ln = st["len"] * 2.2
            card(m, base + d * (st["len"] * 0.3), d * ln, norm(UP - d * float(d @ UP)) * (ln * 0.25), PINE["dead_a"], 0.5, lambda p: out + UP * 0.6, lambda p: 0.9)
    # ---- the limbs, and their pads
    for lb in tree["limbs"]:
        point = lb["point"]
        segs = 4 if rich else 2
        r0 = min(pine_trunk_radius(sp, lb["y"]) * 0.5, 0.05 + lb["reach"] * 0.028)
        pts = [point(i / segs) for i in range(segs + 1)]
        tube(m, pts, [lerp(r0, 0.03, (i / segs) ** 0.8) for i in range(segs + 1)], PINE["bark_hi"], 5 if rich else 3, 2.2, lambda p: 0.75)
        for pad in lb["pads"]: pine_pad(m, pad, rich)
    pine_pad(m, tree["top"], rich)
    return m

# ---- dead snags

SNAG_TALL = dict(H=19, girth=0.025, stubs=12, limbs=5, twist=0.0)
SNAG_OLD = dict(H=12, girth=0.036, stubs=6, limbs=5, twist=0.0)

def snag_trunk_radius(sp, y):
    H = sp["H"]
    return H * sp["girth"] * (1 - 0.62 * clamp01((y + 0.3) / (H + 0.3)))

def grow_snag(sp, rng):
    H = sp["H"]
    lean = horiz(rng.random() * 6.28)
    stubs = []
    for k in range(sp["stubs"] + sp["limbs"]):
        long = k >= sp["stubs"]
        stubs.append(dict(y=H * (rng.uniform(0.3, 0.85) if long else rng.uniform(0.22, 0.97)), az=rng.random() * 6.28,
                          len=rng.uniform(2.2, 3.8) if long else rng.uniform(0.3, 1.2), elev=rng.uniform(-18, 28), long=long,
                          snowy=rng.random() < 0.6, roll=rng.uniform(-30, 30)))
    jag = [rng.uniform(-0.7, 0.5) for _ in range(10)]
    return dict(stubs=stubs, jag=jag, lean=lean)

def snag_mesh(sp, tree, rich):
    m = Mesh()
    H = sp["H"]
    sides = 10 if rich else 6
    def axis(y): return tree["lean"] * (0.012 * H * (max(0.0, y) / H) ** 2) + UP * y
    def shade(p): return lerp(0.72, 1.0, clamp01((p[1] - 0.2) / 1.2))
    ys = trunk_rings(-0.3, H, 2.6 if rich else H / 3, [0.0, 0.3, 0.8] if rich else [])
    ph = H
    def lobes(ang, k):
        y = ys[k]
        return (1 + 0.5 * math.exp(-(y + 0.3) / 0.42) * (1 + 0.3 * math.cos(5 * ang + ph))) * (1 + 0.07 * math.cos(3 * ang + y * 0.5))
    _, rows = tube(m, [axis(y) for y in ys], [snag_trunk_radius(sp, y) for y in ys], PINE["wood"], sides, 2.6, shade, upright=True, lobes=lobes if rich else None)
    # the broken top: a jagged rim round a dark hollow
    was = m.is_wood; m.is_wood = True
    for i, idx in enumerate(rows[-1]):
        m.v[idx] = m.v[idx] + UP * tree["jag"][(i * 10) // sides]
    hu0, hv0, hu1, hv1 = PINE["hollow"]
    mid = m.vert(axis(H) - UP * 0.9, UP, ((hu0 + hu1) / 2, (hv0 + hv1) / 2), 0.6)
    rim = [m.vert(m.v[idx], UP, (lerp(hu0, hu1, 0.5 + 0.4 * math.cos(i * 6.28 / sides)), lerp(hv0, hv1, 0.5 + 0.4 * math.sin(i * 6.28 / sides))), 0.6) for i, idx in enumerate(rows[-1])]
    for i in range(sides): m.tri(rim[i], mid, rim[(i + 1) % sides])
    m.is_wood = was
    for st in tree["stubs"]:
        if not rich and not st["long"] and st["len"] < 0.7: continue
        out = horiz(st["az"]); e = math.radians(st["elev"])
        d = out * math.cos(e) + UP * math.sin(e)
        base = axis(st["y"]); r = min(0.13, snag_trunk_radius(sp, st["y"]) * 0.42)
        if rich:
            mid_p = base + d * (st["len"] * 0.5) - UP * (st["len"] * 0.04)
            tube(m, [base, mid_p, base + d * st["len"]], [r, r * 0.6, r * 0.25], PINE["wood"], 4, 2.0, lambda p: 0.92)
        else:
            tube(m, [base, base + d * st["len"]], [r, r * 0.25], PINE["wood"], 3, 2.0, lambda p: 0.92)
        if st["long"]:
            ln = st["len"] * 1.25
            ac = turn(norm(UP - d * float(d @ UP)), d, st["roll"]) * (ln * 0.25)
            nr = lambda p, out=out: out * 0.5 + UP * 0.7
            card(m, base + d * (st["len"] * 0.15), d * ln, ac, PINE["dead_b" if st["snowy"] else "dead_a"], 0.5, nr, lambda p: 0.95)
            if rich: card(m, base + d * (st["len"] * 0.3), d * (ln * 0.8), turn(ac, d, 80), PINE["dead_a"], 0.5, nr, lambda p: 0.95)
    return m

# ---- saplings: the tops of young trees poking out of the snow (decoration: nothing to hit)

def sapling_fir(m, rng, at, h):
    def nrm(p):
        r = V(p[0] - at[0], 0, p[2] - at[2])
        return (norm(r) if float(r @ r) > 1e-5 else V(1, 0, 0)) * 0.6 + UP * 0.65
    def shade(p): return 0.6 + 0.4 * clamp01(math.hypot(p[0] - at[0], p[2] - at[2]) / (0.3 * h))
    was = m.is_wood
    tube(m, [at - UP * 0.3, at + UP * (h * 0.8)], [0.035 * h / 2, 0.008], FIR_BARK, 3, 2.0, lambda p: 0.8)
    whorls = 5
    az = rng.random() * 6.28
    for i in range(whorls):
        f = i / (whorls - 1)
        y = h * lerp(0.06, 0.8, f)
        n = 5 if i < 3 else 4
        az += 0.7
        for j in range(n):
            a = az + j * 6.28 / n + rng.uniform(-0.25, 0.25)
            e = math.radians(lerp(-4, 28, f) + rng.uniform(-8, 8))
            d = horiz(a) * math.cos(e) + UP * math.sin(e)
            ln = h * lerp(0.36, 0.13, f) * rng.uniform(0.85, 1.15)
            k = ln / (STEM_END - STEM_START)
            side = norm(np.cross(UP, d))
            card(m, at + UP * y - d * (k * STEM_START), d * k, turn(side, d, rng.uniform(-25, 25)) * (k * 0.55), fir_cell(rng.randrange(2), 2 if rng.random() < 0.45 else 1), 0.5, nrm, shade)
    k = h * 0.3 / (STEM_END - STEM_START)
    for i in range(2):
        card(m, at + UP * (h * 0.72) - UP * (k * STEM_START), UP * k, (V(1, 0, 0) if i == 0 else V(0, 0, 1)) * (h * 0.13), fir_cell(i, 1), 0.5, nrm, shade)

def sapling_pine(m, rng, at, h):
    def nrm(p):
        r = V(p[0] - at[0], 0, p[2] - at[2])
        return (norm(r) if float(r @ r) > 1e-5 else V(1, 0, 0)) * 0.6 + UP * 0.65
    def shade(p): return 0.62 + 0.38 * clamp01((p[1] - at[1]) / h)
    tube(m, [at - UP * 0.3, at + UP * (h * 0.7)], [0.03 * h / 2, 0.01], PINE["bark_hi"], 3, 2.0, lambda p: 0.8)
    az = rng.random() * 6.28
    for i in range(3):
        f = i / 2
        y = h * lerp(0.12, 0.62, f)
        n = 5 if i == 0 else 4
        az += 0.9
        for j in range(n):
            a = az + j * 6.28 / n + rng.uniform(-0.3, 0.3)
            e = math.radians(lerp(22, 48, f) + rng.uniform(-8, 8))
            d = horiz(a) * math.cos(e) + UP * math.sin(e)
            ln = h * lerp(0.52, 0.36, f) * rng.uniform(0.85, 1.15)
            ac = norm(UP - d * float(d @ UP)) * (ln * 0.5)
            card(m, at + UP * y, d * ln, turn(ac, d, rng.uniform(-30, 30)), PINE[rng.choice(("twig_a", "twig_b", "twig_c"))], 0.5, nrm, shade)
    for i in range(3):
        d = norm(UP + horiz(i * 2.09 + az) * 0.25)
        ln = h * 0.42
        card(m, at + UP * (h * 0.6), d * ln, norm(np.cross(d, horiz(i * 2.09 + az + 1.57))) * (ln * 0.5), PINE["twig_c" if i else "twig_a"], 0.5, nrm, shade)

def sapling_mesh(spec, rng):
    """One or a few saplings: spec is a list of (kind, x, z, height)."""
    m = Mesh()
    for (kind, x, z, h) in spec:
        (sapling_fir if kind == "fir" else sapling_pine)(m, rng, V(x, 0, z), h)
    return m

# ---- every kind of tree

SNAG, YOUNG, SAPLING, PINEKIND, GIANT = 1, 2, 4, 8, 16   # flags (RealFir.cs reads them)

def measure(m, H):
    """A crown's radius (share of the height) at 17 heights, from what the plain mesh's foliage reaches."""
    out = [0.0] * 17
    for p, wood in zip(m.v, m.wood):
        if wood: continue
        i = int(round(p[1] / H * 16))
        if 0 <= i <= 16: out[i] = max(out[i], math.hypot(p[0], p[2]) / H)
    return [x * 0.85 for x in out]   # the cards' corners are empty

def build_kinds():
    """name, height, atlas (0 firs, 1 pines), flags, size (of a full-grown tree of its stand), trunk and crown tables, rich mesh, plain mesh"""
    kinds = []
    for i, base in enumerate(LEGACY):
        sp = {**FIR_DEFAULTS, **base}
        tree = grow_fir(sp, random.Random(1000 + i * 77))
        trunk, crown = fir_tables(sp)
        name = ("Fir", "Fir", "PlasteredFir", "PlasteredFir", "Hemlock", "Hemlock", "SnappedFir", "RaggedHemlock")[i] + str(i)
        kinds.append(dict(name=name, H=sp["H"], atlas=0, flags=0, size=1.0, trunk=trunk, crown=crown, rich=fir_mesh(sp, tree, True), plain=fir_mesh(sp, tree, False)))
    def pine(name, base, seed, flags, size):
        tree = grow_pine(base, random.Random(seed))
        plain = pine_mesh(base, tree, False)
        kinds.append(dict(name=name, H=base["H"], atlas=1, flags=flags | PINEKIND, size=size, trunk=[pine_trunk_radius(base, base["H"] * i / 16) for i in range(17)],
                          crown=measure(plain, base["H"]), rich=pine_mesh(base, tree, True), plain=plain))
    def fir(name, base, seed, flags, size):
        sp = {**FIR_DEFAULTS, **base}
        tree = grow_fir(sp, random.Random(seed))
        trunk, crown = fir_tables(sp)
        kinds.append(dict(name=name, H=sp["H"], atlas=0, flags=flags, size=size, trunk=trunk, crown=crown, rich=fir_mesh(sp, tree, True), plain=fir_mesh(sp, tree, False)))
    def snag(name, base, seed):
        tree = grow_snag(base, random.Random(seed))
        plain = snag_mesh(base, tree, False)
        kinds.append(dict(name=name, H=base["H"], atlas=1, flags=SNAG, size=base["H"] / 24, trunk=[snag_trunk_radius(base, base["H"] * i / 16) for i in range(17)],
                          crown=[0.0] * 17, rich=snag_mesh(base, tree, True), plain=plain))
    def sapling(name, spec, seed, atlas):
        m = sapling_mesh(spec, random.Random(seed))
        H = max(h for (_, _, _, h) in spec)
        kinds.append(dict(name=name, H=H, atlas=atlas, flags=SAPLING | (PINEKIND if atlas else 0), size=0.0, trunk=[0.03] * 17, crown=measure(m, H), rich=m, plain=m))
    pine("Pine8", PINE_A, 2101, 0, 1.0)
    pine("SnowyPine9", PINE_B, 2202, 0, 1.08)
    pine("GiantPine10", PINE_GIANT, 2303, GIANT, 1.38)
    pine("YoungPine11", PINE_YOUNG, 2404, YOUNG, 0.42)
    fir("GiantFir12", FIR_GIANT, 2505, GIANT, 1.45)
    fir("YoungFir13", FIR_YOUNG, 2606, YOUNG, 0.38)
    snag("Snag14", SNAG_TALL, 2707)
    snag("OldSnag15", SNAG_OLD, 2808)
    sapling("FirSapling16", [("fir", 0, 0, 2.4)], 2909, 0)
    sapling("FirSaplings17", [("fir", 0, 0, 2.1), ("fir", 1.3, 0.5, 1.4), ("fir", -0.6, 1.2, 1.0)], 3010, 0)
    sapling("PineSaplings18", [("pine", 0, 0, 2.0), ("pine", 1.1, -0.7, 1.2)], 3111, 1)
    return kinds

def write_trees(kinds, path):
    """The file RealFir.cs reads (little-endian): 'SMTR', version, count; then each kind: name, height, atlas,
    flags, size, 17 trunk radii, 17 crown radii, and its two meshes (rich, plain): vertex and index counts, then
    per vertex position (3 floats), normal (3 signed bytes) and shade (1 byte), uv (2 unsigned shorts), then
    the triangles (unsigned shorts)."""
    out = bytearray()
    out += b"SMTR" + struct.pack("<II", 1, len(kinds))
    for k in kinds:
        nm = k["name"].encode("ascii")
        out += struct.pack("<B", len(nm)) + nm
        out += struct.pack("<fBBf", k["H"], k["atlas"], k["flags"], k["size"])
        out += struct.pack("<17f", *k["trunk"]) + struct.pack("<17f", *k["crown"])
        for m in (k["rich"], k["plain"]):
            if len(m.v) > 65535: raise SystemExit(k["name"] + ": too many vertices")
            out += struct.pack("<II", len(m.v), len(m.t) * 3)
            for p, n, uv, ao in zip(m.v, m.n, m.uv, m.ao):
                out += struct.pack("<3f3bB2H", p[0], p[1], p[2], *[int(round(max(-1, min(1, c)) * 127)) for c in n], int(round(ao * 255)),
                                   int(round(clamp01(uv[0]) * 65535)), int(round(clamp01(uv[1]) * 65535)))
            for t in m.t: out += struct.pack("<3H", *t)
    with open(path, "wb") as f: f.write(out)
    return len(out)

def report(kinds):
    print("%-16s %6s %6s %7s %7s  %s" % ("kind", "height", "size", "rich", "plain", "widest crown (m)"))
    for i, k in enumerate(kinds):
        print("%2d %-13s %5.1fm %6.2f %7d %7d  %.1f" % (i, k["name"], k["H"], k["size"], len(k["rich"].t), len(k["plain"].t), max(k["crown"]) * k["H"]))

# ------------------------------------------------------------------------------------------------ pictures

def render_all(kinds, folder, pine_atlas, fir_atlas):
    """Pictures of the trees, rendered in Blender (Cycles) with a stand-in for the game's shader: the atlas cut out
    at half alpha, the mesh's own normals and shade, the firs' needles tinted as FirCards.mat tints them."""
    import bpy
    os.makedirs(folder, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 40
    scene.cycles.max_bounces = 3
    scene.cycles.transparent_max_bounces = 24
    try: scene.cycles.use_denoising = True
    except Exception: pass
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Sky"); scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.70, 0.95, 1); bg.inputs[1].default_value = 0.9
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2; sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(52), 0, math.radians(-38))   # from over the left shoulder
    scene.collection.objects.link(sun)
    # the firs' atlas with the material's needle tint already on it (FirCards.shader: Needles)
    a = np.asarray(Image.open(fir_atlas).convert("RGBA"), np.float32) / 255
    sat = a[..., :3].max(axis=2) - a[..., :3].min(axis=2)
    k = np.clip(sat * 6, 0, 1)[..., None]
    a[..., :3] = a[..., :3] * (1 - k) + a[..., :3] * np.array([0.62, 0.78, 0.86], np.float32) * k
    tinted = os.path.join(folder, "_fir_tinted.png")
    Image.fromarray((a * 255).astype(np.uint8), "RGBA").save(tinted)
    def material(name, path):
        mat = bpy.data.materials.new(name); mat.use_nodes = True
        nt = mat.node_tree; nt.nodes.clear()
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(path)
        ao = nt.nodes.new("ShaderNodeAttribute"); ao.attribute_name = "shade"
        mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = "MULTIPLY"; mul.inputs[0].default_value = 1
        nt.links.new(tex.outputs["Color"], mul.inputs[1]); nt.links.new(ao.outputs["Color"], mul.inputs[2])
        # a card lying flat, seen from underneath, is in shade (as in the game's shader)
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["True Normal"], sep.inputs[0])
        inc = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Incoming"], inc.inputs[0])
        flat = nt.nodes.new("ShaderNodeMath"); flat.operation = "ABSOLUTE"; nt.links.new(sep.outputs["Z"], flat.inputs[0])
        isflat = nt.nodes.new("ShaderNodeMath"); isflat.operation = "GREATER_THAN"; isflat.inputs[1].default_value = 0.5; nt.links.new(flat.outputs[0], isflat.inputs[0])
        below = nt.nodes.new("ShaderNodeMath"); below.operation = "LESS_THAN"; below.inputs[1].default_value = 0.0; nt.links.new(inc.outputs["Z"], below.inputs[0])
        both = nt.nodes.new("ShaderNodeMath"); both.operation = "MULTIPLY"; nt.links.new(isflat.outputs[0], both.inputs[0]); nt.links.new(below.outputs[0], both.inputs[1])
        # ... and the snow on its top does not show through: pale becomes needle green first
        lum = nt.nodes.new("ShaderNodeRGBToBW"); nt.links.new(tex.outputs["Color"], lum.inputs[0])
        pale = nt.nodes.new("ShaderNodeMath"); pale.operation = "MULTIPLY_ADD"; pale.inputs[1].default_value = 8.0; pale.inputs[2].default_value = -1.44; pale.use_clamp = True
        nt.links.new(lum.outputs[0], pale.inputs[0])
        snowy = nt.nodes.new("ShaderNodeMath"); snowy.operation = "MULTIPLY"; nt.links.new(pale.outputs[0], snowy.inputs[0]); nt.links.new(both.outputs[0], snowy.inputs[1])
        green = nt.nodes.new("ShaderNodeMixRGB"); green.blend_type = "MIX"; green.inputs[2].default_value = (0.04, 0.06, 0.04, 1)
        nt.links.new(snowy.outputs[0], green.inputs[0]); nt.links.new(tex.outputs["Color"], green.inputs[1]); nt.links.new(green.outputs[0], mul.inputs[1])
        dark = nt.nodes.new("ShaderNodeMixRGB"); dark.blend_type = "MULTIPLY"; dark.inputs[2].default_value = (0.28, 0.28, 0.28, 1)
        nt.links.new(both.outputs[0], dark.inputs[0]); nt.links.new(mul.outputs[0], dark.inputs[1])
        under = dark
        # the mesh's own normal whichever side is seen (the game's shader does not flip it for back faces)
        na = nt.nodes.new("ShaderNodeAttribute"); na.attribute_name = "nrm"
        nw = nt.nodes.new("ShaderNodeVectorMath"); nw.operation = "MULTIPLY_ADD"; nw.inputs[1].default_value = (2, 2, 2); nw.inputs[2].default_value = (-1, -1, -1)
        nt.links.new(na.outputs["Color"], nw.inputs[0])
        diff = nt.nodes.new("ShaderNodeBsdfDiffuse"); nt.links.new(under.outputs[0], diff.inputs["Color"]); nt.links.new(nw.outputs[0], diff.inputs["Normal"])
        # ... nor does it care which side of a card the sun is on: Cycles lights the far side through a translucent twin
        neg = nt.nodes.new("ShaderNodeVectorMath"); neg.operation = "SCALE"; neg.inputs["Scale"].default_value = -1; nt.links.new(nw.outputs[0], neg.inputs[0])
        thru = nt.nodes.new("ShaderNodeBsdfTranslucent"); nt.links.new(under.outputs[0], thru.inputs["Color"]); nt.links.new(neg.outputs[0], thru.inputs["Normal"])
        both_sides = nt.nodes.new("ShaderNodeAddShader"); nt.links.new(diff.outputs[0], both_sides.inputs[0]); nt.links.new(thru.outputs[0], both_sides.inputs[1])
        diff = both_sides
        clear = nt.nodes.new("ShaderNodeBsdfTransparent")
        cut = nt.nodes.new("ShaderNodeMath"); cut.operation = "GREATER_THAN"; cut.inputs[1].default_value = 0.5; nt.links.new(tex.outputs["Alpha"], cut.inputs[0])
        mix = nt.nodes.new("ShaderNodeMixShader"); nt.links.new(cut.outputs[0], mix.inputs[0]); nt.links.new(clear.outputs[0], mix.inputs[1]); nt.links.new(diff.outputs[0], mix.inputs[2])
        out = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(mix.outputs[0], out.inputs["Surface"])
        return mat
    mats = [material("Fir", tinted), material("Pine", pine_atlas)]
    snow = bpy.data.materials.new("Snow"); snow.use_nodes = True
    snow.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.9, 0.92, 0.96, 1)
    snow.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    def clear():
        for o in list(scene.collection.objects):
            if o.type == "MESH": bpy.data.objects.remove(o)
    def place(i, rich, x, y, height=None, sink=0.0, yaw=0.0, lean=0.0):
        """A tree set down in the picture (its mesh turned and scaled here, so its normals are the world's)."""
        k = kinds[i]; m = k["rich"] if rich else k["plain"]
        s = (height / k["H"]) if height else 1.0
        cy, sy, cl, sl = math.cos(yaw), math.sin(yaw), math.cos(math.radians(lean)), math.sin(math.radians(lean))
        def tr(p):   # Unity's y up to Blender's z up, turned about the upright, then leant over
            px, py, pz = p[0] * cy - p[2] * sy, p[0] * sy + p[2] * cy, p[1]
            return (px, py * cl - pz * sl, py * sl + pz * cl)
        me = bpy.data.meshes.new(k["name"])
        me.from_pydata([tuple(c * s + o for c, o in zip(tr(p), (x, y, -0.25 * s - sink))) for p in m.v], [], [tuple(t) for t in m.t])
        uv = me.uv_layers.new(name="uv")
        for loop in me.loops: uv.data[loop.index].uv = m.uv[loop.vertex_index]
        col = me.color_attributes.new("shade", "FLOAT_COLOR", "POINT")
        for j, a in enumerate(m.ao): col.data[j].color = (a, a, a, 1)
        na = me.color_attributes.new("nrm", "FLOAT_COLOR", "POINT")   # (as a colour: 0.5 + n / 2)
        for j, n in enumerate(m.n):
            w = tr(n); na.data[j].color = (w[0] * 0.5 + 0.5, w[1] * 0.5 + 0.5, w[2] * 0.5 + 0.5, 1)
        me.materials.append(mats[k["atlas"]])
        me.update()
        scene.collection.objects.link(bpy.data.objects.new(k["name"], me))
    def ground():
        me = bpy.data.meshes.new("Ground"); me.from_pydata([(-900, -900, 0), (900, -900, 0), (900, 900, 0), (-900, 900, 0)], [], [(0, 1, 2, 3)])
        me.materials.append(snow)
        scene.collection.objects.link(bpy.data.objects.new("Ground", me))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
    def shoot(name, eye, look, fov, w, h):
        from mathutils import Vector
        cam.location = eye
        cam.rotation_euler = (Vector(look) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
        cam.data.angle = math.radians(fov); cam.data.clip_end = 3000
        scene.render.resolution_x, scene.render.resolution_y = w, h
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("rendered", scene.render.filepath)
    if "lineup" in SHOTS:
        for name, ids in (("lineup_firs", [0, 1, 2, 3, 4, 5, 6, 7, 12, 13]), ("lineup_new", [8, 9, 10, 11, 14, 15, 16, 17, 18])):
            clear(); ground()
            x = 0.0; xs = []
            for i in ids:
                wide = max(3.0, max(kinds[i]["crown"]) * kinds[i]["H"] / 0.85)
                x += wide; place(i, True, x, 0); xs.append(x); x += wide + 1.5
            mid = x / 2
            shoot(name, (mid, -x * 1.05, 14), (mid, 0, 15), 50, 2200, 1000)
    if "lods" in SHOTS:
        clear(); ground()
        for j, i in enumerate((0, 4, 8, 14)):
            place(i, True, j * 26, 0); place(i, False, j * 26 + 11, 0)
        shoot("lods_rich_left_plain_right", (44, -70, 12), (44, 0, 13.5), 52, 2200, 900)
    if "near" in SHOTS:
        for name, i in (("near_fir", 0), ("near_pine", 8), ("near_hemlock", 4)):
            for rich in (True, False):
                clear(); ground(); place(i, rich, 0, 0, yaw=0.6)
                shoot(name + ("_rich" if rich else "_plain"), (2.5, -11, 1.7), (0, 0, 6.5 if i != 8 else 9), 62, 900, 1200)
    if "stand" in SHOTS:
        clear(); ground()
        rng = random.Random(77)
        for n in range(150):
            x, y = rng.uniform(-70, 70), rng.uniform(8, 190)
            if abs(x) < 9: continue
            pines = x > 12 + 14 * math.sin(y * 0.03)            # a stand of pines on the right, firs on the left
            r = rng.random()
            if r < 0.07: i = rng.choice((14, 15))
            elif r < 0.2: i = 11 if pines else 13
            elif r < 0.26: i = 10 if pines else 12
            else: i = rng.choice((8, 9, 8) if pines else (0, 1, 4, 5, 7, 6))
            full = 22 * rng.uniform(0.85, 1.15) * (kinds[i]["size"] or 1)
            place(i, y < 60, x, y, height=full, sink=full * 0.12, yaw=rng.random() * 6.28, lean=rng.random() ** 3 * 8)
        for n in range(40):
            x, y = rng.uniform(-40, 40), rng.uniform(6, 80)
            if abs(x) < 7: continue
            place(rng.choice((16, 17, 18)), True, x, y, height=rng.uniform(1.4, 2.8), sink=rng.uniform(0, 0.5), yaw=rng.random() * 6.28)
        shoot("stand", (0, -6, 1.7), (0, 60, 9), 68, 1800, 1100)
    os.remove(tinted)

# ------------------------------------------------------------------------------------------------ a count

def census(kinds, folder=None, seed=4242):
    """What the forest draws: Forest.cs's placement worked through here for one mountain (its grid, its density,
    its stands; not the course's clearings, which only take trees away), counted round a rider at five heights
    on the mountain, before (every tree one of kinds 0-7, about 400 triangles) and after. With a folder, also a map."""
    M = 0xFFFFFFFF
    def hash01(x, y, salt):
        h = ((x + salt * 7919 + seed * 1013) * 374761393 + (y - salt * 104729 + seed * 7) * 668265263) & M
        h = ((h ^ (h >> 13)) * 1274126177) & M
        h ^= h >> 16
        return h / 4294967296.0
    ph = (seed % 628) / 100.0; ox = (seed * 37.31) % 211.0; oz = (seed * 19.77) % 199.0
    def noise(x, y):
        x += ox; y += oz
        x0, y0 = math.floor(x), math.floor(y)
        fx, fy = x - x0, y - y0
        sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10); sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
        a, b, c, d = hash01(x0, y0, 0), hash01(x0 + 1, y0, 0), hash01(x0, y0 + 1, 0), hash01(x0 + 1, y0 + 1, 0)
        return lerp(lerp(a, b, sx), lerp(c, d, sx), sy)
    def centre(z): return 26 * math.sin(z * 0.0047 + ph) + 11 * math.sin(z * 0.0113 + 1.3 + ph * 1.7) + 5 * math.sin(z * 0.0240 + 0.4 + ph * 2.3)
    def sstep(a, b, x):
        t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t)
    CELL, PISTE, FINISH = 6.5, 20.0, 3048 / 0.72
    def pick(low, r):
        plastered = lerp(1, 0.05, clamp01(low * 1.6)); fir = lerp(0.3, 0.9, clamp01(low * 1.5)); hem = lerp(0.15, 1, low); snag = lerp(0.02, 0.12, low)
        w = [fir, fir, plastered, plastered, hem, hem, snag, hem * 0.5]
        r *= sum(w)
        for i, x in enumerate(w):
            r -= x
            if r <= 0: return i
        return 0
    def first(want, without, r):
        ids = [i for i in range(8, len(kinds)) if (kinds[i]["flags"] & want) == want and not (kinds[i]["flags"] & without)]
        return ids[min(len(ids) - 1, int(clamp01(r) * len(ids)))] if ids else -1
    def stand(cx, cz, x, z):
        glade = noise(x * 0.016 + 9, z * 0.016)
        rocks = sum(1 for dz in (-1, 0, 1) for dx in (-1, 0, 1) if (dx or dz) and hash01(cx + dx, cz + dz, 8) < 0.12 and hash01(cx + dx, cz + dz, 1) < 0.45)
        p = noise(x * 0.0075 + 53, z * 0.0055 + 17) + 0.35 * (0.5 - glade) + 0.07 * rocks
        pines = lerp(0.03, 0.92, sstep(0.68, 0.80, p))
        edge = clamp01(1 - abs(glade - 0.42) / 0.14)
        age = noise(x * 0.012 + 91, z * 0.010 + 37)
        young = lerp(0.5, 0.07, sstep(0.22, 0.45, age)) + 0.22 * edge
        old = 0.02 + 0.3 * sstep(0.62, 0.82, age)
        dead = 0.4 if noise(x * 0.03 + 141, z * 0.03 + 5) > 0.8 else 0.035 + 0.04 * pines
        return pines, young, old, dead, edge
    def cell(cx, cz):
        """(kind index or -1, is a rock, the kind it was before) for a cell; None if it is empty."""
        wx, wz = (cx + 0.5) * CELL, (cz + 0.5) * CELL
        off = abs(wx - centre(wz))
        glade = noise(wx * 0.016 + 9, wz * 0.016)
        if off < PISTE - 3: density = 0.10 if glade > 0.78 else 0.012
        else: density = lerp(0.16, 0.62, clamp01((off - PISTE) / 30)) * lerp(0.35, 1.3, glade)
        if wz < 70 and off < 34: density *= 0.1
        low = clamp01(wz / FINISH)
        if hash01(cx, cz, 1) >= density:
            if off < PISTE + 8 or wz < 70: return None
            r = hash01(cx, cz, 13)
            if r >= 0.4: return None
            pines, young, old, dead, edge = stand(cx, cz, wx, wz)
            if r >= 0.04 + 0.26 * edge + 0.2 * max(0.0, young - 0.25): return None
            return (first(SAPLING | (PINEKIND if hash01(cx, cz, 15) < pines else 0), 0 if hash01(cx, cz, 15) < pines else PINEKIND, hash01(cx, cz, 9)), False, -1)
        if hash01(cx, cz, 8) < (0.25 if off < PISTE else 0.12): return (-1, True, -1)
        was = pick(low, hash01(cx, cz, 9))
        if off <= PISTE + 6: return (was, False, was)
        pines, young, old, dead, edge = stand(cx, cz, wx, wz)
        r1, r2, r3, r4 = hash01(cx, cz, 9), hash01(cx, cz, 14), hash01(cx, cz, 15), hash01(cx, cz, 16)
        if r3 < dead: return (first(SNAG, 0, r1), False, was)
        pine = r2 < pines
        age = YOUNG if r4 < young else GIANT if r4 > 1 - old else 0
        if not pine and age == 0: return (was, False, was)
        if pine and age == 0:
            ids = [i for i in range(8, len(kinds)) if kinds[i]["flags"] == PINEKIND]
            return (ids[-1] if r1 < lerp(0.7, 0.25, low) else ids[0], False, was)
        return (first(age | (PINEKIND if pine else 0), SAPLING | (0 if pine else PINEKIND), r1), False, was)
    plain = [len(k["plain"].t) for k in kinds]; rich = [len(k["rich"].t) for k in kinds]
    share = {}
    for cz in range(0, int(FINISH / CELL), 3):
        mid = centre((cz + 0.5) * CELL)
        for cx in range(int((mid - 280) / CELL), int((mid + 280) / CELL), 2):
            c = cell(cx, cz)
            if c is None or c[1]: continue
            f = kinds[c[0]]["flags"]
            name = "saplings" if f & SAPLING else "snags" if (f & SNAG or c[0] == 6) else ("pines" if f & PINEKIND else "hemlocks" if c[0] in (4, 5, 7) else "firs") + (", young" if f & YOUNG else ", giant" if f & GIANT else "")
            share[name] = share.get(name, 0) + 1
    total = sum(v for k, v in share.items() if k != "saplings")
    print("\nthe whole mountain, out to 280 m either side: " + "; ".join("%s %.0f%%" % (k, 100 * v / total) for k, v in sorted(share.items(), key=lambda kv: -kv[1])) + " (of the trees)")
    print("\nround the rider: trees drawn as cards (to 149 m), of them rich as well (to 53 m), pictures (from 91 m), saplings;")
    print("triangles in the colour pass and the shadow pass; instanced draws (colour + shadow), one shadow cascade")
    for rz in (400, 1200, 2200, 3200, 3900):
        rx = centre(rz)
        cx0, cz0 = math.floor(rx / CELL), math.floor(rz / CELL)
        cards = richn = pics = saps = 0
        tb = ta = sa = 0
        kb, ka, kr, kp, kpb = set(), set(), set(), set(), set()
        for cz in range(cz0 - 3, cz0 + 89):
            for cx in range(cx0 - 44, cx0 + 45):
                c = cell(cx, cz)
                if c is None or c[1]: continue
                d2 = ((cx - cx0) * CELL) ** 2 + ((cz - cz0) * CELL) ** 2
                i, _, was = c
                if kinds[i]["flags"] & SAPLING:
                    if d2 <= 149 ** 2: saps += 1; ta += plain[i]; sa += plain[i]; ka.add(i)
                    continue
                if d2 > 91 ** 2: pics += 1; tb += 4; ta += 4; kp.add(i); kpb.add(was)
                if d2 > 149 ** 2: continue
                cards += 1; tb += plain[was]; kb.add(was)
                ta += plain[i]; sa += plain[i]; ka.add(i)
                if d2 < 53 ** 2: richn += 1; ta += rich[i]; kr.add(i)
        sb = tb - 4 * pics
        print("  z %4d m: cards %3d  rich %2d  pictures %4d  saplings %2d | before %6d + %6d shadow, draws %2d + %2d | after %6d + %6d shadow, draws %2d + %2d"
              % (rz, cards, richn, pics, saps, tb, sb, len(kb) + len(kpb), len(kb), ta, sa, len(ka) + len(kr) + len(kp), len(ka)))
    if folder:
        # a map: 600 m across, 1,200 m down the mountain, 2 px a metre... one dot a tree
        os.makedirs(folder, exist_ok=True)
        z0, z1, half, k = 1500, 2700, 300, 2
        im = Image.new("RGB", (half * 2 * k, (z1 - z0) * k), (236, 240, 246))
        d = ImageDraw.Draw(im)
        for cz in range(int(z0 / CELL), int(z1 / CELL)):
            mid = centre((cz + 0.5) * CELL)
            d.line([((mid - PISTE + half) * k, ((cz + 0.5) * CELL - z0) * k), ((mid + PISTE + half) * k, ((cz + 0.5) * CELL - z0) * k)], fill=(250, 252, 255), width=int(CELL * k) + 1)
            for cx in range(int((mid - half) / CELL), int((mid + half) / CELL)):
                c = cell(cx, cz)
                if c is None: continue
                x, y = ((cx + 0.5) * CELL - mid + half + mid) * k, ((cz + 0.5) * CELL - z0) * k
                x = ((cx + 0.5) * CELL + half) * k
                if c[1]: col, r = (120, 120, 125), 2
                else:
                    f = kinds[c[0]]["flags"]
                    col = (150, 150, 150) if (f & SNAG or c[0] == 6) else (205, 120, 40) if f & PINEKIND else (25, 110, 110) if c[0] in (4, 5, 7) else (20, 80, 45)
                    r = 2 if f & SAPLING else 3 if f & YOUNG else 7 if f & GIANT else 5
                    if f & SAPLING: col = tuple(min(255, v + 90) for v in col)
                d.ellipse((x - r, y - r, x + r, y + r), fill=col)
        im.save(os.path.join(folder, "map.png"))
        print("wrote", os.path.join(folder, "map.png"), "(green firs, teal hemlocks, orange pines, grey snags and rocks; big dots giants, small young, pale saplings)")

if __name__ == "__main__":
    if "atlas" in WHAT:
        img = build_atlas(fetch())
        out = os.path.join(RES, "PineCards.png") if RES else os.path.join(CACHE, "PineCards.png")
        img.save(out, optimize=True)
        print("wrote", out)
    KINDS = build_kinds() if ("trees" in WHAT or "census" in WHAT or RENDER) else []
    if "trees" in WHAT:
        out = os.path.join(RES, "Trees.bytes") if RES else os.path.join(CACHE, "Trees.bytes")
        size = write_trees(KINDS, out)
        print("wrote", out, size, "bytes")
        report(KINDS)
    if "census" in WHAT: census(KINDS, RENDER)
    if RENDER:
        if not PROJECT: raise SystemExit("--render needs --project (for the atlases)")
        render_all(KINDS, RENDER, os.path.join(RES, "PineCards.png"), os.path.join(RES, "FirCards.png"))
