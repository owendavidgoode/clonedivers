import { timingSafeEqual } from 'node:crypto';
import { boundedBody, type CrashReportInput, ID, MAX_SQUAD_BYTES, type SquadSessionInput, validate, validateCrash, validateSession } from './schema.ts';
import { dashboard } from './ui.ts';

export type Secrets = {
  ADMIN_TOKEN: string;
  ENROLLMENT_TOKEN: string;
  DISCORD_WEBHOOK_URL?: string;
  GITHUB_TOKEN?: string;
};
type Device = { id: string; token_hash: string; revoked: number };

const GITHUB_ISSUES_URL = 'https://api.github.com/repos/owendavidgoode/clonedivers/issues';
const GITHUB_ISSUE_URL_RE = /^https:\/\/github\.com\/owendavidgoode\/clonedivers\/issues\/\d+$/;
const HOURLY_ISSUE_LIMIT = 5;

const json = (v: unknown, status = 200): Response => Response.json(v, { status, headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' } });

async function hash(value: string | Uint8Array): Promise<string> {
  const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : value;
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(b => b.toString(16).padStart(2, '0')).join('');
}

async function matches(a: string, b: string | undefined): Promise<boolean> {
  if (!b || b.length < 32 || a.length > 256) return false;
  const x = new TextEncoder().encode(await hash(a)); const y = new TextEncoder().encode(await hash(b));
  return typeof crypto.subtle.timingSafeEqual === 'function' ? crypto.subtle.timingSafeEqual(x, y) : timingSafeEqual(x, y);
}

function escapeGitHubMentions(s: string): string {
  return s.replace(/@([a-zA-Z0-9_-]+)/g, '@\u200b$1');
}

function fmtFpsPair(avg: number | null, low1: number | null): string {
  if (avg === null && low1 === null) return '—';
  return `${avg !== null ? avg.toFixed(1) : '—'} / ${low1 !== null ? low1.toFixed(1) : '—'} FPS`;
}

async function postDiscordWebhook(webhookUrl: string | undefined, embed: Record<string, unknown>): Promise<void> {
  if (!webhookUrl || !webhookUrl.startsWith('https://')) return;
  try {
    await fetch(webhookUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ embeds: [embed], allowed_mentions: { parse: [] } }),
      signal: AbortSignal.timeout(4000),
    });
  } catch {
    console.warn(JSON.stringify({ event: 'discord-webhook-failed' }));
  }
}

