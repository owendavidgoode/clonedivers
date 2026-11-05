/* MIT. Real SDK wrapper against owned-memory fixtures and explicit API contracts. */
#include <vector>
#include <cstdio>
#include <cstdlib>
#include "../src/mine_boundary.c"

static unsigned checks, subscriptions, callbacks, writes;
static int subscription_result = 1, callback_result, write_result;
static const char* build = "25480438";
static std::vector<unsigned char> memory(MINE_FINISH + 32, 0);
static void Check(bool okay, const char* why) { if (!okay) { std::fprintf(stderr, "FAIL: %s\n", why); std::exit(1); } ++checks; std::printf("PASS: %s\n", why); }
static const char* Build() { return build; }
static int Subscribe(HfMod*, uint32_t, HfListener, void*, int32_t) { ++subscriptions; return subscription_result; }
static int On(HfMod*, HfEvent event, HfEventCallback, void*) { if (event != HF_EVENT_GAME_LOADED) std::exit(2); ++callbacks; return callback_result; }
static void Log(HfMod*, const char*, ...) {}
static uintptr_t Base(const char*, size_t* n) { *n = memory.size(); return (uintptr_t)memory.data(); }
static int Read(uintptr_t at, void* out, size_t n) {
    auto start = (uintptr_t)memory.data();
    if (at < start || at - start > memory.size() || n > memory.size() - (at - start)) return 1;
    std::memcpy(out, (void*)at, n); return 0;
}
static int Write(uintptr_t at, const void* data, size_t n) {
    if (at != (uintptr_t)memory.data() + MINE_SITE || n != 14) std::exit(2);
    ++writes; std::memcpy((void*)at, data, n); return write_result;
}
int main()
{
    HfApi api{}; api.version = 15; api.size = sizeof api; api.build_id = Build;
    api.subscribe = Subscribe; api.on = On; api.log = Log; api.module_base = Base; api.read = Read; api.write = Write;
    auto owner = reinterpret_cast<HfMod*>(1);
    Check(hellforge_mod_init(&api, owner) == 0 && subscriptions == 2 && callbacks == 1 && writes == 0,
        "SDK subscribe=1 and on=0 are success; initialization registers both reports and game-loaded installation without writes");
    subscriptions = callbacks = 0; subscription_result = 0;
    Check(hellforge_mod_init(&api, owner) == 1 && subscriptions == 1 && callbacks == 0, "failed subscription refuses installation");
    subscription_result = 1; callback_result = 1;
    Check(hellforge_mod_init(&api, owner) == 1, "failed game-loaded registration refuses initialization");
    callback_result = 0; build = "different"; subscriptions = callbacks = 0;
    Check(hellforge_mod_init(&api, owner) == 1 && subscriptions == 0 && callbacks == 0, "another game build gets no subscriptions or writes");
    build = "25480438";
    Check(hellforge_mod_init(&api, owner) == 0, "supported build recovers with correct SDK contracts");
    std::memcpy(memory.data() + MINE_SITE, mine_original, sizeof mine_original);
    memory[MINE_SITE + 66] ^= 1;
    Install(owner, HF_EVENT_GAME_LOADED, nullptr);
    Check(!installed && writes == 0, "mismatch anywhere in all67 original bytes refuses every code write");
    std::memcpy(memory.data() + MINE_SITE, mine_original, sizeof mine_original);
    Install(owner, HF_EVENT_GAME_LOADED, nullptr);
    Check(installed && writes == 1 && memory[MINE_SITE] == 0xff && memory[MINE_SITE + 1] == 0x25,
        "matching decoded code installs and verifies the absolute jump through the SDK");
    Check(std::memcmp(memory.data() + MINE_SITE + 14, mine_original + 14, sizeof mine_original - 14) == 0,
        "the remainder of the original append is untouched");
    Install(owner, HF_EVENT_GAME_LOADED, nullptr);
    Check(writes == 1, "a repeated load event never patches twice");
    installed = 0; write_result = 1;
    std::memcpy(memory.data() + MINE_SITE, mine_original, sizeof mine_original);
    Install(owner, HF_EVENT_GAME_LOADED, nullptr);
    uint64_t target; std::memcpy(&target, memory.data() + MINE_SITE + 6, 8);
    MEMORY_BASIC_INFORMATION info{};
    Check(!installed && VirtualQuery((void*)(uintptr_t)target, &info, sizeof info) == sizeof info &&
        info.State == MEM_COMMIT && info.Protect == PAGE_EXECUTE_READ,
        "a reported write failure after publication cannot leave a jump into freed stub memory");
    std::printf("ALL %u SDK WRAPPER CHECKS PASSED\n", checks);
}
