import { boundedBody, ID, validate } from './schema.ts';
import { dashboard } from './ui.ts';

type Secrets = { ADMIN_TOKEN: string; ENROLLMENT_TOKEN: string };
type Device = { id: string; token_hash: string; revoked: number };
const json = (v: unknown, status = 200): Response => Response.json(v, { status, headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' } });
async function hash(value: string | Uint8Array): Promise<string> {
  const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : value;
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(b => b.toString(16).padStart(2, '0')).join('');
}
async function matches(a: string, b: string | undefined): Promise<boolean> {
  if (!b || b.length < 32 || a.length > 256) return false;
  const x = new TextEncoder().encode(await hash(a)); const y = new TextEncoder().encode(await hash(b));
  return crypto.subtle.timingSafeEqual(x, y);
}
export default {
  async fetch(request, env): Promise<Response> {
    const path = new URL(request.url).pathname;
    const token = request.headers.get('Authorization')?.replace(/^Bearer /, '') ?? '';
    try {
      if (request.method === 'POST' && !path.startsWith('/admin/')) {
        const identity = path === '/chunks' ? request.headers.get('X-Device-Id') ?? 'unknown' : request.headers.get('CF-Connecting-IP') ?? 'unknown';
        if (!(await env.INGEST_LIMIT.limit({key: path + ':' + identity})).success) return json({error:'Try later'},429);
      }
      if (request.method === 'GET' && path === '/') return new Response(dashboard, { headers: {
        'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store',
        'Content-Security-Policy': "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'",
        'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer'
      }});
      if (request.method === 'GET' && path === '/health') return json({ service: 'clonedivers-telemetry', version: 1 });
      if (path.startsWith('/admin/')) {
        if (!await matches(token, env.ADMIN_TOKEN)) return json({ error: 'Unauthorized' }, 401);
        if (request.method === 'GET' && path === '/admin/sessions') return json((await env.DB.prepare(`SELECT c.device_id,d.nickname,c.session_id,MIN(c.first_utc) AS start,MAX(c.last_utc) AS end,SUM(c.records) AS records,SUM(c.bytes) AS bytes,COUNT(*) AS chunks FROM chunks c JOIN devices d ON c.device_id=d.id GROUP BY c.device_id,c.session_id ORDER BY start DESC LIMIT 100`).all()).results);
        if (request.method === 'GET' && path.startsWith('/admin/session/')) {
          const session = path.slice('/admin/session/'.length); if (!ID.test(session)) return json({ error: 'Invalid session' }, 400);
          return json((await env.DB.prepare('SELECT id,first_utc,last_utc,records,bytes FROM chunks WHERE session_id=? ORDER BY first_utc LIMIT 5000').bind(session).all()).results);
        }
        if (request.method === 'GET' && path.startsWith('/admin/chunk/')) {
          const id = path.slice('/admin/chunk/'.length); if (!ID.test(id)) return json({ error: 'Invalid chunk' }, 400);
          const row = await env.DB.prepare('SELECT object_key FROM chunks WHERE id=?').bind(id).first<{object_key: string}>();
          const obj = row ? await env.REPORTS.get(row.object_key) : null;
          return obj ? new Response(obj.body, {encodeBody:'manual',headers: {'Content-Type': 'application/json', 'Content-Encoding':'gzip', 'Cache-Control': 'no-store'}}) : json({error:'Not found'},404);
        }
        if (request.method === 'POST' && path.startsWith('/admin/revoke/')) {
          const id = path.slice('/admin/revoke/'.length); if (!ID.test(id)) return json({ error: 'Invalid device' }, 400);
          await env.DB.prepare('UPDATE devices SET revoked=1 WHERE id=?').bind(id).run(); return json({ ok: true });
        }
        return json({ error: 'Not found' }, 404);
      }
      if (request.method === 'POST' && path === '/enroll') {
        if (!await matches(token, env.ENROLLMENT_TOKEN)) return json({ error: 'Invalid invitation' }, 401);
        const v: unknown = JSON.parse(new TextDecoder().decode(await boundedBody(request)));
        if (!v || typeof v !== 'object' || !('nickname' in v) || typeof v.nickname !== 'string' || !v.nickname.trim() || v.nickname.length > 40) return json({error:'Nickname required'},400);
        const id = crypto.randomUUID().replaceAll('-', ''); const key = crypto.randomUUID().replaceAll('-', '') + crypto.randomUUID().replaceAll('-', '');
        const result = await env.DB.prepare('INSERT INTO devices(id,token_hash,nickname,created_utc) SELECT ?,?,?,? WHERE (SELECT COUNT(*) FROM devices)<100').bind(id, await hash(key), v.nickname.trim(), new Date().toISOString()).run();
        if (!result.meta.changes) return json({error:'Enrollment limit reached'},429);
        return json({ deviceId: id, token: key });
      }
      if (request.method === 'POST' && path === '/chunks') {
        const id = request.headers.get('X-Device-Id') ?? '';
        if (!ID.test(id)) return json({error:'Unauthorized'},401);
        const device = await env.DB.prepare('SELECT id,token_hash,revoked FROM devices WHERE id=?').bind(id).first<Device>();
        if (!device || device.revoked || !await matches(await hash(token), device.token_hash)) return json({error:'Unauthorized'},401);
        const bytes = await boundedBody(request);
        const chunk = validate(JSON.parse(new TextDecoder().decode(bytes)));
        if (chunk.deviceId !== id) return json({error:'Wrong device'},403);
        const canonical = JSON.stringify(chunk); const digest = await hash(canonical);
        const prior = await env.DB.prepare('SELECT device_id,sha256 FROM chunks WHERE id=?').bind(chunk.chunkId).first<{device_id:string;sha256:string}>();
        if (prior) return prior.device_id === id && prior.sha256 === digest ? json({ok:true,id:chunk.chunkId}) : json({error:'Conflicting chunk'},409);
        const today = new Date().toISOString().slice(0,10);
        const quota = await env.DB.prepare(`UPDATE devices SET quota_bytes=CASE WHEN quota_day=? THEN quota_bytes+? ELSE ? END,quota_day=?,nickname=? WHERE id=? AND (quota_day<>? OR quota_bytes+?<=4294967296)`).bind(today,bytes.length,bytes.length,today,chunk.nickname,id,today,bytes.length).run();
        if (!quota.meta.changes) return json({error:'Daily upload quota'},429);
        // Content-addressed suffix prevents concurrent conflicting retries from replacing an acknowledged object.
        const objectKey = `${id}/${chunk.sessionId}/${chunk.chunkId}-${digest}.json.gz`;
        const compressed = new Blob([canonical]).stream().pipeThrough(new CompressionStream('gzip'));
        await env.REPORTS.put(objectKey, await new Response(compressed).arrayBuffer(), {httpMetadata:{contentType:'application/json',contentEncoding:'gzip'}});
        const times = chunk.records.map(r=>r.utc).sort();
        await env.DB.prepare('INSERT OR IGNORE INTO chunks VALUES(?,?,?,?,?,?,?,?,?,?)').bind(chunk.chunkId,id,chunk.sessionId,new Date().toISOString(),times[0],times.at(-1),chunk.records.length,bytes.length,digest,objectKey).run();
        const saved = await env.DB.prepare('SELECT sha256 FROM chunks WHERE id=?').bind(chunk.chunkId).first<{sha256:string}>();
        return saved?.sha256 === digest ? json({ok:true,id:chunk.chunkId}) : json({error:'Conflicting chunk'},409);
      }
      return json({error:'Not found'},404);
    } catch (e) {
      if (e instanceof SyntaxError || (e instanceof Error && /Invalid|Too large|Missing body/.test(e.message))) return json({error:'Invalid or oversized report'},400);
      console.error(JSON.stringify({event:'request-failed',path: ['/chunks','/enroll'].includes(path) ? path : 'other'}));
      return json({error:'Service unavailable'},503);
    }
  },
  async scheduled(_controller, env): Promise<void> {
    const cutoff = new Date(Date.now()-90*86400000).toISOString();
    for(let batch=0;batch<5;batch++) {
      const rows=(await env.DB.prepare('SELECT id,object_key FROM chunks WHERE received_utc<? ORDER BY received_utc LIMIT 500').bind(cutoff).all<{id:string;object_key:string}>()).results;
      if(!rows.length)break;
      await env.REPORTS.delete(rows.map(r=>r.object_key));
      // Delete only the selected rows after object deletion; never remove a concurrent fresh upload.
      await env.DB.prepare('DELETE FROM chunks WHERE id IN (SELECT value FROM json_each(?))').bind(JSON.stringify(rows.map(r=>r.id))).run();
    }
  }
} satisfies ExportedHandler<Env & Secrets>;
