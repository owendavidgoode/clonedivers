#include "compact_math.h"
#include "pinned_data.h"
#include <math.h>
#include <float.h>
#include <string.h>
#include <xmmintrin.h>

#define CM_MAGIC 0x434D0001u
#define TRY(expr) do { cm_status s_=(expr); if(s_!=CM_OK) return s_; } while(0)
_Static_assert(sizeof(cm_render48)==48, "render48 ABI");
_Static_assert(sizeof(cm_mat4)==128, "mat4 ABI");

static int overlaps(const void *a,size_t an,const void *b,size_t bn) {
 uintptr_t x=(uintptr_t)a,y=(uintptr_t)b;
 if(an>UINTPTR_MAX-x || bn>UINTPTR_MAX-y) return 1;
 return x<y+bn && y<x+an;
}
static cm_status environment(void) {
 /* Ignore sticky exception flags, require all masks and the qualified mode. */
 return (_mm_getcsr() & 0xffc0u)==0x1f80u ? CM_OK : CM_NUMERICAL;
}
static cm_status finite64(const double *a,size_t n) {
 for(size_t i=0;i<n;i++) if(!isfinite(a[i])) return CM_NONFINITE;
 return CM_OK;
}
static cm_status finite32(const float *a,size_t n) {
 for(size_t i=0;i<n;i++) {
  if(!isfinite(a[i])) return CM_NONFINITE;
 }
 return CM_OK;
}
static cm_status finite_rows(const cm_render48 *a,size_t n) {
 for(size_t i=0;i<n;i++) TRY(finite32(a[i].v,12));
 return CM_OK;
}
static double det(const double a[9]) {
 return a[0]*(a[4]*a[8]-a[5]*a[7])-a[1]*(a[3]*a[8]-a[5]*a[6])+a[2]*(a[3]*a[7]-a[4]*a[6]);
}
static cm_status inverse(const double a[9],double b[9]) {
 double d=det(a);
 if(!isfinite(d)) return CM_NONFINITE;
 if(fabs(d)<1e-15) return CM_SINGULAR;
 b[0]=(a[4]*a[8]-a[5]*a[7])/d; b[1]=(a[2]*a[7]-a[1]*a[8])/d; b[2]=(a[1]*a[5]-a[2]*a[4])/d;
 b[3]=(a[5]*a[6]-a[3]*a[8])/d; b[4]=(a[0]*a[8]-a[2]*a[6])/d; b[5]=(a[2]*a[3]-a[0]*a[5])/d;
 b[6]=(a[3]*a[7]-a[4]*a[6])/d; b[7]=(a[1]*a[6]-a[0]*a[7])/d; b[8]=(a[0]*a[4]-a[1]*a[3])/d;
 return finite64(b,9);
}
static void linear(const cm_mat4 *a,double b[9]) {
 for(int r=0;r<3;r++) for(int c=0;c<3;c++) b[r*3+c]=a->v[r*4+c];
}
static void putlinear(cm_mat4 *a,const double b[9]) {
 for(int r=0;r<3;r++) for(int c=0;c<3;c++) a->v[r*4+c]=b[r*3+c];
}
static void multiply(const double a[9],const double b[9],double out[9]) {
 double t[9];
 for(int r=0;r<3;r++) for(int c=0;c<3;c++)
  t[r*3+c]=(a[r*3]*b[c]+a[r*3+1]*b[3+c])+a[r*3+2]*b[6+c];
 memcpy(out,t,sizeof t);
}
static double gram(const double a[9]) {
 double e=0;
 for(int r=0;r<3;r++) for(int c=0;c<3;c++) {
  double v=(a[r]*a[c]+a[3+r]*a[3+c])+a[6+r]*a[6+c]-(r==c?1.:0.);
  if(fabs(v)>e) e=fabs(v);
 }
 return e;
}
static cm_status affine(const cm_mat4 *m) {
 TRY(finite64(m->v,16));
 if(m->v[12]!=0 || m->v[13]!=0 || m->v[14]!=0 || m->v[15]!=1) return CM_BASIS;
 return CM_OK;
}
static cm_status rigid(const cm_mat4 *m,double tolerance) {
 double a[9]; TRY(affine(m)); linear(m,a);
 if(det(a)<=0) return CM_REFLECTION;
 if(gram(a)>tolerance) return CM_BASIS;
 return CM_OK;
}
static cm_status split(const cm_mat4 *m,double r[9],double scale[3]) {
 TRY(affine(m));
 for(int c=0;c<3;c++) {
  scale[c]=sqrt((m->v[c]*m->v[c]+m->v[4+c]*m->v[4+c])+m->v[8+c]*m->v[8+c]);
  if(!isfinite(scale[c])) return CM_NONFINITE;
  if(scale[c]<=1e-8) return CM_SINGULAR;
  for(int j=0;j<3;j++) r[j*3+c]=m->v[j*4+c]/scale[c];
 }
 double d=det(r);
 if(d<=0) return CM_REFLECTION;
 if(gram(r)>1e-6 || fabs(d-1)>2e-6) return CM_SHEAR;
 return CM_OK;
}
static void identity(cm_mat4 *m) {
 memset(m,0,sizeof *m); m->v[0]=m->v[5]=m->v[10]=m->v[15]=1;
}
static cm_status transformed(const cm_mat4 *frame,const cm_render48 *in,cm_mat4 *out) {
 identity(out);
 for(int r=0;r<3;r++) for(int c=0;c<4;c++) {
  out->v[4*r+c]=(frame->v[4*r]*in->v[c]+frame->v[4*r+1]*in->v[4+c])+frame->v[4*r+2]*in->v[8+c];
  if(c==3) out->v[4*r+c]+=frame->v[4*r+3];
 }
 return finite64(out->v,16);
}
static cm_status to_rows(const cm_mat4 *in,cm_render48 *out) {
 TRY(affine(in));
 for(int i=0;i<12;i++) {
  if(!isfinite(in->v[i]) || fabs(in->v[i])>FLT_MAX) return CM_NONFINITE;
  float fv = (fabs(in->v[i])<(double)FLT_MIN) ? 0.f : (float)in->v[i];
  if(!isfinite(fv)) return CM_NONFINITE;
  if(fabsf(fv)<FLT_MIN) fv = 0.f;
  out->v[i]=fv;
 }
 return finite32(out->v,12);
}
static cm_status op_mul(float a,float b,float *out) {
 if(!isfinite(a) || !isfinite(b)) return CM_NONFINITE;
 if(fabsf(a)<FLT_MIN) a=0.f;
 if(fabsf(b)<FLT_MIN) b=0.f;
 volatile float v=a*b;
 float fv=v;
 if(!isfinite(fv)) return CM_NONFINITE;
 if(fabsf(fv)<FLT_MIN) fv=0.f;
 *out=fv;
 return CM_OK;
}
static cm_status op_add(float a,float b,float *out) {
 if(!isfinite(a) || !isfinite(b)) return CM_NONFINITE;
 if(fabsf(a)<FLT_MIN) a=0.f;
 if(fabsf(b)<FLT_MIN) b=0.f;
 volatile float v=a+b;
 float fv=v;
 if(!isfinite(fv)) return CM_NONFINITE;
 if(fabsf(fv)<FLT_MIN) fv=0.f;
 *out=fv;
 return CM_OK;
}
CM_API cm_status cm_compose(const cm_render48 *left,const cm_render48 *right,cm_render48 *out) {
 if(!left || !right || !out) return CM_ARGUMENT;
 TRY(environment()); TRY(finite32(left->v,12)); TRY(finite32(right->v,12));
 cm_render48 result;
 for(int r=0;r<3;r++) for(int c=0;c<4;c++) {
  float x,y,z,w,t;
  TRY(op_mul(left->v[r*4],right->v[c],&x));
  TRY(op_mul(left->v[r*4+1],right->v[4+c],&y));
  TRY(op_mul(left->v[r*4+2],right->v[8+c],&z));
  TRY(op_mul(left->v[r*4+3],c==3?1.f:0.f,&w));
  TRY(op_add(y,x,&t)); TRY(op_add(t,z,&t)); TRY(op_add(t,w,&result.v[r*4+c]));
 }
 memcpy(out,&result,sizeof result); return CM_OK;
}
CM_API const cm_garment *cm_garment_at(uint32_t index) {return index<7?&cm_garments[index]:cm_dynamic_garment_at(index);}

