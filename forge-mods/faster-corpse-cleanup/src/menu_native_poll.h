/* Corpse provider-owned settings. Existing game-thread menu cadence only.
 * Copyright (c) 2026 enochlophobianumerophobia. MIT.
 * Sequential observations are NOT an atomic provider transaction. */
#ifndef CC_MENU_NATIVE_POLL_H
#define CC_MENU_NATIVE_POLL_H
static void cc_poll_native(void)
{
    if(!menu_registered||!cc_menu_is_native()||menu_syncing)return;
    const CrMenuApi *api=cc_menu_backend();
    const char *ids[]={enabled_id,seconds_id,extra_id};
    const char *keys[]={"Enabled","DelaySeconds","ExtraCleanup"};
    double values[3];CcConfig next=config;unsigned invalid=0;
    uint64_t generation=api->epoch();
    if(!api->ready()||generation!=menu_epoch)return;
    for(unsigned i=0;i<3;i++){
        if(!api->get(ids[i],&values[i])||!api->ready()||api->epoch()!=generation)return;
        if(!cc_config_value(&next,keys[i],values[i]))invalid|=1u<<i;
    }
    cc_menu_trace(C6T_POLL,3,!invalid,(int)invalid,generation,0);
    if(!invalid){
        if(next.enabled!=config.enabled||next.seconds!=config.seconds||next.extra_cleanup!=config.extra_cleanup)
            cc_note_session_menu_edit();
        cc_accept_config(&next);cc_menu_trace(C6T_APPLY,3,1,0,generation,0);return;
    }
    /* No partial runtime commit. Repair only a still-invalid row, never a valid
     * newer provider edit or an unavailable read. Saved provider authority is
     * unchanged; no client settings file is written. A concurrent change after
     * the last read is an accepted experiment risk, not a CAS guarantee. */
    const double accepted[]={config.enabled,config.seconds,config.extra_cleanup};
    for(unsigned i=0;i<3;i++)if(invalid&(1u<<i)){
        double fresh;CcConfig check=config;
        if(!api->get(ids[i],&fresh)||!api->ready()||api->epoch()!=generation)return;
        if(cc_config_value(&check,keys[i],fresh))continue;
        int ok=api->set(ids[i],accepted[i]);
        cc_menu_trace(C6T_RESYNC,i,ok,1,generation,accepted[i]);
        if(!ok||!api->ready()||api->epoch()!=generation)return;
    }
}
#endif
