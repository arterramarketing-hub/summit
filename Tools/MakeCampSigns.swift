// Renders the summit camp's textures to PNGs: carved wooden signs, a wood-grain tile for the logs and
// planks, and the paper notes pinned to the mission board.
// Run: swiftc -o /tmp/campsigns Tools/MakeCampSigns.swift && /tmp/campsigns Assets/Summit/Resources/Camp
// (compiled: the swift interpreter crashes on it). Drawn with AppKit, so the bitmaps stay premultiplied.
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

func font(_ size: CGFloat) -> NSFont {
    return NSFont(name: "DINCondensed-Bold", size: size) ?? NSFont(name: "Futura-CondensedExtraBold", size: size) ?? NSFont.boldSystemFont(ofSize: size)
}

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

// wood: warm planks with long wavy grain lines and the odd knot (the same pattern tiles left to right)
func wood(_ W: CGFloat, _ H: CGFloat, base: NSColor, planks: Int, seed: Int) {
    srand48(seed)
    base.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    let ph = H / CGFloat(planks)
    for p in 0..<planks {
        let y0 = CGFloat(p) * ph
        // each plank a slightly different tone
        NSColor(calibratedWhite: drand48() < 0.5 ? 0 : 1, alpha: 0.04 + 0.05 * CGFloat(drand48())).setFill()
        NSRect(x: 0, y: y0, width: W, height: ph).fill()
        // grain
        for _ in 0..<Int(ph / 3) {
            let y = y0 + CGFloat(drand48()) * ph, amp = CGFloat(drand48()) * ph * 0.06, k = CGFloat(1 + Int(drand48() * 3)) * 2 * .pi / W
            let path = NSBezierPath(); path.lineWidth = 0.6 + CGFloat(drand48()) * 1.6
            var x: CGFloat = 0
            path.move(to: NSPoint(x: 0, y: y))
            while x <= W { path.line(to: NSPoint(x: x, y: y + amp * sin(k * x + CGFloat(p)))); x += 8 }
            NSColor(calibratedRed: 0.18, green: 0.10, blue: 0.05, alpha: 0.10 + 0.18 * CGFloat(drand48())).setStroke()
            path.stroke()
        }
        // a knot or two
        for _ in 0..<(drand48() < 0.6 ? 1 : 0) {
            let c = NSPoint(x: CGFloat(drand48()) * W, y: y0 + ph * (0.3 + 0.4 * CGFloat(drand48())))
            for r in stride(from: ph * 0.22, to: 1, by: -ph * 0.05) {
                NSColor(calibratedRed: 0.22, green: 0.12, blue: 0.06, alpha: 0.18).setStroke()
                let o = NSBezierPath(ovalIn: NSRect(x: c.x - r * 1.8, y: c.y - r, width: r * 3.6, height: r * 2)); o.lineWidth = 1.2; o.stroke()
            }
        }
        // the gap between planks
        NSColor(calibratedRed: 0.10, green: 0.06, blue: 0.03, alpha: 0.75).setFill()
        NSRect(x: 0, y: y0, width: W, height: max(2, ph * 0.035)).fill()
    }
}

// a carved sign: planks, a dark routed border, cream lettering with a cut-in shadow
func sign(_ name: String, w: Int, h: Int, text: String, sub: String?, seed: Int) {
    save(canvas(w, h) { W, H in
        wood(W, H, base: NSColor(calibratedRed: 0.50, green: 0.32, blue: 0.18, alpha: 1), planks: 2, seed: seed)
        let inset = H * 0.07
        let border = NSBezierPath(roundedRect: NSRect(x: inset, y: inset, width: W - 2 * inset, height: H - 2 * inset), xRadius: H * 0.06, yRadius: H * 0.06)
        border.lineWidth = H * 0.035
        NSColor(calibratedRed: 0.16, green: 0.09, blue: 0.04, alpha: 0.8).setStroke(); border.stroke()
        let para = NSMutableParagraphStyle(); para.alignment = .center
        let size = H * (sub == nil ? 0.56 : 0.40)
        func draw(_ s: String, _ sz: CGFloat, _ y: CGFloat, _ col: NSColor, _ dx: CGFloat, _ dy: CGFloat) {
            let a: [NSAttributedString.Key: Any] = [.font: font(sz), .foregroundColor: col, .paragraphStyle: para, .kern: sz * 0.08]
            NSAttributedString(string: s, attributes: a).draw(in: NSRect(x: dx, y: y + dy, width: W, height: sz * 1.3))
        }
        let ty = sub == nil ? (H - size * 1.3) / 2 - H * 0.1 : H * 0.29
        draw(text, size, ty, NSColor(calibratedRed: 0.12, green: 0.06, blue: 0.02, alpha: 0.85), H * 0.012, -H * 0.018)   // the cut
        draw(text, size, ty, NSColor(calibratedRed: 0.98, green: 0.92, blue: 0.78, alpha: 1), 0, 0)
        if let sub = sub {
            draw(sub, H * 0.17, H * 0.14, NSColor(calibratedRed: 0.12, green: 0.06, blue: 0.02, alpha: 0.85), H * 0.008, -H * 0.01)
            draw(sub, H * 0.17, H * 0.14, NSColor(calibratedRed: 0.98, green: 0.92, blue: 0.78, alpha: 1), 0, 0)
        }
    }, name)
}

