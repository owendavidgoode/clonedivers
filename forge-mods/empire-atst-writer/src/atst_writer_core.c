/* Runtime S2c leaf capture, 512B80/5B2990/1E7CD0 hooks, and fail-closed checks. */
#include "atst_writer_core.h"

#include <immintrin.h>
#include <string.h>

typedef struct {
    int active;
    at_writer_state *state;
    DWORD owner_tid;
    int hide_all;
    uint64_t mask_lo;
    uint64_t mask_hi;
} at_pilot_tls_frame;

static __thread at_tls_frame *tls_active_frame = NULL;
static __thread at_pilot_tls_frame *tls_pilot_frame = NULL;

static int span_ok(uintptr_t a, size_t n) {
    return a >= 0x10000ULL && a < 0x0000800000000000ULL && n <= 0x0000800000000000ULL - a;
}

static uint32_t u32_le(const unsigned char *p) {
    uint32_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static uint64_t u64_le(const unsigned char *p) {
    uint64_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static int default_local_read(uintptr_t addr, void *buf, size_t sz, void *user) {
    (void)user;
    if (!buf || !span_ok(addr, sz)) return -1;
    memcpy(buf, (const void *)addr, sz);
    return 0;
}

int at_writer_init(
    at_writer_state *s,
    uintptr_t engine_base,
    at_safe_read_fn read_fn,
    void *read_user) {
    if (!s || !engine_base) return 0;
    memset(s, 0, sizeof(*s));
    InitializeSRWLock(&s->lock);
    s->engine_base = engine_base;
    s->read_fn = read_fn ? read_fn : default_local_read;
    s->read_user = read_user;
    if (at_rig_init_pinned(&s->rig) != AT_OK) return 0;
    at_registry_init(&s->reg, engine_base);
    return 1;
}

_Static_assert(sizeof(uintptr_t) == sizeof(PVOID) && sizeof(size_t) == sizeof(PVOID), "64-bit pointer width");

static uint32_t at_hash_mesh_ptr(uintptr_t v) {
    uint64_t x = (uint64_t)v;
    x ^= x >> 33;
    x *= 0xff51afd7ed558ccdULL;
    x ^= x >> 33;
    return (uint32_t)x;
}

static void at_fast_publish(at_writer_state *s) {
    /* Caller holds s->lock exclusive (or single-threaded init). */
    uint32_t seq = __atomic_load_n(&s->fast.seq, __ATOMIC_RELAXED);
    __atomic_store_n(&s->fast.seq, seq + 1u, __ATOMIC_RELEASE); /* odd: writer updating */
    __atomic_thread_fence(__ATOMIC_RELEASE);
    memset(s->fast.mesh_ht, 0, sizeof(s->fast.mesh_ht));
    for (unsigned i = 0; i < AT_PILOT_MESH_HT_SIZE; i++) {
        s->fast.pilot_mesh_ht[i] = -1;
    }
    uint32_t pcnt = s->pilot_hide_count <= AT_MAX_PILOT_HIDE_MESHES ? s->pilot_hide_count : AT_MAX_PILOT_HIDE_MESHES;
    s->fast.pilot_mesh_count = pcnt;
    if (pcnt > 0u) {
        memcpy(s->fast.pilot_meshes, s->pilot_hide_entries, (size_t)pcnt * sizeof(s->pilot_hide_entries[0]));
        for (uint32_t pm = 0; pm < pcnt; pm++) {
            uintptr_t mobj = s->fast.pilot_meshes[pm].mesh;
            if (!mobj) continue;
            uint32_t h = at_hash_mesh_ptr(mobj) & AT_PILOT_MESH_HT_MASK;
            for (unsigned p = 0; p < AT_PILOT_MESH_HT_SIZE; p++) {
                uint32_t k = (h + p) & AT_PILOT_MESH_HT_MASK;
                if (s->fast.pilot_mesh_ht[k] < 0) {
                    s->fast.pilot_mesh_ht[k] = (int16_t)pm;
                    break;
                }
            }
        }
    } else {
        memset(s->fast.pilot_meshes, 0, sizeof(s->fast.pilot_meshes));
    }
    uint32_t cnt = 0u;
    uint32_t all_resolved = 1u;
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        const at_walker_slot *slot = &s->reg.slots[i];
        if (slot->occupied && cnt < AT_MAX_WALKERS) {
            s->fast.handles[cnt] = slot->graph_handle;
            s->fast.worlds[cnt] = slot->world;
            cnt++;
            if (!slot->meshes_resolved || slot->mesh_count == 0u) {
                all_resolved = 0u;
            } else {
                for (unsigned m = 0; m < slot->mesh_count && m < AT_MESH_DESC_COUNT; m++) {
                    uintptr_t mobj = slot->meshes[m];
                    if (!mobj) continue;
                    uint32_t h = at_hash_mesh_ptr(mobj) & AT_FAST_MESH_HT_MASK;
                    int inserted = 0;
                    for (unsigned p = 0; p < AT_FAST_MESH_HT_SIZE; p++) {
                        uint32_t k = (h + p) & AT_FAST_MESH_HT_MASK;
                        if (!s->fast.mesh_ht[k] || s->fast.mesh_ht[k] == mobj) {
                            s->fast.mesh_ht[k] = mobj;
                            inserted = 1;
                            break;
                        }
                    }
                    if (!inserted) all_resolved = 0u;
                }
            }
        }
    }
    uint32_t next_ep = (s->fast.filter_epoch + 1u) & 0xFFFFu;
    if (next_ep == 0u) {
        /* 16-bit wrap: clear negative cache so old tags never collide with epoch 1. */
        memset((void *)s->neg_mesh_ht, 0, sizeof(s->neg_mesh_ht));
        next_ep = 1u;
    }
    s->fast.filter_epoch = next_ep;
    s->fast.count = cnt;
    s->fast.all_meshes_resolved = (cnt > 0u && all_resolved) ? 1u : 0u;
    __atomic_store_n(&s->fast.seq, seq + 2u, __ATOMIC_RELEASE); /* even: snapshot ready */
}

void at_writer_republish_fast_locked(at_writer_state *s) {
    if (s) at_fast_publish(s);
}

static int at_fast_lookup_pilot_hide(const at_writer_state *s, uintptr_t mesh, at_pilot_hide_entry *out_entry) {
    if (!s || !mesh || !out_entry) return 0;
    for (unsigned attempt = 0; attempt < 4u; attempt++) {
        uint32_t seq1 = __atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE);
        if (seq1 & 1u) break;
        uint32_t pcnt = s->fast.pilot_mesh_count;
        if (pcnt == 0u) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        uint32_t h = at_hash_mesh_ptr(mesh) & AT_PILOT_MESH_HT_MASK;
        int hit = 0;
        at_pilot_hide_entry local_e;
        memset(&local_e, 0, sizeof(local_e));
        for (unsigned p = 0; p < AT_PILOT_MESH_HT_SIZE; p++) {
            int16_t idx = s->fast.pilot_mesh_ht[(h + p) & AT_PILOT_MESH_HT_MASK];
            if (idx < 0) break;
            if ((uint32_t)idx < pcnt && s->fast.pilot_meshes[idx].mesh == mesh) {
                local_e = s->fast.pilot_meshes[idx];
                hit = 1;
                break;
            }
        }
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) {
            if (hit) *out_entry = local_e;
            return hit;
        }
    }
    AcquireSRWLockShared((SRWLOCK *)&s->lock);
    int hit = 0;
    for (uint32_t i = 0; i < s->pilot_hide_count && i < AT_MAX_PILOT_HIDE_MESHES; i++) {
        if (s->pilot_hide_entries[i].mesh == mesh) {
            *out_entry = s->pilot_hide_entries[i];
            hit = 1;
            break;
        }
    }
    ReleaseSRWLockShared((SRWLOCK *)&s->lock);
    return hit;
}

