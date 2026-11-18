using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the hero's clips (Assets/Summit/Art/Hero/HeroAnims.fbx, made by Tools/MakeHero.py) into the small
/// table the game plays them from (Resources/Rider/HeroAnim.bytes, read by Summit.HeroRig): every keyed frame
/// of every clip, each bone's local rotation and the hips' position. Unity does the axis conversion when it
/// imports the FBX, so sampling the imported clips onto the imported skeleton gives rotations in exactly the
/// frames the game's hero has. Run after re-exporting from Blender:
///   Summit > Bake Hero Animations,  or  -executeMethod HeroBake.Bake
/// </summary>
public static class HeroBake
{
    const string AnimPath = "Assets/Summit/Art/Hero/HeroAnims.fbx";
    const string JsonPath = "Assets/Summit/Art/Hero/HeroClips.json";
    const string RiderPath = "Assets/Summit/Resources/Rider/Hero.fbx";
    const string OutPath = "Assets/Summit/Resources/Rider/HeroAnim.bytes";

    [System.Serializable] class ClipInfo { public string name; public int frames; public bool loop; }
    [System.Serializable] class Meta { public int fps; public List<ClipInfo> clips; }

    [MenuItem("Summit/Bake Hero Animations")]
    public static void Bake()
    {
        AssetDatabase.ImportAsset(AnimPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(RiderPath, ImportAssetOptions.ForceUpdate);
        var meta = JsonUtility.FromJson<Meta>(File.ReadAllText(JsonPath));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimPath);
        var rider = AssetDatabase.LoadAssetAtPath<GameObject>(RiderPath);
        var clips = AssetDatabase.LoadAllAssetsAtPath(AnimPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        if (prefab == null || rider == null || clips.Count == 0) { Debug.LogError($"HEROBAKE missing input: anims={prefab} rider={rider} clips={clips.Count}"); return; }

        var go = Object.Instantiate(prefab);
        var riderGo = Object.Instantiate(rider);
        var bones = new List<Transform>();
        var riderBones = new Dictionary<string, Transform>();
        foreach (var t in riderGo.GetComponentsInChildren<Transform>(true)) riderBones[t.name] = t;
        // the skeleton's bones: everything under the Root bone (the same names in both files)
        Transform root = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root");
        if (root == null) { Debug.LogError("HEROBAKE no Root bone"); return; }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) bones.Add(t);
        // the two imports must agree on every bone's local frame, or the rotations would land in the wrong frames:
        // each bone's offset from its parent (which no clip moves, except the hips') must be the same in both
        float worst = 0f; string worstName = "";
        var rest = bones.Select(b => b.localRotation).ToArray();
        for (int i = 0; i < bones.Count; i++)
            if (riderBones.TryGetValue(bones[i].name, out var rb))
            {
                if (bones[i].name == "Hips") continue;
                float a = (rb.localPosition - bones[i].localPosition).magnitude;
                if (a > worst) { worst = a; worstName = bones[i].name; }
            }
            else Debug.LogWarning("HEROBAKE bone not on the rider: " + bones[i].name);
        Debug.Log($"HEROBAKE {bones.Count} bones; their offsets from their parents differ by at most {worst:0.00000} ({worstName})");
        int hips = bones.FindIndex(b => b.name == "Hips");

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(0x4F524548);   // "HERO"
            w.Write(1);
            w.Write(bones.Count);
            foreach (var b in bones) w.Write(b.name);
            var written = new List<string>();
            foreach (var info in meta.clips)
            {
                var clip = clips.FirstOrDefault(c => c.name == info.name || c.name.EndsWith("|" + info.name));
                if (clip == null) { Debug.LogError("HEROBAKE clip missing from the FBX: " + info.name); continue; }
                int samples = info.frames + 1;
                w.Write(info.name); w.Write(samples); w.Write(info.frames); w.Write(info.loop); w.Write((float)meta.fps);
                float moved = 0f;
                for (int f = 0; f < samples; f++)
                {
                    for (int i = 0; i < bones.Count; i++) { bones[i].localRotation = rest[i]; }
                    clip.SampleAnimation(go, f / (float)meta.fps);
                    for (int i = 0; i < bones.Count; i++)
                    {
                        var q = bones[i].localRotation;
                        moved = Mathf.Max(moved, Quaternion.Angle(q, rest[i]));
                        float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                        w.Write((short)Mathf.RoundToInt(q.x / n * 32767f)); w.Write((short)Mathf.RoundToInt(q.y / n * 32767f));
                        w.Write((short)Mathf.RoundToInt(q.z / n * 32767f)); w.Write((short)Mathf.RoundToInt(q.w / n * 32767f));
                    }
                    var hp = bones[hips].localPosition;
                    w.Write(hp.x); w.Write(hp.y); w.Write(hp.z);
                }
                written.Add($"{info.name}({samples}{(info.loop ? " loop" : "")}, {moved:0}deg)");
            }
            w.Flush();
            File.WriteAllBytes(OutPath, ms.ToArray());
            Debug.Log($"HEROBAKE wrote {OutPath}: {ms.Length / 1024} KB, {written.Count} clips: " + string.Join(", ", written));
        }
        Object.DestroyImmediate(go); Object.DestroyImmediate(riderGo);
        AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
    }
}
