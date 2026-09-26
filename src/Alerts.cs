using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web.Script.Serialization;

namespace RsyncGui
{
    public class AlertSettings
    {
        public bool Enabled;
        public bool OnFail = true;              // a scheduled job fails, after its retries
        public bool OnRecover = true;           // a job that failed works again
        public int StaleHours = 48;             // no success for this long (0 = off)

        public bool EmailOn;
        public string SmtpHost = "";
        public int SmtpPort = 587;
        public bool SmtpTls = true;             // STARTTLS
        public string SmtpUser = "";
        public string SmtpPass = "";            // plain in memory, DPAPI protected on disk
        public string From = "";
        public string To = "";                  // comma separated

        public bool HookOn;
        public string HookUrl = "";             // plain in memory, DPAPI protected on disk
        public string HookFormat = "slack";     // slack (also Teams connectors), teams (Workflows card), discord, json

        public bool EventLogOn = true;
    }

    // What has already been reported for one job, so the same failure is not reported again and again.
    class AlertState
    {
        public bool Failing, Notified;
        public DateTime LastAlert = DateTime.MinValue, StaleAlert = DateTime.MinValue;

        public static AlertState Load(string id)
        {
            AlertState s = new AlertState();
            try
            {
                string f = Path.Combine(Paths.StateDir, id + ".alert");
                if (!File.Exists(f)) return s;
                foreach (string line in File.ReadAllLines(f))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    DateTime t;
                    if (k == "failing") s.Failing = v == "1";
                    else if (k == "notified") s.Notified = v == "1";
                    else if (k == "lastAlert" && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) s.LastAlert = t;
                    else if (k == "staleAlert" && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) s.StaleAlert = t;
                }
            }
            catch { }
            return s;
        }

        public void Save(string id)
        {
            try
            {
                File.WriteAllText(Path.Combine(Paths.StateDir, id + ".alert"),
                    "failing=" + (Failing ? "1" : "0") + Environment.NewLine +
                    "notified=" + (Notified ? "1" : "0") + Environment.NewLine +
                    "lastAlert=" + LastAlert.ToString("s", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "staleAlert=" + StaleAlert.ToString("s", CultureInfo.InvariantCulture));
            }
            catch { }
        }
    }

    public static class Alerts
    {
        public static readonly string[] HookFormats = { "slack", "teams", "discord", "json" };
        public static readonly string[] HookFormatLabels =
        {
            "Slack, or a Teams incoming webhook connector",
            "Microsoft Teams (Workflows webhook)",
            "Discord",
            "Plain JSON (for your own tool)"
        };

        // The runner sets this so problems while sending land in its log.
        public static Action<string> LogSink;

        static string SettingsFile { get { return Path.Combine(Paths.DataDir, "alerts.json"); } }

        static void Say(string s) { Action<string> sink = LogSink; if (sink != null) sink(s); }

        static string Unprotect(string v) { return (v ?? "").StartsWith("dpapi:") ? Secret.Unprotect(v) : (v ?? ""); }

        public static AlertSettings Load()
        {
            AlertSettings s = new AlertSettings();
            try
            {
                if (File.Exists(SettingsFile))
                {
                    s = new JavaScriptSerializer().Deserialize<AlertSettings>(File.ReadAllText(SettingsFile, Encoding.UTF8));
                    s.SmtpPass = Unprotect(s.SmtpPass);
                    s.HookUrl = Unprotect(s.HookUrl);
                }
            }
            catch { s = new AlertSettings(); }
            return s;
        }

        public static void Save(AlertSettings s)
        {
            Paths.EnsureDirs();
            AlertSettings c = new AlertSettings();
            foreach (System.Reflection.FieldInfo f in typeof(AlertSettings).GetFields()) f.SetValue(c, f.GetValue(s));
            c.SmtpPass = Secret.Protect(s.SmtpPass);
            c.HookUrl = Secret.Protect(s.HookUrl);
            string tmp = SettingsFile + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(c), new UTF8Encoding(false));
            if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
            File.Move(tmp, SettingsFile);
        }

        public static bool AnyChannel(AlertSettings s)
        {
            return (s.EmailOn && s.SmtpHost.Trim().Length > 0 && s.To.Trim().Length > 0) || (s.HookOn && s.HookUrl.Trim().Length > 0) || s.EventLogOn;
        }

        // ---------------------------------------------------------------- when to alert

        static bool FreshAlertOn(Job j)
        {
            try
            {
                foreach (Job x in Store.Load().Jobs) if (x.Id == j.Id) return x.AlertOn;
            }
            catch { }
            return j.AlertOn;
        }

