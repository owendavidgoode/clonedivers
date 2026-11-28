/* Effective staged-patch reader, adapted from empire-lego-yoda's visual_gate.h.
 * 0.4.0: Verifies either the 5-tier EmpireDivers TIE closure (assets[]) or the 5-tier Clonedivers r26
 * Republic air wing closure (cloneAssets[]) across [0]=full, [1]=lighter, [2]=reduced1024, [3]=reduced512,
 * [4]=potato256 with cross-part tier coherence (TIE_ALL_TIERS_MASK = 0x1Fu). */
#pragma once
#include <bcrypt.h>
#define TIE_TIER_COUNT 5u
#define TIE_ALL_TIERS_MASK ((uint8_t)((1u << TIE_TIER_COUNT) - 1u))
#define TIE_PACK_NONE   0
#define TIE_PACK_EMPIRE 1
#define TIE_PACK_CLONES 2

typedef struct ArchiveRow { uint64_t q[7]; uint32_t d[6]; } ArchiveRow;
_Static_assert(sizeof(ArchiveRow) == 80, "archive row");
typedef struct Proof
{
    wchar_t path[1024];
    ArchiveRow row;
    unsigned index;
    BY_HANDLE_FILE_INFORMATION info;
    int found;
} Proof;

#define ASSET_COUNT (sizeof assets / sizeof assets[0])
#define MAX_ASSET_COUNT 256u
_Static_assert(ASSET_COUNT <= MAX_ASSET_COUNT, "empire asset closure exceeds MAX_ASSET_COUNT");
_Static_assert(CLONE_ASSET_COUNT <= MAX_ASSET_COUNT, "clones asset closure exceeds MAX_ASSET_COUNT");

static Proof proofs[MAX_ASSET_COUNT];
static const Asset* activeAssets = assets;
static size_t activeAssetCount = ASSET_COUNT;
static int g_activePack = TIE_PACK_EMPIRE;
static char assetError[2400];

static int AssetError(const char* why, const Asset* a, const wchar_t* path)
{
    if (a)
    {
        char expected[384]; size_t pos = 0; expected[0] = 0;
        for (size_t i = 0; i < TIE_TIER_COUNT && a->hashes[i]; i++)
        {
            int dup = 0;
            for (size_t j = 0; j < i; j++) if (a->hashes[j] && !strcmp(a->hashes[i], a->hashes[j])) dup = 1;
            if (dup) continue;
            int n = snprintf(expected + pos, sizeof expected - pos, "%s%s", pos ? "|" : "", a->hashes[i]);
            if (n < 0 || (size_t)n >= sizeof expected - pos) break;
            pos += (size_t)n;
        }
        snprintf(assetError, sizeof assetError,
            "%s resource=%016llx type=%016llx part=%u expected_sha256=%s file=%ls",
            why, (unsigned long long)a->name, (unsigned long long)a->type, a->part, expected, path ? path : L"(no provider)");
    }
    else snprintf(assetError, sizeof assetError, "%s file=%ls", why, path ? path : L"(unknown)");
    return 0;
}
static NTSTATUS (WINAPI *openHash)(BCRYPT_ALG_HANDLE*, LPCWSTR, LPCWSTR, ULONG);
static NTSTATUS (WINAPI *hashBytes)(BCRYPT_ALG_HANDLE, PUCHAR, ULONG, PUCHAR, ULONG, PUCHAR, ULONG);
static BCRYPT_ALG_HANDLE sha;

