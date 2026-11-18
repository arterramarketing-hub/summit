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
        SlideCrack    // the slope letting go behind you: a crack, and the rumble building
    }

    /// <summary>What the ride sounds like this frame (all 0..1 unless noted).</summary>
    public struct RideSound
    {
        public float speed, carve, skid, rough, avalanche, muffle, pan;
        public bool onSnow, air, boost, riding;
        public bool camp, ended;   // walking round the summit camp; on an end screen (for the music)
        public int grind;   // 0 none, 1 rail, 2 box
    }

    /// <summary>
    /// The whole soundscape, from clips made by Tools/MakeAudio.swift (Resources/Audio):
    /// a bed of mountain air; two layers of wind (a deep roar and a whistle past the ears) that rise
    /// with speed; the board on the snow as separate layers that the ride crossfades between: a
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
        AudioClip[] lands, thumps, crows, chirps, bones, screams, grunts, yelps;
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
            screams = new[] { C("scream_1"), C("scream_2"), C("scream_3") };
            grunts = new[] { C("grunt_1"), C("grunt_2"), C("grunt_3"), C("grunt_4") };
            yelps = new[] { C("yelp_1"), C("yelp_2") };
            groan = C("groan");
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0f;
            nooo = C("user_crevasse_nooo"); ledge = C("user_ledge_suspense");
            bigScreams = Resources.LoadAll<AudioClip>("Audio/UserScreams");
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

        public void Set(RideSound r)
        {
            if (glide == null) return;
            float dt = Time.deltaTime, s = Mathf.Clamp01(r.speed);
            float snow = r.onSnow ? 1f : 0f, moving = Mathf.Clamp01(s * 3f);
            float master = 1f;

            // air and wind
            Mix(ambience, 0.22f * (1f - 0.5f * s), 1f, 2f, 2f, dt);
            Mix(windLow, (0.05f + 0.55f * s * s) * master, 0.8f + 0.4f * s, 4f, 3f, dt);
            Mix(windHigh, (0.02f + 0.42f * Mathf.Pow(s, 2.2f) + (r.air ? 0.12f + 0.15f * s : 0f)) * master, 0.85f + 0.35f * s, 4f, 3f, dt);

            // the board on the snow: snaps on when you land, cuts out the moment you leave the ground
            float snowUp = 30f, snowDown = 25f;
            Mix(glide, snow * moving * (0.35f + 0.25f * s) * (1f - 0.6f * r.rough) * (1f - 0.5f * r.carve), 0.8f + 0.45f * s, snowUp, snowDown, dt);
            Mix(carve, snow * moving * Mathf.Pow(r.carve, 1.3f) * 0.75f, 0.85f + 0.3f * s + 0.1f * r.carve, snowUp, 10f, dt);
            Mix(skid, snow * Mathf.Clamp01(s * 4f) * r.skid * 0.8f, 0.8f + 0.4f * s, snowUp, 12f, dt);
            Mix(powder, snow * r.rough * Mathf.Clamp01(s * 2f) * (0.4f + 0.4f * s), 0.8f + 0.3f * s, snowUp, snowDown, dt);
            if (carve.src != null) carve.src.panStereo = Mathf.Clamp(r.pan * 0.3f, -0.3f, 0.3f);

            Mix(grindRail, r.grind == 1 ? 0.55f : 0f, 0.9f + 0.3f * s, 40f, 20f, dt);
            Mix(grindBox, r.grind == 2 ? 0.5f : 0f, 0.9f + 0.25f * s, 40f, 20f, dt);
            Mix(avalanche, Mathf.Pow(Mathf.Clamp01(r.avalanche), 1.5f) * 0.9f, 0.9f + 0.15f * r.avalanche, 3f, 1.5f, dt);
            Mix(slideNear, Mathf.Pow(Mathf.Clamp01((r.avalanche - 0.55f) / 0.45f), 1.6f) * 0.85f, 0.95f + 0.1f * r.avalanche, 3f, 1.2f, dt);
            Mix(boost, r.boost ? 0.45f : 0f, 1f + 0.2f * s, 10f, 4f, dt);

            muffle = Mathf.Lerp(muffle, r.muffle, 1f - Mathf.Exp(-dt * 5f));
            muffler.cutoffFrequency = Mathf.Lerp(22000f, 700f, Mathf.Sqrt(muffle));
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

        void TickScream(float dt)
        {
            if (voice == null || AudioListener.pause) return;
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
                case Sfx.Soda: c = soda; vol = 0.75f; pitch = 1f; break;
                case Sfx.ShieldGet: c = shieldGet; vol = 0.5f; pitch = Mathf.Pow(2f, (amount - 1f) * 2f / 12f); break;
                case Sfx.ShieldFull: c = powerShort; vol = 0.5f; pitch = 1.15f; break;
                case Sfx.ShieldHit: c = shieldHit; vol = 0.8f; break;
                case Sfx.Click: c = click; vol = 0.5f; pitch = 1f; break;
                case Sfx.Play: c = play; vol = 0.5f; pitch = 1f; break;
                case Sfx.Buried: c = buried; vol = 1f; pitch = 1f; break;
                case Sfx.BearRoar: c = bearRoar; vol = 0.9f; break;
                case Sfx.Bone: c = bones[Random.Range(0, bones.Length)]; vol = 1f; pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.SmashWood: c = smashWood; vol = 1f; pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.SmashRock: c = smashRock; vol = 1f; pitch = Random.Range(0.9f, 1.1f); break;
                case Sfx.PowerJump: c = powerJump; vol = 0.7f; pitch = 1f; break;
                case Sfx.PowerStar: c = powerStar; vol = 0.65f; pitch = 1f; break;
                case Sfx.SlideHit: c = slideHit; vol = 1f; pitch = 1f; break;
                case Sfx.SlideCrack: c = slideCrack; vol = 1f; pitch = 1f; break;
            }
            if (kind == Sfx.Nooo)
            {
                if (nooo == null) { kind = Sfx.Scream; }
                else { voice.clip = nooo; voice.pitch = 1f; voice.volume = 1f; voice.Play(); voiceLock = Time.unscaledTime + nooo.length; return; }
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
