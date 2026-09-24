# Clonedivers private telemetry

See [collection and limitations](../docs/AUTOMATIC-PERFORMANCE-REPORTS.md).

## Local development

Node 24 and pnpm 11.25.0. Run `pnpm install`. Create ignored `.dev.vars` with
`ADMIN_TOKEN` and `ENROLLMENT_TOKEN` (each at least 32 characters), then:

```powershell
pnpm exec wrangler types
pnpm exec tsc --noEmit
pnpm test
pnpm exec wrangler d1 migrations apply clonedivers-telemetry --local
pnpm dev
```

`test/integration.mts` targets localhost:8787 and uses fixed local-test-only keys.
It creates/revokes test devices; never point it at production.

## Deployment

Deployed September 23, 2026 in Goode Works at
`https://clonedivers-telemetry.goodecraft.com/`. Production client upload/retry,
private reads and revocation pass. The following steps describe reproduction;
reuse the existing dedicated resources and preserve production secrets on updates.

1. Run `wrangler login` or `login --device`, then `wrangler whoami`.
2. Create dedicated D1 database `clonedivers-telemetry`; replace the placeholder
   ID in `wrangler.jsonc`. Select the account if more than one is available.
3. Create private R2 bucket `clonedivers-telemetry`, with a 90-day object-expiry
   lifecycle rule for orphaned objects. If R2 activation requires billing
   enrollment, have the owner complete that account setup.
4. Apply migrations with `--remote`.
5. Generate separate random 256-bit owner/invitation secrets and store them via
   `wrangler secret put ADMIN_TOKEN` / `ENROLLMENT_TOKEN`, using stdin rather than
   command arguments. Preserve them outside this repo. Never deploy local test keys.
6. Run `wrangler deploy --dry-run`, then `wrangler deploy`. Missing secrets fail
   authentication closed. The local `.dev.vars` file is not production configuration.
7. Verify unauthenticated admin reads fail, enroll one test client, upload/read a
   report, and test revocation. Configure the launcher endpoint and share the
   invitation privately. No owner key, Cloudflare API credential or shared invite
   belongs in the public launcher/release.

The public `/` page contains only the login shell. The owner token stays in page
memory; `/admin/*` requires owner authentication. Device tokens only upload.
There is no remote-control/command API.

On Owen's PC, `tools/open-performance-dashboard.ps1` copies the DPAPI-protected
owner key to the clipboard and opens the dashboard. `tools/copy-performance-invite.ps1`
copies private squad enrollment instructions. Neither prints keys or sends messages.
