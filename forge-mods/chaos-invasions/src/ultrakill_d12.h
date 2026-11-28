/* Level 12 — CLONE WARS / STORMTROOPER Theater Engine Extensions (Chaos Invasions 0.5.2 / v2.3)
 * Target: Helldivers 2 Steam build 25480438, HellForge 0.5.0-beta, Mod SDK 20.
 *
 * Responsibilities:
 *   1. Boot-time Havok kPhysicsPools right-sizing (aligned with HellForge 0.5's 2,048 ragdolls / 262,144 bodies):
 *      ragdolls -> 2,048 (0x800), ragdoll_actors -> 65,536 (0x10000), motions -> 131,072 (0x20000),
 *      bodies -> 262,144 (0x40000), dynamic_actors -> 131,072 (0x20000), keyframed_actors -> 32,768 (0x8000).
 *   2. Wwise 512-voice uncap via AK::SoundEngine::SetMaxNumVoicesLimit(512) (vanilla Init.bnk = 140).
 *   3. Genuine 12th Difficulty Slot ("CLONE WARS" / "STORMTROOPER") configured as a clean 1-primary / 5-optional
 *      battle (<= 14 HUD/map markers < DIF_MARKER_SLOTS = 16) while leaving D11 ("HELL FORGE") 2-primary / 8-optional.
 *   4. Tighter ~700 m (~1.7x, 0.685f * 1,024 m = 701.4 m) playable battlefield radius for continuous action.
 *   5. Hard < 1,024 GwNav::Bot Governor & Hybrid Clone Wars Theater:
 *      - 1-syscall bulk EntityManager descriptor snapshot tracking BOTH live_slots and walking_ai_slots.
 *      - Tight <= 620 walking-AI cap & <= 580 active Director patrol/reinforce valve so total GwNav::Bots
 *        NEVER approach helldivers2.exe+0x801b03's 1,024 pool ceiling even under D12 3x outposts + Pelicans.
 *      - High real active density in BOTH solo (610) and co-op (600), outer cohesive 18-32 unit swarms
 *        at 95-185 m marching inward, > 260 m straggler recycling, and 140-380 m virtual laser exchanges.
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
/* Keeps live EntityManager slots <= 700 and walking AI <= 620, staying > 280 slots below the 1,024 GwNav::Bot
 * hard allocator crash at helldivers2.exe+0x801b03 (1,348 free on 2,048 table; 3,396 free on 4,096 table). */
#define UK_EM_RESERVE_SLOTS             1348u
#define UK_MAX_WALKING_AI_CAP           620u
#define UK_DIRECTOR_VALVE_STOP_WALKING  580u
#define UK_DIRECTOR_VALVE_RESUME_WALKING 520u
#define UK_FOREIGN_HEADROOM_SLOTS       72
#define UK_DEFAULT_MAP_RADIUS_FACTOR    0.685f /* 701.4 m (~1.7x Super Helldive 410 m) */
#define UK_D12_MAJOR_OBJECTIVES         1u
#define UK_D12_OPTIONAL_OBJECTIVES      5u

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
    uint32_t legacy_uk_val;
    uint32_t ultrakill_val;
} UkPhysicsPoolPatch;

