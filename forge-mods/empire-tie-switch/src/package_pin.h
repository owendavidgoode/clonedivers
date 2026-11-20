/* Fail-closed build 25480438 RefcountedPackageSystem & engine package list verification (HellForge packages.c /
 * reference_data.h). Used by empire-tie-switch 0.3.0 ONLY when stock HellForge load_package() omits
 * 0x2C26BC4C6592FA14 (eagle_base) from kRefPackages. */
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define TIE_PKG_REQUEST_RVA      0x12FE350u
#define TIE_PKG_IDLE_RVA         0x12FE680u
#define TIE_PKG_SYSTEM_RVA       0x347CEB0u
#define TIE_GAME_MIN_IMAGE_SIZE  0x3485000u

#define TIE_EXE_ENGINE_RVA       0x1A10208u
#define TIE_EXE_HAS_LOADED_RVA   0x321330u
#define TIE_EXE_FIND_RVA         0x60DA80u
#define TIE_EXE_MIN_IMAGE_SIZE   0x1A11000u

#define TIE_PKG_TIMEOUT_MS       90000ull
#define TIE_SORTIE_TTL_MS        20000ull
#define TIE_MAX_SORTIES          16u
#define TIE_MAX_BEACONS          8u

static const uint8_t kTiePkgRequestPin[32] = {
    0x45, 0x85, 0xc0, 0x0f, 0x84, 0x75, 0x01, 0x00, 0x00, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xec,
    0x38, 0x48, 0x89, 0x5c, 0x24, 0x50, 0x4c, 0x8b, 0xfa, 0x48, 0x89, 0x6c, 0x24, 0x58, 0x4c, 0x8b
};

static const uint8_t kTiePkgIdlePin[32] = {
    0x48, 0x83, 0xec, 0x28, 0x48, 0x8b, 0x05, 0x7d, 0x7c, 0x02, 0x02, 0x48, 0x8b, 0x48, 0x10, 0xff,
    0x91, 0x40, 0x03, 0x00, 0x00, 0x85, 0xc0, 0x0f, 0x94, 0xc0, 0x48, 0x83, 0xc4, 0x28, 0xc3, 0xcc
};

static const uint8_t kTieExeHasLoadedPin[32] = {
    0x48, 0x83, 0xec, 0x28, 0x48, 0x8b, 0x05, 0xcd, 0xee, 0x6e, 0x01, 0x48, 0x8b, 0xd1, 0x48, 0x8b,
    0x88, 0x00, 0x04, 0x00, 0x00, 0x48, 0x8b, 0x89, 0x08, 0x02, 0x00, 0x00, 0xe8, 0x2f, 0xc7, 0x2e
};

static const uint8_t kTieExeFindPin[32] = {
    0x48, 0x89, 0x5c, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x48, 0x89, 0x7c, 0x24, 0x18, 0x8b,
    0xb9, 0x80, 0x00, 0x00, 0x00, 0x45, 0x33, 0xd2, 0x85, 0xff, 0x74, 0x40, 0x48, 0x8b, 0xb1, 0x88
};

typedef int (*TieReadMemFn)(uintptr_t addr, void* out, size_t size);

static int TiePinMatches(TieReadMemFn readCodeFn, uintptr_t base, size_t imageSize,
                         uint32_t rva, const uint8_t pin[32])
{
    uint8_t got[32];
    if (!readCodeFn || !base || (size_t)rva + sizeof got > imageSize) return 0;
    return readCodeFn(base + rva, got, sizeof got) == 0 && memcmp(got, pin, sizeof got) == 0;
}

static int TieVerifyPackagePins(TieReadMemFn readCodeFn,
                                uintptr_t gameBase, size_t gameSize,
                                uintptr_t exeBase, size_t exeSize,
                                char* err, size_t errCap)
{
    if (!readCodeFn || !gameBase || gameSize < TIE_GAME_MIN_IMAGE_SIZE) {
        if (err && errCap) snprintf(err, errCap, "game.dll image unavailable or undersized for package pins");
        return 0;
    }
    if (!exeBase || exeSize < TIE_EXE_MIN_IMAGE_SIZE) {
        if (err && errCap) snprintf(err, errCap, "helldivers2.exe image unavailable or undersized for package pins");
        return 0;
    }
    if (!TiePinMatches(readCodeFn, gameBase, gameSize, TIE_PKG_REQUEST_RVA, kTiePkgRequestPin)) {
        if (err && errCap) snprintf(err, errCap, "build 25480438 pin mismatch: game.dll+0x%X", TIE_PKG_REQUEST_RVA);
        return 0;
    }
    if (!TiePinMatches(readCodeFn, gameBase, gameSize, TIE_PKG_IDLE_RVA, kTiePkgIdlePin)) {
        if (err && errCap) snprintf(err, errCap, "build 25480438 pin mismatch: game.dll+0x%X", TIE_PKG_IDLE_RVA);
        return 0;
    }
    if (!TiePinMatches(readCodeFn, exeBase, exeSize, TIE_EXE_HAS_LOADED_RVA, kTieExeHasLoadedPin)) {
        if (err && errCap) snprintf(err, errCap, "build 25480438 pin mismatch: helldivers2.exe+0x%X", TIE_EXE_HAS_LOADED_RVA);
        return 0;
    }
    if (!TiePinMatches(readCodeFn, exeBase, exeSize, TIE_EXE_FIND_RVA, kTieExeFindPin)) {
        if (err && errCap) snprintf(err, errCap, "build 25480438 pin mismatch: helldivers2.exe+0x%X", TIE_EXE_FIND_RVA);
        return 0;
    }
    return 1;
}

