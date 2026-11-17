// Renders the power-up artwork: the Mountain Zoom soda can's label and lid.
// Run: swift Tools/MakeCans.swift Assets/Summit/Resources/PowerUps
//
// Mountain Zoom is Summit's own made-up citrus soda. Its look is deliberately its own: a lime-to-
// lemon gradient, a black band top and bottom, a red-and-white zigzag peak badge with speed lines,
// and a clean, forward-leaning wordmark set in a plain condensed face (no broken or jagged lettering,
// no real brand's logo, colours or layout).
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

func font(_ size: CGFloat) -> NSFont {
    return NSFont(name: "Futura-CondensedExtraBold", size: size) ?? NSFont(name: "DINCondensed-Bold", size: size) ?? NSFont.boldSystemFont(ofSize: size)
}

// (fully opaque art, so the default premultiplied format is fine: AppKit cannot draw into a
// non-premultiplied bitmap, that format is only for writing raw pixels)
func canvas(_ w: Int, _ h: Int, _ draw: (CGFloat, CGFloat) -> Void) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw(CGFloat(w), CGFloat(h))
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

func save(_ rep: NSBitmapImageRep, _ name: String) {
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
}

let lime = NSColor(calibratedRed: 0.55, green: 0.93, blue: 0.10, alpha: 1)
let lemon = NSColor(calibratedRed: 0.93, green: 0.98, blue: 0.25, alpha: 1)
let deep = NSColor(calibratedRed: 0.10, green: 0.55, blue: 0.08, alpha: 1)
let ink = NSColor(calibratedWhite: 0.05, alpha: 1)
let red = NSColor(calibratedRed: 0.90, green: 0.12, blue: 0.10, alpha: 1)
let white = NSColor(calibratedWhite: 0.98, alpha: 1)

/// Text drawn slanted forward (a shear, so it leans like it is going fast), with an outline.
func slanted(_ s: String, size: CGFloat, fill: NSColor, stroke: NSColor?, strokeW: CGFloat, centre: NSPoint, kern: CGFloat = 0.02) {
    var a: [NSAttributedString.Key: Any] = [.font: font(size), .foregroundColor: fill, .kern: size * kern]
    let plain = NSAttributedString(string: s, attributes: a)
    let sz = plain.size()
    let ctx = NSGraphicsContext.current!.cgContext
    ctx.saveGState()
    ctx.translateBy(x: centre.x, y: centre.y)
    ctx.concatenate(CGAffineTransform(a: 1, b: 0, c: 0.28, d: 1, tx: 0, ty: 0))
    let at = NSPoint(x: -sz.width / 2, y: -sz.height / 2)
    if let stroke = stroke {
        // a fat outline under the fill (a negative stroke width would put the stroke over the fill)
        a[.strokeColor] = stroke; a[.strokeWidth] = strokeW
        a[.foregroundColor] = stroke
        NSAttributedString(string: s, attributes: a).draw(at: at)
    }
    plain.draw(at: at)
    ctx.restoreGState()
}

/// The badge: a zigzag peak with a snow cap, inside a red disc, with speed lines streaming off to the left.
func badge(centre c: NSPoint, r: CGFloat) {
    // speed lines behind it
    for (i, len) in [1.6, 2.1, 1.4].enumerated() {
        let y = c.y + r * (0.35 - 0.35 * CGFloat(i))
        let path = NSBezierPath()
        path.move(to: NSPoint(x: c.x - r * 0.6, y: y))
        path.line(to: NSPoint(x: c.x - r * CGFloat(len), y: y))
        path.lineWidth = r * 0.16; path.lineCapStyle = .round
        ink.setStroke(); path.stroke()
    }
    let disc = NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
    ink.setFill(); disc.fill()
    let inner = NSBezierPath(ovalIn: NSRect(x: c.x - r * 0.86, y: c.y - r * 0.86, width: r * 1.72, height: r * 1.72))
    red.setFill(); inner.fill()
    // two peaks, the tall one leaning forward, clipped to the disc
    NSGraphicsContext.saveGraphicsState()
    inner.addClip()
    let base = c.y - r * 0.55
    let peak = NSBezierPath()
    peak.move(to: NSPoint(x: c.x - r, y: base))
    peak.line(to: NSPoint(x: c.x - r * 0.38, y: c.y + r * 0.12))
    peak.line(to: NSPoint(x: c.x - r * 0.12, y: c.y - r * 0.12))
    peak.line(to: NSPoint(x: c.x + r * 0.22, y: c.y + r * 0.62))
    peak.line(to: NSPoint(x: c.x + r, y: base))
    peak.close()
    ink.setFill(); peak.fill()
    let cap = NSBezierPath()
    cap.move(to: NSPoint(x: c.x + r * 0.22, y: c.y + r * 0.62))
    cap.line(to: NSPoint(x: c.x + r * 0.42, y: c.y + r * 0.22))
    cap.line(to: NSPoint(x: c.x + r * 0.30, y: c.y + r * 0.28))
    cap.line(to: NSPoint(x: c.x + r * 0.18, y: c.y + r * 0.16))
    cap.line(to: NSPoint(x: c.x + r * 0.06, y: c.y + r * 0.28))
    cap.close()
    white.setFill(); cap.fill()
    // the snow below
    white.setFill(); NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: base - (c.y - r)).fill()
    NSGraphicsContext.restoreGraphicsState()
}

