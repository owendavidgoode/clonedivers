/* Read-only discovery and guarded instance-data writes. No detours or code edits. */
#pragma once
#include "hellforge_mod.h"
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <stdexcept>
#include <vector>
#include <array>
#include <algorithm>
#include <cmath>
#include "pins.h"
namespace seaf {
inline const HfApi* api; inline HfMod* mod; inline uintptr_t game;
inline void require(bool value,const char* why){if(!value)throw std::runtime_error(why);}
template<class T> T read(uintptr_t at){T v{};require(at>=65536&&!api->read(at,&v,sizeof v),"unreadable game data");return v;}
inline uintptr_t ptr(uintptr_t at){auto p=read<uintptr_t>(at);require(p>=65536&&p<0x800000000000ull,"invalid game pointer");return p;}
inline std::vector<unsigned char> bytes(uintptr_t at,size_t n){std::vector<unsigned char> b(n);require(!api->read(at,b.data(),n),"unreadable record");return b;}
inline bool writable(uintptr_t at,size_t size){
 static thread_local uintptr_t cached_base=0,cached_end=0;
 if(at>=cached_base&&at+size<=cached_end)return true;
 MEMORY_BASIC_INFORMATION m{};
 if(VirtualQuery((void*)at,&m,sizeof m)==sizeof m&&m.State==MEM_COMMIT&&m.Type==MEM_PRIVATE&&m.Protect==PAGE_READWRITE&&at+size<=(uintptr_t)m.BaseAddress+m.RegionSize){
  cached_base=(uintptr_t)m.BaseAddress;cached_end=cached_base+m.RegionSize;return true;
 }
 return false;
}
inline void data_write(uintptr_t at,const void* data,size_t size){
 require(writable(at,size),"target is not private writable data");
 memcpy((void*)at,data,size);
 require(!memcmp((const void*)at,data,size),"instance write readback failed");
}
struct Change{uintptr_t at;std::vector<unsigned char> before,after;};
inline void transaction(const std::vector<Change>& changes){
 for(auto& c:changes)require(c.before.size()==c.after.size()&&writable(c.at,c.before.size())&&bytes(c.at,c.before.size())==c.before,"instance changed before commit");
 size_t count=0;
 try{for(auto& c:changes){++count;data_write(c.at,c.after.data(),c.after.size());}}
 catch(...){while(count){auto& c=changes[--count];data_write(c.at,c.before.data(),c.before.size());}throw;}
}
template<class T> Change change(uintptr_t at,T value){Change c{at,bytes(at,sizeof(T)),std::vector<unsigned char>(sizeof(T))};memcpy(c.after.data(),&value,sizeof value);return c;}
inline int index(uintptr_t at,uint32_t key){
 auto p=ptr(at);auto n=read<uint32_t>(at+8),empty=read<uint32_t>(at+12),mult=read<uint32_t>(at+16);
 require(n&&n<=1048576&&!(n&(n-1)),"invalid instance map capacity");
 if(key==empty)return -1;
 uint32_t start=key*mult;
 for(uint32_t probe=0;probe<std::min(n,4096u);++probe){auto b=p+((start+probe)&(n-1))*8;auto found=read<uint32_t>(b);if(found==key)return (int)read<uint32_t>(b+4);if(found==empty)return -1;}
 return -1;
}
struct Descriptor{uint64_t type;uint32_t id,unit,network,flags;};
inline Descriptor descriptor(uint32_t id){auto root=ptr(game+0x346BF98);int i=index(root+15838896,id);require(i>=0&&i<0x100000,"entity missing");auto d=read<Descriptor>(root+15937304+(size_t)i*24);require(d.id==id,"entity identity reused");return d;}
inline Descriptor network_descriptor(uint32_t id){auto d=descriptor(id);require(d.network!=0x7FFF,"entity has no network identity");auto root=ptr(game+54968216);auto n=index(root+15871688,d.network);require(n>=0&&n<0x100000&&read<unsigned>(root+15937312+(size_t)n*24)==id,"network identity no longer resolves to entity");return d;}
struct Instance{uintptr_t manager,row,desc;unsigned index;bool owned;};
inline Instance instance(unsigned global,unsigned hash,unsigned desc,unsigned rows,unsigned stride,uint32_t id){
 auto m=ptr(game+global);int i=index(m+hash,id);require(i>=0&&i<65536,"component missing");auto d=ptr(ptr(m+desc)+(size_t)i*8);auto value=read<Descriptor>(d);require(value.id==id,"component owner changed");return {m,ptr(m+rows)+(size_t)i*stride,d,(unsigned)i,(value.flags&1)!=0};
}
inline Instance inventory(uint32_t id){auto i=instance(53634872,40,64,80,48,id);require(i.owned,"inventory not locally owned");return i;}
inline uint32_t held(uint32_t id){auto m=ptr(game+53634080);int i=index(m+48,id);return i<0?0:read<uint32_t>(ptr(m+96)+(size_t)i*464);}
struct Ability{uint32_t id;bool active;};
inline Ability ability(uint32_t id){auto i=instance(53634624,24,48,56,224,id);return {read<uint32_t>(i.row),read<uint8_t>(i.row+16)!=0};}
inline bool host(){if(!api||!mod||!api->session)return false;HfSession s{};s.role=0xFFFFFFFFu;api->session(mod,&s);return s.role==HF_SESSION_SOLO||s.role==HF_SESSION_HOST;}
inline void mission(){require(api->game_state(mod)==HF_STATE_MISSION&&host(),"host mission required");}
#ifdef SEAF_TEST
void* test_native(unsigned rva);
template<class T> T native(unsigned rva){return reinterpret_cast<T>(test_native(rva));}
#else
template<class T> T native(unsigned rva){return reinterpret_cast<T>(game+rva);}
#endif
inline void prove(){
 require(api->build_id()&&!strcmp(api->build_id(),"25480438"),"unsupported game layout");
 for(auto& pin:seaf_pins){size_t n=strlen(pin.hex)/2;auto b=bytes(game+pin.rva,n);for(size_t i=0;i<n;i++){auto digit=[](char c){return c<='9'?c-'0':(c|32)-'a'+10;};require(b[i]==(digit(pin.hex[2*i])*16+digit(pin.hex[2*i+1])),"native implementation differs from reviewed build");}}
}
}
