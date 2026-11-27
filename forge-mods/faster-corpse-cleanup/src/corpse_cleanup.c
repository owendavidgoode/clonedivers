/* HellForge native mod for Steam build 25480438 (SDK 16, host-gated co-op safe). */
#define HF_DISPATCHER_CLIENT_IMPLEMENTATION
#include "hf_dispatcher_client.h"
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <ctype.h>
#include <windows.h>
#include "hellforge_mod.h"
#include "clonedivers_host_options.h"

#define CC_BUILD "25480438"
#define CC_VERSION "1.0.6-clonedivers.3"
#define CC_DEFAULT_ENDPOINT 120
#define CC_COUNT(a) (sizeof(a)/sizeof((a)[0]))
#define CC_STRIDE 22096u
#define CC_DECAY_OFFSET 0x541Cu
static const HfApi *hf;
static HfMod *self;
typedef struct CcConfig { int enabled, seconds, extra_cleanup; } CcConfig;
static CcConfig config={1,5,1};

static char g_host_opts_path[CD_HOST_OPTIONS_MAX_PATH];
static uint64_t g_host_opts_mtime;
static uint64_t g_session_menu_edit_mtime;
static int g_host_opts_loaded;
static uint64_t g_next_host_opts_poll;
static int g_was_host;
static int g_corpse_turbo_reaper = 1;
static int g_corpse_explicit_delay = -1;
static CdHostOptions g_last_host_opts;
static int g_last_host_opts_init;
static int g_prev_live_theater = -1;
static int g_prev_live_turbo = -1;

static int cc_host_override_superseded(void)
{
    return g_session_menu_edit_mtime&&cd_session_override_is_newer(g_host_opts_mtime,g_session_menu_edit_mtime);
}

static int cc_theater_active_now(void)
{
    if(!g_last_host_opts_init){g_last_host_opts=cd_host_options_defaults();g_last_host_opts_init=1;}
    if(cc_host_override_superseded())return 0;
    if(g_last_host_opts.theater_preset==CD_THEATER_LEVEL12_ULTRAKILL||
       g_last_host_opts.chaos_intensity==CD_CHAOS_ULTRAKILL)
        return 1;
    if(g_last_host_opts.theater_preset!=CD_THEATER_CLONE_WARS&&
       g_last_host_opts.chaos_intensity!=CD_CHAOS_CLONE_WARS)
        return 0;
    if(!hf||!self||!hf->game_state||!hf->module_base||!hf->read)return 0;
    if(hf->game_state(self)!=HF_STATE_MISSION)return 0;
    size_t gsz=0;
    uintptr_t gbase=hf->module_base("game.dll",&gsz);
    int live_diff=cd_read_live_mission_difficulty(hf->read,gbase);
    return cd_host_options_active_for_difficulty(&g_last_host_opts,live_diff);
}

static int cc_live_turbo_active(void)
{
    return g_corpse_turbo_reaper&&cc_theater_active_now();
}

static int cc_turbo_reaper_active(void) { return cc_live_turbo_active(); }
static int cc_explicit_delay_seconds(void)
{
    if(cc_host_override_superseded())return -1;
    if(g_last_host_opts_init&&g_last_host_opts.theater_preset==CD_THEATER_CLONE_WARS&&!cc_theater_active_now())
        return -1;
    return g_corpse_explicit_delay;
}

static float cc_effective_decay_seconds(void)
{
    int exp_delay=cc_explicit_delay_seconds();
    if(cc_theater_active_now())
    {
        if(exp_delay>0)return (float)exp_delay;
        if(cc_live_turbo_active())return 0.15f;
        if(exp_delay==0)return 0.0f;
    }
    return (float)((exp_delay>=0)?exp_delay:config.seconds);
}

static float cc_effective_reaper_base_seconds(void)
{
    int exp_delay=cc_explicit_delay_seconds();
    if(cc_theater_active_now())
    {
        if(exp_delay>0)return (float)exp_delay;
        if(cc_live_turbo_active()||exp_delay==0)return 0.0f;
    }
    return (float)((exp_delay>=0)?exp_delay:config.seconds);
}

