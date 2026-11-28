/* Comprehensive C11 unit, offline-capture & 65,672-unit D11 stress suite for empire-camera-pullback 0.1.3.
 * Exercises the exact production implementation in ../src/camera_pullback.c across:
 *   1. INI parsing, defaults, UTF-8 BOM, clamping, and fail-closed allow_experimental_spring_arm.
 *   2. All 61 Compact LEGO (resource, expected_node_count) pairs + vanilla node-count rejection.
 *   3. Offline PE capture pin verification against camera-capture-mission (6 engine pins).
 *   4. Game state guards (TITLE/SPLASH/PREPARE_SHIP blocked; SHIP blocked by default, allowed when opted in).
 *   5. HellForge terminal camera guard (NONE=0 and IDLE=1 allowed; MOVING=2, HELD=3, RETURNING=4 blocked).
 *   6. 4-player co-op LocalUser + PlayerManager (slots 0..3) + EntityManager + HealthManager + offline localFlag.
 *   7. Instant 0.0f bypass & frame-exact pose restoration on ADS (fov < 0.85), FPV/iron sights (dist < 0.55m),
 *      Hellpod / Pelican / FRV link, distant cutscene camera, and Super Destroyer Armory front-preview.
 *   8. Occupied walker profile across all 4 walker IDs (EXO-45/49/51/55), film-scale 2.28x AT-ST auto-detect,
 *      HF_ON_SPAWN O(1) tracking, and rejection of unoccupied parked walkers and teammates' walkers in co-op.
 *   9. Synchronous Havok raycast wall/ceiling/bulkhead clamping + fail-closed when raycast is unavailable.
 *  10. Atomic 4 -> 5 (MISSION -> PREPARE_SHIP) teardown disarm (exe+0x1E6B3E guard), HF_EVENT_SESSION_END
 *      permanent latch, and GetLastError() + SSE MXCSR preservation across EcpStepDetour.
 *  11. Level-11 65,672-unit world stress & real QPC benchmark (< 50 us/frame, O(1) steady-state reads,
 *      zero full-world scans on non-LEGO armor, LEGO armor, HF_ON_SPAWN walkers, and midpoint camera discovery).
 *  12. Co-op teammate Helldiver hijack rejection (FPV/ADS & vanilla armor) + walker fov==0 / avatar==0 fail-closed.
 *  13. Paused / sub-tick unoverwritten-pose guard (zero cumulative camera drift + base-pose restoration).
 *  14. 30-second QPC health telemetry log format & hard 1000 us safety watchdog fail-closed latch. */

#define ECP_UNIT_TEST 1
#include "../src/camera_pullback.c"

#include <assert.h>
#include <stdarg.h>
#include <stdlib.h>

static int g_tests_run = 0;
static int g_tests_failed = 0;

#define TEST_CHECK(cond, msg) do { \
    g_tests_run++; \
    if (!(cond)) { \
        fprintf(stderr, "[FAIL] %s (line %d): %s\n", __func__, __LINE__, (msg)); \
        g_tests_failed++; \
        return; \
    } \
} while (0)

#define TEST_NEAR(a, b, eps, msg) TEST_CHECK(fabsf((a) - (b)) <= (eps), (msg))

/* ---- Mock Engine World, Units, PlayerManager, EntityManager, HealthManager & HfApi ------------------------------ */

typedef struct MockSceneGraph {
    uint8_t pad0[0x10];
    uint32_t node_count;      /* +0x10 */
    uint32_t pad14;
    uintptr_t link_parent;    /* +0x18 */
    uintptr_t local_poses;    /* +0x20 */
    uintptr_t world_poses;    /* +0x28 */
    uint8_t pad30[0x10];
    uintptr_t node_names;     /* +0x40 */
} MockSceneGraph;

typedef struct MockCamera {
    uint8_t pad0[0x34];
    float vertical_fov;       /* +0x34 */
} MockCamera;

typedef struct MockUnit {
    uintptr_t vtable;         /* +0x00 */
    uint32_t ref;             /* +0x08 */
    uint32_t pad0C;
    uint8_t pad10[0x20];
    uintptr_t res_ptr;        /* +0x30 */
    uint8_t pad38[0x1A0];
    uintptr_t first_child;    /* +0x1D8 */
    uintptr_t next_sibling;   /* +0x1E0 */

    /* Test-owned backing storage */
    uint64_t resource_storage;
    MockSceneGraph graph;
    float local_poses[4][16];
    float world_poses[200][16];
    uint32_t node_names[200];
    int cam_count;
    MockCamera cam;
} MockUnit;

/* Compact prop/enemy stub matching Stingray Unit header layout for 65,672-unit Level-11 stress tests. */
typedef struct MockPropUnit {
    uintptr_t vtable;         /* +0x00 */
    uint32_t ref;             /* +0x08 */
    uint32_t pad0C;
    uint8_t pad10[0x20];
    uintptr_t res_ptr;        /* +0x30 */
    uint64_t resource_storage;
    int cam_count;
    MockSceneGraph graph;
} MockPropUnit;

#define MOCK_PROP_TAG 0x50524F50u

static uintptr_t MockVtableGraph(uintptr_t u)
{
    if (((const MockPropUnit*)u)->pad0C == MOCK_PROP_TAG) {
        return (uintptr_t)&((MockPropUnit*)u)->graph;
    }
    return (uintptr_t)&((MockUnit*)u)->graph;
}
static int MockVtableCamCount(uintptr_t u)
{
    if (((const MockPropUnit*)u)->pad0C == MOCK_PROP_TAG) {
        return ((MockPropUnit*)u)->cam_count;
    }
    return ((MockUnit*)u)->cam_count;
}
static uintptr_t MockVtableCamera(uintptr_t u, uint32_t idx)
{
    if (((const MockPropUnit*)u)->pad0C == MOCK_PROP_TAG) return 0;
    return idx == 0 ? (uintptr_t)&((MockUnit*)u)->cam : 0;
}

static uint8_t g_mock_exe_module[ECP_EXE_UNIT_VTABLE + 0x200];
static uint8_t g_mock_rogue_vtable[0x200];
static uint8_t g_mock_world[0x231000];
static uintptr_t g_mock_unit_list[16];
static uint32_t g_mock_unit_count;

static uint8_t g_mock_game_module[0x3500000];
static uint8_t g_mock_local_user[0xC000];
static uint8_t g_mock_player_mgr[0x1000];
static uint8_t g_mock_player_descs[4][32];
static uint8_t g_mock_entity_mgr[0xF40000];
static uint8_t g_mock_em_net_buckets[64 * 8];
static uint8_t g_mock_em_ent_buckets[64 * 8];
static uint8_t g_mock_health_mgr[0x2000];
static uint8_t g_mock_hm_buckets[64 * 8];
static uint8_t g_mock_hm_records[8 * ECP_HM_RECORD_STRIDE];

static MockUnit g_cam_unit;
static MockUnit g_diver_unit;
static MockUnit g_garment_unit;
static MockUnit g_walker_unit;
static MockUnit g_walker_unit_b;
static MockUnit g_teammate_unit;
static MockUnit g_teammate_garment;

static volatile uint32_t g_mock_game_state = HF_STATE_MISSION;
static uint32_t g_mock_browse_cam = HF_BROWSE_CAMERA_IDLE;
static uint32_t g_mock_net_to_entity_id = 0x100055u;
static uint32_t g_mock_net_to_entity_key = 0x42u;

static uint64_t g_hf_read_calls = 0;
static int g_orig_step_calls = 0;
static int g_set_pose_calls = 0;
static volatile LONG g_invalid_pose_calls = 0;
static volatile LONG g_rogue_vtable_calls = 0;
static int g_update_unit_calls = 0;
static int g_mutate_cam_pose_in_place = 0;
static float g_last_set_pose[16];
static DWORD g_seen_err_in_orig = 0;
static unsigned int g_seen_csr_in_orig = 0;
static int g_corrupt_csr_inside_raycast = 0;

static int g_ray_should_hit = 0;
static EcpVec3 g_ray_hit_point = {0, 0, 0};

static char g_log_history[16][512];
static uint32_t g_log_count = 0;
static char g_last_status[256];

static int MockHfRead(uintptr_t addr, void* buf, size_t sz)
{
    g_hf_read_calls++;
    if (addr < 0x10000u || buf == NULL) return 1;
    memcpy(buf, (const void*)addr, sz);
    return 0;
}

static void MockLog(HfMod* m, const char* fmt, ...)
{
    (void)m;
    if (!fmt) return;
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    memcpy(g_log_history[g_log_count % 16u], buf, sizeof(buf));
    g_log_count++;
}

static void MockSetStatus(HfMod* m, const char* text)
{
    (void)m;
    snprintf(g_last_status, sizeof(g_last_status), "%s", text ? text : "");
}

static int LogHistoryContains(const char* needle)
{
    uint32_t n = g_log_count < 16u ? g_log_count : 16u;
    for (uint32_t i = 0; i < n; i++) {
        if (strstr(g_log_history[i], needle) != NULL) return 1;
    }
    return 0;
}

static const char* MockBuildId(void) { return "25480438"; }
static uint32_t MockGameState(HfMod* m) { (void)m; return g_mock_game_state; }

static int MockBrowseState(HfMod* m, HfBrowseState* out)
{
    (void)m;
    memset(out, 0, out->size < sizeof(*out) ? out->size : sizeof(*out));
    out->size = (uint32_t)sizeof(*out);
    out->state = HF_BROWSE_READY;
    out->camera = g_mock_browse_cam;
    return 1;
}

static uint32_t MockEntityOfNetwork(HfMod* m, uint32_t net)
{
    (void)m;
    return net == g_mock_net_to_entity_key ? g_mock_net_to_entity_id : 0xFFFFFFFFu;
}

static uintptr_t MockGlobal(HfMod* m, const char* name)
{
    (void)m;
    if (strcmp(name, "LocalUser") == 0) return (uintptr_t)(g_mock_game_module + ECP_GAME_LOCALUSER_GLOBAL);
    if (strcmp(name, "PlayerManager") == 0) return (uintptr_t)(g_mock_game_module + ECP_GAME_PLAYERS_GLOBAL);
    if (strcmp(name, "EntityManager") == 0) return (uintptr_t)(g_mock_game_module + ECP_GAME_ENTITIES_GLOBAL);
    if (strcmp(name, "HealthManager") == 0) return (uintptr_t)(g_mock_game_module + ECP_GAME_HEALTH_GLOBAL);
    return 0;
}

static HfStateListener g_sub_state;
static HfStateListener g_sub_mstart;
static HfStateListener g_sub_mend;
static HfSpawnListener g_sub_spawn;
static HfEventCallback g_on_session_end;
static HfEventCallback g_on_game_loaded;

