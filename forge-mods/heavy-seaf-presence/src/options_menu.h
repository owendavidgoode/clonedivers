#ifndef HEAVY_SEAF_OPTIONS_MENU_H
#define HEAVY_SEAF_OPTIONS_MENU_H

#include "hellforge_mod.h"

/* CONSTANT DROPS defaults OFF unless clonedivers-host-options.json sets "constant"
 * or the player enables it in-game on the MODS page. */
void OptionsMenuStart(const HfApi* api, HfMod* mod);
void OptionsMenuPoll(float dt);
int OptionsConstant(void);
int OptionsSupportSquads(void);
int OptionsTankHunters(void);
int OptionsWaveIntervalMs(void);
int OptionsSquadsPerWave(void);
int OptionsExtraSquads(void);

#endif
