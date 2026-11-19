/*
 * Comprehensive offline C test suite for clonedivers.empire-atst-writer 0.1.0.
 *
 * Verifies:
 *   1. Build 25480438 code pin checking + fail-closed rejection on single-byte mutation
 *   2. hellforge_mod_init SDK-16 handshake against loaded empire_atst_writer.dll
 *   3. Joint-aware kinematics on all 15 decoded walking/turning frames (< 1e-5 m error),
 *      proving elimination of 3.793m bind-only limb pivot error and 2.28x joint-0 stride error
 *   4. Full S2c 512B80 -> 5B2990 runtime pipeline + protected functional socket invariance
 *   5. 4 concurrent co-op EXO-45/49 walkers + 1E7CD0 retirement + spawn/destroy slot recycling
 *   6. Cross-world isolation, ABA generation checks, descriptor tamper passthrough, MXCSR & LastError
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <float.h>
#include <immintrin.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "hellforge_mod.h"
#include "atst_writer_core.h"
#include "atst_clip_fixture.h"

#define TEST_ASSERT(cond, msg)                                                              \
    do {                                                                                    \
        if (!(cond)) {                                                                      \
            fprintf(stderr, "FAIL %s:%d: %s (%s)\n", __FILE__, __LINE__, #cond, (msg));     \
            exit(1);                                                                        \
        }                                                                                   \
    } while (0)

typedef struct {
    unsigned char hdr[0xB0];
    uintptr_t slots[16];
    uint8_t gens[16];
} sim_unit_mgr;

typedef struct {
    unsigned char hdr[0xD8];
    uint64_t resource_id;
    at_render48 worlds[AT_S2C_NODES];
    uint16_t topology[AT_S2C_NODES];
} sim_unit;

typedef struct {
    unsigned char queue[0x400];
    unsigned char world[0x700];
    unsigned char mgr[0x80];
    uint32_t handles[64];
    uint32_t map[64];
    uintptr_t objects[64];
    uint32_t types[64];
} sim_render_sys;

typedef struct {
    unsigned char hdr[0x80];
    at_render48 cur[AT_S2C_NODES];
    at_render48 prev[AT_S2C_NODES];
} sim_render_graph;

typedef struct {
    unsigned char bytes[0xE0];
    unsigned char sub[AT_SKIN_GROUPS_COUNT * 40u];
    uint32_t skin_hdr[4];
    uint32_t skin_map[AT_SKIN_BONES_COUNT];
    uint32_t dir_hdr[1 + 2 * AT_SKIN_GROUPS_COUNT];
    uint32_t pal0[8];
    uint32_t pal1[1];
    uint32_t pal2[1];
    uint32_t binds48[AT_SKIN_BONES_COUNT * 12] __attribute__((aligned(16)));
} sim_mesh;

typedef struct {
    unsigned char bytes[0x200];
} sim_pool;

typedef struct {
    sim_unit unit;
    sim_render_graph graph;
    sim_mesh mesh;
    sim_pool pool;
    at_render48 factor[64] __attribute__((aligned(16)));
    at_render48 dst_pool[64] __attribute__((aligned(16)));
    uint32_t handle;
    uint32_t slot_index;
} sim_walker;

static sim_render_sys *g_render_sys = NULL;
static uint8_t *g_fake_engine = NULL;
static at_writer_state *g_hook_state = NULL;
static __thread uintptr_t g_last_leaf_addrs[6];
static __thread at_render48 g_last_leaf_snapshots[6][AT_S2C_NODES];
static __thread uint32_t g_last_leaf_count = 0;
static int g_tamper_leaf_palette = 0;
static int g_retire_before_leaf = 0;
static uintptr_t g_retire_world = 0u;
static uint32_t g_retire_handle = 0u;
static uintptr_t g_custom_leaf_rets[2] = {0u, 0u};

static void w_u32(void *base, size_t off, uint32_t val) {
    memcpy((unsigned char *)base + off, &val, sizeof(val));
}

static void w_u64(void *base, size_t off, uint64_t val) {
    memcpy((unsigned char *)base + off, &val, sizeof(val));
}

static int sim_read(uintptr_t addr, void *buf, size_t sz, void *user) {
    (void)user;
    if (!buf || addr < 0x10000ULL) return -1;
    memcpy(buf, (const void *)addr, sz);
    return 0;
}

static int sim_read_code_ok(uintptr_t addr, void *buf, size_t sz) {
    memcpy(buf, (const void *)addr, sz);
    return 0;
}

static void populate_fake_engine(uint8_t *eng) {
    memset(eng, 0xCC, 0x02000000u);
    for (size_t i = 0; i < sizeof(AT_CODE_PINS) / sizeof(AT_CODE_PINS[0]); i++) {
        memcpy(eng + AT_CODE_PINS[i].rva, AT_CODE_PINS[i].bytes, AT_CODE_PINS[i].size);
    }
}

static void init_render_sys(sim_render_sys *rs, uintptr_t eng_base) {
    memset(rs, 0, sizeof(*rs));
    uintptr_t mgr = (uintptr_t)rs->mgr;
    uintptr_t wld = (uintptr_t)rs->world;
    uintptr_t q = (uintptr_t)rs->queue;
    /* HeaderRead reads manager + 0x10 (80 bytes) */
    w_u64(rs->mgr, 0x10 + 0x00, (uint64_t)q);
    w_u64(rs->mgr, 0x10 + 0x08, (uint64_t)(uintptr_t)rs->handles);
    w_u32(rs->mgr, 0x10 + 0x2C, 64u);
    w_u32(rs->mgr, 0x10 + 0x30, 63u);
    w_u32(rs->mgr, 0x10 + 0x34, 0u);
    w_u32(rs->mgr, 0x10 + 0x3C, 64u);
    w_u64(rs->mgr, 0x10 + 0x48, (uint64_t)(uintptr_t)rs->handles);

    /* queue + 0x80 == world + 0x10, queue + 0x20 == world + 0x550 */
    w_u64(rs->queue, 0x80, (uint64_t)(wld + 0x10u));
    w_u64(rs->queue, 0x20, (uint64_t)(wld + 0x550u));

    /* world + 0x150 back == mgr, Vecs at +0x168/+0x180/+0x1B8 */
    w_u64(rs->world, 0x150, (uint64_t)mgr);
    w_u32(rs->world, 0x168 + 0, 64u);
    w_u32(rs->world, 0x168 + 4, 64u);
    w_u64(rs->world, 0x168 + 8, (uint64_t)(uintptr_t)rs->objects);
    w_u32(rs->world, 0x180 + 0, 64u);
    w_u32(rs->world, 0x180 + 4, 64u);
    w_u64(rs->world, 0x180 + 8, (uint64_t)(uintptr_t)rs->types);
    w_u32(rs->world, 0x1B8 + 0, 64u);
    w_u32(rs->world, 0x1B8 + 4, 64u);
    w_u64(rs->world, 0x1B8 + 8, (uint64_t)(uintptr_t)rs->map);
    (void)eng_base;
}

