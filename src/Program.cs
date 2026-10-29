// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
using System;
using System.Threading;
using System.Windows.Forms;

namespace HaulingManager
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { NativeMethods.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.ToString(), MainForm.AppName + " - error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                AppSettings st = Storage.Load<AppSettings>("settings.json");
                Theme.Apply(Theme.Resolve(st.ThemeName ?? Theme.DefaultTheme, st.ThemeColors));
            }
            catch { }

            bool created;
            using (Mutex m = new Mutex(true, "C0P3Y_SCHaulingManager_SingleInstance", out created))
            {
                if (!created && Array.IndexOf(args, "--restart") >= 0)
                {
                    // restarted (as admin, or to apply a theme): wait for the old instance to close
                    try { created = m.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    MessageBox.Show("SC Hauling Manager is already running.", MainForm.AppName);
                    return;
                }
                Application.Run(new MainForm());
            }
        }
    }
}
