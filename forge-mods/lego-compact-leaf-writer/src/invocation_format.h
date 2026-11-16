#ifndef INVOCATION_FORMAT_H
#define INVOCATION_FORMAT_H
/* File wire format is explicitly little-endian; never dump a C struct. */
#define IV_VERSION 1u
#define IV_HEADER_BYTES 256u
#define IV_META_BYTES 1024u
#define IV_PAYLOAD_BYTES 46144u
#define IV_FILE_BYTES (IV_HEADER_BYTES+IV_PAYLOAD_BYTES)
#define IV_ATTEMPTS 512u
#define IV_PER_MESH 2u
#define IV_SELECTED_NODES 128u
#define IV_PARENT_NODES 107u
#define IV_DRAIN_MS 2000u
#define IV_RETIRED (-1003)
enum IvStatus { IV_OK=0,IV_BINDING=1,IV_PARENT_BINDING=2,IV_SELECTED_HEADER=3,
    IV_PARENT_HEADER=4,IV_MESH_CONTEXT=5,IV_POOL_CONTEXT=6,IV_POSE_READ=7,
    IV_POSE_CHANGED=8,IV_HEADER_CHANGED=9,IV_BUDGET=10,IV_STOPPED=11,IV_WHITELIST=12 };
enum IvStage { IV_OUTER_BEFORE=1,IV_PARENT_BEFORE=2,IV_SELECTED_BEFORE=3,
    IV_PARENT_GRAPH_BEFORE=4,IV_MESH_BEFORE=5,IV_POOL_BEFORE=6,
    IV_ARRAY_0=10,IV_SELECTED_AFTER=20,IV_PARENT_GRAPH_AFTER=21,
    IV_MESH_AFTER=22,IV_POOL_AFTER=23,IV_PARENT_AFTER=24,IV_OUTER_AFTER=30 };
/* Valid bits:0 outer begin;1 parent app before;2 parent resolve before;
   3 selected header before;4 parent header before;5 mesh before;6 pool before;
   8..15 complete array sections;16 selected after;17 parent header after;
   18 mesh after;19 pool after;20 parent app after;21 parent resolve after;
   22 full outer owner/target bracket accepted. Bit7 and23..31 are zero. */
#define IV_VALID_COMPLETE 0x007FFF7Fu
/* Acceptance flags:bit0 selected arrays equal;1 parent arrays equal;
   2 raw graph headers equal;3 stable mesh context equal;4 pool output base equal;
   5 parent application/resolution equal;6 full outer bracket accepted. */
#define IV_FLAGS_COMPLETE 0x7Fu
static const uint32_t iv_array_offsets[8]={1024,7168,13312,18448,23584,29728,35872,41008};
static const uint32_t iv_array_capacity[8]={6144,6144,5136,5136,6144,6144,5136,5136};
static void IvU32(unsigned char *p,unsigned off,uint32_t v){memcpy(p+off,&v,4);}
static void IvU64(unsigned char *p,unsigned off,uint64_t v){memcpy(p+off,&v,8);}
#endif
