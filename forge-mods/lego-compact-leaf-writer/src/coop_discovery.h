/* Dynamic multi-actor co-op target discovery, differential UnitManager reverse-join,
 * negative cache, PlayerManager 0..4 correlation, ABA rebinding, and 1E7CD0 handle retire.
 * All state lives in static BSS and is serialized by writer.metadata SRWLock. */
#ifndef COOP_DISCOVERY_H
#define COOP_DISCOVERY_H

#include <math.h>

#define COOP_GARMENT_CACHE_LIMIT 256u
#define COOP_NEG_CACHE_LIMIT 2048u
#define COOP_BULK_CHUNK 4096u
#define COOP_SCAN_SLICE_LIMIT 4096u
#define COOP_NEW_UNIT_PROBE_BUDGET 96u
#define COOP_FAST_HT_SIZE 512u
#define COOP_FAST_HT_MASK (COOP_FAST_HT_SIZE - 1u)
#define COOP_HANDLE_PROBE_WINDOW_MS 500ull

typedef struct CoopGarmentUnit {
    uint32_t index;
    uint32_t target_kind;
    uint32_t app_graph_handle;
    uint8_t generation;
    uint8_t meshes_resolved;
    uintptr_t unit;
    uintptr_t association;
    uint64_t resource;
    uint64_t discovered_ms;
    uintptr_t lod_meshes[4];
} CoopGarmentUnit;

typedef struct CoopSlotCache {
    uint32_t admit_epoch;
    int valid;
    uintptr_t parent_normal_graph;
    uintptr_t handles_base;
    uint32_t handles_mask;
    uint32_t child_nodes;
    uint32_t mesh_node;
    uint32_t groups;
    uint32_t output_count;
    uintptr_t skin;
    uintptr_t bind;
    uintptr_t sub;
    uintptr_t pool_base;
    uintptr_t map;
    lp_group group[LP_GROUPS];
} CoopSlotCache;

typedef struct CoopNegEntry {
    uintptr_t mesh;
    uintptr_t cache;
    uintptr_t skin;
    uintptr_t world;
    uint32_t handle;
    uint32_t epoch;
} CoopNegEntry;

#define COOP_POSE_CACHE_SIZE 32u
#define COOP_POSE_CACHE_MASK (COOP_POSE_CACHE_SIZE - 1u)
typedef struct CoopParentPoseEntry {
    SRWLOCK lock;
    int valid;
    uintptr_t parent_graph;
    uintptr_t arr_ptr;
    cm_render48 parent_in[IV_PARENT_NODES];
    cm_render48 source_packed[CM_SOURCE];
} __attribute__((aligned(64))) CoopParentPoseEntry;

/* Note: coopPoseCache is not keyed on s->rig because s->rig is process-constant (initialized once in hellforge_mod_init). */
static coop_registry coopReg;
static CoopSlotCache coopSlotCache[COOP_MAX_TARGETS];
static CoopParentPoseEntry coopPoseCache[COOP_POSE_CACHE_SIZE];
static void InvocationClearFailures(void);
static Manager coopLastMgr;
static uintptr_t coopRawSlots[COOP_UNIT_CAPACITY_LIMIT], coopScannedSlots[COOP_UNIT_CAPACITY_LIMIT];
static uint8_t coopRawGens[COOP_UNIT_CAPACITY_LIMIT], coopScannedGens[COOP_UNIT_CAPACITY_LIMIT];
static CoopGarmentUnit coopGarments[COOP_GARMENT_CACHE_LIMIT];
static uint32_t coopGarmentCount;

static void coop_reopen_garment_probe(uintptr_t child_unit, uint32_t child_gen) {
    if (!child_unit) return;
    uint64_t nowMs = GetTickCount64();
    for (uint32_t g = 0; g < coopGarmentCount; g++) {
        if (coopGarments[g].unit == child_unit && coopGarments[g].generation == (uint8_t)child_gen) {
            coopGarments[g].discovered_ms = nowMs;
            coopGarments[g].meshes_resolved = 0u;
            memset(coopGarments[g].lod_meshes, 0, sizeof coopGarments[g].lod_meshes);
        }
    }
}
static CoopNegEntry coopNegCache[COOP_NEG_CACHE_LIMIT];
static uint32_t coopNegEpoch = 1u;
static uint32_t coopScanCursor;
static int coopScanComplete;
static CoopRoster coopCachedRoster;
static uint64_t coopLastPeers[COOP_MAX_PLAYERS];
static uint32_t coopLastNetworks[COOP_MAX_PLAYERS], coopLastLifes[COOP_MAX_PLAYERS];
static uint32_t coopLastPlayerCount;
static uint64_t coopLastLocalPeer;
static int coopHasRosterSnapshot;

/* Lock-free seqlock O(1) hash fast filter on its own 64-byte cache line.
 * In steady state (need_handle_probe == 0), 99.95%+ non-LEGO 512B80 calls reject in ~3ns
 * with ZERO hf->read() calls, ZERO locks, and ZERO shared atomic writes. */
typedef struct CoopFastFilter {
    volatile uint32_t seq; /* even = stable, odd = updating */
    uint32_t handle_count;
    uint32_t retire_handle_count;
    uint32_t mesh_count;
    uint32_t need_handle_probe;
    uintptr_t mesh_ht[COOP_FAST_HT_SIZE];
    uint16_t mesh_ht_slot[COOP_FAST_HT_SIZE];
    uint32_t handle_ht[COOP_FAST_HT_SIZE];
    uint32_t handles[COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS];
    uint32_t retire_handles[COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS * 3u];
    uintptr_t meshes[COOP_MAX_TARGETS];
} __attribute__((aligned(64))) CoopFastFilter;
static CoopFastFilter coopFast;

static uint32_t coop_hash_ptr(uintptr_t v) {
    uint64_t x = (uint64_t)v;
    x ^= x >> 33;
    x *= 0xff51afd7ed558ccdULL;
    x ^= x >> 33;
    return (uint32_t)(x & COOP_FAST_HT_MASK);
}

static uint32_t coop_hash_u32(uint32_t v) {
    uint32_t x = v * 0x9E3779B1u;
    x ^= x >> 16;
    return x & COOP_FAST_HT_MASK;
}

static void coop_fast_add_u32(uint32_t *arr, uint32_t *count, uint32_t cap, uint32_t val) {
    if (!val || val == 0xFFFFFFu) return;
    for (uint32_t i = 0; i < *count; i++) {
        if (arr[i] == val) return;
    }
    if (*count < cap) arr[(*count)++] = val;
}

static void coop_fast_publish(void) {
    /* Caller holds writer.metadata exclusive lock (or single-threaded init). */
    uint32_t s = __atomic_load_n(&coopFast.seq, __ATOMIC_RELAXED);
    __atomic_store_n(&coopFast.seq, s + 1u, __ATOMIC_RELEASE); /* odd: writer in progress */
    __atomic_thread_fence(__ATOMIC_RELEASE);
    uint32_t hc = 0u, rhc = 0u, mc = 0u;
    const uint32_t hcap = COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS;
    const uint32_t rcap = COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS * 3u;
    memset(coopFast.mesh_ht, 0, sizeof coopFast.mesh_ht);
    memset(coopFast.mesh_ht_slot, 0, sizeof coopFast.mesh_ht_slot);
    memset(coopFast.handle_ht, 0, sizeof coopFast.handle_ht);
    for (uint32_t i = 0; i < coopGarmentCount; i++) {
        coop_fast_add_u32(coopFast.handles, &hc, hcap, coopGarments[i].app_graph_handle);
        coop_fast_add_u32(coopFast.retire_handles, &rhc, rcap, coopGarments[i].app_graph_handle);
    }
    for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
        const coop_target_slot *ts = &coopReg.targets[i];
        if (!ts->active) continue;
        coop_fast_add_u32(coopFast.handles, &hc, hcap, ts->child_graph_handle);
        coop_fast_add_u32(coopFast.retire_handles, &rhc, rcap, ts->child_graph_handle);
        coop_fast_add_u32(coopFast.retire_handles, &rhc, rcap, ts->parent_graph_handle);
        coop_fast_add_u32(coopFast.retire_handles, &rhc, rcap, ts->render_handle);
        if (mc < COOP_MAX_TARGETS && ts->mesh) {
            coopFast.meshes[mc++] = ts->mesh;
            uint32_t idx = coop_hash_ptr(ts->mesh);
            for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
                uint32_t slot = (idx + p) & COOP_FAST_HT_MASK;
                if (!coopFast.mesh_ht[slot] || coopFast.mesh_ht[slot] == ts->mesh) {
                    coopFast.mesh_ht[slot] = ts->mesh;
                    coopFast.mesh_ht_slot[slot] = (uint16_t)i;
                    break;
                }
            }
        }
    }
    int preMeshOverflow = 0;
    for (uint32_t g = 0; g < coopGarmentCount; g++) {
        const CoopGarmentUnit *gu = &coopGarments[g];
        if (!gu->meshes_resolved) continue;
        for (uint32_t k = 0; k < 4u; k++) {
            uintptr_t mobj = gu->lod_meshes[k];
            if (!mobj) continue;
            int exists = 0;
            uint32_t idx = coop_hash_ptr(mobj);
            for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
                uint32_t slot = (idx + p) & COOP_FAST_HT_MASK;
                if (!coopFast.mesh_ht[slot]) {
                    if (mc < COOP_MAX_TARGETS) {
                        coopFast.meshes[mc++] = mobj;
                        coopFast.mesh_ht[slot] = mobj;
                        coopFast.mesh_ht_slot[slot] = (uint16_t)COOP_MAX_TARGETS;
                    } else {
                        preMeshOverflow = 1;
                    }
                    exists = 1;
                    break;
                }
                if (coopFast.mesh_ht[slot] == mobj) {
                    exists = 1;
                    break;
                }
            }
            if (!exists) preMeshOverflow = 1;
        }
    }
    for (uint32_t i = 0; i < hc; i++) {
        uint32_t h = coopFast.handles[i];
        uint32_t idx = coop_hash_u32(h);
        for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
            uint32_t slot = (idx + p) & COOP_FAST_HT_MASK;
            if (!coopFast.handle_ht[slot] || coopFast.handle_ht[slot] == h) {
                coopFast.handle_ht[slot] = h;
                break;
            }
        }
    }
    /* need_handle_probe:
     *   2 = immediate full handle probe (garment <5s old whose LOD MeshObjects are not yet resolved)
     *   1 = 1-in-64 TLS-sampled probe (garment >5s old whose LOD MeshObjects are still unresolved or overflowed)
     *   0 = zero hf->read() steady state (all live garments have their LOD MeshObjects pre-published in mesh_ht) */
    uint64_t nowMs = GetTickCount64();
    uint32_t needProbe = 0u;
    for (uint32_t g = 0; g < coopGarmentCount; g++) {
        if (!coopGarments[g].meshes_resolved || preMeshOverflow) {
            uint64_t windowMs = (mc > 0u) ? 250ull : COOP_HANDLE_PROBE_WINDOW_MS;
            if ((nowMs - coopGarments[g].discovered_ms) < windowMs) {
                needProbe = 2u;
                break;
            }
            if (needProbe < 1u) needProbe = 1u;
        }
    }
    __atomic_store_n(&coopFast.handle_count, hc, __ATOMIC_RELAXED);
    __atomic_store_n(&coopFast.retire_handle_count, rhc, __ATOMIC_RELAXED);
    __atomic_store_n(&coopFast.mesh_count, mc, __ATOMIC_RELAXED);
    __atomic_store_n(&coopFast.need_handle_probe, needProbe, __ATOMIC_RELAXED);
    __atomic_store_n(&coopFast.seq, s + 2u, __ATOMIC_RELEASE); /* even: consistent snapshot published */
}

