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
make("SignPark", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.95, green: 0.45, blue: 0.08, alpha: 1), fg: .white, text: "TERRAIN PARK", sub: "BOXES  ·  RAILS  ·  JUMP LINE", hazard: false, stripe: .white)
make("SignDanger", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.98, green: 0.80, blue: 0.10, alpha: 1), fg: NSColor(calibratedWhite: 0.08, alpha: 1), text: "DANGER", sub: "CREVASSES  ·  WILDLIFE", hazard: true, stripe: .black)
make("SignPipe", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.10, green: 0.38, blue: 0.80, alpha: 1), fg: .white, text: "HALFPIPE", sub: "STAY IN THE CENTRE", hazard: false, stripe: .white)
make("SignRidge", w: 1024, h: 256, bg: NSColor(calibratedWhite: 0.07, alpha: 1), fg: .white, text: "◆◆  RIDGE", sub: "EXPERTS ONLY  ·  CLIFFS  ·  STAY ON THE CREST", hazard: false, stripe: NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1))
make("SignGorge", w: 1024, h: 256, bg: NSColor(calibratedWhite: 0.07, alpha: 1), fg: .white, text: "◆◆  GORGE", sub: "ROCK WALLS  ·  STAY IN THE MIDDLE", hazard: false, stripe: NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1))
make("SignLedge", w: 1024, h: 256, bg: NSColor(calibratedWhite: 0.07, alpha: 1), fg: .white, text: "◆◆  LEDGE", sub: "CLIFF ONE SIDE  ·  WALL THE OTHER", hazard: false, stripe: NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1))
make("SignLedges", w: 1024, h: 256, bg: NSColor(calibratedWhite: 0.07, alpha: 1), fg: .white, text: "◆◆  LEDGES", sub: "OPTIONAL DROPS  ·  LAND ON THE SLOPE", hazard: false, stripe: NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1))
make("SignBand", w: 1024, h: 256, bg: NSColor(calibratedWhite: 0.07, alpha: 1), fg: .white, text: "◆◆  CLIFF BAND", sub: "TRAVERSE  ·  STAY HIGH  ·  DROP THE GAP", hazard: false, stripe: NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1))
make("SignJump", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.95, green: 0.45, blue: 0.08, alpha: 1), fg: .white, text: "CLIFF JUMP", sub: "SPEED UP  ·  SEND IT  ·  LAND ON THE SLOPE", hazard: false, stripe: .white)
make("SignCliffs", w: 1024, h: 256, bg: red, fg: .white, text: "DANGER  ·  CLIFFS", sub: "SHEER DROP  ·  STAY ON THE PISTE", hazard: true, stripe: .black)
make("SignFreeride", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.11, green: 0.42, blue: 0.24, alpha: 1), fg: .white, text: "FREERIDE LINE", sub: "ROLLERS  ·  DROPS  ·  LOGS  ·  KEEP YOUR SPEED", hazard: false, stripe: .white)
make("SignJumpLine", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.10, green: 0.32, blue: 0.78, alpha: 1), fg: .white, text: "JUMP LINE", sub: "KICKERS  ·  GAPS  ·  RAILS  ·  KEEP YOUR SPEED", hazard: false, stripe: .white)
make("BannerFinish", w: 1024, h: 128, bg: NSColor(calibratedRed: 0.08, green: 0.30, blue: 0.70, alpha: 1), fg: .white, text: "FINISH", sub: nil, hazard: false, stripe: .white)

