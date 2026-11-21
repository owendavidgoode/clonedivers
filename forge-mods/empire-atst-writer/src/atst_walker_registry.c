/* Multi-walker (4 concurrent co-op EXO-45/49) registry & 1E7CD0 handle retirement. */
#include "atst_walker_registry.h"

#include <string.h>

static int valid_addr(uintptr_t a) {
    return a >= 0x10000ULL && a < 0x0000800000000000ULL;
}

static int valid_span(uintptr_t a, size_t n) {
    return valid_addr(a) && n <= 0x0000800000000000ULL - a;
}

static uint32_t rd_u32(const unsigned char *p) {
    uint32_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static uint64_t rd_u64(const unsigned char *p) {
    uint64_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static void evict_slot(at_walker_registry *reg, at_walker_slot *slot) {
    if (reg && slot->occupied && slot->unit_index < AT_UNIT_CAP_LIMIT) {
        reg->scanned_slots[slot->unit_index] = 0u;
    }
    uint32_t next_epoch = slot->epoch + 1u;
    memset(slot, 0, sizeof(*slot));
    slot->epoch = next_epoch ? next_epoch : 1u;
}

void at_registry_init(at_walker_registry *reg, uintptr_t engine_base) {
    if (!reg) return;
    memset(reg, 0, sizeof(*reg));
    reg->engine_base = engine_base;
    reg->rmanager_vtable_graph = engine_base ? (engine_base + 0x16880B0ULL) : 0ULL;
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        reg->slots[i].epoch = 1u;
    }
}

void at_registry_reset(at_walker_registry *reg) {
    if (!reg) return;
    uintptr_t eng = reg->engine_base;
    uintptr_t vt = reg->rmanager_vtable_graph;
    at_registry_init(reg, eng);
    reg->rmanager_vtable_graph = vt;
}

int at_read_unit_manager(
    const at_walker_registry *reg,
    at_safe_read_fn read_fn,
    void *read_user,
    at_unit_manager_view *out) {
    if (!reg || !read_fn || !out || !valid_addr(reg->engine_base)) return 0;
    memset(out, 0, sizeof(*out));
    uintptr_t mgr_addr = 0;
    if (read_fn(reg->engine_base + AT_RVA_UNIT_MANAGER, &mgr_addr, sizeof(mgr_addr), read_user) != 0) return 0;
    if (!valid_span(mgr_addr, 0xB0u)) return 0;

    unsigned char hdr[0xB0];
    if (read_fn(mgr_addr, hdr, sizeof(hdr), read_user) != 0) return 0;
    uintptr_t slots = (uintptr_t)rd_u64(hdr + 0x88u);
    uint32_t cap = rd_u32(hdr + 0x98u);
    uintptr_t gens = (uintptr_t)rd_u64(hdr + 0xA0u);
    if (!cap || cap > AT_UNIT_CAP_LIMIT) return 0;
    if (!valid_span(slots, (size_t)cap * 8u) || !valid_span(gens, (size_t)cap)) return 0;

    out->address = mgr_addr;
    out->slots = slots;
    out->generations = gens;
    out->capacity = cap;
    return 1;
}

int at_read_atst_unit(
    const at_walker_registry *reg,
    const at_unit_manager_view *mgr,
    uint32_t idx,
    at_safe_read_fn read_fn,
    void *read_user,
    at_unit_view *out) {
    if (!reg || !mgr || !read_fn || !out || idx >= mgr->capacity) return 0;
    memset(out, 0, sizeof(*out));

    uint8_t gen = 0;
    uintptr_t unit_ptr = 0;
    if (read_fn(mgr->generations + idx, &gen, 1u, read_user) != 0) return 0;
    if (read_fn(mgr->slots + (uintptr_t)idx * 8u, &unit_ptr, 8u, read_user) != 0) return 0;
    if (!valid_span(unit_ptr, 0xD8u)) return 0;

    unsigned char uhdr[0xD8];
    if (read_fn(unit_ptr, uhdr, sizeof(uhdr), read_user) != 0) return 0;
    uintptr_t vtable = (uintptr_t)rd_u64(uhdr + 0x00u);
    if (vtable != reg->engine_base + AT_RVA_UNIT_VTABLE) return 0;

    uintptr_t res_ptr = (uintptr_t)rd_u64(uhdr + 0x30u);
    uint64_t res_id = 0;
    if (!valid_span(res_ptr, 8u)) return 0;
    if (read_fn(res_ptr, &res_id, 8u, read_user) != 0) return 0;
    if (res_id != AT_UNIT_RESOURCE_ID) return 0;

    uint32_t graph_handle = rd_u32(uhdr + 0x64u);
    uintptr_t assoc = (uintptr_t)rd_u64(uhdr + 0x68u);
    uint32_t nodes = rd_u32(uhdr + 0x70u);
    uintptr_t world_arr = (uintptr_t)rd_u64(uhdr + 0x88u);
    uintptr_t topo = (uintptr_t)rd_u64(uhdr + 0x98u);
    uint32_t meshes = rd_u32(uhdr + 0xC8u);

    if (!graph_handle || graph_handle == 0xFFFFFFu || !valid_addr(assoc) || assoc < 0x10010ULL) return 0;
    if (nodes != AT_S2C_NODES || meshes != AT_MESH_DESC_COUNT) return 0;
    if (!valid_span(world_arr, (size_t)nodes * 48u) || !valid_span(topo, (size_t)nodes * 2u)) return 0;

    uint16_t topo_buf[AT_S2C_NODES];
    if (read_fn(topo, topo_buf, sizeof(topo_buf), read_user) != 0) return 0;
    if (memcmp(topo_buf, AT_S2C_PARENT, sizeof(topo_buf)) != 0) return 0;

    out->index = idx;
    out->generation = gen;
    out->unit = unit_ptr;
    out->vtable = vtable;
    out->resource_ptr = res_ptr;
    out->resource_id = res_id;
    out->graph_handle = graph_handle;
    out->association = assoc;
    out->nodes = nodes;
    out->meshes = meshes;
    out->world_array = world_arr;
    out->topology = topo;
    return 1;
}

static int valid_render_vec(const at_render_vec *v, size_t elem_size) {
    if (!v || !v->length || !v->capacity || v->length > v->capacity || v->capacity > AT_RENDER_VEC_LIMIT) {
        return 0;
    }
    return valid_span(v->data, (size_t)v->capacity * elem_size);
}

int at_read_render_header(
    uintptr_t manager,
    at_safe_read_fn read_fn,
    void *read_user,
    at_render_header *out) {
    if (!read_fn || !out || !valid_span(manager, 0x60u)) return 0;
    memset(out, 0, sizeof(*out));
    out->manager = manager;

    unsigned char raw[80];
    if (read_fn(manager + 0x10u, raw, sizeof(raw), read_user) != 0) return 0;

    out->queue = (uintptr_t)rd_u64(raw + 0x00u);
    out->handles = (uintptr_t)rd_u64(raw + 0x08u);
    out->capacity = rd_u32(raw + 0x2Cu);
    out->mask = rd_u32(raw + 0x30u);
    out->generation = rd_u32(raw + 0x34u);
    out->live = rd_u32(raw + 0x3Cu);

    if (out->handles != (uintptr_t)rd_u64(raw + 0x48u)) return 0;
    if (out->capacity < 2u || out->capacity > AT_RENDER_HANDLE_LIMIT) return 0;
    if (!valid_span(out->handles, (size_t)out->capacity * 4u) || !valid_span(out->queue, 0x3E0u)) return 0;

    uint32_t mask = out->capacity - 1u;
    mask |= mask >> 1;
    mask |= mask >> 2;
    mask |= mask >> 4;
    mask |= mask >> 8;
    mask |= mask >> 16;
    uint32_t gen = rd_u32(raw + 0x34u);
    (void)gen;
    if (out->mask != mask || out->live != mask + 1u) return 0;

    unsigned char qbuf[0x90];
    if (read_fn(out->queue, qbuf, sizeof(qbuf), read_user) != 0) return 0;
    uintptr_t allocator = (uintptr_t)rd_u64(qbuf + 0x80u);
    if (!valid_addr(allocator) || allocator < 0x10010ULL) return 0;
    out->world = allocator - 0x10u;
    if (!valid_span(out->world, 0x6B0u)) return 0;
    if ((uintptr_t)rd_u64(qbuf + 0x20u) != out->world + 0x550u) return 0;

    uintptr_t back = 0;
    if (read_fn(out->world + 0x150u, &back, 8u, read_user) != 0 || back != manager) return 0;

    unsigned char vbuf[16];
    if (read_fn(out->world + 0x168u, vbuf, 16u, read_user) != 0) return 0;
    out->objects = (at_render_vec){rd_u32(vbuf), rd_u32(vbuf + 4), (uintptr_t)rd_u64(vbuf + 8)};
    if (read_fn(out->world + 0x180u, vbuf, 16u, read_user) != 0) return 0;
    out->types = (at_render_vec){rd_u32(vbuf), rd_u32(vbuf + 4), (uintptr_t)rd_u64(vbuf + 8)};
    if (read_fn(out->world + 0x1B8u, vbuf, 16u, read_user) != 0) return 0;
    out->map = (at_render_vec){rd_u32(vbuf), rd_u32(vbuf + 4), (uintptr_t)rd_u64(vbuf + 8)};

    if (!valid_render_vec(&out->objects, 8u) ||
        !valid_render_vec(&out->types, 4u) ||
        !valid_render_vec(&out->map, 4u)) {
        return 0;
    }
    return 1;
}

int at_resolve_render_graph(
    const at_walker_registry *reg,
    const at_render_header *hdr,
    uint32_t handle,
    at_safe_read_fn read_fn,
    void *read_user,
    at_resolved_graph *out) {
    if (!reg || !hdr || !read_fn || !out) return 0;
    memset(out, 0, sizeof(*out));
    uint32_t slot_i = handle & hdr->mask;
    if (!handle || handle == 0xFFFFFFu || !slot_i || slot_i >= hdr->capacity || slot_i >= hdr->map.length) {
        return 0;
    }
    if (!(handle & hdr->live)) return 0;

    uint32_t slot_val = 0, obj_idx = 0, rtype = 0;
    uintptr_t obj = 0;
    if (read_fn(hdr->handles + (uintptr_t)slot_i * 4u, &slot_val, 4u, read_user) != 0 || slot_val != handle) return 0;
    if (read_fn(hdr->map.data + (uintptr_t)slot_i * 4u, &obj_idx, 4u, read_user) != 0) return 0;
    if (obj_idx >= hdr->objects.length || obj_idx >= hdr->types.length) return 0;
    if (read_fn(hdr->types.data + (uintptr_t)obj_idx * 4u, &rtype, 4u, read_user) != 0 || rtype != 2u) return 0;
    if (read_fn(hdr->objects.data + (uintptr_t)obj_idx * 8u, &obj, 8u, read_user) != 0) return 0;
    if (!valid_span(obj, 0x78u)) return 0;

    unsigned char ghead[24];
    if (read_fn(obj, ghead, sizeof(ghead), read_user) != 0) return 0;
    uintptr_t vtable = (uintptr_t)rd_u64(ghead + 0x00u);
    uint32_t hdr_type = rd_u32(ghead + 0x08u);
    uint32_t nodes = rd_u32(ghead + 0x10u);
    if (hdr_type != 2u || vtable != reg->rmanager_vtable_graph || nodes != AT_S2C_NODES) return 0;

    out->handle = handle;
    out->index = obj_idx;
    out->type = rtype;
    out->nodes = nodes;
    out->object = obj;
    out->vtable = vtable;
    return 1;
}

static int find_slot_by_unit(const at_walker_registry *reg, uint32_t unit_idx, uint8_t gen, uintptr_t unit_ptr) {
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        const at_walker_slot *s = &reg->slots[i];
        if (s->occupied && s->unit_index == unit_idx && s->unit_gen == gen && s->unit == unit_ptr) {
            return (int)i;
        }
    }
    return -1;
}

