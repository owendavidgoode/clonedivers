/* Shared EntityManager descriptor table layout resolver for Helldivers 2 build 25480438
 * across HellForge 0.4.0-beta (Engine 0.1.47, 2,048 inline records at em + 0xF32F18) and
 * HellForge 0.5.0-beta+ (Engine 0.1.58+, 4,096 relocated records behind the Game block).
 *
 * Follows HellForge SDK 20 docs/modding/hooks.md ("Fields a mod moved") and entity_table.c:
 *   - Signed record offset = 8LL * (*(int32_t*)(game.dll + 0xFD9DDE)) [prefix 48 8D 80 at +0xFD9DDB],
 *     cross-checked against *(int32_t*)(game.dll + 0x6F41F7) [prefix 49 8D 85 at +0x6F41F4].
 *   - Record count = *(uint32_t*)(game.dll + 0xFDA1C5) [prefix 8B D0 BA at +0xFDA1C2],
 *     cross-checked against *(uint32_t*)(game.dll + 0xFDC06F) [prefix F3 00 3D at +0xFDC06C].
 *   - Fails closed to { .records_offset = 0xF32F18LL, .capacity = 2048u, .bounded_capacity = 2048u }
 *     when synthetic unit-test arenas do not populate those instruction sites or sites disagree.
 */
#ifndef CLONEDIVERS_EM_LAYOUT_H
#define CLONEDIVERS_EM_LAYOUT_H

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CD_EM_DEFAULT_OFFSET      0xF32F18LL
#define CD_EM_DEFAULT_CAPACITY    2048u
#define CD_EM_MAX_CAPACITY        4096u
#define CD_EM_RECORD_STRIDE       24u
#define CD_EM_NET_ENTITY_FIELD    8u
#define CD_EM_GWNAV_SAFE_LIVE_MAX 868u

#define CD_EM_SITE_REC8_RVA       0xFD9DDBu /* 48 8D 80 <disp32: offset / 8> */
#define CD_EM_SITE_REC1_RVA       0x6F41F4u /* 49 8D 85 <disp32: offset>     */
#define CD_EM_SITE_CNT1_RVA       0xFDA1C2u /* 8B D0 BA <imm32: count>       */
#define CD_EM_SITE_CNT2_RVA       0xFDC06Cu /* F3 00 3D <imm32: count>       */

typedef struct CdEmLayout {
    int64_t records_offset;
    uint32_t capacity;
    uint32_t bounded_capacity;
    int relocated;
} CdEmLayout;

typedef int (*CdEmHfReadFn)(uintptr_t address, void* buffer, size_t size);   /* 0 == success (HfApi.read) */
typedef int (*CdEmBoolReadFn)(uintptr_t address, void* buffer, size_t size); /* 1 == success              */

static inline CdEmLayout cd_em_default_layout(void)
{
    CdEmLayout d;
    d.records_offset = CD_EM_DEFAULT_OFFSET;
    d.capacity = CD_EM_DEFAULT_CAPACITY;
    d.bounded_capacity = CD_EM_DEFAULT_CAPACITY;
    d.relocated = 0;
    return d;
}

static inline CdEmLayout cd_em_decode_sites(const uint8_t rec8[7], const uint8_t rec1[7],
                                            const uint8_t cnt1[7], const uint8_t cnt2[7])
{
    static const uint8_t kRec8Prefix[3] = {0x48u, 0x8Du, 0x80u};
    static const uint8_t kRec1Prefix[3] = {0x49u, 0x8Du, 0x85u};
    static const uint8_t kCnt1Prefix[3] = {0x8Bu, 0xD0u, 0xBAu};
    static const uint8_t kCnt2Prefix[3] = {0xF3u, 0x00u, 0x3Du};

    CdEmLayout out = cd_em_default_layout();
    if (memcmp(rec8, kRec8Prefix, 3u) != 0 || memcmp(rec1, kRec1Prefix, 3u) != 0) {
        return out;
    }

    int32_t disp8 = 0, disp1 = 0;
    memcpy(&disp8, rec8 + 3u, 4u);
    memcpy(&disp1, rec1 + 3u, 4u);
    int64_t off8 = (int64_t)disp8 * 8LL;
    int64_t off1 = (int64_t)disp1;
    if (off8 != off1 || off8 == 0LL || off8 < -0x40000000LL || off8 > 0x40000000LL) {
        return out;
    }

    uint32_t cap = CD_EM_DEFAULT_CAPACITY;
    if (memcmp(cnt1, kCnt1Prefix, 3u) == 0 && memcmp(cnt2, kCnt2Prefix, 3u) == 0) {
        uint32_t c1 = 0u, c2 = 0u;
        memcpy(&c1, cnt1 + 3u, 4u);
        memcpy(&c2, cnt2 + 3u, 4u);
        if (c1 != c2 || c1 < 256u || c1 > 65536u) {
            return out;
        }
        cap = c1;
    }

    out.records_offset = off8;
    out.capacity = cap;
    out.bounded_capacity = (cap > CD_EM_MAX_CAPACITY) ? CD_EM_MAX_CAPACITY : cap;
    out.relocated = (off8 != CD_EM_DEFAULT_OFFSET || cap != CD_EM_DEFAULT_CAPACITY) ? 1 : 0;
    return out;
}

