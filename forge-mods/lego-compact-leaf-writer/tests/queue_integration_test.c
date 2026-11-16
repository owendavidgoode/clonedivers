/* Own-process sparse metadata only; no hooks or native game calls. */
#define LEAF_MAIN queue_parent_fixture_main
#include "leaf_integration_test.c"
static Region *replacementQueue;
static unsigned queueMode;
static char queueAck[900],queueFatal[900];
static int QueueRead(uintptr_t at,void *out,size_t bytes){
 int rc=ReadMock(at,out,bytes);if(rc||!writerCaptureActive||!invocationBudget||!samplerTrace.attempted)return rc;
 if(at==rm->address+0x10&&bytes==80)memcpy(out,&replacementQueue->address,8);
 if(queueMode==2&&at==replacementQueue->address&&bytes)((unsigned char*)out)[0]^=1;
 if(queueMode==3&&at==descriptors[activeTarget][1]->address+0xd8&&bytes==4)((unsigned char*)out)[0]^=8;
 if(queueMode==4&&samplerTrace.success&&at==gameModule+0x3326468){memset(out,0xaa,bytes);return 1;}
 return 0;
}
static void QueueLog(HfMod *m,const char *fmt,...){
 char text[900];va_list args;va_start(args,fmt);int n=vsnprintf(text,sizeof text,fmt,args);va_end(args);CHECK(n>=0&&n<900);Log(m,"%s",text);
 if(strstr(text," fault_evidence "))memcpy(queueAck,text,(size_t)n+1);
 if(strstr(text," fatal "))memcpy(queueFatal,text,(size_t)n+1);
}
static void Strict(uint32_t index,const SavedTarget *saved,const GroupMeta *group,const Record *record,const RenderMeta *before,const RenderMeta *after){
 InvocationClearFailures();CHECK(!CaptureRenderClose(index,saved,group,record,before,after,4));
 CHECK(consistencyRefusal.pending&&consistencyRefusal.mask==2&&!consistencyRefusal.ignored&&consistencyRefusal.offset==9);
}
static void Comparator(const char *faultPath){
 SetActive(4,2,0);se_record r={0};for(unsigned i=0;i<invocationEnvelope.target_count;i++)if(invocationEnvelope.targets[i].mesh==activeMesh)r.target=invocationEnvelope.targets[i];
 r.call=(se_call){activeMesh,activePool,0,engine+0x258e19,GetCurrentThreadId(),0x1f80,0};
 se_observe_context ctx={&invocationEnvelope,&r.target,&r.call,r.payload,SE_PAYLOAD};IvBudget budget={0};budget.call=&ctx;
 SavedTarget *saved=&targets[4];Record record={0};record.child=saved->child;record.parent=saved->parent;unsigned index=saved->child.id.index;
 RenderMeta before=saved->render,after=before;after.header.queue^=0x100;
 writerCaptureActive=1;invocationBudget=&budget;CHECK(CaptureQueueScope(index,saved,NULL));
 CHECK(CaptureRenderClose(index,saved,NULL,&record,&before,&after,4));
 /* Every ignored bit succeeds in scope; every other byte plus Q refuses. */
 for(unsigned byte=8;byte<16;byte++)for(unsigned bit=0;bit<8;bit++){after=before;((unsigned char*)&after)[byte]^=(unsigned char)(1u<<bit);InvocationClearFailures();CHECK(CaptureRenderClose(index,saved,NULL,&record,&before,&after,4));CHECK(!consistencyRefusal.pending);}
 for(unsigned byte=0;byte<sizeof(RenderMeta);byte++)if(byte<8||byte>=16)for(unsigned bit=0;bit<8;bit++){
  after=before;after.header.queue^=0x100;((unsigned char*)&after)[byte]^=(unsigned char)(1u<<bit);RenderMeta preservedBefore=before,preservedAfter=after;
  InvocationClearFailures();CHECK(!CaptureRenderClose(index,saved,NULL,&record,&before,&after,4));
  CHECK(consistencyRefusal.pending&&consistencyRefusal.mask&&!(consistencyRefusal.mask&2)&&consistencyRefusal.ignored==2);
  CHECK(consistencyRefusal.offset==byte&&consistencyRefusal.differences==1&&consistencyRefusal.width==(sizeof(RenderMeta)-byte<8?sizeof(RenderMeta)-byte:8));
  CHECK(consistencyRefusal.oldHeader.queue==before.header.queue&&consistencyRefusal.newHeader.queue==after.header.queue);
  CHECK(!memcmp(&before,&preservedBefore,sizeof before)&&!memcmp(&after,&preservedAfter,sizeof after));
 }
 after=before;after.header.queue^=0x100;GroupMeta group={0};
 writerCaptureActive=0;Strict(index,saved,NULL,&record,&before,&after);writerCaptureActive=1;
 Strict(index,saved,&group,&record,&before,&after);Strict(index,NULL,NULL,&record,&before,&after);
 invocationBudget=NULL;Strict(index,saved,NULL,&record,&before,&after);invocationBudget=&budget;
 budget.call=NULL;Strict(index,saved,NULL,&record,&before,&after);budget.call=&ctx;
 ctx.target=NULL;Strict(index,saved,NULL,&record,&before,&after);ctx.target=&r.target;
 ctx.call=NULL;Strict(index,saved,NULL,&record,&before,&after);ctx.call=&r.call;
 uint64_t token=r.target.token;r.target.token=56;Strict(index,saved,NULL,&record,&before,&after);r.target.token=token;
 r.target.token=4*8+7;Strict(index,saved,NULL,&record,&before,&after);r.target.token=token;
 Strict(index,&targets[3],NULL,&record,&before,&after);Strict(index+1,saved,NULL,&record,&before,&after);
 SavedTarget copied=*saved;Strict(index,&copied,NULL,&record,&before,&after);
 uint32_t registered=saved->render.mesh[2].registered;saved->render.mesh[2].registered=0;Strict(index,saved,NULL,&record,&before,&after);saved->render.mesh[2].registered=registered;
 r.call.mesh++;Strict(index,saved,NULL,&record,&before,&after);r.call.mesh--;
 r.target.mesh++;Strict(index,saved,NULL,&record,&before,&after);r.target.mesh--;
 for(unsigned j=0;j<3;j++){unsigned at=j==0?0:j==1?8:36;r.target.identity[at]^=1;Strict(index,saved,NULL,&record,&before,&after);r.target.identity[at]^=1;}
 /* Actual retained operands replay through this pure post-read comparator.
    Context is synthetic qualified scope, not a new live identity assertion. */
 FILE *f=fopen(faultPath,"rb");CHECK(f);static unsigned char fault[81920];CHECK(fread(fault,1,sizeof fault,f)==sizeof fault&&fgetc(f)==EOF);CHECK(!fclose(f));
 CHECK(!memcmp(fault,"LGFAULT3",8)&&U32(fault+73728+28)==1);
 memcpy(&before,fault+73728+256,sizeof before);memcpy(&after,fault+73728+1280,sizeof after);
 CHECK(before.header.queue!=after.header.queue&&RenderDiff(&before,&after)==2);
 InvocationClearFailures();CHECK(CaptureRenderClose(index,saved,NULL,&record,&before,&after,4));CHECK(!consistencyRefusal.pending);
 writerCaptureActive=0;invocationBudget=NULL;
}
static void CaptureCase(const char *base,const char *name,unsigned mode,unsigned grouped){
 char path[2048];snprintf(path,sizeof path,"%s/%s",base,name);wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,directory,1100)>0);CHECK(CreateDirectoryW(directory,NULL));CHECK(InvocationDirectoryInit(directory));
 memset(&writer.fault,0,sizeof writer.fault);writer.fatal=0;writer.first_failure_sequence=0;writerReportedFatal=0;writerFaultPendingReported=0;queueAck[0]=queueFatal[0]=0;
 SetActive(4,2,grouped);api.read=QueueRead;api.log=QueueLog;queueMode=mode;LONG64 corrected=writer.corrected;
 _mm_setcsr(activeCsr);SetLastError(0x2468);CHECK(gw_dispatch(&writer,(void*)activeMesh,(void*)activePool,grouped?(void*)activeGraph:NULL,engine+(grouped?0x258aab:0x258e19))==1234);
 CHECK(_mm_getcsr()==0x5f81&&GetLastError()==0x13579);
 const unsigned char *w=mode==1?samplerWitness:writer.fault.sampler;
 CHECK(U32(w+24)==1&&U32(w+32)==1&&U64(w+256+8)==rq->address);
 if(mode==1){
  CHECK(!writer.fatal&&writer.corrected==corrected+1&&U32(w+28)==1&&U32(w+36)==2&&!(U32(w+16)&128));
  CHECK(U64(w+1280+8)==replacementQueue->address);RenderMeta a,b;memcpy(&a,w+256,sizeof a);memcpy(&b,w+1280,sizeof b);a.header.queue=b.header.queue=0;CHECK(!memcmp(&a,&b,sizeof a));
  CHECK(U32(w+20)==0&&writer.fault.state==0);
 }else {
  CHECK(writer.fatal==GW_CAPTURE&&writer.fault.state==4&&writer.fault.sampler_status==IV_BINDING&&!writer.fault.plan.trace.phase);
  if(mode==2){CHECK(!U32(w+28)&&U32(w+36)==1&&(U32(w+16)&32));CHECK(strcmp((char*)w+64,"accepted"));}
  else {CHECK(U32(w+28)==1&&U32(w+36)==2&&U64(w+1280+8)==replacementQueue->address);
   if(mode==3){CHECK(U64(w+4096+296)==2&&U64(w+4096+288)==(1ull<<25));CHECK(U32(w+4096+328)==152+96+40&&U32(w+4096+336)==1);CHECK(U64(w+4096+8)==rq->address&&U64(w+4096+96+8)==replacementQueue->address);}
   else {CHECK(!(U32(w+16)&128)&&!strcmp((char*)w+64,"accepted"));}
  }
  WriterFaultTick(&mod);WriterTick(&mod,0,NULL);CHECK(writer.fault.state==5&&queueAck[0]&&queueFatal[0]);
 }
 char logPath[2200];snprintf(logPath,sizeof logPath,"%s/case.log",path);FILE *log=fopen(logPath,"wb");CHECK(log);CHECK(fprintf(log,"case=%s mode=%u grouped=%u corrected_delta=%lld old_queue=%llX new_queue=%llX closing_success=%u\n%s\n%s\n",name,mode,grouped,(long long)(writer.corrected-corrected),(unsigned long long)U64(w+264),(unsigned long long)U64(w+1288),U32(w+28),queueAck,queueFatal)>0);CHECK(!fclose(log));
 CHECK(CloseHandle(invocationDirectoryHandle));invocationDirectoryHandle=INVALID_HANDLE_VALUE;api.read=ReadMock;api.log=Log;
}
int main(int argc,char **argv){
 CHECK(argc==4);CHECK(!queue_parent_fixture_main(3,argv));printLogs=0;
 replacementQueue=Add(0x40A000000ull,rq->size);memcpy(replacementQueue->bytes,rq->bytes,rq->size);
 Comparator(argv[3]);
 CaptureCase(argv[2],"queue-only-normal",1,0);CaptureCase(argv[2],"queue-only-grouped",1,1);
 CaptureCase(argv[2],"queue-malformed",2,0);CaptureCase(argv[2],"queue-plus-flags",3,0);CaptureCase(argv[2],"queue-late-owner",4,0);
 printf("PASS %u queue checks: scopedQ8bytes+912retainedbytes/allbits+negativecontexts+actualwitness+freshread/nativeleafpaths; native_game_calls=0\n",checks);return 0;
}
