import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import {parseUnit,groupInputs,murmur,range} from './build-geometry-trials.mts';

// Build a local visual-only marker trial for the Spider Droid (War Strider replacement).
// Markers sit on the stock dmg_eye / dmg_vents anchors, rigidly skinned to the bones that
// carry the stock eye (turret) and vent (boss) geometry. This does not establish
// collision alignment during gameplay.
//
// v4 changes against v3:
// - The stock eye material is only ever used on layouts with four UV sets (War Strider,
//   Berserker, spawner). The Spider Droid body layouts have three, so the three visible
//   body layouts gain a zero UV3 channel (stride 44 -> 48). Every original vertex keeps
//   its bytes; only the UV3 slot is inserted.
// - Marker vertices get real normals and the stock eye's UV values instead of a copied
//   body normal and zero UVs.
// - Eye: bullseye ring and centre dot facing out of the Spider Droid ball.
//   Vents: three orthogonal rings and a centre dot, readable from any side.
const stage='dist/aim-voice-2026-09-24', out=process.argv[2]??`${stage}/markers-v4`;
await mkdir(out); // Fail rather than overwrite an earlier candidate.
const source=await readFile(`${stage}/spider/ef570293245a17c2.unit.main`);
const sourceGpu=await readFile(`${stage}/spider/ef570293245a17c2.unit.gpu`);
const native=await readFile('dist/vehicle-update-2026-09-23/current/content/fac_cyborgs/vehicles/cyborg_assault_walker/cyborg_assault_walker.unit.main');
const archive=await readFile(`${stage}/spider/9ba626afa44a3aa3.patch_192`);
const hash=(b:Buffer)=>createHash('sha256').update(b).digest('hex');
// The measured constants below (ball centre, stock eye UVs) belong to exactly this source.
assert.equal(hash(source),'c3b4cf22d24c8622e8b821d58b54b3c31d30306f486fbe0f37bedd5caf92711c','Unexpected Spider Droid unit');
const thin=(s:string)=>Number(murmur(s)>>32n);
const u=(b:Buffer,p:number)=>{range(b,p,4);return b.readUInt32LE(p);};
const table=(b:Buffer,field:number)=>{const a=u(b,field),n=u(b,a);assert(n<1000);return Array.from({length:n},(_,i)=>a+u(b,a+4+4*i));};
function joint(b:Buffer,name:string) {
  const at=u(b,52),n=u(b,at);assert(n<1000);
  const i=Array.from({length:n},(_,i)=>i).find(i=>u(b,at+16+n*132+i*4)===thin(name));assert(i!==undefined,`Missing joint ${name}`);
  const matrix=b.subarray(at+16+n*64+i*64,at+16+n*64+(i+1)*64);assert.equal(matrix.length,64);
  return {index:i,matrix,position:[0,1,2].map(k=>matrix.readFloatLE(48+4*k))};
}
const anchors=Object.fromEntries([{name:'dmg_eye',bone:'turret'},{name:'dmg_vents',bone:'boss'}].map(spec=>{
  const anchor=joint(source,spec.name),stock=joint(native,spec.name),bone=joint(source,spec.bone);
  assert(anchor.matrix.equals(stock.matrix),`Changed damage anchor ${spec.name}`);
  assert(bone.matrix.equals(joint(native,spec.bone).matrix),`Changed parent bind ${spec.bone}`);
  return [spec.name,{...spec,position:anchor.position,boneIndex:bone.index}];
}));

