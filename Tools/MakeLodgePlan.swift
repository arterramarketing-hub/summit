// Draws the lodge's floor plan with the three ways to lay out its inside (lodge.md; A was chosen and built) to
// Tools/art/lodge/LodgePlan*.png.
// Run: swiftc -o /tmp/lodgeplan Tools/MakeLodgePlan.swift && /tmp/lodgeplan Tools/art/lodge
// The plan is the built shell (Tools/MakeLodge.py): the hall 22 x 14 m, the wing 5 x 10.5 m, the terrace and its
// steps, the doors, the glulam columns, the tall volume under the clerestory. Drawn as seen from above with the front
// (the terrace, the fire, the gate) at the bottom, so the low end (the photo's left) is on the left.
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let S: CGFloat = 30          // pixels a metre
let W = 1240, H = 860
let ox: CGFloat = 575, oy: CGFloat = 530   // where the hall's middle lands (x flipped: +x, the low end, to the left)

func P(_ x: CGFloat, _ z: CGFloat) -> NSPoint { NSPoint(x: ox - x * S, y: oy - z * S) }
func R(_ x0: CGFloat, _ z0: CGFloat, _ x1: CGFloat, _ z1: CGFloat) -> NSRect {
    let a = P(x0, z0), b = P(x1, z1)
    return NSRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
}
func font(_ s: CGFloat, bold: Bool = false) -> NSFont { bold ? NSFont.boldSystemFont(ofSize: s) : NSFont.systemFont(ofSize: s) }
func text(_ t: String, _ at: NSPoint, _ size: CGFloat, _ col: NSColor = .black, bold: Bool = false, centre: Bool = true) {
    let a: [NSAttributedString.Key: Any] = [.font: font(size, bold: bold), .foregroundColor: col]
    let lines = t.components(separatedBy: "\n")
    var y = at.y + CGFloat(lines.count - 1) * size * 0.6
    for l in lines {
        let s = NSAttributedString(string: l, attributes: a)
        let w = s.size().width
        s.draw(at: NSPoint(x: centre ? at.x - w / 2 : at.x, y: y - size * 0.6))
        y -= size * 1.2
    }
}
func fill(_ r: NSRect, _ c: NSColor) { c.setFill(); r.fill() }
func stroke(_ r: NSRect, _ c: NSColor, _ w: CGFloat = 2) { c.setStroke(); let p = NSBezierPath(rect: r); p.lineWidth = w; p.stroke() }

struct Zone { let name: String; let x0, z0, x1, z1: CGFloat; let col: NSColor; let note: String }

let ink = NSColor(calibratedWhite: 0.12, alpha: 1)
let glassC = NSColor(calibratedRed: 0.35, green: 0.62, blue: 0.85, alpha: 1)

func shell() {
    // the terrace and its steps
    fill(R(-17.2, 7, 14, 12.6), NSColor(calibratedWhite: 0.86, alpha: 1))
    fill(R(-17.2, 5, -11, 7), NSColor(calibratedWhite: 0.86, alpha: 1))
    fill(R(11, -7, 14, 7), NSColor(calibratedWhite: 0.86, alpha: 1))
    for (x0, x1) in [(-5.2, 4.8), (-15.2, -11.6)] as [(CGFloat, CGFloat)] {
        for k in 0..<6 { fill(R(x0, 12.6 + CGFloat(k) * 0.36, x1, 12.6 + CGFloat(k + 1) * 0.36), NSColor(calibratedWhite: k % 2 == 0 ? 0.78 : 0.82, alpha: 1)) }
        text("steps down", P((x0 + x1) / 2, 13.9), 12, NSColor(calibratedWhite: 0.35, alpha: 1))
    }
    for (x0, x1) in [(-17.1, -15.4), (-11.4, -5.4), (5.2, 9.4), (9.6, 13.9)] as [(CGFloat, CGFloat)] {
        fill(R(x0, 11.3, x1, 12.6), NSColor(calibratedRed: 0.86, green: 0.9, blue: 0.95, alpha: 1)); stroke(R(x0, 11.3, x1, 12.6), NSColor(calibratedWhite: 0.6, alpha: 1), 1)
    }
    text("TERRACE", P(-3.5, 9.6), 13, NSColor(calibratedWhite: 0.4, alpha: 1), bold: true)
    text("fire table", P(-8.8, 10.4), 11, NSColor(calibratedWhite: 0.4, alpha: 1))
    text("racks,\npicnic\ntables\n(under\nthe roof)", P(12.5, 1.5), 11, NSColor(calibratedWhite: 0.4, alpha: 1))
    // the tall volume (10 m), the clerestory along its front
    fill(R(-11, -7, 5, 0), NSColor(calibratedRed: 0.97, green: 0.95, blue: 0.9, alpha: 1))
    fill(R(5, -7, 11, 7), NSColor(calibratedRed: 0.99, green: 0.98, blue: 0.96, alpha: 1))
    fill(R(-11, 0, 5, 7), NSColor(calibratedRed: 0.99, green: 0.98, blue: 0.96, alpha: 1))
    fill(R(-16, -5.5, -11, 5), NSColor(calibratedRed: 0.99, green: 0.98, blue: 0.96, alpha: 1))
}

