/* Level 12 — ULTRAKILL & Full Clone Wars Theater Engine Extensions (Chaos Invasions 0.4.0)
 * Target: Helldivers 2 Steam build 25480438, HellForge Engine 0.1.47, Mod SDK 16.
 *
 * Responsibilities:
 *   1. Boot-time Havok kPhysicsPools uncap (before [game.dll+0x3326380] initializes):
 *      ragdolls -> 8,192 (0x2000), ragdoll_actors -> 262,144 (0x40000), motions -> 262,144,
 *      bodies -> 524,288 (0x80000, < 2^21 body-ID bit-width limit), dynamic_actors -> 262,144,
 *      keyframed_actors -> 65,536. Prevents the unchecked allocator -1 crash at exe+0x7A307F
 *      past ~963 units+corpses.
 *   2. Wwise 512-voice uncap via AK::SoundEngine::SetMaxNumVoicesLimit(512) (vanilla Init.bnk = 140).
 *   3. Live reversible HfDifficultySettings row-11 mutation + engine text vtable hook
 *      ([[game.dll+0x3326308]+0x10]+1000) serving "LEVEL 12: ULTRAKILL" / "LEVEL 12: CLONE WARS"
 *      and top-HUD difficulty number "12" when armed on Solo/Host.
 *   4. Playable battlefield radius expansion on 2x generated maps (lg+0x2D70: 0.76 -> 0.92 = 942 m
 *      radius, 1,884 m diameter, 2.79 km^2 = 5.86x Super Helldive area) inside NavRetile's 2,048 m
 *      navmesh without overflowing Voronoi/stamp/city generation tables.
 *   5. Hybrid Full Clone Wars Theater:
 *      - 1-syscall bulk EntityManager descriptor snapshot ([game.dll+0x346BF98]+0xF32F18, 48 KB)
 *        enforcing free_em_slots >= 260 so player weapons/stratagems/Pelicans never starve.
 *      - Paced SpawnRosterRow ring queue (<= 6 unit constructions/frame) across 3 concentric rings
 *        (55..115 m, 115..230 m, 230..380 m).
 *      - 180 outer-theater Virtual Battalions (1,400-1,800 CIS Droid + Republic Clone troopers at
 *        190..880 m, 0 EntityManager slots, 0 Havok ragdolls) exchanging visible laser/plasma
 *        salvos across the horizon and dynamically promoting into real AI squads as players close in.
 */
#ifndef CHAOS_ULTRAKILL_D12_H
#define CHAOS_ULTRAKILL_D12_H

#include <math.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>
#include "hellforge_mod.h"
#include "hellforge_content.h"
#include "policy.h"
#include "clonedivers_em_layout.h"

#define UK_PHYSICS_SETUP_RVA        0x3326380u
#define UK_ENGINE_API_RVA           0x3326308u
#define UK_ENGINE_API_TABLE_OFF     0x10u
#define UK_ENGINE_TEXT_SLOT_OFF     1000u
#define UK_CONTEXT_RVA              0x3326340u
#define UK_CONTEXT_LG_OFF           0xAE288u
#define UK_LG_MODE_OFF              0x1E4u
#define UK_LG_PLAYABLE_RADIUS_OFF   0x2D70u
#define UK_LG_BORDER_RADIUS_OFF     0x2D74u
#define UK_DIFF_REINFORCE_CAP_OFF   0x2DCu
#define UK_DIR_REINFORCE_BUDGET_OFF 0x518B0u
#define UK_INVALID_ENTITY_RVA       0x348456Cu

#define UK_EM_DESCRIPTORS_OFF       0xF32F18u
#define UK_EM_DESCRIPTOR_CAP        2048u
#define UK_EM_MAX_DESCRIPTOR_CAP    4096u
#define UK_EM_DESCRIPTOR_STRIDE     24u
/* Keeps live EntityManager slots <= 868 (~600-720 GwNav::Bots), staying >300 below the 1,024 GwNav::Bot
 * hard allocator crash at helldivers2.exe+0x801b03 (1,180 free on 2,048 table; 3,228 free on 4,096 table). */
#define UK_EM_RESERVE_SLOTS         1180u

#define UK_SPAWN_QUEUE_CAP          256u
#define UK_MAX_SPAWNS_PER_FRAME     4u
#define UK_MAX_VIRTUAL_BATTALIONS   225u
#define UK_WWISE_VOICES             512u

typedef struct UkPhysicsPoolPatch {
    uint32_t rva;
    uint8_t length;
    uint8_t value_off;
    uint8_t prefix[6];
    uint32_t vanilla_val;
    uint32_t hf0147_val;
    uint32_t hf0158_val;
    uint32_t ultrakill_val;
} UkPhysicsPoolPatch;

