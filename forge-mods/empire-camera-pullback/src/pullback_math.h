/* Pose validation, 3D vector helpers, entry blending, and synchronous Havok raycast clamping. */
#ifndef ECP_PULLBACK_MATH_H
#define ECP_PULLBACK_MATH_H

#include <math.h>
#include <stdint.h>
#include <string.h>

typedef struct EcpVec3 {
    float x, y, z;
} EcpVec3;

typedef int (*EcpRaycastFn)(
    uint32_t query,
    const float from[3],
    const float dir[3],
    float length,
    int zero,
    float result[16],
    int closest);

static inline EcpVec3 ecp_v3(float x, float y, float z)
{
    EcpVec3 v = {x, y, z};
    return v;
}

static inline EcpVec3 ecp_add(EcpVec3 a, EcpVec3 b)
{
    return ecp_v3(a.x + b.x, a.y + b.y, a.z + b.z);
}

static inline EcpVec3 ecp_sub(EcpVec3 a, EcpVec3 b)
{
    return ecp_v3(a.x - b.x, a.y - b.y, a.z - b.z);
}

static inline EcpVec3 ecp_scale(EcpVec3 a, float s)
{
    return ecp_v3(a.x * s, a.y * s, a.z * s);
}

static inline float ecp_dot(EcpVec3 a, EcpVec3 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

static inline float ecp_len(EcpVec3 a)
{
    return sqrtf(ecp_dot(a, a));
}

static inline float ecp_xy_dist(EcpVec3 a, EcpVec3 b)
{
    float dx = a.x - b.x;
    float dy = a.y - b.y;
    return sqrtf(dx * dx + dy * dy);
}

static inline int ecp_normalize(EcpVec3* v)
{
    float l = ecp_len(*v);
    if (!isfinite(l) || l < 1e-5f) return 0;
    v->x /= l;
    v->y /= l;
    v->z /= l;
    return 1;
}

/* Validates a 16-float Stingray local pose:
 * [0..2]=right, [3..5]=forward, [6..8]=up, [9..11]=position, [12..15]=scale/pad. */
static inline int ecp_local_pose_valid(const float pose[16], EcpVec3* out_pos, EcpVec3* out_fwd)
{
    if (!pose) return 0;
    for (int i = 0; i < 16; i++) {
        if (!isfinite(pose[i])) return 0;
    }
    EcpVec3 fwd = ecp_v3(pose[3], pose[4], pose[5]);
    float fl = ecp_len(fwd);
    if (fl < 0.85f || fl > 1.15f) return 0;
    fwd = ecp_scale(fwd, 1.0f / fl);
    if (out_pos) *out_pos = ecp_v3(pose[9], pose[10], pose[11]);
    if (out_fwd) *out_fwd = fwd;
    return 1;
}

/* Validates a 16-float Stingray row-major 4x4 world pose (position at [12..14], forward at [4..6]). */
static inline int ecp_world_pose_valid(const float m[16], EcpVec3* out_pos, EcpVec3* out_fwd_xy)
{
    if (!m) return 0;
    for (int i = 0; i < 16; i++) {
        if (!isfinite(m[i])) return 0;
    }
    if (out_pos) *out_pos = ecp_v3(m[12], m[13], m[14]);
    if (out_fwd_xy) {
        EcpVec3 f = ecp_v3(m[4], m[5], 0.0f);
        if (!ecp_normalize(&f)) f = ecp_v3(0.0f, 0.0f, 0.0f);
        *out_fwd_xy = f;
    }
    return 1;
}

/* Smooth blend-in when entering standard 3P follow; INSTANT 0.0f snap on any bypass/guard exit. */
static inline float ecp_step_blend(float current_blend, int active, float dt, float blend_in_seconds)
{
    if (!active) return 0.0f;
    if (blend_in_seconds <= 1e-4f || dt <= 0.0f) return 1.0f;
    if (dt > 0.10f) dt = 0.10f;
    float next = current_blend + (dt / blend_in_seconds);
    return next >= 1.0f ? 1.0f : (next < 0.0f ? 0.0f : next);
}

/* Computes the collision-clamped camera local pose for this frame.
 * Fails closed (returns 0 without modifying out_pose) if raycast is NULL or travel is blocked. */
#define ECP_MAX_DISPLACEMENT_M 16.0f

static inline int ecp_compute_clamped_pose_ex(
    const float game_pose[16],
    float pullback_distance,
    float lift_z,
    float blend,
    float safety_margin,
    uint32_t query_id,
    EcpRaycastFn raycast,
    float out_pose[16],
    float* out_applied_len,
    int* out_ray_hit)
{
    if (out_applied_len) *out_applied_len = 0.0f;
    if (out_ray_hit) *out_ray_hit = 0;
    if (!game_pose || !out_pose) return 0;
    memcpy(out_pose, game_pose, 16u * sizeof(float));

    if (!isfinite(blend) || blend <= 1e-4f) return 0;
    if (!raycast || query_id == 0u) return 0; /* Fail closed when Havok raycast is unavailable. */

    EcpVec3 cam_pos, cam_fwd;
    if (!ecp_local_pose_valid(game_pose, &cam_pos, &cam_fwd)) return 0;

    EcpVec3 delta = ecp_v3(
        -cam_fwd.x * pullback_distance * blend,
        -cam_fwd.y * pullback_distance * blend,
        (-cam_fwd.z * pullback_distance + lift_z) * blend);

    float raw_len = ecp_len(delta);
    if (!isfinite(raw_len) || raw_len <= 1e-4f) return 0;
    float desired_len = raw_len > ECP_MAX_DISPLACEMENT_M ? ECP_MAX_DISPLACEMENT_M : raw_len;

    EcpVec3 dir = ecp_scale(delta, 1.0f / raw_len);
    float margin = (isfinite(safety_margin) && safety_margin > 0.05f) ? safety_margin : 0.28f;
    float cast_len = desired_len + margin;

    float from[3] = {cam_pos.x, cam_pos.y, cam_pos.z};
    float udir[3] = {dir.x, dir.y, dir.z};
    float hit_buf[16];
    memset(hit_buf, 0, sizeof(hit_buf));

    float allowed_len = desired_len;
    int hits = raycast(query_id, from, udir, cast_len, 0, hit_buf, 1);
    if (hits > 0) {
        if (out_ray_hit) *out_ray_hit = 1;
        EcpVec3 hit_pos = ecp_v3(hit_buf[0], hit_buf[1], hit_buf[2]);
        if (!isfinite(hit_pos.x) || !isfinite(hit_pos.y) || !isfinite(hit_pos.z)) {
            return 0;
        }
        float d_hit = ecp_dot(ecp_sub(hit_pos, cam_pos), dir);
        if (!isfinite(d_hit) || d_hit <= margin) {
            return 0;
        }
        float clamped = d_hit - margin;
        if (clamped < allowed_len) allowed_len = clamped;
    }

    if (allowed_len <= 1e-4f) return 0;

    float nx = cam_pos.x + dir.x * allowed_len;
    float ny = cam_pos.y + dir.y * allowed_len;
    float nz = cam_pos.z + dir.z * allowed_len;
    if (!isfinite(nx) || !isfinite(ny) || !isfinite(nz)) return 0;

    out_pose[9] = nx;
    out_pose[10] = ny;
    out_pose[11] = nz;
    if (out_applied_len) *out_applied_len = allowed_len;
    return 1;
}

static inline int ecp_compute_clamped_pose(
    const float game_pose[16],
    float pullback_distance,
    float lift_z,
    float blend,
    float safety_margin,
    uint32_t query_id,
    EcpRaycastFn raycast,
    float out_pose[16],
    float* out_applied_len)
{
    return ecp_compute_clamped_pose_ex(
        game_pose,
        pullback_distance,
        lift_z,
        blend,
        safety_margin,
        query_id,
        raycast,
        out_pose,
        out_applied_len,
        NULL);
}

#endif /* ECP_PULLBACK_MATH_H */
