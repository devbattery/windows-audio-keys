// Game Audio Keys - pure decision helpers plus the single audio action implementation
// used by WM_HOTKEY. Nothing here falls back to endpoint or master volume.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    internal enum AudioActionKind
    {
        ToggleMute,
        VolumeUp,
        VolumeDown
    }

    internal sealed class ActionOutcome
    {
        public bool Success;
        public string StatusKo;
        public int TargetSessionCount;
        public string TargetProcessName;
        public int TargetPid;
        public bool FocusChanged;
        public bool RestoreAttempted;
        public bool RestoreSucceeded;
    }

    internal static class VolumeMath
    {
        public const float Epsilon = 0.001f;

        public static float Clamp01(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        // delta is in percentage points (5 means +5%p of the 0..100 scale).
        public static float Step(float current, int deltaPercentagePoints)
        {
            return Clamp01(Clamp01(current) + (deltaPercentagePoints / 100f));
        }

        public static int ToPercent(float volume)
        {
            return (int)Math.Round(Clamp01(volume) * 100f, MidpointRounding.AwayFromZero);
        }
    }

    internal static class ToggleDecision
    {
        // If every selected session is already muted, unmute them all; otherwise mute
        // them all. No sessions means no requested change.
        public static bool ShouldMute(IList<bool> mutedStates)
        {
            if (mutedStates == null || mutedStates.Count == 0)
            {
                return false;
            }

            bool allMuted = true;
            for (int i = 0; i < mutedStates.Count; i++)
            {
                if (!mutedStates[i])
                {
                    allMuted = false;
                    break;
                }
            }

            return !allMuted;
        }
    }

    // 전체 프로세스 스냅샷(Toolhelp, 수 ms)이 꼭 필요한지 미리 판단하는 순수 함수.
    // Targeting.IsTarget은 활성 PID가 아닌 세션을 "같은 실행 파일 경로의 검증된 자손"일
    // 때만 대상으로 삼는다. 경로가 다르거나 읽을 수 없는 세션은 조상 검사 결과와 무관하게
    // 대상이 아니므로, 경로가 같은 후보가 하나라도 있을 때만 조상 검증용 스냅샷을 만든다.
    internal static class TargetPrefilter
    {
        public static bool NeedsAncestry(int foregroundPid, IList<AudioSession> sessions, Func<int, string> pathOf)
        {
            if (foregroundPid <= 0 || sessions == null || pathOf == null)
            {
                return false;
            }

            string foregroundPath = null;
            bool foregroundPathRead = false;
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSession session = sessions[i];
                if (!IsAncestryCandidate(session, foregroundPid))
                {
                    continue;
                }

                // 활성 앱 경로는 후보가 있을 때 한 번만 조회한다.
                if (!foregroundPathRead)
                {
                    foregroundPath = pathOf(foregroundPid);
                    foregroundPathRead = true;
                    if (string.IsNullOrEmpty(foregroundPath))
                    {
                        // 활성 앱 경로를 모르면 어떤 자손도 경로 일치를 통과할 수 없다.
                        return false;
                    }
                }

                string path = pathOf(session.Pid);
                if (!string.IsNullOrEmpty(path)
                    && string.Equals(path, foregroundPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // SelectSessions에서 어차피 제외되는 세션(교차 프로세스·시스템 사운드·만료)과
        // 활성 PID 자신·System(4)·잘못된 PID는 조상 검사 대상이 아니다.
        private static bool IsAncestryCandidate(AudioSession session, int foregroundPid)
        {
            return session != null
                && !session.CrossProcess
                && !session.SystemSounds
                && session.State != 2
                && session.Pid > 4
                && session.Pid != foregroundPid;
        }
    }

    // 단일 PID의 실행 파일 전체 경로 조회. 스냅샷 없이 OpenProcess 한 번으로 끝난다.
    internal static class ProcessImage
    {
        public static string TryGetPath(int pid)
        {
            if (pid <= 0)
            {
                return null;
            }

            IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                System.Text.StringBuilder buffer = new System.Text.StringBuilder(1024);
                int size = buffer.Capacity;
                return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                    ? buffer.ToString(0, size)
                    : null;
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }
    }

    internal static class AudioController
    {
        // 자기 프로세스 ID는 실행 중 바뀌지 않으므로 한 번만 읽는다.
        private static readonly int OwnProcessId = Process.GetCurrentProcess().Id;

        public static ActionOutcome Execute(AudioActionKind kind, int step)
        {
            ActionOutcome outcome = new ActionOutcome();
            if (step < 1)
            {
                step = 1;
            }
            else if (step > 20)
            {
                step = 20;
            }

            int foregroundPid = Native.GetForegroundProcessId();
            if (foregroundPid <= 0)
            {
                return Fail(outcome, "활성 창을 찾지 못해 실행하지 않았습니다.");
            }

            if (foregroundPid == OwnProcessId)
            {
                return Fail(outcome, "이 앱의 창이 활성 상태여서 실행하지 않았습니다.");
            }

            // 세션은 매 동작마다 새로 열거한다(캐시 없음). 대상 판정에 필요한 값만 읽는다.
            List<EndpointInfo> endpoints;
            List<AudioSession> sessions;
            try
            {
                sessions = Audio.EnumerateAll(out endpoints, false);
            }
            catch (Exception ex)
            {
                return Fail(outcome, "오디오 세션을 열거하지 못했습니다: " + ex.Message);
            }

            try
            {
                // 같은 실행 파일의 자식 프로세스 세션(예: 브라우저 오디오 프로세스)이
                // 있을 때만 전체 스냅샷으로 조상·생성 시각을 검증한다. 그 외에는 빈 표로
                // 판정해도 활성 PID 세션만 선택되므로 결과가 동일하다.
                Dictionary<int, string> paths = new Dictionary<int, string>();
                Func<int, string> pathOf = delegate(int pid)
                {
                    string cached;
                    if (!paths.TryGetValue(pid, out cached))
                    {
                        cached = ProcessImage.TryGetPath(pid);
                        paths[pid] = cached;
                    }

                    return cached;
                };

                ProcTable table;
                if (TargetPrefilter.NeedsAncestry(foregroundPid, sessions, pathOf))
                {
                    try
                    {
                        table = ProcTable.Snapshot();
                    }
                    catch (Exception ex)
                    {
                        return Fail(outcome, "프로세스 목록을 읽지 못했습니다: " + ex.Message);
                    }
                }
                else
                {
                    table = new ProcTable();
                }

                string processName = GetProcessName(table, foregroundPid, pathOf);
                outcome.TargetPid = foregroundPid;
                outcome.TargetProcessName = processName;

                List<AudioSession> selected = Targeting.SelectSessions(table, foregroundPid, sessions);
                outcome.TargetSessionCount = selected.Count;
                if (selected.Count == 0)
                {
                    return Fail(outcome,
                        "'" + processName + "' (PID " + foregroundPid.ToString(CultureInfo.InvariantCulture)
                        + ")의 활성 오디오 세션을 찾지 못했습니다. 앱이 소리를 재생 중인지, 출력 장치가 활성 상태인지 확인하세요.");
                }

                // Refresh and validate every original value immediately before mutation.
                // Fail closed: one unreadable session aborts the whole action.
                List<string> preflight = new List<string>();
                for (int i = 0; i < selected.Count; i++)
                {
                    string refreshError;
                    if (!selected[i].TryRefreshOriginals(out refreshError))
                    {
                        preflight.Add(SessionLabel(selected[i]) + ": " + refreshError);
                    }
                }

                if (preflight.Count > 0)
                {
                    return Fail(outcome,
                        "일부 세션 값을 읽지 못해 아무것도 변경하지 않았습니다. " + preflight[0]);
                }

                // Focus can change between classification and mutation.
                int currentPid = Native.GetForegroundProcessId();
                if (currentPid != foregroundPid)
                {
                    outcome.FocusChanged = true;
                    return Fail(outcome, "포커스가 다른 앱으로 바뀌어 실행하지 않았습니다.");
                }

                bool muteTarget = false;
                if (kind == AudioActionKind.ToggleMute)
                {
                    List<bool> mutedStates = new List<bool>();
                    for (int i = 0; i < selected.Count; i++)
                    {
                        mutedStates.Add(selected[i].OrigMuted);
                    }

                    muteTarget = ToggleDecision.ShouldMute(mutedStates);
                }

                int delta = kind == AudioActionKind.VolumeUp ? step : -step;
                List<AudioSession> changed = new List<AudioSession>();
                List<string> errors = new List<string>();
                bool failed = false;

                try
                {
                    for (int i = 0; i < selected.Count; i++)
                    {
                        AudioSession session = selected[i];
                        int hr;
                        if (kind == AudioActionKind.ToggleMute)
                        {
                            hr = session.TrySetMute(muteTarget);
                        }
                        else
                        {
                            hr = session.TrySetVolume(VolumeMath.Step(session.OrigVolume, delta));
                        }

                        if (hr != 0)
                        {
                            failed = true;
                            errors.Add("값 변경 실패 (" + SessionLabel(session) + "): 0x"
                                + hr.ToString("X8", CultureInfo.InvariantCulture));
                            break;
                        }

                        changed.Add(session);
                    }

                    if (!failed)
                    {
                        for (int i = 0; i < changed.Count; i++)
                        {
                            AudioSession session = changed[i];
                            if (kind == AudioActionKind.ToggleMute)
                            {
                                bool mute;
                                if (!session.TryReadMute(out mute))
                                {
                                    failed = true;
                                    errors.Add("변경 후 음소거 상태를 읽지 못했습니다 (" + SessionLabel(session) + ").");
                                    break;
                                }

                                if (mute != muteTarget)
                                {
                                    failed = true;
                                    errors.Add("변경 후 음소거 상태가 일치하지 않습니다 (" + SessionLabel(session) + ").");
                                    break;
                                }
                            }
                            else
                            {
                                float expected = VolumeMath.Step(session.OrigVolume, delta);
                                float volume;
                                if (!session.TryReadVolume(out volume))
                                {
                                    failed = true;
                                    errors.Add("변경 후 볼륨을 읽지 못했습니다 (" + SessionLabel(session) + ").");
                                    break;
                                }

                                if (Math.Abs(volume - expected) > VolumeMath.Epsilon)
                                {
                                    failed = true;
                                    errors.Add("변경 후 볼륨이 일치하지 않습니다 (" + SessionLabel(session) + ").");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    errors.Add("예기치 않은 오류: " + ex.Message);
                }

                if (failed)
                {
                    string restoreText = Restore(changed, kind);
                    outcome.RestoreAttempted = changed.Count > 0;
                    outcome.RestoreSucceeded = changed.Count == 0 || restoreText == null;
                    string firstError = errors.Count > 0 ? errors[0] : "알 수 없는 오류";
                    if (changed.Count == 0)
                    {
                        return Fail(outcome, "오디오 값을 변경하지 못했습니다. " + firstError);
                    }

                    return Fail(outcome,
                        "일부 세션 변경에 실패해 원래 값 복구를 시도했습니다. 복구 "
                        + (outcome.RestoreSucceeded ? "성공" : "실패")
                        + ". " + firstError);
                }

                outcome.Success = true;
                outcome.TargetSessionCount = changed.Count;
                string target = "'" + processName + "' (PID " + foregroundPid.ToString(CultureInfo.InvariantCulture) + ")";
                if (kind == AudioActionKind.ToggleMute)
                {
                    outcome.StatusKo = target + " 음소거 " + (muteTarget ? "켬" : "끔")
                        + " · 세션 " + changed.Count.ToString(CultureInfo.InvariantCulture) + "개";
                }
                else
                {
                    string direction = delta > 0 ? "+" : string.Empty;
                    outcome.StatusKo = target + " 볼륨 " + direction
                        + delta.ToString(CultureInfo.InvariantCulture) + "%"
                        + " · 세션 " + changed.Count.ToString(CultureInfo.InvariantCulture) + "개";
                }

                return outcome;
            }
            finally
            {
                Audio.ReleaseSessions(sessions);
            }
        }

        // Attempts to restore every changed session, continuing even when one fails.
        // Returns null when all restores verified, otherwise a short reason.
        private static string Restore(IList<AudioSession> changed, AudioActionKind kind)
        {
            string firstFailure = null;
            for (int i = 0; i < changed.Count; i++)
            {
                AudioSession session = changed[i];
                int hr;
                if (kind == AudioActionKind.ToggleMute)
                {
                    hr = session.TrySetMute(session.OrigMuted);
                }
                else
                {
                    hr = session.TrySetVolume(session.OrigVolume);
                }

                if (hr != 0)
                {
                    if (firstFailure == null)
                    {
                        firstFailure = "복구 쓰기 실패 (" + SessionLabel(session) + ")";
                    }

                    continue;
                }

                if (kind == AudioActionKind.ToggleMute)
                {
                    bool mute;
                    if (!session.TryReadMute(out mute) || mute != session.OrigMuted)
                    {
                        if (firstFailure == null)
                        {
                            firstFailure = "음소거 복구 확인 실패 (" + SessionLabel(session) + ")";
                        }
                    }
                }
                else
                {
                    float volume;
                    if (!session.TryReadVolume(out volume)
                        || Math.Abs(volume - session.OrigVolume) > VolumeMath.Epsilon)
                    {
                        if (firstFailure == null)
                        {
                            firstFailure = "볼륨 복구 확인 실패 (" + SessionLabel(session) + ")";
                        }
                    }
                }
            }

            return firstFailure;
        }

        private static string GetProcessName(ProcTable table, int pid, Func<int, string> pathOf)
        {
            ProcEntry entry;
            if (table != null && table.TryGet(pid, out entry) && !string.IsNullOrEmpty(entry.ExeName))
            {
                return entry.ExeName;
            }

            // 스냅샷을 만들지 않은 경우 경로에서 실행 파일 이름만 꺼낸다.
            string path = pathOf != null ? pathOf(pid) : null;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string name = System.IO.Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
                catch (ArgumentException)
                {
                    // 비정상 경로 문자는 PID 표기로 대체한다.
                }
            }

            return "PID " + pid.ToString(CultureInfo.InvariantCulture);
        }

        private static string SessionLabel(AudioSession session)
        {
            return "PID " + session.Pid.ToString(CultureInfo.InvariantCulture);
        }

        private static ActionOutcome Fail(ActionOutcome outcome, string message)
        {
            outcome.Success = false;
            outcome.StatusKo = message;
            return outcome;
        }
    }
}
