/* Runtime S2c leaf capture, 512B80/5B2990/1E7CD0 hooks, and fail-closed checks. */
#include "atst_writer_core.h"

#include <immintrin.h>
#include <string.h>

static __thread at_tls_frame *tls_active_frame = NULL;

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

void at_writer_shutdown(at_writer_state *s) {
    if (!s) return;
    InterlockedExchange(&s->ready, 0);
    AcquireSRWLockExclusive(&s->lock);
    at_registry_reset(&s->reg);
    ReleaseSRWLockExclusive(&s->lock);
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
    at_skin_job_fn next = (at_skin_job_fn)InterlockedCompareExchangePointer((PVOID *)&s->skin_next, NULL, NULL);
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
    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();
    if (out_receipt) memset(out_receipt, 0, sizeof(*out_receipt));
    if (!s || !mesh || !pool) {
        return UINT32_MAX;
    }
    if (!InterlockedCompareExchange(&s->ready, 0, 0)) {
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    if (InterlockedCompareExchangePointer((PVOID *)&s->skin_next, NULL, NULL) != s->approved_skin_next ||
        InterlockedCompareExchangePointer((PVOID *)&s->leaf_next, NULL, NULL) != s->approved_leaf_next) {
        InterlockedIncrement((LONG *)&s->chain_failures);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    int is_normal = (caller == s->engine_base + AT_RVA_CALLER_NORMAL) && (opt_graph != NULL);
    int is_rest = (caller == s->engine_base + AT_RVA_CALLER_REST) && (opt_graph == NULL);
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
    at_private_graph evaluated;

    AcquireSRWLockExclusive(&s->lock);
    int slot_idx = at_registry_resolve_render(
        &s->reg, (uintptr_t)pool, (uintptr_t)mesh, (uintptr_t)opt_graph,
        s->read_fn, s->read_user, &captured_epoch, &rgraph);
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

    /* Perform S2c joint-aware evaluation into stack buffer outside lock (preserve FE_TONEAREST). */
    _mm_setcsr((entry_csr & ~0x6000u)); /* Ensure round-to-nearest during math without leaking to caller */
    at_status eval_st = at_evaluate_s2c_pair(&s->rig, raw_cur, raw_prev, AT_S2C_NODES, &evaluated);
    _mm_setcsr(entry_csr);
    if (eval_st != AT_OK) {
        InterlockedIncrement((LONG *)&s->math_failures);
        InterlockedIncrement((LONG *)&s->passthroughs);
        return call_skin_passthrough(s, mesh, pool, opt_graph, entry_err, entry_csr);
    }

    /* Re-acquire lock to verify slot wasn't retired by 1E7CD0 or mutated during capture/eval! */
    AcquireSRWLockExclusive(&s->lock);
    at_walker_slot *slot = &s->reg.slots[slot_idx];
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
    slot->priv = evaluated;
    ReleaseSRWLockExclusive(&s->lock);

    at_tls_frame frame;
    memset(&frame, 0, sizeof(frame));
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

    frame.receipt.environment_ok = (delegate_csr == entry_csr && return_csr == entry_csr) ? 1u : 0u;
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
    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();

    at_leaf_skin_fn next = (at_leaf_skin_fn)InterlockedCompareExchangePointer((PVOID *)&s->leaf_next, NULL, NULL);
    at_tls_frame *f = tls_active_frame;
    const void *chosen = graph;
    int replace = 0;

    if (f && f->active && f->state == s) {
        at_receipt *r = &f->receipt;
        const at_lp_plan *p = &f->plan;
        uint32_t at = r->seen++;
        uint32_t group = at / 2u;
        uint32_t t = at % 2u;
        at_walker_slot *slot = &s->reg.slots[f->slot_index];

        int ok = !r->failure &&
                 f->owner_tid == GetCurrentThreadId() &&
                 slot->occupied &&
                 slot->epoch == f->slot_epoch &&
                 at < r->expected &&
                 group < p->groups &&
                 caller == s->engine_base + (t ? AT_RVA_LEAF_PREV_RET : AT_RVA_LEAF_CUR_RET) &&
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
            chosen = (const void *)slot->priv.graph[t];
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
        if (exit_csr != entry_csr) f->receipt.failure = 2u;
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
    DWORD entry_err = GetLastError();
    uint32_t entry_csr = _mm_getcsr();
    if (!s) return 0;

    if (world && packet && span_ok((uintptr_t)packet, 8u)) {
        uint32_t handle = 0;
        if (s->read_fn((uintptr_t)packet + 4u, &handle, sizeof(handle), s->read_user) == 0) {
            AcquireSRWLockExclusive(&s->lock);
            at_registry_retire_handle(&s->reg, world, handle);
            ReleaseSRWLockExclusive(&s->lock);
        }
    }

    at_render_remove_fn next = (at_render_remove_fn)InterlockedCompareExchangePointer((PVOID *)&s->remove_next, NULL, NULL);
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
    if (!s || !InterlockedCompareExchange(&s->ready, 0, 0)) return 0u;
    AcquireSRWLockExclusive(&s->lock);
    uint32_t admitted = at_registry_reconcile(&s->reg, s->read_fn, s->read_user, max_scan);
    ReleaseSRWLockExclusive(&s->lock);
    return admitted;
}

const at_tls_frame *at_writer_current_frame(void) {
    return tls_active_frame;
}
