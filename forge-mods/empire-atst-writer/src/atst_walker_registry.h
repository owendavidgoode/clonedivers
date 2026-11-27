/* Multi-walker (up to 4 concurrent co-op EXO-45/49 AT-ST) registry & Handle 1E7CD0 retirement. */
#ifndef ATST_WALKER_REGISTRY_H
#define ATST_WALKER_REGISTRY_H

#include "atst_code_pins.h"
#include "atst_policy.h"

#include <stddef.h>
#include <stdint.h>

#define AT_MAX_WALKERS 4u
#define AT_UNIT_CAP_LIMIT 524288u
#define AT_RENDER_HANDLE_LIMIT (1u << 22)
#define AT_RENDER_VEC_LIMIT (1u << 22)

typedef int (*at_safe_read_fn)(uintptr_t address, void *buffer, size_t size, void *user);

typedef struct {
    uintptr_t address;
    uintptr_t slots;
    uintptr_t generations;
    uint32_t capacity;
} at_unit_manager_view;

typedef struct {
    uint32_t index;
    uint8_t generation;
    uintptr_t unit;
    uintptr_t vtable;
    uintptr_t resource_ptr;
    uint64_t resource_id;
    uint32_t graph_handle;
    uintptr_t association;
    uint32_t nodes;
    uint32_t meshes;
    uintptr_t world_array;
    uintptr_t topology;
    uintptr_t mesh_entries;
} at_unit_view;

typedef struct {
    uint32_t length;
    uint32_t capacity;
    uintptr_t data;
} at_render_vec;

typedef struct {
    uintptr_t manager;
    uintptr_t queue;
    uintptr_t world;
    uintptr_t handles;
    uint32_t capacity;
    uint32_t mask;
    uint32_t generation;
    uint32_t live;
    at_render_vec objects;
    at_render_vec types;
    at_render_vec map;
} at_render_header;

typedef struct {
    uint32_t handle;
    uint32_t index;
    uint32_t type;
    uint32_t nodes;
    uintptr_t object;
    uintptr_t vtable;
} at_resolved_graph;

typedef struct {
    uint8_t occupied;
    uint8_t meshes_resolved;
    uint8_t mesh_count;
    uint32_t epoch;
    uint32_t unit_index;
    uint8_t unit_gen;
    uintptr_t unit;
    uint64_t resource_id;
    uintptr_t association;
    uintptr_t world;
    uint32_t graph_handle;
    uintptr_t normal_graph;
    uintptr_t meshes[AT_MESH_DESC_COUNT];
} at_walker_slot;

typedef struct {
    at_walker_slot slots[AT_MAX_WALKERS];
    uintptr_t engine_base;
    uintptr_t rmanager_vtable_graph; /* engine + 0x16880B0u (overridable if needed in unit tests) */
    uintptr_t rmanager_vtable_mesh;  /* engine + 0x1688950u (MeshObject vtable) */
    uint32_t scan_cursor;
    uint32_t admissions;
    uint32_t retirements;
    uint32_t scavenged;
    uint32_t mesh_resolutions;
    uint32_t capacity_refusals;
    uint32_t passthroughs;
    uint32_t sweep_count;
    uintptr_t scanned_slots[AT_UNIT_CAP_LIMIT];
    uint8_t scanned_gens[AT_UNIT_CAP_LIMIT];
} at_walker_registry;

void at_registry_init(at_walker_registry *reg, uintptr_t engine_base);

/** Reset all slots cleanly (e.g., on HF_EVENT_SESSION_END). */
void at_registry_reset(at_walker_registry *reg);

/** Read & verify UnitManager pointer table at engine + AT_RVA_UNIT_MANAGER. */
int at_read_unit_manager(
    const at_walker_registry *reg,
    at_safe_read_fn read_fn,
    void *read_user,
    at_unit_manager_view *out);

/** Read & verify an S2c 106-node AT-ST Unit (EXO-45/49/51/55) at UnitManager index idx. */
int at_read_atst_unit(
    const at_walker_registry *reg,
    const at_unit_manager_view *mgr,
    uint32_t idx,
    at_safe_read_fn read_fn,
    void *read_user,
    at_unit_view *out);

/** Read & validate Stingray RenderManager + RenderWorld backlink + Vec tables. */
int at_read_render_header(
    uintptr_t manager,
    at_safe_read_fn read_fn,
    void *read_user,
    at_render_header *out);

/** Resolve a RenderHandle (type=2 RenderGraph) through RenderManager tables. */
int at_resolve_render_graph(
    const at_walker_registry *reg,
    const at_render_header *hdr,
    uint32_t handle,
    at_safe_read_fn read_fn,
    void *read_user,
    at_resolved_graph *out);

/** Reconcile slots against live UnitManager: evict dead/recycled units, admit new AT-ST walkers into lowest-free slot. */
uint32_t at_registry_reconcile(
    at_walker_registry *reg,
    at_safe_read_fn read_fn,
    void *read_user,
    uint32_t max_scan);

/** Retire any walker bound to (world, graph_handle) on 1E7CD0 removal; returns number retired. */
uint32_t at_registry_retire_handle(
    at_walker_registry *reg,
    uintptr_t world,
    uint32_t handle);

/** Resolve and validate a live AT-ST walker slot at 512B80 dispatch entry.
 *  Returns slot index (0..3) and writes captured epoch into *out_epoch, or -1 if foreign/invalid/retired. */
int at_registry_resolve_render(
    at_walker_registry *reg,
    uintptr_t pool,
    uintptr_t mesh,
    uintptr_t opt_graph,
    at_safe_read_fn read_fn,
    void *read_user,
    uint32_t *out_epoch,
    at_resolved_graph *out_graph);

#endif /* ATST_WALKER_REGISTRY_H */
