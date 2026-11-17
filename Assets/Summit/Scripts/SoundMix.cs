using UnityEngine;

namespace Summit
{
    public enum Sfx
    {
        Pop, Takeoff, Land, LandHard, Crash, HitTree, HitRock, Crevasse, Thump, Clack, Spin,
        Trick, TrickBig, CloseCall, Soda, ShieldGet, ShieldHit, ShieldFull, Click, Play, Buried,
        Scrape, GrindRail, GrindBox, BearRoar, Bone
    }

    /// <summary>What the ride sounds like this frame (all 0..1 unless noted).</summary>
    public struct RideSound
    {
        public float speed, carve, skid, rough, avalanche, muffle, pan;
        public bool onSnow, air, boost, riding;
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
        int nextShot, next3d;
        AudioLowPassFilter muffler;
        float muffle;

        AudioClip powerShort, pop, whoosh, spin, landHard, crash, hitTree, hitRock, crevasse, clack, clank, chime, trickBig,
                  closeCall, soda, shieldGet, shieldHit, click, play, buried, bearRoar, flap;
        AudioClip[] lands, thumps, crows, chirps, bones;

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

            shots = new AudioSource[10];
            for (int i = 0; i < shots.Length; i++) { shots[i] = gameObject.AddComponent<AudioSource>(); shots[i].playOnAwake = false; shots[i].spatialBlend = 0f; }
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
            lands = new[] { C("land_1"), C("land_2"), C("land_3") };
            thumps = new[] { C("thump_1"), C("thump_2"), C("thump_3") };
            crows = new[] { C("crow_1"), C("crow_2") };
            chirps = new[] { C("chirp_1"), C("chirp_2"), C("chirp_3") };
            bones = new[] { C("bone_1"), C("bone_2"), C("bone_3") };
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
            Mix(boost, r.boost ? 0.45f : 0f, 1f + 0.2f * s, 10f, 4f, dt);

            muffle = Mathf.Lerp(muffle, r.muffle, 1f - Mathf.Exp(-dt * 5f));
            muffler.cutoffFrequency = Mathf.Lerp(22000f, 700f, Mathf.Sqrt(muffle));

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

        public void Play(Sfx kind, float amount = 1f)
        {
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
            }
            if (c == null || vol <= 0.001f) return;
            var s = shots[nextShot];
            nextShot = (nextShot + 1) % shots.Length;
            s.pitch = pitch;
            s.PlayOneShot(c, vol);
        }
    }
}
