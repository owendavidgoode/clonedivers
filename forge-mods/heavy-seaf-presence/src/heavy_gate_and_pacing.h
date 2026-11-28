/* Heavy SEAF Presence 1.2.0-clonedivers.5 — Positive mission-ground landing gate,
 * 1-unit/tick synchronous equip budget, lock-free QPC state breadcrumbs (zero FlushFileBuffers),
 * and clonedivers-host-options.json preset mapping.
 */
#ifndef HEAVY_GATE_AND_PACING_H
#define HEAVY_GATE_AND_PACING_H

#include <stdint.h>
#include <string.h>
#include <windows.h>
#include "hellforge_mod.h"
#include "clonedivers_host_options.h"

#ifdef __cplusplus
extern "C" {
#endif

#define HEAVY_LANDING_GRACE_MS            15000ULL
#define HEAVY_CLONE_WARS_LANDING_GRACE_MS 7000ULL
#define HEAVY_MAX_BOT_SAFE_SEAF_CEILING   48
#define HEAVY_MAX_WALKING_AI_GATE         660u
#define HEAVY_MAX_LIVE_EM_GATE            740u
#define HEAVY_GWNAV_RESERVE_2048          (2048u - HEAVY_MAX_LIVE_EM_GATE) /* 1308u */
#define HEAVY_MAX_EQUIPS_PER_TICK         1u
#define HEAVY_EQUIP_RETRY_BACKOFF_MS      250ULL
#define HEAVY_EQUIP_MAX_WINDOW_MS         8000ULL
#define HEAVY_EQUIP_MAX_RETRIES           16u

typedef struct HeavyLandingGate {
    int landed;
    int grace_ready;
    ULONGLONG landed_at_ms;
} HeavyLandingGate;

static inline void HeavyLandingGateReset(HeavyLandingGate* g)
{
    if (!g) return;
    g->landed = 0;
    g->grace_ready = 0;
    g->landed_at_ms = 0ULL;
}

/* Positive mission-ground gate (immune to stale ship-spawn HF_ON_PLAYER_SPAWN):
 * - Resets landed = 0 whenever state != HF_STATE_MISSION or !is_host.
 * - Arms grace_ms timer ONLY after state == HF_STATE_MISSION + living_host_entity != 0.
 * - Returns:
 *     0 : not ready yet
 *     1 : ready (grace elapsed)
 *     2 : newly armed this step (caller may log once; still waiting for grace) */
static inline int HeavyLandingGateStepWithGrace(HeavyLandingGate* g, uint32_t state, int is_host,
                                                int living_host_entity, ULONGLONG now_ms,
                                                ULONGLONG grace_ms)
{
    if (!g) return 0;
    if (state != HF_STATE_MISSION || !is_host) {
        HeavyLandingGateReset(g);
        return 0;
    }
    if (grace_ms == 0ULL) grace_ms = HEAVY_LANDING_GRACE_MS;
    if (!g->landed) {
        if (!living_host_entity) return 0;
        g->landed = 1;
        g->grace_ready = 0;
        g->landed_at_ms = now_ms;
        return 2;
    }
    if (!living_host_entity) {
        /* Host died before grace completed; keep landed=1 once ground entry occurred, or require grace. */
    }
    if (!g->grace_ready) {
        if (now_ms >= g->landed_at_ms && (now_ms - g->landed_at_ms) >= grace_ms) {
            g->grace_ready = 1;
        }
    }
    return g->grace_ready ? 1 : 0;
}

static inline int HeavyLandingGateStep(HeavyLandingGate* g, uint32_t state, int is_host,
                                       int living_host_entity, ULONGLONG now_ms)
{
    return HeavyLandingGateStepWithGrace(g, state, is_host, living_host_entity, now_ms, HEAVY_LANDING_GRACE_MS);
}

/* Maps clonedivers-host-options.json "heavySeaf" mode ("standard" / "heavy" / "constant" / "clone-wars")
 * onto Heavy SEAF runtime parameters. Constant drops ALWAYS default to 0 unless "constant" or "clone-wars" is selected! */
static inline void HeavyApplyHostMode(CdHeavySeafMode mode, int* out_constant, int* out_support_squads,
                                      int* out_interval_seconds, int* out_squads_per_wave, int* out_extra_squads)
{
    if (mode == CD_HEAVY_SEAF_CLONE_WARS) {
        if (out_constant) *out_constant = 1;
        /* Use 1-slot zero-maintenance native 116u Clone Trooper squads bounded <= 48 troops inside the 1,024 GwNav::Bot budget. */
        if (out_support_squads) *out_support_squads = 0;
        if (out_interval_seconds) *out_interval_seconds = 24;
        if (out_squads_per_wave) *out_squads_per_wave = 2;
        if (out_extra_squads) *out_extra_squads = 5;
    } else if (mode == CD_HEAVY_SEAF_CONSTANT) {
        if (out_constant) *out_constant = 1;
        if (out_support_squads) *out_support_squads = 1;
        if (out_interval_seconds) *out_interval_seconds = 90;
        if (out_squads_per_wave) *out_squads_per_wave = 1;
        if (out_extra_squads) *out_extra_squads = 2;
    } else if (mode == CD_HEAVY_SEAF_HEAVY) {
        if (out_constant) *out_constant = 0;
        if (out_support_squads) *out_support_squads = 1;
        if (out_interval_seconds) *out_interval_seconds = 90;
        if (out_squads_per_wave) *out_squads_per_wave = 2;
        if (out_extra_squads) *out_extra_squads = 2;
    } else {
        if (out_constant) *out_constant = 0;
        if (out_support_squads) *out_support_squads = 0;
        if (out_interval_seconds) *out_interval_seconds = 120;
        if (out_squads_per_wave) *out_squads_per_wave = 1;
        if (out_extra_squads) *out_extra_squads = 0;
    }
}

/* ---- Shared 1-unit/tick equip pacing & in-memory QPC ring-buffer breadcrumbs ---- */

enum {
    HEAVY_BC_NONE       = 0u,
    HEAVY_BC_POOL_EQUIP = 1u,
    HEAVY_BC_JOB_EQUIP  = 2u,
    HEAVY_BC_CALL_SEAF  = 3u
};

typedef struct HeavyBreadcrumb {
    uint32_t kind;
    uint32_t shuttle;
    uint32_t soldier;
    uint32_t slot;
    uint64_t enter_qpc;
    uint64_t leave_qpc;
    int32_t status; /* 0 = entered/in-flight, 1 = ok, -1 = error */
} HeavyBreadcrumb;

void HeavyEquipBeginFrame(void);
int HeavyEquipConsumeOne(void);
unsigned HeavyBreadcrumbEnter(uint32_t kind, uint32_t shuttle, uint32_t soldier, uint32_t slot);
double HeavyBreadcrumbLeave(unsigned idx, int32_t status);
const HeavyBreadcrumb* HeavyBreadcrumbRing(unsigned* count_out, unsigned* head_out);

#ifdef __cplusplus
}
#endif

#endif /* HEAVY_GATE_AND_PACING_H */
