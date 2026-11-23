// Renders the lodge's signs to Assets/Summit/Resources/Camp: LodgeChalk.png, the A-frame chalkboard on its terrace,
// LodgeSigns.png, the signs inside in one sheet (four rows, each 4:1): the outfitters, the board shop, wax & tune, and
// the café's menu; and LodgeArt.png, the art on its walls in one sheet (MakeLodge.py's ART): the mountain's trail map
// (the top half), two old ski posters, the LOOKOUT sign over the doors out to the deck, a contour map.
// Run: swiftc -o /tmp/lodgesigns Tools/MakeLodgeSigns.swift && /tmp/lodgesigns Assets/Summit/Resources/Camp
// (compiled, as Tools/MakeCampSigns.swift is). Drawn with AppKit.
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

func canvas(_ w: Int, _ h: Int, _ draw: (CGFloat, CGFloat) -> Void) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw(CGFloat(w), CGFloat(h))
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

func hand(_ size: CGFloat) -> NSFont {
    return NSFont(name: "ChalkboardSE-Bold", size: size) ?? NSFont(name: "Chalkboard SE", size: size) ?? NSFont(name: "MarkerFelt-Wide", size: size) ?? NSFont.boldSystemFont(ofSize: size)
}

let rep = canvas(512, 896) { W, H in
    srand48(17)
    // slate, a little uneven, with the ghosts of rubbed-out writing
    NSColor(calibratedRed: 0.11, green: 0.13, blue: 0.12, alpha: 1).setFill()
    NSRect(x: 0, y: 0, width: W, height: H).fill()
    for _ in 0..<70 {
        let r = 30 + CGFloat(drand48()) * 120
        NSColor(calibratedWhite: 1, alpha: 0.012 + 0.02 * CGFloat(drand48())).setFill()
        NSBezierPath(ovalIn: NSRect(x: CGFloat(drand48()) * W - r / 2, y: CGFloat(drand48()) * H - r / 2, width: r * 1.6, height: r)).fill()
    }
    func line(_ text: String, _ y: CGFloat, _ size: CGFloat, _ col: NSColor) {
        let a: [NSAttributedString.Key: Any] = [.font: hand(size), .foregroundColor: col]
        let s = NSAttributedString(string: text, attributes: a)
        let sz = s.size()
        s.draw(at: NSPoint(x: (W - sz.width) / 2, y: y))
    }
    let chalk = NSColor(calibratedWhite: 0.93, alpha: 0.92)
    let pink = NSColor(calibratedRed: 0.98, green: 0.62, blue: 0.7, alpha: 0.9)
    let blue = NSColor(calibratedRed: 0.55, green: 0.8, blue: 0.98, alpha: 0.9)
    let gold = NSColor(calibratedRed: 0.99, green: 0.84, blue: 0.45, alpha: 0.92)
    line("VERTIGO", H - 120, 72, chalk)
    line("LODGE", H - 190, 60, chalk)
    // a little mountain under the name
    let m = NSBezierPath(); m.lineWidth = 4
    m.move(to: NSPoint(x: W * 0.28, y: H - 228)); m.line(to: NSPoint(x: W * 0.45, y: H - 200)); m.line(to: NSPoint(x: W * 0.52, y: H - 214))
    m.line(to: NSPoint(x: W * 0.6, y: H - 196)); m.line(to: NSPoint(x: W * 0.72, y: H - 228))
    chalk.setStroke(); m.stroke()
    line("HOT CHOCOLATE", H - 330, 44, gold)
    line("& S'MORES", H - 380, 40, gold)
    line("WAX + EDGES", H - 490, 44, blue)
    line("WHILE U WAIT", H - 540, 36, blue)
    line("NEW GEAR", H - 650, 46, pink)
    line("INSIDE  ->", H - 700, 40, pink)
    line("DROP IN  v", 60, 40, chalk)
    // a chalk frame
    let f = NSBezierPath(rect: NSRect(x: 22, y: 22, width: W - 44, height: H - 44)); f.lineWidth = 3
    NSColor(calibratedWhite: 0.9, alpha: 0.5).setStroke(); f.stroke()
}
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/LodgeChalk.png"))
print("wrote \(out)/LodgeChalk.png")

func condensed(_ size: CGFloat) -> NSFont {
    return NSFont(name: "DINCondensed-Bold", size: size) ?? NSFont(name: "Futura-CondensedExtraBold", size: size) ?? NSFont.boldSystemFont(ofSize: size)
}

