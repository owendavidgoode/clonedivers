# Heavy/Squad author review brief — not sent

The launcher review packages contain no third-party mod archives or modified
author source. Fresh-player onboarding still depends on a permitted way to obtain
the exact compatible Heavy/Squad pair. No redistribution permission has been
received. This brief prepares a concrete request; no author has been contacted.

The public author pages were checked November4. [Heavy SEAF Presence Everywhere](https://hf-mod.com/Mods/Details/82)
still lists NontoxicKappa's1.2 download, updated November1, SDK13, and all rights
reserved. [SEAF Squad Stratagems](https://hf-mod.com/Mods/Details/59) still lists
JohnnyGrease's1.0 portal release (manifest0.1.4), updated October25, SDK9, and all
rights reserved. Those are the previously tested originals, not newly released
compatible replacements. Saved page receipts are private in the readiness audit.

| Pair member | Author original SHA256 | Local private trial version/SHA256 |
| --- | --- | --- |
| Heavy | a5a2e97c49a46183b3dcf5a18cdf3df26e9b4a9af69a64f7ebd0dccda78d5def | 1.2.0-clonedivers.3 / a25573dfb15b2f5c2e5df73fa49c193413b9cfe03347f831045ed0da34c38734 |
| Squad | 399189f6f0a3c86fb16f484ab221b839115c74544d3e573a9d1832351a784413 | 0.1.4-clonedivers.2 / d8eb2eae1b7c97bb3b6d4fb926cbe711ac294a0e74838ca21bf44f955a5fadfb |

The target stays HellForge0.3.0-beta/engine0.1.41/game25480438 with SDK15 installed.
Heavy's pool initialization moved to GAME_LOADED; its misleading nonempty-catalog
warning was corrected without removing the catalog guard; same-frame call keys
include the thrower so different players are not deduplicated together. Population
stays conservative at one squad/120s with additional squads/support/tank hunters0.

The Squad trial retains four call-ins, icons, categories and credits. With native
Heavy loaded it delegates delivery to Heavy; otherwise it retains standalone
callbacks. The source-first build uses C++ wrappers with C linkage for the mixed
C/C++ author sources and one explicit const cast; the old prebuilt DLL is omitted.
The two mods remain separately required. No engine binary was changed.

Evidence includes successful initialization and an owner D11 mission with working
call-ins followed by death. Extraction, frame-time/load limits, simultaneous
multi-player throws and intended-size separate-PC multiplayer remain unqualified.
Credits and original sources are retained privately. No author archive, private
adaptation, account capture, key or profile is in a launcher review archive.

Draft request for the owner to review before any contact:

> We are testing a private Clonedivers compatibility preset using your Heavy SEAF
> and SEAF Squad Stratagems mods on the pinned HellForge build above. We found the
> initialization/delivery/build issues summarized here and have local compatibility
> changes with the exact versions/hashes listed. Would you prefer to review these
> changes for an author-maintained compatible release, or explicitly permit a
> credited compatibility patch or modified archive for our tester cohort? Please
> specify the permitted artifact form, distribution scope and required notices.
> We will keep the private adaptations out of distribution unless you approve an
> appropriate route. We can supply the private diff for review if requested.

A compatible author release or explicit permission for the required adaptation
route would resolve this part of onboarding. It would not certify multiplayer or
clear the other runtime gates. Each player still needs their own owned HD2 and
verified one-way account import. Mod sync sharing stays OFF for the current private
pair; friendship or a matching checksum is not a permission grant.