CM_API cm_status cm_prepare(cm_rig *out,const double basis[9],uint32_t known,cm_policy policy) {
 if(!out || !basis) return CM_ARGUMENT;
 if(overlaps(out,sizeof *out,basis,9*sizeof(double))) return CM_OVERLAP;
 out->magic=0;
 TRY(environment());
 if(known!=1) return CM_BASIS_UNKNOWN;
 if(policy!=CM_INHERIT_HELPERS && policy!=CM_HEADPEG_ONLY && policy!=CM_YAW_TORSO_NECK && policy!=CM_NECK_AIM_ONLY && policy!=CM_ARM_SHOULDER_HINGE && policy!=CM_NECK_AND_ARM_SHOULDER_HINGE) return CM_POLICY;
 TRY(finite64(basis,9));
 if(gram(basis)>=1e-10 || fabs(det(basis)-1)>=1e-10) return CM_BASIS;
 memcpy(out->basis,basis,9*sizeof(double)); TRY(inverse(basis,out->basis_inverse));
 out->policy=(uint32_t)policy;
 for(int i=0;i<65;i++) {
  double r[9],scale[3]; TRY(split(&cm_source_rest[i],r,scale));
  for(int c=0;c<3;c++) if(fabs(scale[c]-1)>2e-6) return CM_SCALE;
  int p=cm_parents[i],a=cm_anchors[i];
  if((i==0 && (p!=-1 || cm_helpers[i] || a<0)) || (i>0 && (p<0 || p>=i)) || a< -1 || a>=107) return CM_MAPPING;
  out->source[i]=out->local[i]=cm_source_rest[i];
  if(p>=0) {
   double pr[9],inv[9],own[9]; linear(&cm_source_rest[p],pr); TRY(inverse(pr,inv));
   linear(&cm_source_rest[i],r); multiply(inv,r,own); putlinear(&out->local[i],own);
   for(int row=0;row<3;row++) {
    double v=0;
    for(int c=0;c<3;c++) v+=inv[3*row+c]*(cm_source_rest[i].v[4*c+3]-cm_source_rest[p].v[4*c+3]);
    out->local[i].v[4*row+3]=v;
   }
  }
 }
 for(int i=0;i<107;i++) {
  double r[9]; TRY(split(&cm_native_rest[i],r,out->native_scale[i]));
  TRY(inverse(r,out->native_inverse[i]));
 }
 if(!cm_helpers[13] || cm_anchors[13]!=90) return CM_MAPPING;
 out->magic=CM_MAGIC; return CM_OK;
}

