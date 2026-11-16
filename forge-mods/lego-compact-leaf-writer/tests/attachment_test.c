#undef NDEBUG
#include <assert.h>
#include <stdarg.h>
#include "../src/attachment_observer.c"
#include "../src/envelope.c"
struct HfMod { const HfApi *api; };
static HfApi api;
static struct HfMod mod;
static unsigned reads,logs,ticks,subscriptions,checks,completions,stopLogs,maxLogLength;
static unsigned positives;
static unsigned printLogs,captureReadOrdinal,failReadOrdinal,mutationReadBytes;
static unsigned perturbEnvironment;
static HANDLE captureEntered,captureResume;static uintptr_t captureBlockAt;static volatile LONG captureBlocked;
static int ownerMutation;
static int corruptPin,stopOnRead,tickRefusal,changeOnRead,changed;
static uintptr_t mutateAt;
static uint32_t phase;
static const char *build="25480438";
static char lastLog[2048];
static char lastSchema[900];static unsigned schemaLogs;
static char lastRoute[2][900];static unsigned routeLogs,worldCorrupt;static uintptr_t failAt;
static char lastRender[2][900],lastMesh[900];static unsigned renderLogs,registeredLogs,unregisteredLogs,descriptorCorrupt;
static char lastConsistency[2][900];static unsigned consistencyLogs,stableCorrupt;
typedef struct Region { uintptr_t address; size_t size; unsigned char bytes[65536]; } Region;
static Region regions[256];
static unsigned regionCount;
static Region *renderMutationRegion;static unsigned renderMutationOffset,renderMutationSize;
static uint64_t renderMutationValue;
static Region *manager,*generations,*slots,*units[40],*topologies[7];
static Region *playerRow,*playerDesc,*networkHeader,*entityHeader,*networkBuckets,*entityBuckets,*avatarDesc;
static const uint64_t ids[]={0x2BB094EDBC756963ull,0x7305FDF48136C05Cull,0xDD25333BDF5F0143ull,
    0x1E25DAEB99B1A917ull,0x512399E005614901ull,0x4D2F00B6C4929F00ull,0xBB11C8041C14C747ull};