/* Open-addressed package reference map headroom guard (<75% full). Refusing full maps prevents NULL write crash. */
static int TieRefMapHasHeadroom(const uint8_t* entries, uint32_t capacity, uint64_t emptyKey,
                                uint64_t pkgId, uint32_t* usedOut, int* alreadyPresent)
{
    uint32_t used = 0;
    int present = 0;
    if (usedOut) *usedOut = 0;
    if (alreadyPresent) *alreadyPresent = 0;
    if (!entries || capacity < 16u || capacity > 65536u || (capacity & (capacity - 1u)) != 0u ||
        pkgId == 0ULL || pkgId == emptyKey)
        return 0;
    for (uint32_t i = 0; i < capacity; i++) {
        uint64_t key = 0;
        memcpy(&key, entries + (size_t)i * 16u, sizeof key);
        if (key != emptyKey && key != 0ULL) {
            ++used;
            if (key == pkgId) present = 1;
        }
    }
    if (usedOut) *usedOut = used;
    if (alreadyPresent) *alreadyPresent = present;
    return present || ((uint64_t)(used + 1u) * 4ULL < (uint64_t)capacity * 3ULL);
}

static int TieReadPtr(TieReadMemFn readMemFn, uintptr_t at, uintptr_t* out)
{
    uint64_t v = 0;
    if (!readMemFn || at < 0x10000u || readMemFn(at, &v, sizeof v) != 0 || v < 0x10000ull || (v >> 32) > 0x7FFFull)
        return 0;
    *out = (uintptr_t)v;
    return 1;
}

static int TiePackageResidentExe(TieReadMemFn readMemFn, uintptr_t exeBase, size_t exeSize, uint64_t pkgId)
{
    uintptr_t engine = 0, manager = 0, resources = 0, list = 0;
    uint32_t count = 0;
    if (!readMemFn || !exeBase || exeSize < TIE_EXE_ENGINE_RVA + 8u || !pkgId) return 0;
    if (!TieReadPtr(readMemFn, exeBase + TIE_EXE_ENGINE_RVA, &engine) ||
        !TieReadPtr(readMemFn, engine + 0x400u, &manager) ||
        !TieReadPtr(readMemFn, manager + 0x208u, &resources) ||
        readMemFn(resources + 0x80u, &count, 4) != 0 || count == 0u || count > 8192u ||
        !TieReadPtr(readMemFn, resources + 0x88u, &list))
        return 0;

    for (uint32_t i = 0; i < count; i++) {
        uintptr_t pkg = 0, partsArr = 0;
        uint64_t id = 0;
        uint32_t parts = 0;
        if (!TieReadPtr(readMemFn, list + (uintptr_t)i * 8u, &pkg)) continue;
        if (readMemFn(pkg + 0x10u, &id, 8) != 0) continue;
        int matched = (id == pkgId);
        if (readMemFn(pkg + 0x18u, &parts, 4) != 0 || parts == 0u || parts > 1024u) continue;
        if (!TieReadPtr(readMemFn, pkg + 0x20u, &partsArr)) continue;
        if (!matched) {
            for (uint32_t p = 0; p < parts; p++) {
                uintptr_t part = 0;
                uint64_t partId = 0;
                if (TieReadPtr(readMemFn, partsArr + (uintptr_t)p * 8u, &part) &&
                    readMemFn(part + 0x10u, &partId, 8) == 0 && partId == pkgId) {
                    matched = 1;
                    break;
                }
            }
        }
        if (!matched) continue;
        for (uint32_t p = 0; p < parts; p++) {
            uintptr_t part = 0;
            uint32_t pstate = 0;
            if (!TieReadPtr(readMemFn, partsArr + (uintptr_t)p * 8u, &part) ||
                readMemFn(part + 0x1Cu, &pstate, 4) != 0 || pstate != 4u)
                return 0;
        }
        return 1;
    }
    return 0;
}
