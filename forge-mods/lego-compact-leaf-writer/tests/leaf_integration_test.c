#define main inherited_observer_fixture_unused_main
#include "attachment_test.c"
#undef main
static _Alignas(16) unsigned char retained[37][46400];
static Region *skinRegions[7][5];static unsigned activeTarget,activeOrdinal,activeMode,outerCount,leafCount;
static uint32_t activeCsr;static uintptr_t activeMesh,activePool,activeGraph;
static unsigned testFatalPublication;
void gw_test_fatal_cas_pause(gw_state *s){
 if(testFatalPublication){testFatalPublication=0;CHECK(s==&writer&&writer.fatal&&writer.fault.state==0);
  WriterTick(&mod,0,NULL);CHECK(!writerReportedFatal&&writerFaultPendingReported&&writer.fault.state==0);
 }
}
static uint32_t LeafNext(void *dst,const void *factor,const void *g,const void *bind,const uint32_t *palette,const uint32_t *map,uint32_t count){
 CHECK(dst&&factor&&g&&bind&&palette&&map);CHECK(_mm_getcsr()==0x9fa1&&GetLastError()==0x7799);
 if(!writer.fatal)CHECK((uintptr_t)g!=ivArrays[activeTarget][0]->address&&(uintptr_t)g!=ivArrays[activeTarget][1]->address);
 leafCount++;_mm_setcsr(0x7f85);SetLastError(0x5544);return count;
}
static uint32_t OuterNext(void *m,void *p,void *g){
 CHECK((uintptr_t)m==activeMesh&&(uintptr_t)p==activePool&&(uintptr_t)g==(activeMode?activeGraph:0));
 CHECK(_mm_getcsr()==activeCsr&&GetLastError()==0x2468);outerCount++;
 Region *s=skinRegions[activeTarget][activeOrdinal];const lp_asset *a=lp_asset_find(ids[activeTarget],activeOrdinal);CHECK(a);
 if(!g)Put(meshes[activeTarget][activeOrdinal],0xc4,1234,4);
 _Alignas(16) unsigned char factors[96]={0};unsigned dir=U32(s->bytes+12);
 for(unsigned j=0;j<a->groups;j++)for(unsigned t=0;t<2;t++){
  unsigned char *r=s->bytes+3072+j*40;unsigned q=U32(r+24),n=U32(s->bytes+dir+8+8*q);
  uintptr_t dst=U64(rw->bytes+0x488)+1234*16ull+(uint64_t)(U32(r+28+4*t)/3)*48;
  _mm_setcsr(0x9fa1);SetLastError(0x7799);
  CHECK(gw_leaf_dispatch(&writer,(void*)dst,factors+48*t,(void*)ivArrays[activeTarget][t]->address,(void*)(s->address+1024),
    (uint32_t*)(s->address+dir+U32(s->bytes+dir+4+8*q)),(uint32_t*)(s->address+16),n,engine+(t?0x513054:0x512fea))==n);
  CHECK(_mm_getcsr()==0x7f85&&GetLastError()==0x5544);
  if(!g)CHECK(U32(meshes[activeTarget][activeOrdinal]->bytes+0xc4)==1234);
 }
 _mm_setcsr(0x5f81);SetLastError(0x13579);return 1234;
}
static void FillSkins(void){
 for(unsigned t=0;t<7;t++)for(unsigned j=1;j<5;j++){
  const lp_asset *a=lp_asset_find(ids[t],j);CHECK(a);Region *m=meshes[t][j],*s=Add(U64(m->bytes+0x70),4096);skinRegions[t][j]=s;
  Put(s,0,a->bones,4);Put(s,8,16,4);unsigned dir=16+a->bones*4;Put(s,12,dir,4);Put(s,dir,a->groups,4);
  memcpy(s->bytes+16,a->map,a->bones*4);memcpy(s->bytes+1024,a->bind,a->bones*48);
  unsigned cursor=4+8*a->groups,out=0;
  for(unsigned k=0;k<a->groups;k++){Put(s,dir+4+8*k,cursor,4);Put(s,dir+8+8*k,a->counts[k],4);memcpy(s->bytes+dir+cursor,a->palette[k],a->counts[k]*4);cursor+=a->counts[k]*4;
   Put(s,3072+40*k+24,k,4);Put(s,3072+40*k+28,out*3,4);out+=a->counts[k];Put(s,3072+40*k+32,out*3,4);out+=a->counts[k];}
  Put(m,0x80,s->address+1024,8);Put(m,0x90,a->groups,4);Put(m,0x98,s->address+3072,8);Put(m,0xc8,out,4);
 }
}
static void SetActive(unsigned t,unsigned ordinal,unsigned grouped){
 activeTarget=t;activeOrdinal=ordinal;activeMode=grouped;activeMesh=meshes[t][ordinal]->address;activePool=rw->address+0x318;activeGraph=graphs[t]->address;
 unsigned index=37;for(unsigned i=0;i<37;i++)if(U32(retained[i]+28)==t){index=i;break;}CHECK(index<37);
 memcpy(ivArrays[t][0]->bytes,retained[index]+256+1024,nodeCounts[t]*48);memcpy(ivArrays[t][1]->bytes,retained[index]+256+7168,nodeCounts[t]*48);
 memcpy(ivArrays[7][0]->bytes,retained[index]+256+13312,107*48);memcpy(ivArrays[7][1]->bytes,retained[index]+256+18448,107*48);activeCsr=U32(retained[index]+108);
}
static void Invoke(void){_mm_setcsr(activeCsr);SetLastError(0x2468);CHECK(gw_dispatch(&writer,(void*)activeMesh,(void*)activePool,activeMode?(void*)activeGraph:NULL,engine+(activeMode?0x258aab:0x258e19))==1234);
 CHECK(_mm_getcsr()==0x5f81&&GetLastError()==0x13579&&!writer.fatal);}
