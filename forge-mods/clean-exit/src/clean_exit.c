/* Clonedivers Clean Exit (clonedivers.clean-exit): a HellForge code mod for game build 25480438.
 *
 * HELLDIVERS 2 build 25480438 crashes as it quits after any mission: an access violation at helldivers2.exe+0x626296
 * while the render task pool is torn down (HellForge docs/modding/debugging.md: "the game's own"). Players see the
 * game hang, a crash report, and a session that ends as a crash instead of a quit.
 *
 * This mod ends the process with exit code 0 at that one fault, and only while the game is quitting: the game loop has
 * stopped updating, or its last state was the title screen or the ship. Every other fault, and the same address while
 * the game still runs (once seen while a mission loaded, HellForge #228), passes on to HellForge's crash handling
 * untouched. Each use is recorded in %LOCALAPPDATA%\Clonedivers\clean-exit.log (the session's own LOCALAPPDATA).
 * It changes no game data and calls nothing of the game's. */
#include "hellforge_mod.h"
#include <windows.h>

#define TEARDOWN_FAULT_RVA 0x626296u
#define LOOP_STOPPED_MS 150u

static volatile LONGLONG lastTickMs;   /* GetTickCount64 at the last game update */
static volatile LONG lastState;        /* the game's state at the last update (HF_STATE_...) */
static uintptr_t exeBase;
static HfMod* self;

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    lastTickMs = (LONGLONG)GetTickCount64();
    lastState = (LONG)HF_API(mod)->game_state(mod);
}

/* Appends one line without the CRT: called inside a fault, so no allocation and no locks beyond the file's. */
static void Record(const char* text, DWORD length)
{
    wchar_t path[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH - 40);
    if (n == 0 || n >= MAX_PATH - 40) return;
    lstrcatW(path, L"\\Clonedivers");
    CreateDirectoryW(path, NULL);
    lstrcatW(path, L"\\clean-exit.log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(file, text, length, &written, NULL);
    CloseHandle(file);
}

static LONG CALLBACK Fault(EXCEPTION_POINTERS* info)
{
    const EXCEPTION_RECORD* record = info->ExceptionRecord;
    if (record->ExceptionCode != EXCEPTION_ACCESS_VIOLATION) return EXCEPTION_CONTINUE_SEARCH;
    if ((uintptr_t)record->ExceptionAddress != exeBase + TEARDOWN_FAULT_RVA) return EXCEPTION_CONTINUE_SEARCH;
    LONGLONG last = lastTickMs;
    ULONGLONG idle = last == 0 ? 0 : GetTickCount64() - (ULONGLONG)last;
    LONG state = lastState;
    BOOL quitting = last != 0 && (idle >= LOOP_STOPPED_MS || state == HF_STATE_TITLE || state == HF_STATE_SHIP);
    char line[160];
    int length = wsprintfA(line, "%s teardown fault at +0x%X: game loop idle %lu ms, last state %ld\r\n",
                           quitting ? "clean exit:" : "passed on:", TEARDOWN_FAULT_RVA, (unsigned long)idle, state);
    if (length > 0) Record(line, (DWORD)length);
    if (!quitting) return EXCEPTION_CONTINUE_SEARCH;
    TerminateProcess(GetCurrentProcess(), 0);
    return EXCEPTION_CONTINUE_SEARCH;
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* hf, HfMod* mod)
{
    if (hf->version < 6) return 1; /* on_game_thread needs SDK 6 */
    self = mod;
    exeBase = (uintptr_t)GetModuleHandleW(NULL);
    if (hf_on_game_thread(mod, Tick, NULL) != 0) return 1;
    /* First in line, so the fault is seen before HellForge's crash handler records it. */
    if (AddVectoredExceptionHandler(1, Fault) == NULL) return 1;
    hf->log(mod, "clean exit armed for the quit fault at helldivers2.exe+0x%X", TEARDOWN_FAULT_RVA);
    return 0;
}