static int at_fast_may_match_skin(at_writer_state *s, uintptr_t mesh, uintptr_t pool) {
    for (unsigned attempt = 0; attempt < 4u; attempt++) {
        uint32_t seq1 = __atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE);
        if (seq1 & 1u) return 1;
        uint32_t cnt = s->fast.count;
        if (cnt == 0u) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        if (pool < 0x318ULL + 0x10000ULL || !span_ok(mesh, 0xD0u)) return 0;
        uintptr_t world = pool - 0x318u;
        int world_hit = 0;
        for (uint32_t i = 0; i < cnt && i < AT_MAX_WALKERS; i++) {
            if (s->fast.worlds[i] == world) { world_hit = 1; break; }
        }
        if (!world_hit) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        uint32_t mh = at_hash_mesh_ptr(mesh);
        int mesh_hit = 0;
        for (unsigned p = 0; p < AT_FAST_MESH_HT_SIZE; p++) {
            uintptr_t entry = s->fast.mesh_ht[(mh + p) & AT_FAST_MESH_HT_MASK];
            if (!entry) break;
            if (entry == mesh) { mesh_hit = 1; break; }
        }
        if (mesh_hit) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 1;
            continue;
        }
        if (s->fast.all_meshes_resolved) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        uint32_t ep = s->fast.filter_epoch;
        uint64_t neg_tag = ((uint64_t)(ep ? ep : 1u) << 48) | ((uint64_t)mesh & 0x0000FFFFFFFFFFFFULL);
        uint32_t neg_idx = mh & AT_NEG_MESH_HT_MASK;
        if (__atomic_load_n(&s->neg_mesh_ht[neg_idx], __ATOMIC_RELAXED) == neg_tag) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        DWORD saved_err = GetLastError();
        uint32_t m_handle = 0u;
        int rd_err = (!s->read_fn || s->read_fn(mesh + 0x34u, &m_handle, sizeof(m_handle), s->read_user) != 0);
        SetLastError(saved_err);
        if (rd_err) return 1;
        int matched = 0;
        if (m_handle && m_handle != 0xFFFFFFu) {
            for (uint32_t i = 0; i < cnt && i < AT_MAX_WALKERS; i++) {
                if (s->fast.worlds[i] == world && s->fast.handles[i] == m_handle) { matched = 1; break; }
            }
        }
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) {
            if (!matched) {
                __atomic_store_n(&s->neg_mesh_ht[neg_idx], neg_tag, __ATOMIC_RELAXED);
            }
            return matched;
        }
    }
    return 1;
}

