/* Comprehensive C11 co-op & multi-actor writer verification suite:
 * - Auto-arm / zero-.arm / zero-solo-scan Loaded + WriterTick readiness
 * - 4-player squad (112 active meshes, duplicate garment resources across players)
 * - Local player at non-zero slot (slot 1, 2, 3) with remote host at slot 0
 * - Preview / corpse admission (107-node 4D1C334D294DFA97 outside PlayerManager)
 * - Foreign / non-107 / non-compact rejection & negative cache + handle invalidation
 * - ABA respawn generation rebind on reused MeshObject pointer
 * - 1E7CD0 RenderRemoveObserved native retirement, MXCSR/LastError & cross-world isolation
 * - >= 256 target slots capacity check & clean eviction
 * - Full gw_dispatch -> WriterCapture -> sm_evaluate(enum 5) -> lp_capture/recheck -> gw_leaf_dispatch loop
 */
#undef NDEBUG
#include <assert.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../src/attachment_observer.c"
#include "../src/envelope.c"

struct HfMod { const HfApi *api; };
static HfApi api;
static struct HfMod mod;

#define TEST_MAX_REGIONS 8192u
typedef struct {
    uintptr_t address;
    size_t size;
    unsigned char *bytes;
} MockSlab;

static MockSlab slabs[TEST_MAX_REGIONS];
static unsigned slabCount;
static uintptr_t nextSlabAddr = 0x200000000ULL;

static _Alignas(16) unsigned char retainedFiles[37][46400];
static unsigned testChecks;
static char lastLogLine[1024];
static unsigned logCount;
static uint32_t removeNextCalls;
static uintptr_t lastRemoveWorld, lastRemoveA3;
static uint32_t lastRemoveHandle;
static uint32_t outerCalls, leafCalls;
static uint32_t activeCsr = 0x9F80u;
static const DWORD activeErr = 0x2468u;
static int activeGrouped;
static uintptr_t expectedMesh, expectedPool, expectedNormalGraph;
static uint64_t expectedResource;
static uint32_t expectedOrdinal;
static uint32_t retireOnCaptureHandle;
static uintptr_t retireOnCaptureWorld;
static MockSlab *retirePktSlab;
static const lp_asset *activeAsset;
static void (*testReadHook)(uintptr_t at, size_t bytes);

void gw_test_fatal_cas_pause(gw_state *s) { (void)s; }

#define TCHECK(x) do { \
    if (!(x)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #x); \
        abort(); \
    } \
    testChecks++; \
} while (0)

static MockSlab *AllocSlabAt(uintptr_t at, size_t size) {
    assert(slabCount < TEST_MAX_REGIONS);
    MockSlab *s = &slabs[slabCount++];
    s->address = at;
    s->size = size;
    s->bytes = (unsigned char *)calloc(1, size);
    assert(s->bytes != NULL);
    return s;
}

static MockSlab *AllocSlab(size_t size) {
    size_t aligned = (size + 0xFFFu) & ~(size_t)0xFFFu;
    uintptr_t at = nextSlabAddr;
    nextSlabAddr += aligned + 0x1000u;
    return AllocSlabAt(at, size);
}

static void PutU64(MockSlab *s, size_t off, uint64_t v) {
    assert(off + 8 <= s->size);
    memcpy(s->bytes + off, &v, 8);
}

static void PutU32(MockSlab *s, size_t off, uint32_t v) {
    assert(off + 4 <= s->size);
    memcpy(s->bytes + off, &v, 4);
}

static void PutU16(MockSlab *s, size_t off, uint16_t v) {
    assert(off + 2 <= s->size);
    memcpy(s->bytes + off, &v, 2);
}

static void PutU8(MockSlab *s, size_t off, uint8_t v) {
    assert(off + 1 <= s->size);
    s->bytes[off] = v;
}

static void PutVec(MockSlab *s, size_t off, uint32_t len, uint32_t cap, uintptr_t data) {
    PutU32(s, off, len);
    PutU32(s, off + 4, cap);
    PutU64(s, off + 8, data);
}

static int TestResolveWithConcurrentRetire(struct gw_state *s, void *m, void *p, void *g,
                                           uintptr_t caller, LONG64 seq, se_target *out, coop_actor_kind *kind) {
    int ok = CoopResolveTarget(s, m, p, g, caller, seq, out, kind);
    if (ok && retireOnCaptureHandle && retirePktSlab) {
        uint32_t h = retireOnCaptureHandle;
        uintptr_t w = retireOnCaptureWorld;
        retireOnCaptureHandle = 0;
        PutU32(retirePktSlab, 4u, h);
        _mm_setcsr(0x9F81u); SetLastError(0x1357u);
        RenderRemoveObserved((void *)w, (const void *)retirePktSlab->address, NULL);
    }
    return ok;
}

static const uint64_t COMPACT_IDS[7] = {
    0x2BB094EDBC756963ULL, 0x7305FDF48136C05CULL, 0xDD25333BDF5F0143ULL,
    0x1E25DAEB99B1A917ULL, 0x512399E005614901ULL, 0x4D2F00B6C4929F00ULL,
    0xBB11C8041C14C747ULL
};
static const uint32_t COMPACT_NODES[7] = {21, 21, 43, 21, 43, 17, 29};
static const uint32_t COMPACT_BASES[7] = {14, 12, 15, 12, 15, 12, 17};

static const uintptr_t MOCK_ENGINE = 0x100000000ULL;
static const uintptr_t MOCK_GAME = 0x180000000ULL;

static MockSlab *sMgr, *sSlots, *sGens;
static MockSlab *sGameGlobals, *sPlayerRow, *sContext, *sEntities, *sNetBuckets, *sEntBuckets;
static MockSlab *sPlayerDesc[4];
static char mockLogsDir[512];
static char mockModFolder[1024];
static const char *MockModFolder(HfMod *m) { (void)m; return mockModFolder; }

static int MockRead(uintptr_t at, void *out, size_t bytes) {
    if (at == MOCK_ENGINE + MANAGER_RVA && bytes == 8) {
        memcpy(out, &sMgr->address, 8);
        return 0;
    }
    static const uint32_t vslots[][2] = {{0xE8, 0x2BD870}, {0xF8, 0x2BD890}, {0x1A8, 0x2BD9A0}, {0x1B0, 0x2BD9C0}};
    for (unsigned i = 0; i < 4; i++) {
        if (at == MOCK_ENGINE + VTABLE_RVA + vslots[i][0] && bytes == 8) {
            uintptr_t a = MOCK_ENGINE + vslots[i][1];
            memcpy(out, &a, 8);
            return 0;
        }
    }
    static const uint32_t renderV[][5] = {
        {0x16880B0, 0x4F2490, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x1688950, 0x510FA0, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x167A478, 0x354380, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x1672E70, 0x1E4CF0, 0xC91A0, 0xC91B0, 0xC91C0},
        {0x168B5A0, 0x5C2D20, 0x1CC6C0, 0x1CC6B0, 0x5C2DF0}
    };
    for (unsigned i = 0; i < 5; i++) {
        for (unsigned j = 0; j < 4; j++) {
            if (at == MOCK_ENGINE + renderV[i][0] + j * 8 && bytes == 8) {
                uintptr_t a = MOCK_ENGINE + renderV[i][j + 1];
                memcpy(out, &a, 8);
                return 0;
            }
        }
    }
    if (at == MOCK_ENGINE + 0x16729A8 && bytes == sizeof("RenderWorld")) {
        memcpy(out, "RenderWorld", bytes);
        return 0;
    }
    if (at == MOCK_ENGINE + 0x1689440 && bytes == sizeof("state_reflection_stream_rps")) {
        memcpy(out, "state_reflection_stream_rps", bytes);
        return 0;
    }
    if (at == MOCK_GAME + 0x3326468 && bytes == 8) { memcpy(out, &sPlayerRow->address, 8); return 0; }
    if (at == MOCK_GAME + 0x347CEF0 && bytes == 8) { memcpy(out, &sContext->address, 8); return 0; }
    if (at == MOCK_GAME + 0x346BF98 && bytes == 8) { memcpy(out, &sEntities->address, 8); return 0; }
    if (at == MOCK_GAME + 0x3326340 && bytes == 8) { memcpy(out, &sGameGlobals->address, 8); return 0; }

    for (unsigned i = 0; i < slabCount; i++) {
        MockSlab *s = &slabs[i];
        if (at >= s->address && bytes <= s->size && at - s->address <= s->size - bytes) {
            memcpy(out, s->bytes + (at - s->address), bytes);
            if (testReadHook) testReadHook(at, bytes);
            return 0;
        }
    }
    return 1;
}

static int MockCodeRead(uintptr_t at, void *out, size_t bytes) {
    static const unsigned char leafPrologue[16] = {
        0x48,0x8b,0xc4,0x48,0x89,0x58,0x18,0x48,0x89,0x70,0x20,0x57,0x48,0x83,0xec,0x40
    };
    if (at == MOCK_ENGINE + 0x5b2990 && bytes == 16) { memcpy(out, leafPrologue, 16); return 0; }
    for (unsigned i = 0; i < sizeof(invocationPins)/sizeof(invocationPins[0]); i++)
        if (at == MOCK_ENGINE + invocationPins[i].rva && bytes == invocationPins[i].size) {
            memcpy(out, invocationPins[i].bytes, bytes); return 0;
        }
    for (unsigned i = 0; i < sizeof(pins)/sizeof(pins[0]); i++)
        if (at == MOCK_ENGINE + pins[i].rva && bytes == pins[i].size) {
            memcpy(out, pins[i].bytes, bytes); return 0;
        }
    for (unsigned i = 0; i < sizeof(ownerPins)/sizeof(ownerPins[0]); i++) {
        uintptr_t base = ownerPins[i].game ? MOCK_GAME : MOCK_ENGINE;
        if (at == base + ownerPins[i].rva && bytes == ownerPins[i].size) {
            memcpy(out, ownerPins[i].bytes, bytes); return 0;
        }
    }
    for (unsigned i = 0; i < sizeof(renderPins)/sizeof(renderPins[0]); i++)
        if (at == MOCK_ENGINE + renderPins[i].rva && bytes == renderPins[i].size) {
            memcpy(out, renderPins[i].bytes, bytes); return 0;
        }
    for (unsigned i = 0; i < sizeof(worldPins)/sizeof(worldPins[0]); i++)
        if (at == MOCK_ENGINE + worldPins[i].rva && bytes == worldPins[i].size) {
            memcpy(out, worldPins[i].bytes, bytes); return 0;
        }
    for (unsigned i = 0; i < sizeof(descriptorPins)/sizeof(descriptorPins[0]); i++)
        if (at == MOCK_ENGINE + descriptorPins[i].rva && bytes == descriptorPins[i].size) {
            memcpy(out, descriptorPins[i].bytes, bytes); return 0;
        }
    for (unsigned i = 0; i < sizeof(stablePins)/sizeof(stablePins[0]); i++)
        if (at == MOCK_ENGINE + stablePins[i].rva && bytes == stablePins[i].size) {
            memcpy(out, stablePins[i].bytes, bytes); return 0;
        }
    return 1;
}

static const char *MockBuild(void) { return "25480438"; }
static const char *MockModId(HfMod *m) { (void)m; return OBS_ID; }
static uint32_t MockPhase(HfMod *m) { (void)m; return HF_LOAD_BOOT; }
static uintptr_t MockModBase(const char *name, size_t *sz) {
    assert(!sz);
    return !strcmp(name, "game.dll") ? MOCK_GAME : MOCK_ENGINE;
}
static const char *MockLogs(HfMod *m) { (void)m; return mockLogsDir; }
static void MockLog(HfMod *m, const char *fmt, ...) {
    (void)m;
    va_list args;
    va_start(args, fmt);
    int n = vsnprintf(lastLogLine, sizeof lastLogLine, fmt, args);
    va_end(args);
    assert(n >= 0 && n < (int)sizeof lastLogLine);
    logCount++;
}

typedef struct {
    MockSlab *skin;
    MockSlab *subrec;
    MockSlab *binds;
    MockSlab *poolOut;
    MockSlab *poses[2];
} MeshDrawRef;

static MeshDrawRef activeDrawRef;
static int expectSubstitution;

static uint32_t MockLeafNext(void *dst, const void *factor, const void *graph, const void *bind,
                             const uint32_t *palette, const uint32_t *map, uint32_t count) {
    (void)dst; (void)factor; (void)bind; (void)palette; (void)map;
    TCHECK(_mm_getcsr() == 0x9FA1u && GetLastError() == 0x7799u);
    TCHECK(graph != NULL);
    if (expectSubstitution) {
        TCHECK((uintptr_t)graph != activeDrawRef.poses[0]->address &&
               (uintptr_t)graph != activeDrawRef.poses[1]->address);
    } else if (activeDrawRef.poses[0] && activeDrawRef.poses[1]) {
        TCHECK((uintptr_t)graph == activeDrawRef.poses[0]->address ||
               (uintptr_t)graph == activeDrawRef.poses[1]->address);
    }
    leafCalls++;
    _mm_setcsr(0x7F85u);
    SetLastError(0x5544u);
    return count;
}

