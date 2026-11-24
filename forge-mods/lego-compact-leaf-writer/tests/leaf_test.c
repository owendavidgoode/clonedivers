#undef NDEBUG
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "../src/writer_core.h"
static gw_state state;static se_state binding;static cm_rig rig;
static _Alignas(16) unsigned char files[37][46400],payload[46144],mesh[0xd0],pool[0x180],graph[56],skin[4096],subrecords[320],binds[3072],poses[2][6144],output[16384];
static volatile LONG checks,outer_calls,leaf_calls,done,block,blocked;static HANDLE resume_event;
static unsigned selected,mode,fault,nest,refuse_capture,refuse_recheck,alias_arrays,zero_counts;static const lp_asset *asset;
static gw_receipt last;static uint32_t entry_csr=0x9fbd,entry_error=0x2468;
static SRWLOCK completion_lock=SRWLOCK_INIT;
static uintptr_t engine=0x7ff648df0000;
static _Thread_local unsigned nested;
void gw_test_fatal_cas_pause(gw_state *s){(void)s;}
#define CHECK(x) do{if(!(x)){fprintf(stderr,"line%d %s\n",__LINE__,#x);abort();}InterlockedIncrement(&checks);}while(0)
static uint32_t u32(const void *p){uint32_t x;memcpy(&x,p,4);return x;}
static uintptr_t ptr(const void *p){uintptr_t x;memcpy(&x,p,8);return x;}
static void set32(void *p,uint32_t v){memcpy(p,&v,4);}static void setptr(void *p,uintptr_t v){memcpy(p,&v,8);}
static int read_fixture(uintptr_t at,void *out,size_t n,void *user){(void)user;
 const struct {const unsigned char *p;size_t n;} areas[]={{mesh,sizeof mesh},{pool,sizeof pool},{graph,sizeof graph},{skin,sizeof skin},{subrecords,sizeof subrecords},{binds,sizeof binds},{(const unsigned char*)poses,sizeof poses}};
 for(unsigned i=0;i<sizeof areas/sizeof areas[0];i++)if(at>=(uintptr_t)areas[i].p&&n<=areas[i].n&&at-(uintptr_t)areas[i].p<=areas[i].n-n){memcpy(out,(void*)at,n);return 1;}
 return 0;
}
static int partial_read(uintptr_t at,void *out,size_t n,void *user){(void)at;(void)user;memset(out,0xaa,n);return 0;}
static void capture(const se_observe_context *ctx,se_observation_result *r,const void *user){(void)user;memcpy(ctx->payload,payload,sizeof payload);r->used=sizeof payload;r->status=refuse_capture?1:0;_mm_setcsr(0x3f80);SetLastError(999);}
static int plan(lp_plan *p,se_record *r,int recheck,void *user){(void)user;
 if(recheck)return !refuse_recheck&&lp_recheck(p,read_fixture,NULL);
 int ok=lp_capture(p,r->payload,(uintptr_t)mesh,(uintptr_t)pool,asset->resource,asset->ordinal,read_fixture,NULL);
 if(ok&&zero_counts)for(unsigned i=0;i<p->groups;i++)p->group[i].count=0; /* Leaf transport domain test, not asset admission. */
 return ok;
}
static void completed(const se_record *r,void *user){(void)user;CHECK(r->call.optional_graph==(mode?(uintptr_t)graph:0));
 CHECK(r->return_mxcsr==0x5f81&&r->return_last_error==0x13579);AcquireSRWLockExclusive(&completion_lock);memcpy(&last,r->payload+GW_RECEIPT_OFFSET,sizeof last);ReleaseSRWLockExclusive(&completion_lock);InterlockedIncrement(&done);}
