// Windows Audio Keys - 테마 계층. 라이트/다크/고대비 팔레트, Windows 앱 테마 감지와
// 실행 중 변경 알림, 다크 제목 표시줄, 공용 그리기 도구(벡터 아이콘·둥근 패널).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GameAudioKeys
{
    // 설정 창 상태 문구·상태 점·트레이 아이콘이 함께 쓰는 실행 상태.
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
        Warning,
        Info,
        Power,
        Activity
    }

    internal enum ThemeKind
    {
        Light,
        Dark,
        HighContrast
    }

    #region 팔레트

    // 화면 전체가 쓰는 색 토큰. 직접 그린 컨트롤은 그릴 때마다 ThemeManager.Current에서 읽는다.
    internal sealed class ThemePalette
    {
        public ThemeKind Kind;
        public Color Background;
        public Color Card;
        public Color CardBorder;
        public Color Text;
        public Color SubText;
        public Color Disabled;
        public Color Accent;
        public Color AccentHover;
        public Color AccentPressed;
        public Color OnAccent;
        public Color IconTile;
        public Color Keycap;
        public Color KeycapBorder;
        public Color KeycapShadow;
        public Color Field;
        public Color FieldHover;
        public Color FieldPressed;
        public Color FieldBorder;
        public Color SwitchOff;
        public Color Success;
        public Color Warning;
        public Color Error;
        public Color InfoFill;
        public Color InfoText;
        public Color Footer;
        public Color FocusRing;

        public bool IsDark
        {
            get { return this.Kind == ThemeKind.Dark; }
        }

        // 라이트: Windows 11 설정 앱 계열. 보조 글자는 카드·배경 모두에서 대비 4.5:1 이상.
        public static readonly ThemePalette Light = new ThemePalette
        {
            Kind = ThemeKind.Light,
            Background = Rgb(243, 243, 243),
            Card = Rgb(251, 251, 251),
            CardBorder = Rgb(229, 229, 229),
            Text = Rgb(27, 27, 27),
            SubText = Rgb(93, 93, 93),
            Disabled = Rgb(160, 160, 160),
            Accent = Rgb(0, 95, 184),
            AccentHover = Rgb(25, 117, 197),
            AccentPressed = Rgb(0, 74, 143),
            OnAccent = Color.White,
            IconTile = Rgb(234, 242, 251),
            Keycap = Color.White,
            KeycapBorder = Rgb(209, 209, 209),
            KeycapShadow = Rgb(196, 196, 196),
            Field = Color.White,
            FieldHover = Rgb(246, 246, 246),
            FieldPressed = Rgb(240, 240, 240),
            FieldBorder = Rgb(209, 209, 209),
            SwitchOff = Rgb(93, 93, 93),
            Success = Rgb(15, 123, 15),
            Warning = Rgb(138, 75, 0),
            Error = Rgb(196, 43, 28),
            InfoFill = Rgb(234, 242, 251),
            InfoText = Rgb(0, 62, 115),
            Footer = Rgb(251, 251, 251),
            FocusRing = Rgb(27, 27, 27)
        };

        // 다크: Windows 11 다크 계열. 강조색 위 글자는 검정(밝은 파랑 배경 대비 확보).
        public static readonly ThemePalette Dark = new ThemePalette
        {
            Kind = ThemeKind.Dark,
            Background = Rgb(32, 32, 32),
            Card = Rgb(43, 43, 43),
            CardBorder = Rgb(56, 56, 56),
            Text = Color.White,
            SubText = Rgb(200, 200, 200),
            Disabled = Rgb(120, 120, 120),
            Accent = Rgb(76, 194, 255),
            AccentHover = Rgb(72, 177, 232),
            AccentPressed = Rgb(66, 161, 210),
            OnAccent = Color.Black,
            IconTile = Rgb(31, 52, 70),
            Keycap = Rgb(58, 58, 58),
            KeycapBorder = Rgb(80, 80, 80),
            KeycapShadow = Rgb(21, 21, 21),
            Field = Rgb(51, 51, 51),
            FieldHover = Rgb(58, 58, 58),
            FieldPressed = Rgb(46, 46, 46),
            FieldBorder = Rgb(74, 74, 74),
            SwitchOff = Rgb(200, 200, 200),
            Success = Rgb(108, 203, 95),
            Warning = Rgb(252, 225, 0),
            Error = Rgb(255, 153, 164),
            InfoFill = Rgb(31, 52, 70),
            InfoText = Rgb(230, 243, 252),
            Footer = Rgb(39, 39, 39),
            FocusRing = Color.White
        };

        // 고대비: 사용자가 고른 시스템 색을 그대로 쓴다(우리 색으로 덮어쓰지 않음).
        public static ThemePalette HighContrast()
        {
            return new ThemePalette
            {
                Kind = ThemeKind.HighContrast,
                Background = SystemColors.Window,
                Card = SystemColors.Window,
                CardBorder = SystemColors.WindowText,
                Text = SystemColors.WindowText,
                SubText = SystemColors.WindowText,
                Disabled = SystemColors.GrayText,
                Accent = SystemColors.Highlight,
                AccentHover = SystemColors.Highlight,
                AccentPressed = SystemColors.Highlight,
                OnAccent = SystemColors.HighlightText,
                IconTile = SystemColors.Window,
                Keycap = SystemColors.Window,
                KeycapBorder = SystemColors.WindowText,
                KeycapShadow = SystemColors.WindowText,
                Field = SystemColors.ButtonFace,
                FieldHover = SystemColors.ButtonFace,
                FieldPressed = SystemColors.ButtonFace,
                FieldBorder = SystemColors.ControlText,
                SwitchOff = SystemColors.WindowText,
                Success = SystemColors.WindowText,
                Warning = SystemColors.WindowText,
                Error = SystemColors.WindowText,
                InfoFill = SystemColors.Window,
                InfoText = SystemColors.WindowText,
                Footer = SystemColors.Window,
                FocusRing = SystemColors.WindowText
            };
        }

        public Color StateColor(RuntimeState state)
        {
            switch (state)
            {
                case RuntimeState.Active:
                    return this.Success;
                case RuntimeState.Suspended:
                    return this.Accent;
                case RuntimeState.Conflict:
                case RuntimeState.Error:
                    return this.Error;
                default:
                    return this.SubText;
            }
        }

        private static Color Rgb(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }
    }

    #endregion

    #region 테마 감지·전환

    internal static class ThemeManager
    {
        private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

        private static ThemePalette current;
        private static string forcedTheme;
        private static bool watching;

        // 실행 중 테마가 바뀌면 UI 스레드에서 발생한다(SystemEvents가 구독 스레드로 전달).
        public static event EventHandler ThemeChanged;

        public static ThemePalette Current
        {
            get
            {
                if (current == null)
                {
                    current = Detect();
                }

                return current;
            }
        }

        // 테스트 설정 폴더 실행에서만 쓰는 강제 테마("light"/"dark"). 그 외 값은 무시한다.
        public static void SetTestOverride(string value)
        {
            forcedTheme = value == null ? null : value.Trim().ToLowerInvariant();
            current = Detect();
        }

        // AppsUseLightTheme 레지스트리 값 해석: DWORD 0이면 다크, 그 외(1, 없음, 형식 오류)는 라이트.
        public static bool ResolveDark(object registryValue)
        {
            return registryValue is int && (int)registryValue == 0;
        }

        public static void StartWatching()
        {
            if (watching)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            watching = true;
        }

        public static void StopWatching()
        {
            if (!watching)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            watching = false;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // 앱 테마 전환은 General, 고대비 전환은 Accessibility/Color 범주로 들어온다.
            if (e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.Accessibility
                && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            ThemePalette next = Detect();
            if (current != null && next.Kind == current.Kind && next.Kind != ThemeKind.HighContrast)
            {
                return;
            }

            current = next;
            EventHandler handler = ThemeChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        private static ThemePalette Detect()
        {
            if (SystemInformation.HighContrast)
            {
                return ThemePalette.HighContrast();
            }

            if (forcedTheme == "dark")
            {
                return ThemePalette.Dark;
            }

            if (forcedTheme == "light")
            {
                return ThemePalette.Light;
            }

            object value = null;
            try
            {
                value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", null);
            }
            catch (Exception)
            {
                // 읽을 수 없으면 라이트로 둔다.
            }

            return ResolveDark(value) ? ThemePalette.Dark : ThemePalette.Light;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // 제목 표시줄을 테마에 맞춘다(Windows 10 20H1+는 속성 20, 그 이전 일부 빌드는 19).
        // 지원하지 않는 Windows에서는 조용히 무시한다.
        public static void ApplyTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }

            int enabled = Current.IsDark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }

                // 비활성 상태에서도 제목 표시줄이 바로 다시 그려지도록 프레임 변경을 알린다.
                // SWP_NOSIZE|SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch (Exception)
            {
                // dwmapi가 없거나 거부해도 기능에는 영향이 없다.
            }
        }

        // 스크롤바 등 시스템이 그리는 부분을 테마에 맞춘다(다크 스크롤바는 Windows 10 1809+).
        public static void ApplyScrollBars(Control control)
        {
            if (control == null || !control.IsHandleCreated)
            {
                return;
            }

            try
            {
                SetWindowTheme(control.Handle, Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (Exception)
            {
                // uxtheme 호출 실패는 무시한다.
            }
        }
    }

    #endregion

    #region 글꼴·공용 그리기

    internal static class UiTheme
    {
        private const string FontFamilyName = "Malgun Gothic";

        public static Font BodyFont()
        {
            return new Font(FontFamilyName, 9.75F, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font StrongFont()
        {
            return new Font(FontFamilyName, 9.75F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font SmallFont()
        {
            return new Font(FontFamilyName, 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font SectionFont()
        {
            return new Font(FontFamilyName, 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font TitleFont()
        {
            return new Font(FontFamilyName, 15F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static string StateLabel(RuntimeState state)
        {
            switch (state)
            {
                case RuntimeState.Active:
                    return "사용 중";
                case RuntimeState.Suspended:
                    return "설정 창 활성";
                case RuntimeState.Paused:
                    return "일시정지";
                case RuntimeState.Conflict:
                    return "단축키 충돌";
                case RuntimeState.Error:
                    return "오류";
                default:
                    return "대기 중";
            }
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

        // 현재 글꼴 높이 기준 DPI 배율(96 DPI에서 9.75pt 맑은 고딕 높이 약 17px 기준).
        public static float Scale(Control control, float logicalPixels)
        {
            return logicalPixels * control.Font.Height / 17f;
        }

        // 직접 그린 컨트롤이 모서리 바깥을 칠할 "바로 아래 표면" 색. 둥근 카드 안이면 카드 색.
        public static Color SurfaceOf(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            {
                RoundedPanel rounded = parent as RoundedPanel;
                if (rounded != null)
                {
                    return rounded.FillColor;
                }

                if (parent.BackColor.A == 255)
                {
                    return parent.BackColor;
                }
            }

            return ThemeManager.Current.Background;
        }

        // 키보드 포커스 표시(2px 링). 마우스 조작 중에는 Windows 규칙에 따라 숨는다.
        public static void DrawFocusRing(Graphics g, RectangleF bounds, float radius)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (Pen pen = new Pen(ThemeManager.Current.FocusRing, 2f))
            {
                g.DrawPath(pen, path);
            }
        }
    }

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

                switch (kind)
                {
                    case GlyphKind.Warning:
                        g.DrawPolygon(pen, new[] { P(ox, oy, unit, 12f, 3.5f), P(ox, oy, unit, 21.5f, 20f), P(ox, oy, unit, 2.5f, 20f) });
                        g.DrawLine(pen, P(ox, oy, unit, 12f, 10f), P(ox, oy, unit, 12f, 13.5f));
                        g.FillEllipse(brush, ox + unit * 11f, oy + unit * 16f, unit * 2f, unit * 2f);
                        break;
                    case GlyphKind.Info:
                        g.DrawEllipse(pen, ox + unit * 3f, oy + unit * 3f, unit * 18f, unit * 18f);
                        g.DrawLine(pen, P(ox, oy, unit, 12f, 11f), P(ox, oy, unit, 12f, 16.5f));
                        g.FillEllipse(brush, ox + unit * 11f, oy + unit * 6.6f, unit * 2f, unit * 2f);
                        break;
                    case GlyphKind.Power:
                        g.DrawLine(pen, P(ox, oy, unit, 12f, 3f), P(ox, oy, unit, 12f, 11f));
                        g.DrawArc(pen, ox + unit * 4f, oy + unit * 4.5f, unit * 16f, unit * 16f, -50f, 280f);
                        break;
                    case GlyphKind.Activity:
                        g.DrawLines(pen, new[]
                        {
                            P(ox, oy, unit, 3f, 12f), P(ox, oy, unit, 7f, 12f), P(ox, oy, unit, 10f, 5f),
                            P(ox, oy, unit, 14f, 19f), P(ox, oy, unit, 17f, 12f), P(ox, oy, unit, 21f, 12f)
                        });
                        break;
                    default:
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
                            g.DrawArc(pen, ox + unit * 9.5f, oy + unit * 8f, unit * 8f, unit * 8f, -45f, 90f);
                            if (kind != GlyphKind.VolumeDown)
                            {
                                g.DrawArc(pen, ox + unit * 7f, oy + unit * 4.5f, unit * 15f, unit * 15f, -48f, 96f);
                            }
                        }

                        break;
                }
            }

            g.SmoothingMode = previous;
        }

        private static PointF P(float ox, float oy, float unit, float x, float y)
        {
            return new PointF(ox + x * unit, oy + y * unit);
        }
    }

    #endregion

    #region 장식 컨트롤

    // 둥근 타일 위 아이콘(장식용, 포커스 없음). 색은 그릴 때 팔레트에서 고른다.
    internal sealed class GlyphTile : Control
    {
        private readonly GlyphKind kind;
        private readonly Func<ThemePalette, Color> tile;
        private readonly Func<ThemePalette, Color> glyph;

        public GlyphTile(GlyphKind kind, Func<ThemePalette, Color> tile, Func<ThemePalette, Color> glyph, int logicalSize)
        {
            this.kind = kind;
            this.tile = tile;
            this.glyph = glyph;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            this.SetStyle(ControlStyles.Selectable, false);
            this.TabStop = false;
            this.Size = new Size(logicalSize, logicalSize);
            this.AccessibleRole = AccessibleRole.Graphic;

            // 장식용이다. 이름을 비워 두지 않으면 WinForms가 앞 라벨 문구를 빌려 와
            // 화면 낭독기가 같은 문장을 "그래픽"으로 한 번 더 읽는다.
            this.AccessibleName = string.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = e.Graphics;
            using (SolidBrush surface = new SolidBrush(UiTheme.SurfaceOf(this)))
            {
                g.FillRectangle(surface, this.ClientRectangle);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(0, 0, this.Width - 1, this.Height - 1);
            float inset = 0f;
            Color tileColor = this.tile != null ? this.tile(p) : Color.Empty;
            if (tileColor != Color.Empty)
            {
                using (GraphicsPath path = UiTheme.RoundedRect(bounds, this.Width * 0.24f))
                using (SolidBrush brush = new SolidBrush(tileColor))
                {
                    g.FillPath(brush, path);
                }

                if (p.Kind == ThemeKind.HighContrast)
                {
                    using (GraphicsPath path = UiTheme.RoundedRect(bounds, this.Width * 0.24f))
                    using (Pen pen = new Pen(p.CardBorder, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                inset = this.Width * 0.22f;
            }

            GlyphPainter.Draw(g, RectangleF.Inflate(bounds, -inset, -inset), this.kind, this.glyph(p));
        }
    }

    // 상태 점(8px 원). 상태가 바뀌면 다시 그린다.
    internal sealed class StatusDot : Control
    {
        private RuntimeState state = RuntimeState.Waiting;

        public StatusDot()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.Selectable, false);
            this.TabStop = false;
            this.Size = new Size(9, 9);
            this.AccessibleRole = AccessibleRole.Graphic;
            this.AccessibleName = string.Empty; // 상태 문구는 옆 라벨이 전달한다.
        }

        public RuntimeState State
        {
            get { return this.state; }
            set
            {
                if (this.state != value)
                {
                    this.state = value;
                    this.Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush surface = new SolidBrush(UiTheme.SurfaceOf(this)))
            {
                e.Graphics.FillRectangle(surface, this.ClientRectangle);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush dot = new SolidBrush(ThemeManager.Current.StateColor(this.state)))
            {
                e.Graphics.FillEllipse(dot, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }

    // 둥근 배경과 1px 테두리를 그리는 패널. 모서리 바깥은 부모 표면색으로 칠한다.
    // 안쪽 표·라벨은 BackColor를 Transparent로 두어 카드 색이 그대로 비치게 한다.
    internal class RoundedPanel : Panel
    {
        private readonly Func<ThemePalette, Color> fill;
        private readonly Func<ThemePalette, Color> border;
        private readonly int radius;

        public RoundedPanel(Func<ThemePalette, Color> fill, Func<ThemePalette, Color> border, int radius)
        {
            this.fill = fill;
            this.border = border;
            this.radius = radius;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        public Color FillColor
        {
            get { return this.fill(ThemeManager.Current); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush surface = new SolidBrush(UiTheme.SurfaceOf(this)))
            {
                e.Graphics.FillRectangle(surface, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = ThemeManager.Current;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = UiTheme.Scale(this, this.radius);
            RectangleF bounds = new RectangleF(0.5f, 0.5f, this.Width - 1.5f, this.Height - 1.5f);
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, r))
            using (SolidBrush brush = new SolidBrush(this.fill(p)))
            using (Pen pen = new Pen(this.border(p), 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    // 하단 바: 테마 바닥색 + 위쪽 구분선
    internal sealed class FooterPanel : Panel
    {
        public FooterPanel()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(ThemeManager.Current.CardBorder, 1f))
            {
                e.Graphics.DrawLine(pen, 0, 0, this.Width, 0);
            }
        }
    }

    #endregion
}
