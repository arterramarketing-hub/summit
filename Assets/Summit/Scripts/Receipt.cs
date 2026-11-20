using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>Where a hit landed on the thrown body: its parts (Ragdoll), the arms and legs by side.</summary>
    public enum BodyPart { Head, Chest, Pelvis, ArmL, ArmR, ForearmL, ForearmR, ThighL, ThighR, ShinL, ShinR }

    /// <summary>What the thrown body hit.</summary>
    public enum Surface { Snow, Ice, Tree, Rock, Metal, Wood, Bear }

    /// <summary>One hit the thrown body took (RiderBody.Injured): which part, how hard into what.</summary>
    public struct Injury
    {
        public BodyPart part;
        /// <summary>m/s into the surface (the part of the impact square to it).</summary>
        public float speed;
        public Surface on;
        /// <summary>The game broke this bone with this hit (it gets bandaged): at least a fracture on the bill.</summary>
        public bool broke;
        /// <summary>The head went in face first.</summary>
        public bool face;
        /// <summary>The chest or pelvis: struck on its left (-1) or right (+1); 0 front or back.</summary>
        public int side;
    }

    /// <summary>
    /// The medical bill as a receipt: every line comes from something that happened in the fall, never a draw.
    ///
    /// Each hit is graded by how hard it went into what (snow gives, rock and steel don't): 0 nothing, 1 a bruise,
    /// 2 a sprain, a dislocation or a cut, 3 a fracture, 4 a fracture that needs surgery, 5 an open fracture or an
    /// organ, 6 critical. Each part of the body has its own ladder (a wrist goes bruised, sprained, snowboarder's
    /// wrist, both bones, compound), shaped by the ride (skis: the thumb, the knee's ligaments, the boot-top
    /// fracture; a snowboard: the wrist and the ankle). A part prints again in the same fall only when it is
    /// worse. A bone the game breaks (and bandages) is always at least a fracture; one it didn't break is never
    /// more than a sprain. Then, once the body has stopped, what follows from the worst of it: the ski patrol's
    /// sled, an ambulance or the helicopter, the ER or the trauma bay, the scans, the casts, the surgeries, the
    /// nights. Cliff deaths, crevasses, avalanches and lost limbs add their own.
    ///
    /// Costs are typical US billed charges (2023-2025), from the sources in receipt.md. No Unity objects and no
    /// clock: the game feeds it and the HUD prints what it emits (Printed).
    /// </summary>
    public class Receipt
    {
        public struct Item
        {
            /// <summary>As printed; and with the blood switched off (the graphic few say what was done instead).</summary>
            public string name, soft;
            public int cost;
            /// <summary>0..6, how bad: the HUD lands the bad ones harder.</summary>
            public int grade;
            /// <summary>What followed (transport, the ER, scans, surgery, nights), not an injury.</summary>
            public bool service;
            /// <summary>Which fall of the run.</summary>
            public int fall;
            /// <summary>On the run's receipt: how many times it was billed.</summary>
            public int count;
            /// <summary>Taken while still riding (a sign, a banner's stake), not in a fall.</summary>
            public bool riding;
            public string Name(bool gore) => gore || soft == null ? name : soft;
        }

        /// <summary>This fall's lines, in the order they happened.</summary>
        public readonly List<Item> Lines = new List<Item>();
        /// <summary>Every line of the run.</summary>
        public readonly List<Item> All = new List<Item>();
        public int FallTotal { get; private set; }
        public int RunTotal { get; private set; }
        public int Fall { get; private set; }
        /// <summary>A fall is being billed (from the crash to getting up).</summary>
        public bool Open { get; private set; }
        /// <summary>What followed has been billed (the body came to rest).</summary>
        public bool Served { get; private set; }
        /// <summary>This fall's worst grade so far.</summary>
        public int Worst { get; private set; }
        public System.Action<Item> Printed;

        /// <summary>What is ridden (the ladders differ).</summary>
        public RideKind Ride; public bool Tube;

        const int Parts = 11;
        readonly int[] grade = new int[Parts];
        readonly bool[] stitched = new bool[Parts], lost = new bool[Parts], ligament = new bool[Parts];
        int teeth, side;
        bool fatal, crevasse, avalanche, mountain, faceHead;

        // ---------------------------------------------------------------- the prices (typical US billed charges)

        /// <summary>
        /// Billed (chargemaster) amounts, US, from receipt.md's sources: mostly Vail Health Hospital's 2026 standard
        /// charges (a ski town's hospital: facility fee + physician fee), Medicare's 2024 average billed charges for whole
        /// surgical stays and outpatient claims, GAO and FAIR Health for transport, and the French resorts' piste-rescue
        /// tariffs for the ski patrol (US resorts don't bill in-bounds patrol; Mt. Vertigo does, as Chamonix does).
        /// </summary>
        public static class Price
        {
            // getting you off the hill and in
            public const int PatrolFirstAid = 100, PatrolSled = 680, Ambulance = 2400, Helicopter = 36400;
            public const int MountainRescue = 12000, CrevasseRescue = 18000, AvalancheRescue = 9500;
            // the door you came in by (facility + physician)
            public const int Er3 = 1346, Er4 = 2339, Er5 = 4158, TraumaTeam = 8204;
            // pictures (facility + the radiologist's read)
            public const int XRay = 440, CtHead = 2377, CtTorso = 8279, CtBody = 13676, Mri = 1775;
            // holding it together
            public const int ArmCast = 537, LegCast = 534, ThumbSplint = 456, KneeSplint = 515, Boot = 268, Sling = 20, BackBrace = 1200, Crutches = 40;
            // the operating room: whole-claim or whole-stay billed averages, the lowest tier
            public const int OrifWrist = 41000, OrifHumerus = 85163, OrifTibia = 66748, OrifKnee = 95089, FemurRod = 77908,
                             ChestTube = 2497, Splenectomy = 91066, AbdominalSurgery = 91066, RibFixation = 55456,
                             PelvicFix = 176346, SpineFusion = 170331, SkullRepair = 139743, Craniotomy = 165029, JawOrif = 54170,
                             ReplantArm = 136238, ReplantLeg = 164409, Implant = 4000;
            // the nights, and the blood
            public const int Ward = 3612, Icu = 7833, Unit = 689, Transfusion = 562;
            // each injury's own care in the ER: the physician's evaluation by level, or the procedure for it
            public const int Bruise = 217, Sprain = 411, Serious = 609, Reduction = 1951, ClosedFracture = 3673, NasalReduction = 911,
                             OpenWound = 2579, Tooth = 521, Heels = 7346, Hypothermia = 6065, Frostbite = 3070, Asphyxia = 6944;
            // stitches: simple (to 2.5 cm), intermediate (to 7.5 cm), complex
            public const int StitchSmall = 794, StitchMid = 1940, StitchBig = 3204;
            public static int Stitches(int n) => n <= 5 ? StitchSmall : n <= 12 ? StitchMid : StitchBig;
        }

        // ---------------------------------------------------------------- a fall

        public void Reset()
        {
            Lines.Clear(); All.Clear(); FallTotal = RunTotal = 0; Fall = 0; Open = false; Served = false; Worst = 0;
        }

        /// <summary>A crash: a new fall on the bill.</summary>
        public void Begin()
        {
            Fall++;
            Lines.Clear(); FallTotal = 0; Open = true; Served = false; Worst = 0;
            for (int i = 0; i < Parts; i++) { grade[i] = 0; stitched[i] = false; lost[i] = false; ligament[i] = false; }
            teeth = 0; side = 0; fellM = 0f; fatal = crevasse = avalanche = mountain = faceHead = false;
        }

        /// <summary>Up again (or the run is over): this fall's bill is closed.</summary>
        public void Close() { Open = false; }

        void Print(string name, string soft, int cost, int g, bool service)
        {
            if (!Open || cost <= 0) return;
            var it = new Item { name = name, soft = soft, cost = cost, grade = g, service = service, fall = Fall, count = 1 };
            Lines.Add(it); All.Add(it);
            FallTotal += cost; RunTotal += cost;
            if (!service && g > Worst) Worst = g;
            Printed?.Invoke(it);
        }
        void Print(string name, int cost, int g, bool service = false) => Print(name, null, cost, g, service);

        // ---------------------------------------------------------------- the hits

        static float Hardness(Surface s) =>
            s == Surface.Snow ? 0.72f : s == Surface.Ice ? 0.9f : s == Surface.Rock || s == Surface.Metal ? 1.12f : 1f;

        /// <summary>How bad a hit is, from how hard it went into what.</summary>
        public static int GradeOf(float speed, Surface on)
        {
            float e = speed * Hardness(on);
            return e < 4.5f ? 0 : e < 7f ? 1 : e < 10f ? 2 : e < 14f ? 3 : e < 19f ? 4 : e < 26f ? 5 : 6;
        }

        static bool Limb(BodyPart p) => p >= BodyPart.ArmL;
        static string LimbWord(BodyPart p) => p <= BodyPart.ArmR ? "arm" : p <= BodyPart.ForearmR ? "forearm" : p <= BodyPart.ThighR ? "leg" : "lower leg";
        static string S(BodyPart p) => p == BodyPart.ArmL || p == BodyPart.ForearmL || p == BodyPart.ThighL || p == BodyPart.ShinL ? "L" : "R";

        /// <summary>A hit: a line if it is worse than anything that part has had this fall.</summary>
        public void Hit(Injury i)
        {
            if (!Open || fatal) return;
            int p = (int)i.part;
            if (lost[p]) return;
            float e = i.speed * Hardness(i.on);
            int g = GradeOf(i.speed, i.on);
            if (i.on == Surface.Bear) g = Mathf.Max(g, 2);
            // a bone the game broke is a fracture at least; an arm or leg it didn't break is a sprain at most
            // (unless that bone already broke in this fall: then it can only get worse)
            if (i.broke) g = Mathf.Max(g, 3);
            else if (Limb(i.part) && grade[p] < 3) g = Mathf.Min(g, 2);
            if (i.part == BodyPart.Chest || i.part == BodyPart.Pelvis) side = i.side;

            if (g > grade[p])
            {
                grade[p] = g;
                Ladder(i.part, g, i, e);
            }
            // what the surface adds, once a part a fall: a cut on rock or steel, a mauling
            if (!stitched[p] && ((e >= 8f && (i.on == Surface.Rock || i.on == Surface.Metal)) || i.on == Surface.Bear))
            {
                stitched[p] = true;
                int n = Mathf.Clamp(Mathf.RoundToInt(3f + Mathf.Max(0f, e - 8f) * 1.6f), 3, 40);
                if (i.on == Surface.Bear)
                {
                    n = Mathf.Max(n, 12);
                    Print("Claw wounds, " + n + " stitches", "Stitches (" + n + ")", Price.Stitches(n), 2, false);
                }
                else if (i.part == BodyPart.Head) Print("Scalp laceration, " + n + " stitches", "Stitches (" + n + ")", Price.Stitches(n), 2, false);
                else Print("Laceration, " + n + " stitches", "Stitches (" + n + ")", Price.Stitches(n), 2, false);
            }
            // teeth: the face into something hard enough
            if (i.part == BodyPart.Head && i.face && e >= 9f)
            {
                int n = Mathf.Clamp(1 + Mathf.FloorToInt((e - 9f) / 3f), 1, 6);
                if (n > teeth)
                {
                    int more = n - teeth; teeth = n;
                    Print(more == 1 ? "Knocked-out tooth" : "Knocked-out teeth (" + more + ")", more == 1 ? "Broken tooth" : "Broken teeth (" + more + ")",
                          Price.Tooth * more, 2, false);
                }
            }
        }

        /// <summary>Each part's ladder, worst last.</summary>
        void Ladder(BodyPart part, int g, Injury i, float e)
        {
            string a = S(part);
            bool skis = !Tube && Ride == RideKind.Skis, board = !Tube && Ride == RideKind.Snowboard;
            switch (part)
            {
                case BodyPart.Head:
                    faceHead = i.face;
                    if (i.face)
                    {
                        if (g == 1) Print("Black eye", Price.Bruise, g);
                        else if (g == 2) Print("Broken nose", Price.NasalReduction, g);
                        else if (g == 3) Print("Orbital fracture", Price.Serious, g);
                        else if (g == 4) Print("Broken jaw", Price.Serious, g);
                        else if (g == 5) Print("Brain bleed", "Brain injury", Price.Serious, g, false);
                        else Print("Severe brain injury", Price.Serious, g);
                    }
                    else
                    {
                        if (g == 1) Print(i.on == Surface.Snow ? "Whiplash" : "Head contusion", Price.Bruise, g);
                        else if (g == 2) Print("Concussion", Price.Serious, g);
                        else if (g == 3) Print("Skull fracture", Price.Serious, g);
                        else if (g == 4) Print("Depressed skull fracture", Price.Serious, g);
                        else if (g == 5) Print("Brain bleed", "Brain injury", Price.Serious, g, false);
                        else Print("Severe brain injury", Price.Serious, g);
                    }
                    break;
                case BodyPart.Chest:
                    if (g == 1) Print("Chest contusion", Price.Bruise, g);
                    else if (g == 2) Print("Cracked rib", Price.Sprain, g);
                    else if (g == 3) Print("Fractured ribs (" + (e >= 12f ? 3 : 2) + ")", Price.Serious, g);
                    else if (g == 4) Print("Collapsed lung", Price.Serious, g);
                    else if (g == 5)
                    {
                        if (i.side < 0) Print("Ruptured spleen", "Spleen injury", Price.Serious, g, false);
                        else if (i.side > 0) Print("Lacerated liver", "Liver injury", Price.Serious, g, false);
                        else Print("Internal bleeding", "Internal injuries", Price.Serious, g, false);
                    }
                    else Print("Flail chest", Price.Serious, g);
                    break;
                case BodyPart.Pelvis:
                    if (g == 1) Print("Hip contusion", Price.Bruise, g);
                    else if (g == 2) Print("Bruised tailbone", Price.Sprain, g);
                    else if (g == 3) Print("Fractured tailbone", Price.Serious, g);
                    else if (g == 4) Print("Compression fracture, L1", Price.Serious, g);
                    else if (g == 5) Print("Fractured pelvis", Price.Serious, g);
                    else Print("Pelvic ring fracture", Price.Serious, g);
                    break;
                case BodyPart.ArmL: case BodyPart.ArmR:
                    if (g == 1) Print("Bruised " + a + " shoulder", Price.Bruise, g);
                    else if (g == 2) Print("Dislocated " + a + " shoulder", Price.Reduction, g);
                    else if (g == 3) Print("Fractured " + a + " collarbone", Price.ClosedFracture, g);
                    else if (g == 4) Print("Fractured " + a + " humerus", Price.Serious, g);
                    else Print("Compound fracture, " + a + " humerus", Price.OpenWound, g);
                    break;
                case BodyPart.ForearmL: case BodyPart.ForearmR:
                    if (g == 1) Print("Bruised " + a + " wrist", Price.Bruise, g);
                    else if (g == 2) { if (skis) { Print("Skier's thumb, " + a, Price.Sprain, g); ligament[(int)part] = true; } else Print("Sprained " + a + " wrist", Price.Sprain, g); }
                    else if (g == 3) Print(board ? "Snowboarder's wrist, " + a : skis ? "Fractured " + a + " scaphoid" : "Fractured " + a + " radius", Price.ClosedFracture, g);
                    else if (g == 4) Print("Fractured " + a + " radius + ulna", Price.Serious, g);
                    else Print("Compound fracture, " + a + " forearm", Price.OpenWound, g);
                    break;
                case BodyPart.ThighL: case BodyPart.ThighR:
                    if (g == 1) Print("Bruised " + a + " thigh", Price.Bruise, g);
                    else if (g == 2) { Print(skis ? "Torn " + a + " MCL" : "Sprained " + a + " knee", Price.Sprain, g); ligament[(int)part] = true; }
                    else if (g == 3)
                    {
                        if (skis) { Print("Torn " + a + " ACL", Price.Serious, g); ligament[(int)part] = true; }
                        else Print("Fractured " + a + " kneecap", Price.ClosedFracture, g);
                    }
                    else if (g == 4) Print("Fractured " + a + " femur", Price.Serious, g);
                    else Print("Compound fracture, " + a + " femur", Price.OpenWound, g);
                    break;
                default:   // the shins
                    if (g == 1) Print("Bruised " + a + " shin", Price.Bruise, g);
                    else if (g == 2) Print("Sprained " + a + " ankle", Price.Sprain, g);
                    else if (g == 3) Print(board ? "Snowboarder's ankle, " + a : skis ? "Boot-top fracture, " + a : "Fractured " + a + " ankle", Price.ClosedFracture, g);
                    else if (g == 4) Print(skis ? "Tibial plateau fracture, " + a : "Fractured " + a + " tib-fib", Price.Serious, g);
                    else Print("Compound fracture, " + a + " tibia", Price.OpenWound, g);
                    break;
            }
        }

        /// <summary>A limb torn off (RiderBodyLimbs): what came off.</summary>
        public void Lost(BodyPart part, bool whole)
        {
            if (!Open) return;
            int p = (int)part;
            if (lost[p]) return;
            lost[p] = true; grade[p] = 6;
            string what = S(part) + " " + LimbWord(part);
            Print("Traumatic amputation, " + what, "Detached " + what, Price.OpenWound, 6, false);
            if (Served) Print("Reattachment, " + what, part <= BodyPart.ForearmR ? Price.ReplantArm : Price.ReplantLeg, 6, true);   // (after the rest of the bill: a fatal fall's)
        }

        // ---------------------------------------------------------------- hits taken while riding

        /// <summary>
        /// A sign or a SLOW banner's stake ridden through (Breakables.RiderHit): the rider stays up, so it is a bruise
        /// or a little worse, one line, priced as the game prices it. sign: a sign (else a banner's stake); part: "Head",
        /// "Chest", "Shoulder", "Legs" (a sign) or "Shin" (a stake); speed m/s; cost what the game charges for it
        /// (Breakables.SignCost, StakeCost). A sign worth $2,450 or more was hit at 24 m/s or more into its face; a
        /// stake caught at 22 m/s or more cuts.
        /// </summary>
        public void RideHit(bool sign, string part, float speed, float cost)
        {
            int c = Mathf.RoundToInt(cost);
            if (c <= 0) return;
            bool worse = sign ? cost >= 2450f : speed >= 22f;
            int g = worse ? 2 : 1;
            string name, soft = null;
            if (!sign)
            {
                if (!worse) name = "Bruised shin";
                else { int n = Mathf.Clamp(Mathf.RoundToInt(3f + (speed - 22f) * 1.2f), 3, 20); name = "Shin laceration, " + n + " stitches"; soft = "Stitches (" + n + ")"; }
            }
            else switch (part)
            {
                case "Head": name = worse ? "Concussion" : "Head contusion"; break;
                case "Chest": name = worse ? "Cracked rib" : "Chest contusion"; break;
                case "Shoulder": name = worse ? "Separated shoulder (AC)" : "Bruised shoulder"; break;
                default: name = worse ? "Thigh hematoma" : "Bruised thigh"; break;
            }
            var it = new Item { name = name, soft = soft, cost = c, grade = g, service = false, fall = Fall, count = 1, riding = true };
            All.Add(it); RunTotal += c;
            Printed?.Invoke(it);
        }

        // ---------------------------------------------------------------- the falls that end it

        /// <summary>Off a cliff: fell this far (m), and came down on rock or on snow.</summary>
        public void Killed(float fell, bool onRock)
        {
            if (!Open || fatal) return;
            fatal = true; mountain = true;
            // what a fall from height does to whatever it didn't already: the spine, the pelvis, the heels, the
            // organs, the head on rock or from very high
            Print("Burst fracture, T12", Price.Serious, 6);
            if (grade[(int)BodyPart.Pelvis] < 6) { grade[(int)BodyPart.Pelvis] = 6; Print("Pelvic ring fracture", Price.Serious, 6); }
            Print("Fractured heels (both)", Price.Heels, 5);
            if (grade[(int)BodyPart.Chest] < 5) { grade[(int)BodyPart.Chest] = 5; Print("Internal bleeding", "Internal injuries", Price.Serious, 6, false); }
            if ((onRock || fell > 160f) && grade[(int)BodyPart.Head] < 6) { grade[(int)BodyPart.Head] = 6; Print("Severe brain injury", Price.Serious, 6); }
            Worst = 6;
            fellM = fell;
            Rest();
        }
        float fellM;

        /// <summary>Down a crevasse and not coming back up on its own.</summary>
        public void Crevasse()
        {
            if (!Open || crevasse || fatal) return;
            crevasse = true; fatal = true;
            Print("Hypothermia", Price.Hypothermia, 5);
            Print("Frostbite, both hands", Price.Frostbite, 5);
            Worst = Mathf.Max(Worst, 5);
            Rest();
        }

        /// <summary>Caught and buried by the avalanche.</summary>
        public void Avalanche()
        {
            if (!Open || avalanche) return;
            avalanche = true;
            Print("Hypothermia", Price.Hypothermia, 5);
            Print("Asphyxia", "Breathing support", Price.Asphyxia, 5, false);
            Worst = Mathf.Max(Worst, 5);
        }

        // ---------------------------------------------------------------- what follows

        /// <summary>The body has stopped (or the rider is up, or it is over): what that fall brings with it.</summary>
        public void Rest()
        {
            if (!Open || Served) return;
            Served = true;
            int G = Worst;
            int[] gr = grade;
            int Gp(BodyPart p) => gr[(int)p];

            // off the hill
            if (crevasse) Print("Crevasse rescue (rope team)", Price.CrevasseRescue, 5, true);
            else if (avalanche) Print("Avalanche rescue (probe line)", Price.AvalancheRescue, 5, true);
            else if (mountain) Print("Mountain rescue (rope team)", Price.MountainRescue, 5, true);
            else if (G <= 1) { Print("Ski patrol first aid", Price.PatrolFirstAid, G, true); return; }
            else Print("Ski patrol toboggan", Price.PatrolSled, G, true);
            if (G >= 5) Print("Helicopter evacuation", Price.Helicopter, 5, true);
            else if (G == 4) Print("Ambulance (ALS)", Price.Ambulance, 4, true);

            // in
            if (G >= 5) Print("Trauma team activation", Price.TraumaTeam, 5, true);
            if (G >= 4) Print("Emergency room, level 5", Price.Er5, 4, true);
            else if (G == 3) Print("Emergency room, level 4", Price.Er4, 3, true);
            else Print("Emergency room, level 3", Price.Er3, 2, true);

            // pictures
            int limbs = 0;
            for (var p = BodyPart.ArmL; p <= BodyPart.ShinR; p++) if (Gp(p) >= 2) limbs++;
            if (G >= 5) Print("CT scan, full body", Price.CtBody, 5, true);
            else
            {
                if (Gp(BodyPart.Head) >= 2) Print("CT scan, head", Price.CtHead, 3, true);
                if (Gp(BodyPart.Chest) >= 4 || Gp(BodyPart.Pelvis) >= 4) Print("CT scan, chest + abdomen", Price.CtTorso, 4, true);
            }
            if (limbs > 0 || Gp(BodyPart.Chest) == 2 || Gp(BodyPart.Chest) == 3 || Gp(BodyPart.Pelvis) == 3)
            {
                int n = limbs + (Gp(BodyPart.Chest) >= 2 ? 1 : 0) + (Gp(BodyPart.Pelvis) >= 3 ? 1 : 0);
                Print(n == 1 ? "X-ray" : "X-rays (" + n + ")", Price.XRay * Mathf.Max(1, n), 2, true);
            }
            for (var p = BodyPart.ThighL; p <= BodyPart.ThighR; p++) if (ligament[(int)p] && Gp(p) <= 3) Print("MRI, " + S(p) + " knee", Price.Mri, 2, true);

            // holding it together
            bool leg = false;
            for (var p = BodyPart.ArmL; p <= BodyPart.ShinR; p++)
            {
                int g = Gp(p); if (lost[(int)p]) continue;
                bool isLeg = p >= BodyPart.ThighL;
                if (isLeg && g >= 2) leg = true;
                if (p <= BodyPart.ArmR) { if (g == 2 || g == 3) Print("Sling", Price.Sling, g, true); }
                else if (p <= BodyPart.ForearmR) { if (g == 3) Print("Cast, " + S(p) + " wrist", Price.ArmCast, g, true); else if (g == 2 && ligament[(int)p]) Print("Thumb splint, " + S(p), Price.ThumbSplint, g, true); }
                else if (p <= BodyPart.ThighR) { if (g == 2 || (g == 3 && ligament[(int)p])) Print("Knee splint, " + S(p), Price.KneeSplint, g, true); else if (g == 3) Print("Cast, " + S(p) + " leg", Price.LegCast, g, true); }
                else { if (g == 2) Print("Walking boot", Price.Boot, g, true); else if (g == 3) Print("Cast, " + S(p) + " leg", Price.LegCast, g, true); }
            }
            if (Gp(BodyPart.Pelvis) == 4) Print("Back brace", Price.BackBrace, 4, true);
            if (leg) Print("Crutches", Price.Crutches, 2, true);

            // the operating room
            int ops = 0, big = 0;
            void Op(string name, int cost, int g) { Print(name, cost, g, true); ops++; }
            for (var p = BodyPart.ArmL; p <= BodyPart.ShinR; p++)
            {
                int g = Gp(p); string a = S(p);
                if (lost[(int)p]) { Op("Reattachment, " + a + " " + LimbWord(p), p <= BodyPart.ForearmR ? Price.ReplantArm : Price.ReplantLeg, 6); big++; continue; }
                if (g < 4) continue;
                if (g >= 5) big++;
                if (p <= BodyPart.ArmR) Op("ORIF surgery, " + a + " humerus", Price.OrifHumerus, g);
                else if (p <= BodyPart.ForearmR) Op("ORIF surgery, " + a + " wrist", Price.OrifWrist, g);
                else if (p <= BodyPart.ThighR) Op("Femur rod surgery, " + a, Price.FemurRod, g);
                else Op(Ride == RideKind.Skis && g == 4 ? "ORIF surgery, " + a + " knee" : "ORIF surgery, " + a + " tibia", Ride == RideKind.Skis && g == 4 ? Price.OrifKnee : Price.OrifTibia, g);
            }
            int ch = Gp(BodyPart.Chest);
            if (ch == 4) Op("Chest tube", Price.ChestTube, 4);
            else if (ch == 5) { big++; if (side < 0) Op("Splenectomy", Price.Splenectomy, 5); else if (side > 0) Op("Liver repair surgery", Price.AbdominalSurgery, 5); else Op("Emergency laparotomy", Price.AbdominalSurgery, 5); }
            else if (ch >= 6) { big++; Op("Rib fixation surgery", Price.RibFixation, 6); }
            int pv = Gp(BodyPart.Pelvis);
            if (pv >= 6) { big++; Op("Pelvic fixation surgery", Price.PelvicFix, 6); }
            if (fatal && !crevasse) { big++; Op("Spinal fusion, T11-L1", Price.SpineFusion, 6); }
            int hd = Gp(BodyPart.Head);
            if (hd == 4) Op(faceHead ? "Jaw surgery (ORIF)" : "Skull repair surgery", faceHead ? Price.JawOrif : Price.SkullRepair, 4);
            else if (hd >= 5) { big++; Op("Craniotomy", Price.Craniotomy, hd); }
            if (teeth > 0) Print(teeth == 1 ? "Dental implant" : "Dental implants (" + teeth + ")", Price.Implant * teeth, 2, true);

            // the blood, and the nights
            if (G >= 5)
            {
                int units = Mathf.Clamp(2 + big * 2 + (fatal ? Mathf.RoundToInt(fellM / 25f) : 0), 2, 24);
                Print("Blood transfusion (" + units + " units)", Price.Unit * units + Price.Transfusion, 5, true);
                int icu = Mathf.Clamp(1 + big * 2 + (fatal ? 3 : 0), 2, 21);
                Print("ICU, " + icu + " nights", Price.Icu * icu, 5, true);
                Print("Hospital, " + (2 + ops) + " nights", Price.Ward * (2 + ops), 4, true);
            }
            else if (G == 4) Print("Hospital, " + (1 + ops) + (1 + ops == 1 ? " night" : " nights"), Price.Ward * (1 + ops), 4, true);
        }

        // ---------------------------------------------------------------- the run's receipt

        /// <summary>The run's receipt as the end screen shows it: the same item from different falls on one line, x n.</summary>
        public List<Item> Merged(bool gore)
        {
            var list = new List<Item>();
            var at = new Dictionary<string, int>();
            foreach (var it in All)
            {
                string key = it.Name(gore);
                if (at.TryGetValue(key, out int k)) { var m = list[k]; m.cost += it.cost; m.count++; m.grade = Mathf.Max(m.grade, it.grade); list[k] = m; }
                else { at[key] = list.Count; list.Add(it); }
            }
            return list;
        }
    }
}
