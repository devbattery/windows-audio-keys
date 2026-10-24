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
        private Label noticeLabel;
        private Label stateLabel;
        private Label lastLabel;

        public MainForm(TrayAppContext context)
        {
            this.context = context;
            this.Text = "Game Audio Keys - 설정";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(680, 520);
            this.MinimumSize = new Size(620, 470);
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
            root.RowCount = 6;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
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
            root.Controls.Add(this.BuildStatusGroup(), 0, 4);

            Label footer = new Label();
            footer.AutoSize = true;
            footer.Dock = DockStyle.Fill;
            footer.MaximumSize = new Size(640, 0);
            footer.Margin = new Padding(0, 8, 0, 0);
            footer.ForeColor = Color.FromArgb(100, 100, 100);
            footer.Text =
                "창을 닫거나 최소화하면 트레이로 숨겨집니다. 완전히 종료하려면 트레이 아이콘 메뉴에서 '종료'를 선택하세요. "
                + "설정은 %LOCALAPPDATA%\\GameAudioKeys\\settings.json 에 저장됩니다.";
            root.Controls.Add(footer, 0, 5);

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
