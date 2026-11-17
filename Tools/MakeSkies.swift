// Turns the four Poly Haven "pure sky" HDRIs (CC0; ~/SummitArt/hdri, outside the repo) into Summit's skies:
// one per time of day, Resources/Sky/Sky<Phase>.jpg, imported by Unity as cubemaps (SummitImport.EnsureSky),
// plus Resources/Sky/SkyInfo.json, which DayNight.cs reads to put its light and fog where the photo has them.
// For each sky:
//  - it is turned round (a sideways shift of the panorama) so its sun, or moon, stands where the game wants
//    that phase's light (ahead-left at dawn, behind-left by day, ahead-right at sunset and for the moon);
//  - its brightness is normalised (the open sky between 10 and 60 degrees up comes out the same in all four;
//    DayNight's exposure per phase sets how bright it really is);
//  - everything below the horizon (the photos' mirrored or empty ground) is replaced by the sky's own
//    horizon haze, which is also the phase's fog colour, so the slope's distance melts into it;
//  - it is stored as an 8-bit JPEG with the values squeezed by x/(1+x) (undone in Sky/SkyCycle.shader), so
//    a bright sun still comes back brighter than white, for bloom, without an HDR format's size;
//  - its light is measured for the game: the sun's direction and colour, the horizon's colour, and a coarse
//    grid of the sky's radiance that DayNight turns into the ambient light (a spherical-harmonics probe).
// Run: swiftc -O Tools/MakeSkies.swift -o /tmp/makeskies && /tmp/makeskies ~/SummitArt/hdri Assets/Summit/Resources/Sky
import Foundation
import ImageIO
import CoreGraphics
import UniformTypeIdentifiers

struct Phase { let name, file: String; let az: Double }
// az: where the sun (or moon) should stand, degrees from straight down the mountain (+Z) toward +X (right).
// Low suns sit ahead, or the slope falling away from them would be in shade; the midday one behind-left as
// the game always had it; the sunset and the moon ahead-right, beside the big peak, where you see them.
let phases = [
    Phase(name: "Morning", file: "kloppenheim_06_puresky_4k.hdr", az: 290),
    Phase(name: "Day", file: "kloofendal_48d_partly_cloudy_puresky_4k.hdr", az: 230),
    Phase(name: "Evening", file: "belfast_sunset_puresky_4k.hdr", az: 28),
    Phase(name: "Night", file: "kloppenheim_02_puresky_4k.hdr", az: 38),
]
// Which column of a lat-long picture Unity's cubemap import puts straight ahead (+Z): half-way across (found
// by DayNightShot's sky scan, which turns a camera round to find the sun and logs it against the light).
let uAhead = 0.5
let W = 4096, H = 2048            // the photos' own size: a 1024 cubemap face, as sharp as they are
let GW = 32, GH = 16              // the radiance grid for the ambient light
let skyMedian = 0.3               // the open sky's level after normalising

func load(_ path: String) -> (Int, Int, [Float]) {
    let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil)!
    let img = CGImageSourceCreateImageAtIndex(src, 0, [kCGImageSourceShouldAllowFloat: true] as CFDictionary)!
    let w = img.width, h = img.height
    var px = [Float](repeating: 0, count: w * h * 4)
    let cs = CGColorSpace(name: CGColorSpace.extendedLinearSRGB)!
    px.withUnsafeMutableBytes { buf in
        let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 32, bytesPerRow: w * 16, space: cs,
                            bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue | CGBitmapInfo.floatComponents.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
    }
    return (w, h, px)
}
func lum(_ r: Float, _ g: Float, _ b: Float) -> Float { 0.2126 * r + 0.7152 * g + 0.0722 * b }
func srgb(_ v: Double) -> Double { v <= 0.0031308 ? 12.92 * v : 1.055 * pow(v, 1 / 2.4) - 0.055 }
func hash(_ x: Int, _ y: Int) -> Double {
    var h = UInt64(bitPattern: Int64(x &* 374761393 &+ y &* 668265263))
    h = (h ^ (h >> 13)) &* 1274126177; h ^= h >> 16
    return Double(h & 0xffffff) / Double(0xffffff)
}
func ss(_ a: Double, _ b: Double, _ x: Double) -> Double { let t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t) }

