// Windows Audio Keys - 설정 창의 색·글꼴 토큰과 작은 보조 컨트롤.
// 기본 WinForms 컨트롤 위에 둥근 배경·테두리만 직접 그린다. 입력 컨트롤(콤보, 체크,
// 숫자, 버튼)은 기본 컨트롤 그대로라 키보드 탐색과 접근성 정보가 유지된다.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameAudioKeys
{
    // 상단 배지·트레이 아이콘·상태 문구가 함께 쓰는 실행 상태.
    internal enum RuntimeState
    {
        Waiting,
        Active,
        Suspended,
        Paused,
        Conflict,
        Error
    }

    internal enum GlyphKind
    {
        Speaker,
        Mute,
        VolumeUp,
        VolumeDown,
        Warning
    }

    #region 색·글꼴 토큰

    internal static class UiTheme
    {
        // Windows 11 Fluent 계열 중립 배경과 카드
        public static readonly Color Background = Color.FromArgb(243, 243, 243);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(229, 229, 229);
        public static readonly Color Divider = Color.FromArgb(235, 235, 235);

        // 글자색: 흰 배경과 #F3F3F3 배경 모두에서 대비 4.5:1 이상
        public static readonly Color TextPrimary = Color.FromArgb(27, 27, 27);
        public static readonly Color TextSecondary = Color.FromArgb(95, 95, 95);

        // 강조색(Windows 11 기본 파랑)
        public static readonly Color Accent = Color.FromArgb(0, 95, 184);
        public static readonly Color AccentHover = Color.FromArgb(0, 84, 163);
        public static readonly Color AccentPressed = Color.FromArgb(0, 74, 143);
        public static readonly Color AccentSoft = Color.FromArgb(239, 244, 249);

        // 보조 버튼·입력 테두리
        public static readonly Color ControlBorder = Color.FromArgb(209, 209, 209);
        public static readonly Color ControlFill = Color.FromArgb(251, 251, 251);
        public static readonly Color ControlHover = Color.FromArgb(243, 243, 243);
        public static readonly Color ControlPressed = Color.FromArgb(235, 235, 235);

        // 상태 색
        public static readonly Color SuccessText = Color.FromArgb(14, 107, 14);
        public static readonly Color SuccessFill = Color.FromArgb(223, 246, 221);
        public static readonly Color SuccessDot = Color.FromArgb(15, 123, 15);
        public static readonly Color CautionText = Color.FromArgb(61, 43, 0);
        public static readonly Color CautionFill = Color.FromArgb(255, 244, 206);
        public static readonly Color CautionBorder = Color.FromArgb(241, 223, 166);
        public static readonly Color CautionStrong = Color.FromArgb(157, 93, 0);
        public static readonly Color ErrorText = Color.FromArgb(196, 43, 28);
        public static readonly Color ErrorFill = Color.FromArgb(253, 231, 233);
        public static readonly Color NeutralText = Color.FromArgb(68, 68, 68);
        public static readonly Color NeutralFill = Color.FromArgb(232, 232, 232);
        public static readonly Color NeutralDot = Color.FromArgb(138, 138, 138);

        private const string FontFamilyName = "Malgun Gothic";

        public static Font BodyFont()
        {
            return new Font(FontFamilyName, 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font StrongFont()
        {
            return new Font(FontFamilyName, 9F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font SmallFont()
        {
            return new Font(FontFamilyName, 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font SectionFont()
        {
            return new Font(FontFamilyName, 10F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font TitleFont()
        {
            return new Font(FontFamilyName, 14F, FontStyle.Bold, GraphicsUnit.Point);
        }

        // 상태별 배지 색과 짧은 문구
        public static void Describe(RuntimeState state, out string label, out Color text, out Color fill, out Color dot)
        {
            switch (state)
            {
                case RuntimeState.Active:
                    label = "사용 중";
                    text = SuccessText;
                    fill = SuccessFill;
                    dot = SuccessDot;
                    break;
                case RuntimeState.Suspended:
                    label = "설정 창 활성";
                    text = Accent;
                    fill = AccentSoft;
                    dot = Accent;
                    break;
                case RuntimeState.Paused:
                    label = "일시정지";
                    text = NeutralText;
                    fill = NeutralFill;
                    dot = NeutralDot;
                    break;
                case RuntimeState.Conflict:
                    label = "단축키 충돌";
                    text = ErrorText;
                    fill = ErrorFill;
                    dot = ErrorText;
                    break;
                case RuntimeState.Error:
                    label = "오류";
                    text = ErrorText;
                    fill = ErrorFill;
                    dot = ErrorText;
                    break;
                default:
                    label = "대기 중";
                    text = NeutralText;
                    fill = NeutralFill;
                    dot = NeutralDot;
                    break;
            }
        }

        // 보조 버튼: 옅은 배경 + 회색 테두리
        public static void StyleSecondary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ControlFill;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = ControlBorder;
            button.FlatAppearance.MouseOverBackColor = ControlHover;
            button.FlatAppearance.MouseDownBackColor = ControlPressed;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        // 주 버튼: 강조색 배경 + 흰 글자
        public static void StylePrimary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Accent;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentPressed;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        // 현재 글꼴 높이 기준으로 DPI에 맞춘 픽셀 값(96 DPI에서 글꼴 높이 약 16px 기준)
        public static float Scale(Control control, float logicalPixels)
        {
            return logicalPixels * control.Font.Height / 16f;
        }
    }

    #endregion

    #region 벡터 아이콘

    // 24x24 논리 격자에 그린 선 아이콘. 크기에 맞춰 확대·축소되므로 DPI와 무관하게 선명하다.
    internal static class GlyphPainter
    {
        public static void Draw(Graphics g, RectangleF box, GlyphKind kind, Color color)
        {
            float unit = Math.Min(box.Width, box.Height) / 24f;
            float ox = box.X + (box.Width - unit * 24f) / 2f;
            float oy = box.Y + (box.Height - unit * 24f) / 2f;

            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, Math.Max(1f, unit * 1.9f)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (kind == GlyphKind.Warning)
                {
                    PointF[] triangle =
                    {
                        P(ox, oy, unit, 12f, 3.5f),
                        P(ox, oy, unit, 21.5f, 20f),
                        P(ox, oy, unit, 2.5f, 20f)
                    };
                    g.DrawPolygon(pen, triangle);
                    g.DrawLine(pen, P(ox, oy, unit, 12f, 10f), P(ox, oy, unit, 12f, 13.5f));
                    g.FillEllipse(brush, ox + unit * 11f, oy + unit * 16f, unit * 2f, unit * 2f);
                }
                else
                {
                    // 스피커 몸체(채움)
                    PointF[] body =
                    {
                        P(ox, oy, unit, 3f, 9f),
                        P(ox, oy, unit, 7f, 9f),
                        P(ox, oy, unit, 12.5f, 4.5f),
                        P(ox, oy, unit, 12.5f, 19.5f),
                        P(ox, oy, unit, 7f, 15f),
                        P(ox, oy, unit, 3f, 15f)
                    };
                    g.FillPolygon(brush, body);
                    g.DrawPolygon(pen, body);

                    if (kind == GlyphKind.Mute)
                    {
                        g.DrawLine(pen, P(ox, oy, unit, 16f, 9f), P(ox, oy, unit, 21.5f, 15f));
                        g.DrawLine(pen, P(ox, oy, unit, 21.5f, 9f), P(ox, oy, unit, 16f, 15f));
                    }
                    else
                    {
                        // 작은 음파
                        g.DrawArc(pen, ox + unit * 9.5f, oy + unit * 8f, unit * 8f, unit * 8f, -45f, 90f);
                        if (kind != GlyphKind.VolumeDown)
                        {
                            // 큰 음파(볼륨 올리기·브랜드 아이콘)
                            g.DrawArc(pen, ox + unit * 7f, oy + unit * 4.5f, unit * 15f, unit * 15f, -48f, 96f);
                        }
                    }
                }
            }

            g.SmoothingMode = previous;
        }

        private static PointF P(float ox, float oy, float unit, float x, float y)
        {
            return new PointF(ox + x * unit, oy + y * unit);
        }
    }

    // 둥근 사각 타일 위에 아이콘을 그리는 장식용 컨트롤(포커스·탭 이동 없음).
    internal sealed class GlyphTile : Control
    {
        private GlyphKind kind;
        private Color tileColor;
        private Color glyphColor;

        public GlyphTile(GlyphKind kind, Color tileColor, Color glyphColor, int logicalSize)
        {
            this.kind = kind;
            this.tileColor = tileColor;
            this.glyphColor = glyphColor;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            this.SetStyle(ControlStyles.Selectable, false);
            this.TabStop = false;
            this.BackColor = Color.Transparent;
            this.Size = new Size(logicalSize, logicalSize);
            this.AccessibleRole = AccessibleRole.Graphic;
        }

        public void SetColors(Color tile, Color glyph)
        {
            if (this.tileColor == tile && this.glyphColor == glyph)
            {
                return;
            }

            this.tileColor = tile;
            this.glyphColor = glyph;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(0, 0, this.Width - 1, this.Height - 1);
            float inset = 0f;
            if (this.tileColor != Color.Empty && this.tileColor != Color.Transparent)
            {
                using (GraphicsPath path = UiTheme.RoundedRect(bounds, this.Width * 0.2f))
                using (SolidBrush brush = new SolidBrush(this.tileColor))
                {
                    g.FillPath(brush, path);
                }

                inset = this.Width * 0.2f;
            }

            RectangleF glyph = RectangleF.Inflate(bounds, -inset, -inset);
            GlyphPainter.Draw(g, glyph, this.kind, this.glyphColor);
        }
    }

    #endregion

    #region 둥근 패널·배지

    // 둥근 배경과 1px 테두리를 그리는 패널. 모서리 바깥은 부모 배경색으로 남는다.
    // 내용은 Padding 안쪽에 두며, 내용 컨트롤의 BackColor는 FillColor로 맞춘다.
    internal class RoundedPanel : Panel
    {
        private readonly Color fillColor;
        private readonly Color borderColor;
        private readonly int radius;

        public RoundedPanel(Color fill, Color border, int radius)
        {
            this.fillColor = fill;
            this.borderColor = border;
            this.radius = radius;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            this.BackColor = UiTheme.Background;
        }

        public Color FillColor
        {
            get { return this.fillColor; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = UiTheme.Scale(this, this.radius);
            RectangleF bounds = new RectangleF(0.5f, 0.5f, this.Width - 1.5f, this.Height - 1.5f);
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, r))
            using (SolidBrush brush = new SolidBrush(this.fillColor))
            using (Pen pen = new Pen(this.borderColor, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    // 상단의 상태 배지(점 + 짧은 문구). 문구가 바뀌면 크기를 다시 계산한다.
    internal sealed class StatusPill : Control
    {
        private Color textColor = UiTheme.NeutralText;
        private Color fillColor = UiTheme.NeutralFill;
        private Color dotColor = UiTheme.NeutralDot;
        private RuntimeState state = (RuntimeState)(-1);

        public StatusPill()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            this.SetStyle(ControlStyles.Selectable, false);
            this.TabStop = false;
            this.AccessibleRole = AccessibleRole.StaticText;
            this.SetState(RuntimeState.Waiting);
        }

        public void SetState(RuntimeState newState)
        {
            if (newState == this.state)
            {
                return;
            }

            this.state = newState;
            string label;
            UiTheme.Describe(newState, out label, out this.textColor, out this.fillColor, out this.dotColor);
            this.Text = label;
            this.AccessibleName = "현재 상태: " + label;
            this.UpdateSize();
            this.Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            this.UpdateSize();
        }

        // 크기는 이미 DPI가 반영된 글꼴로 계산하므로, 폼 자동 배율이 크기를 한 번 더
        // 곱하지 않도록 배율 적용 직후 다시 계산한다.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            this.UpdateSize();
        }

        private void UpdateSize()
        {
            Size text = TextRenderer.MeasureText(this.Text, this.Font);
            int padX = (int)Math.Round(UiTheme.Scale(this, 12));
            int dot = (int)Math.Round(UiTheme.Scale(this, 8));
            int gap = (int)Math.Round(UiTheme.Scale(this, 6));
            int padY = (int)Math.Round(UiTheme.Scale(this, 5));
            this.Size = new Size(padX + dot + gap + text.Width + padX, text.Height + padY * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush background = new SolidBrush(this.Parent != null ? this.Parent.BackColor : UiTheme.Background))
            {
                g.FillRectangle(background, this.ClientRectangle);
            }

            RectangleF bounds = new RectangleF(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, this.Height / 2f))
            using (SolidBrush fill = new SolidBrush(this.fillColor))
            {
                g.FillPath(fill, path);
            }

            float padX = UiTheme.Scale(this, 12);
            float dot = UiTheme.Scale(this, 8);
            float gap = UiTheme.Scale(this, 6);
            using (SolidBrush dotBrush = new SolidBrush(this.dotColor))
            {
                g.FillEllipse(dotBrush, padX, (this.Height - dot) / 2f, dot, dot);
            }

            Rectangle textBounds = new Rectangle(
                (int)(padX + dot + gap),
                0,
                this.Width - (int)(padX + dot + gap),
                this.Height);
            TextRenderer.DrawText(
                g,
                this.Text,
                this.Font,
                textBounds,
                this.textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // 하단 바: 흰 배경 + 위쪽 구분선
    internal sealed class FooterPanel : Panel
    {
        public FooterPanel()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = UiTheme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(UiTheme.CardBorder, 1f))
            {
                e.Graphics.DrawLine(pen, 0, 0, this.Width, 0);
            }
        }
    }

    #endregion

    #region 수정자 토글

    // Ctrl/Alt/Shift 선택용 토글 버튼. 실제로는 CheckBox(Appearance.Button)라서
    // Space로 전환되고 화면 낭독기에는 체크 상자로 전달된다.
    internal sealed class ModifierToggle : CheckBox
    {
        public ModifierToggle(string text, string accessibleName)
        {
            this.Text = text;
            this.AccessibleName = accessibleName;
            this.Appearance = Appearance.Button;
            this.FlatStyle = FlatStyle.Flat;
            this.TextAlign = ContentAlignment.MiddleCenter;
            // 버튼 모양 체크 상자는 자동 크기 계산이 글자를 잘라 그리므로 고정 크기를 쓴다.
            // 폼의 DPI 자동 배율이 이 크기를 함께 확대한다.
            this.AutoSize = false;
            this.Size = new Size(58, 30);
            this.Padding = new Padding(0);
            this.Margin = new Padding(0, 0, 6, 0);
            this.UseVisualStyleBackColor = false;
            this.Cursor = Cursors.Hand;
            this.FlatAppearance.BorderColor = UiTheme.ControlBorder;
            this.FlatAppearance.CheckedBackColor = UiTheme.Accent;
            this.ApplyColors();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            this.ApplyColors();
            base.OnCheckedChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            this.ApplyColors();
            base.OnEnabledChanged(e);
        }

        private void ApplyColors()
        {
            if (this.Checked)
            {
                this.BackColor = UiTheme.Accent;
                this.ForeColor = Color.White;
                this.FlatAppearance.BorderColor = UiTheme.Accent;
                this.FlatAppearance.MouseOverBackColor = UiTheme.AccentHover;
                this.FlatAppearance.MouseDownBackColor = UiTheme.AccentPressed;
            }
            else
            {
                this.BackColor = UiTheme.ControlFill;
                this.ForeColor = UiTheme.TextPrimary;
                this.FlatAppearance.BorderColor = UiTheme.ControlBorder;
                this.FlatAppearance.MouseOverBackColor = UiTheme.ControlHover;
                this.FlatAppearance.MouseDownBackColor = UiTheme.ControlPressed;
            }
        }
    }

    #endregion
}
