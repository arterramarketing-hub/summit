# Summit Whiteout

An open-face alpine freeride run for phones — tilt to steer, or touch-only if
you would rather not. One self-contained `index.html`, rendered with three.js.

A helicopter puts you on the summit at 4,208 m, the cornice goes behind you,
and you ride 3.9 km down to the valley floor at 1,000 m — through all six faces
of the mountain, out of the bottom of a cloud deck, and under a finish banner. There is no jump button: the terrain launches you, and
holding **TUCK** and letting go at a lip throws you properly. Spin, flip and
grab on the way over; land pointing where you are actually going.

Everything you do feeds one **chain multiplier**, and a wipeout takes all of it.

The mountain is seeded by the day, so everyone rides the same face until
midnight, and your best run is saved as a **ghost** you race on the next one.

## Getting to the bottom

Crossing the line does not stop the game and put a menu on the screen — which
reads exactly like dying, and was read exactly like dying. You cross the banner
at whatever you carried in — sixty, a hundred — run another eighty to a hundred
and fifteen metres out across the valley floor, put it on edge and throw the
wall of snow that everybody who has ever finished a run throws,
and the camera comes round in front of you and settles before anything is asked
of you. Eleven seconds, or a tap. Then a screen that is green where the losing
ones are ember, and says so.

And there is something to arrive at. The face **lies down** over its last 420
m — the gradient eases from 0.40 to 0.055 — and the line sits 200 m into that
rather than at the end of it, so you cross the banner still carrying speed and
the 220 m past it are where the floor actually flattens and where you stop.
Standing on it: a timber lodge with its windows lit, a lift station with the
bullwheel the cable turns round, six towers carrying that cable back up the
hill, safety netting funnelling into the line, two snowcats parked up, and
woods thick all round the outside. Nothing down there is a hazard — you have
arrived, and the bottom of the mountain is not allowed to take that away from
you.

## The drop-in

The title screen is a drone standing three kilometres off down the valley,
sweeping a narrow arc across the face: the peak with its flag on top, a range
of other mountains on the horizon behind it, and you as a speck a long way
below. Tapping **Drop in** flies that same camera into the cutscene rather than
cutting to it — a wide look up at the summit, and the ship crossing the
mountain and settling into a hover over the drop point.

And then it waits. **You have to jump.** The ship holds there with its clock
stopped and the snow going up underneath it until you tap, which is the one
moment of the opening that is actually yours and used to be something that
happened to you on a timer. Tap and you push off the skid, fall onto the snow,
and the cornice lets go behind you. It goes by itself after nine seconds,
because a game that can be stuck on its own opening is a game that is stuck.

## Playing it

Open `index.html` on a phone **over HTTPS** — iOS only exposes motion sensors on
a secure origin, and iOS 13+ additionally requires a tap to grant access, which
is what the *Drop in* button does.

The gyroscope does exactly one thing: it steers. Everything else is an
on-screen button you hold, and which lights up while held — plus the sides of
the screen, which carve when you hold them and spin you when you tap them in
the air.

**Tilt direction** on the start screen has a live marker under it. Tilt the
handset and watch it: if it runs the way you lean, you are set; if it fights
you, pick **Reversed**. The choice is remembered.

Pick **Snowboard** (loose edge, spins easily) or **Skis** (holds a line, carries
more speed) on the start screen, along with the route: **Descent** to the valley,
or **Endless**.

| Control | On the snow | In the air |
| --- | --- | --- |
| Tilt left / right, **or hold either side of the screen** | Carve | **Steer the flight** — swings the whole arc round, about 23 m either way on a normal jump |
| **Tap** either side of the screen | — | **Half a turn.** They queue and they chain |
| **TUCK** button | Hold to run straight and fast; **let go to pop** | Grab |
| **FLIP** button | — | Backflip |

**Spin is a tap, not a lean.** It used to accumulate at a rate while a side was
held, so how far you came round depended on how long you leaned and nothing
ever landed on a round number. Now a tap is exactly a hundred and eighty
degrees: one is a switch landing, two is a clean three-sixty, and with nothing
queued the board settles onto whichever stance is nearer rather than always
onto forwards. Landing sideways stops being something that happens to you.

