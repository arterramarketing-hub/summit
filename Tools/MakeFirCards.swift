// Builds Summit's realistic fir textures from Poly Haven's "Fir Tree 01" photo-scanned twigs (CC0):
// cuts the twig sprays out of the twig atlas, stamps dozens of them along a stem to make whole branch
// fronds, piles snow on them, and packs everything with a strip of the bark into one 1024x1024 RGBA atlas,
// so a whole tree draws with one material.
//
//   row 0  two side-on branches with a snow cap      (the upright cards: what you see from beside a tree)
//   row 1  two flat branches with a light dusting    (the flat cards seen from above, high on the tree)
//   row 2  two flat branches loaded with snow        (the flat cards lower down)
//   row 3  the bark, rotated so the trunk runs along the strip; its last 64 pixels are a flat shadowed
//          green, for the dense core inside the crown
//
// Run: swiftc -O Tools/MakeFirCards.swift -o /tmp/fircards &&
//      /tmp/fircards fir_tree_01_twig_diff_2k.png fir_tree_01_twig_alpha_2k.png fir_tree_01_bark_diff_1k.png \
//                    Assets/Summit/Resources/RealTrees/FirCards.png
// (the three sources are from https://polyhaven.com/a/fir_tree_01 - see CREDITS.txt)
import Foundation
import AppKit

let args = CommandLine.arguments
guard args.count >= 5 else { print("usage: fircards twig_diff.png twig_alpha.png bark_diff.png out.png"); exit(1) }

// ---------- pixels in and out

struct Img {
    var w: Int, h: Int
    var c: [Float]   // premultiplied RGBA, row 0 at the top
    init(_ w: Int, _ h: Int) { self.w = w; self.h = h; c = [Float](repeating: 0, count: w * h * 4) }
}

func load(_ path: String) -> Img {
    let rep = NSBitmapImageRep(data: try! Data(contentsOf: URL(fileURLWithPath: path)))!
    let cg = rep.cgImage!
    let w = cg.width, h = cg.height
    var bytes = [UInt8](repeating: 0, count: w * h * 4)
    bytes.withUnsafeMutableBytes { buf in
        let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h))
    }
    var img = Img(w, h)
    for i in 0..<(w * h * 4) { img.c[i] = Float(bytes[i]) / 255 }
    return img
}

func save(_ img: Img, _ path: String) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: img.w, pixelsHigh: img.h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: img.w * 4, bitsPerPixel: 32)!
    let p = rep.bitmapData!
    for i in 0..<(img.w * img.h) {
        let a = img.c[i * 4 + 3]
        for k in 0..<3 {
            let v = a > 0.001 ? img.c[i * 4 + k] / a : img.c[i * 4 + k]   // straight alpha in the file
            p[i * 4 + k] = UInt8(max(0, min(255, v * 255 + 0.5)))
        }
        p[i * 4 + 3] = UInt8(max(0, min(255, a * 255 + 0.5)))
    }
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
}

// a tiny deterministic random source, so the atlas is the same every run
var seed: UInt64 = 0x5EED_F1A5
func rnd() -> Float { seed = seed &* 6364136223846793005 &+ 1442695040888963407; return Float((seed >> 33) & 0xFFFFFF) / Float(0xFFFFFF) }
func rr(_ a: Float, _ b: Float) -> Float { a + (b - a) * rnd() }
func ss(_ a: Float, _ b: Float, _ x: Float) -> Float { let t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t) }

// ---------- the twig sprays, cut out of the scanned atlas

let diff = load(args[1]), alpha = load(args[2]), bark = load(args[3])
let SW = diff.w, SH = diff.h
let k = Float(SW) / 2048

struct Sprig { var img: Img; var bx: Float; var by: Float; var len: Float }

// rough boxes round each spray in the 2048 atlas (x0, y0, x1, y1); the largest connected piece inside is kept
let boxes: [(Int, Int, Int, Int)] = [(387, 93, 880, 640), (1333, 80, 1925, 760), (619, 813, 1317, 1595), (1296, 912, 1968, 1712), (1003, 528, 1296, 821)]

