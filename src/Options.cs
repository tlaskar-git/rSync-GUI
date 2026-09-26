using System.Collections.Generic;

namespace RsyncGui
{
    public enum OT { Bool, Text, File, Dir, Choice }

    public class OptDef
    {
        public string Group, Key, Label;
        public OT Type;
        public string[] Choices;
        public string Modes;   // null = every mode, otherwise the only rclone mode that accepts it

        public OptDef(string g, string k, OT t, string l, string[] c)
        {
            Group = g; Key = k; Type = t; Label = l; Choices = c;
        }
    }

    // Every long option from "rsync --help" (v3.5.1) that takes a plain flag or a single value.
    // Anything not listed here can go in the "Extra arguments" box on the Job tab.
    public static class OptTable
    {
        public static readonly string[] Groups =
            { "Basic", "Attributes", "Transfer", "Delete", "Compression", "File lists", "Remote", "Output" };

        // The only options "rsync --daemon" accepts on its command line.
        public static readonly string[] DaemonKeys =
            { "address", "bwlimit", "ipv4", "ipv6", "port", "log-file", "log-file-format", "sockopts", "verbose" };

        public static readonly List<OptDef> All = new List<OptDef>();

        static void B(string g, string k, string l) { All.Add(new OptDef(g, k, OT.Bool, l, null)); }
        static void T(string g, string k, string l) { All.Add(new OptDef(g, k, OT.Text, l, null)); }
        static void F(string g, string k, string l) { All.Add(new OptDef(g, k, OT.File, l, null)); }
        static void D(string g, string k, string l) { All.Add(new OptDef(g, k, OT.Dir, l, null)); }
        static void C(string g, string k, string l, params string[] c) { All.Add(new OptDef(g, k, OT.Choice, l, c)); }