**Land backwards and you stay backwards.** A switch landing pays 1.6× and a
chain step, and from then on you are riding with the board pointing at you
until you spin out of it or crash. The badge on screen says so while it lasts.
It used to mirror the steering as well — lean right, go left — on the grounds
that the board is pointing at you and that ought to cost something. It cost
the wrong thing: riding switch stopped being a flourish you land and became a
punishment you sit out until you could undo it.

**View** on the start screen switches between the chase camera and **first
person**: bolted to the rider's own head rather than lerped towards it, so the
mountain arrives at the speed you are actually going and a tap-spin takes the
whole world round with it. A switch landing is the best of it — you come round
the last ninety degrees, the board takes the snow backwards, and your head
whips forward to find the hill again.

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
On a desktop: arrow keys to steer — a press in the air is half a turn — space
or down to tuck and grab, F or up to flip.

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
- **Nothing leaves you stuck.** The board cannot be held more than about eighty
  degrees off the way you are actually travelling — on snow it would wash out
  long before that — and below 25 km/h the rider points it downhill and skates,
  at full strength from a standstill and gone by the time there is any speed to
  speak of. Hollows, the back of a wind lip and the aftermath of a crash all
  used to be places a run could simply stop.
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
- **The mountain launches you, but it waits to be asked.** Wind lips every
  ~167 m are shaped so their curvature beats gravity *above* a certain speed.
  Ride slowly and you roll over them; carry speed and one throws you properly.
  They used to come every 85 m, which measured out at a takeoff every 79 m of
  descent and **30% of the run spent off the snow** — a jump every three
  seconds whether you wanted one or not. It is 4.8 takeoffs a kilometre and
  **8%** now, at the same speed, and the airs that are left are worth having.
- **It is a mountain you carve down.** The line the face drifts along used to
  wander on a three-kilometre and a one-kilometre term — a lean, not a rhythm,
  and you could hold it with one nudge a minute. There is a 167 m term on it
  now, which at riding speed is a turn every three seconds. Measured: **104 m
  of side-to-side per kilometre of descent before, 236 m after.**
- **And there are slalom courses.** Runs of seven to twelve poles, red and blue,
  alternating either side of the drift line at a 38–48 m pitch, one every three
  or four hundred metres. Going round the outside of every one pays a clean
  run: three chain steps and a bonus. Going straight through the middle costs
  nothing and pays nothing, which is the same bargain the cliff bands offer.
  Clip one and it goes over — a slalom pole is hinged at the base and has never
  put anybody down, so it takes a little speed and breaks the run and that is
  all.
- **Height never hurts you.** There is no fall damage at all: this mountain can
  throw you three hundred metres off a cliff with a pop and you will land on
  full condition. The only thing a landing asks is that the board is pointing
  where you are going. What a big one costs is nothing and what it pays is
  **fourteen points a metre dropped**, times the chain — and a wave of snow
  that runs out from under the board across the whole face.
- **You can steer in the air.** Leaning pushes the whole arc sideways — about
  thirty metres over two seconds of air — so a landing is something you pick
  rather than something that was decided the moment you left the ground. It
  winds up the spin at the same time, which is the trade.
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

