// Renders Summit's banner and section-sign textures (printed-vinyl look) to PNGs.
// Run: swift Tools/MakeSigns.swift <outdir>
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

func font(_ size: CGFloat) -> NSFont {
    return NSFont(name: "DINCondensed-Bold", size: size) ?? NSFont(name: "Futura-CondensedExtraBold", size: size) ?? NSFont.boldSystemFont(ofSize: size)
}

func make(_ name: String, w: Int, h: Int, bg: NSColor, fg: NSColor, text: String, sub: String?, hazard: Bool, stripe: NSColor) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let W = CGFloat(w), H = CGFloat(h)
    bg.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    if hazard {
        // diagonal hazard stripes along top and bottom bands
        NSColor(calibratedWhite: 0.08, alpha: 1).setFill()
        let band = H * 0.16
        for y0 in [CGFloat(0), H - band] {
            var x: CGFloat = -band
            while x < W + band {
                let p = NSBezierPath()
                p.move(to: NSPoint(x: x, y: y0)); p.line(to: NSPoint(x: x + band * 0.7, y: y0))
                p.line(to: NSPoint(x: x + band * 1.7, y: y0 + band)); p.line(to: NSPoint(x: x + band, y: y0 + band)); p.close(); p.fill()
                x += band * 1.4
            }
        }
    } else {
        // printed stripes near the hems
        stripe.setFill()
        NSRect(x: 0, y: H * 0.07, width: W, height: H * 0.045).fill()
        NSRect(x: 0, y: H * 0.885, width: W, height: H * 0.045).fill()
    }
    // vinyl texture: fine speckle and soft horizontal sheen
    srand48(Int(w * 31 + h))
    for _ in 0..<(w * h / 6) {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H
        NSColor(calibratedWhite: drand48() < 0.5 ? 0 : 1, alpha: 0.05).setFill()
        NSRect(x: x, y: y, width: 1.5, height: 1.5).fill()
    }
    for i in 0..<6 {
        let y = H * CGFloat(i) / 6
        NSColor(calibratedWhite: 1, alpha: 0.025 + 0.02 * CGFloat(i % 2)).setFill()
        NSRect(x: 0, y: y, width: W, height: H / 12).fill()
    }
    // text with a slight print shadow
    let size = H * (sub == nil ? 0.66 : 0.5)
    let shadow = NSShadow(); shadow.shadowOffset = NSSize(width: 0, height: -H * 0.015); shadow.shadowBlurRadius = H * 0.02
    shadow.shadowColor = NSColor(calibratedWhite: 0, alpha: 0.35)
    let para = NSMutableParagraphStyle(); para.alignment = .center
    let attrs: [NSAttributedString.Key: Any] = [.font: font(size), .foregroundColor: fg, .shadow: shadow, .paragraphStyle: para, .kern: size * 0.06]
    let s = NSAttributedString(string: text, attributes: attrs)
    let sz = s.size()
    let ty = sub == nil ? (H - sz.height) / 2 - H * 0.02 : H * 0.36
    s.draw(in: NSRect(x: 0, y: ty, width: W, height: sz.height))
    if let sub = sub {
        let a2: [NSAttributedString.Key: Any] = [.font: font(H * 0.17), .foregroundColor: fg, .paragraphStyle: para, .kern: H * 0.02]
        NSAttributedString(string: sub, attributes: a2).draw(in: NSRect(x: 0, y: H * 0.17, width: W, height: H * 0.22))
    }
    // grommets in the corners
    for (gx, gy) in [(0.025, 0.2), (0.975, 0.2), (0.025, 0.8), (0.975, 0.8)] {
        let r = H * 0.045
        let c = NSPoint(x: W * CGFloat(gx), y: H * CGFloat(gy))
        NSColor(calibratedWhite: 0.75, alpha: 1).setFill()
        NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)).fill()
        NSColor(calibratedWhite: 0.2, alpha: 1).setFill()
        NSBezierPath(ovalIn: NSRect(x: c.x - r * 0.5, y: c.y - r * 0.5, width: r, height: r)).fill()
    }
    NSGraphicsContext.restoreGraphicsState()
    let data = rep.representation(using: .png, properties: [:])!
    try! data.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
}

let red = NSColor(calibratedRed: 0.80, green: 0.10, blue: 0.09, alpha: 1)
make("BannerSummit", w: 1024, h: 128, bg: red, fg: .white, text: "SUMMIT", sub: nil, hazard: false, stripe: .white)
make("SignPark", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.95, green: 0.45, blue: 0.08, alpha: 1), fg: .white, text: "TERRAIN PARK", sub: "BOXES  ·  RAILS  ·  JUMP LINE", hazard: false, stripe: .white)
make("SignDanger", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.98, green: 0.80, blue: 0.10, alpha: 1), fg: NSColor(calibratedWhite: 0.08, alpha: 1), text: "DANGER", sub: "CREVASSES  ·  WILDLIFE", hazard: true, stripe: .black)
make("SignPipe", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.10, green: 0.38, blue: 0.80, alpha: 1), fg: .white, text: "HALFPIPE", sub: "STAY IN THE CENTRE", hazard: false, stripe: .white)
make("BannerFinish", w: 1024, h: 128, bg: NSColor(calibratedRed: 0.08, green: 0.30, blue: 0.70, alpha: 1), fg: .white, text: "FINISH", sub: nil, hazard: false, stripe: .white)
print("signs written to \(out)")
