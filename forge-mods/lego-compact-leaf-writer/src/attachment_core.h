/* Read-only attachment metadata shared by the bounded actual-call observer.
 * Pose sampling and SDK-managed delegation are in the invocation modules. */
#define OBS_ID "clonedivers.lego-compact-leaf-writer"
#define MANAGER_RVA 0x1A100F0u
#define VTABLE_RVA 0x1676D80u
#define CAPACITY_LIMIT 8192u
#define COOP_UNIT_CAPACITY_LIMIT 262144u
#define RECORD_LIMIT 32u
#define PER_TICK 2u
#define ARM_MS 90000u
#define NODE_LIMIT 128u
#define PARENT_NODE_LIMIT 1024u
#define CHILD_LIMIT 32u
#define COOP_CHILD_LIMIT 256u
#define READ_TICK_LIMIT 512u
#define READ_TOTAL_LIMIT 131072u
#define BYTE_TICK_LIMIT 65536u
#define BYTE_TOTAL_LIMIT 8388608u
static const HfApi *hf;
static uintptr_t engine;
static volatile LONG stopped, installed, tickLock;
static _Thread_local int coopProbeActive;
static _Thread_local uint32_t coopProbeReads, coopProbeBytes;
#define COOP_PROBE_READ_LIMIT 16384u
#define COOP_PROBE_BYTE_LIMIT 4194304u
static uint64_t armedAt, nextPoll;
static uint32_t cursor, visited, recordCount, updateSerial, observedMask, duplicateResources;
static uint32_t readTick, readTotal, byteTick, byteTotal, rejected[8];
static int budgetFailure;
static wchar_t armPath[1100], stopPath[1100];
typedef struct Manager { uintptr_t address, slots, generations; uint32_t capacity; } Manager;
typedef struct Identity {
    uintptr_t unit, resourceAddress;
    uint64_t resource;
    uint32_t index;
    uint8_t generation;
} Identity;
typedef struct UnitMeta {
    Identity id;
    uintptr_t externalWorld, world, topology, parent, firstChild, sibling;
    uint32_t nodes;
} UnitMeta;
typedef struct Topology { uint16_t type, node; } Topology;
typedef struct Record {
    UnitMeta child, parent;
    Topology topology[NODE_LIMIT];
    uintptr_t chain[CHILD_LIMIT];
    uint32_t chainCount, chainStatus, reciprocal, type2, type2InBounds, unknownTypes;
    uint32_t parentPresent, worldEqual;
} Record;
static Manager initial;
typedef struct Pin { uint32_t rva, size; unsigned char bytes[32]; } Pin;
#include "pins.h"
static int Stopped(void) { return (writerCaptureActive || coopProbeActive)?0:InterlockedCompareExchange(&stopped,0,0)!=0; }
static int Address(uintptr_t a) { return a>=0x10000 && a<0x0000800000000000ull; }
static int Read(uintptr_t at,void *out,size_t bytes)
{
    if(invocationBudget)return InvocationRead(at,out,bytes);
    if(coopProbeActive) {
        if(Stopped() || !Address(at) || !bytes || bytes>0x0000800000000000ull-at ||
           coopProbeReads>=COOP_PROBE_READ_LIMIT || bytes>COOP_PROBE_BYTE_LIMIT-coopProbeBytes){
            memset(out,0,bytes);return 0;
        }
        coopProbeReads++;coopProbeBytes+=(uint32_t)bytes;
        int ok=hf->read(at,out,bytes)==0;
        if(!ok)memset(out,0,bytes);
        return ok;
    }
    if(logicalTick>=READ_TICK_LIMIT || logicalTotal>=READ_TOTAL_LIMIT){budgetFailure=1;return 0;}
    logicalTick++;logicalTotal++;
    if(Stopped() || !Address(at) || !bytes || bytes>0x0000800000000000ull-at)return 0;
    if(readTick>=READ_TICK_LIMIT || readTotal>=READ_TOTAL_LIMIT ||
       bytes>BYTE_TICK_LIMIT-byteTick || bytes>BYTE_TOTAL_LIMIT-byteTotal){budgetFailure=1;return 0;}
    readTick++;readTotal++;byteTick+=(uint32_t)bytes;byteTotal+=(uint32_t)bytes;
    sdkReadTotal++;int ok=hf->read(at,out,bytes)==0;
    return ok && !Stopped();
}
static int Pointer(uintptr_t at,uintptr_t *out)
{ return Read(at,out,8) && Address(*out); }
static int Nullable(uintptr_t at,uintptr_t *out)
{ return Read(at,out,8) && (!*out || Address(*out)); }
static int ManagerReadBounded(Manager *out,uint32_t capacityLimit)
{
    memset(out,0,sizeof *out);
    return Pointer(engine+MANAGER_RVA,&out->address) && Read(out->address+0x98,&out->capacity,4) &&
        out->capacity && out->capacity<=capacityLimit && Pointer(out->address+0x88,&out->slots) &&
        Pointer(out->address+0xA0,&out->generations) &&
        (size_t)out->capacity*8<=0x0000800000000000ull-out->slots &&
        out->capacity<=0x0000800000000000ull-out->generations;
}
static int ManagerRead(Manager *out) { return ManagerReadBounded(out,CAPACITY_LIMIT); }
static int CoopManagerRead(Manager *out) { return ManagerReadBounded(out,COOP_UNIT_CAPACITY_LIMIT); }
static uint32_t Target(uint64_t id)
{
    static const uint64_t ids[]={0x2BB094EDBC756963ull,0x7305FDF48136C05Cull,
        0xDD25333BDF5F0143ull,0x1E25DAEB99B1A917ull,0x512399E005614901ull,
        0x4D2F00B6C4929F00ull,0xBB11C8041C14C747ull};
    for(unsigned i=0;i<7;i++)if(ids[i]==id)return 1u<<i;
    return 0;
}
/* Identity comes from the proven manager slots and generation array, not Unit+8. */
static int IdentityRead(const Manager *m,uint32_t index,Identity *out)
{
    uintptr_t table=0;memset(out,0,sizeof *out);out->index=index;
    if(index>=m->capacity || !Read(m->generations+index,&out->generation,1) ||
       !Nullable(m->slots+(uintptr_t)index*8,&out->unit))return 1;
    if(!out->unit)return 2;
    if(!Pointer(out->unit,&table) || table!=engine+VTABLE_RVA)return 3;
    if(!Pointer(out->unit+0x30,&out->resourceAddress) || !Read(out->resourceAddress,&out->resource,8) ||
       !out->resource)return 4;
    return 0;
}
static int MetaRead(const Manager *m,uint32_t index,UnitMeta *out,uint32_t maxNodes)
{
    memset(out,0,sizeof *out);
    if(IdentityRead(m,index,&out->id))return 0;
    uintptr_t u=out->id.unit;
    return Read(u+0x70,&out->nodes,4) && out->nodes && out->nodes<=maxNodes &&
        Nullable(u+0x78,&out->externalWorld) && Pointer(u+0x88,&out->world) &&
        Pointer(u+0x98,&out->topology) && Nullable(u+0x1D0,&out->parent) &&
        Nullable(u+0x1D8,&out->firstChild) && Nullable(u+0x1E0,&out->sibling);
}
/* Exhaustive bounded pointer lookup; duplicate matches are refused. No foreign dereference. */
static int ChainRead(Record *r)
{
    uintptr_t at=r->parent.firstChild;
    while(at && r->chainCount<CHILD_LIMIT) {
        for(uint32_t i=0;i<r->chainCount;i++)if(r->chain[i]==at){r->chainStatus=2;return 1;}
        uintptr_t table=0,parent=0,next=0;
        if(!Pointer(at,&table) || !Nullable(at+0x1D0,&parent) || !Nullable(at+0x1E0,&next))return 0;
        r->chain[r->chainCount++]=at;
        if(table!=engine+VTABLE_RVA || parent!=r->parent.id.unit){r->chainStatus=3;return 1;}
        if(at==r->child.id.unit)r->reciprocal=1;
        at=next;
    }
    if(at)r->chainStatus=1;
    return 1;
}
#include "owner_lookup.h"


#include "render_core.h"