Sectors change every 650 m — glacier, couloir, treeline, forest, **the cloud
deck** and **the valley floor** — each with its own hazards, colour, iciness and
**light**: alpenglow on the glacier, flat cold
blue down the couloir, low warm sun once the valley walls are above you. The sun
itself drops towards the valley rim as you descend. The **Descent** is exactly one cycle of
the six. Somewhere around 2,000 m you ride down into a sea of cloud, lose the
world for a few seconds and come out of the bottom of it with the valley
underneath you; the bar at the top of the screen turns green for the last
stretch, and the run ends by crossing a line rather than by a panel appearing.

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
- **Heading is wrapped, every frame.** It was not, and a crash could leave it
  at 4.75 radians; the recovery then eased `target - heading` on the raw
  difference and unwound the long way round — 4.75 radians back to zero rather
  than the 1.5 forward — passing through *facing directly uphill* on the way.
  The rider was sliding backwards along the board at 14 m/s at the time, the
  edge held it, and that speed got rotated into 18 m/s straight back up the
  mountain, for five seconds at a stretch. Spins are counted by their own
  accumulator, so wrapping the facing costs the trick system nothing.
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
- **The slam** is a ring of displaced snow: a raised crest between two edges
  pinned to the real surface, every vertex resampled off `groundAt` each frame
  so it rides the bumps instead of cutting through them. The band is held to a
  third of its own radius — any wider and it reads as a disc rather than a wave
  — with a per-segment wobble so it is never a clean circle, a shadowed trough
  behind the crest, powder thrown outward in a full ring, and a kick on the
  lens. It scales with the drop, so a hop does nothing and a cliff moves the
  mountain.
- **The track** is one trench, not a stripe. Three vertices a sample — lip,
  groove, lip — could only draw that as a line, and it read as one: a hairline
  behind the rider. Six gives the section a floor with real width and a wall
  either side, and the walls are what make it a cut you can see the depth of:
  the one the sun is off goes dark, the one it is on stays pale. Nothing here
  is lit — it is a decal on the snow, not geometry in it, and a trench modelled
  below the surface would simply be occluded by it — so that asymmetry is the
  only thing standing in for a shadow in the cut. Five vertices was not enough
  either: with a single vertex down the middle the floor is a hairline the two
  walls interpolate away, and what you get is a pale stripe with bright edges.
  Airborne samples are written with zero intensity, so the track breaks at the
  takeoff and picks up at the landing instead of being wiped. Spray is thrown
  out to the side the edge is sliding towards.
- **The crash is integrated, not played back.** It used to be three angles
  ramped off one timer: the same fall however fast you were going and whatever
  took your feet away. What it should be — what you can feel it ought to be —
  is a body that has just had its feet taken at thirty metres a second and
  nowhere to put that momentum but into rolling. So the tumble is an angular
  velocity seeded from the speed and the direction of travel, a quaternion
  stepped by it, and the snow scrubbing both away over however long that
  takes; most of the roll is about the axis ACROSS the way you were going,
  because that is the axis the snow grabbed — the feet stop and the rest of
  you keeps going, which is a pitch forward and not a spin. The body is
  ballistic too, and bounces, and the snow only gets to scrub the spin while
  it is actually touching it.
- **And the kit goes with it.** The rider's geometry swaps to a body-only bake
  for the length of the crash, and everything they were wearing or standing on
  becomes a loose object with its own velocity, its own spin and its own
  bounce: the board, or both skis and both poles, plus goggles and gloves.
  Each one is baked about its own centre so it tumbles round itself rather
  than orbiting the point the rider used to stand on. It keeps *some* of the
  speed it had and not most of it — at seventy per cent of thirty metres a
  second, on a face that falls away at 0.4, a glove never catches up with the
  ground: it sails three hundred metres and lands in the next sector, which is
  correct and looks ridiculous. At thirty per cent, with a bit of drag and its
  own share of the bang in its own direction, it comes to rest five to sixteen
  metres away, which is a crash you can see the shape of. In first person the
  camera rides the tumble: the head offset goes through the same quaternion,
  so you come off, the world goes over, and you end up looking at the sky.
- **Props** are instanced meshes drawn from slot pools, generated ahead of you
  and recycled behind. Each prop type is baked from several primitives into one
  vertex-coloured buffer, so a forest is one draw call.
- **Tap and hold share one thumb**, so they are told apart by how long it stays
  down. Tilt mode has nothing to tell apart — the gyro does all the carving —
  so there the spin goes the instant the thumb lands, which is the whole point
  of a tap. In touch mode the same side both carves and spins, so the spin
  waits for the release to know which one it was.
- **The stance is one number**, and everything on the snow is measured from it
  rather than from zero: the eighty-degree limit on how far the board can be
  held off the line you are travelling, the carve target, the skate that gets
  you out of a hollow, and the axis the edge grips along — which is taken from
  whichever end of the board is leading, so riding switch the drag, the carve
  and the boost all still push you down the hill instead of back up it.
