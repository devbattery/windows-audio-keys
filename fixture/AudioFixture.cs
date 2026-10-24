// AudioFixture - tiny disposable WinForms app for non-game end-to-end audio tests.
// It owns a looping SILENT PCM stream, shows its PID in the title and a TextBox,
// creates no mutex (two instances must produce two Core Audio sessions), and removes
// its temporary WAV on exit.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace AudioFixture
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            int autoCloseMilliseconds;
            if (!TryParseAutoClose(args, out autoCloseMilliseconds))
            {
                return 64;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int processId = Process.GetCurrentProcess().Id;
            string temporaryWav = Path.Combine(
                Path.GetTempPath(),
                "GameAudioKeys-fixture-" + Guid.NewGuid().ToString("N") + ".wav");
            SoundPlayer player = null;
            string audioError = null;

            try
            {
                WriteSilentWave(temporaryWav, 22050, 2);
                player = new SoundPlayer(temporaryWav);
                player.Load();
                player.PlayLooping();
            }
            catch (Exception ex)
            {
                audioError = ex.Message;
            }

            Form form = new Form();
            form.Text = "AudioFixture PID " + processId.ToString(CultureInfo.InvariantCulture);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ClientSize = new Size(440, 230);
            form.MinimumSize = new Size(360, 200);

            TextBox details = new TextBox();
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical;
            details.Dock = DockStyle.Fill;
            details.BackColor = Color.White;
            StringBuilder text = new StringBuilder();
            text.AppendLine("AudioFixture");
            text.AppendLine();
            text.AppendLine("PID: " + processId.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Silent looping PCM stream is active.");
            text.AppendLine("Close this window to stop audio and delete the temporary WAV.");
            if (audioError != null)
            {
                text.AppendLine();
                text.AppendLine("AUDIO ERROR: " + audioError);
            }

            details.Text = text.ToString();
            form.Controls.Add(details);

            Timer timer = null;
            if (autoCloseMilliseconds > 0)
            {
                timer = new Timer();
                timer.Interval = autoCloseMilliseconds;
                timer.Tick += delegate
                {
                    timer.Stop();
                    form.Close();
                };
                timer.Start();
                form.FormClosed += delegate
                {
                    timer.Stop();
                    timer.Dispose();
                };
            }

            try
            {
                Application.Run(form);
            }
            finally
            {
                if (player != null)
                {
                    try
                    {
                        player.Stop();
                    }
                    catch (Exception)
                    {
                        // The fixture is closing; audio cleanup failure is not fatal.
                    }

                    player.Dispose();
                }

                try
                {
                    if (File.Exists(temporaryWav))
                    {
                        File.Delete(temporaryWav);
                    }
                }
                catch (Exception)
                {
                    // A locked temporary file is harmless and can be deleted later.
                }
            }

            return 0;
        }

        private static bool TryParseAutoClose(string[] args, out int milliseconds)
        {
            milliseconds = 0;
            if (args == null)
            {
                return true;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--auto-close-ms")
                {
                    if (i + 1 >= args.Length)
                    {
                        return false;
                    }

                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)
                        || milliseconds < 0)
                    {
                        return false;
                    }

                    i++;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        // 44-byte PCM WAV header plus zero samples. PlayLooping keeps a real Core Audio
        // session alive without making noise.
        private static void WriteSilentWave(string path, int sampleRate, int seconds)
        {
            const short channels = 1;
            const short bitsPerSample = 16;
            int dataSize = sampleRate * seconds * channels * (bitsPerSample / 8);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                byte[] silence = new byte[8192];
                int remaining = dataSize;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, silence.Length);
                    writer.Write(silence, 0, chunk);
                    remaining -= chunk;
                }
            }
        }
    }
}
