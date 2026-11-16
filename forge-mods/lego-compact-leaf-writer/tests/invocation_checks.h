static Region *ivParentGraph,*ivArrays[8][2];static volatile LONG delegated;
static uint32_t delegateResult=1234;
static uintptr_t receivedMesh,receivedPool,receivedGraph;static uint32_t receivedCsr,receivedError;
static uint32_t FakeNext(void *mesh,void *pool,void *graph)
{
    receivedMesh=(uintptr_t)mesh;receivedPool=(uintptr_t)pool;receivedGraph=(uintptr_t)graph;
    receivedCsr=_mm_getcsr();receivedError=GetLastError();InterlockedIncrement(&delegated);
    if(perturbEnvironment){SetLastError(0x13579);_mm_setcsr(0x5F80);}return delegateResult;
}
static void FillPose(Region *r,unsigned nodes)
{
    for(unsigned n=0;n<nodes;n++){Put(r,n*48,0x3F800000,4);Put(r,n*48+20,0x3F800000,4);Put(r,n*48+40,0x3F800000,4);}
}
static void PoseSetup(unsigned meshesPerTarget)
{
    CHECK(sm_prepare(&shadowRig)==CM_OK);shadowRecords=shadowOk=shadowFail=0;
    invocationBudget=NULL;logicalTick=logicalTotal=sdkReadTotal=0;
    memset(&invocationEnvelope,0,sizeof invocationEnvelope);invocationFatal=invocationTerminal=0;
    invocationFiles=invocationAccepted=invocationRefused=invocationAbandoned=invocationMask=invocationWhitelistCount=0;
    invocationBirth=1;invocationPid=GetCurrentProcessId();invocationDrainAt=0;invocationUserStop=invocationTimedOut=0;
    invocationSetupGate=0;invocationDeferredCount=0;delegated=0;delegateResult=1234;
    captureReadOrdinal=failReadOrdinal=mutationReadBytes=perturbEnvironment=0;captureBlockAt=0;captureBlocked=0;
    Setup(meshesPerTarget,0);Put(units[0],0x70,107,4);armedAt=GetTickCount64();if(!armedAt)armedAt=1;
    CHECK(se_initialize(&invocationEnvelope,InvocationObserve,NULL,56,2)==SE_OK);
    *se_next_slot(&invocationEnvelope)=(void*)FakeNext;
    ivParentGraph=Add(0x4F0000000ull,56);Put(ivParentGraph,0,engine+0x16880B0,8);Put(ivParentGraph,8,2,4);Put(ivParentGraph,0x10,107,4);
    Register(80,ivParentGraph,2);Put(units[0],0x64,Handle(80),4);Put(units[0],0x68,rm->address+0x10,8);
    for(unsigned t=0;t<8;t++) {
        Region *g=t<7?graphs[t]:ivParentGraph;g->size=56;
        unsigned count=t<7?nodeCounts[t]:107;Put(g,0x28,count,4);
        for(unsigned old=0;old<2;old++) {
            Region *a=Add(0x800000000ull+(t*2+old)*0x10000ull,count*48);
            FillPose(a,count);ivArrays[t][old]=a;Put(g,old?0x30:0x18,a->address,8);
        }
        if(t<7)for(unsigned j=0;j<meshesPerTarget;j++) {
            meshes[t][j]->size=0xD0;Put(meshes[t][j],0x80,0xA00000000ull+t*0x10000+j*0x100,8);
            Put(meshes[t][j],0x90,1,4);Put(meshes[t][j],0x98,0xA10000000ull+t*0x10000+j*0x100,8);Put(meshes[t][j],0xC8,6,4);
        }
    }
    Put(rw,0x488,0xA20000000ull,8);Put(rw,0x490,32,4);
    /* Deliberately unqualified global group inventory must never be sampled. */
    VecPut(rw,0x3D0,485,500,0xDEAD000000ull);
}
static void PublishFixture(void)
{for(unsigned i=0;i<200 && renderStage!=4 && !Stopped();i++)FreshScan();CHECK(renderStage==4&&!Stopped()&&finalMask==0x7F);}
static const se_record *ObserveOne(unsigned t,uintptr_t override,se_view *view)
{
    CHECK(SkinObserved((void*)meshes[t][0]->address,(void*)(rw->address+0x318),(void*)override)==delegateResult);
    CHECK(se_try_take(&invocationEnvelope,view));return view->record;
}
static DWORD WINAPI InvokeThread(void *unused)
{(void)unused;return SkinObserved((void*)meshes[0][0]->address,(void*)(rw->address+0x318),NULL);}
static void RefusalCases(void)
{
    /* Fail each admitted SDK read position in a full default capture, including
       partial writes. A failure cannot suppress delegation or leak a pointer into a later attempt. */
    PoseSetup(1);PublishFixture();se_view view={0};const se_record *r=ObserveOne(0,0,&view);
    unsigned calls=U32(r->payload+36);CHECK(se_release(&invocationEnvelope,&view));
    for(unsigned nth=1;nth<=calls;nth++) {
        PoseSetup(1);PublishFixture();failReadOrdinal=nth;
        r=ObserveOne(0,0,&view);CHECK(r->capture_status!=0 && delegated==1 && U32(r->payload+36)==nth && invocationBudget==NULL);
        CHECK(!schemaFailure.pending&&!routeFailure.pending&&!renderRefusal.pending&&!consistencyRefusal.pending);
        CHECK(se_release(&invocationEnvelope,&view));
    }
    PoseSetup(1);PublishFixture();failAt=ivArrays[0][0]->address;
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_POSE_READ && !(U32(r->payload+4)&(1u<<8)));
    for(unsigned i=1024;i<7168;i++)CHECK(r->payload[i]==0);CHECK(se_release(&invocationEnvelope,&view));
    /* A changed transform is refused without poisoning a later independently validated call. */
    PoseSetup(1);PublishFixture();MutateAt(ivArrays[0][0]->address,ivArrays[0][0],0,0x40000000,4);
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_POSE_CHANGED && !r->abandoned && !se_is_stopped(&invocationEnvelope));CHECK(se_release(&invocationEnvelope,&view));
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_OK && delegated==2);CHECK(se_release(&invocationEnvelope,&view));
    /* Proven mutable output offset and pool cursor do not become false identity gates. */
    PoseSetup(1);PublishFixture();MutateAt(meshes[0][0]->address+0x70,meshes[0][0],0xC4,0x12345678,4);mutationReadBytes=96;
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==0 && U32(r->payload+564)!=U32(r->payload+660));CHECK(se_release(&invocationEnvelope,&view));
    PoseSetup(1);PublishFixture();MutateAt(rw->address+0x488,rw,0x490,999,4);
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==0 && U32(r->payload+680)!=U32(r->payload+696));CHECK(se_release(&invocationEnvelope,&view));
    PoseSetup(1);PublishFixture();MutateAt(meshes[0][0]->address+0x70,meshes[0][0],0xC8,7,4);mutationReadBytes=96;
    r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_HEADER_CHANGED);CHECK(se_release(&invocationEnvelope,&view));
    /* Parent count, actual node/previous bounds and fixed session budgets refuse safely. */
    PoseSetup(1);PublishFixture();Put(ivParentGraph,0x28,106,4);r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_PARENT_HEADER);CHECK(se_release(&invocationEnvelope,&view));
    PoseSetup(1);PublishFixture();Put(graphs[0],0x28,0,4);r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_SELECTED_HEADER);CHECK(se_release(&invocationEnvelope,&view));
    PoseSetup(1);PublishFixture();logicalTotal=READ_TOTAL_LIMIT;r=ObserveOne(0,0,&view);CHECK(r->capture_status==IV_BUDGET && !U32(r->payload+36));CHECK(se_release(&invocationEnvelope,&view));
    PoseSetup(1);PublishFixture();perturbEnvironment=1;_mm_setcsr(0x3F80);SetLastError(0x2468);
    CHECK(SkinObserved((void*)meshes[0][0]->address,(void*)(rw->address+0x318),(void*)ivParentGraph->address)==1234);
    CHECK(_mm_getcsr()==0x5F80 && GetLastError()==0x13579 && receivedCsr==0x3F80 && receivedError==0x2468);
    CHECK(receivedMesh==meshes[0][0]->address && receivedPool==rw->address+0x318 && receivedGraph==ivParentGraph->address);
    CHECK(se_try_take(&invocationEnvelope,&view));r=view.record;
    CHECK(r->call.entry_mxcsr==0x3F80 && r->return_mxcsr==0x5F80 && r->call.entry_last_error==0x2468 && r->return_last_error==0x13579);
    CHECK(se_release(&invocationEnvelope,&view));_mm_setcsr(0x1F80);
    /* Real concurrent tick contention and atomic session-end while SDK read is blocked. */
    for(unsigned stop=0;stop<2;stop++) {
        PoseSetup(1);PublishFixture();captureEntered=CreateEventW(NULL,TRUE,FALSE,NULL);captureResume=CreateEventW(NULL,TRUE,FALSE,NULL);
        CHECK(captureEntered&&captureResume);captureBlockAt=ivArrays[0][0]->address;
        HANDLE thread=CreateThread(NULL,0,InvokeThread,NULL,0,NULL);CHECK(thread&&WaitForSingleObject(captureEntered,5000)==WAIT_OBJECT_0);
        unsigned beforeReads=reads,beforeLogs=logs,beforeSerial=updateSerial;Tick(&mod,0,NULL);
        CHECK(reads==beforeReads && logs==beforeLogs && updateSerial==beforeSerial);
        if(stop)Ended(&mod,HF_EVENT_SESSION_END,NULL);
        SetEvent(captureResume);CHECK(WaitForSingleObject(thread,5000)==WAIT_OBJECT_0);
        DWORD result=0;CHECK(GetExitCodeThread(thread,&result)&&result==1234&&delegated==1);
        CHECK(se_try_take(&invocationEnvelope,&view));r=view.record;
        CHECK(stop?(r->abandoned&&r->capture_status==IV_STOPPED&&!invocationFatal):(r->capture_status==0&&!r->abandoned));CHECK(se_release(&invocationEnvelope,&view));
        CloseHandle(thread);CloseHandle(captureEntered);CloseHandle(captureResume);
    }
    printf("read-failure positions exercised: %u; pre-call mutation, budgets, concurrent tick/end passed\n",calls);
}
int main(int argc,char **argv)
{
    api.version=16;api.size=sizeof api;api.build_id=Build;api.mod_id=Id;api.load_phase=Phase;
    api.read=ReadMock;api.read_code=CodeMock;api.module_base=Module;api.on=On;api.on_game_thread=OnTick;api.logs_folder=Folder;api.log=Log;mod.api=&api;
    PoseSetup(8);SlowOwner();PublishFixture();
    Region *large=Add(0xB00000000ull,56),*current=Add(0xB10000000ull,128*48),*previous=Add(0xB20000000ull,128*48);
    Put(large,0,engine+0x16880B0,8);Put(large,8,2,4);Put(large,0x10,128,4);Put(large,0x28,128,4);
    Put(large,0x18,current->address,8);Put(large,0x30,previous->address,8);FillPose(current,128);FillPose(previous,128);
    se_view view={0};const se_record *r=ObserveOne(0,large->address,&view);
    CHECK(r->capture_status==0 && r->result==1234 && !r->abandoned && U32(r->payload+4)==IV_VALID_COMPLETE && U32(r->payload+12)==IV_FLAGS_COMPLETE);
    CHECK(U32(r->payload+32)<=512 && U32(r->payload+36)==U32(r->payload+32) && U32(r->payload+40)<=65536);
    printf("worst full InvocationObserve: %u logical / %u SDK reads / %u bytes\n",U32(r->payload+32),U32(r->payload+36),U32(r->payload+40));
    CHECK(se_release(&invocationEnvelope,&view));
    RefusalCases();
    /* Full emitter refuses unsupported fake caller/null mode in shadow. The
       separate retained-record fixture qualifies successful grouped math. */
    CHECK(argc==2);wchar_t directory[1100];CHECK(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,argv[1],-1,directory,1100)>0);
    PoseSetup(5);for(unsigned t=0;t<7;t++){Put(descriptors[t][4],4,0xFFFFFF,4);Put(descriptors[t][4],8,0,8);}
    CHECK(InvocationDirectoryInit(directory));printLogs=1;printf("BRIDGE_BEGIN\n");PublishFixture();
    CHECK(SkinObserved((void*)meshes[0][0]->address,(void*)(rw->address+0x318),NULL)==1234);
    Tick(&mod,0,NULL);Tick(&mod,0,NULL);
    CHECK(Stopped() && invocationFiles==1 && invocationAccepted==1 && shadowRecords==1 && shadowOk==0 && shadowFail==1 && delegated==1);
    unsigned before=logs;Tick(&mod,0,NULL);CHECK(logs==before);
    printf("PASS %u invocation assertions; files=%u accepted=%u pending=%u\n",checks,invocationFiles,invocationAccepted,InvocationPending());
    return 0;
}
