/* AT-ST Pilot Cab Seat Lift & Lower-Body/Cape Render Suppression implementation. */
#include "atst_pilot_seat.h"
#include "atst_writer_core.h"

#include <float.h>
#include <immintrin.h>
#include <math.h>
#include <string.h>

#define AT_UNIT_REF_OFFSET       0x08u
#define AT_UNIT_RES_PTR_OFFSET   0x30u
#define AT_UNIT_GRAPH_HANDLE_OFF 0x64u
#define AT_UNIT_ASSOC_OFFSET     0x68u
#define AT_UNIT_NODES_OFFSET     0x70u
#define AT_UNIT_TOPO_OFFSET      0x98u
#define AT_UNIT_MESH_CNT_OFFSET  0xC8u
#define AT_UNIT_MESH_ARR_OFFSET  0xD0u
#define AT_UNIT_PARENT_OFFSET    0x1D0u
#define AT_UNIT_CHILD_FIRST      0x1D8u
#define AT_UNIT_CHILD_NEXT       0x1E0u

#define AT_GRAPH_NODE_COUNT_OFF  0x10u
#define AT_GRAPH_LINK_PARENT_OFF 0x18u
#define AT_GRAPH_LOCAL_POSES_OFF 0x20u
#define AT_GRAPH_WORLD_POSES_OFF 0x28u

/* Sorted table of all 32 Compact LEGO lower-body garments (hips, left_leg, right_leg; 3 baked + 29 dynamic). */
static const uint64_t AT_LEGO_LOWER_GARMENTS[32] = {
    0x001A106B352493D5ULL, /* commander_cody_left_leg */
    0x0A09425DA11544D7ULL, /* commander_cody_right_leg */
    0x0ED7F5717E4DF15DULL, /* phase2_501st_hips_bt1 */
    0x174D1DCEB75D16D4ULL, /* cm09_af02_bikini_stormtrooper_hips_brawny */
    0x1E25DAEB99B1A917ULL, /* empire_baked_3 (leg) */
    0x1FC7B7C36F0B1674ULL, /* commander_cody_hips_bt1 */
    0x24105347EBD202F4ULL, /* phase2_212th_hips_bt1 */
    0x29A22CECE5427D2DULL, /* coruscant_guard_hips_bt1 */
    0x322A9A41C3477441ULL, /* coruscant_guard_right_leg */
    0x33F67BDEDFF4B04EULL, /* phase2_212th_hips_bt0 */
    0x3EBFC76341E98F39ULL, /* phase2_212th_left_leg */
    0x4185F04A2DD2FA90ULL, /* phase2_501st_hips_bt0 */
    0x453B56313057297AULL, /* phase1_shiny_hips_bt0 */
    0x4A1DE6A0D1FFC6A7ULL, /* coruscant_guard_left_leg */
    0x4AF7E6656DA96318ULL, /* coruscant_guard_hips_bt0 */
    0x4B330E182EFA97D7ULL, /* cm09_af02_bikini_stormtrooper_left_leg */
    0x4D2F00B6C4929F00ULL, /* empire_baked_5 (hips) */
    0x51E5214EAE73D316ULL, /* phase1_shiny_left_leg */
    0x53D3B2302BF7BC85ULL, /* captain_rex_right_leg */
    0x572650ED5C6665CBULL, /* captain_rex_hips_bt1 */
    0x5A6301820AAAF66BULL, /* phase2_501st_left_leg */
    0x69AD165D13A46FF1ULL, /* cm09_af02_bikini_stormtrooper_hips_lean */
    0x7305FDF48136C05CULL, /* empire_baked_1 (leg) */
    0x75548295338B975AULL, /* phase1_shiny_right_leg */
    0x7604826C8C96A881ULL, /* commander_cody_hips_bt0 */
    0x9588C31DE55D589CULL, /* cm09_af02_bikini_stormtrooper_right_leg */
    0xB26FA103A77BE3F9ULL, /* captain_rex_left_leg */
    0xB850E0612E74845EULL, /* captain_rex_hips_bt0 */
    0xD5024911ADDDFEAAULL, /* phase2_212th_right_leg */
    0xE25339F051883B91ULL, /* phase2_501st_right_leg */
    0xF19F70AF86F4CDB0ULL, /* dp40_tr9_dp11_o44_stormtrooper_hips_lean */
    0xFD1F3953E73C6DFBULL, /* phase1_shiny_hips_bt1 */
};

