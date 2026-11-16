#ifndef LEAF_PLAN_H
#define LEAF_PLAN_H
#include <stdint.h>
#include <stddef.h>
#define LP_GROUPS 8u
#define LP_BONES 64u
#define LP_BLOCKS 32u
#define LP_BYTES 8192u
typedef int (*lp_read_fn)(uintptr_t,void *,size_t,void *);
typedef struct {uint64_t resource;uint32_t ordinal,bones,groups,map[LP_BONES],bind[LP_BONES*12],counts[LP_GROUPS],palette[LP_GROUPS][LP_BONES];} lp_asset;
typedef struct {uintptr_t at;uint32_t offset,bytes;} lp_block;
typedef struct {
 uint32_t phase,stage,error,read_outcome,mismatch_index;
 uintptr_t read_at;uint32_t read_size,comparison_length,probe_offset,probe_bytes;
 unsigned char expected[64],observed[64];
} lp_trace;
typedef struct {
 uintptr_t graph,arrays[2],bind,map,pool_base;
 uint32_t count,mesh_node,groups,output_count,blocks,bytes;
 struct {uintptr_t palette;uint32_t count,offset[2];} group[LP_GROUPS];
 lp_block block[LP_BLOCKS];unsigned char evidence[LP_BYTES];
 lp_trace trace;
} lp_plan;
const lp_asset *lp_asset_find(uint64_t,unsigned);
int lp_capture(lp_plan *,const unsigned char *,uintptr_t,uintptr_t,uint64_t,unsigned,lp_read_fn,void *);
int lp_recheck(lp_plan *,lp_read_fn,void *);
#endif
