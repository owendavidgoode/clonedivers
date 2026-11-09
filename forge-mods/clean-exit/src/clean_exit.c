/* Clonedivers Clean Exit, game build 25480438, SDK 16.
 *
 * Application.quit exits the application (Autodesk Stingray Lua API):
 * https://help.autodesk.com/cloudhelp/ENU/Stingray-Help/lua_ref/ns_stingray_Application.html
 * Wrap that binding, forwarding its original closure, arguments, results and errors. Only a successful zero-exit
 * request commits the watchdog. A ship/title state, idle
 * clock, abandon, window message or session-end event never arms it. The latter arrives too late for teardown hangs.
 *
 * Discover the binding on the game thread after a successful engine lua_pcall. Use Lua's protected C-call API while
 * inspecting its tables, preserve the caller's stack, and accept only a native binding inside helldivers2.exe.
 * No offsets into the protected engine are guessed. If discovery fails, the mod stays inert and reports why.
 * Cached quit references taken before wrapping, and quit paths that bypass this binding, need live verification.
 *
 * Keep v0.1's exact render-pool fault and idle/state checks, with an additional committed-quit requirement. Other
 * serious exceptions during the grace period cancel the watchdog, so their crash handling can finish normally.
 * The worker and pre-opened log need no game, loader, Lua or CRT calls during teardown. This mod is boot-only.
 */
#include "hellforge_mod.h"
#include <windows.h>

#define TEARDOWN_FAULT_RVA 0x626296u
#define LOOP_STOPPED_MS 150u
#define QUIT_GRACE_MS 3000u
#define LUA_GLOBALSINDEX (-10002)
#define LUA_ORIGINAL_QUIT (LUA_GLOBALSINDEX - 1)
#define LUA_TTABLE 5
#define LUA_TNIL 0
#define LUA_TNUMBER 3
#define LUA_MULTRET (-1)

typedef struct lua_State lua_State;
typedef int (*LuaFunction)(lua_State* state);
static struct
{
    int (*gettop)(lua_State*);
    void (*settop)(lua_State*, int);
    void (*pushstring)(lua_State*, const char*);
    void (*rawget)(lua_State*, int);
    void (*rawset)(lua_State*, int);
    int (*type)(lua_State*, int);
    double (*tonumber)(lua_State*, int);
    LuaFunction (*tocfunction)(lua_State*, int);
    void (*pushvalue)(lua_State*, int);
    void (*pushcclosure)(lua_State*, LuaFunction, int);
    void (*insert)(lua_State*, int);
    void (*call)(lua_State*, int, int);
    int (*cpcall)(lua_State*, LuaFunction, void*);
} lua;
static int (*originalPcall)(lua_State*, int, int, int);

static volatile LONGLONG lastTickMs, quitMs;
static volatile LONG lastState, gameThread, enabled, wrapped, crashPassed;
static uintptr_t exeBase;
static size_t exeSize;
static HfMod* self;
static HANDLE logFile = INVALID_HANDLE_VALUE, quitEvent, stopEvent, worker;
static PVOID faultHandler;

/* One append per line, using only Win32 and a pre-opened file, including from the fault handler. */
static void Record(const char* message)
{
    SYSTEMTIME time;
    char line[512];
    GetSystemTime(&time);
    int length = wsprintfA(line, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ pid=%lu %s\r\n",
                          time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond,
                          time.wMilliseconds, GetCurrentProcessId(), message);
    DWORD written;
    if (logFile != INVALID_HANDLE_VALUE && length > 0) WriteFile(logFile, line, (DWORD)length, &written, NULL);
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    InterlockedCompareExchange(&gameThread, (LONG)GetCurrentThreadId(), 0);
    InterlockedExchange64(&lastTickMs, (LONGLONG)GetTickCount64());
    InterlockedExchange(&lastState, (LONG)HF_API(mod)->game_state(mod));
}

static void CommitQuit(void)
{
    if (!InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange(&crashPassed, 0, 0)) return;
    if (InterlockedCompareExchange64(&quitMs, (LONGLONG)GetTickCount64(), 0) != 0) return;
    SetEvent(quitEvent);
    Record("quit committed: stingray.Application.quit returned; grace 3000 ms");
}

