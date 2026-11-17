// Paints the time-of-day skies that go with Summit's day sky (Tools/MakeSky.swift, Materials/SkyPanorama.png):
// a dawn, a sunset and a night, each a 4096x2048 latitude-longitude panorama in the same layout, for the
// Summit/SkyCycle skybox (Resources/Sky/SkyCycle.shader), which blends two of them as the day turns.
// Each sky's sun (or moon) glow sits where DayNight.cs puts that phase's light, so the bright side of the
// sky and the light on the snow agree. Stars and the moon's disc are drawn by the shader, not painted here.
// Run: swiftc -O Tools/MakeSkyCycle.swift -o /tmp/makeskycycle && /tmp/makeskycycle Assets/Summit/Resources/Sky
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

// ---- noise (the same as MakeSky.swift, so the cloud has the same character as the day's)
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

func savePNG(_ px: [UInt8], _ w: Int, _ h: Int, _ path: String) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                               isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: w * 4, bitsPerPixel: 32)!
    px.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, w * h * 4) }
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
    print("wrote", path)
}

/// One time of day's sky. Azimuth 0 is straight down the mountain (+Z), pi/2 is to the right (+X), as in MakeSky.
struct Sky {
    let name: String
    let sunAz, sunE: Double          // where the glow is (radians): DayNight.cs lights the snow from here
    let horizonSun, horizonAway: C   // the horizon toward the sun and away from it
    let midSun, midAway: C           // at the top of the backdrop photo (0.21 rad), where it meets the photo's sky
    let zenith: C
    let glow: C; let glowWide, glowTight: Double
    let disc: Bool                   // paint the sun itself (low suns only: the moon is the shader's)
    let cloudLit, cloudShade: C; let cloudAmt, cloudLow: Double
    let milkyWay: Double
    let seed: Int
}

let skies = [
    // dawn: the sun just up ahead-left, a peach horizon there, lavender-pink round the rest, pale blue above
    Sky(name: "SkyMorning", sunAz: 290 * PI / 180, sunE: 10 * PI / 180,
        horizonSun: C(r: 1.00, g: 0.80, b: 0.62), horizonAway: C(r: 0.86, g: 0.76, b: 0.84),
        midSun: C(r: 0.86, g: 0.80, b: 0.82), midAway: C(r: 0.66, g: 0.70, b: 0.86),
        zenith: C(r: 0.24, g: 0.38, b: 0.68),
        glow: C(r: 1.0, g: 0.78, b: 0.52), glowWide: 0.35, glowTight: 0.45, disc: true,
        cloudLit: C(r: 1.0, g: 0.80, b: 0.72), cloudShade: C(r: 0.66, g: 0.62, b: 0.76), cloudAmt: 0.75, cloudLow: 0.16,
        milkyWay: 0, seed: 101),
    // sunset: the sun low ahead-right, orange there, rose and mauve elsewhere, a deepening violet-blue overhead
    Sky(name: "SkyEvening", sunAz: 70 * PI / 180, sunE: 6 * PI / 180,
        horizonSun: C(r: 1.00, g: 0.62, b: 0.34), horizonAway: C(r: 0.92, g: 0.62, b: 0.68),
        midSun: C(r: 0.92, g: 0.62, b: 0.56), midAway: C(r: 0.70, g: 0.56, b: 0.72),
        zenith: C(r: 0.22, g: 0.25, b: 0.52),
        glow: C(r: 1.0, g: 0.62, b: 0.30), glowWide: 0.45, glowTight: 0.55, disc: true,
        cloudLit: C(r: 1.0, g: 0.62, b: 0.50), cloudShade: C(r: 0.48, g: 0.38, b: 0.56), cloudAmt: 0.85, cloudLow: 0.13,
        milkyWay: 0, seed: 202),
    // night: deep blue-black overhead, a little paler at the horizon, a cold glow round the moon ahead-right
    // (DayNight.MoonAz/MoonE), a faint Milky Way and a few dark clouds with moonlit edges
    Sky(name: "SkyNight", sunAz: 22 * PI / 180, sunE: 14 * PI / 180,
        horizonSun: C(r: 0.12, g: 0.17, b: 0.28), horizonAway: C(r: 0.08, g: 0.12, b: 0.22),
        midSun: C(r: 0.08, g: 0.12, b: 0.22), midAway: C(r: 0.05, g: 0.08, b: 0.16),
        zenith: C(r: 0.015, g: 0.028, b: 0.07),
        glow: C(r: 0.30, g: 0.36, b: 0.48), glowWide: 0.25, glowTight: 0.35, disc: false,
        cloudLit: C(r: 0.20, g: 0.23, b: 0.32), cloudShade: C(r: 0.035, g: 0.05, b: 0.09), cloudAmt: 0.6, cloudLow: 0.18,
        milkyWay: 1, seed: 303),
]

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
// the Milky Way: a band round a tilted great circle
let mwN: (Double, Double, Double) = { let v = (0.35, 0.55, -0.76); let l = sqrt(v.0 * v.0 + v.1 * v.1 + v.2 * v.2); return (v.0 / l, v.1 / l, v.2 / l) }()

