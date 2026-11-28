// Renders the power-up artwork: the Mountain Zoom soda can's label and lid.
// Run: swift Tools/MakeCans.swift Assets/Summit/Resources/PowerUps
//
// Mountain Zoom is Summit's own made-up citrus soda: a loud, high-energy mountain soda, drawn here from scratch
// (no real brand's logo, wordmark, lettering, layout or colour scheme). Neon lime body glowing yellow at its heart;
// deep forest-green bands; its own lettering: MOUNTAIN ZOOM in chunky slanted capitals, each letter of ZOOM
// climbing a little higher than the last, with a white face, a red-orange block extrusion and a fat forest-green
// outline; a three-peak range with snow caps rising out of the bottom band; a red-orange lightning zip striking up
// through the peaks behind the name, with speed streaks; and the can's sweat: condensation beads and a few runs.
// Big, simple shapes and hard contrast, so it reads as "the green soda can" from far up the slope.
import AppKit
import CoreText

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

func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> NSColor { NSColor(calibratedRed: r, green: g, blue: b, alpha: a) }
let neon = rgb(0.46, 0.96, 0.04)     // the body: neon lime
let glowC = rgb(0.86, 1.00, 0.22)    // its yellow heart
let edgeC = rgb(0.22, 0.70, 0.04)    // toward the bands
let forest = rgb(0.02, 0.22, 0.08)   // the dark green: bands, outlines, the range
let ember = rgb(1.00, 0.34, 0.06)    // the red-orange accent: extrusion, the zip, pinstripes
let white = rgb(0.99, 1.00, 0.97)
let snow = rgb(0.93, 0.98, 1.00)

var ctx: CGContext { NSGraphicsContext.current!.cgContext }

/// A word as one path of glyph outlines (CoreText), each letter raised by `rise` x its index (a climbing word),
/// centred on the origin, sheared forward (`lean`) so it reads as going fast.
func wordPath(_ s: String, size: CGFloat, track: CGFloat, rise: CGFloat, lean: CGFloat, fit: CGFloat) -> CGPath {
    let f = font(size) as CTFont
    var chars = Array(s.utf16), glyphs = [CGGlyph](repeating: 0, count: chars.count)
    CTFontGetGlyphsForCharacters(f, &chars, &glyphs, chars.count)
    var adv = [CGSize](repeating: .zero, count: glyphs.count)
    CTFontGetAdvancesForGlyphs(f, .horizontal, glyphs, &adv, glyphs.count)
    let path = CGMutablePath()
    var x: CGFloat = 0
    for (i, g) in glyphs.enumerated() {
        if let gp = CTFontCreatePathForGlyph(f, g, nil) {
            path.addPath(gp, transform: CGAffineTransform(translationX: x, y: rise * CGFloat(i)))
        }
        x += adv[i].width + size * track
    }
    x -= size * track
    let b = path.boundingBoxOfPath
    var t = CGAffineTransform(a: 1, b: 0, c: lean, d: 1, tx: 0, ty: 0)
        .translatedBy(x: -x / 2, y: -(b.minY + b.maxY) / 2)
    let leaned = path.copy(using: &t)!
    // scaled to sit within `fit` wide (its height with it)
    let lb = leaned.boundingBoxOfPath
    let k = min(1, fit / lb.width)
    var s = CGAffineTransform(scaleX: k, y: k).translatedBy(x: -lb.midX, y: -lb.midY)
    return leaned.copy(using: &s)!
}

