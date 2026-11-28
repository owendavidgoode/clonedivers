/* AT-TE Titanfall Tactical Cockpit HUD (clonedivers.titanfall-walker-hud 0.1.0)
 * Target: Helldivers 2 Steam build 25480438, HellForge Mod SDK 17+ (built against SDK v20).
 *
 * Client-side per-player Titanfall-inspired walker cockpit visor HUD (HfScreen / HfDraw)
 * and optional F7 1P Forward-Canopy / 3P Tactical dual-view camera toggle for Clonedivers
 * AT-TE combat walkers (EXO-45 / EXO-49 / EXO-51 / EXO-55, plus modded walker copies). */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <xmmintrin.h>

#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "hellforge_mod.h"
#include "clonedivers_em_layout.h"
#include "titanfall_hud_options.h"
#include "titanfall_hud_render.h"

#define TFH_EXPECTED_BUILD          "25480438"

/* Pinned helldivers2.exe RVAs (build 25480438). */
#define TFH_EXE_CAMERA_STEP         0x689BC0u
#define TFH_EXE_CAMERA_SUB_STEP     0x1C6900u
#define TFH_EXE_CAMERA_STEP_RET     0x689C00u
#define TFH_EXE_SET_LOCAL_POSE      0x1FCAD0u
#define TFH_EXE_UPDATE_UNIT         0x1B2DC0u
#define TFH_EXE_LOOKUP_UNIT         0x09D8C0u
#define TFH_EXE_MAIN_WORLD          0x317DE0u
#define TFH_EXE_PHYSICS_RAYCAST     0x77DEC0u
#define TFH_EXE_RAYCAST_QUERIES     0x27B9440u
#define TFH_EXE_UNIT_VTABLE         0x1676D80u

/* World, Unit, SceneGraph, and Camera offsets. */
#define TFH_WORLD_UNITS_COUNT       0x2305B8u
#define TFH_WORLD_UNITS_ARRAY       0x2305C0u
#define TFH_WORLD_RENDER_POSE       0x230920u
#define TFH_WORLD_UNITS_MAX         200000u

#define TFH_UNIT_REF_OFFSET         0x08u
#define TFH_UNIT_RES_PTR_OFFSET     0x30u
#define TFH_UNIT_VTABLE_GRAPH       0x0E8u
#define TFH_UNIT_VTABLE_CAM_COUNT   0x160u
#define TFH_UNIT_VTABLE_CAMERA      0x168u

#define TFH_GRAPH_NODE_COUNT        0x10u
#define TFH_GRAPH_LINK_PARENT       0x18u
#define TFH_GRAPH_LOCAL_POSES       0x20u
#define TFH_GRAPH_WORLD_POSES       0x28u
#define TFH_GRAPH_NODE_NAMES        0x40u
#define TFH_GRAPH_MAX_NODES         4096u

#define TFH_CAMERA_VERTICAL_FOV     0x34u

/* Havok physics raycast query constants (damage_los_query, mode 1 closest hit). */
#define TFH_GAME_PHYSICS_GLOBAL     0x3326328u
#define TFH_PHYSICS_RAYCAST_SLOT    0x68u
#define TFH_RAYCAST_QUERY_ROW       4u
#define TFH_RAYCAST_QUERY_ID        (0x80000000u | TFH_RAYCAST_QUERY_ROW)
#define TFH_RAYCAST_QUERY_HASH      0x2315515Du

/* Co-op PlayerManager / LocalUser / EntityManager / HealthManager / MotionManager / MountManager offsets. */
#define TFH_LOCALUSER_PEER_OFFSET   0xB398u
#define TFH_PLAYERS_COUNT_OFFSET    0x84u
#define TFH_PLAYERS_DESC_OFFSET     0xE8u
#define TFH_PLAYERS_LOCAL_FLAG      20u
#define TFH_PLAYERS_PEER_OFFSET     0x2C8u
#define TFH_PLAYERS_LIFE_OFFSET     0x2E0u
#define TFH_PLAYERS_SLOT_STRIDE     0x38u
#define TFH_PLAYERS_NET_OFFSET      0x3A8u
#define TFH_PLAYERS_NET_STRIDE      0x20u
#define TFH_PLAYERS_MAX             4u
#define TFH_NET_ID_NONE             0x7FFFu
#define TFH_PLAYER_LIFE_SPAWNED     3u

#define TFH_EM_NETWORK_MAP          0xF22EC8u
#define TFH_EM_ENTITY_MAP           0xF1AEB0u
#define TFH_EM_DESCRIPTORS          0xF32F18u
#define TFH_EM_DESC_STRIDE          24u
#define TFH_EM_DESC_MAX             65536u

#define TFH_HM_COUNT_OFFSET         0x1020u
#define TFH_HM_ENTITY_MAP           0x1030u
#define TFH_HM_RECORDS_PTR          0x1058u
#define TFH_HM_EXT_PTR              0x1060u
#define TFH_HM_RECORD_STRIDE        0x1B8u
#define TFH_HM_EXT_STRIDE           0x1Cu
#define TFH_HM_HEALTH_OFFSET        0x14u
#define TFH_HM_LIFE_OFFSET          0x19Cu

#define TFH_MM_COUNT_OFFSET         0x4880u
#define TFH_MM_ENTITY_MAP           0x48A0u
#define TFH_MM_RECORDS_PTR          0x48C8u
#define TFH_MM_RECORD_STRIDE        0x84u

#define TFH_MOUNT_ENTITY_MAP        0x20u
#define TFH_MOUNT_RECORDS_PTR       0x48u
#define TFH_MOUNT_RECORD_STRIDE     24u

#define TFH_RES_HELLDIVER_AVATAR    0x4D1C334D294DFA97ULL
#define TFH_NODE_HASH_WALKER_PELVIS 0x9B115563u
#define TFH_ATST_S2C_NODES          106u

#define TFH_MAX_TRACKED_WALKERS     16u
#define TFH_MAX_CUSTOM_WALKERS      16u
#define TFH_CAM_SCAN_SLICE_MAX      64u
#define TFH_CAM_HOT_PREFIX_COUNT    8u
#define TFH_HASH_PROBE_MAX          64u

static const char kTfhPinCameraStep[] =
    "48 83 EC 58 33 C0 4C 8D 05 EB 71 00 01 48 89 44 24 30 48 8D 54 24 40 89 44 24 38 48 81 C1 90 01";
static const char kTfhPinCameraSubStep[] =
    "48 89 5C 24 08 48 89 6C 24 10 44 89 4C 24 20 56 57 41 56 48 83 EC 40 4D 8B F0 48 8B EA 48 8B F1 E8";
static const char kTfhPinSetLocalPose[] =
    "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B D8 48 63 FA E8 D6 0D EA FF 48 8B C8 48 8B F0";
static const char kTfhPinUpdateUnit[] =
    "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 55 41 54 41 55 41 56 41 57 48 8D AC 24 30 F8 FF FF";
static const char kTfhPinLookupUnit[] =
    "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 8B 35 1A 28 97 01 8B D9 48 8D 8E D0 00 00 00 FF";
static const char kTfhPinMainWorld[] =
    "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 48 8B 05 12 84 6F 01 33 C9 0F 57 C0 8B D1 8B D9 44";
static const char kTfhPinRaycast[] =
    "48 8B C4 48 89 58 10 48 89 70 18 57 48 83 EC 70 8B D9";

static const uint64_t kTfhWalkerResources[4] = {
    0x79E4B3D2DA5E45E3ULL, /* EXO-45 Patriot */
    0xC2D449ECF7FACAB1ULL, /* EXO-49 Emancipator */
    0x7B2326F6FD9C8069ULL, /* EXO-51 Lumberer */
    0x35DBF54F016F3624ULL, /* EXO-55 Breakthrough */
};

typedef void (*TfhStepFn)(void* app);
typedef uint64_t (*TfhSubStepFn)(void* rcx, void* rdx, void* r8, uint64_t r9, void* arg5);
typedef void (*TfhSetLocalPoseFn)(uint32_t unit_ref, int32_t node, const float pose[16]);
typedef void (*TfhUpdateUnitFn)(uintptr_t world, uintptr_t unit);
typedef uintptr_t (*TfhLookupUnitFn)(uint32_t unit_ref);
typedef uintptr_t (*TfhMainWorldFn)(void);
typedef uintptr_t (*TfhUnitGraphFn)(uintptr_t unit);
typedef int (*TfhUnitCamCountFn)(uintptr_t unit);
typedef uintptr_t (*TfhUnitCameraFn)(uintptr_t unit, uint32_t index);
typedef int (*TfhRaycastFn)(
    uint32_t query,
    const float from[3],
    const float dir[3],
    float length,
    int zero,
    float result[16],
    int closest);

typedef struct TfhEntityDescriptor {
    uint64_t resource;
    uint32_t entity;
    uint32_t reference;
    uint32_t network;
    uint32_t flags;
} TfhEntityDescriptor;

typedef struct TfhWalkerSlot {
    int valid;
    uint32_t generation;
    uint32_t entity;
    uint32_t network;
    uint64_t entity_res;
} TfhWalkerSlot;

static const HfApi* g_hf;
static HfMod* g_mod;
static uintptr_t g_exeBase;
static uintptr_t g_gameBase;
static TfhOptions g_opts;
static int g_screenId;

static volatile LONG g_disarmed = 1;
static volatile LONG g_sessionEnded = 0;
static volatile LONG g_cacheEpoch = 1;
static volatile LONG g_activeWalkerEntity = 0; /* Published by camera thread for game-thread telemetry */
static volatile LONG g_fpCameraActive = 0;     /* 1 iff Tfh actively owns local_poses[0] in 1P canopy view */

