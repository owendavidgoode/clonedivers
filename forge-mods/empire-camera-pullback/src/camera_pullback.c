/* Empire & Clone Camera Pullback (clonedivers.empire-camera-pullback 0.1.3)
 * Target: Helldivers 2 Steam build 25480438, HellForge Mod SDK 16.
 *
 * Per-player third-person follow-camera pullback and dorsal-hull clearance for occupied
 * AT-TE / AT-ST combat walkers and Compact LEGO 3P armor kits, clamped against walls,
 * ceilings, and bulkheads via the engine's synchronous Havok damage_los_query raycast.
 *
 * v0.1.1-0.1.2 Performance & Concurrency Hardening:
 *   - ZERO full-world unit scans on the render thread (eliminates the 65,672-unit D11 FPS drop).
 *   - Direct O(1) local player avatar Unit* lookup via LocalUser -> PlayerManager -> EntityManager
 *     descriptor reference -> LOOKUP_UNIT (eliminates co-op teammate avatar hijack as well).
 *   - SRWLOCK + monotonic generation walker registry (render-thread-private unit_ref cache in g_rt).
 *   - Render-thread-exclusive g_rt / g_health / g_watchdog synchronized via atomic g_cacheEpoch.
 *   - Fail-closed Stingray Unit vtable pin verification (ECP_EXE_UNIT_VTABLE) before indirect calls.
 *   - Direct <= 32-child attachment garment check (removes 65k world-link fallback scan).
 *   - Unoverwritten-pose guard preventing cumulative camera drift while paused or sub-ticking.
 *   - 30-second QPC health telemetry + hard 1000 us safety watchdog that fails closed on overrun. */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xmmintrin.h>

#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "hellforge_mod.h"
#include "pullback_pins.h"
#include "pullback_options.h"
#include "pullback_math.h"

typedef void (*EcpStepFn)(void* app);
typedef void (*EcpSetLocalPoseFn)(uint32_t unit_ref, int32_t node, const float pose[16]);
typedef void (*EcpUpdateUnitFn)(uintptr_t world, uintptr_t unit);
typedef uintptr_t (*EcpLookupUnitFn)(uint32_t unit_ref);
typedef uintptr_t (*EcpMainWorldFn)(void);
typedef uintptr_t (*EcpUnitGraphFn)(uintptr_t unit);
typedef int (*EcpUnitCamCountFn)(uintptr_t unit);
typedef uintptr_t (*EcpUnitCameraFn)(uintptr_t unit, uint32_t index);

typedef enum EcpProfileKind {
    ECP_PROFILE_NONE = 0,
    ECP_PROFILE_WALKER = 1,
    ECP_PROFILE_WALKER_ATST_SCALED = 2,
    ECP_PROFILE_LEGO = 3
} EcpProfileKind;

typedef enum EcpBypassReason {
    ECP_BYPASS_NONE = 0,
    ECP_BYPASS_DISABLED = 1,
    ECP_BYPASS_DISARMED = 2,
    ECP_BYPASS_WATCHDOG = 3,
    ECP_BYPASS_TOGGLED_OFF = 4,
    ECP_BYPASS_TERMINAL_CAM = 5,
    ECP_BYPASS_PLAYER_NOT_ALIVE = 6,
    ECP_BYPASS_RAYCAST_UNREADY = 7,
    ECP_BYPASS_NO_WORLD = 8,
    ECP_BYPASS_CAM_SEARCHING = 9,
    ECP_BYPASS_CAM_POSE_INVALID = 10,
    ECP_BYPASS_NO_SUBJECT = 11,
    ECP_BYPASS_FOV = 12,
    ECP_BYPASS_SIGHTS_DISTANCE = 13,
    ECP_BYPASS_PARENT_LINK = 14,
    ECP_BYPASS_ARMORY_PREVIEW = 15,
    ECP_BYPASS_WALL_BLOCKED = 16
} EcpBypassReason;

/* 24-byte EntityManager descriptor at em + 0xF32F18 + slot * 24 (hellforge_globals.h:78-88). */
typedef struct EcpEntityDescriptor {
    uint64_t resource;  /* +0x00 entity resource hash */
    uint32_t entity;    /* +0x08 session entity id */
    uint32_t reference; /* +0x0C Stingray unit_ref handle */
    uint32_t network;   /* +0x10 network id (0x7FFF when unnetworked) */
    uint32_t flags;     /* +0x14 bit 0 = authority */
} EcpEntityDescriptor;

typedef struct EcpWalkerSlot {
    int valid;
    uint32_t generation;
    uint32_t entity;
    uint32_t network;
    uint64_t entity_res;
} EcpWalkerSlot;

typedef struct EcpWalkerRefCache {
    uint32_t entity;
    uint32_t generation;
    uint32_t unit_ref;
} EcpWalkerRefCache;

static const HfApi* g_hf;
static HfMod* g_mod;
static uintptr_t g_exeBase;
static uintptr_t g_gameBase;

static EcpOptions g_opts;
static volatile LONG g_disarmed = 1;     /* Starts disarmed until entering an allowed game state. */
static volatile LONG g_sessionEnded = 0; /* Latches 1 permanently on HF_EVENT_SESSION_END / unload. */
static volatile LONG g_cacheEpoch = 1;   /* Incremented by lifecycle callbacks; consumed on render thread. */
static volatile LONG g_transitionLogs = 0;
static volatile LONG g_userToggledOff = 0;
static int g_prevKeyDown = 0;

static EcpStepFn g_origStep;
static EcpSetLocalPoseFn g_setLocalPose;
static EcpUpdateUnitFn g_updateUnit;
static EcpLookupUnitFn g_lookupUnit;
static EcpMainWorldFn g_mainWorld;
static EcpRaycastFn g_raycast;
static int g_raycastState; /* 0 = unchecked, 1 = verified, -1 = unavailable (fail-closed) */
static int g_hookInstalled;

/* SRWLOCK-synchronized HF_ON_SPAWN walker registry & modded walker entity resource table. */
static SRWLOCK g_walkerLock = SRWLOCK_INIT;
static EcpWalkerSlot g_walkerSlots[ECP_MAX_TRACKED_WALKERS];
static uint32_t g_walkerGeneration = 0u;
static uint32_t g_walkerWriteCursor = 0u;
static uint64_t g_customWalkerEntities[ECP_MAX_CUSTOM_WALKERS];
static uint32_t g_customWalkerCount = 0u;

/* QPC timing, 30-second health telemetry, and hard frame-budget watchdog (mutated strictly on render thread). */
static uint64_t g_qpf = 0ULL;
#ifdef ECP_UNIT_TEST
static uint64_t g_testSyntheticUs = 0ULL;
static ULONGLONG g_testTickMsOverride = 0ULL;
#endif

static struct {
    volatile LONG tripped;
    uint32_t slow_score;
    uint32_t trip_count;
    uint64_t last_slow_us;
} g_watchdog;

static struct {
    uint64_t tick_us_sum;
    uint64_t tick_us_max;
    uint64_t tick_count;
    ULONGLONG next_log_ms;
    uint32_t row;
} g_health;

/* Cached render-camera & subject state (exclusively read & mutated on the render thread). */
static struct {
    LONG seen_epoch;
    uintptr_t world;
    uint32_t cam_ref;
    uint32_t cam_scan_step;
    uint32_t cam_best_ref;
    float cam_best_sq;
    int cam_retry_wait;
    int cam_logged;

    uintptr_t avatar_unit;
    uint32_t avatar_ref;
    uint64_t avatar_res;
    int32_t head_node;
    uintptr_t last_avatar_link;
    uintptr_t last_first_child;
    uint32_t garment_recheck_tick;
    uint32_t ship_scan_cursor;
    int has_lego_garment;

    uintptr_t walker_unit;
    uint32_t walker_ref;
    uint64_t walker_res;
    int32_t walker_pelvis_node;
    uint32_t walker_scan_cursor;
    uint32_t walker_bg_tick;
    int walker_bg_sweep_done;
    EcpWalkerRefCache walker_slot_cache[ECP_MAX_TRACKED_WALKERS];

    int has_last_written;
    uintptr_t last_written_world;
    uint32_t last_written_cam_ref;
    float last_base_pose[16];
    float last_written_pose[16];

    float blend;
    EcpProfileKind active_profile;
    EcpProfileKind logged_profile;
    EcpBypassReason last_bypass;
    ULONGLONG last_tick_ms;
    uint32_t applied_frames;
    uint32_t raycast_hits;
} g_rt;

static const char* EcpProfileName(EcpProfileKind kind)
{
    switch (kind) {
        case ECP_PROFILE_WALKER: return "walker";
        case ECP_PROFILE_WALKER_ATST_SCALED: return "walker_atst_scaled";
        case ECP_PROFILE_LEGO: return "lego";
        default: return "none";
    }
}

static const char* EcpBypassReasonName(EcpBypassReason reason)
{
    switch (reason) {
        case ECP_BYPASS_NONE: return "active";
        case ECP_BYPASS_DISABLED: return "disabled";
        case ECP_BYPASS_DISARMED: return "disarmed";
        case ECP_BYPASS_WATCHDOG: return "watchdog_tripped";
        case ECP_BYPASS_TOGGLED_OFF: return "toggled_off";
        case ECP_BYPASS_TERMINAL_CAM: return "terminal_cam";
        case ECP_BYPASS_PLAYER_NOT_ALIVE: return "player_not_alive";
        case ECP_BYPASS_RAYCAST_UNREADY: return "raycast_unready";
        case ECP_BYPASS_NO_WORLD: return "no_world";
        case ECP_BYPASS_CAM_SEARCHING: return "cam_searching";
        case ECP_BYPASS_CAM_POSE_INVALID: return "cam_pose_invalid";
        case ECP_BYPASS_NO_SUBJECT: return "no_subject";
        case ECP_BYPASS_FOV: return "fov_bypass";
        case ECP_BYPASS_SIGHTS_DISTANCE: return "sights_or_distance";
        case ECP_BYPASS_PARENT_LINK: return "parent_link";
        case ECP_BYPASS_ARMORY_PREVIEW: return "armory_preview";
        case ECP_BYPASS_WALL_BLOCKED: return "wall_blocked";
        default: return "unknown";
    }
}

static void EcpSetStatus(const char* text)
{
    if (g_hf != NULL && g_hf->size >= offsetof(HfApi, set_status) + sizeof(void*) && g_hf->set_status != NULL) {
        g_hf->set_status(g_mod, text);
    }
}

static ULONGLONG EcpNowMs(void)
{
#ifdef ECP_UNIT_TEST
    if (g_testTickMsOverride != 0ULL) return g_testTickMsOverride;
#endif
    return GetTickCount64();
}

static LARGE_INTEGER EcpQpcStart(void)
{
    if (g_qpf == 0ULL) {
        LARGE_INTEGER f;
        if (QueryPerformanceFrequency(&f) && f.QuadPart > 0) {
            g_qpf = (uint64_t)f.QuadPart;
        } else {
            g_qpf = 10000000ULL;
        }
    }
    LARGE_INTEGER t0;
    QueryPerformanceCounter(&t0);
    return t0;
}

