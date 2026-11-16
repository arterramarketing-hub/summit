using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The rider: physics on the height field plus a simple built-from-shapes body.
    /// Heading 0 is straight down the fall line (+Z); positive heading turns right (+X).
    /// The board always travels where it points, so the model can never be drawn
    /// sideways to its motion.
    /// </summary>
    public class Rider : MonoBehaviour
    {
        public const float GroundAcc = 30f;    // gravity along the slope, tuned for feel
        public const float AirGravity = 26f;
        const float MaxHeading = 1.45f;        // about 83 degrees off the fall line
        const float Tau = Mathf.PI * 2f;

        public RiderInput input;

        // state
        public float X, Z, Y, Heading, Speed, Vy;
        public bool Airborne, Crashed, Running;
        public float Steer, AirTime, Spin, StartZ;
        public float Distance => Mathf.Max(0f, Z - StartZ);
        public float AboveGround { get; private set; }
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;

        public System.Action<string, int> Trick;   // name, points
        public System.Action<string> Crash;        // reason
        public System.Action<float> Landed;
        public System.Action<string> RoughLanding;        // impact

        float vyGround, crashTimer, lean, tuck, peak, spinVis;
        int lastMissId;
        Vector3 crashAxis;
        Transform pivot, roll, upper, trailAnchor;
        TrailRenderer trail;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEm;

        // ---------------------------------------------------------------- build

        public void Build(Material jacket, Material pants, Material board, Material skin, Material dark,
                          Material trailMat, Material sprayMat)
        {
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);
            roll = new GameObject("Roll").transform;
            roll.SetParent(pivot, false);

            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.03f, 0f), new Vector3(0.30f, 0.04f, 1.55f), Vector3.zero, board);
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.03f, 0.80f), new Vector3(0.28f, 0.04f, 0.12f), new Vector3(-18f, 0f, 0f), board);
            Part(PrimitiveType.Cube, roll, new Vector3(0f, 0.03f, -0.80f), new Vector3(0.28f, 0.04f, 0.12f), new Vector3(18f, 0f, 0f), board);

            // the rider stands across the board and faces +X (toe side)
            upper = new GameObject("Body").transform;
            upper.SetParent(roll, false);
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 0.42f, 0.24f), new Vector3(0.17f, 0.38f, 0.17f), new Vector3(0f, 0f, 0f), pants);
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 0.42f, -0.24f), new Vector3(0.17f, 0.38f, 0.17f), new Vector3(0f, 0f, 0f), pants);
            Part(PrimitiveType.Capsule, upper, new Vector3(0f, 1.02f, 0f), new Vector3(0.28f, 0.36f, 0.40f), Vector3.zero, jacket);
            Part(PrimitiveType.Capsule, upper, new Vector3(0.04f, 1.12f, 0.40f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(70f, 0f, 0f), jacket);
            Part(PrimitiveType.Capsule, upper, new Vector3(0.04f, 1.12f, -0.40f), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(-70f, 0f, 0f), jacket);
            Part(PrimitiveType.Sphere, upper, new Vector3(0f, 1.54f, 0f), new Vector3(0.27f, 0.29f, 0.27f), Vector3.zero, skin);
            Part(PrimitiveType.Sphere, upper, new Vector3(-0.01f, 1.60f, 0f), new Vector3(0.29f, 0.24f, 0.29f), Vector3.zero, jacket);
            Part(PrimitiveType.Cube, upper, new Vector3(0.12f, 1.55f, 0f), new Vector3(0.07f, 0.08f, 0.22f), Vector3.zero, dark);

            // a flat ribbon that lies on the snow behind the board
            trailAnchor = new GameObject("Trail").transform;
            trailAnchor.SetParent(transform, false);
            trail = trailAnchor.gameObject.AddComponent<TrailRenderer>();
            trail.alignment = LineAlignment.TransformZ;
            trail.time = 7f;
            trail.minVertexDistance = 0.5f;
            trail.widthMultiplier = 0.34f;
            trail.material = trailMat;
            trail.startColor = new Color(0.78f, 0.84f, 0.96f, 0.85f);
            trail.endColor = new Color(0.84f, 0.89f, 0.98f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.numCapVertices = 0;

            // powder thrown off the edge
            var sg = new GameObject("Spray");
            sg.transform.SetParent(pivot, false);
            sg.transform.localPosition = new Vector3(0f, 0.08f, -0.5f);
            sg.transform.localRotation = Quaternion.Euler(-30f, 180f, 0f);
            spray = sg.AddComponent<ParticleSystem>();
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.45f);
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.15f;
            var col = spray.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = spray.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            sprayEm = spray.emission;
            sprayEm.rateOverTime = 0f;
            var pr = sg.GetComponent<ParticleSystemRenderer>();
            pr.material = sprayMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ---------------------------------------------------------------- lifecycle

        public void ResetAt(float x, float z)
        {
            X = x; Z = z; StartZ = z;
            Y = MountainField.Height(x, z);
            Heading = 0f; Speed = 0f; Vy = 0f; vyGround = 0f;
            Airborne = false; Crashed = false; Running = false;
            AirTime = 0f; Spin = 0f; spinVis = 0f; lean = 0f; tuck = 0f;
            pivot.localRotation = Quaternion.identity;
            roll.localRotation = Quaternion.identity;
            SurfaceNormal = MountainField.Normal(x, z);
            UpdateVisual(0f);
            trail.Clear();
            spray.Clear();
        }

        public void Begin()
        {
            Running = true;
            Speed = 5f;
            Vector2 g = MountainField.Gradient(X, Z);
            vyGround = g.y * Speed;   // heading is 0, so the board points along +Z
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            if (Running)
            {
                int n = Mathf.CeilToInt(dt / (1f / 120f));
                float h = dt / n;
                for (int i = 0; i < n; i++) Step(h, i == 0);
            }
            UpdateVisual(dt);
        }

        // ---------------------------------------------------------------- physics

        void Step(float dt, bool firstSub)
        {
            if (Crashed)
            {
                crashTimer -= dt;
                Speed = Mathf.MoveTowards(Speed, 0f, 22f * dt);
                X += Mathf.Sin(Heading) * Speed * dt;
                Z += Mathf.Cos(Heading) * Speed * dt;
                Y = MountainField.Height(X, Z);
                if (crashTimer <= 0f) Recover();
                return;
            }

            Steer = input.Steer;
            bool holding = input.Hold;

            if (!Airborne)
            {
                Ground(dt, holding);
                if (firstSub && input.Released && input.LastHold > 0.08f) Pop(input.LastHold);
            }
            else Air(dt);

            // trees: low enough to hit them, close enough to scrape them
            if (AboveGround < 3.5f)
            {
                float d = PropField.Nearest(X, Z, out int id, out float s);
                if (d < 0.75f * s) { DoCrash("Tree"); return; }
                if (d < 2.4f * s && Speed > 14f && id != lastMissId && !Airborne)
                {
                    lastMissId = id;
                    Trick?.Invoke("Close call", 50);
                }
            }
        }

        void Ground(float dt, bool holding)
        {
            tuck = Mathf.MoveTowards(tuck, holding ? 1f : 0f, dt * 5f);
            var f = new Vector2(Mathf.Sin(Heading), Mathf.Cos(Heading));
            Vector2 g = MountainField.Gradient(X, Z);
            float slope = -(g.x * f.x + g.y * f.y);   // positive when the board points downhill
            float acc = GroundAcc * slope / Mathf.Sqrt(1f + slope * slope);
            float drag = Mathf.Lerp(0.0105f, 0.0062f, tuck);
            acc -= drag * Speed * Speed + 0.35f;
            acc -= Mathf.Abs(Steer) * Speed * Mathf.Lerp(0.07f, 0.11f, tuck);   // carving bleeds speed
            Speed = Mathf.Max(0f, Speed + acc * dt);
            // never stuck: below a jog you skate, even up the face of a roller
            if (Speed < 4f) Speed = Mathf.Min(4f, Speed + 6f * dt);

            float turn = Mathf.Lerp(2.4f, 1.15f, Mathf.Clamp01(Speed / 38f)) * Mathf.Lerp(1f, 0.55f, tuck);
            Heading += Steer * turn * dt;
            // stalled across the hill: the board swings back down on its own
            if (Speed < 3f) Heading = Mathf.MoveTowards(Heading, 0f, dt * 1.4f * (1f - Speed / 3f));
            Heading = Mathf.Clamp(Heading, -MaxHeading, MaxHeading);

            X += Mathf.Sin(Heading) * Speed * dt;
            Z += Mathf.Cos(Heading) * Speed * dt;
            float ny = MountainField.Height(X, Z);
            float vy = (ny - Y) / dt;
            // the ground falls away faster than gravity can follow: take off
            if (Speed > 8f && vy < vyGround - AirGravity * dt)
            {
                Airborne = true;
                Vy = vyGround;
                AirTime = 0f; Spin = 0f; peak = 0f;
                Y += Vy * dt;
                return;
            }
            Y = ny;
            vyGround = vy;
            AboveGround = 0f;
        }

        void Pop(float held)
        {
            Airborne = true;
            Vy = vyGround + Mathf.Lerp(5f, 7.5f, Mathf.Clamp01(held / 0.8f));
            AirTime = 0f; Spin = 0f; peak = 0f;
        }

        void Air(float dt)
        {
            tuck = Mathf.MoveTowards(tuck, 0.3f, dt * 3f);
            AirTime += dt;
            Vy -= AirGravity * dt;
            Y += Vy * dt;
            Speed = Mathf.Max(0f, Speed - 0.002f * Speed * Speed * dt);
            X += Mathf.Sin(Heading) * Speed * dt;
            Z += Mathf.Cos(Heading) * Speed * dt;
            // steering in the air spins; a short grace stops small hops from rotating you
            if (AirTime > 0.18f) Spin += Steer * 9f * dt;
            float gy = MountainField.Height(X, Z);
            AboveGround = Y - gy;
            peak = Mathf.Max(peak, AboveGround);
            if (Y <= gy) Land(gy);
        }

        void Land(float gy)
        {
            var f = new Vector2(Mathf.Sin(Heading), Mathf.Cos(Heading));
            Vector2 g = MountainField.Gradient(X, Z);
            float groundVy = (g.x * f.x + g.y * f.y) * Speed;
            float impact = groundVy - Vy;   // how much faster you are falling than the slope
            Airborne = false;
            Y = gy;
            vyGround = groundVy;
            AboveGround = 0f;

            float err = Spin - Tau * Mathf.Round(Spin / Tau);
            int turns = Mathf.RoundToInt(Mathf.Abs(Spin) / Tau);
            bool real = peak > 0.8f;
            // bad landings no longer wipe you out: you keep riding, lose speed and skip the trick points
            bool sloppy = real && Mathf.Abs(err) > 0.75f;
            bool hard = impact > 26f;
            if (sloppy) Speed *= 0.88f;
            if (hard) Speed *= 0.9f;
            if (sloppy || hard)
            {
                // keep riding the way you were pointed; just lose a little speed and settle the body
                spinVis = 0f; Spin = 0f;
                Landed?.Invoke(impact);
                spray.Emit(40);
                RoughLanding?.Invoke(sloppy ? "Sloppy landing" : "Hard landing");
                return;
            }

            spinVis = real ? err : Spin;   // fold the small leftover into the picture, not the run
            Spin = 0f;
            Speed = Mathf.Max(0f, Speed - Mathf.Max(0f, impact - 10f) * 0.3f);
            Landed?.Invoke(impact);
            if (impact > 6f) spray.Emit(Mathf.Clamp((int)(impact * 2f), 6, 40));

            if (!real) return;
            if (turns > 0) Trick?.Invoke((turns * 360) + (turns > 2 ? "!" : ""), 400 * turns * turns);
            if (peak > 6f) Trick?.Invoke("Big air", 300);
            else if (AirTime > 0.5f) Trick?.Invoke("Air " + AirTime.ToString("0.0") + "s", Mathf.RoundToInt(AirTime * 120f));
        }

        void DoCrash(string why)
        {
            Crashed = true;
            Airborne = false;
            crashTimer = 1.5f;
            crashAxis = new Vector3(Random.Range(-1f, 1f), 0.2f, Random.Range(0.4f, 1f)).normalized;
            Y = MountainField.Height(X, Z);
            trail.emitting = false;
            spray.Emit(60);
            Crash?.Invoke(why);
        }

        void Recover()
        {
            Crashed = false;
            Heading = 0f;
            Speed = 3f;
            Spin = 0f; spinVis = 0f;
            // step clear of whatever stopped you
            float d = PropField.Nearest(X, Z, out _, out float s);
            if (d < 2.5f * s) X += Mathf.Sign(MountainField.DriftX(Z) - X + 0.01f) * 3f;
            Y = MountainField.Height(X, Z);
            vyGround = 0f;
            pivot.localRotation = Quaternion.identity;
        }

        // ---------------------------------------------------------------- visuals

        void UpdateVisual(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 12f);
            float vis = Airborne ? Y : MountainField.DrawnHeight(X, Z);
            transform.position = new Vector3(X, vis, Z);

            Vector3 n = Airborne ? Vector3.up : MountainField.Normal(X, Z);
            SurfaceNormal = dt <= 0f ? n : Vector3.Slerp(SurfaceNormal, n, Airborne ? k * 0.25f : k);

            if (Crashed)
            {
                pivot.Rotate(crashAxis, 520f * dt * Mathf.Clamp01(crashTimer), Space.World);
            }
            else
            {
                spinVis = Mathf.Lerp(spinVis, 0f, 1f - Mathf.Exp(-dt * 10f));
                float yaw = Heading + Spin + spinVis;
                var f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 fp = Vector3.ProjectOnPlane(f, SurfaceNormal);
                if (fp.sqrMagnitude > 1e-4f) pivot.rotation = Quaternion.LookRotation(fp.normalized, SurfaceNormal);

                // lean into the turn; positive steer turns right, so roll right
                float want = Airborne ? 0f : Steer * Mathf.Clamp01(Speed / 12f) * 38f;
                lean = Mathf.Lerp(lean, want, 1f - Mathf.Exp(-dt * 8f));
                roll.localRotation = Quaternion.Euler(0f, 0f, -lean * 0.45f);
                upper.localRotation = Quaternion.Euler(0f, 0f, -lean * 0.55f);
                upper.localScale = new Vector3(1f, 1f - 0.24f * tuck, 1f);
            }

            // trail lies flat on the snow a hair above it
            var tf = new Vector3(Mathf.Sin(Heading), 0f, Mathf.Cos(Heading));
            trailAnchor.position = transform.position + SurfaceNormal * 0.04f;
            trailAnchor.rotation = Quaternion.LookRotation(SurfaceNormal, Vector3.ProjectOnPlane(tf, SurfaceNormal) + Vector3.forward * 1e-4f);
            trail.emitting = Running && !Crashed && AboveGround < 0.15f;

            sprayEm.rateOverTime = Running && !Crashed && !Airborne
                ? Mathf.Abs(Steer) * Speed * 6f + Mathf.Max(0f, Speed - 22f) * 2f
                : 0f;
        }
    }
}
