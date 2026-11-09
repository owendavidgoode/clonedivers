/* Standalone regression host using the installed LuaJIT DLL, without loading or starting the game.
 * Build with Zig cc -I../../../app/sdk tests/clean_exit_test.c -o build/clean_exit_test.exe -luser32.
 * Run modes logic, inplay, nonzero, hang, fault, normal. LOCALAPPDATA should point to an isolated test directory.
 */
#undef NDEBUG
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include "../src/clean_exit.c"

struct HfMod { const HfApi* api; };
static HfApi api;
static struct HfMod testMod;
static int quitCalls;
static lua_State* (*newState)(void);
static void (*openLibs)(lua_State*);
static int (*loadString)(lua_State*, const char*);
static void (*closeState)(lua_State*);
static void (*pushInteger)(lua_State*, ptrdiff_t);
static ptrdiff_t (*toInteger)(lua_State*, int);
static int (*luaError)(lua_State*);

static void TestLog(HfMod* mod, const char* format, ...)
{
    (void)mod; (void)format;
}

static const char* BuildId(void) { return "25480438"; }

static uintptr_t ModuleBase(const char* module, size_t* size)
{
    (void)module;
    uintptr_t base = (uintptr_t)GetModuleHandleW(NULL);
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    IMAGE_NT_HEADERS64* nt = (IMAGE_NT_HEADERS64*)(base + dos->e_lfanew);
    *size = nt->OptionalHeader.SizeOfImage;
    return base;
}

static int OnTick(HfMod* mod, HfTickCallback callback, void* user)
{
    (void)mod; (void)callback; (void)user;
    return 1;
}

static uint32_t State(HfMod* mod) { (void)mod; return HF_STATE_SHIP; }

static int Hook(HfMod* mod, uintptr_t target, void* detour, void** original)
{
    (void)mod;
    assert(detour == (void*)Pcall);
    *original = (void*)target; /* TestRun enters the production detour directly. */
    return 0;
}

static int NativeQuit(lua_State* state)
{
    assert(lua.gettop(state) <= 1);
    assert(toInteger(state, LUA_ORIGINAL_QUIT) == 73); /* the original closure's own upvalue survives */
    quitCalls++;
    if (toInteger(state, 1) == -1)
    {
        lua.pushstring(state, "quit rejected");
        return luaError(state);
    }
    if (lua.gettop(state)) lua.pushvalue(state, 1);
    else pushInteger(state, 0);
    pushInteger(state, toInteger(state, LUA_ORIGINAL_QUIT));
    return 2;
}

static int TestRun(lua_State* state, const char* source)
{
    assert(loadString(state, source) == 0);
    return Pcall(state, 0, LUA_MULTRET, 0);
}

static void NativeBinding(lua_State* state)
{
    assert(TestRun(state, "stingray = {Application = {}}") == 0);
    int top = lua.gettop(state);
    lua.pushstring(state, "stingray"); lua.rawget(state, LUA_GLOBALSINDEX);
    lua.pushstring(state, "Application"); lua.rawget(state, -2);
    lua.pushstring(state, "quit"); pushInteger(state, 73); lua.pushcclosure(state, NativeQuit, 1);
    lua.rawset(state, -3);
    lua.settop(state, top);
}

static void SyntheticFault(DWORD code, uintptr_t address)
{
    EXCEPTION_RECORD record = {0};
    record.ExceptionCode = code;
    record.ExceptionAddress = (void*)address;
    EXCEPTION_POINTERS pointers = {&record, NULL};
    assert(Fault(&pointers) == EXCEPTION_CONTINUE_SEARCH);
}

static DWORD WINAPI OtherThread(void* state)
{
    assert(TestRun(state, "return 19") == 0);
    return 0;
}