static uint64_t EcpElapsedUs(LARGE_INTEGER t0)
{
    LARGE_INTEGER t1;
    QueryPerformanceCounter(&t1);
    LONGLONG diff = t1.QuadPart - t0.QuadPart;
    uint64_t us = diff > 0 && g_qpf > 0ULL ? (uint64_t)((diff * 1000000LL) / (LONGLONG)g_qpf) : 0ULL;
#ifdef ECP_UNIT_TEST
    us += g_testSyntheticUs;
#endif
    return us;
}

static int EcpRead(uintptr_t addr, void* out, size_t bytes)
{
    if (addr < 0x10000u || g_hf == NULL || g_hf->read == NULL) return 0;
    return g_hf->read(addr, out, bytes) == 0 ? 1 : 0;
}

static int EcpReadPtr(uintptr_t addr, uintptr_t* out)
{
    uintptr_t v = 0;
    if (!EcpRead(addr, &v, sizeof(v)) || v < 0x10000u) return 0;
    *out = v;
    return 1;
}

static void EcpClearWalkerSlots(void)
{
    AcquireSRWLockExclusive(&g_walkerLock);
    for (uint32_t i = 0; i < ECP_MAX_TRACKED_WALKERS; i++) {
        g_walkerSlots[i].valid = 0;
        g_walkerSlots[i].generation = ++g_walkerGeneration;
        g_walkerSlots[i].entity = 0u;
        g_walkerSlots[i].network = 0u;
        g_walkerSlots[i].entity_res = 0ULL;
    }
    g_walkerWriteCursor = 0u;
    ReleaseSRWLockExclusive(&g_walkerLock);
}

static void EcpForgetRuntimeCache(void)
{
    LONG saved_epoch = g_rt.seen_epoch;
    memset(&g_rt, 0, sizeof(g_rt));
    g_rt.seen_epoch = saved_epoch;
    g_rt.head_node = -1;
    g_rt.walker_pelvis_node = -1;
    g_rt.cam_best_sq = 4.0f;
    g_rt.last_bypass = ECP_BYPASS_DISARMED;
}

/* Resets render-thread-private cache, telemetry window, and per-mission watchdog strictly on the render thread. */
static void EcpSyncEpochOnRenderThread(LONG epoch)
{
    EcpForgetRuntimeCache();
    g_rt.seen_epoch = epoch;
    InterlockedExchange(&g_watchdog.tripped, 0);
    g_watchdog.slow_score = 0u;
    g_watchdog.last_slow_us = 0ULL;
    g_health.tick_us_sum = 0ULL;
    g_health.tick_us_max = 0ULL;
    g_health.tick_count = 0ULL;
    g_health.next_log_ms = EcpNowMs() + ECP_HEALTH_FIRST_MS;
}

/* ---- Stingray Unit & SceneGraph Readers ------------------------------------------------------------------------- */

static uintptr_t EcpUnitVtableMethod(uintptr_t unit, uint32_t slot_offset)
{
    uintptr_t vtbl = 0, fn = 0;
    if (g_exeBase == 0 || !EcpReadPtr(unit, &vtbl) || vtbl != g_exeBase + ECP_EXE_UNIT_VTABLE) {
        return 0;
    }
    if (!EcpReadPtr(vtbl + slot_offset, &fn)) return 0;
    return fn;
}

static uintptr_t EcpUnitGraph(uintptr_t unit)
{
    uintptr_t fn = EcpUnitVtableMethod(unit, ECP_UNIT_VTABLE_GRAPH);
    return fn != 0 ? ((EcpUnitGraphFn)fn)(unit) : 0;
}

static int EcpUnitCameraCount(uintptr_t unit)
{
    uintptr_t fn = EcpUnitVtableMethod(unit, ECP_UNIT_VTABLE_CAM_COUNT);
    return fn != 0 ? ((EcpUnitCamCountFn)fn)(unit) : 0;
}

static uintptr_t EcpUnitCamera(uintptr_t unit, int index)
{
    if (index < 0 || index >= EcpUnitCameraCount(unit)) return 0;
    uintptr_t fn = EcpUnitVtableMethod(unit, ECP_UNIT_VTABLE_CAMERA);
    return fn != 0 ? ((EcpUnitCameraFn)fn)(unit, (uint32_t)index) : 0;
}

static uint32_t EcpUnitRef(uintptr_t unit)
{
    uint32_t ref = 0;
    return EcpRead(unit + ECP_UNIT_REF_OFFSET, &ref, sizeof(ref)) ? ref : 0u;
}

static uint64_t EcpUnitResource(uintptr_t unit)
{
    uintptr_t res_ptr = 0;
    uint64_t res_id = 0;
    if (!EcpReadPtr(unit + ECP_UNIT_RES_PTR_OFFSET, &res_ptr)) return 0ULL;
    return EcpRead(res_ptr, &res_id, sizeof(res_id)) ? res_id : 0ULL;
}

static uint32_t EcpGraphNodeCount(uintptr_t graph)
{
    uint32_t count = 0;
    if (!EcpRead(graph + ECP_GRAPH_NODE_COUNT, &count, sizeof(count)) || count == 0 || count > ECP_GRAPH_MAX_NODES) {
        return 0u;
    }
    return count;
}

static uintptr_t EcpUnitLinkParentPoses(uintptr_t unit)
{
    uintptr_t graph = EcpUnitGraph(unit), link = 0;
    if (graph == 0 || !EcpRead(graph + ECP_GRAPH_LINK_PARENT, &link, sizeof(link))) return 0;
    return link >= 0x10000u ? link : 0;
}

static uintptr_t EcpUnitWorldPosesPtr(uintptr_t unit, uint32_t* out_count)
{
    uintptr_t graph = EcpUnitGraph(unit), poses = 0;
    uint32_t count = graph != 0 ? EcpGraphNodeCount(graph) : 0u;
    if (out_count) *out_count = count;
    if (count == 0 || !EcpReadPtr(graph + ECP_GRAPH_WORLD_POSES, &poses)) {
        if (out_count) *out_count = 0u;
        return 0;
    }
    return poses;
}

static int EcpUnitWorldPose(uintptr_t unit, int32_t node, float out_matrix[16])
{
    if (node < 0) return 0;
    uint32_t count = 0;
    uintptr_t poses = EcpUnitWorldPosesPtr(unit, &count);
    if (poses == 0 || (uint32_t)node >= count) return 0;
    return EcpRead(poses + (uintptr_t)node * 64u, out_matrix, 16u * sizeof(float));
}

/* Chunked bone-hash lookup (64 hashes = 256 bytes per read instead of 1 read per bone). */
static int32_t EcpUnitFindNode(uintptr_t unit, uint32_t name_hash)
{
    uintptr_t graph = EcpUnitGraph(unit), names = 0;
    uint32_t count = graph != 0 ? EcpGraphNodeCount(graph) : 0u;
    if (count == 0 || !EcpReadPtr(graph + ECP_GRAPH_NODE_NAMES, &names)) return -1;

    uint32_t chunk_buf[64];
    for (uint32_t base = 0; base < count; base += 64u) {
        uint32_t n = (count - base) < 64u ? (count - base) : 64u;
        if (!EcpRead(names + (uintptr_t)base * sizeof(uint32_t), chunk_buf, (size_t)n * sizeof(uint32_t))) {
            return -1;
        }
        for (uint32_t j = 0; j < n; j++) {
            if (chunk_buf[j] == name_hash) return (int32_t)(base + j);
        }
    }
    return -1;
}

static uint32_t EcpWorldUnits(uintptr_t world, uintptr_t* out_array)
{
    *out_array = 0;
    uint32_t count = 0;
    if (!EcpRead(world + ECP_WORLD_UNITS_COUNT, &count, sizeof(count)) || count == 0 || count > ECP_WORLD_UNITS_MAX) {
        return 0u;
    }
    if (!EcpReadPtr(world + ECP_WORLD_UNITS_ARRAY, out_array)) return 0u;
    return count;
}

static uintptr_t EcpWorldUnitAt(uintptr_t array, uint32_t index)
{
    uintptr_t u = 0;
    return EcpReadPtr(array + (uintptr_t)index * sizeof(uintptr_t), &u) ? u : 0;
}

/* ---- Unoverwritten-Pose Guard (Zero Drift While Paused / Sub-Ticking) ------------------------------------------- */

static int EcpPoseMatches12(const float a[16], const float b[16])
{
    for (int i = 0; i < 12; i++) {
        if (!isfinite(a[i]) || !isfinite(b[i]) || fabsf(a[i] - b[i]) > 1e-5f) return 0;
    }
    return 1;
}

static void EcpRestoreBasePoseIfNeeded(void)
{
    if (!g_rt.has_last_written) return;
    g_rt.has_last_written = 0;
    if (g_mainWorld == NULL || g_lookupUnit == NULL || g_setLocalPose == NULL || g_updateUnit == NULL) return;
    if (InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        return;
    }
    uintptr_t world = g_mainWorld();
    if (world == 0 || world != g_rt.last_written_world || g_rt.last_written_cam_ref == 0u) return;

    uintptr_t cam_unit = g_lookupUnit(g_rt.last_written_cam_ref);
    uintptr_t cam_graph = cam_unit != 0 ? EcpUnitGraph(cam_unit) : 0;
    uintptr_t local_poses = 0;
    float cur_pose[16];
    if (cam_graph == 0 || !EcpReadPtr(cam_graph + ECP_GRAPH_LOCAL_POSES, &local_poses) ||
        !EcpRead(local_poses, cur_pose, sizeof(cur_pose))) {
        return;
    }
    if (EcpPoseMatches12(cur_pose, g_rt.last_written_pose)) {
        g_setLocalPose(g_rt.last_written_cam_ref, 0, g_rt.last_base_pose);
        g_updateUnit(world, cam_unit);
    }
}

/* ---- Bounded O(1) Engine Hash & EntityManager Lookups ----------------------------------------------------------- */

static int EcpHashLookupU32(uintptr_t map_header, uint32_t key, uint32_t stride, uint32_t val_offset, uint32_t* out_val)
{
    uint8_t hdr[20];
    if (key == 0 || !EcpRead(map_header, hdr, sizeof(hdr))) return 0;
    uint64_t buckets = 0;
    uint32_t capacity = 0, empty = 0, mult = 0;
    memcpy(&buckets, hdr, 8);
    memcpy(&capacity, hdr + 8, 4);
    memcpy(&empty, hdr + 12, 4);
    memcpy(&mult, hdr + 16, 4);
    if (buckets < 0x10000u || capacity == 0 || capacity > 1048576u || key == empty) return 0;

    uint32_t start = key * mult;
    uint32_t max_probes = capacity < ECP_HASH_PROBE_MAX ? capacity : ECP_HASH_PROBE_MAX;
    int pow2 = (capacity & (capacity - 1u)) == 0u;

    for (uint32_t i = 0; i < max_probes; i++) {
        uint32_t slot = pow2 ? ((start + i) & (capacity - 1u)) : ((start + i) % capacity);
        uintptr_t slot_addr = (uintptr_t)(buckets + (uint64_t)slot * stride);
        uint32_t k = 0;
        if (!EcpRead(slot_addr, &k, sizeof(k))) return 0;
        if (k == key) {
            uint32_t v = 0xFFFFFFFFu;
            if (!EcpRead(slot_addr + val_offset, &v, sizeof(v)) || v == 0xFFFFFFFFu) return 0;
            *out_val = v;
            return 1;
        }
        if (k == empty) return 0;
    }
    return 0;
}

