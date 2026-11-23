namespace Summit
{
    /// <summary>
    /// Every number that tunes grinding the lift cables, in one place: catching a cable, sliding it, leaving it,
    /// what it scores, and the hills under the lines that put a rider within reach. LiftLine places the hills and
    /// works out what each one reaches from these (LiftHill.cs); CableGrind is the grind itself; SkiLift draws it.
    /// </summary>
    public static class CableTune
    {
        // ---- catching a cable (CableGrind.TryBoard; LiftLine.Reaches uses the same window)
        public const float CatchSide = 1.2f;        // m to either side of the cable that the board still finds it
        public const float CatchAbove = 1.5f;       // m: the board's underside may be this far above the cable
        public const float CatchBelow = 1.5f;       // m: or this far below it (a jump that tops out just short still gets on: eased up onto it)
        public const float CatchAlign = 0.707f;     // the flight must be within 45 degrees of the line, going down it
        public const float CatchMinSpeed = 6f;      // m/s along the cable, at least
        public const float CatchMaxRise = 9f;       // m/s: rising through the cable faster than this, you go on past it
        public const float ClearTower = 3f;         // m below a tower before its span can be boarded (clear of the head)
        public const float ClearNext = 10f;         // m: and no nearer than this to the tower the span ends at

        // ---- sliding it (CableGrind.Step)
        public const float G = 22f;                 // Rider.G: the game's gravity
        public const float BoardR = 0.3f;           // Rider.R: from the rider's position down to the board's underside
        public const float Friction = 0.04f;        // of G: steel on a greased rope (a rail is 0.06)
        public const float Drag = 0.0078f;          // air drag per (m/s)^2, as riding upright (Rider.DragOpen): about 140 km/h at most
        public const float MinSpeed = 4f;           // m/s: it never quite stops on the rope
        public const float DipDepth = 0.45f;        // m the cable sags further under the rider at mid-span
        public const float DipBounce = 0.3f;        // m of bounce when the rider lands on it, dying away in a second

        // ---- leaving it
        public const float HopLead = 0.28f;         // s before the tower that the rider is thrown off
        public const float TowerHop = 9.5f;         // m/s upward at the tower: clears the crossarm and the sheaves
        public const float TowerNudge = 2.5f;       // m/s outward at the tower: the next span has to be steered back onto
        public const float PopHop = 6.5f;           // m/s upward for a pop off the cable mid-span (doubled by a Super Jump)
        public const float ChairGrace = 1.6f;       // s after leaving a cable (or a lift's hill, until landing) that chairs cannot hit you

        // ---- what it scores (a 45 m rail is worth 1,275)
        public const int SpanPoints = 400;          // a span begun
        public const float PointsPerMetre = 12f;    // and for each metre slid
        public const int ChainMax = 5;              // each span in a row without touching snow multiplies it, up to this
        public const int ChairPoints = 40;          // a chair's grip skimmed under the board
        public const int BonkPoints = 80;           // a toboggan clattering off the cable
        public const float BonkKeep = 0.8f;         // the share of its speed it keeps

        // ---- the hills under the lines (LiftHill.cs: LiftLine.PlaceHill)
        public static readonly float[] HillHeights = { 1.5f, 2f, 2.5f, 3f, 3.5f, 4f, 4.5f, 5f, 5.5f, 6f, 7f, 8f, 9f, 10f };   // m over the slope at its lip: the lowest that reaches
        public static readonly float[] HillLips = { 0.3f, 0.5f, 0.7f, 0.9f };     // its face's slope at the lip (per m over the slope's own): the gentlest that reaches
        public const float HillRiseK = 2.4f;        // its face's length in plan: this times its height over its lip slope
        public const float HillRiseMin = 10f;       // m, at least
        public const float HillFallMin = 12f;       // m in plan from the lip, over the crest, back down to the slope's own line, at least (or 3 times its height)
        public const float HillTop = 2.5f;          // m either side of its middle line that it stands at full height
        public const float HillBlendMin = 3f;       // m beyond that over which it eases into the slope: this,
        public const float HillBlendK = 1.0f;       // and this many metres for each metre of its height,
        public const float HillBlendMax = 10f;      // up to this
        public const float HillEdge = 0.3f;         // m: its snow lower than this over the slope (its faint edges) may come onto the piste,
        public const float HillEdgeOnPiste = 8f;    // m in from the groomed edge at most;
        public const float HillRunUp = 1.5f;        // and its run-up while it is lower than this (a gentle swell to aim at),
        public const float HillRunUpOnPiste = 4f;   // m in from the groomed edge at most
        public const float HillSpeedKeep = 0.92f;   // the share of the model's speed over the crest that the game's rider keeps,
        public const float HillRiseLoss = 1.5f;     // and the m/s less it rises leaving it (LiftHillCheck, with main's 9f67fee steering)
        public const float HillCatchMargin = 0.4f;  // m inside the catch window that its flight must pass, worked out (the rider's own line is never quite it)
        public const float HillWantSpeed = 18f;     // m/s (65 km/h): a hill is as good as can be if this speed into it reaches the cable
        public const float HillTypical = 22f;       // m/s (79 km/h): every hill reaches the cable from this speed, the speed most riders bring
        public const float HillMaxSpeed = 30f;      // m/s: no hill is made that needs more than this
        public const float HillTopSpeed = 34f;      // m/s (122 km/h): and most speeds from the least up to this must reach it too:
        public const float HillShare = 0.6f;        // this share of them, at least
        public const float HillHeadroom = 1.9f;     // m between its snow and the chairs' lowest point, everywhere: a rider's height
        public const float HillReachMax = 9f;       // m: no crest is made further under the cable than this (out of reach at the typical speed)
        public const float HillStep = 3f;           // m between the spots tried for its lip along a span
        public const float HillFromTower = 6f;      // m from any tower's foot to the nearest of its snow
        public const float HillMinSlide = 20f;      // m of cable left to slide where its flight meets it, at least
        public const float HillOffPiste = 1f;       // m outside the groomed edge, all of it that stands over HillRunUp
        public const float HillLane = 7f;           // m: half the width cleared of trees from the piste's edge to it
        public const float HillLaneLength = 55f;    // m up the hill that lane starts

    }
}
