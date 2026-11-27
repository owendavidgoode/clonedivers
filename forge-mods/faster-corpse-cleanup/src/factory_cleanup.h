/* Build 25480438: follow both native death lists and request deferred
 * removal only after the game has confirmed an owned corpse (phase 2).
 * 1.0.6-clonedivers.2: Eliminates the count > 128 failure cliff via 2,048-row
 * BSS buffer, adds 2,048-slot O(1) unit hash table + 1-syscall EntityManager
 * descriptor snapshot/occupancy gauge, and runs a 4-Tier Adaptive / Level 12
 * ULTRAKILL 0.0s Emergency Turbo-Reaper (up to 16 removals/update). */
#ifndef CC_FACTORY_CLEANUP_H
#define CC_FACTORY_CLEANUP_H

#define CC_MAX_LIVES 2048u
#define CC_UNIT_HT_SIZE 4096u
#define CC_UNIT_HT_MASK (CC_UNIT_HT_SIZE - 1u)
#define CC_MAX_WRECK_ROWS 2048u
#define CC_EM_DESC_CAP 2048u
#define CC_EM_HT_SIZE 4096u
#define CC_EM_HT_MASK (CC_EM_HT_SIZE - 1u)

typedef struct CcFactoryLife {
    uint32_t unit,entity,stamp;
    uintptr_t descriptor;
    uint8_t identity[24];
    float elapsed,last_request,next_check,retry_at,resolve_retry_at;
    uint32_t checked_kind,retry_entity,retry_kind;
    int used,classified,supported,requested;
} CcFactoryLife;

static CcFactoryLife factory_lives[CC_MAX_LIVES];
static uint16_t factory_unit_ht[CC_UNIT_HT_SIZE];
static unsigned factory_free_cursor;
static uint8_t s_initial_rows[32][64];
static uint8_t s_wreck_rows[CC_MAX_WRECK_ROWS][64];
static uint8_t s_em_desc_buf[CC_EM_DESC_CAP * 24u];
static uint16_t s_em_entity_ht[CC_EM_HT_SIZE];
static int s_em_bulk_valid;
static uint32_t s_live_entity_count;
static uint32_t s_last_wreck_count;
static unsigned s_active_reaper_tier;
static unsigned s_removals_this_frame;
static float s_effective_delay_s = 5.0f;

static uint32_t factory_stamp;
static uintptr_t factory_game,factory_registry;
static volatile LONG factory_thread;
static int factory_in_mission,factory_observing;
static unsigned factory_observed,factory_expired,removal_budget;
#define CC_RESOLVES_PER_UPDATE 16u
#define CC_READS_PER_UPDATE 256u
static unsigned factory_resolves_left,factory_reads_left=CC_READS_PER_UPDATE,factory_cursor;
static float factory_credit;
static double factory_time,factory_next_report=10.0;
static unsigned factory_reported_observed,factory_reported_expired;
static int pending_state;
static uintptr_t pending_root,pending_list;
static uint32_t pending_count_snapshot;
static uint8_t pending_bits[2048/8];
typedef void (*CcQueueUpdate)(uintptr_t,float);
typedef void (*CcRemoveEntity)(uintptr_t,uint32_t);
static CcQueueUpdate original_queue_update;
static CcRemoveEntity native_remove_entity;

static uint32_t cc_u32(const void *at){uint32_t v;memcpy(&v,at,4);return v;}
static uint64_t cc_u64(const void *at){uint64_t v;memcpy(&v,at,8);return v;}
static float cc_f32(const void *at){float v;memcpy(&v,at,4);return v;}
static int cc_address(uintptr_t at,size_t size)
{return at>=0x10000&&at<UINT64_C(0x800000000000)&&size<=UINT64_C(0x800000000000)-at;}
static int cc_memory(uintptr_t at,void *out,size_t n)
{
    if(!cc_address(at,n))return 0;
    if(!factory_reads_left){return 0;}
    --factory_reads_left;return hf->read(at,out,n)==0;
}
static uintptr_t cc_pointer(uintptr_t at)
{uintptr_t value=0;return cc_memory(at,&value,8)&&cc_address(value,1)?value:0;}