static uintptr_t EcpGetEntityManager(void)
{
    uintptr_t em = g_mod != NULL ? (uintptr_t)hf_global_EntityManager(g_mod) : 0;
    if (em == 0 && g_gameBase != 0) {
        EcpReadPtr(g_gameBase + ECP_GAME_ENTITIES_GLOBAL, &em);
    }
    return em;
}

static int EcpReadDescriptorByNetwork(uintptr_t em, uint32_t net_id, EcpEntityDescriptor* out_desc)
{
    if (em == 0 || net_id == 0 || net_id == ECP_NET_ID_NONE || !out_desc) return 0;
    uint32_t idx = 0;
    if (!EcpHashLookupU32(em + ECP_EM_NETWORK_MAP, net_id, 8u, 4u, &idx) || idx >= ECP_EM_DESC_MAX) {
        return 0;
    }
    EcpEntityDescriptor d;
    if (!EcpRead(em + ECP_EM_DESCRIPTORS + (uintptr_t)idx * ECP_EM_DESC_STRIDE, &d, sizeof(d))) {
        return 0;
    }
    if (d.entity == 0u || d.entity == 0xFFFFFFFFu || d.network != net_id) return 0;
    *out_desc = d;
    return 1;
}

static int EcpReadDescriptorByEntity(uintptr_t em, uint32_t entity_id, EcpEntityDescriptor* out_desc)
{
    if (em == 0 || entity_id == 0 || entity_id == 0xFFFFFFFFu || !out_desc) return 0;
    uint32_t idx = 0;
    if (!EcpHashLookupU32(em + ECP_EM_ENTITY_MAP, entity_id, 8u, 4u, &idx) || idx >= ECP_EM_DESC_MAX) {
        return 0;
    }
    EcpEntityDescriptor d;
    if (!EcpRead(em + ECP_EM_DESCRIPTORS + (uintptr_t)idx * ECP_EM_DESC_STRIDE, &d, sizeof(d))) {
        return 0;
    }
    if (d.entity != entity_id) return 0;
    *out_desc = d;
    return 1;
}

static int EcpIsWalkerEntityResource(uint64_t res)
{
    if (res == 0ULL) return 0;
    if (ecp_is_walker_resource(res)) return 1;
    int match = 0;
    AcquireSRWLockShared(&g_walkerLock);
    for (uint32_t i = 0; i < g_customWalkerCount && i < ECP_MAX_CUSTOM_WALKERS; i++) {
        if (g_customWalkerEntities[i] == res) {
            match = 1;
            break;
        }
    }
    ReleaseSRWLockShared(&g_walkerLock);
    return match;
}

static void EcpRefreshCustomWalkerEntities(HfMod* mod)
{
    uint64_t tmp[ECP_MAX_CUSTOM_WALKERS];
    uint32_t added = 0u;
    if (g_hf != NULL && mod != NULL &&
        g_hf->size >= offsetof(HfApi, content) + sizeof(void*) && g_hf->content != NULL) {
        for (uint32_t idx = 0; idx < 256u && added < ECP_MAX_CUSTOM_WALKERS; idx++) {
            HfContent c;
            memset(&c, 0, sizeof(c));
            c.size = (uint32_t)sizeof(c);
            if (!g_hf->content(mod, idx, &c)) break;
            if (c.kind == HF_KIND_ENTITY && c.id != 0ULL && ecp_is_walker_resource(c.base)) {
                tmp[added++] = c.id;
            }
        }
    }
    AcquireSRWLockExclusive(&g_walkerLock);
    g_customWalkerCount = added;
    if (added > 0u) {
        memcpy(g_customWalkerEntities, tmp, (size_t)added * sizeof(uint64_t));
    }
    ReleaseSRWLockExclusive(&g_walkerLock);
}

/* Direct Stingray Unit child attachment walk (avatar + 0x1D8 -> child + 0x1E0, <= 32 children).
 * ZERO world->units scan: Helldiver armor garments attach directly on the avatar's child list. */
static int EcpAvatarHasCompactLegoChild(uintptr_t avatar_unit, uintptr_t* out_first_child)
{
    if (out_first_child) *out_first_child = 0;
    if (avatar_unit == 0) return 0;

    uintptr_t first = 0;
    if (!EcpRead(avatar_unit + ECP_UNIT_CHILD_FIRST, &first, sizeof(first)) || first < 0x10000u) {
        return 0;
    }
    if (out_first_child) *out_first_child = first;

    uintptr_t visited[ECP_MAX_AVATAR_CHILDREN];
    uint32_t n_vis = 0;
    uintptr_t child = first;
    while (child >= 0x10000u && n_vis < ECP_MAX_AVATAR_CHILDREN) {
        for (uint32_t k = 0; k < n_vis; k++) {
            if (visited[k] == child) return 0;
        }
        visited[n_vis++] = child;

        uint64_t res = EcpUnitResource(child);
        if (res != 0ULL) {
            uintptr_t cgraph = EcpUnitGraph(child);
            uint32_t nodes = cgraph != 0 ? EcpGraphNodeCount(cgraph) : 0u;
            if (ecp_match_lego_garment(res, nodes)) return 1;
        }

        uintptr_t next = 0;
        if (!EcpRead(child + ECP_UNIT_CHILD_NEXT, &next, sizeof(next))) break;
        child = next;
    }
    return 0;
}

static void EcpBindAvatarUnit(uintptr_t u, uint32_t ref, uint64_t res)
{
    int same_unit = (u == g_rt.avatar_unit && ref == g_rt.avatar_ref && res == g_rt.avatar_res);
    g_rt.avatar_unit = u;
    g_rt.avatar_ref = ref;
    g_rt.avatar_res = res;

    if (!same_unit || g_rt.head_node < 0) {
        g_rt.head_node = EcpUnitFindNode(u, ECP_NODE_HASH_HEAD);
    }

    uintptr_t first_child = 0;
    EcpRead(u + ECP_UNIT_CHILD_FIRST, &first_child, sizeof(first_child));
    if (first_child < 0x10000u) first_child = 0;

    if (!same_unit || first_child != g_rt.last_first_child || (++g_rt.garment_recheck_tick >= 60u)) {
        g_rt.garment_recheck_tick = 0u;
        g_rt.has_lego_garment = EcpAvatarHasCompactLegoChild(u, &first_child);
        g_rt.last_first_child = first_child;
    }
}

/* Verifies local player (slots 0..3) is alive AND resolves the local Helldiver avatar Unit* in O(1)
 * via EntityManager descriptor reference -> g_lookupUnit(reference).
 * Returns 1 if local player is alive (setting g_rt.avatar_unit when resolved), 0 if dead/downed/unspawned. */