- **Nothing floats.** Every prop used to be pinned by its origin and then drawn
  dead level, on a face that rolls by up to half a metre per metre across the
  slope: measured, the corners of a seven-metre kicker's footprint disagreed by
  4.8 m, and 86% of kickers had a corner more than half a metre in the air.
  Each prop now reads the lie of the land across its *own* size and lies down
  on it — a boulder takes all of that slope, a tree almost none, because trees
  grow up whatever they are standing on — and then settles, bringing its
  highest corner down to the snow so the curvature a tangent plane cannot
  follow is buried rather than hanging. Kickers get their site chosen rather
  than given, a handful of spots either side and the flattest one wins, and
  their physics surface drops with them so what launches you is what you can
  see. Measured after: not one prop with a corner off the ground, and the
  kickers are the same height they always were.
- **The drop-in** is a cutscene on its own clock: a wide look down the fall
  line with the view pushed out, the ship crossing the face, a hover, a jump
  out of the door, a ballistic fall onto the snow, and the cornice letting go
  behind you. After the first one you get the short version — just the door
  and the jump.
- **The clock stops in the hover**, which is what makes the jump yours rather
  than the timer's. Everything downstream of the drop — the cornice, the swing
  round to watch it, the hand-off into the chase seat, the fog opening out —
  is keyed to that one clock, so holding it is one accumulator subtracted from
  it rather than five beats rescheduled. The camera keeps crawling in and
  round on the wait, because a held shot and a frozen one do not read the same.
- **One tap, three meanings**, in the order they come up: jump out of the ship
  if it is hovering, wave the ending on if it is running, and otherwise walk
  out of the cutscene — which a cutscene must always allow.
- **The ride-out is three parts.** It used to brake from the instant you
  crossed the line and stop inside twenty metres, which is not arriving
  anywhere, it is being switched off. Now: 4.2 s of riding on four tenths of
  the hill's gravity — full gravity and you cross the line and speed *up* —
  then 2.4 s of edge, turned towards the middle of the piste so it reads as
  something you did, and then you stand there while the camera comes round.
  Measured: 77 m past the line arriving at 60 km/h, 114 m arriving at 100,
  against the old 20 whatever you arrived at.
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
  square-wave tremolo that the chain drives. Two seconds of noise is 88,200
  samples of JavaScript loop, so it is generated **once** and every transient
  reads it from a random offset — they still all sound different, and nothing
  synthesises a buffer mid-run. The mix is sent twelve times a second rather
  than sixty, and only values that actually moved are sent at all, which took
  the automation traffic from 529 messages a second to 18.
- **An oscillator wired to a gain's AudioParam is summed with it, not
  multiplied by it.** The helicopter is filtered noise chopped by a blade-rate
  square wave, and that square was connected straight onto the rotor bus's own
  gain — so turning the rotor down to nothing still left the blades swinging
  that gain by ±0.55, a sign flip twenty-five times a second on a live noise
  stream, for the entire run. It was heard as static. The chop is now its own
  stage, swinging zero to one, with a level stage behind it that can actually
  close: measured, a third of everything audible after the ship had left was
  the ship.
- **The light** is one sky texture repainted only when the palette has moved
  enough to see — about once a second over a four-minute descent, and far less
  than cross-fading two sky spheres. Fog, both directional lights, the
  hemisphere and the sun's own position lerp with it.
- **The sky used to be eight pixels wide:** one vertical ramp, identical in
  every direction, so the warm band sat all the way round the horizon at equal
  strength. A sky does not do that. The warmth is where the sun is, and the
  further round from it you look the colder and deeper it gets — which on a
  mountain where everything else is white is most of what tells you which way
  you are facing. So it is a 512 × 256 map now: the same ramp for elevation,
  plus the sun's own halo painted in at the sun's own bearing (three times
  over, so a halo near the seam comes back the other side), plus a sun disc
  inside the glow, because a four-hundred-metre smear is a glow and not a sun.
  Two to one, not square, because that is the shape that makes a degree across
  the sky the same number of texels as a degree up it: at one to one a wisp of
  cloud comes out twice as wide as it was drawn and the sun's halo comes out an
  ellipse.