static int MockOn(HfMod* m, HfEvent ev, HfEventCallback cb, void* user)
{
    (void)m;
    (void)user;
    if (ev == HF_EVENT_SESSION_END) g_on_session_end = cb;
    else if (ev == HF_EVENT_GAME_LOADED) g_on_game_loaded = cb;
    return 0;
}

static int MockSubscribe(HfMod* m, uint32_t kind, HfListener cb, void* user, int32_t prio)
{
    (void)m;
    (void)user;
    (void)prio;
    if (kind == HF_ON_STATE) g_sub_state = (HfStateListener)(void (*)(void))cb;
    else if (kind == HF_ON_MISSION_START) g_sub_mstart = (HfStateListener)(void (*)(void))cb;
    else if (kind == HF_ON_MISSION_END) g_sub_mend = (HfStateListener)(void (*)(void))cb;
    else if (kind == HF_ON_SPAWN) g_sub_spawn = (HfSpawnListener)(void (*)(void))cb;
    return 1;
}

static void MockOrigStep(void* app)
{
    (void)app;
    g_orig_step_calls++;
    g_seen_err_in_orig = GetLastError();
    g_seen_csr_in_orig = _mm_getcsr();
}

static void MockSetLocalPose(uint32_t ref, int32_t node, const float pose[16])
{
    if (ref == 0u || pose == NULL || !isfinite(pose[0]) ||
        (pose[0] == 0.0f && pose[1] == 0.0f && pose[2] == 0.0f)) {
        InterlockedIncrement(&g_invalid_pose_calls);
    }
    if (node == 0) {
        g_set_pose_calls++;
        memcpy(g_last_set_pose, pose, sizeof(g_last_set_pose));
        if (g_mutate_cam_pose_in_place && ref == g_cam_unit.ref) {
            memcpy(g_cam_unit.local_poses[0], pose, 16u * sizeof(float));
        }
    }
}

static void MockUpdateUnit(uintptr_t world, uintptr_t unit)
{
    (void)world;
    (void)unit;
    g_update_unit_calls++;
}

static uintptr_t MockLookupUnit(uint32_t ref)
{
    if (ref == 0u) return 0;
    if (ref == g_cam_unit.ref) return (uintptr_t)&g_cam_unit;
    if (ref == g_diver_unit.ref) return (uintptr_t)&g_diver_unit;
    if (ref == g_walker_unit.ref) return (uintptr_t)&g_walker_unit;
    if (ref == g_walker_unit_b.ref) return (uintptr_t)&g_walker_unit_b;
    if (ref == g_teammate_unit.ref) return (uintptr_t)&g_teammate_unit;
    return 0;
}

static uintptr_t MockMainWorld(void)
{
    return (uintptr_t)g_mock_world;
}

static int MockRaycast(
    uint32_t query,
    const float from[3],
    const float dir[3],
    float length,
    int zero,
    float result[16],
    int closest)
{
    (void)from;
    (void)dir;
    (void)length;
    (void)zero;
    (void)closest;
    if (g_corrupt_csr_inside_raycast) {
        SetLastError(0xCAFEBABEu);
        _mm_setcsr(_mm_getcsr() ^ 0x8000u); /* Flip flush-to-zero bit inside detour */
    }
    if (query != ECP_RAYCAST_QUERY_ID || !g_ray_should_hit) return 0;
    result[0] = g_ray_hit_point.x;
    result[1] = g_ray_hit_point.y;
    result[2] = g_ray_hit_point.z;
    return 1;
}

static void InitIdentityWorldMatrix(float m[16], float x, float y, float z)
{
    memset(m, 0, 16u * sizeof(float));
    m[0] = 1.0f;
    m[5] = 1.0f;
    m[10] = 1.0f;
    m[12] = x;
    m[13] = y;
    m[14] = z;
    m[15] = 1.0f;
}

static void InitLocalPoseLookingPlusY(float p[16], float x, float y, float z)
{
    memset(p, 0, 16u * sizeof(float));
    p[0] = 1.0f;
    p[4] = 1.0f;
    p[8] = 1.0f;
    p[9] = x;
    p[10] = y;
    p[11] = z;
    p[12] = 1.0f;
    p[13] = 1.0f;
    p[14] = 1.0f;
}

static void InitMockUnit(MockUnit* u, uint32_t ref, uint64_t res, uint32_t nodes, int cameras)
{
    memset(u, 0, sizeof(*u));
    u->vtable = (uintptr_t)(g_mock_exe_module + ECP_EXE_UNIT_VTABLE);
    u->ref = ref;
    u->resource_storage = res;
    u->res_ptr = (uintptr_t)&u->resource_storage;
    u->cam_count = cameras;
    u->cam.vertical_fov = 0.9599f;
    u->graph.node_count = nodes;
    u->graph.local_poses = (uintptr_t)u->local_poses;
    u->graph.world_poses = (uintptr_t)u->world_poses;
    u->graph.node_names = (uintptr_t)u->node_names;
    for (uint32_t i = 0; i < nodes && i < 200u; i++) {
        InitIdentityWorldMatrix(u->world_poses[i], 0.0f, 0.0f, 0.0f);
    }
    InitLocalPoseLookingPlusY(u->local_poses[0], 0.0f, 0.0f, 0.0f);
}

static void RegisterMockEntityDescriptor(
    uint32_t slot_idx,
    uint64_t resource,
    uint32_t entity_id,
    uint32_t unit_ref,
    uint32_t net_id,
    uint32_t flags)
{
    const uint32_t cap = 32u;
    const uint32_t mult = 2654435761u;

    EcpEntityDescriptor d;
    d.resource = resource;
    d.entity = entity_id;
    d.reference = unit_ref;
    d.network = net_id;
    d.flags = flags;
    memcpy(g_mock_entity_mgr + ECP_EM_DESCRIPTORS + (uintptr_t)slot_idx * ECP_EM_DESC_STRIDE, &d, sizeof(d));

    if (net_id != 0u && net_id != ECP_NET_ID_NONE) {
        for (uint32_t p = 0; p < cap; p++) {
            uint32_t b = ((net_id * mult) + p) & (cap - 1u);
            uint32_t cur_k = 0;
            memcpy(&cur_k, g_mock_em_net_buckets + b * 8u, 4);
            if (cur_k == ECP_NET_ID_NONE || cur_k == net_id) {
                memcpy(g_mock_em_net_buckets + b * 8u + 0u, &net_id, 4);
                memcpy(g_mock_em_net_buckets + b * 8u + 4u, &slot_idx, 4);
                break;
            }
        }
    }

    if (entity_id != 0u && entity_id != 0xFFFFFFFFu) {
        for (uint32_t p = 0; p < cap; p++) {
            uint32_t b = ((entity_id * mult) + p) & (cap - 1u);
            uint32_t cur_k = 0;
            memcpy(&cur_k, g_mock_em_ent_buckets + b * 8u, 4);
            if (cur_k == 0xFFFFFFFFu || cur_k == entity_id) {
                memcpy(g_mock_em_ent_buckets + b * 8u + 0u, &entity_id, 4);
                memcpy(g_mock_em_ent_buckets + b * 8u + 4u, &slot_idx, 4);
                break;
            }
        }
    }
}

static void InitMockEntityManager(void)
{
    memset(g_mock_entity_mgr, 0, sizeof(g_mock_entity_mgr));
    memset(g_mock_em_ent_buckets, 0xFF, sizeof(g_mock_em_ent_buckets));
    for (uint32_t i = 0; i < 64u; i++) {
        uint32_t empty_net = ECP_NET_ID_NONE;
        uint32_t empty_val = 0xFFFFFFFFu;
        memcpy(g_mock_em_net_buckets + i * 8u + 0u, &empty_net, 4);
        memcpy(g_mock_em_net_buckets + i * 8u + 4u, &empty_val, 4);
    }

    uintptr_t em_ptr = (uintptr_t)g_mock_entity_mgr;
    memcpy(g_mock_game_module + ECP_GAME_ENTITIES_GLOBAL, &em_ptr, sizeof(em_ptr));

    uint64_t net_buckets_ptr = (uint64_t)(uintptr_t)g_mock_em_net_buckets;
    uint64_t ent_buckets_ptr = (uint64_t)(uintptr_t)g_mock_em_ent_buckets;
    uint32_t cap = 32u, empty_net = ECP_NET_ID_NONE, empty_ent = 0xFFFFFFFFu, mult = 2654435761u;

    memcpy(g_mock_entity_mgr + ECP_EM_NETWORK_MAP + 0, &net_buckets_ptr, 8);
    memcpy(g_mock_entity_mgr + ECP_EM_NETWORK_MAP + 8, &cap, 4);
    memcpy(g_mock_entity_mgr + ECP_EM_NETWORK_MAP + 12, &empty_net, 4);
    memcpy(g_mock_entity_mgr + ECP_EM_NETWORK_MAP + 16, &mult, 4);

    memcpy(g_mock_entity_mgr + ECP_EM_ENTITY_MAP + 0, &ent_buckets_ptr, 8);
    memcpy(g_mock_entity_mgr + ECP_EM_ENTITY_MAP + 8, &cap, 4);
    memcpy(g_mock_entity_mgr + ECP_EM_ENTITY_MAP + 12, &empty_ent, 4);
    memcpy(g_mock_entity_mgr + ECP_EM_ENTITY_MAP + 16, &mult, 4);
}

