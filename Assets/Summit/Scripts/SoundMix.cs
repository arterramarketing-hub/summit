using UnityEngine;

namespace Summit
{
    public enum Sfx
    {
        Pop, Takeoff, Land, LandHard, Crash, HitTree, HitRock, Crevasse, Thump, Clack, Spin,
        Trick, TrickBig, CloseCall, Soda, ShieldGet, ShieldHit, ShieldFull, Click, Play, Buried,
        Scrape, GrindRail, GrindBox, BearRoar, Bone, Scream, Grunt, Yelp, Groan,
        SmashWood, SmashRock, PowerJump, PowerStar, SlideHit,
        Nooo, Ledge,  // the player's own clips (Resources/Audio/user_*.mp3)
        BigScream,    // one of the player's screams (Resources/Audio/UserScreams), shuffled
        SlideCrack,   // the slope letting go behind you: a crack, and the rumble building
        BoneLeg, BoneBack, BoneNeck, BoneArm,  // the harsh breaks: a big bone, the spine, the neck, an arm
        BuddyWoah, BuddyClose, BuddyCheer, BuddyOuch,  // the player's own voice lines (Resources/Audio/Buddy/<group>)
        BuddyWarn, BuddyDeep,                           // "lookout" (something in the way), "don't go down there" (a deadly drop or crevasse)
        PointTick, Bank, ComboLost, BillTick,           // the points running up (a note that climbs), a combo banked (by its size, 0..4), one lost; the bill running up
        Branches,     // riding through a tree's branches: needles swishing past and twigs cracking (made here: BranchClips)
        ClothRip      // a SLOW banner torn off its stakes: the fabric ripping and a flap (made here: ClothClips)
    }

    /// <summary>What the ride sounds like this frame (all 0..1 unless noted).</summary>
    public struct RideSound
    {
        public float speed, carve, skid, rough, avalanche, muffle, pan;
        public float ice, chatter, slush;   // the snow underfoot (Surfaces): how much of it is ice, how much it chatters, how much is slush
        public bool onSnow, air, boost, riding;
        public bool camp, ended;   // walking round the summit camp; on an end screen (for the music)
        public int grind;   // 0 none, 1 rail, 2 box
    }

    /// <summary>
    /// The whole soundscape, from clips made by Tools/MakeAudio.swift (Resources/Audio):
    /// a bed of mountain air; a quiet bed of moving air (wind_low) always there and barely rising with speed, and a
    /// harsh rush of wind (wind_high) only in extreme weather (Weather.Howl: a storm, a windy day, a front arriving),
    /// coming and going with the gusts; the board on the snow as separate layers that the ride crossfades between: a
    /// glide hiss, the edge biting in a carve, the chatter of a skid, the soft crush of powder; metal
    /// and plastic grinds; the avalanche rumbling up behind; the fizz of a boost. One-shots for
    /// everything that happens, some of them placed in the world (the bear, birds, crows), and a
    /// low-pass over the lot that muffles the world when you are buried.
    /// </summary>
    public class SoundMix : MonoBehaviour
    {
        class Layer { public AudioSource src; public float vol, pitch = 1f; }

        Layer ambience, windLow, windHigh, glide, carve, skid, powder, grindRail, grindBox, avalanche, boost;
        AudioSource[] shots, shots3d;
        AudioSource ui;
        int nextShot, next3d;
        AudioLowPassFilter muffler;
        float muffle;

        AudioClip powerShort, pop, whoosh, spin, landHard, crash, hitTree, hitRock, crevasse, clack, clank, chime, trickBig,
                  closeCall, soda, shieldGet, shieldHit, click, play, buried, bearRoar, flap;
        AudioClip[] lands, thumps, crows, chirps, bones, screams, grunts, yelps, bonesLeg, bonesBack, bonesNeck;
        AudioClip groan;
        AudioClip smashWood, smashRock, powerJump, powerStar;
        // the avalanche up close: the hiss and crackle of the churning front, and the blow that takes you off
        Layer slideNear;
        AudioClip slideHit, slideCrack;
        AudioSource voice;   // one voice: a new cry cuts the last one off, like a real throat
        // the player's own clips: a long "nooo" down a crevasse, and the suspense sting at the first ledge, on its
        // own source so it can be faded out once the jump is over
        AudioClip nooo, ledge;
        AudioClip[] bigScreams; int[] screamBag; int screamNext; float bigStart = -9f;
        AudioSource cue;
        float voiceLock;          // until then the voice is a long cry that nothing short may cut off
        AudioClip[] groans;
        Music music;   // the soundtrack (Music.cs), on its own object so it has its own muffling

        // the bear, followed through the world
        Transform bearSound;
        AudioSource bearGrowl, bearSteps;
        Wildlife wild;
        float chirpClock = 3f;

        static AudioClip C(string n) => Resources.Load<AudioClip>("Audio/" + n);

