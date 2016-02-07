#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <sys/stat.h>
#include <3ds.h>
#define MINIZ_HEADER_FILE_ONLY
#include "miniz.c"
#define GLYPH_HEADER_FILE_ONLY
#include "glyph.c"

#define TRY(item, str) if (item) { myprintf(str "\n"); return -1; }
#define TRYCONT(item, str) if (item) { myprintf(str "\n"); continue; }

typedef struct
{
	u32 ID, ID2, Flags;
	s16 Singer, Icon;
	u16 Title[51], Title2[51], Author[20];
	u8  Scores[50];
} _JbMgrItem;

struct
{
	u32 Magic;
	u16 Version, Count;
	_JbMgrItem Items[3700];
} jbMgr;

typedef struct
{
	u32 Version;
	struct
	{
		u32 Used, UncompLen, Offset, CompLen;
	} Parts[4];
} PackHeader;

static const u8 gzip_header[10] = { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 };
u8 buffer[524288];

// SANITY CHECKS
typedef char test_item[sizeof(_JbMgrItem) == 312 ? 1 : -1];
typedef char test_jbmgr[sizeof(jbMgr) == 1154408 ? 1 : -1];
typedef char test_jbmgr[sizeof(PackHeader) == 68 ? 1 : -1];

// archive-related stuff
u32 extdata_archive_lowpathdata[3] = {MEDIATYPE_SD, 0xa0b, 0};
FS_Archive extdata_archive = {ARCHIVE_EXTDATA, {PATH_BINARY, 0xC, &extdata_archive_lowpathdata}};
FS_Archive sdmc_archive = {ARCHIVE_SDMC, {PATH_ASCII, 1, ""}};

// custom version of printf that calls print()
void myprintf(char* fmt, ...)
{
	char buffer[256];
	va_list args;
	va_start(args,fmt);
	vsprintf(buffer, fmt, args);
	va_end(args);
	print(buffer);
}

// concatenate UTF16 strings
void ConcatUTF16(u16* dst, bool sanitizeFirst, ...)
{
	va_list vl;
	va_start(vl, sanitizeFirst);
	for (u16* src; (src = va_arg(vl, u16*)); sanitizeFirst = true)
	{
		for (u16 c; (c = *src); src++)
		{
			if (sanitizeFirst && (c == '\\' || c == '/' || c == '?' || c == '*' ||
				c == ':' || c == '|' || c == '"' || c == '<' || c == '>'))
			{
				c += 0xFEE0;
			}
			*dst++ = c;
		}
	}
	va_end(vl);
	*dst = 0;
}

Result gz_compress(void* dst, u32* dstLen, const void* src, u32 srcLen)
{
	memcpy(dst, gzip_header, 10);
	if (!(*dstLen = tdefl_compress_mem_to_mem(dst + 10, *dstLen - 18, src, srcLen, 0x300))) return -1; // ERROR COMPRESSING
	*dstLen += 18;
	*(u32*)(dst + *dstLen - 8) = mz_crc32(0, src, srcLen);
	*(u32*)(dst + *dstLen - 4) = srcLen;
	return 0;
}

Result gz_decompress(void* dst, u32 dstLen, const void* src, u32 srcLen)
{
	if (memcmp(src, gzip_header, 10)) return -1; // GZIP HEADER ERROR
	if (dstLen != *(u32*)(src + srcLen - 4)) return -2; // UNEXPECTED LENGTH
	if (dstLen != tinfl_decompress_mem_to_mem(dst, *(u32*)(src + srcLen - 4), src + 10, srcLen - 18, 4)) return -3; // DECOMPRESS FAILED
	if (*(u32*)(src + srcLen - 8) != mz_crc32(0, dst, dstLen)) return -4; // WRONG CRC32
	return 0;
}

Result ReadJbMgr()
{
	Handle handle;
	u64 filesize;
	u32 size2;
	
	TRY(FSUSER_OpenFile(&handle, extdata_archive, fsMakePath(PATH_ASCII, "/jb/mgr.bin"), FS_OPEN_READ, 0), "Unable to open /jb/mgr.bin for reading");
	TRY(FSFILE_GetSize(handle, &filesize), "Unable to obtain jbMgr filesize");
	TRY(filesize != 1155072, "Unexpected jbMgr filesize");
	TRY(FSFILE_Read(handle, NULL, filesize - 4, &size2, 4), "Unable to read uncompressed jbMgr filesize");
	TRY(size2 > sizeof(buffer), "Uncompressed jbMgr is too big for buffer!");
	TRY(FSFILE_Read(handle, NULL, 0, &buffer, size2), "Unable to read jbMgr");
	FSFILE_Close(handle);

	TRY(gz_decompress(&jbMgr, sizeof(jbMgr), buffer, size2), "Unable to decompress jbMgr");
	
	myprintf("Successfully loaded extdata://00000a0b/jb/mgr.bin!\n");
	return 0;
}

