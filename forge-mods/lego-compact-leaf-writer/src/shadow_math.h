#ifndef SHADOW_MATH_H
#define SHADOW_MATH_H
#include "compact_math.h"
#define SM_CAP 128u
#define SM_RAW_BYTES 46144u
/* Private record tail. Nothing here is serialized into LGSKIN01. */
typedef struct {
 uint32_t current_status,previous_status,protected_rows,mesh_rows,header_ok;
 uint32_t math_mxcsr,entry_mxcsr,restored_mxcsr,scope_verified,math_ok;
 uint32_t route_ok,reserved[5];
 unsigned char graph_header[64];
 cm_render48 current[SM_CAP],previous[SM_CAP];
} sm_result;
typedef struct {uintptr_t mesh,pool,graph,caller,engine;uint64_t resource;uint32_t ordinal,entry_mxcsr;} sm_call;
/* Caller owns and serializes rig/workspace; record result belongs to this call.
   Payload must contain a completed accepted immutable sampler result. Core
   independently checks scalar route/count fields. No live reads or game writes. */
CM_API cm_status sm_prepare(cm_rig *rig);
CM_API void sm_evaluate(const cm_rig *rig,const sm_call *call,const unsigned char *payload,size_t bytes,
                 cm_workspace *work,sm_result *result);
#endif