// ---- the label: wraps once round the can, carrying the logo twice so it reads from any side.
// Its shape matches the can's side (a turn of a 0.27 m can is 1.7 m round and 0.95 m tall),
// so each half is a little taller than it is wide and the logo stacks: badge, MOUNTAIN, ZOOM.
let label = canvas(2048, 1144) { W, H in
    // citrus gradient body
    NSGradient(colors: [deep, lime, lemon, lime, deep], atLocations: [0, 0.25, 0.5, 0.75, 1], colorSpace: .deviceRGB)!
        .draw(in: NSRect(x: 0, y: 0, width: W, height: H), angle: 90)
    // fine diagonal fizz lines
    for i in stride(from: -Int(H), to: Int(W), by: 22) {
        let p = NSBezierPath()
        p.move(to: NSPoint(x: CGFloat(i), y: 0)); p.line(to: NSPoint(x: CGFloat(i) + H * 0.5, y: H))
        p.lineWidth = 3; NSColor(calibratedWhite: 1, alpha: 0.06).setStroke(); p.stroke()
    }
    // bubbles
    srand48(7)
    for _ in 0..<160 {
        let r = CGFloat(3 + drand48() * 10), x = CGFloat(drand48()) * W, y = H * CGFloat(0.16 + drand48() * 0.68)
        let b = NSBezierPath(ovalIn: NSRect(x: x - r, y: y - r, width: r * 2, height: r * 2))
        b.lineWidth = 2; NSColor(calibratedWhite: 1, alpha: 0.35).setStroke(); b.stroke()
    }
    // black bands top and bottom, a thin red pinstripe inside each
    ink.setFill()
    NSRect(x: 0, y: 0, width: W, height: H * 0.085).fill()
    NSRect(x: 0, y: H * 0.915, width: W, height: H * 0.085).fill()
    red.setFill()
    NSRect(x: 0, y: H * 0.085, width: W, height: H * 0.012).fill()
    NSRect(x: 0, y: H * 0.903, width: W, height: H * 0.012).fill()
    let small: [NSAttributedString.Key: Any] = [.font: font(H * 0.045), .foregroundColor: white, .kern: H * 0.008]
    func centred(_ s: String, _ x: CGFloat, _ y: CGFloat) {
        let a = NSAttributedString(string: s, attributes: small)
        a.draw(at: NSPoint(x: x - a.size().width / 2, y: y))
    }
    // the logo, twice, each with its own lines of small print in the bands
    for k in 0..<2 {
        let cx = W * (0.25 + 0.5 * CGFloat(k))
        centred("CITRUS SODA  ·  EXTRA FIZZ", cx, H * 0.93)
        centred("BUILT FOR THE STEEP STUFF", cx, H * 0.015)
        badge(centre: NSPoint(x: cx + W * 0.02, y: H * 0.67), r: H * 0.14)
        slanted("MOUNTAIN", size: H * 0.115, fill: red, stroke: ink, strokeW: 10, centre: NSPoint(x: cx, y: H * 0.45), kern: 0.08)
        slanted("ZOOM", size: H * 0.23, fill: white, stroke: ink, strokeW: 12, centre: NSPoint(x: cx, y: H * 0.255), kern: 0.03)
    }
    // the printed can's soft vertical sheen
    for i in 0..<16 {
        NSColor(calibratedWhite: 1, alpha: i % 4 == 0 ? 0.05 : 0.0).setFill()
        NSRect(x: W * CGFloat(i) / 16, y: 0, width: W / 40, height: H).fill()
    }
}
save(label, "MountainZoomLabel")

// ---- the lid: brushed aluminium, a rim and a ring-pull
let lid = canvas(256, 256) { W, H in
    let c = NSPoint(x: W / 2, y: H / 2)
    NSColor(calibratedWhite: 0.62, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    srand48(3)
    for r in stride(from: CGFloat(2), to: W / 2, by: 2) {
        let g = 0.70 + 0.12 * CGFloat(drand48())
        let p = NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        p.lineWidth = 1.2; NSColor(calibratedWhite: g, alpha: 1).setStroke(); p.stroke()
    }
    let rim = NSBezierPath(ovalIn: NSRect(x: c.x - W * 0.47, y: c.y - H * 0.47, width: W * 0.94, height: H * 0.94))
    rim.lineWidth = W * 0.05; NSColor(calibratedWhite: 0.88, alpha: 1).setStroke(); rim.stroke()
    // the ring-pull
    let tab = NSBezierPath(roundedRect: NSRect(x: c.x - W * 0.11, y: c.y - H * 0.02, width: W * 0.22, height: H * 0.34), xRadius: W * 0.1, yRadius: W * 0.1)
    NSColor(calibratedWhite: 0.80, alpha: 1).setFill(); tab.fill()
    let hole = NSBezierPath(roundedRect: NSRect(x: c.x - W * 0.06, y: c.y + H * 0.14, width: W * 0.12, height: H * 0.12), xRadius: W * 0.05, yRadius: W * 0.05)
    NSColor(calibratedWhite: 0.35, alpha: 1).setFill(); hole.fill()
    let rivet = NSBezierPath(ovalIn: NSRect(x: c.x - W * 0.035, y: c.y - H * 0.035, width: W * 0.07, height: H * 0.07))
    NSColor(calibratedWhite: 0.55, alpha: 1).setFill(); rivet.fill()
    let mouth = NSBezierPath(roundedRect: NSRect(x: c.x - W * 0.14, y: c.y - H * 0.34, width: W * 0.28, height: H * 0.2), xRadius: W * 0.08, yRadius: W * 0.08)
    mouth.lineWidth = 3; NSColor(calibratedWhite: 0.5, alpha: 1).setStroke(); mouth.stroke()
}
save(lid, "CanLid")
print("cans written to \(out)")
