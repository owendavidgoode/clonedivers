static void Tick(HfMod *mod,float dt,void *context)
{
    (void)dt;(void)context;
    if(Stopped() || InterlockedCompareExchange(&invocationTerminal,0,0) || InterlockedCompareExchange(&tickLock,1,0))return;
    uint64_t now=GetTickCount64();int wantArm=0,wantStop=0;
    /* File checks are tick-only and outside the metadata gate. */
    if(now>=nextPoll){nextPoll=now+500;wantStop=RequestFile(stopPath);if(!armedAt)wantArm=RequestFile(armPath);}
    if(wantStop){invocationUserStop=1;se_stop(&invocationEnvelope);}
    if(!se_try_gate(&invocationEnvelope))goto done;
    invocationSetupGate=1;readTick=byteTick=logsTick=logicalTick=0;updateSerial++;
    if(wantStop && renderStage<4)Finish(mod,"request");
    else {
        if(!armedAt && wantArm) {
            if(!ManagerRead(&initial)){Finish(mod,"manager_refusal");goto done;}
            armedAt=now?now:1;
            RLog(mod,"LEGO_COMPACT_LEAF_WRITER armed capacity=%u slots_per_tick=%u capacity_limit=%u record_limit=%u deadline_ms=%u",initial.capacity,PER_TICK,CAPACITY_LIMIT,RECORD_LIMIT,ARM_MS);
        }
        if(armedAt && now-armedAt>=ARM_MS && !se_is_stopped(&invocationEnvelope)) {
            invocationTimedOut=1;se_stop(&invocationEnvelope);
            if(renderStage<4)Finish(mod,"deadline");
        } else if(armedAt && renderStage<4 && !Stopped())Scan(mod);
    }
    unsigned attempts=invocationEnvelope.attempts;
    if(renderStage==4 && (attempts>=IV_ATTEMPTS || InterlockedCompareExchange(&invocationFatal,0,0)))se_stop(&invocationEnvelope);
    int admissionStopped=se_is_stopped(&invocationEnvelope);
    invocationSetupGate=0;se_release_gate(&invocationEnvelope);InvocationFlush(mod);
    if(Stopped() || renderStage<4)goto done;
    if(!InvocationDrainOne(mod)) {
        InvocationTerminalLog(mod,attempts,InvocationPending(),"record_file_or_release_refusal",0);goto done;
    }
    if(invocationMask==0x7F && !admissionStopped){se_stop(&invocationEnvelope);invocationDrainAt=now?now:1;goto done;}
    if(admissionStopped) {
        if(!invocationDrainAt)invocationDrainAt=now?now:1;
        unsigned pending=InvocationPending();
        if(!pending || now-invocationDrainAt>=IV_DRAIN_MS) {
            int complete=invocationMask==0x7F && !pending && invocationFiles==attempts &&
                shadowRecords==invocationFiles && shadowOk==shadowRecords && !shadowFail &&
                invocationFiles==invocationAccepted+invocationRefused && !invocationUserStop &&
                !invocationTimedOut && !InterlockedCompareExchange(&invocationFatal,0,0);
            const char *reason=complete?"complete":pending?"drain_expired":invocationUserStop?"request":
                invocationTimedOut?"deadline":InterlockedCompareExchange(&invocationFatal,0,0)?"capture_refusal":"coverage_incomplete";
            InvocationTerminalLog(mod,attempts,pending,reason,complete);
        }
    }
done:
    if(invocationSetupGate){invocationSetupGate=0;se_release_gate(&invocationEnvelope);InvocationFlush(mod);}
    InterlockedExchange(&tickLock,0);
}
static void Ended(HfMod *mod,HfEvent event,void *user)
{
    (void)mod;(void)event;(void)user;se_stop(&invocationEnvelope);
    InterlockedExchange(&invocationTerminal,1);InterlockedExchange(&stopped,1);
}