/* Right-sized to HellForge 0.5's 2,048 ragdolls / 262,144 bodies to prevent ~380 MB Havok arena bloat (#43). */
static const UkPhysicsPoolPatch kUkPhysicsPools[6] = {
    /* config +0x2C ragdoll_actors: 8,192 -> 65,536 (0x10000) */
    {0x4EE32Bu, 7u,  3u, {0xC7, 0x45, 0x9C, 0, 0, 0},       0x00002000u, 0x00008000u, 0x00010000u, 0x00040000u, 0x00010000u},
    /* config +0x3C ragdolls: 512 -> 2,048 (0x800) */
    {0x4EE35Cu, 7u,  3u, {0xC7, 0x45, 0xAC, 0, 0, 0},       0x00000200u, 0x00000400u, 0x00000800u, 0x00002000u, 0x00000800u},
    /* Havok world motions: 36,864 -> 131,072 (0x20000) */
    {0xAB19F8u, 10u, 6u, {0xC7, 0x85, 0x20, 0x05, 0x00, 0x00}, 0x00009000u, 0x00020000u, 0x00020000u, 0x00040000u, 0x00020000u},
    /* Havok world bodies (< 2^21 bit-width cap): 131,072 -> 262,144 (0x40000) */
    {0xAB19EEu, 10u, 6u, {0xC7, 0x85, 0x1C, 0x05, 0x00, 0x00}, 0x00020000u, 0x00040000u, 0x00040000u, 0x00080000u, 0x00040000u},
    /* config +0x24 dynamic_actors: 32,768 -> 131,072 (0x20000) */
    {0x4EE31Du, 7u,  3u, {0xC7, 0x45, 0x94, 0, 0, 0},       0x00008000u, 0x00020000u, 0x00020000u, 0x00040000u, 0x00020000u},
    /* config +0x20 keyframed_actors: 8,192 -> 32,768 (0x8000) */
    {0x4EE332u, 7u,  3u, {0xC7, 0x45, 0x90, 0, 0, 0},       0x00002000u, 0x00008000u, 0x00008000u, 0x00010000u, 0x00008000u},
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
            cur_val != p->hf0158_val && cur_val != p->legacy_uk_val &&
            cur_val != p->ultrakill_val) {
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
    uint32_t walking_ai_slots;
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
    int director_valve_closed;
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
    uint32_t stragglers_recycled_total;
    uint32_t friendly_air_runs_total;
    uint32_t enemy_air_runs_total;
    uint32_t atte_salvos_total;
    ChaosVirtualBattalion battalions[UK_MAX_VIRTUAL_BATTALIONS];
} ChaosUltrakillState;

static inline float UltrakillResolveRadiusFactor(const CdHostOptions* ho)
{
    float radius_factor = UK_DEFAULT_MAP_RADIUS_FACTOR;
    if (ho && ho->map_radius_permille >= 380 && ho->map_radius_permille <= 850) {
        radius_factor = (float)ho->map_radius_permille / 1000.0f;
    } else if (ho && ho->map_radius_permille > 850 && ho->map_radius_permille <= 940 &&
               ho->theater_preset == CD_THEATER_LEVEL12_ULTRAKILL &&
               ho->map_radius_permille != 920) {
        radius_factor = (float)ho->map_radius_permille / 1000.0f;
    }
    return radius_factor;
}

static inline void UltrakillApplyDifficultyRow12(HfDifficultySettings* row, const uint8_t baseline[sizeof(HfDifficultySettings)],
                                                 const CdHostOptions* ho)
{
    if (!row || !baseline) return;
    memcpy(row, baseline, sizeof(HfDifficultySettings));
    if (!ho || !cd_host_options_is_ultrakill(ho)) return;

    float radius_factor = UltrakillResolveRadiusFactor(ho);

    /* Clean 1-primary / 5-optional layout (<= 14 total HUD/map markers < DIF_MARKER_SLOTS = 16). */
    row->optional_objectives = UK_D12_OPTIONAL_OBJECTIVES;
    row->major_objectives = UK_D12_MAJOR_OBJECTIVES;
    row->encounter_points = 1500.0f;
    row->guardforce_points = 1150.0f;
    row->spawner_cooldown = 10.0f;
    for (int i = 0; i < 4; ++i) {
        if (row->map_radius[i] > 0.50f) {
            row->map_radius[i] = radius_factor;
        }
    }
    for (int f = 0; f < 3; ++f) {
        row->patrol_interval[f] = 30.0f;
        row->patrol_cap[f] = 24u;
    }
    uint32_t reinf_cap = 96u;
    memcpy((uint8_t*)row + UK_DIFF_REINFORCE_CAP_OFF, &reinf_cap, sizeof reinf_cap);
}

static inline int UltrakillTargetRealAiCap(const CdHostOptions* ho, int has_remote_peers)
{
    int def_cap = has_remote_peers ? 600 : 610;
    int want = (ho && ho->target_active_units > 0) ? ho->target_active_units : def_cap;
    if (want > (int)UK_MAX_WALKING_AI_CAP) want = (int)UK_MAX_WALKING_AI_CAP;
    if (want < 200) want = 200;
    return want;
}

static inline int UltrakillTargetVirtualUnitsForDiff(const CdHostOptions* ho, int live_diff)
{
    if (!cd_host_options_active_for_difficulty(ho, live_diff)) return 0;
    if (ho && ho->virtual_theater_budget > 0) {
        int want = ho->virtual_theater_budget;
        if (ho->target_active_units > (int)UK_MAX_WALKING_AI_CAP) {
            want += (ho->target_active_units - (int)UK_MAX_WALKING_AI_CAP);
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
