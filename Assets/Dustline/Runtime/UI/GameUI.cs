using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI : MonoBehaviour
    {
        enum ScreenPage { Lobby, Stash, Operator, MenuSettings, Loading, Field, FieldInventory, FieldSettings, Results, Error }

        ScreenPage current;
        public bool InventoryOpen => current == ScreenPage.FieldInventory;
        public bool BlocksGameplay => current != ScreenPage.Field;
        GameSession session;
        Font font;
        RaidUiIcons icons;
        RectTransform safe, page, panelRoot, hud, touch, toastAnchor;
        Canvas uiCanvas;
        OperatorPreview operatorPreview;
        Text toast, fpsReadout;
        float toastUntil, fpsSeconds, sampledFps;
        int fpsFrames;
        Rect lastSafe;
        Vector2Int lastScreen;
        static readonly Color Ink = new Color32(13, 20, 24, 255);
        static readonly Color Surface = new Color32(22, 31, 36, 250);
        static readonly Color Plate = new Color32(33, 47, 53, 255);
        static readonly Color Border = new Color32(52, 73, 79, 255);
        static readonly Color Pale = new Color32(239, 240, 229, 255);
        static readonly Color Muted = new Color32(169, 192, 190, 255);
        static readonly Color Amber = new Color32(229, 174, 91, 255);
        static readonly Color Aqua = new Color32(111, 195, 177, 255);

        public void Initialize(GameSession owner)
        {
            session = owner;
            font = Resources.Load<Font>("Dustline/Fonts/NotoSansSC-Regular");
            icons = new RaidUiIcons();
            var canvasObject = new GameObject("Dustline UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            uiCanvas = canvasObject.GetComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safe = Stretch("Safe area", canvasObject.transform);
            hud = Stretch("HUD", safe);
            BuildHud();
            touch = Stretch("Touch controls", safe);
            BuildTouch();
            page = Stretch("Pages", safe);
            toastAnchor = Anchor("Toast anchor", safe, 0.5f, 0);
            var toastPanel = At("Toast panel", toastAnchor, -290, -101, 580, 71);
            Fill(toastPanel, new Color32(12, 19, 23, 244));
            Fill(At("Toast rule", toastPanel, 0, 0, 3, 71), Amber);
            toast = Label(toastPanel, "", 18, 16, 4, 548, 62, TextAnchor.MiddleCenter);
            toastAnchor.gameObject.SetActive(false);
            var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            UpdateSafeArea();
        }

        public void ShowMenu()
        {
            var panel = Display(ScreenPage.Lobby, "Operations desk");
            Header(panel, "行动大厅", "01 / 今日任务");
            var left = At("Deployment summary", panel, 26, 132, 410, 471);
            Fill(left, Ink);
            Fill(At("Brief accent", left, 0, 0, 3, 99), Amber);
            Label(left, "尘线：撤离行动", 35, 22, 24, 368, 59);
            Label(left, "当前区域 / 北岬货运港", 19, 22, 85, 368, 33, color: Amber);
            Label(left, "沿货运港的铁路与堆场搜索物资，在突袭结束前找到撤离点。", 20, 22, 132, 354, 82, color: Muted);
            Fill(At("Summary divider", left, 22, 237, 366, 1), Border);
            string equipped = ItemName(session.Loadout.Equipped(EquipmentSlot.Weapon), "未装备武器");
            Label(left, "当前整备", 15, 22, 254, 340, 27, color: Muted);
            Label(left, equipped, 25, 22, 286, 366, 43);
            if (!string.IsNullOrEmpty(session.Notice))
                Label(left, session.Notice, 17, 22, 340, 366, 83, color: Amber);
            DrawBriefingMap(panel);
            Button(panel, "开始突袭", 26, 615, 195, 49, session.StartRaid, true);
            Button(panel, "仓库", 231, 615, 100, 49, ShowStash);
            Button(panel, "设置", 341, 615, 95, 49, () => ShowSettings(false));
            Button(panel, "角色", 446, 615, 100, 49, ShowOperator);
            Label(panel, "本地突袭  /  14 名电脑对手  /  15 分钟", 16, 566, 618, 627, 43, TextAnchor.MiddleRight, Muted);
        }

        void DrawBriefingMap(RectTransform parent)
        {
            var field = At("District briefing", parent, 460, 132, 752, 471);
            Fill(field, Plate);
            Fill(At("Schematic rule", field, 0, 0, 752, 2), Aqua);
            Label(field, "北岬货运港", 28, 19, 15, 420, 41);
            Label(field, "02 / 区域示意", 17, 445, 22, 285, 34, TextAnchor.UpperRight, Muted);
            var map = At("Overview grid", field, 20, 65, 712, 349);
            Fill(map, new Color32(17, 28, 33, 255));
            Color grid = new Color32(37, 57, 61, 255);
            for (int x = 0; x <= 8; x++) Fill(At("Grid column", map, 174 + x * 43, 0, 1, 349), grid);
            for (int y = 0; y <= 8; y++) Fill(At("Grid row", map, 174, y * 43, 345, 1), grid);
            Label(map, "北", 17, 23, 13, 34, 24, color: Muted);
            Stroke(map, "North axis", new Vector2(40, 49), new Vector2(40, 82), 2, Muted);
            Stroke(map, "North pointer", new Vector2(35, 55), new Vector2(40, 49), 2, Muted);
            Stroke(map, "North pointer", new Vector2(40, 49), new Vector2(45, 55), 2, Muted);
            Vector2 Point(Vector3 position) => new Vector2(346 + position.x * (344 / PortLayout.Size), 173 - position.z * (344 / PortLayout.Size));
            var districts = PortLayout.Districts;
            for (int i = 0; i < districts.Count; i++)
            {
                var district = districts[i];
                Vector2 p = Point(district.Center);
                Fill(At("District block", map, p.x - 17, p.y - 13, 34, 26), new Color32(44, 68, 71, 255));
                Fill(At("District center", map, p.x - 3, p.y - 3, 6, 6), Muted);
                Label(map, district.Name, 16, p.x - 57, p.y + 10, 114, 28, TextAnchor.MiddleCenter);
            }
            var spawn = Point(PortLayout.Spawn);
            Fill(At("Spawn mark", map, spawn.x - 4, spawn.y - 4, 8, 8), Amber);
            Label(map, "入口", 15, spawn.x - 73, spawn.y - 13, 63, 27, TextAnchor.MiddleRight, Amber);
            foreach (var exit in PortLayout.Exits)
            {
                Vector2 p = Point(exit.Position);
                Fill(At("Exit mark", map, p.x - 4, p.y - 4, 8, 8), Aqua);
                Label(map, exit.Name, 15, p.x + 10, p.y - 13, 149, 27, TextAnchor.MiddleLeft, Aqua);
            }
            Label(field, "960 × 960 米    /    五处货运分区    /    双撤离点", 17, 20, 428, 710, 31, color: Muted);
        }

        void ShowOperator()
        {
            var panel = Display(ScreenPage.Operator, "Operator selection");
            Header(panel, "角色整备", "行动大厅 / 外观");
            var showcase = At("Operator preview", panel, 26, 130, 494, 455);
            Fill(showcase, Ink);
            Fill(At("Preview rule", showcase, 0, 0, 494, 2), Aqua);
            var target = At("Portrait", showcase, 69, 16, 356, 423).gameObject.AddComponent<RawImage>();
            target.raycastTarget = false;
            operatorPreview = new OperatorPreview(transform, target,
                session.Loadout.Equipped(EquipmentSlot.Weapon) is ItemStack item ? session.Catalog.Find(item.ItemId).Weapon : null);
            operatorPreview.Select(session.SelectedOperator);
            var dossier = At("Operator dossier", panel, 545, 130, 667, 455);
            Fill(dossier, Ink);
            Label(dossier, "行动员外观", 30, 24, 24, 617, 52);
            Label(dossier, "选择角色性别。仅改变外观，不影响生命、负重或移动速度。", 21, 24, 93, 617, 76, color: Muted);
            Button male = null, female = null;
            void UpdateSelection(OperatorStyle style)
            {
                session.SelectOperator(style);
                operatorPreview.Select(session.SelectedOperator);
                StyleButton(male, session.SelectedOperator == OperatorStyle.Male, null);
                StyleButton(female, session.SelectedOperator == OperatorStyle.Female, null);
            }
            male = Button(dossier, "男性", 24, 183, 297, 64, () => UpdateSelection(OperatorStyle.Male));
            female = Button(dossier, "女性", 345, 183, 297, 64, () => UpdateSelection(OperatorStyle.Female));
            UpdateSelection(session.SelectedOperator);
            Label(dossier, "外观选择会保存在本机。", 17, 24, 277, 617, 55, color: Aqua);
            Button(panel, "返回大厅", 1014, 615, 198, 49, ShowMenu, true);
        }

        void ShowSettings(bool field)
        {
            var panel = Display(field ? ScreenPage.FieldSettings : ScreenPage.MenuSettings, "Display settings");
            Header(panel, "设置", field ? "突袭 / 显示与触控" : "大厅 / 显示与触控");
            var body = At("Frame settings", panel, 26, 116, 1186, 492);
            Fill(body, Ink);
            Fill(At("Section rule", body, 0, 0, 1186, 2), Amber);
            Label(body, "局内帧率", 29, 24, 12, 1112, 32);
            Label(body, "切换立即生效。实际帧率受设备、画面负载和系统调度影响。", 19, 24, 60, 1112, 37, color: Muted);
            var choices = new Button[RaidFrameRate.Options.Count];
            for (int i = 0; i < choices.Length; i++)
            {
                int value = RaidFrameRate.Options[i];
                choices[i] = Button(body, value == RaidFrameRate.Uncapped ? "不限帧" : value + " 帧", 24 + i * 225, 50, 208, 45, () =>
                {
                    session.SelectFrameRate(value);
                    UpdateFrameChoices(choices);
                    UpdateFpsText();
                });
            }
            UpdateFrameChoices(choices);
            Label(body, Application.isMobilePlatform
                ? "安卓的不限帧使用当前屏幕刷新率。固定档位可能被系统匹配为更低的刷新率分频。"
                : "不限帧会关闭游戏帧率上限与垂直同步。",
                17, 24, 102, 1100, 32, color: Muted);
            fpsReadout = Label(body, "", 20, 24, 138, 1100, 32, color: Aqua);
            UpdateFpsText();
            BuildDisplaySettings(body);
            BuildSprintSetting(body);
            if (!field && !Application.isMobilePlatform)
            {
                var preview = Button(panel, session.Input.ForceTouch ? "触控预览：开启" : "触控预览：关闭", 26, 615, 233, 49, null);
                preview.onClick.AddListener(() =>
                {
                    session.Input.ForceTouch = !session.Input.ForceTouch;
                    preview.GetComponentInChildren<Text>().text = session.Input.ForceTouch ? "触控预览：开启" : "触控预览：关闭";
                });
            }
            if (field) Label(panel, "设置打开时，突袭不会暂停。", 17, 26, 584, 560, 24, color: Amber);
            Button(panel, field ? "返回突袭" : "返回大厅", 1014, 615, 198, 49, BackOrSettings, true);
        }

        void BuildDisplaySettings(Transform parent)
        {
            Fill(At("Quality divider", parent, 24, 177, 1138, 1), Border);
            Label(parent, "画质", 24, 24, 188, 230, 32);
            var quality = new Button[3];
            string[] labels = { "低", "中", "高" };
            for (int i = 0; i < quality.Length; i++)
            {
                int value = i;
                quality[i] = Button(parent, labels[i], 264 + i * 145, 184, 130, 43, () =>
                {
                    session.SelectQuality(value);
                    for (int j = 0; j < quality.Length; j++) StyleButton(quality[j], session.QualityLevel == j, null);
                });
            }
            for (int i = 0; i < quality.Length; i++) StyleButton(quality[i], session.QualityLevel == i, null);
            Label(parent, "画质档位会调节阴影、材质和后处理的总体质量。", 17, 720, 190, 430, 29, TextAnchor.UpperRight, Muted);
            Label(parent, "渲染分辨率", 24, 24, 250, 230, 32);
            var scales = new[] { 0.65f, 0.80f, 1f };
            var resolution = new Button[scales.Length];
            for (int i = 0; i < scales.Length; i++)
            {
                int value = i;
                resolution[i] = Button(parent, Mathf.RoundToInt(scales[i] * 100) + "%", 264 + i * 145, 246, 130, 43, () =>
                {
                    session.SelectResolution(scales[value]);
                    for (int j = 0; j < resolution.Length; j++) StyleButton(resolution[j], Mathf.Abs(session.ResolutionScale - scales[j]) < 0.01f, null);
                });
            }
            for (int i = 0; i < resolution.Length; i++) StyleButton(resolution[i], Mathf.Abs(session.ResolutionScale - scales[i]) < 0.01f, null);
            Label(parent, "只缩放 3D 渲染，界面保持清晰。", 17, 720, 252, 430, 29, TextAnchor.UpperRight, Muted);
        }

        void BuildSprintSetting(Transform parent)
        {
            Fill(At("Touch settings divider", parent, 24, 310, 1138, 1), Border);
            Label(parent, "冲刺灵敏度", 25, 24, 321, 569, 34);
            Label(parent, "摇杆向前推到设定的幅度时自动冲刺。百分比越低，越容易触发。", 17, 24, 363, 748, 34, color: Muted);
            var readout = Label(parent, "", 23, 805, 321, 339, 34, TextAnchor.UpperRight, Amber);
            var root = At("Sprint threshold", parent, 24, 401, 912, 57);
            Fill(root, Color.clear, true);
            Fill(At("Slider background", root, 13, 28, 886, 9), Border);
            var fillZone = At("Fill area", root, 13, 28, 886, 9);
            var fill = Stretch("Threshold fill", fillZone);
            Fill(fill, Amber);
            var handleZone = At("Handle area", root, 13, 0, 886, 65);
            var handle = Anchor("Slider handle", handleZone, 0, 0.5f);
            handle.sizeDelta = new Vector2(24, 39);
            var grip = Fill(handle, Pale);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.targetGraphic = grip;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = PlayerInputReader.MinSprintThreshold;
            slider.maxValue = PlayerInputReader.MaxSprintThreshold;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(session.Input.SprintThreshold);
            readout.text = "前推 " + session.Input.SprintThreshold + "%";
            slider.onValueChanged.AddListener(value =>
            {
                session.SelectSprintThreshold(Mathf.RoundToInt(value));
                readout.text = "前推 " + session.Input.SprintThreshold + "%";
            });
            Button(parent, "恢复默认", 968, 405, 194, 43, () => slider.value = PlayerInputReader.DefaultSprintThreshold);
        }

        void UpdateFrameChoices(Button[] choices)
        {
            for (int i = 0; i < choices.Length; i++)
                StyleButton(choices[i], RaidFrameRate.Options[i] == session.FrameRate, null);
        }

        void UpdateFpsText()
        {
            if (fpsReadout == null) return;
            int target = Application.targetFrameRate;
            fpsReadout.text = "游戏目标：" + (target < 0 ? "不限帧" : target + " 帧") + "    /    实测：" + (sampledFps > 0 ? Mathf.RoundToInt(sampledFps) + " 帧" : "采样中");
        }

        public void BackOrSettings()
        {
            if (session.Phase == SessionPhase.Menu && (current == ScreenPage.Stash || current == ScreenPage.Operator || current == ScreenPage.MenuSettings)) ShowMenu();
            else if (session.Phase == SessionPhase.Raid)
            {
                if (current == ScreenPage.Field) ShowSettings(true);
                else if (current == ScreenPage.FieldInventory || current == ScreenPage.FieldSettings) EnterField();
            }
        }

        public void ShowLoading()
        {
            var panel = Display(ScreenPage.Loading, "Loading district");
            Header(panel, "正在部署", "北岬货运港");
            Label(panel, "正在准备港区、搜刮点与撤离路线…", 30, 42, 285, 1150, 78, TextAnchor.MiddleCenter);
        }

        public void ShowRaid()
        {
            EnterField();
            Notify(session.Input.TouchMode
                ? "摇杆前推可自动冲刺。右侧拖动转向，容器对准后点放大镜搜索。"
                : "WASD 移动 / Shift 冲刺 / F 搜索 / Q、E 探头 / Tab 背包 / Esc 设置");
        }

        public void ShowResults()
        {
            var panel = Display(ScreenPage.Results, "Raid report");
            Header(panel, "突袭结算", "北岬货运港 / 行动记录");
            var report = At("Debrief plate", panel, 26, 136, 1186, 450);
            Fill(report, Ink);
            Fill(At("Report accent", report, 0, 0, 4, 109), Amber);
            Label(report, session.Result, 25, 24, 27, 1138, 389);
            Button(panel, "返回大厅", 26, 615, 192, 49, session.ReturnToMenu);
            Button(panel, "前往仓库", 1015, 615, 197, 49, () =>
            {
                session.ReturnToMenu();
                if (session.Phase == SessionPhase.Menu) ShowStash();
            }, true);
        }

        public void ShowError()
        {
            var panel = Display(ScreenPage.Error, "Session halted");
            Header(panel, "行动已停止", "运行状态");
            Label(panel, session.Error, 23, 36, 148, 1156, 442);
        }

        void EnterField()
        {
            Switch(ScreenPage.Field);
            UpdateHud(true);
        }

        void Switch(ScreenPage next)
        {
            session.SaveControlPreferences();
            operatorPreview?.Dispose();
            operatorPreview = null;
            Clear(page);
            panelRoot = null;
            fpsReadout = null;
            ResetInventoryView();
            current = next;
            bool field = next == ScreenPage.Field;
            hud.gameObject.SetActive(field);
            touch.gameObject.SetActive(field && session.Input.TouchMode);
            toastAnchor.gameObject.SetActive(false);
            session.SyncControls();
        }

        RectTransform Display(ScreenPage next, string title)
        {
            Switch(next);
            Fill(Stretch("Page scrim", page), new Color32(6, 10, 13, 221), true);
            panelRoot = Anchor(title, page, 0.5f, 0.5f);
            panelRoot.sizeDelta = new Vector2(1240, 680);
            Fill(panelRoot, Surface, true);
            Canvas.ForceUpdateCanvases();
            FitPanel();
            return panelRoot;
        }

        void Header(RectTransform root, string title, string subtitle)
        {
            Fill(At("Header fill", root, 0, 0, 1240, 110), Ink);
            Fill(At("Header highlight", root, 0, 0, 1240, 3), Amber);
            Label(root, title, 35, 26, 23, 785, 57);
            Label(root, subtitle, 16, 26, 78, 770, 25, color: Muted);
            Label(root, "尘线 / " + Application.version, 19, 816, 28, 396, 40, TextAnchor.UpperRight, Amber);
            if (session.Profile != null && session.Phase == SessionPhase.Menu)
                creditsReadout = Label(root, session.Profile.Credits + " 信用点", 19, 853, 73, 359, 30, TextAnchor.UpperRight, Muted);
        }

        void FitPanel()
        {
            if (panelRoot == null) return;
            float scale = Mathf.Min(1, Mathf.Min(safe.rect.width / 1260f, safe.rect.height / 700f));
            panelRoot.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        void UpdateSafeArea()
        {
            lastSafe = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width < 1 || Screen.height < 1) return;
            safe.anchorMin = new Vector2(lastSafe.xMin / Screen.width, lastSafe.yMin / Screen.height);
            safe.anchorMax = new Vector2(lastSafe.xMax / Screen.width, lastSafe.yMax / Screen.height);
            Canvas.ForceUpdateCanvases();
            reticleAnchor.anchoredPosition = (new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) - lastSafe.center) / uiCanvas.scaleFactor;
            FitPanel();
        }

        public void Notify(string message)
        {
            toast.text = message;
            toastUntil = Time.unscaledTime + 4.5f;
            float y = current == ScreenPage.Field ? 117 : 145;
            toastAnchor.GetChild(0).GetComponent<RectTransform>().anchoredPosition = new Vector2(-290, y);
            toastAnchor.gameObject.SetActive(true);
        }

        void Update()
        {
            if (Screen.safeArea != lastSafe || Screen.width != lastScreen.x || Screen.height != lastScreen.y) UpdateSafeArea();
            if (toastAnchor.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastAnchor.gameObject.SetActive(false);
            fpsFrames++;
            fpsSeconds += Time.unscaledDeltaTime;
            if (fpsSeconds >= 0.6f)
            {
                sampledFps = fpsFrames / fpsSeconds;
                fpsFrames = 0;
                fpsSeconds = 0;
                UpdateFpsText();
            }
            if (current == ScreenPage.Field) UpdateHud(false);
        }

        static RectTransform Anchor(string title, Transform parent, float x, float y)
        {
            var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static RectTransform Stretch(string title, Transform parent)
        {
            var rect = Anchor(title, parent, 0, 0);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static RectTransform At(string title, Transform parent, float x, float y, float width, float height)
        {
            var rect = Anchor(title, parent, 0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Image Fill(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static void Stroke(Transform root, string title, Vector2 a, Vector2 b, float width, Color color)
        {
            var d = b - a;
            var line = At(title, root, a.x, a.y, d.magnitude, width);
            line.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            Fill(line, color);
        }

        Text Label(Transform parent, string value, int size, float x, float y, float w, float h,
            TextAnchor alignment = TextAnchor.UpperLeft, Color? color = null)
        {
            var label = At("Label", parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = value;
            label.color = color ?? Pale;
            label.alignment = alignment;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        Button Button(Transform parent, string title, float x, float y, float w, float h, Action action, bool primary = false, Color? background = null)
        {
            var rect = At(title, parent, x, y, w, h);
            var image = Fill(rect, Plate, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (action != null) button.onClick.AddListener(() => action());
            if (!string.IsNullOrEmpty(title))
            {
                var text = Label(rect, title, 18, 5, 2, w - 10, h - 4, TextAnchor.MiddleCenter);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 11;
                text.resizeTextMaxSize = primary ? 22 : 19;
            }
            StyleButton(button, primary, background);
            return button;
        }

        static void StyleButton(Button button, bool primary, Color? background)
        {
            button.targetGraphic.color = background ?? (primary ? Amber : Plate);
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = primary ? Ink : Pale;
            var palette = button.colors;
            palette.normalColor = Color.white;
            palette.highlightedColor = new Color(0.91f, 0.94f, 0.98f);
            palette.pressedColor = new Color(0.68f, 0.75f, 0.78f);
            palette.fadeDuration = 0.08f;
            button.colors = palette;
        }

        static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                child.SetActive(false);
                Destroy(child);
            }
        }

        string ItemName(ItemStack item, string missing) => item == null ? missing : session.Catalog.Find(item.ItemId).DisplayName;
        void OnDestroy()
        {
            operatorPreview?.Dispose();
            icons?.Dispose();
        }
    }
}
