/* Tier 0 D11 16-thread, 262,144-slot C stress micro-benchmark for lego-compact-leaf-writer.
 * Runs at BELOW_NORMAL_PRIORITY_CLASS so it does not interfere with active desktop work. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <immintrin.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../src/attachment_observer.c"
#include "../src/envelope.c"

#define BENCH_THREADS 16u
#define BENCH_UNIT_CAP 262144u
#define BENCH_ENEMY_UNITS 104857u /* 40% density */
#define BENCH_FOREIGN_MESHES 1024u
#define BENCH_LAT_SAMPLES 8192u

struct HfMod { const HfApi *api; };
static HfApi bApi;
static struct HfMod bMod;

void gw_test_fatal_cas_pause(gw_state *s) { (void)s; }

typedef struct {
    unsigned char hdr[0xD8];
    uint64_t resource_id;
    unsigned char pad[32];
} bench_enemy_unit;

typedef struct {
    unsigned char bytes[0xE0];
} bench_mesh;

static uint64_t g_qpf = 1;
static volatile LONG64 g_tick_reads = 0;
static volatile LONG64 g_tick_bytes = 0;
static __thread int tls_is_tick = 0;
static uint8_t *g_eng = NULL;
static uint8_t *g_game = NULL;

static void w_u32(void *b, size_t o, uint32_t v) { memcpy((unsigned char *)b + o, &v, 4); }
static void w_u64(void *b, size_t o, uint64_t v) { memcpy((unsigned char *)b + o, &v, 8); }

static uint64_t qpc_now(void) {
    LARGE_INTEGER li;
    QueryPerformanceCounter(&li);
    return (uint64_t)li.QuadPart;
}

static int b_read(uintptr_t addr, void *buf, size_t sz) {
    if (!buf || addr < 0x10000ULL) return -1;
    if (tls_is_tick) {
        g_tick_reads++;
        g_tick_bytes += (LONG64)sz;
    }
    memcpy(buf, (const void *)addr, sz);
    return 0;
}
static int b_read_code(uintptr_t at, void *out, size_t bytes) {
    static const unsigned char leafPrologue[16] = {
        0x48,0x8b,0xc4,0x48,0x89,0x58,0x18,0x48,0x89,0x70,0x20,0x57,0x48,0x83,0xec,0x40
    };
    uintptr_t eng = (uintptr_t)g_eng, gm = (uintptr_t)g_game;
    if (at == eng + 0x5b2990u && bytes == 16u) { memcpy(out, leafPrologue, 16u); return 0; }
    for (unsigned i = 0; i < sizeof(invocationPins)/sizeof(invocationPins[0]); i++)
        if (at == eng + invocationPins[i].rva && bytes == invocationPins[i].size) { memcpy(out, invocationPins[i].bytes, bytes); return 0; }
    for (unsigned i = 0; i < sizeof(pins)/sizeof(pins[0]); i++)
        if (at == eng + pins[i].rva && bytes == pins[i].size) { memcpy(out, pins[i].bytes, bytes); return 0; }
    for (unsigned i = 0; i < sizeof(ownerPins)/sizeof(ownerPins[0]); i++) {
        uintptr_t base = ownerPins[i].game ? gm : eng;
        if (at == base + ownerPins[i].rva && bytes == ownerPins[i].size) { memcpy(out, ownerPins[i].bytes, bytes); return 0; }
    }
    for (unsigned i = 0; i < sizeof(renderPins)/sizeof(renderPins[0]); i++)
        if (at == eng + renderPins[i].rva && bytes == renderPins[i].size) { memcpy(out, renderPins[i].bytes, bytes); return 0; }
    for (unsigned i = 0; i < sizeof(worldPins)/sizeof(worldPins[0]); i++)
        if (at == eng + worldPins[i].rva && bytes == worldPins[i].size) { memcpy(out, worldPins[i].bytes, bytes); return 0; }
    for (unsigned i = 0; i < sizeof(descriptorPins)/sizeof(descriptorPins[0]); i++)
        if (at == eng + descriptorPins[i].rva && bytes == descriptorPins[i].size) { memcpy(out, descriptorPins[i].bytes, bytes); return 0; }
    for (unsigned i = 0; i < sizeof(stablePins)/sizeof(stablePins[0]); i++)
        if (at == eng + stablePins[i].rva && bytes == stablePins[i].size) { memcpy(out, stablePins[i].bytes, bytes); return 0; }
    return b_read(at, out, bytes);
}
static uintptr_t b_mod_base(const char *n, size_t *sz) {
    if (sz) *sz = 0x02000000u;
    if (n && strcmp(n, "helldivers2.exe") == 0) return (uintptr_t)g_eng;
    if (n && strcmp(n, "game.dll") == 0) return (uintptr_t)g_game;
    return 0;
}
static const char *b_build(void) { return "25480438"; }
static const char *b_mod_id(HfMod *m) { (void)m; return "clonedivers.lego-compact-leaf-writer"; }
static const char *b_logs_dir(HfMod *m) { (void)m; return "D:\\ClaudeWork\\cache\\tmp\\lego-tier0-logs"; }
static const char *b_mod_folder(HfMod *m) { (void)m; return ""; }
static uint32_t b_load_phase(HfMod *m) { (void)m; return HF_LOAD_BOOT; }
static void b_log(HfMod *m, const char *fmt, ...) {
    (void)m;
    if (!__atomic_load_n(&writer.ready, __ATOMIC_ACQUIRE)) {
        va_list ap; va_start(ap, fmt);
        vfprintf(stderr, fmt, ap); fputc('\n', stderr);
        va_end(ap);
    }
}
static int b_on(HfMod *m, HfEvent e, HfEventCallback cb, void *u) { (void)m; (void)e; (void)cb; (void)u; return 0; }
static int b_on_gt(HfMod *m, HfTickCallback cb, void *u) { (void)m; (void)cb; (void)u; return 1; }
static uint32_t noop_outer(void *m, void *p, void *g) { (void)m; (void)p; (void)g; return 0u; }
static uint32_t noop_leaf(void *d, const void *f, const void *g, const void *b, const uint32_t *p, const uint32_t *m, uint32_t c) {
    (void)d; (void)f; (void)g; (void)b; (void)p; (void)m; (void)c; return 0u;
}
static uint32_t noop_remove(void *w, const void *p, void *a) { (void)w; (void)p; (void)a; return 0u; }
static int b_hook_ex(HfMod *m, uintptr_t target, void *detour, void **orig, int prio) {
    (void)m; (void)detour; (void)prio;
    if (target == (uintptr_t)g_eng + 0x512B80u) *orig = (void *)noop_outer;
    else if (target == (uintptr_t)g_eng + 0x5B2990u) *orig = (void *)noop_leaf;
    else if (target == (uintptr_t)g_eng + 0x1E7CD0u) *orig = (void *)noop_remove;
    else return -1;
    return 0;
}