static uint8_t Hash(const void* bytes, uint32_t size, const char* const hashes[TIE_TIER_COUNT])
{
    unsigned char digest[32]; char hex[65]; uint8_t mask = 0;
    static const unsigned char empty = 0;
    const unsigned char* input = size ? (const unsigned char*)bytes : &empty;
    if (!sha || !hashes || !input) return 0;
    for (unsigned t = 0; t < TIE_TIER_COUNT; t++) if (!hashes[t]) return 0;
    if (hashBytes(sha, NULL, 0, (PUCHAR)input, size, digest, sizeof digest) < 0) return 0;
    for (int i = 0; i < 32; i++) snprintf(hex + i*2, 3, "%02x", digest[i]);
    for (unsigned t = 0; t < TIE_TIER_COUNT; t++)
        if (!strcmp(hex, hashes[t])) mask |= (uint8_t)(1u << t);
    return mask;
}
static HANDLE OpenRead(const wchar_t* path)
{ return CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL); }
static int ReadAt(HANDLE file, uint64_t at, void* out, DWORD size)
{
    LARGE_INTEGER offset; offset.QuadPart = (LONGLONG)at; DWORD got = 0;
    return SetFilePointerEx(file, offset, NULL, FILE_BEGIN) && ReadFile(file, out, size, &got, NULL) && got == size;
}
static int SameInfo(const BY_HANDLE_FILE_INFORMATION* a, const BY_HANDLE_FILE_INFORMATION* b)
{
    return a->dwVolumeSerialNumber == b->dwVolumeSerialNumber && a->nFileIndexHigh == b->nFileIndexHigh &&
        a->nFileIndexLow == b->nFileIndexLow && a->nFileSizeHigh == b->nFileSizeHigh && a->nFileSizeLow == b->nFileSizeLow &&
        !memcmp(&a->ftLastWriteTime, &b->ftLastWriteTime, sizeof(FILETIME));
}
static int Part(Proof* p, const Asset* a, uint8_t* ioTierMask)
{
    const wchar_t* suffixes[] = {L"", L".stream", L".gpu_resources"}; wchar_t path[1100];
    if (swprintf(path, 1100, L"%ls%ls", p->path, suffixes[a->part]) < 0) return AssetError("payload path too long", a, p->path);
    HANDLE file = OpenRead(path);
    if (file == INVALID_HANDLE_VALUE) return AssetError("payload file missing or unreadable", a, path);
    BY_HANDLE_FILE_INFORMATION before, after; LARGE_INTEGER length;
    uint32_t size = p->row.d[a->part]; uint64_t offset = p->row.q[2+a->part];
    int ok = GetFileInformationByHandle(file, &before) && GetFileSizeEx(file, &length) && size <= 128u*1024*1024 &&
        offset <= (uint64_t)length.QuadPart && size <= (uint64_t)length.QuadPart-offset;
    void* bytes = (ok && size > 0) ? malloc(size) : NULL;
    uint8_t partMask = (ok && (size == 0 || (bytes && ReadAt(file, offset, bytes, size))))
        ? Hash(bytes, size, a->hashes) : 0u;
    int sameOk = (partMask != 0u) && GetFileInformationByHandle(file, &after) && SameInfo(&before, &after);
    free(bytes); CloseHandle(file);
    if (!sameOk) return AssetError("payload bounds/read/hash mismatch or file changed", a, path);
    if (ioTierMask)
    {
        *ioTierMask &= partMask;
        if (!*ioTierMask) return AssetError("inconsistent texture tier across staged closure", a, path);
    }
    p->info = after;
    return 1;
}
static int AssetsUnchanged(void)
{
    const wchar_t* suffixes[] = {L"", L".stream", L".gpu_resources"};
    for (size_t i = 0; i < activeAssetCount; i++)
    {
        wchar_t path[1100]; BY_HANDLE_FILE_INFORMATION info;
        if (!proofs[i].found) return AssetError("missing staged proof; restart required", &activeAssets[i], NULL);
        if (swprintf(path, 1100, L"%ls%ls", proofs[i].path, suffixes[activeAssets[i].part]) < 0)
            return AssetError("staged payload path too long", &activeAssets[i], proofs[i].path);
        HANDLE file = OpenRead(path);
        int ok = file != INVALID_HANDLE_VALUE && GetFileInformationByHandle(file, &info) && SameInfo(&info, &proofs[i].info);
        if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
        if (!ok) return AssetError("staged payload file missing or changed; restart required", &activeAssets[i], path);
    }
    return 1;
}
static int PatchName(const wchar_t* name, unsigned* index)
{
    if (wcslen(name) < 24) return 0;
    for (int i = 0; i < 16; i++) if (!((name[i] >= L'0' && name[i] <= L'9') || (name[i] >= L'a' && name[i] <= L'f'))) return 0;
    if (wcsncmp(name+16, L".patch_", 7)) return 0;
    unsigned n = 0;
    for (const wchar_t* s = name+23; *s; s++)
    {
        if (*s < L'0' || *s > L'9' || n > 100000) return 0;
        n = n*10 + (unsigned)(*s-L'0');
    }
    *index = n; return 1;
}
static int RecordRowInSet(
    const Asset* set,
    size_t setCount,
    Proof* setProofs,
    const ArchiveRow* row,
    const wchar_t* fileName,
    const wchar_t* fullPath,
    unsigned index)
{
    for (size_t i = 0; i < setCount; i++)
    {
        if (row->q[0] == set[i].name && row->q[1] == set[i].type)
        {
            Proof* p = &setProofs[i];
            const wchar_t* prior = wcsrchr(p->path, L'\\');
            if (p->found && (!prior || wcsncmp(prior+1, fileName, 16) || p->index == index))
                return AssetError("ambiguous archive provider", &set[i], fullPath);
            if (!p->found || index > p->index)
            {
                wcscpy(p->path, fullPath);
                p->row = *row;
                p->index = index;
                p->found = 1;
            }
        }
    }
    return 1;
}
static int ScanAssets(const wchar_t* directory)
{
    wchar_t pattern[1100]; WIN32_FIND_DATAW entry;
    static Proof empProofs[ASSET_COUNT];
    static Proof clnProofs[CLONE_ASSET_COUNT];
    memset(proofs, 0, sizeof proofs);
    memset(empProofs, 0, sizeof empProofs);
    memset(clnProofs, 0, sizeof clnProofs);
    assetError[0] = 0;
    if (swprintf(pattern, 1100, L"%ls\\*.patch_*", directory) < 0) return AssetError("staged directory path too long", NULL, directory);
    HANDLE scan = FindFirstFileW(pattern, &entry);
    if (scan == INVALID_HANDLE_VALUE) return AssetError("no readable staged patches", &assets[0], directory);
    int ok = 1;
    do
    {
        unsigned index;
        if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY || !PatchName(entry.cFileName, &index)) continue;
        wchar_t path[1024]; uint32_t head[3]; LARGE_INTEGER size;
        if (swprintf(path, 1024, L"%ls\\%ls", directory, entry.cFileName) < 0) { ok = AssetError("archive path too long", NULL, entry.cFileName); break; }
        HANDLE file = OpenRead(path);
        if (file == INVALID_HANDLE_VALUE || !ReadAt(file, 0, head, sizeof head) || head[0] != 0xF0000011u ||
            head[1] > 100000 || head[2] > 1000000 || !GetFileSizeEx(file, &size) ||
            72ull+32ull*head[1]+80ull*head[2] > (uint64_t)size.QuadPart)
        { if (file != INVALID_HANDLE_VALUE) CloseHandle(file); ok = AssetError("archive missing/unreadable or invalid directory", NULL, path); break; }
        for (uint32_t r = 0; r < head[2] && ok; r++)
        {
            ArchiveRow row;
            if (!ReadAt(file, 72ull+32ull*head[1]+80ull*r, &row, sizeof row)) { ok = AssetError("archive row unreadable", NULL, path); break; }
            if (!RecordRowInSet(assets, ASSET_COUNT, empProofs, &row, entry.cFileName, path, index)) { ok = 0; break; }
            if (!RecordRowInSet(cloneAssets, CLONE_ASSET_COUNT, clnProofs, &row, entry.cFileName, path, index)) { ok = 0; break; }
        }
        CloseHandle(file);
    } while (ok && FindNextFileW(scan, &entry));
    if (ok && GetLastError() != ERROR_NO_MORE_FILES) ok = AssetError("archive enumeration failed", NULL, directory);
    FindClose(scan);
    if (!ok) return 0;

    size_t empFound = 0, clnFound = 0;
    for (size_t i = 0; i < ASSET_COUNT; i++) if (empProofs[i].found) empFound++;
    for (size_t i = 0; i < CLONE_ASSET_COUNT; i++) if (clnProofs[i].found) clnFound++;

    if (empFound == ASSET_COUNT)
    {
        uint8_t tierMask = TIE_ALL_TIERS_MASK;
        int empOk = 1;
        for (size_t i = 0; i < ASSET_COUNT; i++)
            if (!Part(&empProofs[i], &assets[i], &tierMask)) { empOk = 0; break; }
        if (empOk)
        {
            memcpy(proofs, empProofs, sizeof empProofs);
            activeAssets = assets;
            activeAssetCount = ASSET_COUNT;
            g_activePack = TIE_PACK_EMPIRE;
            assetError[0] = 0;
            return 1;
        }
        if (clnFound < CLONE_ASSET_COUNT) return 0;
    }

    if (clnFound == CLONE_ASSET_COUNT)
    {
        uint8_t tierMask = TIE_ALL_TIERS_MASK;
        int clnOk = 1;
        for (size_t i = 0; i < CLONE_ASSET_COUNT; i++)
            if (!Part(&clnProofs[i], &cloneAssets[i], &tierMask)) { clnOk = 0; break; }
        if (clnOk)
        {
            memcpy(proofs, clnProofs, sizeof clnProofs);
            activeAssets = cloneAssets;
            activeAssetCount = CLONE_ASSET_COUNT;
            g_activePack = TIE_PACK_CLONES;
            assetError[0] = 0;
            return 1;
        }
        return 0;
    }

    if (clnFound > empFound)
    {
        for (size_t i = 0; i < CLONE_ASSET_COUNT; i++)
            if (!clnProofs[i].found) return AssetError("missing staged resource", &cloneAssets[i], NULL);
    }
    for (size_t i = 0; i < ASSET_COUNT; i++)
        if (!empProofs[i].found) return AssetError("missing staged resource", &assets[i], NULL);
    return 0;
}
