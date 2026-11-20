using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Dustline
{
    public sealed class GameUI : MonoBehaviour
    {
        public bool InventoryOpen { get; private set; }
        GameSession session;
        Font font;
        RectTransform safe, page, hud, touch, inventory, gridRoot, detailsRoot;
        Text hudStatus, compass, prompt, crosshair, toast, balance, description;
        LootContainer loot;
        GridInventory selectedBag;
        string selectedId;
        bool placing, rotated;
        float toastUntil, hitUntil;
        Rect lastSafe;
        Vector2Int lastScreen;
        readonly Color panel = new Color(0.04f, 0.065f, 0.075f, 0.97f);
        readonly Color accent = new Color(0.76f, 0.58f, 0.28f);
        readonly Color textColor = new Color(0.88f, 0.91f, 0.88f);

        public void Initialize(GameSession owner)
        {
            session = owner;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Dustline UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safe = Rect("Safe area", canvasObject.transform, 0, 0, 1, 1);
            page = Rect("Pages", safe, 0, 0, 1, 1);
            hud = Rect("HUD", safe, 0, 0, 1, 1);
            hudStatus = Label(hud, "", 24, 20, 20, 610, 70);
            compass = Label(hud, "", 20, 730, 20, 520, 70, TextAnchor.UpperRight);
            prompt = Label(hud, "", 23, 350, 440, 580, 70, TextAnchor.MiddleCenter);
            var center = Rect("Crosshair center", hud, 0.5f, 0.5f, 0.5f, 0.5f);
            crosshair = Label(center, "+", 28, -25, -25, 50, 50, TextAnchor.MiddleCenter);
            touch = Rect("Touch controls", safe, 0, 0, 1, 1);
            BuildTouch();
            toast = Label(safe, "", 22, 300, 105, 680, 70, TextAnchor.MiddleCenter);
            toast.color = accent;
            var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            UpdateSafeArea();
        }

        RectTransform Rect(string title, Transform parent, float x0, float y0, float x1, float y1)
        {
            var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        RectTransform At(string title, Transform parent, float x, float y, float width, float height)
        {
            var rect = Rect(title, parent, 0, 1, 0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        Text Label(Transform parent, string value, int size, float x, float y, float w, float h, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var label = At("Label", parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = value;
            label.color = textColor;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        Button Button(Transform parent, string title, float x, float y, float w, float h, Action action, Color? color = null)
        {
            var rect = At(title, parent, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color ?? new Color(0.16f, 0.22f, 0.23f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => action());
            var text = Label(rect, title, 18, 5, 3, w - 10, h - 6, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 19;
            return button;
        }
        void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            { var child = parent.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
        }
        void Background(RectTransform root) { root.gameObject.AddComponent<Image>().color = panel; }
        RectTransform CenterPanel(string title)
        {
            var rect = Rect(title, page, 0.5f, 0.5f, 0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1240, 680);
            Canvas.ForceUpdateCanvases();
            rect.localScale = Vector3.one * Mathf.Min(1, safe.rect.width / 1280, safe.rect.height / 720);
            Background(rect);
            return rect;
        }

        public void ShowMenu()
        {
            InventoryOpen = false;
            loot = null;
            selectedBag = null;
            selectedId = null;
            placing = false;
            Clear(page);
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            session.SetCursor(true);
            inventory = CenterPanel("Loadout and stash");
            Label(inventory, "DUSTLINE / EXTRACTION", 30, 24, 16, 780, 48);
            balance = Label(inventory, "", 23, 820, 22, 390, 40, TextAnchor.UpperRight);
            Label(inventory, "LOCAL RAID  /  14 AI  /  15 MIN\nEquip a rifle, load ammo with R, reach the green NORTH-EAST exit. No online players in this build.", 18, 24, 65, 1170, 56);
            BuildInventoryBody();
            Button(inventory, "DEPLOY", 1010, 604, 205, 54, session.StartRaid, new Color(0.37f, 0.3f, 0.12f));
            Button(inventory, "Emergency gear", 785, 604, 205, 54, session.EmergencyKit);
            Button(inventory, "AR-20 / 240", 24, 604, 140, 54, () => session.Buy("rifle", 1));
            Button(inventory, "Ammo x30 / 60", 174, 604, 150, 54, () => session.Buy("ammo", 30));
            Button(inventory, "Medkit / 40", 334, 604, 140, 54, () => session.Buy("medkit", 1));
            Button(inventory, "Vest / 160", 484, 604, 140, 54, () => session.Buy("armor", 1));
            Button(inventory, "Touch preview", 634, 604, 140, 54, () => { session.Input.ForceTouch = !session.Input.ForceTouch; Notify("Touch preview " + (session.Input.ForceTouch ? "on" : "off")); });
            RefreshInventory();
            if (!string.IsNullOrEmpty(session.Notice)) Notify(session.Notice);
        }

        public void ShowRaid()
        {
            Clear(page);
            InventoryOpen = false;
            inventory = null;
            selectedBag = null;
            selectedId = null;
            hud.gameObject.SetActive(true);
            touch.gameObject.SetActive(session.Input.TouchMode);
            Notify("WASD move | Mouse look/fire | R reload | C crouch | E loot | Tab bag | H heal | B fire mode");
        }
        public void ToggleInventory()
        {
            if (session.Phase != SessionPhase.Raid) return;
            if (InventoryOpen)
            {
                InventoryOpen = false;
                loot = null;
                Clear(page);
                inventory = null;
                touch.gameObject.SetActive(session.Input.TouchMode);
                session.SetCursor(false);
                session.Input.Gameplay = true;
                return;
            }
            OpenInventory(null);
        }
        public void OpenLoot(LootContainer container)
        {
            if (session.Phase != SessionPhase.Raid || container == null) return;
            OpenInventory(container);
        }
        void OpenInventory(LootContainer container)
        {
            loot = container;
            InventoryOpen = true;
            selectedBag = null;
            selectedId = null;
            placing = false;
            Clear(page);
            touch.gameObject.SetActive(false);
            session.SetCursor(true);
            session.Input.Gameplay = false;
            inventory = CenterPanel("Inventory");
            Label(inventory, "FIELD INVENTORY / RAID CONTINUES", 28, 24, 16, 930, 50);
            Label(inventory, "Tap an item, then an action. Move: choose a destination cell. Rotate changes the orientation for your next placement.", 18, 24, 68, 1170, 50);
            Button(inventory, "CLOSE [TAB]", 1025, 20, 185, 44, ToggleInventory);
            BuildInventoryBody();
            Button(inventory, "Use medicine [H]", 24, 604, 215, 54, () => session.UseMedicine());
            Button(inventory, "Fire mode", 249, 604, 180, 54, () => { session.Player.Weapon.ToggleFireMode(); Notify(session.Player.Weapon.Automatic ? "Automatic" : "Semi automatic"); });
            Label(inventory, "Opening the bag does not pause combat or extraction.", 19, 455, 616, 745, 40);
            RefreshInventory();
        }
        void BuildInventoryBody()
        {
            gridRoot = At("Grids", inventory, 20, 122, 1200, 450);
            detailsRoot = At("Selection", inventory, 970, 126, 240, 445);
        }

        public void RefreshInventory()
        {
            if (inventory == null || (session.Phase != SessionPhase.Menu && !InventoryOpen)) return;
            Clear(gridRoot);
            Clear(detailsRoot);
            if (session.Phase == SessionPhase.Menu)
            {
                balance.text = session.Profile.Credits + " CREDITS";
                DrawGrid(session.Stash, "STASH", 0, 0, 32);
            }
            else if (loot != null) DrawGrid(loot.Inventory, loot.DisplayName, 0, 0, 52);
            else Label(gridRoot, "FIELD OPERATIONS\n\nLocate yellow supply crates and fallen enemies. Aim at a container and press USE.\n\nThe extraction zone is the green pad in the north-east corner. Stay inside for 8 seconds.\n\nLeaving the zone resets the timer. Death or raid timeout loses your loadout.", 21, 0, 10, 360, 420);
            DrawGrid(session.Loadout, "BACKPACK", 420, 0, 52);
            DrawEquipment(EquipmentSlot.Weapon, 420, 365);
            DrawEquipment(EquipmentSlot.Armor, 420, 410);
            ShowSelection();
        }

        void DrawGrid(GridInventory bag, string title, float x, float y, float cell)
        {
            Label(gridRoot, title + "  " + bag.Weight.ToString("0.0") + "/" + bag.MaxWeight.ToString("0") + " kg", 19, x, y, 390, 30);
            var board = At(title, gridRoot, x, y + 35, bag.Width * cell, bag.Height * cell);
            for (int row = 0; row < bag.Height; row++)
                for (int col = 0; col < bag.Width; col++)
                {
                    int cx = col, cy = row;
                    Button(board, "", col * cell, row * cell, cell - 2, cell - 2, () => PlaceAt(bag, cx, cy), new Color(0.1f, 0.135f, 0.15f));
                }
            foreach (var item in bag.Items)
            {
                if (item.Slot != EquipmentSlot.None) continue;
                var definition = session.Catalog.Find(item.ItemId);
                int width = item.Rotated ? definition.Height : definition.Width;
                int height = item.Rotated ? definition.Width : definition.Height;
                string id = item.InstanceId;
                Color color = selectedBag == bag && selectedId == id ? accent : definition.Tint;
                Button(board, definition.DisplayName + (item.Quantity > 1 ? " x" + item.Quantity : ""), item.X * cell, item.Y * cell,
                    width * cell - 2, height * cell - 2, () => SelectItem(bag, id), color);
            }
        }
        void DrawEquipment(EquipmentSlot slot, float x, float y)
        {
            var item = session.Loadout.Equipped(slot);
            string text = slot.ToString().ToUpper() + ": " + (item == null ? "EMPTY" : session.Catalog.Find(item.ItemId).DisplayName);
            Button(gridRoot, text, x, y, 312, 40, () => { if (item != null) SelectItem(session.Loadout, item.InstanceId); });
        }
        void SelectItem(GridInventory bag, string id)
        {
            if (placing && bag == selectedBag)
            {
                var destination = bag.Find(id);
                PlaceAt(bag, destination.X, destination.Y);
                return;
            }
            selectedBag = bag;
            selectedId = id;
            rotated = bag.Find(id).Rotated;
            placing = false;
            RefreshInventory();
        }
        void PlaceAt(GridInventory bag, int x, int y)
        {
            if (!placing || bag != selectedBag || selectedId == null) return;
            if (!bag.TryMove(selectedId, x, y, rotated, out var error)) { Notify(error); return; }
            placing = false;
            session.InventoryChanged();
            RefreshInventory();
        }
        void ShowSelection()
        {
            var item = selectedBag?.Find(selectedId);
            if (item == null) { Label(detailsRoot, "SELECT AN ITEM\n\nMove, rotate, equip, transfer or sell items here.", 20, 0, 0, 235, 150); return; }
            var definition = session.Catalog.Find(item.ItemId);
            description = Label(detailsRoot, definition.DisplayName + "\n" + definition.Kind + " / x" + item.Quantity + "\n" + (definition.UnitWeight * item.Quantity).ToString("0.00") + " kg" + (definition.Kind == ItemKind.Weapon ? "\nMagazine: " + Mathf.Max(0, item.LoadedAmmo) : ""), 21, 0, 0, 235, 120);
            float y = 128;
            GridInventory target = null;
            if (session.Phase == SessionPhase.Menu) target = selectedBag == session.Stash ? session.Loadout : session.Stash;
            else if (loot != null) target = selectedBag == loot.Inventory ? session.Loadout : loot.Inventory;
            if (target != null)
            {
                var destination = target;
                ActionButton("TRANSFER", ref y, () =>
                {
                    if (!selectedBag.TryTransferTo(selectedId, destination, out var error)) Notify(error);
                    else { selectedId = null; placing = false; session.InventoryChanged(); }
                    RefreshInventory();
                });
            }
            if (item.Slot == EquipmentSlot.None)
            {
                ActionButton(placing ? "CANCEL MOVE" : "MOVE", ref y, () => { placing = !placing; RefreshInventory(); });
                ActionButton(rotated ? "ROTATE: 90" : "ROTATE: 0", ref y, () => { rotated = !rotated; placing = true; RefreshInventory(); });
            }
            if (selectedBag == session.Loadout && (definition.Kind == ItemKind.Weapon || definition.Kind == ItemKind.Armor))
            {
                ActionButton(item.Slot == EquipmentSlot.None ? "EQUIP" : "UNEQUIP", ref y, () =>
                {
                    string error;
                    bool ok = item.Slot != EquipmentSlot.None ? selectedBag.TryUnequip(item.Slot, out error)
                        : selectedBag.TryEquip(item.InstanceId, definition.Kind == ItemKind.Weapon ? EquipmentSlot.Weapon : EquipmentSlot.Armor, out error);
                    if (!ok) Notify(error); else session.InventoryChanged();
                    placing = false;
                    RefreshInventory();
                });
            }
            if (session.Phase == SessionPhase.Menu) ActionButton("SELL / " + Mathf.Max(1, definition.Value / 2) * item.Quantity, ref y, () => session.Sell(selectedBag, selectedId));
            else if (selectedBag == session.Loadout && definition.Kind == ItemKind.Medicine) ActionButton("USE", ref y, () => session.UseMedicine(selectedId));
            if (placing) Label(detailsRoot, "Choose a cell in the same grid.\nRotation: " + (rotated ? "90 degrees" : "0 degrees"), 17, 0, y + 4, 235, 75);
        }
        void ActionButton(string title, ref float y, Action action)
        {
            Button(detailsRoot, title, 0, y, 230, 42, action);
            y += 49;
        }

        public void ShowResults()
        {
            InventoryOpen = false;
            inventory = null;
            Clear(page);
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            var root = CenterPanel("Raid result");
            Label(root, "RAID REPORT", 38, 55, 55, 1100, 60);
            Label(root, session.Result, 26, 55, 155, 1100, 310);
            Button(root, "RETURN TO STASH", 55, 540, 360, 70, session.ReturnToMenu);
        }
        public void ShowError()
        {
            InventoryOpen = false;
            inventory = null;
            Clear(page);
            hud.gameObject.SetActive(false);
            touch.gameObject.SetActive(false);
            var root = CenterPanel("Error");
            Label(root, "SESSION STOPPED", 36, 50, 50, 1100, 65);
            Label(root, session.Error, 23, 50, 150, 1100, 440);
        }
        public void Notify(string message) { toast.text = message; toastUntil = Time.unscaledTime + 5; toast.transform.SetAsLastSibling(); }
        public void HitMarker() { hitUntil = Time.unscaledTime + 0.12f; }

        void BuildTouch()
        {
            var look = Rect("Look zone", touch, 0.4f, 0, 1, 0.85f);
            look.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            var lookControl = look.gameObject.AddComponent<TouchControl>();
            lookControl.Input = session.Input; lookControl.Action = "Look";
            var bottomLeft = Rect("Left controls", touch, 0, 0, 0, 0);
            var bottomRight = Rect("Right controls", touch, 1, 0, 1, 0);
            var stick = TouchButton(bottomLeft, "Move", "", 35, -210, 180, 180);
            var knob = At("Stick knob", stick.transform, 60, 60, 60, 60);
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0.5f, 0.5f);
            knob.anchoredPosition = Vector2.zero;
            var image = knob.gameObject.AddComponent<Image>(); image.color = new Color(0.8f, 0.8f, 0.7f, 0.35f); image.raycastTarget = false;
            stick.Knob = knob;
            TouchButton(bottomLeft, "Sprint", "SPRINT", 235, -105, 100, 70);
            TouchButton(bottomRight, "Fire", "FIRE", -150, -225, 125, 115);
            TouchButton(bottomRight, "Aim", "AIM", -280, -165, 105, 80);
            TouchButton(bottomRight, "Reload", "RELOAD", -150, -98, 125, 65);
            TouchButton(bottomRight, "Crouch", "CROUCH", -395, -100, 105, 65);
            TouchButton(bottomRight, "Interact", "USE", -280, -260, 105, 70);
            TouchButton(bottomRight, "Heal", "HEAL", -395, -190, 105, 65);
            TouchButton(bottomRight, "Inventory", "BAG", -150, -320, 125, 65);
        }
        TouchControl TouchButton(Transform parent, string action, string title, float x, float y, float w, float h)
        {
            var rect = At(action, parent, x, y, w, h);
            rect.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.18f, 0.19f, 0.55f);
            Label(rect, title, 19, 4, 4, w - 8, h - 8, TextAnchor.MiddleCenter);
            var control = rect.gameObject.AddComponent<TouchControl>();
            control.Input = session.Input; control.Action = action;
            return control;
        }
        void UpdateSafeArea()
        {
            lastSafe = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            safe.anchorMin = new Vector2(lastSafe.xMin / Screen.width, lastSafe.yMin / Screen.height);
            safe.anchorMax = new Vector2(lastSafe.xMax / Screen.width, lastSafe.yMax / Screen.height);
        }
        void Update()
        {
            if (Screen.safeArea != lastSafe || Screen.width != lastScreen.x || Screen.height != lastScreen.y) UpdateSafeArea();
            if (Time.unscaledTime > toastUntil) toast.text = "";
            if (session.Phase != SessionPhase.Raid || session.Player == null) return;
            var player = session.Player;
            var weapon = player.Weapon;
            string ammo = weapon.Magazine == null ? "NO WEAPON" : weapon.Magazine.Reloading ? "RELOADING" : weapon.Magazine.Rounds + " / " + weapon.Reserve + (weapon.Automatic ? " AUTO" : " SEMI");
            hudStatus.text = "HP " + player.Health.Current.ToString("0") + "  /  STA " + player.Stamina.ToString("0") + "  /  " + session.Loadout.Weight.ToString("0.0") + " kg\n" + ammo;
            var exit = session.World.Exit;
            float distance = Vector3.Distance(player.transform.position, exit.transform.position);
            float angle = Mathf.Repeat(player.transform.eulerAngles.y, 360);
            compass.text = "HEADING " + angle.ToString("000") + "  |  EXIT NE " + distance.ToString("0") + "m\nRAID " + Mathf.FloorToInt(session.RaidRemaining / 60).ToString("00") + ":" + Mathf.FloorToInt(session.RaidRemaining % 60).ToString("00") + "  |  KILLS " + session.Kills;
            prompt.text = exit.Inside ? "EXTRACTING " + exit.Timer.Remaining.ToString("0.0") + "s / Stay in the green zone" : player.InteractionHint;
            crosshair.text = Time.unscaledTime < hitUntil ? "x" : "+";
            crosshair.color = Time.unscaledTime < hitUntil ? accent : textColor;
            if (loot != null && Vector3.Distance(loot.transform.position, player.transform.position) > 4)
            {
                loot = null;
                selectedBag = null;
                selectedId = null;
                placing = false;
                RefreshInventory();
            }
        }
    }
}