static int coop_fast_may_match_skin(uintptr_t mesh, int sampled) {
    const uint32_t hcap = COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS;
    for (unsigned attempt = 0; attempt < 4u; attempt++) {
        uint32_t seq1 = __atomic_load_n(&coopFast.seq, __ATOMIC_ACQUIRE);
        if (seq1 & 1u) return 1; /* Writer updating; fall through to locked path. */
        uint32_t hc = __atomic_load_n(&coopFast.handle_count, __ATOMIC_RELAXED);
        uint32_t mc = __atomic_load_n(&coopFast.mesh_count, __ATOMIC_RELAXED);
        uint32_t probe = __atomic_load_n(&coopFast.need_handle_probe, __ATOMIC_RELAXED);
        if (hc > hcap || mc > COOP_MAX_TARGETS) continue;
        if (hc == 0u && mc == 0u) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&coopFast.seq, __ATOMIC_RELAXED) == seq1) return 0;
            continue;
        }
        int matched = 0;
        if (mc > 0u) {
            uint32_t idx = coop_hash_ptr(mesh);
            for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
                uintptr_t entry = __atomic_load_n(&coopFast.mesh_ht[(idx + p) & COOP_FAST_HT_MASK], __ATOMIC_RELAXED);
                if (!entry) break;
                if (entry == mesh) { matched = 1; break; }
            }
        }
        if (!matched && probe && hc > 0u) {
            int doProbe = (!sampled || probe >= 2u);
            if (!doProbe) {
                static _Thread_local uint32_t tls_late_probe;
                doProbe = ((++tls_late_probe & 63u) == 0u);
            }
            if (doProbe) {
                DWORD savedErr = GetLastError();
                uint32_t m_handle = 0u;
                int rdErr = (!hf || !hf->read || hf->read(mesh + 0x34u, &m_handle, 4u) != 0);
                SetLastError(savedErr);
                if (rdErr) return 1;
                if (m_handle && m_handle != 0xFFFFFFu) {
                    uint32_t idx = coop_hash_u32(m_handle);
                    for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
                        uint32_t entry = __atomic_load_n(&coopFast.handle_ht[(idx + p) & COOP_FAST_HT_MASK], __ATOMIC_RELAXED);
                        if (!entry) break;
                        if (entry == m_handle) { matched = 1; break; }
                    }
                }
            }
        }
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (__atomic_load_n(&coopFast.seq, __ATOMIC_RELAXED) == seq1) return matched;
    }
    return 1;
}

static int coop_fast_may_match_retire(uint32_t handle) {
    if (!handle || handle == 0xFFFFFFu) return 0;
    const uint32_t rcap = COOP_GARMENT_CACHE_LIMIT + COOP_MAX_TARGETS * 3u;
    for (unsigned attempt = 0; attempt < 4u; attempt++) {
        uint32_t seq1 = __atomic_load_n(&coopFast.seq, __ATOMIC_ACQUIRE);
        if (seq1 & 1u) return 1;
        uint32_t rhc = __atomic_load_n(&coopFast.retire_handle_count, __ATOMIC_RELAXED);
        if (rhc > rcap) continue;
        if (rhc == 0u) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&coopFast.seq, __ATOMIC_RELAXED) == seq1) return 0;
            continue;
        }
        int matched = 0;
        for (uint32_t i = 0; i < rhc; i++) {
            if (__atomic_load_n(&coopFast.retire_handles[i], __ATOMIC_RELAXED) == handle) { matched = 1; break; }
        }
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (__atomic_load_n(&coopFast.seq, __ATOMIC_RELAXED) == seq1) return matched;
    }
    return 1;
}

static void coop_neg_advance_epoch(void) {
    if (++coopNegEpoch == 0u) coopNegEpoch = 1u;
}

static void coop_discovery_init(void) {
    memset(&coopLastMgr, 0, sizeof coopLastMgr);
    memset(coopScannedSlots, 0, sizeof coopScannedSlots);
    memset(coopScannedGens, 0, sizeof coopScannedGens);
    memset(coopSlotCache, 0, sizeof coopSlotCache);
    coopGarmentCount = 0u;
    memset(coopNegCache, 0, sizeof coopNegCache);
    coopNegEpoch = 1u;
    coopScanCursor = 0u;
    coopScanComplete = 0;
    memset(&coopCachedRoster, 0, sizeof coopCachedRoster);
    memset(coopLastPeers, 0, sizeof coopLastPeers);
    memset(coopLastNetworks, 0, sizeof coopLastNetworks);
    memset(coopLastLifes, 0, sizeof coopLastLifes);
    coopLastPlayerCount = 0u;
    coopLastLocalPeer = 0u;
    coopHasRosterSnapshot = 0;
    memset(&coopFast, 0, sizeof coopFast);
    memset(coopPoseCache, 0, sizeof coopPoseCache);
    for (uint32_t i = 0; i < COOP_POSE_CACHE_SIZE; i++) {
        InitializeSRWLock(&coopPoseCache[i].lock);
    }
}

static uint32_t coop_hash_neg(uintptr_t mesh, uint32_t handle, uintptr_t cache, uintptr_t skin) {
    uint64_t x = (uint64_t)mesh ^ ((uint64_t)handle * 0x9E3779B97F4A7C15ULL) ^ ((uint64_t)cache >> 4) ^ ((uint64_t)skin >> 4);
    x ^= x >> 33; x *= 0xff51afd7ed558ccdULL; x ^= x >> 33;
    return (uint32_t)(x & (COOP_NEG_CACHE_LIMIT - 1u));
}

static int coop_neg_find(uintptr_t mesh, uint32_t handle, uintptr_t cache, uintptr_t skin, uintptr_t world) {
    uint32_t base = coop_hash_neg(mesh, handle, cache, skin);
    for (uint32_t probe = 0; probe < 4u; probe++) {
        const CoopNegEntry *e = &coopNegCache[(base + probe) & (COOP_NEG_CACHE_LIMIT - 1u)];
        if (e->epoch == coopNegEpoch && e->mesh == mesh && e->handle == handle &&
            e->cache == cache && e->skin == skin && e->world == world)
            return 1;
    }
    return 0;
}

static void coop_neg_insert(uintptr_t mesh, uint32_t handle, uintptr_t cache, uintptr_t skin, uintptr_t world) {
    uint32_t base = coop_hash_neg(mesh, handle, cache, skin);
    uint32_t pick = base;
    for (uint32_t probe = 0; probe < 4u; probe++) {
        uint32_t idx = (base + probe) & (COOP_NEG_CACHE_LIMIT - 1u);
        CoopNegEntry *e = &coopNegCache[idx];
        if (e->epoch != coopNegEpoch || (e->mesh == mesh && e->handle == handle)) { pick = idx; break; }
    }
    coopNegCache[pick] = (CoopNegEntry){mesh, cache, skin, world, handle, coopNegEpoch};
}

static void coop_neg_invalidate_handle(uintptr_t world, uint32_t handle) {
    if (!handle || handle == 0xFFFFFFu) return;
    for (uint32_t i = 0; i < COOP_NEG_CACHE_LIMIT; i++) {
        CoopNegEntry *e = &coopNegCache[i];
        if (e->epoch != coopNegEpoch) continue;
        if (e->handle == handle && (!world || !e->world || e->world == world))
            e->epoch = 0u;
    }
}

static void coop_neg_clear_all(void) {
    memset(coopNegCache, 0, sizeof coopNegCache);
    coop_neg_advance_epoch();
}

static int coop_read_chunks(uintptr_t at, void *out, size_t total) {
    if (!Span(at, total)) return 0;
    unsigned char *dst = (unsigned char *)out;
    size_t off = 0;
    while (off < total) {
        size_t chunk = total - off;
        if (chunk > COOP_BULK_CHUNK) chunk = COOP_BULK_CHUNK;
        if (!Read(at + off, dst + off, chunk)) return 0;
        off += chunk;
    }
    return 1;
}

static void coop_remove_garment_index(uint32_t unit_index) {
    for (uint32_t i = 0; i < coopGarmentCount;) {
        if (coopGarments[i].index == unit_index)
            coopGarments[i] = coopGarments[--coopGarmentCount];
        else
            i++;
    }
}

static int coop_upsert_garment(uint32_t idx, uint32_t tk, uint32_t graphHandle,
                               uint8_t gen, uintptr_t u, uintptr_t assoc, uint64_t resId) {
    uint64_t nowMs = GetTickCount64();
    for (uint32_t i = 0; i < coopGarmentCount; i++) {
        CoopGarmentUnit *gu = &coopGarments[i];
        if (gu->index == idx) {
            if (gu->target_kind == tk && gu->app_graph_handle == graphHandle &&
                gu->generation == gen && gu->unit == u &&
                gu->association == assoc && gu->resource == resId) {
                return 0;
            }
            *gu = (CoopGarmentUnit){idx, tk, graphHandle, gen, 0u, u, assoc, resId, nowMs, {0}};
            return 1;
        }
    }
    if (coopGarmentCount < COOP_GARMENT_CACHE_LIMIT) {
        coopGarments[coopGarmentCount++] = (CoopGarmentUnit){idx, tk, graphHandle, gen, 0u, u, assoc, resId, nowMs, {0}};
        return 1;
    }
    return 0;
}

static int coop_resolve_garment_meshes(const Manager *m, CoopGarmentUnit *gu) {
    if (!m || !gu || gu->meshes_resolved) return 0;
    UnitMeta child;
    RenderMeta rmeta;
    memset(&child, 0, sizeof child);
    if (!MetaRead(m, gu->index, &child, NODE_LIMIT) ||
        child.id.unit != gu->unit || child.id.generation != gu->generation ||
        child.id.resource != gu->resource || !RenderRead(&child, &rmeta) ||
        rmeta.graphHandle != gu->app_graph_handle) {
        return 0;
    }
    uintptr_t found[4] = {0};
    uint32_t expected = 0u, got = 0u;
    for (uint32_t ord = 1u; ord <= 4u; ord++) {
        if (!lp_asset_find(gu->resource, ord)) continue;
        expected++;
        if (ord < rmeta.meshes && rmeta.mesh[ord].registered && rmeta.mesh[ord].resolved.object) {
            found[ord - 1u] = rmeta.mesh[ord].resolved.object;
            got++;
        }
    }
    if (expected > 0u && got == expected) {
        memcpy(gu->lod_meshes, found, sizeof found);
        gu->meshes_resolved = 1u;
        return 1;
    }
    return 0;
}

