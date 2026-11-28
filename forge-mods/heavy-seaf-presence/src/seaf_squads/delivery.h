#pragma once
#include "engine.h"
#include "loadouts.h"
#include <memory>
namespace seaf {
constexpr uint64_t shuttle_resource=0x3EC790806D0B0540ull,cargo_resource=0x75F7E96AF7DCD303ull;
struct BeaconTarget {uint32_t entity;std::array<float,3> position;};
inline BeaconTarget beacon_target(unsigned type,const float* dispatched){
 // The activation event carries state+0x30, the dispatch/raycast position.
 // The beacon element's +0x1C is the landing point used by the native drop
 // geometry. Never turn the elevated dispatch position into a squad target.
 auto m=ptr(game+0x346BF98)+0x40+0x1380;
 auto count=read<unsigned>(m+52),active=read<unsigned>(m+56);
 require(active<=count&&count<=4096,"invalid beacon counts");
 auto elements=ptr(m+120),states=ptr(m+104),handles=ptr(m+96);
 BeaconTarget result{};unsigned matches=0;
 for(unsigned n=0;n<active;n++){
  auto el=elements+n*64,st=states+n*2280;
  if(read<unsigned>(el+12)!=type||read<unsigned>(st+2272)==2)continue;
  bool same=true;for(unsigned axis=0;axis<3;axis++)same&=read<float>(st+48+axis*4)==dispatched[axis];
  if(!same)continue;
  auto d=read<Descriptor>(ptr(handles+n*8));require(d.id&&d.flags&1,"beacon is not locally owned");
  require(read<uint8_t>(el+60)!=0,"beacon has not landed");
  result.entity=d.id;
  for(unsigned axis=0;axis<3;axis++){auto p=read<float>(el+28+axis*4);require(std::isfinite(p)&&std::abs(p)<99000,"invalid beacon landing point");result.position[axis]=p;}
  ++matches;
 }
 require(matches==1,"activation does not identify one owned beacon");return result;
}
inline uintptr_t native_patrol_row(){
 // stratagem_row is documented for added types only. The original vanilla
 // pointer is read solely as a validated baseline; never write the old table.
 auto row=ptr(game+58504704+116*8);require(read<unsigned>(row)==116&&read<unsigned>(row+0x3C)==6&&read<unsigned>(row+0xA0)==2,"native patrol row changed");return row;
}
inline uint64_t population(){
 // Read the existing definition; only publish a missing local population entry.
 auto def=game+53300496;auto source=bytes(def,0x118);std::array<unsigned char,0xD8> record{};
 const unsigned roles[4][3]={{3257524892u,2,4},{222864895u,1,2},{4203048215u,2,2},{2356301476u,1,3}};
 auto put=[&](size_t at,const void* p,size_t n){require(at+n<=record.size(),"population field bounds");memcpy(record.data()+at,p,n);};
 for(unsigned n=0;n<4;n++){require(read<unsigned>(def+n*12)==roles[n][0]&&read<unsigned>(def+n*12+4)==roles[n][1]&&read<unsigned>(def+n*12+8)==0,"population composition differs");put(n*12,source.data()+n*12,12);float weight=(float)roles[n][2];put(0x84+n*4,&weight,4);}
 for(unsigned n=48;n<96;n++)require(source[n]==0,"additional population roles");require(read<unsigned>(def+0x60)==3126357841u,"population tag changed");
 unsigned four=4;float one=1;put(0x60,&four,4);put(0xA8,&def,8);put(0xC0,&one,4);
 const unsigned fields[][3]={{0xB0,0x100,8},{0xB8,0xFC,4},{0xBC,0xE8,4},{0xC4,0x108,4},{0xC8,0x10C,4},{0xCC,0x110,4}};
 for(auto& f:fields)put(f[0],source.data()+f[1],f[2]);
 auto row=native_patrol_row();auto payload=ptr(row+0x98);
 require(read<uint64_t>(payload)==cargo_resource&&read<uint64_t>(payload+8)==shuttle_resource,"SEAF payload changed");auto package=read<uint64_t>(row+0xA8);require(package,"SEAF package missing");
 auto m=ptr(game+53636368);auto count=read<unsigned>(m+333996),tags=read<unsigned>(m+334100);require(count<=32&&tags<=16,"population capacity invalid");bool found=false;
 for(unsigned n=0;n<tags;n++)if(read<unsigned>(m+334036+n*4)==3126357841u)found=true;
 std::vector<Change> changes;if(!found){require(tags<16,"population tags full");changes.push_back(change<unsigned>(m+334036+tags*4,3126357841u));changes.push_back(change<unsigned>(m+334100,tags+1));}
 if(!count){changes.push_back({m+327032,bytes(m+327032,record.size()),{record.begin(),record.end()}});changes.push_back(change<unsigned>(m+333996,1));}transaction(changes);return package;
}
struct Delivery {
 alignas(16) unsigned char context[0x8A0]{};alignas(8) unsigned char creation[0x58]{};
 float target[3]{};unsigned roles[6]{};uint32_t out{};
 Delivery(const float* position,const Loadout* roster){
  for(unsigned n=0;n<3;n++){require(std::isfinite(position[n])&&std::abs(position[n])<99000,"invalid beacon position");target[n]=position[n];}
  auto put=[&](unsigned off,auto value){memcpy(context+off,&value,sizeof value);};
  put(0x3D8,cargo_resource);context[0x3E0]=1;put(0x3E4,116u);put(0x5F8,1u);put(0x600,target+0);memcpy(context+0x610,target,12);put(0x628,1u);context[0x7E8]=1;put(0x898,2u);
  if(roster){for(unsigned n=0;n<6;n++)roles[n]=roster[n].role;put(0x638,roles+0);put(0x640,6u);}else context[0x648]=1;
  creation[0]=1;unsigned unit=0x7FFF;memcpy(creation+4,&unit,4);
  // Preserve the earlier working module's standoff entry. The landing anchor
  // stays at the beacon, separately from the initial airborne transform.
  float transform[16]={1,0,0,0,0,1,0,0,0,0,1,0,target[0],target[1]-250,target[2]+80,1};memcpy(creation+8,transform,64);auto ctx=context+0;memcpy(creation+0x48,&ctx,8);
 }
};
inline std::vector<std::unique_ptr<Delivery>> deliveries;
inline uint32_t deliver(const float* position,const Loadout* roster,uint64_t shuttle){
 require(api->game_state(mod)==HF_STATE_MISSION,"delivery outside mission");require(deliveries.size()<128,"delivery session capacity exhausted");
 auto root=ptr(game+0x346BF98);require(read<uint16_t>(root+0xF3F828)==1,"constructor busy");require(has_free_entity_slot(root,em_layout()),"no free shuttle slot");
 auto defaults=bytes(root+15986472,0x8A0);require(std::all_of(defaults.begin(),defaults.end(),[](unsigned char b){return b==0;}),"default spawn context changed");
 auto d=std::make_unique<Delivery>(position,roster);auto p=d.get();deliveries.push_back(std::move(d));
 native<uint32_t*(*)(uint32_t*,uint64_t,void*)>(0xFD9710)(&p->out,shuttle,p->creation);auto desc=network_descriptor(p->out);
 require(desc.type==shuttle&&desc.unit!=0x7FFF&&desc.network!=0x7FFF&&(desc.flags&1),"shuttle ownership or identity not verified");
 auto anchors=ptr(game+53634648);auto ai=index(anchors+48,p->out);require(ai>=0&&ai<65536,"dropship destination missing");auto anchor=ptr(anchors+96)+(size_t)ai*32;
 for(unsigned n=0;n<3;n++)require(std::abs(read<float>(anchor+n*4)-position[n])<.01f,"dropship destination differs from beacon");
 if(roster){auto i=instance(0x3326CC0,0x20,0x38,0x50,0x54,p->out);owned(i);require(read<unsigned>(i.row)==1&&read<unsigned>(i.row+4)==6&&read<uint8_t>(i.row+0x50)==0,"explicit roster not accepted");auto table=ptr(game+0x21CB890);auto count=read<unsigned>(game+0x21CB898);require(count&&count<1024,"variant table invalid");
  for(unsigned n=0;n<6;n++){auto v=read<unsigned>(i.row+8+n*4);require(v<count&&read<unsigned>(table+v*0x80)==roster[n].role,"passenger role differs");}}
 return p->out;
}
}