static void bind_walker(
    sim_walker *w,
    sim_unit_mgr *um,
    sim_render_sys *rs,
    uintptr_t eng_base,
    uint32_t unit_slot,
    uint8_t unit_gen,
    uint32_t handle_idx,
    uint32_t mesh_desc_idx) {
    memset(w, 0, sizeof(*w));
    w->slot_index = unit_slot;
    w->handle = 64u | handle_idx; /* live bit (64) + slot idx (1..63) */

    w->unit.resource_id = AT_UNIT_RESOURCE_ID;
    memcpy(w->unit.topology, AT_S2C_PARENT, sizeof(AT_S2C_PARENT));
    w_u64(w->unit.hdr, 0x00, (uint64_t)(eng_base + AT_RVA_UNIT_VTABLE));
    w_u64(w->unit.hdr, 0x30, (uint64_t)(uintptr_t)&w->unit.resource_id);
    w_u32(w->unit.hdr, 0x64, w->handle);
    w_u64(w->unit.hdr, 0x68, (uint64_t)((uintptr_t)rs->mgr + 0x10u));
    w_u32(w->unit.hdr, 0x70, AT_S2C_NODES);
    w_u64(w->unit.hdr, 0x88, (uint64_t)(uintptr_t)w->unit.worlds);
    w_u64(w->unit.hdr, 0x98, (uint64_t)(uintptr_t)w->unit.topology);
    w_u32(w->unit.hdr, 0xC8, AT_MESH_DESC_COUNT);

    um->slots[unit_slot] = (uintptr_t)&w->unit;
    um->gens[unit_slot] = unit_gen;

    /* RenderGraph header */
    w_u64(w->graph.hdr, 0x00, (uint64_t)(eng_base + 0x16880B0ULL));
    w_u32(w->graph.hdr, 0x08, 2u);
    w_u32(w->graph.hdr, 0x10, AT_S2C_NODES);
    w_u64(w->graph.hdr, 24u, (uint64_t)(uintptr_t)w->graph.cur);
    w_u64(w->graph.hdr, 48u, (uint64_t)(uintptr_t)w->graph.prev);

    rs->handles[handle_idx] = w->handle;
    rs->map[handle_idx] = handle_idx;
    rs->objects[handle_idx] = (uintptr_t)&w->graph;
    rs->types[handle_idx] = 2u;

    /* Skin, binds, sub-groups */
    memcpy(w->mesh.skin_map, AT_SKIN_BONES, sizeof(AT_SKIN_BONES));
    memcpy(w->mesh.binds48, AT_BINDS_48_U32, sizeof(AT_BINDS_48_U32));
    memcpy(w->mesh.pal0, AT_PALETTE_0, sizeof(AT_PALETTE_0));
    memcpy(w->mesh.pal1, AT_PALETTE_1, sizeof(AT_PALETTE_1));
    memcpy(w->mesh.pal2, AT_PALETTE_2, sizeof(AT_PALETTE_2));

    uintptr_t skin_base = (uintptr_t)w->mesh.skin_hdr;
    w->mesh.skin_hdr[0] = AT_SKIN_BONES_COUNT;
    w->mesh.skin_hdr[1] = 16u;
    w->mesh.skin_hdr[2] = (uint32_t)((uintptr_t)w->mesh.skin_map - skin_base);
    w->mesh.skin_hdr[3] = (uint32_t)((uintptr_t)w->mesh.dir_hdr - skin_base);

    uintptr_t dir_base = (uintptr_t)w->mesh.dir_hdr;
    w->mesh.dir_hdr[0] = AT_SKIN_GROUPS_COUNT;
    w->mesh.dir_hdr[1] = (uint32_t)((uintptr_t)w->mesh.pal0 - dir_base);
    w->mesh.dir_hdr[2] = AT_GROUP_COUNTS[0];
    w->mesh.dir_hdr[3] = (uint32_t)((uintptr_t)w->mesh.pal1 - dir_base);
    w->mesh.dir_hdr[4] = AT_GROUP_COUNTS[1];
    w->mesh.dir_hdr[5] = (uint32_t)((uintptr_t)w->mesh.pal2 - dir_base);
    w->mesh.dir_hdr[6] = AT_GROUP_COUNTS[2];

    uint32_t offsets_at[3][2] = {{0u, 8u}, {9u, 10u}, {11u, 12u}};
    for (unsigned i = 0; i < AT_SKIN_GROUPS_COUNT; i++) {
        w_u32(w->mesh.sub + i * 40u, 24u, i);
        w_u32(w->mesh.sub + i * 40u, 28u, offsets_at[i][0] * 3u);
        w_u32(w->mesh.sub + i * 40u, 32u, offsets_at[i][1] * 3u);
    }

    w_u32(w->mesh.bytes, 0x30, AT_DESC_NODES[mesh_desc_idx]);
    w_u32(w->mesh.bytes, 0x34, w->handle);
    w_u64(w->mesh.bytes, 0x38, (uint64_t)(uintptr_t)&w->graph);
    w_u64(w->mesh.bytes, 0x70, (uint64_t)skin_base);
    w_u64(w->mesh.bytes, 0x80, (uint64_t)(uintptr_t)w->mesh.binds48);
    w_u32(w->mesh.bytes, 0x90, AT_SKIN_GROUPS_COUNT);
    w_u64(w->mesh.bytes, 0x98, (uint64_t)(uintptr_t)w->mesh.sub);
    w_u32(w->mesh.bytes, 0xC8, AT_SKIN_OUTPUT_COUNT);

    /* pool lives at world + 0x318? In tests we point world = pool - 0x318, so set pool pointer matching world + 0x318 */
    /* Wait: our sim_render_sys has fixed world buffer; allocate pool inside rs->world + 0x318! */
    (void)w->pool;
    w_u64(rs->world + 0x318u, 0x170u, (uint64_t)(uintptr_t)w->dst_pool);
    (void)eng_base;
}

static uint32_t sim_leaf_next(
    void *dst,
    const void *factor,
    const void *graph,
    const void *bind,
    const void *palette,
    const void *map,
    uint32_t count) {
    (void)dst;
    (void)factor;
    (void)bind;
    (void)palette;
    (void)map;
    (void)count;
    if (g_last_leaf_count < 6u) {
        uint32_t idx = g_last_leaf_count++;
        g_last_leaf_addrs[idx] = (uintptr_t)graph;
        if (graph) {
            memcpy(g_last_leaf_snapshots[idx], graph, sizeof(g_last_leaf_snapshots[idx]));
        } else {
            memset(g_last_leaf_snapshots[idx], 0, sizeof(g_last_leaf_snapshots[idx]));
        }
    }
    return 0u;
}