#ifdef CC_UNIT_TEST
typedef void (*CcTestRemoveFn)(uintptr_t,uint32_t);
static CcTestRemoveFn cc_test_remove_entity_override;
static uint64_t cc_test_now_filetime_override;
#endif

static uint64_t cc_now_filetime_100ns(void)
{
#ifdef CC_UNIT_TEST
    if(cc_test_now_filetime_override)return cc_test_now_filetime_override;
#endif
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    return ((uint64_t)ft.dwHighDateTime<<32)|(uint64_t)ft.dwLowDateTime;
}

static void cc_note_session_menu_edit(void)
{
    g_session_menu_edit_mtime=cc_now_filetime_100ns();
}

/* Strict co-op host authority gate: ONLY HF_SESSION_SOLO and HF_SESSION_HOST may write
 * HealthComponentData decay timers or request native_remove_entity (0xFDC310).
 * HF_SESSION_JOINER and HF_SESSION_NONE fail closed as pure no-ops. */
static int cc_is_host_or_solo(void)
{
    if(!hf||!self||!hf->session)return 0;
    HfSession s;
    memset(&s,0,sizeof s);
    s.role=0xFFFFFFFFu;
    hf->session(self,&s);
    return s.role==HF_SESSION_SOLO||s.role==HF_SESSION_HOST;
}

typedef struct CcDecay {
    uint32_t mode;
    float acceleration, minimum, maximum, extra;
    uint8_t flag, padding[3];
} CcDecay;
_Static_assert(sizeof(CcDecay)==24,"decay layout");
typedef struct CcTarget { uint64_t resource; CcDecay native; } CcTarget;
#include "targets.h"
typedef struct CcEdit {
    uintptr_t address;
    CcDecay before, expected, attempted;
    int held, conflict, pending, settled;
} CcEdit;
static CcEdit edits[CC_COUNT(cc_targets)];
static unsigned cursor;
static uint64_t next_pass,next_menu_poll;
static volatile LONG dirty=1,stopped;
static int template_complete;
static unsigned conflicts, writes, restores;
static void cc_request_reload(void){InterlockedExchange(&dirty,1);}
static int cc_take_reload(void){return InterlockedExchange(&dirty,0)!=0;}
static int cc_stopped(void){return InterlockedCompareExchange(&stopped,0,0)!=0;}
static int cc_cleanup_enabled(void)
{return cc_is_host_or_solo()&&config.enabled&&cc_effective_decay_seconds()<(float)CC_DEFAULT_ENDPOINT;}
static int cc_extra_enabled(void)
{return cc_cleanup_enabled()&&config.extra_cleanup;}

static int cc_any_held(void)
{
    for(unsigned i=0;i<CC_COUNT(edits);++i)if(edits[i].held||edits[i].pending)return 1;
    return 0;
}

#include "factory_cleanup.h"

static void cc_load_host_options_initial(const char *folder)
{
    g_host_opts_path[0]='\0';
    g_host_opts_mtime=0ULL;
    g_host_opts_loaded=0;
    g_last_host_opts=cd_host_options_defaults();
    g_last_host_opts_init=1;
    g_corpse_turbo_reaper=g_last_host_opts.corpse_turbo_reaper?1:0;
    if(!folder||!*folder)return;
    if(!cd_resolve_host_options_path(folder,g_host_opts_path,sizeof g_host_opts_path))return;
    CdHostOptions ho;
    int rc=cd_load_host_options_file(g_host_opts_path,&ho);
    if(rc==1)
    {
        g_last_host_opts=ho;
        config.enabled=ho.faster_corpse_cleanup?1:0;
        g_corpse_explicit_delay=ho.corpse_delay_seconds;
        if(ho.corpse_delay_seconds>=0&&ho.theater_preset!=CD_THEATER_CLONE_WARS)config.seconds=ho.corpse_delay_seconds;
        g_corpse_turbo_reaper=ho.corpse_turbo_reaper?1:0;
        g_host_opts_mtime=ho.mtime_100ns;
        g_host_opts_loaded=1;
        hf->log(self,"Host options loaded (%s): fasterCorpseCleanup=%s delay=%d turbo=%d preset=%s",
            g_host_opts_path,config.enabled?"true":"false",config.seconds,g_corpse_turbo_reaper,cd_theater_preset_name(ho.theater_preset));
    }
    else if(rc<0)
    {
        hf->log(self,"Invalid clonedivers-host-options.json ignored (%s); retaining startup settings",g_host_opts_path);
    }
}

