using System;
using System.Collections.Generic;
using System.Globalization;

namespace RsyncGui
{
    // Schedule rules. Times are local time. Modes: off, start, interval, daily, weekly.
    public static class Sched
    {
        public static readonly string[] Modes = { "off", "start", "interval", "daily", "weekly" };

        public static readonly string[] ModeLabels =
        {
            "Only when I click Run now",
            "Once each time Windows starts",
            "Repeat every ...",
            "Every day at ...",
            "On chosen days at ..."
        };

        static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public static bool On(Job j)
        {
            return j.SchedMode == "start" || j.SchedMode == "interval" || j.SchedMode == "daily" || j.SchedMode == "weekly";
        }

        public static TimeSpan TimeOfDay(Job j)
        {
            TimeSpan t;
            if (!TimeSpan.TryParse(j.SchedTime ?? "", CultureInfo.InvariantCulture, out t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))
                t = new TimeSpan(2, 0, 0);
            return t;
        }

        public static TimeSpan Interval(Job j)
        {
            int n = Math.Max(1, j.SchedEvery);
            return j.SchedUnit == "m" ? TimeSpan.FromMinutes(n) : TimeSpan.FromHours(n);
        }

        static bool DayOk(Job j, DateTime d)
        {
            if (j.SchedMode != "weekly") return true;
            string want = ((int)d.DayOfWeek).ToString();
            foreach (string p in (j.SchedDays ?? "").Split(',')) if (p.Trim() == want) return true;
            return false;
        }

        // Next daily/weekly slot strictly after 'after'. Null when no day is chosen.
        public static DateTime? NextSlot(Job j, DateTime after)
        {
            TimeSpan t = TimeOfDay(j);
            for (int d = 0; d <= 8; d++)
            {
                DateTime c = after.Date.AddDays(d) + t;
                if (c > after && DayOk(j, c)) return c;
            }
            return null;
        }

        // Most recent daily/weekly slot at or before 'at'.
        public static DateTime? PrevSlot(Job j, DateTime at)
        {
            TimeSpan t = TimeOfDay(j);
            for (int d = 0; d <= 8; d++)
            {
                DateTime c = at.Date.AddDays(-d) + t;
                if (c <= at && DayOk(j, c)) return c;
            }
            return null;
        }

        // How long the schedule normally leaves between runs. Null when there is no regular gap.
        public static TimeSpan? Period(Job j)
        {
            switch (j.SchedMode)
            {
                case "interval": return Interval(j);
                case "daily": return TimeSpan.FromDays(1);
                case "weekly":
                    List<int> days = new List<int>();
                    foreach (string p in (j.SchedDays ?? "").Split(','))
                    {
                        int d;
                        if (int.TryParse(p.Trim(), out d) && d >= 0 && d <= 6 && !days.Contains(d)) days.Add(d);
                    }
                    if (days.Count == 0) return null;
                    if (days.Count == 1) return TimeSpan.FromDays(7);
                    days.Sort();
                    int widest = 0;
                    for (int i = 0; i < days.Count; i++)
                    {
                        int next = i + 1 < days.Count ? days[i + 1] : days[0] + 7;
                        widest = Math.Max(widest, next - days[i]);
                    }
                    return TimeSpan.FromDays(widest);
                default: return null;
            }
        }

        public static string Describe(Job j)
        {
            switch (j.SchedMode)
            {
                case "start": return "At Windows start";
                case "interval":
                    int n = Math.Max(1, j.SchedEvery);
                    if (j.SchedUnit == "m") return n == 1 ? "Every minute" : "Every " + n + " minutes";
                    return n == 1 ? "Every hour" : "Every " + n + " hours";
                case "daily": return "Daily at " + TimeOfDay(j).ToString(@"hh\:mm");
                case "weekly":
                    List<string> names = new List<string>();
                    int[] order = { 1, 2, 3, 4, 5, 6, 0 };
                    foreach (int d in order)
                        foreach (string p in (j.SchedDays ?? "").Split(',')) if (p.Trim() == d.ToString()) names.Add(DayNames[d]);
                    if (names.Count == 7) return "Daily at " + TimeOfDay(j).ToString(@"hh\:mm");
                    if (names.Count == 0) return "No days chosen";
                    return string.Join(", ", names.ToArray()) + " " + TimeOfDay(j).ToString(@"hh\:mm");
                default: return "Manual";
            }
        }

        // Old jobs used AutoStart plus "repeat every N minutes".
        public static void Migrate(Job j)
        {
            if (string.IsNullOrEmpty(j.SchedMode))
            {
                if (!j.AutoStart) j.SchedMode = "off";
                else if (j.RepeatMinutes > 0)
                {
                    j.SchedMode = "interval";
                    if (j.RepeatMinutes % 60 == 0) { j.SchedUnit = "h"; j.SchedEvery = j.RepeatMinutes / 60; }
                    else { j.SchedUnit = "m"; j.SchedEvery = j.RepeatMinutes; }
                }
                else j.SchedMode = "start";
            }
            if (j.SchedEvery < 1) j.SchedEvery = 1;
            if (j.SchedUnit != "m" && j.SchedUnit != "h") j.SchedUnit = "h";
            if (string.IsNullOrEmpty(j.SchedTime)) j.SchedTime = "02:00";
            if (string.IsNullOrEmpty(j.SchedDays)) j.SchedDays = "0,1,2,3,4,5,6";
            if (j.MaxRetries < 0) j.MaxRetries = 0;
            j.AutoStart = On(j);
        }
    }
}
