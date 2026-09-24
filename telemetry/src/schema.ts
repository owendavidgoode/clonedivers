export const MAX_BYTES = 512 * 1024;
export const ID = /^[a-f0-9]{32}$/;
export type RecordRow = { kind: string; utc: string; fields: Record<string, string | number | boolean | null> };
export type Chunk = { version: 1; deviceId: string; sessionId: string; chunkId: string; nickname: string; records: RecordRow[] };
const kinds = new Set(['context', 'event', 'resource', 'frame', 'crash']);
const key = /^[a-zA-Z][a-zA-Z0-9_.]{0,79}$/;
function object(v: unknown): v is Record<string, unknown> { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function date(v: unknown): v is string { return typeof v === 'string' && v.length <= 40 && /^20\d\d-/.test(v) && Number.isFinite(Date.parse(v)); }
export function validate(v: unknown): Chunk {
  if (!object(v) || v.version !== 1 || !ID.test(String(v.deviceId)) || !ID.test(String(v.sessionId)) || !ID.test(String(v.chunkId)) ||
      typeof v.nickname !== 'string' || v.nickname.length > 40 || !Array.isArray(v.records) || !v.records.length || v.records.length > 2000) throw new Error('Invalid chunk');
  const records: RecordRow[] = v.records.map((r: unknown) => {
    if (!object(r) || typeof r.kind !== 'string' || !kinds.has(r.kind) || !date(r.utc) || !object(r.fields) || Object.keys(r.fields).length > 150) throw new Error('Invalid record');
    const fields: RecordRow['fields'] = {};
    for (const [k, value] of Object.entries(r.fields)) {
      if (!key.test(k) || k === '__proto__' || k === 'constructor') throw new Error('Invalid field');
      if (value !== null && typeof value !== 'boolean' && !(typeof value === 'number' && Number.isFinite(value)) && !(typeof value === 'string' && value.length <= 1024)) throw new Error('Invalid value');
      fields[k] = value as string | number | boolean | null;
    }
    return { kind: r.kind, utc: r.utc, fields };
  });
  return { version: 1, deviceId: String(v.deviceId), sessionId: String(v.sessionId), chunkId: String(v.chunkId), nickname: v.nickname, records };
}
export async function boundedBody(request: Request): Promise<Uint8Array> {
  if (Number(request.headers.get('content-length')) > MAX_BYTES) throw new Error('Too large');
  const encoding = request.headers.get('Content-Encoding');
  if (encoding && encoding !== 'gzip') throw new Error('Invalid encoding');
  const reader = (encoding === 'gzip' ? request.body?.pipeThrough(new DecompressionStream('gzip')) : request.body)?.getReader();
  if (!reader) throw new Error('Missing body');
  const chunks: Uint8Array[] = []; let length = 0;
  try {
    for (;;) {
      const next = await reader.read(); if (next.done) break;
      length += next.value.length;
      if (length > MAX_BYTES) { await reader.cancel(); throw new Error('Too large'); }
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const result = new Uint8Array(length); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
  return result;
}
