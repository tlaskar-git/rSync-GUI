# Changelog

## 1.4.0 (2026-09-26)
- Run history. A **History** tab on every job lists its last 100 runs with the start time, how long it took, the amount of data, the number of files, the result, the number of errors and how it started (scheduled, manual or command line).
- A summary line above the list: OK and failed counts, the average time of an OK run and the last success.
- Double-click a run for its details. **Save as CSV...** exports the list. **Clear history** empties it.
- Every run is recorded, including runs that fail before they start because of a configuration mistake.
- Deleting a job and saving now also deletes its log, history and state files.
- README: a full install guide (download link that always points at the latest release, checksum check, unblock, unzip, first start, scripted install, a first-run test), an uninstall section and the zip layout. Each release now also carries a `RsyncGui.zip` with a fixed name.

## 1.3.0 (2026-09-26)
- Failure alerts. Email, Slack, Microsoft Teams, Discord or a plain JSON web hook, plus the Windows Event Log, when a scheduled job fails after its retries, works again, or has not succeeded for too long.
- One alert per failure streak, then a reminder every 24 hours. Manual runs never alert.
- An **Alerts...** button on the toolbar with a **Send test alert** button, and a **Send alerts for this job** tick on every job.
- The email password and the web hook URL are stored encrypted for this machine.
- The window is taller to fit the new setting.

## 1.2.2 (2026-09-26)
- Version check. The background runner records its version. A red banner with an **Update runner...** button appears in the window when the runner is a different version.
- The job list records the version that saved it. A program refuses a job list saved by a newer version.
- A job with a type the program does not know is stopped with a clear message and is not retried. It used to be run as an rsync job.
- The status bar shows the runner's version.
- The GUI source is now under the MIT licence.

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