Result WriteJbMgr()
{
	u32 compLen = sizeof(buffer);
	TRY(gz_compress(buffer, &compLen, &jbMgr, sizeof(jbMgr)), "Unable to compress jbMgr");
	
	char* paths[2] = {"/jb/mgr.bin", "/jb/mgr_.bin"};
	for (int i = 0; i < 2; i++)
	{
		Handle handle;
		TRY(FSUSER_OpenFile(&handle, extdata_archive, fsMakePath(PATH_ASCII, paths[i]), FS_OPEN_WRITE, 0), "Unable to write to jbMgr");
		FSFILE_Write(handle, NULL, 0, buffer, compLen, FS_WRITE_FLUSH);
		FSFILE_Write(handle, NULL, 1155068, &compLen, 4, FS_WRITE_FLUSH);
		FSFILE_Close(handle);
	}
	
	// NOT YET IMPLEMENTED
	return 0;
}

int FindSongID(u32 id)
{
	// inefficient code to find the first slot with ID = id
	for (int i = 0; i < 3700; i++)
	{
		if (jbMgr.Items[i].ID == id) return i;
	}
	return -1;
}

/*
// Find the earliest unused customID (0x8000????) in jbMgr
u32 GetEarliestCustomID()
{
	// very inefficient code ahead
	bool used[3700] = {};
	for (int i = 0; i < 3700; i++)
	{
		u32 id = jbMgr.Items[i].ID;
		if ((id >> 16) == 0x8000) used[id & 0xFFFF] = true;
	}
	for (int i = 0; i < 3700; i++)
	{
		if (!used[i]) return 0x80000000 | i;
	}
	return -1;
}
*/

Result DumpAllPacks()
{
	int exportCount = 0;
	
	// create bbpdump folder
	FSUSER_CreateDirectory(sdmc_archive, fsMakePath(PATH_ASCII, "/bbpdump"), 0);
	
	// some operations on jbMgr
	for (int i = 0; i < 3700; i++)
	{
		if (jbMgr.Items[i].ID == (u32)-1) continue; // id != -1 for the item to be valid
		if (!(jbMgr.Items[i].Flags & 1)) continue; // bit0 == 1 to be valid
		if (!(jbMgr.Items[i].Flags & 2)) continue; // bit1 == 1 for pack to be stored on SD
		
		// make a copy with fresh changes
		_JbMgrItem* item = (_JbMgrItem*)buffer;
		*item = jbMgr.Items[i];
		memset(item->Scores, 0, 50);
		item->Singer = -1;
		item->Icon = 0;
		item->Flags &= 0x7FDFFF;
		
		Handle handle;
		u64 filesize;
		
		// read pack
		char packPath[32];
		sprintf(packPath, "/jb/gak/%08lx/pack", item->ID);
		myprintf("* %08lx (", item->ID);
		print(item->Title);
		print(")");
		//myprintf("TEST %s\n", packPath);
		TRYCONT(FSUSER_OpenFile(&handle, extdata_archive, fsMakePath(PATH_ASCII, packPath), FS_OPEN_READ, 0), "Unable to open pack file");
		TRYCONT(FSFILE_GetSize(handle, &filesize), "Unable to obtain pack file size");
		TRYCONT(filesize > sizeof(buffer) - 312, "Size of pack file is unexpectedlly large");
		TRYCONT(FSFILE_Read(handle, NULL, 0, buffer + 312, filesize), "Unable to read pack file");
		FSFILE_Close(handle);
		
		// dump contents of buffer to "/bbpdump/<title> (<author>).bbp"
		u16 bbpPath[128];
		ConcatUTF16(bbpPath, false, u"/bbpdump/", item->Title, u" (", item->Author, u").bbp", NULL);
		TRYCONT(FSUSER_OpenFile(&handle, sdmc_archive, fsMakePath(PATH_UTF16, bbpPath), FS_OPEN_CREATE | FS_OPEN_WRITE, 0), "Unable to create bbp file");
		TRYCONT(FSFILE_Write(handle, NULL, 0, buffer, 312 + filesize, FS_WRITE_FLUSH | FS_WRITE_UPDATE_TIME), "Unable to write bbp file header");
		FSFILE_Close(handle);
		
		printRight("...SUCCESS!");
		exportCount++;
	}

	myprintf("Exported %u bbp files to sdmc://bbpdata/.\n", exportCount);
	return 0;
}