- **A constant row in that map is a constant *elevation*,** which is a circle
  round the zenith — so the first cirrus, drawn as long flat ellipses along
  constant rows, put contour lines in the sky. A cloud is a short string of
  soft puffs along its own tilted axis now: nothing runs far enough to close
  into a ring, a tilt makes a streak climb through elevations the way a real
  one does, and the band sits low, between about six and thirty-six degrees,
  where the projection is not yet crowding the meridians together.
- **Stars are points, not texels.** Painted into that map they were 1.4 degrees
  across — three times the width of the moon — and every one came out a hard
  white square. Four hundred and twenty of them are a point cloud now, sized in
  screen pixels, riding the sky sphere so they inherit its radius and its
  camera-following position, still depth-testing so a peak in front of them
  puts them out, and faded towards the horizon where you would be looking
  through the whole atmosphere. They come out when the top of the sky is dark
  enough to hold them, which on this mountain is a question about the sector.
- **The summit, and the flag on it.** The fall line used to rise for ever, so
  there was no peak and no sense of scale. Above the drop-in the face rolls
  over — the gradient eases to nothing and then goes negative — the flanks fall
  away from the crest so it is a peak and not a ridge, and a cone on top of
  that gives it an actual point, 550 m above where you are dropped. A dome of
  white against a pale sky is still nothing you can pick out, so the top third
  goes above the snow line into dark rock, on a ragged edge rather than a drawn
  one, and a forty-six-metre mast with a red flag stands on the apex. It is
  absurdly outsized, because from the valley floor anything the size of a real
  flag is under a pixel and being seen from down there is the whole job. The
  flag stands on the height the mesh *draws*, not the height the field
  computes: at eighty metres a quad the mesh cuts the corner off a peak, and
  the difference is thirty metres of daylight under the pole. Every metre the
  rider ever touches is below all of this, where the height field is exactly
  what it always was.
- **The range** is in the height field, not in a ring of billboards, so it
  takes the same light, the same fog and the same weather the mountain does and
  it parallaxes because it is actually out there. Fourteen peaks, all of them
  *behind* the summit: a portrait frame is thirty-eight degrees wide even at a
  seventy-four degree lens, so from the drone's stand-off there is about
  thirteen hundred metres either side of the peak to work with and anything
  further out is simply not in the shot. Every peak's skirt is pushed clear of
  the corridor, which is what lets the lookup answer *nothing here* for the
  whole playable world in two comparisons. The back of the summit levels out
  onto a plateau rather than falling for ever, because a range that has to
  climb a kilometre before it clears the ground is a range of spikes.
- **The skyline you ride against** is not the range: riding, the weather only
  lets you see three hundred metres, so the real one is fogged out of existence
  and thirty-nine unfogged peaks stand in for it beyond the fog wall, lagged
  rather than pinned so they parallax like something two kilometres out instead
  of sitting there like wallpaper. They were five-sided cones in one flat
  colour, which is a traffic cone, not a mountain. Each one is baked now with
  an irregular footprint — the same per-ray scale all the way up, so a long ray
  is a spur running out and a short one is a steep face; fade that scale
  towards the apex instead and every peak flares at the bottom and pinches at
  the top, which is a haystack. The crest is not a point either: every ray tops
  out somewhere of its own, so a peak gets a main summit, a shoulder and a
  saddle between them, with a lid over it so the saddle is a saddle and not a
  hole. Six shapes are baked in all, three per range, and each instance takes
  its own height, width, aspect and yaw on top of that.
- **A snowline is an elevation,** so it is one height per peak with a little
  raggedness across the faces, not a number per face — and it has to sit under
  the *lowest* point of the crest or a shoulder tops out below its own snow and
  the summit comes out bare rock with a white band beneath it, which is exactly
  what the first version did. The line itself is narrow: widen it and the cap
  stops being a cap and turns into a wash running down the whole face. The
  shading is baked lit from local bearing zero and every instance is then yawed
  to aim that side at the sun, so the range relights itself whenever the
  palette moves — on a half-lambert ramp, because a hard terminator on a
  five-sided peak is two facets lit and three black, and into a blue rather
  than a grey, because snow in shadow is blue and a peak that merely dims on
  its back side is a cardboard cut-out with better corners on it.
