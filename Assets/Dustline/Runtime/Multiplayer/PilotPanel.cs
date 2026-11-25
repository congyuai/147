#if !UNITY_SERVER
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class PilotPanel : IDisposable
    {
        static readonly Color Ink = new Color32(18, 27, 31, 245), Plate = new Color32(39, 55, 60, 255);
        static readonly Color Pale = new Color32(239, 238, 222, 255), Muted = new Color32(174, 192, 189, 255);
        static readonly Color Mint = new Color32(115, 205, 178, 255), Amber = new Color32(234, 178, 92, 255);
        static readonly string[] CargoNames = { "维修电路板", "调度记录盒", "取样模块", "运输标签" };
        const float EntryWidth = 810, EntryHeight = 602, RoomWidth = 580, RoomHeight = 330;
        readonly GameObject canvasNode, events;
        readonly Canvas canvas;
        readonly Font font;
        readonly RaidUiIcons icons;
        readonly RectTransform safe, entry, room, field, touch, sprintTarget, reticle;
        readonly InputField address, port, access, name, certificate;
        readonly Text entryStatus, roomTitle, roomStatus, mission, ammo, cargo, action;
        readonly Button connect, ready;
        readonly PlayerInputReader input;
        Rect oldSafe;
        int width, height;
        float oldScale;
        uint shownTick = uint.MaxValue;
        string roomWarning;

        public PilotPanel(Transform parent, PlayerInputReader input, Action<string, string, string, string, string> join, Action ready, Action leave)
        {
            this.input = input;
            font = Resources.Load<Font>("Dustline/Fonts/NotoSansSC-Regular");
            if (font == null) throw new InvalidOperationException("Pilot font not available.");
            icons = new RaidUiIcons();
            canvasNode = new GameObject("Pilot interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasNode.transform.SetParent(parent, false);
            canvas = canvasNode.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasNode.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            events = new GameObject("Pilot event system", typeof(DustlineEventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(parent, false);
            safe = Stretch("Pilot safe area", canvasNode.transform);

            entry = Center("Pilot connection", safe, EntryWidth, EntryHeight);
            Fill(entry, Ink);
            Label(entry, "DUSTLINE / 双人联机试跑", 31, 25, 20, 754, 47, Mint);
            Label(entry, "石脊采石场 · 西坡营地    双人合作 / 局内临时物资", 18, 25, 67, 750, 32, Muted);
            Caption("服务器 IPv4", 25, 110, 310);
            Caption("UDP 端口", 355, 110, 120);
            Caption("房间口令", 496, 110, 286);
            address = Field(entry, "服务器地址", PilotAddress.Local, 25, 139, 310, 46, 15);
            port = Field(entry, "端口", PilotAddress.Port.ToString(CultureInfo.InvariantCulture), 355, 139, 120, 46, 5, InputField.ContentType.IntegerNumber);
            access = Field(entry, "房间口令", "", 496, 139, 289, 46, PilotWire.MaxCode, InputField.ContentType.Password);
            Caption("证书中的服务器名称（私网连接必填）", 25, 198, 754);
            name = Field(entry, "服务器名称", "", 25, 228, 760, 46, 253);
            Caption("可信签发证书 PEM（私网连接必填，只填公有证书）", 25, 287, 754);
            certificate = Field(entry, "粘贴签发证书 PEM", "", 25, 317, 760, 104, PilotAddress.MaxPemLength, multiline: true);
            Label(entry, "同机测试可用 127.0.0.1；不同设备须可到达服务器的加密私网。", 17, 25, 435, 750, 33, Muted);
            entryStatus = Label(entry, "", 17, 25, 479, 750, 37, Amber);
            connect = Button(entry, "加入房间", 25, 528, 760, 50, () =>
                join(address.text, port.text, access.text, certificate.text, name.text), true);

            room = Center("Pilot room", safe, RoomWidth, RoomHeight);
            Fill(room, Ink);
            roomTitle = Label(room, "等待队友", 31, 28, 26, 524, 52, Mint);
            roomStatus = Label(room, "", 20, 28, 93, 524, 125, Pale);
            this.ready = Button(room, "准备", 28, 248, 249, 51, () => ready(), true);
            Button(room, "返回", 302, 248, 250, 51, () => leave());

            field = Stretch("Pilot raid", safe);
            touch = Stretch("Pilot touch", field);
            var look = Stretch("Look", touch);
            look.anchorMin = new Vector2(.31f, 0f);
            look.anchorMax = new Vector2(1f, .91f);
            Fill(look, Color.clear, true).canvasRenderer.cullTransparentMesh = false;
            Attach(look, "Look");
            var stick = Dial("Move", RaidIcon.Dial, .113f, .193f, 134f, out var move);
            var knob = Center("Stick knob", stick, 46f, 46f);
            Fill(knob, Pale).sprite = icons[RaidIcon.Knob];
            move.Knob = knob;
            sprintTarget = Center("Sprint lock", stick, 47, 47);
            sprintTarget.anchorMin = sprintTarget.anchorMax = new Vector2(.5f, 1f);
            sprintTarget.anchoredPosition = new Vector2(0, 38f);
            Fill(sprintTarget, Mint).sprite = icons[RaidIcon.Sprint];
            move.SprintTarget = sprintTarget;
            sprintTarget.gameObject.SetActive(false);
            Dial("Quiet", RaidIcon.Quiet, .244f, .167f, 58f, out _);
            Dial("Fire", RaidIcon.Fire, .912f, .29f, 99f, out _);
            Dial("Aim", RaidIcon.Aim, .80f, .355f, 72f, out _);
            Dial("Reload", RaidIcon.Reload, .924f, .121f, 68f, out _);
            Dial("Crouch", RaidIcon.Crouch, .80f, .121f, 68f, out _);
            Dial("Interact", RaidIcon.Search, .67f, .155f, 72f, out _);

            var hud = At("Pilot status", field, 20, 18, 435, 115);
            Fill(hud, Ink);
            mission = Label(hud, "", 22, 14, 8, 407, 39, Mint);
            ammo = Label(hud, "", 19, 14, 46, 407, 36, Pale);
            cargo = Label(hud, "", 16, 14, 82, 407, 28, Muted);
            if (input.TouchMode)
            {
                var back = Button(field, "离开", -116, 18, 96, 44, leave);
                var backRect = (RectTransform)back.transform;
                backRect.anchorMin = backRect.anchorMax = Vector2.one;
            }
            reticle = Center("View center", field, 33, 33);
            Fill(reticle, Pale).sprite = icons[RaidIcon.Reticle];
            var prompt = Center("Pilot objective", field, 438, 50);
            prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, .24f);
            Fill(prompt, Ink);
            action = Label(prompt, "", 19, 12, 3, 414, 44, Pale, TextAnchor.MiddleCenter);
            if (!input.TouchMode)
                Label(field, "WASD 移动   Shift 疾跑   C 蹲下   R 换弹   F 搜刮   Esc 离开", 17, 20, 146, 570, 30, Pale);
            Resize();
            Entry("");
            events.GetComponent<DustlineEventSystem>().BeforeInput = Resize;

            void Caption(string title, float x, float y, float w) => Label(entry, title, 18, x, y, w, 28, Pale);
        }

        public void Resize()
        {
            var rect = Screen.safeArea;
            if (Screen.width == width && Screen.height == height && rect == oldSafe && canvas.scaleFactor == oldScale) return;
            width = Screen.width;
            height = Screen.height;
            oldSafe = rect;
            if (width == 0 || height == 0) return;
            safe.anchorMin = new Vector2(rect.xMin / width, rect.yMin / height);
            safe.anchorMax = new Vector2(rect.xMax / width, rect.yMax / height);
            Canvas.ForceUpdateCanvases();
            oldScale = canvas.scaleFactor;
            float scale = Mathf.Max(.01f, oldScale);
            reticle.anchoredPosition = (new Vector2(width * .5f, height * .5f) - rect.center) / scale;
            Fit(entry, EntryWidth, EntryHeight);
            Fit(room, RoomWidth, RoomHeight);
        }

        void Fit(RectTransform node, float w, float h)
        {
            float scale = Mathf.Min(1f, Mathf.Min(safe.rect.width / (w + 24), safe.rect.height / (h + 24)));
            node.localScale = Vector3.one * Mathf.Max(.01f, scale);
        }

        public void Entry(string message)
        {
            shownTick = uint.MaxValue;
            connect.interactable = true;
            roomWarning = null;
            SetPages(true, false, false);
            entryStatus.text = message;
        }

        public void Status(string message)
        {
            if (entry.gameObject.activeSelf) entryStatus.text = message;
            else { roomWarning = message; roomStatus.text = message; }
        }

        public void Connecting()
        {
            SetPages(false, true, false);
            connect.interactable = false;
            access.SetTextWithoutNotify("");
            roomTitle.text = "正在连接";
            roomStatus.text = "等待服务器响应。";
            ready.interactable = false;
        }

        public bool RoomVisible => room.gameObject.activeSelf;

        public void Show(in PilotFrame frame, bool refresh = false)
        {
            bool active = frame.Phase == PilotPhase.Raid && frame.Result == PilotResult.None;
            sprintTarget.gameObject.SetActive(active && input.TouchMode && (input.TouchSprintLocked || input.StickMoving));
            if (!refresh && frame.Tick == shownTick) return;
            shownTick = frame.Tick;
            SetPages(false, !active, active);
            if (active)
            {
                var own = frame.Actor(frame.Seat);
                var mate = frame.Actor(1 - frame.Seat);
                mission.text = $"西坡营地    {frame.Seconds / 60:00}:{frame.Seconds % 60:00}";
                ammo.text = $"生命 {own.Health}    体力 {frame.Stamina}    弹药 {frame.Rounds} / {frame.Reserve}";
                cargo.text = $"物资 {TotalCargo(frame.Cargo)} 份    队友 {(mate.Visible ? mate.Health.ToString() : "已离开战斗")}";
                int distance = Mathf.RoundToInt((PilotMap.Exit - own.Position).Length());
                float relative = Mathf.DeltaAngle(own.Yaw, PilotMap.Yaw(PilotMap.Exit - own.Position));
                string direction = Mathf.Abs(relative) < 37f ? "前方" : Mathf.Abs(relative) > 143f ? "后方" : relative < 0f ? "左侧" : "右侧";
                action.text = frame.ExtractionTicks > 0 ? $"撤离中    {(PilotMatch.ExtractionTime - frame.ExtractionTicks) / (float)PilotMatch.Hz:0.0} 秒"
                    : frame.SearchTicks > 0 ? $"搜刮中    {frame.SearchTicks * 100 / PilotMatch.SearchTime}%"
                    : frame.Reloading ? "正在换弹" : $"撤离点    {direction} {distance} 米";
                return;
            }
            if (frame.Phase == PilotPhase.Lobby)
            {
                roomTitle.text = "西坡营地 · 准备出发";
                ready.interactable = (frame.Ready & (1 << frame.Seat)) == 0;
                if (!ready.interactable) roomWarning = null;
                roomStatus.text = roomWarning ?? $"一号位：{SeatStatus(frame.Connected, frame.Ready, 0)}    二号位：{SeatStatus(frame.Connected, frame.Ready, 1)}\n你是 {frame.Seat + 1} 号位 · {frame.Seconds} 秒准备时间";
            }
            else
            {
                roomTitle.text = ResultTitle(frame.Result);
                roomStatus.text = frame.Result == PilotResult.Extracted
                    ? DescribeCargo(frame.Cargo) + "\n物资仅计入本局结果。"
                    : "本局没有带回物资。";
                if (frame.Phase == PilotPhase.Raid) roomStatus.text += "\n队友还在行动。";
                ready.interactable = false;
            }
        }

        static string SeatStatus(byte connected, byte ready, int seat)
            => (connected & (1 << seat)) == 0 ? "等待加入" : (ready & (1 << seat)) == 0 ? "未准备" : "已准备";

        static string ResultTitle(PilotResult result) => result switch
        {
            PilotResult.Extracted => "撤离成功", PilotResult.Downed => "行动员倒下", PilotResult.Disconnected => "连接中断",
            PilotResult.TimeExpired => "行动超时", _ => "行动结束"
        };

        static string DescribeCargo(byte mask)
        {
            if (mask == 0) return "未携带搜刮物资。";
            string summary = "带回：";
            string separator = "";
            for (int i = 0; i < CargoNames.Length; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                summary += separator + CargoNames[i];
                separator = "、";
            }
            return summary;
        }

        static int TotalCargo(byte mask)
        {
            int count = 0;
            for (int i = 0; i < CargoNames.Length; i++) count += (mask >> i) & 1;
            return count;
        }

        void SetPages(bool showEntry, bool showRoom, bool showField)
        {
            entry.gameObject.SetActive(showEntry);
            room.gameObject.SetActive(showRoom);
            field.gameObject.SetActive(showField);
            touch.gameObject.SetActive(showField && input.TouchMode);
        }

        RectTransform Dial(string id, RaidIcon icon, float x, float y, float size, out TouchControl control)
        {
            var rect = Center(id, touch, size, size);
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
            var face = Fill(rect, new Color32(42, 64, 67, 190), true);
            face.sprite = icons[RaidIcon.Dial];
            var symbol = Center("Symbol", rect, size * .70f, size * .70f);
            Fill(symbol, Pale).sprite = icons[icon];
            control = Attach(rect, id);
            control.SetFace(face, Mint);
            return rect;
        }

        TouchControl Attach(RectTransform rect, string id)
        {
            var control = rect.gameObject.AddComponent<TouchControl>();
            control.Input = input;
            control.Action = id;
            return control;
        }

        InputField Field(RectTransform parent, string hint, string value, float x, float y, float w, float h, int limit,
            InputField.ContentType type = InputField.ContentType.Standard, bool multiline = false)
        {
            var rect = At(hint, parent, x, y, w, h);
            var face = Fill(rect, Plate, true);
            var placeholder = Label(rect, hint, 18, 12, 3, w - 24, h - 6, Muted, TextAnchor.MiddleLeft);
            var text = Label(rect, "", multiline ? 15 : 20, 12, 3, w - 24, h - 6, Pale,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = face;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = type;
            if (multiline) field.lineType = InputField.LineType.MultiLineNewline;
            field.characterLimit = limit;
            field.shouldHideMobileInput = type == InputField.ContentType.Password;
            field.SetTextWithoutNotify(value);
            return field;
        }

        Button Button(RectTransform parent, string title, float x, float y, float w, float h, Action click, bool primary = false)
        {
            var rect = At(title, parent, x, y, w, h);
            var image = Fill(rect, primary ? Mint : Plate, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label(rect, title, 20, 5, 2, w - 10, h - 4, primary ? Ink : Pale, TextAnchor.MiddleCenter);
            button.onClick.AddListener(() => click());
            return button;
        }

        Text Label(RectTransform parent, string text, int size, float x, float y, float w, float h,
            Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = At("Label", parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        static RectTransform Node(string title, Transform parent)
        {
            var rect = (RectTransform)new GameObject(title, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static RectTransform Stretch(string title, Transform parent)
        {
            var rect = Node(title, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static RectTransform Center(string title, Transform parent, float w, float h)
        {
            var rect = Node(title, parent);
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        static RectTransform At(string title, Transform parent, float x, float y, float w, float h)
        {
            var rect = Node(title, parent);
            rect.anchorMin = rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -y);
            return rect;
        }

        static Image Fill(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public void Dispose()
        {
            canvasNode.SetActive(false);
            events.SetActive(false);
            Object.Destroy(canvasNode);
            Object.Destroy(events);
            icons.Dispose();
        }
    }
}
#endif
