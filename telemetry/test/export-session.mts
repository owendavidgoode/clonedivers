// OWNER_TOKEN is supplied in the environment, never as a command-line argument.
import { createWriteStream } from 'node:fs';
import { once } from 'node:events';
const [origin,session,output] = process.argv.slice(2);
if(!origin || !session || !output || !/^[a-f0-9]{32}$/.test(session) || !origin.startsWith('https://') || !process.env.OWNER_TOKEN)throw Error('Usage: OWNER_TOKEN=<secret> node test/export-session.mts https://collector session-id output.ndjson');
async function get(path:string):Promise<unknown>{const r=await fetch(new URL(path,origin),{redirect:'error',headers:{Authorization:'Bearer '+process.env.OWNER_TOKEN}});if(!r.ok)throw Error('Collector returned '+r.status);return r.json();}
const chunks=await get('/admin/session/'+session) as {id:string}[];
const file=createWriteStream(output,{flags:'wx'});
try{for(const chunk of chunks){const data=await get('/admin/chunk/'+chunk.id) as {records:unknown[]};for(const record of data.records){if(!file.write(JSON.stringify(record)+'\n'))await once(file,'drain');}}file.end();await once(file,'finish');}catch(e){file.destroy();throw e;}
console.log('Exported '+chunks.length+' chunks.');