static int cmp_double(const void *a, const void *b) {
    double da = *(const double *)a, db = *(const double *)b;
    return (da > db) - (da < db);
}

typedef struct {
    bench_mesh *foreign;
    void *pool;
    uintptr_t world;
    uintptr_t caller;
    volatile LONG *go;
    volatile LONG *stop;
    uint32_t tid;
    uint64_t calls;
    double samples_ns[BENCH_LAT_SAMPLES];
    uint32_t sample_count;
} lego_worker_ctx;

static DWORD WINAPI lego_worker_proc(LPVOID p) {
    lego_worker_ctx *c = (lego_worker_ctx *)p;
    unsigned char pkt[8] = {0};
    w_u32(pkt, 4u, 0x22000u + c->tid);
    while (!__atomic_load_n(c->go, __ATOMIC_ACQUIRE)) YieldProcessor();

    uint64_t calls = 0;
    uint32_t sc = 0;
    uint32_t idx = c->tid * 37u;
    while (!__atomic_load_n(c->stop, __ATOMIC_ACQUIRE)) {
        uint64_t t0 = 0;
        int measure = ((calls & 255u) == 0u) && (sc < BENCH_LAT_SAMPLES);
        if (measure) t0 = qpc_now();
        for (unsigned k = 0; k < 16u; k++) {
            bench_mesh *m = &c->foreign[(idx++) & (BENCH_FOREIGN_MESHES - 1u)];
            gw_dispatch(&writer, m, c->pool, NULL, c->caller);
            gw_leaf_dispatch(&writer, NULL, NULL, NULL, NULL, NULL, NULL, 0u, (uintptr_t)g_eng + 0x512FEAu);
            if ((k & 3u) == 0u) {
                gw_retire_handle(&writer, c->world, 0x22000u + (k & 15u));
            }
        }
        if (measure) {
            uint64_t dt = qpc_now() - t0;
            c->samples_ns[sc++] = ((double)dt * 1e9 / (double)g_qpf) / 16.0;
        }
        calls += 16u;
    }
    c->calls = calls;
    c->sample_count = sc;
    return 0;
}

