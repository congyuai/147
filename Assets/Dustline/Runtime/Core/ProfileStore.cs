using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Dustline
{
    [Serializable]
    public sealed class ProfileData
    {
        public const int CurrentVersion = 3;
        public int Version = 1;
        public int Credits = 1000;
        public List<ItemStack> Stash = new List<ItemStack>();
        public List<ItemStack> Loadout = new List<ItemStack>();
        public bool RaidInProgress;
        public int Raids;
        public int Extractions;
        public string BoundBladeId = "";
    }

    public sealed class ProfileStore
    {
        private static readonly UTF8Encoding Encoding = new UTF8Encoding(true, true);

        public ProfileStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A profile path is required.", nameof(path));

            Path = System.IO.Path.GetFullPath(path);
        }

        public string Path { get; }
        public string LegacyBackupPath => Path + ".v1.bak";
        public string UpgradeBackupPath => Path + ".pre-v2.bak";
        public string LoadoutUpgradeBackupPath => Path + ".pre-v3.bak";

        public ProfileData Load() => ReadProfile(out _, out _);

        private ProfileData ReadProfile(out byte[] bytes, out bool requiresUpgrade)
        {
            bytes = null;
            requiresUpgrade = false;
            try
            {
                bytes = File.ReadAllBytes(Path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }

            string json;
            try
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), Encoding))
                    json = reader.ReadToEnd();
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Profile is not valid UTF-8.", exception);
            }

            var validator = new ProfileJsonValidator(json);
            validator.Validate();
            ProfileData profile;
            try
            {
                profile = JsonUtility.FromJson<ProfileData>(json);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Profile JSON cannot be deserialized.", exception);
            }

            if (profile != null && profile.BoundBladeId == null)
                profile.BoundBladeId = "";
            ValidateProfile(profile);
            requiresUpgrade = validator.RequiresUpgrade;
            return profile;
        }

        public void Save(ProfileData profile)
        {
            ValidateProfile(profile);
            string json = JsonUtility.ToJson(profile, true);
            new ProfileJsonValidator(json).Validate();
            Load();

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string temporary = TemporaryPath(Path);
            try
            {
                WriteNewFile(temporary, Encoding.GetBytes(json));

                // Never fall back to deleting the only durable save before the rename.
                var previous = ReadProfile(out byte[] original, out bool requiresUpgrade);
                if (previous != null)
                {
                    if (profile.Version < previous.Version) throw new InvalidDataException("Cannot downgrade an existing profile.");
                    if (previous.Version < ProfileData.CurrentVersion && profile.Version == ProfileData.CurrentVersion)
                        BackupOriginal(LoadoutUpgradeBackupPath, original);
                    else if (previous.Version == 1 && profile.Version == 2)
                        BackupOriginal(UpgradeBackupPath, original);
                    else if (requiresUpgrade)
                        BackupOriginal(LegacyBackupPath, original);
                    File.Replace(temporary, Path, null);
                }
                else
                {
                    File.Move(temporary, Path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private void BackupOriginal(string backup, byte[] original)
        {
            if (File.Exists(backup))
            {
                if (!original.SequenceEqual(File.ReadAllBytes(backup)))
                    throw new IOException("The legacy backup already exists and does not match the profile being upgraded.");
                return;
            }

            string temporary = TemporaryPath(backup);
            try
            {
                WriteNewFile(temporary, original);
                File.Move(temporary, backup);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string TemporaryPath(string path) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),
            "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        private static void WriteNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void ValidateProfile(ProfileData profile)
        {
            if (profile == null || profile.Version < 1 || profile.Version > ProfileData.CurrentVersion || profile.Credits < 0
                || profile.Raids < 0 || profile.Extractions < 0 || profile.Extractions > profile.Raids
                || profile.Stash == null || profile.Loadout == null)
                throw new InvalidDataException("Invalid profile version, balances, counters or item lists.");
            if (!IsValidBladeId(profile.BoundBladeId))
                throw new InvalidDataException("Invalid bound blade ID.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateItems(profile.Stash, ids, profile.Version);
            ValidateItems(profile.Loadout, ids, profile.Version);
            if (profile.Version >= 3 && profile.RaidInProgress
                && profile.Loadout.Exists(item => item.Area != CargoArea.Legacy || item.Slot != EquipmentSlot.None))
                throw new InvalidDataException("An in-progress raid can only retain migration items at home.");
        }

        private static bool IsValidBladeId(string id) => id == "" || !string.IsNullOrWhiteSpace(id);

        private static void ValidateItems(List<ItemStack> items, HashSet<string> ids, int version)
        {
            var slots = new HashSet<EquipmentSlot>();
            foreach (ItemStack item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.InstanceId)
                    || string.IsNullOrWhiteSpace(item.ItemId) || !ids.Add(item.InstanceId)
                    || item.Quantity < 1 || item.X < 0 || item.Y < 0 || item.LoadedAmmo < -1
                    || !Enum.IsDefined(typeof(EquipmentSlot), item.Slot))
                    throw new InvalidDataException("Invalid profile item or duplicate instance ID.");
                if (!Enum.IsDefined(typeof(CargoArea), item.Area) || item.UsesSpent < 0 || item.Wear < 0
                    || version < 3 && ((int)item.Slot > (int)EquipmentSlot.Pack || (int)item.Area > (int)CargoArea.Rig)
                    || version == 1 && ((int)item.Slot > (int)EquipmentSlot.Armor || item.Area != CargoArea.Main || !item.Parts.Empty
                        || !string.IsNullOrEmpty(item.LoadedAmmoId) || item.UsesSpent != 0 || item.Wear != 0))
                    throw new InvalidDataException("Item data does not match the profile version.");
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                {
                    var part = item.Parts.Get((PartSlot)i);
                    if (!part.Empty && (string.IsNullOrWhiteSpace(part.ItemId) || string.IsNullOrWhiteSpace(part.InstanceId) || !ids.Add(part.InstanceId)))
                        throw new InvalidDataException("Invalid or duplicate installed part instance.");
                }
                if (!WeaponTuning.IsValidForItem(item.ItemId, item.Mods))
                    throw new InvalidDataException("Invalid weapon upgrade levels.");
                if (item.Slot != EquipmentSlot.None && (item.Quantity != 1 || !slots.Add(item.Slot)
                    || version >= 2 && (item.Area != CargoArea.Main || item.X != 0 || item.Y != 0)))
                    throw new InvalidDataException("Invalid or duplicate equipped item.");
            }
        }

        private sealed class ProfileJsonValidator
        {
            private readonly string json;
            private int position;
            private bool hasItemWithoutMods;
            private int version;

            public bool RequiresUpgrade { get; private set; }

            public ProfileJsonValidator(string json)
            {
                this.json = json;
            }

            public void Validate()
            {
                Require('{');
                var fields = new HashSet<string>(StringComparer.Ordinal);
                if (!Take('}'))
                {
                    do
                    {
                        string field = ReadString();
                        if (!fields.Add(field))
                            Fail("Duplicate profile field.");
                        Require(':');
                        switch (field)
                        {
                            case "Version":
                                version = ReadInteger();
                                if (version < 1 || version > ProfileData.CurrentVersion) Fail("Unsupported profile version.");
                                break;
                            case "Credits":
                            case "Raids":
                            case "Extractions":
                                if (ReadInteger() < 0)
                                    Fail("Negative profile balance or counter.");
                                break;
                            case "RaidInProgress":
                                ReadBoolean();
                                break;
                            case "BoundBladeId":
                                if (!IsValidBladeId(ReadString()))
                                    Fail("Invalid bound blade ID.");
                                break;
                            case "Stash":
                            case "Loadout":
                                ReadItems();
                                break;
                            default:
                                SkipValue(0);
                                break;
                        }
                    } while (Take(','));
                    Require('}');
                }

                if (!fields.Contains("Version") || !fields.Contains("Credits")
                    || !fields.Contains("Stash") || !fields.Contains("Loadout"))
                    Fail("Required profile fields are missing.");
                SkipWhitespace();
                if (position != json.Length)
                    Fail("Unexpected data after the profile.");
                RequiresUpgrade = version == 1 && (!fields.Contains("BoundBladeId") || hasItemWithoutMods);
            }

            private void ReadItems()
            {
                Require('[');
                if (Take(']'))
                    return;

                do
                {
                    Require('{');
                    var fields = new HashSet<string>(StringComparer.Ordinal);
                    string itemId = null;
                    WeaponMods mods = default;
                    if (!Take('}'))
                    {
                        do
                        {
                            string field = ReadString();
                            if (!fields.Add(field))
                                Fail("Duplicate item field.");
                            Require(':');
                            switch (field)
                            {
                                case "InstanceId":
                                    if (string.IsNullOrWhiteSpace(ReadString()))
                                        Fail("Empty item ID.");
                                    break;
                                case "ItemId":
                                    itemId = ReadString();
                                    if (string.IsNullOrWhiteSpace(itemId))
                                        Fail("Empty item ID.");
                                    break;
                                case "Mods":
                                    mods = ReadMods();
                                    break;
                                case "Quantity":
                                    if (ReadInteger() < 1)
                                        Fail("Invalid item quantity.");
                                    break;
                                case "Parts":
                                    ReadParts();
                                    break;
                                case "LoadedAmmoId":
                                    ReadOptionalString();
                                    break;
                                case "Area":
                                    int area = ReadInteger();
                                    if (area < 0 || area > (int)CargoArea.Legacy) Fail("Invalid cargo area.");
                                    break;
                                case "UsesSpent":
                                case "Wear":
                                case "X":
                                case "Y":
                                    if (ReadInteger() < 0)
                                        Fail("Invalid item coordinates.");
                                    break;
                                case "LoadedAmmo":
                                    if (ReadInteger() < -1)
                                        Fail("Invalid magazine ammunition count.");
                                    break;
                                case "Slot":
                                    int slot = ReadInteger();
                                    if (slot < (int)EquipmentSlot.None || slot > (int)EquipmentSlot.Melee)
                                        Fail("Invalid equipment slot.");
                                    break;
                                case "Rotated":
                                    ReadBoolean();
                                    break;
                                default:
                                    SkipValue(0);
                                    break;
                            }
                        } while (Take(','));
                        Require('}');
                    }

                    if (!fields.Contains("InstanceId") || !fields.Contains("ItemId")
                        || !fields.Contains("Quantity") || !fields.Contains("X") || !fields.Contains("Y")
                        || !fields.Contains("Rotated") || !fields.Contains("Slot") || !fields.Contains("LoadedAmmo"))
                        Fail("Required item fields are missing.");
                    if (!WeaponTuning.IsValidForItem(itemId, mods))
                        Fail("Invalid weapon upgrade levels.");
                    hasItemWithoutMods |= !fields.Contains("Mods");
                } while (Take(','));
                Require(']');
            }

            void ReadParts()
            {
                Require('{');
                if (Take('}')) return;
                var slots = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    string name = ReadString();
                    if (!(name == "Sight" || name == "Grip" || name == "Stock" || name == "Muzzle" || name == "Magazine") || !slots.Add(name))
                        Fail("Invalid or duplicate part slot.");
                    Require(':');
                    Require('{');
                    if (Take('}')) continue;
                    var fields = new HashSet<string>(StringComparer.Ordinal);
                    do
                    {
                        string field = ReadString();
                        if ((field != "ItemId" && field != "InstanceId") || !fields.Add(field)) Fail("Invalid part link.");
                        Require(':');
                        ReadOptionalString();
                    } while (Take(','));
                    Require('}');
                } while (Take(','));
                Require('}');
            }

            void ReadOptionalString()
            {
                SkipWhitespace();
                if (position < json.Length && json[position] == 'n') ReadLiteral("null");
                else ReadString();
            }

            private WeaponMods ReadMods()
            {
                Require('{');
                var mods = new WeaponMods();
                if (Take('}')) return mods;

                var fields = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    string field = ReadString();
                    if (!fields.Add(field))
                        Fail("Duplicate weapon mod field.");
                    Require(':');
                    switch (field)
                    {
                        case "Sight": mods.Sight = ReadInteger(); break;
                        case "Grip": mods.Grip = ReadInteger(); break;
                        case "Stock": mods.Stock = ReadInteger(); break;
                        case "OwnedSight": mods.OwnedSight = ReadInteger(); break;
                        case "OwnedGrip": mods.OwnedGrip = ReadInteger(); break;
                        case "OwnedStock": mods.OwnedStock = ReadInteger(); break;
                        default: SkipValue(0); break;
                    }
                } while (Take(','));
                Require('}');
                return mods;
            }

            private int ReadInteger()
            {
                string token = ReadNumber();
                if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                    Fail("Expected a 32-bit integer.");
                return value;
            }

            private string ReadNumber()
            {
                SkipWhitespace();
                int start = position;
                if (position < json.Length && json[position] == '-')
                    position++;
                if (position >= json.Length)
                    Fail("Expected a number.");

                if (json[position] == '0')
                {
                    position++;
                }
                else
                {
                    if (json[position] < '1' || json[position] > '9')
                        Fail("Expected a number.");
                    while (position < json.Length && IsDigit(json[position]))
                        position++;
                }
                if (position < json.Length && json[position] == '.')
                {
                    position++;
                    ReadDigits();
                }
                if (position < json.Length && (json[position] == 'e' || json[position] == 'E'))
                {
                    position++;
                    if (position < json.Length && (json[position] == '+' || json[position] == '-'))
                        position++;
                    ReadDigits();
                }
                return json.Substring(start, position - start);
            }

            private void ReadDigits()
            {
                int start = position;
                while (position < json.Length && IsDigit(json[position]))
                    position++;
                if (position == start)
                    Fail("Expected decimal digits.");
            }

            private void ReadBoolean()
            {
                SkipWhitespace();
                ReadLiteral(position < json.Length && json[position] == 't' ? "true" : "false");
            }

            private string ReadString()
            {
                Require('"');
                var value = new StringBuilder();
                while (position < json.Length)
                {
                    char character = json[position++];
                    if (character == '"')
                        return value.ToString();
                    if (character < ' ')
                        Fail("Control character in a JSON string.");
                    if (character != '\\')
                    {
                        value.Append(character);
                        continue;
                    }
                    if (position == json.Length)
                        Fail("Unterminated JSON escape.");

                    switch (json[position++])
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (position == json.Length)
                                    Fail("Incomplete Unicode escape.");
                                char digit = json[position++];
                                int hex = digit >= '0' && digit <= '9' ? digit - '0'
                                    : digit >= 'a' && digit <= 'f' ? digit - 'a' + 10
                                    : digit >= 'A' && digit <= 'F' ? digit - 'A' + 10 : -1;
                                if (hex < 0)
                                    Fail("Invalid Unicode escape.");
                                code = code * 16 + hex;
                            }
                            value.Append((char)code);
                            break;
                        default:
                            Fail("Invalid JSON escape.");
                            break;
                    }
                }
                Fail("Unterminated JSON string.");
                return null;
            }

            private void SkipValue(int depth)
            {
                if (depth > 64)
                    Fail("JSON nesting is too deep.");
                SkipWhitespace();
                if (position == json.Length)
                    Fail("Expected a JSON value.");

                switch (json[position])
                {
                    case '{':
                        Require('{');
                        if (!Take('}'))
                        {
                            var fields = new HashSet<string>(StringComparer.Ordinal);
                            do
                            {
                                if (!fields.Add(ReadString()))
                                    Fail("Duplicate JSON field.");
                                Require(':');
                                SkipValue(depth + 1);
                            } while (Take(','));
                            Require('}');
                        }
                        break;
                    case '[':
                        Require('[');
                        if (!Take(']'))
                        {
                            do
                            {
                                SkipValue(depth + 1);
                            } while (Take(','));
                            Require(']');
                        }
                        break;
                    case '"': ReadString(); break;
                    case 't': ReadLiteral("true"); break;
                    case 'f': ReadLiteral("false"); break;
                    case 'n': ReadLiteral("null"); break;
                    default: ReadNumber(); break;
                }
            }

            private void ReadLiteral(string literal)
            {
                SkipWhitespace();
                for (int i = 0; i < literal.Length; i++)
                {
                    if (position == json.Length || json[position++] != literal[i])
                        Fail("Invalid JSON literal.");
                }
            }

            private bool Take(char character)
            {
                SkipWhitespace();
                if (position == json.Length || json[position] != character)
                    return false;
                position++;
                return true;
            }

            private void Require(char character)
            {
                if (!Take(character))
                    Fail("Expected '" + character + "'.");
            }

            private void SkipWhitespace()
            {
                while (position < json.Length)
                {
                    char character = json[position];
                    if (character != ' ' && character != '\t' && character != '\r' && character != '\n')
                        break;
                    position++;
                }
            }

            private static bool IsDigit(char character)
            {
                return character >= '0' && character <= '9';
            }

            private void Fail(string message)
            {
                throw new InvalidDataException(message + " At character " + position + ".");
            }
        }
    }
}