static uint32_t MockSkinNext(void *m, void *p, void *g) {
    TCHECK((uintptr_t)m == expectedMesh && (uintptr_t)p == expectedPool);
    TCHECK((uintptr_t)g == (activeGrouped ? expectedNormalGraph : 0u));
    TCHECK(_mm_getcsr() == activeCsr && GetLastError() == activeErr);
    outerCalls++;
    if (activeAsset && activeDrawRef.skin) {
        _Alignas(16) unsigned char factor[96] = {0};
        uint32_t dirOff = 16u + 4u * activeAsset->bones;
        const unsigned char *sbytes = activeDrawRef.skin->bytes;
        const unsigned char *rbytes = activeDrawRef.subrec->bytes;
        uint32_t poseArrOff = 0; (void)poseArrOff;
        for (uint32_t j = 0; j < activeAsset->groups; j++) {
            for (uint32_t t = 0; t < 2; t++) {
                uint32_t q = 0, n = 0, outOffEncoded = 0, palOff = 0;
                memcpy(&q, rbytes + j * 40u + 24u, 4);
                memcpy(&outOffEncoded, rbytes + j * 40u + 28u + 4u * t, 4);
                memcpy(&palOff, sbytes + dirOff + 4u + 8u * q, 4);
                memcpy(&n, sbytes + dirOff + 8u + 8u * q, 4);
                uintptr_t dst = activeDrawRef.poolOut->address + 0x10u * 16u + (uint64_t)(outOffEncoded / 3u) * 48u;
                uintptr_t pal = activeDrawRef.skin->address + dirOff + palOff;
                uintptr_t map = activeDrawRef.skin->address + 16u;
                uintptr_t caller = MOCK_ENGINE + (t ? 0x513054u : 0x512FEAu);
                _mm_setcsr(0x9FA1u);
                SetLastError(0x7799u);
                uint32_t rc = LeafObserved((void *)dst, factor + 48u * t,
                    (const void *)(expectedNormalGraph),
                    (const void *)activeDrawRef.binds->address,
                    (const uint32_t *)pal, (const uint32_t *)map, n);
                (void)caller;
                (void)rc;
                /* Note: LeafObserved uses __builtin_return_address(0); for exact caller verification call gw_leaf_dispatch directly! */
            }
        }
    }
    _mm_setcsr(0x5F81u);
    SetLastError(0x13579u);
    return 0x10u;
}

static uint32_t MockSkinNextDirectCaller(void *m, void *p, void *g) {
    TCHECK((uintptr_t)m == expectedMesh && (uintptr_t)p == expectedPool);
    TCHECK((uintptr_t)g == (activeGrouped ? expectedNormalGraph : 0u));
    TCHECK(_mm_getcsr() == activeCsr && GetLastError() == activeErr);
    outerCalls++;
    if (activeAsset && activeDrawRef.skin) {
        _Alignas(16) unsigned char factor[96] = {0};
        uint32_t dirOff = 16u + 4u * activeAsset->bones;
        const unsigned char *sbytes = activeDrawRef.skin->bytes;
        const unsigned char *rbytes = activeDrawRef.subrec->bytes;
        for (uint32_t j = 0; j < activeAsset->groups; j++) {
            for (uint32_t t = 0; t < 2; t++) {
                uint32_t q = 0, n = 0, outOffEncoded = 0, palOff = 0;
                memcpy(&q, rbytes + j * 40u + 24u, 4);
                memcpy(&outOffEncoded, rbytes + j * 40u + 28u + 4u * t, 4);
                memcpy(&palOff, sbytes + dirOff + 4u + 8u * q, 4);
                memcpy(&n, sbytes + dirOff + 8u + 8u * q, 4);
                uintptr_t dst = activeDrawRef.poolOut->address + 0x10u * 16u + (uint64_t)(outOffEncoded / 3u) * 48u;
                uintptr_t pal = activeDrawRef.skin->address + dirOff + palOff;
                uintptr_t map = activeDrawRef.skin->address + 16u;
                uintptr_t caller = MOCK_ENGINE + (t ? 0x513054u : 0x512FEAu);
                _mm_setcsr(0x9FA1u);
                SetLastError(0x7799u);
                uint32_t rc = gw_leaf_dispatch(&writer, (void *)dst, factor + 48u * t,
                    (const void *)activeDrawRef.poses[t]->address,
                    (const void *)activeDrawRef.binds->address,
                    (const uint32_t *)pal, (const uint32_t *)map, n, caller);
                TCHECK(rc == n && _mm_getcsr() == 0x7F85u && GetLastError() == 0x5544u);
            }
        }
    }
    _mm_setcsr(0x5F81u);
    SetLastError(0x13579u);
    return 0x10u;
}

static uint32_t MockRemoveNext(void *world, const void *packet, void *a3) {
    TCHECK(_mm_getcsr() == 0x9F81u && GetLastError() == 0x1357u);
    removeNextCalls++;
    lastRemoveWorld = (uintptr_t)world;
    lastRemoveA3 = (uintptr_t)a3;
    if (packet) MockRead((uintptr_t)packet + 4u, &lastRemoveHandle, 4);
    _mm_setcsr(0x3F80u);
    SetLastError(0x8888u);
    return 0;
}

static HfEventCallback capturedLoadedCb;
static HfTickCallback capturedTickCb;

static int MockOn(HfMod *m, HfEvent ev, HfEventCallback cb, void *u) {
    (void)m; (void)u;
    if (ev == HF_EVENT_GAME_LOADED) capturedLoadedCb = cb;
    return 0;
}
static int MockOnTick(HfMod *m, HfTickCallback cb, void *u) {
    (void)m; (void)u;
    capturedTickCb = cb;
    return 1;
}
static int MockHookEx(HfMod *m, uintptr_t target, void *detour, void **orig, int prio) {
    (void)m; (void)detour; (void)prio;
    if (target == MOCK_ENGINE + 0x5B2990u) *orig = (void *)MockLeafNext;
    else if (target == MOCK_ENGINE + 0x512B80u) *orig = (void *)MockSkinNextDirectCaller;
    else if (target == MOCK_ENGINE + 0x1E7CD0u) *orig = (void *)MockRemoveNext;
    else return -1;
    return 0;
}

typedef struct {
    MockSlab *rm, *rq, *rw, *handles, *objects, *types, *mapping, *poolOut;
    uint32_t nextHandleSlot;
} MockWorld;

static uint32_t MakeHandle(uint32_t idx, uint32_t gen) {
    return 0x800u | (idx & 0x7FFu) | ((gen & 0xFFu) << 12);
}

static MockWorld CreateMockWorld(void) {
    MockWorld w = {0};
    w.rm = AllocSlab(0x60);
    w.rq = AllocSlab(0x118);
    w.rw = AllocSlab(0x6B0);
    w.handles = AllocSlab(2048 * 4);
    w.objects = AllocSlab(2048 * 8);
    w.types = AllocSlab(2048 * 4);
    w.mapping = AllocSlab(2048 * 4);
    w.poolOut = AllocSlab(4096);
    w.nextHandleSlot = 1u;

    PutU64(w.rq, 0, MOCK_ENGINE + 0x168B5A0u);
    PutU64(w.rq, 8, MOCK_ENGINE + 0x1689440u);
    PutU64(w.rq, 0x20, w.rw->address + 0x550u);
    PutU64(w.rq, 0x80, w.rw->address + 0x10u);

    PutU64(w.rw, 0, MOCK_ENGINE + 0x1672E70u);
    PutU32(w.rw, 8, 1u);
    PutU64(w.rw, 0x10, MOCK_ENGINE + 0x168B5A0u);
    PutU64(w.rw, 0x18, MOCK_ENGINE + 0x16729A8u);
    PutU64(w.rw, 0x150, w.rm->address);
    PutVec(w.rw, 0x168, 2048, 2048, w.objects->address);
    PutVec(w.rw, 0x180, 2048, 2048, w.types->address);
    PutVec(w.rw, 0x1B8, 2048, 2048, w.mapping->address);
    PutU64(w.rw, 0x488u, w.poolOut->address);
    PutU32(w.rw, 0x490u, 32u);

    PutU64(w.rm, 0x10, w.rq->address);
    PutU64(w.rm, 0x18, w.handles->address);
    PutU64(w.rm, 0x20, MOCK_ENGINE + 0x75230u);
    PutU64(w.rm, 0x28, MOCK_ENGINE + 0x75420u);
    PutU32(w.rm, 0x30, 0x348u);
    PutU32(w.rm, 0x34, 0x04000004u);
    PutU32(w.rm, 0x3C, 2048u);
    PutU32(w.rm, 0x40, 2047u);
    PutU32(w.rm, 0x44, 0x00FFF000u);
    PutU32(w.rm, 0x48, 0xFF000000u);
    PutU32(w.rm, 0x4C, 2048u);
    PutU64(w.rm, 0x58, w.handles->address);
    return w;
}

static uint32_t WorldRegister(MockWorld *w, MockSlab *obj, uint32_t type, uint32_t gen) {
    uint32_t idx = w->nextHandleSlot++;
    assert(idx < 2048u);
    uint32_t h = MakeHandle(idx, gen);
    PutU32(w->handles, idx * 4u, h);
    PutU32(w->mapping, idx * 4u, idx);
    PutU64(w->objects, idx * 8u, obj->address);
    PutU32(w->types, idx * 4u, type);
    return h;
}

typedef struct {
    MockSlab *parentUnit, *parentGraph, *parentPoses[2], *parentRes;
    uint32_t parentUnitIdx, parentAppHandle;
    uint8_t parentGen;
    MockSlab *childUnits[7], *childGraphs[7], *childPoses[7][2], *childRes[7];
    MockSlab *meshEntries[7], *topos[7];
    uint32_t childUnitIdx[7], childGraphHandle[7];
    uint8_t childGen[7];
    MockSlab *descs[7][5], *meshes[7][5], *skins[7][5], *subrecs[7][5], *binds[7][5];
    uint32_t meshHandles[7][5];
    MockSlab *poolOut;
    MockWorld *world;
} MockActor;

static MockSlab *AllocUnitSlot(uint32_t idx, uint8_t gen, uint64_t resId, uint32_t nodes, MockSlab **outRes) {
    MockSlab *u = AllocSlab(0x200);
    MockSlab *r = AllocSlab(16);
    PutU64(r, 0, resId);
    PutU64(u, 0, MOCK_ENGINE + VTABLE_RVA);
    PutU64(u, 0x30, r->address);
    PutU32(u, 0x70, nodes);
    PutU64(sSlots, idx * 8u, u->address);
    PutU8(sGens, idx, gen);
    if (outRes) *outRes = r;
    return u;
}

static uint32_t RetainedSampleForTarget(unsigned t) {
    for (unsigned i = 0; i < 37u; i++) {
        uint32_t rt = 0;
        memcpy(&rt, retainedFiles[i] + 28u, 4);
        if (rt == t) return i;
    }
    return 0;
}

