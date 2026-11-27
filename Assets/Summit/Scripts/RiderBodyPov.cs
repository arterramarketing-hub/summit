using UnityEngine;

namespace Summit
{
    /// <summary>
    /// What the first-person view (ChaseCamera's `pov`) needs of the body: the head bone the lens rides at, and
    /// the head taken out of the picture while the lens is inside it.
    ///
    /// The head, helmet and goggles are skinned to the head bone, so the bone is shrunk to nothing: they draw
    /// nowhere, and the board, legs and arms draw as always. A head-sized ball that only casts a shadow stands
    /// in for it on the neck, so the rider's shadow on the snow keeps its head. The hood hangs behind the neck
    /// (its own bone) and is left alone.
    ///
    /// The head comes back the instant a crash starts, before the ragdoll takes the skeleton over (its head has
    /// a collider and a joint, which must not be built or switched on at no size), and whenever the lens leaves.
    /// Works on any rider with a skeleton (the hero and the previous one); the figure made of shapes has no
    /// head bone, and keeps its head.
    /// </summary>
    public partial class RiderBody
    {
        public static float PovHelmet = 0.30f;     // m: the shadow ball's width (the helmet's)
        public static float PovHelmetUp = 0.14f;   // m: its middle above the head bone (the base of the skull)

        Transform povShadow;
        Vector3 povShadowAt, povHeadScale = Vector3.one;
        bool povHidden, povHooked;

        /// <summary>The head bone: the first-person lens follows it (null on a rider without a skeleton).</summary>
        public Transform PovHead
        {
            get
            {
                return rig != null && rig.Ok ? rig.Head : null;
            }
        }

        /// <summary>Hide the head from the lens (its shadow stays), or put it back. Safe to call every frame.</summary>
        public void SetPovHead(bool hide)
        {
            Transform head = PovHead;
            if (head == null || rider == null) return;
            if (!povHooked) { povHooked = true; rider.Crash += _ => SetPovHead(false); }   // back before the body is thrown
            if (rider.Crashed || Walking) hide = false;
            if (hide == povHidden)
            {
                if (hide) PlacePovShadow(head);
                return;
            }
            povHidden = hide;
            if (hide)
            {
                if (povShadow == null) BuildPovShadow(head);   // (while the head is still its full size)
                povHeadScale = head.localScale;
                head.localScale = povHeadScale * 0.001f;
                if (povShadow != null) { povShadow.gameObject.SetActive(true); PlacePovShadow(head); }
            }
            else
            {
                head.localScale = povHeadScale;
                if (povShadow != null) povShadow.gameObject.SetActive(false);
            }
        }

        void BuildPovShadow(Transform head)
        {
            Transform neck = head.parent;
            if (neck == null) return;
            // the middle of the helmet: on up from the neck, past the base of the skull
            Vector3 up = head.position - neck.position;
            up = up.sqrMagnitude > 1e-6f ? up.normalized : transform.up;
            povShadowAt = Quaternion.Inverse(head.rotation) * (up * PovHelmetUp);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PovHeadShadow";
            foreach (var c in go.GetComponents<Collider>()) DestroyImmediate(c);
            go.transform.SetParent(neck, false);
            float s = Mathf.Max(1e-4f, neck.lossyScale.x);
            go.transform.localScale = Vector3.one * (PovHelmet / s);
            var r = go.GetComponent<MeshRenderer>();
            var sh = Mats.LitShader;
            if (sh != null) r.sharedMaterial = new Material(sh) { name = "PovHeadShadow" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
            go.SetActive(false);
            povShadow = go.transform;
        }

        // where the head would be: turned with the head bone, whatever size the bone is now
        void PlacePovShadow(Transform head)
        {
            if (povShadow != null) povShadow.position = head.position + head.rotation * povShadowAt;
        }
    }
}
