// Renders the SLOW banner's print (Assets/Summit/Resources/Signs/BannerSlow.png): fluorescent orange fabric, SLOW in
// black, white tape along the hems, a woven texture and grommets at the corners, like the banners resorts string
// across their slow zones. Its own script so the other signs (MakeSigns.swift) are not written again.
// Run: swift Tools/MakeSlowBanner.swift Assets/Summit/Resources/Signs
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let w = 1024, h = 256
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                           hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
// (premultiplied, as AppKit draws into it: the print is opaque everywhere, so it saves exactly the same)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
let W = CGFloat(w), H = CGFloat(h)
NSColor(calibratedRed: 1.0, green: 0.40, blue: 0.04, alpha: 1).setFill()
NSRect(x: 0, y: 0, width: W, height: H).fill()
// the weave: fine threads both ways, a shade lighter and darker
for x in stride(from: 0, to: w, by: 3) {
    NSColor(calibratedWhite: x % 6 == 0 ? 1 : 0, alpha: 0.05).setFill()
    NSRect(x: CGFloat(x), y: 0, width: 1, height: H).fill()
}
for y in stride(from: 0, to: h, by: 3) {
    NSColor(calibratedWhite: y % 6 == 0 ? 1 : 0, alpha: 0.05).setFill()
    NSRect(x: 0, y: CGFloat(y), width: W, height: 1).fill()
}
// white reflective tape along both hems, and the hems' stitching
NSColor.white.setFill()
NSRect(x: 0, y: H * 0.05, width: W, height: H * 0.07).fill()
NSRect(x: 0, y: H * 0.88, width: W, height: H * 0.07).fill()
NSColor(calibratedWhite: 0, alpha: 0.25).setFill()
for x in stride(from: CGFloat(4), to: W, by: 14) {
    NSRect(x: x, y: H * 0.025, width: 7, height: 2).fill()
    NSRect(x: x, y: H * 0.965, width: 7, height: 2).fill()
}
// SLOW, as big as the band between the tapes takes
let font = NSFont(name: "DINCondensed-Bold", size: H * 0.74) ?? NSFont(name: "Futura-CondensedExtraBold", size: H * 0.74) ?? NSFont.boldSystemFont(ofSize: H * 0.74)
let para = NSMutableParagraphStyle(); para.alignment = .center
let s = NSAttributedString(string: "SLOW", attributes: [.font: font, .foregroundColor: NSColor(calibratedWhite: 0.05, alpha: 1), .paragraphStyle: para, .kern: H * 0.09])
let sz = s.size()
s.draw(in: NSRect(x: 0, y: (H - sz.height) / 2 - H * 0.035, width: W, height: sz.height))
// a chevron either side, pointing in at the word
NSColor(calibratedWhite: 0.05, alpha: 1).setFill()
for (cx, dir) in [(W * 0.13, CGFloat(1)), (W * 0.87, CGFloat(-1))] {
    for k in 0..<2 {
        let x0 = cx + dir * CGFloat(k) * H * 0.2 - dir * H * 0.1
        let p = NSBezierPath()
        p.move(to: NSPoint(x: x0, y: H * 0.72)); p.line(to: NSPoint(x: x0 + dir * H * 0.1, y: H * 0.72))
        p.line(to: NSPoint(x: x0 + dir * H * 0.24, y: H * 0.5)); p.line(to: NSPoint(x: x0 + dir * H * 0.1, y: H * 0.28))
        p.line(to: NSPoint(x: x0, y: H * 0.28)); p.line(to: NSPoint(x: x0 + dir * H * 0.14, y: H * 0.5)); p.close(); p.fill()
    }
}
// grommets in the corners, where it is tied to its stakes
for (gx, gy) in [(0.018, 0.17), (0.982, 0.17), (0.018, 0.83), (0.982, 0.83)] {
    let r = H * 0.04
    let c = NSPoint(x: W * CGFloat(gx), y: H * CGFloat(gy))
    NSColor(calibratedWhite: 0.78, alpha: 1).setFill()
    NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)).fill()
    NSColor(calibratedWhite: 0.15, alpha: 1).setFill()
    NSBezierPath(ovalIn: NSRect(x: c.x - r * 0.5, y: c.y - r * 0.5, width: r, height: r)).fill()
}
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/BannerSlow.png"))
print("BannerSlow.png written to \(out)")
