// Game Audio Keys - compact Korean settings window. Native WinForms controls only,
// table/flow layout so resizing and DPI changes stay usable.

using System;
using System.Drawing;
using System.Windows.Forms;
using GameAudioKeys.Core;

namespace GameAudioKeys
{
    internal sealed class MainForm : Form
    {
        private readonly TrayAppContext context;
        private BindingRow muteRow;
        private BindingRow volumeUpRow;
        private BindingRow volumeDownRow;
        private NumericUpDown stepInput;
        private CheckBox enabledInput;
        private CheckBox startupInput;
        private Button startupApply;
        private Label startupStateLabel;
        private Label startupNoteLabel;
        private Label noticeLabel;
        private Label stateLabel;
        private Label lastLabel;
        private bool startupBusy;
        private bool startupBlocked;
        private bool startupUnknown;

        public MainForm(TrayAppContext context)
        {
            this.context = context;
            this.Text = StartupConstants.ProductName + " - 설정";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(680, 660);
            this.MinimumSize = new Size(620, 600);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Icon = SystemIcons.Application;
            this.ShowInTaskbar = true;
            this.MaximizeBox = true;
            this.BuildUi();
        }

        public void EnsureHandle()
        {
            // Creating the handle starts the hotkey lifecycle even when the window stays
            // hidden in the tray on later launches.
            this.Handle.ToInt64();
        }

        public void LoadSettingsIntoControls(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

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

        public void SetRuntimeStatus(string text, bool error)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string, bool>(this.SetRuntimeStatus), new object[] { text, error });
                return;
            }

