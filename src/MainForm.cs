// Windows Audio Keys - 설정 창. 헤더(상태 배지) → 주의 안내 → 단축키 카드 →
// 로그인 자동 시작 카드 → 상태 카드 → 하단 동작 바 순서의 카드형 레이아웃.
// 입력은 기본 WinForms 컨트롤이며, TableLayout/AutoSize로 크기·DPI 변경에 대응한다.

using System;
using System.Drawing;
using System.Windows.Forms;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    internal sealed class MainForm : Form
    {
        private readonly TrayAppContext context;
        private readonly Icon windowIcon;
        private Panel scrollHost;
        private TableLayoutPanel root;
        private BindingRow muteRow;
        private BindingRow volumeUpRow;
        private BindingRow volumeDownRow;
        private NumericUpDown stepInput;
        private CheckBox enabledInput;
        private CheckBox startupInput;
        private Button startupApply;
        private Label startupStateLabel;
        private Label startupNoteLabel;
        private RoundedPanel noticePanel;
        private GlyphTile noticeIcon;
        private Label noticeLabel;
        private StatusPill statusPill;
        private RoundedPanel statusCard;
        private Label stateLabel;
        private Label lastCaption;
        private Label lastLabel;
        private Label hintLabel;
        private Label dirtyLabel;
        private bool startupBusy;
        private bool startupBlocked;
        private bool startupUnknown;

        // 변경 감지: 마지막으로 불러온(저장된) 설정과 현재 편집값 비교
        private AppSettings savedSettings;
        private bool loadingControls;

        // 같은 상태를 반복해서 그리지 않도록 마지막 값을 기억한다.
        private RuntimeState? lastRuntimeState;
        private string lastRuntimeText;

        public MainForm(TrayAppContext context)
        {
            this.context = context;
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Text = StartupConstants.ProductName + " - 설정";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(720, 780);
            this.MinimumSize = new Size(700, 520);
            this.Font = UiTheme.BodyFont();
            this.BackColor = UiTheme.Background;
            this.ForeColor = UiTheme.TextPrimary;
            this.windowIcon = AppIcons.Create(UiTheme.Accent, 32);
            this.Icon = this.windowIcon;
            this.ShowInTaskbar = true;
            this.MaximizeBox = true;
            this.BuildUi();
            this.ResumeLayout(true);
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
            this.ApplyToControls(settings);
        }

        public AppSettings ReadSettingsFromControls()
        {
            AppSettings settings = new AppSettings();
            settings.Mute = ReadBinding(this.muteRow);
            settings.VolumeUp = ReadBinding(this.volumeUpRow);
            settings.VolumeDown = ReadBinding(this.volumeDownRow);
            settings.Step = (int)this.stepInput.Value;
            settings.Enabled = this.enabledInput.Checked;
            return settings;
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
            this.statusPill.SetState(state);
            this.stateLabel.Text = text;
            this.stateLabel.ForeColor = state == RuntimeState.Active
                ? UiTheme.SuccessText
                : (state == RuntimeState.Conflict || state == RuntimeState.Error ? UiTheme.ErrorText : UiTheme.TextPrimary);
        }

        public void SetLastAction(string text, bool error)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string, bool>(this.SetLastAction), new object[] { text, error });
                return;
            }

            this.lastLabel.Text = text;
            this.lastLabel.ForeColor = error ? UiTheme.ErrorText : UiTheme.TextPrimary;
        }

        // The checkbox mirrors the ACTUAL Task Scheduler state read by the background
        // query. An owned task that is enabled but still points at an old location is
        // checked (its actual state) while the label separately asks for a refresh.
        // Unreadable or foreign tasks use an indeterminate, disabled checkbox.
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
            this.startupInput.ThreeState = !known;
            this.startupInput.CheckState = known
                ? (state.CheckboxChecked ? CheckState.Checked : CheckState.Unchecked)
                : CheckState.Indeterminate;
            this.startupStateLabel.Text = state == null ? "상태 확인 중..." : state.MessageKo;
            this.startupStateLabel.ForeColor = state != null && state.IsError
                ? UiTheme.ErrorText
                : (state != null && state.IsEnabled ? UiTheme.SuccessText : UiTheme.TextSecondary);
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
            this.startupStateLabel.ForeColor = error ? UiTheme.ErrorText : UiTheme.TextSecondary;
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
            this.startupInput.ThreeState = true;
            this.startupInput.CheckState = CheckState.Indeterminate;
            this.startupStateLabel.Text = message;
            this.startupStateLabel.ForeColor = UiTheme.ErrorText;
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
            if (this.startupInput == null || this.startupApply == null)
            {
                return;
            }

            bool usable = !this.startupBusy && !this.startupBlocked && !this.startupUnknown;
            this.startupInput.Enabled = usable;
            this.startupApply.Enabled = usable;
            this.startupApply.Text = this.startupBusy ? "적용 중..." : "자동 시작 적용";
        }

        public void ShowSettings()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(this.ShowSettings));
                return;
            }

            this.ShowInTaskbar = true;
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
            this.ShowInTaskbar = false;
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
                this.context.HandleHotkey(m.WParam.ToInt32());
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
            this.scrollHost.Padding = new Padding(20, 14, 20, 8);
            this.scrollHost.BackColor = UiTheme.Background;

            this.root = NewGrid(1, UiTheme.Background);
            this.root.Dock = DockStyle.Top;
            this.root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            this.root.Controls.Add(this.BuildHeader(), 0, 0);
            this.root.Controls.Add(this.BuildNotice(), 0, 1);
            this.root.Controls.Add(this.BuildHotkeyCard(), 0, 2);
            this.root.Controls.Add(this.BuildStartupCard(), 0, 3);
            this.root.Controls.Add(this.BuildStatusCard(), 0, 4);
            this.scrollHost.Controls.Add(this.root);

            // 본문(Fill)과 하단 바(Bottom). 하단 바가 먼저 아래 공간을 차지한다.
            this.Controls.Add(this.scrollHost);
            this.Controls.Add(this.BuildFooter());
            this.scrollHost.Layout += delegate { this.UpdateWrappingWidths(); };
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = NewGrid(3, UiTheme.Background);
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0, 0, 0, 12);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            GlyphTile logo = new GlyphTile(GlyphKind.Speaker, UiTheme.Accent, Color.White, 40);
            logo.Anchor = AnchorStyles.Left;
            logo.Margin = new Padding(0, 0, 14, 0);
            header.Controls.Add(logo, 0, 0);

            TableLayoutPanel titles = NewGrid(1, UiTheme.Background);
            titles.Anchor = AnchorStyles.Left;
            titles.Margin = new Padding(0);
            Label title = NewLabel(StartupConstants.ProductName, UiTheme.TitleFont(), UiTheme.TextPrimary);
            title.Margin = new Padding(0, 0, 0, 1);
            titles.Controls.Add(title, 0, 0);
            titles.Controls.Add(NewLabel("활성 창에 있는 앱의 소리만 단축키로 조절합니다", null, UiTheme.TextSecondary), 0, 1);
            header.Controls.Add(titles, 1, 0);

            this.statusPill = new StatusPill();
            this.statusPill.Font = UiTheme.StrongFont();
            this.statusPill.Anchor = AnchorStyles.Right;
            this.statusPill.Margin = new Padding(12, 0, 0, 0);
            header.Controls.Add(this.statusPill, 2, 0);
            return header;
        }

        private Control BuildNotice()
        {
            this.noticePanel = new RoundedPanel(UiTheme.CautionFill, UiTheme.CautionBorder, 6);
            this.noticePanel.Dock = DockStyle.Fill;
            this.noticePanel.AutoSize = true;
            this.noticePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.noticePanel.Padding = new Padding(12, 9, 12, 9);
            this.noticePanel.Margin = new Padding(0, 0, 0, 12);

            TableLayoutPanel grid = NewGrid(2, UiTheme.CautionFill);
            grid.Dock = DockStyle.Fill;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            this.noticeIcon = new GlyphTile(GlyphKind.Warning, Color.Empty, UiTheme.CautionStrong, 16);
            this.noticeIcon.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            this.noticeIcon.Margin = new Padding(0, 1, 8, 0);
            grid.Controls.Add(this.noticeIcon, 0, 0);

            this.noticeLabel = NewLabel(
                "사용 중에는 선택한 단축키가 시스템 전역에서 예약되어 다른 앱의 원래 키 기능을 대신합니다. "
                + "이 설정 창이 활성 상태이거나 일시정지 중에는 단축키가 해제됩니다. "
                + "대상 앱에 오디오 세션이 없어도 키는 예약됩니다.",
                null,
                UiTheme.CautionText);
            grid.Controls.Add(this.noticeLabel, 1, 0);

            this.noticePanel.Controls.Add(grid);
            return this.noticePanel;
        }

        private Control BuildHotkeyCard()
        {
            RoundedPanel card;
            TableLayoutPanel body = NewCard("단축키", out card);

            TableLayoutPanel grid = NewGrid(5, UiTheme.Card);
            grid.Dock = DockStyle.Fill;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            this.muteRow = this.AddBindingRow(grid, 0, GlyphKind.Mute, "음소거 전환", "켜고 끄기");
            AddDivider(grid, 1, 5, 4);
            this.volumeUpRow = this.AddBindingRow(grid, 2, GlyphKind.VolumeUp, "볼륨 올리기", "단계만큼 높이기");
            AddDivider(grid, 3, 5, 4);
            this.volumeDownRow = this.AddBindingRow(grid, 4, GlyphKind.VolumeDown, "볼륨 내리기", "단계만큼 낮추기");
            AddDivider(grid, 5, 5, 8);

            Control options = this.BuildOptionRow();
            grid.Controls.Add(options, 0, 6);
            grid.SetColumnSpan(options, 5);

            body.Controls.Add(grid, 0, 1);
            return card;
        }

        private BindingRow AddBindingRow(TableLayoutPanel grid, int row, GlyphKind glyph, string labelText, string description)
        {
            GlyphTile tile = new GlyphTile(glyph, UiTheme.AccentSoft, UiTheme.Accent, 32);
            tile.Anchor = AnchorStyles.Left;
            tile.Margin = new Padding(0, 4, 12, 4);
            grid.Controls.Add(tile, 0, row);

            TableLayoutPanel names = NewGrid(1, UiTheme.Card);
            names.Anchor = AnchorStyles.Left;
            names.Margin = new Padding(0, 2, 16, 2);
            Label name = NewLabel(labelText, UiTheme.StrongFont(), UiTheme.TextPrimary);
            name.Margin = new Padding(0);
            names.Controls.Add(name, 0, 0);
            Label detail = NewLabel(description, UiTheme.SmallFont(), UiTheme.TextSecondary);
            detail.Margin = new Padding(0, 1, 0, 0);
            names.Controls.Add(detail, 0, 1);
            grid.Controls.Add(names, 1, row);

            ComboBox keyBox = new ComboBox();
            keyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            keyBox.Width = 170;
            keyBox.Anchor = AnchorStyles.Left;
            keyBox.Margin = new Padding(0, 0, 14, 0);
            keyBox.AccessibleName = labelText + " 키";
            keyBox.MaxDropDownItems = 12;
            foreach (KeyOption option in KeyCatalog.Options)
            {
                keyBox.Items.Add(option);
            }

            keyBox.SelectedIndexChanged += delegate { this.UpdateDirtyState(); };
            grid.Controls.Add(keyBox, 2, row);

            FlowLayoutPanel modifiers = new FlowLayoutPanel();
            modifiers.AutoSize = true;
            modifiers.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modifiers.WrapContents = false;
            modifiers.Anchor = AnchorStyles.Left;
            modifiers.Margin = new Padding(0);
            modifiers.BackColor = UiTheme.Card;
            ModifierToggle ctrl = this.NewModifier("Ctrl", labelText);
            ModifierToggle alt = this.NewModifier("Alt", labelText);
            ModifierToggle shift = this.NewModifier("Shift", labelText);
            modifiers.Controls.Add(ctrl);
            modifiers.Controls.Add(alt);
            modifiers.Controls.Add(shift);
            grid.Controls.Add(modifiers, 3, row);

            return new BindingRow(keyBox, ctrl, alt, shift);
        }

        private ModifierToggle NewModifier(string text, string rowLabel)
        {
            ModifierToggle toggle = new ModifierToggle(text, rowLabel + " " + text + " 수정자");
            toggle.CheckedChanged += delegate { this.UpdateDirtyState(); };
            return toggle;
        }

        private Control BuildOptionRow()
        {
            TableLayoutPanel row = NewGrid(5, UiTheme.Card);
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label stepLabel = NewLabel("볼륨 단계", UiTheme.StrongFont(), UiTheme.TextPrimary);
            stepLabel.Anchor = AnchorStyles.Left;
            stepLabel.Margin = new Padding(0, 0, 10, 0);
            row.Controls.Add(stepLabel, 0, 0);

            this.stepInput = new NumericUpDown();
            this.stepInput.Minimum = 1;
            this.stepInput.Maximum = 20;
            this.stepInput.Value = 5;
            this.stepInput.Width = 64;
            this.stepInput.TextAlign = HorizontalAlignment.Right;
            this.stepInput.Anchor = AnchorStyles.Left;
            this.stepInput.Margin = new Padding(0, 0, 6, 0);
            this.stepInput.AccessibleName = "볼륨 단계 퍼센트 포인트";
            this.stepInput.ValueChanged += delegate { this.UpdateDirtyState(); };
            row.Controls.Add(this.stepInput, 1, 0);

            Label range = NewLabel("%p (1~20)", null, UiTheme.TextSecondary);
            range.Anchor = AnchorStyles.Left;
            range.Margin = new Padding(0, 0, 12, 0);
            row.Controls.Add(range, 2, 0);

            this.enabledInput = new CheckBox();
            this.enabledInput.Text = "단축키 사용 (해제하면 일시정지)";
            this.enabledInput.Font = UiTheme.StrongFont();
            this.enabledInput.AutoSize = true;
            this.enabledInput.Anchor = AnchorStyles.Right;
            this.enabledInput.Margin = new Padding(0, 4, 0, 4);
            this.enabledInput.AccessibleName = "단축키 사용 여부";
            this.enabledInput.CheckedChanged += delegate { this.UpdateDirtyState(); };
            row.Controls.Add(this.enabledInput, 4, 0);
            return row;
        }

        private Control BuildStartupCard()
        {
            RoundedPanel card;
            TableLayoutPanel body = NewCard("로그인 자동 시작", out card);

            TableLayoutPanel row = NewGrid(2, UiTheme.Card);
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            this.startupInput = new CheckBox();
            this.startupInput.Text = "Windows 로그인 시 관리자 권한으로 자동 시작";
            this.startupInput.AutoSize = true;
            this.startupInput.Anchor = AnchorStyles.Left;
            this.startupInput.Margin = new Padding(0, 4, 12, 4);
            this.startupInput.AccessibleName = "Windows 로그인 시 관리자 권한으로 자동 시작";
            row.Controls.Add(this.startupInput, 0, 0);

            this.startupApply = NewButton("자동 시작 적용", false);
            this.startupApply.Anchor = AnchorStyles.Right;
            this.startupApply.Margin = new Padding(0);
            this.startupApply.AccessibleName = "자동 시작 설정 적용";
            this.startupApply.Click += delegate
            {
                this.context.ApplyStartupSetting(this, this.startupInput.Checked);
            };
            row.Controls.Add(this.startupApply, 1, 0);
            body.Controls.Add(row, 0, 1);

            this.startupStateLabel = NewLabel("상태 확인 중...", null, UiTheme.TextSecondary);
            this.startupStateLabel.Margin = new Padding(0, 8, 0, 2);
            this.startupStateLabel.AccessibleName = "자동 시작 상태";
            body.Controls.Add(this.startupStateLabel, 0, 2);

            this.startupNoteLabel = NewLabel(
                (StartupTaskHost.IsElevated()
                    ? "현재 실행 권한: 관리자."
                    : "현재 실행 권한: 일반 사용자.")
                + " 적용할 때 Windows 관리자 승인이 필요할 수 있으며, 다음 로그인부터 자동 시작은 관리자 권한으로 실행됩니다.",
                UiTheme.SmallFont(),
                UiTheme.TextSecondary);
            this.startupNoteLabel.Margin = new Padding(0);
            body.Controls.Add(this.startupNoteLabel, 0, 3);
            return card;
        }

        private Control BuildStatusCard()
        {
            TableLayoutPanel body = NewCard("상태", out this.statusCard);

            this.stateLabel = NewLabel("상태 확인 중...", null, UiTheme.TextPrimary);
            this.stateLabel.Margin = new Padding(0, 0, 0, 6);
            this.stateLabel.AccessibleName = "단축키 상태";
            body.Controls.Add(this.stateLabel, 0, 1);

            TableLayoutPanel last = NewGrid(2, UiTheme.Card);
            last.Dock = DockStyle.Fill;
            last.Margin = new Padding(0, 0, 0, 10);
            last.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            last.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            this.lastCaption = NewLabel("마지막 동작", null, UiTheme.TextSecondary);
            this.lastCaption.Margin = new Padding(0, 0, 10, 0);
            last.Controls.Add(this.lastCaption, 0, 0);
            this.lastLabel = NewLabel("없음", null, UiTheme.TextPrimary);
            this.lastLabel.Margin = new Padding(0);
            this.lastLabel.AccessibleName = "마지막 동작";
            last.Controls.Add(this.lastLabel, 1, 0);
            body.Controls.Add(last, 0, 2);

            this.hintLabel = NewLabel(
                "창을 닫거나 최소화하면 트레이로 숨겨집니다. 완전히 끝내려면 '종료'를 누르거나 트레이 아이콘 메뉴에서 '종료'를 선택하세요. "
                + "설정은 %LOCALAPPDATA%\\GameAudioKeys\\settings.json 에 저장되며, 자동 시작 작업은 Windows 작업 스케줄러에 등록됩니다.",
                UiTheme.SmallFont(),
                UiTheme.TextSecondary);
            this.hintLabel.Margin = new Padding(0);
            body.Controls.Add(this.hintLabel, 0, 3);

            this.statusCard.Margin = new Padding(0);
            return this.statusCard;
        }

        private Control BuildFooter()
        {
            FooterPanel footer = new FooterPanel();
            footer.Dock = DockStyle.Bottom;
            footer.AutoSize = true;
            footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footer.Padding = new Padding(20, 12, 20, 12);

            TableLayoutPanel bar = NewGrid(6, UiTheme.Card);
            bar.Dock = DockStyle.Fill;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Button exit = NewButton("종료", false);
            exit.Margin = new Padding(0);
            exit.AccessibleName = "프로그램 종료";
            exit.Click += delegate { this.context.ExitApp(); };
            bar.Controls.Add(exit, 0, 0);

            this.dirtyLabel = NewLabel("저장하지 않은 변경 사항", UiTheme.StrongFont(), UiTheme.CautionStrong);
            this.dirtyLabel.Anchor = AnchorStyles.Right;
            this.dirtyLabel.Margin = new Padding(8, 0, 4, 0);
            this.dirtyLabel.Visible = false;
            bar.Controls.Add(this.dirtyLabel, 2, 0);

            Button defaults = NewButton("기본값", false);
            defaults.Click += delegate
            {
                this.ApplyToControls(AppSettings.CreateDefaults());
                this.SetLastAction("기본값을 불러왔습니다. '저장 및 적용'을 눌러 적용하세요.", false);
            };
            bar.Controls.Add(defaults, 3, 0);

            Button hide = NewButton("트레이로 숨기기", false);
            hide.Click += delegate { this.HideToTray(); };
            bar.Controls.Add(hide, 4, 0);

            Button save = NewButton("저장 및 적용", true);
            save.Click += delegate { this.context.SaveSettingsFromUi(this); };
            bar.Controls.Add(save, 5, 0);

            footer.Controls.Add(bar);
            return footer;
        }

        #endregion

        #region 보조 생성 함수

        // 둥근 카드와 제목을 만들고, 제목 아래(1행부터)에 내용을 넣을 표를 돌려준다.
        private static TableLayoutPanel NewCard(string title, out RoundedPanel card)
        {
            card = new RoundedPanel(UiTheme.Card, UiTheme.CardBorder, 8);
            card.Dock = DockStyle.Fill;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.Padding = new Padding(16, 12, 16, 14);
            card.Margin = new Padding(0, 0, 0, 12);

            TableLayoutPanel body = NewGrid(1, UiTheme.Card);
            body.Dock = DockStyle.Fill;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Label heading = NewLabel(title, UiTheme.SectionFont(), UiTheme.TextPrimary);
            heading.Margin = new Padding(0, 0, 0, 8);
            body.Controls.Add(heading, 0, 0);
            card.Controls.Add(body);
            return body;
        }

        private static TableLayoutPanel NewGrid(int columns, Color background)
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.ColumnCount = columns;
            grid.BackColor = background;
            grid.Margin = new Padding(0);
            grid.Padding = new Padding(0);
            return grid;
        }

        private static Label NewLabel(string text, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = color;
            label.Anchor = AnchorStyles.Left;
            label.UseMnemonic = false;
            if (font != null)
            {
                label.Font = font;
            }

            return label;
        }

        private static Button NewButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(12, 4, 12, 4);
            button.Margin = new Padding(8, 0, 0, 0);
            button.Anchor = AnchorStyles.Right;
            if (primary)
            {
                UiTheme.StylePrimary(button);
                button.Font = UiTheme.StrongFont();
            }
            else
            {
                UiTheme.StyleSecondary(button);
            }

            return button;
        }

        // 표의 한 행 전체에 1px 구분선을 넣는다.
        private static void AddDivider(TableLayoutPanel grid, int row, int span, int verticalMargin)
        {
            Panel line = new Panel();
            line.Height = 1;
            line.BackColor = UiTheme.Divider;
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            line.Margin = new Padding(0, verticalMargin, 0, verticalMargin);
            grid.Controls.Add(line, 0, row);
            grid.SetColumnSpan(line, span);
        }

        #endregion

        #region 줄바꿈·변경 감지

        // 자동 크기 라벨은 MaximumSize 폭이 있어야 줄바꿈한다. 실제 카드 안쪽 폭에 맞춘다.
        private void UpdateWrappingWidths()
        {
            if (this.scrollHost == null || this.noticeLabel == null || this.statusCard == null || this.lastLabel == null)
            {
                return;
            }

            // 세로 스크롤바가 보이면 ClientSize가 그만큼 줄어든 값이다.
            int content = this.scrollHost.ClientSize.Width - this.scrollHost.Padding.Horizontal;
            if (content <= 0)
            {
                return;
            }

            int cardInner = Math.Max(200, content - this.statusCard.Padding.Horizontal - 2);
            int noticeInner = Math.Max(
                200,
                content - this.noticePanel.Padding.Horizontal - this.noticeIcon.Width - this.noticeIcon.Margin.Horizontal - 2);

            SetWrapWidth(this.noticeLabel, noticeInner);
            SetWrapWidth(this.startupStateLabel, cardInner);
            SetWrapWidth(this.startupNoteLabel, cardInner);
            SetWrapWidth(this.stateLabel, cardInner);
            SetWrapWidth(this.hintLabel, cardInner);
            SetWrapWidth(
                this.lastLabel,
                Math.Max(160, cardInner - this.lastCaption.Width - this.lastCaption.Margin.Horizontal));
        }

        private static void SetWrapWidth(Label label, int width)
        {
            if (label != null && label.MaximumSize.Width != width)
            {
                label.MaximumSize = new Size(width, 0);
            }
        }

        private void ApplyToControls(AppSettings settings)
        {
            this.loadingControls = true;
            try
            {
                SelectBinding(this.muteRow, settings.Mute);
                SelectBinding(this.volumeUpRow, settings.VolumeUp);
                SelectBinding(this.volumeDownRow, settings.VolumeDown);
                int step = settings.Step;
                if (step < 1)
                {
                    step = 1;
                }
                else if (step > 20)
                {
                    step = 20;
                }

                this.stepInput.Value = step;
                this.enabledInput.Checked = settings.Enabled;
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
        }

        private static void SelectBinding(BindingRow row, HotkeyBinding binding)
        {
            if (binding == null)
            {
                row.Key.SelectedIndex = -1;
                row.Ctrl.Checked = false;
                row.Alt.Checked = false;
                row.Shift.Checked = false;
                return;
            }

            KeyOption option = KeyCatalog.Find(binding.Key);
            if (option == null)
            {
                row.Key.SelectedIndex = -1;
            }
            else
            {
                row.Key.SelectedItem = option;
            }

            row.Ctrl.Checked = binding.Ctrl;
            row.Alt.Checked = binding.Alt;
            row.Shift.Checked = binding.Shift;
        }

        private static HotkeyBinding ReadBinding(BindingRow row)
        {
            KeyOption option = row.Key.SelectedItem as KeyOption;
            if (option == null)
            {
                return null;
            }

            return new HotkeyBinding(option.VirtualKey, row.Ctrl.Checked, row.Alt.Checked, row.Shift.Checked);
        }

        #endregion

        private sealed class BindingRow
        {
            public readonly ComboBox Key;
            public readonly CheckBox Ctrl;
            public readonly CheckBox Alt;
            public readonly CheckBox Shift;

            public BindingRow(ComboBox key, CheckBox ctrl, CheckBox alt, CheckBox shift)
            {
                this.Key = key;
                this.Ctrl = ctrl;
                this.Alt = alt;
                this.Shift = shift;
            }
        }
    }
}