        AudioClip[] branches;

        /// <summary>
        /// Riding through branches, made rather than recorded (no clip in Resources/Audio has it): a swish of needles
        /// (noise, shaped like a brush past: a fast rise and a half-second fall, band-limited to the hiss of
        /// needles), with twigs cracking in it (sharp clicks, each a few milliseconds of bright noise with a short
        /// woody ring). Three takes, different cracks.
        /// </summary>
        static AudioClip[] BranchClips()
        {
            const int rate = 44100; int n = (int)(rate * 0.55f);
            var takes = new AudioClip[3];
            var rs = Random.state;
            for (int k = 0; k < takes.Length; k++)
            {
                Random.InitState(5021 + k * 97);
                var d = new float[n];
                float lp1 = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / 0.13f);
                    float w = Random.value * 2f - 1f;
                    lp1 += (w - lp1) * 0.55f; lp2 += (lp1 - lp2) * 0.08f;   // keep 1-6 kHz or so: needles, not wind
                    d[i] = (lp1 - lp2) * env * 0.9f;
                }
                int cracks = 4 + k;
                for (int c = 0; c < cracks; c++)
                {
                    int at = (int)(rate * Random.Range(0.005f, 0.3f));
                    float ring = Random.Range(1700f, 3400f), amp = Random.Range(0.45f, 1f) * (c == 0 ? 1f : 0.7f);
                    int len = (int)(rate * 0.03f);
                    for (int i = 0; i < len && at + i < n; i++)
                    {
                        float t = i / (float)rate;
                        float click = (Random.value * 2f - 1f) * Mathf.Exp(-t / 0.0025f);
                        float body = Mathf.Sin(t * ring * 2f * Mathf.PI) * Mathf.Exp(-t / 0.009f) * 0.6f;
                        d[at + i] += (click + body) * amp;
                    }
                }
                float peak = 0.01f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
                for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
                takes[k] = AudioClip.Create("Branches" + k, n, 1, rate, false);
                takes[k].SetData(d, 0);
            }
            Random.state = rs;
            return takes;
        }

        /// <summary>
        /// A banner ripping (three takes): a grainy tear (noise in the fabric's band, gated on and off every few
        /// milliseconds like threads giving one after another) and the slap of the cloth as it comes free.
        /// </summary>
        static AudioClip[] ClothClips()
        {
            const int rate = 44100; int n = (int)(rate * 0.5f);
            var takes = new AudioClip[3];
            var rs = Random.state;
            for (int k = 0; k < takes.Length; k++)
            {
                Random.InitState(7717 + k * 131);
                var d = new float[n];
                float lp1 = 0f, lp2 = 0f, gate = 1f; int nextGate = 0;
                float len = 0.22f + 0.05f * k;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    if (i >= nextGate) { gate = Random.Range(0.15f, 1f); nextGate = i + (int)(rate * Random.Range(0.002f, 0.006f)); }
                    float env = Mathf.Min(1f, t / 0.008f) * (t < len ? 1f - 0.4f * t / len : 0.6f * Mathf.Exp(-(t - len) / 0.03f));
                    float w = Random.value * 2f - 1f;
                    lp1 += (w - lp1) * 0.4f; lp2 += (lp1 - lp2) * 0.04f;   // about 600 Hz to 5 kHz: cloth, not wind
                    d[i] = (lp1 - lp2) * env * gate;
                    // the flap, as it comes off the stake
                    float tf = t - len;
                    if (tf > 0f) d[i] += Mathf.Sin(tf * 150f * 2f * Mathf.PI) * Mathf.Exp(-tf / 0.035f) * 0.5f;
                }
                float peak = 0.01f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
                for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
                takes[k] = AudioClip.Create("ClothRip" + k, n, 1, rate, false);
                takes[k].SetData(d, 0);
            }
            Random.state = rs;
            return takes;
        }
        AudioClip[] clothRips;

        public void Build()
        {
            ambience = Loop("ambience"); windLow = Loop("wind_low"); windHigh = Loop("wind_high");
            glide = Loop("glide"); carve = Loop("carve"); skid = Loop("skid"); powder = Loop("powder");
            grindRail = Loop("grind_rail"); grindBox = Loop("grind_box"); avalanche = Loop("avalanche"); boost = Loop("boost");
            slideNear = Loop("avalanche_near"); slideHit = C("slide_hit"); slideCrack = C("slide_crack");

            shots = new AudioSource[10];
            for (int i = 0; i < shots.Length; i++) { shots[i] = gameObject.AddComponent<AudioSource>(); shots[i].playOnAwake = false; shots[i].spatialBlend = 0f; }
            ui = gameObject.AddComponent<AudioSource>(); ui.playOnAwake = false; ui.ignoreListenerPause = true;   // menu clicks still sound while paused
            muffler = gameObject.AddComponent<AudioLowPassFilter>();
            muffler.cutoffFrequency = 22000f;

            // sounds out in the world: their own objects, positioned when played
            shots3d = new AudioSource[6];
            for (int i = 0; i < shots3d.Length; i++) shots3d[i] = Source3D("World Sound " + i, null, 6f, 160f);

            pop = C("pop"); whoosh = C("whoosh"); spin = C("spin"); landHard = C("land_hard"); crash = C("crash");
            hitTree = C("hit_tree"); hitRock = C("hit_rock"); crevasse = C("crevasse"); clack = C("clack"); clank = C("clank");
            chime = C("trick"); powerShort = C("power_short"); trickBig = C("trick_big"); closeCall = C("close_call"); soda = C("soda");
            shieldGet = C("shield_get"); shieldHit = C("shield_hit"); click = C("click"); play = C("play"); buried = C("buried");
            bearRoar = C("bear_roar"); flap = C("flap");
            smashWood = C("smash_wood"); smashRock = C("smash_rock"); powerJump = C("power_jump"); powerStar = C("power_star");
            lands = new[] { C("land_1"), C("land_2"), C("land_3") };
            thumps = new[] { C("thump_1"), C("thump_2"), C("thump_3") };
            crows = new[] { C("crow_1"), C("crow_2") };
            chirps = new[] { C("chirp_1"), C("chirp_2"), C("chirp_3") };
            bones = new[] { C("bone_1"), C("bone_2"), C("bone_3") };
            bonesLeg = new[] { C("bone_leg_1"), C("bone_leg_2") };
            bonesBack = new[] { C("bone_back_1"), C("bone_back_2") };
            bonesNeck = new[] { C("bone_neck_1"), C("bone_neck_2") };
            screams = new[] { C("scream_1"), C("scream_2"), C("scream_3") };
            grunts = new[] { C("grunt_1"), C("grunt_2"), C("grunt_3"), C("grunt_4") };
            yelps = new[] { C("yelp_1"), C("yelp_2") };
            groan = C("groan");
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0f;
            nooo = C("user_crevasse_nooo"); ledge = C("user_ledge_suspense");
            bigScreams = Resources.LoadAll<AudioClip>("Audio/UserScreams");
            buddy = new[] { new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Woah")), new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Close")),
                            new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Cheer")), new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Ouch")),
                            new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Warn")), new Bag(Resources.LoadAll<AudioClip>("Audio/Buddy/Deep")) };
            screamBag = new int[bigScreams.Length]; screamNext = screamBag.Length;   // shuffled on first use
            cue = gameObject.AddComponent<AudioSource>(); cue.playOnAwake = false; cue.spatialBlend = 0f;
            music = new GameObject("Music").AddComponent<Music>();
            music.transform.SetParent(transform, false);
            music.Build();
        }

        Layer Loop(string name)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = C(name);
            s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.playOnAwake = false;
            if (s.clip != null) { s.time = s.clip.length * Random.value * 0.9f; s.Play(); }   // start each somewhere different
            return new Layer { src = s };
        }

        AudioSource Source3D(string name, AudioClip loopClip, float near, float far)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var s = g.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near; s.maxDistance = far;
            s.dopplerLevel = 0.4f;
            if (loopClip != null) { s.clip = loopClip; s.loop = true; s.volume = 0f; s.Play(); }
            return s;
        }

        /// <summary>Follow the bear and the birds of this world.</summary>
        public void Track(Wildlife w)
        {
            wild = w;
            bearSound = new GameObject("Bear Sound").transform;
            bearSound.SetParent(transform, false);
            bearGrowl = Source3D("Growl", C("bear_growl"), 8f, 140f);
            bearSteps = Source3D("Gallop", C("bear_steps"), 6f, 90f);
            bearGrowl.transform.SetParent(bearSound, false);
            bearSteps.transform.SetParent(bearSound, false);
            w.BearSpotted += () => PlayAt(bearRoar, w.BearPosition + Vector3.up, 1f, Random.Range(0.92f, 1.05f), 25f);
            w.CrowsTookOff += p => { PlayAt(crows[Random.Range(0, crows.Length)], p, 0.8f, Random.Range(0.9f, 1.1f), 10f); PlayAt(flap, p, 0.6f, Random.Range(0.9f, 1.1f), 6f); };
        }

        static void Mix(Layer l, float want, float pitch, float up, float down, float dt)
        {
            if (l.src == null || l.src.clip == null) return;
            l.vol = Mathf.Lerp(l.vol, want, 1f - Mathf.Exp(-dt * (want > l.vol ? up : down)));
            l.pitch = Mathf.Lerp(l.pitch, pitch, 1f - Mathf.Exp(-dt * 6f));
            l.src.volume = l.vol;
            l.src.pitch = l.pitch;
        }

        /// <summary>
        /// The wind's levels (AudioSource volume): wind_low's at rest in calm weather (it rises to about 2.7 times this at
        /// most: speed, air, a gust, a storm), and wind_high's at the height of a storm's gust (0.45 of it between gusts).
        /// Set for the cleaned recordings as Unity imports them (Force To Mono, no Normalize: wind_low -27.5 LUFS, a
        /// rumble under 500 Hz; wind_high -15.8 LUFS, up to 8 kHz) so the wind is heard as loud as it was tuned before the
        /// cleanup: wind_low about -41.5 LUFS at rest, near the ambience bed (-42); a storm's wind_high -33.9 LUFS
        /// between gusts and -27.2 at a full gust, 7.6 and 14.3 LU over the calm bed.
        /// </summary>
        public static float WindLow = 0.2f, WindHigh = 0.27f;

        /// <summary>
        /// The wind's two volumes (x wind_low, y wind_high) for a speed (0..1 of 36 m/s), in the air or not, a gust
        /// (0..1) and the weather's extreme wind (Weather.Howl, 0 calm .. 1 a storm). wind_high is the weather's alone.
        /// </summary>
        public static Vector2 WindLevels(float speed01, bool air, float gust, float howl) =>
            new Vector2(WindLow * (1f + 0.5f * speed01 + (air ? 0.25f : 0f) + 0.375f * gust + 0.6f * howl),
                        howl * WindHigh * (0.45f + 0.55f * gust));

        public void Set(RideSound r)
        {
            if (glide == null) return;
            float dt = Time.deltaTime, s = Mathf.Clamp01(r.speed);
            float snow = r.onSnow ? 1f : 0f, moving = Mathf.Clamp01(s * 3f);
            float master = 1f;

            // air and wind
            Mix(ambience, 0.22f * (1f - 0.5f * s), 1f, 2f, 2f, dt);
            // the wind is ambience, kept low: the speed is heard in the board (glide, carve), not in a roar. wind_low is
            // the everyday bed, a little more with speed, in the air, in a gust and in a storm; wind_high is only the
            // weather's (Weather.Howl, 0 in calm weather, so never from speed), swelling with each gust as the trees bend
            float gust = TreeWind.Strength > 0.01f ? TreeWind.Here : 0f;   // the gust the trees by the camera are bending to
            float howl = Weather.Howl;
            var wind = WindLevels(s, r.air, gust, howl);
            Mix(windLow, wind.x * master, 0.93f + 0.1f * s + 0.04f * gust, 2f, 2f, dt);
            Mix(windHigh, wind.y * master, 0.88f + 0.18f * gust, 1.5f, 1f, dt);

            // the board on the snow: snaps on when you land, cuts out the moment you leave the ground
            // (the snow underfoot, Surfaces. Corduroy is the crisp hiss and the edge's bite, as it was. Powder (rough) is
            // quiet: the soft crush, the glide and the edge hushed. Ice scrapes: the skid's chatter, pitched up, more
            // on the edge, with the glide's hiss thinner and higher. Tracked snow and crust chatter: the same layer,
            // fluttering. Slush is the powder's crush, lower and wetter)
            float snowUp = 30f, snowDown = 25f;
            float flutter = 0.6f + 0.4f * Mathf.Sin(Time.time * 53f) * Mathf.Sin(Time.time * 17f);
            float scrape = Mathf.Max(r.ice * (0.3f + 0.5f * r.carve), 0.3f * r.chatter * flutter);
            Mix(glide, snow * moving * (0.35f + 0.25f * s) * (1f - 0.6f * r.rough) * (1f - 0.5f * r.carve) * (1f - 0.3f * r.ice), 0.8f + 0.45f * s + 0.2f * r.ice - 0.12f * r.slush, snowUp, snowDown, dt);
            Mix(carve, snow * moving * Mathf.Pow(r.carve, 1.3f) * 0.75f * (1f - 0.45f * r.rough), 0.85f + 0.3f * s + 0.1f * r.carve, snowUp, 10f, dt);
            Mix(skid, snow * Mathf.Clamp01(s * 4f) * Mathf.Max(r.skid * 0.8f, scrape * 0.7f), 0.8f + 0.4f * s + 0.35f * r.ice * (1f - r.skid), snowUp, 12f, dt);
            Mix(powder, snow * r.rough * Mathf.Clamp01(s * 2f) * (0.4f + 0.4f * s), 0.8f + 0.3f * s - 0.18f * r.slush, snowUp, snowDown, dt);
            if (carve.src != null) carve.src.panStereo = Mathf.Clamp(r.pan * 0.3f, -0.3f, 0.3f);

            Mix(grindRail, r.grind == 1 ? 0.55f : 0f, 0.9f + 0.3f * s, 40f, 20f, dt);
            Mix(grindBox, r.grind == 2 ? 0.5f : 0f, 0.9f + 0.25f * s, 40f, 20f, dt);
            Mix(avalanche, Mathf.Pow(Mathf.Clamp01(r.avalanche), 1.5f) * 0.9f, 0.9f + 0.15f * r.avalanche, 3f, 1.5f, dt);
            Mix(slideNear, Mathf.Pow(Mathf.Clamp01((r.avalanche - 0.55f) / 0.45f), 1.6f) * 0.85f, 0.95f + 0.1f * r.avalanche, 3f, 1.2f, dt);
            Mix(boost, r.boost ? 0.45f : 0f, 1f + 0.2f * s, 10f, 4f, dt);

            muffle = Mathf.Lerp(muffle, r.muffle, 1f - Mathf.Exp(-dt * 5f));
            // (falling snow hushes the world too, gently: Weather.Hush, 0..1, down to about 4.5 kHz at most)
            muffler.cutoffFrequency = Mathf.Min(Mathf.Lerp(22000f, 700f, Mathf.Sqrt(muffle)), Mathf.Lerp(22000f, 4500f, Weather.Hush));
            music.Set(r, dt);

            TickScream(Time.unscaledDeltaTime);
            UpdateWorld(dt);
        }

        void UpdateWorld(float dt)
        {
            if (wild == null) return;
            // the bear: a growl and the thud of its gallop, wherever it is
            bool bear = wild.BearActive;
            bearSound.position = wild.BearPosition + Vector3.up * 0.8f;
            float run = Mathf.Clamp01(wild.BearSpeed / 12f);
            bearGrowl.volume = Mathf.MoveTowards(bearGrowl.volume, bear ? 0.35f + 0.55f * run : 0f, dt * 2f);
            bearSteps.volume = Mathf.MoveTowards(bearSteps.volume, bear ? 0.85f * run : 0f, dt * 3f);
            bearSteps.pitch = 0.75f + 0.45f * run;
            // birds singing as they pass
            chirpClock -= dt;
            if (chirpClock <= 0f)
            {
                chirpClock = Random.Range(2.5f, 7f);
                var lp = Camera.main != null ? Camera.main.transform.position : transform.position;
                if (wild.NearestFlock(lp, 90f, out Vector3 fp))
                    PlayAt(chirps[Random.Range(0, chirps.Length)], fp, 0.55f, Random.Range(0.9f, 1.15f), 8f);
            }
        }

        void PlayAt(AudioClip c, Vector3 at, float vol, float pitch, float near)
        {
            if (c == null) return;
            var s = shots3d[next3d];
            next3d = (next3d + 1) % shots3d.Length;
            s.transform.position = at;
            s.minDistance = near;
            s.pitch = pitch;
            s.PlayOneShot(c, vol);
        }

        // ---- the ski lifts (SkiLift.cs): the hum of the line from its nearest point, and a clack as a chair crosses a tower
        AudioSource liftHum; AudioClip liftClack; bool liftLoaded;

        public void LiftHum(Vector3 at, float level)
        {
            if (!liftLoaded)
            {
                liftLoaded = true;
                liftClack = C("lift_clack");
                var hum = C("lift_hum");
                if (hum != null) liftHum = Source3D("Lift Hum", hum, 9f, 130f);
            }
            if (liftHum == null) return;
            liftHum.transform.position = at;
            liftHum.volume = Mathf.MoveTowards(liftHum.volume, 0.42f * Mathf.Clamp01(level), Time.unscaledDeltaTime * 0.7f);
        }

        public void LiftClack(Vector3 at, float vol)
        {
            if (liftClack != null) PlayAt(liftClack, at, vol, Random.Range(0.93f, 1.07f), 7f);
        }

        // pentatonic steps up the scale as a trick chain grows
        static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };

        /// <summary>Ease the ledge sting out (call each frame once it should go); stops it when silent.</summary>
        public void FadeCue(float dt)
        {
            if (cue == null || !cue.isPlaying) return;
            cue.volume = Mathf.MoveTowards(cue.volume, 0f, dt / 1.5f);
            if (cue.volume <= 0.001f) cue.Stop();
        }
        public void StopCue() { if (cue != null) cue.Stop(); }

        // ---- the player's voice lines (the Pjama Buddy recording): woahs on big air, a line on a close call, a whoop
        // on a big trick, an ouch on a hard landing. Each group is a shuffle bag (never the same line twice in a row);
        // they share the rider's one voice, never talk over a scream or the "nooo", and keep a few seconds apart
        /// <summary>The player's VOICE LINES setting (pause menu, SOUND tab).</summary>
        public static bool VoiceLines
        {
            get { if (voiceSetting < 0) voiceSetting = PlayerPrefs.GetInt("summit_voicelines", 1); return voiceSetting == 1; }
            set { voiceSetting = value ? 1 : 0; }
        }
        static int voiceSetting = -1;   // (read on first use: Unity refuses PlayerPrefs while a component is being built)
        class Bag
        {
            readonly AudioClip[] clips; readonly int[] order; int next; int last = -1;
            public Bag(AudioClip[] c) { clips = c ?? new AudioClip[0]; order = new int[clips.Length]; next = order.Length; }
            public AudioClip Next()
            {
                if (clips.Length == 0) return null;
                if (next >= order.Length)
                {
                    for (int i = 0; i < order.Length; i++) order[i] = i;
                    for (int i = order.Length - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
                    if (order.Length > 1 && order[0] == last) (order[0], order[1]) = (order[1], order[0]);
                    next = 0;
                }
                last = order[next]; return clips[order[next++]];
            }
        }
        Bag[] buddy;
        float nextBuddy;
        /// <summary>Seconds of quiet the voice lines keep between them.</summary>
        public const float BuddyGap = 7f;

        void PlayBuddy(int group)
        {
            if (!VoiceLines || buddy == null || voice == null) return;
            if (Time.unscaledTime < nextBuddy) return;
            if (screamOn || NoooPlaying) return;                                         // a scream or the "nooo" has the voice
            if (voice.isPlaying && Time.unscaledTime < voiceLock) return;               // nor talk over a cry still going
            var c = buddy[group].Next();
            if (c == null) return;
            voice.clip = c; voice.pitch = 1f; voice.volume = 0.95f; voice.Play();
            voiceLock = Time.unscaledTime + c.length;
            nextBuddy = Time.unscaledTime + c.length + BuddyGap;
        }

        // the player's screams, one after another from the impact until the tumble has slowed (SummitGame decides when)
        bool screamOn, screamFading;
        public bool Screaming => screamOn;
        bool IsScream(AudioClip c) => c != null && bigScreams != null && System.Array.IndexOf(bigScreams, c) >= 0;

        void NextScream()
        {
            // a shuffle bag: every scream once, in a new order each time round, never the same one twice in a row
            if (screamNext >= screamBag.Length)
            {
                int last = screamBag.Length > 0 && screamNext > 0 ? screamBag[screamBag.Length - 1] : -1;
                for (int i = 0; i < screamBag.Length; i++) screamBag[i] = i;
                for (int i = screamBag.Length - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (screamBag[i], screamBag[j]) = (screamBag[j], screamBag[i]); }
                if (screamBag.Length > 1 && screamBag[0] == last) (screamBag[0], screamBag[1]) = (screamBag[1], screamBag[0]);
                screamNext = 0;
            }
            voice.clip = bigScreams[screamBag[screamNext++]];
            voice.pitch = Random.Range(0.97f, 1.03f); voice.volume = 1f; voice.Play();
            bigStart = Time.unscaledTime;
            voiceLock = Time.unscaledTime + voice.clip.length;
            screamFading = false;
        }

        /// <summary>Start screaming now (the impact), and keep it going clip after clip until StopScreaming.</summary>
        public void StartScreaming()
        {
            if (voice == null || bigScreams == null || bigScreams.Length == 0) return;
            screamOn = true;
            NextScream();
        }

        /// <summary>A fresh scream for a big hit mid-tumble (each one gets half a second first).</summary>
        public void ScreamHit()
        {
            if (!screamOn || Time.unscaledTime - bigStart < 0.5f) return;
            NextScream();
        }

        /// <summary>The tumble has slowed: let the scream die away (or cut it dead, for a reset).</summary>
        public void StopScreaming(bool now = false)
        {
            if (voice == null) return;
            screamOn = false;
            bool ours = voice.isPlaying && IsScream(voice.clip);
            if (now) { screamFading = false; if (ours) { voice.Stop(); voice.volume = 1f; } return; }
            screamFading = ours;
        }

        // the "nooo": down a crevasse it fades as the body drops away into the dark (by depth, and a little
        // with time); off a cliff it is cut to a short cry that dies away as you fall
        float noooStart, noooFor = -1f, noooDepth;
        void PlayNooo(float seconds)
        {
            voice.clip = nooo; voice.pitch = 1f; voice.volume = 1f; voice.Play();
            screamOn = false; screamFading = false;
            noooStart = Time.unscaledTime; noooFor = seconds; noooDepth = 0f;
            voiceLock = Time.unscaledTime + (seconds > 0f ? seconds : nooo.length);
        }
        /// <summary>The short "nooo" going off a cliff, gone after this many seconds.</summary>
        public void NoooFor(float seconds) { if (voice != null && nooo != null) PlayNooo(seconds); }
        /// <summary>How far the falling body is below the snow (m): the crevasse cry fades with it.</summary>
        public void NoooDepth(float d) { noooDepth = Mathf.Max(noooDepth, d); }
        public bool NoooPlaying => voice != null && nooo != null && voice.clip == nooo && voice.isPlaying;

        void TickNooo()
        {
            if (voice == null || nooo == null || voice.clip != nooo || !voice.isPlaying) return;
            float since = Time.unscaledTime - noooStart, want;
            if (noooFor > 0f)
            {
                if (since >= noooFor) { voice.Stop(); voice.volume = 1f; return; }
                float k = since / noooFor;
                want = 1f - k * k;
            }
            else
                want = Mathf.Lerp(1f, 0.12f, Mathf.Clamp01(noooDepth / 22f)) * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(since / nooo.length));
            voice.volume = Mathf.MoveTowards(voice.volume, want, Time.unscaledDeltaTime * 3f);
        }

        void TickScream(float dt)
        {
            if (voice == null || AudioListener.pause) return;
            TickNooo();
            if (screamOn && !voice.isPlaying) NextScream();   // one ends, the next begins
            if (screamFading)
            {
                if (!IsScream(voice.clip)) { screamFading = false; return; }   // something else has the voice now
                voice.volume = Mathf.MoveTowards(voice.volume, 0f, dt / 0.35f);
                if (voice.volume <= 0.001f) { voice.Stop(); voice.volume = 1f; screamFading = false; }
            }
        }

        public void Play(Sfx kind, float amount = 1f)
        {
            if (music != null) music.OnSfx(kind, amount);
            AudioClip c = null; float vol = 0.7f, pitch = Random.Range(0.96f, 1.04f);
            switch (kind)
            {
                case Sfx.Pop: c = pop; vol = 0.6f; break;
                case Sfx.Takeoff: c = whoosh; vol = 0.15f + 0.3f * Mathf.Clamp01(amount); break;
                case Sfx.Spin: c = spin; vol = 0.35f; pitch = Random.Range(0.92f, 1.1f); break;
                case Sfx.Land: c = lands[Random.Range(0, lands.Length)]; vol = 0.3f + 0.5f * Mathf.Clamp01(amount); pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.LandHard: c = landHard; vol = 0.9f; break;
                case Sfx.Crash: c = crash; vol = 0.95f * Mathf.Clamp01(amount); break;
                case Sfx.HitTree: c = hitTree; vol = 1f; break;
                case Sfx.HitRock: c = hitRock; vol = 1f; break;
                case Sfx.Crevasse: c = crevasse; vol = 0.9f; pitch = 1f; break;
                case Sfx.Thump: c = thumps[Random.Range(0, thumps.Length)]; vol = Mathf.Clamp01(amount) * 0.7f; pitch = Random.Range(0.85f, 1.15f); break;
                case Sfx.Scrape: c = thumps[Random.Range(0, thumps.Length)]; vol = 0.25f + 0.04f * amount; pitch = 1.3f; break;
                case Sfx.Clack: c = clack; vol = Mathf.Clamp01(amount) * 0.6f; pitch = Random.Range(0.85f, 1.2f); break;
                case Sfx.GrindRail: c = clank; vol = 0.6f; break;
                case Sfx.GrindBox: c = clack; vol = 0.6f; pitch = 0.6f; break;
                case Sfx.Trick: c = chime; vol = 0.55f; pitch = Mathf.Pow(2f, Scale[Mathf.Clamp((int)amount, 0, Scale.Length - 1)] / 36f); break;   // a chain lifts it a little
                case Sfx.TrickBig: c = trickBig; vol = 0.65f; pitch = Mathf.Pow(2f, Scale[Mathf.Clamp((int)amount, 0, Scale.Length - 1)] / 36f); break;
                case Sfx.CloseCall: c = closeCall; vol = 0.5f; break;
                case Sfx.PointTick: c = chime; vol = 0.14f; pitch = 1.5f * Mathf.Pow(2f, Scale[Mathf.Clamp((int)amount, 0, Scale.Length - 1)] / 36f); break;
                case Sfx.Bank:
                    if (amount < 2f) { c = chime; vol = 0.6f; pitch = 1.26f; }
                    else { c = trickBig; vol = 0.75f; pitch = 1f + 0.06f * Mathf.Clamp(amount, 0f, 4f); }
                    break;
                case Sfx.ComboLost: c = thumps[0]; vol = 0.6f; pitch = 0.55f; break;
                case Sfx.BillTick: c = click; vol = 0.28f; pitch = Random.Range(1.25f, 1.4f); break;
                case Sfx.Soda: c = soda; vol = 0.75f; pitch = 1f; break;
                case Sfx.ShieldGet: c = shieldGet; vol = 0.5f; pitch = Mathf.Pow(2f, (amount - 1f) * 2f / 12f); break;
                case Sfx.ShieldFull: c = powerShort; vol = 0.5f; pitch = 1.15f; break;
                case Sfx.ShieldHit: c = shieldHit; vol = 0.8f; break;
                case Sfx.Click: c = click; vol = 0.5f; pitch = 1f; break;
                case Sfx.Play: c = play; vol = 0.5f; pitch = 1f; break;
                case Sfx.Buried: c = buried; vol = 1f; pitch = 1f; break;
                case Sfx.BearRoar: c = bearRoar; vol = 0.9f; break;
                case Sfx.Bone: c = bones[Random.Range(0, bones.Length)]; vol = 1f; pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.BoneLeg: c = bonesLeg[Random.Range(0, bonesLeg.Length)]; vol = 1f; pitch = Random.Range(0.93f, 1.03f); break;
                case Sfx.BoneArm: c = bonesLeg[Random.Range(0, bonesLeg.Length)]; vol = 0.95f; pitch = Random.Range(1.08f, 1.18f); break;
                case Sfx.BoneBack: c = bonesBack[Random.Range(0, bonesBack.Length)]; vol = 1f; pitch = Random.Range(0.92f, 1.02f); break;
                case Sfx.BoneNeck: c = bonesNeck[Random.Range(0, bonesNeck.Length)]; vol = 1f; pitch = Random.Range(0.96f, 1.04f); break;
                case Sfx.SmashWood: c = smashWood; vol = Mathf.Clamp(amount, 0.3f, 1f); pitch = Random.Range(0.9f, 1.1f) * (amount < 0.7f ? 1.15f : 1f); break;   // (quieter and lighter: a fence's thin rails)
                case Sfx.SmashRock: c = smashRock; vol = 1f; pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.PowerJump: c = powerJump; vol = 0.7f; pitch = 1f; break;
                case Sfx.PowerStar: c = powerStar; vol = 0.65f; pitch = 1f; break;
                case Sfx.SlideHit: c = slideHit; vol = 1f; pitch = 1f; break;
                case Sfx.SlideCrack: c = slideCrack; vol = 1f; pitch = 1f; break;
                case Sfx.Branches:
                    if (branches == null) branches = BranchClips();
                    c = branches[Random.Range(0, branches.Length)]; vol = 0.35f + 0.6f * Mathf.Clamp01(amount); pitch = Random.Range(0.9f, 1.12f); break;
                case Sfx.ClothRip:
                    if (clothRips == null) clothRips = ClothClips();
                    c = clothRips[Random.Range(0, clothRips.Length)]; vol = 0.4f + 0.5f * Mathf.Clamp01(amount); pitch = Random.Range(0.92f, 1.1f); break;
            }
            if (kind == Sfx.Nooo)
            {
                if (nooo == null) { kind = Sfx.Scream; }
                else { PlayNooo(-1f); return; }
            }
            if (kind == Sfx.BigScream)
            {
                if (bigScreams == null || bigScreams.Length == 0) { kind = Sfx.Scream; }
                else
                {
                    if (voice.isPlaying && voice.clip == nooo && Time.unscaledTime < voiceLock) return;   // the crevasse cry wins
                    if (voice.isPlaying && Time.unscaledTime - bigStart < 0.5f) return;                   // let each one get going
                    NextScream();
                    return;
                }
            }
            if (kind >= Sfx.BuddyWoah && kind <= Sfx.BuddyDeep) { PlayBuddy(kind - Sfx.BuddyWoah); return; }
            if (kind == Sfx.Ledge)
            {
                if (ledge != null) { cue.clip = ledge; cue.volume = 0.9f; cue.pitch = 1f; cue.Play(); }
                return;
            }
            if (kind == Sfx.Scream || kind == Sfx.Grunt || kind == Sfx.Yelp || kind == Sfx.Groan)
            {
                // the rider's voice: one at a time, and a scream or the long "nooo" is not cut short by the grunts and
                // yelps of the tumble that follows (a new scream may replace a scream)
                if (voice.isPlaying && Time.unscaledTime < voiceLock && kind != Sfx.Scream) return;
                if (voice.isPlaying && voice.clip == nooo && Time.unscaledTime < voiceLock) return;
                if (groans == null) groans = new[] { groan };
                var set = kind == Sfx.Scream ? screams : kind == Sfx.Grunt ? grunts : kind == Sfx.Yelp ? yelps : groans;
                voice.clip = set[Random.Range(0, set.Length)];
                voice.pitch = Random.Range(0.94f, 1.06f) * (kind == Sfx.Grunt ? 1f : 1f);
                voice.volume = kind == Sfx.Scream ? 0.85f : kind == Sfx.Yelp ? 0.8f : kind == Sfx.Groan ? 0.5f : 0.4f + 0.4f * Mathf.Clamp01(amount);
                voice.Play();
                voiceLock = kind == Sfx.Scream ? Time.unscaledTime + Mathf.Min(1.2f, voice.clip.length) : 0f;
                return;
            }
            if (c == null || vol <= 0.001f) return;
            if (kind == Sfx.Click || kind == Sfx.Play) { ui.pitch = pitch; ui.PlayOneShot(c, vol); return; }
            var s = shots[nextShot];
            nextShot = (nextShot + 1) % shots.Length;
            s.pitch = pitch;
            s.PlayOneShot(c, vol);
        }
    }
}
