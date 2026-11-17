// Checks a music loop from Tools/MakeMusic.swift without listening to it:
//   levels   the peak and RMS of each channel, and any DC offset
//   seam     whether the jump from the last sample back to the first stands out from the jumps
//            everywhere else in the file (a click at the loop point would)
//   tempo    estimated from how the onsets repeat (autocorrelation), to compare with the intended BPM
//   grid     with a dry drum stem: where each hit lands against the 16th grid, swing included
//   bars     the loudness of every bar, which shows the arrangement (drops, fills) at a glance
// It also writes <name>_seam.wav: the last and first 20 ms of the loop joined, to draw as a waveform.
// Build and run:  swiftc -O Tools/MusicCheck.swift -o /tmp/musiccheck
//                 /tmp/musiccheck music.wav <bpm> <swing16 steps> <swing8 steps> [drums.wav]
import Foundation

let SR = 44100.0

func readWav(_ path: String) -> [[Double]] {
    let d = try! Data(contentsOf: URL(fileURLWithPath: path))
    func u16(_ o: Int) -> Int { Int(d[o]) | Int(d[o + 1]) << 8 }
    func u32(_ o: Int) -> Int { u16(o) | u16(o + 2) << 16 }
    var o = 12, ch = 1, dataAt = 0, dataLen = 0
    while o + 8 <= d.count {
        let id = String(data: d[o..<(o + 4)], encoding: .ascii)!, len = u32(o + 4)
        if id == "fmt " { ch = u16(o + 10) }
        if id == "data" { dataAt = o + 8; dataLen = len; break }
        o += 8 + len
    }
    let n = dataLen / 2 / ch
    var out = [[Double]](repeating: [Double](repeating: 0, count: n), count: ch)
    d.withUnsafeBytes { (p: UnsafeRawBufferPointer) in
        for i in 0..<n { for c in 0..<ch {
            let v = Int16(bitPattern: UInt16(p[dataAt + (i * ch + c) * 2]) | UInt16(p[dataAt + (i * ch + c) * 2 + 1]) << 8)
            out[c][i] = Double(v) / 32768
        } }
    }
    return out
}

func db(_ x: Double) -> String { String(format: "%.1f dB", 20 * log10(max(x, 1e-12))) }
func pct(_ sorted: [Double], _ p: Double) -> Double { sorted[min(sorted.count - 1, Int(Double(sorted.count) * p))] }

let a = CommandLine.arguments
let path = a[1], bpm = Double(a[2])!, swing16 = Double(a[3])!, swing8 = Double(a[4])!
let ch = readWav(path)
let n = ch[0].count
print("== \(path.split(separator: "/").last!)  \(ch.count) ch  \(n) samples  \(String(format: "%.2f", Double(n) / SR)) s")

// ---- levels
for (c, x) in ch.enumerated() {
    let pk = x.map { abs($0) }.max()!
    let rms = (x.reduce(0) { $0 + $1 * $1 } / Double(n)).squareRoot()
    let dc = x.reduce(0, +) / Double(n)
    print("ch\(c): peak \(db(pk))  rms \(db(rms))  dc \(String(format: "%.5f", dc))")
}

// ---- seam: the step across the loop point against every other step in the file
for (c, x) in ch.enumerated() {
    var d1 = [Double](), d2 = [Double]()
    d1.reserveCapacity(n); d2.reserveCapacity(n)
    for i in 1..<n { d1.append(abs(x[i] - x[i - 1])) }
    for i in 2..<n { d2.append(abs(x[i] - 2 * x[i - 1] + x[i - 2])) }
    let s1 = abs(x[0] - x[n - 1])
    let s2a = abs(x[0] - 2 * x[n - 1] + x[n - 2]), s2b = abs(x[1] - 2 * x[0] + x[n - 1])
    let r1 = Double(d1.filter { $0 < s1 }.count) / Double(d1.count)
    let r2 = Double(d2.filter { $0 < max(s2a, s2b) }.count) / Double(d2.count)
    // and against its own neighbourhood: the biggest step in the 10 ms either side of the seam
    var local = 0.0
    for k in -441..<441 where k != -1 { let i0 = (k + n) % n, i1 = (k + 1 + n) % n; local = max(local, abs(x[i1] - x[i0])) }
    print(String(format: "ch%d seam: the loop-point step is %.2fx the largest step within 10 ms of it", c, s1 / max(local, 1e-9)))
    d1.sort(); d2.sort()
    print(String(format: "ch%d seam: step %.5f (percentile %.1f%%; median %.5f, p99.9 %.5f)  curvature %.5f (percentile %.1f%%; p99.9 %.5f)",
                 c, s1, r1 * 100, pct(d1, 0.5), pct(d1, 0.999), max(s2a, s2b), r2 * 100, pct(d2, 0.999)))
}
// the seam as a little file to draw: last 20 ms then first 20 ms
do {
    let w = Int(0.02 * SR)
    var d = Data()
    func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
    func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
    let m = 2 * w, c = ch.count
    d.append("RIFF".data(using: .ascii)!); u32(36 + m * 2 * c); d.append("WAVE".data(using: .ascii)!)
    d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(c); u32(44100); u32(44100 * 2 * c); u16(2 * c); u16(16)
    d.append("data".data(using: .ascii)!); u32(m * 2 * c)
    for k in 0..<m { for cc in 0..<c {
        let v = k < w ? ch[cc][n - w + k] : ch[cc][k - w]
        u16(Int(UInt16(bitPattern: Int16(max(-32767, min(32767, v * 32767))))))
    } }
    try! d.write(to: URL(fileURLWithPath: path.replacingOccurrences(of: ".wav", with: "_seam.wav")))
}

