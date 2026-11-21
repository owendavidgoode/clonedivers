/* Tier 0 D11 16-thread, 262,144-slot C stress micro-benchmark for empire-atst-writer.
 * Runs at BELOW_NORMAL_PRIORITY_CLASS so it does not interfere with active desktop work. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <immintrin.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "atst_writer_core.h"

#define BENCH_THREADS 16u
#define BENCH_UNIT_CAP 262144u
#define BENCH_ENEMY_UNITS 104857u /* 40% density matching D11 bug breach / heavy SEAF */
#define BENCH_FOREIGN_MESHES 1024u
#define BENCH_LAT_SAMPLES 8192u

typedef struct {
    unsigned char hdr[0xD8];
    uint64_t resource_id;
    unsigned char pad[32]; /* 256B stride to model realistic cache footprint */
} bench_enemy_unit;

typedef struct {
    unsigned char hdr[0xB0];
    uintptr_t *slots;
    uint8_t *gens;
} bench_unit_mgr;

typedef struct {
    unsigned char queue[0x400];
    unsigned char world[0x700];
    unsigned char mgr[0x80];
    uint32_t handles[64];
    uint32_t map[64];
    uintptr_t objects[64];
    uint32_t types[64];
} bench_render_sys;

typedef struct {
    unsigned char bytes[0xE0];
    unsigned char sub[AT_SKIN_GROUPS_COUNT * 40u];
    uint32_t skin_hdr[4];
    uint32_t skin_map[AT_SKIN_BONES_COUNT];
    uint32_t dir_hdr[1 + 2 * AT_SKIN_GROUPS_COUNT];
    uint32_t pal0[8], pal1[1], pal2[1];
    uint32_t binds48[AT_SKIN_BONES_COUNT * 12] __attribute__((aligned(16)));
} bench_mesh;

typedef struct {
    unsigned char hdr[0x80];
    at_render48 cur[AT_S2C_NODES];
    at_render48 prev[AT_S2C_NODES];
} bench_graph;

typedef struct {
    unsigned char hdr[0xD8];
    uint64_t resource_id;
    at_render48 worlds[AT_S2C_NODES];
    uint16_t topology[AT_S2C_NODES];
    bench_graph graph;
    bench_mesh mesh;
    uint32_t handle;
    uint32_t unit_slot;
} bench_walker;

static uint64_t g_qpf = 1;
static volatile LONG64 g_tick_reads = 0;
static volatile LONG64 g_tick_bytes = 0;
static __thread int tls_is_tick = 0;

static void w_u32(void *b, size_t o, uint32_t v) { memcpy((unsigned char *)b + o, &v, 4); }
static void w_u64(void *b, size_t o, uint64_t v) { memcpy((unsigned char *)b + o, &v, 8); }

static uint64_t qpc_now(void) {
    LARGE_INTEGER li;
    QueryPerformanceCounter(&li);
    return (uint64_t)li.QuadPart;
}

static int bench_read(uintptr_t addr, void *buf, size_t sz, void *user) {
    (void)user;
    if (!buf || addr < 0x10000ULL) return -1;
    if (tls_is_tick) {
        g_tick_reads++;
        g_tick_bytes += (LONG64)sz;
    }
    memcpy(buf, (const void *)addr, sz);
    return 0;
}

static uint32_t noop_skin_next(void *m, void *p, void *g) {
    (void)m; (void)p; (void)g;
    return 0u;
}
static uint32_t noop_leaf_next(void *d, const void *f, const void *g, const void *b, const void *p, const void *m, uint32_t c) {
    (void)d; (void)f; (void)g; (void)b; (void)p; (void)m; (void)c;
    return 0u;
}
static uint64_t noop_remove_next(uintptr_t w, void *p, uintptr_t a) {
    (void)w; (void)p; (void)a;
    return 0ULL;
}

