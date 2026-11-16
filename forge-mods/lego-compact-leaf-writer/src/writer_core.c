#include "writer_core.h"
#include <intrin.h>
#include <string.h>
#include <limits.h>
static _Thread_local unsigned gw_metadata_depth;
typedef struct gw_frame {struct gw_frame *previous;gw_state *state;gw_slot *slot;gw_receipt *receipt;
 uintptr_t factor[2],reservation;DWORD owner;uint64_t serial;unsigned depth;int active;} gw_frame;
static _Thread_local gw_frame *gw_current;
static LONG gw_load(volatile LONG *p){return InterlockedCompareExchange(p,0,0);}
void gw_fail(gw_state *s,LONG reason,LONG64 sequence) {
 if(InterlockedCompareExchange(&s->fatal,reason,0)==0){
#ifdef GW_TEST_FATAL_CAS_PAUSE
  extern void gw_test_fatal_cas_pause(gw_state *);gw_test_fatal_cas_pause(s);
#endif
  InterlockedExchange64(&s->first_failure_sequence,sequence);
  InterlockedExchange(&s->fault.state,1);
  s->fault.reason=reason;s->fault.sequence=(uint64_t)sequence;
  s->fault.record_owned=gw_current && gw_current->state==s && gw_current->serial==(uint64_t)sequence && gw_current->slot!=NULL;
  InterlockedExchange(&s->fault.state,2);
 }
}
static LONG64 gw_inc(gw_state *s,volatile LONG64 *p) {
 LONG64 value=InterlockedIncrement64(p);if(value<=0)gw_fail(s,GW_COUNTER,0);return value;
}
void gw_initialize(gw_state *s,se_state *binding,const cm_rig *rig,uintptr_t engine,se_observe_fn capture,const void *user,
                   void (*completed)(const se_record *,void *),void *completed_user) {
 /* Initialization is once-only before reachability; storage is never freed. */
 s->binding=binding;s->rig=rig;s->engine=engine;s->capture=capture;s->user=user;
 s->completed=completed;s->completed_user=completed_user;InitializeSRWLock(&s->metadata);
}
void gw_retire_handle(gw_state *s,uintptr_t world,uint32_t handle) {
 if(!s || !handle || handle==0xFFFFFFu || !s->retire_handle)return;
 s->retire_handle(s,world,handle);
}
uint32_t gw_dispatch(gw_state *s,void *mesh,void *pool,void *graph,uintptr_t caller) {
 uint32_t entry_csr=_mm_getcsr();DWORD entry_error=GetLastError();
 LONG64 sequence=gw_inc(s,&s->calls);gw_slot *slot=NULL;se_record *record=NULL;
 int substitution=0,matched_target=0,retired_mid_call=0;se_target resolved_target;memset(&resolved_target,0,sizeof resolved_target);
 /* Every invocation masks its ancestor immediately, including warmup/refusal. */
 gw_frame frame={0};frame.previous=gw_current;frame.state=s;frame.owner=GetCurrentThreadId();frame.serial=(uint64_t)sequence;
 frame.depth=frame.previous?frame.previous->depth+1:1;gw_current=&frame;
 if(!gw_load(&s->ready))gw_inc(s,&s->warmup);
 else {
  if(s->resolve_target) {
   coop_actor_kind kind=COOP_ACTOR_REJECTED;
   if(s->resolve_target(s,mesh,pool,graph,caller,sequence,&resolved_target,&kind)) {
    matched_target=1;
    if(kind==COOP_ACTOR_LOCAL_PLAYER)gw_inc(s,&s->local_hits);
    else if(kind==COOP_ACTOR_REMOTE_PLAYER)gw_inc(s,&s->remote_hits);
    else if(kind==COOP_ACTOR_PREVIEW_OR_CORPSE)gw_inc(s,&s->preview_hits);
   } else {
    gw_inc(s,&s->unclassified);
   }
  } else {
   unsigned target=SE_TARGETS;
   for(unsigned i=0;i<s->binding->target_count;i++)if(s->binding->targets[i].mesh==(uintptr_t)mesh){target=i;resolved_target=s->binding->targets[i];matched_target=1;break;}
   if(target==SE_TARGETS) {
    gw_inc(s,&s->unclassified);
    if(graph)for(unsigned i=0;i<s->binding->target_count;i++) {
     uintptr_t normal;memcpy(&normal,s->binding->targets[i].identity+56,8);
     if(normal==(uintptr_t)graph){gw_fail(s,GW_SUSPICIOUS,sequence);break;}
    }
   }
  }
  if(matched_target) {
   gw_inc(s,&s->matched);
   for(unsigned i=0;i<GW_SLOTS;i++)if(InterlockedCompareExchange(&s->slots[i].state,1,0)==0){slot=&s->slots[i];break;}
   if(!slot)gw_fail(s,GW_CAPACITY,sequence);
   else {
    LONG64 active=gw_inc(s,&s->inflight),peak=InterlockedCompareExchange64(&s->peak,0,0);
    while(active>peak){LONG64 prior=InterlockedCompareExchange64(&s->peak,active,peak);if(prior==peak)break;peak=prior;}
    record=&slot->record;memset(record,0,sizeof *record);memset(&slot->plan,0,sizeof slot->plan);record->sequence=(uint64_t)sequence;
    frame.slot=slot;slot->sampler_status=-1000;slot->sampler_used=0;memset(slot->sampler,0,sizeof slot->sampler);
    record->target=resolved_target;
    record->call=(se_call){(uintptr_t)mesh,(uintptr_t)pool,(uintptr_t)graph,caller,GetCurrentThreadId(),entry_csr,entry_error};
    se_observation_result observed={-1000,0};
    if(gw_metadata_depth)gw_fail(s,GW_REENTRY,sequence);
    else {
     gw_metadata_depth=1;AcquireSRWLockExclusive(&s->metadata);
     se_observe_context context={s->binding,&record->target,&record->call,record->payload,SE_PAYLOAD};
     s->capture(&context,&observed,s->user);
     slot->sampler_status=observed.status;slot->sampler_used=observed.used;
     if(s->copy_sampler)s->copy_sampler(slot->sampler,s->sampler_user);
     if(!observed.status && observed.used==SM_RAW_BYTES && (!s->plan_capture || !s->plan_capture(&slot->plan,record,0,s->plan_user)))observed.status=-1002;
     ReleaseSRWLockExclusive(&s->metadata);gw_metadata_depth=0;
    }
    if(observed.status==-1003) {
     /* Concurrent 1E7CD0/unit generation retirement between resolve and capture: pass through untouched. */
     retired_mid_call=1;substitution=0;
    } else {
     record->capture_status=observed.used<=SE_PAYLOAD?observed.status:-1001;
     record->payload_used=observed.used<=SE_PAYLOAD?observed.used:0;
     sm_result *math=(sm_result*)(record->payload+SM_RAW_BYTES);
     uint64_t resource;memcpy(&resource,record->target.identity+8,8);
     sm_call call={(uintptr_t)mesh,(uintptr_t)pool,(uintptr_t)graph,caller,s->engine,resource,(uint32_t)(record->target.token%8),entry_csr};
     sm_evaluate(s->rig,&call,record->payload,record->capture_status?0:record->payload_used,&slot->work,math);
     if(record->capture_status || record->payload_used!=SM_RAW_BYTES)gw_fail(s,GW_CAPTURE,sequence);
     else if(!math->math_ok)gw_fail(s,GW_MATH,sequence);
     else {
      gw_metadata_depth=1;AcquireSRWLockExclusive(&s->metadata);
      int checked=s->plan_capture && s->plan_capture(&slot->plan,record,1,s->plan_user);
      ReleaseSRWLockExclusive(&s->metadata);gw_metadata_depth=0;
      if(!checked)gw_fail(s,GW_RECHECK,sequence);else substitution=1;
     }
     gw_receipt *receipt=(gw_receipt*)(record->payload+GW_RECEIPT_OFFSET);
     receipt->route=graph?2:1;receipt->expected=slot->plan.groups*2;receipt->outer_args_unchanged=1;
     receipt->plan_blocks=slot->plan.blocks;receipt->plan_bytes=slot->plan.bytes;receipt->recheck_ok=(uint32_t)substitution;
     frame.slot=slot;frame.receipt=receipt;
     /* The private header is diagnostic only; outer R8 is never replaced. */
     uintptr_t supplied=(uintptr_t)graph;memcpy(math->reserved,&supplied,8);math->reserved[2]=0;
    }
   }
  }
 }
 se_next_fn next=(se_next_fn)InterlockedCompareExchangePointer(&s->binding->next,NULL,NULL);
 if(gw_load(&s->ready) && (void*)next!=s->approved_next) {
  gw_fail(s,GW_CHAIN,sequence);substitution=0;
  if(record){sm_result *math=(sm_result*)(record->payload+SM_RAW_BYTES);uintptr_t supplied=(uintptr_t)graph;
   memcpy(math->reserved,&supplied,8);math->reserved[2]=0;}
 }
 if(gw_load(&s->ready) && InterlockedCompareExchangePointer(&s->leaf_next,NULL,NULL)!=s->approved_leaf_next){gw_fail(s,GW_CHAIN,sequence);substitution=0;}
 if(gw_load(&s->ready) && s->approved_remove_next && InterlockedCompareExchangePointer(&s->remove_next,NULL,NULL)!=s->approved_remove_next){gw_fail(s,GW_CHAIN,sequence);substitution=0;}
 frame.active=substitution;
 SetLastError(entry_error);_mm_setcsr(entry_csr);uint32_t delegate_csr=_mm_getcsr();
 uint32_t result=next(mesh,pool,graph);
 uint32_t return_csr=_mm_getcsr();DWORD return_error=GetLastError();
 frame.active=0;
 if(substitution){gw_receipt *r=frame.receipt;lp_plan *p=&slot->plan;
  r->environment_ok=(delegate_csr==entry_csr);
  r->allocation_ok=frame.reservation>=p->pool_base && frame.reservation-p->pool_base==(uint64_t)result*16;
  if(r->failure || r->seen!=r->expected || r->replaced!=r->expected || !r->allocation_ok){r->failure=r->failure?r->failure:20;gw_fail(s,GW_LEAF,sequence);substitution=0;}
 }
 if(matched_target && !retired_mid_call) {
  if(delegate_csr!=entry_csr)gw_fail(s,GW_ENVIRONMENT,sequence);
  if(result==UINT32_MAX)gw_fail(s,GW_NATIVE,sequence);
  if(substitution && result!=UINT32_MAX && delegate_csr==entry_csr)gw_inc(s,&s->corrected);
  else gw_inc(s,&s->failed);
 }
 if(slot) {
  if(!retired_mid_call) {
   ((sm_result*)(record->payload+SM_RAW_BYTES))->reserved[2]=(uint32_t)substitution;
   record->result=result;record->delegate_mxcsr=delegate_csr;record->return_mxcsr=return_csr;record->return_last_error=return_error;
   if(gw_load(&s->fault.state)==2 && s->fault.record_owned && s->fault.sequence==record->sequence &&
      InterlockedCompareExchange(&s->fault.state,3,2)==2){
    s->fault.sampler_status=slot->sampler_status;s->fault.sampler_used=slot->sampler_used;
    memcpy(&s->fault.record,record,sizeof *record);memcpy(&s->fault.plan,&slot->plan,sizeof slot->plan);
    memcpy(s->fault.sampler,slot->sampler,sizeof slot->sampler);
    InterlockedExchange(&s->fault.state,4);
   }
  }
  gw_current=frame.previous;
  /* Completion callback only atomics/copies to its own diagnostic queue. */
  if(s->completed && !retired_mid_call)s->completed(record,s->completed_user);
  InterlockedDecrement64(&s->inflight);InterlockedExchange(&slot->state,0);
 }
 gw_current=frame.previous;
 SetLastError(return_error);_mm_setcsr(return_csr);return result;
}