#ifndef LEAF_MAIN
#define LEAF_MAIN main
#endif
int LEAF_MAIN(int argc,char **argv){
 CHECK(argc==3);for(unsigned i=0;i<37;i++){char path[2048];snprintf(path,sizeof path,"%s/skin-invocation-%06u.bin",argv[1],i+1);FILE*f=fopen(path,"rb");CHECK(f&&fread(retained[i],1,46400,f)==46400&&fgetc(f)==EOF);fclose(f);}
 api.version=16;api.size=sizeof api;api.build_id=Build;api.mod_id=Id;api.load_phase=Phase;api.read=ReadMock;api.read_code=CodeMock;api.module_base=Module;api.on=On;api.on_game_thread=OnTick;api.logs_folder=Folder;api.log=Log;mod.api=&api;
 PoseSetup(5);static const unsigned bases[7]={14,12,15,12,15,12,17};
 for(unsigned t=0;t<7;t++){Put(descriptors[t][0],4,0xffffff,4);Put(descriptors[t][0],8,0,8);for(unsigned j=1;j<5;j++){Put(descriptors[t][j],0x98,bases[t]+j-1,4);Put(meshes[t][j],0x30,bases[t]+j-1,4);}}
 FillSkins();gw_initialize(&writer,&invocationEnvelope,&shadowRig,engine,WriterCapture,NULL,WriterCompleted,NULL);writer.plan_capture=WriterPlan;
 writer.copy_sampler=SamplerCopy;
 writer.approved_next=(void*)OuterNext;*se_next_slot(&invocationEnvelope)=(void*)OuterNext;writer.leaf_next=writer.approved_leaf_next=(void*)LeafNext;
 wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,argv[2],-1,directory,1100)>0);CHECK(InvocationDirectoryInit(directory));
 printLogs=1;PublishFixture();WriterTick(&mod,0,NULL);CHECK(writer.ready&&invocationWhitelistCount==28);
 logicalTotal=READ_TOTAL_LIMIT;readTotal=READ_TOTAL_LIMIT;byteTotal=BYTE_TOTAL_LIMIT;armedAt=GetTickCount64()-ARM_MS-1;se_stop(&invocationEnvelope);
 /* Two routes per actual registered Mesh; exactly56 diagnostics. */
 for(unsigned t=0;t<7;t++)for(unsigned j=1;j<5;j++)for(unsigned route=0;route<2;route++){
  SetActive(t,j,route);Invoke();logsTick=0;CHECK(InvocationDrainOne(&mod));
  if(!t&&j==1&&!route){Invoke();CHECK(!InvocationPending()&&writerDiagnosticSequence==1);}
 }
 CHECK(writer.corrected==57&&!writer.failed&&shadowOk==56&&!shadowFail&&leafOk==56&&!leafFail&&leafNormalMask==127&&leafGroupedMask==127);
 /* Full queue must not consume a previously unused diagnostic quota. */
 memset((void*)writerDiagnosticAttempts,0,sizeof writerDiagnosticAttempts);
 memset((void*)writerDiagnosticRoutes,0,sizeof writerDiagnosticRoutes);
 for(unsigned i=0;i<SE_SLOTS;i++)InterlockedExchange(&invocationEnvelope.slots[i].state,SE_WRITING);
 Invoke();CHECK(writer.corrected==58&&writerDiagnosticDropped==1);for(unsigned i=0;i<SE_TARGETS;i++)CHECK(!writerDiagnosticAttempts[i]);
 for(unsigned i=0;i<SE_TARGETS;i++)CHECK(!writerDiagnosticRoutes[i]);
 for(unsigned i=0;i<SE_SLOTS;i++)InterlockedExchange(&invocationEnvelope.slots[i].state,SE_FREE);
 InterlockedExchange(&writerDiagnosticProducers,1);WriterTick(&mod,0,NULL);CHECK(!writerDiagnosticsDone);InterlockedExchange(&writerDiagnosticProducers,0);WriterTick(&mod,0,NULL);CHECK(writerDiagnosticsDone);
 WriterEnded(&mod,HF_EVENT_SESSION_END,NULL);for(unsigned i=0;i<20;i++)Invoke();CHECK(writer.corrected==78&&!writer.failed&&shadowRecords==56);
 /* Exercise the actual sampler+plan+recheck reader budget and every failure. */
 SetActive(2,1,0);se_record r={0};for(unsigned i=0;i<invocationEnvelope.target_count;i++)if(invocationEnvelope.targets[i].mesh==activeMesh)r.target=invocationEnvelope.targets[i];
 r.call=(se_call){activeMesh,activePool,0,engine+0x258e19,GetCurrentThreadId(),0x1f80,0};
 se_observe_context ctx={&invocationEnvelope,&r.target,&r.call,r.payload,SE_PAYLOAD};se_observation_result observed={0};lp_plan p;
 WriterCapture(&ctx,&observed,NULL);CHECK(!observed.status&&WriterPlan(&p,&r,0,NULL)&&WriterPlan(&p,&r,1,NULL));
 unsigned allReads=U32(r.payload+36),allBytes=U32(r.payload+40);CHECK(allReads<=512&&allBytes<=65536);
 for(unsigned n=1;n<=allReads;n++){captureReadOrdinal=0;failReadOrdinal=n;WriterCapture(&ctx,&observed,NULL);
  int ok=!observed.status&&WriterPlan(&p,&r,0,NULL)&&WriterPlan(&p,&r,1,NULL);CHECK(!ok&&invocationBudget==NULL&&!writerCaptureActive);}
 failReadOrdinal=0;WriterCapture(&ctx,&observed,NULL);CHECK(!observed.status);IvU32(r.payload,36,512);CHECK(!WriterPlan(&p,&r,0,NULL));
 WriterCapture(&ctx,&observed,NULL);CHECK(!observed.status);IvU32(r.payload,40,65535);CHECK(!WriterPlan(&p,&r,0,NULL));
 /* Independent fault channel remains live after successful quota/diagnostic closure. */
 failReadOrdinal=0;captureReadOrdinal=0;SetActive(2,1,0);skinRegions[2][1]->bytes[16]^=1;
 testFatalPublication=1;_mm_setcsr(activeCsr);SetLastError(0x2468);
 CHECK(gw_dispatch(&writer,(void*)activeMesh,(void*)activePool,NULL,engine+0x258e19)==1234);
 CHECK(writer.fatal==GW_CAPTURE&&writer.fault.state==4&&writer.fault.record.capture_status==-1002);
 CHECK(!writer.fault.sampler_status&&writer.fault.sampler_used==SM_RAW_BYTES&&writer.fault.plan.trace.stage==15&&writer.fault.plan.trace.error==3);
 CHECK(shadowRecords==56&&writerDiagnosticsDone);unsigned beforeFaultLogs=writerFaultPendingReported;
 WriterFaultTick(&mod);CHECK(writer.fault.state==5&&strstr(lastLog,"fault_evidence")&&writerFaultPendingReported==beforeFaultLogs);
 WriterTick(&mod,0,NULL);CHECK(writerReportedFatal==GW_CAPTURE&&!testFatalPublication);
 CHECK(!WriterFaultWrite()); /* CREATE_NEW rejects replacing its own evidence. */
 WriterFaultTick(&mod);CHECK(writer.fault.state==5); /* no repeated I/O or acknowledgement */
 char faultPath[2048];snprintf(faultPath,sizeof faultPath,"%s/leaf-fault-first.bin",argv[2]);FILE *faultFile=fopen(faultPath,"rb");CHECK(faultFile);
 static unsigned char faultBytes[WF_BYTES];CHECK(fread(faultBytes,1,WF_BYTES,faultFile)==WF_BYTES&&fgetc(faultFile)==EOF);CHECK(!fclose(faultFile));
 CHECK(!memcmp(faultBytes,"LGFAULT3",8)&&U32(faultBytes+8)==3&&U32(faultBytes+16)==WF_BYTES&&U64(faultBytes+56)==writer.fault.sequence&&U32(faultBytes+160)==2);
 CHECK(U32(faultBytes+176)==15&&U32(faultBytes+180)==3&&U32(faultBytes+184)==1);
 unsigned valid=U32(faultBytes+208);for(unsigned i=valid;i<LP_BYTES;i++)CHECK(!faultBytes[58864+776+i]);
 for(unsigned i=67968;i<73728;i++)CHECK(!faultBytes[i]);
 CHECK(!memcmp(faultBytes+73728,"LGSMPL01",8)&&U32(faultBytes+73728+16)&1);
 CHECK(U32(faultBytes+73728+24)==1&&U32(faultBytes+73728+28)==1);
 /* Reset ONLY synthetic state to exercise file collision failure reporting. */
 writer.fault.state=4;printLogs=0;WriterFaultTick(&mod);CHECK(writer.fault.state==6&&strstr(lastLog,"fault_write_failed"));printLogs=1;
 WriterFaultTick(&mod);CHECK(writer.fault.state==6);
 CHECK(CloseHandle(invocationDirectoryHandle));invocationDirectoryHandle=INVALID_HANDLE_VALUE;
 printf("PASS %u leaf integration checks:56raw/math/leaf78continuous reads=%u bytes=%u native_game_calls=0\n",checks,allReads,allBytes);return 0;
}
