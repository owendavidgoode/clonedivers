#include "coop_avatar_registry.h"
#include "compact_math.h"
#include <string.h>

int coop_is_compact_garment(uint64_t resource, uint32_t *out_kind) {
    return cm_garment_find(resource, out_kind);
}

void coop_registry_init(coop_registry *reg) {
    if (!reg) return;
    memset(reg, 0, sizeof *reg);
    reg->next_epoch = 1;
    reg->epoch_clock = 1;
}

void coop_sync_players(coop_registry *reg, const coop_player_entry *players, uint32_t count) {
    if (!reg) return;
    if (count > COOP_MAX_PLAYERS) count = COOP_MAX_PLAYERS;
    reg->player_count = count;
    memset(reg->players, 0, sizeof reg->players);
    if (players && count > 0) {
        memcpy(reg->players, players, (size_t)count * sizeof *players);
    }
    /* Reclassify already-active targets whenever PlayerManager slots shift (drop-in/respawn/preview->player). */
    for (uint32_t s = 0; s < COOP_MAX_TARGETS; s++) {
        coop_target_slot *slot = &reg->targets[s];
        if (!slot->active) continue;
        coop_actor_kind next_kind = COOP_ACTOR_PREVIEW_OR_CORPSE;
        uint64_t next_peer = 0;
        for (uint32_t i = 0; i < reg->player_count; i++) {
            const coop_player_entry *p = &reg->players[i];
            if (p->avatar_unit == slot->parent_unit &&
                (!p->avatar_handle || !slot->parent_handle || p->avatar_handle == slot->parent_handle) &&
                p->avatar_gen == slot->parent_gen) {
                next_peer = p->peer;
                next_kind = p->local ? COOP_ACTOR_LOCAL_PLAYER : COOP_ACTOR_REMOTE_PLAYER;
                break;
            }
        }
        slot->actor_kind = next_kind;
        slot->peer = next_peer;
    }
}

static coop_actor_kind classify_actor(const coop_registry *reg, const coop_mesh_probe *probe, uint64_t *out_peer, uint32_t *out_kind) {
    *out_peer = 0;
    if (!probe->mesh || !probe->pool || !probe->descriptor || !probe->child_unit || !probe->parent_unit) {
        return COOP_ACTOR_REJECTED;
    }
    if (probe->parent_unit_resource != COOP_HUMAN_AVATAR_RESOURCE || probe->parent_node_count != 107u) {
        return COOP_ACTOR_REJECTED;
    }
    if (probe->garment_ordinal > 4u) {
        return COOP_ACTOR_REJECTED;
    }
    if (!coop_is_compact_garment(probe->garment_resource, out_kind)) {
        return COOP_ACTOR_REJECTED;
    }
    /* Match against PlayerManager slots 0..count-1 without requiring count == 1 or local-only. */
    for (uint32_t i = 0; i < reg->player_count; i++) {
        const coop_player_entry *p = &reg->players[i];
        if (p->avatar_unit == probe->parent_unit &&
            (!p->avatar_handle || !probe->parent_unit_handle || p->avatar_handle == probe->parent_unit_handle) &&
            p->avatar_gen == probe->parent_unit_gen) {
            *out_peer = p->peer;
            return p->local ? COOP_ACTOR_LOCAL_PLAYER : COOP_ACTOR_REMOTE_PLAYER;
        }
    }
    /* Valid 107-node 4D1C334D294DFA97 unit wearing compact LEGO garments outside live PlayerManager
     * (e.g. drop-pod/armory preview or recently killed player corpse while Unit stays alive). */
    return COOP_ACTOR_PREVIEW_OR_CORPSE;
}

static void fill_slot(coop_target_slot *slot, const coop_mesh_probe *probe, uint64_t peer,
                      coop_actor_kind kind, uint32_t target_kind, uint32_t admit_epoch, uint32_t clock) {
    slot->active = 1;
    slot->mesh = probe->mesh;
    slot->render_handle = probe->render_handle;
    slot->parent_unit = probe->parent_unit;
    slot->parent_handle = probe->parent_unit_handle;
    slot->parent_gen = probe->parent_unit_gen;
    slot->garment_resource = probe->garment_resource;
    slot->peer = peer;
    slot->actor_kind = kind;
    slot->admit_epoch = admit_epoch;
    slot->world = probe->world;
    slot->child_unit = probe->child_unit;
    slot->child_index = probe->child_unit_index;
    slot->child_gen = probe->child_unit_gen;
    slot->parent_index = probe->parent_unit_index;
    slot->descriptor = probe->descriptor;
    slot->skin = probe->skin;
    slot->normal_graph = probe->normal_graph ? probe->normal_graph : probe->graph;
    slot->child_graph_handle = probe->child_graph_handle;
    slot->parent_graph_handle = probe->parent_graph_handle;
    slot->expected_assoc = probe->expected_assoc;
    slot->garment_ordinal = probe->garment_ordinal;
    slot->target_kind = target_kind;
    slot->node_index = probe->node_index;
    slot->last_seen_epoch = clock;
}

