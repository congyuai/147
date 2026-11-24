using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dustline.Editor
{
    public static partial class ProjectSetup
    {
        static readonly int[] ArmorBaseline = { 5000, 10000, 50000, 250000, 500000, 2000000 };

        static void PrepareEconomy(ItemCatalog catalog, LootTable ordinary, LootTable high)
        {
            var cake = EnsureDefinition(LootTable.BirthdayCake, "生日蛋糕", ItemKind.Valuable, 2, 2, .9f, Color.white);
            cake.Special = true;
            cake.NotForSale = true;
            cake.FixedResale = LootTable.BirthdayResale;
            cake.Value = 0;
            EditorUtility.SetDirty(cake);

            foreach (var size in new[] { (2, 2), (2, 3), (3, 3), (3, 4) })
            {
                string dimension = size.Item1 + "x" + size.Item2;
                var secure = EnsureDefinition("secure_" + dimension, "安全箱 " + size.Item1 + " × " + size.Item2,
                    ItemKind.SecureCase, 2, 2, 1.1f, new Color(.28f, .39f, .42f));
                secure.NotForSale = true;
                secure.Storage = new StorageSpec { Columns = size.Item1, Rows = size.Item2, WeightLimit = 30 };
                EditorUtility.SetDirty(secure);
            }

            for (int tier = 1; tier <= 6; tier++)
            {
                var helmet = Variant("helmet_line_", "巡线头盔", ItemKind.Helmet, tier, 2, 2, .94f + tier * .14f);
                helmet.Coverage = BodyCoverage.Head;
                helmet.Durability = 35 + tier * 12;
                helmet.DamageReduction = .10f + .097f * tier;
                var vest = Variant("vest_shell_", "盾壳护甲", ItemKind.Armor, tier, 2, 3, 2.6f + tier * .72f);
                vest.Coverage = BodyCoverage.Chest | (tier >= 2 ? BodyCoverage.Abdomen : BodyCoverage.None);
                vest.Durability = 42 + tier * 24;
                vest.DamageReduction = .11f + .105f * tier;
                var pack = Variant("pack_freight_", "运载背包", ItemKind.Pack, tier, 2, 3, 1.3f + tier * .22f);
                pack.Storage = new StorageSpec { Columns = 5 + tier / 2, Rows = 5 + tier / 2, Split = tier % 2 == 0 ? 2 : 0,
                    CarryBonus = 4 + 4 * tier, WeightLimit = 25 + 4 * tier };
                if (tier <= 5)
                {
                    var rig = Variant("rig_split_", "分仓弹挂", ItemKind.Rig, tier, 2, 2, .7f + tier * .16f);
                    int columns = tier + 2;
                    var split = new List<CargoPocket> { new CargoPocket(0, 0, 1, 2) };
                    for (int row = 0; row < 2; row++)
                        for (int x = 1; x < columns; x += 2)
                            split.Add(new CargoPocket(x, row, Math.Min(2, columns - x), 1));
                    rig.Storage = new StorageSpec { Columns = columns, Rows = 2, CarryBonus = 1.5f * tier, WeightLimit = 3 + tier * 2.2f, Pockets = split.ToArray() };
                }
            }

            foreach (var item in catalog.Items)
            {
                if (item.Special || item.Kind == ItemKind.SecureCase) continue;
                if (item.Tier == 0)
                    item.Tier = item.Kind switch
                    {
                        ItemKind.Weapon => item.Weapon == null || item.Weapon.Melee ? 1 : item.Weapon.AlternateFrame ? 3 : 2,
                        ItemKind.Part => item.Part.Magnification >= 12 ? 6 : item.Part.Magnification >= 6 ? 5 : item.Part.Magnification >= 3 ? 4 : 2,
                        ItemKind.Medicine => item.Treatment == TreatmentKind.Surgery ? 3 : item.Uses >= 3 ? 2 : 1,
                        ItemKind.Valuable => item.Id == "relic" ? 4 : 2,
                        _ => 1
                    };
                int baseline = ArmorBaseline[Math.Max(0, Math.Min(item.Tier - 1, 5))];
                item.Value = item.Kind switch
                {
                    ItemKind.Armor => item.Id == "vest_" + item.Tier ? baseline : (int)(baseline * (item.Id == "armor" ? .82 : 1.08)),
                    ItemKind.Helmet => (int)(baseline * (.43 + .0016 * item.Durability)),
                    ItemKind.Pack => (int)(baseline * (.23 + item.Storage.Capacity / 235d)),
                    ItemKind.Rig => (int)(baseline * (.26 + item.Storage.Capacity / 100d)),
                    ItemKind.Weapon => baseline + (int)(900 * item.UnitWeight) + 200 * (item.Weapon?.MagazineSize ?? 0),
                    ItemKind.Ammo => Math.Max(18, baseline / 750) + 5 * item.Tier,
                    ItemKind.Part => (int)(baseline * (.17 + .017 * item.Part.Magnification)) + 14 * item.Part.Capacity,
                    ItemKind.Medicine => baseline / 14 + 85 * item.Uses,
                    ItemKind.Valuable => baseline / (item.Id == "relic" ? 3 : 5),
                    _ => item.Value
                };
                EditorUtility.SetDirty(item);
            }
            catalog.RebuildIndex();

            ItemDefinition Variant(string prefix, string name, ItemKind kind, int tier, int w, int h, float weight)
            {
                var item = EnsureDefinition(prefix + tier, name + " · " + tier + " 级", kind, w, h, weight,
                    new Color(.43f + .025f * (tier % 3), .35f, .25f + .026f * (tier % 2)));
                item.Tier = tier;
                EnsureLootEntry(tier <= 3 ? ordinary : high, item.Id, 1, 1, 1);
                return item;
            }

            ItemDefinition EnsureDefinition(string id, string title, ItemKind kind, int width, int height, float weight, Color tint)
            {
                var item = catalog.Items.Find(def => def.Id == id);
                if (item != null) return item;
                item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/" + id + ".asset")
                    ?? Item(id, title, kind, width, height, 1, weight, 0, tint);
                catalog.Items.Add(item);
                EditorUtility.SetDirty(catalog);
                return item;
            }
        }

        [Serializable]
        sealed class MarketCatalog { public MarketRow[] Items; }

        [Serializable]
        sealed class MarketRow
        {
            public string Id;
            public int Width, Height, MaxStack, Value, Resale, StarterQuantity;
            public bool ForSale;
        }

        [MenuItem("Dustline/Export Market Catalog")]
        public static void ExportMarketCatalog()
        {
            Prepare();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "/Items.asset");
            var rows = new List<MarketRow>();
            foreach (var item in catalog.Items)
            {
                if (item.Kind == ItemKind.SecureCase) continue;
                rows.Add(new MarketRow { Id = item.Id, Width = item.Width, Height = item.Height, MaxStack = item.MaxStack,
                    Value = item.Value, Resale = item.ResaleValue, ForSale = !item.NotForSale,
                    StarterQuantity = item.Id switch { LootTable.BirthdayCake or "rifle" or "armor" or "medkit" => 1, "ammo" => 30, _ => 0 } });
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            const string path = "Builds/Market/catalog.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(new MarketCatalog { Items = rows.ToArray() }, true));
            Debug.Log("Dustline market catalog: " + path);
        }
    }
}
