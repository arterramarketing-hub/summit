// Synthesises every sound in Summit and writes them as 16-bit mono WAVs.
// Build and run:  swiftc -O Tools/MakeAudio.swift -o /tmp/makeaudio && /tmp/makeaudio Assets/Summit/Resources/Audio
// Everything here is made from noise, oscillators, filters and resonators - no recordings.
import Foundation

let SR = 44100.0
let TAU = 2.0 * Double.pi

// ------------------------------------------------------------------ basics

struct RNG {
    var s: UInt64
    init(_ seed: UInt64) { s = seed &* 0x9E3779B97F4A7C15 | 1 }
    mutating func u() -> Double { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return Double(s >> 11) / Double(UInt64(1) << 53) }
    mutating func bi() -> Double { u() * 2 - 1 }
    mutating func r(_ a: Double, _ b: Double) -> Double { a + (b - a) * u() }
}

func samples(_ sec: Double) -> Int { Int(sec * SR) }
func buf(_ sec: Double) -> [Double] { [Double](repeating: 0, count: samples(sec)) }

func white(_ n: Int, _ rng: inout RNG) -> [Double] { (0..<n).map { _ in rng.bi() } }

func pink(_ n: Int, _ rng: inout RNG) -> [Double] {
    var b0 = 0.0, b1 = 0.0, b2 = 0.0, b3 = 0.0, b4 = 0.0, b5 = 0.0, b6 = 0.0
    var o = [Double](repeating: 0, count: n)
    for i in 0..<n {
        let w = rng.bi()
        b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.96900 * b2 + w * 0.1538520
        b3 = 0.86650 * b3 + w * 0.3104856; b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980
        o[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11
        b6 = w * 0.115926
    }
    return o
}

func brown(_ n: Int, _ rng: inout RNG) -> [Double] {
    var b = 0.0
    var o = [Double](repeating: 0, count: n)
    for i in 0..<n { b = b * 0.996 + rng.bi() * 0.06; o[i] = b }
    return o
}

/// Smooth random curve 0..1, changing about `hz` times a second.
func drift(_ n: Int, _ hz: Double, _ rng: inout RNG) -> [Double] {
    let step = max(1, Int(SR / hz))
    let pts = (0...(n / step + 2)).map { _ in rng.u() }
    var o = [Double](repeating: 0, count: n)
    for i in 0..<n {
        let k = i / step, f = Double(i % step) / Double(step)
        let s = (1 - cos(f * Double.pi)) * 0.5
        o[i] = pts[k] * (1 - s) + pts[k + 1] * s
    }
    return o
}

// RBJ biquad, coefficients refreshed as the parameters move
struct Biquad {
    enum Kind { case lp, hp, bp, peak }
    var kind: Kind
    var b0 = 0.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0
    var x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0
    init(_ k: Kind, _ f: Double, _ q: Double, _ gainDb: Double = 0) { kind = k; set(f, q, gainDb) }
    mutating func set(_ f0: Double, _ q: Double, _ gainDb: Double = 0) {
        let f = min(max(f0, 10), SR * 0.45)
        let w = TAU * f / SR, c = cos(w), s = sin(w), al = s / (2 * q)
        var a0 = 1.0
        switch kind {
        case .lp: b0 = (1 - c) / 2; b1 = 1 - c; b2 = (1 - c) / 2; a0 = 1 + al; a1 = -2 * c; a2 = 1 - al
        case .hp: b0 = (1 + c) / 2; b1 = -(1 + c); b2 = (1 + c) / 2; a0 = 1 + al; a1 = -2 * c; a2 = 1 - al
        case .bp: b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * c; a2 = 1 - al
        case .peak:
            let A = pow(10, gainDb / 40)
            b0 = 1 + al * A; b1 = -2 * c; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * c; a2 = 1 - al / A
        }
        b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0
    }
    mutating func run(_ x: Double) -> Double {
        let y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2 = x1; x1 = x; y2 = y1; y1 = y
        return y
    }
}

func filt(_ x: [Double], _ k: Biquad.Kind, _ f: Double, _ q: Double = 0.707) -> [Double] {
    var b = Biquad(k, f, q)
    return x.map { b.run($0) }
}

/// Filter with a moving frequency: f(i) gives the centre/cutoff at sample i.
func sweep(_ x: [Double], _ k: Biquad.Kind, _ q: Double, _ f: (Int) -> Double) -> [Double] {
    var b = Biquad(k, f(0), q)
    var o = x
    for i in 0..<x.count {
        if i % 16 == 0 { b.set(f(i), q) }
        o[i] = b.run(x[i])
    }
    return o
}

/// A bank of decaying resonant modes (a struck or scraped object).
func modes(_ x: [Double], _ freqs: [Double], _ t60: [Double], _ gains: [Double]) -> [Double] {
    var o = [Double](repeating: 0, count: x.count)
    for m in 0..<freqs.count {
        let r = exp(-6.9 / (t60[m] * SR)), w = TAU * freqs[m] / SR
        let c1 = 2 * r * cos(w), c2 = -r * r
        var y1 = 0.0, y2 = 0.0
        for i in 0..<x.count {
            let y = c1 * y1 + c2 * y2 + x[i] * (1 - r)
            y2 = y1; y1 = y
            o[i] += y * gains[m]
        }
    }
    return o
}

func add(_ a: inout [Double], _ b: [Double], _ g: Double = 1, at: Double = 0) {
    let off = samples(at)
    for i in 0..<b.count where i + off < a.count && i + off >= 0 { a[i + off] += b[i] * g }
}

func mul(_ a: [Double], _ f: (Int) -> Double) -> [Double] { a.enumerated().map { $0.element * f($0.offset) } }

func t(_ i: Int) -> Double { Double(i) / SR }

/// Exponential decay envelope with a short attack.
func env(_ i: Int, attack: Double, decay: Double) -> Double {
    let s = t(i)
    return s < attack ? s / attack : exp(-(s - attack) / decay)
}

/// Tiny bursts of noise at random, the crunch of snow crystals breaking.
func grains(_ n: Int, rate: (Int) -> Double, len: ClosedRange<Double>, _ rng: inout RNG) -> [Double] {
    var o = [Double](repeating: 0, count: n)
    var i = 0
    while i < n {
        if rng.u() < rate(i) / SR * 16 {
            let L = samples(rng.r(len.lowerBound, len.upperBound))
            let a = pow(rng.u(), 2)
            for j in 0..<L where i + j < n {
                o[i + j] += rng.bi() * a * exp(-Double(j) / (Double(L) * 0.3))
            }
        }
        i += 16
    }
    return o
}

/// A dropping sine thump (a body or board meeting the snow).
func thump(_ sec: Double, f0: Double, f1: Double, decay: Double) -> [Double] {
    var o = buf(sec); var ph = 0.0
    for i in 0..<o.count {
        let f = f1 + (f0 - f1) * exp(-t(i) / 0.04)
        ph += TAU * f / SR
        o[i] = sin(ph) * env(i, attack: 0.002, decay: decay)
    }
    return o
}

/// An FM bell at frequency f.
func bell(_ sec: Double, _ f: Double, decay: Double = 0.8, bright: Double = 2.5) -> [Double] {
    var o = buf(sec)
    for i in 0..<o.count {
        let s = t(i), idx = bright * exp(-s / 0.25) + 0.3
        let m = sin(TAU * f * 3.5 * s) * idx
        o[i] = (sin(TAU * f * s + m) + 0.25 * sin(TAU * f * 2.01 * s)) * env(i, attack: 0.003, decay: decay)
    }
    return o
}

/// Mono Freeverb-style room, mixed in at `wet`.
func reverb(_ x: [Double], wet: Double, room: Double = 0.82, damp: Double = 0.3, tail: Double = 0.8) -> [Double] {
    var input = x + [Double](repeating: 0, count: samples(tail))
    let combs = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617], aps = [556, 441, 341, 225]
    var out = [Double](repeating: 0, count: input.count)
    for L in combs {
        var b = [Double](repeating: 0, count: L); var k = 0; var lp = 0.0
        for i in 0..<input.count {
            let y = b[k]
            lp = y * (1 - damp) + lp * damp
            b[k] = input[i] * 0.015 + lp * room
            out[i] += y
            k = (k + 1) % L
        }
    }
    for L in aps {
        var b = [Double](repeating: 0, count: L); var k = 0
        for i in 0..<out.count {
            let bo = b[k], y = -out[i] + bo
            b[k] = out[i] + bo * 0.5
            out[i] = y
            k = (k + 1) % L
        }
    }
    for i in 0..<input.count { input[i] = input[i] * (1 - wet * 0.5) + out[i] * wet }
    return input
}

