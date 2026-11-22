/* Exact-build read-only guard. Inferred internals, not an upstream ABI. MIT. */
#include "c6_guard.h"
#include "c6_sha256.h"
#include "c6_target.h"
#include <string.h>
#define C6_IMAGE_SIZE C6_TARGET_IMAGE_SIZE
#define C6_COMPONENTS C6_TARGET_COMPONENTS
#define C6_STRIDE 0x1e8u
static uint16_t u16(const uint8_t *p){return (uint16_t)(p[0]|(unsigned)p[1]<<8);}
static uint32_t u32(const uint8_t *p){return p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint64_t u64(const uint8_t *p){return u32(p)|((uint64_t)u32(p+4)<<32);}
static int copy(C6Read read,void *ctx,uintptr_t base,uint32_t rva,void *out,size_t n)
{
    if(!read||!base||base>UINTPTR_MAX-C6_IMAGE_SIZE||rva>C6_IMAGE_SIZE||n>C6_IMAGE_SIZE-rva)return 0;
    return read(ctx,base+rva,out,n)==0;
}
int c6_image_probe(C6Read read,void *ctx,uintptr_t base,size_t size)
{
    static const uint8_t text_sha[32]=C6_TARGET_TEXT_SHA_BYTES;
    static const uint32_t target[5]=C6_TARGET_SLOT_RVAS;
    static const uint32_t def[8]=C6_TARGET_DEF_RVAS;
    uint8_t b[4096],digest[32];
    if(size!=C6_IMAGE_SIZE||!copy(read,ctx,base,0,b,1024))return 0;
    if(memcmp(b,"MZ",2)||u32(b+0x3c)!=0x78||memcmp(b+0x78,"PE\0\0",4)||
       u16(b+0x7c)!=0x8664||u16(b+0x7e)!=7||u32(b+0x80)!=C6_TARGET_TIMESTAMP||
       u16(b+0x8c)!=240||u16(b+0x90)!=0x20b||u32(b+0xa0)!=C6_TARGET_ENTRY||u32(b+0xc8)!=C6_IMAGE_SIZE||
       memcmp(b+0x180,".text\0\0\0",8)||u32(b+0x188)!=C6_TARGET_TEXT_BYTES||u32(b+0x18c)!=0x1000)return 0;
    C6Sha256 sha;c6_sha_init(&sha);
    /* The exact PE's relocation directory contains no .text relocation. This
     * fingerprint therefore remains valid under ASLR; changed/hooked text fails. */
    for(uint32_t at=0;at<C6_TARGET_TEXT_BYTES;){size_t n=C6_TARGET_TEXT_BYTES-at;if(n>sizeof b)n=sizeof b;
        if(!copy(read,ctx,base,0x1000+at,b,n))return 0;
        c6_sha_update(&sha,b,n);at+=(uint32_t)n;}
    c6_sha_final(&sha,digest);if(memcmp(digest,text_sha,32))return 0;
    if(!copy(read,ctx,base,C6_TARGET_TABLE,b,56)||u32(b)!=1||u32(b+4)!=56||u32(b+8)!=112||u32(b+12)!=64)return 0;
    for(unsigned i=0;i<5;i++)if(u64(b+16+8*i)!=base+target[i])return 0;
    if(!copy(read,ctx,base,C6_TARGET_DEFINITION,b,64))return 0;
    for(unsigned i=0;i<8;i++)if(u64(b+8*i)!=(def[i]?base+def[i]:0))return 0;
    if(!copy(read,ctx,base,C6_TARGET_NAME,b,17)||memcmp(b,"mod_options_menu",17))return 0;
    return 1;
}
typedef struct Snapshot {uintptr_t component,backend;uint32_t generation,count;} Snapshot;
static int snapshot(C6Read read,void *ctx,uintptr_t base,Snapshot *out)
{
    uint8_t b[40];uint32_t count;uint64_t component,backend;
    /* Separate before/after observations, never a lock or unload guarantee. */
    if(!copy(read,ctx,base,C6_TARGET_STOPPED,b,8))return 0;
    if(b[0]||b[4])return -1;
    if(!copy(read,ctx,base,C6_TARGET_COUNT,b,4)||(count=u32(b))==0||count>32)return 0;
    if(!copy(read,ctx,base,C6_TARGET_COMPONENT_CELL,b,8))return 0;
    component=u64(b);
    if(component<base+C6_COMPONENTS||component>=base+C6_COMPONENTS+(uint64_t)count*C6_STRIDE||
       (component-base-C6_COMPONENTS)%C6_STRIDE)return 0;
    if(!copy(read,ctx,base,(uint32_t)(component-base),b,sizeof b)||u64(b)!=base+C6_TARGET_DEFINITION||
       u32(b+0x10)!=(component-base-C6_COMPONENTS)/C6_STRIDE||
       u32(b+0x18)!=1||u32(b+0x1c)!=1||u32(b+0x24)!=0||!u32(b+0x20))return 0;
    uint32_t generation=u32(b+0x20);
    if(!copy(read,ctx,base,C6_TARGET_BACKEND_CELL,b,8)||(backend=u64(b))==0)return 0;
    if(!copy(read,ctx,base,C6_TARGET_STOPPED,b,8))return 0;
    if(b[0]||b[4])return -1;
    *out=(Snapshot){(uintptr_t)component,(uintptr_t)backend,generation,count};return 1;
}
static int same(const Snapshot *a,const Snapshot *b)
{return a->component==b->component&&a->backend==b->backend&&a->generation==b->generation&&a->count==b->count;}
int c6_guard_observe(C6Guard *g,C6Read read,void *ctx,uintptr_t base,size_t size,uint32_t thread)
{
    if(!g)return -1;
    if(g->dead)return -1;
    if(!thread||!read||(g->image_checked&&(base!=g->base||thread!=g->thread||size!=C6_IMAGE_SIZE))){g->dead=1;return -1;}
    if(!g->image_checked){
        if(!c6_image_probe(read,ctx,base,size)){g->dead=1;return -1;}
        g->base=base;g->thread=thread;g->image_checked=1;
    }
    Snapshot a,b;
    int first=snapshot(read,ctx,base,&a);
    int second=first>0?snapshot(read,ctx,base,&b):0;
    if(first<0||second<0){g->dead=1;return -1;}
    if(!first||!second||!same(&a,&b)){
        if(g->latched){g->dead=1;return -1;}return 0;
    }
    if(g->latched&&(a.component!=g->component||a.backend!=g->backend||a.generation!=g->generation)){
        g->dead=1;return -1;
    }
    g->component=a.component;g->backend=a.backend;g->generation=a.generation;g->latched=1;
    return 1;
}