static TfhStepFn g_origStep;
static TfhSubStepFn g_origSubStep;
static uint32_t g_hookedRva;
static TfhSetLocalPoseFn g_setLocalPose;
static TfhUpdateUnitFn g_updateUnit;
static TfhLookupUnitFn g_lookupUnit;
static TfhMainWorldFn g_mainWorld;
static TfhRaycastFn g_raycast;
static int g_raycastState;
static int g_hookInstalled;

static SRWLOCK g_emLayoutLock = SRWLOCK_INIT;
static CdEmLayout g_emLayout;
static int g_emLayoutResolved = 0;

typedef int (*TfhPeerQueryBasePoseFn)(
    uintptr_t world,
    uint32_t cam_ref,
    const float raw_pose[16],
    float out_base[16],
    int consume);
static HMODULE g_peerEcpMod = NULL;
static TfhPeerQueryBasePoseFn g_peerEcpQueryBase = NULL;
#ifdef TFH_UNIT_TEST
static TfhPeerQueryBasePoseFn g_testPeerQueryBaseOverride = NULL;
#endif

static SRWLOCK g_walkerLock = SRWLOCK_INIT;
static TfhWalkerSlot g_walkerSlots[TFH_MAX_TRACKED_WALKERS];
static uint32_t g_walkerGen = 0u;
static uint32_t g_walkerCursor = 0u;
static uint64_t g_customWalkerEntities[TFH_MAX_CUSTOM_WALKERS];
static uint32_t g_customWalkerCount = 0u;

/* Lock-free double-buffered telemetry between game thread, camera thread, and overlay Present thread. */
static TfhGameTelemetry g_gameTelBuf[2];
static volatile LONG g_gameTelIdx = 0;

static TfhPoseTelemetry g_poseTelBuf[2];
static volatile LONG g_poseTelIdx = 0;

/* Camera-step thread private state. */
static struct {
    LONG seen_epoch;
    uintptr_t world;
    uint32_t cam_ref;
    uint32_t cam_scan_step;
    uint32_t cam_best_ref;
    float cam_best_sq;

    uintptr_t walker_unit;
    uint32_t walker_ref;
    uint32_t walker_entity;
    int32_t pelvis_node;

    int mounted_prev;
    int first_person;
    int hud_hidden;
    int hud_requested_open;
    int prev_dual_down;
    int prev_hud_down;

    int has_last_written;
    uintptr_t last_written_world;
    uint32_t last_written_cam_ref;
    float last_base_pose[16];
    float last_written_pose[16];
} g_cam;

/* Present-thread private state (mutated exclusively inside HfScreen.opened / HfScreen.draw). */
static struct {
    float boot_progress;
    float anim_time;
    int is_open;
} g_overlay;

static int TfhIsWalkerResource(uint64_t res)
{
    if (res == 0ULL) return 0;
    for (size_t i = 0; i < sizeof(kTfhWalkerResources) / sizeof(kTfhWalkerResources[0]); i++) {
        if (kTfhWalkerResources[i] == res) return 1;
    }
    for (uint32_t i = 0; i < g_customWalkerCount; i++) {
        if (g_customWalkerEntities[i] == res) return 1;
    }
    return 0;
}

static void TfhRefreshCustomWalkers(void)
{
    g_customWalkerCount = 0u;
    if (!g_hf || g_hf->size < offsetof(HfApi, content) + sizeof(void*) || !g_hf->content) return;
    for (uint32_t idx = 0u; idx < 2048u; idx++) {
        HfContent c;
        memset(&c, 0, sizeof(c));
        c.size = sizeof(c);
        if (!g_hf->content(g_mod, idx, &c)) break;
        if (c.kind == 2u /* HF_KIND_ENTITY */ && TfhIsWalkerResource(c.base) && c.id != 0ULL) {
            if (g_customWalkerCount < TFH_MAX_CUSTOM_WALKERS) {
                g_customWalkerEntities[g_customWalkerCount++] = c.id;
            }
        }
    }
}

static int TfhRead(uintptr_t addr, void* buf, size_t sz)
{
    if (!g_hf || !g_hf->read || addr < 0x10000u || !buf || sz == 0u) return 0;
    return g_hf->read(addr, buf, sz) == 0 ? 1 : 0;
}

static int TfhReadPtr(uintptr_t addr, uintptr_t* out)
{
    uintptr_t v = 0;
    if (!TfhRead(addr, &v, sizeof(v))) return 0;
    *out = v;
    return 1;
}

static int TfhReadU32(uintptr_t addr, uint32_t* out)
{
    uint32_t v = 0;
    if (!TfhRead(addr, &v, sizeof(v))) return 0;
    *out = v;
    return 1;
}

/* Open-addressed game manager map lookup: {u64 table, u32 capacity, u32 empty, u32 multiplier}. */
static int TfhMapFind(uintptr_t manager, uint32_t at, uint32_t key, uint32_t* out_val)
{
    if (manager < 0x10000u || !out_val) return 0;
    uintptr_t table = 0;
    uint32_t hdr[3] = {0, 0, 0}; /* capacity, empty, multiplier */
    if (!TfhReadPtr(manager + at, &table) || table < 0x10000u) return 0;
    if (!TfhRead(manager + at + 8u, hdr, sizeof(hdr))) return 0;
    uint32_t cap = hdr[0], empty = hdr[1], mult = hdr[2];
    if (cap == 0u || cap > 262144u || (cap & (cap - 1u)) != 0u) return 0;

    uint32_t max_probes = cap < TFH_HASH_PROBE_MAX ? cap : TFH_HASH_PROBE_MAX;
    for (uint32_t i = 0u; i < max_probes; i++) {
        uint32_t slot = (i + mult * key) & (cap - 1u);
        uint32_t kv[2] = {0, 0};
        if (!TfhRead(table + (uintptr_t)slot * 8u, kv, sizeof(kv))) return 0;
        if (kv[0] == key) {
            if (kv[1] == 0xFFFFFFFFu) return 0;
            *out_val = kv[1];
            return 1;
        }
        if (kv[0] == empty) return 0;
    }
    return 0;
}

static void TfhInvalidateEmLayout(void)
{
    AcquireSRWLockExclusive(&g_emLayoutLock);
    g_emLayoutResolved = 0;
    ReleaseSRWLockExclusive(&g_emLayoutLock);
}

static CdEmLayout TfhGetEmLayout(void)
{
    AcquireSRWLockShared(&g_emLayoutLock);
    if (g_emLayoutResolved) {
        CdEmLayout cached = g_emLayout;
        ReleaseSRWLockShared(&g_emLayoutLock);
        return cached;
    }
    ReleaseSRWLockShared(&g_emLayoutLock);

    uintptr_t game = g_gameBase;
    if (game == 0u && g_hf && g_hf->module_base) {
        game = g_hf->module_base("game.dll", NULL);
    }
    if (game == 0u || !g_hf) {
        return cd_em_default_layout();
    }

    CdEmLayout lay = cd_em_default_layout();
    if (g_hf->read_code) {
        lay = cd_em_resolve_hf(game, g_hf->read_code);
    }
    if (!lay.relocated && g_hf->read) {
        lay = cd_em_resolve_hf(game, g_hf->read);
    }

    AcquireSRWLockExclusive(&g_emLayoutLock);
    g_emLayout = lay;
    g_emLayoutResolved = 1;
    ReleaseSRWLockExclusive(&g_emLayoutLock);
    return lay;
}

static int TfhQueryPeerBasePose(
    uintptr_t world,
    uint32_t cam_ref,
    const float raw_pose[16],
    float out_base[16],
    int consume)
{
#ifdef TFH_UNIT_TEST
    if (g_testPeerQueryBaseOverride != NULL) {
        return g_testPeerQueryBaseOverride(world, cam_ref, raw_pose, out_base, consume);
    }
#endif
    HMODULE cur = GetModuleHandleA("empire_camera_pullback.dll");
    if (cur == NULL) {
        g_peerEcpMod = NULL;
        g_peerEcpQueryBase = NULL;
        return 0;
    }
    if (cur != g_peerEcpMod) {
        g_peerEcpMod = cur;
        g_peerEcpQueryBase = (TfhPeerQueryBasePoseFn)(uintptr_t)GetProcAddress(cur, "hellforge_ecp_query_base_pose");
    }
    return g_peerEcpQueryBase != NULL ? g_peerEcpQueryBase(world, cam_ref, raw_pose, out_base, consume) : 0;
}

HF_MOD_EXPORT int hellforge_tfh_is_first_person_active(void)
{
    if (!g_opts.enabled ||
        InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        return 0;
    }
    return InterlockedCompareExchange(&g_fpCameraActive, 0, 0) != 0 ? 1 : 0;
}

static int TfhReadEntityDescById(uintptr_t em, uint32_t entity_id, TfhEntityDescriptor* out)
{
    if (em < 0x10000u || entity_id == 0u || entity_id == 0xFFFFFFFFu || !out) return 0;
    CdEmLayout layout = TfhGetEmLayout();
    uint32_t slot = 0u;
    if (!TfhMapFind(em, TFH_EM_ENTITY_MAP, entity_id, &slot) || slot >= layout.capacity) return 0;
    uintptr_t rec = cd_em_record_at(em, layout.records_offset, slot);
    if (!rec || !TfhRead(rec, out, sizeof(*out))) return 0;
    return out->entity == entity_id ? 1 : 0;
}

static int TfhReadEntityDescByNet(uintptr_t em, uint32_t net_id, TfhEntityDescriptor* out)
{
    if (em < 0x10000u || net_id == 0u || net_id == TFH_NET_ID_NONE || !out) return 0;
    CdEmLayout layout = TfhGetEmLayout();
    uint32_t slot = 0u;
    if (!TfhMapFind(em, TFH_EM_NETWORK_MAP, net_id, &slot) || slot >= layout.capacity) return 0;
    uintptr_t rec = cd_em_record_at(em, layout.records_offset, slot);
    if (!rec || !TfhRead(rec, out, sizeof(*out))) return 0;
    return out->network == net_id ? 1 : 0;
}

