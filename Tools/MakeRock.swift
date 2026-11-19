// Renders Summit's rock: one tileable granite albedo that every rock face shares (the terrain's cliffs and
// gorge walls, the ridge outcrops, the spires), mapped onto them in world space by Resources/RockLit.shader.
//   - patches of grey-brown and blue-grey, at two scales (the shader samples it near and far, so it never tiles)
//   - frost fractures: thin dark cracks round irregular blocks
//   - lichen and meltwater streaks running down the face (texture v is up the face)
//   - pale quartz veins wandering across it
//   - a fine grain
// Run: swift Tools/MakeRock.swift <outdir>   (writes RockAlbedo.png; keep it as Tools/art/rock-procedural.png: the
// game's RockAlbedo is now a scanned cliff regraded to this granite's colours by Tools/MakeRockScan.py)
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let N = 512

// ---- tileable noise: a lattice that wraps every `period` cells
func hash(_ x: Int, _ y: Int, _ s: Int) -> Double {
    var h = UInt32(truncatingIfNeeded: x &* 374761393 &+ y &* 668265263 &+ s &* 2147483647)
    h = (h ^ (h >> 13)) &* 1274126177
    h ^= h >> 16
    return Double(h) / 4294967296.0
}
func smooth(_ t: Double) -> Double { return t * t * t * (t * (t * 6 - 15) + 10) }
func lattice(_ u: Double, _ v: Double, _ px: Int, _ py: Int, _ s: Int) -> Double {
    let x = u * Double(px), y = v * Double(py)
    let x0 = Int(floor(x)), y0 = Int(floor(y))
    let fx = smooth(x - Double(x0)), fy = smooth(y - Double(y0))
    func h(_ i: Int, _ j: Int) -> Double { return hash(((i % px) + px) % px, ((j % py) + py) % py, s) }
    let a = h(x0, y0), b = h(x0 + 1, y0), c = h(x0, y0 + 1), d = h(x0 + 1, y0 + 1)
    return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy
}
func fbm(_ u: Double, _ v: Double, _ px: Int, _ py: Int, _ s: Int, _ oct: Int = 4) -> Double {
    var sum = 0.0, amp = 0.5, norm = 0.0, fx = px, fy = py
    for o in 0..<oct { sum += amp * lattice(u, v, fx, fy, s * 31 + o); norm += amp; amp *= 0.5; fx *= 2; fy *= 2 }
    return sum / norm
}
// distance to the nearest edge between irregular blocks (a wrapped Worley pattern): the frost fractures
func cracks(_ u: Double, _ v: Double, _ cells: Int, _ s: Int) -> Double {
    let x = u * Double(cells), y = v * Double(cells)
    let cx = Int(floor(x)), cy = Int(floor(y))
    var d1 = 9.0, d2 = 9.0
    for j in -1...1 { for i in -1...1 {
        let gx = cx + i, gy = cy + j
        let wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells
        let px = Double(gx) + 0.15 + 0.7 * hash(wx, wy, s), py = Double(gy) + 0.15 + 0.7 * hash(wx, wy, s + 1)
        let d = (px - x) * (px - x) + (py - y) * (py - y) * 1.6   // blocks a little wider than tall: bedded rock
        if d < d1 { d2 = d1; d1 = d } else if d < d2 { d2 = d }
    } }
    return sqrt(d2) - sqrt(d1)
}

struct C { var r, g, b: Double }
func mix(_ a: C, _ b: C, _ t: Double) -> C { let k = max(0, min(1, t)); return C(r: a.r + (b.r - a.r) * k, g: a.g + (b.g - a.g) * k, b: a.b + (b.b - a.b) * k) }

let brown = C(r: 0.36, g: 0.32, b: 0.28), blue = C(r: 0.30, g: 0.33, b: 0.37), pale = C(r: 0.50, g: 0.49, b: 0.47)
let dark = C(r: 0.15, g: 0.15, b: 0.16), lichen = C(r: 0.17, g: 0.18, b: 0.15), quartz = C(r: 0.78, g: 0.77, b: 0.73)

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: N, pixelsHigh: N, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                           isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: N * 4, bitsPerPixel: 32)!
let px = rep.bitmapData!
for y in 0..<N {
    for x in 0..<N {
        let u = Double(x) / Double(N), v = Double(N - 1 - y) / Double(N)   // v up the image = up the face
        // the rock's own colour: broad patches of brown and blue-grey, lighter where it is fresh
        let hue = fbm(u, v, 3, 3, 1)
        let tone = fbm(u, v, 6, 6, 2)
        var c = mix(brown, blue, smoothstep(0.38, 0.62, hue))
        c = mix(c, pale, smoothstep(0.55, 0.8, tone) * 0.55)
        c = mix(c, dark, smoothstep(0.45, 0.2, tone) * 0.5)
        // frost fractures round the blocks: the pattern warped so no block is a tidy polygon, the cracks hairline,
        // and only some of their length open (the rest is a faint seam)
        let wu = u + 0.035 * (fbm(u, v, 5, 5, 12, 3) - 0.5), wv = v + 0.035 * (fbm(u, v, 5, 5, 13, 3) - 0.5)
        let k = cracks(wu, wv, 6, 3)
        let open = smoothstep(0.42, 0.62, fbm(u, v, 4, 4, 8, 3))
        let crack = 1 - smoothstep(0.0, 0.025 + 0.02 * open, k)
        c = mix(c, dark, crack * (0.25 + 0.6 * open))
        // the granite's own crystals: small dark and light flecks
        let speck = lattice(u, v, 170, 170, 11)
        if speck > 0.82 { c = mix(c, pale, (speck - 0.82) * 3.5) }
        if speck < 0.15 { c = mix(c, dark, (0.15 - speck) * 4.0) }
        // streaks down the face: lichen and meltwater, long in v, narrow in u
        let streak = fbm(u, v, 18, 2, 5, 3)
        c = mix(c, lichen, smoothstep(0.58, 0.72, streak) * 0.6 * smoothstep(0.3, 0.7, fbm(u, v, 4, 4, 6, 2)))
        // quartz veins: thin pale lines along the contours of a slow field, broken where the rock has weathered over them
        let vein = abs(fbm(u, v, 3, 3, 7, 4) - 0.5)
        let showing = smoothstep(0.5, 0.65, fbm(u, v, 6, 6, 14, 2))
        c = mix(c, quartz, (1 - smoothstep(0.0015, 0.005, vein)) * 0.7 * showing)
        // grain
        let g = (hash(x, y, 9) - 0.5) * 0.06
        let i = (y * N + x) * 4
        px[i] = UInt8(max(0, min(255, (c.r + g) * 255)))
        px[i + 1] = UInt8(max(0, min(255, (c.g + g) * 255)))
        px[i + 2] = UInt8(max(0, min(255, (c.b + g) * 255)))
        px[i + 3] = 255
    }
}
func smoothstep(_ a: Double, _ b: Double, _ x: Double) -> Double { let t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t) }
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/RockAlbedo.png"))
print("rock written to \(out)")
