using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dustline
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class GameSession : MonoBehaviour
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
        public GridInventory ActiveFieldBag => Phase == SessionPhase.Practice || Phase == SessionPhase.PracticeLoading ? practiceBag : Loadout;
        public bool InField => Phase == SessionPhase.Raid || Phase == SessionPhase.Practice;
        internal PracticeRange Practice { get; private set; }
        GridInventory practiceBag;
        List<ItemStack> practiceHands;
        string preferredMedicine, practiceMedicine;
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
        internal float RaidDuration { get; private set; } = 900f;
        internal float RaidThreat { get; private set; } = 1f;
        internal int ActiveSite { get; private set; }
        internal int ActiveTier { get; private set; }
        internal int PreparedSite { get; private set; } = -1;
        internal int PreparedTier { get; private set; }
        internal int PlayerLevel => Deployment.Level(Profile);
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
            menuCamera.cullingMask &= ~(FirstPersonView.Mask | ItemPreviewStage.LayerMask | OperatorPreview.LayerMask | StrategyTerrain.LayerMask);
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
                Stash = GridInventory.Warehouse(Catalog);
                Loadout = GridInventory.Carried(Catalog);
                store ??= new ProfileStore(Path.Combine(Application.persistentDataPath, "dustline-profile-v1.json"));
                Profile = store.Load();
                if (Profile == null)
                {
                    Profile = new ProfileData { Version = ProfileData.CurrentVersion, Credits = ProfileData.StartingCredits };
                    AddStarterKit(Loadout);
                    Stash.TryAdd("ammo", 120, out _);
                    Stash.TryAdd("medkit", 3, out _);
                    PersistSafeState();
                }
                else
                {
                    bool upgrade = Profile.Version < ProfileData.CurrentVersion;
                    if (Profile.RaidInProgress && Profile.Version < 3) Profile.Loadout.Clear();
                    if (upgrade) UpgradeInventory(Profile);
                    Stash.Restore(Profile.Stash);
                    Loadout.Restore(Profile.Loadout);
                    if (Profile.RaidInProgress)
                    {
                        Profile.RaidInProgress = false;
                        Notice = "上次突袭中断，已保留绑定近战和安全箱物品。";
                        upgrade = true;
                    }
                    if (upgrade || !Profile.BirthdayGiftClaimed) PersistSafeState();
                }
                if (Phase == SessionPhase.Menu) UI.ShowMenu();
            }
            catch (Exception error) { Fail(error); }
        }

        void AddStarterKit(GridInventory bag)
        {
            var stock = new GridInventory(12, 10, 100, Catalog);
            foreach (var (id, slot) in new[] { ("pack_1", EquipmentSlot.Pack), ("rig_1", EquipmentSlot.Rig),
                ("rifle", EquipmentSlot.Weapon), ("armor", EquipmentSlot.Armor) })
            {
                var item = new ItemStack { ItemId = id, InstanceId = Guid.NewGuid().ToString("N") };
                if (!stock.TryInsert(item, out var error) || !bag.TryWearFrom(stock, item.InstanceId, slot, out error))
                    throw new InvalidDataException(error);
            }
            if (!bag.TryAdd("ammo", 90, out var supplies) || !bag.TryAdd("medkit", 2, out supplies))
                throw new InvalidDataException(supplies);
        }

        void Update()
        {
            if (Application.isMobilePlatform && FrameRate == RaidFrameRate.Uncapped && focused && !paused && Time.unscaledTime >= refreshCheckAt)
            {
                refreshCheckAt = Time.unscaledTime + 2;
                RaidFrameRate.Apply(FrameRate);
            }
            if (focused && !paused)
            {
                if (Input.EscapePressed || Input.SettingsPressed) UI.BackOrSettings();
                else if (InField && Input.HealthPressed) UI.ToggleHealth();
                else if (Phase == SessionPhase.Practice && (Input.MapPressed || Input.InventoryPressed)) UI.TogglePracticePanel();
                else if (Phase == SessionPhase.Raid && Input.MapPressed) UI.ToggleMap();
                else if (Phase == SessionPhase.Raid && Input.InventoryPressed) UI.ToggleInventory();
            }
            Input.Gameplay = AcceptsGameplay;
            if (Phase != SessionPhase.Raid) return;
            RaidRemaining = Mathf.Max(0, RaidRemaining - Time.deltaTime);
            if (RaidRemaining <= 0) EndRaid(false, "撤离失败：突袭时间已耗尽");
        }

        bool AcceptsGameplay => InField && focused && !paused && !UI.BlocksGameplay;

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
                if (SaveMenuInventory(Stash, Loadout, Profile.Credits)) return true;
                Profile.BoundBladeId = prior;
                return false;
            }
            return false;
        }

        internal bool SelectWeaponPart(GridInventory bag, string instanceId, WeaponModSlot slot, int level)
        {
            if (Phase != SessionPhase.Menu || (bag != Stash && bag != Loadout)) return false;
            var item = bag.Find(instanceId);
            if (item == null || !WeaponTuning.SupportsUpgrades(Catalog.Find(item.ItemId)?.Weapon)
                || !item.Parts.Get((PartSlot)slot).Empty
                || !WeaponTuning.TrySelect(item.Mods, slot, level, out var updated, out int cost)) return false;
            if (Profile.Credits < cost) { UI.Notify("信用点不足。"); return false; }
            var draft = bag.Draft();
            draft.Find(instanceId).Mods = updated;
            return SaveMenuInventory(bag == Stash ? draft : Stash, bag == Loadout ? draft : Loadout, Profile.Credits - cost);
        }

        internal bool ConfirmDeployment(int site, int tier)
        {
            if (Phase != SessionPhase.Menu) return false;
            var reason = Deployment.EntryError(site, tier, Loadout.EquipmentValue, PlayerLevel);
            if (reason != null) { UI.Notify(reason); return false; }
            PreparedSite = site;
            PreparedTier = tier;
            return true;
        }

        public void StartRaid()
        {
            if (Phase != SessionPhase.Menu) return;
            string blocked = Deployment.EntryError(PreparedSite, PreparedTier, Loadout.EquipmentValue, PlayerLevel);
            if (blocked != null) { UI.Notify(PreparedSite < 0 ? "请先选图并确认配装。" : blocked); return; }
            if (!Deployment.TryDifficulty(PreparedSite, PreparedTier, out var difficulty)) return;
            if (!UI.HasDeploymentResources(PreparedSite)) { UI.Notify("行动地图资源未就绪，请执行 Prepare Project。"); return; }
            ActiveSite = PreparedSite;
            ActiveTier = PreparedTier;
            RaidDuration = difficulty.Seconds;
            RaidThreat = difficulty.Threat;
            Phase = SessionPhase.Loading;
            UI.ShowLoading();
            StartCoroutine(BuildRaid());
        }

        IEnumerator BuildRaid()
        {
            yield return null;
            var beforeDeployment = Loadout;
            bool saved = false;
            try
            {
                var carried = beforeDeployment.Snapshot();
                var atHome = carried.FindAll(item => item.Area == CargoArea.Legacy);
                if (atHome.Count != 0)
                {
                    carried.RemoveAll(item => item.Area == CargoArea.Legacy);
                    var fieldLoadout = GridInventory.Carried(Catalog);
                    fieldLoadout.Restore(carried);
                    Loadout = fieldLoadout;
                }
                Kills = 0;
                RaidRemaining = RaidDuration;
                var worldObject = new GameObject("Raid world");
                worldObject.transform.SetParent(transform);
                World = worldObject.AddComponent<WorldBuilder>();
                World.Build(this, ActiveSite == Deployment.Quarry);
                if (!World.IsQuarry) worldObject.AddComponent<HarborAtmosphere>().Initialize(this, World.Sun);
                CreateFieldPlayer(worldObject, World.Spawn, World.IsQuarry ? 0f : 40f, false);
                World.SpawnBots();
                var deployed = Profile.WithInventory(Stash, Loadout, Profile.Credits);
                deployed.Loadout = RetainedCargo(atHome, Loadout);
                deployed.RaidInProgress = true;
                deployed.Raids = Profile.Raids == int.MaxValue ? int.MaxValue : Profile.Raids + 1;
                store.Save(deployed);
                saved = true;
                Profile = deployed;
                menuCamera.enabled = false;
                Phase = SessionPhase.Raid;
                UI.ShowRaid();
            }
            catch (Exception error)
            {
                if (!saved) Loadout = beforeDeployment;
                ClearRaidWorld();
                Fail(error);
            }
        }

        void CreateFieldPlayer(GameObject field, Vector3 position, float heading, bool practice)
        {
            Effects = field.AddComponent<RaidCombatEffects>();
            Effects.Initialize();
            EnvironmentAudio = field.AddComponent<RaidEnvironmentAudio>();
            EnvironmentAudio.Initialize(this, practice || (World != null && World.IsQuarry));
            var actor = new GameObject("Player");
            actor.transform.SetParent(field.transform, false);
            actor.transform.SetPositionAndRotation(position, Quaternion.Euler(0, heading, 0));
            Player = actor.AddComponent<PlayerController>();
            Player.Initialize(this);
        }

        public void StartPractice()
        {
            if (Phase != SessionPhase.Menu) return;
            practiceHands = Loadout.Snapshot().FindAll(item => LoadoutSlots.IsHand(item.Slot));
            Phase = SessionPhase.PracticeLoading;
            UI.ShowLoading(true);
            StartCoroutine(BuildPractice());
        }

        IEnumerator BuildPractice()
        {
            yield return null;
            GameObject field = null;
            try
            {
                field = new GameObject("Practice field");
                field.transform.SetParent(transform, false);
                practiceBag = CreatePracticeBag();
                Practice = field.AddComponent<PracticeRange>();
                Practice.Build();
                CreateFieldPlayer(field, Practice.Spawn, 0f, true);
                menuCamera.enabled = false;
                Phase = SessionPhase.Practice;
                UI.ShowPractice();
            }
            catch (Exception error)
            {
                ClearPractice(field);
                Fail(error);
            }
        }

        GridInventory CreatePracticeBag()
        {
            var bag = new GridInventory(8, 7, 70f, Catalog);
            var supply = new GridInventory(12, 10, 100f, Catalog);
            const string unavailable = "靶场练习装备未就绪，请执行 Dustline > Prepare Project。";
            if (!practiceHands.Exists(item => Catalog.Find(item.ItemId)?.Weapon?.Melee == false))
                Equip(new ItemStack { ItemId = WeaponDefinition.RifleId, Slot = EquipmentSlot.Weapon });
            foreach (var original in practiceHands) Equip(original);
            foreach (var id in new[] { "medkit", "bandage", "trauma_case", "tourniquet", "splint", "pain_tabs", "surgery" })
                if (!bag.TryAdd(id, id == "bandage" ? 5 : 2, out _)) throw new InvalidDataException(unavailable);
            return bag;

            void Equip(ItemStack original)
            {
                var item = original.Clone();
                item.InstanceId = Guid.NewGuid().ToString("N");
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                {
                    var socket = (PartSlot)i;
                    var part = item.Parts.Get(socket);
                    if (part.Empty) continue;
                    part.InstanceId = Guid.NewGuid().ToString("N");
                    item.Parts.Set(socket, part);
                }
                var rule = Catalog.Find(item.ItemId)?.Weapon;
                if (rule == null) throw new InvalidDataException(unavailable);
                if (!rule.Melee)
                {
                    item.LoadedAmmoId = WeaponRules.LoadedType(rule, item);
                    item.LoadedAmmo = WeaponRules.Capacity(rule, item, Catalog);
                }
                if (!supply.TryInsert(item, out _) || !bag.TryWearFrom(supply, item.InstanceId, original.Slot, out _))
                    throw new InvalidDataException(unavailable);
            }
        }

        internal void RefillPractice()
        {
            if (Phase != SessionPhase.Practice) return;
            GridInventory replacement;
            try { replacement = CreatePracticeBag(); }
            catch (InvalidDataException error) { UI.Notify(error.Message); return; }
            Player.CancelTreatment();
            practiceBag = replacement;
            Player.Weapon.SyncEquipment();
        }

        internal void PracticeHealthSample()
        {
            if (Phase != SessionPhase.Practice) return;
            Player.CancelTreatment();
            Player.Health.ApplyPracticeSample(0);
        }

        internal void PracticeSevereSample()
        {
            if (Phase != SessionPhase.Practice) return;
            Player.CancelTreatment();
            Player.Health.ApplyPracticeSample(1);
        }

        public void LeavePractice()
        {
            if (Phase != SessionPhase.Practice) return;
            ClearPractice(Practice.gameObject);
            Phase = SessionPhase.Menu;
            UI.ShowMenu();
        }

        void ClearPractice(GameObject field)
        {
            ClearField(field);
            Practice = null;
            practiceBag = null;
            practiceHands = null;
            practiceMedicine = null;
        }

        internal void PlayerDown()
        {
            if (Phase == SessionPhase.Practice)
            {
                Player.Weapon.CancelActions();
                Player.Health.Initialize(Player.Health.Maximum, Player.Health.Team);
                UI.CancelFieldGestures();
                SyncControls();
                return;
            }
            EndRaid(false, "撤离失败：你已阵亡");
        }

        internal void AbandonRaid()
        {
            EndRaid(false, "撤离失败：你主动退出了本局");
            if (Phase == SessionPhase.Results)
            {
                ReturnToMenu();
                UI.Notify("本局已退出，安全箱和绑定近战已保留。");
            }
        }

        public void EndRaid(bool extracted, string reason)
        {
            if (Phase != SessionPhase.Raid) return;
            Phase = SessionPhase.Results;
            SyncControls();
            try
            {
                var stash = extracted ? Stash.Draft() : Stash;
                var carried = StageReturn(extracted, stash);
                int reward = extracted ? (int)Math.Min(int.MaxValue - Profile.Credits, 150L + Math.Max(0, Kills) * 25L) : 0;
                var settled = Profile.WithInventory(stash, carried, Profile.Credits + reward);
                settled.RaidInProgress = false;
                if (extracted && settled.Extractions < settled.Raids) settled.Extractions++;
                store.Save(settled);
                if (extracted) Stash.ApplyDraft(stash);
                Loadout = carried;
                Profile = settled;
                Result = reason + "\n击败敌人：" + Kills + (extracted ? "\n带回的装备与物资已存入仓库，放不下的仍安全保留在整备背包中。\n奖励：" + reward + " 信用点" : "\n随身装备与搜刮物资已丢失，绑定近战武器与安全箱物品已保留。");
                UI.ShowResults();
            }
            catch (Exception error) { ClearRaidWorld(); Fail(error); }
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
            ClearField(World != null ? World.gameObject : null);
            World = null;
        }

        void ClearField(GameObject field)
        {
            Player?.CancelTreatment();
            if (field != null) { field.SetActive(false); Destroy(field); }
            Player = null;
            Effects = null;
            EnvironmentAudio = null;
            menuCamera.enabled = true;
        }

        public void PersistSafeState()
        {
            if (Phase != SessionPhase.Menu && Phase != SessionPhase.Results) return;
            SaveMenuInventory(Stash, Loadout, Profile.Credits);
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
            if (Phase != SessionPhase.Menu || quantity < 1) return;
            var item = Catalog.Find(itemId);
            if (item == null || item.NotForSale) return;
            long cost = (long)item.Value * quantity;
            if (Profile.Credits < cost) { UI.Notify("信用点不足。"); return; }
            var stash = Stash.Draft();
            if (!stash.TryAdd(itemId, quantity, out var error)) { UI.Notify(error); return; }
            if (SaveMenuInventory(stash, Loadout, Profile.Credits - (int)cost)) UI.RefreshInventory();
        }

        public void Sell(GridInventory from, string id)
        {
            if (Phase != SessionPhase.Menu || (from != Stash && from != Loadout)) return;
            var item = from.Find(id);
            if (item == null) return;
            long credits = (long)Profile.Credits + (long)Catalog.Find(item.ItemId).ResaleValue * item.Quantity;
            if (credits > int.MaxValue) { UI.Notify("信用点已达上限。"); return; }
            if (!PrepareSale(from, id, out var stash, out var loadout, out var error)) { UI.Notify(error); return; }
            if (SaveMenuInventory(stash, loadout, (int)credits)) UI.RefreshInventory();
        }

        internal ItemStack QuickMedicine(string itemId = null)
        {
            string preferred = itemId ?? (practiceBag != null ? practiceMedicine : preferredMedicine);
            var bag = ActiveFieldBag;
            if (bag == null) return null;
            ItemStack first = null;
            foreach (var stack in bag.Items)
            {
                if (!GridInventory.FieldSupply(stack) || stack.Quantity <= 0 || Catalog.Find(stack.ItemId).Kind != ItemKind.Medicine) continue;
                if (stack.ItemId == preferred) return stack;
                first ??= stack;
            }
            return itemId == null ? first : null;
        }

        internal bool SelectMedicine(string itemId)
        {
            if (!InField || QuickMedicine(itemId) == null) return false;
            if (practiceBag != null) practiceMedicine = itemId;
            else preferredMedicine = itemId;
            return true;
        }

        public void UseMedicine(string id = null) => TryUseMedicine(id, null);

        internal void TryUseMedicine(string id, BodyPart? target)
        {
            if (!InField || !Input.Gameplay || !Player.Health.Alive || Player.WeaponView.MedicineBusy) return;
            var bag = ActiveFieldBag;
            var stack = id == null ? QuickMedicine() : bag.Find(id);
            var item = stack == null ? null : Catalog.Find(stack.ItemId);
            if (item == null || item.Kind != ItemKind.Medicine || !GridInventory.FieldSupply(stack) || stack.Quantity <= 0
                || !(item.UseSeconds > 0f) || float.IsInfinity(item.UseSeconds)
                || (item.Treatment == TreatmentKind.Heal || item.Treatment == TreatmentKind.Pain)
                    && (!(item.HealAmount > 0f) || float.IsInfinity(item.HealAmount))
                || !Enum.IsDefined(typeof(MedicineForm), item.MedicineVisual))
            { UI?.Notify("没有可用的医疗品。"); return; }
            var body = Player.Health.Body;
            var part = target ?? (body != null ? body.Suggest(item.Treatment) : BodyPart.Chest);
            if (!part.HasValue || (body != null ? !body.CanTreat(part.Value, item.Treatment) : Player.Health.Current >= Player.Health.Maximum))
            { UI?.Notify("所选医疗品不适用于当前伤情。"); return; }
            Player.BeginTreatment(bag, stack.InstanceId, item, part.Value);
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
            if (!Input.Gameplay)
            {
                Player?.CancelTreatment();
                Player?.Weapon?.CancelActions();
            }
            SetCursor(!Input.Gameplay);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (UI == null) return;
            if (hasFocus) RaidFrameRate.Apply(FrameRate);
            else UI.CancelFieldGestures();
            UI.ResetFrameSample();
            SyncControls();
        }
        void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            if (UI == null) return;
            if (paused)
            {
                SaveControlPreferences();
                UI.CancelFieldGestures();
            }
            else RaidFrameRate.Apply(FrameRate);
            UI.ResetFrameSample();
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