// ---- onsets: a rise in short-time energy well above the recent level
func onsets(_ x0: [Double], hop: Int = 64, ratio: Double = 3.0, floorDB: Double = -50) -> [Double] {
    // first difference: tilts the spectrum up so a hat's attack isn't hidden under a kick's long tail
    var x = x0
    for i in stride(from: x.count - 1, to: 0, by: -1) { x[i] -= x[i - 1] }
    var e = [Double]()
    var i = 0
    while i + hop <= x.count { var s = 0.0; for k in i..<(i + hop) { s += x[k] * x[k] }; e.append(s / Double(hop)); i += hop }
    var out = [Double]()
    let back = Int(0.03 * SR) / hop, fl = pow(10, floorDB / 10)
    var last = -1e9
    for j in back..<(e.count - 2) {
        var prev = 0.0
        for k in (j - back)..<j { prev += e[k] }
        prev /= Double(back)
        if e[j] > fl && e[j] > ratio * prev + 1e-12 && e[j] >= e[j - 1] && Double(j * hop) - last > 0.06 * SR {
            // the exact start: the first sample reaching a fifth of the peak just after
            var pk = 0.0
            for k in (j * hop)..<min(x.count, j * hop + Int(0.01 * SR)) { pk = max(pk, abs(x[k])) }
            var s = (j - 1) * hop
            while s < x.count - 1 && abs(x[s]) < 0.2 * pk { s += 1 }
            out.append(Double(s)); last = Double(j * hop)
        }
    }
    return out
}

// ---- tempo: autocorrelate an onset-strength curve and find the strongest beat period
do {
    let mono = (0..<n).map { i in ch.map { $0[i] }.reduce(0, +) / Double(ch.count) }
    let hop = 128
    var env = [Double](), prev = 0.0
    var i = 0
    while i + hop <= n {
        var s = 0.0
        for k in i..<(i + hop) { s += mono[k] * mono[k] }
        let v = s.squareRoot()
        env.append(max(0, v - prev)); prev = v; i += hop
    }
    let m = env.reduce(0, +) / Double(env.count)
    env = env.map { $0 - m }
    func ac(_ lag: Int) -> Double { var s = 0.0; for k in 0..<(env.count - lag) { s += env[k] * env[k + lag] }; return s }
    // score each candidate tempo by how well the onsets repeat at 1, 2, 4 and 8 beats (a comb), so swing
    // and syncopation can't win on a single odd lag
    let maxLag = Int(60 / 55 * 8 * SR) / hop + 2
    var acf = [Double](repeating: 0, count: maxLag + 2)
    for lag in 1...(maxLag + 1) { acf[lag] = ac(lag) }
    func at(_ lag: Double) -> Double { let i = Int(lag), f = lag - Double(i); return i + 1 < acf.count ? acf[i] * (1 - f) + acf[i + 1] * f : 0 }
    var bestBpm = 60.0, bestScore = -1e300
    var b = 60.0
    while b <= 130 {
        let beatHops = 60 / b * SR / Double(hop)
        let score = [1.0, 2, 4, 8].map { at(beatHops * $0) }.reduce(0, +)
        if score > bestScore { bestScore = score; bestBpm = b }
        b += 0.05
    }
    var beat = 60 / bestBpm
    let bar = Int((beat * 4 * SR / Double(hop)).rounded())
    var bb = bar, bv = -1e300
    for lag in (bar - 4)...(bar + 4) { let v = ac(lag); if v > bv { bv = v; bb = lag } }
    let z0 = ac(bb - 1), z2 = ac(bb + 1)
    let off2 = 0.5 * (z0 - z2) / (z0 - 2 * bv + z2)
    beat = (Double(bb) + off2) * Double(hop) / SR / 4
    print(String(format: "tempo: %.2f BPM estimated (intended %.2f)", 60 / beat, bpm))
}

