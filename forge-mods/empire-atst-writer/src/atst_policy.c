/* AT-ST film-scale joint-aware kinematics and S2c graph scatter implementation. */
#include "atst_policy.h"

#include <fenv.h>
#include <float.h>
#include <math.h>
#include <string.h>

#define TRY(expr)               \
    do {                        \
        at_status st_ = (expr); \
        if (st_ != AT_OK) {     \
            return st_;         \
        }                       \
    } while (0)

static int overlap(const void *a, size_t an, const void *b, size_t bn) {
    uintptr_t aa = (uintptr_t)a;
    uintptr_t bb = (uintptr_t)b;
    if (!a || !b || !an || !bn) return 0;
    return aa < bb + bn && bb < aa + an;
}

static at_mat4 mat4_identity(void) {
    at_mat4 m;
    memset(&m, 0, sizeof(m));
    m.v[0] = 1.0;
    m.v[5] = 1.0;
    m.v[10] = 1.0;
    m.v[15] = 1.0;
    return m;
}

static double mat3_det(const at_mat4 *m) {
    return m->v[0] * (m->v[5] * m->v[10] - m->v[6] * m->v[9]) -
           m->v[1] * (m->v[4] * m->v[10] - m->v[6] * m->v[8]) +
           m->v[2] * (m->v[4] * m->v[9] - m->v[5] * m->v[8]);
}

static at_status check_affine(const at_mat4 *m) {
    for (unsigned i = 0; i < 16; i++) {
        if (!isfinite(m->v[i])) return AT_NONFINITE;
    }
    if (m->v[12] != 0.0 || m->v[13] != 0.0 || m->v[14] != 0.0 || m->v[15] != 1.0) {
        return AT_AFFINE;
    }
    double d = mat3_det(m);
    if (!isfinite(d)) return AT_NONFINITE;
    if (fabs(d) <= 1e-12) return AT_SINGULAR;
    return AT_OK;
}

static at_mat4 mat4_mul(const at_mat4 *a, const at_mat4 *b) {
    at_mat4 out;
    memset(&out, 0, sizeof(out));
    for (unsigned r = 0; r < 3; r++) {
        for (unsigned c = 0; c < 4; c++) {
            double s = a->v[r * 4 + 0] * b->v[0 * 4 + c] +
                       a->v[r * 4 + 1] * b->v[1 * 4 + c] +
                       a->v[r * 4 + 2] * b->v[2 * 4 + c];
            if (c == 3) s += a->v[r * 4 + 3];
            out.v[r * 4 + c] = s;
        }
    }
    out.v[15] = 1.0;
    return out;
}

at_status at_inv_affine(const at_mat4 *m, at_mat4 *out) {
    if (!m || !out) return AT_ARGUMENT;
    if (overlap(m, sizeof(*m), out, sizeof(*out))) return AT_OVERLAP;
    TRY(check_affine(m));
    double d = mat3_det(m);
    double invd = 1.0 / d;
    at_mat4 inv = mat4_identity();
    inv.v[0] = (m->v[5] * m->v[10] - m->v[6] * m->v[9]) * invd;
    inv.v[1] = (m->v[2] * m->v[9] - m->v[1] * m->v[10]) * invd;
    inv.v[2] = (m->v[1] * m->v[6] - m->v[2] * m->v[5]) * invd;
    inv.v[4] = (m->v[6] * m->v[8] - m->v[4] * m->v[10]) * invd;
    inv.v[5] = (m->v[0] * m->v[10] - m->v[2] * m->v[8]) * invd;
    inv.v[6] = (m->v[2] * m->v[4] - m->v[0] * m->v[6]) * invd;
    inv.v[8] = (m->v[4] * m->v[9] - m->v[5] * m->v[8]) * invd;
    inv.v[9] = (m->v[1] * m->v[8] - m->v[0] * m->v[9]) * invd;
    inv.v[10] = (m->v[0] * m->v[5] - m->v[1] * m->v[4]) * invd;

    double tx = m->v[3], ty = m->v[7], tz = m->v[11];
    inv.v[3] = -(inv.v[0] * tx + inv.v[1] * ty + inv.v[2] * tz);
    inv.v[7] = -(inv.v[4] * tx + inv.v[5] * ty + inv.v[6] * tz);
    inv.v[11] = -(inv.v[8] * tx + inv.v[9] * ty + inv.v[10] * tz);
    TRY(check_affine(&inv));
    *out = inv;
    return AT_OK;
}

