/* Clonedivers Clean Exit, game build 25480438, SDK 16.
 *
 * Handles all clean shutdown paths in HELLDIVERS 2 build 25480438:
 *   1. Lua stingray.Application.quit (wrapped on the game thread after a successful engine lua_pcall).
 *   2. Native C++ in-game Esc menu "Quit Game", which destroys the HELLDIVERS 2 top-level window (!IsWindow) and
 *      stops the game loop without invoking Lua's stingray.Application.quit table entry.
 *   3. Top-level WM_CLOSE (Alt+F4 / window X) when Stingray stops the game loop and enters +0x62697f teardown before
 *      DestroyWindow; automatically disarmed if gameplay ticks continue without window destruction.
 *
 * Minimized, hidden (ShowWindow SW_HIDE during display-mode transitions), and Windows "Not Responding" ghosted windows
 * keep a valid HWND (IsWindow == TRUE), so they never count as destroyed. Pending in-play faults are cleared only when
 * a normal game-thread Tick() runs, preventing real crashes from being masked as clean exits.
 */
#include "hellforge_mod.h"
#include <windows.h>

#define TEARDOWN_FAULT_RVA 0x626296u
#define LOOP_STOPPED_MS 150u
#define QUIT_GRACE_MS 3000u
#define WINDOW_QUIT_GRACE_MS 1500u
#define WATCHDOG_POLL_MS 100u
#define WM_CLOSE_RESUME_TICKS 3
#define MAX_SHOWN_WINDOWS 16
#define MAX_HOOKED_THREADS 8
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

static volatile LONGLONG lastTickMs, quitMs, wmCloseMs, wmCloseTickSnap, tickCount;
static volatile LONG lastState, gameThread, gameThreadHooked, enabled, wrapped, crashPassed, shownCount, windowClosed, ownedPopupsOpen, inplayFaultPending, hookCount;
static HWND volatile shownWindows[MAX_SHOWN_WINDOWS];
static DWORD hookedThreads[MAX_HOOKED_THREADS];
static HHOOK callWndHooks[MAX_HOOKED_THREADS];
static HHOOK getMsgHooks[MAX_HOOKED_THREADS];
static SRWLOCK hookLock = SRWLOCK_INIT;
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

static BOOL WasShownWindow(HWND window)
{
    LONG count = InterlockedCompareExchange(&shownCount, 0, 0);
    for (LONG i = 0; i < count; i++)
    {
        HWND known = (HWND)InterlockedCompareExchangePointer((PVOID volatile*)&shownWindows[i], NULL, NULL);
        if (known == window) return TRUE;
    }
    return FALSE;
}

static void RememberShownWindow(HWND window)
{
    if (window == NULL || WasShownWindow(window)) return;
    LONG count = InterlockedCompareExchange(&shownCount, 0, 0);
    if (count < 0 || count >= MAX_SHOWN_WINDOWS) return;
    InterlockedExchangePointer((PVOID volatile*)&shownWindows[count], window);
    InterlockedExchange(&shownCount, count + 1);
}

/* Lock-free check safe inside the vectored exception handler: true only when at least one top-level unowned game
 * window was previously shown and every shown HWND has been destroyed by DestroyWindow (!IsWindow). Minimized,
 * hidden (SW_HIDE), and Windows "Not Responding" ghosted windows keep IsWindow == TRUE and never trip this check. */
static BOOL AllShownWindowsDestroyed(void)
{
    LONG count = InterlockedCompareExchange(&shownCount, 0, 0);
    if (count <= 0) return FALSE;
    for (LONG i = 0; i < count; i++)
    {
        HWND known = (HWND)InterlockedCompareExchangePointer((PVOID volatile*)&shownWindows[i], NULL, NULL);
        if (known != NULL && IsWindow(known)) return FALSE;
    }
    return TRUE;
}

typedef struct WindowPoll
{
    DWORD pid;
    int ownedPopupCount;
} WindowPoll;

