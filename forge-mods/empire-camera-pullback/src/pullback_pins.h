/* Pinned engine RVAs, instruction byte signatures, walker resources, and the 61 Compact LEGO
 * (resource_id, expected_node_count) pairs for Helldivers 2 build 25480438 (HellForge SDK 16). */
#ifndef ECP_PULLBACK_PINS_H
#define ECP_PULLBACK_PINS_H

#include <stddef.h>
#include <stdint.h>

#define ECP_EXPECTED_BUILD "25480438"

/* helldivers2.exe RVAs (verified in PASS3.md against camera-capture-mission & terminal_camera.c). */
#define ECP_EXE_CAMERA_STEP       0x689BC0u
#define ECP_EXE_SET_LOCAL_POSE    0x1FCAD0u
#define ECP_EXE_UPDATE_UNIT       0x1B2DC0u
#define ECP_EXE_LOOKUP_UNIT       0x09D8C0u
#define ECP_EXE_MAIN_WORLD        0x317DE0u
#define ECP_EXE_PHYSICS_RAYCAST   0x77DEC0u
#define ECP_EXE_RAYCAST_QUERIES   0x27B9440u
#define ECP_EXE_UNIT_VTABLE       0x1676D80u

/* World & Unit & Camera struct offsets (verified in PASS3.md). */
#define ECP_WORLD_UNITS_COUNT     0x2305B8u
#define ECP_WORLD_UNITS_ARRAY     0x2305C0u
#define ECP_WORLD_RENDER_POSE     0x230920u
#define ECP_WORLD_UNITS_MAX       200000u

#define ECP_UNIT_REF_OFFSET       0x08u
#define ECP_UNIT_RES_PTR_OFFSET   0x30u
#define ECP_UNIT_CHILD_FIRST      0x1D8u
#define ECP_UNIT_CHILD_NEXT       0x1E0u
#define ECP_UNIT_VTABLE_GRAPH     0x0E8u
#define ECP_UNIT_VTABLE_CAM_COUNT 0x160u
#define ECP_UNIT_VTABLE_CAMERA    0x168u

#define ECP_GRAPH_NODE_COUNT      0x10u
#define ECP_GRAPH_LINK_PARENT     0x18u
#define ECP_GRAPH_LOCAL_POSES     0x20u
#define ECP_GRAPH_WORLD_POSES     0x28u
#define ECP_GRAPH_NODE_NAMES      0x40u
#define ECP_GRAPH_MAX_NODES       4096u

#define ECP_CAMERA_VERTICAL_FOV   0x34u

/* Physics raycast query constants (damage_los_query, mode 1 closest hit). */
#define ECP_GAME_PHYSICS_GLOBAL   0x3326328u
#define ECP_PHYSICS_RAYCAST_SLOT  0x68u
#define ECP_RAYCAST_QUERY_ROW     4u
#define ECP_RAYCAST_QUERY_ID      (0x80000000u | ECP_RAYCAST_QUERY_ROW)
#define ECP_RAYCAST_QUERY_HASH    0x2315515Du

/* Co-op PlayerManager / LocalUser / EntityManager / HealthManager offsets in game.dll. */
#define ECP_GAME_PLAYERS_GLOBAL   0x3326468u
#define ECP_GAME_LOCALUSER_GLOBAL 0x347CEF0u
#define ECP_GAME_ENTITIES_GLOBAL  0x346BF98u
#define ECP_GAME_HEALTH_GLOBAL    0x3326688u

#define ECP_LOCALUSER_PEER_OFFSET 0xB398u
#define ECP_PLAYERS_COUNT_OFFSET  0x84u
#define ECP_PLAYERS_DESC_OFFSET   0xE8u
#define ECP_PLAYERS_LOCAL_FLAG    20u
#define ECP_PLAYERS_PEER_OFFSET   0x2C8u
#define ECP_PLAYERS_LIFE_OFFSET   0x2E0u
#define ECP_PLAYERS_SLOT_STRIDE   0x38u
#define ECP_PLAYERS_NET_OFFSET    0x3A8u
#define ECP_PLAYERS_NET_STRIDE    0x20u
#define ECP_PLAYERS_MAX           4u
#define ECP_NET_ID_NONE           0x7FFFu
#define ECP_PLAYER_LIFE_SPAWNED   3u

