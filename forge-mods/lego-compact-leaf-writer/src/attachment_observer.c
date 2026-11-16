/* HELD local-ship grouped writer. Private R8 only; no shared graph/C4 writes. */
#include "hellforge_mod.h"
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <stdarg.h>
#include <intrin.h>
#include "envelope.h"
#include "invocation_format.h"
#include "shadow_math.h"
#include "writer_core.h"
static gw_state writer;
static _Thread_local int writerCaptureActive; /* Capture-thread lifecycle predicate only. */
static int coopDynamicMode;
static cm_rig shadowRig;
static cm_workspace shadowWork; /* Serialized by the accepted observer gate. */
static uint32_t shadowRecords,shadowOk,shadowFail;
static uint32_t leafNormalMask,leafGroupedMask,leafOk,leafFail;
_Static_assert(SM_RAW_BYTES==IV_PAYLOAD_BYTES,"Raw payload remains unchanged");
_Static_assert(IV_PAYLOAD_BYTES+sizeof(sm_result)<=SE_PAYLOAD,"Private tail bounded");
_Static_assert(GW_RECEIPT_OFFSET+sizeof(gw_receipt)<=SE_PAYLOAD,"Leaf receipt bounded");
_Static_assert(offsetof(se_record,payload)%16==0,"Private header aligned");
typedef struct IvBudget {uint32_t logical,sdk,bytes,status;unsigned char *payload;const se_observe_context *call;} IvBudget;
static se_state invocationEnvelope;
static IvBudget *invocationBudget; /* Accessible only under the envelope metadata gate. */
static uint32_t logicalTick,logicalTotal,sdkReadTotal;
static volatile LONG invocationFatal,invocationTerminal;
static uint32_t invocationPid,invocationFiles,invocationAccepted,invocationRefused,invocationAbandoned,invocationMask;
static uint64_t invocationBirth,invocationDrainAt;
static se_target invocationWhitelist[SE_TARGETS];
static wchar_t invocationDirectory[1100],invocationDirectoryFinal[1100];
static HANDLE invocationDirectoryHandle=INVALID_HANDLE_VALUE;
static uint32_t invocationWhitelistCount;
static int invocationUserStop,invocationTimedOut;
static void InvocationQueueLog(HfMod *mod,const char *text);
static int InvocationRead(uintptr_t at,void *out,size_t bytes);

#include "attachment_core.h"
#include "invocation_payload.h"
#include "coop_discovery.h"
#include "invocation_control.h"
#include "writer_integration.h"
#include "writer_fault.h"

static int RequestFile(const wchar_t *path)
{
    HANDLE f=CreateFileW(path,FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
        NULL,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(f==INVALID_HANDLE_VALUE)return 0;
    BY_HANDLE_FILE_INFORMATION i;
    int ok=GetFileInformationByHandle(f,&i) && !i.nFileSizeHigh && !i.nFileSizeLow &&
        !(i.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT));
    CloseHandle(f); return ok;
}
#include "invocation_tick.h"
#include "writer_tick.h"
#include "owner_pins.h"
#include "render_pins.h"
#include "world_pins.h"
#include "descriptor_pins.h"
#include "stable_pins.h"
#include "invocation_pins.h"

