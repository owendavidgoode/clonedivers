#undef NDEBUG
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <intrin.h>
#include "../src/writer_core.h"
static gw_state state;static se_state binding;static cm_rig rig;
static _Alignas(16) unsigned char raw[37][46400];
static volatile LONG checks,delegates,captures,capturing,maximumCapture,completed,recurse,block,blocked;
static volatile LONG expectOriginal,failCapture;
static HANDLE resumeNative;
static unsigned selected;
static uint32_t csr=0x9fbd,error=0x2468;
void gw_test_fatal_cas_pause(gw_state *s){(void)s;}
static uintptr_t word(const void *p){uintptr_t v;memcpy(&v,p,8);return v;}
static uint32_t u32(const void *p){uint32_t v;memcpy(&v,p,4);return v;}
#define CHECK(x) do{if(!(x)){fprintf(stderr,"line%d: %s\n",__LINE__,#x);abort();}InterlockedIncrement(&checks);}while(0)
static void capture(const se_observe_context *ctx,se_observation_result *result,const void *user){
 (void)user;CHECK(InterlockedIncrement(&capturing)==1);InterlockedIncrement(&captures);
 maximumCapture=1;memcpy(ctx->payload,raw[selected]+256,46144);result->used=46144;result->status=failCapture?1:0;
 SetLastError(666);_mm_setcsr(0x3f80);Sleep(1);InterlockedDecrement(&capturing);
}
static void completion(const se_record *record,void *user){
 (void)user;CHECK(record->delegate_mxcsr==csr && record->return_mxcsr==0x5f81 && record->return_last_error==0x13579);
 CHECK(record->result==0x12345678);InterlockedIncrement(&completed);
}
static void invoke(void);
static uint32_t delegate(void *mesh,void *pool,void *graph){
 CHECK((uintptr_t)mesh==word(raw[selected]+72)&&(uintptr_t)pool==word(raw[selected]+80));
 CHECK(_mm_getcsr()==csr&&GetLastError()==error);InterlockedIncrement(&delegates);
 if(expectOriginal)CHECK((uintptr_t)graph==word(raw[selected]+88));
 else {
  CHECK((uintptr_t)graph!=word(raw[selected]+88));
  unsigned count=u32(raw[selected]+256+184),node=u32(raw[selected]+256+112);
  CHECK(u32((unsigned char*)graph+16)==count&&u32((unsigned char*)graph+40)==count);
  const unsigned char *now=(const unsigned char*)word((unsigned char*)graph+24),*previous=(const unsigned char*)word((unsigned char*)graph+48);
  CHECK(now!=previous && !memcmp(now+node*48,raw[selected]+256+1024+node*48,48));
  CHECK(!memcmp(previous+node*48,raw[selected]+256+7168+node*48,48));
  unsigned char before[48];memcpy(before,now+node*48,48);
  if(InterlockedCompareExchange(&recurse,0,1)==1)invoke();
  if(block){InterlockedIncrement(&blocked);CHECK(WaitForSingleObject(resumeNative,10000)==WAIT_OBJECT_0);}
  CHECK(!memcmp(before,now+node*48,48));
 }
 SetLastError(0x13579);_mm_setcsr(0x5f81);return 0x12345678;
}
static void invoke(void){
 SetLastError(error);_mm_setcsr(csr);
 uint32_t result=gw_dispatch(&state,(void*)word(raw[selected]+72),(void*)word(raw[selected]+80),(void*)word(raw[selected]+88),0x7ff649048aab);
 CHECK(result==0x12345678&&_mm_getcsr()==0x5f81&&GetLastError()==0x13579);
}
static uint32_t changed_delegate(void *m,void *p,void *g){return delegate(m,p,g);}
static DWORD WINAPI thread(void *arg){(void)arg;invoke();return 0;}
static void setup(void){
 memset(&state,0,sizeof state);memset(&binding,0,sizeof binding);delegates=captures=capturing=maximumCapture=completed=0;
 recurse=block=blocked=expectOriginal=failCapture=0;
 _mm_setcsr(0x1f80);CHECK(sm_prepare(&rig)==CM_OK);CHECK(se_initialize(&binding,capture,NULL,56,2)==SE_OK);
 *se_next_slot(&binding)=(void*)delegate;se_target targets[56]={0};unsigned count=0;
 for(unsigned i=0;i<37;i++){
  uintptr_t mesh=word(raw[i]+72);unsigned found=0;for(unsigned j=0;j<count;j++)if(targets[j].mesh==mesh)found=1;if(found)continue;
  targets[count].mesh=mesh;targets[count].token=u32(raw[i]+28)*8+u32(raw[i]+32);memcpy(targets[count].identity+8,raw[i]+64,8);
  memcpy(targets[count].identity+56,raw[i]+88,8);count++;
 }
 CHECK(count==19&&se_publish(&binding,targets,count)==SE_OK);
 gw_initialize(&state,&binding,&rig,0x7ff648df0000,capture,NULL,completion,NULL);state.approved_next=(void*)delegate;InterlockedExchange(&state.ready,1);
}
static void wait_blocked(unsigned count){for(unsigned n=0;n<10000&&blocked!=(LONG)count;n++)Sleep(1);CHECK(blocked==(LONG)count);}
int main(int argc,char **argv){
 CHECK(argc==2);
 for(unsigned i=0;i<37;i++){char path[2048];snprintf(path,sizeof path,"%s/skin-invocation-%06u.bin",argv[1],i+1);FILE *f=fopen(path,"rb");
  CHECK(f&&fread(raw[i],1,46400,f)==46400&&fgetc(f)==EOF);fclose(f);}
 setup();for(selected=0;selected<37;selected++)invoke();CHECK(state.corrected==37&&!state.fatal&&completed==37);
 /* No correction quotas, diagnostic closure or teardown switch to pass-through. */
 selected=0;InterlockedExchange(&state.diagnostics_closed,1);InterlockedExchange(&state.teardown,1);se_stop(&binding);
 for(unsigned i=0;i<200;i++)invoke();CHECK(state.corrected==237&&!state.failed&&!state.fatal);
 setup();selected=0;recurse=1;invoke();CHECK(state.corrected==2&&state.peak==2&&!state.fatal);
 /* Ordinary 13-thread metadata contention: all private outputs remain alive. */
 setup();block=1;resumeNative=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(resumeNative);HANDLE threads[32];
 for(unsigned i=0;i<13;i++){threads[i]=CreateThread(NULL,0,thread,NULL,0,NULL);CHECK(threads[i]);}
 wait_blocked(13);CHECK(state.inflight==13&&!state.fatal);SetEvent(resumeNative);
 CHECK(WaitForMultipleObjects(13,threads,TRUE,10000)==WAIT_OBJECT_0);
 for(unsigned i=0;i<13;i++)CloseHandle(threads[i]);CloseHandle(resumeNative);CHECK(state.corrected==13&&!state.failed&&!state.inflight);
 /* Capacity is a real failure; existing delegates retain their storage. */
 setup();block=1;resumeNative=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(resumeNative);
 for(unsigned i=0;i<32;i++){threads[i]=CreateThread(NULL,0,thread,NULL,0,NULL);CHECK(threads[i]);}
 wait_blocked(32);expectOriginal=1;invoke();expectOriginal=0;CHECK(state.fatal==GW_CAPACITY&&state.failed==1&&state.inflight==32);
 SetEvent(resumeNative);CHECK(WaitForMultipleObjects(32,threads,TRUE,10000)==WAIT_OBJECT_0);
 for(unsigned i=0;i<32;i++)CloseHandle(threads[i]);CloseHandle(resumeNative);block=0;invoke();CHECK(state.corrected==33&&state.fatal==GW_CAPACITY&&!state.inflight);
 setup();failCapture=expectOriginal=1;invoke();CHECK(state.fatal==GW_CAPTURE&&state.failed==1);failCapture=expectOriginal=0;invoke();CHECK(state.corrected==1&&state.fatal==GW_CAPTURE);
 setup();InterlockedExchange(&state.ready,0);expectOriginal=1;invoke();CHECK(state.warmup==1&&!state.matched&&!state.fatal);
 setup();InterlockedExchangePointer(&binding.next,(void*)changed_delegate);expectOriginal=1;invoke();
 CHECK(state.fatal==GW_CHAIN&&state.failed==1);InterlockedExchangePointer(&binding.next,(void*)delegate);expectOriginal=0;invoke();CHECK(state.corrected==1&&state.fatal==GW_CHAIN);
 printf("PASS %ld writer checks: retained37/74,237continuous,13parallel,32capacity,recursion,fatalcontinuation,environment\n",checks);return 0;
}
