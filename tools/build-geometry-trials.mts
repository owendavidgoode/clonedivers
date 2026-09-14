import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';
import assert from 'node:assert/strict';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const unitType = 'e0a48d0be9a7453f';
const simplifierSha = '127f0bcdca85d0c4252a4b884c30c3d0c96fd1855ec1d18bd640f88f4742ee4f';
type JsonObject = Record<string, unknown>;
function object(value: unknown): JsonObject { assert(value && typeof value === 'object' && !Array.isArray(value)); return value as JsonObject; }
function array(value: unknown): unknown[] { assert(Array.isArray(value)); return value; }
function text(value: unknown): string { assert.equal(typeof value, 'string'); return value as string; }
function number(value: unknown): number { assert.equal(typeof value, 'number'); assert(Number.isFinite(value)); return value as number; }
const hash = (data: Uint8Array): string => createHash('sha256').update(data).digest('hex');
export function range(data: Uint8Array, start: number, length: number): void {
  assert(Number.isSafeInteger(start) && Number.isSafeInteger(length) && start >= 0 && length >= 0 && start + length <= data.length, 'Resource range out of bounds');
}
function u32(data: Buffer, offset: number): number { range(data, offset, 4); return data.readUInt32LE(offset); }
function hex(data: Buffer, offset: number): string { range(data, offset, 8); return data.readBigUInt64LE(offset).toString(16).padStart(16, '0'); }
function offset64(data: Buffer, offset: number): number { const n = Number(data.readBigUInt64LE(offset)); assert(Number.isSafeInteger(n)); return n; }
function pointers(data: Buffer, offset: number): number[] {
  if (!offset) return [];
  const n = u32(data, offset); assert(n < 10000); range(data, offset + 4, n * 4);
  return Array.from({ length: n }, (_, i) => offset + u32(data, offset + 4 + i * 4));
}
export function murmur(value: string): bigint {
  const b = Buffer.from(value); const mask = (1n << 64n) - 1n; const m = 0xc6a4a7935bd1e995n;
  let h = BigInt(b.length) * m & mask; let p = 0;
  for (; p + 8 <= b.length; p += 8) { let k = b.readBigUInt64LE(p) * m & mask; k ^= k >> 47n; k = k * m & mask; h ^= k; h = h * m & mask; }
  if (p < b.length) { for (let i = 0; p + i < b.length; i++) h ^= BigInt(b[p + i]!) << BigInt(8 * i); h = h * m & mask; }
  h ^= h >> 47n; h = h * m & mask; return h ^ h >> 47n;
}
type Item = { kind: number; format: number; layer: number; offset: number; size: number };
type Layout = { vertices: number; stride: number; vertexOffset: number; vertexBytes: number; indices: number; indexOffset: number; width: number; items: Item[] };
type Group = { at: number; material: number; vertexOffset: number; vertices: number; indexOffset: number; indices: number; start: number; end: number };
type Mesh = { id: number; layout: number; bone: string; groups: Group[]; ranks: number[] };
export type UnitInfo = { layouts: Layout[]; meshes: Mesh[] };
const formatSizes = new Map([[0,4],[1,8],[2,12],[3,16],[4,4],[21,4],[22,8],[23,12],[24,16],[25,1],[26,2],[27,3],[28,4],[29,4],[30,4],[32,2],[33,4],[34,6],[35,8]]);
export function parseUnit(main: Buffer, gpu: Buffer, names: ReadonlyMap<string, string>): UnitInfo {
  range(main, 0, 116); assert.equal(hex(main, 16), '0000000000000000', 'Only inline mod geometry supported');
  const layouts = pointers(main, u32(main, 92)).map((at): Layout => {
    range(main, at, 448); const n = u32(main, at + 328); assert(n <= 16);
    let cursor = 0;
    const items = Array.from({ length: n }, (_, i): Item => {
      const kind = u32(main, at + 8 + 20 * i), format = u32(main, at + 12 + 20 * i), layer = u32(main, at + 16 + 20 * i);
      const size = formatSizes.get(format); assert(size, `Unknown vertex format ${format}`);
      const item = { kind, format, layer, offset: cursor, size }; cursor += size; return item;
    });
    const vertices = u32(main, at + 352), stride = u32(main, at + 356), indices = u32(main, at + 392);
    const vertexOffset = u32(main, at + 416), vertexBytes = u32(main, at + 420), indexOffset = u32(main, at + 424), indexBytes = u32(main, at + 428);
    assert(cursor <= stride && vertices > 0 && indices > 0); const width = indexBytes / indices; assert(width === 2 || width === 4);
    range(gpu, vertexOffset, vertices * stride); range(gpu, vertexOffset, vertexBytes); range(gpu, indexOffset, indexBytes);
    assert.equal(items.filter(i => i.kind === 0 && i.format === 2).length, 1, 'Require one float3 position attribute');
    return { vertices, stride, vertexOffset, vertexBytes, indices, indexOffset, width, items };
  });
  const meshes = pointers(main, u32(main, 100)).map((at, id): Mesh => {
    range(main, at, 128); const li = main.readInt32LE(at + 60); assert(li >= 0 && li < layouts.length);
    const layout = layouts[li]!; const count = u32(main, at + 120); assert(count < 10000);
    const groups = Array.from({ length: count }, (_, i): Group => {
      const p = at + u32(main, at + 124) + 24 * i; range(main, p, 24);
      const material = u32(main, p), vertexOffset = u32(main, p + 4), vertices = u32(main, p + 8), indexOffset = u32(main, p + 12), indices = u32(main, p + 16);
      assert(vertexOffset + vertices <= layout.vertices && indexOffset + indices <= layout.indices && indices % 3 === 0);
      const start = layout.indexOffset + indexOffset * layout.width, end = start + indices * layout.width; range(gpu, start, end - start);
      return { at: p, material, vertexOffset, vertices, indexOffset, indices, start, end };
    });
    const boneHash = u32(main, at + 40).toString(16).padStart(8, '0');
    return { id, layout: li, bone: names.get(boneHash) ?? `unknown:${boneHash}`, groups, ranks: [] };
  });
  for (const at of pointers(main, u32(main, 48))) {
    const count = u32(main, at + 16); assert(count < 1000);
    for (let rank = 0; rank < count; rank++) {
      const p = at + u32(main, at + 20 + rank * 4), n = u32(main, p + 8); assert(n < 10000);
      for (let i = 0; i < n; i++) { const id = u32(main, p + 12 + i * 4); assert(id < meshes.length); meshes[id]!.ranks.push(rank); }
    }
  }
  return { layouts, meshes };
}
export function assertExclusive(info: UnitInfo, selected: Group): void {
  for (const mesh of info.meshes) for (const other of mesh.groups) {
    if (other === selected) continue;
    assert(selected.end <= other.start || selected.start >= other.end, 'Selected indices overlap another group');
  }
  for (const layout of info.layouts) assert(selected.end <= layout.vertexOffset || selected.start >= layout.vertexOffset + Math.max(layout.vertexBytes, layout.vertices * layout.stride), 'Selected indices overlap vertex bytes');
}
function half(bits: number): number {
  const sign = bits & 0x8000 ? -1 : 1, exp = bits >> 10 & 31, mant = bits & 1023;
  return sign * (exp === 0 ? mant * 2 ** -24 : exp === 31 ? (mant ? NaN : Infinity) : (1 + mant / 1024) * 2 ** (exp - 15));
}
export function groupInputs(gpu: Buffer, layout: Layout, group: Group): { indices: Uint32Array; positions: Float32Array; attributes: Float32Array; stride: number; locks: Uint8Array } {
  const indices = new Uint32Array(group.indices);
  for (let i = 0; i < indices.length; i++) { const at = group.start + i * layout.width; indices[i] = layout.width === 2 ? gpu.readUInt16LE(at) : gpu.readUInt32LE(at); assert(indices[i]! < group.vertices, 'Index is not group-local'); }
  const position = layout.items.find(i => i.kind === 0)!;
  const uv = layout.items.filter(i => i.kind === 4); assert(uv.every(i => i.format === 1 || i.format === 33));
  const skin = layout.items.filter(i => i.kind === 6 || i.kind === 7);
  const positions = new Float32Array(group.vertices * 3), attributes = new Float32Array(group.vertices * uv.length * 2);
  const signatures: string[] = []; const locks = new Uint8Array(group.vertices);
  for (let i = 0; i < group.vertices; i++) {
    const at = layout.vertexOffset + (group.vertexOffset + i) * layout.stride;
    for (let axis = 0; axis < 3; axis++) { const value = gpu.readFloatLE(at + position.offset + axis * 4); assert(Number.isFinite(value)); positions[i * 3 + axis] = value; }
    for (let j = 0; j < uv.length; j++) for (let axis = 0; axis < 2; axis++) {
      const item = uv[j]!; const value = item.format === 1 ? gpu.readFloatLE(at + item.offset + axis * 4) : half(gpu.readUInt16LE(at + item.offset + axis * 2));
      assert(Number.isFinite(value)); attributes[i * uv.length * 2 + j * 2 + axis] = value;
    }
    signatures.push(skin.map(item => gpu.subarray(at + item.offset, at + item.offset + item.size).toString('hex')).join(':'));
  }
  // Conservative protection: freeze triangle corners wherever skin influence bytes vary.
  for (let i = 0; i < indices.length; i += 3) {
    const a = indices[i]!, b = indices[i + 1]!, c = indices[i + 2]!;
    if (signatures[a] !== signatures[b] || signatures[b] !== signatures[c]) locks[a] = locks[b] = locks[c] = 1;
  }
  return { indices, positions, attributes, stride: uv.length * 2, locks };
}
type Simplifier = { ready: Promise<void>; supported: boolean; simplifyWithAttributes: (indices: Uint32Array, positions: Float32Array, ps: number, attrs: Float32Array, as: number, weights: number[], locks: Uint8Array, target: number, error: number, flags: string[]) => [Uint32Array, number] };
type Variant = 'shadows' | 'balanced';
type GroupChange = { mesh: number; bone: string; material: number; rank: number; before: number; after: number; error: number; lockedVertices: number; gpuStart: number; gpuLength: number; countOffset: number };
export async function optimizeUnit(main: Buffer, gpu: Buffer, names: ReadonlyMap<string, string>, variant: Variant, simplifier: Simplifier): Promise<{ main: Buffer; gpu: Buffer; changes: GroupChange[] }> {
  const info = parseUnit(main, gpu, names), outMain = Buffer.from(main), outGpu = Buffer.from(gpu), changes: GroupChange[] = [];
  for (const mesh of info.meshes) {
    const shadow = mesh.bone.includes('shadow');
    if (mesh.bone.startsWith('unknown:') || (!shadow && variant === 'shadows')) continue;
    if (!shadow && (!mesh.ranks.length || mesh.ranks.includes(0))) continue;
    const rank = mesh.ranks.length ? Math.min(...mesh.ranks) : 0;
    const ratio = shadow ? [0.25, 0.125, 0.0625, 0.03125][Math.min(rank, 3)]! : [1, 0.5, 0.25, 0.125, 0.0625][Math.min(rank, 4)]!;
    const error = shadow ? 0.01 : Math.min(0.002 * rank, 0.008);
    for (const group of mesh.groups) {
      if (group.indices < 300) continue;
      assertExclusive(info, group);
      const layout = info.layouts[mesh.layout]!, input = groupInputs(gpu, layout, group);
      const target = Math.max(96, Math.floor(group.indices * ratio / 3) * 3);
      const [indices, resultError] = simplifier.simplifyWithAttributes(input.indices, input.positions, 3, input.attributes, input.stride, Array(input.stride).fill(0.1), input.locks, target, error, ['LockBorder', 'Regularize']);
      assert(indices.length > 0 && indices.length <= group.indices && indices.length % 3 === 0);
      assert(Number.isFinite(resultError) && resultError <= error + 0.000001);
      if (indices.length === group.indices) continue;
      const originalVertices = new Set(input.indices);
      for (let i = 0; i < indices.length; i++) {
        const value = indices[i]!; assert(value < group.vertices && originalVertices.has(value), 'Simplifier introduced an invalid vertex');
        if (layout.width === 2) outGpu.writeUInt16LE(value, group.start + i * 2); else outGpu.writeUInt32LE(value, group.start + i * 4);
      }
      outMain.writeUInt32LE(indices.length, group.at + 16);
      changes.push({ mesh: mesh.id, bone: mesh.bone, material: group.material, rank, before: group.indices / 3, after: indices.length / 3, error: resultError, lockedVertices: input.locks.reduce((a,b) => a+b,0), gpuStart: group.start, gpuLength: indices.length * layout.width, countOffset: group.at + 16 });
    }
  }
  const gpuRanges = changes.map(c => [c.gpuStart, c.gpuStart + c.gpuLength] as const).sort((a,b) => a[0]-b[0]);
  const mainRanges = changes.map(c => [c.countOffset, c.countOffset + 4] as const).sort((a,b) => a[0]-b[0]);
  assertOutsideEqual(main, outMain, mainRanges); assertOutsideEqual(gpu, outGpu, gpuRanges);
  const checked = parseUnit(outMain, outGpu, names);
  for (const mesh of checked.meshes) for (const group of mesh.groups) {
    const layout = checked.layouts[mesh.layout]!;
    for (let i=0; i<group.indices; i++) { const at=group.start+i*layout.width; assert((layout.width === 2 ? outGpu.readUInt16LE(at) : outGpu.readUInt32LE(at)) < group.vertices); }
  }
  return { main: outMain, gpu: outGpu, changes };
}
export function assertOutsideEqual(before: Buffer, after: Buffer, ranges: ReadonlyArray<readonly [number, number]>): void {
  assert.equal(before.length, after.length); let p = 0;
  for (const [start,end] of ranges) { assert(start >= p && end <= before.length); assert(before.subarray(p,start).equals(after.subarray(p,start)), 'Unexpected mutation outside permitted fields'); p=end; }
  assert(before.subarray(p).equals(after.subarray(p)), 'Unexpected mutation in preserved tail');
}
type Resource = { id: string; row: Buffer; header: Buffer; typeRow: Buffer; main: Buffer; gpu: Buffer; stream: Buffer; name: string; sources: { name: string; sha256: string }[] };
async function loadResource(id: string, patch: string, name: string, expected: ReadonlyMap<string,string>, directory: string): Promise<Resource> {
  assert(/^[a-f0-9]{16}$/.test(id) && /^9ba626afa44a3aa3\.patch_\d+$/.test(patch));
  const main=await readFile(join(directory,patch)); assert.equal(hash(main),expected.get(patch),'Source main differs from published pack');
  assert.equal(u32(main,0),0xf0000011); const nt=u32(main,4), nr=u32(main,8); range(main,72,nt*32+nr*80);
  let row: Buffer|undefined, typeRow: Buffer|undefined;
  for(let i=0;i<nt;i++){const p=72+32*i;if(hex(main,p+8)===unitType) typeRow=Buffer.from(main.subarray(p,p+32));}
  for(let i=0;i<nr;i++){const p=72+32*nt+80*i;if(hex(main,p)===id && hex(main,p+8)===unitType){assert(!row);row=Buffer.from(main.subarray(p,p+80));}}
  assert(row && typeRow); const sources=[{name:patch,sha256:hash(main)}];
  const pieces: Buffer[]=[];
  for(let part=0;part<3;part++){
    const size=u32(row,56+4*part), offset=offset64(row,16+8*part);let data=main;
    if(part && size){const filename=patch+(part===1?'.stream':'.gpu_resources');data=await readFile(join(directory,filename));assert.equal(hash(data),expected.get(filename));sources.push({name:filename,sha256:hash(data)});}
    if(!size){pieces.push(Buffer.alloc(0));continue;}range(data,offset,size);pieces.push(Buffer.from(data.subarray(offset,offset+size)));
  }
  return {id,name,row,header:Buffer.from(main.subarray(0,72)),typeRow,main:pieces[0]!,stream:pieces[1]!,gpu:pieces[2]!,sources};
}
// Preserve the complete working donor: headers, allocation metadata, TOC,
// alignments, neighboring resources, padding and load order are never rebuilt.
export function replaceArchivePayload(archive: Buffer, original: Buffer, payload: Buffer, id: string, part: 0|2): Buffer {
  assert.equal(u32(archive,0),0xf0000011);
  const nt=u32(archive,4),nr=u32(archive,8),tocEnd=72+nt*32+nr*80;
  range(archive,72,nt*32+nr*80);
  const rows=Array.from({length:nr},(_,i)=>72+nt*32+i*80);
  const selected=rows.filter(p=>hex(archive,p)===id && hex(archive,p+8)===unitType);
  assert.equal(selected.length,1,'Require exactly one matching unit');
  const row=selected[0]!,start=offset64(archive,row+16+part*8),size=u32(archive,row+56+part*4);
  assert.equal(payload.length,size,'Resource capacity must remain unchanged');range(original,start,size);
  if(part===0){assert(original.equals(archive));assert(start>=tocEnd,'Resource overlaps archive metadata');}
  for(const p of rows){if(p===row)continue;const other=offset64(archive,p+16+part*8),length=u32(archive,p+56+part*4);if(length)assert(start+size<=other||start>=other+length,'Resource overlaps another archive resource');}
  const result=Buffer.from(original);payload.copy(result,start);
  assertOutsideEqual(original,result,[[start,start+size]]);
  if(part===0)assert(result.subarray(0,tocEnd).equals(archive.subarray(0,tocEnd)));
  return result;
}
async function main(): Promise<void> {
  const { values }=parseArgs({options:{out:{type:'string'},variant:{type:'string',default:'shadows'},profile:{type:'string',default:'lighter'},source:{type:'string'}}});
  assert(values.variant==='shadows'||values.variant==='balanced');const variant=values.variant;
  assert(values.profile==='full'||values.profile==='lighter');const profile=values.profile;
  const directory=resolve(values.source??join(root,'dist/pack-optimized-current'));
  const out=resolve(values.out??join(root,'dist/geometry-candidates/layout-preserved',profile,variant));
  assert(out.startsWith(join(root,'dist')+ '\\') || out.startsWith(join(root,'dist')+'/'),'Output must be inside workspace dist');
  try {await access(out);throw new Error('Output already exists; choose a fresh directory');}catch(e){if(!(e instanceof Error && 'code' in e && e.code==='ENOENT'))throw e;}
  const vendor=join(root,'dist/meshoptimizer-v1.1/meshopt_simplifier.js');assert.equal(hash(await readFile(vendor)),simplifierSha);
  const loaded: unknown=await import(pathToFileURL(vendor).href);const simplifier=object(loaded).MeshoptSimplifier as Simplifier;
  assert(simplifier.supported && typeof simplifier.simplifyWithAttributes==='function');await simplifier.ready;
  assert.equal(murmur('content/fac_cyborgs/vehicles/cyborg_dropship/cyborg_dropship').toString(16),'db90077e76faa025');
  const names=new Map<string,string>();for(const line of (await readFile(join(root,'dist/rc-upgrade/filediver-source/hashes/thinhashes.txt'),'utf8')).split(/\r?\n/)){if(line&&!line.startsWith('//'))names.set((murmur(line)>>32n).toString(16).padStart(8,'0'),line);}
  const inventory=array(JSON.parse((await readFile(join(root,'dist/investigation-2026-09-13/geometry/representative-meshes.json'),'utf8')).replace(/^\uFEFF/,''))).map(object);
  const manifest=object(JSON.parse((await readFile(join(root,'manifest.json'),'utf8')).replace(/^\uFEFF/,'')));const pack=object(manifest.pack);assert.equal(pack.version,'2026.09.12-r8');
  const enabled: Record<string,boolean>={skinny:profile==='lighter',commandos:true};const expected=new Map<string,string>();
  for(const row of array(pack.files).map(object)){if(row.option&&!enabled[text(row.option)]||row.unlessOption&&enabled[text(row.unlessOption)])continue;expected.set(text(row.name),text(row.sha256));}
  const ids=new Set(['db90077e76faa025','282eb766c1ffa6a1','ae63e525853d7044','35dbf54f016f3624','c2d449ecf7facab1','7b2326f6fd9c8069','79e4b3d2da5e45e3','76cf8e26aad1bf7e']);
  const reports:unknown[]=[],files:{name:string;size:number;sha256:string;sourceSha256:string}[]=[];
  await mkdir(out,{recursive:true});
  for(const entry of inventory){const id=text(entry.id);if(!ids.has(id))continue;
    const patch=text(entry.patch),r=await loadResource(id,patch,text(entry.folder),expected,directory);
    const optimized=await optimizeUnit(r.main,r.gpu,names,variant,simplifier);
    assert(optimized.changes.length>0,`No useful reduction for ${r.name}`);
    const archive=await readFile(join(directory,patch));assert.equal(hash(archive),expected.get(patch));
    for(const [part,payload] of [[0,optimized.main],[2,optimized.gpu]] as const){
      const name=patch+(part===2?'.gpu_resources':'');assert(!files.some(f=>f.name===name),'Selected resources share a donor; merge explicitly before staging');
      const source=part===0?archive:await readFile(join(directory,name));assert.equal(hash(source),expected.get(name));
      const output=replaceArchivePayload(archive,source,payload,id,part);
      await writeFile(join(out,name),output,{flag:'wx'});files.push({name,size:output.length,sha256:hash(output),sourceSha256:hash(source)});
    }
    reports.push({id,name:r.name,sources:r.sources,originalMainSha256:hash(r.main),originalGpuSha256:hash(r.gpu),outputMainSha256:hash(optimized.main),outputGpuSha256:hash(optimized.gpu),changes:optimized.changes});
    const before=optimized.changes.reduce((s,c)=>s+c.before,0),after=optimized.changes.reduce((s,c)=>s+c.after,0);
    console.log(`${r.name}: ${before} -> ${after} triangle equivalents across changed groups`);
  }
  assert.equal(reports.length,ids.size);
  await writeFile(join(out,'build-report.json'),JSON.stringify({schemaVersion:2,packaging:'preserved-source-layout',profile,variant,pack:'2026.09.12-r8',node:process.version,builderSha256:hash(await readFile(fileURLToPath(import.meta.url))),simplifier:{version:'v1.1',sha256:simplifierSha,license:'MIT',source:'https://github.com/zeux/meshoptimizer/tree/v1.1'},resources:reports,files,runtimeTested:false,installed:false,published:false,policy:'Disjoint index prefixes and MeshGroup.NumIndices only; original vertices, skinning maps, materials, nearest visible slots, bounds and layout capacities unchanged. Border locking and exact skin-byte transition locks; no permissive/prune simplification. UV-aware error limits. No claimed memory/FPS savings.',validation:'Every output index references an existing vertex within its original group. All other bytes preserved. Entire source archive layout, metadata and neighboring resources retained. Animation and visual acceptance pending.'},null,2)+'\n',{flag:'wx'});
  console.log(`Staged ${variant}: ${files.reduce((s,f)=>s+f.size,0)} bytes; not installed.`);
}
if(import.meta.main)main().catch((error:unknown)=>{console.error(error);process.exitCode=1;});
