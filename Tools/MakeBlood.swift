// Draws the blood marks left by a hard crash: four splats in one 2x2 sheet, and a smear for where the body slid.
// Run: swift Tools/MakeBlood.swift Assets/Summit/Resources/Blood
//
// Stylised, game-like: a flat dark red with a slightly darker middle, a lobed blob with a few round droplets
// flicked out from it, no gore. Written as raw pixels with straight (non-premultiplied) alpha, so the soft,
// see-through edges keep their red instead of turning white.
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

// a small deterministic random generator, so the art is the same every run
var seed: UInt64 = 0x5EED_B100D
func rnd() -> Double { seed = seed &* 6364136223846793005 &+ 1442695040888963407; return Double(seed >> 11) / Double(1 << 53) }
func rnd(_ a: Double, _ b: Double) -> Double { a + (b - a) * rnd() }

func smooth(_ e0: Double, _ e1: Double, _ x: Double) -> Double { let t = max(0, min(1, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t) }

// the reds: fresh at the rim, darker where it pooled
let rim = (0.62, 0.05, 0.06), pool = (0.36, 0.02, 0.03)

struct Image {
    let w: Int, h: Int
    var px: [Double]   // r, g, b, a
    init(_ w: Int, _ h: Int) { self.w = w; self.h = h; px = Array(repeating: 0, count: w * h * 4) }
    /// Lay down coverage a (0..1) with "depth" d (0 rim .. 1 pooled) at a pixel, keeping the strongest.
    mutating func put(_ x: Int, _ y: Int, _ a: Double, _ d: Double) {
        guard x >= 0, y >= 0, x < w, y < h, a > 0 else { return }
        let i = (y * w + x) * 4
        let na = max(px[i + 3], a)
        let dd = max(0, min(1, d))
        if a >= px[i + 3] - 1e-6 || dd > 0 {
            let r = rim.0 + (pool.0 - rim.0) * dd, g = rim.1 + (pool.1 - rim.1) * dd, b = rim.2 + (pool.2 - rim.2) * dd
            // darkest wins where marks overlap
            if px[i + 3] == 0 || r < px[i] { px[i] = r; px[i + 1] = g; px[i + 2] = b }
        }
        px[i + 3] = na
    }
    func save(_ name: String) {
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                                   isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: w * 4, bitsPerPixel: 32)!
        let data = rep.bitmapData!
        for y in 0..<h {
            for x in 0..<w {
                let s = ((h - 1 - y) * w + x) * 4, d = (y * w + x) * 4   // rows top-down in the file
                for k in 0..<4 { data[d + k] = UInt8(max(0, min(255, (px[s + k] * 255).rounded()))) }
            }
        }
        try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
    }
}

/// A filled disc, soft at the edge by about a pixel.
func disc(_ img: inout Image, _ cx: Double, _ cy: Double, _ r: Double, depth: Double) {
    let x0 = Int(cx - r - 2), x1 = Int(cx + r + 2), y0 = Int(cy - r - 2), y1 = Int(cy + r + 2)
    for y in y0...y1 { for x in x0...x1 {
        let d = ((Double(x) + 0.5 - cx) * (Double(x) + 0.5 - cx) + (Double(y) + 0.5 - cy) * (Double(y) + 0.5 - cy)).squareRoot()
        img.put(x, y, 0.92 * (1 - smooth(r - 1.2, r + 0.6, d)), depth * (1 - d / max(r, 1)))
    } }
}

