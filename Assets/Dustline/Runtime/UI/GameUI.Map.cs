using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        Texture2D mapTexture;
        MapDisplay miniMap, fullMap;
        Vector2 mapCentre;
        float mapZoom;
        Text mapScaleText;

        static readonly Color MapDanger = new Color32(236, 137, 75, 255);

        sealed class MapDisplay
        {
            internal RectTransform Root, GuardArea;
            internal RawImage Raster;
            internal RectTransform Player, Danger, Spawn;
            internal RectTransform[] Exits, Names;
            internal bool Mini;
            internal Rect View;
        }

        Texture2D MapRaster()
        {
            if (mapTexture != null) return mapTexture;
            const int size = 320;
            const float half = PortLayout.Size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * PortLayout.Size - half, (y + 0.5f) / size * PortLayout.Size - half);
                    Color32 color = PortLayout.MapColor(p);
                    if (x % 40 == 0 || y % 40 == 0) color = Color32.Lerp(color, new Color32(155, 176, 174, 255), 0.17f);
                    pixels[y * size + x] = color;
                }
            mapTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "North Cape terrain chart", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            mapTexture.SetPixels32(pixels);
            mapTexture.Apply(false, true);
            return mapTexture;
        }

        MapDisplay CreateMap(Transform owner, float x, float y, float size, bool mini, bool player)
        {
            var outer = At("Map frame", owner, x, y, size, size);
            Fill(outer, Border);
            var root = At("Map viewport", outer, 2f, 2f, size - 4f, size - 4f);
            root.gameObject.AddComponent<RectMask2D>();
            var raster = Stretch("Map raster", root).gameObject.AddComponent<RawImage>();
            raster.raycastTarget = false;
            var map = new MapDisplay { Root = root, Raster = raster, Mini = mini };
            if (!mini) raster.texture = MapRaster();
            map.GuardArea = Anchor("Guard precinct", root, 0.5f, 0.5f);
            Fill(map.GuardArea, new Color32(232, 113, 52, 91));
            map.Exits = new RectTransform[PortLayout.Exits.Count];
            for (int i = 0; i < map.Exits.Length; i++)
                map.Exits[i] = MapSymbol(root, RaidIcon.Exit, mini ? 20f : 25f, Aqua);
            map.Danger = MapSymbol(root, RaidIcon.Danger, mini ? 20f : 29f, MapDanger);
            map.Spawn = MapSymbol(root, RaidIcon.Knob, mini ? 13f : 20f, Amber);
            map.Player = MapSymbol(root, RaidIcon.Heading, mini ? 23f : 30f, Color.white);
            if (!mini)
            {
                map.Names = new RectTransform[PortLayout.Districts.Count];
                for (int i = 0; i < map.Names.Length; i++)
                    map.Names[i] = Label(root, PortLayout.Districts[i].Name, 14, 0, 0, 124, 22, TextAnchor.MiddleCenter).rectTransform;
            }
            var compass = Label(root, "北", mini ? 14 : 19, 7f, 3f, 36f, 29f, TextAnchor.UpperLeft, Pale);
            compass.gameObject.AddComponent<Outline>().effectColor = Ink;
            UpdateMap(map, new Rect(-PortLayout.Size / 2f, -PortLayout.Size / 2f, PortLayout.Size, PortLayout.Size), player && session.Player != null ? session.Player.transform : null);
            return map;
        }

        RectTransform MapSymbol(Transform parent, RaidIcon icon, float size, Color color)
        {
            var mark = Anchor(icon.ToString(), parent, 0.5f, 0.5f);
            mark.sizeDelta = new Vector2(size, size);
            var graphic = Fill(mark, color);
            graphic.sprite = icons[icon];
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.025f, 0.045f, 0.05f, 0.85f);
            outline.effectDistance = new Vector2(1.1f, -1.1f);
            return mark;
        }

        static Rect MapWindow(Vector2 center, float width)
        {
            float bound = (PortLayout.Size - width) * 0.5f;
            center.x = Mathf.Clamp(center.x, -bound, bound);
            center.y = Mathf.Clamp(center.y, -bound, bound);
            return new Rect(center.x - width / 2f, center.y - width / 2f, width, width);
        }

        void UpdateMap(MapDisplay map, Rect view, Transform player)
        {
            if (map == null) return;
            map.View = view;
            float half = PortLayout.Size * 0.5f;
            map.Raster.uvRect = new Rect((view.xMin + half) / PortLayout.Size, (view.yMin + half) / PortLayout.Size, view.width / PortLayout.Size, view.height / PortLayout.Size);
            Vector2 Project(float x, float z) => new Vector2((x - view.xMin) / view.width, (z - view.yMin) / view.height);
            var guard = PortLayout.HighValueArea;
            map.GuardArea.anchorMin = Project(guard.xMin, guard.yMin);
            map.GuardArea.anchorMax = Project(guard.xMax, guard.yMax);
            map.GuardArea.offsetMin = map.GuardArea.offsetMax = Vector2.zero;
            for (int i = 0; i < map.Exits.Length; i++) Place(map.Exits[i], PortLayout.Exits[i].Position, true);
            Place(map.Danger, PortLayout.HighValueCenter, true);
            Place(map.Spawn, PortLayout.Spawn, false);
            if (map.Names != null)
                for (int i = 0; i < map.Names.Length; i++)
                {
                    var mark = map.Names[i];
                    var p = PortLayout.Districts[i].Center;
                    mark.anchorMin = mark.anchorMax = Project(p.x, p.z);
                    mark.pivot = new Vector2(0.5f, 0.5f);
                    mark.anchoredPosition = new Vector2(0, 16f);
                }
            SetActive(map.Player.gameObject, player != null);
            if (player != null)
            {
                Place(map.Player, player.position, false);
                map.Player.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
            }

            void Place(RectTransform marker, Vector3 pos, bool rim)
            {
                Vector2 p = Project(pos.x, pos.z);
                if (map.Mini && rim)
                {
                    Vector2 ray = p - Vector2.one * 0.5f;
                    float edge = Mathf.Max(Mathf.Abs(ray.x), Mathf.Abs(ray.y));
                    if (edge > 0.42f) p = Vector2.one * 0.5f + ray * (0.42f / edge);
                }
                marker.anchorMin = marker.anchorMax = p;
                marker.anchoredPosition = Vector2.zero;
            }
        }

        void DrawBriefingMap(RectTransform parent)
        {
            var field = At("Port briefing", parent, 460, 132, 752, 471);
            Fill(field, Plate);
            Fill(At("Briefing edge", field, 0, 0, 752, 2), Aqua);
            CreateMap(field, 18, 18, 435, false, false);
            Label(field, "北岬货运港", 27, 475, 24, 258, 44);
            Label(field, "河岸地形 · 双撤离点", 16, 475, 73, 258, 32, color: Muted);
            Label(field, "高价值区", 19, 475, 129, 240, 28, color: MapDanger);
            Label(field, PortLayout.HighValueName + "\n高价值物资由首领和精英看守。", 18, 475, 161, 248, 96, color: Pale);
            Label(field, "撤离点", 19, 475, 279, 240, 28, color: Aqua);
            Label(field, PortLayout.Exits[0].Name + "\n" + PortLayout.Exits[1].Name, 17, 475, 313, 245, 73, color: Pale);
            Label(field, "960 × 960 米 / 北向上", 16, 475, 414, 245, 30, color: Muted);
        }

        public void ToggleMap()
        {
            if (session.Phase != SessionPhase.Raid) return;
            if (current == ScreenPage.FieldMap) EnterField();
            else if (current == ScreenPage.Field) ShowFieldMap();
        }

        void ShowFieldMap()
        {
            var panel = Display(ScreenPage.FieldMap, "Raid map");
            Header(panel, "港区地图", "北向上 / 查看地图不会暂停突袭");
            mapCentre = new Vector2(session.Player.transform.position.x, session.Player.transform.position.z);
            mapZoom = PortLayout.Size;
            fullMap = CreateMap(panel, 47, 121, 478, false, true);
            Fill(fullMap.Root, Color.clear, true);
            var drag = fullMap.Root.gameObject.AddComponent<RaidMapDrag>();
            drag.Dragged = offset =>
            {
                mapCentre -= offset * (mapZoom / fullMap.Root.rect.width);
                UpdateFullMap();
            };
            var side = At("Chart legend", panel, 563, 128, 649, 457);
            Fill(side, Ink);
            Label(side, "航图图例", 30, 23, 16, 570, 46);
            Label(side, "白箭头  /  自己的位置与朝向", 20, 23, 83, 550, 36);
            Label(side, "橙标  /  " + PortLayout.HighValueName + "：高价值区及首领守卫", 19, 23, 129, 592, 59, color: MapDanger);
            for (int i = 0; i < PortLayout.Exits.Count; i++)
                Label(side, "撤离点  /  " + PortLayout.Exits[i].Name, 19, 23, 202 + i * 43, 576, 40, color: Aqua);
            mapScaleText = Label(side, "", 20, 23, 309, 578, 41, color: Muted);
            Button(side, "缩小", 23, 378, 158, 47, () => { mapZoom = Mathf.Min(PortLayout.Size, mapZoom * 1.45f); UpdateFullMap(); });
            Button(side, "放大", 198, 378, 158, 47, () => { mapZoom = Mathf.Max(245f, mapZoom / 1.45f); UpdateFullMap(); });
            Button(side, "定位自己", 372, 378, 245, 47, () =>
            {
                var actor = session.Player.transform.position;
                mapCentre = new Vector2(actor.x, actor.z);
                if (mapZoom >= PortLayout.Size) mapZoom = 440f;
                UpdateFullMap();
            });
            Button(panel, "返回突袭", 1014, 615, 198, 49, EnterField, true);
            UpdateFullMap();
        }

        void UpdateFullMap()
        {
            var view = MapWindow(mapCentre, mapZoom);
            mapCentre = view.center;
            UpdateMap(fullMap, view, session.Player.transform);
            mapScaleText.text = "范围 " + Mathf.RoundToInt(mapZoom) + " 米  /  放大后可拖动地图";
        }

        void UpdateMiniMap()
        {
            if (miniMap == null || session.Player == null) return;
            var actor = session.Player.transform;
            if (miniMap.Raster.texture == null) miniMap.Raster.texture = MapRaster();
            UpdateMap(miniMap, MapWindow(new Vector2(actor.position.x, actor.position.z), 295f), actor);
        }
    }

    sealed class RaidMapDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<Vector2> Dragged;
        int pointer = int.MinValue;
        Vector2 previous;

        public void OnBeginDrag(PointerEventData data)
        {
            if (pointer != int.MinValue || data.button != PointerEventData.InputButton.Left) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out previous))
                pointer = data.pointerId;
        }

        public void OnDrag(PointerEventData data)
        {
            if (data.pointerId != pointer || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out var p)) return;
            Dragged?.Invoke(p - previous);
            previous = p;
        }

        public void OnEndDrag(PointerEventData data) { if (data.pointerId == pointer) pointer = int.MinValue; }
        void OnDisable() { pointer = int.MinValue; }
    }
}
