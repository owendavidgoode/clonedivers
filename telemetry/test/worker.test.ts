import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { DatabaseSync, type SQLInputValue } from 'node:sqlite';
import worker, { type Secrets } from '../src/index.ts';

const ADMIN = 'admin-secret-token-'.repeat(3);
const INVITE = 'invite-secret-token-'.repeat(3);

function createLocalD1(): { db: D1Database; raw: DatabaseSync } {
  const raw = new DatabaseSync(':memory:');
  raw.exec(readFileSync(fileURLToPath(import.meta.resolve('../migrations/0001_reports.sql')), 'utf8'));
  raw.exec(readFileSync(fileURLToPath(import.meta.resolve('../migrations/0002_squad_and_crashes.sql')), 'utf8'));

  const makeStmt = (sql: string, bound: SQLInputValue[] = []) => ({
    bind(...values: unknown[]) {
      return makeStmt(sql, values.map(v => (v === undefined ? null : (v as SQLInputValue))));
    },
    async all<T = Record<string, unknown>>() {
      const rows = raw.prepare(sql).all(...bound) as T[];
      return { results: rows.map(r => ({ ...r })), success: true, meta: {} };
    },
    async first<T = Record<string, unknown>>() {
      const row = raw.prepare(sql).get(...bound) as T | undefined;
      return row ? { ...row } : null;
    },
    async run() {
      const res = raw.prepare(sql).run(...bound);
      return { success: true, meta: { changes: Number(res.changes) } };
    },
  });

  const db = {
    prepare(sql: string) {
      return makeStmt(sql);
    },
  } as unknown as D1Database;

  return { db, raw };
}

function createEnv(overrides: Partial<Secrets> = {}, allowRateLimit = true): { env: Env & Secrets; raw: DatabaseSync } {
  const { db, raw } = createLocalD1();
  const store = new Map<string, ArrayBuffer>();
  const env = {
    DB: db,
    REPORTS: {
      async put(k: string, v: ArrayBuffer) { store.set(k, v); },
      async get(k: string) {
        const b = store.get(k);
        return b ? { body: new Response(b).body } : null;
      },
      async delete(keys: string | string[]) {
        for (const k of Array.isArray(keys) ? keys : [keys]) store.delete(k);
      },
    } as unknown as R2Bucket,
    INGEST_LIMIT: {
      async limit() { return { success: allowRateLimit }; },
    } as unknown as RateLimit,
    ADMIN_TOKEN: ADMIN,
    ENROLLMENT_TOKEN: INVITE,
    ...overrides,
  };
  return { env, raw };
}

test('serves upgraded dashboard UI and requires ADMIN_TOKEN on /admin/squad', async () => {
  const { env } = createEnv();
  const home = await worker.fetch(new Request('https://telemetry.test/'), env);
  assert.equal(home.status, 200);
  const html = await home.text();
  assert.match(html, /Squad Readiness/);
  assert.match(html, /Recent Crashes/);
  assert.match(html, /Detailed Performance Sessions/);
  assert.match(html, /\/admin\/squad/);

  const unauth = await worker.fetch(new Request('https://telemetry.test/admin/squad'), env);
  assert.equal(unauth.status, 401);

  const wrong = await worker.fetch(new Request('https://telemetry.test/admin/squad', {
    headers: { Authorization: 'Bearer ' + 'x'.repeat(40) },
  }), env);
  assert.equal(wrong.status, 401);
});

