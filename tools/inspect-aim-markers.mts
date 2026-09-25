import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { parseUnit, groupInputs, murmur, range } from './build-geometry-trials.mts';

// Read-only inspection. No guessed weak-point positions are written into game assets.
const output = 'dist/aim-voice-2026-09-24';
await mkdir(output, {recursive:true});
const names = new Map<string,string>();
for (const filename of ['hashes.txt','thinhashes.txt']) {
  for (const line of (await readFile(`dist/rc-upgrade/filediver-source/hashes/${filename}`, 'utf8')).split(/\r?\n/)) {
    if (!line || line.startsWith('//')) continue;
    const hash=murmur(line);
    names.set(hash.toString(16).padStart(16,'0'),line);
    names.set((hash>>32n).toString(16).padStart(8,'0'),line);
  }
}
const main=await readFile(`${output}/spider/9ba626afa44a3aa3.patch_192`);
const gpu=await readFile(`${output}/spider/9ba626afa44a3aa3.patch_192.gpu_resources`);
const resources=[];
for(let i=0;i<main.readUInt32LE(8);i++) {
  const at=72+main.readUInt32LE(4)*32+i*80;
  range(main,at,80);
  const id=main.readBigUInt64LE(at).toString(16).padStart(16,'0');
  const type=main.readBigUInt64LE(at+8).toString(16).padStart(16,'0');
  if(type!=='e0a48d0be9a7453f' || id!=='ef570293245a17c2')continue;
  const start=Number(main.readBigUInt64LE(at+16)),size=main.readUInt32LE(at+56);
  const gpuStart=Number(main.readBigUInt64LE(at+32)),gpuSize=main.readUInt32LE(at+64);
  range(main,start,size);range(gpu,gpuStart,gpuSize);
  const unit=main.subarray(start,start+size),geometry=gpu.subarray(gpuStart,gpuStart+gpuSize);
  const info=parseUnit(unit,geometry,names);
  const jointAt=unit.readUInt32LE(52),count=unit.readUInt32LE(jointAt);
  assert(count<1000);range(unit,jointAt+16,count*136);
  const joints=Array.from({length:count},(_,j)=>{
    const transform=jointAt+16+j*64, matrix=jointAt+16+count*64+j*64;
    const hash=unit.readUInt32LE(jointAt+16+132*count+j*4).toString(16).padStart(8,'0');
    return {index:j,name:names.get(hash)??hash,parent:unit.readUInt16LE(jointAt+16+128*count+j*4+2),
      translation:[0,1,2].map(k=>unit.readFloatLE(transform+36+4*k)),
      matrix:Array.from({length:16},(_,k)=>unit.readFloatLE(matrix+4*k))};
  });
  const meshes=info.meshes.map(mesh=>({id:mesh.id,bone:mesh.bone,ranks:mesh.ranks,groups:mesh.groups.map(group=>{
    const input=groupInputs(geometry,info.layouts[mesh.layout]!,group);
    const bounds=[0,1,2].map(axis=>{let low=Infinity,high=-Infinity;for(let i=axis;i<input.positions.length;i+=3){low=Math.min(low,input.positions[i]!);high=Math.max(high,input.positions[i]!);}return [low,high];});
    return {material:group.material,vertices:group.vertices,triangles:group.indices/3,bounds};
  })}));
  const resource={id,name:names.get(id)??id,sha256:createHash('sha256').update(unit).digest('hex'),joints,meshes};
  resources.push(resource);
  await writeFile(`${output}/spider/${id}.unit.main`,unit);
  await writeFile(`${output}/spider/${id}.unit.gpu`,geometry);
  console.log(JSON.stringify({id,name:resource.name,joints:count,meshCount:meshes.length}));
}
await writeFile(`${output}/spider-inspection.json`,JSON.stringify({resources},null,2));
