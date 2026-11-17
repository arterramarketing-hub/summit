// Synthesises Summit's two music loops and writes them as 16-bit stereo WAVs:
//   music_hub.wav  a dusty 90s boom bap beat for the summit camp (90 BPM, 32 bars)
//   music_run.wav  a reggae dub riddim for the run down the mountain (74 BPM, 36 bars)
// Build and run:  swiftc -O Tools/MakeMusic.swift -o /tmp/makemusic && /tmp/makemusic Assets/Summit/Resources/Audio
// Add a second folder to also write the dry drum stems that Tools/MusicCheck.swift checks the timing on.
// Like MakeAudio.swift, everything is made from oscillators, noise and filters - no samples, no records.
//
// Each loop is rendered as a circle. A note that rings past the end of the loop wraps round onto its
// start, and every effect (tape wobble, echo, reverb, the compressor) runs over the loop twice and keeps
// only the second lap, so what it holds at the seam is exactly what the end of the loop leaves behind.
// Anything that moves over time (a filter sweep, the tape's wow) moves a whole number of times per loop.
// Together that makes the end of the file flow straight into its start, with no click and no gap.
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

func hz(_ midi: Double) -> Double { 440 * pow(2, (midi - 69) / 12) }
func zeros(_ n: Int) -> [Double] { [Double](repeating: 0, count: n) }
func samples(_ sec: Double) -> Int { Int(sec * SR) }

// RBJ biquad (transposed direct form II)
struct Biquad {
    enum Kind { case lp, hp, bp, peak, highshelf }
    var b0 = 0.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0, z1 = 0.0, z2 = 0.0
    init(_ k: Kind, _ f: Double, _ q: Double = 0.707, db: Double = 0) { set(k, f, q, db: db) }
    mutating func set(_ k: Kind, _ f0: Double, _ q: Double, db: Double = 0) {
        let f = min(max(f0, 10), SR * 0.45)
        let w = TAU * f / SR, c = cos(w), al = sin(w) / (2 * q), A = pow(10, db / 40)
        var n0 = 0.0, n1 = 0.0, n2 = 0.0, d0 = 1 + al, d1 = -2 * c, d2 = 1 - al
        switch k {
        case .lp: n0 = (1 - c) / 2; n1 = 1 - c; n2 = (1 - c) / 2
        case .hp: n0 = (1 + c) / 2; n1 = -(1 + c); n2 = (1 + c) / 2
        case .bp: n0 = al; n1 = 0; n2 = -al
        case .peak: n0 = 1 + al * A; n1 = -2 * c; n2 = 1 - al * A; d0 = 1 + al / A; d2 = 1 - al / A
        case .highshelf:
            let sq = 2 * A.squareRoot() * al
            n0 = A * ((A + 1) + (A - 1) * c + sq); n1 = -2 * A * ((A - 1) + (A + 1) * c); n2 = A * ((A + 1) + (A - 1) * c - sq)
            d0 = (A + 1) - (A - 1) * c + sq; d1 = 2 * ((A - 1) - (A + 1) * c); d2 = (A + 1) - (A - 1) * c - sq
        }
        b0 = n0 / d0; b1 = n1 / d0; b2 = n2 / d0; a1 = d1 / d0; a2 = d2 / d0
    }
    mutating func run(_ x: Double) -> Double {
        let y = b0 * x + z1
        z1 = b1 * x - a1 * y + z2
        z2 = b2 * x - a2 * y
        return y
    }
}

func filter(_ x: [Double], _ k: Biquad.Kind, _ f: Double, q: Double = 0.707, db: Double = 0) -> [Double] {
    var b = Biquad(k, f, q, db: db)
    var y = x
    for i in 0..<y.count { y[i] = b.run(y[i]) }
    return y
}

/// A filter whose cutoff follows a curve, re-tuned every 32 samples (smooth enough to hear as a sweep).
func sweep(_ x: [Double], _ k: Biquad.Kind, q: Double, _ cutoff: (Int) -> Double) -> [Double] {
    var b = Biquad(k, cutoff(0), q)
    var y = x
    for i in 0..<y.count {
        if i % 32 == 0 { b.set(k, cutoff(i), q) }
        y[i] = b.run(y[i])
    }
    return y
}

/// Ends a one-shot at exactly zero so it can be dropped anywhere without a click.
func tail(_ x: [Double], _ sec: Double = 0.006) -> [Double] {
    var y = x
    let m = min(y.count, samples(sec))
    for i in 0..<m { y[y.count - 1 - i] *= Double(i) / Double(m) }
    return y
}

// ------------------------------------------------------------------ the loop and its buses

/// Where things fall in a loop: 16 steps to the bar, with the swing that makes it groove.
struct Loop {
    let bpm: Double, bars: Int
    let step: Double     // samples in one 16th
    let n: Int           // samples in the whole loop
    let swing16: Double  // how far (in steps) the odd 16ths sit behind the beat
    let swing8: Double   // how far the off-beat 8ths sit behind
    init(bpm: Double, bars: Int, swing16: Double, swing8: Double) {
        self.bpm = bpm; self.bars = bars; self.swing16 = swing16; self.swing8 = swing8
        step = SR * 60 / bpm / 4
        n = Int((Double(bars * 16) * step).rounded())
    }
    /// The sample a note on `step` (0..15, fractions allowed) of `bar` starts on, swing included.
    func at(_ bar: Int, _ s: Double, jitter: Double = 0) -> Int {
        var p = s
        if s == s.rounded() {
            let k = Int(s)
            if k % 2 == 1 { p += swing16 } else if k % 4 == 2 { p += swing8 }
        }
        let i = Int(((Double(bar * 16) + p) * step + jitter * SR).rounded())
        return ((i % n) + n) % n
    }
    func sec(_ steps: Double) -> Double { steps * step / SR }
    /// 0..1 through the loop, for anything that has to come round to where it started.
    func phase(_ i: Int) -> Double { Double(i % n) / Double(n) }
    /// Which bar (with fraction) sample i of a lap falls in.
    func barPos(_ i: Int) -> Double { Double(i % n) / step / 16 }
}

final class Stereo {
    var l: [Double], r: [Double]
    init(_ n: Int) { l = zeros(n); r = zeros(n) }
}

/// Adds a sound into a buffer at `at`, wrapping whatever runs past the end round to the start.
func place(_ x: [Double], _ into: inout [Double], at: Int, gain: Double) {
    let n = into.count
    var j = at
    for v in x { into[j] += v * gain; j += 1; if j == n { j = 0 } }
}