func normalize(_ x: [Double], _ peak: Double) -> [Double] {
    let m = x.reduce(1e-9) { max($0, abs($1)) }
    return x.map { $0 * peak / m }
}

func fade(_ x: [Double], in fi: Double = 0.002, out fo: Double = 0.02) -> [Double] {
    var o = x
    let a = samples(fi), b = samples(fo)
    for i in 0..<min(a, o.count) { o[i] *= Double(i) / Double(a) }
    for i in 0..<min(b, o.count) { o[o.count - 1 - i] *= Double(i) / Double(b) }
    return o
}

/// Make a seamless loop: the extra `xf` seconds at the end are crossfaded (equal power) into the start.
func loop(_ x: [Double], xf: Double) -> [Double] {
    let n = samples(xf), L = x.count - n
    var o = Array(x[0..<L])
    for i in 0..<n {
        let f = Double(i) / Double(n)
        o[i] = x[i] * sin(f * Double.pi / 2) + x[L + i] * cos(f * Double.pi / 2)
    }
    return o
}

func softclip(_ x: [Double], _ drive: Double) -> [Double] { x.map { tanh($0 * drive) } }

var outDir = "."
func write(_ name: String, _ x0: [Double]) {
    // take out DC and sub-sonic drift (brown noise wanders), then re-level to the same peak
    let pk = x0.reduce(1e-9) { max($0, abs($1)) }
    let x = normalize(filt(x0, .hp, 25, 0.6), pk)
    let n = x.count
    var d = Data()
    func u32(_ v: UInt32) { var v = v.littleEndian; d.append(Data(bytes: &v, count: 4)) }
    func u16(_ v: UInt16) { var v = v.littleEndian; d.append(Data(bytes: &v, count: 2)) }
    d.append("RIFF".data(using: .ascii)!); u32(UInt32(36 + n * 2)); d.append("WAVE".data(using: .ascii)!)
    d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(1); u32(44100); u32(88200); u16(2); u16(16)
    d.append("data".data(using: .ascii)!); u32(UInt32(n * 2))
    var pcm = [Int16](repeating: 0, count: n)
    for i in 0..<n { pcm[i] = Int16(max(-1, min(1, x[i])) * 32000) }
    pcm.withUnsafeBufferPointer { d.append(Data(buffer: $0)) }
    try! d.write(to: URL(fileURLWithPath: outDir + "/" + name + ".wav"))
    print(name, String(format: "%.2fs", Double(n) / SR))
}