at_status at_unpack(const at_render48 *in, at_mat4 *out) {
    if (!in || !out) return AT_ARGUMENT;
    if (overlap(in, sizeof(*in), out, sizeof(*out))) return AT_OVERLAP;
    at_mat4 m = mat4_identity();
    for (unsigned i = 0; i < 12; i++) {
        if (!isfinite(in->v[i])) return AT_NONFINITE;
        m.v[i] = (double)in->v[i];
    }
    TRY(check_affine(&m));
    *out = m;
    return AT_OK;
}

at_status at_pack(const at_mat4 *m, at_render48 *out) {
    if (!m || !out) return AT_ARGUMENT;
    if (overlap(m, sizeof(*m), out, sizeof(*out))) return AT_OVERLAP;
    if (fegetround() != FE_TONEAREST) return AT_NUMERIC;
    TRY(check_affine(m));
    at_render48 p;
    at_mat4 check = mat4_identity();
    for (unsigned k = 0; k < 12; k++) {
        if (fabs(m->v[k]) > (double)FLT_MAX) return AT_NUMERIC;
        p.v[k] = (float)m->v[k];
        if (!isfinite(p.v[k]) ||
            (m->v[k] != 0.0 && p.v[k] == 0.0f) ||
            (p.v[k] != 0.0f && fabsf(p.v[k]) < FLT_MIN)) {
            return AT_NUMERIC;
        }
        check.v[k] = (double)p.v[k];
    }
    TRY(check_affine(&check));
    *out = p;
    return AT_OK;
}

static at_status number32(float x) {
    if (!isfinite(x)) return AT_NONFINITE;
    if (x != 0.0f && fabsf(x) < FLT_MIN) return AT_NUMERIC;
    return AT_OK;
}

static at_status mul32(float a, float b, float *out) {
    volatile float v = a * b;
    *out = v;
    TRY(number32(v));
    if (a != 0.0f && b != 0.0f && v == 0.0f) return AT_NUMERIC;
    return AT_OK;
}

static at_status add32(float a, float b, float *out) {
    volatile float v = a + b;
    *out = v;
    return number32(v);
}

at_status at_compose48(const at_render48 *a, const at_render48 *b, at_render48 *out) {
    if (!a || !b || !out) return AT_ARGUMENT;
    if (overlap(out, sizeof(*out), a, sizeof(*a)) || overlap(out, sizeof(*out), b, sizeof(*b))) {
        return AT_OVERLAP;
    }
    if (fegetround() != FE_TONEAREST) return AT_NUMERIC;
    for (unsigned i = 0; i < 12; i++) {
        TRY(number32(a->v[i]));
        TRY(number32(b->v[i]));
    }
    at_render48 res;
    for (unsigned r = 0; r < 3; r++) {
        for (unsigned c = 0; c < 4; c++) {
            float t0 = 0.0f, t1 = 0.0f, t2 = 0.0f, sum = 0.0f;
            TRY(mul32(a->v[r * 4 + 0], b->v[0 * 4 + c], &t0));
            TRY(mul32(a->v[r * 4 + 1], b->v[1 * 4 + c], &t1));
            TRY(mul32(a->v[r * 4 + 2], b->v[2 * 4 + c], &t2));
            TRY(add32(t0, t1, &sum));
            TRY(add32(sum, t2, &sum));
            if (c == 3) TRY(add32(sum, a->v[r * 4 + 3], &sum));
            res.v[r * 4 + c] = sum;
        }
    }
    at_mat4 verify;
    TRY(at_unpack(&res, &verify));
    *out = res;
    return AT_OK;
}

