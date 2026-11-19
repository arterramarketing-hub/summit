using UnityEngine;

namespace Summit
{
    /// <summary>What the rider rides. Picked at the camp's rack (Progress.Ride); the rider's own copy is Rider.Ride.</summary>
    public enum RideKind { Snowboard, Skis }

    /// <summary>
    /// How one ride feels: a handful of multipliers on the rider's carving model (Rider.cs), so every ride goes
    /// through the same physics and the same upgrades, power-ups and avalanche. The snowboard is all ones: it is
    /// the model as it was tuned, and choosing it changes nothing.
    /// </summary>
    public class RideFeel
    {
        public string Name;
        /// <summary>How fast the body gets from one edge to the other (on Rider.LeanSpring). Higher: quicker, twitchier.</summary>
        public float TurnResponse = 1f;
        /// <summary>How tight a turn the same lean draws (on Rider.CarveK and Rider.MaxYawRate). Lower: wider arcs.</summary>
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
        /// <summary>What its grabs are called, and for each the snowboard grabs a daily mission would accept it as.</summary>
        public string[] Grabs, GrabCountsAs;
        /// <summary>What sliding a box and a rail are called on it.</summary>
        public string BoxTrick = "50-50 Box", RailTrick = "Boardslide";

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
                BoxTrick = "Box Slide", RailTrick = "Rail Slide",
            },
        };

        public static int Count => all.Length;
        public static RideFeel Of(RideKind k) => all[Mathf.Clamp((int)k, 0, all.Length - 1)];
    }
}
