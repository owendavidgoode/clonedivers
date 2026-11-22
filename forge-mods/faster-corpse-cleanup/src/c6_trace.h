#ifndef C6_TRACE_H
#define C6_TRACE_H
#include <stdint.h>
#define C6_TRACE_LIMIT 1024u
#define C6_TRACE_SUMMARIES 16u
#define C6_TRACE_REGULAR_LIMIT 960u
#define C6_TRACE_CRITICAL_LIMIT 64u
#define C6_TRACE_PERIODIC_SUMMARIES 14u
typedef enum C6TraceEvent {C6T_SELECT,C6T_ACQUIRE,C6T_READY,C6T_REGISTER_BEGIN,C6T_REGISTER_END,C6T_GET_BEGIN,C6T_GET_END,C6T_SET_BEGIN,C6T_SET_END,C6T_EPOCH,C6T_POLL,C6T_APPLY,C6T_RESYNC,C6T_SAVE_FAILURE,C6T_CAPACITY,C6T_COUNT} C6TraceEvent;
typedef void (*C6TraceSink)(void *,const char *);
typedef struct C6TraceLast {double value;uint64_t generation;int status,detail,seen;} C6TraceLast;
typedef struct C6Trace {C6TraceSink sink;void *context;uint32_t sequence,emitted,dropped,summaries,regular_emitted,critical_emitted,periodic_summaries;unsigned regular_cap_reported,critical_cap_reported;C6TraceLast last[C6T_COUNT][21];} C6Trace;
void c6_trace_init(C6Trace *,C6TraceSink,void *);
void c6_trace_event(C6Trace *,C6TraceEvent,unsigned row,int status,int detail,uint64_t generation,double value);
#endif