static uint32_t cc_hash_u32(uint32_t v)
{
    uint32_t x=v*0x9E3779B1u;
    x^=x>>16;
    return x;
}

static void cc_clear_factory_lives(void)
{
    memset(factory_lives,0,sizeof factory_lives);
    memset(factory_unit_ht,0,sizeof factory_unit_ht);
    factory_free_cursor=0;
}

static void cc_unit_ht_insert(uint32_t unit,unsigned idx)
{
    uint32_t h=cc_hash_u32(unit)&CC_UNIT_HT_MASK;
    for(unsigned p=0;p<CC_UNIT_HT_SIZE;++p)
    {
        uint32_t k=(h+p)&CC_UNIT_HT_MASK;
        uint16_t cur=factory_unit_ht[k];
        if(!cur||cur==(uint16_t)(idx+1u)){factory_unit_ht[k]=(uint16_t)(idx+1u);return;}
    }
}

static void cc_rebuild_unit_ht(void)
{
    memset(factory_unit_ht,0,sizeof factory_unit_ht);
    for(unsigned i=0;i<CC_MAX_LIVES;++i)
    {
        if(factory_lives[i].used&&factory_lives[i].unit)
            cc_unit_ht_insert(factory_lives[i].unit,i);
    }
}

static int cc_factory_resource(uint64_t resource)
{
    /* Binary search sorted cc_targets[142] in <= 8 comparisons. */
    size_t lo=0,hi=CC_COUNT(cc_targets);
    while(lo<hi)
    {
        size_t mid=lo+(hi-lo)/2u;
        uint64_t cur=cc_targets[mid].resource;
        if(cur==resource)return 1;
        if(cur<resource)lo=mid+1u;
        else hi=mid;
    }
    static const uint64_t parts[]={UINT64_C(0xB971B1141E1CBFAC),UINT64_C(0x49674794A165EDA8),
        UINT64_C(0x8FB47DD28B398CF7),UINT64_C(0xB8580CAC32214FC0)};
    for(unsigned i=0;i<CC_COUNT(parts);++i)if(resource==parts[i])return 1;
    return 0;
}

static void cc_snapshot_em_descriptors(uintptr_t root)
{
    s_em_bulk_valid=0;
    s_live_entity_count=0;
    if(!root||!cc_memory(root+0xF32F18,s_em_desc_buf,sizeof s_em_desc_buf))return;
    memset(s_em_entity_ht,0,sizeof s_em_entity_ht);
    uint32_t live=0;
    for(unsigned idx=0;idx<CC_EM_DESC_CAP;++idx)
    {
        const uint8_t *d=s_em_desc_buf+idx*24u;
        uint64_t res=cc_u64(d);
        uint32_t ent=cc_u32(d+8);
        if(!res||!ent||ent==UINT32_MAX)continue;
        ++live;
        uint32_t h=cc_hash_u32(ent)&CC_EM_HT_MASK;
        for(unsigned p=0;p<64u;++p)
        {
            uint32_t k=(h+p)&CC_EM_HT_MASK;
            if(!s_em_entity_ht[k]){s_em_entity_ht[k]=(uint16_t)(idx+1u);break;}
        }
    }
    s_live_entity_count=live;
    s_em_bulk_valid=1;
}