static int Pins(void)
{
    unsigned char bytes[32]; uintptr_t value=0;
    static const unsigned char leafPrologue[16]={0x48,0x8b,0xc4,0x48,0x89,0x58,0x18,0x48,0x89,0x70,0x20,0x57,0x48,0x83,0xec,0x40};
    if(hf->read_code(engine+0x5b2990,bytes,16)||memcmp(bytes,leafPrologue,16))return 0;
    for(unsigned i=0;i<sizeof invocationPins/sizeof invocationPins[0];i++)
        if(Stopped() || hf->read_code(engine+invocationPins[i].rva,bytes,invocationPins[i].size) ||
           memcmp(bytes,invocationPins[i].bytes,invocationPins[i].size))return 0;
    for(unsigned i=0;i<sizeof pins/sizeof pins[0];i++)
        if(Stopped() || hf->read_code(engine+pins[i].rva,bytes,pins[i].size) || memcmp(bytes,pins[i].bytes,pins[i].size))return 0;
    for(unsigned i=0;i<sizeof ownerPins/sizeof ownerPins[0];i++) {
        uintptr_t base=ownerPins[i].game?gameModule:engine;
        if(Stopped() || hf->read_code(base+ownerPins[i].rva,bytes,ownerPins[i].size) ||
           memcmp(bytes,ownerPins[i].bytes,ownerPins[i].size))return 0;
    }
    for(unsigned i=0;i<sizeof renderPins/sizeof renderPins[0];i++)
        if(Stopped() || hf->read_code(engine+renderPins[i].rva,bytes,renderPins[i].size) ||
           memcmp(bytes,renderPins[i].bytes,renderPins[i].size))return 0;
    for(unsigned i=0;i<sizeof worldPins/sizeof worldPins[0];i++)
        if(Stopped() || hf->read_code(engine+worldPins[i].rva,bytes,worldPins[i].size) ||
           memcmp(bytes,worldPins[i].bytes,worldPins[i].size))return 0;
    for(unsigned i=0;i<sizeof descriptorPins/sizeof descriptorPins[0];i++)
        if(Stopped() || hf->read_code(engine+descriptorPins[i].rva,bytes,descriptorPins[i].size) ||
           memcmp(bytes,descriptorPins[i].bytes,descriptorPins[i].size))return 0;
    for(unsigned i=0;i<sizeof stablePins/sizeof stablePins[0];i++)
        if(Stopped() || hf->read_code(engine+stablePins[i].rva,bytes,stablePins[i].size) ||
           memcmp(bytes,stablePins[i].bytes,stablePins[i].size))return 0;
    static const uint32_t renderSlots[][5]={{0x16880B0,0x4F2490,0xC91A0,0xC91B0,0xC91C0},
        {0x1688950,0x510FA0,0xC91A0,0xC91B0,0xC91C0},{0x167A478,0x354380,0xC91A0,0xC91B0,0xC91C0},
        {0x1672E70,0x1E4CF0,0xC91A0,0xC91B0,0xC91C0},{0x168B5A0,0x5C2D20,0x1CC6C0,0x1CC6B0,0x5C2DF0}};
    for(unsigned i=0;i<sizeof renderSlots/sizeof renderSlots[0];i++)for(unsigned j=0;j<4;j++)
        if(!Read(engine+renderSlots[i][0]+j*8,&value,8)||value!=engine+renderSlots[i][j+1])return 0;
    static const char worldLabel[]="RenderWorld",queueLabel[]="state_reflection_stream_rps";
    if(!Read(engine+0x16729A8,bytes,sizeof worldLabel)||memcmp(bytes,worldLabel,sizeof worldLabel) ||
       !Read(engine+0x1689440,bytes,sizeof queueLabel)||memcmp(bytes,queueLabel,sizeof queueLabel))return 0;
    static const uint32_t slots[][2]={{0xE8,0x2BD870},{0xF8,0x2BD890},{0x1A8,0x2BD9A0},{0x1B0,0x2BD9C0}};
    for(unsigned i=0;i<sizeof slots/sizeof slots[0];i++)
        if(!Read(engine+VTABLE_RVA+slots[i][0],&value,8)||value!=engine+slots[i][1])return 0;
    return 1;
}
static void Loaded(HfMod *mod,HfEvent event,void *user)
{
    if(Stopped() || InterlockedExchange(&installed,1))return;
    engine=hf->module_base("helldivers2.exe",NULL);
    gameModule=hf->module_base("game.dll",NULL);
    const char *logs=hf->logs_folder(mod); wchar_t folder[1024];
    if(!Span(gameModule,0x347CEF8)||!Address(engine)||engine>0x0000800000000000ull-0x1A100F8u || !logs || !*logs ||
       !MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,logs,-1,folder,1024) ||
       swprintf(armPath,1100,L"%ls\\compact-leaf-writer.arm",folder)<0 ||
       swprintf(stopPath,1100,L"%ls\\compact-leaf-writer.stop",folder)<0 ||
       GetFileAttributesW(stopPath)!=INVALID_FILE_ATTRIBUTES || !Pins()) {Finish(mod,"module_path_pin_or_stale_control_refusal");return;}
    FILETIME created,exitTime,kernel,userTime;
    if(!GetProcessTimes(GetCurrentProcess(),&created,&exitTime,&kernel,&userTime)){Finish(mod,"process_time_refusal");return;}
    uint64_t birth=((uint64_t)created.dwHighDateTime<<32)|created.dwLowDateTime;
    invocationPid=GetCurrentProcessId();invocationBirth=birth;
    if(!InvocationDirectoryInit(folder)){Finish(mod,"session_directory_refusal");return;}
    writer.engine=engine;
    if(!hf->on_game_thread(mod,WriterTick,NULL)){Finish(mod,"tick_refused");return;}
    if(hf->hook_ex(mod,engine+0x5B2990,(void*)LeafObserved,(void**)&writer.leaf_next,0)){Finish(mod,"leaf_hook_refused");return;}
    writer.approved_leaf_next=InterlockedCompareExchangePointer(&writer.leaf_next,NULL,NULL);
    if(!writer.approved_leaf_next){Finish(mod,"initial_leaf_next_refused");return;}
    if(hf->hook_ex(mod,engine+0x512B80,(void*)SkinObserved,se_next_slot(&invocationEnvelope),0)){Finish(mod,"hook_refused");return;}
    writer.approved_next=InterlockedCompareExchangePointer(&invocationEnvelope.next,NULL,NULL);
    if(!writer.approved_next){Finish(mod,"initial_next_refused");return;}
    if(hf->hook_ex(mod,engine+0x1E7CD0,(void*)RenderRemoveObserved,(void**)&writer.remove_next,0)){Finish(mod,"remove_hook_refused");return;}
    writer.approved_remove_next=InterlockedCompareExchangePointer(&writer.remove_next,NULL,NULL);
    if(!writer.approved_remove_next){Finish(mod,"initial_remove_next_refused");return;}
    coopDynamicMode=1;
    coop_discovery_init();
    writer.resolve_target=CoopResolveTarget;
    writer.retire_handle=CoopRetireHandle;
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER ready coop_dynamic=1 auto_armed=1 managed_hook=512B80 leaf_hook=5B2990 remove_hook=1E7CD0 priority=0 max_targets=%u originals=M_P_R8_return leaf_graph=private_after_writer_ready pose_reads=bounded no_shared_pose_writes pid=%lu birth_100ns=%llu engine=%llX game=%llX hash_probe_limit=%u",
        COOP_MAX_TARGETS,(unsigned long)GetCurrentProcessId(),(unsigned long long)birth,(unsigned long long)engine,(unsigned long long)gameModule,COOP_HASH_PROBE_LIMIT);
}
HF_MOD_EXPORT int hellforge_mod_init(const HfApi *api,HfMod *mod)
{
    if(!api||api->version!=16||api->size<offsetof(HfApi,load_phase)+sizeof api->load_phase ||
       !api->build_id||!api->mod_id||!api->load_phase||!api->read||!api->read_code||!api->module_base||
       !api->on||!api->on_game_thread||!api->hook_ex||!api->logs_folder||!api->log||!api->build_id()||
       strcmp(api->build_id(),"25480438")||!api->mod_id(mod)||strcmp(api->mod_id(mod),OBS_ID)||
       api->load_phase(mod)!=HF_LOAD_BOOT)return 1;
    hf=api;
    coop_registry_init(&coopReg);
    if(sm_prepare(&shadowRig)!=CM_OK)return 1;
    if(se_initialize(&invocationEnvelope,InvocationObserve,NULL,IV_ATTEMPTS,IV_PER_MESH)!=SE_OK)return 1;
    gw_initialize(&writer,&invocationEnvelope,&shadowRig,0,WriterCapture,NULL,WriterCompleted,NULL);
    writer.plan_capture=WriterPlan;
    writer.copy_sampler=SamplerCopy;
    if(api->on(mod,HF_EVENT_SESSION_END,WriterEnded,NULL))return 1;
    return api->on(mod,HF_EVENT_GAME_LOADED,Loaded,NULL);
}