func place(_ x: [Double], _ into: Stereo, at: Int, gain: Double, pan: Double = 0) {
    let a = (pan + 1) * Double.pi / 4
    place(x, &into.l, at: at, gain: gain * cos(a) * 2.0.squareRoot())
    place(x, &into.r, at: at, gain: gain * sin(a) * 2.0.squareRoot())
}

/// Runs an effect over the loop twice and keeps the second lap, so whatever the effect is still holding
/// at the end of the loop (echoes, reverb, a wobble) carries on over the seam into the start.
func circular(_ x: [Double], _ fx: ([Double]) -> [Double]) -> [Double] {
    let n = x.count
    return Array(fx(x + x)[n..<(2 * n)])
}

func circular2(_ l: [Double], _ r: [Double], _ fx: ([Double], [Double]) -> ([Double], [Double])) -> ([Double], [Double]) {
    let n = l.count
    let (a, b) = fx(l + l, r + r)
    return (Array(a[n..<(2 * n)]), Array(b[n..<(2 * n)]))
}

func addInto(_ a: inout [Double], _ b: [Double], _ g: Double = 1) { for i in 0..<a.count { a[i] += b[i] * g } }

// ------------------------------------------------------------------ effects

/// Freeverb-style room: eight damped combs into four allpasses. `scale` shrinks or grows the room.
func reverb(_ x: [Double], size: Double, damp: Double, scale: Double, spread: Int) -> [Double] {
    let combLen = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617].map { Int(Double($0 + spread) * scale) }
    let apLen = [556, 441, 341, 225].map { Int(Double($0 + spread) * scale) }
    var cb = combLen.map { zeros($0) }, ci = [Int](repeating: 0, count: 8), cf = zeros(8)
    var ab = apLen.map { zeros($0) }, ai = [Int](repeating: 0, count: 4)
    var y = zeros(x.count)
    for i in 0..<x.count {
        let inp = x[i] * 0.03
        var out = 0.0
        for c in 0..<8 {
            let o = cb[c][ci[c]]
            cf[c] = o * (1 - damp) + cf[c] * damp
            cb[c][ci[c]] = inp + cf[c] * size
            ci[c] += 1; if ci[c] == combLen[c] { ci[c] = 0 }
            out += o
        }
        for a in 0..<4 {
            let bo = ab[a][ai[a]]
            let o = bo - out
            ab[a][ai[a]] = out + bo * 0.5
            ai[a] += 1; if ai[a] == apLen[a] { ai[a] = 0 }
            out = o
        }
        y[i] = out
    }
    return y
}

/// A spring reverb: in each of two springs a long chain of allpass filters smears a hit into the
/// metallic, chirping "boing" of a real spring tank, inside a feedback loop that keeps it ringing.
func spring(_ x: [Double], spread: Int) -> [Double] {
    var y = zeros(x.count)
    for (si, len) in [1543 + spread, 1873 + spread].enumerated() {
        var line = zeros(len), idx = 0
        var ap = zeros(40)
        let a = 0.6 + 0.05 * Double(si)
        var lp = Biquad(.lp, 4200, 0.6), hp = Biquad(.hp, 220, 0.6)
        for i in 0..<x.count {
            var s = x[i] + line[idx] * 0.76
            for k in 0..<40 { let o = a * s + ap[k]; ap[k] = s - a * o; s = o }
            s = lp.run(s)
            line[idx] = s; idx += 1; if idx == len { idx = 0 }
            y[i] += hp.run(s) * 0.5
        }
    }
    return y
}

/// A tape echo. The repeats bounce between the speakers, each one darker and a little dirtier than the
/// last, and the delay time wavers slightly as the tape stretches. `feedback(i)` is the knob a dub engineer
/// rides; `tone(i)` is the low-pass on the repeats.
func tapeEcho(_ x: [Double], loop: Loop, delay: Double, feedback: (Int) -> Double, tone: (Int) -> Double) -> ([Double], [Double]) {
    let n = x.count, size = Int(delay * SR * 1.05) + 8
    var bl = zeros(size), br = zeros(size), w = 0
    var hpL = Biquad(.hp, 300, 0.6), hpR = Biquad(.hp, 300, 0.6)
    var lpL = Biquad(.lp, tone(0), 0.7), lpR = Biquad(.lp, tone(0), 0.7)
    let wow = max(1, (Double(loop.n) / SR * 0.45).rounded())
    var yl = zeros(n), yr = zeros(n)
    for i in 0..<n {
        if i % 64 == 0 { let t = tone(i); lpL.set(.lp, t, 0.7); lpR.set(.lp, t, 0.7) }
        let d = delay * SR * (1 + 0.003 * sin(TAU * wow * loop.phase(i)))
        var rp = Double(w) - d
        if rp < 0 { rp += Double(size) }
        let i0 = Int(rp), fr = rp - Double(i0), i1 = (i0 + 1) % size
        let ol = bl[i0] * (1 - fr) + bl[i1] * fr
        let or = br[i0] * (1 - fr) + br[i1] * fr
        let f = feedback(i)
        bl[w] = tanh(1.3 * (x[i] + f * lpL.run(hpL.run(or)))) / 1.3   // the left repeat comes back on the right
        br[w] = tanh(1.3 * (f * lpR.run(hpR.run(ol)))) / 1.3          // and the right one back on the left
        w += 1; if w == size { w = 0 }
        yl[i] = ol; yr[i] = or
    }
    return (yl, yr)
}

/// Worn tape: the pitch wavers slowly (wow) and quickly (flutter). Each wobble turns a whole number
/// of times per loop so it comes back round to where it started at the seam.
func wobble(_ x: [Double], loop: Loop, mods: [(hz: Double, ms: Double)], offset: Double = 0) -> [Double] {
    let base = 8.0 * SR / 1000
    let cyc = mods.map { max(1, (Double(loop.n) / SR * $0.hz).rounded()) }
    var y = zeros(x.count)
    for i in 0..<x.count {
        let ph = loop.phase(i)
        var d = base
        for (k, m) in mods.enumerated() { d += m.ms * SR / 1000 * sin(TAU * cyc[k] * ph + offset + Double(k)) }
        let rp = Double(i) - d
        if rp < 0 { continue }
        let i0 = Int(rp), fr = rp - Double(i0)
        y[i] = x[i0] * (1 - fr) + x[min(i0 + 1, x.count - 1)] * fr
    }
    return y
}

