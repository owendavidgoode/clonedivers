import { test } from 'node:test';
import assert from 'node:assert/strict';
import { gzipSync } from 'node:zlib';
import { validate, validateSession, validateCrash, sanitizeText, boundedBody, MAX_BYTES, MAX_SQUAD_BYTES } from '../src/schema.ts';

const fixture = () => ({version:1,deviceId:'a'.repeat(32),sessionId:'b'.repeat(32),chunkId:'c'.repeat(32),nickname:'Squad',records:[{kind:'resource',utc:'2026-09-23T20:00:00Z',fields:{privateBytes:123,foreground:true}}]});

test('validates known records and strips unexpected envelope data', () => {
  assert.equal(validate({ ...fixture(), password: 'ignored' }).records.length, 1);
  assert.equal('password' in validate({ ...fixture(), password: 'ignored' }), false);
});

test('rejects malformed identifiers, records, dates and nested values', () => {
  for (const v of [
    { ...fixture(), deviceId: '../secret' },
    { ...fixture(), records: [] },
    { ...fixture(), records: [{ kind: 'unknown', utc: '2026-09-23T20:00:00Z', fields: {} }] },
    { ...fixture(), records: [{ kind: 'event', utc: 'never', fields: {} }] },
    { ...fixture(), records: [{ kind: 'resource', utc: '2026-09-23T20:00:00Z', fields: { secret: { a: 1 } } }] },
  ]) {
    assert.throws(() => validate(v));
  }
});

test('caps actual request stream independent of content length', async () => {
  await assert.rejects(() => boundedBody(new Request('https://example.test', { method: 'POST', body: 'x'.repeat(MAX_BYTES + 1) })));
  await assert.rejects(() => boundedBody(new Request('https://example.test', { method: 'POST', body: 'x'.repeat(MAX_SQUAD_BYTES + 1) }), MAX_SQUAD_BYTES));
});

test('decompresses bounded gzip and rejects gzip expansion beyond limit', async () => {
  const data = JSON.stringify(fixture());
  const r = new Request('https://example.test', { method: 'POST', body: gzipSync(data), headers: { 'Content-Encoding': 'gzip' } });
  assert.equal(new TextDecoder().decode(await boundedBody(r)), data);
  await assert.rejects(() => boundedBody(new Request('https://example.test', { method: 'POST', body: gzipSync('x'.repeat(MAX_BYTES + 1)), headers: { 'Content-Encoding': 'gzip' } })));
});

test('validates squad sessions in both snake_case and camelCase and strips extra keys', () => {
  const snake = validateSession({
    player: 'Owen',
    host_label: 'Host PC',
    launcher_version: '2.0.3',
    match_hash: 'deadbeef12345678',
    mode: 'Empire',
    perf_tier: 'Full',
    gpu: 'RTX 4090',
    ram_gb: 64,
    duration_sec: 1842.4,
    exit_code: 0,
    avg_fps: 92.4,
    low1_fps: 64.1,
    status_summary: 'Clean end session',
    steamToken: 'must-be-dropped',
  });
  assert.equal(snake.player, 'Owen');
  assert.equal(snake.duration_sec, 1842);
  assert.equal('steamToken' in snake, false);

  const camel = validateSession({
    playerName: 'Johnson',
    hostLabel: 'Client',
    launcherVersion: 'linux-0.9.3',
    matchHash: 'deadbeef12345678',
    mode: 'Clones',
    perfTier: 'reduced512',
    gpu: 'GTX 1660',
    ramGb: 16,
    durationSec: 900,
    exitCode: 0,
    avgFps: 58.2,
    low1Fps: 41.0,
    statusSummary: 'Ready',
  });
  assert.equal(camel.player, 'Johnson');
  assert.equal(camel.launcher_version, 'linux-0.9.3');
  assert.equal(camel.perf_tier, 'reduced512');
  assert.equal(camel.low1_fps, 41.0);
});

test('validates crash reports, rejects HarmlessQuit/Clean, and sanitizes paths and tokens', () => {
  const crash = validateCrash({
    player: 'Rex',
    launcherVersion: '2.0.3',
    matchHash: 'abc12345',
    mode: 'Empire',
    perfTier: 'Full',
    gpu: 'RX 6800',
    ramGb: 32,
    exitCode: 1,
    crashClass: 'OutOfVram',
    summary: 'DXGI_ERROR_DEVICE_HUNG at C:\\Users\\Alice\\AppData\\Local @everyone',
    logTail: 'line 1 /home/bob/.local/share\nAuthorization: Bearer secret123456\nghp_1234567890abcdef123456',
  });
  assert.equal(crash.crash_class, 'OutOfVram');
  assert.match(crash.summary, /C:\\Users\\<user>\\AppData/);
  assert.doesNotMatch(crash.summary, /Alice/);
  assert.doesNotMatch(crash.log_tail, /bob|secret123456|ghp_/);
  assert.equal(sanitizeText('ping @here now'), 'ping @\u200bhere now');

  assert.throws(() => validateCrash({ player: 'Rex', launcher_version: '2.0.3', crash_class: 'HarmlessQuit', summary: 'exit' }));
  assert.throws(() => validateCrash({ player: 'Rex', launcher_version: '2.0.3', crash_class: 'Clean', summary: 'ok' }));
  assert.throws(() => validateCrash({ player: '', launcher_version: '2.0.3', crash_class: 'UnexpectedCrash', summary: 'bad' }));
  assert.throws(() => validateSession({ player: 'Rex', launcher_version: '../bad' }));
});
