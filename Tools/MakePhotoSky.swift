// Finds the sky in Summit's backdrop photo (Resources/Backdrop/BackdropPhoto.png) and writes it as a mask,
// BackdropPhotoSky.png: white where the photo shows sky, black on the mountains and forest. The backdrop
// shader uses it to let the game's own sky through the photo at dawn, sunset and night, when the photo's
// blue daytime sky would no longer match (by day the photo keeps its own sky).
// A colour key can't do it here: the far side peaks are hazed to almost the sky's own blue. Instead each
// column is walked down from the top, following the sky's slow gradient, until the colour breaks away from
// it: that is the skyline, and everything above it is sky. The rim pixel is half-masked, so no line of
// day-blue is left round the peaks.
// Run: swiftc -O Tools/MakePhotoSky.swift -o /tmp/photosky && /tmp/photosky Assets/Summit/Resources/Backdrop/BackdropPhoto.png Assets/Summit/Resources/Backdrop/BackdropPhotoSky.png
import Foundation
import AppKit

let args = CommandLine.arguments
let src = NSBitmapImageRep(data: try! Data(contentsOf: URL(fileURLWithPath: args[1])))!
let W = src.pixelsWide, H = src.pixelsHigh
var px = [(Double, Double, Double)](repeating: (0, 0, 0), count: W * H)
for j in 0..<H { for i in 0..<W {
    let c = src.colorAt(x: i, y: j)!.usingColorSpace(.deviceRGB)!
    px[j * W + i] = (Double(c.redComponent), Double(c.greenComponent), Double(c.blueComponent))
} }
func dist(_ a: (Double, Double, Double), _ b: (Double, Double, Double)) -> Double {
    let dr = a.0 - b.0, dg = a.1 - b.1, db = a.2 - b.2
    return sqrt(dr * dr + dg * dg + db * db)
}
// the skyline: the first row where the column leaves the sky
var line = [Double](repeating: Double(H), count: W)
for i in 0..<W {
    var ref = px[i], j = 1
    while j < H - 2 {
        let d0 = dist(px[j * W + i], ref), d1 = dist(px[(j + 1) * W + i], ref), d2 = dist(px[(j + 2) * W + i], ref)
        // three in a row clearly off the sky's colour: ground (a single odd pixel is noise)
        if d0 > 0.07 && d1 > 0.07 && d2 > 0.07 { break }
        if d0 < 0.05 { ref = (ref.0 * 0.85 + px[j * W + i].0 * 0.15, ref.1 * 0.85 + px[j * W + i].1 * 0.15, ref.2 * 0.85 + px[j * W + i].2 * 0.15) }
        j += 1
    }
    line[i] = Double(j)
}
// a median across five columns: a lone column that broke off early (a speck in the sky) takes its neighbours' line
let raw = line
for i in 0..<W { line[i] = (max(0, i - 2)...min(W - 1, i + 2)).map { raw[$0] }.sorted()[(min(W - 1, i + 2) - max(0, i - 2)) / 2] }
var out = [UInt8](repeating: 255, count: W * H * 4)
for j in 0..<H { for i in 0..<W {
    // fully sky above the skyline, half on the rim pixel itself, none below
    let m = max(0, min(1, line[i] - Double(j) + 0.5))
    let v = UInt8(m * 255), o = (j * W + i) * 4
    out[o] = v; out[o + 1] = v; out[o + 2] = v; out[o + 3] = 255
} }
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                           isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: W * 4, bitsPerPixel: 32)!
out.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, W * H * 4) }
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: args[2]))
print("wrote", args[2], W, H)
