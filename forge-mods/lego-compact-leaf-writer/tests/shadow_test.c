#undef NDEBUG
#include <assert.h>
#include "../src/attachment_observer.c"
#include "../src/envelope.c"
static _Alignas(16) unsigned char rawRecords[37][IV_FILE_BYTES];
static unsigned selected,delegateCalls,mathLines,recordLines;
static volatile LONG checks;
static uint32_t inputCsr,returnCsr=0x5f81,inputError=0x2468;
static volatile LONG recurse,blockOnce,blockEnabled;
static HANDLE entered,resumeEvent;
static HfApi fakeApi;
static uintptr_t Word64(const unsigned char *p){uintptr_t v;memcpy(&v,p,8);return v;}
#define CHECK(x) do {if(!(x)){fprintf(stderr,"CHECK failed line%d: %s\n",__LINE__,#x);abort();}InterlockedIncrement(&checks);}while(0)
static void LogFixture(HfMod *mod,const char *fmt,...) {
 (void)mod;char line[2048];va_list args;va_start(args,fmt);vsnprintf(line,sizeof line,fmt,args);va_end(args);
 if(strstr(line," math "))mathLines++;if(strstr(line," record "))recordLines++;
 puts(line);
}
static void ObserveFixture(const se_observe_context *ctx,se_observation_result *out,const void *user) {
 (void)user;memcpy(ctx->payload,rawRecords[selected]+256,IV_PAYLOAD_BYTES);
 SetLastError(999);_mm_setcsr(0x3f80); /* Sampler disturbance must not leak. */
 ShadowCaptured(ctx,0);out->status=0;out->used=IV_PAYLOAD_BYTES;
}
static uint32_t Delegate(void *m,void *p,void *g) {
 const unsigned char *raw=rawRecords[selected];
 CHECK((uintptr_t)m==Word64(raw+72)&&(uintptr_t)p==Word64(raw+80)&&(uintptr_t)g==Word64(raw+88));
 CHECK(_mm_getcsr()==inputCsr&&GetLastError()==inputError);InterlockedIncrement((volatile LONG*)&delegateCalls);
 if(InterlockedCompareExchange(&recurse,0,1)==1) {
  CHECK(se_dispatch(&invocationEnvelope,m,p,g,engine+0x258aab)==0x12345678u);
  /* Inner native result is allowed to affect outer native environment. */
 }
 if(blockEnabled && InterlockedCompareExchange(&blockOnce,1,0)==0) {
  SetEvent(entered);CHECK(WaitForSingleObject(resumeEvent,5000)==WAIT_OBJECT_0);
 }
 SetLastError(0x13579);_mm_setcsr(returnCsr);return 0x12345678u;
}
static void SetupFixture(void) {
 memset(&invocationEnvelope,0,sizeof invocationEnvelope);stopped=invocationFatal=invocationTerminal=0;
 invocationFiles=invocationAccepted=invocationRefused=invocationAbandoned=invocationMask=0;
 shadowRecords=shadowOk=shadowFail=0;logsTotal=logsTick=0;invocationSetupGate=0;
 mathLines=recordLines=delegateCalls=0;recurse=blockEnabled=blockOnce=0;
 engine=0x7ff648df0000ull;fakeApi.log=LogFixture;hf=&fakeApi;invocationPid=GetCurrentProcessId();invocationBirth=1;
 _mm_setcsr(0x9fa0);CHECK(sm_prepare(&shadowRig)==CM_OK&&_mm_getcsr()==0x9fa0);
 CHECK(se_initialize(&invocationEnvelope,ObserveFixture,NULL,56,2)==SE_OK);
 *se_next_slot(&invocationEnvelope)=(void*)Delegate;
 se_target rows[56]={0};unsigned count=0;
 for(unsigned i=0;i<37;i++) {
  const unsigned char *raw=rawRecords[i];uintptr_t mesh=Word64(raw+72);unsigned found=0;
  for(unsigned n=0;n<count;n++)if(rows[n].mesh==mesh)found=1;
  if(found)continue;
  rows[count].mesh=mesh;rows[count].token=U32(raw+28)*8+U32(raw+32);
  memcpy(rows[count].identity+8,raw+64,8);count++;
 }
 CHECK(count==19&&se_publish(&invocationEnvelope,rows,count)==SE_OK);invocationWhitelistCount=count;
}
static void Invoke(void) {
 const unsigned char *raw=rawRecords[selected];SetLastError(inputError);_mm_setcsr(inputCsr);
 uint32_t result=se_dispatch(&invocationEnvelope,(void*)Word64(raw+72),(void*)Word64(raw+80),(void*)Word64(raw+88),engine+0x258aab);
 CHECK(result==0x12345678u&&_mm_getcsr()==returnCsr&&GetLastError()==0x13579);
}
static DWORD WINAPI ThreadInvoke(void *p){
 /* A hash collision is an intended observation bypass, so select a distinct
    bucket before the two-record ownership probe; no runtime policy changes. */
 DWORD excluded=*(const DWORD*)p;
 if((GetCurrentThreadId()&(SE_TICKETS-1u))==excluded)return 77;
 Invoke();return 0;
}
static const sm_result *CheckRecord(se_view *view) {
 CHECK(se_try_take(&invocationEnvelope,view));const se_record *r=view->record;
 const sm_result *math=(const sm_result*)(r->payload+IV_PAYLOAD_BYTES);
 CHECK(math->math_ok&&math->current_status==0&&math->previous_status==0);
 CHECK(math->protected_rows&&math->mesh_rows&&math->header_ok&&math->scope_verified);
 CHECK(math->restored_mxcsr==inputCsr&&r->delegate_mxcsr==inputCsr&&r->return_mxcsr==returnCsr);
 CHECK(!memcmp(r->payload,rawRecords[selected]+256,IV_PAYLOAD_BYTES));return math;
}
int main(int argc,char **argv) {
 CHECK(argc==3);
 for(unsigned i=0;i<37;i++) {
  char path[2048];snprintf(path,sizeof path,"%s/skin-invocation-%06u.bin",argv[1],i+1);
  FILE *f=fopen(path,"rb");CHECK(f&&fread(rawRecords[i],1,IV_FILE_BYTES,f)==IV_FILE_BYTES&&fgetc(f)==EOF);fclose(f);
  CHECK(!memcmp(rawRecords[i],"LGSKIN01",8));
 }
 SetupFixture();wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,argv[2],-1,directory,1100)>0);
 CHECK(InvocationDirectoryInit(directory));
 for(selected=0;selected<37;selected++) {
  inputCsr=U32(rawRecords[selected]+108);Invoke();logsTick=0;CHECK(InvocationDrainOne(NULL));
 }
 CHECK(shadowRecords==37&&shadowOk==37&&!shadowFail&&invocationMask==0x7f&&recordLines==37&&mathLines==37);
 CHECK(!InvocationPending());InvocationTerminalLog(NULL,37,0,"complete",1);
 CHECK(CloseHandle(invocationDirectoryHandle));invocationDirectoryHandle=INVALID_HANDLE_VALUE;
 /* Real ticket recursion bypass still delegates; private tail belongs to outer. */
 SetupFixture();selected=0;inputCsr=0x9fbd;recurse=1;Invoke();CHECK(delegateCalls==2);
 se_view v={0};CheckRecord(&v);CHECK(v.record->overlap_epoch_return>0);CHECK(se_release(&invocationEnvelope,&v));
 /* Deterministic occupied hash bucket: unchanged delegation, no observation. */
 SetupFixture();selected=0;inputCsr=0x9fa0;
 DWORD bucket=GetCurrentThreadId()&(SE_TICKETS-1u);
 InterlockedExchange(&invocationEnvelope.tickets[bucket],(LONG)(GetCurrentThreadId()+SE_TICKETS));
 Invoke();CHECK(delegateCalls==1&&!se_try_take(&invocationEnvelope,&v));
 CHECK(invocationEnvelope.tickets[bucket]==(LONG)(GetCurrentThreadId()+SE_TICKETS));
 InterlockedExchange(&invocationEnvelope.tickets[bucket],0);
 /* Two concurrent same-M observations use two tails; first native delegate
    remains blocked while second math reuses only the gate-owned workspace. */
 for(unsigned stop=0;stop<2;stop++) {
  SetupFixture();selected=0;inputCsr=0x9fb2;blockEnabled=1;
  entered=CreateEventW(NULL,TRUE,FALSE,NULL);resumeEvent=CreateEventW(NULL,TRUE,FALSE,NULL);CHECK(entered&&resumeEvent);
  HANDLE thread=NULL;DWORD threadId=0;
  for(unsigned attempt=0;attempt<64;attempt++) {
   thread=CreateThread(NULL,0,ThreadInvoke,&bucket,0,&threadId);CHECK(thread);
   if((threadId&(SE_TICKETS-1u))!=bucket)break;
   CHECK(WaitForSingleObject(thread,5000)==WAIT_OBJECT_0);DWORD exitCode=0;
   CHECK(GetExitCodeThread(thread,&exitCode)&&exitCode==77);CloseHandle(thread);thread=NULL;
  }
  CHECK(thread&&WaitForSingleObject(entered,5000)==WAIT_OBJECT_0);
  Invoke();const sm_result *second=CheckRecord(&v);cm_render48 expected[128];memcpy(expected,second->current,sizeof expected);
  CHECK(v.record->active_at_entry==2&&se_release(&invocationEnvelope,&v));
  if(stop)se_stop(&invocationEnvelope);
  SetEvent(resumeEvent);CHECK(WaitForSingleObject(thread,5000)==WAIT_OBJECT_0);CheckRecord(&v);
  CHECK(!memcmp(expected,((const sm_result*)(v.record->payload+IV_PAYLOAD_BYTES))->current,sizeof expected));
  CHECK(v.record->abandoned==stop&&se_release(&invocationEnvelope,&v));
  CloseHandle(thread);CloseHandle(entered);CloseHandle(resumeEvent);
 }
 printf("PASS shadow fixture %lu checks: 37 records/74 temporal math, exact forwarding, environment, recursion/concurrent tails/stop\n",(unsigned long)checks);
 return 0;
}
