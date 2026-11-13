struct Header
{
	u32 unused[2];
	u32 main_uncompressed_size;
	u32 main_start;//3
	u32 main_size;//4
	u32 has_voc;//5
	u32 voc_uncompressed_size;//
	u32 voc_start;//7
	u32 voc_size;//8
	u32 unused2[8];
};
/*
struct Bbp //fully decompress ,not used
{
	u8 raw[524288];
	u32 size;
	_JbMgrItem item;
	Header header;
	u8 main[2048*1024];
	u32 size_main;
	u8 voc[2048*1024];
	u32 size_voc;
	int init(u8 * buf,u32 size);
	int set_id(u32 id);
	int raw_to_bbp();
	int bbp_to_raw();
	int raw_size();
	u8* get_raw();
};*/

struct fBbp  //faster implement  //do not unzip vocal //pass raw by pointer
{
	u8 *raw;
	u32 size;
	_JbMgrItem item;
	Header header;
	u8 main[2048*1024];
	u32 size_main;
	u8 voc[512*1024];
	u32 size_voc;
	int init(u8 * buf,u32 size);
	int set_id(u32 id);
	int raw_to_bbp();
	int bbp_to_raw();
	int raw_size();
	int do_tricks();
	u8* get_raw();
};
