using UnityEngine;
using UnityEngine.Rendering;

namespace Summit
{
    /// <summary>
    /// A soft glow behind the rider, so the hero reads against the scene in the dark: a small shadowless light
    /// just beyond the body from the camera (it rims the silhouette and lays a faint pool on the snow behind),
    /// and a faint warm halo the body itself hides, so it shows only round the edges. It follows the rider,
    /// the ragdoll's middle after a fall and the walker in the camp. DayNight sets its strength: barely there by
    /// day, strongest at night. One point light and one quad: cheap.
    /// </summary>
    public class RiderGlow : MonoBehaviour
    {
        RiderBody body;
        Light rim;
        Transform halo;
        Material haloMat;
        static readonly Color Warm = new Color(1f, 0.86f, 0.66f);

        public static RiderGlow Build(RiderBody b, Transform parent)
        {
            var g = new GameObject("RiderGlow").AddComponent<RiderGlow>();
            g.transform.SetParent(parent, false);
            g.body = b;
            g.rim = new GameObject("RimLight").AddComponent<Light>();
            g.rim.transform.SetParent(g.transform, false);
            g.rim.type = LightType.Point; g.rim.color = Warm; g.rim.range = 4f; g.rim.shadows = LightShadows.None;
            g.rim.enabled = false;

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            q.name = "Halo"; q.transform.SetParent(g.transform, false);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            g.haloMat = new Material(sh) { name = "RiderHalo" };
            g.haloMat.SetTexture("_BaseMap", Soft());
            g.haloMat.SetFloat("_Surface", 1f); g.haloMat.SetFloat("_Blend", 2f);   // additive
            g.haloMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); g.haloMat.SetFloat("_DstBlend", (float)BlendMode.One);
            g.haloMat.SetFloat("_ZWrite", 0f);
            g.haloMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            g.haloMat.renderQueue = (int)RenderQueue.Transparent;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = g.haloMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            g.halo = q.transform;
            g.halo.gameObject.SetActive(false);
            return g;
        }

        /// <summary>A soft round spot, brightest in the middle and gone well before the edge.</summary>
        static Texture2D Soft()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a); a *= a;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>Put the glow where the body is now, at strength k (0 off .. 1 full night glow).</summary>
        public void Step(float k)
        {
            var cam = Camera.main;
            bool on = k > 0.01f && body != null && cam != null && body.gameObject.activeInHierarchy;
            rim.enabled = on; halo.gameObject.SetActive(on);
            if (!on) return;
            // the middle of the body: the ragdoll's hips after a fall, otherwise about chest height over the feet
            Vector3 c = body.RagdollCentre;
            c += c == body.transform.position ? Vector3.up * 1.05f : Vector3.up * 0.25f;
            Vector3 away = c - cam.transform.position;
            float dist = away.magnitude;
            away = dist > 1e-3f ? away / dist : Vector3.forward;
            Vector3 flat = new Vector3(away.x, 0f, away.z).normalized;
            // the light a metre beyond the body and a little up, so it catches the far side and the edges we see
            rim.transform.position = c + flat * 1.0f + Vector3.up * 0.35f;
            rim.intensity = 3.4f * k;
            // the halo behind the body, facing the camera, as wide as a person is tall
            halo.position = c + away * 0.55f;
            halo.rotation = Quaternion.LookRotation(away, Vector3.up);
            halo.localScale = Vector3.one * 3.2f;
            haloMat.SetColor("_BaseColor", new Color(Warm.r, Warm.g, Warm.b, 0.34f * k));
        }
    }
}
