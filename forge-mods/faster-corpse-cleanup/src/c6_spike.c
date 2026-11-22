/* Experimental data preparation only. See hypothesis and unresolved gates. */
#include "c6_spike.h"
#include <math.h>
#include <float.h>
#include <string.h>
_Static_assert(sizeof(void*)==8,"This hypothesis is Windows x64 only");
_Static_assert(sizeof(C6SpecHypothesis)==112,"Inferred spec layout changed");
_Static_assert(offsetof(C6SpecHypothesis,default_value)==56,"Default offset changed");
_Static_assert(offsetof(C6SpecHypothesis,gap)==104,"Gap offset changed");

static int copy_text(char *dst,size_t cap,const char *src,int required) {
 if(!src)return !required;
 int nonspace=0;size_t n=0;while(n<cap&&src[n]){
  /* Conservative ASCII-only spike subset; no silent UTF-8 sanitization. */
  if((unsigned char)src[n]<32||(unsigned char)src[n]>126)return 0;
  if(src[n]!=' ')nonspace=1;
  ++n;
 }
 if(n==cap||(required&&!n)||(n&&!nonspace))return 0;
 memcpy(dst,src,n+1);return 1;
}
int c6_prepare(const CrMenuOption *in,C6OwnedOption *out) {
 if(!in||!out||in->size!=sizeof *in||!isfinite(in->default_value))return 0;
 C6OwnedOption tmp={0};
 if(!copy_text(tmp.id,sizeof tmp.id,in->id,1)||
    !copy_text(tmp.mod_id,65,in->mod_id,1)||
    !copy_text(tmp.mod,41,in->mod_name,1)||
    !copy_text(tmp.label,65,in->label,1)||
    !copy_text(tmp.description,401,in->description,0))return 0;
 for(const unsigned char *p=(const unsigned char*)tmp.mod_id;*p;++p)
  if(!((*p>='a'&&*p<='z')||(*p>='A'&&*p<='Z')||(*p>='0'&&*p<='9')||strchr("_.-/:",*p)))return 0;
 tmp.type=in->type;tmp.default_value=in->default_value;
 switch(in->type) {
 case CR_MENU_TOGGLE:
  if(in->default_value!=0&&in->default_value!=1)return 0;
  break;
 case CR_MENU_CHOICE:
  if(in->choice_count<2||in->choice_count>16||in->default_value<0||
     in->default_value>=in->choice_count||floor(in->default_value)!=in->default_value)return 0;
  tmp.choice_count=in->choice_count;tmp.default_value=in->default_value+1;
  for(uint32_t i=0;i<in->choice_count;++i)
   if(!copy_text(tmp.choices[i],49,in->choices[i],1))return 0;
  break;
 case CR_MENU_SLIDER:
  if(!isfinite(in->min)||!isfinite(in->max)||!isfinite(in->step)||
     in->min>=in->max||in->step<FLT_MIN||in->step>in->max-in->min||
     in->min<-FLT_MAX||in->max>FLT_MAX||in->max-in->min>FLT_MAX||in->default_value<in->min||in->default_value>in->max)return 0;
  tmp.min=in->min;tmp.max=in->max;tmp.step=in->step;
  break;
 default:return 0;
 }
 *out=tmp;return 1;
}
int c6_view(const C6OwnedOption *o,C6SpecView *out) {
 if(!o||!out||o->type<CR_MENU_TOGGLE||o->type>CR_MENU_SLIDER||
    (o->type==CR_MENU_CHOICE&&(o->choice_count<2||o->choice_count>16)))return 0;
 /* Only c6_prepare-produced immutable owned objects are accepted by contract.
  * Publish pointers after output initialization: no pointer into stack tmp. */
 memset(out,0,sizeof *out);
 out->spec.kind=o->type==CR_MENU_TOGGLE?"toggle":o->type==CR_MENU_CHOICE?"choice":"slider";
 out->spec.label=o->label;out->spec.mod=o->mod;out->spec.mod_id=o->mod_id;
 out->spec.description=o->description[0]?o->description:NULL;out->spec.has_default=1;out->spec.default_value=o->default_value;
 if(o->type==CR_MENU_CHOICE) {
  for(uint32_t i=0;i<o->choice_count;++i)out->choices[i]=o->choices[i];
  out->spec.choices=out->choices;out->spec.choice_count=(int32_t)o->choice_count;
 }
 if(o->type==CR_MENU_SLIDER) {
  out->spec.has_min=out->spec.has_max=out->spec.has_step=1;
  out->spec.min=o->min;out->spec.max=o->max;out->spec.step=o->step;
 }
 return 1;
}
