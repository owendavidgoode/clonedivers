/* Standalone host: fake game memory and audio, real Win32 hashing/archive parsing. Never opens a game process. */
#undef NDEBUG
#include <assert.h>
#include <windows.h>
static ULONGLONG clockMs;
#define GetTickCount64() clockMs
#include "../src/empire_lego_yoda.c"
struct HfMod { const HfApi* api; };
static HfApi api;
static struct HfMod mod;
static unsigned char players[0x500], user[0xB400], health[0x1100], manager[0xC00], playerDesc[24], healthDesc[24], kit[64], healthRecord[0x1B8];
static uintptr_t playerCell, userCell, healthCell, emCell, invCell, kitPointer, healthPointer;
static uint32_t mapEntries[8];
static int calls, saluteCalls, playback = 1, checks;
static uint32_t state = HF_STATE_MISSION;
static int badPin, missingMapping;
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
    if (!strcmp(name, "EntityManager")) return emCell ? (uintptr_t)&emCell : 0;
    if (!strcmp(name, "InventoryManager")) return invCell ? (uintptr_t)&invCell : 0;
    return 0;
}
static uint32_t GameState(HfMod* m) { (void)m; return state; }
static uint32_t Entity(HfMod* m, uint32_t network)
{
    (void)m;
    if (missingMapping) return UINT32_MAX;
    if (network == 580) return 710;
    if (network == 606) return 738;
    if (network == 0x7FFF) return UINT32_MAX;
    return network + 10;
}
static void Log(HfMod* m, const char* format, ...) { (void)m; (void)format; }
static void Status(HfMod* m, const char* text) { (void)m; (void)text; }
static uintptr_t Module(const char* name, size_t* size)
{
    (void)size;
    if (name && !strcmp(name, "helldivers2.exe")) return exeBase;
    return game;
}
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
    CHECK((flags & SND_MEMORY) && (flags & SND_ASYNC));
    if (saluteAudio && wav == (LPCWSTR)saluteAudio) { saluteCalls++; return playback; }
    CHECK(wav == (LPCWSTR)audio);
    calls++;
    return playback;
}
static void Observe(void) { Tick((HfMod*)&mod, 0.016f, NULL); }
static void MarkDead(void) { Put32(players, 0x2E0, 2); Put32(healthRecord, 0x19C, 2); }
static void Die(void)
{
    MarkDead();
    HfPlayerEvent event = {{sizeof event, HF_ON_PLAYER_DOWN, 1}, 42, 100, 1};
    Down((HfMod*)&mod, &event, NULL);
}
static void Reset(void)
{
    memset(&saved, 0, sizeof saved); memset(slotStates, 0, sizeof slotStates);
    memset(consumedRing, 0, sizeof consumedRing); consumedRingCursor = 0; activeSlot = -1;
    consumedPeer = consumedAvatar = consumedNetwork = 0; reportedPollRejection = 0;
    ResetSaluteTracker(); saluteLastFiredMs = 0;
    clockMs = 1000; lastSample = 0; missingMapping = 0;
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
    ready = proven = gameLoaded = 1; calls = 0; saluteCalls = 0; playback = 1; state = HF_STATE_MISSION; play = Play;
}

/* Write a valid minimal 16-bit PCM RIFF/WAVE file for offline Salute tests */
static void WriteMinimalWave(const wchar_t* path, int corrupt)
{
    unsigned char wav[64] = {0};
    memcpy(wav + 0, "RIFF", 4);
    Put32(wav, 4, 56u);
    memcpy(wav + 8, corrupt ? "XXXX" : "WAVE", 4);
    memcpy(wav + 12, "fmt ", 4);
    Put32(wav, 16, 16u);
    uint16_t pcmTag = 1u, ch = 1u, blockAlign = 2u, bits = 16u;
    memcpy(wav + 20, &pcmTag, 2);
    memcpy(wav + 22, &ch, 2);
    Put32(wav, 24, 48000u);
    Put32(wav, 28, 96000u);
    memcpy(wav + 32, &blockAlign, 2);
    memcpy(wav + 34, &bits, 2);
    memcpy(wav + 36, "data", 4);
    Put32(wav, 40, 20u);
    HANDLE out = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(out != INVALID_HANDLE_VALUE);
    DWORD wrote = 0;
    CHECK(WriteFile(out, wav, sizeof wav, &wrote, NULL) && wrote == sizeof wav);
    CloseHandle(out);
}