uint32_t gw_leaf_dispatch(gw_state *s,void *dst,const void *factor,const void *graph,const void *bind,const uint32_t *palette,const uint32_t *map,uint32_t count,uintptr_t caller){
 uint32_t entry_csr=_mm_getcsr();DWORD entry_error=GetLastError();gw_frame *f=gw_current;
 const void *chosen=graph;int replace=0;
 gw_leaf_fn next=(gw_leaf_fn)InterlockedCompareExchangePointer(&s->leaf_next,NULL,NULL);
 if(f && f->active && f->state==s){
  lp_plan *p=&f->slot->plan;gw_receipt *r=f->receipt;unsigned at=r->seen++,group=at/2,t=at%2;
  int ok=!r->failure && f->owner==GetCurrentThreadId() && f->serial==f->slot->record.sequence && at<r->expected && group<p->groups &&
   caller==s->engine+(t?0x513054:0x512fea) && (uintptr_t)graph==p->arrays[t] &&
   (uintptr_t)bind==p->bind && (uintptr_t)map==p->map && (uintptr_t)palette==p->group[group].palette && count==p->group[group].count &&
   (uintptr_t)factor>=0x10000 && !((uintptr_t)factor%16) && (uintptr_t)dst>=p->group[group].offset[t];
  if(ok){
   uintptr_t base=(uintptr_t)dst-p->group[group].offset[t];
   if(at==0)f->reservation=base;
   ok=base==f->reservation && base>=p->pool_base && base>=0x10000 && base<0x0000800000000000ull &&
      !((base-p->pool_base)%16) && base-p->pool_base<=UINT32_MAX &&
      (uint64_t)p->output_count*48<=0x0000800000000000ull-base;
   if(!f->factor[t])f->factor[t]=(uintptr_t)factor;
   ok=ok && f->factor[t]==(uintptr_t)factor && (!t || (f->factor[0]<=UINTPTR_MAX-48 && f->factor[1]==f->factor[0]+48));
  }
  if((void*)next!=s->approved_leaf_next)ok=0;
  if(ok){sm_result *math=(sm_result*)(f->slot->record.payload+SM_RAW_BYTES);chosen=t?(const void*)math->previous:(const void*)math->current;replace=1;}
  else {r->failure=1;gw_fail(s,GW_LEAF,(LONG64)f->serial);}
 }
 /* Mask even off-context/nested leaf execution; never expose an ancestor. */
 gw_current=NULL;
 SetLastError(entry_error);_mm_setcsr(entry_csr);uint32_t delegate_csr=_mm_getcsr();
 uint32_t result=next(dst,factor,chosen,bind,palette,map,count);
 uint32_t return_csr=_mm_getcsr();DWORD return_error=GetLastError();gw_current=f;
 if(f && f->active && f->state==s){if(delegate_csr!=entry_csr){f->receipt->failure=2;gw_fail(s,GW_ENVIRONMENT,(LONG64)f->serial);}if(replace)f->receipt->replaced++;}
 SetLastError(return_error);_mm_setcsr(return_csr);return result;
}
