using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class SpecialItemBackdrop : MaskableGraphic
    {
        static readonly Color32[] Tones =
        {
            new Color32(192, 75, 139, 115), new Color32(207, 137, 62, 115), new Color32(72, 165, 140, 115),
            new Color32(72, 130, 202, 115), new Color32(144, 83, 193, 115)
        };
        int frame = -1;

        void Update()
        {
            if (canvasRenderer.cull) return;
            int next = Mathf.FloorToInt(Time.unscaledTime * 12f);
            if (frame == next) return;
            frame = next;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            const int columns = 7, rows = 3;
            var bounds = GetPixelAdjustedRect();
            float t = (frame / 12f) * .24f;
            mesh.Clear();
            for (int row = 0; row <= rows; row++)
                for (int col = 0; col <= columns; col++)
                {
                    float x = col / (float)columns, y = row / (float)rows;
                    float phase = Mathf.Repeat(t + 2.7f * x + .75f * y + .30f * Mathf.Sin((y + x) * 4f - t), Tones.Length);
                    int first = (int)phase;
                    float blend = Mathf.SmoothStep(0f, 1f, phase - first);
                    var tone = Color32.Lerp(Tones[first % Tones.Length], Tones[(first + 1) % Tones.Length], blend);
                    mesh.AddVert(new Vector3(bounds.xMin + x * bounds.width, bounds.yMin + y * bounds.height), tone, Vector2.zero);
                    if (row == 0 || col == 0) continue;
                    int corner = row * (columns + 1) + col;
                    int prior = corner - columns - 1;
                    mesh.AddTriangle(prior - 1, corner - 1, corner);
                    mesh.AddTriangle(prior - 1, corner, prior);
                }
        }
    }
}
