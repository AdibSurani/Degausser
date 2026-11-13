# Extdata containers and portable packages

This doc covers how Band Brothers P stores content in 3DS extdata, and Degausser's portable `.bbpm` / `.bbpv` packages. The score format itself (BBP, GAK, JbMgr items) is in [bbp-format.md](bbp-format.md).

## SafeFile containers

Manager files (`jb/mgr.bin`, `mv/mgr.bin`, `singer/mgr.bin`, …) are **fixed-size SafeFile containers**:

```text
[ gzip stream of the manager payload ][ zero padding ][ u32 LE: gzip stream length ]
                                                       ^ last 4 bytes of the container
```

- The container size is fixed per manager. For example, `jb/mgr.bin` is 1,155,072 bytes with a 1,154,408-byte payload.
- Every manager is written twice, as a primary and an underscore mirror (`mgr.bin` + `mgr_.bin`), and both copies are identical.
- **The 3DS uses the length footer when loading.** Before 2026-10-31, `ExtDataSafeFile.WriteMirrored` left the footer zero. The first save Degausser rewrote (deleting one song) was then rejected by the game, while an otherwise identical known-good copy loaded. Every fixed-size manager in the working save had a correct footer. `repair_extdata_safefile_footer.py` (a maintenance script outside this repo) fixes saves affected by the old bug: it rewrites only a zero footer, and only after validating the gzip stream.
- `ExtDataSafeFile` refuses a compressed stream that would overlap the footer.

## Extdata layout (as used by Degausser)

```text
jb/mgr.bin, jb/mgr_.bin        song catalogue (JbMgr, 3,700 slots)
jb/mgr2.bin                    filtered secondary catalogue (JbMgr2)
jb/gak/<song-id>/pack          song pack (pack header + gzip GAK [+ Vocaloid])
jb/gak/<song-id>/adpcm         optional separate audio sidecar for some movie songs
mv/mgr.bin                     movie catalogue (MvMgr, 1,000 slots)
mv/data/<video-id>             MOFLEX movie (opaque to Degausser)
singer/mgr.bin, singer/mgr2.bin  downloadable Vocaloid singer catalogues
singer/pack, singer/thumb      singer payloads
voc/db/dbNN.{bin,ddi,ddb}      Vocaloid voice database
```

Paths are always built with `Path.Combine`. An early port embedded Windows `gak\<id>` separators, which broke Linux and macOS.

