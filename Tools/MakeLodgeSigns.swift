// Renders the lodge's signs to Assets/Summit/Resources/Camp: LodgeChalk.png, the A-frame chalkboard on its terrace, and
// LodgeSigns.png, the signs inside in one sheet (four rows, each 4:1): the outfitters, the board shop, wax & tune, and
// the café's menu.
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