static void coop_prune_garments_if_full(const Manager *m) {
    if (coopGarmentCount < COOP_GARMENT_CACHE_LIMIT || !m) return;
    for (uint32_t i = 0; i < coopGarmentCount;) {
        Identity cid;
        const CoopGarmentUnit *gu = &coopGarments[i];
        if (IdentityRead(m, gu->index, &cid) != 0 || cid.unit != gu->unit || cid.generation != gu->generation) {
            coopGarments[i] = coopGarments[--coopGarmentCount];
        } else {
            i++;
        }
    }
}

static void coop_sync_roster_if_changed(const Manager *m, int force) {
    uintptr_t players = 0, context = 0;
    uint32_t rawCount = 0;
    uint64_t localPeer = 0;
    if (!Span(gameModule, 0x347CEF8)) { coop_sync_players(&coopReg, NULL, 0); return; }
    if (!Read(gameModule + 0x3326468, &players, 8) || !Address(players) || !Span(players, 0x428) ||
        !Read(players + 0x84, &rawCount, 4) || rawCount > COOP_MAX_PLAYERS) {
        coop_sync_players(&coopReg, NULL, 0);
        coopHasRosterSnapshot = 0;
        return;
    }
    if (Read(gameModule + 0x347CEF0, &context, 8) && Address(context) && Span(context, 0xB3A0))
        Read(context + 0xB398, &localPeer, 8);
    uint64_t peers[COOP_MAX_PLAYERS] = {0};
    uint32_t nets[COOP_MAX_PLAYERS] = {0}, lifes[COOP_MAX_PLAYERS] = {0};
    for (uint32_t i = 0; i < rawCount; i++) {
        Read(players + 0x2C8 + (uintptr_t)i * 0x38, &peers[i], 8);
        Read(players + 0x2E0 + (uintptr_t)i * 0x38, &lifes[i], 4);
        if (!Read(players + 0x3A8 + (uintptr_t)i * 0x20, &nets[i], 4)) nets[i] = 0x7FFF;
    }
    int same = !force && coopHasRosterSnapshot &&
        rawCount == coopLastPlayerCount && localPeer == coopLastLocalPeer &&
        !memcmp(peers, coopLastPeers, (size_t)rawCount * sizeof(uint64_t)) &&
        !memcmp(nets, coopLastNetworks, (size_t)rawCount * sizeof(uint32_t)) &&
        !memcmp(lifes, coopLastLifes, (size_t)rawCount * sizeof(uint32_t));
    if (same) {
        /* Still check active player avatar Unit generation hasn't bumped silently or finished spawning. */
        uint32_t activeNets = 0;
        for (uint32_t i = 0; i < rawCount; i++) {
            if (nets[i] != 0x7FFF) activeNets++;
        }
        int avatarStale = (activeNets != coopReg.player_count);
        for (uint32_t i = 0; !avatarStale && i < coopReg.player_count; i++) {
            uint32_t idx = coopReg.players[i].avatar_handle & 0x3FFFFFu;
            uint8_t g = 0; uintptr_t u = 0;
            if (idx >= m->capacity || !Read(m->generations + idx, &g, 1) ||
                !Read(m->slots + (uintptr_t)idx * 8u, &u, 8) ||
                g != (uint8_t)coopReg.players[i].avatar_gen || u != coopReg.players[i].avatar_unit) {
                avatarStale = 1; break;
            }
        }
        if (!avatarStale) return;
    }
    CoopRoster roster;
    if (CoopPlayersRead(m, &roster)) {
        coopCachedRoster = roster;
        coop_sync_players(&coopReg, roster.entries, roster.count);
        coopLastPlayerCount = rawCount;
        coopLastLocalPeer = localPeer;
        memcpy(coopLastPeers, peers, sizeof coopLastPeers);
        memcpy(coopLastNetworks, nets, sizeof coopLastNetworks);
        memcpy(coopLastLifes, lifes, sizeof coopLastLifes);
        coopHasRosterSnapshot = 1;
    }
}

