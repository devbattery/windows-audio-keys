// Windows Audio Keys - 관리자 권한 자동 시작을 허용할 설치 위치 검사.
// 로그인 시 관리자 권한으로 실행되는 작업이 일반 사용자가 수정할 수 있는 파일을 가리키면,
// 같은 사용자 권한의 다른 프로그램이 exe·config·DLL을 바꿔 다음 로그인 때 UAC 없이 관리자
// 권한을 얻을 수 있다. 그래서 Program Files 아래이면서 경로의 모든 폴더와 파일이
// 관리자 전용(소유자·쓰기 권한)인 경우에만 자동 시작 등록을 허용한다.
// 판단할 수 없으면(권한 읽기 실패 등) 안전하지 않은 것으로 본다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GameAudioKeys
{
    internal sealed class InstallLocationCheck
    {
        public bool Safe;
        public string ReasonKo;

        public static InstallLocationCheck Ok()
        {
            InstallLocationCheck check = new InstallLocationCheck();
            check.Safe = true;
            return check;
        }

        public static InstallLocationCheck Unsafe(string reasonKo)
        {
            InstallLocationCheck check = new InstallLocationCheck();
            check.Safe = false;
            check.ReasonKo = reasonKo;
            return check;
        }
    }

    // 한 항목(ACE)에서 판단에 필요한 값만 옮긴 것. 테스트에서 직접 만들 수 있다.
    internal sealed class AccessEntry
    {
        public string Sid;
        public bool Allow;
        public bool InheritOnly;
        public int Rights;

        public AccessEntry(string sid, bool allow, bool inheritOnly, int rights)
        {
            this.Sid = sid;
            this.Allow = allow;
            this.InheritOnly = inheritOnly;
            this.Rights = rights;
        }
    }

    // 파일·폴더 하나의 소유자와 접근 항목. 읽기에 실패하면 ErrorKo가 채워진다.
    internal sealed class SecuritySnapshot
    {
        public bool Exists;
        public bool IsReparsePoint;
        public string OwnerSid;
        public List<AccessEntry> Entries = new List<AccessEntry>();
        public string ErrorKo;
    }

    internal static class InstallLocation
    {
        public const string SystemSid = "S-1-5-18";
        public const string AdministratorsSid = "S-1-5-32-544";
        public const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        // 수정에 해당하는 권한: 파일 쓰기/폴더에 파일 추가, 추가 쓰기/하위 폴더 만들기,
        // 하위 항목 삭제, 삭제, 권한 변경, 소유권 가져오기, GENERIC_ALL, GENERIC_WRITE.
        public const int WriteLikeRights =
            0x00000002 | 0x00000004 | 0x00000040 | 0x00010000 | 0x00040000 | 0x00080000
            | 0x10000000 | 0x40000000;

        public static bool IsTrustedSid(string sid)
        {
            return string.Equals(sid, SystemSid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sid, AdministratorsSid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sid, TrustedInstallerSid, StringComparison.OrdinalIgnoreCase);
        }

        // 신뢰하지 않는 SID에 수정 권한을 주는 허용 항목이 있으면 true.
        // 상속 전용 항목은 이 객체 자체에는 적용되지 않으므로 건너뛴다(예: CREATOR OWNER).
        // 거부 항목은 고려하지 않는다(보수적으로 판단).
        public static bool GrantsWriteToUntrusted(IList<AccessEntry> entries, out string sid)
        {
            sid = null;
            if (entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                AccessEntry entry = entries[i];
                if (entry == null || !entry.Allow || entry.InheritOnly)
                {
                    continue;
                }

                if ((entry.Rights & WriteLikeRights) != 0 && !IsTrustedSid(entry.Sid))
                {
                    sid = entry.Sid;
                    return true;
                }
            }

            return false;
        }

        // path가 root 자체이거나 root 아래에 있는지(대소문자 무시, 경로 구분자 경계 확인).
        public static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            string normalizedRoot = root.TrimEnd('\\') + "\\";
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        // 실제 시스템 값으로 현재 실행 파일 위치를 검사한다.
        public static InstallLocationCheck CheckCurrent(string executablePath)
        {
            List<string> roots = new List<string>();
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            return Evaluate(executablePath, roots, ReadSecurity);
        }

        private static void AddRoot(List<string> roots, string root)
        {
            if (!string.IsNullOrEmpty(root) && !roots.Contains(root))
            {
                roots.Add(root);
            }
        }

        // 검사 순서: 위치(Program Files 아래) → exe·config 파일 → exe 폴더부터 루트까지 모든 폴더.
        public static InstallLocationCheck Evaluate(
            string executablePath,
            IList<string> trustedRoots,
            Func<string, bool, SecuritySnapshot> readSecurity)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return InstallLocationCheck.Unsafe("실행 파일 경로를 확인하지 못했습니다.");
            }

            // 작업 스케줄러는 실행 시점에 %변수%를 펼치고 상대 경로를 따로 해석한다. 여기서 같은
            // 결과를 보장할 수 없으므로 절대 경로가 아니거나 %가 들어 있으면 안전하지 않다고 본다.
            if (executablePath.IndexOf('%') >= 0 || !IsAbsoluteDrivePath(executablePath))
            {
                return InstallLocationCheck.Unsafe(
                    "실행 파일 경로가 절대 경로가 아니거나 환경 변수를 포함합니다(" + executablePath + ").");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(executablePath);
            }
            catch (Exception)
            {
                return InstallLocationCheck.Unsafe("실행 파일 경로가 올바르지 않습니다.");
            }

            string root = null;
            if (trustedRoots != null)
            {
                for (int i = 0; i < trustedRoots.Count; i++)
                {
                    if (IsUnderRoot(fullPath, trustedRoots[i]))
                    {
                        root = trustedRoots[i].TrimEnd('\\');
                        break;
                    }
                }
            }

            if (root == null)
            {
                return InstallLocationCheck.Unsafe(
                    "실행 파일이 Program Files 폴더 밖에 있습니다(" + fullPath + ").");
            }

            InstallLocationCheck fileCheck = CheckItem(fullPath, false, true, readSecurity);
            if (!fileCheck.Safe)
            {
                return fileCheck;
            }

            // 설정 파일(.config)은 없을 수도 있지만, 있으면 같은 기준을 적용한다.
            fileCheck = CheckItem(fullPath + ".config", false, false, readSecurity);
            if (!fileCheck.Safe)
            {
                return fileCheck;
            }

            // exe 폴더에서 Program Files 루트까지 모든 폴더. 폴더를 바꿔치기하거나 DLL을 심을 수 없어야 한다.
            string directory = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(directory))
            {
                InstallLocationCheck folderCheck = CheckItem(directory, true, true, readSecurity);
                if (!folderCheck.Safe)
                {
                    return folderCheck;
                }

                if (string.Equals(directory.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return InstallLocationCheck.Ok();
        }

        // "C:\..." 형태의 드라이브 절대 경로만 받는다("C:a.exe" 같은 드라이브 상대 경로 제외).
        private static bool IsAbsoluteDrivePath(string path)
        {
            return path.Length >= 3
                && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                && path[1] == ':'
                && path[2] == '\\';
        }

        private static InstallLocationCheck CheckItem(
            string path,
            bool isDirectory,
            bool required,
            Func<string, bool, SecuritySnapshot> readSecurity)
        {
            SecuritySnapshot snapshot;
            try
            {
                snapshot = readSecurity(path, isDirectory);
            }
            catch (Exception ex)
            {
                return InstallLocationCheck.Unsafe("권한 정보를 읽지 못했습니다(" + path + "): " + ex.Message);
            }

            if (snapshot == null)
            {
                return InstallLocationCheck.Unsafe("권한 정보를 읽지 못했습니다(" + path + ").");
            }

            if (!snapshot.Exists)
            {
                return required
                    ? InstallLocationCheck.Unsafe("파일 또는 폴더를 찾지 못했습니다(" + path + ").")
                    : InstallLocationCheck.Ok();
            }

            if (snapshot.ErrorKo != null)
            {
                return InstallLocationCheck.Unsafe("권한 정보를 읽지 못했습니다(" + path + "): " + snapshot.ErrorKo);
            }

            if (snapshot.IsReparsePoint)
            {
                return InstallLocationCheck.Unsafe("경로에 링크(정션·심볼릭 링크)가 있습니다(" + path + ").");
            }

            if (!IsTrustedSid(snapshot.OwnerSid))
            {
                return InstallLocationCheck.Unsafe(
                    "관리자가 아닌 계정이 소유한 항목이 있습니다(" + path + ").");
            }

            string writableSid;
            if (GrantsWriteToUntrusted(snapshot.Entries, out writableSid))
            {
                return InstallLocationCheck.Unsafe(
                    "일반 사용자가 수정할 수 있는 항목이 있습니다(" + path + ", " + DescribeSid(writableSid) + ").");
            }

            return InstallLocationCheck.Ok();
        }

        private static string DescribeSid(string sid)
        {
            try
            {
                return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            }
            catch (Exception)
            {
                return sid;
            }
        }

        // 실제 파일 시스템에서 소유자·접근 항목·링크 여부를 읽는다.
        private static SecuritySnapshot ReadSecurity(string path, bool isDirectory)
        {
            SecuritySnapshot snapshot = new SecuritySnapshot();
            snapshot.Exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            if (!snapshot.Exists)
            {
                return snapshot;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                snapshot.IsReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;

                AccessControlSections sections = AccessControlSections.Access | AccessControlSections.Owner;
                FileSystemSecurity security = isDirectory
                    ? (FileSystemSecurity)Directory.GetAccessControl(path, sections)
                    : File.GetAccessControl(path, sections);

                IdentityReference owner = security.GetOwner(typeof(SecurityIdentifier));
                snapshot.OwnerSid = owner == null ? null : owner.Value;

                AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    snapshot.Entries.Add(new AccessEntry(
                        rule.IdentityReference.Value,
                        rule.AccessControlType == AccessControlType.Allow,
                        (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0,
                        (int)rule.FileSystemRights));
                }
            }
            catch (Exception ex)
            {
                snapshot.ErrorKo = ex.Message;
            }

            return snapshot;
        }
    }
}
