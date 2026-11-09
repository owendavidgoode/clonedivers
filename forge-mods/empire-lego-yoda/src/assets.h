/* r45-v3 torso payload pins, expected-resources.json; original kit bindings from the r23 native-kit audit. */
#pragma once
typedef struct Visual
{
    uint32_t kit, body;
    uint64_t unit;
    const char* mainHash;
    const char* gpuHash;
} Visual;
static const Visual visuals[] = {
    {0xB513FD54u, 0, 0xBB11C8041C14C747ull, "704ded9d44af5ee3fd28bd50ecb3f98ad1744c09ddb397874a42e4830bfec038", "21bc3b68fed040bd75fdf01368f5088b5ece1cae6cc7a62620c0efe8858f116b"},
    {0xB513FD54u, 1, 0x003CC8A527711DA5ull, "d8e1070c153a2357a80d07b90d10c3b7042e08e9a16491c1d6a3acde2571cb19", "6cb7e860ba2dcf714b890d47d49d54511b0d37962fc60d0da59cd689a05b9984"},
    {0xE9ADD047u, 0, 0xC1141B792C1B621Aull, "eff78f1e43a002a5d9fae8ed22067bbbfb2a94195c79e4de497e231e45b7f227", "ab93205dd5a2b301e378b37953658ee975b4ab4bf0f6977fd916778cd8c9750c"},
    {0xE9ADD047u, 1, 0x09AD5784B873E58Full, "52f5781e8a81cd69675fddef685e0df1b7574e4be5e80cabe96968424031d9a1", "36ddc288a9afcf64bcee0cd7bae7a96119f01baf0fb1889838fe1fc56bde7362"}
};

#define YODA_WAV_SHA256 "f5f49f73e435113295fef77add8c4941b2b6fd7f0c1958ba1a8b3a9d038f4cef"
