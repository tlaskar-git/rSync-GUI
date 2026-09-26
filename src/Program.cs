using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rsync GUI")]
[assembly: AssemblyDescription("Windows front end for rsync with auto-start")]
[assembly: AssemblyVersion("1.2.2.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]

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
            Application.Run(new MainForm());
            return 0;
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
                    int code = new JobRun(j, dry).RunBlocking(null);
                    return code < 0 ? 1 : code;
                }
            }
            return 2;
        }
    }
}
