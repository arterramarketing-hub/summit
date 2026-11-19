# Synthesises the chairlift's two sounds and writes them as 16-bit mono WAVs, in the spirit of Tools/MakeAudio.swift:
# noise, oscillators and resonators, no recordings.
#   python3 Tools/MakeLiftAudio.py Assets/Summit/Resources/Audio
#
#   lift_hum.wav    a loop: the drone of the drive and the line running over the sheaves, heard from a little way off
#   lift_clack.wav  a chair's grip crossing a tower's sheave train: a quick run of knocks on steel

import math, os, random, struct, sys, wave

SR = 44100
TAU = 2 * math.pi
OUT = sys.argv[1] if len(sys.argv) > 1 else "."


def write(name, samples, peak=0.8):
    top = max(1e-9, max(abs(s) for s in samples))
    data = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s / top * peak)) * 32767)) for s in samples)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(data)
    print("wrote", name, "%.2f s" % (len(samples) / SR))


def lowpass(x, hz):
    a = 1 - math.exp(-TAU * hz / SR)
    y, o = 0.0, []
    for s in x:
        y += a * (s - y); o.append(y)
    return o


def highpass(x, hz):
    lp = lowpass(x, hz)
    return [s - l for s, l in zip(x, lp)]


def resonator(x, hz, q):
    """A two-pole band-pass ringing at hz."""
    w = TAU * hz / SR
    r = math.exp(-w / (2 * q))
    a1, a2 = 2 * r * math.cos(w), -r * r
    y1 = y2 = 0.0
    o = []
    for s in x:
        y = s * (1 - r) + a1 * y1 + a2 * y2
        y2, y1 = y1, y
        o.append(y)
    return o


def hum():
    """Four seconds that loop: every tone is a whole number of cycles long, and the noise is cross-faded end to start."""
    rng = random.Random(7)
    T = 4.0
    n = int(T * SR)
    fade = int(0.6 * SR)
    # the drive: a low drone with its harmonics, slowly beating
    tones = [(48.0, 1.0), (96.0, 0.55), (144.25, 0.3), (192.5, 0.16), (97.25, 0.3), (383.75, 0.05), (767.5, 0.02)]
    out = [0.0] * n
    for hz, amp in tones:
        hz = round(hz * T) / T
        ph = rng.random() * TAU
        for i in range(n):
            out[i] += amp * math.sin(TAU * hz * i / SR + ph)
    # the line over the sheaves: a soft rolling rumble, and the faint hiss of the rope
    noise = [rng.uniform(-1, 1) for _ in range(n + fade)]
    rumble = lowpass(lowpass(noise, 140), 140)
    hiss = highpass(lowpass(noise, 2600), 900)
    for i in range(n + fade):
        wob = 0.75 + 0.25 * math.sin(TAU * 1.5 * i / SR) * math.sin(TAU * 0.25 * i / SR)
        noise[i] = rumble[i] * 9.0 * wob + hiss[i] * 0.22
    for i in range(n):
        s = noise[i]
        if i < fade:   # the tail, faded out over the head
            k = i / fade
            s = s * k + noise[n + i] * (1 - k)
        out[i] = out[i] * 0.5 + s
    return out


def clack():
    rng = random.Random(11)
    n = int(0.55 * SR)
    out = [0.0] * n
    # six wheels pass under the grip in three pairs: knocks 45 ms apart, the middle ones loudest
    for k, (at, amp) in enumerate(((0.0, 0.6), (0.05, 1.0), (0.105, 0.85), (0.15, 0.5), (0.21, 0.3))):
        burst = [0.0] * n
        i0 = int(at * SR)
        for i in range(int(0.004 * SR)):
            burst[i0 + i] = rng.uniform(-1, 1) * (1 - i / (0.004 * SR))
        ring = [0.0] * n
        for hz, q, a in ((310.0, 9, 1.0), (820.0 + 40 * k, 14, 0.7), (1940.0 - 60 * k, 20, 0.5), (3300.0, 16, 0.25)):
            r = resonator(burst, hz, q)
            for i in range(i0, n):
                ring[i] += r[i] * a
        thud = lowpass(burst, 180)
        for i in range(i0, n):
            out[i] += amp * (ring[i] * 6.0 + thud[i] * 3.0)
    # a little of the hanger's rattle after it
    tail = highpass(lowpass([rng.uniform(-1, 1) for _ in range(n)], 3200), 1200)
    for i in range(n):
        t = i / SR
        out[i] += tail[i] * 0.05 * math.exp(-t * 9) * (0.5 + 0.5 * math.sin(TAU * 31 * t))
        out[i] *= min(1.0, (n - i) / (0.05 * SR))
    return out


os.makedirs(OUT, exist_ok=True)
write("lift_hum.wav", hum(), 0.7)
write("lift_clack.wav", clack(), 0.85)
