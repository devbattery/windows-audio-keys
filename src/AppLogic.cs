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

    internal static class AudioController
    {
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

            if (foregroundPid == Process.GetCurrentProcess().Id)
            {
                return Fail(outcome, "이 앱의 창이 활성 상태여서 실행하지 않았습니다.");
            }

            ProcTable table;
            try
            {
                table = ProcTable.Snapshot();
            }
            catch (Exception ex)
            {
                return Fail(outcome, "프로세스 목록을 읽지 못했습니다: " + ex.Message);
            }

            List<EndpointInfo> endpoints;
            List<AudioSession> sessions;
            try
            {
                sessions = Audio.EnumerateAll(out endpoints);
            }
            catch (Exception ex)
            {
                return Fail(outcome, "오디오 세션을 열거하지 못했습니다: " + ex.Message);
            }

            try
            {
                string processName = GetProcessName(table, foregroundPid);
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
                        + delta.ToString(CultureInfo.InvariantCulture) + "%p"
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

        private static string GetProcessName(ProcTable table, int pid)
        {
            ProcEntry entry;
            if (table != null && table.TryGet(pid, out entry) && !string.IsNullOrEmpty(entry.ExeName))
            {
                return entry.ExeName;
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