static int find_slot_by_render(const at_walker_registry *reg, uintptr_t world, uint32_t graph_handle) {
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        const at_walker_slot *s = &reg->slots[i];
        if (s->occupied && s->world == world && s->graph_handle == graph_handle) {
            return (int)i;
        }
    }
    return -1;
}

static int admit_unit_into_lowest_free(
    at_walker_registry *reg,
    const at_unit_view *uv,
    const at_render_header *rhdr,
    const at_resolved_graph *rgraph) {
    int free_idx = -1;
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        if (!reg->slots[i].occupied) {
            free_idx = (int)i;
            break;
        }
    }
    if (free_idx < 0) {
        reg->capacity_refusals++;
        return -1;
    }

    at_walker_slot *slot = &reg->slots[free_idx];
    uint32_t next_epoch = slot->epoch + 1u;
    memset(slot, 0, sizeof(*slot));
    slot->occupied = 1u;
    slot->epoch = next_epoch ? next_epoch : 1u;
    slot->unit_index = uv->index;
    slot->unit_gen = uv->generation;
    slot->unit = uv->unit;
    slot->association = uv->association;
    slot->world = rhdr->world;
    slot->graph_handle = uv->graph_handle;
    slot->normal_graph = rgraph->object;
    reg->admissions++;
    return free_idx;
}