// ------------------------------------------------------------------ loops

func windLow() -> [Double] {
    var r = RNG(1); let n = samples(9)
    let b = brown(n, &r), d1 = drift(n, 0.15, &r), d2 = drift(n, 0.22, &r)
    var x = sweep(b, .lp, 0.9) { 140 + 260 * d1[$0] }
    x = mul(x) { 0.55 + 0.45 * pow(d2[$0], 1.5) }
    return normalize(loop(x, xf: 1), 0.8)
}

func windHigh() -> [Double] {
    var r = RNG(2); let n = samples(9)
    let p = pink(n, &r), d1 = drift(n, 0.11, &r), d2 = drift(n, 0.07, &r), d3 = drift(n, 0.09, &r), g = drift(n, 0.25, &r)
    let a = sweep(p, .bp, 4) { 500 + 700 * d1[$0] }
    let b = sweep(p, .bp, 7) { 1400 + 1600 * d2[$0] }     // the whistle
    let c = sweep(p, .bp, 3) { 3000 + 2500 * d3[$0] }
    let h = filt(white(n, &r), .hp, 5000)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = (0.5 * a[i] + 0.35 * b[i] + 0.15 * c[i] + 0.03 * h[i]) * (0.4 + 0.6 * g[i] * g[i]) }
    return normalize(loop(x, xf: 1), 0.75)
}

func glide() -> [Double] {
    var r = RNG(3); let n = samples(4.5)
    let w = white(n, &r), am = drift(n, 12, &r)
    let hiss = filt(filt(w, .bp, 1800, 0.6), .lp, 7000)
    let gr = filt(grains(n, rate: { _ in 220 }, len: 0.0015...0.005, &r), .bp, 2800, 1.2)
    let body = filt(pink(n, &r), .lp, 400)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = (0.5 * hiss[i] + 0.9 * gr[i] + 0.25 * body[i]) * (0.8 + 0.2 * am[i]) }
    return normalize(loop(x, xf: 0.5), 0.7)
}

func carve() -> [Double] {
    var r = RNG(4); let n = samples(4.5)
    let w = white(n, &r), rip = drift(n, 45, &r), swell = drift(n, 3, &r)
    let edge = filt(w, .bp, 3200, 1.3), shimmer = filt(w, .hp, 6500)
    let gr = filt(grains(n, rate: { _ in 900 }, len: 0.0008...0.003, &r), .bp, 4000, 0.9)
    let whoosh = filt(pink(n, &r), .bp, 500, 0.8)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = (0.6 * edge[i] + 0.1 * shimmer[i] + 0.8 * gr[i]) * (0.6 + 0.4 * rip[i]) + 0.35 * whoosh[i] * (0.6 + 0.4 * swell[i]) }
    x = filt(x, .lp, 10000, 0.6)   // no fizz above the snow
    return normalize(loop(x, xf: 0.5), 0.75)
}

func skid() -> [Double] {
    var r = RNG(5); let n = samples(3.5)
    let w = white(n, &r), fd = drift(n, 1, &r)
    var ph = 0.0
    let gate: [Double] = (0..<n).map { i in ph += TAU * (22 + 6 * fd[i]) / SR; return pow(0.5 + 0.5 * sin(ph), 3) }
    let scrape = filt(w, .bp, 1100, 0.9)
    let gr = filt(grains(n, rate: { _ in 500 }, len: 0.001...0.004, &r), .bp, 2000, 1)
    let rumble = filt(brown(n, &r), .lp, 200)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = scrape[i] * (0.25 + 0.75 * gate[i]) + 0.8 * gr[i] + 0.3 * rumble[i] * gate[i] }
    return normalize(loop(x, xf: 0.5), 0.75)
}