async function createCrashGitHubIssue(githubToken: string, c: CrashReportInput, reportId: string, receivedUtc: string): Promise<string | null> {
  const safeTail = escapeGitHubMentions(c.log_tail.replaceAll('```', "'''")).slice(0, 4000);
  const bodyLines = [
    `### Automated Squad Crash Report (\`${c.crash_class}\`)`,
    '',
    '| Field | Value |',
    '| --- | --- |',
    `| **Report ID** | \`${reportId}\` |`,
    `| **Received (UTC)** | \`${receivedUtc}\` |`,
    `| **Launcher Version** | \`${c.launcher_version}\` |`,
    `| **Crash Class** | \`${c.crash_class}\` |`,
    `| **Exit Code** | \`${c.exit_code}\` |`,
    `| **Player** | ${escapeGitHubMentions(c.player)} |`,
    `| **Mode / Tier** | ${escapeGitHubMentions([c.mode, c.perf_tier].filter(Boolean).join(' · ') || '—')} |`,
    `| **Match Hash** | ${c.match_hash ? `\`${c.match_hash}\`` : '—'} |`,
    `| **GPU / RAM** | ${escapeGitHubMentions([c.gpu, c.ram_gb !== null ? `${c.ram_gb} GB` : ''].filter(Boolean).join(' · ') || '—')} |`,
    '',
    '#### Summary',
    escapeGitHubMentions(c.summary),
  ];
  if (safeTail) {
    bodyLines.push('', '#### Log Tail', '```text', safeTail, '```');
  }
  bodyLines.push('', '_Deduplicated per `(crash_class, launcher_version)` over a 24h window by `clonedivers-telemetry`._');

  try {
    const resp = await fetch(GITHUB_ISSUES_URL, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${githubToken}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'Clonedivers-Telemetry-Worker/1.0',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        title: `[Crash] ${c.crash_class} (${c.launcher_version}): ${c.summary.slice(0, 90)}`,
        body: bodyLines.join('\n'),
        labels: ['crash-report'],
      }),
      signal: AbortSignal.timeout(4500),
    });
    if (!resp.ok) {
      console.warn(JSON.stringify({ event: 'github-issue-failed', status: resp.status }));
      return null;
    }
    const created: unknown = await resp.json();
    if (created && typeof created === 'object' && 'html_url' in created && typeof created.html_url === 'string' && GITHUB_ISSUE_URL_RE.test(created.html_url)) {
      return created.html_url;
    }
  } catch {
    console.warn(JSON.stringify({ event: 'github-issue-error' }));
  }
  return null;
}

function buildSessionEmbed(s: SquadSessionInput, receivedUtc: string): Record<string, unknown> {
  const fields = [
    { name: 'Mode / Tier', value: [s.mode, s.perf_tier].filter(Boolean).join(' · ') || '—', inline: true },
    { name: 'Match Hash', value: s.match_hash ? `\`${s.match_hash.slice(0, 12)}\`` : '—', inline: true },
    { name: 'Avg / 1% Low', value: fmtFpsPair(s.avg_fps, s.low1_fps), inline: true },
    { name: 'Exit / Duration', value: `Exit ${s.exit_code}` + (s.duration_sec !== null ? ` (${Math.round(s.duration_sec / 60)}m)` : ''), inline: true },
    { name: 'GPU / RAM', value: [s.gpu, s.ram_gb !== null ? `${s.ram_gb} GB` : ''].filter(Boolean).join(' · ') || '—', inline: true },
  ];
  if (s.host_label) fields.push({ name: 'Host', value: s.host_label, inline: true });
  return {
    title: `Squad Session · ${s.player} (${s.launcher_version})`,
    description: s.status_summary || undefined,
    color: s.exit_code === 0 ? 0x2ecc71 : 0xf39c12,
    fields,
    timestamp: receivedUtc,
  };
}

function buildCrashEmbed(c: CrashReportInput, receivedUtc: string, issueUrl: string | null): Record<string, unknown> {
  const fields = [
    { name: 'Player', value: c.player, inline: true },
    { name: 'Crash Class', value: `\`${c.crash_class}\` (exit ${c.exit_code})`, inline: true },
    { name: 'Mode / Tier', value: [c.mode, c.perf_tier].filter(Boolean).join(' · ') || '—', inline: true },
    { name: 'Match Hash', value: c.match_hash ? `\`${c.match_hash.slice(0, 12)}\`` : '—', inline: true },
    { name: 'GPU / RAM', value: [c.gpu, c.ram_gb !== null ? `${c.ram_gb} GB` : ''].filter(Boolean).join(' · ') || '—', inline: true },
  ];
  if (issueUrl) fields.push({ name: 'GitHub Issue', value: issueUrl, inline: true });
  const tailSnippet = c.log_tail ? `\n\`\`\`text\n${c.log_tail.replaceAll('```', "'''").slice(-600)}\n\`\`\`` : '';
  return {
    title: `Crash Report · ${c.crash_class} (${c.launcher_version})`,
    url: issueUrl || undefined,
    description: (c.summary + tailSnippet).slice(0, 2000),
    color: 0xe74c3c,
    fields,
    timestamp: receivedUtc,
  };
}

