/* Fixed-field bounded client trace. No provider text, paths or addresses. MIT. */
#include "c6_trace.h"
#include <stdio.h>
#include <string.h>
#include <math.h>
static const char *const names[C6T_COUNT]={"select","acquire","ready","register_begin","register_end","get_begin","get_end","set_begin","set_end","epoch","poll","apply","resync","save_failure","capacity"};
static void increment(uint32_t *n){if(*n<UINT32_MAX)++*n;}
void c6_trace_init(C6Trace *t,C6TraceSink sink,void *context)
{
 if(!t)return;
 memset(t,0,sizeof *t);t->sink=sink;t->context=context;
 if(sink)sink(context,"CC-MENU-EXP version=1.0.6 session=1 binary_derived_contract=STATIC_REVIEWED runtime=UNVERIFIED target=megapack-0.2.0 expected_sha256=6205b8c5699b9f07118b334cc4ee3e053a3023c9e973f94de1db18c68b31f02f event_limit=1024 regular_limit=960 critical_limit=64 summary_limit=16");
}
/* Ordinary repetition cannot consume critical slots or the two independent
 * capacity notices. No guarantee of infinite capture: exhaustion is explicit. */
static int critical_event(C6TraceEvent e,int status,int detail,double value)
{
 return e==C6T_SAVE_FAILURE||(e==C6T_READY&&status<0)||
   (e==C6T_ACQUIRE&&status==0&&detail==1)||
   ((e==C6T_REGISTER_END||e==C6T_GET_END||e==C6T_SET_END||e==C6T_RESYNC||e==C6T_APPLY||e==C6T_CAPACITY)&&status<=0)||
   (e==C6T_GET_END&&!isfinite(value))||(e==C6T_POLL&&status!=1);
}
void c6_trace_event(C6Trace *t,C6TraceEvent event,unsigned row,int status,int detail,uint64_t generation,double value)
{
 if(!t||!t->sink||(unsigned)event>=C6T_COUNT||row>20)return;
 increment(&t->sequence);
 C6TraceLast *last=&t->last[event][row];
 int same=last->seen&&last->status==status&&last->detail==detail&&last->generation==generation&&
   (last->value==value||(!isfinite(last->value)&&!isfinite(value)));
 *last=(C6TraceLast){value,generation,status,detail,1};
 char line[384];
 int critical=critical_event(event,status,detail,value);
 uint32_t *emitted=critical?&t->critical_emitted:&t->regular_emitted;
 unsigned *reported=critical?&t->critical_cap_reported:&t->regular_cap_reported;
 uint32_t limit=critical?C6_TRACE_CRITICAL_LIMIT:C6_TRACE_REGULAR_LIMIT;
 int capped=*emitted>=limit;
 if(same||capped){
  increment(&t->dropped);
  if(capped&&!*reported){
   *reported=1;++t->summaries;
   snprintf(line,sizeof line,"CC-MENU-EXP session=1 seq=%u event=trace_capacity emitted=%u dropped=%u capped=1 regular_capped=%u critical_capped=%u capture=%s",t->sequence,t->emitted,t->dropped,t->regular_cap_reported,t->critical_cap_reported,t->regular_cap_reported&&t->critical_cap_reported?"stopped":"partial");
   t->sink(t->context,line);
  }else if(t->periodic_summaries<C6_TRACE_PERIODIC_SUMMARIES&&t->dropped%128u==0){
   ++t->periodic_summaries;++t->summaries;
   snprintf(line,sizeof line,"CC-MENU-EXP session=1 seq=%u event=trace_summary emitted=%u dropped=%u capped=%u",t->sequence,t->emitted,t->dropped,t->regular_cap_reported||t->critical_cap_reported);
   t->sink(t->context,line);
  }
  return;
 }
 ++*emitted;++t->emitted;char number[40];
 if(isfinite(value))snprintf(number,sizeof number,"%.17g",value);else strcpy(number,"unavailable");
 snprintf(line,sizeof line,"CC-MENU-EXP session=1 seq=%u event=%s row=%u status=%d detail=%d generation=%llu value=%s dropped=%u",t->sequence,names[event],row,status,detail,(unsigned long long)generation,number,t->dropped);
 t->sink(t->context,line);
}