func cut(_ b: (Int, Int, Int, Int)) -> Sprig {
    let x0 = Int(Float(b.0) * k), y0 = Int(Float(b.1) * k), x1 = Int(Float(b.2) * k), y1 = Int(Float(b.3) * k)
    let w = x1 - x0, h = y1 - y0
    func av(_ x: Int, _ y: Int) -> Float { alpha.c[((y0 + y) * SW + x0 + x) * 4] }
    // flood fill every piece above a low threshold, keep the biggest
    var label = [Int32](repeating: -1, count: w * h)
    var best = -1, bestN = 0, next: Int32 = 0
    for sy in 0..<h { for sx in 0..<w where label[sy * w + sx] < 0 && av(sx, sy) > 0.1 {
        var stack = [(sx, sy)]; label[sy * w + sx] = next; var n = 0
        while let (x, y) = stack.popLast() {
            n += 1
            for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                let nx = x + dx, ny = y + dy
                if nx >= 0 && ny >= 0 && nx < w && ny < h && label[ny * w + nx] < 0 && av(nx, ny) > 0.1 { label[ny * w + nx] = next; stack.append((nx, ny)) }
            }
        }
        if n > bestN { bestN = n; best = Int(next) }
        next += 1
    } }
    var img = Img(w, h)
    var minY = h, maxY = 0
    for y in 0..<h { for x in 0..<w where label[y * w + x] == Int32(best) {
        let a = av(x, y), s = ((y0 + y) * SW + x0 + x) * 4
        // straight colour from the diffuse map, alpha from the alpha map
        img.c[(y * w + x) * 4] = diff.c[s] * a; img.c[(y * w + x) * 4 + 1] = diff.c[s + 1] * a; img.c[(y * w + x) * 4 + 2] = diff.c[s + 2] * a
        img.c[(y * w + x) * 4 + 3] = a
        minY = min(minY, y); maxY = max(maxY, y)
    } }
    // the stem's foot: the middle of the lowest few rows
    var sx: Float = 0, n: Float = 0
    for y in max(0, maxY - 6)...maxY { for x in 0..<w where label[y * w + x] == Int32(best) { sx += Float(x); n += 1 } }
    return Sprig(img: img, bx: sx / max(1, n), by: Float(maxY), len: Float(maxY - minY))
}
let sprigs = boxes.map(cut)
print("sprigs: " + sprigs.map { "\($0.img.w)x\($0.img.h) len \(Int($0.len))" }.joined(separator: ", "))

func sample(_ im: Img, _ x: Float, _ y: Float) -> (Float, Float, Float, Float) {
    let fx = x - 0.5, fy = y - 0.5
    let ix = Int(floor(fx)), iy = Int(floor(fy)), tx = fx - Float(ix), ty = fy - Float(iy)
    func p(_ a: Int, _ b: Int) -> (Float, Float, Float, Float) {
        if a < 0 || b < 0 || a >= im.w || b >= im.h { return (0, 0, 0, 0) }
        let o = (b * im.w + a) * 4; return (im.c[o], im.c[o + 1], im.c[o + 2], im.c[o + 3])
    }
    let a = p(ix, iy), b = p(ix + 1, iy), c = p(ix, iy + 1), d = p(ix + 1, iy + 1)
    func l(_ u: Float, _ v: Float, _ t: Float) -> Float { u + (v - u) * t }
    return (l(l(a.0, b.0, tx), l(c.0, d.0, tx), ty), l(l(a.1, b.1, tx), l(c.1, d.1, tx), ty),
            l(l(a.2, b.2, tx), l(c.2, d.2, tx), ty), l(l(a.3, b.3, tx), l(c.3, d.3, tx), ty))
}

// the scanned twigs are a bright spring green; the firs in the backdrop photo are a deep blue-green
func grade(_ r: Float, _ g: Float, _ b: Float, _ shade: Float) -> (Float, Float, Float) {
    let lum = 0.3 * r + 0.55 * g + 0.15 * b
    let sat: Float = 0.62
    var (r2, g2, b2) = (lum + (r - lum) * sat, lum + (g - lum) * sat, lum + (b - lum) * sat)
    r2 *= 0.58; g2 *= 0.80; b2 *= 0.74
    return (r2 * shade, g2 * shade, b2 * shade)
}

