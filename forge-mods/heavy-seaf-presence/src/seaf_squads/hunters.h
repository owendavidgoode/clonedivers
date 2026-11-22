#pragma once
#include "maintenance.h"
namespace seaf { namespace hunters {
/* Tank Hunter squads: while one of their soldiers has a heavy or elite (armor 4 and up) in its perception, the other
 * enemies are hidden from that soldier's AI, so it picks the heavy. frame() runs once a game update (puts everything
 * back when the switch is off), maintain() once per Tank Hunter soldier, forget() when a soldier is dropped. */
void frame(float dt);
void maintain(const Soldier& soldier, float dt);
void forget(uint32_t id);
void reset();
}}
