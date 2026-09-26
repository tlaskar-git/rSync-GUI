# Changelog

## Unreleased
- The GUI source is now under the MIT licence (see LICENSE). The release zip will include it as licenses\RsyncGui-LICENSE.txt.

## 1.2.1 (2026-09-25)
- Each job log is capped at 20 MB. The start line, end line and result are always recorded.
- The runner's own log resets at 1 MB.

## 1.2.0 (2026-09-25)
- Scheduler for every job: at Windows start, every N minutes or hours, daily at a time, or on chosen days.
  Missed runs are made up when Windows starts. Failed runs retry a set number of times.
- Live progress panel with a progress bar, speed, time left, file count and current files. The job list shows the percentage of running jobs.
- An rsync job that points at a cloud account is caught with a clear message and is not retried.
- Log noise is kept down: progress lines stay out of the log, errors and symbolic link notices are counted and capped.
- `skip-links` is on by default for new cloud jobs. The Patterns tab has an "Add common excludes" button.

## 1.1.0 (2026-09-24)
- Cloud jobs with rclone: OneDrive, Google Drive, Dropbox, Box, S3, Azure, Backblaze, SFTP, WebDAV and about 60 more.
- Cloud account setup and sign-in inside the GUI, a cloud folder browser, and copy, sync, move and two-way modes.

## 1.0.0 (2026-09-24)
- Windows front end for the real rsync 3.5.1 (built from the official source under Cygwin) with Cygwin ssh.
- Every rsync option that takes a flag or one value, live command preview, dry run, log view.
- Server jobs (`rsync --daemon`) and a boot task that restarts jobs after every reboot.