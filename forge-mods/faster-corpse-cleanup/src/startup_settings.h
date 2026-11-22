/* Read-only startup fallbacks. Megapack 1.1.2 owns menu persistence through
 * its public register/get/set/on_change API and sandbox-aware AppData. */
#ifndef CC_STARTUP_SETTINGS_H
#define CC_STARTUP_SETTINGS_H
static FILE *cc_file(const char *path)
{
    wchar_t wide[4096];
    if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,wide,4096))return NULL;
    return _wfopen(wide,L"rb");
}
static char *cc_trim(char *s)
{
    while(isspace((unsigned char)*s))++s;
    size_t n=strlen(s);while(n&&isspace((unsigned char)s[n-1]))s[--n]=0;
    return s;
}
/* 1: valid, 0: missing/unreadable, -1: invalid. Never partially changes out. */
static int cc_read_config(const char *path,CcConfig *out)
{
    FILE *f=cc_file(path);if(!f)return 0;
    char line[512];int valid=1,section=0;CcConfig next={1,5,1};
    while(fgets(line,sizeof line,f))
    {
        if(!strchr(line,'\n')&&!feof(f)){valid=0;break;}
        char *s=cc_trim(line),*comment=strpbrk(s,";#");if(comment)*comment=0;s=cc_trim(s);
        if(!*s)continue;
        if(!strcmp(s,"[Cleanup]")){section=1;continue;}
        char *equal=strchr(s,'=');if(!section||!equal){valid=0;break;}*equal=0;
        char *value=cc_trim(equal+1),*end;double v=strtod(value,&end);
        if(end==value||*cc_trim(end)||!isfinite(v)){valid=0;break;}
        char *key=cc_trim(s);
        if(!strcmp(key,"Enabled")&&(v==0||v==1))next.enabled=(int)v;
        else if(!strcmp(key,"DelaySeconds")&&v>=0&&v<=120&&floor(v)==v)next.seconds=(int)v;
        else if(!strcmp(key,"ExtraCleanup")&&(v==0||v==1))next.extra_cleanup=(int)v;
        else {valid=0;break;}
    }
    if(ferror(f))valid=0;fclose(f);
    if(!valid)return -1;*out=next;return 1;
}
static int cc_legacy_path(const char *folder,char path[4096])
{
    size_t root=SIZE_MAX;
    for(size_t i=0;folder[i];)
    {
        size_t start=i;while(folder[i]&&folder[i]!='/'&&folder[i]!='\\')++i;
        if(i-start==4&&tolower((unsigned char)folder[start])=='m'&&tolower((unsigned char)folder[start+1])=='o'&&
           tolower((unsigned char)folder[start+2])=='d'&&tolower((unsigned char)folder[start+3])=='s')root=start?start-1:0;
        if(folder[i])++i;
    }
    if(root==SIZE_MAX||root>3500)return 0;
    int n=snprintf(path,4096,"%.*s/userdata/faster-corpse-cleanup/config.ini",(int)root,folder);
    return n>=0&&n<4096;
}
static int cc_load(void)
{
    config=(CcConfig){1,5,1};const char *folder=hf->mod_folder(self);
    if(!folder||!*folder)return 1;
    char path[4096];int n=snprintf(path,sizeof path,"%s/config.ini",folder);
    if(n>=0&&n<(int)sizeof path&&cc_read_config(path,&config)<0)
        hf->log(self,"Invalid bundled startup config ignored; using built-in defaults");
    if(cc_legacy_path(folder,path)&&cc_read_config(path,&config)<0)
        hf->log(self,"Invalid legacy config ignored; menu values take precedence");
    cc_load_host_options_initial(folder);
    return 1;
}
#endif