static BOOL HasNonEmptyRectAndActivatableStyle(HWND hwnd)
{
    LONG_PTR ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return FALSE;
    if (!IsIconic(hwnd))
    {
        RECT rc = {0};
        if (!GetWindowRect(hwnd, &rc) || rc.right <= rc.left || rc.bottom <= rc.top) return FALSE;
    }
    return TRUE;
}

static BOOL IsTopLevelGameWindow(HWND hwnd)
{
    return hwnd != NULL && IsWindowVisible(hwnd) &&
           GetAncestor(hwnd, GA_ROOT) == hwnd && GetWindow(hwnd, GW_OWNER) == NULL &&
           HasNonEmptyRectAndActivatableStyle(hwnd);
}

static BOOL IsVisibleOwnedDialogPopup(HWND hwnd)
{
    return hwnd != NULL && IsWindowVisible(hwnd) &&
           GetAncestor(hwnd, GA_ROOT) == hwnd && GetWindow(hwnd, GW_OWNER) != NULL &&
           HasNonEmptyRectAndActivatableStyle(hwnd);
}

static void CancelWatchdogForPriorFault(const char* message)
{
    if (InterlockedExchange(&crashPassed, 1) == 0)
    {
        InterlockedExchange64(&wmCloseMs, 0);
        SetEvent(stopEvent);
        Record(message);
    }
}

static void NoteWmClose(const char* reason)
{
    if (!InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange(&crashPassed, 0, 0)) return;
    if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
    {
        CancelWatchdogForPriorFault("passed on: unhandled fault preceded WM_CLOSE; watchdog cancelled");
        return;
    }
    if (InterlockedCompareExchange64(&wmCloseMs, 0, 0) != 0) return;
    LONGLONG snap = InterlockedCompareExchange64(&tickCount, 0, 0);
    ULONGLONG now = GetTickCount64();
    if (now == 0) now = 1;
    InterlockedExchange64(&wmCloseTickSnap, snap);
    if (InterlockedCompareExchange64(&wmCloseMs, (LONGLONG)now, 0) == 0)
        Record(reason);
}