static cm_status buffers(const cm_rig *rig,const cm_frame *frame,cm_workspace *work,cm_render48 *out,size_t capacity) {
 if(!rig || !frame || !work || !out) return CM_ARGUMENT;
 if(!frame->parent || !frame->graph || !frame->parent_graph_to_native_rest_model || !frame->source_model_to_selected_graph) return CM_ARGUMENT;
 if(frame->parent_count!=107 || frame->graph_count==0 || frame->graph_count>512 || capacity<frame->graph_count || capacity>512) return CM_COUNT;
 const void *ptrs[]={rig,frame,frame->parent,frame->graph,frame->parent_graph_to_native_rest_model,frame->source_model_to_selected_graph};
 size_t sizes[]={sizeof *rig,sizeof *frame,frame->parent_count*48,frame->graph_count*48,sizeof(cm_mat4),sizeof(cm_mat4)};
 if(overlaps(work,sizeof *work,out,capacity*48)) return CM_OVERLAP;
 for(int i=0;i<6;i++) if(overlaps(work,sizeof *work,ptrs[i],sizes[i]) || overlaps(out,capacity*48,ptrs[i],sizes[i])) return CM_OVERLAP;
 return CM_OK;
}
/* Opt-in source-pose policy. No game pointers, allocation, IO, environment
 * mutation, or output commit. A failed policy leaves only workspace scratch. */
