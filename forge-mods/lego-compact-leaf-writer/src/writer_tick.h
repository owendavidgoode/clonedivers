static uint64_t writerPollAt,writerHealthAt,writerDrainAt;
static unsigned writerHealthRows,writerDiagnosticsDone;static LONG writerReportedFatal;
static uint32_t writerLastAdmissions,writerLastRetires,writerLastAba,writerLastPlayers;
static LONG64 WriterCounter(volatile LONG64 *value){return InterlockedCompareExchange64(value,0,0);}
static void WriterTick(HfMod *mod,float dt,void *context) {
    if(!InterlockedCompareExchange(&writer.ready,0,0)){
        if(coopDynamicMode) {
            if(InterlockedCompareExchange(&tickLock,1,0))return;
            if(Stopped()){InterlockedExchange(&tickLock,0);return;}
            if(RequestFile(stopPath)){Finish(mod,"request");InterlockedExchange(&tickLock,0);return;}
            readTick=byteTick=logsTick=logicalTick=0;
            coopProbeActive=1;coopProbeReads=coopProbeBytes=0;
            Manager m;int hasMgr=CoopManagerRead(&m);
            coopProbeActive=0;
            if(!hasMgr){InterlockedExchange(&tickLock,0);return;}
            InterlockedExchange64(&writer.unit_cap,(LONG64)m.capacity);
            initial=m;
            if(!writer.approved_next || InterlockedCompareExchangePointer(&invocationEnvelope.next,NULL,NULL)!=writer.approved_next ||
               !writer.approved_leaf_next || InterlockedCompareExchangePointer(&writer.leaf_next,NULL,NULL)!=writer.approved_leaf_next ||
               !writer.approved_remove_next || InterlockedCompareExchangePointer(&writer.remove_next,NULL,NULL)!=writer.approved_remove_next){
                gw_fail(&writer,GW_CHAIN,0);WriterFaultTick(mod);Finish(mod,"chain_changed_before_ready");
                InterlockedExchange(&tickLock,0);return;
            }
            AcquireSRWLockExclusive(&writer.metadata);
            coopProbeActive=1;coopProbeReads=coopProbeBytes=0;
            coop_sync_roster_if_changed(&initial,1);
            coop_discover_player_garments(&initial);
            coop_refresh_units(&initial,0);
            InterlockedExchange64(&writer.garments_cached,(LONG64)coopGarmentCount);
            coopProbeActive=0;
            uint32_t pc=coopReg.player_count;
            ReleaseSRWLockExclusive(&writer.metadata);
            if(!coopDiagnosticCapture) {
                InterlockedExchange(&writer.diagnostics_closed,1);
                writerDiagnosticsDone=1;
            }
            InterlockedExchange(&writer.ready,1);
            hf->log(mod,"LEGO_COMPACT_LEAF_WRITER writer_ready grouped=1 normal=1 coop=1 dynamic=1 max_targets=%u warmup=unqualified policy=neck_and_arm_shoulder_hinge approved_next=%llX approved_leaf_next=%llX approved_remove_next=%llX players=%u diag_capture=%u",
                COOP_MAX_TARGETS,(unsigned long long)(uintptr_t)writer.approved_next,(unsigned long long)(uintptr_t)writer.approved_leaf_next,(unsigned long long)(uintptr_t)writer.approved_remove_next,pc,(unsigned)coopDiagnosticCapture);
            InterlockedExchange(&tickLock,0);
            return;
        }
        Tick(mod,dt,context);
        /* Freeze all setup globals before making concurrent Capture reachable. */
        if(renderStage==4 && !Stopped() && InterlockedCompareExchange(&invocationEnvelope.published,0,0)==1) {
            if(!writer.approved_next || InterlockedCompareExchangePointer(&invocationEnvelope.next,NULL,NULL)!=writer.approved_next ||
               !writer.approved_leaf_next || InterlockedCompareExchangePointer(&writer.leaf_next,NULL,NULL)!=writer.approved_leaf_next){
                gw_fail(&writer,GW_CHAIN,0);WriterFaultTick(mod);Finish(mod,"chain_changed_before_ready");return;
            }
            InterlockedExchange(&writer.ready,1);
            hf->log(mod,"LEGO_COMPACT_LEAF_WRITER writer_ready grouped=1 normal=1 correction_active=1 slots=32 eligible_objects=28 expected_mask=7F warmup=unqualified policy=neck_and_arm_shoulder_hinge approved_next=%llX approved_leaf_next=%llX",(unsigned long long)(uintptr_t)writer.approved_next,(unsigned long long)(uintptr_t)writer.approved_leaf_next);
        }
        return;
    }
    if(InterlockedCompareExchange(&tickLock,1,0))return;
    uint64_t now=GetTickCount64();
    int pollDue=now>=writerPollAt;
    if(pollDue){writerPollAt=now+250;if(RequestFile(stopPath)){
        InterlockedExchange(&writer.diagnostics_closed,1);InterlockedExchange(&writer.teardown,1);}}
    if(coopDynamicMode && pollDue && !Stopped()) {
        AcquireSRWLockExclusive(&writer.metadata);
        coopProbeActive=1;coopProbeReads=coopProbeBytes=0;
        Manager m;
        if(CoopManagerRead(&m)) {
            InterlockedExchange64(&writer.unit_cap,(LONG64)m.capacity);
            coop_refresh_units(&m,0);
            InterlockedExchange64(&writer.garments_cached,(LONG64)coopGarmentCount);
        }
        coopProbeActive=0;
        ReleaseSRWLockExclusive(&writer.metadata);
    }
    logsTick=0;
    WriterFaultTick(mod);
    if(!writerDiagnosticsDone) {
        if(!InvocationDrainOne(mod)){
            if(!coopDynamicMode) gw_fail(&writer,GW_CAPTURE,0);
            InterlockedExchange(&writer.diagnostics_closed,1);
        }
        if(!coopDynamicMode && leafNormalMask==0x7f && leafGroupedMask==0x7f)InterlockedExchange(&writer.diagnostics_closed,1);
        if(InterlockedCompareExchange(&writer.diagnostics_closed,0,0)) {
            if(!writerDrainAt)writerDrainAt=now?now:1;
            unsigned pending=InvocationPending();
            LONG producers=InterlockedCompareExchange(&writerDiagnosticProducers,0,0);
            if((!pending&&!producers) || now-writerDrainAt>=IV_DRAIN_MS) {
                int complete=coopDynamicMode?(!pending && !producers && !shadowFail && !leafFail && leafOk==shadowRecords && shadowOk==shadowRecords && !InterlockedCompareExchange(&writer.fatal,0,0)):
                    (leafNormalMask==0x7f && leafGroupedMask==0x7f && !pending && !producers && !shadowFail && !leafFail && leafOk==shadowRecords && shadowOk==shadowRecords && !InterlockedCompareExchange(&writer.fatal,0,0));
                hf->log(mod,"LEGO_COMPACT_LEAF_WRITER diagnostics_complete complete=%u correction_active=1 expected_mask=7F accepted_mask=%02X records=%u math_ok=%u math_fail=%u accepted_records=%u refused_records=%u abandoned_records=%u attempted=%llu pending=%u producers=%ld whitelist_objects=%u normal_mask=%02X grouped_mask=%02X leaf_ok=%u leaf_fail=%u",
                    complete,invocationMask,shadowRecords,shadowOk,shadowFail,invocationAccepted,invocationRefused,invocationAbandoned,
                    (unsigned long long)WriterCounter(&writerDiagnosticSequence),pending,producers,coopDynamicMode?coopReg.active_targets:invocationWhitelistCount,leafNormalMask,leafGroupedMask,leafOk,leafFail);
                writerDiagnosticsDone=1;
            }
        }
    }
    LONG fatal=InterlockedCompareExchange(&writer.fatal,0,0);
    /* Drain a failure first raised by diagnostics before emitting fatal. */
    WriterFaultTick(mod);
    if(fatal && !writerReportedFatal && InterlockedCompareExchange(&writer.fault.state,0,0)>=2){writerReportedFatal=writer.fault.reason;
        hf->log(mod,"LEGO_COMPACT_LEAF_WRITER fatal reason=%ld sequence=%llu correction_active=1 cleanup_required=1",writer.fault.reason,(unsigned long long)writer.fault.sequence);}
    if(now>=writerHealthAt && writerHealthRows<600) {
        writerHealthAt=now+(coopDynamicMode?30000u:500u);writerHealthRows++;
        uint32_t activeTargets=0,adm=0,ret=0,aba=0,players=0;
        if(coopDynamicMode) {
            AcquireSRWLockShared(&writer.metadata);
            activeTargets=coopReg.active_targets;adm=coopReg.admissions;
            ret=coopReg.retirements;aba=coopReg.aba_rebinds;players=coopReg.player_count;
            ReleaseSRWLockShared(&writer.metadata);
            if(adm!=writerLastAdmissions || ret!=writerLastRetires || aba!=writerLastAba || players!=writerLastPlayers) {
                writerLastAdmissions=adm;writerLastRetires=ret;writerLastAba=aba;writerLastPlayers=players;
                hf->log(mod,"LEGO_COMPACT_LEAF_WRITER coop_registry active_targets=%u admissions=%u retirements=%u aba_rebinds=%u players=%u local_hits=%llu remote_hits=%llu preview_hits=%llu",
                    activeTargets,adm,ret,aba,players,(unsigned long long)WriterCounter(&writer.local_hits),(unsigned long long)WriterCounter(&writer.remote_hits),(unsigned long long)WriterCounter(&writer.preview_hits));
            }
        }
        hf->log(mod,"LEGO_COMPACT_LEAF_WRITER health row=%u correction_active=1 matched=%llu corrected=%llu failed=%llu unclassified=%llu warmup=%llu inflight=%llu peak=%llu diagnostic_dropped=%llu fatal=%ld teardown=%ld active_targets=%u retires=%llu aba=%llu local=%llu remote=%llu preview=%llu players=%u passthrough=%llu plan_pass=%llu recheck_pass=%llu fast_evicts=%llu teardown_evicts=%llu mut_skips=%llu unit_cap=%llu garments=%llu neg_hits=%llu rej_mgr=%llu rej_world=%llu rej_nomatch=%llu rej_meta=%llu rej_parent=%llu rej_admit=%llu",
            writerHealthRows,(unsigned long long)WriterCounter(&writer.matched),(unsigned long long)WriterCounter(&writer.corrected),
            (unsigned long long)WriterCounter(&writer.failed),(unsigned long long)WriterCounter(&writer.unclassified),
            (unsigned long long)WriterCounter(&writer.warmup),(unsigned long long)WriterCounter(&writer.inflight),
            (unsigned long long)WriterCounter(&writer.peak),(unsigned long long)WriterCounter(&writerDiagnosticDropped),fatal,
            InterlockedCompareExchange(&writer.teardown,0,0),activeTargets,
            (unsigned long long)WriterCounter(&writer.retires),(unsigned long long)WriterCounter(&writer.aba_rebinds),
            (unsigned long long)WriterCounter(&writer.local_hits),(unsigned long long)WriterCounter(&writer.remote_hits),
            (unsigned long long)WriterCounter(&writer.preview_hits),players,
            (unsigned long long)WriterCounter(&writer.passthrough_retired),
            (unsigned long long)WriterCounter(&writer.passthrough_plan),
            (unsigned long long)WriterCounter(&writer.passthrough_recheck),
            (unsigned long long)WriterCounter(&writer.fast_evicts),
            (unsigned long long)WriterCounter(&writer.capture_teardown_evicts),
            (unsigned long long)WriterCounter(&writer.capture_mutation_skips),
            (unsigned long long)WriterCounter(&writer.unit_cap),
            (unsigned long long)WriterCounter(&writer.garments_cached),
            (unsigned long long)WriterCounter(&writer.neg_hits),
            (unsigned long long)WriterCounter(&writer.rej_mgr),
            (unsigned long long)WriterCounter(&writer.rej_world),
            (unsigned long long)WriterCounter(&writer.rej_nomatch),
            (unsigned long long)WriterCounter(&writer.rej_meta),
            (unsigned long long)WriterCounter(&writer.rej_parent),
            (unsigned long long)WriterCounter(&writer.rej_admit));
    }
    InterlockedExchange(&tickLock,0);
}
static void WriterEnded(HfMod *mod,HfEvent event,void *user) {
    InterlockedExchange(&writer.teardown,1);InterlockedExchange(&writer.diagnostics_closed,1);
    if(!InterlockedCompareExchange(&writer.ready,0,0))Ended(mod,event,user);
}
