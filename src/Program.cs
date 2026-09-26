using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rsync GUI")]
[assembly: AssemblyDescription("Windows front end for rsync with auto-start")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace RsyncGui
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                // --runner            headless loop started by the boot task
                // --run NAME|ID [--dry]   run one job now and exit with rsync's exit code
                // --enable-autostart [USER PASSWORD]   create the boot task (SYSTEM when no user is given)
                // --disable-autostart                  remove the boot task
                // --quit              ask the open window to exit (same as Exit in the tray menu)
                // --shot DIR          test hook, saves a PNG of every tab
                if (args[0] == "--runner") return BackgroundRunner.Run();
                if (args[0] == "--enable-autostart" || args[0] == "--disable-autostart")
                {
                    Paths.EnsureDirs();
                    string err = args[0] == "--enable-autostart"
                        ? BootTask.Install(args.Length > 2 ? args[1] : "", args.Length > 2 ? args[2] : "")
                        : BootTask.Remove();
                    if (err != null) { MessageBox.Show(err, "Rsync GUI"); return 1; }
                    return 0;
                }
                if (args[0] == "--run" && args.Length > 1) return CliRun(args);
                if (args[0] == "--quit")
                {
                    // asks the open window (also one hidden in the tray) to exit, like Exit in the tray menu
                    try { System.Threading.EventWaitHandle.OpenExisting("Local\\" + InstanceKey() + ".quit").Set(); return 0; }
                    catch { return 1; }
                }
                if (args[0] == "--shot" && args.Length > 1)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    MainForm f = new MainForm();
                    f.Shots(args[1]);
                    return 0;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Paths.EnsureDirs(); }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot use the data folder " + Paths.DataDir + "\r\n\r\n" + ex.Message + "\r\n\r\nRun Rsync GUI as administrator.",
                    "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            try { Store.Load(); }
            catch (NewerFormatException ex)
            {
                MessageBox.Show(ex.Message, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            catch { }
            // One window per data folder. A second start shows the first one (it may be hidden in the tray).
            string key = InstanceKey();
            bool first;
            System.Threading.Mutex single = new System.Threading.Mutex(true, "Local\\" + key, out first);
            if (!first)
            {
                try
                {
                    System.Threading.EventWaitHandle.OpenExisting("Local\\" + key + ".show").Set();
                }
                catch
                {
                    MessageBox.Show("Rsync GUI is already running. Look for its icon in the system tray, next to the clock.", "Rsync GUI",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            System.Threading.EventWaitHandle show = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\" + key + ".show");
            MainForm main = new MainForm();
            main.EnableTray();
            System.Threading.EventWaitHandle quit = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\" + key + ".quit");
            main.WatchForShow(show, quit);
            Application.Run(main);
            GC.KeepAlive(single);
            return 0;
        }

        // One window per data folder: this name identifies it.
        static string InstanceKey()
        {
            byte[] md5 = System.Security.Cryptography.MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(Paths.DataDir.ToLowerInvariant()));
            return "RsyncGui." + BitConverter.ToString(md5).Replace("-", "").Substring(0, 16);
        }

        static int CliRun(string[] args)
        {
            Paths.EnsureDirs();
            bool dry = Array.IndexOf(args, "--dry") >= 0;
            StoreData all;
            try { all = Store.Load(); }
            catch (NewerFormatException) { return 3; }
            foreach (Job j in all.Jobs)
            {
                if (j.Id == args[1] || string.Equals(j.Name, args[1], StringComparison.OrdinalIgnoreCase))
                {
                    JobRun jr = new JobRun(j, dry);
                    jr.Trigger = "command line";
                    int code = jr.RunBlocking(null);
                    return code < 0 ? 1 : code;
                }
            }
            return 2;
        }
    }
}
