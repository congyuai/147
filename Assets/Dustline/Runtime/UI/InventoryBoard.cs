using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed class InventoryBoard : MaskableGraphic, IPointerClickHandler
    {
        int columns, rows;
        float cell;
        Action<int, int> onCell;
        static readonly Color32 EmptyDark = new Color32(29, 40, 46, 255);
        static readonly Color32 EmptyLight = new Color32(36, 49, 54, 255);

        internal void Initialize(int width, int height, float cellSize, Action<int, int> select)
        {
            columns = width;
            rows = height;
            cell = cellSize;
            onCell = select;
            SetVerticesDirty();
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || cell <= 0) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out var point)) return;
            Rect bounds = rectTransform.rect;
            int x = Mathf.FloorToInt((point.x - bounds.xMin) / cell);
            int y = Mathf.FloorToInt((bounds.yMax - point.y) / cell);
            if (x >= 0 && x < columns && y >= 0 && y < rows) onCell?.Invoke(x, y);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = rectTransform.rect;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    float left = bounds.xMin + x * cell + 1;
                    float top = bounds.yMax - y * cell - 1;
                    float right = left + cell - 2;
                    float bottom = top - cell + 2;
                    Color32 color = ((x + y) & 1) == 0 ? EmptyDark : EmptyLight;
                    int first = mesh.currentVertCount;
                    mesh.AddVert(new Vector3(left, bottom), color, Vector2.zero);
                    mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
                    mesh.AddVert(new Vector3(right, top), color, Vector2.zero);
                    mesh.AddVert(new Vector3(right, bottom), color, Vector2.zero);
                    mesh.AddTriangle(first, first + 1, first + 2);
                    mesh.AddTriangle(first + 2, first + 3, first);
                }
        }
    }
}
