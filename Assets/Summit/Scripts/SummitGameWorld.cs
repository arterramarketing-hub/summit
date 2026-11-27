using UnityEngine;

namespace Summit
{
    // SummitGame, part: the world, built at start-up (the test tools call Awake themselves, outside play mode)
    public partial class SummitGame
    {
        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 120f;   // physics in small steps; the rider is interpolated between them
            // after a long frame, catch up at most 50 ms of physics (6 steps), not a third of a second's worth, which only
            // made the next frame long too (set here: the batch tools leave TimeManager.asset changed)
            Time.maximumDeltaTime = 0.05f;
            Physics.simulationMode = SimulationMode.FixedUpdate;   // never left on manual by a test tool
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            Slope.Init(Slope.TodaysSeed());
            best = PlayerPrefs.GetInt("summit_best", 0);
            SteerPrefs.Apply();   // (the TURN setting)
            MakeMissingMaterials();
            SetUpWorld();

            input = gameObject.AddComponent<RiderInput>();

            var rg = new GameObject("Rider");
            rider = rg.AddComponent<Rider>();
            rider.input = input;
            _ = rider.Body;   // its physics body exists before the ragdoll is built (as on device; edit-mode tools skip Awake)
            body = rg.AddComponent<RiderBody>();
            body.Build(rider, jacket, pants, board, skin, dark, trail, spray);
            rider.Link += OnLink;
            rider.Crash += OnCrash;
            rider.RoughLanding += OnRough;
            rider.Jumped += () => { if (state == State.Riding) { audioFx.Play(Sfx.Pop); popped = true; } };
            rider.Landed += impact =>
            {
                if (state != State.Riding || impact <= 3f) return;
                if (impact > 11f) audioFx.Play(Sfx.LandHard);
                if (impact > 13f && impact <= 18f && !rider.Crashed) audioFx.Play(Sfx.BuddyClose);   // a big landing ridden away (harder is the ouch)
                hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((impact - 7f) / 12f) * 0.7f);
                audioFx.Play(Sfx.Land, Mathf.Clamp01(impact / 14f));
            };
            rider.Scraped += hit => { if (state == State.Riding) audioFx.Play(Sfx.Scrape, hit); };
            body.BoneBroke += (limb, at) =>
            {
                if (state != State.Riding) return;   // the bill is closed once the run is over
                // a harsh crack for each kind of break: the neck, the spine, a big bone in the leg, an arm
                audioFx.Play(limb == "Head" ? Sfx.BoneNeck : limb == "Ribs" ? Sfx.BoneBack : limb.EndsWith("leg") ? Sfx.BoneLeg : Sfx.BoneArm);
                hud.Hurt(0.5f);
                hurtShake = 1f;   // (the bill hears the hit itself, with the break: body.Injured)
            };
            body.LimbLost += (limb, at, whole) =>
            {
                // a limb torn off: it goes on the bill, and you get up and ride on without it (RiderBodyLimbs).
                // (the crash it came off in has already counted as a fall)
                if (state != State.Riding) return;
                receipt.Lost(LostPart(limb), whole);
                hud.Hurt(1f); hurtShake = 1f;
                audioFx.Play(Sfx.Bone); audioFx.Play(Sfx.Crash);
                if (audioFx.Screaming) audioFx.ScreamHit();
            };
            // a tree hit and still standing: it shudders and its snow comes down off it (SnowFX, TreeWind)
            rider.HitObstacle += (ob, at, into) => { if (ob.label == "Tree") ShakeTree(ob.GetComponent<Collider>(), into, rider.Body.linearVelocity); };
            // arcadetrees: a tree clipped and ridden on, staggering: a branch's snap and the thud of the trunk, the snow off it
            // (ShakeTree, above), the camera jolted and the rider flinching away from it
            rider.TreeGlanced += (at, into, square) =>
            {
                if (state != State.Riding) return;
                float sp = rider.Speed;
                audioFx.PlayTree(Sfx.LimbSnap, 0.35f + 0.4f * square, sp, 0.55f + 0.35f * square);
                audioFx.PlayTree(Sfx.TrunkCrack, 0.2f + 0.3f * square, sp, 0.3f + 0.4f * square);
                audioFx.PlayTree(Sfx.SnowDump, 0.5f, sp, 0.5f + 0.3f * square);
                audioFx.Play(Sfx.Branches, 0.5f + 0.3f * square);
                hurtShake = Mathf.Max(hurtShake, 0.3f + 0.4f * square);
            };
            body.RagdollStruck += (c, at, into) =>
            {
                var ob = c != null ? c.GetComponentInParent<Obstacle>() : null;
                if (ob != null && ob.label == "Tree" && into > 4f) ShakeTree(c, into, c.transform.position - at);
            };
            body.RagdollHit += (v, isBoard) =>
            {
                if (!isBoard && v > 6f) hurtShake = Mathf.Max(hurtShake, Mathf.Clamp01((v - 6f) / 10f) * 0.8f);
                if (v < 1.5f || Time.time < nextImpactSound) return;
                // mid-tumble: a fresh scream on each big hit, above all against trees, rocks and signs
                if (audioFx.Screaming && rider.Crashed && !isBoard && (v > 14f || (v > 7f && body.RagdollHitObject)))
                    audioFx.ScreamHit();
                // a hard hit into a tree, rock or sign mid-tumble gets its own beat (never the rolling on the snow)
                if (rider.Crashed && !isBoard && state == State.Riding && v > 8f && body.RagdollHitObject)
                    HitStop(0.25f + (v - 8f) / 30f);
                nextImpactSound = Time.time + 0.07f;
                audioFx.Play(isBoard ? Sfx.Clack : Sfx.Thump, Mathf.Clamp01(v / 10f));
            };

