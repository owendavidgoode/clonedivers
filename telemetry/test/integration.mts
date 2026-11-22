// Run against a local Wrangler instance with the test-only keys below.
import assert from 'node:assert/strict';
import { gzipSync } from 'node:zlib';
const root='http://127.0.0.1:8787';
const admin='local-test-admin-'.repeat(4), invite='local-test-invite-'.repeat(4);
const post=(path:string,token:string,body:unknown,extra:Record<string,string>={})=>fetch(root+path,{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json',...extra},body:JSON.stringify(body)});
assert.equal((await fetch(root+'/admin/sessions')).status,401);
assert.equal((await fetch(root+'/admin/squad')).status,401);
assert.equal((await post('/enroll','wrong',{nickname:'Test'})).status,401);
const sr=await post('/session','',{player:'Local QA',launcher_version:'2.0.3',match_hash:'abc123456789',mode:'Empire',perf_tier:'Full',gpu:'RTX 4080',ram_gb:32,duration_sec:300,exit_code:0,avg_fps:84.5,low1_fps:59.2,status_summary:'Clean exit'});assert.equal(sr.status,200);
const cr=await post('/crash','',{player:'Local QA',launcher_version:'2.0.3',crash_class:'OutOfVram',exit_code:1,summary:'Simulated local VRAM crash'});assert.equal(cr.status,200);
const squad=await (await fetch(root+'/admin/squad',{headers:{Authorization:'Bearer '+admin}})).json() as {sessions:unknown[];crashes:unknown[]};assert.equal(squad.sessions.length>=1&&squad.crashes.length>=1,true);
const er=await post('/enroll',invite,{nickname:'Local QA'});assert.equal(er.status,200);const e=await er.json() as {deviceId:string;token:string};
const chunk={version:1,deviceId:e.deviceId,sessionId:crypto.randomUUID().replaceAll('-',''),chunkId:crypto.randomUUID().replaceAll('-',''),nickname:'Local QA',records:[{kind:'resource',utc:new Date().toISOString(),fields:{privateBytes:100,foreground:true}},{kind:'frame',utc:new Date().toISOString(),fields:{CPUFrameTime:16.6,SwapChainAddress:'0x123'}}]};
assert.equal((await post('/chunks',admin,chunk,{'X-Device-Id':e.deviceId})).status,401);
for(let n=0;n<2;n++){const r=await fetch(root+'/chunks',{method:'POST',headers:{Authorization:'Bearer '+e.token,'X-Device-Id':e.deviceId,'Content-Encoding':'gzip'},body:gzipSync(JSON.stringify(chunk))});assert.equal(r.status,200,await r.text())}
assert.equal((await post('/chunks',e.token,{...chunk,nickname:'Conflict'},{'X-Device-Id':e.deviceId})).status,409);
const read=await fetch(root+'/admin/chunk/'+chunk.chunkId,{headers:{Authorization:'Bearer '+admin}});assert.equal(read.status,200);assert.deepEqual(await read.json(),chunk);
const sessions=await (await fetch(root+'/admin/sessions',{headers:{Authorization:'Bearer '+admin}})).json() as {session_id:string;chunks:number}[];assert.equal(sessions.find(s=>s.session_id===chunk.sessionId)?.chunks,1);
assert.equal((await post('/admin/revoke/'+e.deviceId,admin,{})).status,200);
assert.equal((await post('/chunks',e.token,{...chunk,chunkId:crypto.randomUUID().replaceAll('-','')},{'X-Device-Id':e.deviceId})).status,401);
console.log('PASS: unauthorized access, enrollment, device isolation, gzip upload/download, duplicate retry, conflict, session index, revocation');
