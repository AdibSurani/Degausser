# Daigasso! Band Brothers P `.bbp` format

**Updated:** 2026-09-07

**Status:** archaeology closed for practical parser/converter purposes. The fixed BBP/GAK score format and the playback-relevant structures needed for high-fidelity BBP → MIDI are understood well enough that the remaining unknowns are editor/catalogue naming details, exact Nintendo DSP/sample-engine behaviour, and optional Vocaloid internals. Evidence combines the original Degausser `master`, all six GitHub forks, original issues/notes, paired DX→P migrations, direct reverse engineering of the stripped Band Brothers P update ELF (`SHA-256 9e1aea3dee9038361f22b9ed56a976fa89c287ea19422ca099aaf6075493830`), and a statistical sweep of **9,722 official BBPs / 97,220 ChannelInfo records**.

This document is intended to be implementation-oriented: a developer should be able to write a parser/converter from it without having to understand the original Degausser source. Fields whose semantics are not yet established are kept explicitly unknown rather than given confident names.

## 0. Conventions

- Multi-byte integers are little-endian unless stated otherwise.
- `u8`, `i8`, `u16`, `i16`, `u32`, `i32`, `u64`, `i64` have their usual meanings.
- Strings in the song metadata and GAK payload are fixed-size UTF-16LE arrays.
- Offsets in the GAK section are relative to the beginning of the **uncompressed GAK payload**.
- Offsets in the pack directory are relative to the beginning of the **pack header**, not the beginning of the `.bbp` file.
- A BBP file consists of a 312-byte catalogue record followed by a 68-byte pack directory and one or more compressed payloads.

Confidence labels used below:

- **Confirmed** — used successfully by Degausser and/or structurally exact across the known implementation.
- **High confidence** — strongly correlated against independent ROM/resource data.
- **Tentative** — plausible old reverse-engineering interpretation, not yet independently verified.
- **Unknown** — structure is known but semantics are not.

Evidence priority used in this document:

1. direct behaviour observed in the game ELF, especially constructors, importers, migration routines, and field consumers;
2. behaviour implemented by the original Degausser and independently exercised by later working tools;
3. exact binary structure in original source;
4. independent resource/ROM data and old differential spreadsheets;
5. later forks that successfully import/export/edit songs;
6. old comments, names, and hypotheses;
7. downstream heuristics (kept explicitly non-normative).

A field name from old source is **not** automatically promoted to fact. Where later evidence contradicts an old name, this specification uses the better-supported interpretation and records the discrepancy.

---

# 1. Top-level BBP layout

```text
+0x000  JbMgr.Item         0x138 (312) bytes
+0x138  PackHeader         0x044 (68) bytes
+...    compressed GAK     variable size
+...    optional Vocaloid  variable size
```

The first 312 bytes are the same record type used by the game's `jbmgr` catalogue. Degausser then parses the remainder exactly as a standalone `pack` file.

## 1.1 `PackHeader`

The pack header is exactly 17 little-endian `u32` values (68 bytes). The most useful representation is the explicit word table below rather than assuming all 16 post-version words are active payload descriptors.

```text
Word  +Offset  Type  Meaning
0     0x00     u32   version / format word; writers use 0x00020001
1     0x04     u32   GAK-present/constant; writers use 1
2     0x08     u32   GAK uncompressed size
3     0x0C     u32   GAK compressed-stream offset, from PackHeader start
4     0x10     u32   GAK compressed size
5     0x14     u32   Vocaloid-present flag (0 or 1)
6     0x18     u32   Vocaloid uncompressed size
7     0x1C     u32   Vocaloid compressed-stream offset, from PackHeader start
8     0x20     u32   Vocaloid compressed size
9..16 0x24..43 u32  reserved / unknown; Degausser writers normalize to zero
```

The original C# implementation can be viewed as `version + four 4-word descriptors`, but only the first two descriptor-shaped groups have observed semantics. The later `wangyu-/Degausser` importer explicitly models words 9..16 as an eight-word unused/reserved region and zeroes them before writing. Do not invent meanings for parts 2 and 3 without new evidence.

For ordinary BBP files:

```text
word[0] = 0x00020001
word[1] = 1
word[2] = 0x14E3C          // 85,564-byte GAK
word[3] = 0x44             // first gzip stream immediately follows header
word[4] = compressed GAK size

if Vocaloid exists:
    word[5] = 1
    word[6] = Vocaloid uncompressed size
    word[7] = 0x44 + align4(word[4])
    word[8] = compressed Vocaloid size
else:
    word[5] = 0
```

### 1.1.1 Compression

Both known payloads are gzip streams. The original Windows code uses `.NET GZipStream` and then patches gzip header bytes 8 and 9 to match game-produced streams. The original 3DS implementation likewise uses gzip-compatible miniz code.

For maximum compatibility, a writer should emit a conventional gzip stream and reproduce the game/Degausser header style where practical. A reader should use a normal gzip decoder rather than depending on cosmetic gzip-header fields.

When Vocaloid is present, its stream starts at a 4-byte-aligned offset after the compressed GAK.

### 1.1.2 Opaque Vocaloid preservation

The Vocaloid payload does **not** need to be understood in order to edit the main GAK safely. `wangyu-/Degausser` has a fast path that decompresses/recompresses only the main GAK and copies the already-compressed Vocaloid bytes unchanged to the rebuilt aligned location.

A round-trip-capable library should therefore retain both:

- decoded GAK bytes/fields; and
- the original raw compressed Vocaloid stream when it does not intend to edit Vocaloid data.

This is stronger than merely saying “Vocaloid is unknown”: an implementation can preserve it losslessly as an opaque payload.

---

# 2. `JbMgr.Item` — 312-byte catalogue metadata

**Confirmed structurally.**

```text
Offset  Size  Type       Field
0x000   4     u32        title_id
0x004   4     u32        other_id
0x008   4     bitfield   flags
0x00C   2     i16        singer
0x00E   2     i16        icon
0x010   102   UTF-16LE   title[51]
0x076   102   UTF-16LE   title_simple[51]
0x0DC   36    UTF-16LE   author[18]
0x100   4     u32        video_id
0x104   50    u8[50]     scores
0x136   2     i16        zero_padding
0x138                  end
```

`title_simple` is the song list's sort and index key: the game files a song under the letter or kana row its first character falls in. Across the 9,722 official BBPs its first character is katakana (7,170: the reading, without dakuten, small kana made full-size), lowercase ASCII (2,455: `ＳＴＡＲＴ` → `start`), a digit (86) or another symbol (11), and **never uppercase ASCII**. A song whose `title_simple` starts with a capital is filed under symbols (hardware-observed on a save with scorefixed songs). Latin may keep or drop its spaces (`partita b dur bwv 825`, `somedayofmylife`). Degausser writes it with `JbMgr.Item.SimpleTitle` (one line, full-width Latin folded to ASCII, lowercase) when it sets a title: scorefixes and BDX conversion. It doesn't attempt kana readings. `[corpus] [hardware]`

Known `singer` values:

```text
 -1  none
 -2  original male
 -3  original female
```

Known `icon` values in Degausser:

```text
0 none
1 heart
2 skull
3 music
4 warning
```

### 2.0.1 Empty-slot sentinel

`title_id == 0xFFFFFFFF` is an empty catalogue slot. This was present in the old XBKJ notes and corpus, and is now **confirmed directly by the ELF**: the `JbMgr.Item` initializer at `0x0017F80C` zeros the record and writes `0xFFFFFFFF` at `+0x000`, with `singer = -1` and `video_id = 0`. `[ELF]`

## 2.1 `flags`

Degausser's names are a mixture of established behaviour and old guesses. Keep that distinction in new code.

| Bit(s) | Degausser name / interpretation | Status |
|---:|---|---|
| 0 | entry valid/present | **Confirmed** — both original and later 3DS code require it before processing an item |
| 1 | `OnSD` / pack stored on SD extdata | **Confirmed/high** — original and later 3DS code require it before opening/deleting the corresponding pack |
| 2 | expected 0 in Degausser validity check | structural observation |
| 3..7 | `Parts` / nonblank-part count (5-bit value) | **Confirmed structurally by ELF** — setter at `0x0017E800` clears mask `0xF8` and inserts `value << 3`; old Degausser `%16` truncates the fifth bit |
| 8 | `HasMelody` | **Confirmed/high** — current-GAK importer sets it when signed `GAK+0x0207 >= 0` |
| 9 | `IsSingable` | likely |
| 10 | `IsClassic` | **Confirmed/high** — GAK `+0x0206` matches this bit in 9,721/9,722 official BBPs; the set songs are overwhelmingly Nintendo's classical/public-domain catalogue |
| 11 | `HasLyrics` | **Confirmed/high** — ELF checks the lyric string and karaoke timer rather than trusting metadata |
| 12 | old Degausser `IsReceived` | **Structural relation confirmed; semantic name tentative** — importer derives it from `GAK+0x0204 bit 0` |
| 13 | `HasFans` | tentative/likely; notably cleared by portable-BBP export normalization |
| 14 | `HasGuitar` | **Confirmed by ELF** — derived from channel play types |
| 15 | `HasDrum` | **Confirmed by ELF** — derived from channel play types |
| 16 | `HasPiano` | **Confirmed by ELF** — derived from channel play types |
| 17 | `IsThreeFourMeter` / 3/4 meter | **Confirmed/high** — native import/build paths set this for meter value 3; old `HasInstrX` name was wrong |
| 18..19 | expected 0 by Degausser validity check | unknown; old comment suggests download-related |
| 20 | `HasVocals` | high confidence; Degausser expects this to equal bit 9 |
| 21 | `IsOnlineable` | tentative |
| 22 | expected 0 by Degausser validity check | unknown |
| 23 | `IsHidden` | **Confirmed/high** — source name plus old bit-correlation spreadsheet explicitly labels `0x800000` as hidden |
| 24..31 | outside the low 24-bit normalized portable subset | semantics not specified here |

Degausser considers an entry valid when:

```text
(flags & 0x004C0005) == 1
and bit9 == bit20
```

This validation predicate should not be mistaken for a complete definition of the bitfield.

### 2.1.1 ELF-derived flag construction

The current `0x14E3C` GAK importer constructs several catalogue flags from song data rather than merely copying a stored flag word. Observed relationships include:

```text
number of nonblank channels      -> bits 3..7 (`Parts`, 5 bits)
(i8)GAK[0x0207] >= 0             -> bit 8 (`HasMelody`)
external singing state           -> bit 9
lyrics/timing actually present   -> bit 11 (`HasLyrics`)
GAK[0x0204] bit 0                -> bit 12 (old name `IsReceived`)
any Guitar channel               -> bit 14
any Drum channel                 -> bit 15
any Piano channel                -> bit 16
```

`[ELF]` This is useful when synthesizing catalogue metadata from a GAK: several outer flags are **derived properties** of the song payload, not independent BBP content.

### 2.1.2 Portable export normalization

Both the original Windows/3DS code and later 3DS work intentionally sanitize catalogue metadata when making a portable `.bbp`:

```text
scores[:] = 0
singer    = -1
icon      = 0
flags    &= 0x007FDFFF
```