/* ---- Game-Thread Telemetry Publisher ---------------------------------------------------------------------------- */

static void TfhPublishEmptyTelemetry(void)
{
    LONG cur = InterlockedCompareExchange(&g_gameTelIdx, 0, 0);
    LONG nxt = (cur == 0) ? 1 : 0;
    memset(&g_gameTelBuf[nxt], 0, sizeof(g_gameTelBuf[nxt]));
    MemoryBarrier();
    InterlockedExchange(&g_gameTelIdx, nxt);
}

static void TfhOnGameThread(HfMod* mod, float dt, void* user)
{
    (void)dt;
    (void)user;
    if (!g_opts.enabled || InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        return;
    }

    uint32_t state = (g_hf && g_hf->size >= offsetof(HfApi, game_state) + sizeof(void*) && g_hf->game_state)
                         ? g_hf->game_state(mod)
                         : HF_STATE_NONE;
    if (state != HF_STATE_MISSION) {
        TfhPublishEmptyTelemetry();
        return;
    }

    uintptr_t pm = (uintptr_t)hf_global_PlayerManager(mod);
    uintptr_t em = (uintptr_t)hf_global_EntityManager(mod);
    uintptr_t hm = (uintptr_t)hf_global_HealthManager(mod);
    if (pm < 0x10000u || em < 0x10000u) {
        TfhPublishEmptyTelemetry();
        return;
    }

    uintptr_t lu = (uintptr_t)hf_global_LocalUser(mod);
    uint64_t local_peer = 0ULL;
    if (lu >= 0x10000u) {
        (void)TfhRead(lu + TFH_LOCALUSER_PEER_OFFSET, &local_peer, sizeof(local_peer));
    }

    uint32_t pcount = 0u;
    if (!TfhReadU32(pm + TFH_PLAYERS_COUNT_OFFSET, &pcount) || pcount == 0u) {
        TfhPublishEmptyTelemetry();
        return;
    }
    if (pcount > TFH_PLAYERS_MAX) pcount = TFH_PLAYERS_MAX;

    int found_slot = -1;
    uint32_t local_net = TFH_NET_ID_NONE;
    for (uint32_t i = 0u; i < pcount; i++) {
        uint64_t peer = 0ULL;
        uint32_t life = 0u;
        if (!TfhRead(pm + TFH_PLAYERS_PEER_OFFSET + i * TFH_PLAYERS_SLOT_STRIDE, &peer, sizeof(peer))) continue;
        if (!TfhReadU32(pm + TFH_PLAYERS_LIFE_OFFSET + i * TFH_PLAYERS_SLOT_STRIDE, &life)) continue;
        int is_local = 0;
        if (local_peer != 0ULL && peer == local_peer) {
            is_local = 1;
        } else if (local_peer == 0ULL) {
            uintptr_t pdesc = 0u;
            uint8_t lflag = 0u;
            if (TfhReadPtr(pm + TFH_PLAYERS_DESC_OFFSET + i * sizeof(uintptr_t), &pdesc) && pdesc >= 0x10000u &&
                TfhRead(pdesc + TFH_PLAYERS_LOCAL_FLAG, &lflag, 1u) && (lflag & 1u) != 0u) {
                is_local = 1;
            }
        }
        if (is_local) {
            if (life != TFH_PLAYER_LIFE_SPAWNED) {
                TfhPublishEmptyTelemetry();
                return;
            }
            if (!TfhReadU32(pm + TFH_PLAYERS_NET_OFFSET + i * TFH_PLAYERS_NET_STRIDE, &local_net)) {
                TfhPublishEmptyTelemetry();
                return;
            }
            found_slot = (int)i;
            break;
        }
    }
    if (found_slot < 0 || local_net == 0u || local_net == TFH_NET_ID_NONE) {
        TfhPublishEmptyTelemetry();
        return;
    }

    TfhEntityDescriptor adesc;
    memset(&adesc, 0, sizeof(adesc));
    int got_avatar = TfhReadEntityDescByNet(em, local_net, &adesc);
    if (!got_avatar && g_hf->size >= offsetof(HfApi, entity_of_network) + sizeof(void*) && g_hf->entity_of_network) {
        uint32_t ent = g_hf->entity_of_network(mod, local_net);
        got_avatar = TfhReadEntityDescById(em, ent, &adesc);
    }
    if (!got_avatar || adesc.reference == 0u) {
        TfhPublishEmptyTelemetry();
        return;
    }

    /* Check local Helldiver is alive (not downed/dead). */
    if (hm >= 0x10000u) {
        uint32_t hm_cnt = 0u, hm_idx = 0u;
        uintptr_t hm_recs = 0u;
        if (TfhReadU32(hm + TFH_HM_COUNT_OFFSET, &hm_cnt) &&
            TfhMapFind(hm, TFH_HM_ENTITY_MAP, adesc.entity, &hm_idx) &&
            hm_idx < hm_cnt && TfhReadPtr(hm + TFH_HM_RECORDS_PTR, &hm_recs) && hm_recs >= 0x10000u) {
            int32_t hp = 0;
            uint32_t hlife = 0u;
            if (TfhRead(hm_recs + (uintptr_t)hm_idx * TFH_HM_RECORD_STRIDE + TFH_HM_HEALTH_OFFSET, &hp, 4u) &&
                TfhReadU32(hm_recs + (uintptr_t)hm_idx * TFH_HM_RECORD_STRIDE + TFH_HM_LIFE_OFFSET, &hlife)) {
                if (hp <= 0 || hlife != 0u) {
                    TfhPublishEmptyTelemetry();
                    return;
                }
            }
        }
    }

    TfhGameTelemetry next;
    memset(&next, 0, sizeof(next));
    next.avatar_entity = adesc.entity;
    next.avatar_ref = adesc.reference;

    /* Read telemetry for the walker currently occupied by the local pilot (or most recent tracked walker). */
    uint32_t target_walker_ent = (uint32_t)InterlockedCompareExchange(&g_activeWalkerEntity, 0, 0);
    if (target_walker_ent != 0u) {
        TfhEntityDescriptor wdesc;
        if (TfhReadEntityDescById(em, target_walker_ent, &wdesc) && wdesc.reference != 0u) {
            next.walker_entity = wdesc.entity;
            next.walker_ref = wdesc.reference;
            next.walker_res = wdesc.resource;

            /* 1. Hull HP & Max HP from HealthManager */
            if (hm >= 0x10000u) {
                uint32_t hm_cnt = 0u, hm_idx = 0u;
                uintptr_t hm_recs = 0u, hm_ext = 0u;
                if (TfhReadU32(hm + TFH_HM_COUNT_OFFSET, &hm_cnt) &&
                    TfhMapFind(hm, TFH_HM_ENTITY_MAP, target_walker_ent, &hm_idx) && hm_idx < hm_cnt) {
                    if (TfhReadPtr(hm + TFH_HM_RECORDS_PTR, &hm_recs) && hm_recs >= 0x10000u) {
                        (void)TfhRead(hm_recs + (uintptr_t)hm_idx * TFH_HM_RECORD_STRIDE + TFH_HM_HEALTH_OFFSET,
                                      &next.hull_hp, sizeof(next.hull_hp));
                    }
                    if (TfhReadPtr(hm + TFH_HM_EXT_PTR, &hm_ext) && hm_ext >= 0x10000u) {
                        (void)TfhRead(hm_ext + (uintptr_t)hm_idx * TFH_HM_EXT_STRIDE + TFH_HM_HEALTH_OFFSET,
                                      &next.hull_max_hp, sizeof(next.hull_max_hp));
                    }
                }
            }

            /* 2. Ground velocity (m/s) from MotionManager */
            uintptr_t mm = (uintptr_t)hf_global_MotionManager(mod);
            if (mm >= 0x10000u) {
                uint32_t mm_cnt = 0u, mm_idx = 0u;
                uintptr_t mm_recs = 0u;
                if (TfhReadU32(mm + TFH_MM_COUNT_OFFSET, &mm_cnt) &&
                    TfhMapFind(mm, TFH_MM_ENTITY_MAP, target_walker_ent, &mm_idx) && mm_idx < mm_cnt &&
                    TfhReadPtr(mm + TFH_MM_RECORDS_PTR, &mm_recs) && mm_recs >= 0x10000u) {
                    float vel[3] = {0.0f, 0.0f, 0.0f};
                    if (TfhRead(mm_recs + (uintptr_t)mm_idx * TFH_MM_RECORD_STRIDE, vel, sizeof(vel)) &&
                        isfinite(vel[0]) && isfinite(vel[1]) && isfinite(vel[2])) {
                        next.speed_mps = sqrtf(vel[0] * vel[0] + vel[1] * vel[1] + vel[2] * vel[2]);
                    }
                }
            }

            /* 3. Mounted Weapon Rounds & Capacity from MountManager + MagazineRounds / MagazineCapacity */
            uintptr_t mounts = (uintptr_t)hf_global_MountManager(mod);
            void* wmgr = (void*)hf_global_WeaponManager(mod);
            HfFn_MagazineRounds fn_rounds = hf_fn_MagazineRounds(mod);
            HfFn_MagazineCapacity fn_cap = hf_fn_MagazineCapacity(mod);
            if (mounts >= 0x10000u && wmgr != NULL && fn_rounds != NULL) {
                uint32_t m_idx = 0u;
                uintptr_t m_recs = 0u;
                if (TfhMapFind(mounts, TFH_MOUNT_ENTITY_MAP, target_walker_ent, &m_idx) && m_idx < 16384u &&
                    TfhReadPtr(mounts + TFH_MOUNT_RECORDS_PTR, &m_recs) && m_recs >= 0x10000u) {
                    uint32_t children[6] = {0, 0, 0, 0, 0, 0};
                    if (TfhRead(m_recs + (uintptr_t)m_idx * TFH_MOUNT_RECORD_STRIDE, children, sizeof(children))) {
                        for (uint32_t k = 0u; k < 5u && next.mount_count < TFH_MAX_MOUNTS; k++) {
                            uint32_t ce = children[k];
                            if (ce == 0u || ce == 0xFFFFFFFFu) continue;
                            int32_t cap = fn_cap ? fn_cap(NULL, ce) : 0;
                            int32_t rnd = fn_rounds(wmgr, ce);
                            if (cap <= 0 && rnd <= 0) continue;
                            uint32_t slot_i = next.mount_count++;
                            next.mounts[slot_i].present = 1u;
                            next.mounts[slot_i].rounds = rnd >= 0 ? rnd : 0;
                            next.mounts[slot_i].capacity = cap >= 0 ? cap : 0;
                        }
                    }
                }
            }
        }
    }

    LONG cur = InterlockedCompareExchange(&g_gameTelIdx, 0, 0);
    LONG nxt = (cur == 0) ? 1 : 0;
    g_gameTelBuf[nxt] = next;
    MemoryBarrier();
    InterlockedExchange(&g_gameTelIdx, nxt);
}

