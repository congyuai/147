using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dustline
{
    [DefaultExecutionOrder(-100)]
    public sealed class GameSession : MonoBehaviour
    {
        public SessionPhase Phase { get; private set; } = SessionPhase.Menu;
        public ItemCatalog Catalog { get; private set; }
        public LootTable LootTable { get; private set; }
        public LootTable HighValueLootTable { get; private set; }
        public BladeCatalog Blades { get; private set; }
        public string BoundBladeId => string.IsNullOrEmpty(Profile.BoundBladeId) ? BladeCatalog.DefaultId : Profile.BoundBladeId;
        public WeaponDefinition BoundBlade => Blades.Resolve(BoundBladeId);
        public string BoundBladeName
        {
            get
            {
                foreach (var entry in Blades.Blades)
                    if (entry != null && entry.Weapon == BoundBlade) return entry.DisplayName;
                return "唐刀";
            }
        }
        public GridInventory Stash { get; private set; }
        public GridInventory Loadout { get; private set; }
        public ProfileData Profile { get; private set; }
        public PlayerInputReader Input { get; private set; }
        public GameUI UI { get; private set; }
        public PlayerController Player { get; private set; }
        public WorldBuilder World { get; private set; }
        public RaidCombatEffects Effects { get; private set; }
        public RaidEnvironmentAudio EnvironmentAudio { get; private set; }
        public int FrameRate { get; private set; }
        public int QualityLevel { get; private set; }
        public float ResolutionScale { get; private set; }
        public OperatorStyle SelectedOperator { get; private set; }
        public float RaidRemaining { get; private set; }
        public int Kills;
        public string Result { get; private set; }
        public string Error { get; private set; }
        public string Notice { get; private set; }
        const string OperatorPreference = "Dustline.OperatorStyle.v1";
        const string SprintPreference = "Dustline.TouchSprintThreshold.v1";
        const string QualityPreference = "Dustline.QualityLevel.v1";
        const string ResolutionPreference = "Dustline.ResolutionScale.v1";
        const string LookPreference = "Dustline.ViewSensitivity.v1";
        const string GyroPreference = "Dustline.GyroSensitivity.v1";
        const string GyroUsagePreference = "Dustline.GyroUsage.v1";
        const string AimPreference = "Dustline.AimGesture.v1";
        ProfileStore store;
        Camera menuCamera;
        RenderPipelineAsset originalPipeline;
        readonly UniversalRenderPipelineAsset[] graphics = new UniversalRenderPipelineAsset[3];
        bool focused = true, paused, preferencesDirty;
        float refreshCheckAt;

        void Awake()
        {
            FrameRate = RaidFrameRate.Load();
            RaidFrameRate.Apply(FrameRate);
            SelectedOperator = PlayerPrefs.GetInt(OperatorPreference, 0) == 1 ? OperatorStyle.Female : OperatorStyle.Male;
            Input = gameObject.AddComponent<PlayerInputReader>();
            Input.SprintThreshold = PlayerInputReader.ClampSprintThreshold(PlayerPrefs.GetInt(SprintPreference, PlayerInputReader.DefaultSprintThreshold));
            Input.LookSensitivity = Sensitivity(PlayerPrefs.GetFloat(LookPreference, 1f));
            Input.GyroSensitivity = Sensitivity(PlayerPrefs.GetFloat(GyroPreference, 1f));
            Input.GyroUsage = (GyroMode)Mathf.Clamp(PlayerPrefs.GetInt(GyroUsagePreference, 0), 0, 2);
            Input.SetAimMode((AimMode)Mathf.Clamp(PlayerPrefs.GetInt(AimPreference, 0), 0, 2));
            QualityLevel = Mathf.Clamp(PlayerPrefs.GetInt(QualityPreference, 1), 0, 2);
            ResolutionScale = NormalizeScale(PlayerPrefs.GetFloat(ResolutionPreference, 1f));
            originalPipeline = QualitySettings.renderPipeline;
            var cameraObject = new GameObject("Menu camera", typeof(Camera));
            cameraObject.transform.SetParent(transform);
            menuCamera = cameraObject.GetComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.cullingMask &= ~FirstPersonView.Mask;
            menuCamera.backgroundColor = new Color(0.025f, 0.035f, 0.045f);
            ApplyDisplayPreferences();
            UI = gameObject.AddComponent<GameUI>();
            UI.Initialize(this);
            try
            {
                Catalog = Resources.Load<ItemCatalog>("Dustline/Items");
                LootTable = Resources.Load<LootTable>("Dustline/Loot");
                HighValueLootTable = Resources.Load<LootTable>("Dustline/HighValueLoot");
                Blades = Resources.Load<BladeCatalog>("Dustline/Blades");
                if (Catalog == null || LootTable == null || HighValueLootTable == null || Blades == null || Blades.Resolve(null) == null)
                    throw new InvalidDataException("缺少游戏资源，请在编辑器中执行 Dustline > Prepare Project。");
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
                        Notice = "上次突袭中断，随身装备与物资已丢失。";
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
            if (Application.isMobilePlatform && Time.unscaledTime >= refreshCheckAt)
            {
                refreshCheckAt = Time.unscaledTime + 2;
                RaidFrameRate.Apply(FrameRate);
            }
            if (focused && !paused)
            {
                if (Input.EscapePressed || Input.SettingsPressed) UI.BackOrSettings();
                else if (Phase == SessionPhase.Raid && Input.MapPressed) UI.ToggleMap();
                else if (Phase == SessionPhase.Raid && Input.InventoryPressed) UI.ToggleInventory();
            }
            Input.Gameplay = AcceptsGameplay;
            if (Phase != SessionPhase.Raid) return;
            RaidRemaining = Mathf.Max(0, RaidRemaining - Time.deltaTime);
            if (RaidRemaining <= 0) EndRaid(false, "撤离失败：突袭时间已耗尽");
        }

        bool AcceptsGameplay => Phase == SessionPhase.Raid && focused && !paused && !UI.BlocksGameplay;

        public void SelectFrameRate(int value)
        {
            FrameRate = RaidFrameRate.Normalize(value);
            RaidFrameRate.Apply(FrameRate);
            RaidFrameRate.Save(FrameRate);
        }

        internal void SelectQuality(int value)
        {
            QualityLevel = Mathf.Clamp(value, 0, 2);
            ApplyDisplayPreferences();
            PlayerPrefs.SetInt(QualityPreference, QualityLevel);
            preferencesDirty = true;
        }

        internal void SelectResolution(float value)
        {
            ResolutionScale = NormalizeScale(value);
            ApplyDisplayPreferences();
            PlayerPrefs.SetFloat(ResolutionPreference, ResolutionScale);
            preferencesDirty = true;
        }

        static float NormalizeScale(float value) => value < 0.725f ? 0.65f : value < 0.9f ? 0.80f : 1f;
        static float Sensitivity(float value) => float.IsNaN(value) ? 1f : Mathf.Clamp(value, 0.25f, 3f);

        void ApplyDisplayPreferences()
        {
            if (graphics[QualityLevel] == null)
            {
                var baseline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
                var asset = QualityLevel == 1 ? baseline : Resources.Load<UniversalRenderPipelineAsset>(
                    QualityLevel == 0 ? "Dustline/Rendering/Low" : "Dustline/Rendering/High");
                if (asset == null) asset = baseline;
                if (asset != null)
                {
                    graphics[QualityLevel] = Instantiate(asset);
                    graphics[QualityLevel].hideFlags = HideFlags.DontSave;
                }
            }
            var active = graphics[QualityLevel];
            if (active != null)
            {
                active.renderScale = ResolutionScale;
                if (QualitySettings.renderPipeline != active) QualitySettings.renderPipeline = active;
            }
        }

        internal void SelectControlOptions(float look, float gyro, GyroMode usage, AimMode aim)
        {
            Input.LookSensitivity = Sensitivity(look);
            Input.GyroSensitivity = Sensitivity(gyro);
            Input.GyroUsage = (GyroMode)Mathf.Clamp((int)usage, 0, 2);
            Input.SetAimMode((AimMode)Mathf.Clamp((int)aim, 0, 2));
            PlayerPrefs.SetFloat(LookPreference, Input.LookSensitivity);
            PlayerPrefs.SetFloat(GyroPreference, Input.GyroSensitivity);
            PlayerPrefs.SetInt(GyroUsagePreference, (int)Input.GyroUsage);
            PlayerPrefs.SetInt(AimPreference, (int)Input.AimBehavior);
            preferencesDirty = true;
        }

        internal void SelectSprintThreshold(int value)
        {
            value = PlayerInputReader.ClampSprintThreshold(value);
            if (Input.SprintThreshold == value) return;
            Input.SprintThreshold = value;
            PlayerPrefs.SetInt(SprintPreference, value);
            preferencesDirty = true;
        }

        internal void SaveControlPreferences()
        {
            if (!preferencesDirty) return;
            PlayerPrefs.Save();
            preferencesDirty = false;
        }

        internal void SelectOperator(OperatorStyle style)
        {
            if (Phase != SessionPhase.Menu || SelectedOperator == style) return;
            SelectedOperator = style;
            PlayerPrefs.SetInt(OperatorPreference, style == OperatorStyle.Female ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal bool SelectBlade(string id)
        {
            if (Phase != SessionPhase.Menu) return false;
            foreach (var choice in Blades.Blades)
            {
                if (choice == null || choice.Id != id || choice.Weapon == null) continue;
                if (id == BoundBladeId) return true;
                var prior = Profile.BoundBladeId;
                Profile.BoundBladeId = id;
                try { PersistSafeState(); }
                catch (Exception error) { Profile.BoundBladeId = prior; Fail(error); return false; }
                return true;
            }
            return false;
        }

        internal bool SelectWeaponPart(GridInventory bag, string instanceId, WeaponModSlot slot, int level)
        {
            if (Phase != SessionPhase.Menu || (bag != Stash && bag != Loadout)) return false;
            var item = bag.Find(instanceId);
            if (item == null || !WeaponTuning.SupportsUpgrades(Catalog.Find(item.ItemId)?.Weapon)
                || !WeaponTuning.TrySelect(item.Mods, slot, level, out var updated, out int cost)) return false;
            if (Profile.Credits < cost) { UI.Notify("信用点不足。"); return false; }
            var prior = item.Mods;
            item.Mods = updated;
            Profile.Credits -= cost;
            try { PersistSafeState(); }
            catch (Exception error)
            {
                item.Mods = prior;
                Profile.Credits += cost;
                Fail(error);
                return false;
            }
            bag.Touch();
            return true;
        }

        public void StartRaid()
        {
            if (Phase != SessionPhase.Menu) return;
            Phase = SessionPhase.Loading;
            UI.ShowLoading();
            StartCoroutine(BuildRaid());
        }

        IEnumerator BuildRaid()
        {
            yield return null;
            try
            {
                Kills = 0;
                RaidRemaining = 900;
                var worldObject = new GameObject("Raid world");
                worldObject.transform.SetParent(transform);
                World = worldObject.AddComponent<WorldBuilder>();
                World.Build(this);
                worldObject.AddComponent<HarborAtmosphere>().Initialize(this, World.Sun);
                Effects = worldObject.AddComponent<RaidCombatEffects>();
                Effects.Initialize();
                var actor = new GameObject("Player");
                actor.transform.SetParent(worldObject.transform);
                actor.transform.position = World.Spawn;
                actor.transform.rotation = Quaternion.Euler(0, 40, 0);
                Player = actor.AddComponent<PlayerController>();
                Player.Initialize(this);
                EnvironmentAudio = worldObject.AddComponent<RaidEnvironmentAudio>();
                EnvironmentAudio.Initialize(this);
                World.SpawnBots();
                Profile.Stash = Stash.Snapshot();
                Profile.Loadout.Clear();
                Profile.RaidInProgress = true;
                Profile.Raids++;
                store.Save(Profile);
                menuCamera.enabled = false;
                Phase = SessionPhase.Raid;
                UI.ShowRaid();
            }
            catch (Exception error)
            {
                ClearRaidWorld();
                Fail(error);
            }
        }

        public void EndRaid(bool extracted, string reason)
        {
            if (Phase != SessionPhase.Raid) return;
            Phase = SessionPhase.Results;
            Input.Gameplay = false;
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
                Result = reason + "\n击败敌人：" + Kills + (extracted ? "\n带回的装备与物资已存入仓库，放不下的仍安全保留在整备背包中。\n奖励：" + (150 + Kills * 25) + " 信用点" : "\n随身装备与搜刮物资已丢失，绑定近战武器已保留。");
                UI.ShowResults();
            }
            catch (Exception error) { Fail(error); }
        }

        public void ReturnToMenu()
        {
            if (Phase != SessionPhase.Results) return;
            ClearRaidWorld();
            Phase = SessionPhase.Menu;
            Notice = null;
            UI.ShowMenu();
        }

        void ClearRaidWorld()
        {
            if (World != null) { World.gameObject.SetActive(false); Destroy(World.gameObject); }
            World = null;
            Player = null;
            Effects = null;
            EnvironmentAudio = null;
            menuCamera.enabled = true;
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
            if (Profile.Credits < cost) { UI.Notify("信用点不足。"); return; }
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
            { UI.Notify("仅在没有步枪且信用点不足以购买时，才能领取应急装备。"); return; }
            if (!Loadout.TryAdd("rifle", 1, out var error)) { UI.Notify(error); return; }
            Loadout.TryAdd("ammo", 30, out _);
            InventoryChanged();
            UI.RefreshInventory();
        }

        public void UseMedicine(string id = null)
        {
            if (Phase != SessionPhase.Raid || !Player.Health.Alive) return;
            if (Player.Health.Current >= Player.Health.Maximum) { UI.Notify("生命值已满，无需治疗。"); return; }
            ItemStack item = id == null ? null : Loadout.Find(id);
            if (id == null) foreach (var candidate in Loadout.Items) if (Catalog.Find(candidate.ItemId).Kind == ItemKind.Medicine) { item = candidate; break; }
            if (item == null || Catalog.Find(item.ItemId).Kind != ItemKind.Medicine) { UI.Notify("没有可用的医疗品。"); return; }
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

        public void SyncControls()
        {
            Input.Gameplay = AcceptsGameplay;
            SetCursor(!Input.Gameplay);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (UI == null) return;
            if (hasFocus) RaidFrameRate.Apply(FrameRate);
            SyncControls();
        }
        void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            if (UI == null) return;
            if (paused) SaveControlPreferences();
            else RaidFrameRate.Apply(FrameRate);
            SyncControls();
        }
        void Fail(Exception error)
        {
            Phase = SessionPhase.Error;
            Input.Gameplay = false;
            SetCursor(true);
            Error = "无法安全继续本次行动，现有存档未被重置。\n请检查存储权限、剩余空间和游戏资源，详细原因见日志。";
            Debug.LogException(error);
            UI.ShowError();
        }
        void OnDestroy()
        {
            SaveControlPreferences();
            foreach (var asset in graphics)
                if (asset != null)
                {
                    if (QualitySettings.renderPipeline == asset) QualitySettings.renderPipeline = originalPipeline;
                    Destroy(asset);
                }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