for s in skies {
    // rows are painted in parallel straight into one buffer (each row its own slice)
    let buf = UnsafeMutablePointer<UInt8>.allocate(capacity: W * H * 4)
    let sd = (x: cos(s.sunE) * sin(s.sunAz), y: sin(s.sunE), z: cos(s.sunE) * cos(s.sunAz))
    DispatchQueue.concurrentPerform(iterations: H) { j in
        let e = (0.5 - (Double(j) + 0.5) / Double(H)) * PI      // elevation: top row is straight up
        let row = buf + j * W * 4
        for i in 0..<W {
            let az = (Double(i) + 0.5) / Double(W) * 2 * PI
            let dx = cos(e) * sin(az), dy = sin(e), dz = cos(e) * cos(az)
            let dot = max(-1, min(1, dx * sd.x + dy * sd.y + dz * sd.z))
            // how much this part of the sky faces the sun round the horizon (1 toward it, 0 opposite)
            let azDot = cos(az - s.sunAz), toward = pow((1 + azDot) / 2, 1.6)
            let horizon = mix(s.horizonAway, s.horizonSun, toward)
            let mid = mix(s.midAway, s.midSun, toward * toward)
            var c: C
            if e <= 0.21 { c = mix(horizon, mid, ss(0, 0.21, max(0, e))) }
            else { c = mix(mid, s.zenith, pow((e - 0.21) / (PI / 2 - 0.21), 0.55)) }
            // the glow round the sun: a wide warm wash and a tight bright core
            let g = max(0, dot)
            c = c + s.glow * (s.glowWide * pow(g, 6) + s.glowTight * pow(g, 48))
            if s.disc { c = mix(c, C(r: 1.0, g: 0.93, b: 0.78), ss(0.99985, 0.99993, dot)) }
            if s.milkyWay > 0 && e > 0.05 {
                let d = abs(dx * mwN.0 + dy * mwN.1 + dz * mwN.2)
                let band = exp(-pow(d / 0.16, 2))
                let dust = fbm(dx * 9 + 20, dz * 9 + dy * 5, s.seed + 9, 5)
                let lane = ss(0.5, 0.7, fbm(dx * 14, dz * 14 + dy * 7, s.seed + 11, 4))   // the dark dust lane
                c = c + C(r: 0.12, g: 0.13, b: 0.16) * (band * ss(0.35, 0.75, dust) * (1 - 0.6 * lane) * ss(0.05, 0.3, e) * s.milkyWay)
            }
            if e > s.cloudLow - 0.06 {
                // high wispy cloud on a ceiling, as in the day sky, lower and thicker toward the low sun
                let k = 1 / max(0.05, dy)
                let cx = dx * k * 1.2, cz = dz * k * 1.2
                let warp = fbm(cx * 0.6, cz * 0.6, s.seed + 51, 4)
                let wisp = fbm(cx * 0.9 + warp * 1.8, cz * 2.6 + warp, s.seed + 53, 6)
                let amt = ss(0.52, 0.78, wisp) * ss(s.cloudLow - 0.06, s.cloudLow + 0.2, e) * s.cloudAmt
                // lit from the sun's side: the near ones bright and coloured, the far ones in shade
                let lit = ss(-0.2, 0.9, dot) * (0.55 + 0.45 * ss(0.4, 0.8, wisp))
                c = mix(c, mix(s.cloudShade, s.cloudLit, lit), amt)
            }
            // a hair of noise so 8 bits don't band in the dark gradients
            let n = (hash(i, j, s.seed) - 0.5) / 255
            let o = i * 4
            row[o] = UInt8(max(0, min(255, (c.r + n) * 255))); row[o + 1] = UInt8(max(0, min(255, (c.g + n) * 255)))
            row[o + 2] = UInt8(max(0, min(255, (c.b + n) * 255))); row[o + 3] = 255
        }
    }
    let px = Array(UnsafeBufferPointer(start: buf, count: W * H * 4)); buf.deallocate()
    savePNG(px, W, H, outDir + "/" + s.name + ".png")
}
