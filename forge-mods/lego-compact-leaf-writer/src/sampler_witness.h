/* Included after metadata diagnostic types. All access owns writer.metadata.
   No engine reads, allocation, logging, callbacks or native calls here. */
typedef struct SamplerTrace {
 uint32_t attempted,success,beforeValid,afterState;
 RenderMeta before,after;char underlying[96];
} SamplerTrace;
static SamplerTrace samplerTrace;
static unsigned char samplerWitness[GW_SAMPLER_BYTES];
static void SamplerText(void *destination,size_t capacity,const char *text){
 unsigned char *d=destination;memset(d,0,capacity);if(!text)return;
 size_t i=0;while(i+1<capacity && text[i]){d[i]=(unsigned char)text[i];i++;}
}
static void SamplerReset(void){memset(&samplerTrace,0,sizeof samplerTrace);memset(samplerWitness,0,sizeof samplerWitness);}
static void SamplerCopy(unsigned char *out,void *user){(void)user;memcpy(out,samplerWitness,GW_SAMPLER_BYTES);}
static void SW32(unsigned char *p,size_t at,uint32_t value){memcpy(p+at,&value,4);}
static void SW64(unsigned char *p,size_t at,uint64_t value){memcpy(p+at,&value,8);}
static void SWVec(unsigned char *p,const Vec *v){SW32(p,0,v->length);SW32(p,4,v->capacity);SW64(p,8,v->data);}
static void SWHeader(unsigned char *p,const RHeader *h){
 SW64(p,0,h->manager);SW64(p,8,h->queue);SW64(p,16,h->world);SW64(p,24,h->handles);SW32(p,32,h->capacity);SW32(p,36,h->mask);SW32(p,40,h->generation);SW32(p,44,h->live);
 SWVec(p+48,&h->objects);SWVec(p+64,&h->types);SWVec(p+80,&h->map);
}
static void SWResolved(unsigned char *p,const Resolved *r){SW32(p,0,r->handle);SW32(p,4,r->index);SW32(p,8,r->type);SW32(p,12,r->nodes);SW64(p,16,r->object);SW64(p,24,r->vtable);}
static void SWMeta(unsigned char *p,const RenderMeta *m){
 SWHeader(p,&m->header);SWResolved(p+96,&m->graph);SW64(p,128,m->meshEntries);SW32(p,136,m->graphHandle);SW32(p,140,m->meshes);SW32(p,144,m->registeredMeshes);SW32(p,148,m->unregisteredMeshes);
 for(unsigned i=0;i<MESH_LIMIT;i++){unsigned char *q=p+152+96*i;const MeshMeta *v=&m->mesh[i];
  SW64(q,0,v->descriptor);SW64(q,8,v->association);SW64(q,16,v->sourceGraph);SW64(q,24,v->skin);SW32(q,32,v->handle);SW32(q,36,v->node);SW32(q,40,v->flags);SW32(q,44,v->registered);
  SWResolved(q+48,&v->resolved);SW32(q,80,v->ref.node);SW32(q,84,v->ref.handle);SW64(q,88,v->ref.cache);
 }
}
static void SWIdentity(unsigned char *p,const Identity *v){SW64(p,0,v->unit);SW64(p,8,v->resourceAddress);SW64(p,16,v->resource);SW32(p,24,v->index);SW32(p,28,v->generation);}
static void SWResolve(unsigned char *p,const ResolveTrace *r){
 SW32(p,0,r->attempted);SW32(p,4,r->valid);SW32(p,8,r->checked);SW32(p,12,r->failed);SW32(p,16,r->slot);SW32(p,20,r->requestedType);SW32(p,24,r->headerType);SWResolved(p+32,&r->r);
}
static void SamplerSnapshot(int32_t status){
 unsigned char *o=samplerWitness;memset(o,0,GW_SAMPLER_BYTES);memcpy(o,"LGSMPL01",8);SW32(o,8,1);SW32(o,12,GW_SAMPLER_BYTES);
 unsigned flags=1u|(samplerTrace.attempted?2u:0u)|(samplerTrace.success?4u:0u)|(samplerTrace.beforeValid?8u:0u)|
  (schemaFailure.pending?16u:0u)|(routeFailure.pending?32u:0u)|(renderRefusal.pending?64u:0u)|(consistencyRefusal.pending?128u:0u)|
  (resolveTrace.attempted?256u:0u)|(budgetFailure?512u:0u)|(samplerTrace.afterState?1024u:0u);
 SW32(o,16,flags);SW32(o,20,(uint32_t)status);SW32(o,24,samplerTrace.attempted);SW32(o,28,samplerTrace.success);SW32(o,32,samplerTrace.beforeValid);SW32(o,36,samplerTrace.afterState);SW32(o,40,renderStage);SW32(o,44,(uint32_t)budgetFailure);
 SamplerText(o+64,96,samplerTrace.underlying);SamplerText(o+160,96,renderFailure);SWMeta(o+256,&samplerTrace.before);SWMeta(o+1280,&samplerTrace.after);
 unsigned char *p=o+2304;SW64(p,0,schemaFailure.manager);memcpy(p+8,schemaFailure.raw,80);SW32(p,88,schemaFailure.failed);SW32(p,92,(uint32_t)schemaFailure.pending);SW32(p,96,(uint32_t)schemaFailure.logged);
 p=o+2432;SWHeader(p,&routeFailure.h);memcpy(p+96,routeFailure.queue,136);memcpy(p+232,routeFailure.world,32);SW64(p,264,routeFailure.back);
 SW32(p,272,routeFailure.stage);SW32(p,276,routeFailure.attempted);SW32(p,280,routeFailure.valid);SW32(p,284,routeFailure.checked);SW32(p,288,routeFailure.failed);SW32(p,292,(uint32_t)routeFailure.pending);SW32(p,296,(uint32_t)routeFailure.logged);
 p=o+2816;SW64(p,0,renderRefusal.unit);SW64(p,8,renderRefusal.association);SW64(p,16,renderRefusal.expectedSource);SW64(p,24,renderRefusal.resource);
 SW32(p,32,renderRefusal.unitNodes);SW32(p,36,renderRefusal.ordinal);SW32(p,40,renderRefusal.stage);SW32(p,44,renderRefusal.attempted);SW32(p,48,renderRefusal.valid);SW32(p,52,renderRefusal.checked);SW32(p,56,renderRefusal.failed);
 SW32(p,60,renderRefusal.resolveKind);SW32(p,64,renderRefusal.headerValid);SW32(p,68,renderRefusal.graphValid);SW32(p,72,renderRefusal.conflictOrdinal);SW32(p,76,(uint32_t)renderRefusal.pending);SW32(p,80,(uint32_t)renderRefusal.logged);
 SWMeta(p+96,&renderRefusal.meta);SWResolve(p+1120,&renderRefusal.resolve);
 p=o+4096;SWHeader(p,&consistencyRefusal.oldHeader);SWHeader(p+96,&consistencyRefusal.newHeader);SWIdentity(p+192,&consistencyRefusal.oldParent);SWIdentity(p+240,&consistencyRefusal.newParent);
 SW64(p,288,consistencyRefusal.mask);SW64(p,296,consistencyRefusal.ignored);SW64(p,304,consistencyRefusal.oldValue);SW64(p,312,consistencyRefusal.newValue);
 SW32(p,320,consistencyRefusal.target);SW32(p,324,consistencyRefusal.prior);SW32(p,328,consistencyRefusal.offset);SW32(p,332,consistencyRefusal.width);SW32(p,336,consistencyRefusal.differences);SW32(p,340,consistencyRefusal.stage);
 SW32(p,344,(uint32_t)consistencyRefusal.pending);SW32(p,348,(uint32_t)consistencyRefusal.logged);SW32(p,352,(uint32_t)consistencyRefusal.headers);SW32(p,356,(uint32_t)consistencyRefusal.parents);
 SWResolve(o+4736,&resolveTrace);SamplerText(o+4864,64,consistencyRefusal.kind);SamplerText(o+4928,96,consistencyRefusal.reason);SamplerText(o+5024,96,routeFailure.reason);SamplerText(o+5120,96,renderRefusal.reason);
}
