using UnityEngine;

namespace Summit
{
    // SummitGame, part: the buddy's voice and the screams, watching the run
    public partial class SummitGame
    {
        // the "nooo": down a crevasse it fades with the depth; off a cliff, a short one the moment you are clearly
        // falling far below where you left the snow
        bool cliffNooo, crevNooo;
        // the warnings, a beat before it happens: "don't go down there" with a deadly drop or a crevasse ahead on the
        // line you're riding, "lookout" with a tree, rock or sign right in the way
        float nextWarnCheck;
        void WatchWarn()
        {
            if (state != State.Riding || audioFx == null || rider.Crashed || rider.Airborne || paused) return;
            if (Time.unscaledTime < nextWarnCheck) return;
            nextWarnCheck = Time.unscaledTime + 0.25f;
            Vector3 p = rider.transform.position, v = rider.Body.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float sp = flat.magnitude;
            if (sp < 8f) return;
            Vector3 dir = flat / sp;
            for (float t = 0.8f; t <= 1.6f; t += 0.4f)
            {
                Vector3 q = p + dir * sp * t;
                if (Deadly.Below(q.x, q.z) || Course.CrevasseNear(q.x, q.z, 1.5f)) { audioFx.Play(Sfx.BuddyDeep); return; }
            }
            if (Physics.SphereCast(p + Vector3.up * 0.8f, 0.5f, dir, out RaycastHit hit, sp * 0.9f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Obstacle>() != null)
                audioFx.Play(Sfx.BuddyWarn);
        }

        // the woahs: high off a jump (once per air), and now and then at full tilt
        bool airWoah; float nextSpeedWoah;
        void WatchWoah()
        {
            if (state != State.Riding || audioFx == null || rider.Crashed) { airWoah = false; return; }
            if (!rider.Airborne) airWoah = false;
            else if (!airWoah && rider.AboveGround > 5f) { airWoah = true; audioFx.Play(Sfx.BuddyWoah); }
            if (!rider.Airborne && rider.Speed * 3.6f > 150f && Time.unscaledTime > nextSpeedWoah)
            {
                nextSpeedWoah = Time.unscaledTime + 25f;
                if (Random.value < 0.5f) audioFx.Play(Sfx.BuddyWoah);
            }
        }
        void WatchDrop()
        {
            if (audioFx == null) return;
            if (state != State.Riding && state != State.Buried) return;
            Vector3 c = rider.Crashed ? body.RagdollCentre : rider.transform.position;
            bool overCrevasse = Course.CrevasseNear(c.x, c.z, 2.5f);
            if (rider.Crashed)
            {
                audioFx.NoooDepth(body.RagdollBelowSnow);
                // into a crevasse, however you got there: only the "nooo", never a scream over it
                if (!crevNooo && overCrevasse && body.RagdollBelowSnow > 0.8f)
                {
                    crevNooo = true;
                    audioFx.StopScreaming(true);
                    if (!audioFx.NoooPlaying) audioFx.Play(Sfx.Nooo);
                }
            }
            if (state != State.Riding) return;
            if (!rider.Airborne && !rider.Crashed) { cliffNooo = false; return; }
            if (cliffNooo || crevasseWatch || overCrevasse || audioFx.NoooPlaying) return;
            // off a cliff: only once the snow under you is further down than anyone survives (a jump, however big,
            // comes down onto a slope that isn't that far below, so it never cries out)
            Vector3 d = rider.DropFrom;
            float ground = Slope.DrawnHeight(c.x, c.z);
            float toFall = d.y - ground - Slope.Fall * Mathf.Max(0f, c.z - d.z);
            float falling = rider.Crashed ? -1f : rider.Body.linearVelocity.y;
            if (toFall > Rider.DeathFall && c.y - ground > 3f && falling < -1f) { cliffNooo = true; audioFx.StopScreaming(true); audioFx.NoooFor(1.5f); }
        }

        void StartScreaming()
        {
            screamT = 0f; screamSlowT = 0f; audioFx.StartScreaming();
            if (autoplay || showDiag) Debug.Log("SUMMIT SCREAM start v=" + (rider.Speed * 3.6f).ToString("F0") + " km/h");
        }

        // the screaming goes on, clip after clip, until the body has slowed below 10 km/h (held for a moment, so a
        // bounce doesn't cut it) or you're back on your feet; then it dies away
        void WatchScream(float dt)
        {
            if (audioFx == null || !audioFx.Screaming || dt <= 0f) return;
            screamT += dt;
            bool slow = body.RagdollSpeed * 3.6f < 10f;
            screamSlowT = slow ? screamSlowT + dt : 0f;
            if (!rider.Crashed || (screamT > 0.8f && screamSlowT > 0.3f))
            {
                audioFx.StopScreaming();
                if (autoplay || showDiag) Debug.Log("SUMMIT SCREAM stop after " + screamT.ToString("F1") + " s, body at " + (body.RagdollSpeed * 3.6f).ToString("F0") + " km/h");
            }
        }
    }
}
