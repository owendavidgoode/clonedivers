cmake_minimum_required(VERSION 3.25)
project(corpse_cleanup C)
if(NOT HELLFORGE_SDK)
  set(HELLFORGE_SDK "${CMAKE_CURRENT_LIST_DIR}/../../app/sdk")
endif()
include("${HELLFORGE_SDK}/cmake/HellForgeMod.cmake")
# Eight client translation units; runtime tests and installation are separate.
# Keep HELLFORGE_INSTALL empty unless installation is explicitly authorized.
hellforge_add_mod(corpse_cleanup
  src/corpse_cleanup.c src/c6_spike.c src/c6_guard.c src/c6_sha256.c
  src/c6_mapping.c src/c6_client.c src/c6_trace.c src/mom_adapter.c)