// ---- the in-run warning signs: big safety-style panels (3:2) on posts before the lip
func panel(_ name: String, w: Int, h: Int, _ draw: (CGFloat, CGFloat) -> Void) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let W = CGFloat(w), H = CGFloat(h)
    draw(W, H)
    // weathered print: fine speckle, a few scuffs
    srand48(Int(w * 17 + h * 3 + name.count))
    for _ in 0..<(w * h / 5) {
        NSColor(calibratedWhite: drand48() < 0.5 ? 0 : 1, alpha: 0.05).setFill()
        NSRect(x: CGFloat(drand48()) * W, y: CGFloat(drand48()) * H, width: 1.5, height: 1.5).fill()
    }
    for _ in 0..<14 {
        NSColor(calibratedWhite: 1, alpha: 0.10).setFill()
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H
        NSRect(x: x, y: y, width: CGFloat(4 + drand48() * 30), height: 2).fill()
    }
    // bolts
    for (gx, gy) in [(0.05, 0.07), (0.95, 0.07), (0.05, 0.93), (0.95, 0.93)] {
        let r = H * 0.022, c = NSPoint(x: W * CGFloat(gx), y: H * CGFloat(gy))
        NSColor(calibratedWhite: 0.7, alpha: 1).setFill()
        NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)).fill()
    }
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
}
func text(_ t: String, _ size: CGFloat, _ c: NSColor, in r: NSRect, kern: CGFloat = 0.04) {
    let para = NSMutableParagraphStyle(); para.alignment = .center
    let a: [NSAttributedString.Key: Any] = [.font: font(size), .foregroundColor: c, .paragraphStyle: para, .kern: size * kern]
    let s = NSAttributedString(string: t, attributes: a)
    let sz = s.size()
    s.draw(in: NSRect(x: r.minX, y: r.midY - sz.height / 2 - size * 0.06, width: r.width, height: sz.height))
}
let black = NSColor(calibratedWhite: 0.06, alpha: 1), white = NSColor(calibratedWhite: 0.97, alpha: 1)

