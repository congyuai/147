using UnityEditor;
using UnityEngine;

namespace Dustline.Editor
{
    public static partial class ProjectSetup
    {
        static void PrepareEquipment(ItemCatalog catalog, LootTable ordinary, LootTable high)
        {
            var rifle = catalog.Find("rifle").Weapon;
            rifle.AmmoFamily = AmmoFamily.Amber;
            rifle.FrameInterface = "feed-a";
            rifle.MoveFactor = 1f;
            EditorUtility.SetDirty(rifle);
            catalog.Find("ammo").AmmoFamily = AmmoFamily.Amber;
            EditorUtility.SetDirty(catalog.Find("ammo"));

            Ammo("ammo_copper", "铜线轻型弹", AmmoFamily.Copper, 2, 45);
            Ammo("ammo_silver", "银标远程弹", AmmoFamily.Silver, 4, 30);
            Ammo("ammo_slate", "岩纹散弹", AmmoFamily.Slate, 3, 24);
            Ammo("ammo_amber_range", "琥珀训练弹", AmmoFamily.Amber, 2, 60);
            Gun("dust_pistol", "潮汐 P-9 手枪", WeaponForm.Pistol, "ammo_copper", AmmoFamily.Copper, "feed-c", 2, 1, 12, 19, .22f, 58, 1.24f, .75f, 1.07f, 1, false, 132, new Color(.40f,.50f,.54f));
            Gun("dust_smg", "浪花 S-12 冲锋枪", WeaponForm.Smg, "ammo_copper", AmmoFamily.Copper, "feed-c", 3, 2, 24, 17, .11f, 72, 1.68f, .88f, 1.03f, 1, true, 218, new Color(.24f,.34f,.36f));
            Gun("dust_shotgun", "堤岸 S-6 霰弹枪", WeaponForm.Shotgun, "ammo_slate", AmmoFamily.Slate, "feed-d", 4, 1, 6, 12, .65f, 55, 2.37f, 1.28f, .94f, 5, false, 238, new Color(.49f,.32f,.22f));
            Gun("dust_marksman", "航标 M-14 精确射手步枪", WeaponForm.Marksman, "ammo_silver", AmmoFamily.Silver, "feed-b", 4, 2, 14, 41, .31f, 150, 2.22f, 1.43f, .93f, 1, false, 362, new Color(.39f,.43f,.33f));
            Gun("dust_sniper", "岬角 N-5 狙击枪", WeaponForm.Sniper, "ammo_silver", AmmoFamily.Silver, "feed-n", 5, 2, 5, 68, .98f, 198, 2.57f, 1.83f, .87f, 1, false, 452, new Color(.39f,.43f,.51f));
            Gun("dust_lmg", "堆场 L-40 轻机枪", WeaponForm.Lmg, "ammo", AmmoFamily.Amber, "feed-l", 5, 2, 40, 24, .12f, 112, 3.15f, 1.46f, .76f, 1, true, 484, new Color(.36f,.39f,.27f));

            Part("part_sight_dot", "沿线反射瞄具", PartSlot.Sight, "glass-line", 1, 0, .91f, 1, .91f);
            Part("part_sight_prism", "沿线棱镜瞄具", PartSlot.Sight, "glass-line", 2, 0, .88f, 1, 1.16f);
            Part("part_sight_mini", "微型瞄具", PartSlot.Sight, "glass-mini", 1, 0, .91f, 1, .96f);
            Part("part_grip", "稳固握把", PartSlot.Grip, "hand-line", 2, 0, .79f, 1.08f, 1);
            Part("part_grip_mini", "紧凑握套", PartSlot.Grip, "hand-mini", 1, 0, .87f, 1, .95f);
            Part("part_stock", "平衡枪托", PartSlot.Stock, "tail-line", 2, 0, .84f, .94f, 1);
            Part("part_stock_light", "轻型枪托", PartSlot.Stock, "tail-line", 1, 0, .93f, .89f, .89f);
            Part("part_muzzle", "沿线前端配件", PartSlot.Muzzle, "front-line", 1, 0, .87f, 1, 1);
            Part("part_muzzle_mini", "紧凑前端配件", PartSlot.Muzzle, "front-mini", 1, 0, .93f, 1, 1);
            Part("part_muzzle_cap", "岬角专用前端配件", PartSlot.Muzzle, "front-line", 2, 0, .80f, 1, 1.1f, "dust_sniper");
            Part("part_mag_a", "琥珀扩容弹匣", PartSlot.Magazine, "feed-a", 1, 30, 1, 1.12f, 1);
            Part("part_mag_b", "航标备用弹匣", PartSlot.Magazine, "feed-b", 1, 20, 1, 1.09f, 1);
            Part("part_mag_c", "铜线通用弹匣", PartSlot.Magazine, "feed-c", 3, 26, 1, 1.08f, 1);
            Part("part_mag_d", "堤岸备用弹匣", PartSlot.Magazine, "feed-d", 1, 9, 1, 1.12f, 1);
            Part("part_mag_n", "岬角专用弹匣", PartSlot.Magazine, "feed-n", 1, 8, 1, 1.09f, 1, "dust_sniper");
            Part("part_mag_l", "堆场供弹盒", PartSlot.Magazine, "feed-l", 2, 56, 1, 1.16f, 1);

            var oldArmor = catalog.Find("armor");
            oldArmor.Tier = 2; oldArmor.Coverage = BodyCoverage.Chest; oldArmor.Durability = 60;
            EditorUtility.SetDirty(oldArmor);
            for (int tier = 1; tier <= 6; tier++)
            {
                int n = tier;
                var shell = Gear("helmet_" + n, "护卫头盔 · " + n + " 级", ItemKind.Helmet, n, 2, 2, .9f + .16f * n, 48 + n * 38);
                shell.Coverage = BodyCoverage.Head;
                shell.DamageReduction = .12f + .10f * n;
                shell.Durability = 28 + 14 * n;
                var vest = Gear("vest_" + n, "港卫防弹衣 · " + n + " 级", ItemKind.Armor, n, 2, 3, 1.8f + .65f * n, 64 + n * 59);
                vest.Coverage = BodyCoverage.Chest | (n >= 3 ? BodyCoverage.Abdomen : BodyCoverage.None)
                    | (n >= 5 ? BodyCoverage.LeftArm | BodyCoverage.RightArm : BodyCoverage.None);
                vest.DamageReduction = .13f + .105f * n;
                vest.Durability = 28 + 22 * n;
                var pack = Gear("pack_" + n, "行旅背包 · " + n + " 级", ItemKind.Pack, n, 2, 3, .7f + .25f * n, 39 + 38 * n);
                pack.Storage = new StorageSpec { Columns = 6 + (n - 1) / 2, Rows = 6 + n / 2, CarryBonus = 1 + n * 4, WeightLimit = 26 + n * 4, Split = n == 4 ? 3 : 0 };
                if (n < 6)
                {
                    var rig = Gear("rig_" + n, "甲板弹挂 · " + n + " 级", ItemKind.Rig, n, 2, 2, .5f + .17f * n, 30 + n * 32);
                    rig.Storage = new StorageSpec { Columns = 2 + n, Rows = n > 1 ? 2 : 1, CarryBonus = n * 1.5f, WeightLimit = 2 + n * 2, Split = n > 2 ? 2 : 0 };
                }
            }
            var scout = Gear("helmet_scout", "轻巡头盔 · 3 级", ItemKind.Helmet, 3, 2, 2, .88f, 140);
            scout.Coverage = BodyCoverage.Head; scout.Durability = 52; scout.DamageReduction = .39f;
            var watch = Gear("rig_watch", "值守弹挂 · 4 级", ItemKind.Rig, 4, 2, 2, 1.4f, 170);
            watch.Storage = new StorageSpec { Columns = 4, Rows = 3, CarryBonus = 4f, WeightLimit = 11f };
            var patrol = Gear("pack_patrol", "巡线背包 · 4 级", ItemKind.Pack, 4, 2, 3, 1.4f, 172);
            patrol.Storage = new StorageSpec { Columns = 5, Rows = 7, CarryBonus = 15, WeightLimit = 34f };
            var metro = Gear("vest_flex", "轻纤防弹衣 · 4 级", ItemKind.Armor, 4, 2, 3, 3.2f, 290);
            metro.Coverage = BodyCoverage.Chest; metro.Durability = 88; metro.DamageReduction = .60f;

            Med("medkit", "战地医疗包", MedicineForm.Pouch, TreatmentKind.Heal, 3, 45, 2.15f);
            Med("bandage", "急救绷带", MedicineForm.Roll, TreatmentKind.LightBleed, 1, 0, 1.15f);
            Med("trauma_case", "创伤急救盒", MedicineForm.Case, TreatmentKind.Heal, 4, 70, 3.05f);
            Med("tourniquet", "止血带", MedicineForm.Tourniquet, TreatmentKind.HeavyBleed, 1, 0, 1.9f);
            Med("splint", "夹板", MedicineForm.Splint, TreatmentKind.Fracture, 2, 0, 2.45f);
            Med("pain_tabs", "止痛药", MedicineForm.Tablets, TreatmentKind.Pain, 3, 45, 1.4f);
            Med("surgery", "手术包", MedicineForm.Surgery, TreatmentKind.Surgery, 2, 0, 5.8f);
            catalog.RebuildIndex();

            ItemDefinition Need(string id, string title, ItemKind kind, int w, int h, int stack, float weight, int value, Color tint)
            {
                var item = catalog.Items.Find(candidate => candidate != null && candidate.Id == id);
                if (item != null) return item;
                item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/" + id + ".asset")
                    ?? Item(id, title, kind, w, h, stack, weight, value, tint);
                catalog.Items.Add(item);
                EditorUtility.SetDirty(catalog);
                return item;
            }

            void Ammo(string id, string title, AmmoFamily family, int value, int stack)
            {
                var item = Need(id, title, ItemKind.Ammo, 1, 1, stack, .013f, value, new Color(.62f,.55f,.33f));
                item.AmmoFamily = family;
                EnsureLootEntry(ordinary, id, 9, 23, 2);
                EditorUtility.SetDirty(item);
            }

            void Gun(string id, string title, WeaponForm form, string ammo, AmmoFamily family, string feed,
                int w, int h, int capacity, float damage, float interval, float range, float reload, float recoil,
                float move, int rays, bool auto, int price, Color tint)
            {
                var item = Need(id, title, ItemKind.Weapon, w, h, 1, 1.0f + w * .66f, price, tint);
                var def = item.Weapon ?? AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/" + id + "_weapon.asset");
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<WeaponDefinition>();
                    AssetDatabase.CreateAsset(def, Root + "/" + id + "_weapon.asset");
                }
                def.Form = form; def.AmmoId = ammo; def.AmmoFamily = family; def.FrameInterface = feed;
                def.MagazineSize = capacity; def.Damage = damage; def.FireInterval = interval; def.Range = range;
                def.ReloadSeconds = reload; def.Recoil = recoil; def.MoveFactor = move; def.Rays = rays;
                def.Automatic = auto; def.Melee = false;
                item.Weapon = def;
                EnsureLootEntry(ordinary, id, 1, 1, 1);
                EditorUtility.SetDirty(def); EditorUtility.SetDirty(item);
            }

