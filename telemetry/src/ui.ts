export const dashboard = `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Clonedivers squad & performance</title>
<style>
body{background:#101722;color:#e6ecf5;font:15px/1.45 system-ui,-apple-system,sans-serif;margin:28px auto;padding:0 20px;max-width:1440px}
h1{margin:0 0 6px;font-size:24px}h2{margin:28px 0 8px;font-size:18px;color:#dce8ff;display:flex;align-items:center;gap:10px}
p{margin:6px 0;color:#b8c6db}
input,button{font:inherit;padding:8px 12px;margin:4px 4px 4px 0;background:#203047;color:inherit;border:1px solid #566781;border-radius:6px;cursor:pointer}
input{cursor:text}button:hover{background:#2a3f5e}
table{border-collapse:collapse;width:100%;margin-top:10px;background:#141e2d;border:1px solid #28384f;border-radius:6px;overflow:hidden}
td,th{text-align:left;border-bottom:1px solid #263549;padding:9px 11px;vertical-align:top}
th{background:#182538;color:#b8c7dc;font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.03em}
tr:last-child td{border-bottom:none}
code{font-family:ui-monospace,Consolas,monospace;font-size:13px;background:#1b2940;padding:2px 6px;border-radius:4px;color:#9fd0ff}
.badge{display:inline-block;padding:2px 8px;border-radius:999px;font-size:12px;font-weight:600}
.ok{background:#143829;color:#6ee7a8;border:1px solid #246347}
.warn{background:#3d2d12;color:#fbbf24;border:1px solid #78571e}
.err{background:#3f1a20;color:#fca5a5;border:1px solid #7f2d38}
a{color:#69baff;text-decoration:none}a:hover{text-decoration:underline}
pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#141e2d;border:1px solid #28384f;border-radius:6px;padding:14px}
small{color:#93a4bc;display:block;font-size:12px}
canvas{width:100%;height:220px;background:#152033;border-radius:6px;margin-top:8px}
.empty{padding:14px;color:#8fa1ba;background:#141e2d;border:1px solid #28384f;border-radius:6px;margin-top:8px}
</style>
<h1>Clonedivers squad &amp; performance</h1>
<p>Squad readiness, automatic crash triage, and detailed PresentMon / memory session timelines.</p>
<div id="toolbar">
  <form id="login" style="display:inline"><input id="key" type="password" placeholder="Owner access key" autocomplete="off"><button>Open dashboard</button></form>
  <button id="refresh" hidden>Refresh</button>
  <button id="logout" hidden>Lock</button>
</div>
<p id="status"></p>
<div id="content" hidden>
  <h2>Squad Readiness <span id="hash-badge"></span></h2>
  <div id="squad"></div>
  <h2>Recent Crashes</h2>
  <div id="crashes"></div>
  <h2>Detailed Performance Sessions</h2>
  <div id="sessions"></div>
  <h2 id="heading"></h2>
  <canvas id="chart" width="1200" height="220" hidden></canvas>
  <pre id="details" hidden></pre>
</div>
<script>
let token=''; const el=id=>document.getElementById(id); const say=t=>el('status').textContent=t;
const issueRe=/^https:\\/\\/github\\.com\\/owendavidgoode\\/clonedivers\\/issues\\/(\\d+)$/;
async function api(path){const r=await fetch(path,{headers:{Authorization:'Bearer '+token},cache:'no-store'});if(!r.ok)throw Error('Request failed ('+r.status+')');return r.json()}
function badge(text,cls){const s=document.createElement('span');s.className='badge '+cls;s.textContent=text;return s}
function fmtDur(sec){if(typeof sec!=='number'||sec<0)return '';const m=Math.floor(sec/60),s=sec%60;return m?m+'m '+s+'s':s+'s'}
function fmtFps(avg,low){const a=typeof avg==='number'?avg.toFixed(1):'—',l=typeof low==='number'?low.toFixed(1):'—';return a==='—'&&l==='—'?'—':a+' / '+l}
function fmtHw(gpu,ram){const parts=[];if(gpu)parts.push(gpu);if(typeof ram==='number')parts.push(ram.toFixed(ram%1?1:0)+' GB');return parts.join(' · ')||'—'}
function fmtMode(mode,tier){const parts=[];if(mode)parts.push(mode);if(tier)parts.push(tier);return parts.join(' · ')||'—'}
async function loadAll(){
  say('Loading squad readiness and sessions…');
  const [squadData,sessionRows]=await Promise.all([api('/admin/squad'),api('/admin/sessions')]);
  el('login').hidden=true;el('refresh').hidden=false;el('logout').hidden=false;el('content').hidden=false;
  showSquad(squadData.sessions||[]);
  showCrashes(squadData.crashes||[]);
  showSessions(sessionRows||[]);
  say((squadData.sessions||[]).length+' squad players · '+(squadData.crashes||[]).length+' recent crashes · '+(sessionRows||[]).length+' detailed sessions');
}
el('logout').onclick=()=>location.reload();
el('refresh').onclick=()=>loadAll().catch(e=>say(e.message));
el('login').onsubmit=async e=>{e.preventDefault();token=el('key').value;el('key').value='';try{await loadAll()}catch(err){say(err.message);token=''}};
function showSquad(rows){
  const hb=el('hash-badge');hb.replaceChildren();
  if(!rows.length){const d=document.createElement('div');d.className='empty';d.textContent='No squad sessions reported yet.';el('squad').replaceChildren(d);return}
  const hashes=[...new Set(rows.map(r=>r.match_hash).filter(Boolean))];
  if(hashes.length===1&&rows.length>1)hb.append(badge('Match hash synced','ok'));
  else if(hashes.length>1)hb.append(badge(hashes.length+' distinct match hashes','warn'));
  const t=document.createElement('table'),thead=t.createTHead().insertRow(),tbody=t.createTBody();
  for(const col of ['Player','Launcher','Match Hash','Mode / Tier','GPU / RAM','Avg / 1% Low FPS','Last Exit']){const th=document.createElement('th');th.textContent=col;thead.append(th)}
  const topHash=hashes[0]||'';
  for(const r of rows){
    const tr=tbody.insertRow();
    const cPlayer=tr.insertCell();cPlayer.textContent=r.player;
    const meta=document.createElement('small');meta.textContent=(r.host_label?r.host_label+' · ':'')+new Date(r.received_utc).toLocaleString();cPlayer.append(meta);
    tr.insertCell().textContent=r.launcher_version||'—';
    const cHash=tr.insertCell();
    if(r.match_hash){const code=document.createElement('code');code.textContent=r.match_hash.slice(0,12);code.title=r.match_hash;cHash.append(code);if(hashes.length>1&&r.match_hash!==topHash){cHash.append(' ',badge('mismatch','warn'))}}else cHash.textContent='—';
    tr.insertCell().textContent=fmtMode(r.mode,r.perf_tier);
    tr.insertCell().textContent=fmtHw(r.gpu,r.ram_gb);
    tr.insertCell().textContent=fmtFps(r.avg_fps,r.low1_fps);
    const cExit=tr.insertCell();
    cExit.append(badge('Exit '+r.exit_code,r.exit_code===0?'ok':'warn'));
    const dur=fmtDur(r.duration_sec);
    if(dur||r.status_summary){const sm=document.createElement('small');sm.textContent=[dur,r.status_summary].filter(Boolean).join(' · ');cExit.append(sm)}
  }
  el('squad').replaceChildren(t);
}
function showCrashes(rows){
  if(!rows.length){const d=document.createElement('div');d.className='empty';d.textContent='No unexpected crashes reported.';el('crashes').replaceChildren(d);return}
  const t=document.createElement('table'),thead=t.createTHead().insertRow(),tbody=t.createTBody();
  for(const col of ['When','Player','Launcher','Class / Exit','Mode / Tier','Summary','GitHub Issue','']){const th=document.createElement('th');th.textContent=col;thead.append(th)}
  for(const r of rows){
    const tr=tbody.insertRow();
    tr.insertCell().textContent=new Date(r.received_utc).toLocaleString();
    const cP=tr.insertCell();cP.textContent=r.player;if(r.gpu){const sm=document.createElement('small');sm.textContent=fmtHw(r.gpu,r.ram_gb);cP.append(sm)}
    tr.insertCell().textContent=r.launcher_version;
    const cCls=tr.insertCell();cCls.append(badge(r.crash_class,'err'));const ex=document.createElement('small');ex.textContent='Exit '+r.exit_code;cCls.append(ex);
    tr.insertCell().textContent=fmtMode(r.mode,r.perf_tier);
    tr.insertCell().textContent=r.summary;
    const cIss=tr.insertCell();
    const m=typeof r.github_issue_url==='string'?issueRe.exec(r.github_issue_url):null;
    if(m){const a=document.createElement('a');a.href=r.github_issue_url;a.target='_blank';a.rel='noopener noreferrer';a.textContent='#'+m[1];cIss.append(a)}else cIss.textContent='—';
    const cBtn=tr.insertCell();
    if(r.log_tail){const b=document.createElement('button');b.textContent='Log';b.onclick=()=>{el('heading').textContent='Crash · '+r.player+' · '+r.crash_class+' ('+new Date(r.received_utc).toLocaleString()+')';el('chart').hidden=true;el('details').hidden=false;el('details').textContent=r.summary+'\\n\\n'+r.log_tail};cBtn.append(b)}
  }
  el('crashes').replaceChildren(t);
}
function showSessions(rows){
  if(!rows.length){const d=document.createElement('div');d.className='empty';d.textContent='No detailed PresentMon / resource chunk sessions uploaded yet.';el('sessions').replaceChildren(d);return}
  const t=document.createElement('table'),thead=t.createTHead().insertRow(),tbody=t.createTBody();
  for(const s of ['Player','Started','Last record','Records','MiB','']){const th=document.createElement('th');th.textContent=s;thead.append(th)}
  for(const r of rows){const tr=tbody.insertRow();for(const v of [r.nickname,new Date(r.start).toLocaleString(),new Date(r.end).toLocaleString(),r.records,(r.bytes/1048576).toFixed(1)])tr.insertCell().textContent=v;const b=document.createElement('button');b.textContent='Inspect';b.onclick=()=>inspect(r);tr.insertCell().append(b)}
  el('sessions').replaceChildren(t);
}
async function inspect(s){try{say('Loading session…');el('heading').textContent=s.nickname+' · '+new Date(s.start).toLocaleString();const chunks=await api('/admin/session/'+s.session_id);let rows=[];for(const c of chunks){rows.push(...(await api('/admin/chunk/'+c.id)).records);if(rows.length>250000){say('Showing first 250,000 records; download individual chunks for more.');break}}rows.sort((a,b)=>Date.parse(a.utc)-Date.parse(b.utc));const context=rows.filter(r=>r.kind==='context');const events=rows.filter(r=>r.kind==='event'||r.kind==='crash');const resources=rows.filter(r=>r.kind==='resource');const groups=new Map();for(const r of rows.filter(r=>r.kind==='frame')){const k=r.fields.SwapChainAddress||'unknown';if(!groups.has(k))groups.set(k,[]);groups.get(k).push(r)}const frames=[...groups.values()].sort((a,b)=>b.length-a.length)[0]||[];const intervalField=['CPUFrameTime','MsBetweenPresents','MsBetweenAppStart'].find(k=>frames.some(r=>typeof r.fields[k]==='number'));const vals=frames.map(r=>r.fields[intervalField]).filter(v=>typeof v==='number'&&v>0).sort((a,b)=>a-b);const stats=vals.length?{frames:vals.length,intervalField,fps:1000/(vals.reduce((a,b)=>a+b,0)/vals.length),p95Ms:vals[Math.ceil(vals.length*.95)-1],p99Ms:vals[Math.ceil(vals.length*.99)-1],over50Ms:vals.filter(v=>v>50).length,method:'Largest swapchain; includes foreground/background, menus and loading; not display FPS'}:{frames:0};el('details').hidden=false;el('details').textContent=JSON.stringify({context,frameStatistics:stats,resourceSamples:resources.length,events},null,2);draw(resources);say('Loaded '+rows.length+' records. Resource chart: game private memory (GiB).')}catch(e){say(e.message)}}
function draw(rows){const canvas=el('chart');canvas.hidden=!rows.length;const c=canvas.getContext('2d');c.clearRect(0,0,1200,220);const valid=rows.filter(r=>typeof r.fields.privateBytes==='number');if(!valid.length)return;const start=Date.parse(valid[0].utc),span=Math.max(1,Date.parse(valid.at(-1).utc)-start),max=Math.max(...valid.map(r=>r.fields.privateBytes),1);c.strokeStyle='#69baff';c.beginPath();valid.forEach((r,i)=>{const x=10+(Date.parse(r.utc)-start)/span*1180,y=200-r.fields.privateBytes/max*170;i?c.lineTo(x,y):c.moveTo(x,y)});c.stroke();c.fillStyle='#dce8ff';c.fillText((max/1073741824).toFixed(2)+' GiB peak',12,15)}
</script></html>`;
