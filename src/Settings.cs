// Game Audio Keys - settings model, key catalog, validation and atomic persistence.
// No external dependencies beyond the in-box JavaScriptSerializer.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GameAudioKeys
{
    internal sealed class HotkeyBinding
    {
        public int Key;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;

        public HotkeyBinding()
        {
        }

        public HotkeyBinding(int key, bool ctrl, bool alt, bool shift)
        {
            this.Key = key;
            this.Ctrl = ctrl;
            this.Alt = alt;
            this.Shift = shift;
        }

        public HotkeyBinding Clone()
        {
            return new HotkeyBinding(this.Key, this.Ctrl, this.Alt, this.Shift);
        }

        public bool SameAs(HotkeyBinding other)
        {
            return other != null
                && this.Key == other.Key
                && this.Ctrl == other.Ctrl
                && this.Alt == other.Alt
                && this.Shift == other.Shift;
        }

        public string Display
        {
            get { return KeyCatalog.Format(this); }
        }
    }

    internal sealed class KeyOption
    {
        public readonly int VirtualKey;
        public readonly string Display;

        public KeyOption(int virtualKey, string display)
        {
            this.VirtualKey = virtualKey;
            this.Display = display;
        }

        public override string ToString()
        {
            return this.Display;
        }
    }

    // A deliberately small, readable key set. F12, bare modifiers and Windows keys
    // are excluded so no broad or reserved key is swallowed.
    internal static class KeyCatalog
    {
        private static readonly List<KeyOption> options = BuildOptions();
        private static readonly Dictionary<int, string> names = BuildNames();

        public static IList<KeyOption> Options
        {
            get { return options.AsReadOnly(); }
        }

        public static bool IsSupported(int virtualKey)
        {
            return names.ContainsKey(virtualKey);
        }

        public static string GetName(int virtualKey)
        {
            string name;
            return names.TryGetValue(virtualKey, out name) ? name : null;
        }

        public static string Format(HotkeyBinding binding)
        {
            if (binding == null)
            {
                return "(없음)";
            }

            StringBuilder text = new StringBuilder();
            if (binding.Ctrl)
            {
                text.Append("Ctrl+");
            }

            if (binding.Alt)
            {
                text.Append("Alt+");
            }

            if (binding.Shift)
            {
                text.Append("Shift+");
            }

            string name = GetName(binding.Key);
            text.Append(name == null ? "VK " + binding.Key.ToString(CultureInfo.InvariantCulture) : name);
            return text.ToString();
        }

        public static KeyOption Find(int virtualKey)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].VirtualKey == virtualKey)
                {
                    return options[i];
                }
            }

            return null;
        }

        private static List<KeyOption> BuildOptions()
        {
            List<KeyOption> list = new List<KeyOption>();
            Add(list, 0x2E, "Delete");
            Add(list, 0x21, "Page Up");
            Add(list, 0x22, "Page Down");
            Add(list, 0x2D, "Insert");
            Add(list, 0x24, "Home");
            Add(list, 0x23, "End");
            Add(list, 0x25, "Left");
            Add(list, 0x26, "Up");
            Add(list, 0x27, "Right");
            Add(list, 0x28, "Down");
            for (int key = 0x70; key <= 0x7A; key++)
            {
                Add(list, key, "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture));
            }

            for (int key = 0x41; key <= 0x5A; key++)
            {
                Add(list, key, ((char)key).ToString());
            }

            for (int key = 0x30; key <= 0x39; key++)
            {
                Add(list, key, ((char)key).ToString());
            }

            return list;
        }

        private static void Add(List<KeyOption> list, int key, string name)
        {
            list.Add(new KeyOption(key, name));
        }

        private static Dictionary<int, string> BuildNames()
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            for (int i = 0; i < options.Count; i++)
            {
                map[options[i].VirtualKey] = options[i].Display;
            }

            return map;
        }
    }

    internal sealed class AppSettings
    {
        public HotkeyBinding Mute;
        public HotkeyBinding VolumeUp;
        public HotkeyBinding VolumeDown;
        public int Step;
        public bool Enabled;

        public static AppSettings CreateDefaults()
        {
            AppSettings settings = new AppSettings();
            settings.Mute = new HotkeyBinding(0x2E, false, false, false);      // Delete
            settings.VolumeUp = new HotkeyBinding(0x21, false, false, false);  // Page Up
            settings.VolumeDown = new HotkeyBinding(0x22, false, false, false); // Page Down
            settings.Step = 5;
            settings.Enabled = true;
            return settings;
        }

        public AppSettings Clone()
        {
            AppSettings copy = new AppSettings();
            copy.Mute = this.Mute == null ? null : this.Mute.Clone();
            copy.VolumeUp = this.VolumeUp == null ? null : this.VolumeUp.Clone();
            copy.VolumeDown = this.VolumeDown == null ? null : this.VolumeDown.Clone();
            copy.Step = this.Step;
            copy.Enabled = this.Enabled;
            return copy;
        }
    }

    internal sealed class SettingsValidation
    {
        public readonly List<string> Errors = new List<string>();

        public bool IsValid
        {
            get { return this.Errors.Count == 0; }
        }

        public string Summary
        {
            get { return string.Join(Environment.NewLine, this.Errors.ToArray()); }
        }
    }

    internal static class SettingsValidator
    {
        public static SettingsValidation Validate(AppSettings settings)
        {
            SettingsValidation result = new SettingsValidation();
            if (settings == null)
            {
                result.Errors.Add("설정이 없습니다.");
                return result;
            }

            if (settings.Step < 1 || settings.Step > 20)
            {
                result.Errors.Add("볼륨 단계는 1~20 사이여야 합니다. (현재 " + settings.Step.ToString(CultureInfo.InvariantCulture) + ")");
            }

            ValidateBinding(result, "음소거 전환", settings.Mute);
            ValidateBinding(result, "볼륨 올리기", settings.VolumeUp);
            ValidateBinding(result, "볼륨 내리기", settings.VolumeDown);

            Dictionary<string, string> seen = new Dictionary<string, string>();
            CheckDuplicate(result, seen, "음소거 전환", settings.Mute);
            CheckDuplicate(result, seen, "볼륨 올리기", settings.VolumeUp);
            CheckDuplicate(result, seen, "볼륨 내리기", settings.VolumeDown);

            return result;
        }

        private static void CheckDuplicate(SettingsValidation result, Dictionary<string, string> seen, string slot, HotkeyBinding binding)
        {
            if (binding == null || !KeyCatalog.IsSupported(binding.Key))
            {
                return;
            }

            string key = Canonical(binding);
            string previous;
            if (seen.TryGetValue(key, out previous))
            {
                result.Errors.Add("단축키가 중복됩니다: " + previous + "와 " + slot + " (" + binding.Display + ")");
            }
            else
            {
                seen[key] = slot;
            }
        }

        private static string Canonical(HotkeyBinding binding)
        {
            return binding.Key.ToString(CultureInfo.InvariantCulture)
                + ":" + (binding.Ctrl ? "1" : "0")
                + ":" + (binding.Alt ? "1" : "0")
                + ":" + (binding.Shift ? "1" : "0");
        }

        private static void ValidateBinding(SettingsValidation result, string slot, HotkeyBinding binding)
        {
            if (binding == null)
            {
                result.Errors.Add(slot + " 단축키가 없습니다.");
                return;
            }

            if (binding.Key == 0x5B || binding.Key == 0x5C)
            {
                result.Errors.Add(slot + ": Windows 키가 포함된 조합은 사용할 수 없습니다.");
                return;
            }

            if (IsBareModifier(binding.Key))
            {
                result.Errors.Add(slot + ": 수정자 키만으로는 단축키를 만들 수 없습니다.");
                return;
            }

            if (binding.Key == 0x7B)
            {
                result.Errors.Add(slot + ": F12는 Windows 디버거 예약 키라 사용할 수 없습니다.");
                return;
            }

            // Reserved combinations are reported specifically even when the key itself
            // is not in the selectable catalog (Tab, Escape).
            if (binding.Key == 0x2E && binding.Ctrl && binding.Alt)
            {
                result.Errors.Add(slot + ": Ctrl+Alt+Delete는 Windows가 예약한 조합입니다.");
                return;
            }

            if (binding.Key == 0x09 && binding.Alt)
            {
                result.Errors.Add(slot + ": Alt+Tab은 Windows가 예약한 조합입니다.");
                return;
            }

            if (binding.Key == 0x1B && binding.Ctrl && binding.Shift)
            {
                result.Errors.Add(slot + ": Ctrl+Shift+Escape는 작업 관리자 예약 조합입니다.");
                return;
            }

            if (binding.Key == 0x1B && binding.Ctrl)
            {
                result.Errors.Add(slot + ": Ctrl+Escape는 Windows가 예약한 조합입니다.");
                return;
            }

            if (binding.Key == 0x73 && binding.Alt)
            {
                result.Errors.Add(slot + ": Alt+F4는 Windows가 예약한 조합입니다.");
                return;
            }

            if (!KeyCatalog.IsSupported(binding.Key))
            {
                result.Errors.Add(slot + ": 지원하지 않는 키입니다. (VK " + binding.Key.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }

        private static bool IsBareModifier(int key)
        {
            return key == 0x10      // Shift
                || key == 0x11      // Ctrl
                || key == 0x12      // Alt
                || key == 0x14      // Caps Lock
                || key == 0x5B      // Left Windows
                || key == 0x5C      // Right Windows
                || (key >= 0xA0 && key <= 0xA5);
        }
    }

    // Plain JSON DTOs. Public fields keep the on-disk format stable and readable.
    public sealed class BindingJson
    {
        public int key;
        public bool ctrl;
        public bool alt;
        public bool shift;
    }

    public sealed class SettingsJson
    {
        public int version = 1;
        public BindingJson mute;
        public BindingJson volumeUp;
        public BindingJson volumeDown;
        public int step = 5;
        public bool enabled = true;
    }

    internal sealed class SettingsLoadResult
    {
        public AppSettings Settings;
        public bool FirstLaunch;
        public bool Corrupt;
        public string StatusKo;
        public string FilePath;
    }

    internal static class SettingsStore
    {
        public const int CurrentVersion = 1;

        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameAudioKeys");
            }
        }

        public static string DefaultPath
        {
            get { return Path.Combine(DefaultDirectory, "settings.json"); }
        }

        public static SettingsLoadResult Load(string path)
        {
            SettingsLoadResult result = new SettingsLoadResult();
            result.FilePath = path;
            result.Settings = AppSettings.CreateDefaults();

            if (string.IsNullOrEmpty(path))
            {
                result.Corrupt = true;
                result.StatusKo = "설정 파일 경로가 올바르지 않아 기본값으로 실행합니다.";
                return result;
            }

            if (!File.Exists(path))
            {
                result.FirstLaunch = true;
                result.StatusKo = "첫 실행입니다. 기본 단축키(Delete, Page Up, Page Down)로 시작합니다.";
                return result;
            }

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                result.Corrupt = true;
                result.StatusKo = "설정 파일을 읽지 못해 기본값으로 실행합니다. 원본은 보존했습니다: "
                    + path + Environment.NewLine + ex.Message;
                return result;
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                SettingsJson json = serializer.Deserialize<SettingsJson>(text);
                if (json == null)
                {
                    throw new InvalidOperationException("설정 내용이 비어 있습니다.");
                }

                if (json.version != CurrentVersion)
                {
                    result.Corrupt = true;
                    result.StatusKo = "지원하지 않는 설정 버전(" + json.version.ToString(CultureInfo.InvariantCulture)
                        + ")이라 기본값으로 실행합니다. 원본은 그대로 두었습니다: " + path;
                    return result;
                }

                AppSettings loaded = FromJson(json);
                SettingsValidation validation = SettingsValidator.Validate(loaded);
                if (!validation.IsValid)
                {
                    result.Corrupt = true;
                    result.StatusKo = "설정 파일이 올바르지 않아 기본값으로 실행합니다. 원본은 그대로 두었습니다: "
                        + path + Environment.NewLine + validation.Summary;
                    return result;
                }

                result.Settings = loaded;
                result.StatusKo = "설정을 불러왔습니다: " + path;
                return result;
            }
            catch (Exception ex)
            {
                result.Corrupt = true;
                result.StatusKo = "설정 파일을 해석하지 못해 기본값으로 실행합니다. 원본은 그대로 두었습니다: "
                    + path + Environment.NewLine + ex.Message;
                return result;
            }
        }

        // Writes a sibling temporary file first, then replaces the real file so a crash
        // or write failure cannot leave a half-written settings.json.
        public static bool TrySave(string path, AppSettings settings, out string errorKo)
        {
            errorKo = null;
            string temporary = null;
            try
            {
                SettingsValidation validation = SettingsValidator.Validate(settings);
                if (!validation.IsValid)
                {
                    errorKo = "저장할 설정이 올바르지 않습니다: " + Environment.NewLine + validation.Summary;
                    return false;
                }

                if (string.IsNullOrEmpty(path))
                {
                    errorKo = "설정 파일 경로가 올바르지 않습니다.";
                    return false;
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(ToJson(settings));
                temporary = path + ".tmp";
                File.WriteAllText(temporary, json + Environment.NewLine, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    // Never silently destroy a config that could not be loaded: the
                    // existing loader decides validity, and an invalid original is
                    // atomically moved to a unique sibling backup by File.Replace.
                    SettingsLoadResult existing = Load(path);
                    if (existing.Corrupt)
                    {
                        string backup = CreateInvalidBackupPath(path);
                        File.Replace(temporary, path, backup);
                    }
                    else
                    {
                        File.Replace(temporary, path, null);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorKo = ex.Message;
                if (temporary != null)
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    catch (Exception)
                    {
                        // Leaving a stray .tmp file is harmless; the real settings file is intact.
                    }
                }

                return false;
            }
        }

        private static string CreateInvalidBackupPath(string path)
        {
            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                string candidateName = fileName + ".invalid-" + stamp + "-" + suffix + ".bak";
                string candidate = string.IsNullOrEmpty(directory)
                    ? candidateName
                    : Path.Combine(directory, candidateName);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("고유한 설정 백업 파일 이름을 만들지 못했습니다.");
        }

        private static SettingsJson ToJson(AppSettings settings)
        {
            SettingsJson json = new SettingsJson();
            json.version = CurrentVersion;
            json.mute = ToJson(settings.Mute);
            json.volumeUp = ToJson(settings.VolumeUp);
            json.volumeDown = ToJson(settings.VolumeDown);
            json.step = settings.Step;
            json.enabled = settings.Enabled;
            return json;
        }

        private static BindingJson ToJson(HotkeyBinding binding)
        {
            if (binding == null)
            {
                return null;
            }

            BindingJson json = new BindingJson();
            json.key = binding.Key;
            json.ctrl = binding.Ctrl;
            json.alt = binding.Alt;
            json.shift = binding.Shift;
            return json;
        }

        private static AppSettings FromJson(SettingsJson json)
        {
            AppSettings settings = new AppSettings();
            settings.Mute = FromJson(json.mute);
            settings.VolumeUp = FromJson(json.volumeUp);
            settings.VolumeDown = FromJson(json.volumeDown);
            settings.Step = json.step;
            settings.Enabled = json.enabled;
            return settings;
        }

        private static HotkeyBinding FromJson(BindingJson json)
        {
            if (json == null)
            {
                return null;
            }

            return new HotkeyBinding(json.key, json.ctrl, json.alt, json.shift);
        }
    }
}