static double vec3_norm(const at_mat4 *m) {
    double x = m->v[3], y = m->v[7], z = m->v[11];
    return sqrt(x * x + y * y + z * z);
}

at_status at_rig_init(
    at_rig *out,
    const uint16_t parent[AT_JOINTS],
    const uint32_t hash[AT_JOINTS],
    const at_mat4 native_local[AT_JOINTS],
    const at_mat4 authored_local[AT_JOINTS]) {
    if (!out || !parent || !hash || !native_local || !authored_local) return AT_ARGUMENT;
    at_rig r;
    memset(&r, 0, sizeof(r));
    for (unsigned i = 0; i < AT_JOINTS; i++) {
        if (parent[i] > i) return AT_HIERARCHY;
        if (i > 0 && parent[i] >= i) return AT_HIERARCHY;
        r.parent[i] = parent[i];
        r.hash[i] = hash[i];
        TRY(check_affine(&native_local[i]));
        TRY(check_affine(&authored_local[i]));
        r.native_local[i] = native_local[i];
        r.authored_local[i] = authored_local[i];
        if (i == 0u) {
            r.ratio[i] = AT_FILM_SCALE_STRIDE;
        } else {
            double nn = vec3_norm(&native_local[i]);
            double aa = vec3_norm(&authored_local[i]);
            if (!isfinite(nn) || !isfinite(aa)) return AT_NONFINITE;
            if (nn <= 1e-8 && aa > 1e-8) return AT_RATIO;
            r.ratio[i] = (nn > 1e-8) ? (aa / nn) : 1.0;
        }
        if (r.ratio[i] < 0.1 || r.ratio[i] > 10.0 || !isfinite(r.ratio[i])) return AT_RATIO;
    }
    *out = r;
    return AT_OK;
}

at_status at_rig_init_pinned(at_rig *out) {
    if (!out) return AT_ARGUMENT;
    at_mat4 nat[AT_JOINTS];
    at_mat4 auth[AT_JOINTS];
    for (unsigned i = 0; i < AT_JOINTS; i++) {
        nat[i] = mat4_identity();
        auth[i] = mat4_identity();
        for (unsigned k = 0; k < 12; k++) {
            nat[i].v[k] = (double)AT_NAT_LOCAL_198[i][k];
            auth[i].v[k] = (double)AT_AUTH_LOCAL_198[i][k];
        }
    }
    return at_rig_init(out, AT_PARENT_198, AT_HASH_198, nat, auth);
}

static at_status compute_world(
    unsigned i,
    const at_rig *rig,
    const at_mat4 local[AT_JOINTS],
    at_mat4 world_out[AT_JOINTS],
    unsigned char done[AT_JOINTS]) {
    if (done[i]) return AT_OK;
    unsigned p = rig->parent[i];
    if (p == i) {
        world_out[i] = local[i];
    } else {
        TRY(compute_world(p, rig, local, world_out, done));
        world_out[i] = mat4_mul(&world_out[p], &local[i]);
    }
    TRY(check_affine(&world_out[i]));
    done[i] = 1;
    return AT_OK;
}

at_status at_retarget_pair(const at_rig *rig, const at_pair *pair, at_retarget_result *out) {
    if (!rig || !pair || !out) return AT_ARGUMENT;
    if (overlap(out, sizeof(*out), rig, sizeof(*rig)) ||
        overlap(out, sizeof(*out), pair, sizeof(*pair))) {
        return AT_OVERLAP;
    }
    if (fegetround() != FE_TONEAREST) return AT_NUMERIC;

    at_retarget_result res;
    memset(&res, 0, sizeof(res));
    for (unsigned t = 0; t < 2; t++) {
        const at_pose *pose = (t == 0) ? &pair->current : &pair->previous;
        at_mat4 scaled_local[AT_JOINTS];
        unsigned char done[AT_JOINTS];
        memset(done, 0, sizeof(done));

        for (unsigned i = 0; i < AT_JOINTS; i++) {
            TRY(check_affine(&pose->local[i]));
            scaled_local[i] = pose->local[i];
            for (unsigned k = 3; k < 12; k += 4) {
                double delta = pose->local[i].v[k] - rig->native_local[i].v[k];
                scaled_local[i].v[k] = rig->authored_local[i].v[k] + rig->ratio[i] * delta;
            }
            TRY(check_affine(&scaled_local[i]));
        }
        for (unsigned i = 0; i < AT_JOINTS; i++) {
            TRY(compute_world(i, rig, scaled_local, res.world[t], done));
            TRY(at_pack(&res.world[t][i], &res.packed[t][i]));
        }
    }
    *out = res;
    return AT_OK;
}

