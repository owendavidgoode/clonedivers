export const MAX_BYTES = 512 * 1024;
export const MAX_SQUAD_BYTES = 32 * 1024;
export const ID = /^[a-f0-9]{32}$/;
export type RecordRow = { kind: string; utc: string; fields: Record<string, string | number | boolean | null> };
export type Chunk = { version: 1; deviceId: string; sessionId: string; chunkId: string; nickname: string; records: RecordRow[] };
export type SquadSessionInput = {
  player: string;
  host_label: string;
  launcher_version: string;
  match_hash: string;
  mode: string;
  perf_tier: string;
  gpu: string;
  ram_gb: number | null;
  duration_sec: number | null;
  exit_code: number;
  avg_fps: number | null;
  low1_fps: number | null;
  status_summary: string;
};
export type CrashReportInput = {
  player: string;
  launcher_version: string;
  match_hash: string;
  mode: string;
  perf_tier: string;
  gpu: string;
  ram_gb: number | null;
  exit_code: number;
  crash_class: string;
  summary: string;
  log_tail: string;
};

const kinds = new Set(['context', 'event', 'resource', 'frame', 'crash']);
const key = /^[a-zA-Z][a-zA-Z0-9_.]{0,79}$/;
const safeVersion = /^[a-zA-Z0-9._+-]{1,40}$/;
const safeCrashClass = /^[a-zA-Z0-9_.-]{1,64}$/;
const suppressedCrashClasses = new Set(['clean', 'harmlessquit', 'harmless_quit', 'harmless-quit']);

function object(v: unknown): v is Record<string, unknown> { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function date(v: unknown): v is string { return typeof v === 'string' && v.length <= 40 && /^20\d\d-/.test(v) && Number.isFinite(Date.parse(v)); }

function pick(obj: Record<string, unknown>, ...names: string[]): unknown {
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(obj, name) && obj[name] !== undefined) return obj[name];
  }
  return undefined;
}

