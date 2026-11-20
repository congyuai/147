using System;
using System.IO;
using UnityEngine;

namespace Dustline
{
    [DefaultExecutionOrder(-100)]
    public sealed class GameSession : MonoBehaviour
    {
        public SessionPhase Phase { get; private set; } = SessionPhase.Menu;
        public ItemCatalog Catalog { get; private set; }
        public LootTable LootTable { get; private set; }
        public GridInventory Stash { get; private set; }
        public GridInventory Loadout { get; private set; }
        public ProfileData Profile { get; private set; }
        public PlayerInputReader Input { get; private set; }
        public GameUI UI { get; private set; }
        public PlayerController Player { get; private set; }
        public WorldBuilder World { get; private set; }
        public float RaidRemaining { get; private set; }
        public int Kills;
        public string Result { get; private set; }
        public string Error { get; private set; }
        public string Notice { get; private set; }
        ProfileStore store;
        Camera menuCamera;
        bool focused = true;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Input = gameObject.AddComponent<PlayerInputReader>();
            var cameraObject = new GameObject("Menu camera", typeof(Camera));
            cameraObject.transform.SetParent(transform);
            menuCamera = cameraObject.GetComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = new Color(0.025f, 0.035f, 0.045f);
            UI = gameObject.AddComponent<GameUI>();
            UI.Initialize(this);
            try
            {
                Catalog = Resources.Load<ItemCatalog>("Dustline/Items");
                LootTable = Resources.Load<LootTable>("Dustline/Loot");
                if (Catalog == null || LootTable == null) throw new InvalidDataException("Game assets are missing. In the Editor, run Dustline > Prepare Project.");
                Catalog.RebuildIndex();
                Stash = new GridInventory(12, 10, 300, Catalog);
                Loadout = new GridInventory(6, 6, 30, Catalog);
                store = new ProfileStore(Path.Combine(Application.persistentDataPath, "dustline-profile-v1.json"));
                Profile = store.Load();
                if (Profile == null)
                {
                    Profile = new ProfileData();
                    AddStarterKit(Loadout);
                    Stash.TryAdd("ammo", 120, out _);
                    Stash.TryAdd("medkit", 3, out _);
                    PersistSafeState();
                }
                else
                {
                    Stash.Restore(Profile.Stash);
                    if (Profile.RaidInProgress)
                    {
                        Profile.Loadout.Clear();
                        Profile.RaidInProgress = false;
                        Notice = "Previous raid was interrupted: carried gear was lost.";
                        store.Save(Profile);
                    }
                    Loadout.Restore(Profile.Loadout);
                }
                UI.ShowMenu();
            }
            catch (Exception error) { Fail(error); }
        }

        void AddStarterKit(GridInventory bag)
        {
            bag.TryAdd("rifle", 1, out _);
            bag.TryAdd("armor", 1, out _);
            bag.TryAdd("ammo", 90, out _);
            bag.TryAdd("medkit", 2, out _);
            foreach (var item in bag.Snapshot())
            {
                if (item.ItemId == "rifle") bag.TryEquip(item.InstanceId, EquipmentSlot.Weapon, out _);
                if (item.ItemId == "armor") bag.TryEquip(item.InstanceId, EquipmentSlot.Armor, out _);
            }
        }

        void Update()
        {
            Input.Gameplay = Phase == SessionPhase.Raid && !UI.InventoryOpen && focused;
            if (Phase != SessionPhase.Raid) return;
            if (Input.InventoryPressed || Input.EscapePressed) UI.ToggleInventory();
            Input.Gameplay = !UI.InventoryOpen && focused;
            RaidRemaining = Mathf.Max(0, RaidRemaining - Time.deltaTime);
            if (RaidRemaining <= 0) EndRaid(false, "Missing in action: raid time expired");
        }

        public void StartRaid()
        {
            if (Phase != SessionPhase.Menu) return;
            if (Loadout.Equipped(EquipmentSlot.Weapon) == null) { UI.Notify("Equip a weapon before deployment."); return; }
            try
            {
                // The durable profile relinquishes carried items before the raid can begin.
                Profile.Stash = Stash.Snapshot();
                Profile.Loadout.Clear();
                Profile.RaidInProgress = true;
                Profile.Raids++;
                store.Save(Profile);
                Kills = 0;
                RaidRemaining = 900;
                var worldObject = new GameObject("Raid world");
                worldObject.transform.SetParent(transform);
                World = worldObject.AddComponent<WorldBuilder>();
                World.Build(this);
                var actor = new GameObject("Player");
                actor.transform.SetParent(worldObject.transform);
                actor.transform.position = World.Spawn;
                actor.transform.rotation = Quaternion.Euler(0, 40, 0);
                Player = actor.AddComponent<PlayerController>();
                Player.Initialize(this);
                World.SpawnBots();
                menuCamera.enabled = false;
                Phase = SessionPhase.Raid;
                UI.ShowRaid();
                SetCursor(false);
            }
            catch (Exception error) { Fail(error); }
        }