test('POST /session stores sessions, collapses latest per player in GET /admin/squad, and posts Discord embed', async () => {
  const discordCalls: { url: string; body: Record<string, unknown> }[] = [];
  const origFetch = globalThis.fetch;
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    discordCalls.push({ url: String(input), body: JSON.parse(String(init?.body ?? '{}')) });
    return new Response(null, { status: 204 });
  }) as typeof fetch;

  try {
    const { env, raw } = createEnv({ DISCORD_WEBHOOK_URL: 'https://discord.example/api/webhooks/test' });

    const r1 = await worker.fetch(new Request('https://telemetry.test/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '198.51.100.1' },
      body: JSON.stringify({
        player: 'Cody',
        host_label: 'Squad Host',
        launcher_version: '2.0.2',
        match_hash: 'hash_old_111111',
        mode: 'Clones',
        perf_tier: 'Lighter',
        gpu: 'RTX 3070',
        ram_gb: 32,
        duration_sec: 600,
        exit_code: 0,
        avg_fps: 71.2,
        low1_fps: 49.5,
        status_summary: 'Older run',
      }),
    }), env);
    assert.equal(r1.status, 200);
    // Backdate first row so ordering is deterministic even within the same millisecond
    raw.prepare('UPDATE squad_sessions SET received_utc = ? WHERE launcher_version = ?').run('2026-11-21T10:00:00.000Z', '2.0.2');

    const r2 = await worker.fetch(new Request('https://telemetry.test/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '198.51.100.1' },
      body: JSON.stringify({
        player: 'Cody',
        hostLabel: 'Squad Host',
        launcherVersion: '2.0.3',
        matchHash: 'hash_new_222222',
        mode: 'Empire',
        perfTier: 'Full',
        gpu: 'RTX 3070',
        ramGb: 32,
        durationSec: 1200,
        exitCode: 0,
        avgFps: 88.4,
        low1Fps: 63.0,
        statusSummary: 'Ready for R2',
      }),
    }), env);
    assert.equal(r2.status, 200);

    const r3 = await worker.fetch(new Request('https://telemetry.test/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '198.51.100.2' },
      body: JSON.stringify({
        player: 'Rex',
        launcherVersion: '2.0.3',
        matchHash: 'hash_new_222222',
        mode: 'Empire',
        perfTier: 'reduced512',
        gpu: 'GTX 1070',
        ramGb: 16,
        exitCode: 0,
        avgFps: 61.0,
        low1Fps: 45.2,
      }),
    }), env);
    assert.equal(r3.status, 200);
    assert.equal(discordCalls.length, 3);
    assert.deepEqual(discordCalls[0].body.allowed_mentions, { parse: [] });

    const squadResp = await worker.fetch(new Request('https://telemetry.test/admin/squad', {
      headers: { Authorization: 'Bearer ' + ADMIN },
    }), env);
    assert.equal(squadResp.status, 200);
    const squad = await squadResp.json() as { sessions: Array<{ player: string; launcher_version: string; match_hash: string }>; crashes: unknown[] };
    assert.equal(squad.sessions.length, 2);
    const cody = squad.sessions.find(s => s.player === 'Cody');
    assert.equal(cody?.launcher_version, '2.0.3');
    assert.equal(cody?.match_hash, 'hash_new_222222');
  } finally {
    globalThis.fetch = origFetch;
  }
});

test('POST /crash deduplicates GitHub Issues per (crash_class, launcher_version) within 24h and caps /admin/squad at 25 crashes', async () => {
  const ghCalls: Array<{ title: string; body: string }> = [];
  const discordCalls: unknown[] = [];
  let issueNum = 100;
  const origFetch = globalThis.fetch;
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes('api.github.com')) {
      const parsed = JSON.parse(String(init?.body ?? '{}')) as { title: string; body: string };
      ghCalls.push(parsed);
      issueNum += 1;
      return Response.json({ html_url: `https://github.com/owendavidgoode/clonedivers/issues/${issueNum}` }, { status: 201 });
    }
    discordCalls.push(JSON.parse(String(init?.body ?? '{}')));
    return new Response(null, { status: 204 });
  }) as typeof fetch;

  try {
    const { env } = createEnv({
      GITHUB_TOKEN: 'ghp_test_token_value_for_worker_spec',
      DISCORD_WEBHOOK_URL: 'https://discord.example/api/webhooks/crash',
    });

    // First crash for (OutOfVram, 2.0.3) -> opens Issue #101
    const c1 = await worker.fetch(new Request('https://telemetry.test/crash', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        player: 'Fives',
        launcher_version: '2.0.3',
        match_hash: 'match_abc',
        mode: 'Empire',
        perf_tier: 'Full',
        gpu: 'RTX 2060',
        ram_gb: 16,
        exit_code: 1,
        crash_class: 'OutOfVram',
        summary: 'DXGI device hung during drop',
        log_tail: 'D3D12 error 0x887A0006',
      }),
    }), env);
    assert.equal(c1.status, 200);
    const j1 = await c1.json() as { ok: boolean; githubIssueUrl: string; deduplicated: boolean };
    assert.equal(j1.githubIssueUrl, 'https://github.com/owendavidgoode/clonedivers/issues/101');
    assert.equal(j1.deduplicated, false);
    assert.equal(ghCalls.length, 1);

    // Duplicate (OutOfVram, 2.0.3) within 24h -> NO second GitHub issue, reuses #101!
    const c2 = await worker.fetch(new Request('https://telemetry.test/crash', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        player: 'Echo',
        launcher_version: '2.0.3',
        crash_class: 'OutOfVram',
        exit_code: 1,
        summary: 'Second VRAM hang on same version',
      }),
    }), env);
    assert.equal(c2.status, 200);
    const j2 = await c2.json() as { ok: boolean; githubIssueUrl: string; deduplicated: boolean };
    assert.equal(j2.githubIssueUrl, 'https://github.com/owendavidgoode/clonedivers/issues/101');
    assert.equal(j2.deduplicated, true);
    assert.equal(ghCalls.length, 1);

    // Distinct crash_class -> opens Issue #102
    const c3 = await worker.fetch(new Request('https://telemetry.test/crash', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        player: 'Echo',
        launcher_version: '2.0.3',
        crash_class: 'UnexpectedCrash',
        exit_code: -1073741819,
        summary: 'Unhandled access violation',
      }),
    }), env);
    const j3 = await c3.json() as { githubIssueUrl: string; deduplicated: boolean };
    assert.equal(j3.githubIssueUrl, 'https://github.com/owendavidgoode/clonedivers/issues/102');
    assert.equal(j3.deduplicated, false);
    assert.equal(ghCalls.length, 2);

    // Insert 26 more deduplicated crashes to verify GET /admin/squad caps crashes at 25
    for (let i = 0; i < 26; i++) {
      const r = await worker.fetch(new Request('https://telemetry.test/crash', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          player: `Trooper-${i}`,
          launcher_version: '2.0.3',
          crash_class: 'UnexpectedCrash',
          exit_code: 1,
          summary: `Repeat crash ${i}`,
        }),
      }), env);
      assert.equal(r.status, 200);
    }
    assert.equal(ghCalls.length, 2);
    assert.equal(discordCalls.length, 29);

    const squadResp = await worker.fetch(new Request('https://telemetry.test/admin/squad', {
      headers: { Authorization: 'Bearer ' + ADMIN },
    }), env);
    const squad = await squadResp.json() as { crashes: Array<{ github_issue_url: string }> };
    assert.equal(squad.crashes.length, 25);
    assert.equal(squad.crashes[0].github_issue_url, 'https://github.com/owendavidgoode/clonedivers/issues/102');
  } finally {
    globalThis.fetch = origFetch;
  }
});

