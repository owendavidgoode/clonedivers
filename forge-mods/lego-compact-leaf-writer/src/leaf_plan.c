#include "leaf_plan.h"
#include <string.h>
#include <limits.h>
#include "leaf_asset_pins.h"
static uint32_t u32(const void *p){uint32_t x;memcpy(&x,p,4);return x;}
static uintptr_t ptr(const void *p){uintptr_t x;memcpy(&x,p,8);return x;}
static int span(uintptr_t at,size_t n){return at>=0x10000 && at<0x0000800000000000ull && n && n<=0x0000800000000000ull-at;}
static int reject(lp_plan *p){if(!p->trace.error)p->trace.error=1;return 0;}
static int equal(lp_plan *p,const void *actual,const void *expected,size_t n){
 if(!memcmp(actual,expected,n))return 1;
 lp_trace *t=&p->trace;t->error=3;t->comparison_length=(uint32_t)n;
 const unsigned char *a=actual,*e=expected;size_t i=0;while(i<n && a[i]==e[i])i++;
 t->mismatch_index=(uint32_t)i;t->probe_offset=(uint32_t)(i>16?i-16:0);
 t->probe_bytes=(uint32_t)(n-t->probe_offset);if(t->probe_bytes>64)t->probe_bytes=64;
 memcpy(t->expected,e+t->probe_offset,t->probe_bytes);memcpy(t->observed,a+t->probe_offset,t->probe_bytes);return 0;
}
static const unsigned char *save(lp_plan *p,uintptr_t at,size_t n,lp_read_fn read,void *user){
 p->trace.read_at=at;p->trace.read_size=(uint32_t)n;p->trace.read_outcome=0;
 if(!span(at,n)||p->blocks>=LP_BLOCKS||n>LP_BYTES-p->bytes){p->trace.error=1;return NULL;}
 unsigned char *out=p->evidence+p->bytes;if(!read(at,out,n,user)){memset(out,0,n);p->trace.error=2;p->trace.read_outcome=2;return NULL;}
 p->trace.read_outcome=1;
 p->block[p->blocks++]=(lp_block){at,p->bytes,(uint32_t)n};p->bytes+=(uint32_t)n;return out;
}
static lp_ext_asset lp_dyn_assets[LP_MAX_DYN_ASSETS];
static uint32_t lp_dyn_count;

void lp_reset_dynamic_assets(void) {
 lp_dyn_count=0;
}

int lp_register_asset(const lp_asset *asset,uint32_t pin_mask) {
 if(!asset || !asset->resource || asset->ordinal<1 || asset->ordinal>4 ||
    !asset->bones || asset->bones>LP_BONES || !asset->groups || asset->groups>LP_GROUPS)return 0;
 if(lp_asset_find(asset->resource,asset->ordinal))return 0;
 if(lp_dyn_count>=LP_MAX_DYN_ASSETS)return 0;
 lp_ext_asset *dst=&lp_dyn_assets[lp_dyn_count++];
 dst->asset=*asset;
 dst->pin_mask=pin_mask;
 return 1;
}

const lp_asset *lp_asset_find(uint64_t resource,unsigned ordinal){
 for(unsigned i=0;i<28;i++)if(lp_assets[i]->resource==resource&&lp_assets[i]->ordinal==ordinal)return lp_assets[i];
 for(uint32_t i=0;i<lp_dyn_count;i++)if(lp_dyn_assets[i].asset.resource==resource&&lp_dyn_assets[i].asset.ordinal==ordinal)return &lp_dyn_assets[i].asset;
 return NULL;
}

const lp_ext_asset *lp_asset_find_ext(uint64_t resource,unsigned ordinal){
 static lp_ext_asset baked_view;
 for(unsigned i=0;i<28;i++)if(lp_assets[i]->resource==resource&&lp_assets[i]->ordinal==ordinal){
  baked_view.asset=*lp_assets[i];baked_view.pin_mask=LP_PIN_ALL;return &baked_view;
 }
 for(uint32_t i=0;i<lp_dyn_count;i++)if(lp_dyn_assets[i].asset.resource==resource&&lp_dyn_assets[i].asset.ordinal==ordinal)return &lp_dyn_assets[i];
 return NULL;
}

