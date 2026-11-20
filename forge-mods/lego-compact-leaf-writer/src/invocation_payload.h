/* Called only while the envelope metadata gate is held. No logging or game writes. */
static int IvFail(uint32_t status,const char *reason)
{if(invocationBudget && !invocationBudget->status)invocationBudget->status=status;return RF(reason);}
static void IvValid(unsigned bit)
{unsigned char *p=invocationBudget->payload;IvU32(p,4,U32(p+4)|(1u<<bit));}
static void IvStage(uint32_t stage){IvU32(invocationBudget->payload,8,stage);}
static int InvocationRead(uintptr_t at,void *out,size_t bytes)
{
    IvBudget *b=invocationBudget;
    if(writerCaptureActive) {
        if(b->logical>=READ_TICK_LIMIT || b->sdk>=READ_TICK_LIMIT || bytes>BYTE_TICK_LIMIT-b->bytes) {
            memset(out,0,bytes);return IvFail(IV_BUDGET,"writer_per_call_budget");
        }
        b->logical++;
        if(!Address(at)||!bytes||bytes>0x0000800000000000ull-at){memset(out,0,bytes);return 0;}
        b->sdk++;b->bytes+=(uint32_t)bytes;
        int ok=hf->read(at,out,bytes)==0;if(!ok)memset(out,0,bytes);return ok;
    }
    if(b->logical>=READ_TICK_LIMIT || logicalTotal>=READ_TOTAL_LIMIT){budgetFailure=1;memset(out,0,bytes);return IvFail(IV_BUDGET,"invocation_logical_budget");}
    b->logical++;logicalTotal++;
    if(Stopped() || se_is_stopped(&invocationEnvelope) || !armedAt || GetTickCount64()-armedAt>=ARM_MS){
        memset(out,0,bytes);return IvFail(IV_STOPPED,"invocation_stopped_or_deadline");}
    if(!Address(at)||!bytes||bytes>0x0000800000000000ull-at){memset(out,0,bytes);return 0;}
    if(readTotal>=READ_TOTAL_LIMIT || bytes>BYTE_TICK_LIMIT-b->bytes || bytes>BYTE_TOTAL_LIMIT-byteTotal){
        budgetFailure=1;memset(out,0,bytes);return IvFail(IV_BUDGET,"invocation_sdk_budget");}
    b->sdk++;b->bytes+=(uint32_t)bytes;readTotal++;byteTotal+=(uint32_t)bytes;sdkReadTotal++;
    int ok=hf->read(at,out,bytes)==0;
    if(Stopped() || se_is_stopped(&invocationEnvelope)){memset(out,0,bytes);return IvFail(IV_STOPPED,"invocation_stop_after_read");}
    if(!ok){memset(out,0,bytes);return 0;}
    return 1;
}
static int IvGraph(uintptr_t graph,unsigned char *header,unsigned validBit,unsigned maximum,int parent,uint32_t meshNode)
{
    if(!Span(graph,56)||!Read(graph,header,56))return IvFail(parent?IV_PARENT_HEADER:IV_SELECTED_HEADER,"invocation_graph_header_read");
    IvValid(validBit);
    uint32_t now=U32(header+0x10),previous=U32(header+0x28);
    if(U64(header)!=engine+0x16880B0 || U32(header+8)!=2 || !now || now>maximum || !previous || previous>maximum ||
       (parent && (now!=IV_PARENT_NODES || previous!=IV_PARENT_NODES)) ||
       (!parent && (meshNode>=now || meshNode>=previous)) ||
       !Span(U64(header+0x18),(size_t)now*48) || !Span(U64(header+0x30),(size_t)previous*48))
        return IvFail(parent?IV_PARENT_HEADER:IV_SELECTED_HEADER,"invocation_graph_header_bounds");
    return 1;
}
static int IvParent(const Record *r,const RenderMeta *meta,unsigned char *app,Resolved *graph,unsigned appBit,unsigned graphBit)
{
    if(r->parent.nodes!=IV_PARENT_NODES || !Span(r->parent.id.unit,0x70) || !Read(r->parent.id.unit+0x60,app,16))
        return IvFail(IV_PARENT_BINDING,"invocation_parent_app_read");
    IvValid(appBit);
    if(U64(app+8)!=meta->header.manager+0x10 || !Resolve(&meta->header,U32(app+4),2,graph) || graph->nodes!=IV_PARENT_NODES)
        return IvFail(IV_PARENT_BINDING,"invocation_parent_graph_binding");
    IvValid(graphBit);return 1;
}
static int IvMeshSame(const unsigned char *a,const unsigned char *b)
{
    /* Only fields actually consumed by512B80. C4 and other raw context are not identity. */
    return U64(a)==U64(b) && U64(a+16)==U64(b+16) && U32(a+32)==U32(b+32) &&
           U64(a+40)==U64(b+40) && U32(a+88)==U32(b+88);
}
static int InvocationPayload(const Record *r,const RenderMeta *meta,const Owner *owner)
{
    IvBudget *budget=invocationBudget;unsigned char *p=budget->payload;const se_call *call=budget->call->call;
    uint32_t token=(uint32_t)budget->call->target->token,target=token/8,ordinal=token%8;
    IvValid(0);
    uint32_t expectedTarget=0,maxTargets=coopDynamicMode?cm_garment_count():7u;
    if(target>=maxTargets || ordinal>=meta->meshes ||
       (coopDynamicMode ? (!cm_garment_find(r->child.id.resource,&expectedTarget) || expectedTarget!=target)
                        : (Target(r->child.id.resource)!=(1u<<target))))return IvFail(IV_WHITELIST,"invocation_target_token");
    const MeshMeta *m=&meta->mesh[ordinal];
    if(!m->registered || m->resolved.object!=call->mesh || m->resolved.object!=budget->call->target->mesh || !m->skin)
        return IvFail(IV_WHITELIST,"invocation_mesh_binding");
    uintptr_t selected=call->optional_graph?call->optional_graph:m->ref.cache;
    IvU32(p,48,owner->descriptor.reference);IvU32(p,52,owner->unit.index);IvU32(p,56,owner->unit.generation);IvU32(p,60,r->child.nodes);
    IvU64(p,64,owner->unit.unit);IvU64(p,72,owner->unit.resource);IvU64(p,80,r->child.id.unit);IvU64(p,88,r->child.id.resource);
    IvU64(p,96,m->descriptor);IvU32(p,104,m->handle);IvU32(p,108,m->resolved.index);IvU32(p,112,m->node);IvU32(p,116,m->ref.handle);IvU64(p,120,m->ref.cache);
    IvU64(p,128,meta->header.manager);IvU64(p,136,meta->header.queue);IvU64(p,144,meta->header.world);IvU64(p,152,selected);
    IvU32(p,176,r->parent.nodes);IvU32(p,180,IV_PARENT_NODES);
    Resolved parent={0},parentAfter={0};
    IvStage(IV_PARENT_BEFORE);if(!IvParent(r,meta,p+704,&parent,1,2))return 0;
    IvU64(p,160,parent.object);IvU32(p,168,parent.handle);IvU32(p,172,parent.index);
    IvStage(IV_SELECTED_BEFORE);if(!IvGraph(selected,p+256,3,IV_SELECTED_NODES,0,m->node))return 0;
    IvStage(IV_PARENT_GRAPH_BEFORE);if(!IvGraph(parent.object,p+368,4,IV_PARENT_NODES,1,0))return 0;
    uint32_t counts[4]={U32(p+272),U32(p+296),U32(p+384),U32(p+408)};
    uintptr_t arrays[4]={U64(p+280),U64(p+304),U64(p+392),U64(p+416)};
    for(unsigned i=0;i<4;i++){IvU32(p,184+4*i,counts[i]);IvU64(p,200+8*i,arrays[i]);}
    IvStage(IV_MESH_BEFORE);
    if(!Span(call->mesh,0xD0)||!Read(call->mesh+0x70,p+480,96))return IvFail(IV_MESH_CONTEXT,"invocation_mesh_context_read");IvValid(5);
    if(U64(p+480)!=m->skin || !Span(m->skin,16) || (U64(p+496)&&!Span(U64(p+496),48)))return IvFail(IV_MESH_CONTEXT,"invocation_mesh_context_binding");
    IvStage(IV_POOL_BEFORE);
    if(call->pool!=meta->header.world+0x318 || !Span(call->pool,0x180) || !Read(call->pool+0x170,p+672,16))return IvFail(IV_POOL_CONTEXT,"invocation_pool_binding_read");IvValid(6);
    size_t outputBytes=(size_t)U32(p+568)*48; /* Metadata span only; no output payload read. */
    if(!Span(U64(p+672),outputBytes?outputBytes:1))return IvFail(IV_POOL_CONTEXT,"invocation_pool_output_span");
    for(unsigned i=0;i<8;i++) {
        IvStage(IV_ARRAY_0+i);size_t bytes=(size_t)counts[i%4]*48;
        if(bytes>iv_array_capacity[i] || !Read(arrays[i%4],p+iv_array_offsets[i],bytes))return IvFail(IV_POSE_READ,"invocation_pose_array_read");
        IvValid(8+i);
    }
    IvStage(IV_SELECTED_AFTER);if(!IvGraph(selected,p+312,16,IV_SELECTED_NODES,0,m->node))return 0;
    IvStage(IV_PARENT_GRAPH_AFTER);if(!IvGraph(parent.object,p+424,17,IV_PARENT_NODES,1,0))return 0;
    IvStage(IV_MESH_AFTER);if(!Read(call->mesh+0x70,p+576,96))return IvFail(IV_MESH_CONTEXT,"invocation_mesh_context_after_read");IvValid(18);
    IvStage(IV_POOL_AFTER);if(!Read(call->pool+0x170,p+688,16))return IvFail(IV_POOL_CONTEXT,"invocation_pool_after_read");IvValid(19);
    IvStage(IV_PARENT_AFTER);if(!IvParent(r,meta,p+720,&parentAfter,20,21))return 0;
    uint32_t flags=0;
    if(!memcmp(p+1024,p+23584,6144)&&!memcmp(p+7168,p+29728,6144))flags|=1;
    if(!memcmp(p+13312,p+35872,5136)&&!memcmp(p+18448,p+41008,5136))flags|=2;
    if(!memcmp(p+256,p+312,56)&&!memcmp(p+368,p+424,56))flags|=4;
    if(IvMeshSame(p+480,p+576))flags|=8;
    if(U64(p+672)==U64(p+688))flags|=16;
    if(!memcmp(p+704,p+720,16)&&!memcmp(&parent,&parentAfter,sizeof parent))flags|=32;
    IvU32(p,12,flags);
    if((flags&3)!=3)return IvFail(IV_POSE_CHANGED,"invocation_pose_changed");
    if(flags!=63)return IvFail(IV_HEADER_CHANGED,"invocation_context_changed");
    IvStage(IV_OUTER_AFTER);return 1;
}
