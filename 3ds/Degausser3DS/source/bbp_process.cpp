#include "comm.h"
#include "bbp_process.h"

u32 align_to_4(u32 a)
{
	return (a+3)&(~3);
}



int get_u32(u8 *buf,int index)
{

}
int set_u32(u8 *buf,int index,u32 value)
{
	*((u32*)(buf+index))=value;
	return 0;
}
int get_u16(u8 *buf,int index)
{

}
int set_u16(u8 *buf,int index,int value)
{

}

/*
int Bbp:: init(u8 * buf,u32 size)
{
	if(size >sizeof(raw))
		return -1;
	for(u32 i=0;i<size;i++)
		raw[i]=buf[i];
	this->size=size;
	return 0;
}
int Bbp:: set_id(u32 id)
{
	item.ID=id;
	set_u32(main,4,id);
	return 0;
}
int Bbp:: raw_to_bbp()
{
	int res;
	memcpy((u8*)&item,raw,sizeof(item));
	memcpy((u8*)&header,raw+sizeof(item),sizeof(header));
	debugprintf("\nmain start=%d\n ",header.main_start);
	size_main=sizeof(main);
	if ((res=gz_decompress2(main,&size_main,raw+sizeof(item)+header.main_start,header.main_size))!=0)
	{
		debugprintf("\ndecompress main fail,res=%d\n",res);
		return -1;
	}
	if(header.has_voc==1)
	{
		size_voc=sizeof(voc);
		if((res=gz_decompress2(voc,&size_voc,raw+sizeof(item)+header.voc_start,header.voc_size))!=0)
		{
			debugprintf("\ndecompress voc fail,res=%d\n",res);
			return -1;
		}
	}
	return 0;
}
int Bbp:: bbp_to_raw()
{
	int res;
	memset(raw,0,size);
	memcpy(raw,(u8*)&item,sizeof(item));

	header.main_size=sizeof(main);
	if((res=gz_compress(raw+header.main_start+sizeof(item),&header.main_size,main,size_main)!=0))
	{
		debugprintf("\ngz_compress main fail,res=%d\n",res);
		return -1;
	}
	size=header.main_start+sizeof(item)+header.main_size;

	if(header.has_voc==1)
	{
		header.voc_start=align_to_4(header.main_start+header.main_size);
		header.voc_size=sizeof(voc);
		if((res=gz_compress(raw+header.voc_start+sizeof(item),&header.voc_size,voc,size_voc)!=0))
		{
			debugprintf("\ngz_compress voc fail,res=%d\n",res);
			return -1;
		}
		size=header.voc_start+sizeof(item)+header.voc_size;
	}
	memcpy(raw+sizeof(item),(u8*)&header,sizeof(header));

}
int Bbp:: raw_size()
{
	return size;
}
u8* Bbp:: get_raw()
{
	return this->raw;
}
*/



int fBbp:: init(u8 * buf,u32 size)
{
	raw=buf;
	this->size=size;
	return 0;
}
int fBbp:: set_id(u32 id)
{
	item.ID=id;
	set_u32(main,4,id);
	return 0;
}
int fBbp:: do_tricks()
{
	header.unused[0]=0x20001;
	header.unused[1]=1;
	memset(header.unused2,0,sizeof(header.unused2));//clear this unknown zone to avoid problem
	return 0;
}
int fBbp:: raw_to_bbp()
{
	int res;
	memcpy((u8*)&item,raw,sizeof(item));
	memcpy((u8*)&header,raw+sizeof(item)/*312*/,sizeof(header));
	//debugprintf("\nmain start=%d\n ",header.main_start);
	size_main=sizeof(main);
	if ((res=gz_decompress2(main,&size_main,raw+sizeof(item)+header.main_start,header.main_size))!=0)
	{
		debugprintf("\ndecompress main fail,res=%d\n",res);
		return -1;
	}
	if(header.has_voc==1)
	{
		size_voc=header.voc_size;
		memcpy(voc,raw+sizeof(item)+header.voc_start,header.voc_size);
	}
	return 0;
}
int fBbp:: bbp_to_raw()
{
	do_tricks();
	int res;
	memset(raw,0,size);
	memcpy(raw,(u8*)&item,sizeof(item));

	header.main_size=sizeof(main);
	if((res=gz_compress(raw+header.main_start+sizeof(item),&header.main_size,main,size_main)!=0))
	{
		debugprintf("\ngz_compress main fail,res=%d\n",res);
		return -1;
	}
	size=header.main_start+sizeof(item)+header.main_size;

	if(header.has_voc==1)
	{
		header.voc_start=align_to_4(header.main_start+header.main_size);
		//header.voc_size=sizeof(voc);
		/*if((res=gz_compress(raw+header.voc_start+sizeof(item),&header.voc_size,voc,size_voc)!=0))
		{
			debugprintf("\ngz_compress voc fail,res=%d\n",res);
			return -1;
		}*/
		memcpy(raw+header.voc_start+sizeof(item),voc,header.voc_size);
		size=header.voc_start+sizeof(item)+header.voc_size;
	}
	memcpy(raw+sizeof(item)/*312*/,(u8*)&header,sizeof(header));
	return 0;
}
int fBbp:: raw_size()
{
	return size;
}
u8* fBbp:: get_raw()
{
	return this->raw;
}


#ifdef TEST //test bbp processor on computer
u8 buffer[524288];
fBbp bbp;
fBbp bbp2;

int main()
{
	g_show_log=1;
	printf("asdasd");
	fflush(stdout);
	printf("%d ",sizeof(_JbMgrItem));
	fflush(stdout);
	u8 buf[1024*1024];
	FILE *fp;
	int size;
	fp=fopen("./test.bbp","rb");
	if(fp==NULL)
	{
		printf("copy a bbp file as test.bbp first\n");
		return -1;
	}
	printf("%d",fp);
	fflush(stdout);
    fseek (fp, 0 , SEEK_END);
    size= ftell (fp);
    rewind (fp);
    fread (buf,1,size,fp);

    printf("good so far @%d",__LINE__);
    fflush(stdout);
    bbp.init(buf,size);
    printf("good so far @%d",__LINE__);
    fflush(stdout);
    bbp.raw_to_bbp();
    bbp.bbp_to_raw();
    FILE *fp2=fopen("output.bbp","wb");
    fwrite(bbp.get_raw(),1,bbp.raw_size(),fp2);
    fclose(fp2);



    bbp2.init(bbp.get_raw(),bbp.raw_size());
    bbp2.raw_to_bbp();
    bbp2.set_id(0x80000001);
    bbp2.bbp_to_raw();
    FILE *fp3=fopen("output2.bbp","wb");
    fwrite(bbp2.get_raw(),1,bbp2.raw_size(),fp3);
    fclose(fp3);

    FILE *fp4=fopen("output.main","wb");
    fwrite(bbp2.main,1,bbp2.size_main,fp4);
    fclose(fp4);

}
#endif