export function sanitizeText(raw: string, allowNewlines = false): string {
  let text = raw.replace(/\r\n?/g, allowNewlines ? '\n' : ' ');
  text = allowNewlines
    ? text.replace(/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/g, '')
    : text.replace(/[\x00-\x1f\x7f]/g, ' ');
  text = text
    .replace(/([A-Za-z]:[\\/]+Users[\\/]+)[^\\/\r\n"']+/gi, '$1<user>')
    .replace(/(\/(?:home|Users)\/)[^/\s"']+/g, '$1<user>')
    .replace(/(Bearer\s+)[A-Za-z0-9._~+/=-]+/gi, '$1<redacted>')
    .replace(/\b(gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,})\b/g, '<redacted-token>')
    .replace(/(steamLoginSecure\s*[=:]\s*)[^\s;,"']+/gi, '$1<redacted>')
    .replace(/@(everyone|here)\b/gi, '@\u200b$1');
  return text.trim();
}

function reqString(v: unknown, maxLen: number, label: string): string {
  if (typeof v !== 'string') throw new Error(`Invalid ${label}`);
  if (v.length > maxLen * 2) throw new Error(`Invalid ${label}`);
  const cleaned = sanitizeText(v, false);
  if (!cleaned || cleaned.length > maxLen) throw new Error(`Invalid ${label}`);
  return cleaned;
}

function optString(v: unknown, maxLen: number, label: string, allowNewlines = false): string {
  if (v === undefined || v === null || v === '') return '';
  if (typeof v !== 'string' || v.length > maxLen) throw new Error(`Invalid ${label}`);
  return sanitizeText(v, allowNewlines);
}

function optNumber(v: unknown, min: number, max: number, label: string): number | null {
  if (v === undefined || v === null) return null;
  if (typeof v !== 'number' || !Number.isFinite(v) || v < min || v > max) throw new Error(`Invalid ${label}`);
  return v;
}

function intField(v: unknown, fallback: number, label: string): number {
  if (v === undefined || v === null) return fallback;
  if (typeof v !== 'number' || !Number.isInteger(v) || v < -2147483648 || v > 4294967295) throw new Error(`Invalid ${label}`);
  return v;
}

function checkEnvelope(v: unknown, label: string): Record<string, unknown> {
  if (!object(v)) throw new Error(`Invalid ${label}`);
  const keys = Object.keys(v);
  if (!keys.length || keys.length > 40) throw new Error(`Invalid ${label}`);
  for (const k of keys) {
    if (k === '__proto__' || k === 'constructor' || k === 'prototype') throw new Error(`Invalid ${label}`);
  }
  return v;
}

export function validateSession(v: unknown): SquadSessionInput {
  const obj = checkEnvelope(v, 'session');
  const player = reqString(pick(obj, 'player', 'Player', 'nickname', 'Nickname', 'player_name', 'playerName'), 64, 'player');
  const launcherVersion = reqString(pick(obj, 'launcher_version', 'launcherVersion', 'LauncherVersion', 'version', 'Version'), 40, 'launcher_version');
  if (!safeVersion.test(launcherVersion)) throw new Error('Invalid launcher_version');
  const durationRaw = optNumber(pick(obj, 'duration_sec', 'durationSec', 'DurationSec', 'durationSeconds'), 0, 604800, 'duration_sec');
  return {
    player,
    host_label: optString(pick(obj, 'host_label', 'hostLabel', 'HostLabel', 'host'), 64, 'host_label'),
    launcher_version: launcherVersion,
    match_hash: optString(pick(obj, 'match_hash', 'matchHash', 'MatchHash'), 80, 'match_hash'),
    mode: optString(pick(obj, 'mode', 'Mode'), 48, 'mode'),
    perf_tier: optString(pick(obj, 'perf_tier', 'perfTier', 'PerfTier', 'tier', 'Tier'), 48, 'perf_tier'),
    gpu: optString(pick(obj, 'gpu', 'Gpu', 'GPU'), 120, 'gpu'),
    ram_gb: optNumber(pick(obj, 'ram_gb', 'ramGb', 'RamGb'), 0, 2048, 'ram_gb'),
    duration_sec: durationRaw === null ? null : Math.round(durationRaw),
    exit_code: intField(pick(obj, 'exit_code', 'exitCode', 'ExitCode'), 0, 'exit_code'),
    avg_fps: optNumber(pick(obj, 'avg_fps', 'avgFps', 'AvgFps'), 0, 2000, 'avg_fps'),
    low1_fps: optNumber(pick(obj, 'low1_fps', 'low1Fps', 'Low1Fps', 'onePercentLowFps'), 0, 2000, 'low1_fps'),
    status_summary: optString(pick(obj, 'status_summary', 'statusSummary', 'StatusSummary', 'summary', 'Summary'), 280, 'status_summary'),
  };
}

export function validateCrash(v: unknown): CrashReportInput {
  const obj = checkEnvelope(v, 'crash');
  const player = reqString(pick(obj, 'player', 'Player', 'nickname', 'Nickname', 'player_name', 'playerName'), 64, 'player');
  const launcherVersion = reqString(pick(obj, 'launcher_version', 'launcherVersion', 'LauncherVersion', 'version', 'Version'), 40, 'launcher_version');
  if (!safeVersion.test(launcherVersion)) throw new Error('Invalid launcher_version');
  const crashClass = reqString(pick(obj, 'crash_class', 'crashClass', 'CrashClass', 'triageClass', 'TriageClass'), 64, 'crash_class');
  if (!safeCrashClass.test(crashClass) || suppressedCrashClasses.has(crashClass.toLowerCase())) {
    throw new Error('Invalid crash_class');
  }
  return {
    player,
    launcher_version: launcherVersion,
    match_hash: optString(pick(obj, 'match_hash', 'matchHash', 'MatchHash'), 80, 'match_hash'),
    mode: optString(pick(obj, 'mode', 'Mode'), 48, 'mode'),
    perf_tier: optString(pick(obj, 'perf_tier', 'perfTier', 'PerfTier', 'tier', 'Tier'), 48, 'perf_tier'),
    gpu: optString(pick(obj, 'gpu', 'Gpu', 'GPU'), 120, 'gpu'),
    ram_gb: optNumber(pick(obj, 'ram_gb', 'ramGb', 'RamGb'), 0, 2048, 'ram_gb'),
    exit_code: intField(pick(obj, 'exit_code', 'exitCode', 'ExitCode'), 1, 'exit_code'),
    crash_class: crashClass,
    summary: reqString(pick(obj, 'summary', 'Summary', 'message', 'Message'), 500, 'summary'),
    log_tail: optString(pick(obj, 'log_tail', 'logTail', 'LogTail', 'tail', 'Tail', 'errorTail'), 16384, 'log_tail', true),
  };
}

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

export async function boundedBody(request: Request, maxBytes = MAX_BYTES): Promise<Uint8Array> {
  if (Number(request.headers.get('content-length')) > maxBytes) throw new Error('Too large');
  const encoding = request.headers.get('Content-Encoding');
  if (encoding && encoding !== 'gzip') throw new Error('Invalid encoding');
  const reader = (encoding === 'gzip' ? request.body?.pipeThrough(new DecompressionStream('gzip')) : request.body)?.getReader();
  if (!reader) throw new Error('Missing body');
  const chunks: Uint8Array[] = []; let length = 0;
  try {
    for (;;) {
      const next = await reader.read(); if (next.done) break;
      length += next.value.length;
      if (length > maxBytes) { await reader.cancel(); throw new Error('Too large'); }
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const result = new Uint8Array(length); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
  return result;
}
