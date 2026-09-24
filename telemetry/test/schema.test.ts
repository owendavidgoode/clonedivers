import { test } from 'node:test';
import assert from 'node:assert/strict';
import { gzipSync } from 'node:zlib';
import { validate, boundedBody, MAX_BYTES } from '../src/schema.ts';
const fixture = () => ({version:1,deviceId:'a'.repeat(32),sessionId:'b'.repeat(32),chunkId:'c'.repeat(32),nickname:'Squad',records:[{kind:'resource',utc:'2026-09-23T20:00:00Z',fields:{privateBytes:123,foreground:true}}]});
test('validates known records and strips unexpected envelope data',()=>{assert.equal(validate({...fixture(),password:'ignored'}).records.length,1);assert.equal('password' in validate({...fixture(),password:'ignored'}),false)});
test('rejects malformed identifiers, records, dates and nested values',()=>{
  for(const v of [{...fixture(),deviceId:'../secret'}, {...fixture(),records:[]}, {...fixture(),records:[{kind:'unknown',utc:'2026-09-23T20:00:00Z',fields:{}}]}, {...fixture(),records:[{kind:'event',utc:'never',fields:{}}]}, {...fixture(),records:[{kind:'resource',utc:'2026-09-23T20:00:00Z',fields:{secret:{a:1}}}]}])assert.throws(()=>validate(v));
});
test('caps actual request stream independent of content length',async()=>{await assert.rejects(()=>boundedBody(new Request('https://example.test',{method:'POST',body:'x'.repeat(MAX_BYTES+1)})));});
test('decompresses bounded gzip and rejects gzip expansion beyond limit',async()=>{
 const data=JSON.stringify(fixture());const r=new Request('https://example.test',{method:'POST',body:gzipSync(data),headers:{'Content-Encoding':'gzip'}});
 assert.equal(new TextDecoder().decode(await boundedBody(r)),data);
 await assert.rejects(()=>boundedBody(new Request('https://example.test',{method:'POST',body:gzipSync('x'.repeat(MAX_BYTES+1)),headers:{'Content-Encoding':'gzip'}})));
});