static const UkPhysicsPoolPatch kUkPhysicsPools[6] = {
    /* config +0x2C ragdoll_actors: 8,192 / 32,768 (0.1.47) / 65,536 (0.1.58) -> 262,144 */
    {0x4EE32Bu, 7u,  3u, {0xC7, 0x45, 0x9C, 0, 0, 0},       0x00002000u, 0x00008000u, 0x00010000u, 0x00040000u},
    /* config +0x3C ragdolls: 512 / 1,024 (0.1.47) / 2,048 (0.1.58) -> 8,192 */
    {0x4EE35Cu, 7u,  3u, {0xC7, 0x45, 0xAC, 0, 0, 0},       0x00000200u, 0x00000400u, 0x00000800u, 0x00002000u},
    /* Havok world motions: 36,864 / 131,072 -> 262,144 */
    {0xAB19F8u, 10u, 6u, {0xC7, 0x85, 0x20, 0x05, 0x00, 0x00}, 0x00009000u, 0x00020000u, 0x00020000u, 0x00040000u},
    /* Havok world bodies (< 2^21 bit-width cap): 131,072 / 262,144 -> 524,288 */
    {0xAB19EEu, 10u, 6u, {0xC7, 0x85, 0x1C, 0x05, 0x00, 0x00}, 0x00020000u, 0x00040000u, 0x00040000u, 0x00080000u},
    /* config +0x24 dynamic_actors: 32,768 / 131,072 -> 262,144 */
    {0x4EE31Du, 7u,  3u, {0xC7, 0x45, 0x94, 0, 0, 0},       0x00008000u, 0x00020000u, 0x00020000u, 0x00040000u},
    /* config +0x20 keyframed_actors: 8,192 / 32,768 -> 65,536 */
    {0x4EE332u, 7u,  3u, {0xC7, 0x45, 0x90, 0, 0, 0},       0x00002000u, 0x00008000u, 0x00008000u, 0x00010000u},
};

typedef int (*UkReadFn)(uintptr_t address, void* buffer, size_t size);
typedef int (*UkWriteFn)(uintptr_t address, const void* data, size_t size);
typedef const char* (*UkEngineTextFn)(uint32_t id, void* a, void* b, void* c);

/* Returns: 1 = patched all 6 Havok pools, 0 = setup already ran ([+0x3326380] != 0), -1 = pin/write mismatch. */
static inline int UltrakillPatchPoolsWithOps(uintptr_t gbase, size_t gsize, UkReadFn read_fn, UkWriteFn write_fn)
{
    if (!gbase || gsize < UK_PHYSICS_SETUP_RVA + 8u || !read_fn || !write_fn) return -1;
    uint64_t setup_ptr = 1ULL;
    if (read_fn(gbase + UK_PHYSICS_SETUP_RVA, &setup_ptr, sizeof setup_ptr) != 0) return -1;
    if (setup_ptr != 0ULL) return 0;

    for (unsigned i = 0; i < 6u; ++i) {
        const UkPhysicsPoolPatch* p = &kUkPhysicsPools[i];
        uint8_t insn[10];
        uint32_t cur_val = 0u;
        if ((size_t)p->rva + p->length > gsize) return -1;
        if (read_fn(gbase + p->rva, insn, p->length) != 0) return -1;
        if (memcmp(insn, p->prefix, p->value_off) != 0) return -1;
        memcpy(&cur_val, insn + p->value_off, 4u);
        if (cur_val != p->vanilla_val && cur_val != p->hf0147_val &&
            cur_val != p->hf0158_val && cur_val != p->ultrakill_val) {
            return -1;
        }
    }

    for (unsigned i = 0; i < 6u; ++i) {
        const UkPhysicsPoolPatch* p = &kUkPhysicsPools[i];
        if (write_fn(gbase + p->rva + p->value_off, &p->ultrakill_val, 4u) != 0) {
            return -1;
        }
    }
    return 1;
}

typedef struct ChaosEmSnapshot {
    uintptr_t em_root;
    uintptr_t desc_base;
    uintptr_t desc_end;
    uint32_t em_cap;
    uint32_t gwnav_reserve;
    uint32_t invalid_entity;
    uint32_t live_slots;
    uint32_t free_slots;
    int valid;
    double captured_age;
    uint8_t raw[UK_EM_MAX_DESCRIPTOR_CAP * UK_EM_DESCRIPTOR_STRIDE];
} ChaosEmSnapshot;

typedef struct ChaosSpawnJob {
    uint8_t faction;
    uint8_t row_slot;
    uint16_t pad;
    uint32_t flags;
    float pos[3];
    float target[3];
    float delay_s;
} ChaosSpawnJob;

typedef struct ChaosSpawnQueue {
    ChaosSpawnJob jobs[UK_SPAWN_QUEUE_CAP];
    uint32_t head;
    uint32_t tail;
    uint32_t count;
} ChaosSpawnQueue;

static inline void ChaosSpawnQueueReset(ChaosSpawnQueue* q)
{
    if (!q) return;
    q->head = 0u;
    q->tail = 0u;
    q->count = 0u;
}

