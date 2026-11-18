// Turns a wide mountain photograph into Summit's backdrop band: crops away the foreground at the bottom
// and the roadside on the right, keys out the plain blue sky (so the game's own sky and clouds show round
// the peaks) and feathers the two ends, then writes a 4096-wide RGBA PNG.
// Run: swiftc -O Tools/MakePhotoBand.swift -o /tmp/band && /tmp/band in.jpg out.png
// The extended photo (Tools/art/rainier-extended.jpg, cropped to 2000x664 from row 162 so its top sits at 0.21 rad):
//   /tmp/band rainier-extended-crop.jpg BackdropPhoto.png edited 0.985 0.175
import Foundation
import AppKit

let args = CommandLine.arguments
let inPath = args.count > 1 ? args[1] : "rainier.jpg", outPath = args.count > 2 ? args[2] : "BackdropPhoto.png"
// "edited": an already-cropped picture with the forest running to its bottom edge (Tools/art/rainier-edit.jpg):
// used whole, at its own resolution, with only a plain haze fade below it (nothing copied or mirrored)
let edited = args.count > 3 && args[3] == "edited"
// the part of the photo to keep (fractions of its width and height)
let cropX0 = 0.0, cropX1 = edited ? 1.0 : 0.775, cropY0 = 0.0, cropY1 = edited ? 1.0 : 0.70