            var tg = new GameObject("Terrain");
            terrain = tg.AddComponent<SlopeStreamer>();
            terrain.snowMaterial = snow;
            terrain.target = rg.transform;
            SnowLayer.Create(snow);   // the snow the thrown body ploughs into (see RiderBody.PhysicsStep)

            var pg = new GameObject("Trees");
            props = pg.AddComponent<Forest>();
            props.foliage = foliage;
            props.bark = bark;
            props.target = rg.transform;
            Farview.Create(rg.transform, snow, foliage);   // the world's far band and the far field beyond it (Farview.cs)

            power = new GameObject("PowerUps").AddComponent<PowerUps>();
            power.rider = rider;
            power.Build(snow.shader, trail, spray);
            power.Collected += OnPickup;
            rider.ShieldHit += () =>
            {
                if (state != State.Riding) return;
                hud.Flash("SHIELD SAVED YOU", rider.Shields + " left", 1.1f);
                audioFx.Play(Sfx.ShieldHit);
            };
            BuildStartGate();
            dressing = new GameObject("Course").AddComponent<CourseDressing>();
            dressing.target = rg.transform;
            dressing.Build(snow.shader, snow);
            Hamlets.Create(rg.transform, snow.shader, spray);   // hamlets beside the piste (Hamlets.cs): they stream and rebuild themselves
            wild = new GameObject("Wildlife").AddComponent<Wildlife>();
            wild.rider = rider;
            wild.Build(snow.shader);
            wild.BearSpotted += () => { };   // (the bear is there to see and hear: no caption)
            SkiLift.Create(rg.transform, snow.shader);   // ski lifts beside the run (SkiLift.cs): they stream, and follow a new mountain, by themselves
            MountainRiders.Create(null, rider, snow.shader);   // other people riding the run (MountainRiders.cs): the same, by themselves (not under the rider: their solids would ride along with him)
            // (ridercrash) into another rider: they go down; the first time a run you stay up, and know it without a word
            rider.PersonWarned += (ob, at) => FreeHit();
            if (LiftEnds.Instance != null) LiftEnds.Instance.Grunted = (at, female) => { if (state == State.Riding) audioFx.Play(female ? Sfx.Yelp : Sfx.Grunt); };
            // (rides2) the car and the bear run people down: the thud of it, the spray, and on you go
            rider.RanOver += (at, into, person) =>
            {
                if (state != State.Riding) return;
                audioFx.Play(Sfx.Thump, Mathf.Clamp01(into / 8f));
                if (person) { if (blood != null && blood.On && into > Blood.ObstacleHard) audioFx.Play(Sfx.Bone, 0.6f); PeopleBlood.Hit(at + Vector3.up * 0.9f, rider.Velocity, into); }
                hurtShake = Mathf.Max(hurtShake, 0.25f);
            };
            // (rides2) the car dented by a tree (or bumped off anything built): the crunch, a jolt, and how many it has left
            rider.CarDented += (trees, at) =>
            {
                if (state != State.Riding) return;
                audioFx.Play(Sfx.SmashWood, 0.8f); audioFx.Play(Sfx.Thump, 1f);
                hurtShake = Mathf.Max(hurtShake, 0.5f);
                if (trees > 0)
                {
                    int left = Rider.CarTreesToWreck - trees;
                    hud.Flash("DENTED", left == 1 ? "one more tree and you're out" : left + " more trees and you're out", 1.1f);
                }
            };
            if (MountainRiders.Current != null) MountainRiders.Current.Grunted = (at, female) => { if (state == State.Riding) audioFx.Play(female ? Sfx.Yelp : Sfx.Grunt); };
            rider.GrindStart += f => { if (state == State.Riding) audioFx.Play(f.kind == Course.Kind.Rail ? Sfx.GrindRail : Sfx.GrindBox); };
            // a numbered flag wherever a fall came to rest, for the rest of the run
            flags = new GameObject("FallFlags").AddComponent<FallFlags>();
            flags.Build(jacket != null ? jacket.shader : null);
            body.Injured += inj => { if (state == State.Riding) receipt.Hit(inj); };   // each hit on the receipt (Receipt)
            receipt.Printed += OnBilled;
            body.FallRested += at => { if (state == State.Riding) flags.Plant(at, falls); };
            body.Killed += fell =>
            {
                // off a cliff and into the snow far below: it's over (see RiderBodyFall)
                if (state == State.Hub) { HitStop(1f); audioFx.Play(Sfx.Crash); return; }   // off the camp's edge: just the thump
                if (state != State.Riding) return;
                Vector3 c = body.RagdollCentre;
                rider.StayDown = true; wipeoutTimer = 2.6f; endTitle = "WIPED OUT";
                receipt.Killed(fell, RockFace.IsRock(Slope.Normal(c.x, c.z), c.x, c.z));
                HitStop(1f);   // the body hitting the snow at the foot of the cliff
                audioFx.Play(Sfx.BoneBack); audioFx.Play(Sfx.Crash);
            };

