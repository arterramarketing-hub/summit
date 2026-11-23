using UnityEngine;

namespace Summit
{
    /// <summary>What the rider rides: a snowboard, skis or a wooden toboggan. Picked at the camp's rack (Progress.Ride); the rider's own copy is Rider.Ride.</summary>
    public enum RideKind { Snowboard, Skis, Toboggan }

    /// <summary>
    /// How one ride feels: a handful of multipliers on the rider's carving model (Rider.cs), so every ride goes
    /// through the same physics and the same upgrades, power-ups and avalanche. The snowboard is all ones: it is
    /// the model as it was tuned, and choosing it changes nothing.
    /// </summary>
    public class RideFeel
    {
        public string Name;
        /// <summary>How fast the board swings onto the line and the body onto its edge (on Rider.SideResponse, SideAccel and LeanSpring). Higher: quicker, twitchier.</summary>
        public float TurnResponse = 1f;
        /// <summary>How far sideways the same drag takes you (on Rider.SideSpeed). Lower: wider, lazier lines.</summary>
        public float TurnRate = 1f;
        /// <summary>How much sideways pull the edges hold before they skid, and how fast a skid is caught (on
        /// Rider.GripLimit and Rider.Grip, after the SHARP EDGES upgrade). Lower: it drifts wide.</summary>
        public float Grip = 1f;
        /// <summary>The speed it runs out to down a steady slope, against the snowboard's (air drag goes with one over its square).</summary>
        public float TopSpeed = 1f;
        /// <summary>How hard a released tuck pops it off the snow (on the pop's speed; height goes with its square).</summary>
        public float Pop = 1f;
        /// <summary>How far steering bends the flight in the air (on Rider.AirControl).</summary>
        public float AirControl = 1f;
        /// <summary>How much of a landing's speed into the snow throws it back up again (0: the legs soak it all up).</summary>
        public float Bounce = 0f;
        /// <summary>Can it slide a rail? (If not it is knocked off the end of one: see Rider.TryGrind.)</summary>
        public bool Rails = true;
        /// <summary>What its grabs are called, and for each the snowboard grabs a daily mission would accept it as.</summary>
        public string[] Grabs, GrabCountsAs;
        /// <summary>What sliding a box and a rail are called on it (the stance it comes on in; the rest are in Rides.GrindName).</summary>
        public string BoxTrick = "50-50", RailTrick = "Boardslide";
        /// <summary>How fast it spins and flips in the air, against the snowboard (on Rider.SpinRate and Rider.FlipRate).</summary>
        public float Spin = 1f, Flip = 1f;
        /// <summary>What a grab stepped up once is called.</summary>
        public string Tweak = "Tweaked";
        /// <summary>Quarter turns a switch-up on a rail takes (a board goes round by quarters: along it, across it), and can it press?</summary>
        public int SwitchStep = 1;
        public bool Presses = true;

        /// <summary>The multiplier on air drag that gives TopSpeed.</summary>
        public float Drag => 1f / (TopSpeed * TopSpeed);
    }

    public static class Rides
    {
        // Kept close together on purpose: the missions' speeds and distances, the scores, the upgrades and the
        // avalanche's chase were all balanced on the snowboard, and every ride has to be able to live with them.
        static readonly RideFeel[] all =
        {
            new RideFeel
            {
                Name = "SNOWBOARD",
                Grabs = new[] { "Indy", "Melon", "Mute", "Method", "Stalefish", "Tail Grab" },
                GrabCountsAs = new[] { "Indy", "Melon", "Mute", "Method", "Stalefish", "Tail Grab" },
            },
            // Skis: two edges and a stance that faces the way you go. Quicker from edge to edge and a touch more
            // hold in a carve, a little faster flat out in a tuck's low stance; but each turn is a wider arc, the
            // pop off two long planks is smaller, and with a pole in each hand there is a little more say in the air.
            new RideFeel
            {
                Name = "SKIS",
                TurnResponse = 1.25f, TurnRate = 0.92f, Grip = 1.08f, TopSpeed = 1.03f, Pop = 0.92f, AirControl = 1.15f,
                Grabs = new[] { "Safety", "Mute", "Tail" },
                GrabCountsAs = new[] { "Indy Stalefish", "Mute Melon", "Method Tail Grab" },
                BoxTrick = "Box Slide", RailTrick = "Rail Slide", Spin = 0.95f, SwitchStep = 2,
            },
            // The toboggan: a plank of wood with no edges and nobody's legs under it. It comes round slowly, and
            // what it is asked to turn it mostly slides (lean gently and it tracks; haul on it and it drifts wide,
            // the dragged heel scrubbing speed). A released tuck is only a hop; it flies off kickers like anything
            // else but there is little to steer with up there, and it lands flat and skips. A touch slower flat
            // out, which the drift more than makes up for in the trees. It slides boxes; rails knock it aside.
            new RideFeel
            {
                Name = "TOBOGGAN",
                TurnResponse = 0.55f, TurnRate = 0.62f, Grip = 0.18f, TopSpeed = 0.95f, Pop = 0.6f, AirControl = 0.45f,
                Bounce = 0.35f, Rails = false,
                Grabs = new[] { "No-Hander" },
                GrabCountsAs = new[] { "Indy Melon Mute Method Stalefish Tail Grab" },
                BoxTrick = "Sled Slide", RailTrick = "Rail Bonk", Spin = 0.7f, Flip = 0.7f, Tweak = "Wild", SwitchStep = 2, Presses = false,
            },
        };

        public static int Count => all.Length;
        public static RideFeel Of(RideKind k) => all[Mathf.Clamp((int)k, 0, all.Length - 1)];

        /// <summary>
        /// What a grind is called: on a rail (or a lift's cable) or a box, with the board a number of quarter turns
        /// off the line (0 along it, 1 across it, 2 along it backwards, 3 across it the other way) and pressed on
        /// its nose (+1) or tail (-1). The stance each feature is come onto in is the ride's own BoxTrick / RailTrick.
        /// </summary>
        public static string GrindName(RideKind ride, bool rail, bool cable, int quarter, int press)
        {
            var f = Of(ride);
            string name;
            if (ride == RideKind.Snowboard)
            {
                bool across = quarter == 1 || quarter == 3;
                name = press > 0 ? (across ? "Noseslide" : "Nose Press") : press < 0 ? (across ? "Tailslide" : "Tail Press")
                     : quarter == 0 ? "50-50" : quarter == 1 ? "Boardslide" : quarter == 2 ? "Switch 50-50" : "Lipslide";
                if (cable) name = quarter == 1 && press == 0 ? "Cable Grind" : "Cable " + name;
                return name;
            }
            // skis and the toboggan turn half way round: the stance come on in, or switch
            bool sw = quarter != (rail ? 1 : 0);
            name = cable ? "Cable Grind" : rail ? f.RailTrick : f.BoxTrick;
            if (press != 0) name = (press > 0 ? "Tip Press" : "Tail Press") + (cable ? " Cable" : "");
            return sw ? "Switch " + name : name;
        }
    }
}
