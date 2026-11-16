// Windows Audio Keys - 빌드 도구. 앱이 실행 중에 그리는 아이콘과 같은 코드(AppIcons)로
// 여러 크기 .ico 파일을 만든다. build.ps1이 이 파일을 실행 파일에 내장(/win32icon)한다.
// 사용법: WriteAppIcon.exe <출력 .ico 경로>

using System;
using System.IO;

namespace GameAudioKeys.Tools
{
    internal static class WriteAppIcon
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.Error.WriteLine("Usage: WriteAppIcon.exe <output.ico>");
                return 2;
            }

            try
            {
                string path = Path.GetFullPath(args[0]);
                using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    AppIcons.WriteIco(stream, AppIcons.Accent, AppIcons.StandardSizes);
                }

                Console.WriteLine("Wrote: " + path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Icon generation failed: " + ex.Message);
                return 1;
            }
        }
    }
}
