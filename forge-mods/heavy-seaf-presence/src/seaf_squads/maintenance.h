#pragma once
#include "equipment.h"
namespace seaf {
constexpr uint64_t recoilless=0x9F80D67A12A7E40Full,rrpack=0x96DFC6542AA22980ull;
struct Soldier {
 Descriptor identity{};uint32_t primary{},primary_network{},support{},support_network{},backpack{},backpack_network{};uint64_t weapon{};
 Ammo previous{};bool observed{},reloading{},refilled{},pending_reload{},pending_end{},launching=true,pending_rr{},rr_done{};
 bool released_seen{},landed{};float released_for{},quiet{},bolt_delay=-1,rr_cooldown{};unsigned visual{},squad{};
};
inline void valid(const Soldier& t){auto d=network_descriptor(t.identity.id),w=network_descriptor(t.primary);require(d.type==t.identity.type&&d.network==t.identity.network&&w.type==t.weapon&&w.network==t.primary_network,"tracked entity changed");auto i=inventory(d.id);require(read<unsigned>(i.row)==t.primary,"tracked primary changed");auto h=instance(53634696,4144,4168,4184,440,d.id);require(read<int32_t>(h.row+20)>0,"soldier no longer alive");}
inline unsigned buffer_rr(Soldier& t,bool initial){
 auto inv=inventory(t.identity.id);require(t.weapon==recoilless&&read<unsigned>(inv.row)==t.primary&&read<unsigned>(inv.row+12)==t.backpack,"Recoilless inventory changed");auto pack=descriptor(t.backpack);require(pack.type==rrpack&&pack.network==t.backpack_network,"Recoilless pack changed");
 auto a=ammo(t.primary);require(a.magazine&&a.rounds<=2&&a.working==a.rounds,"Recoilless magazine changed");auto bag=instance(0x33265E8,0x20,0x38,0x50,8,t.backpack);owned(bag);auto reserve=read<unsigned>(bag.row);require(reserve<=16,"Recoilless reserve outside bounds");
 unsigned n=a.rounds;if((n==1||(initial&&n==0))&&reserve){unsigned cost=std::min(2-n,reserve);native<void(*)(void*,uint32_t,uint32_t,uint8_t)>(0x87F970)((void*)bag.manager,t.backpack,cost,1);require(read<unsigned>(bag.row)==reserve-cost,"Recoilless reserve debit failed");n+=cost;}
 if(n!=a.rounds)transaction({change<unsigned>(a.entry+4,n),change<unsigned>(a.live,n)});
 if(n&&(a.empty||!a.chamber))chamber(t.primary);return ammo(t.primary).rounds;
}
inline void maintain(Soldier& t,float dt){
 valid(t);auto a=ammo(t.primary);auto action=ability(t.identity.id);auto wielded=held(t.identity.id);bool reload=action.active&&action.id==2326;
 // A reload must demonstrably increase ammunition before chamber completion.
 if(reload&&!t.reloading){t.refilled=false;t.pending_reload=false;}
 if(t.observed&&(a.magazine!=t.previous.magazine||a.pool!=t.previous.pool)){t.observed=false;t.refilled=false;t.pending_reload=false;}
 if(t.observed&&(reload||t.reloading)&&a.rounds>t.previous.rounds)t.refilled=true;
 if(t.reloading&&!reload){t.pending_reload=t.refilled&&a.empty&&!a.chamber&&a.rounds;t.refilled=false;}
 if(t.weapon==0xFE3B29B2CFA63F9Bull)t.pending_reload=false; // De-Escalator has a chamberless feed.
 if(t.pending_reload){if(!a.empty||a.chamber||!a.rounds)t.pending_reload=false;else if(!action.active&&wielded==t.primary){complete_reload(t.identity.id,t.primary);t.pending_reload=false;}}
 if(t.weapon==eruptor){
  if(t.bolt_delay>=0)t.bolt_delay=std::max(0.f,t.bolt_delay-dt);
  if(t.observed&&a.empty&&!a.chamber&&(a.rounds<t.previous.rounds||!t.previous.empty))t.bolt_delay=60.f/32.f;
  if(!a.empty||a.chamber||!a.rounds||(t.observed&&a.rounds>t.previous.rounds))t.bolt_delay=-1;
  if(t.bolt_delay==0&&!action.active&&wielded==t.primary){complete_reload(t.identity.id,t.primary);t.bolt_delay=-1;}
 }
 if(t.weapon==hmg){
  if(wielded!=t.primary){t.pending_end=false;t.quiet=0;}
  else {if(t.observed&&a.rounds<t.previous.rounds){t.pending_end=true;t.quiet=0;}else t.quiet+=std::min(dt,.1f);
   if(action.active)t.quiet=0;
   if(t.pending_end&&t.quiet>=.35f&&!action.active){auto m=ptr(game+0x3326730);int i=index(m+0x18,t.primary);require(i>=0&&i<65536&&read<unsigned>(ptr(m+0x38)+i*4)==t.identity.id,"HMG wielder changed");native<void(*)(void*,uint32_t,uint32_t)>(0x79B880)(nullptr,t.primary,0x49CC8DB1);t.pending_end=false;t.quiet=0;}}
 }
 if(t.weapon==recoilless&&!t.rr_done){
  auto inv=inventory(t.identity.id);auto support=descriptor(t.support);require(read<unsigned>(inv.row+8)==t.support&&support.network==t.support_network&&support.type==0x5FECAB819F96A3E8ull,"Recoilless secondary changed");
  t.rr_cooldown=std::max(0.f,t.rr_cooldown-dt);if(t.observed&&a.rounds<t.previous.rounds){t.rr_cooldown=6;t.pending_rr=true;}
  if(!action.active){
   if(wielded!=t.visual&&(wielded==t.primary||wielded==t.support)){select(t.identity.id,wielded==t.support);t.visual=wielded;}
   bool want=t.rr_cooldown==0&&a.rounds>0;
   if(!a.rounds){want=false;t.rr_done=true;}
   else if(t.pending_rr&&t.rr_cooldown==0){a.rounds=buffer_rr(t,false);t.pending_rr=false;}
   if(want!=t.launching){select(t.identity.id,!want);t.launching=want;t.visual=want?t.primary:t.support;}
  }
 }
 t.previous=a;t.observed=true;t.reloading=reload;
}
}
