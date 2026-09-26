using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RsyncGui
{
    // The Alerts... window: where failure alerts go and when they are sent.
    public static class AlertsUi
    {
        static Label L(string t, int x, int y) { Label l = new Label(); l.Text = t; l.AutoSize = true; l.Location = new Point(x, y + 3); return l; }
        static TextBox T(int x, int y, int w) { TextBox t = new TextBox(); t.Location = new Point(x, y); t.Width = w; return t; }

        public static bool IsOn()
        {
            AlertSettings s = Alerts.Load();
            return s.Enabled && Alerts.AnyChannel(s);
        }

        public static void Show(IWin32Window owner)
        {
            AlertSettings cur = Alerts.Load();

            Form f = new Form();
            f.Text = "Alerts"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(700, 690); f.Font = new Font("Segoe UI", 9F); f.ShowInTaskbar = false;
            ToolTip tip = new ToolTip(); tip.AutoPopDelay = 30000;

            CheckBox cEnabled = new CheckBox(); cEnabled.Text = "Send alerts"; cEnabled.AutoSize = true; cEnabled.Location = new Point(16, 14);
            cEnabled.Font = new Font("Segoe UI", 10F, FontStyle.Bold); cEnabled.Checked = cur.Enabled;
            Label intro = new Label(); intro.Location = new Point(16, 40); intro.Size = new Size(670, 34); intro.ForeColor = SystemColors.GrayText;
            intro.Text = "Alerts are sent by the background runner for scheduled jobs. Jobs you start by hand do not send alerts. Each job also has a \"Send alerts for this job\" box on its Job tab.";

            GroupBox gw = new GroupBox(); gw.Text = "Send an alert when"; gw.Location = new Point(16, 78); gw.Size = new Size(668, 108);
            CheckBox cFail = new CheckBox(); cFail.Text = "a scheduled job fails (after its retries are used up)"; cFail.AutoSize = true; cFail.Location = new Point(14, 22); cFail.Checked = cur.OnFail;
            CheckBox cRecover = new CheckBox(); cRecover.Text = "a job that failed works again"; cRecover.AutoSize = true; cRecover.Location = new Point(14, 46); cRecover.Checked = cur.OnRecover;
            CheckBox cStale = new CheckBox(); cStale.Text = "a job has not succeeded for"; cStale.AutoSize = true; cStale.Location = new Point(14, 70); cStale.Checked = cur.StaleHours > 0;
            NumericUpDown nStale = new NumericUpDown(); nStale.Location = new Point(190, 68); nStale.Width = 64; nStale.Minimum = 1; nStale.Maximum = 10000; nStale.Value = Math.Max(1, Math.Min(10000, cur.StaleHours > 0 ? cur.StaleHours : 48));
            Label lStale = L("hours (never sooner than the schedule allows)", 260, 70);
            gw.Controls.AddRange(new Control[] { cFail, cRecover, cStale, nStale, lStale });
            tip.SetToolTip(cFail, "You get one alert per failure, then a reminder every 24 hours while the job keeps failing.");

            GroupBox ge = new GroupBox(); ge.Text = "Email"; ge.Location = new Point(16, 194); ge.Size = new Size(668, 182);
            CheckBox cEmail = new CheckBox(); cEmail.Text = "Send email"; cEmail.AutoSize = true; cEmail.Location = new Point(14, 20); cEmail.Checked = cur.EmailOn;
            Label l1 = L("Server", 14, 48); TextBox tHost = T(80, 46, 250); tHost.Text = cur.SmtpHost;
            Label l2 = L("Port", 344, 48); NumericUpDown nPort = new NumericUpDown(); nPort.Location = new Point(380, 46); nPort.Width = 70; nPort.Maximum = 65535; nPort.Value = Math.Max(1, Math.Min(65535, cur.SmtpPort));
            CheckBox cTls = new CheckBox(); cTls.Text = "Use TLS (STARTTLS, port 587)"; cTls.AutoSize = true; cTls.Location = new Point(466, 48); cTls.Checked = cur.SmtpTls;
            Label l3 = L("User", 14, 78); TextBox tUser = T(80, 76, 250); tUser.Text = cur.SmtpUser;
            Label l4 = L("Password", 344, 78); TextBox tPass = T(414, 76, 236); tPass.UseSystemPasswordChar = true; tPass.Text = cur.SmtpPass;
            Label l5 = L("From", 14, 108); TextBox tFrom = T(80, 106, 250); tFrom.Text = cur.From;
            Label l6 = L("To", 344, 108); TextBox tTo = T(414, 106, 236); tTo.Text = cur.To;
            Label eh = new Label(); eh.Location = new Point(14, 136); eh.Size = new Size(640, 40); eh.ForeColor = SystemColors.GrayText;
            eh.Text = "Separate addresses with commas. Port 465 (implicit SSL) is not supported. Some providers need an app password. If email is hard, use the web hook.";
            ge.Controls.AddRange(new Control[] { cEmail, l1, tHost, l2, nPort, cTls, l3, tUser, l4, tPass, l5, tFrom, l6, tTo, eh });

            GroupBox gh = new GroupBox(); gh.Text = "Chat or web hook"; gh.Location = new Point(16, 384); gh.Size = new Size(668, 118);
            CheckBox cHook = new CheckBox(); cHook.Text = "Send to a web hook"; cHook.AutoSize = true; cHook.Location = new Point(14, 20); cHook.Checked = cur.HookOn;
            Label l7 = L("Web hook URL", 14, 48); TextBox tUrl = T(110, 46, 540); tUrl.Text = cur.HookUrl; tUrl.UseSystemPasswordChar = true;
            Label l8 = L("Format", 14, 78); ComboBox cFmt = new ComboBox(); cFmt.DropDownStyle = ComboBoxStyle.DropDownList; cFmt.Location = new Point(110, 76); cFmt.Width = 340;
            foreach (string fl in Alerts.HookFormatLabels) cFmt.Items.Add(fl);
            cFmt.SelectedIndex = Math.Max(0, Array.IndexOf(Alerts.HookFormats, cur.HookFormat));
            CheckBox cShow = new CheckBox(); cShow.Text = "Show the URL"; cShow.AutoSize = true; cShow.Location = new Point(470, 78);
            cShow.CheckedChanged += delegate { tUrl.UseSystemPasswordChar = !cShow.Checked; };
            gh.Controls.AddRange(new Control[] { cHook, l7, tUrl, l8, cFmt, cShow });
            tip.SetToolTip(tUrl, "The URL contains a secret. It is stored encrypted for this machine.");

            CheckBox cEvent = new CheckBox(); cEvent.Text = "Also write to the Windows Event Log (Application log, source RsyncGui)"; cEvent.AutoSize = true; cEvent.Location = new Point(20, 514); cEvent.Checked = cur.EventLogOn;

            Label note = new Label(); note.Location = new Point(16, 542); note.Size = new Size(668, 56); note.ForeColor = SystemColors.GrayText;
            note.Text = "An alert contains the job name, the result, the source and destination paths, the last lines of the log and this computer's name. " +
                        "It goes only to the places you set up here. The email password and the web hook URL are stored encrypted with Windows (this machine only).";

            Button test = new Button(); test.Text = "Send test alert"; test.Location = new Point(16, 640); test.Size = new Size(140, 30); test.UseVisualStyleBackColor = true;
            Button save = new Button(); save.Text = "Save"; save.Location = new Point(504, 640); save.Size = new Size(84, 30); save.DialogResult = DialogResult.OK; save.UseVisualStyleBackColor = true;
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.Location = new Point(600, 640); cancel.Size = new Size(84, 30); cancel.DialogResult = DialogResult.Cancel; cancel.UseVisualStyleBackColor = true;
            f.AcceptButton = save; f.CancelButton = cancel;

            Func<AlertSettings> read = delegate
            {
                AlertSettings s = new AlertSettings();
                s.Enabled = cEnabled.Checked; s.OnFail = cFail.Checked; s.OnRecover = cRecover.Checked;
                s.StaleHours = cStale.Checked ? (int)nStale.Value : 0;
                s.EmailOn = cEmail.Checked; s.SmtpHost = tHost.Text.Trim(); s.SmtpPort = (int)nPort.Value; s.SmtpTls = cTls.Checked;
                s.SmtpUser = tUser.Text.Trim(); s.SmtpPass = tPass.Text; s.From = tFrom.Text.Trim(); s.To = tTo.Text.Trim();
                s.HookOn = cHook.Checked; s.HookUrl = tUrl.Text.Trim(); s.HookFormat = Alerts.HookFormats[Math.Max(0, cFmt.SelectedIndex)];
                s.EventLogOn = cEvent.Checked;
                return s;
            };

            test.Click += delegate
            {
                AlertSettings s = read();
                test.Enabled = false; f.Cursor = Cursors.WaitCursor;
                List<string> res = null;
                Thread t = new Thread(delegate() { res = Alerts.SendTest(s); });
                t.IsBackground = true; t.Start();
                while (t.IsAlive) { Application.DoEvents(); Thread.Sleep(50); }
                f.Cursor = Cursors.Default; test.Enabled = true;
                MessageBox.Show(f, string.Join("\r\n", res.ToArray()), "Test alert");
            };

            f.Controls.AddRange(new Control[] { cEnabled, intro, gw, ge, gh, cEvent, note, test, save, cancel });
            if (f.ShowDialog(owner) != DialogResult.OK) return;

            try
            {
                AlertSettings s = read();
                if (s.Enabled && !Alerts.AnyChannel(s))
                    MessageBox.Show(owner, "Alerts are switched on but no channel is filled in, so nothing will be sent. Fill in an email, a web hook or the Event Log.", "Alerts");
                Alerts.Save(s);
            }
            catch (Exception ex) { MessageBox.Show(owner, "Could not save: " + ex.Message, "Alerts", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
