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

        // 트레이 아이콘: 사용 중(강조색)·일시정지(회색)·오류(빨강). 시작 시 한 번만 만든다.
        private readonly Icon activeTrayIcon;
        private readonly Icon idleTrayIcon;
        private readonly Icon errorTrayIcon;

        private const string ThemeOverrideEnvironment = "GAME_AUDIO_KEYS_THEME";
        private readonly EventHandler themeChangedHandler;

        // 새 버전 알림. 테스트 설정 폴더 실행에서만 환경 변수로 "최신 릴리스"를 흉내 낼 수 있다.
        private const string UpdateTestEnvironment = "GAME_AUDIO_KEYS_UPDATE_TEST_LATEST";
        private readonly ReleaseVersion currentVersion;
        private readonly System.Windows.Forms.Timer updateTimer;
        private readonly ToolStripMenuItem updateItem;
        private readonly ToolStripSeparator updateSeparator;
        private bool updateBusy;
        private ReleaseVersion availableVersion;
        private string notifiedVersion;
        private bool updateBalloonPending;
        private string updateText;
        private UpdateTone updateTone = UpdateTone.Normal;

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

            // 테마: 테스트 설정 폴더 실행에서만 환경 변수로 라이트/다크를 강제할 수 있다.
            // 그 외에는 Windows 앱 테마를 따르고, 실행 중 바뀌면 설정 창을 다시 칠한다.
            if (settingsOverridden)
            {
                ThemeManager.SetTestOverride(Environment.GetEnvironmentVariable(ThemeOverrideEnvironment));
            }

            this.themeChangedHandler = delegate
            {
                if (!this.exiting && this.form != null)
                {
                    this.form.ApplyTheme();
                }
            };
            ThemeManager.ThemeChanged += this.themeChangedHandler;
            ThemeManager.StartWatching();

            this.form = new MainForm(this);
            this.form.LoadSettingsIntoControls(this.settings);
            this.form.SetLastAction(load.StatusKo, load.Corrupt);

            int traySize = SystemInformation.SmallIconSize.Width;
            this.activeTrayIcon = AppIcons.Create(AppIcons.TileColorFor(RuntimeState.Active), traySize);
            this.idleTrayIcon = AppIcons.Create(AppIcons.TileColorFor(RuntimeState.Paused), traySize);
            this.errorTrayIcon = AppIcons.Create(AppIcons.TileColorFor(RuntimeState.Error), traySize);

            this.tray = new NotifyIcon();
            this.tray.Icon = this.idleTrayIcon;
            this.tray.Visible = true;
            this.tray.Text = StartupConstants.ProductName;

            ContextMenuStrip menu = new ContextMenuStrip();

            // 새 버전이 있을 때만 맨 위에 보이는 항목
            this.updateItem = new ToolStripMenuItem("새 버전 내려받기");
            this.updateItem.Visible = false;
            this.updateItem.Click += delegate { this.OpenReleasePage(null); };
            this.updateSeparator = new ToolStripSeparator();
            this.updateSeparator.Visible = false;
            menu.Items.Add(this.updateItem);
            menu.Items.Add(this.updateSeparator);

            menu.Items.Add("설정 열기", null, delegate { this.form.ShowSettings(); });
            this.pauseItem = new ToolStripMenuItem("일시정지");
            this.pauseItem.Click += delegate { this.TogglePause(); };
            menu.Items.Add(this.pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { this.ExitApp(); });
            this.tray.ContextMenuStrip = menu;
            this.tray.DoubleClick += delegate { this.form.ShowSettings(); };

            // 알림(풍선/토스트)을 누르면 새 버전 알림일 때만 릴리스 페이지를 연다.
            this.tray.BalloonTipClicked += delegate
            {
                bool wasUpdate = this.updateBalloonPending;
                this.updateBalloonPending = false;
                if (wasUpdate)
                {
                    this.OpenReleasePage(null);
                }
                else
                {
                    // 그 밖의 알림(보안 경고, 열기 실패 안내)은 설정 창에서 자세히 보여 준다.
                    this.form.ShowSettings();
                }
            };
            this.tray.BalloonTipClosed += delegate { this.updateBalloonPending = false; };

            this.currentVersion = UpdateCheckLogic.CurrentVersion(Application.ProductVersion);
            this.updateTimer = new System.Windows.Forms.Timer();
            this.updateTimer.Tick += delegate
            {
                this.updateTimer.Stop();
                this.StartUpdateCheck(false);
            };

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
            this.UpdateStatusIndicators();

            this.SetUpdateView(this.IdleUpdateText(), this.IdleUpdateTone());
            this.ScheduleAutomaticUpdateCheck(UpdateCheckLogic.FirstCheckDelay);

            // 로그인 자동 시작 인스턴스는 트레이에만 머문다. 자동 시작 작업이 위험한 위치를
            // 가리키면 사용자가 설정 창을 열지 않아도 알 수 있게 한 번 알린다.
            if (startupMode && !settingsOverridden)
            {
                this.WarnIfStartupLocationUnsafe();
            }
        }

        private void WarnIfStartupLocationUnsafe()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StartupTaskState state;
                try
                {
                    state = StartupTaskHost.QueryState();
                }
                catch (Exception)
                {
                    return;
                }

                if (state == null || state.LocationWarningKo == null)
                {
                    return;
                }

                try
                {
                    this.uiDispatcher.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (!this.exiting)
                        {
                            this.ShowTrayNotice(
                                StartupConstants.ProductName + " 보안 경고",
                                "자동 시작 작업이 일반 사용자가 수정할 수 있는 위치의 실행 파일을 관리자 권한으로 실행하고 있습니다. 눌러서 설정 창에서 확인하세요.",
                                false);
                        }
                    }));
                }
                catch (Exception)
                {
                    // 알림을 못 띄워도 설정 창을 열면 같은 경고가 표시된다.
                }
            });
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

            this.UpdateStatusIndicators();
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
                    source.SetRuntimeStatus(RuntimeState.Conflict, "단축키 충돌: " + registerError);
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
            this.UpdateStatusIndicators();
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

                        // 실패 이유(위치 거부, 승인 취소 등)는 스위치 바로 아래에도 보여 준다.
                        // 단, 상태 자체에 보안 경고가 있으면 그 경고를 그대로 둔다.
                        if (!result.Success && !string.IsNullOrEmpty(result.MessageKo)
                            && (state == null || state.LocationWarningKo == null))
                        {
                            source.SetStartupMessage(result.MessageKo, true);
                        }

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

        #region 새 버전 알림

        // 자동 확인 일정. 꺼져 있거나 테스트 설정 폴더 실행이면 예약하지 않는다.
        private void ScheduleAutomaticUpdateCheck(TimeSpan delay)
        {
            if (this.exiting)
            {
                return;
            }

            this.updateTimer.Stop();
            if (!this.settings.CheckForUpdates || this.settingsOverridden || this.currentVersion == null)
            {
                return;
            }

            double milliseconds = Math.Max(1000d, Math.Min(int.MaxValue, delay.TotalMilliseconds));
            this.updateTimer.Interval = (int)milliseconds;
            this.updateTimer.Start();
        }

        // 설정 창의 "지금 확인". 테스트 설정 폴더에서도 동작한다.
        public void CheckForUpdatesNow()
        {
            this.StartUpdateCheck(true);
        }

        private void StartUpdateCheck(bool manual)
        {
            if (this.exiting || this.updateBusy)
            {
                return;
            }

            if (this.currentVersion == null)
            {
                this.SetUpdateView("현재 버전 정보를 읽지 못해 새 버전을 확인할 수 없습니다.", UpdateTone.Error);
                return;
            }

            this.updateBusy = true;
            this.SetUpdateView("새 버전을 확인하는 중입니다...", UpdateTone.Normal);

            ReleaseVersion current = this.currentVersion;
            string testLatest = this.settingsOverridden
                ? Environment.GetEnvironmentVariable(UpdateTestEnvironment)
                : null;

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result;
                try
                {
                    if (string.IsNullOrEmpty(testLatest))
                    {
                        result = UpdateClient.CheckLatest(current);
                    }
                    else
                    {
                        LatestRelease simulated = new LatestRelease();
                        simulated.TagName = testLatest;
                        result = UpdateCheckLogic.BuildResult(current, simulated);
                    }
                }
                catch (Exception ex)
                {
                    result = UpdateCheckResult.Failure("새 버전 확인 중 오류가 발생했습니다: " + ex.Message);
                }

                // 자동 시작 작업과 진행 상태를 섞지 않도록 업데이트 결과는 따로 보낸다.
                // 전달에 실패하면(메시지 루프 종료 중) 진행 중 표시만 푼다.
                try
                {
                    this.uiDispatcher.BeginInvoke(new MethodInvoker(delegate { this.ApplyUpdateResult(result, manual); }));
                }
                catch (Exception)
                {
                    this.updateBusy = false;
                }
            });
        }

        private void ApplyUpdateResult(UpdateCheckResult result, bool manual)
        {
            this.updateBusy = false;
            if (this.exiting)
            {
                return;
            }

            string checkedAt = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (!result.Success)
            {
                // 실패해도 이전에 찾은 새 버전 정보는 유지한다. 자동 확인은 1시간 뒤 다시 시도한다.
                this.SetUpdateView(result.MessageKo + " (" + checkedAt + ")", UpdateTone.Error);
                this.ScheduleAutomaticUpdateCheck(UpdateCheckLogic.RetryInterval);
                return;
            }

            string current = "v" + this.currentVersion;
            if (result.Status == UpdateStatus.UpdateAvailable)
            {
                this.availableVersion = result.Latest;
                string latest = "v" + result.Latest;
                this.updateItem.Text = "새 버전 " + latest + " 내려받기";
                this.SetUpdateView(
                    "새 버전이 있습니다: " + latest + " (현재 " + current + "). '릴리스 페이지 열기'에서 내려받으세요.",
                    UpdateTone.Attention);

                // 같은 버전은 실행 중 한 번만 알린다. 설정 창을 보고 있으면 행 표시로 충분하다.
                if (this.notifiedVersion != latest && !(manual && this.form.Visible))
                {
                    this.notifiedVersion = latest;
                    this.ShowTrayNotice(
                        StartupConstants.ProductName + " 새 버전 " + latest,
                        "새 버전이 있습니다. 눌러서 릴리스 페이지를 엽니다. (현재 " + current + ")",
                        true);
                }
            }
            else
            {
                this.availableVersion = null;
                this.SetUpdateView(
                    result.Status == UpdateStatus.UpToDate
                        ? "최신 버전입니다. (" + current + ", " + checkedAt + " 확인)"
                        : "이 버전(" + current + ")이 게시된 최신 릴리스(v" + result.Latest + ")보다 새 버전입니다.",
                    UpdateTone.Success);
            }

            this.ScheduleAutomaticUpdateCheck(UpdateCheckLogic.CheckInterval);
        }

        // 설정 창 스위치: 일시정지 스위치처럼 즉시 저장한다. 편집 중인 단축키 값은 건드리지 않는다.
        public void SetUpdateCheckEnabled(MainForm source, bool enabled)
        {
            if (this.exiting)
            {
                return;
            }

            if (enabled == this.settings.CheckForUpdates)
            {
                source.SetUpdateCheckEnabledState(enabled);
                return;
            }

            AppSettings proposed = this.settings.Clone();
            proposed.CheckForUpdates = enabled;
            string saveError;
            if (!SettingsStore.TrySave(this.settingsPath, proposed, out saveError))
            {
                source.SetUpdateCheckEnabledState(this.settings.CheckForUpdates);
                this.SetUpdateView("설정을 저장하지 못했습니다: " + saveError, UpdateTone.Error);
                return;
            }

            this.settings = proposed;
            source.SetUpdateCheckEnabledState(enabled);
            if (!enabled)
            {
                this.updateTimer.Stop();
                if (!this.updateBusy && this.availableVersion == null)
                {
                    this.SetUpdateView(this.IdleUpdateText(), this.IdleUpdateTone());
                }

                return;
            }

            if (this.settingsOverridden)
            {
                this.SetUpdateView(this.IdleUpdateText(), this.IdleUpdateTone());
                return;
            }

            // 다시 켜면 바로 한 번 확인하고, 결과에 따라 다음 일정을 잡는다.
            this.StartUpdateCheck(false);
        }

        // 트레이 메뉴·알림·설정 창 버튼에서 공통으로 쓴다. 검증된 버전으로 만든 주소만 연다.
        public void OpenReleasePage(MainForm source)
        {
            if (this.exiting || this.availableVersion == null)
            {
                return;
            }

            string message;
            if (ShellLauncher.OpenReleasePage(UpdateCheckLogic.ReleasePageUrl(this.availableVersion), out message))
            {
                return;
            }

            this.SetUpdateView(message, UpdateTone.Error);
            if (!this.form.Visible)
            {
                this.ShowTrayNotice(StartupConstants.ProductName, message, false);
            }
        }

        private string IdleUpdateText()
        {
            if (this.currentVersion == null)
            {
                return "현재 버전 정보를 읽지 못해 새 버전을 확인할 수 없습니다.";
            }

            if (!this.settings.CheckForUpdates)
            {
                return "자동 확인이 꺼져 있습니다. '지금 확인'으로 직접 확인할 수 있습니다.";
            }

            if (this.settingsOverridden)
            {
                return "테스트 설정 폴더에서는 자동 확인을 하지 않습니다. '지금 확인'은 사용할 수 있습니다.";
            }

            return "잠시 후 새 버전을 확인합니다.";
        }

        private UpdateTone IdleUpdateTone()
        {
            return this.currentVersion == null ? UpdateTone.Error : UpdateTone.Normal;
        }

        private void SetUpdateView(string text, UpdateTone tone)
        {
            this.updateText = text;
            this.updateTone = tone;
            this.PushUpdateView();
        }

        private void PushUpdateView()
        {
            bool available = this.availableVersion != null;
            this.updateItem.Visible = available;
            this.updateSeparator.Visible = available;
            if (!this.form.IsDisposed)
            {
                this.form.SetUpdateState(this.updateText, this.updateTone, available, this.updateBusy);
            }
        }

        private void ShowTrayNotice(string title, string text, bool isUpdate)
        {
            try
            {
                this.updateBalloonPending = isUpdate;
                this.tray.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
            }
            catch (Exception)
            {
                // 알림 영역 오류가 단축키나 설정에 영향을 주면 안 된다.
                this.updateBalloonPending = false;
            }
        }

        #endregion

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

        // 설정 창 헤더 스위치: 트레이의 일시정지/다시 시작과 같은 경로로 즉시 적용한다.
        // 다시 시작이 등록 검증에서 실패하면 스위치를 실제 상태로 되돌린다.
        public void SetHotkeysEnabled(bool enabled)
        {
            if (this.exiting)
            {
                return;
            }

            if (enabled != this.settings.Enabled)
            {
                this.TogglePause();
            }

            this.form.SetEnabledState(this.settings.Enabled);
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
                this.form.SetEnabledState(this.settings.Enabled);
                this.UpdateStatusIndicators();

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
                this.UpdateStatusIndicators();
                this.UpdatePauseMenu();
                return;
            }

            AppSettings proposed = this.settings.Clone();
            proposed.Enabled = true;
            string registerError;
            if (!this.hotkeys.TryValidateTemporary(proposed, out registerError))
            {
                this.form.SetLastAction("단축키를 다시 시작하지 못했습니다: " + registerError, true);
                this.UpdateStatusIndicators();
                this.UpdatePauseMenu();
                return;
            }

            string saveError;
            if (!SettingsStore.TrySave(this.settingsPath, proposed, out saveError))
            {
                this.form.SetLastAction(
                    "단축키를 다시 시작하지 못했습니다. 상태를 저장할 수 없습니다: " + saveError,
                    true);
                this.UpdateStatusIndicators();
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
                    this.form.SetEnabledState(this.settings.Enabled);
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

            this.form.SetEnabledState(this.settings.Enabled);
            this.UpdateHotkeyLifecycle();
            this.UpdateStatusIndicators();
            this.form.SetLastAction("단축키를 다시 시작했습니다.", false);
        }

        private void UpdateHotkeyLifecycle()
        {
            if (this.hotkeys == null)
            {
                this.UpdateStatusIndicators();
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

            this.UpdateStatusIndicators();
            this.UpdatePauseMenu();
        }

        // 설정 창 상태 문구·배지와 트레이 표시가 같은 판정을 쓰도록 한곳에서 계산한다.
        private RuntimeState ComputeRuntimeState(out string text)
        {
            if (this.hookError != null)
            {
                text = this.hookError;
                return RuntimeState.Error;
            }

            if (!this.settings.Enabled)
            {
                text = "일시정지됨 - 단축키가 등록되지 않았습니다. 위쪽 '단축키 사용' 스위치나 트레이 메뉴의 '다시 시작'으로 켤 수 있습니다.";
                return RuntimeState.Paused;
            }

            if (this.suspended)
            {
                text = "설정 창이 활성 상태라 단축키를 잠시 해제했습니다. 다른 앱으로 돌아가면 다시 등록됩니다.";
                return RuntimeState.Suspended;
            }

            if (this.registrationError != null)
            {
                text = "단축키 충돌: " + this.registrationError;
                return RuntimeState.Conflict;
            }

            if (this.hotkeys != null && this.hotkeys.HasRegistrations)
            {
                text = "사용 중 - " + this.DisplayBindings() + " 가 시스템에 등록되어 있습니다.";
                return RuntimeState.Active;
            }

            text = "대기 중 - 단축키가 아직 등록되지 않았습니다.";
            return RuntimeState.Waiting;
        }

        // 상태를 한 번만 판정해 설정 창(배지·문구)과 트레이(툴팁·아이콘)에 함께 반영한다.
        private void UpdateStatusIndicators()
        {
            string text;
            RuntimeState state = this.ComputeRuntimeState(out text);
            this.form.SetRuntimeStatus(state, text);
            this.UpdateTray(state);
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

        private void UpdateTray(RuntimeState state)
        {
            if (this.tray == null)
            {
                return;
            }

            string text = StartupConstants.ProductName + " - "
                + (state == RuntimeState.Error ? "포커스 감시 오류" : UiTheme.StateLabel(state));

            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }

            Icon icon = state == RuntimeState.Active || state == RuntimeState.Suspended
                ? this.activeTrayIcon
                : (state == RuntimeState.Conflict || state == RuntimeState.Error ? this.errorTrayIcon : this.idleTrayIcon);

            try
            {
                // 포그라운드 변경마다 불리므로 바뀐 경우에만 셸 알림 영역을 갱신한다.
                if (this.tray.Text != text)
                {
                    this.tray.Text = text;
                }

                if (!object.ReferenceEquals(this.tray.Icon, icon))
                {
                    this.tray.Icon = icon;
                }
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

            // SystemEvents는 정적 이벤트라 구독을 해제하지 않으면 종료 후에도 참조가 남는다.
            ThemeManager.ThemeChanged -= this.themeChangedHandler;
            ThemeManager.StopWatching();

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

            this.updateTimer.Stop();
            this.updateTimer.Dispose();

            try
            {
                this.tray.Visible = false;
                this.tray.Dispose();
                this.activeTrayIcon.Dispose();
                this.idleTrayIcon.Dispose();
                this.errorTrayIcon.Dispose();
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
