# Summit Whiteout

An open-face alpine freeride run for phones — tilt to steer, or touch-only if
you would rather not. One self-contained `index.html`, rendered with three.js.

A helicopter puts you on the summit at 4,208 m, the cornice goes behind you,
and you ride 2.6 km down to the valley floor at 1,000 m — through all four
faces of the mountain. There is no jump button: the terrain launches you, and
holding **TUCK** and letting go at a lip throws you properly. Spin, flip and
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

**Tilt direction** on the start screen has a live marker under it. Tilt the
handset and watch it: if it runs the way you lean, you are set; if it fights
you, pick **Reversed**. The choice is remembered.

Pick **Snowboard** (loose edge, spins easily) or **Skis** (holds a line, carries
more speed) on the start screen, along with the route: **Descent** to the valley,
or **Endless**.

| Control | On the snow | In the air |
| --- | --- | --- |
| Tilt left / right, **or hold either side of the screen** | Carve | Spin |
| **TUCK** button | Hold to run straight and fast; **let go to pop** | Grab |
| **FLIP** button | — | Backflip |

**Controls** on the start screen (and the pause screen) switches between
**Tilt to steer** and **Touch only**. Touch-only steers by holding a side of the
screen — anywhere on the left half turns left, anywhere on the right turns
right. Nothing lights up when you do: the rider leaning into the carve is the
feedback. The turn eases in rather than jumping to full lock, so a stab is a
correction and a long hold is a committed carve. The two action buttons sit
outboard in the bottom corners, clear of the steering, so a steering thumb and
an action thumb never collide. The choice is remembered, and if motion access is
refused the game switches to touch-only on its own.

The first time you play, five prompts walk you through it — each one clears when
you actually do the thing, not on a timer.

**Re-centre** rezeros the tilt to however you happen to be holding the phone.
It does not matter how steeply you hold it — the steering reads true roll, not
raw `gamma`, so it behaves the same flat on a table or held upright.
On a desktop: arrow keys to steer, space or down to tuck and grab, F or up to flip.

Locally, any static server works (a plain `file://` open will not — the ES module
import needs an origin):

```sh
npx http-server -p 8080 .      # then http://localhost:8080
```

## The run

- **The tuck is the jump.** Holding **TUCK** locks the board dead straight —
  you cannot steer at all — and presses you into the snow, so the rollers stop
  throwing you and you just accumulate speed. Around 180 km/h against 100 for a
  rider who never tucks. Letting go springs you off: a metre or so on flat snow,
  and **twelve to fourteen metres off a lip**, because the terrain is already
  throwing you and the pop adds to it. The ring round the button is the charge.
  Hold, aim, release at the lip.
- **Carving costs speed.** The board keeps what points forward and scrubs what
  does not — though a carve *redirects* most of that sideways speed down the new
  line rather than deleting it. Holding a turn costs you; slamming edge to edge
  costs you a lot. It is your only brake, and your biggest liability.
- **Nothing fences you in, and going wide pays.** There is no corridor and no
  out-of-bounds. What shapes a run instead are **forks**, about three per
  kilometre, each marked by an orange gate you can see from 300 m up the hill
  and each sitting 40–110 m off the fall line, so you have to commit to go and
  get one. A fork is either a **cliff band** you huck or a **rock spine** with a
  long ramp up its face and a short drop off the back — a gap jump. Riding the
  line pays cans, points and a big push to the chain; going round is perfectly
  safe and pays nothing at all. That is the whole trade, and it is the only
  reason an open mountain is worth having. Wandering *aimlessly* off the fall
  line is still punished by the only thing that should punish it: the slide
  catches up.
- **The face has furniture.** Summit spires, serac towers and huts sit at real
  coordinates out to 300 m either side. They come out of the haze, hold still
  while you ride past, and are how you know you have actually travelled.
- **The mountain launches you.** Wind lips every ~85 m are shaped so their
  curvature beats gravity *above* a certain speed. Ride slowly and you roll over
  them; carry speed and you are airborne roughly a quarter of the time.
- **Land it.** The board has to be pointing within about 60° of your direction
  of travel, and flips have to finish — but let go of the steering in the air
  and the board settles back towards where you are going, and a half-finished
  flip rotates out to the nearest whole one. Steering over a roller never winds
  up a trick you then have to land. Genuinely sideways is still a wipeout.
- **The chain is the whole game.** Landing a trick, taking a can, threading a
  tree by a couple of metres, holding it above 120 km/h — all of it pushes one
  multiplier that never times out and multiplies everything you score. A wipeout
  resets it to ×1, which is the only punishment the game really needs.