The mask clears bit 13, bit 23, and all bits 24..31 while retaining most musical/content flags. Treat this as **Degausser export policy**, not a parser requirement: a lossless editor should normally preserve the original item unless the caller asks for portable normalization.

## 2.2 Song-ID consistency

The outer catalogue `title_id` and GAK `+0x0004` song/title ID represent the same operational identity in later tooling. `wangyu-/Degausser` changes a song ID by writing **both** fields before rebuilding the BBP.

Recommended invariant for a writer:

```text
JbMgr.Item.title_id == GAK.title_id
```

If an editor changes a song ID, update both locations. A tolerant reader may report mismatches instead of rejecting the file.

---

# 3. GAK payload overview

The GAK payload is exactly `0x14E3C` (85,564) bytes in the ordinary packed P format documented here. This size is not padding-derived: it is the exact sum of the fixed fields below, and the ELF repeatedly requests/validates exactly `0x14E3C` bytes when loading the main pack payload. `[code] [ELF]`

```text
Offset   Size       Description
0x00000  0x020C     GAK header / song-wide metadata
0x0020C  0x01E0     10 × ChannelInfo (48 bytes each)
0x003EC  0x5DC0     10 × note streams (2400 bytes each)
0x061AC  0x2EE0     10 × packed per-score note-strength arrays (600 u16 each; solved structure)
0x0908C  0x0200     tempo changes (128 × TimeValuePair)
0x0928C  0x2800     volume changes (10 × 256 × TimeValuePair)
0x0BA8C  0x1400     range changes (10 × 128 × TimeValuePair)
0x0CE8C  0x1400     tone-preset A/B changes (10 × 128 × TimeValuePair; high confidence)
0x0E28C  0x0050     10 × PanState tuples (8 bytes each)
0x0E2DC  0x0080     guitarOrig[32] (i32)
0x0E35C  0x0420     guitarTimer[48] (22 bytes each)
0x0E77C  0x12C0     guitarNoteMeta[2400] (u16; solved)
0x0FA3C  0x00F0     pianoPair[120] (2 bytes each)
0x0FB2C  0x0100     pianoOrig[64] (i32)
0x0FC2C  0x1770     karaoke lyrics (3000 UTF-16 code units)
0x1139C  0x1770     karaoke timing (3000 i16)
0x12B0C  0x19C8     11 × 600-byte clef/key state timelines
0x144D4  0x04B4     piano chord-change count + 300 TimeValuePair
0x14988  0x04B4     guitar chord-change count + 300 TimeValuePair
0x14E3C             end
```

---

# 4. GAK header (`0x0000..0x020B`)

```text
Offset  Size  Type       Field
0x0000  4     i32        version                  // Degausser BDX converter emits 0x00020001
0x0004  4     i32        title_id
0x0008  4     i32        date_created
0x000C  4     i32        date_modified
0x0010  500   UTF-16LE   title[250]
0x0204  1     u8         unknown_0204        // bit 0 feeds outer catalogue bit 12
0x0205  1     u8         reserved_0205       // 0 in all 9,722 official BBPs
0x0206  1     u8/bool    is_classic          // matches outer IsClassic in 9,721/9,722 official BBPs
0x0207  1     i8         main_melody_channel // -1 means no designated melody; nonnegative channel index
0x0208  1     u8         lines_plus_6
0x0209  1     u8         time_signature
0x020A  1     i8         master_gain         // signed; 3DS ALL multiplier = 1 + raw/128
0x020B  1     u8         reserved_020B       // 0 in all 9,722 official BBPs
```

`line_count = lines_plus_6 - 6` is used throughout Degausser.

Old spreadsheet correlations show small channel-like integers and `0xFF` (`-1`) at `0x0207`. The ELF materially strengthens this: the current-GAK importer reads this byte as **signed** and sets outer `HasMelody` when it is nonnegative, while format-migration code deliberately preserves the field across revisions. Therefore `-1 = no designated melody/main channel` is confirmed at high confidence. Treating the nonnegative value as an actual channel index is still one step short of fully confirmed until a playback/editor consumer is traced, so `main_melody_channel` remains descriptive rather than a recovered Nintendo symbol. `[empirical corpus] [ELF]`

The song's internal timeline has **48 ticks per line**. Degausser asks all change trackers for `line_count * 48` ticks.

`0x0206` is the inner classic/public-domain catalogue flag. In the official corpus the cross-tab against outer `JbMgr.flags bit 10` is:

```text
GAK +206=0, outer IsClassic=0 : 8872
GAK +206=1, outer IsClassic=1 :  849
GAK +206=0, outer IsClassic=1 :    1   // known outlier: 四季
GAK +206=1, outer IsClassic=0 :    0
```

`0x020A` is a signed song-wide ALL/master gain/normalisation byte. Real-3DS editor ground truth establishes the visible multiplier as:

```text
master_multiplier = 1 + (i8)gak[0x020A] / 128
                  = (128 + (i8)gak[0x020A]) / 128
```

For example, raw `+43` is `1.3359375x` and is displayed by the game as `1.33x`. Degausser's older `(raw + 64) / 64` MIDI projection is obsolete.

The corpus distribution remains useful context for the signed interpretation: **9,718/9,722** official values are within signed `-64..+64`; native-P songs have median `+64`; only four DX migrations exceed `+64` (`68, 74, 80, 80`). See the note-strength section below for the recovered native attack stage and `midi-playback.md` for Degausser's practical MIDI composition. None of this claims that Nintendo's final DSP amplitude curve is literally MIDI velocity.

`0x0205` and `0x020B` are zero in all 9,722 official BBPs. Preserve arbitrary values in a lossless parser, but official-content tooling can treat them as reserved-zero fields.

---

# 5. `ChannelInfo` — 48 bytes × 10

Located at GAK `0x020C + channel*48`.

```text
+0x00  u8    play_type
+0x01  u8    instrument_id
+0x02  u8    volume
+0x03  u8    clone_id
+0x04  u8    master_pro_stars      // low=Pro, high=Master
+0x05  u8    amateur_beginner_stars// low=Beginner, high=Amateur
+0x06  u8    note_strength_curve   // 0..2; selects one of three four-level curves
+0x07  u8    performance_param_07  // live P/editor parameter; audio relevance not found
+0x08  u8    performance_param_08  // derived representation of +07 for non-drums; drums use 0
+0x09  u8    difficulty_metadata_09// gameplay metadata; exact label not recovered
+0x0A  u8    vocal_difficulty      // low nibble = Vocal stars; high nibble still metadata/flags
+0x0B  u8    reserved_0B           // 0 in all 97,220 official ChannelInfo records
+0x0C  u32   reserved_0C           // 0 in all 97,220 official ChannelInfo records
+0x10  16B   tone_params_A
+0x20  16B   tone_params_B
```

`play_type`:

```text
0 Standard
1 Drum
2 Guitar
3 Piano
```


### 5.0.1 Corpus behaviour of the formerly unknown `+07..+0F` block

The 9,722-song official corpus makes most of this block low priority:

- `+0B` is zero in **97,220/97,220** ChannelInfo records.
- `+0C..+0F` are zero in **97,220/97,220** records.
- `+07/+08` are live P-only performance/editor parameters but behave as two encodings of essentially one setting. Across **72,603 active non-drum parts**, all but one obey:

```text
if +07 <= 10:  +08 = +07
else:          +08 = +07 / 2 + 5
```

The sole official exception is part 9 of `ＭＩＬＫ (Ｎｉｎｔｅｎｄｏ)` (`+07=10`, `+08=5`). For **all 10,564 active drum parts**, `+08=0`.

Nintendo DX migrations overwhelmingly use `+07=8,+08=8` for Standard/Guitar/Piano parts and `+07=8,+08=0` for drums. Native-P songs use the wider `+07=0..30` / `+08=0..20` domains. No playback/DSP dependency was established, so a MIDI converter may safely preserve these bytes without interpreting their user-facing name.

`+09/+0A` sit with the difficulty/gameplay interface. Native code confirms `low(+0A)` as the fifth/Vocal difficulty rating. Their remaining bits are not required for audio fidelity.

## 5.1 Tone parameter block — major archaeology result

Degausser originally represented each 16-byte block as a 12-byte `SoundEnvelope` plus a completely unknown 4-byte integer. The uploaded `inst_def.xml` independently names the preset parameters, and an Ocarina record matches byte-for-byte.

**High confidence layout:**

```text
+0x00 u8   attack         // ar
+0x01 u8   decay          // dr
+0x02 u8   sustain        // sl
+0x03 u8   release        // rr
+0x04 u8   shape          // shp
+0x05 u8   hold           // hld
+0x06 u8   delay          // dly
+0x07 u8   speed          // spd
+0x08 u8   depth          // dep
+0x09 u8   target
+0x0A u8   dual
+0x0B u8   dual_rate
+0x0C u8   biquad_type
+0x0D u8   biquad_value
+0x0E i16  lpf_value
```

This corrects several names in the decade-old Degausser source:

```text
Degausser byte 7  `depth`         -> speed
Degausser byte 8  `speed`         -> depth
Degausser byte 9  `zero`          -> target
Degausser byte 10 `effectorType`  -> dual
Degausser byte 11 `effectorValue` -> dual_rate
Degausser unknown i32 tail         -> biquad_type, biquad_value, lpf_value
```

### Ocarina proof fixture

One old ChannelInfo row for instrument ID `0x20` (Ocarina) contains this second 16-byte block:

```text
62 02 3C 46 01 14 0A 27 1E 00 03 0A 01 19 00 00
```

The independently extracted Ocarina preset is:

```text
ar=98, dr=2, sl=60, rr=70,
shp=1, hld=20, dly=10, spd=39, dep=30, target=0,
dual=3, dual_rate=10,
biquad_type=1, biquad_value=25, lpf_value=0
```

The values are an exact byte-for-byte match (`98=0x62`, `60=0x3C`, `70=0x46`, `39=0x27`, `30=0x1E`, `25=0x19`).

**Relationship of the two blocks:** now high confidence as **tone preset A / tone preset B**. P's composition editor supports two registered tone settings per part and up to 126 mid-song tone changes. The separate 128-entry timeline at `0x0CE8C` fits `initial preset + 126 changes + terminator` exactly. DX has only one tone setting, so native DX migrations normally duplicate the same 12-byte DX tone into both P blocks and initialise the selector timeline to preset A at time 0. A controlled native P file switching A/B is still desirable to prove the exact selector values. `[P documentation] [paired corpus] [inference]`

---

# 6. Note streams

There are 10 note streams, each 2400 bytes:

```text
channel_notes[ch] = GAK[0x03EC + ch*2400 .. +2400]
```

The meaningful prefix for a song is:

```text
line_count * 16 bytes
```

because one line comprises four 4-byte groups, each representing 12 ticks:

```text
1 group = 4 bytes = 12 ticks
1 line  = 4 groups = 16 bytes = 48 ticks
max 150 lines because 2400 / 16 = 150
```

## 6.1 Standard / melodic channels

Each 4-byte group is normally four events spaced three ticks apart:

```text
b0 at +0 ticks
b1 at +3 ticks
b2 at +6 ticks
b3 at +9 ticks
```

If `b0 == 0xFF`, the first byte is a sentinel and the remaining three events are spaced four ticks apart:

```text
0xFF, b1, b2, b3
      +0  +4  +8 ticks
```

For an event byte `< 0x80`:

- release the previously held note at that event time;
- `0x00` means no new note / rest after release;
- `0x01..0x7F` starts that note value.

Bytes `>= 0x80`, apart from the `0xFF` subdivision sentinel, are not handled by Degausser's standard-note decoder and need corpus investigation before assigning semantics.

## 6.2 Drum channels

A drum event byte contains two independent 4-bit lanes:

```text
low_nibble  = byte & 0x0F
high_nibble = byte >> 4
```

Each nibble is decoded with the same 4-event / 3-event subdivision rule, using `0xF` as the sentinel. Non-zero nibble values index a drum mapping for the selected drum kit.

This permits two simultaneous percussion hits in one event byte.

## 6.3 Guitar and Piano channels

The byte stream exists in the same 2400-byte per-channel region. The historical Degausser player left Guitar and Piano decoding unfinished; the current Core MIDI projection now combines the later recovered chord/pair structures (sections 10–11) with that stream so both are audible. The historical limitation remains useful context when judging which playback behavior is reconstructed rather than directly observed from the old player.

---

# 7. `TimeValuePair` and automation/change arrays

```c
struct TimeValuePair {
    i16 time;
    i16 value;
}; // 4 bytes
```

Degausser treats these as sorted step changes: entry 0 supplies the initial value, and later entries become active exactly when the timeline reaches their `time`.

Mapped arrays:

```text
0x0908C  tempoChanges     128 entries total
0x0928C  volumeChanges    10 × 256 entries
0x0BA8C  rangeChanges     10 × 128 entries
0x0CE8C  tonePresetChanges 10 × 128 entries   // high-confidence identity; old name effectorChanges
```

`rangeChanges` remains a useful functional name: it stores playable range/root/key-layout state. Nintendo often canonicalises these during DX migration. `0x0CE8C` should no longer be described generically as effector automation; its size plus P's documented two-tone/126-change feature make a tone-preset A/B selector timeline the strongest interpretation. `[P documentation] [paired corpus]`


## 7.1 Timeline resolution and tempo

Original Degausser playback uses the GAK tempo-change values directly while scheduling one internal tick as:

```text
60 seconds / (tempo_value * 12)
```

Therefore, under the original implementation's interpretation:

```text
12 GAK ticks = 1 quarter note
48 GAK ticks = 4 quarter notes
```

This aligns with the note-stream grouping: a 12-tick group is one quarter-note-sized unit divided either into four 3-tick subdivisions or three 4-tick subdivisions.

`line_count * 48` is used as the total tracker length even when the song-wide `time_signature` byte is not 4. For that reason this document calls 48 ticks a **BBP line unit**, not unconditionally a musical 4/4 measure.

### Non-normative later MIDI-export heuristic

`edgarcantuco/DegausserMidi` applies `tempo_value * 10 * 1.19620253165` when writing Standard MIDI, and its own source says the final factor was derived empirically from expected lengths of a few songs. That is useful evidence about the downstream exporter, but it is **not** evidence that BBP stores tempo using that formula. Do not encode that multiplier into a BBP parser. The original Degausser player uses the value directly as described above.

---

# 8. Packed per-score note strength (`0x061AC`)

At `0x061AC`:

```c
u16 noteStrengthPacked[10][600];
```

Total size: 12,000 bytes. Each channel has 2,400 score positions and therefore exactly one four-bit nibble per score byte:

```text
600 × 16 bits = 2400 × 4 bits
```

Nibble order inside each little-endian stored `u16` follows score position order from most-significant to least-significant nibble:

```text
bits 15..12 -> score position 4n+0
bits 11..8  -> score position 4n+1
bits 7..4   -> score position 4n+2
bits 3..0   -> score position 4n+3
```

For Standard/Piano/Guitar playback, the **low two bits** are the four note-strength codes:

```text
0 weakest
1 weak
2 normal/reference
3 strong
```

`ChannelInfo +0x06` selects the native four-level curve:

```text
curve 0:  70, 85, 100, 115
curve 1:  50, 75, 100, 125
curve 2:  80, 90, 100, 110
```

The native four-level value is recovered, but mapping it directly onto generic MIDI velocity is necessarily an approximation: Band Brothers can use attack/intensity to affect timbre as well as loudness. Degausser therefore exposes an intensity-effect amount `effect` in `0..1` and compresses the native value toward neutral `100` before the MIDI projection:

```text
native_strength    = curve[strength]
effective_strength = 100 + (native_strength - 100) * effect
attack_velocity     = floor(effective_strength * channel_volume / 127)
master_term         = 128 + (i8)gak[0x020A]
midi_velocity       = clamp(volume_change * attack_velocity * master_term >> 15, 0, 127)
```

`effect=0` ignores note intensity, intermediate values compress it, and `effect=1` uses the full recovered curve. The native strength codes and curve values are format facts; the interpolation and final conversion to MIDI velocity are Degausser export policy, not a claim about Nintendo's final sample/DSP amplitude law.

In native-P melodic/Guitar/Piano attacks, the 9,722-song corpus found no use of the nibble's upper two bits: occupied native attack values stay in `0..3`. DX-migrated material commonly contains `0xF` nibbles, so those upper bits should be preserved as migration/reserved metadata rather than assigned a new P feature without evidence.

For drums the full nibble is meaningful as **two independent two-bit strength lanes**, matching the two simultaneous drum nibbles stored in each score byte. Exact physical lane/pad naming is a drum-mapping concern, not a format-structure unknown.

The historical `SomeMask` name is obsolete. A lossless implementation should preserve all four bits even when a melodic renderer only consumes the low two.

# 9. Panning region

At `0x0E28C`, 10 entries × 8 bytes:

```c
struct PanState {
    i16 zero_0;      // observed 0 in native/migrated samples
    i16 value;       // signed pan value
    i16 minus_one;   // observed -1 in native/migrated samples
    i16 zero_6;      // observed 0 in native/migrated samples
};
```

This region is now **high-confidence panning data**, supported independently by the historical BDX converter, the paired Nintendo DX→P migration corpus, and native P samples. In all 112 migrated DX channel records the tuple is exactly `(0, mapped_pan, -1, 0)`. The exact normal DX→P mapping is documented in §15.5.3. `[paired corpus] [BDX conversion]`

---

# 10. Guitar structures

```text
0x0E2DC  i32 guitar_orig[32]
0x0E35C  GuitarStuff guitar_timer[48]
0x0E77C  u16 guitar_note_meta[2400]
0x14988  i32 guitar_chord_change_count
0x1498C  TimeValuePair guitar_chord_change_table[300]
```

`GuitarStuff`:

```c
struct IndexRootPair {
    u8 index;
    u8 raw_root;
};

struct GuitarStuff {
    i16 time;
    IndexRootPair pair[10];
}; // 22 bytes
```

### `raw_root`

Degausser/old DX documentation interprets the low nibble as a C-major scale degree (`C D E F G A B`) and the high nibble as natural/sharp/flat selector. `0xFF` is a special/custom value.

The resource chord table represents a Guitar chord as six packed 5-bit fret values. Fret `<16` is sounding; values `>=16` are muted. Degausser's string order/tuning is:

```text
E4 B3 G3 D3 A2 E2 = {64, 59, 55, 50, 45, 40}
```

### Native-P chord IDs

The P ELF does not treat every pair as the DX-era `{ chord_type, raw_root }` fields. It first reads the two bytes as one little-endian 16-bit chord ID. Its generic resolver has a legacy resource-table path for the migrated-DX encoding and a native `ChordSet` catalogue path loaded from `rom:/snd/gt_chord.xml`. The actual Guitar playback path applies the user-chord special case below before calling that generic resolver.

The playback-side resolver has a separate user-chord branch for every category-7 ID (`id >> 13 == 7`). That branch ignores the remaining high-byte fields and indexes the custom chord array directly with the low byte. Since the GAK contains 32 `guitarOrig` entries, valid file references use low-byte slots `0x00`-`0x1F`. Two established encodings therefore resolve to the same storage:

```text
Native-P sample:  0xE200 .. 0xE21F  -> guitarOrig[0] .. guitarOrig[31]
DX -> P migration: 0xEF00 .. 0xEF1F -> guitarOrig[0] .. guitarOrig[31]
```

This ordering matters for `0xE20F` and `0xE21F`: checking only the low nibble incorrectly sends those two user chords through the legacy decoder. Native built-in IDs use the external `gt_chord.xml` catalogue. The supplied resource confirms the generic resolver's bit layout: note letter in bits 15..13 (`C`..`B`), accidental in bits 10..9 (`natural`, `#`, `b`), chord quality in bits 7..4 (`major`, `m`, `7`, `M7`, `m7`, `dim`, `m7b5`, `aug`, `sus4`, `7sus4`, `6`, `add9`), and zero-based voicing rotation in bits 3..0. `[ELF 0x1AD4BC-0x1AD6A8, 0x66156C-0x6615B4] [gt_chord.xml] [native P sample]`

### DX -> P pair conversion

Paired migrations establish:

```text
FF/FF                     -> FF/FF       empty
DX raw_root == FF          -> index/EF    custom/original chord
otherwise:
  P.index                  = DX.index * 16 + 15
  P.raw_root               = ROL8(DX.raw_root, 5)
```

For custom Guitar words:

```text
P.guitarOrig = DX.guitarOrig & 0x3FFFFFFF
```

### Guitar note codes

DX/P Guitar note bytes select one of eight chord buttons and stroke direction; `+0x80` is hold/continuation. Known attack codes:

```text
button   down   up
↓        05     07
←        09     0B
↑        0D     0F
→        11     13
L↓       19     1B
L←       1D     1F
L↑       21     23
L→       25     27
```

Nintendo's official DX migration often rebuilds/reorders the P Guitar palette and rewrites only the button part of these note codes. Stroke and hold semantics are preserved. This canonicalisation is **not required for storage/playback** if a converter preserves the DX palette ordering and the original button codes together. `[paired corpus]`

### `guitar_note_meta[2400]` at `0x0E77C` — solved

The old `unknownGuitar3[600] (i64)` model hid a simple per-note structure. It is 2400 little-endian `u16`s, exactly one per Guitar note-stream byte.

```text
0x8000       fresh strum / envelope retrigger
0x4000       direction: 1 = down, 0 = up
bits 0..5    per-strum string suppression mask
              bit 0 = high E ... bit 5 = low E; set bit = do not hit string
0x00FF       triplet-cell marker where applicable
0            rests / passive continuation positions where no fresh strum occurs
```

The metadata attack bit is authoritative for native P. A Guitar score byte with `+0x80` can still be a **fresh repeated strum** when metadata bit 15 is set; the high score bit is therefore not sufficient to classify a position as passive continuation. Every fresh strum ends the preceding ringing envelope and starts the new one at the same score tick. No artificial one-tick gap or per-string strum delay is implied. `[real-3DS Studio] [ELF/corpus]`

The six low bits are an **event suppression mask**, not the chord's muted-string state. A set bit means the pick/strum does not hit that physical string at all. This is independent of a packed chord fret in the range `16..31`, which represents a physically struck but damped/dead string. An all-six chord-muted voicing can therefore have event mask `0`.

