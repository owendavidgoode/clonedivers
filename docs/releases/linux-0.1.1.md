QA update for the native Linux terminal launcher preview.

- Repair restores an active pack even when every active patch was deleted.
- Vanilla selection remains parked after repair even when every parked patch was deleted.
- Recovery targets are scoped to each game installation, preserving unfinished 0.1.0 updates.
- Repair and finish reject queued Steam updates and show untested-build prompts.
- A game build changing during download/preparation stops the install for a new compatibility review.
- Packaged executable checks now cover both Ubuntu 22.04 and 24.04; published downloads
  are downloaded again and checked for hashes, archive contents, architecture and behavior.

Download **Clonedivers-linux-x64.tar.gz** for a typical Linux PC or Steam Deck.
Extract it, open a terminal in that folder, and run `chmod +x clonedivers` followed
by `./clonedivers`. No separate .NET runtime is required.

[Setup and commands](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.1.1/docs/LINUX.md).

Actual Steam/Proton gameplay remains unverified. Windows performance sharing and
launcher self-update are unavailable in this preview.
