/* MIT. Executes the exact production stub in owned memory, without HD2. */
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>
#include "../src/mine_boundary.h"

static unsigned checks;
static void Check(bool okay, const char* message)
{
    if (!okay) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); }
    ++checks; std::printf("PASS: %s\n", message);
}
static uint32_t Count(const std::vector<unsigned char>& manager)
{
    uint32_t count; std::memcpy(&count, manager.data() + MINE_COUNT, 4); return count;
}
using Append = void(*)(void*, const void*);
static unsigned char* Fixture(bool guarded, volatile LONG* skipped)
{
    auto code = (unsigned char*)VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!code) std::exit(2);
    const unsigned char entry[] = {0x53,0x41,0x56,0x48,0x83,0xec,0x28,0x48,0x89,0xcb,0x49,0x89,0xd6};
    std::memcpy(code, entry, sizeof entry);
    MineJump(code + sizeof entry, (uintptr_t)(code + 512));
    std::memcpy(code + 128, mine_original + 14, sizeof mine_original - 14);
    MineJump(code + 128 + sizeof mine_original - 14, (uintptr_t)(code + 256));
    const unsigned char epilogue[] = {0x48,0x83,0xc4,0x28,0x41,0x5e,0x5b,0xc3};
    std::memcpy(code + 256, epilogue, sizeof epilogue);
    if (guarded) MineStub(code + 512, (uintptr_t)(code + 128), (uintptr_t)(code + 256), (uintptr_t)skipped);
    else { std::memcpy(code + 512, mine_original, 14); MineJump(code + 526, (uintptr_t)(code + 128)); }
    DWORD before;
    if (!VirtualProtect(code, 4096, PAGE_EXECUTE_READ, &before) || !FlushInstructionCache(GetCurrentProcess(), code, 4096)) std::exit(2);
    return code;
}
int main()
{
    Check(MINE_CAPACITY == 512, "buffer layout holds512 64-byte transforms before its count");
    volatile LONG skipped = 0;
    auto bad = Fixture(false, &skipped); auto good = Fixture(true, &skipped);
    unsigned char matrix[64]; for (unsigned i = 0; i < sizeof matrix; i++) matrix[i] = (unsigned char)(i + 1);
    uint32_t first = 0x3f7b6acf; std::memcpy(matrix, &first, 4);
    std::vector<unsigned char> manager(MINE_COUNT + 256, 0);
    for (unsigned i = 0; i < 513; i++) ((Append)bad)(manager.data(), matrix);
    Check(Count(manager) == 0x3f7b6ad0, "unguarded513th append reproduces the exact Q6 corrupted count");
    Check(0x16f857ffa88ull + ((uint64_t)Count(manager) << 6) + MINE_BUFFER == 0x17f646151e4ull,
        "corrupted count reproduces the exact Q6 invalid write address");
    std::fill(manager.begin(), manager.end(), 0);
    std::memset(manager.data() + MINE_COUNT + 4, 0xa5, 128);
    for (unsigned i = 0; i < 511; i++) ((Append)good)(manager.data(), matrix);
    Check(Count(manager) == 511 && skipped == 0, "first511 appends use unchanged original stores");
    ((Append)good)(manager.data(), matrix);
    Check(Count(manager) == 512 && skipped == 0, "512th append fills the last valid row without clipping");
    for (unsigned i = 0; i < 10000; i++) ((Append)good)(manager.data(), matrix);
    Check(Count(manager) == 512 && skipped == 10000, "10000 overflow appends leave count bounded at512");
    bool retained = true; for (unsigned i = 0; i < 512; i++) retained &= std::memcmp(manager.data() + MINE_BUFFER + i * 64, matrix, 64) == 0;
    Check(retained, "all512 retained transforms are byte-identical");
    bool canary = true; for (unsigned i = 0; i < 128; i++) canary &= manager[MINE_COUNT + 4 + i] == 0xa5;
    Check(canary, "adjacent game fields are untouched");
    ((Append)good)(manager.data(), nullptr);
    Check(Count(manager) == 512 && skipped == 10001, "full-buffer path never dereferences the supplied transform");
    uint32_t corrupt = 0x3f7b6ad0; std::memcpy(manager.data() + MINE_COUNT, &corrupt, 4);
    ((Append)good)(manager.data(), nullptr);
    Check(Count(manager) == corrupt && skipped == 10002, "unsigned guard also refuses an already-invalid index without another write");
    VirtualFree(bad, 0, MEM_RELEASE); VirtualFree(good, 0, MEM_RELEASE);
    std::printf("ALL %u NATIVE MACHINE-CODE CHECKS PASSED\n", checks);
    return 0;
}