static int at_fast_may_match_retire(const at_writer_state *s, uintptr_t world, uint32_t handle) {
    for (unsigned attempt = 0; attempt < 4u; attempt++) {
        uint32_t seq1 = __atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE);
        if (seq1 & 1u) return 1;
        uint32_t cnt = s->fast.count;
        if (cnt == 0u) {
            __atomic_thread_fence(__ATOMIC_ACQUIRE);
            if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return 0;
            continue;
        }
        int matched = 0;
        for (uint32_t i = 0; i < cnt && i < AT_MAX_WALKERS; i++) {
            if (s->fast.worlds[i] == world && s->fast.handles[i] == handle) { matched = 1; break; }
        }
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (__atomic_load_n(&s->fast.seq, __ATOMIC_ACQUIRE) == seq1) return matched;
    }
    return 1;
}

void at_writer_shutdown(at_writer_state *s) {
    if (!s) return;
    InterlockedExchange(&s->ready, 0);
    AcquireSRWLockExclusive(&s->lock);
    at_registry_reset(&s->reg);
    s->pilot_hide_count = 0u;
    memset(s->pilot_hide_entries, 0, sizeof(s->pilot_hide_entries));
    memset(s->pilot.slots, 0, sizeof(s->pilot.slots));
    at_fast_publish(s);
    ReleaseSRWLockExclusive(&s->lock);
}

void at_writer_set_peer_lookup(
    at_writer_state *s,
    at_peer_lookup_fn lookup_fn,
    void *lookup_user) {
    if (!s) return;
    s->peer_lookup_fn = lookup_fn;
    s->peer_lookup_user = lookup_user;
    InterlockedExchangePointer((PVOID *)&s->peer_leaf_base, NULL);
    InterlockedExchangePointer((PVOID *)&s->peer_leaf_size, NULL);
    if (lookup_fn) {
        size_t sz = 0;
        uintptr_t base = lookup_fn(AT_PEER_LEAF_DLL, &sz, lookup_user);
        if (base && sz && sz <= UINTPTR_MAX - base) {
            InterlockedExchangePointer((PVOID *)&s->peer_leaf_size, (PVOID)sz);
            InterlockedExchangePointer((PVOID *)&s->peer_leaf_base, (PVOID)base);
        }
    }
}

int at_is_peer_caller(at_writer_state *s, uintptr_t caller) {
    if (!s || !caller) return 0;
    uintptr_t base = (uintptr_t)InterlockedCompareExchangePointer((PVOID *)&s->peer_leaf_base, NULL, NULL);
    size_t size = (size_t)InterlockedCompareExchangePointer((PVOID *)&s->peer_leaf_size, NULL, NULL);
    if ((!base || !size) && s->peer_lookup_fn) {
        size_t looked_up_size = 0;
        uintptr_t looked_up_base = s->peer_lookup_fn(AT_PEER_LEAF_DLL, &looked_up_size, s->peer_lookup_user);
        if (looked_up_base && looked_up_size && looked_up_size <= UINTPTR_MAX - looked_up_base) {
            InterlockedExchangePointer((PVOID *)&s->peer_leaf_size, (PVOID)looked_up_size);
            InterlockedExchangePointer((PVOID *)&s->peer_leaf_base, (PVOID)looked_up_base);
            base = looked_up_base;
            size = looked_up_size;
        }
    }
    if (!base || !size || size > UINTPTR_MAX - base) return 0;
    return (caller >= base && caller - base < size) ? 1 : 0;
}

