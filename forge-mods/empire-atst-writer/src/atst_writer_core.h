/* Core S2c leaf capture, 512B80/5B2990/1E7CD0 runtime dispatch, and fail-closed verification. */
#ifndef ATST_WRITER_CORE_H
#define ATST_WRITER_CORE_H

#include "atst_code_pins.h"
#include "atst_policy.h"
#include "atst_walker_registry.h"

#include <windows.h>
#include <stddef.h>
#include <stdint.h>

#define AT_PEER_LEAF_DLL "lego_compact_leaf_writer.dll"
#define AT_HOOK_PRIORITY (-10)

typedef uint32_t (*at_skin_job_fn)(void *mesh, void *pool, void *graph);
typedef uintptr_t (*at_peer_lookup_fn)(const char *module_name, size_t *out_size, void *user);
typedef uint32_t (*at_leaf_skin_fn)(
    void *dst,
    const void *factor,
    const void *graph,
    const void *bind,
    const void *palette,
    const void *map,
    uint32_t count);
typedef uint64_t (*at_render_remove_fn)(uintptr_t world, void *packet, uintptr_t aux);

typedef struct {
    uintptr_t palette;
    uint32_t count;
    uint32_t offset[2];
} at_lp_group;

typedef struct {
    uintptr_t graph;
    uintptr_t arrays[2];
    uintptr_t bind;
    uintptr_t map;
    uintptr_t pool_base;
    uint32_t mesh_node;
    uint32_t groups;
    uint32_t output_count;
    at_lp_group group[AT_SKIN_GROUPS_COUNT];
} at_lp_plan;

typedef struct {
    uint32_t seen;
    uint32_t expected;
    uint32_t replaced;
    uint32_t failure;
    uint32_t allocation_ok;
    uint32_t environment_ok;
} at_receipt;

typedef struct at_writer_state {
    SRWLOCK lock;
    volatile LONG ready;
    uintptr_t engine_base;
    at_rig rig;
    at_walker_registry reg;
    at_safe_read_fn read_fn;
    void *read_user;
    uintptr_t peer_leaf_base;
    size_t peer_leaf_size;
    at_peer_lookup_fn peer_lookup_fn;
    void *peer_lookup_user;

    at_skin_job_fn skin_next;
    at_skin_job_fn approved_skin_next;
    at_leaf_skin_fn leaf_next;
    at_leaf_skin_fn approved_leaf_next;
    at_render_remove_fn remove_next;
    at_render_remove_fn approved_remove_next;

    uint32_t substitutions;
    uint32_t passthroughs;
    uint32_t caller_rejects;
    uint32_t capture_rejects;
    uint32_t recheck_rejects;
    uint32_t retired_mid_call;
    uint32_t leaf_failures;
    uint32_t math_failures;
    uint32_t chain_failures;
    uint32_t env_failures;
} at_writer_state;

typedef struct {
    int active;
    at_writer_state *state;
    int slot_index;
    uint32_t slot_epoch;
    DWORD owner_tid;
    uintptr_t reservation;
    at_lp_plan plan;
    at_receipt receipt;
    at_private_graph priv __attribute__((aligned(16)));
} at_tls_frame;

int at_writer_init(
    at_writer_state *s,
    uintptr_t engine_base,
    at_safe_read_fn read_fn,
    void *read_user);

void at_writer_shutdown(at_writer_state *s);

/** Configure optional peer leaf-writer module lookup and populate peer_leaf_base/size if loaded. */
void at_writer_set_peer_lookup(
    at_writer_state *s,
    at_peer_lookup_fn lookup_fn,
    void *lookup_user);

/** Return 1 when caller falls inside lego_compact_leaf_writer.dll (lazily refreshing if needed). */
int at_is_peer_caller(
    at_writer_state *s,
    uintptr_t caller);

/** Capture and validate S2c mesh/skin/palette/bind/group offsets against pinned asset. */
int at_lp_capture(
    const at_writer_state *s,
    uintptr_t mesh,
    uintptr_t pool,
    uintptr_t expected_graph,
    at_lp_plan *out_plan);

/** Re-verify live runtime descriptor bytes haven't mutated between capture and leaf call. */
int at_lp_recheck(
    const at_writer_state *s,
    uintptr_t mesh,
    uintptr_t pool,
    const at_lp_plan *captured);

/** Outer 512B80 dispatch handler (substitutes private S2c graph via 5B2990 TLS frame). */
uint32_t at_writer_dispatch_512B80(
    at_writer_state *s,
    void *mesh,
    void *pool,
    void *opt_graph,
    uintptr_t caller,
    at_receipt *out_receipt);

/** Inner 5B2990 leaf skinning handler (verifies contract & substitutes chosen graph[t]). */
uint32_t at_writer_leaf_5B2990(
    at_writer_state *s,
    void *dst,
    const void *factor,
    const void *graph,
    const void *bind,
    const void *palette,
    const void *map,
    uint32_t count,
    uintptr_t caller);

/** RenderHandle 1E7CD0 removal handler (retires matching world+handle & bumps slot epoch). */
uint64_t at_writer_retire_1E7CD0(
    at_writer_state *s,
    uintptr_t world,
    void *packet,
    uintptr_t aux);

/** Periodic game-thread reconciliation scan of UnitManager. */
uint32_t at_writer_tick(at_writer_state *s, uint32_t max_scan);

/** Read TLS receipt pointer if inside active 512B80 frame (diagnostic/test inspection). */
const at_tls_frame *at_writer_current_frame(void);

#endif /* ATST_WRITER_CORE_H */
