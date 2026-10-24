// Dependency-free console tests for the shared application logic. No test framework.
// Settings always use a temporary directory; no real user config or audio is touched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameAudioKeys.Core;

namespace GameAudioKeys.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        public static int Main(string[] args)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "GameAudioKeys.Tests-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                Run(temporaryDirectory);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL unexpected exception: " + ex);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WARN temporary directory cleanup failed: " + ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Tests: " + (passed + failed).ToString(CultureInfo.InvariantCulture)
                + ", passed: " + passed.ToString(CultureInfo.InvariantCulture)
                + ", failed: " + failed.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string temporaryDirectory)
        {
            TestDefaults();
            TestVolume();
            TestToggleDecision();
            TestValidation();
            TestKeyCatalog();
            TestSettingsRoundTrip(temporaryDirectory);
            TestMissingSettings(temporaryDirectory);
            TestCorruptSettingsPreserved(temporaryDirectory);
            TestInvalidSettingsPreserved(temporaryDirectory);
            TestUnsupportedVersionPreserved(temporaryDirectory);
            TestCorruptSettingsPreservedOnSave(temporaryDirectory);
            TestInvalidSettingsPreservedOnSave(temporaryDirectory);
            TestTargetSelection();
        }

        private static void TestDefaults()
        {
            AppSettings defaults = AppSettings.CreateDefaults();
            SettingsValidation validation = SettingsValidator.Validate(defaults);
            Check("defaults-valid", validation.IsValid, validation.Summary);
            Check("defaults-bindings",
                defaults.Mute.Key == 0x2E && !defaults.Mute.Ctrl && !defaults.Mute.Alt && !defaults.Mute.Shift
                && defaults.VolumeUp.Key == 0x21 && !defaults.VolumeUp.Ctrl
                && defaults.VolumeDown.Key == 0x22 && !defaults.VolumeDown.Ctrl,
                "default keys are Delete/Page Up/Page Down without modifiers");
            Check("defaults-step-enabled", defaults.Step == 5 && defaults.Enabled,
                "step=" + defaults.Step.ToString(CultureInfo.InvariantCulture) + ", enabled=" + defaults.Enabled.ToString());
        }

        private static void TestVolume()
        {
            Check("volume-lower-edge", Near(VolumeMath.Step(0f, -5), 0f),
                "0% - 5%p must clamp to 0");
            Check("volume-upper-edge", Near(VolumeMath.Step(1f, 5), 1f),
                "100% + 5%p must clamp to 100");
            Check("volume-near-upper", Near(VolumeMath.Step(0.98f, 5), 1f),
                "98% + 5%p must clamp to 100");
            Check("volume-near-lower", Near(VolumeMath.Step(0.02f, -5), 0f),
                "2% - 5%p must clamp to 0");
            Check("volume-relative-up", Near(VolumeMath.Step(0.5f, 5), 0.55f),
                "50% + 5%p must be 55%");
            Check("volume-relative-down", Near(VolumeMath.Step(0.5f, -5), 0.45f),
                "50% - 5%p must be 45%");
            Check("volume-large-step", Near(VolumeMath.Step(0.95f, 20), 1f),
                "95% + 20%p must clamp to 100");
            Check("volume-clamp-direct",
                Near(VolumeMath.Clamp01(-0.5f), 0f)
                && Near(VolumeMath.Clamp01(1.5f), 1f)
                && Near(VolumeMath.Clamp01(float.NaN), 0f),
                "Clamp01 handles negative, over-range and NaN");
            Check("volume-percent", VolumeMath.ToPercent(0.55f) == 55 && VolumeMath.ToPercent(1f) == 100,
                "percent conversion");
        }

        private static void TestToggleDecision()
        {
            Check("toggle-all-muted-unmutes",
                !ToggleDecision.ShouldMute(new bool[] { true, true, true }),
                "all muted -> unmute all");
            Check("toggle-none-muted-mutes",
                ToggleDecision.ShouldMute(new bool[] { false, false }),
                "none muted -> mute all");
            Check("toggle-mixed-mutes",
                ToggleDecision.ShouldMute(new bool[] { true, false }),
                "mixed -> mute all");
            Check("toggle-empty-no-change",
                !ToggleDecision.ShouldMute(new bool[0]),
                "no sessions -> no requested change");
        }

        private static void TestValidation()
        {
            AppSettings defaults = AppSettings.CreateDefaults();
            Check("validation-valid-modified",
                SettingsValidator.Validate(WithMute(defaults, new HotkeyBinding(0x2E, true, false, false))).IsValid,
                "Ctrl+Delete is allowed");

            AppSettings duplicate = AppSettings.CreateDefaults();
            duplicate.VolumeUp = duplicate.Mute.Clone();
            ExpectInvalid("validation-duplicate", duplicate, "중복");

            AppSettings stepLow = AppSettings.CreateDefaults();
            stepLow.Step = 0;
            ExpectInvalid("validation-step-low", stepLow, "1~20");

            AppSettings stepHigh = AppSettings.CreateDefaults();
            stepHigh.Step = 21;
            ExpectInvalid("validation-step-high", stepHigh, "1~20");

            AppSettings unsupported = AppSettings.CreateDefaults();
            unsupported.Mute = new HotkeyBinding(0x01, false, false, false);
            ExpectInvalid("validation-unsupported-key", unsupported, "지원하지");

            AppSettings f12 = AppSettings.CreateDefaults();
            f12.Mute = new HotkeyBinding(0x7B, false, false, false);
            ExpectInvalid("validation-f12", f12, "F12");

            AppSettings bareModifier = AppSettings.CreateDefaults();
            bareModifier.Mute = new HotkeyBinding(0x11, false, false, false);
            ExpectInvalid("validation-bare-modifier", bareModifier, "수정자");

            AppSettings cad = AppSettings.CreateDefaults();
            cad.Mute = new HotkeyBinding(0x2E, true, true, false);
            ExpectInvalid("validation-ctrl-alt-delete", cad, "Ctrl+Alt+Delete");

            AppSettings altTab = AppSettings.CreateDefaults();
            altTab.Mute = new HotkeyBinding(0x09, false, true, false);
            ExpectInvalid("validation-alt-tab", altTab, "Alt+Tab");

            AppSettings ctrlEsc = AppSettings.CreateDefaults();
            ctrlEsc.Mute = new HotkeyBinding(0x1B, true, false, false);
            ExpectInvalid("validation-ctrl-escape", ctrlEsc, "Ctrl+Escape");

            AppSettings ctrlShiftEsc = AppSettings.CreateDefaults();
            ctrlShiftEsc.Mute = new HotkeyBinding(0x1B, true, false, true);
            ExpectInvalid("validation-ctrl-shift-escape", ctrlShiftEsc, "작업 관리자");

            AppSettings altF4 = AppSettings.CreateDefaults();
            altF4.Mute = new HotkeyBinding(0x73, false, true, false);
            ExpectInvalid("validation-alt-f4", altF4, "Alt+F4");

            AppSettings missing = AppSettings.CreateDefaults();
            missing.Mute = null;
            ExpectInvalid("validation-missing-binding", missing, "없습니다");

            Check("binding-display",
                new HotkeyBinding(0x2E, true, true, true).Display == "Ctrl+Alt+Shift+Delete",
                "human-readable binding text");
        }

        private static void TestKeyCatalog()
        {
            Check("key-catalog-supported",
                KeyCatalog.IsSupported(0x2E)
                && KeyCatalog.IsSupported(0x21)
                && KeyCatalog.IsSupported(0x22)
                && KeyCatalog.IsSupported(0x70)
                && KeyCatalog.IsSupported(0x41)
                && KeyCatalog.IsSupported(0x30),
                "Delete/PageUp/PageDown/F1/letters/digits supported");
            Check("key-catalog-rejected",
                !KeyCatalog.IsSupported(0x7B) && !KeyCatalog.IsSupported(0x11) && !KeyCatalog.IsSupported(0x5B),
                "F12, bare modifier and Windows key rejected");
            Check("key-catalog-name",
                KeyCatalog.GetName(0x21) == "Page Up" && KeyCatalog.GetName(0x22) == "Page Down",
                "display names are human readable");
        }

        private static void TestSettingsRoundTrip(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "roundtrip", "settings.json");
            AppSettings settings = AppSettings.CreateDefaults();
            settings.Mute = new HotkeyBinding(0x2E, true, false, false);
            settings.VolumeUp = new HotkeyBinding(0x21, false, true, true);
            settings.VolumeDown = new HotkeyBinding(0x22, false, false, false);
            settings.Step = 7;
            settings.Enabled = false;

            string error;
            Check("settings-save", SettingsStore.TrySave(path, settings, out error), error);
            Check("settings-save-atomic", !File.Exists(path + ".tmp") && File.Exists(path),
                "temporary sibling removed after replace");
            Check("settings-save-no-invalid-backup",
                Directory.GetFiles(Path.GetDirectoryName(path), "*.invalid-*.bak").Length == 0,
                "valid overwrite must not create an invalid backup");

            SettingsLoadResult loaded = SettingsStore.Load(path);
            Check("settings-roundtrip",
                !loaded.FirstLaunch
                && !loaded.Corrupt
                && Same(loaded.Settings.Mute, settings.Mute)
                && Same(loaded.Settings.VolumeUp, settings.VolumeUp)
                && Same(loaded.Settings.VolumeDown, settings.VolumeDown)
                && loaded.Settings.Step == 7
                && !loaded.Settings.Enabled,
                loaded.StatusKo);

            settings.Step = 9;
            settings.Enabled = true;
            Check("settings-overwrite", SettingsStore.TrySave(path, settings, out error), error);
            SettingsLoadResult overwritten = SettingsStore.Load(path);
            Check("settings-overwrite-roundtrip",
                !overwritten.Corrupt && overwritten.Settings.Step == 9 && overwritten.Settings.Enabled,
                "second atomic replace keeps valid data");
        }

        private static void TestMissingSettings(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "missing", "settings.json");
            SettingsLoadResult result = SettingsStore.Load(path);
            Check("settings-missing-defaults",
                result.FirstLaunch
                && !result.Corrupt
                && SettingsValidator.Validate(result.Settings).IsValid,
                result.StatusKo);
            Check("settings-missing-not-created", !File.Exists(path),
                "loading must not write a config file");
        }

        private static void TestCorruptSettingsPreserved(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "corrupt", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string corruptText = "{ this is not json";
            File.WriteAllText(path, corruptText, new UTF8Encoding(false));

            SettingsLoadResult result = SettingsStore.Load(path);
            Check("settings-corrupt-defaults",
                result.Corrupt
                && !result.FirstLaunch
                && result.Settings.Step == 5
                && result.Settings.Mute.Key == 0x2E,
                result.StatusKo);
            Check("settings-corrupt-preserved",
                File.ReadAllText(path, Encoding.UTF8) == corruptText,
                "corrupt original file must not be overwritten on load");
            Check("settings-corrupt-readable-status",
                result.StatusKo.IndexOf("기본값", StringComparison.Ordinal) >= 0
                && result.StatusKo.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0,
                result.StatusKo);
        }

        private static void TestInvalidSettingsPreserved(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "invalid", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string invalidJson =
                "{\"version\":1,"
                + "\"mute\":{\"key\":46,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeUp\":{\"key\":33,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeDown\":{\"key\":34,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"step\":99,\"enabled\":true}";
            File.WriteAllText(path, invalidJson, new UTF8Encoding(false));

            SettingsLoadResult result = SettingsStore.Load(path);
            Check("settings-invalid-defaults",
                result.Corrupt && result.Settings.Step == 5,
                result.StatusKo);
            Check("settings-invalid-preserved",
                File.ReadAllText(path, Encoding.UTF8) == invalidJson,
                "invalid original file must not be overwritten on load");
            Check("settings-invalid-status",
                result.StatusKo.IndexOf("볼륨 단계", StringComparison.Ordinal) >= 0,
                result.StatusKo);
        }

        private static void TestUnsupportedVersionPreserved(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "version", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json =
                "{\"version\":2,"
                + "\"mute\":{\"key\":46,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeUp\":{\"key\":33,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeDown\":{\"key\":34,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"step\":5,\"enabled\":true}";
            File.WriteAllText(path, json, new UTF8Encoding(false));

            SettingsLoadResult result = SettingsStore.Load(path);
            Check("settings-version-defaults", result.Corrupt && result.Settings.Step == 5, result.StatusKo);
            Check("settings-version-preserved", File.ReadAllText(path, Encoding.UTF8) == json,
                "unsupported-version file must be preserved");
        }

        private static void TestCorruptSettingsPreservedOnSave(string temporaryDirectory)
        {
            string directory = Path.Combine(temporaryDirectory, "save-corrupt");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            string original = "{ this is not json";
            File.WriteAllText(path, original, new UTF8Encoding(false));

            SettingsLoadResult before = SettingsStore.Load(path);
            Check("save-corrupt-detected", before.Corrupt, before.StatusKo);

            AppSettings paused = AppSettings.CreateDefaults();
            paused.Enabled = false;
            string error;
            Check("save-corrupt-succeeds", SettingsStore.TrySave(path, paused, out error), error);

            string[] backups = Directory.GetFiles(directory, "settings.json.invalid-*.bak");
            Check("save-corrupt-backup-created", backups.Length == 1,
                "expected exactly one .invalid-*.bak backup, found " + backups.Length.ToString(CultureInfo.InvariantCulture));
            Check("save-corrupt-backup-bytes",
                backups.Length == 1 && File.ReadAllText(backups[0], Encoding.UTF8) == original,
                "original corrupt bytes must survive in the backup");

            SettingsLoadResult after = SettingsStore.Load(path);
            Check("save-corrupt-new-valid",
                !after.Corrupt && SettingsValidator.Validate(after.Settings).IsValid && !after.Settings.Enabled,
                after.StatusKo);

            // A later save over the now-valid file must not create another backup.
            Check("save-corrupt-second-save", SettingsStore.TrySave(path, paused, out error), error);
            Check("save-corrupt-backup-stable",
                Directory.GetFiles(directory, "settings.json.invalid-*.bak").Length == 1,
                "valid overwrite must not create an invalid backup");
        }

        private static void TestInvalidSettingsPreservedOnSave(string temporaryDirectory)
        {
            string directory = Path.Combine(temporaryDirectory, "save-invalid");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            string invalidJson =
                "{\"version\":1,"
                + "\"mute\":{\"key\":46,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeUp\":{\"key\":33,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeDown\":{\"key\":34,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"step\":99,\"enabled\":true}";
            File.WriteAllText(path, invalidJson, new UTF8Encoding(false));

            string error;
            Check("save-invalid-succeeds",
                SettingsStore.TrySave(path, AppSettings.CreateDefaults(), out error),
                error);

            string[] backups = Directory.GetFiles(directory, "settings.json.invalid-*.bak");
            Check("save-invalid-backup-created", backups.Length == 1,
                "expected exactly one .invalid-*.bak backup, found " + backups.Length.ToString(CultureInfo.InvariantCulture));
            Check("save-invalid-backup-bytes",
                backups.Length == 1 && File.ReadAllText(backups[0], Encoding.UTF8) == invalidJson,
                "original invalid bytes must survive in the backup");

            SettingsLoadResult after = SettingsStore.Load(path);
            Check("save-invalid-new-valid",
                !after.Corrupt && SettingsValidator.Validate(after.Settings).IsValid,
                after.StatusKo);
        }

        private static void TestTargetSelection()
        {
            ProcTable table = new ProcTable();
            table.AddSynthetic(100, 1, "game.exe", true, 1000, @"C:\Game\game.exe");
            table.AddSynthetic(200, 100, "game.exe", true, 2000, @"C:\Game\game.exe");
            table.AddSynthetic(300, 100, "other.exe", true, 3000, @"C:\Other\other.exe");

            AudioSession direct = NewSession(100);
            AudioSession descendant = NewSession(200);
            AudioSession otherExecutable = NewSession(300);
            AudioSession systemSounds = NewSession(100);
            systemSounds.SystemSounds = true;
            AudioSession crossProcess = NewSession(100);
            crossProcess.CrossProcess = true;
            AudioSession expired = NewSession(100);
            expired.State = 2;

            List<AudioSession> sessions = new List<AudioSession>();
            sessions.Add(direct);
            sessions.Add(descendant);
            sessions.Add(otherExecutable);
            sessions.Add(systemSounds);
            sessions.Add(crossProcess);
            sessions.Add(expired);

            List<AudioSession> selected = Targeting.SelectSessions(table, 100, sessions);
            Check("target-selection-shared",
                selected.Count == 2 && selected[0] == direct && selected[1] == descendant,
                "only direct PID and same-image descendant are selected");
            Check("target-selection-exclusions",
                systemSounds.MatchReason == "system-sounds"
                && crossProcess.MatchReason == "cross-process"
                && expired.MatchReason == "expired"
                && otherExecutable.MatchReason == "different-executable",
                "system/cross/expired/different-executable reasons");
        }

        private static AudioSession NewSession(int pid)
        {
            AudioSession session = new AudioSession();
            session.Pid = pid;
            session.PidReadOk = true;
            session.State = 1;
            session.StateReadOk = true;
            session.Muted = false;
            session.MuteReadOk = true;
            session.Volume = 0.5f;
            session.VolumeReadOk = true;
            session.InstanceId = "test-" + pid.ToString(CultureInfo.InvariantCulture);
            return session;
        }

        private static AppSettings WithMute(AppSettings source, HotkeyBinding mute)
        {
            AppSettings copy = source.Clone();
            copy.Mute = mute;
            return copy;
        }

        private static void ExpectInvalid(string name, AppSettings settings, string expectedText)
        {
            SettingsValidation validation = SettingsValidator.Validate(settings);
            Check(name,
                !validation.IsValid
                && validation.Summary.IndexOf(expectedText, StringComparison.Ordinal) >= 0,
                validation.IsValid ? "expected invalid" : validation.Summary);
        }

        private static bool Same(HotkeyBinding left, HotkeyBinding right)
        {
            return left != null && right != null && left.SameAs(right);
        }

        private static bool Near(float left, float right)
        {
            return Math.Abs(left - right) <= 0.0001f;
        }

        private static void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("PASS " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("FAIL " + name + " - " + detail);
            }
        }
    }
}
