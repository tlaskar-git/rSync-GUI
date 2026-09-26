using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RsyncGui
{
    // What a running job is doing right now. The job process writes it about once a second and the
    // window (or anything else) reads it. It is deleted when the run ends.
    public class ProgressInfo
    {
        public DateTime Started = DateTime.Now, Updated = DateTime.Now;
        public int Pct = -1;                       // -1 = unknown
        public string Phase = "", Bytes = "", Speed = "", Eta = "", Files = "", Current = "";

        public void Save(string id)
        {
            Updated = DateTime.Now;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("started=" + Started.ToString("s"));
            sb.AppendLine("updated=" + Updated.ToString("s"));
            sb.AppendLine("pct=" + Pct);
            sb.AppendLine("phase=" + Clean(Phase));
            sb.AppendLine("bytes=" + Clean(Bytes));
            sb.AppendLine("speed=" + Clean(Speed));
            sb.AppendLine("eta=" + Clean(Eta));
            sb.AppendLine("files=" + Clean(Files));
            sb.AppendLine("current=" + Clean(Current));
            try
            {
                string tmp = Paths.ProgressFile(id) + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Copy(tmp, Paths.ProgressFile(id), true);
                File.Delete(tmp);
            }
            catch { }
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        public static void Clear(string id)
        {
            try { File.Delete(Paths.ProgressFile(id)); } catch { }
        }

        public static ProgressInfo Load(string id)
        {
            try
            {
                if (!File.Exists(Paths.ProgressFile(id))) return null;
                ProgressInfo p = new ProgressInfo();
                foreach (string line in File.ReadAllLines(Paths.ProgressFile(id), Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "started": DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out p.Started); break;
                        case "updated": DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out p.Updated); break;
                        case "pct": int.TryParse(v, out p.Pct); break;
                        case "phase": p.Phase = v; break;
                        case "bytes": p.Bytes = v; break;
                        case "speed": p.Speed = v; break;
                        case "eta": p.Eta = v; break;
                        case "files": p.Files = v; break;
                        case "current": p.Current = v; break;
                    }
                }
                return p;
            }
            catch { return null; }
        }
    }

    // Reads the output of rclone and rsync while they run. Feed() returns true for lines that are only
    // progress noise, so the caller keeps them out of the log.
    public class ProgressParser
    {
        readonly string id;
        readonly bool cloud;
        public readonly ProgressInfo P = new ProgressInfo();
        public List<string> LastBlock;             // last full rclone stats block, written to the log at the end
        DateTime lastSave = DateTime.MinValue;
        bool inBlock;
        List<string> block = new List<string>();
        List<string> xfer = new List<string>();
        string listed = "";

        static readonly Regex ReHeader = new Regex(@"^\d{4}/\d\d/\d\d \d\d:\d\d:\d\d (?:NOTICE|INFO)\s*: ?$", RegexOptions.Compiled);
        static readonly Regex ReBytes = new Regex(@"^Transferred:\s+(?<d>[\d.,]+\s*\S*)\s*/\s*(?<t>[\d.,]+\s*\S*),\s*(?<p>\d+|-)%?,\s*(?<s>[\d.,]+\s*\S+/s),\s*ETA\s*(?<e>\S+)", RegexOptions.Compiled);
        static readonly Regex ReFiles = new Regex(@"^Transferred:\s+(?<d>\d+)\s*/\s*(?<t>\d+),\s*(?<p>\d+|-)", RegexOptions.Compiled);
        static readonly Regex ReChecks = new Regex(@"^Checks:.*Listed\s+(?<l>\d+)", RegexOptions.Compiled);
        static readonly Regex ReXfer = new Regex(@"^\s*\*\s+(?<n>.+?):\s+(?<p>\d+)%", RegexOptions.Compiled);
        static readonly Regex ReCloudFile = new Regex(@"INFO\s*:\s+(?<n>.+?):\s+(?:Copied|Moved|Updated|Deleted|Multi-thread Copied)", RegexOptions.Compiled);
        static readonly Regex ReRsync = new Regex(@"^\s*(?<b>[\d.,]+[KMGTP]?)\s+(?<p>\d+)%\s+(?<s>[\d.,]+\S*/s)\s+(?<e>\S+)(?:\s+\(xfr#(?<x>\d+),\s*(?:ir|to)-chk=(?<r>\d+)/(?<t>\d+)\))?", RegexOptions.Compiled);

        static readonly string[] BlockStarts = { "Transferred:", "Checks:", "Deleted:", "Renamed:", "Errors:", "Elapsed time:", "Transferring:", "Checking:", "Renaming:", "Server Side" };

        public ProgressParser(string jobId, bool isCloud)
        {
            id = jobId; cloud = isCloud;
            P.Started = DateTime.Now;
            P.Phase = "Starting";
        }

        // The last complete stats block, written to the log when the run ends.
        public List<string> Summary { get { return block.Count > 0 ? block : LastBlock; } }

        public void Save(bool force)
        {
            if (!force && (DateTime.Now - lastSave).TotalSeconds < 1) return;
            lastSave = DateTime.Now;
            P.Save(id);
        }

        static string Trim2(string s) { return Regex.Replace(s.Trim(), @"\s+", " "); }

        void ParseBlockLine(string line)
        {
            Match m = ReBytes.Match(line);
            if (m.Success)
            {
                P.Bytes = Trim2(m.Groups["d"].Value) + " of " + Trim2(m.Groups["t"].Value);
                int pct;
                P.Pct = int.TryParse(m.Groups["p"].Value, out pct) ? pct : -1;
                P.Speed = Trim2(m.Groups["s"].Value);
                P.Eta = m.Groups["e"].Value == "-" ? "" : m.Groups["e"].Value;
                string tot = m.Groups["t"].Value.Trim();
                P.Phase = (P.Pct < 0 || tot.StartsWith("0 ") || tot == "0") ? "Scanning" : "Transferring";
                return;
            }
            m = ReFiles.Match(line);
            if (m.Success) { P.Files = m.Groups["d"].Value + " of " + m.Groups["t"].Value + " files"; return; }
            m = ReChecks.Match(line);
            if (m.Success) { listed = m.Groups["l"].Value; if (P.Phase == "Scanning") P.Files = "listed " + listed; return; }
            if (line.StartsWith("Transferring:")) { xfer.Clear(); return; }
            m = ReXfer.Match(line);
            if (m.Success)
            {
                xfer.Add(m.Groups["n"].Value.Trim() + " " + m.Groups["p"].Value + "%");
                P.Current = string.Join("   |   ", xfer.GetRange(0, Math.Min(3, xfer.Count)).ToArray()) + (xfer.Count > 3 ? "   (+" + (xfer.Count - 3) + " more)" : "");
            }
        }

        static bool IsBlockLine(string line)
        {
            if (line.Length == 0) return true;
            foreach (string b in BlockStarts) if (line.StartsWith(b)) return true;
            return ReXfer.IsMatch(line);
        }

        static readonly string[] RsyncSkip =
        {
            "sending ", "sent ", "total size", "building file list", "receiving ", "Number of", "Total ", "File list", "Literal",
            "Matched", "deleting ", "rsync", "created directory", "skipping", "Warning:", "ssh:", "Permanently added"
        };

        // Returns true when the line is progress noise that should not go to the log.
        public bool Feed(string line)
        {
            if (cloud)
            {
                if (ReHeader.IsMatch(line))
                {
                    if (block.Count > 0) LastBlock = block;
                    block = new List<string>();
                    inBlock = true;
                    Save(false);
                    return true;
                }
                if (inBlock)
                {
                    if (IsBlockLine(line))
                    {
                        block.Add(line);
                        ParseBlockLine(line);
                        Save(line.Length == 0);          // an empty line ends the block: publish it now
                        return true;
                    }
                    inBlock = false;
                }
                Match cf = ReCloudFile.Match(line);
                if (cf.Success) { P.Current = cf.Groups["n"].Value; Save(false); }
                return false;
            }

            if (line.Trim().Length == 0) return true;        // rsync progress leaves empty lines behind
            Match m = ReRsync.Match(line);
            if (m.Success && line.IndexOf('%') > 0)
            {
                int pct;
                P.Pct = int.TryParse(m.Groups["p"].Value, out pct) ? pct : -1;
                P.Bytes = m.Groups["b"].Value;
                P.Speed = m.Groups["s"].Value;
                P.Eta = m.Groups["e"].Value;
                P.Phase = "Transferring";
                if (m.Groups["x"].Success)
                    P.Files = m.Groups["x"].Value + " files done, " + m.Groups["r"].Value + " left of " + m.Groups["t"].Value;
                Save(false);
                return true;
            }
            string t = line.Trim();
            if (t.Length > 0 && t.Length < 400 && !t.EndsWith("/"))
            {
                bool skip = false;
                foreach (string k in RsyncSkip) if (t.StartsWith(k)) { skip = true; break; }
                if (!skip) { P.Current = t; P.Phase = P.Pct >= 0 ? "Transferring" : "Working"; Save(false); }
            }
            return false;
        }
    }
}