// ---- loudness per bar, the shape of the arrangement
do {
    let barLen = SR * 60 / bpm * 4
    let bars = Int((Double(n) / barLen).rounded())
    // the whole signal, and only the mids, 300 Hz - 3 kHz, where the chords, skank and leads live (under the
    // hats and over the bass and kick), which is what the drops take away
    // (two steep high-passes at 300 Hz and two low-passes at 3 kHz, 24 dB per octave, so the loud bass stays out)
    func biquad(_ x: [Double], high: Bool, f: Double) -> [Double] {
        let w = 2 * Double.pi * f / SR, c = cos(w), al = sin(w) / (2 * 0.707)
        let n0 = high ? (1 + c) / 2 : (1 - c) / 2, n1 = high ? -(1 + c) : 1 - c, d0 = 1 + al, d1 = -2 * c, d2 = 1 - al
        var y = x, z1 = 0.0, z2 = 0.0
        for i in 0..<x.count {
            let o = n0 / d0 * x[i] + z1
            z1 = n1 / d0 * x[i] - d1 / d0 * o + z2
            z2 = n0 / d0 * x[i] - d2 / d0 * o
            y[i] = o
        }
        return y
    }
    func mids(_ x: [Double]) -> [Double] {
        biquad(biquad(biquad(biquad(x, high: true, f: 300), high: true, f: 300), high: false, f: 3000), high: false, f: 3000)
    }
    for (label, sig) in [("bars, RMS dBFS:       ", ch), ("bars, mids 0.3-3 kHz: ", ch.map(mids))] {
        var line = label
        for b in 0..<bars {
            let s = Int(Double(b) * barLen), e = min(n, Int(Double(b + 1) * barLen))
            var acc = 0.0
            for c in sig { for k in s..<e { acc += c[k] * c[k] } }
            let r = (acc / Double((e - s) * sig.count)).squareRoot()
            line += String(format: " %d:%.0f", b + 1, 20 * log10(r))
        }
        print(line)
    }
}

// ---- grid: every drum hit against the 16th grid, with the track's swing
if a.count > 5 {
    let drums = readWav(a[5])[0]
    let step = SR * 60 / bpm / 4
    let hits = onsets(drums)
    var errs = [Double](), straightErrs = [Double](), stepsHit = [Int](repeating: 0, count: 16)
    for h in hits {
        let k = Int((h / step).rounded(.down))
        // the nearest grid point, swung or straight, within a step
        var bestErr = 1e9, bestStep = 0
        for kk in (k - 1)...(k + 1) {
            let s = ((kk % 16) + 16) % 16
            let sw = s % 2 == 1 ? swing16 : s % 4 == 2 ? swing8 : 0
            let g = (Double(kk) + sw) * step
            if abs(h - g) < abs(bestErr) { bestErr = h - g; bestStep = s }
        }
        errs.append(bestErr / SR * 1000)
        if abs(bestErr / SR) > 0.008 { print(String(format: "grid: off-grid hit at %.3f s (bar %d, step %.2f), %.1f ms", h / SR, Int(h / step) / 16 + 1, (h / step).truncatingRemainder(dividingBy: 16), bestErr / SR * 1000)) }
        stepsHit[bestStep] += 1
        let straight = h - (h / step).rounded() * step
        straightErrs.append(straight / SR * 1000)
    }
    let absE = errs.map { abs($0) }.sorted()
    print(String(format: "grid: %d hits; off the swung 16th grid by median %.2f ms, p95 %.2f ms, max %.2f ms",
                 hits.count, pct(absE, 0.5), pct(absE, 0.95), absE.last ?? 0))
    print("grid: hits per step of the bar: " + stepsHit.enumerated().map { "\($0.offset):\($0.element)" }.joined(separator: " "))
    let late = straightErrs.filter { $0 > 5 }.count
    print(String(format: "grid: %d of %d hits sit more than 5 ms behind the straight grid (the swing)", late, hits.count))
}