test('enforces rate limit 429 and rejects HarmlessQuit on POST /crash', async () => {
  const { env: limitedEnv } = createEnv({}, false);
  const r429 = await worker.fetch(new Request('https://telemetry.test/session', {
    method: 'POST',
    body: JSON.stringify({ player: 'Rex', launcher_version: '2.0.3' }),
  }), limitedEnv);
  assert.equal(r429.status, 429);

  const { env } = createEnv();
  const r400 = await worker.fetch(new Request('https://telemetry.test/crash', {
    method: 'POST',
    body: JSON.stringify({ player: 'Rex', launcher_version: '2.0.3', crash_class: 'HarmlessQuit', summary: 'exit bug' }),
  }), env);
  assert.equal(r400.status, 400);
});

test('dashboard inline script has valid JS syntax, optional webhooks degrade gracefully, and scheduled() prunes >90d rows', async () => {
  const { env, raw } = createEnv();
  const html = await (await worker.fetch(new Request('https://telemetry.test/'), env)).text();
  const scriptMatch = /<script>([\s\S]*?)<\/script>/.exec(html);
  assert.ok(scriptMatch, 'dashboard must contain inline <script>');
  assert.doesNotThrow(() => new Function(scriptMatch[1]));

  // Works without DISCORD_WEBHOOK_URL or GITHUB_TOKEN configured
  const sOk = await worker.fetch(new Request('https://telemetry.test/session', {
    method: 'POST',
    body: JSON.stringify({ player: 'Tech', launcher_version: '2.0.3', exit_code: 0 }),
  }), env);
  assert.equal(sOk.status, 200);

  const cOk = await worker.fetch(new Request('https://telemetry.test/crash', {
    method: 'POST',
    body: JSON.stringify({ player: 'Tech', launcher_version: '2.0.3', crash_class: 'MissingLinuxNetwork', summary: 'pasta missing' }),
  }), env);
  assert.equal(cOk.status, 200);
  const cj = await cOk.json() as { githubIssueUrl: string | null };
  assert.equal(cj.githubIssueUrl, null);

  // Seed >90-day-old rows and verify scheduled() prunes them while keeping fresh rows
  raw.prepare("INSERT INTO squad_sessions(id,received_utc,player,launcher_version,exit_code) VALUES('old1','2025-01-01T00:00:00Z','Old','1.0.0',0)").run();
  raw.prepare("INSERT INTO crash_reports(id,received_utc,player,launcher_version,exit_code,crash_class,summary) VALUES('old2','2025-01-01T00:00:00Z','Old','1.0.0',1,'UnexpectedCrash','old')").run();
  await worker.scheduled({} as ScheduledController, env);

  const squad = await (await worker.fetch(new Request('https://telemetry.test/admin/squad', {
    headers: { Authorization: 'Bearer ' + ADMIN },
  }), env)).json() as { sessions: Array<{ player: string }>; crashes: Array<{ player: string }> };
  assert.deepEqual(squad.sessions.map(s => s.player), ['Tech']);
  assert.deepEqual(squad.crashes.map(c => c.player), ['Tech']);
});