static cm_status contact_policy(const cm_rig *rig,cm_workspace *work) {
 if(cm_parents[12]!=5 || cm_parents[13]!=12 || !cm_helpers[12] || !cm_helpers[13]) return CM_MAPPING;
 for(int i=0;i<65;i++) if((cm_parents[i]==12 && i!=13) || cm_parents[i]==13) return CM_MAPPING;
 double hip[9],chest[9],rest[9],inv[9],hipdelta[9],chestdelta[9],relative[9],delta[9];
 linear(&work->world[1],hip); linear(&rig->source[1],rest); TRY(inverse(rest,inv)); multiply(hip,inv,hipdelta);
 linear(&work->world[5],chest); linear(&rig->source[5],rest); TRY(inverse(rest,inv)); multiply(chest,inv,chestdelta);
 TRY(inverse(hipdelta,inv)); multiply(inv,chestdelta,relative);
 double projection=sqrt(relative[0]*relative[0]+relative[3]*relative[3]);
 if(!isfinite(projection)) return CM_NONFINITE;
 if(projection<1e-8) return CM_POLICY;
 double yaw=atan2(relative[3],relative[0]),c=cos(yaw),s=sin(yaw);
 double yawrotation[9]={c,-s,0,s,c,0,0,0,1}; multiply(hipdelta,yawrotation,delta);
 for(int i=0;i<65;i++) {
  int p=cm_parents[i]; double target[9],pr[9],own[9];
  if(i>=2 && i<=5) {linear(&rig->source[i],rest); multiply(delta,rest,target);}
  else if(cm_helpers[i] && i!=13) {linear(&work->world[p],pr); linear(&rig->local[i],own); multiply(pr,own,target);}
  else linear(&work->world[i],target);
  work->local[i]=rig->local[i]; putlinear(&work->world[i],target);
  if(p>=0) {linear(&work->world[p],pr); TRY(inverse(pr,inv)); multiply(inv,target,own); putlinear(&work->local[i],own);}
  for(int row=0;row<3;row++) {
   double value=rig->local[i].v[row*4+3];
   if(p>=0) {value=0;for(int k=0;k<3;k++) value+=work->world[p].v[row*4+k]*rig->local[i].v[k*4+3];value+=work->world[p].v[row*4+3];}
   work->world[i].v[row*4+3]=value;
  }
 }
 double a[3],b[3],aa=0,bb=0;
 for(int i=0;i<3;i++) {a[i]=work->world[13].v[i*4+3]-work->world[12].v[i*4+3];b[i]=work->world[7].v[i*4+3]-work->world[12].v[i*4+3];aa+=a[i]*a[i];bb+=b[i]*b[i];}
 aa=sqrt(aa);bb=sqrt(bb);
 if(!isfinite(aa) || !isfinite(bb)) return CM_NONFINITE;
 if(aa<1e-8 || bb<1e-8 || fabs(aa-bb)>1e-6) return CM_POLICY;
 for(int i=0;i<3;i++) {a[i]/=aa;b[i]/=bb;}
 double dot=(a[0]*b[0]+a[1]*b[1])+a[2]*b[2];
 if(dot< -1+1e-9) return CM_POLICY;
 double cross[3]={a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
 double k[9]={0,-cross[2],cross[1],cross[2],0,-cross[0],-cross[1],cross[0],0},square[9],aim[9];
 multiply(k,k,square);
 for(int i=0;i<9;i++) aim[i]=(i%4==0?1.:0.)+k[i]+square[i]/(1+dot);
 linear(&work->world[12],rest);multiply(aim,rest,delta);putlinear(&work->world[12],delta);
 linear(&work->world[5],rest);TRY(inverse(rest,inv));multiply(inv,delta,relative);putlinear(&work->local[12],relative);
 TRY(inverse(delta,inv));linear(&work->world[13],rest);multiply(inv,rest,relative);putlinear(&work->local[13],relative);
 for(int row=0;row<3;row++) {
  double value=0;for(int i=0;i<3;i++) value+=work->world[12].v[row*4+i]*rig->local[13].v[i*4+3];
  work->world[13].v[row*4+3]=value+work->world[12].v[row*4+3];
 }
 for(int i=0;i<65;i++) {
  TRY(finite64(work->local[i].v,16)); TRY(finite64(work->world[i].v,16));
  cm_render48 packed;TRY(to_rows(&work->world[i],&packed));cm_mat4 quantized;identity(&quantized);
  for(int j=0;j<12;j++) quantized.v[j]=packed.v[j];TRY(rigid(&quantized,2e-6));
 }
 return CM_OK;
}
/* Separate opt-in branch: do not rebuild or alter any torso/arm chain.
 * Policies 0/1/2 retain their original source path above. */
#include "neck_interval.h"
static cm_status neck_policy(const cm_rig *rig,cm_workspace *work) {
 if(cm_parents[12]!=5 || cm_parents[13]!=12 || !cm_helpers[12] || !cm_helpers[13]) return CM_MAPPING;
 for(int i=0;i<65;i++) if((cm_parents[i]==12 && i!=13) || cm_parents[i]==13) return CM_MAPPING;
 double a[3],b[3],aa=0,bb=0;
 for(int i=0;i<3;i++) {a[i]=work->world[13].v[i*4+3]-work->world[12].v[i*4+3];b[i]=work->world[7].v[i*4+3]-work->world[12].v[i*4+3];aa+=a[i]*a[i];bb+=b[i]*b[i];}
 aa=sqrt(aa);bb=sqrt(bb);
 if(!isfinite(aa) || !isfinite(bb)) return CM_NONFINITE;
 if(aa<1e-8 || bb<1e-8 || fabs(aa-bb)>1e-6) return CM_POLICY;
 for(int i=0;i<3;i++) {a[i]/=aa;b[i]/=bb;}
 double dot=(a[0]*b[0]+a[1]*b[1])+a[2]*b[2];
 if(dot< -1+1e-9) return CM_POLICY;
 double p[3],q[3],t[3],link[3];
 for(int i=0;i<3;i++){p[i]=work->world[12].v[i*4+3];q[i]=work->world[13].v[i*4+3];t[i]=work->world[7].v[i*4+3];link[i]=rig->local[13].v[i*4+3];}
 cm_neck_bounds bounded;TRY(neck_small_bounds(p,q,t,link,&bounded));
 if(bounded.accept)return CM_OK; /* Explicit bounded policy, before any edits. */
 double cross[3]={a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
 double k[9]={0,-cross[2],cross[1],cross[2],0,-cross[0],-cross[1],cross[0],0},square[9],aim[9];
 multiply(k,k,square);
 for(int i=0;i<9;i++) aim[i]=(i%4==0?1.:0.)+k[i]+square[i]/(1+dot);
 double rest[9],delta[9],inv[9],relative[9];
 linear(&work->world[12],rest);multiply(aim,rest,delta);putlinear(&work->world[12],delta);
 linear(&work->world[5],rest);TRY(inverse(rest,inv));multiply(inv,delta,relative);putlinear(&work->local[12],relative);
 TRY(inverse(delta,inv));linear(&work->world[13],rest);multiply(inv,rest,relative);putlinear(&work->local[13],relative);
 for(int row=0;row<3;row++) {
  double value=0;for(int i=0;i<3;i++) value+=work->world[12].v[row*4+i]*rig->local[13].v[i*4+3];
  work->world[13].v[row*4+3]=value+work->world[12].v[row*4+3];
 }
 for(int i=12;i<=13;i++) {
  TRY(finite64(work->local[i].v,16));TRY(finite64(work->world[i].v,16));
  cm_render48 packed;TRY(to_rows(&work->world[i],&packed));cm_mat4 quantized;identity(&quantized);
  for(int j=0;j<12;j++) quantized.v[j]=packed.v[j];TRY(rigid(&quantized,2e-6));
 }
 return CM_OK;
}
static double dot3(const double a[3],const double b[3]) {
 return (a[0]*b[0]+a[1]*b[1])+a[2]*b[2];
}
static void cross3(const double a[3],const double b[3],double out[3]) {
 out[0]=a[1]*b[2]-a[2]*b[1];
 out[1]=a[2]*b[0]-a[0]*b[2];
 out[2]=a[0]*b[1]-a[1]*b[0];
}
/* Align arm chains 14..31 (left) and 32..49 (right) to torso-locked sagittal shoulder hinges
 * matching arm_profile.py authored_profile(..., hinge=True). Non-arm rows 0..13, 50..64 untouched. */
static cm_status arm_policy(const cm_rig *rig,cm_workspace *work) {
 if(cm_parents[14]!=5 || cm_parents[15]!=14 || cm_parents[32]!=5 || cm_parents[33]!=32) return CM_MAPPING;
 if(cm_helpers[14] || cm_helpers[15] || cm_helpers[32] || cm_helpers[33]) return CM_MAPPING;
 for(int i=0;i<65;i++) {
  int p=cm_parents[i];
  if(i>=15 && i<=31) {if(p<14 || p>=32) return CM_MAPPING;}
  else if(i>=33 && i<=49) {if(p<32 || p>=50) return CM_MAPPING;}
  else if(i!=14 && i!=32 && p>=14 && p<=49) return CM_MAPPING;
 }
 double right[3],up[3],forward[3];
 for(int row=0;row<3;row++) {
  right[row]=rig->source[33].v[row*4+3]-rig->source[15].v[row*4+3];
  up[row]=rig->source[5].v[row*4+3]-rig->source[1].v[row*4+3];
 }
 TRY(finite64(right,3)); TRY(finite64(up,3));
 double rn=sqrt(dot3(right,right));
 if(!isfinite(rn)) return CM_NONFINITE;
 if(rn<1e-8) return CM_POLICY;
 for(int row=0;row<3;row++) right[row]/=rn;
 double proj=dot3(right,up);
 for(int row=0;row<3;row++) up[row]-=right[row]*proj;
 double un=sqrt(dot3(up,up));
 if(!isfinite(un)) return CM_NONFINITE;
 if(un<1e-8) return CM_POLICY;
 for(int row=0;row<3;row++) up[row]/=un;
 cross3(up,right,forward);
 double basis[9],basis_t[9];
 for(int row=0;row<3;row++) {
  basis[row*3+0]=right[row]; basis[row*3+1]=forward[row]; basis[row*3+2]=up[row];
  basis_t[0*3+row]=right[row]; basis_t[1*3+row]=forward[row]; basis_t[2*3+row]=up[row];
 }
 TRY(finite64(basis,9));
 if(gram(basis)>1e-6 || fabs(det(basis)-1.)>1e-6) return CM_BASIS;

 double chest_rest[9],chest_rest_inv[9],chest_world[9],chest_delta_r[9],chest_delta_inv[9],chest_delta_t[3];
 linear(&rig->source[5],chest_rest); TRY(inverse(chest_rest,chest_rest_inv));
 linear(&work->world[5],chest_world); multiply(chest_world,chest_rest_inv,chest_delta_r);
 TRY(inverse(chest_delta_r,chest_delta_inv));
 for(int row=0;row<3;row++) {
  double v=work->world[5].v[row*4+3];
  for(int k=0;k<3;k++) v-=chest_delta_r[row*3+k]*rig->source[5].v[k*4+3];
  chest_delta_t[row]=v;
 }
 TRY(finite64(chest_delta_t,3));

 static const int sides[2][3]={{14,15,32},{32,33,50}};
 double side_delta[2][9];
 for(int s=0;s<2;s++) {
  int arm=sides[s][1];
  double arm_rest[9],arm_rest_inv[9],arm_world[9],arm_total[9],rel[9],rel_basis[9],t[9];
  linear(&rig->source[arm],arm_rest); TRY(inverse(arm_rest,arm_rest_inv));
  linear(&work->world[arm],arm_world); multiply(arm_world,arm_rest_inv,arm_total);
  multiply(chest_delta_inv,arm_total,rel);
  multiply(rel,basis,t); multiply(basis_t,t,rel_basis);
  double sine=rel_basis[2*3+1]-rel_basis[1*3+2];
  double cosine=rel_basis[1*3+1]+rel_basis[2*3+2];
  if(!isfinite(sine) || !isfinite(cosine)) return CM_NONFINITE;
  if(hypot(sine,cosine)<1e-8) return CM_POLICY;
  double angle=atan2(sine,cosine),c=cos(angle),sn=sin(angle);
  double swing[9]={1.,0.,0.,0.,c,-sn,0.,sn,c};
  multiply(basis,swing,t); multiply(t,basis_t,rel);
  multiply(chest_delta_r,rel,side_delta[s]);
 }
 for(int s=0;s<2;s++) {
  int carrier=sides[s][0],arm=sides[s][1],end=sides[s][2];
  double target[9],pr[9],pinv[9],own[9],carrier_rest[9];
  linear(&rig->source[carrier],carrier_rest); multiply(chest_delta_r,carrier_rest,target);
  putlinear(&work->world[carrier],target);
  for(int row=0;row<3;row++) {
   double v=chest_delta_t[row];
   for(int k=0;k<3;k++) v+=chest_delta_r[row*3+k]*rig->source[carrier].v[k*4+3];
   work->world[carrier].v[row*4+3]=v;
  }
  linear(&work->world[5],pr); TRY(inverse(pr,pinv)); multiply(pinv,target,own);
  putlinear(&work->local[carrier],own);

  double pivot[3];
  for(int row=0;row<3;row++) {
   double v=chest_delta_t[row];
   for(int k=0;k<3;k++) v+=chest_delta_r[row*3+k]*rig->source[arm].v[k*4+3];
   pivot[row]=v;
  }
  for(int node=arm;node<end;node++) {
   double node_rest[9]; linear(&rig->source[node],node_rest);
   multiply(side_delta[s],node_rest,target);
   putlinear(&work->world[node],target);
   for(int row=0;row<3;row++) {
    double v=pivot[row];
    for(int k=0;k<3;k++) v+=side_delta[s][row*3+k]*(rig->source[node].v[k*4+3]-rig->source[arm].v[k*4+3]);
    work->world[node].v[row*4+3]=v;
   }
   int p=cm_parents[node];
   linear(&work->world[p],pr); TRY(inverse(pr,pinv)); multiply(pinv,target,own);
   putlinear(&work->local[node],own);
  }
 }
 for(int i=14;i<50;i++) {
  TRY(finite64(work->local[i].v,16)); TRY(finite64(work->world[i].v,16));
  cm_render48 packed; TRY(to_rows(&work->world[i],&packed));
  cm_mat4 quantized; identity(&quantized);
  for(int j=0;j<12;j++) quantized.v[j]=packed.v[j];
  TRY(rigid(&quantized,2e-6));
 }
 return CM_OK;
}
CM_API cm_status cm_evaluate(const cm_rig *rig,const cm_frame *frame,cm_workspace *work,cm_render48 *out,size_t capacity) {
 TRY(buffers(rig,frame,work,out,capacity));
 TRY(environment());
 if(rig->magic!=CM_MAGIC) return CM_UNPREPARED;
 if(rig->policy>CM_NECK_AND_ARM_SHOULDER_HINGE) return CM_POLICY;
 if(frame->basis_known!=1) return CM_BASIS_UNKNOWN;
 const cm_garment *garment=cm_garment_at(frame->garment_index);
 if(!garment) return CM_MAPPING;
 if(frame->graph_count!=garment->count) return CM_COUNT;
 if(frame->mesh_node>=frame->graph_count) return CM_INDEX;
 for(uint32_t i=0;i<garment->alias_count;i++) if(garment->aliases[i].node==frame->mesh_node) return CM_MAPPING;
 TRY(finite_rows(frame->parent,107));
 TRY(finite_rows(frame->graph,frame->graph_count));
 TRY(rigid(frame->parent_graph_to_native_rest_model,2e-6));
 cm_render48 graph_frame; TRY(to_rows(frame->source_model_to_selected_graph,&graph_frame));
 cm_mat4 quantized; identity(&quantized); for(int i=0;i<12;i++) quantized.v[i]=graph_frame.v[i];
 TRY(rigid(&quantized,2e-6));
 memset(&work->diagnostics,0,sizeof work->diagnostics);
 for(int i=0;i<107;i++) {
  cm_mat4 pose; double rotation[9],scale[3],delta[9],temp[9];
  TRY(transformed(frame->parent_graph_to_native_rest_model,&frame->parent[i],&pose));
  TRY(split(&pose,rotation,scale));
  int nonunit=0;
  for(int c=0;c<3;c++) {
   double e=fabs(scale[c]/rig->native_scale[i][c]-1);
   if(e>work->diagnostics.scale_relative_error) work->diagnostics.scale_relative_error=e;
   if(e>2e-6) return CM_SCALE;
   if(fabs(rig->native_scale[i][c]-1)>2e-6) nonunit=1;
   e=fabs(pose.v[c*4+3]-cm_native_rest[i].v[c*4+3]);
   if(e>work->diagnostics.ignored_native_translation_max) work->diagnostics.ignored_native_translation_max=e;
  }
  work->diagnostics.nonunit_native_rest_nodes+=(uint32_t)nonunit;
  multiply(rotation,rig->native_inverse[i],delta);
  double error=gram(delta); if(error>work->diagnostics.delta_gram_error) work->diagnostics.delta_gram_error=error;
  if(error>4e-6) return CM_SHEAR;
  multiply(rig->basis,delta,temp); multiply(temp,rig->basis_inverse,work->delta[i]);
 }
 for(int i=0;i<65;i++) {
  int p=cm_parents[i],anchor=cm_anchors[i];
  int absolute=anchor>=0 && (!cm_helpers[i] || (rig->policy!=CM_INHERIT_HELPERS && i==13));
  double r[9],target[9]; work->local[i]=rig->local[i]; identity(&work->world[i]);
  if(absolute) {
   linear(&rig->source[i],r); multiply(work->delta[anchor],r,target);
   if(p>=0) {double pr[9],inv[9],own[9]; linear(&work->world[p],pr); TRY(inverse(pr,inv)); multiply(inv,target,own); putlinear(&work->local[i],own);}
   else putlinear(&work->local[i],target);
  } else {
   if(p<0) return CM_MAPPING;
   double pr[9]; linear(&work->world[p],pr); linear(&rig->local[i],r); multiply(pr,r,target);
  }
  putlinear(&work->world[i],target);
  for(int row=0;row<3;row++) {
   double t=work->local[i].v[row*4+3];
   if(p>=0) {
    t=0; for(int c=0;c<3;c++) t+=work->world[p].v[row*4+c]*work->local[i].v[c*4+3];
    t+=work->world[p].v[row*4+3];
   }
   work->world[i].v[row*4+3]=t;
  }
  TRY(finite64(work->local[i].v,16)); TRY(finite64(work->world[i].v,16));
  cm_render48 packed; TRY(to_rows(&work->world[i],&packed));
  identity(&quantized); for(int j=0;j<12;j++) quantized.v[j]=packed.v[j];
  TRY(rigid(&quantized,2e-6));
 }
 if(rig->policy==CM_YAW_TORSO_NECK) TRY(contact_policy(rig,work));
 if(rig->policy==CM_NECK_AIM_ONLY || rig->policy==CM_NECK_AND_ARM_SHOULDER_HINGE) TRY(neck_policy(rig,work));
 if(rig->policy==CM_ARM_SHOULDER_HINGE || rig->policy==CM_NECK_AND_ARM_SHOULDER_HINGE) TRY(arm_policy(rig,work));
 memcpy(work->graph,frame->graph,frame->graph_count*48);
 for(uint32_t i=0;i<garment->alias_count;i++) {
  const cm_alias a=garment->aliases[i];
  if(a.source>=65 || a.node>=frame->graph_count) return CM_MAPPING;
  for(uint32_t j=0;j<i;j++) if(garment->aliases[j].source==a.source || garment->aliases[j].node==a.node) return CM_MAPPING;
  cm_render48 packed; TRY(to_rows(&work->world[a.source],&packed));
  TRY(cm_compose(&graph_frame,&packed,&work->graph[a.node]));
 }
 memcpy(out,work->graph,frame->graph_count*48); return CM_OK;
}

CM_API cm_status cm_solve_parent(const cm_rig *rig,const cm_render48 *parent,size_t parent_count,
                                 const cm_mat4 *parent_graph_to_native_rest_model,
                                 cm_workspace *work,cm_render48 source_packed[CM_SOURCE]) {
 if(!rig || !parent || !parent_graph_to_native_rest_model || !work || !source_packed) return CM_ARGUMENT;
 if(parent_count!=107) return CM_COUNT;
 if(overlaps(work,sizeof *work,source_packed,CM_SOURCE*48) ||
    overlaps(work,sizeof *work,parent,parent_count*48) ||
    overlaps(work,sizeof *work,parent_graph_to_native_rest_model,sizeof(cm_mat4)) ||
    overlaps(source_packed,CM_SOURCE*48,parent,parent_count*48)) return CM_OVERLAP;
 TRY(environment());
 if(rig->magic!=CM_MAGIC) return CM_UNPREPARED;
 if(rig->policy>CM_NECK_AND_ARM_SHOULDER_HINGE) return CM_POLICY;
 TRY(finite_rows(parent,107));
 TRY(rigid(parent_graph_to_native_rest_model,2e-6));
 memset(&work->diagnostics,0,sizeof work->diagnostics);
 for(int i=0;i<107;i++) {
  cm_mat4 pose; double rotation[9],scale[3],delta[9],temp[9];
  TRY(transformed(parent_graph_to_native_rest_model,&parent[i],&pose));
  TRY(split(&pose,rotation,scale));
  int nonunit=0;
  for(int c=0;c<3;c++) {
   double e=fabs(scale[c]/rig->native_scale[i][c]-1);
   if(e>work->diagnostics.scale_relative_error) work->diagnostics.scale_relative_error=e;
   if(e>2e-6) return CM_SCALE;
   if(fabs(rig->native_scale[i][c]-1)>2e-6) nonunit=1;
   e=fabs(pose.v[c*4+3]-cm_native_rest[i].v[c*4+3]);
   if(e>work->diagnostics.ignored_native_translation_max) work->diagnostics.ignored_native_translation_max=e;
  }
  work->diagnostics.nonunit_native_rest_nodes+=(uint32_t)nonunit;
  multiply(rotation,rig->native_inverse[i],delta);
  double error=gram(delta); if(error>work->diagnostics.delta_gram_error) work->diagnostics.delta_gram_error=error;
  if(error>4e-6) return CM_SHEAR;
  multiply(rig->basis,delta,temp); multiply(temp,rig->basis_inverse,work->delta[i]);
 }
 cm_mat4 quantized;
 for(int i=0;i<65;i++) {
  int p=cm_parents[i],anchor=cm_anchors[i];
  int absolute=anchor>=0 && (!cm_helpers[i] || (rig->policy!=CM_INHERIT_HELPERS && i==13));
  double r[9],target[9]; work->local[i]=rig->local[i]; identity(&work->world[i]);
  if(absolute) {
   linear(&rig->source[i],r); multiply(work->delta[anchor],r,target);
   if(p>=0) {double pr[9],inv[9],own[9]; linear(&work->world[p],pr); TRY(inverse(pr,inv)); multiply(inv,target,own); putlinear(&work->local[i],own);}
   else putlinear(&work->local[i],target);
  } else {
   if(p<0) return CM_MAPPING;
   double pr[9]; linear(&work->world[p],pr); linear(&rig->local[i],r); multiply(pr,r,target);
  }
  putlinear(&work->world[i],target);
  for(int row=0;row<3;row++) {
   double t=work->local[i].v[row*4+3];
   if(p>=0) {
    t=0; for(int c=0;c<3;c++) t+=work->world[p].v[row*4+c]*work->local[i].v[c*4+3];
    t+=work->world[p].v[row*4+3];
   }
   work->world[i].v[row*4+3]=t;
  }
  TRY(finite64(work->local[i].v,16)); TRY(finite64(work->world[i].v,16));
  cm_render48 packed; TRY(to_rows(&work->world[i],&packed));
  identity(&quantized); for(int j=0;j<12;j++) quantized.v[j]=packed.v[j];
  TRY(rigid(&quantized,2e-6));
 }
 if(rig->policy==CM_YAW_TORSO_NECK) TRY(contact_policy(rig,work));
 if(rig->policy==CM_NECK_AIM_ONLY || rig->policy==CM_NECK_AND_ARM_SHOULDER_HINGE) TRY(neck_policy(rig,work));
 if(rig->policy==CM_ARM_SHOULDER_HINGE || rig->policy==CM_NECK_AND_ARM_SHOULDER_HINGE) TRY(arm_policy(rig,work));
 for(int i=0;i<CM_SOURCE;i++) TRY(to_rows(&work->world[i],&source_packed[i]));
 return CM_OK;
}

CM_API cm_status cm_apply_garment(const cm_rig *rig,uint32_t garment_index,uint32_t mesh_node,
                                  const cm_render48 *graph,size_t graph_count,
                                  const cm_mat4 *source_model_to_selected_graph,
                                  const cm_render48 source_packed[CM_SOURCE],
                                  cm_workspace *work,cm_render48 *out,size_t capacity) {
 if(!rig || !graph || !source_model_to_selected_graph || !source_packed || !work || !out) return CM_ARGUMENT;
 if(graph_count==0 || graph_count>512 || capacity<graph_count || capacity>512) return CM_COUNT;
 if(overlaps(work,sizeof *work,out,capacity*48) ||
    overlaps(work,sizeof *work,graph,graph_count*48) ||
    overlaps(out,capacity*48,graph,graph_count*48) ||
    overlaps(out,capacity*48,source_packed,CM_SOURCE*48)) return CM_OVERLAP;
 TRY(environment());
 if(rig->magic!=CM_MAGIC) return CM_UNPREPARED;
 if(rig->policy>CM_NECK_AND_ARM_SHOULDER_HINGE) return CM_POLICY;
 const cm_garment *garment=cm_garment_at(garment_index);
 if(!garment) return CM_MAPPING;
 if(graph_count!=garment->count) return CM_COUNT;
 if(mesh_node>=graph_count) return CM_INDEX;
 for(uint32_t i=0;i<garment->alias_count;i++) if(garment->aliases[i].node==mesh_node) return CM_MAPPING;
 TRY(finite_rows(graph,graph_count));
 cm_render48 graph_frame; TRY(to_rows(source_model_to_selected_graph,&graph_frame));
 cm_mat4 quantized; identity(&quantized); for(int i=0;i<12;i++) quantized.v[i]=graph_frame.v[i];
 TRY(rigid(&quantized,2e-6));
 memcpy(work->graph,graph,graph_count*48);
 for(uint32_t i=0;i<garment->alias_count;i++) {
  const cm_alias a=garment->aliases[i];
  if(a.source>=65 || a.node>=graph_count) return CM_MAPPING;
  for(uint32_t j=0;j<i;j++) if(garment->aliases[j].source==a.source || garment->aliases[j].node==a.node) return CM_MAPPING;
  TRY(cm_compose(&graph_frame,&source_packed[a.source],&work->graph[a.node]));
 }
 memcpy(out,work->graph,graph_count*48); return CM_OK;
}
