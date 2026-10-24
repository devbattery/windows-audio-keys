// Game Audio Keys - entry point, single-instance guard and test-only settings path.

using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameAudioKeys
{
    internal static class Program
    {
        private const string MutexName = @"Local\GameAudioKeys.SingleInstance";
        private const string SettingsDirectoryEnvironment = "GAME_AUDIO_KEYS_SETTINGS_DIR";
        private const string Usage =
            "사용법: GameAudioKeys.exe [--settings-dir 폴더]\r\n"
            + "--settings-dir은 자동 테스트 전용이며 일반 사용에는 필요하지 않습니다.";

        [STAThread]
        public static int Main(string[] args)
        {
            string settingsDirectory;
            string argumentError;
            if (!TryGetSettingsDirectory(args, out settingsDirectory, out argumentError))
            {
                MessageBox.Show(
                    argumentError + Environment.NewLine + Environment.NewLine + Usage,
                    "Game Audio Keys",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 64;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Game Audio Keys가 이미 실행 중입니다. 트레이 아이콘을 열어 설정을 사용하세요.",
                        "Game Audio Keys",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 0;
                }

                try
                {
                    string settingsPath = Path.Combine(settingsDirectory, "settings.json");
                    SettingsLoadResult load = SettingsStore.Load(settingsPath);
                    using (TrayAppContext context = new TrayAppContext(load, settingsPath))
                    {
                        Application.Run(context);
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Game Audio Keys를 시작하지 못했습니다: " + ex.Message,
                        "Game Audio Keys",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 1;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static bool TryGetSettingsDirectory(string[] args, out string directory, out string error)
        {
            directory = null;
            error = null;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string argument = args[i];
                    if (argument == "--settings-dir")
                    {
                        if (i + 1 >= args.Length)
                        {
                            error = "--settings-dir 뒤에 폴더 경로가 필요합니다.";
                            return false;
                        }

                        directory = args[i + 1];
                        i++;
                    }
                    else if (argument.StartsWith("--settings-dir=", StringComparison.OrdinalIgnoreCase))
                    {
                        directory = argument.Substring("--settings-dir=".Length);
                    }
                    else if (argument == "--help" || argument == "-h" || argument == "/?")
                    {
                        error = Usage;
                        return false;
                    }
                    else
                    {
                        error = "알 수 없는 인수입니다: " + argument;
                        return false;
                    }
                }
            }

            if (string.IsNullOrEmpty(directory))
            {
                directory = Environment.GetEnvironmentVariable(SettingsDirectoryEnvironment);
            }

            if (string.IsNullOrEmpty(directory))
            {
                directory = SettingsStore.DefaultDirectory;
            }

            try
            {
                directory = Path.GetFullPath(directory);
            }
            catch (Exception ex)
            {
                error = "설정 폴더 경로가 올바르지 않습니다: " + ex.Message;
                return false;
            }

            return true;
        }
    }
}