Real-3DS listening further establishes that chord-muted strings are not silent: they retain string/pitch and instrument character but die very quickly. Degausser's current MIDI approximation uses `open_string + (packed_fret & 0x0F)` on the part's normal instrument and gates it to one BBP frame (40 MIDI ticks). The low-nibble fret interpretation and exact one-frame envelope are still empirical rather than ELF-proven.

The original migration reconstruction regenerated all 4,800 metadata bytes exactly in **32/32 Guitar migrations** in the targeted evidence corpus; later native-P Studio/corpus work recovered the retrigger and direction semantics above. `[paired corpus]`

For a media player, this is enough to render the actual struck subset, repeated arrows, damped strings and up/down ordering instead of treating Guitar as an opaque score type.

# 11. Piano structures

```text
0x0FA3C  IndexRootPair piano_pair[120]
0x0FB2C  i32           piano_orig[64]
0x144D4  i32           piano_chord_change_count
0x144D8  TimeValuePair piano_chord_change_table[300]
```

A nonzero Piano score byte below `0x80` is a **1-based index into `piano_pair[120]`**. It does not index `piano_orig` directly. The selected two-byte descriptor has two runtime forms.

For native-P generated chords, the packed fields recovered from the game ELF are:

```text
pair.index:
  bits 0..3  voicing rotation, centred on 7
  bits 4..7  chord quality 0..11

pair.raw_root:
  bit 0      second/drop voicing transform
  bits 1..2  accidental: 0 natural, 1 sharp, 2 flat
  bit 3      voice count: 0 = 3 voices, 1 = 4 voices
  bit 4      native marker in observed packed songs
  bits 5..7  diatonic root C,D,E,F,G,A,B; value 7 = original/custom
```

The twelve native quality families, in index order, are `major`, `minor`, `7`, `M7`, `m7`, `dim`, `m7b5`, `aug`, `sus4`, `7sus4`, `6`, and `add9`. ELF routine `0x001AD6BC` expands the corresponding 3/4-voice interval template, applies pitch normalisation, rotation and the optional drop transform, and returns literal pitches. Confirmed examples include `index/raw_root = 06/10` -> C voicing `G3 C4 E4` and `06/90` -> G voicing `G3 B3 D4`. `[ELF] [real-3DS Studio]`

When `(raw_root & 0xE0) == 0xE0` (root category 7), `pair.index` instead selects `piano_orig[64]`. Each `piano_orig[i]` is four literal note bytes packed into an `i32`. `F9` and `FF` are therefore not unique magic markers: observed native files use the wider `E0..FF` selector family. This same literal representation is also where the historical BDX→BBP converter materialises DX Piano chords.

For each active Piano chord-change `TimeValuePair`, its 16-bit `value` is still interpreted as:

```text
high byte = IndexRootPair.index
low byte  = IndexRootPair.raw_root
```

Degausser now implements both native-generated and original/custom Piano forms directly; a separate replacement `PianoChords.tbl` is not required for native P playback.

---

# 12. Karaoke lyrics and timing

```text
0x0FC2C  UTF-16LE lyrics[3000]
0x1139C  i16      timer[3000]
```

Karaoke is considered disabled by Degausser when:

```text
timer[0] == 0x3FFF
```

Unused timer entries are padded with `0x3FFF`.

For display timing, Degausser uses:

```text
tick = timer[i] & 0x3FFF
```

so the low 14 bits are the musical timeline position. The song timeline ends at:

```text
(line_count * 48) ticks
```

Native P's own karaoke normalizer establishes the high-bit behaviour:

```text
ordinary lyric character             -> time | 0x8000
LF / ASCII space / U+3000 full space -> time | 0xC000
```

Therefore `0x8000` is the **valid/active timing-entry marker**, while `0x4000` is an additional separator/spacing/grouping marker. The historical BDX→BBP converter also uses bit 14 when adjacent imported characters share a timestamp, so `0x4000` should be understood broadly as non-anchor grouping/separation metadata rather than a literal `isWhitespace` bit in every provenance path. Retiming selects `(timer & 0xC000) == 0x8000` as ordinary timing anchors. `[ELF] [BDX conversion] [code]`

Degausser caps interpolation between lyric anchors at 48 ticks per syllable/segment.

The uploaded lyric image named with title ID `2147624995` corresponds to the old song-ID list's `STARRY HEAVENS` entry and is useful as a future karaoke fixture.

---

# 13. Clef / key state timelines

At `0x12B0C`, the old C# declaration `KeySignature { i32 stuff[150]; }[11]` is type-misleading. The same 6,600 bytes are much better represented as:

```c
u8 state[11][600];
```

The 600-byte length is `150 × 4`, and DX's predecessor is nine 512-byte blocks (`120-ish lines × 4`, with spare capacity). `[code] [paired corpus]`

Current semantics:

```text
blocks 0..7   per-channel clef/notation state inherited from DX
block 8       P extra channel 8
block 9       P extra channel 9
block 10      global key-signature/transposition state inherited from DX block 8
```

Per-channel low state values observed/documented for DX include:

```text
0  treble clef
1  bass clef
2  treble clef +1 octave
```

Bit 7 marks an **explicit state change/initial state**. A byte without bit 7 continues the same low-7-bit state.

### Exact DX migration expansion rule

For each DX per-channel block 0..7:

1. copy the 512 source bytes;
2. force bit 7 on byte 0;
3. determine the low-7-bit state at the song's last used line;
4. fill P-only bytes 512..599 with that final state.

This reconstructed **384/384 channel blocks exactly** across the 48-pair targeted corpus. P blocks 8 and 9 use `0x80` at byte 0 and zero thereafter. `[paired corpus]`

DX global block 8 maps to P block 10 by the same expansion and is exact in most migrations. Nintendo sometimes canonicalises the global key state alongside its different P playable-range/button calculation; that optional recalculation is not required for a conservative BDX->BBP conversion.

# 14. Instrument IDs and MIDI playback mappings

`src/Degausser.Core/Midi/Instruments.xml` records Band Brothers instrument IDs and **Degausser's playback choices**. The MIDI values are not BBP file semantics and are not evidence that Nintendo used General MIDI internally. `[code]`

The old table is useful but should not be treated as authoritative audio mapping. A modern player should keep the BBP instrument ID/name separately from the chosen synth preset.

## 14.1 Direct melodic mappings: audit and proposed GM fallbacks

The table below uses **zero-based MIDI Program Change numbers**, i.e. the actual byte sent by the Windows/FluidSynth MIDI API.

| BBP | Name | Old | Proposed fallback | Assessment |
|---:|---|---:|---:|---|
| 1 | Piano | 1 | **0** | old value selects Bright Acoustic; Acoustic Grand is the natural name match |
| 2 | Electric Piano | 3 | **4** | old value is Honky-Tonk; GM Electric Piano 1 is 4 |
| 3 | Rock Organ | 18 | 18 | exact GM name match |
| 4 | Synth Lead | 81 | 81 | plausible Saw Lead |
| 5 | Synth Bell | 88 | **98** | old value is New Age Pad; Crystal is a better generic synth-bell fallback |
| 6 | Pipe Organ | 19 | 19 | Church Organ; good fallback |
| 7 | Folk Guitar | 25 | 25 | Acoustic Steel Guitar; good |
| 8 | Electric Guitar | 26 | 26 | Jazz Electric Guitar; reasonable generic electric |
| 9 | Distortion Guitar | 30 | 30 | exact |
| 10 | Rock Guitar | 28 | 28 | questionable but intentionally distinct; GM Muted Guitar |
| 11 | Pick Bass | 34 | 34 | exact |
| 12 | Synth Bass | 39 | 39 | Synth Bass 2; good |
| 13 | Acoustic Bass | 32 | 32 | exact |
| 14 | Strings | 44 | **48** | old value is Tremolo Strings; String Ensemble 1 is safer generic mapping |
| 15 | Violin | 40 | 40 | exact |
| 16 | Double Bass | 43 | 43 | Contrabass; exact |
| 17 | Harp | 46 | 46 | exact |
| 18 | Pizzicato Violin | 45 | 45 | Pizzicato Strings; good |
| 19 | Piccolo | 72 | 72 | exact |
| 20 | Flute | 73 | 73 | exact |
| 21 | Clarinet | 71 | 71 | exact |
| 22 | Oboe | 68 | 68 | exact |
| 23 | Soprano Sax | 64 | 64 | exact |
| 24 | Alto Sax | 65 | 65 | exact |
| 25 | Brass | 62 | **61** | old value is Synth Brass 1; Brass Section is the closer generic name match |
| 26 | Trumpet | 56 | 56 | exact |
| 27 | Trombone | 57 | 57 | exact |
| 28 | Horn | 60 | 60 | French Horn; exact |
| 29 | Tuba | 58 | 58 | exact |
| 30 | Harmonica | 22 | 22 | exact |
| 31 | Pan Flute | 75 | 75 | exact |
| 32 | Ocarina | 79 | 79 | exact |
| 33 | Vibraphone | 11 | 11 | exact |
| 34 | Marimba | 12 | 12 | exact |
| 35 | Timpani | 47 | 47 | exact |
| 36 | Steel Drum | 114 | 114 | exact |
| 37 | Chorus | 91 | **52** | old value is Choir Pad; Choir Aahs is the closer acoustic chorus fallback |
| 38 | Shamisen | 106 | 106 | exact |
| 39 | Koto | 107 | 107 | exact |
| 40 | Shakuhachi | 77 | 77 | exact |
| 41 | NES A | 87 | **80** | old value is Bass & Lead and is a poor chip approximation; Square Lead is a better portable fallback, but a pulse/square oscillator is preferable |
| 131 | Harpsichord | 6 | 6 | exact |
| 132 | Accordion | 21 | 21 | exact |
| 133 | Mute Trumpet | 59 | 59 | exact |
| 134 | Music Box | 10 | 10 | exact |
| 135 | Banjo | 105 | 105 | exact |
| 136 | Square Lead | 80 | 80 | exact |
| 137 | Spanish Guitar | 24 | 24 | Nylon Guitar; exact intent |
| 138 | Clean Guitar | 27 | 27 | exact |
| 139 | Overdrive Guitar | 29 | 29 | exact |
| 140 | Slap Bass | 36 | 36 | Slap Bass 1; exact intent |

The proposed changes above are **player fallbacks**, not BBP-format changes. Listening tests against real P audio should override name-only guesses.

## 14.2 P-specific melodic IDs 161..171: best current guesses

P's instrument list confirms these are real P additions. Old Degausser mapped all of them to MIDI program 0, which is only a placeholder.