func powder() -> [Double] {
    var r = RNG(6); let n = samples(4.5)
    let p = pink(n, &r), sw = drift(n, 1.5, &r)
    let soft = filt(p, .lp, 1100, 0.7)
    let gr = filt(grains(n, rate: { _ in 300 }, len: 0.004...0.012, &r), .lp, 1400)
    let low = filt(brown(n, &r), .lp, 150)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = soft[i] * (0.6 + 0.4 * sw[i]) + 0.7 * gr[i] + 0.3 * low[i] }
    return normalize(loop(x, xf: 0.5), 0.7)
}

func grindRail() -> [Double] {
    var r = RNG(7); let n = samples(2.5)
    let am = drift(n, 9, &r)
    var ex = white(n, &r).map { $0 * 0.15 }
    add(&ex, grains(n, rate: { _ in 120 }, len: 0.0005...0.002, &r), 1.5)
    let ring = modes(ex, [742, 1253, 2087, 2969, 4110, 5390], [0.9, 0.7, 0.5, 0.4, 0.3, 0.25], [1, 0.7, 0.55, 0.45, 0.4, 0.35])
    let hiss = filt(white(n, &r), .hp, 3000)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = (ring[i] * 6 + 0.2 * hiss[i]) * (0.85 + 0.15 * am[i]) }
    return normalize(loop(x, xf: 0.5), 0.7)
}

func grindBox() -> [Double] {
    var r = RNG(8); let n = samples(2.5)
    var ex = filt(white(n, &r), .bp, 600, 0.7)
    add(&ex, grains(n, rate: { _ in 90 }, len: 0.001...0.003, &r), 1.2)
    let body = modes(ex, [180, 410, 870, 1530], [0.15, 0.12, 0.1, 0.08], [1, 0.8, 0.5, 0.3])
    let hiss = filt(white(n, &r), .bp, 2500, 0.8)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = body[i] * 5 + 0.4 * hiss[i] }
    return normalize(loop(x, xf: 0.5), 0.7)
}

func avalanche() -> [Double] {
    var r = RNG(9); let n = samples(7)
    let d = drift(n, 0.5, &r), d2 = drift(n, 2, &r)
    let deep = filt(brown(n, &r), .lp, 90, 0.7), mid = filt(brown(n, &r), .lp, 400)
    let roar = filt(pink(n, &r), .bp, 250, 0.6)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = deep[i] + 0.35 * mid[i] * d2[i] + 0.3 * roar[i] }
    var at = 0.05
    while at < 6.8 {
        add(&x, thump(0.3, f0: r.r(60, 80), f1: r.r(35, 45), decay: 0.12), r.r(0.3, 1.0), at: at)
        at += r.r(0.12, 0.4)
    }
    x = mul(x) { 0.75 + 0.25 * d[$0] }
    return normalize(loop(x, xf: 1), 0.85)
}

func boost() -> [Double] {
    var r = RNG(10); let n = samples(2.5)
    let fizz = filt(grains(n, rate: { _ in 3000 }, len: 0.0002...0.0008, &r), .hp, 4000)
    let rush = filt(pink(n, &r), .bp, 1800, 0.8)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = 0.6 * fizz[i] + 0.4 * rush[i] * (0.85 + 0.15 * sin(TAU * 6 * t(i))) }
    return normalize(loop(x, xf: 0.5), 0.6)
}

func ambience() -> [Double] {
    var r = RNG(11); let n = samples(13)
    let g = drift(n, 0.08, &r)
    let air = filt(pink(n, &r), .lp, 700), high = filt(white(n, &r), .hp, 3000)
    var x = [Double](repeating: 0, count: n)
    for i in 0..<n { x[i] = air[i] * (0.3 + 0.7 * g[i] * g[i]) + 0.04 * high[i] }
    return normalize(loop(x, xf: 1), 0.5)
}

/// A voice through formants: f0(i) is the pitch, used for the bear.
func voice(_ n: Int, f0: (Int) -> Double, formants: [(Double, Double, Double)], breath: Double, _ rng: inout RNG) -> [Double] {
    var ph = 0.0
    let src: [Double] = (0..<n).map { i in
        ph += f0(i) / SR; if ph >= 1 { ph -= 1 }
        return (2 * ph - 1) + 0.3 * rng.bi() * 0.2
    }
    var x = [Double](repeating: 0, count: n)
    for (f, q, g) in formants { add(&x, filt(src, .bp, f, q), g) }
    add(&x, filt(white(n, &rng), .bp, 900, 1), breath)
    return x
}

