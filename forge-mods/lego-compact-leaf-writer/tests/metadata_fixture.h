/* Synthetic metadata only. World/pose, skin and palette payloads are absent. */
static Region *rm,*rq,*rw,*handles,*objects,*types,*mapping,*meshEntries[7];
static Region *graphs[7],*descriptors[7][8],*meshes[7][8],*groups,*groupList,*provider,*providerRefs,*overrideGraph;
static unsigned maxReads,maxBytes,maxTickLogs,meshLogs,groupLogs;
static uint32_t Handle(unsigned i){return 0x100u|i;}
static void VecPut(Region *r,unsigned offset,unsigned length,unsigned capacity,uintptr_t data)
{Put(r,offset,length,4);Put(r,offset+4,capacity,4);Put(r,offset+8,data,8);}
static void Register(unsigned index,Region *object,unsigned type)
{Put(handles,index*4,Handle(index),4);Put(mapping,index*4,index,4);Put(objects,index*8,object->address,8);Put(types,index*4,type,4);}
static void Setup(unsigned count,unsigned groupCount)
{
    Reset();memset(targets,0,sizeof targets);memset(&poolInitial,0,sizeof poolInitial);
    memset(&groupBaseline,0,sizeof groupBaseline);memset(&groupProvider,0,sizeof groupProvider);
    renderStage=targetCursor=meshCursor=groupCursor=groupTarget=groupTotal=matchedGroups=0;
    meshRecords=declaredMeshes=finalMask=logsTotal=logsTick=0;groupMatches=0;poolKnown=groupKnown=0;renderFailure="none";
    registeredRecords=unregisteredRecords=0;memset(&renderRefusal,0,sizeof renderRefusal);memset(&resolveTrace,0,sizeof resolveTrace);
    memset(&consistencyRefusal,0,sizeof consistencyRefusal);consistencyLogs=0;memset(lastConsistency,0,sizeof lastConsistency);
    meshLogs=groupLogs=0;renderMutationRegion=NULL;
    memset(&schemaFailure,0,sizeof schemaFailure);
    memset(&routeFailure,0,sizeof routeFailure);
    schemaLogs=0;lastSchema[0]=0;
    rm=Add(0x400000000ull,0x60);rq=Add(0x408000000ull,0x118);rw=Add(0x410000000ull,0x6B0);
    Put(rq,0,engine+0x168B5A0,8);Put(rq,8,engine+0x1689440,8);
    Put(rq,0x20,rw->address+0x550,8);Put(rq,0x80,rw->address+0x10,8);
    Put(rw,0,engine+0x1672E70,8);Put(rw,8,1,4);
    Put(rw,0x10,engine+0x168B5A0,8);Put(rw,0x18,engine+0x16729A8,8);
    handles=Add(0x420000000ull,1024);objects=Add(0x430000000ull,2048);
    types=Add(0x440000000ull,1024);mapping=Add(0x450000000ull,1024);
    Put(rm,0x10,rq->address,8);Put(rm,0x18,handles->address,8);Put(rm,0x20,engine+0x75230,8);
    Put(rm,0x28,engine+0x75420,8);Put(rm,0x30,0x348,4);Put(rm,0x34,0x04000004,4);
    Put(rm,0x3C,256,4);Put(rm,0x40,255,4);Put(rm,0x44,0x00FFFE00,4);
    Put(rm,0x48,0xFF000000,4);Put(rm,0x4C,256,4);Put(rm,0x58,handles->address,8);
    Put(rw,0x150,rm->address,8);VecPut(rw,0x168,256,256,objects->address);
    VecPut(rw,0x180,256,256,types->address);VecPut(rw,0x1B8,256,256,mapping->address);
    for(unsigned t=0;t<7;t++) {
        unsigned gi=1+t*9;graphs[t]=Add(0x460000000ull+t*0x1000,24);
        Put(graphs[t],0,engine+0x16880B0,8);Put(graphs[t],8,2,4);Put(graphs[t],0x10,nodeCounts[t],4);Register(gi,graphs[t],2);
        Put(units[t+1],0x64,Handle(gi),4);Put(units[t+1],0x68,rm->address+0x10,8);
        Put(units[t+1],0xC8,count,4);meshEntries[t]=Add(0x490000000ull+t*0x1000,128);
        Put(units[t+1],0xD0,meshEntries[t]->address,8);
        for(unsigned j=0;j<count;j++) {
            unsigned index=gi+j+1;uintptr_t off=(t*8+j)*0x1000ull;
            descriptors[t][j]=Add(0x470000000ull+off,0xDC);meshes[t][j]=Add(0x480000000ull+off,0x78);
            Region *d=descriptors[t][j],*m=meshes[t][j];
            Put(meshEntries[t],j*16,d->address,8);Put(d,4,Handle(index),4);Put(d,8,rm->address+0x10,8);
            Put(d,0x90,units[t+1]->address+0x60,8);Put(d,0x98,j,4);
            Put(m,0,engine+0x1688950,8);Put(m,8,3,4);Put(m,0x30,j,4);Put(m,0x34,Handle(gi),4);
            Put(m,0x38,graphs[t]->address,8);Put(m,0x70,0x600000000ull+off,8); /* Unmapped skin payload. */
            Register(index,m,3);
        }
    }
    groups=Add(0x4A0000000ull,6144);groupList=Add(0x4B0000000ull,2048);
    provider=Add(0x4C0000000ull,0x6C);providerRefs=Add(0x4D0000000ull,16);
    overrideGraph=Add(0x4E0000000ull,24);Put(overrideGraph,0,engine+0x16880B0,8);
    Put(overrideGraph,8,2,4);Put(overrideGraph,0x10,107,4);Register(64,overrideGraph,2);
    Put(provider,0,engine+0x167A478,8);Put(provider,8,13,4);Put(provider,0x18,providerRefs->address,8);Put(provider,0x68,256,4);
    Put(providerRefs,0,100,4);Put(providerRefs,4,Handle(64),4);Put(providerRefs,8,overrideGraph->address,8);
    VecPut(rw,0x3D0,groupCount,groupCount,groups->address);
    for(unsigned i=0;i<groupCount;i++) {
        Put(groups,i*24,provider->address,8);Put(groups,i*24+8,groupList->address,8);Put(groups,i*24+16,1,4);Put(groups,i*24+20,1,1);
    }
    Put(groupList,0,meshes[0][0]->address,8);
}
static void FreshScan(void)
{
    readTick=byteTick=logsTick=logicalTick=0;Scan(&mod);
    if(readTick>maxReads)maxReads=readTick;if(byteTick>maxBytes)maxBytes=byteTick;if(logsTick>maxTickLogs)maxTickLogs=logsTick;
    CHECK(readTick<=READ_TICK_LIMIT && byteTick<=BYTE_TICK_LIMIT && logsTick<=8 && logsTotal<=LOG_LIMIT);
}
static void Run(void){for(unsigned i=0;i<2400 && !Stopped();i++)FreshScan();CHECK(Stopped());}
static void ToStage(unsigned stage){for(unsigned i=0;i<100 && !Stopped() && renderStage<stage;i++)FreshScan();CHECK(renderStage==stage && !Stopped());}
static void Rejected(void){Run();CHECK(!completions && stopLogs==1 && !strstr(lastLog,"reason=complete"));}
static void MutateAt(uintptr_t at,Region *r,unsigned off,uint64_t val,unsigned size)
{changeOnRead=1;mutateAt=at;ownerMutation=6;renderMutationRegion=r;renderMutationOffset=off;renderMutationValue=val;renderMutationSize=size;}
static void SlowOwner(void)
{
    for(unsigned i=0;i<16;i++) {
        Put(networkBuckets,(20+i)*8,i==15?10:1000+i,4);Put(networkBuckets,(20+i)*8+4,1,4);
        Put(entityBuckets,(506+i)*8,i==15?253:2000+i,4);Put(entityBuckets,(506+i)*8+4,1,4);
    }
    Put(manager,0x98,40,4);CHECK(ManagerRead(&initial));
    Put(units[7],0x1E0,0x230008000ull,8);
    for(unsigned i=8;i<=32;i++) {
        units[i]=Add(0x230000000ull+i*0x1000,0x200);Put(units[i],0,engine+VTABLE_RVA,8);
        Put(units[i],0x1D0,units[0]->address,8);Put(units[i],0x1E0,i==32?0:units[i]->address+0x1000,8);
    }
    readTick=byteTick=0;
}
static void SchemaChecks(void)
{
    static const unsigned offsets[]={0x20,0x28,0x30,0x34,0x48,0x58,0x3C,0x18,0x10};
    static const unsigned sizes[]={8,8,4,4,4,8,4,8,8};
    for(unsigned bit=0;bit<9;bit++) {
        Setup(1,1);Put(rm,offsets[bit],bit==6?HANDLE_SLOT_LIMIT+1:0,sizes[bit]);
        if(bit==7)Put(rm,0x58,0,8); /* Only span fails; mirror remains equal. */
        RHeader header;CHECK(!HeaderRead(rm->address,&header));
        CHECK(schemaFailure.pending && schemaFailure.failed==(1u<<bit));
        CHECK(readTick==1 && byteTick==80 && reads==1); /* No failed pointer is followed. */
        CHECK(!memcmp(schemaFailure.raw,rm->bytes+0x10,80));
        Finish(&mod,"complete");CHECK(Stopped() && !completions && schemaLogs==1 && stopLogs==1);
        CHECK(strstr(lastLog,"reason=render_manager_schema") && strstr(lastLog,"partial=1") && strstr(lastLog,"truncated=1"));
        char expected[40];snprintf(expected,sizeof expected,"failed_mask=%08X",1u<<bit);
        CHECK(strstr(lastSchema,expected) && strstr(lastSchema,"checked_mask=000001FF") && strstr(lastSchema,"failed_fields_followed=0"));
        Finish(&mod,"complete");CHECK(schemaLogs==1 && logs==2);
    }
    Setup(1,1);memset(rm->bytes+0x10,0xFF,80);RHeader header;
    CHECK(!HeaderRead(rm->address,&header));CHECK(schemaFailure.failed==0x1DF); /* Both handle pointers equal all-ones: mirror alone passes. */
    CHECK(reads==1 && byteTick==80);schemaFailure.manager=UINTPTR_MAX;Finish(&mod,"complete");CHECK(!completions && schemaLogs==1);
    CHECK(strstr(lastSchema,"capacity=4294967295") && strstr(lastSchema,"handles=FFFFFFFFFFFFFFFF"));
    printf("max-width schema payload: %u bytes\n",(unsigned)strlen(lastSchema));
    Setup(1,1);Put(rm,0x3C,0,4);CHECK(!HeaderRead(rm->address,&header));CHECK(schemaFailure.failed==((1u<<6)|(1u<<7)));
    Setup(1,1);Put(rm,0x3C,1,4);CHECK(!HeaderRead(rm->address,&header));CHECK(schemaFailure.failed==(1u<<6));
    Setup(1,1);Put(rm,0x30,0,4);CHECK(!HeaderRead(rm->address,&header));logsTotal=LOG_LIMIT-1;
    Finish(&mod,"complete");CHECK(Stopped() && schemaFailure.pending && !completions && !schemaLogs && stopLogs==1 && logsTotal==LOG_LIMIT);
    CHECK(strstr(lastLog,"reason=log_budget"));
    Setup(1,1);Put(rm,0x30,0,4);CHECK(!HeaderRead(rm->address,&header));Ended(&mod,HF_EVENT_SESSION_END,NULL);
    Finish(&mod,"complete");CHECK(Stopped() && schemaFailure.pending && !completions && !logs);
    Setup(1,1);Put(rm,0x30,0,4);CHECK(ManagerRead(&initial));
    RLog(&mod,"LEGO_COMPACT_WRITER armed capacity=%u slots_per_tick=%u capacity_limit=%u record_limit=%u deadline_ms=%u",initial.capacity,PER_TICK,CAPACITY_LIMIT,RECORD_LIMIT,ARM_MS);
    Scan(&mod);CHECK(Stopped() && schemaLogs==1 && logsTick==3 && logsTick<=8 && !completions);
}