static at_status check_unit_rot_frame(const at_mat4 *m) {
    if (mat3_det(m) <= 0.0) return AT_AFFINE;
    for (unsigned a = 0; a < 3; a++) {
        for (unsigned b = 0; b < 3; b++) {
            double gram = 0.0;
            for (unsigned k = 0; k < 3; k++) {
                gram += m->v[k * 4 + a] * m->v[k * 4 + b];
            }
            if (fabs(gram - (a == b ? 1.0 : 0.0)) > 2e-5) return AT_AFFINE;
        }
    }
    return AT_OK;
}

at_status at_scatter(
    const at_leaf_plan *plan,
    const at_retarget_result *retarget,
    at_private_graph *out) {
    if (!plan || !retarget || !out) return AT_ARGUMENT;
    if (overlap(out, sizeof(*out), plan, sizeof(*plan)) ||
        overlap(out, sizeof(*out), retarget, sizeof(*retarget))) {
        return AT_OVERLAP;
    }
    if (fegetround() != FE_TONEAREST) return AT_NUMERIC;
    if (!plan->graph_count || plan->graph_count > AT_GRAPH_CAP ||
        plan->previous_count != plan->graph_count ||
        plan->bones != AT_SKIN_BONES_COUNT ||
        plan->protected_count > AT_S2C_NODES) {
        return AT_MAPPING;
    }

    uint8_t slot_used[AT_GRAPH_CAP];
    memset(slot_used, 0, sizeof(slot_used));
    for (unsigned i = 0; i < plan->bones; i++) {
        if (plan->map[i] >= plan->graph_count || plan->joint[i] >= AT_JOINTS || plan->used[i] > 1u) {
            return AT_MAPPING;
        }
        if (plan->used[i]) {
            if (slot_used[plan->map[i]]) return AT_MAPPING;
            slot_used[plan->map[i]] = 1u;
        }
    }
    for (unsigned i = 0; i < plan->protected_count; i++) {
        if (plan->protected_slot[i] >= plan->graph_count || slot_used[plan->protected_slot[i]]) {
            return AT_MAPPING;
        }
    }

    for (unsigned t = 0; t < 2; t++) {
        at_render48 frame;
        TRY(at_pack(&plan->model_to_graph[t], &frame));
        at_mat4 frame_q;
        TRY(at_unpack(&frame, &frame_q));
        TRY(check_unit_rot_frame(&frame_q));

        for (unsigned b = 0; b < plan->bones; b++) {
            if (!plan->used[b]) continue;
            at_render48 world_packed;
            TRY(at_pack(&retarget->world[t][plan->joint[b]], &world_packed));
            TRY(at_compose48(&frame, &world_packed, &out->graph[t][plan->map[b]]));
        }
    }
    return AT_OK;
}

