/* MIT. Shared by the SDK mod and the native machine-code regression fixture. */
#ifndef CLONEDIVERS_MINE_BOUNDARY_H
#define CLONEDIVERS_MINE_BOUNDARY_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

#define MINE_SITE 0xAD36A3u
#define MINE_CONTINUE 0xAD36B1u
#define MINE_FINISH 0xAD3DBDu
#define MINE_BUFFER 0x6A35Cu
#define MINE_COUNT 0x7235Cu
#define MINE_CAPACITY ((MINE_COUNT - MINE_BUFFER) / 64u)

/* Actual decoded instructions from the pinned game, including all four stores
 * and the count increment. Check the complete block before replacing its prefix. */
static const unsigned char mine_original[67] = {
    0x8b,0x83,0x5c,0x23,0x07,0x00,0x41,0x0f,0x10,0x06,0x48,0xc1,0xe0,0x06,
    0x41,0x0f,0x10,0x4e,0x10,0x41,0x0f,0x10,0x56,0x20,0x41,0x0f,0x10,0x5e,0x30,
    0x0f,0x11,0x84,0x18,0x5c,0xa3,0x06,0x00,0x0f,0x11,0x8c,0x18,0x6c,0xa3,0x06,0x00,
    0x0f,0x11,0x94,0x18,0x7c,0xa3,0x06,0x00,0x0f,0x11,0x9c,0x18,0x8c,0xa3,0x06,0x00,
    0xff,0x83,0x5c,0x23,0x07,0x00
};

static void MineJump(unsigned char* out, uintptr_t target)
{
    out[0] = 0xff; out[1] = 0x25;
    memset(out + 2, 0, 4);
    uint64_t address = (uint64_t)target;
    memcpy(out + 6, &address, 8);
}

static size_t MineStub(unsigned char* out, uintptr_t valid, uintptr_t full, uintptr_t skipped)
{
    /* Valid path replays the overwritten instructions, with the same shl flags.
     * Full path never reads the transform, leaves the count and all other game
     * data untouched, and resumes at the original branch's shared epilogue. */
    static const unsigned char prefix[] = {
        0x8b,0x83,0x5c,0x23,0x07,0x00,       /* mov eax,[rbx+MINE_COUNT] */
        0x3d,0x00,0x02,0x00,0x00,             /* cmp eax,512 */
        0x0f,0x83,0x16,0x00,0x00,0x00,       /* jae +22 -> full at39 */
        0x41,0x0f,0x10,0x06,                   /* movups xmm0,[r14] */
        0x48,0xc1,0xe0,0x06                    /* shl rax,6 */
    };
    memcpy(out, prefix, sizeof prefix);
    MineJump(out + 25, valid);
    out[39] = 0x50;                            /* push rax */
    out[40] = 0x48; out[41] = 0xb8;            /* mov rax,skipped */
    uint64_t address = (uint64_t)skipped;
    memcpy(out + 42, &address, 8);
    out[50] = 0xf0; out[51] = 0xff; out[52] = 0x00; /* lock inc dword [rax] */
    out[53] = 0x58;                            /* pop rax */
    MineJump(out + 54, full);
    return 68;
}
#endif
