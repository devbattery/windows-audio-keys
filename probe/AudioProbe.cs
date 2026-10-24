// AudioProbe - minimal feasibility probe for muting ONLY the foreground
// application's audio sessions (no registration list, no injection, no hooks).
//
// Modes:
//   --list                                read-only session dump (JSON)
//   --test-foreground --expect-pid NUMBER  mutating probe against the foreground PID
//   --self-test                           pure logic assertions, no audio changes
//
// Built with the in-box .NET Framework 4.x csc.exe (C# 5). No dependencies.
// Only ISimpleAudioVolume per session is used; endpoint/master volume is never touched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading;

namespace AudioProbe
{

    using GameAudioKeys.Core;

    #region JSON output

    // System.Web.Extensions' JavaScriptSerializer is used instead of a hand-written
    // serializer; build.ps1 adds the reference (in-box assembly, no new dependency).
    internal static class Json
    {
        public static string Serialize(object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 200;
            return serializer.Serialize(value);
        }
    }

    #endregion

    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int NoSession = 2;
        public const int Refused = 3;
        public const int EnumerationFailed = 4;
        public const int Usage = 64;
    }

    internal static class Program
    {
        private const int HoldMilliseconds = 2000;
        private const float VolumeEpsilon = 0.001f;

        private static readonly string[] Limitations = new string[]
        {
            "Mute/volume apply to the whole audio session of a PID (or a matching descendant), never per browser tab.",
            "Descendant sessions are selected only when the executable path matches the foreground image (Chromium-style helpers). Helpers running a different executable are unsupported.",
            "Exclusive-mode audio and some brokered/remoted sessions may not expose a usable ISimpleAudioVolume session.",
            "Sessions of other users, elevated processes, or protected processes may be invisible or fail to read; access errors are reported, never retried with elevation.",
            "A target whose state, mute, volume or instance id cannot be read exactly is never muted (fail closed), and one such session aborts the whole test.",
            "Device or process changes during a test surface as per-session failures; nothing falls back to endpoint/master volume.",
            "The foreground PID is re-checked immediately before muting, but focus can still change afterwards."
        };

        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
                // Redirected/conhost-less output may refuse an encoding change; default is fine.
            }

            try
            {
                if (args == null || args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
                {
                    WriteUsage();
                    return ExitCodes.Ok;
                }

                switch (args[0])
                {
                    case "--list":
                        return RunList();
                    case "--test-foreground":
                        return RunTestForeground(args);
                    case "--self-test":
                        return RunSelfTest();
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[0]);
                        WriteUsage();
                        return ExitCodes.Usage;
                }
            }
            catch (AudioKeysException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return ex.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("UNEXPECTED: " + ex);
                return ExitCodes.Failed;
            }
        }

        private static void WriteUsage()
        {
            Console.Error.WriteLine("AudioProbe - foreground-application audio probe (read-only diagnostics + one manual mute test)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  AudioProbe.exe --list");
            Console.Error.WriteLine("      Read-only JSON dump: foreground window/process and every active render");
            Console.Error.WriteLine("      endpoint audio session (instance id, PID, path, parent PID, state, mute, volume).");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  AudioProbe.exe --test-foreground --expect-pid NUMBER");
            Console.Error.WriteLine("      Refuses unless GetForegroundWindow PID == NUMBER. Mutes only that process's");
            Console.Error.WriteLine("      sessions (and same-image descendants), holds " + HoldMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms,");
            Console.Error.WriteLine("      restores the exact previous mute state and reports evidence. Volume is never changed.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  AudioProbe.exe --self-test");
            Console.Error.WriteLine("      Pure logic assertions (matching, cycles, recycled PIDs, empty match).");
            Console.Error.WriteLine("      Does not touch audio.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Exit codes: 0 ok, 1 failure, 2 no matching session, 3 refused (foreground), 4 enumeration error, 64 usage.");
        }

        private static Dictionary<string, object> NewResult(string mode)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["tool"] = "AudioProbe";
            result["mode"] = mode;
            result["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            return result;
        }

        private static List<object> ToObjects(IEnumerable<string> items)
        {
            List<object> list = new List<object>();
            if (items != null)
            {
                foreach (string item in items)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        private static void AddLimitations(Dictionary<string, object> result)
        {
            result["limitations"] = ToObjects(Limitations);
        }

        private static string StateName(int state)
        {
            switch (state)
            {
                case 0:
                    return "Inactive";
                case 1:
                    return "Active";
                case 2:
                    return "Expired";
                default:
                    return "Unknown(" + state.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static Dictionary<string, object> DescribeForeground(ProcTable table)
        {
            IntPtr window = Native.GetForegroundWindow();
            int pid = 0;
            if (window != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(window, out pid);
            }

            Dictionary<string, object> description = new Dictionary<string, object>();
            description["hwnd"] = window == IntPtr.Zero ? null : ("0x" + window.ToInt64().ToString("X", CultureInfo.InvariantCulture));
            description["pid"] = pid;
            DescribeProcess(description, table, pid);
            return description;
        }

        private static void DescribeProcess(Dictionary<string, object> target, ProcTable table, int pid)
        {
            ProcEntry entry;
            if (pid > 0 && table != null && table.TryGet(pid, out entry))
            {
                target["processName"] = entry.ExeName;
                target["parentPid"] = entry.ParentPid;
            }
            else
            {
                target["processName"] = null;
                target["parentPid"] = null;
            }

            if (pid <= 0)
            {
                target["processPath"] = null;
                target["processPathError"] = "invalid pid";
                return;
            }

            string error;
            string path = table.GetPath(pid, out error);
            target["processPath"] = path;
            if (path == null)
            {
                target["processPathError"] = error;
            }
        }

        private static Dictionary<string, object> DescribeSession(AudioSession session, ProcTable table)
        {
            Dictionary<string, object> description = new Dictionary<string, object>();
            description["instanceId"] = session.InstanceId;
            description["pid"] = session.Pid;
            DescribeProcess(description, table, session.Pid);
            description["state"] = StateName(session.State);
            description["mute"] = session.MuteReadOk ? (object)session.Muted : null;
            description["volume"] = session.VolumeReadOk ? (object)session.Volume : null;
            description["systemSounds"] = session.SystemSounds;
            description["crossProcess"] = session.CrossProcess;
            description["selected"] = session.Selected;

            if (session.MatchReason != null)
            {
                description["targetReason"] = session.MatchReason;
            }

            if (session.MatchVerification != null)
            {
                description["targetVerification"] = session.MatchVerification;
            }

            if (session.ReadError != null)
            {
                description["readError"] = session.ReadError;
            }

            if (session.Selected || session.AfterMuteReadOk || session.DuringMuteReadOk || session.DuringVolumeReadOk)
            {
                description["before"] = Snapshot(session.MuteReadOk, session.Muted, session.VolumeReadOk, session.Volume);
                if (session.DuringMuteReadOk || session.DuringVolumeReadOk)
                {
                    description["during"] = Snapshot(session.DuringMuteReadOk, session.DuringMuted, session.DuringVolumeReadOk, session.DuringVolume);
                }

                description["after"] = Snapshot(session.AfterMuteReadOk, session.AfterMuted, session.AfterVolumeReadOk, session.AfterVolume);

                bool muteRestored = session.AfterMuteReadOk && session.AfterMuted == session.OrigMuted;
                bool volumeSame = session.AfterVolumeReadOk
                    && Math.Abs(session.AfterVolume - session.OrigVolume) <= VolumeEpsilon;
                if (session.DuringVolumeReadOk)
                {
                    volumeSame = volumeSame && Math.Abs(session.DuringVolume - session.OrigVolume) <= VolumeEpsilon;
                }

                description["muteRestored"] = muteRestored;
                description["volumeUnchanged"] = volumeSame;
            }

            return description;
        }

        private static Dictionary<string, object> Snapshot(bool muteOk, bool mute, bool volumeOk, float volume)
        {
            Dictionary<string, object> snapshot = new Dictionary<string, object>();
            snapshot["mute"] = muteOk ? (object)mute : null;
            snapshot["volume"] = volumeOk ? (object)volume : null;
            return snapshot;
        }

        private static List<object> DescribeEndpoints(List<EndpointInfo> endpoints, ProcTable table)
        {
            List<object> list = new List<object>();
            foreach (EndpointInfo endpoint in endpoints)
            {
                Dictionary<string, object> description = new Dictionary<string, object>();
                description["deviceId"] = endpoint.DeviceId;
                description["sessionCount"] = endpoint.Sessions.Count;
                if (endpoint.Error != null)
                {
                    description["error"] = endpoint.Error;
                }

                List<object> sessions = new List<object>();
                foreach (AudioSession session in endpoint.Sessions)
                {
                    sessions.Add(DescribeSession(session, table));
                }

                description["sessions"] = sessions;
                list.Add(description);
            }

            return list;
        }

        private static int RunList()
        {
            Dictionary<string, object> result = NewResult("list");
            AddLimitations(result);

            ProcTable table = ProcTable.Snapshot();
            result["foreground"] = DescribeForeground(table);

            List<EndpointInfo> endpoints;
            List<AudioSession> sessions = null;
            try
            {
                sessions = Audio.EnumerateAll(out endpoints);
                result["endpointCount"] = endpoints.Count;
                result["sessionCount"] = sessions.Count;
                result["endpoints"] = DescribeEndpoints(endpoints, table);
                result["status"] = "ok";
            }
            catch (AudioKeysException ex)
            {
                result["status"] = "enumeration-failed";
                result["error"] = ex.Message;
                result["endpoints"] = new List<object>();
                Console.Out.WriteLine(Json.Serialize(result));
                return ex.ExitCode;
            }
            finally
            {
                Audio.ReleaseSessions(sessions);
            }

            Console.Out.WriteLine(Json.Serialize(result));
            return ExitCodes.Ok;
        }

        private static bool TryParseExpectPid(string[] args, out int expectPid, out string error)
        {
            expectPid = 0;
            error = null;
            bool seen = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--expect-pid")
                {
                    if (i + 1 >= args.Length)
                    {
                        error = "--expect-pid requires a number";
                        return false;
                    }

                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectPid) || expectPid <= 0)
                    {
                        error = "--expect-pid must be a positive PID";
                        return false;
                    }

                    seen = true;
                    i++;
                }
                else
                {
                    error = "Unknown argument: " + args[i];
                    return false;
                }
            }

            if (!seen)
            {
                error = "--test-foreground requires --expect-pid NUMBER";
                return false;
            }

            return true;
        }

        private static int RunTestForeground(string[] args)
        {
            int expectPid;
            string parseError;
            if (!TryParseExpectPid(args, out expectPid, out parseError))
            {
                Console.Error.WriteLine("ERROR: " + parseError);
                WriteUsage();
                return ExitCodes.Usage;
            }

            Dictionary<string, object> result = NewResult("test-foreground");
            AddLimitations(result);
            result["expectedPid"] = expectPid;
            result["holdMilliseconds"] = HoldMilliseconds;

            List<string> errors = new List<string>();
            ProcTable table;
            try
            {
                table = ProcTable.Snapshot();
            }
            catch (Exception ex)
            {
                result["status"] = "failed";
                errors.Add("process snapshot failed: " + ex.Message);
                result["errors"] = ToObjects(errors);
                Console.Out.WriteLine(Json.Serialize(result));
                return ExitCodes.EnumerationFailed;
            }

            Dictionary<string, object> foreground = DescribeForeground(table);
            result["foreground"] = foreground;
            int foregroundPid = (int)foreground["pid"];

            if (foregroundPid == 0)
            {
                result["status"] = "refused";
                result["reason"] = "no-foreground-window";
                result["muteSucceeded"] = false;
                result["restoreSucceeded"] = null;
                result["nonTargetSessionsUnchanged"] = null;
                errors.Add("GetForegroundWindow returned no window; nothing was changed.");
                result["errors"] = ToObjects(errors);
                Console.Out.WriteLine(Json.Serialize(result));
                return ExitCodes.Refused;
            }

            if (foregroundPid != expectPid)
            {
                result["status"] = "refused";
                result["reason"] = "foreground-pid-mismatch";
                result["actualForegroundPid"] = foregroundPid;
                result["muteSucceeded"] = false;
                result["restoreSucceeded"] = null;
                result["nonTargetSessionsUnchanged"] = null;
                errors.Add("Foreground PID " + foregroundPid.ToString(CultureInfo.InvariantCulture)
                    + " does not equal --expect-pid " + expectPid.ToString(CultureInfo.InvariantCulture) + "; nothing was changed.");
                result["errors"] = ToObjects(errors);
                Console.Out.WriteLine(Json.Serialize(result));
                return ExitCodes.Refused;
            }

            List<EndpointInfo> endpoints;
            List<AudioSession> sessions = null;
            try
            {
                try
                {
                    sessions = Audio.EnumerateAll(out endpoints);
                }
                catch (AudioKeysException ex)
                {
                    result["status"] = "failed";
                    errors.Add("audio enumeration failed: " + ex.Message);
                    result["errors"] = ToObjects(errors);
                    Console.Out.WriteLine(Json.Serialize(result));
                    return ex.ExitCode;
                }

                List<AudioSession> selected = Targeting.SelectSessions(table, foregroundPid, sessions);

                result["targetSessionCount"] = selected.Count;
                result["targetSessionStatus"] = Targeting.ResultStatus(selected.Count);

                Dictionary<int, bool> targetPids = new Dictionary<int, bool>();
                foreach (AudioSession session in selected)
                {
                    targetPids[session.Pid] = true;
                }

                List<object> pidList = new List<object>();
                foreach (int pid in targetPids.Keys)
                {
                    Dictionary<string, object> pidEntry = new Dictionary<string, object>();
                    pidEntry["pid"] = pid;
                    DescribeProcess(pidEntry, table, pid);
                    pidList.Add(pidEntry);
                }

                result["targetPids"] = pidList;

                if (selected.Count == 0)
                {
                    result["endpointCount"] = endpoints.Count;
                    result["sessionCount"] = sessions.Count;
                    result["endpoints"] = DescribeEndpoints(endpoints, table);
                    result["status"] = "no-session";
                    result["muteSucceeded"] = false;
                    result["restoreSucceeded"] = null;
                    result["nonTargetSessionsUnchanged"] = true;
                    errors.Add("No render audio session belongs to foreground PID "
                        + foregroundPid.ToString(CultureInfo.InvariantCulture)
                        + " or a verified descendant. Unsupported/absent session; endpoint/master volume is never used instead.");
                    result["errors"] = ToObjects(errors);
                    Console.Out.WriteLine(Json.Serialize(result));
                    return ExitCodes.NoSession;
                }

                // Preflight: refresh every selected original immediately before mutating and
                // fail closed. A session whose state/mute/volume/instance id cannot be read
                // exactly is never muted, and one bad session aborts the whole test instead
                // of silently turning the run into a partial success.
                List<string> preflightErrors = new List<string>();
                foreach (AudioSession session in selected)
                {
                    string refreshError;
                    if (!session.TryRefreshOriginals(out refreshError))
                    {
                        preflightErrors.Add(Label(session) + ": " + refreshError);
                    }
                }

                if (preflightErrors.Count > 0)
                {
                    result["targetSessionStatus"] = "preflight-failed";
                    result["muteSucceeded"] = false;
                    result["restoreSucceeded"] = null;
                    result["mutedSessionCount"] = 0;
                    result["nonTargetSessionsUnchanged"] = null;
                    result["endpointCount"] = endpoints.Count;
                    result["sessionCount"] = sessions.Count;
                    result["endpoints"] = DescribeEndpoints(endpoints, table);
                    errors.AddRange(preflightErrors);
                    errors.Add("Preflight failed for " + preflightErrors.Count.ToString(CultureInfo.InvariantCulture)
                        + " selected session(s); no session was muted (fail closed).");
                    result["errors"] = ToObjects(errors);
                    Console.Out.WriteLine(Json.Serialize(result));
                    return ExitCodes.Failed;
                }

                // Focus can change between classification and mutation; refuse rather than
                // muting a process the operator did not intend.
                IntPtr windowNow = Native.GetForegroundWindow();
                int foregroundPidNow = 0;
                if (windowNow != IntPtr.Zero)
                {
                    Native.GetWindowThreadProcessId(windowNow, out foregroundPidNow);
                }

                if (foregroundPidNow != expectPid)
                {
                    result["status"] = "aborted";
                    result["reason"] = "foreground-changed-before-mute";
                    result["actualForegroundPid"] = foregroundPidNow;
                    result["muteSucceeded"] = false;
                    result["restoreSucceeded"] = null;
                    result["nonTargetSessionsUnchanged"] = true;
                    errors.Add("Foreground PID changed to " + foregroundPidNow.ToString(CultureInfo.InvariantCulture)
                        + " before muting; aborted without changing audio.");
                    result["errors"] = ToObjects(errors);
                    Console.Out.WriteLine(Json.Serialize(result));
                    return ExitCodes.Refused;
                }

                bool muteConfirmed = true;
                bool restoreConfirmed = true;
                bool targetVolumeUnchanged = true;
                bool nonTargetUnchanged = true;
                int mutedCount = 0;

                try
                {
                    foreach (AudioSession session in selected)
                    {
                        int hr = session.TrySetMute(true);
                        if (hr != 0)
                        {
                            muteConfirmed = false;
                            errors.Add("SetMute(true) failed for " + Label(session) + ": 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            mutedCount++;
                        }
                    }

                    foreach (AudioSession session in selected)
                    {
                        bool mute;
                        bool muteOk = session.TryReadMute(out mute);
                        session.DuringMuteReadOk = muteOk;
                        session.DuringMuted = mute;
                        if (!muteOk)
                        {
                            muteConfirmed = false;
                            errors.Add("GetMute failed during hold for " + Label(session));
                        }
                        else if (!mute)
                        {
                            muteConfirmed = false;
                            errors.Add("Session reported unmuted during hold: " + Label(session));
                        }
                    }

                    Thread.Sleep(HoldMilliseconds);

                    // Targets must still be muted with unchanged volume; non-target sessions
                    // are verified DURING the hold as well as after restore.
                    foreach (AudioSession session in sessions)
                    {
                        bool mute;
                        bool muteOk = session.TryReadMute(out mute);
                        session.DuringMuteReadOk = muteOk;
                        session.DuringMuted = mute;
                        float volume;
                        bool volumeOk = session.TryReadVolume(out volume);
                        session.DuringVolumeReadOk = volumeOk;
                        session.DuringVolume = volume;

                        if (session.Selected)
                        {
                            if (!muteOk)
                            {
                                muteConfirmed = false;
                                errors.Add("GetMute failed during hold for " + Label(session));
                            }
                            else if (!mute)
                            {
                                muteConfirmed = false;
                                errors.Add("Mute not held after " + HoldMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms for " + Label(session));
                            }

                            if (!volumeOk)
                            {
                                targetVolumeUnchanged = false;
                                errors.Add("GetMasterVolume failed during hold for " + Label(session));
                            }
                            else if (Math.Abs(volume - session.OrigVolume) > VolumeEpsilon)
                            {
                                targetVolumeUnchanged = false;
                                errors.Add("Volume changed on target session " + Label(session));
                            }
                        }
                        else if (!muteOk || !volumeOk)
                        {
                            nonTargetUnchanged = false;
                            errors.Add("Non-target session became unreadable during hold: " + Label(session));
                        }
                        else if (mute != session.OrigMuted || Math.Abs(volume - session.OrigVolume) > VolumeEpsilon)
                        {
                            nonTargetUnchanged = false;
                            errors.Add("Non-target session changed during hold: " + Label(session)
                                + " (mute " + session.OrigMuted.ToString() + "->" + mute.ToString()
                                + ", volume " + session.OrigVolume.ToString("R", CultureInfo.InvariantCulture)
                                + "->" + volume.ToString("R", CultureInfo.InvariantCulture) + ")");
                        }
                    }
                }
                finally
                {
                    // Restore always runs, and every session is attempted even if one fails.
                    foreach (AudioSession session in selected)
                    {
                        int hr = session.TrySetMute(session.OrigMuted);
                        if (hr != 0)
                        {
                            restoreConfirmed = false;
                            errors.Add("Restore SetMute(" + session.OrigMuted.ToString() + ") failed for " + Label(session)
                                + ": 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                        }
                    }

                    foreach (AudioSession session in selected)
                    {
                        bool mute;
                        bool muteOk = session.TryReadMute(out mute);
                        session.AfterMuteReadOk = muteOk;
                        session.AfterMuted = mute;
                        if (!muteOk)
                        {
                            restoreConfirmed = false;
                            errors.Add("GetMute failed after restore for " + Label(session));
                        }
                        else if (mute != session.OrigMuted)
                        {
                            restoreConfirmed = false;
                            errors.Add("Mute state not restored for " + Label(session)
                                + " (expected " + session.OrigMuted.ToString() + ", got " + mute.ToString() + ")");
                        }

                        float volume;
                        bool volumeOk = session.TryReadVolume(out volume);
                        session.AfterVolumeReadOk = volumeOk;
                        session.AfterVolume = volume;
                        if (!volumeOk)
                        {
                            targetVolumeUnchanged = false;
                            errors.Add("GetMasterVolume failed after restore for " + Label(session));
                        }
                        else if (Math.Abs(volume - session.OrigVolume) > VolumeEpsilon)
                        {
                            targetVolumeUnchanged = false;
                            errors.Add("Volume changed on target session after restore " + Label(session));
                        }
                    }

                    foreach (AudioSession session in sessions)
                    {
                        if (session.Selected)
                        {
                            continue;
                        }

                        bool mute;
                        bool muteOk = session.TryReadMute(out mute);
                        session.AfterMuteReadOk = muteOk;
                        session.AfterMuted = mute;
                        float volume;
                        bool volumeOk = session.TryReadVolume(out volume);
                        session.AfterVolumeReadOk = volumeOk;
                        session.AfterVolume = volume;

                        if (!muteOk || !volumeOk)
                        {
                            nonTargetUnchanged = false;
                            errors.Add("Non-target session became unreadable: " + Label(session));
                        }
                        else if (mute != session.OrigMuted || Math.Abs(volume - session.OrigVolume) > VolumeEpsilon)
                        {
                            nonTargetUnchanged = false;
                            errors.Add("Non-target session changed during the test: " + Label(session)
                                + " (mute " + session.OrigMuted.ToString() + "->" + mute.ToString()
                                + ", volume " + session.OrigVolume.ToString("R", CultureInfo.InvariantCulture)
                                + "->" + volume.ToString("R", CultureInfo.InvariantCulture) + ")");
                        }
                    }
                }

                bool success = muteConfirmed && restoreConfirmed && targetVolumeUnchanged && nonTargetUnchanged && errors.Count == 0;

                result["endpointCount"] = endpoints.Count;
                result["sessionCount"] = sessions.Count;
                result["endpoints"] = DescribeEndpoints(endpoints, table);
                result["mutedSessionCount"] = mutedCount;
                result["muteSucceeded"] = muteConfirmed;
                result["restoreSucceeded"] = restoreConfirmed;
                result["targetVolumeUnchanged"] = targetVolumeUnchanged;
                result["nonTargetSessionsUnchanged"] = nonTargetUnchanged;
                result["status"] = success ? "ok" : "failed";
                result["errors"] = ToObjects(errors);

                Console.Out.WriteLine(Json.Serialize(result));
                return success ? ExitCodes.Ok : ExitCodes.Failed;
            }
            finally
            {
                Audio.ReleaseSessions(sessions);
            }
        }

        private static string Label(AudioSession session)
        {
            string instance = string.IsNullOrEmpty(session.InstanceId) ? "<no-instance-id>" : session.InstanceId;
            return "pid=" + session.Pid.ToString(CultureInfo.InvariantCulture) + " instance=" + instance;
        }

        private static int RunSelfTest()
        {
            List<object> tests = new List<object>();
            int failed = 0;

            // Foreground 100 (game.exe) -> child 200 -> grandchild 300, same image.
            ProcTable graph = new ProcTable();
            graph.AddSynthetic(100, 1, "game.exe", true, 1000, @"C:\Games\game\game.exe");
            graph.AddSynthetic(200, 100, "game.exe", true, 2000, @"C:\Games\game\game.exe");
            graph.AddSynthetic(300, 200, "game.exe", true, 3000, @"C:\Games\game\game.exe");
            // Unrelated process 400 under launcher 500.
            graph.AddSynthetic(400, 500, "music.exe", true, 4000, @"C:\Media\music.exe");
            graph.AddSynthetic(500, 1, "launcher.exe", true, 500, @"C:\Launcher\launcher.exe");
            // Children of the foreground app that must NOT be treated as targets.
            graph.AddSynthetic(600, 100, "cmd.exe", true, 6000, @"C:\Windows\System32\cmd.exe");
            graph.AddSynthetic(601, 100, "helper.exe", true, 7000, null);

            // Chromium-style browser: helper shares the browser image; a portable copy with
            // the same file name but another path must not match.
            ProcTable chrome = new ProcTable();
            chrome.AddSynthetic(800, 1, "chrome.exe", true, 1000, @"C:\Program Files\Google\Chrome\Application\chrome.exe");
            chrome.AddSynthetic(801, 800, "chrome.exe", true, 2000, @"C:\Program Files\Google\Chrome\Application\chrome.exe");
            chrome.AddSynthetic(802, 800, "chrome.exe", true, 3000, @"D:\Portable\chrome.exe");

            string reason;
            string verification;

            Check(tests, ref failed, "descendant-child-selected",
                Targeting.IsTarget(graph, 100, 200, out reason, out verification),
                "child of foreground must be a target (reason=" + reason + ", verification=" + verification + ")");

            Check(tests, ref failed, "descendant-grandchild-selected",
                Targeting.IsTarget(graph, 100, 300, out reason, out verification),
                "grandchild (Chromium audio utility style) must be a target (reason=" + reason + ")");

            Check(tests, ref failed, "self-selected",
                Targeting.IsTarget(graph, 100, 100, out reason, out verification),
                "exact foreground PID must be a target (reason=" + reason + ")");

            Check(tests, ref failed, "unrelated-sibling-excluded",
                !Targeting.IsTarget(graph, 100, 400, out reason, out verification),
                "unrelated process must not be a target (reason=" + reason + ")");

            Check(tests, ref failed, "ancestor-launcher-not-followed",
                !Targeting.IsTarget(graph, 300, 200, out reason, out verification)
                    && !Targeting.IsTarget(graph, 300, 100, out reason, out verification),
                "ancestors of the foreground PID must never be followed");

            Check(tests, ref failed, "same-path-descendant-selected",
                Targeting.IsTarget(chrome, 800, 801, out reason, out verification) && reason == "descendant",
                "Chromium-style helper sharing the browser image must be a target (reason=" + reason + ")");

            Check(tests, ref failed, "different-executable-child-rejected",
                !Targeting.IsTarget(graph, 100, 600, out reason, out verification) && reason == "different-executable",
                "child running another image must be rejected (reason=" + reason + ")");

            Check(tests, ref failed, "same-basename-different-path-rejected",
                !Targeting.IsTarget(chrome, 800, 802, out reason, out verification) && reason == "different-executable",
                "same file name at another path must be rejected (reason=" + reason + ")");

            Check(tests, ref failed, "unknown-path-descendant-rejected",
                !Targeting.IsTarget(graph, 100, 601, out reason, out verification) && reason == "unknown-path",
                "descendant with unreadable image path must fail closed (reason=" + reason + ")");

            ProcTable cycle = new ProcTable();
            cycle.AddSynthetic(700, 800, "a.exe", true, 1000);
            cycle.AddSynthetic(800, 700, "b.exe", true, 2000);
            bool cycleResult = Targeting.IsDescendant(cycle, 700, 900, out verification);
            Check(tests, ref failed, "cycle-protection",
                !cycleResult && verification == "cycle",
                "cyclic parent links must stop with cycle (verification=" + verification + ")");

            ProcTable recycled = new ProcTable();
            recycled.AddSynthetic(4000, 1, "fg.exe", true, 100000);
            recycled.AddSynthetic(4001, 4000, "reused.exe", true, 50000);
            bool recycledResult = Targeting.IsDescendant(recycled, 4001, 4000, out verification);
            Check(tests, ref failed, "recycled-pid-rejected",
                !recycledResult && verification == "recycled-pid",
                "child older than claimed parent must be rejected (verification=" + verification + ")");

            Check(tests, ref failed, "zero-pid-excluded",
                !Targeting.IsTarget(graph, 100, 0, out reason, out verification)
                    && !Targeting.IsTarget(graph, 0, 100, out reason, out verification),
                "PID 0 and missing foreground PID must never match");

            Check(tests, ref failed, "missing-process-excluded",
                !Targeting.IsTarget(graph, 100, 999, out reason, out verification),
                "session PID absent from the snapshot must not match (reason=" + reason + ")");

            List<int> emptySelection = Targeting.SelectTargets(graph, 100, new int[0]);
            Check(tests, ref failed, "empty-audio-match-no-session",
                emptySelection.Count == 0 && Targeting.ResultStatus(0) == "no-session",
                "no session means explicit no-session status, never a master-volume fallback");

            List<int> unrelatedOnly = Targeting.SelectTargets(graph, 100, new int[] { 400, 500, 999 });
            Check(tests, ref failed, "unrelated-audio-match-no-session",
                unrelatedOnly.Count == 0 && Targeting.ResultStatus(0) == "no-session",
                "sessions of other processes yield zero targets");

            List<int> mixed = Targeting.SelectTargets(graph, 100, new int[] { 200, 400, 0, 300 });
            Check(tests, ref failed, "mixed-audio-match-exact",
                mixed.Count == 2 && mixed[0] == 0 && mixed[1] == 3,
                "only the foreground descendant sessions are selected");

            ProcTable recycledChain = new ProcTable();
            recycledChain.AddSynthetic(6000, 1, "fg.exe", true, 100000, @"C:\App\fg.exe");
            recycledChain.AddSynthetic(6002, 6000, "fg.exe", true, 50000, @"C:\App\fg.exe");
            recycledChain.AddSynthetic(6001, 6002, "fg.exe", true, 60000, @"C:\App\fg.exe");
            bool recycledChainResult = Targeting.IsDescendant(recycledChain, 6001, 6000, out verification);
            Check(tests, ref failed, "recycled-intermediate-parent-rejected",
                !recycledChainResult && verification == "recycled-pid",
                "a recycled middle link must reject the whole chain (verification=" + verification + ")");

            ProcTable unknownCreation = new ProcTable();
            unknownCreation.AddSynthetic(5000, 1, "fg.exe", true, 1000, @"C:\App\fg.exe");
            unknownCreation.AddSynthetic(5001, 5000, "fg.exe", false, 0, @"C:\App\fg.exe");
            bool unknownResult = Targeting.IsDescendant(unknownCreation, 5001, 5000, out verification);
            Check(tests, ref failed, "unknown-creation-time-rejected",
                !unknownResult && verification == "unknown-creation-time",
                "unknown creation time must fail closed (verification=" + verification + ")");

            SessionSnapshot valid = new SessionSnapshot();
            valid.HasControl = true;
            valid.HasVolumeControl = true;
            valid.StateReadOk = true;
            valid.State = 1;
            valid.PidReadOk = true;
            valid.Pid = 100;
            valid.HasInstanceId = true;
            valid.MuteReadOk = true;
            valid.VolumeReadOk = true;

            string eligibilityReason;
            Check(tests, ref failed, "preflight-eligible-session-accepted",
                Eligibility.IsEligible(valid, out eligibilityReason),
                "fully readable active session must pass preflight");

            SessionSnapshot noMute = valid;
            noMute.MuteReadOk = false;
            Check(tests, ref failed, "preflight-unreadable-mute-rejected",
                !Eligibility.IsEligible(noMute, out eligibilityReason) && eligibilityReason == "mute state unreadable",
                "unreadable mute must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot noVolume = valid;
            noVolume.VolumeReadOk = false;
            Check(tests, ref failed, "preflight-unreadable-volume-rejected",
                !Eligibility.IsEligible(noVolume, out eligibilityReason) && eligibilityReason == "volume unreadable",
                "unreadable volume must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot noState = valid;
            noState.StateReadOk = false;
            Check(tests, ref failed, "preflight-unreadable-state-rejected",
                !Eligibility.IsEligible(noState, out eligibilityReason) && eligibilityReason == "session state unreadable",
                "unreadable state must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot noInstance = valid;
            noInstance.HasInstanceId = false;
            Check(tests, ref failed, "preflight-missing-instance-id-rejected",
                !Eligibility.IsEligible(noInstance, out eligibilityReason) && eligibilityReason == "session instance id missing",
                "missing instance id must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot noInterfaces = valid;
            noInterfaces.HasControl = false;
            noInterfaces.HasVolumeControl = false;
            Check(tests, ref failed, "preflight-missing-interfaces-rejected",
                !Eligibility.IsEligible(noInterfaces, out eligibilityReason) && eligibilityReason == "audio session interfaces unavailable",
                "missing COM interfaces must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot expiredSession = valid;
            expiredSession.State = 2;
            Check(tests, ref failed, "preflight-expired-session-rejected",
                !Eligibility.IsEligible(expiredSession, out eligibilityReason) && eligibilityReason == "session state is Expired",
                "expired session must block mutation (reason=" + eligibilityReason + ")");

            SessionSnapshot noPid = valid;
            noPid.PidReadOk = false;
            Check(tests, ref failed, "preflight-missing-pid-rejected",
                !Eligibility.IsEligible(noPid, out eligibilityReason) && eligibilityReason == "session process id unavailable",
                "missing PID must block mutation (reason=" + eligibilityReason + ")");

            Dictionary<string, object> report = NewResult("self-test");
            report["testCount"] = tests.Count;
            report["failedCount"] = failed;
            report["passed"] = failed == 0;
            report["tests"] = tests;
            Console.Out.WriteLine(Json.Serialize(report));
            return failed == 0 ? ExitCodes.Ok : ExitCodes.Failed;
        }

        private static void Check(List<object> tests, ref int failed, string name, bool passed, string detail)
        {
            Dictionary<string, object> test = new Dictionary<string, object>();
            test["name"] = name;
            test["passed"] = passed;
            test["detail"] = detail;
            tests.Add(test);
            if (!passed)
            {
                failed++;
            }
        }
    }
}