static int EcpResolveLocalPlayerAndAvatar(
    uintptr_t world,
    EcpVec3 cam_pos,
    EcpVec3 cam_fwd,
    uint32_t game_state,
    LARGE_INTEGER frame_t0)
{
    if (g_gameBase == 0 || g_lookupUnit == NULL) return 0;

    uintptr_t local_user = 0, pm = 0;
    if (g_mod != NULL) {
        local_user = (uintptr_t)hf_global_LocalUser(g_mod);
        pm = (uintptr_t)hf_global_PlayerManager(g_mod);
    }
    if (local_user == 0) EcpReadPtr(g_gameBase + ECP_GAME_LOCALUSER_GLOBAL, &local_user);
    if (pm == 0 && !EcpReadPtr(g_gameBase + ECP_GAME_PLAYERS_GLOBAL, &pm)) return 0;

    uint64_t local_peer = 0;
    if (local_user != 0) {
        EcpRead(local_user + ECP_LOCALUSER_PEER_OFFSET, &local_peer, sizeof(local_peer));
    }

    uint32_t count = 0;
    if (!EcpRead(pm + ECP_PLAYERS_COUNT_OFFSET, &count, sizeof(count)) || count == 0u || count > ECP_PLAYERS_MAX) {
        return 0;
    }

    for (uint32_t slot = 0; slot < count; slot++) {
        uint64_t peer = 0;
        if (!EcpRead(pm + ECP_PLAYERS_PEER_OFFSET + (uintptr_t)slot * ECP_PLAYERS_SLOT_STRIDE, &peer, sizeof(peer))) {
            continue;
        }
        int is_local = 0;
        if (local_peer != 0ULL) {
            is_local = (peer == local_peer);
        } else {
            uintptr_t pdesc = 0;
            uint8_t local_flag = 0;
            if (EcpReadPtr(pm + ECP_PLAYERS_DESC_OFFSET + (uintptr_t)slot * sizeof(uintptr_t), &pdesc) &&
                EcpRead(pdesc + ECP_PLAYERS_LOCAL_FLAG, &local_flag, sizeof(local_flag))) {
                is_local = (local_flag & 1u) != 0;
            }
        }
        if (!is_local) continue;

        uint32_t lifecycle = 0;
        if (!EcpRead(pm + ECP_PLAYERS_LIFE_OFFSET + (uintptr_t)slot * ECP_PLAYERS_SLOT_STRIDE, &lifecycle, sizeof(lifecycle)) ||
            lifecycle != ECP_PLAYER_LIFE_SPAWNED) {
            g_rt.avatar_unit = 0;
            return 0;
        }

        uint32_t net_id = ECP_NET_ID_NONE;
        EcpRead(pm + ECP_PLAYERS_NET_OFFSET + (uintptr_t)slot * ECP_PLAYERS_NET_STRIDE, &net_id, sizeof(net_id));

        uintptr_t em = EcpGetEntityManager();
        uint32_t avatar_entity = 0u;
        uint32_t avatar_unit_ref = 0u;

        if (net_id != 0u && net_id != ECP_NET_ID_NONE) {
            EcpEntityDescriptor desc;
            if (EcpReadDescriptorByNetwork(em, net_id, &desc)) {
                avatar_entity = desc.entity;
                if (desc.resource == ECP_RES_HELLDIVER_AVATAR) {
                    avatar_unit_ref = desc.reference;
                }
            }
            if (avatar_entity == 0u && g_hf != NULL &&
                g_hf->size >= offsetof(HfApi, entity_of_network) + sizeof(void*) &&
                g_hf->entity_of_network != NULL && g_mod != NULL) {
                uint32_t ent = g_hf->entity_of_network(g_mod, net_id);
                if (ent != 0u && ent != 0xFFFFFFFFu) {
                    avatar_entity = ent;
                    EcpEntityDescriptor edesc;
                    if (avatar_unit_ref == 0u && EcpReadDescriptorByEntity(em, ent, &edesc) &&
                        edesc.resource == ECP_RES_HELLDIVER_AVATAR) {
                        avatar_unit_ref = edesc.reference;
                    }
                }
            }
        }

        if (game_state == HF_STATE_MISSION) {
            if (avatar_entity == 0u) {
                g_rt.avatar_unit = 0;
                return 0;
            }
            uintptr_t hm = g_mod != NULL ? (uintptr_t)hf_global_HealthManager(g_mod) : 0;
            if (hm == 0 && !EcpReadPtr(g_gameBase + ECP_GAME_HEALTH_GLOBAL, &hm)) {
                g_rt.avatar_unit = 0;
                return 0;
            }

            uint32_t hm_count = 0, hm_index = 0;
            uintptr_t hm_records = 0;
            if (!EcpRead(hm + ECP_HM_COUNT_OFFSET, &hm_count, sizeof(hm_count)) ||
                !EcpHashLookupU32(hm + ECP_HM_ENTITY_MAP, avatar_entity, 8u, 4u, &hm_index) ||
                hm_index >= hm_count || !EcpReadPtr(hm + ECP_HM_RECORDS_PTR, &hm_records)) {
                g_rt.avatar_unit = 0;
                return 0;
            }

            int32_t health = 0;
            uint32_t hm_life = 99u;
            uintptr_t rec = hm_records + (uintptr_t)hm_index * ECP_HM_RECORD_STRIDE;
            if (!EcpRead(rec + ECP_HM_HEALTH_OFFSET, &health, sizeof(health)) ||
                !EcpRead(rec + ECP_HM_LIFE_OFFSET, &hm_life, sizeof(hm_life)) ||
                health <= 0 || hm_life != ECP_HM_LIFE_ALIVE) {
                g_rt.avatar_unit = 0;
                return 0;
            }
        }

        /* 1. Direct O(1) resolution from EntityManager descriptor's Stingray unit_ref. */
        if (avatar_unit_ref != 0u) {
            uintptr_t u = g_lookupUnit(avatar_unit_ref);
            uint64_t res = u != 0 ? EcpUnitResource(u) : 0ULL;
            if (u != 0 && EcpUnitRef(u) == avatar_unit_ref && res == ECP_RES_HELLDIVER_AVATAR) {
                EcpBindAvatarUnit(u, avatar_unit_ref, res);
                return 1;
            }
            /* EntityManager is authoritative for the local player's avatar in missions; if its unit handle
             * is not currently live (e.g. mid-despawn), fail closed instead of scanning world units. */
            g_rt.avatar_unit = 0;
            return 1;
        }

        /* 2. Cached handle check (O(1)) when EntityManager has no network descriptor (e.g. solo ship walk
         *    or minimal unit test without EntityManager descriptors). */
        if (world != 0 && g_rt.avatar_ref != 0u) {
            uintptr_t u = g_lookupUnit(g_rt.avatar_ref);
            uint64_t res = u != 0 ? EcpUnitResource(u) : 0ULL;
            if (u != 0 && EcpUnitRef(u) == g_rt.avatar_ref && res != 0ULL &&
                (res == ECP_RES_HELLDIVER_AVATAR || game_state == HF_STATE_SHIP)) {
                EcpBindAvatarUnit(u, g_rt.avatar_ref, res);
                return 1;
            }
        }

        /* 3. Strictly bounded micro-slice (<= 16 units/frame, QPC < 35 us) ONLY when em == 0 or HF_STATE_SHIP. */
        if (world != 0 && (em == 0 || game_state == HF_STATE_SHIP)) {
            uintptr_t units = 0;
            uint32_t wcount = EcpWorldUnits(world, &units);
            if (wcount > 0u) {
                uint32_t limit = wcount < ECP_SHIP_SCAN_SLICE_MAX ? wcount : ECP_SHIP_SCAN_SLICE_MAX;
                for (uint32_t step = 0; step < limit; step++) {
                    if (step > 0u && EcpElapsedUs(frame_t0) >= ECP_SLICE_BUDGET_US) break;
                    uint32_t idx = (g_rt.ship_scan_cursor++) % wcount;
                    uintptr_t u = EcpWorldUnitAt(units, idx);
                    if (u == 0) continue;
                    uint64_t res = EcpUnitResource(u);
                    if (res == ECP_RES_HELLDIVER_AVATAR) {
                        float m[16];
                        EcpVec3 root;
                        if (EcpUnitWorldPose(u, 0, m) && ecp_world_pose_valid(m, &root, NULL)) {
                            EcpVec3 to = ecp_sub(root, cam_pos);
                            if (ecp_dot(to, to) <= 144.0f) {
                                uint32_t ref = EcpUnitRef(u);
                                if (ref != 0u) {
                                    EcpBindAvatarUnit(u, ref, res);
                                    return 1;
                                }
                            }
                        }
                    } else if (game_state == HF_STATE_SHIP && g_opts.ship_walk_enabled && EcpUnitCameraCount(u) == 0) {
                        uintptr_t fc = 0;
                        if (!EcpAvatarHasCompactLegoChild(u, &fc)) continue;
                        int32_t head = EcpUnitFindNode(u, ECP_NODE_HASH_HEAD);
                        float m[16];
                        EcpVec3 hpos;
                        if (head >= 0 && EcpUnitWorldPose(u, head, m) && ecp_world_pose_valid(m, &hpos, NULL)) {
                            EcpVec3 to = ecp_sub(hpos, cam_pos);
                            float sq = ecp_dot(to, to);
                            if (sq >= 0.30f && sq <= 36.0f && ecp_dot(to, cam_fwd) > 0.0f) {
                                uint32_t ref = EcpUnitRef(u);
                                if (ref != 0u) {
                                    EcpBindAvatarUnit(u, ref, res);
                                    return 1;
                                }
                            }
                        }
                    }
                }
            }
        }

        g_rt.avatar_unit = 0;
        return 1;
    }

    g_rt.avatar_unit = 0;
    return 0;
}

static int EcpTerminalCameraIdle(void)
{
    if (g_hf == NULL || g_mod == NULL) return 0;
    if (g_hf->size < offsetof(HfApi, browse_state) + sizeof(void*) || g_hf->browse_state == NULL) {
        return 1;
    }
    HfBrowseState bs;
    memset(&bs, 0, sizeof(bs));
    bs.size = (uint32_t)sizeof(bs);
    g_hf->browse_state(g_mod, &bs);
    return (bs.camera == HF_BROWSE_CAMERA_NONE || bs.camera == HF_BROWSE_CAMERA_IDLE) ? 1 : 0;
}

/* ---- Synchronous Havok Raycast Verification --------------------------------------------------------------------- */

static int EcpEnsureRaycastReady(void)
{
    if (g_raycastState != 0) return g_raycastState == 1;
    if (g_exeBase == 0 || g_gameBase == 0 || g_hf == NULL) {
        g_raycastState = -1;
        return 0;
    }
    if (g_hf->size >= offsetof(HfApi, code_matches) + sizeof(void*) && g_hf->code_matches != NULL) {
        if (!g_hf->code_matches(g_exeBase + ECP_EXE_PHYSICS_RAYCAST, kEcpPinRaycast)) {
            g_raycastState = -1;
            if (g_hf->log) g_hf->log(g_mod, "raycast off: exe+0x%X bytes do not match build %s", ECP_EXE_PHYSICS_RAYCAST, ECP_EXPECTED_BUILD);
            return 0;
        }
    }
    uintptr_t phys = 0, slot_fn = 0;
    uint32_t query_hash = 0;
    if (!EcpReadPtr(g_gameBase + ECP_GAME_PHYSICS_GLOBAL, &phys) ||
        !EcpReadPtr(phys + ECP_PHYSICS_RAYCAST_SLOT, &slot_fn) ||
        slot_fn != g_exeBase + ECP_EXE_PHYSICS_RAYCAST ||
        !EcpRead(g_exeBase + ECP_EXE_RAYCAST_QUERIES + ECP_RAYCAST_QUERY_ROW * 20u + 12u, &query_hash, sizeof(query_hash)) ||
        query_hash != ECP_RAYCAST_QUERY_HASH) {
        if (phys != 0) {
            g_raycastState = -1;
            if (g_hf->log) g_hf->log(g_mod, "raycast off: physics slot or damage_los_query mismatch");
        }
        return 0;
    }
    g_raycast = (EcpRaycastFn)slot_fn;
    g_raycastState = 1;
    return 1;
}

/* ---- Bidirectional Time-Sliced Render Camera Discovery (<= 32 units/frame, QPC < 35 us) ------------------------- */

static int EcpProbeCameraUnitCandidate(uintptr_t units, uint32_t index, const float render_pose[16])
{
    uintptr_t u = EcpWorldUnitAt(units, index);
    if (u == 0 || EcpUnitCameraCount(u) <= 0) return 0;
    uintptr_t graph = EcpUnitGraph(u), local_poses = 0;
    float pose[12];
    if (graph == 0 || !EcpReadPtr(graph + ECP_GRAPH_LOCAL_POSES, &local_poses) ||
        !EcpRead(local_poses, pose, sizeof(pose))) {
        return 0;
    }
    float dx = pose[9] - render_pose[12];
    float dy = pose[10] - render_pose[13];
    float dz = pose[11] - render_pose[14];
    float sq = dx * dx + dy * dy + dz * dz;
    uint32_t ref = EcpUnitRef(u);
    if (ref != 0u && sq < g_rt.cam_best_sq) {
        g_rt.cam_best_sq = sq;
        g_rt.cam_best_ref = ref;
        return 1;
    }
    return 0;
}

static uint32_t EcpStepFindRenderCameraSliced(uintptr_t world, LARGE_INTEGER frame_t0)
{
    (void)frame_t0;
    uintptr_t units = 0;
    uint32_t count = EcpWorldUnits(world, &units);
    float render_pose[16];
    if (count == 0 || !EcpRead(world + ECP_WORLD_RENDER_POSE, render_pose, sizeof(render_pose))) return 0u;
    if (!isfinite(render_pose[12]) || !isfinite(render_pose[13]) || !isfinite(render_pose[14])) return 0u;

    /* Always probe the low-index hot prefix (units[0..7]) first: on build 25480438 the primary render
     * camera is typically unit index 0 (ref 0x00000001), which would otherwise be missed for minutes
     * if the scan cursor moved past index 0 during the Hellpod drop cutscene. */
    uint32_t hot_limit = count < ECP_CAM_HOT_PREFIX_COUNT ? count : ECP_CAM_HOT_PREFIX_COUNT;
    for (uint32_t h = 0u; h < hot_limit; h++) {
        EcpProbeCameraUnitCandidate(units, h, render_pose);
        if (g_rt.cam_best_ref != 0u && g_rt.cam_best_sq <= 0.25f) {
            uint32_t found = g_rt.cam_best_ref;
            g_rt.cam_best_ref = 0u;
            g_rt.cam_best_sq = 4.0f;
            return found;
        }
    }

    uint32_t half = (count + 1u) / 2u;
    if (g_rt.cam_scan_step >= half) {
        g_rt.cam_scan_step = 0u;
        g_rt.cam_best_ref = 0u;
        g_rt.cam_best_sq = 4.0f;
    }

    /* Measure slice budget locally so upstream O(1) avatar/health reads (~45-55 us) never starve discovery. */
    LARGE_INTEGER slice_t0 = EcpQpcStart();
    uint32_t probed = hot_limit;
    while (g_rt.cam_scan_step < half && probed < ECP_CAM_SCAN_SLICE_MAX) {
        if (probed > 0u && EcpElapsedUs(slice_t0) >= ECP_CAM_SLICE_BUDGET_US) break;

        uint32_t lo = g_rt.cam_scan_step;
        uint32_t hi = count - 1u - g_rt.cam_scan_step;
        g_rt.cam_scan_step++;

        if (lo >= hot_limit) {
            EcpProbeCameraUnitCandidate(units, lo, render_pose);
            probed++;
            if (g_rt.cam_best_ref != 0u && g_rt.cam_best_sq <= 0.25f) {
                break; /* Exact viewport camera match within 0.5m — done immediately. */
            }
        }

        if (hi > lo && hi >= hot_limit && probed < ECP_CAM_SCAN_SLICE_MAX) {
            EcpProbeCameraUnitCandidate(units, hi, render_pose);
            probed++;
            if (g_rt.cam_best_ref != 0u && g_rt.cam_best_sq <= 0.25f) {
                break;
            }
        }
    }

    if (g_rt.cam_best_ref != 0u) {
        uint32_t found = g_rt.cam_best_ref;
        g_rt.cam_best_ref = 0u;
        g_rt.cam_best_sq = 4.0f;
        return found;
    }

    if (g_rt.cam_scan_step >= half) {
        g_rt.cam_scan_step = 0u;
        g_rt.cam_best_sq = 4.0f;
        g_rt.cam_retry_wait = 15;
    }
    return 0u;
}

