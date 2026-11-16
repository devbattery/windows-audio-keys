// Windows Audio Keys - 창·트레이·실행 파일 아이콘을 코드로 그려 만든다(.ico 원본 파일 없음).
// 강조색 타일 위 흰 스피커. 상태에 따라 타일 색만 바꾼다.
// 실행 파일에 내장하는 .ico도 빌드 때 이 코드(WriteIco)로 만들어 그림 원본을 한 곳에만 둔다.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GameAudioKeys
{
    internal static class AppIcons
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        // 아이콘 타일 기본 강조색(작업 표시줄이 밝든 어둡든 보이도록 진한 파랑)
        public static readonly Color Accent = Color.FromArgb(0, 95, 184);

        // 여러 크기 아이콘에 담는 크기. 작은 아이콘(16~24)·작업 표시줄·Alt+Tab(32~48)·
        // 탐색기 보기와 고DPI 배율(64, 256)을 덮어 Windows가 확대·축소로 흐리게 만들지 않게 한다.
        public static readonly int[] StandardSizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 256 };

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
            using (Bitmap bitmap = Render(tile, size))
            {
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

        // 여러 크기를 한 아이콘에 담는다. 창 아이콘으로 쓰면 WinForms와 Windows가
        // 작업 표시줄·제목 표시줄·Alt+Tab마다 가장 가까운 크기를 골라 선명하게 표시한다.
        // 반환된 Icon은 호출자가 Dispose한다.
        public static Icon CreateMultiSize(Color tile)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                WriteIco(stream, tile, StandardSizes);
                stream.Position = 0;
                // Icon(Stream)은 스트림 내용을 자체 버퍼로 복사하므로 스트림은 바로 닫아도 된다.
                return new Icon(stream);
            }
        }

        // .ico 형식으로 쓴다. 256은 PNG 압축(Vista 이후 표준), 나머지는 호환성이 가장 넓은
        // 32비트 BGRA DIB + AND 마스크로 쓴다. 빌드 스크립트가 이 결과를 실행 파일에 내장한다.
        public static void WriteIco(Stream output, Color tile, int[] sizes)
        {
            if (output == null)
            {
                throw new ArgumentNullException("output");
            }

            if (sizes == null || sizes.Length == 0 || sizes.Length > ushort.MaxValue)
            {
                throw new ArgumentException("아이콘 크기 목록이 비어 있거나 너무 깁니다.", "sizes");
            }

            byte[][] images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i] < 16 || sizes[i] > 256)
                {
                    throw new ArgumentOutOfRangeException("sizes", "아이콘 크기는 16~256 사이여야 합니다.");
                }

                using (Bitmap bitmap = Render(tile, sizes[i]))
                {
                    images[i] = sizes[i] >= 256 ? EncodePng(bitmap) : EncodeDib(bitmap);
                }
            }

            BinaryWriter writer = new BinaryWriter(output);

            // ICONDIR: 예약(0), 형식(1 = 아이콘), 이미지 개수
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);

            // ICONDIRENTRY(16바이트) 목록. 이미지 데이터는 디렉터리 바로 뒤에 순서대로 놓는다.
            int offset = 6 + (16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                // 너비·높이 256은 형식상 0으로 적는다.
                byte dimension = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);   // 팔레트 색 수(트루컬러는 0)
                writer.Write((byte)0);   // 예약
                writer.Write((ushort)1); // 색 평면
                writer.Write((ushort)32); // 픽셀당 비트
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < images.Length; i++)
            {
                writer.Write(images[i]);
            }

            writer.Flush();
        }

        // 한 크기의 아이콘 그림. 호출자가 Dispose한다.
        private static Bitmap Render(Color tile, int size)
        {
            if (size < 16)
            {
                size = 16;
            }

            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
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

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static byte[] EncodePng(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        // 아이콘용 DIB: BITMAPINFOHEADER(높이는 XOR+AND 두 장이라 2배) + 아래에서 위로 쌓은
        // BGRA 픽셀 + 1비트 AND 마스크(투명 픽셀 = 1, 각 행은 4바이트 경계로 맞춤).
        private static byte[] EncodeDib(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int maskStride = ((width + 31) / 32) * 4;
            int pixelBytes = width * height * 4;
            int maskBytes = maskStride * height;

            byte[] pixels = new byte[pixelBytes];
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                // Format32bppArgb는 메모리에 B,G,R,A 순서로 놓여 아이콘 DIB와 바이트 순서가 같다.
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        new IntPtr(data.Scan0.ToInt64() + ((long)y * data.Stride)),
                        pixels,
                        y * width * 4,
                        width * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            using (MemoryStream stream = new MemoryStream(40 + pixelBytes + maskBytes))
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(40);            // biSize
                writer.Write(width);         // biWidth
                writer.Write(height * 2);    // biHeight(XOR + AND)
                writer.Write((ushort)1);     // biPlanes
                writer.Write((ushort)32);    // biBitCount
                writer.Write(0);             // biCompression = BI_RGB
                writer.Write(pixelBytes + maskBytes); // biSizeImage
                writer.Write(0);             // biXPelsPerMeter
                writer.Write(0);             // biYPelsPerMeter
                writer.Write(0);             // biClrUsed
                writer.Write(0);             // biClrImportant

                // DIB는 아래 행부터 쓴다.
                for (int y = height - 1; y >= 0; y--)
                {
                    writer.Write(pixels, y * width * 4, width * 4);
                }

                for (int y = height - 1; y >= 0; y--)
                {
                    byte[] row = new byte[maskStride];
                    for (int x = 0; x < width; x++)
                    {
                        byte alpha = pixels[(((y * width) + x) * 4) + 3];
                        if (alpha == 0)
                        {
                            row[x / 8] |= (byte)(0x80 >> (x % 8));
                        }
                    }

                    writer.Write(row);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
