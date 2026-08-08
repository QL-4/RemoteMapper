#pragma once

#include <stddef.h>

typedef unsigned char UCHAR;
typedef UCHAR *PUCHAR;
typedef size_t SIZE_T;
typedef unsigned char BOOLEAN;

#define TRUE ((BOOLEAN)1)
#define FALSE ((BOOLEAN)0)
#ifndef _Inout_updates_bytes_
#define _Inout_updates_bytes_(Length)
#endif
#ifndef _In_
#define _In_
#endif