/* ---- Stingray Unit Helpers & Camera Step Detour ----------------------------------------------------------------- */

static int TfhVerifyUnitVtable(uintptr_t unit)
{
    if (unit < 0x10000u || g_exeBase == 0u) return 0;
    uintptr_t vtbl = 0u;
    if (!TfhReadPtr(unit, &vtbl)) return 0;
    return vtbl == (g_exeBase + TFH_EXE_UNIT_VTABLE) ? 1 : 0;
}

static uintptr_t TfhUnitGraph(uintptr_t unit)
{
    if (!TfhVerifyUnitVtable(unit)) return 0u;
    uintptr_t vtbl = g_exeBase + TFH_EXE_UNIT_VTABLE;
    uintptr_t fn = 0u;
    if (!TfhReadPtr(vtbl + TFH_UNIT_VTABLE_GRAPH, &fn) || fn < 0x10000u) return 0u;
    return ((TfhUnitGraphFn)fn)(unit);
}

static int TfhUnitCamCount(uintptr_t unit)
{
    if (!TfhVerifyUnitVtable(unit)) return 0;
    uintptr_t vtbl = g_exeBase + TFH_EXE_UNIT_VTABLE;
    uintptr_t fn = 0u;
    if (!TfhReadPtr(vtbl + TFH_UNIT_VTABLE_CAM_COUNT, &fn) || fn < 0x10000u) return 0;
    return ((TfhUnitCamCountFn)fn)(unit);
}

static uintptr_t TfhUnitCamera(uintptr_t unit, uint32_t idx)
{
    if (!TfhVerifyUnitVtable(unit)) return 0u;
    uintptr_t vtbl = g_exeBase + TFH_EXE_UNIT_VTABLE;
    uintptr_t fn = 0u;
    if (!TfhReadPtr(vtbl + TFH_UNIT_VTABLE_CAMERA, &fn) || fn < 0x10000u) return 0u;
    return ((TfhUnitCameraFn)fn)(unit, idx);
}

static int TfhUnitWorldPose(uintptr_t unit, uint32_t node, float out[16])
{
    uintptr_t g = TfhUnitGraph(unit);
    if (g < 0x10000u) return 0;
    uint32_t n = 0u;
    uintptr_t wposes = 0u;
    if (!TfhReadU32(g + TFH_GRAPH_NODE_COUNT, &n) || node >= n || n > TFH_GRAPH_MAX_NODES) return 0;
    if (!TfhReadPtr(g + TFH_GRAPH_WORLD_POSES, &wposes) || wposes < 0x10000u) return 0;
    return TfhRead(wposes + (uintptr_t)node * 64u, out, 64u);
}

static int32_t TfhFindNodeByHash(uintptr_t unit, uint32_t hash)
{
    uintptr_t g = TfhUnitGraph(unit);
    if (g < 0x10000u) return -1;
    uint32_t n = 0u;
    uintptr_t names = 0u;
    if (!TfhReadU32(g + TFH_GRAPH_NODE_COUNT, &n) || n == 0u || n > 256u) return -1;
    if (!TfhReadPtr(g + TFH_GRAPH_NODE_NAMES, &names) || names < 0x10000u) return -1;
    uint32_t buf[256];
    if (!TfhRead(names, buf, (size_t)n * sizeof(uint32_t))) return -1;
    for (uint32_t i = 0u; i < n; i++) {
        if (buf[i] == hash) return (int32_t)i;
    }
    return -1;
}

static int TfhPoseMatches12(const float a[16], const float b[16])
{
    for (int i = 0; i < 12; i++) {
        if (!isfinite(a[i]) || !isfinite(b[i]) || fabsf(a[i] - b[i]) > 1e-4f) return 0;
    }
    return 1;
}

static int TfhLocalPoseValid(const float p[16], TfhVec3* out_pos, TfhVec3* out_fwd)
{
    for (int i = 0; i < 16; i++) {
        if (!isfinite(p[i])) return 0;
    }
    TfhVec3 fwd = tfh_v3(p[3], p[4], p[5]);
    float fl = tfh_len(fwd);
    if (fl < 0.85f || fl > 1.15f) return 0;
    fwd.x /= fl; fwd.y /= fl; fwd.z /= fl;
    if (out_pos) *out_pos = tfh_v3(p[9], p[10], p[11]);
    if (out_fwd) *out_fwd = fwd;
    return 1;
}

static void TfhSetScreenVisible(int want_open)
{
    if (g_screenId <= 0 || !g_hf || !g_hf->screen_show || !g_mod) return;
    int norm = want_open ? 1 : 0;
    if (g_cam.hud_requested_open != norm) {
        g_cam.hud_requested_open = norm;
        g_hf->screen_show(g_mod, g_screenId, norm);
    }
}

static void TfhPublishPoseTelemetry(const TfhPoseTelemetry* pt)
{
    LONG cur = InterlockedCompareExchange(&g_poseTelIdx, 0, 0);
    LONG nxt = (cur == 0) ? 1 : 0;
    g_poseTelBuf[nxt] = *pt;
    MemoryBarrier();
    InterlockedExchange(&g_poseTelIdx, nxt);
}

static void TfhRestoreBasePoseIfNeeded(void)
{
    if (!g_cam.has_last_written || g_cam.last_written_world == 0u || g_cam.last_written_cam_ref == 0u) {
        g_cam.has_last_written = 0;
        return;
    }
    if (g_lookupUnit && g_setLocalPose && g_updateUnit && g_mainWorld) {
        uintptr_t cur_w = g_mainWorld();
        if (cur_w != 0u && cur_w == g_cam.last_written_world) {
            uintptr_t cu = g_lookupUnit(g_cam.last_written_cam_ref);
            uintptr_t cg = cu ? TfhUnitGraph(cu) : 0u;
            uintptr_t lposes = 0u;
            float cur_p[16];
            if (cg && TfhReadPtr(cg + TFH_GRAPH_LOCAL_POSES, &lposes) &&
                TfhRead(lposes, cur_p, sizeof(cur_p)) &&
                TfhPoseMatches12(cur_p, g_cam.last_written_pose)) {
                g_setLocalPose(g_cam.last_written_cam_ref, 0, g_cam.last_base_pose);
                g_updateUnit(cur_w, cu);
            }
        }
    }
    g_cam.has_last_written = 0;
}

static void TfhResetCameraCache(void)
{
    TfhRestoreBasePoseIfNeeded();
    TfhSetScreenVisible(0);
    InterlockedExchange(&g_activeWalkerEntity, 0);
    InterlockedExchange(&g_fpCameraActive, 0);

    TfhPoseTelemetry empty_pt;
    memset(&empty_pt, 0, sizeof(empty_pt));
    TfhPublishPoseTelemetry(&empty_pt);

    LONG epoch = g_cam.seen_epoch;
    memset(&g_cam, 0, sizeof(g_cam));
    g_cam.seen_epoch = epoch;
    g_cam.pelvis_node = -1;
}

static int TfhEnsureRaycastReady(void)
{
    if (g_raycastState == 1) return 1;
    if (g_raycastState < 0) return 0;
    if (g_exeBase == 0u || g_gameBase == 0u || !g_hf || !g_hf->code_matches) {
        g_raycastState = -1;
        return 0;
    }
    if (!g_hf->code_matches(g_exeBase + TFH_EXE_PHYSICS_RAYCAST, kTfhPinRaycast)) {
        g_raycastState = -1;
        if (g_hf->log) g_hf->log(g_mod, "titanfall-walker-hud: raycast off (exe+0x%X pin mismatch)", TFH_EXE_PHYSICS_RAYCAST);
        return 0;
    }
    uintptr_t phys_api = 0u, slot_fn = 0u;
    uint32_t qhash = 0u;
    /* helldivers2.exe+0x77DEC0 (+0x3F: lea rcx,[rax+rax*4]) indexes 20-byte query rows (+0x5C for row 4). */
    if (!TfhReadPtr(g_gameBase + TFH_GAME_PHYSICS_GLOBAL, &phys_api) || phys_api < 0x10000u ||
        !TfhReadPtr(phys_api + TFH_PHYSICS_RAYCAST_SLOT, &slot_fn) ||
        slot_fn != (g_exeBase + TFH_EXE_PHYSICS_RAYCAST) ||
        !TfhReadU32(g_exeBase + TFH_EXE_RAYCAST_QUERIES + TFH_RAYCAST_QUERY_ROW * 20u + 12u, &qhash) ||
        qhash != TFH_RAYCAST_QUERY_HASH) {
        if (phys_api >= 0x10000u) {
            g_raycastState = -1;
            if (g_hf->log) {
                g_hf->log(g_mod, "titanfall-walker-hud: raycast off (physics slot or damage_los_query mismatch)");
            }
        }
        return 0;
    }
#ifdef TFH_UNIT_TEST
    if (!g_raycast) g_raycast = (TfhRaycastFn)slot_fn;
#else
    g_raycast = (TfhRaycastFn)slot_fn;
#endif
    g_raycastState = 1;
    return 1;
}

