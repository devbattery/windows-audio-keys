// Game Audio Keys - global hotkey registration lifecycle. RegisterHotKey only;
// no low-level keyboard hook, injection or driver.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    internal sealed class HotkeyManager : IDisposable
    {
        public const int MuteId = 0x4A01;
        public const int VolumeUpId = 0x4A02;
        public const int VolumeDownId = 0x4A03;

        private const int TemporaryIdOffset = 0x100;
        private const int ErrorHotkeyAlreadyRegistered = 1409;

        private readonly IntPtr window;
        private readonly Dictionary<int, HotkeyBinding> registered = new Dictionary<int, HotkeyBinding>();
        private bool disposed;

        public HotkeyManager(IntPtr window)
        {
            this.window = window;
        }

        public bool HasRegistrations
        {
            get { return this.registered.Count > 0; }
        }

        // Single registration lifecycle: either all configured hotkeys are registered or
        // none are. A partial failure unregisters the IDs that did succeed.
        public bool Apply(AppSettings settings, bool shouldRegister, out string errorKo)
        {
            errorKo = null;
            if (this.disposed)
            {
                errorKo = "단축키 관리자가 이미 종료되었습니다.";
                return false;
            }

            if (this.window == IntPtr.Zero)
            {
                if (shouldRegister)
                {
                    errorKo = "설정 창 핸들이 없어 단축키를 등록할 수 없습니다.";
                    return false;
                }

                return true;
            }

            if (!shouldRegister)
            {
                this.UnregisterAll();
                return true;
            }

            if (settings == null)
            {
                errorKo = "단축키 설정이 없습니다.";
                return false;
            }

            Dictionary<int, HotkeyBinding> desired = BuildDesired(settings);
            if (Matches(desired))
            {
                return true;
            }

            this.UnregisterAll();
            foreach (KeyValuePair<int, HotkeyBinding> pair in desired)
            {
                if (pair.Value.Key <= 0 || pair.Value.Key > 254)
                {
                    this.UnregisterAll();
                    errorKo = "등록할 수 없는 키 값입니다.";
                    return false;
                }

                uint modifiers = Modifiers(pair.Value);
                if (pair.Key == MuteId)
                {
                    modifiers |= Native.MOD_NOREPEAT;
                }

                if (!Native.RegisterHotKey(this.window, pair.Key, modifiers, (uint)pair.Value.Key))
                {
                    int win32 = Marshal.GetLastWin32Error();
                    this.UnregisterAll();
                    errorKo = DescribeFailure(pair.Value, win32);
                    return false;
                }

                this.registered[pair.Key] = pair.Value.Clone();
            }

            return true;
        }

        // Used while the settings window is focused (real hotkeys are unregistered then).
        // Temporary IDs are always released, whether the check succeeds or fails.
        public bool TryValidateTemporary(AppSettings settings, out string errorKo)
        {
            errorKo = null;
            if (this.window == IntPtr.Zero)
            {
                errorKo = "설정 창 핸들이 없어 단축키를 검증할 수 없습니다.";
                return false;
            }

            if (settings == null)
            {
                errorKo = "단축키 설정이 없습니다.";
                return false;
            }

            Dictionary<int, HotkeyBinding> desired = BuildDesired(settings);
            List<int> temporaryIds = new List<int>();
            try
            {
                foreach (KeyValuePair<int, HotkeyBinding> pair in desired)
                {
                    if (pair.Value.Key <= 0 || pair.Value.Key > 254)
                    {
                        errorKo = "등록할 수 없는 키 값입니다.";
                        return false;
                    }

                    int temporaryId = pair.Key + TemporaryIdOffset;
                    uint modifiers = Modifiers(pair.Value);
                    if (pair.Key == MuteId)
                    {
                        modifiers |= Native.MOD_NOREPEAT;
                    }

                    if (!Native.RegisterHotKey(this.window, temporaryId, modifiers, (uint)pair.Value.Key))
                    {
                        int win32 = Marshal.GetLastWin32Error();
                        errorKo = DescribeFailure(pair.Value, win32);
                        return false;
                    }

                    temporaryIds.Add(temporaryId);
                }
            }
            finally
            {
                for (int i = 0; i < temporaryIds.Count; i++)
                {
                    try
                    {
                        Native.UnregisterHotKey(this.window, temporaryIds[i]);
                    }
                    catch (Exception)
                    {
                        // Nothing useful to do; the window is going away or the ID is free.
                    }
                }
            }

            return true;
        }

        public void UnregisterAll()
        {
            if (this.window != IntPtr.Zero)
            {
                foreach (int id in this.registered.Keys)
                {
                    try
                    {
                        Native.UnregisterHotKey(this.window, id);
                    }
                    catch (Exception)
                    {
                        // Unregistering an already-released ID is harmless.
                    }
                }
            }

            this.registered.Clear();
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.UnregisterAll();
        }

        private static Dictionary<int, HotkeyBinding> BuildDesired(AppSettings settings)
        {
            Dictionary<int, HotkeyBinding> desired = new Dictionary<int, HotkeyBinding>();
            if (settings.Mute != null)
            {
                desired[MuteId] = settings.Mute.Clone();
            }

            if (settings.VolumeUp != null)
            {
                desired[VolumeUpId] = settings.VolumeUp.Clone();
            }

            if (settings.VolumeDown != null)
            {
                desired[VolumeDownId] = settings.VolumeDown.Clone();
            }

            return desired;
        }

        private bool Matches(Dictionary<int, HotkeyBinding> desired)
        {
            if (desired.Count != this.registered.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, HotkeyBinding> pair in desired)
            {
                HotkeyBinding current;
                if (!this.registered.TryGetValue(pair.Key, out current) || !current.SameAs(pair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static uint Modifiers(HotkeyBinding binding)
        {
            uint modifiers = 0;
            if (binding.Ctrl)
            {
                modifiers |= Native.MOD_CONTROL;
            }

            if (binding.Alt)
            {
                modifiers |= Native.MOD_ALT;
            }

            if (binding.Shift)
            {
                modifiers |= Native.MOD_SHIFT;
            }

            return modifiers;
        }

        private static string DescribeFailure(HotkeyBinding binding, int win32)
        {
            string reason;
            if (win32 == ErrorHotkeyAlreadyRegistered)
            {
                reason = "이미 다른 프로그램이 이 조합을 사용 중입니다.";
            }
            else if (win32 == 0)
            {
                reason = "Windows가 등록을 거부했습니다.";
            }
            else
            {
                reason = "Win32 오류 " + win32.ToString(CultureInfo.InvariantCulture) + ".";
            }

            return "단축키 '" + binding.Display + "' 등록에 실패했습니다: " + reason
                + " 다른 조합을 선택하거나 일시정지하세요.";
        }
    }
}