- **Cans** sit in trails across the open snow and in ballistic arcs over every
  kicker, so the line that pays is the line with air on it. **Every can fires a
  boost**: a hard surge, double points, and enough speed that the next roller
  throws you properly. Taking them back to back stacks the timer rather than
  restarting it, so a whole trail of cans is one long surge — the five pips by
  the score are how much of it you have left.
- **Landing switch** — backwards, within about 40° of straight — pays 1.6× and
  pushes the chain harder than landing forwards.
- **Crevasses** are cut into the terrain itself, with **three kickers across the
  lip** so the crossing is findable from wherever you are on the face, and a
  warning when one is coming that you cannot yet see over the roller. None
  appear in the first 600 m.
- **The slide** sits about 120 m back while you ride clean and hauls in when a
  crash bleeds your speed — though it stops gaining for two and a half seconds
  after a wipeout, so one mistake does not cascade into three, and it comes up
  to speed over the first 260 m rather than being at full pace before a rider
  dropping in from a standstill is. **Clearing a sector shoves it 80 m back
  down the hill.**

The first 400 m are deliberately quiet, hazards in a row are never closer than
a rider can thread, and you are untouchable while you are picking yourself up.

- **Your ghost calls the time.** If you have a best run saved, the HUD shows a
  live **±seconds against it** — green when you are up, red when you are down.
- **The music tightens with the chain.** A drone in the sector's key, whose
  filter opens, upper voices arrive and pulse quickens the more you have to lose.

Sectors change every 650 m — glacier, couloir, treeline, forest — each with its
own hazards, colour, iciness and **light**: alpenglow on the glacier, flat cold
blue down the couloir, low warm sun once the valley walls are above you. The sun
itself drops towards the valley rim as you descend. The **Descent** is exactly
one cycle of the four, so you finish in the trees where a valley ought to be,
and the bar at the top of the screen tells you how much of it is left.

Every run banks into a **career total** — all the vertical you have ever ridden,
counted in Everests.

## How it is built

Single file, no build step, no assets. three.js is pinned and loaded from a CDN;
everything else is generated at runtime.

- **Terrain** is `height(x, z)`: a fall line, broad cross-slope ridges and bowls,
  four octaves of value noise, asymmetric wind lips, and — cut straight into the
  same field, so the mesh, the physics and the kill test can never disagree —
  crevasses, cliff bands and buttresses, each windowed to open ends rather than
  running on forever. A rolling 300 × 470 m mesh window re-samples it as you
  descend, so nothing about the world is ever stored. Hazards are generated in a
  band around wherever the rider actually is, not around a fixed course line.
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
- **The drop-in** is a seven-second cutscene on its own clock: a wide look down
  the fall line with the view pushed out to 520 m, the ship crossing the face,
  a step out of the door, a ballistic fall onto the snow, and the cornice
  letting go behind you. A tap skips it, and after the first one you get the
  short version — just the door and the drop.
- **The tilt** is reconstructed rather than read. Browser orientation angles are
  intrinsic Z-X'-Y'', so raw `gamma` measures rotation about an axis that has
  already been pitched by `beta`: at the 60-75 degrees people actually hold a
  handset it reads roughly two to four times the real roll, saturates after
  about twenty degrees of wrist, and past that hands the rotation to `beta` and
  flips sign — which is felt as the steering suddenly working backwards. So the
  gravity vector is rebuilt in the handset frame and the true roll taken from
  it, which is measured exact and sign-stable from 10 to 88 degrees of pitch.
- **Audio** is synthesised in the Web Audio API — one looping noise buffer
  through three filter chains for wind, carve and avalanche rumble, plus impact
  and pickup transients, plus a five-voice drone through one lowpass and a
  square-wave tremolo that the chain drives.
- **The light** is one 8 × 256 gradient strip repainted only when the palette has
  moved enough to see — about once a second over a four-minute descent, which
  costs 0.02 ms, and far less than cross-fading two sky spheres. Fog, both
  directional lights, the hemisphere and the sun's own position lerp with it.
- **Empty prop pools do not draw.** A pool hides its instanced mesh when nothing
  is in it, which is most of them most of the time — no seracs in the forest, no
  trees on the glacier.
- **Resolution** adapts: if the frame rate sits under 42 fps the renderer drops
  its pixel ratio rather than dropping frames. The terrain rebuild skips vertex
  normals (the material is flat-shaded, so they come from screen-space
  derivatives) and bounding spheres (the mesh is never culled), which is most of
  the reason a 43% wider window costs less CPU than the old narrow one.
