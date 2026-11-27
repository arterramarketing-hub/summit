using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the rider rides: a snowboard, skis or a wooden toboggan; or, for fun (rides), a kayak, a rally car or a bear.
    /// Picked in the closet's RIDE list or the pause menu's RIDE row (Progress.Ride); the rider's own copy is Rider.Ride.
    /// (Saved by number: new ones go on the end.)
    /// </summary>
    public enum RideKind { Snowboard, Skis, Toboggan, Kayak, Car, Bear }

    /// <summary>
    /// How one ride feels: a handful of multipliers on the rider's carving model (RiderMove.cs), so every ride goes
    /// through the same physics and the same upgrades, power-ups and avalanche. The snowboard is all ones: it is
    /// the model as it was tuned, and choosing it changes nothing.
    /// </summary>
    public class RideFeel
    {
        public string Name;
        /// <summary>How soon it answers the finger's push (one over Rider.PushTime). Higher: quicker from side to side.</summary>
        public float TurnResponse = 1f;
        /// <summary>How far sideways a whole drag takes it (on Rider.SideShare). Lower: lazier lines.</summary>
        public float TurnRate = 1f;
        /// <summary>How soon a slide dies away once the finger lets go (one over Rider.EaseTime, by its root).
        /// Lower: it slides on longer.</summary>
        public float Grip = 1f;
        /// <summary>The speed it runs out to down a steady slope, against the snowboard's (air drag goes with one over its square).</summary>
        public float TopSpeed = 1f;
        /// <summary>How hard a released tuck pops it off the snow (on the pop's speed; height goes with its square).</summary>
        public float Pop = 1f;
        /// <summary>How far the finger drifts the flight in the air (on Rider.AirDrift).</summary>
        public float AirControl = 1f;
        /// <summary>How much of a landing's speed into the snow throws it back up again (0: the legs soak it all up).</summary>
        public float Bounce = 0f;
        /// <summary>Can it slide a rail? (If not it is knocked off the end of one: see Rider.TryGrind.)</summary>
        public bool Rails = true;
        /// <summary>What its grabs are called, and for each the snowboard grabs a daily mission would accept it as.</summary>
        public string[] Grabs, GrabCountsAs;
        /// <summary>What each grab adds on top of the grab's own points: the harder and the more distinct a shape, the more.</summary>
        public int[] GrabBonus;
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

        /// <summary>The fun rides (rides): one that is not a board, skis or a toboggan, with its own extra physics
        /// (RiderVehicle.cs), body and clips (RiderBodyVehicle.cs) and sounds (VehicleSound.cs).</summary>
        public bool Vehicle;
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
                Grabs = new[] { "Indy", "Melon", "Mute", "Method", "Stalefish", "Tail Grab", "Nose Grab", "Japan", "Roast Beef", "Crail", "Truck Driver" },
                GrabCountsAs = new[] { "Indy", "Melon", "Mute", "Method", "Stalefish", "Tail Grab", "Nose Grab", "Japan Mute", "Roast Beef", "Crail", "Truck Driver Indy Melon" },
                GrabBonus = new[] { 0, 0, 0, 50, 50, 0, 0, 100, 100, 75, 150 },
            },
            // Skis: two edges and a stance that faces the way you go. Quicker from side to side, a little further
            // across for the same drag and quicker to stop sliding, a little faster flat out in a tuck's low stance;
            // the pop off two long planks is smaller, and with a pole in each hand there is a little more say in
            // the air.
            new RideFeel
            {
                Name = "SKIS",
                TurnResponse = 1.25f, TurnRate = 1.08f, Grip = 1.18f, TopSpeed = 1.03f, Pop = 0.92f, AirControl = 1.15f,
                // (the old school's no-hand shapes ride in the grabs' place: a tap is a daffy as readily as a safety)
                Grabs = new[] { "Safety", "Mute", "Tail", "Japan", "Octograb", "Daffy", "Iron Cross", "Spread Eagle" },
                GrabCountsAs = new[] { "Indy Stalefish", "Mute Melon", "Method Tail Grab", "Japan Mute", "Indy Melon", "Method", "Stalefish", "Tail Grab" },
                GrabBonus = new[] { 0, 0, 0, 75, 150, 50, 100, 50 },
                BoxTrick = "Box Slide", RailTrick = "Rail Slide", Spin = 0.95f, SwitchStep = 2,
            },
            // The toboggan: a plank of wood with no edges and nobody's legs under it. It is mostly gravity's: it
            // answers a push late, goes less far across for it, and slides on a good while after. A released tuck is only a hop; it flies off kickers like anything
            // else but there is little to steer with up there, and it lands flat and skips. A touch slower flat
            // out, which the drift more than makes up for in the trees. It slides boxes; rails knock it aside.
            new RideFeel
            {
                Name = "TOBOGGAN",
                TurnResponse = 0.55f, TurnRate = 0.62f, Grip = 0.4f, TopSpeed = 0.95f, Pop = 0.6f, AirControl = 0.45f,
                Bounce = 0.35f, Rails = false,
                Grabs = new[] { "No-Hander", "Superman", "Stand-Up" },
                GrabCountsAs = new[] { "Indy Melon Mute Method Stalefish Tail Grab", "Indy Melon Mute Method Stalefish Tail Grab", "Indy Melon Mute Method Stalefish Tail Grab" },
                GrabBonus = new[] { 0, 150, 200 },
                BoxTrick = "Sled Slide", RailTrick = "Rail Bonk", Spin = 0.7f, Flip = 0.7f, Tweak = "Wild", SwitchStep = 2, Presses = false,
            },
            // The kayak: a plastic hull on snow, no edges at all. It slides on its hull (it slides on and on, and
            // answers a push late), the paddle does the rest: each stroke a little push on and round (RiderVehicle:
            // the game strokes on the outside of a turn, and to get going), the inside blade dragged as a rudder. Turned
            // too hard it spins out, round on its own middle, and slides on backwards a moment before it comes round.
            // A bit quicker than the toboggan flat out (a long, smooth hull), a hop for a jump, a flat landing that skips.
            new RideFeel
            {
                Name = "KAYAK", Vehicle = true,
                TurnResponse = 0.5f, TurnRate = 0.72f, Grip = 0.3f, TopSpeed = 1.0f, Pop = 0.62f, AirControl = 0.45f,
                Bounce = 0.28f, Rails = false,
                Grabs = new[] { "Paddle Up" },
                GrabBonus = new[] { 100 },
                GrabCountsAs = new[] { "Indy Melon Mute Method Stalefish Tail Grab" },
                BoxTrick = "Hull Slide", RailTrick = "Rail Bonk", Spin = 0.8f, Flip = 0.6f, Tweak = "Wild", SwitchStep = 2, Presses = false,
            },
            // The rally car: four wheels on snow. It drifts (little grip, a long slide) but a push answers soon enough,
            // and it is the quickest thing on the mountain flat out; the engine keeps it going where the hill gives up
            // (RiderVehicle). It bounces on its springs off every landing and leans in a hard turn, but never goes over.
            // It is built to take a knock: a tree only dents it (the third throws you out), a rock throws you out, and
            // the people and animals in its way go flying (rides2). A jump is the springs' hop; little steering in the air.
            new RideFeel
            {
                Name = "RALLY CAR", Vehicle = true,
                TurnResponse = 0.85f, TurnRate = 0.85f, Grip = 0.45f, TopSpeed = 1.07f, Pop = 0.55f, AirControl = 0.3f,
                Bounce = 0.18f, Rails = false,
                Grabs = new[] { "Window Wave" },
                GrabBonus = new[] { 100 },
                GrabCountsAs = new[] { "Indy Melon Mute Method Stalefish Tail Grab" },
                BoxTrick = "Box Drift", RailTrick = "Rail Bonk", Spin = 0.55f, Flip = 0.5f, Tweak = "Wild", SwitchStep = 2, Presses = false,
            },
            // The bear: four legs and claws. It grips like skis and answers at once, a little slower flat out than a
            // board, but it runs on by itself where the hill flattens (RiderVehicle) and it jumps big. Its gallop bounds
            // the rider up and down. It cannot balance on a rail; a slam throws you off (and the bear runs on and stops).
            new RideFeel
            {
                Name = "BEAR", Vehicle = true,
                TurnResponse = 1.15f, TurnRate = 1.0f, Grip = 1.15f, TopSpeed = 0.93f, Pop = 1.2f, AirControl = 0.8f,
                Rails = false,
                Grabs = new[] { "Rodeo" },
                GrabBonus = new[] { 150 },
                GrabCountsAs = new[] { "Indy Melon Mute Method Stalefish Tail Grab" },
                BoxTrick = "Bear Slide", RailTrick = "Rail Bonk", Spin = 0.6f, Flip = 0.5f, Tweak = "Wild", SwitchStep = 2, Presses = false,
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