static int coop_discover_player_garments(const Manager *m) {
    if (!m || !m->capacity || m->capacity > COOP_UNIT_CAPACITY_LIMIT) return 0;
    coop_sync_roster_if_changed(m, 0);
    int spawned_garment = 0;
    unsigned char uhdr[0x78];
    for (uint32_t p = 0; p < coopReg.player_count; p++) {
        uintptr_t avatar = coopReg.players[p].avatar_unit;
        if (!Address(avatar) || !Span(avatar, 0x1E8u)) continue;
        uintptr_t u = 0;
        if (!Nullable(avatar + 0x1D8u, &u)) continue;
        uintptr_t visited[COOP_CHILD_LIMIT];
        uint32_t visitedCount = 0;
        while (u && visitedCount < COOP_CHILD_LIMIT) {
            int seen = 0;
            for (uint32_t k = 0; k < visitedCount; k++) {
                if (visited[k] == u) { seen = 1; break; }
            }
            if (seen) break;
            visited[visitedCount++] = u;
            if (!Span(u, sizeof uhdr) || !Read(u, uhdr, sizeof uhdr)) break;
            uintptr_t next = 0;
            if (!Nullable(u + 0x1E0u, &next)) break;
            if (U64(uhdr) == engine + VTABLE_RVA) {
                uintptr_t resPtr = U64(uhdr + 0x30u);
                uint64_t resId = 0;
                uint32_t tk = 0;
                if (Address(resPtr) && Span(resPtr, 8u) && Read(resPtr, &resId, 8u) &&
                    coop_is_compact_garment(resId, &tk)) {
                    const cm_garment *cg = cm_garment_at(tk);
                    uintptr_t assoc = U64(uhdr + 0x68u);
                    uint32_t graphHandle = U32(uhdr + 0x64u);
                    uint32_t nodes = U32(uhdr + 0x70u);
                    if (cg && nodes == cg->count &&
                        Address(assoc) && assoc >= 0x10010u && graphHandle && graphHandle != 0xFFFFFFu) {
                        uint32_t h = U32(uhdr + 8u);
                        uint32_t idx = h & 0x3FFFFFu;
                        Identity cid;
                        if (IdentityRead(m, idx, &cid) == 0 && cid.unit == u) {
                            coop_prune_garments_if_full(m);
                            if (coop_upsert_garment(idx, tk, graphHandle, cid.generation, u, assoc, resId)) {
                                spawned_garment = 1;
                                for (uint32_t k = 0; k < coopGarmentCount; k++) {
                                    if (coopGarments[k].index == idx && !coopGarments[k].meshes_resolved) {
                                        coop_resolve_garment_meshes(m, &coopGarments[k]);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            u = next;
        }
    }
    if (spawned_garment) {
        coop_neg_advance_epoch();
        coop_fast_publish();
    }
    return spawned_garment;
}

static int coop_retire_stale_targets(const Manager *m, uintptr_t preserve_mesh) {
    if (!m || !m->capacity || m->capacity > COOP_UNIT_CAPACITY_LIMIT) return 0;
    int changed = 0;
    for (uint32_t s = 0; s < COOP_MAX_TARGETS; s++) {
        coop_target_slot *ts = &coopReg.targets[s];
        if (!ts->active || (preserve_mesh && ts->mesh == preserve_mesh)) continue;
        int bad = 0;
        if (ts->child_unit) {
            uint8_t cg = 0; uintptr_t cu = 0;
            if (ts->child_index >= m->capacity ||
                !Read(m->generations + ts->child_index, &cg, 1u) ||
                !Read(m->slots + (uintptr_t)ts->child_index * 8u, &cu, 8u) ||
                cu != ts->child_unit || cg != (uint8_t)ts->child_gen) bad = 1;
        }
        if (!bad && ts->parent_unit) {
            uint8_t pg = 0; uintptr_t pu = 0;
            if (ts->parent_index >= m->capacity ||
                !Read(m->generations + ts->parent_index, &pg, 1u) ||
                !Read(m->slots + (uintptr_t)ts->parent_index * 8u, &pu, 8u) ||
                pu != ts->parent_unit || pg != (uint8_t)ts->parent_gen) bad = 1;
        }
        if (bad) {
            coop_reopen_garment_probe(ts->child_unit, ts->child_gen);
            coopSlotCache[s].valid = 0;
            coopSlotCache[s].admit_epoch = 0;
            memset(ts, 0, sizeof *ts);
            if (coopReg.active_targets > 0) coopReg.active_targets--;
            coopReg.retirements++;
            changed = 1;
        }
    }
    if (changed) coop_fast_publish();
    return changed;
}

static int coop_refresh_units(const Manager *m, uintptr_t preserve_mesh) {
    if (!m || !m->capacity || m->capacity > COOP_UNIT_CAPACITY_LIMIT) return 0;
    int filterDirty = 0;
    if (!coopLastMgr.address || m->address != coopLastMgr.address ||
        m->slots != coopLastMgr.slots || m->generations != coopLastMgr.generations) {
        memset(coopScannedSlots, 0, sizeof coopScannedSlots);
        memset(coopScannedGens, 0, sizeof coopScannedGens);
        coopGarmentCount = 0;
        coopScanCursor = 0;
        coopScanComplete = 0;
        coopLastMgr = *m;
        coop_neg_clear_all();
        filterDirty = 1;
    } else if (m->capacity != coopLastMgr.capacity) {
        if (m->capacity < coopLastMgr.capacity) {
            for (uint32_t i = m->capacity; i < coopLastMgr.capacity; i++) {
                coopScannedSlots[i] = 0;
                coopScannedGens[i] = 0;
            }
            for (uint32_t i = 0; i < coopGarmentCount;) {
                if (coopGarments[i].index >= m->capacity) {
                    coopGarments[i] = coopGarments[--coopGarmentCount];
                    filterDirty = 1;
                } else {
                    i++;
                }
            }
            if (coopScanCursor >= m->capacity) coopScanCursor = 0;
        } else {
            coopScanComplete = 0;
        }
        coopLastMgr.capacity = m->capacity;
    }
    /* Retire any active slot whose UnitManager index changed pointer/gen using O(active_targets) point reads. */
    if (coop_retire_stale_targets(m, preserve_mesh)) filterDirty = 1;
    unsigned char uhdr[0x78];
    int spawned_garment = 0;
    /* Keep cached garment units' render association & graph handle fresh via O(coopGarmentCount) point reads. */
    for (uint32_t i = 0; i < coopGarmentCount;) {
        CoopGarmentUnit *gu = &coopGarments[i];
        uint8_t curGen = 0; uintptr_t curUnit = 0;
        if (gu->index >= m->capacity ||
            !Read(m->generations + gu->index, &curGen, 1u) ||
            !Read(m->slots + (uintptr_t)gu->index * 8u, &curUnit, 8u) ||
            curUnit != gu->unit || curGen != gu->generation) {
            coopGarments[i] = coopGarments[--coopGarmentCount];
            filterDirty = 1;
            continue;
        }
        unsigned char uapp[16];
        if (Span(gu->unit, 0x70u) && Read(gu->unit + 0x60u, uapp, sizeof uapp)) {
            uintptr_t assoc = U64(uapp + 8);
            uint32_t graphHandle = U32(uapp + 4);
            if (!Address(assoc) || assoc < 0x10010u || !graphHandle || graphHandle == 0xFFFFFFu) {
                coopScannedSlots[gu->index] = 0;
                coopScannedGens[gu->index] = 0;
                coopGarments[i] = coopGarments[--coopGarmentCount];
                filterDirty = 1;
                continue;
            }
            if (gu->association != assoc || gu->app_graph_handle != graphHandle) {
                gu->association = assoc;
                gu->app_graph_handle = graphHandle;
                gu->meshes_resolved = 0u;
                memset(gu->lod_meshes, 0, sizeof gu->lod_meshes);
                gu->discovered_ms = GetTickCount64();
                spawned_garment = 1;
                filterDirty = 1;
            }
        }
        if (!gu->meshes_resolved && coop_resolve_garment_meshes(m, gu)) {
            filterDirty = 1;
        }
        i++;
    }
    /* O(1) direct discovery from active player avatars. */
    if (coop_discover_player_garments(m)) {
        spawned_garment = 1;
        filterDirty = 1;
    }
    /* Bounded incremental slice read of UnitManager (<= 36 KB per tick, never copying 2+ MB on D11). */
    uint32_t start = (coopScanCursor < m->capacity) ? coopScanCursor : 0u;
    uint32_t slice = m->capacity - start;
    if (slice > COOP_SCAN_SLICE_LIMIT) slice = COOP_SCAN_SLICE_LIMIT;
    if (!coop_read_chunks(m->slots + (uintptr_t)start * 8u, coopRawSlots + start, (size_t)slice * 8u) ||
        !coop_read_chunks(m->generations + start, coopRawGens + start, (size_t)slice)) {
        if (filterDirty) coop_fast_publish();
        return 0;
    }
    uint32_t incomplete = 0u;
    uint32_t probedNew = 0u;
    uint32_t step = 0u;
    for (; step < slice; step++) {
        uint32_t i = start + step;
        uintptr_t u = coopRawSlots[i];
        uint8_t g = coopRawGens[i];
        if (u == coopScannedSlots[i] && g == coopScannedGens[i]) continue;
        if (!u || !Span(u, sizeof uhdr)) {
            coop_remove_garment_index(i);
            coopScannedSlots[i] = u;
            coopScannedGens[i] = g;
            continue;
        }
        if (probedNew >= COOP_NEW_UNIT_PROBE_BUDGET) {
            break;
        }
        probedNew++;
        if (!Read(u, uhdr, sizeof uhdr)) {
            coopScannedSlots[i] = 0;
            coopScannedGens[i] = 0;
            incomplete++;
            continue;
        }
        if (U64(uhdr) != engine + VTABLE_RVA) {
            coop_remove_garment_index(i);
            coopScannedSlots[i] = u;
            coopScannedGens[i] = g;
            continue;
        }
        uintptr_t resPtr = U64(uhdr + 0x30u);
        uint64_t resId = 0;
        if (!Address(resPtr) || !Span(resPtr, 8u)) {
            coop_remove_garment_index(i);
            coopScannedSlots[i] = u;
            coopScannedGens[i] = g;
            continue;
        }
        if (!Read(resPtr, &resId, 8u)) {
            coopScannedSlots[i] = 0;
            coopScannedGens[i] = 0;
            incomplete++;
            continue;
        }
        uint32_t tk = 0;
        if (!coop_is_compact_garment(resId, &tk)) {
            coop_remove_garment_index(i);
            coopScannedSlots[i] = u;
            coopScannedGens[i] = g;
            continue;
        }
        const cm_garment *cg = cm_garment_at(tk);
        if (!cg || U32(uhdr + 0x70u) != cg->count) {
            coop_remove_garment_index(i);
            coopScannedSlots[i] = u;
            coopScannedGens[i] = g;
            continue;
        }
        uintptr_t assoc = U64(uhdr + 0x68u);
        uint32_t graphHandle = U32(uhdr + 0x64u);
        if (!Address(assoc) || assoc < 0x10010u || !graphHandle || graphHandle == 0xFFFFFFu) {
            /* Compact garment unit spawned before RenderWorld registration: leave uncommitted for re-scan. */
            coop_remove_garment_index(i);
            coopScannedSlots[i] = 0;
            coopScannedGens[i] = 0;
            incomplete++;
            continue;
        }
        coop_prune_garments_if_full(m);
        if (coop_upsert_garment(i, tk, graphHandle, g, u, assoc, resId)) {
            spawned_garment = 1;
            filterDirty = 1;
        }
        for (uint32_t k = 0; k < coopGarmentCount; k++) {
            if (coopGarments[k].index == i && !coopGarments[k].meshes_resolved) {
                if (coop_resolve_garment_meshes(m, &coopGarments[k])) filterDirty = 1;
                break;
            }
        }
        coopScannedSlots[i] = u;
        coopScannedGens[i] = g;
    }
    uint32_t nextCursor = start + step;
    if (nextCursor >= m->capacity) {
        coopScanCursor = 0u;
        coopScanComplete = (incomplete == 0u);
    } else {
        coopScanCursor = nextCursor;
        coopScanComplete = 0;
    }
    if (__atomic_load_n(&coopFast.need_handle_probe, __ATOMIC_RELAXED) >= 2u) filterDirty = 1;
    if (spawned_garment) coop_neg_advance_epoch();
    if (filterDirty || spawned_garment) coop_fast_publish();
    return 1;
}

static int coop_find_slot_ptr(const Manager *m, uintptr_t unit_ptr, uint32_t *out_idx) {
    /* O(4) roster lookup first so we never scan 262k slots when a player avatar lacks a self-handle hint. */
    for (uint32_t p = 0; p < coopReg.player_count; p++) {
        if (coopReg.players[p].avatar_unit == unit_ptr && coopReg.players[p].avatar_handle) {
            uint32_t idx = coopReg.players[p].avatar_handle & 0x3FFFFFu;
            uintptr_t live = 0;
            if (idx < m->capacity && Read(m->slots + (uintptr_t)idx * 8u, &live, 8u) && live == unit_ptr) {
                *out_idx = idx;
                return 1;
            }
        }
    }
    uint32_t hits = 0;
    for (uint32_t i = 0; i < m->capacity; i++) {
        if (coopRawSlots[i] == unit_ptr) { *out_idx = i; hits++; }
    }
    return hits == 1;
}

static int coop_topology_valid(uint32_t target_kind, const UnitMeta *child, Topology topo[NODE_LIMIT]) {
    const cm_garment *g = cm_garment_at(target_kind);
    if (!g || child->nodes != g->count) return 0;
    if (!Read(child->topology, topo, (size_t)child->nodes * 4u)) return 0;
    for (uint32_t i = 0; i < child->nodes; i++) {
        if (topo[i].type == 2u) { if (topo[i].node >= 107u) return 0; }
        else if (topo[i].type != 1u || topo[i].node >= i) return 0;
    }
    return 1;
}

static void coop_fill_se_target(const coop_target_slot *slot, se_target *out) {
    memset(out, 0, sizeof *out);
    out->mesh = slot->mesh;
    out->token = (uint64_t)(slot->target_kind * 8u + slot->garment_ordinal);
    IvU64(out->identity, 0, slot->child_unit);
    IvU64(out->identity, 8, slot->garment_resource);
    IvU64(out->identity, 16, slot->parent_unit);
    IvU64(out->identity, 24, slot->descriptor);
    IvU32(out->identity, 32, slot->render_handle);
    IvU32(out->identity, 36, slot->child_index);
    IvU32(out->identity, 40, slot->child_gen);
    IvU32(out->identity, 44, slot->parent_index);
    IvU32(out->identity, 48, slot->parent_gen);
    IvU32(out->identity, 52, (uint32_t)slot->actor_kind);
    IvU64(out->identity, 56, slot->normal_graph);
}

static void coop_evict_slot(coop_target_slot *slot) {
    if (!slot || !slot->active) return;
    coop_reopen_garment_probe(slot->child_unit, slot->child_gen);
    ptrdiff_t idx = slot - coopReg.targets;
    if (idx >= 0 && (uint32_t)idx < COOP_MAX_TARGETS) {
        coopSlotCache[idx].valid = 0;
        coopSlotCache[idx].admit_epoch = 0;
    }
    memset(slot, 0, sizeof *slot);
    if (coopReg.active_targets > 0) coopReg.active_targets--;
    coopReg.retirements++;
    coop_fast_publish();
}

static int CoopChainRead(Record *r) {
    uintptr_t seen[COOP_CHILD_LIMIT];
    uint32_t seenCount = 0;
    r->chainCount = 0;
    r->chainStatus = 0;
    r->reciprocal = 0;
    uintptr_t at = r->parent.firstChild;
    while (at && seenCount < COOP_CHILD_LIMIT) {
        for (uint32_t i = 0; i < seenCount; i++) {
            if (seen[i] == at) {
                r->chainStatus = 2;
                return 1;
            }
        }
        uintptr_t next = 0;
        if (!Nullable(at + 0x1E0u, &next)) return 0;
        seen[seenCount++] = at;
        if (r->chainCount < CHILD_LIMIT) r->chain[r->chainCount] = at;
        r->chainCount++;
        if (at == r->child.id.unit) {
            uintptr_t table = 0, parent = 0;
            if (!Pointer(at, &table) || !Nullable(at + 0x1D0u, &parent)) return 0;
            if (table != engine + VTABLE_RVA || parent != r->parent.id.unit) {
                r->chainStatus = 3;
                return 1;
            }
            r->reciprocal = 1;
        }
        at = next;
    }
    if (at) r->chainStatus = 1;
    return 1;
}

static void CoopRetireHandle(gw_state *s, uintptr_t world, uint32_t handle) {
    if (!s || !handle || handle == 0xFFFFFFu) return;
    /* Lock-free reject when this render handle is not tracked by any active LEGO target or garment. */
    if (!coop_fast_may_match_retire(handle)) return;
    AcquireSRWLockExclusive(&s->metadata);
    uint32_t removed = coop_retire_handle_world(&coopReg, world, handle);
    coop_neg_invalidate_handle(world, handle);
    if (removed > 0u) {
        uint64_t nowMs = GetTickCount64();
        for (uint32_t i = 0; i < COOP_MAX_TARGETS; i++) {
            if (!coopReg.targets[i].active) {
                coopSlotCache[i].valid = 0;
                coopSlotCache[i].admit_epoch = 0;
            }
        }
        for (uint32_t g = 0; g < coopGarmentCount; g++) {
            if (coopGarments[g].app_graph_handle == handle) {
                coopGarments[g].discovered_ms = nowMs;
                coopGarments[g].meshes_resolved = 0u;
                memset(coopGarments[g].lod_meshes, 0, sizeof coopGarments[g].lod_meshes);
            }
        }
        coop_neg_advance_epoch();
        coop_fast_publish();
    }
    ReleaseSRWLockExclusive(&s->metadata);
    if (removed) InterlockedAdd64(&s->retires, (LONG64)removed);
}

static int CoopFastMayMatch(gw_state *s, void *mesh_ptr, void *pool_ptr) {
    if (!s || !mesh_ptr || !pool_ptr) return 1;
    uintptr_t mesh = (uintptr_t)mesh_ptr, pool = (uintptr_t)pool_ptr;
    if (!Span(mesh, 0xD0) || pool < 0x10318u || !Span(pool, 0x180u)) return 1;
    return coop_fast_may_match_skin(mesh, 1);
}

static int CoopResolveTarget(gw_state *s, void *mesh_ptr, void *pool_ptr, void *graph_ptr,
                             uintptr_t caller, LONG64 sequence, se_target *out_target, coop_actor_kind *out_kind) {
    (void)caller; (void)sequence;
    if (!s || !mesh_ptr || !pool_ptr || !out_target || !out_kind) return 0;
    *out_kind = COOP_ACTOR_REJECTED;
    uintptr_t mesh = (uintptr_t)mesh_ptr, pool = (uintptr_t)pool_ptr, optGraph = (uintptr_t)graph_ptr;
    if (!Span(mesh, 0xD0) || pool < 0x10318u || !Span(pool, 0x180u)) {
        InterlockedIncrement64(&s->rej_world);
        return 0;
    }

    /* Lock-free fast filter: reject 99.95%+ non-LEGO meshes without taking writer.metadata, scanning UnitManager, or bouncing shared atomics. */
    if (!coop_fast_may_match_skin(mesh, 0)) {
        return 0;
    }

    int matched = 0;
    AcquireSRWLockExclusive(&s->metadata);
    coopProbeActive = 1; coopProbeReads = coopProbeBytes = 0;

    Manager mgr;
    if (!CoopManagerRead(&mgr)) {
        InterlockedIncrement64(&s->rej_mgr);
        goto out;
    }
    InterlockedExchange64(&s->unit_cap, (LONG64)mgr.capacity);

    /* Fast path: mesh already active in coopReg -> verify handle, skin, Unit generations, AND parent graph / world RHeader. */
    coop_target_slot *existing = coop_lookup_mesh(&coopReg, mesh);
    if (existing) {
        unsigned char mfast[0x78];
        if (pool - 0x318u == existing->world && Read(mesh, mfast, sizeof mfast) &&
            U64(mfast) == engine + 0x1688950u && U32(mfast + 8) == 3u) {
            Ref normal = {U32(mfast + 0x30), U32(mfast + 0x34), U64(mfast + 0x38)};
            uintptr_t skin = U64(mfast + 0x70);
            uint8_t cg = 0, pg = 0; uintptr_t cu = 0, pu = 0;
            if (normal.handle == existing->child_graph_handle && normal.cache == existing->normal_graph && skin == existing->skin &&
                existing->child_index < mgr.capacity && existing->parent_index < mgr.capacity &&
                Read(mgr.generations + existing->child_index, &cg, 1) && Read(mgr.slots + (uintptr_t)existing->child_index * 8u, &cu, 8) &&
                Read(mgr.generations + existing->parent_index, &pg, 1) && Read(mgr.slots + (uintptr_t)existing->parent_index * 8u, &pu, 8) &&
                cu == existing->child_unit && cg == (uint8_t)existing->child_gen &&
                pu == existing->parent_unit && pg == (uint8_t)existing->parent_gen) {
                uintptr_t wFast = pool - 0x318u, rmanagerFast = 0, expectedAssocFast = 0;
                RHeader rhdrFast;
                unsigned char pappFast[16];
                Resolved pgraphFast;
                int fastWorldOk = Span(wFast, 0x6B0u) && Pointer(wFast + 0x150u, &rmanagerFast) &&
                    HeaderRead(rmanagerFast, &rhdrFast) && rhdrFast.world == wFast;
                if (fastWorldOk) expectedAssocFast = rhdrFast.manager + 0x10u;
                int fastAppOk = fastWorldOk && Span(existing->parent_unit, 0x70u) &&
                    Read(existing->parent_unit + 0x60u, pappFast, sizeof pappFast);
                uint32_t liveParentGraph = fastAppOk ? U32(pappFast + 4) : 0u;
                uintptr_t liveParentAssoc = fastAppOk ? U64(pappFast + 8) : 0u;
                int fastParentOk = fastAppOk && liveParentAssoc == expectedAssocFast &&
                    (!existing->expected_assoc || existing->expected_assoc == expectedAssocFast) &&
                    liveParentGraph == existing->parent_graph_handle &&
                    Resolve(&rhdrFast, existing->parent_graph_handle, 2u, &pgraphFast) &&
                    pgraphFast.nodes == IV_PARENT_NODES;
                if (fastParentOk) {
                    if (optGraph && optGraph != existing->normal_graph) {
                        InterlockedIncrement64(&s->rej_world);
                        goto out;
                    }
                    coop_sync_roster_if_changed(&mgr, 0);
                    existing->last_seen_epoch = ++coopReg.epoch_clock;
                    coop_fill_se_target(existing, out_target);
                    *out_kind = existing->actor_kind;
                    matched = 1;
                    goto out;
                }
                /* Fast-path parent graph or world association invalid -> evict stale slot cleanly! */
                coop_evict_slot(existing);
                existing = NULL;
                InterlockedIncrement64(&s->fast_evicts);
                if (!fastWorldOk || !fastAppOk || liveParentAssoc != expectedAssocFast ||
                    !Resolve(&rhdrFast, liveParentGraph, 2u, &pgraphFast) || pgraphFast.nodes != IV_PARENT_NODES) {
                    InterlockedIncrement64(!fastWorldOk ? &s->rej_world : &s->rej_parent);
                    goto out;
                }
            }
        }
        /* Fast validation failed on an active LEGO mesh -> O(1) refresh of player garments & this exact slot for instant ABA rebind. */
        if (existing) {
            coop_discover_player_garments(&mgr);
            if (existing->child_index < mgr.capacity) {
                Identity abaId;
                unsigned char abahdr[0x78];
                if (IdentityRead(&mgr, existing->child_index, &abaId) == 0 &&
                    Span(abaId.unit, sizeof abahdr) && Read(abaId.unit, abahdr, sizeof abahdr) &&
                    U64(abahdr) == engine + VTABLE_RVA) {
                    uint32_t tk = 0;
                    uintptr_t assoc = U64(abahdr + 0x68u);
                    uint32_t gh = U32(abahdr + 0x64u);
                    const cm_garment *cg = coop_is_compact_garment(abaId.resource, &tk) ? cm_garment_at(tk) : NULL;
                    if (cg && U32(abahdr + 0x70u) == cg->count &&
                        Address(assoc) && assoc >= 0x10010u && gh && gh != 0xFFFFFFu) {
                        coop_prune_garments_if_full(&mgr);
                        if (coop_upsert_garment(existing->child_index, tk, gh, abaId.generation, abaId.unit, assoc, abaId.resource)) {
                            for (uint32_t k = 0; k < coopGarmentCount; k++) {
                                if (coopGarments[k].index == existing->child_index && !coopGarments[k].meshes_resolved) {
                                    coop_resolve_garment_meshes(&mgr, &coopGarments[k]);
                                    break;
                                }
                            }
                            coop_neg_advance_epoch();
                            coop_fast_publish();
                        }
                    }
                }
            }
        }
    }

    /* Full incoming MeshObject header check */
    unsigned char mhdr[0x78];
    if (!Read(mesh, mhdr, sizeof mhdr) || U64(mhdr) != engine + 0x1688950u || U32(mhdr + 8) != 3u) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_world);
        goto out;
    }
    Ref normal = {U32(mhdr + 0x30), U32(mhdr + 0x34), U64(mhdr + 0x38)};
    uintptr_t skin = U64(mhdr + 0x70);
    if (!skin || !Address(skin) || !normal.handle || normal.handle == 0xFFFFFFu || !Address(normal.cache)) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_world);
        goto out;
    }
    uintptr_t w = pool - 0x318u;
    if (!existing && coop_neg_find(mesh, normal.handle, normal.cache, skin, w)) {
        InterlockedIncrement64(&s->neg_hits);
        goto out;
    }

    /* Validate RenderWorld = pool - 0x318 -> manager -> RHeader -> normal RenderGraph */
    unsigned char whdr[32];
    uintptr_t rmanager = 0;
    RHeader rhdr;
    Resolved ngraph;
    if (!Span(w, 0x6B0u) || !Read(w, whdr, sizeof whdr) ||
        U64(whdr) != engine + 0x1672E70u || U32(whdr + 8) != 1u ||
        U64(whdr + 0x10) != engine + 0x168B5A0u || U64(whdr + 0x18) != engine + 0x16729A8u ||
        !Pointer(w + 0x150u, &rmanager) || !HeaderRead(rmanager, &rhdr) || rhdr.world != w ||
        !Resolve(&rhdr, normal.handle, 2u, &ngraph) || ngraph.object != normal.cache || normal.node >= ngraph.nodes) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_world);
        goto out;
    }

    /* Reverse-join matching compact garment unit in already-discovered UnitManager cache (never bulk-scan UnitManager on 512B80). */
    uintptr_t expectedAssoc = rhdr.manager + 0x10u;
    CoopGarmentUnit *foundGu = NULL;
    uint32_t matchCount = 0;
    for (uint32_t i = 0; i < coopGarmentCount; i++) {
        if (coopGarments[i].association == expectedAssoc && coopGarments[i].app_graph_handle == normal.handle) {
            foundGu = &coopGarments[i];
            matchCount++;
        }
    }
    if (matchCount == 1u) {
        Identity liveId;
        if (IdentityRead(&mgr, foundGu->index, &liveId) != 0 ||
            liveId.unit != foundGu->unit || liveId.generation != foundGu->generation) {
            foundGu = NULL; matchCount = 0;
        }
    }
    if (matchCount != 1u || !foundGu) {
        if (existing) coop_evict_slot(existing);
        if (matchCount == 0u && (coopScanComplete || coopReg.player_count > 0u))
            coop_neg_insert(mesh, normal.handle, normal.cache, skin, w);
        InterlockedIncrement64(&s->rej_nomatch);
        goto out;
    }

    /* Reject override optional_graph if non-null and != normal.cache (passthrough safely without evicting existing). */
    if (optGraph && optGraph != normal.cache) {
        InterlockedIncrement64(&s->rej_world);
        goto out;
    }

    /* Full structural & parent 107-node 0x4D1C334D294DFA97 reverse-join validation */
    Record rec;
    RenderMeta rmeta;
    memset(&rec, 0, sizeof rec);
    if (!MetaRead(&mgr, foundGu->index, &rec.child, NODE_LIMIT) ||
        rec.child.id.unit != foundGu->unit || rec.child.id.generation != foundGu->generation ||
        rec.child.id.resource != foundGu->resource || !coop_topology_valid(foundGu->target_kind, &rec.child, rec.topology) ||
        !RenderRead(&rec.child, &rmeta) ||
        rmeta.header.manager != rhdr.manager || rmeta.header.world != rhdr.world ||
        rmeta.header.handles != rhdr.handles || rmeta.header.capacity != rhdr.capacity ||
        rmeta.header.mask != rhdr.mask || rmeta.header.generation != rhdr.generation ||
        rmeta.header.live != rhdr.live ||
        rmeta.graphHandle != normal.handle || rmeta.graph.object != normal.cache) {
        if (existing) coop_evict_slot(existing);
        if (coopScanComplete) coop_neg_insert(mesh, normal.handle, normal.cache, skin, w);
        InterlockedIncrement64(&s->rej_meta);
        goto out;
    }

    uint32_t ordinal = UINT32_MAX, mmatches = 0;
    for (uint32_t j = 0; j < rmeta.meshes; j++) {
        if (rmeta.mesh[j].registered && rmeta.mesh[j].resolved.object == mesh) {
            ordinal = j; mmatches++;
        }
    }
    if (mmatches != 1u || ordinal < 1u || ordinal > 4u || !lp_asset_find(foundGu->resource, ordinal)) {
        if (existing) coop_evict_slot(existing);
        if (coopScanComplete) coop_neg_insert(mesh, normal.handle, normal.cache, skin, w);
        InterlockedIncrement64(&s->rej_meta);
        goto out;
    }
    const MeshMeta *mm = &rmeta.mesh[ordinal];
    uintptr_t descSkin = 0;
    if (mm->skin != skin || !Read(mm->descriptor + 0x80u, &descSkin, 8) || descSkin != skin) {
        if (existing) coop_evict_slot(existing);
        if (coopScanComplete) coop_neg_insert(mesh, normal.handle, normal.cache, skin, w);
        InterlockedIncrement64(&s->rej_meta);
        goto out;
    }

    uint32_t parentIdx = 0;
    int parentOk = 0;
    if (!rec.child.parent) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_parent);
        goto out;
    }
    uint32_t pHintHandle = 0;
    if (Span(rec.child.parent, 12u) && Read(rec.child.parent + 8u, &pHintHandle, 4u)) {
        uint32_t hintIdx = pHintHandle & 0x3FFFFFu;
        if (MetaRead(&mgr, hintIdx, &rec.parent, PARENT_NODE_LIMIT) && rec.parent.id.unit == rec.child.parent) {
            parentIdx = hintIdx;
            parentOk = 1;
        }
    }
    if (!parentOk) {
        if (!coop_find_slot_ptr(&mgr, rec.child.parent, &parentIdx)) {
            if (existing) coop_evict_slot(existing);
            InterlockedIncrement64(&s->rej_parent);
            goto out;
        }
        parentOk = MetaRead(&mgr, parentIdx, &rec.parent, PARENT_NODE_LIMIT);
    }
    if (!parentOk || rec.parent.id.unit != rec.child.parent ||
        rec.parent.id.resource != COOP_HUMAN_AVATAR_RESOURCE || rec.parent.nodes != IV_PARENT_NODES ||
        rec.child.externalWorld != rec.parent.world) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_parent);
        goto out;
    }
    rec.parentPresent = 1; rec.worldEqual = 1;
    if (!CoopChainRead(&rec) || rec.chainStatus || !rec.reciprocal) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_parent);
        goto out;
    }
    unsigned char papp[16];
    Resolved pgraph;
    if (!Span(rec.parent.id.unit, 0x70u) || !Read(rec.parent.id.unit + 0x60u, papp, 16) ||
        U64(papp + 8) != expectedAssoc || !Resolve(&rmeta.header, U32(papp + 4), 2u, &pgraph) ||
        pgraph.nodes != IV_PARENT_NODES) {
        if (existing) coop_evict_slot(existing);
        InterlockedIncrement64(&s->rej_parent);
        goto out;
    }

    coop_sync_roster_if_changed(&mgr, 1);
    if (coopReg.active_targets >= COOP_MAX_TARGETS && !existing) {
        /* Sweep dead active slots via O(active_targets) point reads before admitting into a full registry. */
        coop_retire_stale_targets(&mgr, 0);
    }

    coop_mesh_probe probe;
    memset(&probe, 0, sizeof probe);
    probe.mesh = mesh;
    probe.pool = pool;
    probe.graph = optGraph ? optGraph : normal.cache;
    probe.descriptor = mm->descriptor;
    probe.child_unit = rec.child.id.unit;
    probe.parent_unit = rec.parent.id.unit;
    probe.parent_unit_handle = ((uint32_t)rec.parent.id.generation << 22) | (rec.parent.id.index & 0x3FFFFFu);
    probe.parent_unit_gen = rec.parent.id.generation;
    probe.parent_node_count = rec.parent.nodes;
    probe.parent_unit_resource = rec.parent.id.resource;
    probe.garment_resource = rec.child.id.resource;
    probe.node_index = mm->node;
    probe.render_handle = mm->handle;
    probe.world = w;
    probe.skin = skin;
    probe.normal_graph = normal.cache;
    probe.child_unit_index = rec.child.id.index;
    probe.child_unit_gen = rec.child.id.generation;
    probe.parent_unit_index = rec.parent.id.index;
    probe.child_graph_handle = rmeta.graphHandle;
    probe.parent_graph_handle = U32(papp + 4);
    probe.expected_assoc = expectedAssoc;
    probe.garment_ordinal = ordinal;

    uint32_t prevAdmissions = coopReg.admissions;
    uint32_t prevAba = coopReg.aba_rebinds;
    coop_target_slot admitted;
    coop_actor_kind kind = coop_admit_mesh(&coopReg, &probe, &admitted);
    if (coopReg.admissions > prevAdmissions) InterlockedAdd64(&s->dynamic_admissions, (LONG64)(coopReg.admissions - prevAdmissions));
    if (coopReg.aba_rebinds > prevAba) InterlockedAdd64(&s->aba_rebinds, (LONG64)(coopReg.aba_rebinds - prevAba));
    if (kind == COOP_ACTOR_REJECTED) {
        coop_reopen_garment_probe(probe.child_unit, probe.child_unit_gen);
        coop_fast_publish();
        InterlockedIncrement64(&s->rej_admit);
        goto out;
    }

    if (!foundGu->meshes_resolved) {
        coop_resolve_garment_meshes(&mgr, foundGu);
    }
    coop_fill_se_target(&admitted, out_target);
    coop_fast_publish();
    *out_kind = kind;
    matched = 1;

