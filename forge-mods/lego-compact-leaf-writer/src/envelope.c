#include "envelope.h"
#include <xmmintrin.h>
#include <string.h>
#define SE_INITIALIZED 0x53450001L
#define SE_BAD_LENGTH (-2147483647)
#define SE_UNSET (-2147483646)
_Static_assert(offsetof(se_state,next)%8==0,"SDK next must be aligned");
_Static_assert(sizeof(se_record)<=64u*1024u,"bounded record");
_Static_assert(SE_TICKETS==64,"thread ticket hash");

static LONG load_long(const volatile LONG *p) {
 return InterlockedCompareExchange((volatile LONG *)p,0,0);
}
static LONG64 load_long64(const volatile LONG64 *p) {
 return InterlockedCompareExchange64((volatile LONG64 *)p,0,0);
}
static int overlap(const void *a,size_t an,const void *b,size_t bn) {
 uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;
 return an>UINTPTR_MAX-x || bn>UINTPTR_MAX-y || (x<y+bn && y<x+an);
}
se_status se_initialize(se_state *s,se_observe_fn observe,const void *user,uint32_t attempts,uint32_t per_target) {
 if(!s || !observe || !attempts || attempts>4096u || !per_target || per_target>4096u) return SE_ARGUMENT;
 if(InterlockedCompareExchange(&s->initialized,-1,0)!=0) return SE_ALREADY;
 /* All other storage was zero-initialized by the caller before any reachability. */
 s->observe=observe;s->user=user;s->max_attempts=attempts;s->max_per_target=per_target;
 InterlockedExchange(&s->initialized,SE_INITIALIZED);return SE_OK;
}
se_status se_publish(se_state *s,const se_target *targets,uint32_t count) {
 if(!s || !targets || count==0 || count>SE_TARGETS || load_long(&s->initialized)!=SE_INITIALIZED) return SE_ARGUMENT;
 if(overlap(s,sizeof *s,targets,(size_t)count*sizeof *targets)) return SE_ARGUMENT;
 if(se_is_stopped(s)) return SE_STOPPED;
 for(uint32_t i=0;i<count;i++) {
  if(!targets[i].mesh) return SE_TARGET_ERROR;
  for(uint32_t j=0;j<i;j++) if(targets[i].mesh==targets[j].mesh) return SE_TARGET_ERROR;
 }
 if(InterlockedCompareExchange(&s->published,-1,0)!=0) return SE_ALREADY;
 memcpy(s->targets,targets,(size_t)count*sizeof *targets);s->target_count=count;
 if(se_is_stopped(s)) {InterlockedExchange(&s->published,2);return SE_STOPPED;}
 InterlockedExchange(&s->published,1);return SE_OK;
}
void **se_next_slot(se_state *s) {return s?(void **)&s->next:NULL;}
void se_stop(se_state *s) {if(s) InterlockedExchange(&s->stopped,1);}
int se_is_stopped(const se_state *s) {return !s || load_long(&s->stopped)!=0;}
int se_try_gate(se_state *s) {
 if(!s) return 0;
 LONG id=(LONG)GetCurrentThreadId();
 return InterlockedCompareExchange(&s->gate,id,0)==0;
}
int se_release_gate(se_state *s) {
 if(!s) return 0;
 LONG id=(LONG)GetCurrentThreadId();
 return InterlockedCompareExchange(&s->gate,0,id)==id;
}
static se_slot *reserve(se_state *s) {
 for(uint32_t i=0;i<SE_SLOTS;i++)
  if(InterlockedCompareExchange(&s->slots[i].state,SE_WRITING,SE_FREE)==SE_FREE) return &s->slots[i];
 return NULL;
}
uint32_t se_dispatch(se_state *s,void *mesh,void *pool,void *graph,uintptr_t caller) {
 const uint32_t entry_csr=_mm_getcsr();
 const DWORD entry_error=GetLastError();
 const DWORD tid=GetCurrentThreadId();
 se_slot *slot=NULL;
 uint32_t ticket_index=tid&(SE_TICKETS-1u);
 uint32_t target=SE_TARGETS;
 uint64_t active_entry=0,overlap_before=0,overlap_return=0;
 int ticket_owned=0;
 InterlockedIncrement64(&s->calls);
 if(load_long(&s->published)==1) {
  for(uint32_t i=0;i<s->target_count;i++) if(s->targets[i].mesh==(uintptr_t)mesh) {target=i;break;}
  if(target<SE_TARGETS) {
   overlap_before=(uint64_t)load_long64(&s->overlap[target].overlap_epoch);
   active_entry=(uint64_t)InterlockedIncrement64(&s->overlap[target].active);
   if(active_entry>1) InterlockedIncrement64(&s->overlap[target].overlap_epoch);
  }
 }
 if(!se_is_stopped(s) && target<SE_TARGETS) {
  if(InterlockedCompareExchange(&s->tickets[ticket_index],(LONG)tid,0)==0) {
   ticket_owned=1;
   if(se_try_gate(s)) {
    if(!se_is_stopped(s) && s->attempts<s->max_attempts && s->target_attempts[target]<s->max_per_target) {
     slot=reserve(s);
     if(slot) {
      s->attempts++;s->target_attempts[target]++;
      /* Clearing the fixed owned record prevents stale reused-payload exposure. */
      memset(&slot->record,0,sizeof slot->record);
      slot->record.sequence=++s->sequence;
      slot->record.target=s->targets[target];
      slot->record.call=(se_call){(uintptr_t)mesh,(uintptr_t)pool,(uintptr_t)graph,caller,tid,entry_csr,entry_error};
      slot->record.active_at_entry=active_entry;slot->record.overlap_epoch_before=overlap_before;
      se_observation_result observed={SE_UNSET,0};
      const se_observe_context ctx={s,&slot->record.target,&slot->record.call,slot->record.payload,SE_PAYLOAD};
      InterlockedIncrement64(&s->callback_calls);
      s->observe(&ctx,&observed,s->user);
      slot->record.capture_status=observed.used<=SE_PAYLOAD?observed.status:SE_BAD_LENGTH;
      slot->record.payload_used=observed.used<=SE_PAYLOAD?observed.used:0;
     }
    }
    se_release_gate(s);
   }
  }
 }
 if(slot) InterlockedExchange(&slot->state,SE_INFLIGHT);
 else InterlockedIncrement64(&s->bypass);
 /* Load mutable next only at delegation, after observation. No stale cached link. */
 se_next_fn next=(se_next_fn)InterlockedCompareExchangePointer(&s->next,NULL,NULL);
 SetLastError(entry_error);_mm_setcsr(entry_csr);
 const uint32_t delegate_csr=_mm_getcsr();
 const uint32_t result=next(mesh,pool,graph);
 const uint32_t return_csr=_mm_getcsr();
 const DWORD return_error=GetLastError();
 if(target<SE_TARGETS) {
  InterlockedDecrement64(&s->overlap[target].active);
  overlap_return=(uint64_t)load_long64(&s->overlap[target].overlap_epoch);
 }
 if(slot) {
  slot->record.delegate_mxcsr=delegate_csr;
  slot->record.overlap_epoch_return=overlap_return;
  slot->record.result=result;slot->record.return_mxcsr=return_csr;slot->record.return_last_error=return_error;
  slot->record.abandoned=se_is_stopped(s)?1u:0u;
  InterlockedExchange(&slot->state,slot->record.abandoned?SE_ABANDONED:SE_READY);
  InterlockedIncrement64(&s->completed);
 }
 if(ticket_owned) InterlockedExchange(&s->tickets[ticket_index],0);
 SetLastError(return_error);_mm_setcsr(return_csr);
 return result;
}
int se_try_take(se_state *s,se_view *view) {
 if(!s || !view || load_long(&s->initialized)!=SE_INITIALIZED || overlap(s,sizeof *s,view,sizeof *view)) return 0;
 for(uint32_t i=0;i<SE_SLOTS;i++) {
  LONG state=load_long(&s->slots[i].state);
  if(state!=SE_READY && state!=SE_ABANDONED) continue;
  if(InterlockedCompareExchange(&s->slots[i].state,SE_READING,state)!=state) continue;
  *view=(se_view){&s->slots[i].record,i,s->slots[i].record.sequence};return 1;
 }
 return 0;
}
int se_release(se_state *s,se_view *view) {
 if(!s || !view || view->index>=SE_SLOTS || overlap(s,sizeof *s,view,sizeof *view)) return 0;
 se_slot *slot=&s->slots[view->index];
 if(view->record!=&slot->record) return 0;
 /* Own the state before reading generation: a stale view must not race record
    clearing/reuse, or free a newly acquired generation after a stale check. */
 if(InterlockedCompareExchange(&slot->state,SE_RELEASING,SE_READING)!=SE_READING) return 0;
 if(view->sequence!=slot->record.sequence) {InterlockedExchange(&slot->state,SE_READING);return 0;}
 InterlockedExchange(&slot->state,SE_FREE);
 view->record=NULL;return 1;
}