/// A gentle bus compressor (linked stereo) that glues the parts together.
func glue(_ l: [Double], _ r: [Double], threshDB: Double, ratio: Double, attack: Double, release: Double) -> ([Double], [Double]) {
    var yl = l, yr = r, env = 0.0
    let ga = exp(-1 / (attack * SR)), gr = exp(-1 / (release * SR))
    for i in 0..<l.count {
        let p = max(abs(l[i]), abs(r[i]))
        env = p > env ? ga * env + (1 - ga) * p : gr * env + (1 - gr) * p
        let over = 20 * log10(max(env, 1e-9)) - threshDB
        if over > 0 {
            let g = pow(10, -over * (1 - 1 / ratio) / 20)
            yl[i] *= g; yr[i] *= g
        }
    }
    return (yl, yr)
}

// ------------------------------------------------------------------ drums

/// Boom bap kick: a sine that drops from a knock to a deep thump, a long sub tail under it, and a click on top.
func kickBoom(seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(0.6)
    var o = zeros(n), ph = 0.0
    var lp = Biquad(.lp, 3800, 0.7)
    let drop = rng.r(0.03, 0.038)
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * (47 + 115 * exp(-t / drop)) / SR
        let amp = min(1, t / 0.0008) * (0.75 * exp(-t / 0.11) + 0.5 * exp(-t / 0.42))
        let click = lp.run(rng.bi()) * exp(-t / 0.004) * 0.7
        o[i] = tanh(1.7 * (sin(ph) * amp + click)) * 0.9
    }
    return tail(o)
}

/// A cracking snare: a short tuned body, a burst of bright noise and a snap on the front.
func snareCrack(seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(0.42)
    var o = zeros(n), ph = 0.0
    var hp = Biquad(.hp, 1100, 0.7), pk = Biquad(.peak, 4300, 1.1, db: 6), snapF = Biquad(.hp, 2500, 0.8)
    let tune = rng.r(182, 194)
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * tune * (1 + 0.3 * exp(-t / 0.008)) / SR
        let body = sin(ph) * exp(-t / 0.07) * 0.75 + sin(ph * 1.78) * exp(-t / 0.04) * 0.35
        let w = rng.bi()
        let noise = pk.run(hp.run(w)) * (0.85 * exp(-t / 0.13) + 0.3 * exp(-t / 0.3))
        let snap = snapF.run(w) * exp(-t / 0.007) * 1.1
        o[i] = tanh(1.5 * (body + noise + snap) * min(1, t / 0.0005)) * 0.85
    }
    return tail(o)
}

/// Hi-hat: six square-wave partials at clashing pitches (the metal) mixed with noise, kept high.
func hat(open: Bool, seed: UInt64, len: Double = 0.45) -> [Double] {
    var rng = RNG(seed)
    let n = samples(open ? len : 0.1)
    let fs = [263.0, 400, 421, 474, 587, 845].map { $0 * rng.r(1.55, 1.62) }
    var ph = zeros(6), o = zeros(n)
    var bp = Biquad(.bp, 9500, 0.9), hp = Biquad(.hp, 6800, 0.7)
    let decay = open ? 0.17 : rng.r(0.018, 0.026)
    for i in 0..<n {
        let t = Double(i) / SR
        var m = 0.0
        for k in 0..<6 { ph[k] += fs[k] / SR; m += ph[k].truncatingRemainder(dividingBy: 1) < 0.5 ? 1 : -1 }
        let x = m / 6 * 0.55 + rng.bi() * 0.6
        o[i] = hp.run(bp.run(x)) * exp(-t / decay) * min(1, t / 0.0004) * 1.6
    }
    return tail(o, open ? 0.04 : 0.006)
}

/// The one-drop's partner: a deep round kick with a soft front.
func kickDub(seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(0.75)
    var o = zeros(n), ph = 0.0
    var lp = Biquad(.lp, 1800, 0.7)
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * (44 + 70 * exp(-t / 0.045)) / SR
        let amp = min(1, t / 0.002) * (0.6 * exp(-t / 0.16) + 0.55 * exp(-t / 0.5))
        o[i] = tanh(1.4 * (sin(ph) * amp + lp.run(rng.bi()) * exp(-t / 0.005) * 0.35)) * 0.95
    }
    return tail(o)
}

/// Cross-stick rimshot: a hard wooden click with a hollow knock behind it.
func rim(seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(0.2)
    var o = zeros(n)
    var bp1 = Biquad(.bp, rng.r(1650, 1750), 9), bp2 = Biquad(.bp, 520, 5)
    for i in 0..<n {
        let t = Double(i) / SR
        let x = rng.bi() * exp(-t / 0.0025)
        o[i] = (bp1.run(x) * 3.2 + bp2.run(x) * 2.2) * min(1, t / 0.0003)
    }
    return tail(softclip(o, 1.4))
}

/// A tuned tom for the rolls at the ends of phrases.
func tom(_ f: Double, seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(0.5)
    var o = zeros(n), ph = 0.0
    var bp = Biquad(.bp, f * 3, 1.5)
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * f * (1 + 0.35 * exp(-t / 0.03)) / SR
        o[i] = (sin(ph) * exp(-t / 0.22) + bp.run(rng.bi()) * exp(-t / 0.03) * 0.5) * min(1, t / 0.001)
    }
    return tail(softclip(o, 1.3))
}

func softclip(_ x: [Double], _ drive: Double) -> [Double] { x.map { tanh($0 * drive) / tanh(drive) } }

// ------------------------------------------------------------------ pitched instruments

/// Electric piano: a two-operator FM voice whose bark fades as the note rings, with a little bell of
/// the tine on the front.
func rhodes(_ m: Double, dur: Double, vel: Double) -> [Double] {
    let f = hz(m), rel = 0.22
    let n = samples(dur + rel + 0.02)
    let decay = 2.2 * pow(0.5, (m - 55) / 24)
    var o = zeros(n)
    for i in 0..<n {
        let t = Double(i) / SR
        let gate = t < dur ? 1 : exp(-(t - dur) / 0.06)
        let index = (0.7 + 1.5 * vel) * exp(-t / 0.2) + 0.3
        let car = sin(TAU * f * t + index * sin(TAU * f * t))
        let tine = f * 7 < 9000 ? sin(TAU * f * 7 * t) * 0.1 * vel * exp(-t / 0.02) : 0
        let amp = min(1, t / 0.002) * (0.7 * exp(-t / decay) + 0.3 * exp(-t / 0.4)) * gate
        let x = (car + tine) * amp * (0.5 + 0.5 * vel)
        o[i] = x + 0.12 * x * x   // a lean of even harmonics, the warmth of the pickup
    }
    return tail(o)
}

