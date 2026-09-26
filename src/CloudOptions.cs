using System.Collections.Generic;

namespace RsyncGui
{
    // Common rclone flags for cloud jobs (rclone v1.75). Anything not listed goes in "Extra arguments".
    // Flags that only one mode accepts carry that mode in OptDef.Modes.
    public static class CloudOpts
    {
        public const string Group = "Cloud options";

        public static readonly string[] Modes = { "copy", "sync", "move", "bisync" };

        public static readonly string[] ModeLabels =
        {
            "Copy: add new and changed files. Never deletes anything.",
            "Sync: make the destination match the source. Deletes extra files at the destination.",
            "Move: copy, then delete the files from the source.",
            "Two-way (bisync): keep both sides the same. Tick --resync for the first run."
        };

        public static readonly List<OptDef> All = new List<OptDef>();

        static void B(string k, string l, string modes) { OptDef d = new OptDef(Group, k, OT.Bool, l, null); d.Modes = modes; All.Add(d); }
        static void T(string k, string l) { All.Add(new OptDef(Group, k, OT.Text, l, null)); }
        static void C(string k, string l, string modes, params string[] c) { OptDef d = new OptDef(Group, k, OT.Choice, l, c); d.Modes = modes; All.Add(d); }

        public static bool AppliesTo(OptDef d, string mode)
        {
            return d.Modes == null || d.Modes == mode;
        }

        static CloudOpts()
        {
            B("verbose", "Log each transferred file", null);
            B("dry-run", "Trial run, make no changes", null);
            B("checksum", "Compare by checksum, not by size and time", null);
            B("update", "Skip files that are newer on the destination", null);
            B("ignore-existing", "Skip files that already exist on the destination", null);
            B("size-only", "Compare by size only", null);
            B("ignore-times", "Transfer everything, ignore size and time", null);
            B("immutable", "Files never change, report an error if one does", null);
            B("fast-list", "Use fewer API calls (uses more memory)", null);
            B("no-traverse", "Do not list the destination (faster for few files)", null);
            B("check-first", "Check all files before transferring any", null);
            B("create-empty-src-dirs", "Create empty folders at the destination", null);
            B("track-renames", "Detect renamed files instead of uploading again", null);
            B("delete-before", "Sync: delete extra files before transferring", null);
            B("delete-during", "Sync: delete extra files while transferring", null);
            B("delete-after", "Sync: delete extra files after transferring", null);
            B("delete-excluded", "Also delete excluded files at the destination", null);
            B("ignore-errors", "Delete even if there were errors", null);
            B("no-update-modtime", "Do not set modification times on the destination", null);
            B("use-server-modtime", "Use the server's upload time as the modification time", null);
            B("metadata", "Preserve metadata (permissions, times) where supported", null);
            B("copy-links", "Follow symlinks and copy what they point to", null);
            B("skip-links", "Skip symlinks without warning", null);
            B("one-file-system", "Do not cross filesystem boundaries", null);
            B("resync", "Two-way: first run, build the initial state", "bisync");
            B("check-access", "Two-way: stop unless RCLONE_TEST files exist on both sides", "bisync");
            B("force", "Two-way: run even if it would delete a large share of files", "bisync");
            B("resilient", "Two-way: retry after a recoverable error", "bisync");
            B("recover", "Two-way: recover after an interrupted run", "bisync");
            B("remove-empty-dirs", "Two-way: remove empty folders after the run", "bisync");

            T("transfers", "Parallel file transfers (default 4)");
            T("checkers", "Parallel checkers (default 8)");
            T("bwlimit", "Bandwidth limit, for example 5M or 08:00,512k 23:00,off");
            T("tpslimit", "Maximum API transactions per second");
            T("retries", "Retry the whole run this many times (default 3)");
            T("low-level-retries", "Retry a failed API call this many times (default 10)");
            T("timeout", "I/O idle timeout (default 5m)");
            T("contimeout", "Connection timeout (default 1m)");
            T("max-age", "Only files newer than this, for example 30d or 12h");
            T("min-age", "Only files older than this, for example 1d");
            T("max-size", "Skip files larger than this, for example 2G");
            T("min-size", "Skip files smaller than this, for example 10k");
            T("max-delete", "Stop if more than this many files would be deleted");
            T("max-transfer", "Stop after transferring this much data, for example 50G");
            T("max-depth", "Limit the folder depth");
            T("order-by", "Transfer order, for example size,descending");
            T("stats", "Print progress this often, for example 30s");
            T("multi-thread-streams", "Streams per large file download (default 4)");
            T("buffer-size", "Buffer per transfer (default 16M)");
            T("suffix", "Suffix for files that are replaced or deleted (needs backup-dir)");
            T("backup-dir", "Move replaced or deleted files here, for example remote:old");

            C("log-level", "Amount of detail in the log", null, "ERROR", "NOTICE", "INFO", "DEBUG");
            C("conflict-resolve", "Two-way: winner when both sides changed a file", "bisync", "none", "newer", "older", "larger", "smaller", "path1", "path2");
            C("track-renames-strategy", "How renames are detected", null, "hash", "modtime", "leaf");
        }
    }
}
