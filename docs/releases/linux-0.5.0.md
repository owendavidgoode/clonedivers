Both interfaces now show **Linux launcher update notices**, and package QA includes
**Ubuntu 26.04** alongside Ubuntu 22.04 and 24.04.

- The desktop checks in the background and shows a banner for a newer launcher.
  **View release** opens its download page; **Check for updates** refreshes manually.
- The terminal dashboard shows the available version. Choose **Launcher updates**
  (`u`) for the release link, browser action, upgrade instructions and manual refresh.
- Notices track Linux releases, including Linux previews, and match the current
  package architecture. Windows releases never trigger a Linux launcher notice.
- Successful checks are cached for six hours; failures retry after fifteen minutes.
  Manual refresh bypasses the cache. Offline checks do not block normal launcher use.
- Updates remain user-installed: close the launcher, then install the new `.deb` or
  replace the portable executable. Existing pack files and settings are preserved.
  Install 0.5.0 once to receive these notices for future releases.

The workflow tests installed packages on Ubuntu 22.04, 24.04 and 26.04, then downloads
and retests the published release on Ubuntu 24.04 and 26.04. Update notices, cache and
failure handling, desktop release links, terminal controls and existing Steam routing,
install/repair/recovery/recording flows are covered.

A player also reported successful gameplay on Ubuntu 26.04. That confirms one real
machine alongside the automated checks; other hardware and Proton combinations
remain subject to playtesting.

[Setup and controls](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.5.0/docs/LINUX.md).