static uint32_t fake_leaf(void *dst,const void *factor,const void *g,const void *bind,const uint32_t *palette,const uint32_t *map,uint32_t count){
 (void)dst;CHECK(factor&&(bind==binds||fault==5)&&(map==(uint32_t*)(skin+16)||fault==6));CHECK(palette>=((uint32_t*)skin)&&palette<(uint32_t*)(skin+sizeof skin));
 CHECK(_mm_getcsr()==0x9fa1&&GetLastError()==0x7799);CHECK(g!=NULL);InterlockedIncrement(&leaf_calls);
 SetLastError(0x5544);_mm_setcsr(0x7f85);return count;
}
static uint32_t changed_leaf(void *d,const void*f,const void*g,const void*b,const uint32_t*p,const uint32_t*m,uint32_t n){return fake_leaf(d,f,g,b,p,m,n);}
static void invoke(void);
static uint32_t fake_outer(void *m,void *p,void *g){
 CHECK(m==mesh&&p==pool&&g==(mode?(void*)graph:NULL));CHECK(_mm_getcsr()==entry_csr&&GetLastError()==entry_error);InterlockedIncrement(&outer_calls);
 if(!g)InterlockedExchange((volatile LONG*)(mesh+0xc4),0x10); /* Native-style early publication, before any leaf. */
 if(nest&&!nested){nested=1;unsigned old=refuse_capture;refuse_capture=1;invoke();refuse_capture=old;nested=0;}
 _Alignas(16) unsigned char factor[96]={0};uintptr_t directory=(uintptr_t)skin+u32(skin+12);unsigned total=asset->groups*2;
 if(fault==8)total--;if(fault==9)total++;
 for(unsigned at=0;at<total;at++){
  unsigned j=(at/2)%asset->groups,t=at%2,q=u32(subrecords+j*40+24),n=u32((void*)(directory+8+8*q));
  uintptr_t pal=directory+u32((void*)(directory+4+8*q));uintptr_t dest=(uintptr_t)output+256+(uint64_t)u32(subrecords+j*40+28+4*t)/3*48;
  uintptr_t source=(uintptr_t)poses[alias_arrays?0:t],caller=engine+(t?0x513054:0x512fea);const void *b=binds;const uint32_t *map=(uint32_t*)(skin+16);
  if(zero_counts)n=0;
  if(at==0){if(fault==1)source+=48;if(fault==2)pal+=4;if(fault==3)n++;if(fault==4)caller++;if(fault==5)b=binds+48;if(fault==6)map++;
   if(fault==7)dest+=0x100000000ull;if(fault==10)dest=0x800000000000ull;}
  if(fault==11&&at==0)state.leaf_next=(void*)changed_leaf;
  _mm_setcsr(0x9fa1);SetLastError(0x7799);
  uint32_t result=gw_leaf_dispatch(&state,(void*)dest,factor+48*t,(void*)source,b,(uint32_t*)pal,map,n,caller);
  CHECK(result==n&&_mm_getcsr()==0x7f85&&GetLastError()==0x5544);
  if(!g)CHECK(InterlockedCompareExchange((volatile LONG*)(mesh+0xc4),0,0)==0x10);
 }
 if(block){InterlockedIncrement(&blocked);CHECK(WaitForSingleObject(resume_event,10000)==WAIT_OBJECT_0);}
 SetLastError(0x13579);_mm_setcsr(0x5f81);return 0x10;
}
static void invoke(void){_mm_setcsr(entry_csr);SetLastError(entry_error);uint32_t result=gw_dispatch(&state,mesh,pool,mode?graph:NULL,engine+(mode?0x258aab:0x258e19));
 CHECK(result==0x10&&_mm_getcsr()==0x5f81&&GetLastError()==0x13579);}