static int Quit(lua_State* state)
{
    int arguments = lua.gettop(state);
    int type = arguments == 0 ? LUA_TNIL : lua.type(state, 1);
    BOOL clean = type == LUA_TNIL || (type == LUA_TNUMBER && lua.tonumber(state, 1) == 0.0);
    lua.pushvalue(state, LUA_ORIGINAL_QUIT);
    lua.insert(state, 1);
    lua.call(state, arguments, LUA_MULTRET); /* a Lua error propagates without arming the watchdog */
    if (clean) CommitQuit();
    else
    {
        InterlockedExchange(&crashPassed, 1);
        SetEvent(stopEvent);
        Record("passed on: Application.quit requested a nonzero or unsupported exit code; watchdog cancelled");
    }
    return lua.gettop(state);
}

/* Called inside lua_cpcall, so even an allocation failure while finding the table is a protected Lua error. */
static int WrapQuit(lua_State* state)
{
    lua.pushstring(state, "stingray");
    lua.rawget(state, LUA_GLOBALSINDEX);
    if (lua.type(state, -1) != LUA_TTABLE) return 0;
    lua.pushstring(state, "Application");
    lua.rawget(state, -2);
    if (lua.type(state, -1) != LUA_TTABLE) return 0;
    int application = lua.gettop(state);
    lua.pushstring(state, "quit");
    lua.rawget(state, application);
    uintptr_t native = (uintptr_t)lua.tocfunction(state, -1);
    if (native < exeBase || native - exeBase >= exeSize) return 0;
    /* Keep the original closure as an upvalue, including any engine upvalues of its own. */
    lua.pushstring(state, "quit");
    lua.pushvalue(state, -2);
    lua.pushcclosure(state, Quit, 1);
    lua.rawset(state, application);
    InterlockedExchange(&wrapped, 1);
    Record("ready: wrapped native stingray.Application.quit");
    HF_API(self)->log(self, "quit binding wrapped; watchdog waits for a successful Application.quit call (3000 ms grace)");
    return 0;
}

static int Pcall(lua_State* state, int arguments, int results, int errorFunction)
{
    int result = originalPcall(state, arguments, results, errorFunction);
    if (result != 0 || !InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange(&wrapped, 0, 0) != 0 ||
        (DWORD)InterlockedCompareExchange(&gameThread, 0, 0) != GetCurrentThreadId()) return result;
    int top = lua.gettop(state);
    int failed = lua.cpcall(state, WrapQuit, NULL);
    lua.settop(state, top);
    if (failed)
    {
        InterlockedExchange(&wrapped, -1);
        Record("off: protected quit-binding discovery failed");
        HF_API(self)->log(self, "quit-binding discovery failed; clean exit stays off");
    }
    return result;
}

static DWORD WINAPI Watchdog(void* user)
{
    (void)user;
    HANDLE events[2] = {stopEvent, quitEvent};
    if (WaitForMultipleObjects(2, events, FALSE, INFINITE) != WAIT_OBJECT_0 + 1) return 0;
    ULONGLONG elapsed = GetTickCount64() - (ULONGLONG)InterlockedCompareExchange64(&quitMs, 0, 0);
    DWORD remaining = elapsed < QUIT_GRACE_MS ? QUIT_GRACE_MS - (DWORD)elapsed : 0;
    if (WaitForSingleObject(stopEvent, remaining) != WAIT_TIMEOUT || InterlockedCompareExchange(&crashPassed, 0, 0)) return 0;
    Record("clean exit: committed quit exceeded 3000 ms; TerminateProcess(0)");
    TerminateProcess(GetCurrentProcess(), 0);
    return 0;
}

