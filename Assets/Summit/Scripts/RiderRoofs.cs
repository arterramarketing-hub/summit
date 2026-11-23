using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The ground under the board, roofs and all: a cabin's roof (Roofs) where the board is over one and not under it,
    /// the mountain's drawn snow everywhere else. Rider's ground and landing code asks here instead of Slope, so a roof
    /// is landed on, ridden and left by the same rules as the snow.
    /// </summary>
    public partial class Rider
    {
        /// <summary>Riding on a roof right now (null on the snow, in the air, on a rail or down).</summary>
        public Roofs.Roof OnRoof => !Airborne && !Crashed && !Grinding ? roofUnder : null;
        Roofs.Roof roofUnder, roofScored;
        /// <summary>A roof landed on (or ridden onto) pays this, once a roof, as a link in the combo.</summary>
        public static int RoofPoints = 150;

        /// <summary>The ground's height under (x, z) for a board whose centre is at refY, and its normal.</summary>
        float GroundAt(float x, float z, float refY, out Vector3 n, out Roofs.Roof roof)
        {
            if (Roofs.Under(x, z, refY, out float y, out n, out roof)) return y;
            float snow = Slope.DrawnHeight(x, z);
            if (OverGround(x, z, snow, out y, out n)) return y;   // a rock, a stump, a block low enough to ride (RiderRideOver)
            n = Slope.Normal(x, z);
            return snow;
        }

        float GroundAt(float x, float z, float refY) => GroundAt(x, z, refY, out _, out _);

        /// <summary>Which way the ground under the board faces (a roof's pitch, a rock's top, or the snow's): for drawing the board on it.</summary>
        public Vector3 GroundNormal(Vector3 p)
        {
            if (Roofs.Under(p.x, p.z, p.y, out _, out Vector3 n, out _)) return n;
            if (OverGround(p.x, p.z, Slope.DrawnHeight(p.x, p.z), out _, out n)) return n;
            return Slope.Normal(p.x, p.z);
        }

        /// <summary>Each step on the ground: which roof (if any) the board is on, and the points for getting up there.</summary>
        void RoofStep(Roofs.Roof roof)
        {
            roofUnder = roof;
            if (roof == null) { roofScored = null; return; }
            if (roof == roofScored) return;
            roofScored = roof;
            if (roof.s.points != 0) Score(roof.s.label, roof.s.points < 0 ? RoofPoints : roof.s.points, LinkKind.Bonus);
        }

        void RoofReset() { roofUnder = null; roofScored = null; }

        /// <summary>
        /// In the air over a roof, coming down to it this step, but the step's end is past its edge (no roof there): the
        /// board comes down on the edge instead of sweeping on past it, through the eave, into the solid under it.
        /// </summary>
        void RoofEdge(Vector3 p, ref float gy, ref Vector3 n, ref Roofs.Roof roof)
        {
            if (!Roofs.Under(p.x, p.z, p.y, out float y, out Vector3 rn, out var here) || here == roof) return;
            float at = y + R + Lift;
            if (at <= gy) return;
            gy = at; n = rn; roof = here;
        }

        /// <summary>Would coming down on a ridge at this velocity be a slam (as Land judges one)? Then it is not slid but
        /// landed on, and slammed.</summary>
        bool RidgeSlam(Vector3 v)
        {
            if (!Airborne) return false;
            float impact = Mathf.Max(0f, -v.y), along = new Vector2(v.x, v.z).magnitude;
            if (superAir) impact = Mathf.Min(impact, 11f + 0.25f * Mathf.Max(0f, impact - 11f));
            return impact > SlamImpact && Mathf.Atan2(impact, along) * Mathf.Rad2Deg > MaxLandAngle;
        }
    }
}