static int run_lego_case(const char *label, bench_mesh *foreign, void *pool, uintptr_t world,
                         uintptr_t *slots, uint8_t *gens, bench_enemy_unit *enemies, int churn) {
    volatile LONG go = 0, stop = 0;
    lego_worker_ctx ctx[BENCH_THREADS];
    HANDLE th[BENCH_THREADS];
    memset(ctx, 0, sizeof(ctx));
    uintptr_t caller = (uintptr_t)g_eng + 0x258E19u;
    for (unsigned i = 0; i < BENCH_THREADS; i++) {
        ctx[i].foreign = foreign; ctx[i].pool = pool; ctx[i].world = world;
        ctx[i].caller = caller; ctx[i].go = &go; ctx[i].stop = &stop; ctx[i].tid = i;
        th[i] = CreateThread(NULL, 0, lego_worker_proc, &ctx[i], 0, NULL);
        SetThreadPriority(th[i], THREAD_PRIORITY_BELOW_NORMAL);
    }

    double tick_us[128];
    uint32_t tick_n = 0;
    g_tick_reads = 0; g_tick_bytes = 0;
    tls_is_tick = 1;

    uint64_t t_start = qpc_now();
    __atomic_store_n(&go, 1, __ATOMIC_RELEASE);

    /* Run 128 poll ticks (~8 full 262k sweeps at 16384/tick) so p95/p99 are real percentiles */
    for (unsigned k = 0; k < 128u; k++) {
        if (churn) {
            /* Mutate 2,000 enemy slots per tick (outside player garment slots 60000..60027) */
            uint32_t base = (k * 2000u) % BENCH_UNIT_CAP;
            for (uint32_t j = 0; j < 2000u; j++) {
                uint32_t sl = (base + j) % BENCH_UNIT_CAP;
                if (sl >= 60000u && sl < 60028u) continue;
                gens[sl]++;
                slots[sl] = (j & 1u) ? (uintptr_t)enemies[(k * 1000u + j) % BENCH_ENEMY_UNITS].hdr : 0u;
            }
        }
        writerPollAt = 0;
        uint64_t tk0 = qpc_now();
        WriterTick(&bMod, 0.016f, NULL);
        uint64_t tk1 = qpc_now();
        tick_us[tick_n++] = (double)(tk1 - tk0) * 1e6 / (double)g_qpf;
        Sleep(12);
    }

    __atomic_store_n(&stop, 1, __ATOMIC_RELEASE);
    uint64_t t_end = qpc_now();
    tls_is_tick = 0;
    WaitForMultipleObjects(BENCH_THREADS, th, TRUE, INFINITE);
    for (unsigned i = 0; i < BENCH_THREADS; i++) CloseHandle(th[i]);

    double elapsed_s = (double)(t_end - t_start) / (double)g_qpf;
    uint64_t total_calls = 0;
    double *all_ns = (double *)malloc(sizeof(double) * BENCH_THREADS * BENCH_LAT_SAMPLES);
    uint32_t all_n = 0;
    for (unsigned i = 0; i < BENCH_THREADS; i++) {
        total_calls += ctx[i].calls;
        for (uint32_t j = 0; j < ctx[i].sample_count; j++) all_ns[all_n++] = ctx[i].samples_ns[j];
    }
    qsort(all_ns, all_n, sizeof(double), cmp_double);
    qsort(tick_us, tick_n, sizeof(double), cmp_double);

    double ns_p50 = all_n ? all_ns[all_n / 2] : 0.0;
    double ns_p99 = all_n ? all_ns[(size_t)(all_n * 0.99)] : 0.0;
    double ns_max = all_n ? all_ns[all_n - 1] : 0.0;
    double tk_p50 = tick_us[tick_n / 2];
    double tk_p99 = tick_us[(size_t)(tick_n * 0.99)];
    double tk_max = tick_us[tick_n - 1];
    double mcalls = ((double)total_calls / elapsed_s) / 1e6;
    double reads_per_tick = (double)g_tick_reads / (double)tick_n;
    double kb_per_tick = ((double)g_tick_bytes / (double)tick_n) / 1024.0;
    int pass = (ns_p99 <= 150.0) && (tk_p99 <= 2000.0) && (writer.fatal == 0) && (writer.failed == 0);
    printf("LEGO %-26s | fast_hc=%u | 16T=%.2f Mcalls/s | rej_ns p50=%.1f p99=%.1f max=%.1f | tick_us p50=%.1f p99=%.1f max=%.1f | reads/tk=%.0f (%.1f KB) | fatal=%ld | %s\n",
           label, coopFast.handle_count, mcalls, ns_p50, ns_p99, ns_max, tk_p50, tk_p99, tk_max, reads_per_tick, kb_per_tick,
           (long)writer.fatal, pass ? "PASS" : "FAIL");
    free(all_ns);
    return pass;
}