// --- marker geometry (Stingray model space: x lateral, y forward, z up) ---
type V3=[number,number,number];
const add=(a:V3,b:V3,s=1):V3=>[a[0]+b[0]*s,a[1]+b[1]*s,a[2]+b[2]*s];
const cross=(a:V3,b:V3):V3=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const unit=(a:V3):V3=>{const l=Math.hypot(...a);assert(l>1e-9);return [a[0]/l,a[1]/l,a[2]/l];};
function basis(n:V3):[V3,V3]{const t=unit(cross(n,Math.abs(n[2])<0.9?[0,0,1]:[1,0,0]));return [t,cross(n,t)];}
type Vertex={p:V3;n:V3;bone:number};
const vertices:Vertex[]=[],indices:number[]=[];
// Double-sided: each face gets its own vertices with an outward normal and matching winding.
function face(points:V3[],normal:V3,bone:number,tris:number[]){
  for(const side of [1,-1]){
    const base=vertices.length,n:V3=[normal[0]*side,normal[1]*side,normal[2]*side];
    for(const p of points)vertices.push({p,n,bone});
    for(let i=0;i<tris.length;i+=3)indices.push(...(side>0?[tris[i]!,tris[i+1]!,tris[i+2]!]:[tris[i]!,tris[i+2]!,tris[i+1]!]).map(v=>base+v));
  }
}
function ring(c:V3,normal:V3,outer:number,inner:number,bone:number,segments=32){
  const [a,b]=basis(normal),points:V3[]=[],tris:number[]=[];
  for(let i=0;i<segments;i++){const t=i*2*Math.PI/segments;for(const r of [outer,inner])points.push(add(add(c,a,Math.cos(t)*r),b,Math.sin(t)*r));}
  for(let i=0;i<segments;i++){const x=2*i,y=2*((i+1)%segments);tris.push(x,y,x+1,x+1,y,y+1);}
  face(points,normal,bone,tris);
}
function disc(c:V3,normal:V3,r:number,bone:number,segments=16){
  const [a,b]=basis(normal),points:V3[]=[c],tris:number[]=[];
  for(let i=0;i<segments;i++){const t=i*2*Math.PI/segments;points.push(add(add(c,a,Math.cos(t)*r),b,Math.sin(t)*r));}
  for(let i=0;i<segments;i++)tris.push(0,1+i,1+(i+1)%segments);
  face(points,normal,bone,tris);
}
function dot(c:V3,r:number,bone:number){ // flat-shaded octahedron
  const axes:V3[]=[[1,0,0],[0,1,0],[0,0,1]];
  for(const sx of [1,-1])for(const sy of [1,-1])for(const sz of [1,-1]){
    const p=[add(c,axes[0]!,sx*r),add(c,axes[1]!,sy*r),add(c,axes[2]!,sz*r)];
    const base=vertices.length,n=unit([sx,sy,sz]);for(const q of p)vertices.push({p:q,n,bone});
    indices.push(...(sx*sy*sz>0?[0,1,2]:[0,2,1]).map(v=>base+v));
  }
}
const eye=anchors.dmg_eye!,vents=anchors.dmg_vents!;
// Least-squares sphere fit of the source LOD0 body shell (1,905 support vertices, radius 1.772).
const ballCentre:V3=[0.0178,0.1193,4.5866];
const eyeAt=eye.position as V3,eyeFacing=unit(add(eyeAt,ballCentre,-1));
ring(eyeAt,eyeFacing,.16,.12,eye.boneIndex);disc(add(eyeAt,eyeFacing,.005),eyeFacing,.05,eye.boneIndex);
const ventAt=vents.position as V3;
for(const n of [[1,0,0],[0,1,0],[0,0,1]] as V3[])ring(ventAt,n,.26,.20,vents.boneIndex);
dot(ventAt,.07,vents.boneIndex);
const eyeCount=vertices.filter(v=>v.bone===eye.boneIndex).length;

// --- vertex encoding for the rewritten layout ---
const f16=(value:number)=>{ // IEEE half, round to nearest; inputs are small finite numbers
  const f=new Float32Array([value]),x=new Uint32Array(f.buffer)[0]!,sign=(x>>>16)&0x8000,e=((x>>>23)&0xff)-127+15,m=x&0x7fffff;
  if(e<=0)return sign; assert(e<31); let h=sign|(e<<10)|(m>>>13); if(m&0x1000)h++; return h;
};
const packNormal=(n:V3)=>{ // octahedral 10:10 normal, tangent rotation 0, see filediver packed_normal.go
  const l=Math.abs(n[0])+Math.abs(n[1])+Math.abs(n[2]);let x=n[0]/l,y=n[1]/l;
  if(n[2]<0)[x,y]=[(1-Math.abs(y))*Math.sign(x||1),(1-Math.abs(x))*Math.sign(y||1)];
  return (Math.round((x+1)*1023/2)|Math.round((y+1)*1023/2)<<10)>>>0;
};
// Stock War Strider eye vertices (LOD0/1, m_boteye): mean UV0..UV3 from the filediver export.
const eyeUv=[[7.4073,0.4948],[16.3911,-1.6726],[0.0252,0.9747],[0.8837,0.8495]];