uint32_t at_registry_reconcile(
    at_walker_registry *reg,
    at_safe_read_fn read_fn,
    void *read_user,
    uint32_t max_scan) {
    at_unit_manager_view mgr;
    if (!at_read_unit_manager(reg, read_fn, read_user, &mgr)) return 0u;

    /* Step 1: Verify existing slots are still live & bound to exact unit_gen, world, graphHandle. */
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        at_walker_slot *s = &reg->slots[i];
        if (!s->occupied) continue;
        at_unit_view uv;
        at_render_header rhdr;
        at_resolved_graph rgraph;
        if (!at_read_atst_unit(reg, &mgr, s->unit_index, read_fn, read_user, &uv) ||
            uv.generation != s->unit_gen ||
            uv.unit != s->unit ||
            uv.association != s->association ||
            uv.graph_handle != s->graph_handle ||
            !at_read_render_header(uv.association - 0x10u, read_fn, read_user, &rhdr) ||
            rhdr.world != s->world ||
            !at_resolve_render_graph(reg, &rhdr, uv.graph_handle, read_fn, read_user, &rgraph) ||
            rgraph.object != s->normal_graph) {
            evict_slot(reg, s);
            reg->scavenged++;
        }
    }

    /* Step 2: Differential rolling-cursor bulk-chunk scan of UnitManager to admit new 79E4B3D2DA5E45E3 walkers. */
    uint32_t limit = (max_scan && max_scan < mgr.capacity) ? max_scan : mgr.capacity;
    uint32_t start = (max_scan && max_scan < mgr.capacity) ? (reg->scan_cursor % mgr.capacity) : 0u;
    uint32_t admitted = 0;
    uint32_t scanned = 0;
    uintptr_t slot_chunk[512];
    uint8_t gen_chunk[512];
    while (scanned < limit) {
        uint32_t chunk_start = (start + scanned) % mgr.capacity;
        uint32_t run = mgr.capacity - chunk_start;
        if (run > limit - scanned) run = limit - scanned;
        if (run > 512u) run = 512u;
        int bulk_ok = (read_fn(mgr.slots + (uintptr_t)chunk_start * 8u, slot_chunk, (size_t)run * 8u, read_user) == 0 &&
                       read_fn(mgr.generations + chunk_start, gen_chunk, (size_t)run, read_user) == 0);
        for (uint32_t j = 0; j < run; j++) {
            uint32_t idx = chunk_start + j;
            if (bulk_ok) {
                uintptr_t unit_ptr = slot_chunk[j];
                uint8_t unit_gen = gen_chunk[j];
                if (!valid_span(unit_ptr, 0xD8u)) {
                    reg->scanned_slots[idx] = 0u;
                    reg->scanned_gens[idx] = unit_gen;
                    continue;
                }
                if (reg->scanned_slots[idx] == unit_ptr && reg->scanned_gens[idx] == unit_gen) {
                    continue;
                }
                unsigned char probe[0x38];
                if (read_fn(unit_ptr, probe, sizeof(probe), read_user) != 0) continue;
                if ((uintptr_t)rd_u64(probe + 0x00u) != reg->engine_base + AT_RVA_UNIT_VTABLE) {
                    reg->scanned_slots[idx] = unit_ptr;
                    reg->scanned_gens[idx] = unit_gen;
                    continue;
                }
                uintptr_t res_ptr = (uintptr_t)rd_u64(probe + 0x30u);
                uint64_t res_id = 0;
                if (!valid_span(res_ptr, 8u) || read_fn(res_ptr, &res_id, 8u, read_user) != 0) continue;
                if (res_id != AT_UNIT_RESOURCE_ID) {
                    reg->scanned_slots[idx] = unit_ptr;
                    reg->scanned_gens[idx] = unit_gen;
                    continue;
                }
            }
            at_unit_view uv;
            if (!at_read_atst_unit(reg, &mgr, idx, read_fn, read_user, &uv)) continue;
            if (find_slot_by_unit(reg, uv.index, uv.generation, uv.unit) >= 0) {
                if (bulk_ok) {
                    reg->scanned_slots[idx] = uv.unit;
                    reg->scanned_gens[idx] = uv.generation;
                }
                continue;
            }

            at_render_header rhdr;
            at_resolved_graph rgraph;
            if (!at_read_render_header(uv.association - 0x10u, read_fn, read_user, &rhdr)) continue;
            if (!at_resolve_render_graph(reg, &rhdr, uv.graph_handle, read_fn, read_user, &rgraph)) continue;

            /* Ensure no stale slot still claims this exact (world, graph_handle) before admitting. */
            int stale = find_slot_by_render(reg, rhdr.world, uv.graph_handle);
            if (stale >= 0) {
                evict_slot(reg, &reg->slots[stale]);
                reg->scavenged++;
            }
            if (admit_unit_into_lowest_free(reg, &uv, &rhdr, &rgraph) >= 0) {
                if (bulk_ok) {
                    reg->scanned_slots[idx] = uv.unit;
                    reg->scanned_gens[idx] = uv.generation;
                }
                admitted++;
            }
        }
        scanned += run;
    }
    int sweep_wrapped = 0;
    if (max_scan && max_scan < mgr.capacity) {
        if (start + limit >= mgr.capacity) sweep_wrapped = 1;
        reg->scan_cursor = (start + limit) % mgr.capacity;
    } else {
        sweep_wrapped = 1;
        reg->scan_cursor = 0u;
    }
    if (sweep_wrapped) {
        reg->sweep_count++;
        if ((reg->sweep_count & 15u) == 0u) {
            memset(reg->scanned_slots, 0, sizeof(reg->scanned_slots));
        }
    }
    return admitted;
}

