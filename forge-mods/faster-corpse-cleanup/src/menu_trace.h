/* Existing HF session logger only. Called on Corpse's game-thread menu path. MIT. */
#ifndef CC_MENU_TRACE_H
#define CC_MENU_TRACE_H
#include "c6_trace.h"
static C6Trace cc_menu_trace_state;
static int cc_menu_trace_started;
static void cc_menu_trace_sink(void *unused,const char *line)
{(void)unused;if(hf&&hf->log)hf->log(self,"%s",line);}
static void cc_menu_trace(C6TraceEvent event,unsigned row,int status,int detail,uint64_t generation,double value)
{
 if(!cc_menu_trace_started){cc_menu_trace_started=1;c6_trace_init(&cc_menu_trace_state,cc_menu_trace_sink,NULL);}
 c6_trace_event(&cc_menu_trace_state,event,row,status,detail,generation,value);
}
#endif
