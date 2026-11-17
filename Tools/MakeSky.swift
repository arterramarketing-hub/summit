// Paints Summit's sky: a 4096x2048 latitude-longitude panorama for Unity's Skybox/Panoramic shader.
// Deep blue zenith, wispy high cloud in perspective, cloud banks hugging the peaks, and three ranges of
// mountains: hazy far peaks, snowy mid peaks with rock bands, dark forest and ski runs cut through it,
// and near forested ridges. Everything wraps seamlessly round the horizon.
// Run: swiftc -O Tools/MakeSky.swift -o /tmp/makesky && /tmp/makesky Assets/Summit/Materials/SkyPanorama.png
import Foundation
import AppKit

let W = 4096, H = 2048
let PI = Double.pi

struct C { var r, g, b: Double
    static func *(a: C, k: Double) -> C { C(r: a.r * k, g: a.g * k, b: a.b * k) }
    static func +(a: C, b: C) -> C { C(r: a.r + b.r, g: a.g + b.g, b: a.b + b.b) }
}
func mix(_ a: C, _ b: C, _ t: Double) -> C { let k = max(0, min(1, t)); return a * (1 - k) + b * k }
func ss(_ a: Double, _ b: Double, _ x: Double) -> Double { let t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t) }

// ---- noise
func hash(_ x: Int, _ y: Int, _ s: Int) -> Double {
    var h = UInt64(bitPattern: Int64(x &* 374761393 &+ y &* 668265263 &+ s &* 982451653))
    h = (h ^ (h >> 13)) &* 1274126177; h ^= h >> 16
    return Double(h & 0xffffff) / Double(0xffffff)
}
func noise(_ x: Double, _ y: Double, _ s: Int) -> Double {
    let x0 = Int(floor(x)), y0 = Int(floor(y))
    let fx = x - floor(x), fy = y - floor(y)
    let sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy)
    let a = hash(x0, y0, s), b = hash(x0 + 1, y0, s), c = hash(x0, y0 + 1, s), d = hash(x0 + 1, y0 + 1, s)
    return (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy
}
func fbm(_ x: Double, _ y: Double, _ s: Int, _ oct: Int = 6) -> Double {
    var v = 0.0, a = 0.5, f = 1.0, n = 0.0
    for o in 0..<oct { v += a * noise(x * f, y * f, s + o * 17); n += a; a *= 0.5; f *= 2.03 }
    return v / n
}
func ridged(_ x: Double, _ y: Double, _ s: Int, _ oct: Int = 7) -> Double {
    var v = 0.0, a = 0.5, f = 1.0, n = 0.0, prev = 1.0
    for o in 0..<oct {
        var r = 1 - abs(noise(x * f, y * f, s + o * 31) * 2 - 1); r = r * r * prev
        v += a * r; n += a; prev = r; a *= 0.5; f *= 2.1
    }
    return v / n
}
/// Noise round the horizon: sampled on a circle so az = 0 and 2pi meet.
func ring(_ az: Double, _ freq: Double, _ y: Double, _ s: Int, ridge: Bool, oct: Int = 7) -> Double {
    let x = cos(az) * freq + 100, z = sin(az) * freq + 100
    return ridge ? ridged(x + y * 0.37, z - y * 0.21, s, oct) : fbm(x + y * 0.37, z - y * 0.21, s, oct)
}

// ---- palette (matched to the game's light: a bright sun from behind-left, blue shadows)
let zenith = C(r: 0.09, g: 0.27, b: 0.66), horizon = C(r: 0.60, g: 0.74, b: 0.92)
let haze = C(r: 0.80, g: 0.86, b: 0.94)                     // the game's fog colour
let snowLit = C(r: 0.97, g: 0.98, b: 1.0), snowShade = C(r: 0.62, g: 0.70, b: 0.84)
let rock = C(r: 0.20, g: 0.21, b: 0.24), forest = C(r: 0.07, g: 0.12, b: 0.13), forestLit = C(r: 0.12, g: 0.18, b: 0.17)

struct Range { let base, amp, freq: Double; let seed: Int; let snowline, hazeAmt: Double; let forest: Bool; let runs: [Double] }
let ranges = [
    Range(base: 0.025, amp: 0.21, freq: 2.2, seed: 11, snowline: -1.0, hazeAmt: 0.55, forest: false, runs: []),
    Range(base: 0.0, amp: 0.27, freq: 3.0, seed: 23, snowline: 0.075, hazeAmt: 0.22, forest: true, runs: [0.35, 0.42, 1.9, 2.05, 2.12, 3.6, 4.4, 4.52, 5.6]),
    Range(base: -0.02, amp: 0.075, freq: 4.5, seed: 37, snowline: 0.045, hazeAmt: 0.08, forest: true, runs: [1.2, 3.1, 5.0]),
]

