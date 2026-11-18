# Cuts the player's snow-steps recording into single footsteps, so the camp can play one exactly when a foot lands.
#   python3 Tools/CutSteps.py [--plot out.svg]
#
# Reads Assets/Summit/Resources/Audio/user_snow_steps.mp3 (left as the player gave it; decoded with ffmpeg), finds
# every step by its onset (where a burst of crunch first rises off the quiet floor, in 5 ms frames of high-passed
# energy), and
# writes each step from just before its onset to just before the next one (at most 0.42 s, faded out) as
# Assets/Summit/Resources/Audio/Steps/snow_step_NN.wav, all at a matched loudness. The onset lands 4 ms into each
# clip, so a step started at the moment of contact is heard on it. Steps.txt beside them lists the onset times.
# Plain Python (no numpy), so it runs anywhere ffmpeg does.

import array, math, os, struct, subprocess, sys, wave

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets/Summit/Resources/Audio/user_snow_steps.mp3")
OUT = os.path.join(ROOT, "Assets/Summit/Resources/Audio/Steps")
SR = 44100
HOP = int(SR * 0.005)            # 5 ms analysis frames
PRE = int(SR * 0.004)            # keep 4 ms before each onset (the very start of the attack)
MAXLEN = 0.42                    # s: no step is longer than this (the tail of the crunch)
MINGAP = 0.22                    # s: two steps are at least this far apart (faster than any sprint)

pcm = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", SRC, "-ac", "1", "-ar", str(SR), "-f", "s16le", "-"],
                     check=True, capture_output=True).stdout
x = array.array("h"); x.frombytes(pcm)
n = len(x)
print("STEPS source: %.2f s, %d samples" % (n / SR, n))

# high-passed energy per frame (the crunch is bright; first difference takes out rumble and handling noise)
frames = n // HOP
energy = []
for f in range(frames):
    s = f * HOP
    e = 0.0
    prev = x[s]
    for i in range(s + 1, s + HOP):
        d = x[i] - prev; prev = x[i]
        e += d * d
    energy.append(math.log10(1e-9 + e / HOP))
# each step is a burst of crunch standing well clear of the quiet between steps: find the bursts, then put the
# onset where the burst first leaves the floor (the boot meeting the snow), not at its loudest moment
sm_ = [sum(energy[max(0, f - 1):f + 2]) / len(energy[max(0, f - 1):f + 2]) for f in range(frames)]
srt = sorted(sm_)
floor = srt[int(len(srt) * 0.25)]
peak_e = srt[int(len(srt) * 0.99)]
thr = floor + 0.45 * (peak_e - floor)
runs, f = [], 0
while f < frames:
    if sm_[f] > thr:
        g = f
        while g < frames and (sm_[g] > thr or any(sm_[h] > thr for h in range(g, min(frames, g + 12)))): g += 1
        if (g - f) * HOP / SR >= 0.04: runs.append((f, g))
        f = g
    else: f += 1
times = []
for f0, f1 in runs:
    k = f0
    while k > 0 and sm_[k - 1] > floor + 0.25 * (peak_e - floor) and f0 - k < 20: k -= 1
    t = k * HOP / SR
    if times and t - times[-1] < MINGAP: continue
    times.append(t)
# to the sample: the first big jump in the high-passed signal within that frame and the next
refined = []
for t in times:
    s0 = max(1, int(t * SR) - HOP); e0 = min(n - 1, int(t * SR) + 3 * HOP)
    peak = max(abs(x[i] - x[i - 1]) for i in range(s0, e0))
    for i in range(s0, e0):
        if abs(x[i] - x[i - 1]) > peak * 0.3: refined.append(i / SR); break
times = refined
flux = sm_
print("STEPS found %d steps at %s" % (len(times), ", ".join("%.3f" % t for t in times)))
gaps = [b - a for a, b in zip(times, times[1:])]
if gaps: print("STEPS gaps: mean %.3f s, min %.3f, max %.3f (a %.2f steps/s pace)" % (sum(gaps) / len(gaps), min(gaps), max(gaps), len(gaps) / sum(gaps)))

os.makedirs(OUT, exist_ok=True)
for fn in os.listdir(OUT):
    if fn.startswith("snow_step_") and fn.endswith(".wav"): os.remove(os.path.join(OUT, fn))
clips = []
for k, t in enumerate(times):
    s = max(0, int(t * SR) - PRE)
    end = int(times[k + 1] * SR) - PRE if k + 1 < len(times) else n
    end = min(end, s + int(MAXLEN * SR))
    seg = x[s:end]
    if len(seg) < int(0.08 * SR): continue
    rms = math.sqrt(sum(v * v for v in seg[:int(0.12 * SR)]) / min(len(seg), int(0.12 * SR)))
    clips.append((seg, rms, t))
target = sorted(c[1] for c in clips)[len(clips) // 2] if clips else 1.0
peak_all = max(max(abs(v) for v in c[0]) * target / max(1.0, c[1]) for c in clips) if clips else 1.0
norm = min(1.0, 29000.0 / peak_all)
lines = []
for k, (seg, rms, t) in enumerate(clips):
    g = target / max(1.0, rms) * norm
    m = len(seg)
    fade_in = int(0.002 * SR); fade_out = int(0.06 * SR)
    out = array.array("h")
    for i, v in enumerate(seg):
        a = g
        if i < fade_in: a *= i / fade_in
        if i > m - fade_out: a *= max(0.0, (m - i) / fade_out)
        out.append(int(max(-32767, min(32767, v * a))))
    path = os.path.join(OUT, "snow_step_%02d.wav" % k)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(out.tobytes())
    lines.append("%02d onset %.4f s in the source, %.3f s long, gain %.2f" % (k, t, m / SR, g))
with open(os.path.join(OUT, "Steps.txt"), "w") as fh:
    fh.write("Single footsteps cut from user_snow_steps.mp3 by Tools/CutSteps.py. Each clip's onset is 4 ms in.\n")
    fh.write("\n".join(lines) + "\n")
print("STEPS wrote %d clips to %s" % (len(clips), OUT))

if "--plot" in sys.argv:
    # a picture of the analysis: energy, onset strength, and the onsets found
    out = sys.argv[sys.argv.index("--plot") + 1]
    W, H = 1600, 360
    lo = min(energy); hi = max(energy)
    def px(f): return f / frames * W
    pts = " ".join("%.1f,%.1f" % (px(f), H - 40 - (energy[f] - lo) / (hi - lo) * (H - 80)) for f in range(0, frames, 2))
    pts2 = " ".join("%.1f,%.1f" % (px(f), H - 40 - (thr - lo) / (hi - lo) * (H - 80)) for f in (0, frames - 1))
    svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 1600 360"><rect width="100%%" height="100%%" fill="#fff"/>' % (W, H),
           '<polyline fill="none" stroke="#4a6fa5" stroke-width="1" points="%s"/>' % pts,
           '<polyline fill="none" stroke="#e07b39" stroke-width="1" points="%s"/>' % pts2]
    for t in times:
        X = t * SR / HOP / frames * W
        svg.append('<line x1="%.1f" y1="10" x2="%.1f" y2="%d" stroke="#c0392b" stroke-width="1.5"/>' % (X, X, H - 10))
    svg.append('<text x="10" y="20" font-family="sans-serif" font-size="14">user_snow_steps: high-passed energy (blue), burst threshold (orange), steps found (red), %d steps</text>' % len(times))
    svg.append("</svg>")
    open(out, "w").write("\n".join(svg))
    print("STEPS plot", out)