static LONG CALLBACK Fault(EXCEPTION_POINTERS* info)
{
    if (!InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange64(&quitMs, 0, 0) == 0)
        return EXCEPTION_CONTINUE_SEARCH;
    const EXCEPTION_RECORD* record = info->ExceptionRecord;
    LONGLONG last = InterlockedCompareExchange64(&lastTickMs, 0, 0);
    ULONGLONG idle = last == 0 ? 0 : GetTickCount64() - (ULONGLONG)last;
    LONG state = InterlockedCompareExchange(&lastState, 0, 0);
    if (record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION &&
        (uintptr_t)record->ExceptionAddress == exeBase + TEARDOWN_FAULT_RVA && last != 0 &&
        (idle >= LOOP_STOPPED_MS || state == HF_STATE_TITLE || state == HF_STATE_SHIP) &&
        !InterlockedCompareExchange(&crashPassed, 0, 0))
    {
        char line[192];
        wsprintfA(line, "clean exit: committed teardown fault at +0x%X; idle %lu ms, state %ld; TerminateProcess(0)",
                  TEARDOWN_FAULT_RVA, (unsigned long)idle, state);
        Record(line);
        TerminateProcess(GetCurrentProcess(), 0);
    }
    else if (record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION || record->ExceptionCode == EXCEPTION_IN_PAGE_ERROR ||
             record->ExceptionCode == EXCEPTION_ILLEGAL_INSTRUCTION || record->ExceptionCode == EXCEPTION_STACK_OVERFLOW ||
             record->ExceptionCode == EXCEPTION_INT_DIVIDE_BY_ZERO || record->ExceptionCode == 0xC0000374u ||
             record->ExceptionCode == 0xC0000409u)
    {
        InterlockedExchange(&crashPassed, 1);
        SetEvent(stopEvent);
        char line[192];
        wsprintfA(line, "passed on: exception 0x%X after quit; watchdog cancelled", record->ExceptionCode);
        Record(line);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

static int OpenLog(void)
{
    wchar_t path[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH - 40);
    if (n == 0 || n >= MAX_PATH - 40) return 1;
    lstrcatW(path, L"\\Clonedivers");
    CreateDirectoryW(path, NULL);
    lstrcatW(path, L"\\clean-exit.log");
    logFile = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
                          OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    return logFile == INVALID_HANDLE_VALUE;
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* hf, HfMod* mod)
{
    if (hf->version < 16 || hf->size < offsetof(HfApi, game_state) + sizeof hf->game_state) return 1;
    if (hf->build_id() == NULL || lstrcmpA(hf->build_id(), "25480438") != 0) return 1;
    self = mod;
    exeBase = hf->module_base("helldivers2.exe", &exeSize);
    if (exeBase == 0 || exeSize == 0 || OpenLog()) return 1;
    HMODULE module = GetModuleHandleW(L"lua51.dll");
#define LUA_API(field, name) do { *(FARPROC*)(void*)&lua.field = GetProcAddress(module, name); if (!lua.field) goto failed; } while (0)
    if (module == NULL) goto failed;
    LUA_API(gettop, "lua_gettop");
    LUA_API(settop, "lua_settop");
    LUA_API(pushstring, "lua_pushstring");
    LUA_API(rawget, "lua_rawget");
    LUA_API(rawset, "lua_rawset");
    LUA_API(type, "lua_type");
    LUA_API(tonumber, "lua_tonumber");
    LUA_API(tocfunction, "lua_tocfunction");
    LUA_API(pushvalue, "lua_pushvalue");
    LUA_API(pushcclosure, "lua_pushcclosure");
    LUA_API(insert, "lua_insert");
    LUA_API(call, "lua_call");
    LUA_API(cpcall, "lua_cpcall");
#undef LUA_API
    uintptr_t pcall = (uintptr_t)GetProcAddress(module, "lua_pcall");
    if (pcall == 0) goto failed;
    quitEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    stopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (quitEvent == NULL || stopEvent == NULL) goto failed;
    worker = CreateThread(NULL, 0, Watchdog, NULL, 0, NULL);
    if (worker == NULL) goto failed;
    faultHandler = AddVectoredExceptionHandler(1, Fault);
    if (faultHandler == NULL || hf_on_game_thread(mod, Tick, NULL) != 0) goto failed;
    if (hf->hook(mod, pcall, Pcall, (void**)&originalPcall) != 0) goto failed;
    InterlockedExchange(&enabled, 1);
    Record("loaded v0.2.0: waiting for native quit binding on the game thread; watchdog unarmed");
    hf->log(mod, "clean exit 0.2.0 loaded; waiting for native stingray.Application.quit; no idle-only recovery");
    return 0;

failed:
    Record("off: initialization failed");
    if (faultHandler != NULL) RemoveVectoredExceptionHandler(faultHandler);
    if (worker != NULL) { SetEvent(stopEvent); WaitForSingleObject(worker, INFINITE); CloseHandle(worker); }
    if (stopEvent != NULL) CloseHandle(stopEvent);
    if (quitEvent != NULL) CloseHandle(quitEvent);
    CloseHandle(logFile);
    logFile = INVALID_HANDLE_VALUE;
    hf->log(mod, "clean exit initialization failed; no recovery armed");
    return 1;
}