let src = NSBitmapImageRep(data: try! Data(contentsOf: URL(fileURLWithPath: inPath)))!
let SW = src.pixelsWide, SH = src.pixelsHigh
let x0 = Int(Double(SW) * cropX0), x1 = Int(Double(SW) * cropX1), y0 = Int(Double(SH) * cropY0), y1 = Int(Double(SH) * cropY1)
let CW = x1 - x0, CH = y1 - y0
let W = edited ? min(4096, CW) : 4096, H = Int(Double(W) * Double(CH) / Double(CW))
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
// (an edited picture keeps its own sky: the game's sky is painted to carry on from it)
if !edited { for i in 0..<W where skyMask[i] > 0.3 { stack.append(i); seen[i] = true } }
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
        if edited { a *= ss(0.0, 0.12, Double(j) / Double(H)) }   // the top of its sky melts into the painted sky
        let (r, g, b) = cols[j * W + i]
        let o = (j * W + i) * 4
        out[o] = UInt8(max(0, min(255, r * 255))); out[o + 1] = UInt8(max(0, min(255, g * 255)))
        out[o + 2] = UInt8(max(0, min(255, b * 255))); out[o + 3] = UInt8(max(0, min(255, a * 255)))
    }
}
if edited {
    // the picture covers PhotoTop..PhotoBottom radians of elevation in the game; below it, down to HazeBottom,
    // each column's own forest colour (the average of its last rows, softened sideways) fades into the haze
    // (optional args: the picture's span of elevation and the haze below it, in radians; the defaults are the
    // first edit's, Tools/art/rainier-edit.jpg)
    let photoSpan = args.count > 4 ? Double(args[4])! : 0.737, hazeSpan = args.count > 5 ? Double(args[5])! : 0.85 - 0.527
    let extra = Int((Double(H) * hazeSpan / photoSpan).rounded()), H2 = H + extra
    var ext = [UInt8](repeating: 0, count: W * H2 * 4)
    for k in 0..<(W * H * 4) { ext[k] = out[k] }
    let haze = (0.80, 0.86, 0.94)
    let rows = 10
    var avg = [(Double, Double, Double)](repeating: (0, 0, 0), count: W)
    for i in 0..<W {
        var r = 0.0, g = 0.0, b = 0.0
        for j in (H - rows)..<H { let o = (j * W + i) * 4; r += Double(out[o]); g += Double(out[o + 1]); b += Double(out[o + 2]) }
        avg[i] = (r / Double(rows * 255), g / Double(rows * 255), b / Double(rows * 255))
    }
    var soft = avg
    let R = 30
    for i in 0..<W {
        var r = 0.0, g = 0.0, b = 0.0, n = 0.0
        for d in -R...R { let k = min(W - 1, max(0, i + d)); r += avg[k].0; g += avg[k].1; b += avg[k].2; n += 1 }
        soft[i] = (r / n, g / n, b / n)
    }
    for j in H..<H2 {
        let f = Double(j - H) / Double(extra)
        let toSoft = ss(0, 0.06, f), toHaze = 0.95 * ss(0.02, 0.9, f)
        for i in 0..<W {
            let so = ((H - 1) * W + i) * 4, o = (j * W + i) * 4
            let last = (Double(out[so]) / 255, Double(out[so + 1]) / 255, Double(out[so + 2]) / 255)
            var c = (last.0 + (soft[i].0 - last.0) * toSoft, last.1 + (soft[i].1 - last.1) * toSoft, last.2 + (soft[i].2 - last.2) * toSoft)
            c = (c.0 + (haze.0 - c.0) * toHaze, c.1 + (haze.1 - c.1) * toHaze, c.2 + (haze.2 - c.2) * toHaze)
            let u = Double(i) / Double(W - 1)
            ext[o] = UInt8(c.0 * 255); ext[o + 1] = UInt8(c.1 * 255); ext[o + 2] = UInt8(c.2 * 255)
            ext[o + 3] = UInt8(255 * ss(0.0, 0.07, u) * ss(1.0, 0.93, u))
        }
    }
    print("edited: \(W)x\(H2), the picture is the top \(H) rows, haze below")
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H2, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                               isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: W * 4, bitsPerPixel: 32)!
    ext.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, W * H2 * 4) }
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: outPath))
    print("wrote", outPath)
    exit(0)
}
// below the photo: the forest carries on down into the valley and fades into the game's fog haze, so
// there is never a blank strip between the end of the drawn terrain and the bottom of the picture.
// The fill is made only from distant forest with no near trees in it (the donor stretch below), so the
// big foreground firs never get copied and mirrored into a kaleidoscope; the photo's own bottom rows
// blend into that fill, so the near trees' trunks sink into the forest rather than stopping at a line.
let extra = Int(Double(H) * 0.9), H2 = H + extra
var ext = [UInt8](repeating: 0, count: W * H2 * 4)
for k in 0..<(W * H * 4) { ext[k] = out[k] }
let haze = (0.80, 0.86, 0.94)
let donorX0 = Int(Double(W) * 0.185), donorX1 = Int(Double(W) * 0.50)
let cut = 16                                   // the photo's last rows are a pale hazy strip: neither kept nor copied
let Hs = H - cut                               // where the fill takes over completely
let band = max(8, H / 9)                       // the donor rows: the photo's lowest forest, above that strip
let dy0 = Hs - band
let blend = H / 16                             // photo rows that fade into the fill
// the donor patch made seamlessly periodic (its far edge cross-faded into its near one), so it repeats
// with no mirror lines: Px across, Py down
let Fx = 90, Fy = band / 3
let Px = donorX1 - donorX0 - Fx, Py = band - Fy
func at(_ x: Int, _ y: Int) -> (Double, Double, Double, Double) {
    let o = (y * W + x) * 4
    return (Double(out[o]) / 255, Double(out[o + 1]) / 255, Double(out[o + 2]) / 255, Double(out[o + 3]) / 255)
}
func mix(_ a: (Double, Double, Double, Double), _ b: (Double, Double, Double, Double), _ t: Double) -> (Double, Double, Double, Double) {
    (a.0 + (b.0 - a.0) * t, a.1 + (b.1 - a.1) * t, a.2 + (b.2 - a.2) * t, a.3 + (b.3 - a.3) * t)
}
func row(_ tx: Int, _ y: Int) -> (Double, Double, Double, Double) {
    tx < Fx ? mix(at(donorX0 + Px + tx, y), at(donorX0 + tx, y), Double(tx) / Double(Fx)) : at(donorX0 + tx, y)
}
func fill(_ i: Int, _ j: Int) -> (Double, Double, Double, Double) {
    let tx = ((i % Px) + Px) % Px, ty = (((j - Hs) % Py) + Py) % Py
    return ty < Fy ? mix(row(tx, dy0 + Py + ty), row(tx, dy0 + ty), Double(ty) / Double(Fy)) : row(tx, dy0 + ty)
}
for j in (Hs - blend)..<H2 {
    let f = j >= H ? Double(j - H) / Double(extra) : 0
    let toHaze = 0.92 * ss(0.0, 0.85, f)       // further down the slope is further away: hazier (none at the seam)
    let mixIn = j >= Hs ? 1.0 : ss(0, 1, Double(j - (Hs - blend)) / Double(blend))
    for i in 0..<W {
        let o = (j * W + i) * 4
        let u = Double(i) / Double(W - 1)
        let feather = ss(0.0, 0.07, u) * ss(1.0, 0.93, u)
        var (r, g, b, a) = fill(i, j)
        a = feather                            // the fill is solid forest, feathered at the band's ends
        if mixIn < 1 {
            let p = at(i, j)
            (r, g, b, a) = mix(p, (r, g, b, a), mixIn)
        }
        ext[o] = UInt8(max(0, min(255, (r + (haze.0 - r) * toHaze) * 255)))
        ext[o + 1] = UInt8(max(0, min(255, (g + (haze.1 - g) * toHaze) * 255)))
        ext[o + 2] = UInt8(max(0, min(255, (b + (haze.2 - b) * toHaze) * 255)))
        ext[o + 3] = UInt8(max(0, min(255, a * 255)))
    }
}
print("extended to \(W)x\(H2): the photo is the top \(H) rows")
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H2, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                           isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: W * 4, bitsPerPixel: 32)!
ext.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, W * H2 * 4) }
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: outPath))
print("wrote", outPath)