static int cc_factory_descriptor(uintptr_t root,uint32_t entity,uint8_t out[24],uintptr_t *address)
{
    if(!root||!entity||entity==UINT32_MAX)return 0;
    if(s_em_bulk_valid)
    {
        uint32_t h=cc_hash_u32(entity)&CC_EM_HT_MASK;
        for(unsigned p=0;p<64u;++p)
        {
            uint16_t slot=s_em_entity_ht[(h+p)&CC_EM_HT_MASK];
            if(!slot)break;
            unsigned idx=(unsigned)slot-1u;
            const uint8_t *d=s_em_desc_buf+idx*24u;
            if(cc_u32(d+8)==entity)
            {
                *address=root+0xF32F18+24u*(uintptr_t)idx;
                memcpy(out,d,24);
                return 1;
            }
        }
    }
    uint8_t map[20];
    if(!cc_memory(root+0xF1AEB0,map,sizeof map))return 0;
    uintptr_t entries=(uintptr_t)cc_u64(map);uint32_t count=cc_u32(map+8),empty=cc_u32(map+12),mult=cc_u32(map+16);
    if(!cc_address(entries,8)||!count||count>1048576||(count&(count-1))||entity==empty)return 0;
    for(uint32_t probe=0;probe<count&&probe<128;++probe)
    {
        uint8_t row[8];
        if(!cc_memory(entries+8u*((entity*mult+probe)&(count-1)),row,8))return 0;
        if(cc_u32(row)==entity)
        {
            uint32_t index=cc_u32(row+4);if(index>=2048)return 0;
            *address=root+0xF32F18+24u*index;
            return cc_memory(*address,out,24)&&cc_u32(out+8)==entity;
        }
        if(cc_u32(row)==empty)return 0;
    }
    return 0;
}

static CcFactoryLife *cc_factory_life(uint32_t unit,uint32_t entity)
{
    (void)entity;
    uint32_t h=cc_hash_u32(unit)&CC_UNIT_HT_MASK;
    for(unsigned p=0;p<CC_UNIT_HT_SIZE;++p)
    {
        uint16_t slot=factory_unit_ht[(h+p)&CC_UNIT_HT_MASK];
        if(!slot)break;
        CcFactoryLife *life=&factory_lives[slot-1u];
        if(life->used&&life->unit==unit)return life;
    }
    for(unsigned step=0;step<CC_MAX_LIVES;++step)
    {
        unsigned idx=(factory_free_cursor+step)%CC_MAX_LIVES;
        CcFactoryLife *life=&factory_lives[idx];
        if(!life->used)
        {
            factory_free_cursor=(idx+1u)%CC_MAX_LIVES;
            memset(life,0,sizeof *life);
            life->used=1;
            life->unit=unit;
            life->last_request=-1000.f;
            cc_unit_ht_insert(unit,idx);
            return life;
        }
    }
    return NULL;
}

static int cc_corpse_wrapper(uint64_t resource)
{return resource==UINT64_C(0x62928899DAC02979)||resource==UINT64_C(0xC6D5AF4610B26661)||resource==UINT64_C(0x9C311C48F9609212);}

/* A snapshot of native pending membership is shared by every candidate in
 * this update. An existing request is a backoff, not more work per frame. */
static int cc_factory_pending(uintptr_t root,uint32_t index)
{
    if(pending_state&&pending_root!=root)return -1;
    if(!pending_state)
    {
        uint32_t rows[2048];pending_root=root;pending_state=-1;
        if(!cc_memory(root+0xF3EF18,&pending_count_snapshot,4)||pending_count_snapshot>=2048)return -1;
        pending_list=cc_pointer(root+0xF3EF20);
        if(!pending_list||(pending_count_snapshot&&!cc_memory(pending_list,rows,(size_t)pending_count_snapshot*4)))return -1;
        memset(pending_bits,0,sizeof pending_bits);
        for(unsigned i=0;i<pending_count_snapshot;++i)
        {if(rows[i]>=2048)return -1;pending_bits[rows[i]/8]|=(uint8_t)(1u<<(rows[i]%8));}
        pending_state=1;
    }
    if(pending_state<0)return -1;
    return (pending_bits[index/8]>>(index%8))&1;
}

