using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace RsyncGui
{
    public static class Paths
    {
        public static readonly string AppDir;
        public static readonly string DataDir;

        static Paths()
        {
            AppDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string env = Environment.GetEnvironmentVariable("RSYNCGUI_DATA");
            DataDir = !string.IsNullOrEmpty(env)
                ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RsyncGui");
        }

        public static string BinDir { get { return Path.Combine(AppDir, "bin"); } }
        public static string RsyncExe { get { return Path.Combine(BinDir, "rsync.exe"); } }
        public static string JobsFile { get { return Path.Combine(DataDir, "jobs.json"); } }
        public static string KnownHosts { get { return Path.Combine(DataDir, "known_hosts"); } }
        public static string HomeDir { get { return Path.Combine(DataDir, "home"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string LockDir { get { return Path.Combine(DataDir, "locks"); } }
        public static string StateDir { get { return Path.Combine(DataDir, "state"); } }
        public static string LogFile(string id) { return Path.Combine(LogDir, id + ".log"); }
        public static string LockFile(string id) { return Path.Combine(LockDir, id + ".lock"); }
        public static string StopFile(string id) { return Path.Combine(LockDir, id + ".stop"); }
        public static string StateFile(string id) { return Path.Combine(StateDir, id + ".txt"); }
        public static string OkFile(string id) { return Path.Combine(StateDir, id + ".ok"); }
        public static string NextFile(string id) { return Path.Combine(StateDir, id + ".next"); }
        public static string ProgressFile(string id) { return Path.Combine(StateDir, id + ".progress"); }
        public static string DaemonConf(string id) { return Path.Combine(DataDir, "rsyncd-" + id + ".conf"); }

        // The data folder holds job definitions that the runner executes as SYSTEM, so only
        // SYSTEM, Administrators and the creating account can write to it.
        public static void EnsureDirs()
        {
            if (!Directory.Exists(DataDir))
            {
                DirectorySecurity sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                    FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(DataDir).Create(sec);
            }
            Directory.CreateDirectory(HomeDir);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(LockDir);
            Directory.CreateDirectory(StateDir);
        }
    }

    public class Job
    {
        public string Id = "";
        public string Name = "New job";
        public string Kind = "sync";            // "sync" or "daemon"
        public string Sources = "";             // one per line
        public string Dest = "";
        public Dictionary<string, string> Opt = new Dictionary<string, string>();
        public string Includes = "";
        public string Excludes = "";
        public string Filters = "";
        public string Extra = "";
        public string SshPort = "";
        public string SshKey = "";
        public string SshOpts = "";
        public bool AcceptNew = true;
        public string Password = "";            // DPAPI protected, sent as RSYNC_PASSWORD
        public bool AutoStart;
        public int RepeatMinutes;
        public int RetryMinutes = 5;
        public string DaemonConfig = "";
        public string SchedMode = "";           // off, start, interval, daily, weekly ("" = old job, migrated on load)
        public int SchedEvery = 1;
        public string SchedUnit = "h";          // m or h
        public string SchedTime = "02:00";
        public string SchedDays = "0,1,2,3,4,5,6";   // 0 = Sunday
        public bool CatchUp = true;
        public int MaxRetries = 3;
        public string CloudMode = "copy";       // rclone: copy, sync, move, bisync
        public Dictionary<string, string> COpt = new Dictionary<string, string>();   // rclone flags

        public static Job Create()
        {
            Job j = new Job();
            j.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            j.Opt["recursive"] = "1";
            j.Opt["times"] = "1";
            j.Opt["partial"] = "1";
            j.Opt["verbose"] = "1";
            j.Opt["stats"] = "1";
            j.Opt["human-readable"] = "1";
            j.Opt["8-bit-output"] = "1";
            j.COpt["verbose"] = "1";
            j.COpt["skip-links"] = "1";
            j.SchedMode = "off";
            return j;
        }

        public const string DaemonTemplate =
            "# rsyncd.conf - see rsyncd.conf(5). Paths use Cygwin form: D:\\Backup is /cygdrive/d/Backup\r\n" +
            "use chroot = false\r\n" +
            "strict modes = false\r\n" +
            "max connections = 4\r\n" +
            "\r\n" +
            "[backup]\r\n" +
            "    path = /cygdrive/d/Backup\r\n" +
            "    comment = Backup share\r\n" +
            "    read only = false\r\n" +
            "    # hosts allow = 192.0.2.0/24\r\n" +
            "    # auth users = backupuser\r\n" +
            "    # secrets file = /cygdrive/c/ProgramData/RsyncGui/rsyncd.secrets\r\n";

        public Job Clone()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            Job c = s.Deserialize<Job>(s.Serialize(this));
            if (c.Opt == null) c.Opt = new Dictionary<string, string>(); if (c.COpt == null) c.COpt = new Dictionary<string, string>(); if (string.IsNullOrEmpty(c.CloudMode)) c.CloudMode = "copy";
            return c;
        }

        // Stable text form for change detection (JSON field order is not stable). Add new fields here.
        public string Signature()
        {
            StringBuilder sb = new StringBuilder();
            string[] f = { Id, Name, Kind, Sources, Dest, Includes, Excludes, Filters, Extra, SshPort, SshKey, SshOpts,
                           AcceptNew.ToString(), Password, AutoStart.ToString(), RepeatMinutes.ToString(), RetryMinutes.ToString(), DaemonConfig,
                           SchedMode, SchedEvery.ToString(), SchedUnit, SchedTime, SchedDays, CatchUp.ToString(), MaxRetries.ToString() };
            foreach (string x in f) sb.Append(x ?? "").Append('\u0001');
            List<string> keys = new List<string>(Opt.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) sb.Append(k).Append('=').Append(Opt[k]).Append('\u0002');
            sb.Append(CloudMode ?? "").Append('\u0001');
            List<string> ck = new List<string>(COpt.Keys);
            ck.Sort(StringComparer.Ordinal);
            foreach (string k in ck) sb.Append(k).Append('=').Append(COpt[k]).Append('\u0002');
            return sb.ToString();
        }

        public string PlainPassword()
        {
            return Secret.Unprotect(Password);
        }
    }

    public class StoreData
    {
        public int Format;                       // 0 = written by a version before 1.2.2
        public string WrittenBy = "";
        public List<Job> Jobs = new List<Job>();

        public string Signature()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Job j in Jobs) sb.Append(j.Signature()).Append('\u0003');
            return sb.ToString();
        }
    }

    public static class Store
    {
        // Raise this when a change to jobs.json would confuse an older program.
        public const int CurrentFormat = 2;

        static JavaScriptSerializer Ser()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static StoreData Load()
        {
            StoreData d = new StoreData();
            if (File.Exists(Paths.JobsFile))
            {
                string txt;
                using (FileStream fs = new FileStream(Paths.JobsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    txt = sr.ReadToEnd();
                if (txt.Trim().Length > 0) d = Ser().Deserialize<StoreData>(txt);
            }
            if (d.Format > CurrentFormat) throw new NewerFormatException(d.WrittenBy, d.Format);
            if (d.Jobs == null) d.Jobs = new List<Job>();
            foreach (Job j in d.Jobs)
            {
                if (j.Opt == null) j.Opt = new Dictionary<string, string>(); if (j.COpt == null) j.COpt = new Dictionary<string, string>(); if (string.IsNullOrEmpty(j.CloudMode)) j.CloudMode = "copy"; Sched.Migrate(j);
                if (string.IsNullOrEmpty(j.Id)) j.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            }
            return d;
        }

        public static string Serialize(StoreData d)
        {
            return Ser().Serialize(d);
        }

        public static void Save(StoreData d)
        {
            Paths.EnsureDirs();
            d.Format = CurrentFormat;
            d.WrittenBy = AppInfo.Version;
            string tmp = Paths.JobsFile + ".tmp";
            File.WriteAllText(tmp, Serialize(d), new UTF8Encoding(false));
            if (File.Exists(Paths.JobsFile)) File.Delete(Paths.JobsFile);
            File.Move(tmp, Paths.JobsFile);
        }
    }

    public static class AppInfo
    {
        // 1.2.2 style version taken from the program itself
        public static readonly string Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    }

    // jobs.json was saved by a newer version than this program understands.
    public class NewerFormatException : Exception
    {
        public NewerFormatException(string writtenBy, int format)
            : base("The job list was saved by a newer version of Rsync GUI (" + (string.IsNullOrEmpty(writtenBy) ? "format " + format : writtenBy) +
                   "). This program is version " + AppInfo.Version + " and does not read it. Update this program.") { }
    }

    // The background runner records which program it is, so the window can spot a runner of another version.
    public static class RunnerInfo
    {
        public static string VersionFile { get { return Path.Combine(Paths.StateDir, "_runner.version"); } }

        public static void WriteSelf()
        {
            try
            {
                File.WriteAllText(VersionFile, AppInfo.Version + Environment.NewLine +
                    System.Reflection.Assembly.GetExecutingAssembly().Location + Environment.NewLine + DateTime.Now.ToString("s"));
            }
            catch { }
        }

        public static bool Read(out string version, out string exe)
        {
            version = ""; exe = "";
            try
            {
                if (!File.Exists(VersionFile)) return false;
                string[] l = File.ReadAllLines(VersionFile);
                if (l.Length > 0) version = l[0].Trim();
                if (l.Length > 1) exe = l[1].Trim();
                return version.Length > 0;
            }
            catch { return false; }
        }

        // Null when the running background runner has this program's version.
        public static string Mismatch(bool runnerActive)
        {
            if (!runnerActive) return null;
            string v, exe;
            if (!Read(out v, out exe))
                return "The background runner is an older version (before " + AppInfo.Version + ") and does not report its version.";
            if (v != AppInfo.Version)
                return "The background runner is version " + v + " but this window is version " + AppInfo.Version + ".";
            return null;
        }
    }

    public static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RsyncGui.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] b = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
            return "dpapi:" + Convert.ToBase64String(b);
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:")) return "";
            try
            {
                byte[] b = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(6)), Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(b);
            }
            catch { return ""; }
        }
    }
}
