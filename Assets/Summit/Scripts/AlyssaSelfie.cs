using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Alyssa's selfies. Every so often (30 to 90 seconds apart, at random) she takes one in the lull of her round,
    /// when the s'more is eaten and she's warming her hands, and never while she's talking to you: the phone comes
    /// out of her pocket and up to arm's length, she turns her head to it and smiles, throws a peace sign, the flash
    /// goes (lighting her face, which tells at night) with a faint shutter click if you're near, then she brings it in
    /// to look at the picture and puts it away. Most often it's held up in front; sometimes angled out to the side to
    /// get the mountain in, and if you're standing close she turns it so you're in the shot too.
    /// The phone is a small prop, hidden the rest of the time; the pose is her same rig, arms placed by IK.
    /// </summary>
    public partial class Alyssa
    {
        public const float SelfieLen = 5.2f;
        // the selfie, in seconds: out and up, the pose (the flash at FlashAt), a look at the picture, away
        const float UpAt = 0.9f, FlashAt = 2.3f, PeekAt = 3.3f, AwayAt = 4.4f;
        Transform phone, screen, vee;
        Light flash;
        AudioSource shutter;
        float selfieT = -1f, sinceSelfie, nextSelfie = 40f;
        int selfieKind;              // 0 held up in front, 1 out to the side for the mountain, 2 higher, to get you in too
        System.Random rng;
        Material screenMat;

        /// <summary>In the middle of one (test tools), and how many she's taken.</summary>
        public bool TakingSelfie => selfieT >= 0f;
        public int Selfies { get; private set; }
        /// <summary>The phone (test tools: the selfie's own point of view).</summary>
        public Transform Phone => phone;

        void BuildPhone()
        {
            rng = new System.Random(1962);
            nextSelfie = 30f + (float)rng.NextDouble() * 60f;
            var black = Marshmallow.Plain(new Color(0.05f, 0.05f, 0.06f), 0.75f);
            phone = new GameObject("Phone").transform;
            phone.SetParent(transform, false);
            Marshmallow.Prim(PrimitiveType.Cube, phone, Vector3.zero, new Vector3(0.072f, 0.148f, 0.009f), black);
            // its screen faces her (-Z of the phone), the lens on the back faces out
            var sh = Resources.Load<Shader>("StudioBackdrop");   // unlit: a screen gives its own light
            screenMat = new Material(sh != null ? sh : Marshmallow.Lit()) { name = "PhoneScreen" };
            screenMat.SetColor("_Tint", new Color(0.75f, 0.85f, 1f));
            screen = Marshmallow.Prim(PrimitiveType.Quad, phone, new Vector3(0f, 0f, -0.0051f), new Vector3(0.064f, 0.136f, 1f), screenMat);
            screen.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Marshmallow.Prim(PrimitiveType.Cylinder, phone, new Vector3(0.02f, 0.052f, 0.006f), new Vector3(0.014f, 0.002f, 0.014f), Marshmallow.Plain(new Color(0.2f, 0.22f, 0.26f), 0.95f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
            phone.gameObject.SetActive(false);
            // two gloved fingers for the peace sign, on her left hand
            vee = new GameObject("PeaceSign").transform;
            vee.SetParent(transform, false);
            var glove = Marshmallow.Plain(new Color(0.03f, 0.03f, 0.035f), 0.15f);
            for (int s = -1; s <= 1; s += 2)
                Marshmallow.Prim(PrimitiveType.Capsule, vee, new Vector3(s * 0.016f, 0.05f, 0f), new Vector3(0.019f, 0.032f, 0.019f), glove).localRotation = Quaternion.Euler(0f, 0f, -s * 14f);
            vee.gameObject.SetActive(false);
            // the flash (and the screen's glow on her face while she looks at it)
            flash = new GameObject("PhoneLight").AddComponent<Light>();
            flash.transform.SetParent(phone, false);
            flash.type = LightType.Point; flash.range = 1.2f; flash.shadows = LightShadows.None;
            flash.color = new Color(0.9f, 0.95f, 1f); flash.renderMode = LightRenderMode.ForcePixel;
            flash.enabled = false;
            // a faint shutter click: made here (a short burst of noise), heard only close by
            shutter = gameObject.AddComponent<AudioSource>();
            shutter.playOnAwake = false; shutter.spatialBlend = 1f; shutter.rolloffMode = AudioRolloffMode.Linear;
            shutter.minDistance = 1f; shutter.maxDistance = 7f; shutter.volume = 0.35f;
            shutter.clip = Click();
        }

        static AudioClip click;
        static AudioClip Click()
        {
            if (click != null) return click;
            const int rate = 44100, n = rate / 14;
            var d = new float[n];
            var r = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // two clacks, the shutter opening and closing, each a quick burst of noise
                float env = Mathf.Exp(-t * 260f) + 0.7f * Mathf.Exp(-Mathf.Max(0f, t - 0.035f) * 300f) * (t > 0.035f ? 1f : 0f);
                d[i] = ((float)r.NextDouble() * 2f - 1f) * env * 0.8f;
            }
            click = AudioClip.Create("Shutter", n, 1, rate, false);
            click.SetData(d, 0);
            return click;
        }

        /// <summary>Test tools: take one now (she must be in the lull of her round: it's moved there).</summary>
        public void TestSelfie(int kind)
        {
            RoundTime = 23.4f;
            selfieT = 0f; selfieKind = kind; sinceSelfie = 0f;
        }

        /// <summary>The selfie clock (from Step): time one, start it, run it, end it.</summary>
        void SelfieStep(float dt, bool talking)
        {
            sinceSelfie += dt;
            if (selfieT < 0f)
            {
                // only in the lull (her hands free, nothing in the fire) and never mid-conversation
                if (!talking && sinceSelfie >= nextSelfie && t > 23.2f && t < 30.6f - SelfieLen)
                {
                    selfieT = 0f; sinceSelfie = 0f;
                    nextSelfie = 30f + (float)rng.NextDouble() * 60f;
                    double roll = rng.NextDouble();
                    bool near = Hero != null && (Hero.position - seat).sqrMagnitude < 9f;
                    selfieKind = near && roll < 0.5 ? 2 : roll > 0.75 ? 1 : 0;   // you in it now and then, the mountain sometimes
                }
                else return;
            }
            // you walking up and talking: it's put away at once
            if (talking && selfieT < AwayAt) selfieT = AwayAt;
            float before = selfieT;
            selfieT += dt;
            if (before < FlashAt && selfieT >= FlashAt) { Selfies++; if (shutter != null && Application.isPlaying) shutter.Play(); }
            if (selfieT >= SelfieLen) { selfieT = -1f; phone.gameObject.SetActive(false); vee.gameObject.SetActive(false); flash.enabled = false; }
        }

        /// <summary>How much of the selfie pose there is now (0 none, 1 phone up).</summary>
        float SelfieWeight => selfieT < 0f ? 0f : Sm(0f, UpAt, selfieT) * (1f - Sm(AwayAt, SelfieLen, selfieT));

        /// <summary>
        /// The selfie's pose over her own (after the arms are placed for the round): the right arm out with the phone,
        /// the left hand up in a peace sign beside her face, her head to the phone. Called from Pose.
        /// </summary>
        void PoseSelfie(float s)
        {
            float w = SelfieWeight;
            if (w <= 0.001f) return;
            Vector3 head = h.Head.position, sh = h.UpperArm[1].position;
            // where the phone is held, measured from her right shoulder so the arm can always reach it: up and out to her
            // right in front (her face turned to it, not hidden by it); wide to the side for the mountain; or turned so
            // you're behind her in the shot
            Vector3 dir = face * 0.3f + right * 0.3f + Vector3.up * 0.32f;   // (out to the side enough that the arm never crosses her face)
            if (selfieKind == 1) dir = face * 0.2f + right * 0.38f + Vector3.up * 0.22f;
            // with you: you're standing beside her, so it's the same hold, higher and a little further out, the phone
            // aimed between the two of you (a selfie with a friend: side by side, the phone in front)
            if (selfieKind == 2) dir = face * 0.32f + right * 0.24f + Vector3.up * 0.38f;
            float look = selfieT > PeekAt ? Sm(PeekAt, PeekAt + 0.4f, selfieT) : 0f;   // brought in to look at the picture
            Vector3 held = sh + dir * s;
            Vector3 check = h.Chest.position + face * 0.32f * s + Vector3.up * 0.12f * s + right * 0.04f;
            Vector3 phoneAt = Vector3.Lerp(held, check, look);
            Vector3 hand = Vector3.Lerp(h.Hand[1].position, phoneAt, w);
            h.ArmTo(1, hand, sh + right * 0.35f - Vector3.up * 0.25f);
            // the peace sign: the left hand up by her face, the other side from the phone
            float peace = Sm(UpAt, UpAt + 0.4f, selfieT) * (1f - Sm(PeekAt - 0.3f, PeekAt + 0.1f, selfieT));
            Vector3 vAt = head - right * 0.17f * s + face * 0.1f * s + Vector3.up * 0.04f * s;
            if (peace > 0.01f) h.ArmTo(0, Vector3.Lerp(h.Hand[0].position, vAt, peace), h.UpperArm[0].position - right * 0.35f - Vector3.up * 0.2f);
            // the phone in her hand, its screen to her face
            phone.gameObject.SetActive(w > 0.05f);
            Vector3 aimAt = head + Vector3.up * 0.05f;
            if (selfieKind == 2 && Hero != null) aimAt = Vector3.Lerp(aimAt, Hero.position + Vector3.up * 1.55f, 0.45f);
            Vector3 toFace = (aimAt - h.Hand[1].position).normalized;
            phone.position = h.Hand[1].position + toFace * 0.03f + Vector3.up * 0.04f;
            phone.rotation = Quaternion.LookRotation(-toFace, Vector3.up);
            vee.gameObject.SetActive(peace > 0.3f);
            if (peace > 0.3f) { vee.position = h.Hand[0].position + Vector3.up * 0.04f; vee.rotation = Quaternion.LookRotation(face, Vector3.up); }
            // the flash, then the screen's glow while she looks at the picture (its light on her face, at night above all)
            float f = Mathf.Clamp01(1f - Mathf.Abs(selfieT - FlashAt) / 0.09f);
            float glow = 0.35f * w + 0.4f * look;
            flash.enabled = f > 0f || glow > 0.05f;
            flash.intensity = f * 2.2f + glow * (0.35f + 0.9f * Shader.GetGlobalFloat("_SummitNight"));   // (a phone's flash at arm's length: bright, not blinding)
            screenMat.SetColor("_Tint", Color.Lerp(new Color(0.55f, 0.65f, 0.85f), new Color(1.6f, 1.6f, 1.7f), Mathf.Max(f, look * 0.6f)));
        }

        /// <summary>Where her head turns during one: to the phone.</summary>
        Vector3 SelfieLook(Vector3 eye, Vector3 otherwise)
        {
            float w = SelfieWeight;
            if (w <= 0.001f || phone == null) return otherwise;
            Vector3 to = phone.position - eye;
            return Vector3.Slerp(otherwise.normalized, to.normalized, w) * Mathf.Max(0.1f, to.magnitude);
        }

        /// <summary>The hero, when they're near enough to get in the shot (the camp tells her).</summary>
        public Transform Hero;
    }
}