/* ---- O(1) Walker Subject Resolution (HF_ON_SPAWN + EntityManager + Micro-Sliced Fallback) ----------------------- */

static void EcpBindWalkerUnit(uintptr_t u, uint32_t ref, uint64_t res)
{
    int same = (u == g_rt.walker_unit && ref == g_rt.walker_ref && res == g_rt.walker_res);
    g_rt.walker_unit = u;
    g_rt.walker_ref = ref;
    g_rt.walker_res = res;
    if (!same || g_rt.walker_pelvis_node < 0) {
        int32_t pnode = EcpUnitFindNode(u, ECP_NODE_HASH_ATST_PELVIS);
        if (pnode < 0) {
            uintptr_t wg = EcpUnitGraph(u);
            uint32_t wnodes = wg != 0 ? EcpGraphNodeCount(wg) : 0u;
            if (wnodes >= 198u) pnode = 13;
            else if (wnodes == 106u) pnode = 12;
        }
        g_rt.walker_pelvis_node = pnode;
    }
}

static int EcpScoreWalkerUnit(uintptr_t u, EcpVec3 cam_pos, EcpVec3 cam_fwd, uintptr_t cur_link, float* inout_best_sq)
{
    if (u == 0) return 0;
    float m[16];
    EcpVec3 wroot;
    if (!EcpUnitWorldPose(u, 0, m) || !ecp_world_pose_valid(m, &wroot, NULL)) return 0;

    if (cur_link != 0) {
        uint32_t wnodes = 0;
        uintptr_t wwposes = EcpUnitWorldPosesPtr(u, &wnodes);
        if (wwposes == cur_link) {
            *inout_best_sq = 0.0f;
            return 1; /* Exact parent scene-graph match always wins. */
        }
    }

    EcpVec3 focus = ecp_add(wroot, ecp_v3(0.0f, 0.0f, 2.0f));
    EcpVec3 to_focus = ecp_sub(focus, cam_pos);
    float sq = ecp_dot(to_focus, to_focus);
    if (sq < 1.44f || sq > *inout_best_sq) return 0;
    EcpVec3 dir = to_focus;
    if (!ecp_normalize(&dir) || ecp_dot(dir, cam_fwd) < 0.45f) return 0;
    *inout_best_sq = sq;
    return 1;
}

static void EcpResolveWalkerSubject(
    uintptr_t world,
    EcpVec3 cam_pos,
    EcpVec3 cam_fwd,
    uintptr_t cur_link,
    LARGE_INTEGER frame_t0)
{
    if (!g_opts.walker_enabled || g_lookupUnit == NULL) {
        g_rt.walker_unit = 0;
        g_rt.walker_ref = 0u;
        return;
    }

    uintptr_t best_u = 0;
    uint32_t best_ref = 0u;
    uint64_t best_res = 0ULL;
    float best_sq = 14.0f * 14.0f;

    /* Tier 0: Re-verify currently cached walker handle in O(1). */
    if (g_rt.walker_ref != 0u) {
        uintptr_t u = g_lookupUnit(g_rt.walker_ref);
        uint64_t res = u != 0 ? EcpUnitResource(u) : 0ULL;
        if (u != 0 && EcpUnitRef(u) == g_rt.walker_ref && res == g_rt.walker_res &&
            EcpScoreWalkerUnit(u, cam_pos, cam_fwd, cur_link, &best_sq)) {
            best_u = u;
            best_ref = g_rt.walker_ref;
            best_res = res;
            if (best_sq == 0.0f) {
                EcpBindWalkerUnit(best_u, best_ref, best_res);
                return;
            }
        }
    }

    /* Tier 1: O(1) HF_ON_SPAWN walker table (SRWLOCK snapshot + render-thread-private unit_ref cache). */
    uintptr_t em = EcpGetEntityManager();
    EcpWalkerSlot slots_snap[ECP_MAX_TRACKED_WALKERS];
    AcquireSRWLockShared(&g_walkerLock);
    memcpy(slots_snap, g_walkerSlots, sizeof(slots_snap));
    ReleaseSRWLockShared(&g_walkerLock);

    for (uint32_t i = 0; i < ECP_MAX_TRACKED_WALKERS; i++) {
        if (!slots_snap[i].valid || slots_snap[i].entity == 0u) continue;
        uint32_t ent = slots_snap[i].entity;
        uint32_t net = slots_snap[i].network;
        uint32_t gen = slots_snap[i].generation;

        uint32_t uref = 0u;
        if (g_rt.walker_slot_cache[i].entity == ent && g_rt.walker_slot_cache[i].generation == gen) {
            uref = g_rt.walker_slot_cache[i].unit_ref;
        }

        uintptr_t u = uref != 0u ? g_lookupUnit(uref) : 0;
        if (u == 0 || EcpUnitRef(u) != uref) {
            EcpEntityDescriptor d;
            int ok = EcpReadDescriptorByEntity(em, ent, &d);
            if (!ok && net != 0u && net != ECP_NET_ID_NONE) {
                ok = EcpReadDescriptorByNetwork(em, net, &d) && d.entity == ent;
            }
            if (!ok) {
                g_rt.walker_slot_cache[i].unit_ref = 0u;
                if (em != 0) {
                    AcquireSRWLockExclusive(&g_walkerLock);
                    if (g_walkerSlots[i].valid && g_walkerSlots[i].generation == gen && g_walkerSlots[i].entity == ent) {
                        g_walkerSlots[i].valid = 0;
                        g_walkerSlots[i].generation = ++g_walkerGeneration;
                    }
                    ReleaseSRWLockExclusive(&g_walkerLock);
                }
                continue;
            }
            uref = d.reference;
            g_rt.walker_slot_cache[i].entity = ent;
            g_rt.walker_slot_cache[i].generation = gen;
            g_rt.walker_slot_cache[i].unit_ref = uref;
            u = uref != 0u ? g_lookupUnit(uref) : 0;
        }
        if (u == 0 || uref == 0u || EcpUnitRef(u) != uref) continue;
        uint64_t ures = EcpUnitResource(u);
        if (ures == 0ULL) continue;

        if (EcpScoreWalkerUnit(u, cam_pos, cam_fwd, cur_link, &best_sq)) {
            best_u = u;
            best_ref = uref;
            best_res = ures;
            if (best_sq == 0.0f) break;
        }
    }

    /* Tier 2: Bounded micro-sliced fallback (<= 16 units/frame, QPC < 35 us) for pre-existing walkers
     * on HF_LOAD_LIVE or synthetic test harnesses without HF_ON_SPAWN events. Never scans full world. */
    if (best_u == 0) {
        uintptr_t units = 0;
        uint32_t wcount = EcpWorldUnits(world, &units);
        int should_slice = (cur_link != 0) || (wcount > 0u && wcount <= ECP_WALKER_SCAN_SLICE_MAX) ||
                           (!g_rt.walker_bg_sweep_done && ((++g_rt.walker_bg_tick & 7u) == 0u));
        if (wcount > 0u && should_slice) {
            uint32_t max_slice = (cur_link != 0 || wcount <= ECP_WALKER_SCAN_SLICE_MAX) ? ECP_WALKER_SCAN_SLICE_MAX : 4u;
            uint32_t limit = wcount < max_slice ? wcount : max_slice;
            for (uint32_t step = 0; step < limit; step++) {
                if (step > 0u && EcpElapsedUs(frame_t0) >= ECP_SLICE_BUDGET_US) break;
                uint32_t idx = g_rt.walker_scan_cursor++;
                if (idx >= wcount) {
                    g_rt.walker_scan_cursor = 0u;
                    g_rt.walker_bg_sweep_done = 1;
                    idx = 0u;
                }
                uintptr_t u = EcpWorldUnitAt(units, idx);
                if (u == 0) continue;
                uint64_t ures = EcpUnitResource(u);
                if (!ecp_is_walker_resource(ures)) continue;
                uint32_t uref = EcpUnitRef(u);
                if (uref == 0u) continue;
                if (EcpScoreWalkerUnit(u, cam_pos, cam_fwd, cur_link, &best_sq)) {
                    best_u = u;
                    best_ref = uref;
                    best_res = ures;
                    if (best_sq == 0.0f) break;
                }
            }
        }
    }

    if (best_u != 0) {
        EcpBindWalkerUnit(best_u, best_ref, best_res);
    } else {
        g_rt.walker_unit = 0;
        g_rt.walker_ref = 0u;
        g_rt.walker_pelvis_node = -1;
    }
}

