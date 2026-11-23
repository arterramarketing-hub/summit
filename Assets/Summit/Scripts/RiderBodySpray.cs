using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The snow the board throws (snowfx), all drawn with Resources/SnowSpray.shader (soft round puffs, lit by the
    /// sky, the sun thrown forward so backlit spray glows, the floodlights, and fogged):
    ///  - crystals: a fan of fine snow off the edge, out to the outside of the turn and back, by how hard the edge is
    ///    pushing (Rider.Bite) and how much it skids. Light and drifting in powder (more of it), a fine fast icy hiss
    ///    on ice, a small puff on the groomed run;
    ///  - clumps: heavier lumps that arc out and fall under gravity (powder and crud throw the most, ice none);
    ///  - the wake hanging in the air behind, and the bow wave off the nose (RiderBody built them; here they are given
    ///    the lit material and tuned for the snow);
    ///  - the hand dragged through the snow in a hard lean (RiderBodyTouch), or a ski pole's tip: a spray of its own,
    ///    and a small groove in the track beside the board's.
    /// Nothing here touches the riding: only Rider's readouts are read.
    /// </summary>
    public partial class RiderBody
    {
        ParticleSystem clumps, dragSpray;
        ParticleSystem.EmissionModule clumpsEm, dragEm;
        SnowTrail dragTrail;
        Material sprayLit, crystalLit, clumpLit;
        Transform sprayAt;
        SnowMix sprayMix;
        bool sprayMixSet;

        public static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.Center, ParticleSystemVertexStream.StableRandomX,
        };

        /// <summary>The most each system holds (the phone's budget: see snowfx.md).</summary>
        public const int CrystalMax = 450, ClumpMax = 110, WakeMax = 260, BowMax = 320, DragMax = 120;

        static Material SprayMaterial(string name, Color c, float core, float sparkle, Material fallback)
        {
            var sh = Resources.Load<Shader>("SnowSpray");
            if (sh == null || !sh.isSupported) return fallback;
            var m = new Material(sh) { name = name, renderQueue = 3000 };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Core", core);
            m.SetFloat("_Sparkle", sparkle);
            m.SetFloat("_Grain", core > 2f ? 0.75f : core < 1f ? 0.45f : 0.6f);   // (fine crystals most broken up, clumps least)
            return m;
        }

        static void Lit(ParticleSystem ps, Material m)
        {
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = m;
            if (m != null && m.shader != null && m.shader.name == "Summit/SnowSpray") pr.SetActiveVertexStreams(Streams);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sortMode = ParticleSystemSortMode.None;
            pr.maxParticleSize = 0.25f;   // (never a screenful in front of the lens: the overdraw stays bounded)
        }

        /// <summary>After the systems are built (Build): the lit material on them all, the clumps and the drag spray made.</summary>
        void SetUpSpray(Material sprayMat, Shader lit)
        {
            // snow in the air: a shade under white (it is lit here, not tinted by the time of day)
            sprayLit = SprayMaterial("SnowSprayPuff", new Color(0.84f, 0.86f, 0.91f, 1f), 1.2f, 0.25f, sprayMat);
            crystalLit = SprayMaterial("SnowSprayCrystal", new Color(0.86f, 0.88f, 0.93f, 1f), 2.2f, 0.9f, sprayMat);
            clumpLit = SprayMaterial("SnowSprayClump", new Color(0.80f, 0.82f, 0.87f, 1f), 0.9f, 0.1f, sprayMat);

            // where the edge throws its snow from: moved each frame to the outside of the turn (SprayFrame)
            sprayAt = new GameObject("SprayAt").transform;
            sprayAt.SetParent(transform, false);
            spray.transform.SetParent(sprayAt, false);
            spray.transform.localPosition = Vector3.zero; spray.transform.localRotation = Quaternion.identity;
            var m = spray.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.0f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            m.startColor = new Color(1f, 1f, 1f, 0.75f);
            m.gravityModifier = 0.55f;
            m.maxParticles = CrystalMax;
            var sh = spray.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 24f; sh.radius = 0.1f;
            var iv = spray.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Initial; iv.curve = 0.35f;
            var drag = spray.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.6f; drag.multiplyDragByParticleSize = false; drag.limit = 40f;
            var sz = spray.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 2.2f));   // (crystals spreading into a mist)
            var cl = spray.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.45f, 0.45f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            Lit(spray, crystalLit);

            // the clumps: heavier, arcing out and falling
            var cg = new GameObject("Clumps");
            cg.transform.SetParent(sprayAt, false);
            clumps = cg.AddComponent<ParticleSystem>();
            clumps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var cm = clumps.main;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            cm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            cm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
            cm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.86f, 0.88f, 0.93f, 0.95f));   // (some in their own shade)
            cm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            cm.gravityModifier = 1f;
            cm.simulationSpace = ParticleSystemSimulationSpace.World;
            cm.maxParticles = ClumpMax;
            cm.playOnAwake = false;
            var csh = clumps.shape; csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 18f; csh.radius = 0.08f;
            var civ = clumps.inheritVelocity; civ.enabled = true; civ.mode = ParticleSystemInheritVelocityMode.Initial; civ.curve = 0.5f;
            var ccol = clumps.colorOverLifetime; ccol.enabled = true;
            var cgd = new Gradient();
            cgd.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            ccol.color = cgd;
            var csz = clumps.sizeOverLifetime; csz.enabled = true; csz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));   // (breaking up as they fall)
            clumpsEm = clumps.emission; clumpsEm.rateOverTime = 0f;
            Lit(clumps, clumpLit);
            clumps.Play();

            // the wake and the bow wave: lit, and fewer
            var wm = wake.main; wm.maxParticles = WakeMax;
            Lit(wake, sprayLit);
            var bm = bow.main; bm.maxParticles = BowMax;
            Lit(bow, sprayLit);

            // the hand (or a pole's tip) dragged through the snow
            var dg = new GameObject("DragSpray");
            dg.transform.SetParent(transform, false);
            dragSpray = dg.AddComponent<ParticleSystem>();
            dragSpray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var dm = dragSpray.main;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            dm.startColor = new Color(1f, 1f, 1f, 0.8f);
            dm.gravityModifier = 0.7f;
            dm.simulationSpace = ParticleSystemSimulationSpace.World;
            dm.maxParticles = DragMax;
            dm.playOnAwake = false;
            var dsh = dragSpray.shape; dsh.shapeType = ParticleSystemShapeType.Cone; dsh.angle = 30f; dsh.radius = 0.04f;
            var div = dragSpray.inheritVelocity; div.enabled = true; div.mode = ParticleSystemInheritVelocityMode.Initial; div.curve = 0.6f;
            var dsz = dragSpray.sizeOverLifetime; dsz.enabled = true; dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 2.4f));
            var dcol = dragSpray.colorOverLifetime; dcol.enabled = true; dcol.color = g;
            dragEm = dragSpray.emission; dragEm.rateOverTime = 0f;
            Lit(dragSpray, crystalLit);
            dragSpray.Play();
            dragTrail = new SnowTrail(lit);
        }

        /// <summary>The spray's sizes, speeds and weights for the snow underfoot (only when it has changed).</summary>
        void SprayFor(SnowMix s)
        {
            if (sprayMixSet && Mathf.Abs(s.powder - sprayMix.powder) + Mathf.Abs(s.ice - sprayMix.ice) + Mathf.Abs(s.crud - sprayMix.crud) + Mathf.Abs(s.slush - sprayMix.slush) < 0.05f) return;
            sprayMix = s; sprayMixSet = true;
            float powder = s.powder, ice = s.ice, wet = s.slush;
            // crystals: bigger and lighter in powder, fine and quick on ice
            var m = spray.main;
            m.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.03f, 0.06f, powder) * (1f - 0.5f * ice), Mathf.Lerp(0.09f, 0.16f, powder) * (1f - 0.55f * ice));
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f + 2f * ice, 7f + 3f * ice - 1.5f * powder);
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f - 0.2f * ice + 0.2f * powder, 1.0f - 0.45f * ice + 0.5f * powder);
            m.gravityModifier = 0.55f - 0.25f * powder + 0.3f * wet;
            var lv = spray.limitVelocityOverLifetime; lv.drag = 1.6f + 1.4f * powder - 0.8f * ice;
            crystalLit?.SetFloat("_Sparkle", 0.9f + 1.6f * ice);
            // clumps: big in powder, wet and heavy in slush
            var cm = clumps.main;
            cm.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.06f, 0.10f, powder + wet), Mathf.Lerp(0.15f, 0.24f, powder + wet));
            cm.gravityModifier = 1f + 0.3f * wet - 0.25f * powder;
            float puff = 1f + 0.6f * powder + 0.2f * wet - 0.5f * ice;
            var wm = wake.main; wm.startSize = new ParticleSystem.MinMaxCurve(0.4f * puff, 0.9f * puff);
            var bm = bow.main; bm.startSize = new ParticleSystem.MinMaxCurve(0.12f * puff, 0.42f * puff);
        }

        /// <summary>
        /// Once a frame while riding (RiderBody.Apply): where the edge throws from, how much of each spray, the drag.
        /// `at`: the board's contact with the snow.
        /// </summary>
        void SprayFrame(Vector3 at, float dt)
        {
            if (sprayAt == null) return;
            var snow = rider.Underfoot;
            SprayFor(snow);
            bool riding = rider.Running && !rider.Crashed && !rider.Airborne && !rider.Grinding;
            float powder = snow.powder, ice = snow.ice, deep = Mathf.Clamp01(snow.powder + 0.5f * snow.slush), heavy = Mathf.Clamp01(snow.powder * snow.deep + snow.crud + 0.6f * snow.slush);

            // the edge throws its snow out to the outside of the turn and back, from the tail half of the board
            Vector3 v = rider.Velocity; v.y = 0f;
            Vector3 fwd = v.sqrMagnitude > 0.25f ? v.normalized : new Vector3(Mathf.Sin(rider.TravelHeading), 0f, Mathf.Cos(rider.TravelHeading));
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float turn = Mathf.Clamp(rider.Lean / Rider.MaxLean, -1f, 1f);
            Vector3 outside = -right * (Mathf.Abs(turn) > 0.05f ? Mathf.Sign(turn) : 1f);
            sprayAt.position = at + Vector3.up * 0.05f - fwd * 0.35f + outside * 0.18f;
            Vector3 throwDir = (outside * (0.55f + 0.5f * Mathf.Abs(turn)) - fwd * 0.75f + Vector3.up * (0.45f + 0.35f * powder)).normalized;
            sprayAt.rotation = Quaternion.LookRotation(throwDir, Vector3.up);

            // how hard the edge works: its bite, the carve, the skid, and the speed it does it at
            float work = rider.Carve * 6f + rider.Bite * 12f + rider.Skid * 14f;
            float speed = rider.Speed;
            float kind = 0.55f + 2.2f * powder + 0.9f * snow.crud + 0.8f * snow.slush - 0.45f * ice;   // more and lighter in powder, a little puff on the groove, a fine hiss on ice
            sprayEm.rateOverTime = riding && !Tube ? work * speed * kind * 0.75f + Mathf.Max(0f, speed - 22f) * 1.2f + (rider.BoostTime > 0f ? 50f : 0f) : 0f;
            clumpsEm.rateOverTime = riding && !Tube ? (rider.Bite * 3.5f + rider.Skid * 5f) * speed * (0.15f + 1.6f * heavy) * (1f - ice) : 0f;
            bowEm.rateOverTime = riding && speed > 3f && !Tube
                ? speed * (3f + 3.5f * deep) * (1f - 0.75f * ice) * (1f + 1.0f * Mathf.Abs(rider.Lean) / Rider.MaxLean + rider.Skid)
                : 0f;
            wakeEm.rateOverTime = rider.Running && !rider.Crashed && !rider.Airborne ? Mathf.Clamp(speed * 1.6f, 0f, 60f) * (0.55f + 1.2f * powder) * (1f - 0.85f * ice) * (1f - 0.6f * snow.slush) : 0f;

            // the hand (or a pole's tip) dragging: its own spray, and a groove beside the board's
            bool skis = RidingGear && Ride == RideKind.Skis;
            float dragOn = skis ? PoleOnSnow : HandOnSnow;
            Vector3 dragAt = skis ? PoleDragAt : HandDragAt;
            bool dragging = riding && dragOn > 0.5f && speed > 2f;
            if (dragging)
            {
                dragSpray.transform.SetPositionAndRotation(dragAt + Vector3.up * 0.03f, Quaternion.LookRotation((-fwd * 0.8f + Vector3.up * 0.6f + outside * 0.2f).normalized, Vector3.up));
            }
            dragEm.rateOverTime = dragging ? speed * (4f + 6f * powder) * dragOn * (1f - 0.6f * ice) : 0f;
            if (dragTrail != null)
            {
                dragTrail.Mix = snow; dragTrail.Bite = 0.3f;
                dragTrail.Feed(dragAt, normal, rider.TravelHeading, dragging, skis ? 0.16f : 0.32f, 0f, 0f, dt);
            }
        }

        void ClearSpray()
        {
            if (clumps != null) clumps.Clear();
            if (dragSpray != null) dragSpray.Clear();
            dragTrail?.Clear();
        }
    }
}
