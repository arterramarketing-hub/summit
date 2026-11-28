// Draws the lodge's floor plan with its inside (lodge.md) to Tools/art/lodge/LodgePlan.png.
// Run: swiftc -o /tmp/lodgeplan Tools/MakeLodgePlan.swift && /tmp/lodgeplan Tools/art/lodge
// The plan is the built shell (Tools/MakeLodge.py): the hall 22 x 14 m, the wing 5 x 10.5 m, the terrace and its
// steps, the doors, the glulam columns, the tall volume under the clerestory. Drawn as seen from above with the front
// (the terrace, the fire, the gate) at the bottom, so the low end (the photo's left) is on the left.
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let S: CGFloat = 30          // pixels a metre
let W = 1400, H = 860
let ox: CGFloat = 735, oy: CGFloat = 530   // where the hall's middle lands (x flipped: +x, the low end, to the left)

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
    fill(R(-17.2, 7, 19, 12.6), NSColor(calibratedWhite: 0.86, alpha: 1))
    fill(R(-17.2, 5, -11, 7), NSColor(calibratedWhite: 0.86, alpha: 1))
    fill(R(11, -7, 19, 7), NSColor(calibratedWhite: 0.86, alpha: 1))
    for (x0, x1) in [(-5.2, 4.8), (-15.2, -11.6)] as [(CGFloat, CGFloat)] {
        for k in 0..<6 { fill(R(x0, 12.6 + CGFloat(k) * 0.36, x1, 12.6 + CGFloat(k + 1) * 0.36), NSColor(calibratedWhite: k % 2 == 0 ? 0.78 : 0.82, alpha: 1)) }
        text("steps down", P((x0 + x1) / 2, 13.9), 12, NSColor(calibratedWhite: 0.35, alpha: 1))
    }
    for (x0, x1) in [(-17.1, -15.4), (-11.4, -5.4), (5.2, 9.4), (9.6, 18.9)] as [(CGFloat, CGFloat)] {
        fill(R(x0, 11.3, x1, 12.6), NSColor(calibratedRed: 0.86, green: 0.9, blue: 0.95, alpha: 1)); stroke(R(x0, 11.3, x1, 12.6), NSColor(calibratedWhite: 0.6, alpha: 1), 1)
    }
    text("TERRACE", P(-3.5, 9.6), 13, NSColor(calibratedWhite: 0.4, alpha: 1), bold: true)
    text("fire table", P(-8.8, 10.4), 11, NSColor(calibratedWhite: 0.4, alpha: 1))
    // the covered section under the roof's tail (MakeLodge.py's RACKS, TABLES, TAIL_POSTS): its roof's outline, the
    // posts along its edge, the racks in two rows at the back, the picnic tables, the way out of the side door
    let roofC = NSColor(calibratedRed: 0.55, green: 0.42, blue: 0.3, alpha: 1)
    let edge = NSBezierPath(rect: R(11, -8, 19, 10)); edge.lineWidth = 1.5; edge.setLineDash([6, 4], count: 2, phase: 0); roofC.setStroke(); edge.stroke()
    for z in [-6.55, -2.4, 2.6, 6.1, 9.6] as [CGFloat] { fill(R(18.35, z - 0.15, 18.65, z + 0.15), roofC) }
    for (x0, x1, z) in [(11.5, 14.2, -6.2), (15.4, 18.0, -6.2), (11.5, 14.2, -3.3), (15.4, 18.0, -3.3)] as [(CGFloat, CGFloat, CGFloat)] {
        fill(R(x0, z - 0.25, x1, z + 0.25), NSColor(calibratedWhite: 0.25, alpha: 1))
        for k in 0..<Int((x1 - x0) / 0.24) { fill(R(x0 + 0.12 + CGFloat(k) * 0.24, z - 0.05, x0 + 0.2 + CGFloat(k) * 0.24, z + 0.15), [NSColor.systemRed, .systemOrange, .systemGreen, .systemPurple, .white][k % 5]) }
    }
    let wood = NSColor(calibratedRed: 0.85, green: 0.68, blue: 0.45, alpha: 1)
    for (x, z) in [(12.9, -0.2), (16.5, -0.2), (12.9, 6.6), (16.5, 6.6), (16.5, 9.2)] as [(CGFloat, CGFloat)] {
        fill(R(x - 1.1, z - 0.4, x + 1.1, z + 0.4), wood)
        for s in [-1, 1] as [CGFloat] { fill(R(x - 1.1, z + s * 0.72 - 0.15, x + 1.1, z + s * 0.72 + 0.15), wood.withAlphaComponent(0.7)) }
    }
    text("COVERED SECTION\n(under the roof's tail)", P(14.9, -4.75), 11, NSColor(calibratedWhite: 0.3, alpha: 1), bold: true)
    text("side door's way out", P(14.9, 3.95), 10, NSColor(calibratedWhite: 0.4, alpha: 1))
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

