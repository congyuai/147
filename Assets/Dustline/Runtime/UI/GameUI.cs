using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI : MonoBehaviour
    {
        enum ScreenPage { Lobby, Stash, Shop, Operator, Gunsmith, MenuSettings, Loading, Field, FieldInventory, FieldMap, FieldSettings, Health, ExitConfirm, PracticePanel, TouchLayout, Results, Error, Strategy, Preparation, Market }

        ScreenPage current;
        readonly Stack<Action> backRoutes = new Stack<Action>();
        Action pageRestore, capturePageState;
        SessionPhase routePhase;
        bool restoringRoute;
        int backHandledFrame = -1;
        public bool InventoryOpen => current == ScreenPage.FieldInventory;
        public bool BlocksGameplay => current != ScreenPage.Field || MedicineWheelOpen;
        GameSession session;
        Font font;
        RaidUiIcons icons;
        RectTransform safe, page, panelRoot, hud, touch, toastAnchor;
        Canvas uiCanvas;
        OperatorPreview operatorPreview;
        Action reflowWidePage;
        bool widePage;
        Text toast, fpsReadout;
        float toastUntil, fpsSeconds, sampledFps;
        int fpsFrames;
        bool skipFrameSample;
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
            session.Input.GestureUI = this;
            font = Resources.Load<Font>("Dustline/Fonts/NotoSansSC-Regular");
            icons = new RaidUiIcons();
            itemIcons = new ItemIconCache(transform);
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
            BuildWeaponBar();
            page = Stretch("Pages", safe);
            toastAnchor = Anchor("Toast anchor", safe, 0.5f, 0);
            var toastPanel = At("Toast panel", toastAnchor, -290, -101, 580, 71);
            Fill(toastPanel, new Color32(12, 19, 23, 244));
            Fill(At("Toast rule", toastPanel, 0, 0, 3, 71), Amber);
            toast = Label(toastPanel, "", 18, 16, 4, 548, 62, TextAnchor.MiddleCenter);
            toastAnchor.gameObject.SetActive(false);
            var events = new GameObject("Event System", typeof(DustlineEventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<DustlineEventSystem>().BeforeInput = CheckSafeArea;
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            weaponBar.gameObject.SetActive(false);
            UpdateSafeArea();
        }

        public void ShowMenu()
        {
            var panel = Display(ScreenPage.Lobby, "Operations desk", wide: true);
            reflowWidePage = Draw;
            Draw();

            void Draw()
            {
                operatorPreview?.Dispose();
                ClearContent(panel);
                float w = panel.rect.width;
                WideHeader(panel, "尘线：行动大厅", "行动员等级 " + session.PlayerLevel + "   /   本地突袭", true);
                const float nav = 162;
                Fill(At("Lobby navigation", panel, 12, 58, nav, 609), Ink);
                Button(panel, "仓库", 20, 78, nav - 16, 55, ShowStash);
                Button(panel, "商店", 20, 142, nav - 16, 55, () => ShowShop());
                Button(panel, "市场", 20, 206, nav - 16, 55, ShowMarket);
                Button(panel, "枪械改装", 20, 280, nav - 16, 48, () => ShowGunsmith());
                Button(panel, "离线靶场", 20, 336, nav - 16, 48, session.StartPractice);
                Button(panel, "角色", 20, 560, nav - 16, 43, ShowOperator);
                Button(panel, "设置", 20, 611, nav - 16, 43, () => ShowSettings(false));
                float portraitWidth = Mathf.Min(w * .44f, 560f);
                var portrait = At("Lobby operator", panel, (w - portraitWidth) * .5f, 56, portraitWidth, 605);
                BuildPortrait(portrait);
                Button(portrait, "切换正背面", (portraitWidth - 146) * .5f, 551, 146, 38, () => operatorPreview?.RotateView());
                float sideW = Mathf.Clamp(w * .24f, 215f, 310f);
                float x = w - sideW - 14;
                var summary = At("Deployment summary", panel, x, 124, sideW, 351);
                Fill(summary, Ink);
                Label(summary, "当前穿戴", 25, 11, 12, sideW - 22, 36, color: Amber);
                DrawPreparedSummary(summary, sideW);
                if (!string.IsNullOrEmpty(session.Notice))
                    Label(panel, session.Notice, 15, x, 487, sideW, 57, color: Amber);
                bool ready = Deployment.TryDifficulty(session.PreparedSite, session.PreparedTier, out var tier);
                if (ready) Button(panel, Deployment.Sites[session.PreparedSite].Name + " / " + tier.Name, x, 550, sideW, 44, OpenStrategicMap);
                Button(panel, ready ? "出发" : "开始游戏", x, 603, sideW, 56, () =>
                {
                    if (ready) session.StartRaid(); else OpenStrategicMap();
                }, true);
            }
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
            operatorPreview = new OperatorPreview(transform, target, session.BoundBlade);
            operatorPreview.Select(session.SelectedOperator);
            operatorPreview.SyncGear(session.Loadout);
            var dossier = At("Operator dossier", panel, 545, 130, 667, 455);
            Fill(dossier, Ink);
            Label(dossier, "行动员外观", 30, 24, 24, 617, 52);
            Label(dossier, "选择角色性别。仅改变外观，不影响生命、负重或移动速度。", 21, 24, 93, 617, 76, color: Muted);
            Button male = null, female = null;
            void UpdateSelection(OperatorStyle style)
            {
                session.SelectOperator(style);
                operatorPreview.Select(session.SelectedOperator);
                operatorPreview.SyncGear(session.Loadout);
                StyleButton(male, session.SelectedOperator == OperatorStyle.Male, null);
                StyleButton(female, session.SelectedOperator == OperatorStyle.Female, null);
            }
            male = Button(dossier, "男性", 24, 183, 297, 64, () => UpdateSelection(OperatorStyle.Male));
            female = Button(dossier, "女性", 345, 183, 297, 64, () => UpdateSelection(OperatorStyle.Female));
            UpdateSelection(session.SelectedOperator);
            Label(dossier, "随身刀", 24, 24, 266, 617, 37, color: Amber);
            int bladeIndex = 0;
            foreach (var option in session.Blades.Blades)
            {
                if (option == null || option.Weapon == null) continue;
                string id = option.Id;
                bool active = id == session.BoundBladeId;
                var blade = Button(dossier, option.DisplayName, 24 + (bladeIndex++) * 321, 314, 296, 54, () =>
                {
                    if (session.SelectBlade(id)) ShowOperator();
                }, active);
                DrawItemIcon(blade.transform, null, null, 8, 5, 54, 44, blade: option.Weapon.Form);
            }
            Label(dossier, "随身刀不可卸下、出售，不因阵亡丢失。", 17, 24, 388, 617, 49, color: Aqua);
        }

        void ShowSettings(bool field, bool controls = false)
        {
            var panel = Display(field ? ScreenPage.FieldSettings : ScreenPage.MenuSettings, "Game settings");
            SetPageRestore(() => ShowSettings(field, controls));
            bool practice = field && session.Phase == SessionPhase.Practice;
            Header(panel, "设置", practice ? "离线靶场 / 本机偏好" : field ? "突袭 / 设置不会暂停游戏" : "大厅 / 本机偏好");
            Button(panel, "画面", 26, 120, 188, 44, () => ShowSettings(field), !controls);
            Button(panel, "操控", 225, 120, 188, 44, () => ShowSettings(field, true), controls);
            var body = At("Settings options", panel, 26, 176, 1186, 424);
            Fill(body, Ink);
            Fill(At("Section rule", body, 0, 0, 1186, 2), Amber);
            if (controls) BuildControlSettings(body, field);
            else BuildVisualOptions(body);
            if (!field && !Application.isMobilePlatform)
            {
                var preview = Button(panel, session.Input.ForceTouch ? "触控预览：开启" : "触控预览：关闭", 26, 615, 233, 49, null);
                preview.onClick.AddListener(() =>
                {
                    session.Input.ForceTouch = !session.Input.ForceTouch;
                    preview.GetComponentInChildren<Text>().text = session.Input.ForceTouch ? "触控预览：开启" : "触控预览：关闭";
                });
            }
            if (practice) Button(panel, "靶场控制", 26, 615, 171, 49, ShowPracticePanel);
            if (field && session.Phase == SessionPhase.Raid)
                Button(panel, "退出本局", 26, 615, 171, 49, ShowExitConfirm, false, new Color32(91, 47, 39, 255));
        }

        void BuildVisualOptions(RectTransform body)
        {
            Label(body, "局内帧率", 29, 24, 12, 1112, 32);
            var choices = new Button[RaidFrameRate.Options.Count];
            for (int i = 0; i < choices.Length; i++)
            {
                int value = RaidFrameRate.Options[i];
                choices[i] = Button(body, value == RaidFrameRate.Uncapped ? "不限帧" : value + " 帧", 24 + i * 225, 50, 208, 45, () =>
                {
                    session.SelectFrameRate(value);
                    UpdateFrameChoices(choices);
                    ResetFrameSample();
                });
            }
            UpdateFrameChoices(choices);
            Label(body, Application.isMobilePlatform
                ? "固定档位保持所选目标；不限帧跟随屏幕刷新率。实际帧率还受系统、省电和温控限制。"
                : "不限帧会关闭游戏帧率上限与垂直同步。",
                17, 24, 102, 1100, 32, color: Muted);
            fpsReadout = Label(body, "", 20, 24, 138, 1100, 32, color: Aqua);
            UpdateFpsText();
            BuildDisplaySettings(body);
            Label(body, "若转动视角时不流畅，先降低渲染分辨率或画质，再选择合适的帧率。", 18, 24, 326, 1100, 53, color: Muted);
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
            Label(parent, "画质影响阴影清晰度、抗锯齿和云层质量。", 17, 720, 190, 430, 35, TextAnchor.UpperRight, Muted);
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

        void UpdateFrameChoices(Button[] choices)
        {
            for (int i = 0; i < choices.Length; i++)
                StyleButton(choices[i], RaidFrameRate.Options[i] == session.FrameRate, null);
        }

        internal void ResetFrameSample()
        {
            sampledFps = fpsSeconds = 0f;
            fpsFrames = 0;
            skipFrameSample = true;
            UpdateFpsText();
        }

        void UpdateFpsText()
        {
            if (fpsReadout == null) return;
            int target = Application.targetFrameRate;
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            fpsReadout.text = "设定：" + (session.FrameRate < 0 ? "不限帧" : session.FrameRate + " 帧")
                + " / 游戏目标：" + (target < 0 ? "不限帧" : target + " 帧")
                + (refresh > 0 && refresh < 1000 ? " / 屏幕：" + refresh.ToString("0.#") + " Hz" : "")
                + " / 实测：" + (sampledFps > 0 ? Mathf.RoundToInt(sampledFps) + " 帧" : "采样中");
        }

        public void BackOrSettings()
        {
            // A UI click and Escape can both arrive before the next frame.
            if (session.Phase != routePhase || backHandledFrame == Time.frameCount) return;
            backHandledFrame = Time.frameCount;
            if (MedicineWheelOpen) { CloseMedicineWheel(true); return; }
            if (CloseInventoryOverlay()) return;
            if (current == ScreenPage.Preparation && closePreparationPicker?.Invoke() == true) return;
            if (current == ScreenPage.Strategy && strategyFocused) { FocusStrategicOverview(); return; }
            if (current == ScreenPage.Results) session.ReturnToMenu();
            else if (backRoutes.Count > 0)
            {
                var restore = backRoutes.Pop();
                restoringRoute = true;
                try { restore(); }
                finally { restoringRoute = false; }
            }
            else if (current == ScreenPage.Field && session.InField) ShowSettings(true);
        }

        void SetPageRestore(Action restore, Action capture = null)
        {
            pageRestore = restore;
            capturePageState = capture;
        }

        void ShowExitConfirm()
        {
            if (session.Phase != SessionPhase.Raid) return;
            var panel = Display(ScreenPage.ExitConfirm, "Raid exit confirmation");
            Header(panel, "退出本局？", "主动退出将按撤离失败结算");
            var body = At("Exit warning", panel, 26, 135, 1186, 438);
            Fill(body, Ink);
            Label(body, "未保护的随身装备和物资将丢失。\n安全箱、绑定近战和已有仓库物资会保留。", 26, 36, 89, 1100, 147, color: Pale);
            Button(panel, "取消 · 继续本局", 26, 615, 225, 49, () =>
            {
                backHandledFrame = Time.frameCount;
                EnterField();
            }, true);
            Button(panel, "确认退出", 1014, 615, 198, 49, session.AbandonRaid, false, new Color32(91,47,39,255));
        }

        public void ShowLoading(bool practice = false)
        {
            var panel = Display(ScreenPage.Loading, "Loading district");
            Header(panel, "正在部署", practice ? "离线靶场" : Deployment.Sites[session.ActiveSite].Name);
            Label(panel, practice ? "正在准备标靶和练习装备…" : "正在准备关卡、搜刮点与撤离路线…", 30, 42, 285, 1150, 78, TextAnchor.MiddleCenter);
        }

        public void ShowPractice()
        {
            ResetDamageCues();
            EnterField();
        }

        internal void TogglePracticePanel()
        {
            if (session.Phase != SessionPhase.Practice) return;
            if (current == ScreenPage.PracticePanel) BackOrSettings();
            else if (current == ScreenPage.Field) ShowPracticePanel();
        }

        void ShowPracticePanel()
        {
            if (session.Phase != SessionPhase.Practice || session.Practice == null) return;
            var panel = Display(ScreenPage.PracticePanel, "Practice controls", wide: true);
            var range = session.Practice;
            var gun = session.Player.Weapon;
            int target = 0;
            SetPageRestore(ShowPracticePanel);
            reflowWidePage = Draw;
            Draw();

            void Draw()
            {
                Clear(panel);
                float w = panel.rect.width;
                WideHeader(panel, "靶场控制", "练习装备独立 · 无限备用弹药", false);
                var stats = range.GetStats();
                var summary = At("Practice statistics", panel, 12, 59, w - 24, 78);
                Fill(summary, Ink);
                Label(summary, "开火 " + stats.Shots + "  /  命中发数 " + stats.HitShots + "  /  命中率 " + stats.Accuracy.ToString("P0")
                    + "   ·   击倒 " + stats.Plates, 21, 12, 7, w - 48, 31, color: Aqua);
                Label(summary, "命中射线 " + stats.Hits + " / " + stats.Rays + "   ·   开火散布均方根 " + stats.SpreadRmsDegrees.ToString("0.00") + "°",
                    16, 12, 44, w - 48, 25, color: Muted);
                var roster = At("Dummy roster", panel, 12, 147, 151, 455);
                Fill(roster, Ink);
                Label(roster, "选中假人", 18, 9, 5, 133, 30, color: Amber);
                for (int i = 0; i < range.DummyCount; i++)
                {
                    int index = i;
                    if (!range.TryGetDummy(index, out var entry)) continue;
                    Button(roster, entry.Name, 9, 40 + 51 * i, 133, 44, () => { target = index; Draw(); }, index == target);
                }
                Button(roster, "重置标靶和计数", 9, 361, 133, 53, () => { range.ResetPlates(); Draw(); });
                const float left = 173f;
                float rightW = Mathf.Min(275f, w * .25f), wide = w - left - rightW - 22f;
                var detail = At("Dummy equipment and condition", panel, left, 147, wide, 455);
                Fill(detail, Ink);
                if (range.TryGetDummy(target, out var dummy))
                {
                    Label(detail, dummy.Name + (dummy.Alive ? " · 就绪" : " · 等待重现"), 24, 12, 5, wide - 24, 33, color: Aqua);
                    Label(detail, "耐受 " + dummy.CurrentHealth.ToString("0.0") + " / " + dummy.MaxHealth.ToString("0")
                        + "   ·   命中 " + dummy.Hits + "   ·   击倒 " + dummy.Knockdowns, 17, 12, 42, wide - 24, 32, color: Pale);
                    string region = dummy.LastHitPart.HasValue ? BodyCondition.Name(dummy.LastHitPart.Value) : "—";
                    Label(detail, "最后命中 " + region + " · " + dummy.LastDamage.ToString("0.0") + "    累计伤害 " + dummy.TotalDamage.ToString("0.0")
                        + "\n命中点散布均方根 " + (100f * dummy.SpreadRms).ToString("0.0") + " 厘米", 16, 12, 80, wide - 24, 55, color: Muted);
                    Gear(EquipmentSlot.Helmet, ItemKind.Helmet, dummy.HelmetId, dummy.HelmetWear, dummy.HelmetDurability, 151);
                    Gear(EquipmentSlot.Armor, ItemKind.Armor, dummy.ArmorId, dummy.ArmorWear, dummy.ArmorDurability, 249);
                    Button(detail, "复位此假人", 12, 362, 163, 48, () => { range.ResetDummy(target); Draw(); }, true);
                }
                var ammo = At("Practice ammo levels", panel, w - rightW - 12, 147, rightW, 455);
                Fill(ammo, Ink);
                Label(ammo, "训练弹等级", 21, 12, 7, rightW - 24, 37, color: Aqua);
                Label(ammo, gun.HasInfiniteReserve ? "备用弹药无限" : "请先切换到一支枪械", 16, 12, 47, rightW - 24, 27, color: Muted);
                float optionWidth = (rightW - 31) * .5f;
                for (int tier = 1; tier <= WeaponRules.AmmoTierCount; tier++)
                {
                    int grade = tier;
                    var round = session.Catalog.Items.Find(def => def.Kind == ItemKind.Ammo && def.Tier == grade
                        && WeaponRules.CanLoad(gun.Definition, def, out _));
                    int i = tier - 1;
                    var option = Button(ammo, tier + " 级弹", 12 + i % 2 * (optionWidth + 7), 87 + i / 2 * 55, optionWidth, 47, () =>
                    {
                        if (round != null && gun.SelectPracticeAmmo(round.Id)) Draw();
                    }, round != null && gun.ReadyAmmoType == round.Id);
                    option.interactable = round != null;
                }
                Label(ammo, session.Catalog.Find(gun.ReadyAmmoType)?.DisplayName ?? "未选训练弹", 18, 12, 264, rightW - 24, 51, color: Amber);
                Button(ammo, "补充练习装备", 12, 362, rightW - 24, 48, () => { session.RefillPractice(); EnterField(); });
                Button(panel, "返回大厅", 12, 617, 145, 48, session.LeavePractice);
                Button(panel, "模拟受伤", 174, 617, 145, 48, () => { session.PracticeHealthSample(); EnterField(); });
                Button(panel, "重伤演练", 330, 617, 145, 48, () => { session.PracticeSevereSample(); ShowHealth(); });
                Button(panel, "继续练习", w - 177, 617, 165, 48, EnterField, true);

                void Gear(EquipmentSlot slot, ItemKind kind, string id, int wear, int durability, float y)
                {
                    var stock = session.Catalog.Stock(kind);
                    var currentGear = session.Catalog.Find(id);
                    Label(detail, SlotName(slot) + "   " + (currentGear?.DisplayName ?? "未装备")
                        + (durability > 0 ? "   耐久 " + (durability - wear) + "/" + durability : ""), 16, 12, y, wide - 24, 27, color: Muted);
                    Button(detail, "清空", 12, y + 31, 74, 44, () => SetGear(null));
                    Button(detail, "上一件", 94, y + 31, 86, 44, () => Move(-1));
                    Button(detail, "下一件", 188, y + 31, 86, 44, () => Move(1));
                    void Move(int delta)
                    {
                        if (stock.Count == 0) return;
                        int i = stock.FindIndex(item => item.Id == id);
                        var item = stock[i < 0 ? (delta > 0 ? 0 : stock.Count - 1) : (i + delta + stock.Count) % stock.Count];
                        SetGear(item.Id);
                    }
                    void SetGear(string option)
                    {
                        if (range.TrySetDummyGear(target, slot, option, session.Catalog)) Draw();
                    }
                }
            }
        }

        public void ShowRaid()
        {
            ResetDamageCues();
            mapZoom = 0f;
            EnterField();
            Notify(session.Input.TouchMode
                ? "向前推杆锁定奔跑，回拉解除。地图标出撤离点。"
                : "F 交互 / 1–4 或 X 切换武器 / J 健康 / M 地图 / Tab 背包 / Esc 设置");
        }

        public void ShowResults()
        {
            var panel = Display(ScreenPage.Results, "Raid report");
            Header(panel, "突袭结算", Deployment.Sites[session.ActiveSite].Name + " / 行动记录");
            var report = At("Debrief plate", panel, 26, 136, 1186, 450);
            Fill(report, Ink);
            Fill(At("Report accent", report, 0, 0, 4, 109), Amber);
            Label(report, session.Result, 25, 24, 27, 1138, 389);
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
            bool practice = session.Phase == SessionPhase.Practice;
            SetActive(miniMap.Root.parent.gameObject, !practice);
            UpdateHud(true);
        }

        void Switch(ScreenPage next)
        {
            var phase = session.Phase;
            if (routePhase != phase || next == ScreenPage.Lobby || next == ScreenPage.Field
                || next == ScreenPage.Loading || next == ScreenPage.Results || next == ScreenPage.Error)
                backRoutes.Clear();
            else if (next != current && !restoringRoute)
            {
                var source = pageRestore;
                if (current == ScreenPage.FieldInventory)
                {
                    var container = loot;
                    source = () => ShowFieldInventory(container);
                }
                if (source != null)
                {
                    capturePageState?.Invoke();
                    backRoutes.Push(source);
                }
            }
            routePhase = phase;
            SetPageRestore(next switch
            {
                ScreenPage.Lobby => (Action)ShowMenu,
                ScreenPage.Stash => ShowStash,
                ScreenPage.Operator => ShowOperator,
                ScreenPage.Field => EnterField,
                ScreenPage.PracticePanel => ShowPracticePanel,
                _ => null
            });

            CancelFieldGestures();
            session.SaveControlPreferences();
            operatorPreview?.Dispose();
            operatorPreview = null;
            ReleaseStrategicMap();
            closePreparationPicker = null;
            DisposeGunsmithPreview();
            healthRefresh = null;
            ResetInventoryView();
            Clear(page);
            panelRoot = null;
            reflowWidePage = null;
            widePage = false;
            fpsReadout = null;
            fullMap = null;
            layoutSelection = null;
            layoutField = null;
            current = next;
            bool field = next == ScreenPage.Field;
            hud.gameObject.SetActive(field);
            touch.gameObject.SetActive(field && session.Input.TouchMode);
            weaponBar.gameObject.SetActive(field);
            toastAnchor.gameObject.SetActive(false);
            session.SyncControls();
        }

        RectTransform Display(ScreenPage next, string title, bool wide = false)
        {
            Switch(next);
            Fill(Stretch("Page scrim", page), new Color32(6, 10, 13, 221), true);
            panelRoot = Anchor(title, page, 0.5f, 0.5f);
            panelRoot.sizeDelta = new Vector2(1240, 680);
            widePage = wide;
            Fill(panelRoot, Surface, true);
            Canvas.ForceUpdateCanvases();
            FitPanel();
            return panelRoot;
        }

        Button AddMenuBack(RectTransform root)
        {
            return Button(root, "‹ 返回", 12, 7, 100, 36, BackOrSettings);
        }

        bool HasMenuBack => backRoutes.Count > 0 || current == ScreenPage.Results;

        void WideHeader(RectTransform root, string title, string subtitle, bool showCredits)
        {
            float w = root.rect.width;
            Fill(At("Compact header", root, 0, 0, w, 50), Ink);
            Fill(At("Header highlight", root, 0, 0, w, 2), Amber);
            float left = HasMenuBack ? 126f : 16f;
            if (HasMenuBack) AddMenuBack(root);
            float subtitleX = w * .39f;
            var heading = Label(root, title, 26, left, 4, Mathf.Min(subtitleX - left - 14f, 430f), 43, TextAnchor.MiddleLeft);
            heading.resizeTextForBestFit = true;
            heading.resizeTextMinSize = 16;
            heading.resizeTextMaxSize = 26;
            Label(root, subtitle, 15, subtitleX, 6, (showCredits ? w - 245f : w - 16f) - subtitleX, 37, TextAnchor.MiddleLeft, Muted);
            if (!showCredits) return;
            creditsReadout = Label(root, "", 19, w - 235, 4, 218, 43, TextAnchor.MiddleRight, Aqua);
            UpdateCredits();
        }

        void UpdateCredits()
        {
            if (creditsReadout != null && session.Profile != null) creditsReadout.text = session.Profile.Credits + " 信用点";
        }

        void Header(RectTransform root, string title, string subtitle)
        {
            Fill(At("Header fill", root, 0, 0, 1240, 110), Ink);
            Fill(At("Header highlight", root, 0, 0, 1240, 3), Amber);
            float left = HasMenuBack ? 126f : 26f;
            if (HasMenuBack) AddMenuBack(root);
            Label(root, title, 35, left, 23, 811f - left, 57);
            Label(root, subtitle, 16, left, 78, 796f - left, 25, color: Muted);
            Label(root, "尘线 / " + Application.version, 19, 816, 28, 396, 40, TextAnchor.UpperRight, Amber);
            if (session.Profile != null && session.Phase == SessionPhase.Menu)
                creditsReadout = Label(root, session.Profile.Credits + " 信用点", 19, 853, 73, 359, 30, TextAnchor.UpperRight, Muted);
        }

        void FitPanel()
        {
            if (panelRoot == null) return;
            if (!widePage)
            {
                float compact = Mathf.Min(1f, Mathf.Min(safe.rect.width / 1260f, safe.rect.height / 700f));
                panelRoot.localScale = Vector3.one * Mathf.Max(.01f, compact);
                return;
            }
            float availableWidth = Mathf.Max(1f, safe.rect.width - 20f);
            float availableHeight = Mathf.Max(1f, safe.rect.height - 20f);
            float width = Mathf.Clamp(Mathf.Floor(680f * availableWidth / availableHeight), 1040f, 2048f);
            bool resized = Mathf.Abs(panelRoot.sizeDelta.x - width) >= 1f;
            if (resized) panelRoot.sizeDelta = new Vector2(width, 680);
            float scale = Mathf.Min(1f, Mathf.Min(availableWidth / width, availableHeight / 680f));
            panelRoot.localScale = Vector3.one * Mathf.Max(.01f, scale);
            if (resized) reflowWidePage?.Invoke();
        }

        void UpdateSafeArea()
        {
            lastSafe = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width < 1 || Screen.height < 1) return;
            if (medicineHeld) CloseMedicineWheel(true);
            CancelInventoryInput();
            if (workshopGesture != null) workshopGesture.ResetPointers();
            safe.anchorMin = new Vector2(lastSafe.xMin / Screen.width, lastSafe.yMin / Screen.height);
            safe.anchorMax = new Vector2(lastSafe.xMax / Screen.width, lastSafe.yMax / Screen.height);
            Canvas.ForceUpdateCanvases();
            reticleAnchor.anchoredPosition = (new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) - lastSafe.center) / uiCanvas.scaleFactor;
            ApplyTouchLayout();
            FitPanel();
        }

        public void Notify(string message)
        {
            toast.text = message;
            toastUntil = Time.unscaledTime + 4.5f;
            float y = current == ScreenPage.Field ? 180f : 145f;
            toastAnchor.GetChild(0).GetComponent<RectTransform>().anchoredPosition = new Vector2(-290, y);
            toastAnchor.gameObject.SetActive(true);
        }

        void CheckSafeArea()
        {
            if (Screen.safeArea != lastSafe || Screen.width != lastScreen.x || Screen.height != lastScreen.y) UpdateSafeArea();
        }

        void Update()
        {
            if (toastAnchor.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastAnchor.gameObject.SetActive(false);
            if (skipFrameSample) skipFrameSample = false;
            else
            {
                fpsFrames++;
                fpsSeconds += Time.unscaledDeltaTime;
            }
            if (fpsSeconds >= 0.6f)
            {
                sampledFps = fpsFrames / fpsSeconds;
                fpsFrames = 0;
                fpsSeconds = 0;
                UpdateFpsText();
                if (current == ScreenPage.FieldMap && fullMap != null) UpdateFullMap();
            }
            if (current == ScreenPage.Field)
            {
                UpdateHud(false);
            }
            else if (current == ScreenPage.Health && Time.unscaledTime >= healthRefreshAt)
            {
                healthRefreshAt = Time.unscaledTime + .15f;
                healthRefresh?.Invoke();
            }
        }

        internal void UpdateFieldGestures()
        {
            if (current == ScreenPage.Field && medicineHeld) UpdateMedicineGesture();
        }

        void LateUpdate()
        {
            UpdateInventoryDrag();
            if (current == ScreenPage.Strategy) UpdateStrategicMap();
            itemIcons?.Step();
            if (current == ScreenPage.Gunsmith) RenderGunsmithPreview();
        }

        void OnApplicationFocus(bool focused) { if (!focused) CancelInventoryInput(); }
        void OnApplicationPause(bool paused) { if (paused) CancelInventoryInput(); }

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
            ReleaseStrategicMap();
            DisposeGunsmithPreview();
            itemIcons?.Dispose();
            icons?.Dispose();
        }
    }
}
