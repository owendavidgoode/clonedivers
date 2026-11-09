/* Read staged patches, never the install's mod selection: staging renumbers patches. No file writes. */
#pragma once
#include <bcrypt.h>
typedef struct ArchiveRow { uint64_t q[7]; uint32_t d[6]; } ArchiveRow;
_Static_assert(sizeof(ArchiveRow) == 80, "archive row");
typedef struct Proof
{
    wchar_t path[1024];
    ArchiveRow row;
    unsigned index;
    BY_HANDLE_FILE_INFORMATION mainInfo, gpuInfo;
    int found, valid;
} Proof;
static Proof proofs[4];
static NTSTATUS (WINAPI *openHash)(BCRYPT_ALG_HANDLE*, LPCWSTR, LPCWSTR, ULONG);
static NTSTATUS (WINAPI *hashBytes)(BCRYPT_ALG_HANDLE, PUCHAR, ULONG, PUCHAR, ULONG, PUCHAR, ULONG);
static BCRYPT_ALG_HANDLE sha;

static int Hash(const void* bytes, uint32_t size, const char* wanted)
{
    unsigned char digest[32];
    char hex[65];
    if (!sha || hashBytes(sha, NULL, 0, (PUCHAR)bytes, size, digest, sizeof digest) < 0) return 0;
    for (int i = 0; i < 32; i++) snprintf(hex + i * 2, 3, "%02x", digest[i]);
    return !strcmp(hex, wanted);
}

static int ReadAt(HANDLE file, uint64_t at, void* out, DWORD size)
{
    LARGE_INTEGER offset; offset.QuadPart = (LONGLONG)at;
    DWORD got = 0;
    return SetFilePointerEx(file, offset, NULL, FILE_BEGIN) && ReadFile(file, out, size, &got, NULL) && got == size;
}

static HANDLE OpenRead(const wchar_t* path)
{
    return CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
}

static int SameInfo(const BY_HANDLE_FILE_INFORMATION* a, const BY_HANDLE_FILE_INFORMATION* b)
{
    return a->dwVolumeSerialNumber == b->dwVolumeSerialNumber && a->nFileIndexHigh == b->nFileIndexHigh &&
        a->nFileIndexLow == b->nFileIndexLow && a->nFileSizeHigh == b->nFileSizeHigh && a->nFileSizeLow == b->nFileSizeLow &&
        !memcmp(&a->ftLastWriteTime, &b->ftLastWriteTime, sizeof(FILETIME));
}

static int Part(Proof* p, int gpu, const char* expected)
{
    wchar_t path[1100];
    if (swprintf(path, 1100, L"%ls%ls", p->path, gpu ? L".gpu_resources" : L"") < 0) return 0;
    HANDLE file = OpenRead(path);
    if (file == INVALID_HANDLE_VALUE) return 0;
    BY_HANDLE_FILE_INFORMATION before, after;
    LARGE_INTEGER length;
    uint32_t size = p->row.d[gpu ? 2 : 0];
    uint64_t offset = p->row.q[gpu ? 4 : 2];
    int ok = GetFileInformationByHandle(file, &before) && GetFileSizeEx(file, &length) && size > 0 && size <= 64u * 1024 * 1024 &&
        offset <= (uint64_t)length.QuadPart && size <= (uint64_t)length.QuadPart - offset;
    void* bytes = ok ? malloc(size) : NULL;
    ok = bytes && ReadAt(file, offset, bytes, size) && Hash(bytes, size, expected) &&
        GetFileInformationByHandle(file, &after) && SameInfo(&before, &after);
    free(bytes);
    CloseHandle(file);
    if (ok) { if (gpu) p->gpuInfo = after; else p->mainInfo = after; }
    return ok;
}