func bearGrowl() -> [Double] {
    var r = RNG(12); let n = samples(3.5)
    let d = drift(n, 2, &r)
    var x = voice(n, f0: { 62 + 10 * d[$0] + 5 * sin(TAU * 0.33 * t($0)) },
                  formants: [(320, 3, 1), (750, 4, 0.6), (1900, 5, 0.25)], breath: 0.15, &r)
    x = mul(x) { i in let p = 0.5 + 0.5 * sin(TAU * 0.66 * t(i)); return 0.3 + 0.7 * p * p }
    return normalize(loop(softclip(normalize(x, 1), 2), xf: 0.5), 0.8)
}

func bearSteps() -> [Double] {
    var r = RNG(13); let n = samples(2.0)
    var x = [Double](repeating: 0, count: n)
    let cycle = 2.0 / 3.0
    for c in 0..<3 {
        for (k, off) in [0.0, 0.08, 0.30, 0.38].enumerated() {
            let at = Double(c) * cycle + off
            add(&x, thump(0.2, f0: 90, f1: 55, decay: 0.07), k < 2 ? 1 : 0.8, at: at)
            let m = samples(0.12)
            var cr = filt(white(m, &r), .lp, 2500); cr = mul(cr) { env($0, attack: 0.002, decay: 0.035) }
            add(&x, cr, 0.5, at: at)
        }
    }
    return normalize(x, 0.8)
}

// ------------------------------------------------------------------ one-shots

func bearRoar() -> [Double] {
    var r = RNG(14); let n = samples(1.9)
    func f0(_ i: Int) -> Double {
        let s = t(i)
        if s < 0.35 { return 85 + (150 - 85) * s / 0.35 }
        if s < 1.0 { return 150 + (120 - 150) * (s - 0.35) / 0.65 }
        return 120 + (70 - 120) * min(1, (s - 1.0) / 0.6)
    }
    var x = voice(n, f0: f0, formants: [(450, 2.5, 1), (1100, 3, 0.7), (2400, 4, 0.35)], breath: 0.35, &r)
    x = mul(x) { i in let s = t(i); return min(1, s / 0.12) * (s > 1.3 ? exp(-(s - 1.3) / 0.2) : 1) * (0.7 + 0.3 * sin(TAU * 33 * s)) }
    x = softclip(normalize(x, 1), 2.5)
    return normalize(fade(reverb(x, wet: 0.25), out: 0.3), 0.9)
}

func pop() -> [Double] {
    var r = RNG(15); let n = samples(0.35)
    var x = thump(0.35, f0: 140, f1: 60, decay: 0.09)
    var b = filt(white(n, &r), .bp, 1600, 0.8); b = mul(b) { env($0, attack: 0.002, decay: 0.12) }
    add(&x, b, 0.8)
    add(&x, mul(filt(grains(n, rate: { _ in 1500 }, len: 0.001...0.003, &r), .bp, 2500)) { env($0, attack: 0.001, decay: 0.1) }, 1)
    add(&x, filt(white(samples(0.002), &r), .hp, 4000), 0.4)
    return normalize(fade(x), 0.9)
}

func whoosh(_ sec: Double, lo: Double, hi: Double, end: Double, q: Double, peak: Double, seed: UInt64) -> [Double] {
    var r = RNG(seed); let n = samples(sec)
    let w = white(n, &r)
    var x = sweep(w, .bp, q) { i in
        let f = t(i) / sec
        return f < peak ? lo + (hi - lo) * f / peak : hi + (end - hi) * (f - peak) / (1 - peak)
    }
    x = mul(x) { i in let f = t(i) / sec; let e = f < peak ? f / peak : (1 - f) / (1 - peak); return e * e }
    return normalize(fade(x), 0.8)
}

func land(_ seed: UInt64, hard: Bool) -> [Double] {
    var r = RNG(seed)
    let sec = hard ? 0.9 : 0.5, n = samples(sec)
    var x = hard ? thump(sec, f0: 60, f1: 35, decay: 0.25) : thump(sec, f0: r.r(70, 85), f1: r.r(42, 50), decay: 0.12)
    let cd = hard ? 0.45 : 0.18
    add(&x, mul(filt(white(n, &r), .lp, hard ? 2500 : 3000)) { env($0, attack: 0.003, decay: cd) }, 0.7)
    add(&x, mul(filt(grains(n, rate: { _ in 1500 }, len: 0.001...0.004, &r), .bp, 2200)) { env($0, attack: 0.001, decay: cd) }, 1.2)
    if hard { add(&x, mul(filt(white(samples(0.08), &r), .bp, 1200, 1.5)) { env($0, attack: 0.001, decay: 0.025) }, 0.7) }
    if hard { x = reverb(x, wet: 0.1) }
    return normalize(fade(x, out: 0.05), 0.9)
}

