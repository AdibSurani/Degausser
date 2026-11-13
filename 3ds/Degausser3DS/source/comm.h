/*
 * comm.h
 *
 *  Created on: Jan 4, 2017
 *      Author: wangyu
 */
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <sys/stat.h>
#define MINIZ_HEADER_FILE_ONLY
#include "miniz.c"




#ifndef DEGAUSSER3DS_SOURCE_COMM_H_
#define DEGAUSSER3DS_SOURCE_COMM_H_




//#define TEST




#ifdef TEST
#include <stdint.h>
typedef uint8_t u8;   ///<  8-bit unsigned integer
typedef uint16_t u16; ///< 16-bit unsigned integer
typedef uint32_t u32; ///< 32-bit unsigned integer
typedef uint64_t u64; ///< 64-bit unsigned integer

typedef int8_t s8;   ///<  8-bit signed integer
typedef int16_t s16; ///< 16-bit signed integer
typedef int32_t s32; ///< 32-bit signed integer
typedef int64_t s64; ///< 64-bit signed integer

typedef s32 Result;

#else


#include <3ds.h>
#define GLYPH_HEADER_FILE_ONLY
#include "glyph.cpp"

#endif



extern int g_show_log;

void debugprintf(const char* fmt, ...);

void print(char* str);
void print(u16* str);


typedef struct
{
	u32 ID, ID2, Flags;
	s16 Singer, Icon;
	u16 Title[51], Title2[51], Author[20];
	u8  Scores[50];
} _JbMgrItem;

struct JbMgr
{
	u32 Magic;
	u16 Version, Count;
	_JbMgrItem Items[3700];
};

typedef struct
{
	u32 Version;
	struct
	{
		u32 Used, UncompLen, Offset, CompLen;
	} Parts[4];
} PackHeader;

static const u8 gzip_header[10] = { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 };





// concatenate UTF16 strings
void ConcatUTF16(u16* dst, bool sanitizeFirst, ...);


Result gz_compress(u8* dst, u32* dstLen, const u8* src, u32 srcLen);


Result gz_decompress(u8* dst, u32 dstLen, const u8* src, u32 srcLen);


Result gz_decompress2(u8* dst, u32* dstLen, const u8* src, u32 srcLen);

#endif /* DEGAUSSER3DS_SOURCE_COMM_H_ */