static uint32_t TfhStepFindRenderCameraSliced(uintptr_t world)
{
    uint32_t count = 0u;
    uintptr_t arr = 0u;
    float rpose[16];
    if (!TfhReadU32(world + TFH_WORLD_UNITS_COUNT, &count) || count == 0u || count > TFH_WORLD_UNITS_MAX) return 0u;
    if (!TfhReadPtr(world + TFH_WORLD_UNITS_ARRAY, &arr) || arr < 0x10000u) return 0u;
    if (!TfhRead(world + TFH_WORLD_RENDER_POSE, rpose, sizeof(rpose))) return 0u;
    TfhVec3 rpos = tfh_v3(rpose[12], rpose[13], rpose[14]);
    if (!isfinite(rpos.x) || !isfinite(rpos.y) || !isfinite(rpos.z)) return 0u;

    if (g_cam.cam_scan_step == 0u) {
        g_cam.cam_best_ref = 0u;
        g_cam.cam_best_sq = 324.0f; /* 18m^2 radius matching empire-camera-pullback */
    }

    uint32_t limit = (count + 1u) / 2u;
    uint32_t inspected = 0u;
    while (inspected < TFH_CAM_SCAN_SLICE_MAX && g_cam.cam_scan_step < limit) {
        for (int side = 0; side < 2 && inspected < TFH_CAM_SCAN_SLICE_MAX; side++) {
            uint32_t idx = (side == 0) ? (count - 1u - g_cam.cam_scan_step) : g_cam.cam_scan_step;
            if (side == 1 && idx == (count - 1u - g_cam.cam_scan_step)) continue;
            inspected++;

            uintptr_t u = 0u;
            if (!TfhReadPtr(arr + (uintptr_t)idx * sizeof(uintptr_t), &u) || u < 0x10000u) continue;
            if (TfhUnitCamCount(u) <= 0) continue;
            uint32_t uref = 0u;
            float wm[16];
            if (!TfhReadU32(u + TFH_UNIT_REF_OFFSET, &uref) || uref == 0u) continue;
            if (!TfhUnitWorldPose(u, 0u, wm)) continue;
            float dx = wm[12] - rpos.x, dy = wm[13] - rpos.y, dz = wm[14] - rpos.z;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < g_cam.cam_best_sq) {
                g_cam.cam_best_sq = d2;
                g_cam.cam_best_ref = uref;
                if (d2 < 0.0025f) {
                    g_cam.cam_scan_step = 0u;
                    return uref;
                }
            }
        }
        g_cam.cam_scan_step++;
    }

    if (g_cam.cam_scan_step >= limit) {
        uint32_t found = g_cam.cam_best_ref;
        g_cam.cam_scan_step = 0u;
        return found;
    }
    return 0u;
}

static int TfhTerminalCameraIdle(void)
{
    if (!g_hf || g_hf->size < offsetof(HfApi, browse_state) + sizeof(void*) || !g_hf->browse_state) return 1;
    HfBrowseState bs;
    memset(&bs, 0, sizeof(bs));
    bs.size = sizeof(bs);
    (void)g_hf->browse_state(g_mod, &bs);
    return (bs.camera == HF_BROWSE_CAMERA_NONE || bs.camera == HF_BROWSE_CAMERA_IDLE) ? 1 : 0;
}

static int TfhIsForegroundProcess(void)
{
#ifdef TFH_UNIT_TEST
    return 1;
#else
    HWND fg = GetForegroundWindow();
    if (!fg) return 0;
    DWORD pid = 0;
    GetWindowThreadProcessId(fg, &pid);
    return pid == GetCurrentProcessId() ? 1 : 0;
#endif
}

#ifdef TFH_UNIT_TEST
static uint32_t g_testSimulatedVkDown = 0u;
#endif

static int TfhIsVkDown(uint32_t vk)
{
    if (vk == 0u) return 0;
#ifdef TFH_UNIT_TEST
    return g_testSimulatedVkDown == vk ? 1 : 0;
#else
    return (GetAsyncKeyState((int)vk) & 0x8000) != 0 ? 1 : 0;
#endif
}

/* Resolves the walker Unit* that avatar_graph->link_parent points into (via tracked HF_ON_SPAWN slots or bounded scan). */
static uintptr_t TfhResolveMountedWalker(uintptr_t world, uintptr_t avatar_link, uint32_t* out_entity)
{
    if (out_entity) *out_entity = 0u;
    if (avatar_link < 0x10000u || !g_lookupUnit) return 0u;

    /* 1. Fast O(1) check against cached walker_unit */
    if (g_cam.walker_ref != 0u) {
        uintptr_t wu = g_lookupUnit(g_cam.walker_ref);
        uintptr_t wg = wu ? TfhUnitGraph(wu) : 0u;
        uintptr_t wwposes = 0u;
        if (wg && TfhReadPtr(wg + TFH_GRAPH_WORLD_POSES, &wwposes) && wwposes == avatar_link) {
            if (out_entity) *out_entity = g_cam.walker_entity;
            return wu;
        }
    }

    /* 2. Check HF_ON_SPAWN tracked walker slots via EntityManager */
    TfhWalkerSlot snap[TFH_MAX_TRACKED_WALKERS];
    AcquireSRWLockShared(&g_walkerLock);
    memcpy(snap, g_walkerSlots, sizeof(snap));
    ReleaseSRWLockShared(&g_walkerLock);

    uintptr_t em = (g_hf && g_mod) ? (uintptr_t)hf_global_EntityManager(g_mod) : 0u;
    if (em >= 0x10000u) {
        for (uint32_t i = 0u; i < TFH_MAX_TRACKED_WALKERS; i++) {
            if (!snap[i].valid || snap[i].entity == 0u) continue;
            TfhEntityDescriptor wd;
            if (!TfhReadEntityDescById(em, snap[i].entity, &wd) || wd.reference == 0u) continue;
            uintptr_t wu = g_lookupUnit(wd.reference);
            uintptr_t wg = wu ? TfhUnitGraph(wu) : 0u;
            uintptr_t wwposes = 0u;
            if (wg && TfhReadPtr(wg + TFH_GRAPH_WORLD_POSES, &wwposes) && wwposes == avatar_link) {
                g_cam.walker_unit = wu;
                g_cam.walker_ref = wd.reference;
                g_cam.walker_entity = wd.entity;
                g_cam.pelvis_node = TfhFindNodeByHash(wu, TFH_NODE_HASH_WALKER_PELVIS);
                if (out_entity) *out_entity = wd.entity;
                return wu;
            }
        }
    }

    /* 3. Bounded world-tail scan fallback (at most 128 tail units if walker spawned before mod live-load) */
    uint32_t count = 0u;
    uintptr_t arr = 0u;
    if (TfhReadU32(world + TFH_WORLD_UNITS_COUNT, &count) && count > 0u && count <= TFH_WORLD_UNITS_MAX &&
        TfhReadPtr(world + TFH_WORLD_UNITS_ARRAY, &arr) && arr >= 0x10000u) {
        uint32_t scan = count < 128u ? count : 128u;
        for (uint32_t k = 0u; k < scan; k++) {
            uintptr_t wu = 0u;
            if (!TfhReadPtr(arr + (uintptr_t)(count - 1u - k) * sizeof(uintptr_t), &wu) || wu < 0x10000u) continue;
            uintptr_t res_ptr = 0u;
            uint64_t res = 0ULL;
            if (!TfhReadPtr(wu + TFH_UNIT_RES_PTR_OFFSET, &res_ptr) || !TfhRead(res_ptr, &res, 8u)) continue;
            if (!TfhIsWalkerResource(res)) continue;
            uintptr_t wg = TfhUnitGraph(wu);
            uintptr_t wwposes = 0u;
            uint32_t uref = 0u;
            if (wg && TfhReadPtr(wg + TFH_GRAPH_WORLD_POSES, &wwposes) && wwposes == avatar_link &&
                TfhReadU32(wu + TFH_UNIT_REF_OFFSET, &uref) && uref != 0u) {
                g_cam.walker_unit = wu;
                g_cam.walker_ref = uref;
                g_cam.walker_entity = 0u;
                g_cam.pelvis_node = TfhFindNodeByHash(wu, TFH_NODE_HASH_WALKER_PELVIS);
                return wu;
            }
        }
    }
    return 0u;
}