- **A peak goes into the bank at the waterline,** over the last few per cent of
  its height, and this took three attempts. Cut flat at its base you can see
  exactly where it ends — a silhouette standing in a tray. Faded gradually up
  from that base, the whole buried half comes out as a broad pale shelf lying
  across the bank, because it only gets *most* of the way to the fog colour.
  What works is the horizon: below it the sky is the fog colour to the byte, so
  a foot that reaches the fog colour there is genuinely gone. Which is also why
  every peak is buried by the same *fraction* of itself rather than the same
  number of metres — that is what lets one baked fade height serve a range
  whose peaks run from 210 m to a kilometre.
- **There are two ranges, not one,** at 1.1–1.7 km and 2.0–2.6 km, and the far
  one is washed seventy per cent of the way to white where the near one is
  washed fourteen — because what says *twenty kilometres* is not size, it is
  how much air is in front of it. The material colour is the fog colour
  exactly, with the brightening baked into the peaks instead as a gain above
  one: tint the material down towards the fog, as the flat cones did, and a
  sunlit cap can only ever come out a darker shade of the sky, and the whole
  point of a skyline is the white edge along the top of it — and tint it *up*
  and a dissolved foot no longer lands on the bank. The near range draws first
  so the depth buffer throws the far one away where it is hidden: these are the
  biggest polygons in the frame and back to front pays for every one of those
  pixels twice. Drawn back to front, thirty-nine peaks in two ranges cost about
  4% of the frame against the twenty-eight flat cones they replaced; drawn
  near-first they are inside the noise floor, under a frame either way over
  repeated runs.
- **The skyline stands down for the cloud deck as well as the weather.** It is
  unfogged on purpose, which means nothing else can take it away — so it has to
  fade itself, or you ride through the inside of a cloud with two kilometres of
  mountain range showing through it.
- **The clip planes move with the shot,** and this was the bug that hid all of
  the above. Riding, nothing is further off than the weather lets you see and
  the near plane has to be centimetres because the rider is two metres from the
  lens. From the drone the mountain is three kilometres away and the range
  behind it is nine — and a far plane fixed at 3,000 m was quietly cutting them
  out of the frustum after they had been built, lit and fogged.
- **Relief, which is the half of depth that shading alone never gives you.**
  Which way a face is pointing is the easy half; the other half is whether it
  is the top of a roller or the bottom of a hollow, and that is the half that
  reads as depth. A centred second difference over the finished vertex
  positions says which — it needs the vertex after as well as the one before,
  so it is a second pass, and a second pass of array reads is cheap against a
  first one paying for a `height()` call on every vertex. Hollows take the
  sky's colour and lose the sun; crests catch it. Underneath that, the same
  snow height field is baked a second time as a **normal map**, so the wind
  ripples themselves catch the light on one side and lose it on the other.
  A normal map rather than a bump map, because three's bump map is derivative
  based — it samples the texture three times per fragment to work out its own
  slope, and that measured 16 fps down to 10. The slope is already known here;
  it is a height field this file generated. Baked in once, it costs a single
  fetch: 10.4 fps down to 9.6. The grain is in the colour copy only and
  nowhere near the relief one — noise that changes every four centimetres has
  an enormous derivative, and it came out as round pebbles scattered over the
  snow rather than as snow.
- **Nothing was added to the terrain's own geometry to do it**, and that is
  deliberate. Any bump big enough to see at 40 m/s is a bump big enough to
  launch you — a metre and a half over seventeen metres drops faster than
  gravity can follow — so geometric relief and a run that is not wall-to-wall
  air are the same dial. The depth is in the shading, where it costs nothing
  you have to ride.