#include "mod_menu.h"
#include "startup_settings.h"
static int menu_registered, menu_syncing;
static uint64_t menu_epoch, next_menu_attempt;
static uint64_t config_log_due;
static const char enabled_id[]="local.faster-corpse-cleanup.Enabled";
static const char seconds_id[]="local.faster-corpse-cleanup.DelaySeconds";
static const char extra_id[]="local.faster-corpse-cleanup.ExtraCleanup";

static int cc_config_value(CcConfig *out,const char *key,double value)
{
    if(!isfinite(value)||floor(value)!=value)return 0;
    if(!strcmp(key,"Enabled")&&value>=0&&value<=1){out->enabled=(int)value;return 1;}
    if(!strcmp(key,"DelaySeconds")&&value>=0&&value<=CC_DEFAULT_ENDPOINT){out->seconds=(int)value;return 1;}
    if(!strcmp(key,"ExtraCleanup")&&value>=0&&value<=1){out->extra_cleanup=(int)value;return 1;}
    return 0;
}
static void cc_accept_config(const CcConfig *next)
{
    if(next->enabled==config.enabled&&next->seconds==config.seconds&&next->extra_cleanup==config.extra_cleanup)return;
    config=*next;cc_request_reload();cc_clear_factory_lives();factory_credit=0;
    config_log_due=GetTickCount64()+300;
}
static void cc_sync_menu_ui(void)
{
    if(!menu_registered||menu_syncing)return;
    const CrMenuApi *api=cc_menu_backend();
    menu_syncing=1;
    double value;
    if(!api->get(enabled_id,&value)||value!=config.enabled)api->set(enabled_id,config.enabled);
    if(!api->get(seconds_id,&value)||value!=config.seconds)api->set(seconds_id,config.seconds);
    if(!api->get(extra_id,&value)||value!=config.extra_cleanup)api->set(extra_id,config.extra_cleanup);
    menu_syncing=0;
}
static void cc_show_config(void)
{
    if(cc_menu_is_native())return; /* Provider edits are consumed by cc_poll_native. */
    cc_sync_menu_ui();
}
static void cc_menu_changed(const char *id,double value,void *user)
{
    (void)user;
    if(menu_syncing||!id)return;
    const char *key=!strcmp(id,enabled_id)?"Enabled":!strcmp(id,seconds_id)?"DelaySeconds":!strcmp(id,extra_id)?"ExtraCleanup":NULL;
    CcConfig next=config;
    if(!key||!cc_config_value(&next,key,value))
    {hf->log(self,"Setting refused: invalid menu value");cc_show_config();return;}
    /* Live session edit in Mod Options Menu takes precedence over older host options file. */
    cc_note_session_menu_edit();
    cc_accept_config(&next);
}
#include "menu_native_poll.h"
static void cc_menu(uint64_t now)
{
    const CrMenuApi *api=cc_menu_backend();
    if(!api->connect())return;
    if(menu_epoch!=api->epoch())
    {menu_epoch=api->epoch();menu_registered=0;next_menu_attempt=0;}
    if(!menu_registered&&now>=next_menu_attempt)
    {
        CrMenuOption on={.size=sizeof on,.type=CR_MENU_TOGGLE,.id=enabled_id,
            .mod_id="local.faster-corpse-cleanup",.mod_name="Faster Corpse Cleanup",
            .label="Enable faster corpse cleanup",.description="Shortens cleanup of enemy bodies after death. Turning this off restores original timers. Bodies already removed cannot be restored.",
            .default_value=1,.min=0,.max=1,.step=1,.on_change=cc_menu_changed};
        CrMenuOption delay={.size=sizeof delay,.type=CR_MENU_SLIDER,.id=seconds_id,
            .mod_id=on.mod_id,.mod_name=on.mod_name,.label="Corpse cleanup delay (seconds)",
            .description="0 requests the earliest cleanup. 1 to 119 selects seconds, counting the initial death stage. Removal waits until the game confirms a corpse, so long death animations can still add time. 120 restores original timers. Default: 5 seconds.",
            .default_value=5,.min=0,.max=120,.step=1,.on_change=cc_menu_changed};
        CrMenuOption extra={.size=sizeof extra,.type=CR_MENU_TOGGLE,.id=extra_id,
            .mod_id=on.mod_id,.mod_name=on.mod_name,.label="Extra corpse cleanup",
            .description="Also clears corpses that linger after their timers. Turn off to use timer changes only and skip extra corpse checks and removal requests. Some wrecks may then remain longer. Default: on.",
            .default_value=1,.min=0,.max=1,.step=1,.on_change=cc_menu_changed};
        menu_syncing=1;
        int ok=api->register_option(&on)&&api->register_option(&delay)&&api->register_option(&extra);
        menu_syncing=0;
        if(ok)
        {
            CcConfig saved=config;double on_value,delay_value,extra_value;
            if(api->get(enabled_id,&on_value)&&api->get(seconds_id,&delay_value)&&api->get(extra_id,&extra_value)&&
               cc_config_value(&saved,"Enabled",on_value)&&cc_config_value(&saved,"DelaySeconds",delay_value)&&
               cc_config_value(&saved,"ExtraCleanup",extra_value))
            {
                /* If Launcher explicitly wrote clonedivers-host-options.json before session start
                 * and no newer in-game edit has occurred yet, honor Launcher's fasterCorpseCleanup
                 * toggle while retaining saved DelaySeconds/ExtraCleanup from MOM unless overridden. */
                if(g_host_opts_loaded&&!cd_session_override_is_newer(g_host_opts_mtime,g_session_menu_edit_mtime))
                {
                    CdHostOptions ho;
                    if(cd_load_host_options_file(g_host_opts_path,&ho)==1)
                    {
                        g_last_host_opts=ho;
                        g_last_host_opts_init=1;
                        saved.enabled=ho.faster_corpse_cleanup?1:0;
                        g_corpse_explicit_delay=ho.corpse_delay_seconds;
                        if(ho.corpse_delay_seconds>=0&&ho.theater_preset!=CD_THEATER_CLONE_WARS)saved.seconds=ho.corpse_delay_seconds;
                        g_corpse_turbo_reaper=ho.corpse_turbo_reaper?1:0;
                        g_host_opts_mtime=ho.mtime_100ns;
                    }
                    else
                    {
                        saved.enabled=config.enabled;
                    }
                }
                menu_registered=1;
                cc_accept_config(&saved);
                cc_sync_menu_ui();
                hf->log(self,"Registered 3 options; settings synced with Mod Options Menu and host options");
            }
        }
        next_menu_attempt=now+2000;
    }
    if(cc_menu_is_native())cc_poll_native();
}

