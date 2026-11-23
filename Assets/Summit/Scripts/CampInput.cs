using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// The camp's touch controls, as plain logic (no Unity input in here, so the test tools can drive it with made-up
    /// touches). Every finger is one of:
    ///   - the MOVE stick: the first finger down, anywhere on the screen (floating: wherever the thumb lands). It
    ///     walks the hero, and the camera swings round behind as you go, so one thumb does both;
    ///   - the LOOK stick: a second finger pressed and held while the first is down, for turning the camera round
    ///     the hero by hand when you want to;
    ///   - a TAP: a quick touch anywhere (not on a button) that hardly moves: the hero jumps (or, standing still on
    ///     a station, opens it);
    ///   - a SWIPE: a quick flick that lets go: the hero rolls that way;
    ///   - a button: the menus and the context button take their own touches, as before.
    /// A second finger isn't the look stick straight away: it waits a moment, so a tap or a flick with it never
    /// nudges the camera first. (Two sticks at once, one per thumb, was too much to handle on a phone; a plugged-in
    /// controller still has its two sticks, see Camp.)
    /// The move stick is sticky: once the thumb is on it, it is that thumb's until it lifts. Past the rim the base
    /// follows the thumb and the walk stays at full strength; other fingers (taps, buttons, the look stick) never
    /// take it; a thumb that lands on the station button and drags walks instead; and a thumb that loses the
    /// screen for an instant (the phone dropping the touch) and comes straight back near where it was picks the
    /// same stick up again, already walking, rather than starting over. Lifted, it's gone: nothing keeps walking.
    /// </summary>
    public class CampInput
    {
        public struct Touch { public int id; public Vector2 pos; public Touch(int i, Vector2 p) { id = i; pos = p; } }
        enum Role { Undecided, Move, Look, Button, Context, Ignore }
        class Finger { public int id; public Role role; public Vector2 start, origin, last; public float t, travel; public bool seen, resumed; public string button; }

        // what this frame came to
        public Vector2 Move, Look;
        public bool MoveShown, LookShown;
        public Vector2 MoveOrigin, MovePos, LookOrigin, LookPos;
        public bool Tapped, Swiped, ContextTapped;
        public Vector2 TapAt, SwipeDir;   // the swipe's direction on the screen (up = away from the camera)
        public string Button;              // a button touched this frame (it acts on touching down, as before)
        /// <summary>Test tools: what the last finger to lift was taken as.</summary>
        public string LastGesture = "";

        public const float TapTime = 0.3f, SwipeTime = 0.42f, LookWait = 0.16f;
        /// <summary>A walking thumb that comes back within this long (s), this near (in stick radii), picks its stick up again.</summary>
        public const float ResumeTime = 0.12f, ResumeReach = 1.25f;
        /// <summary>The stick's throw this frame (screen pixels): the drawn ring is this big.</summary>
        public float Radius { get; private set; } = 150f;
        readonly List<Finger> fingers = new List<Finger>();
        // the walking thumb that lifted last: where its stick was, and how long ago
        Vector2 lostOrigin, lostPos; float lostT = 99f;

        /// <summary>Ignore the fingers down right now (the tap that brought you to the camp) until they lift.</summary>
        public void IgnoreCurrent(List<Touch> now)
        {
            fingers.Clear();
            lostT = 99f;
            foreach (var t in now) fingers.Add(new Finger { id = t.id, role = Role.Ignore, start = t.pos, last = t.pos, seen = true });
        }

        public void Step(List<Touch> now, float dt, float w, float h, System.Func<Vector2, string> hit, bool panelOpen)
        {
            Move = Look = Vector2.zero; MoveShown = LookShown = false;
            Tapped = Swiped = ContextTapped = false; Button = null;
            float radius = Mathf.Max(60f, Mathf.Min(w, h) * 0.14f);
            Radius = radius;
            float tapDist = radius * 0.14f, swipeDist = radius * 0.55f;
            lostT += dt;
            foreach (var f in fingers) f.seen = false;
            foreach (var t in now)
            {
                var f = fingers.Find(x => x.id == t.id);
                if (f == null)
                {
                    // a new finger: a button, the move stick (the first finger down), or wait and see (a second)
                    f = new Finger { id = t.id, start = t.pos, origin = t.pos, last = t.pos };
                    string id = hit != null ? hit(t.pos) : null;
                    if (id == "context") f.role = Role.Context;   // it opens on lifting a tap, never on touching down
                    else if (id != null) { f.role = Role.Button; Button = id; }
                    else if (panelOpen) f.role = Role.Ignore;
                    else f.role = fingers.Exists(x => x.role == Role.Move) ? Role.Undecided : Role.Move;
                    // the walking thumb back after losing the screen for an instant: the same stick, still walking
                    if (f.role == Role.Move && lostT < ResumeTime && (t.pos - lostPos).magnitude < radius * ResumeReach)
                    {
                        f.origin = lostOrigin; f.resumed = true;
                        lostT = 99f;
                    }
                    fingers.Add(f);
                }
                f.seen = true;
                f.t += dt;
                f.travel = Mathf.Max(f.travel, (t.pos - f.start).magnitude);
                f.last = t.pos;
                if (f.role == Role.Undecided && f.t >= LookWait) { f.role = Role.Look; f.origin = t.pos; }
                // a thumb that landed on the station button and drags off is walking, not pressing it
                if (f.role == Role.Context && f.travel > tapDist * 1.5f && !fingers.Exists(x => x.role == Role.Move)) f.role = Role.Move;
                if (f.role == Role.Move || f.role == Role.Look)
                {
                    var d = t.pos - f.origin;
                    // the stick's centre trails the thumb past full throw, so turning round is instant
                    if (d.magnitude > radius) { f.origin = t.pos - d.normalized * radius; d = t.pos - f.origin; }
                    bool live = f.role == Role.Look || f.resumed || f.travel > tapDist;   // a resting thumb isn't a walk yet
                    if (f.role == Role.Move && live) { Move = d / radius; MoveShown = true; MoveOrigin = f.origin; MovePos = t.pos; }
                    if (f.role == Role.Look) { Look = d / radius; LookShown = true; LookOrigin = f.origin; LookPos = t.pos; }
                }
            }
            // fingers lifted this frame: was that a tap or a flick?
            for (int i = fingers.Count - 1; i >= 0; i--)
            {
                var f = fingers[i];
                if (f.seen) continue;
                fingers.RemoveAt(i);
                var d = f.last - f.start;
                if (f.role == Role.Move && (f.travel > tapDist || f.resumed)) { lostOrigin = f.origin; lostPos = f.last; lostT = 0f; }
                if (f.role == Role.Ignore || f.role == Role.Button) { LastGesture = f.role.ToString().ToLowerInvariant(); continue; }
                if (f.role == Role.Context) { if (f.travel < tapDist && f.t < 0.6f) { ContextTapped = true; LastGesture = "context"; } continue; }
                if (f.t < TapTime && f.travel < tapDist) { Tapped = true; TapAt = f.last; LastGesture = "tap"; }
                else if (f.t < SwipeTime && d.magnitude > swipeDist && d.magnitude / Mathf.Max(f.t, 1e-3f) > radius * 2.2f)
                { Swiped = true; SwipeDir = d.normalized; LastGesture = "swipe"; }
                else LastGesture = f.role == Role.Move ? "move" : f.role == Role.Look ? "look" : "hold";
            }
        }

        /// <summary>For the test tools: how many fingers are being tracked.</summary>
        public int Count => fingers.Count;
    }
}
