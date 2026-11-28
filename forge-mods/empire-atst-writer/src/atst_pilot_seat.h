/* AT-ST Pilot Cab Seat Lift & Lower-Body/Cape Render Suppression (v0.2.7).
 *
 * Raises any Helldiver avatar (0x4D1C334D294DFA97) mounted in an admitted 106-node S2c AT-ST
 * from native attach_driver (s2c=39, Z=2.68m dangling between the walker shins) into the
 * retargeted 2.28x armored head cab (s2c=32 cockpit, Z~6.52m, pelvis Z~7.49m, head Z~8.07m),
 * keeping pure rigid rotation + unit scale {1,1,1} (det=+1.0) so lego-compact-leaf-writer's
 * cm_solve_parent succeeds with CM_OK, while collapsing lower-body (hips/legs/feet) and cape
 * skinning matrices in 5B2990 to zero triangle area inside the cab. */
#ifndef ATST_PILOT_SEAT_H
#define ATST_PILOT_SEAT_H

#include "atst_code_pins.h"
#include "atst_policy.h"
#include "atst_walker_registry.h"

#include <stddef.h>
#include <stdint.h>

#define AT_HELLDIVER_AVATAR_ID    0x4D1C334D294DFA97ULL
#define AT_HELLDIVER_NODES        107u
#define AT_S2C_COCKPIT_NODE       32u
#define AT_S2C_ATTACH_DRIVER_NODE 39u

/* Cab-local offset relative to retargeted s2c=32 cockpit ([0.0, -1.1271, 6.6710]):
 *   pilot root -> [0.00, -1.077, 6.521], pelvis -> [0.00, -1.10, 7.49], head -> [0.00, -1.10, 8.07],
 *   centered inside the armored AT-ST cab (X: +-1.77m, Y: -3.38..+1.42m, Z: 5.87..8.64m). */
#define AT_PILOT_CAB_OFFSET_X     0.00f
#define AT_PILOT_CAB_OFFSET_Y     0.05f
#define AT_PILOT_CAB_OFFSET_Z    (-0.15f)
#define AT_PILOT_WAIST_MODEL_Z    0.95f

#define AT_MAX_PILOT_SLOTS        4u
#define AT_MAX_PILOT_CHILDREN     32u
#define AT_MAX_UNIT_MESH_ENTRIES  16u
#define AT_MAX_PILOT_HIDE_MESHES  64u
#define AT_PILOT_MESH_HT_SIZE     128u
#define AT_PILOT_MESH_HT_MASK     (AT_PILOT_MESH_HT_SIZE - 1u)

typedef void (*at_set_local_pose_fn)(uint32_t unit_ref, int32_t node, const float pose[16]);
typedef void (*at_update_unit_fn)(uintptr_t world, uintptr_t unit);
typedef uintptr_t (*at_main_world_fn)(void);
typedef uintptr_t (*at_unit_graph_fn)(uintptr_t unit);
typedef void (*at_camera_step_fn)(void *app);
typedef uint64_t (*at_camera_sub_step_fn)(void *rcx, void *rdx, void *r8, uint64_t r9, void *arg5);

typedef struct {
    uintptr_t mesh;
    uint8_t hide_all;
    uint8_t pad[7];
    uint64_t mask_lo;
    uint64_t mask_hi;
} at_pilot_hide_entry;

typedef struct {
    uint8_t active;
    uint8_t has_written;
    uint32_t avatar_ref;
    uintptr_t avatar_unit;
    uintptr_t walker_unit;
    uintptr_t world;
    float last_base_pose[16];
    float last_written_pose[16];
} at_pilot_slot;

typedef struct {
    at_set_local_pose_fn set_local_pose;
    at_update_unit_fn update_unit;
    at_main_world_fn main_world;
    at_unit_graph_fn unit_graph_override; /* Optional hook for unit tests without live vtable */
    at_pilot_slot slots[AT_MAX_PILOT_SLOTS];
    uint32_t lifted_frames;
    uint32_t hidden_leaf_calls;
} at_pilot_state;

/** Return 1 if resource_id is one of the 32 Compact LEGO hips / left_leg / right_leg garments. */
int at_is_lego_lower_garment(uint64_t resource_id);

/** Return 1 if resource_id is any of the 79 Compact LEGO garments (including upper body). */
int at_is_any_lego_garment(uint64_t resource_id);

/** Compute the lifted local pose[16] for a Helldiver mounted on walker_world[attach_node]. */
int at_pilot_compute_cab_local_pose(
    const at_rig *rig,
    const float walker_world_64[AT_S2C_NODES][16],
    uint32_t attach_node,
    const float base_local_pose[16],
    float out_local_pose[16]);

/** Collapse lower-body/cape palette entries in 5B2990 output buffer dst[0..count-1] to cab waist point. */
uint32_t at_pilot_collapse_leaf_output(
    void *dst,
    const void *palette,
    const void *map,
    uint32_t count,
    int hide_all,
    uint64_t mask_lo,
    uint64_t mask_hi,
    at_safe_read_fn read_fn,
    void *read_user);

#endif /* ATST_PILOT_SEAT_H */