func centred(_ text: String, _ font: NSFont, _ col: NSColor, _ cx: CGFloat, _ y: CGFloat, kern: CGFloat = 0) {
    let s = NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: col, .kern: kern])
    s.draw(at: NSPoint(x: cx - s.size().width / 2, y: y))
}

let sheet = canvas(1024, 1024) { W, H in
    let rowH = H / 4
    let warm = NSColor(calibratedRed: 1.0, green: 0.93, blue: 0.82, alpha: 1)
    let amber = NSColor(calibratedRed: 0.98, green: 0.72, blue: 0.38, alpha: 1)
    for r in 0..<4 {
        let y0 = H - CGFloat(r + 1) * rowH
        // a charcoal steel panel with a thin warm rule (the café's is slate, chalked)
        (r == 3 ? NSColor(calibratedRed: 0.11, green: 0.13, blue: 0.12, alpha: 1) : NSColor(calibratedWhite: 0.09, alpha: 1)).setFill()
        NSRect(x: 0, y: y0, width: W, height: rowH).fill()
        let frame = NSBezierPath(rect: NSRect(x: 14, y: y0 + 14, width: W - 28, height: rowH - 28)); frame.lineWidth = 3
        (r == 3 ? NSColor(calibratedWhite: 0.85, alpha: 0.5) : amber.withAlphaComponent(0.7)).setStroke(); frame.stroke()
    }
    // 0: the outfitters
    centred("VERTIGO OUTFITTERS", condensed(118), warm, W / 2, H - rowH + 62, kern: 6)
    centred("JACKETS  ·  HELMETS  ·  GOGGLES  ·  BOOTS", condensed(34), amber, W / 2, H - rowH + 26, kern: 4)
    // 1: the board shop, a board either side
    centred("BOARD SHOP", condensed(140), warm, W / 2, H - 2 * rowH + 56, kern: 10)
    for x in [CGFloat(110), W - 110] {
        let b = NSBezierPath(roundedRect: NSRect(x: x - 22, y: H - 2 * rowH + 40, width: 44, height: 176), xRadius: 22, yRadius: 22)
        amber.setFill(); b.fill()
    }
    // 2: wax and tune
    centred("WAX & TUNE", condensed(140), warm, W / 2, H - 3 * rowH + 56, kern: 10)
    // 3: the café's menu, in chalk
    let chalk = NSColor(calibratedWhite: 0.94, alpha: 0.92)
    centred("HOT CHOCOLATE", hand(84), chalk, W / 2, rowH - 110)
    centred("CLASSIC  ·  MARSHMALLOW  ·  PEPPERMINT  ·  S'MORES", hand(30), amber, W / 2, 36)
}
try! sheet.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/LodgeSigns.png"))
print("wrote \(out)/LodgeSigns.png")

// ---------------------------------------------------------------- the art on the walls (LodgeArt.png, 1024 x 1024)

func path(_ pts: [(CGFloat, CGFloat)], close: Bool = true) -> NSBezierPath {
    let b = NSBezierPath()
    b.move(to: NSPoint(x: pts[0].0, y: pts[0].1))
    for p in pts.dropFirst() { b.line(to: NSPoint(x: p.0, y: p.1)) }
    if close { b.close() }
    return b
}