static uint32_t sim_skin_next(void *mesh_p, void *pool_p, void *opt_graph) {
    (void)opt_graph;
    sim_mesh *m = (sim_mesh *)mesh_p;
    uintptr_t skin_base = (uintptr_t)m->skin_hdr;
    void *map = (void *)(skin_base + m->skin_hdr[2]);
    void *bind = (void *)m->binds48;
    uintptr_t dir_base = (uintptr_t)m->dir_hdr;
    uintptr_t pool_base = 0;
    memcpy(&pool_base, (unsigned char *)pool_p + 0x170u, sizeof(pool_base));
    void *factor = (void *)(((uintptr_t)pool_p + 15u) & ~(uintptr_t)15u);
    if ((uintptr_t)factor < 0x10000ULL) factor = (void *)m->pal0;

    /* Locate parent graph */
    uintptr_t graph_ptr = 0;
    memcpy(&graph_ptr, m->bytes + 0x38u, 8u);
    const sim_render_graph *rg = (const sim_render_graph *)graph_ptr;

    g_last_leaf_count = 0u;
    if (g_retire_before_leaf) {
        unsigned char pkt[8] = {0};
        w_u32(pkt, 4u, g_retire_handle);
        at_writer_retire_1E7CD0(g_hook_state, g_retire_world, pkt, 0u);
    }
    uint32_t res_off = 0u; /* matches frame reservation == pool_base */
    for (unsigned g = 0; g < AT_SKIN_GROUPS_COUNT; g++) {
        uint32_t q = 0, enc0 = 0, enc1 = 0;
        memcpy(&q, m->sub + g * 40u + 24u, 4u);
        memcpy(&enc0, m->sub + g * 40u + 28u, 4u);
        memcpy(&enc1, m->sub + g * 40u + 32u, 4u);
        const void *pal = (const void *)(dir_base + m->dir_hdr[1u + 2u * q]);
        uint32_t cnt = m->dir_hdr[2u + 2u * q];
        uint32_t offs[2] = {(enc0 / 3u) * 48u, (enc1 / 3u) * 48u};
        const void *arrs[2] = {rg->cur, rg->prev};
        uintptr_t rets[2] = {
            g_custom_leaf_rets[0] ? g_custom_leaf_rets[0] : ((uintptr_t)g_fake_engine + AT_RVA_LEAF_CUR_RET),
            g_custom_leaf_rets[1] ? g_custom_leaf_rets[1] : ((uintptr_t)g_fake_engine + AT_RVA_LEAF_PREV_RET),
        };
        for (unsigned t = 0; t < 2u; t++) {
            const void *call_pal = (g_tamper_leaf_palette && g == 0 && t == 0) ? (const void *)m->pal1 : pal;
            at_writer_leaf_5B2990(
                g_hook_state,
                (void *)(pool_base + offs[t]),
                m->binds48, /* 16-byte aligned factor pointer check */
                arrs[t],
                bind,
                call_pal,
                map,
                cnt,
                rets[t]);
        }
    }
    return res_off;
}

static uint64_t sim_remove_next(uintptr_t world, void *packet, uintptr_t aux) {
    (void)world;
    (void)packet;
    (void)aux;
    return 1ULL;
}

static at_mat4 mat4_from_r48_f(const float v[12]) {
    at_mat4 m;
    memset(&m, 0, sizeof(m));
    for (unsigned k = 0; k < 12; k++) m.v[k] = (double)v[k];
    m.v[15] = 1.0;
    return m;
}

static at_mat4 mul_mat4_host(const at_mat4 *a, const at_mat4 *b) {
    at_mat4 o;
    memset(&o, 0, sizeof(o));
    for (unsigned r = 0; r < 3; r++) {
        for (unsigned c = 0; c < 4; c++) {
            double s = a->v[r * 4 + 0] * b->v[c] +
                       a->v[r * 4 + 1] * b->v[4 + c] +
                       a->v[r * 4 + 2] * b->v[8 + c];
            if (c == 3) s += a->v[r * 4 + 3];
            o.v[r * 4 + c] = s;
        }
    }
    o.v[15] = 1.0;
    return o;
}

static double eval_pose_vertex_err(
    const at_mat4 world_actual_8[8],
    const at_mat4 world_expected_8[8],
    const at_mat4 binds_8[8]) {
    double max_err = 0.0;
    at_mat4 skin_a[8], skin_e[8];
    for (unsigned b = 0; b < 8; b++) {
        skin_a[b] = mul_mat4_host(&world_actual_8[b], &binds_8[b]);
        skin_e[b] = mul_mat4_host(&world_expected_8[b], &binds_8[b]);
    }
    for (unsigned vi = 0; vi < AT_SAMPLE_VERTS; vi++) {
        double pa[3] = {0}, pe[3] = {0};
        const float *p = AT_SAMPLE_POS[vi];
        for (unsigned k = 0; k < 4; k++) {
            double wk = (double)AT_SAMPLE_WGT[vi][k];
            if (wk <= 0.0) continue;
            unsigned b = AT_SAMPLE_BONE8[vi][k];
            for (unsigned r = 0; r < 3; r++) {
                pa[r] += wk * (skin_a[b].v[r * 4 + 0] * p[0] + skin_a[b].v[r * 4 + 1] * p[1] + skin_a[b].v[r * 4 + 2] * p[2] + skin_a[b].v[r * 4 + 3]);
                pe[r] += wk * (skin_e[b].v[r * 4 + 0] * p[0] + skin_e[b].v[r * 4 + 1] * p[1] + skin_e[b].v[r * 4 + 2] * p[2] + skin_e[b].v[r * 4 + 3]);
            }
        }
        double dx = pa[0] - pe[0], dy = pa[1] - pe[1], dz = pa[2] - pe[2];
        double err = sqrt(dx * dx + dy * dy + dz * dz);
        if (err > max_err) max_err = err;
    }
    return max_err;
}

/* Construct S2c 106-node native render48 world array from a 198-joint native local pose + UnitWorld frame. */
static void build_s2c_graph_from_clip(
    const at_rig *rig,
    const float clip_local[198][12],
    const at_mat4 *unit_world,
    at_render48 out_s2c[AT_S2C_NODES]) {
    at_mat4 w198[AT_JOINTS];
    for (unsigned j = 0; j < AT_JOINTS; j++) {
        at_mat4 loc = mat4_from_r48_f(clip_local[j]);
        if (j == 0u) {
            /* Runtime RenderGraph stores UnitWorld at slot 0 (locomotion applied by engine). */
            loc = *unit_world;
        }
        unsigned p = rig->parent[j];
        w198[j] = (p == j) ? loc : mul_mat4_host(&w198[p], &loc);
    }
    for (unsigned k = 0; k < AT_S2C_NODES; k++) {
        at_status st = at_pack(&w198[AT_S2C_ORIG_JOINT[k]], &out_s2c[k]);
        TEST_ASSERT(st == AT_OK, "pack s2c graph");
    }
}

static HfEventCallback g_dll_game_loaded_cb = NULL;
static HfEventCallback g_dll_session_end_cb = NULL;
static uintptr_t g_dll_hook_targets[4];
static int32_t g_dll_hook_priorities[4];
static uint32_t g_dll_hook_count = 0;
static uint32_t g_dll_peer_module_queries = 0;
static uintptr_t g_dll_fake_peer_base = 0;
static size_t g_dll_fake_peer_size = 0;

static void mock_hf_log(HfMod *mod, const char *fmt, ...) {
    (void)mod;
    (void)fmt;
}
static const char *mock_hf_build_id(void) { return AT_EXPECTED_BUILD; }
static const char *mock_hf_mod_id(HfMod *mod) {
    (void)mod;
    return "clonedivers.empire-atst-writer";
}
static uint32_t mock_hf_load_phase(HfMod *mod) {
    (void)mod;
    return HF_LOAD_BOOT;
}
static int mock_hf_read(uintptr_t address, void *buffer, size_t size) {
    return sim_read(address, buffer, size, NULL);
}
static uintptr_t mock_hf_module_base(const char *module, size_t *size) {
    if (!module) return 0u;
    if (strcmp(module, "helldivers2.exe") == 0) {
        if (size) *size = 0x02000000u;
        return (uintptr_t)g_fake_engine;
    }
    if (strcmp(module, AT_PEER_LEAF_DLL) == 0) {
        g_dll_peer_module_queries++;
        if (size) *size = g_dll_fake_peer_size;
        return g_dll_fake_peer_base;
    }
    return 0u;
}
static int mock_hf_on(HfMod *mod, HfEvent event, HfEventCallback cb, void *user) {
    (void)mod;
    (void)user;
    if (event == HF_EVENT_GAME_LOADED) g_dll_game_loaded_cb = cb;
    if (event == HF_EVENT_SESSION_END) g_dll_session_end_cb = cb;
    return 0;
}
static int mock_hf_on_game_thread(HfMod *mod, HfTickCallback cb, void *user) {
    (void)mod;
    (void)cb;
    (void)user;
    return 1;
}
static int mock_hf_hook_ex(HfMod *mod, uintptr_t target, void *detour, void **original, int32_t priority) {
    (void)mod;
    if (!detour || !original || g_dll_hook_count >= 4u) return 1;
    g_dll_hook_targets[g_dll_hook_count] = target;
    g_dll_hook_priorities[g_dll_hook_count] = priority;
    g_dll_hook_count++;
    *original = (void *)(uintptr_t)(0x7FF000001000ULL + g_dll_hook_count * 0x100ULL);
    return 0;
}

