# Summit Whiteout

An open-face alpine freeride run for phones — tilt to steer, or touch-only if
you would rather not. One self-contained `index.html`, rendered with three.js.

You drop in at 4,208 m and ride down to the valley floor at 1,000 m — about
four minutes if you hold it together. There is no jump button: the terrain
launches you. Carry speed into a wind lip and it throws you; spin, flip and
grab on the way over; land pointing where you are actually going.

Everything you do feeds one **chain multiplier**, and a wipeout takes all of it.

The mountain is seeded by the day, so everyone rides the same face until
midnight, and your best run is saved as a **ghost** you race on the next one.

## Playing it

Open `index.html` on a phone **over HTTPS** — iOS only exposes motion sensors on
a secure origin, and iOS 13+ additionally requires a tap to grant access, which
is what the *Drop in* button does.

The gyroscope does exactly one thing: it steers. Everything else is an
on-screen button you hold, and which lights up while held.

Pick **Snowboard** (loose edge, spins easily) or **Skis** (holds a line, carries
more speed) on the start screen, along with the route: **Descent** to the valley,
or **Endless**.

| Control | On the snow | In the air |
| --- | --- | --- |
| Tilt left / right, **or hold either side of the screen** | Carve | Spin |
| **TUCK** button | Tuck for speed | Grab |
| **FLIP** button | — | Backflip |

**Controls** on the start screen (and the pause screen) switches between
**Tilt to steer** and **Touch only**. Touch-only steers by holding a side of the
screen — anywhere on the left half turns left, anywhere on the right turns
right, and the edge lights up while held. The turn eases in rather than jumping
to full lock, so a stab is a correction and a long hold is a committed carve.
The two action buttons sit outboard in the bottom corners, clear of the
steering, so a steering thumb and an action thumb never collide. The choice is
remembered, and if motion access is refused the game switches to touch-only on
its own.

**Re-centre** rezeros the tilt to however you happen to be holding the phone.
On a desktop: arrow keys to steer, space or down to tuck and grab, F or up to flip.

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
- **The chain is the whole game.** Landing a trick, taking a can, threading a
  tree by a couple of metres, holding it above 120 km/h — all of it pushes one
  multiplier that never times out and multiplies everything you score. A wipeout
  resets it to ×1, which is the only punishment the game really needs.
- **Cans** sit in trails across the open snow and in ballistic arcs over every
  kicker, so the line that pays is the line with air on it. **Every fifth can
  fires a boost**: a hard surge, double points, and enough speed that the next
  roller throws you properly.
- **Landing switch** — backwards, within about 40° of straight — pays 1.6× and
  pushes the chain harder than landing forwards.
- **Crevasses** are cut into the terrain itself. Every one has a kicker on its
  near lip — the gap is always makeable, but only if you use it.
- **The slide** sits about 120 m back while you ride clean and hauls in hard the
  moment a crash bleeds your speed. It never stops — but **clearing a sector
  shoves it 80 m back down the hill**, which is the run's only moment of relief.

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
