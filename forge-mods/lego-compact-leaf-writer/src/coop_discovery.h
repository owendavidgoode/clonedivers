/* Dynamic multi-actor co-op target discovery, differential UnitManager reverse-join,
 * negative cache, PlayerManager 0..4 correlation, ABA rebinding, and 1E7CD0 handle retire.
 * All state lives in static BSS and is serialized by writer.metadata SRWLock. */
#ifndef COOP_DISCOVERY_H
#define COOP_DISCOVERY_H

#define COOP_GARMENT_CACHE_LIMIT 256u
#define COOP_NEG_CACHE_LIMIT 2048u
#define COOP_BULK_CHUNK 4096u

typedef struct CoopGarmentUnit {
    uint32_t index;
    uint32_t target_kind;
    uint32_t app_graph_handle;
    uint8_t generation;
    uintptr_t unit;
    uintptr_t association;
    uint64_t resource;
} CoopGarmentUnit;

typedef struct CoopNegEntry {
    uintptr_t mesh;
    uintptr_t cache;
    uintptr_t skin;
    uintptr_t world;
    uint32_t handle;
    uint32_t valid;
} CoopNegEntry;

static coop_registry coopReg;
static void InvocationClearFailures(void);
static Manager coopLastMgr;
static uintptr_t coopRawSlots[CAPACITY_LIMIT], coopScannedSlots[CAPACITY_LIMIT];
static uint8_t coopRawGens[CAPACITY_LIMIT], coopScannedGens[CAPACITY_LIMIT];
static CoopGarmentUnit coopGarments[COOP_GARMENT_CACHE_LIMIT];
static uint32_t coopGarmentCount;
static CoopNegEntry coopNegCache[COOP_NEG_CACHE_LIMIT];
static CoopRoster coopCachedRoster;
static uint64_t coopLastPeers[COOP_MAX_PLAYERS];
static uint32_t coopLastNetworks[COOP_MAX_PLAYERS], coopLastLifes[COOP_MAX_PLAYERS];
static uint32_t coopLastPlayerCount;
static uint64_t coopLastLocalPeer;
static int coopHasRosterSnapshot;

static uint32_t coop_hash_neg(uintptr_t mesh, uint32_t handle, uintptr_t cache, uintptr_t skin) {
    uint64_t x = (uint64_t)mesh ^ ((uint64_t)handle * 0x9E3779B97F4A7C15ULL) ^ ((uint64_t)cache >> 4) ^ ((uint64_t)skin >> 4);
    x ^= x >> 33; x *= 0xff51afd7ed558ccdULL; x ^= x >> 33;
    return (uint32_t)(x & (COOP_NEG_CACHE_LIMIT - 1u));
}

static int coop_neg_find(uintptr_t mesh, uint32_t handle, uintptr_t cache, uintptr_t skin, uintptr_t world) {
    uint32_t base = coop_hash_neg(mesh, handle, cache, skin);
    for (uint32_t probe = 0; probe < 4u; probe++) {
        const CoopNegEntry *e = &coopNegCache[(base + probe) & (COOP_NEG_CACHE_LIMIT - 1u)];
        if (e->valid && e->mesh == mesh && e->handle == handle && e->cache == cache && e->skin == skin && e->world == world)
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
        if (!e->valid || (e->mesh == mesh && e->handle == handle)) { pick = idx; break; }
    }
    coopNegCache[pick] = (CoopNegEntry){mesh, cache, skin, world, handle, 1u};
}

static void coop_neg_invalidate_handle(uintptr_t world, uint32_t handle) {
    if (!handle || handle == 0xFFFFFFu) return;
    for (uint32_t i = 0; i < COOP_NEG_CACHE_LIMIT; i++) {
        CoopNegEntry *e = &coopNegCache[i];
        if (!e->valid) continue;
        if (e->handle == handle && (!world || !e->world || e->world == world))
            e->valid = 0;
    }
}