static void TfhApplyCameraAndTelemetryStep(void)
{
    if (!g_opts.enabled) return;
    LONG epoch = InterlockedCompareExchange(&g_cacheEpoch, 0, 0);
    if (InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        if (g_cam.seen_epoch != epoch || g_cam.hud_requested_open || g_cam.has_last_written) {
            g_cam.seen_epoch = epoch;
            TfhResetCameraCache();
        }
        return;
    }
    if (g_cam.seen_epoch != epoch) {
        g_cam.seen_epoch = epoch;
        TfhResetCameraCache();
    }
    if (!g_hf || !g_mod || !g_mainWorld || !g_lookupUnit || !g_setLocalPose || !g_updateUnit) return;

    uint32_t state = (g_hf->size >= offsetof(HfApi, game_state) + sizeof(void*) && g_hf->game_state)
                         ? g_hf->game_state(g_mod)
                         : HF_STATE_NONE;
    if (state != HF_STATE_MISSION) {
        InterlockedExchange(&g_disarmed, 1);
        TfhResetCameraCache();
        return;
    }

    if (!TfhTerminalCameraIdle()) {
        TfhResetCameraCache();
        return;
    }

    uintptr_t world = g_mainWorld();
    if (world < 0x10000u) {
        TfhResetCameraCache();
        return;
    }
    if (world != g_cam.world) {
        TfhResetCameraCache();
        g_cam.world = world;
    }

    LONG gt_idx = InterlockedCompareExchange(&g_gameTelIdx, 0, 0);
    TfhGameTelemetry gt = g_gameTelBuf[gt_idx & 1];
    if (gt.avatar_ref == 0u) {
        TfhResetCameraCache();
        return;
    }

    uintptr_t avatar_unit = g_lookupUnit(gt.avatar_ref);
    uintptr_t avatar_graph = avatar_unit ? TfhUnitGraph(avatar_unit) : 0u;
    uintptr_t avatar_link = 0u;
    if (!avatar_graph || !TfhReadPtr(avatar_graph + TFH_GRAPH_LINK_PARENT, &avatar_link) || avatar_link == 0u) {
        /* Local player is on foot (or unmounted) -> close HUD & restore any 1P pose immediately. */
        if (g_cam.mounted_prev || g_cam.hud_requested_open || g_cam.has_last_written ||
            InterlockedCompareExchange(&g_fpCameraActive, 0, 0) != 0) {
            TfhRestoreBasePoseIfNeeded();
            TfhSetScreenVisible(0);
            InterlockedExchange(&g_activeWalkerEntity, 0);
            InterlockedExchange(&g_fpCameraActive, 0);
            g_cam.mounted_prev = 0;
            g_cam.first_person = 0;
            TfhPoseTelemetry off_pt;
            memset(&off_pt, 0, sizeof(off_pt));
            TfhPublishPoseTelemetry(&off_pt);
        }
        return;
    }

    uint32_t walker_ent = 0u;
    uintptr_t walker_unit = TfhResolveMountedWalker(world, avatar_link, &walker_ent);
    if (!walker_unit) {
        /* Mounted in a non-walker (Hellpod, Pelican, FRV) -> keep HUD shut. */
        if (g_cam.mounted_prev || g_cam.hud_requested_open || g_cam.has_last_written ||
            InterlockedCompareExchange(&g_fpCameraActive, 0, 0) != 0) {
            TfhRestoreBasePoseIfNeeded();
            TfhSetScreenVisible(0);
            InterlockedExchange(&g_activeWalkerEntity, 0);
            InterlockedExchange(&g_fpCameraActive, 0);
            g_cam.mounted_prev = 0;
            g_cam.first_person = 0;
            TfhPoseTelemetry off_pt;
            memset(&off_pt, 0, sizeof(off_pt));
            TfhPublishPoseTelemetry(&off_pt);
        }
        return;
    }

    if (walker_ent != 0u) {
        InterlockedExchange(&g_activeWalkerEntity, (LONG)walker_ent);
    }

    /* Inspect walker scene-graph node count & pelvis height to distinguish 198-node AT-TE from 106-node AT-ST. */
    uintptr_t wgraph = TfhUnitGraph(walker_unit);
    uint32_t wnodes = 0u;
    float wm[16];
    if (!wgraph || !TfhReadU32(wgraph + TFH_GRAPH_NODE_COUNT, &wnodes) || !TfhUnitWorldPose(walker_unit, 0u, wm)) {
        InterlockedExchange(&g_fpCameraActive, 0);
        return;
    }
    TfhVec3 wroot = tfh_v3(wm[12], wm[13], wm[14]);
    TfhVec3 wright = tfh_v3(wm[0], wm[1], wm[2]);
    TfhVec3 wfwd = tfh_v3(wm[4], wm[5], wm[6]);
    TfhVec3 wup = tfh_v3(wm[8], wm[9], wm[10]);
    if (!tfh_normalize(&wright) || !tfh_normalize(&wfwd) || !tfh_normalize(&wup)) {
        InterlockedExchange(&g_fpCameraActive, 0);
        return;
    }

    int is_atst = (wnodes == TFH_ATST_S2C_NODES) ? 1 : 0;
    if (!is_atst && g_cam.pelvis_node >= 0) {
        float pm[16];
        if (TfhUnitWorldPose(walker_unit, (uint32_t)g_cam.pelvis_node, pm) && isfinite(pm[14]) &&
            (pm[14] - wroot.z) > 3.50f) {
            is_atst = 1;
        }
    }

    if (is_atst && !g_opts.enable_on_atst) {
        if (g_cam.mounted_prev || g_cam.hud_requested_open || g_cam.has_last_written ||
            InterlockedCompareExchange(&g_fpCameraActive, 0, 0) != 0) {
            TfhRestoreBasePoseIfNeeded();
            TfhSetScreenVisible(0);
            InterlockedExchange(&g_fpCameraActive, 0);
            g_cam.mounted_prev = 0;
            g_cam.first_person = 0;
        }
        return;
    }

    /* Acquire active render camera if not yet cached. */
    if (g_cam.cam_ref == 0u) {
        g_cam.cam_ref = TfhStepFindRenderCameraSliced(world);
        if (g_cam.cam_ref == 0u) {
            InterlockedExchange(&g_fpCameraActive, 0);
            return;
        }
    }

    uintptr_t cam_unit = g_lookupUnit(g_cam.cam_ref);
    uintptr_t cam_graph = cam_unit ? TfhUnitGraph(cam_unit) : 0u;
    uintptr_t lposes = 0u;
    float raw_pose[16];
    if (!cam_graph || !TfhReadPtr(cam_graph + TFH_GRAPH_LOCAL_POSES, &lposes) ||
        !TfhRead(lposes, raw_pose, sizeof(raw_pose))) {
        g_cam.cam_ref = 0u;
        g_cam.has_last_written = 0;
        InterlockedExchange(&g_fpCameraActive, 0);
        return;
    }

    float base_pose[16];
    int was_unoverwritten = 0;
    int from_peer_ecp = 0;
    if (g_cam.has_last_written && g_cam.last_written_world == world &&
        g_cam.last_written_cam_ref == g_cam.cam_ref && TfhPoseMatches12(raw_pose, g_cam.last_written_pose)) {
        memcpy(base_pose, g_cam.last_base_pose, sizeof(base_pose));
        was_unoverwritten = 1;
    } else if (TfhQueryPeerBasePose(world, g_cam.cam_ref, raw_pose, base_pose, 0)) {
        /* Peek pre-pullback engine pose from empire-camera-pullback (-10) without disturbing paused 3P frames. */
        from_peer_ecp = 1;
        g_cam.has_last_written = 0;
    } else {
        memcpy(base_pose, raw_pose, sizeof(base_pose));
        g_cam.has_last_written = 0;
    }

    TfhVec3 cam_pos, cam_fwd;
    if (!TfhLocalPoseValid(base_pose, &cam_pos, &cam_fwd)) {
        InterlockedExchange(&g_fpCameraActive, 0);
        return;
    }

    /* Initialize view mode upon freshly boarding a walker. */
    if (!g_cam.mounted_prev) {
        g_cam.mounted_prev = 1;
        g_cam.first_person = (g_opts.dual_view_enabled && g_opts.start_in_first_person) ? 1 : 0;
    }

    /* Poll foreground-safe F7 dual-view & HUD visibility hotkeys. */
    if (TfhIsForegroundProcess()) {
        if (g_opts.dual_view_enabled && g_opts.dual_view_vk != 0u) {
            int d_down = TfhIsVkDown(g_opts.dual_view_vk);
            if (d_down && !g_cam.prev_dual_down) {
                g_cam.first_person = g_cam.first_person ? 0 : 1;
                if (g_hf->log) {
                    g_hf->log(g_mod, "TFH_HUD dual_view_toggle mode=%s", g_cam.first_person ? "1p_canopy" : "3p_tactical");
                }
            }
            g_cam.prev_dual_down = d_down;
        }
        if (g_opts.hud_toggle_vk != 0u) {
            int h_down = TfhIsVkDown(g_opts.hud_toggle_vk);
            if (h_down && !g_cam.prev_hud_down) {
                g_cam.hud_hidden = g_cam.hud_hidden ? 0 : 1;
            }
            g_cam.prev_hud_down = h_down;
        }
    } else {
        g_cam.prev_dual_down = 0;
        g_cam.prev_hud_down = 0;
    }

    float cam_fov = 0.0f;
    uintptr_t cam_obj = TfhUnitCamera(cam_unit, 0u);
    if (cam_obj != 0u) {
        float f = 0.0f;
        if (TfhRead(cam_obj + TFH_CAMERA_VERTICAL_FOV, &f, sizeof(f)) && isfinite(f) && f > 0.05f && f < 3.10f) {
            cam_fov = f;
        }
    }

    /* Apply 1P Forward-Canopy viewport camera when active (fail-closed if Havok raycast is unavailable). */
    int applied_fp = 0;
    if (g_cam.first_person && g_opts.dual_view_enabled && cam_fov >= 0.45f && TfhEnsureRaycastReady() && g_raycast) {
        TfhVec3 spine_anchor = tfh_v3(
            wroot.x + wup.x * g_opts.fp_up_offset,
            wroot.y + wup.y * g_opts.fp_up_offset,
            wroot.z + wup.z * g_opts.fp_up_offset);
        TfhVec3 desired = tfh_v3(
            spine_anchor.x + wfwd.x * g_opts.fp_forward_offset,
            spine_anchor.y + wfwd.y * g_opts.fp_forward_offset,
            spine_anchor.z + wfwd.z * g_opts.fp_forward_offset);

        TfhVec3 step_vec = tfh_v3(desired.x - spine_anchor.x, desired.y - spine_anchor.y, desired.z - spine_anchor.z);
        float step_len = tfh_len(step_vec);
        TfhVec3 final_pos = desired;

        if (step_len > 1e-3f) {
            TfhVec3 dir = tfh_v3(step_vec.x / step_len, step_vec.y / step_len, step_vec.z / step_len);
            float margin = g_opts.wall_safety_margin;
            float from[3] = {spine_anchor.x, spine_anchor.y, spine_anchor.z};
            float udir[3] = {dir.x, dir.y, dir.z};
            float hit_buf[16];
            memset(hit_buf, 0, sizeof(hit_buf));
            if (g_raycast(TFH_RAYCAST_QUERY_ID, from, udir, step_len + margin, 0, hit_buf, 1) > 0 &&
                isfinite(hit_buf[0]) && isfinite(hit_buf[1]) && isfinite(hit_buf[2])) {
                TfhVec3 to_hit = tfh_v3(hit_buf[0] - spine_anchor.x, hit_buf[1] - spine_anchor.y, hit_buf[2] - spine_anchor.z);
                float d_hit = tfh_dot(to_hit, dir);
                float allowed = (d_hit > margin) ? (d_hit - margin) : 0.0f;
                if (allowed < step_len) {
                    final_pos = tfh_v3(
                        spine_anchor.x + dir.x * allowed,
                        spine_anchor.y + dir.y * allowed,
                        spine_anchor.z + dir.z * allowed);
                }
            }
        }

        if (isfinite(final_pos.x) && isfinite(final_pos.y) && isfinite(final_pos.z)) {
            if (from_peer_ecp) {
                (void)TfhQueryPeerBasePose(world, g_cam.cam_ref, raw_pose, NULL, 1);
            }
            float fp_pose[16];
            memcpy(fp_pose, base_pose, sizeof(fp_pose));
            fp_pose[9]  = final_pos.x;
            fp_pose[10] = final_pos.y;
            fp_pose[11] = final_pos.z;

            g_setLocalPose(g_cam.cam_ref, 0, fp_pose);
            g_updateUnit(world, cam_unit);

            memcpy(g_cam.last_base_pose, base_pose, sizeof(g_cam.last_base_pose));
            memcpy(g_cam.last_written_pose, fp_pose, sizeof(g_cam.last_written_pose));
            g_cam.last_written_world = world;
            g_cam.last_written_cam_ref = g_cam.cam_ref;
            g_cam.has_last_written = 1;
            applied_fp = 1;
        }
    }

    if (!applied_fp && was_unoverwritten) {
        g_setLocalPose(g_cam.cam_ref, 0, base_pose);
        g_updateUnit(world, cam_unit);
        g_cam.has_last_written = 0;
    }
    InterlockedExchange(&g_fpCameraActive, applied_fp ? 1 : 0);

    /* Compute compass heading, pitch, and walker hull yaw for the HfScreen overlay. */
    float hdg_deg = 0.0f, pitch_deg = 0.0f, wyaw_deg = 0.0f;
    tfh_angles_from_forward(cam_fwd, &hdg_deg, &pitch_deg);
    tfh_angles_from_forward(tfh_v3(wfwd.x, wfwd.y, 0.0f), &wyaw_deg, NULL);

    TfhPoseTelemetry pt;
    memset(&pt, 0, sizeof(pt));
    pt.mounted = 1;
    pt.is_atst = is_atst;
    pt.first_person = applied_fp;
    pt.hud_hidden_by_user = g_cam.hud_hidden;
    pt.heading_deg = hdg_deg;
    pt.walker_yaw_deg = wyaw_deg;
    pt.pitch_deg = pitch_deg;
    pt.cam_fov = cam_fov;
    TfhPublishPoseTelemetry(&pt);

    int want_hud = (g_opts.hud_enabled && !g_cam.hud_hidden) ? 1 : 0;
    TfhSetScreenVisible(want_hud);
}

