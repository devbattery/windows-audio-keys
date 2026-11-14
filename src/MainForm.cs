// Windows Audio Keys - 설정 창(모던 라이트/다크, Windows 앱 테마 자동 전환).
// 헤더(상태·단축키 스위치) → 단축키 행(키 녹화) → 볼륨 단계 → 안내 → 시작과 상태 → 하단 바.
// 직접 그린 컨트롤은 그릴 때 팔레트를 읽고, 라벨 색은 등록된 적용 함수로 갱신한다.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    // 업데이트 상태 문구의 색 의미: 보통·최신·새 버전 있음·오류.
    internal enum UpdateTone
    {
        Normal,
        Success,
        Attention,
        Error
    }

    internal sealed class MainForm : Form
    {
        private const string HeroSuffix = " · 활성 창의 앱 소리만 조절합니다";

        private readonly TrayAppContext context;
        private readonly Icon windowIcon;

        // 테마가 바뀔 때 다시 실행할 색 적용 함수, 줄바꿈 폭을 맞출 라벨 목록
        private readonly List<Action<ThemePalette>> themeAppliers = new List<Action<ThemePalette>>();
        private readonly List<WrapTarget> wrapTargets = new List<WrapTarget>();

        private Panel scrollHost;
        private FooterPanel footer;
        private StatusDot heroDot;
        private Label heroStatus;
        private ToggleSwitch enabledSwitch;
        private KeyRecorder muteRecorder;
        private KeyRecorder volumeUpRecorder;
        private KeyRecorder volumeDownRecorder;
        private Stepper stepInput;
        private ToggleSwitch startupSwitch;
        private ModernButton startupRefreshButton;
        private Label startupStateLabel;
        private ToggleSwitch updateSwitch;
        private ModernButton updateCheckButton;
        private ModernButton updateOpenButton;
        private Label updateStateLabel;
        private Label stateLabel;
        private Label lastLabel;
        private Label dirtyLabel;
        private ModernButton revertButton;

        private bool startupBusy;
        private bool startupBlocked;
        private bool startupUnknown;

        // 변경 감지: 마지막으로 불러온(저장된) 설정과 현재 편집값 비교
        private AppSettings savedSettings;
        private bool loadingControls;

        // 테마가 바뀌면 같은 의미의 색으로 다시 칠하기 위해 마지막 상태를 기억한다.
        private RuntimeState? lastRuntimeState;
        private string lastRuntimeText;
        private bool lastActionError;
        private StartupTone startupTone = StartupTone.Normal;
        private UpdateTone updateTone = UpdateTone.Normal;

        public MainForm(TrayAppContext context)
        {
            this.context = context;
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Text = StartupConstants.ProductName + " " + Application.ProductVersion + " - 설정";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(720, 800);
            this.MinimumSize = new Size(700, 520);
            this.Font = UiTheme.BodyFont();
            this.windowIcon = AppIcons.Create(AppIcons.Accent, 32);
            this.Icon = this.windowIcon;
            this.ShowInTaskbar = true;
            this.MaximizeBox = true;
            this.Themed(delegate(ThemePalette p)
            {
                this.BackColor = p.Background;
                this.ForeColor = p.Text;
            });
            this.BuildUi();
            this.ResumeLayout(true);
        }

        private enum StartupTone
        {
            Normal,
            Success,
            Error
        }

        public void EnsureHandle()
        {
            // Creating the handle starts the hotkey lifecycle even when the window stays
            // hidden in the tray on later launches.
            this.Handle.ToInt64();
        }

        // 저장된(또는 방금 적용된) 설정을 표시한다. 이 값이 변경 감지의 기준이 된다.
        public void LoadSettingsIntoControls(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            this.savedSettings = settings.Clone();
            this.ApplyToControls(settings, true);
        }

        public AppSettings ReadSettingsFromControls()
        {
            AppSettings settings = new AppSettings();
            settings.Mute = this.muteRecorder.Binding;
            settings.VolumeUp = this.volumeUpRecorder.Binding;
            settings.VolumeDown = this.volumeDownRecorder.Binding;
            settings.Step = this.stepInput.Value;
            settings.Enabled = this.enabledSwitch.Checked;

            // 업데이트 자동 확인은 스위치를 누르는 즉시 저장된다. 저장 버튼이 이 값을
            // 되돌리지 않도록 마지막으로 저장된 값을 그대로 쓴다.
            settings.CheckForUpdates = this.savedSettings != null
                ? this.savedSettings.CheckForUpdates
                : this.updateSwitch.Checked;
            return settings;
        }

        // 업데이트 자동 확인 스위치가 즉시 저장된 뒤(또는 저장 실패로 되돌릴 때) 호출된다.
        public void SetUpdateCheckEnabledState(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(this.SetUpdateCheckEnabledState), new object[] { enabled });
                return;
            }

            if (this.savedSettings != null)
            {
                this.savedSettings.CheckForUpdates = enabled;
            }

            this.updateSwitch.Checked = enabled;
            this.UpdateDirtyState();
        }

        // 업데이트 상태 문구, 새 버전 버튼 표시, 확인 중 버튼 잠금을 한 번에 갱신한다.
        public void SetUpdateState(string text, UpdateTone tone, bool updateAvailable, bool busy)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(
                    new Action<string, UpdateTone, bool, bool>(this.SetUpdateState),
                    new object[] { text, tone, updateAvailable, busy });
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            this.updateStateLabel.Text = text;
            this.updateTone = tone;
            if (this.updateOpenButton.Visible != updateAvailable)
            {
                // 포커스가 있는 버튼을 숨기면 다음 탭 위치(종료 버튼 등)로 포커스가 넘어가므로
                // 먼저 옆의 '지금 확인'으로 옮기거나 창에 맡긴다.
                if (!updateAvailable && this.updateOpenButton.Focused)
                {
                    if (!busy && this.updateCheckButton.CanFocus)
                    {
                        this.updateCheckButton.Focus();
                    }
                    else
                    {
                        this.ActiveControl = null;
                    }
                }

                this.updateOpenButton.Visible = updateAvailable;
            }

            this.SetEnabledKeepingFocus(this.updateCheckButton, !busy);
            this.ApplyDynamicColors(ThemeManager.Current);
        }

        // 헤더 스위치·트레이 일시정지로 사용 여부만 바뀐 경우. 편집 중인 단축키 값은 그대로 둔다.
        public void SetEnabledState(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(this.SetEnabledState), new object[] { enabled });
                return;
            }

            if (this.savedSettings != null)
            {
                this.savedSettings.Enabled = enabled;
            }

            this.enabledSwitch.Checked = enabled;
            this.UpdateDirtyState();
        }

        public void SetRuntimeStatus(RuntimeState state, string text)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<RuntimeState, string>(this.SetRuntimeStatus), new object[] { state, text });
                return;
            }

            // 포그라운드가 바뀔 때마다 호출되므로 값이 같으면 다시 그리지 않는다.
            if (this.lastRuntimeState == state && this.lastRuntimeText == text)
            {
                return;
            }

            this.lastRuntimeState = state;
            this.lastRuntimeText = text;
            this.heroDot.State = state;
            this.heroStatus.Text = UiTheme.StateLabel(state) + HeroSuffix;
            this.heroStatus.AccessibleName = "현재 상태: " + UiTheme.StateLabel(state);
            this.stateLabel.Text = text;
            this.ApplyDynamicColors(ThemeManager.Current);
        }

        public void SetLastAction(string text, bool error)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string, bool>(this.SetLastAction), new object[] { text, error });
                return;
            }

            this.lastActionError = error;
            this.lastLabel.Text = text;
            this.ApplyDynamicColors(ThemeManager.Current);
        }

        // The switch mirrors the ACTUAL Task Scheduler state read by the background
        // query. An owned task that is enabled but still points at an old location is
        // shown on (its actual state) while the label separately asks for a refresh.
        // Unreadable or foreign tasks show an indeterminate, disabled switch.
        public void SetStartupState(StartupTaskState state)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<StartupTaskState>(this.SetStartupState), new object[] { state });
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            bool known = state != null && state.CheckboxKnown;
            this.startupUnknown = !known;
            this.startupSwitch.Indeterminate = !known;
            this.startupSwitch.Checked = known && state.CheckboxChecked;
            this.startupRefreshButton.Visible = state != null
                && (state.Kind == StartupTaskStateKind.NeedsRefresh || state.Kind == StartupTaskStateKind.Mismatch);
            this.startupStateLabel.Text = state == null ? "상태 확인 중..." : state.MessageKo;
            this.startupTone = state != null && state.IsError
                ? StartupTone.Error
                : (state != null && state.IsEnabled ? StartupTone.Success : StartupTone.Normal);
            this.ApplyDynamicColors(ThemeManager.Current);
            this.UpdateStartupControls();
        }

        public void SetStartupMessage(string text, bool error)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string, bool>(this.SetStartupMessage), new object[] { text, error });
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            this.startupStateLabel.Text = text;
            this.startupTone = error ? StartupTone.Error : StartupTone.Normal;
            this.ApplyDynamicColors(ThemeManager.Current);
        }

        public void SetStartupUnavailable(string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(this.SetStartupUnavailable), new object[] { message });
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            this.startupBlocked = true;
            this.startupUnknown = true;
            this.startupSwitch.Indeterminate = true;
            this.startupRefreshButton.Visible = false;
            this.startupStateLabel.Text = message;
            this.startupTone = StartupTone.Error;
            this.ApplyDynamicColors(ThemeManager.Current);
            this.UpdateStartupControls();
        }

        public void SetStartupBusy(bool busy)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(this.SetStartupBusy), new object[] { busy });
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            this.startupBusy = busy;
            this.UpdateStartupControls();
        }

        private void UpdateStartupControls()
        {
            if (this.startupSwitch == null)
            {
                return;
            }

            bool usable = !this.startupBusy && !this.startupBlocked && !this.startupUnknown;
            this.SetEnabledKeepingFocus(this.startupSwitch, usable);
            this.SetEnabledKeepingFocus(this.startupRefreshButton, usable);
        }

        // 포커스가 있는 컨트롤을 비활성화하면 WinForms가 다음 탭 위치(하단 '종료' 버튼 등)로
        // 포커스를 옮긴다. 그 상태에서 Space를 다시 누르면 엉뚱한 버튼이 눌리므로, 잠시 창 자체에
        // 포커스를 두었다가 컨트롤이 다시 켜지면 돌려준다.
        private Control focusToRestore;

        private void SetEnabledKeepingFocus(Control control, bool enabled)
        {
            if (control.Enabled == enabled)
            {
                return;
            }

            if (!enabled && control.Focused)
            {
                this.focusToRestore = control;
                this.ActiveControl = null;
            }

            control.Enabled = enabled;

            // 그사이 사용자가 다른 곳으로 포커스를 옮겼으면(창에 맡겨 둔 상태가 아니면) 돌려주지 않는다.
            if (enabled && object.ReferenceEquals(this.focusToRestore, control))
            {
                this.focusToRestore = null;
                if (this.ContainsFocus && this.ActiveControl == null && control.Visible && control.CanFocus)
                {
                    control.Focus();
                }
            }
        }

        public void ShowSettings()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(this.ShowSettings));
                return;
            }

            // ShowInTaskbar를 바꾸면 창 핸들 전체가 다시 만들어지고(자식 창 수십 개 재생성,
            // 단축키 재등록) 느려진다. 숨긴 창에는 작업 표시줄 단추가 없으므로 Show/Hide만 쓴다.
            if (!this.Visible)
            {
                this.Show();
            }

            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            this.Activate();
            this.BringToFront();
            this.context.RefreshStartupState(this);
        }

        public void HideToTray()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(this.HideToTray));
                return;
            }

            this.Hide();
        }

        // Windows 앱 테마(라이트/다크/고대비)가 바뀌면 색과 제목 표시줄을 다시 적용한다.
        public void ApplyTheme()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(this.ApplyTheme));
                return;
            }

            if (this.IsDisposed)
            {
                return;
            }

            ThemePalette p = ThemeManager.Current;
            this.SuspendLayout();
            for (int i = 0; i < this.themeAppliers.Count; i++)
            {
                this.themeAppliers[i](p);
            }

            this.ApplyDynamicColors(p);
            this.ResumeLayout(false);
            ThemeManager.ApplyTitleBar(this);
            ThemeManager.ApplyScrollBars(this.scrollHost);
            this.Invalidate(true);
        }

        // 고배율(예: 1080p 150%)에서는 기본 크기가 작업 영역보다 클 수 있다. 제목 표시줄과
        // 하단 버튼이 화면 밖으로 나가지 않도록 처음 표시할 때 작업 영역 안으로 줄인다.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(this.Width, area.Width);
            int height = Math.Min(this.Height, area.Height);
            if (width != this.Width || height != this.Height)
            {
                this.Size = new Size(width, height);
                this.Location = new Point(
                    area.Left + (area.Width - width) / 2,
                    area.Top + (area.Height - height) / 2);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // ShowInTaskbar 변경 등으로 핸들이 다시 만들어질 때마다 제목 표시줄 테마를 적용한다.
            ThemeManager.ApplyTitleBar(this);
            this.context.OnFormHandleCreated();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            this.context.OnFormHandleDestroyed();
            base.OnHandleDestroyed(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            this.context.RefreshForegroundState();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            this.context.RefreshForegroundState();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized && this.Visible)
            {
                this.WindowState = FormWindowState.Normal;
                this.HideToTray();
                return;
            }

            this.UpdateWrappingWidths();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!this.context.IsExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.HideToTray();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                this.context.HandleHotkey(id);

                // 볼륨 키를 누르고 있으면 처리하는 동안 반복 입력이 쌓인다. 쌓인 것을 그대로
                // 처리하면 키를 놓은 뒤에도 볼륨이 계속 바뀌므로, 처리 중 쌓인 같은 키 입력은
                // 버린다(덜 바꾸는 쪽). 계속 누르고 있으면 다음 반복 입력이 이어서 처리된다.
                // 음소거는 MOD_NOREPEAT로 등록되어 반복 입력이 없다. 쌓인 음소거는 실제로 따로 누른
                // 입력이므로 버리지 않는다.
                if (id == HotkeyManager.VolumeUpId || id == HotkeyManager.VolumeDownId)
                {
                    Native.DiscardQueuedHotkeys(this.Handle, id, 64);
                }

                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                this.windowIcon.Dispose();
            }
        }

        #region 화면 구성

        private void BuildUi()
        {
            // 스크롤 영역: 창이 내용보다 작으면 세로 스크롤바가 생긴다. TableLayoutPanel의
            // AutoScroll은 비율 행과 함께 쓰면 스크롤바를 만들지 않으므로 Panel이 담당한다.
            this.scrollHost = new Panel();
            this.scrollHost.Dock = DockStyle.Fill;
            this.scrollHost.AutoScroll = true;
            this.scrollHost.Padding = new Padding(28, 12, 28, 16);
            this.Themed(delegate(ThemePalette p) { this.scrollHost.BackColor = p.Background; });
            this.scrollHost.HandleCreated += delegate { ThemeManager.ApplyScrollBars(this.scrollHost); };

            TableLayoutPanel root = NewGrid(1);
            root.Dock = DockStyle.Top;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.Controls.Add(this.BuildHero(), 0, 0);
            root.Controls.Add(this.BuildSectionHeader("단축키", true), 0, 1);
            root.Controls.Add(this.BuildHotkeyRows(), 0, 2);
            root.Controls.Add(this.BuildInfoBar(), 0, 3);
            root.Controls.Add(this.BuildSectionHeader("시작과 상태", false), 0, 4);
            root.Controls.Add(this.BuildStatusRows(), 0, 5);

            Label hint = this.NewLabel(
                "창을 닫아도 트레이에서 계속 실행됩니다. 완전히 끝내려면 '종료'를 누르거나 트레이 아이콘 메뉴에서 '종료'를 선택하세요. "
                + "설정은 %LOCALAPPDATA%\\GameAudioKeys\\settings.json 에 저장되며, 자동 시작 작업은 Windows 작업 스케줄러에 등록됩니다.",
                UiTheme.SmallFont(),
                delegate(ThemePalette p) { return p.SubText; });
            hint.Margin = new Padding(2, 14, 2, 0);
            this.wrapTargets.Add(new WrapTarget(hint, null));
            root.Controls.Add(hint, 0, 6);
            this.scrollHost.Controls.Add(root);

            // 본문(Fill)과 하단 바(Bottom). 하단 바가 먼저 아래 공간을 차지한다.
            this.Controls.Add(this.scrollHost);
            this.Controls.Add(this.BuildFooter());
            this.scrollHost.Layout += delegate { this.UpdateWrappingWidths(); };
        }

        private Control BuildHero()
        {
            RoundedPanel card = NewCard(10);
            card.Padding = new Padding(20, 18, 20, 18);

            TableLayoutPanel grid = NewGrid(3);
            grid.Dock = DockStyle.Fill;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            GlyphTile logo = new GlyphTile(
                GlyphKind.Speaker,
                delegate(ThemePalette p) { return p.Accent; },
                delegate(ThemePalette p) { return p.OnAccent; },
                56);
            logo.Anchor = AnchorStyles.Left;
            logo.Margin = new Padding(0, 0, 18, 0);
            grid.Controls.Add(logo, 0, 0);

            TableLayoutPanel titles = NewGrid(1);
            titles.Anchor = AnchorStyles.Left;
            Label title = this.NewLabel(StartupConstants.ProductName, UiTheme.TitleFont(), null);
            title.Margin = new Padding(0, 0, 0, 4);
            titles.Controls.Add(title, 0, 0);

            TableLayoutPanel statusLine = NewGrid(2);
            this.heroDot = new StatusDot();
            this.heroDot.Anchor = AnchorStyles.Left;
            this.heroDot.Margin = new Padding(1, 0, 8, 0);
            statusLine.Controls.Add(this.heroDot, 0, 0);
            this.heroStatus = this.NewLabel("대기 중" + HeroSuffix, null, delegate(ThemePalette p) { return p.SubText; });
            statusLine.Controls.Add(this.heroStatus, 1, 0);
            titles.Controls.Add(statusLine, 0, 1);
            grid.Controls.Add(titles, 1, 0);

            this.enabledSwitch = new ToggleSwitch("단축키 사용");
            this.enabledSwitch.Anchor = AnchorStyles.Right;
            this.enabledSwitch.Margin = new Padding(12, 0, 0, 0);
            this.enabledSwitch.UserToggled += delegate
            {
                this.context.SetHotkeysEnabled(this.enabledSwitch.Checked);
            };
            grid.Controls.Add(this.enabledSwitch, 2, 0);

            card.Controls.Add(grid);
            return card;
        }

        private Control BuildSectionHeader(string text, bool withDefaults)
        {
            TableLayoutPanel header = NewGrid(2);
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(2, 18, 0, 6);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label label = this.NewLabel(text, UiTheme.SectionFont(), null);
            header.Controls.Add(label, 0, 0);

            if (withDefaults)
            {
                ModernButton defaults = new ModernButton("기본값으로", ModernButtonKind.Text);
                defaults.Anchor = AnchorStyles.Right;
                defaults.Margin = new Padding(0);
                defaults.AccessibleName = "단축키와 볼륨 단계를 기본값으로";
                defaults.Click += delegate
                {
                    this.ApplyToControls(AppSettings.CreateDefaults(), false);
                    this.SetLastAction("기본값을 불러왔습니다. '저장'을 눌러 적용하세요.", false);
                };
                header.Controls.Add(defaults, 1, 0);
            }

            return header;
        }

        private Control BuildHotkeyRows()
        {
            TableLayoutPanel rows = NewGrid(1);
            rows.Dock = DockStyle.Fill;
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            this.muteRecorder = this.NewRecorder("음소거 전환");
            rows.Controls.Add(this.NewRow(GlyphKind.Mute, "음소거 전환", "활성 앱 음소거를 켜고 끕니다", this.muteRecorder), 0, 0);
            this.volumeUpRecorder = this.NewRecorder("볼륨 올리기");
            rows.Controls.Add(this.NewRow(GlyphKind.VolumeUp, "볼륨 올리기", "단계만큼 높입니다", this.volumeUpRecorder), 0, 1);
            this.volumeDownRecorder = this.NewRecorder("볼륨 내리기");
            rows.Controls.Add(this.NewRow(GlyphKind.VolumeDown, "볼륨 내리기", "단계만큼 낮춥니다", this.volumeDownRecorder), 0, 2);

            this.stepInput = new Stepper(1, 20, "%", "볼륨 단계");
            this.stepInput.ValueChanged += delegate { this.UpdateDirtyState(); };
            rows.Controls.Add(this.NewRow(null, "볼륨 단계", "키를 한 번 누를 때 바뀌는 양 (1~20%)", this.stepInput), 0, 3);
            return rows;
        }

        private KeyRecorder NewRecorder(string rowName)
        {
            KeyRecorder recorder = new KeyRecorder(rowName);
            recorder.Anchor = AnchorStyles.Right;
            recorder.BindingChanged += delegate { this.UpdateDirtyState(); };
            return recorder;
        }

        private Control BuildInfoBar()
        {
            RoundedPanel bar = new RoundedPanel(
                delegate(ThemePalette p) { return p.InfoFill; },
                delegate(ThemePalette p) { return p.Kind == ThemeKind.HighContrast ? p.CardBorder : p.InfoFill; },
                8);
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Padding = new Padding(14, 10, 14, 10);
            bar.Margin = new Padding(0, 6, 0, 0);

            TableLayoutPanel grid = NewGrid(2);
            grid.Dock = DockStyle.Fill;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            GlyphTile icon = new GlyphTile(GlyphKind.Info, null, delegate(ThemePalette p) { return p.InfoText; }, 18);
            icon.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            icon.Margin = new Padding(0, 1, 10, 0);
            grid.Controls.Add(icon, 0, 0);

            Label text = this.NewLabel(
                "켜져 있는 동안 선택한 키는 시스템 전역에서 예약되어 다른 앱의 원래 기능 대신 쓰입니다. "
                + "이 창이 앞에 있거나 일시정지 중에는 해제됩니다. 대상 앱에 오디오 세션이 없어도 키는 예약됩니다.",
                UiTheme.SmallFont(),
                delegate(ThemePalette p) { return p.InfoText; });
            text.Margin = new Padding(0);
            grid.Controls.Add(text, 1, 0);
            // 글자 영역 = 안내 표(grid). 같은 표의 아이콘 폭과 안내 바 여백을 뺀다.
            this.wrapTargets.Add(new WrapTarget(text, grid, icon));

            bar.Controls.Add(grid);
            return bar;
        }

        private Control BuildStatusRows()
        {
            TableLayoutPanel rows = NewGrid(1);
            rows.Dock = DockStyle.Fill;
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            this.startupSwitch = new ToggleSwitch("Windows 로그인 시 관리자 권한으로 자동 시작");
            this.startupSwitch.Anchor = AnchorStyles.Right;
            this.startupSwitch.Margin = new Padding(0);
            this.startupSwitch.UserToggled += delegate
            {
                this.context.ApplyStartupSetting(this, this.startupSwitch.Checked);
            };

            // 실행 파일을 옮겼거나 구성이 달라진 작업은 켜진 상태 그대로 "다시 등록"해야 한다.
            // 스위치는 켬/끔만 바꾸므로 이때만 별도 버튼을 보여 준다.
            this.startupRefreshButton = new ModernButton("다시 등록", ModernButtonKind.Secondary);
            this.startupRefreshButton.Anchor = AnchorStyles.Right;
            this.startupRefreshButton.Margin = new Padding(0, 0, 12, 0);
            this.startupRefreshButton.AccessibleName = "자동 시작 작업을 현재 위치로 다시 등록";
            this.startupRefreshButton.Visible = false;
            this.startupRefreshButton.Click += delegate
            {
                this.context.ApplyStartupSetting(this, true);
            };

            TableLayoutPanel startupControls = NewGrid(2);
            startupControls.Anchor = AnchorStyles.Right;
            startupControls.Controls.Add(this.startupRefreshButton, 0, 0);
            startupControls.Controls.Add(this.startupSwitch, 1, 0);
            TableLayoutPanel startupText;
            rows.Controls.Add(
                this.NewRow(
                    GlyphKind.Power,
                    "Windows 로그인 시 자동 시작",
                    "관리자 권한으로 시작합니다. 보안을 위해 Program Files 폴더 아래에 설치한 경우에만 켤 수 있으며, "
                        + "켜고 끌 때 Windows 승인이 필요할 수 있습니다. 현재 권한: "
                        + (StartupTaskHost.IsElevated() ? "관리자" : "일반 사용자"),
                    startupControls,
                    out startupText),
                0,
                0);
            this.startupStateLabel = this.NewLabel("상태 확인 중...", UiTheme.SmallFont(), null);
            this.startupStateLabel.Margin = new Padding(0, 4, 0, 0);
            this.startupStateLabel.AccessibleName = "자동 시작 상태";
            startupText.Controls.Add(this.startupStateLabel, 0, 2);
            this.AddWrapForRowText(this.startupStateLabel, startupText);

            rows.Controls.Add(this.BuildUpdateRow(), 0, 1);

            TableLayoutPanel stateText;
            rows.Controls.Add(this.NewRow(GlyphKind.Activity, "현재 상태", null, null, out stateText), 0, 2);
            this.stateLabel = this.NewLabel("상태 확인 중...", UiTheme.SmallFont(), null);
            this.stateLabel.Margin = new Padding(0, 2, 0, 0);
            this.stateLabel.AccessibleName = "단축키 상태";
            stateText.Controls.Add(this.stateLabel, 0, 1);
            this.AddWrapForRowText(this.stateLabel, stateText);

            TableLayoutPanel lastText;
            rows.Controls.Add(this.NewRow(GlyphKind.Speaker, "마지막 동작", null, null, out lastText), 0, 3);
            this.lastLabel = this.NewLabel("없음", UiTheme.SmallFont(), null);
            this.lastLabel.Margin = new Padding(0, 2, 0, 0);
            this.lastLabel.AccessibleName = "마지막 동작";
            lastText.Controls.Add(this.lastLabel, 0, 1);
            this.AddWrapForRowText(this.lastLabel, lastText);
            return rows;
        }

        // 업데이트 행: [아이콘] [제목·설명·상태·버튼 줄] [자동 확인 스위치].
        // 버튼을 오른쪽 열이 아닌 글자 영역 아래에 두어 최소 창 폭에서도 설명이 좁아지지 않게 한다.
        private Control BuildUpdateRow()
        {
            this.updateSwitch = new ToggleSwitch("새 버전 자동 확인");
            this.updateSwitch.Anchor = AnchorStyles.Right;
            this.updateSwitch.UserToggled += delegate
            {
                this.context.SetUpdateCheckEnabled(this, this.updateSwitch.Checked);
            };

            TableLayoutPanel updateText;
            Control row = this.NewRow(
                GlyphKind.Info,
                "새 버전 알림",
                "하루 한 번 GitHub에서 새 버전을 확인해 알려 줍니다. 설치는 직접 합니다. 현재 버전: v"
                    + Application.ProductVersion,
                this.updateSwitch,
                out updateText);

            this.updateStateLabel = this.NewLabel("아직 확인하지 않았습니다.", UiTheme.SmallFont(), null);
            this.updateStateLabel.Margin = new Padding(0, 4, 0, 0);
            this.updateStateLabel.AccessibleName = "업데이트 상태";
            updateText.Controls.Add(this.updateStateLabel, 0, 2);
            this.AddWrapForRowText(this.updateStateLabel, updateText);

            this.updateCheckButton = new ModernButton("지금 확인", ModernButtonKind.Secondary);
            this.updateCheckButton.Margin = new Padding(0, 0, 8, 0);
            this.updateCheckButton.AccessibleName = "새 버전 지금 확인";
            this.updateCheckButton.Click += delegate { this.context.CheckForUpdatesNow(); };

            this.updateOpenButton = new ModernButton("릴리스 페이지 열기", ModernButtonKind.Primary);
            this.updateOpenButton.Margin = new Padding(0);
            this.updateOpenButton.AccessibleName = "새 버전 릴리스 페이지 열기";
            this.updateOpenButton.Visible = false;
            this.updateOpenButton.Click += delegate { this.context.OpenReleasePage(this); };

            TableLayoutPanel buttons = NewGrid(2);
            buttons.Anchor = AnchorStyles.Left;
            buttons.Margin = new Padding(0, 8, 0, 0);
            buttons.Controls.Add(this.updateCheckButton, 0, 0);
            buttons.Controls.Add(this.updateOpenButton, 1, 0);
            updateText.Controls.Add(buttons, 0, 3);
            return row;
        }

        private Control BuildFooter()
        {
            this.footer = new FooterPanel();
            this.footer.Dock = DockStyle.Bottom;
            this.footer.AutoSize = true;
            this.footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.footer.Padding = new Padding(28, 12, 28, 12);
            this.Themed(delegate(ThemePalette p) { this.footer.BackColor = p.Footer; });

            TableLayoutPanel bar = NewGrid(5);
            bar.Dock = DockStyle.Fill;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            ModernButton exit = new ModernButton("종료", ModernButtonKind.Secondary);
            exit.Margin = new Padding(0);
            exit.AccessibleName = "프로그램 종료";
            exit.Click += delegate { this.context.ExitApp(); };
            bar.Controls.Add(exit, 0, 0);

            this.dirtyLabel = this.NewLabel("저장하지 않은 변경 사항", UiTheme.StrongFont(), delegate(ThemePalette p) { return p.Warning; });
            this.dirtyLabel.Anchor = AnchorStyles.Right;
            this.dirtyLabel.Margin = new Padding(8, 0, 4, 0);
            this.dirtyLabel.Visible = false;
            bar.Controls.Add(this.dirtyLabel, 2, 0);

            this.revertButton = new ModernButton("되돌리기", ModernButtonKind.Secondary);
            this.revertButton.Anchor = AnchorStyles.Right;
            this.revertButton.AccessibleName = "저장된 설정으로 되돌리기";
            this.revertButton.Enabled = false;
            this.revertButton.Click += delegate
            {
                if (this.savedSettings != null)
                {
                    this.ApplyToControls(this.savedSettings, false);
                    this.SetLastAction("저장된 설정으로 되돌렸습니다.", false);
                }
            };
            bar.Controls.Add(this.revertButton, 3, 0);

            ModernButton save = new ModernButton("저장", ModernButtonKind.Primary);
            save.Anchor = AnchorStyles.Right;
            save.Font = UiTheme.StrongFont();
            save.AccessibleName = "단축키 설정 저장 및 적용";
            save.Click += delegate { this.context.SaveSettingsFromUi(this); };
            bar.Controls.Add(save, 4, 0);

            this.footer.Controls.Add(bar);
            return this.footer;
        }

        #endregion

        #region 보조 생성 함수

        private Control NewRow(GlyphKind? glyph, string title, string description, Control right)
        {
            TableLayoutPanel ignored;
            return this.NewRow(glyph, title, description, right, out ignored);
        }

        // 설정 앱식 행 카드: [아이콘] [제목/설명] [오른쪽 컨트롤]. 설명 아래에 줄을 더 붙일 수 있게
        // 글자 영역 표를 돌려준다(0행 제목, 1행 설명).
        private Control NewRow(GlyphKind? glyph, string title, string description, Control right, out TableLayoutPanel text)
        {
            RoundedPanel card = NewCard(6);
            card.Margin = new Padding(0, 0, 0, 4);

            TableLayoutPanel grid = NewGrid(3);
            grid.Dock = DockStyle.Fill;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            if (glyph.HasValue)
            {
                GlyphTile tile = new GlyphTile(
                    glyph.Value,
                    delegate(ThemePalette p) { return p.IconTile; },
                    delegate(ThemePalette p) { return p.Accent; },
                    36);
                tile.Anchor = AnchorStyles.Left;
                tile.Margin = new Padding(0, 0, 16, 0);
                grid.Controls.Add(tile, 0, 0);
            }

            text = NewGrid(1);
            text.Anchor = AnchorStyles.Left;
            text.Margin = new Padding(0, 2, 12, 2);
            text.Controls.Add(this.NewLabel(title, null, null), 0, 0);
            if (description != null)
            {
                Label desc = this.NewLabel(description, UiTheme.SmallFont(), delegate(ThemePalette p) { return p.SubText; });
                desc.Margin = new Padding(0, 2, 0, 0);
                text.Controls.Add(desc, 0, 1);
                this.AddWrapForRowText(desc, text);
            }

            grid.Controls.Add(text, 1, 0);
            if (right != null)
            {
                right.Margin = new Padding(0);
                grid.Controls.Add(right, 2, 0);
            }

            card.Controls.Add(grid);
            return card;
        }

        private static RoundedPanel NewCard(int radius)
        {
            RoundedPanel card = new RoundedPanel(
                delegate(ThemePalette p) { return p.Card; },
                delegate(ThemePalette p) { return p.CardBorder; },
                radius);
            card.Dock = DockStyle.Fill;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.Padding = new Padding(16, 12, 16, 12);
            card.Margin = new Padding(0);
            return card;
        }

        private static TableLayoutPanel NewGrid(int columns)
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.ColumnCount = columns;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);
            grid.Padding = new Padding(0);
            return grid;
        }

        // 투명 배경 라벨. 글자색 함수가 없으면 부모(폼)의 기본 글자색을 물려받는다.
        private Label NewLabel(string text, Font font, Func<ThemePalette, Color> fore)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.BackColor = Color.Transparent;
            label.Anchor = AnchorStyles.Left;
            label.UseMnemonic = false;
            label.Margin = new Padding(0);
            if (font != null)
            {
                label.Font = font;
            }

            if (fore != null)
            {
                this.Themed(delegate(ThemePalette p) { label.ForeColor = fore(p); });
            }

            return label;
        }

        private void Themed(Action<ThemePalette> apply)
        {
            this.themeAppliers.Add(apply);
            apply(ThemeManager.Current);
        }

        // 상태에 따라 색이 달라지는 라벨(현재 상태·마지막 동작·자동 시작 상태)
        private void ApplyDynamicColors(ThemePalette p)
        {
            if (this.stateLabel != null)
            {
                RuntimeState state = this.lastRuntimeState ?? RuntimeState.Waiting;
                this.stateLabel.ForeColor = state == RuntimeState.Active || state == RuntimeState.Conflict || state == RuntimeState.Error
                    ? p.StateColor(state)
                    : p.SubText;
            }

            if (this.lastLabel != null)
            {
                this.lastLabel.ForeColor = this.lastActionError ? p.Error : p.SubText;
            }

            if (this.startupStateLabel != null)
            {
                this.startupStateLabel.ForeColor = this.startupTone == StartupTone.Error
                    ? p.Error
                    : (this.startupTone == StartupTone.Success ? p.Success : p.SubText);
            }

            if (this.updateStateLabel != null)
            {
                this.updateStateLabel.ForeColor = this.updateTone == UpdateTone.Error
                    ? p.Error
                    : (this.updateTone == UpdateTone.Success
                        ? p.Success
                        : (this.updateTone == UpdateTone.Attention ? p.Accent : p.SubText));
            }
        }

        private void AddWrapForRowText(Label label, TableLayoutPanel text)
        {
            this.wrapTargets.Add(new WrapTarget(label, text));
        }

        #endregion

        #region 줄바꿈·변경 감지

        // 자동 크기 라벨은 MaximumSize 폭이 있어야 줄바꿈한다. 카드 안 실제 글자 영역 폭에 맞춘다.
        private bool updatingWraps;

        private void UpdateWrappingWidths()
        {
            // MaximumSize 변경이 레이아웃을 다시 일으켜 이 함수가 재진입하므로 한 번만 돈다.
            if (this.scrollHost == null || this.updatingWraps)
            {
                return;
            }

            this.updatingWraps = true;
            try
            {
                this.UpdateWrappingWidthsCore();
            }
            finally
            {
                this.updatingWraps = false;
            }
        }

        private void UpdateWrappingWidthsCore()
        {

            // 세로 스크롤바가 보이면 ClientSize가 그만큼 줄어든 값이다.
            int content = this.scrollHost.ClientSize.Width - this.scrollHost.Padding.Horizontal;
            if (content <= 0)
            {
                return;
            }

            for (int i = 0; i < this.wrapTargets.Count; i++)
            {
                WrapTarget target = this.wrapTargets[i];
                int width = Math.Max(160, content - target.Reserved());
                if (target.Label.MaximumSize.Width != width)
                {
                    target.Label.MaximumSize = new Size(width, 0);
                }
            }
        }

        private void ApplyToControls(AppSettings settings, bool includeEnabled)
        {
            this.loadingControls = true;
            try
            {
                this.muteRecorder.Binding = settings.Mute;
                this.volumeUpRecorder.Binding = settings.VolumeUp;
                this.volumeDownRecorder.Binding = settings.VolumeDown;
                this.stepInput.Value = Math.Max(1, Math.Min(20, settings.Step));
                if (includeEnabled)
                {
                    this.enabledSwitch.Checked = settings.Enabled;
                    this.updateSwitch.Checked = settings.CheckForUpdates;
                }
            }
            finally
            {
                this.loadingControls = false;
            }

            this.UpdateDirtyState();
        }

        private void UpdateDirtyState()
        {
            if (this.loadingControls || this.dirtyLabel == null || this.savedSettings == null)
            {
                return;
            }

            bool dirty = !AppSettings.AreEquivalent(this.ReadSettingsFromControls(), this.savedSettings);
            if (this.dirtyLabel.Visible != dirty)
            {
                this.dirtyLabel.Visible = dirty;
            }

            this.revertButton.Enabled = dirty;
        }

        #endregion

        // 줄바꿈 대상 라벨과, 같은 카드 안에서 라벨 폭을 줄이는 요소(카드 여백·아이콘·오른쪽 컨트롤).
        private sealed class WrapTarget
        {
            public readonly Label Label;
            private readonly Control textArea;
            private readonly Control[] siblings;

            public WrapTarget(Label label, Control textArea, params Control[] siblings)
            {
                this.Label = label;
                this.textArea = textArea;
                this.siblings = siblings;
            }

            public int Reserved()
            {
                int reserved = this.Label.Margin.Horizontal + 2;
                if (this.textArea == null)
                {
                    return reserved;
                }

                // 글자 영역이 들어 있는 행 표의 다른 열(아이콘·오른쪽 컨트롤)과 카드 여백을 뺀다.
                RoundedPanel card = null;
                for (Control c = this.textArea; c != null; c = c.Parent)
                {
                    card = c as RoundedPanel;
                    if (card != null)
                    {
                        break;
                    }
                }

                reserved += this.textArea.Margin.Horizontal;
                if (card != null)
                {
                    reserved += card.Padding.Horizontal + 2;
                }

                TableLayoutPanel row = this.textArea.Parent as TableLayoutPanel;
                if (row != null)
                {
                    foreach (Control other in row.Controls)
                    {
                        if (other != this.textArea && other.Visible)
                        {
                            reserved += other.Width + other.Margin.Horizontal;
                        }
                    }
                }

                if (this.siblings != null)
                {
                    for (int i = 0; i < this.siblings.Length; i++)
                    {
                        reserved += this.siblings[i].Width + this.siblings[i].Margin.Horizontal;
                    }
                }

                return reserved;
            }
        }
    }
}