out:
    InterlockedExchange64(&s->garments_cached, (LONG64)coopGarmentCount);
    coopProbeActive = 0;
    ReleaseSRWLockExclusive(&s->metadata);
    InvocationClearFailures();
    return matched;
}

static int coop_capture_passthrough(gw_state *s, coop_target_slot *slot, int evict, int is_mutation, const char *reason) {
    if (evict && slot) coop_evict_slot(slot);
    if (s) InterlockedIncrement64(is_mutation ? &s->capture_mutation_skips : &s->capture_teardown_evicts);
    if (invocationBudget) invocationBudget->status = (uint32_t)IV_RETIRED;
    return reason ? RF(reason) : 0;
}

static int CoopCaptureTarget(gw_state *s, const se_observe_context *ctx, Record *out, RenderMeta *meta, Owner *owner) {
    memset(out, 0, sizeof *out); memset(meta, 0, sizeof *meta); memset(owner, 0, sizeof *owner);
    const se_target *tgt = ctx->target;
    coop_target_slot *slot = coop_lookup_mesh(&coopReg, tgt->mesh);
    uint32_t cIdx = U32(tgt->identity + 36), cGen = U32(tgt->identity + 40);
    uint32_t pIdx = U32(tgt->identity + 44), pGen = U32(tgt->identity + 48);
    uint64_t gRes = U64(tgt->identity + 8);
    if (!slot || !slot->active || slot->child_index != cIdx || slot->child_gen != cGen ||
        slot->parent_index != pIdx || slot->parent_gen != pGen || slot->garment_resource != gRes) {
        return coop_capture_passthrough(s, NULL, 0, 0, "retired_mid_call");
    }

    Manager openMgr, closeMgr;
    if (!CoopManagerRead(&openMgr)) {
        return coop_capture_passthrough(s, NULL, 0, 0, "unit_manager_read");
    }
    if (slot->child_index >= openMgr.capacity || slot->parent_index >= openMgr.capacity ||
        !MetaRead(&openMgr, slot->child_index, &out->child, NODE_LIMIT) ||
        out->child.id.unit != slot->child_unit || out->child.id.generation != slot->child_gen ||
        out->child.id.resource != slot->garment_resource || !cm_garment_find(out->child.id.resource, NULL)) {
        return coop_capture_passthrough(s, slot, 1, 0, "child_unit_retired");
    }
    if (!MetaRead(&openMgr, slot->parent_index, &out->parent, PARENT_NODE_LIMIT) ||
        out->parent.id.unit != slot->parent_unit || out->parent.id.generation != slot->parent_gen ||
        out->parent.id.resource != COOP_HUMAN_AVATAR_RESOURCE || out->parent.nodes != IV_PARENT_NODES ||
        out->child.parent != slot->parent_unit) {
        return coop_capture_passthrough(s, slot, 1, 0, "parent_unit_retired");
    }
    out->parentPresent = 1;
    out->worldEqual = (out->child.externalWorld == out->parent.world);
    if (!out->worldEqual) {
        return coop_capture_passthrough(s, slot, 1, 0, "target_parent_world_mismatch");
    }
    if (!CoopChainRead(out) || out->chainStatus || !out->reciprocal) {
        return coop_capture_passthrough(s, slot, 1, 0, "target_reciprocal_chain");
    }
    if (!RenderRead(&out->child, meta)) {
        return coop_capture_passthrough(s, slot, 1, 0, NULL);
    }
    if (meta->header.world != slot->world ||
        (slot->expected_assoc && meta->header.manager + 0x10u != slot->expected_assoc) ||
        meta->graphHandle != slot->child_graph_handle ||
        meta->graph.object != slot->normal_graph) {
        return coop_capture_passthrough(s, slot, 1, 0, "render_world_or_graph_changed");
    }

    uint32_t ordinal = (uint32_t)(tgt->token % 8u);
    if (ordinal < 1u || ordinal >= meta->meshes || !meta->mesh[ordinal].registered ||
        meta->mesh[ordinal].resolved.object != ctx->call->mesh ||
        meta->mesh[ordinal].descriptor != slot->descriptor ||
        meta->mesh[ordinal].handle != slot->render_handle ||
        (slot->skin && meta->mesh[ordinal].skin != slot->skin)) {
        return coop_capture_passthrough(s, slot, 1, 0, "render_mesh_changed");
    }

    unsigned char papp[16];
    Resolved pgraph;
    if (!Span(out->parent.id.unit, 0x70u) || !Read(out->parent.id.unit + 0x60u, papp, sizeof papp) ||
        U64(papp + 8) != meta->header.manager + 0x10u ||
        (slot->parent_graph_handle && U32(papp + 4) != slot->parent_graph_handle) ||
        !Resolve(&meta->header, U32(papp + 4), 2u, &pgraph) ||
        pgraph.nodes != IV_PARENT_NODES) {
        return coop_capture_passthrough(s, slot, 1, 0, "invocation_parent_graph_binding");
    }

    owner->unit = out->parent.id;
    owner->descriptor.reference = (slot->parent_gen << 22) | (slot->parent_index & 0x3FFFFFu);
    for (uint32_t i = 0; i < coopReg.player_count; i++) {
        if (coopReg.players[i].avatar_unit == slot->parent_unit &&
            coopReg.players[i].avatar_gen == slot->parent_gen) {
            if (coopReg.players[i].avatar_handle) owner->descriptor.reference = coopReg.players[i].avatar_handle;
            owner->peer = coopReg.players[i].peer;
            owner->network = coopReg.players[i].network_id;
            owner->life = coopReg.players[i].life;
            break;
        }
    }

    SavedTarget savedSnapshot;
    memset(&savedSnapshot, 0, sizeof savedSnapshot);
    savedSnapshot.child = out->child;
    savedSnapshot.parent = out->parent;
    savedSnapshot.render = *meta;
    savedSnapshot.owner = *owner;

    if (invocationBudget && !InvocationPayload(out, meta, owner)) {
        uint32_t st = invocationBudget->status;
        int isMutation = (st == IV_POSE_CHANGED || st == IV_HEADER_CHANGED);
        return coop_capture_passthrough(s, slot, !isMutation, isMutation, NULL);
    }

    RenderMeta again;
    if (writerCaptureActive) { samplerTrace.attempted = 1; samplerTrace.beforeValid = 1; samplerTrace.before = *meta; }
    int closingRead = RenderRead(&out->child, &again);
    if (writerCaptureActive) {
        samplerTrace.success = (uint32_t)closingRead;
        samplerTrace.afterState = closingRead ? 2u : 1u;
        samplerTrace.after = again;
        SamplerText(samplerTrace.underlying, sizeof samplerTrace.underlying, closingRead ? "accepted" : renderFailure);
    }
    if (!closingRead) {
        return coop_capture_passthrough(s, slot, 1, 0, "render_metadata_changed");
    }
    if (!CaptureRenderClose(slot->child_index, &savedSnapshot, NULL, out, meta, &again, slot->target_kind)) {
        int structural = again.header.world != meta->header.world ||
            again.header.manager != meta->header.manager ||
            again.graphHandle != meta->graphHandle ||
            again.graph.object != meta->graph.object ||
            ordinal >= again.meshes || !again.mesh[ordinal].registered ||
            again.mesh[ordinal].resolved.object != ctx->call->mesh ||
            again.mesh[ordinal].descriptor != slot->descriptor ||
            again.mesh[ordinal].handle != slot->render_handle ||
            again.mesh[ordinal].skin != meta->mesh[ordinal].skin;
        return coop_capture_passthrough(s, slot, structural, !structural, NULL);
    }

    UnitMeta childAfter, parentAfter;
    if (!MetaRead(&openMgr, slot->child_index, &childAfter, NODE_LIMIT) || memcmp(&childAfter, &out->child, sizeof childAfter) ||
        !MetaRead(&openMgr, slot->parent_index, &parentAfter, PARENT_NODE_LIMIT) || memcmp(&parentAfter, &out->parent, sizeof parentAfter)) {
        return coop_capture_passthrough(s, slot, 1, 1, "target_metadata_changed");
    }
    if (!CoopManagerRead(&closeMgr) || closeMgr.slots != openMgr.slots || closeMgr.generations != openMgr.generations ||
        slot->child_index >= closeMgr.capacity || slot->parent_index >= closeMgr.capacity) {
        return coop_capture_passthrough(s, NULL, 0, 1, "unit_manager_changed");
    }
    Identity closeChildId, closeParentId;
    if (IdentityRead(&closeMgr, slot->child_index, &closeChildId) != 0 ||
        closeChildId.unit != out->child.id.unit || closeChildId.generation != out->child.id.generation ||
        closeChildId.resource != out->child.id.resource ||
        IdentityRead(&closeMgr, slot->parent_index, &closeParentId) != 0 ||
        closeParentId.unit != out->parent.id.unit || closeParentId.generation != out->parent.id.generation ||
        closeParentId.resource != out->parent.id.resource) {
        return coop_capture_passthrough(s, slot, 1, 1, "unit_manager_changed");
    }
    return 1;
}