func walls() {
    ink.setStroke()
    let wall = NSBezierPath(); wall.lineWidth = 6
    // the hall's walls, gaps at the doors and the wing's opening
    func seg(_ a: (CGFloat, CGFloat), _ b: (CGFloat, CGFloat), _ w: CGFloat = 6, _ c: NSColor = ink) {
        let p = NSBezierPath(); p.lineWidth = w; p.move(to: P(a.0, a.1)); p.line(to: P(b.0, b.1)); c.setStroke(); p.stroke()
    }
    seg((-11, -7), (11, -7)); seg((11, -7), (11, 3.0)); seg((11, 4.9), (11, 7))
    seg((11, 7), (8.6, 7)); seg((8.6, 7), (5.9, 7), 4, glassC); seg((3.1, 7), (-11, 7), 4, glassC)
    seg((-11, 7), (-11, 4.1)); seg((-11, -4.6), (-11, -7))
    seg((-11, 5), (-16, 5), 4, glassC); seg((-16, 5), (-16, -5.5)); seg((-16, -5.5), (-11, -5.5))
    seg((-11, -7), (3.8, -7), 3, glassC)   // (the high window band over the back: drawn as glass)
    // the clerestory line (glass above, columns below)
    let d = NSBezierPath(); d.lineWidth = 1.5; d.setLineDash([6, 5], count: 2, phase: 0)
    d.move(to: P(-11, 0)); d.line(to: P(5, 0)); d.line(to: P(5, -7)); NSColor(calibratedWhite: 0.45, alpha: 1).setStroke(); d.stroke()
    text("tall volume, 10 m high (clerestory over its front)", P(-3, -0.5), 11, NSColor(calibratedWhite: 0.45, alpha: 1))
    text("ceiling 3.2 m → 10 m (rising →)", P(0, 6.2), 11, NSColor(calibratedWhite: 0.45, alpha: 1))
    // glulam columns
    for x in [-6.37, -1.73, 2.9, 8.6] as [CGFloat] {
        for z in [0.0, 6.4] as [CGFloat] { fill(NSRect(x: P(x, z).x - 6, y: P(x, z).y - 6, width: 12, height: 12), NSColor(calibratedRed: 0.75, green: 0.55, blue: 0.3, alpha: 1)) }
    }
    // doors
    text("DOOR", P(4.5, 7.7), 12, ink, bold: true)
    text("side\ndoor", P(11.8, 3.95), 11, ink, bold: true)
}

func zones(_ zs: [Zone]) {
    for z in zs {
        let r = R(z.x0, z.z0, z.x1, z.z1).insetBy(dx: 3, dy: 3)
        fill(r, z.col.withAlphaComponent(0.33))
        let p = NSBezierPath(roundedRect: r, xRadius: 6, yRadius: 6); p.lineWidth = 2; z.col.setStroke(); p.stroke()
        text(z.name, NSPoint(x: r.midX, y: r.midY + (z.note.isEmpty ? 0 : 10)), 14, ink, bold: true)
        if !z.note.isEmpty { text(z.note, NSPoint(x: r.midX, y: r.midY - 14), 10.5, NSColor(calibratedWhite: 0.25, alpha: 1)) }
    }
}