#define ECP_EM_NETWORK_MAP        0xF22EC8u
#define ECP_EM_NETWORK_ENTITIES   0xF32F20u
#define ECP_EM_ENTITY_MAP         0xF1AEB0u
#define ECP_EM_DESCRIPTORS        0xF32F18u
#define ECP_EM_DESC_STRIDE        24u
#define ECP_EM_DESC_MAX           65536u

/* Hard O(1) / micro-slice / QPC frame-budget & safety-watchdog bounds (v0.1.1). */
#define ECP_HASH_PROBE_MAX        64u
#define ECP_MAX_AVATAR_CHILDREN   32u
#define ECP_MAX_TRACKED_WALKERS   16u
#define ECP_MAX_CUSTOM_WALKERS    16u
#define ECP_CAM_SCAN_SLICE_MAX    64u
#define ECP_WALKER_SCAN_SLICE_MAX 16u
#define ECP_SHIP_SCAN_SLICE_MAX   16u
#define ECP_SLICE_BUDGET_US       35ULL
#define ECP_FRAME_TARGET_US       50ULL
#define ECP_WATCHDOG_WARN_US      1000ULL
#define ECP_WATCHDOG_SEVERE_US    5000ULL
#define ECP_WATCHDOG_TRIP_SCORE   16u
#define ECP_HEALTH_FIRST_MS       5000ULL
#define ECP_HEALTH_INTERVAL_MS    30000ULL
#define ECP_HEALTH_MAX_ROWS       600u

#define ECP_HM_COUNT_OFFSET       0x1020u
#define ECP_HM_ENTITY_MAP         0x1030u
#define ECP_HM_RECORDS_PTR        0x1058u
#define ECP_HM_RECORD_STRIDE      0x1B8u
#define ECP_HM_HEALTH_OFFSET      0x14u
#define ECP_HM_LIFE_OFFSET        0x19Cu
#define ECP_HM_LIFE_ALIVE         0u

/* Resource & Bone Hashes. */
#define ECP_RES_HELLDIVER_AVATAR  0x4D1C334D294DFA97ULL
#define ECP_NODE_HASH_HEAD        0x8C5570C9u
#define ECP_NODE_HASH_ATST_PELVIS 0x9B115563u

/* First 32 bytes of each engine function in helldivers2.exe build 25480438. */
static const char kEcpPinCameraStep[] =
    "48 83 EC 58 33 C0 4C 8D 05 EB 71 00 01 48 89 44 24 30 48 8D 54 24 40 89 44 24 38 48 81 C1 90 01";
static const char kEcpPinSetLocalPose[] =
    "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B D8 48 63 FA E8 D6 0D EA FF 48 8B C8 48 8B F0";
static const char kEcpPinUpdateUnit[] =
    "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 55 41 54 41 55 41 56 41 57 48 8D AC 24 30 F8 FF FF";
static const char kEcpPinLookupUnit[] =
    "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 48 8B 35 1A 28 97 01 8B D9 48 8D 8E D0 00 00 00 FF";
static const char kEcpPinMainWorld[] =
    "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 48 8B 05 12 84 6F 01 33 C9 0F 57 C0 8B D1 8B D9 44";
static const char kEcpPinRaycast[] =
    "48 8B C4 48 89 58 10 48 89 70 18 57 48 83 EC 70 8B D9";