/// The lettering: a fat forest-green outline round the whole block, a red-orange extrusion down and to the right,
/// a white face with a cool shade toward its foot, and a thin green keyline.
func letters(_ p: CGPath, at c: CGPoint, depth: CGFloat, outline: CGFloat) {
    let c0 = ctx
    c0.saveGState()
    c0.translateBy(x: c.x, y: c.y)
    let steps = max(2, Int(depth / 2))
    func at(_ i: Int) -> CGAffineTransform { CGAffineTransform(translationX: depth * 0.55 * CGFloat(i) / CGFloat(steps), y: -depth * CGFloat(i) / CGFloat(steps)) }
    c0.setLineJoin(.round); c0.setLineCap(.round)
    // the outline, round the face and the extrusion both
    for i in 0...steps {
        var t = at(i); let q = p.copy(using: &t)!
        c0.addPath(q); c0.setStrokeColor(forest.cgColor); c0.setLineWidth(outline * 2); c0.strokePath()
        c0.addPath(q); c0.setFillColor(forest.cgColor); c0.fillPath()
    }
    // the extrusion
    for i in 1...steps {
        var t = at(i); let q = p.copy(using: &t)!
        let k = CGFloat(i) / CGFloat(steps)
        let col = rgb(1.0 - 0.25 * k, 0.34 - 0.14 * k, 0.06, 1)
        c0.addPath(q); c0.setStrokeColor(col.cgColor); c0.setLineWidth(outline * 0.5); c0.strokePath()
        c0.addPath(q); c0.setFillColor(col.cgColor); c0.fillPath()
    }
    // the face: white, a little cooler toward its foot
    c0.saveGState()
    c0.addPath(p); c0.clip()
    let b = p.boundingBoxOfPath
    let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: [rgb(0.80, 0.95, 0.72).cgColor, white.cgColor, white.cgColor] as CFArray, locations: [0, 0.45, 1])!
    c0.drawLinearGradient(grad, start: CGPoint(x: 0, y: b.minY), end: CGPoint(x: 0, y: b.maxY), options: [])
    c0.restoreGState()
    c0.addPath(p); c0.setStrokeColor(forest.cgColor); c0.setLineWidth(max(2, outline * 0.16)); c0.strokePath()
    c0.restoreGState()
}

/// Three snow-capped peaks rising out of the bottom band (the middle one tallest, leaning forward).
func range(cx: CGFloat, w: CGFloat, base: CGFloat, top: CGFloat) {
    let h = top - base
    let pts: [(CGFloat, CGFloat)] = [(-0.62, 0), (-0.36, 0.52), (-0.24, 0.38), (0.02, 1.0), (0.18, 0.66), (0.33, 0.78), (0.62, 0)]
    let ridge = NSBezierPath()
    ridge.move(to: NSPoint(x: cx + w * pts[0].0, y: base - 4))
    for p in pts { ridge.line(to: NSPoint(x: cx + w * p.0, y: base + h * p.1)) }
    ridge.line(to: NSPoint(x: cx + w * 0.62, y: base - 4)); ridge.close()
    forest.setFill(); ridge.fill()
    // snow caps: each peak's top third, with a bitten lower edge
    func cap(_ i: Int, _ frac: CGFloat) {
        let (px, py) = pts[i], (lx, ly) = pts[i - 1], (rx, ry) = pts[i + 1]
        let tip = NSPoint(x: cx + w * px, y: base + h * py)
        func along(_ ax: CGFloat, _ ay: CGFloat) -> NSPoint {
            let x = cx + w * ax, y = base + h * ay
            return NSPoint(x: tip.x + (x - tip.x) * frac, y: tip.y + (y - tip.y) * frac)
        }
        let l = along(lx, ly), r = along(rx, ry)
        let c = NSBezierPath()
        c.move(to: tip); c.line(to: r)
        let n = 4
        for k in 1..<n {
            let t = CGFloat(k) / CGFloat(n)
            let x = r.x + (l.x - r.x) * t, y = r.y + (l.y - r.y) * t
            c.line(to: NSPoint(x: x, y: y + (k % 2 == 0 ? 1 : -1) * h * 0.05))
        }
        c.line(to: l); c.close()
        snow.setFill(); c.fill()
    }
    cap(1, 0.3); cap(3, 0.34); cap(5, 0.32)
}