static EcpProfileKind EcpSelectActiveProfile(
    uintptr_t world,
    EcpVec3 cam_pos,
    EcpVec3 cam_fwd,
    float cam_fov,
    uint32_t game_state,
    LARGE_INTEGER frame_t0,
    float* out_pullback,
    float* out_lift_z,
    EcpBypassReason* out_bypass)
{
    *out_pullback = 0.0f;
    *out_lift_z = 0.0f;
    *out_bypass = ECP_BYPASS_NO_SUBJECT;

    int avatar_ok = (g_rt.avatar_unit != 0);
    uintptr_t cur_link = avatar_ok ? EcpUnitLinkParentPoses(g_rt.avatar_unit) : 0;
    g_rt.last_avatar_link = cur_link;

    if (game_state == HF_STATE_MISSION && g_opts.walker_enabled) {
        EcpResolveWalkerSubject(world, cam_pos, cam_fwd, cur_link, frame_t0);
    } else {
        g_rt.walker_unit = 0;
        g_rt.walker_ref = 0u;
    }
    int walker_ok = (g_rt.walker_unit != 0);

    /* 1. Occupied Combat Walker Profile (EXO-45 / EXO-49 / EXO-51 / EXO-55 -> AT-TE & Film-Scale AT-ST).
     *    Requires avatar_ok so an unresolved local avatar can NEVER trigger on a parked or teammate walker,
     *    and requires cam_fov > 0.0f (fail-closed if Camera+0x34 cannot be read). */
    if (game_state == HF_STATE_MISSION && g_opts.walker_enabled && walker_ok && avatar_ok) {
        float wm[16];
        EcpVec3 wroot;
        uint32_t wnodes = 0;
        uintptr_t wwposes = EcpUnitWorldPosesPtr(g_rt.walker_unit, &wnodes);
        if (wwposes != 0 && EcpUnitWorldPose(g_rt.walker_unit, 0, wm) && ecp_world_pose_valid(wm, &wroot, NULL)) {
            EcpVec3 focus = ecp_add(wroot, ecp_v3(0.0f, 0.0f, 2.0f));
            EcpVec3 to_focus = ecp_sub(focus, cam_pos);
            float dist_focus = ecp_len(to_focus);
            EcpVec3 dir_focus = to_focus;
            int in_front = dist_focus >= 1.20f && dist_focus <= 14.0f &&
                           ecp_normalize(&dir_focus) && ecp_dot(dir_focus, cam_fwd) >= 0.45f;

            int local_piloting = 0;
            if (in_front) {
                float am[16], hm[16];
                EcpVec3 aroot;
                if (cur_link != 0 && cur_link == wwposes) {
                    local_piloting = 1; /* Exact scene-graph parent link to this walker. */
                } else if (cur_link == 0 &&
                           EcpUnitWorldPose(g_rt.avatar_unit, 0, am) && ecp_world_pose_valid(am, &aroot, NULL)) {
                    int sights = 0;
                    if (g_rt.head_node >= 0 && EcpUnitWorldPose(g_rt.avatar_unit, g_rt.head_node, hm)) {
                        EcpVec3 hpos;
                        if (ecp_world_pose_valid(hm, &hpos, NULL) &&
                            ecp_len(ecp_sub(hpos, cam_pos)) < g_opts.sights_distance_min) {
                            sights = 1;
                        }
                    }
                    float xy = ecp_xy_dist(aroot, wroot);
                    float dz = aroot.z - wroot.z;
                    if (!sights && ((xy <= 0.85f && fabsf(dz) <= 4.80f) ||
                                    (xy <= 1.75f && dz >= 0.75f && dz <= 4.80f))) {
                        local_piloting = 1;
                    }
                }
            }

            if (local_piloting) {
                if (cam_fov <= 0.0f || cam_fov < g_opts.walker_fov_min || cam_fov > g_opts.fov_max) {
                    *out_bypass = ECP_BYPASS_FOV;
                    return ECP_PROFILE_NONE;
                }
                if (fabsf(cam_fwd.z) > 0.94f) {
                    *out_bypass = ECP_BYPASS_SIGHTS_DISTANCE;
                    return ECP_PROFILE_NONE;
                }

                int film_scale_atst = 0;
                if (g_rt.walker_pelvis_node >= 0) {
                    float pm[16];
                    EcpVec3 ppos;
                    if (EcpUnitWorldPose(g_rt.walker_unit, g_rt.walker_pelvis_node, pm) &&
                        ecp_world_pose_valid(pm, &ppos, NULL) &&
                        (ppos.z - wroot.z) > 3.50f) {
                        film_scale_atst = 1;
                    }
                }
                *out_bypass = ECP_BYPASS_NONE;
                if (film_scale_atst) {
                    *out_pullback = g_opts.atst_scaled_pullback_distance;
                    *out_lift_z = g_opts.atst_scaled_lift_z;
                    return ECP_PROFILE_WALKER_ATST_SCALED;
                }
                *out_pullback = g_opts.walker_pullback_distance;
                *out_lift_z = g_opts.walker_lift_z;
                return ECP_PROFILE_WALKER;
            }
        }
    }

    /* 2. On-Foot Compact LEGO 3P Profile (61 verified Empire & Clone garments). */
    if (!g_opts.lego_enabled || !avatar_ok || !g_rt.has_lego_garment || g_rt.head_node < 0) {
        *out_bypass = ECP_BYPASS_NO_SUBJECT;
        return ECP_PROFILE_NONE;
    }
    if (fabsf(g_opts.lego_pullback_distance) <= 1e-4f && fabsf(g_opts.lego_lift_z) <= 1e-4f) {
        *out_bypass = ECP_BYPASS_NO_SUBJECT;
        return ECP_PROFILE_NONE;
    }
    if (cur_link != 0) {
        *out_bypass = ECP_BYPASS_PARENT_LINK;
        return ECP_PROFILE_NONE;
    }
    if (cam_fov <= 0.0f || cam_fov < g_opts.fov_min || cam_fov > g_opts.fov_max) {
        *out_bypass = ECP_BYPASS_FOV;
        return ECP_PROFILE_NONE;
    }

    float hm[16], rm[16];
    EcpVec3 head_pos, root_pos, body_facing;
    if (!EcpUnitWorldPose(g_rt.avatar_unit, g_rt.head_node, hm) ||
        !EcpUnitWorldPose(g_rt.avatar_unit, 0, rm) ||
        !ecp_world_pose_valid(hm, &head_pos, NULL) ||
        !ecp_world_pose_valid(rm, &root_pos, &body_facing)) {
        *out_bypass = ECP_BYPASS_SIGHTS_DISTANCE;
        return ECP_PROFILE_NONE;
    }

    EcpVec3 to_head = ecp_sub(head_pos, cam_pos);
    float dist_head = ecp_len(to_head);
    if (dist_head < g_opts.sights_distance_min || dist_head > g_opts.on_foot_distance_max) {
        *out_bypass = ECP_BYPASS_SIGHTS_DISTANCE;
        return ECP_PROFILE_NONE;
    }

    float head_above_root = head_pos.z - root_pos.z;
    if (head_above_root < 0.25f || head_above_root > 2.25f) {
        *out_bypass = ECP_BYPASS_SIGHTS_DISTANCE;
        return ECP_PROFILE_NONE;
    }

    EcpVec3 dir_head = to_head;
    if (!ecp_normalize(&dir_head) || ecp_dot(dir_head, cam_fwd) < 0.35f) {
        *out_bypass = ECP_BYPASS_SIGHTS_DISTANCE;
        return ECP_PROFILE_NONE;
    }

    if (game_state == HF_STATE_SHIP && dist_head < 2.20f &&
        ecp_dot(body_facing, body_facing) > 0.5f && ecp_dot(body_facing, cam_fwd) < -0.65f) {
        *out_bypass = ECP_BYPASS_ARMORY_PREVIEW;
        return ECP_PROFILE_NONE;
    }

    *out_bypass = ECP_BYPASS_NONE;
    *out_pullback = g_opts.lego_pullback_distance;
    *out_lift_z = g_opts.lego_lift_z;
    return ECP_PROFILE_LEGO;
}

/* ---- Optional Foreground-Safe Hotkey Poll ----------------------------------------------------------------------- */

static int EcpIsProcessForeground(void)
{
    HWND fg = GetForegroundWindow();
    if (!fg) return 0;
    DWORD pid = 0;
    GetWindowThreadProcessId(fg, &pid);
    return pid == GetCurrentProcessId() ? 1 : 0;
}

static void EcpPollToggleHotkey(void)
{
    if (g_opts.toggle_vk == 0u) return;
    if (!EcpIsProcessForeground()) {
        g_prevKeyDown = 0;
        return;
    }
    int down = (GetAsyncKeyState((int)g_opts.toggle_vk) & 0x8000) != 0;
    if (down && !g_prevKeyDown) {
        LONG next = InterlockedCompareExchange(&g_userToggledOff, 0, 0) ? 0 : 1;
        InterlockedExchange(&g_userToggledOff, next);
        if (g_hf && g_hf->log) {
            g_hf->log(g_mod, "ECP_CAMERA hotkey_toggle state=%s vk=0x%02X", next ? "paused" : "enabled", g_opts.toggle_vk);
        }
    }
    g_prevKeyDown = down;
}

/* ---- Health Telemetry & Hard Safety Watchdog -------------------------------------------------------------------- */

static void EcpFinishFrameTelemetry(LARGE_INTEGER frame_t0, EcpBypassReason bypass, const char* stage)
{
    g_rt.last_bypass = bypass;

    if (g_rt.active_profile != g_rt.logged_profile) {
        if (g_hf && g_hf->log && g_mod && InterlockedIncrement(&g_transitionLogs) <= 128) {
            g_hf->log(
                g_mod,
                "ECP_CAMERA profile_change from=%s to=%s bypass_reason=%s",
                EcpProfileName(g_rt.logged_profile),
                EcpProfileName(g_rt.active_profile),
                EcpBypassReasonName(bypass));
        }
        g_rt.logged_profile = g_rt.active_profile;
    }

    uint64_t us = EcpElapsedUs(frame_t0);
    g_health.tick_us_sum += us;
    g_health.tick_count++;
    if (us > g_health.tick_us_max) g_health.tick_us_max = us;

    if (us > ECP_WATCHDOG_WARN_US) {
        g_watchdog.slow_score += (us > ECP_WATCHDOG_SEVERE_US) ? 4u : 2u;
        g_watchdog.last_slow_us = us;
        if (g_watchdog.slow_score >= ECP_WATCHDOG_TRIP_SCORE) {
            InterlockedExchange(&g_watchdog.tripped, 1);
            g_watchdog.trip_count++;
            EcpRestoreBasePoseIfNeeded();
            g_rt.blend = 0.0f;
            g_rt.active_profile = ECP_PROFILE_NONE;
            g_rt.logged_profile = ECP_PROFILE_NONE;
            g_rt.last_bypass = ECP_BYPASS_WATCHDOG;
            if (g_hf && g_hf->log && g_mod) {
                g_hf->log(
                    g_mod,
                    "ECP_WATCHDOG FAIL-CLOSED: frame_us=%llu slow_score=%u stage=%s — disarmed for current mission",
                    (unsigned long long)us,
                    g_watchdog.slow_score,
                    stage ? stage : "unknown");
            }
            EcpSetStatus("paused by watchdog (frame budget exceeded)");
        }
    } else if (us < 200ULL && g_watchdog.slow_score > 0u) {
        g_watchdog.slow_score--;
    }

    ULONGLONG now_ms = EcpNowMs();
    if (g_health.next_log_ms == 0ULL) {
        g_health.next_log_ms = now_ms + ECP_HEALTH_FIRST_MS;
    } else if (now_ms >= g_health.next_log_ms && g_hf && g_hf->log && g_mod && g_health.row < ECP_HEALTH_MAX_ROWS) {
        g_health.next_log_ms = now_ms + ECP_HEALTH_INTERVAL_MS;
        g_health.row++;
        uint64_t avg_us = g_health.tick_count ? (g_health.tick_us_sum / g_health.tick_count) : 0ULL;
        uint64_t max_us = g_health.tick_us_max;
        g_health.tick_us_sum = 0ULL;
        g_health.tick_us_max = 0ULL;
        g_health.tick_count = 0ULL;

        g_hf->log(
            g_mod,
            "ECP_CAMERA health row=%u profile=%s cam_ok=%d avatar_ok=%d walker_ok=%d "
            "applied_frames=%u raycast_hits=%u bypass_reason=%s watchdog_trips=%u "
            "tick_us_avg=%llu tick_us_max=%llu",
            g_health.row,
            EcpProfileName(g_rt.active_profile),
            g_rt.cam_ref != 0u ? 1 : 0,
            g_rt.avatar_unit != 0 ? 1 : 0,
            g_rt.walker_unit != 0 ? 1 : 0,
            g_rt.applied_frames,
            g_rt.raycast_hits,
            EcpBypassReasonName(g_rt.last_bypass),
            g_watchdog.trip_count,
            (unsigned long long)avg_us,
            (unsigned long long)max_us);
    }
}

/* ---- Per-Frame Render-Step Filter & Engine Detour --------------------------------------------------------------- */