func flow(_ pts: [(CGFloat, CGFloat)]) {
    let p = NSBezierPath(); p.lineWidth = 5; p.lineCapStyle = .round; p.lineJoinStyle = .round
    p.move(to: P(pts[0].0, pts[0].1))
    for q in pts.dropFirst() { p.line(to: P(q.0, q.1)) }
    NSColor(calibratedRed: 0.2, green: 0.6, blue: 0.3, alpha: 0.55).setStroke(); p.stroke()
}

func plan(_ name: String, _ title: String, _ sub: String, _ zs: [Zone], _ flows: [[(CGFloat, CGFloat)]], _ extra: () -> Void = {}) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    fill(NSRect(x: 0, y: 0, width: W, height: H), .white)
    shell()
    zones(zs)
    for f in flows { flow(f) }
    extra()
    walls()
    text(title, NSPoint(x: 30, y: CGFloat(H) - 40), 24, ink, bold: true, centre: false)
    text(sub, NSPoint(x: 30, y: CGFloat(H) - 72), 14, NSColor(calibratedWhite: 0.3, alpha: 1), centre: false)
    // the scale and which way is which
    let sb = NSBezierPath(); sb.lineWidth = 3; sb.move(to: NSPoint(x: 1050, y: 40)); sb.line(to: NSPoint(x: 1050 + 5 * S, y: 40)); ink.setStroke(); sb.stroke()
    text("5 m", NSPoint(x: 1050 + 2.5 * S, y: 58), 12)
    text("front (terrace, fire pit, gate) at the bottom · low end (by the fire) on the left · high end and the wing on the right", NSPoint(x: 30, y: CGFloat(H) - 98), 12, NSColor(calibratedWhite: 0.35, alpha: 1), centre: false)
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
    print("wrote \(out)/\(name).png")
}

let shop = NSColor(calibratedRed: 0.85, green: 0.35, blue: 0.45, alpha: 1)
let board = NSColor(calibratedRed: 0.25, green: 0.45, blue: 0.85, alpha: 1)
let tune = NSColor(calibratedRed: 0.35, green: 0.55, blue: 0.95, alpha: 1)
let lounge = NSColor(calibratedRed: 0.95, green: 0.6, blue: 0.2, alpha: 1)
let cocoa = NSColor(calibratedRed: 0.55, green: 0.35, blue: 0.2, alpha: 1)
let mission = NSColor(calibratedRed: 0.3, green: 0.65, blue: 0.4, alpha: 1)

// A: the one chosen and built
plan("LodgePlanA", "A — Gallery hall (built, LODGE: A)", "Clothes under the tall volume, boards at the low end by the racks, lounge and fire under the big glass, cocoa in the wing",
     [Zone(name: "OUTFITTERS", x0: -10.7, z0: -6.7, x1: -1.2, z1: -0.6, col: shop, note: "gear gallery up the tall back wall\njacket rail + closet · tables · till · mannequins"),
      Zone(name: "BOARD SHOP", x0: 4.4, z0: -6.7, x1: 10.7, z1: 0.8, col: board, note: "board walls · board tree\ncounter facing the door (upgrades)"),
      Zone(name: "WAX & TUNE", x0: 8.4, z0: 4.4, x1: 10.7, z1: 6.7, col: tune, note: "bench (upgrades)\nby the side door"),
      Zone(name: "LOUNGE + FIRE", x0: -10.7, z0: 1.6, x1: -2.4, z1: 6.7, col: lounge, note: "hanging fireplace · sofa · armchairs\nunder 10 m of glass: the view"),
      Zone(name: "HOT CHOCOLATE", x0: -15.7, z0: -5.2, x1: -11, z1: 4.7, col: cocoa, note: "stools at the glass · tables\nthe station on the back wall\n(hub session's corner)"),
      Zone(name: "missions", x0: 0.4, z0: -6.7, x1: 3.6, z1: -5.2, col: mission, note: "")],
     [[(-0.2, 12.6), (2.5, 8.2), (4.5, 7), (4.5, 1.0), (-1.5, 0.6), (-13.5, 0.4)], [(4.5, 1.0), (7.0, -0.5)], [(4.5, 1.0), (1.5, -3.5), (-6, -3.6)], [(11, 4), (9.7, 5.0)]])