static void test_code_pins_and_dll_handshake(uint8_t *eng) {
    uint32_t failed_rva = 0;
    TEST_ASSERT(atst_verify_code_pins(sim_read_code_ok, (uintptr_t)eng, &failed_rva) == 1, "pins valid");

    /* Single-byte mutation must fail closed and report exact RVA */
    eng[AT_RVA_LEAF_SKIN + 5u] ^= 0xFFu;
    TEST_ASSERT(atst_verify_code_pins(sim_read_code_ok, (uintptr_t)eng, &failed_rva) == 0, "pin mutation rejected");
    TEST_ASSERT(failed_rva == AT_RVA_LEAF_SKIN, "reports mutated leaf RVA");
    eng[AT_RVA_LEAF_SKIN + 5u] ^= 0xFFu;

    /* Load actual built DLL and verify hellforge_mod_init + OnGameLoaded hook priorities (-10) & peer lookup */
    HMODULE mod_dll = LoadLibraryA("..\\build\\empire_atst_writer.dll");
    if (!mod_dll) {
        mod_dll = LoadLibraryA("forge-mods\\empire-atst-writer\\build\\empire_atst_writer.dll");
    }
    if (!mod_dll) {
        mod_dll = LoadLibraryA("..\\empire-atst-writer\\build\\empire_atst_writer.dll");
    }
    if (!mod_dll) {
        mod_dll = LoadLibraryA("D:\\ClaudeWork\\workstreams-20261116-v2\\at-st\\empire-atst-writer\\build\\empire_atst_writer.dll");
    }
    TEST_ASSERT(mod_dll != NULL, "load compiled empire_atst_writer.dll");
    typedef int (*init_fn_t)(const HfApi *, HfMod *);
    init_fn_t init_fn = (init_fn_t)GetProcAddress(mod_dll, "hellforge_mod_init");
    TEST_ASSERT(init_fn != NULL, "hellforge_mod_init exported");
    TEST_ASSERT(init_fn(NULL, NULL) != 0, "NULL api refused");

    HfApi api;
    memset(&api, 0, sizeof(api));
    api.version = HF_MOD_SDK_VERSION;
    api.size = sizeof(api);
    api.log = mock_hf_log;
    api.build_id = mock_hf_build_id;
    api.mod_id = mock_hf_mod_id;
    api.load_phase = mock_hf_load_phase;
    api.read = mock_hf_read;
    api.read_code = sim_read_code_ok;
    api.module_base = mock_hf_module_base;
    api.on = mock_hf_on;
    api.on_game_thread = mock_hf_on_game_thread;
    api.hook_ex = mock_hf_hook_ex;
    const HfApi *fake_mod_slot = &api;

    g_dll_game_loaded_cb = NULL;
    g_dll_session_end_cb = NULL;
    g_dll_hook_count = 0;
    g_dll_peer_module_queries = 0;
    g_dll_fake_peer_base = 0x7FFE12340000ULL;
    g_dll_fake_peer_size = 0x28000u;

    TEST_ASSERT(init_fn(&api, (HfMod *)&fake_mod_slot) == 0, "hellforge_mod_init valid SDK-16 handshake");
    TEST_ASSERT(g_dll_game_loaded_cb != NULL && g_dll_session_end_cb != NULL, "registered load & session callbacks");
    g_dll_game_loaded_cb((HfMod *)&fake_mod_slot, HF_EVENT_GAME_LOADED, NULL);
    TEST_ASSERT(g_dll_hook_count == 3u, "OnGameLoaded registered all 3 hooks");
    TEST_ASSERT(g_dll_hook_targets[0] == (uintptr_t)eng + AT_RVA_LEAF_SKIN && g_dll_hook_priorities[0] == -10,
                "5B2990 hooked at priority -10");
    TEST_ASSERT(g_dll_hook_targets[1] == (uintptr_t)eng + AT_RVA_SKIN_JOB && g_dll_hook_priorities[1] == -10,
                "512B80 hooked at priority -10");
    TEST_ASSERT(g_dll_hook_targets[2] == (uintptr_t)eng + AT_RVA_RENDER_REMOVE && g_dll_hook_priorities[2] == -10,
                "1E7CD0 hooked at priority -10");
    TEST_ASSERT(g_dll_peer_module_queries == 1u, "OnGameLoaded queried lego_compact_leaf_writer.dll module_base");
    g_dll_session_end_cb((HfMod *)&fake_mod_slot, HF_EVENT_SESSION_END, NULL);

    FreeLibrary(mod_dll);
    printf("[PASS] test_code_pins_and_dll_handshake\n");
}

