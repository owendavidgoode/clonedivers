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
int OptionsWaveCeiling(void);
int OptionsCloneWars(void);
int OptionsEffectiveExtraSquads(int live_diff);
int OptionsEffectiveSupportSquads(int live_diff);
int OptionsEffectiveCloneWars(int live_diff);
void OptionsResolveForDifficulty(
    int live_diff,
    int* out_clone_wars,
    int* out_constant,
    int* out_squads,
    int* out_interval_ms,
    int* out_ceiling);

#endif