static int cmp_double(const void *a, const void *b) {
    double da = *(const double *)a, db = *(const double *)b;
    return (da > db) - (da < db);
}

typedef struct {
    at_writer_state *ws;
    bench_render_sys *rs;
    bench_mesh *foreign_meshes;
    uintptr_t caller;
    volatile LONG *go;
    volatile LONG *stop;
    uint32_t tid;
    uint64_t calls;
    double samples_ns[BENCH_LAT_SAMPLES];
    uint32_t sample_count;
} worker_ctx;

static DWORD WINAPI worker_proc(LPVOID p) {
    worker_ctx *c = (worker_ctx *)p;
    void *pool = (void *)(c->rs->world + 0x318u);
    unsigned char pkt[8] = {0};
    w_u32(pkt, 4u, 0x12345u + c->tid);
    while (!__atomic_load_n(c->go, __ATOMIC_ACQUIRE)) YieldProcessor();

    uint64_t calls = 0;
    uint32_t sc = 0;
    uint32_t idx = c->tid * 37u;
    while (!__atomic_load_n(c->stop, __ATOMIC_ACQUIRE)) {
        uint64_t t0 = 0;
        int measure = ((calls & 255u) == 0u) && (sc < BENCH_LAT_SAMPLES);
        if (measure) t0 = qpc_now();
        for (unsigned k = 0; k < 16u; k++) {
            bench_mesh *m = &c->foreign_meshes[(idx++) & (BENCH_FOREIGN_MESHES - 1u)];
            at_writer_dispatch_512B80(c->ws, m, pool, NULL, c->caller, NULL);
            if ((k & 3u) == 0u) {
                at_writer_retire_1E7CD0(c->ws, (uintptr_t)c->rs->world, pkt, 0u);
            }
        }
        if (measure) {
            uint64_t dt = qpc_now() - t0;
            /* 16 calls of 512B80 + 4 retire calls -> divide by 16 for per-512B80 amortized ns */
            c->samples_ns[sc++] = ((double)dt * 1e9 / (double)g_qpf) / 16.0;
        }
        calls += 16u;
    }
    c->calls = calls;
    c->sample_count = sc;
    return 0;
}

static void init_render_sys(bench_render_sys *rs) {
    memset(rs, 0, sizeof(*rs));
    uintptr_t mgr = (uintptr_t)rs->mgr, wld = (uintptr_t)rs->world, q = (uintptr_t)rs->queue;
    w_u64(rs->mgr, 0x10 + 0x00, q);
    w_u64(rs->mgr, 0x10 + 0x08, (uintptr_t)rs->handles);
    w_u32(rs->mgr, 0x10 + 0x2C, 64u);
    w_u32(rs->mgr, 0x10 + 0x30, 63u);
    w_u32(rs->mgr, 0x10 + 0x3C, 64u);
    w_u64(rs->mgr, 0x10 + 0x48, (uintptr_t)rs->handles);
    w_u64(rs->queue, 0x80, wld + 0x10u);
    w_u64(rs->queue, 0x20, wld + 0x550u);
    w_u64(rs->world, 0x150, mgr);
    w_u32(rs->world, 0x168, 64u); w_u32(rs->world, 0x16C, 64u); w_u64(rs->world, 0x170, (uintptr_t)rs->objects);
    w_u32(rs->world, 0x180, 64u); w_u32(rs->world, 0x184, 64u); w_u64(rs->world, 0x188, (uintptr_t)rs->types);
    w_u32(rs->world, 0x1B8, 64u); w_u32(rs->world, 0x1BC, 64u); w_u64(rs->world, 0x1C0, (uintptr_t)rs->map);
}