static int EcpStateAllowed(uint32_t state)
{
    if (state == HF_STATE_MISSION) return 1;
    if (state == HF_STATE_SHIP && g_opts.ship_walk_enabled) return 1;
    return 0;
}

static void EcpApplyFrame(void)
{
    if (!g_opts.enabled) return;
    LONG epoch = InterlockedCompareExchange(&g_cacheEpoch, 0, 0);
    if (InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        if (g_rt.seen_epoch != epoch || g_rt.world != 0 || g_rt.blend > 0.0f) {
            EcpSyncEpochOnRenderThread(epoch);
        }
        return;
    }
    if (g_rt.seen_epoch != epoch) {
        EcpSyncEpochOnRenderThread(epoch);
    }
    if (InterlockedCompareExchange(&g_watchdog.tripped, 0, 0) != 0) {
        g_rt.last_bypass = ECP_BYPASS_WATCHDOG;
        return;
    }
    if (g_hf == NULL || g_mod == NULL || g_mainWorld == NULL || g_lookupUnit == NULL ||
        g_setLocalPose == NULL || g_updateUnit == NULL) {
        return;
    }

    LARGE_INTEGER frame_t0 = EcpQpcStart();

    /* Re-verify game_state inside StepDetour to close any race with 4 -> 5 mission-exit teardown (exe+0x1E6B3E). */
    uint32_t state = (g_hf->size >= offsetof(HfApi, game_state) + sizeof(void*) && g_hf->game_state != NULL)
                         ? g_hf->game_state(g_mod)
                         : HF_STATE_NONE;
    if (!EcpStateAllowed(state)) {
        InterlockedExchange(&g_disarmed, 1);
        EcpForgetRuntimeCache();
        return;
    }

    EcpPollToggleHotkey();
    if (InterlockedCompareExchange(&g_userToggledOff, 0, 0) != 0) {
        EcpRestoreBasePoseIfNeeded();
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_TOGGLED_OFF, "hotkey_paused");
        return;
    }

    if (!EcpTerminalCameraIdle()) {
        EcpRestoreBasePoseIfNeeded();
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_TERMINAL_CAM, "terminal_cam");
        return;
    }

    uintptr_t world = g_mainWorld();
    if (world == 0) {
        EcpForgetRuntimeCache();
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_NO_WORLD, "no_world");
        return;
    }
    if (world != g_rt.world) {
        EcpForgetRuntimeCache();
        g_rt.world = world;
    }

    if (!EcpEnsureRaycastReady()) {
        EcpRestoreBasePoseIfNeeded();
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_RAYCAST_UNREADY, "raycast_unready");
        return;
    }

    int player_resolved_pre_cam = 0;
    if (g_rt.cam_ref == 0u) {
        /* Verify local player is alive BEFORE probing for the render camera so spectating/dead frames cost ~0. */
        EcpVec3 zero_v = ecp_v3(0.0f, 0.0f, 0.0f);
        if (!EcpResolveLocalPlayerAndAvatar(0, zero_v, zero_v, state, frame_t0)) {
            g_rt.blend = 0.0f;
            g_rt.active_profile = ECP_PROFILE_NONE;
            EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_PLAYER_NOT_ALIVE, "player_not_alive");
            return;
        }
        player_resolved_pre_cam = 1;
        if (g_rt.cam_retry_wait > 0) {
            g_rt.cam_retry_wait--;
            EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_CAM_SEARCHING, "cam_retry_wait");
            return;
        }
        g_rt.cam_ref = EcpStepFindRenderCameraSliced(world, frame_t0);
        if (g_rt.cam_ref == 0u) {
            EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_CAM_SEARCHING, "cam_slice_scan");
            return;
        }
        if (!g_rt.cam_logged && g_hf->log) {
            g_rt.cam_logged = 1;
            g_hf->log(
                g_mod,
                "ECP_CAMERA camera_acquired world=0x%llX cam_ref=0x%08X scan_steps=%u",
                (unsigned long long)world,
                g_rt.cam_ref,
                g_rt.cam_scan_step);
        }
    }

    uintptr_t cam_unit = g_lookupUnit(g_rt.cam_ref);
    uintptr_t cam_graph = cam_unit != 0 ? EcpUnitGraph(cam_unit) : 0;
    uintptr_t local_poses = 0;
    float raw_pose[16];
    if (cam_graph == 0 || !EcpReadPtr(cam_graph + ECP_GRAPH_LOCAL_POSES, &local_poses) ||
        !EcpRead(local_poses, raw_pose, sizeof(raw_pose))) {
        g_rt.cam_ref = 0u;
        g_rt.has_last_written = 0;
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_CAM_POSE_INVALID, "cam_read_failed");
        return;
    }

    float game_pose[16];
    int pose_was_unoverwritten = 0;
    if (g_rt.has_last_written && g_rt.last_written_world == world &&
        g_rt.last_written_cam_ref == g_rt.cam_ref && EcpPoseMatches12(raw_pose, g_rt.last_written_pose)) {
        memcpy(game_pose, g_rt.last_base_pose, sizeof(game_pose));
        pose_was_unoverwritten = 1;
    } else {
        memcpy(game_pose, raw_pose, sizeof(game_pose));
        g_rt.has_last_written = 0;
    }

    EcpVec3 cam_pos, cam_fwd;
    if (!ecp_local_pose_valid(game_pose, &cam_pos, &cam_fwd)) {
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_CAM_POSE_INVALID, "cam_pose_invalid");
        return;
    }

    if (!(player_resolved_pre_cam && g_rt.avatar_unit != 0) &&
        !EcpResolveLocalPlayerAndAvatar(world, cam_pos, cam_fwd, state, frame_t0)) {
        if (pose_was_unoverwritten) {
            g_setLocalPose(g_rt.cam_ref, 0, game_pose);
            g_updateUnit(world, cam_unit);
            g_rt.has_last_written = 0;
        }
        g_rt.blend = 0.0f;
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_PLAYER_NOT_ALIVE, "player_not_alive");
        return;
    }

    float cam_fov = 0.0f;
    uintptr_t cam_obj = EcpUnitCamera(cam_unit, 0);
    if (cam_obj != 0) {
        float f = 0.0f;
        if (EcpRead(cam_obj + ECP_CAMERA_VERTICAL_FOV, &f, sizeof(f)) && isfinite(f) && f > 0.05f && f < 3.10f) {
            cam_fov = f;
        }
    }

    ULONGLONG now_ms = EcpNowMs();
    float dt = (g_rt.last_tick_ms != 0ULL && now_ms >= g_rt.last_tick_ms)
                   ? (float)(now_ms - g_rt.last_tick_ms) * 0.001f
                   : 0.0f;
    g_rt.last_tick_ms = now_ms;

    float pullback = 0.0f, lift_z = 0.0f;
    EcpBypassReason bypass = ECP_BYPASS_NONE;
    EcpProfileKind profile = EcpSelectActiveProfile(
        world,
        cam_pos,
        cam_fwd,
        cam_fov,
        state,
        frame_t0,
        &pullback,
        &lift_z,
        &bypass);

    if (profile == ECP_PROFILE_NONE) {
        if (pose_was_unoverwritten) {
            g_setLocalPose(g_rt.cam_ref, 0, game_pose);
            g_updateUnit(world, cam_unit);
            g_rt.has_last_written = 0;
        }
        g_rt.blend = 0.0f; /* Instant 0.0f snap & unmodified engine pose restoration. */
        g_rt.active_profile = ECP_PROFILE_NONE;
        EcpFinishFrameTelemetry(frame_t0, bypass, "profile_bypass");
        return;
    }

    g_rt.blend = ecp_step_blend(g_rt.blend, 1, dt, g_opts.blend_in_seconds);
    g_rt.active_profile = profile;

    float out_pose[16];
    float applied_len = 0.0f;
    int ray_hit = 0;
    int clamped_ok = ecp_compute_clamped_pose_ex(
        game_pose,
        pullback,
        lift_z,
        g_rt.blend,
        g_opts.wall_safety_margin,
        ECP_RAYCAST_QUERY_ID,
        g_raycast,
        out_pose,
        &applied_len,
        &ray_hit);
    if (ray_hit) {
        g_rt.raycast_hits++;
    }
    if (!clamped_ok) {
        if (pose_was_unoverwritten) {
            g_setLocalPose(g_rt.cam_ref, 0, game_pose);
            g_updateUnit(world, cam_unit);
            g_rt.has_last_written = 0;
        }
        EcpFinishFrameTelemetry(frame_t0, ray_hit ? ECP_BYPASS_WALL_BLOCKED : ECP_BYPASS_CAM_POSE_INVALID, "raycast_clamp");
        return;
    }

    g_setLocalPose(g_rt.cam_ref, 0, out_pose);
    g_updateUnit(world, cam_unit);

    memcpy(g_rt.last_base_pose, game_pose, sizeof(g_rt.last_base_pose));
    memcpy(g_rt.last_written_pose, out_pose, sizeof(g_rt.last_written_pose));
    g_rt.last_written_world = world;
    g_rt.last_written_cam_ref = g_rt.cam_ref;
    g_rt.has_last_written = 1;
    g_rt.applied_frames++;

    EcpFinishFrameTelemetry(frame_t0, ECP_BYPASS_NONE, "applied");
}

static void EcpStepDetour(void* app)
{
    DWORD saved_err = GetLastError();
    unsigned int saved_csr = _mm_getcsr();

    EcpApplyFrame();

    _mm_setcsr(saved_csr);
    SetLastError(saved_err);

    if (g_origStep != NULL) {
        g_origStep(app);
    }
}

/* ---- HellForge Lifecycle, Spawn & Teardown Event Callbacks ------------------------------------------------------ */

static void EcpOnSpawn(HfMod* mod, const HfSpawnEvent* ev, void* user)
{
    (void)mod;
    (void)user;
    if (!ev || ev->entity == 0u || !EcpIsWalkerEntityResource(ev->resource)) return;

    AcquireSRWLockExclusive(&g_walkerLock);
    for (uint32_t i = 0; i < ECP_MAX_TRACKED_WALKERS; i++) {
        if (g_walkerSlots[i].valid && g_walkerSlots[i].entity == ev->entity) {
            g_walkerSlots[i].generation = ++g_walkerGeneration;
            g_walkerSlots[i].network = ev->network;
            g_walkerSlots[i].entity_res = ev->resource;
            ReleaseSRWLockExclusive(&g_walkerLock);
            return;
        }
    }

    for (uint32_t i = 0; i < ECP_MAX_TRACKED_WALKERS; i++) {
        if (!g_walkerSlots[i].valid) {
            g_walkerSlots[i].valid = 1;
            g_walkerSlots[i].generation = ++g_walkerGeneration;
            g_walkerSlots[i].entity = ev->entity;
            g_walkerSlots[i].network = ev->network;
            g_walkerSlots[i].entity_res = ev->resource;
            ReleaseSRWLockExclusive(&g_walkerLock);
            return;
        }
    }

    uint32_t victim = (g_walkerWriteCursor++) & (ECP_MAX_TRACKED_WALKERS - 1u);
    g_walkerSlots[victim].valid = 1;
    g_walkerSlots[victim].generation = ++g_walkerGeneration;
    g_walkerSlots[victim].entity = ev->entity;
    g_walkerSlots[victim].network = ev->network;
    g_walkerSlots[victim].entity_res = ev->resource;
    ReleaseSRWLockExclusive(&g_walkerLock);
}