static void ConfigureCoopPlayers(
    uint32_t total_players,
    uint32_t local_slot,
    uint32_t local_lifecycle,
    int32_t health,
    uint32_t hm_life)
{
    memset(g_mock_local_user, 0, sizeof(g_mock_local_user));
    memset(g_mock_player_mgr, 0, sizeof(g_mock_player_mgr));
    memset(g_mock_player_descs, 0, sizeof(g_mock_player_descs));
    memset(g_mock_health_mgr, 0, sizeof(g_mock_health_mgr));
    memset(g_mock_hm_buckets, 0xFF, sizeof(g_mock_hm_buckets));
    memset(g_mock_hm_records, 0, sizeof(g_mock_hm_records));
    InitMockEntityManager();

    uintptr_t lu_ptr = (uintptr_t)g_mock_local_user;
    uintptr_t pm_ptr = (uintptr_t)g_mock_player_mgr;
    uintptr_t hm_ptr = (uintptr_t)g_mock_health_mgr;
    memcpy(g_mock_game_module + ECP_GAME_LOCALUSER_GLOBAL, &lu_ptr, sizeof(lu_ptr));
    memcpy(g_mock_game_module + ECP_GAME_PLAYERS_GLOBAL, &pm_ptr, sizeof(pm_ptr));
    memcpy(g_mock_game_module + ECP_GAME_HEALTH_GLOBAL, &hm_ptr, sizeof(hm_ptr));

    uint64_t local_peer = 0x1122334455667788ULL;
    memcpy(g_mock_local_user + ECP_LOCALUSER_PEER_OFFSET, &local_peer, sizeof(local_peer));
    memcpy(g_mock_player_mgr + ECP_PLAYERS_COUNT_OFFSET, &total_players, sizeof(total_players));

    for (uint32_t i = 0; i < total_players; i++) {
        uint64_t peer = (i == local_slot) ? local_peer : (0x9900000000000000ULL + i);
        uint32_t life = (i == local_slot) ? local_lifecycle : ECP_PLAYER_LIFE_SPAWNED;
        uint32_t net = (i == local_slot) ? g_mock_net_to_entity_key : (0x10u + i);
        uintptr_t pdesc = (uintptr_t)g_mock_player_descs[i];
        g_mock_player_descs[i][ECP_PLAYERS_LOCAL_FLAG] = (i == local_slot) ? 1u : 0u;
        memcpy(g_mock_player_mgr + ECP_PLAYERS_DESC_OFFSET + i * sizeof(uintptr_t), &pdesc, sizeof(pdesc));
        memcpy(g_mock_player_mgr + ECP_PLAYERS_PEER_OFFSET + i * ECP_PLAYERS_SLOT_STRIDE, &peer, 8);
        memcpy(g_mock_player_mgr + ECP_PLAYERS_LIFE_OFFSET + i * ECP_PLAYERS_SLOT_STRIDE, &life, 4);
        memcpy(g_mock_player_mgr + ECP_PLAYERS_NET_OFFSET + i * ECP_PLAYERS_NET_STRIDE, &net, 4);
    }

    /* Register local player's Helldiver descriptor in EntityManager (slot 5) */
    RegisterMockEntityDescriptor(
        5u,
        ECP_RES_HELLDIVER_AVATAR,
        g_mock_net_to_entity_id,
        g_diver_unit.ref,
        g_mock_net_to_entity_key,
        1u);

    /* Populate HealthManager hash map at +0x1030 for g_mock_net_to_entity_id -> index 2 */
    uint32_t hm_count = 4u;
    uint64_t buckets_ptr = (uint64_t)(uintptr_t)g_mock_hm_buckets;
    uint32_t cap = 32u, empty = 0xFFFFFFFFu, mult = 2654435761u;
    uintptr_t recs_ptr = (uintptr_t)g_mock_hm_records;
    memcpy(g_mock_health_mgr + ECP_HM_COUNT_OFFSET, &hm_count, 4);
    memcpy(g_mock_health_mgr + ECP_HM_ENTITY_MAP + 0, &buckets_ptr, 8);
    memcpy(g_mock_health_mgr + ECP_HM_ENTITY_MAP + 8, &cap, 4);
    memcpy(g_mock_health_mgr + ECP_HM_ENTITY_MAP + 12, &empty, 4);
    memcpy(g_mock_health_mgr + ECP_HM_ENTITY_MAP + 16, &mult, 4);
    memcpy(g_mock_health_mgr + ECP_HM_RECORDS_PTR, &recs_ptr, sizeof(recs_ptr));

    uint32_t bucket = (g_mock_net_to_entity_id * mult) & (cap - 1u);
    uint32_t rec_idx = 2u;
    memcpy(g_mock_hm_buckets + bucket * 8u + 0u, &g_mock_net_to_entity_id, 4);
    memcpy(g_mock_hm_buckets + bucket * 8u + 4u, &rec_idx, 4);

    uintptr_t rec_off = (uintptr_t)rec_idx * ECP_HM_RECORD_STRIDE;
    memcpy(g_mock_hm_records + rec_off + ECP_HM_HEALTH_OFFSET, &health, 4);
    memcpy(g_mock_hm_records + rec_off + ECP_HM_LIFE_OFFSET, &hm_life, 4);
}

static HfApi g_mock_api;
static struct { const HfApi* api; } g_mock_mod_obj;

static uintptr_t RogueVtableTrap(uintptr_t u)
{
    (void)u;
    InterlockedIncrement(&g_rogue_vtable_calls);
    return 0;
}

static void ResetHarnessScene(void)
{
    uint8_t* unit_vtbl = g_mock_exe_module + ECP_EXE_UNIT_VTABLE;
    memset(unit_vtbl, 0, 0x200);
    memset(g_mock_rogue_vtable, 0, sizeof(g_mock_rogue_vtable));
    uintptr_t fn_g = (uintptr_t)MockVtableGraph;
    uintptr_t fn_cc = (uintptr_t)MockVtableCamCount;
    uintptr_t fn_c = (uintptr_t)MockVtableCamera;
    uintptr_t fn_rogue = (uintptr_t)RogueVtableTrap;
    memcpy(unit_vtbl + ECP_UNIT_VTABLE_GRAPH, &fn_g, sizeof(fn_g));
    memcpy(unit_vtbl + ECP_UNIT_VTABLE_CAM_COUNT, &fn_cc, sizeof(fn_cc));
    memcpy(unit_vtbl + ECP_UNIT_VTABLE_CAMERA, &fn_c, sizeof(fn_c));
    memcpy(g_mock_rogue_vtable + ECP_UNIT_VTABLE_GRAPH, &fn_rogue, sizeof(fn_rogue));
    memcpy(g_mock_rogue_vtable + ECP_UNIT_VTABLE_CAM_COUNT, &fn_rogue, sizeof(fn_rogue));
    memcpy(g_mock_rogue_vtable + ECP_UNIT_VTABLE_CAMERA, &fn_rogue, sizeof(fn_rogue));

    memset(&g_mock_api, 0, sizeof(g_mock_api));
    g_mock_api.version = 16;
    g_mock_api.size = sizeof(HfApi);
    g_mock_api.log = MockLog;
    g_mock_api.set_status = MockSetStatus;
    g_mock_api.build_id = MockBuildId;
    g_mock_api.read = MockHfRead;
    g_mock_api.on = MockOn;
    g_mock_api.subscribe = MockSubscribe;
    g_mock_api.game_state = MockGameState;
    g_mock_api.browse_state = MockBrowseState;
    g_mock_api.global = MockGlobal;
    g_mock_api.entity_of_network = MockEntityOfNetwork;

    g_mock_mod_obj.api = &g_mock_api;
    g_mock_game_state = HF_STATE_MISSION;
    g_mock_browse_cam = HF_BROWSE_CAMERA_IDLE;
    g_ray_should_hit = 0;
    g_corrupt_csr_inside_raycast = 0;
    g_mutate_cam_pose_in_place = 0;
    g_testSyntheticUs = 0ULL;
    g_testTickMsOverride = 0ULL;
    g_hf_read_calls = 0;
    g_orig_step_calls = 0;
    g_set_pose_calls = 0;
    InterlockedExchange(&g_invalid_pose_calls, 0);
    InterlockedExchange(&g_rogue_vtable_calls, 0);
    g_update_unit_calls = 0;
    memset(&g_walker_unit_b, 0, sizeof(g_walker_unit_b));
    g_log_count = 0;
    memset(g_log_history, 0, sizeof(g_log_history));
    g_last_status[0] = '\0';
    memset(g_last_set_pose, 0, sizeof(g_last_set_pose));

    /* Camera at (0, -2.0, 1.7) looking +Y; world render pose matches camera. */
    InitMockUnit(&g_cam_unit, 0x400001u, 0x1111ULL, 1u, 1);
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.0f, 1.7f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.0f, 1.7f);

    /* Helldiver at (0, 0, 0) with head node 90 at (0, 0, 1.58). */
    InitMockUnit(&g_diver_unit, 0x400002u, ECP_RES_HELLDIVER_AVATAR, 107u, 0);
    g_diver_unit.node_names[90] = ECP_NODE_HASH_HEAD;
    InitIdentityWorldMatrix(g_diver_unit.world_poses[0], 0.0f, 0.0f, 0.0f);
    InitIdentityWorldMatrix(g_diver_unit.world_poses[90], 0.0f, 0.0f, 1.58f);

    /* Compact LEGO helmet child attached to Helldiver (captain_rex_helmet: 21 nodes). */
    InitMockUnit(&g_garment_unit, 0x400003u, 0x0208D1FFFB6C1675ULL, 21u, 0);
    g_diver_unit.first_child = (uintptr_t)&g_garment_unit;

    g_mock_unit_list[0] = (uintptr_t)&g_cam_unit;
    g_mock_unit_list[1] = (uintptr_t)&g_diver_unit;
    g_mock_unit_count = 2u;
    uintptr_t list_ptr = (uintptr_t)g_mock_unit_list;
    memcpy(g_mock_world + ECP_WORLD_UNITS_COUNT, &g_mock_unit_count, 4);
    memcpy(g_mock_world + ECP_WORLD_UNITS_ARRAY, &list_ptr, sizeof(list_ptr));

    ConfigureCoopPlayers(4u, 2u, ECP_PLAYER_LIFE_SPAWNED, 125, ECP_HM_LIFE_ALIVE);

    int rc = hellforge_mod_init(&g_mock_api, (HfMod*)&g_mock_mod_obj);
    if (rc != 0) abort();
    g_opts.blend_in_seconds = 0.0f; /* Instant 1.0 blend in deterministic tests unless testing ramp. */
    g_hookInstalled = 1;
    ecp_test_bind_engine_fns(
        (uintptr_t)g_mock_exe_module,
        (uintptr_t)g_mock_game_module,
        MockOrigStep,
        MockSetLocalPose,
        MockUpdateUnit,
        MockLookupUnit,
        MockMainWorld,
        MockRaycast,
        1);
    EcpUpdateArmForState(HF_STATE_MISSION);
}

/* ---- Individual Test Cases -------------------------------------------------------------------------------------- */