static void cc_factory_row(uintptr_t root,uintptr_t queue,uintptr_t at,const uint8_t row[64],float dt,int after)
{
    (void)after;
    if(!cc_is_host_or_solo())return;
    uint32_t unit=cc_u32(row),entity=cc_u32(row+4),kind=cc_u32(row+8);
    if((kind!=1&&kind!=2)||!unit||!entity||entity==UINT32_MAX)return;
    float remaining=cc_f32(row+12);
    if(!isfinite(remaining)||remaining<-3600.f||remaining>3600.f)return;
    CcFactoryLife *life=cc_factory_life(unit,entity);if(!life)return;
    if(life->stamp!=factory_stamp){life->stamp=factory_stamp;life->elapsed+=dt;}
    if(life->retry_entity==entity&&life->retry_kind==kind&&life->elapsed<life->resolve_retry_at)
    {return;}
    int changed=!life->classified||life->entity!=entity||life->checked_kind!=kind;
    int due=kind==2&&life->supported&&(life->identity[20]&1)&&life->elapsed>=s_effective_delay_s&&life->elapsed>=life->retry_at;
    if(!changed&&life->elapsed<life->next_check&&!(due&&removal_budget))return;
    if(!factory_resolves_left)return;
    --factory_resolves_left;
    uint8_t identity[24];uintptr_t descriptor=life->descriptor;
    int cached=life->classified&&life->entity==entity&&descriptor&&
        ((s_em_bulk_valid&&descriptor>=root+0xF32F18&&descriptor<root+0xF3EF18&&((descriptor-root-0xF32F18)%24u==0u))
            ?(memcpy(identity,s_em_desc_buf+(descriptor-root-0xF32F18),24),1)
            :cc_memory(descriptor,identity,24))&&
        cc_u32(identity+8)==entity&&cc_u32(identity+12)==unit;
    if(!cached&&(!cc_factory_descriptor(root,entity,identity,&descriptor)||cc_u32(identity+12)!=unit))
    {life->next_check=life->resolve_retry_at=life->elapsed+.25f;life->retry_entity=entity;life->retry_kind=kind;return;}
    life->resolve_retry_at=0;
    int known=life->classified;
    if(known&&life->entity==entity&&memcmp(life->identity,identity,16))
    {memset(life,0,sizeof *life);return;}
    int supported=cc_factory_resource(cc_u64(identity))||
        (known&&life->supported&&kind==2&&cc_corpse_wrapper(cc_u64(identity)));
    if(supported&&(!known||!life->supported))++factory_observed;
    if(life->entity!=entity){life->requested=0;life->retry_at=0;}
    life->classified=1;life->supported=supported;life->entity=entity;life->checked_kind=kind;
    life->descriptor=descriptor;memcpy(life->identity,identity,24);
    life->next_check=life->elapsed+(supported?(s_active_reaper_tier>=2u?0.05f:.25f):1.f);
    /* Phase 1 contributes to age; only phase 2 on SOLO/HOST may request removal. */
    if(!cc_is_host_or_solo()||!cc_extra_enabled()||!supported||kind!=2||life->elapsed<s_effective_delay_s||!native_remove_entity||!removal_budget||
       life->elapsed<life->retry_at||!(identity[20]&1))return;
    if(!root||descriptor<root+0xF32F18||descriptor>=root+0xF3EF18||
       (descriptor-root-0xF32F18)%24)return;
    uint32_t index=(uint32_t)((descriptor-root-0xF32F18)/24);
    int queued=cc_factory_pending(root,index);
    if(queued){life->retry_at=life->elapsed+(queued>0?1.f:.25f);life->next_check=life->retry_at;return;}
    uint8_t again[24],current[64];uint32_t count;
    if(!cc_memory(descriptor,again,24)||memcmp(again,identity,24)||
       cc_pointer(factory_game+0x346BF98)!=root||cc_pointer(factory_game+0x346D500)!=queue||
       !cc_memory(at,current,64)||memcmp(current,row,64)||
       !cc_memory(root+0xF3EF18,&count,4)||count>=2040u||
       count<pending_count_snapshot||count>pending_count_snapshot+s_removals_this_frame||
       cc_pointer(root+0xF3EF20)!=pending_list)
    {pending_state=-1;life->retry_at=life->elapsed+.25f;return;}
    pending_count_snapshot=count;
    --removal_budget;
    factory_credit=fmaxf(0.f,factory_credit-1.f);
    ++s_removals_this_frame;
    native_remove_entity(root,entity);
    pending_bits[index/8]|=(uint8_t)(1u<<(index%8));
    life->last_request=life->elapsed;life->retry_at=life->elapsed+1.f;life->next_check=life->retry_at;
    life->requested=1;++factory_expired;
}

