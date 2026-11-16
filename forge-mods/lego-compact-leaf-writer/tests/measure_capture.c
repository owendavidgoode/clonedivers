#define main inherited_fixture_main
#include "D:/ClaudeWork/empire-forge/lego-render-stable-observer-astra/tests/attachment_test.c"
#undef main
int main(void) {
 api.version=16;api.size=sizeof api;api.build_id=Build;api.mod_id=Id;api.load_phase=Phase;
 api.read=ReadMock;api.read_code=CodeMock;api.module_base=Module;api.on=On;api.on_game_thread=OnTick;api.logs_folder=Folder;api.log=Log;mod.api=&api;
 Setup(8,0);SlowOwner();Record record;RenderMeta meta;Owner owner;uint64_t matches;
 readTick=byteTick=readTotal=byteTotal=0;
 CHECK(Capture(1,NULL,&record,&meta,NULL,&matches,&owner));
 printf("no-group Capture: %u reads / %u bytes; child nodes %u parent nodes %u meshes %u\n",readTick,byteTick,record.child.nodes,record.parent.nodes,meta.meshes);
 return 0;
}