func crash() -> [Double] {
    var r = RNG(20); let n = samples(1.4)
    var x = thump(1.4, f0: 60, f1: 35, decay: 0.2)
    for (at, g) in [(0.18, 0.6), (0.42, 0.45), (0.7, 0.3)] { add(&x, thump(0.3, f0: 75, f1: 45, decay: 0.08), g, at: at) }
    add(&x, mul(filt(white(n, &r), .lp, 4000)) { env($0, attack: 0.005, decay: 0.5) }, 0.6)
    add(&x, mul(filt(grains(n, rate: { _ in 2000 }, len: 0.001...0.005, &r), .bp, 2000)) { env($0, attack: 0.002, decay: 0.45) }, 1.3)
    for _ in 0..<3 {
        var k = [Double](repeating: 0, count: samples(0.1)); k[0] = 1
        add(&x, modes(k, [900, 2300], [0.04, 0.03], [1, 0.6]), 0.4, at: r.r(0.1, 0.6))
    }
    return normalize(fade(reverb(x, wet: 0.12), out: 0.2), 0.95)
}

func hitTree() -> [Double] {
    var r = RNG(21); let n = samples(1.4)
    var k = [Double](repeating: 0, count: n); for i in 0..<40 { k[i] = r.bi() * (1 - Double(i) / 40) }
    var x = modes(k, [190, 420, 760, 1240], [0.25, 0.18, 0.12, 0.08], [1, 0.8, 0.5, 0.3]).map { $0 * 30 }
    add(&x, thump(0.4, f0: 90, f1: 55, decay: 0.1), 0.8)
    var shower = filt(pink(n, &r), .lp, 2500)
    shower = mul(shower) { i in let s = t(i) - 0.08; return s < 0 ? 0 : min(1, s / 0.1) * exp(-s / 0.6) }
    add(&x, shower, 0.6)
    for at in [0.4, 0.7, 0.95] { add(&x, thump(0.2, f0: 110, f1: 70, decay: 0.05), 0.3, at: at) }
    return normalize(fade(x, out: 0.2), 0.95)
}

func hitRock() -> [Double] {
    var r = RNG(22); let n = samples(0.8)
    var k = [Double](repeating: 0, count: n); for i in 0..<10 { k[i] = r.bi() }
    var x = modes(k, [1300, 2650, 3900], [0.06, 0.05, 0.04], [1, 0.7, 0.5]).map { $0 * 25 }
    add(&x, thump(0.4, f0: 100, f1: 60, decay: 0.08), 0.9)
    add(&x, mul(filt(grains(n, rate: { _ in 600 }, len: 0.0005...0.002, &r), .hp, 2000)) { env($0, attack: 0.001, decay: 0.3) }, 0.8)
    return normalize(fade(x, out: 0.1), 0.95)
}

func bodyThump(_ seed: UInt64) -> [Double] {
    var r = RNG(seed); let n = samples(0.3)
    var x = thump(0.3, f0: r.r(65, 80), f1: r.r(40, 50), decay: 0.08)
    add(&x, mul(filt(white(n, &r), .lp, 2000)) { env($0, attack: 0.002, decay: 0.08) }, 0.5)
    return normalize(fade(x, out: 0.05), 0.85)
}

func clack() -> [Double] {
    var r = RNG(23); let n = samples(0.25)
    var k = [Double](repeating: 0, count: n); for i in 0..<6 { k[i] = r.bi() }
    var x = modes(k, [620, 1480, 2900], [0.05, 0.04, 0.03], [1, 0.7, 0.4]).map { $0 * 20 }
    add(&x, filt(white(samples(0.003), &r), .hp, 3000), 0.3)
    return normalize(fade(x, out: 0.05), 0.85)
}

/// A board landing on a rail: a struck metal tube.
func clank() -> [Double] {
    var r = RNG(33); let n = samples(1.0)
    var k = [Double](repeating: 0, count: n); for i in 0..<12 { k[i] = r.bi() }
    var x = modes(k, [742, 1253, 2087, 2969, 4110], [0.6, 0.45, 0.35, 0.25, 0.2], [1, 0.8, 0.6, 0.45, 0.35]).map { $0 * 20 }
    add(&x, thump(0.2, f0: 120, f1: 80, decay: 0.04), 0.4)
    return normalize(fade(x, out: 0.2), 0.8)
}

func chime() -> [Double] { normalize(fade(reverb(bell(1.4, 1046.5), wet: 0.25), out: 0.3), 0.6) }

func notes(_ fs: [Double], gap: Double, sec: Double, wet: Double, chorus: Bool = false) -> [Double] {
    var x = buf(sec)
    for (k, f) in fs.enumerated() {
        add(&x, bell(sec - Double(k) * gap, f, decay: 0.6), 1, at: Double(k) * gap)
        if chorus { add(&x, bell(sec - Double(k) * gap, f * 1.004, decay: 0.6), 0.6, at: Double(k) * gap + 0.004) }
    }
    return normalize(fade(reverb(x, wet: wet), out: 0.3), 0.6)
}

