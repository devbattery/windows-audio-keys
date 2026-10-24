// Game Audio Keys - shared Core Audio and process-matching core.
//
// Extracted unchanged (plus small additive helpers) from probe/AudioProbe.cs, which
// passed real-application verification. Both the app and the diagnostic probe compile
// this same file; do not add a second COM implementation elsewhere.
//
// Only ISimpleAudioVolume per application session is used. Endpoint/master volume is
// never touched. Windows APIs only, out of process; no hooks or injection.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GameAudioKeys.Core
{
    internal sealed class AudioKeysException : Exception
    {
        public readonly int ExitCode;

        public AudioKeysException(string message, int exitCode)
            : base(message)
        {
            this.ExitCode = exitCode;
        }
    }

    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int NoSession = 2;
        public const int Refused = 3;
        public const int EnumerationFailed = 4;
        public const int Usage = 64;
    }
    #region Native interop

    internal static class Native
    {
        public const uint CLSCTX_ALL = 0x17;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint TH32CS_SNAPPROCESS = 0x00000002;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        public delegate void WinEventDelegate(
            IntPtr winEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr module,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWinEvent(IntPtr winEventHook);

        public static int GetForegroundProcessId()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return 0;
            }

            int processId;
            GetWindowThreadProcessId(window, out processId);
            return processId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr process, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public static long ToLong(FILETIME time)
        {
            return ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
        }
    }

    internal enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    [Flags]
    internal enum DeviceState
    {
        ACTIVE = 0x00000001,
        DISABLED = 0x00000002,
        NOTPRESENT = 0x00000004,
        UNPLUGGED = 0x00000008,
        ALL = 0x0000000F
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(uint storageAccess, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    // IAudioSessionManager2: IAudioSessionManager methods first, then the
    // session-enumerator and notification methods appended by v2.
    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IntPtr sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int RegisterDuckingNotification(IntPtr duckingNotification);

        [PreserveSig]
        int UnregisterDuckingNotification(IntPtr duckingNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingParam);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr newNotifications);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingParam);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr newNotifications);

        // IAudioSessionControl2
        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);

        [PreserveSig]
        int GetProcessId(out int processId);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    #endregion

    #region Process table

    internal sealed class ProcEntry
    {
        public int Pid;
        public int ParentPid;
        public string ExeName;
        public bool CreationFetched;
        public bool CreationKnown;
        public long CreationTime;
        public bool PathFetched;
        public string Path;
        public string PathError;
        public string SyntheticPath;
    }

    internal sealed class ProcTable
    {
        private readonly Dictionary<int, ProcEntry> entries = new Dictionary<int, ProcEntry>();
        private bool fromSnapshot;

        public static ProcTable Snapshot()
        {
            ProcTable table = new ProcTable();
            table.fromSnapshot = true;
            IntPtr snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot.ToInt64() == -1)
            {
                int error = Marshal.GetLastWin32Error();
                throw new AudioKeysException("CreateToolhelp32Snapshot failed (" + error.ToString(CultureInfo.InvariantCulture) + ")", ExitCodes.EnumerationFailed);
            }

            try
            {
                Native.PROCESSENTRY32W entry = new Native.PROCESSENTRY32W();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32W));
                if (Native.Process32FirstW(snapshot, ref entry))
                {
                    do
                    {
                        table.AddRaw((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile);
                    }
                    while (Native.Process32NextW(snapshot, ref entry));
                }
            }
            finally
            {
                Native.CloseHandle(snapshot);
            }

            return table;
        }

        private void AddRaw(int pid, int parentPid, string exeName)
        {
            ProcEntry entry = new ProcEntry();
            entry.Pid = pid;
            entry.ParentPid = parentPid;
            entry.ExeName = exeName;
            this.entries[pid] = entry;
        }

        // Used by --self-test with a synthetic, fully specified table.
        public void AddSynthetic(int pid, int parentPid, string exeName, bool creationKnown, long creationTime, string syntheticPath)
        {
            ProcEntry entry = new ProcEntry();
            entry.Pid = pid;
            entry.ParentPid = parentPid;
            entry.ExeName = exeName;
            entry.CreationFetched = true;
            entry.CreationKnown = creationKnown;
            entry.CreationTime = creationTime;
            entry.SyntheticPath = syntheticPath;
            this.entries[pid] = entry;
        }

        public void AddSynthetic(int pid, int parentPid, string exeName, bool creationKnown, long creationTime)
        {
            this.AddSynthetic(pid, parentPid, exeName, creationKnown, creationTime, null);
        }

        public bool Contains(int pid)
        {
            return this.entries.ContainsKey(pid);
        }

        public bool TryGet(int pid, out ProcEntry entry)
        {
            return this.entries.TryGetValue(pid, out entry);
        }

        public long? GetCreationTime(int pid)
        {
            ProcEntry entry;
            if (!this.entries.TryGetValue(pid, out entry))
            {
                return null;
            }

            if (entry.CreationFetched)
            {
                return entry.CreationKnown ? (long?)entry.CreationTime : null;
            }

            entry.CreationFetched = true;
            if (!this.fromSnapshot || pid <= 0)
            {
                return null;
            }

            IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                entry.CreationKnown = false;
                return null;
            }

            try
            {
                Native.FILETIME creation, exit, kernel, user;
                if (Native.GetProcessTimes(handle, out creation, out exit, out kernel, out user))
                {
                    entry.CreationKnown = true;
                    entry.CreationTime = Native.ToLong(creation);
                    return entry.CreationTime;
                }

                entry.CreationKnown = false;
                return null;
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }

        public string GetPath(int pid, out string error)
        {
            error = null;
            ProcEntry entry;
            if (!this.entries.TryGetValue(pid, out entry))
            {
                error = "process not found in snapshot";
                return null;
            }

            if (entry.PathFetched)
            {
                error = entry.PathError;
                return entry.Path;
            }

            entry.PathFetched = true;
            if (!this.fromSnapshot || pid <= 0)
            {
                entry.Path = entry.SyntheticPath;
                if (entry.Path == null)
                {
                    entry.PathError = "process path unavailable (synthetic or system process)";
                }

                error = entry.PathError;
                return entry.Path;
            }

            IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                int win32 = Marshal.GetLastWin32Error();
                entry.PathError = "OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) failed (" + win32.ToString(CultureInfo.InvariantCulture) + ")";
                error = entry.PathError;
                return null;
            }

            try
            {
                StringBuilder buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                {
                    entry.Path = buffer.ToString(0, size);
                    return entry.Path;
                }

                int win32 = Marshal.GetLastWin32Error();
                entry.PathError = "QueryFullProcessImageName failed (" + win32.ToString(CultureInfo.InvariantCulture) + ")";
                error = entry.PathError;
                return null;
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }
    }

    #endregion

    #region Target matching (pure, testable)

    internal static class Targeting
    {
        public const int MaxDepth = 64;

        public static string ResultStatus(int targetCount)
        {
            return targetCount > 0 ? "ok" : "no-session";
        }

        public static List<int> SelectTargets(ProcTable table, int foregroundPid, IList<int> sessionPids)
        {
            List<int> selected = new List<int>();
            if (sessionPids == null || table == null)
            {
                return selected;
            }

            for (int i = 0; i < sessionPids.Count; i++)
            {
                string reason;
                string verification;
                if (IsTarget(table, foregroundPid, sessionPids[i], out reason, out verification))
                {
                    selected.Add(i);
                }
            }

            return selected;
        }

        // Shared classification used by both the app action and the diagnostic probe:
        // annotates every session with the match reason and returns the selected subset.
        public static List<AudioSession> SelectSessions(ProcTable table, int foregroundPid, IList<AudioSession> sessions)
        {
            List<AudioSession> selected = new List<AudioSession>();
            if (sessions == null)
            {
                return selected;
            }

            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSession session = sessions[i];
                string reason;
                string verification;
                bool isTarget = IsTarget(table, foregroundPid, session.Pid, out reason, out verification);
                if (session.CrossProcess)
                {
                    isTarget = false;
                    reason = "cross-process";
                }
                else if (session.SystemSounds)
                {
                    isTarget = false;
                    reason = "system-sounds";
                }
                else if (session.State == 2)
                {
                    isTarget = false;
                    reason = "expired";
                }

                session.MatchReason = reason;
                session.MatchVerification = verification;
                if (isTarget)
                {
                    session.Selected = true;
                    selected.Add(session);
                }
            }

            return selected;
        }

        // A session is a target only when its PID is exactly the foreground PID, or a
        // verified descendant that runs the SAME executable image. Generic ancestry alone
        // would also capture unrelated programs launched by the foreground app (terminal,
        // launcher, updater), so the executable path must match. Ancestors are never
        // followed and names alone are never compared.
        public static bool IsTarget(ProcTable table, int foregroundPid, int pid, out string reason, out string verification)
        {
            verification = null;
            if (foregroundPid <= 0)
            {
                reason = "invalid-foreground-pid";
                return false;
            }

            if (pid <= 0)
            {
                reason = "invalid-session-pid";
                return false;
            }

            if (pid == foregroundPid)
            {
                reason = "self";
                return true;
            }

            if (pid == 4)
            {
                reason = "system-pid";
                return false;
            }

            if (!IsDescendant(table, pid, foregroundPid, out verification))
            {
                bool structural = verification == "cycle"
                    || verification == "recycled-pid"
                    || verification == "unknown-creation-time"
                    || verification == "missing-process";
                reason = structural ? verification : "unrelated";
                return false;
            }

            string pathReason;
            if (!SameExecutablePath(table, pid, foregroundPid, out pathReason))
            {
                reason = pathReason;
                return false;
            }

            reason = "descendant";
            return true;
        }

        public static bool SameExecutablePath(ProcTable table, int pid, int foregroundPid, out string reason)
        {
            string pathErrorA;
            string pathErrorB;
            string pathA = table.GetPath(pid, out pathErrorA);
            string pathB = table.GetPath(foregroundPid, out pathErrorB);
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
            {
                reason = "unknown-path";
                return false;
            }

            if (!string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase))
            {
                reason = "different-executable";
                return false;
            }

            reason = "same-executable";
            return true;
        }

        // Walks child -> parent links collecting the chain first (so cycles are detected
        // regardless of timestamps), then validates EVERY edge: both creation times must be
        // known and parent <= child. Missing links, unknown times, recycled PIDs and cycles
        // all fail closed. The foreground PID terminates the walk successfully only after
        // its final edge is validated.
        public static bool IsDescendant(ProcTable table, int pid, int ancestorPid, out string verification)
        {
            verification = "unrelated-parent-chain";
            if (table == null || pid <= 0 || ancestorPid <= 0 || pid == ancestorPid)
            {
                return false;
            }

            List<int> chain = new List<int>();
            Dictionary<int, bool> seen = new Dictionary<int, bool>();
            seen[pid] = true;
            int current = pid;
            bool reached = false;

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                ProcEntry entry;
                if (!table.TryGet(current, out entry))
                {
                    verification = "missing-process";
                    return false;
                }

                int parent = entry.ParentPid;
                if (parent <= 4)
                {
                    verification = "root";
                    return false;
                }

                if (!table.Contains(parent))
                {
                    verification = "missing-process";
                    return false;
                }

                if (parent == ancestorPid)
                {
                    chain.Add(current);
                    reached = true;
                    break;
                }

                if (seen.ContainsKey(parent))
                {
                    verification = "cycle";
                    return false;
                }

                seen[parent] = true;
                chain.Add(current);
                current = parent;
            }

            if (!reached)
            {
                verification = "depth-limit";
                return false;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                int child = chain[i];
                int parent = (i + 1 < chain.Count) ? chain[i + 1] : ancestorPid;
                long? parentCreated = table.GetCreationTime(parent);
                long? childCreated = table.GetCreationTime(child);
                if (!parentCreated.HasValue || !childCreated.HasValue)
                {
                    verification = "unknown-creation-time";
                    return false;
                }

                if (childCreated.Value < parentCreated.Value)
                {
                    // The claimed parent is younger than the child, so this PID was recycled
                    // after the real parent exited.
                    verification = "recycled-pid";
                    return false;
                }
            }

            verification = "creation-time";
            return true;
        }
    }

    #endregion

    #region Audio sessions

    internal sealed class AudioSession
    {
        public string EndpointId;
        public string InstanceId;
        public int Pid;
        public bool PidReadOk;
        public int State;
        public bool StateReadOk;
        public bool Muted;
        public bool MuteReadOk;
        public float Volume;
        public bool VolumeReadOk;
        public bool SystemSounds;
        public bool CrossProcess;
        public string ReadError;

        public bool OrigMuted;
        public float OrigVolume;
        public bool Selected;
        public string MatchReason;
        public string MatchVerification;

        // Diagnostic hold/restore evidence is assigned by the probe only; the app shares
        // this type but never touches these fields.
#pragma warning disable 0649
        public bool DuringMuteReadOk;
        public bool DuringMuted;
        public bool DuringVolumeReadOk;
        public float DuringVolume;
        public bool AfterMuteReadOk;
        public bool AfterMuted;
        public bool AfterVolumeReadOk;
        public float AfterVolume;
#pragma warning restore 0649

        public IAudioSessionControl2 Control;
        public ISimpleAudioVolume VolumeControl;

        public bool TryReadMute(out bool mute)
        {
            mute = this.Muted;
            if (this.VolumeControl == null)
            {
                return false;
            }

            try
            {
                return this.VolumeControl.GetMute(out mute) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryReadVolume(out float level)
        {
            level = this.Volume;
            if (this.VolumeControl == null)
            {
                return false;
            }

            try
            {
                return this.VolumeControl.GetMasterVolume(out level) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public int TrySetMute(bool mute)
        {
            if (this.VolumeControl == null)
            {
                return unchecked((int)0x80004005);
            }

            try
            {
                Guid eventContext = Guid.Empty;
                return this.VolumeControl.SetMute(mute, ref eventContext);
            }
            catch (Exception)
            {
                return unchecked((int)0x80004005);
            }
        }

        public int TrySetVolume(float level)
        {
            if (this.VolumeControl == null)
            {
                return unchecked((int)0x80004005);
            }

            try
            {
                Guid eventContext = Guid.Empty;
                return this.VolumeControl.SetMasterVolume(level, ref eventContext);
            }
            catch (Exception)
            {
                return unchecked((int)0x80004005);
            }
        }

        public SessionSnapshot CreateSnapshot()
        {
            SessionSnapshot snapshot = new SessionSnapshot();
            snapshot.HasControl = this.Control != null;
            snapshot.HasVolumeControl = this.VolumeControl != null;
            snapshot.StateReadOk = this.StateReadOk;
            snapshot.State = this.State;
            snapshot.PidReadOk = this.PidReadOk;
            snapshot.Pid = this.Pid;
            snapshot.HasInstanceId = !string.IsNullOrEmpty(this.InstanceId);
            snapshot.MuteReadOk = this.MuteReadOk;
            snapshot.VolumeReadOk = this.VolumeReadOk;
            return snapshot;
        }

        // Re-reads every value needed to restore this session and re-validates it
        // immediately before mutation. Returns false (fail closed) when anything is
        // uncertain, so callers never guess a previous mute/volume.
        public bool TryRefreshOriginals(out string error)
        {
            error = null;
            if (this.Control == null || this.VolumeControl == null)
            {
                error = "audio session interfaces unavailable";
                return false;
            }

            int hr;
            int state;
            try
            {
                hr = this.Control.GetState(out state);
            }
            catch (Exception ex)
            {
                this.StateReadOk = false;
                error = "GetState threw " + ex.GetType().Name;
                return false;
            }

            if (hr != 0)
            {
                this.StateReadOk = false;
                error = "GetState failed 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                return false;
            }

            bool mute;
            if (!this.TryReadMute(out mute))
            {
                this.MuteReadOk = false;
                error = "GetMute failed";
                return false;
            }

            float volume;
            if (!this.TryReadVolume(out volume))
            {
                this.VolumeReadOk = false;
                error = "GetMasterVolume failed";
                return false;
            }

            string instanceId = null;
            try
            {
                hr = this.Control.GetSessionInstanceIdentifier(out instanceId);
            }
            catch (Exception ex)
            {
                error = "GetSessionInstanceIdentifier threw " + ex.GetType().Name;
                return false;
            }

            if (hr != 0)
            {
                error = "GetSessionInstanceIdentifier failed 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                return false;
            }

            this.InstanceId = instanceId;
            this.State = state;
            this.StateReadOk = true;
            this.Muted = mute;
            this.MuteReadOk = true;
            this.Volume = volume;
            this.VolumeReadOk = true;

            string reason;
            if (!Eligibility.IsEligible(this.CreateSnapshot(), out reason))
            {
                error = reason;
                return false;
            }

            this.OrigMuted = mute;
            this.OrigVolume = volume;
            return true;
        }
    }

    internal struct SessionSnapshot
    {
        public bool HasControl;
        public bool HasVolumeControl;
        public bool StateReadOk;
        public int State;
        public bool PidReadOk;
        public int Pid;
        public bool HasInstanceId;
        public bool MuteReadOk;
        public bool VolumeReadOk;
    }

    // Fail-closed preflight predicate: a session may only be mutated when every
    // original value needed for restoration is known for certain.
    internal static class Eligibility
    {
        public static bool IsEligible(SessionSnapshot snapshot, out string reason)
        {
            if (!snapshot.HasControl || !snapshot.HasVolumeControl)
            {
                reason = "audio session interfaces unavailable";
                return false;
            }

            if (!snapshot.StateReadOk)
            {
                reason = "session state unreadable";
                return false;
            }

            if (snapshot.State != 0 && snapshot.State != 1)
            {
                reason = snapshot.State == 2
                    ? "session state is Expired"
                    : "session state is Unknown(" + snapshot.State.ToString(CultureInfo.InvariantCulture) + ")";
                return false;
            }

            if (!snapshot.PidReadOk || snapshot.Pid <= 0)
            {
                reason = "session process id unavailable";
                return false;
            }

            if (!snapshot.HasInstanceId)
            {
                reason = "session instance id missing";
                return false;
            }

            if (!snapshot.MuteReadOk)
            {
                reason = "mute state unreadable";
                return false;
            }

            if (!snapshot.VolumeReadOk)
            {
                reason = "volume unreadable";
                return false;
            }

            reason = null;
            return true;
        }
    }

    internal sealed class EndpointInfo
    {
        public string DeviceId;
        public string Error;
        public List<AudioSession> Sessions = new List<AudioSession>();

        public void AddError(string message)
        {
            this.Error = string.IsNullOrEmpty(this.Error) ? message : this.Error + "; " + message;
        }
    }

    internal static class Audio
    {
        public static List<AudioSession> EnumerateAll(out List<EndpointInfo> endpoints)
        {
            endpoints = new List<EndpointInfo>();
            List<AudioSession> all = new List<AudioSession>();

            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.ACTIVE, out collection);
                if (hr != 0)
                {
                    throw new AudioKeysException("EnumAudioEndpoints failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture), ExitCodes.EnumerationFailed);
                }

                int endpointCount;
                hr = collection.GetCount(out endpointCount);
                if (hr != 0)
                {
                    throw new AudioKeysException("IMMDeviceCollection.GetCount failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture), ExitCodes.EnumerationFailed);
                }

                for (int i = 0; i < endpointCount; i++)
                {
                    EndpointInfo endpoint = new EndpointInfo();
                    IMMDevice device = null;
                    try
                    {
                        hr = collection.Item(i, out device);
                        if (hr != 0)
                        {
                            endpoint.AddError("IMMDeviceCollection.Item(" + i.ToString(CultureInfo.InvariantCulture) + ") failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            string deviceId;
                            hr = device.GetId(out deviceId);
                            if (hr == 0)
                            {
                                endpoint.DeviceId = deviceId;
                            }
                            else
                            {
                                endpoint.AddError("IMMDevice.GetId failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                            }

                            EnumerateEndpoint(device, endpoint);
                        }
                    }
                    catch (Exception ex)
                    {
                        endpoint.AddError("endpoint " + i.ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
                    }
                    finally
                    {
                        Release(device);
                    }

                    endpoints.Add(endpoint);
                    all.AddRange(endpoint.Sessions);
                }
            }
            finally
            {
                Release(collection);
                Release(enumerator);
            }

            return all;
        }

        private static void EnumerateEndpoint(IMMDevice device, EndpointInfo endpoint)
        {
            Guid iid = typeof(IAudioSessionManager2).GUID;
            IntPtr managerPointer;
            int hr = device.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out managerPointer);
            if (hr != 0)
            {
                endpoint.AddError("IMMDevice.Activate(IAudioSessionManager2) failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                return;
            }

            IAudioSessionManager2 manager = null;
            IAudioSessionEnumerator sessionEnumerator = null;
            try
            {
                manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(managerPointer);

                hr = manager.GetSessionEnumerator(out sessionEnumerator);
                if (hr != 0)
                {
                    endpoint.AddError("GetSessionEnumerator failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                    return;
                }

                int count;
                hr = sessionEnumerator.GetCount(out count);
                if (hr != 0)
                {
                    endpoint.AddError("IAudioSessionEnumerator.GetCount failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control = null;
                    bool ownershipTransferred = false;
                    try
                    {
                        hr = sessionEnumerator.GetSession(i, out control);
                        if (hr != 0)
                        {
                            endpoint.AddError("GetSession(" + i.ToString(CultureInfo.InvariantCulture) + ") failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                            continue;
                        }

                        AudioSession session = ReadSession(control, endpoint.DeviceId);
                        if (session == null)
                        {
                            endpoint.AddError("session " + i.ToString(CultureInfo.InvariantCulture) + " is not IAudioSessionControl2");
                        }
                        else
                        {
                            // Expired sessions are listed for diagnostics; target selection skips
                            // state 2. Ownership of the control passes to endpoint.Sessions and the
                            // caller releases it once through Audio.ReleaseSessions.
                            endpoint.Sessions.Add(session);
                            ownershipTransferred = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        endpoint.AddError("session " + i.ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
                    }
                    finally
                    {
                        // Release exactly once when the session was not stored (ReadSession threw
                        // or returned null). Interface aliases share one RCW, so this single
                        // release is the only one for an unstored control.
                        if (!ownershipTransferred)
                        {
                            Release(control);
                        }
                    }
                }
            }
            finally
            {
                Release(sessionEnumerator);
                Release(manager);
                // Balance the reference Activate handed to us. The RCW created by
                // GetObjectForIUnknown holds its own reference, released above.
                Marshal.Release(managerPointer);
            }
        }

        private static AudioSession ReadSession(IAudioSessionControl control, string endpointId)
        {
            IAudioSessionControl2 control2 = control as IAudioSessionControl2;
            if (control2 == null)
            {
                return null;
            }

            AudioSession session = new AudioSession();
            session.Control = control2;
            session.EndpointId = endpointId;
            session.VolumeControl = control as ISimpleAudioVolume;

            List<string> errors = new List<string>();

            int state;
            int hr = control2.GetState(out state);
            if (hr == 0)
            {
                session.State = state;
                session.StateReadOk = true;
            }
            else
            {
                session.State = -1;
                session.StateReadOk = false;
                errors.Add("GetState 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
            }

            int processId;
            hr = control2.GetProcessId(out processId);
            if (hr == 0)
            {
                session.Pid = processId;
                session.PidReadOk = true;
            }
            else
            {
                session.Pid = 0;
                if (hr == unchecked((int)0x0889000D))
                {
                    // AUDCLNT_S_NO_SINGLE_PROCESS: a success HRESULT, but the session
                    // cannot be attributed to one process, so it is never a target.
                    session.CrossProcess = true;
                    errors.Add("GetProcessId -> AUDCLNT_S_NO_SINGLE_PROCESS (cross-process session)");
                }
                else
                {
                    errors.Add("GetProcessId 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                }
            }

            session.SystemSounds = control2.IsSystemSoundsSession() == 0;

            string instanceId;
            hr = control2.GetSessionInstanceIdentifier(out instanceId);
            if (hr == 0)
            {
                session.InstanceId = instanceId;
            }
            else
            {
                errors.Add("GetSessionInstanceIdentifier 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
            }

            if (session.VolumeControl == null)
            {
                errors.Add("ISimpleAudioVolume not available");
            }
            else
            {
                bool mute;
                hr = session.VolumeControl.GetMute(out mute);
                if (hr == 0)
                {
                    session.Muted = mute;
                    session.MuteReadOk = true;
                }
                else
                {
                    errors.Add("GetMute 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                }

                float volume;
                hr = session.VolumeControl.GetMasterVolume(out volume);
                if (hr == 0)
                {
                    session.Volume = volume;
                    session.VolumeReadOk = true;
                }
                else
                {
                    errors.Add("GetMasterVolume 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                }
            }

            session.OrigMuted = session.Muted;
            session.OrigVolume = session.Volume;

            if (errors.Count > 0)
            {
                session.ReadError = string.Join("; ", errors.ToArray());
            }

            return session;
        }

        public static void ReleaseSessions(IList<AudioSession> sessions)
        {
            if (sessions == null)
            {
                return;
            }

            for (int i = 0; i < sessions.Count; i++)
            {
                // IAudioSessionControl2 and ISimpleAudioVolume are aliases of the same RCW,
                // so release the RCW exactly once through one of them.
                Release(sessions[i].Control);
                sessions[i].Control = null;
                sessions[i].VolumeControl = null;
            }
        }

        public static void Release(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
            catch (Exception)
            {
                // Nothing useful to do; the RCW will be finalized.
            }
        }
    }

    #endregion
}