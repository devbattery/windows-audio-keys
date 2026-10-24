// Windows Audio Keys - entry point, helper mode, single-instance guard and the
// test-only settings path.

using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameAudioKeys
{
    internal static class Program
    {
        private const string MutexName = @"Local\GameAudioKeys.SingleInstance";

        private const string Usage =
            "사용법: WindowsAudioKeys.exe [--startup] [--settings-dir 폴더]\r\n"
            + "--startup: Windows 로그인 자동 시작용으로 설정 창 없이 트레이에서 시작합니다.\r\n"
            + "--settings-dir: 자동 테스트 전용이며 일반 사용에는 필요하지 않습니다.\r\n"
            + "관리자 권한 자동 시작은 프로그램의 설정 창에서 켜고 끕니다.";

        [STAThread]
        public static int Main(string[] args)
        {
            StartupCommandLine command = StartupCli.Parse(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (command.Kind == StartupCommandKind.Error)
            {
                MessageBox.Show(
                    command.ErrorKo + Environment.NewLine + Environment.NewLine + Usage,
                    StartupConstants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 64;
            }

            // The elevated helper must run before the single-instance mutex: it is a
            // short-lived sibling process of an already running UI instance.
            if (command.Kind == StartupCommandKind.Helper)
            {
                return StartupTaskHost.RunHelper(command);
            }

            if (command.HelpRequested)
            {
                MessageBox.Show(
                    Usage,
                    StartupConstants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            string environmentDirectory = Environment.GetEnvironmentVariable(
                StartupConstants.SettingsDirectoryEnvironment);
            bool settingsOverridden = StartupCli.IsSettingsOverrideActive(
                command.SettingsDirectoryOverridden,
                environmentDirectory);

            string settingsDirectory = command.SettingsDirectory;
            if (string.IsNullOrEmpty(settingsDirectory))
            {
                settingsDirectory = environmentDirectory;
            }

            if (string.IsNullOrEmpty(settingsDirectory))
            {
                settingsDirectory = SettingsStore.DefaultDirectory;
            }

            try
            {
                settingsDirectory = Path.GetFullPath(settingsDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "설정 폴더 경로가 올바르지 않습니다: " + ex.Message,
                    StartupConstants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 64;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Login startup is a background convenience: a second automatic
                    // start is a silent success, not a dialog over the user's work.
                    if (command.StartupMode)
                    {
                        return 0;
                    }

                    MessageBox.Show(
                        StartupConstants.ProductName + "가 이미 실행 중입니다. 트레이 아이콘을 열어 설정을 사용하세요.",
                        StartupConstants.ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 0;
                }

                try
                {
                    string settingsPath = Path.Combine(settingsDirectory, "settings.json");
                    SettingsLoadResult load = SettingsStore.Load(settingsPath);
                    using (TrayAppContext context = new TrayAppContext(
                        load,
                        settingsPath,
                        settingsOverridden,
                        command.StartupMode))
                    {
                        Application.Run(context);
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        StartupConstants.ProductName + "를 시작하지 못했습니다: " + ex.Message,
                        StartupConstants.ProductName,
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
    }
}