func closeCall() -> [Double] {
    var x = whoosh(0.4, lo: 2500, hi: 3000, end: 600, q: 3, peak: 0.25, seed: 24)
    var r = RNG(25)
    add(&x, filt(white(samples(0.004), &r), .hp, 3000), 0.3)
    return normalize(x, 0.8)
}

func soda() -> [Double] {
    var r = RNG(26); let n = samples(1.3)
    var k = [Double](repeating: 0, count: n); k[0] = 1; k[1] = -0.8
    var x = modes(k, [2800, 4300], [0.05, 0.04], [1, 0.5]).map { $0 * 12 }
    add(&x, filt(white(samples(0.004), &r), .hp, 2000), 0.6)
    add(&x, mul(filt(white(n, &r), .hp, 2500)) { env($0, attack: 0.005, decay: 0.35) }, 0.6, at: 0.01)
    add(&x, mul(filt(grains(n, rate: { _ in 2000 }, len: 0.0002...0.0008, &r), .hp, 4000)) { i in exp(-t(i) / 0.6) }, 1.2, at: 0.05)
    add(&x, bell(1.0, 1568, decay: 0.4), 0.25, at: 0.25)
    add(&x, bell(0.9, 2349, decay: 0.4), 0.2, at: 0.33)
    return normalize(fade(x, out: 0.2), 0.85)
}

func shieldHit() -> [Double] {
    var r = RNG(27); let n = samples(0.9)
    var x = buf(0.9)
    for (f, d, g) in [(520.0, 0.6, 1.0), (1310.0, 0.4, 0.6), (2470.0, 0.25, 0.35)] {
        var ph = 0.0
        for i in 0..<n { ph += TAU * f * (0.94 + 0.06 * exp(-t(i) / 0.1)) / SR; x[i] += sin(ph) * g * env(i, attack: 0.002, decay: d) }
    }
    add(&x, filt(white(samples(0.02), &r), .hp, 3000), 0.5)
    return normalize(fade(reverb(x, wet: 0.3), out: 0.3), 0.75)
}

func click() -> [Double] {
    var r = RNG(28); var x = buf(0.08)
    for i in 0..<x.count { x[i] = sin(TAU * 2200 * t(i)) * env(i, attack: 0.0005, decay: 0.012) }
    add(&x, filt(white(samples(0.001), &r), .hp, 4000), 0.3)
    return normalize(fade(x, out: 0.01), 0.6)
}

func buried() -> [Double] {
    var r = RNG(29); let n = samples(2.5)
    var x = thump(2.5, f0: 45, f1: 30, decay: 0.6)
    add(&x, mul(filt(brown(n, &r), .lp, 500)) { env($0, attack: 0.05, decay: 0.9) }, 1.2)
    add(&x, mul(filt(grains(n, rate: { _ in 1200 }, len: 0.002...0.006, &r), .lp, 2000)) { env($0, attack: 0.02, decay: 0.7) }, 1)
    return normalize(fade(reverb(x, wet: 0.2), out: 0.4), 0.95)
}

func crevasse() -> [Double] {
    var r = RNG(30); let n = samples(1.4)
    var x = buf(1.4)
    var at = 0.0
    while at < 0.35 {
        var k = [Double](repeating: 0, count: samples(0.2)); for i in 0..<8 { k[i] = r.bi() }
        add(&x, modes(k, [r.r(2500, 3200), r.r(4300, 5000)], [0.12, 0.08], [1, 0.6]), r.r(4, 10), at: at)
        at += r.r(0.02, 0.09)
    }
    add(&x, whoosh(1.0, lo: 1200, hi: 900, end: 300, q: 1.5, peak: 0.2, seed: 31), 0.5, at: 0.15)
    add(&x, thump(0.3, f0: 80, f1: 45, decay: 0.08), 0.8, at: 1.05)
    return normalize(fade(reverb(x, wet: 0.35), out: 0.3), 0.9)
}

func crow(_ seed: UInt64, f0: Double, f1: Double) -> [Double] {
    var r = RNG(seed)
    var x = buf(0.9)
    for (at, k) in [(0.0, 1.0), (0.4, 0.92)] {
        let n = samples(0.28)
        var ph = 0.0
        var src = [Double](repeating: 0, count: n)
        for i in 0..<n {
            let s = t(i) / 0.28, f = (f0 + (f1 - f0) * s) * k
            ph += f / SR; if ph >= 1 { ph -= 1 }
            src[i] = ((2 * ph - 1) * 0.7 + (ph < 0.5 ? 0.3 : -0.3)) * (0.5 + 0.5 * sin(TAU * 75 * t(i))) + 0.1 * r.bi()
        }
        var v = filt(src, .bp, 1500, 2)
        add(&v, filt(src, .bp, 2600, 3), 0.6)
        v = mul(v) { i in min(1, t(i) / 0.015) * min(1, (0.28 - t(i)) / 0.06) }
        add(&x, v, 1, at: at)
    }
    return normalize(fade(reverb(x, wet: 0.2), out: 0.2), 0.8)
}