        static string LogTail(string id, int lines)
        {
            try
            {
                string p = Paths.LogFile(id);
                if (!File.Exists(p)) return "";
                string[] all;
                using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, fs.Length - 12000);
                    fs.Seek(start, SeekOrigin.Begin);
                    byte[] buf = new byte[fs.Length - start];
                    int n = fs.Read(buf, 0, buf.Length);
                    all = Encoding.UTF8.GetString(buf, 0, n).Replace("\r", "").Split('\n');
                }
                List<string> keep = new List<string>();
                foreach (string l in all) if (l.Trim().Length > 0 && !l.StartsWith("> ")) keep.Add(l);
                int from = Math.Max(0, keep.Count - lines);
                string t = string.Join("\n", keep.GetRange(from, keep.Count - from).ToArray());
                return t.Length > 1500 ? t.Substring(t.Length - 1500) : t;
            }
            catch { return ""; }
        }

        static string Describe(Job j)
        {
            string kind = j.Kind == "cloud" ? "cloud (rclone)" : (j.Kind == "daemon" ? "server" : "files (rsync)");
            return "Job: " + j.Name + " (" + kind + ")\nSchedule: " + Sched.Describe(j) + "\nMachine: " + Environment.MachineName;
        }

        // Called by the background runner when a scheduled run has its final result (after any retries).
        public static void JobFinished(Job j, int code, int errors)
        {
            try
            {
                AlertSettings s = Load();
                if (!s.Enabled || !FreshAlertOn(j)) return;
                AlertState st = AlertState.Load(j.Id);
                bool ok = JobRun.IsSuccess(code, j.Kind);
                string result = "Result: " + (ok ? "OK" : JobRun.ExitText(code, j.Kind) + " (exit " + code + ")") + (errors > 0 ? ", " + errors + " errors" : "") +
                                "\nTime: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                if (ok)
                {
                    bool wasNotified = st.Notified;
                    st.Failing = false; st.Notified = false; st.StaleAlert = DateTime.MinValue;
                    st.Save(j.Id);
                    if (wasNotified && s.OnRecover)
                        Send(s, "recovered", j, "[Rsync GUI] OK again: " + j.Name, Describe(j) + "\n" + result + "\nThe job works again.");
                    return;
                }
                if (!s.OnFail) return;
                DateTime now = DateTime.Now;
                if (!st.Failing || !st.Notified || (now - st.LastAlert).TotalHours >= 24)
                {
                    st.LastAlert = now; st.Notified = true;
                    string tail = LogTail(j.Id, 14);
                    Send(s, "failed", j, "[Rsync GUI] FAILED: " + j.Name,
                        Describe(j) + "\n" + result + "\nFrom: " + j.Sources.Replace("\r", "").Replace("\n", ", ") + "\nTo: " + j.Dest +
                        (tail.Length > 0 ? "\n\nLast lines of the log:\n" + tail : "") +
                        "\n\nYou get one alert per failure, then a reminder every 24 hours while it keeps failing.");
                }
                st.Failing = true;
                st.Save(j.Id);
            }
            catch (Exception ex) { Say("alert error: " + ex.Message); }
        }

        // Called by the runner every few minutes. A job that has not succeeded for too long is reported once a day.
        public static void CheckStale(IEnumerable<Job> jobs)
        {
            try
            {
                AlertSettings s = Load();
                if (!s.Enabled || !s.OnFail || s.StaleHours <= 0) return;
                foreach (Job j in jobs)
                {
                    if (!Sched.On(j) || j.Kind == "daemon" || !j.AlertOn) continue;
                    TimeSpan? period = Sched.Period(j);
                    if (period == null) continue;
                    DateTime? ok = JobRun.LastOk(j.Id);
                    if (ok == null) continue;
                    double limit = Math.Max(s.StaleHours, period.Value.TotalHours * 1.5);   // never sooner than a schedule allows
                    double age = (DateTime.Now - ok.Value).TotalHours;
                    if (age < limit) continue;
                    AlertState st = AlertState.Load(j.Id);
                    if ((DateTime.Now - st.StaleAlert).TotalHours < 24) continue;
                    st.StaleAlert = DateTime.Now; st.Notified = true;
                    st.Save(j.Id);
                    Send(s, "stale", j, "[Rsync GUI] No success for " + (int)age + " hours: " + j.Name,
                        Describe(j) + "\nLast success: " + ok.Value.ToString("yyyy-MM-dd HH:mm") + " (" + (int)age + " hours ago)\n" +
                        "The job has not finished a successful run for longer than " + (int)limit + " hours.\nYou get this reminder once a day until it succeeds.");
                }
            }
            catch (Exception ex) { Say("alert error: " + ex.Message); }
        }

        // ---------------------------------------------------------------- sending

        static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 3) + "..."; }

        static List<string> Send(AlertSettings s, string kind, Job j, string subject, string body)
        {
            List<string> results = new List<string>();
            string jobName = j == null ? "" : j.Name;
            if (s.EmailOn && s.SmtpHost.Trim().Length > 0 && s.To.Trim().Length > 0)
            {
                try { SendEmail(s, subject, body); results.Add("Email: sent"); }
                catch (Exception ex) { results.Add("Email: FAILED - " + ex.Message); Say("alert email failed: " + ex.Message); }
            }
            if (s.HookOn && s.HookUrl.Trim().Length > 0)
            {
                try { SendHook(s, kind, jobName, subject, body); results.Add("Web hook: sent"); }
                catch (Exception ex) { results.Add("Web hook: FAILED - " + ex.Message); Say("alert web hook failed: " + ex.Message); }
            }
            if (s.EventLogOn)
            {
                try { WriteEvent(kind, subject + "\n\n" + body); results.Add("Windows Event Log: written"); }
                catch (Exception ex) { results.Add("Windows Event Log: FAILED - " + ex.Message); Say("alert event log failed: " + ex.Message); }
            }
            return results;
        }

        // For the Send test alert button. Ignores the "Send alerts" master switch.
        public static List<string> SendTest(AlertSettings s)
        {
            Job j = new Job(); j.Name = "Test job"; j.Kind = "cloud"; j.SchedMode = "daily";
            List<string> r = Send(s, "test", j, "[Rsync GUI] Test alert",
                "This is a test alert from " + Environment.MachineName + " at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\nIf you can read this, the channel works.");
            if (r.Count == 0) r.Add("Nothing to test. Tick a channel and fill in its settings.");
            return r;
        }

        static void SendEmail(AlertSettings s, string subject, string body)
        {
            using (SmtpClient c = new SmtpClient(s.SmtpHost.Trim(), s.SmtpPort))
            {
                c.EnableSsl = s.SmtpTls;
                c.Timeout = 25000;
                c.DeliveryMethod = SmtpDeliveryMethod.Network;
                if (s.SmtpUser.Trim().Length > 0) c.Credentials = new NetworkCredential(s.SmtpUser.Trim(), s.SmtpPass);
                string from = s.From.Trim().Length > 0 ? s.From.Trim() : (s.SmtpUser.IndexOf('@') > 0 ? s.SmtpUser.Trim() : "rsyncgui@localhost");
                using (MailMessage m = new MailMessage())
                {
                    m.From = new MailAddress(from);
                    foreach (string to in s.To.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) m.To.Add(to.Trim());
                    m.Subject = subject;
                    m.SubjectEncoding = Encoding.UTF8; m.BodyEncoding = Encoding.UTF8;
                    m.Body = body.Replace("\n", "\r\n");
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;    // TLS 1.2
                    c.Send(m);
                }
            }
        }

        static string HookJson(string format, string kind, string jobName, string subject, string body)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            string text = subject + "\n\n" + body;
            switch (format)
            {
                case "discord":
                    return js.Serialize(new Dictionary<string, object> { { "content", Trunc(text, 1900) } });
                case "teams":
                    Dictionary<string, object> card = new Dictionary<string, object>
                    {
                        { "$schema", "http://adaptivecards.io/schemas/adaptive-card.json" },
                        { "type", "AdaptiveCard" },
                        { "version", "1.4" },
                        { "body", new object[]
                            {
                                new Dictionary<string, object> { { "type", "TextBlock" }, { "text", subject }, { "weight", "Bolder" }, { "size", "Medium" }, { "wrap", true } },
                                new Dictionary<string, object> { { "type", "TextBlock" }, { "text", body.Replace("\n", "\n\n") }, { "wrap", true } }
                            } }
                    };
                    Dictionary<string, object> att = new Dictionary<string, object> { { "contentType", "application/vnd.microsoft.card.adaptive" }, { "content", card } };
                    return js.Serialize(new Dictionary<string, object> { { "type", "message" }, { "attachments", new object[] { att } } });
                case "json":
                    return js.Serialize(new Dictionary<string, object>
                    {
                        { "app", "RsyncGui" }, { "event", kind }, { "job", jobName }, { "host", Environment.MachineName },
                        { "subject", subject }, { "message", body }, { "time", DateTime.Now.ToString("s", CultureInfo.InvariantCulture) }
                    });
                default:    // slack, and Teams incoming webhook connectors
                    return js.Serialize(new Dictionary<string, object> { { "text", text } });
            }
        }

        static void SendHook(AlertSettings s, string kind, string jobName, string subject, string body)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;    // TLS 1.2
            byte[] data = Encoding.UTF8.GetBytes(HookJson(s.HookFormat, kind, jobName, subject, body));
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(s.HookUrl.Trim());
            rq.Method = "POST";
            rq.ContentType = "application/json; charset=utf-8";
            rq.ContentLength = data.Length;
            rq.Timeout = 25000; rq.ReadWriteTimeout = 25000;
            using (Stream st = rq.GetRequestStream()) st.Write(data, 0, data.Length);
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse()) { int code = (int)rs.StatusCode; if (code < 200 || code > 299) throw new Exception("HTTP " + code); }
        }

        static void WriteEvent(string kind, string message)
        {
            const string source = "RsyncGui";
            if (!EventLog.SourceExists(source)) EventLog.CreateEventSource(source, "Application");
            EventLogEntryType type = kind == "failed" ? EventLogEntryType.Error : (kind == "stale" ? EventLogEntryType.Warning : EventLogEntryType.Information);
            int id = kind == "failed" ? 1001 : (kind == "recovered" ? 1002 : (kind == "stale" ? 1003 : 1000));
            EventLog.WriteEntry(source, Trunc(message, 30000), type, id);
        }
    }
}