let inDir = (CommandLine.arguments[1] as NSString).expandingTildeInPath, outDir = CommandLine.arguments[2]
var json = "{\n  \"phases\": [\n"
for (pi, ph) in phases.enumerated() {
    let (w, h, src) = load(inDir + "/" + ph.file)
    precondition(w == W && h == H, "expected \(W)x\(H)")
    // the sun: the brightest 8x8 block above the horizon
    var best: Float = 0, bu = 0, bv = 0
    for by in 0..<(h / 16) { for bx in 0..<(w / 8) {
        var s: Float = 0
        for y in 0..<8 { for x in 0..<8 { let o = ((by * 8 + y) * w + bx * 8 + x) * 4; s += lum(src[o], src[o + 1], src[o + 2]) } }
        if s > best { best = s; bu = bx * 8 + 4; bv = by * 8 + 4 }
    } }
    let sunEl = (0.5 - (Double(bv) + 0.5) / Double(h)) * 180
    // its colour: the bright core averaged, scaled to the brightest channel
    var sc = (0.0, 0.0, 0.0)
    for y in (bv - 3)...(bv + 3) { for x in (bu - 3)...(bu + 3) { let o = (y * w + (x + w) % w) * 4; sc.0 += Double(src[o]); sc.1 += Double(src[o + 1]); sc.2 += Double(src[o + 2]) } }
    let sm = max(sc.0, sc.1, sc.2); sc = (sc.0 / sm, sc.1 / sm, sc.2 / sm)
    // normalise: the open sky's median luminance to skyMedian
    var lums = [Float]()
    for y in stride(from: 0, to: h / 2, by: 4) {
        let e = (0.5 - (Double(y) + 0.5) / Double(h)) * 180
        if e < 10 || e > 60 { continue }
        for x in stride(from: 0, to: w, by: 4) { let o = (y * w + x) * 4; lums.append(lum(src[o], src[o + 1], src[o + 2])) }
    }
    lums.sort()
    let k = skyMedian / Double(lums[lums.count / 2])
    // the turn: the column that holds the sun moves to the column Unity shows at the wanted azimuth
    let sunU = (Double(bu) + 0.5) / Double(w), wantU = uAhead + ph.az / 360
    let shift = Int(((wantU - sunU) * Double(w)).rounded())
    func at(_ x: Int, _ y: Int) -> (Double, Double, Double) {
        let o = (y * w + ((x - shift) % w + w) % w) * 4
        return (Double(src[o]) * k, Double(src[o + 1]) * k, Double(src[o + 2]) * k)
    }
    // the horizon's haze: each column's own colour just above it (softened sideways), and for the fog the
    // middle one of those by brightness (the mean would be swamped by a low sun's glow)
    let y0 = Int((0.5 - 2.0 / 180) * Double(h)), y1 = Int((0.5 - 0.5 / 180) * Double(h))
    var colH = [(Double, Double, Double)](repeating: (0, 0, 0), count: w)
    for x in 0..<w {
        var c = (0.0, 0.0, 0.0)
        for y in y0...y1 { let v = at(x, y); c.0 += v.0; c.1 += v.1; c.2 += v.2 }
        let n = Double(y1 - y0 + 1); colH[x] = (c.0 / n, c.1 / n, c.2 / n)
    }
    let R = 64
    var soft = colH
    for x in 0..<w {
        var c = (0.0, 0.0, 0.0)
        for d in -R...R { let v = colH[(x + d + w) % w]; c.0 += v.0; c.1 += v.1; c.2 += v.2 }
        let n = Double(2 * R + 1); soft[x] = (c.0 / n, c.1 / n, c.2 / n)
    }
    let hz = soft.sorted { 0.2126 * $0.0 + 0.7152 * $0.1 + 0.0722 * $0.2 < 0.2126 * $1.0 + 0.7152 * $1.1 + 0.0722 * $1.2 }[w / 2]
    // the finished sky, linear: below the horizon, that column's haze (from 0.5 degrees up to 1.5 down), then
    // the fog's own colour further down
    // (rows are worked in parallel, each writing only its own slice of these buffers)
    let lin = UnsafeMutablePointer<Float>.allocate(capacity: w * h * 3)
    defer { lin.deallocate() }
    DispatchQueue.concurrentPerform(iterations: h) { y in
        let e = (0.5 - (Double(y) + 0.5) / Double(h)) * 180
        let f = ss(0.5, -1.5, e), g = ss(-1.5, -12, e)
        for x in 0..<w {
            let s = soft[x], fill = (s.0 + (hz.0 - s.0) * g, s.1 + (hz.1 - s.1) * g, s.2 + (hz.2 - s.2) * g)
            var c = f >= 1 ? fill : at(x, y)
            if f > 0 && f < 1 { c = (c.0 + (fill.0 - c.0) * f, c.1 + (fill.1 - c.1) * f, c.2 + (fill.2 - c.2) * f) }
            let o = (y * w + x) * 3
            lin[o] = Float(c.0); lin[o + 1] = Float(c.1); lin[o + 2] = Float(c.2)
        }
    }
    // stored: x/(1+x), sRGB, a hair of dither so the dark gradients don't band, JPEG
    let out = UnsafeMutablePointer<UInt8>.allocate(capacity: w * h * 4)
    defer { out.deallocate() }
    DispatchQueue.concurrentPerform(iterations: h) { y in
        for x in 0..<w {
            let o = (y * w + x) * 3, n = (hash(x, y + pi * 9973) - 0.5) / 255
            for c in 0..<3 { let v = Double(lin[o + c]); out[(y * w + x) * 4 + c] = UInt8(max(0, min(255, (srgb(v / (1 + v)) + n) * 255 + 0.5))) }
            out[(y * w + x) * 4 + 3] = 255
        }
    }
    let ctx = CGContext(data: out, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
    let path = outDir + "/Sky" + ph.name + ".jpg"
    let dst = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL, UTType.jpeg.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(dst, ctx.makeImage()!, [kCGImageDestinationLossyCompressionQuality: 0.88] as CFDictionary)
    CGImageDestinationFinalize(dst)
    // the radiance grid (in the game's directions: column 0 starts straight ahead, turning right)
    var grid = [Double](repeating: 0, count: GW * GH * 3)
    let cw = w / GW, ch = h / GH
    for gy in 0..<GH { for gx in 0..<GW {
        var s = (0.0, 0.0, 0.0)
        let x0 = Int((uAhead * Double(w)).rounded()) + gx * cw
        for y in stride(from: gy * ch, to: (gy + 1) * ch, by: 2) { for x in stride(from: x0, to: x0 + cw, by: 2) {
            let o = (y * w + x % w) * 3; s.0 += Double(lin[o]); s.1 += Double(lin[o + 1]); s.2 += Double(lin[o + 2])
        } }
        let n = Double(cw * ch / 4), g = (gy * GW + gx) * 3
        grid[g] = s.0 / n; grid[g + 1] = s.1 / n; grid[g + 2] = s.2 / n
    } }
    func f(_ v: Double) -> String { String(format: "%.5f", v) }
    print(ph.name, "sun el", String(format: "%.1f", sunEl), "colour", f(sc.0), f(sc.1), f(sc.2), "scale", f(k), "horizon", f(hz.0), f(hz.1), f(hz.2), "->", path)
    json += "    { \"name\": \"\(ph.name)\", \"sunAz\": \(ph.az), \"sunEl\": \(f(sunEl)), \"sunColour\": [\(f(sc.0)), \(f(sc.1)), \(f(sc.2))],\n"
    json += "      \"horizon\": [\(f(hz.0)), \(f(hz.1)), \(f(hz.2))],\n"
    json += "      \"grid\": [" + grid.map(f).joined(separator: ", ") + "] }" + (pi < phases.count - 1 ? "," : "") + "\n"
}
json += "  ],\n  \"gridW\": \(GW), \"gridH\": \(GH)\n}\n"
try! json.write(toFile: outDir + "/SkyInfo.json", atomically: true, encoding: .utf8)
print("wrote", outDir + "/SkyInfo.json")