static void test_options_parser_and_bounds(const char* temp_dir)
{
    EcpOptions opts;
    ecp_options_defaults(&opts);
    TEST_CHECK(opts.enabled == 1, "default enabled");
    TEST_CHECK(opts.ship_walk_enabled == 0, "default ship_walk_enabled == false");
    TEST_CHECK(opts.allow_experimental_spring_arm == 0, "default allow_experimental_spring_arm == false");
    TEST_NEAR(opts.walker_pullback_distance, 2.80f, 1e-4f, "default walker pullback");
    TEST_NEAR(opts.walker_lift_z, 1.65f, 1e-4f, "default walker lift");

    const char* raw_ini =
        "\xEF\xBB\xBF; UTF-8 BOM + comment\r\n"
        "[general]\r\n"
        "ship_walk_enabled = true # inline comment\n"
        "wall_safety_margin = 0.01 ; clamped to 0.05\n"
        "fov_min = 0.88\n"
        "toggle_vk = 0x78\n"
        "allow_experimental_spring_arm = true\n"
        "[walker]\n"
        "walker_pullback_distance = 3.10\n"
        "walker_lift_z = 1.90\n"
        "[lego]\n"
        "lego_pullback_distance = 0.35\n"
        "lego_lift_z = -0.10\n";
    ecp_options_parse_text(&opts, raw_ini);
    TEST_CHECK(opts.ship_walk_enabled == 1, "parsed ship_walk_enabled");
    TEST_CHECK(opts.allow_experimental_spring_arm == 1, "parsed experimental flag");
    TEST_CHECK(opts.toggle_vk == 0x78u, "parsed hex toggle_vk");
    TEST_NEAR(opts.wall_safety_margin, 0.05f, 1e-5f, "clamped min safety margin");
    TEST_NEAR(opts.fov_min, 0.88f, 1e-5f, "parsed fov_min");
    TEST_NEAR(opts.walker_pullback_distance, 3.10f, 1e-5f, "parsed walker pullback");
    TEST_NEAR(opts.lego_pullback_distance, 0.35f, 1e-5f, "parsed lego pullback");

    if (temp_dir && *temp_dir) {
        char path[1024];
        snprintf(path, sizeof(path), "%s\\test_options.ini", temp_dir);
        FILE* f = fopen(path, "wb");
        if (f) {
            fwrite(raw_ini, 1, strlen(raw_ini), f);
            fclose(f);
            EcpOptions loaded;
            TEST_CHECK(ecp_options_load_file(&loaded, path) == 1, "load options file");
            TEST_NEAR(loaded.walker_lift_z, 1.90f, 1e-5f, "file walker_lift_z");
        }
    }
}

static void test_all_61_lego_garments_and_vanilla_rejection(void)
{
    TEST_CHECK(ECP_LEGO_GARMENT_COUNT == 79u, "exact 79 garments");
    for (size_t i = 0; i < ECP_LEGO_GARMENT_COUNT; i++) {
        if (i > 0) {
            TEST_CHECK(kEcpLegoGarments[i - 1].resource < kEcpLegoGarments[i].resource, "garment table strictly sorted");
        }
        uint64_t res = kEcpLegoGarments[i].resource;
        uint32_t nodes = kEcpLegoGarments[i].expected_nodes;
        TEST_CHECK(ecp_match_lego_garment(res, nodes) == 1, "exact LEGO garment match");
        TEST_CHECK(ecp_match_lego_garment(res, nodes + 15u) == 0, "reject vanilla higher node count");
        TEST_CHECK(ecp_match_lego_garment(res, nodes - 1u) == 0, "reject mismatched node count");
    }
    TEST_CHECK(ecp_match_lego_garment(0ULL, 21u) == 0, "reject zero resource");
    TEST_CHECK(ecp_match_lego_garment(0xBC20D0B4EFFF128CULL, 18u) == 0, "reject vanilla B-01 helmet");
}

static int MatchPatternBytes(const uint8_t* data, const char* pat)
{
    size_t idx = 0;
    const char* p = pat;
    while (*p) {
        while (*p == ' ') p++;
        if (!*p) break;
        if (p[0] == '?' && p[1] == '?') {
            p += 2;
            idx++;
            continue;
        }
        unsigned int byte_val = 0;
        if (sscanf(p, "%2x", &byte_val) != 1) return 0;
        if (data[idx] != (uint8_t)byte_val) return 0;
        p += 2;
        idx++;
    }
    return 1;
}

static void test_offline_capture_pins_if_present(void)
{
    const char* exe_text_path =
        "D:\\ClaudeWork\\clonedivers-lab\\camera-evidence-astra\\camera-capture-mission\\capture-helldivers2.exe-00001000.bin";

    FILE* f = fopen(exe_text_path, "rb");
    if (!f) return; /* Skip gracefully if lab capture folder is not mounted on another machine. */

    struct { uint32_t rva; const char* pin; const char* name; } checks[] = {
        {ECP_EXE_CAMERA_STEP, kEcpPinCameraStep, "CAMERA_STEP"},
        {ECP_EXE_CAMERA_SUB_STEP, kEcpPinCameraSubStep, "CAMERA_SUB_STEP_PROTON_FALLBACK"},
        {ECP_EXE_SET_LOCAL_POSE, kEcpPinSetLocalPose, "SET_LOCAL_POSE"},
        {ECP_EXE_UPDATE_UNIT, kEcpPinUpdateUnit, "UPDATE_UNIT"},
        {ECP_EXE_LOOKUP_UNIT, kEcpPinLookupUnit, "LOOKUP_UNIT"},
        {ECP_EXE_MAIN_WORLD, kEcpPinMainWorld, "MAIN_WORLD"},
        {ECP_EXE_PHYSICS_RAYCAST, kEcpPinRaycast, "PHYSICS_RAYCAST"},
    };
    for (size_t i = 0; i < sizeof(checks) / sizeof(checks[0]); i++) {
        uint8_t buf[40];
        long file_off = (long)(checks[i].rva - 0x1000u);
        TEST_CHECK(fseek(f, file_off, SEEK_SET) == 0, checks[i].name);
        TEST_CHECK(fread(buf, 1, sizeof(buf), f) == sizeof(buf), checks[i].name);
        TEST_CHECK(MatchPatternBytes(buf, checks[i].pin) == 1, checks[i].name);
    }
    /* Verify the callsite at exe+0x689BFB inside CAMERA_STEP is `E8 00 CD B3 FF` (call 0x1C6900, ret 0x689C00). */
    {
        uint8_t call_bytes[5] = {0};
        TEST_CHECK(fseek(f, (long)(0x689BFBu - 0x1000u), SEEK_SET) == 0, "CAMERA_STEP_SUB_CALL_SEEK");
        TEST_CHECK(fread(call_bytes, 1, sizeof(call_bytes), f) == sizeof(call_bytes), "CAMERA_STEP_SUB_CALL_READ");
        TEST_CHECK(MatchPatternBytes(call_bytes, "E8 00 CD B3 FF") == 1, "CAMERA_STEP_CALLS_1C6900_RET_689C00");
    }
    fclose(f);
}

static void test_lego_on_foot_and_coop_slots_0_to_3(void)
{
    for (uint32_t slot = 0; slot < 4u; slot++) {
        ResetHarnessScene();
        ConfigureCoopPlayers(4u, slot, ECP_PLAYER_LIFE_SPAWNED, 100, ECP_HM_LIFE_ALIVE);
        g_set_pose_calls = 0;
        EcpStepDetour(NULL);
        TEST_CHECK(g_set_pose_calls == 1, "LEGO pullback applied across all co-op slots 0..3");
        TEST_NEAR(g_last_set_pose[10], -2.0f - g_opts.lego_pullback_distance, 1e-4f, "LEGO Y pullback");
        TEST_NEAR(g_last_set_pose[11], 1.7f + g_opts.lego_lift_z, 1e-4f, "LEGO Z lift");
    }

    /* Offline / solo fallback when LocalUser.peer == 0 (resolved via playerDescriptor + 20 localFlag & 1). */
    ResetHarnessScene();
    ConfigureCoopPlayers(4u, 3u, ECP_PLAYER_LIFE_SPAWNED, 100, ECP_HM_LIFE_ALIVE);
    uint64_t zero_peer = 0ULL;
    memcpy(g_mock_local_user + ECP_LOCALUSER_PEER_OFFSET, &zero_peer, sizeof(zero_peer));
    g_set_pose_calls = 0;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1, "localFlag fallback resolves local player when LocalUser.peer == 0");

    /* Vanilla armor sharing a resource ID but with vanilla node count (50 != 21) MUST NOT trigger. */
    ResetHarnessScene();
    g_garment_unit.graph.node_count = 50u;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "vanilla garment node count rejected");
}

static void test_player_downed_dead_and_terminal_camera_guards(void)
{
    ResetHarnessScene();
    ConfigureCoopPlayers(4u, 3u, ECP_PLAYER_LIFE_SPAWNED, 25, 1u);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "downed Helldiver rejected");

    ResetHarnessScene();
    ConfigureCoopPlayers(4u, 1u, ECP_PLAYER_LIFE_SPAWNED, 0, 2u);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "dead Helldiver rejected");

    ResetHarnessScene();
    ConfigureCoopPlayers(4u, 0u, 2u, 100, 0u);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "waiting-to-reinforce lifecycle==2 rejected");

    ResetHarnessScene();
    g_mock_browse_cam = HF_BROWSE_CAMERA_NONE;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1, "terminal camera NONE allowed");

    for (uint32_t cam_state = HF_BROWSE_CAMERA_MOVING; cam_state <= HF_BROWSE_CAMERA_RETURNING; cam_state++) {
        g_set_pose_calls = 0;
        g_mock_browse_cam = cam_state;
        EcpStepDetour(NULL);
        TEST_CHECK(g_set_pose_calls == 0, "terminal camera glide/hold blocked");
    }
}

static void test_instant_bypass_ads_fpv_hellpod_ship_armory(void)
{
    ResetHarnessScene();
    g_opts.blend_in_seconds = 0.25f;
    g_rt.last_tick_ms = GetTickCount64() - 100ULL;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1 && g_rt.blend > 0.0f, "blend ramped up on foot");

    g_set_pose_calls = 0;
    g_cam_unit.cam.vertical_fov = 0.72f;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0 && g_rt.blend == 0.0f, "instant 0.0f bypass on ADS FOV zoom");

    g_opts.blend_in_seconds = 0.0f;
    g_cam_unit.cam.vertical_fov = 0.96f;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1, "restored after ADS");

    g_set_pose_calls = 0;
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -0.16f, 1.58f);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0 && g_rt.blend == 0.0f, "instant 0.0f bypass on FPV/iron sights");

    ResetHarnessScene();
    g_diver_unit.graph.link_parent = 0x77770000ULL;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "Hellpod/Pelican parent link bypassed");

    ResetHarnessScene();
    g_mock_game_state = HF_STATE_SHIP;
    EcpUpdateArmForState(HF_STATE_SHIP);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "HF_STATE_SHIP blocked by default");

    g_opts.ship_walk_enabled = 1;
    EcpUpdateArmForState(HF_STATE_SHIP);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1, "HF_STATE_SHIP walk allowed when opted in");

    g_set_pose_calls = 0;
    g_diver_unit.world_poses[0][4] = 0.0f;
    g_diver_unit.world_poses[0][5] = -1.0f;
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -1.4f, 1.55f);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "Super Destroyer Armory front preview bypassed");
}

