/* Own-process proof that writer v0.0.6 preparation selects enum5 and preserves CSR across leaf tests. */
#define main inherited_leaf_main
#include "leaf_test.c"
#undef main
int main(int argc,char **argv){
 static cm_rig selected_rig;
 const unsigned csrs[]={0x1f80,0x9fa0,0x9fbd,0x3fc0};
 for(unsigned i=0;i<sizeof csrs/sizeof csrs[0];i++){
  _mm_setcsr(csrs[i]);CHECK(sm_prepare(&selected_rig)==CM_OK);
  CHECK(selected_rig.policy==CM_NECK_AND_ARM_SHOULDER_HINGE);CHECK(_mm_getcsr()==csrs[i]);
 }
 return inherited_leaf_main(argc,argv);
}