/* Combat Walker Stingray unit resources (EXO-45 Patriot, EXO-49 Emancipator, EXO-51 Lumberer, EXO-55 Breakthrough). */
static const uint64_t kEcpWalkerResources[4] = {
    0x79E4B3D2DA5E45E3ULL, /* EXO-45 Patriot (Empire AT-ST & Clones AT-TE) */
    0xC2D449ECF7FACAB1ULL, /* EXO-49 Emancipator (Clones AT-TE) */
    0x7B2326F6FD9C8069ULL, /* EXO-51 Lumberer (Clones AT-TE) */
    0x35DBF54F016F3624ULL, /* EXO-55 Breakthrough (Clones AT-TE) */
};

static inline int ecp_is_walker_resource(uint64_t res)
{
    for (size_t i = 0; i < sizeof(kEcpWalkerResources) / sizeof(kEcpWalkerResources[0]); i++) {
        if (kEcpWalkerResources[i] == res) return 1;
    }
    return 0;
}

typedef struct EcpLegoGarmentPin {
    uint64_t resource;
    uint32_t expected_nodes;
} EcpLegoGarmentPin;

/* Sorted table of all 61 Compact LEGO 3P garments (7 Empire baked + 54 Clone dynamic). */
#define ECP_LEGO_GARMENT_COUNT 61u
static const EcpLegoGarmentPin kEcpLegoGarments[ECP_LEGO_GARMENT_COUNT] = {
    {0x001A106B352493D5ULL, 21u}, /* commander_cody_left_leg */
    {0x0208D1FFFB6C1675ULL, 21u}, /* captain_rex_helmet */
    {0x0593D8E403C51BC2ULL, 47u}, /* phase1_shiny_left_arm */
    {0x0A09425DA11544D7ULL, 21u}, /* commander_cody_right_leg */
    {0x0ED7F5717E4DF15DULL, 18u}, /* phase2_501st_hips_bt1 */
    {0x1E25DAEB99B1A917ULL, 21u}, /* empire_baked_3 */
    {0x1FC7B7C36F0B1674ULL, 17u}, /* commander_cody_hips_bt1 */
    {0x24105347EBD202F4ULL, 17u}, /* phase2_212th_hips_bt1 */
    {0x29A22CECE5427D2DULL, 17u}, /* coruscant_guard_hips_bt1 */
    {0x2B507DE741D174E9ULL, 21u}, /* phase1_shiny_helmet */
    {0x2B7598966411556FULL, 27u}, /* commander_cody_torso_bt0 */
    {0x2BB094EDBC756963ULL, 21u}, /* empire_baked_0 */
    {0x322A9A41C3477441ULL, 21u}, /* coruscant_guard_right_leg */
    {0x33F67BDEDFF4B04EULL, 17u}, /* phase2_212th_hips_bt0 */
    {0x356EB2A710E33523ULL, 44u}, /* phase2_212th_left_arm */
    {0x37D0A8598D125564ULL, 27u}, /* phase2_501st_torso_bt0 */
    {0x3EBFC76341E98F39ULL, 21u}, /* phase2_212th_left_leg */
    {0x4185F04A2DD2FA90ULL, 18u}, /* phase2_501st_hips_bt0 */
    {0x4249A344A5268033ULL, 27u}, /* coruscant_guard_torso_bt1 */
    {0x453B56313057297AULL, 17u}, /* phase1_shiny_hips_bt0 */
    {0x4A1DE6A0D1FFC6A7ULL, 22u}, /* coruscant_guard_left_leg */
    {0x4AF7E6656DA96318ULL, 17u}, /* coruscant_guard_hips_bt0 */
    {0x4D2F00B6C4929F00ULL, 17u}, /* empire_baked_5 */
    {0x512399E005614901ULL, 43u}, /* empire_baked_4 */
    {0x51E5214EAE73D316ULL, 22u}, /* phase1_shiny_left_leg */
    {0x53D3B2302BF7BC85ULL, 21u}, /* captain_rex_right_leg */
    {0x572650ED5C6665CBULL, 25u}, /* captain_rex_hips_bt1 */
    {0x590D30736E66432CULL, 44u}, /* coruscant_guard_right_arm */
    {0x5A6301820AAAF66BULL, 22u}, /* phase2_501st_left_leg */
    {0x6B826D06AB7AF07CULL, 27u}, /* coruscant_guard_torso_bt0 */
    {0x7305FDF48136C05CULL, 21u}, /* empire_baked_1 */
    {0x7514090FA90DA6F0ULL, 43u}, /* phase2_212th_right_arm */
    {0x75548295338B975AULL, 22u}, /* phase1_shiny_right_leg */
    {0x7604826C8C96A881ULL, 17u}, /* commander_cody_hips_bt0 */
    {0x95EC84B1EFA977B3ULL, 27u}, /* phase2_212th_torso_bt1 */
    {0x9E291E101E16DC4FULL, 27u}, /* phase2_212th_torso_bt0 */
    {0xA111DE5354035DD7ULL, 44u}, /* commander_cody_right_arm */
    {0xA88278A6EA6B3E94ULL, 21u}, /* phase2_501st_helmet */
    {0xB26FA103A77BE3F9ULL, 21u}, /* captain_rex_left_leg */
    {0xB63D3C0BD23EE274ULL, 43u}, /* captain_rex_right_arm */
    {0xB850E0612E74845EULL, 25u}, /* captain_rex_hips_bt0 */
    {0xBAFFF2673EB5CBCAULL, 44u}, /* phase2_501st_right_arm */
    {0xBB11C8041C14C747ULL, 29u}, /* empire_baked_6 */
    {0xBFDC0F01475D16C8ULL, 21u}, /* commander_cody_helmet */
    {0xC11416CC62AA026CULL, 47u}, /* phase1_shiny_right_arm */
    {0xC5BCFEDD0D88A16BULL, 43u}, /* captain_rex_left_arm */
    {0xC6369F219C208C0DULL, 44u}, /* coruscant_guard_left_arm */
    {0xC86087DDDF60D9DEULL, 27u}, /* commander_cody_torso_bt1 */
    {0xCBB536EEDB3005EDULL, 44u}, /* phase2_501st_left_arm */
    {0xD184172E9096A6C2ULL, 27u}, /* phase1_shiny_torso_bt1 */
    {0xD3D41E93EC4C7842ULL, 29u}, /* captain_rex_torso_bt1 */
    {0xD5024911ADDDFEAAULL, 21u}, /* phase2_212th_right_leg */
    {0xDAB5777BFBBD1A83ULL, 27u}, /* phase1_shiny_torso_bt0 */
    {0xDD25333BDF5F0143ULL, 43u}, /* empire_baked_2 */
    {0xDDCAF179A5CF81B4ULL, 29u}, /* captain_rex_torso_bt0 */
    {0xE0EEAE016DA8C9EDULL, 44u}, /* commander_cody_left_arm */
    {0xE25339F051883B91ULL, 22u}, /* phase2_501st_right_leg */
    {0xE79251F8F34D351AULL, 21u}, /* coruscant_guard_helmet */
    {0xF2EEF185355BAE5EULL, 27u}, /* phase2_501st_torso_bt1 */
    {0xF4AF05AD6D4D73A4ULL, 21u}, /* phase2_212th_helmet */
    {0xFD1F3953E73C6DFBULL, 17u}, /* phase1_shiny_hips_bt1 */
};

/* Binary search verifying BOTH resource id AND exact Compact LEGO scene-graph node count.
 * Prevents false positives when vanilla armors share base resource ids without LEGO patch archives. */
static inline int ecp_match_lego_garment(uint64_t resource, uint32_t node_count)
{
    if (resource == 0 || node_count == 0) return 0;
    size_t lo = 0, hi = ECP_LEGO_GARMENT_COUNT;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2u;
        uint64_t cur = kEcpLegoGarments[mid].resource;
        if (cur == resource) {
            return kEcpLegoGarments[mid].expected_nodes == node_count ? 1 : 0;
        }
        if (cur < resource) lo = mid + 1u;
        else hi = mid;
    }
    return 0;
}

#endif /* ECP_PULLBACK_PINS_H */
