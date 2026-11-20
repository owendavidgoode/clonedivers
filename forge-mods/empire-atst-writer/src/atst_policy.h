/* AT-ST film-scale joint-aware retargeting and S2c graph scatter policy. */
#ifndef ATST_POLICY_H
#define ATST_POLICY_H

#include "atst_asset_pins.h"
#include <stddef.h>
#include <stdint.h>

typedef enum {
    AT_OK = 0,
    AT_ARGUMENT,
    AT_COUNT,
    AT_HASH,
    AT_HIERARCHY,
    AT_NONFINITE,
    AT_AFFINE,
    AT_SINGULAR,
    AT_RATIO,
    AT_NUMERIC,
    AT_OVERLAP,
    AT_TEMPORAL,
    AT_MAPPING,
    AT_LAYOUT,
    AT_PALETTE,
    AT_FRAME_UNKNOWN,
    AT_PIN
} at_status;

typedef struct {
    double v[16]; /* 4x4 affine row-major */
} at_mat4;

typedef struct {
    float v[12]; /* 3x4 row-major float32 (implicit bottom row 0,0,0,1) */
} at_render48;

typedef struct {
    uint16_t parent[AT_JOINTS];
    uint32_t hash[AT_JOINTS];
    at_mat4 native_local[AT_JOINTS];
    at_mat4 authored_local[AT_JOINTS];
    double ratio[AT_JOINTS];
} at_rig;

typedef struct {
    at_mat4 local[AT_JOINTS];
} at_pose;

typedef struct {
    at_pose current;
    at_pose previous;
} at_pair;

typedef struct {
    at_mat4 world[2][AT_JOINTS];
    at_render48 packed[2][AT_JOINTS];
} at_retarget_result;

typedef struct {
    uint32_t graph_count;
    uint32_t previous_count;
    uint32_t bones;
    uint32_t map[AT_SKIN_BONES_COUNT];
    uint32_t joint[AT_SKIN_BONES_COUNT];
    uint8_t used[AT_SKIN_BONES_COUNT];
    uint32_t protected_count;
    uint32_t protected_slot[AT_S2C_NODES];
    at_mat4 model_to_graph[2];
} at_leaf_plan;

typedef struct {
    at_render48 graph[2][AT_GRAPH_CAP];
} at_private_graph;

/** Initialize a 198-joint retargeting rig from explicit arrays (ratio[0] = AT_FILM_SCALE_STRIDE). */
at_status at_rig_init(
    at_rig *out,
    const uint16_t parent[AT_JOINTS],
    const uint32_t hash[AT_JOINTS],
    const at_mat4 native_local[AT_JOINTS],
    const at_mat4 authored_local[AT_JOINTS]);

/** Initialize the pinned 198-joint AT-ST rig from atst_asset_pins.h. */
at_status at_rig_init_pinned(at_rig *out);

/** Unpack row-major 3x4 render48 into affine 4x4 double matrix. */
at_status at_unpack(const at_render48 *in, at_mat4 *out);

/** Pack affine 4x4 double matrix into finite non-subnormal 3x4 float32 render48. */
at_status at_pack(const at_mat4 *m, at_render48 *out);

/** Invert an affine 4x4 matrix (det > 0 or non-singular). */
at_status at_inv_affine(const at_mat4 *m, at_mat4 *out);

/** Quantized float32 composition matching Stingray render48 composition. */
at_status at_compose48(const at_render48 *a, const at_render48 *b, at_render48 *out);

/** Retarget a (current, previous) 198-joint native local pose pair into film-scale world frames. */
at_status at_retarget_pair(const at_rig *rig, const at_pair *pair, at_retarget_result *out);

/** Scatter retargeted skin bone transforms into private S2c RenderGraph buffers using plan. */
at_status at_scatter(
    const at_leaf_plan *plan,
    const at_retarget_result *retarget,
    at_private_graph *out);

/** End-to-end S2c runtime evaluation: recover local kinematics from cur/prev 106-node render48
 *  graphs, retarget with joint-aware kinematics (root anchored to G[t][0]), and scatter into out. */
at_status at_evaluate_s2c_pair(
    const at_rig *rig,
    const at_render48 *cur_s2c,
    const at_render48 *prev_s2c,
    uint32_t node_count,
    at_private_graph *out);

/** Build the canonical S2c leaf scatter plan with runtime model_to_graph frames. */
void at_init_s2c_plan(at_leaf_plan *plan, const at_mat4 *model_cur, const at_mat4 *model_prev);

const char *at_status_name(at_status status);

#endif /* ATST_POLICY_H */