static void cc_poll_host_options(void)
{
    if(!g_host_opts_path[0])return;
    uint64_t mtime=0ULL;
    int st=cd_get_file_mtime_100ns(g_host_opts_path,&mtime);
    if(st!=1||mtime==g_host_opts_mtime)return;
    g_host_opts_mtime=mtime;
    if(cd_session_override_is_newer(mtime,g_session_menu_edit_mtime))return;
    CdHostOptions ho;
    if(cd_load_host_options_file(g_host_opts_path,&ho)!=1)return;
    g_host_opts_loaded=1;
    CdTheaterPreset prev_preset=g_last_host_opts.theater_preset;
    g_last_host_opts=ho;
    g_last_host_opts_init=1;
    CcConfig next=config;
    next.enabled=ho.faster_corpse_cleanup?1:0;
    int prev_turbo=g_corpse_turbo_reaper;
    int prev_exp_delay=g_corpse_explicit_delay;
    g_corpse_explicit_delay=ho.corpse_delay_seconds;
    if(ho.corpse_delay_seconds>=0&&ho.theater_preset!=CD_THEATER_CLONE_WARS)next.seconds=ho.corpse_delay_seconds;
    g_corpse_turbo_reaper=ho.corpse_turbo_reaper?1:0;
    if(next.enabled!=config.enabled||next.seconds!=config.seconds||g_corpse_turbo_reaper!=prev_turbo||g_corpse_explicit_delay!=prev_exp_delay||ho.theater_preset!=prev_preset)
    {
        hf->log(self,"Host options reloaded: fasterCorpseCleanup=%s delay=%d turbo=%d preset=%s",
            next.enabled?"true":"false",next.seconds,g_corpse_turbo_reaper,cd_theater_preset_name(ho.theater_preset));
        cc_accept_config(&next);
        cc_request_reload();
        cc_sync_menu_ui();
    }
}