            void Part(string id, string title, PartSlot slot, string fitting, int form, int capacity,
                float stability, float reload, float aim, string dedicated = "")
            {
                var item = Need(id, title, ItemKind.Part, slot == PartSlot.Stock ? 2 : 1, 1, 1, .19f, 61 + form * 28, new Color(.35f,.42f,.42f));
                item.Part = new PartSpec { Slot = slot, Interface = fitting, DedicatedItem = dedicated, Form = form,
                    Capacity = capacity, Stability = stability, ReloadFactor = reload, AimFactor = aim };
                EnsureLootEntry(ordinary, id, 1, 1, 1);
                EnsureLootEntry(high, id, 1, 1, 1);
                EditorUtility.SetDirty(item);
            }

            ItemDefinition Gear(string id, string title, ItemKind kind, int tier, int w, int h, float weight, int cost)
            {
                var item = Need(id, title, kind, w, h, 1, weight, cost,
                    new Color(.22f + .035f * (tier % 3), .32f + .025f * (tier % 4), .27f + .031f * (tier % 2)));
                item.Tier = tier;
                if (tier <= 3) EnsureLootEntry(ordinary, id, 1, 1, 1);
                else EnsureLootEntry(high, id, 1, 1, 1);
                EditorUtility.SetDirty(item);
                return item;
            }

            void Med(string id, string title, MedicineForm form, TreatmentKind type, int uses, float strength, float duration)
            {
                var item = Need(id, title, ItemKind.Medicine, 1, form == MedicineForm.Surgery ? 2 : 1, 2,
                    .17f, 26 + uses * 8, new Color(.49f,.43f,.28f));
                item.MedicineVisual = form; item.Treatment = type; item.Uses = uses;
                item.HealAmount = strength; item.UseSeconds = duration;
                EnsureLootEntry(ordinary, id, 1, 1, 2);
                EditorUtility.SetDirty(item);
            }
        }
    }
}
