import { readFile,writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
const root='dist/aim-voice-2026-09-24';
const blob=await readFile(`${root}/native-model/content/fac_cyborgs/vehicles/cyborg_assault_walker/cyborg_assault_walker.unit.glb`);
type Accessor={bufferView:number;byteOffset?:number;componentType:number;count:number;type:string};
type Primitive={attributes:Record<string,number>;indices:number;material:number};
type Node={name:string;mesh?:number;skin?:number;translation?:number[]};
type Gltf={bufferViews:{byteOffset?:number;byteStride?:number;byteLength:number}[];accessors:Accessor[];meshes:{primitives:Primitive[]}[];nodes:Node[];skins:{joints:number[]}[];materials:{name:string}[]};
const jsonSize=blob.readUInt32LE(12);
const gltf=JSON.parse(blob.toString('utf8',20,20+jsonSize)) as Gltf;
const bin=blob.subarray(28+jsonSize);
const cache=new Map<number,number[][]>();
function read(index:number):number[][] {
  const cached=cache.get(index);if(cached)return cached;
  const a=gltf.accessors[index];assert(a);
  const view=gltf.bufferViews[a.bufferView];assert(view);
  const n=({SCALAR:1,VEC2:2,VEC3:3,VEC4:4} as Record<string,number>)[a.type];assert(n);
  const size=({5121:1,5123:2,5125:4,5126:4} as Record<number,number>)[a.componentType];assert(size);
  const at=(view.byteOffset??0)+(a.byteOffset??0),stride=view.byteStride??size*n;
  assert(at+(a.count-1)*stride+size*n<=bin.length);
  const data=Array.from({length:a.count},(_,i)=>Array.from({length:n},(_,j)=>{
    const p=at+i*stride+j*size;
    return a.componentType===5126?bin.readFloatLE(p):a.componentType===5125?bin.readUInt32LE(p):a.componentType===5123?bin.readUInt16LE(p):bin[p]!;
  }));
  cache.set(index,data);return data;
}
const result=[];
const ventCandidates=[];
for(const node of gltf.nodes) {
  if(node.mesh===undefined||node.skin===undefined||!/(body|platform)_whole/.test(node.name))continue;
  const mesh=gltf.meshes[node.mesh],skin=gltf.skins[node.skin];assert(mesh&&skin);
  for(const primitive of mesh.primitives) {
    const p=read(primitive.attributes.POSITION!),w=read(primitive.attributes.WEIGHTS_0!),b=read(primitive.attributes.JOINTS_0!);
    for(const i of new Set(read(primitive.indices).flat())) {
      const position=[p[i]![0]!,-p[i]![2]!,p[i]![1]!];
      const distance=Math.hypot(position[0]!,position[1]!+1.6501188278198242,position[2]!-3.0071661472320557);
      if(distance<0.75)ventCandidates.push({distance,position,weights:w[i],bones:b[i]!.map(j=>gltf.nodes[skin.joints[j]!]!.name)});
    }
    if(!gltf.materials[primitive.material]?.name.startsWith('m_boteye '))continue;
    const positions=read(primitive.attributes.POSITION!),weights=read(primitive.attributes.WEIGHTS_0!),joints=read(primitive.attributes.JOINTS_0!);
    const indices=read(primitive.indices).flat(),used=[...new Set(indices)];
    const vertices=used.map(i=>({index:i,position:[positions[i]![0],-positions[i]![2]!,positions[i]![1]],
      weights:weights[i],bones:joints[i]!.map(j=>gltf.nodes[skin.joints[j]!]!.name)}));
    const bounds=[0,1,2].map(a=>[Math.min(...vertices.map(v=>v.position[a]!)),Math.max(...vertices.map(v=>v.position[a]!))]);
    result.push({node:node.name,triangles:indices.length/3,bounds,vertices});
  }
}
await writeFile(`${root}/native-eye.json`,JSON.stringify(result,null,2));
await writeFile(`${root}/native-vent.json`,JSON.stringify(ventCandidates.sort((a,b)=>a.distance-b.distance).slice(0,30),null,2));
console.log(JSON.stringify(result.map(x=>({node:x.node,triangles:x.triangles,bounds:x.bounds,bones:[...new Set(x.vertices.flatMap(v=>v.bones.filter((_,i)=>v.weights![i]!>0)))]})),null,2));
console.log('Nearest vent vertices:',JSON.stringify(ventCandidates.slice(0,3)));
