// Windows Audio Keys - elevated login startup: command line, task definition,
// actual-state evaluation and the pure decision of what an apply request must do.
// This file has no COM or WinForms dependency so the console tests can verify it
// without reading or changing the real Task Scheduler.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace GameAudioKeys
{
    internal static class StartupConstants
    {
        public const string ProductName = "Windows Audio Keys";
        public const string TaskNamePrefix = "WindowsAudioKeys-";
        public const string OwnershipMarker = "WindowsAudioKeys.OwnedTask.v1";
        public const string TaskFolderPath = "\\";
        public const string StartupArgument = "--startup";
        public const string HelperEnableSwitch = "--configure-startup";
        public const string HelperSidSwitch = "--request-user-sid";
        public const string SettingsDirectoryEnvironment = "GAME_AUDIO_KEYS_SETTINGS_DIR";

        // Task Scheduler constants used both by the definition writer and the COM
        // adapter, kept here so the pure tests can assert exact values.
        public const int TriggerTypeLogon = 9;              // TASK_TRIGGER_LOGON
        public const int ActionTypeExec = 0;                // TASK_ACTION_EXEC
        public const int RunLevelHighest = 1;               // TASK_RUNLEVEL_HIGHEST
        public const int LogonTypeInteractiveToken = 3;     // TASK_LOGON_INTERACTIVE_TOKEN

        public static string BuildTaskName(string sid)
        {
            if (!StartupSid.IsValid(sid))
            {
                throw new ArgumentException("사용자 SID 형식이 올바르지 않습니다.", "sid");
            }

            return TaskNamePrefix + sid;
        }
    }

    // Strict numeric SID syntax. The helper mode must never accept an arbitrary
    // identity string, so SDDL aliases and names are rejected.
    internal static class StartupSid
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 184)
            {
                return false;
            }

            if (!value.StartsWith("S-1-", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = value.Split('-');
            if (parts.Length < 4)
            {
                return false;
            }

            for (int i = 2; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.Length > 10)
                {
                    return false;
                }

                for (int c = 0; c < part.Length; c++)
                {
                    if (part[c] < '0' || part[c] > '9')
                    {
                        return false;
                    }
                }
            }

            try
            {
                new SecurityIdentifier(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool Same(string left, string right)
        {
            return !string.IsNullOrEmpty(left)
                && !string.IsNullOrEmpty(right)
                && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal enum StartupCommandKind
    {
        Run,
        Helper,
        Error
    }

    internal sealed class StartupCommandLine
    {
        public StartupCommandKind Kind;
        public bool StartupMode;
        public bool HelpRequested;
        public bool SettingsDirectoryOverridden;
        public string SettingsDirectory;
        public bool HelperEnable;
        public string HelperUserSid;
        public string ErrorKo;
    }

    internal static class StartupCli
    {
        public static StartupCommandLine Parse(string[] args)
        {
            StartupCommandLine result = new StartupCommandLine();
            result.Kind = StartupCommandKind.Run;

            bool startupSeen = false;
            bool settingsSeen = false;
            bool configureSeen = false;
            bool sidSeen = false;
            string configureValue = null;
            string sidValue = null;

            for (int i = 0; i < (args == null ? 0 : args.Length); i++)
            {
                string argument = args[i] == null ? string.Empty : args[i];

                if (argument == StartupConstants.StartupArgument)
                {
                    if (startupSeen)
                    {
                        return Error(result, "--startup 은 한 번만 지정할 수 있습니다.");
                    }

                    startupSeen = true;
                }
                else if (argument == StartupConstants.HelperEnableSwitch)
                {
                    if (configureSeen)
                    {
                        return Error(result, "--configure-startup 은 한 번만 지정할 수 있습니다.");
                    }

                    if (i + 1 >= args.Length)
                    {
                        return Error(result, "--configure-startup 뒤에 enable 또는 disable 이 필요합니다.");
                    }

                    string value = args[i + 1];
                    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--configure-startup 뒤에 enable 또는 disable 이 필요합니다.");
                    }

                    configureValue = value;
                    i++;
                    configureSeen = true;
                }
                else if (argument.StartsWith(StartupConstants.HelperEnableSwitch + "=", StringComparison.OrdinalIgnoreCase))
                {
                    if (configureSeen)
                    {
                        return Error(result, "--configure-startup 은 한 번만 지정할 수 있습니다.");
                    }

                    configureValue = argument.Substring(StartupConstants.HelperEnableSwitch.Length + 1);
                    if (string.IsNullOrWhiteSpace(configureValue)
                        || configureValue.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--configure-startup= 뒤에 enable 또는 disable 이 필요합니다.");
                    }

                    configureSeen = true;
                }
                else if (argument == StartupConstants.HelperSidSwitch)
                {
                    if (sidSeen)
                    {
                        return Error(result, "--request-user-sid 는 한 번만 지정할 수 있습니다.");
                    }

                    if (i + 1 >= args.Length)
                    {
                        return Error(result, "--request-user-sid 뒤에 사용자 SID가 필요합니다.");
                    }

                    string value = args[i + 1];
                    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--request-user-sid 뒤에 사용자 SID가 필요합니다.");
                    }

                    sidValue = value;
                    i++;
                    sidSeen = true;
                }
                else if (argument.StartsWith(StartupConstants.HelperSidSwitch + "=", StringComparison.OrdinalIgnoreCase))
                {
                    if (sidSeen)
                    {
                        return Error(result, "--request-user-sid 는 한 번만 지정할 수 있습니다.");
                    }

                    sidValue = argument.Substring(StartupConstants.HelperSidSwitch.Length + 1);
                    if (string.IsNullOrWhiteSpace(sidValue)
                        || sidValue.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--request-user-sid= 뒤에 사용자 SID가 필요합니다.");
                    }

                    sidSeen = true;
                }
                else if (argument == "--settings-dir")
                {
                    if (settingsSeen)
                    {
                        return Error(result, "--settings-dir 은 한 번만 지정할 수 있습니다.");
                    }

                    if (i + 1 >= args.Length)
                    {
                        return Error(result, "--settings-dir 뒤에 폴더 경로가 필요합니다.");
                    }

                    string value = args[i + 1];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return Error(result, "--settings-dir 뒤에 비어 있지 않은 폴더 경로가 필요합니다.");
                    }

                    if (value.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--settings-dir 값으로 다른 옵션을 사용할 수 없습니다: " + value);
                    }

                    result.SettingsDirectory = value;
                    i++;
                    settingsSeen = true;
                }
                else if (argument.StartsWith("--settings-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    if (settingsSeen)
                    {
                        return Error(result, "--settings-dir 은 한 번만 지정할 수 있습니다.");
                    }

                    string value = argument.Substring("--settings-dir=".Length);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return Error(result, "--settings-dir= 뒤에 비어 있지 않은 폴더 경로가 필요합니다.");
                    }

                    if (value.StartsWith("--", StringComparison.Ordinal))
                    {
                        return Error(result, "--settings-dir 값으로 다른 옵션을 사용할 수 없습니다: " + value);
                    }

                    result.SettingsDirectory = value;
                    settingsSeen = true;
                }
                else if (argument == "--help" || argument == "-h" || argument == "/?")
                {
                    result.HelpRequested = true;
                }
                else
                {
                    return Error(result, "알 수 없는 인수입니다: " + argument);
                }
            }

            bool helper = configureSeen || sidSeen;
            if (helper)
            {
                if (startupSeen)
                {
                    return Error(result, "자동 시작 설정 모드에서는 --startup 을 함께 사용할 수 없습니다.");
                }

                if (settingsSeen)
                {
                    return Error(result, "자동 시작 설정 모드에서는 --settings-dir 을 함께 사용할 수 없습니다.");
                }

                if (result.HelpRequested)
                {
                    return Error(result, "자동 시작 설정 모드에서는 --help 를 함께 사용할 수 없습니다.");
                }

                if (!configureSeen)
                {
                    return Error(result, "--configure-startup 이 필요합니다.");
                }

                if (!string.Equals(configureValue, "enable", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(configureValue, "disable", StringComparison.OrdinalIgnoreCase))
                {
                    return Error(result, "--configure-startup 값은 enable 또는 disable 이어야 합니다: " + configureValue);
                }

                if (!sidSeen)
                {
                    return Error(result, "--request-user-sid <SID> 가 필요합니다.");
                }

                if (!StartupSid.IsValid(sidValue))
                {
                    return Error(result, "사용자 SID 형식이 올바르지 않습니다: " + sidValue);
                }

                result.Kind = StartupCommandKind.Helper;
                result.HelperEnable = string.Equals(configureValue, "enable", StringComparison.OrdinalIgnoreCase);
                result.HelperUserSid = sidValue;
                return result;
            }

            if (result.HelpRequested && (startupSeen || settingsSeen))
            {
                return Error(result, "--help 는 다른 인수와 함께 사용할 수 없습니다.");
            }

            result.StartupMode = startupSeen;
            result.SettingsDirectoryOverridden = settingsSeen;
            return result;
        }

        public static bool IsSettingsOverrideActive(bool explicitOverride, string environmentValue)
        {
            // The explicit --settings-dir flag is authoritative even if a future parser
            // change ever produced an empty value. Task registration must never become
            // available merely because the path string happened to be empty.
            return explicitOverride || !string.IsNullOrEmpty(environmentValue);
        }

        public static string BuildHelperArguments(bool enable, string userSid)
        {
            if (!StartupSid.IsValid(userSid))
            {
                throw new ArgumentException("사용자 SID 형식이 올바르지 않습니다.", "userSid");
            }

            return StartupConstants.HelperEnableSwitch + " " + (enable ? "enable" : "disable")
                + " " + StartupConstants.HelperSidSwitch + " " + userSid;
        }

        private static StartupCommandLine Error(StartupCommandLine result, string message)
        {
            result.Kind = StartupCommandKind.Error;
            result.ErrorKo = message;
            return result;
        }
    }

    internal static class StartupLaunchPolicy
    {
        // Settings visibility is independent of the startup mode flag once the user
        // launched the app manually. Login startup stays silent unless the saved
        // settings are corrupt and the user must see the problem.
        public static bool ShouldShowSettings(bool startupMode, bool settingsCorrupt)
        {
            return !startupMode || settingsCorrupt;
        }
    }

    // The exact definition this application owns. Paths are stored and emitted
    // without surrounding quotes (Task Scheduler stores the executable path as a
    // plain string); XmlWriter handles escaping of special characters.
    internal sealed class StartupTaskDefinition
    {
        private readonly string description;

        public readonly string UserSid;
        public readonly string ExecutablePath;
        public readonly string WorkingDirectory;
        public readonly string Arguments;
        public readonly string TaskName;

        public StartupTaskDefinition(string userSid, string executablePath, string workingDirectory, string arguments)
        {
            if (!StartupSid.IsValid(userSid))
            {
                throw new ArgumentException("사용자 SID 형식이 올바르지 않습니다.", "userSid");
            }

            if (string.IsNullOrEmpty(executablePath) || !Path.IsPathRooted(executablePath))
            {
                throw new ArgumentException("실행 파일의 절대 경로가 필요합니다.", "executablePath");
            }

            if (string.IsNullOrEmpty(workingDirectory) || !Path.IsPathRooted(workingDirectory))
            {
                throw new ArgumentException("작업 폴더의 절대 경로가 필요합니다.", "workingDirectory");
            }

            if (string.IsNullOrEmpty(arguments))
            {
                throw new ArgumentException("실행 인수가 필요합니다.", "arguments");
            }

            this.UserSid = userSid;
            this.ExecutablePath = executablePath;
            this.WorkingDirectory = workingDirectory;
            this.Arguments = arguments;
            this.TaskName = StartupConstants.BuildTaskName(userSid);
            this.description = StartupConstants.ProductName
                + ": Windows 로그인 시 관리자 권한으로 자동 시작합니다. 소유 표식: "
                + StartupConstants.OwnershipMarker;
        }

        public string Description
        {
            get { return this.description; }
        }

        public bool OwnsDescription(string candidate)
        {
            return candidate != null
                && candidate.IndexOf(StartupConstants.OwnershipMarker, StringComparison.Ordinal) >= 0;
        }

        public string ToTaskXml()
        {
            StringBuilder builder = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.OmitXmlDeclaration = false;

            using (XmlWriter writer = XmlWriter.Create(builder, settings))
            {
                writer.WriteStartDocument();

                writer.WriteStartElement("Task", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                writer.WriteAttributeString("version", "1.2");

                writer.WriteStartElement("RegistrationInfo");
                writer.WriteElementString("Description", this.description);
                writer.WriteEndElement();

                writer.WriteStartElement("Triggers");
                writer.WriteStartElement("LogonTrigger");
                writer.WriteElementString("Enabled", "true");
                writer.WriteElementString("UserId", this.UserSid);
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("Principals");
                writer.WriteStartElement("Principal");
                writer.WriteAttributeString("id", "Author");
                writer.WriteElementString("UserId", this.UserSid);
                writer.WriteElementString("LogonType", "InteractiveToken");
                writer.WriteElementString("RunLevel", "HighestAvailable");
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("Settings");
                writer.WriteElementString("MultipleInstancesPolicy", "IgnoreNew");
                writer.WriteElementString("DisallowStartIfOnBatteries", "false");
                writer.WriteElementString("StopIfGoingOnBatteries", "false");
                writer.WriteElementString("AllowHardTerminate", "true");
                writer.WriteElementString("StartWhenAvailable", "false");
                writer.WriteElementString("RunOnlyIfNetworkAvailable", "false");
                writer.WriteStartElement("IdleSettings");
                writer.WriteElementString("StopOnIdleEnd", "false");
                writer.WriteElementString("RestartOnIdle", "false");
                writer.WriteEndElement();
                writer.WriteElementString("AllowStartOnDemand", "true");
                writer.WriteElementString("Enabled", "true");
                writer.WriteElementString("Hidden", "false");
                writer.WriteElementString("RunOnlyIfIdle", "false");
                writer.WriteElementString("WakeToRun", "false");
                writer.WriteElementString("ExecutionTimeLimit", "PT0S");
                writer.WriteElementString("Priority", "7");
                writer.WriteEndElement();

                writer.WriteStartElement("Actions");
                writer.WriteAttributeString("Context", "Author");
                writer.WriteStartElement("Exec");
                writer.WriteElementString("Command", this.ExecutablePath);
                writer.WriteElementString("Arguments", this.Arguments);
                writer.WriteElementString("WorkingDirectory", this.WorkingDirectory);
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return builder.ToString();
        }
    }

    internal sealed class StartupTaskTriggerSnapshot
    {
        public int Type;
        public bool Enabled = true;
        public string UserId;
    }

    internal sealed class StartupTaskActionSnapshot
    {
        public int Type;
        public string Path;
        public string Arguments;
        public string WorkingDirectory;
    }

    internal sealed class StartupTaskSnapshot
    {
        public bool ReadOk;
        public string ReadErrorKo;
        public bool TaskExists;
        public bool TaskEnabled;
        public string Description;
        public string PrincipalUserId;
        public int RunLevel;
        public int LogonType;
        public readonly List<StartupTaskTriggerSnapshot> Triggers = new List<StartupTaskTriggerSnapshot>();
        public readonly List<StartupTaskActionSnapshot> Actions = new List<StartupTaskActionSnapshot>();

        public static StartupTaskSnapshot Missing()
        {
            StartupTaskSnapshot snapshot = new StartupTaskSnapshot();
            snapshot.ReadOk = true;
            snapshot.TaskExists = false;
            return snapshot;
        }

        public static StartupTaskSnapshot Unreadable(string errorKo)
        {
            StartupTaskSnapshot snapshot = new StartupTaskSnapshot();
            snapshot.ReadOk = false;
            snapshot.ReadErrorKo = errorKo;
            return snapshot;
        }
    }

    internal enum StartupTaskStateKind
    {
        NotConfigured,
        Enabled,
        Disabled,
        NeedsRefresh,
        Mismatch,
        ForeignOwner,
        Unreadable
    }

    internal sealed class StartupTaskState
    {
        public StartupTaskStateKind Kind;
        public string MessageKo;

        // Actual checkbox value for the owned task, independent of whether the task
        // still points at the current executable/configuration. A stale but enabled
        // task is checked; CheckboxKnown is false for unreadable or foreign tasks so
        // the UI never presents an unknown state as a trustworthy "off".
        public bool CheckboxKnown;
        public bool CheckboxChecked;

        public bool IsEnabled
        {
            get { return this.Kind == StartupTaskStateKind.Enabled; }
        }

        public bool IsError
        {
            get
            {
                return this.Kind == StartupTaskStateKind.Unreadable
                    || this.Kind == StartupTaskStateKind.ForeignOwner;
            }
        }
    }

    internal static class StartupTaskInspector
    {
        public static StartupTaskState UnreadableState(string detail)
        {
            StartupTaskState state = new StartupTaskState();
            state.Kind = StartupTaskStateKind.Unreadable;
            state.CheckboxKnown = false;
            state.CheckboxChecked = false;
            state.MessageKo = "상태 확인 실패 - 자동 시작 작업 상태를 읽지 못했습니다"
                + (string.IsNullOrEmpty(detail) ? "." : ": " + detail)
                + " 작업이 꺼져 있다고 간주하지 않았습니다.";
            return state;
        }

        public static StartupTaskState Evaluate(StartupTaskSnapshot snapshot, StartupTaskDefinition definition)
        {
            if (snapshot == null)
            {
                return UnreadableState("작업 정보가 없습니다.");
            }

            if (!snapshot.ReadOk)
            {
                return UnreadableState(snapshot.ReadErrorKo);
            }

            if (!snapshot.TaskExists)
            {
                StartupTaskState missing = new StartupTaskState();
                missing.Kind = StartupTaskStateKind.NotConfigured;
                missing.CheckboxKnown = true;
                missing.CheckboxChecked = false;
                missing.MessageKo = "사용 안 함 - 이 앱의 자동 시작 작업이 등록되어 있지 않습니다.";
                return missing;
            }

            StartupTaskState foreign = new StartupTaskState();
            foreign.Kind = StartupTaskStateKind.ForeignOwner;
            if (!definition.OwnsDescription(snapshot.Description)
                || !StartupSid.Same(snapshot.PrincipalUserId, definition.UserSid))
            {
                foreign.CheckboxKnown = false;
                foreign.CheckboxChecked = false;
                foreign.MessageKo = "변경하지 않음 - 같은 이름의 작업이 있지만 이 앱/현재 사용자 소유가 아닙니다. "
                    + "다른 프로그램이나 관리자가 만든 작업은 그대로 보존했습니다. 작업 스케줄러에서 직접 확인하세요.";
                return foreign;
            }

            string detail;
            bool pathChanged;
            if (!Matches(snapshot, definition, out detail, out pathChanged))
            {
                StartupTaskState mismatch = new StartupTaskState();
                mismatch.Kind = pathChanged ? StartupTaskStateKind.NeedsRefresh : StartupTaskStateKind.Mismatch;
                mismatch.CheckboxKnown = true;
                mismatch.CheckboxChecked = snapshot.TaskEnabled;
                mismatch.MessageKo = pathChanged
                    ? "등록 갱신 필요 - 이전 위치나 구성을 가리키는 자동 시작 작업이 남아 있습니다. '자동 시작 적용'을 눌러 현재 위치로 갱신하세요. (" + detail + ")"
                    : "다시 적용 필요 - 소유한 자동 시작 작업의 구성이 현재 버전과 다릅니다. '자동 시작 적용'으로 다시 등록하세요. (" + detail + ")";
                return mismatch;
            }

            StartupTaskState owned = new StartupTaskState();
            owned.CheckboxKnown = true;
            owned.CheckboxChecked = snapshot.TaskEnabled;
            if (snapshot.TaskEnabled)
            {
                owned.Kind = StartupTaskStateKind.Enabled;
                owned.MessageKo = "사용 중 - Windows 로그인 시 관리자 권한으로 자동 시작합니다.";
            }
            else
            {
                owned.Kind = StartupTaskStateKind.Disabled;
                owned.MessageKo = "꺼짐 - 이 앱의 자동 시작 작업이 있지만 사용 안 함 상태입니다. ('자동 시작 적용'으로 다시 켤 수 있습니다)";
            }

            return owned;
        }

        private static bool Matches(
            StartupTaskSnapshot snapshot,
            StartupTaskDefinition definition,
            out string detail,
            out bool pathChanged)
        {
            detail = null;
            pathChanged = false;

            if (snapshot.RunLevel != StartupConstants.RunLevelHighest)
            {
                detail = "최고 권한 실행이 아닙니다.";
                return false;
            }

            if (snapshot.LogonType != StartupConstants.LogonTypeInteractiveToken)
            {
                detail = "대화형 로그온 토큰이 아닙니다.";
                return false;
            }

            if (snapshot.Triggers.Count != 1)
            {
                detail = "로그온 트리거가 정확히 하나가 아닙니다.";
                return false;
            }

            StartupTaskTriggerSnapshot trigger = snapshot.Triggers[0];
            if (trigger.Type != StartupConstants.TriggerTypeLogon)
            {
                detail = "로그온 트리거가 아닙니다.";
                return false;
            }

            if (!trigger.Enabled)
            {
                detail = "로그온 트리거가 사용 안 함 상태입니다.";
                return false;
            }

            if (!StartupSid.Same(trigger.UserId, definition.UserSid))
            {
                detail = "로그온 트리거가 현재 사용자로 제한되지 않았습니다.";
                return false;
            }

            if (snapshot.Actions.Count != 1)
            {
                detail = "실행 동작이 정확히 하나가 아닙니다.";
                return false;
            }

            StartupTaskActionSnapshot action = snapshot.Actions[0];
            if (action.Type != StartupConstants.ActionTypeExec)
            {
                detail = "실행 파일 동작이 아닙니다.";
                return false;
            }

            if (!SamePath(action.Path, definition.ExecutablePath))
            {
                detail = "등록된 실행 파일 경로가 현재 위치와 다릅니다: " + (action.Path == null ? "(없음)" : action.Path);
                pathChanged = true;
                return false;
            }

            if (!SamePath(action.WorkingDirectory, definition.WorkingDirectory))
            {
                detail = "등록된 작업 폴더가 현재 위치와 다릅니다: "
                    + (action.WorkingDirectory == null ? "(없음)" : action.WorkingDirectory);
                pathChanged = true;
                return false;
            }

            if (!string.Equals(action.Arguments, definition.Arguments, StringComparison.Ordinal))
            {
                detail = "실행 인수가 '" + definition.Arguments + "' 가 아닙니다: "
                    + (action.Arguments == null ? "(없음)" : action.Arguments);
                return false;
            }

            return true;
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            string trimmed = path.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed.TrimEnd('\\');
        }
    }

    internal enum StartupApplyAction
    {
        None,
        Register,
        Delete,
        Refuse
    }

    internal sealed class StartupApplyPlan
    {
        public StartupApplyAction Action;
        public string MessageKo;
    }

    // Turns the current real state plus the requested checkbox value into exactly
    // one operation. Foreign or unreadable tasks are never overwritten or removed.
    internal static class StartupApplyPlanner
    {
        public static StartupApplyPlan Plan(StartupTaskState state, bool enable)
        {
            StartupApplyPlan plan = new StartupApplyPlan();
            if (state == null)
            {
                plan.Action = StartupApplyAction.Refuse;
                plan.MessageKo = "자동 시작 작업 상태를 확인하지 못해 변경하지 않았습니다.";
                return plan;
            }

            switch (state.Kind)
            {
                case StartupTaskStateKind.Enabled:
                    if (enable)
                    {
                        plan.Action = StartupApplyAction.None;
                        plan.MessageKo = "자동 시작이 이미 사용 중입니다. " + state.MessageKo;
                    }
                    else
                    {
                        plan.Action = StartupApplyAction.Delete;
                    }

                    return plan;

                case StartupTaskStateKind.Disabled:
                    if (enable)
                    {
                        plan.Action = StartupApplyAction.Register;
                    }
                    else
                    {
                        plan.Action = StartupApplyAction.Delete;
                    }

                    return plan;

                case StartupTaskStateKind.NotConfigured:
                    if (enable)
                    {
                        plan.Action = StartupApplyAction.Register;
                    }
                    else
                    {
                        plan.Action = StartupApplyAction.None;
                        plan.MessageKo = "자동 시작이 이미 사용 안 함 상태입니다.";
                    }

                    return plan;

                case StartupTaskStateKind.NeedsRefresh:
                case StartupTaskStateKind.Mismatch:
                    plan.Action = enable ? StartupApplyAction.Register : StartupApplyAction.Delete;
                    return plan;

                case StartupTaskStateKind.ForeignOwner:
                    plan.Action = StartupApplyAction.Refuse;
                    plan.MessageKo = state.MessageKo;
                    return plan;

                case StartupTaskStateKind.Unreadable:
                    plan.Action = StartupApplyAction.Refuse;
                    plan.MessageKo = "자동 시작 작업 상태를 확인하지 못해 변경하지 않았습니다. " + state.MessageKo;
                    return plan;

                default:
                    plan.Action = StartupApplyAction.Refuse;
                    plan.MessageKo = "자동 시작 작업 상태를 알 수 없어 변경하지 않았습니다.";
                    return plan;
            }
        }
    }
}
