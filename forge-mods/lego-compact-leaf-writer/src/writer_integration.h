static volatile LONG writerDiagnosticAttempts[SE_TARGETS];
static volatile LONG writerDiagnosticRoutes[SE_TARGETS];
static void *volatile writerDiagnosticMeshes[SE_TARGETS];
static volatile LONG64 writerDiagnosticSequence,writerDiagnosticDropped;
static volatile LONG writerDiagnosticProducers;
static int WriterPlanRead(uintptr_t at,void *out,size_t bytes,void *user){(void)user;return InvocationRead(at,out,bytes);}
static int WriterPlan(lp_plan *plan,se_record *record,int recheck,void *user){
    (void)user;unsigned char *p=record->payload;IvBudget budget={0};
    budget.payload=p;budget.logical=U32(p+32);budget.sdk=U32(p+36);budget.bytes=U32(p+40);
    if(budget.logical>READ_TICK_LIMIT||budget.sdk>READ_TICK_LIMIT||budget.bytes>BYTE_TICK_LIMIT)return 0;
    invocationBudget=&budget;writerCaptureActive=1;
    int ok=recheck?lp_recheck(plan,WriterPlanRead,NULL):lp_capture(plan,p,record->call.mesh,record->call.pool,U64(record->target.identity+8),(unsigned)(record->target.token%8),WriterPlanRead,NULL);
    writerCaptureActive=0;invocationBudget=NULL;
    IvU32(p,32,budget.logical);IvU32(p,36,budget.sdk);IvU32(p,40,budget.bytes);
    InvocationClearFailures();return ok && !budget.status;
}
static void WriterCapture(const se_observe_context *ctx,se_observation_result *result,const void *user) {
    /* gw_dispatch already owns the metadata SRW lock; no math inside it. */
    writerCaptureActive=1;InvocationObserve(ctx,result,user);writerCaptureActive=0;
}
static unsigned WriterDiagnosticTargetSlot(uintptr_t mesh) {
    if(invocationEnvelope.published==1) {
        for(unsigned i=0;i<invocationEnvelope.target_count;i++)
            if(invocationEnvelope.targets[i].mesh==mesh)return i;
        return SE_TARGETS;
    }
    if(!mesh)return SE_TARGETS;
    for(unsigned i=0;i<SE_TARGETS;i++) {
        void *cur=InterlockedCompareExchangePointer(&writerDiagnosticMeshes[i],NULL,NULL);
        if(cur==(void*)mesh)return i;
        if(!cur) {
            void *prev=InterlockedCompareExchangePointer(&writerDiagnosticMeshes[i],(void*)mesh,NULL);
            if(!prev || prev==(void*)mesh)return i;
        }
    }
    return SE_TARGETS;
}
static void WriterCompletedBody(const se_record *record,void *user) {
    (void)user;
    if(InterlockedCompareExchange(&writer.diagnostics_closed,0,0))return;
    unsigned target=WriterDiagnosticTargetSlot(record->target.mesh);
    if(target==SE_TARGETS){InterlockedExchange(&writer.diagnostics_closed,1);return;}
    LONG route=record->call.optional_graph?2:1;
    if(InterlockedCompareExchange(&writerDiagnosticRoutes[target],0,0)&route)return;
    if(InterlockedCompareExchange(&writerDiagnosticAttempts[target],0,0)>=2)return;
    se_slot *slot=NULL;
    for(unsigned i=0;i<SE_SLOTS;i++)if(InterlockedCompareExchange(&invocationEnvelope.slots[i].state,SE_WRITING,SE_FREE)==SE_FREE){slot=&invocationEnvelope.slots[i];break;}
    if(!slot){InterlockedIncrement64(&writerDiagnosticDropped);return;}
    LONG prior=InterlockedCompareExchange(&writerDiagnosticRoutes[target],0,0);
    for(;;){
        if(prior&route){InterlockedExchange(&slot->state,SE_FREE);return;}
        LONG observed=InterlockedCompareExchange(&writerDiagnosticRoutes[target],prior|route,prior);
        if(observed==prior)break;prior=observed;
    }
    LONG attempt=InterlockedIncrement(&writerDiagnosticAttempts[target]);
    if(attempt>2){InterlockedDecrement(&writerDiagnosticAttempts[target]);InterlockedExchange(&slot->state,SE_FREE);return;}
    LONG64 seq=InterlockedIncrement64(&writerDiagnosticSequence);
    if(seq>(LONG64)IV_ATTEMPTS) {
        /* Cap live diagnostic file volume cleanly without flagging fatal on active correction. */
        if(invocationEnvelope.published==1)gw_fail(&writer,GW_COUNTER,(LONG64)record->sequence);
        InterlockedExchange(&writer.diagnostics_closed,1);
        InterlockedExchange(&slot->state,SE_FREE);
        return;
    }
    memcpy(&slot->record,record,sizeof *record);
    slot->record.sequence=(uint64_t)seq;
    /* Private addresses in copied tail are historical receipt values only. */
    InterlockedExchange(&slot->state,SE_READY);
}
static void WriterCompleted(const se_record *record,void *user) {
    /* Enter before checking closed: completion cannot race an unpublished sample. */
    InterlockedIncrement(&writerDiagnosticProducers);
    WriterCompletedBody(record,user);
    InterlockedDecrement(&writerDiagnosticProducers);
}