/// Round bass for the boom bap: a sine with a touch of the next two harmonics and a soft pluck.
func roundBass(_ m: Double, dur: Double, vel: Double) -> [Double] {
    let f = hz(m), rel = 0.05
    let n = samples(dur + rel)
    var o = zeros(n), ph = 0.0
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * f / SR
        let gate = t < dur ? 1 : exp(-(t - dur) / 0.02)
        let w = sin(ph) + 0.25 * sin(2 * ph) + 0.07 * sin(3 * ph) + 0.18 * exp(-t / 0.04) * sin(4 * ph)
        o[i] = w * min(1, t / 0.005) * (0.65 + 0.35 * exp(-t / 0.35)) * gate * vel
    }
    return tail(filter(o, .lp, 800, q: 0.6))
}

/// Dub sub bass: almost a pure sine, felt more than heard, slightly driven.
func subBass(_ m: Double, dur: Double, vel: Double) -> [Double] {
    let f = hz(m), rel = 0.09
    let n = samples(dur + rel)
    var o = zeros(n), ph = 0.0
    for i in 0..<n {
        let t = Double(i) / SR
        ph += TAU * f / SR
        let gate = t < dur ? 1 : exp(-(t - dur) / 0.03)
        let w = sin(ph) + 0.14 * sin(2 * ph) + 0.04 * sin(3 * ph)
        o[i] = tanh(1.3 * w * min(1, t / 0.012) * (0.8 + 0.2 * exp(-t / 0.3)) * gate) * vel
    }
    return tail(filter(o, .lp, 320, q: 0.6))
}

/// One plucked string (Karplus-Strong), with a fractional delay so it stays in tune up high.
func pluck(_ f: Double, sec: Double, damp: Double, pick: Double = 5000, seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let n = samples(sec), d = SR / f - 0.5
    var y = zeros(n)
    var lp = Biquad(.lp, pick, 0.7)   // how hard and bright the pick is
    let burst = Int(d) + 2
    for i in 0..<n {
        let x = i < burst ? lp.run(rng.bi()) : 0
        let rp = Double(i) - d
        var fb = 0.0
        if rp >= 1 {
            let i0 = Int(rp), fr = rp - Double(i0)
            let a = y[i0] * (1 - fr) + y[min(i0 + 1, i - 1)] * fr
            let b = y[i0 - 1] * (1 - fr) + y[i0] * fr
            fb = damp * 0.5 * (a + b)
        }
        y[i] = x + fb
    }
    return y
}

/// The skank: a muted up-strum on the off-beat, choked short by the palm.
func skank(_ notes: [Double], seed: UInt64) -> [Double] {
    let n = samples(0.24)
    var o = zeros(n)
    for (k, m) in notes.reversed().enumerated() {   // an up-strum: the top string first
        let s = pluck(hz(m), sec: 0.24, damp: 0.996, pick: 2600, seed: seed &+ UInt64(k))
        let off = samples(0.004 * Double(k))
        for i in 0..<(n - off) { o[i + off] += s[i] }
    }
    for i in 0..<n {
        let t = Double(i) / SR
        o[i] *= t < 0.085 ? 1 : exp(-(t - 0.085) / 0.03)   // the palm comes down
    }
    // through a small, slightly driven amp: rounds off the pick's spike so the chord itself carries
    let bright = filter(filter(o, .hp, 280, q: 0.6), .peak, 2300, q: 1, db: 5)
    let pk = bright.map { abs($0) }.max()!
    return tail(bright.map { tanh(2.2 * $0 / pk) * 0.25 })
}

/// Drawbar organ for the bubble: a few sine drawbars and the click of the key contacts.
func organ(_ notes: [Double], dur: Double, seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let rel = 0.03, n = samples(dur + rel)
    var o = zeros(n)
    let bars: [(Double, Double)] = [(1, 1), (2, 0.65), (3, 0.3), (4, 0.3), (6, 0.12)]
    for m in notes {
        let f = hz(m)
        for i in 0..<n {
            let t = Double(i) / SR
            let gate = t < dur ? 1 : exp(-(t - dur) / 0.01)
            var w = 0.0
            for (r, g) in bars where f * r < 9000 { w += sin(TAU * f * r * t) * g }
            o[i] += w * min(1, t / 0.003) * gate
        }
    }
    var bp = Biquad(.bp, 2800, 1.2)
    for i in 0..<min(n, samples(0.004)) { o[i] += bp.run(rng.bi()) * 1.5 }   // the key click
    return tail(o.map { $0 / Double(notes.count) * 0.4 })
}

/// Melodica: a free reed (a narrow pulse, built from its harmonics) with breath noise, a slow swell
/// and a vibrato that creeps in on held notes.
func melodica(_ m: Double, dur: Double, vel: Double, seed: UInt64) -> [Double] {
    var rng = RNG(seed)
    let f = hz(m), rel = 0.09, n = samples(dur + rel)
    var o = zeros(n), ph = 0.0
    var breath = Biquad(.bp, 1700, 0.9)
    let hs = Int(7000 / f)
    var amps = zeros(hs + 1)
    for k in 1...hs { amps[k] = abs(sin(Double.pi * Double(k) * 0.32)) / Double(k) }
    for i in 0..<n {
        let t = Double(i) / SR
        let vib = 1 + 0.005 * sin(TAU * 5.3 * t) * min(1, max(0, (t - 0.18) / 0.3))
        ph += TAU * f * vib / SR
        var w = 0.0
        for k in 1...hs { w += sin(Double(k) * ph) * amps[k] }
        let gate = t < dur ? 1 : exp(-(t - dur) / 0.035)
        let env = min(1, t / 0.04) * (0.85 + 0.15 * min(1, t / 0.4)) * gate
        o[i] = (w + breath.run(rng.bi()) * 0.12) * env * vel
    }
    return tail(filter(filter(o, .peak, 1250, q: 1, db: 4), .lp, 5200, q: 0.6).map { $0 * 0.8 })
}

/// A brass stab: two detuned saws whose brightness blooms and falls back, like a lip on a mouthpiece.
func horn(_ m: Double, dur: Double, vel: Double) -> [Double] {
    let f = hz(m), rel = 0.07, n = samples(dur + rel)
    let hs = min(30, Int(8000 / f))
    var o = zeros(n), p1 = 0.0, p2 = 0.0
    for i in 0..<n {
        let t = Double(i) / SR
        p1 += TAU * f * 1.0023 / SR; p2 += TAU * f * 0.9977 / SR
        let bright = 1.2 + 6 * min(1, t / 0.035) * (0.55 + 0.45 * exp(-t / 0.12))
        var w = 0.0
        for k in 1...hs {
            let a = exp(-Double(k - 1) / bright) / Double(k)
            w += (sin(Double(k) * p1) + sin(Double(k) * p2)) * a
        }
        let gate = t < dur ? 1 : exp(-(t - dur) / 0.025)
        o[i] = w * min(1, t / 0.015) * gate * vel * 0.5
    }
    return tail(o)
}

