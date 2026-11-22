/* SDK inventory -> exact read-only file -> already-loaded referenced mapping.
 * Never calls LoadLibrary, a provider export, or any provider table slot. MIT. */
#include "c6_mapping.h"
#include "c6_guard.h"
#include "c6_sha256.h"
#include "c6_target.h"
#include <windows.h>
#include <string.h>
static int mapping_read(void *context,uintptr_t at,void *out,size_t n)
{const HfApi *api=context;return api->read(at,out,n);}
void c6_mapping_close(C6Mapping *m)
{
    if(!m)return;
    if(m->module){FreeLibrary((HMODULE)m->module);m->module=NULL;}
    m->base=0;m->closed=1;
}
int c6_mapping_acquire(C6Mapping *m,const HfApi *api,HfMod *self)
{
    static const uint8_t pin[32]=C6_TARGET_DLL_SHA_BYTES;
    if(!m||m->closed)return 0;
    if(m->module)return 1;
    if(m->attempted)return 0;
    m->attempted=1;
    if(!api||api->version!=15||api->size<offsetof(HfApi,stats_source)+sizeof api->stats_source||!api->mods||!api->read)return 0;
    char folder[MAX_PATH]={0};unsigned found=0,ended=0;
    for(uint32_t i=0;i<4096;i++){
        HfModInfo info={.size=sizeof info};
        if(!api->mods(self,i,&info)){ended=1;break;}
        if(!info.id||strcmp(info.id,"cowboybingus.vanilla-plus-megapack"))continue;
        if(found++||info.kind!=HF_MOD_CODE||!info.loaded||!info.folder)return 0;
        size_t n=0;while(n<sizeof folder&&info.folder[n])n++;
        if(!n||n>=sizeof folder)return 0;
        memcpy(folder,info.folder,n+1);
    }
    if(!ended||found!=1)return 0;
    wchar_t wide[MAX_PATH],path[MAX_PATH],loaded[MAX_PATH],canonical[MAX_PATH];
    if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,folder,-1,wide,MAX_PATH))return 0;
    for(wchar_t *p=wide;*p;p++)if(*p==L'/')*p=L'\\';
    size_t n=wcslen(wide);static const wchar_t name[]=L"vanilla_plus_megapack.dll";
    if(n+1+sizeof name/sizeof *name>MAX_PATH)return 0;
    if(wide[n-1]!=L'\\')wide[n++]=L'\\';
    memcpy(wide+n,name,sizeof name);
    DWORD length=GetFullPathNameW(wide,MAX_PATH,path,NULL);
    if(!length||length>=MAX_PATH)return 0;
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);
    if(file==INVALID_HANDLE_VALUE)return 0;
    HMODULE module=NULL;LARGE_INTEGER size;int ok=0;
    if(!GetFileSizeEx(file,&size)||size.QuadPart!=C6_TARGET_FILE_BYTES)goto done;
    C6Sha256 sha;c6_sha_init(&sha);uint8_t bytes[4096],digest[32];uint32_t total=0;
    while(total<C6_TARGET_FILE_BYTES){DWORD got=0,want=C6_TARGET_FILE_BYTES-total;if(want>sizeof bytes)want=sizeof bytes;
        if(!ReadFile(file,bytes,want,&got,NULL)||!got||got>want)goto done;
        c6_sha_update(&sha,bytes,got);total+=got;}
    c6_sha_final(&sha,digest);if(memcmp(digest,pin,32))goto done;
    /* Flags 0 increments a normal reference. It neither loads an absent DLL
     * nor pins it forever. The file denies write/delete sharing during checks. */
    if(!GetModuleHandleExW(0,path,&module))goto done;
    length=GetModuleFileNameW(module,loaded,MAX_PATH);
    if(!length||length>=MAX_PATH)goto done;
    length=GetFullPathNameW(loaded,MAX_PATH,canonical,NULL);
    if(!length||length>=MAX_PATH||_wcsicmp(path,canonical))goto done;
    if(!c6_image_probe(mapping_read,(void *)api,(uintptr_t)module,C6_TARGET_IMAGE_SIZE))goto done;
    m->module=module;m->base=(uintptr_t)module;module=NULL;ok=1;
done:
    if(module)FreeLibrary(module);
    CloseHandle(file);return ok;
}