int lp_capture(lp_plan *p,const unsigned char *raw,uintptr_t mesh,uintptr_t pool,uint64_t resource,unsigned ordinal,lp_read_fn read,void *user){
 memset(p,0,sizeof *p);p->trace.phase=1;p->trace.stage=1;p->trace.mismatch_index=UINT32_MAX;
 const lp_asset *a=NULL;uint32_t pin_mask=0;
 if(resource){
  for(unsigned i=0;i<28;i++)if(lp_assets[i]->resource==resource&&lp_assets[i]->ordinal==ordinal){a=lp_assets[i];pin_mask=LP_PIN_ALL;break;}
  if(!a)for(uint32_t i=0;i<lp_dyn_count;i++)if(lp_dyn_assets[i].asset.resource==resource&&lp_dyn_assets[i].asset.ordinal==ordinal){
   a=&lp_dyn_assets[i].asset;pin_mask=lp_dyn_assets[i].pin_mask;break;
  }
 }
 if(!a||!raw||!read||!span(mesh,0xd0)||!span(pool,0x180)||!a->bones||a->bones>LP_BONES||!a->groups||a->groups>LP_GROUPS)return reject(p);
 p->trace.stage=2;p->graph=ptr(raw+152);p->count=u32(raw+184);p->mesh_node=u32(raw+112);
 if(!p->count||p->count>128||u32(raw+188)!=p->count||p->mesh_node>=p->count)return reject(p);
 p->trace.stage=3;const unsigned char *h=save(p,p->graph,56,read,user);
 if(!h||!equal(p,h,raw+256,56))return reject(p);
 p->arrays[0]=ptr(h+24);p->arrays[1]=ptr(h+48);
 for(unsigned t=0;t<2;t++){
  p->trace.stage=4+t;if(!span(p->arrays[t],(size_t)p->count*48))return reject(p);
  const unsigned char *row=save(p,p->arrays[t]+48*p->mesh_node,48,read,user);
  if(!row||!equal(p,row,raw+(t?7168:1024)+48*p->mesh_node,48))return reject(p);
 }
 p->trace.stage=6;const unsigned char *v=save(p,mesh+0x30,16,read,user);
 if(!v||u32(v)!=p->mesh_node||ptr(v+8)!=p->graph)return reject(p);
 const unsigned offsets[5]={0x70,0x80,0x90,0x98,0xc8},sizes[5]={8,8,4,8,4},rawOffsets[5]={480,496,512,520,568};
 uintptr_t skin=0,sub=0;
 for(unsigned i=0;i<5;i++){p->trace.stage=7+i;v=save(p,mesh+offsets[i],sizes[i],read,user);if(!v||!equal(p,v,raw+rawOffsets[i],sizes[i]))return reject(p);
  if(i==0)skin=ptr(v);else if(i==1)p->bind=ptr(v);else if(i==2)p->groups=u32(v);else if(i==3)sub=ptr(v);else p->output_count=u32(v);}
 p->trace.stage=12;if(!p->groups||p->groups>LP_GROUPS||p->groups!=a->groups||p->output_count>UINT32_MAX/48)return reject(p);
 p->trace.stage=13;v=save(p,pool+0x170,8,read,user);if(!v||ptr(v)!=ptr(raw+672))return reject(p);p->pool_base=ptr(v);
 p->trace.stage=14;const unsigned char *s=save(p,skin,16,read,user);if(!s||u32(s)!=a->bones||!u32(s+12)||!span(skin,u32(s+8)+1ull)||!span(skin,u32(s+12)+1ull))return reject(p);
 p->map=skin+u32(s+8);uintptr_t directory=skin+u32(s+12);
 p->trace.stage=15;v=save(p,p->map,(size_t)a->bones*4,read,user);if(!v)return reject(p);
 if((pin_mask&LP_PIN_MAP)&&!equal(p,v,a->map,(size_t)a->bones*4))return reject(p);
 for(unsigned i=0;i<a->bones;i++){uint32_t m=(pin_mask&LP_PIN_MAP)?a->map[i]:u32(v+i*4);if(m>=p->count)return reject(p);}
 p->trace.stage=16;v=save(p,p->bind,(size_t)a->bones*48,read,user);if(!v)return reject(p);
 if((pin_mask&LP_PIN_BIND)&&!equal(p,v,a->bind,(size_t)a->bones*48))return reject(p);
 p->trace.stage=17;const unsigned char *d=save(p,directory,4+8*a->groups,read,user);if(!d||u32(d)!=a->groups)return reject(p);
 p->trace.stage=18;const unsigned char *records=save(p,sub,(size_t)p->groups*40,read,user);if(!records)return reject(p);
 for(unsigned i=0;i<p->groups;i++){
  p->trace.stage=20+i*4;const unsigned char *r=records+i*40;unsigned q=u32(r+24);if(q>=a->groups)return reject(p);
  unsigned offset=u32(d+4+8*q),n=u32(d+8+8*q);
  if((pin_mask&LP_PIN_PALETTE)&&n!=a->counts[q])return reject(p);
  if(!n||n>LP_BONES||!span(directory,(size_t)offset+1))return reject(p);
  p->group[i].palette=directory+offset;p->group[i].count=n;
  p->trace.stage=21+i*4;v=save(p,p->group[i].palette,4*n,read,user);if(!v)return reject(p);
  if((pin_mask&LP_PIN_PALETTE)){if(!equal(p,v,a->palette[q],4*n))return reject(p);}
  else {for(unsigned j=0;j<n;j++)if(u32(v+j*4)>=a->bones)return reject(p);}
  for(unsigned t=0;t<2;t++){p->trace.stage=22+i*4+t;unsigned encoded=u32(r+28+4*t);if(encoded%3)return reject(p);
   unsigned at=encoded/3;if(at>p->output_count||n>p->output_count-at)return reject(p);p->group[i].offset[t]=at*48;}
 }
 return 1;
}
int lp_recheck(lp_plan *p,lp_read_fn read,void *user){
 unsigned char bytes[LP_BYTES];if(!p)return 0;
 memset(&p->trace,0,sizeof p->trace);p->trace.phase=2;p->trace.stage=99;p->trace.mismatch_index=UINT32_MAX;
 if(p->blocks>LP_BLOCKS||p->bytes>LP_BYTES||!read)return reject(p);
 for(unsigned i=0;i<p->blocks;i++){const lp_block *b=&p->block[i];p->trace.stage=100+i;
  p->trace.read_at=b->at;p->trace.read_size=b->bytes;p->trace.read_outcome=0;
  if(!b->bytes||b->offset>p->bytes||b->bytes>p->bytes-b->offset)return reject(p);
  if(!read(b->at,bytes,b->bytes,user)){memset(bytes,0,sizeof bytes);p->trace.read_outcome=2;p->trace.error=2;return 0;}
  p->trace.read_outcome=1;if(!equal(p,bytes,p->evidence+b->offset,b->bytes))return 0;
 }
 return 1;
}