static inline CdEmLayout cd_em_resolve_hf(uintptr_t game_base, CdEmHfReadFn hf_read)
{
    uint8_t rec8[7], rec1[7], cnt1[7], cnt2[7];
    if (game_base < 0x10000u || !hf_read) return cd_em_default_layout();
    if (hf_read(game_base + CD_EM_SITE_REC8_RVA, rec8, sizeof rec8) != 0 ||
        hf_read(game_base + CD_EM_SITE_REC1_RVA, rec1, sizeof rec1) != 0 ||
        hf_read(game_base + CD_EM_SITE_CNT1_RVA, cnt1, sizeof cnt1) != 0 ||
        hf_read(game_base + CD_EM_SITE_CNT2_RVA, cnt2, sizeof cnt2) != 0) {
        return cd_em_default_layout();
    }
    return cd_em_decode_sites(rec8, rec1, cnt1, cnt2);
}

static inline CdEmLayout cd_em_resolve_bool(uintptr_t game_base, CdEmBoolReadFn ok_read)
{
    uint8_t rec8[7], rec1[7], cnt1[7], cnt2[7];
    if (game_base < 0x10000u || !ok_read) return cd_em_default_layout();
    if (!ok_read(game_base + CD_EM_SITE_REC8_RVA, rec8, sizeof rec8) ||
        !ok_read(game_base + CD_EM_SITE_REC1_RVA, rec1, sizeof rec1) ||
        !ok_read(game_base + CD_EM_SITE_CNT1_RVA, cnt1, sizeof cnt1) ||
        !ok_read(game_base + CD_EM_SITE_CNT2_RVA, cnt2, sizeof cnt2)) {
        return cd_em_default_layout();
    }
    return cd_em_decode_sites(rec8, rec1, cnt1, cnt2);
}

static inline uintptr_t cd_em_records_base(uintptr_t em_root, int64_t records_offset)
{
    if (em_root < 0x10000u) return 0u;
    if (records_offset >= 0) {
        uintptr_t off = (uintptr_t)records_offset;
        if (off > UINTPTR_MAX - em_root) return 0u;
        return em_root + off;
    } else {
        uintptr_t mag = (uintptr_t)(-(records_offset + 1LL)) + 1u;
        if (mag >= em_root || em_root - mag < 0x10000u) return 0u;
        return em_root - mag;
    }
}

static inline uintptr_t cd_em_record_at(uintptr_t em_root, int64_t records_offset, uint32_t slot)
{
    uintptr_t base = cd_em_records_base(em_root, records_offset);
    if (!base) return 0u;
    uintptr_t byte_off = (uintptr_t)slot * CD_EM_RECORD_STRIDE;
    if (byte_off > UINTPTR_MAX - base) return 0u;
    return base + byte_off;
}

/* Scales a 2,048-table free-slot reserve (such as 1,180 free slots = max 868 live entities)
 * so the same live-entity / <1,024-GwNav::Bot ceiling holds on 4,096-record tables. */
static inline uint32_t cd_em_scale_reserve(uint32_t bounded_capacity, uint32_t reserve_at_2048)
{
    if (reserve_at_2048 == 0u || bounded_capacity <= CD_EM_DEFAULT_CAPACITY) {
        return reserve_at_2048;
    }
    if (reserve_at_2048 >= CD_EM_DEFAULT_CAPACITY) {
        return bounded_capacity;
    }
    uint32_t max_live = CD_EM_DEFAULT_CAPACITY - reserve_at_2048;
    return bounded_capacity - max_live;
}

#ifdef __cplusplus
}
#endif

#endif /* CLONEDIVERS_EM_LAYOUT_H */