static void test_walker_profiles_atte_atst_and_parked_rejection(void)
{
    for (size_t w = 0; w < 4u; w++) {
        ResetHarnessScene();
        InitMockUnit(&g_walker_unit, 0x400010u, kEcpWalkerResources[w], 198u, 0);
        g_walker_unit.node_names[13] = ECP_NODE_HASH_ATST_PELVIS;
        InitIdentityWorldMatrix(g_walker_unit.world_poses[0], 0.0f, 3.0f, 0.0f);
        InitIdentityWorldMatrix(g_walker_unit.world_poses[13], 0.0f, 3.0f, 2.06f);
        InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.5f, 2.4f);
        InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.5f, 2.4f);

        RegisterMockEntityDescriptor(9u, kEcpWalkerResources[w], 0x200010u + (uint32_t)w, g_walker_unit.ref, 0x90u + (uint32_t)w, 1u);
        HfSpawnEvent sp = {{sizeof(sp), HF_ON_SPAWN, 1}, 0x200010u + (uint32_t)w, 0x90u + (uint32_t)w, kEcpWalkerResources[w], 1u};
        if (g_sub_spawn) g_sub_spawn((HfMod*)&g_mock_mod_obj, &sp, NULL);

        g_mock_unit_list[2] = (uintptr_t)&g_walker_unit;
        g_mock_unit_count = 3u;
        memcpy(g_mock_world + ECP_WORLD_UNITS_COUNT, &g_mock_unit_count, 4);

        g_opts.lego_enabled = 0;
        EcpStepDetour(NULL);
        TEST_CHECK(g_set_pose_calls == 0, "parked/teammate walker rejected when diver is on foot outside");

        g_diver_unit.graph.link_parent = g_walker_unit.graph.world_poses;
        EcpStepDetour(NULL);
        TEST_CHECK(g_set_pose_calls == 1 && g_rt.active_profile == ECP_PROFILE_WALKER, "occupied walker triggers");
        TEST_NEAR(g_last_set_pose[10], -2.5f - g_opts.walker_pullback_distance, 1e-4f, "walker pullback Y");
        TEST_NEAR(g_last_set_pose[11], 2.4f + g_opts.walker_lift_z, 1e-4f, "walker dorsal lift Z");
    }

    g_set_pose_calls = 0;
    InitIdentityWorldMatrix(g_walker_unit.world_poses[13], 0.0f, 3.0f, 4.70f);
    g_cam_unit.cam.vertical_fov = 0.78f;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1 && g_rt.active_profile == ECP_PROFILE_WALKER_ATST_SCALED,
        "film-scale 2.28x AT-ST auto-detected while aiming in 3P");
    TEST_NEAR(g_last_set_pose[10], -2.5f - g_opts.atst_scaled_pullback_distance, 1e-4f, "AT-ST scaled pullback Y");
    TEST_NEAR(g_last_set_pose[11], 2.4f + g_opts.atst_scaled_lift_z, 1e-4f, "AT-ST scaled lift Z");
}

static void test_havok_wall_clamp_and_raycast_fail_closed(void)
{
    ResetHarnessScene();
    g_opts.lego_pullback_distance = 2.00f;
    g_opts.lego_lift_z = 0.00f;
    g_opts.wall_safety_margin = 0.30f;

    g_ray_should_hit = 1;
    g_ray_hit_point = ecp_v3(0.0f, -3.00f, 1.70f);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1, "clamped wall pose written");
    TEST_NEAR(g_last_set_pose[10], -2.70f, 1e-4f, "camera stopped 0.30m in front of wall");
    TEST_CHECK(g_rt.raycast_hits == 1u, "raycast_hits incremented on clamped hit");

    g_set_pose_calls = 0;
    g_ray_hit_point = ecp_v3(0.0f, -2.20f, 1.70f);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "wall within safety margin suppresses pullback completely");
    TEST_CHECK(g_rt.raycast_hits == 2u && g_rt.last_bypass == ECP_BYPASS_WALL_BLOCKED, "wall_blocked bypass recorded");

    g_ray_should_hit = 0;
    g_set_pose_calls = 0;
    g_raycastState = -1;
    g_raycast = NULL;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "fail-closed when Havok raycast is unavailable");
}

static void test_teardown_4_to_5_session_end_and_mxcsr_lasterror(void)
{
    ResetHarnessScene();
    g_corrupt_csr_inside_raycast = 1;
    unsigned int orig_csr = _mm_getcsr();
    SetLastError(0x13572468u);

    EcpStepDetour(NULL);
    TEST_CHECK(g_orig_step_calls == 1 && g_set_pose_calls == 1, "active frame ran");
    TEST_CHECK(g_seen_err_in_orig == 0x13572468u && GetLastError() == 0x13572468u, "GetLastError preserved across StepDetour");
    TEST_CHECK(g_seen_csr_in_orig == orig_csr && _mm_getcsr() == orig_csr, "SSE MXCSR preserved across StepDetour");

    g_set_pose_calls = 0;
    g_update_unit_calls = 0;
    LONG epoch_before = InterlockedCompareExchange(&g_cacheEpoch, 0, 0);
    HfStateEvent sev = {{sizeof(sev), HF_ON_STATE, 10}, HF_STATE_MISSION, HF_STATE_PREPARE_SHIP};
    g_mock_game_state = HF_STATE_PREPARE_SHIP;
    if (g_sub_state) g_sub_state((HfMod*)&g_mock_mod_obj, &sev, NULL);
    TEST_CHECK(InterlockedCompareExchange(&g_disarmed, 0, 0) == 1, "4 -> 5 atomically disarmed");
    TEST_CHECK(InterlockedCompareExchange(&g_cacheEpoch, 0, 0) != epoch_before && g_rt.cam_ref != 0u,
        "H1: 4 -> 5 state callback bumps g_cacheEpoch without touching g_rt off the render thread");

    EcpStepDetour(NULL);
    TEST_CHECK(g_rt.world == 0 && g_rt.cam_ref == 0u, "H1: render thread purges g_rt on g_cacheEpoch mismatch");
    TEST_CHECK(g_set_pose_calls == 0 && g_update_unit_calls == 0, "zero UPDATE_UNIT calls during 4 -> 5 teardown");

    EcpUpdateArmForState(HF_STATE_MISSION);
    g_mock_game_state = HF_STATE_PREPARE_SHIP;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0 && InterlockedCompareExchange(&g_disarmed, 0, 0) == 1,
        "StepDetour self-disarms on live state == 5 before event delivery");

    if (g_on_session_end) g_on_session_end((HfMod*)&g_mock_mod_obj, HF_EVENT_SESSION_END, NULL);
    g_mock_game_state = HF_STATE_MISSION;
    EcpUpdateArmForState(HF_STATE_MISSION);
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0 && InterlockedCompareExchange(&g_sessionEnded, 0, 0) == 1,
        "HF_EVENT_SESSION_END permanently latches disarmed");

    TEST_CHECK(hellforge_mod_unload(&g_mock_api, (HfMod*)&g_mock_mod_obj, HF_UNLOAD_REMOVE) == 0, "clean unload");
}