static void EcpUpdateArmForState(uint32_t to_state)
{
    if (InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0) {
        InterlockedExchange(&g_disarmed, 1);
        InterlockedIncrement(&g_cacheEpoch);
        return;
    }
    if (EcpStateAllowed(to_state)) {
        LONG was_disarmed = InterlockedCompareExchange(&g_disarmed, 0, 0);
        if (was_disarmed != 0) {
            InterlockedIncrement(&g_cacheEpoch);
            InterlockedExchange(&g_watchdog.tripped, 0);
            InterlockedExchange(&g_disarmed, 0);
            if (g_hf && g_hf->log && g_mod && InterlockedIncrement(&g_transitionLogs) <= 128) {
                g_hf->log(g_mod, "ECP_CAMERA armed state=%u", to_state);
            }
        }
    } else {
        /* Immediate atomic disarm on 4 -> 5 (MISSION -> PREPARE_SHIP) and any non-allowed state.
         * Never mutate g_rt/g_health/g_watchdog off the render thread; bump g_cacheEpoch instead! */
        LONG was_disarmed = InterlockedExchange(&g_disarmed, 1);
        InterlockedIncrement(&g_cacheEpoch);
        EcpClearWalkerSlots();
        if (was_disarmed == 0 && g_hf && g_hf->log && g_mod && InterlockedIncrement(&g_transitionLogs) <= 128) {
            g_hf->log(g_mod, "ECP_CAMERA disarmed state=%u", to_state);
        }
    }
}

static void EcpOnState(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)user;
    if (!ev) return;
    EcpUpdateArmForState(ev->to);
}

static void EcpOnMissionStart(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)ev;
    (void)user;
    EcpClearWalkerSlots();
    EcpUpdateArmForState(HF_STATE_MISSION);
}

static void EcpOnMissionEnd(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)ev;
    (void)user;
    InterlockedExchange(&g_disarmed, 1);
    InterlockedIncrement(&g_cacheEpoch);
    EcpClearWalkerSlots();
}

static void EcpOnModsChanged(HfMod* mod, HfEvent event, void* user)
{
    (void)event;
    (void)user;
    EcpRefreshCustomWalkerEntities(mod);
}

static void EcpOnSessionEnd(HfMod* mod, HfEvent event, void* user)
{
    (void)mod;
    (void)event;
    (void)user;
    /* Keep HF_EVENT_SESSION_END strictly lock-free & engine-call-free per SDK 16 rules. */
    InterlockedExchange(&g_sessionEnded, 1);
    InterlockedExchange(&g_disarmed, 1);
}

static void EcpOnGameLoaded(HfMod* mod, HfEvent event, void* user)
{
    (void)event;
    (void)user;
    if (g_hookInstalled || g_hf == NULL) return;

    const char* build = g_hf->build_id ? g_hf->build_id() : "";
    if (!build || strcmp(build, ECP_EXPECTED_BUILD) != 0) {
        if (g_hf->log) g_hf->log(mod, "off: unsupported game build '%s' (expected %s)", build ? build : "?", ECP_EXPECTED_BUILD);
        EcpSetStatus("off: unsupported game build");
        return;
    }

    size_t exe_sz = 0, game_sz = 0;
    g_exeBase = g_hf->module_base("helldivers2.exe", &exe_sz);
    g_gameBase = g_hf->module_base("game.dll", &game_sz);
    if (g_exeBase == 0 || g_gameBase == 0) {
        if (g_hf->log) g_hf->log(mod, "off: helldivers2.exe or game.dll base unavailable");
        EcpSetStatus("off: module base unavailable");
        return;
    }

    if (g_hf->size < offsetof(HfApi, code_matches) + sizeof(void*) || g_hf->code_matches == NULL) {
        if (g_hf->log) g_hf->log(mod, "off: loader lacks SDK 13 code_matches");
        EcpSetStatus("off: loader too old");
        return;
    }

    if (!g_hf->code_matches(g_exeBase + ECP_EXE_CAMERA_STEP, kEcpPinCameraStep) ||
        !g_hf->code_matches(g_exeBase + ECP_EXE_SET_LOCAL_POSE, kEcpPinSetLocalPose) ||
        !g_hf->code_matches(g_exeBase + ECP_EXE_UPDATE_UNIT, kEcpPinUpdateUnit) ||
        !g_hf->code_matches(g_exeBase + ECP_EXE_LOOKUP_UNIT, kEcpPinLookupUnit) ||
        !g_hf->code_matches(g_exeBase + ECP_EXE_MAIN_WORLD, kEcpPinMainWorld) ||
        !g_hf->code_matches(g_exeBase + ECP_EXE_PHYSICS_RAYCAST, kEcpPinRaycast)) {
        if (g_hf->log) g_hf->log(mod, "off: engine instruction pins did not match build %s", ECP_EXPECTED_BUILD);
        EcpSetStatus("off: engine code mismatch");
        return;
    }

    EcpRefreshCustomWalkerEntities(mod);

    g_setLocalPose = (EcpSetLocalPoseFn)(g_exeBase + ECP_EXE_SET_LOCAL_POSE);
    g_updateUnit = (EcpUpdateUnitFn)(g_exeBase + ECP_EXE_UPDATE_UNIT);
    g_lookupUnit = (EcpLookupUnitFn)(g_exeBase + ECP_EXE_LOOKUP_UNIT);
    g_mainWorld = (EcpMainWorldFn)(g_exeBase + ECP_EXE_MAIN_WORLD);

    int rc = 1;
    if (g_hf->size >= offsetof(HfApi, hook_ex) + sizeof(void*) && g_hf->hook_ex != NULL) {
        rc = g_hf->hook_ex(mod, g_exeBase + ECP_EXE_CAMERA_STEP, (void*)EcpStepDetour, (void**)&g_origStep, -10);
    } else if (g_hf->hook != NULL) {
        rc = g_hf->hook(mod, g_exeBase + ECP_EXE_CAMERA_STEP, (void*)EcpStepDetour, (void**)&g_origStep);
    }
    if (rc != 0) {
        if (g_hf->log) g_hf->log(mod, "off: could not hook render step exe+0x%X", ECP_EXE_CAMERA_STEP);
        EcpSetStatus("off: render step hook failed");
        return;
    }

    g_hookInstalled = 1;
    uint32_t cur_state = (g_hf->size >= offsetof(HfApi, game_state) + sizeof(void*) && g_hf->game_state != NULL)
                             ? g_hf->game_state(mod)
                             : HF_STATE_NONE;
    EcpUpdateArmForState(cur_state);

    if (g_opts.allow_experimental_spring_arm && g_hf->log) {
        g_hf->log(mod, "note: allow_experimental_spring_arm is set, but raw game.dll .data spring-arm writes remain disabled in 0.1.3; using verified post-solve Havok raycast clamp");
    }
    if (g_hf->log) {
        g_hf->log(mod, "ready v0.1.3 (O(1) lookup + hot-prefix cam, watchdog=%lluus): walker=%.2fm/+%.2fm (AT-ST=%.2fm/+%.2fm), lego=%s (%.2fm/%+.2fm), margin=%.2fm",
            (unsigned long long)ECP_WATCHDOG_WARN_US,
            g_opts.walker_pullback_distance, g_opts.walker_lift_z,
            g_opts.atst_scaled_pullback_distance, g_opts.atst_scaled_lift_z,
            g_opts.lego_enabled ? "on" : "off",
            g_opts.lego_pullback_distance, g_opts.lego_lift_z,
            g_opts.wall_safety_margin);
    }
    EcpSetStatus("active (per-player O(1) 3P walker & LEGO framing)");
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    if (api == NULL || mod == NULL || api->version < 13u) return 1;

    g_hf = api;
    g_mod = mod;
    g_hookInstalled = 0;
    g_raycastState = 0;
    g_raycast = NULL;
    memset(&g_watchdog, 0, sizeof(g_watchdog));
    memset(&g_health, 0, sizeof(g_health));
    InterlockedExchange(&g_sessionEnded, 0);
    InterlockedExchange(&g_disarmed, 1);
    InterlockedExchange(&g_cacheEpoch, 1);
    InterlockedExchange(&g_transitionLogs, 0);
    InterlockedExchange(&g_userToggledOff, 0);
    AcquireSRWLockExclusive(&g_walkerLock);
    g_customWalkerCount = 0u;
    ReleaseSRWLockExclusive(&g_walkerLock);
    EcpClearWalkerSlots();
    g_rt.seen_epoch = 0;
    EcpForgetRuntimeCache();

    char ini_path[1024];
    ini_path[0] = '\0';
    const char* folder = api->mod_folder ? api->mod_folder(mod) : NULL;
    if (folder && *folder) {
        snprintf(ini_path, sizeof(ini_path), "%s\\options.ini", folder);
        ecp_options_load_file(&g_opts, ini_path);
    } else {
        ecp_options_defaults(&g_opts);
    }

    if (!g_opts.enabled) {
        if (api->log) api->log(mod, "disabled via options.ini");
        EcpSetStatus("disabled in options.ini");
        return 0;
    }

    if (api->version >= 16u) {
        api->on(mod, HF_EVENT_SESSION_END, EcpOnSessionEnd, NULL);
        api->on(mod, HF_EVENT_MODS_CHANGED, EcpOnModsChanged, NULL);
    }
    hf_on_state(mod, EcpOnState, NULL, 100);
    hf_on_mission_start(mod, EcpOnMissionStart, NULL, 100);
    hf_on_mission_end(mod, EcpOnMissionEnd, NULL, 100);
    hf_on_spawn(mod, EcpOnSpawn, NULL, 0);

    if (api->on(mod, HF_EVENT_GAME_LOADED, EcpOnGameLoaded, NULL) != 0) {
        return 1;
    }
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi* api, HfMod* mod, uint32_t reason)
{
    (void)reason;
    InterlockedExchange(&g_sessionEnded, 1);
    InterlockedExchange(&g_disarmed, 1);
    InterlockedIncrement(&g_cacheEpoch);
    if (g_hookInstalled && api != NULL && api->size >= offsetof(HfApi, unhook) + sizeof(void*) && api->unhook != NULL) {
        api->unhook(mod, g_exeBase + ECP_EXE_CAMERA_STEP);
    }
    g_hookInstalled = 0;
    EcpClearWalkerSlots();
    return 0;
}

#ifdef ECP_UNIT_TEST
static void ecp_test_bind_engine_fns(
    uintptr_t exe_base,
    uintptr_t game_base,
    EcpStepFn orig_step,
    EcpSetLocalPoseFn set_pose,
    EcpUpdateUnitFn update_unit,
    EcpLookupUnitFn lookup_unit,
    EcpMainWorldFn main_world,
    EcpRaycastFn raycast,
    int raycast_ready)
{
    g_exeBase = exe_base;
    g_gameBase = game_base;
    g_origStep = orig_step;
    g_setLocalPose = set_pose;
    g_updateUnit = update_unit;
    g_lookupUnit = lookup_unit;
    g_mainWorld = main_world;
    g_raycast = raycast;
    g_raycastState = raycast_ready ? 1 : -1;
}
#endif