// the floor at +3.8 m drawn over the ground floor (dashed, violet) with its stair
let upCol = NSColor(calibratedRed: 0.45, green: 0.3, blue: 0.85, alpha: 1)
func upstairs(_ rects: [(CGFloat, CGFloat, CGFloat, CGFloat)], _ label: String, _ at: (CGFloat, CGFloat), _ stairs: [(CGFloat, CGFloat, CGFloat, CGFloat)]) {
    for (x0, z0, x1, z1) in rects {
        let r = R(x0, z0, x1, z1)
        upCol.withAlphaComponent(0.14).setFill(); r.fill()
        let p = NSBezierPath(rect: r); p.lineWidth = 3; p.setLineDash([10, 6], count: 2, phase: 0); upCol.setStroke(); p.stroke()
    }
    for (x0, z0, x1, z1) in stairs {
        let r = R(x0, z0, x1, z1)
        NSColor(calibratedWhite: 0.45, alpha: 0.85).setFill(); r.fill()
        text("stair", NSPoint(x: r.midX, y: r.midY - 6), 11, .white, bold: true)
    }
    text(label, P(at.0, at.1), 13, upCol, bold: true)
}
let bGround = [Zone(name: "LOUNGE + FIRE", x0: -6.2, z0: -6.7, x1: 2.7, z1: -0.4, col: lounge, note: "low open fire, a clear ring all round"),
               Zone(name: "BOARD SHOP", x0: 5.3, z0: -6.7, x1: 10.7, z1: 0.8, col: board, note: "counter (upgrades)"),
               Zone(name: "WAX & TUNE", x0: 8.4, z0: 4.4, x1: 10.7, z1: 6.7, col: tune, note: "bench"),
               Zone(name: "HOT CHOCOLATE", x0: -15.7, z0: -5.2, x1: -11, z1: 4.7, col: cocoa, note: "café in the wing"),
               Zone(name: "missions", x0: 3.05, z0: -6.7, x1: 4.95, z1: -5.2, col: mission, note: "")]
let snug = Zone(name: "SNUG + CAFÉ TABLES", x0: -10.7, z0: -6.7, x1: -6.4, z1: 6.7, col: lounge, note: "books · leather sofa\nunder the loft")

// the inside (the B2R design, the one kept): the loft and its stair along the back wall, the lounge round the fire and
// facing the glass, the snug and the café's tables under the loft, the loft's doors out onto the lookout deck
plan("LodgePlan", "The lodge's inside — loft, back stair, lookout deck (built)", "The loft over the high end, its stair along the back wall; the lounge round the fire and facing the glass; the snug and the café's tables under the loft; the loft's doors out onto a lookout deck on the wing's roof",
     [Zone(name: "LOUNGE + FIRE", x0: -6.4, z0: -5.1, x1: 2.7, z1: 0.3, col: lounge, note: "sofas across the fire\narmchair pairs at its ends · lamps"),
      Zone(name: "WINDOW LOUNGE", x0: -5.2, z0: 2.05, x1: 1.2, z1: 5.15, col: lounge, note: "a sofa facing the glass\nand the view")] + Array(bGround.dropFirst()) + [snug],
     [[(-0.2, 12.6), (2.5, 8.2), (4.5, 7), (3.6, 0.8), (3.0, -4.4), (1.6, -5.9), (-5.0, -5.9), (-7.2, -4.4), (-8.6, -4.0)], [(4.5, 7), (3.6, 1.2), (-4.6, 1.0), (-10, 0.8), (-13.5, 1.0)],
      [(-0.1, -1.72), (0.45, -2.71), (-0.1, -3.7), (-3.5, -3.7), (-4.1, -2.71), (-3.5, -1.72), (-0.1, -1.72)],
      [(-7.2, -4.4), (-7.3, -0.7), (-9.8, -0.1), (-12.2, 0.0), (-13.0, 2.0), (-13.2, 4.7)]]) {
    upstairs([(-10.7, -6.7, -5.4, 6.5), (-16.9, -5.8, -11.0, 6.6)], "UPSTAIRS +3.8: the outfitters' loft (wardrobe + closet)", (-8.0, 7.6), [(-5.4, -6.64, 0.76, -5.14)])
    text("LOOKOUT DECK +3.8 (on the wing's roof)\nglass rails · ledge + stools · telescope", P(-13.9, -7.35), 12, upCol, bold: true)
    fill(R(-11.15, -0.95, -10.55, 0.95), NSColor(calibratedRed: 0.36, green: 0.55, blue: 0.85, alpha: 1))
    text("doors", P(-10.85, -1.35), 10, upCol, bold: true)
}