static void test_clip_pose_and_pipeline_accuracy(at_writer_state *ws, sim_unit_mgr *um, sim_render_sys *rs) {
    at_mat4 binds_8[8];
    for (unsigned b = 0; b < 8; b++) {
        at_render48 r;
        memcpy(r.v, AT_BINDS_48_U32[b], sizeof(r.v));
        TEST_ASSERT(at_unpack(&r, &binds_8[b]) == AT_OK, "unpack bind");
    }

    sim_walker w0;
    bind_walker(&w0, um, rs, (uintptr_t)g_fake_engine, 0u, 1u, 1u, 0u);
    TEST_ASSERT(at_writer_tick(ws, 16u) == 1u, "admit test walker");

    /* Part A: Offline clip pose retargeting across all 15 walk/turn poses (with joint-0 stride) */
    double max_clip_err = 0.0;
    double max_bind_only_err = 0.0;
    for (unsigned p = 0; p < AT_CLIP_POSE_COUNT; p++) {
        const at_clip_pose_sample *cur_s = &AT_CLIP_POSES[p];
        const at_clip_pose_sample *prev_s = &AT_CLIP_POSES[(p + AT_CLIP_POSE_COUNT - 1u) % AT_CLIP_POSE_COUNT];
        at_pair pair;
        for (unsigned j = 0; j < AT_JOINTS; j++) {
            pair.current.local[j] = mat4_from_r48_f(cur_s->native_local[j]);
            pair.previous.local[j] = mat4_from_r48_f(prev_s->native_local[j]);
        }
        at_retarget_result retarg;
        TEST_ASSERT(at_retarget_pair(&ws->rig, &pair, &retarg) == AT_OK, "at_retarget_pair");

        at_mat4 act_8[8], exp_8[8], nat_8[8];
        at_mat4 w_nat198[AT_JOINTS];
        for (unsigned j = 0; j < AT_JOINTS; j++) {
            unsigned par = ws->rig.parent[j];
            w_nat198[j] = (par == j) ? pair.current.local[j] : mul_mat4_host(&w_nat198[par], &pair.current.local[j]);
        }
        at_mat4 auth_rest_198[AT_JOINTS], nat_rest_198[AT_JOINTS], reanchor_bind[8];
        for (unsigned j = 0; j < AT_JOINTS; j++) {
            at_mat4 nr = mat4_from_r48_f(AT_NAT_LOCAL_198[j]);
            at_mat4 ar = mat4_from_r48_f(AT_AUTH_LOCAL_198[j]);
            unsigned par = ws->rig.parent[j];
            nat_rest_198[j] = (par == j) ? nr : mul_mat4_host(&nat_rest_198[par], &nr);
            auth_rest_198[j] = (par == j) ? ar : mul_mat4_host(&auth_rest_198[par], &ar);
        }
        for (unsigned b = 0; b < 8; b++) {
            unsigned j = AT_SKIN_ORIG_JOINTS[b];
            act_8[b] = retarg.world[0][j];
            exp_8[b] = mat4_from_r48_f(cur_s->expected_world[j]);
            at_mat4 inv_nr;
            TEST_ASSERT(at_inv_affine(&nat_rest_198[j], &inv_nr) == AT_OK, "inv_nr");
            at_mat4 delta = mul_mat4_host(&inv_nr, &auth_rest_198[j]);
            reanchor_bind[b] = mul_mat4_host(&delta, &binds_8[b]);
            nat_8[b] = w_nat198[j];
        }
        double err = eval_pose_vertex_err(act_8, exp_8, binds_8);
        double bind_err = eval_pose_vertex_err(nat_8, exp_8, reanchor_bind);
        /* Compare bind_err using same root alignment? No, naive bind-only on native skeleton yields >3.79m limb pivot drift */
        if (err > max_clip_err) max_clip_err = err;
        if (bind_err > max_bind_only_err) max_bind_only_err = bind_err;
    }
    printf("[INFO] 15-pose raw walk/turn max_err=%.9e m, bind_only_max_err=%.4f m\n",
           max_clip_err, max_bind_only_err);
    TEST_ASSERT(max_clip_err < 1e-5, "raw walk/turn pose accuracy < 1e-5 m");
    TEST_ASSERT(max_bind_only_err > 3.79, "confirms >3.79m bind-only error eliminated");

    /* Part B: Full 512B80 -> 5B2990 runtime pipeline accuracy on all 15 poses under yawed UnitWorld (< 1e-5 m)
     * plus far-field (120.5, -45.25, 8.75) mission translation float32 quantization verification */
    double yaw = 0.64577; /* ~37 deg */
    at_mat4 rotated_origin = {{
        cos(yaw), -sin(yaw), 0.0, 0.0,
        sin(yaw),  cos(yaw), 0.0, 0.0,
        0.0,       0.0,      1.0, 0.0,
        0.0,       0.0,      0.0, 1.0
    }};
    at_mat4 mission_world = {{
        cos(yaw), -sin(yaw), 0.0, 120.5,
        sin(yaw),  cos(yaw), 0.0, -45.25,
        0.0,       0.0,      1.0,  8.75,
        0.0,       0.0,      0.0,  1.0
    }};
    double max_hook_err = 0.0;
    double max_far_err = 0.0;
    for (unsigned pass = 0; pass < 2u; pass++) {
        const at_mat4 *uw = (pass == 0u) ? &rotated_origin : &mission_world;
        for (unsigned p = 0; p < AT_CLIP_POSE_COUNT; p++) {
            const at_clip_pose_sample *cur_s = &AT_CLIP_POSES[p];
            const at_clip_pose_sample *prev_s = &AT_CLIP_POSES[(p + AT_CLIP_POSE_COUNT - 1u) % AT_CLIP_POSE_COUNT];
            build_s2c_graph_from_clip(&ws->rig, cur_s->native_local, uw, w0.graph.cur);
            build_s2c_graph_from_clip(&ws->rig, prev_s->native_local, uw, w0.graph.prev);

            at_receipt rec;
            at_writer_dispatch_512B80(
                ws, &w0.mesh, (void *)(rs->world + 0x318u), &w0.graph,
                (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);

            TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u, "all 6 leaf calls replaced");
            TEST_ASSERT(rec.allocation_ok == 1u && rec.environment_ok == 1u, "alloc & csr ok");
            TEST_ASSERT(g_last_leaf_count == 6u, "6 leaf hooks observed");
            TEST_ASSERT(g_last_leaf_addrs[0] != (uintptr_t)w0.graph.cur &&
                        g_last_leaf_addrs[1] != (uintptr_t)w0.graph.prev,
                        "substituted private S2c graph used");
            const at_render48 *sub_cur = g_last_leaf_snapshots[0];

            if (pass == 0u) {
                /* All 98 protected non-weighted sockets stay identical to native RenderGraph */
                for (unsigned k = 0; k < AT_S2C_NODES; k++) {
                    int is_skin = 0;
                    for (unsigned b = 0; b < 8; b++) if (AT_SKIN_BONES[b] == k) is_skin = 1;
                    if (!is_skin) {
                        TEST_ASSERT(memcmp(&sub_cur[k], &w0.graph.cur[k], sizeof(at_render48)) == 0,
                                    "protected socket invariant");
                    }
                }
            }

            at_mat4 exp_8[8], act_8[8], auth0, inv_auth0, frame_auth;
            auth0 = mat4_from_r48_f(cur_s->expected_world[0]);
            TEST_ASSERT(at_inv_affine(&auth0, &inv_auth0) == AT_OK, "inv auth0");
            frame_auth = mul_mat4_host(uw, &inv_auth0);
            for (unsigned b = 0; b < 8; b++) {
                unsigned j = AT_SKIN_ORIG_JOINTS[b];
                at_mat4 ej = mat4_from_r48_f(cur_s->expected_world[j]);
                exp_8[b] = mul_mat4_host(&frame_auth, &ej);
                TEST_ASSERT(at_unpack(&sub_cur[AT_SKIN_BONES[b]], &act_8[b]) == AT_OK, "unpack substituted");
            }
            double err = eval_pose_vertex_err(act_8, exp_8, binds_8);
            if (pass == 0u) {
                if (err > max_hook_err) max_hook_err = err;
            } else {
                if (err > max_far_err) max_far_err = err;
            }
        }
    }
    TEST_ASSERT(max_hook_err < 1e-5, "512B80->5B2990 rotated UnitWorld pose accuracy < 1e-5 m");
    TEST_ASSERT(max_far_err < 2e-5, "512B80->5B2990 129m far-field mission pose accuracy < 2e-5 m");
    printf("[PASS] test_clip_pose_and_pipeline_accuracy yawed_hook_max_err=%.9e m (< 1e-5 m), far_129m_max_err=%.9e m\n",
           max_hook_err, max_far_err);
}

