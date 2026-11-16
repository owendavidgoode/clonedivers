/* Only bounded metadata reads. No native call, hook, matrix, skin or palette payload. */
#define HANDLE_SLOT_LIMIT 0x200000u
#define RENDER_VECTOR_LIMIT 0x200000u
#define MESH_LIMIT 8u
#define GROUP_LIMIT 256u
#define GROUP_MESH_LIMIT 256u
#define GROUP_TOTAL_LIMIT 4096u
#define MATCH_LIMIT 32u
#define LOG_LIMIT 256u
typedef struct Vec { uint32_t length,capacity;uintptr_t data; } Vec;
typedef struct RHeader {
    uintptr_t manager,queue,world,handles;
    uint32_t capacity,mask,generation,live;
    Vec objects,types,map;
} RHeader;
typedef struct Ref { uint32_t node,handle;uintptr_t cache; } Ref;
typedef struct Resolved { uint32_t handle,index,type,nodes;uintptr_t object,vtable; } Resolved;
typedef struct MeshMeta {
    uintptr_t descriptor,association,sourceGraph,skin;
    uint32_t handle,node,flags,registered;Resolved resolved;Ref ref;
} MeshMeta;
typedef struct RenderMeta {
    RHeader header;Resolved graph;uintptr_t meshEntries;
    uint32_t graphHandle,meshes,registeredMeshes,unregisteredMeshes;MeshMeta mesh[MESH_LIMIT];
} RenderMeta;
static int InvocationPayload(const Record *record,const RenderMeta *meta,const Owner *owner);
static int InvocationPublish(HfMod *mod);
typedef struct SavedTarget { UnitMeta child,parent;RenderMeta render;Owner owner; } SavedTarget;
static int SameWriterOwner(const Owner *a,const Owner *b) {
    return a->game==b->game && a->players==b->players && a->context==b->context && a->entities==b->entities &&
        a->playerDescriptor==b->playerDescriptor && a->peer==b->peer && a->rowPeer==b->rowPeer &&
        a->player==b->player && a->network==b->network && a->descriptor.resource==b->descriptor.resource &&
        a->descriptor.entity==b->descriptor.entity && a->descriptor.reference==b->descriptor.reference &&
        a->descriptor.network==b->descriptor.network;
}
typedef struct GroupEntry { uintptr_t provider,list;uint32_t count;uint8_t enabled,padding[3]; } GroupEntry;
typedef struct GroupMeta {
    Vec header;GroupEntry entry;uintptr_t list[GROUP_MESH_LIMIT];
    uintptr_t vtable,refs;uint32_t type,nodes;Ref first;Resolved graph;
} GroupMeta;
static SavedTarget targets[7];
static uint32_t renderStage,targetCursor,meshCursor,groupCursor,groupTarget,groupTotal,matchedGroups;
static uint32_t meshRecords,declaredMeshes,finalMask,logsTotal,logsTick;
static uint32_t registeredRecords,unregisteredRecords;
static uint64_t groupMatches;
static Vec poolInitial;static int poolKnown,groupKnown;
static GroupMeta groupBaseline,groupProvider;
/* Snapshot of the already-read manager+10..+5F block. Never dereference its values. */
typedef struct SchemaFailure { uintptr_t manager;unsigned char raw[80];uint32_t failed;int pending,logged; } SchemaFailure;
static SchemaFailure schemaFailure;
/* First refusal only; valid bits identify successful safe reads. Never log by re-reading. */
typedef struct RouteFailure {
    RHeader h;unsigned char queue[136],world[32];uintptr_t back;
    uint32_t stage,attempted,valid,checked,failed;int pending,logged;
    const char *reason;
} RouteFailure;
static RouteFailure routeFailure;
typedef struct ResolveTrace {
    uint32_t attempted,valid,checked,failed,slot;
    Resolved r;uint32_t requestedType,headerType;
} ResolveTrace;
static ResolveTrace resolveTrace;
typedef struct RenderFailure {
    uintptr_t unit,association,expectedSource;uint64_t resource;uint32_t unitNodes,ordinal,stage,attempted,valid,checked,failed;
    uint32_t resolveKind,headerValid,graphValid,conflictOrdinal;RenderMeta meta;ResolveTrace resolve;
    int pending,logged;const char *reason;
} RenderFailure;
static RenderFailure renderRefusal;
/* Already-read comparison operands only. No failure path dereferences these values. */
typedef struct ConsistencyFailure {
    RHeader oldHeader,newHeader;Identity oldParent,newParent;
    uint64_t mask,ignored,oldValue,newValue;uint32_t target,prior,offset,width,differences,stage;
    const char *reason,*kind;int pending,logged,headers,parents;
} ConsistencyFailure;
static ConsistencyFailure consistencyRefusal;
static const char *renderFailure="none";
static uint32_t U32(const void *p){uint32_t v;memcpy(&v,p,4);return v;}
static uintptr_t U64(const void *p){uintptr_t v;memcpy(&v,p,8);return v;}
#include "sampler_witness.h"
static int RF(const char *reason){renderFailure=reason;return 0;}
static uint64_t RenderDiff(const RenderMeta *a,const RenderMeta *b)
{
    uint64_t m=0;
    if(a->header.manager!=b->header.manager)m|=1ull;
    if(a->header.queue!=b->header.queue)m|=2ull;
    if(a->header.world!=b->header.world)m|=4ull;
    if(a->header.handles!=b->header.handles)m|=8ull;
    if(a->header.capacity!=b->header.capacity||a->header.mask!=b->header.mask||
       a->header.generation!=b->header.generation||a->header.live!=b->header.live)m|=16ull;
    if(memcmp(&a->header.objects,&b->header.objects,sizeof(Vec)))m|=32ull;
    if(memcmp(&a->header.types,&b->header.types,sizeof(Vec)))m|=64ull;
    if(memcmp(&a->header.map,&b->header.map,sizeof(Vec)))m|=128ull;
    if(memcmp(&a->graph,&b->graph,sizeof(Resolved)))m|=256ull;
    if(a->meshEntries!=b->meshEntries)m|=512ull;
    if(a->graphHandle!=b->graphHandle||a->meshes!=b->meshes||a->registeredMeshes!=b->registeredMeshes||
       a->unregisteredMeshes!=b->unregisteredMeshes)m|=1024ull;
    for(unsigned i=0;i<MESH_LIMIT;i++) {
        MeshMeta x=a->mesh[i],y=b->mesh[i];
        if(x.flags!=y.flags)m|=1ull<<(24+i);
        x.flags=y.flags=0;
        if(memcmp(&x,&y,sizeof x))m|=1ull<<(16+i);
    }
    return m;
}
static void StableRender(RenderMeta *out,const RenderMeta *in)
{
    *out=*in;out->header.queue=0;
    memset(&out->header.objects,0,sizeof(Vec));memset(&out->header.types,0,sizeof(Vec));memset(&out->header.map,0,sizeof(Vec));
    for(unsigned i=0;i<MESH_LIMIT;i++)out->mesh[i].flags=0;
}
static int ConsFail(const char *kind,const char *reason,uint32_t target,uint32_t prior,
                    const void *old,const void *now,size_t bytes,uint64_t mask,uint64_t ignored,
                    const RHeader *oh,const RHeader *nh,const Identity *op,const Identity *np)
{
    if(!consistencyRefusal.pending && !schemaFailure.pending && !routeFailure.pending && !renderRefusal.pending) {
        ConsistencyFailure *f=&consistencyRefusal;memset(f,0,sizeof *f);
        f->pending=1;f->kind=kind;f->reason=reason;f->target=target;f->prior=prior;f->stage=renderStage;
        f->mask=mask;f->ignored=ignored;
        if(oh&&nh){f->headers=1;f->oldHeader=*oh;f->newHeader=*nh;}
        if(op&&np){f->parents=1;f->oldParent=*op;f->newParent=*np;}
        const unsigned char *a=old,*b=now;f->offset=UINT32_MAX;
        for(size_t i=0;i<bytes;i++)if(a[i]!=b[i]){if(f->offset==UINT32_MAX)f->offset=(uint32_t)i;f->differences++;}
        if(f->offset!=UINT32_MAX){f->width=(uint32_t)(bytes-f->offset);if(f->width>8)f->width=8;
            memcpy(&f->oldValue,a+f->offset,f->width);memcpy(&f->newValue,b+f->offset,f->width);}
    }
    return RF(reason);
}
static int RouteFail(RouteFailure *r,unsigned stage,const char *reason)
{
    r->stage=stage;r->reason=reason;
    if(!routeFailure.pending){routeFailure=*r;routeFailure.pending=1;}
    return RF(reason);
}
static int RenderFail(RenderFailure *f,const RenderMeta *meta,unsigned stage,const char *reason)
{
    if(!renderRefusal.pending && !schemaFailure.pending && !routeFailure.pending) {
        f->stage=stage;f->reason=reason;f->meta=*meta;f->resolve=resolveTrace;
        renderRefusal=*f;renderRefusal.pending=1;
    }
    return RF(reason);
}
static void RLog(HfMod *mod,const char *fmt,...)
{
    if(Stopped())return;
    if(logsTick>=7 || logsTotal>=LOG_LIMIT-1){
        InvocationQueueLog(mod,"LEGO_COMPACT_LEAF_WRITER stopped reason=log_budget coverage=partial");
        logsTick++;logsTotal++;se_stop(&invocationEnvelope);InterlockedExchange(&invocationTerminal,1);InterlockedExchange(&stopped,1);return;
    }
    char text[900];va_list args;va_start(args,fmt);int n=vsnprintf(text,sizeof text,fmt,args);va_end(args);
    if(n<0 || n>=(int)sizeof text){
        InvocationQueueLog(mod,"LEGO_COMPACT_LEAF_WRITER stopped reason=log_format coverage=partial");
        logsTick++;logsTotal++;se_stop(&invocationEnvelope);InterlockedExchange(&invocationTerminal,1);InterlockedExchange(&stopped,1);return;
    }
    logsTick++;logsTotal++;InvocationQueueLog(mod,text);
}
static void Finish(HfMod *mod,const char *reason)
{
    se_stop(&invocationEnvelope);InterlockedExchange(&invocationTerminal,1);
    if(consistencyRefusal.pending) {
        ConsistencyFailure *f=&consistencyRefusal;reason=f->reason;
        if(!f->logged) {
            f->logged=1;
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER consistency_refusal row=0 rows=2 kind=%s reason=%s stage=%u target=%u prior=%u mask=%016llX ignored=%016llX offset=%u width=%u differences=%u old_value=%016llX new_value=%016llX headers_valid=%u old_manager=%llX new_manager=%llX old_queue=%llX new_queue=%llX old_world=%llX new_world=%llX non_atomic=1 failed_fields_followed=0",
                f->kind,f->reason,f->stage,f->target,f->prior,(unsigned long long)f->mask,(unsigned long long)f->ignored,
                f->offset,f->width,f->differences,(unsigned long long)f->oldValue,(unsigned long long)f->newValue,f->headers,
                (unsigned long long)f->oldHeader.manager,(unsigned long long)f->newHeader.manager,
                (unsigned long long)f->oldHeader.queue,(unsigned long long)f->newHeader.queue,
                (unsigned long long)f->oldHeader.world,(unsigned long long)f->newHeader.world);
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER consistency_refusal row=1 rows=2 kind=%s reason=%s stage=%u target=%u prior=%u mask=%016llX parents_valid=%u old_parent_unit=%llX new_parent_unit=%llX old_parent_resource_address=%llX new_parent_resource_address=%llX old_parent_resource=%016llX new_parent_resource=%016llX old_parent_slot=%u new_parent_slot=%u old_parent_generation=%u new_parent_generation=%u non_atomic=1 failed_fields_followed=0",
                f->kind,f->reason,f->stage,f->target,f->prior,(unsigned long long)f->mask,f->parents,
                (unsigned long long)f->oldParent.unit,(unsigned long long)f->newParent.unit,
                (unsigned long long)f->oldParent.resourceAddress,(unsigned long long)f->newParent.resourceAddress,
                (unsigned long long)f->oldParent.resource,(unsigned long long)f->newParent.resource,
                f->oldParent.index,f->newParent.index,f->oldParent.generation,f->newParent.generation);
        }
    }
    if(renderRefusal.pending) {
        RenderFailure *f=&renderRefusal;reason=f->reason;
        if(!f->logged) {
            MeshMeta empty={0};const MeshMeta *m=f->ordinal<MESH_LIMIT?&f->meta.mesh[f->ordinal]:&empty;
            const ResolveTrace *t=&f->resolve;f->logged=1;
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER render_refusal row=0 rows=2 stage=%u reason=%s attempted=%03X valid=%03X checked=%08X failed=%08X unit=%llX resource=%016llX ordinal=%u unit_nodes=%u app_handle=%08X app_association=%llX expected_source=%llX descriptor=%llX descriptor_flags=%08X registered=%u handle=%08X association=%llX source_graph=%llX node=%u meshes=%u registered_meshes=%u unregistered_meshes=%u entries=%llX header_valid=%u graph_valid=%u expected_graph=%llX expected_graph_nodes=%u non_atomic=1 failed_fields_followed=0",
                f->stage,f->reason,f->attempted,f->valid,f->checked,f->failed,(unsigned long long)f->unit,(unsigned long long)f->resource,
                f->ordinal,f->unitNodes,f->meta.graphHandle,(unsigned long long)f->association,(unsigned long long)f->expectedSource,
                (unsigned long long)m->descriptor,m->flags,m->registered,m->handle,(unsigned long long)m->association,
                (unsigned long long)m->sourceGraph,m->node,f->meta.meshes,f->meta.registeredMeshes,f->meta.unregisteredMeshes,
                (unsigned long long)f->meta.meshEntries,f->headerValid,f->graphValid,(unsigned long long)f->meta.graph.object,f->meta.graph.nodes);
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER render_refusal row=1 rows=2 stage=%u reason=%s resolve_kind=%u resolve_attempted=%02X resolve_valid=%02X resolve_checked=%08X resolve_failed=%08X handles=%llX capacity=%u mask=%08X map_length=%u objects_length=%u types_length=%u requested_handle=%08X requested_type=%u sampled_slot=%08X object_index=%u object=%llX type=%u object_header_type=%u vtable=%llX nodes=%u mesh_ref_node=%u mesh_ref_handle=%08X mesh_cache=%llX skin=%llX conflict_ordinal=%u non_atomic=1 failed_fields_followed=0",
                f->stage,f->reason,f->resolveKind,t->attempted,t->valid,t->checked,t->failed,
                (unsigned long long)f->meta.header.handles,f->meta.header.capacity,f->meta.header.mask,
                f->meta.header.map.length,f->meta.header.objects.length,f->meta.header.types.length,
                t->r.handle,t->requestedType,t->slot,t->r.index,(unsigned long long)t->r.object,t->r.type,t->headerType,
                (unsigned long long)t->r.vtable,t->r.nodes,m->ref.node,m->ref.handle,(unsigned long long)m->ref.cache,(unsigned long long)m->skin,f->conflictOrdinal);
        }
    }
    if(routeFailure.pending) {
        RouteFailure *r=&routeFailure;reason=r->reason;
        if(!r->logged) {
            r->logged=1;
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER route_refusal row=0 rows=2 stage=%u reason=%s attempted=%02X valid=%02X checked=%08X failed=%08X manager=%llX queue=%llX world=%llX queue_vtable=%llX queue_label=%llX queue_parent=%llX queue_owner_allocator=%llX world_vtable=%llX world_type=%u world_allocator_vtable=%llX world_allocator_label=%llX back=%llX non_atomic=1 failed_fields_followed=0",
                r->stage,r->reason,r->attempted,r->valid,r->checked,r->failed,
                (unsigned long long)r->h.manager,(unsigned long long)r->h.queue,(unsigned long long)r->h.world,
                (unsigned long long)U64(r->queue),(unsigned long long)U64(r->queue+8),(unsigned long long)U64(r->queue+0x20),
                (unsigned long long)U64(r->queue+0x80),(unsigned long long)U64(r->world),U32(r->world+8),
                (unsigned long long)U64(r->world+0x10),(unsigned long long)U64(r->world+0x18),(unsigned long long)r->back);
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER route_refusal row=1 rows=2 stage=%u reason=%s attempted=%02X valid=%02X checked=%08X failed=%08X manager=%llX queue=%llX world=%llX handles=%llX capacity=%u mask=%08X generation_mask=%08X live_bit=%08X objects_length=%u objects_capacity=%u objects_data=%llX types_length=%u types_capacity=%u types_data=%llX map_length=%u map_capacity=%u map_data=%llX non_atomic=1 failed_fields_followed=0",
                r->stage,r->reason,r->attempted,r->valid,r->checked,r->failed,
                (unsigned long long)r->h.manager,(unsigned long long)r->h.queue,(unsigned long long)r->h.world,
                (unsigned long long)r->h.handles,r->h.capacity,r->h.mask,r->h.generation,r->h.live,
                r->h.objects.length,r->h.objects.capacity,(unsigned long long)r->h.objects.data,
                r->h.types.length,r->h.types.capacity,(unsigned long long)r->h.types.data,
                r->h.map.length,r->h.map.capacity,(unsigned long long)r->h.map.data);
        }
    }
    if(schemaFailure.pending) {
        reason="render_manager_schema"; /* Telemetry failure can never convert this refusal to success. */
        if(!schemaFailure.logged) {
            const unsigned char *r=schemaFailure.raw;schemaFailure.logged=1;
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER schema_refusal gate=render_manager_schema checked_mask=000001FF failed_mask=%08X manager=%llX queue=%llX handles=%llX handles_copy=%llX alloc=%llX release=%llX flags=%08X layout=%08X active=%u capacity=%u mask=%08X generation_mask=%08X user_mask=%08X live_bit=%08X tail50=%08X tail54=%08X bytes=80 non_atomic=1 failed_fields_followed=0",
                schemaFailure.failed,(unsigned long long)schemaFailure.manager,(unsigned long long)U64(r),
                (unsigned long long)U64(r+8),(unsigned long long)U64(r+0x48),(unsigned long long)U64(r+0x10),
                (unsigned long long)U64(r+0x18),U32(r+0x20),U32(r+0x24),U32(r+0x28),U32(r+0x2C),
                U32(r+0x30),U32(r+0x34),U32(r+0x38),U32(r+0x3C),U32(r+0x40),U32(r+0x44));
        }
    }
    int complete=!strcmp(reason,"complete");
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER stopped reason=%s stage=%u expected_mask=7F observed_mask=%02X associated_mask=%02X final_mask=%02X records=%u meshes=%u declared_meshes=%u registered_meshes=%u unregistered_meshes=%u group_cursor=%u groups=%u group_pass_complete=%u partial=%u truncated=%u reads=%u bytes=%u scope=non_atomic_metadata provider_identity=pointer_type_only lifetime=unproven",
         reason,renderStage,observedMask,observedMask,finalMask,recordCount,meshRecords,declaredMeshes,registeredRecords,unregisteredRecords,groupCursor,poolInitial.length,complete,!complete,!complete,readTotal,byteTotal);
    InterlockedExchange(&stopped,1);
}
static int Vector(const Vec *v,uint32_t limit,size_t stride)
{return v->length<=v->capacity && v->capacity<=limit && (!v->capacity || Span(v->data,(size_t)v->capacity*stride));}
static int HeaderRead(uintptr_t manager,RHeader *out)
{
    unsigned char raw[80];RouteFailure route;memset(&route,0,sizeof route);memset(out,0,sizeof *out);
    RHeader *h=&route.h;h->manager=manager;
    if(!Span(manager,0x60)){route.checked=route.failed=1u<<13;return RouteFail(&route,1,"render_manager_span");}
    route.attempted|=1;
    if(!Read(manager+0x10,raw,sizeof raw))return RouteFail(&route,2,"render_manager_read");
    route.valid|=1;
    h->queue=U64(raw);h->handles=U64(raw+8);h->capacity=U32(raw+0x2C);
    h->mask=U32(raw+0x30);h->generation=U32(raw+0x34);h->live=U32(raw+0x3C);
    /* Same nine gates, evaluated independently on local bytes for exact failure telemetry.
     * Span is arithmetic only. No pointer from a failed gate is followed. */
    uint32_t failed=0;
    if(U64(raw+0x10)!=engine+0x75230)failed|=1u<<0;
    if(U64(raw+0x18)!=engine+0x75420)failed|=1u<<1;
    if(U32(raw+0x20)!=0x348)failed|=1u<<2;
    if(U32(raw+0x24)!=0x04000004)failed|=1u<<3;
    if(U32(raw+0x38)!=0xFF000000)failed|=1u<<4;
    if(h->handles!=U64(raw+0x48))failed|=1u<<5;
    if(h->capacity<2 || h->capacity>HANDLE_SLOT_LIMIT)failed|=1u<<6;
    if(!Span(h->handles,(size_t)h->capacity*4))failed|=1u<<7;
    if(!Span(h->queue,0x3E0))failed|=1u<<8; /* Retained conservative arithmetic gate, not a World identity. */
    if(failed) {
        if(!schemaFailure.pending){schemaFailure.manager=manager;memcpy(schemaFailure.raw,raw,80);schemaFailure.failed=failed;schemaFailure.pending=1;}
        return RF("render_manager_schema");
    }
    uint32_t mask=h->capacity-1;mask|=mask>>1;mask|=mask>>2;mask|=mask>>4;mask|=mask>>8;mask|=mask>>16;
    uint32_t g=~(mask|0xFF000000u);
    route.checked=1u<<14;
    if(h->mask!=mask || h->live!=mask+1 || h->generation!=(g&(g<<1))){route.failed=1u<<14;return RouteFail(&route,3,"render_manager_masks");}
    route.attempted|=2;
    if(!Read(h->queue,route.queue,sizeof route.queue)){memset(route.queue,0,sizeof route.queue);return RouteFail(&route,4,"render_queue_read");}
    route.valid|=2;route.checked|=7;
    if(U64(route.queue)!=engine+0x168B5A0)route.failed|=1;
    if(U64(route.queue+8)!=engine+0x1689440)route.failed|=2;
    uintptr_t allocator=U64(route.queue+0x80);
    if(!Address(allocator)||allocator<0x10010)route.failed|=4;
    else {
        h->world=allocator-0x10;route.checked|=8;
        if(!Span(h->world,0x6B0))route.failed|=8;
        else {route.checked|=16;if(U64(route.queue+0x20)!=h->world+0x550)route.failed|=16;}
    }
    if(route.failed)return RouteFail(&route,5,"render_queue_identity");
    route.attempted|=4;
    if(!Read(h->world,route.world,sizeof route.world)){memset(route.world,0,sizeof route.world);return RouteFail(&route,6,"render_world_read");}
    route.valid|=4;route.checked|=0x1E0;
    if(U64(route.world)!=engine+0x1672E70)route.failed|=1u<<5;
    if(U32(route.world+8)!=1)route.failed|=1u<<6;
    if(U64(route.world+0x10)!=engine+0x168B5A0)route.failed|=1u<<7;
    if(U64(route.world+0x18)!=engine+0x16729A8)route.failed|=1u<<8;
    if(route.failed)return RouteFail(&route,7,"render_world_identity");
    route.attempted|=8;
    if(!Read(h->world+0x150,&route.back,8)){route.back=0;return RouteFail(&route,8,"render_world_back_read");}
    route.valid|=8;route.checked|=1u<<9;
    if(route.back!=manager){route.failed|=1u<<9;return RouteFail(&route,9,"render_world_back_mismatch");}
    route.attempted|=16;
    if(!Read(h->world+0x168,&h->objects,16)){memset(&h->objects,0,sizeof h->objects);return RouteFail(&route,10,"render_objects_read");}
    route.valid|=16;route.attempted|=32;
    if(!Read(h->world+0x180,&h->types,16)){memset(&h->types,0,sizeof h->types);return RouteFail(&route,11,"render_types_read");}
    route.valid|=32;route.attempted|=64;
    if(!Read(h->world+0x1B8,&h->map,16)){memset(&h->map,0,sizeof h->map);return RouteFail(&route,12,"render_map_read");}
    route.valid|=64;route.checked|=0x1C00;
    if(!Vector(&h->objects,RENDER_VECTOR_LIMIT,8))route.failed|=1u<<10;
    if(!Vector(&h->types,RENDER_VECTOR_LIMIT,4))route.failed|=1u<<11;
    if(!Vector(&h->map,RENDER_VECTOR_LIMIT,4))route.failed|=1u<<12;
    if(route.failed)return RouteFail(&route,13,"render_table_bounds");
    *out=*h;return 1;
}
static int ResolveFail(Resolved *r,const char *reason)
{resolveTrace.r=*r;return RF(reason);}
static int Resolve(const RHeader *h,uint32_t handle,uint32_t type,Resolved *r)
{
    unsigned char head[24];memset(r,0,sizeof *r);memset(&resolveTrace,0,sizeof resolveTrace);
    r->handle=handle;resolveTrace.requestedType=type;
    uint32_t i=handle&h->mask;resolveTrace.checked=7;
    if(!handle || handle==0xFFFFFF)resolveTrace.failed|=1;
    if(!i || i>=h->capacity || i>=h->map.length)resolveTrace.failed|=2;
    if(!(handle&h->live))resolveTrace.failed|=4;
    if(resolveTrace.failed)return ResolveFail(r,"render_handle_bounds");
    resolveTrace.attempted|=1;
    if(!Read(h->handles+(uintptr_t)i*4,&resolveTrace.slot,4)){resolveTrace.slot=0;return ResolveFail(r,"render_handle_read");}
    resolveTrace.valid|=1;resolveTrace.checked|=8;
    if(resolveTrace.slot!=handle){resolveTrace.failed|=8;return ResolveFail(r,"render_handle_generation");}
    resolveTrace.attempted|=2;
    if(!Read(h->map.data+(uintptr_t)i*4,&r->index,4)){r->index=0;return ResolveFail(r,"render_index_read");}
    resolveTrace.valid|=2;resolveTrace.checked|=16;
    if(r->index>=h->objects.length || r->index>=h->types.length){resolveTrace.failed|=16;return ResolveFail(r,"render_object_index");}
    resolveTrace.attempted|=4;
    if(!Read(h->objects.data+(uintptr_t)r->index*8,&r->object,8)){r->object=0;return ResolveFail(r,"render_object_pointer_read");}
    resolveTrace.valid|=4;resolveTrace.checked|=32;
    if(!Address(r->object)){resolveTrace.failed|=32;return ResolveFail(r,"render_object_pointer");}
    resolveTrace.attempted|=8;
    if(!Read(h->types.data+(uintptr_t)r->index*4,&r->type,4)){r->type=0;return ResolveFail(r,"render_type_read");}
    resolveTrace.valid|=8;resolveTrace.checked|=64;
    if(r->type!=type){resolveTrace.failed|=64;return ResolveFail(r,"render_object_type");}
    if(!Span(r->object,0x78)){resolveTrace.failed|=32;return ResolveFail(r,"render_object_span");}
    resolveTrace.attempted|=16;
    if(!Read(r->object,head,sizeof head))return ResolveFail(r,"render_object_header_read");
    resolveTrace.valid|=16;r->vtable=U64(head);r->nodes=type==2?U32(head+0x10):0;resolveTrace.headerType=U32(head+8);
    uintptr_t wanted=engine+(type==2?0x16880B0u:0x1688950u);resolveTrace.checked|=128;
    if(U32(head+8)!=type || r->vtable!=wanted)resolveTrace.failed|=128;
    if(type==2){resolveTrace.checked|=256;if(!r->nodes || r->nodes>PARENT_NODE_LIMIT)resolveTrace.failed|=256;}
    if(resolveTrace.failed)return ResolveFail(r,"render_object_vtable_or_nodes");
    resolveTrace.r=*r;return 1;
}
static int RenderRead(const UnitMeta *child,RenderMeta *r)
{
    RenderFailure f;memset(&f,0,sizeof f);memset(r,0,sizeof *r);memset(&resolveTrace,0,sizeof resolveTrace);
    f.unit=child->id.unit;f.resource=child->id.resource;f.unitNodes=child->nodes;f.ordinal=f.conflictOrdinal=UINT32_MAX;
    unsigned char app[16];uintptr_t entries[MESH_LIMIT*2];f.checked=1;
    if(!Span(child->id.unit,0xD8)){f.failed=1;return RenderFail(&f,r,1,"app_unit_span");}
    f.expectedSource=child->id.unit+0x60;f.attempted|=1;
    if(!Read(child->id.unit+0x60,app,16))return RenderFail(&f,r,2,"app_graph_read");
    f.valid|=1;f.association=U64(app+8);r->graphHandle=U32(app+4);f.checked|=2;
    if(!Address(f.association)||f.association<0x10010){f.failed|=2;return RenderFail(&f,r,3,"app_graph_association");}
    if(!HeaderRead(f.association-0x10,&r->header))return 0; /* Existing schema/route snapshot owns this refusal. */
    f.headerValid=1;f.resolveKind=1;
    if(!Resolve(&r->header,r->graphHandle,2,&r->graph))return RenderFail(&f,r,4,renderFailure);
    f.graphValid=1;f.checked|=4;
    if(r->graph.nodes!=child->nodes){f.failed|=4;return RenderFail(&f,r,5,"graph_node_count_mismatch");}
    f.attempted|=2;
    if(!Read(child->id.unit+0xC8,&r->meshes,4)){r->meshes=0;return RenderFail(&f,r,6,"unit_mesh_count_read");}
    f.valid|=2;f.checked|=8;
    if(!r->meshes||r->meshes>MESH_LIMIT){f.failed|=8;return RenderFail(&f,r,7,"unit_mesh_count_bounds");}
    f.attempted|=4;
    if(!Read(child->id.unit+0xD0,&r->meshEntries,8)){r->meshEntries=0;return RenderFail(&f,r,8,"unit_mesh_entries_read");}
    f.valid|=4;f.checked|=16;
    if(!Span(r->meshEntries,(size_t)r->meshes*16)){f.failed|=16;return RenderFail(&f,r,9,"unit_mesh_entries_bounds");}
    f.attempted|=8;
    if(!Read(r->meshEntries,entries,(size_t)r->meshes*16))return RenderFail(&f,r,10,"unit_mesh_array_read");
    f.valid|=8;
    for(uint32_t i=0;i<r->meshes;i++) {
        f.ordinal=i;f.resolveKind=0;memset(&resolveTrace,0,sizeof resolveTrace);
        /* Per-descriptor read bits/predicates must not inherit the prior ordinal's state. */
        f.attempted&=15;f.valid&=15;f.checked&=31;f.failed=0;
        MeshMeta *m=&r->mesh[i];unsigned char d[16],source[16];m->descriptor=entries[i*2];f.checked|=32;
        if(!Span(m->descriptor,0xDC)){f.failed|=32;return RenderFail(&f,r,11,"descriptor_span");}
        f.attempted|=16;
        if(!Read(m->descriptor,d,16))return RenderFail(&f,r,12,"descriptor_header_read");
        f.valid|=16;m->handle=U32(d+4);m->association=U64(d+8);f.attempted|=32;
        if(!Read(m->descriptor+0x90,source,16))return RenderFail(&f,r,13,"descriptor_source_read");
        f.valid|=32;m->sourceGraph=U64(source);m->node=U32(source+8);f.attempted|=64;
        if(!Read(m->descriptor+0xD8,&m->flags,4)){m->flags=0;return RenderFail(&f,r,14,"descriptor_flags_read");}
        f.valid|=64;f.checked|=0xC0;
        if(m->sourceGraph!=f.expectedSource)f.failed|=64;
        if(m->node>=r->graph.nodes)f.failed|=128;
        if(f.failed)return RenderFail(&f,r,15,"descriptor_source_or_node");
        f.checked|=256;
        if(m->association==f.association && m->handle!=0xFFFFFF) {
            m->registered=1;f.resolveKind=2;
            if(!Resolve(&r->header,m->handle,3,&m->resolved))return RenderFail(&f,r,17,renderFailure);
            f.attempted|=128;
            if(!Read(m->resolved.object+0x30,&m->ref,16)){memset(&m->ref,0,sizeof m->ref);return RenderFail(&f,r,18,"mesh_ref_read");}
            f.valid|=128;f.attempted|=256;
            if(!Read(m->resolved.object+0x70,&m->skin,8)){m->skin=0;return RenderFail(&f,r,19,"mesh_skin_pointer_read");}
            f.valid|=256;f.checked|=0x10E00;
            if(m->ref.node!=m->node)f.failed|=1u<<9;
            if(m->ref.handle!=r->graphHandle)f.failed|=1u<<10;
            if(m->ref.cache!=r->graph.object)f.failed|=1u<<11;
            if(m->skin && !Address(m->skin))f.failed|=1u<<16;
            if(f.failed)return RenderFail(&f,r,20,"mesh_normal_cache_or_node");
        } else if(m->handle!=0xFFFFFF || m->association || (m->flags&0x193)) {
            f.failed|=256;return RenderFail(&f,r,16,"descriptor_registration_state");
        }
        f.checked|=0x7000;
        for(uint32_t j=0;j<i;j++) {
            if(r->mesh[j].descriptor==m->descriptor)f.failed|=1u<<12;
            if(m->registered && r->mesh[j].registered) {
                if(r->mesh[j].handle==m->handle)f.failed|=1u<<13;
                if(r->mesh[j].resolved.object==m->resolved.object)f.failed|=1u<<14;
            }
            if(f.failed && f.conflictOrdinal==UINT32_MAX)f.conflictOrdinal=j;
        }
        if(f.failed)return RenderFail(&f,r,21,"duplicate_mesh_identity");
        if(m->registered)r->registeredMeshes++;else r->unregisteredMeshes++;
    }
    f.checked|=1u<<15;
    if(!r->registeredMeshes){f.failed|=1u<<15;return RenderFail(&f,r,22,"no_registered_mesh_evidence");}
    return 1;
}
/* Fresh owner reference supplies parent index; never infer it from Unit memory.
 * IdentityRead already repeats this exact slot+generation in each OwnerRead. */
