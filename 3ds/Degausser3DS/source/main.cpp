#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <sys/stat.h>
#include <3ds.h>
#define MINIZ_HEADER_FILE_ONLY
#include "miniz.c"
#define GLYPH_HEADER_FILE_ONLY
#include "glyph.cpp"

#include "comm.h"
#include "bbp_process.h"

#include <map>
#include <set>
#include <string>
#include <vector>
using namespace std;

u8 buffer[2048*1024];
//JbMgr jbMgr;
fBbp bbp;


struct JbMgrWithIndex
{
	JbMgr jbMgr;
	set<u32> empty_slots;
	set<u32> non_empty_slots;
	set<u32> unused_custom_id;
	map<u32,u32> used_id;//id -> idx
	int from_raw(u8* buf,u32 size)
	{
		empty_slots.clear();
		non_empty_slots.clear();
		unused_custom_id.clear();
		used_id.clear();
		debugprintf("<before from_raw:%d %d %d %d>",(int)empty_slots.size(),(int)non_empty_slots.size(),(int)unused_custom_id.size(),(int)used_id.size());


		TRY(gz_decompress((u8 *)&jbMgr, sizeof(jbMgr), buf, size), "Unable to decompress jbMgr");
		for (int i = 0; i < 3700; i++)
		{
			if (jbMgr.Items[i].ID == (u32)-1) empty_slots.insert(i);
			else non_empty_slots.insert(i);
		}
		bool used[3700] = {};
		for (int i = 0; i < 3700; i++)
		{
			u32 id = jbMgr.Items[i].ID;
			if(id!=(u32)-1)
			{
				if(used_id.find(id)!=used_id.end())
				{
					myprintf("\n duplicate id in jbmgr.bin!!");
					return -1;
				}
				used_id.insert(pair<u32,u32>(id,i));
			}
			if ((id >> 16) == 0x8000) used[id & 0xFFFF] = true;
		}
		for (int i = 0; i < 3700; i++)
		{
			if (!used[i])  unused_custom_id.insert(0x80000000 | i);
		}
		debugprintf("<after_from_raw:%d %d %d %d>",(int)empty_slots.size(),(int)non_empty_slots.size(),(int)unused_custom_id.size(),(int)used_id.size());
		return 0;
	}
	int to_raw(u8* buf,u32 *size)
	{
		TRY(gz_compress(buffer, size, (u8 *)&jbMgr, sizeof(jbMgr)), "Unable to compress jbMgr");
		return 0;
	}
	int has_id(u32 id)
	{
		return used_id.find(id)!=used_id.end();
	}
	int empty_slots_size()
	{
		return empty_slots.size();
	}
	int insert(_JbMgrItem *item)//can also insert custom song,just wont change id
	{
		debugprintf("<before insert:%d %d %d %d>",empty_slots.size(),non_empty_slots.size(),unused_custom_id.size(),used_id.size());
		u32 id=item->ID;
		TRY(used_id.find(id)!=used_id.end(),"id already exist");
		TRY(empty_slots.size()==0,"\n jbmgr full!");
		if(id>>16==(u32)0x8000) unused_custom_id.erase(unused_custom_id.find(id));
		s32 idx=*empty_slots.begin();
		empty_slots.erase(empty_slots.begin());
		non_empty_slots.insert(idx);
		jbMgr.Items[idx]=*item;
		used_id[id]=idx;
		debugprintf("<after insert:%d %d %d %d>",empty_slots.size(),non_empty_slots.size(),unused_custom_id.size(),used_id.size());
		return 0;
	}
	u32 get_an_unused_custom_id()
	{
		TRY(unused_custom_id.size()==0,"\n custom id used up!");
		return *unused_custom_id.begin();
	}
	int delete_by_id(u32 id)
	{
		debugprintf("<before delete:%d %d %d %d>",empty_slots.size(),non_empty_slots.size(),unused_custom_id.size(),used_id.size());

		TRY(id==(u32)-1,"invaild id:-1");
		TRY(used_id.find(id)==used_id.end(),"not found");
		int idx=used_id[id];
		if (!(jbMgr.Items[idx].Flags & 2))
		{
			myprintf("jbMgr.Items[idx].Flags & 2 =0 ,i dont know how to deal with this...");
			return -1;
		}

		if(id>>16==(u32)0x8000) unused_custom_id.insert(id);
		debugprintf("idx:%d",idx);
		empty_slots.insert(idx);
		non_empty_slots.erase(idx);
		used_id.erase(used_id.find(id));
		memset((void*)(jbMgr.Items+idx),0,sizeof(jbMgr.Items[idx]));
		jbMgr.Items[idx].ID=-1;
		debugprintf("<after delete:%d %d %d %d>",empty_slots.size(),non_empty_slots.size(),unused_custom_id.size(),used_id.size());
		return 0;
	}
}JbMgrIdx;