        public void EndRaid(bool extracted, string reason)
        {
            if (Phase != SessionPhase.Raid) return;
            Phase = SessionPhase.Results;
            Input.Gameplay = false;
            Input.ReleaseTouch();
            SetCursor(true);
            try
            {
                if (extracted)
                {
                    foreach (var item in Loadout.Snapshot()) Loadout.TryTransferTo(item.InstanceId, Stash, out _);
                    Profile.Credits += 150 + Kills * 25;
                    Profile.Extractions++;
                }
                else Loadout = new GridInventory(6, 6, 30, Catalog);
                Profile.RaidInProgress = false;
                PersistSafeState();
                Result = reason + "\nEliminations: " + Kills + (extracted ? "\nRecovered gear is in your stash. Overflow stays in your safe loadout.\nReward: " + (150 + Kills * 25) + " credits" : "\nAll carried equipment and loot were lost.");
                UI.ShowResults();
            }
            catch (Exception error) { Fail(error); }
        }

        public void ReturnToMenu()
        {
            if (Phase != SessionPhase.Results) return;
            if (World != null) { World.gameObject.SetActive(false); Destroy(World.gameObject); }
            World = null;
            Player = null;
            Phase = SessionPhase.Menu;
            menuCamera.enabled = true;
            Notice = null;
            UI.ShowMenu();
        }

        public void PersistSafeState()
        {
            Profile.Stash = Stash.Snapshot();
            Profile.Loadout = Loadout.Snapshot();
            store.Save(Profile);
        }

        public void InventoryChanged()
        {
            if (Phase == SessionPhase.Menu)
            {
                try { PersistSafeState(); }
                catch (Exception error) { Fail(error); }
            }
            if (Phase == SessionPhase.Raid && Player != null) Player.Weapon.SyncEquipment();
        }

        public void Buy(string itemId, int quantity)
        {
            if (Phase != SessionPhase.Menu) return;
            var item = Catalog.Find(itemId);
            int cost = item.Value * quantity;
            if (Profile.Credits < cost) { UI.Notify("Not enough credits."); return; }
            if (!Stash.TryAdd(itemId, quantity, out var error)) { UI.Notify(error); return; }
            Profile.Credits -= cost;
            InventoryChanged();
            UI.RefreshInventory();
        }

        public void Sell(GridInventory from, string id)
        {
            if (Phase != SessionPhase.Menu) return;
            var item = from.Find(id);
            if (item == null) return;
            Profile.Credits += Mathf.Max(1, Catalog.Find(item.ItemId).Value / 2) * item.Quantity;
            from.Remove(id);
            InventoryChanged();
            UI.RefreshInventory();
        }

        public void EmergencyKit()
        {
            if (Phase != SessionPhase.Menu) return;
            if (Profile.Credits >= Catalog.Find("rifle").Value || Stash.Count("rifle") + Loadout.Count("rifle") > 0)
            { UI.Notify("Emergency gear is available only when you have no rifle and cannot afford one."); return; }
            if (!Loadout.TryAdd("rifle", 1, out var error)) { UI.Notify(error); return; }
            Loadout.TryAdd("ammo", 30, out _);
            InventoryChanged();
            UI.RefreshInventory();
        }

        public void UseMedicine(string id = null)
        {
            if (Phase != SessionPhase.Raid || !Player.Health.Alive) return;
            if (Player.Health.Current >= Player.Health.Maximum) { UI.Notify("Health is already full."); return; }
            ItemStack item = id == null ? null : Loadout.Find(id);
            if (id == null) foreach (var candidate in Loadout.Items) if (Catalog.Find(candidate.ItemId).Kind == ItemKind.Medicine) { item = candidate; break; }
            if (item == null || Catalog.Find(item.ItemId).Kind != ItemKind.Medicine) { UI.Notify("No medical supplies."); return; }
            var definition = Catalog.Find(item.ItemId);
            if (Loadout.Consume(item.ItemId, 1)) Player.Health.Heal(definition.HealAmount);
            UI.RefreshInventory();
        }

        public void SetCursor(bool visible)
        {
            Cursor.lockState = visible || Input.TouchMode ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible || Input.TouchMode;
            Input.ReleaseTouch();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (Input == null) return;
            Input.ReleaseTouch();
            SetCursor(!hasFocus || Phase != SessionPhase.Raid || UI.InventoryOpen);
        }
        void OnApplicationPause(bool paused)
        {
            focused = !paused;
            if (Input != null) Input.ReleaseTouch();
        }
        void Fail(Exception error)
        {
            Phase = SessionPhase.Error;
            Input.Gameplay = false;
            SetCursor(true);
            Error = "Could not continue safely. Your existing save has not been reset.\n" + error.Message;
            Debug.LogException(error);
            UI.ShowError();
        }
        void OnDestroy() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