static void SetBindPose(float mats[SALUTE_AVATAR_NODES][16])
{
    memset(mats, 0, sizeof(float) * SALUTE_AVATAR_NODES * 16u);
    for (unsigned i = 0; i < SALUTE_AVATAR_NODES; i++)
    {
        mats[i][0] = mats[i][5] = mats[i][10] = mats[i][15] = 1.0f;
    }
    /* Verified Helldiver avatar bind-pose coordinates */
    mats[AV_NODE_ROOT][12] = 0.0f;        mats[AV_NODE_ROOT][13] = 0.0f;       mats[AV_NODE_ROOT][14] = 0.0f;
    mats[AV_NODE_HIPS][12] = 0.0f;        mats[AV_NODE_HIPS][13] = -0.02f;     mats[AV_NODE_HIPS][14] = 0.98f;
    mats[AV_NODE_CHEST][12] = 0.0f;       mats[AV_NODE_CHEST][13] = -0.029f;   mats[AV_NODE_CHEST][14] = 1.344f;
    mats[AV_NODE_L_SHOULDER][12] = -0.21f; mats[AV_NODE_L_SHOULDER][13] = -0.069f; mats[AV_NODE_L_SHOULDER][14] = 1.474f;
    mats[AV_NODE_L_HAND][12] = -0.30f;    mats[AV_NODE_L_HAND][13] = 0.02f;    mats[AV_NODE_L_HAND][14] = 0.92f;
    mats[AV_NODE_R_SHOULDER][12] = +0.21f; mats[AV_NODE_R_SHOULDER][13] = -0.069f; mats[AV_NODE_R_SHOULDER][14] = 1.474f;
    mats[AV_NODE_R_ELBOW][12] = +0.34f;   mats[AV_NODE_R_ELBOW][13] = -0.04f;  mats[AV_NODE_R_ELBOW][14] = 1.22f;
    mats[AV_NODE_R_HAND][12] = +0.32f;    mats[AV_NODE_R_HAND][13] = 0.12f;    mats[AV_NODE_R_HAND][14] = 0.94f;
    mats[AV_NODE_HEAD][12] = 0.0f;        mats[AV_NODE_HEAD][13] = -0.025f;    mats[AV_NODE_HEAD][14] = 1.691f;
}

static void SetSuperEarthSalutePose(float mats[SALUTE_AVATAR_NODES][16])
{
    SetBindPose(mats);
    /* Super Earth Salute: right upper arm horizontal (+X), right forearm vertical (+Z), fist at head height */
    mats[AV_NODE_R_ELBOW][12] = +0.49f; mats[AV_NODE_R_ELBOW][13] = -0.05f; mats[AV_NODE_R_ELBOW][14] = 1.47f;
    mats[AV_NODE_R_HAND][12]  = +0.48f; mats[AV_NODE_R_HAND][13]  = -0.02f; mats[AV_NODE_R_HAND][14]  = 1.75f;
}

static void SetCasualTempleSalutePose(float mats[SALUTE_AVATAR_NODES][16])
{
    SetBindPose(mats);
    /* Casual Salute: right elbow flared out, right hand at right temple */
    mats[AV_NODE_R_ELBOW][12] = +0.44f; mats[AV_NODE_R_ELBOW][13] = +0.04f; mats[AV_NODE_R_ELBOW][14] = 1.50f;
    mats[AV_NODE_R_HAND][12]  = +0.16f; mats[AV_NODE_R_HAND][13]  = +0.05f; mats[AV_NODE_R_HAND][14]  = 1.71f;
}

/* The controls recorded player 5, avatar 710 at lifecycle 3 then 2, and 738 at 3.
 * There was no logged Down invocation. Native health >= 2 is a fixture input, not in the receipt. */
