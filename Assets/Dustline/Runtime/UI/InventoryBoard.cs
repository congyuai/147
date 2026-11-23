using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed class InventoryBoard : MaskableGraphic, IPointerClickHandler
    {
        int columns, rows, split;
        internal GridInventory Bag;
        internal CargoArea Area;
        float cell;
        Action<int, int> onCell;
        InventoryFootprint footprint;
        static readonly Color32 EmptyDark = new Color32(29, 40, 46, 255);
        static readonly Color32 EmptyLight = new Color32(36, 49, 54, 255);

        internal void Initialize(int width, int height, float cellSize, Action<int, int> select, int divider = 0)
        {
            columns = width;
            rows = height;
            split = divider;
            cell = cellSize;
            onCell = select;
            SetVerticesDirty();
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && !TouchControl.TouchWasCancelled(data)
                && TryCell(data.position, data.pressEventCamera, out int x, out int y)) onCell?.Invoke(x, y);
        }

        internal bool TryCell(Vector2 screen, Camera eventCamera, out int x, out int y)
        {
            x = y = -1;
            if (cell <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, eventCamera, out var point)) return false;
            Rect bounds = rectTransform.rect;
            x = Mathf.FloorToInt((point.x - bounds.xMin) / cell);
            y = Mathf.FloorToInt((bounds.yMax - point.y) / cell);
            return x >= 0 && x < columns && y >= 0 && y < rows;
        }

        internal void Highlight(int x, int y, int w, int h, bool fits)
        {
            if (footprint == null)
            {
                var overlay = new GameObject("Drop footprint", typeof(RectTransform), typeof(CanvasRenderer));
                var rect = (RectTransform)overlay.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                footprint = overlay.AddComponent<InventoryFootprint>();
                footprint.raycastTarget = false;
            }
            footprint.gameObject.SetActive(true);
            footprint.Paint(Mathf.Max(0, x), Mathf.Max(0, y), Mathf.Min(columns, x + w), Mathf.Min(rows, y + h), cell, fits);
        }

        internal void ClearHighlight()
        {
            if (footprint != null) footprint.gameObject.SetActive(false);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = rectTransform.rect;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    float left = bounds.xMin + x * cell + (split > 0 && split == x ? 4 : 1);
                    float top = bounds.yMax - y * cell - 1;
                    float right = bounds.xMin + (x + 1) * cell - (split > 0 && split == x + 1 ? 4 : 1);
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

    internal sealed class InventoryFootprint : MaskableGraphic
    {
        int left, top, right, bottom;
        float cell;
        Color32 edge, tint;

        internal void Paint(int x1, int y1, int x2, int y2, float size, bool fits)
        {
            left = x1; top = y1; right = x2; bottom = y2; cell = size;
            edge = fits ? new Color32(123, 244, 193, 255) : new Color32(255, 129, 109, 255);
            tint = new Color32(edge.r, edge.g, edge.b, fits ? (byte)86 : (byte)111);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = rectTransform.rect;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    float a = bounds.xMin + x * cell + 1f, b = bounds.yMax - y * cell - 1f;
                    Quad(a, b - cell + 2f, a + cell - 2f, b, tint);
                    Quad(a, b - 2f, a + cell - 2f, b, edge);
                    Quad(a, b - cell + 2f, a + 2f, b - 2f, edge);
                    Quad(a + cell - 4f, b - cell + 2f, a + cell - 2f, b - 2f, edge);
                    Quad(a + 2f, b - cell + 2f, a + cell - 4f, b - cell + 4f, edge);
                }

            void Quad(float x1, float y1, float x2, float y2, Color32 color)
            {
                int i = mesh.currentVertCount;
                mesh.AddVert(new Vector3(x1, y1), color, Vector2.zero);
                mesh.AddVert(new Vector3(x1, y2), color, Vector2.zero);
                mesh.AddVert(new Vector3(x2, y2), color, Vector2.zero);
                mesh.AddVert(new Vector3(x2, y1), color, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2);
                mesh.AddTriangle(i + 2, i + 3, i);
            }
        }
    }
}