/// The record: a bed of hiss and a scatter of crackle and the odd pop, different in each ear.
func vinyl(_ loop: Loop, seed: UInt64) -> Stereo {
    var rng = RNG(seed)
    let out = Stereo(loop.n)
    for ch in 0..<2 {
        var hiss = zeros(loop.n), b0 = 0.0, b1 = 0.0, b2 = 0.0
        for i in 0..<loop.n {
            let w = rng.bi()
            b0 = 0.99765 * b0 + w * 0.0990460; b1 = 0.96300 * b1 + w * 0.2965164; b2 = 0.57000 * b2 + w * 1.0526913
            hiss[i] = (b0 + b1 + b2 + w * 0.1848) * 0.05
        }
        hiss = circular(hiss) { filter(filter($0, .hp, 2200), .lp, 9500) }
        var crackle = zeros(loop.n)
        let count = Int(Double(loop.n) / SR * 11)
        for _ in 0..<count {
            let at = Int(rng.u() * Double(loop.n))
            let big = pow(rng.u(), 7)
            let a = (0.05 + 0.6 * big) * (rng.u() < 0.5 ? -1 : 1)
            let len = 2 + Int(rng.u() * 6 + big * 30)
            for k in 0..<len { crackle[(at + k) % loop.n] += a * exp(-Double(k) / Double(len) * 3) * (k % 2 == 0 ? 1 : -0.6) }
        }
        crackle = circular(crackle) { filter($0, .hp, 1200) }
        let dst = ch == 0 ? \Stereo.l : \Stereo.r
        for i in 0..<loop.n { out[keyPath: dst][i] = hiss[i] * 0.05 + crackle[i] * 0.09 }
    }
    return out
}

// ------------------------------------------------------------------ master and file

func peak(_ l: [Double], _ r: [Double]) -> Double { max(l.map { abs($0) }.max()!, r.map { abs($0) }.max()!) }

/// Loudness in LUFS the way EBU R128 measures it: K-weighted (a high-pass and a lift above ~1.7 kHz,
/// roughly how loud we hear things), in 400 ms blocks, ignoring blocks 10 LU quieter than the rest.
func lufs(_ l: [Double], _ r: [Double]) -> Double {
    func k(_ x: [Double]) -> [Double] { filter(filter(x, .hp, 38, q: 0.5), .highshelf, 1681, q: 0.707, db: 4) }
    let kl = k(l), kr = k(r), blk = samples(0.4)
    var blocks = [Double]()
    var i = 0
    while i + blk <= kl.count {
        var s = 0.0
        for j in i..<(i + blk) { s += kl[j] * kl[j] + kr[j] * kr[j] }
        blocks.append(s / Double(blk)); i += blk
    }
    let loud = { (ms: Double) in -0.691 + 10 * log10(max(ms, 1e-12)) }
    let ungated = blocks.reduce(0, +) / Double(blocks.count)
    let kept = blocks.filter { loud($0) > loud(ungated) - 10 && loud($0) > -70 }
    return loud(kept.reduce(0, +) / Double(kept.count))
}

/// The master: a gentle glue compressor over the whole mix, then the level set so the loop plays at
/// `targetLUFS` (both tracks the same, so the crossfade between them doesn't jump), never peaking above `ceilingDB`.
func master(_ l: [Double], _ r: [Double], targetLUFS: Double, ceilingDB: Double) -> ([Double], [Double]) {
    let pre = 1 / peak(l, r)
    var (a, b) = circular2(l.map { $0 * pre }, r.map { $0 * pre }) { x, y in
        glue(filter(x, .hp, 24), filter(y, .hp, 24), threshDB: -8, ratio: 2, attack: 0.012, release: 0.2)
    }
    let toLoud = pow(10, (targetLUFS - lufs(a, b)) / 20), toCeiling = pow(10, ceilingDB / 20) / peak(a, b)
    let g = min(toLoud, toCeiling)
    for i in 0..<a.count { a[i] *= g; b[i] *= g }
    print(String(format: "  master: %.1f LUFS, peak %.1f dBFS%@", lufs(a, b), 20 * log10(peak(a, b)), toLoud > toCeiling ? " (held down by the peak ceiling)" : ""))
    return (a, b)
}

func writeWav(_ path: String, _ chans: [[Double]]) {
    let n = chans[0].count, ch = chans.count
    var rng = RNG(12345)
    var d = Data()
    func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
    func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
    d.append("RIFF".data(using: .ascii)!); u32(36 + n * 2 * ch); d.append("WAVE".data(using: .ascii)!)
    d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(ch); u32(Int(SR)); u32(Int(SR) * 2 * ch); u16(2 * ch); u16(16)
    d.append("data".data(using: .ascii)!); u32(n * 2 * ch)
    var pcm = [Int16](repeating: 0, count: n * ch)
    for i in 0..<n {
        for c in 0..<ch {
            let v = chans[c][i] * 32767 + (rng.u() - rng.u())   // a whisper of dither
            pcm[i * ch + c] = Int16(max(-32767, min(32767, v.rounded())))
        }
    }
    pcm.withUnsafeBytes { d.append(contentsOf: $0) }
    try! d.write(to: URL(fileURLWithPath: path))
    print("wrote \(path)  \(String(format: "%.1f", Double(n) / SR)) s  \(d.count / 1024) KB")
}

// ------------------------------------------------------------------ track 1: the camp (boom bap)

