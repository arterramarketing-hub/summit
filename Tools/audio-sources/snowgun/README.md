# snow_gun.wav: source and processing

- Source: "LUMEKAHUR" by inspire153, Freesound id 842656, CC0. https://freesound.org/people/inspire153/sounds/842656/
  - A snow cannon at the Tallinn Song Festival Grounds, Estonia, recorded with a Schoeps shotgun mic about 180 m away, at -7 °C in calm wind, unprocessed.
- Downloaded file: `842656_16094772-hq.mp3`, Freesound's public HQ preview (mono, 48 kHz, ~175 kb/s, 2:21.8, 3.1 MB). Downloaded 2026-11-27 with the user's approval.
- Made into `Assets/Summit/Resources/Audio/snow_gun.wav` with ffmpeg:
  1. High-pass at 70 Hz; a +5 dB high shelf at 3.5 kHz (the hiss of the jets, brought closer than a 180 m recording).
  2. 40–56 s of it as a seamless loop: the 1 s after it crossfaded (equal power) into its first second, so the wrap is the recording carrying on.
  3. Mono, 44.1 kHz, 16-bit, +2.6 dB, giving -18.0 LUFS integrated with peaks at -5.6 dBFS (beside lift_hum's -16.9 LUFS).
- The recording is very steady (about -22.5 to -23 dBFS RMS throughout), so the loop has no swells or events in it.