int main(int argc, char** argv)
{
    assert(argc == 2);
    HMODULE library = LoadLibraryW(L"D:\\HellForge\\game\\bin\\lua51.dll");
    assert(library != NULL);
#define RESOLVE(field, name) do { *(FARPROC*)(void*)&field = GetProcAddress(library, name); assert(field); } while (0)
    RESOLVE(newState, "luaL_newstate");
    RESOLVE(openLibs, "luaL_openlibs");
    RESOLVE(loadString, "luaL_loadstring");
    RESOLVE(closeState, "lua_close");
    RESOLVE(pushInteger, "lua_pushinteger");
    RESOLVE(toInteger, "lua_tointeger");
    RESOLVE(luaError, "lua_error");
#undef RESOLVE
    api.version = 16; api.size = sizeof api;
    api.log = TestLog; api.build_id = BuildId; api.module_base = ModuleBase;
    api.on_game_thread = OnTick; api.game_state = State; api.hook = Hook;
    testMod.api = &api;
    assert(hellforge_mod_init(&api, &testMod) == 0);
    lua_State* state = newState();
    assert(state != NULL);
    openLibs(state);
    Tick(&testMod, 0, NULL);

    if (!strcmp(argv[1], "logic"))
    {
        assert(TestRun(state, "return 17, 'kept'") == 0);
        assert(lua.gettop(state) == 2 && toInteger(state, 1) == 17 && wrapped == 0 && quitMs == 0);
        lua.settop(state, 0);
        assert(TestRun(state, "stingray = {Application = {quit = function() end}}") == 0);
        assert(wrapped == 0); /* a mod's Lua replacement is never mistaken for the native engine quit */
        assert(TestRun(state, "stingray = setmetatable({}, {__index = function() error('lookup') end})") == 0);
        assert(wrapped == 0 && lua.gettop(state) == 0); /* inspection does not invoke metatables */
        NativeBinding(state);
        HANDLE thread = CreateThread(NULL, 0, OtherThread, state, 0, NULL);
        assert(thread != NULL && WaitForSingleObject(thread, 3000) == WAIT_OBJECT_0);
        CloseHandle(thread);
        assert(wrapped == 0 && toInteger(state, -1) == 19);
        lua.settop(state, 0);
        assert(TestRun(state, "error('engine error')") != 0 && wrapped == 0 && lua.gettop(state) == 1);
        lua.settop(state, 0);
        assert(TestRun(state, "return 23, 29") == 0);
        assert(wrapped == 1 && lua.gettop(state) == 2 && toInteger(state, 1) == 23 && toInteger(state, 2) == 29);
        lua.settop(state, 0);
        assert(TestRun(state, "stingray.Application.quit(-1)") != 0);
        assert(quitCalls == 1 && quitMs == 0 && lua.gettop(state) == 1);
        lua.settop(state, 0);
        Sleep(QUIT_GRACE_MS + 200); /* rejected quit remains unarmed */
        assert(TestRun(state, "return stingray.Application.quit(0)") == 0);
        assert(quitCalls == 2 && quitMs != 0 && lua.gettop(state) == 2);
        assert(toInteger(state, 1) == 0 && toInteger(state, 2) == 73);
        LONGLONG firstQuit = quitMs;
        lua.settop(state, 0);
        assert(TestRun(state, "return stingray.Application.quit()") == 0 && quitMs == firstQuit);
        SyntheticFault(EXCEPTION_ACCESS_VIOLATION, exeBase + TEARDOWN_FAULT_RVA + 1);
        assert(crashPassed == 1 && WaitForSingleObject(worker, 1000) == WAIT_OBJECT_0);
        SyntheticFault(EXCEPTION_ACCESS_VIOLATION, exeBase + TEARDOWN_FAULT_RVA);
        Sleep(QUIT_GRACE_MS + 200); /* unrelated crash keeps its original handling past the grace */
    }
    else
    {
        NativeBinding(state);
        assert(TestRun(state, "return 1") == 0 && wrapped == 1);
        lua.settop(state, 0);
        if (!strcmp(argv[1], "inplay"))
        {
            lastTickMs = (LONGLONG)GetTickCount64() - 10000;
            lastState = HF_STATE_MISSION;
            SyntheticFault(EXCEPTION_ACCESS_VIOLATION, exeBase + TEARDOWN_FAULT_RVA);
            lastState = HF_STATE_SHIP;
            SyntheticFault(EXCEPTION_ACCESS_VIOLATION, exeBase + TEARDOWN_FAULT_RVA);
            assert(quitMs == 0 && crashPassed == 0);
            Sleep(QUIT_GRACE_MS + 500); /* idle in play or on the ship never starts a deadline */
        }
        else if (!strcmp(argv[1], "nonzero"))
        {
            assert(TestRun(state, "return stingray.Application.quit(7)") == 0);
            assert(quitMs == 0 && crashPassed == 1 && toInteger(state, 1) == 7 && toInteger(state, 2) == 73);
            Sleep(QUIT_GRACE_MS + 500);
        }
        else
        {
            assert(TestRun(state, "return stingray.Application.quit(0)") == 0 && quitMs != 0);
            if (!strcmp(argv[1], "hang")) { Sleep(QUIT_GRACE_MS + 5000); return 42; }
            if (!strcmp(argv[1], "fault")) { SyntheticFault(EXCEPTION_ACCESS_VIOLATION, exeBase + TEARDOWN_FAULT_RVA); return 43; }
            assert(!strcmp(argv[1], "normal"));
        }
    }
    SetEvent(stopEvent);
    assert(WaitForSingleObject(worker, 1000) == WAIT_OBJECT_0);
    closeState(state);
    puts("PASS");
    return 0;
}