int main(void) {
    SetPriorityClass(GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS);
    CreateDirectoryA("D:\\ClaudeWork\\cache\\tmp\\lego-tier0-logs", NULL);
    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);
    g_qpf = (uint64_t)freq.QuadPart;

    g_eng = (uint8_t *)VirtualAlloc(NULL, 0x02000000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    g_game = (uint8_t *)VirtualAlloc(NULL, 0x03000000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    unsigned char *mgrHdr = (unsigned char *)calloc(1, 0xC0);
    uintptr_t *slots = (uintptr_t *)calloc(BENCH_UNIT_CAP, sizeof(uintptr_t));
    uint8_t *gens = (uint8_t *)calloc(BENCH_UNIT_CAP, sizeof(uint8_t));
    bench_enemy_unit *enemies = (bench_enemy_unit *)calloc(BENCH_ENEMY_UNITS, sizeof(*enemies));
    bench_mesh *foreign = (bench_mesh *)calloc(BENCH_FOREIGN_MESHES, sizeof(*foreign));
    unsigned char *fakeWorld = (unsigned char *)calloc(1, 0x800);

    w_u64(mgrHdr, 0x88u, (uintptr_t)slots);
    w_u32(mgrHdr, 0x98u, BENCH_UNIT_CAP);
    w_u64(mgrHdr, 0xA0u, (uintptr_t)gens);
    w_u64(g_eng, MANAGER_RVA, (uintptr_t)mgrHdr);

    /* Populate 40% of 262,144 slots with realistic enemy units */
    uint32_t stride = BENCH_UNIT_CAP / BENCH_ENEMY_UNITS;
    for (uint32_t i = 0; i < BENCH_ENEMY_UNITS; i++) {
        uint32_t sl = (i * stride + (i & 1u)) % BENCH_UNIT_CAP;
        enemies[i].resource_id = 0x5555666677770000ULL + i;
        w_u64(enemies[i].hdr, 0x00u, (uintptr_t)g_eng + VTABLE_RVA);
        w_u64(enemies[i].hdr, 0x30u, (uintptr_t)&enemies[i].resource_id);
        slots[sl] = (uintptr_t)enemies[i].hdr;
        gens[sl] = 1u;
    }
    for (uint32_t i = 0; i < BENCH_FOREIGN_MESHES; i++) {
        w_u32(foreign[i].bytes, 0x34u, 0x9000u + i);
    }

    memset(&bApi, 0, sizeof(bApi));
    bApi.version = HF_MOD_SDK_VERSION; bApi.size = sizeof(bApi);
    bApi.build_id = b_build; bApi.mod_id = b_mod_id; bApi.load_phase = b_load_phase;
    bApi.logs_folder = b_logs_dir; bApi.mod_folder = b_mod_folder;
    bApi.read = b_read; bApi.read_code = b_read_code; bApi.module_base = b_mod_base;
    bApi.log = b_log; bApi.on = b_on; bApi.on_game_thread = b_on_gt; bApi.hook_ex = b_hook_ex;
    bMod.api = &bApi;

    /* Populate vtable & string expectations checked by Loaded() */
    static const uint32_t vslots[][2] = {{0xE8, 0x2BD870}, {0xF8, 0x2BD890}, {0x1A8, 0x2BD9A0}, {0x1B0, 0x2BD9C0}};
    for (unsigned i = 0; i < 4u; i++) w_u64(g_eng, VTABLE_RVA + vslots[i][0], (uintptr_t)g_eng + vslots[i][1]);
    static const uint32_t renderV[][5] = {
        {0x16880B0, 0x4F2490, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x1688950, 0x510FA0, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x167A478, 0x354380, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x1672E70, 0x1E4CF0, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x168B5A0, 0x5C2D20, 0x1CC6C0, 0x1CC6B0, 0x5C2DF0}
    };
    for (unsigned i = 0; i < 5u; i++)
        for (unsigned j = 0; j < 4u; j++)
            w_u64(g_eng, renderV[i][0] + j * 8u, (uintptr_t)g_eng + renderV[i][j + 1u]);
    memcpy(g_eng + 0x16729A8u, "RenderWorld", sizeof("RenderWorld"));
    memcpy(g_eng + 0x1689440u, "state_reflection_stream_rps", sizeof("state_reflection_stream_rps"));

    /* Populate minimal game globals so CoopManagerRead + OwnerRead succeed cleanly */
    unsigned char *gameGlobals = (unsigned char *)calloc(1, 0x100);
    unsigned char *playerRow = (unsigned char *)calloc(1, 0x200);
    unsigned char *ctxSlab = (unsigned char *)calloc(1, 0xC000);
    unsigned char *entitiesSlab = (unsigned char *)calloc(1, 0x100);
    w_u64(g_game, 0x3326340u, (uintptr_t)gameGlobals);
    w_u64(g_game, 0x3326468u, (uintptr_t)playerRow);
    w_u64(g_game, 0x347CEF0u, (uintptr_t)ctxSlab);
    w_u64(g_game, 0x346BF98u, (uintptr_t)entitiesSlab);

    hellforge_mod_init(&bApi, &bMod);
    Loaded(&bMod, HF_EVENT_GAME_LOADED, NULL);
    WriterTick(&bMod, 0.016f, NULL); /* becomes ready */
    if (!__atomic_load_n(&writer.ready, __ATOMIC_ACQUIRE)) {
        fprintf(stderr, "FATAL: writer did not become ready! fatal=%ld\n", (long)writer.fatal);
        return 2;
    }

    void *pool = (void *)(fakeWorld + 0x318u);
    uintptr_t world = (uintptr_t)fakeWorld;
    int ok1 = run_lego_case("CaseA_0Garments_262k", foreign, pool, world, slots, gens, enemies, 0);

    /* Case B: 28 real compact garment units at slots 60000..60027 -> coopFast.handle_count stays == 28! */
    static const uint64_t compact_ids[7] = {
        0x2BB094EDBC756963ULL, 0x7305FDF48136C05CULL, 0xDD25333BDF5F0143ULL,
        0x1E25DAEB99B1A917ULL, 0x512399E005614901ULL, 0x4D2F00B6C4929F00ULL,
        0xBB11C8041C14C747ULL
    };
    static const uint32_t compact_nodes[7] = {21, 21, 43, 21, 43, 17, 29};
    bench_enemy_unit *garments = (bench_enemy_unit *)calloc(28u, sizeof(*garments));
    for (uint32_t i = 0; i < 28u; i++) {
        uint32_t sl = 60000u + i;
        garments[i].resource_id = compact_ids[i % 7u];
        w_u64(garments[i].hdr, 0x00u, (uintptr_t)g_eng + VTABLE_RVA);
        w_u64(garments[i].hdr, 0x30u, (uintptr_t)&garments[i].resource_id);
        w_u32(garments[i].hdr, 0x64u, 0x100u + i);
        w_u64(garments[i].hdr, 0x68u, world + 0x10u);
        w_u32(garments[i].hdr, 0x70u, compact_nodes[i % 7u]);
        slots[sl] = (uintptr_t)garments[i].hdr;
        gens[sl] = 1u;
    }
    AcquireSRWLockExclusive(&writer.metadata);
    coopGarmentCount = 28u;
    for (uint32_t i = 0; i < 28u; i++) {
        coopGarments[i].index = 60000u + i;
        coopGarments[i].generation = 1u;
        coopGarments[i].app_graph_handle = 0x100u + i;
        coopGarments[i].target_kind = i % 7u;
        coopGarments[i].unit = (uintptr_t)garments[i].hdr;
        coopGarments[i].association = world + 0x10u;
        coopGarments[i].resource = compact_ids[i % 7u];
    }
    coop_fast_publish();
    ReleaseSRWLockExclusive(&writer.metadata);
    int ok2 = run_lego_case("CaseB_28Garments_262k", foreign, pool, world, slots, gens, enemies, 0);
    int ok3 = run_lego_case("CaseC_28Garments_Churn", foreign, pool, world, slots, gens, enemies, 1);

    printf("LEGO_TIER0_SUMMARY: %s\n", (ok1 && ok2 && ok3) ? "ALL_PASS" : "GATE_FAIL");
    free(garments); free(entitiesSlab); free(ctxSlab); free(playerRow); free(gameGlobals);
    free(fakeWorld); free(foreign); free(enemies);
    free(slots); free(gens); free(mgrHdr);
    VirtualFree(g_game, 0, MEM_RELEASE);
    VirtualFree(g_eng, 0, MEM_RELEASE);
    return (ok1 && ok2 && ok3) ? 0 : 1;
}
