using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace RsyncGui
{
    public static class Cmd
    {
        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return a;
            StringBuilder sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; }
                else if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; }
                else { sb.Append('\\', bs); bs = 0; sb.Append(c); }
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(a));
            }
            return sb.ToString();
        }

        // C:\dir\x -> /cygdrive/c/dir/x, \\host\share -> //host/share. Other values pass through.
        public static string ToCyg(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/'))
                return "/cygdrive/" + char.ToLower(p[0]) + "/" + p.Substring(3).Replace('\\', '/');
            if (p.StartsWith("\\\\")) return p.Replace('\\', '/');
            return p;
        }

        // 0 = local path, 1 = remote shell (host:path), 2 = rsync daemon (host::mod or rsync://)
        public static int RemoteKind(string s)
        {
            if (s.StartsWith("rsync://", StringComparison.OrdinalIgnoreCase)) return 2;
            int i = s.IndexOf(':');
            if (i < 0) return 0;
            if (i == 1 && char.IsLetter(s[0])) return 0;
            int slash = s.IndexOfAny(new char[] { '/', '\\' });
            if (slash >= 0 && slash < i) return 0;
            if (i + 1 < s.Length && s[i + 1] == ':') return 2;
            return 1;
        }

        static string LocalArg(string s)
        {
            s = s.Trim();
            if (RemoteKind(s) != 0) return s;
            return ToCyg(s).Replace('\\', '/');
        }

        public static List<string> Lines(string text)
        {
            List<string> r = new List<string>();
            if (text == null) return r;
            foreach (string l in text.Replace("\r", "").Split('\n'))
            {
                string t = l.Trim();
                if (t.Length > 0) r.Add(t);
            }
            return r;
        }

        // Shell-like split: whitespace separated, "double" and 'single' quotes group words.
        public static List<string> Split(string s)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(s)) return r;
            StringBuilder cur = new StringBuilder();
            bool have = false;
            char q = '\0';
            foreach (char c in s)
            {
                if (q != '\0') { if (c == q) q = '\0'; else cur.Append(c); }
                else if (c == '"' || c == '\'') { q = c; have = true; }
                else if (char.IsWhiteSpace(c)) { if (have || cur.Length > 0) { r.Add(cur.ToString()); cur.Length = 0; have = false; } }
                else cur.Append(c);
            }
            if (have || cur.Length > 0) r.Add(cur.ToString());
            return r;
        }

        static string SshTok(string s)
        {
            return s.IndexOf(' ') >= 0 ? "'" + s + "'" : s;
        }

        public static string SshCommand(Job j)
        {
            List<string> s = new List<string>();
            s.Add("ssh");
            if (!string.IsNullOrEmpty(j.SshPort)) { s.Add("-p"); s.Add(j.SshPort); }
            if (!string.IsNullOrEmpty(j.SshKey))
            {
                s.Add("-i"); s.Add(SshTok(Keys.PosixPath(j)));
                s.Add("-o"); s.Add("IdentitiesOnly=yes");
            }
            s.Add("-o"); s.Add("BatchMode=yes");
            if (j.AcceptNew) { s.Add("-o"); s.Add("StrictHostKeyChecking=accept-new"); }
            s.Add("-o"); s.Add(SshTok("UserKnownHostsFile=" + ToCyg(Paths.KnownHosts)));
            if (!string.IsNullOrEmpty(j.SshOpts)) s.Add(j.SshOpts.Trim());
            return string.Join(" ", s.ToArray());
        }

        public static List<string> BuildArgs(Job j, bool forceDry)
        {
            List<string> a = new List<string>();
            string v;

            if (j.Kind == "cloud") return BuildCloudArgs(j, forceDry);
            if (j.Kind == "daemon")
            {
                a.Add("--daemon");
                a.Add("--no-detach");
                a.Add("--config=" + ToCyg(Paths.DaemonConf(j.Id)));
                foreach (string k in OptTable.DaemonKeys)
                {
                    if (!j.Opt.TryGetValue(k, out v) || string.IsNullOrEmpty(v)) continue;
                    OptDef d = OptTable.All.Find(delegate(OptDef x) { return x.Key == k; });
                    if (d != null && d.Type == OT.Bool) { if (v == "1") a.Add("--" + k); }
                    else a.Add("--" + k + "=" + ToCyg(v));
                }
                a.AddRange(Split(j.Extra));
                return a;
            }

            foreach (OptDef d in OptTable.All)
            {
                if (!j.Opt.TryGetValue(d.Key, out v) || string.IsNullOrEmpty(v)) continue;
                if (d.Type == OT.Bool) { if (v == "1") a.Add("--" + d.Key); }
                else a.Add("--" + d.Key + "=" + ToCyg(v));
            }
            if (forceDry && !(j.Opt.TryGetValue("dry-run", out v) && v == "1")) a.Add("--dry-run");
            if (!(j.Opt.TryGetValue("quiet", out v) && v == "1")) a.Add("--info=progress2");

            List<string> src = Lines(j.Sources);
            bool needSsh = RemoteKind(j.Dest.Trim()) == 1;
            foreach (string s in src) if (RemoteKind(s) == 1) needSsh = true;
            if (needSsh && !(j.Opt.TryGetValue("rsh", out v) && !string.IsNullOrEmpty(v)))
            {
                a.Add("-e");
                a.Add(SshCommand(j));
            }

            foreach (string l in Lines(j.Filters)) if (!l.StartsWith("#")) a.Add("--filter=" + l);
            foreach (string l in Lines(j.Includes)) a.Add("--include=" + l);
            foreach (string l in Lines(j.Excludes)) a.Add("--exclude=" + l);
            a.AddRange(Split(j.Extra));

            foreach (string s in src) a.Add(LocalArg(s));
            if (j.Dest.Trim().Length > 0) a.Add(LocalArg(j.Dest));
            return a;
        }

        // An rsync job whose source or destination is "name:path" where name is one of the user's cloud
        // accounts. Rsync would treat it as an SSH host, so the job must use the Cloud type instead.
        public static string CloudMixup(Job j)
        {
            if (j.Kind != "sync") return null;
            List<string> parts = Lines(j.Sources);
            parts.Add((j.Dest ?? "").Trim());
            List<string> remotes = null;
            foreach (string s in parts)
            {
                if (s.Length == 0 || RemoteKind(s) != 1) continue;
                int i = s.IndexOf(':');
                string host = s.Substring(0, i);
                if (host.IndexOf('@') >= 0) continue;
                if (remotes == null) remotes = Rclone.Remotes();
                foreach (string r in remotes) if (string.Equals(r, host + ":", StringComparison.OrdinalIgnoreCase)) return host;
            }
            return null;
        }

        public static string Exe(Job j) { return j.Kind == "cloud" ? Rclone.Exe : Paths.RsyncExe; }

        public static List<string> BuildCloudArgs(Job j, bool forceDry)
        {
            List<string> a = new List<string>();
            string mode = Array.IndexOf(CloudOpts.Modes, j.CloudMode) >= 0 ? j.CloudMode : "copy";
            a.Add(mode);
            a.Add("--config"); a.Add(Rclone.Conf);
            string v;
            foreach (OptDef d in CloudOpts.All)
            {
                if (!CloudOpts.AppliesTo(d, mode)) continue;
                if (!j.COpt.TryGetValue(d.Key, out v) || string.IsNullOrEmpty(v)) continue;
                if (d.Type == OT.Bool) { if (v == "1") a.Add("--" + d.Key); }
                else a.Add("--" + d.Key + "=" + v);
            }
            if (forceDry && !(j.COpt.TryGetValue("dry-run", out v) && v == "1")) a.Add("--dry-run");
            if (!(j.COpt.TryGetValue("stats", out v) && !string.IsNullOrEmpty(v))) a.Add("--stats=3s");
            a.Add("--stats-log-level=NOTICE");
            foreach (string l in Lines(j.Filters)) if (!l.StartsWith("#")) a.Add("--filter=" + l);
            foreach (string l in Lines(j.Includes)) a.Add("--include=" + l);
            foreach (string l in Lines(j.Excludes)) a.Add("--exclude=" + l);
            a.AddRange(Split(j.Extra));
            List<string> src = Lines(j.Sources);
            if (src.Count > 0) a.Add(src[0].Trim());
            if (j.Dest.Trim().Length > 0) a.Add(j.Dest.Trim());
            return a;
        }

        public static string Preview(Job j, bool dry)
        {
            return Quote(Exe(j)) + " " + Join(BuildArgs(j, dry));
        }
    }

    // Cygwin's ssh refuses key files whose Windows ACLs look too open. Each run uses a private copy
    // of the key, readable only by the account that runs the job, inside the app's Cygwin root
    // (where ACLs are honoured). The original key file is never changed.
    public static class Keys
    {
        static string Sid { get { return WindowsIdentity.GetCurrent().User.Value; } }

        public static string PosixPath(Job j) { return "/tmp/keys/" + Sid + "/" + j.Id; }

        public static void Cleanup(Job j)
        {
            try
            {
                string dst = Path.Combine(Paths.AppDir, "tmp", "keys", Sid, j.Id);
                if (File.Exists(dst)) File.Delete(dst);
            }
            catch { }
        }

        public static string Prepare(Job j)
        {
            if (string.IsNullOrEmpty(j.SshKey)) return null;
            if (!File.Exists(j.SshKey)) return "SSH key file not found: " + j.SshKey;
            try
            {
                SecurityIdentifier me = WindowsIdentity.GetCurrent().User;
                string dir = Path.Combine(Paths.AppDir, "tmp", "keys", Sid);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(Path.Combine(Paths.AppDir, "tmp"));
                    Directory.CreateDirectory(Path.Combine(Paths.AppDir, "tmp", "keys"));
                    DirectorySecurity ds = new DirectorySecurity();
                    ds.SetAccessRuleProtection(true, false);
                    ds.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    new DirectoryInfo(dir).Create(ds);
                }
                string dst = Path.Combine(dir, j.Id);
                if (File.Exists(dst)) File.Delete(dst);
                byte[] data = File.ReadAllBytes(j.SshKey);
                if (data.Length > 0 && !Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 12)).StartsWith("PuTTY"))
                {
                    // OpenSSH wants LF line endings and a final newline
                    string txt = Encoding.ASCII.GetString(data).Replace("\r\n", "\n");
                    if (!txt.EndsWith("\n")) txt += "\n";
                    data = Encoding.ASCII.GetBytes(txt);
                }
                File.WriteAllBytes(dst, data);
                FileSecurity fs = new FileSecurity();
                fs.SetAccessRuleProtection(true, false);
                fs.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
                fs.SetOwner(me);
                File.SetAccessControl(dst, fs);
                return null;
            }
            catch (Exception ex) { return "Could not prepare the SSH key: " + ex.Message; }
        }
    }

    public class JobRun
    {
        public const int Busy = -999;
        public const int Killed = -998;
        public const int Missing = -997;
        public const int Config = -996;

        readonly Job job;
        readonly bool dry;
        Process proc;
        volatile bool killRequested;
        readonly object logLock = new object();
        StreamWriter log;
        ProgressParser pp;
        int errors, links, warnings, errLogged, linkLogged;
        long logBytes;
        bool logCapped;

        // A job log never grows past this many bytes (RSYNCGUI_LOGMAX overrides it for testing).
        static readonly long MaxLogBytes = ReadLogMax();

        static long ReadLogMax()
        {
            long v;
            return long.TryParse(Environment.GetEnvironmentVariable("RSYNCGUI_LOGMAX") ?? "", out v) && v > 0 ? v : 20L * 1024 * 1024;
        }

        public JobRun(Job j, bool dryRun) { job = j; dry = dryRun; }

        public static FileStream TryLock(string id)
        {
            try { return new FileStream(Paths.LockFile(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static bool IsRunning(string id)
        {
            FileStream fs = TryLock(id);
            if (fs == null) return true;
            fs.Dispose();
            return false;
        }

        public static void RequestStop(string id)
        {
            try { File.WriteAllText(Paths.StopFile(id), "stop"); } catch { }
        }

        public static string ExitText(int code, string kind)
        {
            if (kind == "cloud")
                switch (code)
                {
                    case 1: return "Error";
                    case 2: return "Syntax or usage error";
                    case 3: return "Folder not found";
                    case 4: return "File not found";
                    case 5: return "Temporary error, retries used up";
                    case 6: return "Some files failed";
                    case 7: return "Fatal error";
                    case 8: return "Transfer limit reached";
                    case 9: return "No files transferred";
                }
            return ExitText(code);
        }

        public static string ExitText(int code)
        {
            switch (code)
            {
                case 0: return "Success";
                case 1: return "Syntax or usage error";
                case 2: return "Protocol incompatibility";
                case 3: return "Errors selecting input/output files, dirs";
                case 4: return "Requested action not supported";
                case 5: return "Error starting client-server protocol";
                case 6: return "Daemon unable to append to log-file";
                case 10: return "Error in socket I/O";
                case 11: return "Error in file I/O";
                case 12: return "Error in rsync protocol data stream";
                case 13: return "Errors with program diagnostics";
                case 14: return "Error in IPC code";
                case 20: return "Received SIGUSR1 or SIGINT";
                case 21: return "Some error returned by waitpid()";
                case 22: return "Error allocating core memory buffers";
                case 23: return "Partial transfer due to error";
                case 24: return "Partial transfer due to vanished source files";
                case 25: return "The --max-delete limit stopped deletions";
                case 30: return "Timeout in data send/receive";
                case 35: return "Timeout waiting for daemon connection";
                case Busy: return "Already running";
                case Killed: return "Stopped";
                case Missing: return "Program not found";
                case Config: return "Job is set up wrongly";
                default: return "Exit code " + code;
            }
        }

        public static bool IsSuccess(int code, string kind)
        {
            return code == 0 || (kind == "sync" && code == 24);
        }

        public static DateTime? LastOk(string id)
        {
            try
            {
                DateTime t;
                if (DateTime.TryParse(File.ReadAllText(Paths.OkFile(id)).Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) return t;
            }
            catch { }
            return null;
        }

        // Filters progress noise, counts errors and keeps the log short.
        void OnLine(string line, Action<string> onLine)
        {
            if (pp != null && pp.Feed(line)) return;
            if (job.Kind == "cloud")
            {
                if (line.Contains("Can't follow symlink")) { Interlocked.Increment(ref links); if (Interlocked.Increment(ref linkLogged) > 3) return; }
                else if (line.Contains(" ERROR : ")) { Interlocked.Increment(ref errors); if (Interlocked.Increment(ref errLogged) > 100) return; }
                else if (line.Contains(" WARNING")) Interlocked.Increment(ref warnings);
            }
            else if (job.Kind == "sync" && (line.StartsWith("rsync:") || line.StartsWith("rsync error")))
            {
                Interlocked.Increment(ref errors);
                if (Interlocked.Increment(ref errLogged) > 100) return;
            }
            Log(line, onLine);
        }

        void Log(string line, Action<string> onLine) { Log(line, onLine, false); }

        void Log(string line, Action<string> onLine, bool force)
        {
            lock (logLock)
            {
                if (log != null)
                {
                    if (force || logBytes < MaxLogBytes)
                    {
                        log.WriteLine(line); log.Flush();
                        logBytes += Encoding.UTF8.GetByteCount(line) + 2;
                    }
                    else if (!logCapped)
                    {
                        logCapped = true;
                        log.WriteLine("(Log size limit of " + (MaxLogBytes >= 1024 * 1024 ? (MaxLogBytes / (1024 * 1024)) + " MB" : (MaxLogBytes / 1024) + " KB") + " reached. The rest of this run is not logged. The result is still recorded below.)");
                        log.Flush();
                    }
                }
            }
            if (onLine != null) onLine(line);
        }

        public void Kill()
        {
            killRequested = true;
            Process p = proc;
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                {
                    ProcessStartInfo k = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                    k.CreateNoWindow = true; k.UseShellExecute = false;
                    Process kp = Process.Start(k);
                    kp.WaitForExit(10000);
                }
            }
            catch { }
        }

        public int RunBlocking(Action<string> onLine)
        {
            Paths.EnsureDirs();
            FileStream lk = TryLock(job.Id);
            if (lk == null) return Busy;
            try
            {
                try { File.Delete(Paths.StopFile(job.Id)); } catch { }
                string logPath = Paths.LogFile(job.Id);
                try
                {
                    FileInfo fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                    {
                        string old = logPath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(logPath, old);
                    }
                }
                catch { }
                log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                try { logBytes = new FileInfo(logPath).Length; } catch { logBytes = 0; }

                Log("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  start  " + job.Name + (dry ? "  (dry run)" : "") + " ===", onLine, true);
                string exe = Cmd.Exe(job);
                if (!File.Exists(exe))
                {
                    Log(Path.GetFileName(exe) + " not found at " + exe, onLine);
                    return Finish(Missing, onLine);
                }

                if (job.Kind == "sync")
                {
                    string mix = Cmd.CloudMixup(job);
                    if (mix != null)
                    {
                        Log("\"" + mix + ":\" is a cloud account, not an SSH server. Open the job and set Type to Cloud. Nothing was run.", onLine);
                        return Finish(Config, onLine);
                    }
                }

                if (job.Kind == "daemon") File.WriteAllText(Paths.DaemonConf(job.Id), job.DaemonConfig.Replace("\r\n", "\n"), new UTF8Encoding(false));

                if (job.Kind == "sync")
                {
                    string keyErr = Keys.Prepare(job);
                    if (keyErr != null) { Log(keyErr, onLine); return Finish(1, onLine); }
                }
                List<string> args = Cmd.BuildArgs(job, dry);
                Log("> " + Path.GetFileNameWithoutExtension(exe) + " " + Cmd.Join(args), onLine);

                ProcessStartInfo psi = new ProcessStartInfo(exe, Cmd.Join(args));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.WorkingDirectory = Paths.DataDir;
                psi.EnvironmentVariables["PATH"] = Paths.BinDir + ";" + Environment.GetEnvironmentVariable("PATH");
                psi.EnvironmentVariables["HOME"] = Paths.HomeDir;
                psi.EnvironmentVariables["LANG"] = "C.UTF-8";
                psi.EnvironmentVariables["LC_ALL"] = "C.UTF-8";
                string pw = job.PlainPassword();
                if (pw.Length > 0) psi.EnvironmentVariables["RSYNC_PASSWORD"] = pw;

                try
                {
                    proc = new Process();
                    proc.StartInfo = psi;
                    if (job.Kind != "daemon") { pp = new ProgressParser(job.Id, job.Kind == "cloud"); pp.Save(true); }
                    proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) OnLine(e.Data, onLine); };
                    proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) OnLine(e.Data, onLine); };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                }
                catch (Exception ex)
                {
                    Log("Could not start rsync: " + ex.Message, onLine);
                    return Finish(1, onLine);
                }

                while (!proc.WaitForExit(1000))
                {
                    if (File.Exists(Paths.StopFile(job.Id)))
                    {
                        try { File.Delete(Paths.StopFile(job.Id)); } catch { }
                        Kill();
                    }
                }
                proc.WaitForExit();
                int code = killRequested ? Killed : proc.ExitCode;
                return Finish(code, onLine);
            }
            finally
            {
                ProgressInfo.Clear(job.Id);
                Keys.Cleanup(job);
                lock (logLock)
                {
                    if (log != null) { log.Dispose(); log = null; }
                }
                lk.Dispose();
            }
        }

        int Finish(int code, Action<string> onLine)
        {
            if (pp != null && pp.Summary != null)
                foreach (string l in pp.Summary) if (l.Trim().Length > 0) Log(l, onLine);
            if (links > 3) Log("(" + links + " symbolic links were skipped. Tick skip-links on the Cloud options tab to hide this notice.)", onLine);
            if (errors > 100) Log("(" + errors + " errors in total. Only the first 100 are shown above.)", onLine);
            string counts = errors > 0 ? "  " + errors + " errors" : "";
            Log("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  end  exit " + code + " (" + ExitText(code, job.Kind) + ")" + counts + " ===", onLine, true);
            try
            {
                File.WriteAllText(Paths.StateFile(job.Id), DateTime.Now.ToString("s") + "|" + code + "|" + (dry ? "1" : "0") + "|" + job.Kind + "|" + errors + "|" + links);
                if (!dry && IsSuccess(code, job.Kind)) File.WriteAllText(Paths.OkFile(job.Id), DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
            return code;
        }

        public static string LastResult(string id)
        {
            try
            {
                string[] p = File.ReadAllText(Paths.StateFile(id)).Split('|');
                DateTime t = DateTime.Parse(p[0]);
                int c = int.Parse(p[1]);
                int er = 0;
                if (p.Length > 4) int.TryParse(p[4], out er);
                string what = c == 0 ? "OK" : ExitText(c, p.Length > 3 ? p[3] : "") + (er > 0 ? " (" + er + " errors)" : "");
                return t.ToString("yyyy-MM-dd HH:mm") + "  " + what + (p[2] == "1" ? " (dry run)" : "");
            }
            catch { return ""; }
        }
    }

    // Windows scheduled task that starts the background runner at boot.
    public static class BootTask
    {
        public const string Name = "RsyncGui Runner";

        static int Schtasks(string args, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Process p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            output = o.Trim();
            return p.ExitCode;
        }

        public static bool Exists()
        {
            string o;
            return Schtasks("/Query /TN \"" + Name + "\"", out o) == 0;
        }

        static string Esc(string s) { return System.Security.SecurityElement.Escape(s); }

        public static string Install(string user, string password)
        {
            string exe = Path.Combine(Paths.AppDir, "RsyncGui.exe");
            bool system = string.IsNullOrEmpty(user);
            string principal = system
                ? "<UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel>"
                : "<UserId>" + Esc(user) + "</UserId><LogonType>Password</LogonType><RunLevel>HighestAvailable</RunLevel>";
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                " <RegistrationInfo><Description>Starts the Rsync GUI background runner at boot. It runs every job marked for auto-start.</Description></RegistrationInfo>\r\n" +
                " <Triggers><BootTrigger><Enabled>true</Enabled><Delay>PT30S</Delay></BootTrigger></Triggers>\r\n" +
                " <Principals><Principal id=\"Author\">" + principal + "</Principal></Principals>\r\n" +
                " <Settings>\r\n" +
                "  <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "  <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "  <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "  <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "  <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "  <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "  <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "  <Enabled>true</Enabled>\r\n" +
                "  <Hidden>false</Hidden>\r\n" +
                "  <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "  <Priority>7</Priority>\r\n" +
                "  <RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure>\r\n" +
                " </Settings>\r\n" +
                " <Actions Context=\"Author\"><Exec><Command>" + Esc(exe) + "</Command><Arguments>--runner</Arguments><WorkingDirectory>" + Esc(Paths.AppDir) + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "rsyncgui-task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                string o;
                string args = "/Create /F /TN \"" + Name + "\" /XML \"" + tmp + "\"";
                if (!system) args += " /RU \"" + user + "\" /RP \"" + password.Replace("\"", "\\\"") + "\"";
                int rc = Schtasks(args, out o);
                if (rc != 0) return "Could not create the scheduled task: " + o;
                Schtasks("/Run /TN \"" + Name + "\"", out o);
                return null;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static string Remove()
        {
            string o;
            Schtasks("/End /TN \"" + Name + "\"", out o);
            int rc = Schtasks("/Delete /F /TN \"" + Name + "\"", out o);
            return rc == 0 ? null : "Could not remove the scheduled task: " + o;
        }

        public static string StartNow()
        {
            string o;
            int rc = Schtasks("/Run /TN \"" + Name + "\"", out o);
            return rc == 0 ? null : o;
        }
    }

    // Headless mode used by the boot task: runs every job flagged for auto-start.
    public static class BackgroundRunner
    {
        class Worker
        {
            public Job Job;
            public string Sig;
            public volatile bool Stop;
            Thread th;
            JobRun cur;

            public void Start()
            {
                th = new Thread(Loop);
                th.IsBackground = true;
                th.Start();
            }

            public void Halt()
            {
                Stop = true;
                JobRun c = cur;
                if (c != null) c.Kill();
                ClearNext();
            }

            void SetNext(DateTime due)
            {
                try { File.WriteAllText(Paths.NextFile(Job.Id), due.ToString("s", System.Globalization.CultureInfo.InvariantCulture)); } catch { }
            }

            void ClearNext()
            {
                try { File.Delete(Paths.NextFile(Job.Id)); } catch { }
            }

            void SleepUntil(DateTime due)
            {
                while (!Stop && DateTime.Now < due) Thread.Sleep(1000);
            }

            DateTime InitialDue(DateTime now)
            {
                DateTime? ok = JobRun.LastOk(Job.Id);
                switch (Job.SchedMode)
                {
                    case "interval":
                        return ok == null ? now : ok.Value + Sched.Interval(Job);
                    case "daily":
                    case "weekly":
                        DateTime? prev = Sched.PrevSlot(Job, now);
                        if (Job.CatchUp && ok != null && prev != null && ok.Value < prev.Value) return now;   // missed while off
                        DateTime? next = Sched.NextSlot(Job, now);
                        return next == null ? DateTime.MaxValue : next.Value;
                    default:
                        return now;
                }
            }

            void Loop()
            {
                if (Job.Kind == "daemon")
                {
                    while (!Stop)
                    {
                        JobRun d = new JobRun(Job, false);
                        cur = d;
                        d.RunBlocking(null);
                        cur = null;
                        if (Stop) break;
                        for (int i = 0; i < 15 && !Stop; i++) Thread.Sleep(1000);
                    }
                    return;
                }

                DateTime due = InitialDue(DateTime.Now);
                int attempts = 0;
                while (!Stop)
                {
                    if (due == DateTime.MaxValue) break;
                    if (DateTime.Now < due) { SetNext(due); SleepUntil(due); if (Stop) break; }
                    ClearNext();

                    JobRun run = new JobRun(Job, false);
                    cur = run;
                    int code = run.RunBlocking(null);
                    cur = null;
                    if (Stop) break;
                    DateTime fin = DateTime.Now;

                    if (code == JobRun.Busy) { due = fin.AddSeconds(30); continue; }
                    bool ok = JobRun.IsSuccess(code, Job.Kind);
                    bool fatal = code == JobRun.Config || code == JobRun.Missing;
                    if (ok) attempts = 0;
                    else if (!fatal && attempts < Job.MaxRetries && Job.RetryMinutes > 0)
                    {
                        attempts++;
                        due = fin.AddMinutes(Job.RetryMinutes);
                        continue;
                    }
                    else attempts = 0;

                    if (Job.SchedMode == "start") break;
                    if (Job.SchedMode == "interval") due = fin + Sched.Interval(Job);
                    else
                    {
                        DateTime? n = Sched.NextSlot(Job, fin);
                        if (n == null) break;
                        due = n.Value;
                    }
                }
                ClearNext();
            }
        }

        static void Note(string s)
        {
            try
            {
                string rl = Path.Combine(Paths.LogDir, "_runner.log");
                if (File.Exists(rl) && new FileInfo(rl).Length > 1024 * 1024) File.Delete(rl);
                File.AppendAllText(rl,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + "\r\n");
            }
            catch { }
        }

        public static int Run()
        {
            Paths.EnsureDirs();
            FileStream me = JobRun.TryLock("_runner");
            if (me == null) return 0;   // another runner is already active

            Note("runner started");
            Dictionary<string, Worker> workers = new Dictionary<string, Worker>();
            while (true)
            {
                try
                {
                    StoreData d = Store.Load();
                    Dictionary<string, Job> want = new Dictionary<string, Job>();
                    foreach (Job j in d.Jobs) if (Sched.On(j)) want[j.Id] = j;

                    foreach (string id in new List<string>(workers.Keys))
                    {
                        Job nj;
                        if (!want.TryGetValue(id, out nj) || nj.Signature() != workers[id].Sig)
                        {
                            workers[id].Halt();
                            workers.Remove(id);
                            Note("stopped " + id);
                        }
                    }
                    foreach (KeyValuePair<string, Job> kv in want)
                    {
                        if (workers.ContainsKey(kv.Key)) continue;
                        Worker w = new Worker();
                        w.Job = kv.Value;
                        w.Sig = kv.Value.Signature();
                        workers[kv.Key] = w;
                        w.Start();
                        Note("started " + kv.Value.Name);
                    }
                }
                catch (Exception ex) { Note("config error: " + ex.Message); }
                Thread.Sleep(5000);
            }
        }
    }
}