void at_init_s2c_plan(at_leaf_plan *plan, const at_mat4 *model_cur, const at_mat4 *model_prev) {
    memset(plan, 0, sizeof(*plan));
    plan->graph_count = AT_S2C_NODES;
    plan->previous_count = AT_S2C_NODES;
    plan->bones = AT_SKIN_BONES_COUNT;
    for (unsigned b = 0; b < AT_SKIN_BONES_COUNT; b++) {
        plan->map[b] = AT_SKIN_BONES[b];
        plan->joint[b] = AT_SKIN_ORIG_JOINTS[b];
        plan->used[b] = 1u;
    }
    for (unsigned k = 0; k < AT_S2C_NODES; k++) {
        /* All functional non-weighted sockets stay untouched at native graph slots. */
        int is_skin = 0;
        for (unsigned b = 0; b < AT_SKIN_BONES_COUNT; b++) {
            if (AT_SKIN_BONES[b] == k) {
                is_skin = 1;
                break;
            }
        }
        if (!is_skin && AT_S2C_IS_PROTECTED[k]) {
            plan->protected_slot[plan->protected_count++] = k;
        }
    }
    plan->model_to_graph[0] = model_cur ? *model_cur : mat4_identity();
    plan->model_to_graph[1] = model_prev ? *model_prev : mat4_identity();
}

at_status at_evaluate_s2c_pair(
    const at_rig *rig,
    const at_render48 *cur_s2c,
    const at_render48 *prev_s2c,
    uint32_t node_count,
    at_private_graph *out) {
    if (!rig || !cur_s2c || !prev_s2c || !out) return AT_ARGUMENT;
    if (node_count != AT_S2C_NODES) return AT_COUNT;
    if (fegetround() != FE_TONEAREST) return AT_NUMERIC;

    at_pair poses;
    at_mat4 g_unpacked[2][AT_S2C_NODES];
    at_mat4 inv_g[AT_S2C_NODES];

    for (unsigned t = 0; t < 2; t++) {
        const at_render48 *src = (t == 0) ? cur_s2c : prev_s2c;
        at_pose *pose = (t == 0) ? &poses.current : &poses.previous;

        /* Unpack all 106 runtime S2c nodes and pre-fill collapsed leaves with native rest. */
        for (unsigned i = 0; i < AT_JOINTS; i++) {
            pose->local[i] = rig->native_local[i];
        }
        for (unsigned k = 0; k < AT_S2C_NODES; k++) {
            TRY(at_unpack(&src[k], &g_unpacked[t][k]));
            TRY(at_inv_affine(&g_unpacked[t][k], &inv_g[k]));
        }

        /* G[0] acts as model_to_graph[t]; joint 0 local relative to model root is Identity. */
        pose->local[0] = mat4_identity();
        for (unsigned k = 1; k < AT_S2C_NODES; k++) {
            unsigned p = AT_S2C_PARENT[k];
            unsigned orig_j = AT_S2C_ORIG_JOINT[k];
            if (p == k) {
                pose->local[orig_j] = mat4_mul(&inv_g[0], &g_unpacked[t][k]);
            } else {
                pose->local[orig_j] = mat4_mul(&inv_g[p], &g_unpacked[t][k]);
            }
            TRY(check_affine(&pose->local[orig_j]));
        }
    }

    at_retarget_result retarg;
    TRY(at_retarget_pair(rig, &poses, &retarg));

    at_private_graph next_graph;
    memset(&next_graph, 0, sizeof(next_graph));
    memcpy(next_graph.graph[0], cur_s2c, AT_S2C_NODES * sizeof(at_render48));
    memcpy(next_graph.graph[1], prev_s2c, AT_S2C_NODES * sizeof(at_render48));

    at_leaf_plan plan;
    at_init_s2c_plan(&plan, &g_unpacked[0][0], &g_unpacked[1][0]);
    TRY(at_scatter(&plan, &retarg, &next_graph));

    *out = next_graph;
    return AT_OK;
}

const char *at_status_name(at_status s) {
    static const char *const names[] = {
        "OK", "ARGUMENT", "COUNT", "HASH", "HIERARCHY", "NONFINITE", "AFFINE",
        "SINGULAR", "RATIO", "NUMERIC", "OVERLAP", "TEMPORAL", "MAPPING",
        "LAYOUT", "PALETTE", "FRAME_UNKNOWN", "PIN"
    };
    unsigned idx = (unsigned)s;
    return idx < (unsigned)(sizeof(names) / sizeof(names[0])) ? names[idx] : "UNKNOWN";
}