static inline int ChaosSpawnQueuePush(ChaosSpawnQueue* q, const ChaosSpawnJob* job)
{
    if (!q || !job || q->count >= UK_SPAWN_QUEUE_CAP) return 0;
    q->jobs[q->tail] = *job;
    q->tail = (q->tail + 1u) % UK_SPAWN_QUEUE_CAP;
    ++q->count;
    return 1;
}

static inline int ChaosSpawnQueuePop(ChaosSpawnQueue* q, ChaosSpawnJob* out)
{
    if (!q || !out || q->count == 0u) return 0;
    *out = q->jobs[q->head];
    q->head = (q->head + 1u) % UK_SPAWN_QUEUE_CAP;
    --q->count;
    return 1;
}

/* Outer-ring Virtual Battalion (0 EntityManager slots, 0 Havok ragdolls).
 * faction 0..2 = enemy (Bots/CIS, Squids, Bugs); faction 3 = friendly Republic Clone Battalion. */
#define UK_VIRTUAL_FACTION_CLONES 3u

typedef struct ChaosVirtualBattalion {
    uint8_t active;
    uint8_t faction;
    uint8_t unit_count;
    uint8_t engaged;
    float pos[3];
    float vel[2];
    float fire_cooldown;
    float ttl;
} ChaosVirtualBattalion;

typedef struct ChaosUltrakillState {
    int pools_status;          /* 0 = pending, 1 = enlarged, -1 = skipped */
    int voices_raised;
    int d11_snapshot_valid;
    int d12_applied;
    uint32_t d11_name_text_id;
    uint32_t d11_num_text_id;
    uint8_t d11_baseline[sizeof(HfDifficultySettings)];
    volatile LONG text_hook_state; /* 0 = none, 1 = installing, 2 = active, -1 = failed */
    UkEngineTextFn orig_text;
    float live_radius_factor;
    float surge_accum_s;
    float telemetry_accum_s;
    float budget_refresh_s;
    float salvo_budget_tokens;
    uint32_t virtual_units_total;
    uint32_t virtual_regiments_active;
    uint32_t salvos_fired_total;
    uint32_t promotions_total;
    uint32_t multi_ring_spawns_total;
    ChaosVirtualBattalion battalions[UK_MAX_VIRTUAL_BATTALIONS];
} ChaosUltrakillState;

static inline void UltrakillApplyDifficultyRow12(HfDifficultySettings* row, const uint8_t baseline[sizeof(HfDifficultySettings)],
                                                 const CdHostOptions* ho)
{
    if (!row || !baseline) return;
    memcpy(row, baseline, sizeof(HfDifficultySettings));
    if (!ho || !cd_host_options_is_ultrakill(ho)) return;

    float radius_factor = 0.92f;
    if (ho->map_radius_permille >= 380 && ho->map_radius_permille <= 940) {
        radius_factor = (float)ho->map_radius_permille / 1000.0f;
    }

    row->encounter_points = 1500.0f;
    row->guardforce_points = 1200.0f;
    row->spawner_cooldown = 8.0f;
    for (int i = 0; i < 4; ++i) {
        if (row->map_radius[i] > 0.50f && row->map_radius[i] < radius_factor) {
            row->map_radius[i] = radius_factor;
        }
    }
    for (int f = 0; f < 3; ++f) {
        row->patrol_interval[f] = 28.0f;
        row->patrol_cap[f] = 32u;
    }
    uint32_t reinf_cap = 120u;
    memcpy((uint8_t*)row + UK_DIFF_REINFORCE_CAP_OFF, &reinf_cap, sizeof reinf_cap);
}

static inline int UltrakillTargetRealAiCap(const CdHostOptions* ho, int has_remote_peers)
{
    if (has_remote_peers) {
        /* Protect Stingray peer snapshot bandwidth in active multiplayer sessions. */
        return 420;
    }
    int want = (ho && ho->target_active_units > 0) ? ho->target_active_units : 820;
    if (want > 840) want = 840; /* Hard governor keeping >= 1,180 EntityManager slots free (< 1,024 GwNav::Bots) */
    if (want < 200) want = 200;
    return want;
}

static inline int UltrakillTargetVirtualUnitsForDiff(const CdHostOptions* ho, int live_diff)
{
    if (!cd_host_options_active_for_difficulty(ho, live_diff)) return 0;
    if (ho && ho->virtual_theater_budget > 0) {
        int want = ho->virtual_theater_budget;
        if (ho->target_active_units > 840) {
            want += (ho->target_active_units - 840);
        }
        return want > 2150 ? 2150 : want;
    }
    return (cd_effective_chaos_for_difficulty(ho, live_diff) == CD_CHAOS_CLONE_WARS) ? 2050 : 1650;
}

static inline int UltrakillTargetVirtualUnits(const CdHostOptions* ho)
{
    return UltrakillTargetVirtualUnitsForDiff(ho, 12);
}

#endif /* CHAOS_ULTRAKILL_D12_H */