static int cc_factory_scan(uintptr_t root,uintptr_t queue,uint32_t raw_count,float dt,int after)
{
    /* Never abort cleanup when raw_count > 128! Clamp to CC_MAX_WRECK_ROWS (2048). */
    if(raw_count>65536u)return 0;
    uint32_t scan_count=raw_count>CC_MAX_WRECK_ROWS?CC_MAX_WRECK_ROWS:raw_count;
    if(!cc_memory(queue+0x18,s_initial_rows,sizeof s_initial_rows)||
       (scan_count&&!cc_memory(queue+0x820,s_wreck_rows,(size_t)scan_count*64u)))return 0;
    unsigned total=32u+scan_count;
    unsigned step_stride=s_active_reaper_tier>=2u?64u:CC_RESOLVES_PER_UPDATE;
    for(unsigned j=0;j<total;++j)
    {
        unsigned i=(factory_cursor+j)%total;
        if(i<32u)cc_factory_row(root,queue,queue+0x18+(size_t)i*64u,s_initial_rows[i],dt,after);
        else cc_factory_row(root,queue,queue+0x820+(size_t)(i-32u)*64u,s_wreck_rows[i-32u],dt,after);
    }
    factory_cursor=(factory_cursor+step_stride)%total;
    return 1;
}

static void cc_factory_before(uintptr_t queue,float dt)
{
    factory_reads_left=2048u;factory_resolves_left=CC_RESOLVES_PER_UPDATE;pending_state=0;
    removal_budget=0;s_em_bulk_valid=0;s_removals_this_frame=0;
    if(!cc_is_host_or_solo()||!cc_extra_enabled()||!isfinite(dt)||dt<0||dt>1||
       !factory_game||cc_pointer(factory_game+0x346D500)!=queue)
    {cc_clear_factory_lives();factory_credit=0;return;}
    uintptr_t registry=cc_pointer(factory_game+0x346BF98);
    if(!registry){cc_clear_factory_lives();factory_credit=0;return;}
    if(registry!=factory_registry)
    {cc_clear_factory_lives();factory_registry=registry;factory_cursor=0;}

    uint32_t raw_count=0;
    if(!cc_memory(queue+0x818,&raw_count,4)||raw_count>65536u)return;
    s_last_wreck_count=raw_count;

    if(cc_turbo_reaper_active()||raw_count>=8u)
        cc_snapshot_em_descriptors(registry);

    unsigned max_budget=1u;
    float rate_per_s=60.f;
    if(cc_turbo_reaper_active()||s_live_entity_count>=1550u||raw_count>=320u)
    {
        s_active_reaper_tier=3u;
        s_effective_delay_s=(cc_turbo_reaper_active()&&cc_explicit_delay_seconds()>=0)?(float)config.seconds:0.0f;
        max_budget=16u;rate_per_s=960.f;factory_resolves_left=512u;factory_reads_left=2048u;
    }
    else if(s_live_entity_count>=1300u||raw_count>=192u)
    {
        s_active_reaper_tier=2u;
        s_effective_delay_s=fminf((float)config.seconds,0.25f);
        max_budget=12u;rate_per_s=720.f;factory_resolves_left=192u;factory_reads_left=1024u;
    }
    else if(s_live_entity_count>=900u||raw_count>=96u)
    {
        s_active_reaper_tier=1u;
        s_effective_delay_s=fminf((float)config.seconds,1.5f);
        max_budget=6u;rate_per_s=360.f;factory_resolves_left=96u;factory_reads_left=512u;
    }
    else
    {
        s_active_reaper_tier=0u;
        s_effective_delay_s=(float)config.seconds;
        max_budget=1u;rate_per_s=60.f;factory_resolves_left=16u;factory_reads_left=256u;
    }

    factory_time+=dt;
    factory_credit=fminf((float)max_budget,factory_credit+dt*rate_per_s);
    if(cc_turbo_reaper_active()&&max_budget>1u&&factory_credit<1.f)
        factory_credit=(float)max_budget;
    if(factory_credit>=1.f&&cc_extra_enabled())
        removal_budget=(unsigned)factory_credit;

    if(++factory_stamp==0){cc_clear_factory_lives();factory_stamp=1;}
    if(!cc_factory_scan(registry,queue,raw_count,dt,0))return;
    int evicted=0;
    for(unsigned i=0;i<CC_MAX_LIVES;++i)
    {
        if(factory_lives[i].used&&factory_lives[i].stamp!=factory_stamp)
        {memset(&factory_lives[i],0,sizeof factory_lives[i]);evicted=1;}
    }
    if(evicted)cc_rebuild_unit_ht();

    if(factory_time>=factory_next_report)
    {
        factory_next_report=factory_time+10.0;
        if(factory_observed!=factory_reported_observed||factory_expired!=factory_reported_expired)
        {
            hf->log(self,"Cleanup activity: tracked=%u native_requests=%u tier=%u live_em=%u wrecks=%u delay=%.2fs max_per_update=%u",
                factory_observed,factory_expired,s_active_reaper_tier,s_live_entity_count,raw_count,s_effective_delay_s,max_budget);
            factory_reported_observed=factory_observed;factory_reported_expired=factory_expired;
        }
    }
}

