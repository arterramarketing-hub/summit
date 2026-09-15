# Summit Whiteout

An open-face alpine freeride run for phones, steered entirely by the handset's
gyroscope. One self-contained `index.html`, rendered with three.js.

You drop in at 4,208 m and ride down a mountain generated from a pure function
of position. There is no jump button — the terrain launches you. Carry speed
into a wind lip and it throws you; spin, flip and grab on the way over; land
pointing where you are actually going. Three things are trying to end the run:
the mountain, the cold, and the avalanche behind you.

## Playing it

Open `index.html` on a phone **over HTTPS** — iOS only exposes motion sensors on
a secure origin, and iOS 13+ additionally requires a tap to grant access, which
is what the *Drop in* button does.

| Control | On the snow | In the air |
| --- | --- | --- |
| Tilt left / right | Carve | Spin |
| Tilt forward / back | Tuck for speed | Front / back flip |
| Hold the screen | — | Grab (left / centre / right = method / indy / stalefish) |

**Re-centre** rezeros both tilt axes to however you happen to be holding the
phone. Without a gyroscope: drag to steer and flip, tap and hold to grab, or use
the arrow keys and space.

Locally, any static server works (a plain `file://` open will not — the ES module
import needs an origin):

```sh
npx http-server -p 8080 .      # then http://localhost:8080
```

## The run

- **Carving costs speed.** The board keeps what points forward and scrubs what
  does not — though a carve *redirects* most of that sideways speed down the new
  line rather than deleting it. Holding a turn costs you; slamming edge to edge
  costs you a lot. It is your only brake, and your biggest liability.
- **The mountain launches you.** Wind lips every ~85 m are shaped so their
  curvature beats gravity *above* a certain speed. Ride slowly and you roll over
  them; carry speed and you are airborne roughly a quarter of the time.
- **Land it.** The board has to be pointing within about 40° of your direction
  of travel, and flips have to finish. Sideways is a wipeout: no control, no
  speed, and the avalanche takes back the metres.
- **Cans** sit in trails across the open snow and in ballistic arcs over every
  kicker, so the line that pays is the line with air on it. They chain, and
  **every fifth can fires a boost**: a hard surge, double points, and enough
  speed that the next roller throws you properly. Crashing ends it early.
- **Crevasses** are cut into the terrain itself. Every one has a kicker on its
  near lip — the gap is always makeable, but only if you use it.
- **Body heat** drains with wind chill and drains faster in a whiteout. Ride
  through a bonfire to reload it; at zero you start losing condition.
- **The slide** sits about 120 m back while you ride clean and hauls in hard the
  moment a crash bleeds your speed. It never stops.

Sectors change every 1,350 m of descent — glacier, couloir, treeline, forest —
each with its own hazards, colour and iciness, and each cycle steeper than the last.

## How it is built

Single file, no build step, no assets. three.js is pinned and loaded from a CDN;
everything else is generated at runtime.

- **Terrain** is `height(x, z)`: a fall line, spines and bowls, three octaves of
  value noise, asymmetric wind lips, rising shoulder ridges, and crevasses cut
  straight into the field so the mesh, the physics and the kill test all agree
  on exactly where the hole is. A rolling 210 × 470 m mesh window re-samples it
  as you descend, so nothing about the world is ever stored.
- **Physics** runs off the real surface gradient. Gravity is resolved along the
  slope, so bowls hold you and spines shed you; the board's edge grip decomposes
  velocity into along-board and across-board components and scrubs the second.
  Vertically the rider is always a ballistic particle — air happens when the
  ground drops away faster than gravity can follow it, which is why the terrain
  itself is the jump button. Gradients are clamped, because a crevasse lip is a
  near-vertical wall.
- **Collision** is a swept circle against the path actually travelled that
  frame — no tunnelling at 40 m/s — with a real vertical extent, so anything you
  cleared is cleared. Radii match the visible geometry: a pine is a 0.46 m trunk
  that wrecks you inside a 1.95 m canopy that only slows you.
- **The track** is one trench, not a stripe: three vertices per sample give a
  dark groove with snow pushed up either side, widening as the edge goes over.
  Airborne samples are written with zero intensity, so the track breaks at the
  takeoff and picks up at the landing instead of being wiped. Spray is thrown
  out to the side the edge is sliding towards.
- **Props** are instanced meshes drawn from slot pools, generated ahead of you
  and recycled behind. Each prop type is baked from several primitives into one
  vertex-coloured buffer, so a forest is one draw call.
- **Audio** is synthesised in the Web Audio API — one looping noise buffer
  through three filter chains for wind, carve and avalanche rumble, plus impact
  and pickup transients.
- **Resolution** adapts: if the frame rate sits under 42 fps the renderer drops
  its pixel ratio rather than dropping frames.