| BBP | P name | Proposed GM fallback | Confidence / note |
|---:|---|---:|---|
| 161 | Electro Bass | **33** Finger Bass | medium; distinguishes it from Pick Bass 34 |
| 162 | Synth Pad | **89** Warm Pad | medium; generic pad fallback |
| 163 | Fat Lead | **81** Saw Lead | medium; likely closer than a piano |
| 164 | Sine | **custom sine oscillator**; GM fallback 80 | high that plain GM is inadequate; GeneralUser GS has an actual Sine Wave at bank 8 / PC 80 |
| 165 | NES B | **custom chip pulse/square**; fallback 80 | high that GM is inadequate; community P experimentation describes B as closely reproducible by layered NES/Famicom A voices |
| 166 | NES C | **custom chip waveform**; fallback 80 | low/medium without a controlled listening fixture; keep it distinct at the synth layer rather than inventing a different GM acoustic timbre |
| 167 | NES Bass | **38** Synth Bass 1 | medium as GM fallback; P community analysis identifies the Famicom Bass timbre as triangle-wave-based, so a triangle oscillator is the better real implementation |
| 168 | Orchestra Hit | **55** | exact GM name match |
| 169 | Tenor Sax | **66** | exact GM name match |
| 170 | Cello | **42** | exact GM name match |
| 171 | Bassoon | **70** | exact GM name match |

For 164-167, a modern player should generate/load tiny custom oscillator presets rather than pretending General MIDI has equivalent NES/sine timbres. A GS-capable bank can improve a few cases without custom synthesis: GeneralUser GS provides `bank 1 / PC 80 = Square Wave` and `bank 8 / PC 80 = Sine Wave`. Its `bank 11 / PC 98 = Synth Chime` is also a compelling bank-specific candidate for BBP Synth Bell, better than the portable `bank 0 / PC 98 = Crystal` fallback. These banked choices cannot be represented in Degausser's current single-`Midi` XML field.

## 14.3 Alias ranges (GT / KB)

The old resource resolves these as aliases of existing base instrument entries:

| ID range | Type | Base-ID formula |
|---|---|---|
| 49-89 | `GT` | `base = id - 48` |
| 90-130 | `KB` | `base = id - 89` |
| 141-150 | `GT` | `base = id - 10` |
| 151-160 | `KB` | `base = id - 20` |
| 172-182 | `GT` | `base = id - 11` |
| 183-193 | `KB` | `base = id - 22` |

These IDs are primarily play-type variants of the underlying timbre. A player should preserve the BBP ID/play type for score semantics but can initially reuse the base synth preset.

## 14.4 P `GT_DX` aliases 197..212

`Instruments.xml` now carries the historical mapping as active `Type="GT_DX" ReplaceWith="..."` aliases, and the loader resolves `ReplaceWith` generically rather than hard-coding a subset:

```text
197 -> 34   Marimba
198 -> 8    Electric Guitar
199 -> 138  Clean Guitar
200 -> 11   Pick Bass
201 -> 140  Slap Bass
202 -> 12   Synth Bass
203 -> 13   Acoustic Bass
204 -> 5    Synth Bell
205 -> 16   Contrabass
206 -> 14   Strings
207 -> 37   Chorus
208 -> 27   Trombone
209 -> 25   Brass
210 -> 133  Muted Trumpet
211 -> 22   Oboe
212 -> 32   Ocarina
```

The alias retains its original BBP instrument ID/play type while inheriting the replacement instrument's MIDI program/drum mapping. At least `DX Guitar instrument 56 -> P 198` is directly confirmed by a Nintendo migration pair, and 198's replacement with base Electric Guitar is coherent. The remaining alias identities are still best treated as plausible historical mappings rather than individually ELF-proven semantics. `[paired corpus] [code]`

A robust reader should never crash on an unknown `u8` instrument ID; preserve and display it even when no friendly mapping exists.

## 14.5 Drum kits: current mapping audit

The old XML's comma-separated values are **per-pad MIDI percussion note numbers**, not MIDI program changes.

| BBP | Name | Old per-pad mapping | Audit |
|---:|---|---|---|
| 42 | Rock Drums | `0,38,35,37,54,42,48,47,50,46,57` | broadly plausible GM1 approximation |
| 43 | Electronic Drums | `0,40,35,51,54,42,48,47,50,46,57` | plausible notes, but timbre should come from an Electronic kit rather than only remapping pads |
| 44 | Synth Drums | `0,40,35,29,54,42,48,47,50,46,57` | note 29 is outside GM1; relies on GS/extended percussion semantics |
| 45 | Percussion Set | `0,38,41,81,58,52,54,38,54,55,57` | mixed orchestral/aux percussion; reasonable approximation |
| 46 | Bongo Set | `0,60,61,80,81,54,75,39,69,66,65` | mostly sensible GM/GM2 Latin/percussion choices |
| 47 | Conga Set | `0,63,64,62,58,55,82,85,56,77,76` | 63/64/62 are exact congas; 82/85 are GM2/GS extended percussion (Shaker/Castanets on common maps), not strict GM1 |
| 48 | Japanese Percussion Set | `0,62,54,64,55,59,56,62,64,60,61` | structurally playable but timbrally poor: mostly conga/bongo/cymbal substitutes for Japanese drums |
| 194 | Noise Drums | Rock map reused | definite placeholder |
| 195 | Jazz Drums | Rock map reused | definite placeholder |
| 196 | Pop Drums | Rock map reused | definite placeholder |

The original mappings therefore make more sense if the old Windows MIDI target is treated as **GS-ish**, not strict GM1: several percussion notes outside 35..81 are common GS/GM2 extensions.

### Current portable GM1 projection

Degausser's portable MIDI mapping is now kept in sync with the deployed BBP2MIDI web player. The stored pad order is:

```text
B, A, Y, X, Up, Down, Left, Right, L, R
```

The leading zero below is the unused score value 0 sentinel. Every selected percussion note stays inside the GM1 percussion range 35..81.

| BBP | Name | Portable mapping |
|---:|---|---|
| 42 | Rock Drums | `0,35,38,37,54,42,47,45,48,46,49` |
| 43 | Electronic Drums | `0,35,40,51,54,42,47,45,48,46,49` |
| 44 | Synth Drums | `0,35,40,37,54,70,47,45,48,46,49` |
| 45 | Percussion Set | `0,35,38,81,38,52,54,38,54,55,49` |
| 46 | Bongo Set | `0,61,60,80,81,54,75,39,69,66,65` |
| 47 | Conga Set | `0,64,63,62,58,55,64,67,45,56,76` |
| 48 | Japanese Percussion Set | `0,54,38,61,57,75,64,67,45,46,52` |
| 194 | Noise Drums | Rock portable map fallback |
| 195 | Jazz Drums | Rock portable map fallback |
| 196 | Pop Drums | Rock portable map fallback |

This is intentionally a **portable projection**, not a claim that these MIDI note numbers encode the game's native kit semantics exactly. SoundFont-specific/native-kit playback can make richer choices separately.

### Better modern strategy

Do not encode kit identity solely by choosing different note numbers on the same default drum kit. With a GS/GM2-capable SoundFont/synth, select an actual kit/preset first, then retain/refine the pad map.

Reasonable first choices:

```text
Pop Drums         Standard or Room kit
Rock Drums        Power/Rock kit
Electronic Drums  Electronic kit
Synth Drums       TR-808 / Synth kit
Jazz Drums        Jazz kit
Noise Drums       Electronic/SFX/custom noise kit
```

`Japanese Percussion Set` deserves a custom mapping/sample layer (taiko/kane/etc.) rather than increasingly clever GM substitutions. `Noise Drums` is also a strong custom-synthesis candidate: P community descriptions explicitly use its pads as white-noise-style Famicom percussion, so an SFX kit is only a fallback rather than the intended timbre.

## 14.6 Recommended SoundFont/synth direction

For updating Degausser itself:

- **GeneralUser GS 2.0.3 + FluidSynth** is the strongest first target. GeneralUser GS is current (2026), GM+GS compatible, has 261 melodic presets and 13 drum kits at roughly 30.7 MB RAM, and its own documentation rates FluidSynth 2.3+ as an excellent/fully compatible renderer. This is especially valuable for Degausser because the available kits include Power, Electronic, 808/909, Jazz, Brush, Orchestral and SFX rather than forcing every BBP drum set through one Standard kit.
- **MuseScore_General / MuseScore_General_HQ** is a good quality-first alternative and is a complete GM bank. The HQ/lossless variants are substantially larger; MuseScore's documentation specifically notes support for single-note dynamics in its General banks.
- **FluidR3** remains a useful legacy/tiny fallback but is no longer the quality target.
- **Timbres of Heaven** can sound excellent, but redistribution/licensing is less convenient for an open-source application.

For the synth engine, **FluidSynth** is the safest recommendation when paired with current GeneralUser GS because the bank depends heavily on SoundFont modulators and explicitly documents FluidSynth compatibility. **MeltySynth** is still architecturally attractive for Degausser because it is a lightweight pure-C#/.NET SoundFont synthesizer with no dependencies beyond .NET Standard 2.1, but GeneralUser GS 2.0.3 does not currently list MeltySynth among its verified compatible engines. Treat `MeltySynth + GeneralUser GS 2.0.3` as something to audition/test rather than an assumed-equivalent combination. A clean playback abstraction would let Degausser support a pure-managed engine and FluidSynth without coupling the BBP sequencer to either one.

Most importantly, the score decoder is now independent of SoundFont choice: Degausser resolves native/custom/DX Guitar chords, native/custom Piano chords, per-strum Guitar metadata, pan, and recovered gain/intensity before the MIDI/synth layer. A future SoundFont upgrade should therefore focus on timbre/kit fidelity rather than compensating for missing chord semantics.

# 15. Relationship to BDX and the historical BDX→BBP converter

Band Brothers DX `.bdx` is a substantially smaller, older fixed format, but it is a direct semantic ancestor of many P fields. The historical Degausser `BDXFormat` parses a **0x8000-byte BDX file** as follows:

```text
0x0000..0x0047   72-byte BDX container/header
0x0048..0x7F47   interpreted BDX/GAK-like structure (0x7F00 bytes)
0x7F48..0x7FFF   0xB8 bytes trailing/uninterpreted in Degausser
```

The `0xB8` value exactly explains the old note saying that 184 bytes were left unread. This is useful when comparing ELF format sizes: **raw BDX is 0x8000 bytes, not 0x13550, 0x1441C, 0x14E3C, or 0x1BD58**. `[code] [old note]`

BDX uses 8 channels, 2048-byte note streams, 32 tempo changes, smaller automation/chord tables, and only one piano-or-guitar chord system. P expands and reorganises this to 10 channels and the fixed `0x14E3C` packed GAK described above.

## 15.1 Historical Degausser BDX→BBP crosswalk

An older source revision contains a dedicated `BDX2BBP.cs`. This is not normative Nintendo behaviour, but it is unusually valuable provenance because it records the transformations the original tool deliberately chose when translating known DX semantics into P fields. `[BDX conversion]`

```text
BDX                                      -> P GAK / outer metadata
labels[3]                                -> title[250]
version                                  -> 0x00020001
created/modified timestamps              -> +0x0008/+0x000C
linesPlus6                               -> +0x0208
timeSignature                            -> +0x0209
masterVolume                             -> +0x020A
mainInstrument                           -> +0x020B  // historical converter only; now known wrong
32 tempo TimeValuePairs                  -> start of P tempo table

channel playType                         -> ChannelInfo +0x00
channel instrument                       -> ChannelInfo +0x01
channel volume                           -> ChannelInfo +0x02
channel cloneID                          -> ChannelInfo +0x03
channel masterProStar                    -> ChannelInfo +0x04
channel amaBegiStar                      -> ChannelInfo +0x05
channel envelope                         -> both P tone blocks
channel notes[2048]                      -> first 2048 bytes of P notes[2400]
channel panning                          -> PanState.value = panning * 2
DX channel-change volume                 -> first 32 P volume-change entries

nonblank channel count                   -> outer Parts
mainInstrument != 0xFF                   -> outer HasMelody (old converter guess)
hasKaraoke != 0                          -> outer HasLyrics
```