        static OptTable()
        {
            string g = "Basic";
            B(g, "verbose", "Increase verbosity");
            B(g, "quiet", "Suppress non-error messages");
            B(g, "archive", "Archive mode (same as recursive, links, perms, times, group, owner, devices, specials)");
            B(g, "recursive", "Recurse into directories");
            B(g, "dirs", "Transfer directories without recursing");
            B(g, "relative", "Use relative path names");
            B(g, "no-implied-dirs", "Do not send implied directories with relative");
            B(g, "mkpath", "Create the destination's missing path components");
            B(g, "update", "Skip files that are newer on the receiver");
            B(g, "checksum", "Skip based on checksum, not mod-time and size");
            B(g, "compress", "Compress file data during the transfer");
            B(g, "dry-run", "Trial run, make no changes");
            B(g, "prune-empty-dirs", "Prune empty directory chains from the file list");
            B(g, "one-file-system", "Do not cross filesystem boundaries");
            B(g, "list-only", "List the files instead of copying them");
            B(g, "stats", "Give some file-transfer stats");
            B(g, "human-readable", "Output numbers in a human-readable format");
            B(g, "progress", "Show progress during transfer");
            B(g, "itemize-changes", "Output a change summary for all updates");

            g = "Attributes";
            B(g, "links", "Copy symlinks as symlinks");
            B(g, "copy-links", "Transform symlink into referent file/directory");
            B(g, "copy-unsafe-links", "Only unsafe symlinks are transformed");
            B(g, "safe-links", "Ignore symlinks that point outside the tree");
            B(g, "munge-links", "Munge symlinks to make them safe and unusable");
            B(g, "copy-dirlinks", "Transform symlink to directory into referent directory");
            B(g, "keep-dirlinks", "Treat symlinked directory on receiver as directory");
            B(g, "insecure-links", "Follow attacker-owned symlinks in operator paths");
            B(g, "hard-links", "Preserve hard links");
            B(g, "perms", "Preserve permissions");
            B(g, "executability", "Preserve executability");
            B(g, "acls", "Preserve ACLs (implies perms)");
            B(g, "xattrs", "Preserve extended attributes");
            B(g, "owner", "Preserve owner (super-user only)");
            B(g, "group", "Preserve group");
            B(g, "devices", "Preserve device files (super-user only)");
            B(g, "specials", "Preserve special files");
            B(g, "copy-devices", "Copy device contents as a regular file");
            B(g, "write-devices", "Write to devices as files (implies inplace)");
            B(g, "drop-D", "Receiver refuses to create devices/specials");
            B(g, "times", "Preserve modification times");
            B(g, "atimes", "Preserve access (use) times");
            B(g, "open-noatime", "Avoid changing the atime on opened files");
            B(g, "crtimes", "Preserve create times (newness)");
            B(g, "omit-dir-times", "Omit directories from times");
            B(g, "omit-link-times", "Omit symlinks from times");
            B(g, "super", "Receiver attempts super-user activities");
            B(g, "fake-super", "Store/recover privileged attributes using xattrs");
            B(g, "numeric-ids", "Do not map uid/gid values by user/group name");
            B(g, "sparse", "Turn sequences of nulls into sparse blocks");
            B(g, "preallocate", "Allocate dest files before writing them");
            T(g, "chmod", "Affect file and/or directory permissions (CHMOD)");
            T(g, "usermap", "Custom username mapping");
            T(g, "groupmap", "Custom groupname mapping");
            T(g, "chown", "Simple username/groupname mapping (USER:GROUP)");
            T(g, "iconv", "Request charset conversion of filenames");
            T(g, "copy-as", "Specify user and optional group for the copy");
            D(g, "confine-root", "Refuse operator paths resolving outside DIR");

            g = "Transfer";
            B(g, "whole-file", "Copy files whole (without the delta-xfer algorithm)");
            B(g, "no-whole-file", "Use the delta-xfer algorithm");
            B(g, "inplace", "Update destination files in-place");
            B(g, "append", "Append data onto shorter files");
            B(g, "append-verify", "Append with old data in the file checksum");
            B(g, "partial", "Keep partially transferred files");
            B(g, "delay-updates", "Put all updated files into place at the end");
            B(g, "fuzzy", "Find similar file for basis if no dest file");
            B(g, "existing", "Skip creating new files on receiver");
            B(g, "ignore-non-existing", "Skip creating new files on receiver (alias)");
            B(g, "ignore-existing", "Skip updating files that exist on receiver");
            B(g, "remove-source-files", "Sender removes synchronized files (non-directory)");
            B(g, "ignore-times", "Do not skip files that match size and time");
            B(g, "size-only", "Skip files that match in size");
            B(g, "backup", "Make backups (see suffix and backup-dir)");
            B(g, "inc-recursive", "Enable incremental recursion");
            B(g, "no-inc-recursive", "Disable incremental recursion");
            B(g, "fsync", "Fsync every written file");
            T(g, "bwlimit", "Limit socket I/O bandwidth (RATE)");
            T(g, "timeout", "I/O timeout in seconds");
            T(g, "contimeout", "Daemon connection timeout in seconds");
            T(g, "stop-after", "Stop rsync after MINS minutes have elapsed");
            T(g, "stop-at", "Stop rsync at the given point in time (y-m-dTh:m)");
            T(g, "max-size", "Do not transfer any file larger than SIZE");
            T(g, "min-size", "Do not transfer any file smaller than SIZE");
            T(g, "max-alloc", "Change a limit relating to memory alloc (SIZE)");
            T(g, "block-size", "Force a fixed checksum block-size");
            T(g, "modify-window", "Accuracy for mod-time comparisons (NUM)");
            T(g, "checksum-seed", "Set block/file checksum seed (advanced)");
            T(g, "partial-dir", "Put a partially transferred file into DIR");
            D(g, "temp-dir", "Create temporary files in directory DIR");
            D(g, "backup-dir", "Make backups into hierarchy based in DIR");
            T(g, "suffix", "Backup suffix (default ~ without backup-dir)");
            D(g, "compare-dest", "Also compare destination files relative to DIR");
            D(g, "copy-dest", "... and include copies of unchanged files");
            D(g, "link-dest", "Hardlink to files in DIR when unchanged");
            C(g, "checksum-choice", "Choose the checksum algorithm", "auto", "xxh128", "xxh3", "xxh64", "md5", "md4", "none");

            g = "Delete";
            B(g, "delete", "Delete extraneous files from dest directories");
            B(g, "del", "Alias for delete-during");
            B(g, "delete-before", "Receiver deletes before transfer, not during");
            B(g, "delete-during", "Receiver deletes during the transfer");
            B(g, "delete-delay", "Find deletions during, delete after");
            B(g, "delete-after", "Receiver deletes after transfer, not during");
            B(g, "delete-excluded", "Also delete excluded files from dest directories");
            B(g, "delete-missing-args", "Delete missing source arguments from destination");
            B(g, "ignore-missing-args", "Ignore missing source arguments without error");
            B(g, "ignore-errors", "Delete even if there are I/O errors");
            B(g, "force", "Force deletion of directories even if not empty");
            T(g, "max-delete", "Do not delete more than NUM files");

            g = "Compression";
            C(g, "compress-choice", "Choose the compression algorithm", "zstd", "lz4", "zlibx", "zlib", "none");
            T(g, "compress-level", "Explicitly set compression level (NUM)");
            T(g, "compress-threads", "Explicitly set compression threads (NUM)");
            T(g, "skip-compress", "Do not compress files with suffix in LIST (e.g. gz/zip/7z)");

            g = "File lists";
            B(g, "cvs-exclude", "Auto-ignore files in the same way CVS does");
            B(g, "from0", "All *-from/filter files are delimited by 0s");
            F(g, "exclude-from", "Read exclude patterns from FILE");
            F(g, "include-from", "Read include patterns from FILE");
            F(g, "files-from", "Read list of source-file names from FILE");

            g = "Remote";
            B(g, "secluded-args", "Use the protocol to safely send the arguments");
            B(g, "old-args", "Disable the modern argument-protection idiom");
            B(g, "trust-sender", "Trust the remote sender's file list");
            B(g, "blocking-io", "Use blocking I/O for the remote shell");
            B(g, "no-motd", "Suppress daemon-mode MOTD");
            B(g, "ipv4", "Prefer IPv4");
            B(g, "ipv6", "Prefer IPv6");
            T(g, "rsync-path", "Rsync program to run on the remote machine");
            T(g, "rsh", "Remote shell command (replaces the built-in SSH setup)");
            T(g, "remote-option", "Send OPTION to the remote side only");
            T(g, "port", "Alternate daemon port (also the port for a daemon job)");
            T(g, "address", "Bind address for outgoing socket to daemon");
            T(g, "sockopts", "Custom TCP options");
            T(g, "protocol", "Force an older protocol version (NUM)");
            F(g, "password-file", "Read daemon-access password from FILE");
            F(g, "early-input", "Use FILE for daemon's early exec input");

            g = "Output";
            T(g, "info", "Fine-grained informational verbosity (FLAGS)");
            T(g, "debug", "Fine-grained debug verbosity (FLAGS)");
            C(g, "stderr", "Change stderr output mode", "errors", "all", "client");
            C(g, "outbuf", "Set out buffering to None, Line, or Block", "N", "L", "B");
            B(g, "8-bit-output", "Leave high-bit chars unescaped in output");
            T(g, "out-format", "Output updates using the specified FORMAT");
            F(g, "log-file", "Log what we are doing to the specified FILE");
            T(g, "log-file-format", "Log updates using the specified FMT");
            F(g, "write-batch", "Write a batched update to FILE");
            F(g, "only-write-batch", "Like write-batch but without updating dest");
            F(g, "read-batch", "Read a batched update from FILE");
        }
    }
}