/// Lays one spray into `dst` with its foot at (x, y), pointing along `ang` (radians, image y down), `len` pixels long.
func stamp(_ dst: inout Img, _ sp: Sprig, _ x: Float, _ y: Float, _ ang: Float, _ len: Float, _ shade: Float, _ flip: Bool) {
    let s = len / sp.len
    let ca = cos(ang), sa = sin(ang)
    let rad = Float(max(sp.img.w, sp.img.h)) * s * 1.1
    let xa = max(0, Int(x - rad)), xb = min(dst.w - 1, Int(x + rad)), ya = max(0, Int(y - rad)), yb = min(dst.h - 1, Int(y + rad))
    if xa > xb || ya > yb { return }
    for py in ya...yb { for px in xa...xb {
        let rx = Float(px) + 0.5 - x, ry = Float(py) + 0.5 - y
        let along = (rx * ca + ry * sa) / s, side = (-rx * sa + ry * ca) / s
        let c = sample(sp.img, sp.bx + (flip ? side : -side), sp.by - along)
        if c.3 < 0.002 { continue }
        let g = grade(c.0, c.1, c.2, shade)
        let o = (py * dst.w + px) * 4, ia = 1 - c.3
        dst.c[o] = g.0 + dst.c[o] * ia; dst.c[o + 1] = g.1 + dst.c[o + 1] * ia; dst.c[o + 2] = g.2 + dst.c[o + 2] * ia; dst.c[o + 3] = c.3 + dst.c[o + 3] * ia
    } }
}

// ---------- a whole frond: a stem with sprays down both sides, seen from above

let CW = 512, CH = 256, M: Float = 8   // cell size and the clear margin round it

/// Half the frond's width at t (0 at the trunk, 1 at the tip): broad near the base, tapering to the tip.
func halfWidth(_ t: Float) -> Float { Float(CH) * 0.5 - M - 4 - (1 - sin(Float.pi * (0.12 + 0.88 * t) * 0.62 + 0.25)) * 60 - t * t * 50 }

func frond() -> Img {
    var im = Img(CW, CH)
    let x0 = M + 4, x1 = Float(CW) - M - 6, cy = Float(CH) * 0.5
    let L = x1 - x0
    // the bare stem, under everything
    for px in Int(x0)..<Int(x1) {
        let t = (Float(px) - x0) / L, wdt = 2.6 - 1.8 * t
        for py in Int(cy - wdt - 1)...Int(cy + wdt + 1) {
            let a = ss(wdt + 0.8, wdt - 0.4, abs(Float(py) + 0.5 - cy))
            let o = (py * CW + px) * 4
            im.c[o] = 0.16 * a; im.c[o + 1] = 0.12 * a; im.c[o + 2] = 0.09 * a; im.c[o + 3] = a
        }
    }
    // sprays: back layer first (darker, out to the sides), then the top layer along the stem
    for layer in 0..<3 {
        let n = layer == 2 ? 9 : 13
        for i in 0..<n {
            let t = (Float(i) + rr(0.1, 0.9)) / Float(n) * 0.9
            let hw = halfWidth(t)
            for side in [-1, 1] as [Float] {
                let sp = sprigs[Int(rnd() * Float(layer == 2 ? 4 : 5)) % sprigs.count]
                let spread: Float = layer == 0 ? rr(0.95, 1.2) : layer == 1 ? rr(0.6, 0.85) : rr(0.25, 0.45)
                let ang = side * spread
                let len = min(hw / max(0.3, sin(abs(ang))) * rr(0.8, 0.98), L * 0.34) * (layer == 2 ? 0.8 : 1)
                let shade: Float = layer == 0 ? rr(0.48, 0.62) : layer == 1 ? rr(0.7, 0.85) : rr(0.88, 1.05)
                stamp(&im, sp, x0 + t * L, cy + side * rr(0, 3), ang, len, shade, rnd() < 0.5)
            }
        }
    }
    // the leader at the tip
    stamp(&im, sprigs[0], x0 + L * 0.72, cy, 0, L * 0.28, 1.0, false)
    return im
}

// ---------- snow

/// Smooth value noise, 0..1.
func hash2(_ x: Int, _ y: Int, _ s: Int) -> Float {
    var h = UInt32(truncatingIfNeeded: x &* 374761393 &+ y &* 668265263 &+ s &* 1442695040)
    h = (h ^ (h >> 13)) &* 1274126177
    return Float((h ^ (h >> 16)) & 0xFFFF) / 65535
}
func vnoise(_ x: Float, _ y: Float, _ s: Int) -> Float {
    let ix = Int(floor(x)), iy = Int(floor(y))
    let tx = x - Float(ix), ty = y - Float(iy)
    let ux = tx * tx * (3 - 2 * tx), uy = ty * ty * (3 - 2 * ty)
    let a = hash2(ix, iy, s), b = hash2(ix + 1, iy, s), c = hash2(ix, iy + 1, s), d = hash2(ix + 1, iy + 1, s)
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uy
}
func fbm(_ x: Float, _ y: Float) -> Float { 0.5 * vnoise(x / 22, y / 22, 1) + 0.3 * vnoise(x / 9, y / 9, 2) + 0.2 * vnoise(x / 3.5, y / 3.5, 3) }