static void SpawnMockActor(MockActor *a, MockWorld *w, uint32_t baseUnitIdx, uint8_t gen,
                           uint64_t parentResource, uint32_t parentNodes, int isCompactGarment) {
    memset(a, 0, sizeof *a);
    a->world = w;
    a->parentUnitIdx = baseUnitIdx;
    a->parentGen = gen;
    a->poolOut = w->poolOut;

    a->parentUnit = AllocUnitSlot(baseUnitIdx, gen, parentResource, parentNodes, &a->parentRes);
    MockSlab *pWorld = AllocSlab(0x100);
    MockSlab *pTopo = AllocSlab(parentNodes * 4u);
    PutU64(a->parentUnit, 0x88u, pWorld->address);
    PutU64(a->parentUnit, 0x98u, pTopo->address);

    a->parentGraph = AllocSlab(56);
    a->parentPoses[0] = AllocSlab(parentNodes * 48u);
    a->parentPoses[1] = AllocSlab(parentNodes * 48u);
    unsigned s0 = RetainedSampleForTarget(0);
    if (parentNodes == 107u) {
        memcpy(a->parentPoses[0]->bytes, retainedFiles[s0] + 256u + 13312u, 107u * 48u);
        memcpy(a->parentPoses[1]->bytes, retainedFiles[s0] + 256u + 18448u, 107u * 48u);
    }
    PutU64(a->parentGraph, 0, MOCK_ENGINE + 0x16880B0u);
    PutU32(a->parentGraph, 8, 2u);
    PutU32(a->parentGraph, 0x10u, parentNodes);
    PutU64(a->parentGraph, 0x18u, a->parentPoses[0]->address);
    PutU32(a->parentGraph, 0x28u, parentNodes);
    PutU64(a->parentGraph, 0x30u, a->parentPoses[1]->address);
    a->parentAppHandle = WorldRegister(w, a->parentGraph, 2u, gen);
    PutU32(a->parentUnit, 0x64u, a->parentAppHandle);
    PutU64(a->parentUnit, 0x68u, w->rm->address + 0x10u);

    for (unsigned t = 0; t < 7u; t++) {
        uint64_t gRes = isCompactGarment ? COMPACT_IDS[t] : (0x9999000000000000ULL + t);
        uint32_t gNodes = COMPACT_NODES[t];
        uint32_t idx = baseUnitIdx + 1u + t;
        a->childUnitIdx[t] = idx;
        a->childGen[t] = gen;
        a->childUnits[t] = AllocUnitSlot(idx, gen, gRes, gNodes, &a->childRes[t]);
        PutU64(a->childUnits[t], 0x78u, pWorld->address);
        PutU64(a->childUnits[t], 0x88u, pWorld->address + (t + 1u) * 0x100u);
        PutU64(a->childUnits[t], 0x1D0u, a->parentUnit->address);
        if (t > 0) PutU64(a->childUnits[t - 1], 0x1E0u, a->childUnits[t]->address);

        a->topos[t] = AllocSlab(gNodes * 4u);
        for (uint32_t k = 0; k < gNodes; k++) {
            PutU16(a->topos[t], k * 4u, 2u);
            PutU16(a->topos[t], k * 4u + 2u, (uint16_t)(k % parentNodes));
        }
        PutU64(a->childUnits[t], 0x98u, a->topos[t]->address);

        unsigned sample = RetainedSampleForTarget(t);
        a->childGraphs[t] = AllocSlab(56);
        a->childPoses[t][0] = AllocSlab(gNodes * 48u);
        a->childPoses[t][1] = AllocSlab(gNodes * 48u);
        memcpy(a->childPoses[t][0]->bytes, retainedFiles[sample] + 256u + 1024u, gNodes * 48u);
        memcpy(a->childPoses[t][1]->bytes, retainedFiles[sample] + 256u + 7168u, gNodes * 48u);
        PutU64(a->childGraphs[t], 0, MOCK_ENGINE + 0x16880B0u);
        PutU32(a->childGraphs[t], 8, 2u);
        PutU32(a->childGraphs[t], 0x10u, gNodes);
        PutU64(a->childGraphs[t], 0x18u, a->childPoses[t][0]->address);
        PutU32(a->childGraphs[t], 0x28u, gNodes);
        PutU64(a->childGraphs[t], 0x30u, a->childPoses[t][1]->address);
        a->childGraphHandle[t] = WorldRegister(w, a->childGraphs[t], 2u, gen);
        PutU32(a->childUnits[t], 0x64u, a->childGraphHandle[t]);
        PutU64(a->childUnits[t], 0x68u, w->rm->address + 0x10u);

        a->meshEntries[t] = AllocSlab(5 * 16);
        PutU32(a->childUnits[t], 0xC8u, 5u);
        PutU64(a->childUnits[t], 0xD0u, a->meshEntries[t]->address);

        for (unsigned j = 0; j < 5u; j++) {
            a->descs[t][j] = AllocSlab(0xDC);
            a->meshes[t][j] = AllocSlab(0xD0);
            MockSlab *d = a->descs[t][j], *m = a->meshes[t][j];
            PutU64(a->meshEntries[t], j * 16u, d->address);
            PutU64(d, 0x90u, a->childUnits[t]->address + 0x60u);
            if (j == 0) {
                PutU32(d, 4u, 0xFFFFFFu);
                PutU64(d, 8u, 0u);
                PutU32(d, 0x98u, 0u);
            } else {
                uint32_t mHandle = WorldRegister(w, m, 3u, gen);
                a->meshHandles[t][j] = mHandle;
                uint32_t mNode = COMPACT_BASES[t] + j - 1u;
                const lp_asset *asset = lp_asset_find(COMPACT_IDS[t], j);
                assert(asset != NULL);

                a->skins[t][j] = AllocSlab(4096);
                a->binds[t][j] = AllocSlab(48u * asset->bones + 64u);
                a->subrecs[t][j] = AllocSlab(320);

                MockSlab *sk = a->skins[t][j], *bd = a->binds[t][j], *sr = a->subrecs[t][j];
                memcpy(bd->bytes, asset->bind, 48u * asset->bones);
                PutU32(sk, 0, asset->bones);
                PutU32(sk, 8, 16u);
                uint32_t dirOff = 16u + 4u * asset->bones;
                PutU32(sk, 12, dirOff);
                PutU32(sk, dirOff, asset->groups);
                memcpy(sk->bytes + 16u, asset->map, 4u * asset->bones);
                uint32_t palCur = 4u + 8u * asset->groups;
                uint32_t outMat = 0;
                for (uint32_t g = 0; g < asset->groups; g++) {
                    PutU32(sk, dirOff + 4u + 8u * g, palCur);
                    PutU32(sk, dirOff + 8u + 8u * g, asset->counts[g]);
                    memcpy(sk->bytes + dirOff + palCur, asset->palette[g], 4u * asset->counts[g]);
                    palCur += 4u * asset->counts[g];
                    PutU32(sr, g * 40u + 24u, g);
                    PutU32(sr, g * 40u + 28u, outMat * 3u);
                    outMat += asset->counts[g];
                    PutU32(sr, g * 40u + 32u, outMat * 3u);
                    outMat += asset->counts[g];
                }

                PutU32(d, 4u, mHandle);
                PutU64(d, 8u, w->rm->address + 0x10u);
                PutU64(d, 0x80u, sk->address);
                PutU32(d, 0x98u, mNode);

                PutU64(m, 0, MOCK_ENGINE + 0x1688950u);
                PutU32(m, 8, 3u);
                PutU32(m, 0x30u, mNode);
                PutU32(m, 0x34u, a->childGraphHandle[t]);
                PutU64(m, 0x38u, a->childGraphs[t]->address);
                PutU64(m, 0x70u, sk->address);
                PutU64(m, 0x80u, bd->address);
                PutU32(m, 0x90u, asset->groups);
                PutU64(m, 0x98u, sr->address);
                PutU32(m, 0xC8u, outMat);
            }
        }
    }
    PutU64(a->parentUnit, 0x1D8u, a->childUnits[0]->address);
}

static void SetPlayerSlotEx(uint32_t slot, uint64_t peer, uint32_t networkId, uint32_t life,
                            int localFlag, uint32_t entityId, const MockActor *actor,
                            uint32_t hashCap, uint32_t descIdx, uint32_t probeCollisions) {
    PutU64(sPlayerRow, 0x2C8u + slot * 0x38u, peer);
    PutU32(sPlayerRow, 0x2E0u + slot * 0x38u, life);
    PutU32(sPlayerRow, 0x3A8u + slot * 0x20u, networkId);
    PutU64(sPlayerRow, 0xE8u + slot * 8u, sPlayerDesc[slot]->address);
    PutU32(sPlayerDesc[slot], 8u, entityId);
    PutU8(sPlayerDesc[slot], 20u, (uint8_t)localFlag);

    if (actor) {
        uint32_t nBase = networkId * 2u;
        uint32_t eBase = entityId * 2u;
        for (uint32_t p = 0; p < probeCollisions; p++) {
            uint32_t nb = (nBase + p) % hashCap;
            uint32_t eb = (eBase + p) % hashCap;
            PutU32(sNetBuckets, nb * 8u, 0x70000000u + p);
            PutU32(sNetBuckets, nb * 8u + 4u, 0xFFFFFFFFu);
            PutU32(sEntBuckets, eb * 8u, 0x71000000u + p);
            PutU32(sEntBuckets, eb * 8u + 4u, 0xFFFFFFFFu);
        }
        uint32_t nBucket = (nBase + probeCollisions) % hashCap;
        uint32_t eBucket = (eBase + probeCollisions) % hashCap;
        PutU32(sNetBuckets, nBucket * 8u, networkId);
        PutU32(sNetBuckets, nBucket * 8u + 4u, descIdx);
        PutU32(sEntBuckets, eBucket * 8u, entityId);
        PutU32(sEntBuckets, eBucket * 8u + 4u, descIdx);

        uint32_t unitRef = ((uint32_t)actor->parentGen << 22) | (actor->parentUnitIdx & 0x3FFFFFu);
        size_t off = 0xF32F18u + (size_t)descIdx * 24u;
        PutU64(sEntities, off, COOP_HUMAN_AVATAR_RESOURCE);
        PutU32(sEntities, off + 8u, entityId);
        PutU32(sEntities, off + 12u, unitRef);
        PutU32(sEntities, off + 16u, networkId);
        PutU32(sEntities, off + 20u, 1u);
    }
}

static void SetPlayerSlot(uint32_t slot, uint64_t peer, uint32_t networkId, uint32_t life,
                          int localFlag, uint32_t entityId, const MockActor *actor) {
    SetPlayerSlotEx(slot, peer, networkId, life, localFlag, entityId, actor, 4096u, slot + 1u, 0u);
}

static void SetActorSelfHandles(const MockActor *a) {
    PutU32(a->parentUnit, 8u, ((uint32_t)a->parentGen << 22) | (a->parentUnitIdx & 0x3FFFFFu));
    for (unsigned t = 0; t < 7u; t++) {
        PutU32(a->childUnits[t], 8u, ((uint32_t)a->childGen[t] << 22) | (a->childUnitIdx[t] & 0x3FFFFFu));
    }
}

static void InvokeActorMesh(const MockActor *a, unsigned t, unsigned j, int grouped, int subExpected) {
    activeGrouped = grouped;
    expectSubstitution = subExpected;
    expectedMesh = a->meshes[t][j]->address;
    expectedPool = a->world->rw->address + 0x318u;
    expectedNormalGraph = a->childGraphs[t]->address;
    expectedResource = COMPACT_IDS[t];
    expectedOrdinal = j;
    activeAsset = lp_asset_find(COMPACT_IDS[t], j);
    activeDrawRef = (MeshDrawRef){a->skins[t][j], a->subrecs[t][j], a->binds[t][j], a->poolOut, {a->childPoses[t][0], a->childPoses[t][1]}};

    unsigned sample = RetainedSampleForTarget(t);
    memcpy(a->parentPoses[0]->bytes, retainedFiles[sample] + 256u + 13312u, 107u * 48u);
    memcpy(a->parentPoses[1]->bytes, retainedFiles[sample] + 256u + 18448u, 107u * 48u);

    _mm_setcsr(activeCsr);
    SetLastError(activeErr);
    uint32_t rc = gw_dispatch(&writer, (void *)expectedMesh, (void *)expectedPool,
        grouped ? (void *)expectedNormalGraph : NULL,
        MOCK_ENGINE + (grouped ? 0x258AABu : 0x258E19u));
    TCHECK(rc == 0x10u);
    TCHECK(_mm_getcsr() == 0x5F81u && GetLastError() == 0x13579u);
}

static uint32_t unbindParentHandleOnResolve;
static void InitMockHost(const char *logsDir) {
    if (invocationDirectoryHandle != INVALID_HANDLE_VALUE) {
        CloseHandle(invocationDirectoryHandle);
        invocationDirectoryHandle = INVALID_HANDLE_VALUE;
    }
    for (unsigned i = 0; i < slabCount; i++) {
        free(slabs[i].bytes);
        slabs[i].bytes = NULL;
    }
    snprintf(mockLogsDir, sizeof mockLogsDir, "%s", logsDir);
    slabCount = 0;
    nextSlabAddr = 0x200000000ULL;
    testReadHook = NULL;
    unbindParentHandleOnResolve = 0;
    retireOnCaptureHandle = 0;
    memset(&writer, 0, sizeof writer);
    memset(&invocationEnvelope, 0, sizeof invocationEnvelope);
    installed = 0; stopped = 0; tickLock = 0; coopDynamicMode = 0;
    writerPollAt = 0; writerHealthAt = 0; writerDrainAt = 0;
    writerLastAdmissions = writerLastRetires = writerLastAba = writerLastPlayers = 0;
    writerHealthRows = 0; writerDiagnosticsDone = 0; writerReportedFatal = 0;
    writerDiagnosticSequence = 0; writerDiagnosticDropped = 0;
    memset((void *)writerDiagnosticAttempts, 0, sizeof writerDiagnosticAttempts);
    memset((void *)writerDiagnosticRoutes, 0, sizeof writerDiagnosticRoutes);
    memset((void *)writerDiagnosticMeshes, 0, sizeof writerDiagnosticMeshes);
    shadowRecords = shadowOk = shadowFail = 0;
    leafOk = leafFail = leafNormalMask = leafGroupedMask = 0;
    invocationFiles = invocationAccepted = invocationRefused = invocationAbandoned = invocationMask = 0;
    outerCalls = leafCalls = removeNextCalls = logCount = 0;

    sMgr = AllocSlab(0xB0);
    sSlots = AllocSlab(1024 * 8);
    sGens = AllocSlab(1024);
    PutU32(sMgr, 0x98u, 512u);
    PutU64(sMgr, 0x88u, sSlots->address);
    PutU64(sMgr, 0xA0u, sGens->address);

    sGameGlobals = AllocSlab(0xB0000);
    sPlayerRow = AllocSlab(0x480);
    sContext = AllocSlab(0x16000);
    sEntities = AllocSlab(0xF50000);
    sNetBuckets = AllocSlab(4096 * 8);
    sEntBuckets = AllocSlab(4096 * 8);
    memset(sNetBuckets->bytes, 0xFF, sNetBuckets->size);
    memset(sEntBuckets->bytes, 0xFF, sEntBuckets->size);
    PutU64(sEntities, 0xF22EC8u, sNetBuckets->address);
    PutU32(sEntities, 0xF22EC8u + 8u, 4096u);
    PutU32(sEntities, 0xF22EC8u + 12u, 0xFFFFFFFFu);
    PutU32(sEntities, 0xF22EC8u + 16u, 2u);
    PutU64(sEntities, 0xF1AEB0u, sEntBuckets->address);
    PutU32(sEntities, 0xF1AEB0u + 8u, 4096u);
    PutU32(sEntities, 0xF1AEB0u + 12u, 0xFFFFFFFFu);
    PutU32(sEntities, 0xF1AEB0u + 16u, 2u);
    for (unsigned i = 0; i < 4; i++) sPlayerDesc[i] = AllocSlab(32);

    api.version = 16;
    api.size = sizeof api;
    api.build_id = MockBuild;
    api.mod_id = MockModId;
    api.load_phase = MockPhase;
    api.read = MockRead;
    api.read_code = MockCodeRead;
    api.module_base = MockModBase;
    api.on = MockOn;
    api.on_game_thread = MockOnTick;
    api.hook_ex = MockHookEx;
    api.logs_folder = MockLogs;
    api.mod_folder = MockModFolder;
    api.log = MockLog;
    mod.api = &api;

    TCHECK(hellforge_mod_init(&api, &mod) == 0);
    TCHECK(capturedLoadedCb != NULL);
    capturedLoadedCb(&mod, HF_EVENT_GAME_LOADED, NULL);
    TCHECK(!Stopped() && coopDynamicMode == 1 && capturedTickCb != NULL);
    TCHECK(writer.approved_next == (void *)MockSkinNextDirectCaller);
    TCHECK(writer.approved_leaf_next == (void *)MockLeafNext);
    TCHECK(writer.approved_remove_next == (void *)MockRemoveNext);
}

static MockWorld *unbindWorld;
static int TestResolveWithUnbindParentGraph(struct gw_state *s, void *m, void *p, void *g,
                                            uintptr_t caller, LONG64 seq, se_target *out, coop_actor_kind *kind) {
    int ok = CoopResolveTarget(s, m, p, g, caller, seq, out, kind);
    if (ok && unbindParentHandleOnResolve && unbindWorld) {
        uint32_t idx = unbindParentHandleOnResolve & 0x7FFu;
        PutU32(unbindWorld->handles, idx * 4u, 0u);
        unbindParentHandleOnResolve = 0;
    }
    return ok;
}

