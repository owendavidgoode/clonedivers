/* Corpse-owned Megapack0.2.0 binding. Original code, MIT.
 * Exact identity plus observed thread/lifecycle checks are not locks or runtime
 * safety proof. No callback subscription or unsupported hot reload. */
#ifndef CC_MENU_C6_ADAPTER_H
#define CC_MENU_C6_ADAPTER_H
#include <windows.h>
#include "c6_mapping.h"
#include "c6_guard.h"
#include "c6_target.h"
#include "mom_adapter.h"
#include "menu_trace.h"
static C6Mapping cc_c6_mapping;
static C6Guard cc_c6_guard;
static CcMom020 cc_c6_mom;
static int cc_c6_mom_started;
static unsigned cc_c6_row(const char *id)
{
 static const char *ids[]={"local.faster-corpse-cleanup.Enabled","local.faster-corpse-cleanup.DelaySeconds","local.faster-corpse-cleanup.ExtraCleanup"};
 if(id)for(unsigned i=0;i<3;i++)if(!strcmp(id,ids[i]))return i;
 return 3;
}
static int cc_c6_on_thread(void)
{
 return hf&&hf->read&&!cc_stopped()&&
  (DWORD)InterlockedCompareExchange(&factory_thread,0,0)==GetCurrentThreadId();
}
static int cc_c6_read(void *context,uintptr_t at,void *out,size_t n)
{(void)context;return hf->read(at,out,n);}
static int cc_c6_guard_transport(void *context,const VpmMomTable *table)
{
 (void)context;
 if(!cc_c6_on_thread()||!cc_c6_mapping.module||cc_c6_mapping.closed||
    (uintptr_t)table!=cc_c6_mapping.base+C6_TARGET_TABLE)return 0;
 int state=c6_guard_observe(&cc_c6_guard,cc_c6_read,NULL,cc_c6_mapping.base,
     C6_TARGET_IMAGE_SIZE,GetCurrentThreadId());
 cc_menu_trace(C6T_READY,3,state,cc_c6_guard.latched,cc_c6_guard.generation,0);
 if(state<0)c6_mapping_close(&cc_c6_mapping);
 return state==1;
}
static int cc_c6_selectable(void)
{
 if(!cc_c6_on_thread()||cc_c6_mapping.closed)return 0;
 if(!cc_c6_mapping.module){
  if(cc_c6_mapping.attempted)return 0;
  cc_menu_trace(C6T_ACQUIRE,3,0,0,0,0);
  int ok=c6_mapping_acquire(&cc_c6_mapping,hf,self);
  cc_menu_trace(C6T_ACQUIRE,3,ok,1,0,0);
  if(!ok)return 0;
 }
 if(!cc_c6_mom_started){
  cc_mom020_init(&cc_c6_mom,(const VpmMomTable *)(cc_c6_mapping.base+C6_TARGET_TABLE),
      cc_c6_guard_transport,NULL);
  cc_c6_mom_started=1;
 }
 return 1;
}
static int cc_c6_connect(void)
{return cc_c6_mom_started&&cc_c6_guard_transport(NULL,cc_c6_mom.table);}
static uint64_t cc_c6_epoch(void)
{return cc_c6_guard.latched&&!cc_c6_guard.dead?cc_c6_guard.generation:0;}
static int cc_c6_register(const CrMenuOption *o)
{
 if(!cc_c6_on_thread()||!cc_c6_mom_started)return 0;
 unsigned row=cc_c6_row(o?o->id:NULL);
 cc_menu_trace(C6T_REGISTER_BEGIN,row,0,0,cc_c6_guard.generation,0);
 int ok=cc_mom020_register(&cc_c6_mom,o);
 cc_menu_trace(C6T_REGISTER_END,row,ok,0,cc_c6_guard.generation,0);
 cc_menu_trace(C6T_CAPACITY,3,ok,(int)cc_c6_mom.client.count,cc_c6_guard.generation,C6_CLIENT_ROWS);
 return ok;
}
static int cc_c6_get(const char *id,double *v)
{
 if(!cc_c6_on_thread()||!cc_c6_mom_started)return 0;
 cc_menu_trace(C6T_GET_BEGIN,cc_c6_row(id),0,0,cc_c6_guard.generation,0);
 int ok=cc_mom020_get(&cc_c6_mom,id,v);
 cc_menu_trace(C6T_GET_END,cc_c6_row(id),ok,0,cc_c6_guard.generation,ok&&v?*v:NAN);
 return ok;
}
static int cc_c6_set(const char *id,double v)
{
 if(!cc_c6_on_thread()||!cc_c6_mom_started)return 0;
 cc_menu_trace(C6T_SET_BEGIN,cc_c6_row(id),0,0,cc_c6_guard.generation,v);
 int ok=cc_mom020_set(&cc_c6_mom,id,v);
 cc_menu_trace(C6T_SET_END,cc_c6_row(id),ok,0,cc_c6_guard.generation,v);
 return ok;
}
static const CrMenuApi cc_c6_menu_api={.connect=cc_c6_connect,.epoch=cc_c6_epoch,
 .register_option=cc_c6_register,.get=cc_c6_get,.set=cc_c6_set,.ready=cc_c6_connect};
#endif