/// A blurred copy of a cell's alpha: where there are needles for snow to lie on.
func coverage(_ im: Img, _ r: Int) -> [Float] {
    var a = [Float](repeating: 0, count: im.w * im.h), t = a
    for i in 0..<(im.w * im.h) { a[i] = im.c[i * 4 + 3] }
    for y in 0..<im.h { for x in 0..<im.w { var s: Float = 0; for d in -r...r { s += a[y * im.w + max(0, min(im.w - 1, x + d))] }; t[y * im.w + x] = s / Float(2 * r + 1) } }
    for y in 0..<im.h { for x in 0..<im.w { var s: Float = 0; for d in -r...r { s += t[max(0, min(im.h - 1, y + d)) * im.w + x] }; a[y * im.w + x] = s / Float(2 * r + 1) } }
    return a
}

struct Lump { var x: Float; var y: Float; var rx: Float; var ry: Float }

/// Snow on a cell: soft lumps (summed, so they melt into each other with no seams), only where there are
/// needles under them, broken up by noise so green shows through. Side-on, the snow is lit on top and
/// shaded underneath; from above it is lit all over, greyer where it is thin.
func snow(_ im: inout Img, _ lumps: [Lump], sideOn: Bool, amount: Float) {
    let cov = coverage(im, sideOn ? 3 : 5)
    func field(_ x: Float, _ y: Float) -> Float {
        var f: Float = 0
        for l in lumps { let dx = (x - l.x) / l.rx, dy = (y - l.y) / l.ry; f += exp(-(dx * dx + dy * dy) * 1.6) }
        return f
    }
    var out = im
    for py in 0..<im.h { for px in 0..<im.w {
        let x = Float(px) + 0.5, y = Float(py) + 0.5
        let f = field(x, y)
        if f < 0.05 { continue }
        // snow sits on the needles: side-on it may sit a little above them
        var c = cov[py * im.w + px]
        if sideOn { for d in 1...10 where py + d < im.h { c = max(c, cov[(py + d) * im.w + px] * (1 - Float(d) / 12)) } }
        let n = fbm(x, y)
        let v = f * min(1, c * 1.8) * amount + (n - 0.5) * 0.9
        let a = ss(0.32, 0.46, v)
        if a <= 0 { continue }
        var lit: Float
        if sideOn {
            let g = field(x, y + 4) - field(x, y - 4)          // > 0 on the top of a lump, < 0 underneath
            lit = max(0.66, min(1, 0.86 + g * 0.5))
        } else {
            lit = 0.80 + 0.2 * ss(0.4, 1.0, v)
        }
        lit *= 0.94 + 0.08 * vnoise(x / 2, y / 2, 4)          // grain
        let r = 0.90 * lit, g = 0.93 * lit, b = 0.97 * min(1, lit + 0.05)
        let o = (py * im.w + px) * 4, ia = 1 - a
        out.c[o] = r * a + im.c[o] * ia; out.c[o + 1] = g * a + im.c[o + 1] * ia; out.c[o + 2] = b * a + im.c[o + 2] * ia
        out.c[o + 3] = a + im.c[o + 3] * ia
    } }
    im = out
}

let x0 = M + 4, L = Float(CW) - M - 6 - x0, cy = Float(CH) * 0.5

func flatSnowy(heavy: Bool) -> Img {
    var im = frond()
    var lumps: [Lump] = []
    let n = heavy ? 30 : 40
    for _ in 0..<n {
        let t = rr(0.03, 0.9)
        let hw = halfWidth(t)
        if heavy {
            let r = hw * rr(0.22, 0.4)
            lumps.append(Lump(x: x0 + t * L, y: cy + rr(-0.45, 0.45) * hw, rx: r * rr(1.1, 1.6), ry: r))
        } else {
            let r = hw * rr(0.08, 0.16)
            lumps.append(Lump(x: x0 + t * L, y: cy + rr(-0.8, 0.8) * hw, rx: r * rr(1.2, 2.0), ry: r))
        }
    }
    snow(&im, lumps, sideOn: false, amount: heavy ? 0.66 : 1.0)
    return im
}

