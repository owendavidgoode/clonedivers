/* Pure own-process checks for arm_policy (enum 4) and combined neck+arm (enum 5). */
#include "compact_math.c"
#include <stdio.h>
#include <stdlib.h>

static unsigned checks;
#define CHECK(x) do{checks++;if(!(x)){fprintf(stderr,"line%d: %s\n",__LINE__,#x);exit(1);}}while(0)

static void rot_axis(int axis,double rad,double r[9]) {
 double c=cos(rad),s=sin(rad);
 if(axis==0){double m[9]={1,0,0,0,c,-s,0,s,c};memcpy(r,m,sizeof m);}
 else if(axis==1){double m[9]={c,0,s,0,1,0,-s,0,c};memcpy(r,m,sizeof m);}
 else{double m[9]={c,-s,0,s,c,0,0,0,1};memcpy(r,m,sizeof m);}
}

static void reset_rig(cm_rig *r,cm_workspace *w,cm_policy policy) {
 double b[9]={1,0,0,0,1,0,0,0,1};
 CHECK(cm_prepare(r,b,1,policy)==CM_OK);
 memset(w,0,sizeof *w);
 memcpy(w->world,r->source,sizeof w->world);
 memcpy(w->local,r->local,sizeof w->local);
}

static double max_mat4_diff(const cm_mat4 *a,const cm_mat4 *b) {
 double m=0;
 for(int i=0;i<16;i++){double d=fabs(a->v[i]-b->v[i]);if(d>m)m=d;}
 return m;
}

static void mul_mat_vec3(const double m[9],const double v[3],double out[3]) {
 for(int r=0;r<3;r++) out[r]=(m[r*3]*v[0]+m[r*3+1]*v[1])+m[r*3+2]*v[2];
}

