/* Co-op / multi-actor avatar discovery & generation-safe MeshObject target registry.
 * Bridges PlayerManager 0..4 slots (hellforge-src event_polls.c) + demand-admitted 107-node
 * parent reverse-join (lego-mesh-demand-classifier-astra) + 1E7CD0 native RenderHandle retirement. */
#ifndef COOP_AVATAR_REGISTRY_H
#define COOP_AVATAR_REGISTRY_H

#include <stdint.h>
#include <stddef.h>

#define COOP_MAX_PLAYERS 4u
#define COOP_MAX_GARMENTS 256u /* 7 baked Stormtrooper + up to 249 dynamic JSON Clone/extensible kits */
#define COOP_MAX_TARGETS 256u /* 4 players * 7+ garments * 4 ordinals + Armory/pod previews + respawns */
#define COOP_HUMAN_AVATAR_RESOURCE 0x4D1C334D294DFA97ULL

typedef enum {
    COOP_ACTOR_REJECTED = 0,
    COOP_ACTOR_LOCAL_PLAYER = 1,
    COOP_ACTOR_REMOTE_PLAYER = 2,
    COOP_ACTOR_PREVIEW_OR_CORPSE = 3
} coop_actor_kind;

typedef enum {
    COOP_FAIL_CLOSED_NONE = 0,
    COOP_FAIL_CAPACITY = 1,
    COOP_FAIL_STALE_GEN = 2,
    COOP_FAIL_RETIRED = 3,
    COOP_FAIL_INVALID_TOPOLOGY = 4
} coop_reject_reason;

typedef struct {
    uintptr_t mesh;
    uintptr_t pool;
    uintptr_t graph;
    uintptr_t descriptor;
    uintptr_t child_unit;
    uintptr_t parent_unit;
    uint32_t parent_unit_handle;
    uint32_t parent_unit_gen;
    uint32_t parent_node_count;
    uint64_t parent_unit_resource;
    uint64_t garment_resource;
    uint32_t node_index;
    uint32_t render_handle;
    /* Extended live reverse-join metadata (zero in lightweight synthetic probes). */
    uintptr_t world;
    uintptr_t skin;
    uintptr_t normal_graph;
    uint32_t child_unit_index;
    uint32_t child_unit_gen;
    uint32_t parent_unit_index;
    uint32_t child_graph_handle;
    uint32_t parent_graph_handle;
    uintptr_t expected_assoc;
    uint32_t garment_ordinal; /* 1..4 for live calls, 0 allowed in minimal probe tests */
} coop_mesh_probe;

typedef struct {
    uint64_t peer;
    uint32_t slot;
    uint32_t life;
    uint32_t network_id;
    int local;
    uintptr_t avatar_unit;
    uint32_t avatar_handle;
    uint32_t avatar_gen;
} coop_player_entry;

typedef struct {
    int active;
    uintptr_t mesh;
    uint32_t render_handle;
    uintptr_t parent_unit;
    uint32_t parent_handle;
    uint32_t parent_gen;
    uint64_t garment_resource;
    uint64_t peer;
    coop_actor_kind actor_kind;
    uint32_t admit_epoch;
    /* Extended live runtime fields */
    uintptr_t world;
    uintptr_t child_unit;
    uint32_t child_index;
    uint32_t child_gen;
    uint32_t parent_index;
    uintptr_t descriptor;
    uintptr_t skin;
    uintptr_t normal_graph;
    uint32_t child_graph_handle;
    uint32_t parent_graph_handle;
    uintptr_t expected_assoc;
    uint32_t garment_ordinal;
    uint32_t target_kind; /* 0..6 garment table index */
    uint32_t node_index;
    uint32_t last_seen_epoch;
} coop_target_slot;

typedef struct {
    uint32_t player_count;
    coop_player_entry players[COOP_MAX_PLAYERS];
    coop_target_slot targets[COOP_MAX_TARGETS];
    uint32_t active_targets;
    uint32_t next_epoch;
    uint32_t epoch_clock;
    uint32_t admissions;
    uint32_t aba_rebinds;
    uint32_t retirements;
    uint32_t rejections;
    coop_reject_reason last_reject;
} coop_registry;

int coop_is_compact_garment(uint64_t resource, uint32_t *out_kind);
void coop_registry_init(coop_registry *reg);
void coop_sync_players(coop_registry *reg, const coop_player_entry *players, uint32_t count);
coop_actor_kind coop_admit_mesh(coop_registry *reg, const coop_mesh_probe *probe, coop_target_slot *out_slot);
uint32_t coop_retire_handle(coop_registry *reg, uint32_t render_handle);
uint32_t coop_retire_handle_world(coop_registry *reg, uintptr_t world, uint32_t render_handle);
uint32_t coop_retire_unit(coop_registry *reg, uintptr_t parent_unit, uint32_t old_gen);
uint32_t coop_retire_unit_slot(coop_registry *reg, uint32_t unit_index, uintptr_t live_unit, uint32_t live_gen);
const coop_target_slot *coop_find_mesh(const coop_registry *reg, uintptr_t mesh);
coop_target_slot *coop_lookup_mesh(coop_registry *reg, uintptr_t mesh);

#endif