func sideCapped() -> Img {
    // the frond seen almost edge-on: squash it, shade its underside, sit a ridge of snow along its top
    let flat = frond()
    var im = Img(CW, CH)
    let stemY = Float(CH) * 0.56, squash: Float = 0.5
    for py in 0..<CH { for px in 0..<CW {
        let sy = cy + (Float(py) + 0.5 - stemY) / squash
        let c = sample(flat, Float(px) + 0.5, sy)
        if c.3 < 0.002 { continue }
        let under: Float = 1 - 0.4 * ss(-0.2, 1, (Float(py) - stemY) / (Float(CH) * 0.5 * squash))   // darker beneath
        let o = (py * CW + px) * 4
        im.c[o] = c.0 * under; im.c[o + 1] = c.1 * under; im.c[o + 2] = c.2 * under; im.c[o + 3] = c.3
    } }
    var lumps: [Lump] = []
    for _ in 0..<26 {
        let t = rr(0.04, 0.9)
        let hw = halfWidth(t) * squash
        let r = hw * rr(0.3, 0.5)
        lumps.append(Lump(x: x0 + t * L, y: stemY - hw * rr(0.3, 0.6), rx: r * rr(1.5, 2.6), ry: r))
    }
    snow(&im, lumps, sideOn: true, amount: 0.72)
    return im
}

// ---------- the atlas

var atlas = Img(1024, 1024)
func put(_ cell: Img, _ col: Int, _ row: Int) {
    for y in 0..<cell.h { for x in 0..<cell.w {
        let s = (y * cell.w + x) * 4, d = ((row * CH + y) * 1024 + col * CW + x) * 4
        for q in 0..<4 { atlas.c[d + q] = cell.c[s + q] }
    } }
}
put(sideCapped(), 0, 0); put(sideCapped(), 1, 0)
put(flatSnowy(heavy: false), 0, 1); put(flatSnowy(heavy: false), 1, 1)
put(flatSnowy(heavy: true), 0, 2); put(flatSnowy(heavy: true), 1, 2)
// bark: the trunk runs along the strip (x), round the trunk is across it (y)
for y in 0..<CH { for x in 0..<1024 {
    let c = sample(bark, Float(y) * Float(bark.w) / Float(CH) + 0.5, Float(x) * Float(bark.h) / 1024 + 0.5)
    let d = ((3 * CH + y) * 1024 + x) * 4
    atlas.c[d] = c.0 * 0.62; atlas.c[d + 1] = c.1 * 0.58; atlas.c[d + 2] = c.2 * 0.56; atlas.c[d + 3] = 1
    if x >= 960 {   // the core swatch: deep shadow among the needles, with a little mottling
        let m = 0.85 + 0.3 * vnoise(Float(x) / 6, Float(y) / 6, 9)
        atlas.c[d] = 0.075 * m; atlas.c[d + 1] = 0.13 * m; atlas.c[d + 2] = 0.11 * m
    }
} }

// bleed colour out into the clear pixels, so mipmaps and filtering don't pull in black fringes
// (alpha stays 0 there; this only fills the colour channels, kept as straight colour)
var straight = atlas
for i in 0..<(1024 * 1024) { let a = atlas.c[i * 4 + 3]; if a > 0.002 { for q in 0..<3 { straight.c[i * 4 + q] = atlas.c[i * 4 + q] / a } } }
var filled = [Bool](repeating: false, count: 1024 * 1024)
for i in 0..<(1024 * 1024) { filled[i] = atlas.c[i * 4 + 3] > 0.05 }
for _ in 0..<24 {
    var nf = filled; var changed = false
    for y in 0..<1024 { for x in 0..<1024 where !filled[y * 1024 + x] {
        var r: Float = 0, g: Float = 0, b: Float = 0, n: Float = 0
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            let nx = x + dx, ny = y + dy
            if nx < 0 || ny < 0 || nx >= 1024 || ny >= 1024 || !filled[ny * 1024 + nx] { continue }
            let o = (ny * 1024 + nx) * 4; r += straight.c[o]; g += straight.c[o + 1]; b += straight.c[o + 2]; n += 1
        }
        if n > 0 { let o = (y * 1024 + x) * 4; straight.c[o] = r / n; straight.c[o + 1] = g / n; straight.c[o + 2] = b / n; nf[y * 1024 + x] = true; changed = true }
    } }
    filled = nf
    if !changed { break }
}
// back to premultiplied for save() (which divides again); clear pixels keep their bled colour
for i in 0..<(1024 * 1024) {
    let a = atlas.c[i * 4 + 3]
    for q in 0..<3 { atlas.c[i * 4 + q] = a > 0.002 ? straight.c[i * 4 + q] * a : straight.c[i * 4 + q] }
}
save(atlas, args[4])
print("wrote \(args[4])")