static int Unchanged(const Proof* p)
{
    for (int gpu = 0; gpu < 2; gpu++)
    {
        wchar_t path[1100]; BY_HANDLE_FILE_INFORMATION info;
        if (swprintf(path, 1100, L"%ls%ls", p->path, gpu ? L".gpu_resources" : L"") < 0) return 0;
        HANDLE file = OpenRead(path);
        int ok = file != INVALID_HANDLE_VALUE && GetFileInformationByHandle(file, &info) && SameInfo(&info, gpu ? &p->gpuInfo : &p->mainInfo);
        if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
        if (!ok) return 0;
    }
    return 1;
}

static int PatchName(const wchar_t* name, unsigned* index)
{
    if (wcslen(name) < 24) return 0;
    for (int i = 0; i < 16; i++) if (!((name[i] >= L'0' && name[i] <= L'9') || (name[i] >= L'a' && name[i] <= L'f'))) return 0;
    if (wcsncmp(name + 16, L".patch_", 7)) return 0;
    unsigned n = 0;
    for (const wchar_t* s = name + 23; *s; s++)
    {
        if (*s < L'0' || *s > L'9' || n > 100000) return 0;
        n = n * 10 + (unsigned)(*s - L'0');
    }
    *index = n;
    return 1;
}

static int ScanVisuals(const wchar_t* directory)
{
    wchar_t pattern[1100];
    WIN32_FIND_DATAW entry;
    memset(proofs, 0, sizeof proofs);
    if (swprintf(pattern, 1100, L"%ls\\*.patch_*", directory) < 0) return 0;
    HANDLE scan = FindFirstFileW(pattern, &entry);
    if (scan == INVALID_HANDLE_VALUE) return 0;
    int ok = 1;
    do
    {
        unsigned index;
        if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY || !PatchName(entry.cFileName, &index)) continue;
        wchar_t path[1024];
        if (swprintf(path, 1024, L"%ls\\%ls", directory, entry.cFileName) < 0) { ok = 0; break; }
        HANDLE file = OpenRead(path);
        uint32_t head[3];
        LARGE_INTEGER size;
        if (file == INVALID_HANDLE_VALUE || !ReadAt(file, 0, head, sizeof head) || head[0] != 0xF0000011u ||
            head[1] > 100000 || head[2] > 1000000 || !GetFileSizeEx(file, &size) ||
            72ull + 32ull * head[1] + 80ull * head[2] > (uint64_t)size.QuadPart)
        { if (file != INVALID_HANDLE_VALUE) CloseHandle(file); ok = 0; break; }
        for (uint32_t i = 0; i < head[2] && ok; i++)
        {
            ArchiveRow row;
            if (!ReadAt(file, 72ull + 32ull * head[1] + 80ull * i, &row, sizeof row)) { ok = 0; break; }
            if (row.q[1] != 0xE0A48D0BE9A7453Full) continue;
            for (int v = 0; v < 4; v++) if (row.q[0] == visuals[v].unit)
            {
                Proof* p = &proofs[v];
                const wchar_t* prior = wcsrchr(p->path, L'\\');
                /* Across families there is no reviewed load-order rule: refuse ambiguity. */
                if (p->found && (!prior || wcsncmp(prior + 1, entry.cFileName, 16) || p->index == index)) { ok = 0; break; }
                if (!p->found || index > p->index)
                { wcscpy(p->path, path); p->row = row; p->index = index; p->found = 1; }
            }
        }
        CloseHandle(file);
    } while (ok && FindNextFileW(scan, &entry));
    if (ok && GetLastError() != ERROR_NO_MORE_FILES) ok = 0;
    FindClose(scan);
    int qualified = 0;
    if (ok) for (int v = 0; v < 4; v++)
    {
        Proof* p = &proofs[v];
        p->valid = p->found && p->row.d[1] == 0 && Part(p, 0, visuals[v].mainHash) && Part(p, 1, visuals[v].gpuHash);
        qualified += p->valid;
    }
    if (!ok) memset(proofs, 0, sizeof proofs);
    return qualified;
}