func hubTrack(stem: inout [Double]) -> ([Double], [Double]) {
    let L = Loop(bpm: 90, bars: 32, swing16: 0.18, swing8: 0)
    var rng = RNG(90)
    let drums = Stereo(L.n), keys = Stereo(L.n)
    var bass = zeros(L.n), room = zeros(L.n)
    stem = zeros(L.n)

    let kicks = (0..<4).map { kickBoom(seed: 10 + UInt64($0)) }
    let snares = (0..<4).map { snareCrack(seed: 20 + UInt64($0)) }
    let closed = (0..<6).map { hat(open: false, seed: 30 + UInt64($0)) }
    let open = hat(open: true, seed: 40, len: L.sec(2) + 0.01)   // rings until the next closed hat chokes it

    func hit(_ s: [Double], _ bar: Int, _ step: Double, _ vel: Double, pan: Double = 0, room send: Double = 0, jitter: Double = 0.0007) {
        let at = L.at(bar, step, jitter: rng.bi() * jitter)
        let v = vel * rng.r(0.93, 1.05)
        place(s, drums, at: at, gain: v * 0.42, pan: pan)   // into the drum bus's saturation just hard enough to thicken the kick
        place(s, &stem, at: at, gain: v)
        if send > 0 { place(s, &room, at: at, gain: v * send * 0.42) }
    }

    // Cm9 - F13 - Bbmaj9 - Gm9, a ii-V-I-vi in Bb: rootless voicings with the 7ths and 9ths on top
    let chords: [[Double]] = [[51, 55, 58, 62], [51, 57, 62, 67], [50, 57, 60, 65], [53, 57, 58, 62]]
    // the bass under each chord: (step, note, length in steps), walking into the next root
    let lines: [[(Double, Double, Double)]] = [
        [(0, 36, 6.5), (10, 36, 2), (14, 34, 1.8)],                // C .. C, Bb stepping down to F
        [(0, 29, 6.5), (10, 29, 2), (14, 33, 1.8)],                // F .. F, A leaning up to Bb
        [(0, 34, 6.5), (10, 34, 2), (14, 38, 1.8)],                // Bb .. Bb, D falling to G
        [(0, 31, 6.5), (10, 31, 1.5), (12, 34, 1.5), (14, 35, 1.8)], // G .. G, Bb, B creeping up to C
    ]
    // two little Rhodes answers, over the F13 and the Gm9
    let licks: [[(Double, Double, Double)]] = [
        [(8, 79, 1.5), (10, 77, 1.5), (12, 74, 2), (14, 72, 3)],
        [(6, 74, 1), (7, 77, 1), (8, 79, 3), (12, 81, 1), (13, 79, 1), (14, 77, 4)],
    ]

    for b in 0..<L.bars {
        let ci = b % 4
        let drop = b == 16 || b == 17                 // the beat falls away: keys, bass and record only
        let halfDrop = b == 18 || b == 19              // the kick comes back, the snare waits
        let groove2 = (20...27).contains(b)

        // ---- drums
        if !drop {
            var ks: [Double] = b % 2 == 0 ? [0, 10] : [0, 7, 10]
            if groove2 { ks = b % 2 == 0 ? [0, 3, 10] : [0, 10, 11] }
            if b % 4 == 3 { ks = [0, 10, 15] }
            if b == 15 { ks = [0, 15] }                // the stop: the beat cuts after the backbeat
            if b == 31 { ks = [0, 10] }
            for s in ks { hit(kicks[Int(rng.u() * 4)], b, s, s == 0 ? 1 : 0.88) }

            if !halfDrop {
                var ss: [(Double, Double)] = [(4, 1), (12, 1)]
                if b % 2 == 1 { ss.append((7, 0.16)) }                     // a ghost note
                if b % 4 == 1 { ss.append((15, 0.22)) }
                if b % 8 == 7 && b != 15 && b != 31 { ss += [(13, 0.35), (14, 0.5), (15, 0.68)] }   // a fill into the next phrase
                if b == 15 { ss = [(4, 1)] }
                for (s, v) in ss { hit(snares[Int(rng.u() * 4)], b, s, v * 0.9, room: 0.55) }
            }
            if b == 19 { for k in 0..<8 { hit(snares[k % 4], b, 8 + Double(k), 0.22 + 0.08 * Double(k), room: 0.5) } }   // a roll back in
        }
        // hats: every 16th, loudest on the beat, softest on the swung "e" and "a"
        let openHere = b % 2 == 1 && !drop && b != 15 && b != 31
        let lastHat = b == 15 ? 7 : b == 31 ? 11 : 15
        for s in 0...lastHat {
            if openHere && s >= 14 { if s == 14 { hit(open, b, 14, 0.5, pan: 0.2, jitter: 0.0015) }; continue }
            var v = s % 4 == 0 ? 0.62 : s % 2 == 0 ? 0.48 : 0.24
            if drop { v *= 0.55 }
            hit(closed[Int(rng.u() * 6)], b, Double(s), v, pan: 0.2, jitter: 0.0015)
        }

        // ---- Rhodes: struck on the one, a swung push, and again on the and-of-three; rolled bottom-up
        var rhythm: [(Double, Double, Double)] = [(0, 6, 0.75), (7, 2, 0.45), (10, 5.5, 0.62)]
        if (8...15).contains(b) || groove2 { rhythm = [(0, 5, 0.72), (6, 1.5, 0.42), (10, 2, 0.58), (13, 2.8, 0.5)] }
        if drop || halfDrop { rhythm = [(0, 15.5, 0.6)] }
        if b == 31 { rhythm = [(0, 6, 0.75), (10, 5.8, 0.7)] }
        for (s, len, v) in rhythm {
            for (k, m) in chords[ci].enumerated() {
                let at = L.at(b, s) + samples(0.007 * Double(k))
                place(rhodes(m, dur: L.sec(len), vel: v * rng.r(0.92, 1.04)), keys, at: at % L.n, gain: 0.32, pan: -0.25 + 0.17 * Double(k))
            }
        }
        if ((8...15).contains(b) || b >= 20) && b % 2 == 1 && b != 15 {
            for (s, m, len) in licks[ci == 1 ? 0 : 1] {
                place(rhodes(m, dur: L.sec(len), vel: 0.5 * rng.r(0.9, 1.05)), keys, at: L.at(b, s, jitter: rng.bi() * 0.004), gain: 0.3, pan: 0.15)
            }
        }

        // ---- bass
        var line = lines[ci]
        if drop || halfDrop { line = [(0, line[0].1, 14.5)] }
        if groove2 && b % 2 == 0 { line.insert((7, line[0].1 + 12, 1.4), at: 1) }   // an octave pop
        for (s, m, len) in line { place(roundBass(m, dur: L.sec(len), vel: s == 0 ? 1 : 0.85), &bass, at: L.at(b, s), gain: 0.55) }
    }

    // ---- the buses
    // keys: worn tape (a slow wow and a little flutter), a suitcase tremolo panning them gently, and a
    // low-pass that closes right down while the beat drops out and opens again as it comes back
    let (kl, kr) = circular2(keys.l, keys.r) { l, r in
        let mods: [(hz: Double, ms: Double)] = [(0.55, 1.3), (5.8, 0.05)]
        var a = wobble(l, loop: L, mods: mods), b = wobble(r, loop: L, mods: mods, offset: 0.4)
        let trem = (Double(L.bars) * 3)   // three sways a bar
        for i in 0..<a.count {
            let p = sin(TAU * trem * L.phase(i)) * 0.22
            a[i] *= 1 - p; b[i] *= 1 + p
        }
        let cutoff: (Int) -> Double = { i in
            let bar = L.barPos(i)
            let closed = bar < 16 ? 0 : bar < 17 ? (bar - 16) : bar < 19 ? 1 : bar < 20 ? 20 - bar : 0
            return 2600 * pow(800.0 / 2600.0, closed)
        }
        return (sweep(a, .lp, q: 0.8, cutoff), sweep(b, .lp, q: 0.8, cutoff))
    }
    let (rl, rr) = circular2(room, room) { a, b in
        (reverb(a, size: 0.72, damp: 0.45, scale: 0.4, spread: 0), reverb(b, size: 0.72, damp: 0.45, scale: 0.4, spread: 23))
    }
    let (kvl, kvr) = circular2(kl, kr) { a, b in
        (reverb(a, size: 0.8, damp: 0.5, scale: 0.7, spread: 0), reverb(b, size: 0.8, damp: 0.5, scale: 0.7, spread: 23))
    }
    let bassF = circular(bass) { filter($0, .lp, 700, q: 0.6) }
    let rec = vinyl(L, seed: 9)

    var l = zeros(L.n), r = zeros(L.n)
    for i in 0..<L.n {
        let dl = tanh(1.25 * (drums.l[i] + rl[i] * 0.9)) / 1.25, dr = tanh(1.25 * (drums.r[i] + rr[i] * 0.9)) / 1.25
        l[i] = dl * 0.85 + bassF[i] * 0.7 + kl[i] * 0.36 + kvl[i] * 0.12 + rec.l[i] * 2.5
        r[i] = dr * 0.85 + bassF[i] * 0.7 + kr[i] * 0.36 + kvr[i] * 0.12 + rec.r[i] * 2.5
    }
    // the dust: the top rolled off like an old sampler, and a little saturation on the whole thing
    (l, r) = circular2(l, r) { a, b in (filter(a, .lp, 11500, q: 0.6).map { tanh($0 * 1.1) }, filter(b, .lp, 11500, q: 0.6).map { tanh($0 * 1.1) }) }
    return master(l, r, targetLUFS: -17, ceilingDB: -1.5)
}