            var cam = Camera.main;
            if (cam == null)
            {
                var cg = new GameObject("Main Camera");
                cg.tag = "MainCamera";
                cam = cg.AddComponent<Camera>();
                cg.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 2300f;
            chase = cam.gameObject.GetComponent<ChaseCamera>();
            if (chase == null) chase = cam.gameObject.AddComponent<ChaseCamera>();
            chase.rider = rider;
            chase.floor = slide.Depth;   // the camera rides up over the avalanche's debris
            camLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_camdist", 2), 0, CamDist.Length - 1);
            chase.distScale = CamDist[camLevel];
            angleLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_angle", 1), 0, AngleHeight.Length - 1);
            lensLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_lens", 1), 0, LensFov.Length - 1);
            PlayerPrefs.DeleteKey("summit_lenstype");   // (the lens is always spherical now: an old ANAMORPHIC choice is forgotten)
            filterLevel = Mathf.Clamp(PlayerPrefs.GetInt("summit_filter", 0), 0, LookGrade.Names.Length - 1);
            filterStrength = Mathf.Clamp(PlayerPrefs.GetInt("summit_filterstrength", 10), 0, 10);
            halation = Mathf.Clamp(PlayerPrefs.GetInt("summit_halation", 0), 0, LensFX.HalationName.Length - 1);

            fx = new GameObject("SnowFX").AddComponent<SnowFX>();
            fx.Build(cam, spray);
            body.fx = fx;
            // blood on the hardest crashes (can be switched off in the pause menu: it raises the age rating)
            blood = new GameObject("Blood").AddComponent<Blood>();
            blood.Build(spray);
            blood.SetOn(PlayerPrefs.GetInt("summit_blood", 1) == 1);
            body.blood = blood;
            blood.forest = props;
            PeopleBlood.blood = blood;   // (the people knocked over bleed too: rides2)

            // invincible: what you run through is smashed, not crashed into
            smasher = new GameObject("Smasher").AddComponent<Smasher>();
            smasher.Build(fx, props, wild, spray);
            aura = rg.AddComponent<PowerAura>();
            aura.Build(rider, spray);
            rider.Smashed += (ob, at, v) =>
            {
                if (state != State.Riding) return;
                smasher.Hit(ob, at, v);
                if (ob.label == "Tree")
                {
                    // (treesfx: a tree smashed through is a trunk breaking, the recorded layers, not the crate's break)
                    float sp = v.magnitude;
                    audioFx.PlayTree(Sfx.TrunkCrack, 0.9f, sp);
                    audioFx.PlayTree(Sfx.LimbSnap, 0.8f, sp, 0.6f);
                    audioFx.PlayTree(Sfx.SnowDump, 0.9f, sp, 0.8f);
                    audioFx.Play(Sfx.Branches, 0.7f);
                }
                else audioFx.Play(ob.label == "Rock" ? Sfx.SmashRock : Sfx.SmashWood);
                if (ob.always) audioFx.Play(Sfx.BearRoar);
                hurtShake = Mathf.Max(hurtShake, 0.35f);
                Link(ob.always ? "BEAR BOWLED" : ob.label == "Rock" ? "ROCK SMASH" : "TIMBER!", ob.always ? 500 : 100, LinkKind.Smash);
            };
            // young trees and dead snags hit fast enough snap: the top falls, you go down but carry on (TreeBreak)
            treeBreak = new GameObject("TreeBreak").AddComponent<TreeBreak>();
            treeBreak.Build(props, fx, smasher);
            // the sound of it in layers (SoundMix.PlayTree, by the size of what broke and how fast it was hit): a twig's
            // sharp crack, a limb's splintering snap, a trunk's heavy crack and creak, the thump as it lands, the snow
            // falling off the branches
            treeBreak.Snapped += (at, into, byRider) =>
            {
                if (state != State.Riding) return;
                audioFx.PlayTree(Sfx.TrunkCrack, byRider ? 0.85f : 0.7f, into);
                audioFx.PlayTree(Sfx.LimbSnap, 0.9f, into, 0.5f);
                audioFx.PlayTree(Sfx.SnowDump, 0.9f, into, 0.9f);
                audioFx.Play(Sfx.Branches, 0.6f);
                hurtShake = Mathf.Max(hurtShake, byRider ? 0.8f : 0.5f);
            };
            // a thin trunk ridden through (a small tree, a treetop) and branches brushed: you ride on. Heard, felt in the
            // camera, the hero flinching from them (the rest - the snow, the needles, the pieces - is TreeBreak's)
            treeBreak.Broke += (at, into, top) =>
            {
                if (state != State.Riding) return;
                audioFx.PlayTree(Sfx.LimbSnap, top ? 0.75f : 0.55f, into);
                audioFx.PlayTree(Sfx.TrunkCrack, top ? 0.35f : 0.15f, into, top ? 0.55f : 0.4f);
                audioFx.PlayTree(Sfx.SnowDump, top ? 0.8f : 0.6f, into, top ? 0.9f : 0.7f);
                audioFx.Play(Sfx.Branches, 0.8f);
                hurtShake = Mathf.Max(hurtShake, top ? 0.4f : 0.5f);
            };
            treeBreak.Brushed += (at, side, depth) =>
            {
                if (state != State.Riding) return;
                float sp = rider.Speed;
                audioFx.Play(Sfx.Branches, depth * 0.8f);
                // the tips: twigs; deeper in, limbs torn out too; and the snow knocked off them
                audioFx.PlayTree(Sfx.TwigCrack, 0.3f * depth, sp, 0.5f + 0.5f * depth);
                if (depth > 0.45f) audioFx.PlayTree(Sfx.LimbSnap, 0.2f + 0.4f * depth, sp, depth);
                if (depth > 0.2f || Random.value < 0.5f) audioFx.PlayTree(Sfx.SnowDump, 0.3f * depth, sp, 0.35f + 0.55f * depth);
                hurtShake = Mathf.Max(hurtShake, 0.2f + 0.3f * depth);
            };
            treeBreak.Landed += (at, size, force) =>
            {
                if (state == State.Riding) audioFx.TreeLanded(at, size, force);
            };
            // signs and flags: ridden into (or tumbled into), signs break and flags go down (Breakables)
            breaks = new GameObject("Breakables").AddComponent<Breakables>();
            breaks.Build(rider, fx, smasher);
            breaks.Broke += (b, crashed, invincible, thrown) =>
            {
                if (state != State.Riding) return;
                bool sign = b.kind == Breakable.Kind.Sign;
                if (sign) audioFx.Play(Sfx.SmashWood, crashed || invincible ? 1f : 0.8f);
                else if (b.kind == Breakable.Kind.Fence) { audioFx.Play(Sfx.SmashWood, 0.55f); if (!thrown) hurtShake = Mathf.Max(hurtShake, 0.2f); }
                else if (b.kind == Breakable.Kind.Banner) audioFx.Play(Sfx.ClothRip, thrown ? 0.5f : Mathf.Clamp01(rider.Speed / 25f));
                else audioFx.Play(Sfx.Clack, 0.7f);
                if (!sign || thrown) return;
                hurtShake = Mathf.Max(hurtShake, crashed ? 0.8f : 0.45f);
                if (invincible) Link("SIGN SMASH", 100, LinkKind.Smash);
            };

            // a sign (or a banner's stake) ridden into: you stay up, and it hurts. The camera shakes, the hero flinches
            // from it, and it goes on the receipt while you ride: one line that pops up and folds into the corner's total
            breaks.RiderHit += (kind, speed, at, part, cost) =>
            {
                if (state != State.Riding) return;
                float k = Mathf.Clamp01(speed / 30f);
                hurtShake = Mathf.Max(hurtShake, kind == Breakable.Kind.Sign ? 0.4f + 0.45f * k : 0.25f);
                if (kind == Breakable.Kind.Sign && speed > 12f) audioFx.Play(Sfx.Grunt);
                receipt.RideHit(kind == Breakable.Kind.Sign, part, speed, cost);
            };

            atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.Build(cam, rider, spray);

            slideView = new GameObject("Avalanche").AddComponent<AvalancheView>();
            slideView.Build(cam, rider, body, slide, snow, spray);

            var hg = new GameObject("HUD");
            hud = hg.AddComponent<Hud>();
            hud.Build();

            var ag = new GameObject("Audio");
            audioFx = ag.AddComponent<SoundMix>();
            audioFx.Build();
            audioFx.Track(wild);
            HookPoints();

            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            post = vol != null ? vol.profile : null;
            lensFx = cam.gameObject.AddComponent<LensFX>();
            lensFx.Build(cam);
            pov = PlayerPrefs.GetInt(ChaseCamera.PovPref, 0) == 1;
            chase.pov = pov; chase.body = body; chase.lensFx = lensFx;
            Tuning.Load();
            FillSettings(hud.PauseSettings);
            hud.PauseSettings.Clicked = () => audioFx.Play(Sfx.Click);
            ApplyCamera();
            ApplyLook();
            bool tube = PlayerPrefs.GetInt("summit_tube", 0) == 1;
            rider.Tube = tube; body.SetTube(tube); ShowRider();
            rider.Bounced += into => { if (state != State.Riding) return; audioFx.Play(Sfx.Thump, Mathf.Clamp01(into / 12f)); };
            // (tube) bounced off something: the ring gives, spins, tips and hops (RiderBodyTube), with its rubbery bounce
            rider.TubeHit += (n, into) =>
            {
                if (state != State.Riding) return;
                body.TubeBounce(n, into);
                audioFx.Play(Sfx.TubeBounce, Mathf.Clamp01((into - 1f) / 13f));
            };
            // the fun rides' (rides): a paddle stroke's swish; the ridden bear roars as it leaps and grunts as it lands
            rider.Stroke += side => { if (state == State.Riding) audioFx.Stroke(side); };
            rider.Jumped += () => { if (state == State.Riding && body.OnVehicleNow && body.Ride == RideKind.Bear) audioFx.BearRoar(); };
            rider.Landed += impact => { if (state == State.Riding && body.OnVehicleNow && body.Ride == RideKind.Bear && impact > 4f) audioFx.BearGrunt(Mathf.Clamp01(impact / 16f)); };
            rider.SpunOut += sp => { if (state == State.Riding) audioFx.Play(Sfx.Scrape, Mathf.Clamp(sp * 0.3f, 2f, 8f)); };
            BuildMeta();
            dayNight.Hook(rider, fx, camp, spray, trail);
            weather = Weather.Build(rider, camp, cam, atmo, fx, snow, audioFx, () => start);   // the weather on top of the time of day (Weather.cs)
            NightLights.Build(rider, cam, dayNight, atmo, spray);   // floodlights down the mountain at dusk and night (NightLights.cs)
            PisteKit.Build(rider);   // piste poles, rope lines, snow guns, the patrol hut, trail signs (PisteKit.cs, TrailSigns.cs)
            ToHub();
            // the game opens on the start page (StartPage.cs), over the camp it has just set up. Not for the
            // device-test autoplay, which carries straight on into a run, nor for the editor's test tools, which
            // call Awake themselves outside play mode and expect to find themselves in the camp
            if (!autoplay && Application.isPlaying) ToTitle();
            // device testing: launched with SUMMIT_TREEBENCH=1 (and SUMMIT_AUTOPLAY=1) it times both tree looks
            if (System.Environment.GetEnvironmentVariable("SUMMIT_TREEBENCH") == "1") gameObject.AddComponent<TreeBench>().forest = props;
        }
    }
}
