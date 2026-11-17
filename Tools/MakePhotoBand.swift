// Turns a wide mountain photograph into Summit's backdrop band: crops away the foreground at the bottom
// and the roadside on the right, keys out the plain blue sky (so the game's own sky and clouds show round
// the peaks) and feathers the two ends, then writes a 4096-wide RGBA PNG.
// Run: swiftc -O Tools/MakePhotoBand.swift -o /tmp/band && /tmp/band in.jpg out.png
import Foundation
import AppKit

let args = CommandLine.arguments
let inPath = args.count > 1 ? args[1] : "rainier.jpg", outPath = args.count > 2 ? args[2] : "BackdropPhoto.png"
// the part of the photo to keep (fractions of its width and height)
let cropX0 = 0.0, cropX1 = 0.775, cropY0 = 0.0, cropY1 = 0.70

let src = NSBitmapImageRep(data: try! Data(contentsOf: URL(fileURLWithPath: inPath)))!
let SW = src.pixelsWide, SH = src.pixelsHigh
let x0 = Int(Double(SW) * cropX0), x1 = Int(Double(SW) * cropX1), y0 = Int(Double(SH) * cropY0), y1 = Int(Double(SH) * cropY1)
let CW = x1 - x0, CH = y1 - y0
let W = 4096, H = Int(Double(W) * Double(CH) / Double(CW))
print("source \(SW)x\(SH) crop \(CW)x\(CH) -> \(W)x\(H)")

func ss(_ a: Double, _ b: Double, _ x: Double) -> Double { let t = max(0, min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t) }

// read the source into a float buffer once
var sp = [Float](repeating: 0, count: SW * SH * 3)
for y in 0..<SH { for x in 0..<SW {
    let c = src.colorAt(x: x, y: y)!.usingColorSpace(.deviceRGB)!
    let o = (y * SW + x) * 3
    sp[o] = Float(c.redComponent); sp[o + 1] = Float(c.greenComponent); sp[o + 2] = Float(c.blueComponent)
} }
func sample(_ fx: Double, _ fy: Double) -> (Double, Double, Double) {
    // bilinear, in source pixels
    let x = max(0, min(Double(SW - 1) - 0.001, fx)), y = max(0, min(Double(SH - 1) - 0.001, fy))
    let ix = Int(x), iy = Int(y), tx = x - Double(ix), ty = y - Double(iy)
    func p(_ a: Int, _ b: Int) -> (Double, Double, Double) { let o = (b * SW + a) * 3; return (Double(sp[o]), Double(sp[o + 1]), Double(sp[o + 2])) }
    let a = p(ix, iy), b = p(ix + 1, iy), c = p(ix, iy + 1), d = p(ix + 1, iy + 1)
    func l(_ u: Double, _ v: Double, _ t: Double) -> Double { u + (v - u) * t }
    return (l(l(a.0, b.0, tx), l(c.0, d.0, tx), ty), l(l(a.1, b.1, tx), l(c.1, d.1, tx), ty), l(l(a.2, b.2, tx), l(c.2, d.2, tx), ty))
}