/* Only the two delay floats are owned. Read through the SDK's unique record
 * resolver each pass; a relocated table is treated as a new record. */
static int cc_read(uintptr_t at,CcDecay *out)
{return at&&hf->read(at,out,sizeof *out)==0;}
static void cc_conflict(CcEdit *edit,const CcTarget *target,const CcDecay *live)
{
    edit->settled=1;
    if(!edit->conflict)
    {edit->conflict=1;++conflicts;
     hf->log(self,"Cleanup conflict %016llX: live delays=%.3f/%.3f mode=%u; native=%.3f/%.3f; preserving unexpected template bytes; another mod or changed game data may be responsible",(unsigned long long)target->resource,live->minimum,live->maximum,live->mode,target->native.minimum,target->native.maximum);}
}
static void cc_apply(unsigned index)
{
    int host_role=cc_is_host_or_solo();
    const CcTarget *target=&cc_targets[index];CcEdit *edit=&edits[index];
    /* Strict fail-closed no-op on joiners/none unless restoring our own prior held edit after demotion. */
    if(!host_role&&!edit->held&&!edit->pending){edit->settled=1;return;}
    uint32_t stride=0;
    uintptr_t record=hf->component_record("HealthComponentData",target->resource,&stride);
    if(!record||stride!=CC_STRIDE||record>UINTPTR_MAX-CC_STRIDE)return;
    uintptr_t at=record+CC_DECAY_OFFSET;
    if(at!=edit->address)
    {memset(edit,0,sizeof *edit);edit->address=at;if(!host_role){edit->settled=1;return;}}
    CcDecay live;
    if(!cc_read(at,&live))return;
    if(edit->pending)
    {
        if(!memcmp(&live,&edit->attempted,sizeof live))
        {edit->expected=live;edit->held=memcmp(&live,&edit->before,sizeof live)!=0;}
        else if(memcmp(&live,&edit->expected,sizeof live)){cc_conflict(edit,target,&live);return;}
        edit->pending=0;
    }
    /* Full structural guard includes mode, acceleration, auxiliary scalar,
     * flag, padding and both native delay values before ownership is taken. */
    if(!edit->held)
    {
        if(!host_role){edit->settled=1;return;}
        if(memcmp(&live,&target->native,sizeof live)){cc_conflict(edit,target,&live);return;}
        edit->before=live;edit->expected=live;edit->conflict=0;
    }
    else if(memcmp(&live,&edit->expected,sizeof live))
    {
        /* A failed SDK write can leave the original bytes intact. */
        if(!memcmp(&live,&edit->before,sizeof live)){edit->held=0;edit->expected=live;edit->conflict=0;}
        else {cc_conflict(edit,target,&live);return;}
    }
    CcDecay want=edit->before;
    int on=cc_cleanup_enabled();
    if(on)
    {
        float req_s=cc_effective_decay_seconds();
        float seconds=fminf(req_s,edit->before.maximum);
        want.minimum=seconds;want.maximum=seconds;
    }
    if(!memcmp(&live,&want,sizeof live)){edit->held=on;edit->expected=want;edit->settled=1;return;}
    /* Retain both possible outcomes until read-back resolves a failed write
     * or read. Never recapture our edited delay as the native baseline. */
    edit->attempted=want;edit->pending=1;
    int failed=hf->write(at+offsetof(CcDecay,minimum),&want.minimum,2*sizeof(float));
    CcDecay after;if(!cc_read(at,&after))return;
    if(memcmp(&after,&want,sizeof after))
    {
        if(!memcmp(&after,&edit->expected,sizeof after)){edit->pending=0;return;}
        cc_conflict(edit,target,&after);return;
    }
    (void)failed;edit->pending=0;edit->expected=want;edit->held=on;edit->settled=1;
    if(on)++writes;else ++restores;
}
static void cc_restore_all_now(void)
{
    for(unsigned i=0;i<CC_COUNT(cc_targets);++i)
    {
        if(edits[i].held||edits[i].pending)
        {
            edits[i].settled=0;
            cc_apply(i);
        }
    }
    template_complete=1;
    for(unsigned i=0;i<CC_COUNT(edits);++i)if(edits[i].held||edits[i].pending){template_complete=0;break;}
}
static void cc_tick(HfMod *mod,float dt,void *user)
{
    (void)mod;(void)dt;(void)user;
    if(cc_stopped())return;
    InterlockedExchange(&factory_thread,(LONG)GetCurrentThreadId());
    uint32_t state=hf->game_state(self);int mission=state==HF_STATE_MISSION;
    if(!mission&&factory_in_mission)cc_clear_factory_lives();
    factory_in_mission=mission;

    int is_host=cc_is_host_or_solo();
    if(!is_host)
    {
        if(g_was_host)
        {
            g_was_host=0;
            cc_clear_factory_lives();
            factory_credit=0;
            cc_restore_all_now();
            hf->log(self,"Host role lost; restored decay timers and paused corpse cleanup on joiner");
        }
        else if(cc_any_held())
        {
            cc_restore_all_now();
        }
        return;
    }
    if(!g_was_host)
    {
        g_was_host=1;
        cc_request_reload();
    }

    uint64_t now=GetTickCount64();
    if(config_log_due&&now>=config_log_due)
    {config_log_due=0;hf->log(self,"Cleanup %s; delay=%d seconds (120=original settings); extra cleanup=%s; menu provider manages saved values",config.enabled?"enabled":"disabled",config.seconds,config.extra_cleanup?"on":"off");}
    if(now>=g_next_host_opts_poll){g_next_host_opts_poll=now+1000;cc_poll_host_options();}
    {
        int cur_live_theater=cc_theater_active_now();
        int cur_live_turbo=cc_live_turbo_active();
        if(cur_live_theater!=g_prev_live_theater||cur_live_turbo!=g_prev_live_turbo)
        {
            g_prev_live_theater=cur_live_theater;
            g_prev_live_turbo=cur_live_turbo;
            cc_request_reload();
        }
    }
    /* Callbacks apply changes immediately; provider discovery/reconnection
     * only needs a low-frequency poll, including while on the ship. */
    if(now>=next_menu_poll){next_menu_poll=now+250;cc_menu(now);}
    if(cc_take_reload())
    {
        cursor=0;next_pass=0;template_complete=0;
        for(unsigned i=0;i<CC_COUNT(edits);++i)edits[i].settled=0;
    }
    if(template_complete||now<next_pass)return;
    /* One resolver call per frame while applying a change. Settled authored
     * data is left idle until settings or ENTITIES_READY invalidate it. */
    while(cursor<CC_COUNT(cc_targets))
    {unsigned index=cursor++;if(!edits[index].settled){cc_apply(index);break;}}
    if(cursor==CC_COUNT(cc_targets))
    {
        static unsigned previous_writes=UINT32_MAX,previous_restores=UINT32_MAX,previous_conflicts=UINT32_MAX;
        if(writes!=previous_writes||restores!=previous_restores||conflicts!=previous_conflicts)
        {hf->log(self,"Cleanup templates: writes=%u restores=%u conflicts=%u; supported enemies=%u",writes,restores,conflicts,(unsigned)CC_COUNT(cc_targets));
         previous_writes=writes;previous_restores=restores;previous_conflicts=conflicts;}
        template_complete=1;
        for(unsigned i=0;i<CC_COUNT(edits);++i)if(!edits[i].settled){template_complete=0;break;}
        cursor=0;next_pass=now+1000;cc_show_config();
    }
}
static void cc_entities(HfMod *mod,HfEvent event,void *user)
{(void)mod;(void)event;(void)user;cc_request_reload();}

