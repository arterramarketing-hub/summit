namespace Summit
{
    /// <summary>
    /// Every number that tunes grinding the lift cables, in one place: catching a cable, sliding it, leaving it,
    /// what it scores, and the launch ramps that put a rider within reach. LiftLine places the ramps and works out
    /// what each one reaches from these; CableGrind is the grind itself; SkiLift draws it.
    /// </summary>
    public static class CableTune
    {
        // ---- catching a cable (CableGrind.TryBoard; LiftLine.Reaches uses the same window)
        public const float CatchSide = 1.2f;        // m to either side of the cable that the board still finds it
        public const float CatchAbove = 1.5f;       // m: the board's underside may be this far above the cable
        public const float CatchBelow = 1.0f;       // m: or this far below it (a jump that tops out just short still gets on)
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
        public const float ChairGrace = 1.6f;       // s after leaving a cable (or a ramp, until landing) that chairs cannot hit you

        // ---- what it scores (a 45 m rail is worth 1,275)
        public const int SpanPoints = 400;          // a span begun
        public const float PointsPerMetre = 12f;    // and for each metre slid
        public const int ChainMax = 5;              // each span in a row without touching snow multiplies it, up to this
        public const int ChairPoints = 40;          // a chair's grip skimmed under the board
        public const int BonkPoints = 80;           // a toboggan clattering off the cable
        public const float BonkKeep = 0.8f;         // the share of its speed it keeps

        // ---- launch ramps (LiftLine.PlaceRamp)
        public const float RampLength = 13f;        // m in plan, down the line
        public const float RampHalfWidth = 1.1f;    // m
        public static readonly float[] RampHeights = { 3f, 3.75f, 4.5f, 5.25f };   // m at the lip: the lowest that reaches
        public const float RampWantSpeed = 24f;     // m/s (86 km/h): a ramp is good if this speed into it reaches the cable
        public const float RampMaxSpeed = 30f;      // m/s: no ramp is built that needs more than this
        public const float RampTopSpeed = 40f;      // m/s: and every speed from the least up to this must reach it too
        public const float RampMinSpeed = 8f;       // m/s into it to be carried up it at all
        public const float RampHeadroom = 1.9f;     // m between the lip and the chairs' lowest point: a rider's height
        public const float RampFromTower = 10f;     // m down the line from a tower before a ramp may start
        public const float RampMinSlide = 20f;      // m of cable left to slide where a ramp's flight meets it, at least
        public const float RampOffPiste = 3f;       // m outside the groomed edge, at least
        public const float RampLane = 7f;           // m: half the width cleared of trees from the piste's edge to the ramp
        public const float RampLaneLength = 55f;    // m up the hill that lane starts

        // ---- a bottom station's lane (CableGrind's Deck mode; LiftJump.cs lays it out and works out the flights)
        public const float DeckOver = 0.03f;        // m the lane's top must stand over the snow before the board is on it, not on the snow
        public const float DeckAbove = 1.2f;        // m: a board coming down out of the air is on it from this far above its top
        public const float DeckBelow = 1.0f;        // m: or this far below (the hollow where the snow turns into the deck)
        public const float DeckSteer = 3.5f;        // m/s across the lane at full steer
        public const float DeckCatch = 1.5f;        // m outside either kerb that a board is still caught onto the lane
        public const float DeckCatchOver = 0.35f;   // m: only where the kerb stands no higher than this over the snow
        public const float DeckAlign = 0.3f;        // the board must be going down the lane within about 70 degrees of its line
    }
}
