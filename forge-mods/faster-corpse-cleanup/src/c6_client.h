/* Client-private owned-settings adapter. NOT a provider ABI. MIT. */
#ifndef C6_CLIENT_H
#define C6_CLIENT_H
#include "c6_spike.h"
#define C6_CLIENT_ROWS 3u
/* All four callbacks are this client's own wrapper functions. No callback or
 * context in this struct is passed to, or retained by, the foreign provider. */
typedef struct C6ClientCalls {
 void *context;
 int (*ready)(void *);
 int (*register_row)(void *,const char *,const C6SpecHypothesis *);
 int (*get)(void *,const char *,double *);
 int (*set)(void *,const char *,double);
} C6ClientCalls;
typedef struct C6ClientRow {C6OwnedOption owned;int registered;} C6ClientRow;
typedef struct C6Client {C6ClientCalls calls;C6ClientRow rows[C6_CLIENT_ROWS];unsigned count;} C6Client;
int c6_client_register(C6Client *,const CrMenuOption *);
int c6_client_get(C6Client *,const char *,double *);
int c6_client_set(C6Client *,const char *,double);
#endif
