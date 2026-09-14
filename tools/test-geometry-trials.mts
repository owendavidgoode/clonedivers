import test from 'node:test';
import assert from 'node:assert/strict';
import { MeshoptSimplifier } from '../dist/meshoptimizer-v1.1/meshopt_simplifier.js';
import { murmur, parseUnit, groupInputs, assertExclusive, optimizeUnit, assertOutsideEqual, replaceArchivePayload } from './build-geometry-trials.mts';

function fixture(): { main: Buffer; gpu: Buffer; names: Map<string,string> } {
  const side=30, vertices=side*side, triangles=(side-1)*(side-1)*2, indices=triangles*3;
  const main=Buffer.alloc(1400),gpu=Buffer.alloc(vertices*2*16+indices*2*2),names=new Map<string,string>();
  const put=(at:number,value:number):void=>{main.writeUInt32LE(value,at);};
  put(48,128);put(92,256);put(100,768);
  put(128,1);put(132,8);put(152,2);put(156,32);put(160,52);
  for(let i=0;i<2;i++){const p=168+20*i;main.writeFloatLE(i?0.1:2,p);main.writeFloatLE(i?0:0.1,p+4);put(p+8,1);put(p+12,i);}
  put(256,1);put(260,8);const layout=264;
  put(layout+8,5);put(layout+12,4);put(layout+28,0);put(layout+32,2);put(layout+328,2);
  put(layout+352,vertices*2);put(layout+356,16);put(layout+392,indices*2);
  put(layout+416,0);put(layout+420,vertices*2*16);put(layout+424,vertices*2*16);put(layout+428,indices*2*2);
  put(768,2);put(772,12);put(776,232);
  for(let mesh=0;mesh<2;mesh++){
    const p=mesh?1000:780,bone=mesh?'g_body_LOD1':'g_body',boneHash=(murmur(bone)>>32n).toString(16).padStart(8,'0');names.set(boneHash,bone);
    put(p+40,Number(murmur(bone)>>32n));put(p+60,0);put(p+120,1);put(p+124,128);
    put(p+128+4,mesh*vertices);put(p+128+8,vertices);put(p+128+12,mesh*indices);put(p+128+16,indices);
    for(let y=0;y<side;y++)for(let x=0;x<side;x++){const at=(mesh*vertices+y*side+x)*16;gpu.writeUInt32LE(0xff00ffaa,at);gpu.writeFloatLE(x,at+4);gpu.writeFloatLE(y,at+8);gpu.writeFloatLE(0,at+12);}
    let cursor=vertices*2*16+mesh*indices*2;
    for(let y=0;y<side-1;y++)for(let x=0;x<side-1;x++)for(const v of [y*side+x,y*side+x+1,(y+1)*side+x,y*side+x+1,(y+1)*side+x+1,(y+1)*side+x]){gpu.writeUInt16LE(v,cursor);cursor+=2;}
  }
  return {main,gpu,names};
}