int at_lp_capture(
    const at_writer_state *s,
    uintptr_t mesh,
    uintptr_t pool,
    uintptr_t expected_graph,
    at_lp_plan *out_plan) {
    if (!s || !s->read_fn || !out_plan || !span_ok(mesh, 0xD0u) || !span_ok(pool, 0x178u)) return 0;
    memset(out_plan, 0, sizeof(*out_plan));

    unsigned char mbuf[0xD0];
    if (s->read_fn(mesh, mbuf, sizeof(mbuf), s->read_user) != 0) return 0;

    uint32_t mesh_node = u32_le(mbuf + 0x30u);
    uintptr_t graph = (uintptr_t)u64_le(mbuf + 0x38u);
    uintptr_t skin = (uintptr_t)u64_le(mbuf + 0x70u);
    uintptr_t bind = (uintptr_t)u64_le(mbuf + 0x80u);
    uint32_t groups = u32_le(mbuf + 0x90u);
    uintptr_t sub = (uintptr_t)u64_le(mbuf + 0x98u);
    uint32_t out_cnt = u32_le(mbuf + 0xC8u);

    int node_valid = 0;
    for (unsigned i = 0; i < AT_MESH_DESC_COUNT; i++) {
        if (mesh_node == AT_DESC_NODES[i]) {
            node_valid = 1;
            break;
        }
    }
    if (!node_valid || graph != expected_graph || !span_ok(graph, 56u)) return 0;
    if (groups != AT_SKIN_GROUPS_COUNT || out_cnt != AT_SKIN_OUTPUT_COUNT) return 0;

    unsigned char gbuf[56];
    if (s->read_fn(graph, gbuf, sizeof(gbuf), s->read_user) != 0) return 0;
    uintptr_t arr0 = (uintptr_t)u64_le(gbuf + 24u);
    uintptr_t arr1 = (uintptr_t)u64_le(gbuf + 48u);
    if (!span_ok(arr0, AT_S2C_NODES * 48u) || !span_ok(arr1, AT_S2C_NODES * 48u)) return 0;

    uintptr_t pool_base = 0;
    if (s->read_fn(pool + 0x170u, &pool_base, sizeof(pool_base), s->read_user) != 0 ||
        !span_ok(pool_base, 16u)) {
        return 0;
    }

    unsigned char skin_hdr[16];
    if (!span_ok(skin, 16u) || s->read_fn(skin, skin_hdr, sizeof(skin_hdr), s->read_user) != 0) return 0;
    uint32_t bones = u32_le(skin_hdr + 0u);
    uint32_t map_off = u32_le(skin_hdr + 8u);
    uint32_t dir_off = u32_le(skin_hdr + 12u);
    if (bones != AT_SKIN_BONES_COUNT || !map_off || !dir_off) return 0;

    uintptr_t map_addr = skin + map_off;
    uintptr_t dir_addr = skin + dir_off;
    uint32_t map_buf[AT_SKIN_BONES_COUNT];
    if (!span_ok(map_addr, sizeof(map_buf)) || s->read_fn(map_addr, map_buf, sizeof(map_buf), s->read_user) != 0) return 0;
    if (memcmp(map_buf, AT_SKIN_BONES, sizeof(map_buf)) != 0) return 0;

    uint32_t bind_buf[AT_SKIN_BONES_COUNT * 12];
    if (!span_ok(bind, sizeof(bind_buf)) || s->read_fn(bind, bind_buf, sizeof(bind_buf), s->read_user) != 0) return 0;
    if (memcmp(bind_buf, AT_BINDS_48_U32, sizeof(bind_buf)) != 0) return 0;

    unsigned char dir_buf[4 + 8 * AT_SKIN_GROUPS_COUNT];
    if (!span_ok(dir_addr, sizeof(dir_buf)) || s->read_fn(dir_addr, dir_buf, sizeof(dir_buf), s->read_user) != 0) return 0;
    if (u32_le(dir_buf) != AT_SKIN_GROUPS_COUNT) return 0;

    unsigned char sub_buf[AT_SKIN_GROUPS_COUNT * 40u];
    if (!span_ok(sub, sizeof(sub_buf)) || s->read_fn(sub, sub_buf, sizeof(sub_buf), s->read_user) != 0) return 0;

    const uint32_t *expected_pals[AT_SKIN_GROUPS_COUNT] = { AT_PALETTE_0, AT_PALETTE_1, AT_PALETTE_2 };
    uint32_t pal_tmp[AT_SKIN_BONES_COUNT];

    for (unsigned i = 0; i < groups; i++) {
        const unsigned char *r = sub_buf + i * 40u;
        uint32_t q = u32_le(r + 24u);
        if (q >= AT_SKIN_GROUPS_COUNT) return 0;
        uint32_t pal_off = u32_le(dir_buf + 4u + 8u * q);
        uint32_t pal_cnt = u32_le(dir_buf + 8u + 8u * q);
        if (pal_cnt != AT_GROUP_COUNTS[q] || pal_cnt > AT_SKIN_BONES_COUNT) return 0;

        uintptr_t pal_addr = dir_addr + pal_off;
        if (!span_ok(pal_addr, (size_t)pal_cnt * 4u) ||
            s->read_fn(pal_addr, pal_tmp, (size_t)pal_cnt * 4u, s->read_user) != 0) {
            return 0;
        }
        if (memcmp(pal_tmp, expected_pals[q], (size_t)pal_cnt * 4u) != 0) return 0;

        out_plan->group[i].palette = pal_addr;
        out_plan->group[i].count = pal_cnt;
        for (unsigned t = 0; t < 2u; t++) {
            uint32_t encoded = u32_le(r + 28u + 4u * t);
            if (encoded % 3u != 0u) return 0;
            uint32_t at = encoded / 3u;
            if (at > out_cnt || pal_cnt > out_cnt - at) return 0;
            out_plan->group[i].offset[t] = at * 48u;
        }
    }

    out_plan->graph = graph;
    out_plan->arrays[0] = arr0;
    out_plan->arrays[1] = arr1;
    out_plan->bind = bind;
    out_plan->map = map_addr;
    out_plan->pool_base = pool_base;
    out_plan->mesh_node = mesh_node;
    out_plan->groups = groups;
    out_plan->output_count = out_cnt;
    return 1;
}