static void cc_factory_update(uintptr_t queue,float dt)
{
    DWORD game_thread=(DWORD)InterlockedCompareExchange(&factory_thread,0,0);
    int own_thread=game_thread&&GetCurrentThreadId()==game_thread;
    int outer=own_thread&&!factory_observing;
    if(outer)factory_observing=1;
    original_queue_update(queue,dt);
    if(outer)
    {
        if(!cc_stopped()&&factory_in_mission&&cc_is_host_or_solo()&&cc_extra_enabled())
        {cc_factory_before(queue,dt);}
        else
        {cc_clear_factory_lives();factory_credit=0;}
        factory_observing=0;
    }
}

static void cc_factory_loaded(HfMod *mod,HfEvent event,void *user)
{
    (void)mod;(void)event;(void)user;
    if(original_queue_update)return;
    size_t size=0;uintptr_t game=hf->module_base("game.dll",&size);
    if(!game||size!=74727424u)return;
    static const struct {uint32_t rva;const char *bytes;} pins[]={
        {0x13BE660,"4C 8B DC 53 55 56 57 41 54 48 81 EC 40 01 00 00 41 0F 29 73 C8 45 0F 29 43 A8 48 8B 05 8F D9 27"},
        {0x13BE690,"48 8D 59 24 4D 89 7B 18 44 0F 28 C1 41 BF 20 00 00 00"},
        {0x13BE710,"F3 0F 10 43 04 0F 2F C6 0F 86 0B 01 00 00"},
        {0x13BE84A,"44 39 A6 18 08 00 00 0F 86 0C 04 00 00"},
        {0x13BF30C,"45 89 67 04 41 8B DC 48 8B 35 4E 7B F6 01 4C 8B A4 24 F0 09 00 00 41 C7 47 08 02 00 00 00"},
        {0xFDC310,"48 89 5C 24 08 57 48 83 EC 30 3B 15 4C 82 4A 02"},
        {0xFDC3E6,"8B 97 18 EF F3 00 8B 40 04 48 8B 8F 20 EF F3 00 89 04 91 FF 87 18 EF F3"},
    };
    for(unsigned i=0;i<CC_COUNT(pins);++i)
        if(!hf->code_matches(game+pins[i].rva,pins[i].bytes))
        {hf->log(self,"Corpse lifecycle hook refused: native guard +%X",pins[i].rva);return;}
    factory_game=game;
#ifdef CC_UNIT_TEST
    if(cc_test_remove_entity_override)native_remove_entity=cc_test_remove_entity_override;
    else
#endif
    native_remove_entity=(CcRemoveEntity)(game+0xFDC310);
    if(hf->hook(self,game+0x13BE660,(void *)cc_factory_update,(void **)&original_queue_update))
    {factory_game=0;native_remove_entity=NULL;hf->log(self,"Corpse lifecycle hook unavailable");return;}
    hf->log(self,"Corpse lifecycle hook installed; 2048-row wreck scan + 4-tier ULTRAKILL turbo-reaper ready");
}
#endif