func ridgeTop(_ r: Range, _ az: Double) -> Double {
    // broad massifs carrying a jagged crest of peaks
    let broad = ring(az, r.freq * 0.45, 0, r.seed + 1, ridge: false, oct: 3)
    let crest = ring(az, r.freq, 0, r.seed, ridge: true)
    let fine = ring(az, r.freq * 7, 3, r.seed + 5, ridge: false, oct: 4)
    return r.base + r.amp * (0.6 * ss(0.3, 0.75, broad) + 0.55 * pow(crest, 1.3)) + 0.006 * (fine - 0.5)
}

/// The mountainside as a height field: spurs and couloirs running down the fall line from the crest.
func face(_ r: Range, _ az: Double, _ below: Double) -> Double {
    let x = az * r.freq * 5, y = below * 18
    let warp = fbm(x * 0.5, y * 0.5, r.seed + 40, 3)
    return ridged(x + warp * 1.6, y * 0.6 + warp * 1.2, r.seed + 41, 6)
}

func mountain(_ r: Range, _ az: Double, _ e: Double, top: Double) -> C {
    let below = top - e                                      // how far below the crest line
    // light from the left: faces of spurs that turn to the left are lit, the rest in blue shade
    let d = 0.0015
    let fx = (face(r, az + d, below) - face(r, az - d, below)) / (2 * d)
    let ridgeSlope = (ridgeTop(r, az + d) - ridgeTop(r, az - d)) / (2 * d)
    var lit = 0.55 - 0.012 * fx + 1.0 * ridgeSlope
    lit = max(0, min(1, lit))
    var c = mix(snowShade, snowLit, lit)
    // rock: the steep spur crests show through near the top, and cliff bands lower down
    let f = face(r, az, below)
    let band = ring(az, r.freq * 3, below * 22, r.seed + 13, ridge: false, oct: 4)
    if (f > 0.7 && below > 0.003 && below < 0.12) || (band > 0.64 && below > 0.015) {
        c = mix(c, rock * (0.75 + 0.7 * lit), 0.7)
    }
    // treeline: dark forest below a ragged snowline, with white avalanche chutes and ski runs through it
    if r.forest {
        let line = (r.amp < 0.1 ? top - 0.006 : r.base + (top - r.base) * 0.42) + r.snowline * 0.15 + 0.03 * (ring(az, r.freq * 2.5, 7, r.seed + 3, ridge: false, oct: 4) - 0.5) + 0.025 * (f - 0.5)
        if e < line {
            let tex = fbm(az * 260, e * 260, r.seed + 4, 3)
            var fc = mix(forest, forestLit, lit * 0.55 + 0.35 * tex)
            fc = mix(fc, c, ss(line - 0.008, line, e) * 0.85)               // trees thin out into the snow
            // avalanche chutes: pale streaks down the gullies
            let chute = ring(az, r.freq * 11, 0, r.seed + 7, ridge: true, oct: 3)
            if chute > 0.93 && e > line - 0.05 { fc = mix(fc, snowShade, 0.7 * ss(line - 0.05, line - 0.01, e)) }
            for (k, run) in r.runs.enumerated() {
                let wig = 0.003 * sin(e * 70 + Double(k) * 2) + 0.0015 * sin(e * 160 + Double(k))
                let w = 0.0022 + 0.0012 * Double(k % 3)
                let dist = abs(az - run - wig)
                if dist < w { fc = mix(fc, mix(snowLit * 0.94, snowShade, 0.3 + 0.3 * (1 - lit)), ss(w, w * 0.6, dist)) }
            }
            c = fc
        }
    }
    return c
}

func savePNG(_ px: [UInt8], _ w: Int, _ h: Int, _ path: String) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                               isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: w * 4, bitsPerPixel: 32)!
    px.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, w * h * 4) }
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
    print("wrote", path)
}
func put(_ px: inout [UInt8], _ o: Int, _ c: C, _ a: Double) {
    px[o] = UInt8(max(0, min(255, c.r * 255))); px[o + 1] = UInt8(max(0, min(255, c.g * 255)))
    px[o + 2] = UInt8(max(0, min(255, c.b * 255))); px[o + 3] = UInt8(max(0, min(255, a * 255)))
}