panel("SignTurnAround", w: 768, h: 512) { W, H in
    NSColor(calibratedRed: 0.84, green: 0.07, blue: 0.06, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    let b = NSBezierPath(roundedRect: NSRect(x: W * 0.03, y: H * 0.045, width: W * 0.94, height: H * 0.91), xRadius: H * 0.05, yRadius: H * 0.05)
    b.lineWidth = H * 0.025; white.setStroke(); b.stroke()
    text("TURN", H * 0.36, white, in: NSRect(x: 0, y: H * 0.56, width: W, height: H * 0.34))
    text("AROUND", H * 0.36, white, in: NSRect(x: 0, y: H * 0.24, width: W, height: H * 0.34))
    text("SERIOUSLY.", H * 0.1, white, in: NSRect(x: 0, y: H * 0.08, width: W, height: H * 0.14), kern: 0.2)
}

panel("SignWarning", w: 768, h: 512) { W, H in
    white.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    let orange = NSColor(calibratedRed: 1.0, green: 0.52, blue: 0.0, alpha: 1)
    orange.setFill(); NSRect(x: 0, y: H * 0.64, width: W, height: H * 0.36).fill()
    // the warning triangle
    let cx = W * 0.2, cy = H * 0.82, s = H * 0.25
    let tri = NSBezierPath(); tri.move(to: NSPoint(x: cx - s * 0.6, y: cy - s * 0.48)); tri.line(to: NSPoint(x: cx + s * 0.6, y: cy - s * 0.48))
    tri.line(to: NSPoint(x: cx, y: cy + s * 0.55)); tri.close(); tri.lineJoinStyle = .round; tri.lineWidth = H * 0.025
    black.setFill(); tri.fill(); black.setStroke(); tri.stroke()
    text("!", s * 0.8, orange, in: NSRect(x: cx - s, y: cy - s * 0.42, width: s * 2, height: s * 0.8))
    text("WARNING", H * 0.25, black, in: NSRect(x: W * 0.3, y: H * 0.64, width: W * 0.66, height: H * 0.36))
    text("STEEP DROP", H * 0.22, black, in: NSRect(x: 0, y: H * 0.34, width: W, height: H * 0.26))
    text("AHEAD", H * 0.22, black, in: NSRect(x: 0, y: H * 0.08, width: W, height: H * 0.26))
    let b = NSBezierPath(rect: NSRect(x: 0, y: 0, width: W, height: H)); b.lineWidth = H * 0.03; black.setStroke(); b.stroke()
}

panel("SignDangerStart", w: 768, h: 512) { W, H in
    white.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    black.setFill(); NSRect(x: 0, y: H * 0.6, width: W, height: H * 0.4).fill()
    let red = NSColor(calibratedRed: 0.86, green: 0.06, blue: 0.05, alpha: 1)
    let oval = NSBezierPath(ovalIn: NSRect(x: W * 0.12, y: H * 0.645, width: W * 0.76, height: H * 0.31))
    red.setFill(); oval.fill(); oval.lineWidth = H * 0.018; white.setStroke(); oval.stroke()
    text("DANGER", H * 0.24, white, in: NSRect(x: 0, y: H * 0.645, width: W, height: H * 0.31), kern: 0.08)
    text("CLIFFS · BEARS · DEATH ·", H * 0.135, black, in: NSRect(x: 0, y: H * 0.33, width: W, height: H * 0.24))
    text("BAD DECISIONS", H * 0.17, red, in: NSRect(x: 0, y: H * 0.09, width: W, height: H * 0.24))
    let b = NSBezierPath(rect: NSRect(x: 0, y: 0, width: W, height: H)); b.lineWidth = H * 0.03; black.setStroke(); b.stroke()
}

panel("SignRemote", w: 768, h: 512) { W, H in
    white.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    let blue = NSColor(calibratedRed: 0.05, green: 0.30, blue: 0.68, alpha: 1)
    blue.setFill(); NSRect(x: 0, y: H * 0.64, width: W, height: H * 0.36).fill()
    let it = NSFontManager.shared.convert(font(H * 0.25), toHaveTrait: .italicFontMask)
    let para = NSMutableParagraphStyle(); para.alignment = .center
    let ns = NSAttributedString(string: "NOTICE", attributes: [.font: it, .foregroundColor: white, .paragraphStyle: para, .kern: H * 0.02])
    let sz = ns.size(); ns.draw(in: NSRect(x: 0, y: H * 0.82 - sz.height / 2 - H * 0.015, width: W, height: sz.height))
    text("REMOTE AREA", H * 0.22, black, in: NSRect(x: 0, y: H * 0.35, width: W, height: H * 0.26))
    text("NO SIGNAL · NO PATROL · NO HELP", H * 0.11, blue, in: NSRect(x: 0, y: H * 0.12, width: W, height: H * 0.18))
    let b = NSBezierPath(rect: NSRect(x: 0, y: 0, width: W, height: H)); b.lineWidth = H * 0.03; black.setStroke(); b.stroke()
}

// caution tape: tiles along its length (a repeat every 1024 px), the phrase twice with a cross between
do {
    let w = 1024, h = 64
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let W = CGFloat(w), H = CGFloat(h)
    NSColor(calibratedRed: 1.0, green: 0.83, blue: 0.0, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    black.setFill(); NSRect(x: 0, y: 0, width: W, height: H * 0.09).fill(); NSRect(x: 0, y: H * 0.91, width: W, height: H * 0.09).fill()
    for cx in [W * 0.25, W * 0.75] { text("POINT OF NO RETURN", H * 0.62, black, in: NSRect(x: cx - W * 0.25, y: H * 0.1, width: W * 0.5, height: H * 0.8), kern: 0.08) }
    for cx in [CGFloat(0), W * 0.5, W] { text("✕", H * 0.55, black, in: NSRect(x: cx - 40, y: H * 0.1, width: 80, height: H * 0.8)) }
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/Tape.png"))
}
print("signs written to \(out)")

// the lifts' ends (LiftEnds.cs): each lift's name at its load and unload, the singles lane, the way to the camp
let liftBlue = NSColor(calibratedRed: 0.07, green: 0.20, blue: 0.42, alpha: 1)
make("LiftSummit", w: 1024, h: 256, bg: liftBlue, fg: .white, text: "SUMMIT EXPRESS", sub: "TO THE CAMP  ·  MT. VERTIGO SUMMIT", hazard: false, stripe: red)
make("LiftPark", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.95, green: 0.45, blue: 0.08, alpha: 1), fg: .white, text: "PARK LIFT", sub: "TERRAIN PARK LAPS  ·  RAILS  ·  JUMP LINE", hazard: false, stripe: .white)
make("LiftChair", w: 1024, h: 256, bg: liftBlue, fg: .white, text: "QUAD CHAIR", sub: "LOAD HERE  ·  GROUPS OF FOUR  ·  BAR DOWN", hazard: false, stripe: red)
make("LiftSingles", w: 512, h: 256, bg: NSColor(calibratedRed: 0.98, green: 0.80, blue: 0.10, alpha: 1), fg: NSColor(calibratedWhite: 0.08, alpha: 1), text: "SINGLES", sub: "FILL EVERY CHAIR", hazard: false, stripe: .black)
make("LiftCamp", w: 1024, h: 256, bg: NSColor(calibratedRed: 0.13, green: 0.30, blue: 0.16, alpha: 1), fg: .white, text: "CAMP  ›", sub: "LODGE  ·  LOOKOUT  ·  DROP IN", hazard: false, stripe: .white)
make("LiftUnload", w: 1024, h: 256, bg: liftBlue, fg: .white, text: "UNLOAD  ›  RUNS", sub: "STRAP IN CLEAR OF THE RAMP", hazard: false, stripe: red)

// the trail map board: the mountain drawn as a print, its runs in their colours and the lifts in red
func makeMap(_ name: String, w: Int, h: Int) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let W = CGFloat(w), H = CGFloat(h)
    // sky and the mountain's white mass, its ridges shaded
    NSColor(calibratedRed: 0.56, green: 0.72, blue: 0.90, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    let m = NSBezierPath()
    m.move(to: NSPoint(x: 0, y: H * 0.08)); m.line(to: NSPoint(x: W * 0.18, y: H * 0.55)); m.line(to: NSPoint(x: W * 0.36, y: H * 0.70))
    m.line(to: NSPoint(x: W * 0.52, y: H * 0.90)); m.line(to: NSPoint(x: W * 0.66, y: H * 0.72)); m.line(to: NSPoint(x: W * 0.84, y: H * 0.60))
    m.line(to: NSPoint(x: W, y: H * 0.30)); m.line(to: NSPoint(x: W, y: 0)); m.line(to: NSPoint(x: 0, y: 0)); m.close()
    NSColor(calibratedWhite: 0.97, alpha: 1).setFill(); m.fill()
    NSColor(calibratedRed: 0.78, green: 0.84, blue: 0.92, alpha: 1).setStroke()
    for (x0, y0, x1, y1) in [(0.52, 0.90, 0.40, 0.30), (0.52, 0.90, 0.70, 0.25), (0.36, 0.70, 0.22, 0.20), (0.66, 0.72, 0.86, 0.18)] {
        let p = NSBezierPath(); p.lineWidth = W * 0.006
        p.move(to: NSPoint(x: W * CGFloat(x0), y: H * CGFloat(y0))); p.line(to: NSPoint(x: W * CGFloat(x1), y: H * CGFloat(y1))); p.stroke()
    }
    // the forest below the tree line
    srand48(4242)
    for _ in 0..<900 {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H * 0.32
        if abs(x - W * 0.52) < W * 0.05 + y * 0.2 { continue }
        NSColor(calibratedRed: 0.12, green: 0.30 + CGFloat(drand48()) * 0.1, blue: 0.18, alpha: 0.9).setFill()
        let t = NSBezierPath(); t.move(to: NSPoint(x: x, y: y + H * 0.025)); t.line(to: NSPoint(x: x - W * 0.006, y: y)); t.line(to: NSPoint(x: x + W * 0.006, y: y)); t.close(); t.fill()
    }
    // the run: down the middle, winding, its stretches in their colours
    func run(_ pts: [(CGFloat, CGFloat)], _ c: NSColor, _ wide: CGFloat) {
        let p = NSBezierPath(); p.lineWidth = W * wide; p.lineCapStyle = .round; p.lineJoinStyle = .round
        p.move(to: NSPoint(x: W * pts[0].0, y: H * pts[0].1))
        for q in pts.dropFirst() { p.line(to: NSPoint(x: W * q.0, y: H * q.1)) }
        c.setStroke(); p.stroke()
    }
    let green = NSColor(calibratedRed: 0.05, green: 0.55, blue: 0.22, alpha: 1), blue = NSColor(calibratedRed: 0.05, green: 0.30, blue: 0.75, alpha: 1)
    let black = NSColor(calibratedWhite: 0.05, alpha: 1)
    run([(0.52, 0.86), (0.50, 0.74), (0.53, 0.64)], black, 0.010)
    run([(0.53, 0.64), (0.49, 0.52), (0.52, 0.44)], blue, 0.010)
    run([(0.52, 0.44), (0.47, 0.34), (0.51, 0.24)], black, 0.010)
    run([(0.51, 0.24), (0.48, 0.14), (0.50, 0.04)], green, 0.010)
    run([(0.53, 0.64), (0.62, 0.52), (0.60, 0.40)], blue, 0.006)
    run([(0.50, 0.74), (0.38, 0.58), (0.40, 0.46)], black, 0.006)
    // the lifts: straight red lines, a dot at each station
    func lift(_ x0: CGFloat, _ y0: CGFloat, _ x1: CGFloat, _ y1: CGFloat) {
        let p = NSBezierPath(); p.lineWidth = W * 0.005
        p.move(to: NSPoint(x: W * x0, y: H * y0)); p.line(to: NSPoint(x: W * x1, y: H * y1))
        NSColor(calibratedRed: 0.85, green: 0.10, blue: 0.08, alpha: 1).setStroke(); p.stroke()
        for (x, y) in [(x0, y0), (x1, y1)] {
            NSColor(calibratedRed: 0.85, green: 0.10, blue: 0.08, alpha: 1).setFill()
            NSBezierPath(ovalIn: NSRect(x: W * x - W * 0.010, y: H * y - W * 0.010, width: W * 0.02, height: W * 0.02)).fill()
        }
    }
    lift(0.45, 0.86, 0.44, 0.60); lift(0.58, 0.58, 0.58, 0.40); lift(0.44, 0.40, 0.45, 0.20); lift(0.56, 0.20, 0.56, 0.05)
    // the title band
    NSColor(calibratedRed: 0.07, green: 0.20, blue: 0.42, alpha: 1).setFill(); NSRect(x: 0, y: H * 0.88, width: W, height: H * 0.12).fill()
    let para = NSMutableParagraphStyle(); para.alignment = .center
    NSAttributedString(string: "MT. VERTIGO  ·  TRAIL MAP", attributes: [.font: font(H * 0.08), .foregroundColor: NSColor.white, .paragraphStyle: para, .kern: H * 0.01])
        .draw(in: NSRect(x: 0, y: H * 0.895, width: W, height: H * 0.1))
    // the key
    let key: [(String, NSColor)] = [("●  EASIER", green), ("■  MORE DIFFICULT", blue), ("◆  EXPERT", black), ("—  LIFTS", NSColor(calibratedRed: 0.85, green: 0.10, blue: 0.08, alpha: 1))]
    for (i, (t, c)) in key.enumerated() {
        NSAttributedString(string: t, attributes: [.font: font(H * 0.045), .foregroundColor: c]).draw(at: NSPoint(x: W * 0.03, y: H * (0.80 - 0.06 * CGFloat(i))))
    }
    NSColor(calibratedWhite: 0.15, alpha: 1).setStroke(); let frame = NSBezierPath(rect: NSRect(x: 2, y: 2, width: W - 4, height: H - 4)); frame.lineWidth = 4; frame.stroke()
    NSGraphicsContext.restoreGraphicsState()
    let data = rep.representation(using: .png, properties: [:])!
    try! data.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
}
makeMap("TrailMap", w: 1024, h: 640)
