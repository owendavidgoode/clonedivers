#pragma once
#include "engine.h"
#include "package_layout.h"
#include <set>
namespace seaf {
// These existing game packages are absent from HellForge 0.1.14's catalogue.
// Request each once through the normal reference-counted loader and keep its
// reference for the session, since a squad can outlive its mission beacon.
inline bool uncatalogued_package(uint64_t id){
 return id==0x6EB59430A00A525Cull||id==0x522BE23400CC6086ull||id==0x73CFBBF2E883AF96ull||id==0xF230D2D2ADCC034Aull;
}
inline std::set<uint64_t> held_packages;
inline void prove_package_loader(){
 size_t n=strlen(package_request_proof)/2;auto b=bytes(game+package_request_rva,n);
 for(size_t i=0;i<n;i++){auto digit=[](char c){return c<='9'?c-'0':(c|32)-'a'+10;};require(b[i]==digit(package_request_proof[i*2])*16+digit(package_request_proof[i*2+1]),"package loader differs from reviewed build");}
}
inline bool package_resident(uintptr_t exe,uint64_t id){
 auto manager=ptr(ptr(exe+27329032)+1024);auto resources=ptr(manager+520);
 auto count=read<unsigned>(resources+128);require(count<=8192,"package list bounds");if(!count)return false;
 auto list=ptr(resources+136);
 for(unsigned i=0;i<count;i++){
  auto package=ptr(list+i*8);if(read<uint64_t>(package+16)!=id)continue;
  auto parts=read<unsigned>(package+24);require(parts<=64,"package part bounds");if(!parts)return false;
  auto array=ptr(package+32);for(unsigned p=0;p<parts;p++)if(read<unsigned>(ptr(array+p*8)+28)!=4)return false;
  return true;
 }
 return false;
}
inline int load_squad_package(uint64_t id){
 if(!uncatalogued_package(id))return api->load_package(mod,id);
 if(!held_packages.count(id)){
  prove_package_loader();auto system=ptr(game+package_system_rva);auto capacity=read<unsigned>(system+8);
  require(capacity>=16&&capacity<=65536&&(capacity&(capacity-1))==0,"package reference capacity invalid");
  auto entries=ptr(system);auto empty_key=read<uint64_t>(system+16);unsigned used=0;
  for(unsigned n=0;n<capacity;n++)if(read<uint64_t>(entries+n*16)!=empty_key)++used;
  require((used+1)*4<capacity*3,"package reference map full");
  native<void(*)(void*,const uint64_t*,uint32_t)>(package_request_rva)((void*)system,&id,1);
  held_packages.insert(id);api->log(mod,"SQUAD_PACKAGE_REQUESTED: %016llx",(unsigned long long)id);
 }
 size_t size=0;auto exe=api->module_base("helldivers2.exe",&size);require(exe&&size>27329040,"package engine unavailable");
 return package_resident(exe,id)?HF_PACKAGE_RESIDENT:HF_PACKAGE_LOADING;
}
}