func flap() -> [Double] {
    var r = RNG(32); var x = buf(0.6)
    for k in 0..<5 {
        let n = samples(0.06)
        add(&x, mul(filt(pink(n, &r), .bp, 900, 0.7)) { env($0, attack: 0.008, decay: 0.02) }, 1 - Double(k) * 0.15, at: Double(k) / 9)
    }
    return normalize(fade(x), 0.7)
}

func chirp(_ seed: UInt64) -> [Double] {
    var r = RNG(seed); var x = buf(0.6)
    var at = 0.0
    let count = 2 + Int(r.u() * 3)
    for _ in 0..<count {
        let len = r.r(0.04, 0.09), up = r.u() < 0.6, a = r.r(3200, 4200), b = r.r(4800, 6000)
        let n = samples(len); var ph = 0.0
        var v = [Double](repeating: 0, count: n)
        for i in 0..<n {
            let s = t(i) / len, f = (up ? a + (b - a) * s : b + (a - b) * s) + 120 * sin(TAU * 30 * t(i))
            ph += TAU * f / SR
            v[i] = sin(ph) * sin(Double.pi * s)
        }
        add(&x, v, r.r(0.6, 1), at: at)
        at += len + r.r(0.03, 0.08)
    }
    return normalize(fade(reverb(x, wet: 0.15), out: 0.1), 0.6)
}

/// A bone giving way: two or three dry snaps, a gritty crackle and a dull knock under them.
func boneCrack(_ seed: UInt64) -> [Double] {
    var r = RNG(seed); let n = samples(0.6)
    var x = buf(0.6)
    var at = 0.0
    for _ in 0..<(2 + Int(r.u() * 2)) {
        var k = [Double](repeating: 0, count: samples(0.08))
        for i in 0..<samples(0.0015) { k[i] = r.bi() }
        let snap = modes(filt(k, .hp, 1500), [r.r(1600, 2000), r.r(3100, 3700), r.r(4800, 5600)], [0.025, 0.018, 0.012], [1, 0.8, 0.5])
        add(&x, snap, r.r(14, 22), at: at)
        add(&x, filt(k, .hp, 2500), 0.8, at: at)
        at += r.r(0.012, 0.035)
    }
    add(&x, mul(filt(grains(n, rate: { _ in 6000 }, len: 0.0003...0.0015, &r), .bp, 2600, 0.8)) { i in env(i, attack: 0.001, decay: 0.04) }, 1.2)
    add(&x, thump(0.3, f0: 120, f1: 70, decay: 0.06), 0.7)
    return normalize(fade(reverb(x, wet: 0.08, tail: 0.3), out: 0.1), 0.95)
}

// ------------------------------------------------------------------ write them all

if CommandLine.arguments.count > 1 { outDir = CommandLine.arguments[1] }
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

write("wind_low", windLow())
write("wind_high", windHigh())
write("glide", glide())
write("carve", carve())
write("skid", skid())
write("powder", powder())
write("grind_rail", grindRail())
write("grind_box", grindBox())
write("avalanche", avalanche())
write("boost", boost())
write("ambience", ambience())
write("bear_growl", bearGrowl())
write("bear_steps", bearSteps())
write("bear_roar", bearRoar())
write("pop", pop())
write("whoosh", whoosh(0.7, lo: 300, hi: 1800, end: 700, q: 1.2, peak: 0.45, seed: 16))
write("spin", whoosh(0.32, lo: 600, hi: 2600, end: 1200, q: 2, peak: 0.4, seed: 17))
write("land_1", land(40, hard: false))
write("land_2", land(41, hard: false))
write("land_3", land(42, hard: false))
write("land_hard", land(43, hard: true))
write("crash", crash())
write("hit_tree", hitTree())
write("hit_rock", hitRock())
write("thump_1", bodyThump(50))
write("thump_2", bodyThump(51))
write("thump_3", bodyThump(52))
write("clack", clack())
write("clank", clank())
write("chime", chime())
write("trick_big", notes([1046.5, 1318.5, 1760], gap: 0.08, sec: 1.6, wet: 0.3))
write("close_call", closeCall())
write("soda", soda())
write("shield_get", notes([1046.5, 1318.5, 1568, 2093], gap: 0.07, sec: 1.3, wet: 0.35, chorus: true))
write("shield_hit", shieldHit())
write("click", click())
write("play", notes([784, 1175], gap: 0.09, sec: 0.8, wet: 0.25))
write("buried", buried())
write("crevasse", crevasse())
write("crow_1", crow(60, f0: 620, f1: 520))
write("crow_2", crow(61, f0: 700, f1: 560))
write("flap", flap())
write("chirp_1", chirp(70))
write("chirp_2", chirp(71))
write("chirp_3", chirp(72))
write("bone_1", boneCrack(80))
write("bone_2", boneCrack(81))
write("bone_3", boneCrack(82))
