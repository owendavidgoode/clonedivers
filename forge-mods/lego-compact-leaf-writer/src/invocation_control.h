/* Static state remains resident through session end and late delegated returns. */
static int invocationSetupGate;
static char invocationDeferred[8][900];static unsigned invocationDeferredCount;
static void InvocationQueueLog(HfMod *mod,const char *text)
{
    if(invocationSetupGate) {
        if(invocationDeferredCount<8){size_t n=strlen(text);if(n>=900)n=899;memcpy(invocationDeferred[invocationDeferredCount],text,n);invocationDeferred[invocationDeferredCount++][n]=0;}
        else {InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);}
    } else hf->log(mod,"%s",text);
}
static void InvocationFlush(HfMod *mod)
{
    for(unsigned i=0;i<invocationDeferredCount;i++)hf->log(mod,"%s",invocationDeferred[i]);
    invocationDeferredCount=0;
}
static void InvocationClearFailures(void)
{
    memset(&schemaFailure,0,sizeof schemaFailure);memset(&routeFailure,0,sizeof routeFailure);
    memset(&renderRefusal,0,sizeof renderRefusal);memset(&consistencyRefusal,0,sizeof consistencyRefusal);
    memset(&resolveTrace,0,sizeof resolveTrace);renderFailure="none";budgetFailure=0;
}
static void ShadowCaptured(const se_observe_context *ctx,int32_t captureStatus)
{
    sm_result *math=(sm_result*)(ctx->payload+IV_PAYLOAD_BYTES);
    const sm_call mathCall={ctx->call->mesh,ctx->call->pool,ctx->call->optional_graph,
        ctx->call->caller,engine,U64(ctx->target->identity+8),(uint32_t)(ctx->target->token%8),ctx->call->entry_mxcsr};
    sm_evaluate(&shadowRig,&mathCall,ctx->payload,captureStatus?0:IV_PAYLOAD_BYTES,&shadowWork,math);
}
static void InvocationObserve(const se_observe_context *ctx,se_observation_result *result,const void *user)
{
    (void)user;
    if(writerCaptureActive)SamplerReset();
    if(ctx->capacity<IV_PAYLOAD_BYTES){result->status=IV_BUDGET;result->used=0;return;}
    unsigned char *p=ctx->payload;memset(p,0,IV_PAYLOAD_BYTES);result->used=IV_PAYLOAD_BYTES;
    IvU32(p,0,IV_VERSION);IvU32(p,8,IV_OUTER_BEFORE);IvU64(p,16,GetTickCount64());IvU32(p,44,updateSerial);
    IvBudget budget={0};budget.payload=p;budget.call=ctx;
    uint64_t token=ctx->target->token;unsigned t=(unsigned)(token/8),ordinal=(unsigned)(token%8);
    InvocationClearFailures();invocationBudget=&budget;
    Record record;RenderMeta meta;Owner owner;uint64_t matches=0;
    int ok=0;
    if(coopDynamicMode) {
        ok=token<56 && t<7 && ordinal>=1 && ordinal<=4 && CoopCaptureTarget(ctx,&record,&meta,&owner);
    } else {
        ok=token<56 && t<7 && ordinal<targets[t].render.meshes &&
            ctx->target->mesh==targets[t].render.mesh[ordinal].resolved.object &&
            Capture(targets[t].child.id.index,&targets[t],&record,&meta,NULL,&matches,&owner);
    }
    if(ok){IvValid(22);IvU32(p,12,U32(p+12)|64);}
    if(!ok && !budget.status)budget.status=IV_BINDING;
    if(ok && (U32(p+4)!=IV_VALID_COMPLETE || U32(p+12)!=IV_FLAGS_COMPLETE))budget.status=IV_BINDING;
    IvU64(p,24,GetTickCount64());IvU32(p,32,budget.logical);IvU32(p,36,budget.sdk);IvU32(p,40,budget.bytes);
    /* Readable constant reason text is copied; no pointer is exposed in the file. */
    const char *reason=ok?"accepted":((int32_t)budget.status==IV_RETIRED?"retired_mid_call":renderFailure);
    if(reason){size_t n=strlen(reason);if(n>63)n=63;memcpy(p+744,reason,n);}
    IvU32(p,832,logicalTotal);IvU32(p,836,sdkReadTotal);IvU32(p,840,byteTotal);
    result->status=(int32_t)budget.status;
    if(writerCaptureActive && (int32_t)budget.status!=IV_RETIRED)SamplerSnapshot((int32_t)budget.status);
    invocationBudget=NULL;
    if(!writerCaptureActive)ShadowCaptured(ctx,(int32_t)budget.status);
    /* Math refusal is reported with the raw record at drain. No game call is
       suppressed; terminal completeness fails and admission then stops. */
    /* Byte/header repeatability refusals consume an attempt but permit a later
       independently bracketed attempt. Identity/bounds/read failures stop admission. */
    if(!writerCaptureActive && budget.status && budget.status!=IV_POSE_CHANGED && budget.status!=IV_HEADER_CHANGED && budget.status!=IV_STOPPED && (int32_t)budget.status!=IV_RETIRED) {
        InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);
    }
    InvocationClearFailures();
}
static __declspec(noinline) uint32_t SkinObserved(void *mesh,void *pool,void *optionalGraph)
{
    uintptr_t caller=(uintptr_t)__builtin_return_address(0);
    return gw_dispatch(&writer,mesh,pool,optionalGraph,caller);
}
static __declspec(noinline) uint32_t LeafObserved(void *dst,const void *factor,const void *graph,const void *bind,const uint32_t *palette,const uint32_t *map,uint32_t count)
{
    uintptr_t caller=(uintptr_t)__builtin_return_address(0);
    return gw_leaf_dispatch(&writer,dst,factor,graph,bind,palette,map,count,caller);
}
static __declspec(noinline) uint32_t RenderRemoveObserved(void *world,const void *packet,void *a3)
{
    uint32_t entry_csr=_mm_getcsr();
    DWORD entry_error=GetLastError();
    uint32_t handle=0;
    uintptr_t pkt=(uintptr_t)packet;
    if(hf && Address(pkt) && pkt<=0x0000800000000000ull-8u && hf->read(pkt+4u,&handle,4)==0) {
        if(handle && handle!=0xFFFFFFu) gw_retire_handle(&writer,(uintptr_t)world,handle);
    }
    gw_remove_fn next=(gw_remove_fn)InterlockedCompareExchangePointer(&writer.remove_next,NULL,NULL);
    SetLastError(entry_error);
    _mm_setcsr(entry_csr);
    return next ? next(world,packet,a3) : 0u;
}
static int InvocationPublish(HfMod *mod)
{
    static const uint32_t expectedCounts[7]={21,21,43,21,43,17,29};
    static const uint32_t meshBases[7]={14,12,15,12,15,12,17};
    if(observedMask!=0x7F || finalMask!=0x7F || recordCount!=7 || meshRecords!=declaredMeshes ||
       registeredRecords+unregisteredRecords!=meshRecords || !registeredRecords || registeredRecords>56)return 0;
    for(unsigned t=0;t<7;t++) {
        if(!SameWriterOwner(&targets[0].owner,&targets[t].owner))return 0;
        if(targets[t].render.meshes!=5 || targets[t].render.graph.nodes!=expectedCounts[t] || targets[t].render.mesh[0].registered)return 0;
        for(unsigned j=1;j<=4;j++)if(!targets[t].render.mesh[j].registered || !targets[t].render.mesh[j].skin ||
            targets[t].render.mesh[j].node!=meshBases[t]+j-1 || !targets[t].render.mesh[j].ref.cache)return 0;
    }
    unsigned count=0;
    memset(invocationWhitelist,0,sizeof invocationWhitelist);
    for(unsigned t=0;t<7;t++)for(unsigned j=0;j<targets[t].render.meshes;j++) {
        const MeshMeta *m=&targets[t].render.mesh[j];if(!m->registered)continue;
        if(count>=SE_TARGETS)return 0;
        for(unsigned k=0;k<count;k++)if(invocationWhitelist[k].mesh==m->resolved.object)return 0;
        se_target *row=&invocationWhitelist[count++];row->mesh=m->resolved.object;row->token=t*8+j;
        IvU64(row->identity,0,targets[t].child.id.unit);IvU64(row->identity,8,targets[t].child.id.resource);
        IvU64(row->identity,16,targets[t].parent.id.unit);IvU64(row->identity,24,m->descriptor);
        IvU32(row->identity,32,m->handle);IvU32(row->identity,36,targets[t].child.id.index);IvU32(row->identity,40,targets[t].child.id.generation);
        IvU32(row->identity,44,targets[t].parent.id.index);IvU32(row->identity,48,targets[t].parent.id.generation);
        IvU64(row->identity,56,m->ref.cache);
    }
    if(count!=28 || registeredRecords!=28 || unregisteredRecords!=7 || meshRecords!=35 ||
       count!=registeredRecords || se_publish(&invocationEnvelope,invocationWhitelist,count)!=SE_OK)return 0;
    invocationWhitelistCount=count;
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER whitelist_ready expected_mask=7F observed_mask=7F associated_mask=7F final_mask=7F records=7 meshes=%u declared_meshes=%u registered_meshes=%u unregistered_meshes=%u whitelist_objects=%u group_inventory=not_collected coverage=sampled_eligibility_only atomic_frame=unproven",
        meshRecords,declaredMeshes,registeredRecords,unregisteredRecords,count);
    return !Stopped();
}
static void InvocationHeader(unsigned char *out,const se_record *r)
{
    memset(out,0,IV_HEADER_BYTES);memcpy(out,"LGSKIN01",8);
    uint32_t t=(uint32_t)(r->target.token/8),ordinal=(uint32_t)(r->target.token%8);
    IvU32(out,8,1);IvU32(out,12,IV_HEADER_BYTES);IvU32(out,16,IV_FILE_BYTES);IvU32(out,20,IV_PAYLOAD_BYTES);
    IvU32(out,24,(uint32_t)r->capture_status);IvU32(out,28,t);IvU32(out,32,ordinal);IvU32(out,36,r->call.thread_id);IvU32(out,40,invocationPid);
    IvU64(out,48,invocationBirth);IvU64(out,56,r->sequence);IvU64(out,64,U64(r->target.identity+8));
    IvU64(out,72,r->call.mesh);IvU64(out,80,r->call.pool);IvU64(out,88,r->call.optional_graph);IvU64(out,96,r->call.caller);
    IvU32(out,104,r->result);IvU32(out,108,r->call.entry_mxcsr);IvU32(out,112,r->return_mxcsr);
    IvU32(out,116,r->call.entry_last_error);IvU32(out,120,r->return_last_error);IvU32(out,124,r->abandoned);
    IvU64(out,128,r->active_at_entry);IvU64(out,136,r->overlap_epoch_before);IvU64(out,144,r->overlap_epoch_return);
}
static int InvocationDirectoryInit(const wchar_t *directory)
{
    if(wcslen(directory)>=1100)return 0;
    HANDLE h=CreateFileW(directory,FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,NULL,OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(h==INVALID_HANDLE_VALUE)return 0;
    BY_HANDLE_FILE_INFORMATION info;DWORD n=GetFinalPathNameByHandleW(h,invocationDirectoryFinal,1100,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
    if(!GetFileInformationByHandle(h,&info)||!(info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)||
       (info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)||!n||n>=1100){CloseHandle(h);return 0;}
    while(n && invocationDirectoryFinal[n-1]==L'\\')invocationDirectoryFinal[--n]=0;
    wcscpy(invocationDirectory,directory);invocationDirectoryHandle=h;return 1;
}
static int InvocationWrite(const se_record *r,char name[64])
{
    if(r->sequence<1||r->sequence>IV_ATTEMPTS||r->payload_used!=IV_PAYLOAD_BYTES||r->target.token>=56)return 0;
    wchar_t basename[64],path[1200],expected[1200],actual[1200],directoryNow[1100];
    int count=snprintf(name,64,"skin-invocation-%06u.bin",(unsigned)r->sequence);
    if(count<=0||count>=64||swprintf(basename,64,L"skin-invocation-%06u.bin",(unsigned)r->sequence)<=0)return 0;
    DWORD n=GetFinalPathNameByHandleW(invocationDirectoryHandle,directoryNow,1100,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
    if(!n||n>=1100)return 0;while(n&&directoryNow[n-1]==L'\\')directoryNow[--n]=0;
    if(wcscmp(directoryNow,invocationDirectoryFinal)||swprintf(path,1200,L"%ls\\%ls",invocationDirectory,basename)<0||
       swprintf(expected,1200,L"%ls\\%ls",invocationDirectoryFinal,basename)<0)return 0;
    HANDLE f=CreateFileW(path,GENERIC_WRITE|FILE_READ_ATTRIBUTES,FILE_SHARE_READ,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(f==INVALID_HANDLE_VALUE)return 0;
    BY_HANDLE_FILE_INFORMATION info;n=GetFinalPathNameByHandleW(f,actual,1200,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
    int ok=n && n<1200 && !wcscmp(actual,expected) && GetFileInformationByHandle(f,&info) &&
        !(info.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) &&
        info.nNumberOfLinks==1 && !info.nFileSizeHigh && !info.nFileSizeLow;
    unsigned char header[IV_HEADER_BYTES];InvocationHeader(header,r);DWORD written=0;
    if(ok)ok=WriteFile(f,header,IV_HEADER_BYTES,&written,NULL)&&written==IV_HEADER_BYTES;
    if(ok)ok=WriteFile(f,r->payload,IV_PAYLOAD_BYTES,&written,NULL)&&written==IV_PAYLOAD_BYTES;
    LARGE_INTEGER size;
    if(ok)ok=GetFileSizeEx(f,&size)&&size.QuadPart==IV_FILE_BYTES;
    int closed=CloseHandle(f)!=0;return ok&&closed;
}
static unsigned InvocationPending(void)
{
    unsigned count=0;
    for(unsigned i=0;i<SE_SLOTS;i++)if(InterlockedCompareExchange(&invocationEnvelope.slots[i].state,0,0)!=SE_FREE)count++;
    return count;
}
static int InvocationDrainOne(HfMod *mod)
{
    se_view view={0};if(!se_try_take(&invocationEnvelope,&view))return 1;
    const se_record *r=view.record;char name[64];
    if(!InvocationWrite(r,name)){InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);return 0;}
    unsigned t=(unsigned)(r->target.token/8),ordinal=(unsigned)(r->target.token%8);
    int accepted=r->capture_status==IV_OK && r->result!=UINT32_MAX && !r->abandoned &&
        U32(r->payload+4)==IV_VALID_COMPLETE && U32(r->payload+12)==IV_FLAGS_COMPLETE;
    invocationFiles++;if(accepted){invocationAccepted++;invocationMask|=1u<<t;}else invocationRefused++;
    if(r->abandoned)invocationAbandoned++;
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER record seq=%llu file=%s bytes=%u status=%d target=%u ordinal=%u resource=%016llX mesh=%llX pool=%llX optional_graph=%llX caller=%llX result=%08X entry_mxcsr=%08X return_mxcsr=%08X abandoned=%u accepted=%u non_atomic=1 sample=pre_delegation",
        (unsigned long long)r->sequence,name,IV_FILE_BYTES,r->capture_status,t,ordinal,(unsigned long long)U64(r->target.identity+8),
        (unsigned long long)r->call.mesh,(unsigned long long)r->call.pool,(unsigned long long)r->call.optional_graph,(unsigned long long)r->call.caller,
        r->result,r->call.entry_mxcsr,r->return_mxcsr,r->abandoned,accepted);
    const sm_result *math=(const sm_result*)(r->payload+IV_PAYLOAD_BYTES);
    const gw_receipt *leaf=(const gw_receipt*)(r->payload+GW_RECEIPT_OFFSET);
    int restored=math->restored_mxcsr==r->call.entry_mxcsr && r->delegate_mxcsr==r->call.entry_mxcsr;
    int mathOk=accepted && math->math_ok && restored && math->reserved[2]==1;
    shadowRecords++;if(mathOk)shadowOk++;else shadowFail++;
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER math seq=%llu target=%u ordinal=%u resource=%016llX shadow=0 current_status=%u previous_status=%u protected_rows=%u mesh_rows=%u header_ok=%u math_mxcsr=%u entry_mxcsr=%u restored_mxcsr=%u delegate_mxcsr=%u restored_before_delegate=%u math_ok=%u outer_graph=%llX leaf_substituted=%u",
        (unsigned long long)r->sequence,t,ordinal,(unsigned long long)U64(r->target.identity+8),
        math->current_status,math->previous_status,math->protected_rows,math->mesh_rows,math->header_ok,
        math->math_mxcsr,r->call.entry_mxcsr,math->restored_mxcsr,r->delegate_mxcsr,restored,mathOk,
        (unsigned long long)U64(math->reserved),math->reserved[2]);
    if(!mathOk){InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);}
    int leafSuccess=mathOk && !leaf->failure && leaf->expected && leaf->seen==leaf->expected && leaf->replaced==leaf->expected &&
        leaf->outer_args_unchanged && leaf->environment_ok && leaf->recheck_ok && leaf->allocation_ok &&
        leaf->route==(r->call.optional_graph?2u:1u);
    if(leafSuccess){leafOk++;if(leaf->route==1)leafNormalMask|=1u<<t;else leafGroupedMask|=1u<<t;}else leafFail++;
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER leaf seq=%llu target=%u ordinal=%u route=%u expected=%u seen=%u replaced=%u failure=%u original_outer_args=%u environment_ok=%u plan_blocks=%u plan_bytes=%u recheck_ok=%u allocation_ok=%u leaf_ok=%u",
        (unsigned long long)r->sequence,t,ordinal,leaf->route,leaf->expected,leaf->seen,leaf->replaced,leaf->failure,
        leaf->outer_args_unchanged,leaf->environment_ok,leaf->plan_blocks,leaf->plan_bytes,leaf->recheck_ok,leaf->allocation_ok,leafSuccess);
    if(!leafSuccess){InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);}
    if(!se_release(&invocationEnvelope,&view)){InterlockedExchange(&invocationFatal,1);se_stop(&invocationEnvelope);return 0;}
    if(coopDynamicMode && (logsTotal>=LOG_LIMIT-16u || invocationFiles>=128u))
        InterlockedExchange(&writer.diagnostics_closed,1);
    return !Stopped();
}
static void InvocationTerminalLog(HfMod *mod,unsigned attempts,unsigned pending,const char *reason,int complete)
{
    for(unsigned row=0;row<(complete?2u:1u);row++)
    RLog(mod,"LEGO_COMPACT_LEAF_WRITER %s phase=invocation reason=%s expected_mask=7F accepted_mask=%02X accepted_records=%u refused_records=%u abandoned_records=%u record_files=%u attempted=%u pending_slots=%u whitelist_objects=%u group_inventory=not_collected object_coverage=sampled_only atomic_frame=unproven partial=%u shadow=1 math_records=%u math_ok=%u math_fail=%u",
        complete&&!row?"complete":"stopped",reason,invocationMask,invocationAccepted,invocationRefused,invocationAbandoned,
        invocationFiles,attempts,pending,invocationWhitelistCount,!complete,shadowRecords,shadowOk,shadowFail);
    se_stop(&invocationEnvelope);InterlockedExchange(&invocationTerminal,1);InterlockedExchange(&stopped,1);
}