static void coop_cache_slot_plan(uintptr_t mesh, const unsigned char *payload, const lp_plan *plan) {
    /* Caller holds writer.metadata exclusive lock (slow path WriterPlan recheck==0). */
    if (!mesh || !payload || !plan || !plan->groups || plan->groups > LP_GROUPS) return;
    coop_target_slot *slot = coop_lookup_mesh(&coopReg, mesh);
    if (!slot || !slot->active) return;
    ptrdiff_t idx = slot - coopReg.targets;
    if (idx < 0 || (uint32_t)idx >= COOP_MAX_TARGETS) return;
    uintptr_t manager = U64(payload + 128);
    uintptr_t parentGraph = U64(payload + 160);
    uint32_t childNodes = U32(payload + 184);
    uintptr_t skin = U64(payload + 480);
    uintptr_t bind = U64(payload + 496);
    uint32_t groups = U32(payload + 512);
    uintptr_t sub = U64(payload + 520);
    uint32_t outputCount = U32(payload + 568);
    uintptr_t poolBase = U64(payload + 672);
    unsigned char mraw[80];
    if (!hf || !hf->read || !Address(manager) || !Span(manager, 0x60u) ||
        hf->read(manager + 0x10u, mraw, sizeof mraw) != 0) return;
    uintptr_t handlesBase = U64(mraw + 0x08u);
    uint32_t handlesMask = U32(mraw + 0x30u);
    if (!Address(handlesBase) || !handlesMask) return;
    if (!Address(parentGraph) || !childNodes || childNodes > SM_CAP || !Address(skin) ||
        !Address(bind) || !Address(sub) || !Address(poolBase) || groups != plan->groups || outputCount != plan->output_count) {
        return;
    }
    CoopSlotCache *cc = &coopSlotCache[idx];
    cc->admit_epoch = slot->admit_epoch;
    cc->parent_normal_graph = parentGraph;
    cc->handles_base = handlesBase;
    cc->handles_mask = handlesMask;
    cc->child_nodes = childNodes;
    cc->mesh_node = plan->mesh_node;
    cc->groups = plan->groups;
    cc->output_count = plan->output_count;
    cc->skin = skin;
    cc->bind = bind;
    cc->sub = sub;
    cc->pool_base = poolBase;
    cc->map = plan->map;
    memcpy(cc->group, plan->group, sizeof cc->group);
    cc->valid = 1;
}