// ------------------------------------------------------------------ track 2: the run (reggae dub)

func runTrack(stem: inout [Double]) -> ([Double], [Double]) {
    let L = Loop(bpm: 74, bars: 36, swing16: 0.2, swing8: 0.33)
    var rng = RNG(74)
    let drums = Stereo(L.n), music = Stereo(L.n)
    var bass = zeros(L.n), echo = zeros(L.n), springIn = zeros(L.n)
    stem = zeros(L.n)

    let kicks = (0..<3).map { kickDub(seed: 50 + UInt64($0)) }
    let rims = (0..<4).map { rim(seed: 60 + UInt64($0)) }
    let snares = (0..<2).map { snareCrack(seed: 70 + UInt64($0)) }
    let closed = (0..<6).map { hat(open: false, seed: 80 + UInt64($0)) }
    let open = hat(open: true, seed: 90, len: 0.5)
    let toms = [tom(185, seed: 95), tom(140, seed: 96), tom(104, seed: 97)]

    func hit(_ s: [Double], _ bar: Int, _ step: Double, _ vel: Double, pan: Double = 0, echo e: Double = 0, spring sp: Double = 0, jitter: Double = 0.0008) {
        let at = L.at(bar, step, jitter: rng.bi() * jitter)
        let v = vel * rng.r(0.94, 1.04)
        place(s, drums, at: at, gain: v * 0.45, pan: pan)
        place(s, &stem, at: at, gain: v)
        if e > 0 { place(s, &echo, at: at, gain: v * e) }
        if sp > 0 { place(s, &springIn, at: at, gain: v * sp) }
    }

    // Am - Am - Dm - Em, round and round
    let chordIx = [0, 0, 1, 2]
    let skankV: [[Double]] = [[69, 72, 76], [69, 74, 77], [67, 71, 76]]
    let bubbleLo: [[Double]] = [[45, 52], [50, 57], [52, 59]]
    let bubbleHi: [[Double]] = [[57, 60, 64], [57, 62, 65], [55, 59, 64]]
    // the bass carries the tune: a four-bar line that leaves room to breathe
    let bassLine: [[(Double, Double, Double)]] = [
        [(0, 33, 3), (4, 36, 2), (6, 40, 4), (11, 38, 1), (12, 36, 3)],
        [(0, 33, 6), (8, 31, 2), (10, 33, 2), (14, 36, 2)],
        [(0, 38, 3), (4, 41, 2), (6, 45, 4), (11, 43, 1), (12, 41, 3)],
        [(0, 40, 4), (6, 43, 2), (8, 35, 4), (12, 38, 2), (14, 40, 1.5)],
    ]
    // melodica: a call and an answer in A minor pentatonic
    let call: [[(Double, Double, Double)]] = [
        [(4, 76, 2), (6, 79, 2), (8, 81, 6)],
        [(0, 79, 2), (2, 76, 2), (4, 74, 3), (8, 76, 7)],
        [(4, 77, 2), (6, 81, 2), (8, 84, 6)],
        [(0, 83, 2), (2, 79, 2), (4, 76, 9)],
    ]
    let answer: [[(Double, Double, Double)]] = [
        [(8, 81, 2), (10, 84, 2), (12, 81, 4)],
        [(0, 79, 4), (4, 76, 4), (8, 74, 2), (10, 76, 5)],
        [(8, 74, 2), (10, 77, 2), (12, 81, 4)],
        [(0, 79, 4), (4, 76, 10)],
    ]

    for b in 0..<L.bars {
        let ci = chordIx[b % 4]
        let drop = (16...19).contains(b) || (32...35).contains(b)   // everything out but the bass and drums
        let skankOn = !drop
        let bubbleOn = !drop && !(20...23).contains(b)

        // ---- drums: the one drop - nothing on the one, kick and rim together on three
        let sectionTop = b % 8 == 0
        hit(kicks[b % 3], b, 8, 1)
        if sectionTop {
            hit(snares[b % 2], b, 8, 0.75, spring: 1.1, jitter: 0)   // a splash in the spring tank
        } else {
            let thrown = [17, 19, 33, 35].contains(b)
            hit(rims[Int(rng.u() * 4)], b, 8, 0.8, echo: thrown ? 0.9 : 0, spring: 0.12)
        }
        if b % 4 == 1 && !drop { hit(rims[Int(rng.u() * 4)], b, 14, 0.35, echo: 0.5) }
        // hats on the 8ths with a lazy shuffle, leaning on the off-beats
        for s in stride(from: 0, through: 14, by: 2) {
            if b % 4 == 3 && s == 14 { hit(open, b, 14, 0.4, pan: -0.15); continue }
            hit(closed[Int(rng.u() * 6)], b, Double(s), s % 4 == 2 ? 0.42 : 0.28, pan: -0.15, jitter: 0.0015)
        }
        if b % 2 == 1 { hit(closed[1], b, 13, 0.18, pan: -0.15); hit(closed[2], b, 15, 0.2, pan: -0.15) }
        // a tom roll down into each new eight bars
        if b % 8 == 7 {
            for (k, s) in [12.0, 13.333, 14.667].enumerated() { hit(toms[k], b, s, 0.6, pan: 0.3 - 0.3 * Double(k), echo: k == 2 ? 0.4 : 0) }
        }

        // ---- the skank on two and four, the organ bubbling in between
        if skankOn {
            let throwIt = [21, 23, 29].contains(b)
            for s in [4.0, 12.0] {
                let sk = skank(skankV[ci], seed: UInt64(b * 16) + UInt64(s))
                let at = L.at(b, s, jitter: rng.bi() * 0.001)
                place(sk, music, at: at, gain: 1.8, pan: -0.35)
                place(sk, &springIn, at: at, gain: 0.12)
                if throwIt && s == 12 { place(sk, &echo, at: at, gain: 0.8) }
            }
        }
        if bubbleOn {
            for (s, hi, v) in [(2.0, false, 0.8), (6.0, true, 0.75), (7.0, true, 0.5), (10.0, false, 0.8), (14.0, true, 0.75), (15.0, true, 0.5)] {
                let o = organ(hi ? bubbleHi[ci] : bubbleLo[ci], dur: L.sec(hi ? 0.7 : 1.1), seed: UInt64(b * 31) + UInt64(s))
                place(o, music, at: L.at(b, s, jitter: rng.bi() * 0.001), gain: v * 2.1, pan: 0.35)
            }
        }

        // ---- melodica: the call in bars 8-11, the answer in 12-15 and again at the end of the last verse
        var mel: [(Double, Double, Double)] = []
        if (8...11).contains(b) { mel = call[b - 8] }
        if (12...15).contains(b) { mel = answer[b - 12] }
        if (28...31).contains(b) { mel = answer[b - 28] }
        for (k, (s, m, len)) in mel.enumerated() {
            let last = k == mel.count - 1
            let note = melodica(m, dur: L.sec(len), vel: 0.8, seed: UInt64(b * 7 + k))
            let at = L.at(b, s, jitter: rng.bi() * 0.006)
            place(note, music, at: at, gain: 0.36, pan: 0.1)
            place(note, &echo, at: at, gain: last ? 0.85 : 0.3)
            place(note, &springIn, at: at, gain: 0.15)
        }
        // ---- horn stabs at the ends of phrases, flung into the echo
        let stabs: [(Double, Double)] = b == 23 ? [(12, 1.2), (14, 1.6)] : b == 27 ? [(10, 1.4)] : b == 31 ? [(12, 0.9), (13.333, 2)] : []
        for (s, len) in stabs {
            let at = L.at(b, s)
            for m in [64.0, 67, 71, 52] {
                let h = horn(m, dur: L.sec(len), vel: m == 52 ? 0.5 : 0.7)
                place(h, music, at: at, gain: 0.32, pan: m == 67 ? 0.25 : -0.05)
                place(h, &echo, at: at, gain: 0.45)
            }
        }

        // ---- bass: the same line through the drops, so the tune never stops
        for (s, m, len) in bassLine[b % 4] {
            place(subBass(m, dur: L.sec(len) * 0.92, vel: s == 0 ? 1 : 0.9), &bass, at: L.at(b, s), gain: 0.7)
        }
    }

    // ---- the dub mix
    // filter sweeps: the skank and bubble come back through a high-pass that opens up over bars 20-23,
    // and the whole band leans into a low-pass over the last bars before each drop
    let (ml, mr) = circular2(music.l, music.r) { l, r in
        let hpCut: (Int) -> Double = { i in
            let bar = L.barPos(i)
            return bar >= 20 && bar < 24 ? 2400 * pow(120.0 / 2400.0, (bar - 20) / 4) : 60
        }
        let lpCut: (Int) -> Double = { i in
            let bar = L.barPos(i)
            let into = bar >= 14 && bar < 16 ? (bar - 14) / 2 : bar >= 30 && bar < 32 ? (bar - 30) / 2 : 0
            return 14000 * pow(900.0 / 14000.0, into * into)
        }
        return (sweep(sweep(l, .hp, q: 0.9, hpCut), .lp, q: 1.1, lpCut), sweep(sweep(r, .hp, q: 0.9, hpCut), .lp, q: 1.1, lpCut))
    }
    // the tape echo, a dotted eighth, its feedback ridden up into the drops so the throws hang there
    let (el, er) = circular2(echo, echo) { x, _ in
        let fb: (Int) -> Double = { i in
            let bar = L.barPos(i)
            let swell = (bar >= 16 && bar < 20) || (bar >= 32) ? 0.72 : 0.5
            return swell
        }
        let tone: (Int) -> Double = { i in 1600 + 1400 * (0.5 + 0.5 * sin(TAU * Double(L.bars / 4) * L.phase(i))) }   // the engineer's hand on the tone knob
        return tapeEcho(x, loop: L, delay: L.sec(3), feedback: fb, tone: tone)
    }
    let (sl, sr) = circular2(springIn, springIn) { a, b in (spring(a, spread: 0), spring(b, spread: 37)) }
    let bassF = circular(bass) { filter($0, .lp, 260, q: 0.6) }

    var l = zeros(L.n), r = zeros(L.n)
    for i in 0..<L.n {
        let dl = tanh(1.15 * drums.l[i]) / 1.15, dr = tanh(1.15 * drums.r[i]) / 1.15
        l[i] = dl * 0.75 + bassF[i] * 0.55 + ml[i] * 0.25 + el[i] * 0.35 + sl[i] * 0.55
        r[i] = dr * 0.75 + bassF[i] * 0.55 + mr[i] * 0.25 + er[i] * 0.35 + sr[i] * 0.55
    }
    return master(l, r, targetLUFS: -17, ceilingDB: -1.5)
}

// ------------------------------------------------------------------ main

let args = CommandLine.arguments
let outDir = args.count > 1 ? args[1] : "."
let stemDir = args.count > 2 ? args[2] : nil
var stem = [Double]()
let (hl, hr) = hubTrack(stem: &stem)
writeWav(outDir + "/music_hub.wav", [hl, hr])
if let s = stemDir { writeWav(s + "/hub_drums.wav", [stem.map { $0 * 0.4 }]) }
let (rl, rr) = runTrack(stem: &stem)
writeWav(outDir + "/music_run.wav", [rl, rr])
if let s = stemDir { writeWav(s + "/run_drums.wav", [stem.map { $0 * 0.4 }]) }