/// The lightning zip: a red-orange bolt with a white edge, striking from low left to high right, and speed streaks.
func zip(from a: CGPoint, to b: CGPoint, width wd: CGFloat) {
    let d = CGPoint(x: b.x - a.x, y: b.y - a.y)
    let len = sqrt(d.x * d.x + d.y * d.y)
    let u = CGPoint(x: d.x / len, y: d.y / len), n = CGPoint(x: -u.y, y: u.x)
    func p(_ t: CGFloat, _ s: CGFloat) -> CGPoint { CGPoint(x: a.x + d.x * t + n.x * s, y: a.y + d.y * t + n.y * s) }
    // a bolt in two strokes with a hard step between them, tapering to points at both ends
    let bolt = CGMutablePath()
    bolt.move(to: p(0, 0))
    bolt.addLine(to: p(0.56, wd * 0.9)); bolt.addLine(to: p(0.50, wd * 0.05))
    bolt.addLine(to: p(1.0, 0))
    bolt.addLine(to: p(0.44, -wd * 0.9)); bolt.addLine(to: p(0.50, -wd * 0.05))
    bolt.closeSubpath()
    let c0 = ctx
    c0.setLineJoin(.miter)
    c0.addPath(bolt); c0.setStrokeColor(forest.cgColor); c0.setLineWidth(wd * 0.34); c0.strokePath()
    c0.addPath(bolt); c0.setStrokeColor(white.cgColor); c0.setLineWidth(wd * 0.16); c0.strokePath()
    c0.addPath(bolt); c0.setFillColor(ember.cgColor); c0.fillPath()
    // speed streaks trailing off behind it
    c0.setLineCap(.round)
    for (k, s) in [(-1.0, 0.55), (0.0, 0.8), (1.0, 0.45)] {
        let off = CGFloat(k) * wd * 0.42, l = CGFloat(s) * len * 0.2
        let st = p(0.12, off), en = CGPoint(x: st.x - u.x * l, y: st.y - u.y * l)
        c0.move(to: st); c0.addLine(to: en)
        c0.setStrokeColor(forest.cgColor); c0.setLineWidth(wd * 0.22); c0.strokePath()
        c0.move(to: st); c0.addLine(to: en)
        c0.setStrokeColor(white.cgColor); c0.setLineWidth(wd * 0.09); c0.strokePath()
    }
}

/// Condensation: beads of water all over the can (a dark bottom edge, a bright top-left glint), and a few runs.
func sweat(_ W: CGFloat, _ H: CGFloat) {
    srand48(11)
    func bead(_ x: CGFloat, _ y: CGFloat, _ r: CGFloat) {
        let o = NSBezierPath(ovalIn: NSRect(x: x - r, y: y - r * 1.08, width: r * 2, height: r * 2.16))
        rgb(0.0, 0.18, 0.02, 0.16).setFill(); o.fill()
        let lo = NSBezierPath(ovalIn: NSRect(x: x - r * 0.8, y: y - r * 1.0, width: r * 1.6, height: r * 0.9))
        rgb(1, 1, 1, 0.22).setFill(); lo.fill()
        let g = NSBezierPath(ovalIn: NSRect(x: x - r * 0.55, y: y + r * 0.25, width: r * 0.55, height: r * 0.5))
        rgb(1, 1, 1, 0.85).setFill(); g.fill()
        o.lineWidth = max(1, r * 0.12); rgb(0.0, 0.15, 0.0, 0.25).setStroke(); o.stroke()
    }
    for _ in 0..<520 {
        let r = CGFloat(2.5 + pow(drand48(), 2.2) * 15)
        bead(CGFloat(drand48()) * W, CGFloat(drand48()) * H, r)
    }
    // runs: a drop that slid down, leaving a wet track
    for _ in 0..<9 {
        let x = CGFloat(drand48()) * W, y0 = H * CGFloat(0.5 + drand48() * 0.4), len = H * CGFloat(0.15 + drand48() * 0.3)
        let w = CGFloat(4 + drand48() * 4)
        let track = NSBezierPath()
        track.move(to: NSPoint(x: x, y: y0))
        var yy = y0, xx = x
        while yy > y0 - len { yy -= 12; xx += CGFloat(drand48() - 0.5) * 3; track.line(to: NSPoint(x: xx, y: yy)) }
        track.lineWidth = w; track.lineCapStyle = .round
        rgb(1, 1, 1, 0.16).setStroke(); track.stroke()
        track.lineWidth = w * 0.3; rgb(1, 1, 1, 0.35).setStroke(); track.stroke()
        bead(xx, yy, w * 1.5)
    }
}

