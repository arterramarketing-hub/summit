#!/usr/bin/env python3
"""
The board on ice (SoundMix: the ice layers), cut from three CC0 recordings on Freesound (Resources/CREDITS.txt):
  skate fx by splodge123 (249833): an ice-skate blade's edges, scrapes and skids in an empty rink
  Ice_BreakScrapeIce by Pipppo89 (665109): a hard ice scrape with a squeak
  ski_04 by blaukreuz (52151): stones skidding over lake ice, contact mics
Writes, into Assets/Summit/Resources/Audio (mono, 44.1 kHz, 16-bit, levels set beside skid/glide/carve):
  ice_scrape.wav   a loop: the blade's long scrapes, the edge grinding over the ice (with speed and the edge's bite)
  ice_hiss.wav     a loop: the same scrapes, only their top, thin and steady (the runner's hiss on ice at speed)
  ice_squeal.wav   a loop: the hard squeaking scrape, its highs (the edge set hard: high edge angle)
  ice_chatter.wav  a loop: the skids' rattle (the board juddering on the ice)
  ice_skid.wav     a one-shot: a skate's hard skid stop (an edge thrown sideways on ice)
Usage: python3 Tools/MakeIceScrape.py skate.mp3 scrape.mp3 ski04.mp3   (any format ffmpeg reads)
"""
import subprocess, sys, wave, os
import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Summit", "Resources", "Audio")


def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(RATE), "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


def cut(x, a, b):
    return x[int(a * RATE):int(b * RATE)].copy()


def filt(x, hp=None, lp=None):
    """A gentle FFT filter (high-pass at hp Hz, low-pass at lp Hz, each with an octave's slope)."""
    n = len(x); f = np.fft.rfftfreq(n, 1.0 / RATE); X = np.fft.rfft(x); g = np.ones_like(f)
    if hp: g *= np.clip(np.log2(np.maximum(f, 1.0) / hp) + 1.0, 0.0, 1.0)
    if lp: g *= np.clip(1.0 - np.log2(np.maximum(f, 1.0) / lp), 0.0, 1.0)
    return np.fft.irfft(X * g, n)


def even(x, win=0.15, most=2.5):
    """Evens out the level (slow): the quiet moments between scrapes lifted, the loud ones held (no gaps in a loop)."""
    w = int(win * RATE); k = np.ones(w) / w
    env = np.sqrt(np.convolve(x * x, k, mode="same")) + 1e-6
    g = np.minimum(np.median(env) / env, most)
    g = np.convolve(g, k, mode="same")
    return x * g


def join(parts, fade=0.12):
    out = parts[0]
    for p in parts[1:]:
        n = int(fade * RATE); t = np.linspace(0.0, 1.0, n)
        out = np.concatenate([out[:-n], out[-n:] * np.cos(t * np.pi / 2) + p[:n] * np.sin(t * np.pi / 2), p[n:]])
    return out


def loop(x, fade=0.25):
    """A seamless loop: its last `fade` seconds crossfaded (equal power) into its first."""
    n = int(fade * RATE); t = np.linspace(0.0, 1.0, n)
    body, head, tail = x[n:len(x) - n], x[:n], x[len(x) - n:]
    return np.concatenate([body, tail * np.cos(t * np.pi / 2) + head * np.sin(t * np.pi / 2)])


def level(x, mean_db):
    rms = np.sqrt(np.mean(x * x)) + 1e-9
    x = x * (10 ** (mean_db / 20.0) / rms)
    # (the few peaks over -1 dB rounded off, softly, rather than the whole clip turned down)
    return 0.89 * np.tanh(x / 0.89) if np.max(np.abs(x)) > 0.6 else x


def fades(x, a=0.005, b=0.08):
    na, nb = int(a * RATE), int(b * RATE)
    x[:na] *= np.linspace(0, 1, na); x[-nb:] *= np.linspace(1, 0, nb) ** 2
    return x


def save(name, x):
    pcm = (np.clip(x, -1.0, 1.0) * 32767.0).astype("<i2")
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(pcm.tobytes())
    print(f"{name}: {len(x) / RATE:.2f} s, rms {20 * np.log10(np.sqrt(np.mean(x * x)) + 1e-9):.1f} dB, peak {20 * np.log10(np.max(np.abs(x)) + 1e-9):.1f} dB")


def main():
    skate, squeak, ski = (load(p) for p in sys.argv[1:4])
    # the blade's long scrapes (4.8-7.4 s and 10.4-13.2 s: steady, the edge held on the ice), joined into one loop
    scr = join([cut(skate, 10.4, 13.25), cut(skate, 4.75, 7.45)])
    scr = even(filt(scr, hp=180.0, lp=9000.0))
    save("ice_scrape.wav", level(loop(scr), -19.0))
    # the runner's hiss: the same, its top only (thin, steady, no thump)
    save("ice_hiss.wav", level(loop(even(filt(scr, hp=2500.0), 0.08, 3.0)), -21.0))
    # the squeal: the hard scrape's squeak and grit, the highs (the whole recording, levelled: it is quiet)
    sq = even(filt(squeak, hp=900.0, lp=12000.0), 0.1, 4.0)
    save("ice_squeal.wav", level(loop(sq, 0.3), -20.0))
    # the chatter: the stones' skids and rattles (0.55-3.5 s), held level so the loop judders all through
    ch = even(filt(cut(ski, 0.55, 3.5), hp=120.0), 0.05, 3.0)
    save("ice_chatter.wav", level(loop(ch, 0.2), -20.0))
    # a hard skid stop (30.1-31.8 s), a one-shot
    sk = filt(cut(skate, 30.1, 31.8), hp=150.0)
    save("ice_skid.wav", fades(level(sk, -16.0)))


if __name__ == "__main__":
    main()
