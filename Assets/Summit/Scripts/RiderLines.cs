using UnityEngine;

namespace Summit
{
    /// <summary>
    /// A feature line ridden as a line (Lines): every hit landed (or a jib slid) after the one before it in the same
    /// line, with no more than LineHold on the snow between, is a link of its own ("Line x2", "Line x3": a new name
    /// each, so each raises the combo's multiplier), and the whole line clean is "Full Line!". While a chain is going
    /// the combo's window is held (SummitGame: InLineFlow), so a line ridden clean is one combo however long it is.
    /// </summary>
    public partial class Rider
    {
        /// <summary>The longest gap on the snow between two of a line's hits that still chains them (s).</summary>
        public const float LineHold = 2.8f;
        public const int LinePoints = 40, FullLinePoints = 150, FullLinePerHit = 50;

        int lnLine = -1, lnLast = -1, lnCount, lnFrom = -1;
        float lnSnow, lnZ = float.MaxValue;
        bool lnUp;

        /// <summary>Riding a line's chain: a hit of it landed and the next not yet missed.</summary>
        public bool InLineFlow => lnLine >= 0 && lnSnow < LineHold && !Crashed;
        /// <summary>How many hits of the line in hand have been chained (0: none).</summary>
        public int LineChain => lnLine >= 0 ? lnCount : 0;
        /// <summary>A hit of a line was landed: its line's id, its index, the chain's length now.</summary>
        public System.Action<int, int, int> LineHit;

        void LineReset() { lnLine = -1; lnLast = -1; lnCount = 0; lnFrom = -1; lnSnow = 0f; lnUp = false; lnZ = float.MaxValue; }

        void LineWatch(float dt)
        {
            if (Crashed || !Running) { if (lnLine >= 0 || lnFrom >= 0) LineReset(); return; }
            bool up = Airborne || Grinding;
            if (up && !lnUp)
            {
                // off the snow: off one of a line's hits?
                Vector3 p = transform.position;
                lnFrom = Grinding ? Lines.HitAt(p, GrindFeature.id) : Lines.HitAt(DropFrom);
            }
            else if (!up && lnUp)
            {
                if (lnFrom >= 0) Credit(lnFrom);
                lnFrom = -1;
            }
            // (a roller counts as it is ridden over, flown or not)
            int rolled = Lines.RollerPassed(transform.position, lnZ);
            lnZ = transform.position.z;
            if (rolled >= 0) Credit(rolled);
            if (!up)
            {
                lnSnow += dt;
                if (lnLine >= 0 && lnSnow > LineHold) { lnLine = -1; lnLast = -1; lnCount = 0; }
            }
            lnUp = up;
        }

        void Credit(int key)
        {
            int line = key / 32, i = key % 32;
            // (the same hit again: a roller's next crest, a hop on a landing. It keeps the chain going, no more)
            if (line == lnLine && i == lnLast) { lnSnow = 0f; return; }
            if (line == lnLine && i > lnLast) lnCount++;
            else { lnLine = line; lnCount = 1; }
            lnLast = i; lnSnow = 0f;
            LineHit?.Invoke(line, i, lnCount);
            if (lnCount >= 2) Score("Line x" + lnCount, LinePoints * lnCount, LinkKind.Bonus);
            int n = Lines.CountOf(line);
            if (n > 0 && i == n - 1 && lnCount == n) Score("Full Line!", FullLinePoints + FullLinePerHit * n, LinkKind.Bonus);
        }
    }
}