static uintptr_t hookWatchMgrAddr, hookWatchWorldAddr, hookWatchPoseAddr;
static MockSlab *hookWatchMgrSlab, *hookWatchWorldSlab, *hookWatchPoseSlab;
static unsigned hookMgrHits, hookWorldHits, hookPoseHits;
static size_t hookWatchPoseOff;

static void HookMutateUnitMgrCount(uintptr_t at, size_t bytes) {
    (void)bytes;
    if (hookWatchMgrSlab && at == hookWatchMgrAddr) {
        hookMgrHits++;
        /* After openMgr is read inside CoopCaptureTarget (2nd read after CoopResolveTarget), bump capacity 512 -> 514 and populate new enemy slots. */
        if (hookMgrHits == 2u) {
            MockSlab *enemy1 = AllocSlab(0x200);
            MockSlab *enemy2 = AllocSlab(0x200);
            PutU64(sSlots, 512u * 8u, enemy1->address);
            PutU8(sGens, 512u, 1u);
            PutU64(sSlots, 513u * 8u, enemy2->address);
            PutU8(sGens, 513u, 1u);
            PutU32(hookWatchMgrSlab, 0x98u, 514u);
        }
    }
}

static void HookMutateRenderWorldLen(uintptr_t at, size_t bytes) {
    (void)bytes;
    if (hookWatchWorldSlab && at == hookWatchWorldAddr) {
        hookWorldHits++;
        /* Mutate objects.length (2040 -> 2041, keeping <= capacity 2048) right before the closing RenderRead inside CoopCaptureTarget. */
        if (hookWorldHits == 2u) {
            PutU32(hookWatchWorldSlab, 0x168u, 2041u);
        }
    }
}

static void HookMutatePoseDuringRecheck(uintptr_t at, size_t bytes) {
    (void)bytes;
    if (hookWatchPoseSlab && at == hookWatchPoseAddr) {
        hookPoseHits++;
        /* Corrupt one float at mesh_node row right after lp_capture (1st read at mesh_node offset) so lp_recheck (2nd read) catches it! */
        if (hookPoseHits == 1u) {
            uint32_t orig = 0;
            memcpy(&orig, hookWatchPoseSlab->bytes + hookWatchPoseOff, 4);
            orig ^= 0x00000001u;
            PutU32(hookWatchPoseSlab, hookWatchPoseOff, orig);
        }
    }
}

static void ResetMockUnitsAndWorld(void) {
    memset(sSlots->bytes, 0, sSlots->size);
    memset(sGens->bytes, 0, sGens->size);
    PutU32(sMgr, 0x98u, 512u);
}

static uint64_t perfTestReadCount, perfTestReadBytes;
static void PerfCountReadHook(uintptr_t at, size_t bytes) {
    (void)at;
    perfTestReadCount++;
    perfTestReadBytes += (uint64_t)bytes;
}

