using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace RsyncGui
{
    // One finished run of a job.
    public class RunRecord
    {
        public string Start = "", End = "", Result = "", Data = "", Files = "", Trigger = "manual", Kind = "", Mode = "", Version = "";
        public int Seconds, Code, Errors, Links;
        public bool Dry;

        public DateTime StartDate()
        {
            DateTime t;
            return DateTime.TryParse(Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.MinValue;
        }

        public bool Succeeded() { return !Dry && Code == 0 || (!Dry && Kind == "sync" && Code == 24); }
    }

    // The last runs of each job, one JSON line per run in state\<id>.history.
    public static class History
    {
        public const int Keep = 100;

        static JavaScriptSerializer Ser() { return new JavaScriptSerializer(); }

        static List<string> ReadLines(string file)
        {
            List<string> l = new List<string>();
            if (!File.Exists(file)) return l;
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null) if (line.Trim().Length > 0) l.Add(line);
            }
            return l;
        }

        public static void Add(string id, RunRecord r)
        {
            try
            {
                string f = Paths.HistoryFile(id);
                List<string> lines = ReadLines(f);
                lines.Add(Ser().Serialize(r));
                if (lines.Count > Keep) lines.RemoveRange(0, lines.Count - Keep);
                File.WriteAllLines(f, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        // Newest first.
        public static List<RunRecord> Load(string id)
        {
            List<RunRecord> r = new List<RunRecord>();
            try
            {
                foreach (string line in ReadLines(Paths.HistoryFile(id)))
                {
                    try { r.Add(Ser().Deserialize<RunRecord>(line)); } catch { }
                }
            }
            catch { }
            r.Reverse();
            return r;
        }

        public static void Clear(string id)
        {
            try { File.Delete(Paths.HistoryFile(id)); } catch { }
        }

        public static string Duration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds >= 3600) return (seconds / 3600) + " h " + ((seconds % 3600) / 60) + " m";
            if (seconds >= 60) return (seconds / 60) + " m " + (seconds % 60) + " s";
            return seconds + " s";
        }

        // "125,829,123" -> "120.0 MiB". Anything that is not a plain number is returned as it is.
        public static string HumanBytes(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            double v;
            if (!double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return s;
            string[] u = { "B", "KiB", "MiB", "GiB", "TiB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return i == 0 ? ((long)v) + " B" : v.ToString("0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new char[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static string ToCsv(List<RunRecord> runs)
        {
            StringBuilder sb = new StringBuilder("Started,Ended,Seconds,How,Result,Exit code,Data,Files,Errors,Dry run,Type,Mode,Version\r\n");
            foreach (RunRecord r in runs)
                sb.Append(string.Join(",", new string[]
                {
                    Csv(r.Start), Csv(r.End), r.Seconds.ToString(), Csv(r.Trigger), Csv(r.Result), r.Code.ToString(), Csv(r.Data), Csv(r.Files),
                    r.Errors.ToString(), r.Dry ? "yes" : "no", Csv(r.Kind), Csv(r.Mode), Csv(r.Version)
                })).Append("\r\n");
            return sb.ToString();
        }

        // One line above the list: how the job has been doing.
        public static string Summary(List<RunRecord> runs)
        {
            if (runs.Count == 0) return "No runs recorded yet. Every run of this job appears here.";
            int ok = 0, bad = 0, dry = 0; long secs = 0; DateTime? lastOk = null;
            foreach (RunRecord r in runs)
            {
                if (r.Dry) { dry++; continue; }
                if (r.Succeeded()) { ok++; secs += r.Seconds; if (lastOk == null || r.StartDate() > lastOk.Value) lastOk = r.StartDate(); }
                else if (r.Code != JobRun.Killed) bad++;
            }
            string s = "Last " + runs.Count + " runs:  " + ok + " OK,  " + bad + " failed" + (dry > 0 ? ",  " + dry + " dry" : "");
            if (ok > 0) s += ".   Average time of an OK run: " + Duration((int)(secs / ok));
            if (lastOk != null) s += ".   Last success: " + lastOk.Value.ToString("yyyy-MM-dd HH:mm");
            return s + ".";
        }
    }

    // Removes what a deleted job leaves behind.
    public static class Housekeeping
    {
        public static void RemoveJobFiles(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            string[] files =
            {
                Paths.LogFile(id), Paths.LogFile(id) + ".1", Paths.StateFile(id), Paths.OkFile(id), Paths.NextFile(id), Paths.ProgressFile(id),
                Paths.HistoryFile(id), Path.Combine(Paths.StateDir, id + ".alert"), Paths.DaemonConf(id), Paths.StopFile(id), Paths.LockFile(id)
            };
            foreach (string f in files) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        }
    }
}
