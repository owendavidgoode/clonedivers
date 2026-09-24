# Automatic performance reports

Status (September 23, 2026): deployed in Goode Works at https://clonedivers-telemetry.goodecraft.com/. The local `1.6.0-preview4` launcher is enrolled as Owen and has uploaded automatically. It temporarily uses the workers.dev alias while the local upstream DNS cache expires. Public launcher 1.5.0/r9 is unchanged.

## Collection

- PresentMon 2.5.1 per-frame numeric metrics, swapchain, rendering API, presentation mode and available frame type. Application/process identity fields are omitted and input tracking is disabled. QPC fields preserve timing; record UTC is reception time.
- Every two seconds: game CPU normalized across logical cores, working set/private memory, handles/threads, cumulative process I/O, foreground status, system CPU, available RAM/load, commit usage/limit, paging, system disk activity/queue and available Windows GPU engine/memory counters.
- At session start and every five minutes: CPU/GPU models, RAM/VRAM capacity, registry driver versions/dates, Windows, desktop display bounds, allowlisted game graphics settings, launcher/pack/game versions, texture profile, mode and Delta/droid/walker selections.
- Launcher session observations and recent pack-operation/error categories.
- At observed game exit: matching HD2 Windows Application events 1000/1001/1002 with selected fault fields, plus timestamps/types/sizes of new HD2 dump files. Binary dump contents are not uploaded.

Missing counters remain missing/null. System counters cover the whole PC. Process I/O is not exclusively disk traffic; shared GPU allocations may be counted more than once. Foreground does not distinguish combat from menus/loading. Temperatures, clocks, fans, network latency, mission/enemy/equipment data and full game/Windows logs are not captured. No process-memory reads, injection, overlays or GameGuard changes. Observed exit does not establish crash cause.

## Player setup

Local setup update: the requested permission retry succeeded, and membership in
Performance Log Users was verified. Sign out of Windows and back in once before
testing automatic FPS capture. The previous canceled attempt made no change.

In Help & diagnostics → Performance sharing, enter a nickname, private collector HTTPS address and squad invitation, then enable sharing. This creates a device-specific upload-only credential. A new invitation can re-enroll a revoked device. Support exports omit credentials; do not share the AppData configuration file.

Use Set up FPS permission once if needed. Windows asks for administrator consent to add the current user to Performance Log Users. Sign out/back in to refresh permissions. The launcher remains unelevated. Keep it open/minimized during play; collection/upload are automatic thereafter. Closing stops observation. Disabling sharing cancels capture/uploads; retained reports are not uploaded while disabled.

## Storage and access

Local reports live in `%APPDATA%\Clonedivers\telemetry`. Atomic JSON chunks have stable random IDs, at most 1,000 records and under 480 KiB. They flush at least every 20 seconds. Queue limit: 512 MiB or 14 days, expiring oldest records. A hard kill can lose the unflushed tail. PresentMon uses a device-specific ETW session and one-hour bounded runs, restarting for longer games. Abrupt launcher termination can leave that recorder/session until timeout or the next capture cleanup.

HTTPS uploads use gzip, refuse redirects, retry with backoff and delete local reports only after an acknowledgment identifies the exact chunk. Invalid/conflicting entries are quarantined within the same storage/age limit. Offline/upload failure never blocks game launch. Collector errors never fabricate FPS.

Cloudflare Worker authenticates per-device writes, validates bounded decompressed reports, supports exact retries and rejects conflicting IDs. Limits: 120 ingestion requests/minute per device, 4 GiB uncompressed/device/day, 100 enrolled installations. Separate owner key protects dashboard/read/revoke routes; it never ships to friends. R2 stores compressed detail, D1 indexes sessions. Hourly cleanup deletes indexed reports older than 90 days. A 90-day R2 lifecycle rule is active to cover orphaned objects too.

Dashboard: newest 100 sessions, memory timeline, hardware/settings, event records and interval statistics. Largest swapchain is selected, with FPS = 1,000 / mean interval and nearest-rank percentiles; not claimed as displayed FPS. Browser detail caps at 250,000 records, while server chunks remain downloadable. `telemetry/test/export-session.mts` exports NDJSON using an OWNER_TOKEN environment variable, never a token in a URL/argument. Compare matched settings/gameplay on the same PC. No remote command execution or silent graphics rewrites.

## Validation

Native launcher tests and 12 diagnostics checks pass. New checks cover frame parsing, schema, HTTPS destinations, stable IDs, chunk/queue bounds and disabled sharing. Four Worker schema tests pass, including bounded gzip expansion. Local Wrangler integration passes authentication rejection, enrollment, compressed upload/download, retry, conflict, session index and revocation. Types and Worker dry-run bundle pass.

Windows sampler smoke against its own process returned 23 resource fields (20 available), 50 context fields and two recent matching HD2 crash events. First sample: 9.67 ms; next: 3.18 ms on this developer PC. This does not measure weak-PC or per-frame capture overhead. Production HTTPS validation passed using the actual C# client: enrollment, compressed upload, duplicate retry, private readback and revocation. Custom-domain TLS/health passed against its public IP. The FPS-permission UAC prompt was canceled, so no Windows group change occurred. Live-game frame capture and weak-PC overhead remain open.

Build prerequisite: `tools/fetch-presentmon.ps1` retrieves/hash-checks the official 2.5.1 x64 binary. Its MIT license is embedded and extracted beside the recorder. Cloudflare setup instructions are in `telemetry/README.md`.

References: https://github.com/GameTechDev/PresentMon and https://developers.cloudflare.com/workers/