coop_actor_kind coop_admit_mesh(coop_registry *reg, const coop_mesh_probe *probe, coop_target_slot *out_slot) {
    if (!reg || !probe) return COOP_ACTOR_REJECTED;
    uint64_t peer = 0;
    uint32_t target_kind = 0;
    coop_actor_kind kind = classify_actor(reg, probe, &peer, &target_kind);
    if (kind == COOP_ACTOR_REJECTED) {
        reg->rejections++;
        reg->last_reject = COOP_FAIL_INVALID_TOPOLOGY;
        return COOP_ACTOR_REJECTED;
    }

    int free_idx = -1;
    int existing_idx = -1;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        if (reg->targets[i].active) {
            if (reg->targets[i].mesh == probe->mesh) {
                existing_idx = (int)i;
                break;
            }
        } else if (free_idx < 0) {
            free_idx = (int)i;
        }
    }

    uint32_t now_clock = reg->epoch_clock++;
    if (!now_clock) now_clock = reg->epoch_clock++;

    if (existing_idx >= 0) {
        coop_target_slot *slot = &reg->targets[existing_idx];
        /* Fast path: same render handle, child/parent unit, generation, descriptor, skin, and resource. */
        if (slot->render_handle == probe->render_handle &&
            slot->parent_unit == probe->parent_unit &&
            slot->parent_handle == probe->parent_unit_handle &&
            slot->parent_gen == probe->parent_unit_gen &&
            slot->garment_resource == probe->garment_resource &&
            slot->child_unit == probe->child_unit &&
            slot->child_gen == probe->child_unit_gen &&
            slot->descriptor == probe->descriptor &&
            slot->skin == probe->skin &&
            slot->world == probe->world &&
            slot->expected_assoc == probe->expected_assoc &&
            slot->child_graph_handle == probe->child_graph_handle &&
            slot->parent_graph_handle == probe->parent_graph_handle) {
            slot->actor_kind = kind;
            slot->peer = peer;
            slot->last_seen_epoch = now_clock;
            if (out_slot) *out_slot = *slot;
            return kind;
        }
        /* ABA safety: pointer address reused for a new unit/generation/resource without retirement hook firing first. */
        reg->aba_rebinds++;
        reg->last_reject = COOP_FAIL_STALE_GEN;
        uint32_t epoch = reg->next_epoch++;
        if (!epoch) epoch = reg->next_epoch++;
        fill_slot(slot, probe, peer, kind, target_kind, epoch, now_clock);
        if (out_slot) *out_slot = *slot;
        return kind;
    }

    if (free_idx < 0) {
        reg->rejections++;
        reg->last_reject = COOP_FAIL_CAPACITY;
        return COOP_ACTOR_REJECTED;
    }

    coop_target_slot *slot = &reg->targets[free_idx];
    uint32_t epoch = reg->next_epoch++;
    if (!epoch) epoch = reg->next_epoch++;
    fill_slot(slot, probe, peer, kind, target_kind, epoch, now_clock);
    reg->active_targets++;
    reg->admissions++;
    if (out_slot) *out_slot = *slot;
    return kind;
}

uint32_t coop_retire_handle_world(coop_registry *reg, uintptr_t world, uint32_t render_handle) {
    if (!reg || !render_handle || render_handle == 0xFFFFFFu) return 0;
    uint32_t removed = 0;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        coop_target_slot *slot = &reg->targets[i];
        if (!slot->active) continue;
        if (world && slot->world && slot->world != world) continue;
        if (slot->render_handle == render_handle ||
            (slot->child_graph_handle && slot->child_graph_handle == render_handle) ||
            (slot->parent_graph_handle && slot->parent_graph_handle == render_handle)) {
            memset(slot, 0, sizeof *slot);
            if (reg->active_targets > 0) reg->active_targets--;
            reg->retirements++;
            removed++;
        }
    }
    return removed;
}

uint32_t coop_retire_handle(coop_registry *reg, uint32_t render_handle) {
    return coop_retire_handle_world(reg, 0, render_handle);
}

uint32_t coop_retire_unit(coop_registry *reg, uintptr_t parent_unit, uint32_t old_gen) {
    if (!reg || !parent_unit) return 0;
    uint32_t removed = 0;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        if (reg->targets[i].active &&
            reg->targets[i].parent_unit == parent_unit &&
            reg->targets[i].parent_gen == old_gen) {
            memset(&reg->targets[i], 0, sizeof reg->targets[i]);
            if (reg->active_targets > 0) reg->active_targets--;
            reg->retirements++;
            removed++;
        }
    }
    return removed;
}

uint32_t coop_retire_unit_slot(coop_registry *reg, uint32_t unit_index, uintptr_t live_unit, uint32_t live_gen) {
    if (!reg) return 0;
    uint32_t removed = 0;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        coop_target_slot *slot = &reg->targets[i];
        if (!slot->active) continue;
        int stale_child = (slot->child_unit && slot->child_index == unit_index &&
                           (slot->child_unit != live_unit || slot->child_gen != live_gen));
        int stale_parent = (slot->parent_unit && slot->parent_index == unit_index &&
                            (slot->parent_unit != live_unit || slot->parent_gen != live_gen));
        if (stale_child || stale_parent) {
            memset(slot, 0, sizeof *slot);
            if (reg->active_targets > 0) reg->active_targets--;
            reg->retirements++;
            removed++;
        }
    }
    return removed;
}

const coop_target_slot *coop_find_mesh(const coop_registry *reg, uintptr_t mesh) {
    if (!reg || !mesh) return NULL;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        if (reg->targets[i].active && reg->targets[i].mesh == mesh) {
            return &reg->targets[i];
        }
    }
    return NULL;
}

coop_target_slot *coop_lookup_mesh(coop_registry *reg, uintptr_t mesh) {
    if (!reg || !mesh) return NULL;
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        if (reg->targets[i].active && reg->targets[i].mesh == mesh) {
            return &reg->targets[i];
        }
    }
    return NULL;
}