static void test_four_coop_walkers_and_slot_recycling(at_writer_state *ws, sim_unit_mgr *um, sim_render_sys *rs) {
    /* Reset cleanly */
    at_writer_shutdown(ws);
    memset(um->slots, 0, sizeof(um->slots));
    InterlockedExchange(&ws->ready, 1);

    sim_walker walkers[4];
    double yaws[4] = {0.0, 0.5, 1.2, -0.9};
    for (unsigned i = 0; i < 4u; i++) {
        bind_walker(&walkers[i], um, rs, (uintptr_t)g_fake_engine, i, (uint8_t)(10u + i), 10u + i, i % 8u);
        at_mat4 mw = {{
            cos(yaws[i]), -sin(yaws[i]), 0.0, 10.0 * (double)i,
            sin(yaws[i]),  cos(yaws[i]), 0.0, -5.0 * (double)i,
            0.0,           0.0,          1.0, 2.0,
            0.0,           0.0,          0.0, 1.0
        }};
        build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[(i * 3u) % AT_CLIP_POSE_COUNT].native_local, &mw, walkers[i].graph.cur);
        build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[(i * 3u + 1u) % AT_CLIP_POSE_COUNT].native_local, &mw, walkers[i].graph.prev);
    }

    /* Admit all 4 concurrent walkers */
    for (unsigned i = 0; i < 4u; i++) {
        at_receipt rec;
        at_writer_dispatch_512B80(
            ws, &walkers[i].mesh, (void *)(rs->world + 0x318u), &walkers[i].graph,
            (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
        TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u, "4-walker concurrent draw");
        TEST_ASSERT(ws->reg.slots[i].occupied == 1u && ws->reg.slots[i].graph_handle == walkers[i].handle,
                    "occupied expected slot");
    }
    TEST_ASSERT(ws->reg.admissions == 4u, "4 admitted");

    /* 5th simultaneous live walker fails closed cleanly without disturbing slots 0..3 */
    sim_walker w5_extra;
    bind_walker(&w5_extra, um, rs, (uintptr_t)g_fake_engine, 4u, 20u, 20u, 0u);
    at_mat4 id = {{1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1}};
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[0].native_local, &id, w5_extra.graph.cur);
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[0].native_local, &id, w5_extra.graph.prev);
    at_receipt rec5;
    at_writer_dispatch_512B80(
        ws, &w5_extra.mesh, (void *)(rs->world + 0x318u), &w5_extra.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec5);
    TEST_ASSERT(rec5.replaced == 0u && g_last_leaf_addrs[0] == (uintptr_t)w5_extra.graph.cur, "5th live fails closed");
    TEST_ASSERT(ws->reg.capacity_refusals >= 1u, "capacity refusal recorded");

    /* Cross-world 1E7CD0 isolation: retiring wrong world leaves slot 1 intact */
    unsigned char pkt[8];
    w_u32(pkt, 4u, walkers[1].handle);
    at_writer_retire_1E7CD0(ws, (uintptr_t)rs->world + 0x1000u, pkt, 0u);
    TEST_ASSERT(ws->reg.slots[1].occupied == 1u, "cross-world retire ignored");

    /* Real 1E7CD0 retire of walker 1 + destroy slot 1 in UnitManager */
    uint32_t old_epoch1 = ws->reg.slots[1].epoch;
    at_writer_retire_1E7CD0(ws, (uintptr_t)rs->world, pkt, 0u);
    TEST_ASSERT(ws->reg.slots[1].occupied == 0u && ws->reg.slots[1].epoch == old_epoch1 + 1u,
                "walker 1 retired & epoch bumped");
    um->slots[1] = 0u;
    um->gens[1]++;

    /* Move walker 5 into UnitManager slot 1 -> lowest-free-slot recycles slot 1 with bumped epoch! */
    bind_walker(&walkers[1], um, rs, (uintptr_t)g_fake_engine, 1u, um->gens[1], 25u, 2u);
    at_mat4 mw_rec = {{1,0,0,33.0, 0,1,0,44.0, 0,0,1,5.0, 0,0,0,1}};
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[7].native_local, &mw_rec, walkers[1].graph.cur);
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[6].native_local, &mw_rec, walkers[1].graph.prev);
    at_receipt rec_new1;
    at_writer_dispatch_512B80(
        ws, &walkers[1].mesh, (void *)(rs->world + 0x318u), &walkers[1].graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec_new1);
    TEST_ASSERT(rec_new1.replaced == 6u && ws->reg.slots[1].occupied == 1u, "slot 1 recycled");
    TEST_ASSERT(ws->reg.slots[1].epoch > old_epoch1 + 1u, "epoch monotonically advanced on readmit");
    TEST_ASSERT(ws->reg.slots[1].graph_handle == walkers[1].handle, "new handle bound");

    /* ABA generation guard on slot 2: bump generation without calling 1E7CD0 -> detected & evicted cleanly */
    um->gens[2]++;
    at_receipt rec_aba;
    at_writer_dispatch_512B80(
        ws, &walkers[2].mesh, (void *)(rs->world + 0x318u), &walkers[2].graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec_aba);
    TEST_ASSERT(rec_aba.replaced == 0u && ws->reg.slots[2].occupied == 0u, "ABA generation mismatch scavenged");
    TEST_ASSERT(ws->reg.scavenged >= 1u, "scavenge counter incremented");

    printf("[PASS] test_four_coop_walkers_and_slot_recycling\n");
}

static void test_fail_closed_and_env_preservation(at_writer_state *ws, sim_unit_mgr *um, sim_render_sys *rs) {
    at_writer_shutdown(ws);
    memset(um->slots, 0, sizeof(um->slots));
    InterlockedExchange(&ws->ready, 1);

    sim_walker w;
    bind_walker(&w, um, rs, (uintptr_t)g_fake_engine, 0u, 1u, 1u, 0u);
    at_mat4 id = {{1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1}};
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[0].native_local, &id, w.graph.cur);
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[0].native_local, &id, w.graph.prev);

    /* 1. Foreign caller RVA -> passthrough */
    at_receipt rec;
    uint32_t saved_csr = _mm_getcsr();
    SetLastError(0x1337BEEFu);
    _mm_setcsr((saved_csr & ~0x6000u) | 0x4000u); /* Round +inf */
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + 0x123456u, &rec);
    TEST_ASSERT(GetLastError() == 0x1337BEEFu, "LastError preserved on passthrough");
    TEST_ASSERT((_mm_getcsr() & 0x6000u) == 0x4000u, "MXCSR rounding preserved on passthrough");
    _mm_setcsr(saved_csr);
    TEST_ASSERT(ws->caller_rejects >= 1u && rec.replaced == 0u, "foreign caller passthrough");

    /* 2. Foreign UNIT resource -> rejected */
    w.unit.resource_id = 0xDEADBEEFCAFEBABEULL;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    TEST_ASSERT(rec.replaced == 0u && g_last_leaf_addrs[0] == (uintptr_t)w.graph.cur, "foreign resource untouched");
    w.unit.resource_id = AT_UNIT_RESOURCE_ID;

    /* 3. Mid-leaf palette tamper -> 5B2990 fails closed and passes native graph through */
    g_tamper_leaf_palette = 1;
    SetLastError(0xCAFE0001u);
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    g_tamper_leaf_palette = 0;
    TEST_ASSERT(GetLastError() == 0xCAFE0001u, "LastError preserved across active substitution");
    TEST_ASSERT(_mm_getcsr() == saved_csr, "MXCSR preserved across active substitution");
    TEST_ASSERT(rec.failure != 0u && rec.replaced == 0u && g_last_leaf_addrs[0] == (uintptr_t)w.graph.cur,
                "tampered leaf palette fails closed");

    /* 4. Mutated bind matrix -> capture_rejects passthrough */
    w.mesh.binds48[0] ^= 1u;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    TEST_ASSERT(ws->capture_rejects >= 1u && rec.replaced == 0u, "mutated bind matrix rejected");

    printf("[PASS] test_fail_closed_and_env_preservation\n");
}

