/* Embedded squad delivery/loadout implementation for Heavy SEAF Presence.
 * Source pack: SEAF Squad Stratagems 0.1.7, provided by the user. When that mod
 * is installed its four squad stratagems are dispatched through this copy, and
 * the squad pool adds its compositions to the game's SEAF roster; without it,
 * neither happens. */
#include "hellforge_mod.h"
#include "hellforge_content.h"
#include "../tm_tick.h"
#include "../options_menu.h"
#include "../heavy_gate_and_pacing.h"
#include "equipment.h"
#include "delivery.h"
#include "maintenance.h"
#include "hunters.h"
#include "assets.h"
#include <deque>
#include <set>
namespace seaf {
struct Squad {const char *key,*title,*description,*code,*icon;const Loadout* roster;uint32_t type{};uint64_t shuttle{};};
static Squad squads[]={
 {"patrol","SEAF Patrol Squad","An authorized requisition of a standard six-person SEAF patrol squad, dropped in via Pelican transport shuttle to assist in combat operations.","UDRLDRU","local/seaf_squads/icons/patrol",nullptr},
 {"stealth","SEAF Stealth Squad","A six-person SEAF stealth squad with three AR-59 Suppressors, two R-72 Censors with smoke grenades, and a P-35 Re-Educator combat medic. Delivered by Pelican transport.","UDRLLDU","local/seaf_squads/icons/stealth",stealth_loadouts},
 {"riot","SEAF Riot Control Squad","A six-person SEAF riot control squad with Pacifiers, a Halt shotgun, a De-Escalator, and two Defender-equipped combat medics. Carries stun, Urchin, and smoke grenades.","UDRLRDU","local/seaf_squads/icons/riot",riot_loadouts},
 {"tank","SEAF Tank Hunter Squad","A six-person SEAF anti-tank squad with an Eruptor, a Recoilless Rifle, Adjudicators, EAT launchers, an MG-43, a handheld HMG, and a Counter Sniper combat medic.","UDRLDLU","local/seaf_squads/icons/tank",tank_loadouts}
};
static constexpr size_t max_live_custom_troops=18;
struct Job {unsigned squad;Descriptor shuttle;float elapsed{};bool grouped{},equipped{};unsigned next_slot{};std::vector<Soldier> troops;};
static std::deque<Job> jobs;static std::vector<Soldier> soldiers;
static std::vector<Descriptor> live_troops;
struct PendingCall {unsigned squad;std::array<float,3> position;};
static std::deque<PendingCall> pending_calls;
struct Arrival {unsigned squad;Descriptor identity;float elapsed{};};
static std::vector<Arrival> arrivals;
static bool prepared{},failed{},assets_ready{};static float asset_wait{},asset_elapsed{};static uint64_t patrol_package{};static unsigned state{};
static unsigned role(uint64_t type){
 switch(type){case 0x75F7E96AF7DCD303ull:case 0x6B22AE8E5F669D4Full:case 0xA00331F24DEEF2EEull:case 0x548136F66F5C9B5Cull:return 0xC229E29C;
 case 0xBFB5C88D557595D8ull:case 0xA6DCC225E1263606ull:return 0xFA856D17;
 case 0x6FB1E73E00A84329ull:case 0x14453B8FCB040099ull:case 0xC47287D738BE91C3ull:return 0x8C724EA4;default:return 0;}
}
static bool released(const Soldier& t){auto d=descriptor(t.identity.id);require(d.type==t.identity.type&&d.network==t.identity.network,"passenger identity changed");auto i=instance(0x3326A88,0x18,0x30,0x38,8,d.id);return read<unsigned>(i.row)==0x7FFF;}
static void track_live(const Descriptor& identity){
 for(const auto& current:live_troops)if(current.id==identity.id&&current.network==identity.network)return;
 live_troops.push_back(identity);
}
static void prune_live(){
 for(size_t n=0;n<live_troops.size();){
  bool alive=false;
  try{auto d=descriptor(live_troops[n].id);require(d.type==live_troops[n].type&&d.network==live_troops[n].network,"SEAF identity changed");auto h=instance(53634696,4144,4168,4184,440,d.id);alive=read<int32_t>(h.row+20)>0;}catch(...){alive=false;}
  if(alive)++n;else live_troops.erase(live_troops.begin()+n);
 }
}
static bool capacity_for_squad(){
 size_t pending=pending_calls.size()*6;
 for(const auto& job:jobs){if(!job.equipped)pending+=6;else for(const auto& troop:job.troops)if(!troop.landed)pending++;}
 return live_troops.size()+pending+6<=max_live_custom_troops;
}
static bool equip(Job& job){
 auto roster=squads[job.squad].roster;
 if(!job.grouped){
  auto d=descriptor(job.shuttle.id);require(d.type==job.shuttle.type&&d.network==job.shuttle.network,"shuttle identity changed");
  auto i=instance(0x3326CC0,0x20,0x38,0x48,0x3D8,d.id);std::vector<Soldier> group;std::set<unsigned> ids;
  for(unsigned n=0;n<16;n++){auto id=read<unsigned>(i.row+0x280+n*4);if(id&&id!=0xffffffffu&&ids.insert(id).second){Soldier t;t.identity=descriptor(id);group.push_back(t);}}
  if(group.size()<6)return false;require(group.size()==6,"unexpected passenger count");
  if(!roster){for(auto& t:group){if(!(t.identity.flags&1))return false;require(!released(t),"patrol passenger unloaded before arrival");}job.grouped=true;job.equipped=true;job.troops=std::move(group);api->log(mod,"SQUAD_TRACKING: %s; six passengers",squads[job.squad].title);return true;}
  for(unsigned n=0;n<6;n++){
   auto& t=group[n];require(role(t.identity.type)==roster[n].role,"passenger is not requested soldier role");
   if(!(t.identity.flags&1))return false;
   auto m=ptr(game+53634872);if(index(m+40,t.identity.id)<0)return false;
   auto inv=inventory(t.identity.id);auto primary=read<unsigned>(inv.row);if(empty(primary))return false;descriptor(primary);
   require(!released(t),"passenger unloaded before equipment was ready");
  }
  job.troops=std::move(group);
  job.grouped=true;
  job.next_slot=0;
 }
 if(job.next_slot<6u){
  if(!HeavyEquipConsumeOne())return false; /* Cap strictly to <= 1 unit constructed/equipped per tick! */
  unsigned n=job.next_slot;
  auto& t=job.troops[n];const auto& spec=roster[n];auto id=t.identity.id;
  unsigned bc=HeavyBreadcrumbEnter(HEAVY_BC_JOB_EQUIP,job.shuttle.id,id,n);
  try{
   if(spec.role==0xFA856D17){auto inv=inventory(id);require(descriptor(read<unsigned>(inv.row)).type==spec.primary&&descriptor(read<unsigned>(inv.row+8)).type==spec.support,"native MG-43/EAT loadout changed");}
   else {
    replace(id,spec.primary);
    if(spec.backpack)replace(id,spec.backpack,6);
    auto inv=inventory(id);auto support=read<unsigned>(inv.row+8);
    if(spec.support){if(empty(support)||descriptor(support).type!=spec.support)replace(id,spec.support,3);}
    else remove_support(id);
   }
   if(spec.grenade)grenades(id,spec.grenade,spec.count);
   else if(job.squad==1){auto inv=inventory(id);grenades(id,read<uint64_t>(inv.row+40),0);}
   auto inv=inventory(id);t.primary=read<unsigned>(inv.row);t.primary_network=descriptor(t.primary).network;t.weapon=spec.primary;t.support=read<unsigned>(inv.row+8);t.backpack=read<unsigned>(inv.row+12);
   if(!empty(t.support))t.support_network=descriptor(t.support).network;
   if(!empty(t.backpack))t.backpack_network=descriptor(t.backpack).network;
   if(t.weapon==recoilless)buffer_rr(t,true);
   HeavyBreadcrumbLeave(bc,1);
   job.next_slot++;
  }catch(...){HeavyBreadcrumbLeave(bc,-1);throw;}
 }
 if(job.next_slot>=6u&&!job.equipped){
  job.equipped=true;
  api->log(mod,"SQUAD_EQUIPPED: %s; six passengers (1/tick paced)",squads[job.squad].title);
  return true;
 }
 return job.equipped;
}
static void DispatchCalls(){
 // Activation notifications can run inside the native beacon dispatcher. Only
 // construct entities after that stack has returned, from the update callback.
 while(!pending_calls.empty()){
  auto call=pending_calls.front();pending_calls.pop_front();auto& s=squads[call.squad];
  try{mission();prove();population();auto id=deliver(call.position.data(),s.roster,s.shuttle);auto d=descriptor(id);
   require(jobs.size()<128,"too many pending deliveries");jobs.push_back({call.squad,d});
   arrivals.push_back({call.squad,d});api->log(mod,"SQUAD_CALLED: %s; shuttle %u; network %u; target %.1f %.1f %.1f",s.title,id,d.network,call.position[0],call.position[1],call.position[2]);
  }catch(const std::exception& e){api->log(mod,"SQUAD_CALL_REFUSED: %s: %s",s.title,e.what());}
 }
}
static void Prepare(HfMod*,HfEvent,void*){
 try {size_t size=0;game=api->module_base("game.dll",&size);require(game&&size>58504704+117*8,"game module unavailable");prove();auto row=native_patrol_row();patrol_package=read<uint64_t>(row+0xA8);require(patrol_package,"SEAF package unavailable");prepared=true;assets_ready=false;api->log(mod,"SQUADS_PREPARED: loading squad packages");}
 catch(const std::exception& e){failed=true;api->log(mod,"SQUADS_REFUSED: %s",e.what());}
}
static void Tick(HfMod*,float dt,void*){
 static float live_check;
 static bool was_host;
 HeavyEquipBeginFrame();
 if(!prepared||failed||!std::isfinite(dt)||dt<0)return;auto now=api->game_state(mod);
 if(now!=state){if(now!=HF_STATE_MISSION){jobs.clear();soldiers.clear();live_troops.clear();pending_calls.clear();arrivals.clear();hunters::reset();was_host=false;}state=now;}
 if(now!=HF_STATE_SHIP&&now!=HF_STATE_MISSION)return;
 if(!assets_ready){asset_elapsed+=dt;asset_wait-=dt;if(asset_wait>0)return;asset_wait=.5f;
  try {bool resident=true;auto load=[&](uint64_t p){auto status=load_squad_package(p);if(status==0||(status!=HF_PACKAGE_RESIDENT&&asset_elapsed>90)){char message[128];snprintf(message,sizeof message,"package %016llx %s",(unsigned long long)p,status==0?"request refused":"loading timed out");throw std::runtime_error(message);}resident&=status==HF_PACKAGE_RESIDENT;};load(patrol_package);for(auto p:asset_packages)load(p);if(resident)population();assets_ready=resident;if(resident)api->log(mod,"SQUADS_READY: all four squads available");}
  catch(const std::exception& e){failed=true;api->log(mod,"SQUAD_ASSETS_REFUSED: %s",e.what());}return;
 }
 if(now!=HF_STATE_MISSION)return;
 if(!host()){
  if(was_host){jobs.clear();soldiers.clear();live_troops.clear();pending_calls.clear();arrivals.clear();hunters::reset();was_host=false;}
  return;
 }
 was_host=true;
 for(size_t n=0;n<arrivals.size();){bool done=false;auto& a=arrivals[n];try{
  auto d=descriptor(a.identity.id);require(d.type==a.identity.type&&d.network==a.identity.network&&d.unit!=0x7FFF,"dropship disappeared after construction");
  a.elapsed+=dt;if(a.elapsed>=1){api->log(mod,"SQUAD_SHUTTLE_ACTIVE: %s; shuttle %u; network %u",squads[a.squad].title,d.id,d.network);done=true;}
 }catch(const std::exception& e){api->log(mod,"SQUAD_SHUTTLE_STOPPED: %s: %s",squads[a.squad].title,e.what());done=true;}
 if(done)arrivals.erase(arrivals.begin()+n);else ++n;
 }
 for(size_t n=0;n<jobs.size();){bool done=false;auto& job=jobs[n];try{
  job.elapsed+=dt;require(job.elapsed<90,"passenger preparation timed out");if(!job.equipped)equip(job);
  if(job.equipped){unsigned finished=0;for(auto& t:job.troops){if(!t.landed&&released(t)){t.released_for+=dt;if(t.released_seen&&t.released_for>=.1f){
    t.landed=true;track_live(t.identity);t.squad=job.squad;
    if(squads[job.squad].roster){
     // Preserve the native machine-gunner/EAT handling unchanged.
     if(t.weapon!=0x587878FB76F4B9B1ull)select(t.identity.id);
     t.previous=ammo(t.primary);t.observed=true;soldiers.push_back(t);
    }
   }t.released_seen=true;}if(t.landed)finished++;}done=finished==6;}
 }catch(const std::exception& e){api->log(mod,"SQUAD_SETUP_STOPPED: %s: %s",squads[job.squad].title,e.what());done=true;}
 if(done)jobs.erase(jobs.begin()+n);else ++n;
 }
 hunters::frame(dt);
 for(size_t n=0;n<soldiers.size();){try{maintain(soldiers[n],dt);if(soldiers[n].squad==3&&OptionsTankHunters())hunters::maintain(soldiers[n],dt);++n;}catch(const std::exception& e){api->log(mod,"SOLDIER_MAINTENANCE_STOPPED: %u: %s",soldiers[n].identity.id,e.what());hunters::forget(soldiers[n].identity.id);soldiers.erase(soldiers.begin()+n);}}
 live_check-=dt;if(live_check<=0){prune_live();live_check=1.0f;}
 DispatchCalls();
}
}
extern "C" int heavy_seaf_squads_init(const HfApi* hf,HfMod* owner){
 using namespace seaf;api=hf;mod=owner;
 try {require(hf&&hf->version>=9,"HellForge SDK 9 is required");require(hf->build_id()&&!strcmp(hf->build_id(),"25480438"),"game layout has not been validated");
  for(auto& s:squads)s.shuttle=shuttle_resource;
  require(!hf->on(owner,HF_EVENT_ENTITIES_READY,Prepare,nullptr),"ready subscription failed");
  require(tm_tick_join(hf,owner,Tick,nullptr,"Heavy SEAF squads")!=0,"shared game-thread update unavailable");
  hf->log(owner,"Bundled SEAF squad delivery initialized");return 0;
 }catch(const std::exception& e){if(hf)hf->log(owner,"SQUAD_REGISTRATION_FAILED: %s",e.what());return 1;}
}