static int AttachBegin(const Owner *owner,uint32_t index,Record *r)
{
    memset(r,0,sizeof *r);
    if(!MetaRead(&owner->manager,index,&r->child,NODE_LIMIT)||!Target(r->child.id.resource)||
       !MetaRead(&owner->manager,owner->unit.index,&r->parent,PARENT_NODE_LIMIT)||
       memcmp(&r->parent.id,&owner->unit,sizeof owner->unit)||r->child.parent!=owner->unit.unit)return RF("target_owner_parent");
    r->parentPresent=1;r->worldEqual=r->child.externalWorld==r->parent.world;
    if(!r->worldEqual)return RF("target_parent_world_mismatch");
    if(!ChainRead(r)||r->chainStatus||!r->reciprocal)return RF("target_reciprocal_chain");
    return 1;
}
static int AttachEnd(const Owner *owner,const Record *r)
{
    UnitMeta c,p;
    return (MetaRead(&owner->manager,r->child.id.index,&c,NODE_LIMIT)&&!memcmp(&c,&r->child,sizeof c)&&
        MetaRead(&owner->manager,r->parent.id.index,&p,PARENT_NODE_LIMIT)&&!memcmp(&p,&r->parent,sizeof p)) || RF("target_metadata_changed");
}
static int GroupRead(const RHeader *h,uint32_t index,GroupMeta *g)
{
    memset(g,0,sizeof *g);
    if(!Read(h->world+0x3D0,&g->header,16)||!Vector(&g->header,GROUP_LIMIT,24))return RF("group_header_bounds");
    if(index>=g->header.length)return RF("group_index_bounds");
    if(!Read(g->header.data+(uintptr_t)index*24,&g->entry,24))return RF("group_entry_unreadable");
    /* Ignore native padding bytes: they are not identity fields. */
    memset(g->entry.padding,0,sizeof g->entry.padding);
    if(!g->entry.enabled || !g->entry.count)return 1;
    if(g->entry.count>GROUP_MESH_LIMIT || !Span(g->entry.list,(size_t)g->entry.count*8)||
       !Read(g->entry.list,g->list,(size_t)g->entry.count*8))return RF("group_list_bounds_or_read");
    return 1;
}
static int ProviderRead(const RHeader *h,GroupMeta *g)
{
    unsigned char head[16];uintptr_t p=g->entry.provider;
    if(!Span(p,0x6C)||!Read(p,head,16)||!Pointer(p+0x18,&g->refs)||!Read(p+0x68,&g->nodes,4))return RF("provider_unreadable");
    g->vtable=U64(head);g->type=U32(head+8);
    if(g->vtable!=engine+0x167A478 || g->type!=13 || !g->nodes || g->nodes>256 ||
       !Span(g->refs,(size_t)g->nodes*16)||!Read(g->refs,&g->first,16)||
       !Resolve(h,g->first.handle,2,&g->graph))return RF("provider_schema_or_graph");
    return (g->first.node<g->graph.nodes && g->first.cache==g->graph.object) || RF("provider_cache_or_node");
}
static int SameSaved(const SavedTarget *saved,const Record *r,const RenderMeta *meta)
{
    RenderMeta a,b;StableRender(&a,&saved->render);StableRender(&b,meta);
    return !memcmp(&saved->child,&r->child,sizeof saved->child)&&!memcmp(&saved->parent,&r->parent,sizeof saved->parent)&&!memcmp(&a,&b,sizeof a);
}
static int SavedFail(const SavedTarget *saved,const Record *r,const RenderMeta *meta,uint32_t t)
{
    uint64_t raw=RenderDiff(&saved->render,meta),ignored=raw&(2ull|32ull|64ull|128ull|0xFF000000ull);
    uint64_t fatal=raw&~(2ull|32ull|64ull|128ull|0xFF000000ull);
    if(memcmp(&saved->child,&r->child,sizeof(UnitMeta)))fatal|=1ull<<32;
    if(memcmp(&saved->parent,&r->parent,sizeof(UnitMeta)))fatal|=1ull<<33;
    const RHeader *oh=&saved->render.header,*nh=&meta->header;
    if(fatal&(1ull<<32))return ConsFail("saved_child","saved_target_changed",t,t,&saved->child,&r->child,sizeof(UnitMeta),fatal,ignored,oh,nh,&saved->parent.id,&r->parent.id);
    if(fatal&(1ull<<33))return ConsFail("saved_parent","saved_target_changed",t,t,&saved->parent,&r->parent,sizeof(UnitMeta),fatal,ignored,oh,nh,&saved->parent.id,&r->parent.id);
    RenderMeta a,b;StableRender(&a,&saved->render);StableRender(&b,meta);
    return ConsFail("saved_render","saved_target_changed",t,t,&a,&b,sizeof a,fatal,ignored,oh,nh,&saved->parent.id,&r->parent.id);
}
static void AssociationLog(HfMod *mod,uint32_t t,const Identity *id,const Owner *owner,const RenderMeta *meta)
{
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER association seq=%u target=%u resource=%016llX unit=%llX slot=%u generation=%u owner_ref=%08X owner_unit=%llX owner_slot=%u owner_generation=%u avatar=%u network=%u player=%u descriptor_index=%u reciprocal=1 external_world_equal=1 positive=1 graph_handle=%08X graph=%llX nodes=%u meshes=%u registered_meshes=%u unregistered_meshes=%u render_association=%llX world=%llX manager=%llX queue=%llX non_atomic=1",
         recordCount,t,(unsigned long long)id->resource,(unsigned long long)id->unit,id->index,id->generation,
         owner->descriptor.reference,(unsigned long long)owner->unit.unit,owner->unit.index,owner->unit.generation,
         owner->descriptor.entity,owner->network,owner->player,owner->networkLookup.index,
         meta->graphHandle,(unsigned long long)meta->graph.object,meta->graph.nodes,meta->meshes,meta->registeredMeshes,meta->unregisteredMeshes,
         (unsigned long long)(meta->header.manager+0x10),(unsigned long long)meta->header.world,
         (unsigned long long)meta->header.manager,(unsigned long long)meta->header.queue);
}
static void MeshLog(HfMod *mod,uint32_t t,const Identity *id,const Owner *owner,const RenderMeta *meta,uint32_t ordinal)
{
    const MeshMeta *mesh=&meta->mesh[ordinal];
    if(!mesh->registered) {
        RLog(mod,"LEGO_COMPACT_LEAF_WRITER mesh target=%u ordinal=%u total=%u resource=%016llX descriptor=%llX handle=%08X node=%u owner_ref=%08X registered=0 state=sampled_unregistered descriptor_flags=%08X source_graph=%llX association=%llX expected_association=%llX non_atomic=1",
            t,ordinal,meta->meshes,(unsigned long long)id->resource,(unsigned long long)mesh->descriptor,mesh->handle,mesh->node,
            owner->descriptor.reference,mesh->flags,(unsigned long long)mesh->sourceGraph,(unsigned long long)mesh->association,
            (unsigned long long)(meta->header.manager+0x10));return;
    }
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER mesh target=%u ordinal=%u total=%u resource=%016llX descriptor=%llX handle=%08X object=%llX type=%u node=%u graph_handle=%08X normal_cache=%llX skinned=%u owner_ref=%08X registered=1 state=registered descriptor_flags=%08X source_graph=%llX association=%llX expected_association=%llX non_atomic=1",
         t,ordinal,meta->meshes,(unsigned long long)id->resource,(unsigned long long)mesh->descriptor,
         mesh->handle,(unsigned long long)mesh->resolved.object,mesh->resolved.type,mesh->node,mesh->ref.handle,
         (unsigned long long)mesh->ref.cache,mesh->skin!=0,owner->descriptor.reference,mesh->flags,
         (unsigned long long)mesh->sourceGraph,(unsigned long long)mesh->association,(unsigned long long)(meta->header.manager+0x10));
}
/* Only an actual writer capture may tolerate replacement of the already-read
 * queue. Setup/group/plan-only calls never acquire this comparison policy. */