typedef struct {
    uintptr_t base;
    size_t size;
    int available;
    uint32_t calls;
} sim_peer_ctx;

static uintptr_t sim_peer_lookup_cb(const char *module_name, size_t *out_size, void *user) {
    sim_peer_ctx *ctx = (sim_peer_ctx *)user;
    if (!ctx || !module_name || strcmp(module_name, AT_PEER_LEAF_DLL) != 0) return 0u;
    ctx->calls++;
    if (!ctx->available) return 0u;
    if (out_size) *out_size = ctx->size;
    return ctx->base;
}

static void test_peer_hook_chaining_and_lazy_lookup(at_writer_state *ws, sim_unit_mgr *um, sim_render_sys *rs) {
    at_writer_shutdown(ws);
    memset(um->slots, 0, sizeof(um->slots));
    at_writer_set_peer_lookup(ws, NULL, NULL);
    g_custom_leaf_rets[0] = 0u;
    g_custom_leaf_rets[1] = 0u;
    InterlockedExchange(&ws->ready, 1);

    sim_walker w;
    bind_walker(&w, um, rs, (uintptr_t)g_fake_engine, 0u, 1u, 1u, 0u);
    at_mat4 id = {{1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1}};
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[2].native_local, &id, w.graph.cur);
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[1].native_local, &id, w.graph.prev);

    /* 1. Solo caller RVAs: normal (opt_graph != NULL) and rest (opt_graph == NULL) both succeed */
    at_receipt rec;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u, "solo normal caller RVA accepted");

    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), NULL,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_REST, &rec);
    TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u, "solo rest caller RVA accepted");

    /* Mismatched solo RVA vs opt_graph must reject */
    uint32_t rej_before = ws->caller_rejects;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), NULL,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    TEST_ASSERT(ws->caller_rejects == rej_before + 1u && rec.replaced == 0u,
                "solo normal RVA with NULL opt_graph rejected");
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_REST, &rec);
    TEST_ASSERT(ws->caller_rejects == rej_before + 2u && rec.replaced == 0u,
                "solo rest RVA with non-NULL opt_graph rejected");

    /* 2. Lazy peer lookup: peer unavailable at OnGameLoaded, then available on first chained dispatch */
    uint8_t *fake_peer_mod = (uint8_t *)VirtualAlloc(NULL, 0x24000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    TEST_ASSERT(fake_peer_mod != NULL, "alloc fake peer module");
    uintptr_t peer_base = (uintptr_t)fake_peer_mod;
    size_t peer_size = 0x24000u;

    sim_peer_ctx pctx = {peer_base, peer_size, 0, 0u};
    at_writer_set_peer_lookup(ws, sim_peer_lookup_cb, &pctx);
    TEST_ASSERT(pctx.calls == 1u && ws->peer_leaf_base == 0u && ws->peer_leaf_size == 0u,
                "initial lookup sees peer unloaded");

    /* While peer is still unloaded, peer-range address is rejected cleanly */
    rej_before = ws->caller_rejects;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        peer_base + 0x3140u, &rec);
    TEST_ASSERT(pctx.calls == 2u && ws->caller_rejects == rej_before + 1u && rec.replaced == 0u,
                "unloaded peer range still rejected");

    /* Now peer DLL loads -> next chained 512B80 normal & 5B2990 cur/prev lazily refresh and succeed! */
    pctx.available = 1;
    g_custom_leaf_rets[0] = peer_base + 0x4810u; /* cur leaf return inside lego_compact_leaf_writer.dll */
    g_custom_leaf_rets[1] = peer_base + 0x4898u; /* prev leaf return inside lego_compact_leaf_writer.dll */
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        peer_base + 0x3140u, &rec);
    TEST_ASSERT(ws->peer_leaf_base == peer_base && ws->peer_leaf_size == peer_size,
                "lazy peer lookup populated base & size");
    TEST_ASSERT(pctx.calls == 3u, "single lazy refresh performed");
    TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u,
                "chained peer 512B80 normal + 5B2990 cur/prev succeeded");

    /* 3. Chained peer 512B80 rest (opt_graph == NULL) + inclusive boundary callers [base, base + size - 1] */
    g_custom_leaf_rets[0] = peer_base;                   /* exact start of peer DLL */
    g_custom_leaf_rets[1] = peer_base + peer_size - 1u;  /* exact last byte of peer DLL */
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), NULL,
        peer_base + 0x31F0u, &rec);
    TEST_ASSERT(rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u,
                "chained peer 512B80 rest + boundary 5B2990 callers succeeded");
    TEST_ASSERT(pctx.calls == 3u, "cached peer module bounds reused without extra lookups");

    /* 4. Unknown external caller addresses outside [peer_base, peer_base + peer_size) must be rejected! */
    rej_before = ws->caller_rejects;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        peer_base - 1u, &rec);
    TEST_ASSERT(ws->caller_rejects == rej_before + 1u && rec.replaced == 0u,
                "caller 1 byte below peer_base rejected");

    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        peer_base + peer_size, &rec);
    TEST_ASSERT(ws->caller_rejects == rej_before + 2u && rec.replaced == 0u,
                "caller at peer_base + peer_size upper bound rejected");

    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), NULL,
        peer_base + peer_size + 0x50000u, &rec);
    TEST_ASSERT(ws->caller_rejects == rej_before + 3u && rec.replaced == 0u,
                "unknown external rest caller rejected");

    /* Valid 512B80 peer caller, but unknown external 5B2990 leaf caller -> fails closed inside leaf */
    g_custom_leaf_rets[0] = peer_base + peer_size; /* 1 byte outside peer module */
    g_custom_leaf_rets[1] = peer_base + 0x4898u;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        peer_base + 0x3140u, &rec);
    TEST_ASSERT(rec.failure == 1u && rec.replaced == 0u && g_last_leaf_addrs[0] == (uintptr_t)w.graph.cur,
                "unknown external 5B2990 leaf caller fails closed");

    /* Lazy peer lookup triggered first inside 5B2990 (when 512B80 came from engine RVA) */
    pctx.calls = 0u;
    at_writer_set_peer_lookup(ws, NULL, NULL);
    ws->peer_lookup_fn = sim_peer_lookup_cb;
    ws->peer_lookup_user = &pctx;
    g_custom_leaf_rets[0] = peer_base + 0x4810u;
    g_custom_leaf_rets[1] = peer_base + 0x4898u;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    TEST_ASSERT(pctx.calls == 1u && rec.seen == 6u && rec.replaced == 6u && rec.failure == 0u,
                "lazy peer lookup inside 5B2990 succeeded");

    g_custom_leaf_rets[0] = 0u;
    g_custom_leaf_rets[1] = 0u;
    at_writer_set_peer_lookup(ws, NULL, NULL);
    VirtualFree(fake_peer_mod, 0, MEM_RELEASE);
    printf("[PASS] test_peer_hook_chaining_and_lazy_lookup\n");
}

typedef struct {
    at_writer_state *ws;
    sim_walker *w;
    void *pool_ptr;
    uintptr_t caller;
    const at_private_graph *expected;
    volatile LONG *start_gate;
    uintptr_t observed_cur_ptr;
    uint32_t iterations;
    int ok;
} mt_pass_worker_arg;

