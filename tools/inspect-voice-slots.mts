import { readFile, writeFile } from 'node:fs/promises';
import { gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';

const root='dist/aim-voice-2026-09-24';
const bytes=gunzipSync(await readFile(`${root}/dl_library-v0.7.53.gz`));
assert.equal(bytes.toString('ascii',0,4),'LTLD');assert.equal(bytes.readUInt32LE(4),4);
const u=(p:number)=>bytes.readUInt32LE(p);
const [types,enums,members,values,aliases,defaults,strings]=Array.from({length:7},(_,i)=>u(8+i*4));
assert(types!==undefined&&enums!==undefined&&members!==undefined&&values!==undefined&&aliases!==undefined&&defaults!==undefined&&strings!==undefined);
const typeStart=36+4*(types+enums),enumStart=typeStart+36*types,memberStart=enumStart+32*enums;
// v0.7.53 documents the expanded 72-byte member record (including UnknownOffset).
const memberSize=72;
const valueStart=memberStart+memberSize*members,aliasStart=valueStart+16*values,stringStart=aliasStart+8*aliases+defaults;
assert.equal(stringStart+strings,bytes.length);
const hash=(s:string)=>{let h=5381;for(const c of s)h=(Math.imul(h,33)+c.charCodeAt(0))>>>0;return (h-5381)>>>0;};
const names=new Map((await readFile('dist/rc-upgrade/filediver-source/hashes/dl_type_names.txt','utf8')).split(/\r?\n/).filter(s=>s&&!s.startsWith('//')).map(s=>[hash(s),s]));
const name=(h:number)=>names.get(h)??`0x${h.toString(16)}`;
const str=(offset:number)=>{
  if(offset===0xffffffff||offset>=strings)return `unresolved:${offset}`;
  const end=bytes.indexOf(0,stringStart+offset);assert(end>=stringStart+offset&&end<bytes.length);
  return bytes.toString('utf8',stringStart+offset,end);
};
const enumRows=[];
for(let i=0;i<enums;i++){
  const label=name(u(36+types*4+i*4));if(!/voice/i.test(label))continue;
  const at=enumStart+i*32,count=u(at+12),first=u(at+16);assert(first+count<=values);
  enumRows.push({name:label,values:Array.from({length:count},(_,j)=>{
    const v=valueStart+16*(first+j),a=u(v);assert(a<aliases);
    return {value:Number(bytes.readBigUInt64LE(v+8)),name:str(u(aliasStart+a*8))};
  })});
}
const typeRows=[];
for(let i=0;i<types;i++){
  const label=name(u(36+i*4));if(!/voice|customization/i.test(label))continue;
  const at=typeStart+i*36,count=u(at+24),first=u(at+28);assert(first+count<=members);
  typeRows.push({name:label,size64:u(at+12),members:Array.from({length:count},(_,j)=>{
    const m=memberStart+memberSize*(first+j);return {name:str(u(m)),type:name(u(m+16)),offset64:u(m+40),size64:u(m+24)};
  })});
}
const result={source:'https://github.com/xypwn/filediver/tree/v0.7.53/datalibrary',sha256:createHash('sha256').update(bytes).digest('hex'),
  provenance:'Published extracted schema, not a live-game selector or network capability test',enums:enumRows,types:typeRows};
await writeFile(`${root}/voice-schema.json`,JSON.stringify(result,null,2));
console.log(JSON.stringify({enums:enumRows,types:typeRows.map(x=>({name:x.name,size64:x.size64}))},null,2));
