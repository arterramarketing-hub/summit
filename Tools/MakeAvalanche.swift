// Paints the avalanche's powder puffs: four cauliflower billows in a 2 x 2 atlas.
// Build and run:  swiftc -O Tools/MakeAvalanche.swift -o /tmp/makeavalanche && /tmp/makeavalanche Assets/Summit/Resources/Avalanche
//
// Each puff is a heap of overlapping spheres seen from the front, so every pixel knows the shape of the
// billow it sits on. The texture stores that shape, not a colour, and the shader lights it (see Powder.shader):
//   R, G  the surface normal's x and y (0.5 = flat on to the camera)
//   B     how open to the sky it is there: dark in the creases between billows and low down at the back,
//         bright on the crowns (ambient occlusion, which is what makes a white cloud read against white snow)
//   A     coverage, with a soft, wind-torn edge
import AppKit

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let cell = 512, cells = 2, size = cell * cells

struct RNG {
    var s: UInt64
    mutating func u() -> Double { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return Double(s >> 11) / Double(UInt64(1) << 53) }
    mutating func r(_ a: Double, _ b: Double) -> Double { a + (b - a) * u() }
}

// smooth value noise, a few octaves
func hash(_ x: Int, _ y: Int, _ salt: Int) -> Double {
    var h = UInt64(bitPattern: Int64(x &* 73856093 ^ y &* 19349663 ^ salt &* 83492791))
    h ^= h >> 33; h = h &* 0xff51afd7ed558ccd; h ^= h >> 33; h = h &* 0xc4ceb9fe1a85ec53; h ^= h >> 33
    return Double(h & 0xffffff) / Double(0xffffff)
}
func vnoise(_ x: Double, _ y: Double, _ salt: Int) -> Double {
    let xi = Int(floor(x)), yi = Int(floor(y))
    let fx = x - floor(x), fy = y - floor(y)
    let sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy)
    let a = hash(xi, yi, salt), b = hash(xi + 1, yi, salt), c = hash(xi, yi + 1, salt), d = hash(xi + 1, yi + 1, salt)
    return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy
}
func fbm(_ x: Double, _ y: Double, _ salt: Int, octaves: Int = 5) -> Double {
    var v = 0.0, amp = 0.5, f = 1.0, norm = 0.0
    for o in 0..<octaves { v += amp * vnoise(x * f, y * f, salt + o * 31); norm += amp; amp *= 0.5; f *= 2.03 }
    return v / norm
}

struct Ball { var x, y, z, r: Double }

/// A billow: one big round body, lobes heaped on its top and shoulders, and small bumps all over the outline.
func billow(_ seed: UInt64) -> [Ball] {
    var g = RNG(s: seed)
    var b: [Ball] = [Ball(x: g.r(-0.04, 0.04), y: -0.1, z: -0.08, r: 0.34)]
    // a crowd of lobes over the face, with more over the top and the shoulders: a cauliflower grows up,
    // and sits on a flatter base
    for _ in 0..<7 {
        let a = g.r(0, 2 * Double.pi), d = g.r(0.0, 0.2)
        b.append(Ball(x: b[0].x + cos(a) * d, y: b[0].y + sin(a) * d * 0.8, z: g.r(-0.04, 0.1), r: g.r(0.16, 0.24)))
    }
    let lobes = 10 + Int(g.u() * 4)
    for _ in 0..<lobes {
        let a = g.r(0.05, 0.95) * Double.pi + g.r(-0.25, 0.25)
        let d = g.r(0.26, 0.44)
        b.append(Ball(x: b[0].x + cos(a) * d * 1.05, y: b[0].y + sin(a) * d, z: g.r(-0.12, 0.06), r: g.r(0.15, 0.27)))
    }
    for i in 0..<3 {   // a few low down at the sides, so the bottom is not a clean circle
        let side = i % 2 == 0 ? -1.0 : 1.0
        b.append(Ball(x: side * g.r(0.3, 0.45), y: g.r(-0.38, -0.2), z: g.r(-0.1, 0.05), r: g.r(0.15, 0.22)))
    }
    // a second, smaller generation of lobes on the first: billows grow on billows
    let first = b
    for _ in 0..<16 {
        let host = first[1 + Int(g.u() * Double(first.count - 1))]
        let a = g.r(0, 2 * Double.pi)
        let r = g.r(0.09, 0.16)
        let d = host.r - r * g.r(0.3, 0.8)
        if host.y + sin(a) * d < -0.4 { continue }
        b.append(Ball(x: host.x + cos(a) * d, y: host.y + sin(a) * d, z: host.z + g.r(-0.06, 0.04), r: r))
    }
    // small bumps on the outline of what is there so far
    let big = b
    for _ in 0..<46 {
        let host = big[Int(g.u() * Double(big.count))]
        let a = g.r(0, 2 * Double.pi)
        let r = g.r(0.045, 0.11)
        let d = host.r - r * g.r(0.2, 0.7)
        let p = Ball(x: host.x + cos(a) * d, y: host.y + sin(a) * d, z: host.z + g.r(-0.05, 0.05), r: r)
        if p.y < -0.42 { continue }   // keep the base fairly calm
        b.append(p)
    }
    // keep everything inside the cell, with room for the soft edge
    var lo = 1e9, hi = -1e9, bot = 1e9, top = -1e9
    for s in b { lo = min(lo, s.x - s.r); hi = max(hi, s.x + s.r); bot = min(bot, s.y - s.r); top = max(top, s.y + s.r) }
    let scale = 1.82 / max(hi - lo, top - bot), cx = (lo + hi) * 0.5, cy = (bot + top) * 0.5
    return b.map { Ball(x: ($0.x - cx) * scale, y: ($0.y - cy) * scale, z: $0.z * scale, r: $0.r * scale) }
}

