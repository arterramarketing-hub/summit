# Summit Whiteout

An endless procedural snowboarding descent for phones, steered entirely by the
handset's gyroscope. One self-contained `index.html`, rendered with three.js.

Tilt to carve down a gully that is generated from a pure function of position —
so the mountain is infinite, seamless, and identical every time you pass the same
point. Meanwhile three things are trying to end the run: the trees, the cold, and
the avalanche behind you.

## Playing it

Open `index.html` on a phone **over HTTPS** — iOS only exposes motion sensors on
a secure origin, and iOS 13+ additionally requires a tap to grant access, which
is what the *Drop in* button does.

| Control | |
| --- | --- |
| Tilt the handset left / right | Carve. Hold it in portrait. |
| Drag anywhere | Fallback steering when there is no gyroscope. |
| `←` `→` or `A` `D` | Desktop steering. |
| **Re-centre** | Rezero the tilt to however you are holding the phone. |

Locally, any static server works (a plain `file://` open will not — the ES module
import needs an origin):

```sh
npx http-server -p 8080 .      # then http://localhost:8080
```

## The run

- **Carving costs speed.** Turning across the fall line scrubs velocity, which is
  your only brake and your biggest liability.
- **The slide** sits roughly 120 m back while you ride clean and hauls in hard
  the moment a crash bleeds your speed. It never stops and it never gets bored.
- **Body heat** drains with wind chill and drains faster in a whiteout. Ride
  through a bonfire to reload it; at zero you start losing condition.
- **Condition** takes the hits — trees, boulders, and scraping the rock banks.
- **Points** come from distance, close passes (which chain into a combo), air off
  the snow kickers, and every fire you reach.

## How it is built

Single file, no build step, no assets. three.js is pinned and loaded from a CDN;
everything else is generated at runtime.

- **Terrain** is `height(x, z)` — a fall line, a meandering gully centre, quadratic
  rock banks, and two octaves of value noise. A rolling 156 × 430 m mesh window
  re-samples that field as you descend, so nothing about the world is ever stored.
- **Props** (pines, boulders, kickers, slalom wands) are instanced meshes drawn
  from slot pools, generated ahead of you and recycled behind.
- **Geometry** is baked: each prop is many primitives merged into one
  vertex-coloured buffer, so a forest is one draw call.
- **Audio** is synthesised in the Web Audio API — one looping noise buffer through
  three filter chains for wind, carve, and avalanche rumble, plus impact and
  pickup transients.
- **Resolution** adapts: if the frame rate sits under 42 fps the renderer drops
  its pixel ratio rather than dropping frames.