/// A cloud bank: its colour and density at (az, e).
func bank(_ az: Double, _ e: Double, _ lo: Double, _ hi: Double, _ s: Int, _ amt: Double) -> (C, Double) {
    if e < lo - 0.02 || e > hi + 0.04 { return (haze, 0) }
    let b = fbm(az * 7 + 3, e * 26, s, 6)
    let profile = ss(hi + 0.04, lo, e) * ss(lo - 0.02, lo + 0.01, e)
    let dens = ss(0.45, 0.62, b * 0.6 + profile * 0.55) * amt
    if dens <= 0 { return (haze, 0) }
    let up = fbm(az * 7 + 3, (e + 0.01) * 26, s, 6)
    let shade = 0.78 + 0.25 * max(0, min(1, (b - up) * 8 + 0.5))   // tops bright, bellies grey-blue
    return (C(r: 0.92 * shade, g: 0.95 * shade, b: 1.0 * min(1, shade + 0.05)), dens)
}

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let sunDir = (x: -0.49, y: 0.77, z: -0.41)

// ---- the sky alone (the mountains are separate layers in front of it, for parallax)
var px = [UInt8](repeating: 255, count: W * H * 4)
for j in 0..<H {
    let e = (0.5 - (Double(j) + 0.5) / Double(H)) * PI      // elevation: top row is straight up
    for i in 0..<W {
        let az = (Double(i) + 0.5) / Double(W) * 2 * PI
        let dx = cos(e) * sin(az), dy = sin(e), dz = cos(e) * cos(az)
        // the colours of the backdrop photo's own sky (Tools/art/rainier-edit.jpg, measured row by row): it covers
        // up to 0.21 rad, and this carries on from it, deepening to the zenith
        let photoHorizon = C(r: 0.628, g: 0.787, b: 0.906), photoTop = C(r: 0.489, g: 0.674, b: 0.820)
        var c: C
        if e <= 0.21 { c = mix(photoHorizon, photoTop, max(-0.7, e) / 0.21) }
        else { c = mix(photoTop, C(r: 0.20, g: 0.42, b: 0.76), pow((e - 0.21) / (PI / 2 - 0.21), 0.6)) }
        let sunDot = max(0, dx * sunDir.x + dy * sunDir.y + dz * sunDir.z)
        c = c + C(r: 1, g: 0.95, b: 0.85) * (0.25 * pow(sunDot, 24))
        if e > 0.25 {
            // high wispy cloud, seen in perspective on a ceiling: only well above the mountain, so the photo's
            // clear sky meets plain sky
            let k = 1 / max(0.05, dy)
            let cx = dx * k * 1.2, cz = dz * k * 1.2
            let warp = fbm(cx * 0.6, cz * 0.6, 51, 4)
            let wisp = fbm(cx * 0.9 + warp * 1.8, cz * 2.6 + warp, 53, 6)
            c = mix(c, C(r: 0.97, g: 0.98, b: 1.0), ss(0.52, 0.78, wisp) * ss(0.28, 0.55, e) * 0.85)
        }
        c = mix(c, haze, ss(0.045, -0.005, e) * 0.92)         // the valley lost in the fog's haze
        put(&px, (j * W + i) * 4, c, 1)
    }
}
savePNG(px, W, H, outDir + "/SkyPanorama.png")

// ---- the three ranges as transparent bands, elevation E0..E1 (top row is E1)
let LH = 640, E0 = -0.07, E1 = 0.33
let names = ["BackdropFar", "BackdropMid", "BackdropNear"]
for (n, r) in ranges.enumerated() {
    var lp = [UInt8](repeating: 0, count: W * LH * 4)
    let pix = (E1 - E0) / Double(LH)
    for j in 0..<LH {
        let e = E1 - (Double(j) + 0.5) * pix
        for i in 0..<W {
            let az = (Double(i) + 0.5) / Double(W) * 2 * PI
            let top = ridgeTop(r, az)
            var a = max(0, min(1, (top - e) / pix + 0.5))       // antialiased crest
            var c = haze
            if a > 0 { c = mix(mountain(r, az, e, top: top), haze, r.hazeAmt) }
            if n == 0 {
                // cloud wrapping between the far and the mid range
                let (bc, bd) = bank(az, e, 0.05, 0.11, 67, 0.55)
                if bd > 0 { c = a > 0 ? mix(c, bc, bd) : bc; a = a + bd * (1 - a) }
            }
            c = mix(c, haze, ss(0.045, -0.005, e) * 0.92)
            put(&lp, (j * W + i) * 4, c, a)
        }
    }
    savePNG(lp, W, LH, outDir + "/" + names[n] + ".png")
}