var px = [UInt8](repeating: 0, count: size * size * 4)
for c in 0..<(cells * cells) {
    let balls = billow(UInt64(1000 + c * 7919))
    let ox = (c % cells) * cell, oy = (c / cells) * cell
    let salt = 100 + c * 17
    // 1. the height of the billow's front surface at every pixel: the spheres blended together with a smooth
    //    maximum, so lobes melt into each other instead of meeting in hard creases, plus churned detail
    let k = 0.022
    var hgt = [Double](repeating: 0, count: cell * cell), cover = [Double](repeating: 0, count: cell * cell)
    for j in 0..<cell {
        for i in 0..<cell {
            let x = (Double(i) + 0.5) / Double(cell) * 2 - 1
            let y = (Double(j) + 0.5) / Double(cell) * 2 - 1
            var sum = 0.0, inside = -1e9
            for s in balls {
                let dx = x - s.x, dy = y - s.y, d2 = dx * dx + dy * dy
                inside = max(inside, s.r - sqrt(d2))
                if d2 < s.r * s.r { sum += exp((s.z + 0.5 + sqrt(s.r * s.r - d2)) / k) }
            }
            var h = sum > 0 ? max(0, k * log(sum) - 0.5) : 0
            // domain-warped noise: curdled, churning lumps on the surface, strongest where the cloud is thick
            let wx = x + 0.12 * (fbm(x * 3 + 7, y * 3, salt + 3) - 0.5), wy = y + 0.12 * (fbm(x * 3, y * 3 + 5, salt + 4) - 0.5)
            let lumps = fbm(wx * 5, wy * 5, salt) - 0.5, fine = fbm(wx * 16, wy * 16, salt + 9) - 0.5
            h += (0.045 * lumps + 0.008 * fine) * min(1, h / 0.15)
            hgt[j * cell + i] = max(0, h)
            cover[j * cell + i] = inside
        }
    }
    // the height field blurred (a box blur, twice across and down): a crease is where the surface dips below it
    func blur(_ a: [Double], _ r: Int) -> [Double] {
        var t = a, o = a
        for j in 0..<cell { var acc = 0.0; var cnt = 0.0
            for i in -r..<(cell + r) {
                if i + r < cell && i + r >= 0 { acc += a[j * cell + i + r]; cnt += 1 }
                if i - r - 1 >= 0 && i - r - 1 < cell { acc -= a[j * cell + i - r - 1]; cnt -= 1 }
                if i >= 0 && i < cell { t[j * cell + i] = acc / max(cnt, 1) } } }
        for i in 0..<cell { var acc = 0.0; var cnt = 0.0
            for j in -r..<(cell + r) {
                if j + r < cell && j + r >= 0 { acc += t[(j + r) * cell + i]; cnt += 1 }
                if j - r - 1 >= 0 && j - r - 1 < cell { acc -= t[(j - r - 1) * cell + i]; cnt -= 1 }
                if j >= 0 && j < cell { o[j * cell + i] = acc / max(cnt, 1) } } }
        return o
    }
    let soft = blur(blur(hgt, 14), 14), wide = blur(blur(hgt, 40), 40)
    // 2. normals from the height field's slope; alpha from the outline, torn by noise
    let px2 = 2.0 / Double(cell)
    for j in 0..<cell {
        for i in 0..<cell {
            let x = (Double(i) + 0.5) / Double(cell) * 2 - 1
            let y = (Double(j) + 0.5) / Double(cell) * 2 - 1
            func H(_ a: Int, _ b: Int) -> Double { hgt[min(cell - 1, max(0, b)) * cell + min(cell - 1, max(0, a))] }
            let dhx = (H(i + 1, j) - H(i - 1, j)) / (2 * px2), dhy = (H(i, j + 1) - H(i, j - 1)) / (2 * px2)
            var nx = -dhx, ny = -dhy, nz = 1.0
            let len = sqrt(nx * nx + ny * ny + nz * nz); nx /= len; ny /= len; nz /= len
            let tear = fbm(x * 6 + 3.1, y * 6 - 1.7, salt + 7) - 0.5
            let edge = cover[j * cell + i] + tear * 0.11
            var a = min(1, max(0, (edge + 0.02) / 0.13))
            a = a * a * (3 - 2 * a)
            let h = H(i, j), idx = j * cell + i
            let thick = min(1, max(0, 0.2 + 0.95 * min(1, h / 0.55) + 4.0 * (h - soft[idx]) + 1.5 * (h - wide[idx])))
            let o = ((oy + (cell - 1 - j)) * size + (ox + i)) * 4   // image rows run top-down
            px[o] = UInt8(max(0, min(255, (nx * 0.5 + 0.5) * 255)))
            px[o + 1] = UInt8(max(0, min(255, (ny * 0.5 + 0.5) * 255)))
            px[o + 2] = UInt8(max(0, min(255, thick * 255)))
            px[o + 3] = UInt8(max(0, min(255, a * 255)))
        }
    }
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                           isPlanar: false, colorSpaceName: .deviceRGB, bitmapFormat: .alphaNonpremultiplied, bytesPerRow: size * 4, bitsPerPixel: 32)!
_ = px.withUnsafeBufferPointer { memcpy(rep.bitmapData!, $0.baseAddress!, size * size * 4) }
try! FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)
let path = outDir + "/PuffAtlas.png"
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
print("wrote", path)