int at_lp_recheck(
    const at_writer_state *s,
    uintptr_t mesh,
    uintptr_t pool,
    const at_lp_plan *captured) {
    if (!captured) return 0;
    at_lp_plan fresh;
    if (!at_lp_capture(s, mesh, pool, captured->graph, &fresh)) return 0;
    return memcmp(&fresh, captured, sizeof(fresh)) == 0;
}

static uint32_t call_skin_passthrough(
    at_writer_state *s,
    void *mesh,
    void *pool,
    void *opt_graph,
    DWORD entry_err,
    uint32_t entry_csr) {
    at_skin_job_fn next = __atomic_load_n(&s->skin_next, __ATOMIC_ACQUIRE);
    SetLastError(entry_err);
    _mm_setcsr(entry_csr);
    uint32_t ret = next ? next(mesh, pool, opt_graph) : UINT32_MAX;
    DWORD exit_err = GetLastError();
    uint32_t exit_csr = _mm_getcsr();
    SetLastError(exit_err);
    _mm_setcsr(exit_csr);
    return ret;
}

uint32_t at_writer_dispatch_512B80(
    at_writer_state *s,
    void *mesh,
    void *pool,
    void *opt_graph,
    uintptr_t caller,
    at_receipt *out_receipt) {
    if (out_receipt) memset(out_receipt, 0, sizeof(*out_receipt));
    if (!s || !mesh || !pool) {
        return UINT32_MAX;
    }
    at_skin_job_fn fast_next = __atomic_load_n(&s->skin_next, __ATOMIC_ACQUIRE);
    if (!__atomic_load_n(&s->ready, __ATOMIC_ACQUIRE)) {
        return fast_next ? fast_next(mesh, pool, opt_graph) : UINT32_MAX;
    }
    if (fast_next == s->approved_skin_next &&
        __atomic_load_n(&s->leaf_next, __ATOMIC_ACQUIRE) == s->approved_leaf_next) {
        at_pilot_hide_entry ph_entry;
        if (at_fast_lookup_pilot_hide(s, (uintptr_t)mesh, &ph_entry)) {
            DWORD p_err = GetLastError();
            uint32_t p_csr = _mm_getcsr();
            at_pilot_tls_frame pframe;
            pframe.active = 1;
            pframe.state = s;
            pframe.owner_tid = GetCurrentThreadId();
            pframe.hide_all = ph_entry.hide_all ? 1 : 0;
            pframe.mask_lo = ph_entry.mask_lo;
            pframe.mask_hi = ph_entry.mask_hi;
            at_pilot_tls_frame *prev_p = tls_pilot_frame;
            tls_pilot_frame = &pframe;
            SetLastError(p_err);
            _mm_setcsr(p_csr);
            uint32_t pret = fast_next ? fast_next(mesh, pool, opt_graph) : UINT32_MAX;
            DWORD exit_err = GetLastError();
            uint32_t exit_csr = _mm_getcsr();
            tls_pilot_frame = prev_p;
            SetLastError(exit_err);
            _mm_setcsr(exit_csr);
            return pret;
        }
        /* Lock-free fast reject: zero GetLastError, zero _mm_getcsr, zero shared atomic writes,
         * and zero ReadProcessMemory syscalls in steady state across all 14 render threads. */
        if (!at_fast_may_match_skin(s, (uintptr_t)mesh, (uintptr_t)pool)) {
            return fast_next ? fast_next(mesh, pool, opt_graph) : UINT32_MAX;
        }
    }

    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();

    if (__atomic_load_n(&s->skin_next, __ATOMIC_ACQUIRE) != s->approved_skin_next ||
        __atomic_load_n(&s->leaf_next, __ATOMIC_ACQUIRE) != s->approved_leaf_next) {
        InterlockedIncrement((LONG *)&s->chain_failures);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    int from_peer = (caller != s->engine_base + AT_RVA_CALLER_NORMAL &&
                     caller != s->engine_base + AT_RVA_CALLER_REST)
                        ? at_is_peer_caller(s, caller)
                        : 0;
    int is_normal = ((caller == s->engine_base + AT_RVA_CALLER_NORMAL) || from_peer) && (opt_graph != NULL);
    int is_rest = ((caller == s->engine_base + AT_RVA_CALLER_REST) || from_peer) && (opt_graph == NULL);
    if (!is_normal && !is_rest) {
        InterlockedIncrement((LONG *)&s->caller_rejects);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    at_resolved_graph rgraph;
    uint32_t captured_epoch = 0;
    at_lp_plan plan;
    at_render48 raw_cur[AT_S2C_NODES];
    at_render48 raw_prev[AT_S2C_NODES];
    at_tls_frame frame;
    memset(&frame, 0, sizeof(frame));

    AcquireSRWLockExclusive(&s->lock);
    uint32_t prev_scavenged = s->reg.scavenged;
    int slot_idx = at_registry_resolve_render(
        &s->reg, (uintptr_t)pool, (uintptr_t)mesh, (uintptr_t)opt_graph,
        s->read_fn, s->read_user, &captured_epoch, &rgraph);
    if (s->reg.scavenged != prev_scavenged) at_fast_publish(s);
    if (slot_idx < 0) {
        ReleaseSRWLockExclusive(&s->lock);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }
    if (!at_lp_capture(s, (uintptr_t)mesh, (uintptr_t)pool, rgraph.object, &plan)) {
        ReleaseSRWLockExclusive(&s->lock);
        InterlockedIncrement((LONG *)&s->capture_rejects);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }
    if (s->read_fn(plan.arrays[0], raw_cur, sizeof(raw_cur), s->read_user) != 0 ||
        s->read_fn(plan.arrays[1], raw_prev, sizeof(raw_prev), s->read_user) != 0) {
        ReleaseSRWLockExclusive(&s->lock);
        InterlockedIncrement((LONG *)&s->capture_rejects);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }
    ReleaseSRWLockExclusive(&s->lock);

    /* Perform S2c joint-aware evaluation into thread-private TLS stack frame outside lock (preserve FE_TONEAREST). */
    _mm_setcsr((entry_csr & ~0x6000u)); /* Ensure round-to-nearest during math without leaking to caller */
    at_status eval_st = at_evaluate_s2c_pair(&s->rig, raw_cur, raw_prev, AT_S2C_NODES, &frame.priv);
    _mm_setcsr(entry_csr);
    if (eval_st != AT_OK) {
        InterlockedIncrement((LONG *)&s->math_failures);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    /* Re-acquire lock to verify slot wasn't retired by 1E7CD0 or mutated during capture/eval! */
    AcquireSRWLockExclusive(&s->lock);
    const at_walker_slot *slot = &s->reg.slots[slot_idx];
    if (!slot->occupied || slot->epoch != captured_epoch || slot->normal_graph != rgraph.object) {
        ReleaseSRWLockExclusive(&s->lock);
        InterlockedIncrement((LONG *)&s->retired_mid_call);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }
    if (!at_lp_recheck(s, (uintptr_t)mesh, (uintptr_t)pool, &plan)) {
        ReleaseSRWLockExclusive(&s->lock);
        InterlockedIncrement((LONG *)&s->recheck_rejects);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }
    ReleaseSRWLockExclusive(&s->lock);

    frame.active = 1;
    frame.state = s;
    frame.slot_index = slot_idx;
    frame.slot_epoch = captured_epoch;
    frame.owner_tid = GetCurrentThreadId();
    frame.plan = plan;
    frame.receipt.expected = plan.groups * 2u;

    at_tls_frame *prev_tls = tls_active_frame;
    tls_active_frame = &frame;

    SetLastError(entry_err);
    _mm_setcsr(entry_csr);
    uint32_t delegate_csr = _mm_getcsr();
    at_skin_job_fn next = (at_skin_job_fn)InterlockedCompareExchangePointer((PVOID *)&s->skin_next, NULL, NULL);
    uint32_t result = next ? next(mesh, pool, opt_graph) : UINT32_MAX;
    uint32_t return_csr = _mm_getcsr();
    DWORD return_err = GetLastError();

    tls_active_frame = prev_tls;

    /* Mask sticky x86 SSE exception status bits (0x3Fu: IE..PE) while strictly enforcing rounding/FTZ/DAZ/mask control bits. */
    frame.receipt.environment_ok = (((delegate_csr ^ entry_csr) & ~0x3Fu) == 0u &&
                                    ((return_csr ^ entry_csr) & ~0x3Fu) == 0u) ? 1u : 0u;
    frame.receipt.allocation_ok = (frame.reservation >= plan.pool_base &&
                                   frame.reservation - plan.pool_base == (uint64_t)result * 16ULL)
                                      ? 1u
                                      : 0u;

    if (!frame.receipt.environment_ok) InterlockedIncrement((LONG *)&s->env_failures);
    if (frame.receipt.failure || frame.receipt.seen != frame.receipt.expected ||
        frame.receipt.replaced != frame.receipt.expected || !frame.receipt.allocation_ok) {
        frame.receipt.failure = frame.receipt.failure ? frame.receipt.failure : 1u;
        InterlockedIncrement((LONG *)&s->leaf_failures);
    } else {
        InterlockedIncrement((LONG *)&s->substitutions);
    }

    if (out_receipt) *out_receipt = frame.receipt;
    SetLastError(return_err);
    _mm_setcsr(return_csr);
    return result;
}

uint32_t at_writer_leaf_5B2990(
    at_writer_state *s,
    void *dst,
    const void *factor,
    const void *graph,
    const void *bind,
    const void *palette,
    const void *map,
    uint32_t count,
    uintptr_t caller) {
    at_leaf_skin_fn next = __atomic_load_n(&s->leaf_next, __ATOMIC_ACQUIRE);
    at_tls_frame *f = tls_active_frame;
    if (!f || !f->active || f->state != s) {
        at_pilot_tls_frame *pf = tls_pilot_frame;
        if (pf && pf->active && pf->state == s && pf->owner_tid == GetCurrentThreadId() &&
            (void *)next == (void *)s->approved_leaf_next) {
            DWORD p_err = GetLastError();
            uint32_t p_csr = _mm_getcsr();
            tls_pilot_frame = NULL;
            SetLastError(p_err);
            _mm_setcsr(p_csr);
            uint32_t pret = next ? next(dst, factor, graph, bind, palette, map, count) : UINT32_MAX;
            DWORD exit_err = GetLastError();
            uint32_t exit_csr = _mm_getcsr();
            tls_pilot_frame = pf;
            if (at_pilot_collapse_leaf_output(
                    dst, palette, map, count, pf->hide_all, pf->mask_lo, pf->mask_hi,
                    s->read_fn, s->read_user) > 0u) {
                InterlockedIncrement((LONG *)&s->pilot.hidden_leaf_calls);
            }
            SetLastError(exit_err);
            _mm_setcsr(exit_csr);
            return pret;
        }
        return next ? next(dst, factor, graph, bind, palette, map, count) : UINT32_MAX;
    }
    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();
    const void *chosen = graph;
    int replace = 0;

    if (1) {
        at_receipt *r = &f->receipt;
        const at_lp_plan *p = &f->plan;
        uint32_t at = r->seen++;
        uint32_t group = at / 2u;
        uint32_t t = at % 2u;
        int slot_alive = 0;
        AcquireSRWLockShared(&s->lock);
        if (f->slot_index >= 0 && (unsigned)f->slot_index < AT_MAX_WALKERS) {
            const at_walker_slot *slot = &s->reg.slots[f->slot_index];
            slot_alive = (slot->occupied && slot->epoch == f->slot_epoch) ? 1 : 0;
        }
        ReleaseSRWLockShared(&s->lock);

        int ok = !r->failure &&
                 f->owner_tid == GetCurrentThreadId() &&
                 slot_alive &&
                 at < r->expected &&
                 group < p->groups &&
                 (caller == s->engine_base + (t ? AT_RVA_LEAF_PREV_RET : AT_RVA_LEAF_CUR_RET) ||
                  at_is_peer_caller(s, caller)) &&
                 (uintptr_t)graph == p->arrays[t] &&
                 (uintptr_t)bind == p->bind &&
                 (uintptr_t)map == p->map &&
                 (uintptr_t)palette == p->group[group].palette &&
                 count == p->group[group].count &&
                 (uintptr_t)factor >= 0x10000ULL &&
                 ((uintptr_t)factor % 16ULL) == 0ULL &&
                 (uintptr_t)dst >= p->group[group].offset[t];

        if (ok) {
            uintptr_t base = (uintptr_t)dst - p->group[group].offset[t];
            if (at == 0u) f->reservation = base;
            ok = (base == f->reservation) &&
                 (base >= p->pool_base) &&
                 span_ok(base, (size_t)p->output_count * 48u) &&
                 ((base - p->pool_base) % 16ULL) == 0ULL &&
                 (base - p->pool_base) <= UINT32_MAX;
        }
        if ((void *)next != (void *)s->approved_leaf_next) ok = 0;

        if (ok) {
            chosen = (const void *)f->priv.graph[t];
            replace = 1;
        } else {
            r->failure = 1u;
        }
    }

    /* Mask active TLS frame across next() call so re-entrant/foreign draws stay untouched. */
    tls_active_frame = NULL;
    SetLastError(entry_err);
    _mm_setcsr(entry_csr);
    uint32_t result = next ? next(dst, factor, chosen, bind, palette, map, count) : UINT32_MAX;
    uint32_t exit_csr = _mm_getcsr();
    DWORD exit_err = GetLastError();
    tls_active_frame = f;

    if (f && f->active && f->state == s) {
        if (((exit_csr ^ entry_csr) & ~0x3Fu) != 0u) f->receipt.failure = 2u;
        if (replace) f->receipt.replaced++;
    }

    SetLastError(exit_err);
    _mm_setcsr(exit_csr);
    return result;
}

uint64_t at_writer_retire_1E7CD0(
    at_writer_state *s,
    uintptr_t world,
    void *packet,
    uintptr_t aux) {
    if (!s) return 0;
    if (__atomic_load_n(&s->fast.count, __ATOMIC_ACQUIRE) == 0u) {
        at_render_remove_fn fast_next = __atomic_load_n(&s->remove_next, __ATOMIC_ACQUIRE);
        return fast_next ? fast_next(world, packet, aux) : 0ULL;
    }
    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();

    if (world && packet && span_ok((uintptr_t)packet, 8u)) {
        uint32_t handle = 0;
        if (s->read_fn((uintptr_t)packet + 4u, &handle, sizeof(handle), s->read_user) == 0 &&
            at_fast_may_match_retire(s, world, handle)) {
            AcquireSRWLockExclusive(&s->lock);
            if (at_registry_retire_handle(&s->reg, world, handle) > 0u) at_fast_publish(s);
            ReleaseSRWLockExclusive(&s->lock);
        }
    }

    at_render_remove_fn next = __atomic_load_n(&s->remove_next, __ATOMIC_ACQUIRE);
    SetLastError(entry_err);
    _mm_setcsr(entry_csr);
    uint64_t ret = next ? next(world, packet, aux) : 0ULL;
    DWORD exit_err = GetLastError();
    uint32_t exit_csr = _mm_getcsr();
    SetLastError(exit_err);
    _mm_setcsr(exit_csr);
    return ret;
}

uint32_t at_writer_tick(at_writer_state *s, uint32_t max_scan) {
    if (!s || !__atomic_load_n(&s->ready, __ATOMIC_ACQUIRE)) return 0u;
    AcquireSRWLockExclusive(&s->lock);
    uint32_t prev_scavenged = s->reg.scavenged;
    uint32_t prev_mesh_res = s->reg.mesh_resolutions;
    uint32_t admitted = at_registry_reconcile(&s->reg, s->read_fn, s->read_user, max_scan);
    if (admitted > 0u || s->reg.scavenged != prev_scavenged || s->reg.mesh_resolutions != prev_mesh_res) {
        at_fast_publish(s);
    }
    ReleaseSRWLockExclusive(&s->lock);
    return admitted;
}

const at_tls_frame *at_writer_current_frame(void) {
    return tls_active_frame;
}