static void test_level11_65672_unit_stress_and_qpc_budget(void)
{
    const uint32_t kD11Units = 65672u;
    MockPropUnit* props = (MockPropUnit*)calloc(kD11Units, sizeof(MockPropUnit));
    uintptr_t* big_list = (uintptr_t*)calloc(kD11Units, sizeof(uintptr_t));
    TEST_CHECK(props != NULL && big_list != NULL, "allocated 65,672-unit D11 world buffers");

    ResetHarnessScene();
    for (uint32_t i = 0; i < kD11Units; i++) {
        props[i].vtable = (uintptr_t)(g_mock_exe_module + ECP_EXE_UNIT_VTABLE);
        props[i].ref = 0x500000u + i;
        props[i].pad0C = MOCK_PROP_TAG;
        props[i].resource_storage = 0xDEAD000000000000ULL + (uint64_t)i;
        props[i].res_ptr = (uintptr_t)&props[i].resource_storage;
        props[i].cam_count = 0;
        props[i].graph.node_count = 8u;
        big_list[i] = (uintptr_t)&props[i];
    }

    /* Case A: Owen's exact D11 demo scenario — 65,672 live units, player alive wearing NON-LEGO armor (garments=0)! */
    g_diver_unit.first_child = 0;
    big_list[0] = (uintptr_t)&g_cam_unit;
    big_list[42123] = (uintptr_t)&g_diver_unit;
    memcpy(g_mock_world + ECP_WORLD_UNITS_COUNT, &kD11Units, 4);
    memcpy(g_mock_world + ECP_WORLD_UNITS_ARRAY, &big_list, sizeof(big_list));

    uint64_t max_reads_frame = 0;
    uint64_t steady_reads_max = 0;
    LARGE_INTEGER qf, qt0, qt1;
    QueryPerformanceFrequency(&qf);
    QueryPerformanceCounter(&qt0);

    for (uint32_t frame = 0; frame < 1000u; frame++) {
        uint64_t r0 = g_hf_read_calls;
        EcpStepDetour(NULL);
        uint64_t reads = g_hf_read_calls - r0;
        if (reads > max_reads_frame) max_reads_frame = reads;
        if (frame >= 2u && reads > steady_reads_max) steady_reads_max = reads;
    }
    QueryPerformanceCounter(&qt1);
    double d11_non_lego_avg_us =
        (double)(qt1.QuadPart - qt0.QuadPart) * 1000000.0 / ((double)qf.QuadPart * 1000.0);

    printf("  [D11 65,672-unit non-LEGO 1000 frames] cold_max_reads=%llu steady_max_reads=%llu avg_us=%.2f\n",
        (unsigned long long)max_reads_frame,
        (unsigned long long)steady_reads_max,
        d11_non_lego_avg_us);

    TEST_CHECK(g_set_pose_calls == 0, "D11 non-LEGO armor performs zero pose writes");
    TEST_CHECK(max_reads_frame <= 64u, "D11 cold discovery reads strictly bounded (<= 64 vs 460,000 in 0.1.0)");
    TEST_CHECK(steady_reads_max <= 60u, "D11 steady-state reads strictly O(1) (<= 60 across 65,672 units)");
    TEST_CHECK(d11_non_lego_avg_us < 50.0, "D11 non-LEGO average frame time < 50 us");

    /* Case B: Adversarial midpoint camera discovery (camera placed at index 32836 of 65,672). */
    big_list[0] = (uintptr_t)&props[0];
    big_list[32836] = (uintptr_t)&g_cam_unit;
    EcpForgetRuntimeCache();
    g_rt.world = (uintptr_t)g_mock_world;
    g_diver_unit.first_child = (uintptr_t)&g_garment_unit;
    g_set_pose_calls = 0;

    uint64_t worst_slice_reads = 0;
    uint32_t frames_to_acquire = 0;
    for (uint32_t frame = 0; frame < 1200u; frame++) {
        uint64_t r0 = g_hf_read_calls;
        EcpStepDetour(NULL);
        uint64_t reads = g_hf_read_calls - r0;
        if (reads > worst_slice_reads) worst_slice_reads = reads;
        if (g_rt.cam_ref != 0u) {
            frames_to_acquire = frame + 1u;
            break;
        }
    }
    printf("  [D11 midpoint cam index=32836] acquired_in_frames=%u worst_slice_reads=%llu\n",
        frames_to_acquire, (unsigned long long)worst_slice_reads);
    TEST_CHECK(g_rt.cam_ref == g_cam_unit.ref && g_set_pose_calls == 1,
        "adversarial midpoint camera acquired cleanly via bidirectional micro-slices");
    TEST_CHECK(worst_slice_reads <= 260u,
        "adversarial midpoint camera slice never scans more than 64 units in a single frame");

    /* Case C: Occupied AT-ST Walker at index 65,670 via HF_ON_SPAWN in 65,672-unit world -> Frame-1 O(1)! */
    big_list[32836] = (uintptr_t)&props[32836];
    big_list[65671] = (uintptr_t)&g_cam_unit; /* Tail of world->units — bidirectional finds on Frame 1! */
    InitMockUnit(&g_walker_unit, 0x400099u, kEcpWalkerResources[0], 198u, 0);
    g_walker_unit.node_names[13] = ECP_NODE_HASH_ATST_PELVIS;
    InitIdentityWorldMatrix(g_walker_unit.world_poses[0], 0.0f, 3.0f, 0.0f);
    InitIdentityWorldMatrix(g_walker_unit.world_poses[13], 0.0f, 3.0f, 4.70f);
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.5f, 2.4f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.5f, 2.4f);
    big_list[65670] = (uintptr_t)&g_walker_unit;
    g_diver_unit.graph.link_parent = g_walker_unit.graph.world_poses;

    EcpForgetRuntimeCache();
    RegisterMockEntityDescriptor(11u, kEcpWalkerResources[0], 0x300077u, g_walker_unit.ref, 0xCCu, 1u);
    HfSpawnEvent wsp = {{sizeof(wsp), HF_ON_SPAWN, 2}, 0x300077u, 0xCCu, kEcpWalkerResources[0], 1u};
    if (g_sub_spawn) g_sub_spawn((HfMod*)&g_mock_mod_obj, &wsp, NULL);

    g_set_pose_calls = 0;
    QueryPerformanceCounter(&qt0);
    uint64_t frame1_walker_reads = 0;
    uint64_t steady_walker_reads_max = 0;
    for (uint32_t frame = 0; frame < 500u; frame++) {
        uint64_t r0 = g_hf_read_calls;
        EcpStepDetour(NULL);
        uint64_t r = g_hf_read_calls - r0;
        if (frame == 0u) frame1_walker_reads = r;
        else if (r > steady_walker_reads_max) steady_walker_reads_max = r;
    }
    QueryPerformanceCounter(&qt1);
    double d11_walker_avg_us =
        (double)(qt1.QuadPart - qt0.QuadPart) * 1000000.0 / ((double)qf.QuadPart * 500.0);

    printf("  [D11 65,672-unit AT-ST walker 500 frames] frame1_reads=%llu steady_max_reads=%llu avg_us=%.2f\n",
        (unsigned long long)frame1_walker_reads,
        (unsigned long long)steady_walker_reads_max,
        d11_walker_avg_us);

    TEST_CHECK(g_set_pose_calls == 500 && g_rt.active_profile == ECP_PROFILE_WALKER_ATST_SCALED,
        "HF_ON_SPAWN walker activates on Frame 1 in 65,672-unit world");
    TEST_CHECK(frame1_walker_reads <= 128u && steady_walker_reads_max <= 80u,
        "Frame-1 and steady-state walker + tail camera acquisition are strictly O(1)");
    TEST_CHECK(d11_walker_avg_us < 50.0, "D11 active AT-ST walker average frame time < 50 us");

    /* Case D: Compact LEGO armor on foot in 65,672-unit world -> 500 frames O(1) + < 50 us! */
    EcpClearWalkerSlots();
    g_diver_unit.graph.link_parent = 0;
    g_diver_unit.first_child = (uintptr_t)&g_garment_unit;
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.0f, 1.7f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.0f, 1.7f);
    EcpForgetRuntimeCache();

    g_set_pose_calls = 0;
    uint64_t steady_lego_reads_max = 0;
    QueryPerformanceCounter(&qt0);
    for (uint32_t frame = 0; frame < 500u; frame++) {
        uint64_t r0 = g_hf_read_calls;
        EcpStepDetour(NULL);
        uint64_t r = g_hf_read_calls - r0;
        if (frame >= 2u && r > steady_lego_reads_max) steady_lego_reads_max = r;
    }
    QueryPerformanceCounter(&qt1);
    double d11_lego_avg_us =
        (double)(qt1.QuadPart - qt0.QuadPart) * 1000000.0 / ((double)qf.QuadPart * 500.0);

    printf("  [D11 65,672-unit Compact LEGO 500 frames] steady_max_reads=%llu avg_us=%.2f\n",
        (unsigned long long)steady_lego_reads_max,
        d11_lego_avg_us);

    TEST_CHECK(g_set_pose_calls == 500 && g_rt.active_profile == ECP_PROFILE_LEGO,
        "Compact LEGO activates on Frame 1 in 65,672-unit world");
    TEST_CHECK(steady_lego_reads_max <= 80u, "Compact LEGO steady-state reads strictly O(1)");
    TEST_CHECK(d11_lego_avg_us < 50.0, "D11 Compact LEGO average frame time < 50 us");

    free(big_list);
    free(props);
}

static void test_coop_teammate_hijack_and_walker_fail_closed(void)
{
    /* Place a LEGO-wearing teammate Helldiver right in front of the camera at (0, 0.5, 0). */
    ResetHarnessScene();
    InitMockUnit(&g_teammate_unit, 0x400088u, ECP_RES_HELLDIVER_AVATAR, 107u, 0);
    g_teammate_unit.node_names[90] = ECP_NODE_HASH_HEAD;
    InitIdentityWorldMatrix(g_teammate_unit.world_poses[0], 0.0f, 0.5f, 0.0f);
    InitIdentityWorldMatrix(g_teammate_unit.world_poses[90], 0.0f, 0.5f, 1.58f);
    InitMockUnit(&g_teammate_garment, 0x400089u, 0x0208D1FFFB6C1675ULL, 21u, 0);
    g_teammate_unit.first_child = (uintptr_t)&g_teammate_garment;

    g_mock_unit_list[2] = (uintptr_t)&g_teammate_unit;
    g_mock_unit_count = 3u;
    memcpy(g_mock_world + ECP_WORLD_UNITS_COUNT, &g_mock_unit_count, 4);

    /* 1. Local player wears vanilla non-LEGO armor -> teammate's LEGO helmet MUST NOT trigger pullback! */
    g_diver_unit.first_child = 0;
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "co-op teammate LEGO kit rejected when local player wears vanilla armor");

    /* 2. Local player wears LEGO armor AND is in FPV/iron sights (0.15m from local head, looking up so local root
     *    is behind camera while teammate stands 2.0m ahead in view) -> MUST bypass cleanly! */
    g_diver_unit.first_child = (uintptr_t)&g_garment_unit;
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -0.15f, 1.58f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -0.15f, 1.58f);
    InitIdentityWorldMatrix(g_teammate_unit.world_poses[0], 0.0f, 2.0f, 0.0f);
    InitIdentityWorldMatrix(g_teammate_unit.world_poses[90], 0.0f, 2.0f, 1.58f);
    EcpForgetRuntimeCache();
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "co-op teammate in front of FPV camera cannot hijack local avatar");

    /* 3. Walker mode MUST fail closed if Camera+0x34 vertical_fov <= 0.0f or if local avatar is unresolved. */
    ResetHarnessScene();
    InitMockUnit(&g_walker_unit, 0x400010u, kEcpWalkerResources[0], 198u, 0);
    InitIdentityWorldMatrix(g_walker_unit.world_poses[0], 0.0f, 3.0f, 0.0f);
    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.5f, 2.4f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.5f, 2.4f);
    g_mock_unit_list[2] = (uintptr_t)&g_walker_unit;
    g_mock_unit_count = 3u;
    memcpy(g_mock_world + ECP_WORLD_UNITS_COUNT, &g_mock_unit_count, 4);
    g_diver_unit.graph.link_parent = g_walker_unit.graph.world_poses;
    g_cam_unit.cam.vertical_fov = 0.0f; /* Unreadable / zero FOV */
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0 && g_rt.last_bypass == ECP_BYPASS_FOV,
        "walker mode fails closed when vertical_fov == 0.0f");

    /* Unresolved local avatar (d.reference = 0x999999 invalid) facing parked walker -> MUST NOT trigger! */
    g_cam_unit.cam.vertical_fov = 0.96f;
    RegisterMockEntityDescriptor(5u, ECP_RES_HELLDIVER_AVATAR, g_mock_net_to_entity_id, 0x999999u, g_mock_net_to_entity_key, 1u);
    EcpForgetRuntimeCache();
    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 0, "walker mode fails closed when local avatar unit is unresolved");
}

static void test_paused_unoverwritten_pose_drift_guard(void)
{
    ResetHarnessScene();
    g_mutate_cam_pose_in_place = 1;

    /* Simulate 20 consecutive render frames while game simulation is paused (game.dll does NOT rewrite local_poses[0]). */
    for (int f = 0; f < 20; f++) {
        EcpStepDetour(NULL);
    }
    TEST_CHECK(g_set_pose_calls == 20, "20 paused frames rendered");
    TEST_NEAR(g_cam_unit.local_poses[0][10], -2.0f - g_opts.lego_pullback_distance, 1e-4f,
        "zero cumulative camera Y drift across 20 unoverwritten frames");
    TEST_NEAR(g_cam_unit.local_poses[0][11], 1.7f + g_opts.lego_lift_z, 1e-4f,
        "zero cumulative camera Z drift across 20 unoverwritten frames");

    /* Now trigger ADS bypass while local_poses[0] still holds our written pose -> base pose (-2.0, 1.7) restored! */
    g_cam_unit.cam.vertical_fov = 0.70f;
    EcpStepDetour(NULL);
    TEST_NEAR(g_cam_unit.local_poses[0][10], -2.0f, 1e-4f, "unmodified base Y pose immediately restored on bypass");
    TEST_NEAR(g_cam_unit.local_poses[0][11], 1.7f, 1e-4f, "unmodified base Z pose immediately restored on bypass");
}

