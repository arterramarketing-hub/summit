using System.Text;
using UnityEditor;
using UnityEngine;
using Summit;

/// <summary>What the hero model is made of, for the people code: its meshes, bones and the clip poses' key points.</summary>
public static class PeopleProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        string which = System.Environment.GetEnvironmentVariable("SUMMIT_PROBE_MODEL") ?? "Hero";
        var prefab = Resources.Load<GameObject>("Rider/" + which);
        var table = Resources.Load<TextAsset>("Rider/HeroAnim");
        var go = Object.Instantiate(prefab);
        sb.AppendLine($"root scale {go.transform.localScale} rot {go.transform.localRotation.eulerAngles}");
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var m = smr.sharedMesh;
            sb.AppendLine($"SMR {smr.name} mesh {m.name} verts {m.vertexCount} tris {m.triangles.Length / 3} subs {m.subMeshCount} bones {smr.bones.Length} root {smr.rootBone?.name} mats {string.Join(",", System.Array.ConvertAll(smr.sharedMaterials, x => x != null ? x.name : "null"))} blend {m.blendShapeCount} uv2 {m.uv2?.Length} col {m.colors?.Length}");
        }
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var m = mr.GetComponent<MeshFilter>().sharedMesh;
            sb.AppendLine($"MR {mr.name} parent {mr.transform.parent?.name} verts {m.vertexCount} tris {m.triangles.Length / 3} mats {string.Join(",", System.Array.ConvertAll(mr.sharedMaterials, x => x != null ? x.name : "null"))}");
        }
        var h = new HeroRig(go.transform, table);
        sb.AppendLine($"rig ok {h.Ok} clips {h.ClipCount}");
        foreach (var t in go.GetComponentsInChildren<Transform>())
            sb.AppendLine($"T {t.name} <- {t.parent?.name} pos {go.transform.InverseTransformPoint(t.position).ToString("F3")} lscale {t.localScale.ToString("F3")} right {go.transform.InverseTransformDirection(t.right).ToString("F2")} up {go.transform.InverseTransformDirection(t.up).ToString("F2")} fwd {go.transform.InverseTransformDirection(t.forward).ToString("F2")}");
        // the gear's uv swatches in use
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var uv = smr.sharedMesh.uv;
            var cells = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var u in uv) { int c = Mathf.FloorToInt(u.x * 8f) + 8 * Mathf.FloorToInt(u.y * 8f); cells[c] = cells.TryGetValue(c, out var n) ? n + 1 : 1; }
            var s = new StringBuilder();
            foreach (var kv in cells) s.Append(kv.Key).Append(':').Append(kv.Value).Append(' ');
            var b = smr.sharedMesh.bounds;
            sb.AppendLine($"UVCELLS {smr.name} {s} bounds {b.center.ToString("F3")} {b.size.ToString("F3")} uvrange x {Min(uv, 0)}..{Max(uv, 0)} y {Min(uv, 1)}..{Max(uv, 1)}");
        }
        foreach (var clip in new[] { "Stand", "Walk", "Run", "Sit", "Sled_Idle", "Ski_Idle", "Ride_Idle" })
        {
            int id = h.Id(clip);
            if (id < 0) { sb.AppendLine($"CLIP {clip} missing"); continue; }
            for (int k = 0; k < 4; k++)
            {
                h.Begin(); h.Want(id, 1f); h.At(id, k / 4f); h.Snap(); h.Evaluate(0f);
                Vector3 P(Transform t) => go.transform.InverseTransformPoint(t.position);
                sb.AppendLine($"CLIP {clip} t{k / 4f:0.00} len {h.Length(id):0.00} root {P(h.Root).ToString("F3")} hips {P(h.Hips).ToString("F3")} head {P(h.Head).ToString("F3")} footL {P(h.Foot[0]).ToString("F3")} footR {P(h.Foot[1]).ToString("F3")} kneeL {P(h.LowerLeg[0]).ToString("F3")} handL {P(h.Hand[0]).ToString("F3")} handR {P(h.Hand[1]).ToString("F3")}");
            }
        }
        System.IO.File.WriteAllText("Logs/people-probe-" + which + ".txt", sb.ToString());
        Debug.Log("PROBE written");
        EditorApplication.Exit(0);
    }
    static float Min(Vector2[] a, int k) { float m = 9f; foreach (var v in a) m = Mathf.Min(m, v[k]); return m; }
    static float Max(Vector2[] a, int k) { float m = -9f; foreach (var v in a) m = Mathf.Max(m, v[k]); return m; }
}