extern "C" int heavy_seaf_support_call(unsigned squad,unsigned type,const float* dispatched){
 using namespace seaf;
 if(!host()||squad>=4||type==0||dispatched==nullptr||!api||!mod)return 0;
 try{
  require(prepared&&!failed&&assets_ready,"squad assets are not ready");
  require(pending_calls.size()<128,"delivery queue full");
  auto target=beacon_target(type,dispatched);
  PendingCall call{};call.squad=squad;for(unsigned n=0;n<3;n++)call.position[n]=target.position[n];pending_calls.push_back(call);
  api->log(mod,"SUPPORT_SQUAD_QUEUED: %s; beacon %u; landing %.1f %.1f %.1f; dispatch %.1f %.1f %.1f",squads[squad].title,target.entity,call.position[0],call.position[1],call.position[2],dispatched[0],dispatched[1],dispatched[2]);
  return 1;
 }catch(const std::exception& e){api->log(mod,"SUPPORT_SQUAD_REFUSED: %s: %s",squads[squad].title,e.what());return 0;}
}

extern "C" int heavy_seaf_squads_ready(void){return seaf::prepared&&!seaf::failed&&seaf::assets_ready;}
/* 1 for a shuttle, passenger or tracked trooper of a player-called delivery, so
 * the SEAF squad pool leaves it to this path. Game thread only. */
