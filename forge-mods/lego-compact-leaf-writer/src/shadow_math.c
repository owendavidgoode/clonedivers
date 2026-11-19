#include "shadow_math.h"
#include "pinned_data.h"
#include <string.h>
#include <math.h>
#include <xmmintrin.h>
static uint32_t sm_u32(const unsigned char *p){uint32_t x;memcpy(&x,p,4);return x;}
static uint64_t sm_u64(const unsigned char *p){uint64_t x;memcpy(&x,p,8);return x;}
static void sm_mat(cm_mat4 *m,const cm_render48 *r){memset(m,0,sizeof *m);for(unsigned i=0;i<12;i++)m->v[i]=r->v[i];m->v[15]=1.;}
static cm_status sm_inverse(const cm_render48 *r,cm_mat4 *out) {
 double a=r->v[0],b=r->v[1],c=r->v[2],d=r->v[4],e=r->v[5],f=r->v[6],g=r->v[8],h=r->v[9],i=r->v[10];
 for(unsigned n=0;n<12;n++)if(!isfinite(r->v[n]))return CM_NONFINITE;
 double det=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);
 if(!isfinite(det))return CM_NONFINITE;
 if(det==0)return CM_SINGULAR;
 cm_mat4 v={{(e*i-f*h)/det,(c*h-b*i)/det,(b*f-c*e)/det,0,
             (f*g-d*i)/det,(a*i-c*g)/det,(c*d-a*f)/det,0,
             (d*h-e*g)/det,(b*g-a*h)/det,(a*e-b*d)/det,0,0,0,0,1}};
 for(unsigned j=0;j<3;j++)v.v[j*4+3]=-(v.v[j*4]*r->v[3]+v.v[j*4+1]*r->v[7]+v.v[j*4+2]*r->v[11]);
 for(unsigned n=0;n<16;n++)if(!isfinite(v.v[n]))return CM_NONFINITE;
 *out=v;return CM_OK;
}
cm_status sm_prepare(cm_rig *rig) {
 uint32_t incoming=_mm_getcsr();_mm_setcsr(0x1f80);
 const double identity[9]={1,0,0,0,1,0,0,0,1};cm_status status=CM_POLICY;
 int identity100=1;for(unsigned n=0;n<16;n++)if(cm_native_rest[100].v[n]!=(n%5==0?1.:0.))identity100=0;
 if(identity100)status=cm_prepare(rig,identity,1,CM_NECK_AND_ARM_SHOULDER_HINGE);
 _mm_setcsr(incoming);return status;
}
void sm_evaluate(const cm_rig *rig,const sm_call *call,const unsigned char *p,size_t bytes,cm_workspace *work,sm_result *out) {
 if(!out)return;
 memset(out,0,sizeof *out);out->current_status=out->previous_status=UINT32_MAX;
 uint32_t before=_mm_getcsr();out->entry_mxcsr=call?call->entry_mxcsr:before;
 /* Always restore the actual detour entry value supplied by the envelope;
    sampler Win32 calls may have changed the current CSR before this callback. */
 uint32_t restore=call?call->entry_mxcsr:before;
 if(!call||!p||bytes!=SM_RAW_BYTES||!rig||!work)goto done;
 uint32_t gi=0;
 if(!cm_garment_find(call->resource,&gi)||call->ordinal<1||call->ordinal>4||call->engine>UINTPTR_MAX-0x258aab||
    !((call->caller==call->engine+0x258aab && call->graph && call->graph==sm_u64(p+120)) ||
      (call->caller==call->engine+0x258e19 && !call->graph))||
    sm_u64(p+120)!=sm_u64(p+152)||!sm_u64(p+120)||
    sm_u64(p+144)>UINTPTR_MAX-0x318||call->pool!=sm_u64(p+144)+0x318||
    sm_u64(p+88)!=call->resource||sm_u32(p+4)!=0x007fff7fu||sm_u32(p+12)!=0x7fu)goto done;
 const cm_garment *garment=cm_garment_at(gi);uint32_t count=sm_u32(p+184),previous=sm_u32(p+188);
 uint32_t node=sm_u32(p+112);uint32_t expected_node=cm_mesh_node_for(gi,call->ordinal);
 if(!garment||!count||count>SM_CAP||count!=garment->count||previous!=count||
    sm_u32(p+192)!=107||sm_u32(p+196)!=107||expected_node==UINT32_MAX||node!=expected_node||node>=count)goto done;
 out->route_ok=1;_mm_setcsr(0x1f80);out->math_mxcsr=_mm_getcsr();
 out->scope_verified=(out->math_mxcsr&0xffc0u)==0x1f80u;
 const cm_render48 *parents[2]={(const cm_render48*)(p+13312),(const cm_render48*)(p+18448)};
 const cm_render48 *graphs[2]={(const cm_render48*)(p+1024),(const cm_render48*)(p+7168)};
 cm_render48 *outputs[2]={out->current,out->previous};uint32_t statuses[2]={UINT32_MAX,UINT32_MAX};
 for(unsigned temporal=0;temporal<2;temporal++) {
  cm_mat4 h,f;cm_status status=sm_inverse(&parents[temporal][100],&h);
  if(status==CM_OK){sm_mat(&f,&graphs[temporal][node]);cm_frame frame={parents[temporal],107,graphs[temporal],count,gi,node,&h,&f,1};
   status=cm_evaluate(rig,&frame,work,outputs[temporal],SM_CAP);}
  statuses[temporal]=(uint32_t)status;
 }
 out->current_status=statuses[0];out->previous_status=statuses[1];
 if(statuses[0]||statuses[1])goto done;
 out->protected_rows=out->mesh_rows=1;
 for(unsigned temporal=0;temporal<2;temporal++) {
  for(unsigned n=0;n<count;n++) {
   int alias=0;for(unsigned a=0;a<garment->alias_count;a++)if(garment->aliases[a].node==n)alias=1;
   if(!alias&&memcmp(&outputs[temporal][n],&graphs[temporal][n],48))out->protected_rows=0;
  }
  if(memcmp(&outputs[temporal][node],&graphs[temporal][node],48))out->mesh_rows=0;
 }
 uintptr_t now=(uintptr_t)out->current,old=(uintptr_t)out->previous;
 memcpy(out->graph_header+0x10,&count,4);memcpy(out->graph_header+0x18,&now,8);
 memcpy(out->graph_header+0x28,&count,4);memcpy(out->graph_header+0x30,&old,8);
 out->header_ok=((uintptr_t)out->graph_header%16==0)&&sm_u32(out->graph_header+0x10)==count&&
  sm_u32(out->graph_header+0x28)==count&&sm_u64(out->graph_header+0x18)==now&&sm_u64(out->graph_header+0x30)==old;
done:
 _mm_setcsr(restore);out->restored_mxcsr=_mm_getcsr();
 out->math_ok=out->route_ok&&out->scope_verified&&out->current_status==CM_OK&&out->previous_status==CM_OK&&
  out->protected_rows&&out->mesh_rows&&out->header_ok&&out->restored_mxcsr==out->entry_mxcsr;
}
