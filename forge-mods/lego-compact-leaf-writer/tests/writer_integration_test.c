#define main inherited_observer_fixture_unused_main
#include "attachment_test.c"
#undef main
static _Alignas(16) unsigned char retained[37][46400];
static unsigned writerTestCalls;static uintptr_t expectedM,expectedP,expectedG;static uint32_t expectedCsr;
static uint32_t WriterFakeNext(void *m,void *p,void *g){
 CHECK((uintptr_t)m==expectedM&&(uintptr_t)p==expectedP&&(uintptr_t)g!=expectedG&&g);
 CHECK(_mm_getcsr()==expectedCsr&&GetLastError()==0x2468);
 CHECK(U32((unsigned char*)g+16)>0&&U32((unsigned char*)g+16)==U32((unsigned char*)g+40));
 CHECK(U64((unsigned char*)g+24)!=U64((unsigned char*)g+48));writerTestCalls++;
 _mm_setcsr(0x5f81);SetLastError(0x13579);return 1234;
}
int main(int argc,char **argv){
 CHECK(argc==3);
 for(unsigned i=0;i<37;i++){char path[2048];snprintf(path,sizeof path,"%s/skin-invocation-%06u.bin",argv[1],i+1);FILE *f=fopen(path,"rb");CHECK(f&&fread(retained[i],1,46400,f)==46400&&fgetc(f)==EOF);fclose(f);}
 api.version=16;api.size=sizeof api;api.build_id=Build;api.mod_id=Id;api.load_phase=Phase;
 api.read=ReadMock;api.read_code=CodeMock;api.module_base=Module;api.on=On;api.on_game_thread=OnTick;api.logs_folder=Folder;api.log=Log;mod.api=&api;
 PoseSetup(5);static const unsigned bases[7]={14,12,15,12,15,12,17};
 for(unsigned t=0;t<7;t++){
  Put(descriptors[t][0],4,0xffffff,4);Put(descriptors[t][0],8,0,8);
  for(unsigned j=1;j<5;j++){Put(descriptors[t][j],0x98,bases[t]+j-1,4);Put(meshes[t][j],0x30,bases[t]+j-1,4);}
 }
 gw_initialize(&writer,&invocationEnvelope,&shadowRig,engine,WriterCapture,NULL,WriterCompleted,NULL);
 writer.approved_next=(void*)WriterFakeNext;*se_next_slot(&invocationEnvelope)=(void*)WriterFakeNext;
 wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,argv[2],-1,directory,1100)>0);CHECK(InvocationDirectoryInit(directory));
 printLogs=1;PublishFixture();WriterTick(&mod,0,NULL);CHECK(writer.ready&&invocationWhitelistCount==28);
 /* Writer capture ignores ONLY old diagnostic lifetime/quota controls. */
 logicalTotal=READ_TOTAL_LIMIT;readTotal=READ_TOTAL_LIMIT;byteTotal=BYTE_TOTAL_LIMIT;armedAt=GetTickCount64()-ARM_MS-1;se_stop(&invocationEnvelope);
 for(unsigned i=0;i<37;i++){
  unsigned t=U32(retained[i]+28),ordinal=U32(retained[i]+32),count=nodeCounts[t];
  memcpy(ivArrays[t][0]->bytes,retained[i]+256+1024,count*48);memcpy(ivArrays[t][1]->bytes,retained[i]+256+7168,count*48);
  memcpy(ivArrays[7][0]->bytes,retained[i]+256+13312,107*48);memcpy(ivArrays[7][1]->bytes,retained[i]+256+18448,107*48);
  expectedM=meshes[t][ordinal]->address;expectedP=rw->address+0x318;expectedG=graphs[t]->address;expectedCsr=U32(retained[i]+108);
  _mm_setcsr(expectedCsr);SetLastError(0x2468);
  CHECK(gw_dispatch(&writer,(void*)expectedM,(void*)expectedP,(void*)expectedG,engine+0x258aab)==1234);
  CHECK(_mm_getcsr()==0x5f81&&GetLastError()==0x13579&&!writer.fatal);logsTick=0;CHECK(InvocationDrainOne(&mod));
 }
 CHECK(writer.corrected==37&&writerTestCalls==37&&shadowOk==37&&!shadowFail&&invocationMask==0x7f);
 /* Full diagnostic queue never occupies or suppresses a correction slot. */
 for(unsigned i=0;i<SE_SLOTS;i++)InterlockedExchange(&invocationEnvelope.slots[i].state,SE_WRITING);
 expectedM=meshes[4][1]->address;_mm_setcsr(expectedCsr);SetLastError(0x2468);
 CHECK(gw_dispatch(&writer,(void*)expectedM,(void*)expectedP,(void*)expectedG,engine+0x258aab)==1234);
 CHECK(writer.corrected==38&&writerDiagnosticDropped==1&&!writer.failed);
 for(unsigned i=0;i<SE_SLOTS;i++)InterlockedExchange(&invocationEnvelope.slots[i].state,SE_FREE);
 /* Closing diagnostics waits for a producer already inside its admission. */
 InterlockedExchange(&writerDiagnosticProducers,1);WriterTick(&mod,0,NULL);CHECK(!writerDiagnosticsDone);
 InterlockedExchange(&writerDiagnosticProducers,0);WriterTick(&mod,0,NULL);CHECK(writerDiagnosticsDone);
 InterlockedExchange(&writer.diagnostics_closed,1);WriterEnded(&mod,HF_EVENT_SESSION_END,NULL);
 for(unsigned i=0;i<70;i++){_mm_setcsr(expectedCsr);SetLastError(0x2468);CHECK(gw_dispatch(&writer,(void*)expectedM,(void*)expectedP,(void*)expectedG,engine+0x258aab)==1234);}
 CHECK(writer.corrected==108&&!writer.failed&&!writer.fatal&&writer.teardown&&shadowRecords==37);
 /* Every admitted read position fails closed without stale payload/math. */
 se_record record={0};record.target=invocationEnvelope.targets[0];
 record.call=(se_call){record.target.mesh,rw->address+0x318,graphs[0]->address,engine+0x258aab,GetCurrentThreadId(),0x1f80,0};
 se_observe_context ctx={&invocationEnvelope,&record.target,&record.call,record.payload,SE_PAYLOAD};se_observation_result result={0};
 WriterCapture(&ctx,&result,NULL);CHECK(result.status==0);unsigned callReads=U32(record.payload+36);
 for(unsigned nth=1;nth<=callReads;nth++){
  captureReadOrdinal=0;failReadOrdinal=nth;WriterCapture(&ctx,&result,NULL);CHECK(result.status!=0&&U32(record.payload+36)==nth&&invocationBudget==NULL&&!writerCaptureActive);
 }
 failReadOrdinal=0;CHECK(callReads<=512&&U32(record.payload+40)<=65536);
 /* Actual identity paths, not merely an injected transport failure. */
 struct Corruption {Region *r;unsigned off;uint64_t value;unsigned bytes;} corruptions[]={
  {generations,1,1,1},{generations,0,1,1},{playerRow,0x84,99,4},{avatarDesc,0,0x1234,8},{avatarDesc,12,0x10001,4},
  {units[1],0x1d0,0,8},{descriptors[0][1],0x90,units[2]->address+0x60,8},
  {descriptors[0][1],4,0xffffff,4},{meshes[0][1],0x38,graphs[1]->address,8},
  {meshes[0][1],0x30,0,4},{ivParentGraph,0x28,106,4},{graphs[0],0x28,0,4}};
 for(unsigned n=0;n<sizeof corruptions/sizeof corruptions[0];n++){
  struct Corruption c=corruptions[n];unsigned char saved[8];memcpy(saved,c.r->bytes+c.off,c.bytes);
  Put(c.r,c.off,c.value,c.bytes);WriterCapture(&ctx,&result,NULL);if(!result.status)fprintf(stderr,"corruption %u not rejected\n",n);CHECK(result.status!=0&&invocationBudget==NULL&&!writerCaptureActive);
  memcpy(c.r->bytes+c.off,saved,c.bytes);
 }
 WriterCapture(&ctx,&result,NULL);CHECK(result.status==0);
 CHECK(CloseHandle(invocationDirectoryHandle));invocationDirectoryHandle=INVALID_HANDLE_VALUE;
 printf("PASS %u integrated writer assertions:37raw/math108continuous,%u read-failure positions,oldcaps/end/diagnostic-full ignored by correction\n",checks,callReads);return 0;
}
