using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dustline
{
    [Serializable]
    public sealed class ProfileData
    {
        public int Version = 1;
        public int Credits = 1000;
        public List<ItemStack> Stash = new List<ItemStack>();
        public List<ItemStack> Loadout = new List<ItemStack>();
        public bool RaidInProgress;
        public int Raids;
        public int Extractions;
    }

    public sealed class ProfileStore
    {
        private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);

        public ProfileStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A profile path is required.", nameof(path));

            Path = System.IO.Path.GetFullPath(path);
        }

        public string Path { get; }

        public ProfileData Load()
        {
            string json;
            try
            {
                json = File.ReadAllText(Path, Encoding);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Profile is not valid UTF-8.", exception);
            }

            new ProfileJsonValidator(json).Validate();
            ProfileData profile;
            try
            {
                profile = JsonUtility.FromJson<ProfileData>(json);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Profile JSON cannot be deserialized.", exception);
            }

            ValidateProfile(profile);
            return profile;
        }

        public void Save(ProfileData profile)
        {
            ValidateProfile(profile);
            string json = JsonUtility.ToJson(profile, true);
            new ProfileJsonValidator(json).Validate();
            Load();

            string directory = System.IO.Path.GetDirectoryName(Path);
            Directory.CreateDirectory(directory);
            string temporary = System.IO.Path.Combine(directory,
                "." + System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = Encoding.GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                // Never fall back to deleting the only durable save before the rename.
                if (File.Exists(Path))
                {
                    Load();
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

        private static void ValidateProfile(ProfileData profile)
        {
            if (profile == null || profile.Version != 1 || profile.Credits < 0
                || profile.Raids < 0 || profile.Extractions < 0 || profile.Extractions > profile.Raids
                || profile.Stash == null || profile.Loadout == null)
                throw new InvalidDataException("Invalid profile version, balances, counters or item lists.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateItems(profile.Stash, ids);
            ValidateItems(profile.Loadout, ids);
        }

        private static void ValidateItems(List<ItemStack> items, HashSet<string> ids)
        {
            var slots = new HashSet<EquipmentSlot>();
            foreach (ItemStack item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.InstanceId)
                    || string.IsNullOrWhiteSpace(item.ItemId) || !ids.Add(item.InstanceId)
                    || item.Quantity < 1 || item.X < 0 || item.Y < 0 || item.LoadedAmmo < -1
                    || !Enum.IsDefined(typeof(EquipmentSlot), item.Slot))
                    throw new InvalidDataException("Invalid profile item or duplicate instance ID.");
                if (item.Slot != EquipmentSlot.None && (item.Quantity != 1 || !slots.Add(item.Slot)))
                    throw new InvalidDataException("Invalid or duplicate equipped item.");
            }
        }

        private sealed class ProfileJsonValidator
        {
            private readonly string json;
            private int position;

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
                                if (ReadInteger() != 1)
                                    Fail("Unsupported profile version.");
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
                                case "ItemId":
                                    if (string.IsNullOrWhiteSpace(ReadString()))
                                        Fail("Empty item ID.");
                                    break;
                                case "Quantity":
                                    if (ReadInteger() < 1)
                                        Fail("Invalid item quantity.");
                                    break;
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
                                    if (slot < (int)EquipmentSlot.None || slot > (int)EquipmentSlot.Armor)
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
                } while (Take(','));
                Require(']');
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
