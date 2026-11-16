/* Pure own-process edge/transaction checks. No SDK or game dependency. */
#include "compact_math.c"
#include <stdio.h>
#include <stdlib.h>
static unsigned checks;
#define CHECK(x) do{checks++;if(!(x)){fprintf(stderr,"line%d: %s\n",__LINE__,#x);exit(1);}}while(0)
static void reset(cm_rig *r,cm_workspace *w){
 double basis[9]={1,0,0,0,1,0,0,0,1};CHECK(cm_prepare(r,basis,1,CM_NECK_AIM_ONLY)==CM_OK);
 memset(w,0,sizeof *w);memcpy(w->world,r->source,sizeof w->world);memcpy(w->local,r->local,sizeof w->local);
}
static void unchanged(const cm_workspace *before,const cm_workspace *after){
 for(int i=0;i<65;i++){
  if(i!=12&&i!=13){CHECK(!memcmp(&before->world[i],&after->world[i],sizeof(cm_mat4)));CHECK(!memcmp(&before->local[i],&after->local[i],sizeof(cm_mat4)));}
  for(int row=0;row<3;row++)CHECK(before->local[i].v[row*4+3]==after->local[i].v[row*4+3]);
 }
 for(int row=0;row<3;row++){
  CHECK(before->world[12].v[row*4+3]==after->world[12].v[row*4+3]);
  for(int col=0;col<3;col++)CHECK(before->world[13].v[row*4+col]==after->world[13].v[row*4+col]);
 }
}
static void target(cm_workspace *w,double dot,double length){
 double a[3],n=0;for(int i=0;i<3;i++){a[i]=w->world[13].v[i*4+3]-w->world[12].v[i*4+3];n+=a[i]*a[i];}n=sqrt(n);
 for(int i=0;i<3;i++)a[i]/=n;
 int axis=0;for(int i=1;i<3;i++)if(fabs(a[i])<fabs(a[axis]))axis=i;
 double v[3]={0,0,0};v[axis]=1;double p[3]={a[1]*v[2]-a[2]*v[1],a[2]*v[0]-a[0]*v[2],a[0]*v[1]-a[1]*v[0]};
 double pn=sqrt(p[0]*p[0]+p[1]*p[1]+p[2]*p[2]);
 for(int i=0;i<3;i++)w->world[7].v[i*4+3]=w->world[12].v[i*4+3]+length*n*(dot*a[i]+sqrt(1-dot*dot)*p[i]/pn);
}
int main(void){
 unsigned csr=_mm_getcsr();_mm_setcsr(0x1f80);cm_rig r;cm_workspace w,before;
 reset(&r,&w);before=w;CHECK(neck_policy(&r,&w)==CM_OK);unchanged(&before,&w);CHECK(!memcmp(&before,&w,sizeof w));
 reset(&r,&w);w.world[12].v[3]=0.;w.world[13].v[3]=-0.;w.world[7].v[3]=0.;before=w;
 CHECK(neck_policy(&r,&w)==CM_OK);CHECK(!memcmp(&before,&w,sizeof w));
 for(int side=0;side<2;side++){
  reset(&r,&w);target(&w,-1+(side?2e-9:.5e-9),1);before=w;
  CHECK(neck_policy(&r,&w)==(side?CM_OK:CM_POLICY));if(side)unchanged(&before,&w);
 }
 reset(&r,&w);target(&w,-1,1);CHECK(neck_policy(&r,&w)==CM_POLICY);
 reset(&r,&w);target(&w,1,0);CHECK(neck_policy(&r,&w)==CM_POLICY);
 reset(&r,&w);target(&w,0,1.01);CHECK(neck_policy(&r,&w)==CM_POLICY);
 reset(&r,&w);for(int i=0;i<3;i++)w.world[13].v[i*4+3]=w.world[12].v[i*4+3];CHECK(neck_policy(&r,&w)==CM_POLICY);
 reset(&r,&w);w.world[7].v[3]=NAN;CHECK(neck_policy(&r,&w)==CM_NONFINITE);
 /* A proper180 head orientation is independent of the neck aim direction. */
 reset(&r,&w);target(&w,.8,1);
 for(int row=0;row<2;row++)for(int col=0;col<3;col++)w.world[13].v[row*4+col]*=-1;
 before=w;CHECK(neck_policy(&r,&w)==CM_OK);unchanged(&before,&w);
 cm_render48 parent[107],graph[21],out[21],sentinel[21];cm_mat4 h,f;identity(&h);identity(&f);
 for(int i=0;i<107;i++)for(int j=0;j<12;j++)parent[i].v[j]=(float)cm_native_rest[i].v[j];
 for(int i=0;i<21;i++)for(int j=0;j<12;j++)graph[i].v[j]=(j==0||j==5||j==10)?1.f:0.f;
 cm_frame frame={parent,107,graph,21,0,17,&h,&f,1};
 /* The explicit approximation no-op leaves the public neutral baseline exact. */
 memset(out,0xA5,sizeof out);memcpy(sentinel,out,sizeof out);reset(&r,&w);
 CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_OK);
 cm_rig baseline=r;baseline.policy=CM_HEADPEG_ONLY;cm_workspace bw;cm_render48 bo[21];
 CHECK(cm_evaluate(&baseline,&frame,&bw,bo,21)==CM_OK);
 CHECK(!memcmp(out,bo,sizeof out)&&!memcmp(w.world,bw.world,sizeof w.world)&&!memcmp(w.local,bw.local,sizeof w.local));
 for(unsigned garment=0;garment<7;garment++){
  const cm_garment *description=cm_garment_at(garment);CHECK(description&&description->count<=128);
  cm_render48 input[128],actual[128],expected[128];
  for(unsigned i=0;i<description->count;i++)for(int j=0;j<12;j++)input[i].v[j]=(j==0||j==5||j==10)?1.f:0.f;
  cm_frame each={parent,107,input,description->count,garment,0,&h,&f,1};
  CHECK(cm_evaluate(&r,&each,&w,actual,128)==CM_OK);CHECK(cm_evaluate(&baseline,&each,&bw,expected,128)==CM_OK);
  CHECK(!memcmp(actual,expected,description->count*48)&&!memcmp(w.world,bw.world,sizeof w.world)&&!memcmp(w.local,bw.local,sizeof w.local));
 }
 cm_mat4 tiny;identity(&tiny);tiny.v[1]=-0x1.78p-155;cm_render48 packed;CHECK(to_rows(&tiny,&packed)==CM_NUMERICAL);
 for(int policy=0;policy<3;policy++){r.policy=(cm_policy)policy;CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_OK);}
 memset(out,0xA5,sizeof out);memcpy(sentinel,out,sizeof out);reset(&r,&w);r.local[13].v[11]*=2;
 CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_POLICY);CHECK(!memcmp(out,sentinel,sizeof out));
 reset(&r,&w);r.policy=6;CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_POLICY);CHECK(!memcmp(out,sentinel,sizeof out));
 reset(&r,&w);h.v[0]=1.01;CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_BASIS);CHECK(!memcmp(out,sentinel,sizeof out));h.v[0]=1;
 _mm_setcsr(0x9fa0);CHECK(cm_evaluate(&r,&frame,&w,out,21)==CM_NUMERICAL);CHECK(!memcmp(out,sentinel,sizeof out));
 _mm_setcsr(csr);printf("PASS %u neck policy assertions\n",checks);return 0;
}