func curve(_ pts: [(CGFloat, CGFloat)]) -> NSBezierPath {
    // a smooth line through the points (Catmull-Rom as cubic Béziers)
    let b = NSBezierPath()
    b.move(to: NSPoint(x: pts[0].0, y: pts[0].1))
    for i in 0..<(pts.count - 1) {
        let p0 = pts[max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[min(pts.count - 1, i + 2)]
        b.curve(to: NSPoint(x: p2.0, y: p2.1),
                controlPoint1: NSPoint(x: p1.0 + (p2.0 - p0.0) / 6, y: p1.1 + (p2.1 - p0.1) / 6),
                controlPoint2: NSPoint(x: p2.0 - (p3.0 - p1.0) / 6, y: p2.1 - (p3.1 - p1.1) / 6))
    }
    return b
}

func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> NSColor { NSColor(calibratedRed: r, green: g, blue: b, alpha: a) }

let art = canvas(1024, 1024) { W, H in
    srand48(41)
    // ---- the trail map: the top half (0, 512) .. (1024, 1024)
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: NSRect(x: 0, y: 512, width: 1024, height: 512)).addClip()
    NSGradient(starting: rgb(0.52, 0.72, 0.9), ending: rgb(0.86, 0.92, 0.97))!.draw(in: NSRect(x: 0, y: 512, width: 1024, height: 512), angle: -90)
    // the massif: the summit left of middle, a shoulder right, falling to the valley
    let ridge: [(CGFloat, CGFloat)] = [(0, 700), (90, 760), (190, 820), (300, 905), (380, 968), (430, 940), (500, 975), (560, 930), (650, 880), (740, 900), (830, 840), (930, 800), (1024, 760), (1024, 512), (0, 512)]
    rgb(0.97, 0.98, 1.0).setFill(); path(ridge).fill()
    // shaded faces down from the ridge (blue-grey), and rock bands
    rgb(0.72, 0.79, 0.88).setFill()
    path([(380, 968), (430, 940), (470, 820), (420, 700), (360, 780)]).fill()
    path([(500, 975), (560, 930), (600, 800), (540, 690), (520, 840)]).fill()
    path([(740, 900), (830, 840), (860, 740), (790, 700), (760, 800)]).fill()
    rgb(0.58, 0.62, 0.7).setFill()
    path([(300, 905), (330, 890), (320, 850), (290, 870)]).fill()
    path([(650, 880), (690, 875), (680, 830), (640, 845)]).fill()
    // the forest along the foot, and the valley floor
    rgb(0.86, 0.9, 0.95).setFill(); NSRect(x: 0, y: 512, width: 1024, height: 70).fill()
    for i in 0..<90 {
        let x = CGFloat(i) * 11.6 + CGFloat(drand48()) * 6, y = 560 + CGFloat(drand48()) * 60 + 30 * sin(CGFloat(i) / 9)
        let h = 22 + CGFloat(drand48()) * 16
        rgb(0.13 + 0.05 * CGFloat(drand48()), 0.3, 0.2).setFill()
        path([(x - h * 0.3, y), (x + h * 0.3, y), (x, y + h)]).fill()
    }
    // the lifts: straight lines up, towers along them
    for (a, b) in [((260, 590), (420, 900)), ((520, 600), (640, 860)), ((760, 590), (860, 800))] as [((CGFloat, CGFloat), (CGFloat, CGFloat))] {
        let l = path([a, b], close: false); l.lineWidth = 3; rgb(0.12, 0.12, 0.14).setStroke(); l.stroke()
        for k in 0...6 {
            let t = CGFloat(k) / 6
            let p = NSPoint(x: a.0 + (b.0 - a.0) * t, y: a.1 + (b.1 - a.1) * t)
            rgb(0.12, 0.12, 0.14).setFill(); NSBezierPath(ovalIn: NSRect(x: p.x - 4, y: p.y - 4, width: 8, height: 8)).fill()
        }
    }
    // the runs, white-edged
    let runs: [([(CGFloat, CGFloat)], NSColor, String)] = [
        ([(420, 905), (380, 820), (300, 760), (240, 690), (200, 600)], rgb(0.15, 0.55, 0.25), "MEADOW"),
        ([(430, 930), (470, 850), (450, 760), (500, 680), (480, 600)], rgb(0.12, 0.4, 0.85), "BIRCH RUN"),
        ([(560, 920), (600, 840), (580, 760), (650, 690), (640, 600)], rgb(0.85, 0.15, 0.15), "VERTIGO"),
        ([(500, 970), (520, 880), (540, 780), (560, 700), (590, 610)], rgb(0.05, 0.05, 0.06), "THE WALL"),
        ([(860, 800), (820, 730), (880, 660), (840, 600)], rgb(0.12, 0.4, 0.85), "GLACIER"),
        ([(830, 830), (760, 760), (720, 680), (740, 600)], rgb(0.85, 0.15, 0.15), "CREVASSE"),
    ]
    for (pts, col, _) in runs {
        let w = curve(pts); w.lineWidth = 11; NSColor.white.setStroke(); w.lineCapStyle = .round; w.stroke()
        let c = curve(pts); c.lineWidth = 6; col.setStroke(); c.lineCapStyle = .round; c.stroke()
    }
    for (pts, col, name) in runs {
        let m = pts[pts.count / 2]
        let s = NSAttributedString(string: name, attributes: [.font: condensed(20), .foregroundColor: col, .strokeColor: NSColor.white, .strokeWidth: -3])
        s.draw(at: NSPoint(x: m.0 + 12, y: m.1 - 8))
    }
    // the lodge at the foot, the title, the legend
    rgb(0.25, 0.17, 0.12).setFill(); NSRect(x: 470, y: 560, width: 34, height: 16).fill()
    path([(466, 576), (508, 576), (487, 592)]).fill()
    rgb(0.08, 0.1, 0.14, 0.9).setFill(); NSRect(x: 24, y: 940, width: 300, height: 64).fill()
    centred("VERTIGO", condensed(54), rgb(1, 0.93, 0.82), 174, 950, kern: 8)
    rgb(0.08, 0.1, 0.14, 0.85).setFill(); NSRect(x: 830, y: 526, width: 176, height: 120).fill()
    for (k, (col, txt)) in [(rgb(0.15, 0.55, 0.25), "EASY"), (rgb(0.12, 0.4, 0.85), "INTERMEDIATE"), (rgb(0.85, 0.15, 0.15), "ADVANCED"), (rgb(0.6, 0.6, 0.62), "EXPERT")].enumerated() {
        let y = 616 - CGFloat(k) * 26
        col.setFill(); NSBezierPath(ovalIn: NSRect(x: 846, y: y, width: 14, height: 14)).fill()
        NSAttributedString(string: txt, attributes: [.font: condensed(19), .foregroundColor: NSColor.white]).draw(at: NSPoint(x: 870, y: y - 3))
    }
    NSGraphicsContext.restoreGraphicsState()
    let mf = NSBezierPath(rect: NSRect(x: 4, y: 516, width: 1016, height: 504)); mf.lineWidth = 8; rgb(0.96, 0.95, 0.92).setStroke(); mf.stroke()

    // ---- poster A (0, 0) .. (340, 510): a skier under a big sun
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: NSRect(x: 0, y: 0, width: 340, height: 510)).addClip()
    rgb(0.96, 0.9, 0.78).setFill(); NSRect(x: 0, y: 0, width: 340, height: 510).fill()
    rgb(0.95, 0.52, 0.2).setFill(); NSBezierPath(ovalIn: NSRect(x: 150, y: 300, width: 160, height: 160)).fill()
    rgb(0.16, 0.33, 0.52).setFill(); path([(0, 150), (90, 330), (170, 230), (250, 380), (340, 220), (340, 150)]).fill()
    rgb(0.93, 0.95, 0.97).setFill(); path([(70, 290), (90, 330), (112, 300)]).fill(); path([(225, 335), (250, 380), (276, 333)]).fill()
    rgb(0.1, 0.22, 0.38).setFill(); path([(0, 110), (120, 240), (220, 160), (340, 260), (340, 110)]).fill()
    rgb(0.98, 0.98, 0.98).setFill(); path([(0, 0), (0, 150), (340, 70), (340, 0)]).fill()
    // the skier: crouched, poles back, skis across the slope
    rgb(0.8, 0.12, 0.1).setFill()
    let body = NSBezierPath(roundedRect: NSRect(x: -18, y: 0, width: 36, height: 62), xRadius: 14, yRadius: 14)
    let tr = NSAffineTransform(); tr.translateX(by: 170, yBy: 150); tr.rotate(byDegrees: -28); body.transform(using: tr as AffineTransform); body.fill()
    rgb(0.12, 0.12, 0.14).setFill(); NSBezierPath(ovalIn: NSRect(x: 180, y: 208, width: 26, height: 26)).fill()
    let skis = path([(120, 128), (250, 98)], close: false); skis.lineWidth = 6; rgb(0.12, 0.12, 0.14).setStroke(); skis.lineCapStyle = .round; skis.stroke()
    let poles = path([(160, 170), (110, 140)], close: false); poles.lineWidth = 3; poles.stroke()
    let p2 = path([(185, 180), (140, 145)], close: false); p2.lineWidth = 3; p2.stroke()
    centred("VERTIGO", condensed(78), rgb(0.1, 0.22, 0.38), 170, 22, kern: 6)
    centred("SKI  ·  LODGE  ·  1962", condensed(20), rgb(0.8, 0.3, 0.12), 170, 104, kern: 3)
    NSGraphicsContext.restoreGraphicsState()

    // ---- poster B (342, 0) .. (682, 510): a snowboarder in the air over the peak, at night
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: NSRect(x: 342, y: 0, width: 340, height: 510)).addClip()
    NSGradient(starting: rgb(0.05, 0.18, 0.24), ending: rgb(0.1, 0.42, 0.45))!.draw(in: NSRect(x: 342, y: 0, width: 340, height: 510), angle: -90)
    for _ in 0..<40 {
        let x = 342 + CGFloat(drand48()) * 340, y = 260 + CGFloat(drand48()) * 250, r = 1 + CGFloat(drand48()) * 2
        NSColor(calibratedWhite: 1, alpha: 0.8).setFill(); NSBezierPath(ovalIn: NSRect(x: x, y: y, width: r, height: r)).fill()
    }
    rgb(0.96, 0.97, 0.98).setFill(); path([(342, 60), (470, 290), (530, 230), (682, 120), (682, 0), (342, 0)]).fill()
    rgb(0.72, 0.82, 0.88).setFill(); path([(470, 290), (530, 230), (500, 160), (450, 200)]).fill()
    // the rider: arms out, board grabbed, high over the ridge
    let bd = NSBezierPath(roundedRect: NSRect(x: -60, y: -9, width: 120, height: 18), xRadius: 9, yRadius: 9)
    let t2 = NSAffineTransform(); t2.translateX(by: 520, yBy: 360); t2.rotate(byDegrees: 18); bd.transform(using: t2 as AffineTransform)
    rgb(0.98, 0.6, 0.15).setFill(); bd.fill()
    rgb(0.98, 0.98, 0.98).setFill()
    let rb = NSBezierPath(roundedRect: NSRect(x: -16, y: 0, width: 32, height: 56), xRadius: 12, yRadius: 12)
    let t3 = NSAffineTransform(); t3.translateX(by: 525, yBy: 372); t3.rotate(byDegrees: 12); rb.transform(using: t3 as AffineTransform); rb.fill()
    NSBezierPath(ovalIn: NSRect(x: 505, y: 428, width: 24, height: 24)).fill()
    let arms = path([(470, 420), (520, 410), (580, 430)], close: false); arms.lineWidth = 7; NSColor(calibratedWhite: 0.98, alpha: 1).setStroke(); arms.lineCapStyle = .round; arms.stroke()
    centred("SUMMIT", condensed(74), rgb(0.98, 0.6, 0.15), 512, 82, kern: 8)
    centred("SESSIONS  ·  NIGHT RIDING", condensed(20), rgb(0.1, 0.28, 0.32), 512, 50, kern: 3)
    NSGraphicsContext.restoreGraphicsState()

    // ---- the LOOKOUT sign (684, 386) .. (1024, 510)
    rgb(0.09, 0.09, 0.09).setFill(); NSRect(x: 684, y: 386, width: 340, height: 124).fill()
    let lf = NSBezierPath(rect: NSRect(x: 692, y: 394, width: 324, height: 108)); lf.lineWidth = 3
    rgb(0.98, 0.72, 0.38, 0.75).setStroke(); lf.stroke()
    centred("LOOKOUT", condensed(70), rgb(1.0, 0.93, 0.82), 834, 414, kern: 7)
    let ar = path([(974, 448), (1002, 448), (990, 436), (1002, 448), (990, 460)], close: false); ar.lineWidth = 5
    rgb(0.98, 0.72, 0.38).setStroke(); ar.lineCapStyle = .round; ar.stroke()

    // ---- the contour map (684, 0) .. (1024, 384)
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: NSRect(x: 684, y: 0, width: 340, height: 384)).addClip()
    rgb(0.95, 0.92, 0.84).setFill(); NSRect(x: 684, y: 0, width: 340, height: 384).fill()
    for (cx, cy, n) in [(CGFloat(820), CGFloat(230), 11), (CGFloat(930), CGFloat(140), 7)] {
        for k in 1...n {
            var pts: [(CGFloat, CGFloat)] = []
            let r0 = CGFloat(k) * 15
            for j in 0..<25 {
                let a = CGFloat(j) / 24 * 2 * .pi
                let r = r0 * (1 + 0.18 * sin(a * 3 + CGFloat(k) * 0.4) + 0.1 * cos(a * 5 - CGFloat(k)))
                pts.append((cx + r * cos(a) * 1.2, cy + r * sin(a)))
            }
            let c = curve(pts); c.lineWidth = k % 5 == 0 ? 2.2 : 1.1
            rgb(0.55, 0.36, 0.2, 0.85).setStroke(); c.stroke()
        }
    }
    let river = curve([(684, 60), (740, 90), (790, 70), (860, 40), (930, 60), (1024, 30)]); river.lineWidth = 3
    rgb(0.3, 0.5, 0.75).setStroke(); river.stroke()
    centred("VERTIGO MASSIF   1 : 25 000", condensed(18), rgb(0.35, 0.22, 0.12), 854, 14, kern: 2)
    NSGraphicsContext.restoreGraphicsState()
}
try! art.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/LodgeArt.png"))
print("wrote \(out)/LodgeArt.png")
