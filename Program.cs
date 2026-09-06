using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal static class Program
    {
        /// <summary>
        /// A fault inside a timer tick or a paint handler repeats every time it fires. The
        /// first few are worth a dialog; after that the window would be unusable, so the rest
        /// go to error.log only.
        /// </summary>
        private const int MaxDialogs = 3;
        private static int _reported;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                ReportCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                ReportCrash(e.ExceptionObject as Exception);
            };

            try
            {
                App.Init();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup failed:\r\n\r\n" + ex.Message, App.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                // Whatever happened, do not leave the machine unable to sleep.
                Native.KeepAwake(false);
            }
        }

        private static void ReportCrash(Exception ex)
        {
            if (ex == null) return;

            string logPath = null;
            try
            {
                logPath = Path.Combine(App.DataDir ?? Path.GetTempPath(), "error.log");
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("s") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }

            int n = Interlocked.Increment(ref _reported);
            if (n > MaxDialogs) return;

            var more = n == MaxDialogs
                ? "\r\n\r\nFurther errors will be written to the log without interrupting you."
                : "";

            MessageBox.Show(
                "Something went wrong:\r\n\r\n" + ex.Message +
                "\r\n\r\nDetails were written to:\r\n" + (logPath ?? "(the log could not be written)") +
                "\r\n\r\nThe application will keep running." + more,
                App.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