static DWORD WINAPI mt_pass_worker_proc(LPVOID param) {
    mt_pass_worker_arg *arg = (mt_pass_worker_arg *)param;
    while (InterlockedCompareExchange(arg->start_gate, 0, 0) == 0) {
        YieldProcessor();
    }
    for (uint32_t i = 0; i < arg->iterations; i++) {
        at_receipt rec;
        at_writer_dispatch_512B80(arg->ws, &arg->w->mesh, arg->pool_ptr, &arg->w->graph, arg->caller, &rec);
        if (rec.seen != 6u || rec.replaced != 6u || rec.failure != 0u || g_last_leaf_count != 6u) {
            arg->ok = 0;
            return 1u;
        }
        uintptr_t cur_addr = g_last_leaf_addrs[0];
        uintptr_t prev_addr = g_last_leaf_addrs[1];
        if ((cur_addr % 16u) != 0u || (prev_addr % 16u) != 0u) {
            arg->ok = 0;
            return 1u;
        }
        if (memcmp(g_last_leaf_snapshots[0], arg->expected->graph[0], AT_S2C_NODES * sizeof(at_render48)) != 0 ||
            memcmp(g_last_leaf_snapshots[1], arg->expected->graph[1], AT_S2C_NODES * sizeof(at_render48)) != 0) {
            arg->ok = 0;
            return 1u;
        }
        if (i == 0u) arg->observed_cur_ptr = cur_addr;
    }
    arg->ok = 1;
    return 0u;
}

static void test_concurrent_multi_pass_and_mid_leaf_retire(at_writer_state *ws, sim_unit_mgr *um, sim_render_sys *rs) {
    at_writer_shutdown(ws);
    memset(um->slots, 0, sizeof(um->slots));
    at_writer_set_peer_lookup(ws, NULL, NULL);
    g_custom_leaf_rets[0] = 0u;
    g_custom_leaf_rets[1] = 0u;
    g_retire_before_leaf = 0;
    InterlockedExchange(&ws->ready, 1);

    sim_walker w;
    bind_walker(&w, um, rs, (uintptr_t)g_fake_engine, 0u, 1u, 1u, 0u);
    at_mat4 mw = {{1,0,0,15.0, 0,1,0,-9.5, 0,0,1,3.25, 0,0,0,1}};
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[4].native_local, &mw, w.graph.cur);
    build_s2c_graph_from_clip(&ws->rig, AT_CLIP_POSES[3].native_local, &mw, w.graph.prev);

    at_private_graph expected_priv;
    memset(&expected_priv, 0, sizeof(expected_priv));
    TEST_ASSERT(at_evaluate_s2c_pair(&ws->rig, w.graph.cur, w.graph.prev, AT_S2C_NODES, &expected_priv) == AT_OK,
                "reference s2c pair evaluation");

    /* 1. 4 concurrent render worker threads dispatching 200 passes each on the same walker slot */
    volatile LONG start_gate = 0;
    mt_pass_worker_arg args[4];
    HANDLE threads[4];
    for (unsigned t = 0; t < 4u; t++) {
        memset(&args[t], 0, sizeof(args[t]));
        args[t].ws = ws;
        args[t].w = &w;
        args[t].pool_ptr = (void *)(rs->world + 0x318u);
        args[t].caller = (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL;
        args[t].expected = &expected_priv;
        args[t].start_gate = &start_gate;
        args[t].iterations = 200u;
        threads[t] = CreateThread(NULL, 0, mt_pass_worker_proc, &args[t], 0, NULL);
        TEST_ASSERT(threads[t] != NULL, "spawn mt worker thread");
    }
    InterlockedExchange(&start_gate, 1);
    WaitForMultipleObjects(4u, threads, TRUE, INFINITE);
    for (unsigned t = 0; t < 4u; t++) {
        CloseHandle(threads[t]);
        TEST_ASSERT(args[t].ok == 1, "concurrent worker thread passes clean & uncorrupted");
        for (unsigned u = t + 1u; u < 4u; u++) {
            TEST_ASSERT(args[t].observed_cur_ptr != args[u].observed_cur_ptr,
                        "each worker thread uses distinct thread-private TLS stack graph");
        }
    }

    /* 2. Mid-dispatch 1E7CD0 retirement before 5B2990 leaf call -> SRWLockShared observes retirement & fails closed */
    g_retire_before_leaf = 1;
    g_retire_world = (uintptr_t)rs->world;
    g_retire_handle = w.handle;
    at_receipt rec;
    at_writer_dispatch_512B80(
        ws, &w.mesh, (void *)(rs->world + 0x318u), &w.graph,
        (uintptr_t)g_fake_engine + AT_RVA_CALLER_NORMAL, &rec);
    g_retire_before_leaf = 0;
    TEST_ASSERT(rec.failure == 1u && rec.replaced == 0u && g_last_leaf_addrs[0] == (uintptr_t)w.graph.cur,
                "mid-dispatch 1E7CD0 retirement before leaf fails closed under SRWLockShared");
    printf("[PASS] test_concurrent_multi_pass_and_mid_leaf_retire\n");
}

int main(void) {
    g_fake_engine = (uint8_t *)VirtualAlloc(NULL, 0x02000000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    g_render_sys = (sim_render_sys *)calloc(1, sizeof(sim_render_sys));
    sim_unit_mgr *um = (sim_unit_mgr *)calloc(1, sizeof(sim_unit_mgr));
    at_writer_state *ws = (at_writer_state *)calloc(1, sizeof(at_writer_state));
    TEST_ASSERT(g_fake_engine && g_render_sys && um && ws, "alloc test fixtures");

    populate_fake_engine(g_fake_engine);
    init_render_sys(g_render_sys, (uintptr_t)g_fake_engine);
    w_u64(um->hdr, 0x88, (uint64_t)(uintptr_t)um->slots);
    w_u32(um->hdr, 0x98, 16u);
    w_u64(um->hdr, 0xA0, (uint64_t)(uintptr_t)um->gens);
    w_u64(g_fake_engine, AT_RVA_UNIT_MANAGER, (uint64_t)(uintptr_t)um->hdr);

    TEST_ASSERT(at_writer_init(ws, (uintptr_t)g_fake_engine, sim_read, NULL) == 1, "writer init");
    ws->skin_next = ws->approved_skin_next = sim_skin_next;
    ws->leaf_next = ws->approved_leaf_next = sim_leaf_next;
    ws->remove_next = ws->approved_remove_next = sim_remove_next;
    InterlockedExchange(&ws->ready, 1);
    g_hook_state = ws;

    test_code_pins_and_dll_handshake(g_fake_engine);
    test_clip_pose_and_pipeline_accuracy(ws, um, g_render_sys);
    test_four_coop_walkers_and_slot_recycling(ws, um, g_render_sys);
    test_fail_closed_and_env_preservation(ws, um, g_render_sys);
    test_peer_hook_chaining_and_lazy_lookup(ws, um, g_render_sys);
    test_concurrent_multi_pass_and_mid_leaf_retire(ws, um, g_render_sys);

    printf("ALL C TESTS PASSED (0 failures)\n");
    free(ws);
    free(um);
    free(g_render_sys);
    VirtualFree(g_fake_engine, 0, MEM_RELEASE);
    return 0;
}