int main(int argc, char **argv) {
    TCHECK(argc == 3);
    for (unsigned i = 0; i < 37u; i++) {
        char path[2048];
        snprintf(path, sizeof path, "%s/skin-invocation-%06u.bin", argv[1], i + 1u);
        FILE *f = fopen(path, "rb");
        TCHECK(f && fread(retainedFiles[i], 1, 46400, f) == 46400 && fgetc(f) == EOF);
        fclose(f);
    }

    InitMockHost(argv[2]);

    /* Test 1: Auto-arm without .arm file or solo ship scan (even with PlayerManager.count == 0!). */
    PutU32(sPlayerRow, 0x84u, 0u);
    WriterTick(&mod, 0.016f, NULL);
    TCHECK(writer.ready == 1);
    TCHECK(strstr(lastLogLine, "writer_ready") && strstr(lastLogLine, "coop=1") && strstr(lastLogLine, "dynamic=1"));

    /* Create World 1 & World 2 */
    MockWorld w1 = CreateMockWorld();
    MockWorld w2 = CreateMockWorld();

    /* Spawn 4 player avatars + 1 preview/corpse + 1 invalid 106-node avatar + 1 non-compact avatar */
    static MockActor squad[4], preview, badParent, foreignGarment;
    for (unsigned p = 0; p < 4u; p++) {
        SpawnMockActor(&squad[p], &w1, p * 8u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
    }
    SpawnMockActor(&preview, &w1, 40u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
    SpawnMockActor(&badParent, &w1, 50u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 106u, 1);
    SpawnMockActor(&foreignGarment, &w1, 60u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 0);

    /* Configure 4-player co-op with LOCAL player at slot 1 and REMOTE HOST at slot 0! */
    PutU64(sContext, 0xB398u, 0x2002ULL);
    PutU32(sPlayerRow, 0x84u, 4u);
    SetPlayerSlot(0, 0x1001ULL, 10u, 3u, 0, 100u, &squad[0]); /* remote host */
    SetPlayerSlot(1, 0x2002ULL, 11u, 3u, 1, 101u, &squad[1]); /* local client at non-zero slot! */
    SetPlayerSlot(2, 0x3003ULL, 12u, 3u, 0, 102u, &squad[2]); /* remote peer */
    SetPlayerSlot(3, 0x4004ULL, 13u, 3u, 0, 103u, &squad[3]); /* remote peer */

    /* WriterTick discovers all spawned garments on the game thread (never scanning UnitManager on 512B80). */
    writerPollAt = 0;
    WriterTick(&mod, 0.016f, NULL);

    /* Test 2: Full 4-player 112-mesh admission across BOTH normal (route 1) and grouped (route 2) = 224 calls */
    for (unsigned p = 0; p < 4u; p++) {
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                InvokeActorMesh(&squad[p], t, j, 0, 1);
                InvokeActorMesh(&squad[p], t, j, 1, 1);
            }
        }
    }
    TCHECK(coopReg.active_targets == 112u);
    TCHECK(writer.dynamic_admissions == 112);
    TCHECK(writer.local_hits == 56 && writer.remote_hits == 168 && writer.preview_hits == 0);
    TCHECK(writer.corrected == 224 && writer.failed == 0 && writer.fatal == 0);

    /* Test 3: Preview/corpse outside PlayerManager */
    for (unsigned t = 0; t < 7u; t++) {
        for (unsigned j = 1; j <= 4u; j++) {
            InvokeActorMesh(&preview, t, j, 0, 1);
        }
    }
    TCHECK(coopReg.active_targets == 140u);
    TCHECK(writer.preview_hits == 28 && writer.corrected == 252 && writer.failed == 0);

    /* Test 4: Non-107 parent & non-compact garment must fail admission cleanly (unclassified, passthrough) */
    activeGrouped = 0; activeAsset = NULL; expectSubstitution = 0;
    expectedMesh = badParent.meshes[0][1]->address;
    expectedPool = w1.rw->address + 0x318u;
    _mm_setcsr(activeCsr); SetLastError(activeErr);
    TCHECK(SkinObserved((void *)expectedMesh, (void *)expectedPool, NULL) == 0x10u);
    expectedMesh = foreignGarment.meshes[0][1]->address;
    _mm_setcsr(activeCsr); SetLastError(activeErr);
    TCHECK(SkinObserved((void *)expectedMesh, (void *)expectedPool, NULL) == 0x10u);
    /* Hit negative cache a second time on foreign garment */
    _mm_setcsr(activeCsr); SetLastError(activeErr);
    TCHECK(SkinObserved((void *)expectedMesh, (void *)expectedPool, NULL) == 0x10u);
    TCHECK(writer.unclassified == 3 && coopReg.active_targets == 140u && !writer.fatal);

    /* Test 5: 1E7CD0 RenderRemoveObserved native retirement + cross-world isolation */
    MockSlab *sPkt = AllocSlab(16);
    /* Retire same handle value in DIFFERENT world (w2): MUST NOT retire w1 slots! */
    uint32_t handleSlot1 = squad[0].meshHandles[0][1];
    PutU32(sPkt, 4u, handleSlot1);
    _mm_setcsr(0x9F81u); SetLastError(0x1357u);
    RenderRemoveObserved((void *)w2.rw->address, (const void *)sPkt->address, (void *)0xDEADu);
    TCHECK(removeNextCalls == 1u && coopReg.active_targets == 140u && writer.retires == 0);

    /* Retire squad[0].meshHandles[0][1] in w1 -> retires that 1 mesh */
    _mm_setcsr(0x9F81u); SetLastError(0x1357u);
    RenderRemoveObserved((void *)w1.rw->address, (const void *)sPkt->address, (void *)0xBEEFu);
    TCHECK(removeNextCalls == 2u && lastRemoveHandle == handleSlot1 && coopReg.active_targets == 139u && writer.retires == 1);

    /* Retire by child_graph_handle -> retires remaining 3 ordinals of garment 0 on squad[0] */
    PutU32(sPkt, 4u, squad[0].childGraphHandle[0]);
    _mm_setcsr(0x9F81u); SetLastError(0x1357u);
    RenderRemoveObserved((void *)w1.rw->address, (const void *)sPkt->address, NULL);
    TCHECK(coopReg.active_targets == 136u && writer.retires == 4);

    /* Retire by parent_graph_handle -> retires ALL remaining 24 garment meshes of squad[0]! */
    PutU32(sPkt, 4u, squad[0].parentAppHandle);
    _mm_setcsr(0x9F81u); SetLastError(0x1357u);
    RenderRemoveObserved((void *)w1.rw->address, (const void *)sPkt->address, NULL);
    TCHECK(coopReg.active_targets == 112u && writer.retires == 28);

    /* Test 6: ABA respawn rebind WITHOUT 1E7CD0 firing first (reused MeshObject pointer + bumped Unit generation) */
    uintptr_t reusedMeshAddr = squad[2].meshes[4][2]->address;
    uint32_t reusedNewHandle = WorldRegister(&w1, squad[2].meshes[4][2], 3u, 7u);
    PutU8(sGens, squad[2].parentUnitIdx, 2u); /* bump parent generation 1 -> 2 */
    PutU8(sGens, squad[2].childUnitIdx[4], 2u); /* bump child generation 1 -> 2 */
    squad[2].parentGen = 2u;
    squad[2].childGen[4] = 2u;
    squad[2].meshHandles[4][2] = reusedNewHandle;
    PutU32(squad[2].descs[4][2], 4u, reusedNewHandle);
    SetPlayerSlot(2, 0x3003ULL, 12u, 3u, 0, 102u, &squad[2]); /* Update EntityManager reference gen 2 */
    const coop_target_slot *beforeAba = coop_find_mesh(&coopReg, reusedMeshAddr);
    TCHECK(beforeAba && beforeAba->parent_gen == 1u);
    uint32_t epochBefore = beforeAba->admit_epoch;
    InvokeActorMesh(&squad[2], 4, 2, 0, 1);
    const coop_target_slot *afterAba = coop_find_mesh(&coopReg, reusedMeshAddr);
    TCHECK(afterAba && afterAba->parent_gen == 2u && afterAba->child_gen == 2u && afterAba->admit_epoch > epochBefore);
    TCHECK(writer.aba_rebinds >= 1 && writer.failed == 0 && writer.fatal == 0);

    /* Test 7: Concurrent 1E7CD0 retire between resolve and capture -> clean passthrough without false GW_CAPTURE fatal */
    retirePktSlab = sPkt;
    writer.resolve_target = TestResolveWithConcurrentRetire;
    retireOnCaptureHandle = squad[1].meshHandles[2][3];
    retireOnCaptureWorld = w1.rw->address;
    LONG64 prevCorrected = writer.corrected, prevFailed = writer.failed;
    InvokeActorMesh(&squad[1], 2, 3, 1, 0);
    writer.resolve_target = CoopResolveTarget;
    TCHECK(writer.corrected == prevCorrected && writer.failed == prevFailed && writer.fatal == 0);

    /* Production mode (no .arm file): diagnostics stay closed -> zero skin-invocation-*.bin disk spam & zero fatal=3! */
    writerPollAt = 0; writerHealthAt = 0;
    WriterTick(&mod, 0.016f, NULL);
    TCHECK(coopDiagnosticCapture == 0 && invocationFiles == 0u && shadowFail == 0u && leafFail == 0u && writer.fatal == 0);

    /* Test 8: Full >= 256 target slot table capacity + rejection */
    coop_registry fullReg;
    coop_registry_init(&fullReg);
    coop_player_entry capPlayers[4];
    for (uint32_t i = 0; i < 4; i++) {
        capPlayers[i] = (coop_player_entry){0xAA00ULL + i, i, 3u, 20u + i, i == 2, 0x900000ULL + i * 0x1000u, MakeHandle(1 + i, 1), 1};
    }
    coop_sync_players(&fullReg, capPlayers, 4);
    for (uint32_t k = 0; k < COOP_MAX_TARGETS; k++) {
        uint32_t p = k % 4u;
        coop_mesh_probe pr = {0};
        pr.mesh = 0xA00000ULL + k * 0x100ULL;
        pr.pool = 0xB00000ULL;
        pr.graph = 0xC00000ULL + k * 0x100ULL;
        pr.descriptor = 0xD00000ULL + k * 0x100ULL;
        pr.child_unit = 0xE00000ULL + (k / 4u) * 0x100ULL;
        pr.parent_unit = capPlayers[p].avatar_unit;
        pr.parent_unit_handle = capPlayers[p].avatar_handle;
        pr.parent_unit_gen = capPlayers[p].avatar_gen;
        pr.parent_node_count = 107u;
        pr.parent_unit_resource = COOP_HUMAN_AVATAR_RESOURCE;
        pr.garment_resource = COMPACT_IDS[(k / 4u) % 7u];
        pr.garment_ordinal = (k % 4u) + 1u;
        pr.render_handle = MakeHandle(300u + k, 1u);
        coop_actor_kind ak = coop_admit_mesh(&fullReg, &pr, NULL);
        TCHECK(ak == (p == 2 ? COOP_ACTOR_LOCAL_PLAYER : COOP_ACTOR_REMOTE_PLAYER));
    }
    TCHECK(fullReg.active_targets == COOP_MAX_TARGETS);
    coop_mesh_probe extra = {0};
    extra.mesh = 0xF00000ULL; extra.pool = 0xB00000ULL; extra.descriptor = 0xD10000ULL;
    extra.child_unit = 0xE90000ULL; extra.parent_unit = capPlayers[0].avatar_unit;
    extra.parent_unit_handle = capPlayers[0].avatar_handle; extra.parent_unit_gen = 1u;
    extra.parent_node_count = 107u; extra.parent_unit_resource = COOP_HUMAN_AVATAR_RESOURCE;
    extra.garment_resource = COMPACT_IDS[0]; extra.garment_ordinal = 1u; extra.render_handle = MakeHandle(999u, 1u);
    TCHECK(coop_admit_mesh(&fullReg, &extra, NULL) == COOP_ACTOR_REJECTED);
    TCHECK(fullReg.last_reject == COOP_FAIL_CAPACITY);
    TCHECK(coop_retire_handle(&fullReg, MakeHandle(300u, 1u)) == 1u);
    TCHECK(coop_admit_mesh(&fullReg, &extra, NULL) == COOP_ACTOR_REMOTE_PLAYER);

    /* Test 9: Sub-Run 1C ship->Hellpod regression (parent_graph_handle unresolvable & nodes != 107 -> retire 28 ship meshes -> spawn 28 ground meshes). */
    ResetMockUnitsAndWorld();
    for (unsigned s = 0; s < 4u; s++) {
        PutU32(sPlayerRow, 0x3A8u + s * 0x20u, 0x7FFFu);
    }
    MockWorld wShip = CreateMockWorld();
    MockWorld wGround = CreateMockWorld();
    static MockActor shipAvatar, groundAvatar;
    SpawnMockActor(&shipAvatar, &wShip, 0u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
    PutU64(sContext, 0xB398u, 0x7777ULL);
    PutU32(sPlayerRow, 0x84u, 1u);
    SetPlayerSlot(0, 0x7777ULL, 50u, 3u, 1, 500u, &shipAvatar);
    writerPollAt = 0;
    WriterTick(&mod, 0.016f, NULL);

    for (unsigned t = 0; t < 7u; t++) {
        for (unsigned j = 1; j <= 4u; j++) {
            InvokeActorMesh(&shipAvatar, t, j, 0, 1);
        }
    }
    TCHECK(writer.fatal == 0 && writer.failed == 0);
    TCHECK(coop_find_mesh(&coopReg, shipAvatar.meshes[1][2]->address) != NULL);

    /* 9a: Destroy shipAvatar parent RenderGraph handle (0x2036a1-equivalent) while UnitManager still holds parentUnit -> fast-path evicts cleanly, 0 substitutions, fatal == 0! */
    uint32_t shipParentIdx = shipAvatar.parentAppHandle & 0x7FFu;
    uint32_t savedShipParentHandle = shipAvatar.parentAppHandle;
    PutU32(wShip.handles, shipParentIdx * 4u, 0u);
    LONG64 baseCorrected = writer.corrected;
    LONG64 prevFastEvicts = writer.fast_evicts;
    InvokeActorMesh(&shipAvatar, 1, 2, 1, 0); /* target=1 ordinal=2 grouped (exact leaf-fault-first shape) */
    TCHECK(writer.fatal == 0 && writer.failed == 0 && writer.corrected == baseCorrected);
    TCHECK(writer.fast_evicts > prevFastEvicts && coop_find_mesh(&coopReg, shipAvatar.meshes[1][2]->address) == NULL);

    /* 9b: Restore handle temporarily, corrupt parent graph nodes (107 -> 106) -> fast-path also rejects & evicts cleanly! */
    PutU32(wShip.handles, shipParentIdx * 4u, savedShipParentHandle);
    PutU32(shipAvatar.parentGraph, 0x10u, 106u);
    PutU32(shipAvatar.parentGraph, 0x28u, 106u);
    prevFastEvicts = writer.fast_evicts;
    InvokeActorMesh(&shipAvatar, 0, 1, 0, 0);
    TCHECK(writer.fatal == 0 && writer.failed == 0 && writer.corrected == baseCorrected);
    TCHECK(writer.fast_evicts > prevFastEvicts && coop_find_mesh(&coopReg, shipAvatar.meshes[0][1]->address) == NULL);

    /* 9c: Restore nodes=107, unbind parentAppHandle mid-call AFTER CoopResolveTarget succeeds -> CoopCaptureTarget passthrough IV_RETIRED (no GW_CAPTURE fatal=3)! */
    PutU32(shipAvatar.parentGraph, 0x10u, 107u);
    PutU32(shipAvatar.parentGraph, 0x28u, 107u);
    /* Admit target=2 ordinal=1 cleanly first */
    InvokeActorMesh(&shipAvatar, 2, 1, 0, 1);
    TCHECK(writer.corrected == baseCorrected + 1 && coop_find_mesh(&coopReg, shipAvatar.meshes[2][1]->address) != NULL);
    LONG64 prevPassthrough = writer.passthrough_retired;
    LONG64 prevTeardownEvicts = writer.capture_teardown_evicts;
    unbindWorld = &wShip;
    unbindParentHandleOnResolve = savedShipParentHandle;
    writer.resolve_target = TestResolveWithUnbindParentGraph;
    InvokeActorMesh(&shipAvatar, 2, 1, 1, 0);
    writer.resolve_target = CoopResolveTarget;
    TCHECK(writer.fatal == 0 && writer.failed == 0 && writer.corrected == baseCorrected + 1);
    TCHECK(writer.passthrough_retired == prevPassthrough + 1 && writer.capture_teardown_evicts == prevTeardownEvicts + 1);
    TCHECK(coop_find_mesh(&coopReg, shipAvatar.meshes[2][1]->address) == NULL);

    /* 9d: Retire all 28 ship meshes cleanly and spawn 28 ground mission avatar meshes in wGround -> 100% corrections! */
    for (unsigned t = 0; t < 7u; t++) {
        for (unsigned j = 1; j <= 4u; j++) {
            PutU32(sPkt, 4u, shipAvatar.meshHandles[t][j]);
            _mm_setcsr(0x9F81u); SetLastError(0x1357u);
            RenderRemoveObserved((void *)wShip.rw->address, (const void *)sPkt->address, NULL);
            TCHECK(coop_find_mesh(&coopReg, shipAvatar.meshes[t][j]->address) == NULL);
        }
    }
    SpawnMockActor(&groundAvatar, &wGround, 16u, 2u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
    SetPlayerSlot(0, 0x7777ULL, 50u, 4u, 1, 501u, &groundAvatar);
    writerPollAt = 0;
    WriterTick(&mod, 0.016f, NULL);
    baseCorrected = writer.corrected;
    for (unsigned t = 0; t < 7u; t++) {
        for (unsigned j = 1; j <= 4u; j++) {
            InvokeActorMesh(&groundAvatar, t, j, 0, 1);
            InvokeActorMesh(&groundAvatar, t, j, 1, 1);
        }
    }
    TCHECK(writer.corrected == baseCorrected + 56 && writer.failed == 0 && writer.fatal == 0);

    /* Test 10: Concurrent unrelated UnitManager capacity/free-list change mid-CoopCaptureTarget -> correction SUCCEEDS with fatal == 0! */
    hookWatchMgrSlab = sMgr;
    hookWatchMgrAddr = sMgr->address + 0x98u;
    hookMgrHits = 0;
    testReadHook = HookMutateUnitMgrCount;
    baseCorrected = writer.corrected;
    InvokeActorMesh(&groundAvatar, 3, 2, 1, 1);
    testReadHook = NULL;
    TCHECK(hookMgrHits >= 2u && U32(sMgr->bytes + 0x98u) == 514u);
    TCHECK(writer.corrected == baseCorrected + 1 && writer.failed == 0 && writer.fatal == 0);

    /* Test 11: Concurrent RenderWorld.objects.length change (CaptureRenderClose) & lp_recheck pose mutation -> passthrough with fatal == 0, next frame recovers 100%! */
    PutU32(wGround.rw, 0x168u, 2040u);
    hookWatchWorldSlab = wGround.rw;
    hookWatchWorldAddr = wGround.rw->address + 0x168u;
    hookWorldHits = 0;
    testReadHook = HookMutateRenderWorldLen;
    prevPassthrough = writer.passthrough_retired;
    LONG64 prevMutSkips = writer.capture_mutation_skips;
    baseCorrected = writer.corrected;
    InvokeActorMesh(&groundAvatar, 4, 1, 0, 0);
    testReadHook = NULL;
    TCHECK(hookWorldHits >= 2u && U32(wGround.rw->bytes + 0x168u) == 2041u);
    TCHECK(writer.fatal == 0 && writer.failed == 0 && writer.corrected == baseCorrected);
    TCHECK(writer.passthrough_retired == prevPassthrough + 1 && writer.capture_mutation_skips == prevMutSkips + 1);
    TCHECK(coop_find_mesh(&coopReg, groundAvatar.meshes[4][1]->address) != NULL);
    InvokeActorMesh(&groundAvatar, 4, 1, 0, 1);
    TCHECK(writer.corrected == baseCorrected + 1 && writer.fatal == 0);

    /* 11b: Mid-sm_evaluate pose mutation causing lp_recheck failure -> passthrough without GW_RECHECK fatal, next frame recovers 100%! */
    hookWatchPoseSlab = groundAvatar.childPoses[5][0];
    hookWatchPoseOff = (size_t)COMPACT_BASES[5] * 48u;
    hookWatchPoseAddr = hookWatchPoseSlab->address + hookWatchPoseOff;
    uint32_t savedPoseWord = U32(hookWatchPoseSlab->bytes + hookWatchPoseOff);
    hookPoseHits = 0;
    testReadHook = HookMutatePoseDuringRecheck;
    LONG64 prevRecheckPass = writer.passthrough_recheck;
    baseCorrected = writer.corrected;
    InvokeActorMesh(&groundAvatar, 5, 1, 0, 0);
    testReadHook = NULL;
    TCHECK(hookPoseHits >= 2u && writer.fatal == 0 && writer.failed == 0 && writer.corrected == baseCorrected);
    TCHECK(writer.passthrough_recheck == prevRecheckPass + 1);
    PutU32(hookWatchPoseSlab, hookWatchPoseOff, savedPoseWord);
    InvokeActorMesh(&groundAvatar, 5, 1, 0, 1);
    TCHECK(writer.corrected == baseCorrected + 1 && writer.fatal == 0);

    /* Emit final health row confirming new telemetry counters format cleanly under 900 chars. */
    writerHealthAt = 0;
    WriterTick(&mod, 0.016f, NULL);
    TCHECK(strstr(lastLogLine, "health ") && strstr(lastLogLine, "passthrough=") &&
           strstr(lastLogLine, "recheck_pass=") && strstr(lastLogLine, "fast_evicts=") &&
           strstr(lastLogLine, "unit_cap=") && strstr(lastLogLine, "garments=") &&
           strstr(lastLogLine, "neg_hits=") && strstr(lastLogLine, "rej_mgr=") &&
           strstr(lastLogLine, "rej_world=") && strstr(lastLogLine, "rej_nomatch=") &&
           strstr(lastLogLine, "rej_meta=") && strstr(lastLogLine, "rej_parent=") &&
           strstr(lastLogLine, "rej_admit=") && strlen(lastLogLine) < 900u);

    /* Test 12: Sub-Run 1D post-teardown mission scale (UnitManager 2048 -> 32768 -> 32768 on ship return),
     * garments at 18500..18506, >40 parent siblings incl. non-VTABLE_RVA, EntityManager hash 16384 / desc 5000. */
    {
        char subDir12[1024];
        snprintf(subDir12, sizeof subDir12, "%s/test12", argv[2]);
        CreateDirectoryA(subDir12, NULL);
        InitMockHost(subDir12);
        sSlots = AllocSlab(65536u * 8u);
        sGens = AllocSlab(65536u);
        PutU32(sMgr, 0x98u, 2048u);
        PutU64(sMgr, 0x88u, sSlots->address);
        PutU64(sMgr, 0xA0u, sGens->address);

        PutU32(sPlayerRow, 0x84u, 0u);
        WriterTick(&mod, 0.016f, NULL);
        TCHECK(writer.ready == 1 && writer.unit_cap == 2048);

        MockWorld wShip12 = CreateMockWorld();
        MockWorld wMission12 = CreateMockWorld();
        MockWorld wReturn12 = CreateMockWorld();
        static MockActor ship12, mission12, return12;

        SpawnMockActor(&ship12, &wShip12, 10u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SetActorSelfHandles(&ship12);
        PutU64(sContext, 0xB398u, 0x8888ULL);
        PutU32(sPlayerRow, 0x84u, 1u);
        SetPlayerSlot(0, 0x8888ULL, 60u, 3u, 1, 600u, &ship12);
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);

        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                InvokeActorMesh(&ship12, t, j, 0, 1);
            }
        }
        TCHECK(coopReg.admissions == 28u && coopReg.active_targets == 28u);
        TCHECK(writer.corrected == 28 && writer.failed == 0 && writer.fatal == 0);

        /* 1E7CD0 retires all 28 ship targets on Hellpod launch. */
        MockSlab *sPkt12 = AllocSlab(16);
        PutU32(sPkt12, 4u, ship12.parentAppHandle);
        _mm_setcsr(0x9F81u); SetLastError(0x1357u);
        RenderRemoveObserved((void *)wShip12.rw->address, (const void *)sPkt12->address, NULL);
        TCHECK(coopReg.active_targets == 0u && writer.retires == 28);

        /* Mission load: raise UnitManager.capacity to 32768, fill 0..4999 with dummy non-garments
         * so cursor scan cannot reach 18500 in one 4096-slot slice, testing O(1) player garment discovery. */
        PutU32(sMgr, 0x98u, 32768u);
        MockSlab *dummyNonUnit = AllocSlab(0x200);
        PutU64(dummyNonUnit, 0u, MOCK_ENGINE + 0x1234560u);
        for (uint32_t i = 0; i < 5000u; i++) {
            PutU64(sSlots, i * 8u, dummyNonUnit->address);
            PutU8(sGens, i, 1u);
        }

        /* Mission EntityManager: hash capacity=16384, probe depth > 16 (24 collisions), descriptor index=5000. */
        sEntities = AllocSlab(0x1000000);
        sNetBuckets = AllocSlab(16384u * 8u);
        sEntBuckets = AllocSlab(16384u * 8u);
        memset(sNetBuckets->bytes, 0xFF, sNetBuckets->size);
        memset(sEntBuckets->bytes, 0xFF, sEntBuckets->size);
        PutU64(sEntities, 0xF22EC8u, sNetBuckets->address);
        PutU32(sEntities, 0xF22EC8u + 8u, 16384u);
        PutU32(sEntities, 0xF22EC8u + 12u, 0xFFFFFFFFu);
        PutU32(sEntities, 0xF22EC8u + 16u, 2u);
        PutU64(sEntities, 0xF1AEB0u, sEntBuckets->address);
        PutU32(sEntities, 0xF1AEB0u + 8u, 16384u);
        PutU32(sEntities, 0xF1AEB0u + 12u, 0xFFFFFFFFu);
        PutU32(sEntities, 0xF1AEB0u + 16u, 2u);

        SpawnMockActor(&mission12, &wMission12, 18499u, 3u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SetActorSelfHandles(&mission12);

        /* Add >40 parent siblings (41 extra + 7 garments = 48 siblings total) including non-VTABLE_RVA siblings! */
        MockSlab *extraSibs[41];
        for (unsigned k = 0; k < 41u; k++) {
            extraSibs[k] = AllocSlab(0x200);
            if (k % 3u == 0u) {
                PutU64(extraSibs[k], 0u, MOCK_ENGINE + 0x1234567u); /* non-VTABLE_RVA weapon/effect */
            } else if (k % 3u == 1u) {
                PutU64(extraSibs[k], 0u, 0u); /* non-Unit attachment without VTABLE_RVA */
            } else {
                PutU64(extraSibs[k], 0u, MOCK_ENGINE + VTABLE_RVA);
                PutU64(extraSibs[k], 0x1D0u, 0xCAFE0000ULL); /* unrelated parent */
            }
        }
        /* Prepend 20 siblings before childUnits[0], 1 non-VTABLE_RVA between [2]->[3], and 20 after childUnits[6]. */
        PutU64(mission12.parentUnit, 0x1D8u, extraSibs[0]->address);
        for (unsigned k = 0; k < 19u; k++) PutU64(extraSibs[k], 0x1E0u, extraSibs[k + 1u]->address);
        PutU64(extraSibs[19], 0x1E0u, mission12.childUnits[0]->address);
        PutU64(mission12.childUnits[2], 0x1E0u, extraSibs[20]->address);
        PutU64(extraSibs[20], 0x1E0u, mission12.childUnits[3]->address);
        PutU64(mission12.childUnits[6], 0x1E0u, extraSibs[21]->address);
        for (unsigned k = 21u; k < 40u; k++) PutU64(extraSibs[k], 0x1E0u, extraSibs[k + 1u]->address);
        PutU64(extraSibs[40], 0x1E0u, 0u);

        SetPlayerSlotEx(0, 0x8888ULL, 61u, 4u, 1, 601u, &mission12, 16384u, 5000u, 24u);
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);

        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                InvokeActorMesh(&mission12, t, j, 0, 1);
                InvokeActorMesh(&mission12, t, j, 1, 1);
                const coop_target_slot *ts = coop_find_mesh(&coopReg, mission12.meshes[t][j]->address);
                TCHECK(ts && ts->actor_kind == COOP_ACTOR_LOCAL_PLAYER && ts->child_index == 18500u + t);
            }
        }
        TCHECK(coopReg.admissions == 56u && coopReg.active_targets == 28u);
        TCHECK(writer.corrected == 84 && writer.failed == 0 && writer.fatal == 0);
        TCHECK(writer.unit_cap == 32768);

        /* Retire all 28 mission targets via 1E7CD0. */
        PutU32(sPkt12, 4u, mission12.parentAppHandle);
        _mm_setcsr(0x9F81u); SetLastError(0x1357u);
        RenderRemoveObserved((void *)wMission12.rw->address, (const void *)sPkt12->address, NULL);
        TCHECK(coopReg.active_targets == 0u && writer.retires == 56);

        /* Ship return: UnitManager.capacity STAYS 32768! Spawn return avatar and re-admit all 28. */
        SpawnMockActor(&return12, &wReturn12, 20000u, 4u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SetActorSelfHandles(&return12);
        SetPlayerSlotEx(0, 0x8888ULL, 62u, 3u, 1, 602u, &return12, 16384u, 5001u, 24u);
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                InvokeActorMesh(&return12, t, j, 0, 1);
                const coop_target_slot *ts = coop_find_mesh(&coopReg, return12.meshes[t][j]->address);
                TCHECK(ts && ts->actor_kind == COOP_ACTOR_LOCAL_PLAYER);
            }
        }
        TCHECK(coopReg.admissions == 84u && coopReg.active_targets == 28u);
        TCHECK(writer.corrected == 112 && writer.failed == 0 && writer.fatal == 0);
    }

    /* Test 13: Poison coopNegCache on all 28 meshes before u + 0x60 is populated -> populate & refresh -> recover 28/28. */
    {
        char subDir13[1024];
        snprintf(subDir13, sizeof subDir13, "%s/test13", argv[2]);
        CreateDirectoryA(subDir13, NULL);
        InitMockHost(subDir13);
        sSlots = AllocSlab(65536u * 8u);
        sGens = AllocSlab(65536u);
        PutU32(sMgr, 0x98u, 32768u);
        PutU64(sMgr, 0x88u, sSlots->address);
        PutU64(sMgr, 0xA0u, sGens->address);

        PutU32(sPlayerRow, 0x84u, 0u);
        WriterTick(&mod, 0.016f, NULL);
        TCHECK(writer.ready == 1);

        MockWorld w13 = CreateMockWorld();
        static MockActor actor13;
        SpawnMockActor(&actor13, &w13, 18499u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SetActorSelfHandles(&actor13);
        PutU64(sContext, 0xB398u, 0x9999ULL);
        PutU32(sPlayerRow, 0x84u, 1u);

        /* First remove actor13 garments from sSlots so sweep completes (coopScanComplete=1) & poisons coopNegCache. */
        uintptr_t savedChildPtrs[7];
        for (unsigned t = 0; t < 7u; t++) {
            savedChildPtrs[t] = actor13.childUnits[t]->address;
            PutU64(sSlots, actor13.childUnitIdx[t] * 8u, 0u);
        }
        SetPlayerSlot(0, 0x9999ULL, 70u, 3u, 1, 700u, &actor13);

        se_target tmpTgt;
        coop_actor_kind tmpKind;
        /* With 0 discovered garments, lock-free CoopFastFilter rejects immediately (zero UnitManager scans, zero lock/atomic writes). */
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                TCHECK(!CoopResolveTarget(&writer, (void *)actor13.meshes[t][j]->address,
                    (void *)(w13.rw->address + 0x318u), NULL, MOCK_ENGINE + 0x258E19u, 1, &tmpTgt, &tmpKind));
            }
        }
        TCHECK(writer.rej_nomatch == 0 && coopReg.active_targets == 0u);

        /* Now bind childUnits into sSlots WITHOUT u + 0x60 populated and trigger WriterTick ->
         * still 0 registered garments -> fast filter continues rejecting lock-free! */
        for (unsigned t = 0; t < 7u; t++) {
            PutU64(sSlots, actor13.childUnitIdx[t] * 8u, savedChildPtrs[t]);
            PutU32(actor13.childUnits[t], 0x64u, 0u);
            PutU64(actor13.childUnits[t], 0x68u, 0u);
        }
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                TCHECK(!CoopResolveTarget(&writer, (void *)actor13.meshes[t][j]->address,
                    (void *)(w13.rw->address + 0x318u), NULL, MOCK_ENGINE + 0x258E19u, 3, &tmpTgt, &tmpKind));
            }
        }
        TCHECK(coopReg.active_targets == 0u && writer.garments_cached == 0);

        /* Also re-poison coopNegCache explicitly on all 28 meshes right before populating u + 0x60,
         * then populate u + 0x60 and trigger WriterTick refresh/epoch advance -> verify 28/28 recover & admit! */
        uint32_t savedEpoch = coopNegEpoch;
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                coop_neg_insert(actor13.meshes[t][j]->address, actor13.childGraphHandle[t],
                    actor13.childGraphs[t]->address, actor13.skins[t][j]->address, w13.rw->address);
            }
        }
        for (unsigned t = 0; t < 7u; t++) {
            PutU32(actor13.childUnits[t], 0x64u, actor13.childGraphHandle[t]);
            PutU64(actor13.childUnits[t], 0x68u, w13.rm->address + 0x10u);
        }
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        TCHECK(coopNegEpoch != savedEpoch && writer.garments_cached == 7 && coopFast.handle_count == 7u);

        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1; j <= 4u; j++) {
                InvokeActorMesh(&actor13, t, j, 0, 1);
            }
        }
        TCHECK(coopReg.active_targets == 28u && coopReg.admissions == 28u);
        TCHECK(writer.corrected == 28 && writer.failed == 0 && writer.fatal == 0);

        writerHealthAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        TCHECK(strstr(lastLogLine, "unit_cap=32768") && strstr(lastLogLine, "garments=7") &&
               strstr(lastLogLine, "neg_hits=") && strstr(lastLogLine, "rej_nomatch=") &&
               strlen(lastLogLine) < 900u);
    }

    /* Test 14: Legacy !coopDynamicMode refusal boundaries remain untouched (8192 / 4096 / 16 / 2048 / 32 / non-VTABLE). */
    {
        char subDir14[1024];
        snprintf(subDir14, sizeof subDir14, "%s/test14", argv[2]);
        CreateDirectoryA(subDir14, NULL);
        InitMockHost(subDir14);
        sSlots = AllocSlab(32768u * 8u);
        sGens = AllocSlab(32768u);
        PutU64(sMgr, 0x88u, sSlots->address);
        PutU64(sMgr, 0xA0u, sGens->address);

        coopProbeActive = 1; coopProbeReads = coopProbeBytes = 0;
        Manager lm;
        PutU32(sMgr, 0x98u, CAPACITY_LIMIT);
        TCHECK(ManagerRead(&lm) && lm.capacity == CAPACITY_LIMIT);
        PutU32(sMgr, 0x98u, CAPACITY_LIMIT + 1u);
        TCHECK(!ManagerRead(&lm));
        TCHECK(CoopManagerRead(&lm) && lm.capacity == CAPACITY_LIMIT + 1u);
        PutU32(sMgr, 0x98u, COOP_UNIT_CAPACITY_LIMIT);
        TCHECK(CoopManagerRead(&lm) && lm.capacity == COOP_UNIT_CAPACITY_LIMIT);
        PutU32(sMgr, 0x98u, COOP_UNIT_CAPACITY_LIMIT + 1u);
        TCHECK(!CoopManagerRead(&lm));

        /* Legacy HashLookup vs CoopHashLookup */
        MockSlab *hHdr = AllocSlab(32);
        MockSlab *hBkt = AllocSlab(8192u * 8u);
        memset(hBkt->bytes, 0xFF, hBkt->size);
        PutU64(hHdr, 0u, hBkt->address);
        PutU32(hHdr, 8u, 8192u);
        PutU32(hHdr, 12u, 0xFFFFFFFFu);
        PutU32(hHdr, 16u, 2u);
        PutU32(hBkt, ((77u * 2u) % 8192u) * 8u, 77u);
        PutU32(hBkt, ((77u * 2u) % 8192u) * 8u + 4u, 3000u);
        Lookup lk;
        TCHECK(!HashLookup(hHdr->address, 77u, &lk));
        TCHECK(CoopHashLookup(hHdr->address, 77u, &lk) && lk.index == 3000u);
        PutU32(hBkt, ((77u * 2u) % 8192u) * 8u + 4u, COOP_DESCRIPTOR_LIMIT);
        TCHECK(!CoopHashLookup(hHdr->address, 77u, &lk));

        /* Legacy ChainRead vs CoopChainRead */
        MockSlab *pUnit = AllocSlab(0x200);
        MockSlab *cUnit = AllocSlab(0x200);
        MockSlab *nonUnit = AllocSlab(0x200);
        PutU64(pUnit, 0u, MOCK_ENGINE + VTABLE_RVA);
        PutU64(cUnit, 0u, MOCK_ENGINE + VTABLE_RVA);
        PutU64(cUnit, 0x1D0u, pUnit->address);
        PutU64(nonUnit, 0u, MOCK_ENGINE + 0x1234567u);
        PutU64(nonUnit, 0x1E0u, cUnit->address);

        Record lr;
        memset(&lr, 0, sizeof lr);
        lr.parent.id.unit = pUnit->address;
        lr.parent.firstChild = nonUnit->address;
        lr.child.id.unit = cUnit->address;
        TCHECK(ChainRead(&lr) && lr.chainStatus == 3u && !lr.reciprocal);
        memset(&lr, 0, sizeof lr);
        lr.parent.id.unit = pUnit->address;
        lr.parent.firstChild = nonUnit->address;
        lr.child.id.unit = cUnit->address;
        TCHECK(CoopChainRead(&lr) && lr.chainStatus == 0u && lr.reciprocal == 1u);

        /* Cycle detection & 256 overflow */
        PutU64(cUnit, 0x1E0u, nonUnit->address);
        memset(&lr, 0, sizeof lr);
        lr.parent.id.unit = pUnit->address;
        lr.parent.firstChild = nonUnit->address;
        lr.child.id.unit = cUnit->address;
        TCHECK(CoopChainRead(&lr) && lr.chainStatus == 2u);
        coopProbeActive = 0;
    }

    /* Test 15: Extensible JSON/config garment registry (lego-garments-schema.json) + Wave 2 Clone kit pipeline (token >= 56). */
    {
        char subDir15[1024], modCfgDir[1024], cfgFile[1024];
        snprintf(subDir15, sizeof subDir15, "%s/test15", argv[2]);
        snprintf(modCfgDir, sizeof modCfgDir, "%s/test15_mod", argv[2]);
        CreateDirectoryA(subDir15, NULL);
        CreateDirectoryA(modCfgDir, NULL);
        snprintf(cfgFile, sizeof cfgFile, "%s\\lego-garments.json", modCfgDir);

        char err[192] = {0};
        uint32_t loaded = 0;
        const char *badVer = "{\"schema_version\":2,\"garments\":[]}";
        TCHECK(!lg_load_garments_json(badVer, strlen(badVer), &loaded, err, sizeof err) && strstr(err, "schema_version"));
        const char *dupBaked = "{\"schema_version\":1,\"garments\":[{\"resource\":\"2BB094EDBC756963\",\"node_count\":21,\"aliases\":[],\"meshes\":[{\"ordinal\":1,\"mesh_node\":14}]}]}";
        TCHECK(!lg_load_garments_json(dupBaked, strlen(dupBaked), &loaded, err, sizeof err));
        const char *aliasCollision = "{\"schema_version\":1,\"garments\":[{\"resource\":\"C104E99900000001\",\"node_count\":21,\"aliases\":[{\"source\":2,\"node\":14}],\"meshes\":[{\"ordinal\":1,\"mesh_node\":14}]}]}";
        TCHECK(!lg_load_garments_json(aliasCollision, strlen(aliasCollision), &loaded, err, sizeof err));
        TCHECK(cm_garment_count() == 7u);

        const lp_asset *cloneTmpl = lp_asset_find(COMPACT_IDS[6], 1u);
        const lp_asset *pinnedTmpl = lp_asset_find(COMPACT_IDS[0], 1u);
        TCHECK(cloneTmpl && pinnedTmpl);

        /* Write Wave 2 Clone config file matching lego-garments-schema.json (structural garment 7 + pinned garment 8) */
        FILE *cf = fopen(cfgFile, "wb");
        TCHECK(cf != NULL);
        fprintf(cf, "{\n  \"schema_version\": 1,\n  \"kit_pack\": \"Wave 2 LEGO Clone Kits\",\n  \"garments\": [\n");
        fprintf(cf, "    {\n      \"resource\": \"C104E00100000001\",\n      \"name\": \"clone_501st_torso\",\n      \"faction\": \"clonedivers\",\n      \"node_count\": 29,\n");
        fprintf(cf, "      \"aliases\": [{\"source\":2,\"node\":22},{\"source\":3,\"node\":23},{\"source\":4,\"node\":24},{\"source\":5,\"node\":25},{\"source\":13,\"node\":26},{\"source\":14,\"node\":27},{\"source\":32,\"node\":28}],\n");
        fprintf(cf, "      \"meshes\": [\n");
        fprintf(cf, "        {\"ordinal\":1,\"mesh_node\":17,\"skin_mode\":\"structural\",\"bones\":%u,\"groups\":%u},\n", cloneTmpl->bones, cloneTmpl->groups);
        fprintf(cf, "        {\"ordinal\":2,\"mesh_node\":18,\"skin_mode\":\"structural\",\"bones\":%u,\"groups\":%u},\n", cloneTmpl->bones, cloneTmpl->groups);
        fprintf(cf, "        {\"ordinal\":3,\"mesh_node\":19,\"skin_mode\":\"structural\",\"bones\":%u,\"groups\":%u},\n", cloneTmpl->bones, cloneTmpl->groups);
        fprintf(cf, "        {\"ordinal\":4,\"mesh_node\":20,\"skin_mode\":\"structural\",\"bones\":%u,\"groups\":%u}\n", cloneTmpl->bones, cloneTmpl->groups);
        fprintf(cf, "      ]\n    },\n");
        fprintf(cf, "    {\n      \"resource\": \"C104E00200000002\",\n      \"name\": \"clone_arc_shoulder\",\n      \"faction\": \"clonedivers\",\n      \"node_count\": 21,\n");
        fprintf(cf, "      \"aliases\": [{\"source\":7,\"node\":18},{\"source\":8,\"node\":19},{\"source\":9,\"node\":20}],\n");
        fprintf(cf, "      \"meshes\": [{\"ordinal\":1,\"mesh_node\":14,\"skin_mode\":\"pinned\",\"bones\":%u,\"groups\":%u,\"map\":[", pinnedTmpl->bones, pinnedTmpl->groups);
        for (uint32_t i = 0; i < pinnedTmpl->bones; i++) fprintf(cf, "%s%u", i ? "," : "", pinnedTmpl->map[i]);
        fprintf(cf, "],\"bind_bits\":[");
        for (uint32_t i = 0; i < pinnedTmpl->bones * 12u; i++) fprintf(cf, "%s%u", i ? "," : "", pinnedTmpl->bind[i]);
        fprintf(cf, "],\"counts\":[");
        for (uint32_t g = 0; g < pinnedTmpl->groups; g++) fprintf(cf, "%s%u", g ? "," : "", pinnedTmpl->counts[g]);
        fprintf(cf, "],\"palette\":[");
        for (uint32_t g = 0; g < pinnedTmpl->groups; g++) {
            fprintf(cf, "%s[", g ? "," : "");
            for (uint32_t k = 0; k < pinnedTmpl->counts[g]; k++) fprintf(cf, "%s%u", k ? "," : "", pinnedTmpl->palette[g][k]);
            fprintf(cf, "]");
        }
        fprintf(cf, "]}]\n    }\n  ]\n}\n");
        fclose(cf);

        int cfgFound = 0;
        cm_reset_dynamic_garments(); lp_reset_dynamic_assets();
        TCHECK(lg_load_garments_file(cfgFile, &cfgFound, &loaded, err, sizeof err) && cfgFound == 1 && loaded == 2u);
        TCHECK(cm_garment_count() == 9u);
        uint32_t ck = 0;
        TCHECK(coop_is_compact_garment(0xC104E00100000001ULL, &ck) && ck == 7u);
        TCHECK(coop_is_compact_garment(0xC104E00200000002ULL, &ck) && ck == 8u);
        TCHECK(cm_mesh_node_for(7u, 1u) == 17u && cm_mesh_node_for(8u, 1u) == 14u);

        /* Point api.mod_folder at modCfgDir and verify hellforge_mod_init auto-loads lego-garments.json! */
        snprintf(mockModFolder, sizeof mockModFolder, "%s", modCfgDir);
        InitMockHost(subDir15);
        TCHECK(cm_garment_count() == 9u);
        TCHECK(coop_is_compact_garment(0xC104E00100000001ULL, &ck) && ck == 7u);
        TCHECK(coop_is_compact_garment(0xC104E00200000002ULL, &ck) && ck == 8u);
        MockWorld w15 = CreateMockWorld();
        static MockActor storm0, cloneHost;
        SpawnMockActor(&storm0, &w15, 10u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SpawnMockActor(&cloneHost, &w15, 30u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        /* Retarget cloneHost's t=6 unit to Clone kit 0xC104E00100000001ULL */
        PutU64(cloneHost.childRes[6], 0u, 0xC104E00100000001ULL);
        PutU64(sContext, 0xB398u, 77ULL);
        PutU32(sPlayerRow, 0x84u, 2u);
        SetPlayerSlot(0, 77ULL, 10u, 3u, 1, 100u, &cloneHost);
        SetPlayerSlot(1, 88ULL, 11u, 3u, 0, 101u, &storm0);

        WriterTick(&mod, 0.016f, NULL);
        TCHECK(InterlockedCompareExchange(&writer.ready, 0, 0) == 1);

        activeAsset = lp_asset_find(0xC104E00100000001ULL, 1u);
        TCHECK(activeAsset != NULL);
        expectedMesh = cloneHost.meshes[6][1]->address;
        expectedPool = cloneHost.world->rw->address + 0x318u;
        expectedNormalGraph = cloneHost.childGraphs[6]->address;
        expectedResource = 0xC104E00100000001ULL;
        expectedOrdinal = 1u;
        activeGrouped = 1;
        expectSubstitution = 1;
        activeDrawRef = (MeshDrawRef){cloneHost.skins[6][1], cloneHost.subrecs[6][1], cloneHost.binds[6][1], cloneHost.poolOut, {cloneHost.childPoses[6][0], cloneHost.childPoses[6][1]}};
        unsigned s6 = RetainedSampleForTarget(6u);
        memcpy(cloneHost.parentPoses[0]->bytes, retainedFiles[s6] + 256u + 13312u, 107u * 48u);
        memcpy(cloneHost.parentPoses[1]->bytes, retainedFiles[s6] + 256u + 18448u, 107u * 48u);
        _mm_setcsr(activeCsr); SetLastError(activeErr);
        uint32_t crc = gw_dispatch(&writer, (void *)expectedMesh, (void *)expectedPool, (void *)expectedNormalGraph, MOCK_ENGINE + 0x258AABu);
        TCHECK(crc == 0x10u && writer.corrected == 1 && writer.failed == 0 && writer.fatal == 0);
        const coop_target_slot *cslot = coop_find_mesh(&coopReg, expectedMesh);
        TCHECK(cslot != NULL && cslot->target_kind == 7u && cslot->target_kind * 8u + 1u == 57u);
        TCHECK(coopReg.targets[0].mesh != 0);

        /* Verify token=57 succeeds inside CaptureQueueScope & CaptureRenderClose when coopDynamicMode=1 */
        se_target dynTgt; coop_fill_se_target(cslot, &dynTgt);
        TCHECK(dynTgt.token == 57u);
        Record rec15; RenderMeta meta15; Owner own15;
        memset(&rec15, 0, sizeof rec15); memset(&meta15, 0, sizeof meta15); memset(&own15, 0, sizeof own15);
        TCHECK(MetaRead(&initial, cslot->child_index, &rec15.child, NODE_LIMIT));
        TCHECK(MetaRead(&initial, cslot->parent_index, &rec15.parent, PARENT_NODE_LIMIT));
        TCHECK(RenderRead(&rec15.child, &meta15));
        se_call call15 = {expectedMesh, expectedPool, expectedNormalGraph, MOCK_ENGINE + 0x258AABu, GetCurrentThreadId(), activeCsr, activeErr};
        se_observe_context ctx15 = {&invocationEnvelope, &dynTgt, &call15, NULL, 0};
        IvBudget b15 = {0}; b15.call = &ctx15;
        RenderMeta before15 = meta15, after15 = meta15;
        after15.header.queue ^= 0x100u;
        SavedTarget saved15; memset(&saved15, 0, sizeof saved15);
        saved15.child = rec15.child; saved15.parent = rec15.parent; saved15.render = meta15;
        writerCaptureActive = 1; invocationBudget = &b15;
        TCHECK(CaptureQueueScope(cslot->child_index, &saved15, NULL) == 1);
        TCHECK(CaptureRenderClose(cslot->child_index, &saved15, NULL, &rec15, &before15, &after15, 7u) == 1);
        writerCaptureActive = 0; invocationBudget = NULL;

        /* Verify 256-garment / 512-asset capacity headroom + t >= 32 (token >= 256) drain safety */
        TCHECK(CM_MAX_GARMENTS == 256u && COOP_MAX_GARMENTS == 256u && LP_MAX_DYN_ASSETS == 512u);
        cm_reset_dynamic_garments();
        lp_reset_dynamic_assets();
        for (uint32_t g = 0; g < 249u; g++) {
            cm_alias ga[1] = {{1u, 16u}};
            uint32_t mn[4] = {12u, g < 249u ? 13u : UINT32_MAX, UINT32_MAX, UINT32_MAX};
            uint64_t gid = 0xC200000000000000ULL + (uint64_t)(g + 1u);
            TCHECK(cm_register_dynamic_garment(gid, 17u, 1u, ga, mn) == 1);
            lp_asset ma;
            memset(&ma, 0, sizeof ma);
            ma.resource = gid; ma.ordinal = 1u; ma.bones = 1u; ma.groups = 1u;
            TCHECK(lp_register_asset(&ma, 0u) == 1);
            ma.ordinal = 2u;
            TCHECK(lp_register_asset(&ma, 0u) == 1);
        }
        TCHECK(cm_garment_count() == 256u);
        {
            cm_alias extraAlias[1] = {{1u, 16u}};
            uint32_t extraNodes[4] = {12u, UINT32_MAX, UINT32_MAX, UINT32_MAX};
            TCHECK(cm_register_dynamic_garment(0xC2FFFFFFFFFFFFFFULL, 17u, 1u, extraAlias, extraNodes) == 0);
            /* Remaining 14 asset slots up to 512 (249*2 = 498 registered so far) */
            for (uint32_t extra = 0; extra < 14u; extra++) {
                lp_asset ma;
                memset(&ma, 0, sizeof ma);
                ma.resource = 0xC200000000000001ULL + extra;
                ma.ordinal = 3u; ma.bones = 1u; ma.groups = 1u;
                TCHECK(lp_register_asset(&ma, 0u) == 1);
            }
            lp_asset overAsset;
            memset(&overAsset, 0, sizeof overAsset);
            overAsset.resource = 0xC2000000000000FFULL;
            overAsset.ordinal = 3u; overAsset.bones = 1u; overAsset.groups = 1u;
            TCHECK(lp_register_asset(&overAsset, 0u) == 0);
        }

        /* Also exercise high garment index t = 60 (>= 32) through full dispatch + WriterTick drain */
        cm_reset_dynamic_garments();
        lp_reset_dynamic_assets();
        for (uint32_t g = 0; g < 54u; g++) {
            cm_alias ga[7] = {{2,22},{3,23},{4,24},{5,25},{13,26},{14,27},{32,28}};
            uint32_t mn[4] = {17u, 18u, 19u, 20u};
            uint64_t gid = 0xC300000000000000ULL + (uint64_t)(g + 1u);
            TCHECK(cm_register_dynamic_garment(gid, 29u, 7u, ga, mn) == 1);
            for (uint32_t ord = 1u; ord <= 4u; ord++) {
                lp_asset ma;
                memset(&ma, 0, sizeof ma);
                ma.resource = gid; ma.ordinal = ord;
                ma.bones = cloneTmpl->bones; ma.groups = cloneTmpl->groups;
                TCHECK(lp_register_asset(&ma, 0u) == 1);
            }
        }
        TCHECK(cm_garment_count() == 61u);
        {
            uint64_t highId = 0xC300000000000036ULL; /* g=53 -> target_kind = 60, token = 481 */
            TCHECK(coop_is_compact_garment(highId, &ck) && ck == 60u);
            PutU64(cloneHost.childRes[6], 0u, highId);
            PutU8(sGens, cloneHost.childUnitIdx[6], 2u);
            cloneHost.childGen[6] = 2u;
            PutU32(cloneHost.childUnits[6], 8u, (2u << 22) | (cloneHost.childUnitIdx[6] & 0x3FFFFFu));
            writerPollAt = 0;
            WriterTick(&mod, 0.016f, NULL);
            activeAsset = lp_asset_find(highId, 1u);
            TCHECK(activeAsset != NULL);
            expectedMesh = cloneHost.meshes[6][1]->address;
            expectedPool = cloneHost.world->rw->address + 0x318u;
            expectedNormalGraph = cloneHost.childGraphs[6]->address;
            expectedResource = highId;
            expectedOrdinal = 1u;
            activeGrouped = 1;
            expectSubstitution = 1;
            activeDrawRef = (MeshDrawRef){cloneHost.skins[6][1], cloneHost.subrecs[6][1], cloneHost.binds[6][1], cloneHost.poolOut, {cloneHost.childPoses[6][0], cloneHost.childPoses[6][1]}};
            _mm_setcsr(activeCsr); SetLastError(activeErr);
            uint32_t hrc = gw_dispatch(&writer, (void *)expectedMesh, (void *)expectedPool, (void *)expectedNormalGraph, MOCK_ENGINE + 0x258AABu);
            TCHECK(hrc == 0x10u && writer.corrected == 2 && writer.failed == 0 && writer.fatal == 0);
            const coop_target_slot *hslot = coop_find_mesh(&coopReg, expectedMesh);
            TCHECK(hslot != NULL && hslot->target_kind == 60u && hslot->target_kind * 8u + 1u == 481u);
            WriterTick(&mod, 0.016f, NULL);
            TCHECK(writer.fatal == 0 && shadowFail == 0 && leafFail == 0);
        }

        /* Validate production lego-garments.json (54 Wave 2A Clone garments, 216 pinned descriptors) */
        {
            int prodFound = 0;
            uint32_t prodLoaded = 0;
            const char *prodPath = "lego-garments.json";
            FILE *probeProd = fopen(prodPath, "rb");
            if (probeProd) {
                fclose(probeProd);
            } else {
                prodPath = "../lego-garments.json";
            }
            cm_reset_dynamic_garments();
            lp_reset_dynamic_assets();
            TCHECK(lg_load_garments_file(prodPath, &prodFound, &prodLoaded, err, sizeof err));
            TCHECK(prodFound == 1 && prodLoaded == 54u && cm_garment_count() == 61u);
            for (uint32_t gi = 7u; gi < 61u; gi++) {
                const cm_garment *cg = cm_garment_at(gi);
                TCHECK(cg != NULL && cg->count >= 1u && cg->count <= 128u);
                uint64_t gRes = strtoull(cg->resource, NULL, 16);
                uint32_t foundIdx = 0;
                TCHECK(cm_garment_find(gRes, &foundIdx) && foundIdx == gi);
                for (uint32_t ord = 1u; ord <= 4u; ord++) {
                    uint32_t mNode = cm_mesh_node_for(gi, ord);
                    TCHECK(mNode < cg->count);
                    const lp_ext_asset *ea = lp_asset_find_ext(gRes, ord);
                    TCHECK(ea != NULL && ea->pin_mask == LP_PIN_ALL && ea->asset.bones >= 1u && ea->asset.groups >= 1u);
                }
            }
        }

        cm_reset_dynamic_garments();
        lp_reset_dynamic_assets();
        mockModFolder[0] = 0;
    }

    /* Test 16: Explicit 3-Gate Co-op & Recycling Stress (4-player OwnerRead, multi-owner duplicate resources, >300 respawns/previews across 256 slots). */
    {
        char subDir16[1024];
        mockModFolder[0] = 0;
        snprintf(subDir16, sizeof subDir16, "%s/test16", argv[2]);
        CreateDirectoryA(subDir16, NULL);
        InitMockHost(subDir16);
        MockWorld w16 = CreateMockWorld();
        static MockActor pActors[4], previews[2];
        for (unsigned p = 0; p < 4u; p++) {
            SpawnMockActor(&pActors[p], &w16, p * 8u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        }
        PutU64(sContext, 0xB398u, 44ULL);
        PutU32(sPlayerRow, 0x84u, 4u);
        SetPlayerSlot(0, 11ULL, 10u, 3u, 0, 100u, &pActors[0]);
        SetPlayerSlot(1, 22ULL, 11u, 3u, 0, 101u, &pActors[1]);
        SetPlayerSlot(2, 33ULL, 12u, 3u, 0, 102u, &pActors[2]);
        SetPlayerSlot(3, 44ULL, 13u, 3u, 1, 103u, &pActors[3]); /* local player at non-zero slot 3 */

        WriterTick(&mod, 0.016f, NULL);
        TCHECK(InterlockedCompareExchange(&writer.ready, 0, 0) == 1);

        /* Gate 1: OwnerRead accepts count == 4 (no count == 1 gate) and selects local slot 3 peer 44 */
        Owner own16;
        TCHECK(OwnerRead(&own16) && own16.count == 4u && own16.rowPeer == 44ULL);

        /* Gate 2: All 4 co-op players + 2 Armory/Pod previews share IDENTICAL Stormtrooper resource hashes with zero duplicate_target_resource abort */
        SpawnMockActor(&previews[0], &w16, 40u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SpawnMockActor(&previews[1], &w16, 48u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        for (unsigned p = 0; p < 4u; p++) {
            for (unsigned t = 0; t < 7u; t++) {
                for (unsigned j = 1u; j <= 4u; j++) InvokeActorMesh(&pActors[p], t, j, 1, 1);
            }
        }
        for (unsigned a = 0; a < 2u; a++) {
            for (unsigned t = 0; t < 7u; t++) {
                for (unsigned j = 1u; j <= 4u; j++) InvokeActorMesh(&previews[a], t, j, 0, 1);
            }
        }
        TCHECK(coopReg.active_targets == 168u && duplicateResources == 0u);

        /* Gate 3: Retire 112 targets via 1E7CD0 RenderRemoveObserved and spawn/admit 448 more respawn slots -> 616 lifetime admissions cleanly inside 256-slot cap! */
        MockSlab *pkt = AllocSlab(16);
        for (unsigned p = 0; p < 4u; p++) {
            PutU32(pkt, 4u, pActors[p].parentAppHandle);
            _mm_setcsr(0x9F81u); SetLastError(0x1357u);
            RenderRemoveObserved((void *)w16.rw->address, (const void *)pkt->address, NULL);
        }
        TCHECK(coopReg.active_targets == 56u);

        for (unsigned wave = 0; wave < 4u; wave++) {
            static MockActor resp[4];
            uint32_t baseSlot = 64u + wave * 32u;
            for (unsigned p = 0; p < 4u; p++) {
                SpawnMockActor(&resp[p], &w16, baseSlot + p * 8u, (uint8_t)(2u + wave), COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
                SetPlayerSlot(p, 11ULL * (p + 1u), 10u + p, 3u, p == 3u, 100u + p, &resp[p]);
                writerPollAt = 0;
                WriterTick(&mod, 0.016f, NULL);
                for (unsigned t = 0; t < 7u; t++) {
                    for (unsigned j = 1u; j <= 4u; j++) InvokeActorMesh(&resp[p], t, j, 1, 1);
                }
                TCHECK(coopReg.active_targets <= COOP_MAX_TARGETS);
                PutU32(pkt, 4u, resp[p].parentAppHandle);
                _mm_setcsr(0x9F81u); SetLastError(0x1357u);
                RenderRemoveObserved((void *)w16.rw->address, (const void *)pkt->address, NULL);
            }
        }
        TCHECK(coopReg.admissions == 616u && coopReg.active_targets == 56u && writer.fatal == 0);
    }

    /* Test 17: D11 262,144-slot UnitManager Zero-Lock / Zero-Scan Performance Regression Guard. */
    {
        char subDir17[1024];
        snprintf(subDir17, sizeof subDir17, "%s/test17", argv[2]);
        CreateDirectoryA(subDir17, NULL);
        InitMockHost(subDir17);
        sSlots = AllocSlab(COOP_UNIT_CAPACITY_LIMIT * 8u);
        sGens = AllocSlab(COOP_UNIT_CAPACITY_LIMIT);
        PutU32(sMgr, 0x98u, COOP_UNIT_CAPACITY_LIMIT);
        PutU64(sMgr, 0x88u, sSlots->address);
        PutU64(sMgr, 0xA0u, sGens->address);
        PutU32(sPlayerRow, 0x84u, 0u);
        WriterTick(&mod, 0.016f, NULL);
        TCHECK(writer.ready == 1 && writer.unit_cap == COOP_UNIT_CAPACITY_LIMIT);

        MockWorld w17 = CreateMockWorld();
        static MockActor d11Player, d11Foreign;
        /* Foreign enemy mesh with 0 garments active -> 5,000 calls do EXACTLY 0 reads! */
        SpawnMockActor(&d11Foreign, &w17, 100u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 0);
        perfTestReadCount = 0; perfTestReadBytes = 0;
        testReadHook = PerfCountReadHook;
        for (unsigned i = 0; i < 5000u; i++) {
            se_target t17; coop_actor_kind k17;
            TCHECK(!CoopResolveTarget(&writer, (void *)d11Foreign.meshes[0][1]->address,
                (void *)(w17.rw->address + 0x318u), NULL, MOCK_ENGINE + 0x258E19u, (LONG64)i, &t17, &k17));
        }
        testReadHook = NULL;
        TCHECK(perfTestReadCount == 0u && perfTestReadBytes == 0u);

        /* Now spawn a LEGO player at slot 180,000 (far past the 4,096 slice) -> WriterTick discovers in O(1)
         * and reads <= 64 KB total (incremental slice, NEVER 2.35 MB)! */
        SpawnMockActor(&d11Player, &w17, 180000u, 1u, COOP_HUMAN_AVATAR_RESOURCE, 107u, 1);
        SetActorSelfHandles(&d11Player);
        PutU64(sContext, 0xB398u, 0xD110ULL);
        PutU32(sPlayerRow, 0x84u, 1u);
        SetPlayerSlot(0, 0xD110ULL, 80u, 3u, 1, 800u, &d11Player);
        perfTestReadCount = 0; perfTestReadBytes = 0;
        testReadHook = PerfCountReadHook;
        writerPollAt = 0;
        WriterTick(&mod, 0.016f, NULL);
        testReadHook = NULL;
        TCHECK(writer.garments_cached == 7 && coopFast.handle_count == 7u);
        TCHECK(perfTestReadBytes <= 196608u); /* bounded 16384-slot slice (~144 KB), never full 2.35 MB copy! */

        /* Verify all 28 player LEGO meshes still substitute cleanly */
        for (unsigned t = 0; t < 7u; t++) {
            for (unsigned j = 1u; j <= 4u; j++) {
                InvokeActorMesh(&d11Player, t, j, 0, 1);
            }
        }
        TCHECK(coopReg.active_targets == 28u && writer.corrected == 28 && writer.fatal == 0);

        /* Now run 5,000 non-LEGO enemy/SEAF 512B80 calls with LEGO active -> EXACTLY 1 x 4-byte read each! */
        perfTestReadCount = 0; perfTestReadBytes = 0;
        testReadHook = PerfCountReadHook;
        for (unsigned i = 0; i < 5000u; i++) {
            se_target t17; coop_actor_kind k17;
            TCHECK(!CoopResolveTarget(&writer, (void *)d11Foreign.meshes[0][1]->address,
                (void *)(w17.rw->address + 0x318u), NULL, MOCK_ENGINE + 0x258E19u, (LONG64)i, &t17, &k17));
        }
        testReadHook = NULL;
        TCHECK(perfTestReadCount == 5000u && perfTestReadBytes == 20000u);
    }

    if (invocationDirectoryHandle != INVALID_HANDLE_VALUE) {
        CloseHandle(invocationDirectoryHandle);
        invocationDirectoryHandle = INVALID_HANDLE_VALUE;
    }

    printf("PASS %u coop multi-actor checks (112 squad meshes x2 routes, slot1 local, preview/corpse, 1E7CD0 retire, ABA rebind, %u-slot cap, Wave2 JSON Clone kits, 616-admission recycling)\n",
        testChecks, COOP_MAX_TARGETS);
    return 0;
}
