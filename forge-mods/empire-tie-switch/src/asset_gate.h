/* Effective staged-patch reader, adapted from empire-lego-yoda's visual_gate.h. */
#pragma once
#include <bcrypt.h>
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
static Proof proofs[ASSET_COUNT];
static char assetError[2400];
static int AssetError(const char* why, const Asset* a, const wchar_t* path)
{
    if (a) snprintf(assetError, sizeof assetError,
        "%s resource=%016llx type=%016llx part=%u expected_sha256=%s file=%ls",
        why, (unsigned long long)a->name, (unsigned long long)a->type, a->part, a->hash, path ? path : L"(no provider)");
    else snprintf(assetError, sizeof assetError, "%s file=%ls", why, path ? path : L"(unknown)");
    return 0;
}
static NTSTATUS (WINAPI *openHash)(BCRYPT_ALG_HANDLE*, LPCWSTR, LPCWSTR, ULONG);
static NTSTATUS (WINAPI *hashBytes)(BCRYPT_ALG_HANDLE, PUCHAR, ULONG, PUCHAR, ULONG, PUCHAR, ULONG);
static BCRYPT_ALG_HANDLE sha;

static int Hash(const void* bytes, uint32_t size, const char* wanted)
{
    unsigned char digest[32]; char hex[65];
    if (!sha || hashBytes(sha, NULL, 0, (PUCHAR)bytes, size, digest, sizeof digest) < 0) return 0;
    for (int i = 0; i < 32; i++) snprintf(hex + i*2, 3, "%02x", digest[i]);
    return !strcmp(hex, wanted);
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
static int Part(Proof* p, const Asset* a)
{
    const wchar_t* suffixes[] = {L"", L".stream", L".gpu_resources"}; wchar_t path[1100];
    if (swprintf(path, 1100, L"%ls%ls", p->path, suffixes[a->part]) < 0) return AssetError("payload path too long", a, p->path);
    HANDLE file = OpenRead(path);
    if (file == INVALID_HANDLE_VALUE) return AssetError("payload file missing or unreadable", a, path);
    BY_HANDLE_FILE_INFORMATION before, after; LARGE_INTEGER length;
    uint32_t size = p->row.d[a->part]; uint64_t offset = p->row.q[2+a->part];
    int ok = GetFileInformationByHandle(file, &before) && GetFileSizeEx(file, &length) && size > 0 && size <= 128u*1024*1024 &&
        offset <= (uint64_t)length.QuadPart && size <= (uint64_t)length.QuadPart-offset;
    void* bytes = ok ? malloc(size) : NULL;
    ok = bytes && ReadAt(file, offset, bytes, size) && Hash(bytes, size, a->hash) &&
        GetFileInformationByHandle(file, &after) && SameInfo(&before, &after);
    free(bytes); CloseHandle(file);
    if (ok) p->info = after;
    else AssetError("payload bounds/read/hash mismatch or file changed", a, path);
    return ok;
}
static int AssetsUnchanged(void)
{
    const wchar_t* suffixes[] = {L"", L".stream", L".gpu_resources"};
    for (size_t i = 0; i < ASSET_COUNT; i++)
    {
        wchar_t path[1100]; BY_HANDLE_FILE_INFORMATION info;
        if (!proofs[i].found) return AssetError("missing staged proof; restart required", &assets[i], NULL);
        if (swprintf(path, 1100, L"%ls%ls", proofs[i].path, suffixes[assets[i].part]) < 0)
            return AssetError("staged payload path too long", &assets[i], proofs[i].path);
        HANDLE file = OpenRead(path);
        int ok = file != INVALID_HANDLE_VALUE && GetFileInformationByHandle(file, &info) && SameInfo(&info, &proofs[i].info);
        if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
        if (!ok) return AssetError("staged payload file missing or changed; restart required", &assets[i], path);
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
static int ScanAssets(const wchar_t* directory)
{
    wchar_t pattern[1100]; WIN32_FIND_DATAW entry;
    memset(proofs, 0, sizeof proofs); assetError[0] = 0;
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
            for (size_t i = 0; i < ASSET_COUNT; i++) if (row.q[0] == assets[i].name && row.q[1] == assets[i].type)
            {
                Proof* p = &proofs[i]; const wchar_t* prior = wcsrchr(p->path, L'\\');
                if (p->found && (!prior || wcsncmp(prior+1, entry.cFileName, 16) || p->index == index))
                { ok = AssetError("ambiguous archive provider", &assets[i], path); break; }
                if (!p->found || index > p->index) { wcscpy(p->path, path); p->row = row; p->index = index; p->found = 1; }
            }
        }
        CloseHandle(file);
    } while (ok && FindNextFileW(scan, &entry));
    if (ok && GetLastError() != ERROR_NO_MORE_FILES) ok = AssetError("archive enumeration failed", NULL, directory);
    FindClose(scan);
    if (ok) for (size_t i = 0; i < ASSET_COUNT; i++)
    {
        if (!proofs[i].found) { ok = AssetError("missing staged resource", &assets[i], NULL); break; }
        if (!Part(&proofs[i], &assets[i])) { ok = 0; break; }
    }
    return ok;
}