`ExtDataWorkspace` is the logical document. `mgr.bin` is only the file the user picks to open it. Writes are **immediate**, not staged. A dirty/transactional workspace is a possible future change, and it is the reason batch operations can only cancel between items (see [architecture.md](architecture.md#extdata-writes)).

## Restore points

`ExtDataBackup` (Core) copies every file directly inside `jb/`, `mv/` and `singer/` (managers and `_` mirrors, about 6 MB; the `gak`, `mv/data` and `voc/db` payloads are only ever added) into `managers/` of a timestamped snapshot, and `ExtDataWorkspace.CreateRestorePoint()` runs before each write. Payload files that an operation removes or rewrites (deleted song folders, scorefixed or translated packs) are copied into `payloads/` of that snapshot first, because a restored manager must not point at files that are gone. The Avalonia app keeps snapshots in `LocalApplicationData/Degausser/backups/<save>-<hash>` (never on the SD card), keeps the newest 30, skips a snapshot identical to the newest, and refuses to write if the backup fails. Restoring snapshots the current state first. `BackupDirectory` is null for library callers and tests unless set. `[test-verified on a real save copy: delete then restore is byte-identical]`

## Playable-video song packs

The song linked to a downloadable movie is not an ordinary BBP pack:

- The wrapper version is `0x00010003`, where ordinary packs are `0x00020001`. The wrapper starts with a descriptor count of 7, and descriptor 0 is the score.
- The score is gzip-compressed and expands to the **runtime working layout**: `0x1BD58` bytes, revision `0x00020002`.
- `ExpandedGakLayout.Contract()` applies the ELF-derived inverse of the runtime expansion ([bbp-format.md §17.7.1](bbp-format.md)). It produces the normal `0x14E3C` serialised GAK at revision `0x00020001`, which then plays through the ordinary GAK and MIDI path.

This is how **Preview track** works for movies. The MOFLEX itself is treated as opaque bytes. An early build launched `ffplay` on it, but real BBP movies use a DSP-ADPCM audio stream (MOFLEX codec 4) that stock FFmpeg can't decode, so that path was removed.

## BBPM and BBPV packages

Portable, uncompressed, deliberately simple. There is no ZIP or JSON layer, so payloads can be streamed and preserved byte-for-byte.

```text
BBPM 1\n                        magic, space, version
<decimal length>\t<name>\n       one line per entry
…
\n                              blank line ends the header
<payload bytes, in header order>
```

The payload lengths must sum exactly to the rest of the file. Names are unique.

**BBPM v1** (one movie and its playable song):

| Entry | Source in extdata |
|---|---|
| `jb/mgr2.item` | the song's JbMgr2 item |
| `mv/mgr.item` | the movie's MvMgr item |
| `jb/gak/pack` | `jb/gak/<song-id>/pack` |
| `jb/gak/adpcm` | `jb/gak/<song-id>/adpcm`. **Optional**, present only if the source has one |
| `mv/data/movie` | `mv/data/<video-id>` |

`adpcm` is a separate GAK sidecar, *not* the audio embedded in the MOFLEX. It was added without bumping the version because the framing has no fixed entry count, so four-entry packages stay valid. No synthetic sidecar is ever generated.

**BBPV v1** (one downloadable singer): `singer/mgr2.item`, `singer/pack`, `singer/thumb`, and exactly one `voc/db/dbNN.bin` plus its `.ddi` and `.ddb`. On import, `singer/mgr.bin`'s legacy item is regenerated from the mgr2 item using the recovered save-side conversion (`SingerMgr2.Item.ToLegacyItem()`).

### Import rules

- Import **refuses** conflicting movie, song or voice-DB IDs instead of rebasing them. Rebasing hasn't been proven safe.
- On failure, partially copied GAK and movie payloads are deleted before any manager entry is published.

## English translations of downloadable content

`src/Degausser.Core/Translations/english-bbpm-bbpv-v1.json` is an embedded catalogue: 40 movies keyed by video ID and 10 singers keyed by artist.

- **Optional** via 3DS Content → *Use English titles and lyrics* (on by default; it also controls English lyrics for songs, when a scorefix folder is loaded).
- A translation is applied only if the package's current Japanese text **exactly matches** the catalogue's `expected*` fields. Anything else imports unchanged.
- Translation rewrites manager metadata in memory during import. Original `.bbpm` / `.bbpv` files, MOFLEX and voice databases are untouched.
- **A movie's song pack is translated too.** The title shown while a movie plays, and its karaoke, come from the pack and not the managers; before this, only the lists were English. A song with a scorefix gets its romaji title and lyrics (the manager titles then follow the scorefix); one without (the medleys have no karaoke) gets the translated title in the pack. A scorefix that doesn't fit is skipped. Playable-video packs (`0x00010003`) are patched in place (`PlayableVideoPack`: the title and karaoke are written into the expanded score and every other payload is kept), because rewriting them as ordinary packs is not known to be playable.
- **Movies already in a save** are handled by 3DS Content → *Translate movies already in this save to English…* (or `Degausser.Corpus translate-movies <extdata> [--write]`). A movie whose pack has no Japanese title or lyrics left is skipped, so it can be repeated. Evidence: `[corpus-inferred]` from the 40-movie donor extdata; not yet run on hardware.
- Names of Flipnote and *Dream* contest contributors in a few descriptions (タカヒロ, んば, …) are left as the translators' handles.
- The singer artist field holds **7 UTF-16 units**. That is why `あおきラピス`, `カムイがくぽ` and `アルスロイド` become `Lapis`, `Gackpo` and `ARSLOID`, with the full names kept in the greetings.
- Display widths are checked against the karaoke `cbf_std` font (a maintenance script outside this repo). The real movie and singer UI font and pane sizes are unconfirmed.
