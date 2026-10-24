// Windows Audio Keys - Task Scheduler COM adapter and the narrow elevated helper
// mode. Registration is only ever performed by this process (or by this same
// executable launched with a UAC consent prompt); no argument can select another
// executable, task name or XML.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace GameAudioKeys
{
    // Task Scheduler's COM API may return a principal/trigger identity as an
    // account name (for example after NewTask transforms the current user's SID),
    // while the inspector compares numeric SIDs. Both forms are normalized to
    // SecurityIdentifier.Value here. Anything that cannot be resolved stays
    // unresolved so it can never equal the current user's SID by accident.
    internal static class StartupIdentity
    {
        public static bool TryNormalize(string value, out string sidValue)
        {
            sidValue = null;
            string candidate = value == null ? null : value.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            try
            {
                if (StartupSid.IsValid(candidate))
                {
                    sidValue = new SecurityIdentifier(candidate).Value;
                    return true;
                }

                NTAccount account = new NTAccount(candidate);
                SecurityIdentifier sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                sidValue = sid.Value;
                return true;
            }
            catch (Exception)
            {
                sidValue = null;
                return false;
            }
        }

        // The principal identity decides whether the task belongs to this user, so
        // an unreadable value must fail closed and surface a readable reason.
        public static string RequirePrincipal(string value)
        {
            string sidValue;
            if (!TryNormalize(value, out sidValue))
            {
                throw new InvalidOperationException(
                    "작업 스케줄러가 반환한 작업 실행 계정을 확인하지 못했습니다"
                    + (string.IsNullOrEmpty(value) ? "." : ": " + value.Trim() + ".")
                    + " 이 작업이 현재 사용자 소유인지 확인할 수 없어 상태를 읽지 못한 것으로 처리했습니다.");
            }

            return sidValue;
        }

        // A logon trigger without a resolvable user must not match the current
        // user; null is compared literally and never equals a SID.
        public static string NormalizeOptional(string value)
        {
            string sidValue;
            return TryNormalize(value, out sidValue) ? sidValue : null;
        }
    }

    internal static class StartupTaskService
    {
        private const int TaskCreateOrUpdate = 6;
        private const int TaskDeleteNone = 0;
        private const int ErrorFileNotFound = unchecked((int)0x80070002);
        private const int ErrorPathNotFound = unchecked((int)0x80070003);

        public static StartupTaskSnapshot Query(string taskName)
        {
            if (string.IsNullOrEmpty(taskName))
            {
                return StartupTaskSnapshot.Unreadable("작업 이름이 없습니다.");
            }

            try
            {
                dynamic service = CreateService();
                dynamic folder = service.GetFolder(StartupConstants.TaskFolderPath);
                dynamic task;
                try
                {
                    task = folder.GetTask(taskName);
                }
                catch (COMException ex)
                {
                    if (ex.HResult == ErrorFileNotFound || ex.HResult == ErrorPathNotFound)
                    {
                        return StartupTaskSnapshot.Missing();
                    }

                    throw;
                }

                return ReadSnapshot(task);
            }
            catch (Exception ex)
            {
                return StartupTaskSnapshot.Unreadable(Describe(ex));
            }
        }

        public static void Register(StartupTaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            dynamic service = CreateService();
            dynamic folder = service.GetFolder(StartupConstants.TaskFolderPath);

            // RegisterTaskDefinition's task argument must be an ITaskDefinition COM
            // object, not XML text. NewTask(0) creates only an in-memory definition;
            // assigning XmlText makes the Task Scheduler service parse and validate
            // the generated document before anything is registered.
            dynamic task = service.NewTask(0);
            task.XmlText = definition.ToTaskXml();

            // The current user's SID and InteractiveToken are explicit here as well
            // as in the XML principal. TASK_CREATE_OR_UPDATE (6) replaces only this
            // exact task name; no password and no other logon type is used.
            folder.RegisterTaskDefinition(
                definition.TaskName,
                task,
                TaskCreateOrUpdate,
                definition.UserSid,
                null,
                StartupConstants.LogonTypeInteractiveToken,
                null);
        }

        // Non-mutating platform check for the RegisterTaskDefinition contract. It
        // connects to Task Scheduler and assigns the generated XML to a fresh
        // in-memory ITaskDefinition, but never registers, updates or deletes a task.
        // The test suite invokes this once so a pure-XML-only test cannot hide a
        // runtime type/parse error.
        public static void VerifyTaskXmlAssignment(StartupTaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            dynamic service = CreateService();
            dynamic task = service.NewTask(0);
            task.XmlText = definition.ToTaskXml();

            string roundTrip = (string)task.XmlText;
            if (string.IsNullOrEmpty(roundTrip))
            {
                throw new InvalidOperationException("작업 스케줄러가 생성된 XML을 받아들이지 않았습니다.");
            }
        }

        // Non-mutating identity check: reads the values Task Scheduler actually
        // returns from an in-memory definition (NewTask(0) + XmlText) through the
        // same normalization path used for a registered task. Never registers,
        // updates or deletes a task.
        public static StartupTaskSnapshot ReadInMemorySnapshot(StartupTaskDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            dynamic service = CreateService();
            dynamic inMemory = service.NewTask(0);
            inMemory.XmlText = definition.ToTaskXml();

            try
            {
                return ReadDefinitionSnapshot(inMemory, (bool)inMemory.Settings.Enabled);
            }
            catch (Exception ex)
            {
                return StartupTaskSnapshot.Unreadable(Describe(ex));
            }
        }

        public static void Delete(string taskName)
        {
            dynamic service = CreateService();
            dynamic folder = service.GetFolder(StartupConstants.TaskFolderPath);
            folder.DeleteTask(taskName, TaskDeleteNone);
        }

        private static dynamic CreateService()
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service", true);
            dynamic service = Activator.CreateInstance(serviceType);
            service.Connect();
            return service;
        }

        private static StartupTaskSnapshot ReadSnapshot(dynamic task)
        {
            try
            {
                bool taskEnabled = (bool)task.Enabled;
                dynamic taskDefinition = task.Definition;
                return ReadDefinitionSnapshot(taskDefinition, taskEnabled);
            }
            catch (Exception ex)
            {
                return StartupTaskSnapshot.Unreadable(Describe(ex));
            }
        }

        private static StartupTaskSnapshot ReadDefinitionSnapshot(dynamic definition, bool taskEnabled)
        {
            StartupTaskSnapshot snapshot = new StartupTaskSnapshot();
            try
            {
                snapshot.TaskExists = true;
                snapshot.TaskEnabled = taskEnabled;

                snapshot.Description = (string)definition.RegistrationInfo.Description;

                dynamic principal = definition.Principal;
                snapshot.PrincipalUserId = StartupIdentity.RequirePrincipal(
                    OptionalString(delegate { return (string)principal.UserId; }));
                snapshot.RunLevel = (int)principal.RunLevel;
                snapshot.LogonType = (int)principal.LogonType;

                dynamic triggers = definition.Triggers;
                int triggerCount = (int)triggers.Count;
                for (int i = 1; i <= triggerCount; i++)
                {
                    dynamic trigger = triggers.Item(i);
                    StartupTaskTriggerSnapshot entry = new StartupTaskTriggerSnapshot();
                    entry.Type = (int)trigger.Type;
                    entry.Enabled = (bool)trigger.Enabled;
                    if (entry.Type == StartupConstants.TriggerTypeLogon)
                    {
                        entry.UserId = StartupIdentity.NormalizeOptional(
                            OptionalString(delegate { return (string)trigger.UserId; }));
                    }

                    snapshot.Triggers.Add(entry);
                }

                dynamic actions = definition.Actions;
                int actionCount = (int)actions.Count;
                for (int i = 1; i <= actionCount; i++)
                {
                    dynamic action = actions.Item(i);
                    StartupTaskActionSnapshot entry = new StartupTaskActionSnapshot();
                    entry.Type = (int)action.Type;
                    if (entry.Type == StartupConstants.ActionTypeExec)
                    {
                        entry.Path = OptionalString(delegate { return (string)action.Path; });
                        entry.Arguments = OptionalString(delegate { return (string)action.Arguments; });
                        entry.WorkingDirectory = OptionalString(delegate { return (string)action.WorkingDirectory; });
                    }

                    snapshot.Actions.Add(entry);
                }
            }
            catch (Exception ex)
            {
                return StartupTaskSnapshot.Unreadable(Describe(ex));
            }

            snapshot.ReadOk = true;
            return snapshot;
        }

        private static string OptionalString(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return "알 수 없는 오류";
            }

            COMException com = ex as COMException;
            if (com != null)
            {
                return com.Message + " (HRESULT 0x" + com.HResult.ToString("X8", CultureInfo.InvariantCulture) + ")";
            }

            return ex.Message;
        }
    }

    internal sealed class StartupApplyResult
    {
        public bool Success;
        public bool Cancelled;
        public string MessageKo;
        public StartupTaskState State;
    }

    internal static class StartupTaskHost
    {
        private const int ErrorCancelled = 1223;

        public static string CurrentUserSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.User == null)
                {
                    throw new InvalidOperationException("현재 Windows 사용자 SID를 확인하지 못했습니다.");
                }

                return identity.User.Value;
            }
        }

        public static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static string OwnExecutablePath()
        {
            string path = Application.ExecutablePath;
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("현재 실행 파일 경로를 확인하지 못했습니다.");
            }

            return Path.GetFullPath(path);
        }

        public static StartupTaskDefinition CreateDefinition()
        {
            string sid = CurrentUserSid();
            string executable = OwnExecutablePath();
            string directory = Path.GetDirectoryName(executable);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("실행 파일의 폴더 경로를 확인하지 못했습니다.");
            }

            return new StartupTaskDefinition(sid, executable, directory, StartupConstants.StartupArgument);
        }

        // Reads the real task state. Never throws: failures are returned as an
        // Unreadable state so the UI cannot present them as a successful "off".
        public static StartupTaskState QueryState()
        {
            try
            {
                StartupTaskDefinition definition = CreateDefinition();
                return StartupTaskInspector.Evaluate(
                    StartupTaskService.Query(definition.TaskName),
                    definition);
            }
            catch (Exception ex)
            {
                return StartupTaskInspector.UnreadableState(StartupTaskService.Describe(ex));
            }
        }

        // UI entry point. When the current process is already elevated the task is
        // changed directly; otherwise the same executable is started once with a
        // UAC consent prompt in helper mode. This method blocks, so callers must
        // run it off the UI thread.
        public static StartupApplyResult Apply(bool enable)
        {
            if (IsElevated())
            {
                return ApplyDirect(enable);
            }

            return ApplyWithElevation(enable);
        }

        // Used by the elevated helper. Uses only this executable's own path.
        public static StartupApplyResult ApplyDirect(bool enable)
        {
            StartupApplyResult result = new StartupApplyResult();

            StartupTaskDefinition definition;
            try
            {
                definition = CreateDefinition();
            }
            catch (Exception ex)
            {
                result.MessageKo = "자동 시작 작업 정보를 만들지 못했습니다: " + StartupTaskService.Describe(ex);
                return result;
            }

            StartupTaskSnapshot snapshot = StartupTaskService.Query(definition.TaskName);
            StartupTaskState state = StartupTaskInspector.Evaluate(snapshot, definition);
            StartupApplyPlan plan = StartupApplyPlanner.Plan(state, enable);

            if (plan.Action == StartupApplyAction.Refuse)
            {
                result.MessageKo = plan.MessageKo;
                result.State = state;
                return result;
            }

            if (plan.Action == StartupApplyAction.None)
            {
                result.Success = true;
                result.MessageKo = plan.MessageKo;
                result.State = state;
                return result;
            }

            try
            {
                if (plan.Action == StartupApplyAction.Register)
                {
                    StartupTaskService.Register(definition);
                }
                else
                {
                    StartupTaskService.Delete(definition.TaskName);
                }
            }
            catch (Exception ex)
            {
                StartupTaskState failed = StartupTaskInspector.Evaluate(
                    StartupTaskService.Query(definition.TaskName),
                    definition);
                result.MessageKo = (enable
                        ? "자동 시작 작업을 등록하지 못했습니다: "
                        : "자동 시작 작업을 제거하지 못했습니다: ")
                    + StartupTaskService.Describe(ex)
                    + Environment.NewLine + "실제 상태: " + failed.MessageKo;
                result.State = failed;
                return result;
            }

            StartupTaskState after = StartupTaskInspector.Evaluate(
                StartupTaskService.Query(definition.TaskName),
                definition);
            result.State = after;

            bool matches;
            if (enable)
            {
                matches = after.Kind == StartupTaskStateKind.Enabled;
            }
            else
            {
                matches = after.Kind == StartupTaskStateKind.NotConfigured
                    || after.Kind == StartupTaskStateKind.Disabled;
            }

            result.Success = matches;
            result.MessageKo = matches
                ? (enable
                    ? "자동 시작을 사용합니다. Windows 로그인 시 관리자 권한으로 실행됩니다."
                    : "자동 시작을 사용 안 함으로 설정했습니다.")
                : "요청한 상태와 실제 작업 상태가 다릅니다. " + after.MessageKo;
            return result;
        }

        private static StartupApplyResult ApplyWithElevation(bool enable)
        {
            StartupApplyResult result = new StartupApplyResult();
            StartupTaskDefinition definition;

            try
            {
                definition = CreateDefinition();
            }
            catch (Exception ex)
            {
                result.MessageKo = "자동 시작 작업 정보를 만들지 못했습니다: " + StartupTaskService.Describe(ex);
                return result;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = definition.ExecutablePath;
            info.Arguments = StartupCli.BuildHelperArguments(enable, definition.UserSid);
            info.WorkingDirectory = definition.WorkingDirectory;
            info.UseShellExecute = true;
            info.Verb = "runas";

            int helperExitCode;
            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        result.MessageKo = "관리자 권한 도우미 프로세스를 시작하지 못했습니다. "
                            + "자동 시작 설정은 변경되지 않았습니다.";
                        result.State = QueryState();
                        return result;
                    }

                    process.WaitForExit();
                    helperExitCode = process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                result.Cancelled = ex.NativeErrorCode == ErrorCancelled;
                result.MessageKo = result.Cancelled
                    ? "관리자 승인이 취소되어 자동 시작 설정을 변경하지 않았습니다."
                    : "관리자 권한으로 자동 시작 설정을 실행하지 못했습니다: " + ex.Message;
                result.State = QueryState();
                return result;
            }
            catch (Exception ex)
            {
                result.MessageKo = "관리자 권한으로 자동 시작 설정을 실행하지 못했습니다: "
                    + StartupTaskService.Describe(ex);
                result.State = QueryState();
                return result;
            }

            StartupTaskState actual = QueryState();
            result.State = actual;

            // A helper process that started but failed must never be reported as a
            // successful change, even when the real state already happened to match
            // the request. The refreshed state is still shown so the user can see it.
            if (helperExitCode != 0)
            {
                result.Success = false;
                result.MessageKo = "관리자 권한 도우미가 오류로 종료되어(종료 코드 "
                    + helperExitCode.ToString(CultureInfo.InvariantCulture)
                    + ") 자동 시작 설정을 적용했다고 간주하지 않았습니다. 실제 상태: "
                    + actual.MessageKo;
                return result;
            }

            bool matches;
            if (enable)
            {
                matches = actual.Kind == StartupTaskStateKind.Enabled;
            }
            else
            {
                matches = actual.Kind == StartupTaskStateKind.NotConfigured
                    || actual.Kind == StartupTaskStateKind.Disabled;
            }

            result.Success = matches;
            result.MessageKo = matches
                ? (enable
                    ? "자동 시작을 사용합니다. Windows 로그인 시 관리자 권한으로 실행됩니다."
                    : "자동 시작을 사용 안 함으로 설정했습니다.")
                : "관리자 권한 작업이 요청한 상태를 만들지 못했습니다. 실제 상태를 다시 확인했습니다. "
                    + actual.MessageKo;
            return result;
        }

        // Elevated helper mode (handled before the single-instance mutex). Accepts
        // only the exact two parsed options; verifies that the Windows identity is
        // the requested user and refuses test settings overrides.
        public static int RunHelper(StartupCommandLine command)
        {
            try
            {
                string currentSid = CurrentUserSid();
                if (!StartupSid.Same(currentSid, command.HelperUserSid))
                {
                    ShowError("요청한 사용자 SID(" + command.HelperUserSid + ")와 현재 Windows 사용자 SID("
                        + currentSid + ")가 다릅니다. 다른 관리자 자격 증명으로는 이 사용자의 자동 시작을 변경할 수 없습니다.");
                    return 66;
                }

                string overrideDirectory = Environment.GetEnvironmentVariable(
                    StartupConstants.SettingsDirectoryEnvironment);
                if (!string.IsNullOrEmpty(overrideDirectory))
                {
                    ShowError("테스트 설정 폴더(GAME_AUDIO_KEYS_SETTINGS_DIR)를 사용하는 동안에는 자동 시작 작업을 변경할 수 없습니다. "
                        + "임시 앱 위치/설정이 등록되지 않도록 일반 실행에서 설정하세요.");
                    return 66;
                }

                if (!IsElevated())
                {
                    ShowError("관리자 권한이 없어 자동 시작 작업을 변경할 수 없습니다. Windows 관리자 승인을 확인한 뒤 다시 시도하세요.");
                    return 66;
                }

                StartupApplyResult result = ApplyDirect(command.HelperEnable);
                if (!result.Success)
                {
                    ShowError(result.MessageKo);
                    return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                ShowError("자동 시작 설정을 적용하지 못했습니다: " + StartupTaskService.Describe(ex));
                return 1;
            }
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                StartupConstants.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
