using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Dustline
{
    public sealed class ProfileTests
    {
        [Test]
        public void MissingProfileReturnsNullWithoutCreatingDirectories()
        {
            string directory = TemporaryDirectory();
            try
            {
                var store = new ProfileStore(System.IO.Path.Combine(directory, "nested", "profile.json"));
                Assert.That(store.Load(), Is.Null);
                Assert.That(Directory.Exists(directory), Is.False);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SaveAndLoadRoundTripAndReplaceExistingProfile()
        {
            string directory = TemporaryDirectory();
            try
            {
                string path = System.IO.Path.Combine(directory, "profile.json");
                var store = new ProfileStore(path);
                var profile = new ProfileData
                {
                    Credits = 2468,
                    RaidInProgress = true,
                    Raids = 3,
                    Extractions = 2,
                    BoundBladeId = BladeCatalog.ShortBladeId
                };
                profile.Stash.Add(new ItemStack
                {
                    InstanceId = "stash-instance",
                    ItemId = "物资-\"Stash\":[]\\",
                    Quantity = 4,
                    X = 2,
                    Y = 3,
                    Rotated = true
                });
                profile.Loadout.Add(new ItemStack
                {
                    InstanceId = "weapon-instance",
                    ItemId = "rifle",
                    Slot = EquipmentSlot.Weapon,
                    LoadedAmmo = 7,
                    Mods = new WeaponMods { Sight = 1, OwnedSight = 2, Grip = 2, OwnedGrip = 2, OwnedStock = 1 }
                });

                store.Save(profile);
                Assert.That(store.Path, Is.EqualTo(System.IO.Path.GetFullPath(path)));
                ProfileData loaded = store.Load();
                Assert.That(loaded.Version, Is.EqualTo(1));
                Assert.That(loaded.Credits, Is.EqualTo(2468));
                Assert.That(loaded.RaidInProgress, Is.True);
                Assert.That(loaded.Raids, Is.EqualTo(3));
                Assert.That(loaded.Extractions, Is.EqualTo(2));
                Assert.That(loaded.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
                Assert.That(loaded.Stash.Count, Is.EqualTo(1));
                Assert.That(loaded.Stash[0].InstanceId, Is.EqualTo("stash-instance"));
                Assert.That(loaded.Stash[0].ItemId, Is.EqualTo(profile.Stash[0].ItemId));
                Assert.That(loaded.Stash[0].Quantity, Is.EqualTo(4));
                Assert.That(loaded.Stash[0].X, Is.EqualTo(2));
                Assert.That(loaded.Stash[0].Y, Is.EqualTo(3));
                Assert.That(loaded.Stash[0].Rotated, Is.True);
                Assert.That(loaded.Stash[0].LoadedAmmo, Is.EqualTo(-1));
                Assert.That(loaded.Loadout[0].InstanceId, Is.EqualTo("weapon-instance"));
                Assert.That(loaded.Loadout[0].Slot, Is.EqualTo(EquipmentSlot.Weapon));
                Assert.That(loaded.Loadout[0].LoadedAmmo, Is.EqualTo(7));
                Assert.That(loaded.Loadout[0].Mods, Is.EqualTo(profile.Loadout[0].Mods));
                Assert.That(loaded.Stash[0], Is.Not.SameAs(profile.Stash[0]));

                loaded.Credits = 900;
                loaded.RaidInProgress = false;
                loaded.Loadout[0].LoadedAmmo = 0;
                store.Save(loaded);
                ProfileData replaced = store.Load();
                Assert.That(replaced.Credits, Is.EqualTo(900));
                Assert.That(replaced.RaidInProgress, Is.False);
                Assert.That(replaced.Loadout[0].LoadedAmmo, Is.Zero);
                Assert.That(replaced.Loadout[0].Mods, Is.EqualTo(profile.Loadout[0].Mods));
                Assert.That(replaced.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private const string V1Rifle = "{\"InstanceId\":\"v1-rifle\",\"ItemId\":\"rifle\",\"Quantity\":1,\"X\":0,\"Y\":0,\"Rotated\":false,\"Slot\":1,\"LoadedAmmo\":8";
        private const string V1Profile = "{\"Version\":1,\"Credits\":690,\"Stash\":[],\"Loadout\":[" + V1Rifle + "}]}";

        [Test]
        public void FirstLegacyWriteBackKeepsAnExactRecoverableBackup()
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                File.WriteAllText(path, V1Profile + "\r\n", new System.Text.UTF8Encoding(true));
                byte[] original = File.ReadAllBytes(path);
                var store = new ProfileStore(path);
                var profile = store.Load();
                Assert.That(profile.BoundBladeId, Is.Empty);
                Assert.That(profile.Loadout[0].Mods, Is.EqualTo(default(WeaponMods)));
                Assert.That(profile.Loadout[0].InstanceId, Is.EqualTo("v1-rifle"));
                Assert.That(profile.Loadout[0].LoadedAmmo, Is.EqualTo(8));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));

                profile.BoundBladeId = BladeCatalog.ShortBladeId;
                store.Save(profile);
                Assert.That(File.ReadAllBytes(store.LegacyBackupPath), Is.EqualTo(original));
                Assert.That(store.Load().BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
                profile.Loadout.Clear(); // Loss of the raid backpack does not affect the blade choice.
                profile.Raids++;
                new ProfileStore(path).Save(profile);
                Assert.That(store.Load().Loadout, Is.Empty);
                Assert.That(store.Load().BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
                Assert.That(File.ReadAllBytes(store.LegacyBackupPath), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path, store.LegacyBackupPath }));

                var recovered = new ProfileStore(store.LegacyBackupPath).Load();
                Assert.That(recovered.Version, Is.EqualTo(1));
                Assert.That(recovered.Loadout[0].InstanceId, Is.EqualTo("v1-rifle"));
                Assert.That(recovered.Loadout[0].LoadedAmmo, Is.EqualTo(8));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SchemaTwoBacksUpTheExactPreviousFileWithoutOverwritingLegacyBackup()
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                var store = new ProfileStore(System.IO.Path.Combine(directory, "profile.json"));
                string legacy = V1Profile.Replace("690", "550");
                File.WriteAllText(store.LegacyBackupPath, legacy);
                var mods = new WeaponMods { Sight = 1, OwnedSight = 2, Grip = 2, OwnedGrip = 2, Stock = 1, OwnedStock = 2 };
                var profile = new ProfileData { Credits = 700 };
                profile.Loadout.Add(new ItemStack { ItemId = "rifle", InstanceId = "retained-rifle", Slot = EquipmentSlot.Weapon, LoadedAmmo = 8, Mods = mods });
                store.Save(profile);
                byte[] before = File.ReadAllBytes(store.Path);
                var upgraded = store.Load();
                upgraded.Version = 2;
                upgraded.Loadout[0].LoadedAmmoId = "ammo";
                store.Save(upgraded);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(store.UpgradeBackupPath));
                Assert.That(File.ReadAllText(store.LegacyBackupPath), Is.EqualTo(legacy));
                var recovered = store.Load();
                Assert.That(recovered.Version, Is.EqualTo(2));
                Assert.That(recovered.Loadout[0].InstanceId, Is.EqualTo("retained-rifle"));
                Assert.That(recovered.Loadout[0].LoadedAmmo, Is.EqualTo(8));
                Assert.That(recovered.Loadout[0].LoadedAmmoId, Is.EqualTo("ammo"));
                Assert.That(recovered.Loadout[0].Mods, Is.EqualTo(mods));
                Assert.That(WeaponTuning.TrySelect(recovered.Loadout[0].Mods, WeaponModSlot.Sight, 2, out _, out int cost), Is.True);
                Assert.That(cost, Is.Zero);
                Assert.Throws<InvalidDataException>(() => store.Save(profile));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(store.UpgradeBackupPath));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void SchemaTwoRoundTripsEquipmentCargoWearAndInstalledParts()
        {
            string directory = TemporaryDirectory();
            try
            {
                var store = new ProfileStore(System.IO.Path.Combine(directory, "profile.json"));
                var initial = new ProfileData { Version = 2 };
                var gun = new ItemStack { ItemId = "rifle", InstanceId = "gun", Slot = EquipmentSlot.Weapon,
                    LoadedAmmo = 28, LoadedAmmoId = "ammo_amber_range", Mods = new WeaponMods { Grip = 1, OwnedGrip = 2 } };
                gun.Parts.Magazine = new PartLink { ItemId = "part_mag_a", InstanceId = "extended" };
                initial.Loadout.Add(gun);
                initial.Loadout.Add(new ItemStack { ItemId = "helmet_4", InstanceId = "helmet", Slot = EquipmentSlot.Helmet, Wear = 23 });
                initial.Loadout.Add(new ItemStack { ItemId = "rig_3", InstanceId = "rig", Slot = EquipmentSlot.Rig });
                initial.Loadout.Add(new ItemStack { ItemId = "medkit", InstanceId = "kit", Area = CargoArea.Rig, X = 2, Y = 0, UsesSpent = 2, Rotated = true });
                store.Save(initial);
                var recovered = store.Load();
                Assert.That(recovered.Version, Is.EqualTo(2));
                Assert.That(recovered.Loadout[0].LoadedAmmo, Is.EqualTo(28));
                Assert.That(recovered.Loadout[0].LoadedAmmoId, Is.EqualTo("ammo_amber_range"));
                Assert.That(recovered.Loadout[0].Parts.Magazine.ItemId, Is.EqualTo("part_mag_a"));
                Assert.That(recovered.Loadout[0].Parts.Magazine.InstanceId, Is.EqualTo("extended"));
                Assert.That(recovered.Loadout[0].Mods.OwnedGrip, Is.EqualTo(2));
                Assert.That(recovered.Loadout[1].Wear, Is.EqualTo(23));
                Assert.That(recovered.Loadout[1].Slot, Is.EqualTo(EquipmentSlot.Helmet));
                Assert.That(recovered.Loadout[3].Area, Is.EqualTo(CargoArea.Rig));
                Assert.That(recovered.Loadout[3].X, Is.EqualTo(2));
                Assert.That(recovered.Loadout[3].Rotated, Is.True);
                Assert.That(recovered.Loadout[3].UsesSpent, Is.EqualTo(2));
                byte[] bytes = File.ReadAllBytes(store.Path);
                recovered.Stash.Add(new ItemStack { ItemId = "part_mag_a", InstanceId = "extended" });
                Assert.Throws<InvalidDataException>(() => store.Save(recovered));
                recovered.Stash.Clear();
                recovered.Loadout[1].Area = CargoArea.Rig;
                Assert.Throws<InvalidDataException>(() => store.Save(recovered));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.Path));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExistingLegacyBackupIsNeverOverwritten(bool matchesProfile)
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                var store = new ProfileStore(path);
                string oldBackup = matchesProfile ? V1Profile : V1Profile.Replace("690", "700");
                File.WriteAllText(path, V1Profile);
                File.WriteAllText(store.LegacyBackupPath, oldBackup);
                var profile = store.Load();
                profile.Credits = 750;
                if (matchesProfile)
                {
                    store.Save(profile);
                    Assert.That(store.Load().Credits, Is.EqualTo(750));
                }
                else
                {
                    Assert.Throws<IOException>(() => store.Save(profile));
                    Assert.That(File.ReadAllText(path), Is.EqualTo(V1Profile));
                }
                Assert.That(File.ReadAllText(store.LegacyBackupPath), Is.EqualTo(oldBackup));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path, store.LegacyBackupPath }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [TestCase("\"BoundBladeId\":123")]
        [TestCase("\"BoundBladeId\":null")]
        [TestCase("\"BoundBladeId\":[]")]
        [TestCase("\"BoundBladeId\":\" \"")]
        [TestCase("\"BoundBladeId\":\"tangdao\",\"\\u0042oundBladeId\":\"short_blade\"")]
        [TestCase("\"Credits\":10")]
        public void KnownRootFieldsRejectWrongTypesAndDuplicates(string fields)
        {
            string json = V1Profile.Insert(V1Profile.Length - 1, "," + fields);
            LoadRejectsInvalidJsonOrSchemaWithoutChangingFile(json);
        }

        [TestCase("\"Mods\":[]")]
        [TestCase("\"Mods\":null")]
        [TestCase("\"Mods\":{},\"Mods\":{}")]
        [TestCase("\"Mods\":{\"Sight\":0,\"\\u0053ight\":0}")]
        [TestCase("\"Mods\":{\"OwnedSight\":\"1\"}")]
        [TestCase("\"Mods\":{\"Grip\":1.0,\"OwnedGrip\":2}")]
        [TestCase("\"Mods\":{\"OwnedGrip\":true}")]
        [TestCase("\"Mods\":{\"Stock\":-1}")]
        [TestCase("\"Mods\":{\"OwnedStock\":3}")]
        [TestCase("\"Mods\":{\"Stock\":2,\"OwnedStock\":1}")]
        [TestCase("\"Mods\":{},\"LoadedAmmo\":9")]
        public void KnownItemFieldsRejectWrongTypesDuplicatesAndInvalidLevels(string fields)
        {
            string json = "{\"Version\":1,\"Credits\":690,\"Stash\":[],\"Loadout\":[" + V1Rifle + "," + fields + "}]}";
            LoadRejectsInvalidJsonOrSchemaWithoutChangingFile(json);
        }

        [TestCase("\"LoadedAmmo\":8", "\"LoadedAmmo\":\"8\"")]
        [TestCase("\"Rotated\":false", "\"Rotated\":0")]
        [TestCase("\"Slot\":1", "\"Slot\":1.0")]
        [TestCase("\"InstanceId\":\"v1-rifle\"", "\"InstanceId\":true")]
        public void LegacyItemFieldsRemainStrictlyTyped(string original, string replacement)
        {
            LoadRejectsInvalidJsonOrSchemaWithoutChangingFile(V1Profile.Replace(original, replacement));
        }

        [Test]
        public void AdditiveFieldsDoNotRequireOtherNewFieldsOrAFieldOrder()
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                var store = new ProfileStore(System.IO.Path.Combine(directory, "profile.json"));
                string item = V1Rifle.Insert(1, "\"Mods\":{\"OwnedStock\":2},");
                string json = "{\"Version\":1,\"Credits\":690,\"Stash\":[],\"Loadout\":[" + item + "}]}";
                File.WriteAllText(store.Path, json);
                var loaded = store.Load();
                Assert.That(loaded.BoundBladeId, Is.Empty);
                Assert.That(loaded.Loadout[0].Mods.Stock, Is.Zero);
                Assert.That(loaded.Loadout[0].Mods.OwnedStock, Is.EqualTo(2));
                Assert.That(loaded.Loadout[0].LoadedAmmo, Is.EqualTo(8));
                store.Save(loaded);
                Assert.That(File.ReadAllText(store.LegacyBackupPath), Is.EqualTo(json));
                Assert.That(store.Load().Loadout[0].Mods, Is.EqualTo(loaded.Loadout[0].Mods));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]")]
        [TestCase("{\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[]}")]
        [TestCase("{\"Version\":5,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":4,\"Credits\":1000,\"Stash\":[],\"Loadout\":[],\"BirthdayGiftClaimed\":1}")]
        [TestCase("{\"Version\":0,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":\"1\",\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":-1,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":\"1000\",\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":2147483648,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1.5,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":01,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":NaN,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":null,\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":{},\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":null}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[null],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[{}],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[],\"Raids\":-1}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[],\"Extractions\":1,\"Raids\":0}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[],\"RaidInProgress\":1}")]
        [TestCase("{\"Version\":1,\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Version\":1,\"\\u0056ersion\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}")]
        [TestCase("{\"Credits\":1000,\"Stash\":[],\"Loadout\":[],\"Extra\":{\"Version\":1}}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[],}")]
        [TestCase("{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":[]}garbage")]
        public void LoadRejectsInvalidJsonOrSchemaWithoutChangingFile(string json)
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                File.WriteAllText(path, json);
                var store = new ProfileStore(path);
                Assert.Throws<InvalidDataException>(() => store.Load());
                Assert.Throws<InvalidDataException>(() => store.Save(new ProfileData()));
                Assert.That(File.ReadAllText(path), Is.EqualTo(json));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [TestCase("null-stash")]
        [TestCase("null-loadout")]
        [TestCase("null-item")]
        [TestCase("empty-instance")]
        [TestCase("empty-item")]
        [TestCase("duplicate-instance")]
        [TestCase("zero-quantity")]
        [TestCase("negative-coordinate")]
        [TestCase("invalid-slot")]
        [TestCase("equipped-stack")]
        [TestCase("duplicate-slot")]
        [TestCase("invalid-loaded")]
        [TestCase("non-rifle-mods")]
        [TestCase("invalid-mod-level")]
        [TestCase("invalid-version")]
        [TestCase("negative-credits")]
        [TestCase("negative-raids")]
        [TestCase("negative-extractions")]
        [TestCase("extractions-exceed-raids")]
        public void InvalidProfileItemsAndCountersAreRejectedByLoadAndSave(string scenario)
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                var store = new ProfileStore(path);
                var profile = new ProfileData();
                profile.Stash.Add(new ItemStack { InstanceId = "item", ItemId = "ammo", Quantity = 3 });
                switch (scenario)
                {
                    case "null-stash": profile.Stash = null; break;
                    case "null-loadout": profile.Loadout = null; break;
                    case "null-item": profile.Stash[0] = null; break;
                    case "empty-instance": profile.Stash[0].InstanceId = ""; break;
                    case "empty-item": profile.Stash[0].ItemId = " "; break;
                    case "duplicate-instance": profile.Loadout.Add(profile.Stash[0].Clone()); break;
                    case "zero-quantity": profile.Stash[0].Quantity = 0; break;
                    case "negative-coordinate": profile.Stash[0].Y = -1; break;
                    case "invalid-slot": profile.Stash[0].Slot = (EquipmentSlot)20; break;
                    case "equipped-stack": profile.Stash[0].Slot = EquipmentSlot.Weapon; break;
                    case "duplicate-slot":
                        profile.Stash[0].Quantity = 1;
                        profile.Stash[0].Slot = EquipmentSlot.Weapon;
                        profile.Stash.Add(new ItemStack
                        {
                            InstanceId = "second", ItemId = "rifle", Slot = EquipmentSlot.Weapon
                        });
                        break;
                    case "invalid-loaded": profile.Stash[0].LoadedAmmo = -2; break;
                    case "non-rifle-mods": profile.Stash[0].Mods = new WeaponMods { OwnedGrip = 1 }; break;
                    case "invalid-mod-level":
                        profile.Stash[0].ItemId = WeaponDefinition.RifleId;
                        profile.Stash[0].Mods = new WeaponMods { Grip = 2, OwnedGrip = 1 };
                        break;
                    case "invalid-version": profile.Version = ProfileData.CurrentVersion + 1; break;
                    case "negative-credits": profile.Credits = -1; break;
                    case "negative-raids": profile.Raids = -1; break;
                    case "negative-extractions": profile.Extractions = -1; break;
                    case "extractions-exceed-raids": profile.Extractions = 1; break;
                }

                Assert.Throws<InvalidDataException>(() => store.Save(profile));
                Assert.That(File.Exists(path), Is.False);
                string json = JsonUtility.ToJson(profile);
                if (scenario == "null-stash")
                    json = "{\"Version\":1,\"Credits\":1000,\"Stash\":null,\"Loadout\":[]}";
                else if (scenario == "null-loadout")
                    json = "{\"Version\":1,\"Credits\":1000,\"Stash\":[],\"Loadout\":null}";
                File.WriteAllText(path, json);
                Assert.Throws<InvalidDataException>(() => store.Load());
                Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SaveRefusesToOverwriteCorruptExistingProfile()
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                const string corrupt = "{\"Version\":1,\"Credits\":";
                File.WriteAllText(path, corrupt);
                var store = new ProfileStore(path);
                Assert.Throws<InvalidDataException>(() => store.Load());
                Assert.Throws<InvalidDataException>(() => store.Save(new ProfileData()));
                Assert.That(File.ReadAllText(path), Is.EqualTo(corrupt));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void InvalidSaveKeepsThePreviousValidProfile()
        {
            string directory = TemporaryDirectory();
            try
            {
                string path = System.IO.Path.Combine(directory, "profile.json");
                var store = new ProfileStore(path);
                var profile = new ProfileData { Credits = 350 };
                store.Save(profile);
                byte[] original = File.ReadAllBytes(path);
                profile.Credits = -1;
                Assert.Throws<InvalidDataException>(() => store.Save(profile));
                Assert.Throws<InvalidDataException>(() => store.Save(null));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                Assert.That(store.Load().Credits, Is.EqualTo(350));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidEncodingIsReportedAsCorruptData(bool bom)
        {
            string directory = TemporaryDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "profile.json");
                string json = V1Profile.Insert(V1Profile.Length - 1, ",\"BoundBladeId\":\"tang?dao\"");
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(bom));
                byte[] bytes = File.ReadAllBytes(path);
                bytes[Array.IndexOf(bytes, (byte)'?')] = 0xff;
                File.WriteAllBytes(path, bytes);
                var store = new ProfileStore(path);
                Assert.Throws<InvalidDataException>(() => store.Load());
                Assert.Throws<InvalidDataException>(() => store.Save(new ProfileData()));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { path }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private static string TemporaryDirectory()
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dustline-profile-tests-" + Guid.NewGuid().ToString("N"));
        }
    }
}
