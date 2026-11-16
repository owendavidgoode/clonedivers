/* Enum3-only authoring approximation. No pose writes or environment changes.
 * Arithmetic contract: binary64 nearest-even, gradual underflow, no contraction.
 * Invalid intervals refuse; finite uncertain bounds never select a no-op. */
#include <emmintrin.h>
_Static_assert(sizeof(double)==8&&DBL_MANT_DIG==53&&DBL_MAX_EXP==1024,"binary64 intervals required");
typedef struct {double lo,hi;} cm_interval;
typedef struct {cm_interval chord,gap,length;double gap_budget;int accept;} cm_neck_bounds;
#define CM_NECK_NOOP_TAU 0x1p-26
static double ni_up(double value){
 uint64_t bits;if(value==0){bits=1;memcpy(&value,&bits,8);return value;}
 memcpy(&bits,&value,8);bits+=(bits>>63)?(uint64_t)-1:1;memcpy(&value,&bits,8);return value;
}
static double ni_down(double value){
 uint64_t bits;if(value==0){bits=0x8000000000000001ull;memcpy(&value,&bits,8);return value;}
 memcpy(&bits,&value,8);bits+=(bits>>63)?1:(uint64_t)-1;memcpy(&value,&bits,8);return value;
}
static int ni_valid(cm_interval a){return isfinite(a.lo)&&isfinite(a.hi)&&a.lo<=a.hi;}
static int ni_sub(cm_interval a,cm_interval b,cm_interval *out){
 if(!ni_valid(a)||!ni_valid(b))return 0;
 double lo=a.lo-b.hi,hi=a.hi-b.lo;if(!isfinite(lo)||!isfinite(hi))return 0;
 *out=(cm_interval){ni_down(lo),ni_up(hi)};return ni_valid(*out);
}
static int ni_div(cm_interval a,cm_interval b,cm_interval *out){
 if(!ni_valid(a)||!ni_valid(b)||b.lo<=0)return 0;
 double values[4]={a.lo/b.lo,a.lo/b.hi,a.hi/b.lo,a.hi/b.hi};
 double lo=values[0],hi=values[0];
 for(int i=0;i<4;i++){if(!isfinite(values[i]))return 0;if(values[i]<lo)lo=values[i];if(values[i]>hi)hi=values[i];}
 *out=(cm_interval){ni_down(lo),ni_up(hi)};return ni_valid(*out);
}
static int ni_square(cm_interval a,cm_interval *out){
 if(!ni_valid(a))return 0;
 double x=a.lo*a.lo,y=a.hi*a.hi;if(!isfinite(x)||!isfinite(y))return 0;
 double lo=(a.lo<=0&&a.hi>=0)?0:(x<y?x:y),hi=x>y?x:y;
 *out=(cm_interval){lo>0?ni_down(lo):0,ni_up(hi)};return ni_valid(*out);
}
static int ni_norm(const cm_interval a[3],cm_interval *out){
 double scale=0;
 for(int i=0;i<3;i++){if(!ni_valid(a[i]))return 0;double x=fabs(a[i].lo),y=fabs(a[i].hi);if(x>scale)scale=x;if(y>scale)scale=y;}
 if(scale==0){*out=(cm_interval){0,0};return 1;}
 cm_interval sum={0,0},denominator={scale,scale};
 for(int i=0;i<3;i++){
  cm_interval q,square;if(!ni_div(a[i],denominator,&q)||!ni_square(q,&square))return 0;
  double lo=sum.lo+square.lo,hi=sum.hi+square.hi;
  if(!isfinite(lo)||!isfinite(hi))return 0;
  sum=(cm_interval){lo>0?ni_down(lo):0,ni_up(hi)};if(!ni_valid(sum))return 0;
 }
 /* SSE2 SQRTSD uses the already-required MXCSR rounding mode. */
 double lo=_mm_cvtsd_f64(_mm_sqrt_sd(_mm_setzero_pd(),_mm_set_sd(sum.lo)));
 double hi=_mm_cvtsd_f64(_mm_sqrt_sd(_mm_setzero_pd(),_mm_set_sd(sum.hi)));
 lo=lo>0?ni_down(lo):0;hi=ni_up(hi);
 lo*=scale;hi*=scale;if(!isfinite(lo)||!isfinite(hi))return 0;
 *out=(cm_interval){lo>0?ni_down(lo):0,ni_up(hi)};return ni_valid(*out);
}
static cm_status neck_small_bounds(const double p[3],const double q[3],const double t[3],const double link[3],cm_neck_bounds *out){
 const double tau=CM_NECK_NOOP_TAU;
 memset(out,0,sizeof *out);
 cm_interval a[3],b[3],gap[3],source[3],an,bn;
 for(int i=0;i<3;i++){
  cm_interval pi={p[i],p[i]},qi={q[i],q[i]},ti={t[i],t[i]};source[i]=(cm_interval){link[i],link[i]};
  if(!ni_sub(qi,pi,&a[i])||!ni_sub(ti,pi,&b[i])||!ni_sub(qi,ti,&gap[i]))return CM_NUMERICAL;
 }
 if(!ni_norm(a,&an)||!ni_norm(b,&bn)||an.lo<=0||bn.lo<=0||!ni_norm(gap,&out->gap)||!ni_norm(source,&out->length)||out->length.lo<=0)return CM_NUMERICAL;
 cm_interval direction[3];
 for(int i=0;i<3;i++){cm_interval x,y;if(!ni_div(a[i],an,&x)||!ni_div(b[i],bn,&y)||!ni_sub(x,y,&direction[i]))return CM_NUMERICAL;}
 if(!ni_norm(direction,&out->chord))return CM_NUMERICAL;
 double budget=tau*out->length.lo;if(!isfinite(budget)||budget<=0)return CM_NUMERICAL;
 out->gap_budget=ni_down(budget);if(!isfinite(out->gap_budget)||out->gap_budget<=0)return CM_NUMERICAL;
 out->accept=out->chord.hi<=tau&&out->gap.hi<=out->gap_budget;
 return CM_OK;
}
