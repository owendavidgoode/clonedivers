/* Authored SHA-256, FIPS 180-4; no external runtime dependency. MIT. */
#ifndef C6_SHA256_H
#define C6_SHA256_H
#include <stddef.h>
#include <stdint.h>
typedef struct C6Sha256 {uint32_t h[8];uint64_t bytes;unsigned used;uint8_t block[64];} C6Sha256;
void c6_sha_init(C6Sha256 *s);
void c6_sha_update(C6Sha256 *s,const void *data,size_t n);
void c6_sha_final(C6Sha256 *s,uint8_t out[32]);
#endif