- **Snow has a texture now**, because vertex colour had run out of room: the
  mesh has a vertex every 5.8 m across and 4.4 m down, so the finest thing it
  can paint is about ten metres wide and everything under that is invisible by
  construction. One tileable square of wind-blown snow — value noise on a
  lattice that wraps, so it joins itself on all four edges — laid on in world
  coordinates, so it stays put on the ground while the mesh scrolls underneath
  it. Nothing in that square is bigger than about two metres, which is the
  whole trick: the vertex colours already own everything from ten metres up,
  so there is nothing in the texture big enough to recognise and laying it
  down every eleven metres reads as snow rather than as a grid. The first
  version instead sampled the same square a second time at a very different
  scale to hide the join, and that second fetch measured 27 fps down to 22 on
  a renderer that is really a CPU — a quarter of the frame rate to solve a
  problem that smaller octaves solve for nothing. One fetch costs 29 down to
  27, and even that is the last rung of the quality ladder: it comes off after
  three bad windows in a row on hardware that cannot hold it.
- **Snow is also four octaves and a slope.** The finest is sized just above the
  mesh's own spacing — 5.8 m across, 4.4 m down — because that is the finest
  thing that renders as texture rather than as noise, and it carries over half
  the weight: octaves chosen to be *smoothly* resolved measured three times
  flatter, since neighbouring vertices then agree with each other. Above it sit
  sastrugi stretched across the fall line, a broad drift, and one 290 m octave
  that is the only thing surviving the far field's eighty metres a quad. On top
  of that each vertex knows which way its own patch of snow is facing — the
  slope is the difference between this vertex's height and the last one's, both
  already paid for, so it is free — measured against the *mean* fall line
  rather than against flat, because every square metre of this mountain is a
  0.4 slope and measuring from horizontal only brightens the whole face by a
  constant. Faces the sun is on go warm, faces it is off go blue.
- **Below the horizon the sky is the fog colour, to the byte.** Distant terrain
  fades to the fog colour, and if the sky there is anything else the far ridges
  read as a pale slab pasted over it. Invisible while the weather only let you
  see 338 m; glaring the moment a title screen looked two thousand metres.
- **The far field.** The playable window is 300 x 470 m, which is plenty to ride
  and nowhere near enough to look at: any wide shot swings past its edge. So the
  title screen and the drop-in get a second, much coarser mesh — 7.6 km square
  at 80 m a quad, sampled from the same height field and sunk two metres so the
  real one always wins where they overlap. It is one draw call, it reaches that
  far because that is where the range is, and it is hidden the moment you are
  riding. Beyond a couple of hundred metres the *fine* mesh stands down
  instead: from the drone it was reading as exactly what it is, a rectangle of
  detail with four straight edges pasted onto the mountain. The title screen
  also renders at
  0.58 of the pixel ratio, because it looks at the whole mountain from a hundred
  metres up — two to three times the fill of riding it — and all of it sits
  behind a scrim.
- **The forest is a forest.** Built out of the hazard budget it was four trees
  every twenty metres across a two-hundred-metre band, which is an orchard —
  105 trees in the treeline sector and 120 in the forest. The tree sectors now
  get their own pass on top, laid down through a noise field rather than
  uniformly so it arrives in stands with glades between them: a wall of evenly
  spaced trunks is not a forest either, it is a fence, and there would be no
  line through it to find. Measured per 650 m sector: 523 in the treeline and
  1,039 in the forest, which is about 470 standing at any one moment, or a
  tree every fifteen metres. Four times the trees cost 2.2 fps rather than the
  5.4 they first did, because of two things that were wrong before there were
  ever enough trees to notice: an `InstancedMesh` draws every instance it was
  *sized* for whether or not anything is in it, so the count now follows the
  highest slot ever handed out; and every cone in a pine was closed, which is
  40% of its triangles spent on discs you could only see by standing under the
  tree and looking up through it.
- **And there are fewer kickers.** One built every two hundred metres, on top
  of a crevasse lip every three hundred and a cliff fork every five, is a lot
  of jumping for a run that is supposed to be about reading a face — and the
  terrain is already throwing you off its own wind lips. Measured per sector,
  10.0 down to 7.8 on the glacier, 7.0 to 3.2 in the couloir, 5.2 to 3.0 in
  the forest. A crevasse still gets three kickers across its lip when it is
  wide, because the crossing has to be findable from wherever you happen to
  be; a narrow one is findable anyway and gets the middle one only.