/* Sorted table of the 47 Compact LEGO upper-body garments (helmet, torso, left_arm, right_arm) — NEVER hidden. */
static const uint64_t AT_LEGO_UPPER_GARMENTS[47] = {
    0x003CC8A527711DA5ULL, 0x0208D1FFFB6C1675ULL, 0x0377EA04BCBD7319ULL, 0x0506E669825C1648ULL,
    0x0593D8E403C51BC2ULL, 0x088772986FCA2B13ULL, 0x09AD5784B873E58FULL, 0x2B507DE741D174E9ULL,
    0x2B7598966411556FULL, 0x2BB094EDBC756963ULL, 0x356EB2A710E33523ULL, 0x37D0A8598D125564ULL,
    0x4249A344A5268033ULL, 0x43840F40D7999C80ULL, 0x48E63F84996BE394ULL, 0x512399E005614901ULL,
    0x590D30736E66432CULL, 0x6B826D06AB7AF07CULL, 0x6FF15ACC991E8A9AULL, 0x7514090FA90DA6F0ULL,
    0x95EC84B1EFA977B3ULL, 0x9E291E101E16DC4FULL, 0xA111DE5354035DD7ULL, 0xA4443F91278A5C5FULL,
    0xA88278A6EA6B3E94ULL, 0xB63D3C0BD23EE274ULL, 0xBAFFF2673EB5CBCAULL, 0xBB11C8041C14C747ULL,
    0xBFDC0F01475D16C8ULL, 0xC11416CC62AA026CULL, 0xC1141B792C1B621AULL, 0xC5BCFEDD0D88A16BULL,
    0xC6369F219C208C0DULL, 0xC6F9903FD728206BULL, 0xC86087DDDF60D9DEULL, 0xCBB536EEDB3005EDULL,
    0xD184172E9096A6C2ULL, 0xD3D41E93EC4C7842ULL, 0xDAB5777BFBBD1A83ULL, 0xDD25333BDF5F0143ULL,
    0xDDCAF179A5CF81B4ULL, 0xDE118AD3932E0A23ULL, 0xE0EEAE016DA8C9EDULL, 0xE79251F8F34D351AULL,
    0xF2EEF185355BAE5EULL, 0xF4AF05AD6D4D73A4ULL, 0xF6F783E1679BA0FDULL,
};

static int bsearch_u64(const uint64_t *arr, size_t n, uint64_t key) {
    if (!key) return 0;
    size_t lo = 0, hi = n;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2u;
        uint64_t cur = arr[mid];
        if (cur == key) return 1;
        if (cur < key) lo = mid + 1u;
        else hi = mid;
    }
    return 0;
}

int at_is_lego_lower_garment(uint64_t resource_id) {
    return bsearch_u64(AT_LEGO_LOWER_GARMENTS, sizeof(AT_LEGO_LOWER_GARMENTS) / sizeof(AT_LEGO_LOWER_GARMENTS[0]), resource_id);
}

int at_is_any_lego_garment(uint64_t resource_id) {
    return at_is_lego_lower_garment(resource_id) ||
           bsearch_u64(AT_LEGO_UPPER_GARMENTS, sizeof(AT_LEGO_UPPER_GARMENTS) / sizeof(AT_LEGO_UPPER_GARMENTS[0]), resource_id);
}

static int p_span_ok(uintptr_t a, size_t n) {
    return a >= 0x10000ULL && a < 0x0000800000000000ULL && n <= 0x0000800000000000ULL - a;
}

