// Game Audio Keys - application lifetime, tray menu, foreground suppression and the
// one consistent hotkey registration lifecycle.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly MainForm form;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private readonly string settingsPath;
        private readonly int ownProcessId;
        private readonly bool settingsOverridden;

        // Stable WinForms marshaling target, independent of the settings window's
        // HWND. HideToTray/ShowInTaskbar recreate that HWND, which would otherwise
        // let a BeginInvoke posted to the old window disappear and leave the
        // startup controls disabled forever.
        private readonly Control uiDispatcher;

        private AppSettings settings;
        private HotkeyManager hotkeys;
        private Native.WinEventDelegate foregroundCallback;
        private IntPtr foregroundHook = IntPtr.Zero;
        private string hookError;
        private string registrationError;
        private bool suspended;
        private bool exiting;
        private bool startupBusy;

        public TrayAppContext(
            SettingsLoadResult load,
            string settingsPath,
            bool settingsOverridden,
            bool startupMode)
        {
            this.settings = load.Settings;
            this.settingsPath = settingsPath;
            this.settingsOverridden = settingsOverridden;
            this.ownProcessId = Process.GetCurrentProcess().Id;

            // Force a handle on the current UI thread before any background query can
            // begin. The control is never shown and exists only as a message target.
            this.uiDispatcher = new Control();
            if (this.uiDispatcher.Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("자동 시작 상태 알림용 UI 핸들을 만들지 못했습니다.");
            }

            this.form = new MainForm(this);
            this.form.LoadSettingsIntoControls(this.settings);
            this.form.SetLastAction(load.StatusKo, load.Corrupt);

            this.tray = new NotifyIcon();
            this.tray.Icon = SystemIcons.Application;
            this.tray.Visible = true;
            this.tray.Text = StartupConstants.ProductName;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("설정 열기", null, delegate { this.form.ShowSettings(); });
            this.pauseItem = new ToolStripMenuItem("일시정지");
            this.pauseItem.Click += delegate { this.TogglePause(); };
            menu.Items.Add(this.pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { this.ExitApp(); });
            this.tray.ContextMenuStrip = menu;
            this.tray.DoubleClick += delegate { this.form.ShowSettings(); };

            // Suppress registration until the first foreground check completes when the
            // settings window is about to be shown.
            this.suspended = load.FirstLaunch
                || load.Corrupt
                || Native.GetForegroundProcessId() == this.ownProcessId;

            this.form.EnsureHandle();
            this.InstallForegroundHook();

            // Login startup must stay quietly in the tray, but every manual launch
            // (including one with saved settings) shows the settings window. A corrupt
            // config also opens it so the problem is visible and actionable.
            if (StartupLaunchPolicy.ShouldShowSettings(startupMode, load.Corrupt))
            {
                this.form.ShowSettings();
            }

            this.RefreshForegroundState();
            this.UpdateTrayText();
        }

        public bool IsExiting
        {
            get { return this.exiting; }
        }

        public void OnFormHandleCreated()
        {
            if (this.exiting)
            {
                return;
            }

            if (this.hotkeys != null)
            {
                this.hotkeys.Dispose();
            }

            this.hotkeys = new HotkeyManager(this.form.Handle);
            this.UpdateHotkeyLifecycle();
        }

        public void OnFormHandleDestroyed()
        {
            if (this.hotkeys != null)
            {
                this.hotkeys.Dispose();
                this.hotkeys = null;
            }
        }

        // Called by the WinEvent hook and by form activation/deactivation as a
        // belt-and-braces fallback. Own-process focus is never ignored.
        public void RefreshForegroundState()
        {
            if (this.exiting)
            {
                return;
            }

            bool nowSuspended;
            try
            {
                nowSuspended = Native.GetForegroundProcessId() == this.ownProcessId;
            }
            catch (Exception ex)
            {
                this.form.SetLastAction("포커스 상태를 확인하지 못했습니다: " + ex.Message, true);
                return;
            }

            if (nowSuspended != this.suspended)
            {
                this.suspended = nowSuspended;
                this.UpdateHotkeyLifecycle();
            }

            this.UpdateStateLabel();
            this.UpdateTrayText();
        }

        public void HandleHotkey(int id)
        {
            if (this.exiting || !this.settings.Enabled || this.suspended)
            {
                return;
            }

            AudioActionKind kind;
            if (id == HotkeyManager.MuteId)
            {
                kind = AudioActionKind.ToggleMute;
            }
            else if (id == HotkeyManager.VolumeUpId)
            {
                kind = AudioActionKind.VolumeUp;
            }
            else if (id == HotkeyManager.VolumeDownId)
            {
                kind = AudioActionKind.VolumeDown;
            }
            else
            {
                return;
            }

            try
            {
                ActionOutcome outcome = AudioController.Execute(kind, this.settings.Step);
                this.form.SetLastAction(outcome.StatusKo, !outcome.Success);
            }
            catch (Exception ex)
            {
                // A COM or process error must never escape into the message loop.
                this.form.SetLastAction("단축키 처리 중 오류가 발생했습니다: " + ex.Message, true);
            }
        }

        public void SaveSettingsFromUi(MainForm source)
        {
            if (this.exiting)
            {
                return;
            }

            AppSettings proposed = source.ReadSettingsFromControls();
            SettingsValidation validation = SettingsValidator.Validate(proposed);
            if (!validation.IsValid)
            {
                source.SetLastAction("저장하지 않았습니다:" + Environment.NewLine + validation.Summary, true);
                return;
            }

            if (this.hotkeys == null && proposed.Enabled)
            {
                source.SetLastAction("설정 창 핸들이 아직 준비되지 않았습니다. 잠시 후 다시 시도하세요.", true);
                return;
            }

            if (proposed.Enabled && this.foregroundHook == IntPtr.Zero)
            {
                source.SetLastAction(
                    "포커스 감시를 시작하지 못해 단축키를 활성화할 수 없습니다. 앱을 재시작하세요.",
                    true);
                this.UpdateHotkeyLifecycle();
                return;
            }

            // The settings window has focus while Save is clicked, so real hotkeys are
            // suppressed. Only an enabled proposal needs registration validation: pausing
            // must never depend on acquiring a conflicting hotkey.
            if (this.hotkeys != null)
            {
                this.hotkeys.UnregisterAll();
            }

            if (proposed.Enabled)
            {
                string registerError;
                if (!this.hotkeys.TryValidateTemporary(proposed, out registerError))
                {
                    source.SetLastAction(registerError, true);
                    this.UpdateHotkeyLifecycle();
                    source.SetRuntimeStatus("단축키 충돌: " + registerError, true);
                    return;
                }
            }

            string saveError;
            if (!SettingsStore.TrySave(this.settingsPath, proposed, out saveError))
            {
                source.SetLastAction("설정을 저장하지 못했습니다: " + saveError, true);
                this.UpdateHotkeyLifecycle();
                return;
            }

            AppSettings previous = this.settings;
            this.settings = proposed;

            // Normally still suspended here, but if focus changed during saving, make sure
            // the new bindings really register; otherwise restore the previous settings.
            if (this.settings.Enabled && !this.suspended && this.hotkeys != null)
            {
                string applyError;
                if (!this.hotkeys.Apply(this.settings, true, out applyError))
                {
                    this.settings = previous;
                    string rollbackError;
                    bool rollbackSaved = SettingsStore.TrySave(this.settingsPath, previous, out rollbackError);
                    source.LoadSettingsIntoControls(previous);
                    this.registrationError = applyError;
                    string message = "새 단축키 적용에 실패해 이전 설정을 유지했습니다: " + applyError;
                    if (!rollbackSaved)
                    {
                        message += Environment.NewLine + "이전 설정 파일 복구에도 실패했습니다: " + rollbackError;
                    }

                    source.SetLastAction(message, true);
                    this.UpdateHotkeyLifecycle();
                    return;
                }
            }

            source.LoadSettingsIntoControls(this.settings);
            this.UpdateHotkeyLifecycle();
            this.UpdateTrayText();
            source.SetLastAction(
                this.settings.Enabled
                    ? "설정을 저장했습니다. 다른 앱으로 돌아가면 단축키가 활성화됩니다."
                    : "설정을 저장했습니다. 단축키는 일시정지 상태입니다.",
                false);
        }

        // The Task Scheduler registration is intentionally separate from the audio
        // settings save: it is a wholly different Windows subsystem, and a failed
        // registration must not roll back unrelated audio settings.
        public void RefreshStartupState(MainForm source)
        {
            if (this.exiting || source == null)
            {
                return;
            }

            if (this.settingsOverridden)
            {
                source.SetStartupUnavailable(
                    "테스트 설정 폴더를 사용하는 동안에는 자동 시작 작업을 조회하거나 변경하지 않습니다. "
                    + "임시 앱 위치가 등록되지 않도록 일반 실행에서 설정하세요.");
                return;
            }

            if (this.startupBusy)
            {
                return;
            }

            this.startupBusy = true;
            source.SetStartupBusy(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                StartupTaskState state = StartupTaskHost.QueryState();
                this.PostToUi(delegate
                {
                    this.startupBusy = false;
                    if (!source.IsDisposed)
                    {
                        source.SetStartupBusy(false);
                        source.SetStartupState(state);
                    }
                });
            });
        }

        public void ApplyStartupSetting(MainForm source, bool enable)
        {
            if (this.exiting || source == null)
            {
                return;
            }

            if (this.settingsOverridden)
            {
                source.SetStartupUnavailable(
                    "테스트 설정 폴더를 사용하는 동안에는 자동 시작 작업을 조회하거나 변경하지 않습니다. "
                    + "임시 앱 위치가 등록되지 않도록 일반 실행에서 설정하세요.");
                return;
            }

            if (this.startupBusy)
            {
                source.SetStartupMessage("자동 시작 설정을 처리하는 중입니다. 잠시 기다리세요.", true);
                return;
            }

            this.startupBusy = true;
            source.SetStartupBusy(true);
            source.SetStartupMessage(
                enable
                    ? "Windows 관리자 승인을 기다리는 중입니다..."
                    : "자동 시작 작업을 정리하는 중입니다...",
                false);

            ThreadPool.QueueUserWorkItem(delegate
            {
                StartupApplyResult result;
                StartupTaskState state;
                try
                {
                    result = StartupTaskHost.Apply(enable);
                    state = result.State != null ? result.State : StartupTaskHost.QueryState();
                }
                catch (Exception ex)
                {
                    result = new StartupApplyResult();
                    result.MessageKo = "자동 시작 설정 중 오류가 발생했습니다: " + ex.Message;
                    state = StartupTaskHost.QueryState();
                }

                this.PostToUi(delegate
                {
                    this.startupBusy = false;
                    if (!source.IsDisposed)
                    {
                        source.SetStartupBusy(false);
                        source.SetStartupState(state);
                        this.form.SetLastAction(result.MessageKo, !result.Success);
                    }
                });
            });
        }

        // Background startup queries must never keep the window's buttons disabled
        // when the settings HWND is hidden, recreated or disposed. The callback runs
        // on the stable dispatcher control, then updates the still-living form
        // controls directly on the UI thread.
        private void PostToUi(Action action)
        {
            if (this.exiting)
            {
                this.ResetStartupBusy();
                return;
            }

            try
            {
                if (this.uiDispatcher.IsDisposed)
                {
                    this.ResetStartupBusy();
                    return;
                }

                this.uiDispatcher.BeginInvoke(action);
            }
            catch (Exception)
            {
                this.ResetStartupBusy();
            }
        }

        // Called from the worker thread when dispatch itself fails. The busy flag is
        // cleared first; if the UI thread is still alive the controls are recovered
        // there too, so a lost callback cannot permanently disable them.
        private void ResetStartupBusy()
        {
            this.startupBusy = false;
            try
            {
                if (this.uiDispatcher.InvokeRequired)
                {
                    this.uiDispatcher.BeginInvoke(new MethodInvoker(this.RecoverUiBusyState));
                }
                else
                {
                    this.RecoverUiBusyState();
                }
            }
            catch (Exception)
            {
                // The UI/message loop is already gone; nothing can remain disabled.
            }
        }

        private void RecoverUiBusyState()
        {
            this.startupBusy = false;
            if (!this.form.IsDisposed)
            {
                this.form.SetStartupBusy(false);
            }
        }

        private void InstallForegroundHook()
        {
            this.foregroundCallback = new Native.WinEventDelegate(this.OnForegroundEvent);
            try
            {
                this.foregroundHook = Native.SetWinEventHook(
                    Native.EVENT_SYSTEM_FOREGROUND,
                    Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    this.foregroundCallback,
                    0,
                    0,
                    Native.WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception ex)
            {
                this.foregroundHook = IntPtr.Zero;
                this.hookError = "포커스 감시를 시작하지 못해 안전을 위해 단축키를 등록하지 않습니다: " + ex.Message;
            }

            if (this.foregroundHook == IntPtr.Zero && this.hookError == null)
            {
                this.hookError = "포커스 감시를 시작하지 못해 안전을 위해 단축키를 등록하지 않았습니다.";
            }

            if (this.hookError != null)
            {
                this.form.SetLastAction(this.hookError, true);
            }

            // Hook installation happens after the window handle is created, so run the
            // lifecycle once more: valid startup must still register as expected.
            this.UpdateHotkeyLifecycle();
        }

        private void OnForegroundEvent(
            IntPtr hook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint thread,
            uint time)
        {
            if (eventType != Native.EVENT_SYSTEM_FOREGROUND)
            {
                return;
            }

            try
            {
                this.RefreshForegroundState();
            }
            catch (Exception ex)
            {
                this.form.SetLastAction("포커스 이벤트 처리 중 오류가 발생했습니다: " + ex.Message, true);
            }
        }

        private void TogglePause()
        {
            if (this.exiting)
            {
                return;
            }

            if (this.settings.Enabled)
            {
                // Pause immediately and never validate/acquire a hotkey for this. Disk
                // persistence is best effort: a failed write must not leave keys reserved.
                this.settings.Enabled = false;
                this.UpdateHotkeyLifecycle();
                this.form.LoadSettingsIntoControls(this.settings);
                this.UpdateTrayText();

                string pauseError;
                if (SettingsStore.TrySave(this.settingsPath, this.settings, out pauseError))
                {
                    this.form.SetLastAction("단축키를 일시정지했습니다. 키가 등록되지 않습니다.", false);
                }
                else
                {
                    this.form.SetLastAction(
                        "단축키를 일시정지했지만 상태를 저장하지 못했습니다: " + pauseError
                        + " (이번 실행에서는 일시정지가 유지됩니다)",
                        true);
                }

                return;
            }

            // Resume only after the proposed bindings can actually be registered.
            if (this.hotkeys == null)
            {
                this.form.SetLastAction("설정 창 핸들이 아직 준비되지 않아 다시 시작할 수 없습니다.", true);
                return;
            }

            if (this.foregroundHook == IntPtr.Zero)
            {
                this.form.SetLastAction(
                    "포커스 감시를 시작하지 못해 단축키를 다시 시작할 수 없습니다. 앱을 재시작하세요.",
                    true);
                this.UpdateStateLabel();
                this.UpdateTrayText();
                this.UpdatePauseMenu();
                return;
            }

            AppSettings proposed = this.settings.Clone();
            proposed.Enabled = true;
            string registerError;
            if (!this.hotkeys.TryValidateTemporary(proposed, out registerError))
            {
                this.form.SetLastAction("단축키를 다시 시작하지 못했습니다: " + registerError, true);
                this.UpdateStateLabel();
                this.UpdateTrayText();
                this.UpdatePauseMenu();
                return;
            }

            string saveError;
            if (!SettingsStore.TrySave(this.settingsPath, proposed, out saveError))
            {
                this.form.SetLastAction(
                    "단축키를 다시 시작하지 못했습니다. 상태를 저장할 수 없습니다: " + saveError,
                    true);
                this.UpdateStateLabel();
                this.UpdateTrayText();
                this.UpdatePauseMenu();
                return;
            }

            this.settings = proposed;

            // Tray resume usually happens with another app foreground. If registration
            // really fails now, return to the paused state instead of pretending success.
            if (!this.suspended && this.foregroundHook != IntPtr.Zero)
            {
                string applyError;
                if (!this.hotkeys.Apply(this.settings, true, out applyError))
                {
                    this.settings.Enabled = false;
                    string rollbackError;
                    bool rollbackSaved = SettingsStore.TrySave(this.settingsPath, this.settings, out rollbackError);
                    this.registrationError = applyError;
                    this.form.LoadSettingsIntoControls(this.settings);
                    string message = "단축키를 다시 시작하지 못해 일시정지 상태를 유지합니다: " + applyError;
                    if (!rollbackSaved)
                    {
                        message += Environment.NewLine + "일시정지 상태 저장에도 실패했습니다: " + rollbackError;
                    }

                    this.form.SetLastAction(message, true);
                    this.UpdateHotkeyLifecycle();
                    return;
                }
            }

            this.form.LoadSettingsIntoControls(this.settings);
            this.UpdateHotkeyLifecycle();
            this.UpdateTrayText();
            this.form.SetLastAction("단축키를 다시 시작했습니다.", false);
        }

        private void UpdateHotkeyLifecycle()
        {
            if (this.hotkeys == null)
            {
                this.UpdateStateLabel();
                return;
            }

            // A failed foreground hook means focus tracking is unreliable, so no global
            // keys may stay reserved.
            bool shouldRegister = this.settings.Enabled
                && !this.suspended
                && this.foregroundHook != IntPtr.Zero;
            string error;
            if (!this.hotkeys.Apply(this.settings, shouldRegister, out error))
            {
                if (this.registrationError != error)
                {
                    this.form.SetLastAction(error, true);
                }

                this.registrationError = error;
            }
            else
            {
                this.registrationError = null;
            }

            this.UpdateStateLabel();
            this.UpdateTrayText();
            this.UpdatePauseMenu();
        }

        private void UpdateStateLabel()
        {
            string text;
            bool error = false;
            if (this.hookError != null)
            {
                text = this.hookError;
                error = true;
            }
            else if (!this.settings.Enabled)
            {
                text = "일시정지됨 - 단축키가 등록되지 않았습니다. 트레이 메뉴에서 다시 시작할 수 있습니다.";
            }
            else if (this.suspended)
            {
                text = "설정 창이 활성 상태라 단축키를 잠시 해제했습니다. 다른 앱으로 돌아가면 다시 등록됩니다.";
            }
            else if (this.registrationError != null)
            {
                text = "단축키 충돌: " + this.registrationError;
                error = true;
            }
            else if (this.hotkeys != null && this.hotkeys.HasRegistrations)
            {
                text = "사용 중 - " + this.DisplayBindings() + " 가 시스템에 등록되어 있습니다.";
            }
            else
            {
                text = "대기 중 - 단축키가 아직 등록되지 않았습니다.";
            }

            this.form.SetRuntimeStatus(text, error);
        }

        private string DisplayBindings()
        {
            if (this.settings.Mute == null || this.settings.VolumeUp == null || this.settings.VolumeDown == null)
            {
                return "(설정 없음)";
            }

            return this.settings.Mute.Display
                + " / " + this.settings.VolumeUp.Display
                + " / " + this.settings.VolumeDown.Display;
        }

        private void UpdateTrayText()
        {
            string text;
            if (this.hookError != null)
            {
                text = StartupConstants.ProductName + " - 포커스 감시 오류";
            }
            else if (!this.settings.Enabled)
            {
                text = StartupConstants.ProductName + " - 일시정지";
            }
            else if (this.suspended)
            {
                text = StartupConstants.ProductName + " - 설정 창 활성";
            }
            else if (this.registrationError != null)
            {
                text = StartupConstants.ProductName + " - 단축키 충돌";
            }
            else
            {
                text = StartupConstants.ProductName + " - 사용 중";
            }

            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }

            try
            {
                this.tray.Text = text;
            }
            catch (Exception)
            {
                // Tooltip length/notification failures must not affect hotkeys.
            }
        }

        private void UpdatePauseMenu()
        {
            this.pauseItem.Text = this.settings.Enabled ? "일시정지" : "다시 시작";
        }

        public void ExitApp()
        {
            if (this.exiting)
            {
                return;
            }

            this.exiting = true;

            if (this.foregroundHook != IntPtr.Zero)
            {
                try
                {
                    Native.UnhookWinEvent(this.foregroundHook);
                }
                catch (Exception)
                {
                    // The process is exiting; a failed unhook is harmless.
                }

                this.foregroundHook = IntPtr.Zero;
            }

            if (this.hotkeys != null)
            {
                this.hotkeys.Dispose();
                this.hotkeys = null;
            }

            try
            {
                this.tray.Visible = false;
                this.tray.Dispose();
            }
            catch (Exception)
            {
                // Never block shutdown on tray cleanup.
            }

            this.form.Close();

            try
            {
                this.uiDispatcher.Dispose();
            }
            catch (Exception)
            {
                // Shutdown must not depend on the dispatcher control cleanup.
            }

            this.ExitThread();
        }
    }
}