extern "C" int heavy_seaf_squad_owned(uint32_t id){
 using namespace seaf;
 for(const auto& job:jobs){if(job.shuttle.id==id)return 1;for(const auto& t:job.troops)if(t.identity.id==id)return 1;}
 for(const auto& a:arrivals)if(a.identity.id==id)return 1;
 for(const auto& t:live_troops)if(t.id==id)return 1;
 for(const auto& t:soldiers)if(t.identity.id==id)return 1;
 return 0;
}
extern "C" int heavy_seaf_squads_can_spawn(void){return seaf::capacity_for_squad();}
extern "C" int heavy_seaf_spawn_squad(unsigned squad,const float* position){
 using namespace seaf;
 if(!host()||squad>=4||position==nullptr||!heavy_seaf_squads_ready()||pending_calls.size()>=128)return 0;
 try{for(unsigned n=0;n<3;n++)require(std::isfinite(position[n])&&std::abs(position[n])<99000,"invalid landing point");
  PendingCall call{};call.squad=squad;for(unsigned n=0;n<3;n++)call.position[n]=position[n];pending_calls.push_back(call);
  api->log(mod,"SQUAD_QUEUED: %s; landing %.1f %.1f %.1f",squads[squad].title,position[0],position[1],position[2]);return 1;
 }catch(const std::exception& e){api->log(mod,"SQUAD_CALL_REFUSED: %s",e.what());return 0;}
}
