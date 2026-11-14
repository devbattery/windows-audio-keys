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
            TestTargetPrefilter();
            TestSettingsEquivalence();
            TestKeyCapture();
            TestThemeResolution();
            TestStartupCommandLine();
            TestStartupLaunchPolicy();
            TestStartupTaskDefinition();
            TestStartupTaskEvaluation();
            TestStartupTaskIdentityRoundTrip();
            TestStartupApplyPlanner();
            TestStartupTaskPlatformXml();
            TestReleaseVersionParsing();
            TestReleaseVersionOrdering();
            TestLatestReleaseParsing();
            TestUpdateEvaluation();
            TestReleasePageUrl();
            TestUpdateCheckSetting(temporaryDirectory);
            TestInstallLocationRules();
            TestInstallLocationEvaluate();
            TestInstallLocationReal(temporaryDirectory);
            TestStartupLocationWarning();
            TestStartupTaskNotFound();
        }

        // 작업이 없을 때(처음 켜는 경우) 조회는 "미등록"이어야 한다. dynamic COM 호출에서는
        // 0x80070002가 FileNotFoundException으로 올라오므로 예외 타입이 아니라 HRESULT로 판정한다.
        private static void TestStartupTaskNotFound()
        {
            Check("task-notfound-file", StartupTaskService.IsNotFound(new FileNotFoundException("x")), "FileNotFoundException");
            Check("task-notfound-directory", StartupTaskService.IsNotFound(new DirectoryNotFoundException("x")), "DirectoryNotFoundException");
            Check("task-notfound-com",
                StartupTaskService.IsNotFound(new System.Runtime.InteropServices.COMException("x", unchecked((int)0x80070002))),
                "COMException 0x80070002");
            Check("task-notfound-wrapped",
                StartupTaskService.IsNotFound(new System.Reflection.TargetInvocationException(new FileNotFoundException("x"))),
                "wrapped");
            Check("task-notfound-access-denied",
                !StartupTaskService.IsNotFound(new UnauthorizedAccessException("x")),
                "access denied is not 'missing'");
            Check("task-notfound-other-com",
                !StartupTaskService.IsNotFound(new System.Runtime.InteropServices.COMException("x", unchecked((int)0x80070005))),
                "other HRESULT is not 'missing'");
            Check("task-notfound-null", !StartupTaskService.IsNotFound(null), "null");

            // 실제 작업 스케줄러: 존재하지 않는 작업 이름 조회(읽기 전용, 등록·삭제 없음).
            string missingName = "WindowsAudioKeys-test-missing-" + Guid.NewGuid().ToString("N");
            StartupTaskSnapshot snapshot = StartupTaskService.Query(missingName);
            Check("task-query-missing-platform",
                snapshot.ReadOk && !snapshot.TaskExists,
                snapshot.ReadErrorKo ?? "(no error)");
        }

        // 보안 경고는 지금 실행 중인 exe가 아니라 작업에 등록된 exe 경로로 판단해야 한다.
        private static void TestStartupLocationWarning()
        {
            const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
            const string safeDir = @"C:\Program Files\WindowsAudioKeys";
            const string unsafeDir = @"C:\Users\me\AppData\Local\Programs\WindowsAudioKeys";
            StartupTaskDefinition current = new StartupTaskDefinition(sid, safeDir + @"\WindowsAudioKeys.exe", safeDir, "--startup");
            StartupTaskDefinition old = new StartupTaskDefinition(sid, unsafeDir + @"\WindowsAudioKeys.exe", unsafeDir, "--startup");

            Func<string, InstallLocationCheck> fakeCheck = delegate(string path)
            {
                return path.StartsWith(@"C:\Program Files\", StringComparison.OrdinalIgnoreCase)
                    ? InstallLocationCheck.Ok()
                    : InstallLocationCheck.Unsafe("outside Program Files: " + path);
            };

            // 안전한 위치의 새 복사본을 실행 중이지만, 남은 작업은 예전(수정 가능) 위치를 실행한다.
            StartupTaskState stale = StartupTaskInspector.Evaluate(MatchingSnapshot(old), current);
            Check("location-warning-registered-path",
                stale.Kind == StartupTaskStateKind.NeedsRefresh
                && string.Equals(stale.RegisteredExecutablePath, unsafeDir + @"\WindowsAudioKeys.exe", StringComparison.OrdinalIgnoreCase),
                stale.RegisteredExecutablePath ?? "(null)");
            StartupTaskHost.ApplyLocationWarning(stale, fakeCheck);
            Check("location-warning-stale-task-flagged",
                stale.LocationWarningKo != null && stale.IsError && stale.MessageKo.StartsWith("보안 경고", StringComparison.Ordinal),
                stale.MessageKo);

            // 반대로, 위험한 위치의 복사본을 실행 중이어도 작업이 안전한 위치를 가리키면 경고하지 않는다.
            StartupTaskState safeTask = StartupTaskInspector.Evaluate(MatchingSnapshot(current), old);
            StartupTaskHost.ApplyLocationWarning(safeTask, fakeCheck);
            Check("location-warning-safe-task-quiet", safeTask.LocationWarningKo == null, safeTask.MessageKo);

            // 일치하고 켜진 작업이 위험한 위치면 경고한다.
            StartupTaskState enabledUnsafe = StartupTaskInspector.Evaluate(MatchingSnapshot(old), old);
            StartupTaskHost.ApplyLocationWarning(enabledUnsafe, fakeCheck);
            Check("location-warning-enabled-unsafe",
                enabledUnsafe.Kind == StartupTaskStateKind.Enabled && enabledUnsafe.LocationWarningKo != null,
                enabledUnsafe.MessageKo);

            // 꺼진 작업은 실행되지 않으므로 경고하지 않는다.
            StartupTaskSnapshot disabledSnapshot = MatchingSnapshot(old);
            disabledSnapshot.TaskEnabled = false;
            StartupTaskState disabled = StartupTaskInspector.Evaluate(disabledSnapshot, old);
            StartupTaskHost.ApplyLocationWarning(disabled, fakeCheck);
            Check("location-warning-disabled-quiet", disabled.LocationWarningKo == null, disabled.MessageKo);

            // 검사 함수가 예외를 던지면 안전하지 않은 것으로 본다.
            StartupTaskState throwing = StartupTaskInspector.Evaluate(MatchingSnapshot(current), current);
            StartupTaskHost.ApplyLocationWarning(throwing, delegate(string p) { throw new IOException("x"); });
            Check("location-warning-check-throws", throwing.LocationWarningKo != null, "fail closed");
        }

        private const string UserSid = "S-1-5-21-1000-2000-3000-1001";
        private const string UsersSid = "S-1-5-32-545";
        private const string AuthenticatedUsersSid = "S-1-5-11";
        private const string CreatorOwnerSid = "S-1-3-0";

        // Program Files 기본 권한과 비슷한 항목: 관리자·시스템 전체, 사용자 읽기/실행,
        // CREATOR OWNER는 상속 전용.
        private static List<AccessEntry> ProtectedEntries()
        {
            List<AccessEntry> entries = new List<AccessEntry>();
            entries.Add(new AccessEntry(InstallLocation.TrustedInstallerSid, true, false, 0x1F01FF));
            entries.Add(new AccessEntry(InstallLocation.SystemSid, true, false, 0x1301BF));
            entries.Add(new AccessEntry(InstallLocation.AdministratorsSid, true, false, 0x1301BF));
            entries.Add(new AccessEntry(UsersSid, true, false, 0x1200A9));
            entries.Add(new AccessEntry(UsersSid, true, true, unchecked((int)0xA0000000)));
            entries.Add(new AccessEntry(CreatorOwnerSid, true, true, 0x10000000));
            return entries;
        }

        private static void TestInstallLocationRules()
        {
            string sid;
            Check("install-acl-protected", !InstallLocation.GrantsWriteToUntrusted(ProtectedEntries(), out sid), sid);

            List<AccessEntry> usersModify = ProtectedEntries();
            usersModify.Add(new AccessEntry(UsersSid, true, false, 0x1301BF));
            Check("install-acl-users-modify", InstallLocation.GrantsWriteToUntrusted(usersModify, out sid) && sid == UsersSid, sid);

            List<AccessEntry> userAppend = ProtectedEntries();
            userAppend.Add(new AccessEntry(UserSid, true, false, 0x4));
            Check("install-acl-user-append", InstallLocation.GrantsWriteToUntrusted(userAppend, out sid), "create subfolder");

            List<AccessEntry> authDelete = ProtectedEntries();
            authDelete.Add(new AccessEntry(AuthenticatedUsersSid, true, false, 0x10000));
            Check("install-acl-delete", InstallLocation.GrantsWriteToUntrusted(authDelete, out sid), "delete right");

            List<AccessEntry> genericWrite = ProtectedEntries();
            genericWrite.Add(new AccessEntry(UserSid, true, false, 0x40000000));
            Check("install-acl-generic-write", InstallLocation.GrantsWriteToUntrusted(genericWrite, out sid), "GENERIC_WRITE");

            List<AccessEntry> denyOnly = ProtectedEntries();
            denyOnly.Add(new AccessEntry(UsersSid, false, false, 0x1F01FF));
            Check("install-acl-deny-ignored", !InstallLocation.GrantsWriteToUntrusted(denyOnly, out sid), "deny ACE");

            Check("install-root-under", InstallLocation.IsUnderRoot(@"C:\Program Files\App\a.exe", @"C:\Program Files"), "child");
            Check("install-root-case", InstallLocation.IsUnderRoot(@"c:\program files\App\a.exe", @"C:\Program Files\"), "case/trailing");
            Check("install-root-prefix-trap", !InstallLocation.IsUnderRoot(@"C:\Program Files Evil\a.exe", @"C:\Program Files"), "sibling prefix");
            Check("install-root-null", !InstallLocation.IsUnderRoot(null, @"C:\Program Files"), "null");
        }

        // 가짜 파일 시스템: 경로별 보안 정보. 없는 경로는 존재하지 않는 것으로 본다.
        private static Func<string, bool, SecuritySnapshot> FakeSecurity(Dictionary<string, SecuritySnapshot> items)
        {
            return delegate(string path, bool isDirectory)
            {
                SecuritySnapshot snapshot;
                if (items.TryGetValue(path, out snapshot))
                {
                    return snapshot;
                }

                SecuritySnapshot missing = new SecuritySnapshot();
                missing.Exists = false;
                return missing;
            };
        }

        private static SecuritySnapshot Item(string owner, List<AccessEntry> entries)
        {
            SecuritySnapshot snapshot = new SecuritySnapshot();
            snapshot.Exists = true;
            snapshot.OwnerSid = owner;
            snapshot.Entries = entries;
            return snapshot;
        }

        private static Dictionary<string, SecuritySnapshot> SafeTree()
        {
            Dictionary<string, SecuritySnapshot> items = new Dictionary<string, SecuritySnapshot>(StringComparer.OrdinalIgnoreCase);
            items[@"C:\Program Files"] = Item(InstallLocation.TrustedInstallerSid, ProtectedEntries());
            items[@"C:\Program Files\Vendor"] = Item(InstallLocation.AdministratorsSid, ProtectedEntries());
            items[@"C:\Program Files\Vendor\WAK"] = Item(InstallLocation.AdministratorsSid, ProtectedEntries());
            items[@"C:\Program Files\Vendor\WAK\WindowsAudioKeys.exe"] = Item(InstallLocation.AdministratorsSid, ProtectedEntries());
            items[@"C:\Program Files\Vendor\WAK\WindowsAudioKeys.exe.config"] = Item(InstallLocation.AdministratorsSid, ProtectedEntries());
            return items;
        }

        private static void TestInstallLocationEvaluate()
        {
            string exe = @"C:\Program Files\Vendor\WAK\WindowsAudioKeys.exe";
            List<string> roots = new List<string>();
            roots.Add(@"C:\Program Files");
            roots.Add(@"C:\Program Files (x86)");

            InstallLocationCheck check = InstallLocation.Evaluate(exe, roots, FakeSecurity(SafeTree()));
            Check("install-eval-safe", check.Safe, check.ReasonKo);

            Dictionary<string, SecuritySnapshot> noConfig = SafeTree();
            noConfig.Remove(exe + ".config");
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(noConfig));
            Check("install-eval-missing-config-ok", check.Safe, check.ReasonKo);

            check = InstallLocation.Evaluate(@"C:\Users\me\AppData\Local\Programs\WAK\WindowsAudioKeys.exe", roots, FakeSecurity(SafeTree()));
            Check("install-eval-outside-root", !check.Safe && check.ReasonKo.IndexOf("Program Files", StringComparison.Ordinal) >= 0, check.ReasonKo);

            Dictionary<string, SecuritySnapshot> userOwnedExe = SafeTree();
            userOwnedExe[exe] = Item(UserSid, ProtectedEntries());
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(userOwnedExe));
            Check("install-eval-user-owned-exe", !check.Safe, "owner can rewrite the DACL");

            Dictionary<string, SecuritySnapshot> writableConfig = SafeTree();
            List<AccessEntry> configEntries = ProtectedEntries();
            configEntries.Add(new AccessEntry(UsersSid, true, false, 0x2));
            writableConfig[exe + ".config"] = Item(InstallLocation.AdministratorsSid, configEntries);
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(writableConfig));
            Check("install-eval-writable-config", !check.Safe, "config injection");

            Dictionary<string, SecuritySnapshot> writableFolder = SafeTree();
            List<AccessEntry> folderEntries = ProtectedEntries();
            folderEntries.Add(new AccessEntry(AuthenticatedUsersSid, true, false, 0x1301BF));
            writableFolder[@"C:\Program Files\Vendor\WAK"] = Item(InstallLocation.AdministratorsSid, folderEntries);
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(writableFolder));
            Check("install-eval-writable-folder", !check.Safe, "DLL planting");

            Dictionary<string, SecuritySnapshot> writableAncestor = SafeTree();
            writableAncestor[@"C:\Program Files\Vendor"] = Item(UserSid, ProtectedEntries());
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(writableAncestor));
            Check("install-eval-user-owned-ancestor", !check.Safe, "folder swap");

            Dictionary<string, SecuritySnapshot> junction = SafeTree();
            junction[@"C:\Program Files\Vendor\WAK"].IsReparsePoint = true;
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(junction));
            Check("install-eval-reparse-point", !check.Safe, "junction");

            Dictionary<string, SecuritySnapshot> unreadable = SafeTree();
            unreadable[exe].ErrorKo = "access denied";
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(unreadable));
            Check("install-eval-unreadable", !check.Safe, "fail closed");

            Dictionary<string, SecuritySnapshot> missingExe = SafeTree();
            missingExe.Remove(exe);
            check = InstallLocation.Evaluate(exe, roots, FakeSecurity(missingExe));
            Check("install-eval-missing-exe", !check.Safe, "exe must exist");

            check = InstallLocation.Evaluate(exe, roots, delegate(string p, bool d) { throw new UnauthorizedAccessException("x"); });
            Check("install-eval-reader-throws", !check.Safe, "fail closed on exception");

            check = InstallLocation.Evaluate(null, roots, FakeSecurity(SafeTree()));
            Check("install-eval-null", !check.Safe, "null path");

            string[] unresolved =
            {
                "WindowsAudioKeys.exe",
                @"Vendor\WAK\WindowsAudioKeys.exe",
                @"C:Program Files\Vendor\WAK\WindowsAudioKeys.exe",
                @"%ProgramFiles%\Vendor\WAK\WindowsAudioKeys.exe",
                @"C:\Program Files\%X%\WAK\WindowsAudioKeys.exe",
                @"\\server\share\WindowsAudioKeys.exe"
            };
            for (int i = 0; i < unresolved.Length; i++)
            {
                check = InstallLocation.Evaluate(unresolved[i], roots, FakeSecurity(SafeTree()));
                Check("install-eval-unresolved-" + i.ToString(CultureInfo.InvariantCulture), !check.Safe, unresolved[i]);
            }
        }

        // 실제 파일 시스템: 임시 폴더(사용자 소유)의 실행 파일은 항상 거부된다.
        private static void TestInstallLocationReal(string temporaryDirectory)
        {
            string exe = Path.Combine(temporaryDirectory, "install-real", "WindowsAudioKeys.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "x");
            InstallLocationCheck check = InstallLocation.CheckCurrent(exe);
            Check("install-real-temp-refused", !check.Safe && !string.IsNullOrEmpty(check.ReasonKo), check.ReasonKo);
        }

        // 새 버전 알림: 태그 형식은 "v주.부.수정" 또는 "주.부.수정"만 허용한다.
        private static void TestReleaseVersionParsing()
        {
            string[] accepted = { "1.2.3", "v1.2.3", "0.0.0", "v99999.0.1", "v10.20.30" };
            foreach (string text in accepted)
            {
                ReleaseVersion version;
                Check("release-version-accept " + text, ReleaseVersion.TryParse(text, out version) && version != null, text);
            }

            string[] rejected =
            {
                null, "", " 1.2.3", "1.2.3 ", "V1.2.3", "vv1.2.3", "1.2", "1.2.3.4", "v01.2.3", "1.02.3",
                "1.2.03", "1.2.3-beta", "100000.0.0", "a.b.c", "1..3", "v", "-1.2.3", "1.2.3\n"
            };
            foreach (string text in rejected)
            {
                ReleaseVersion version;
                Check("release-version-reject " + (text == null ? "(null)" : text.Replace("\n", "\\n")),
                    !ReleaseVersion.TryParse(text, out version) && version == null,
                    "must reject");
            }

            ReleaseVersion parsed;
            ReleaseVersion.TryParse("v12.0.7", out parsed);
            Check("release-version-tostring", parsed != null && parsed.ToString() == "12.0.7", "canonical form without v");
        }

        private static void TestReleaseVersionOrdering()
        {
            Check("release-version-order-patch", Ver("1.2.10").CompareTo(Ver("1.2.9")) > 0, "numeric, not lexical");
            Check("release-version-order-major", Ver("2.0.0").CompareTo(Ver("1.99.99")) > 0, "major first");
            Check("release-version-order-minor", Ver("1.3.0").CompareTo(Ver("1.2.99")) > 0, "minor before patch");
            Check("release-version-equal", Ver("v1.0.0").CompareTo(Ver("1.0.0")) == 0, "prefix ignored");
            Check("release-version-null", Ver("1.0.0").CompareTo(null) > 0, "null sorts first");
        }

        private static ReleaseVersion Ver(string text)
        {
            ReleaseVersion version;
            if (!ReleaseVersion.TryParse(text, out version))
            {
                throw new ArgumentException("test version must parse: " + text);
            }

            return version;
        }

        private static void TestLatestReleaseParsing()
        {
            LatestRelease release;
            string error;

            bool ok = UpdateCheckLogic.TryParseLatestRelease(
                "{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://evil.example/\",\"assets\":[{\"name\":\"x\"}]}",
                out release,
                out error);
            Check("update-json-valid", ok && release.TagName == "v1.1.0" && !release.Draft && !release.Prerelease, error);

            ok = UpdateCheckLogic.TryParseLatestRelease("{\"tag_name\":\"v1.1.0\"}", out release, out error);
            Check("update-json-optional-flags", ok && !release.Draft && !release.Prerelease, error);

            ok = UpdateCheckLogic.TryParseLatestRelease("{\"tag_name\":\"v1.1.0\",\"draft\":true}", out release, out error);
            Check("update-json-draft-read", ok && release.Draft, error);

            string[] invalid =
            {
                null, "", "{", "[]", "\"v1.1.0\"", "{\"name\":\"v1.1.0\"}", "{\"tag_name\":110}",
                "{\"tag_name\":null}", "{\"tag_name\":\"v1.1.0\",\"draft\":\"no\"}",
                "{\"tag_name\":\"v1.1.0\",\"prerelease\":1}"
            };
            for (int i = 0; i < invalid.Length; i++)
            {
                ok = UpdateCheckLogic.TryParseLatestRelease(invalid[i], out release, out error);
                Check("update-json-invalid-" + i.ToString(CultureInfo.InvariantCulture),
                    !ok && release == null && !string.IsNullOrEmpty(error),
                    invalid[i] ?? "(null)");
            }
        }

        private static void TestUpdateEvaluation()
        {
            ReleaseVersion current = Ver("1.0.0");
            ReleaseVersion latest;

            Check("update-eval-available",
                UpdateCheckLogic.Evaluate(current, Release("v1.1.0", false, false), out latest) == UpdateStatus.UpdateAvailable
                && latest.ToString() == "1.1.0",
                "newer tag is an update");
            Check("update-eval-uptodate",
                UpdateCheckLogic.Evaluate(current, Release("v1.0.0", false, false), out latest) == UpdateStatus.UpToDate,
                "same version");
            Check("update-eval-local-newer",
                UpdateCheckLogic.Evaluate(Ver("2.0.0"), Release("v1.9.9", false, false), out latest) == UpdateStatus.LocalNewer,
                "local build ahead of the release");
            Check("update-eval-draft",
                UpdateCheckLogic.Evaluate(current, Release("v9.0.0", true, false), out latest) == UpdateStatus.Invalid && latest == null,
                "drafts are ignored");
            Check("update-eval-prerelease",
                UpdateCheckLogic.Evaluate(current, Release("v9.0.0", false, true), out latest) == UpdateStatus.Invalid,
                "prereleases are ignored");
            Check("update-eval-bad-tag",
                UpdateCheckLogic.Evaluate(current, Release("latest", false, false), out latest) == UpdateStatus.Invalid,
                "unparsable tag");
            Check("update-eval-null-current",
                UpdateCheckLogic.Evaluate(null, Release("v1.1.0", false, false), out latest) == UpdateStatus.Invalid,
                "unknown current version");

            UpdateCheckResult result = UpdateCheckLogic.BuildResult(current, Release("v1.2.0", false, false));
            Check("update-result-available",
                result.Success && result.Status == UpdateStatus.UpdateAvailable && result.Latest.ToString() == "1.2.0",
                result.MessageKo);
            result = UpdateCheckLogic.BuildResult(current, Release("garbage", false, false));
            Check("update-result-invalid-failure",
                !result.Success && !string.IsNullOrEmpty(result.MessageKo),
                "invalid release info must be reported as a failure");
            Check("update-current-version",
                UpdateCheckLogic.CurrentVersion("1.0.0") != null && UpdateCheckLogic.CurrentVersion("0.0.0.0") == null,
                "product version must be major.minor.patch");
        }

        private static LatestRelease Release(string tag, bool draft, bool prerelease)
        {
            LatestRelease release = new LatestRelease();
            release.TagName = tag;
            release.Draft = draft;
            release.Prerelease = prerelease;
            return release;
        }

        // 앱이 여는 주소는 저장소 릴리스 태그 페이지 하나뿐이다.
        private static void TestReleasePageUrl()
        {
            string url = UpdateCheckLogic.ReleasePageUrl(Ver("1.2.3"));
            Check("release-url-format",
                url == "https://github.com/devbattery/windows-audio-keys/releases/tag/v1.2.3",
                url);
            Check("release-url-allowed", UpdateCheckLogic.IsAllowedReleaseUrl(url), url);

            string[] blocked =
            {
                null,
                "",
                "https://evil.example/",
                "https://github.com/devbattery/windows-audio-keys-evil/releases/tag/v1.2.3",
                "https://github.com/devbattery/windows-audio-keys/releases/tag/v1.2.3 --flag",
                "https://github.com/devbattery/windows-audio-keys/releases/tag/v1.2.3\"",
                "https://github.com/devbattery/windows-audio-keys/releases/tag/v1.2.3/../../x",
                "https://github.com/devbattery/windows-audio-keys/releases/tag/1.2.3",
                "https://github.com/devbattery/windows-audio-keys/releases/tag/v01.2.3",
                "https://github.com/devbattery/windows-audio-keys/releases/latest",
                "http://github.com/devbattery/windows-audio-keys/releases/tag/v1.2.3",
                "HTTPS://GITHUB.COM/devbattery/windows-audio-keys/releases/tag/v1.2.3"
            };
            for (int i = 0; i < blocked.Length; i++)
            {
                Check("release-url-blocked-" + i.ToString(CultureInfo.InvariantCulture),
                    !UpdateCheckLogic.IsAllowedReleaseUrl(blocked[i]),
                    blocked[i] ?? "(null)");
            }
        }

        // 자동 확인 설정: 기존 파일(키 없음)은 켜짐, 저장 왕복, 변경 감지·복제에 반영.
        private static void TestUpdateCheckSetting(string temporaryDirectory)
        {
            string path = Path.Combine(temporaryDirectory, "update-setting", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string legacyJson =
                "{\"version\":1,"
                + "\"mute\":{\"key\":46,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeUp\":{\"key\":33,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"volumeDown\":{\"key\":34,\"ctrl\":false,\"alt\":false,\"shift\":false},"
                + "\"step\":5,\"enabled\":true}";
            File.WriteAllText(path, legacyJson, new UTF8Encoding(false));

            SettingsLoadResult legacy = SettingsStore.Load(path);
            Check("update-setting-legacy-default-on",
                !legacy.Corrupt && legacy.Settings.CheckForUpdates,
                legacy.StatusKo);

            AppSettings off = legacy.Settings.Clone();
            off.CheckForUpdates = false;
            string error;
            Check("update-setting-save", SettingsStore.TrySave(path, off, out error), error);
            SettingsLoadResult reloaded = SettingsStore.Load(path);
            Check("update-setting-roundtrip-off",
                !reloaded.Corrupt && !reloaded.Settings.CheckForUpdates,
                reloaded.StatusKo);
            Check("update-setting-file-key",
                File.ReadAllText(path, Encoding.UTF8).IndexOf("\"checkForUpdates\":false", StringComparison.Ordinal) >= 0,
                "the key is written explicitly");

            Check("update-setting-defaults-on", AppSettings.CreateDefaults().CheckForUpdates, "default is on");
            Check("update-setting-clone", !off.Clone().CheckForUpdates, "clone copies the flag");
            Check("update-setting-equivalence",
                !AppSettings.AreEquivalent(off, legacy.Settings)
                && AppSettings.AreEquivalent(off, off.Clone()),
                "only the update flag differs");
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

        // 스냅샷 지연 생성 판단: 같은 실행 파일 경로의 다른 PID 세션이 있을 때만 true
        private static void TestTargetPrefilter()
        {
            Dictionary<int, string> paths = new Dictionary<int, string>();
            paths[100] = @"C:\Game\game.exe";
            paths[200] = @"C:\GAME\GAME.EXE";
            paths[300] = @"C:\Chat\chat.exe";
            List<int> lookups = new List<int>();
            Func<int, string> pathOf = delegate(int pid)
            {
                lookups.Add(pid);
                string path;
                return paths.TryGetValue(pid, out path) ? path : null;
            };

            List<AudioSession> selfOnly = new List<AudioSession>();
            selfOnly.Add(NewSession(100));
            Check("prefilter-self-only",
                !TargetPrefilter.NeedsAncestry(100, selfOnly, pathOf) && lookups.Count == 0,
                "foreground PID sessions never need a snapshot or path lookup");

            List<AudioSession> other = new List<AudioSession>();
            other.Add(NewSession(100));
            other.Add(NewSession(300));
            Check("prefilter-different-executable",
                !TargetPrefilter.NeedsAncestry(100, other, pathOf),
                "an unrelated executable cannot be a target");

            List<AudioSession> sameImage = new List<AudioSession>();
            sameImage.Add(NewSession(300));
            sameImage.Add(NewSession(200));
            Check("prefilter-same-executable",
                TargetPrefilter.NeedsAncestry(100, sameImage, pathOf),
                "same image path (case-insensitive) requires ancestry verification");

            List<AudioSession> unknownPath = new List<AudioSession>();
            unknownPath.Add(NewSession(999));
            Check("prefilter-unknown-session-path",
                !TargetPrefilter.NeedsAncestry(100, unknownPath, pathOf),
                "an unreadable session path fails SameExecutablePath anyway");

            Check("prefilter-unknown-foreground-path",
                !TargetPrefilter.NeedsAncestry(555, sameImage, pathOf),
                "an unreadable foreground path means no descendant can match");

            AudioSession system = NewSession(200);
            system.SystemSounds = true;
            AudioSession cross = NewSession(200);
            cross.CrossProcess = true;
            AudioSession expired = NewSession(200);
            expired.State = 2;
            AudioSession kernel = NewSession(4);
            List<AudioSession> excluded = new List<AudioSession>();
            excluded.Add(system);
            excluded.Add(cross);
            excluded.Add(expired);
            excluded.Add(kernel);
            Check("prefilter-excluded-sessions",
                !TargetPrefilter.NeedsAncestry(100, excluded, pathOf),
                "system/cross-process/expired/System PID sessions are never candidates");

            Check("prefilter-null-input",
                !TargetPrefilter.NeedsAncestry(100, null, pathOf)
                && !TargetPrefilter.NeedsAncestry(0, sameImage, pathOf)
                && !TargetPrefilter.NeedsAncestry(100, sameImage, null),
                "null or invalid input never requests a snapshot");

            // 판단 생략 시 결과 동등성: 빈 표에서도 활성 PID 세션만 선택된다.
            List<AudioSession> mixed = new List<AudioSession>();
            AudioSession direct = NewSession(100);
            mixed.Add(direct);
            mixed.Add(NewSession(300));
            List<AudioSession> selected = Targeting.SelectSessions(new ProcTable(), 100, mixed);
            Check("prefilter-empty-table-selects-self",
                selected.Count == 1 && selected[0] == direct,
                "without a snapshot only the foreground PID session is selected");
        }

        // 설정 창의 "저장하지 않은 변경 사항" 판단
        private static void TestSettingsEquivalence()
        {
            AppSettings a = AppSettings.CreateDefaults();
            AppSettings b = a.Clone();
            Check("settings-equivalent-clone", AppSettings.AreEquivalent(a, b), "clone is equivalent");

            b.Step = 6;
            Check("settings-differs-step", !AppSettings.AreEquivalent(a, b), "step change is detected");

            b = a.Clone();
            b.Enabled = false;
            Check("settings-differs-enabled", !AppSettings.AreEquivalent(a, b), "enabled change is detected");

            b = a.Clone();
            b.VolumeUp.Ctrl = true;
            Check("settings-differs-modifier", !AppSettings.AreEquivalent(a, b), "modifier change is detected");

            b = a.Clone();
            b.Mute = null;
            Check("settings-differs-missing-binding",
                !AppSettings.AreEquivalent(a, b) && !AppSettings.AreEquivalent(b, a),
                "missing binding differs in both directions");

            Check("settings-null-handling",
                AppSettings.AreEquivalent(null, null) && !AppSettings.AreEquivalent(a, null),
                "null settings compare safely");
        }

        // 키 녹화: 키 입력 → 조합 완성/대기/거부 판정
        private static void TestKeyCapture()
        {
            KeyCaptureResult delete = KeyCapture.Evaluate(System.Windows.Forms.Keys.Delete);
            Check("capture-bare-delete",
                delete.Kind == KeyCaptureKind.Complete && delete.Binding.Key == 0x2E
                && !delete.Binding.Ctrl && !delete.Binding.Alt && !delete.Binding.Shift,
                "bare Delete completes");

            KeyCaptureResult combo = KeyCapture.Evaluate(
                System.Windows.Forms.Keys.PageDown | System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift);
            Check("capture-ctrl-shift-pagedown",
                combo.Kind == KeyCaptureKind.Complete && combo.Binding.Key == 0x22
                && combo.Binding.Ctrl && combo.Binding.Shift && !combo.Binding.Alt,
                "Ctrl+Shift+Page Down completes with modifiers");

            KeyCaptureResult modifierOnly = KeyCapture.Evaluate(
                System.Windows.Forms.Keys.ControlKey | System.Windows.Forms.Keys.Control);
            Check("capture-modifier-pending",
                modifierOnly.Kind == KeyCaptureKind.Pending && modifierOnly.Binding == null,
                "a modifier alone keeps waiting");

            KeyCaptureResult f12 = KeyCapture.Evaluate(System.Windows.Forms.Keys.F12);
            Check("capture-f12-rejected",
                f12.Kind == KeyCaptureKind.Rejected && f12.MessageKo != null && f12.MessageKo.Contains("F12"),
                "F12 is rejected with the reserved-key reason");

            KeyCaptureResult tab = KeyCapture.Evaluate(System.Windows.Forms.Keys.Tab);
            Check("capture-unsupported-rejected",
                tab.Kind == KeyCaptureKind.Rejected && tab.Binding == null && !string.IsNullOrEmpty(tab.MessageKo),
                "unsupported keys are rejected");

            KeyCaptureResult shiftEscape = KeyCapture.Evaluate(System.Windows.Forms.Keys.Escape | System.Windows.Forms.Keys.Shift);
            Check("capture-unsupported-friendly-message",
                shiftEscape.Kind == KeyCaptureKind.Rejected && !shiftEscape.MessageKo.Contains("VK"),
                "unsupported keys never show raw virtual-key numbers");

            KeyCaptureResult altTab = KeyCapture.Evaluate(System.Windows.Forms.Keys.Tab | System.Windows.Forms.Keys.Alt);
            Check("capture-alt-tab-reserved",
                altTab.Kind == KeyCaptureKind.Rejected && altTab.MessageKo.Contains("Alt+Tab"),
                "Alt+Tab keeps the specific reserved reason");

            KeyCaptureResult windows = KeyCapture.Evaluate(System.Windows.Forms.Keys.LWin);
            Check("capture-windows-key-rejected",
                windows.Kind == KeyCaptureKind.Rejected && windows.MessageKo.Contains("Windows"),
                "Windows key combinations are rejected");

            KeyCaptureResult cad = KeyCapture.Evaluate(
                System.Windows.Forms.Keys.Delete | System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt);
            Check("capture-ctrl-alt-delete-rejected",
                cad.Kind == KeyCaptureKind.Rejected && cad.MessageKo.Contains("Ctrl+Alt+Delete"),
                "Ctrl+Alt+Delete is reported as reserved");

            KeyCaptureResult altF4 = KeyCapture.Evaluate(System.Windows.Forms.Keys.F4 | System.Windows.Forms.Keys.Alt);
            Check("capture-alt-f4-rejected",
                altF4.Kind == KeyCaptureKind.Rejected && altF4.MessageKo.Contains("Alt+F4"),
                "Alt+F4 is reported as reserved");

            Check("capture-pending-text",
                KeyCapture.DescribePending(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt) == "Ctrl + Alt + …"
                && KeyCapture.DescribePending(System.Windows.Forms.Keys.None) == null,
                "pending modifier text");
        }

        // Windows 앱 테마 레지스트리 값 해석
        private static void TestThemeResolution()
        {
            Check("theme-dark-when-zero", ThemeManager.ResolveDark(0), "AppsUseLightTheme=0 means dark");
            Check("theme-light-when-one", !ThemeManager.ResolveDark(1), "AppsUseLightTheme=1 means light");
            Check("theme-light-when-missing", !ThemeManager.ResolveDark(null), "missing value falls back to light");
            Check("theme-light-when-malformed", !ThemeManager.ResolveDark("0"), "non-DWORD value falls back to light");
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