static void test_health_telemetry_and_hard_watchdog(void)
{
    ResetHarnessScene();
    g_testTickMsOverride = 10000ULL;
    g_health.next_log_ms = 10000ULL + ECP_HEALTH_FIRST_MS;
    g_ray_should_hit = 1;
    g_ray_hit_point = ecp_v3(0.0f, -3.50f, 1.70f);

    EcpStepDetour(NULL);
    TEST_CHECK(LogHistoryContains("ECP_CAMERA camera_acquired"), "logged camera_acquired");
    TEST_CHECK(LogHistoryContains("ECP_CAMERA profile_change from=none to=lego"), "logged profile_change to lego");

    /* Advance clock past first health interval -> verify 30s health telemetry line fields. */
    g_testTickMsOverride = 10000ULL + ECP_HEALTH_FIRST_MS + 10ULL;
    EcpStepDetour(NULL);
    TEST_CHECK(
        LogHistoryContains("ECP_CAMERA health row=1 profile=lego cam_ok=1 avatar_ok=1 walker_ok=0 applied_frames=2 raycast_hits=2 bypass_reason=active"),
        "logged 30s health telemetry with all required metrics");
    TEST_CHECK(LogHistoryContains("tick_us_avg=") && LogHistoryContains("tick_us_max="),
        "health telemetry includes QPC tick_us_avg and tick_us_max");

    /* Inject 8 consecutive > 1000 us slow frames -> hard watchdog MUST fail closed & restore base pose! */
    g_ray_should_hit = 0;
    g_mutate_cam_pose_in_place = 1;
    EcpStepDetour(NULL);
    TEST_NEAR(g_cam_unit.local_poses[0][10], -2.0f - g_opts.lego_pullback_distance, 1e-4f, "pose offset active before watchdog");

    g_testSyntheticUs = 1500ULL;
    for (int i = 0; i < 8; i++) {
        EcpStepDetour(NULL);
    }
    TEST_CHECK(InterlockedCompareExchange(&g_watchdog.tripped, 0, 0) == 1, "watchdog tripped after 8 frames > 1000 us");
    TEST_CHECK(LogHistoryContains("ECP_WATCHDOG FAIL-CLOSED"), "watchdog logged fail-closed reason");
    TEST_NEAR(g_cam_unit.local_poses[0][10], -2.0f, 1e-4f, "watchdog restored unmodified base camera pose on trip");

    /* While tripped, StepDetour must perform ZERO hf->read calls and ZERO pose writes! */
    g_testSyntheticUs = 0ULL;
    uint64_t r0 = g_hf_read_calls;
    int p0 = g_set_pose_calls;
    EcpStepDetour(NULL);
    TEST_CHECK((g_hf_read_calls - r0) == 0ULL && g_set_pose_calls == p0,
        "tripped watchdog short-circuits in O(1) with 0 memory reads");

    /* Transitioning out of mission and into a new mission resets the per-mission watchdog cleanly. */
    EcpUpdateArmForState(HF_STATE_PREPARE_SHIP);
    EcpUpdateArmForState(HF_STATE_MISSION);
    TEST_CHECK(InterlockedCompareExchange(&g_watchdog.tripped, 0, 0) == 0, "watchdog resets cleanly for next mission");
    EcpStepDetour(NULL);
    TEST_CHECK(g_watchdog.slow_score == 0u && g_rt.seen_epoch == InterlockedCompareExchange(&g_cacheEpoch, 0, 0),
        "H1: render thread resets g_watchdog.slow_score and syncs seen_epoch on new mission");
}

static volatile LONG g_churn_stop = 0;

static DWORD WINAPI StateAndSpawnChurnThread(LPVOID param)
{
    (void)param;
    for (uint32_t iter = 0; iter < 2000u && InterlockedCompareExchange(&g_churn_stop, 0, 0) == 0; iter++) {
        g_mock_game_state = HF_STATE_PREPARE_SHIP;
        EcpUpdateArmForState(HF_STATE_PREPARE_SHIP);
        if ((iter & 3u) == 0u && g_sub_mend) {
            HfStateEvent mend = {{sizeof(mend), HF_ON_MISSION_END, iter}, HF_STATE_MISSION, HF_STATE_PREPARE_SHIP};
            g_sub_mend((HfMod*)&g_mock_mod_obj, &mend, NULL);
        }
        g_mock_game_state = HF_STATE_MISSION;
        EcpUpdateArmForState(HF_STATE_MISSION);

        HfSpawnEvent sp = {
            {sizeof(sp), HF_ON_SPAWN, iter},
            0x200010u + (iter % 20u),
            0x90u + (iter % 20u),
            kEcpWalkerResources[iter & 3u],
            1u
        };
        if (g_sub_spawn) g_sub_spawn((HfMod*)&g_mock_mod_obj, &sp, NULL);
        if ((iter % 17u) == 0u) EcpClearWalkerSlots();
    }
    return 0;
}

static void test_h1_h2_m3_concurrency_and_vtable_guards(void)
{
    /* 1. M3: Verify Unit vtable pin in offline capture (013F6000.bin) if present, and fail-closed rejection on mismatch. */
    const char* rdata_path =
        "D:\\ClaudeWork\\clonedivers-lab\\camera-evidence-astra\\camera-capture-mission\\capture-helldivers2.exe-013F6000.bin";
    FILE* rf = fopen(rdata_path, "rb");
    if (rf) {
        uint64_t fn_graph = 0, fn_cam_count = 0, fn_cam = 0;
        long base_off = (long)(ECP_EXE_UNIT_VTABLE - 0x013F6000u);
        TEST_CHECK(fseek(rf, base_off + (long)ECP_UNIT_VTABLE_GRAPH, SEEK_SET) == 0 &&
                   fread(&fn_graph, 1, 8, rf) == 8, "read Unit vtable +0xE8 in 013F6000.bin");
        TEST_CHECK(fseek(rf, base_off + (long)ECP_UNIT_VTABLE_CAM_COUNT, SEEK_SET) == 0 &&
                   fread(&fn_cam_count, 1, 8, rf) == 8, "read Unit vtable +0x160 in 013F6000.bin");
        TEST_CHECK(fseek(rf, base_off + (long)ECP_UNIT_VTABLE_CAMERA, SEEK_SET) == 0 &&
                   fread(&fn_cam, 1, 8, rf) == 8, "read Unit vtable +0x168 in 013F6000.bin");
        /* PASS3.md: exe_base = 0x00007FF648DF0000; slots point to +0x2BD870, +0x2BD950, +0x2BD960 */
        TEST_CHECK((fn_graph & 0xFFFFu) == 0xD870u &&
                   (fn_cam_count - fn_graph) == (0x2BD950ULL - 0x2BD870ULL) &&
                   (fn_cam - fn_graph) == (0x2BD960ULL - 0x2BD870ULL),
                   "M3: offline capture confirms Stingray Unit vtable slots at exe+0x1676D80");
        fclose(rf);
    }

    ResetHarnessScene();
    g_garment_unit.vtable = (uintptr_t)g_mock_rogue_vtable;
    EcpStepDetour(NULL);
    TEST_CHECK(InterlockedCompareExchange(&g_rogue_vtable_calls, 0, 0) == 0 && g_set_pose_calls == 0,
        "M3: non-Unit vtable on child attachment rejected with 0 indirect vtable calls");

    ResetHarnessScene();
    g_cam_unit.vtable = (uintptr_t)g_mock_rogue_vtable;
    EcpStepDetour(NULL);
    TEST_CHECK(InterlockedCompareExchange(&g_rogue_vtable_calls, 0, 0) == 0 && g_set_pose_calls == 0,
        "M3: non-Unit vtable in world->units rejected with 0 indirect vtable calls");

    /* 2. H2: Render-thread-private walker_slot_cache + generation counter prevents stale unit_ref leaks across slot reuse. */
    ResetHarnessScene();
    g_opts.lego_enabled = 0;
    InitMockUnit(&g_walker_unit, 0x400010u, kEcpWalkerResources[0], 198u, 0);
    g_walker_unit.node_names[13] = ECP_NODE_HASH_ATST_PELVIS;
    InitIdentityWorldMatrix(g_walker_unit.world_poses[0], 0.0f, 3.0f, 0.0f);
    InitIdentityWorldMatrix(g_walker_unit.world_poses[13], 0.0f, 3.0f, 2.06f);

    InitMockUnit(&g_walker_unit_b, 0x400020u, kEcpWalkerResources[1], 106u, 0);
    g_walker_unit_b.node_names[12] = ECP_NODE_HASH_ATST_PELVIS;
    InitIdentityWorldMatrix(g_walker_unit_b.world_poses[0], 0.0f, 3.0f, 0.0f);
    InitIdentityWorldMatrix(g_walker_unit_b.world_poses[12], 0.0f, 3.0f, 4.70f); /* Film-scale S2c EXO-49 AT-ST! */

    InitLocalPoseLookingPlusY(g_cam_unit.local_poses[0], 0.0f, -2.5f, 2.4f);
    InitIdentityWorldMatrix((float*)(g_mock_world + ECP_WORLD_RENDER_POSE), 0.0f, -2.5f, 2.4f);

    RegisterMockEntityDescriptor(9u, kEcpWalkerResources[0], 0x200001u, g_walker_unit.ref, 0x91u, 1u);
    HfSpawnEvent sp_a = {{sizeof(sp_a), HF_ON_SPAWN, 1}, 0x200001u, 0x91u, kEcpWalkerResources[0], 1u};
    g_sub_spawn((HfMod*)&g_mock_mod_obj, &sp_a, NULL);
    g_diver_unit.graph.link_parent = g_walker_unit.graph.world_poses;

    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1 && g_rt.active_profile == ECP_PROFILE_WALKER &&
               g_rt.walker_slot_cache[0].unit_ref == g_walker_unit.ref,
               "H2: slot 0 cached Walker A unit_ref privately in g_rt");

    /* Recycle slot 0 via EcpClearWalkerSlots + new spawn of Walker B (EXO-49 S2c AT-ST) while stale Walker A still exists! */
    EcpClearWalkerSlots();
    RegisterMockEntityDescriptor(10u, kEcpWalkerResources[1], 0x200002u, g_walker_unit_b.ref, 0x92u, 1u);
    HfSpawnEvent sp_b = {{sizeof(sp_b), HF_ON_SPAWN, 2}, 0x200002u, 0x92u, kEcpWalkerResources[1], 1u};
    g_sub_spawn((HfMod*)&g_mock_mod_obj, &sp_b, NULL);
    g_diver_unit.graph.link_parent = g_walker_unit_b.graph.world_poses;
    g_rt.walker_ref = 0u; /* Force Tier-1 lookup from slot 0 */
    g_set_pose_calls = 0;

    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1 && g_rt.active_profile == ECP_PROFILE_WALKER_ATST_SCALED &&
               g_rt.walker_slot_cache[0].unit_ref == g_walker_unit_b.ref,
               "H2: slot 0 generation bump invalidated stale Walker A ref and resolved Walker B cleanly");

    /* Same-entity re-spawn update on 0x200002 -> bumps generation & refreshes unit_ref! */
    g_walker_unit_b.ref = 0x400021u;
    RegisterMockEntityDescriptor(10u, kEcpWalkerResources[1], 0x200002u, g_walker_unit_b.ref, 0x93u, 1u);
    HfSpawnEvent sp_b2 = {{sizeof(sp_b2), HF_ON_SPAWN, 3}, 0x200002u, 0x93u, kEcpWalkerResources[1], 1u};
    g_sub_spawn((HfMod*)&g_mock_mod_obj, &sp_b2, NULL);
    g_rt.walker_ref = 0u;
    g_set_pose_calls = 0;

    EcpStepDetour(NULL);
    TEST_CHECK(g_set_pose_calls == 1 && g_rt.walker_slot_cache[0].unit_ref == 0x400021u,
               "H2: same-entity re-spawn bumped generation and refreshed unit_ref");

    /* 3. H1 + H2 Multi-Threaded Stress Test: concurrent state/mission-end/spawn churn vs render-thread EcpStepDetour. */
    ResetHarnessScene();
    g_mutate_cam_pose_in_place = 1;
    InterlockedExchange(&g_churn_stop, 0);
    InterlockedExchange(&g_invalid_pose_calls, 0);
    HANDLE worker = CreateThread(NULL, 0, StateAndSpawnChurnThread, NULL, 0, NULL);
    TEST_CHECK(worker != NULL, "spawned concurrent H1/H2 churn thread");
    for (uint32_t f = 0; f < 2000u; f++) {
        EcpStepDetour(NULL);
    }
    InterlockedExchange(&g_churn_stop, 1);
    WaitForSingleObject(worker, 5000);
    CloseHandle(worker);
    TEST_CHECK(InterlockedCompareExchange(&g_invalid_pose_calls, 0, 0) == 0,
        "H1/H2: 0 invalid/zeroed cam_ref or base_pose writes across 2,000 concurrent state/teardown/spawn transitions");
}

