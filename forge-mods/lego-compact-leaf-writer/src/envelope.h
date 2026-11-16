#ifndef SE_ENVELOPE_H
#define SE_ENVELOPE_H
#include <stdint.h>
#include <stddef.h>
#include <windows.h>
#define SE_TARGETS 256u
#define SE_TICKETS 64u
#define SE_SLOTS 8u
#define SE_PAYLOAD (60u*1024u)
typedef uint32_t (*se_next_fn)(void *,void *,void *);
typedef enum {SE_FREE=0,SE_WRITING,SE_INFLIGHT,SE_READY,SE_READING,SE_ABANDONED,SE_RELEASING} se_slot_state;
typedef enum {SE_OK=0,SE_ARGUMENT,SE_ALREADY,SE_STOPPED,SE_TARGET_ERROR} se_status;
typedef struct {uintptr_t mesh;uint64_t token;uint8_t identity[64];} se_target;
typedef struct {
 uintptr_t mesh,pool,optional_graph,caller;
 uint32_t thread_id,entry_mxcsr,entry_last_error;
} se_call;
typedef struct {int32_t status;uint32_t used;} se_observation_result;
struct se_state;
typedef struct {
 const struct se_state *envelope;
 const se_target *target;
 const se_call *call;
 uint8_t *payload;
 uint32_t capacity;
} se_observe_context;
/* Synchronous trusted callback. Writes only its payload/result; no game/next
   calls, retained pointers, async writers, exceptions/longjmp or gate release. */
typedef void (*se_observe_fn)(const se_observe_context *,se_observation_result *,const void *user);
typedef struct {
 uint64_t sequence;
 se_target target;
 se_call call;
 uint64_t active_at_entry,overlap_epoch_before,overlap_epoch_return;
 int32_t capture_status;
 uint32_t payload_used,result,return_mxcsr,return_last_error,abandoned;
 uint32_t delegate_mxcsr; /* Actual readback immediately before unchanged next. */
 _Alignas(16) uint8_t payload[SE_PAYLOAD];
} se_record;
typedef struct {volatile LONG state;se_record record;} se_slot;
typedef struct {volatile LONG64 active,overlap_epoch;} se_overlap;
typedef struct se_state {
 volatile LONG initialized,published,stopped,gate;
 /* SDK-owned mutable next slot. Never clear after hook reachability. */
 void *volatile next;
 se_observe_fn observe;
 const void *user;
 /* max_per_target limits callback attempts (including refused captures), not
    accepted records. attempts/target_attempts/sequence require gate ownership
    when inspected during operation. Limits/targets are immutable after setup. */
 uint32_t target_count,max_attempts,max_per_target,attempts;
 uint32_t target_attempts[SE_TARGETS];
 uint64_t sequence;
 se_target targets[SE_TARGETS];
 volatile LONG tickets[SE_TICKETS];
 se_slot slots[SE_SLOTS];
 /* Matching instrumented detour intervals, including observation bypass/stop.
    Positive evidence only; unchanged epoch is NOT native exclusion/quiescence.
    Separate atomics can be observed between updates. Nonreturn retains active. */
 se_overlap overlap[SE_TARGETS];
 volatile LONG64 calls,bypass,callback_calls,completed;
} se_state;
typedef struct {const se_record *record;uint32_t index;uint64_t sequence;} se_view;

/* Caller provides zero-initialized, aligned, process-resident storage; once only
   before hook installation. next may be NULL here only if SDK will initialize
   it before making the detour reachable. No successful dispatch accepts NULL.
   Both caps count callback attempts, including refusals. */
se_status se_initialize(se_state *,se_observe_fn,const void *user,uint32_t max_attempts,uint32_t max_per_target);
/* Copies <=SE_TARGETS unique nonzero mesh entries for static test fixtures. Dynamic
   multi-actor mode may operate without calling se_publish. */
se_status se_publish(se_state *,const se_target *,uint32_t count);
void **se_next_slot(se_state *);
/* PRECONDITION: initialized state and current next are valid/resident. actual
   detour captures caller=_ReturnAddress() itself before helper calls. This
   helper's incoming MXCSR/LastError must still equal actual detour entry state.
   Original faults/nonreturns or callback faults are not recovered here. */
uint32_t se_dispatch(se_state *,void *mesh,void *pool,void *optional_graph,uintptr_t caller);
void se_stop(se_state *); /* monotonic; never waits, frees or clears records/next */
int se_is_stopped(const se_state *);
/* Shared tick/observer gate. One try only. Do not own it across a game call or
   file IO. Callback already owns it and must not release/reacquire it. */
int se_try_gate(se_state *);
int se_release_gate(se_state *);
/* Claim completed READY or ABANDONED records only. Single-owner borrowed view,
   valid until matching release; do not use or duplicate a released view. No
   allocation/copy/IO; one bounded scan. Stop does not revoke an existing view.
   A refused release leaves view owned; caller may retry on a later drain turn. */
int se_try_take(se_state *,se_view *);
int se_release(se_state *,se_view *);
#endif