Important defaults inserted by the **historical** converter, using its old names:

```text
ChannelInfo +0x07 = 8
ChannelInfo +0x08 = 8 for ordinary DX-migrated non-drums; drums use 0  // native corpus baseline
NoteStrengths[ch][0..599] = 0xFFFF          // packed note-strength/migration metadata
for each channel: tonePresetChanges[ch].changes[1].time = -1
```

These lines are retained here as archaeology, not current terminology. The panning conversion is specifically known to be imperfect: original issue #1 says **negative panning does not convert properly**. Thus the region is confidently panning-related, but the `*2` transformation must remain compatibility evidence rather than a native scale definition.

## 15.2 Piano conversion

The original Japanese DX ARM9 has now independently confirmed the BDX Piano chord table and generator; see [research/2026-09-20-bdx-dx-rom-chords.md](research/2026-09-20-bdx-dx-rom-chords.md). DX has 13 built-in qualities and a song-global top-note / six-state voicing mode (3/4 voices × close/medium/wide), plus a previously undocumented `u32` custom-chord transform mask at BDX `0x66C8`.

The current converter first evaluates the exact DX sounding pitches. It then emits a native P generated descriptor only when P's ELF-backed same-root/same-quality generator produces the identical pitch multiset. Otherwise it materializes those exact pitches in `pianoOrig[next]` and points `pianoPair` at that literal slot. DX `m6`, wide layouts, and some DX 3-voice extended chords necessarily take the literal path. This retains the earlier evidence that `pianoOrig[i]` is four packed literal note bytes while avoiding unnecessary custom chords when the native P representation is genuinely equivalent.

## 15.3 Guitar conversion

For each DX guitar `IndexRootPair`, the historical converter applies:

```text
P.index   = DX.index * 16 + 15
P.rawRoot = (DX.rawRoot << 5) | (DX.rawRoot >> 3)   // low 8 bits retained
```

The same transformation is applied to guitar chord-change values. The 624-byte Guitar voicing table used by P's compatibility resolver has now been found **byte-for-byte in the original DX ARM9 at 0x021002B4**, proving that this is a literal DX-compatibility dialect. It remains distinct from P's native Guitar chord encoding.

## 15.4 Karaoke conversion

For each imported lyric character, the converter stores the low-14-bit BDX tick and sets `0x8000`. When the current tick equals the previous character's tick it additionally sets `0x4000`. This is why the P karaoke high bits must be treated as marker/grouping metadata rather than a single whitespace flag.

---

## 15.5 DX -> P paired-corpus results (expanded 2026 pass)

The evidence now consists of the original 14 known pairs, ten deliberately selected follow-ups, and a targeted second batch mined from the larger 529-row candidate index. The targeted working comparison set contained **48 native-looking migration pairs**, including 32 Guitar migrations. `[paired corpus]`

Important stable mappings:

- creation date, line count, meter and DX tempo prefix transfer directly;
- play type, volume and clone transfer directly for normal DX channels;
- normal instruments transfer directly except documented/canonicalised aliases;
- DX 12-byte tone settings normally duplicate into both P 16-byte tone blocks;
- ordinary Standard/Drum note bytes transfer directly;
- DX volume-change records populate P's dedicated volume table;
- DX range/root state is directly representable in P's dedicated range table, although Nintendo often canonicalises/retimes it;
- effect/tone-change table for DX migrations is the canonical empty `A at t=0` + terminator pattern;
- `guitarOrig` mask, Guitar pair conversion and Guitar per-note metadata are solved;
- per-channel clef timeline expansion is exact across all 48 tested migrations.

### Melody designation

The old converter wrote DX `mainInstrument` to P `+0x20B`; this is wrong. P `+0x207` is the signed melody field. Normal Nintendo-looking migrations support:

```text
DX FF -> P -1
DX 0  -> P 0
```

One `Gun's & Roses` candidate violates the simple `FF -> -1` expectation while otherwise matching strongly. That file is not JASRAC/Nintendo-labelled and is likely community-converted/round-tripped, so it is treated as secondary evidence rather than overturning the Nintendo-like rule.

### Panning

The original nine editor positions were extended by the follow-up corpus. A practical quantiser matching every observed point is:

```text
abs(DX)   abs(P)
0..4       0
5..10      20
11..16     32
17..22     47
23..29     57
30+        62
```

The exact observed input/output knots are corpus facts; the interval boundaries between them are an inferred compatibility rule.

### Instrument/tone canonicalisation

Most DX tone blocks copy directly, but Nintendo sometimes canonicalises old presets. Examples:

- `Beautiful World`: one channel is reclassified from Flute (20) to Clarinet (21) and both tone blocks are replaced with the canonical P Clarinet preset.
- `Merry Christmas Mr. Lawrence`: Pizzicato Violin remains the same instrument but one tone byte `FE -> 00` is sanitised.

These are editor migration rules, not required storage transforms, so the practical converter normally preserves the DX timbre.

### Guitar migration

Nintendo's own migration rebuilds palette windows/button assignments and rewrites note selectors accordingly. Follow-up corpus work disproved several tempting simple rules (copy DX slots, static sort, top-frequency buttons, simple ergonomic optimisation). The exact canonical ordering remains unresolved.

For practical conversion this no longer blocks anything: preserving the DX palette and matching note selectors is coherent and keeps the intended chord progression.

The formerly unknown `0xE77C` region is solved as 2400 `u16` per-note Guitar metadata entries, and the generator matches **32/32 Guitar migrations exactly**.

### Clef/key region

The 6,600-byte region at `0x12B0C` is byte timelines, not opaque ints. The per-channel expansion rule matches **384/384 blocks exactly**. The entire 11-block region is exact in **40/48 songs**; the other eight differ only in Nintendo's global key canonicalisation.

### Note-strength region

The 12,000-byte former `SomeMask` region is confirmed packed per-position note-strength metadata. Native-P melodic attacks use the low two-bit codes `0..3`; drums carry two independent two-bit lane strengths. DX migrations commonly retain `F` nibbles as migration/default metadata.

### Practical converter