export default {
  async fetch(request, env): Promise<Response> {
    const path = new URL(request.url).pathname;
    const token = request.headers.get('Authorization')?.replace(/^Bearer /, '') ?? '';
    try {
      if (request.method === 'POST' && !path.startsWith('/admin/')) {
        const identity = path === '/chunks' ? request.headers.get('X-Device-Id') ?? 'unknown' : request.headers.get('CF-Connecting-IP') ?? 'unknown';
        if (!(await env.INGEST_LIMIT.limit({ key: path + ':' + identity })).success) return json({ error: 'Try later' }, 429);
      }
      if (request.method === 'GET' && path === '/') return new Response(dashboard, { headers: {
        'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store',
        'Content-Security-Policy': "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'",
        'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer'
      }});
      if (request.method === 'GET' && path === '/health') return json({ service: 'clonedivers-telemetry', version: 2 });
      if (path.startsWith('/admin/')) {
        if (!await matches(token, env.ADMIN_TOKEN)) return json({ error: 'Unauthorized' }, 401);
        if (request.method === 'GET' && path === '/admin/squad') {
          const sessions = (await env.DB.prepare(`
            SELECT id, received_utc, player, host_label, launcher_version, match_hash,
                   mode, perf_tier, gpu, ram_gb, duration_sec, exit_code, avg_fps, low1_fps, status_summary
            FROM (
              SELECT *, ROW_NUMBER() OVER (PARTITION BY player ORDER BY received_utc DESC, id DESC) AS rn
              FROM squad_sessions
            )
            WHERE rn = 1
            ORDER BY received_utc DESC
            LIMIT 100
          `).all()).results;
          const crashes = (await env.DB.prepare(`
            SELECT id, received_utc, player, launcher_version, match_hash, mode, perf_tier,
                   gpu, ram_gb, exit_code, crash_class, summary, log_tail, github_issue_url
            FROM crash_reports
            ORDER BY received_utc DESC, id DESC
            LIMIT 25
          `).all()).results;
          return json({ sessions, crashes });
        }
        if (request.method === 'GET' && path === '/admin/sessions') return json((await env.DB.prepare(`SELECT c.device_id,d.nickname,c.session_id,MIN(c.first_utc) AS start,MAX(c.last_utc) AS end,SUM(c.records) AS records,SUM(c.bytes) AS bytes,COUNT(*) AS chunks FROM chunks c JOIN devices d ON c.device_id=d.id GROUP BY c.device_id,c.session_id ORDER BY start DESC LIMIT 100`).all()).results);
        if (request.method === 'GET' && path.startsWith('/admin/session/')) {
          const session = path.slice('/admin/session/'.length); if (!ID.test(session)) return json({ error: 'Invalid session' }, 400);
          return json((await env.DB.prepare('SELECT id,first_utc,last_utc,records,bytes FROM chunks WHERE session_id=? ORDER BY first_utc LIMIT 5000').bind(session).all()).results);
        }
        if (request.method === 'GET' && path.startsWith('/admin/chunk/')) {
          const id = path.slice('/admin/chunk/'.length); if (!ID.test(id)) return json({ error: 'Invalid chunk' }, 400);
          const row = await env.DB.prepare('SELECT object_key FROM chunks WHERE id=?').bind(id).first<{object_key: string}>();
          const obj = row ? await env.REPORTS.get(row.object_key) : null;
          return obj ? new Response(obj.body, { encodeBody: 'manual', headers: { 'Content-Type': 'application/json', 'Content-Encoding': 'gzip', 'Cache-Control': 'no-store' } }) : json({ error: 'Not found' }, 404);
        }
        if (request.method === 'POST' && path.startsWith('/admin/revoke/')) {
          const id = path.slice('/admin/revoke/'.length); if (!ID.test(id)) return json({ error: 'Invalid device' }, 400);
          await env.DB.prepare('UPDATE devices SET revoked=1 WHERE id=?').bind(id).run(); return json({ ok: true });
        }
        return json({ error: 'Not found' }, 404);
      }
      if (request.method === 'POST' && path === '/session') {
        const s = validateSession(JSON.parse(new TextDecoder().decode(await boundedBody(request, MAX_SQUAD_BYTES))));
        const id = crypto.randomUUID().replaceAll('-', '');
        const receivedUtc = new Date().toISOString();
        await env.DB.prepare(
          'INSERT INTO squad_sessions(id,received_utc,player,host_label,launcher_version,match_hash,mode,perf_tier,gpu,ram_gb,duration_sec,exit_code,avg_fps,low1_fps,status_summary) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)'
        ).bind(
          id, receivedUtc, s.player, s.host_label, s.launcher_version, s.match_hash,
          s.mode, s.perf_tier, s.gpu, s.ram_gb, s.duration_sec, s.exit_code, s.avg_fps, s.low1_fps, s.status_summary
        ).run();
        await postDiscordWebhook(env.DISCORD_WEBHOOK_URL, buildSessionEmbed(s, receivedUtc));
        return json({ ok: true, id });
      }
      if (request.method === 'POST' && path === '/crash') {
        const c = validateCrash(JSON.parse(new TextDecoder().decode(await boundedBody(request, MAX_SQUAD_BYTES))));
        const id = crypto.randomUUID().replaceAll('-', '');
        const now = Date.now();
        const receivedUtc = new Date(now).toISOString();
        const cutoff24h = new Date(now - 24 * 3600_000).toISOString();

        const prior = await env.DB.prepare(
          'SELECT id, github_issue_url FROM crash_reports WHERE crash_class=? AND launcher_version=? AND received_utc>=? ORDER BY (github_issue_url IS NOT NULL) DESC, received_utc DESC LIMIT 1'
        ).bind(c.crash_class, c.launcher_version, cutoff24h).first<{ id: string; github_issue_url: string | null }>();

        let githubIssueUrl: string | null = prior?.github_issue_url ?? null;
        const deduplicated = Boolean(prior);

        if (env.GITHUB_TOKEN && env.GITHUB_TOKEN.trim() && !githubIssueUrl) {
          const cutoff1h = new Date(now - 3600_000).toISOString();
          const recentCount = await env.DB.prepare(
            'SELECT COUNT(DISTINCT github_issue_url) AS cnt FROM crash_reports WHERE received_utc>=? AND github_issue_url IS NOT NULL'
          ).bind(cutoff1h).first<{ cnt: number }>();
          if ((recentCount?.cnt ?? 0) < HOURLY_ISSUE_LIMIT) {
            githubIssueUrl = await createCrashGitHubIssue(env.GITHUB_TOKEN.trim(), c, id, receivedUtc);
          }
        }

        await env.DB.prepare(
          'INSERT INTO crash_reports(id,received_utc,player,launcher_version,match_hash,mode,perf_tier,gpu,ram_gb,exit_code,crash_class,summary,log_tail,github_issue_url) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)'
        ).bind(
          id, receivedUtc, c.player, c.launcher_version, c.match_hash, c.mode, c.perf_tier,
          c.gpu, c.ram_gb, c.exit_code, c.crash_class, c.summary, c.log_tail, githubIssueUrl
        ).run();
        await postDiscordWebhook(env.DISCORD_WEBHOOK_URL, buildCrashEmbed(c, receivedUtc, githubIssueUrl));
        return json({ ok: true, id, githubIssueUrl, deduplicated });
      }
      if (request.method === 'POST' && path === '/enroll') {
        if (!await matches(token, env.ENROLLMENT_TOKEN)) return json({ error: 'Invalid invitation' }, 401);
        const v: unknown = JSON.parse(new TextDecoder().decode(await boundedBody(request)));
        if (!v || typeof v !== 'object' || !('nickname' in v) || typeof v.nickname !== 'string' || !v.nickname.trim() || v.nickname.length > 40) return json({ error: 'Nickname required' }, 400);
        const id = crypto.randomUUID().replaceAll('-', ''); const key = crypto.randomUUID().replaceAll('-', '') + crypto.randomUUID().replaceAll('-', '');
        const result = await env.DB.prepare('INSERT INTO devices(id,token_hash,nickname,created_utc) SELECT ?,?,?,? WHERE (SELECT COUNT(*) FROM devices)<100').bind(id, await hash(key), v.nickname.trim(), new Date().toISOString()).run();
        if (!result.meta.changes) return json({ error: 'Enrollment limit reached' }, 429);
        return json({ deviceId: id, token: key });
      }
      if (request.method === 'POST' && path === '/chunks') {
        const id = request.headers.get('X-Device-Id') ?? '';
        if (!ID.test(id)) return json({ error: 'Unauthorized' }, 401);
        const device = await env.DB.prepare('SELECT id,token_hash,revoked FROM devices WHERE id=?').bind(id).first<Device>();
        if (!device || device.revoked || !await matches(await hash(token), device.token_hash)) return json({ error: 'Unauthorized' }, 401);
        const bytes = await boundedBody(request);
        const chunk = validate(JSON.parse(new TextDecoder().decode(bytes)));
        if (chunk.deviceId !== id) return json({ error: 'Wrong device' }, 403);
        const canonical = JSON.stringify(chunk); const digest = await hash(canonical);
        const prior = await env.DB.prepare('SELECT device_id,sha256 FROM chunks WHERE id=?').bind(chunk.chunkId).first<{device_id:string;sha256:string}>();
        if (prior) return prior.device_id === id && prior.sha256 === digest ? json({ ok: true, id: chunk.chunkId }) : json({ error: 'Conflicting chunk' }, 409);
        const today = new Date().toISOString().slice(0, 10);
        const quota = await env.DB.prepare(`UPDATE devices SET quota_bytes=CASE WHEN quota_day=? THEN quota_bytes+? ELSE ? END,quota_day=?,nickname=? WHERE id=? AND (quota_day<>? OR quota_bytes+?<=4294967296)`).bind(today, bytes.length, bytes.length, today, chunk.nickname, id, today, bytes.length).run();
        if (!quota.meta.changes) return json({ error: 'Daily upload quota' }, 429);
        // Content-addressed suffix prevents concurrent conflicting retries from replacing an acknowledged object.
        const objectKey = `${id}/${chunk.sessionId}/${chunk.chunkId}-${digest}.json.gz`;
        const compressed = new Blob([canonical]).stream().pipeThrough(new CompressionStream('gzip'));
        await env.REPORTS.put(objectKey, await new Response(compressed).arrayBuffer(), { httpMetadata: { contentType: 'application/json', contentEncoding: 'gzip' } });
        const times = chunk.records.map(r => r.utc).sort();
        await env.DB.prepare('INSERT OR IGNORE INTO chunks VALUES(?,?,?,?,?,?,?,?,?,?)').bind(chunk.chunkId, id, chunk.sessionId, new Date().toISOString(), times[0], times.at(-1), chunk.records.length, bytes.length, digest, objectKey).run();
        const saved = await env.DB.prepare('SELECT sha256 FROM chunks WHERE id=?').bind(chunk.chunkId).first<{sha256:string}>();
        return saved?.sha256 === digest ? json({ ok: true, id: chunk.chunkId }) : json({ error: 'Conflicting chunk' }, 409);
      }
      return json({ error: 'Not found' }, 404);
    } catch (e) {
      if (e instanceof SyntaxError || (e instanceof Error && /Invalid|Too large|Missing body/.test(e.message))) return json({ error: 'Invalid or oversized report' }, 400);
      console.error(JSON.stringify({ event: 'request-failed', path: ['/chunks', '/enroll', '/session', '/crash'].includes(path) ? path : 'other' }));
      return json({ error: 'Service unavailable' }, 503);
    }
  },
  async scheduled(_controller, env): Promise<void> {
    const cutoff = new Date(Date.now() - 90 * 86400000).toISOString();
    for (let batch = 0; batch < 5; batch++) {
      const rows = (await env.DB.prepare('SELECT id,object_key FROM chunks WHERE received_utc<? ORDER BY received_utc LIMIT 500').bind(cutoff).all<{id:string;object_key:string}>()).results;
      if (!rows.length) break;
      await env.REPORTS.delete(rows.map(r => r.object_key));
      // Delete only the selected rows after object deletion; never remove a concurrent fresh upload.
      await env.DB.prepare('DELETE FROM chunks WHERE id IN (SELECT value FROM json_each(?))').bind(JSON.stringify(rows.map(r => r.id))).run();
    }
    await env.DB.prepare('DELETE FROM squad_sessions WHERE received_utc<?').bind(cutoff).run();
    await env.DB.prepare('DELETE FROM crash_reports WHERE received_utc<?').bind(cutoff).run();
  }
} satisfies ExportedHandler<Env & Secrets>;