static void bind_walker(bench_walker *w, bench_unit_mgr *um, bench_render_sys *rs, uintptr_t eng, uint32_t slot, uint32_t hidx) {
    memset(w, 0, sizeof(*w));
    w->unit_slot = slot;
    w->handle = 64u | hidx;
    w->resource_id = AT_UNIT_RESOURCE_ID;
    memcpy(w->topology, AT_S2C_PARENT, sizeof(AT_S2C_PARENT));
    w_u64(w->hdr, 0x00, eng + AT_RVA_UNIT_VTABLE);
    w_u64(w->hdr, 0x30, (uintptr_t)&w->resource_id);
    w_u32(w->hdr, 0x64, w->handle);
    w_u64(w->hdr, 0x68, (uintptr_t)rs->mgr + 0x10u);
    w_u32(w->hdr, 0x70, AT_S2C_NODES);
    w_u64(w->hdr, 0x88, (uintptr_t)w->worlds);
    w_u64(w->hdr, 0x98, (uintptr_t)w->topology);
    w_u32(w->hdr, 0xC8, AT_MESH_DESC_COUNT);
    um->slots[slot] = (uintptr_t)w->hdr;
    um->gens[slot] = 1u;

    w_u64(w->graph.hdr, 0x00, eng + 0x16880B0ULL);
    w_u32(w->graph.hdr, 0x08, 2u);
    w_u32(w->graph.hdr, 0x10, AT_S2C_NODES);
    w_u64(w->graph.hdr, 24u, (uintptr_t)w->graph.cur);
    w_u64(w->graph.hdr, 48u, (uintptr_t)w->graph.prev);

    rs->handles[hidx] = w->handle;
    rs->map[hidx] = hidx;
    rs->types[hidx] = 2u;
    rs->objects[hidx] = (uintptr_t)w->graph.hdr;
}