static void TfhStepDetour(void* app)
{
    DWORD saved_err = GetLastError();
    unsigned int saved_csr = _mm_getcsr();
    TfhApplyCameraAndTelemetryStep();
    _mm_setcsr(saved_csr);
    SetLastError(saved_err);
    if (g_origStep) g_origStep(app);
}

static uint64_t TfhSubStepDetourWithCaller(void* rcx, void* rdx, void* r8, uint64_t r9, void* arg5, uintptr_t ret_addr)
{
    if (g_exeBase != 0u && ret_addr == g_exeBase + TFH_EXE_CAMERA_STEP_RET) {
        DWORD saved_err = GetLastError();
        unsigned int saved_csr = _mm_getcsr();
        TfhApplyCameraAndTelemetryStep();
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
    }
    return g_origSubStep ? g_origSubStep(rcx, rdx, r8, r9, arg5) : 0ULL;
}

static uint64_t TfhSubStepDetour(void* rcx, void* rdx, void* r8, uint64_t r9, void* arg5)
{
    return TfhSubStepDetourWithCaller(rcx, rdx, r8, r9, arg5, (uintptr_t)__builtin_return_address(0));
}

/* ---- HfScreen Overlay Callbacks (Present Thread) ---------------------------------------------------------------- */

static void TfhOpenedScreen(void* user, int open)
{
    (void)user;
    g_overlay.is_open = open;
    if (open) {
        g_overlay.boot_progress = 0.0f;
        g_overlay.anim_time = 0.0f;
    }
}

static int TfhDrawScreen(void* user, const HfDraw* draw, const HfInput* input, float dt)
{
    (void)user;
    if (!draw || !input) return 0;
    float safe_dt = (isfinite(dt) && dt >= 0.0f) ? (dt > 0.10f ? 0.10f : dt) : 0.016f;
    g_overlay.anim_time += safe_dt;

    if (input->closing || InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_disarmed, 0, 0) != 0) {
        g_overlay.boot_progress -= safe_dt / TFH_SHUT_DURATION;
        if (g_overlay.boot_progress <= 0.0f) {
            g_overlay.boot_progress = 0.0f;
            return 0;
        }
    } else {
        if (g_overlay.boot_progress < 1.0f) {
            g_overlay.boot_progress += safe_dt / TFH_BOOT_DURATION;
            if (g_overlay.boot_progress > 1.0f) g_overlay.boot_progress = 1.0f;
        }
    }

    LONG gt_i = InterlockedCompareExchange(&g_gameTelIdx, 0, 0);
    LONG pt_i = InterlockedCompareExchange(&g_poseTelIdx, 0, 0);
    TfhGameTelemetry gt = g_gameTelBuf[gt_i & 1];
    TfhPoseTelemetry pt = g_poseTelBuf[pt_i & 1];

    if (!pt.mounted && !input->closing) {
        return 0;
    }

    tfh_render_cockpit_hud(draw, &g_opts, &gt, &pt, g_overlay.boot_progress, g_overlay.anim_time);
    return 1;
}

/* ---- Lifecycle, Spawn Tracking, and Hook Installation ----------------------------------------------------------- */

static void TfhClearWalkers(void)
{
    AcquireSRWLockExclusive(&g_walkerLock);
    memset(g_walkerSlots, 0, sizeof(g_walkerSlots));
    g_walkerCursor = 0u;
    ReleaseSRWLockExclusive(&g_walkerLock);
}

static void TfhUpdateArmForState(uint32_t state)
{
    if (InterlockedCompareExchange(&g_sessionEnded, 0, 0) != 0) return;
    if (state == HF_STATE_MISSION && g_opts.enabled) {
        InterlockedExchange(&g_disarmed, 0);
    } else {
        InterlockedExchange(&g_disarmed, 1);
        InterlockedExchange(&g_fpCameraActive, 0);
        InterlockedIncrement(&g_cacheEpoch);
        if (g_screenId > 0 && g_hf && g_hf->screen_show && g_mod) {
            g_hf->screen_show(g_mod, g_screenId, 0);
        }
        TfhClearWalkers();
    }
}

static void TfhOnState(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)user;
    if (ev) TfhUpdateArmForState(ev->to);
}

static void TfhOnMissionStart(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)ev;
    (void)user;
    TfhRefreshCustomWalkers();
    TfhUpdateArmForState(HF_STATE_MISSION);
}

static void TfhOnMissionEnd(HfMod* mod, const HfStateEvent* ev, void* user)
{
    (void)mod;
    (void)ev;
    (void)user;
    TfhUpdateArmForState(HF_STATE_PREPARE_SHIP);
}

static void TfhOnSpawn(HfMod* mod, const HfSpawnEvent* ev, void* user)
{
    (void)mod;
    (void)user;
    if (!ev || ev->entity == 0u || !TfhIsWalkerResource(ev->resource)) return;

    AcquireSRWLockExclusive(&g_walkerLock);
    uint32_t slot = g_walkerCursor % TFH_MAX_TRACKED_WALKERS;
    for (uint32_t i = 0u; i < TFH_MAX_TRACKED_WALKERS; i++) {
        if (g_walkerSlots[i].valid && g_walkerSlots[i].entity == ev->entity) {
            slot = i;
            break;
        }
        if (!g_walkerSlots[i].valid) {
            slot = i;
            break;
        }
    }
    g_walkerSlots[slot].valid = 1;
    g_walkerSlots[slot].generation = ++g_walkerGen;
    g_walkerSlots[slot].entity = ev->entity;
    g_walkerSlots[slot].network = ev->network;
    g_walkerSlots[slot].entity_res = ev->resource;
    g_walkerCursor = (slot + 1u) % TFH_MAX_TRACKED_WALKERS;
    ReleaseSRWLockExclusive(&g_walkerLock);
}