static void RecordedMissingDown(void)
{
    Reset(); sampleEvery = 100;
    Put32(playerDesc, 8, 5); mapEntries[2] = 5; mapEntries[3] = 0;
    Put32(players, 0x3A8, 580); Put32(healthDesc, 8, 710);
    Observe(); CHECK(saved.avatar == 710 && saved.network == 580 && saved.player == 5 && calls == 0);
    MarkDead();
    clockMs += 100; Observe(); CHECK(calls == 1);
    clockMs += 100; Observe(); CHECK(calls == 1);
    HfPlayerEvent late = {{sizeof late, HF_ON_PLAYER_DOWN, 1}, 42, 5, 1};
    Down((HfMod*)&mod, &late, NULL); CHECK(calls == 1);
    Put32(players, 0x2E0, 3); Put32(players, 0x3A8, 606);
    Put32(healthDesc, 8, 738); Put32(healthRecord, 0x19C, 0);
    clockMs += 5000; Observe(); CHECK(saved.avatar == 738 && calls == 1);
    Down((HfMod*)&mod, &late, NULL); CHECK(calls == 1);
    sampleEvery = 0;
}

static void RunSaluteTests(const wchar_t* fixtureDir)
{
    /* 1. Optional WAV loader: absent -> graceful silent fallback; corrupt -> rejected; valid -> loaded */
    CHECK(!LoadOptionalSaluteWave(fixtureDir) && !saluteReady && saluteAudio == NULL);
    wchar_t audioDir[1100], salutePath[1100];
    swprintf(audioDir, 1100, L"%ls\\audio", fixtureDir);
    CHECK(CreateDirectoryW(audioDir, NULL) || GetLastError() == ERROR_ALREADY_EXISTS);
    swprintf(salutePath, 1100, L"%ls\\stormtrooper-salute.wav", audioDir);
    WriteMinimalWave(salutePath, 1);
    CHECK(!LoadOptionalSaluteWave(fixtureDir) && !saluteReady);
    WriteMinimalWave(salutePath, 0);
    CHECK(LoadOptionalSaluteWave(fixtureDir) && saluteReady && saluteAudio != NULL && saluteAudioSize == 64u);

    /* 2. Build synthetic EntityManager + UnitManager + InventoryManager in heap memory */
    unsigned char* fakeExe = (unsigned char*)calloc(1, 0x1A20000);
    unsigned char* fakeEm = (unsigned char*)calloc(1, 0xF40000);
    unsigned char* fakeUm = (unsigned char*)calloc(1, 0x200);
    unsigned char* fakeInv = (unsigned char*)calloc(1, 0x200);
    uint32_t* emBuckets = (uint32_t*)calloc(16, sizeof(uint32_t));
    uint32_t* invBuckets = (uint32_t*)calloc(16, sizeof(uint32_t));
    unsigned char* invRecords = (unsigned char*)calloc(4, 0x30);
    uintptr_t* umSlots = (uintptr_t*)calloc(8, sizeof(uintptr_t));
    uint8_t* umGens = (uint8_t*)calloc(8, 1);
    unsigned char* fakeUnit = (unsigned char*)calloc(1, 0x200);
    float (*worldPoses)[16] = (float (*)[16])calloc(SALUTE_AVATAR_NODES, sizeof(float[16]));
    CHECK(fakeExe && fakeEm && fakeUm && fakeInv && emBuckets && invBuckets && invRecords && umSlots && umGens && fakeUnit && worldPoses);

    exeBase = (uintptr_t)fakeExe;
    emCell = (uintptr_t)fakeEm;
    invCell = (uintptr_t)fakeInv;

    /* Wire EntityManager map at +0xF1AEB0: avatar=210 -> descIdx=3 -> unitHandle=(gen=5<<22)|2 */
    for (int i = 0; i < 16; i++) emBuckets[i] = UINT32_MAX;
    uint32_t avatarId = 210u, descIdx = 3u, unitIdx = 2u;
    uint8_t unitGen = 5u;
    uint32_t unitHandle = ((uint32_t)unitGen << 22) | unitIdx;
    emBuckets[(avatarId & 7u) * 2u] = avatarId;
    emBuckets[(avatarId & 7u) * 2u + 1u] = descIdx;
    Put64(fakeEm, SALUTE_EM_ENTITY_MAP + 0, (uintptr_t)emBuckets);
    Put32(fakeEm, SALUTE_EM_ENTITY_MAP + 8, 8u);
    Put32(fakeEm, SALUTE_EM_ENTITY_MAP + 12, UINT32_MAX);
    Put32(fakeEm, SALUTE_EM_ENTITY_MAP + 16, 1u);

    uintptr_t descAt = (uintptr_t)fakeEm + (uintptr_t)CD_EM_DEFAULT_OFFSET + (uintptr_t)descIdx * 24u;
    Put64((void*)descAt, 0, SALUTE_AVATAR_RESOURCE);
    Put32((void*)descAt, 8, avatarId);
    Put32((void*)descAt, 12, unitHandle);
    Put32((void*)descAt, 16, 200u);
    Put32((void*)descAt, 20, 1u);

    /* Wire UnitManager at exe + 0x1A100F0 */
    Put64(fakeExe, SALUTE_EXE_UNITS_RVA, (uintptr_t)fakeUm);
    Put64(fakeUm, 0x88, (uintptr_t)umSlots);
    Put32(fakeUm, 0x98, 8u);
    Put64(fakeUm, 0xA0, (uintptr_t)umGens);
    umGens[unitIdx] = unitGen;
    umSlots[unitIdx] = (uintptr_t)fakeUnit;
    Put64(fakeUnit, 0x00, exeBase + SALUTE_EXE_UNIT_VTABLE_RVA);
    Put32(fakeUnit, 0x70, SALUTE_AVATAR_NODES);
    Put64(fakeUnit, 0x88, (uintptr_t)worldPoses);

    /* Wire InventoryManager at invCell: avatar=210 -> recIdx=1 */
    for (int i = 0; i < 16; i++) invBuckets[i] = UINT32_MAX;
    invBuckets[(avatarId & 7u) * 2u] = avatarId;
    invBuckets[(avatarId & 7u) * 2u + 1u] = 1u;
    Put64(fakeInv, 0x28 + 0, (uintptr_t)invBuckets);
    Put32(fakeInv, 0x28 + 8, 8u);
    Put32(fakeInv, 0x28 + 12, UINT32_MAX);
    Put32(fakeInv, 0x28 + 16, 1u);
    Put64(fakeInv, 0x50, (uintptr_t)invRecords);

    /* Test A: Idle bind pose never triggers Salute */
    Reset(); ready = 0; saluteReady = 1; sampleEvery = 100;
    SetBindPose(worldPoses);
    for (int t = 0; t < 6; t++) { clockMs += 100; Observe(); }
    CHECK(saluteCalls == 0 && calls == 0);

    /* Test B: Super Earth Salute in HF_STATE_SHIP triggers once after 3 samples and latches while held */
    Reset(); ready = 0; saluteReady = 1; state = HF_STATE_SHIP; sampleEvery = 100;
    SetSuperEarthSalutePose(worldPoses);
    clockMs += 100; Observe(); CHECK(saluteCalls == 0);
    clockMs += 100; Observe(); CHECK(saluteCalls == 0);
    clockMs += 100; Observe(); CHECK(saluteCalls == 1);
    /* Holding salute for 3 full seconds must NOT loop/repeat */
    for (int t = 0; t < 30; t++) { clockMs += 100; Observe(); }
    CHECK(saluteCalls == 1);
    /* Dropping back to bind pose unlatches after 2 samples, then Casual Temple Salute fires in HF_STATE_MISSION! */
    SetBindPose(worldPoses);
    clockMs += 100; Observe();
    clockMs += 100; Observe();
    CHECK(!saluteLatched && saluteCalls == 1);

    state = HF_STATE_MISSION;
    SetCasualTempleSalutePose(worldPoses);
    clockMs += 1600; Observe(); CHECK(saluteCalls == 1);
    clockMs += 100;  Observe(); CHECK(saluteCalls == 1);
    clockMs += 100;  Observe(); CHECK(saluteCalls == 2);

    /* Test C: Rejections — two-handed raise (Hug), grenade/stratagem selection=4, sprinting, downed/dead */
    Reset(); ready = 0; saluteReady = 1; state = HF_STATE_MISSION; sampleEvery = 100;
    SetSuperEarthSalutePose(worldPoses);
    worldPoses[AV_NODE_L_HAND][14] = 1.55f; /* Left hand raised too (two-handed emote / support weapon) */
    for (int t = 0; t < 5; t++) { clockMs += 100; Observe(); }
    CHECK(saluteCalls == 0);

    Reset(); ready = 0; saluteReady = 1; state = HF_STATE_MISSION; sampleEvery = 100;
    SetSuperEarthSalutePose(worldPoses);
    Put32(invRecords, 0x30u * 1u + 0x1Cu, 4u); /* Holding grenade/stratagem orb */
    for (int t = 0; t < 5; t++) { clockMs += 100; Observe(); }
    CHECK(saluteCalls == 0);
    Put32(invRecords, 0x30u * 1u + 0x1Cu, 1u); /* Primary equipped on hip while saluting -> allowed */
    for (int t = 0; t < 3; t++) { clockMs += 100; Observe(); }
    CHECK(saluteCalls == 1);

    /* Moving fast (>0.45 m/s) rejects salute */
    Reset(); ready = 0; saluteReady = 1; state = HF_STATE_MISSION; sampleEvery = 100;
    SetSuperEarthSalutePose(worldPoses);
    for (int t = 0; t < 5; t++)
    {
        worldPoses[AV_NODE_ROOT][12] += 0.20f; /* 2.0 m/s */
        clockMs += 100;
        Observe();
    }
    CHECK(saluteCalls == 0);

    /* Clean up synthetic Salute arenas & file */
    CHECK(DeleteFileW(salutePath) && RemoveDirectoryW(audioDir));
    LoadOptionalSaluteWave(NULL);
    exeBase = 0; emCell = 0; invCell = 0; sampleEvery = 0;
    free(worldPoses); free(fakeUnit); free(umGens); free(umSlots);
    free(invRecords); free(invBuckets); free(emBuckets);
    free(fakeInv); free(fakeUm); free(fakeEm); free(fakeExe);
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
    static Proof candidate[YODA_MAX_VISUALS]; memcpy(candidate, proofs, sizeof proofs);
    Proof source = proofs[0]; CHECK(Unchanged(&source));
    CHECK(visuals[0].altMainHash && visuals[0].altGpuHash && strcmp(visuals[0].mainHash, visuals[0].altMainHash) && strcmp(visuals[0].gpuHash, visuals[0].altGpuHash));
    {
        Proof probe = source;
        int isCompact = MatchesPair(&probe, visuals[0].mainHash, visuals[0].gpuHash);
        int isAlt = MatchesPair(&probe, visuals[0].altMainHash, visuals[0].altGpuHash);
        CHECK(isCompact != isAlt);
        CHECK(!MatchesPair(&probe, visuals[0].mainHash, visuals[0].altGpuHash));
        CHECK(!MatchesPair(&probe, visuals[0].altMainHash, visuals[0].gpuHash));
    }
    CHECK(CreateDirectoryW(argv[2], NULL));
    CHECK(ScanVisuals(argv[2]) == 0);
    wchar_t main[1100], gpu[1100], later[1100], other[1100];
    swprintf(main, 1100, L"%ls\\9ba626afa44a3aa3.patch_1", argv[2]);
    swprintf(gpu, 1100, L"%ls.gpu_resources", main);
    WritePart(main, &source, 0, 0); WritePart(gpu, &source, 1, 0);
    CHECK(ScanVisuals(argv[2]) == 1); CHECK(proofs[0].valid && Unchanged(&proofs[0]));
    WritePart(gpu, &source, 1, 1);
    CHECK(ScanVisuals(argv[2]) == 0);
    WritePart(gpu, &source, 1, 0);
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
    game = (uintptr_t)calloc(1, 0x3480000); CHECK(game != 0);
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
    RecordedMissingDown();
    /* SDK samples run before Tick. A Down callback can see health before it confirms death. */
    Reset(); Observe(); Put32(players, 0x2E0, 2); Put32(healthRecord, 0x19C, 1);
    HfPlayerEvent event = {{sizeof event, HF_ON_PLAYER_DOWN, 1}, 42, 100, 1};
    Down((HfMod*)&mod, &event, NULL); Observe(); CHECK(calls == 0);
    Put32(healthRecord, 0x19C, 2); clockMs += 100; Observe(); CHECK(calls == 1);
    Down((HfMod*)&mod, &event, NULL); CHECK(calls == 1);
    /* The SDK suppresses Down if its last sample was intermediate. An intermediate sample
     * by itself, a missing mapping and a death belonging to another avatar must stay silent. */
    Reset(); Observe(); Put32(players, 0x2E0, 1); Put32(healthRecord, 0x19C, 2);
    clockMs += 100; Observe(); CHECK(calls == 0);
    Put32(players, 0x2E0, 2); clockMs += 100; Observe(); CHECK(calls == 1);
    Reset(); Observe(); MarkDead();
    missingMapping = 1; Observe(); CHECK(calls == 0);
    Reset(); Observe(); MarkDead();
    Put32(players, 0x3A8, 201); Observe(); CHECK(calls == 0 && !saved.avatar);
    Reset(); Observe(); MarkDead();
    Put64(user, 0xB398, 999); Observe(); CHECK(calls == 0 && !saved.avatar);
    Reset(); Observe(); MarkDead();
    Put32(health, 0x1020, 0); Observe(); CHECK(calls == 0);
    Reset(); Observe(); MarkDead();
    Put32(manager, 0x96C + 4, 123); Observe(); CHECK(calls == 0);
    Reset(); Observe(); MarkDead();
    clockMs += 501; Observe(); CHECK(calls == 0 && !saved.avatar);
    Reset(); Put32(manager, 0x96C + 12, 0x61B31723); Observe(); CHECK(!saved.avatar);
    Die(); Observe(); CHECK(calls == 0);
    Reset(); gameLoaded = 0; Observe(); CHECK(!saved.avatar); Loaded((HfMod*)&mod, HF_EVENT_GAME_LOADED, NULL); Observe(); CHECK(saved.avatar == 210);
    Reset(); Observe(); CHECK(saved.avatar == 210 && saved.kit == visuals[0].kit); Die(); CHECK(calls == 1); Die(); Observe(); CHECK(calls == 1);
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
    Down((HfMod*)&mod, &event, NULL); CHECK(calls == 0);
    Reset(); Observe(); Put32(health, 0x1020, 0); Die(); CHECK(calls == 0);
    Reset(); Observe(); event.peer = 999; Down((HfMod*)&mod, &event, NULL); CHECK(calls == 0);
    Reset(); Observe(); playback = 0; Die(); CHECK(calls == 1); playback = 1; Die(); Observe(); CHECK(calls == 1);
    Reset(); Observe(); Die(); Put32(players, 0x2E0, 3); Put32(players, 0x3A8, 201); Put32(healthDesc, 8, 211); Put32(healthRecord, 0x19C, 0); Observe(); Die(); CHECK(calls == 2);
    Reset(); Observe(); HfStateEvent changed = {{sizeof changed, HF_ON_STATE, 1}, 4, 3}; State((HfMod*)&mod, &changed, NULL); CHECK(!saved.avatar); Die(); Observe(); CHECK(calls == 0);
    Reset(); state = HF_STATE_SHIP; Observe(); CHECK(!saved.avatar); Die(); Observe(); CHECK(calls == 0);
    Reset(); proofs[0].valid = 0; Observe(); Die(); CHECK(calls == 0); proofs[0] = good;
    Reset(); Observe(); HANDLE edit = CreateFileW(gpu, FILE_APPEND_DATA, 0, NULL, OPEN_EXISTING, 0, NULL); CHECK(edit != INVALID_HANDLE_VALUE);
    DWORD wrote; CHECK(WriteFile(edit, "x", 1, &wrote, NULL)); CloseHandle(edit); CHECK(!Unchanged(&proofs[0])); Die(); Observe(); CHECK(calls == 0);

    /* 0.2.0 Co-op per-slot tests: verify local death in every slot 0..3 and silent remote slots. */
    static unsigned char slotDesc[4][24];
    for (uint32_t s = 0; s < 4; s++)
    {
        memcpy(proofs, candidate, sizeof proofs);
        Reset();
        Put32(players, 0x84, 4);
        for (uint32_t k = 0; k < 4; k++)
        {
            uint64_t p = (k == s) ? 42ull : (1000ull + k);
            uint32_t pl = 100u + k;
            Put64(players, 0x2C8 + 0x38 * k, p);
            Put64(players, 0xE8 + 8 * k, (uintptr_t)slotDesc[k]);
            Put32(slotDesc[k], 8, pl);
            Put32(players, 0x2E0 + 0x38 * k, 3);
            Put32(players, 0x3A8 + 0x20 * k, 200u + k);
        }
        Put64(manager, 0x948, (uintptr_t)slotDesc[s]);
        for (int mi = 0; mi < 8; mi++) mapEntries[mi] = UINT32_MAX;
        mapEntries[((100u + s) % 4u) * 2u] = 100u + s;
        mapEntries[((100u + s) % 4u) * 2u + 1u] = 0;
        Put32(healthDesc, 8, 210u + s);
        Observe();
        CHECK(saved.avatar == 210u + s && saved.slot == s && activeSlot == (int)s && calls == 0);
        /* Remote peer dying must stay silent. */
        uint32_t remoteSlot = (s + 1u) % 4u;
        Put32(players, 0x2E0 + 0x38 * remoteSlot, 2);
        HfPlayerEvent remoteEv = {{sizeof remoteEv, HF_ON_PLAYER_DOWN, 1}, 1000ull + remoteSlot, 100u + remoteSlot, 0};
        Down((HfMod*)&mod, &remoteEv, NULL);
        CHECK(calls == 0);
        /* Local peer in slot s dying triggers exactly once. */
        Put32(players, 0x2E0 + 0x38 * s, 2);
        Put32(healthRecord, 0x19C, 2);
        HfPlayerEvent localEv = {{sizeof localEv, HF_ON_PLAYER_DOWN, 1}, 42ull, 100u + s, 1};
        Down((HfMod*)&mod, &localEv, NULL);
        CHECK(calls == 1);
        Down((HfMod*)&mod, &localEv, NULL);
        clockMs += 100; Observe();
        CHECK(calls == 1);
    }

    /* Slot compaction survival: local peer shifts from slot 3 to slot 1 when other peers leave. */
    memcpy(proofs, candidate, sizeof proofs);
    Reset();
    Put32(players, 0x84, 4);
    for (uint32_t k = 0; k < 4; k++)
    {
        Put64(players, 0x2C8 + 0x38 * k, (k == 3) ? 42ull : (2000ull + k));
        Put64(players, 0xE8 + 8 * k, (uintptr_t)slotDesc[k]);
        Put32(slotDesc[k], 8, 300u + k);
        Put32(players, 0x2E0 + 0x38 * k, 3);
        Put32(players, 0x3A8 + 0x20 * k, 400u + k);
    }
    Put64(manager, 0x948, (uintptr_t)slotDesc[3]);
    for (int mi = 0; mi < 8; mi++) mapEntries[mi] = UINT32_MAX;
    mapEntries[(303u % 4u) * 2u] = 303u;
    mapEntries[(303u % 4u) * 2u + 1u] = 0;
    Put32(healthDesc, 8, 413u);
    Observe();
    CHECK(saved.avatar == 413u && saved.slot == 3 && activeSlot == 3);
    /* Compact: slots 0 and 2 leave; local peer moves to slot 1 keeping player=303, network=403. */
    Put32(players, 0x84, 2);
    Put64(players, 0x2C8 + 0x38 * 0, 2001ull); Put32(slotDesc[0], 8, 301u); Put32(players, 0x3A8 + 0x20 * 0, 401u);
    Put64(players, 0x2C8 + 0x38 * 1, 42ull);   Put32(slotDesc[1], 8, 303u); Put32(players, 0x3A8 + 0x20 * 1, 403u);
    Put32(players, 0x2E0 + 0x38 * 1, 2);
    Put32(healthRecord, 0x19C, 2);
    clockMs += 50;
    {
        HfPlayerEvent compEv = {{sizeof compEv, HF_ON_PLAYER_DOWN, 1}, 42ull, 303u, 1};
        Down((HfMod*)&mod, &compEv, NULL);
        CHECK(calls == 1 && activeSlot == 1 && saved.slot == 1);
    }

    /* Extensible yoda-torsos.json test + Clonedivers real staged check if present on disk. */
    {
        wchar_t jsonPath[1100];
        swprintf(jsonPath, 1100, L"%ls\\yoda-torsos.json", argv[2]);
        HANDLE jf = CreateFileW(jsonPath, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        CHECK(jf != INVALID_HANDLE_VALUE);
        const char* sampleJson =
            "{\"torsos\":["
            "{\"kit\":\"0xABCDEF01\",\"body\":0,\"unit\":\"0x1122334455667788\","
            "\"main_sha256\":\"" "4b4a641bb83575eaeb452db5ecbccefaf8e868957f994a98f0e7aa6198517bde" "\","
            "\"gpu_sha256\":\""  "e04b9b14658300e743f5377c35637cdd16578f8303616c66883ca44ca4ea58f7" "\"},"
            "{\"kit\":123,\"body\":1,\"unit\":999,\"main_sha256\":\"bad\",\"gpu_sha256\":\"bad\"}"
            "]}";
        DWORD jw = 0; CHECK(WriteFile(jf, sampleJson, (DWORD)strlen(sampleJson), &jw, NULL) && jw == strlen(sampleJson));
        CloseHandle(jf);
        LoadExtraTorsos(argv[2]);
        CHECK(visualCount == YODA_BUILTIN_VISUALS + 1);
        CHECK(visuals[visualCount - 1].kit == 0xABCDEF01u && visuals[visualCount - 1].body == 0u && visuals[visualCount - 1].unit == 0x1122334455667788ull);
        CHECK(DeleteFileW(jsonPath));

        /* Verify YODA_MAX_VISUALS == 256 capacity ceiling (8 built-ins + 248 dynamic entries, 249th rejected) */
        CHECK(YODA_MAX_VISUALS == 256);
        FILE* capf = _wfopen(jsonPath, L"wb");
        CHECK(capf != NULL);
        fprintf(capf, "{\"schema_version\":1,\"torsos\":[\n");
        for (unsigned vi = 0; vi < 250u; vi++) {
            fprintf(capf, "%s{\"kit\":\"0x%08X\",\"body\":%u,\"unit\":\"0x%016llX\","
                    "\"main_sha256\":\"4b4a641bb83575eaeb452db5ecbccefaf8e868957f994a98f0e7aa6198517bde\","
                    "\"gpu_sha256\":\"e04b9b14658300e743f5377c35637cdd16578f8303616c66883ca44ca4ea58f7\"}",
                    vi ? ",\n" : "", 0x70000000u + vi, vi & 1u, 0xD000000000000000ull + vi + 1ull);
        }
        fprintf(capf, "\n]}\n");
        fclose(capf);
        LoadExtraTorsos(argv[2]);
        CHECK(visualCount == 256u);
        CHECK(visuals[255].kit == 0x70000000u + 247u);
        CHECK(DeleteFileW(jsonPath));

        /* Validate production yoda-torsos.json (22 extra torsos -> 30 total visuals) + real delta archives if present */
        {
            const wchar_t* prodModDir = (GetFileAttributesW(L".\\yoda-torsos.json") != INVALID_FILE_ATTRIBUTES) ? L"." :
                                        (GetFileAttributesW(L"..\\yoda-torsos.json") != INVALID_FILE_ATTRIBUTES) ? L".." :
                                        L"forge-mods\\empire-lego-yoda";
            LoadExtraTorsos(prodModDir);
            CHECK(visualCount == YODA_BUILTIN_VISUALS + 22u);
            const wchar_t* cloneDelta = L"D:\\ClaudeWork\\workstreams-20261116-v2\\clonedivers-r20\\delta\\lego-clones";
            if (GetFileAttributesW(cloneDelta) != INVALID_FILE_ATTRIBUTES) {
                CHECK(ScanVisuals(cloneDelta) == 12);
                static Proof cloneProofs[YODA_MAX_VISUALS];
                memcpy(cloneProofs, proofs, sizeof proofs);
                for (size_t cv = YODA_BUILTIN_VISUALS; cv < YODA_BUILTIN_VISUALS + 12u; cv++) {
                    CHECK(cloneProofs[cv].valid && cloneProofs[cv].row.d[1] == 0 && Unchanged(&cloneProofs[cv]));
                    memcpy(proofs, cloneProofs, sizeof proofs);
                    Reset();
                    Put32(manager, 0x96C, visuals[cv].body);
                    Put32(manager, 0x96C + 12, visuals[cv].kit);
                    Put32(kit, 0, visuals[cv].kit);
                    Observe();
                    CHECK(saved.visual == (int)cv && saved.avatar == 210);
                    Die();
                    CHECK(calls == 1);
                }
            }
        }

        LoadExtraTorsos(NULL);
        CHECK(visualCount == YODA_BUILTIN_VISUALS);
    }

    /* 0.2.3 Stormtrooper Salute pose detector & optional drop-in WAV tests */
    RunSaluteTests(argv[2]);

    const wchar_t* optLiveDir = _wgetenv(L"YODA_TEST_LIVE_MOD_DIR");
    if (optLiveDir && *optLiveDir && GetFileAttributesW(optLiveDir) != INVALID_FILE_ATTRIBUTES)
    {
        CHECK(ScanVisuals(optLiveDir) >= 4);
    }

    free((void*)game);
    printf("PASS: %d checks; 0.2.3 co-op slots 0..3, compaction, Empire + Clonedivers LEGO hashes, yoda-torsos.json, Stormtrooper Salute pose & WAV verified\n", checks);
    return 0;
}
