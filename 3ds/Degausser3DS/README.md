# Degausser3DS

Degausser3DS is the native Nintendo 3DS companion utility for **Daigasso! Band Brothers P**. It imports and exports Band Brothers P content directly on the console.

This directory contains the native source: the original Degausser repository's code, with the additions made by [wangyu-](https://github.com/wangyu-/Degausser) (MIT licensed, based on the original) merged back in. Those additions are: import songs as custom songs from `SD:\bbpimportc\`, dump a song list to `SD:\bbpdump\songlist.txt`, and delete or dump specific songs by ID from `SD:\bbpdelete\songlist.txt` / `SD:\bbpdump\songlist.txt`. See `examples/` for the song list format. The only change to wangyu-'s code is a missing `return 0;` in `fBbp::bbp_to_raw()`. It is C++ and has not been built or run on a 3DS by this project yet; test on a copy of your save. It is intentionally kept outside `Degausser.sln`: Degausser3DS is a separate application that happens to share reverse-engineered format knowledge with the desktop Degausser app.

## Versioning and releases

Degausser3DS is versioned independently from the desktop application. Use product-qualified Git tags such as:

```text
degausser-v3.0.0       # desktop (Windows, Linux, macOS)
degausser3ds-vX.Y.Z    # native 3DS application
```

A GitHub release for Degausser3DS should contain only the native 3DS artefacts for that Degausser3DS version (for example its `.3dsx`) and its own release notes. Do not bundle the desktop Degausser executables merely because both applications live in this repository.

The inclusion of this source in the Degausser 3.0.0 repository does **not** itself constitute a new Degausser3DS release or assign it version 3.0.0.

## Which saves it works on

Degausser3DS reads and writes the game's standard extdata save (`jb/mgr.bin` and `jb/gak/`). If you play with the English translation patch, your save is most likely in `sd:/P/` on the SD card instead of in extdata, so Degausser3DS will not find your songs and will not be much use. Use the desktop Degausser on that save instead.

## Building

The checked-in Makefile is the historical devkitARM/libctru build. It expects the devkitPro/3DS development environment variables used by that toolchain. The native project has not been modernised as part of the Degausser 3.0 desktop work; verify it against a current devkitPro installation before preparing the next Degausser3DS release.

From an appropriate devkitPro shell, the historical entry point is simply:

```sh
make
```

The source remains valuable independently as a second implementation of the BBP/extdata behaviour documented elsewhere in this repository.

## License

MIT, the same licence as the rest of this repository ([LICENSE](../../LICENSE)). The wangyu- fork this code merges in is MIT too, and its LICENSE carries the same copyright notice as this repository's.
