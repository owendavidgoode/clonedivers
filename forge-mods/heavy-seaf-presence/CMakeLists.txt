cmake_minimum_required(VERSION 3.25)
project(heavy_seaf_presence CXX)
include(../../app/sdk/cmake/HellForgeMod.cmake)

hellforge_add_mod(heavy_seaf_presence src/heavy_seaf_presence.cpp src/options_menu.cpp src/tm_tick.cpp src/seaf_squads/seaf_squads.cpp src/seaf_squads/hunters.cpp src/seaf_squads/squad_pool.cpp)
