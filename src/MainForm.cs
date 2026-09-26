using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RsyncGui
{
    public class MainForm : Form
    {
        StoreData store;
        Job cur;
        bool loading;
        bool pwChanged;
        string savedSnapshot = "";

        ListView lv;
        TabControl tabs;
        ToolStrip ts;
        ToolStripButton bNew, bDup, bDel, bSave, bRun, bDry, bStop, bAuto, bLogs, bAlerts;
        CheckBox cAlert;
        ToolStripStatusLabel sVer, sAuto;
        ToolTip tip = new ToolTip();

        TextBox tName, tSrc, tDest, tSshPort, tSshKey, tSshOpts, tPass, tExtra;
        ComboBox cKind;
        CheckBox cAccept, cCatch;
        NumericUpDown nEvery, nRetries, nRetry;
        ComboBox cSched, cUnit;
        DateTimePicker dtTime;
        CheckBox[] chDays = new CheckBox[7];
        Label lNext, lSchedWhen;
        LinkLabel lnkBoot;
        Panel pnlRun, pnlWarn;
        Label lWarn;
        Button bWarn;
        ProgressBar pbRun;
        Label lRunState, lRunPct, lRunDetail, lRunFile, lRunNext;
        Button bAddDir, bAddFile, bDestBrowse, bKeyBrowse;
        Dictionary<string, Control> optCtl = new Dictionary<string, Control>();
        TextBox tInc, tExc, tFil;
        TextBox tDaemon, tCmd, tLog;
        TabPage pgCmd, pgLog, pgJob, pgPatterns, pgCloud, pgDaemon, pgHistory;
        ListView lvHist;
        Label lHistSum;
        string histStamp = "";
        List<string> pendingDeletes = new List<string>();
        List<TabPage> rsyncPages = new List<TabPage>();
        Dictionary<string, Control> cloudCtl = new Dictionary<string, Control>();
        ComboBox cMode;
        GroupBox gsRsync, gcCloud;
        Button bSrcCloud, bDestCloud, bAccounts;
        Label lHint, lExtra, lExtraHint;
        bool applyingTabs;
        System.Windows.Forms.Timer timer;
        int ticks;
        volatile bool taskExists;
        volatile bool runnerActive;
        long lastLogLen = -1;
        string lastLogId = "";

        public MainForm()
        {
            Text = "Rsync GUI";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(1280, 880);
            MinimumSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;
            tip.AutoPopDelay = 20000;

            BuildUi();
            Paths.EnsureDirs();
            store = Store.Load();
            savedSnapshot = store.Signature();
            RefreshList(null);
            if (lv.Items.Count > 0) lv.Items[0].Selected = true; else LoadJob(null);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += OnTick;
            timer.Start();
            try { taskExists = BootTask.Exists(); runnerActive = JobRun.IsRunning("_runner"); } catch { }
            RefreshAutoStatus();
            RefreshAlertsLabel();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string v = "rsync not found";
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Paths.RsyncExe, "--version");
                    psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                    psi.EnvironmentVariables["PATH"] = Paths.BinDir + ";" + Environment.GetEnvironmentVariable("PATH");
                    Process p = Process.Start(psi);
                    v = p.StandardOutput.ReadLine();
                    p.WaitForExit();
                }
                catch { }
                try
                {
                    if (File.Exists(Rclone.Exe))
                    {
                        ProcessStartInfo rp = new ProcessStartInfo(Rclone.Exe, "version");
                        rp.UseShellExecute = false; rp.CreateNoWindow = true; rp.RedirectStandardOutput = true;
                        Process q = Process.Start(rp);
                        v += "   |   " + q.StandardOutput.ReadLine();
                        q.WaitForExit();
                    }
                }
                catch { }
                try { BeginInvoke((MethodInvoker)delegate { sVer.Text = v; }); } catch { }
            });
        }

        // ---------------------------------------------------------------- UI construction

        static Label L(string t, int x, int y)
        {
            Label l = new Label(); l.Text = t; l.AutoSize = true; l.Location = new Point(x, y + 3); return l;
        }

        static TextBox T(int x, int y, int w)
        {
            TextBox t = new TextBox(); t.Location = new Point(x, y); t.Width = w; return t;
        }

        static Button Btn(string t, int x, int y, int w)
        {
            Button b = new Button(); b.Text = t; b.Location = new Point(x, y - 1); b.Size = new Size(w, 26); b.UseVisualStyleBackColor = true; return b;
        }

        void BuildUi()
        {
            ts = new ToolStrip();
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.RenderMode = ToolStripRenderMode.System;
            bNew = TB("New job", NewJob);
            bDup = TB("Duplicate", DupJob);
            bDel = TB("Delete", DelJob);
            ts.Items.Add(new ToolStripSeparator());
            bSave = TB("Save", delegate { SaveAll(); });
            ts.Items.Add(new ToolStripSeparator());
            bRun = TB("Run now", delegate { DoRun(false); });
            bDry = TB("Dry run", delegate { DoRun(true); });
            bStop = TB("Stop", DoStop);
            ts.Items.Add(new ToolStripSeparator());
            bAuto = TB("Auto-start at boot: ...", ToggleAutoStart);
            bLogs = TB("Open data folder", delegate { Process.Start("explorer.exe", Paths.DataDir); });
            bAlerts = TB("Alerts: ...", delegate { AlertsUi.Show(this); RefreshAlertsLabel(); });
            tip.SetToolTip(ts, "");

            StatusStrip ss = new StatusStrip();
            sVer = new ToolStripStatusLabel("rsync ...");
            sAuto = new ToolStripStatusLabel("");
            sAuto.Spring = true; sAuto.TextAlign = ContentAlignment.MiddleRight;
            ss.Items.Add(sVer); ss.Items.Add(sAuto);

            lv = new ListView();
            lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false; lv.MultiSelect = false;
            lv.Dock = DockStyle.Left; lv.Width = 400; lv.GridLines = false;
            lv.Columns.Add("Job", 130); lv.Columns.Add("Schedule", 115); lv.Columns.Add("Status", 160);
            lv.ItemSelectionChanged += OnSel;
            Splitter sp = new Splitter(); sp.Dock = DockStyle.Left; sp.Width = 5;

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            pgJob = BuildJobPage();
            pgPatterns = BuildPatternsPage();
            pgCloud = BuildOptionPage("Cloud options", CloudOpts.Group, CloudOpts.All, cloudCtl);
            foreach (string g in OptTable.Groups) rsyncPages.Add(BuildOptionPage(g, g, OptTable.All, optCtl));
            pgDaemon = BuildDaemonPage();
            pgCmd = BuildCmdPage();
            pgLog = BuildLogPage();
            pgHistory = BuildHistoryPage();
            ApplyTabs("sync");
            tabs.SelectedIndexChanged += delegate
            {
                if (applyingTabs) return;
                if (tabs.SelectedTab == pgCmd) UpdateCmd();
                if (tabs.SelectedTab == pgLog) TailLog(true);
                if (tabs.SelectedTab == pgHistory) RefreshHistory(true);
            };

            Panel pnlRight = new Panel(); pnlRight.Dock = DockStyle.Fill;
            pnlRight.Controls.Add(tabs);
            pnlRight.Controls.Add(BuildRunPanel());
            pnlRight.Controls.Add(BuildWarnPanel());
            Controls.Add(pnlRight);
            Controls.Add(sp);
            Controls.Add(lv);
            Controls.Add(ts);
            Controls.Add(ss);
            FormClosing += OnClosing;
        }

        ToolStripButton TB(string text, EventHandler h)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Click += h;
            ts.Items.Add(b);
            return b;
        }

        TabPage BuildJobPage()
        {
            TabPage p = new TabPage("Job");
            p.AutoScroll = true;
            p.Padding = new Padding(6);

            p.Controls.Add(L("Name", 12, 14)); tName = T(110, 12, 250); p.Controls.Add(tName);
            p.Controls.Add(L("Type", 380, 14));
            cKind = new ComboBox(); cKind.DropDownStyle = ComboBoxStyle.DropDownList;
            cKind.Items.Add("Files: folders, SSH or rsync server (rsync)");
            cKind.Items.Add("Cloud: OneDrive, Google Drive, S3 and more (rclone)");
            cKind.Items.Add("Run an rsync server (daemon)");
            cKind.Location = new Point(430, 12); cKind.Width = 320; cKind.SelectedIndex = 0;
            cKind.SelectedIndexChanged += delegate { UpdateKindUi(); };
            p.Controls.Add(cKind);

            p.Controls.Add(L("Source(s)", 12, 46));
            tSrc = T(110, 44, 520); tSrc.Multiline = true; tSrc.Height = 78; tSrc.ScrollBars = ScrollBars.Vertical; tSrc.AcceptsReturn = true;
            p.Controls.Add(tSrc);
            bAddDir = Btn("Add folder...", 640, 44, 110); bAddDir.Click += delegate { BrowseInto(tSrc, true, true); }; p.Controls.Add(bAddDir);
            bAddFile = Btn("Add file...", 640, 74, 110); bAddFile.Click += delegate { BrowseInto(tSrc, false, true); }; p.Controls.Add(bAddFile);
            p.Controls.Add(L("one per line", 12, 66));
            bSrcCloud = Btn("Cloud folder...", 640, 104, 110); bSrcCloud.Visible = false;
            bSrcCloud.Click += delegate { CloudPick(tSrc, true); };
            p.Controls.Add(bSrcCloud);

            p.Controls.Add(L("Destination", 12, 134)); tDest = T(110, 132, 520); p.Controls.Add(tDest);
            bDestBrowse = Btn("Browse...", 640, 132, 110); bDestBrowse.Click += delegate { BrowseInto(tDest, true, false); }; p.Controls.Add(bDestBrowse);
            bDestCloud = Btn("Cloud folder...", 640, 160, 110); bDestCloud.Visible = false;
            bDestCloud.Click += delegate { CloudPick(tDest, false); };
            p.Controls.Add(bDestCloud);
            lHint = new Label();
            lHint.Location = new Point(110, 160); lHint.Size = new Size(520, 44); lHint.ForeColor = SystemColors.GrayText;
            p.Controls.Add(lHint);

            GroupBox gs = new GroupBox(); gsRsync = gs; gs.Text = "Connection (used when a source or destination is remote)";
            gs.Location = new Point(12, 210); gs.Size = new Size(740, 116);
            gs.Controls.Add(L("SSH port", 12, 24)); tSshPort = T(110, 22, 70); gs.Controls.Add(tSshPort);
            gs.Controls.Add(L("SSH key file", 210, 24)); tSshKey = T(300, 22, 340); gs.Controls.Add(tSshKey);
            bKeyBrowse = Btn("Browse...", 648, 22, 80); bKeyBrowse.Click += delegate { BrowseInto(tSshKey, false, false); }; gs.Controls.Add(bKeyBrowse);
            gs.Controls.Add(L("Extra SSH options", 12, 54)); tSshOpts = T(140, 52, 340); gs.Controls.Add(tSshOpts);
            cAccept = new CheckBox(); cAccept.Text = "Accept new host keys automatically"; cAccept.AutoSize = true; cAccept.Location = new Point(490, 54); cAccept.Checked = true;
            gs.Controls.Add(cAccept);
            gs.Controls.Add(L("Daemon password", 12, 84)); tPass = T(140, 82, 200); tPass.UseSystemPasswordChar = true; gs.Controls.Add(tPass);
            tPass.Enter += delegate { tPass.SelectAll(); };
            tPass.TextChanged += delegate { if (!loading) pwChanged = true; };
            Label pl = new Label(); pl.Text = "SSH uses key login only. The password is for rsync:// daemons."; pl.AutoSize = true;
            pl.Location = new Point(350, 86); pl.ForeColor = SystemColors.GrayText; gs.Controls.Add(pl);
            p.Controls.Add(gs);

            GroupBox gc = new GroupBox(); gcCloud = gc; gc.Text = "Cloud storage (rclone)";
            gc.Location = new Point(12, 210); gc.Size = new Size(740, 116); gc.Visible = false;
            gc.Controls.Add(L("What to do", 12, 26));
            cMode = new ComboBox(); cMode.DropDownStyle = ComboBoxStyle.DropDownList; cMode.Location = new Point(100, 24); cMode.Width = 620;
            foreach (string ml in CloudOpts.ModeLabels) cMode.Items.Add(ml);
            cMode.SelectedIndex = 0;
            gc.Controls.Add(cMode);
            gc.Controls.Add(L("Accounts", 12, 62));
            bAccounts = Btn("Cloud accounts...", 100, 60, 170); bAccounts.Click += delegate { CloudUi.ManageAccounts(this); };
            gc.Controls.Add(bAccounts);
            Label ch = new Label(); ch.AutoSize = true; ch.ForeColor = SystemColors.GrayText; ch.Location = new Point(284, 66);
            ch.Text = "Sign in to OneDrive, Google Drive and others once. Then use name:folder below.";
            gc.Controls.Add(ch);
            Label ch2 = new Label(); ch2.AutoSize = true; ch2.ForeColor = SystemColors.GrayText; ch2.Location = new Point(100, 92);
            ch2.Text = "Pick a cloud folder with the Cloud folder... buttons, or type a path such as  myonedrive:Backups/Photos";
            gc.Controls.Add(ch2);
            p.Controls.Add(gc);

            GroupBox ga = new GroupBox(); ga.Text = "When to run";
            ga.Location = new Point(12, 336); ga.Size = new Size(740, 210);
            ga.Controls.Add(L("Run", 12, 26));
            cSched = new ComboBox(); cSched.DropDownStyle = ComboBoxStyle.DropDownList; cSched.Location = new Point(56, 24); cSched.Width = 250;
            foreach (string ml in Sched.ModeLabels) cSched.Items.Add(ml);
            cSched.SelectedIndex = 0;
            cSched.SelectedIndexChanged += delegate { UpdateSchedUi(); };
            ga.Controls.Add(cSched);
            lSchedWhen = L("every", 322, 26); ga.Controls.Add(lSchedWhen);
            nEvery = new NumericUpDown(); nEvery.Location = new Point(372, 24); nEvery.Width = 64; nEvery.Minimum = 1; nEvery.Maximum = 10000; nEvery.Value = 1;
            nEvery.ValueChanged += delegate { UpdateSchedUi(); };
            ga.Controls.Add(nEvery);
            cUnit = new ComboBox(); cUnit.DropDownStyle = ComboBoxStyle.DropDownList; cUnit.Location = new Point(442, 24); cUnit.Width = 90;
            cUnit.Items.Add("minutes"); cUnit.Items.Add("hours"); cUnit.SelectedIndex = 1;
            cUnit.SelectedIndexChanged += delegate { UpdateSchedUi(); };
            ga.Controls.Add(cUnit);
            dtTime = new DateTimePicker(); dtTime.Format = DateTimePickerFormat.Custom; dtTime.CustomFormat = "HH:mm"; dtTime.ShowUpDown = true;
            dtTime.Location = new Point(372, 24); dtTime.Width = 80;
            dtTime.ValueChanged += delegate { UpdateSchedUi(); };
            ga.Controls.Add(dtTime);
            string[] dayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            int[] dayNums = { 1, 2, 3, 4, 5, 6, 0 };
            for (int di = 0; di < 7; di++)
            {
                CheckBox dc = new CheckBox(); dc.Text = dayNames[di]; dc.AutoSize = true; dc.Checked = true;
                dc.Location = new Point(56 + di * 66, 56);
                dc.CheckedChanged += delegate { UpdateSchedUi(); };
                chDays[dayNums[di]] = dc;
                ga.Controls.Add(dc);
            }
            cCatch = new CheckBox(); cCatch.Text = "If Windows was off at the scheduled time, run the job when Windows starts"; cCatch.AutoSize = true;
            cCatch.Location = new Point(12, 88); cCatch.Checked = true;
            ga.Controls.Add(cCatch);
            ga.Controls.Add(L("If a run fails, try again", 12, 116));
            nRetries = new NumericUpDown(); nRetries.Location = new Point(158, 114); nRetries.Width = 52; nRetries.Maximum = 20; nRetries.Value = 3;
            ga.Controls.Add(nRetries);
            ga.Controls.Add(L("times, waiting", 216, 116));
            nRetry = new NumericUpDown(); nRetry.Location = new Point(300, 114); nRetry.Width = 64; nRetry.Maximum = 10000; nRetry.Value = 5;
            ga.Controls.Add(nRetry);
            ga.Controls.Add(L("minutes each time (0 = never try again)", 370, 116));
            lNext = new Label(); lNext.Location = new Point(12, 142); lNext.Size = new Size(716, 18); lNext.ForeColor = SystemColors.GrayText;
            ga.Controls.Add(lNext);
            lnkBoot = new LinkLabel(); lnkBoot.AutoSize = true; lnkBoot.Location = new Point(12, 162); lnkBoot.Visible = false;
            lnkBoot.Text = "Auto-start at boot is OFF, so nothing runs by itself. Click here to turn it on.";
            lnkBoot.LinkClicked += delegate { ToggleAutoStart(null, EventArgs.Empty); };
            ga.Controls.Add(lnkBoot);
            cAlert = new CheckBox(); cAlert.Text = "Send alerts for this job (set up where they go with Alerts... on the toolbar)"; cAlert.AutoSize = true;
            cAlert.Location = new Point(12, 184); cAlert.Checked = true;
            ga.Controls.Add(cAlert);
            p.Controls.Add(ga);

            lExtra = L("Extra rsync arguments", 12, 562); p.Controls.Add(lExtra);
            tExtra = T(150, 560, 602); p.Controls.Add(tExtra);
            lExtraHint = new Label(); lExtraHint.AutoSize = true; lExtraHint.ForeColor = SystemColors.GrayText; lExtraHint.Location = new Point(150, 586);
            p.Controls.Add(lExtraHint);
            return p;
        }

        TabPage BuildOptionPage(string title, string group, List<OptDef> table, Dictionary<string, Control> ctl)
        {
            TabPage pg = new TabPage(title);
            Panel scroller = new Panel(); scroller.Dock = DockStyle.Fill; scroller.AutoScroll = true;
            TableLayoutPanel tl = new TableLayoutPanel();
            tl.Dock = DockStyle.Top; tl.AutoSize = true; tl.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tl.ColumnCount = 1; tl.Padding = new Padding(6);
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            FlowLayoutPanel fb = new FlowLayoutPanel();
            fb.Dock = DockStyle.Fill; fb.AutoSize = true; fb.AutoSizeMode = AutoSizeMode.GrowAndShrink; fb.WrapContents = true;
            fb.FlowDirection = FlowDirection.LeftToRight;
            tl.Controls.Add(fb);

            foreach (OptDef d in table)
            {
                if (d.Group != group) continue;
                string label = d.Label + "   --" + d.Key;
                if (d.Type == OT.Bool)
                {
                    CheckBox cb = new CheckBox();
                    cb.Text = d.Label + "  (--" + d.Key + ")";
                    cb.AutoSize = false;
                    cb.Width = 390;
                    cb.Height = cb.Text.Length > 50 ? 38 : 22;
                    cb.CheckAlign = ContentAlignment.TopLeft; cb.TextAlign = ContentAlignment.TopLeft;
                    tip.SetToolTip(cb, "--" + d.Key);
                    fb.Controls.Add(cb);
                    ctl[d.Key] = cb;
                }
            }
            foreach (OptDef d in table)
            {
                if (d.Group != group || d.Type == OT.Bool) continue;
                Panel row = new Panel(); row.Height = 28; row.Dock = DockStyle.Fill;
                Label l = new Label(); l.Text = d.Label + "  (--" + d.Key + ")"; l.Location = new Point(2, 5); l.Size = new Size(400, 20);
                l.AutoEllipsis = true; row.Controls.Add(l);
                Control input;
                if (d.Type == OT.Choice)
                {
                    ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cb.Items.Add(""); foreach (string c in d.Choices) cb.Items.Add(c);
                    cb.Location = new Point(410, 2); cb.Width = 200; input = cb;
                }
                else
                {
                    TextBox tb = new TextBox(); tb.Location = new Point(410, 2); tb.Width = (d.Type == OT.File || d.Type == OT.Dir) ? 310 : 340;
                    input = tb;
                    if (d.Type == OT.File || d.Type == OT.Dir)
                    {
                        Button b = Btn("...", 724, 2, 30);
                        bool isDir = d.Type == OT.Dir;
                        TextBox target = tb;
                        b.Click += delegate { BrowseInto(target, isDir, false); };
                        row.Controls.Add(b);
                    }
                }
                tip.SetToolTip(input, "--" + d.Key);
                row.Controls.Add(input);
                ctl[d.Key] = input;
                tl.Controls.Add(row);
            }
            scroller.Controls.Add(tl);
            pg.Controls.Add(scroller);
            return pg;
        }

        TabPage BuildPatternsPage()
        {
            TabPage pg = new TabPage("Patterns");
            Panel scroller = new Panel(); scroller.Dock = DockStyle.Fill; scroller.AutoScroll = true;
            TableLayoutPanel tl = new TableLayoutPanel();
            tl.Dock = DockStyle.Top; tl.AutoSize = true; tl.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tl.ColumnCount = 1; tl.Padding = new Padding(6);
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tInc = PatternBox(tl, "Include patterns (one per line, checked first). Example: *.docx");
            tExc = PatternBox(tl, "Exclude patterns (one per line). Example: *.tmp   or   node_modules/");
            Button bCommon = new Button(); bCommon.Text = "Add common excludes (node_modules, temp files)"; bCommon.AutoSize = true;
            bCommon.UseVisualStyleBackColor = true; bCommon.Margin = new Padding(3, 2, 3, 6);
            bCommon.Click += delegate { AddCommonExcludes(); };
            tl.Controls.Add(bCommon);
            tFil = PatternBox(tl, "Advanced filter rules (one per line, + includes and - excludes). Example: - *.bak   or   + */");
            scroller.Controls.Add(tl);
            pg.Controls.Add(scroller);
            return pg;
        }

        TextBox PatternBox(TableLayoutPanel tl, string caption)
        {
            Label l = new Label(); l.Text = caption; l.AutoSize = true; l.Margin = new Padding(3, 8, 3, 2);
            tl.Controls.Add(l);
            TextBox t = new TextBox(); t.Multiline = true; t.Height = 64; t.Dock = DockStyle.Fill; t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true;
            tl.Controls.Add(t);
            return t;
        }

        TabPage BuildDaemonPage()
        {
            TabPage p = new TabPage("Daemon");
            Label l = new Label(); l.Dock = DockStyle.Top; l.Height = 58; l.Padding = new Padding(8, 8, 8, 0);
            l.Text = "Used only by jobs of type \"Daemon server\". This is the rsyncd.conf that \"rsync --daemon\" runs with.\r\n" +
                     "Set the listening port and address on the Remote tab (--port, --address). The default port is 873.";
            tDaemon = new TextBox(); tDaemon.Multiline = true; tDaemon.Dock = DockStyle.Fill; tDaemon.ScrollBars = ScrollBars.Both;
            tDaemon.Font = new Font("Consolas", 9.5F); tDaemon.WordWrap = false; tDaemon.AcceptsReturn = true; tDaemon.AcceptsTab = true;
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 40;
            Button fw = Btn("Open Windows Firewall port...", 8, 8, 220); fw.Click += OpenFirewall; bottom.Controls.Add(fw);
            p.Controls.Add(tDaemon); p.Controls.Add(bottom); p.Controls.Add(l);
            return p;
        }

        TabPage BuildCmdPage()
        {
            TabPage p = new TabPage("Command");
            tCmd = new TextBox(); tCmd.Multiline = true; tCmd.ReadOnly = true; tCmd.Dock = DockStyle.Fill;
            tCmd.ScrollBars = ScrollBars.Vertical; tCmd.Font = new Font("Consolas", 9.5F); tCmd.BackColor = SystemColors.Window;
            Label l = new Label(); l.Dock = DockStyle.Top; l.Height = 26; l.Padding = new Padding(6, 6, 0, 0);
            l.Text = "The exact command line that Run now will start (read only).";
            p.Controls.Add(tCmd); p.Controls.Add(l);
            return p;
        }

        TabPage BuildHistoryPage()
        {
            TabPage p = new TabPage("History");
            lHistSum = new Label(); lHistSum.Dock = DockStyle.Top; lHistSum.Height = 40; lHistSum.Padding = new Padding(8, 10, 8, 0);
            lvHist = new ListView(); lvHist.Dock = DockStyle.Fill; lvHist.View = View.Details; lvHist.FullRowSelect = true; lvHist.HideSelection = false;
            lvHist.Columns.Add("Started", 130); lvHist.Columns.Add("How", 90); lvHist.Columns.Add("Took", 80); lvHist.Columns.Add("Result", 250);
            lvHist.Columns.Add("Data", 90); lvHist.Columns.Add("Files", 70); lvHist.Columns.Add("Errors", 60);
            lvHist.DoubleClick += ShowRunDetails;
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 40;
            Button refresh = Btn("Refresh", 8, 8, 90); refresh.Click += delegate { RefreshHistory(true); };
            Button csv = Btn("Save as CSV...", 106, 8, 120);
            csv.Click += delegate
            {
                if (cur == null) return;
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = "CSV file (*.csv)|*.csv"; d.FileName = cur.Name.Replace(' ', '-') + "-history.csv";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        try { File.WriteAllText(d.FileName, History.ToCsv(History.Load(cur.Id)), new UTF8Encoding(true)); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    }
                }
            };
            Button clear = Btn("Clear history", 234, 8, 110);
            clear.Click += delegate
            {
                if (cur == null) return;
                if (MessageBox.Show(this, "Clear the run history of \"" + cur.Name + "\"? The log file is not touched.", "Rsync GUI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                History.Clear(cur.Id); RefreshHistory(true);
            };
            Label hint = new Label(); hint.Text = "Double-click a run for details. The last " + History.Keep + " runs are kept."; hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText; hint.Location = new Point(360, 14);
            bottom.Controls.AddRange(new Control[] { refresh, csv, clear, hint });
            p.Controls.Add(lvHist); p.Controls.Add(bottom); p.Controls.Add(lHistSum);
            return p;
        }

        void RefreshHistory(bool force)
        {
            if (cur == null) { lvHist.Items.Clear(); lHistSum.Text = ""; return; }
            string stamp = cur.Id;
            try
            {
                FileInfo fi = new FileInfo(Paths.HistoryFile(cur.Id));
                stamp += "|" + (fi.Exists ? fi.Length + "|" + fi.LastWriteTimeUtc.Ticks : "none");
            }
            catch { }
            if (!force && stamp == histStamp) return;
            histStamp = stamp;
            List<RunRecord> runs = History.Load(cur.Id);
            lvHist.BeginUpdate();
            lvHist.Items.Clear();
            foreach (RunRecord r in runs)
            {
                DateTime st = r.StartDate();
                ListViewItem it = new ListViewItem(st == DateTime.MinValue ? r.Start : st.ToString("yyyy-MM-dd HH:mm:ss"));
                it.SubItems.Add(r.Trigger);
                it.SubItems.Add(History.Duration(r.Seconds));
                it.SubItems.Add(r.Result + (r.Dry ? "  (dry run)" : ""));
                it.SubItems.Add(r.Data);
                it.SubItems.Add(r.Files);
                it.SubItems.Add(r.Errors > 0 ? r.Errors.ToString() : "");
                it.Tag = r;
                if (r.Dry) it.ForeColor = SystemColors.GrayText;
                else if (r.Code == JobRun.Killed) it.ForeColor = Color.DarkOrange;
                else if (!r.Succeeded()) it.ForeColor = Color.Firebrick;
                else if (r.Errors > 0) it.ForeColor = Color.DarkGoldenrod;
                lvHist.Items.Add(it);
            }
            lvHist.EndUpdate();
            lHistSum.Text = History.Summary(runs);
        }

        void ShowRunDetails(object s, EventArgs e)
        {
            if (lvHist.SelectedItems.Count == 0) return;
            RunRecord r = (RunRecord)lvHist.SelectedItems[0].Tag;
            MessageBox.Show(this,
                "Started:  " + r.Start.Replace('T', ' ') + "\r\nEnded:  " + r.End.Replace('T', ' ') + "\r\nTook:  " + History.Duration(r.Seconds) +
                "\r\nHow:  " + r.Trigger + (r.Dry ? " (dry run)" : "") + "\r\nType:  " + r.Kind + (r.Mode.Length > 0 ? " (" + r.Mode + ")" : "") +
                "\r\nResult:  " + r.Result + "  (exit code " + r.Code + ")" + "\r\nData:  " + (r.Data.Length > 0 ? r.Data : "not reported") +
                "\r\nFiles:  " + (r.Files.Length > 0 ? r.Files : "not reported") + "\r\nErrors:  " + r.Errors + "\r\nSymbolic links skipped:  " + r.Links +
                "\r\nProgram version:  " + r.Version,
                "Run details");
        }

        TabPage BuildLogPage()
        {
            TabPage p = new TabPage("Log");
            tLog = new TextBox(); tLog.Multiline = true; tLog.ReadOnly = true; tLog.Dock = DockStyle.Fill;
            tLog.ScrollBars = ScrollBars.Both; tLog.WordWrap = false; tLog.Font = new Font("Consolas", 9F); tLog.BackColor = SystemColors.Window;
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 40;
            Button open = Btn("Open log file", 8, 8, 110);
            open.Click += delegate { if (cur != null && File.Exists(Paths.LogFile(cur.Id))) Process.Start("notepad.exe", Paths.LogFile(cur.Id)); };
            Button clear = Btn("Clear log", 126, 8, 90);
            clear.Click += delegate { if (cur != null) { try { File.Delete(Paths.LogFile(cur.Id)); } catch { } TailLog(true); } };
            bottom.Controls.Add(open); bottom.Controls.Add(clear);
            p.Controls.Add(tLog); p.Controls.Add(bottom);
            return p;
        }

        // ---------------------------------------------------------------- data <-> UI

        void ApplyTabs(string kind)
        {
            applyingTabs = true;
            TabPage keep = tabs.SelectedTab;
            tabs.TabPages.Clear();
            tabs.TabPages.Add(pgJob);
            if (kind != "daemon") tabs.TabPages.Add(pgPatterns);
            if (kind == "cloud") tabs.TabPages.Add(pgCloud);
            else foreach (TabPage rp in rsyncPages) tabs.TabPages.Add(rp);
            if (kind == "daemon") tabs.TabPages.Add(pgDaemon);
            tabs.TabPages.Add(pgCmd);
            tabs.TabPages.Add(pgHistory);
            tabs.TabPages.Add(pgLog);
            tabs.SelectedTab = (keep != null && tabs.TabPages.Contains(keep)) ? keep : pgJob;
            applyingTabs = false;
        }

        void CloudPick(TextBox target, bool source)
        {
            string first = target.Text.Replace("\r", "").Split('\n')[0].Trim();
            string picked = CloudUi.BrowseFolder(this, first);
            if (picked == null) return;
            target.Text = picked;
        }

        void UpdateKindUi()
        {
            int idx = cKind.SelectedIndex;
            bool sync = idx == 0, cloud = idx == 1, daemon = idx == 2;
            tSrc.Enabled = !daemon; tDest.Enabled = !daemon; bAddDir.Enabled = !daemon; bAddFile.Enabled = !daemon; bDestBrowse.Enabled = !daemon;
            bSrcCloud.Visible = cloud; bDestCloud.Visible = cloud;
            gsRsync.Visible = !cloud; gcCloud.Visible = cloud;
            tSshPort.Enabled = sync; tSshKey.Enabled = sync; tSshOpts.Enabled = sync; cAccept.Enabled = sync; bKeyBrowse.Enabled = sync; tPass.Enabled = sync;
            bDry.Enabled = !daemon;
            lExtra.Text = cloud ? "Extra rclone arguments" : "Extra rsync arguments";
            lExtraHint.Text = cloud
                ? "Anything not on the Cloud options tab, for example --drive-chunk-size 64M. Use \"quotes\" around spaces."
                : "Anything not on the option tabs, for example --link-dest=/a --link-dest=/b or -e \"custom shell\". Use \"quotes\" around spaces.";
            lHint.Text = cloud
                ? "Cloud jobs take one source. Either side can be a cloud path (name:folder) or a local folder.\r\nUpload: source C:\\Data, destination myonedrive:Backup.   Download: the other way round."
                : "Local: C:\\Data\\   Over SSH: user@host:/srv/data   Rsync daemon: rsync://host/module\r\nA trailing \\ or / on a source copies its contents. Without it, the folder itself is copied.";
            ApplyTabs(cloud ? "cloud" : (daemon ? "daemon" : "sync"));
        }

        void LoadOpts(List<OptDef> table, Dictionary<string, Control> ctl, Dictionary<string, string> d)
        {
            foreach (OptDef o in table)
            {
                string v;
                d.TryGetValue(o.Key, out v);
                Control c = ctl[o.Key];
                if (o.Type == OT.Bool) ((CheckBox)c).Checked = v == "1";
                else if (o.Type == OT.Choice) ((ComboBox)c).SelectedItem = v ?? "";
                else c.Text = v ?? "";
            }
        }

        void ReadOpts(List<OptDef> table, Dictionary<string, Control> ctl, Dictionary<string, string> d)
        {
            d.Clear();
            foreach (OptDef o in table)
            {
                Control c = ctl[o.Key];
                string v = o.Type == OT.Bool ? (((CheckBox)c).Checked ? "1" : "") : c.Text.Trim();
                if (v.Length > 0) d[o.Key] = v;
            }
        }

        void LoadJob(Job j)
        {
            loading = true;
            cur = j;
            bool has = j != null;
            tabs.Enabled = has;
            bDup.Enabled = has; bDel.Enabled = has; bRun.Enabled = has; bDry.Enabled = has; bStop.Enabled = has;
            if (has)
            {
                tName.Text = j.Name;
                cKind.SelectedIndex = j.Kind == "daemon" ? 2 : (j.Kind == "cloud" ? 1 : 0);
                cMode.SelectedIndex = Math.Max(0, Array.IndexOf(CloudOpts.Modes, j.CloudMode));
                tSrc.Text = (j.Sources ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                tDest.Text = j.Dest ?? "";
                tSshPort.Text = j.SshPort ?? ""; tSshKey.Text = j.SshKey ?? ""; tSshOpts.Text = j.SshOpts ?? "";
                cAccept.Checked = j.AcceptNew;
                tPass.Text = string.IsNullOrEmpty(j.Password) ? "" : "********";
                pwChanged = false;
                cSched.SelectedIndex = Math.Max(0, Array.IndexOf(Sched.Modes, j.SchedMode));
                nEvery.Value = Math.Max(1, Math.Min(10000, j.SchedEvery));
                cUnit.SelectedIndex = j.SchedUnit == "m" ? 0 : 1;
                dtTime.Value = DateTime.Today + Sched.TimeOfDay(j);
                string[] dl = (j.SchedDays ?? "").Split(',');
                for (int dd = 0; dd < 7; dd++) chDays[dd].Checked = Array.IndexOf(dl, dd.ToString()) >= 0;
                cCatch.Checked = j.CatchUp;
                cAlert.Checked = j.AlertOn;
                nRetries.Value = Math.Max(0, Math.Min(20, j.MaxRetries));
                nRetry.Value = Math.Max(0, Math.Min(10000, j.RetryMinutes));
                UpdateSchedUi();
                tExtra.Text = j.Extra ?? "";
                tInc.Text = (j.Includes ?? "").Replace("\n", "\r\n").Replace("\r\r", "\r");
                tExc.Text = (j.Excludes ?? "").Replace("\n", "\r\n").Replace("\r\r", "\r");
                tFil.Text = (j.Filters ?? "").Replace("\n", "\r\n").Replace("\r\r", "\r");
                tDaemon.Text = string.IsNullOrEmpty(j.DaemonConfig) ? Job.DaemonTemplate : j.DaemonConfig;
                LoadOpts(OptTable.All, optCtl, j.Opt);
                LoadOpts(CloudOpts.All, cloudCtl, j.COpt);
                UpdateKindUi();
                if (tabs.SelectedTab == pgCmd) UpdateCmd();
                TailLog(true);
            }
            else
            {
                tLog.Text = ""; tCmd.Text = "";
            }
            loading = false;
        }

        void ReadJob(Job j)
        {
            j.Name = tName.Text.Trim().Length == 0 ? "Unnamed job" : tName.Text.Trim();
            j.Kind = cKind.SelectedIndex == 2 ? "daemon" : (cKind.SelectedIndex == 1 ? "cloud" : "sync");
            j.CloudMode = CloudOpts.Modes[Math.Max(0, cMode.SelectedIndex)];
            j.Sources = tSrc.Text.Replace("\r\n", "\n");
            j.Dest = tDest.Text.Trim();
            j.SshPort = tSshPort.Text.Trim(); j.SshKey = tSshKey.Text.Trim(); j.SshOpts = tSshOpts.Text.Trim();
            j.AcceptNew = cAccept.Checked;
            if (pwChanged) j.Password = Secret.Protect(tPass.Text);
            FillSched(j);
            j.AlertOn = cAlert.Checked;
            j.Extra = tExtra.Text.Trim();
            j.Includes = tInc.Text.Replace("\r\n", "\n");
            j.Excludes = tExc.Text.Replace("\r\n", "\n");
            j.Filters = tFil.Text.Replace("\r\n", "\n");
            j.DaemonConfig = tDaemon.Text.Replace("\r\n", "\n");
            ReadOpts(OptTable.All, optCtl, j.Opt);
            ReadOpts(CloudOpts.All, cloudCtl, j.COpt);
        }

        void CommitCurrent()
        {
            if (cur == null || loading) return;
            ReadJob(cur);
            pwChanged = false;
            foreach (ListViewItem it in lv.Items) if (it.Tag == cur) { it.Text = cur.Name; break; }
        }

        void RefreshList(Job select)
        {
            loading = true;
            lv.Items.Clear();
            foreach (Job j in store.Jobs)
            {
                ListViewItem it = new ListViewItem(j.Name);
                it.SubItems.Add(Sched.Describe(j));
                it.SubItems.Add("");
                it.Tag = j;
                lv.Items.Add(it);
            }
            loading = false;
            UpdateStatuses();
            if (select != null) foreach (ListViewItem it in lv.Items) if (it.Tag == select) it.Selected = true;
        }

        void UpdateStatuses()
        {
            foreach (ListViewItem it in lv.Items)
            {
                Job j = (Job)it.Tag;
                string st;
                if (JobRun.IsRunning(j.Id))
                {
                    ProgressInfo pi = j.Kind == "daemon" ? null : ProgressInfo.Load(j.Id);
                    st = pi != null && pi.Pct >= 0 ? "Running " + pi.Pct + "%" : "Running";
                }
                else st = JobRun.LastResult(j.Id);
                if (st.Length == 0) st = "Never run";
                if (it.SubItems[2].Text != st) it.SubItems[2].Text = st;
                string sc = Sched.Describe(j);
                if (it.SubItems[1].Text != sc) it.SubItems[1].Text = sc;
            }
        }

        void OnSel(object s, ListViewItemSelectionChangedEventArgs e)
        {
            if (!e.IsSelected || loading) return;
            CommitCurrent();
            LoadJob((Job)e.Item.Tag);
        }

        // ---------------------------------------------------------------- actions

        void NewJob(object s, EventArgs e)
        {
            CommitCurrent();
            Job j = Job.Create();
            j.DaemonConfig = Job.DaemonTemplate;
            store.Jobs.Add(j);
            RefreshList(j);
            tabs.SelectedIndex = 0;
            tName.Focus(); tName.SelectAll();
        }

        void DupJob(object s, EventArgs e)
        {
            if (cur == null) return;
            CommitCurrent();
            Job c = cur.Clone();
            c.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            c.Name = cur.Name + " (copy)";
            c.SchedMode = "off";
            c.AutoStart = false;
            store.Jobs.Add(c);
            RefreshList(c);
        }

        void DelJob(object s, EventArgs e)
        {
            if (cur == null) return;
            if (MessageBox.Show(this, "Delete the job \"" + cur.Name + "\"?", "Rsync GUI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Job old = cur;
            cur = null;
            store.Jobs.Remove(old);
            pendingDeletes.Add(old.Id);
            RefreshList(null);
            if (lv.Items.Count > 0) lv.Items[0].Selected = true; else LoadJob(null);
        }

        // Once a deletion is saved, the job's log, state and history go too.
        void CleanDeleted()
        {
            List<string> keep = new List<string>();
            foreach (string id in pendingDeletes)
            {
                bool inUse = false;
                foreach (Job j in store.Jobs) if (j.Id == id) inUse = true;
                if (inUse) continue;
                if (JobRun.IsRunning(id)) { keep.Add(id); continue; }
                Housekeeping.RemoveJobFiles(id);
            }
            pendingDeletes = keep;
        }

        bool SaveAll()
        {
            try
            {
                CommitCurrent();
                Store.Save(store);
                savedSnapshot = store.Signature();
                CleanDeleted();
                UpdateStatuses();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save: " + ex.Message, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void DoRun(bool dry)
        {
            if (cur == null) return;
            CommitCurrent();
            if (cur.Kind == "cloud")
            {
                if (!Rclone.Present) { MessageBox.Show(this, "rclone.exe is missing from the bin folder.", "Rsync GUI"); return; }
                if (Cmd.Lines(cur.Sources).Count > 1)
                {
                    MessageBox.Show(this, "Cloud jobs take one source. Remove the extra lines, or make one job per source.", "Rsync GUI");
                    tabs.SelectedTab = pgJob; return;
                }
            }
            if (cur.Kind != "daemon")
            {
                if (Cmd.Lines(cur.Sources).Count == 0)
                {
                    MessageBox.Show(this, "Add at least one source.", "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    tabs.SelectedIndex = 0; return;
                }
                string v;
                bool listOnly = cur.Opt.TryGetValue("list-only", out v) && v == "1";
                if (cur.Dest.Length == 0 && !listOnly)
                {
                    MessageBox.Show(this, "Set a destination, or tick --list-only on the Basic tab.", "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    tabs.SelectedIndex = 0; return;
                }
            }
            string mix = Cmd.CloudMixup(cur);
            if (mix != null)
            {
                if (MessageBox.Show(this, "\"" + mix + ":\" is one of your cloud accounts, but this job uses the Files type (rsync), which treats it as an SSH server.\r\n\r\nSwitch this job to the Cloud type?",
                    "Rsync GUI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    cKind.SelectedIndex = 1;
                    CommitCurrent();
                    tabs.SelectedTab = pgJob;
                    MessageBox.Show(this, "Switched to Cloud. Check the mode on the Job tab, then click Run now.", "Rsync GUI");
                }
                return;
            }
            if (JobRun.IsRunning(cur.Id))
            {
                MessageBox.Show(this, "This job is already running.", "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!SaveAll()) return;
            Job j = cur.Clone();
            JobRun run = new JobRun(j, dry);
            Thread t = new Thread(delegate() { run.RunBlocking(null); });
            t.IsBackground = true;
            t.Start();
            tabs.SelectedTab = pgLog;
        }

        void DoStop(object s, EventArgs e)
        {
            if (cur == null) return;
            if (!JobRun.IsRunning(cur.Id)) return;
            JobRun.RequestStop(cur.Id);
        }

        void ToggleAutoStart(object s, EventArgs e)
        {
            try
            {
                if (BootTask.Exists())
                {
                    if (MessageBox.Show(this, "Remove the boot task? Jobs will no longer start by themselves after a reboot.", "Rsync GUI",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string err = BootTask.Remove();
                    if (err != null) MessageBox.Show(this, err, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    string user, pass;
                    if (!AskRunAs(out user, out pass)) return;
                    SaveAll();
                    string err = BootTask.Install(user, pass);
                    if (err != null) MessageBox.Show(this, err, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else MessageBox.Show(this, "Auto-start is on. The runner starts at every boot, and it has started now.\r\n" +
                        "It runs every job that has \"Start this job automatically when Windows starts\" ticked.", "Rsync GUI",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            RefreshAutoStatus();
        }

        bool AskRunAs(out string user, out string pass)
        {
            user = ""; pass = "";
            Form f = new Form();
            f.Text = "Run auto-start jobs as"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(560, 230); f.Font = Font;
            RadioButton rs = new RadioButton(); rs.Text = "SYSTEM account (recommended)"; rs.Location = new Point(16, 16); rs.AutoSize = true; rs.Checked = true;
            Label ls = new Label(); ls.Location = new Point(36, 38); ls.Size = new Size(510, 34); ls.ForeColor = SystemColors.GrayText;
            ls.Text = "Starts before anyone signs in. Full access to local files. No access to network shares that need your login.";
            RadioButton ru = new RadioButton(); ru.Text = "This account (needed for network shares or your own SSH keys):"; ru.Location = new Point(16, 84); ru.AutoSize = true;
            Label lu = new Label(); lu.Text = "User"; lu.Location = new Point(36, 116); lu.AutoSize = true;
            TextBox tu = new TextBox(); tu.Location = new Point(110, 112); tu.Width = 300; tu.Text = WindowsIdentity.GetCurrent().Name; tu.Enabled = false;
            Label lp = new Label(); lp.Text = "Password"; lp.Location = new Point(36, 148); lp.AutoSize = true;
            TextBox tp = new TextBox(); tp.Location = new Point(110, 144); tp.Width = 300; tp.UseSystemPasswordChar = true; tp.Enabled = false;
            ru.CheckedChanged += delegate { tu.Enabled = ru.Checked; tp.Enabled = ru.Checked; };
            Button ok = new Button(); ok.Text = "Turn on"; ok.DialogResult = DialogResult.OK; ok.Location = new Point(360, 190); ok.Size = new Size(90, 28);
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(456, 190); cancel.Size = new Size(90, 28);
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { rs, ls, ru, lu, tu, lp, tp, ok, cancel });
            if (f.ShowDialog(this) != DialogResult.OK) return false;
            if (ru.Checked)
            {
                if (tu.Text.Trim().Length == 0 || tp.Text.Length == 0)
                {
                    MessageBox.Show(this, "Enter the user name and password.", "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                user = tu.Text.Trim(); pass = tp.Text;
            }
            return true;
        }

        void OpenFirewall(object s, EventArgs e)
        {
            string port = "873";
            Control pc;
            if (optCtl.TryGetValue("port", out pc) && pc.Text.Trim().Length > 0) port = pc.Text.Trim();
            int n;
            if (!int.TryParse(port, out n) || n < 1 || n > 65535) { MessageBox.Show(this, "The port must be a number from 1 to 65535.", "Rsync GUI"); return; }
            if (MessageBox.Show(this, "Add an inbound Windows Firewall rule that allows TCP port " + n + " to rsync.exe?", "Rsync GUI",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ProcessStartInfo psi = new ProcessStartInfo("netsh.exe",
                "advfirewall firewall add rule name=\"Rsync GUI daemon " + n + "\" dir=in action=allow protocol=TCP localport=" + n +
                " program=\"" + Paths.RsyncExe + "\" enable=yes");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            Process p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            MessageBox.Show(this, o.Length > 0 ? o : "Done.", "Rsync GUI");
        }

        void BrowseInto(TextBox target, bool folder, bool append)
        {
            string path = null;
            if (folder)
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.ShowNewFolderButton = true;
                    if (d.ShowDialog(this) == DialogResult.OK) path = d.SelectedPath;
                }
            }
            else
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.CheckFileExists = false;
                    if (d.ShowDialog(this) == DialogResult.OK) path = d.FileName;
                }
            }
            if (path == null) return;
            if (append)
            {
                string t = target.Text.TrimEnd();
                target.Text = t.Length == 0 ? path : t + "\r\n" + path;
            }
            else target.Text = path;
        }

        void UpdateCmd()
        {
            if (cur == null) { tCmd.Text = ""; return; }
            Job tmp = cur.Clone();
            ReadJob(tmp);
            string txt = Cmd.Preview(tmp, false);
            if (tmp.PlainPassword().Length > 0 || !string.IsNullOrEmpty(tmp.Password)) txt += "\r\n\r\n(RSYNC_PASSWORD is set from the saved daemon password.)";
            tCmd.Text = txt;
        }

        // ---------------------------------------------------------------- schedule and live status

        void FillSched(Job j)
        {
            j.SchedMode = Sched.Modes[Math.Max(0, cSched.SelectedIndex)];
            j.SchedEvery = (int)nEvery.Value;
            j.SchedUnit = cUnit.SelectedIndex == 0 ? "m" : "h";
            j.SchedTime = dtTime.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            List<string> days = new List<string>();
            for (int d = 0; d < 7; d++) if (chDays[d].Checked) days.Add(d.ToString());
            j.SchedDays = string.Join(",", days.ToArray());
            j.CatchUp = cCatch.Checked;
            j.MaxRetries = (int)nRetries.Value;
            j.RetryMinutes = (int)nRetry.Value;
            j.RepeatMinutes = 0;
            j.AutoStart = Sched.On(j);
        }

        void UpdateSchedUi()
        {
            int m = cSched.SelectedIndex;
            bool interval = m == 2, timed = m == 3 || m == 4, weekly = m == 4;
            lSchedWhen.Visible = interval || timed;
            lSchedWhen.Text = interval ? "every" : "at";
            lSchedWhen.Left = interval ? 322 : 330;
            nEvery.Visible = interval; cUnit.Visible = interval; dtTime.Visible = timed;
            foreach (CheckBox c in chDays) c.Visible = weekly;
            cCatch.Enabled = timed;
            Job t = new Job();
            FillSched(t);
            string msg;
            if (m == 0) msg = "This job runs only when you click Run now.";
            else if (m == 1) msg = "This job runs once each time Windows starts.";
            else if (m == 2) msg = "First run when the runner starts, then " + Sched.Describe(t).ToLower() + " after each run ends.";
            else
            {
                DateTime? nx = Sched.NextSlot(t, DateTime.Now);
                msg = nx == null ? "Tick at least one day." : "Next run: " + nx.Value.ToString("dddd d MMMM 'at' HH:mm") + ".";
            }
            lNext.Text = msg;
            lnkBoot.Visible = m != 0 && !taskExists;
        }

        Panel BuildWarnPanel()
        {
            pnlWarn = new Panel(); pnlWarn.Dock = DockStyle.Top; pnlWarn.Height = 50; pnlWarn.Visible = false;
            pnlWarn.BackColor = Color.FromArgb(255, 232, 232);
            lWarn = new Label(); lWarn.Location = new Point(10, 6); lWarn.Size = new Size(760, 40); lWarn.ForeColor = Color.Firebrick;
            lWarn.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bWarn = Btn("Update runner...", 780, 12, 130);
            bWarn.Click += UpdateRunner;
            pnlWarn.Controls.Add(lWarn); pnlWarn.Controls.Add(bWarn);
            pnlWarn.Resize += delegate
            {
                bWarn.Left = pnlWarn.ClientSize.Width - 145;
                lWarn.Width = Math.Max(200, bWarn.Left - 20);
            };
            return pnlWarn;
        }

        // Restarts the background runner from this program, so the runner and the window are the same version.
        void UpdateRunner(object s, EventArgs e)
        {
            try
            {
                string user, pass;
                if (!AskRunAs(out user, out pass)) return;
                SaveAll();
                Cursor = Cursors.WaitCursor;
                BootTask.Remove();
                Thread.Sleep(3000);
                string err = BootTask.Install(user, pass);
                Thread.Sleep(2500);
                Cursor = Cursors.Default;
                taskExists = BootTask.Exists(); runnerActive = JobRun.IsRunning("_runner");
                RefreshAutoStatus();
                if (err != null) MessageBox.Show(this, err, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else if (RunnerInfo.Mismatch(runnerActive) != null)
                    MessageBox.Show(this, "The old runner is still running, so the new one could not start. Restart Windows or stop RsyncGui.exe --runner in Task Manager, then try again.", "Rsync GUI");
                else MessageBox.Show(this, "The background runner now starts from this program (version " + AppInfo.Version + ").", "Rsync GUI");
            }
            catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Rsync GUI", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        Panel BuildRunPanel()
        {
            pnlRun = new Panel(); pnlRun.Dock = DockStyle.Bottom; pnlRun.Height = 122; pnlRun.BorderStyle = BorderStyle.FixedSingle;
            lRunState = new Label(); lRunState.Location = new Point(10, 8); lRunState.AutoSize = true; lRunState.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            pbRun = new ProgressBar(); pbRun.Location = new Point(10, 36); pbRun.Size = new Size(600, 20);
            lRunPct = new Label(); lRunPct.Location = new Point(620, 39); lRunPct.AutoSize = true;
            lRunDetail = new Label(); lRunDetail.Location = new Point(10, 62); lRunDetail.Size = new Size(700, 18); lRunDetail.AutoEllipsis = true;
            lRunFile = new Label(); lRunFile.Location = new Point(10, 82); lRunFile.Size = new Size(700, 18); lRunFile.AutoEllipsis = true;
            lRunNext = new Label(); lRunNext.Location = new Point(10, 100); lRunNext.Size = new Size(700, 18); lRunNext.ForeColor = SystemColors.GrayText;
            pnlRun.Controls.AddRange(new Control[] { lRunState, pbRun, lRunPct, lRunDetail, lRunFile, lRunNext });
            pnlRun.Resize += delegate
            {
                int w = pnlRun.ClientSize.Width;
                pbRun.Width = Math.Max(100, w - 90);
                lRunPct.Left = pbRun.Right + 8;
                lRunDetail.Width = w - 20; lRunFile.Width = w - 20; lRunNext.Width = w - 20;
            };
            return pnlRun;
        }

        static string Dur(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalHours >= 24) return (int)t.TotalDays + " d " + t.Hours + " h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " m";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + " m " + t.Seconds + " s";
            return (int)t.TotalSeconds + " s";
        }

        void SetBar(ProgressBarStyle style)
        {
            if (pbRun.Style == style) return;
            pbRun.Style = style;
            if (style == ProgressBarStyle.Marquee) pbRun.MarqueeAnimationSpeed = 30;
            else pbRun.Value = 0;
        }

        void UpdateRunPanel()
        {
            if (cur == null) { pnlRun.Visible = false; return; }
            pnlRun.Visible = true;
            if (JobRun.IsRunning(cur.Id))
            {
                ProgressInfo pi = cur.Kind == "daemon" ? null : ProgressInfo.Load(cur.Id);
                DateTime st = pi != null ? pi.Started : DateTime.Now;
                lRunState.ForeColor = Color.DarkGreen;
                lRunState.Text = "RUNNING   started " + st.ToString("HH:mm") + "  (" + Dur(DateTime.Now - st) + ")" + (pi != null && pi.Phase.Length > 0 ? "   " + pi.Phase : "");
                if (cur.Kind == "daemon")
                {
                    SetBar(ProgressBarStyle.Continuous); pbRun.Value = 0; lRunPct.Text = "";
                    lRunDetail.Text = "The server is running and waiting for connections."; lRunFile.Text = ""; lRunNext.Text = "";
                    return;
                }
                if (pi != null && pi.Pct >= 0) { SetBar(ProgressBarStyle.Continuous); pbRun.Value = Math.Min(100, pi.Pct); lRunPct.Text = pi.Pct + "%"; }
                else { SetBar(ProgressBarStyle.Marquee); lRunPct.Text = ""; }
                List<string> bits = new List<string>();
                if (pi != null)
                {
                    if (pi.Bytes.Length > 0) bits.Add(pi.Bytes);
                    if (pi.Speed.Length > 0) bits.Add(pi.Speed);
                    if (pi.Eta.Length > 0) bits.Add("time left " + pi.Eta);
                    if (pi.Files.Length > 0) bits.Add(pi.Files);
                }
                lRunDetail.Text = bits.Count > 0 ? string.Join("     ", bits.ToArray()) : "Starting up. The first numbers appear after the folders are scanned.";
                string cf = pi != null && pi.Current.Length > 0 ? "Now: " + pi.Current : "";
                if (pi != null && (DateTime.Now - pi.Updated).TotalSeconds > 90) cf += "     (no news for " + Dur(DateTime.Now - pi.Updated) + ", still working)";
                lRunFile.Text = cf;
                lRunNext.Text = "";
                return;
            }
            SetBar(ProgressBarStyle.Continuous);
            pbRun.Value = 0; lRunPct.Text = "";
            string last = JobRun.LastResult(cur.Id);
            lRunState.ForeColor = last.Length == 0 ? SystemColors.GrayText : (last.Contains("  OK") ? Color.DarkGreen : Color.Firebrick);
            lRunState.Text = last.Length == 0 ? "IDLE   never run" : "IDLE   last run " + last;
            lRunDetail.Text = ""; lRunFile.Text = "";
            string nx = "";
            try
            {
                DateTime due;
                if (File.Exists(Paths.NextFile(cur.Id)) && DateTime.TryParse(File.ReadAllText(Paths.NextFile(cur.Id)).Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out due))
                    nx = "Next run: " + due.ToString("ddd d MMM 'at' HH:mm") + "   (in " + Dur(due - DateTime.Now) + ")";
            }
            catch { }
            if (nx.Length == 0)
            {
                if (!Sched.On(cur)) nx = "Runs only when you click Run now.";
                else if (!taskExists) nx = "Not scheduled: Auto-start at boot is OFF.";
                else if (cur.SchedMode == "start") nx = "Runs each time Windows starts.";
                else nx = "Waiting for the background runner to pick up this schedule (a few seconds after you save).";
            }
            lRunNext.Text = nx;
        }

        void AddCommonExcludes()
        {
            string[] items = { "node_modules/", "*.tmp", "Thumbs.db", "~$*", ".DS_Store" };
            List<string> have = Cmd.Lines(tExc.Text);
            string t = tExc.Text.TrimEnd();
            foreach (string it in items)
            {
                if (have.Contains(it)) continue;
                t += (t.Length == 0 ? "" : "\r\n") + it;
            }
            tExc.Text = t;
        }

        // ---------------------------------------------------------------- timer, log tail, status

        void OnTick(object s, EventArgs e)
        {
            ticks++;
            UpdateStatuses();
            UpdateRunPanel();
            if (tabs.SelectedTab == pgHistory) RefreshHistory(false);
            if (cur != null)
            {
                bool running = JobRun.IsRunning(cur.Id);
                bRun.Enabled = !running; bDry.Enabled = !running && cur.Kind != "daemon"; bStop.Enabled = running;
            }
            if (tabs.SelectedTab == pgLog) TailLog(false);
            if (ticks % 8 == 1)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { taskExists = BootTask.Exists(); runnerActive = JobRun.IsRunning("_runner"); } catch { }
                    try { BeginInvoke((MethodInvoker)RefreshAutoStatus); } catch { }
                });
            }
        }

        void RefreshAlertsLabel()
        {
            bool on = false;
            try { on = AlertsUi.IsOn(); } catch { }
            bAlerts.Text = "Alerts: " + (on ? "ON" : "OFF");
            bAlerts.ForeColor = on ? Color.DarkGreen : Color.Firebrick;
        }

        void RefreshAutoStatus()
        {
            bAuto.Text = "Auto-start at boot: " + (taskExists ? "ON" : "OFF");
            bAuto.ForeColor = taskExists ? Color.DarkGreen : Color.Firebrick;
            string rv, rexe;
            string rvText = runnerActive && RunnerInfo.Read(out rv, out rexe) ? " (version " + rv + ")" : "";
            sAuto.Text = (taskExists ? "Boot task installed" : "Boot task not installed") + "   |   Background runner: " + (runnerActive ? "running" + rvText : "not running");
            string mm = RunnerInfo.Mismatch(runnerActive);
            pnlWarn.Visible = mm != null;
            if (mm != null) lWarn.Text = mm + " It can run jobs the wrong way. Click Update runner to start it from this program.";
            if (cur != null && cSched != null) UpdateSchedUi();
        }

        void TailLog(bool force)
        {
            if (cur == null) return;
            string p = Paths.LogFile(cur.Id);
            if (!File.Exists(p)) { if (tLog.Text.Length > 0) tLog.Text = ""; lastLogLen = -1; lastLogId = cur.Id; return; }
            try
            {
                using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (!force && fs.Length == lastLogLen && lastLogId == cur.Id) return;
                    lastLogLen = fs.Length; lastLogId = cur.Id;
                    long start = Math.Max(0, fs.Length - 200000);
                    fs.Seek(start, SeekOrigin.Begin);
                    byte[] buf = new byte[fs.Length - start];
                    int n = 0;
                    while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
                    string txt = Encoding.UTF8.GetString(buf, 0, n);
                    if (start > 0) { int nl = txt.IndexOf('\n'); if (nl >= 0) txt = txt.Substring(nl + 1); }
                    tLog.Text = txt;
                    tLog.SelectionStart = tLog.TextLength;
                    tLog.ScrollToCaret();
                }
            }
            catch { }
        }

        void OnClosing(object s, FormClosingEventArgs e)
        {
            try
            {
                CommitCurrent();
                if (store.Signature() != savedSnapshot)
                {
                    DialogResult r = MessageBox.Show(this, "Save your changes before closing?", "Rsync GUI", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                    if (r == DialogResult.Yes && !SaveAll()) { e.Cancel = true; return; }
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- test hook: render every tab to PNG

        public void Shots(string dir)
        {
            Directory.CreateDirectory(dir);
            Show();
            Application.DoEvents();
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                OnTick(null, EventArgs.Empty);
                Thread.Sleep(150);
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, string.Format("tab{0:00}-{1}.png", i, tabs.TabPages[i].Text)));
                }
            }
        }
    }
}