static int run_case(const char *label, at_writer_state *ws, bench_render_sys *rs, bench_mesh *foreign, uintptr_t caller, uint32_t scan_slice) {
    volatile LONG go = 0, stop = 0;
    worker_ctx ctx[BENCH_THREADS];
    HANDLE th[BENCH_THREADS];
    memset(ctx, 0, sizeof(ctx));
    for (unsigned i = 0; i < BENCH_THREADS; i++) {
        ctx[i].ws = ws; ctx[i].rs = rs; ctx[i].foreign_meshes = foreign;
        ctx[i].caller = caller; ctx[i].go = &go; ctx[i].stop = &stop; ctx[i].tid = i;
        th[i] = CreateThread(NULL, 0, worker_proc, &ctx[i], 0, NULL);
        SetThreadPriority(th[i], THREAD_PRIORITY_BELOW_NORMAL);
    }

    double tick_us[256];
    uint32_t tick_n = 0;
    g_tick_reads = 0; g_tick_bytes = 0;
    tls_is_tick = 1;

    uint64_t t_start = qpc_now();
    __atomic_store_n(&go, 1, __ATOMIC_RELEASE);

    /* Run 128 ticks (~2 full D11 sweeps at 2048/tick) spread over ~2 seconds */
    for (unsigned k = 0; k < 128u; k++) {
        uint64_t tk0 = qpc_now();
        at_writer_tick(ws, scan_slice);
        uint64_t tk1 = qpc_now();
        tick_us[tick_n++] = (double)(tk1 - tk0) * 1e6 / (double)g_qpf;
        Sleep(10);
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

    int pass = (ns_p99 <= 150.0) && (tk_p99 <= 2000.0);
    printf("ATST %-26s | fast_cnt=%u | 16T=%.2f Mcalls/s | rej_ns p50=%.1f p99=%.1f max=%.1f | tick_us p50=%.1f p99=%.1f max=%.1f | reads/tk=%.0f (%.1f KB) | %s\n",
           label, ws->fast.count, mcalls, ns_p50, ns_p99, ns_max, tk_p50, tk_p99, tk_max, reads_per_tick, kb_per_tick,
           pass ? "PASS" : "FAIL");
    free(all_ns);
    return pass;
}

int main(void) {
    SetPriorityClass(GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS);
    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);
    g_qpf = (uint64_t)freq.QuadPart;

    uint8_t *eng = (uint8_t *)VirtualAlloc(NULL, 0x02000000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    bench_render_sys *rs = (bench_render_sys *)calloc(1, sizeof(*rs));
    bench_unit_mgr *um = (bench_unit_mgr *)calloc(1, sizeof(*um));
    um->slots = (uintptr_t *)calloc(BENCH_UNIT_CAP, sizeof(uintptr_t));
    um->gens = (uint8_t *)calloc(BENCH_UNIT_CAP, sizeof(uint8_t));
    bench_enemy_unit *enemies = (bench_enemy_unit *)calloc(BENCH_ENEMY_UNITS, sizeof(*enemies));
    bench_mesh *foreign = (bench_mesh *)calloc(BENCH_FOREIGN_MESHES, sizeof(*foreign));
    at_writer_state *ws = (at_writer_state *)calloc(1, sizeof(*ws));

    init_render_sys(rs);
    w_u64(um->hdr, 0x88, (uintptr_t)um->slots);
    w_u32(um->hdr, 0x98, BENCH_UNIT_CAP);
    w_u64(um->hdr, 0xA0, (uintptr_t)um->gens);
    w_u64(eng, AT_RVA_UNIT_MANAGER, (uintptr_t)um->hdr);

    /* Populate 40% of 262,144 slots with realistic enemy units across scattered memory */
    uint32_t stride = BENCH_UNIT_CAP / BENCH_ENEMY_UNITS;
    for (uint32_t i = 0; i < BENCH_ENEMY_UNITS; i++) {
        uint32_t slot = (i * stride + (i & 1u)) % BENCH_UNIT_CAP;
        enemies[i].resource_id = 0x1111222233330000ULL + i;
        w_u64(enemies[i].hdr, 0x00, (uintptr_t)eng + AT_RVA_UNIT_VTABLE);
        w_u64(enemies[i].hdr, 0x30, (uintptr_t)&enemies[i].resource_id);
        um->slots[slot] = (uintptr_t)enemies[i].hdr;
        um->gens[slot] = 1u;
    }
    for (uint32_t i = 0; i < BENCH_FOREIGN_MESHES; i++) {
        w_u32(foreign[i].bytes, 0x34u, 0x8000u + i);
    }

    at_writer_init(ws, (uintptr_t)eng, bench_read, NULL);
    ws->skin_next = ws->approved_skin_next = noop_skin_next;
    ws->leaf_next = ws->approved_leaf_next = noop_leaf_next;
    ws->remove_next = ws->approved_remove_next = noop_remove_next;
    __atomic_store_n(&ws->ready, 1, __ATOMIC_RELEASE);

    uintptr_t caller = (uintptr_t)eng + AT_RVA_CALLER_NORMAL;
    int ok1 = run_case("CaseA_0Walkers_262k", ws, rs, foreign, caller, 2048u);

    /* Case B: 4 active co-op AT-ST walkers at high D11 slots */
    bench_walker *walkers = (bench_walker *)calloc(4, sizeof(*walkers));
    uint32_t wslots[4] = {50003u, 120007u, 180011u, 240013u};
    for (unsigned i = 0; i < 4u; i++) bind_walker(&walkers[i], um, rs, (uintptr_t)eng, wslots[i], i + 1u);
    at_writer_tick(ws, 0u); /* full admit */
    int ok2 = run_case("CaseB_4Walkers_262k", ws, rs, foreign, caller, 2048u);

    printf("ATST_TIER0_SUMMARY: %s\n", (ok1 && ok2) ? "ALL_PASS" : "GATE_FAIL");
    free(walkers); free(ws); free(foreign); free(enemies);
    free(um->slots); free(um->gens); free(um); free(rs);
    VirtualFree(eng, 0, MEM_RELEASE);
    return (ok1 && ok2) ? 0 : 1;
}