static cm_status coop_fast_inv3x4(const cm_render48 *r, cm_mat4 *out) {
    double a = r->v[0], b = r->v[1], c = r->v[2];
    double d = r->v[4], e = r->v[5], f = r->v[6];
    double g = r->v[8], h = r->v[9], i = r->v[10];
    for (unsigned n = 0; n < 12; n++) if (!isfinite(r->v[n])) return CM_NONFINITE;
    double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    if (!isfinite(det)) return CM_NONFINITE;
    if (det == 0.0) return CM_SINGULAR;
    cm_mat4 v = {{
        (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det, 0.0,
        (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det, 0.0,
        (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det, 0.0,
        0.0, 0.0, 0.0, 1.0
    }};
    for (unsigned j = 0; j < 3; j++) {
        v.v[j * 4 + 3] = -(v.v[j * 4] * r->v[3] + v.v[j * 4 + 1] * r->v[7] + v.v[j * 4 + 2] * r->v[11]);
    }
    for (unsigned n = 0; n < 16; n++) if (!isfinite(v.v[n])) return CM_NONFINITE;
    *out = v;
    return CM_OK;
}

/* Lock-free/shared-lock parallel hot path for already-admitted LEGO garments in live gameplay.
 * Returns:  1 = slot->plan & math->current/previous ready for 5B2990 leaf substitution;
 *           0 = cache miss (fall through to slow path once to admit/refresh CoopSlotCache);
 *          -1 = retired/mutated mid-call (passthrough cleanly untouched). */
static int CoopFastRun(gw_state *s, void *mesh_ptr, void *pool_ptr, void *graph_ptr,
                       uintptr_t caller, gw_slot *slot, uint32_t entry_csr,
                       coop_actor_kind *out_kind, uint32_t *out_route) {
    if (!s || !s->rig || s->resolve_target != CoopResolveTarget || !mesh_ptr || !pool_ptr || !slot || !hf || !hf->read) return 0;
    uintptr_t mesh = (uintptr_t)mesh_ptr, pool = (uintptr_t)pool_ptr, optGraph = (uintptr_t)graph_ptr;
    if (!Span(mesh, 0xD0u) || pool < 0x10318u || !Span(pool, 0x180u)) return 0;
    uintptr_t world = pool - 0x318u;

    /* Step A: Shared lock (<50ns, 100% parallel across all 14 render worker threads) to snapshot cached slot & plan. */
    coop_target_slot ts;
    CoopSlotCache cc;
    Manager mgr;
    uint32_t slotIdx = COOP_MAX_TARGETS;
    AcquireSRWLockShared(&s->metadata);
    uint32_t hidx = coop_hash_ptr(mesh);
    for (uint32_t p = 0; p < COOP_FAST_HT_SIZE; p++) {
        uint32_t k = (hidx + p) & COOP_FAST_HT_MASK;
        uintptr_t entry = coopFast.mesh_ht[k];
        if (!entry) break;
        if (entry == mesh) { slotIdx = coopFast.mesh_ht_slot[k]; break; }
    }
    int hit = 0;
    if (slotIdx < COOP_MAX_TARGETS) {
        const coop_target_slot *live = &coopReg.targets[slotIdx];
        const CoopSlotCache *lcc = &coopSlotCache[slotIdx];
        if (live->active && live->mesh == mesh && live->world == world &&
            lcc->valid && lcc->admit_epoch == live->admit_epoch && coopLastMgr.capacity > 0u) {
            ts = *live;
            cc = *lcc;
            mgr = coopLastMgr;
            hit = 1;
        }
    }
    ReleaseSRWLockShared(&s->metadata);
    if (!hit) return 0;

    /* Validate caller route against pinned 512B80 call sites. */
    int isNormal = (caller == engine + 0x258AABu && optGraph == ts.normal_graph);
    int isGrouped = (caller == engine + 0x258E19u && !optGraph);
    if (!isNormal && !isGrouped) return 0;
    uint32_t gi = ts.target_kind;
    const cm_garment *garment = cm_garment_at(gi);
    if (!garment || cc.child_nodes != garment->count || cc.mesh_node >= cc.child_nodes ||
        cc.mesh_node != cm_mesh_node_for(gi, ts.garment_ordinal)) {
        return 0;
    }

    /* Step B: Lock-free parallel live reads (~15 hf->read() calls total, ZERO exclusive locks). */
    unsigned char mbuf[0xA0];
    if (hf->read(mesh + 0x30u, mbuf, sizeof mbuf) != 0) return -1;
    if (U32(mbuf + 0x00u) != cc.mesh_node || U32(mbuf + 0x04u) != ts.child_graph_handle ||
        U64(mbuf + 0x08u) != ts.normal_graph || U64(mbuf + 0x40u) != cc.skin ||
        U64(mbuf + 0x50u) != cc.bind || U32(mbuf + 0x60u) != cc.groups ||
        U64(mbuf + 0x68u) != cc.sub || U32(mbuf + 0x98u) != cc.output_count) {
        return 0;
    }

    uintptr_t livePoolBase = 0;
    if (hf->read(pool + 0x170u, &livePoolBase, 8u) != 0 ||
        !Address(livePoolBase) || !Span(livePoolBase, (size_t)cc.output_count * 48u)) {
        return 0;
    }

    /* Verify live child & parent UnitManager slots/generations (ABA & teardown safe). */
    if (ts.child_index >= mgr.capacity || ts.parent_index >= mgr.capacity) return 0;
    uint8_t cg = 0, pg = 0; uintptr_t cu = 0, pu = 0;
    if (hf->read(mgr.generations + ts.child_index, &cg, 1u) != 0 ||
        hf->read(mgr.slots + (uintptr_t)ts.child_index * 8u, &cu, 8u) != 0 ||
        hf->read(mgr.generations + ts.parent_index, &pg, 1u) != 0 ||
        hf->read(mgr.slots + (uintptr_t)ts.parent_index * 8u, &pu, 8u) != 0) {
        return -1;
    }
    if (cu != ts.child_unit || cg != (uint8_t)ts.child_gen ||
        pu != ts.parent_unit || pg != (uint8_t)ts.parent_gen) {
        return 0; /* Unit recycled -> let slow path evict/rebind cleanly. */
    }

    unsigned char papp[16];
    if (!Span(ts.parent_unit, 0x70u) || hf->read(ts.parent_unit + 0x60u, papp, sizeof papp) != 0) return -1;
    if ((ts.expected_assoc && U64(papp + 8u) != ts.expected_assoc) ||
        (ts.parent_graph_handle && U32(papp + 4u) != ts.parent_graph_handle)) {
        return 0;
    }
    /* Verify live parent RenderHandle slot in RenderManager.handles[] (catches ship-to-planet teardown immediately). */
    uint32_t pslot = ts.parent_graph_handle & cc.handles_mask;
    uint32_t liveParentHandle = 0u;
    if (!pslot || hf->read(cc.handles_base + (uintptr_t)pslot * 4u, &liveParentHandle, 4u) != 0 ||
        liveParentHandle != ts.parent_graph_handle) {
        return 0;
    }

    unsigned char cgh[56], pgh[56];
    if (!Span(ts.normal_graph, 56u) || hf->read(ts.normal_graph, cgh, 56u) != 0) return -1;
    if (U64(cgh) != engine + 0x16880B0u || U32(cgh + 8u) != 2u ||
        U32(cgh + 0x10u) != cc.child_nodes || U32(cgh + 0x28u) != cc.child_nodes) {
        return 0;
    }
    uintptr_t carr0 = U64(cgh + 0x18u), carr1 = U64(cgh + 0x30u);
    size_t cbytes = (size_t)cc.child_nodes * 48u;
    if (!Span(carr0, cbytes) || !Span(carr1, cbytes)) return -1;

    if (!Span(cc.parent_normal_graph, 56u) || hf->read(cc.parent_normal_graph, pgh, 56u) != 0) return -1;
    if (U64(pgh) != engine + 0x16880B0u || U32(pgh + 8u) != 2u ||
        U32(pgh + 0x10u) != IV_PARENT_NODES || U32(pgh + 0x28u) != IV_PARENT_NODES) {
        return 0;
    }
    uintptr_t parr0 = U64(pgh + 0x18u), parr1 = U64(pgh + 0x30u);
    size_t pbytes = (size_t)IV_PARENT_NODES * 48u;
    if (!Span(parr0, pbytes) || !Span(parr1, pbytes)) return -1;

    /* Single read of the 4 pose arrays directly into slot->record.payload (no 48KB memset, no double-read). */
    unsigned char *raw = slot->record.payload;
    if (hf->read(carr0, raw + 1024u, cbytes) != 0 ||
        hf->read(carr1, raw + 7168u, cbytes) != 0 ||
        hf->read(parr0, raw + 13312u, pbytes) != 0 ||
        hf->read(parr1, raw + 18448u, pbytes) != 0) {
        return -1;
    }

    /* Evaluate compact LEGO math using parent-pose rig-solve cache (exact 5136B memcmp key, bit-for-bit identical to sm_evaluate). */
    sm_result *math = (sm_result *)(raw + SM_RAW_BYTES);
    _mm_setcsr(0x1f80u);
    const cm_render48 *parents[2] = {(const cm_render48 *)(raw + 13312u), (const cm_render48 *)(raw + 18448u)};
    const cm_render48 *graphs[2] = {(const cm_render48 *)(raw + 1024u), (const cm_render48 *)(raw + 7168u)};
    const uintptr_t parrs[2] = {parr0, parr1};
    cm_render48 *outputs[2] = {math->current, math->previous};
    int mathOk = 1;
    uint32_t poseHits = 0u;
    for (unsigned temporal = 0; temporal < 2u; temporal++) {
        cm_render48 packed[CM_SOURCE];
        uint32_t pck = (coop_hash_ptr(cc.parent_normal_graph ^ (parrs[temporal] << 3)) ^ temporal) & COOP_POSE_CACHE_MASK;
        CoopParentPoseEntry *pe = &coopPoseCache[pck];
        int poseCached = 0;
        AcquireSRWLockShared(&pe->lock);
        if (pe->valid && pe->parent_graph == cc.parent_normal_graph && pe->arr_ptr == parrs[temporal] &&
            memcmp(pe->parent_in, parents[temporal], pbytes) == 0) {
            memcpy(packed, pe->source_packed, sizeof packed);
            poseCached = 1;
        }
        ReleaseSRWLockShared(&pe->lock);

        cm_status st = CM_OK;
        if (poseCached) {
            poseHits++;
        } else {
            cm_mat4 h;
            st = coop_fast_inv3x4(&parents[temporal][100], &h);
            if (st == CM_OK) {
                st = cm_solve_parent(s->rig, parents[temporal], IV_PARENT_NODES, &h, &slot->work, packed);
            }
            if (st == CM_OK) {
                AcquireSRWLockExclusive(&pe->lock);
                pe->parent_graph = cc.parent_normal_graph;
                pe->arr_ptr = parrs[temporal];
                memcpy(pe->parent_in, parents[temporal], pbytes);
                memcpy(pe->source_packed, packed, sizeof packed);
                pe->valid = 1;
                ReleaseSRWLockExclusive(&pe->lock);
            }
        }
        if (st == CM_OK) {
            cm_mat4 f;
            memset(&f, 0, sizeof f);
            for (unsigned n = 0; n < 12u; n++) f.v[n] = graphs[temporal][cc.mesh_node].v[n];
            f.v[15] = 1.0;
            st = cm_apply_garment(s->rig, gi, cc.mesh_node, graphs[temporal], cc.child_nodes, &f, packed, &slot->work, outputs[temporal], SM_CAP);
        }
        if (st != CM_OK) { mathOk = 0; break; }
        for (unsigned n = 0; n < cc.child_nodes; n++) {
            int alias = 0;
            for (unsigned a = 0; a < garment->alias_count; a++) {
                if (garment->aliases[a].node == n) { alias = 1; break; }
            }
            if (!alias && memcmp(&outputs[temporal][n], &graphs[temporal][n], 48u) != 0) { mathOk = 0; break; }
        }
        if (!mathOk || memcmp(&outputs[temporal][cc.mesh_node], &graphs[temporal][cc.mesh_node], 48u) != 0) {
            mathOk = 0; break;
        }
    }
    _mm_setcsr(entry_csr);
    if (!mathOk) return -1;

    /* Lightweight post-math recheck (5 lock-free reads + 20ns shared-lock epoch check). */
    unsigned char cgh2[56], mref2[16], row0[48], row1[48];
    uintptr_t poolBase2 = 0;
    if (hf->read(ts.normal_graph, cgh2, 56u) != 0 || memcmp(cgh2, cgh, 56u) != 0 ||
        hf->read(carr0 + (uintptr_t)cc.mesh_node * 48u, row0, 48u) != 0 ||
        memcmp(row0, raw + 1024u + (uintptr_t)cc.mesh_node * 48u, 48u) != 0 ||
        hf->read(carr1 + (uintptr_t)cc.mesh_node * 48u, row1, 48u) != 0 ||
        memcmp(row1, raw + 7168u + (uintptr_t)cc.mesh_node * 48u, 48u) != 0 ||
        hf->read(mesh + 0x30u, mref2, 16u) != 0 || memcmp(mref2, mbuf, 16u) != 0 ||
        hf->read(pool + 0x170u, &poolBase2, 8u) != 0 || poolBase2 != livePoolBase) {
        InterlockedIncrement64(&s->passthrough_recheck);
        return -1;
    }
    int stillAlive = 0;
    AcquireSRWLockShared(&s->metadata);
    const coop_target_slot *live2 = &coopReg.targets[slotIdx];
    stillAlive = (live2->active && live2->admit_epoch == ts.admit_epoch && live2->mesh == mesh);
    ReleaseSRWLockShared(&s->metadata);
    if (!stillAlive) return -1;
    InterlockedIncrement64(&s->fast_hits);
    if (poseHits) InterlockedAdd64(&s->pose_hits, (LONG64)poseHits);

    /* Populate minimal lp_plan fields consumed by gw_leaf_dispatch (5B2990). */
    lp_plan *p = &slot->plan;
    p->graph = ts.normal_graph;
    p->arrays[0] = carr0;
    p->arrays[1] = carr1;
    p->bind = cc.bind;
    p->map = cc.map;
    p->pool_base = livePoolBase;
    p->count = cc.child_nodes;
    p->mesh_node = cc.mesh_node;
    p->groups = cc.groups;
    p->output_count = cc.output_count;
    memcpy(p->group, cc.group, sizeof cc.group);

    if (out_kind) *out_kind = ts.actor_kind;
    if (out_route) *out_route = isNormal ? 2u : 1u;
    return 1;
}

#endif