            this.stateLabel.Text = text;
            this.stateLabel.ForeColor = error ? Color.Firebrick : Color.FromArgb(0, 95, 45);
        }

        public void SetLastAction(string text, bool error)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string, bool>(this.SetLastAction), new object[] { text, error });
                return;
            }

            this.lastLabel.Text = "마지막 동작: " + text;
            this.lastLabel.ForeColor = error ? Color.Firebrick : Color.FromArgb(40, 40, 40);
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
                ? Color.Firebrick
                : (state != null && state.IsEnabled ? Color.FromArgb(0, 95, 45) : Color.FromArgb(80, 80, 80));
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
            this.startupStateLabel.ForeColor = error ? Color.Firebrick : Color.FromArgb(80, 80, 80);
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
            this.startupStateLabel.ForeColor = Color.Firebrick;
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

        private void BuildUi()
        {
            this.SuspendLayout();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(14);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            this.noticeLabel = new Label();
            this.noticeLabel.AutoSize = true;
            this.noticeLabel.Dock = DockStyle.Fill;
            this.noticeLabel.MaximumSize = new Size(640, 0);
            this.noticeLabel.Margin = new Padding(0, 0, 0, 8);
            this.noticeLabel.ForeColor = Color.FromArgb(95, 55, 15);
            this.noticeLabel.Text =
                "사용 중에는 선택한 단축키가 시스템 전역에서 예약되어 다른 앱의 원래 키 기능을 대신합니다. "
                + "이 설정 창이 활성 상태이거나 일시정지 중에는 단축키가 해제됩니다. "
                + "대상 앱에 오디오 세션이 없어도 키는 예약됩니다.";
            root.Controls.Add(this.noticeLabel, 0, 0);

            root.Controls.Add(this.BuildBindingGroup(), 0, 1);
            root.Controls.Add(this.BuildOptionRow(), 0, 2);
            root.Controls.Add(this.BuildButtonRow(), 0, 3);
            root.Controls.Add(this.BuildStartupGroup(), 0, 4);
            root.Controls.Add(this.BuildStatusGroup(), 0, 5);

            Label footer = new Label();
            footer.AutoSize = true;
            footer.Dock = DockStyle.Fill;
            footer.MaximumSize = new Size(640, 0);
            footer.Margin = new Padding(0, 8, 0, 0);
            footer.ForeColor = Color.FromArgb(100, 100, 100);
            footer.Text =
                "창을 닫거나 최소화하면 트레이로 숨겨집니다. 완전히 종료하려면 트레이 아이콘 메뉴에서 '종료'를 선택하세요. "
                + "설정은 %LOCALAPPDATA%\\GameAudioKeys\\settings.json 에 저장되며, 자동 시작 작업은 Windows 작업 스케줄러에 등록됩니다.";
            root.Controls.Add(footer, 0, 6);

            this.Controls.Add(root);
            root.Layout += delegate { this.UpdateWrappingWidths(); };
            this.ResumeLayout(true);
        }

        private GroupBox BuildBindingGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "단축키";
            group.Dock = DockStyle.Fill;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.Padding = new Padding(12, 8, 12, 10);
            group.Margin = new Padding(0, 0, 0, 8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.ColumnCount = 6;
            grid.RowCount = 4;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            AddHeader(grid, 0, 0, "동작");
            AddHeader(grid, 1, 0, "키");
            AddHeader(grid, 2, 0, "Ctrl");
            AddHeader(grid, 3, 0, "Alt");
            AddHeader(grid, 4, 0, "Shift");

            this.muteRow = AddBindingRow(grid, 1, "음소거 전환");
            this.volumeUpRow = AddBindingRow(grid, 2, "볼륨 올리기");
            this.volumeDownRow = AddBindingRow(grid, 3, "볼륨 내리기");

            group.Controls.Add(grid);
            return group;
        }

        private static void AddHeader(TableLayoutPanel grid, int column, int row, string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 0, 12, 4);
            label.ForeColor = Color.FromArgb(70, 70, 70);
            grid.Controls.Add(label, column, row);
        }

        private static BindingRow AddBindingRow(TableLayoutPanel grid, int row, string labelText)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 7, 12, 7);
            grid.Controls.Add(label, 0, row);

            ComboBox keyBox = new ComboBox();
            keyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            keyBox.Width = 190;
            keyBox.Anchor = AnchorStyles.Left;
            keyBox.Margin = new Padding(0, 5, 14, 5);
            keyBox.AccessibleName = labelText + " 키";
            foreach (KeyOption option in KeyCatalog.Options)
            {
                keyBox.Items.Add(option);
            }

            grid.Controls.Add(keyBox, 1, row);

            CheckBox ctrl = NewModifierCheckBox("Ctrl", labelText);
            CheckBox alt = NewModifierCheckBox("Alt", labelText);
            CheckBox shift = NewModifierCheckBox("Shift", labelText);
            grid.Controls.Add(ctrl, 2, row);
            grid.Controls.Add(alt, 3, row);
            grid.Controls.Add(shift, 4, row);

            return new BindingRow(keyBox, ctrl, alt, shift);
        }

        private static CheckBox NewModifierCheckBox(string text, string rowLabel)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.AutoSize = true;
            box.Anchor = AnchorStyles.Left;
            box.Margin = new Padding(0, 5, 14, 5);
            box.AccessibleName = rowLabel + " " + text + " 수정자";
            return box;
        }

        private Control BuildOptionRow()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoSize = true;
            panel.WrapContents = true;
            panel.Margin = new Padding(0, 0, 0, 8);

            Label stepLabel = new Label();
            stepLabel.Text = "볼륨 단계(%p):";
            stepLabel.AutoSize = true;
            stepLabel.Margin = new Padding(0, 6, 6, 6);
            panel.Controls.Add(stepLabel);

            this.stepInput = new NumericUpDown();
            this.stepInput.Minimum = 1;
            this.stepInput.Maximum = 20;
            this.stepInput.Value = 5;
            this.stepInput.Width = 64;
            this.stepInput.TextAlign = HorizontalAlignment.Right;
            this.stepInput.Margin = new Padding(0, 4, 4, 4);
            this.stepInput.AccessibleName = "볼륨 단계 퍼센트 포인트";
            panel.Controls.Add(this.stepInput);

            Label range = new Label();
            range.Text = "(1~20)";
            range.AutoSize = true;
            range.ForeColor = Color.FromArgb(100, 100, 100);
            range.Margin = new Padding(0, 6, 20, 6);
            panel.Controls.Add(range);

            this.enabledInput = new CheckBox();
            this.enabledInput.Text = "단축키 사용 (체크 해제 = 일시정지, 저장 시 반영)";
            this.enabledInput.AutoSize = true;
            this.enabledInput.Margin = new Padding(0, 4, 0, 4);
            this.enabledInput.AccessibleName = "단축키 사용 여부";
            panel.Controls.Add(this.enabledInput);

            return panel;
        }

        private Control BuildButtonRow()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoSize = true;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.WrapContents = true;
            panel.Margin = new Padding(0, 0, 0, 8);

            Button save = new Button();
            save.Text = "저장 및 적용";
            save.AutoSize = true;
            save.Padding = new Padding(10, 3, 10, 3);
            save.Margin = new Padding(8, 0, 0, 0);
            save.Click += delegate { this.context.SaveSettingsFromUi(this); };
            panel.Controls.Add(save);

            Button defaults = new Button();
            defaults.Text = "기본값";
            defaults.AutoSize = true;
            defaults.Padding = new Padding(10, 3, 10, 3);
            defaults.Margin = new Padding(8, 0, 0, 0);
            defaults.Click += delegate
            {
                this.LoadSettingsIntoControls(AppSettings.CreateDefaults());
                this.SetLastAction("기본값을 불러왔습니다. '저장 및 적용'을 눌러 적용하세요.", false);
            };
            panel.Controls.Add(defaults);

            Button hide = new Button();
            hide.Text = "트레이로 숨기기";
            hide.AutoSize = true;
            hide.Padding = new Padding(10, 3, 10, 3);
            hide.Margin = new Padding(8, 0, 0, 0);
            hide.Click += delegate { this.HideToTray(); };
            panel.Controls.Add(hide);

            Button exit = new Button();
            exit.Text = "종료";
            exit.AutoSize = true;
            exit.Padding = new Padding(10, 3, 10, 3);
            exit.Margin = new Padding(8, 0, 0, 0);
            exit.AccessibleName = "프로그램 종료";
            exit.Click += delegate { this.context.ExitApp(); };
            panel.Controls.Add(exit);

            return panel;
        }

        private Control BuildStartupGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "로그인 자동 시작";
            group.Dock = DockStyle.Fill;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.Padding = new Padding(12, 8, 12, 10);
            group.Margin = new Padding(0, 0, 0, 8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.ColumnCount = 2;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            this.startupInput = new CheckBox();
            this.startupInput.Text = "Windows 로그인 시 관리자 권한으로 자동 시작";
            this.startupInput.AutoSize = true;
            this.startupInput.Anchor = AnchorStyles.Left;
            this.startupInput.Margin = new Padding(0, 4, 12, 4);
            this.startupInput.AccessibleName = "Windows 로그인 시 관리자 권한으로 자동 시작";
            grid.Controls.Add(this.startupInput, 0, 0);

            this.startupApply = new Button();
            this.startupApply.Text = "자동 시작 적용";
            this.startupApply.AutoSize = true;
            this.startupApply.Padding = new Padding(10, 3, 10, 3);
            this.startupApply.Margin = new Padding(0, 1, 0, 1);
            this.startupApply.AccessibleName = "자동 시작 설정 적용";
            this.startupApply.Click += delegate
            {
                this.context.ApplyStartupSetting(this, this.startupInput.Checked);
            };
            grid.Controls.Add(this.startupApply, 1, 0);

            this.startupStateLabel = new Label();
            this.startupStateLabel.AutoSize = true;
            this.startupStateLabel.Dock = DockStyle.Fill;
            this.startupStateLabel.MaximumSize = new Size(600, 0);
            this.startupStateLabel.Margin = new Padding(0, 6, 0, 4);
            this.startupStateLabel.ForeColor = Color.FromArgb(80, 80, 80);
            this.startupStateLabel.Text = "상태 확인 중...";
            this.startupStateLabel.AccessibleName = "자동 시작 상태";
            grid.Controls.Add(this.startupStateLabel, 0, 1);
            grid.SetColumnSpan(this.startupStateLabel, 2);

            this.startupNoteLabel = new Label();
            this.startupNoteLabel.AutoSize = true;
            this.startupNoteLabel.Dock = DockStyle.Fill;
            this.startupNoteLabel.MaximumSize = new Size(600, 0);
            this.startupNoteLabel.Margin = new Padding(0, 0, 0, 0);
            this.startupNoteLabel.ForeColor = Color.FromArgb(100, 100, 100);
            this.startupNoteLabel.Text =
                (StartupTaskHost.IsElevated()
                    ? "현재 실행 권한: 관리자."
                    : "현재 실행 권한: 일반 사용자.")
                + " 적용할 때 Windows 관리자 승인이 필요할 수 있으며, 다음 로그인부터 자동 시작은 관리자 권한으로 실행됩니다.";
            grid.Controls.Add(this.startupNoteLabel, 0, 2);
            grid.SetColumnSpan(this.startupNoteLabel, 2);

            group.Controls.Add(grid);
            return group;
        }

        private Control BuildStatusGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "상태";
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(12, 8, 12, 10);
            group.Margin = new Padding(0, 0, 0, 0);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            this.stateLabel = new Label();
            this.stateLabel.AutoSize = true;
            this.stateLabel.Dock = DockStyle.Fill;
            this.stateLabel.MaximumSize = new Size(620, 0);
            this.stateLabel.Margin = new Padding(0, 0, 0, 6);
            this.stateLabel.Text = "상태 확인 중...";
            grid.Controls.Add(this.stateLabel, 0, 0);

            this.lastLabel = new Label();
            this.lastLabel.AutoSize = true;
            this.lastLabel.Dock = DockStyle.Fill;
            this.lastLabel.MaximumSize = new Size(620, 0);
            this.lastLabel.Margin = new Padding(0, 0, 0, 0);
            this.lastLabel.Text = "마지막 동작: 없음";
            grid.Controls.Add(this.lastLabel, 0, 1);

            group.Controls.Add(grid);
            return group;
        }

        private void UpdateWrappingWidths()
        {
            if (this.noticeLabel == null || this.stateLabel == null || this.lastLabel == null)
            {
                return;
            }

            int width = Math.Max(260, this.ClientSize.Width - 56);
            this.noticeLabel.MaximumSize = new Size(width, 0);
            int statusWidth = Math.Max(220, width - 40);
            this.stateLabel.MaximumSize = new Size(statusWidth, 0);
            this.lastLabel.MaximumSize = new Size(statusWidth, 0);
            if (this.startupStateLabel != null)
            {
                this.startupStateLabel.MaximumSize = new Size(statusWidth, 0);
            }

            if (this.startupNoteLabel != null)
            {
                this.startupNoteLabel.MaximumSize = new Size(statusWidth, 0);
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