uint32_t at_registry_retire_handle(at_walker_registry *reg, uintptr_t world, uint32_t handle) {
    if (!reg || !world || !handle || handle == 0xFFFFFFu) return 0u;
    uint32_t retired = 0;
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        at_walker_slot *s = &reg->slots[i];
        if (s->occupied && s->world == world && s->graph_handle == handle) {
            evict_slot(reg, s);
            reg->retirements++;
            retired++;
        }
    }
    return retired;
}

int at_registry_resolve_render(
    at_walker_registry *reg,
    uintptr_t pool,
    uintptr_t mesh,
    uintptr_t opt_graph,
    at_safe_read_fn read_fn,
    void *read_user,
    uint32_t *out_epoch,
    at_resolved_graph *out_graph) {
    if (!reg || !read_fn || !valid_span(mesh, 0xD0u) || pool < 0x318ULL + 0x10000ULL) return -1;

    unsigned char mref[16];
    if (read_fn(mesh + 0x30u, mref, sizeof(mref), read_user) != 0) return -1;
    uint32_t m_node = rd_u32(mref + 0x00u);
    uint32_t m_handle = rd_u32(mref + 0x04u);
    uintptr_t m_cache = (uintptr_t)rd_u64(mref + 0x08u);
    if (!m_handle || m_handle == 0xFFFFFFu || !valid_addr(m_cache)) return -1;
    if (opt_graph && opt_graph != m_cache) return -1;

    int node_ok = 0;
    for (unsigned i = 0; i < AT_MESH_DESC_COUNT; i++) {
        if (m_node == AT_DESC_NODES[i]) {
            node_ok = 1;
            break;
        }
    }
    if (!node_ok) return -1;

    uintptr_t world = pool - 0x318u;
    uintptr_t rmanager = 0;
    if (!valid_span(world, 0x6B0u) || read_fn(world + 0x150u, &rmanager, 8u, read_user) != 0) return -1;

    at_render_header rhdr;
    at_resolved_graph rgraph;
    if (!at_read_render_header(rmanager, read_fn, read_user, &rhdr) || rhdr.world != world) return -1;
    if (!at_resolve_render_graph(reg, &rhdr, m_handle, read_fn, read_user, &rgraph)) return -1;
    if (rgraph.object != m_cache || rgraph.nodes != AT_S2C_NODES) return -1;

    int slot_idx = find_slot_by_render(reg, world, m_handle);
    if (slot_idx < 0) {
        /* Never run full UnitManager scans inside the hot 512B80 render hook; WriterTick owns admission. */
        reg->passthroughs++;
        return -1;
    }

    /* Re-validate live UnitManager slot generation, vtable, and association on every render entry! */
    at_walker_slot *s = &reg->slots[slot_idx];
    at_unit_manager_view mgr;
    at_unit_view uv;
    if (!at_read_unit_manager(reg, read_fn, read_user, &mgr) ||
        !at_read_atst_unit(reg, &mgr, s->unit_index, read_fn, read_user, &uv) ||
        uv.generation != s->unit_gen ||
        uv.unit != s->unit ||
        uv.association != rhdr.manager + 0x10u ||
        uv.graph_handle != m_handle ||
        s->normal_graph != rgraph.object) {
        evict_slot(reg, s);
        reg->scavenged++;
        reg->passthroughs++;
        return -1;
    }

    if (out_epoch) *out_epoch = s->epoch;
    if (out_graph) *out_graph = rgraph;
    return slot_idx;
}