int main(void) {
 unsigned csr=_mm_getcsr();
 _mm_setcsr(0x1f80);

 cm_rig r4,r5,r1,r3,bad;
 cm_workspace w,w_base,before;
 double id[9]={1,0,0,0,1,0,0,0,1};
 CHECK(cm_prepare(&r4,id,1,CM_ARM_SHOULDER_HINGE)==CM_OK);
 CHECK(cm_prepare(&r5,id,1,CM_NECK_AND_ARM_SHOULDER_HINGE)==CM_OK);
 CHECK(cm_prepare(&bad,id,1,(cm_policy)6)==CM_POLICY);

 /* Rest pose: arm_policy produces rest within roundoff and keeps local translations bit-exact. */
 reset_rig(&r4,&w,CM_ARM_SHOULDER_HINGE); before=w;
 CHECK(arm_policy(&r4,&w)==CM_OK);
 for(int i=0;i<65;i++) {
  CHECK(max_mat4_diff(&w.world[i],&before.world[i])<1e-13);
  for(int row=0;row<3;row++) CHECK(w.local[i].v[row*4+3]==before.local[i].v[row*4+3]);
 }

 /* Non-arm rows 0..13 and 50..64 remain bit-identical across arbitrary arm swing + twist. */
 reset_rig(&r4,&w,CM_ARM_SHOULDER_HINGE);
 double chest_rot[9],r_x[9],r_y[9],r_z[9],tmp[9];
 rot_axis(2,0.3,r_z); rot_axis(0,0.45,r_x); rot_axis(1,-0.25,r_y);
 multiply(r_z,r_x,chest_rot);
 double c_rest[9],c_world[9]; linear(&r4.source[5],c_rest); multiply(chest_rot,c_rest,c_world);
 putlinear(&w.world[5],c_world);
 w.world[5].v[3]+=0.08; w.world[5].v[7]-=0.04; w.world[5].v[11]+=0.02;

 double arm_delta[9],swing[9],twist[9];
 rot_axis(0,0.75,swing); rot_axis(2,0.4,twist); multiply(swing,twist,tmp); multiply(chest_rot,tmp,arm_delta);
 for(int arm_i=0;arm_i<2;arm_i++) {
  int arm=arm_i?33:15;
  double ar[9],aw[9]; linear(&r4.source[arm],ar); multiply(arm_delta,ar,aw); putlinear(&w.world[arm],aw);
 }
 before=w;
 CHECK(arm_policy(&r4,&w)==CM_OK);
 for(int i=0;i<65;i++) {
  if(i<14 || i>=50) {
   CHECK(!memcmp(&w.world[i],&before.world[i],sizeof(cm_mat4)));
   CHECK(!memcmp(&w.local[i],&before.local[i],sizeof(cm_mat4)));
  } else {
   for(int row=0;row<3;row++) CHECK(w.local[i].v[row*4+3]==before.local[i].v[row*4+3]);
  }
 }
 /* Rigid segment distances inside both arms match rest lengths. */
 static const int edges[][2]={{15,17},{17,20},{20,30},{14,15},{33,35},{35,38},{38,48},{32,33}};
 for(unsigned e=0;e<sizeof(edges)/sizeof(edges[0]);e++) {
  int a=edges[e][0],b=edges[e][1];
  double d0=0,d1=0;
  for(int row=0;row<3;row++) {
   double da=r4.source[b].v[row*4+3]-r4.source[a].v[row*4+3];
   double db=w.world[b].v[row*4+3]-w.world[a].v[row*4+3];
   d0+=da*da; d1+=db*db;
  }
  CHECK(fabs(sqrt(d0)-sqrt(d1))<1e-12);
 }

 /* Degenerate 180-deg twist around torso-up axis triggers swing projection refusal cleanly. */
 reset_rig(&r4,&w,CM_ARM_SHOULDER_HINGE);
 double right[3],up[3];
 for(int k=0;k<3;k++){right[k]=r4.source[33].v[k*4+3]-r4.source[15].v[k*4+3];up[k]=r4.source[5].v[k*4+3]-r4.source[1].v[k*4+3];}
 double rn=sqrt(dot3(right,right)); for(int k=0;k<3;k++)right[k]/=rn;
 double prj=dot3(right,up); for(int k=0;k<3;k++)up[k]-=right[k]*prj;
 double un=sqrt(dot3(up,up)); for(int k=0;k<3;k++)up[k]/=un;
 /* Householder reflection around right/forward plane (= 180 deg rotation around up). */
 double rot180_up[9];
 for(int r=0;r<3;r++) for(int c=0;c<3;c++) rot180_up[r*3+c]=2.0*up[r]*up[c]-(r==c?1.0:0.0);
 {
  double ar[9],aw[9]; linear(&r4.source[15],ar); multiply(rot180_up,ar,aw); putlinear(&w.world[15],aw);
  CHECK(arm_policy(&r4,&w)==CM_POLICY);
 }

 /* Non-finite input produces CM_NONFINITE. */
 reset_rig(&r4,&w,CM_ARM_SHOULDER_HINGE);
 w.world[15].v[0]=NAN;
 CHECK(arm_policy(&r4,&w)==CM_NONFINITE);

 /* Full cm_evaluate equivalence across all 7 garments (non-arm rows exact vs policy 1 / 3). */
 cm_render48 parent[107],graph[128],out4[128],out1[128],out5[128],out3[128],sentinel[128];
 cm_mat4 h,f; identity(&h); identity(&f);
 for(int i=0;i<107;i++) for(int j=0;j<12;j++) parent[i].v[j]=(float)cm_native_rest[i].v[j];
 CHECK(cm_prepare(&r1,id,1,CM_HEADPEG_ONLY)==CM_OK);
 CHECK(cm_prepare(&r3,id,1,CM_NECK_AIM_ONLY)==CM_OK);
 /* Apply a forward chest bend + shoulder shrug + twist on native arm bones so policies differ on arms & neck. */
 double q_arm[9]; rot_axis(0,0.6,swing); rot_axis(2,0.5,twist); multiply(swing,twist,q_arm);
 const int native_arms[]={42,44,46,47,65,67,69,71};
 for(unsigned k=0;k<sizeof(native_arms)/sizeof(native_arms[0]);k++) {
  int ni=native_arms[k];
  double nr[9],nw[9]; linear(&cm_native_rest[ni],nr); multiply(q_arm,nr,nw);
  for(int r=0;r<3;r++) for(int c=0;c<3;c++) parent[ni].v[r*4+c]=(float)nw[r*3+c];
 }
 for(unsigned g=0;g<7;g++) {
  const cm_garment *gm=cm_garment_at(g); CHECK(gm && gm->count<=128);
  for(unsigned i=0;i<gm->count;i++) for(int j=0;j<12;j++) graph[i].v[j]=(j==0||j==5||j==10)?1.f:0.f;
  cm_frame fr={parent,107,graph,gm->count,g,0,&h,&f,1};
  CHECK(cm_evaluate(&r1,&fr,&w_base,out1,128)==CM_OK);
  CHECK(cm_evaluate(&r4,&fr,&w,out4,128)==CM_OK);
  for(int i=0;i<65;i++) if(i<14||i>=50) CHECK(!memcmp(&w.world[i],&w_base.world[i],sizeof(cm_mat4)));
  for(unsigned n=0;n<gm->count;n++) {
   int is_arm_alias=0;
   for(uint32_t a=0;a<gm->alias_count;a++) if(gm->aliases[a].node==n && gm->aliases[a].source>=14 && gm->aliases[a].source<50) is_arm_alias=1;
   if(!is_arm_alias) CHECK(!memcmp(&out4[n],&out1[n],48));
  }
  CHECK(cm_evaluate(&r3,&fr,&w_base,out3,128)==CM_OK);
  CHECK(cm_evaluate(&r5,&fr,&w,out5,128)==CM_OK);
  for(int i=0;i<65;i++) if(i<14||i>=50) CHECK(!memcmp(&w.world[i],&w_base.world[i],sizeof(cm_mat4)));
  for(unsigned n=0;n<gm->count;n++) {
   int is_arm_alias=0;
   for(uint32_t a=0;a<gm->alias_count;a++) if(gm->aliases[a].node==n && gm->aliases[a].source>=14 && gm->aliases[a].source<50) is_arm_alias=1;
   if(!is_arm_alias) CHECK(!memcmp(&out5[n],&out3[n],48));
  }
 }

 /* MXCSR preserved + invalid policy 6 fails atomically. */
 memset(out4,0xA5,sizeof out4); memcpy(sentinel,out4,sizeof sentinel);
 cm_frame fbad={parent,107,graph,21,0,17,&h,&f,1};
 for(int i=0;i<21;i++) for(int j=0;j<12;j++) graph[i].v[j]=(j==0||j==5||j==10)?1.f:0.f;
 cm_rig r6=r5; r6.policy=6;
 CHECK(cm_evaluate(&r6,&fbad,&w,out4,21)==CM_POLICY);
 CHECK(!memcmp(out4,sentinel,21*48));
 _mm_setcsr(0x9fa0);
 CHECK(cm_prepare(&r5,id,1,CM_NECK_AND_ARM_SHOULDER_HINGE)==CM_NUMERICAL);
 CHECK(_mm_getcsr()==0x9fa0);

 _mm_setcsr(csr);
 printf("PASS %u arm policy assertions (hinge + combined neck+arm + atomic failure + MXCSR)\n",checks);
 return 0;
}