const info=parseUnit(source,sourceGpu,new Map());
const meshAt=table(source,100),layoutAt=table(source,92),mapAt=table(source,88);
const selected=info.meshes.filter(m=>m.id>=14);assert.equal(selected.length,5); // visible body LODs; 0-3 shadow, 4-13 culling
const selectedLayouts=[...new Set(selected.map(m=>m.layout))];
const oldItems=(li:number)=>info.layouts[li]!.items.map(i=>`${i.kind}/${i.format}/${i.layer}`).join(' ');
for(const li of selectedLayouts)assert.equal(oldItems(li),'5/4/0 0/2/0 1/30/0 4/33/0 4/33/1 4/33/2 7/35/0 6/28/0','Unexpected body layout');
const OLD=44,NEW=48,UV3=32; // UV3 half2 inserted after UV2; weights move to 36, bone indices to 44
const selectedMaps=new Set(selected.map(m=>source.readInt32LE(meshAt[m.id]!+56)));
const boneOrders=new Map<number,number[]>();
const mapRecords=mapAt.map((at,i)=>{
  const n=u(source,at),mat=u(source,at+4),bones=u(source,at+8),remap=u(source,at+12);
  const matrixBytes=source.subarray(at+mat,at+mat+64*n),boneBytes=source.subarray(at+bones,at+bones+4*n);
  assert.equal(matrixBytes.length,64*n);assert.equal(boneBytes.length,4*n);
  const order=Array.from({length:n},(_,j)=>boneBytes.readUInt32LE(4*j));boneOrders.set(i,order);
  const rc=u(source,at+remap);
  const maps=Array.from({length:rc},(_,j)=>{
    const p=at+remap+4+j*8,offset=u(source,p),count=u(source,p+4);
    range(source,at+remap+offset,4*count);return Buffer.from(source.subarray(at+remap+offset,at+remap+offset+4*count));
  });
  if(selectedMaps.has(i)){ // one remap per group: the marker group indexes the bone list directly
    assert.equal(rc,2);const identity=Buffer.alloc(4*n);for(let j=0;j<n;j++)identity.writeUInt32LE(j,4*j);maps.push(identity);
    for(const a of Object.values(anchors))assert(order.includes(a.boneIndex));
  }
  const remapHeader=Buffer.alloc(4+8*maps.length);remapHeader.writeUInt32LE(maps.length);
  let offset=remapHeader.length;maps.forEach((m,j)=>{remapHeader.writeUInt32LE(offset,4+j*8);remapHeader.writeUInt32LE(m.length/4,8+j*8);offset+=m.length;});
  const header=Buffer.alloc(16);header.writeUInt32LE(n);header.writeUInt32LE(16,4);header.writeUInt32LE(16+matrixBytes.length,8);header.writeUInt32LE(16+matrixBytes.length+boneBytes.length,12);
  return Buffer.concat([header,matrixBytes,boneBytes,remapHeader,...maps]);
});
const edited=Buffer.from(source),gpuChunks=[sourceGpu];let gpuLength=sourceGpu.length;
const appendGpu=(b:Buffer)=>{const pad=Buffer.alloc((-gpuLength)&15);gpuChunks.push(pad);gpuLength+=pad.length;const offset=gpuLength;gpuChunks.push(b);gpuLength+=b.length;return offset;};
for(const li of selectedLayouts){
  const layout=info.layouts[li]!;assert.equal(layout.stride,OLD);
  const users=selected.filter(m=>m.layout===li),orders=users.map(m=>boneOrders.get(source.readInt32LE(meshAt[m.id]!+56))!);
  for(const order of orders)assert.deepEqual(order,orders[0]);
  const total=layout.vertices+vertices.length,v=Buffer.alloc(total*NEW);
  for(let i=0;i<layout.vertices;i++){
    const from=layout.vertexOffset+i*OLD;
    sourceGpu.copy(v,i*NEW,from,from+UV3);sourceGpu.copy(v,i*NEW+UV3+4,from+UV3,from+OLD); // UV3 stays zero
  }
  vertices.forEach((p,k)=>{
    const at=(layout.vertices+k)*NEW;v.writeUInt32LE(0xffffffff,at);
    p.p.forEach((c,j)=>v.writeFloatLE(c,at+4+4*j));v.writeUInt32LE(packNormal(p.n),at+16);
    eyeUv.forEach((uv,j)=>{v.writeUInt16LE(f16(uv[0]!),at+20+4*j);v.writeUInt16LE(f16(uv[1]!),at+22+4*j);});
    v.writeUInt16LE(0x3c00,at+36); // weight 1.0, remaining weights zero
    const slot=orders[0]!.indexOf(p.bone);assert(slot>=0&&slot<256);v[at+44]=slot;
  });
  const index=Buffer.alloc(indices.length*layout.width);indices.forEach((x,i)=>layout.width===2?index.writeUInt16LE(x,i*2):index.writeUInt32LE(x,i*4));
  const ix=Buffer.concat([sourceGpu.subarray(layout.indexOffset,layout.indexOffset+layout.indices*layout.width),index]);
  const vo=appendGpu(v),io=appendGpu(ix),at=layoutAt[li]!;
  // Items: [color,pos,normal,uv0,uv1,uv2,uv3,weights,bones]; the engine packs them in order.
  const items=[[5,4,0],[0,2,0],[1,30,0],[4,33,0],[4,33,1],[4,33,2],[4,33,3],[7,35,0],[6,28,0]];
  assert.equal(u(source,at+328),8);assert(source.subarray(at+8+20*8,at+8+20*9).every(x=>x===0),'No free layout item slot');
  items.forEach((item,k)=>{item.forEach((x,j)=>edited.writeUInt32LE(x,at+8+20*k+4*j));edited.writeUInt32LE(0,at+20+20*k);edited.writeUInt32LE(0,at+24+20*k);});
  edited.writeUInt32LE(items.length,at+328);edited.writeUInt32LE(total,at+352);edited.writeUInt32LE(NEW,at+356);edited.writeUInt32LE(layout.indices+indices.length,at+392);
  edited.writeUInt32LE(vo,at+416);edited.writeUInt32LE(v.length,at+420);edited.writeUInt32LE(io,at+424);edited.writeUInt32LE(ix.length,at+428);
}
const eyeKey=thin('m_boteye');
const meshRecords=meshAt.map((at,i)=>{
  const nm=u(source,at+104),mo=u(source,at+108),ng=u(source,at+120),go=u(source,at+124);
  const original=source.subarray(at,at+Math.max(128,mo+nm*4,go+ng*24));
  if(!selected.some(m=>m.id===i))return Buffer.from(original);
  assert.equal(nm,2);assert.equal(ng,2);
  const header=Buffer.from(source.subarray(at,at+128)),materials=Buffer.alloc((nm+1)*4);
  source.copy(materials,0,at+mo,at+mo+nm*4);materials.writeUInt32LE(eyeKey,nm*4);
  const group=Buffer.alloc(24),layout=info.layouts[info.meshes[i]!.layout]!;
  group.writeUInt32LE(nm,0);group.writeUInt32LE(layout.vertices,4);group.writeUInt32LE(vertices.length,8);
  group.writeUInt32LE(layout.indices,12);group.writeUInt32LE(indices.length,16);
  group.writeUInt32LE(u(source,at+go+20),20);
  header.writeUInt32LE(nm+1,104);header.writeUInt32LE(128,108);header.writeUInt32LE(ng+1,120);header.writeUInt32LE(128+materials.length,124);
  return Buffer.concat([header,materials,source.subarray(at+go,at+go+ng*24),group]);
});
const parts=[edited];let mainLength=edited.length;
const append=(b:Buffer)=>{const pad=Buffer.alloc((-mainLength)&15);parts.push(pad);mainLength+=pad.length;const at=mainLength;parts.push(b);mainLength+=b.length;return at;};
function pointerList(records:Buffer[]):Buffer{
  const header=Buffer.alloc(4+4*records.length);header.writeUInt32LE(records.length);
  const chunks=[header];let n=header.length;
  records.forEach((record,i)=>{const pad=Buffer.alloc((-n)&15);chunks.push(pad);n+=pad.length;header.writeUInt32LE(n,4+i*4);chunks.push(record);n+=record.length;});
  return Buffer.concat(chunks);
}
edited.writeUInt32LE(append(pointerList(mapRecords)),88);
edited.writeUInt32LE(append(pointerList(meshRecords)),100);
const ma=u(source,112),nm=u(source,ma),keys=Array.from({length:nm},(_,i)=>u(source,ma+4+i*4));
const vals=Array.from({length:nm},(_,i)=>source.readBigUInt64LE(ma+4+nm*4+i*8));
const na=u(native,112),nn=u(native,na),ni=Array.from({length:nn},(_,i)=>i).find(i=>u(native,na+4+4*i)===eyeKey);assert(ni!==undefined);
const eyeMaterial=native.readBigUInt64LE(na+4+nn*4+ni*8);
const existing=keys.indexOf(eyeKey);if(existing>=0)assert.equal(vals[existing],eyeMaterial);else{keys.push(eyeKey);vals.push(eyeMaterial);}
const materialMap=Buffer.alloc(4+keys.length*12);materialMap.writeUInt32LE(keys.length);
keys.forEach((k,i)=>materialMap.writeUInt32LE(k,4+i*4));vals.forEach((v,i)=>materialMap.writeBigUInt64LE(v,4+keys.length*4+i*8));
edited.writeUInt32LE(append(materialMap),112);
const main=Buffer.concat(parts),gpu=Buffer.concat(gpuChunks),checked=parseUnit(main,gpu,new Map());

