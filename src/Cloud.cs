using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RsyncGui
{
    public class REx { public string Value = "", Help = "", Provider = ""; }

    public class ROpt
    {
        public string Name = "", Help = "", Default = "", Provider = "", Type = "";
        public bool Required, IsPassword, Advanced, Exclusive;
        public int Hide;
        public List<REx> Examples = new List<REx>();
    }

    public class Prov
    {
        public string Prefix = "", Desc = "";
        public bool Hidden, OAuth;
        public List<ROpt> Opts = new List<ROpt>();
        public override string ToString() { return Desc + "  (" + Prefix + ")"; }
    }

    public class Step
    {
        public string State = "", Error = "";
        public ROpt Option;
    }

    // Thin wrapper around rclone.exe. All accounts live in <data folder>\rclone.conf so the GUI and
    // the boot runner (SYSTEM) use the same sign-ins.
    public static class Rclone
    {
        public class Res
        {
            public int Code;
            public string Out = "", Err = "";
            public bool TimedOut, Canceled;
        }

        public static string Exe { get { return Path.Combine(Paths.BinDir, "rclone.exe"); } }
        public static string Conf { get { return Path.Combine(Paths.DataDir, "rclone.conf"); } }
        public static bool Present { get { return File.Exists(Exe); } }

        static readonly string[] Popular =
        {
            "onedrive", "drive", "dropbox", "box", "s3", "azureblob", "b2", "gcs", "sftp", "ftp", "webdav", "smb",
            "pcloud", "mega", "protondrive", "iclouddrive", "jottacloud", "koofr", "hidrive", "gphotos", "local", "crypt"
        };

        public static void Kill(Process p)
        {
            try
            {
                if (p.HasExited) return;
                ProcessStartInfo k = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                k.CreateNoWindow = true; k.UseShellExecute = false;
                Process kp = Process.Start(k);
                kp.WaitForExit(10000);
            }
            catch { }
        }

        public static Res Run(string args, int timeoutMs) { return Run(args, timeoutMs, null); }

        public static Res Run(string args, int timeoutMs, Action<Process> started)
        {
            Paths.EnsureDirs();
            Res r = new Res();
            ProcessStartInfo psi = new ProcessStartInfo(Exe, "--config " + Cmd.Quote(Conf) + " " + args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Paths.DataDir;
            StringBuilder o = new StringBuilder(), e = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs a) { if (a.Data != null) lock (o) o.AppendLine(a.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a) { if (a.Data != null) lock (e) e.AppendLine(a.Data); };
                p.Start();
                if (started != null) started(p);
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; Kill(p); }
                p.WaitForExit();
                r.Code = r.TimedOut ? -1 : p.ExitCode;
            }
            r.Out = o.ToString(); r.Err = e.ToString();
            return r;
        }

        // Last meaningful error line from rclone's stderr.
        public static string ErrorText(Res r)
        {
            if (r.TimedOut) return "Timed out.";
            string t = (r.Err ?? "").Trim();
            if (t.Length == 0) t = (r.Out ?? "").Trim();
            string[] lines = t.Replace("\r", "").Split('\n');
            List<string> keep = new List<string>();
            foreach (string l in lines) if (l.Trim().Length > 0 && l.IndexOf("NOTICE: Config file") < 0) keep.Add(l.Trim());
            if (keep.Count == 0) return "Exit code " + r.Code;
            int from = Math.Max(0, keep.Count - 4);
            return string.Join("\r\n", keep.GetRange(from, keep.Count - from).ToArray());
        }

        public static List<string> Remotes()
        {
            List<string> l = new List<string>();
            if (!Present) return l;
            Res r = Run("listremotes", 30000);
            foreach (string x in r.Out.Replace("\r", "").Split('\n')) if (x.Trim().EndsWith(":")) l.Add(x.Trim());
            return l;
        }

        public static Dictionary<string, string> RemoteTypes()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (!Present) return d;
            Res r = Run("config dump", 30000);
            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(r.Out) as Dictionary<string, object>;
                if (root != null)
                    foreach (KeyValuePair<string, object> kv in root)
                    {
                        Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                        d[kv.Key + ":"] = v != null && v.ContainsKey("type") ? Str(v["type"]) : "";
                    }
            }
            catch { }
            return d;
        }

        static string Str(object o)
        {
            if (o == null) return "";
            if (o is bool) return ((bool)o) ? "true" : "false";
            return Convert.ToString(o);
        }

        static ROpt ParseOpt(Dictionary<string, object> d)
        {
            ROpt o = new ROpt();
            object v;
            if (d.TryGetValue("Name", out v)) o.Name = Str(v);
            if (d.TryGetValue("Help", out v)) o.Help = Str(v);
            if (d.TryGetValue("Default", out v)) o.Default = Str(v);
            if (d.TryGetValue("Provider", out v)) o.Provider = Str(v);
            if (d.TryGetValue("Type", out v)) o.Type = Str(v);
            if (d.TryGetValue("Required", out v)) o.Required = v is bool && (bool)v;
            if (d.TryGetValue("IsPassword", out v)) o.IsPassword = v is bool && (bool)v;
            if (d.TryGetValue("Advanced", out v)) o.Advanced = v is bool && (bool)v;
            if (d.TryGetValue("Exclusive", out v)) o.Exclusive = v is bool && (bool)v;
            if (d.TryGetValue("Hide", out v) && v != null) { try { o.Hide = Convert.ToInt32(v); } catch { } }
            if (d.TryGetValue("Examples", out v) && v is IEnumerable && !(v is string))
                foreach (object eo in (IEnumerable)v)
                {
                    Dictionary<string, object> ed = eo as Dictionary<string, object>;
                    if (ed == null) continue;
                    REx x = new REx();
                    object ev;
                    if (ed.TryGetValue("Value", out ev)) x.Value = Str(ev);
                    if (ed.TryGetValue("Help", out ev)) x.Help = Str(ev);
                    if (ed.TryGetValue("Provider", out ev)) x.Provider = Str(ev);
                    o.Examples.Add(x);
                }
            return o;
        }

        static List<Prov> provCache;

        public static List<Prov> Providers()
        {
            if (provCache != null) return provCache;
            List<Prov> list = new List<Prov>();
            if (!Present) return list;
            Res r = Run("config providers", 60000);
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            object root = s.DeserializeObject(r.Out);
            foreach (object po in (IEnumerable)root)
            {
                Dictionary<string, object> pd = po as Dictionary<string, object>;
                if (pd == null) continue;
                Prov p = new Prov();
                object v;
                if (pd.TryGetValue("Prefix", out v)) p.Prefix = Str(v);
                if (pd.TryGetValue("Description", out v)) p.Desc = Str(v);
                if (pd.TryGetValue("Hide", out v)) p.Hidden = v is bool && (bool)v;
                if (pd.TryGetValue("Options", out v) && v is IEnumerable)
                    foreach (object oo in (IEnumerable)v)
                    {
                        Dictionary<string, object> od = oo as Dictionary<string, object>;
                        if (od != null) p.Opts.Add(ParseOpt(od));
                    }
                foreach (ROpt o in p.Opts) if (o.Name == "token") p.OAuth = true;
                if (!p.Hidden && p.Prefix.Length > 0) list.Add(p);
            }
            list.Sort(delegate(Prov a, Prov b)
            {
                int ia = Array.IndexOf(Popular, a.Prefix), ib = Array.IndexOf(Popular, b.Prefix);
                if (ia < 0) ia = 1000; if (ib < 0) ib = 1000;
                if (ia != ib) return ia.CompareTo(ib);
                return string.Compare(a.Desc, b.Desc, StringComparison.OrdinalIgnoreCase);
            });
            provCache = list;
            return list;
        }

        public static Step ParseStep(string json)
        {
            Step st = new Step();
            int i = json.IndexOf('{');
            if (i < 0) { st.Error = "Unexpected reply from rclone."; return st; }
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json.Substring(i)) as Dictionary<string, object>;
            if (d == null) { st.Error = "Unexpected reply from rclone."; return st; }
            object v;
            if (d.TryGetValue("State", out v)) st.State = Str(v);
            if (d.TryGetValue("Error", out v)) st.Error = Str(v);
            if (d.TryGetValue("Option", out v) && v is Dictionary<string, object>) st.Option = ParseOpt((Dictionary<string, object>)v);
            return st;
        }

        public static Res CreateRemote(string name, string type, List<string> keyValues, Action<Process> started)
        {
            StringBuilder a = new StringBuilder("config create " + Cmd.Quote(name) + " " + Cmd.Quote(type));
            foreach (string kv in keyValues) a.Append(' ').Append(Cmd.Quote(kv));
            a.Append(" --non-interactive");
            return Run(a.ToString(), 600000, started);
        }

        public static Res Continue(string name, string state, string answer, Action<Process> started)
        {
            return Run("config update " + Cmd.Quote(name) + " --continue --state " + Cmd.Quote(state) + " --result " + Cmd.Quote(answer) + " --non-interactive", 600000, started);
        }

        public static void Delete(string name)
        {
            Run("config delete " + Cmd.Quote(name), 30000);
        }

        // Does the provider option apply for the chosen sub-provider? Values look like "AWS,Ceph" or "!AWS".
        public static bool Applies(string cond, string selected)
        {
            if (string.IsNullOrEmpty(cond)) return true;
            bool neg = cond.StartsWith("!");
            string[] parts = (neg ? cond.Substring(1) : cond).Split(',');
            bool hit = false;
            foreach (string p in parts) if (p.Trim() == selected) hit = true;
            return neg ? !hit : (selected.Length > 0 && hit);
        }
    }

    public static class CloudUi
    {
        static Label Lbl(string t, int x, int y) { Label l = new Label(); l.Text = t; l.AutoSize = true; l.Location = new Point(x, y + 3); return l; }
        static Button Btn(string t, int x, int y, int w) { Button b = new Button(); b.Text = t; b.Location = new Point(x, y); b.Size = new Size(w, 27); b.UseVisualStyleBackColor = true; return b; }

        static Form NewForm(string title, int w, int h)
        {
            Form f = new Form();
            f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(w, h); f.Font = new Font("Segoe UI", 9F);
            f.ShowInTaskbar = false;
            return f;
        }

        // Runs work on a background thread while a small window with a Cancel button is open.
        public static Rclone.Res RunWait(IWin32Window owner, string title, string message, Func<Action<Process>, Rclone.Res> work)
        {
            Form f = NewForm(title, 480, 150);
            f.ControlBox = false;
            Label l = new Label(); l.Text = message; l.Location = new Point(16, 16); l.Size = new Size(448, 74);
            Button cancel = Btn("Cancel", 374, 104, 90);
            f.Controls.Add(l); f.Controls.Add(cancel);
            Process proc = null;
            bool canceled = false;
            Rclone.Res result = null;
            cancel.Click += delegate
            {
                canceled = true;
                Process p = proc;
                if (p != null) Rclone.Kill(p);
            };
            Thread t = new Thread(delegate()
            {
                try { result = work(delegate(Process p) { proc = p; }); }
                catch (Exception ex) { result = new Rclone.Res(); result.Code = -1; result.Err = ex.Message; }
                try { f.BeginInvoke((MethodInvoker)delegate { f.Close(); }); } catch { }
            });
            t.IsBackground = true;
            f.Shown += delegate { t.Start(); };
            f.ShowDialog(owner);
            t.Join(15000);
            if (result == null) { result = new Rclone.Res(); result.Code = -1; }
            result.Canceled = canceled;
            return result;
        }

        // One question from rclone's setup dialogue.
        static bool Ask(IWin32Window owner, ROpt q, out string answer)
        {
            answer = "";
            Form f = NewForm("Account setup", 600, 400);
            TextBox help = new TextBox(); help.Multiline = true; help.ReadOnly = true; help.ScrollBars = ScrollBars.Vertical;
            help.Location = new Point(14, 14); help.Size = new Size(572, 220); help.BackColor = SystemColors.Window;
            help.Text = q.Help.Replace("\r", "").Replace("\n", "\r\n");
            Control input;
            List<REx> ex = q.Examples;
            if (q.Type == "bool")
            {
                ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList;
                cb.Items.Add("Yes"); cb.Items.Add("No");
                cb.SelectedIndex = q.Default == "true" ? 0 : 1;
                input = cb;
            }
            else if (ex.Count > 0)
            {
                ComboBox cb = new ComboBox(); cb.DropDownStyle = q.Exclusive ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
                int sel = -1;
                foreach (REx e in ex)
                {
                    string first = e.Help.Replace("\r", "").Split('\n')[0];
                    cb.Items.Add(new ExItem(e.Value, e.Value.Length == 0 ? first : (first.Length > 0 ? e.Value + "  -  " + first : e.Value)));
                    if (e.Value == q.Default && sel < 0) sel = cb.Items.Count - 1;
                }
                if (sel >= 0) cb.SelectedIndex = sel; else if (cb.Items.Count > 0 && q.Exclusive) cb.SelectedIndex = 0; else cb.Text = q.Default;
                input = cb;
            }
            else
            {
                TextBox tb = new TextBox(); tb.Text = q.Default; tb.UseSystemPasswordChar = q.IsPassword;
                input = tb;
            }
            input.Location = new Point(14, 250); input.Width = 572;
            Label hint = new Label(); hint.Location = new Point(14, 280); hint.AutoSize = true; hint.ForeColor = SystemColors.GrayText;
            hint.Text = q.Name + (q.Required ? "  (required)" : "");
            Button ok = Btn("Next", 396, 356, 90); ok.DialogResult = DialogResult.OK;
            Button cancel = Btn("Cancel", 494, 356, 90); cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { help, input, hint, ok, cancel });
            if (f.ShowDialog(owner) != DialogResult.OK) return false;
            ComboBox c2 = input as ComboBox;
            if (q.Type == "bool") answer = c2.SelectedIndex == 0 ? "true" : "false";
            else if (c2 != null) answer = c2.SelectedItem is ExItem ? ((ExItem)c2.SelectedItem).Value : c2.Text;
            else answer = input.Text;
            return true;
        }

        class ExItem
        {
            public string Value, Text;
            public ExItem(string v, string t) { Value = v; Text = t; }
            public override string ToString() { return Text; }
        }

        // ---------------------------------------------------------------- add account

        class ProviderForm : Form
        {
            public TextBox TName = new TextBox();
            public ComboBox CProv = new ComboBox();
            Label desc = new Label();
            LinkLabel note = new LinkLabel();
            Panel scroll = new Panel();
            Dictionary<string, Control> inputs = new Dictionary<string, Control>();
            Dictionary<string, ROpt> defs = new Dictionary<string, ROpt>();
            bool building;
            ToolTip tip = new ToolTip();
            public string ProviderPrefix = "";

            public ProviderForm(IList<string> existing)
            {
                Text = "Add cloud account"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(640, 640); Font = new Font("Segoe UI", 9F); ShowInTaskbar = false;
                tip.AutoPopDelay = 30000;
                Controls.Add(Lbl("Account name", 14, 14));
                TName.Location = new Point(120, 14); TName.Width = 220;
                Controls.Add(TName);
                Label nh = new Label(); nh.Text = "Letters, digits, - and _. You pick it in jobs as name:"; nh.AutoSize = true; nh.ForeColor = SystemColors.GrayText; nh.Location = new Point(350, 18);
                Controls.Add(nh);
                Controls.Add(Lbl("Storage type", 14, 48));
                CProv.DropDownStyle = ComboBoxStyle.DropDownList; CProv.Location = new Point(120, 48); CProv.Width = 500; CProv.MaxDropDownItems = 20;
                foreach (Prov p in Rclone.Providers()) CProv.Items.Add(p);
                CProv.SelectedIndexChanged += delegate { if (!building) Rebuild(null); };
                Controls.Add(CProv);
                desc.Location = new Point(120, 78); desc.Size = new Size(500, 20); desc.ForeColor = SystemColors.GrayText;
                Controls.Add(desc);
                note.Location = new Point(14, 104); note.Size = new Size(612, 46);
                note.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e) { try { Process.Start((string)e.Link.LinkData); } catch { } };
                Controls.Add(note);
                scroll.Location = new Point(14, 156); scroll.Size = new Size(612, 424); scroll.AutoScroll = true; scroll.BorderStyle = BorderStyle.FixedSingle;
                Controls.Add(scroll);
                Button ok = Btn("Sign in / Create", 400, 596, 130); ok.Click += OnOk;
                Button cancel = Btn("Cancel", 536, 596, 90); cancel.DialogResult = DialogResult.Cancel;
                CancelButton = cancel;
                Controls.Add(ok); Controls.Add(cancel);
                if (CProv.Items.Count > 0) CProv.SelectedIndex = 0;
                existingNames = existing;
            }

            IList<string> existingNames;

            string SelectedSubProvider()
            {
                Control c;
                if (inputs.TryGetValue("provider", out c)) return ValueOf("provider", c);
                return "";
            }

            static string FirstLine(string s)
            {
                string t = (s ?? "").Replace("\r", "").Split('\n')[0].Trim();
                return t.Length > 110 ? t.Substring(0, 107) + "..." : t;
            }

            string ValueOf(string name, Control c)
            {
                ROpt o = defs[name];
                if (c is CheckBox) return ((CheckBox)c).Checked ? "true" : "false";
                ComboBox cb = c as ComboBox;
                if (cb != null) return cb.SelectedItem is ExItem ? ((ExItem)cb.SelectedItem).Value : cb.Text.Trim();
                return c.Text.Trim();
            }

            void Rebuild(Dictionary<string, string> keep)
            {
                Prov p = CProv.SelectedItem as Prov;
                if (p == null) return;
                ProviderPrefix = p.Prefix;
                desc.Text = p.Desc;
                if (keep == null) keep = new Dictionary<string, string>();
                scroll.Controls.Clear(); inputs.Clear(); defs.Clear();
                string sub = keep.ContainsKey("provider") ? keep["provider"] : "";
                // If the provider has a "provider" choice, show only that first, then the matching options.
                int y = 6;
                foreach (ROpt o in p.Opts)
                {
                    if (o.Advanced || o.Hide != 0) continue;
                    if (!Rclone.Applies(o.Provider, sub)) continue;
                    defs[o.Name] = o;
                    Panel row = new Panel(); row.Location = new Point(4, y); row.Size = new Size(580, 52);
                    Label l = new Label(); l.AutoSize = false; l.Location = new Point(0, 0); l.Size = new Size(580, 18);
                    l.Text = o.Name + (o.Required ? " *" : "") + "   " + FirstLine(o.Help);
                    l.AutoEllipsis = true;
                    row.Controls.Add(l);
                    Control input;
                    List<REx> ex = new List<REx>();
                    foreach (REx e in o.Examples) if (Rclone.Applies(e.Provider, sub) || e.Provider.Length == 0) ex.Add(e);
                    if (o.Type == "bool")
                    {
                        CheckBox cb = new CheckBox(); cb.Text = "Yes"; cb.AutoSize = true; cb.Checked = o.Default == "true";
                        input = cb;
                    }
                    else if (ex.Count > 0)
                    {
                        ComboBox cb = new ComboBox(); cb.DropDownStyle = o.Exclusive ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
                        cb.Items.Add(new ExItem("", ""));
                        foreach (REx e in ex)
                        {
                            string first = FirstLine(e.Help);
                            cb.Items.Add(new ExItem(e.Value, first.Length > 0 && e.Value.Length > 0 ? e.Value + "  -  " + first : (e.Value.Length > 0 ? e.Value : first)));
                        }
                        cb.Width = 480; cb.MaxDropDownItems = 15;
                        input = cb;
                    }
                    else
                    {
                        TextBox tb = new TextBox(); tb.UseSystemPasswordChar = o.IsPassword; tb.Width = 480; tb.Text = o.Default;
                        input = tb;
                    }
                    input.Location = new Point(0, 24);
                    tip.SetToolTip(input, o.Help);
                    tip.SetToolTip(l, o.Help);
                    row.Controls.Add(input);
                    scroll.Controls.Add(row);
                    inputs[o.Name] = input;
                    string kv;
                    if (keep.TryGetValue(o.Name, out kv))
                    {
                        if (input is CheckBox) ((CheckBox)input).Checked = kv == "true";
                        else if (input is ComboBox)
                        {
                            ComboBox cb = (ComboBox)input;
                            bool found = false;
                            foreach (object it in cb.Items) if (it is ExItem && ((ExItem)it).Value == kv) { cb.SelectedItem = it; found = true; break; }
                            if (!found && cb.DropDownStyle == ComboBoxStyle.DropDown) cb.Text = kv;
                        }
                        else input.Text = kv;
                    }
                    else if (input is ComboBox && ((ComboBox)input).Items.Count > 0)
                    {
                        ComboBox cb = (ComboBox)input;
                        foreach (object it in cb.Items) if (it is ExItem && ((ExItem)it).Value == o.Default) { cb.SelectedItem = it; break; }
                        if (cb.SelectedIndex < 0 && cb.DropDownStyle == ComboBoxStyle.DropDownList) cb.SelectedIndex = 0;
                    }
                    y += 56;
                    if (o.Name == "provider" && input is ComboBox)
                    {
                        ComboBox pc = (ComboBox)input;
                        pc.SelectedIndexChanged += delegate
                        {
                            if (building) return;
                            building = true;
                            Dictionary<string, string> k2 = new Dictionary<string, string>();
                            foreach (KeyValuePair<string, Control> kvp in inputs) k2[kvp.Key] = ValueOf(kvp.Key, kvp.Value);
                            building = false;
                            BeginInvoke((MethodInvoker)delegate { building = true; Rebuild(k2); building = false; });
                        };
                    }
                }
                if (p.Prefix == "drive")
                {
                    note.Text = "Google is retiring the shared sign-in key that rclone ships with. Create your own free Client ID and Secret (steps: rclone.org/drive/#making-your-own-client-id) and enter them below.";
                    note.Links.Clear(); note.Links.Add(0, note.Text.Length, "https://rclone.org/drive/#making-your-own-client-id");
                }
                else if (p.OAuth)
                {
                    note.Text = "You sign in with your browser in the next step. Client ID and secret are optional. Leave them blank to use rclone's shared key.";
                    note.Links.Clear();
                }
                else if (p.Prefix == "local") { note.Text = "A folder on this PC or a network path, useful to combine with the crypt type."; note.Links.Clear(); }
                else { note.Text = "Fill in the connection details. Fields marked * are required. Hover a field for its full help."; note.Links.Clear(); }
            }

            public string AccountName { get { return TName.Text.Trim(); } }

            public List<string> KeyValues()
            {
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, Control> kv in inputs)
                {
                    ROpt o = defs[kv.Key];
                    string v = ValueOf(kv.Key, kv.Value);
                    if (v.Length == 0) continue;
                    if (v == o.Default) continue;
                    l.Add(kv.Key + "=" + v);
                }
                return l;
            }

            void OnOk(object s, EventArgs e)
            {
                string n = AccountName;
                if (!Regex.IsMatch(n, @"^[A-Za-z0-9_][A-Za-z0-9_\-]*$")) { MessageBox.Show(this, "Enter an account name with letters, digits, - or _.", "Rsync GUI"); return; }
                if (existingNames.Contains(n + ":")) { MessageBox.Show(this, "An account called " + n + " exists already.", "Rsync GUI"); return; }
                foreach (KeyValuePair<string, Control> kv in inputs)
                {
                    ROpt o = defs[kv.Key];
                    if (o.Required && ValueOf(kv.Key, kv.Value).Length == 0 && o.Default.Length == 0)
                    { MessageBox.Show(this, kv.Key + " is required.", "Rsync GUI"); return; }
                }
                DialogResult = DialogResult.OK;
            }
        }

        public static bool AddAccount(IWin32Window owner)
        {
            if (!Rclone.Present) { MessageBox.Show(owner, "rclone.exe is missing from the bin folder.", "Rsync GUI"); return false; }
            List<string> existing = Rclone.Remotes();
            ProviderForm pf = new ProviderForm(existing);
            if (pf.ShowDialog(owner) != DialogResult.OK) return false;
            string name = pf.AccountName, type = pf.ProviderPrefix;
            List<string> kv = pf.KeyValues();

            Rclone.Res r = RunWait(owner, "Account setup", "Creating the account...", delegate(Action<Process> st) { return Rclone.CreateRemote(name, type, kv, st); });
            for (int guard = 0; guard < 40; guard++)
            {
                if (r.Canceled) { Rclone.Delete(name); return false; }
                if (r.Code != 0) { MessageBox.Show(owner, Rclone.ErrorText(r), "Account setup failed"); Rclone.Delete(name); return false; }
                Step s = Rclone.ParseStep(r.Out);
                if (s.Error.Length > 0) { MessageBox.Show(owner, s.Error, "Account setup failed"); Rclone.Delete(name); return false; }
                if (s.State.Length == 0) break;
                string answer;
                if (s.Option == null) { answer = ""; }
                else if (s.Option.Name == "config_is_local") answer = "true";      // sign in with this PC's browser
                else if (!Ask(owner, s.Option, out answer)) { Rclone.Delete(name); return false; }
                string state = s.State;
                bool oauth = state.StartsWith("*oauth");
                string msg = oauth
                    ? "Finish signing in in the browser window that opened.\r\nIf no window opened, look for a link in your browser or taskbar. This window closes by itself when you are done."
                    : "Working...";
                r = RunWait(owner, "Account setup", msg, delegate(Action<Process> st) { return Rclone.Continue(name, state, answer, st); });
            }
            MessageBox.Show(owner, "Account \"" + name + "\" is ready. Use " + name + ":folder as a source or destination in a cloud job.", "Rsync GUI");
            return true;
        }

        // ---------------------------------------------------------------- manage accounts

        public static void ManageAccounts(IWin32Window owner)
        {
            if (!Rclone.Present) { MessageBox.Show(owner, "rclone.exe is missing from the bin folder.", "Rsync GUI"); return; }
            Form f = NewForm("Cloud accounts", 620, 420);
            ListBox lb = new ListBox(); lb.Location = new Point(14, 14); lb.Size = new Size(430, 340); lb.IntegralHeight = false;
            Label info = new Label(); info.Location = new Point(14, 362); info.Size = new Size(590, 44); info.ForeColor = SystemColors.GrayText;
            info.Text = "Accounts are stored in the data folder (rclone.conf) with their sign-in tokens. Only administrators and SYSTEM can read that folder.";
            Button add = Btn("Add account...", 456, 14, 150);
            Button test = Btn("Test", 456, 50, 150);
            Button remove = Btn("Remove", 456, 86, 150);
            Button classic = Btn("Classic setup...", 456, 150, 150);
            Button refresh = Btn("Refresh list", 456, 186, 150);
            Button close = Btn("Close", 456, 318, 150); close.DialogResult = DialogResult.OK;
            f.CancelButton = close;
            Action reload = delegate
            {
                lb.Items.Clear();
                Dictionary<string, string> types = Rclone.RemoteTypes();
                foreach (KeyValuePair<string, string> kv in types) lb.Items.Add(kv.Key + "    " + kv.Value);
            };
            add.Click += delegate { if (AddAccount(f)) reload(); };
            refresh.Click += delegate { reload(); };
            remove.Click += delegate
            {
                if (lb.SelectedItem == null) return;
                string n = ((string)lb.SelectedItem).Split(' ')[0].TrimEnd(':');
                if (MessageBox.Show(f, "Remove the account \"" + n + "\"? Files in the cloud are not touched. Jobs that use it will fail until you add it again.", "Rsync GUI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Rclone.Delete(n); reload();
            };
            test.Click += delegate
            {
                if (lb.SelectedItem == null) return;
                string n = ((string)lb.SelectedItem).Split(' ')[0];
                Rclone.Res r = RunWait(f, "Test connection", "Connecting to " + n + " ...", delegate(Action<Process> st) { return Rclone.Run("lsd " + Cmd.Quote(n) + " --max-depth 1", 90000, st); });
                if (r.Canceled) return;
                if (r.Code == 0)
                {
                    int count = 0; foreach (string x in r.Out.Replace("\r", "").Split('\n')) if (x.Trim().Length > 0) count++;
                    MessageBox.Show(f, "Connected. " + count + " top-level folder(s) found.", "Rsync GUI");
                }
                else MessageBox.Show(f, Rclone.ErrorText(r), "Connection failed");
            };
            classic.Click += delegate
            {
                MessageBox.Show(f, "This opens rclone's own text setup in a console window. Close the window when you finish, then click Refresh list.", "Rsync GUI");
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/k \"\"" + Rclone.Exe + "\" --config \"" + Rclone.Conf + "\" config\"");
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception ex) { MessageBox.Show(f, ex.Message, "Rsync GUI"); }
            };
            f.Controls.AddRange(new Control[] { lb, info, add, test, remove, classic, refresh, close });
            f.Shown += delegate { reload(); };
            f.ShowDialog(owner);
        }

        // ---------------------------------------------------------------- browse cloud folders

        public static string BrowseFolder(IWin32Window owner, string current)
        {
            if (!Rclone.Present) { MessageBox.Show(owner, "rclone.exe is missing from the bin folder.", "Rsync GUI"); return null; }
            List<string> remotes = Rclone.Remotes();
            if (remotes.Count == 0)
            {
                MessageBox.Show(owner, "There is no cloud account yet. Click \"Cloud accounts...\" and add one first.", "Rsync GUI");
                return null;
            }
            Form f = NewForm("Choose a cloud folder", 520, 470);
            ComboBox acc = new ComboBox(); acc.DropDownStyle = ComboBoxStyle.DropDownList; acc.Location = new Point(14, 14); acc.Width = 200;
            foreach (string r in remotes) acc.Items.Add(r);
            TextBox path = new TextBox(); path.Location = new Point(224, 14); path.Width = 282;
            ListBox lb = new ListBox(); lb.Location = new Point(14, 48); lb.Size = new Size(492, 340); lb.IntegralHeight = false;
            Label status = new Label(); status.Location = new Point(14, 394); status.Size = new Size(492, 20); status.ForeColor = SystemColors.GrayText;
            Button up = Btn("Up", 14, 428, 70);
            Button ok = Btn("Use this folder", 300, 428, 120); ok.DialogResult = DialogResult.OK;
            Button cancel = Btn("Cancel", 426, 428, 80); cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { acc, path, lb, status, up, ok, cancel });

            string startRemote = null, startPath = "";
            if (!string.IsNullOrEmpty(current) && current.IndexOf(':') > 0)
            {
                int i = current.IndexOf(':');
                startRemote = current.Substring(0, i + 1); startPath = current.Substring(i + 1);
            }
            acc.SelectedIndex = Math.Max(0, startRemote == null ? 0 : remotes.IndexOf(startRemote));
            path.Text = startPath;

            Action load = delegate
            {
                lb.Items.Clear();
                status.Text = "Loading...";
                f.Cursor = Cursors.WaitCursor; Application.DoEvents();
                string target = (string)acc.SelectedItem + path.Text.Trim().Trim('/');
                Rclone.Res r = Rclone.Run("lsf " + Cmd.Quote(target) + " --dirs-only", 90000);
                f.Cursor = Cursors.Default;
                if (r.Code != 0) { status.Text = "Could not list this folder."; MessageBox.Show(f, Rclone.ErrorText(r), "Rsync GUI"); return; }
                foreach (string x in r.Out.Replace("\r", "").Split('\n')) { string t = x.Trim().TrimEnd('/'); if (t.Length > 0) lb.Items.Add(t); }
                status.Text = lb.Items.Count + " folder(s). Double-click one to open it.";
            };
            acc.SelectedIndexChanged += delegate { path.Text = ""; load(); };
            lb.DoubleClick += delegate
            {
                if (lb.SelectedItem == null) return;
                string p = path.Text.Trim().Trim('/');
                path.Text = (p.Length == 0 ? "" : p + "/") + (string)lb.SelectedItem;
                load();
            };
            up.Click += delegate
            {
                string p = path.Text.Trim().Trim('/');
                int i = p.LastIndexOf('/');
                path.Text = i < 0 ? "" : p.Substring(0, i);
                load();
            };
            path.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == System.Windows.Forms.Keys.Enter) { e.SuppressKeyPress = true; load(); } };
            f.Shown += delegate { load(); };
            if (f.ShowDialog(owner) != DialogResult.OK) return null;
            return (string)acc.SelectedItem + path.Text.Trim().Trim('/');
        }
    }
}