/// One splat in a square cell: a lobed blob, a ring of droplets and a few streaks thrown out from it.
func splat(_ img: inout Image, ox: Int, oy: Int, size: Int) {
    let c = Double(size) / 2, R = Double(size) * rnd(0.20, 0.25)
    // the blob's edge wobbles with a few lobes
    var lobes: [(Double, Double, Double)] = []
    for k in 1...5 { lobes.append((Double(k + 1), rnd(0.05, 0.16) / Double(k).squareRoot(), rnd(0, 6.283))) }
    for y in 0..<size { for x in 0..<size {
        let dx = Double(x) + 0.5 - c, dy = Double(y) + 0.5 - c
        let d = (dx * dx + dy * dy).squareRoot(), ang = atan2(dy, dx)
        var edge = R
        for l in lobes { edge += R * l.1 * sin(ang * l.0 + l.2) }
        let a = 0.92 * (1 - smooth(edge - 1.5, edge + 0.8, d))
        img.put(ox + x, oy + y, a, 0.85 * (1 - d / edge) + 0.15)
    } }
    // droplets flung out, smaller the further they flew; some trailing a streak back toward the middle
    let n = Int(rnd(10, 16))
    for _ in 0..<n {
        let ang = rnd(0, 6.283), far = rnd(1.15, 1.95)
        let r = max(1.5, Double(size) * rnd(0.012, 0.04) / far)
        let px = c + cos(ang) * R * far, py = c + sin(ang) * R * far
        disc(&img, Double(ox) + px, Double(oy) + py, r, depth: 0.3)
        if rnd() < 0.45 {
            // a tapering streak from the blob's edge out to the droplet
            let steps = 80
            for s in 0..<steps {
                let t = Double(s) / Double(steps)
                let sx = c + cos(ang) * R * (0.9 + (far - 0.9) * t), sy = c + sin(ang) * R * (0.9 + (far - 0.9) * t)
                disc(&img, Double(ox) + sx, Double(oy) + sy, max(0.8, r * (0.35 + 0.5 * t)), depth: 0.2)
            }
        }
    }
}

// ---- the splat sheet: four different splats, 256 px each
var sheet = Image(512, 512)
for cy in 0..<2 { for cx in 0..<2 { splat(&sheet, ox: cx * 256, oy: cy * 256, size: 256) } }
sheet.save("Splats")

// ---- the smear: what the sliding body wipes along the snow. Long axis is up the texture (v), the body slid
// from the bottom to the top: heavy where it started, thinning and breaking up into streaks as it went
var smear = Image(128, 512)
var streaks: [(Double, Double)] = []   // (x across 0..1, strength)
for i in 0..<16 { streaks.append((0.1 + 0.8 * (Double(i) + rnd(0.2, 0.8)) / 16, rnd(0.35, 1.0))) }   // spread evenly across, jittered
for y in 0..<512 { for x in 0..<128 {
    let u = (Double(x) + 0.5) / 128, v = (Double(y) + 0.5) / 512
    // narrows as it goes, each edge ragged in its own way
    let left = 0.5 - (0.38 - 0.15 * v + 0.04 * sin(v * 17 + 1.3) + 0.025 * sin(v * 47 + 4.1))
    let right = 0.5 + (0.38 - 0.15 * v + 0.04 * sin(v * 13 + 2.7) + 0.025 * sin(v * 53 + 0.4))
    let mid = (left + right) / 2, half = (right - left) / 2
    let across = abs(u - mid) / half
    var a = 1 - smooth(0.82, 1.0, across)
    // dragged streaks along the length, stronger toward the far end where it runs dry
    var s = 0.0
    for st in streaks { s += st.1 * exp(-pow((u - st.0) / 0.022, 2)) }
    let dry = smooth(0.15, 0.85, v)
    a *= (1 - dry) * (0.75 + 0.25 * min(1, s)) + dry * min(1, s)
    a *= smooth(0.0, 0.06, v) * (1 - smooth(0.9, 1.0, v))           // soft ends
    smear.put(x, y, 0.88 * a, 0.7 * (1 - v) * (1 - across))
} }
// a few drops beside the smear
for _ in 0..<10 { disc(&smear, rnd(8, 120), rnd(20, 490), rnd(1.5, 3.5), depth: 0.3) }
smear.save("Smear")
print("wrote Splats.png and Smear.png to \(out)")
