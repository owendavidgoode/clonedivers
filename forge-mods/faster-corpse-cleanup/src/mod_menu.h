/* Corpse client-local selection. Exact-target experimental native route; UMM
 * presence wins and selection never changes during this DLL lifetime. MIT. */
#ifndef CR_MOD_MENU_H
#define CR_MOD_MENU_H
#include "menu_contract.h"
#include "menu_umm_adapter.h"
#include "menu_trace.h"
#ifndef C6_NATIVE_EXPERIMENTAL_ENABLED
#define C6_NATIVE_EXPERIMENTAL_ENABLED 1
#endif
#if defined(_WIN32) && C6_NATIVE_EXPERIMENTAL_ENABLED
#include "menu_c6_adapter.h"
static const CrMenuApi *cc_selected;
static const CrMenuApi *cc_menu_backend(void)
{
 if(!cc_selected){
  if(cr_wire_discover())cc_selected=&cc_umm_menu_api;
  else if(cc_c6_selectable())cc_selected=&cc_c6_menu_api;
  else cc_selected=&cc_umm_menu_api;
  cc_menu_trace(C6T_SELECT,3,cc_selected==&cc_c6_menu_api?2:1,cc_c6_mapping.attempted,0,0);
 }
 return cc_selected;
}
static int cc_menu_is_native(void){return cc_selected==&cc_c6_menu_api;}
#else
static const CrMenuApi *cc_menu_backend(void){return &cc_umm_menu_api;}
static int cc_menu_is_native(void){return 0;}
#endif
#endif