static LRESULT CALLBACK InspectCallWndProc(int code, WPARAM wParam, LPARAM lParam)
{
    (void)wParam;
    if (code == HC_ACTION && lParam != 0)
    {
        const CWPSTRUCT* msg = (const CWPSTRUCT*)lParam;
        if (msg->message == WM_CLOSE && IsTopLevelGameWindow(msg->hwnd))
            NoteWmClose("close requested: sent WM_CLOSE on top-level game window; waiting for loop stop (grace 3000 ms)");
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

static LRESULT CALLBACK InspectGetMsgProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION && wParam == PM_REMOVE && lParam != 0)
    {
        const MSG* msg = (const MSG*)lParam;
        if (msg->message == WM_CLOSE && IsTopLevelGameWindow(msg->hwnd))
            NoteWmClose("close requested: posted WM_CLOSE retrieved for top-level game window; waiting for loop stop (grace 3000 ms)");
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

static void EnsureThreadCloseHook(DWORD tid)
{
    if (tid == 0) return;
    AcquireSRWLockExclusive(&hookLock);
    LONG n = hookCount;
    for (LONG i = 0; i < n; i++)
    {
        if (hookedThreads[i] == tid)
        {
            ReleaseSRWLockExclusive(&hookLock);
            return;
        }
    }
    if (n < MAX_HOOKED_THREADS)
    {
        HHOOK hc = SetWindowsHookExW(WH_CALLWNDPROC, InspectCallWndProc, NULL, tid);
        HHOOK hg = SetWindowsHookExW(WH_GETMESSAGE, InspectGetMsgProc, NULL, tid);
        if (hc != NULL || hg != NULL)
        {
            hookedThreads[n] = tid;
            callWndHooks[n] = hc;
            getMsgHooks[n] = hg;
            hookCount = n + 1;
        }
    }
    ReleaseSRWLockExclusive(&hookLock);
}

static BOOL CALLBACK InspectWindowCallback(HWND window, LPARAM param)
{
    WindowPoll* poll = (WindowPoll*)param;
    DWORD pid = 0;
    DWORD wndTid = GetWindowThreadProcessId(window, &pid);
    if (wndTid == 0 || pid != poll->pid) return TRUE;
    if (IsTopLevelGameWindow(window))
    {
        RememberShownWindow(window);
        EnsureThreadCloseHook(wndTid);
    }
    else if (IsVisibleOwnedDialogPopup(window))
    {
        poll->ownedPopupCount++;
    }
    return TRUE;
}

static BOOL PollAllShownWindowsDestroyed(int* ownedPopupsOut)
{
    WindowPoll poll;
    poll.pid = GetCurrentProcessId();
    poll.ownedPopupCount = 0;
    EnumWindows(InspectWindowCallback, (LPARAM)&poll);
    InterlockedExchange(&ownedPopupsOpen, (LONG)poll.ownedPopupCount);
    if (ownedPopupsOut != NULL) *ownedPopupsOut = poll.ownedPopupCount;
    BOOL allDestroyed = AllShownWindowsDestroyed();
    InterlockedExchange(&windowClosed, (allDestroyed && poll.ownedPopupCount == 0) ? 1 : 0);
    return allDestroyed;
}

static BOOL IsSevereFault(DWORD code)
{
    return code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_IN_PAGE_ERROR ||
           code == EXCEPTION_ILLEGAL_INSTRUCTION || code == EXCEPTION_STACK_OVERFLOW ||
           code == EXCEPTION_INT_DIVIDE_BY_ZERO || code == 0xC0000374u || code == 0xC0000409u;
}

static void CommitQuit(void)
{
    if (!InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange(&crashPassed, 0, 0)) return;
    if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
    {
        CancelWatchdogForPriorFault("passed on: unhandled fault preceded Application.quit; watchdog cancelled");
        return;
    }
    ULONGLONG now = GetTickCount64();
    if (now == 0) now = 1;
    if (InterlockedCompareExchange64(&quitMs, (LONGLONG)now, 0) != 0) return;
    SetEvent(quitEvent);
    Record("quit committed: stingray.Application.quit returned; grace 3000 ms");
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    DWORD tid = GetCurrentThreadId();
    InterlockedCompareExchange(&gameThread, (LONG)tid, 0);
    if (InterlockedCompareExchange(&enabled, 0, 0) &&
        (DWORD)InterlockedCompareExchange(&gameThread, 0, 0) == tid &&
        InterlockedCompareExchange(&gameThreadHooked, 1, 0) == 0)
    {
        EnsureThreadCloseHook(tid);
    }
    LONGLONG curTicks = InterlockedIncrement64(&tickCount);
    InterlockedExchange64(&lastTickMs, (LONGLONG)GetTickCount64());
    InterlockedExchange(&lastState, (LONG)HF_API(mod)->game_state(mod));
    InterlockedExchange(&inplayFaultPending, 0);

    if (InterlockedCompareExchange64(&wmCloseMs, 0, 0) != 0)
    {
        LONGLONG snap = InterlockedCompareExchange64(&wmCloseTickSnap, 0, 0);
        if (curTicks - snap >= WM_CLOSE_RESUME_TICKS && !AllShownWindowsDestroyed())
        {
            if (InterlockedExchange64(&wmCloseMs, 0) != 0)
                Record("disarmed: gameplay ticks continued after WM_CLOSE; close request not accepted");
        }
    }
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
    HF_API(self)->log(self, "quit binding wrapped; watchdog monitors Application.quit (3000 ms), WM_CLOSE teardown (3000 ms), and window destruction (1500 ms)");
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
        HF_API(self)->log(self, "quit-binding discovery failed; window-destruction teardown monitor stays active");
    }
    return result;
}

static DWORD WINAPI Watchdog(void* user)
{
    (void)user;
    HANDLE events[2] = {stopEvent, quitEvent};
    ULONGLONG windowGoneSince = 0;

    for (;;)
    {
        DWORD wait = WaitForMultipleObjects(2, events, FALSE, WATCHDOG_POLL_MS);
        if (wait == WAIT_OBJECT_0) return 0;
        if (wait == WAIT_OBJECT_0 + 1)
        {
            ULONGLONG elapsed = GetTickCount64() - (ULONGLONG)InterlockedCompareExchange64(&quitMs, 0, 0);
            DWORD remaining = elapsed < QUIT_GRACE_MS ? QUIT_GRACE_MS - (DWORD)elapsed : 0;
            if (WaitForSingleObject(stopEvent, remaining) != WAIT_TIMEOUT || InterlockedCompareExchange(&crashPassed, 0, 0)) return 0;
            if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
            {
                CancelWatchdogForPriorFault("passed on: unhandled fault preceded committed quit; watchdog cancelled");
                return 0;
            }
            Record("clean exit: committed quit exceeded 3000 ms; TerminateProcess(0)");
            TerminateProcess(GetCurrentProcess(), 0);
            return 0;
        }
        if (wait != WAIT_TIMEOUT) return 0;
        if (!InterlockedCompareExchange(&enabled, 0, 0) || InterlockedCompareExchange(&crashPassed, 0, 0)) continue;

        int ownedPopups = 0;
        BOOL allDestroyed = PollAllShownWindowsDestroyed(&ownedPopups);
        LONGLONG last = InterlockedCompareExchange64(&lastTickMs, 0, 0);
        ULONGLONG now = GetTickCount64();
        BOOL loopStopped = last != 0 && (now - (ULONGLONG)last) >= LOOP_STOPPED_MS;

        LONGLONG closeStart = InterlockedCompareExchange64(&wmCloseMs, 0, 0);
        if (closeStart != 0)
        {
            if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
            {
                CancelWatchdogForPriorFault("passed on: unhandled fault preceded WM_CLOSE teardown; watchdog cancelled");
                return 0;
            }
            LONGLONG curTicks = InterlockedCompareExchange64(&tickCount, 0, 0);
            LONGLONG snap = InterlockedCompareExchange64(&wmCloseTickSnap, 0, 0);
            if (curTicks - snap >= WM_CLOSE_RESUME_TICKS && !allDestroyed)
            {
                if (InterlockedExchange64(&wmCloseMs, 0) != 0)
                    Record("disarmed: gameplay ticks continued after WM_CLOSE; close request not accepted");
            }
            else if (ownedPopups > 0)
            {
                /* An owned modal/dialog popup is open; postpone the WM_CLOSE timer until all owned popups close. */
                ULONGLONG resetNow = now == 0 ? 1 : now;
                InterlockedExchange64(&wmCloseMs, (LONGLONG)resetNow);
            }
            else if (ownedPopups == 0 && (loopStopped || allDestroyed) && now >= (ULONGLONG)closeStart &&
                     (now - (ULONGLONG)closeStart) >= QUIT_GRACE_MS && !InterlockedCompareExchange(&crashPassed, 0, 0))
            {
                Record("clean exit: WM_CLOSE teardown exceeded 3000 ms with game loop stopped; TerminateProcess(0)");
                TerminateProcess(GetCurrentProcess(), 0);
                return 0;
            }
        }

        if (allDestroyed && loopStopped && ownedPopups == 0)
        {
            if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
            {
                CancelWatchdogForPriorFault("passed on: unhandled fault preceded window destruction; watchdog cancelled");
                return 0;
            }
            if (windowGoneSince == 0)
            {
                windowGoneSince = now;
                Record("window destroyed and game loop stopped: teardown grace 1500 ms");
            }
            else if (now - windowGoneSince >= WINDOW_QUIT_GRACE_MS && !InterlockedCompareExchange(&crashPassed, 0, 0))
            {
                Record("clean exit: game window destroyed and loop stopped > 1500 ms; TerminateProcess(0)");
                TerminateProcess(GetCurrentProcess(), 0);
                return 0;
            }
        }
        else
        {
            windowGoneSince = 0;
        }
    }
}

static LONG CALLBACK Fault(EXCEPTION_POINTERS* info)
{
    if (!InterlockedCompareExchange(&enabled, 0, 0)) return EXCEPTION_CONTINUE_SEARCH;
    const EXCEPTION_RECORD* record = info->ExceptionRecord;
    BOOL committedQuit = InterlockedCompareExchange64(&quitMs, 0, 0) != 0;
    BOOL wmCloseActive = InterlockedCompareExchange64(&wmCloseMs, 0, 0) != 0;
    BOOL hasOwnedPopups = InterlockedCompareExchange(&ownedPopupsOpen, 0, 0) > 0;
    BOOL destroyedWindow = !hasOwnedPopups &&
                           (InterlockedCompareExchange(&windowClosed, 0, 0) != 0 || AllShownWindowsDestroyed());

    if (!committedQuit && (!wmCloseActive || hasOwnedPopups) && !destroyedWindow)
    {
        /* Latch an in-play severe fault without disarming caught SEH probes (the next game Tick clears it);
         * if a real crash destroys or closes the window before Tick ever runs again, Watchdog & Fault cancel cleanly. */
        if (IsSevereFault(record->ExceptionCode)) InterlockedExchange(&inplayFaultPending, 1);
        return EXCEPTION_CONTINUE_SEARCH;
    }

    if (InterlockedCompareExchange(&inplayFaultPending, 0, 0))
    {
        CancelWatchdogForPriorFault("passed on: unhandled fault preceded quit/window close; watchdog cancelled");
        return EXCEPTION_CONTINUE_SEARCH;
    }

    LONGLONG last = InterlockedCompareExchange64(&lastTickMs, 0, 0);
    ULONGLONG idle = last == 0 ? 0 : GetTickCount64() - (ULONGLONG)last;
    LONG state = InterlockedCompareExchange(&lastState, 0, 0);
    BOOL validTeardownState = idle >= LOOP_STOPPED_MS || destroyedWindow ||
                              (committedQuit && (state == HF_STATE_TITLE || state == HF_STATE_SHIP));

    if (!committedQuit && !validTeardownState)
    {
        /* WM_CLOSE was observed < 150 ms ago while ticks are still running and the window is still alive;
         * treat any severe fault here as an in-play crash rather than a teardown fault. */
        if (IsSevereFault(record->ExceptionCode))
        {
            InterlockedExchange(&inplayFaultPending, 1);
            CancelWatchdogForPriorFault("passed on: severe fault occurred while gameplay ticks were still active; watchdog cancelled");
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }

    if (record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION &&
        (uintptr_t)record->ExceptionAddress == exeBase + TEARDOWN_FAULT_RVA && last != 0 &&
        validTeardownState && !InterlockedCompareExchange(&crashPassed, 0, 0))
    {
        const char* kind = committedQuit ? "committed" : (wmCloseActive ? "wm-close" : "window-destroyed");
        char line[224];
        wsprintfA(line, "clean exit: %s teardown fault at +0x%X; idle %lu ms, state %ld; TerminateProcess(0)",
                  kind, TEARDOWN_FAULT_RVA, (unsigned long)idle, state);
        Record(line);
        TerminateProcess(GetCurrentProcess(), 0);
    }
    else if (IsSevereFault(record->ExceptionCode))
    {
        InterlockedExchange(&crashPassed, 1);
        InterlockedExchange64(&wmCloseMs, 0);
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
    Record("loaded v0.2.1: monitoring native quit binding, WM_CLOSE teardown, and window destruction");
    hf->log(mod, "clean exit 0.2.1 loaded; monitoring native stingray.Application.quit, WM_CLOSE teardown, and window destruction");
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
