Linux terminal launcher preview with local support reports and optional session recording.

- Discover Ubuntu Snap Steam and its external libraries, in addition to native/Flatpak Steam.
- Simplify extraction instructions to avoid a missing `tar -C` destination.

- **Save support report** / `report`: export pack, build, options, system details,
  recent launcher errors and available Proton/MangoHud log excerpts to a ZIP.
- **Session recording** / `record-setup`: print a one-time Steam launch wrapper.
- `record`: arm MangoHud and Proton logging for the next Steam launch only.
- `record-status`: inspect the capture result and log location; missing CSVs are
  explicitly reported. `record-cancel` cancels a pending request.
- Ordinary launches preserve the original command and environment. No automatic
  uploads, package installation, FPS caps or rendering changes.

Download **Clonedivers-linux-x64.tar.gz** for a typical Linux PC. No separate .NET
runtime is needed. MangoHud's Vulkan layer is an optional dependency for performance
recording. Its Flatpak extension must match the Steam runtime when using Flatpak.
Recording targets desktop sessions; Gamescope/Deck Gaming Mode needs separate
mangoapp integration. Existing install/switch/repair/recovery behavior is retained.

[Setup and recording instructions](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.2.0/docs/LINUX.md).

Reports are local and text redaction is best-effort; review a ZIP before sharing it.
Log excerpts are bounded. Complete opt-in recordings remain in the game library until
you remove them. Actual modded Proton gameplay remains a player playtest requirement.