static void TfhTryInstallHooks(void)
{
    if (g_hookInstalled || !g_opts.enabled || !g_hf || !g_mod) return;

    size_t exe_sz = 0u, game_sz = 0u;
    uintptr_t exe = g_hf->module_base ? g_hf->module_base("helldivers2.exe", &exe_sz) : 0u;
    uintptr_t game = g_hf->module_base ? g_hf->module_base("game.dll", &game_sz) : 0u;
    if (exe == 0u || game == 0u || exe_sz < TFH_EXE_RAYCAST_QUERIES + 0x100u) return;

    if (!g_hf->code_matches(exe + TFH_EXE_SET_LOCAL_POSE, kTfhPinSetLocalPose) ||
        !g_hf->code_matches(exe + TFH_EXE_UPDATE_UNIT, kTfhPinUpdateUnit) ||
        !g_hf->code_matches(exe + TFH_EXE_LOOKUP_UNIT, kTfhPinLookupUnit) ||
        !g_hf->code_matches(exe + TFH_EXE_MAIN_WORLD, kTfhPinMainWorld)) {
        g_hf->log(g_mod, "titanfall-walker-hud: engine helper code pins did not match; staying fail-closed");
        return;
    }

    g_exeBase = exe;
    g_gameBase = game;
    TfhInvalidateEmLayout();
    g_setLocalPose = (TfhSetLocalPoseFn)(exe + TFH_EXE_SET_LOCAL_POSE);
    g_updateUnit = (TfhUpdateUnitFn)(exe + TFH_EXE_UPDATE_UNIT);
    g_lookupUnit = (TfhLookupUnitFn)(exe + TFH_EXE_LOOKUP_UNIT);
    g_mainWorld = (TfhMainWorldFn)(exe + TFH_EXE_MAIN_WORLD);

    int hooked = 0;
    uint32_t hooked_rva = 0u;
    if (g_hf->code_matches(exe + TFH_EXE_CAMERA_STEP, kTfhPinCameraStep)) {
        if (g_hf->hook_ex(g_mod, exe + TFH_EXE_CAMERA_STEP, (void*)TfhStepDetour, (void**)&g_origStep, -20) == 0) {
            hooked = 1;
            hooked_rva = TFH_EXE_CAMERA_STEP;
        }
    }
    if (!hooked &&
        g_hf->code_matches(exe + TFH_EXE_CAMERA_SUB_STEP, kTfhPinCameraSubStep) &&
        g_hf->code_matches(exe + 0x689BFBu, "E8 00 CD B3 FF")) {
        if (g_hf->hook_ex(g_mod, exe + TFH_EXE_CAMERA_SUB_STEP, (void*)TfhSubStepDetour, (void**)&g_origSubStep, -20) == 0) {
            hooked = 1;
            hooked_rva = TFH_EXE_CAMERA_SUB_STEP;
        }
    }

    if (!hooked) {
        g_hf->log(g_mod, "titanfall-walker-hud: CAMERA_STEP hook refused; staying fail-closed");
        return;
    }

    g_hookedRva = hooked_rva;
    g_hookInstalled = 1;
    if (g_hf->set_status) {
        g_hf->set_status(g_mod, "ready (AT-TE tactical visor + F7 1P/3P optics)");
    }
    g_hf->log(g_mod, "titanfall-walker-hud v0.1.0: installed camera & telemetry pipeline (rva=0x%X, screen_id=%d, dual_view_vk=0x%02X)",
              g_hookedRva, g_screenId, g_opts.dual_view_vk);
}

static void TfhOnLoadEvent(HfMod* mod, HfEvent event, void* user)
{
    (void)mod;
    (void)user;
    if (event == HF_EVENT_GAME_LOADED) {
        TfhTryInstallHooks();
    } else if (event == HF_EVENT_ENTITIES_READY || event == HF_EVENT_MODS_CHANGED) {
        TfhInvalidateEmLayout();
        TfhRefreshCustomWalkers();
        TfhTryInstallHooks();
    } else if (event == HF_EVENT_SESSION_END) {
        InterlockedExchange(&g_sessionEnded, 1);
        InterlockedExchange(&g_disarmed, 1);
        InterlockedExchange(&g_fpCameraActive, 0);
        InterlockedIncrement(&g_cacheEpoch);
    }
}

#ifdef TFH_UNIT_TEST
static void tfh_test_bind_engine(
    uintptr_t exe_base,
    uintptr_t game_base,
    TfhStepFn orig_step,
    TfhSetLocalPoseFn set_pose,
    TfhUpdateUnitFn update_unit,
    TfhLookupUnitFn lookup_unit,
    TfhMainWorldFn main_world,
    TfhRaycastFn raycast,
    int raycast_ready)
{
    g_exeBase = exe_base;
    g_gameBase = game_base;
    TfhInvalidateEmLayout();
    g_origStep = orig_step;
    g_setLocalPose = set_pose;
    g_updateUnit = update_unit;
    g_lookupUnit = lookup_unit;
    g_mainWorld = main_world;
    g_raycast = raycast;
    g_raycastState = raycast_ready;
    g_hookedRva = TFH_EXE_CAMERA_STEP;
    g_hookInstalled = 1;
}
#endif

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi* api, HfMod* mod, uint32_t reason)
{
    (void)reason;
    InterlockedExchange(&g_sessionEnded, 1);
    InterlockedExchange(&g_disarmed, 1);
    InterlockedExchange(&g_fpCameraActive, 0);
    InterlockedIncrement(&g_cacheEpoch);
    if (g_hookInstalled && g_exeBase != 0u && api &&
        api->size >= offsetof(HfApi, unhook) + sizeof(void*) && api->unhook) {
        api->unhook(mod, g_exeBase + (g_hookedRva ? g_hookedRva : TFH_EXE_CAMERA_STEP));
    }
    g_hookInstalled = 0;
    g_hookedRva = 0u;
    g_origStep = NULL;
    g_origSubStep = NULL;
    if (g_screenId > 0 && api && api->size >= offsetof(HfApi, screen_show) + sizeof(void*) && api->screen_show) {
        api->screen_show(mod, g_screenId, 0);
    }
    TfhClearWalkers();
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    if (!api || !mod) return 1;
    if (api->version < 17 || api->size < offsetof(HfApi, screen_show) + sizeof(void*) || !api->log) {
        return 1;
    }
    if (!api->build_id || !api->on || !api->read || !api->hook_ex || !api->code_matches || !api->add_screen || !api->screen_show) {
        api->log(mod, "titanfall-walker-hud: missing required SDK 17 functions; staying inactive");
        return 0;
    }

    const char* build = api->build_id();
    if (!build || strcmp(build, TFH_EXPECTED_BUILD) != 0) {
        api->log(mod, "titanfall-walker-hud: unsupported game build '%s' (expected %s); staying inactive",
                 build ? build : "<null>", TFH_EXPECTED_BUILD);
        return 0;
    }

    g_hf = api;
    g_mod = mod;
    g_screenId = 0;
    g_hookInstalled = 0;
    g_hookedRva = 0u;
    g_origStep = NULL;
    g_origSubStep = NULL;
    g_raycastState = 0;
    g_raycast = NULL;
    TfhInvalidateEmLayout();
    InterlockedExchange(&g_sessionEnded, 0);
    InterlockedExchange(&g_disarmed, 1);
    InterlockedExchange(&g_cacheEpoch, 1);
    InterlockedExchange(&g_activeWalkerEntity, 0);
    InterlockedExchange(&g_fpCameraActive, 0);
    memset(&g_cam, 0, sizeof(g_cam));
    g_cam.pelvis_node = -1;
    memset(&g_overlay, 0, sizeof(g_overlay));
    memset(g_gameTelBuf, 0, sizeof(g_gameTelBuf));
    memset(g_poseTelBuf, 0, sizeof(g_poseTelBuf));
    TfhClearWalkers();

    tfh_options_defaults(&g_opts);
    if (api->mod_folder) {
        const char* folder = api->mod_folder(mod);
        if (folder && *folder) {
            char ini_path[MAX_PATH];
            int n = snprintf(ini_path, sizeof(ini_path), "%s\\options.ini", folder);
            if (n > 0 && (size_t)n < sizeof(ini_path)) {
                (void)tfh_options_load_file(&g_opts, ini_path);
            }
        }
    }

    if (!g_opts.enabled) {
        if (api->set_status) api->set_status(mod, "disabled in options.ini");
        return 0;
    }

    if (g_opts.hud_enabled) {
        HfScreen scr;
        memset(&scr, 0, sizeof(scr));
        scr.size = sizeof(scr);
        scr.name = "AT-TE Tactical Cockpit HUD";
        scr.key = 0u;   /* Controlled automatically on walker mount/disembark via screen_show */
        scr.flags = 0u; /* Non-modal: player retains 100% of mouse & keyboard control */
        scr.user = NULL;
        scr.draw = TfhDrawScreen;
        scr.opened = TfhOpenedScreen;
        scr.can_open = NULL;
        g_screenId = api->add_screen(mod, &scr);
        if (g_screenId <= 0) {
            api->log(mod, "titanfall-walker-hud: add_screen refused; continuing with camera toggle only");
        }
    }

    api->on(mod, HF_EVENT_GAME_LOADED, TfhOnLoadEvent, NULL);
    api->on(mod, HF_EVENT_ENTITIES_READY, TfhOnLoadEvent, NULL);
    api->on(mod, HF_EVENT_MODS_CHANGED, TfhOnLoadEvent, NULL);
    api->on(mod, HF_EVENT_SESSION_END, TfhOnLoadEvent, NULL);

    (void)hf_on_state(mod, TfhOnState, NULL, 0);
    (void)hf_on_mission_start(mod, TfhOnMissionStart, NULL, 0);
    (void)hf_on_mission_end(mod, TfhOnMissionEnd, NULL, 0);
    (void)hf_on_spawn(mod, TfhOnSpawn, NULL, 0);
    (void)hf_on_game_thread(mod, TfhOnGameThread, NULL);

    if (api->game_state) {
        TfhUpdateArmForState(api->game_state(mod));
    }
    return 0;
}