// --- preservation checks ---
assert.equal(checked.meshes.length,info.meshes.length);
for(const li of selectedLayouts){
  const a=info.layouts[li]!,b=checked.layouts[li]!;assert.equal(b.stride,NEW);assert.equal(b.vertices,a.vertices+vertices.length);
  for(let i=0;i<a.vertices;i++){ // every original vertex byte, with only the UV3 slot inserted
    const x=sourceGpu.subarray(a.vertexOffset+i*OLD,a.vertexOffset+(i+1)*OLD),y=gpu.subarray(b.vertexOffset+i*NEW,b.vertexOffset+(i+1)*NEW);
    assert(x.subarray(0,UV3).equals(y.subarray(0,UV3))&&x.subarray(UV3).equals(y.subarray(UV3+4))&&y.readUInt32LE(UV3)===0,`Vertex ${i} of layout ${li} changed`);
  }
  assert(gpu.subarray(b.indexOffset,b.indexOffset+a.indices*a.width).equals(sourceGpu.subarray(a.indexOffset,a.indexOffset+a.indices*a.width)));
}
for(const old of info.meshes){
  const now=checked.meshes[old.id]!;assert.deepEqual(now.ranks,old.ranks);
  const isSelected=selected.some(m=>m.id===old.id);
  assert.equal(now.groups.length,old.groups.length+(isSelected?1:0));
  old.groups.forEach((g,i)=>{
    const a=groupInputs(sourceGpu,info.layouts[old.layout]!,g),b=groupInputs(gpu,checked.layouts[now.layout]!,now.groups[i]!);
    assert.deepEqual(b.indices,a.indices);assert.deepEqual(b.positions,a.positions);
    if(!isSelected)assert.deepEqual(b.attributes,a.attributes);
    else for(let v=0;v<g.vertices;v++)for(let k=0;k<6;k++)assert.equal(b.attributes[v*8+k],a.attributes[v*6+k]); // UV0-2
  });
  if(isSelected){const g=now.groups.at(-1)!;assert.equal(g.vertices,vertices.length);assert.equal(g.indices,indices.length);}
}
// Exact preservation of all original unit bytes except documented descriptor fields.
const restored=Buffer.from(main.subarray(0,source.length));
for(const field of [88,100,112])source.copy(restored,field,field,field+4);
for(const li of selectedLayouts){
  const at=layoutAt[li]!;source.copy(restored,at+8,at+8,at+8+20*16); // item table
  for(const field of [328,352,356,392,416,420,424,428])source.copy(restored,at+field,at+field,at+field+4);
}
assert(restored.equals(source));assert(gpu.subarray(0,sourceGpu.length).equals(sourceGpu));
// Standalone additive UNIT override; no physics, state machine or original file changes.
const type=0xe0a48d0be9a7453fn,id=0xef570293245a17c2n;
const nt=u(archive,4),nr=u(archive,8),sourceRow=Array.from({length:nr},(_,i)=>72+nt*32+i*80).find(at=>archive.readBigUInt64LE(at)===id&&archive.readBigUInt64LE(at+8)===type);assert(sourceRow!==undefined);
const head=Buffer.from(archive.subarray(0,72));head.writeUInt32LE(1,4);head.writeUInt32LE(1,8);
const types=Buffer.alloc(32);types.writeBigUInt64LE(type,8);types.writeBigUInt64LE(1n,16);types.writeUInt32LE(16,24);types.writeUInt32LE(64,28);
const row=Buffer.from(archive.subarray(sourceRow,sourceRow+80));row.writeBigUInt64LE(192n,16);row.writeBigUInt64LE(0n,24);row.writeBigUInt64LE(0n,32);row.writeBigUInt64LE(0n,40);row.writeBigUInt64LE(0n,48);row.writeUInt32LE(main.length,56);row.writeUInt32LE(0,60);row.writeUInt32LE(gpu.length,64);row.writeUInt32LE(0,76);
const patch=Buffer.concat([head,types,row,Buffer.alloc(8),main,Buffer.alloc((-main.length)&15)]);
await writeFile(`${out}/9ba626afa44a3aa3.patch_0`,patch);await writeFile(`${out}/9ba626afa44a3aa3.patch_0.gpu_resources`,gpu);await writeFile(`${out}/9ba626afa44a3aa3.patch_0.stream`,Buffer.alloc(0));
await writeFile(`${out}/unit.main`,main);await writeFile(`${out}/unit.gpu`,gpu);
await writeFile(`${out}/report.json`,JSON.stringify({sourceUnitSha256:hash(source),sourceGpuSha256:hash(sourceGpu),anchors,
  design:{eye:{ring:[.16,.12],centreDot:.05,facing:eyeFacing,vertices:eyeCount},vents:{rings:3,radius:[.26,.20],centreDot:.07,vertices:vertices.length-eyeCount}},
  addedVertices:vertices.length,addedTriangles:indices.length/3,visibleLods:selected.map(m=>m.ranks),layoutChange:'Visible body layouts gain zero UV3 (stride 44->48); all original vertex bytes preserved',
  originalGeometry:'Indices, positions, UV0-2, weights and bone indices exactly preserved',originalUnit:'Only geometry descriptors/pointers, layout item tables and appended data changed; joint tables and all other source bytes preserved',
  physicsOverrides:0,eyeMaterial:eyeMaterial.toString(16),installed:false,gameplayVerified:false,
  risks:['Alignment while walking/turning','Damage/destruction visibility','Emissive appearance and size at range','Engine acceptance of the UV3 layout change'],
  patchSha256:hash(patch),gpuSha256:hash(gpu)},null,2));
console.log(`Built local marker trial: ${vertices.length} vertices, ${indices.length/3} triangles per visible LOD; original geometry and rig preserved.`);
