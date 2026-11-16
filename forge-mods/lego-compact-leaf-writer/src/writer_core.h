#ifndef WRITER_CORE_H
#define WRITER_CORE_H
#include "envelope.h"
#include "shadow_math.h"
#include "leaf_plan.h"
#include "coop_avatar_registry.h"
#define GW_SLOTS 32u
#define GW_SAMPLER_BYTES 8192u
enum gw_failure {GW_OK=0,GW_CAPACITY=1,GW_REENTRY=2,GW_CAPTURE=3,GW_MATH=4,GW_NATIVE=5,GW_ENVIRONMENT=6,GW_SUSPICIOUS=7,GW_COUNTER=8,GW_CHAIN=9,GW_PLAN=10,GW_LEAF=11,GW_RECHECK=12};
typedef uint32_t (*gw_leaf_fn)(void *,const void *,const void *,const void *,const uint32_t *,const uint32_t *,uint32_t);
typedef uint32_t (*gw_remove_fn)(void *,const void *,void *);
typedef struct {uint32_t route,expected,seen,replaced,failure,outer_args_unchanged,environment_ok,plan_blocks,plan_bytes,recheck_ok,allocation_ok,reserved;} gw_receipt;
#define GW_RECEIPT_OFFSET (SM_RAW_BYTES+sizeof(sm_result))
typedef struct {volatile LONG state;se_record record;cm_workspace work;lp_plan plan;int32_t sampler_status;uint32_t sampler_used;unsigned char sampler[GW_SAMPLER_BYTES];} gw_slot;
/* Resident first-fatal evidence. Identity is readable only after state>=2;
   copied record/plan only after state>=4. Never reused or reclaimed. */
typedef struct {volatile LONG state;LONG reason;uint64_t sequence;uint32_t record_owned;
 int32_t sampler_status;uint32_t sampler_used;se_record record;lp_plan plan;unsigned char sampler[GW_SAMPLER_BYTES];} gw_fault;
struct gw_state;
typedef int (*gw_resolve_fn)(struct gw_state *,void *,void *,void *,uintptr_t,LONG64,se_target *,coop_actor_kind *);
typedef struct gw_state {
 se_state *binding;const cm_rig *rig;uintptr_t engine;
 void *approved_next; /* Set once after successful hook registration, before readiness. */
 void *volatile leaf_next;void *approved_leaf_next;
 void *volatile remove_next;void *approved_remove_next;
 gw_resolve_fn resolve_target;
 void (*retire_handle)(struct gw_state *,uintptr_t,uint32_t);
 void *resolve_user;
 int (*plan_capture)(lp_plan *,se_record *,int,void *);void *plan_user;
 /* Copies gate-owned completed sampler telemetry; no reads/logging/native call. */
 void (*copy_sampler)(unsigned char *,void *);void *sampler_user;
 se_observe_fn capture;const void *user;
 void (*completed)(const se_record *,void *);void *completed_user;
 SRWLOCK metadata;
 volatile LONG ready,fatal,teardown,diagnostics_closed;
 volatile LONG64 calls,matched,corrected,failed,unclassified,warmup,inflight,peak,first_failure_sequence;
 volatile LONG64 retires,aba_rebinds,dynamic_admissions,local_hits,remote_hits,preview_hits;
 volatile LONG64 passthrough_retired,passthrough_plan,passthrough_recheck,fast_evicts,capture_teardown_evicts,capture_mutation_skips;
 gw_slot slots[GW_SLOTS];
 gw_fault fault;
} gw_state;
/* Zeroed resident state, valid immutable rig, already initialized SDK next
   owner. Static tests publish se targets before gw.ready; dynamic co-op mode
   sets resolve_target/retire_handle and admits targets dynamically. */
void gw_initialize(gw_state *,se_state *,const cm_rig *,uintptr_t,se_observe_fn,const void *,void (*)(const se_record *,void *),void *);
void gw_fail(gw_state *,LONG,LONG64);
void gw_retire_handle(gw_state *,uintptr_t,uint32_t);
uint32_t gw_dispatch(gw_state *,void *,void *,void *,uintptr_t);
uint32_t gw_leaf_dispatch(gw_state *,void *,const void *,const void *,const void *,const uint32_t *,const uint32_t *,uint32_t,uintptr_t);
#endif