// ---- the label: wraps once round the can, carrying the design twice so it reads from any side.
// Its shape matches the can's side (a turn of a 0.27 m can is 1.7 m round and 0.95 m tall), so each half is a
// little taller than it is wide and the name stacks: MOUNTAIN over a big ZOOM.
let label = canvas(2048, 1144) { W, H in
    // the neon body, glowing yellow-lime at its heart
    NSGradient(colors: [edgeC, neon, glowC, neon, edgeC], atLocations: [0, 0.22, 0.55, 0.82, 1], colorSpace: .deviceRGB)!
        .draw(in: NSRect(x: 0, y: 0, width: W, height: H), angle: 90)
    for k in 0..<2 {
        let cx = W * (0.25 + 0.5 * CGFloat(k))
        let rad = NSGradient(colors: [rgb(0.95, 1.0, 0.45, 0.75), rgb(0.86, 1.0, 0.22, 0)])!
        rad.draw(fromCenter: NSPoint(x: cx, y: H * 0.56), radius: 0, toCenter: NSPoint(x: cx, y: H * 0.56), radius: W * 0.24, options: [])
    }
    // the bands: forest green, an ember pinstripe inside each
    forest.setFill()
    NSRect(x: 0, y: 0, width: W, height: H * 0.075).fill()
    NSRect(x: 0, y: H * 0.905, width: W, height: H * 0.095).fill()
    ember.setFill()
    NSRect(x: 0, y: H * 0.885, width: W, height: H * 0.014).fill()
    let small: [NSAttributedString.Key: Any] = [.font: font(H * 0.05), .foregroundColor: white, .kern: H * 0.01]
    func centred(_ s: String, _ x: CGFloat, _ y: CGFloat) {
        let a = NSAttributedString(string: s, attributes: small)
        a.draw(at: NSPoint(x: x - a.size().width / 2, y: y))
    }
    for k in 0..<2 {
        let cx = W * (0.25 + 0.5 * CGFloat(k))
        centred("CITRUS  ·  EXTRA FIZZ", cx, H * 0.922)
        // the zip strikes up through the range, behind the name
        NSGraphicsContext.saveGraphicsState()
        NSRect(x: 0, y: H * 0.075, width: W, height: H * 0.81).clip()
        zip(from: CGPoint(x: cx - W * 0.13, y: H * 0.22), to: CGPoint(x: cx + W * 0.2, y: H * 0.87), width: H * 0.1)
        NSGraphicsContext.restoreGraphicsState()
        // the range, rising out of the bottom band
        range(cx: cx, w: W * 0.33, base: H * 0.075, top: H * 0.33)
        // the name
        letters(wordPath("MOUNTAIN", size: H * 0.15, track: 0.05, rise: 0, lean: 0.24, fit: W * 0.38), at: CGPoint(x: cx - W * 0.008, y: H * 0.7), depth: H * 0.02, outline: H * 0.014)
        letters(wordPath("ZOOM", size: H * 0.34, track: 0.0, rise: H * 0.03, lean: 0.26, fit: W * 0.38), at: CGPoint(x: cx + W * 0.004, y: H * 0.46), depth: H * 0.038, outline: H * 0.02)
    }
    // the can sweating in the sun
    sweat(W, H)
    for k in 0..<2 { centred("BUILT FOR THE STEEP STUFF", W * (0.25 + 0.5 * CGFloat(k)), H * 0.01) }
}
save(label, "MountainZoomLabel")

// ---- the lid: brushed aluminium, a rolled rim and the tab's rivet and mouth (the tab itself is a part, PowerUps.cs)
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
    rim.lineWidth = W * 0.05; NSColor(calibratedWhite: 0.9, alpha: 1).setStroke(); rim.stroke()
    let rim2 = NSBezierPath(ovalIn: NSRect(x: c.x - W * 0.43, y: c.y - H * 0.43, width: W * 0.86, height: H * 0.86))
    rim2.lineWidth = 2; NSColor(calibratedWhite: 0.45, alpha: 1).setStroke(); rim2.stroke()
    let rivet = NSBezierPath(ovalIn: NSRect(x: c.x - W * 0.035, y: c.y - H * 0.035, width: W * 0.07, height: H * 0.07))
    NSColor(calibratedWhite: 0.55, alpha: 1).setFill(); rivet.fill()
    let mouth = NSBezierPath(roundedRect: NSRect(x: c.x - W * 0.14, y: c.y - H * 0.34, width: W * 0.28, height: H * 0.2), xRadius: W * 0.08, yRadius: W * 0.08)
    mouth.lineWidth = 3; NSColor(calibratedWhite: 0.5, alpha: 1).setStroke(); mouth.stroke()
}
save(lid, "CanLid")
print("cans written to \(out)")
