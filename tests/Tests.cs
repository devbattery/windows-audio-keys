// Dependency-free console tests for the shared application logic. No test framework.
// Settings always use a temporary directory; no real user config or audio is touched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml;
using GameAudioKeys.Core;

namespace GameAudioKeys.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        [STAThread]
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
            TestStartupCommandLine();
            TestStartupLaunchPolicy();
            TestStartupTaskDefinition();
            TestStartupTaskEvaluation();
            TestStartupTaskIdentityRoundTrip();
            TestStartupApplyPlanner();
            TestStartupTaskPlatformXml();
        }

        private static void TestStartupLaunchPolicy()
        {
            Check("startup-window-manual-with-saved-settings",
                StartupLaunchPolicy.ShouldShowSettings(false, false),
                "a manual launch with valid saved settings must show the window");
            Check("startup-window-manual-with-corrupt-settings",
                StartupLaunchPolicy.ShouldShowSettings(false, true),
                "a manual launch with corrupt settings must show the window");
            Check("startup-window-login-hidden",
                !StartupLaunchPolicy.ShouldShowSettings(true, false),
                "login startup with valid settings must stay silent in the tray");
            Check("startup-window-login-corrupt-visible",
                StartupLaunchPolicy.ShouldShowSettings(true, true),
                "login startup with corrupt settings must show the actionable error");
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

        private static void TestStartupCommandLine()
        {
            const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";

            StartupCommandLine defaultRun = StartupCli.Parse(null);
            Check("startup-cli-default-run",
                defaultRun.Kind == StartupCommandKind.Run
                && !defaultRun.StartupMode
                && !defaultRun.SettingsDirectoryOverridden,
                "no arguments means an ordinary manual launch");

            StartupCommandLine startup = StartupCli.Parse(new string[] { "--startup" });
            Check("startup-cli-startup-flag",
                startup.Kind == StartupCommandKind.Run
                && startup.StartupMode
                && !startup.SettingsDirectoryOverridden,
                "--startup is a quiet run mode");

            StartupCommandLine settings = StartupCli.Parse(new string[] { "--settings-dir", @"C:\temp\gak-settings" });
            Check("startup-cli-settings-dir",
                settings.Kind == StartupCommandKind.Run
                && !settings.StartupMode
                && settings.SettingsDirectoryOverridden
                && settings.SettingsDirectory == @"C:\temp\gak-settings",
                "test settings directory is tracked as an override");

            StartupCommandLine settingsEquals = StartupCli.Parse(
                new string[] { "--settings-dir=C:\\temp\\gak-settings", "--startup" });
            Check("startup-cli-settings-dir-equals",
                settingsEquals.Kind == StartupCommandKind.Run
                && settingsEquals.StartupMode
                && settingsEquals.SettingsDirectoryOverridden,
                "--settings-dir=... combines with --startup");

            Check("startup-cli-override-args",
                StartupCli.IsSettingsOverrideActive(true, null)
                && !StartupCli.IsSettingsOverrideActive(false, null),
                "explicit test settings directory activates the override even without a nonempty path string");
            Check("startup-cli-override-env",
                StartupCli.IsSettingsOverrideActive(false, @"C:\temp")
                && !StartupCli.IsSettingsOverrideActive(false, ""),
                "environment test settings directory activates the override");

            StartupCommandLine helperEnable = StartupCli.Parse(new string[]
            {
                "--configure-startup", "enable", "--request-user-sid", sid
            });
            Check("startup-cli-helper-enable",
                helperEnable.Kind == StartupCommandKind.Helper
                && helperEnable.HelperEnable
                && helperEnable.HelperUserSid == sid,
                "helper enable syntax is accepted");

            StartupCommandLine helperDisable = StartupCli.Parse(new string[]
            {
                "--configure-startup=disable", "--request-user-sid=" + sid
            });
            Check("startup-cli-helper-disable",
                helperDisable.Kind == StartupCommandKind.Helper
                && !helperDisable.HelperEnable
                && helperDisable.HelperUserSid == sid,
                "helper disable syntax with '=' is accepted");

            ExpectStartupError("startup-cli-helper-startup-combination",
                new string[] { "--startup", "--configure-startup", "enable", "--request-user-sid", sid },
                "--startup");
            ExpectStartupError("startup-cli-helper-settings-combination",
                new string[] { "--settings-dir", @"C:\temp", "--configure-startup", "enable", "--request-user-sid", sid },
                "--settings-dir");
            ExpectStartupError("startup-cli-helper-missing-value",
                new string[] { "--configure-startup" },
                "--configure-startup");
            ExpectStartupError("startup-cli-helper-invalid-action",
                new string[] { "--configure-startup", "later", "--request-user-sid", sid },
                "enable");
            ExpectStartupError("startup-cli-helper-invalid-sid",
                new string[] { "--configure-startup", "enable", "--request-user-sid", "S-1-5-21-abc" },
                "SID");
            ExpectStartupError("startup-cli-helper-missing-sid",
                new string[] { "--configure-startup", "enable" },
                "--request-user-sid");
            ExpectStartupError("startup-cli-helper-missing-action",
                new string[] { "--request-user-sid", sid },
                "--configure-startup");
            ExpectStartupError("startup-cli-helper-extra-argument",
                new string[] { "--configure-startup", "enable", "--request-user-sid", sid, "--settings-dir", @"C:\temp" },
                "--settings-dir");
            ExpectStartupError("startup-cli-duplicate-startup",
                new string[] { "--startup", "--startup" },
                "한 번");
            ExpectStartupError("startup-cli-unknown-argument",
                new string[] { "--configure-startup", "enable", "--request-user-sid", sid, "--exec", @"C:\other.exe" },
                "알 수 없는");
            ExpectStartupError("startup-cli-help-combination",
                new string[] { "--help", "--startup" },
                "--help");
            ExpectStartupError("startup-cli-missing-sid-value",
                new string[] { "--configure-startup", "enable", "--request-user-sid" },
                "--request-user-sid");
            ExpectStartupError("startup-cli-settings-dir-missing-value",
                new string[] { "--settings-dir" },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-empty-separated",
                new string[] { "--settings-dir", "" },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-whitespace-separated",
                new string[] { "--settings-dir", "   " },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-empty-equals",
                new string[] { "--settings-dir=" },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-whitespace-equals",
                new string[] { "--settings-dir=   " },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-option-as-value",
                new string[] { "--settings-dir", "--startup" },
                "--settings-dir");
            ExpectStartupError("startup-cli-settings-dir-option-as-equals-value",
                new string[] { "--settings-dir=--startup" },
                "--settings-dir");
            ExpectStartupError("startup-cli-configure-option-as-value",
                new string[] { "--configure-startup", "--request-user-sid", sid },
                "--configure-startup");
            ExpectStartupError("startup-cli-configure-empty-equals",
                new string[] { "--configure-startup=", "--request-user-sid", sid },
                "--configure-startup");
            ExpectStartupError("startup-cli-sid-option-as-value",
                new string[] { "--configure-startup", "enable", "--request-user-sid", "--startup" },
                "--request-user-sid");

            Check("startup-sid-syntax",
                StartupSid.IsValid("S-1-5-18")
                && StartupSid.IsValid(sid)
                && !StartupSid.IsValid("WD")
                && !StartupSid.IsValid("S-1-5")
                && !StartupSid.IsValid("S-1-5-21-abc")
                && !StartupSid.IsValid(" S-1-5-18")
                && !StartupSid.IsValid(string.Empty),
                "numeric SID syntax only; aliases and malformed values rejected");

            Check("startup-cli-helper-arguments",
                StartupCli.BuildHelperArguments(true, sid)
                    == "--configure-startup enable --request-user-sid " + sid
                && StartupCli.BuildHelperArguments(false, sid)
                    == "--configure-startup disable --request-user-sid " + sid,
                "helper command contains only the fixed switches and the SID");
        }

        private static void TestStartupTaskDefinition()
        {
            const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
            const string directory = @"C:\Tools & Games\Windows Audio Keys";
            const string executable = directory + @"\WindowsAudioKeys.exe";

            StartupTaskDefinition definition = new StartupTaskDefinition(sid, executable, directory, "--startup");
            Check("startup-task-name", definition.TaskName == "WindowsAudioKeys-" + sid, definition.TaskName);
            Check("startup-task-description",
                definition.Description.IndexOf(StartupConstants.OwnershipMarker, StringComparison.Ordinal) >= 0
                && definition.Description.IndexOf(StartupConstants.ProductName, StringComparison.Ordinal) >= 0,
                definition.Description);
            Check("startup-task-ownership-detection",
                definition.OwnsDescription("x " + StartupConstants.OwnershipMarker + " y")
                && !definition.OwnsDescription("some other task")
                && !definition.OwnsDescription(null),
                "ownership marker detection");

            string xml = definition.ToTaskXml();
            XmlDocument document = new XmlDocument();
            string parseError = null;
            try
            {
                document.LoadXml(xml);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            Check("startup-task-xml-wellformed", parseError == null, parseError);

            XmlNamespaceManager namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

            Check("startup-task-principal",
                Text(document, namespaces, "/t:Task/t:Principals/t:Principal/t:UserId") == sid
                && Text(document, namespaces, "/t:Task/t:Principals/t:Principal/t:LogonType") == "InteractiveToken"
                && Text(document, namespaces, "/t:Task/t:Principals/t:Principal/t:RunLevel") == "HighestAvailable",
                "principal is the current SID, interactive token, highest available");

            XmlNodeList triggers = document.SelectNodes("/t:Task/t:Triggers/*", namespaces);
            Check("startup-task-single-logon-trigger",
                triggers.Count == 1
                && triggers[0].Name == "LogonTrigger"
                && Text(document, namespaces, "/t:Task/t:Triggers/t:LogonTrigger/t:UserId") == sid
                && Text(document, namespaces, "/t:Task/t:Triggers/t:LogonTrigger/t:Enabled") == "true",
                "exactly one enabled logon trigger for the current user SID");

            Check("startup-task-action",
                Text(document, namespaces, "/t:Task/t:Actions/t:Exec/t:Command") == executable
                && Text(document, namespaces, "/t:Task/t:Actions/t:Exec/t:Arguments") == "--startup"
                && Text(document, namespaces, "/t:Task/t:Actions/t:Exec/t:WorkingDirectory") == directory,
                "one exec action with the absolute product path, --startup and its parent directory");

            Check("startup-task-path-not-quoted",
                !Text(document, namespaces, "/t:Task/t:Actions/t:Exec/t:Command").StartsWith("\"", StringComparison.Ordinal),
                "Task Scheduler stores the command path without surrounding quotes");

            Check("startup-task-xml-escaping",
                xml.IndexOf("Tools &amp; Games", StringComparison.Ordinal) >= 0
                && xml.IndexOf("Tools & Games", StringComparison.Ordinal) < 0,
                "ampersands are escaped; the raw path is not injected as markup");

            Check("startup-task-settings",
                Text(document, namespaces, "/t:Task/t:Settings/t:MultipleInstancesPolicy") == "IgnoreNew"
                && Text(document, namespaces, "/t:Task/t:Settings/t:DisallowStartIfOnBatteries") == "false"
                && Text(document, namespaces, "/t:Task/t:Settings/t:StopIfGoingOnBatteries") == "false"
                && Text(document, namespaces, "/t:Task/t:Settings/t:ExecutionTimeLimit") == "PT0S",
                "ignores concurrent instances, runs on battery and has no finite time limit");

            Check("startup-task-no-password",
                document.GetElementsByTagName("Password").Count == 0,
                "no password element is ever written");

            Check("startup-task-invalid-input",
                Throws(delegate { new StartupTaskDefinition(sid, @"relative\app.exe", directory, "--startup"); })
                && Throws(delegate { new StartupTaskDefinition("not-a-sid", executable, directory, "--startup"); }),
                "relative executable paths and malformed SIDs are rejected");
        }

        private static void TestStartupTaskEvaluation()
        {
            const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
            const string directory = @"C:\Tools & Games\Windows Audio Keys";
            const string executable = directory + @"\WindowsAudioKeys.exe";
            StartupTaskDefinition definition = new StartupTaskDefinition(sid, executable, directory, "--startup");

            StartupTaskState matching = StartupTaskInspector.Evaluate(MatchingSnapshot(definition), definition);
            Check("startup-state-enabled",
                matching.Kind == StartupTaskStateKind.Enabled && matching.IsEnabled && !matching.IsError
                && matching.MessageKo.IndexOf("사용 중", StringComparison.Ordinal) >= 0,
                matching.MessageKo);

            StartupTaskSnapshot disabledSnapshot = MatchingSnapshot(definition);
            disabledSnapshot.TaskEnabled = false;
            StartupTaskState disabled = StartupTaskInspector.Evaluate(disabledSnapshot, definition);
            Check("startup-state-disabled",
                disabled.Kind == StartupTaskStateKind.Disabled && !disabled.IsEnabled && !disabled.IsError,
                disabled.MessageKo);
            Check("startup-state-disabled-checkbox",
                disabled.CheckboxKnown && !disabled.CheckboxChecked,
                "a disabled owned task is known and unchecked");

            StartupTaskState missing = StartupTaskInspector.Evaluate(StartupTaskSnapshot.Missing(), definition);
            Check("startup-state-missing",
                missing.Kind == StartupTaskStateKind.NotConfigured && !missing.IsEnabled && !missing.IsError,
                missing.MessageKo);

            StartupTaskState unreadable = StartupTaskInspector.Evaluate(
                StartupTaskSnapshot.Unreadable("액세스 거부"),
                definition);
            Check("startup-state-unreadable",
                unreadable.Kind == StartupTaskStateKind.Unreadable
                && unreadable.IsError
                && unreadable.MessageKo.IndexOf("읽지 못했습니다", StringComparison.Ordinal) >= 0
                && unreadable.MessageKo.IndexOf("꺼져 있다고 간주하지 않았습니다", StringComparison.Ordinal) >= 0,
                unreadable.MessageKo);
            Check("startup-state-unreadable-checkbox-unknown",
                !unreadable.CheckboxKnown && !unreadable.CheckboxChecked,
                "an unreadable task has no trustworthy checkbox value");

            StartupTaskSnapshot foreignDescription = MatchingSnapshot(definition);
            foreignDescription.Description = "somebody else's task";
            StartupTaskState foreign = StartupTaskInspector.Evaluate(foreignDescription, definition);
            Check("startup-state-foreign-description",
                foreign.Kind == StartupTaskStateKind.ForeignOwner && foreign.IsError,
                foreign.MessageKo);
            Check("startup-state-foreign-checkbox-unknown",
                !foreign.CheckboxKnown,
                "a foreign task is never shown as this app's trustworthy off state");

            StartupTaskSnapshot foreignPrincipal = MatchingSnapshot(definition);
            foreignPrincipal.PrincipalUserId = "S-1-5-21-999999999-999999999-999999999-1001";
            StartupTaskState foreignUser = StartupTaskInspector.Evaluate(foreignPrincipal, definition);
            Check("startup-state-foreign-principal",
                foreignUser.Kind == StartupTaskStateKind.ForeignOwner && foreignUser.IsError,
                foreignUser.MessageKo);

            StartupTaskSnapshot oldPath = MatchingSnapshot(definition);
            oldPath.Actions[0].Path = @"C:\Old Place\WindowsAudioKeys.exe";
            StartupTaskState refresh = StartupTaskInspector.Evaluate(oldPath, definition);
            Check("startup-state-needs-refresh",
                refresh.Kind == StartupTaskStateKind.NeedsRefresh
                && refresh.MessageKo.IndexOf("갱신", StringComparison.Ordinal) >= 0,
                refresh.MessageKo);
            Check("startup-state-needs-refresh-enabled-checkbox",
                refresh.CheckboxKnown && refresh.CheckboxChecked,
                "an enabled owned task at an old path stays checked while refresh is requested");

            StartupTaskSnapshot oldDirectory = MatchingSnapshot(definition);
            oldDirectory.Actions[0].WorkingDirectory = @"C:\Old Place";
            Check("startup-state-needs-refresh-workdir",
                StartupTaskInspector.Evaluate(oldDirectory, definition).Kind == StartupTaskStateKind.NeedsRefresh,
                "old working directory also requests a registration refresh");

            StartupTaskSnapshot wrongRunLevel = MatchingSnapshot(definition);
            wrongRunLevel.RunLevel = 0;
            Check("startup-state-runlevel-mismatch",
                StartupTaskInspector.Evaluate(wrongRunLevel, definition).Kind == StartupTaskStateKind.Mismatch,
                "limited run level is not a matching elevated startup");

            StartupTaskSnapshot wrongLogonType = MatchingSnapshot(definition);
            wrongLogonType.LogonType = 5;
            Check("startup-state-logon-type-mismatch",
                StartupTaskInspector.Evaluate(wrongLogonType, definition).Kind == StartupTaskStateKind.Mismatch,
                "non-interactive logon type is not a matching startup");

            StartupTaskSnapshot extraTrigger = MatchingSnapshot(definition);
            extraTrigger.Triggers.Add(new StartupTaskTriggerSnapshot
            {
                Type = 9,
                Enabled = true,
                UserId = sid
            });
            Check("startup-state-single-trigger",
                StartupTaskInspector.Evaluate(extraTrigger, definition).Kind == StartupTaskStateKind.Mismatch,
                "more than one trigger is not a matching definition");

            StartupTaskSnapshot allUsersTrigger = MatchingSnapshot(definition);
            allUsersTrigger.Triggers[0].UserId = null;
            Check("startup-state-trigger-user",
                StartupTaskInspector.Evaluate(allUsersTrigger, definition).Kind == StartupTaskStateKind.Mismatch,
                "a logon trigger without the current SID is not accepted");

            StartupTaskSnapshot wrongArguments = MatchingSnapshot(definition);
            wrongArguments.Actions[0].Arguments = null;
            Check("startup-state-arguments",
                StartupTaskInspector.Evaluate(wrongArguments, definition).Kind == StartupTaskStateKind.Mismatch,
                "missing --startup arguments are not accepted");
        }

        // Task Scheduler COM can return principal/trigger identities as account
        // names. This check uses a real in-memory ITaskDefinition (NewTask(0) with
        // the generated XML) for the current user, so the values read back are the
        // platform's real values. Nothing is registered or elevated.
        private static void TestStartupTaskIdentityRoundTrip()
        {
            string currentSid = null;
            string currentName = null;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.User != null)
                {
                    currentSid = identity.User.Value;
                }

                currentName = identity.Name;
            }

            Check("startup-identity-current-user-available",
                !string.IsNullOrEmpty(currentSid),
                "current Windows user SID is unavailable");

            if (string.IsNullOrEmpty(currentSid))
            {
                return;
            }

            string normalized;
            Check("startup-identity-accepts-sid",
                StartupIdentity.TryNormalize(currentSid, out normalized)
                && StartupSid.Same(normalized, currentSid),
                "a valid SID must normalize to itself: " + (normalized == null ? "(null)" : normalized));

            Check("startup-identity-translates-account-name",
                StartupIdentity.TryNormalize(currentName, out normalized)
                && StartupSid.Same(normalized, currentSid),
                currentName + " -> " + (normalized == null ? "(null)" : normalized));

            Check("startup-identity-rejects-unresolved",
                !StartupIdentity.TryNormalize(string.Empty, out normalized)
                && !StartupIdentity.TryNormalize(null, out normalized)
                && !StartupIdentity.TryNormalize("GameAudioKeys-NoSuchAccount-4C1F", out normalized)
                && normalized == null,
                "empty and unresolvable identities must not produce a SID");

            string principalError = null;
            try
            {
                StartupIdentity.RequirePrincipal(string.Empty);
            }
            catch (Exception ex)
            {
                principalError = ex.Message;
            }

            Check("startup-identity-missing-principal-fails-closed",
                principalError != null,
                "a missing principal identity must throw instead of matching the current user");
            Check("startup-identity-principal-error-readable",
                principalError != null
                && principalError.IndexOf("확인하지 못했습니다", StringComparison.Ordinal) >= 0,
                principalError == null ? "no error" : principalError);

            const string directory = @"C:\Tools & Games\Windows Audio Keys";
            StartupTaskDefinition definition = new StartupTaskDefinition(
                currentSid,
                directory + @"\WindowsAudioKeys.exe",
                directory,
                "--startup");

            StartupTaskState unreadable = StartupTaskInspector.Evaluate(
                StartupTaskSnapshot.Unreadable(principalError),
                definition);
            Check("startup-identity-principal-error-surfaced",
                unreadable.Kind == StartupTaskStateKind.Unreadable
                && !unreadable.CheckboxKnown
                && principalError != null
                && unreadable.MessageKo.IndexOf(principalError, StringComparison.Ordinal) >= 0,
                unreadable.MessageKo);

            string readError = null;
            StartupTaskSnapshot snapshot = null;
            try
            {
                snapshot = StartupTaskService.ReadInMemorySnapshot(definition);
            }
            catch (Exception ex)
            {
                readError = StartupTaskService.Describe(ex);
            }

            Check("startup-identity-roundtrip-readable",
                readError == null && snapshot != null && snapshot.ReadOk,
                readError ?? (snapshot == null ? "no snapshot" : snapshot.ReadErrorKo));

            if (snapshot == null || !snapshot.ReadOk)
            {
                return;
            }

            Check("startup-identity-roundtrip-principal",
                StartupSid.Same(snapshot.PrincipalUserId, currentSid),
                "principal read back as: " + (snapshot.PrincipalUserId == null ? "(null)" : snapshot.PrincipalUserId));

            Check("startup-identity-roundtrip-trigger",
                snapshot.Triggers.Count == 1
                && StartupSid.Same(snapshot.Triggers[0].UserId, currentSid),
                "logon trigger read back as: "
                    + (snapshot.Triggers.Count == 0 || snapshot.Triggers[0].UserId == null
                        ? "(null)"
                        : snapshot.Triggers[0].UserId));

            StartupTaskState owned = StartupTaskInspector.Evaluate(snapshot, definition);
            Check("startup-identity-roundtrip-owned",
                owned.Kind == StartupTaskStateKind.Enabled,
                owned.MessageKo);

            StartupTaskSnapshot emptyTrigger = MatchingSnapshot(definition);
            emptyTrigger.Triggers[0].UserId = StartupIdentity.NormalizeOptional(string.Empty);
            Check("startup-identity-empty-trigger-unmatched",
                emptyTrigger.Triggers[0].UserId == null
                && StartupTaskInspector.Evaluate(emptyTrigger, definition).Kind == StartupTaskStateKind.Mismatch,
                "an empty logon trigger identity must not match the current user");

            StartupTaskSnapshot unresolved = MatchingSnapshot(definition);
            unresolved.PrincipalUserId = StartupIdentity.NormalizeOptional("GameAudioKeys-NoSuchAccount-4C1F");
            Check("startup-identity-unresolved-principal-rejected",
                !StartupSid.Same(unresolved.PrincipalUserId, currentSid)
                && StartupTaskInspector.Evaluate(unresolved, definition).Kind == StartupTaskStateKind.ForeignOwner,
                "an unresolved principal identity is never treated as the owner");
        }

        private static void TestStartupApplyPlanner()
        {
            Check("startup-plan-enabled-enable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.Enabled), true).Action == StartupApplyAction.None,
                "already enabled does not register again");
            Check("startup-plan-enabled-disable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.Enabled), false).Action == StartupApplyAction.Delete,
                "disabling removes the owned task");
            Check("startup-plan-missing-enable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.NotConfigured), true).Action == StartupApplyAction.Register,
                "missing task plus enable registers");
            Check("startup-plan-missing-disable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.NotConfigured), false).Action == StartupApplyAction.None,
                "disable is idempotent when the task is absent");
            Check("startup-plan-disabled-enable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.Disabled), true).Action == StartupApplyAction.Register,
                "a disabled owned task is re-registered when enabled");
            Check("startup-plan-refresh-enable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.NeedsRefresh), true).Action == StartupApplyAction.Register,
                "an old-location owned task is refreshed");
            Check("startup-plan-foreign-enable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.ForeignOwner), true).Action == StartupApplyAction.Refuse,
                "a foreign task is never overwritten");
            Check("startup-plan-foreign-disable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.ForeignOwner), false).Action == StartupApplyAction.Refuse,
                "a foreign task is never deleted");
            Check("startup-plan-unreadable-disable",
                StartupApplyPlanner.Plan(State(StartupTaskStateKind.Unreadable), false).Action == StartupApplyAction.Refuse,
                "an unreadable task is not treated as successfully disabled");
            Check("startup-plan-null-state",
                StartupApplyPlanner.Plan(null, true).Action == StartupApplyAction.Refuse,
                "no state means no change");
        }

        // Non-mutating platform check: the generated XML must be accepted by a real
        // Task Scheduler ITaskDefinition created with NewTask(0). Nothing is registered.
        private static void TestStartupTaskPlatformXml()
        {
            const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
            const string directory = @"C:\Tools & Games\Windows Audio Keys";
            string error = null;
            try
            {
                StartupTaskDefinition definition = new StartupTaskDefinition(
                    sid,
                    directory + @"\WindowsAudioKeys.exe",
                    directory,
                    "--startup");
                StartupTaskService.VerifyTaskXmlAssignment(definition);
            }
            catch (Exception ex)
            {
                error = StartupTaskService.Describe(ex);
            }

            Check("startup-task-service-accepts-newtask-xml",
                error == null,
                error == null ? "accepted" : error);
        }

        private static StartupTaskState State(StartupTaskStateKind kind)
        {
            StartupTaskState state = new StartupTaskState();
            state.Kind = kind;
            state.MessageKo = kind.ToString();
            return state;
        }

        private static StartupTaskSnapshot MatchingSnapshot(StartupTaskDefinition definition)
        {
            StartupTaskSnapshot snapshot = new StartupTaskSnapshot();
            snapshot.ReadOk = true;
            snapshot.TaskExists = true;
            snapshot.TaskEnabled = true;
            snapshot.Description = definition.Description;
            snapshot.PrincipalUserId = definition.UserSid;
            snapshot.RunLevel = StartupConstants.RunLevelHighest;
            snapshot.LogonType = StartupConstants.LogonTypeInteractiveToken;
            snapshot.Triggers.Add(new StartupTaskTriggerSnapshot
            {
                Type = StartupConstants.TriggerTypeLogon,
                Enabled = true,
                UserId = definition.UserSid
            });
            snapshot.Actions.Add(new StartupTaskActionSnapshot
            {
                Type = StartupConstants.ActionTypeExec,
                Path = definition.ExecutablePath,
                Arguments = definition.Arguments,
                WorkingDirectory = definition.WorkingDirectory
            });
            return snapshot;
        }

        private static string Text(XmlDocument document, XmlNamespaceManager namespaces, string path)
        {
            XmlNode node = document.SelectSingleNode(path, namespaces);
            return node == null ? null : node.InnerText;
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void ExpectStartupError(string name, string[] args, string expectedText)
        {
            StartupCommandLine command = StartupCli.Parse(args);
            Check(name,
                command.Kind == StartupCommandKind.Error
                && command.ErrorKo != null
                && command.ErrorKo.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0,
                command.Kind == StartupCommandKind.Error ? command.ErrorKo : "expected an argument error");
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
