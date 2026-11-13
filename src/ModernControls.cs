// Windows Audio Keys - 직접 그린 입력 컨트롤(스위치, 키 녹화기, 스테퍼, 둥근 버튼).
// 기본 컨트롤은 다크 테마를 그리지 못하므로 모양만 직접 그리고, 키보드 조작과
// 접근성 정보(역할·이름·상태·기본 동작)는 각 컨트롤이 직접 제공한다.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameAudioKeys
{
    #region 공통 기반

    // 포커스를 받는 직접 그린 컨트롤의 공통 설정(더블 버퍼, 포커스·마우스 오버 시 다시 그리기).
    internal abstract class ThemedControl : Control
    {
        private bool hovered;
        private Font boldFont;

        protected ThemedControl()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.Selectable
                | ControlStyles.StandardClick,
                true);

            // 빠르게 두 번 누르면 두 번 모두 Click으로 처리한다(두 번째가 DoubleClick으로 바뀌지 않게).
            this.SetStyle(ControlStyles.StandardDoubleClick, false);
            this.TabStop = true;
            this.Cursor = Cursors.Hand;
        }

        protected bool Hovered
        {
            get { return this.hovered; }
        }

        // 키캡·값 표시용 굵은 글꼴. 그릴 때마다 만들지 않고 글꼴이 바뀔 때만 다시 만든다.
        protected Font BoldFont
        {
            get
            {
                if (this.boldFont == null)
                {
                    this.boldFont = new Font(this.Font, FontStyle.Bold);
                }

                return this.boldFont;
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            this.ReleaseBoldFont();
            base.OnFontChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.ReleaseBoldFont();
            }

            base.Dispose(disposing);
        }

        private void ReleaseBoldFont()
        {
            if (this.boldFont != null)
            {
                this.boldFont.Dispose();
                this.boldFont = null;
            }
        }

        // 키보드로 포커스가 왔을 때만 포커스 링을 그린다(Windows 기본 규칙).
        protected bool ShowFocusRing
        {
            get { return this.Focused && this.ShowFocusCues; }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            this.hovered = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            this.hovered = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            this.Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            this.Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            this.Invalidate();
            base.OnEnabledChanged(e);
        }

        protected void PaintSurface(Graphics g)
        {
            using (SolidBrush surface = new SolidBrush(UiTheme.SurfaceOf(this)))
            {
                g.FillRectangle(surface, this.ClientRectangle);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        protected float S(float logicalPixels)
        {
            return UiTheme.Scale(this, logicalPixels);
        }
    }

    #endregion

    #region 토글 스위치

    // Windows 11식 켬/끔 스위치. 코드로 Checked를 바꿀 때는 이벤트가 없고, 사용자가
    // 클릭·Space·Enter·접근성 기본 동작으로 바꿀 때만 UserToggled가 발생한다.
    internal sealed class ToggleSwitch : ThemedControl
    {
        private bool isChecked;
        private bool indeterminate;

        public ToggleSwitch(string accessibleName)
        {
            this.AccessibleName = accessibleName;
            this.Size = new Size(112, 30);
        }

        public event EventHandler UserToggled;

        public bool Checked
        {
            get { return this.isChecked; }
            set
            {
                if (this.isChecked == value)
                {
                    return;
                }

                this.isChecked = value;
                this.Invalidate();
                this.AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        // 실제 상태를 아직 모를 때(조회 중·조회 실패). 이 동안은 사용자가 바꿀 수 없다.
        public bool Indeterminate
        {
            get { return this.indeterminate; }
            set
            {
                if (this.indeterminate == value)
                {
                    return;
                }

                this.indeterminate = value;
                this.Invalidate();
                this.AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        internal void ToggleByUser()
        {
            if (!this.Enabled || this.indeterminate)
            {
                return;
            }

            this.isChecked = !this.isChecked;
            this.Invalidate();
            this.AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            EventHandler handler = this.UserToggled;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            this.Focus();
            this.ToggleByUser();
            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        // 체크 상자와 같은 규칙: 키를 뗄 때 한 번만 전환한다. 누르고 있는 동안의 반복 입력으로
        // 일시정지/다시 시작이나 자동 시작 적용이 여러 번 실행되지 않게 한다.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                this.ToggleByUser();
                e.Handled = true;
            }

            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = e.Graphics;
            this.PaintSurface(g);

            float w = this.S(40);
            float h = this.S(20);
            RectangleF track = new RectangleF(this.Width - w - this.S(3), (this.Height - h) / 2f, w, h);
            bool usable = this.Enabled && !this.indeterminate;

            using (GraphicsPath path = UiTheme.RoundedRect(track, h / 2f))
            {
                if (this.isChecked && !this.indeterminate)
                {
                    Color fill = !usable ? p.Disabled : (this.Hovered ? p.AccentHover : p.Accent);
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }

                    float knob = this.S(12);
                    using (SolidBrush brush = new SolidBrush(p.OnAccent))
                    {
                        g.FillEllipse(brush, track.Right - knob - this.S(4), track.Y + (h - knob) / 2f, knob, knob);
                    }
                }
                else
                {
                    Color line = usable ? p.SwitchOff : p.Disabled;
                    if (this.Hovered && usable)
                    {
                        using (SolidBrush brush = new SolidBrush(p.FieldHover))
                        {
                            g.FillPath(brush, path);
                        }
                    }

                    using (Pen pen = new Pen(line, 1f))
                    {
                        g.DrawPath(pen, path);
                    }

                    float knob = this.S(10);
                    float x = this.indeterminate ? track.X + (w - knob) / 2f : track.X + this.S(5);
                    using (SolidBrush brush = new SolidBrush(line))
                    {
                        g.FillEllipse(brush, x, track.Y + (h - knob) / 2f, knob, knob);
                    }
                }
            }

            string text = this.indeterminate ? "확인 중" : (this.isChecked ? "켬" : "끔");
            Rectangle textBounds = new Rectangle(0, 0, (int)(track.X - this.S(10)), this.Height);
            TextRenderer.DrawText(
                g,
                text,
                this.Font,
                textBounds,
                this.Enabled ? p.Text : p.Disabled,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (this.ShowFocusRing)
            {
                UiTheme.DrawFocusRing(g, RectangleF.Inflate(track, this.S(2.5f), this.S(2.5f)), h / 2f + this.S(2.5f));
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new SwitchAccessible(this);
        }

        // 화면 낭독기·UI Automation에 "체크 단추"와 체크 상태를 알린다(Toggle 패턴으로 노출).
        private sealed class SwitchAccessible : ControlAccessibleObject
        {
            private readonly ToggleSwitch owner;

            public SwitchAccessible(ToggleSwitch owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.CheckButton; }
            }

            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = base.State;
                    if (this.owner.indeterminate)
                    {
                        state |= AccessibleStates.Mixed;
                    }
                    else if (this.owner.isChecked)
                    {
                        state |= AccessibleStates.Checked;
                    }

                    return state;
                }
            }

            public override string DefaultAction
            {
                get { return "전환"; }
            }

            public override void DoDefaultAction()
            {
                this.owner.ToggleByUser();
            }
        }
    }

    #endregion

    #region 키 녹화기

    // 현재 조합을 키캡으로 보여 주고, 누르면 다음 키 입력을 그대로 녹화한다.
    // 녹화 중에는 모든 키(Tab·Alt 조합 포함)를 이 컨트롤이 받으며 Esc로 취소한다.
    internal sealed class KeyRecorder : ThemedControl
    {
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyUp = 0x0105;
        private const int WmSysChar = 0x0106;

        private readonly string rowName;
        private HotkeyBinding binding;
        private bool recording;
        private string message;

        public KeyRecorder(string rowName)
        {
            this.rowName = rowName;
            this.Size = new Size(300, 36);
        }

        public event EventHandler BindingChanged;

        public HotkeyBinding Binding
        {
            get { return this.binding == null ? null : this.binding.Clone(); }
            set
            {
                this.binding = value == null ? null : value.Clone();
                this.recording = false;
                this.message = null;
                this.Invalidate();
                this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            }
        }

        public bool IsRecording
        {
            get { return this.recording; }
        }

        public void StartRecording()
        {
            if (!this.Enabled)
            {
                return;
            }

            this.Focus();
            this.recording = true;
            this.message = null;
            this.Invalidate();
            this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        public void CancelRecording()
        {
            if (!this.recording)
            {
                return;
            }

            this.recording = false;
            this.message = null;
            this.Invalidate();
            this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        protected override void OnClick(EventArgs e)
        {
            if (!this.recording)
            {
                this.StartRecording();
            }

            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return this.recording || keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        // 키를 뗄 때 녹화를 시작한다. 누를 때 시작하면 같은 키의 반복 입력이 곧바로
        // "지원하지 않는 키"로 녹화되어 버린다.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!this.recording && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                this.StartRecording();
                e.Handled = true;
            }

            base.OnKeyUp(e);
        }

        // 녹화 중에는 대화상자 키(Tab, Alt+문자, Alt+F4 등)보다 먼저 가로챈다.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (this.recording)
            {
                this.HandleRecordedKey(keyData);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            // 녹화 중 Alt를 떼면 창 메뉴 모드로 들어가거나 경고음이 나지 않도록 키 떼기와
            // 시스템 문자 메시지를 삼킨다. 수정자 표시만 갱신한다.
            if (this.recording && (m.Msg == WmKeyUp || m.Msg == WmSysKeyUp || m.Msg == WmSysChar))
            {
                this.Invalidate();
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            this.CancelRecording();
            base.OnLostFocus(e);
        }

        private void HandleRecordedKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.CancelRecording();
                return;
            }

            KeyCaptureResult result = KeyCapture.Evaluate(keyData);
            if (result.Kind == KeyCaptureKind.Pending)
            {
                this.message = null;
                this.Invalidate();
                return;
            }

            if (result.Kind == KeyCaptureKind.Rejected)
            {
                this.message = result.MessageKo;
                this.Invalidate();
                this.AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
                return;
            }

            this.binding = result.Binding;
            this.recording = false;
            this.message = null;
            this.Invalidate();
            this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            EventHandler handler = this.BindingChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = e.Graphics;
            this.PaintSurface(g);
            RectangleF bounds = new RectangleF(1f, 1f, this.Width - 3f, this.Height - 3f);
            float radius = this.S(6);

            if (this.recording)
            {
                this.PaintRecording(g, p, bounds, radius);
            }
            else
            {
                this.PaintIdle(g, p, bounds, radius);
            }

            if (this.ShowFocusRing)
            {
                UiTheme.DrawFocusRing(g, bounds, radius);
            }
        }

        private void PaintRecording(Graphics g, ThemePalette p, RectangleF bounds, float radius)
        {
            bool error = this.message != null;
            Color color = error ? p.Error : p.Accent;
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, radius))
            using (Pen pen = new Pen(color, 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawPath(pen, path);
            }

            float dot = this.S(8);
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, bounds.X + this.S(12), (this.Height - dot) / 2f, dot, dot);
            }

            string text = this.message
                ?? KeyCapture.DescribePending(Control.ModifierKeys)
                ?? "새 키 조합을 누르세요 · Esc 취소";
            int left = (int)(bounds.X + this.S(28));
            TextRenderer.DrawText(
                g,
                text,
                this.Font,
                new Rectangle(left, 0, (int)bounds.Right - left - (int)this.S(8), this.Height),
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void PaintIdle(Graphics g, ThemePalette p, RectangleF bounds, float radius)
        {
            // 오른쪽 "변경" 버튼 모양
            const string changeText = "변경";
            Size changeSize = TextRenderer.MeasureText(changeText, this.Font);
            float changeWidth = changeSize.Width + this.S(22);
            RectangleF change = new RectangleF(bounds.Right - changeWidth, bounds.Y + this.S(1), changeWidth, bounds.Height - this.S(2));
            using (GraphicsPath path = UiTheme.RoundedRect(change, radius))
            using (SolidBrush brush = new SolidBrush(!this.Enabled ? p.Field : (this.Hovered ? p.FieldHover : p.Field)))
            using (Pen pen = new Pen(p.FieldBorder, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                g,
                changeText,
                this.Font,
                Rectangle.Round(change),
                this.Enabled ? p.Text : p.Disabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // 키캡: 오른쪽에서 왼쪽으로 배치
            string[] parts = Parts(this.binding);
            float right = change.X - this.S(10);
            float capHeight = this.S(28);
            float capTop = (this.Height - capHeight) / 2f - this.S(1);
            Font capFont = this.BoldFont;
            {
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    Size textSize = TextRenderer.MeasureText(parts[i], capFont);
                    float capWidth = Math.Max(capHeight, textSize.Width + this.S(16));
                    RectangleF cap = new RectangleF(right - capWidth, capTop, capWidth, capHeight);
                    if (cap.X < bounds.X)
                    {
                        break;
                    }

                    RectangleF shadow = cap;
                    shadow.Offset(0, this.S(2));
                    using (GraphicsPath shadowPath = UiTheme.RoundedRect(shadow, this.S(5)))
                    using (SolidBrush brush = new SolidBrush(p.KeycapShadow))
                    {
                        g.FillPath(brush, shadowPath);
                    }

                    using (GraphicsPath capPath = UiTheme.RoundedRect(cap, this.S(5)))
                    using (SolidBrush brush = new SolidBrush(p.Keycap))
                    using (Pen pen = new Pen(p.KeycapBorder, 1f))
                    {
                        g.FillPath(brush, capPath);
                        g.DrawPath(pen, capPath);
                    }

                    TextRenderer.DrawText(
                        g,
                        parts[i],
                        capFont,
                        Rectangle.Round(cap),
                        this.Enabled ? p.Text : p.Disabled,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    right = cap.X - this.S(6);
                }
            }
        }

        private static string[] Parts(HotkeyBinding binding)
        {
            if (binding == null)
            {
                return new[] { "없음" };
            }

            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            if (binding.Ctrl)
            {
                parts.Add("Ctrl");
            }

            if (binding.Alt)
            {
                parts.Add("Alt");
            }

            if (binding.Shift)
            {
                parts.Add("Shift");
            }

            string name = KeyCatalog.GetName(binding.Key);
            parts.Add(name ?? "?");
            return parts.ToArray();
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new RecorderAccessible(this);
        }

        // 이름에 동작과 현재 조합(또는 녹화 중)을 함께 담아 읽어 준다.
        private sealed class RecorderAccessible : ControlAccessibleObject
        {
            private readonly KeyRecorder owner;

            public RecorderAccessible(KeyRecorder owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.PushButton; }
            }

            public override string Name
            {
                get
                {
                    return this.owner.rowName + " 단축키, "
                        + (this.owner.recording
                            ? "녹화 중. 새 키 조합을 누르거나 Esc로 취소"
                            : KeyCatalog.Format(this.owner.binding));
                }
            }

            public override string Description
            {
                get { return this.owner.message; }
            }

            public override string DefaultAction
            {
                get { return "변경"; }
            }

            public override void DoDefaultAction()
            {
                this.owner.StartRecording();
            }
        }
    }

    #endregion

    #region 스테퍼

    // "− 5%p +" 형태의 정수 입력(1~20). 방향키·+/−·Home/End·마우스 휠·클릭으로 바꾼다.
    internal sealed class Stepper : ThemedControl
    {
        private readonly int minimum;
        private readonly int maximum;
        private readonly string suffix;
        private int current;
        private int hoverPart = -1;

        public Stepper(int minimum, int maximum, string suffix, string accessibleName)
        {
            this.minimum = minimum;
            this.maximum = maximum;
            this.suffix = suffix;
            this.current = minimum;
            this.AccessibleName = accessibleName;
            this.Size = new Size(140, 34);
        }

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return this.current; }
            set
            {
                int clamped = Math.Max(this.minimum, Math.Min(this.maximum, value));
                if (clamped == this.current)
                {
                    return;
                }

                this.current = clamped;
                this.Invalidate();
                this.AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                EventHandler handler = this.ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private float PartWidth
        {
            get { return this.S(36); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Right:
                case Keys.Add:
                case Keys.Oemplus:
                    this.Value = this.current + 1;
                    e.Handled = true;
                    break;
                case Keys.Down:
                case Keys.Left:
                case Keys.Subtract:
                case Keys.OemMinus:
                    this.Value = this.current - 1;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    this.Value = this.minimum;
                    e.Handled = true;
                    break;
                case Keys.End:
                    this.Value = this.maximum;
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int part = e.X < this.PartWidth ? 0 : (e.X > this.Width - this.PartWidth ? 1 : -1);
            if (part != this.hoverPart)
            {
                this.hoverPart = part;
                this.Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            this.hoverPart = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            this.Focus();
            if (e.Button == MouseButtons.Left)
            {
                if (e.X < this.PartWidth)
                {
                    this.Value = this.current - 1;
                }
                else if (e.X > this.Width - this.PartWidth)
                {
                    this.Value = this.current + 1;
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            this.Value = this.current + (e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = e.Graphics;
            this.PaintSurface(g);
            RectangleF bounds = new RectangleF(1f, 1f, this.Width - 3f, this.Height - 3f);
            float radius = this.S(6);
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, radius))
            using (SolidBrush brush = new SolidBrush(p.Field))
            using (Pen pen = new Pen(p.FieldBorder, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            float part = this.PartWidth;
            RectangleF minus = new RectangleF(bounds.X, bounds.Y, part, bounds.Height);
            RectangleF plus = new RectangleF(bounds.Right - part, bounds.Y, part, bounds.Height);
            this.PaintPart(g, p, minus, false, this.current > this.minimum, this.hoverPart == 0);
            this.PaintPart(g, p, plus, true, this.current < this.maximum, this.hoverPart == 1);

            Font valueFont = this.BoldFont;
            {
                TextRenderer.DrawText(
                    g,
                    this.current.ToString(System.Globalization.CultureInfo.InvariantCulture) + this.suffix,
                    valueFont,
                    Rectangle.Round(new RectangleF(minus.Right, 0, plus.X - minus.Right, this.Height)),
                    this.Enabled ? p.Text : p.Disabled,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            if (this.ShowFocusRing)
            {
                UiTheme.DrawFocusRing(g, bounds, radius);
            }
        }

        private void PaintPart(Graphics g, ThemePalette p, RectangleF area, bool isPlus, bool available, bool hover)
        {
            if (hover && available && this.Enabled)
            {
                RectangleF inner = RectangleF.Inflate(area, -this.S(3), -this.S(3));
                using (GraphicsPath path = UiTheme.RoundedRect(inner, this.S(4)))
                using (SolidBrush brush = new SolidBrush(p.FieldHover))
                {
                    g.FillPath(brush, path);
                }
            }

            float half = this.S(5);
            float cx = area.X + area.Width / 2f;
            float cy = area.Y + area.Height / 2f;
            using (Pen pen = new Pen(available && this.Enabled ? p.Text : p.Disabled, Math.Max(1f, this.S(1.5f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - half, cy, cx + half, cy);
                if (isPlus)
                {
                    g.DrawLine(pen, cx, cy - half, cx, cy + half);
                }
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new StepperAccessible(this);
        }

        private sealed class StepperAccessible : ControlAccessibleObject
        {
            private readonly Stepper owner;

            public StepperAccessible(Stepper owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.SpinButton; }
            }

            public override string Value
            {
                get
                {
                    return this.owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + this.owner.suffix;
                }

                set
                {
                    int parsed;
                    string digits = value == null ? null : value.Replace(this.owner.suffix, string.Empty).Trim();
                    if (int.TryParse(digits, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                    {
                        this.owner.Value = parsed;
                    }
                }
            }
        }
    }

    #endregion

    #region 둥근 버튼

    internal enum ModernButtonKind
    {
        Primary,
        Secondary,
        Text
    }

    // 기본 Button을 상속해 클릭·키보드·접근성은 그대로 두고 모양만 다시 그린다.
    internal sealed class ModernButton : Button
    {
        private readonly ModernButtonKind kind;
        private bool hovered;
        private bool pressed;

        public ModernButton(string text, ModernButtonKind kind)
        {
            this.kind = kind;
            this.Text = text;
            this.UseMnemonic = false;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = kind == ModernButtonKind.Text ? new Padding(8, 4, 8, 4) : new Padding(14, 5, 14, 5);
            this.Margin = new Padding(8, 0, 0, 0);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            this.hovered = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            this.hovered = false;
            this.pressed = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            this.pressed = mevent.Button == MouseButtons.Left;
            this.Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            this.pressed = false;
            this.Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = pevent.Graphics;
            using (SolidBrush surface = new SolidBrush(UiTheme.SurfaceOf(this)))
            {
                g.FillRectangle(surface, this.ClientRectangle);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = UiTheme.Scale(this, 5);
            RectangleF bounds = new RectangleF(1f, 1f, this.Width - 3f, this.Height - 3f);

            Color fill;
            Color border;
            Color text;
            if (!this.Enabled)
            {
                fill = this.kind == ModernButtonKind.Text ? Color.Empty : p.Field;
                border = this.kind == ModernButtonKind.Secondary ? p.FieldBorder : Color.Empty;
                text = p.Disabled;
            }
            else if (this.kind == ModernButtonKind.Primary)
            {
                fill = this.pressed ? p.AccentPressed : (this.hovered ? p.AccentHover : p.Accent);
                border = Color.Empty;
                text = p.OnAccent;
            }
            else if (this.kind == ModernButtonKind.Secondary)
            {
                fill = this.pressed ? p.FieldPressed : (this.hovered ? p.FieldHover : p.Field);
                border = p.FieldBorder;
                text = p.Text;
            }
            else
            {
                fill = this.pressed ? p.FieldPressed : (this.hovered ? p.FieldHover : Color.Empty);
                border = Color.Empty;
                text = p.Accent;
            }

            using (GraphicsPath path = UiTheme.RoundedRect(bounds, radius))
            {
                if (fill != Color.Empty)
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }
                }

                if (border != Color.Empty)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(
                g,
                this.Text,
                this.Font,
                this.ClientRectangle,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (this.Focused && this.ShowFocusCues)
            {
                UiTheme.DrawFocusRing(g, bounds, radius);
            }
        }
    }

    #endregion
}
