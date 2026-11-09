/* Standalone host: fake game memory and audio, real Win32 hashing/archive parsing. Never opens a game process. */
#undef NDEBUG
#include <assert.h>
#include "../src/empire_lego_yoda.c"
struct HfMod { const HfApi* api; };
static HfApi api;
static struct HfMod mod;
static unsigned char players[0x500], user[0xB400], health[0x1100], manager[0xC00], playerDesc[24], healthDesc[24], kit[64], healthRecord[0x1B8];
static uintptr_t playerCell, userCell, healthCell, kitPointer, healthPointer;
static uint32_t mapEntries[8];
static int calls, playback = 1, checks;
static uint32_t state = HF_STATE_MISSION;
static int badPin;
#define CHECK(x) do { assert(x); checks++; } while (0)
static void Put32(void* p, size_t at, uint32_t value) { memcpy((char*)p + at, &value, 4); }
static void Put64(void* p, size_t at, uint64_t value) { memcpy((char*)p + at, &value, 8); }
static int TestRead(uintptr_t p, void* out, size_t n) { if (p < 65536) return 1; memcpy(out, (void*)p, n); return 0; }
static uintptr_t Global(HfMod* m, const char* name)
{
    (void)m;
    if (!strcmp(name, "PlayerManager")) return (uintptr_t)&playerCell;
    if (!strcmp(name, "LocalUser")) return (uintptr_t)&userCell;
    if (!strcmp(name, "HealthManager")) return (uintptr_t)&healthCell;
    return 0;
}
static uint32_t GameState(HfMod* m) { (void)m; return state; }
static uint32_t Entity(HfMod* m, uint32_t network) { (void)m; return network + 10; }
static void Log(HfMod* m, const char* format, ...) { (void)m; (void)format; }
static void Status(HfMod* m, const char* text) { (void)m; (void)text; }
static uintptr_t Module(const char* name, size_t* size) { (void)name; (void)size; return game; }
static int Code(uintptr_t p, void* out, size_t n)
{
    for (size_t i = 0; i < sizeof kPassivePins / sizeof kPassivePins[0]; i++)
        if (p == game + kPassivePins[i].rva && n == kPassivePins[i].size)
        { memcpy(out, kPassivePins[i].bytes, n); if (badPin && !i) ((unsigned char*)out)[0] ^= 1; return 0; }
    return 1;
}
static BOOL WINAPI Play(LPCWSTR wav, HMODULE module, DWORD flags)
{
    (void)module;
    CHECK(wav == (LPCWSTR)audio && (flags & SND_MEMORY) && (flags & SND_ASYNC)); calls++; return playback;
}
static void Observe(void) { Tick((HfMod*)&mod, 0.016f, NULL); }
static void Die(void)
{
    Put32(players, 0x2E0, 2); Put32(healthRecord, 0x19C, 2);
    HfPlayerEvent event = {{sizeof event, HF_ON_PLAYER_DOWN, 1}, 42, 100, 1};
    Down((HfMod*)&mod, &event, NULL);
}
static void Reset(void)
{
    memset(&saved, 0, sizeof saved); consumedPeer = consumedAvatar = consumedNetwork = 0;
    memset(players, 0, sizeof players); memset(user, 0, sizeof user); memset(manager, 0, sizeof manager);
    memset(health, 0, sizeof health); memset(kit, 0, sizeof kit); memset(healthRecord, 0, sizeof healthRecord);
    playerCell = (uintptr_t)players; userCell = (uintptr_t)user; healthCell = (uintptr_t)health;
    Put32(players, 0x84, 1); Put64(players, 0x2C8, 42); Put64(user, 0xB398, 42);
    Put64(players, 0xE8, (uintptr_t)playerDesc); Put32(playerDesc, 8, 100);
    Put32(players, 0x2E0, 3); Put32(players, 0x3A8, 200);
    Put64((void*)game, 0x33264F8, (uintptr_t)manager);
    Put32(manager, 0x8E8, 1); Put64(manager, 0x930, (uintptr_t)mapEntries);
    Put32(manager, 0x938, 4); Put32(manager, 0x93C, UINT32_MAX); Put32(manager, 0x940, 1);
    for (int i = 0; i < 8; i++) mapEntries[i] = UINT32_MAX;
    mapEntries[0] = 100; mapEntries[1] = 0;
    Put64(manager, 0x948, (uintptr_t)playerDesc); Put32(manager, 0x96C + 12, visuals[0].kit);
    kitPointer = (uintptr_t)kit; Put64(manager, 0, (uintptr_t)&kitPointer); Put32(manager, 8, 1); Put32(kit, 0, visuals[0].kit);
    Put32(health, 0x1020, 1); healthPointer = (uintptr_t)healthDesc;
    Put64(health, 0x1048, (uintptr_t)&healthPointer); Put64(health, 0x1058, (uintptr_t)healthRecord); Put32(healthDesc, 8, 210);
    ready = proven = gameLoaded = 1; calls = 0; playback = 1; state = HF_STATE_MISSION; play = Play;
}
static void WritePart(const wchar_t* target, const Proof* original, int gpu, int corrupt)
{
    wchar_t input[1100]; swprintf(input, 1100, L"%ls%ls", original->path, gpu ? L".gpu_resources" : L"");
    HANDLE in = OpenRead(input); CHECK(in != INVALID_HANDLE_VALUE);
    uint32_t n = original->row.d[gpu ? 2 : 0]; void* bytes = malloc(n); CHECK(bytes != NULL);
    CHECK(ReadAt(in, original->row.q[gpu ? 4 : 2], bytes, n)); CloseHandle(in);
    if (corrupt) ((unsigned char*)bytes)[0] ^= 1;
    HANDLE out = CreateFileW(target, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL); CHECK(out != INVALID_HANDLE_VALUE);
    DWORD wrote;
    if (!gpu)
    {
        unsigned char head[72] = {0}; Put32(head, 0, 0xF0000011u); Put32(head, 8, 1);
        ArchiveRow row = original->row; row.q[2] = 152; row.q[3] = row.q[4] = 0;
        CHECK(WriteFile(out, head, 72, &wrote, NULL) && wrote == 72);
        CHECK(WriteFile(out, &row, 80, &wrote, NULL) && wrote == 80);
    }
    CHECK(WriteFile(out, bytes, n, &wrote, NULL) && wrote == n); CloseHandle(out); free(bytes);
}
int wmain(int argc, wchar_t** argv)
{
    CHECK(argc == 3);
    HMODULE bcrypt = LoadLibraryW(L"bcrypt.dll"); CHECK(bcrypt != NULL);
    openHash = (void*)GetProcAddress(bcrypt, "BCryptOpenAlgorithmProvider"); hashBytes = (void*)GetProcAddress(bcrypt, "BCryptHash");
    CHECK(openHash && hashBytes && openHash(&sha, BCRYPT_SHA256_ALGORITHM, NULL, 0) >= 0);
    CHECK(Hash("abc", 3, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
    CHECK(ScanVisuals(argv[1]) == 4);
    Proof candidate[4]; memcpy(candidate, proofs, sizeof candidate);
    Proof source = proofs[0]; CHECK(Unchanged(&source));
    CHECK(CreateDirectoryW(argv[2], NULL));
    CHECK(ScanVisuals(argv[2]) == 0);
    wchar_t main[1100], gpu[1100], later[1100], other[1100];
    swprintf(main, 1100, L"%ls\\9ba626afa44a3aa3.patch_1", argv[2]);
    swprintf(gpu, 1100, L"%ls.gpu_resources", main);
    WritePart(main, &source, 0, 0); WritePart(gpu, &source, 1, 0);
    CHECK(ScanVisuals(argv[2]) == 1); CHECK(proofs[0].valid && Unchanged(&proofs[0]));
    Proof good = proofs[0];
    swprintf(later, 1100, L"%ls\\9ba626afa44a3aa3.patch_2", argv[2]);
    WritePart(later, &source, 0, 1);
    wchar_t laterGpu[1100]; swprintf(laterGpu, 1100, L"%ls.gpu_resources", later); WritePart(laterGpu, &source, 1, 0);
    CHECK(ScanVisuals(argv[2]) == 0); CHECK(DeleteFileW(later) && DeleteFileW(laterGpu));
    swprintf(other, 1100, L"%ls\\1111111111111111.patch_3", argv[2]); WritePart(other, &source, 0, 0);
    CHECK(ScanVisuals(argv[2]) == 0); CHECK(DeleteFileW(other));
    CHECK(ScanVisuals(argv[2]) == 1);
    api.size = sizeof api; api.version = 16; api.read = TestRead; api.global = Global; api.game_state = GameState; api.entity_of_network = Entity; api.log = Log;
    hf = &api; mod.api = &api;
    game = (uintptr_t)calloc(1, 0x3326500); CHECK(game != 0);
    api.module_base = Module; api.read_code = Code; api.set_status = Status;
    proven = 0; CHECK(Prove((HfMod*)&mod) && proven == 1);
    proven = 0; badPin = 1; CHECK(!Prove((HfMod*)&mod) && proven == -1);
    badPin = 0; CHECK(!Prove((HfMod*)&mod));
    sampleEvery = 0;
    for (int v = 0; v < 4; v++)
    {
        memcpy(proofs, candidate, sizeof proofs);
        Reset(); Put32(manager, 0x96C, visuals[v].body); Put32(manager, 0x96C + 12, visuals[v].kit); Put32(kit, 0, visuals[v].kit);
        Observe(); CHECK(saved.visual == v && saved.avatar == 210); Die(); CHECK(calls == 1); Die(); CHECK(calls == 1);
    }
    memset(proofs, 0, sizeof proofs); proofs[0] = good;
    Reset(); gameLoaded = 0; Observe(); CHECK(!saved.avatar); Loaded((HfMod*)&mod, HF_EVENT_GAME_LOADED, NULL); Observe(); CHECK(saved.avatar == 210);
    Reset(); Observe(); CHECK(saved.avatar == 210 && saved.kit == visuals[0].kit); Die(); CHECK(calls == 1); Die(); CHECK(calls == 1);
    Reset(); Observe(); HfPlayerEvent remote = {{sizeof remote, HF_ON_PLAYER_DOWN, 1}, 42, 100, 0}; Down((HfMod*)&mod, &remote, NULL); CHECK(calls == 0);
    Reset(); Put32(manager, 0x96C + 12, 123); Observe(); Die(); CHECK(calls == 0);
    Reset(); Put32(manager, 0x96C + 4, visuals[0].kit); Put32(manager, 0x96C + 12, 123); Observe(); Die(); CHECK(calls == 0);
    Reset(); Put32(manager, 0x96C, 2); Observe(); Die(); CHECK(calls == 0);
    Reset(); Put32(kit, 40, 1); Observe(); Die(); CHECK(calls == 0);
    Reset(); Put32(manager, 0x938, 65); Observe(); CHECK(!saved.avatar);
    Reset(); Put32(playerDesc, 8, 999); Observe(); CHECK(!saved.avatar);
    Reset(); Put32(players, 0x84, 2); Put64(players, 0x2C8 + 0x38, 42); Observe(); CHECK(!saved.avatar);
    Reset(); Observe(); saved.observed = GetTickCount64() - 501; Die(); CHECK(calls == 0);
    Reset(); Observe(); Put32(manager, 0x96C + 4, 123); Die(); CHECK(calls == 0);
    Reset(); Observe(); Put32(players, 0x2E0, 2); Put32(healthRecord, 0x19C, 1);
    HfPlayerEvent event = {{sizeof event, HF_ON_PLAYER_DOWN, 1}, 42, 100, 1}; Down((HfMod*)&mod, &event, NULL); CHECK(calls == 0);
    Reset(); Observe(); Put32(health, 0x1020, 0); Die(); CHECK(calls == 0);
    Reset(); Observe(); event.peer = 999; Down((HfMod*)&mod, &event, NULL); CHECK(calls == 0);
    Reset(); Observe(); playback = 0; Die(); CHECK(calls == 1); playback = 1; Die(); CHECK(calls == 1);
    Reset(); Observe(); Die(); Put32(players, 0x2E0, 3); Put32(players, 0x3A8, 201); Put32(healthDesc, 8, 211); Put32(healthRecord, 0x19C, 0); Observe(); Die(); CHECK(calls == 2);
    Reset(); Observe(); HfStateEvent changed = {{sizeof changed, HF_ON_STATE, 1}, 4, 3}; State((HfMod*)&mod, &changed, NULL); CHECK(!saved.avatar); Die(); CHECK(calls == 0);
    Reset(); state = HF_STATE_SHIP; Observe(); CHECK(!saved.avatar); Die(); CHECK(calls == 0);
    Reset(); proofs[0].valid = 0; Observe(); Die(); CHECK(calls == 0); proofs[0] = good;
    Reset(); Observe(); HANDLE edit = CreateFileW(gpu, FILE_APPEND_DATA, 0, NULL, OPEN_EXISTING, 0, NULL); CHECK(edit != INVALID_HANDLE_VALUE);
    DWORD wrote; CHECK(WriteFile(edit, "x", 1, &wrote, NULL)); CloseHandle(edit); CHECK(!Unchanged(&proofs[0])); Die(); CHECK(calls == 0);
    free((void*)game);
    printf("PASS: %d checks; real r45 4/4 torso hashes; synthetic deaths/overrides; audio stub only\n", checks);
    return 0;
}
