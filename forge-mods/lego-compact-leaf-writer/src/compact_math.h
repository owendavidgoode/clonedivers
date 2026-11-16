#ifndef COMPACT_MATH_H
#define COMPACT_MATH_H
#include <stddef.h>
#include <stdint.h>
#ifdef _WIN32
#define CM_API __declspec(dllexport)
#else
#define CM_API
#endif
#define CM_SOURCE 65
#define CM_NATIVE 107
#define CM_GRAPH_CAP 512
typedef enum {
 CM_OK=0, CM_ARGUMENT, CM_COUNT, CM_INDEX, CM_MAPPING, CM_BASIS_UNKNOWN,
 CM_BASIS, CM_NONFINITE, CM_SINGULAR, CM_SCALE, CM_SHEAR, CM_REFLECTION,
 CM_POLICY, CM_NUMERICAL, CM_OVERLAP, CM_UNPREPARED
} cm_status;
typedef enum { CM_INHERIT_HELPERS=0, CM_HEADPEG_ONLY=1,
 CM_YAW_TORSO_NECK=2, /* Explicit opt-in: baseline arms, no hand IK. */
 CM_NECK_AIM_ONLY=3, /* Baseline torso/arms; only NeckPeg/HeadPeg branch changes. */
 CM_ARM_SHOULDER_HINGE=4, /* Baseline neck; torso-sagittal shoulder hinge on arms. */
 CM_NECK_AND_ARM_SHOULDER_HINGE=5 /* Combined neck aim + torso-sagittal shoulder hinge arms. */ } cm_policy;
/* render48 is row-major 3x4 float; mat4 is row-major column-vector affine double. */
typedef struct { float v[12]; } cm_render48;
typedef struct { double v[16]; } cm_mat4;
typedef struct { uint32_t source, node; } cm_alias;
typedef struct { const char *resource; uint32_t count, alias_count; const cm_alias *aliases; } cm_garment;
typedef struct {
 uint32_t magic, policy;
 cm_mat4 source[65], local[65];
 double native_inverse[107][9], native_scale[107][3], basis[9], basis_inverse[9];
} cm_rig;
typedef struct {
 double scale_relative_error, delta_gram_error, ignored_native_translation_max;
 uint32_t nonunit_native_rest_nodes;
} cm_diagnostics;
typedef struct {
 double delta[107][9];
 cm_mat4 world[65], local[65];
 cm_render48 graph[512];
 cm_diagnostics diagnostics;
} cm_workspace;
typedef struct {
 const cm_render48 *parent; size_t parent_count;
 const cm_render48 *graph; size_t graph_count;
 uint32_t garment_index, mesh_node;
 /* Explicit coordinate conversion; caller qualification is NOT inferred.
    Both frames allow max column-Gram error <=2e-6 and require positive
    determinant; H is evaluated as supplied doubles, F after float32 packing.
    This accommodates float32-derived near-rigid frames without snapping.
    Native pose decomposition/scale checks remain separate and can refuse. */
 const cm_mat4 *parent_graph_to_native_rest_model;
 const cm_mat4 *source_model_to_selected_graph;
 uint32_t basis_known;
} cm_frame;
/* Fixed accepted source65/native107 rig. Explicit helper policy, no default.
   basis is a row-major proper-rotation double[9]; known=0 always refuses.
   After nonnull/disjoint arguments are established, failure invalidates
   out->magic. Null-argument/overlap refusal leaves memory untouched; callers
   must use the returned status, not a prior magic value. Caller owns buffers. */
CM_API cm_status cm_prepare(cm_rig *out, const double basis[9], uint32_t known, cm_policy policy);
/* One temporal sample ONLY. No current/previous substitution. All pointed-to
   inputs must have valid extents and remain immutable throughout this call.
   Output commits only on success; workspace is scratch and unspecified on error.
   Rig/workspace/output must be mutually disjoint and not overlap input ranges.
   world/local and diagnostics remain available in workspace after success.
   Actual mesh node is checked/preserved; no factor/publication occurs here. */
CM_API cm_status cm_evaluate(const cm_rig *rig, const cm_frame *frame,
                            cm_workspace *work, cm_render48 *out, size_t capacity);
/* Literal separate binary32 compose, also used by scatter. Atomic output.
   Finite normal/zero arithmetic; required MXCSR nearest-even, masks on, DAZ/FTZ off. */
CM_API cm_status cm_compose(const cm_render48 *left, const cm_render48 *right, cm_render48 *out);
CM_API const cm_garment *cm_garment_at(uint32_t index);
#endif
