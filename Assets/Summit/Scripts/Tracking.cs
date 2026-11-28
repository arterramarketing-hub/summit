using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Summit
{
    /// <summary>
    /// Letter spacing for uGUI's Text, which has none: each letter's quad moves along by `em` of the type's size for
    /// every letter before it on its line, and the line is moved back to keep its alignment. A gap between words gets
    /// the spacing twice, as a word space would.
    /// </summary>
    [DisallowMultipleComponent]
    public class Tracking : BaseMeshEffect
    {
        [SerializeField] float _em;
        public float em { get => _em; set { if (Mathf.Approximately(_em, value)) return; _em = value; if (graphic != null) graphic.SetVerticesDirty(); } }

        static readonly List<UIVertex> verts = new List<UIVertex>();
        static readonly List<int> lineStart = new List<int>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(_em, 0f)) return;
            var text = graphic as Text;
            if (text == null) return;
            verts.Clear();
            vh.GetUIVertexStream(verts);
            int quads = verts.Count / 6;
            if (quads == 0) return;
            float px = text.fontSize * _em;
            int col = (int)text.alignment % 3;   // 0 left, 1 centre, 2 right
            float lineH = text.fontSize * 0.5f;
            // walk the quads in order: a new line where the type drops a line; a word gap where the letters stand apart
            int start = 0; float prevY = float.NaN, prevRight = float.NaN, shift = 0f;
            for (int q = 0; q <= quads; q++)
            {
                bool end = q == quads;
                float cy = 0f, left = 0f, right = 0f;
                if (!end)
                {
                    float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                    for (int k = 0; k < 6; k++) { var p = verts[q * 6 + k].position; minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
                    cy = (minY + maxY) * 0.5f; left = minX; right = maxX;
                    if (maxX - minX < 0.01f) continue;   // (an empty quad: a space, where a space still makes one)
                }
                bool newLine = end || (!float.IsNaN(prevY) && cy < prevY - lineH);
                if (newLine && q > start)
                {
                    // the line from `start` to here: pull it back for its alignment (the spacing after the last letter doesn't count)
                    float back = col == 1 ? (shift - px) * 0.5f : col == 2 ? shift - px : 0f;
                    for (int i = start * 6; i < q * 6; i++) { var v = verts[i]; v.position.x -= back; verts[i] = v; }
                    start = q; shift = 0f; prevRight = float.NaN;
                }
                if (end) break;
                if (!float.IsNaN(prevRight) && left - prevRight > text.fontSize * 0.18f) shift += px;   // a word gap
                for (int k = 0; k < 6; k++) { var v = verts[q * 6 + k]; v.position.x += shift; verts[q * 6 + k] = v; }
                shift += px;
                prevY = cy; prevRight = right;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
        }
    }
}