sign("SignCamp", w: 1024, h: 256, text: "SUMMIT CAMP", sub: "MT. VERTIGO  ·  ELEV. 10,000 FT", seed: 3)
sign("SignMissions", w: 1024, h: 256, text: "DAILY MISSIONS", sub: nil, seed: 5)
sign("SignUpgrades", w: 1024, h: 256, text: "TUNE-UP BENCH", sub: nil, seed: 7)
sign("SignDropIn", w: 1024, h: 256, text: "DROP IN", sub: "THIS WAY TO THE START", seed: 11)
sign("SignLookout", w: 1024, h: 256, text: "LOOKOUT", sub: nil, seed: 13)

// wood tile for logs, planks and benches (grain runs along U)
save(canvas(512, 512) { W, H in wood(W, H, base: NSColor(calibratedRed: 0.55, green: 0.36, blue: 0.20, alpha: 1), planks: 4, seed: 21) }, "Wood")
// bark for the cabin's logs: rough, dark streaks running along V (a Unity cylinder's length)
save(canvas(256, 512) { W, H in
    srand48(31)
    NSColor(calibratedRed: 0.42, green: 0.27, blue: 0.15, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    for _ in 0..<900 {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H, l = 10 + CGFloat(drand48()) * 60
        NSColor(calibratedRed: 0.20, green: 0.12, blue: 0.06, alpha: 0.15 + 0.25 * CGFloat(drand48())).setFill()
        NSRect(x: x, y: y, width: 1 + CGFloat(drand48()) * 2.5, height: l).fill()
    }
    for _ in 0..<300 {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H
        NSColor(calibratedRed: 0.75, green: 0.55, blue: 0.35, alpha: 0.12).setFill()
        NSRect(x: x, y: y, width: 1.2, height: 20 + CGFloat(drand48()) * 40).fill()
    }
}, "Logs")
// a pinned paper note: ruled lines, a few scribbles, a red pin
for (i, tint) in [(0, NSColor(calibratedRed: 0.98, green: 0.96, blue: 0.86, alpha: 1)), (1, NSColor(calibratedRed: 0.92, green: 0.96, blue: 0.99, alpha: 1)),
                  (2, NSColor(calibratedRed: 0.99, green: 0.93, blue: 0.80, alpha: 1))] {
    save(canvas(256, 320) { W, H in
        srand48(41 + i)
        NSColor.clear.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
        let paper = NSBezierPath()
        paper.move(to: NSPoint(x: W * 0.04, y: H * 0.03)); paper.line(to: NSPoint(x: W * 0.97, y: H * 0.05))
        paper.line(to: NSPoint(x: W * 0.95, y: H * 0.97)); paper.line(to: NSPoint(x: W * 0.03, y: H * 0.95)); paper.close()
        tint.setFill(); paper.fill()
        NSColor(calibratedRed: 0.55, green: 0.65, blue: 0.85, alpha: 0.5).setStroke()
        for k in 1..<10 { let y = H * (0.12 + 0.075 * CGFloat(k)); let l = NSBezierPath(); l.move(to: NSPoint(x: W * 0.1, y: y)); l.line(to: NSPoint(x: W * 0.9, y: y)); l.lineWidth = 1.5; l.stroke() }
        // scribbled handwriting
        NSColor(calibratedRed: 0.12, green: 0.15, blue: 0.30, alpha: 0.75).setStroke()
        for k in 0..<6 {
            let y = H * (0.80 - 0.075 * CGFloat(k)) + 4, end = W * (0.45 + 0.4 * CGFloat(drand48()))
            let s = NSBezierPath(); s.lineWidth = 3; s.move(to: NSPoint(x: W * 0.12, y: y))
            var x = W * 0.12
            while x < end { x += 6 + CGFloat(drand48()) * 6; s.line(to: NSPoint(x: x, y: y + CGFloat(drand48() * 10 - 3))) }
            s.stroke()
        }
        NSColor(calibratedRed: 0.85, green: 0.12, blue: 0.10, alpha: 1).setFill()
        NSBezierPath(ovalIn: NSRect(x: W * 0.44, y: H * 0.86, width: W * 0.12, height: W * 0.12)).fill()
        NSColor(calibratedWhite: 1, alpha: 0.6).setFill()
        NSBezierPath(ovalIn: NSRect(x: W * 0.47, y: H * 0.905, width: W * 0.035, height: W * 0.035)).fill()
    }, "Note\(i)")
}
print("camp textures written to \(out)")

// the trodden footpath: a soft packed strip with boot prints stepping along it (V runs along the path;
// alpha is how much it shows over the snow)
save(canvas(256, 512) { W, H in
    srand48(51)
    NSColor.clear.setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    // the packed snow down the middle, fading out at the sides
    for i in 0..<64 {
        let u = CGFloat(i) / 63, a = 0.22 * max(0, 1 - pow(abs(u - 0.5) * 2.1, 2))
        NSColor(calibratedRed: 0.55, green: 0.62, blue: 0.75, alpha: a).setFill()
        NSRect(x: u * W - W / 64, y: 0, width: W / 32, height: H).fill()
    }
    // boot prints, left and right feet in turn, a little scattered (some walked by others)
    func boot(_ cx: CGFloat, _ cy: CGFloat, _ ang: CGFloat, _ a: CGFloat) {
        let ctx = NSGraphicsContext.current!.cgContext
        ctx.saveGState(); ctx.translateBy(x: cx, y: cy); ctx.rotate(by: ang)
        NSColor(calibratedRed: 0.30, green: 0.36, blue: 0.50, alpha: a).setFill()
        NSBezierPath(ovalIn: NSRect(x: -16, y: -6, width: 32, height: 46)).fill()     // the sole
        NSBezierPath(ovalIn: NSRect(x: -13, y: -44, width: 26, height: 30)).fill()    // the heel
        NSColor(calibratedRed: 0.20, green: 0.25, blue: 0.38, alpha: a * 0.6).setFill()
        for k in 0..<4 { NSRect(x: -12, y: CGFloat(k) * 9 + 2, width: 24, height: 3).fill() }   // tread
        ctx.restoreGState()
    }
    var y: CGFloat = 30
    var left = true
    while y < H + 40 {
        boot(W * (left ? 0.38 : 0.62) + CGFloat(drand48() * 10 - 5), y, CGFloat(drand48() * 0.3 - 0.15), 0.55)
        if drand48() < 0.5 { boot(W * (drand48() < 0.5 ? 0.25 : 0.75), y + 40, CGFloat(drand48() * 0.6 - 0.3), 0.3) }
        y += H / 6; left.toggle()   // six to a tile, so it repeats cleanly
    }
}, "Bootprints")

// birch bark: chalk white, with dark horizontal slashes and knots (V runs up the trunk)
save(canvas(128, 512) { W, H in
    srand48(61)
    NSColor(calibratedRed: 0.92, green: 0.91, blue: 0.88, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: W, height: H).fill()
    for _ in 0..<500 {
        NSColor(calibratedRed: 0.75, green: 0.73, blue: 0.70, alpha: 0.25).setFill()
        NSRect(x: CGFloat(drand48()) * W, y: CGFloat(drand48()) * H, width: 4 + CGFloat(drand48()) * 14, height: 1).fill()
    }
    for _ in 0..<70 {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H, w = 6 + CGFloat(drand48()) * 30
        NSColor(calibratedRed: 0.10, green: 0.09, blue: 0.08, alpha: 0.75 + 0.2 * CGFloat(drand48())).setFill()
        NSBezierPath(ovalIn: NSRect(x: x, y: y, width: w, height: 2 + CGFloat(drand48()) * 4)).fill()
    }
    for _ in 0..<6 {
        let x = CGFloat(drand48()) * W, y = CGFloat(drand48()) * H
        NSColor(calibratedRed: 0.12, green: 0.10, blue: 0.09, alpha: 0.85).setFill()
        let p = NSBezierPath(); p.move(to: NSPoint(x: x - 12, y: y)); p.line(to: NSPoint(x: x, y: y + 9)); p.line(to: NSPoint(x: x + 12, y: y)); p.line(to: NSPoint(x: x, y: y + 4)); p.close(); p.fill()
    }
}, "Birch")
print("footpath and birch textures written")
