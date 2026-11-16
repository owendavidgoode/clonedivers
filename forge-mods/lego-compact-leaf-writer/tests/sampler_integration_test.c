#define LEAF_MAIN parent_fixture_main
#include "leaf_integration_test.c"
static unsigned inject;
static char caseAck[900],caseFatal[900];
static void CaseLog(HfMod *m,const char *fmt,...){
 char text[900];va_list args;va_start(args,fmt);int n=vsnprintf(text,sizeof text,fmt,args);va_end(args);CHECK(n>=0&&n<900);Log(m,"%s",text);
 if(strstr(text," fault_evidence "))memcpy(caseAck,text,(size_t)n+1);
 if(strstr(text," fatal "))memcpy(caseFatal,text,(size_t)n+1);
}
static int CaseRead(uintptr_t at,void *out,size_t bytes){
 int rc=ReadMock(at,out,bytes);if(rc||!writerCaptureActive||!invocationBudget||!samplerTrace.attempted)return rc;
 if(inject==1){ /* two different descriptors change, both remain valid reads */
  for(unsigned j=1;j<=2;j++)if(at==descriptors[activeTarget][j]->address+0xd8&&bytes==4){uint32_t flags=U32(out)^(4u<<j);memcpy(out,&flags,4);}
 }else if(inject==2 && at==descriptors[activeTarget][activeOrdinal]->address+0x90){memset(out,0xaa,bytes);return 1;}
 else if(inject==3 && at==rm->address+0x10&&bytes==80){uint32_t bad=0;memcpy((unsigned char*)out+0x2c,&bad,4);}
 else if(inject==4 && at==rw->address+0x150&&bytes==8){uint64_t wrong=rm->address+16;memcpy(out,&wrong,8);}
 else if(inject==5 && samplerTrace.success && at==gameModule+0x3326468){memset(out,0xaa,bytes);return 1;}
 return 0;
}
static void Case(const char *base,const char *name,unsigned mode){
 char path[2048];snprintf(path,sizeof path,"%s/%s",base,name);wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,directory,1100)>0);CHECK(CreateDirectoryW(directory,NULL));CHECK(InvocationDirectoryInit(directory));
 memset(&writer.fault,0,sizeof writer.fault);writer.fatal=0;writer.first_failure_sequence=0;writerReportedFatal=0;writerFaultPendingReported=0;memset(caseAck,0,sizeof caseAck);memset(caseFatal,0,sizeof caseFatal);
 SetActive(4,2,0);inject=mode;api.read=CaseRead;api.log=CaseLog;_mm_setcsr(activeCsr);SetLastError(0x2468);
 CHECK(gw_dispatch(&writer,(void*)activeMesh,(void*)activePool,NULL,engine+0x258e19)==1234);inject=0;
 CHECK(writer.fatal==GW_CAPTURE&&writer.fault.state==4&&writer.fault.sampler_status==IV_BINDING&&!writer.fault.plan.trace.phase);
 const unsigned char *w=writer.fault.sampler;CHECK(!memcmp(w,"LGSMPL01",8)&&U32(w+24)==1&&U32(w+32)==1);
 if(mode!=5)CHECK(!strcmp((char*)w+160,"render_metadata_changed"));
 if(mode==1){CHECK(U32(w+28)==1&&U32(w+36)==2&&(U32(w+16)&128));CHECK(!strcmp((char*)w+4864,"capture_render"));
  CHECK((U64(w+4096+288)&((1ull<<25)|(1ull<<26)))==((1ull<<25)|(1ull<<26)));
  for(unsigned j=1;j<=2;j++)CHECK(U32(w+256+152+j*96+40)!=U32(w+1280+152+j*96+40));
 }else if(mode==5){CHECK(U32(w+28)==1&&U32(w+36)==2&&!strcmp((char*)w+64,"accepted"));CHECK(!memcmp(w+256,w+1280,1024));}
 else {CHECK(!U32(w+28)&&U32(w+36)==1);
  if(mode==2){CHECK(U32(w+16)&64);CHECK(!strcmp((char*)w+64,"descriptor_source_read"));CHECK(U32(w+2816+40)==13);CHECK(!(U32(w+2816+48)&32));}
  if(mode==3){CHECK(U32(w+16)&16);CHECK(!strcmp((char*)w+64,"render_manager_schema"));CHECK(U32(w+2304+88)&64);}
  if(mode==4){CHECK(U32(w+16)&32);CHECK(!strcmp((char*)w+64,"render_world_back_mismatch"));CHECK(U32(w+2432+272)==9&&U32(w+2432+288)&512);}
 }
 WriterFaultTick(&mod);WriterTick(&mod,0,NULL);CHECK(writer.fault.state==5&&caseAck[0]&&caseFatal[0]);
 char logPath[2200];snprintf(logPath,sizeof logPath,"%s/case.log",path);FILE *f=fopen(logPath,"wb");CHECK(f);CHECK(fprintf(f,"%s\n%s\n",caseAck,caseFatal)>0);CHECK(!fclose(f));
 CHECK(CloseHandle(invocationDirectoryHandle));invocationDirectoryHandle=INVALID_HANDLE_VALUE;
}
int main(int argc,char **argv){
 int prior=parent_fixture_main(argc,argv);CHECK(!prior);printLogs=0;
 Case(argv[2],"closing-mismatch",1);Case(argv[2],"closing-read-refusal",2);Case(argv[2],"closing-schema-refusal",3);Case(argv[2],"closing-route-refusal",4);
 Case(argv[2],"post-closing-owner-refusal",5);
 /* A valid snapshot cannot leak into early capacity/token/owner refusal. */
 api.read=ReadMock;SetActive(4,2,0);se_record record={0};for(unsigned i=0;i<invocationEnvelope.target_count;i++)if(invocationEnvelope.targets[i].mesh==activeMesh)record.target=invocationEnvelope.targets[i];
 record.call=(se_call){activeMesh,activePool,0,engine+0x258e19,GetCurrentThreadId(),0x1f80,0};se_observe_context ctx={&invocationEnvelope,&record.target,&record.call,record.payload,SE_PAYLOAD};se_observation_result result;
 WriterCapture(&ctx,&result,NULL);CHECK(!result.status&&U32(samplerWitness+28)==1);
 ctx.capacity=0;WriterCapture(&ctx,&result,NULL);CHECK(result.status==IV_BUDGET);for(unsigned i=0;i<GW_SAMPLER_BYTES;i++)CHECK(!samplerWitness[i]);
 ctx.capacity=SE_PAYLOAD;record.target.token=UINT64_MAX;WriterCapture(&ctx,&result,NULL);CHECK(result.status==IV_BINDING&&!U32(samplerWitness+24)&&!U32(samplerWitness+32));
 for(unsigned i=256;i<2304;i++)CHECK(!samplerWitness[i]);
 record.target=invocationEnvelope.targets[0];failAt=gameModule+0x3326468;WriterCapture(&ctx,&result,NULL);failAt=0;CHECK(result.status&&!U32(samplerWitness+24));
 /* Absent sampler wire has canonical header and no stale contents. */
 memset(writer.fault.sampler,0,sizeof writer.fault.sampler);WriterFaultSerialize(&writer.fault);CHECK(!memcmp(writerFaultWire+73728,"LGSMPL01",8)&&U32(writerFaultWire+73728+8)==1);
 for(unsigned i=73728+16;i<WF_BYTES;i++)CHECK(!writerFaultWire[i]);
 printf("PASS %u sampler checks; closing mismatch/read/schema/route plus early resets; native_game_calls=0\n",checks);return 0;
}
