# Developer build

Players install the complete prebuilt ZIP through HellForge. This information is for source reproduction by developers.

Use the installed official HellForge Zig 0.17.0, target x86_64-windows-gnu, and SDK15 from commit4a5373b88b8d001b58927edc27aa1b998578fe9f. Compiler SHA256 f54b14f02a4d7d8ac62a94a0663486eb166bba3745d85b466238c1b93e723d46; external hellforge_mod.h SHA256 ce790f4bd6c820f127f39686d8c94ee0223452bb341783e3136f56f2dd9f73c1. HellForge core, SDK and compiler are external build inputs.

Build the eight source files with these arguments, substituting the external SDK and source paths:

```text
zig cc -target x86_64-windows-gnu -std=c11 -Wall -Wextra -Werror -Wno-unused-function -O2 -g0 -s -shared -I <SDK> -I src src/corpse_cleanup.c src/c6_spike.c src/c6_guard.c src/c6_sha256.c src/c6_mapping.c src/c6_client.c src/c6_trace.c src/mom_adapter.c -o corpse_cleanup.dll
```

Define HF_DISPATCHER_CLIENT_IMPLEMENTATION in corpse_cleanup.c only, before the first Windows header. The exact accepted SDK0.2.0 header is included at src/hf_dispatcher_client.h, SHA2567736a18b34a4f2a6f35e2d705aab51dcf7b4fc53fe561ff79d9514a6679e53ed. Its historical candidate comments are preserved byte-for-byte; final private SDK acceptance supersedes their old status wording. Do not change the common header or operate its embedded legacy adapters in this client.

build.ps1 is developer-only and takes an explicit HellForgeRoot. It compiles the client and does not install, test or launch. CMakeLists.txt retains the external-SDK alternative; leave HELLFORGE_INSTALL empty. The maintained reproduction uses the same existing owned global/local compiler cache additively, with both source locations recorded. No cold-cache or cross-toolchain byte-identity claim is made.

Keep manifest prebuilt=true, sdk=15 and game=25480438. A newly rebuilt binary needs its own checks and inventory; it does not inherit runtime evidence from another build. No guide compilation is required for this package.