static void coop_neg_clear_all(void) {
    memset(coopNegCache, 0, sizeof coopNegCache);
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

static int coop_refresh_units(const Manager *m, uintptr_t preserve_mesh) {
    if (!m || !m->capacity || m->capacity > CAPACITY_LIMIT) return 0;
    if (!coopLastMgr.address || m->address != coopLastMgr.address ||
        m->slots != coopLastMgr.slots || m->generations != coopLastMgr.generations) {
        memset(coopScannedSlots, 0, sizeof coopScannedSlots);
        memset(coopScannedGens, 0, sizeof coopScannedGens);
        coopGarmentCount = 0;
        coopLastMgr = *m;
        coop_neg_clear_all();
    } else if (m->capacity != coopLastMgr.capacity) {
        if (m->capacity < coopLastMgr.capacity) {
            for (uint32_t i = m->capacity; i < coopLastMgr.capacity; i++) {
                coopScannedSlots[i] = 0;
                coopScannedGens[i] = 0;
            }
            for (uint32_t i = 0; i < coopGarmentCount;) {
                if (coopGarments[i].index >= m->capacity)
                    coopGarments[i] = coopGarments[--coopGarmentCount];
                else
                    i++;
            }
        }
        coopLastMgr.capacity = m->capacity;
    }
    if (!coop_read_chunks(m->slots, coopRawSlots, (size_t)m->capacity * 8u) ||
        !coop_read_chunks(m->generations, coopRawGens, (size_t)m->capacity))
        return 0;
    /* Retire any active slot whose UnitManager index changed pointer/gen cleanly first. */
    for (uint32_t s = 0; s < COOP_MAX_TARGETS; s++) {
        coop_target_slot *ts = &coopReg.targets[s];
        if (!ts->active || (preserve_mesh && ts->mesh == preserve_mesh)) continue;
        int bad = 0;
        if (ts->child_unit && (ts->child_index >= m->capacity ||
            coopRawSlots[ts->child_index] != ts->child_unit ||
            coopRawGens[ts->child_index] != (uint8_t)ts->child_gen)) bad = 1;
        if (ts->parent_unit && (ts->parent_index >= m->capacity ||
            coopRawSlots[ts->parent_index] != ts->parent_unit ||
            coopRawGens[ts->parent_index] != (uint8_t)ts->parent_gen)) bad = 1;
        if (bad) {
            memset(ts, 0, sizeof *ts);
            if (coopReg.active_targets > 0) coopReg.active_targets--;
            coopReg.retirements++;
        }
    }
    unsigned char uhdr[112];
    int spawned_garment = 0;
    /* Keep cached garment units' render association & graph handle fresh across world transitions. */
    for (uint32_t i = 0; i < coopGarmentCount;) {
        CoopGarmentUnit *gu = &coopGarments[i];
        if (gu->index >= m->capacity || coopRawSlots[gu->index] != gu->unit || coopRawGens[gu->index] != gu->generation) {
            coopGarments[i] = coopGarments[--coopGarmentCount];
            continue;
        }
        unsigned char uapp[16];
        if (Span(gu->unit, 0x70u) && Read(gu->unit + 0x60u, uapp, sizeof uapp)) {
            uintptr_t assoc = U64(uapp + 8);
            uint32_t graphHandle = U32(uapp + 4);
            if (!Address(assoc) || assoc < 0x10010u || !graphHandle || graphHandle == 0xFFFFFFu) {
                coopScannedSlots[gu->index] = 0;
                coopGarments[i] = coopGarments[--coopGarmentCount];
                continue;
            }
            if (gu->association != assoc || gu->app_graph_handle != graphHandle) {
                gu->association = assoc;
                gu->app_graph_handle = graphHandle;
                spawned_garment = 1;
            }
        }
        i++;
    }
    for (uint32_t i = 0; i < m->capacity; i++) {
        uintptr_t u = coopRawSlots[i];
        uint8_t g = coopRawGens[i];
        if (u == coopScannedSlots[i] && g == coopScannedGens[i]) continue;
        coop_remove_garment_index(i);
        coopScannedSlots[i] = u;
        coopScannedGens[i] = g;
        if (!u || !Span(u, sizeof uhdr)) continue;
        if (!Read(u, uhdr, sizeof uhdr)) { coopScannedSlots[i] = 0; continue; }
        if (U64(uhdr) != engine + VTABLE_RVA) continue;
        uintptr_t resPtr = U64(uhdr + 0x30);
        uint64_t resId = 0;
        if (!Address(resPtr) || !Span(resPtr, 8) || !Read(resPtr, &resId, 8)) continue;
        uint32_t tk = 0;
        if (!coop_is_compact_garment(resId, &tk)) continue;
        uintptr_t assoc = U64(uhdr + 0x68);
        uint32_t graphHandle = U32(uhdr + 0x64);
        if (!Address(assoc) || assoc < 0x10010u || !graphHandle || graphHandle == 0xFFFFFFu) {
            /* Compact garment unit spawned before RenderWorld registration: re-scan next refresh. */
            coopScannedSlots[i] = 0;
            continue;
        }
        if (coopGarmentCount < COOP_GARMENT_CACHE_LIMIT) {
            coopGarments[coopGarmentCount++] = (CoopGarmentUnit){i, tk, graphHandle, g, u, assoc, resId};
            spawned_garment = 1;
        }
    }
    if (spawned_garment) coop_neg_clear_all();
    return 1;
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

static int coop_find_slot_ptr(const Manager *m, uintptr_t unit_ptr, uint32_t *out_idx) {
    uint32_t hits = 0;
    for (uint32_t i = 0; i < m->capacity; i++) {
        if (coopRawSlots[i] == unit_ptr) { *out_idx = i; hits++; }
    }
    return hits == 1;
}

static int coop_topology_valid(uint32_t target_kind, const UnitMeta *child, Topology topo[NODE_LIMIT]) {
    static const uint32_t expectedNodeCounts[7] = {21, 21, 43, 21, 43, 17, 29};
    if (target_kind >= 7u || child->nodes != expectedNodeCounts[target_kind]) return 0;
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
    memset(slot, 0, sizeof *slot);
    if (coopReg.active_targets > 0) coopReg.active_targets--;
    coopReg.retirements++;
}

static void CoopRetireHandle(gw_state *s, uintptr_t world, uint32_t handle) {
    if (!s || !handle || handle == 0xFFFFFFu) return;
    AcquireSRWLockExclusive(&s->metadata);
    uint32_t removed = coop_retire_handle_world(&coopReg, world, handle);
    coop_neg_invalidate_handle(world, handle);
    ReleaseSRWLockExclusive(&s->metadata);
    if (removed) InterlockedAdd64(&s->retires, (LONG64)removed);
}

static int CoopResolveTarget(gw_state *s, void *mesh_ptr, void *pool_ptr, void *graph_ptr,
                             uintptr_t caller, LONG64 sequence, se_target *out_target, coop_actor_kind *out_kind) {
    (void)caller; (void)sequence;
    if (!s || !mesh_ptr || !pool_ptr || !out_target || !out_kind) return 0;
    *out_kind = COOP_ACTOR_REJECTED;
    uintptr_t mesh = (uintptr_t)mesh_ptr, pool = (uintptr_t)pool_ptr, optGraph = (uintptr_t)graph_ptr;
    if (!Span(mesh, 0xD0) || pool < 0x10318u || !Span(pool, 0x180u)) return 0;

    int matched = 0;
    AcquireSRWLockExclusive(&s->metadata);
    coopProbeActive = 1; coopProbeReads = coopProbeBytes = 0;

    Manager mgr;
    if (!ManagerRead(&mgr)) goto out;

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
                    if (optGraph && optGraph != existing->normal_graph) goto out;
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
                    goto out;
                }
            }
        }
        /* Fast validation failed -> fall through to full probe / ABA rebind (do not trust stale slot!). */
    }

    if (existing) {
        coop_refresh_units(&mgr, mesh);
    }

    /* Full incoming MeshObject header check */
    unsigned char mhdr[0x78];
    if (!Read(mesh, mhdr, sizeof mhdr) || U64(mhdr) != engine + 0x1688950u || U32(mhdr + 8) != 3u) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }
    Ref normal = {U32(mhdr + 0x30), U32(mhdr + 0x34), U64(mhdr + 0x38)};
    uintptr_t skin = U64(mhdr + 0x70);
    if (!skin || !Address(skin) || !normal.handle || normal.handle == 0xFFFFFFu || !Address(normal.cache)) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }
    uintptr_t w = pool - 0x318u;
    if (!existing && coop_neg_find(mesh, normal.handle, normal.cache, skin, w)) goto out;

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
        goto out;
    }

    /* Reverse-join matching compact garment unit in UnitManager cache (differential refresh on miss or stale). */
    uintptr_t expectedAssoc = rhdr.manager + 0x10u;
    const CoopGarmentUnit *foundGu = NULL;
    uint32_t matchCount = 0;
    for (unsigned pass = 0; pass < 2u; pass++) {
        if (pass == 1u || memcmp(&mgr, &coopLastMgr, sizeof mgr) != 0) {
            if (!coop_refresh_units(&mgr, existing ? mesh : 0)) break;
        }
        foundGu = NULL; matchCount = 0;
        for (uint32_t i = 0; i < coopGarmentCount; i++) {
            if (coopGarments[i].association == expectedAssoc && coopGarments[i].app_graph_handle == normal.handle) {
                foundGu = &coopGarments[i];
                matchCount++;
            }
        }
        if (matchCount == 1u) {
            Identity liveId;
            if (IdentityRead(&mgr, foundGu->index, &liveId) == 0 &&
                liveId.unit == foundGu->unit && liveId.generation == foundGu->generation)
                break;
            foundGu = NULL; matchCount = 0;
        }
    }
    if (matchCount != 1u || !foundGu) {
        if (existing) coop_evict_slot(existing);
        if (matchCount == 0u) coop_neg_insert(mesh, normal.handle, normal.cache, skin, w);
        goto out;
    }

    /* Reject override optional_graph if non-null and != normal.cache (passthrough safely). */
    if (optGraph && optGraph != normal.cache) {
        if (existing) coop_evict_slot(existing);
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
        goto out;
    }
    const MeshMeta *mm = &rmeta.mesh[ordinal];
    uintptr_t descSkin = 0;
    if (mm->skin != skin || !Read(mm->descriptor + 0x80u, &descSkin, 8) || descSkin != skin) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }

    uint32_t parentIdx = 0;
    if (!rec.child.parent) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }
    if (!coop_find_slot_ptr(&mgr, rec.child.parent, &parentIdx)) {
        if (!coop_refresh_units(&mgr, existing ? mesh : 0) || !coop_find_slot_ptr(&mgr, rec.child.parent, &parentIdx)) {
            if (existing) coop_evict_slot(existing);
            goto out;
        }
    }
    if (!MetaRead(&mgr, parentIdx, &rec.parent, PARENT_NODE_LIMIT) ||
        rec.parent.id.unit != rec.child.parent ||
        rec.parent.id.resource != COOP_HUMAN_AVATAR_RESOURCE || rec.parent.nodes != IV_PARENT_NODES ||
        rec.child.externalWorld != rec.parent.world) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }
    rec.parentPresent = 1; rec.worldEqual = 1;
    if (!ChainRead(&rec) || rec.chainStatus || !rec.reciprocal) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }
    unsigned char papp[16];
    Resolved pgraph;
    if (!Span(rec.parent.id.unit, 0x70u) || !Read(rec.parent.id.unit + 0x60u, papp, 16) ||
        U64(papp + 8) != expectedAssoc || !Resolve(&rmeta.header, U32(papp + 4), 2u, &pgraph) ||
        pgraph.nodes != IV_PARENT_NODES) {
        if (existing) coop_evict_slot(existing);
        goto out;
    }

    coop_sync_roster_if_changed(&mgr, 1);
    if (coopReg.active_targets >= COOP_MAX_TARGETS && !existing) {
        /* Sweep dead UnitManager slots before admitting into a full registry. */
        coop_refresh_units(&mgr, 0);
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
    if (kind == COOP_ACTOR_REJECTED) goto out;

    coop_fill_se_target(&admitted, out_target);
    *out_kind = kind;
    matched = 1;

out:
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
    if (!ManagerRead(&openMgr)) {
        return coop_capture_passthrough(s, NULL, 0, 0, "unit_manager_read");
    }
    if (slot->child_index >= openMgr.capacity || slot->parent_index >= openMgr.capacity ||
        !MetaRead(&openMgr, slot->child_index, &out->child, NODE_LIMIT) ||
        out->child.id.unit != slot->child_unit || out->child.id.generation != slot->child_gen ||
        out->child.id.resource != slot->garment_resource || !Target(out->child.id.resource)) {
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
    if (!ChainRead(out) || out->chainStatus || !out->reciprocal) {
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
    if (!ManagerRead(&closeMgr) || closeMgr.slots != openMgr.slots || closeMgr.generations != openMgr.generations ||
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

#endif
