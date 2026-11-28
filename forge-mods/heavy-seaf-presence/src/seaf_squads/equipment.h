#pragma once
#include "engine.h"
#include <memory>
namespace seaf {
constexpr uint64_t eruptor=0xB6AFF2195568767Full, hmg=0x2152D5147B0AC418ull;
inline void owned(const Instance& i){require(i.owned,"instance not authoritative");}
inline bool empty(uint32_t id){return !id||id==0xffffffffu||id==read<uint32_t>(game+0x3483C4C);}
struct Ammo {unsigned rounds,spares,working,chamber,pattern;bool empty,magazine;uintptr_t entry,live;unsigned pool{};};
inline Ammo ammo(uint32_t weapon){
 auto m=ptr(game+0x3326648);int n=index(m+0x20,weapon);
 if(n>=0){auto i=instance(0x3326648,0x20,0x38,0x50,12,weapon);owned(i);auto live=ptr(m+0x48)+i.index*16;
  return {read<unsigned>(i.row+4),read<unsigned>(i.row),read<unsigned>(live),read<unsigned>(live+8),read<unsigned>(live+4),read<uint8_t>(i.row+8)==1,true,i.row,live};}
 auto i=instance(0x3326CF0,0x28,0x40,0x58,20,weapon);owned(i);auto live=ptr(i.manager+0x50)+i.index*24;auto pool=read<unsigned>(i.row+4);require(pool<=1,"unknown ammunition pool");
 return {read<unsigned>(i.row+8+pool*4),read<unsigned>(i.row),read<unsigned>(live+4+pool*4),read<unsigned>(live+16),0,read<uint8_t>(i.row+16)==1,false,i.row,live,pool};
}
inline void chamber(uint32_t weapon){auto i=instance(0x3326660,0x28,0x40,0x50,40,weapon);owned(i);native<void(*)(void*,uint32_t)>(0x745300)((void*)i.manager,weapon);}
inline void complete_reload(uint32_t soldier,uint32_t weapon){
 auto inv=inventory(soldier);require(read<uint32_t>(inv.row)==weapon&&held(soldier)==weapon&&!ability(soldier).active,"reload owner or action changed");
 auto a=ammo(weapon);require(a.empty&&a.chamber==0&&a.rounds>0&&a.rounds<=2047&&a.working==a.rounds&&a.pattern==0,"chamber not ready");
 chamber(weapon);auto b=ammo(weapon);require(!b.empty&&b.chamber&&b.rounds==a.rounds&&b.spares==a.spares,"chamber completion failed");
}
inline void model(uint32_t soldier,uint32_t weapon,bool holster){
 auto i=instance(0x3326DC0,0x20,0x38,0x40,0x30,weapon);owned(i);require(read<uint32_t>(i.row+4)==soldier,"attachment owner changed");
 native<void(*)(void*,uint32_t,uint32_t,float)>(0x8C0E90)((void*)i.manager,weapon,holster?1:0,0.f);
}
inline void select(uint32_t soldier,bool support=false){
 auto i=inventory(soldier);auto target=read<uint32_t>(i.row+(support?8:0)),other=read<uint32_t>(i.row+(support?0:8));require(!empty(target),"selected slot empty");
 if(!empty(other)&&other!=target)model(soldier,other,true);
 native<void(*)(void*,uint32_t,uint32_t)>(0x9AAA50)((void*)i.manager,soldier,support?3:1);
 require(held(soldier)==target,"inventory selection failed");model(soldier,target,false);
}
inline void prepare_eruptor(uint32_t weapon){
 require(descriptor(weapon).type==eruptor,"wrong Eruptor instance");auto m=ptr(game+0x3326CE0);auto n=index(m+0x30,weapon);require(n>=0&&n<65536,"WeaponData missing");
 auto d=ptr(ptr(m+0x48)+n*8);auto desc=read<Descriptor>(d);require(desc.id==weapon&&desc.type==eruptor&&(desc.flags&1),"Eruptor owner changed");
 auto shared=ptr(ptr(game+0x346BF98)+0xF12BD8)+0x2DA0+291*0x4D0;auto source=bytes(shared,0x4D0);
 require(read<unsigned>(shared+0x3AC)==2660&&read<unsigned>(shared+0x4B0)==2660,"Eruptor baseline changed");
 int ci=index(m+0x70,weapon);
 if(ci<0){auto count=read<unsigned>(m+0xA8),capacity=read<unsigned>(m+0x14);require(count<capacity&&capacity<=65536&&count+1<read<unsigned>(m+0x78)&&count+1<read<unsigned>(m+0x98),"private WeaponData capacity exhausted");
  unsigned delta[3]{};native<void(*)(void*,void*,void*)>(0x75F080)((void*)m,(void*)d,delta);ci=index(m+0x70,weapon);
  require(ci==(int)count&&read<unsigned>(m+0xA8)==count+1&&bytes(ptr(m+0xB0)+ci*0x4D0,0x4D0)==source,"private WeaponData copy failed");}
 require(ci>=0&&(unsigned)ci<read<unsigned>(m+0xA8)&&(uint32_t)index(m+0x90,ci)==weapon,"private WeaponData owner changed");
 auto at=ptr(m+0xB0)+ci*0x4D0;require(at!=shared&&read<unsigned>(at+0x3AC)==2660&&read<unsigned>(at+0x4B0)==2660,"private Eruptor already modified");
 transaction({change<unsigned>(at+0x3AC,2708),change<unsigned>(at+0x4B0,0)});require(bytes(shared,0x4D0)==source,"shared WeaponData changed");
}
struct WeaponConstruction {alignas(16) unsigned char context[0x8A0]{};alignas(8) unsigned char creation[0x58]{};uint32_t out{};};
inline std::vector<std::unique_ptr<WeaponConstruction>> weapon_buffers;
inline uint32_t replace(uint32_t soldier,uint64_t resource,unsigned slot=1){
 require(resource&&(slot==1||slot==3||slot==6),"invalid replacement");auto inv=inventory(soldier);auto before=bytes(inv.row,16);require(!empty(read<uint32_t>(inv.row)),"inventory not initialized");
 auto root=ptr(game+0x346BF98);require(read<uint16_t>(root+0xF3F828)==1,"constructor busy");
 require(has_free_entity_slot(root,em_layout()),"no free entity slot");require(weapon_buffers.size()<768,"weapon session capacity exhausted");
 auto c=std::make_unique<WeaponConstruction>();auto p=c.get();weapon_buffers.push_back(std::move(c));
 auto desc=native<void*(*)(uint32_t)>(0xFD9D40)(soldier);require(desc&&read<uint32_t>((uintptr_t)desc+8)==soldier,"soldier descriptor changed");
 native<void*(*)(void*,uint64_t,uint8_t,uint8_t)>(0x9A6630)(p->creation,resource,1,0);auto context=p->context;memcpy(p->creation+0x48,&context,8);
 native<uint32_t*(*)(uint32_t*,void*,uint32_t,uint64_t,void*)>(0x9A67A0)(&p->out,desc,slot,resource,p->creation);
 require(p->out&&!empty(p->out)&&descriptor(p->out).type==resource,"weapon construction failed");if(resource==eruptor)prepare_eruptor(p->out);
 native<void(*)(void*,uint32_t,uint32_t)>(0x9AAA50)((void*)inv.manager,soldier,0);require(empty(held(soldier)),"old weapon did not unwield");
 native<void(*)(void*,uint32_t,uint32_t,uint32_t,uint8_t)>(0x9AE030)((void*)inv.manager,soldier,p->out,slot,4);
 inv=inventory(soldier);unsigned offset=slot==1?0:slot==3?8:12;require(read<unsigned>(inv.row+offset)==p->out,"replacement not attached");
 for(unsigned off=0;off<16;off+=4)if(off!=offset){unsigned previous;memcpy(&previous,before.data()+off,4);require(read<unsigned>(inv.row+off)==previous,"another inventory slot changed");}
 select(soldier);return p->out;
}
inline void remove_support(uint32_t soldier){
 auto i=inventory(soldier);auto old=read<unsigned>(i.row+8);if(empty(old))return;require(descriptor(old).type==0x5C54E81A4AAA31FCull,"unexpected support item");select(soldier);
 native<void(*)(void*,uint32_t,uint32_t,uint8_t,void*,uint8_t)>(0x9A8840)((void*)i.manager,soldier,old,4,nullptr,1);
 i=inventory(soldier);auto now=read<unsigned>(i.row+8);if(!empty(now)){require(now==old,"support changed during removal");transaction({change<unsigned>(i.row+8,read<unsigned>(game+0x3483C4C))});}
}
inline void grenades(uint32_t soldier,uint64_t resource,unsigned count){
 require(count<=2,"invalid grenade count");auto i=inventory(soldier);auto counts=ptr(i.manager+88)+i.index*8;
 transaction({change<uint64_t>(i.row+40,resource),change<unsigned>(counts,count)});
}
}