static const uint32_t nodeCounts[]={21,21,43,21,43,17,29};
#define CHECK(x) do {assert(x);checks++;}while(0)
static Region *Add(uintptr_t at,size_t size)
{assert(regionCount<256 && size<=65536);Region *r=&regions[regionCount++];memset(r,0,sizeof *r);r->address=at;r->size=size;return r;}
static void Put(Region *r,unsigned off,uint64_t value,unsigned size)
{assert(off+size<=r->size);memcpy(r->bytes+off,&value,size);}
static int ReadMock(uintptr_t at,void *out,size_t bytes)
{
    reads++;
    if(invocationBudget) {
        captureReadOrdinal++;
        if(perturbEnvironment){SetLastError(999);_mm_setcsr(0x1F80);}
        if(failReadOrdinal && captureReadOrdinal==failReadOrdinal){memset(out,0xAA,bytes);return 1;}
        if(captureBlockAt==at && InterlockedCompareExchange(&captureBlocked,1,0)==0){SetEvent(captureEntered);WaitForSingleObject(captureResume,5000);}
    }
    if(at==failAt){memset(out,0xAA,bytes);return 1;} /* Failed safe reads may leave partial output. */
    if(stopOnRead){stopOnRead=0;Ended(&mod,HF_EVENT_SESSION_END,NULL);return 1;}
    static const uint32_t globalOffsets[]={0x3326340,0x3326468,0x347CEF0,0x346BF98};
    for(unsigned i=0;i<4;i++)if(at==gameModule+globalOffsets[i] && bytes==8){
        uintptr_t value=0x300000000ull+(uintptr_t)i*0x10000000ull;memcpy(out,&value,8);return 0;
    }
    if(at==engine+MANAGER_RVA && bytes==8){uintptr_t a=0x200000000ull;memcpy(out,&a,8);return 0;}
    static const uint32_t vslots[][2]={{0xE8,0x2BD870},{0xF8,0x2BD890},{0x1A8,0x2BD9A0},{0x1B0,0x2BD9C0}};
    for(unsigned i=0;i<4;i++)if(at==engine+VTABLE_RVA+vslots[i][0] && bytes==8){uintptr_t a=engine+vslots[i][1];memcpy(out,&a,8);return 0;}
    static const uint32_t renderV[][5]={{0x16880B0,0x4F2490,0xC91A0,0xC91B0,0xC91C0},
      {0x1688950,0x510FA0,0xC91A0,0xC91B0,0xC91C0},{0x167A478,0x354380,0xC91A0,0xC91B0,0xC91C0},
      {0x1672E70,0x1E4CF0,0xC91A0,0xC91B0,0xC91C0},{0x168B5A0,0x5C2D20,0x1CC6C0,0x1CC6B0,0x5C2DF0}};
    for(unsigned i=0;i<sizeof renderV/sizeof renderV[0];i++)for(unsigned j=0;j<4;j++)if(at==engine+renderV[i][0]+j*8 && bytes==8){
        uintptr_t a=engine+renderV[i][j+1];memcpy(out,&a,8);return 0;
    }
    if(at==engine+0x16729A8 && bytes==sizeof "RenderWorld"){memcpy(out,"RenderWorld",bytes);return 0;}
    if(at==engine+0x1689440 && bytes==sizeof "state_reflection_stream_rps"){memcpy(out,"state_reflection_stream_rps",bytes);return 0;}
    for(unsigned i=0;i<regionCount;i++) {
        Region *r=&regions[i];
        if(at>=r->address && bytes<=r->size && at-r->address<=r->size-bytes) {
            memcpy(out,r->bytes+at-r->address,bytes);
            if(changeOnRead && !changed && at==mutateAt && (!mutationReadBytes||bytes==mutationReadBytes)){
                changed=1;
                if(ownerMutation==1)Put(playerRow,0x2C8,99,8);
                else if(ownerMutation==2)Put(avatarDesc,12,1,4);
                else if(ownerMutation==3)Put(networkBuckets,0,0xFFFFFFFF,4);
                else if(ownerMutation==4)Put(networkHeader,16,3,4);
                else if(ownerMutation==6)Put(renderMutationRegion,renderMutationOffset,renderMutationValue,renderMutationSize);
                else if(ownerMutation==7){Put(renderMutationRegion,4,0xFFFFFF,4);Put(renderMutationRegion,8,0,8);Put(renderMutationRegion,0xD8,0,4);}
                else generations->bytes[0]++;
            }
            return 0;
        }
    }
    return 1; /* World/pose pointer targets have no mapped synthetic region. */
}
static int CodeMock(uintptr_t at,void *out,size_t bytes)
{
    for(unsigned i=0;i<sizeof stablePins/sizeof stablePins[0];i++)if(at==engine+stablePins[i].rva && bytes==stablePins[i].size){
        memcpy(out,stablePins[i].bytes,bytes);if(corruptPin || stableCorrupt==i+1)((unsigned char*)out)[0]^=1;return 0;
    }
    for(unsigned i=0;i<sizeof descriptorPins/sizeof descriptorPins[0];i++)if(at==engine+descriptorPins[i].rva && bytes==descriptorPins[i].size){
        memcpy(out,descriptorPins[i].bytes,bytes);if(corruptPin || descriptorCorrupt==i+1)((unsigned char*)out)[0]^=1;return 0;
    }
    for(unsigned i=0;i<sizeof worldPins/sizeof worldPins[0];i++)if(at==engine+worldPins[i].rva && bytes==worldPins[i].size){
        memcpy(out,worldPins[i].bytes,bytes);if(corruptPin || worldCorrupt==i+1)((unsigned char*)out)[0]^=1;return 0;
    }
    for(unsigned i=0;i<sizeof renderPins/sizeof renderPins[0];i++)if(at==engine+renderPins[i].rva && bytes==renderPins[i].size){
        memcpy(out,renderPins[i].bytes,bytes);if(corruptPin)((unsigned char*)out)[0]^=1;return 0;
    }
    for(unsigned i=0;i<sizeof pins/sizeof pins[0];i++)if(at==engine+pins[i].rva && bytes==pins[i].size){
        memcpy(out,pins[i].bytes,bytes);if(corruptPin)((unsigned char*)out)[0]^=1;return 0;
    }
    for(unsigned i=0;i<sizeof ownerPins/sizeof ownerPins[0];i++) {
        uintptr_t base=ownerPins[i].game?gameModule:engine;
        if(at==base+ownerPins[i].rva && bytes==ownerPins[i].size){
            memcpy(out,ownerPins[i].bytes,bytes);if(corruptPin)((unsigned char*)out)[0]^=1;return 0;
        }
    }
    return 1;
}
static const char *Build(void){return build;}
static const char *Id(HfMod *m){return OBS_ID;}
static const char *Folder(HfMod *m){return "D:/held-synthetic-test-no-files-created";}
static uint32_t Phase(HfMod *m){return phase;}
static uintptr_t Module(const char *name,size_t *size){assert(!size);return !strcmp(name,"game.dll")?0x180000000ull:0x100000000ull;}
static void Log(HfMod *m,const char *fmt,...)
{
    logs++;va_list args;va_start(args,fmt);int n=vsnprintf(lastLog,sizeof lastLog,fmt,args);va_end(args);
    assert(n>=0 && n<900); if(printLogs)printf("%s\n",lastLog); /* Leaves room for loader timestamp/prefix in1024-byte buffer. */
    if((unsigned)n>maxLogLength)maxLogLength=(unsigned)n;
    if(strstr(lastLog,"LEGO_COMPACT_WRITER association ")){assert(strstr(lastLog,"positive=1"));positives++;}
    if(strstr(lastLog,"LEGO_COMPACT_WRITER complete "))completions++;
    if(strstr(lastLog,"LEGO_COMPACT_WRITER stopped "))stopLogs++;
    if(strstr(lastLog,"LEGO_COMPACT_WRITER schema_refusal ")){schemaLogs++;memcpy(lastSchema,lastLog,(size_t)n+1);}
    if(strstr(lastLog,"LEGO_COMPACT_WRITER route_refusal ")){unsigned row=strstr(lastLog,"row=1 ")?1:0;routeLogs++;memcpy(lastRoute[row],lastLog,(size_t)n+1);}
    if(strstr(lastLog,"LEGO_COMPACT_WRITER render_refusal ")){unsigned row=strstr(lastLog,"row=1 ")?1:0;renderLogs++;memcpy(lastRender[row],lastLog,(size_t)n+1);}
    if(strstr(lastLog,"LEGO_COMPACT_WRITER consistency_refusal ")){unsigned row=strstr(lastLog,"row=1 ")?1:0;consistencyLogs++;memcpy(lastConsistency[row],lastLog,(size_t)n+1);}
    if(strstr(lastLog,"LEGO_COMPACT_WRITER mesh ")){if(strstr(lastLog," registered=1 "))registeredLogs++;else unregisteredLogs++;memcpy(lastMesh,lastLog,(size_t)n+1);}
}
static int On(HfMod *m,HfEvent e,HfEventCallback cb,void *user){subscriptions++;return 0;}
static int OnTick(HfMod *m,HfTickCallback cb,void *user){ticks++;return !tickRefusal;}
static int RealLocalRead(uintptr_t at,void *out,size_t bytes)
{SIZE_T got=0;return !(ReadProcessMemory(GetCurrentProcess(),(const void*)at,out,bytes,&got) && got==bytes);}
static void Reset(void)
{
    stopped=installed=tickLock=0;armedAt=0;nextPoll=UINT64_MAX;
    cursor=visited=recordCount=updateSerial=observedMask=duplicateResources=0;
    readTick=readTotal=byteTick=byteTotal=0;budgetFailure=0;memset(rejected,0,sizeof rejected);
    ownerMutation=0;positives=0;gameModule=0x180000000ull;
    reads=logs=ticks=subscriptions=completions=stopLogs=0;
    corruptPin=stopOnRead=tickRefusal=changeOnRead=changed=0;mutateAt=0;
    failAt=0;routeLogs=worldCorrupt=0;memset(lastRoute,0,sizeof lastRoute);
    renderLogs=registeredLogs=unregisteredLogs=descriptorCorrupt=stableCorrupt=0;memset(lastRender,0,sizeof lastRender);lastMesh[0]=0;
    build="25480438";phase=HF_LOAD_BOOT;engine=0x100000000ull;hf=&api;regionCount=0;
    manager=Add(0x200000000ull,0xB0);Put(manager,0x98,8,4);Put(manager,0x88,0x220000000ull,8);Put(manager,0xA0,0x210000000ull,8);
    generations=Add(0x210000000ull,8192);slots=Add(0x220000000ull,65536);
    Region *resources=Add(0x240000000ull,64);Put(resources,0,AVATAR_RESOURCE,8);
    for(unsigned i=0;i<8;i++) {
        uintptr_t u=0x230000000ull+i*0x1000;units[i]=Add(u,0x200);Put(slots,i*8,u,8);
        Put(units[i],0,engine+VTABLE_RVA,8);Put(units[i],0x30,resources->address+i*8,8);
        Put(units[i],0x70,i?nodeCounts[i-1]:128,4);
        Put(units[i],0x88,0x260000000ull+i*0x10000,8); /* no mapped world array */
        Put(units[i],0x98,0x270000000ull+i*0x1000,8);
        if(i) {
            Put(resources,i*8,ids[i-1],8);Put(units[i],0x1D0,units[0]->address,8);
            Put(units[i],0x1E0,i<7?u+0x1000:0,8);Put(units[i],0x78,0x260000000ull,8);
            topologies[i-1]=Add(0x270000000ull+i*0x1000,512);
            for(unsigned j=0;j<nodeCounts[i-1];j++){Put(topologies[i-1],j*4,2,2);Put(topologies[i-1],j*4+2,j,2);}
        }
    }
    Put(units[0],0x1D8,units[1]->address,8);
    Region *invite=Add(0x300000000ull+0xAC550,4);Put(invite,0,2,4);
    Region *state=Add(0x300000000ull+0xAC21C,4);Put(state,0,3,4);
    playerRow=Add(0x310000000ull,0x400);Put(playerRow,0x84,1,4);Put(playerRow,0x2C8,77,8);
    Put(playerRow,0xE8,0x340000000ull,8);Put(playerRow,0x3A8,10,4);Put(playerRow,0x2E0,3,4);
    Region *transition=Add(0x320000000ull+0x159CC,4);Put(transition,0,0,4);
    Region *peer=Add(0x320000000ull+0xB398,8);Put(peer,0,77,8);
    playerDesc=Add(0x340000000ull,24);Put(playerDesc,8,5,4);Put(playerDesc,20,1,1);
    networkHeader=Add(0x330000000ull+0xF22EC8,20);
    entityHeader=Add(0x330000000ull+0xF1AEB0,20);
    networkBuckets=Add(0x350000000ull,32768);entityBuckets=Add(0x360000000ull,32768);
    memset(networkBuckets->bytes,0xFF,32768);memset(entityBuckets->bytes,0xFF,32768);
    Put(networkHeader,0,networkBuckets->address,8);Put(networkHeader,8,4096,4);Put(networkHeader,12,0xFFFFFFFF,4);Put(networkHeader,16,2,4);
    Put(entityHeader,0,entityBuckets->address,8);Put(entityHeader,8,4096,4);Put(entityHeader,12,0xFFFFFFFF,4);Put(entityHeader,16,2,4);
    Put(networkBuckets,20*8,10,4);Put(networkBuckets,20*8+4,1,4);
    Put(entityBuckets,506*8,253,4);Put(entityBuckets,506*8+4,1,4);
    avatarDesc=Add(0x330000000ull+0xF32F18+24,24);Put(avatarDesc,0,AVATAR_RESOURCE,8);
    Put(avatarDesc,8,253,4);Put(avatarDesc,12,0,4);Put(avatarDesc,16,10,4);Put(avatarDesc,20,1,4);
    assert(ManagerRead(&initial));reads=readTick=readTotal=byteTick=byteTotal=0;
}
#include "metadata_fixture.h"
#include "invocation_checks.h"