The reference Python converter used during the 2026 research (since ported to Degausser.Core's C# converter) generated structurally valid BBPs for **58/58** available BDX inputs. See `docs/bdx-to-bbp.md` for exact policy and validation details.

# 16. XBKJ/JKBX catalogue note

An old note labelled `XBKJ` observed repeated records of `0x138 = 312` bytes with title, simplified title and author fields. This is clearly the same record size/layout family as `JbMgr.Item` used as the BBP prefix.

Degausser's `JbMgr` container itself has:

```c
struct JbMgr {
    i32 magic;      // 0x4A4B4258 in Degausser (`XBKJ`/`JKBX` depending byte display)
    i16 version;    // 0x0101
    i16 count;      // 3700 in Degausser's empty template
    Item items[count];
};
```

Old guesses about the early words inside each 312-byte item should defer to the concrete `JbMgr.Item` layout in section 2.


---

# 17. Extdata/custom-song behaviour (operational, not core BBP syntax)

Later working 3DS tooling adds several useful invariants about how BBPs are installed into game extdata.

## 17.1 Custom/editable song IDs

`wangyu-/Degausser` treats IDs whose upper 16 bits equal `0x8000` as custom songs and allocates new custom IDs in the form:

```text
0x80000000 | slot_index
```

for an unused catalogue slot. When importing a BBP “as custom”, it rewrites the song ID consistently in both the outer `JbMgr.Item` and GAK `+0x0004`, then installs the pack into the game catalogue/extdata.

This namespace rule is useful for tools that install songs, but it is not a syntactic requirement for a standalone BBP reader.

## 17.2 The BBP prefix is catalogue metadata; extdata stores the pack separately

The 3DS importer/exporter behaviour reinforces the conceptual split:

```text
BBP = 312-byte JbMgr.Item + pack
```

When installing a song, tooling stores the pack portion under the song's extdata path and inserts/updates the separate `JbMgr.Item` catalogue record. This explains why portable `.bbp` concatenates two objects that the game itself manages separately.

## 17.3 Historical vs current BDX conversion

Original GitHub issue #1 recorded that old Degausser converted negative panning incorrectly. The 2026 paired-corpus work resolves both causes: DX pan is signed, and native migration uses a nonlinear/saturating mapping rather than `pan * 2`.

Degausser.Core's converter (ported from that Python reference) also fills several P-only structures that old Degausser omitted or left as naive zeroes, including range changes, canonical empty tone-switch streams, Guitar per-note metadata, dormant Guitar/Piano tables and the expanded clef/key timelines. See `docs/bdx-to-bbp.md`.

The old warning that some conversions needed to be opened and saved unchanged in P remains useful historical evidence of editor canonicalisation, but should no longer be read as evidence that those fields are all unknown.


## 17.4 `mgr2.bin` is a filtered secondary catalogue, not another 1500 song slots

The old source contains a `JbMgr2` class but explicitly says its guessed magic is wrong. The ELF resolves the structure and its role. `[ELF]`

```text
mgr2.bin (uncompressed)

0x000  u32 magic    = 0x4A425632   // bytes: 32 56 42 4A; mnemonic unknown
0x004  u16 version  = 0x0101
0x006  u16 count    = 1500
0x008  Entry[1500]

Entry (0x140 = 320 bytes):
    JbMgr.Item item;    // 0x138 = 312 bytes
    u32 extra0;
    u32 extra1;

Total = 8 + 1500*320 = 0x75308
```

The builder at approximately `0x00162708` scans the **main catalogue/runtime slots** and copies selected records into this 1500-entry structure. It is therefore best understood as a persisted filtered projection/cache/index of the main catalogue. It does **not** provide an independent bank of 1500 ordinary free song slots; ordinary free-slot/import logic searches the main catalogue. Consequently `3700 + 1500 = 5200` is not available simply by filling `mgr2`.

Observed inclusion logic includes main runtime/status conditions and special treatment of the `0x8001xxxx` song-ID class (with four specially excluded IDs). Exact user-facing purpose of every `mgr2` selection condition remains to be named.

## 17.5 Song-ID classes recovered from ELF

A classifier around `0x00174370` partitions IDs into these ranges: `[ELF]`

```text
class 0: 0x00000000 .. 0x7FFFFFFF
class 1: 0x90000000 .. 0xFFFFFFFE
class 2: 0x80010000 .. 0x8001FFFF
class 3: 0x80000000 .. 0x8000FFFF
class 4: 0x80020000 .. 0x8FFFFFFF
else:    -1
```

This confirms that `0x8000xxxx` is a distinct class and that `0x8001xxxx` is separately meaningful. The old prose labels (“custom”, “songs on cart”, “free”, “downloaded”) should still be treated according to their individual evidence rather than projected wholesale onto these recovered class numbers.

## 17.6 Main catalogue runtime layout (useful operational context)

The ELF constructs a runtime manager containing the fixed 3700-entry `JbMgr` plus parallel per-slot data and a pointer table. The important point for BBP understanding is that a portable `.bbp`'s 312-byte prefix corresponds to the same `JbMgr.Item` object used by this live catalogue; the game then associates it with pack data/runtime state separately. `[ELF]`

For the stock 3700-entry build, the observed layout is:

```text
manager + 0x000000 : JbMgr header + 3700 * 0x138-byte Item
manager + 0x119D68 : 3700 * 8-byte auxiliary records
manager + 0x121108 : 3700 * 4-byte status records
manager + 0x124AD8 : 3700 * 12-byte runtime records
manager + 0x12F848 : end
```

Each 12-byte runtime record is initialized as three pointers to the corresponding main item, 8-byte auxiliary record, and 4-byte status record. This supports the conceptual model that catalogue metadata and pack payload are distinct game-managed objects even though BBP concatenates them for portability.

## 17.7 Other GAK-like structures seen in the ELF

The ELF knows several fixed structure sizes besides the ordinary packed BBP GAK. They should **not yet be called alternate BBP payload sizes** without tracing their callers:

```text
0x13550  79,184 bytes
0x1441C  82,972 bytes
0x14E3C  85,564 bytes   <-- ordinary packed BBP GAK documented here
0x1BD58 114,008 bytes
```

The conversion dispatcher around `0x00289A68` / `0x00289DE0` knows at least two explicit structure migrations: a `0x13550` input can be rebuilt as `0x1441C`, and a `0x1441C` input can be rebuilt as `0x1BD58`. Constructors at `0x00673564` and `0x006737A4` initialize the latter two layouts with different internal version halfwords. The same binary also contains strings such as `rom:/Gak2`, `patch:/patch/Gak3`, `Gak3-Gak4`, and `sdmc:/interconvert/gak3` / `gak4`. Do not assume one call automatically runs every step; the dispatcher tests the input size and performs the applicable migration.

Crucially, this is **not simple trailing padding around our `0x14E3C` structure**: the migration routines copy/reposition individual regions and change field widths/offsets (for example, one step preserves `+0x204` and `+0x207`, expands the field around `+0x208`, and moves later header/channel data). The sizes therefore represent genuinely different layouts or working representations.

There is also a direct bridge from the ordinary packed `0x14E3C` GAK to the larger `0x1BD58` representation. A routine at `0x00673338` copies exactly `0x14E3C` bytes from a source GAK into a larger destination buffer, then calls `0x00138630` with size `0x1BD58`. `0x00138630` uses overlap-safe memory moves to insert/expand regions in place and finally writes the first two `u16` version fields as `2, 2`. This is strong evidence that `0x1BD58` can be an **expanded/normalized working representation derived from the ordinary packed GAK**, rather than `0x1BD58` bytes being stored in a normal BBP pack. `[ELF]`

The exact mapping of the numeric sizes to Nintendo's names `Gak2`, `Gak3`, and `Gak4` is still unresolved and must not be guessed. Raw BDX can be ruled out as a direct identity for these sizes because a `.bdx` file is fixed at `0x8000` bytes in Degausser's parsed format.

The ELF repeatedly uses exactly `0x14E3C` for the ordinary main pack payload and `0xD2FA8` for the known second/Vocaloid-like payload. Other recognized second-payload sizes, including `0xCA868`, indicate additional revision/working formats that need the same cautious treatment.

### 17.7.1 Exact `0x14E3C` -> `0x1BD58` expansion

The relationship between the ordinary serialized GAK and the larger `0x1BD58` representation is now sufficiently clear to describe structurally. `0x00138630` expands a buffer in place; `0x0037A970` performs the inverse contraction before serialization. `[ELF]`

The size difference is exactly:

```text
0x1BD58 - 0x14E3C = 0x6F1C = 28,444 bytes
```

That entire difference is accounted for by five deliberate capacity/width changes rather than unexplained padding:

```text
region                         packed 0x14E3C       expanded 0x1BD58       growth
-----------------------------  -------------------  ---------------------  -------
header around +0x208           u8 length field       u16 length field       +4 total
ChannelNotes[10]               2400 bytes/channel    3840 bytes/channel     +14,400
NoteStrengths[10]              600 u16/channel       960 u16/channel        +7,200
GuitarNoteMetadata             2400 u16 total         3840 u16 total         +2,880
KeyState[11]                   600 bytes/entry       960 bytes/entry        +3,960
                                                                      ------------
                                                                         28,444
```

The header expansion is particularly useful semantically. Packed `+0x208` is copied into an expanded `u16` at `+0x208`; packed bytes `+0x209`, `+0x20A`, and `+0x20B` move to expanded `+0x20A`, `+0x20B`, and `+0x20C`, and the packed `ChannelInfo` array moves from `+0x20C` to expanded `+0x210`. The expansion sets the first two `u16` revision fields to `(2, 2)`. `[ELF]`

The inverse routine `0x0037A970` restores the ordinary packed layout and writes revision halfwords `(1, 2)`, i.e. packed `u32 0x00020001`. It removes precisely the inserted capacity regions and clears the now-unused `0x6F1C`-byte tail after `0x14E3C`. `[ELF]`

This also proves more about the song-length fields. When contracting the expanded `u16` length at `+0x208` back to the packed `u8` field, Nintendo clamps according to the byte at expanded `+0x20A` (the packed `+0x209` time-signature field):

```text
if time_signature == 4: packed lines_plus_6 = min(expanded value, 156)
if time_signature == 3: packed lines_plus_6 = min(expanded value, 206)
otherwise:               packed lines_plus_6 = 0
```

These limits exactly match the fixed 2400-byte packed note stream: 150 four-beat lines consume `150 * 16 = 2400` note bytes, and 200 three-beat lines consume `200 * 12 = 2400` bytes. Since the stored value is `Lines + 6`, those maxima become 156 and 206. This is direct ELF confirmation of the `lines_plus_6` / time-signature interpretation and explains why the expanded working representation needs a wider length field and 3840-byte note capacity. `[ELF]`

The expansion also initializes the newly created tails rather than leaving them as arbitrary padding. In particular, the newly inserted key-signature capacity is passed through helper `0x00167CF4`, using the preceding byte as an initialization input (with `0x80` removed when set). These extended regions matter to the live editor even though they are discarded when writing the ordinary packed GAK. `[ELF]`

### 17.7.2 The older fixed sizes are P-era GAK structures, not raw BDX

The `0x13550` and `0x1441C` migration code gives a stronger negative result for the tempting BDX hypothesis. Routine `0x00289A68` recognizes a `0x13550` source and constructs a `0x1441C` destination. The source already contains **ten P-style channels**: it copies `10 * 48 = 480` bytes of channel metadata from source `+0x220` to destination `+0x20C`, and `10 * 2400 = 24,000` note bytes from source `+0x400` to destination `+0x3EC`. `[ELF]`

Some observed header remapping in that conversion is:

```text
0x13550 source     0x1441C destination
----------------  --------------------
+0x20E            +0x204
+0x20F            +0x207
+0x212            +0x208
+0x213            +0x209
+0x214            +0x20A
+0x220            +0x20C   ChannelInfo[10]
+0x400            +0x3EC   10 * 2400 note bytes
```

Routine `0x00289DE0` likewise handles ten channels while converting a `0x1441C` representation toward the larger working layout. Therefore neither `0x13550` nor `0x1441C` is a raw Nintendo DS BDX file. Raw BDX remains the fixed `0x8000` 8-channel format described in section 15. A separate BDX import/conversion path may exist, but these P-ELF migrations are not raw-BDX parsers. `[ELF] [code]`

The embedded path names strengthen the interpretation that these are Band Brothers P's own content generations: the ELF has path-return functions for `patch:/patch/Gak3`, `rom:/Gak2`, `ext:/jb/gak`, and `sdmc:/gak`, plus the `Gak3-Gak4` interconversion strings. The exact numeric-size-to-`Gak2`/`Gak3`/`Gak4` mapping remains unresolved and should not be guessed. `[ELF]`

## 17.8 Native composition-editor serialization and the “open then save unchanged” clue

The composition save path gives an important model for reproducing Nintendo's canonical output. A path around `0x003C51C4` works on the expanded `0x1BD58` representation, temporarily contracts it, writes the ordinary packed payload, then expands the same working buffer again. `[ELF]`

The editor also performs semantic synchronization that is not part of the structural expand/contract routines. Function `0x001FDBB0` scans all ten live editor channels backwards for the last occupied line, using a 150-line maximum for 4/4 and 200 for the other supported meter path, then writes `max_used_line + 6` into the expanded GAK line-count field. Thus simply loading a song into the composition environment can cause at least `linesPlus6` to be regenerated from actual musical content before the next save. `[ELF]`

The observed sequence is approximately:

```text
load/copy ordinary GAK
    -> allocate/copy 0x1BD58 working buffer
    -> 0x00138630 expand packed 0x14E3C -> working 0x1BD58
    -> editor/state synchronization
    -> 0x0037A970 contract working 0x1BD58 -> packed 0x14E3C
    -> native pack writer (wrapper 0x004D95C0; underlying path near 0x004D8FF8)
    -> 0x00138630 expand the same buffer back to 0x1BD58 for continued editing
```

Immediately before serialization, the save path also canonicalizes GAK byte `+0x204`: it tests bit 1; if bit 1 was set it clears bit 0, then clears bit 1 unconditionally. Thus a native composition save always leaves bit 1 clear, and an input with bit 1 set also loses bit 0. The user-visible meaning of these bits is still unresolved. `[ELF]`

A pure expand-then-contract is otherwise close to an identity operation for an already valid ordinary GAK: it canonicalizes the revision to `(1,2)`, applies the documented line-count clamp for serialization, and initializes extended working-only tails that are discarded again. Consequently, the historically reported **“open the imported song in composition and save it unchanged”** repair cannot be explained simply by the size expansion/contraction itself. Additional editor/state synchronization or derived-field regeneration between load and save remains a high-priority target for a bit-faithful BDX->BBP converter. `[ELF]`

This distinction is useful for converter design: a future Nintendo-faithful conversion pipeline should model **semantic/editor canonicalization separately from structural packing**. Reimplementing only `0x00138630` and `0x0037A970` will reproduce the packed/working layout transformation, but not necessarily all values the native editor repairs.

---

# 18. Validation and round-trip invariants

A useful implementation should separate **hard structural checks** from **soft semantic warnings**.

Recommended hard/near-hard checks:

```text
file length >= 0x138 + 0x44
PackHeader GAK offset/size lies within file
GAK gzip decompresses successfully
GAK decompressed size == 0x14E3C for the format documented here
if Vocaloid-present == 1, Vocaloid offset/size lies within file
```

Recommended soft checks/warnings:

```text
pack word[0] is normally 0x00020001
BDX→BBP converter also emits GAK version 0x00020001
pack word[1] is normally 1
GAK offset is normally 0x44
pack words[9..16] are normally zero in normalized files
Vocaloid offset is normally 4-byte aligned
JbMgr.Item.title_id normally equals GAK.title_id
line_count = lines_plus_6 - 6 should fit the packed note-stream capacity (150 lines at time_signature 4; 200 lines at time_signature 3)
instrument IDs may be unknown and must not be rejected
```

For a lossless or minimally destructive editor:

1. preserve unknown GAK bytes/arrays unchanged unless explicitly edited;
2. preserve the 312-byte catalogue fields you do not intend to normalize;
3. preserve unknown pack-header words when doing a strict lossless round trip, or deliberately normalize words 9..16 to zero when emulating Degausser export policy;
4. preserve the raw compressed Vocaloid stream if you do not understand/edit it;
5. update both song-ID locations together when changing the ID.

---

# 19. Fork/network findings

The original GitHub repository currently has six forks. Four (`asdlei99`, `chocobo7777777`, `globcell`, `TheAgamer554`) retain the same 14-commit history and do not add useful BBP-format work. Two contain substantive later implementation work:

- **`wangyu-/Degausser`** — extensive 3DS-side development (89 commits), including custom/editable import, specific-song dump/delete, explicit pack-header structure, synchronized ID rewriting, and opaque preservation of compressed Vocaloid data. This is strong operational cross-check evidence.
- **`edgarcantuco/DegausserMidi`** — adds MIDI export on top of the existing decoded event model. It does not finish Guitar/Piano decoding; its tempo scale factor is explicitly empirical and is documented here only as a non-normative downstream heuristic.

The original repository's issues add two format-relevant warnings: negative panning conversion was known-broken, and some real BBPs contain instrument IDs outside the current instrument table. The repo wiki contains no additional documentation.


---

# 20. Residual unknowns / project closure

For **BBP parsing, editing, BDX migration, and high-fidelity BBP → MIDI**, the archaeology is considered practically complete as of 2026-09-07. The remaining unknowns do not justify blocking converter work.

### Known-but-not-named gameplay/editor metadata

- `ChannelInfo +0x07/+0x08`: live, tightly related P-only performance/editor parameters; corpus behaviour is documented above, but Nintendo's user-facing name was not recovered.
- `ChannelInfo +0x09` and the high nibble of `+0x0A`: difficulty/gameplay companion metadata. `low(+0x0A)` is Vocal difficulty.
- Some JbMgr catalogue flag names remain historical/approximate.

These should be preserved, not guessed. They are not known to affect audio playback.

### Synth/emulation-level work deliberately out of scope

- exact Nintendo tone/DSP transfer curves and the final sample engine;
- exact timbre/sample mapping for every P-only instrument and drum kit;
- optional Vocaloid payload internals (opaque preservation is already solved);
- exact native editor canonicalisation for Guitar palettes and playable range/button layouts;
- obscure Piano editor/control edge cases beyond the recovered chord representation/generation work.

These are relevant to emulator-quality reproduction or editor cloning, not to understanding the BBP score format itself.

### Corpus-derived reserved fields

A lossless parser should retain arbitrary values, but Nintendo's complete 9,722-song official corpus makes the following safe practical assumptions:

```text
PackHeader words 9..16 : 0 in 9,722/9,722 files
GAK +0x0205            : 0 in 9,722/9,722 files
GAK +0x020B            : 0 in 9,722/9,722 files
ChannelInfo +0x0B      : 0 in 97,220/97,220 records
ChannelInfo +0x0C..0F  : 0 in 97,220/97,220 records
```

Do not spend further reverse-engineering effort on these unless a real file with a nonzero value appears.

The reproducible corpus findings are summarized in [research/2026-09-07-bbp-archaeology-closure.md](research/2026-09-07-bbp-archaeology-closure.md).

# 21. Recommended parser / converter model

For a new project, preserve raw data as well as interpreted fields. In particular, avoid throwing away unknowns:

```text
BBP
 ├─ metadata: JbMgrItem
 └─ pack
     ├─ gak: Gak
     │   ├─ header
     │   ├─ channel[10]
     │   │   ├─ info (including raw unknown bytes)
     │   │   ├─ notes[2400]
     │   │   ├─ note strengths[600 u16]
     │   │   ├─ volume changes
     │   │   ├─ range changes
     │   │   ├─ tone-preset changes
     │   │   └─ pan
     │   ├─ tempo
     │   ├─ guitar structures + per-note metadata
     │   ├─ piano structures
     │   ├─ lyrics/timing
     │   ├─ notation/key-state timelines
     │   └─ chord-change tables
     └─ vocaloid?: raw bytes
```

For a new parser API, expose both interpreted values and raw backing regions. Unknown fields should survive parse→edit→write. In particular, do not force instrument IDs through a closed enum.

A converter such as BBP → Tomo Jam can implement the now-recovered playback-relevant score layer directly:

- metadata/title/author;
- time signature and 48-tick line timeline;
- tempo changes;
- Standard, Drum, Piano and Guitar score semantics;
- per-score note strengths and channel strength curves;
- instrument IDs;
- per-channel volume automation and pan;
- karaoke text/timing;

while preserving but not necessarily synthesizing tone-preset/DSP data and the few unresolved gameplay/editor metadata bytes. This is enough for high-fidelity musical conversion without emulating Nintendo's sample engine.

A practical first implementation should also expose `raw_vocaloid_gzip` (if present), `unknown_pack_words[8]`, raw unknown `ChannelInfo` bytes, and all unresolved GAK arrays, so future semantic discoveries do not require redesigning the file model.

---

# 22. Evidence/provenance

## 22.1 Original repository — primary implementation evidence

`AdibSurani/Degausser`:

- `src/File Formats/Gak.cs` — exact fixed GAK layout and helper structs.
- `src/BBPRecord.cs` — BBP parsing/rebuilding, note/drum decoding, change trackers, BDX conversion, partial MIDI interpretation.
- `src/File Formats/JbMgr.cs` — exact 312-byte catalogue structure, flags, singer/icon enums.
- `src/Karaoke.cs` — karaoke timer bit masking, whitespace/anchor behaviour, retiming.
- `src/Utils/MidiPlayer.cs` — playback clock; establishes the original 12-ticks-per-quarter/direct-tempo interpretation.
- `src/Resources/Instruments.xml`, chord tables, and `src/Utils/InstrumentData.cs` — known instrument and chord mappings.
- `src/HackingTests.cs` — historical experiments/corpus paths; useful archaeology but not normative by itself.
- `degausser3ds/source/main.c` — independent C implementation of catalogue/pack import/export and flag behaviour.
- GitHub issues — known negative-panning conversion bug and evidence that real files may contain instrument IDs outside the current table.

The repository wiki is empty and contributes no additional format knowledge.

## 22.2 Substantive forks

`wangyu-/Degausser`:

- explicit 17-word pack-header grouping (`main_*`, `has_voc`, `voc_*`, reserved words);
- working custom/editable-song import;
- synchronized update of outer item ID and GAK `+0x04`;
- custom-song ID namespace `0x8000xxxx`;
- opaque preservation of the already-compressed Vocaloid stream;
- normalization of pack words 0, 1 and 9..16.

`edgarcantuco/DegausserMidi`:

- downstream Standard MIDI export from Degausser's decoded event model;
- no completed Guitar/Piano decoder beyond original source;
- empirical tempo multiplier explicitly labelled as derived from expected song lengths, therefore non-normative.

Other current forks (`asdlei99`, `chocobo7777777`, `globcell`, `TheAgamer554`) are effectively mirrors of the 14-commit original history and add no identified format semantics.

## 22.3 Historical BDX format documentation

The 2021 mirror at `https://bbptrdintro.hateblo.jp/entry/2021/03/16/173411` preserves the older `banbro.moero.info` BDX structure notes. It independently documents the BDX melody selector, nominal signed pan range (`-63..+63`), Guitar button-assignment table, Guitar note button/stroke codes, custom-chord marker (`rawRoot=0xFF`), and other DX-era semantics used when interpreting the paired conversion corpus. Treat it as `[old BDX documentation]`: valuable semantic ancestry, but native P behaviour still requires P-side evidence.

## 22.4 Uploaded archaeology

- `daigasso p specs.txt` — old end-to-end P GAK offset map.
- `possibly modified inst_def.xml` — independently names the 16-byte tone parameter structure and resolves old Degausser field-name errors.
- `header files.txt` — older DX GAK layout; semantic ancestry/crosswalk only.
- `XBKJ format maybe.txt` — early observations of the 312-byte catalogue record.
- `instrument names.txt` / `instrument parts.txt` — older instrument/resource notes.
- `daigasso p songs.txt` — title-ID fixtures.
- `xlsxs_dump.txt`, including exports of `bdx_data.xlsx`, `daigasso correlation.xlsx`, `daigasso mgrbin flags.xlsx`, and `lookie new jbcontents.xlsx` — catalogue/flag/header correlations and a large flattened BDX corpus.
- lyric/reference screenshots, including `2147624995_nywbPPaXGw.png`.

Important archaeology upgrades already incorporated into the normative text:

- `JbMgr` bit 23 = hidden is strongly corroborated by the flag spreadsheet.
- `GAK+0x0207` is high-confidence as a signed main/melody-channel index.
- the 16-byte tone parameter layout is independently matched byte-for-byte to instrument preset data.

## 22.5 Game ELF — direct implementation evidence

Analyzed file: `the-happiest.elf` / `DaigassoUpdate.elf`, ELF32 little-endian ARM, stripped, SHA-256 `9e1aea3dee9038361f22b9ed56a976fa89c287ea19422ca099aaf6075493830`. `[ELF]`

High-value recovered anchors currently documented above include:

```text
0x0017F80C  JbMgr.Item empty initializer (0x138 bytes, ID=0xFFFFFFFF)
0x0017E800  5-bit Parts setter (mask 0xF8)
0x00174370  song-ID range classifier
~0x00162708 mgr2 filtered-projection builder
0x00289A68  0x13550 -> 0x1441C structure migration
0x00289DE0  migration step producing 0x1BD58
0x00673564  0x1441C structure initializer
0x006737A4  0x1BD58 structure initializer
0x00138630  packed 0x14E3C -> expanded 0x1BD58 in-place conversion
0x0037A970  expanded 0x1BD58 -> packed 0x14E3C contraction
~0x003C51C4 composition-editor save path (contract -> write -> re-expand)
~0x004D95C0 native pack-write wrapper used by composition save
```

The binary also gives direct consumers for the packed `0x14E3C` GAK size and outer-flag derivation from GAK fields/channel types. Addresses are working reverse-engineering labels, not recovered Nintendo symbol names.

## 22.6 What is *not* yet evidence

- commented/speculative instrument IDs without observed files;
- names such as `SomeMask`, `rangeChanges`, `effectorChanges`, or `mainInstrumentMaybe` merely because they appear in decade-old source;
- DegausserMidi's empirical MIDI tempo multiplier as a statement about on-disk BBP encoding;
- assumptions about Vocaloid internals from its size alone.

This distinction is intentional: the goal is a specification that can safely be handed to another developer or coding model without silently turning old hypotheses into facts.


### Pack header preservation

The 68-byte pack header is now represented by `BbpPackHeader` in Core and serialized with EndianBinaryIO. Words 9–16 are still unknown. They were all zero across the 48 BDX-migration BBPs available during the 2026 recovery, but Degausser preserves them verbatim rather than assuming they must always be zero.