static void setup(unsigned index,unsigned grouped){
 selected=index;mode=grouped;fault=nest=refuse_capture=refuse_recheck=block=blocked=alias_arrays=zero_counts=0;outer_calls=leaf_calls=done=0;memset(&last,0,sizeof last);
 memset(&state,0,sizeof state);memset(&binding,0,sizeof binding);memcpy(payload,files[index]+256,sizeof payload);
 unsigned target=u32(files[index]+28),ordinal=u32(files[index]+32);uint64_t resource=ptr(files[index]+64);asset=lp_asset_find(resource,ordinal);CHECK(asset);
 memset(mesh,0,sizeof mesh);memset(pool,0,sizeof pool);memset(skin,0,sizeof skin);memset(subrecords,0,sizeof subrecords);
 unsigned count=u32(payload+184),node=u32(payload+112);memcpy(poses[0],payload+1024,6144);memcpy(poses[1],payload+7168,6144);
 memcpy(graph,payload+256,56);setptr(graph+24,(uintptr_t)poses[0]);setptr(graph+48,(uintptr_t)poses[1]);
 set32(mesh+0x30,node);setptr(mesh+0x38,(uintptr_t)graph);setptr(mesh+0x70,(uintptr_t)skin);setptr(mesh+0x80,(uintptr_t)binds);set32(mesh+0x90,asset->groups);setptr(mesh+0x98,(uintptr_t)subrecords);
 set32(skin,asset->bones);set32(skin+8,16);unsigned directory=16+4*asset->bones;set32(skin+12,directory);set32(skin+directory,asset->groups);memcpy(skin+16,asset->map,4*asset->bones);memcpy(binds,asset->bind,48*asset->bones);
 unsigned cursor=4+8*asset->groups,out=0;
 for(unsigned i=0;i<asset->groups;i++){set32(skin+directory+4+8*i,cursor);set32(skin+directory+8+8*i,asset->counts[i]);memcpy(skin+directory+cursor,asset->palette[i],4*asset->counts[i]);cursor+=4*asset->counts[i];
  set32(subrecords+40*i+24,i);set32(subrecords+40*i+28,out*3);out+=asset->counts[i];set32(subrecords+40*i+32,out*3);out+=asset->counts[i];}
 set32(mesh+0xc8,out);setptr(pool+0x170,(uintptr_t)output);
 setptr(payload+120,(uintptr_t)graph);setptr(payload+152,(uintptr_t)graph);setptr(payload+144,(uintptr_t)pool-0x318);
 setptr(payload+200,(uintptr_t)poses[0]);setptr(payload+208,(uintptr_t)poses[1]);memcpy(payload+256,graph,56);memcpy(payload+312,graph,56);
 memcpy(payload+480,mesh+0x70,96);memcpy(payload+576,mesh+0x70,96);memcpy(payload+672,pool+0x170,16);memcpy(payload+688,pool+0x170,16);
 CHECK(count==u32(graph+16));_mm_setcsr(0x1f80);CHECK(sm_prepare(&rig)==CM_OK);CHECK(se_initialize(&binding,capture,NULL,56,2)==SE_OK);
 se_target one={0};one.mesh=(uintptr_t)mesh;one.token=target*8+ordinal;memcpy(one.identity+8,&resource,8);setptr(one.identity+56,(uintptr_t)graph);CHECK(se_publish(&binding,&one,1)==SE_OK);
 *se_next_slot(&binding)=(void*)fake_outer;gw_initialize(&state,&binding,&rig,engine,capture,NULL,completed,NULL);state.approved_next=(void*)fake_outer;state.leaf_next=state.approved_leaf_next=(void*)fake_leaf;state.plan_capture=plan;state.ready=1;
}
static DWORD WINAPI worker(void *p){(void)p;invoke();return 0;}
int main(int argc,char **argv){CHECK(argc==2);for(unsigned i=0;i<37;i++){char path[2048];snprintf(path,sizeof path,"%s/skin-invocation-%06u.bin",argv[1],i+1);FILE *f=fopen(path,"rb");CHECK(f&&fread(files[i],1,46400,f)==46400&&fgetc(f)==EOF);fclose(f);}
 for(unsigned i=0;i<37;i++)for(unsigned route=0;route<2;route++){setup(i,route);invoke();CHECK(state.corrected==1&&!state.failed&&!state.fatal&&last.expected==asset->groups*2&&last.replaced==last.expected&&last.allocation_ok);}
 for(unsigned bad=1;bad<=11;bad++){setup(0,0);fault=bad;invoke();CHECK(state.fatal&&state.failed==1&&!state.corrected);}
 setup(0,1);refuse_recheck=1;invoke();CHECK(state.fatal==GW_RECHECK&&!state.corrected);
 setup(0,0);alias_arrays=1;setptr(graph+48,(uintptr_t)poses[0]);memcpy(payload+256,graph,56);memcpy(payload+7168,poses[0],6144);invoke();CHECK(state.corrected==1&&!state.fatal&&last.seen==last.expected);
 setup(0,1);zero_counts=1;invoke();CHECK(state.corrected==1&&!state.fatal&&last.seen==last.expected&&leaf_calls==(LONG)last.expected);
 setup(0,0);set32(graph+40,u32(graph+16)-1);memcpy(payload+256,graph,56);set32(payload+188,u32(graph+40));invoke();CHECK(state.failed==1&&!state.corrected);
 setup(0,0);invoke();refuse_capture=1;invoke();CHECK(last.expected==0&&last.plan_blocks==0&&last.plan_bytes==0&&state.corrected==1&&state.failed==1);
 setup(0,0);nest=1;invoke();CHECK(outer_calls==2&&state.corrected==1&&state.failed==1); /* nested refused call cannot borrow ancestor */
 setup(0,1);state.diagnostics_closed=state.teardown=1;se_stop(&binding);for(unsigned i=0;i<100;i++)invoke();CHECK(state.corrected==100&&!state.fatal);
 setup(0,0);block=1;resume_event=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(resume_event);HANDLE threads[13];
 for(unsigned i=0;i<13;i++){threads[i]=CreateThread(NULL,0,worker,NULL,0,NULL);CHECK(threads[i]);}
 for(unsigned i=0;i<10000&&blocked!=13;i++)Sleep(1);CHECK(blocked==13);SetEvent(resume_event);CHECK(WaitForMultipleObjects(13,threads,TRUE,10000)==WAIT_OBJECT_0);
 for(unsigned i=0;i<13;i++)CloseHandle(threads[i]);CloseHandle(resume_event);CHECK(state.corrected==13&&!state.failed&&!state.fatal&&!state.inflight);
 setup(0,1);block=1;resume_event=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(resume_event);HANDLE full[32];
 for(unsigned i=0;i<32;i++){full[i]=CreateThread(NULL,0,worker,NULL,0,NULL);CHECK(full[i]);}
 for(unsigned i=0;i<10000&&blocked!=32;i++)Sleep(1);CHECK(blocked==32);block=0;invoke();CHECK(state.fatal==GW_CAPACITY&&state.failed==1&&state.inflight==32);SetEvent(resume_event);
 CHECK(WaitForMultipleObjects(32,full,TRUE,10000)==WAIT_OBJECT_0);for(unsigned i=0;i<32;i++)CloseHandle(full[i]);CloseHandle(resume_event);CHECK(state.corrected==32&&!state.inflight);
 /* Off-context leaf retains every supplied argument and returned environment. */
 setup(0,0);_Alignas(16) unsigned char factor[48]={0};unsigned dir=u32(skin+12);_mm_setcsr(0x9fa1);SetLastError(0x7799);
 CHECK(gw_leaf_dispatch(&state,output,factor,poses[0],binds,(uint32_t*)(skin+dir+u32(skin+dir+4)),(uint32_t*)(skin+16),0,engine+0x512fea)==0);
 CHECK(!state.corrected&&!state.fatal&&_mm_getcsr()==0x7f85&&GetLastError()==0x5544);
 /* Direct plan corruption and same retained read witnesses. */
 setup(0,0);lp_plan p;CHECK(lp_capture(&p,payload,(uintptr_t)mesh,(uintptr_t)pool,asset->resource,asset->ordinal,read_fixture,NULL));CHECK(lp_recheck(&p,read_fixture,NULL));
 skin[16]^=1;CHECK(!lp_recheck(&p,read_fixture,NULL));CHECK(!lp_capture(&p,payload,(uintptr_t)mesh,(uintptr_t)pool,asset->resource,asset->ordinal,read_fixture,NULL));
 CHECK(p.trace.phase==1&&p.trace.stage==15&&p.trace.error==3&&p.trace.read_outcome==1&&p.trace.mismatch_index==0&&p.trace.probe_bytes);
 CHECK(p.trace.expected[0]!=p.trace.observed[0]);
 setup(0,0);CHECK(!lp_capture(&p,payload,(uintptr_t)mesh,(uintptr_t)pool,asset->resource,asset->ordinal,partial_read,NULL));
 CHECK(p.trace.stage==3&&p.trace.error==2&&p.trace.read_outcome==2&&!p.blocks&&!p.bytes);
 for(unsigned i=0;i<LP_BYTES;i++)CHECK(!p.evidence[i]);
 /* First fatal decision owns evidence even if its delegate returns last. */
 setup(0,0);refuse_capture=1;block=1;state.diagnostics_closed=1;resume_event=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(resume_event);
 HANDLE first=CreateThread(NULL,0,worker,NULL,0,NULL);CHECK(first);
 for(unsigned i=0;i<10000&&!blocked;i++)Sleep(1);CHECK(blocked==1);
 CHECK(state.fault.state==2&&state.fault.reason==GW_CAPTURE&&state.fault.sequence==1&&state.fault.record_owned);
 block=0;invoke();CHECK(state.fault.state==2&&state.fault.sequence==1&&done==1); /* still pending; no theft */
 SetEvent(resume_event);CHECK(WaitForSingleObject(first,10000)==WAIT_OBJECT_0);CloseHandle(first);CloseHandle(resume_event);
 CHECK(state.fault.state==4&&state.fault.record.sequence==1&&state.fault.sampler_status==1&&state.fault.sampler_used==SM_RAW_BYTES);
 CHECK(state.fault.record.result==0x10&&state.fault.record.return_mxcsr==0x5f81&&state.fault.record.return_last_error==0x13579);
 invoke();CHECK(state.fault.state==4&&state.fault.record.sequence==1); /* resident immutable, no quota effect */
 setup(0,0);for(unsigned i=0;i<GW_SLOTS;i++)state.slots[i].state=1;invoke();
 CHECK(state.fault.state==2&&state.fault.reason==GW_CAPACITY&&!state.fault.record_owned&&state.fault.sequence==1);
 for(unsigned i=0;i<GW_SLOTS;i++)state.slots[i].state=0;refuse_capture=1;invoke();CHECK(state.fault.state==2&&!state.fault.record_owned);
 setup(0,0);skin[16]^=1;invoke();CHECK(state.fault.state==4&&state.fault.record.capture_status==-1002&&!state.fault.sampler_status&&state.fault.plan.trace.stage==15);
 /* Regression test for Clonedivers 33bf1c45 leaf-fault-first.bin: valid near-zero float 8.3859653e-38f
  * producing ~1.274e-57 double residuals and subnormal products must flush cleanly to 0.0f without CM_NUMERICAL. */
 {setup(0,0);float tiny=8.3859653e-38f,sub=1.0e-40f;
  cm_render48 *p0=(cm_render48*)(poses[0]),*p1=(cm_render48*)(poses[1]);
  /* Perturb parent root (payload+13312+100*48) off-axis zero entry with 8.3859653e-38f while keeping rigid basis within tolerance. */
  cm_render48 *par0=(cm_render48*)(payload+13312),*par1=(cm_render48*)(payload+18448);
  par0[100].v[2]=tiny;par1[100].v[2]=tiny;
  p0[0].v[2]=sub;p1[0].v[2]=sub;
  invoke();CHECK(state.corrected==1&&!state.failed&&!state.fatal);
  cm_render48 r_out;cm_render48 r_a={{1.f,0.f,tiny,-0.00914f, 0.f,1.f,0.f,0.f, 0.f,0.f,1.f,0.f}};
  cm_render48 r_b={{1.f,0.f,0.f,0.f, 0.f,1.f,0.f,0.f, -0.00914f,0.f,1.f,0.f}};
  _mm_setcsr(0x1f80);CHECK(cm_compose(&r_a,&r_b,&r_out)==CM_OK);}
 printf("leaf_checks=%ld retained_calls=74 native_game_calls=0\n",checks);return 0;
}
