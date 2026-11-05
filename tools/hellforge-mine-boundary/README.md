# Mine-marker bounds fix — source preview

This original MIT-licensed source-first mod targets game25480438 only, through
HellForge's official SDK. It is part of the owner's tested local setup, not a
multiplayer-qualified downloadable mod bundle.
It checks the complete original append block before installing an index guard.
It preserves the512 valid mine-placement transforms and omits excess markers.

The machine-code fixture executes the exact production stub in owned memory.
The wrapper fixture uses the real SDK wrapper with explicit API success contracts,
a fake module and an SDK writer confined to the fixture's14-byte patch site.

```powershell
& D:/HellForge/tools/zig/zig.exe c++ -std=c++17 -O2 -Wall -Wextra -Werror ./tests/native_tests.cpp -o D:/CodexScratch/hellforge/mine-boundary-native-tests.exe
& D:/CodexScratch/hellforge/mine-boundary-native-tests.exe
& D:/HellForge/tools/zig/zig.exe c++ -std=c++17 -O2 -Wall -Wextra -Werror -I D:/HellForge/app/sdk ./tests/sdk_tests.cpp -o D:/CodexScratch/hellforge/mine-boundary-sdk-tests.exe
& D:/CodexScratch/hellforge/mine-boundary-sdk-tests.exe
```

Run those commands from this folder, substituting your SDK/compiler and scratch
output paths. Import/build and switch mods only while idle through the official
CLI. No compiled DLL or owner archive is included here.
`MINE_BOUNDARY_READY` confirms byte-signature validation,
installation and readback; it does not certify a mission or multiplayer. The
mission-boundary log reports the cumulative number of skipped excess markers.
