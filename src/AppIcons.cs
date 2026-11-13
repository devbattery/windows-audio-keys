// Windows Audio Keys - 창·트레이 아이콘을 실행 중에 그려 만든다(별도 .ico 파일 없음).
// 강조색 타일 위 흰 스피커. 상태에 따라 타일 색만 바꾼다.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GameAudioKeys
{
    internal static class AppIcons
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        // 아이콘 타일 기본 강조색(작업 표시줄이 밝든 어둡든 보이도록 진한 파랑)
        public static readonly Color Accent = Color.FromArgb(0, 95, 184);

        // 상태별 타일 색: 사용 중·설정 창 활성은 강조색, 일시정지·대기는 회색, 충돌·오류는 빨강
        public static Color TileColorFor(RuntimeState state)
        {
            switch (state)
            {
                case RuntimeState.Active:
                case RuntimeState.Suspended:
                    return Accent;
                case RuntimeState.Conflict:
                case RuntimeState.Error:
                    return Color.FromArgb(196, 43, 28);
                default:
                    return Color.FromArgb(112, 112, 112);
            }
        }

        // 반환된 Icon은 자체 핸들을 소유하므로 호출자가 Dispose한다.
        public static Icon Create(Color tile, int size)
        {
            if (size < 16)
            {
                size = 16;
            }

            using (Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF bounds = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
                    using (GraphicsPath path = UiTheme.RoundedRect(bounds, size * 0.22f))
                    using (SolidBrush brush = new SolidBrush(tile))
                    {
                        g.FillPath(brush, path);
                    }

                    // 작은 크기에서는 여백을 줄여 스피커가 뭉개지지 않게 한다.
                    float inset = size <= 20 ? size * 0.1f : size * 0.16f;
                    GlyphPainter.Draw(g, RectangleF.Inflate(bounds, -inset, -inset), GlyphKind.Speaker, Color.White);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(handle))
                    {
                        // Clone은 아이콘을 복사해 자체 핸들을 갖는다. 원본 HICON은 아래에서 해제.
                        return (Icon)borrowed.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