test('known Stingray resource hash',()=>{assert.equal(murmur('content/fac_cyborgs/vehicles/cyborg_dropship/cyborg_dropship').toString(16),'db90077e76faa025');});
test('group-local indices, nonzero vertex base, 16-bit indices and position offset four',()=>{
  const f=fixture(),info=parseUnit(f.main,f.gpu,f.names),mesh=info.meshes[1]!,group=mesh.groups[0]!;
  const input=groupInputs(f.gpu,info.layouts[0]!,group);
  assert.equal(info.layouts[0]!.width,2);assert.equal(group.vertexOffset,900);
  assert.deepEqual(Array.from(input.positions.subarray(0,6)),[0,0,0,1,0,0]);assert.equal(input.indices[0],0);
});
test('overlapping group index ranges rejected',()=>{
  const f=fixture();f.main.writeUInt32LE(0,1000+128+12);const info=parseUnit(f.main,f.gpu,f.names);
  assert.throws(()=>assertExclusive(info,info.meshes[1]!.groups[0]!),/overlap/);
});
test('out-of-range local vertex rejected before simplification',()=>{
  const f=fixture(),info=parseUnit(f.main,f.gpu,f.names),group=info.meshes[1]!.groups[0]!;
  f.gpu.writeUInt16LE(900,group.start);assert.throws(()=>groupInputs(f.gpu,info.layouts[0]!,group),/group-local/);
});
test('distant simplification preserves near indices, all vertices, and original inputs',async()=>{
  await MeshoptSimplifier.ready;const f=fixture(),originalMain=Buffer.from(f.main),originalGpu=Buffer.from(f.gpu);
  // Deterministic backend isolates writer permissions from the simplifier's quality heuristic.
  // Real candidate builds exercise the pinned WASM simplifier separately.
  const backend={...MeshoptSimplifier,simplifyWithAttributes:(indices:Uint32Array):[Uint32Array,number]=>[indices.slice(0,Math.floor(indices.length/6)*3),0]};
  const before=parseUnit(f.main,f.gpu,f.names),after=await optimizeUnit(f.main,f.gpu,f.names,'balanced',backend);
  assert.equal(after.changes.length,1,JSON.stringify(before.meshes.map(m=>({bone:m.bone,ranks:m.ranks}))));assert(after.changes[0]!.after<after.changes[0]!.before);
  const near=before.meshes[0]!.groups[0]!,vertexEnd=before.layouts[0]!.vertexBytes;
  assert(after.gpu.subarray(near.start,near.end).equals(f.gpu.subarray(near.start,near.end)));
  assert(after.gpu.subarray(0,vertexEnd).equals(f.gpu.subarray(0,vertexEnd)));
  assert(originalMain.equals(f.main));assert(originalGpu.equals(f.gpu));
});
test('invalid simplifier output never reaches a patch',async()=>{
  await MeshoptSimplifier.ready;const f=fixture();
  const backend={...MeshoptSimplifier,simplifyWithAttributes:():[Uint32Array,number]=>[new Uint32Array([9999,0,1]),0]};
  await assert.rejects(optimizeUnit(f.main,f.gpu,f.names,'balanced',backend),/invalid vertex/);
});
test('shadow-only leaves visible LODs byte-identical',async()=>{
  await MeshoptSimplifier.ready;const f=fixture(),after=await optimizeUnit(f.main,f.gpu,f.names,'shadows',MeshoptSimplifier);
  assert.equal(after.changes.length,0);assert(after.main.equals(f.main));assert(after.gpu.equals(f.gpu));
});
test('unexpected metadata mutation is detected',()=>{const a=Buffer.alloc(20),b=Buffer.from(a);b[9]=1;assert.throws(()=>assertOutsideEqual(a,b,[[0,4]]),/preserved tail/);});
test('vertex overlap is rejected independently of index overlap',()=>{
  const f=fixture(),info=parseUnit(f.main,f.gpu,f.names),group=info.meshes[1]!.groups[0]!;
  info.layouts[0]!.vertexOffset=group.start;assert.throws(()=>assertExclusive(info,group),/vertex bytes/);
});

function archiveFixture(): {main:Buffer;gpu:Buffer} {
  const main=Buffer.alloc(512,0xaa),gpu=Buffer.alloc(256,0xbb);
  main.writeUInt32LE(0xf0000011,0);main.writeUInt32LE(1,4);main.writeUInt32LE(2,8);
  for(let i=0;i<2;i++){
    const p=104+i*80;main.writeBigUInt64LE(BigInt(i+1),p);main.writeBigUInt64LE(0xe0a48d0be9a7453fn,p+8);
    main.writeBigUInt64LE(BigInt(320+i*64),p+16);main.writeBigUInt64LE(BigInt(64+i*64),p+32);
    main.writeUInt32LE(32,p+56);main.writeUInt32LE(32,p+64);
  }
  return {main,gpu};
}
test('source layout writer preserves metadata, allocation fields, neighbors and padding',()=>{
  const f=archiveFixture(),payload=Buffer.alloc(32,0xcc);
  const main=replaceArchivePayload(f.main,f.main,payload,'0000000000000001',0);
  assert(main.subarray(320,352).equals(payload));assertOutsideEqual(f.main,main,[[320,352]]);
  assert(main.subarray(0,264).equals(f.main.subarray(0,264)));
  const gpu=replaceArchivePayload(f.main,f.gpu,payload,'0000000000000001',2);
  assert(gpu.subarray(64,96).equals(payload));assertOutsideEqual(f.gpu,gpu,[[64,96]]);
});
test('source layout writer rejects changed capacities and aliased resources',()=>{
  const f=archiveFixture();
  assert.throws(()=>replaceArchivePayload(f.main,f.main,Buffer.alloc(31),'0000000000000001',0),/capacity/);
  f.main.writeBigUInt64LE(64n,184+32);
  assert.throws(()=>replaceArchivePayload(f.main,f.gpu,Buffer.alloc(32),'0000000000000001',2),/overlaps another/);
});
test('source layout writer rejects metadata overlap',()=>{
  const f=archiveFixture();f.main.writeBigUInt64LE(100n,104+16);
  assert.throws(()=>replaceArchivePayload(f.main,f.main,Buffer.alloc(32),'0000000000000001',0),/archive metadata/);
});
test('skin influence boundaries lock every incident triangle corner',()=>{
  const f=fixture(),info=parseUnit(f.main,f.gpu,f.names),layout=info.layouts[0]!,group=info.meshes[0]!.groups[0]!;
  // Reinterpret fixture's packed color as one skin attribute; positions remain at +4.
  layout.items[0]!.kind=6;f.gpu.writeUInt32LE(123,0);
  const input=groupInputs(f.gpu,layout,group);
  assert.equal(input.locks[0],1);assert.equal(input.locks[1],1);assert.equal(input.locks[30],1);
  assert.equal(input.locks[899],0);
});