void wait()
{
	gspWaitForVBlank();
	gfxFlushBuffers();
	gfxSwapBuffers();
}


// SANITY CHECKS
typedef char test_item[sizeof(_JbMgrItem) == 312 ? 1 : -1];
typedef char test_jbmgr[sizeof(JbMgr) == 1154408 ? 1 : -1];
typedef char test2_jbmgr[sizeof(PackHeader) == 68 ? 1 : -1];

// archive-related stuff

u32 extdata_archive_lowpathdata[3] = {MEDIATYPE_SD, 0xa0b, 0};
FS_Archive extdata_archive = {ARCHIVE_EXTDATA, {PATH_BINARY, 0xC, &extdata_archive_lowpathdata}};
FS_Archive sdmc_archive = {ARCHIVE_SDMC, {PATH_ASCII, 1, ""}};



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

	//TRY(gz_decompress((u8 *)&jbMgr, sizeof(jbMgr), buffer, size2), "Unable to decompress jbMgr");

	JbMgrIdx.from_raw(buffer,size2);
	
	myprintf("Successfully loaded extdata://00000a0b/jb/mgr.bin!\n");
	return 0;
}

Result WriteJbMgr()
{
	u32 compLen = sizeof(buffer);
	
	JbMgrIdx.to_raw(buffer,&compLen);
	//TRY(gz_compress(buffer, &compLen, (u8 *)&jbMgr, sizeof(jbMgr)), "Unable to compress jbMgr");

	const char* paths[2] = {"/jb/mgr.bin", "/jb/mgr_.bin"};
	for (int i = 0; i < 2; i++)
	{
		Handle handle;
		TRY(FSUSER_OpenFile(&handle, extdata_archive, fsMakePath(PATH_ASCII, paths[i]), FS_OPEN_WRITE, 0), "Unable to write to jbMgr");
		TRY(FSFILE_Write(handle, NULL, 0, buffer, compLen, FS_WRITE_FLUSH),"write mgr fail");
		TRY(FSFILE_Write(handle, NULL, 1155068, &compLen, 4, FS_WRITE_FLUSH),"write mgr fail");
		FSFILE_Close(handle);
	}
	
	// NOT YET IMPLEMENTED
	return 0;
}
/*
int FindSongID(u32 id)
{
	// inefficient code to find the first slot with ID = id
	for (int i = 0; i < 3700; i++)
	{
		if (jbMgr.Items[i].ID == id) return i;
	}
	return -1;
}


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
vector<u32> get_song_idx_from_song_list(string path)
{
	vector<u32> vec;
	Handle handle;
	if(FSUSER_OpenFile(&handle, sdmc_archive, fsMakePath(PATH_ASCII, path.c_str()), FS_OPEN_READ, 0))
	{
		myprintf("open %s fail!\n",path.c_str());
	}
	u64 filesize;
	FSFILE_GetSize(handle,&filesize);
	FSFILE_Read(handle, NULL, 0, buffer, filesize);
	buffer[filesize]=0;
	u8*str=buffer;
	if(str[0]==0xef&&str[1]==0xbb&&str[2]==0xbf)
	{
		str+=3;
	}

	int line=0;
	for (char *sub_str = strtok ((char *)str,"\n");sub_str != NULL;sub_str = strtok (NULL, "\n"))
	{
		line++;
		if(line%20==0)
			myprintf("%d lines processed\n",line);
	    //myprintf ("<%s>\n",sub_str);

	    char *p;
	    for(p=sub_str;*p;p++)
	    	*p=tolower(*p);
	    u32 len=p-sub_str;
	    if(len<8)
	    {
	    	debugprintf("skipped line %d ,not enough length;",line);
	    	continue;
	    }
	    int fail=0;
	    for(p=sub_str;(p-sub_str)<8;p++)
	    {
	    	if(!( ('0'<=*p&&*p<='9') || ('a'<=*p&&*p<='f') ))
	    	{
	    		debugprintf("invaild number at line %d %lx,skipped;",line,(u32)*p);
	    		fail=1;
	    		break;
	    	}
	    }
	    if(fail) continue;
	    if(('0'<=*p&&*p<='9') || ('a'<=*p&&*p<='f') )
	    {
	    	debugprintf("longer number than expected at line %d,skipped;",line);
    		continue;
	    }
	    u32 id;
	    sscanf(sub_str,"%lx",&id);
	    debugprintf("<id:%lx>",id);
	    if(id==(u32)-1)
	    {
	    	debugprintf("skipped invaild id ffffffff;",line);
    		continue;
	    }
	    if(JbMgrIdx.has_id(id)==0)
	    {
	    	debugprintf("no such id %lx,skipped;",id);
    		continue;
	    }
	    vec.push_back(JbMgrIdx.used_id[id]);

	}
	myprintf("finish reading %s.it contains %d lines,%d vaild&existing IDs\n",path.c_str(),line,vec.size());
	return vec;
}


Result DumpAllPacks(int dump_list_file,int dump_by_list)
{
	int exportCount = 0;
	string song_list;
	song_list+=(char)0xFF;
	song_list+=(char)0xFE;
	// create bbpdump folder
	FSUSER_CreateDirectory(sdmc_archive, fsMakePath(PATH_ASCII, "/bbpdump"), 0);
	
	vector<u32> vec;
	if(dump_by_list==0)
	{
		for (auto it=JbMgrIdx.non_empty_slots.begin(); it!= JbMgrIdx.non_empty_slots.end(); it++)
		{
			vec.push_back(*it);
		}
	}
	else
	{
		vec=get_song_idx_from_song_list("/bbpdump/songlist.txt");
	}

	// some operations on jbMgr
	for (u32 u=0; u<vec.size(); u++)
	{
		wait();
		hidScanInput();
		if(hidKeysHeld() & KEY_B)
		{
			myprintf("break by pressed B\n");
			break;
		}

		debugprintf("<u:%d>",u);
		int i=vec[u];
		if (JbMgrIdx.jbMgr.Items[i].ID == (u32)-1) continue; // id != -1 for the item to be valid
		if (!(JbMgrIdx.jbMgr.Items[i].Flags & 1))
		{
			debugprintf("invaild data at idx:%d id:%d",i,JbMgrIdx.jbMgr.Items[i].ID);
			continue; // bit0 == 1 to be valid
		}
		if (!(JbMgrIdx.jbMgr.Items[i].Flags & 2))
		{
			debugprintf("a not-on-SD record at  idx:%d id%d",i,JbMgrIdx.jbMgr.Items[i].ID);
			continue; // bit1 == 1 for pack to be stored on SD
		}
		// make a copy with fresh changes
		_JbMgrItem* item = (_JbMgrItem*)buffer;
		*item = JbMgrIdx.jbMgr.Items[i];
		memset(item->Scores, 0, 50);
		item->Singer = -1;
		item->Icon = 0;
		item->Flags &= 0x7FDFFF;
		
		Handle handle;
		u64 filesize;
		
		// read pack
		char packPath[32];
		sprintf(packPath, "/jb/gak/%08lx/pack", item->ID);

		if(dump_list_file==0)
		{
			myprintf("* %08lx (", item->ID);
			print(item->Title);
			print(")");
		}
		else
		{
			if(u>0&&u%20==0)
			{
				myprintf("%d processed,%d remain\n",u,vec.size()-u);
			}
		}
		u16 bbpPath[128];
		unsigned char tmp_id[10];
		sprintf((char*)tmp_id,"%08lx-",item->ID);
		u16 tmp_id2[10];
		for(int j=0;j<10;j++)
			tmp_id2[j]=tmp_id[j];
		//myprintf("TEST %s\n", packPath);
		if(dump_list_file==0)
		{
			TRYCONT(FSUSER_OpenFile(&handle, extdata_archive, fsMakePath(PATH_ASCII, packPath), FS_OPEN_READ, 0), "Unable to open pack file");
			TRYCONT(FSFILE_GetSize(handle, &filesize), "Unable to obtain pack file size");
			TRYCONT(filesize > sizeof(buffer) - 312, "Size of pack file is unexpectedlly large");
			TRYCONT(FSFILE_Read(handle, NULL, 0, buffer + 312, filesize), "Unable to read pack file");
			FSFILE_Close(handle);

			// dump contents of buffer to "/bbpdump/<title> (<author>).bbp"

			ConcatUTF16(bbpPath, false, u"/bbpdump/", tmp_id2,item->Title, u" (", item->Author, u").bbp", NULL);


			TRYCONT(FSUSER_OpenFile(&handle, sdmc_archive, fsMakePath(PATH_UTF16, bbpPath), FS_OPEN_CREATE | FS_OPEN_WRITE, 0), "Unable to create bbp file");
			TRYCONT(FSFILE_SetSize(handle,312 + filesize),"set file size fail");
			TRYCONT(FSFILE_Write(handle, NULL, 0, buffer, 312 + filesize, FS_WRITE_FLUSH | FS_WRITE_UPDATE_TIME), "Unable to write bbp file header");
			FSFILE_Close(handle);
			myprintf("...SUCCESS!\n");
		}
		else
		{
			ConcatUTF16(bbpPath, false, tmp_id2,item->Title, u" (", item->Author, u").bbp", NULL);
			debugprintf("songlist.size:%d",song_list.size());
			for(int j=0;bbpPath[j]!=0;j++)
			{
				song_list+=((u8*)(bbpPath+j))[0];
				song_list+=((u8*)(bbpPath+j))[1];
			}
			u16 tmp='\r';
			song_list+=((u8*)(&tmp))[0];
			song_list+=((u8*)(&tmp))[1];
			tmp='\n';
			song_list+=((u8*)(&tmp))[0];
			song_list+=((u8*)(&tmp))[1];
		}

		exportCount++;
	}
	if(dump_list_file==1)
	{
		Handle handle;
		song_list+=(char)0;
		song_list+=(char)0;
		int res=utf16_to_utf8(buffer,(const u16*)song_list.c_str(),sizeof(buffer) );//this function wont set endofstr to zero
		if(res==-1)
		{
			myprintf("convert utf16 to utf8 failed,saving songlist to /bbpdump/songlist.utf16.txt\n");

			TRY(FSUSER_OpenFile(&handle, sdmc_archive,  fsMakePath(PATH_ASCII, "/bbpdump/songlist.utf16.txt"), FS_OPEN_CREATE | FS_OPEN_WRITE, 0), "Unable to create songlist.utf16.txt");
			TRY(FSFILE_SetSize(handle,song_list.size()-2),"set file size fail");
			TRY(FSFILE_Write(handle, NULL, 0,song_list.c_str(),song_list.size()-2, FS_WRITE_FLUSH | FS_WRITE_UPDATE_TIME), "Unable to write songlist.utf16.txt");
			FSFILE_Close(handle);
		}
		else
		{
			//int len=strlen((const char *)buffer);
			debugprintf("songlist.size:%d",song_list.size());

			TRY(FSUSER_OpenFile(&handle, sdmc_archive,  fsMakePath(PATH_ASCII, "/bbpdump/songlist.txt"), FS_OPEN_CREATE | FS_OPEN_WRITE, 0), "Unable to create songlist.txt");
			TRY(FSFILE_SetSize(handle,res),"set file size fail");
			TRY(FSFILE_Write(handle, NULL, 0, buffer, res, FS_WRITE_FLUSH | FS_WRITE_UPDATE_TIME), "Unable to write songlist.txt");
			FSFILE_Close(handle);
			myprintf("Exported %u bbp entires to sdmc://bbpdump/songlist.txt\n", exportCount);
		}
	}
	else
	{
		myprintf("Exported %u bbp files to sdmc://bbpdump/.\n", exportCount);
	}
	return 0;
}

Result ImportPacks(int c)
{
	Handle dirHandle;
	if(c==0)
	{
		TRY(FSUSER_OpenDirectory(&dirHandle, sdmc_archive, fsMakePath(PATH_ASCII, "/bbpimport")), "Cannot find bbpimport directory");
	}
	else
	{
		TRY(FSUSER_OpenDirectory(&dirHandle, sdmc_archive, fsMakePath(PATH_ASCII, "/bbpimportc")), "Cannot find bbpimportc directory");
	}
	int fileCount = 0;
	
	FS_DirectoryEntry entry;
	u32 entriesRead = 0;
	while (!FSDIR_Read(dirHandle, &entriesRead, 1, &entry) && entriesRead)
	{
		wait();
		hidScanInput();
		if(hidKeysHeld() & KEY_B)
		{
			myprintf("break by pressed B\n");
			break;
		}

		if(JbMgrIdx.empty_slots_size()==0)
		{
			myprintf("reached limitation3700,no empty slot!");
			return -1;
		}
		if (entry.attributes & FS_ATTRIBUTE_DIRECTORY) continue; // skip folders
		if (strcmp(entry.shortExt, "BBP")) continue; // only read *.bbp
		//myprintf("* %8s (%5llu B)... ", entry.shortName, entry.fileSize);
		print("* ");
		print(entry.name);
		
		print(" ");
		Handle handle;
		u16 bbpPath[300];
		if(c==0)
		{
			ConcatUTF16(bbpPath, false, u"/bbpimport/", entry.name, NULL);
		}
		else
		{
			ConcatUTF16(bbpPath, false, u"/bbpimportc/", entry.name, NULL);
		}

		if (FSUSER_OpenFile(&handle, sdmc_archive, fsMakePath(PATH_UTF16, bbpPath), FS_OPEN_READ, 0))
		{
			myprintf("...unable to open\n");
			continue;
		}
		if (entry.fileSize > 131072)
		{
			myprintf("...file too large\n");
			FSFILE_Close(handle);
			continue;
		}
		//else
		{
			int size=entry.fileSize;
			FSFILE_Read(handle, NULL, 0, buffer, entry.fileSize);
			FSFILE_Close(handle);
			
			_JbMgrItem* item;
			if(c==0)
			{
				item = (_JbMgrItem*)buffer;
				if ((item->ID >> 16) == 0x8000)
				{
					myprintf("...customID error\n");
					continue;
				}
				else if (JbMgrIdx.has_id(item->ID))
				{
					myprintf("...already exists\n");
					continue;
				}
				if (JbMgrIdx.empty_slots_size()==0)
				{
					myprintf("...no empty slot\n");
					continue;
				}
			}
			else //c=1;
			{
				if (JbMgrIdx.empty_slots_size()==0)
				{
					myprintf("...no empty slot\n");
					continue;
				}
				u32 id=JbMgrIdx.get_an_unused_custom_id();
				if(id==(u32)-1)
				{
					//full
					myprintf("...customID full\n");
					continue;
				}

				bbp.init(buffer, entry.fileSize);
				bbp.raw_to_bbp();
				bbp.set_id(id);
				bbp.bbp_to_raw();
				//memcpy(buffer,bbp.get_raw(),bbp.raw_size());
				size=bbp.raw_size();
				item = (_JbMgrItem*)buffer;
			}



			char dirPath[32];
			char packPath[32];
			sprintf(dirPath, "/jb/gak/%08lx", item->ID);
			sprintf(packPath, "/jb/gak/%08lx", item->ID);

			strcat(packPath, "/pack");
			FS_Path fsPath;
			fsPath = fsMakePath(PATH_ASCII, packPath);
			debugprintf("packPath:%s;",packPath);
			//FSUSER_DeleteDirectoryRecursively(extdata_archive, fsMakePath(PATH_ASCII, packPath));
			if(FSUSER_CreateDirectory(extdata_archive, fsMakePath(PATH_ASCII, dirPath), 0))
			{
				debugprintf("create dir %s fail;",dirPath);
				FSUSER_DeleteFile(extdata_archive,fsPath);
			}
			FSUSER_CreateFile(extdata_archive, fsPath, 0, size - 312); //do not check failure;
			TRYCONT(FSUSER_OpenFile(&handle, extdata_archive, fsPath, FS_OPEN_WRITE, 0),"open pack file fail;");
			//TRYCONT(FSFILE_SetSize(handle,0),"set pack file size fail;");//set size doesnt work here....
			TRYCONT(FSFILE_Write(handle, NULL, 0, buffer + 312, size - 312, FS_WRITE_FLUSH),"write pack file fail");
			FSFILE_Close(handle);

			// TODO: CHECK FIRST THAT PACK WAS SUCCESSFULLY CREATED!?
			JbMgrIdx.insert(item);

			myprintf("...SUCCESS!\n");
			fileCount++;
		}
	}
	
	FSDIR_Close(dirHandle);
	
	if (fileCount)
	{
		myprintf("Committing changes to /jb/mgr.bin...\n");
		TRY(WriteJbMgr(), "ERROR: Could not modify /jb/mgr.bin\n");
		if(c==0)
			myprintf("Imported %u bbp files from sdmc://bbpimport/.\n", fileCount);
		else
			myprintf("Imported %u bbp files from sdmc://bbpimportc/.\n", fileCount);
	}
	else
	{
		myprintf("No changes were made to the extdata.\n");
	}
	
	return 0;
}

Result DeletePacks(int fast)
{
	Handle dirHandle;
	TRY(FSUSER_OpenDirectory(&dirHandle, sdmc_archive, fsMakePath(PATH_ASCII, "/bbpdelete")), "Cannot find bbpdelete directory");
	int fileCount = 0;

	myprintf("dangerous operation,hold A B X Y to confirm,SELECT to cancel\n");

	while(1)
	{
		wait();
		hidScanInput();
		if(hidKeysHeld()&KEY_SELECT)
		{
			myprintf("cancelled,release SELECT to return.\n");

			while(1)
			{
				wait();
				hidScanInput();
				if((hidKeysHeld()&KEY_SELECT)==0)
					return -1;
			}
		}
		if((hidKeysHeld() & (KEY_A|KEY_B |KEY_X| KEY_Y))  ==(KEY_A|KEY_B|KEY_X|KEY_Y))
		{
			myprintf("confirmed.release keys to continue\n");
			break;
		}
	}

	while(1)
	{
		wait();
		hidScanInput();
		if((hidKeysHeld() & (KEY_A|KEY_B |KEY_X| KEY_Y))==0)
			break ;
	}
	//FS_DirectoryEntry entry;
	//u32 entriesRead = 0;
	vector<u32> vec=get_song_idx_from_song_list("/bbpdelete/songlist.txt");
	//while (!FSDIR_Read(dirHandle, &entriesRead, 1, &entry) && entriesRead)
	for(u32 i=0;i<vec.size();i++)
	{
		int idx=vec[i];
		wait();
		hidScanInput();
		if(hidKeysHeld() & KEY_B)
		{
			myprintf("break by pressed B\n");
			break;
		}
		if(!fast)//print is slow....
		{
			myprintf("* deleteing, id:%lx ",JbMgrIdx.jbMgr.Items[idx].ID);
			print(JbMgrIdx.jbMgr.Items[idx].Title);
		}
		else
		{
			if(i>0&&i%20==0)
			{
				myprintf("%d processed,%d remaining;",i,vec.size()-i);
			}
		}

		u32 id=JbMgrIdx.jbMgr.Items[idx].ID;
		if ((id >> 16) == 0x8000)
		{
			//printRight(" skiped custom song");
			//continue;
		}
		if (JbMgrIdx.has_id(id)==0)
		{
			myprintf("...not found!\n");
			continue;
		}

		if(JbMgrIdx.delete_by_id(id)!=0)
		{
			myprintf("delete fail!\n");
			continue;
		}

		fileCount++;
		if(!fast)
		{
			char packPath[32];
			sprintf(packPath, "/jb/gak/%08lx", id);
			if(FSUSER_DeleteDirectoryRecursively(extdata_archive, fsMakePath(PATH_ASCII, packPath)))
			{
				myprintf("\ndelete dir %s fail!\n",packPath);
				myprintf("...part SUCCESS!\n");
				continue;
			}
		}
		if(!fast) myprintf("...SUCCESS!\n");

	}

	FSDIR_Close(dirHandle);

	if (fileCount)
	{
		myprintf("Committing changes to /jb/mgr.bin...\n");
		TRY(WriteJbMgr(), "ERROR: Could not modify /jb/mgr.bin\n");
		myprintf("deleted %u songs.\n", fileCount);
	}
	else
	{
		myprintf("No changes were made \n");
	}

	return 0;
}

bool initialised = false;
void ShowInstructions()
{
	myprintf("\n");
	if (initialised)
	{
     //debugprintf("123456789012345678901234567890123456789012345678901234567890\n");
		debugprintf("debug info is now turned on\n");
		myprintf("Press X to dump all BBP files(to /bbpdump/).\n");
		myprintf("Press Y to import all BBP files(from /bbpimport/)\n");
		myprintf("A to import all BBP as custom(from /bbpimportc/).\n");
		myprintf("LEFT to dump a songlist(to /bbpdump/songlist.txt)\n");
		myprintf("RIGHT to dump BBP by(/bbpdump/songlist.txt)\n");
		myprintf("UP to del songs by(/bbpdelete/songlist.txt)\n");
		myprintf("DOWN to do a fast delete,wont release space\n");
		myprintf("(hold B to break a running operation)\n");
	}
	myprintf("Press START to exit.\n\n");
}



int main()
{
	// Initialize services
	glyphInit();

	hidScanInput();
	if(hidKeysHeld() & KEY_B) g_show_log=1;
	//gfxInitDefault();
	//consoleInit(GFX_BOTTOM, NULL);

	//FSUSER_OpenArchive(&sdmc_archive); ImportPacks(); while(true);
	
	myprintf("== degausser3ds v2.2a modified==\n");
	myprintf("Loading Daigasso! Band Bros P extdata into memory...\n");
	if (FSUSER_OpenArchive(&extdata_archive)) myprintf("ERROR: Unable to open DBBP extdata.\n");
	else if (FSUSER_OpenArchive(&sdmc_archive)) myprintf("ERROR: Unable to open SDMC archive.\n");
	else if (ReadJbMgr()) myprintf("ERROR: Unable to fully process /jb/mgr.bin\n");
	else initialised = true;

	ShowInstructions();
	while (aptMainLoop())
	{
		wait();
		hidScanInput();
		//myprintf("test");
		if (hidKeysDown() & KEY_X)
		{
			if (!initialised) continue;
			myprintf("Dumping bbp files to sdmc://bbpdump/:\n");
			DumpAllPacks(0,0);
			ShowInstructions();
		}
		if (hidKeysDown() & KEY_DLEFT)
		{
			if (!initialised) continue;
			myprintf("Dumping bbp list to sdmc://bbpdump/songlist.txt:\n");
			DumpAllPacks(1,0);
			ShowInstructions();
		}
		if (hidKeysDown() & KEY_DRIGHT)
		{
			if (!initialised) continue;
			myprintf("Dumping bbp by list sdmc://bbpdump/songlist.txt:\n");
			DumpAllPacks(0,1);
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_Y)
		{
			if (!initialised) continue;
			myprintf("Importing bbp files from sdmc://bbpimport/:\n");
			ImportPacks(0);
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_A)
		{
			if (!initialised) continue;
			myprintf("Importing bbp files from sdmc://bbpimportc/:\n");
			ImportPacks(1);
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_DUP)
		{
			if (!initialised) continue;
			myprintf("deleting:\n");
			DeletePacks(0);
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_DDOWN)
		{
			if (!initialised) continue;
			myprintf("do fast deleting:\n");
			DeletePacks(1);
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_R)
		{
			g_show_log=1;
			ShowInstructions();
		}
		else if (hidKeysDown() & KEY_L)
		{
			g_show_log=0;
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
