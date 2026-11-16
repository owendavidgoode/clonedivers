/* Tick-only serialization/I/O; producer only publishes resident bounded data. */
#define WF_BYTES 81920u
static unsigned char writerFaultWire[WF_BYTES];
static unsigned writerFaultPendingReported;
_Static_assert(sizeof(sm_result)==12416,"Fault wire math layout");
_Static_assert(sizeof(gw_receipt)==48,"Fault wire leaf receipt layout");
_Static_assert(offsetof(lp_plan,trace)==8968,"Fault wire plan prefix layout");
static unsigned WriterFaultClass(const gw_fault *f){
 if(f->reason==GW_CAPTURE)return f->record.capture_status==-1002?2:1;
 return f->reason==GW_RECHECK?3:4;
}
static void WriterFaultSerialize(const gw_fault *f){
 unsigned char *o=writerFaultWire;const se_record *r=&f->record;const lp_plan *p=&f->plan;const lp_trace *t=&p->trace;
 memset(o,0,WF_BYTES);InvocationHeader(o,r);memcpy(o,"LGFAULT3",8);IvU32(o,8,3);IvU32(o,16,WF_BYTES);IvU32(o,44,(uint32_t)f->reason);
 IvU32(o,224,1);IvU32(o,228,73728);IvU32(o,232,GW_SAMPLER_BYTES);IvU32(o,236,U32(f->sampler+16));
 memcpy(o+73728,f->sampler,GW_SAMPLER_BYTES);
 /* A skipped/no-capture sampler has an explicit empty appendix, never stale data. */
 memcpy(o+73728,"LGSMPL01",8);IvU32(o+73728,8,1);IvU32(o+73728,12,GW_SAMPLER_BYTES);
 IvU32(o,152,r->payload_used);IvU32(o,156,r->delegate_mxcsr);IvU32(o,160,WriterFaultClass(f));
 IvU32(o,164,(uint32_t)f->sampler_status);IvU32(o,168,f->sampler_used);
 IvU32(o,172,t->phase);IvU32(o,176,t->stage);IvU32(o,180,t->error);IvU32(o,184,t->read_outcome);
 IvU32(o,188,t->phase?t->mismatch_index:UINT32_MAX);IvU64(o,192,t->read_at);IvU32(o,200,t->read_size);
 IvU32(o,204,p->blocks);IvU32(o,208,p->bytes);IvU32(o,212,t->comparison_length);IvU32(o,216,t->probe_offset);IvU32(o,220,t->probe_bytes);
 memcpy(o+256,r->payload,SM_RAW_BYTES);memcpy(o+46400,r->payload+SM_RAW_BYTES,sizeof(sm_result));
 memcpy(o+58816,r->payload+GW_RECEIPT_OFFSET,sizeof(gw_receipt));
 unsigned char *q=o+58864;
 IvU64(q,0,p->graph);IvU64(q,8,p->arrays[0]);IvU64(q,16,p->arrays[1]);IvU64(q,24,p->bind);IvU64(q,32,p->map);IvU64(q,40,p->pool_base);
 IvU32(q,48,p->count);IvU32(q,52,p->mesh_node);IvU32(q,56,p->groups);IvU32(q,60,p->output_count);IvU32(q,64,p->blocks);IvU32(q,68,p->bytes);
 for(unsigned i=0;i<LP_GROUPS;i++){unsigned at=72+24*i;IvU64(q,at,p->group[i].palette);IvU32(q,at+8,p->group[i].count);IvU32(q,at+12,p->group[i].offset[0]);IvU32(q,at+16,p->group[i].offset[1]);}
 for(unsigned i=0;i<p->blocks && i<LP_BLOCKS;i++){unsigned at=264+16*i;IvU64(q,at,p->block[i].at);IvU32(q,at+8,p->block[i].offset);IvU32(q,at+12,p->block[i].bytes);}
 if(p->bytes<=LP_BYTES)memcpy(q+776,p->evidence,p->bytes);
 memcpy(o+67840,t->expected,64);memcpy(o+67904,t->observed,64);
}
static int WriterFaultWrite(void){
 const wchar_t *basename=L"leaf-fault-first.bin";
 wchar_t path[1200],expected[1200],actual[1200],directoryNow[1100];
 DWORD n=GetFinalPathNameByHandleW(invocationDirectoryHandle,directoryNow,1100,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
 if(!n||n>=1100)return 0;while(n&&directoryNow[n-1]==L'\\')directoryNow[--n]=0;
 if(wcscmp(directoryNow,invocationDirectoryFinal)||swprintf(path,1200,L"%ls\\%ls",invocationDirectory,basename)<0||
    swprintf(expected,1200,L"%ls\\%ls",invocationDirectoryFinal,basename)<0)return 0;
 HANDLE file=CreateFileW(path,GENERIC_WRITE|FILE_READ_ATTRIBUTES,FILE_SHARE_READ,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
 if(file==INVALID_HANDLE_VALUE)return 0;
 BY_HANDLE_FILE_INFORMATION info;n=GetFinalPathNameByHandleW(file,actual,1200,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
 int ok=n && n<1200 && !wcscmp(actual,expected) && GetFileInformationByHandle(file,&info) &&
  !(info.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) && info.nNumberOfLinks==1 && !info.nFileSizeHigh && !info.nFileSizeLow;
 DWORD written=0;if(ok)ok=WriteFile(file,writerFaultWire,WF_BYTES,&written,NULL)&&written==WF_BYTES;
 LARGE_INTEGER size;if(ok)ok=GetFileSizeEx(file,&size)&&size.QuadPart==WF_BYTES;
 int closed=CloseHandle(file)!=0;return ok&&closed;
}
static void WriterFaultTick(HfMod *mod){
 gw_fault *f=&writer.fault;LONG state=InterlockedCompareExchange(&f->state,0,0);
 if(state==4){
  WriterFaultSerialize(f);int ok=WriterFaultWrite();InterlockedExchange(&f->state,ok?5:6);
  if(ok)hf->log(mod,"LEGO_COMPACT_LEAF_WRITER fault_evidence file=leaf-fault-first.bin bytes=81920 reason=%ld sequence=%llu classification=%u capture_status=%ld plan_stage=%u plan_error=%u complete=1",f->reason,(unsigned long long)f->sequence,WriterFaultClass(f),(long)f->record.capture_status,f->plan.trace.stage,f->plan.trace.error);
  else hf->log(mod,"LEGO_COMPACT_LEAF_WRITER fault_write_failed reason=%ld sequence=%llu",f->reason,(unsigned long long)f->sequence);
 }else if(state<4 && !writerFaultPendingReported && InterlockedCompareExchange(&writer.fatal,0,0)){
  writerFaultPendingReported=1;unsigned known=state>=2;
  hf->log(mod,"LEGO_COMPACT_LEAF_WRITER fault_pending state=%ld reason=%ld sequence=%llu record_owned=%u identity_ready=%u",state,known?f->reason:0,(unsigned long long)(known?f->sequence:0),known?f->record_owned:0,known);
 }
}