// B: the fire in the middle (as built)
plan("LodgePlanB", "B — Fireside centre (built, LODGE: B)", "The lounge round a low open fire in the middle of the hall, a boutique at the high end with a storefront, the café straight from the hall",
     [Zone(name: "LOUNGE + FIRE", x0: -6.2, z0: -6.7, x1: 2.7, z1: -0.4, col: lounge, note: "low open fireplace, seen through\nsofas both sides · armchairs at the ends\na clear ring all round"),
      Zone(name: "BOUTIQUE", x0: -10.7, z0: -6.7, x1: -6.4, z1: -0.4, col: shop, note: "gear wall · jacket rail\nwardrobe + closet"),
      Zone(name: "STOREFRONT", x0: -10.7, z0: 1.9, x1: -6.4, z1: 6.7, col: shop, note: "rails · mannequins\nat the tall glass"),
      Zone(name: "BOARD SHOP", x0: 5.3, z0: -6.7, x1: 10.7, z1: 0.8, col: board, note: "counter facing the door\n(upgrades)"),
      Zone(name: "WAX & TUNE", x0: 8.4, z0: 4.4, x1: 10.7, z1: 6.7, col: tune, note: "bench (upgrades)"),
      Zone(name: "HOT CHOCOLATE", x0: -15.7, z0: -5.2, x1: -11, z1: 4.7, col: cocoa, note: "café in the wing,\nstraight from the hall"),
      Zone(name: "missions", x0: 3.05, z0: -6.7, x1: 4.95, z1: -5.2, col: mission, note: "")],
     [[(-0.2, 12.6), (2.5, 8.2), (4.5, 7), (4.5, 1.0), (-6, 0.9), (-13.5, 1.0)], [(4.5, 1.0), (7.0, -0.5)], [(-0.1, -2.05), (0.6, -3.2), (-0.1, -4.35), (-3.5, -4.35), (-4.3, -3.2), (-3.5, -2.05), (-0.1, -2.05)], [(-7.1, 0.9), (-7.1, -3.6)]])

// C: a mezzanine (as built)
plan("LodgePlanC", "C — Two levels (built, LODGE: C)", "A mezzanine across the back (+3.8 m, a wide stair by the door): the outfitters upstairs over the hall, the lounge below",
     [Zone(name: "OUTFITTERS (UPSTAIRS, +3.8 m)", x0: -10.7, z0: -6.7, x1: 2.2, z1: -1.8, col: shop, note: "gear wall · rails · mirror\nwardrobe + closet up here\n(a quiet corner under it)"),
      Zone(name: "stair", x0: 0, z0: -1.8, x1: 2.2, z1: 4.36, col: NSColor.gray, note: "22 steps\nup to -z"),
      Zone(name: "LOUNGE + FIRE", x0: -10.7, z0: -1.6, x1: -0.4, z1: 6.7, col: lounge, note: "hanging fire · sofa · armchairs\ndouble height at the glass"),
      Zone(name: "BOARD SHOP", x0: 4.4, z0: -6.7, x1: 10.7, z1: 0.8, col: board, note: "as A"),
      Zone(name: "WAX & TUNE", x0: 8.4, z0: 4.4, x1: 10.7, z1: 6.7, col: tune, note: "as A"),
      Zone(name: "HOT CHOCOLATE", x0: -15.7, z0: -5.2, x1: -11, z1: 4.7, col: cocoa, note: "café in the wing"),
      Zone(name: "missions", x0: 2.6, z0: -6.7, x1: 4.6, z1: -5.2, col: mission, note: "")],
     [[(-0.2, 12.6), (2.5, 8.2), (4.5, 7), (2.9, 5.0), (1.1, 4.4), (1.1, -1.8), (-2, -3.1), (-6, -4.5)], [(2.9, 5.0), (-1.0, 3.0), (-5, 2.6)], [(4.5, 7), (3.8, 1.0), (3.8, -4.4)], [(3.8, -2.8), (-9.5, -2.8), (-13.5, 1.0)], [(3.8, 1.0), (7.0, -0.5)]])