/* One resident startup instance. The SDK may retain queued timer tombstones
 * after cancellation, so neither this state nor the business context is reset
 * or freed. Only public startup/status operations are used by this client. */
static HfdStartup cc_dispatch_startup;
static volatile LONG cc_init_once;
enum { CC_DISPATCH_INITIALIZING, CC_DISPATCH_PUBLISHED, CC_DISPATCH_STOPPED };
static volatile LONG cc_dispatch_publication;
static DWORD cc_dispatch_initial_thread;
typedef struct CcDispatchBusiness {
    HfTickCallback callback;
    void *user;
    volatile LONG observed;
} CcDispatchBusiness;
static CcDispatchBusiness cc_dispatch_business={cc_tick,NULL,0};

/* Called only from init/control or the real business callback, never an SDK
 * timer. IDs are separate sampled observations, not OS thread counts. */
static void cc_dispatch_log(const char *stage,int result,LONG milestone,DWORD business_thread)
{
    HfdStartupStatus status={.size=sizeof status};
    if(hfd_startup_get_status(&cc_dispatch_startup,&status)!=HFD_OK)
    {hf->log(self,"DSP stage=%s result=%d status=UNOBSERVED",stage,result);return;}
    char timer_ids[HFD_STARTUP_THREAD_CAPACITY*11+1]={0};size_t used=0;
    for(unsigned i=0;i<HFD_STARTUP_THREAD_CAPACITY;++i)if(status.timer_threads[i])
    {
        int n=snprintf(timer_ids+used,sizeof timer_ids-used,"%s%u",used?",":"",status.timer_threads[i]);
        if(n<0||(size_t)n>=sizeof timer_ids-used){strcpy(timer_ids,"UNOBSERVED");break;}
        used+=(size_t)n;
    }
    if(!timer_ids[0])strcpy(timer_ids,"UNOBSERVED");
    hf->log(self,"DSP stage=%s result=%d plan=%u phase=%u route=%u reason=%u ready=%u stopped=%u owned=%u shared=%u raw=%u activation=%u timer_allocations=%u timer_wakes=%u initial_id=%lu control_id=%u timer_ids=%s timer_overflow=%u business_id=%lu milestone=%ld",
        stage,result,status.plan,status.phase,status.route,status.reason,status.ready,status.stopped,status.owned,
        status.shared_attempts,status.raw_attempts,status.activation_attempts,status.timer_allocations,status.timer_wakes,
        (unsigned long)cc_dispatch_initial_thread,status.initial_control_thread,timer_ids,status.timer_thread_overflow,
        (unsigned long)business_thread,(long)milestone);
}
static void cc_dispatch_tick(HfMod *mod,float dt,void *user)
{
    CcDispatchBusiness *business=(CcDispatchBusiness*)user;
    if(InterlockedCompareExchange(&cc_dispatch_publication,0,0)!=CC_DISPATCH_PUBLISHED||
       cc_stopped()||!business||!business->callback)return;
    /* Two business snapshots only; stop growing the observation counter at 120. */
    if(InterlockedCompareExchange(&business->observed,0,0)<120)
    {
        LONG n=InterlockedIncrement(&business->observed);
        if(n==1||n==120)cc_dispatch_log("business",HFD_OK,n,GetCurrentThreadId());
    }
    business->callback(mod,dt,business->user);
}
static void cc_dispatch_inactive(const char *stage,int result)
{
    InterlockedExchange(&cc_dispatch_publication,CC_DISPATCH_STOPPED);
    InterlockedExchange(&stopped,1);
    int cancelled=hfd_startup_cancel(&cc_dispatch_startup);
    cc_dispatch_log(stage,result,0,0);
    hf->log(self,"DSP cleanup inactive; cancellation=%d; resident state retained; restart required",cancelled);
}
static void cc_session_end(HfMod *mod,HfEvent event,void *user)
{
    (void)mod;(void)event;(void)user;
    InterlockedExchange(&cc_dispatch_publication,CC_DISPATCH_STOPPED);
    InterlockedExchange(&stopped,1);
}
HF_MOD_EXPORT int hellforge_mod_init(const HfApi *api,HfMod *mod)
{
    if(!api||!mod||api->version<16||api->size<offsetof(HfApi,code_matches)+sizeof api->code_matches||
       !api->log||!api->build_id||!api->mod_folder||!api->component_record||!api->read||!api->write||
       !api->on||!api->on_game_thread||!api->hook||!api->code_matches||!api->module_base||
       !api->game_state||!api->session)return 1;
    if(InterlockedCompareExchange(&cc_init_once,1,0))return 0;
    hf=api;self=mod;
    const char *build=api->build_id();
    if(!build||strcmp(build,CC_BUILD)){hf->log(self,"Unsupported game build; requires " CC_BUILD);return 1;}
    if(!cc_load())return 1;
    g_was_host=cc_is_host_or_solo();
    cc_dispatch_initial_thread=GetCurrentThreadId();
    HfdClient client={sizeof client,0,mod,cc_dispatch_tick,&cc_dispatch_business};
    const HfdRecoveryLimits limits={8,0,8,2000,3000};
    int startup=hfd_startup_begin(&cc_dispatch_startup,api,&client,&limits,250);
    cc_dispatch_log("begin",startup,0,0);
    if(startup!=HFD_OK&&startup!=HFD_PENDING)
    {cc_dispatch_inactive("begin-failed",startup);return 0;}
    if(api->on(mod,HF_EVENT_ENTITIES_READY,cc_entities,NULL)||
       api->on(mod,HF_EVENT_GAME_LOADED,cc_factory_loaded,NULL)||
       api->on(mod,HF_EVENT_SESSION_END,cc_session_end,NULL))
    {cc_dispatch_inactive("event-failed",HFD_REFUSED);return 0;}
    hf->log(self,"INIT Faster Corpse Cleanup " CC_VERSION "; build " CC_BUILD " sdk=16; host_gated=1 enabled=%d delay=%d extra_cleanup=%d",
        config.enabled,config.seconds,config.extra_cleanup);
    startup=hfd_startup_ready(&cc_dispatch_startup);
    cc_dispatch_log("ready",startup,0,0);
    if(startup!=HFD_OK&&startup!=HFD_PENDING)
    {cc_dispatch_inactive("ready-failed",startup);return 0;}
    (void)InterlockedCompareExchange(&cc_dispatch_publication,
        CC_DISPATCH_PUBLISHED,CC_DISPATCH_INITIALIZING);
    return 0;
}