static uint32_t p_u32(const unsigned char *p) {
    uint32_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static uint64_t p_u64(const unsigned char *p) {
    uint64_t v = 0;
    memcpy(&v, p, sizeof(v));
    return v;
}

static int pose_matches_12(const float a[16], const float b[16]) {
    for (int i = 0; i < 12; i++) {
        if (!isfinite(a[i]) || !isfinite(b[i]) || fabsf(a[i] - b[i]) > 1e-4f) return 0;
    }
    return 1;
}

static int is_helldiver_leg_or_cape_bone(uint16_t hb) {
    /* 10..20 = r_thigh..r_toe & l_thigh..l_toe; 34..41 = cape_01..cape_08 */
    return (hb >= 10u && hb <= 20u) || (hb >= 34u && hb <= 41u);
}

static int is_helldiver_upper_body_bone(uint16_t hb) {
    /* 21..33 = spine1..chest; 42..94 = clavicles/arms/hands/neck/head; 103..104, 106 = arm/head/spine helpers */
    return (hb >= 21u && hb <= 33u) || (hb >= 42u && hb <= 94u) || hb == 103u || hb == 104u || hb == 106u;
}

static void set_mask_bit(uint64_t *lo, uint64_t *hi, uint32_t bit) {
    if (bit < 64u) *lo |= (1ULL << bit);
    else if (bit < 128u) *hi |= (1ULL << (bit - 64u));
}

static int get_mask_bit(uint64_t lo, uint64_t hi, uint32_t bit) {
    if (bit < 64u) return (int)((lo >> bit) & 1ULL);
    if (bit < 128u) return (int)((hi >> (bit - 64u)) & 1ULL);
    return 0;
}

int at_pilot_compute_cab_local_pose(
    const at_rig *rig,
    const float walker_world_64[AT_S2C_NODES][16],
    uint32_t attach_node,
    const float base_local_pose[16],
    float out_local_pose[16]) {
    if (!rig || !walker_world_64 || !base_local_pose || !out_local_pose || attach_node >= AT_S2C_NODES) {
        return 0;
    }
    for (int i = 0; i < 16; i++) {
        if (!isfinite(base_local_pose[i])) return 0;
    }
    /* Guard against corrupt/extreme base local offsets while preserving normal mount/seat local rotations. */
    float bx = base_local_pose[9], by = base_local_pose[10], bz = base_local_pose[11];
    if (fabsf(bx) > 3.5f || fabsf(by) > 3.5f || fabsf(bz) > 3.5f) return 0;

    /* Convert Stingray SceneGraph world_poses (row-vector 4x4: right=[0..2], fwd=[4..6], up=[8..10], pos=[12..14])
     * into column-vector row-major at_render48 so at_evaluate_s2c_pair computes the exact 2.28x retargeted cab! */
    at_render48 cur_s2c[AT_S2C_NODES];
    for (unsigned k = 0; k < AT_S2C_NODES; k++) {
        const float *m = walker_world_64[k];
        for (unsigned j = 0; j < 16; j++) {
            if (!isfinite(m[j])) return 0;
        }
        cur_s2c[k].v[0]  = m[0]; cur_s2c[k].v[1]  = m[4]; cur_s2c[k].v[2]  = m[8];  cur_s2c[k].v[3]  = m[12];
        cur_s2c[k].v[4]  = m[1]; cur_s2c[k].v[5]  = m[5]; cur_s2c[k].v[6]  = m[9];  cur_s2c[k].v[7]  = m[13];
        cur_s2c[k].v[8]  = m[2]; cur_s2c[k].v[9]  = m[6]; cur_s2c[k].v[10] = m[10]; cur_s2c[k].v[11] = m[14];
    }

    uint32_t saved_csr = _mm_getcsr();
    _mm_setcsr(saved_csr & ~0x6000u); /* FE_TONEAREST for at_evaluate_s2c_pair */
    at_private_graph priv;
    at_status st = at_evaluate_s2c_pair(rig, cur_s2c, cur_s2c, AT_S2C_NODES, &priv);
    _mm_setcsr(saved_csr);
    if (st != AT_OK) return 0;

    /* Desired world pilot root from retargeted s2c=32 cockpit + cab seat offset: */
    const at_render48 *cab = &priv.graph[0][AT_S2C_COCKPIT_NODE];
    double target_wx = (double)cab->v[0] * (double)AT_PILOT_CAB_OFFSET_X +
                       (double)cab->v[1] * (double)AT_PILOT_CAB_OFFSET_Y +
                       (double)cab->v[2] * (double)AT_PILOT_CAB_OFFSET_Z + (double)cab->v[3];
    double target_wy = (double)cab->v[4] * (double)AT_PILOT_CAB_OFFSET_X +
                       (double)cab->v[5] * (double)AT_PILOT_CAB_OFFSET_Y +
                       (double)cab->v[6] * (double)AT_PILOT_CAB_OFFSET_Z + (double)cab->v[7];
    double target_wz = (double)cab->v[8] * (double)AT_PILOT_CAB_OFFSET_X +
                       (double)cab->v[9] * (double)AT_PILOT_CAB_OFFSET_Y +
                       (double)cab->v[10] * (double)AT_PILOT_CAB_OFFSET_Z + (double)cab->v[11];

    /* Invert walker_world_64[attach_node] linear 3x3 to map world delta into attach_node's local frame. */
    at_mat4 parent_m, inv_parent;
    if (at_unpack(&cur_s2c[attach_node], &parent_m) != AT_OK ||
        at_inv_affine(&parent_m, &inv_parent) != AT_OK) {
        return 0;
    }

    double dx = target_wx - parent_m.v[3];
    double dy = target_wy - parent_m.v[7];
    double dz = target_wz - parent_m.v[11];

    double lx = inv_parent.v[0] * dx + inv_parent.v[1] * dy + inv_parent.v[2] * dz + (double)bx;
    double ly = inv_parent.v[4] * dx + inv_parent.v[5] * dy + inv_parent.v[6] * dz + (double)by;
    double lz = inv_parent.v[8] * dx + inv_parent.v[9] * dy + inv_parent.v[10] * dz + (double)bz;
    if (!isfinite(lx) || !isfinite(ly) || !isfinite(lz) ||
        fabs(lx) > 32.0 || fabs(ly) > 32.0 || fabs(lz) > 32.0) {
        return 0;
    }

    /* Orthonormalize base 3x3 rotation (Gram-Schmidt) and enforce unit scale {1,1,1} (det == +1.0)
     * so cm_solve_parent in lego-compact-leaf-writer ALWAYS sees a pure proper rotation! */
    double rx = (double)base_local_pose[0], ry = (double)base_local_pose[1], rz = (double)base_local_pose[2];
    double fx = (double)base_local_pose[3], fy = (double)base_local_pose[4], fz = (double)base_local_pose[5];
    double ux = (double)base_local_pose[6], uy = (double)base_local_pose[7], uz = (double)base_local_pose[8];

    double rlen = sqrt(rx * rx + ry * ry + rz * rz);
    if (!isfinite(rlen) || rlen < 0.5 || rlen > 2.0) return 0;
    rx /= rlen; ry /= rlen; rz /= rlen;

    double rf = rx * fx + ry * fy + rz * fz;
    fx -= rf * rx; fy -= rf * ry; fz -= rf * rz;
    double flen = sqrt(fx * fx + fy * fy + fz * fz);
    if (!isfinite(flen) || flen < 0.5 || flen > 2.0) return 0;
    fx /= flen; fy /= flen; fz /= flen;

    /* Right-handed cross product up = right x forward (guarantees det == +1.000000). */
    double cx = ry * fz - rz * fy;
    double cy = rz * fx - rx * fz;
    double cz = rx * fy - ry * fx;
    if (cx * ux + cy * uy + cz * uz < 0.0) {
        /* Refuse improper reflection rather than flipping pilot handedness. */
        return 0;
    }

    out_local_pose[0]  = (float)rx;
    out_local_pose[1]  = (float)ry;
    out_local_pose[2]  = (float)rz;
    out_local_pose[3]  = (float)fx;
    out_local_pose[4]  = (float)fy;
    out_local_pose[5]  = (float)fz;
    out_local_pose[6]  = (float)cx;
    out_local_pose[7]  = (float)cy;
    out_local_pose[8]  = (float)cz;
    out_local_pose[9]  = (float)lx;
    out_local_pose[10] = (float)ly;
    out_local_pose[11] = (float)lz;
    out_local_pose[12] = 1.0f;
    out_local_pose[13] = 1.0f;
    out_local_pose[14] = 1.0f;
    out_local_pose[15] = base_local_pose[15];
    return 1;
}

uint32_t at_pilot_collapse_leaf_output(
    void *dst,
    const void *palette,
    const void *map,
    uint32_t count,
    int hide_all,
    uint64_t mask_lo,
    uint64_t mask_hi,
    at_safe_read_fn read_fn,
    void *read_user) {
    if (!dst || !palette || !map || count == 0u || count > 128u || !read_fn) return 0u;
    if (!p_span_ok((uintptr_t)dst, (size_t)count * sizeof(at_render48)) ||
        !p_span_ok((uintptr_t)palette, (size_t)count * sizeof(uint32_t))) {
        return 0u;
    }

    uint32_t pal_buf[128];
    if (read_fn((uintptr_t)palette, pal_buf, (size_t)count * sizeof(uint32_t), read_user) != 0) {
        return 0u;
    }

    uint32_t node_buf[128];
    int hide_flags[128];
    int ref_idx = -1;
    uint32_t to_collapse = 0u;

    for (uint32_t k = 0; k < count; k++) {
        uint32_t p_idx = pal_buf[k];
        if (p_idx >= 128u) return 0u;
        uint32_t n_idx = 0u;
        if (read_fn((uintptr_t)map + (uintptr_t)p_idx * sizeof(uint32_t), &n_idx, sizeof(uint32_t), read_user) != 0) {
            return 0u;
        }
        node_buf[k] = n_idx;
        int h = hide_all ? 1 : get_mask_bit(mask_lo, mask_hi, n_idx);
        hide_flags[k] = h;
        if (h) {
            to_collapse++;
        } else if (ref_idx < 0) {
            ref_idx = (int)k;
        }
    }
    if (to_collapse == 0u) return 0u;
    if (ref_idx < 0) ref_idx = 0;

    at_render48 *out_mats = (at_render48 *)dst;
    const at_render48 *mref = &out_mats[ref_idx];
    for (unsigned i = 0; i < 12u; i++) {
        if (!isfinite(mref->v[i])) return 0u;
    }

    /* Evaluate cab waist collapse anchor in dst's exact (*factor)-transformed render space. */
    float px = mref->v[2]  * AT_PILOT_WAIST_MODEL_Z + mref->v[3];
    float py = mref->v[6]  * AT_PILOT_WAIST_MODEL_Z + mref->v[7];
    float pz = mref->v[10] * AT_PILOT_WAIST_MODEL_Z + mref->v[11];
    if (!isfinite(px) || !isfinite(py) || !isfinite(pz)) return 0u;

    for (uint32_t k = 0; k < count; k++) {
        if (!hide_flags[k]) continue;
        out_mats[k].v[0]  = 0.0f; out_mats[k].v[1]  = 0.0f; out_mats[k].v[2]  = 0.0f; out_mats[k].v[3]  = px;
        out_mats[k].v[4]  = 0.0f; out_mats[k].v[5]  = 0.0f; out_mats[k].v[6]  = 0.0f; out_mats[k].v[7]  = py;
        out_mats[k].v[8]  = 0.0f; out_mats[k].v[9]  = 0.0f; out_mats[k].v[10] = 0.0f; out_mats[k].v[11] = pz;
    }
    (void)node_buf;
    return to_collapse;
}

static uintptr_t unit_scene_graph(at_writer_state *s, uintptr_t unit) {
    if (!s || !p_span_ok(unit, 0xE0u)) return 0u;
    uintptr_t vtbl = 0u;
    if (s->read_fn(unit, &vtbl, sizeof(vtbl), s->read_user) != 0 ||
        vtbl != s->engine_base + AT_RVA_UNIT_VTABLE) {
        return 0u;
    }
    if (s->pilot.unit_graph_override) {
        return s->pilot.unit_graph_override(unit);
    }
    uintptr_t fn_addr = 0u;
    if (!p_span_ok(vtbl + AT_UNIT_VTABLE_GRAPH, sizeof(fn_addr)) ||
        s->read_fn(vtbl + AT_UNIT_VTABLE_GRAPH, &fn_addr, sizeof(fn_addr), s->read_user) != 0 ||
        !p_span_ok(fn_addr, 16u)) {
        return 0u;
    }
    return ((at_unit_graph_fn)fn_addr)(unit);
}

static void collect_unit_hide_meshes(
    at_writer_state *s,
    uintptr_t unit,
    int is_avatar_self,
    at_pilot_hide_entry *out_entries,
    uint32_t *inout_count) {
    if (!s || !out_entries || !inout_count || *inout_count >= AT_MAX_PILOT_HIDE_MESHES) return;
    if (!p_span_ok(unit, 0xE0u)) return;

    unsigned char uhdr[0xD8];
    if (s->read_fn(unit, uhdr, sizeof(uhdr), s->read_user) != 0) return;
    if ((uintptr_t)p_u64(uhdr + 0x00u) != s->engine_base + AT_RVA_UNIT_VTABLE) return;

    uintptr_t res_ptr = (uintptr_t)p_u64(uhdr + AT_UNIT_RES_PTR_OFFSET);
    uint64_t res_id = 0ULL;
    if (!p_span_ok(res_ptr, sizeof(res_id)) || s->read_fn(res_ptr, &res_id, sizeof(res_id), s->read_user) != 0) {
        return;
    }

    uint32_t nodes = p_u32(uhdr + AT_UNIT_NODES_OFFSET);
    uint32_t mesh_cnt = p_u32(uhdr + AT_UNIT_MESH_CNT_OFFSET);
    uintptr_t mesh_arr = (uintptr_t)p_u64(uhdr + AT_UNIT_MESH_ARR_OFFSET);
    uintptr_t assoc = (uintptr_t)p_u64(uhdr + AT_UNIT_ASSOC_OFFSET);
    uint32_t ghandle = p_u32(uhdr + AT_UNIT_GRAPH_HANDLE_OFF);
    if (!nodes || nodes > 128u || !mesh_cnt || mesh_cnt > AT_MAX_UNIT_MESH_ENTRIES ||
        !p_span_ok(mesh_arr, (size_t)mesh_cnt * 16u) || !ghandle || ghandle == 0xFFFFFFu || assoc < 0x10010ULL) {
        return;
    }

    int hide_all = 0;
    uint64_t mask_lo = 0ULL, mask_hi = 0ULL;

    if (is_avatar_self) {
        if (res_id != AT_HELLDIVER_AVATAR_ID || nodes != AT_HELLDIVER_NODES) return;
        for (uint16_t hb = 10u; hb <= 20u; hb++) set_mask_bit(&mask_lo, &mask_hi, hb);
        for (uint16_t hb = 34u; hb <= 41u; hb++) set_mask_bit(&mask_lo, &mask_hi, hb);
    } else if (at_is_lego_lower_garment(res_id)) {
        hide_all = 1;
        mask_lo = ~0ULL;
        mask_hi = ~0ULL;
    } else if (at_is_any_lego_garment(res_id)) {
        /* Compact LEGO helmet / torso / arms stay 100% visible inside the cab. */
        return;
    } else {
        /* Realistic / vanilla child unit (armor, cape, belt/hips): inspect 4-byte topology links to avatar_unit. */
        uintptr_t topo_ptr = (uintptr_t)p_u64(uhdr + AT_UNIT_TOPO_OFFSET);
        struct { uint16_t type; uint16_t parent; } topo[128];
        if (!p_span_ok(topo_ptr, (size_t)nodes * 4u) ||
            s->read_fn(topo_ptr, topo, (size_t)nodes * 4u, s->read_user) != 0) {
            return;
        }
        int has_upper = 0;
        int16_t ext_anchor[128];
        for (uint32_t n = 0; n < nodes; n++) {
            ext_anchor[n] = -1;
            uint32_t cur = n;
            for (unsigned hop = 0; hop < 16u; hop++) {
                uint16_t tp = topo[cur].type;
                uint16_t pr = topo[cur].parent;
                if (tp == 2u) {
                    if (pr < AT_HELLDIVER_NODES) {
                        ext_anchor[n] = (int16_t)pr;
                        if (is_helldiver_upper_body_bone(pr)) has_upper = 1;
                    }
                    break;
                } else if (tp == 1u && pr < cur) {
                    cur = pr;
                } else {
                    break;
                }
            }
        }
        for (uint32_t n = 0; n < nodes; n++) {
            if (ext_anchor[n] < 0) continue;
            uint16_t hb = (uint16_t)ext_anchor[n];
            if (is_helldiver_leg_or_cape_bone(hb) || (hb == 9u && !has_upper)) {
                set_mask_bit(&mask_lo, &mask_hi, n);
            }
        }
    }

    if (!hide_all && mask_lo == 0ULL && mask_hi == 0ULL) return;

    at_render_header rhdr;
    if (!at_read_render_header(assoc - 0x10u, s->read_fn, s->read_user, &rhdr)) return;

    unsigned char raw_entries[AT_MAX_UNIT_MESH_ENTRIES * 16u];
    if (s->read_fn(mesh_arr, raw_entries, (size_t)mesh_cnt * 16u, s->read_user) != 0) return;

    for (uint32_t i = 0; i < mesh_cnt && *inout_count < AT_MAX_PILOT_HIDE_MESHES; i++) {
        uintptr_t desc = (uintptr_t)p_u64(raw_entries + i * 16u);
        if (!p_span_ok(desc, 16u)) continue;
        unsigned char d[16];
        if (s->read_fn(desc, d, sizeof(d), s->read_user) != 0) continue;
        uint32_t mhandle = p_u32(d + 4u);
        uintptr_t massoc = (uintptr_t)p_u64(d + 8u);
        if (massoc != assoc || !mhandle || mhandle == 0xFFFFFFu) continue;

        uintptr_t mobj = 0u;
        if (!at_resolve_render_mesh_obj(&s->reg, &rhdr, mhandle, s->read_fn, s->read_user, &mobj) || !mobj) {
            continue;
        }
        int dup = 0;
        for (uint32_t k = 0; k < *inout_count; k++) {
            if (out_entries[k].mesh == mobj) {
                out_entries[k].hide_all |= (uint8_t)hide_all;
                out_entries[k].mask_lo |= mask_lo;
                out_entries[k].mask_hi |= mask_hi;
                dup = 1;
                break;
            }
        }
        if (!dup) {
            at_pilot_hide_entry *e = &out_entries[(*inout_count)++];
            memset(e, 0, sizeof(*e));
            e->mesh = mobj;
            e->hide_all = (uint8_t)hide_all;
            e->mask_lo = mask_lo;
            e->mask_hi = mask_hi;
        }
    }
}

void at_writer_step_pilots(at_writer_state *s, uintptr_t explicit_world) {
    if (!s || !__atomic_load_n(&s->ready, __ATOMIC_ACQUIRE)) return;

    uint32_t fast_walkers = __atomic_load_n(&s->fast.count, __ATOMIC_ACQUIRE);
    uint32_t fast_pilot_meshes = __atomic_load_n(&s->fast.pilot_mesh_count, __ATOMIC_ACQUIRE);
    if (fast_walkers == 0u && fast_pilot_meshes == 0u) {
        return; /* Lock-free 2ns fast path when no AT-ST walkers or mounted pilots exist. */
    }
    if (!s->pilot.set_local_pose || !s->pilot.update_unit) return;

    uintptr_t world = explicit_world ? explicit_world : (s->pilot.main_world ? s->pilot.main_world() : 0u);
    if (!p_span_ok(world, 0x200u)) return;

    uintptr_t active_walkers[AT_MAX_WALKERS];
    uint32_t walker_cnt = 0u;

    AcquireSRWLockShared(&s->lock);
    for (unsigned i = 0; i < AT_MAX_WALKERS; i++) {
        if (s->reg.slots[i].occupied && s->reg.slots[i].unit) {
            active_walkers[walker_cnt++] = s->reg.slots[i].unit;
        }
    }
    ReleaseSRWLockShared(&s->lock);

    uint8_t slot_seen[AT_MAX_PILOT_SLOTS] = {0, 0, 0, 0};
    at_pilot_hide_entry hide_list[AT_MAX_PILOT_HIDE_MESHES];
    uint32_t hide_count = 0u;

    for (uint32_t wi = 0; wi < walker_cnt; wi++) {
        uintptr_t wunit = active_walkers[wi];
        uintptr_t wgraph = unit_scene_graph(s, wunit);
        if (!p_span_ok(wgraph, 0x30u)) continue;

        uint32_t wnodes = 0u;
        uintptr_t wwposes = 0u;
        if (s->read_fn(wgraph + AT_GRAPH_NODE_COUNT_OFF, &wnodes, sizeof(wnodes), s->read_user) != 0 ||
            wnodes != AT_S2C_NODES ||
            s->read_fn(wgraph + AT_GRAPH_WORLD_POSES_OFF, &wwposes, sizeof(wwposes), s->read_user) != 0 ||
            !p_span_ok(wwposes, (size_t)AT_S2C_NODES * 64u)) {
            continue;
        }

        uintptr_t child = 0u;
        if (s->read_fn(wunit + AT_UNIT_CHILD_FIRST, &child, sizeof(child), s->read_user) != 0) continue;

        uintptr_t visited[AT_MAX_PILOT_CHILDREN];
        uint32_t n_vis = 0u;
        int have_walker_poses = 0;
        float walker_w64[AT_S2C_NODES][16];

        while (p_span_ok(child, 0x1E8u) && n_vis < AT_MAX_PILOT_CHILDREN) {
            int cycle = 0;
            for (uint32_t k = 0; k < n_vis; k++) {
                if (visited[k] == child) { cycle = 1; break; }
            }
            if (cycle) break;
            visited[n_vis++] = child;

            uintptr_t next_sib = 0u;
            (void)s->read_fn(child + AT_UNIT_CHILD_NEXT, &next_sib, sizeof(next_sib), s->read_user);

            unsigned char chdr[0xD8];
            uintptr_t cparent = 0u;
            if (s->read_fn(child, chdr, sizeof(chdr), s->read_user) == 0 &&
                (uintptr_t)p_u64(chdr + 0x00u) == s->engine_base + AT_RVA_UNIT_VTABLE &&
                p_u32(chdr + AT_UNIT_NODES_OFFSET) == AT_HELLDIVER_NODES &&
                s->read_fn(child + AT_UNIT_PARENT_OFFSET, &cparent, sizeof(cparent), s->read_user) == 0 &&
                cparent == wunit) {

                uintptr_t cres_ptr = (uintptr_t)p_u64(chdr + AT_UNIT_RES_PTR_OFFSET);
                uint64_t cres_id = 0ULL;
                uint32_t cref = p_u32(chdr + AT_UNIT_REF_OFFSET);
                uintptr_t ctopo_ptr = (uintptr_t)p_u64(chdr + AT_UNIT_TOPO_OFFSET);
                struct { uint16_t type; uint16_t parent; } root_topo = {0, 0};

                uintptr_t agraph = unit_scene_graph(s, child);
                uintptr_t alink = 0u, alocal = 0u;
                if (cref != 0u && p_span_ok(cres_ptr, 8u) &&
                    s->read_fn(cres_ptr, &cres_id, 8u, s->read_user) == 0 &&
                    cres_id == AT_HELLDIVER_AVATAR_ID &&
                    p_span_ok(ctopo_ptr, 4u) &&
                    s->read_fn(ctopo_ptr, &root_topo, 4u, s->read_user) == 0 &&
                    root_topo.type == 2u && root_topo.parent < AT_S2C_NODES &&
                    p_span_ok(agraph, 0x30u) &&
                    s->read_fn(agraph + AT_GRAPH_LINK_PARENT_OFF, &alink, sizeof(alink), s->read_user) == 0 &&
                    alink == wwposes &&
                    s->read_fn(agraph + AT_GRAPH_LOCAL_POSES_OFF, &alocal, sizeof(alocal), s->read_user) == 0 &&
                    p_span_ok(alocal, 64u)) {

                    float raw_local[16];
                    if (s->read_fn(alocal, raw_local, sizeof(raw_local), s->read_user) == 0) {
                        if (!have_walker_poses) {
                            if (s->read_fn(wwposes, walker_w64, sizeof(walker_w64), s->read_user) != 0) {
                                child = next_sib;
                                continue;
                            }
                            have_walker_poses = 1;
                        }

                        int pslot_idx = -1, free_idx = -1;
                        for (unsigned ps = 0; ps < AT_MAX_PILOT_SLOTS; ps++) {
                            if (s->pilot.slots[ps].active && s->pilot.slots[ps].avatar_unit == child &&
                                s->pilot.slots[ps].avatar_ref == cref) {
                                pslot_idx = (int)ps;
                                break;
                            }
                            if (!s->pilot.slots[ps].active && free_idx < 0) free_idx = (int)ps;
                        }
                        if (pslot_idx < 0) pslot_idx = free_idx;

                        if (pslot_idx >= 0) {
                            at_pilot_slot *pslot = &s->pilot.slots[pslot_idx];
                            float base_pose[16];
                            if (pslot->active && pslot->has_written && pslot->world == world &&
                                pose_matches_12(raw_local, pslot->last_written_pose)) {
                                memcpy(base_pose, pslot->last_base_pose, sizeof(base_pose));
                            } else {
                                memcpy(base_pose, raw_local, sizeof(base_pose));
                            }

                            float lifted[16];
                            if (at_pilot_compute_cab_local_pose(&s->rig, walker_w64, root_topo.parent, base_pose, lifted)) {
                                s->pilot.set_local_pose(cref, 0, lifted);
                                s->pilot.update_unit(world, child);

                                pslot->active = 1u;
                                pslot->has_written = 1u;
                                pslot->avatar_ref = cref;
                                pslot->avatar_unit = child;
                                pslot->walker_unit = wunit;
                                pslot->world = world;
                                memcpy(pslot->last_base_pose, base_pose, sizeof(base_pose));
                                memcpy(pslot->last_written_pose, lifted, sizeof(lifted));
                                slot_seen[pslot_idx] = 1u;
                                s->pilot.lifted_frames++;

                                /* Update attached child units (armor/garments/cape/equipment) & collect lower-body/cape meshes. */
                                collect_unit_hide_meshes(s, child, 1, hide_list, &hide_count);
                                uintptr_t gchild = 0u;
                                if (s->read_fn(child + AT_UNIT_CHILD_FIRST, &gchild, sizeof(gchild), s->read_user) == 0) {
                                    uintptr_t gvis[AT_MAX_PILOT_CHILDREN];
                                    uint32_t ng = 0u;
                                    while (p_span_ok(gchild, 0x1E8u) && ng < AT_MAX_PILOT_CHILDREN) {
                                        int gcyc = 0;
                                        for (uint32_t k = 0; k < ng; k++) {
                                            if (gvis[k] == gchild) { gcyc = 1; break; }
                                        }
                                        if (gcyc) break;
                                        gvis[ng++] = gchild;

                                        uintptr_t gnext = 0u;
                                        (void)s->read_fn(gchild + AT_UNIT_CHILD_NEXT, &gnext, sizeof(gnext), s->read_user);

                                        uintptr_t gvtbl = 0u;
                                        if (s->read_fn(gchild, &gvtbl, sizeof(gvtbl), s->read_user) == 0 &&
                                            gvtbl == s->engine_base + AT_RVA_UNIT_VTABLE) {
                                            s->pilot.update_unit(world, gchild);
                                            collect_unit_hide_meshes(s, gchild, 0, hide_list, &hide_count);
                                        }
                                        gchild = gnext;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            child = next_sib;
        }
    }

    /* Restore base pose if any previously mounted pilot slot unmounted without overwriting local_poses[0]. */
    for (unsigned ps = 0; ps < AT_MAX_PILOT_SLOTS; ps++) {
        at_pilot_slot *pslot = &s->pilot.slots[ps];
        if (!pslot->active || slot_seen[ps]) continue;
        if (pslot->has_written && pslot->world == world && p_span_ok(pslot->avatar_unit, 0xE0u)) {
            uint32_t live_ref = 0u;
            uintptr_t agraph = unit_scene_graph(s, pslot->avatar_unit);
            uintptr_t alocal = 0u;
            float cur_local[16];
            if (s->read_fn(pslot->avatar_unit + AT_UNIT_REF_OFFSET, &live_ref, sizeof(live_ref), s->read_user) == 0 &&
                live_ref == pslot->avatar_ref &&
                p_span_ok(agraph, 0x30u) &&
                s->read_fn(agraph + AT_GRAPH_LOCAL_POSES_OFF, &alocal, sizeof(alocal), s->read_user) == 0 &&
                p_span_ok(alocal, 64u) &&
                s->read_fn(alocal, cur_local, sizeof(cur_local), s->read_user) == 0 &&
                pose_matches_12(cur_local, pslot->last_written_pose)) {
                s->pilot.set_local_pose(pslot->avatar_ref, 0, pslot->last_base_pose);
                s->pilot.update_unit(world, pslot->avatar_unit);
            }
        }
        memset(pslot, 0, sizeof(*pslot));
    }

    /* Publish pilot lower-body/cape hide table into s->fast under exclusive lock ONLY when changed. */
    int changed = (hide_count != s->pilot_hide_count) ||
                  (hide_count > 0u && memcmp(hide_list, s->pilot_hide_entries, (size_t)hide_count * sizeof(hide_list[0])) != 0);
    if (changed) {
        AcquireSRWLockExclusive(&s->lock);
        s->pilot_hide_count = hide_count;
        memset(s->pilot_hide_entries, 0, sizeof(s->pilot_hide_entries));
        if (hide_count > 0u) {
            memcpy(s->pilot_hide_entries, hide_list, (size_t)hide_count * sizeof(hide_list[0]));
        }
        at_writer_republish_fast_locked(s);
        ReleaseSRWLockExclusive(&s->lock);
    }
}