var out = [UInt8](repeating: 0, count: W * H * 4)
// first pass: sky key per pixel (blue, bright, unsaturated-red) -> alpha
var alpha = [Double](repeating: 1, count: W * H)
var cols = [(Double, Double, Double)](repeating: (0, 0, 0), count: W * H)
var skyMask = [Double](repeating: 0, count: W * H)
for j in 0..<H {
    for i in 0..<W {
        let fx = Double(x0) + (Double(i) + 0.5) / Double(W) * Double(CW)
        let fy = Double(y0) + (Double(j) + 0.5) / Double(H) * Double(CH)
        var (r, g, b) = sample(fx, fy)
        // sky-likeness: clearly bluer than red, and bright (snow is near-white, its shadows only faintly blue)
        let skyness = ss(0.09, 0.2, b - r) * ss(0.55, 0.7, b)
        skyMask[j * W + i] = skyness
        // grade toward the game's cool winter light: a little less green, a touch bluer in shadow
        let lum = 0.3 * r + 0.59 * g + 0.11 * b
        g = g * 0.92 + lum * 0.08; r = r * 0.95 + lum * 0.05; b = min(1, b * 1.04 + 0.01)
        cols[j * W + i] = (r, g, b)
        alpha[j * W + i] = 1
    }
}
// only sky that is joined to the open sky at the top counts: flood fill down from the top edge, so blue
// shadows in the snow are never punched out
var seen = [Bool](repeating: false, count: W * H)
var stack = [Int]()
for i in 0..<W where skyMask[i] > 0.3 { stack.append(i); seen[i] = true }
while let k = stack.popLast() {
    alpha[k] = 1 - skyMask[k]
    let i = k % W, j = k / W
    for (di, dj) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
        let ii = i + di, jj = j + dj
        if ii < 0 || ii >= W || jj < 0 || jj >= H { continue }
        let q = jj * W + ii
        if !seen[q] && skyMask[q] > 0.3 { seen[q] = true; stack.append(q) }
    }
}
// soften the key a pixel or two, and feather the ends of the band
for j in 0..<H {
    for i in 0..<W {
        var a = 0.0, n = 0.0
        for dj in -1...1 { for di in -1...1 {
            let ii = min(W - 1, max(0, i + di)), jj = min(H - 1, max(0, j + dj)); a += alpha[jj * W + ii]; n += 1
        } }
        a /= n
        let u = Double(i) / Double(W - 1)
        a *= ss(0.0, 0.07, u) * ss(1.0, 0.93, u)
        let (r, g, b) = cols[j * W + i]
        let o = (j * W + i) * 4
        out[o] = UInt8(max(0, min(255, r * 255))); out[o + 1] = UInt8(max(0, min(255, g * 255)))
        out[o + 2] = UInt8(max(0, min(255, b * 255))); out[o + 3] = UInt8(max(0, min(255, a * 255)))
    }
}
// below the photo: the forest carries on down into the valley (the photo's lowest rows mirrored downward,
// so the texture stays real) and fades into the game's fog haze, so there is never a blank strip between
// the end of the drawn terrain and the bottom of the picture
let extra = Int(Double(H) * 0.9), H2 = H + extra
var ext = [UInt8](repeating: 0, count: W * H2 * 4)
for k in 0..<(W * H * 4) { ext[k] = out[k] }
let haze = (0.80, 0.86, 0.94)
for j in H..<H2 {
    let d = j - H
    let band = max(8, H / 12), k = d % (2 * band)          // only the lowest forest rows, back and forth (no seams)
    let srcRow = max(0, H - 1 - (k < band ? k : 2 * band - 1 - k))
    let f = Double(d) / Double(extra)
    let toHaze = 0.92 * ss(0.0, 0.85, f)                  // further down the slope is further away: hazier (none at the seam)
    for i in 0..<W {
        let so = (srcRow * W + i) * 4, o = (j * W + i) * 4
        let r = Double(out[so]) / 255, g = Double(out[so + 1]) / 255, b = Double(out[so + 2]) / 255
        let a = Double(out[so + 3]) / 255
        ext[o] = UInt8(max(0, min(255, (r + (haze.0 - r) * toHaze) * 255)))
        ext[o + 1] = UInt8(max(0, min(255, (g + (haze.1 - g) * toHaze) * 255)))
        ext[o + 2] = UInt8(max(0, min(255, (b + (haze.2 - b) * toHaze) * 255)))
        ext[o + 3] = UInt8(max(0, min(255, a * 255)))
    }
}
print("extended to \(W)x\(H2): the photo is the top \(H) rows")
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H2, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                           isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: W * 4, bitsPerPixel: 32)!
ext.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, W * H2 * 4) }
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: outPath))
print("wrote", outPath)