Result ImportPacks()
{
	Handle dirHandle;
	TRY(FSUSER_OpenDirectory(&dirHandle, sdmc_archive, fsMakePath(PATH_ASCII, "/bbpimport")), "Cannot find bbpimport directory");
	int fileCount = 0;
	
	FS_DirectoryEntry entry;
	u32 entriesRead = 0;
	while (!FSDIR_Read(dirHandle, &entriesRead, 1, &entry) && entriesRead)
	{
		if (entry.attributes & FS_ATTRIBUTE_DIRECTORY) continue; // skip folders
		if (strcmp(entry.shortExt, "BBP")) continue; // only read *.bbp
		//myprintf("* %8s (%5llu B)... ", entry.shortName, entry.fileSize);
		print("* ");
		print(entry.name);
		
		Handle handle;
		u16 bbpPath[300];
		ConcatUTF16(bbpPath, false, u"/bbpimport/", entry.name, NULL);
		if (FSUSER_OpenFile(&handle, sdmc_archive, fsMakePath(PATH_UTF16, bbpPath), FS_OPEN_READ, 0))
		{
			printRight("...unable to open");
		}
		else if (entry.fileSize > 131072)
		{
			printRight("...file too large");
			FSFILE_Close(handle);
		}
		else
		{
			FSFILE_Read(handle, NULL, 0, buffer, entry.fileSize);
			FSFILE_Close(handle);
			
			_JbMgrItem* item = (_JbMgrItem*)buffer;
			if ((item->ID >> 16) == 0x8000)
			{
				// custom ID, do weird stuff
				// TODO: MUCH LATER
				// 1) decompress part1 to buffer + 131072
				// 2) copy part2 to buffer + 262044
				// 3) modify decompressed part1
				// 4) recompress part1
				/*
				PackHeader* header = (PackHeader*)(buffer + 312);
				
				if (header->Version != 0x20001 || header->Parts[0].Used != 1 || header->Parts[2].Used || header->Parts[3].Used || header->Parts[0].Offset != 68)
				{
					myprintf("parsing error\n");
				}
				else if (gz_decompress(buffer + 312 + 68, header->Parts[0].
				{
					// 1)
					gz_decomp
				}
				*/
				printRight("...customID error");
			}
			else if (FindSongID(item->ID) != -1)
			{
				printRight("...already exists");
			}
			else
			{
				//int index = FindEmptySlot();
				int index = FindSongID(-1);
				if (index == -1)
				{
					// THERE IS NO EMPTY SLOT!!!
				}
				
				char packPath[32];
				sprintf(packPath, "/jb/gak/%08lx", item->ID);
				FSUSER_CreateDirectory(extdata_archive, fsMakePath(PATH_ASCII, packPath), 0);
				strcat(packPath, "/pack");
				
				FS_Path fsPath = fsMakePath(PATH_ASCII, packPath);
				FSUSER_CreateFile(extdata_archive, fsPath, 0, entry.fileSize - 312);
				FSUSER_OpenFile(&handle, extdata_archive, fsPath, FS_OPEN_WRITE, 0);
				FSFILE_Write(handle, NULL, 0, buffer + 312, entry.fileSize - 312, FS_WRITE_FLUSH);
				FSFILE_Close(handle);
				
				// TODO: CHECK FIRST THAT PACK WAS SUCCESSFULLY CREATED!?
				jbMgr.Items[index] = *item;
				
				printRight("...SUCCESS!");
				fileCount++;
			}
		}
	}
	
	FSDIR_Close(dirHandle);
	
	if (fileCount)
	{
		myprintf("Committing changes to /jb/mgr.bin...\n");
		TRY(WriteJbMgr(), "ERROR: Could not modify /jb/mgr.bin\n");
		myprintf("Imported %u bbp files from sdmc://bbpimport/.\n", fileCount);
	}
	else
	{
		myprintf("No changes were made to the extdata.\n");
	}
	
	return 0;
}

bool initialised = false;
void ShowInstructions()
{
	myprintf("\n");
	if (initialised)
	{
		myprintf("Press X to dump all BBP files.\n");
		myprintf("Press Y to import all BBP files.\n");
	}
	myprintf("Press START to exit.\n\n");
}

int main()
{
	// Initialize services
	glyphInit();
	//gfxInitDefault();
	//consoleInit(GFX_BOTTOM, NULL);
	
	//FSUSER_OpenArchive(&sdmc_archive); ImportPacks(); while(true);
	
	myprintf("== degausser3ds v2.2a ==\n");
	myprintf("Loading Daigasso! Band Bros P extdata into memory...\n");
	if (FSUSER_OpenArchive(&extdata_archive)) myprintf("ERROR: Unable to open DBBP extdata.\n");
	else if (FSUSER_OpenArchive(&sdmc_archive)) myprintf("ERROR: Unable to open SDMC archive.\n");
	else if (ReadJbMgr()) myprintf("ERROR: Unable to fully process /jb/mgr.bin\n");
	else initialised = true;

	ShowInstructions();
	while (aptMainLoop())
	{
		gspWaitForVBlank();
		gfxFlushBuffers();
		gfxSwapBuffers();
		hidScanInput();
		if (hidKeysDown() & KEY_X)
		{
			if (!initialised) continue;
			myprintf("Dumping bbp files to sdmc://bbpdata/:\n");
			DumpAllPacks();
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_Y)
		{
			if (!initialised) continue;
			myprintf("Importing bbp files from sdmc://bbpimport/:\n");
			ImportPacks();
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_START) break;
	}
	
	// Close archives
	FSUSER_CloseArchive(&sdmc_archive);
	FSUSER_CloseArchive(&extdata_archive);
	
	glyphExit();
	return 0;
}