- **The valley floor is the integral of an easing gradient**, not a spliced-on
  plane: the fall line's gradient ramps from 0.40 to 0.055 over the last 340 m
  and the height is what you get by integrating that, so the floor meets the
  face with no crease in it. The face's own texture fades out across the same
  340 m — the sharp octaves almost completely, the broad roll to a seventh —
  or the floor is a flat gradient with twenty metres of mountain still on it.
  Crevasses and cliff bands switch off down there, and so does every hazard the
  generator would otherwise stock. Endless has no bottom, so `floorZ` goes out
  of reach and every one of these terms switches itself off.
- **The descent ends in the valley and stays there.** Six sectors, 650 m each,
  is exactly the 3,900 m descent — and the palette used to wrap straight back
  round to the glacier's at the bottom, so the run-out, the line and everything
  standing at the bottom of the mountain were painted in the colours of the ice
  four kilometres above them. The valley floor came out a slab of dark blue.
  The last face is held now; endless still cycles.
- **The terrain writes its own normals.** They used to come from screen-space
  derivatives, which is free and exactly right until a 2×2 fragment quad spans
  more than one triangle — and on a valley floor seen from a camera six metres
  above it, 400 m of ground compress into a few dozen rows of pixels and every
  quad does. The sun stopped reaching the floor at all. The grid knows its own
  slope: it is the difference between neighbouring heights, which the rebuild
  has already paid for, so it writes `(-df/dx, 1, -df/dz)` per vertex and the
  material stopped deriving anything. Measured against flat shading on the same
  frame, the face renders the same; the floor no longer renders as water.
- **Shading is read against the *local* mean fall line**, which stopped being
  0.40 the moment the run-out existed. Flat ground measured against a 0.4
  reference reads as a face turned hard away from the sun.
- **Crossing the line ends the descent, not the altimeter reading 1,000.** The
  finish stands at a fixed z; with the run-out in, the height you are at when
  you reach it is no longer a fixed multiple of how far you have come, and the
  old altitude test simply never fired — the run never ended. The altimeter's
  own scale is recomputed from the height the descent actually loses.
- **What throws a rider is not size, it is the rate the ground changes slope**,
  which goes as amplitude over wavelength squared — and against the game's own
  gravity of 26 m/s², not 9.81. Two things followed from measuring that. The
  face's *fine grain* was doing half the jumping: a 34 m bump of 5.6 m changes
  slope faster than a wind lip six times its length, so most of what threw you
  was texture rather than anything shaped like a takeoff, and it is 2.0 m
  across 29 m now. And the wind lip went the other way — twice as tall, twice
  the wavelength, and it launches less than half as often. The broad rollers
  went from 17 m across 95 to 16 across 118, which puts them at a third of
  gravity: you ride over one instead of off it.
- **Being airborne now takes 62 cm of daylight**, not 35. Thirty-five
  centimetres on a face with a bump every thirty metres is not flying, it is a
  board skipping, and it was being counted and scored as air.
- **A smoother face is a faster one**, which nobody asks for: with the fine
  grain gone there is less sideways scrub, and the same pilot ran at **152 km/h
  mean and 247 peak** against the old 87 and 132. Drag went from 0.0112 to
  0.0212 to put it back — 93 and 149 measured, which is where the avalanche
  chase was balanced.
- **Empty prop pools do not draw.** A pool hides its instanced mesh when nothing
  is in it, which is most of them most of the time — no seracs in the forest, no
  trees on the glacier.
- **Resolution** adapts: if the frame rate sits under 42 fps the renderer drops
  its pixel ratio rather than dropping frames. The terrain rebuild skips vertex
  normals (the material is flat-shaded, so they come from screen-space
  derivatives) and bounding spheres (the mesh is never culled), which is most of
  the reason a 43% wider window costs less CPU than the old narrow one.