static uint32_t g_fake_sub_calls = 0;
static uint64_t FakeOrigSub(void* a, void* b, void* c, uint64_t d, void* e)
{
    (void)a; (void)b; (void)c; (void)d; (void)e;
    g_fake_sub_calls++;
    return 0xCAFEBABEULL;
}

static void test_proton_fallback_substep_caller_filter(void)
{
    ResetHarnessScene();
    g_origSubStep = FakeOrigSub;
    g_fake_sub_calls = 0;
    g_set_pose_calls = 0;
    /* Non-matching caller (e.g. exe+0x68922A) -> delegates to g_origSubStep WITHOUT applying camera pullback! */
    uint64_t r1 = EcpSubStepDetourWithCaller((void*)1, (void*)2, (void*)3, 4ULL, (void*)5, g_exeBase + 0x68922Au);
    TEST_CHECK(r1 == 0xCAFEBABEULL && g_fake_sub_calls == 1u && g_set_pose_calls == 0,
        "Proton SubStep ignores non-CAMERA_STEP callers");
    /* Matching caller (exe+0x689C00) -> applies camera pullback AND delegates to g_origSubStep! */
    uint64_t r2 = EcpSubStepDetourWithCaller((void*)1, (void*)2, (void*)3, 4ULL, (void*)5, g_exeBase + ECP_EXE_CAMERA_STEP_RET);
    TEST_CHECK(r2 == 0xCAFEBABEULL && g_fake_sub_calls == 2u && g_set_pose_calls == 1,
        "Proton SubStep applies pullback on exact exe+0x689C00 return address");
    g_origSubStep = NULL;
}

static void test_hellforge_05_relocated_4096_entity_manager(void)
{
    ResetHarnessScene();
    g_mock_api.version = 20u;
    TEST_CHECK(hellforge_mod_init(&g_mock_api, (HfMod*)&g_mock_mod_obj) == 0, "SDK 20 handshake succeeds");
    ecp_test_bind_engine_fns(
        (uintptr_t)g_mock_exe_module,
        (uintptr_t)g_mock_game_module,
        MockOrigStep,
        MockSetLocalPose,
        MockUpdateUnit,
        MockLookupUnit,
        MockMainWorld,
        MockRaycast,
        1);

    const size_t arena_size = 0x2000000u;
    uint8_t* combo = (uint8_t*)calloc(1, arena_size);
    TEST_CHECK(combo != NULL, "allocated relocated 4,096-record EntityManager slab");
    memcpy(combo, g_mock_entity_mgr, sizeof(g_mock_entity_mgr));
    int32_t off1 = 0x1200000; /* +18 MB, exact multiple of 24 & 8 */
    uint8_t* reloc_recs = combo + (size_t)off1;
    int32_t off8 = off1 / 8;
    uint32_t cap = 4096u;

    uint8_t s_rec8[7] = {0x48u, 0x8Du, 0x80u, 0, 0, 0, 0};
    uint8_t s_rec1[7] = {0x49u, 0x8Du, 0x85u, 0, 0, 0, 0};
    uint8_t s_cnt1[7] = {0x8Bu, 0xD0u, 0xBAu, 0, 0, 0, 0};
    uint8_t s_cnt2[7] = {0xF3u, 0x00u, 0x3Du, 0, 0, 0, 0};
    memcpy(s_rec8 + 3u, &off8, 4u);
    memcpy(s_rec1 + 3u, &off1, 4u);
    memcpy(s_cnt1 + 3u, &cap, 4u);
    memcpy(s_cnt2 + 3u, &cap, 4u);
    memcpy(g_mock_game_module + CD_EM_SITE_REC8_RVA, s_rec8, 7u);
    memcpy(g_mock_game_module + CD_EM_SITE_REC1_RVA, s_rec1, 7u);
    memcpy(g_mock_game_module + CD_EM_SITE_CNT1_RVA, s_cnt1, 7u);
    memcpy(g_mock_game_module + CD_EM_SITE_CNT2_RVA, s_cnt2, 7u);
    g_emLayoutResolved = 0;

    const uint32_t kSlot = 3500u;
    const uint32_t kNet = 0x79u;
    const uint32_t kEnt = 0x200079u;
    EcpEntityDescriptor want = {kEcpWalkerResources[0], kEnt, 0x400010u, kNet, 1u};
    memcpy(reloc_recs + (size_t)kSlot * 24u, &want, sizeof(want));

    memset(combo + ECP_EM_NETWORK_MAP, 0, 20u);
    memset(combo + ECP_EM_ENTITY_MAP, 0, 20u);
    uintptr_t netPtr = (uintptr_t)g_mock_em_net_buckets;
    uintptr_t entPtr = (uintptr_t)g_mock_em_ent_buckets;
    uint32_t bCap = 64u, bEmpty = 0xFFFFFFFFu, bMult = 1u;
    memcpy(combo + ECP_EM_NETWORK_MAP + 0u, &netPtr, 8u);
    memcpy(combo + ECP_EM_NETWORK_MAP + 8u, &bCap, 4u);
    memcpy(combo + ECP_EM_NETWORK_MAP + 12u, &bEmpty, 4u);
    memcpy(combo + ECP_EM_NETWORK_MAP + 16u, &bMult, 4u);
    memcpy(combo + ECP_EM_ENTITY_MAP + 0u, &entPtr, 8u);
    memcpy(combo + ECP_EM_ENTITY_MAP + 8u, &bCap, 4u);
    memcpy(combo + ECP_EM_ENTITY_MAP + 12u, &bEmpty, 4u);
    memcpy(combo + ECP_EM_ENTITY_MAP + 16u, &bMult, 4u);

    memset(g_mock_em_net_buckets, 0xFF, sizeof(g_mock_em_net_buckets));
    memset(g_mock_em_ent_buckets, 0xFF, sizeof(g_mock_em_ent_buckets));
    memcpy(g_mock_em_net_buckets + (kNet & 63u) * 8u + 0u, &kNet, 4u);
    memcpy(g_mock_em_net_buckets + (kNet & 63u) * 8u + 4u, &kSlot, 4u);
    memcpy(g_mock_em_ent_buckets + (kEnt & 63u) * 8u + 0u, &kEnt, 4u);
    memcpy(g_mock_em_ent_buckets + (kEnt & 63u) * 8u + 4u, &kSlot, 4u);

    EcpEntityDescriptor gotNet = {0}, gotEnt = {0};
    TEST_CHECK(EcpReadDescriptorByNetwork((uintptr_t)combo, kNet, &gotNet) == 1 && gotNet.entity == kEnt,
               "EcpReadDescriptorByNetwork resolves relocated slot 3,500 (> 2,048)");
    TEST_CHECK(EcpReadDescriptorByEntity((uintptr_t)combo, kEnt, &gotEnt) == 1 && gotEnt.resource == kEcpWalkerResources[0],
               "EcpReadDescriptorByEntity resolves relocated slot 3,500 (> 2,048)");

    memset(g_mock_game_module + CD_EM_SITE_REC8_RVA, 0, 7u);
    memset(g_mock_game_module + CD_EM_SITE_REC1_RVA, 0, 7u);
    memset(g_mock_game_module + CD_EM_SITE_CNT1_RVA, 0, 7u);
    memset(g_mock_game_module + CD_EM_SITE_CNT2_RVA, 0, 7u);
    g_emLayoutResolved = 0;
    free(combo);
}

int main(int argc, char** argv)
{
    const char* temp_dir = argc > 1 ? argv[1] : NULL;
    test_options_parser_and_bounds(temp_dir);
    test_all_61_lego_garments_and_vanilla_rejection();
    test_offline_capture_pins_if_present();
    test_lego_on_foot_and_coop_slots_0_to_3();
    test_player_downed_dead_and_terminal_camera_guards();
    test_instant_bypass_ads_fpv_hellpod_ship_armory();
    test_walker_profiles_atte_atst_and_parked_rejection();
    test_havok_wall_clamp_and_raycast_fail_closed();
    test_teardown_4_to_5_session_end_and_mxcsr_lasterror();
    test_level11_65672_unit_stress_and_qpc_budget();
    test_coop_teammate_hijack_and_walker_fail_closed();
    test_paused_unoverwritten_pose_drift_guard();
    test_health_telemetry_and_hard_watchdog();
    test_h1_h2_m3_concurrency_and_vtable_guards();
    test_proton_fallback_substep_caller_filter();
    test_hellforge_05_relocated_4096_entity_manager();

    printf("empire-camera-pullback v0.1.5 unit suite: %d checks run, %d failed\n", g_tests_run, g_tests_failed);
    return g_tests_failed == 0 ? 0 : 1;
}