static int CaptureQueueScope(uint32_t index,const SavedTarget *saved,const GroupMeta *group)
{
    if(!writerCaptureActive || group || !saved || !invocationBudget || !invocationBudget->call)return 0;
    const se_observe_context *ctx=invocationBudget->call;
    if(!ctx->target || !ctx->call || ctx->target->token>=56)return 0;
    unsigned t=(unsigned)(ctx->target->token/8),ordinal=(unsigned)(ctx->target->token%8);
    if(t>=7 || (!coopDynamicMode && saved!=&targets[t]) || index!=saved->child.id.index || ordinal>=MESH_LIMIT || ordinal>=saved->render.meshes)return 0;
    const MeshMeta *mesh=&saved->render.mesh[ordinal];
    return mesh->registered && ctx->call->mesh==ctx->target->mesh && ctx->target->mesh==mesh->resolved.object &&
        U64(ctx->target->identity)==saved->child.id.unit && U64(ctx->target->identity+8)==saved->child.id.resource &&
        U32(ctx->target->identity+36)==index;
}
static int CaptureRenderClose(uint32_t index,const SavedTarget *saved,const GroupMeta *group,
                              const Record *record,const RenderMeta *before,const RenderMeta *after,uint32_t target)
{
    _Static_assert(sizeof(RenderMeta)==920,"Queue policy retains the other912 bytes");
    _Static_assert(offsetof(RenderMeta,header)+offsetof(RHeader,queue)==8,"Exact queue offset");
    _Static_assert(sizeof before->header.queue==8,"Exact queue width");
    /* memcpy preserves every byte, including any unknown/padding differences. */
    RenderMeta a,b;memcpy(&a,before,sizeof a);memcpy(&b,after,sizeof b);
    uint64_t raw=RenderDiff(before,after),ignored=0;
    if(CaptureQueueScope(index,saved,group)){a.header.queue=b.header.queue=0;ignored=raw&2ull;}
    if(!memcmp(&a,&b,sizeof a))return 1;
    uint64_t fatal=raw&~ignored;if(!fatal)fatal=1ull<<63;
    return ConsFail("capture_render","render_metadata_changed",target,target,&a,&b,sizeof a,fatal,ignored,
        &before->header,&after->header,&record->parent.id,&record->parent.id);
}
static int Capture(uint32_t index,const SavedTarget *saved,Record *out,RenderMeta *meta,GroupMeta *group,uint64_t *matches,Owner *owner)
{
    Owner after;RenderMeta again;Manager closing;GroupMeta groupAfter;
    renderFailure="metadata_read_refusal";*matches=0;
    if(!OwnerRead(owner))return RF(ownerFailure);
    if(saved && !SameWriterOwner(owner,&saved->owner))return RF("writer_owner_identity_changed");
    if(!AttachBegin(owner,index,out)||!RenderRead(&out->child,meta))return 0;
    uint32_t t=0,bit=Target(out->child.id.resource);while(t<7 && (1u<<t)!=bit)t++;
    if(saved && !SameSaved(saved,out,meta))return SavedFail(saved,out,meta,t);
    if(invocationBudget && !InvocationPayload(out,meta,owner))return 0;
    if(group) {
        if(!GroupRead(&meta->header,groupCursor,group))return 0;
        if(group->entry.enabled)for(uint32_t i=0;i<meta->meshes;i++)for(uint32_t j=0;j<group->entry.count;j++)
            if(meta->mesh[i].registered && meta->mesh[i].resolved.object==group->list[j])*matches|=1ull<<(groupTarget*8+i);
        if(*matches && !ProviderRead(&meta->header,group))return 0;
        if(*matches)for(uint32_t i=0;i<meta->meshes;i++)
            if((*matches&(1ull<<(groupTarget*8+i))) && meta->mesh[i].node>=group->graph.nodes)return RF("override_mesh_node_bounds");
    }
    if(writerCaptureActive){samplerTrace.attempted=1;samplerTrace.beforeValid=1;samplerTrace.before=*meta;}
    int closingRead=RenderRead(&out->child,&again);
    if(writerCaptureActive){samplerTrace.success=(uint32_t)closingRead;samplerTrace.afterState=closingRead?2:1;samplerTrace.after=again;SamplerText(samplerTrace.underlying,sizeof samplerTrace.underlying,closingRead?"accepted":renderFailure);}
    if(!closingRead)return RF("render_metadata_changed");
    if(!CaptureRenderClose(index,saved,group,out,meta,&again,t))return 0;
    if(group) {
        if(!GroupRead(&again.header,groupCursor,&groupAfter)||(*matches && !ProviderRead(&again.header,&groupAfter)))return RF("group_metadata_changed");
        if(memcmp(group,&groupAfter,sizeof *group))return ConsFail("capture_group","group_metadata_changed",t,t,
            group,&groupAfter,sizeof *group,1,0,&meta->header,&again.header,&out->parent.id,&out->parent.id);
    }
    if(!AttachEnd(owner,out)||!OwnerRead(&after))return RF(ownerFailure&&strcmp(ownerFailure,"none")?ownerFailure:renderFailure);
    if(memcmp(owner,&after,sizeof after)||!Associated(owner,&after,out))return RF("owner_changed");
    if(!ManagerRead(&closing)||memcmp(&closing,&initial,sizeof closing))return RF("unit_manager_changed");
    if(!writerCaptureActive && armedAt && GetTickCount64()-armedAt>=ARM_MS)return RF("deadline");
    return !Stopped();
}
static void Scan(HfMod *mod)
{
    if(Stopped())return;Manager m;Record record;RenderMeta meta;Owner owner;uint64_t matches=0;
    if(!ManagerRead(&m)||memcmp(&m,&initial,sizeof m)){Finish(mod,budgetFailure?"read_budget":"unit_manager_changed");return;}
    if(renderStage==0) {
        for(uint32_t n=0;n<PER_TICK && cursor<m.capacity;n++) {
            Identity id;uint32_t index=cursor++;visited++;
            int status=IdentityRead(&m,index,&id);if(status){rejected[status]++;continue;}
            uint32_t bit=Target(id.resource);if(!bit)continue;
            if(observedMask&bit){duplicateResources++;continue;}
            if(!Capture(index,NULL,&record,&meta,NULL,&matches,&owner)){Finish(mod,budgetFailure?"read_budget":renderFailure);return;}
            if(memcmp(&id,&record.child.id,sizeof id)){Finish(mod,"candidate_changed");return;}
            uint32_t t=0;while((1u<<t)!=bit)t++;
            if(recordCount)for(uint32_t prior=0;prior<7;prior++)if(observedMask&(1u<<prior)) {
                const SavedTarget *old=&targets[prior];
                uint64_t mask=(old->render.header.manager!=meta.header.manager?1ull:0)|
                    (old->render.header.world!=meta.header.world?4ull:0)|
                    (memcmp(&old->parent.id,&record.parent.id,sizeof record.parent.id)?1ull<<33:0);
                if(mask) {
                    struct Domain {uintptr_t manager,world;Identity parent;} a={0},b={0};
                    a.manager=old->render.header.manager;a.world=old->render.header.world;a.parent=old->parent.id;
                    b.manager=meta.header.manager;b.world=meta.header.world;b.parent=record.parent.id;
                    ConsFail("cross_domain","cross_target_owner_world_changed",t,prior,&a,&b,sizeof a,mask,
                        old->render.header.queue!=meta.header.queue?2:0,&old->render.header,&meta.header,&old->parent.id,&record.parent.id);
                    Finish(mod,renderFailure);return;
                }
            }
            targets[t].child=record.child;targets[t].parent=record.parent;targets[t].render=meta;targets[t].owner=owner;
            observedMask|=bit;recordCount++;declaredMeshes+=meta.meshes;
            AssociationLog(mod,t,&id,&owner,&meta);
            if(observedMask==0x7F)renderStage=1;
            break;
        }
        if(budgetFailure)Finish(mod,"read_budget");
        else if(cursor==m.capacity && observedMask!=0x7F)Finish(mod,"discovery_incomplete");
        return;
    }
    uint32_t t=targetCursor;
    if(t>=7){Finish(mod,"stage_bounds");return;}
    if(!Capture(targets[t].child.id.index,&targets[t],&record,&meta,NULL,&matches,&owner)) {
        Finish(mod,budgetFailure?"read_budget":renderFailure);return;
    }
    if(renderStage==1) {
        if(meshCursor>=meta.meshes){Finish(mod,"mesh_cursor_bounds");return;}
        MeshLog(mod,t,&record.child.id,&owner,&meta,meshCursor);
        if(meta.mesh[meshCursor].registered)registeredRecords++;else unregisteredRecords++;
        meshRecords++;if(++meshCursor==meta.meshes){meshCursor=0;targetCursor++;}
        if(targetCursor==7) {
            targetCursor=0;renderStage=3;
        }
    } else if(renderStage==3) {
        finalMask|=1u<<t;
        if(++targetCursor==7) {
            if(meshRecords!=declaredMeshes || finalMask!=0x7F){Finish(mod,"completion_invariant");return;}
            if(registeredRecords+unregisteredRecords!=meshRecords || meshRecords!=declaredMeshes){Finish(mod,"descriptor_count_reconciliation");return;}
            if(!InvocationPublish(mod)){Finish(mod,"whitelist_publish_refusal");return;}
            renderStage=4;
        }
    } else Finish(mod,"stage_unknown");
}


