# Practical BDX -> BBP conversion

**Updated:** 2026-09-20

This document describes the current practical `.bdx` -> `.bbp` converter and the evidence behind it. The production implementation is `BdxToBbpConverter` in `Degausser.Core`; the older Python scripts remain archaeology/reference material only.

The goal is **not** to reproduce every Nintendo editor canonicalisation byte-for-byte. The goal is a conservative P file that preserves the DX song, uses correct P structures/defaults, and does not invent transformations we do not understand.

## 1. Usage

The desktop app treats `.bdx` as a first-class input: open or add a BDX to the library, preview it through the converted P model, then save/export it as BBP. Programmatic conversion lives in `BdxToBbpConverter.Convert(...)` and `BBPRecord.FromBDX(...)`.

The older standalone Python converter is retained only as archaeology/reference material; new work should use the Core implementation so it gets the DX-ROM-grounded Piano path and the current BBP model.

## 2. Validation status

In the latest working set:

```text
58 / 58 BDX inputs converted
58 / 58 output BBPs passed structural self-validation
```

Validation checks include:

- canonical BDX slot size `0x8000`; historical dumps ending after all converter-read fields (`>=0x7F1A`) are zero-padded, and harmless suffix bytes after `0x8000` are ignored;
- packed outer structure and gzip readability;
- GAK decompressed size exactly `0x14E3C`;
- JbMgr ID == inner GAK ID;
- fixed-region bounds/sentinels;
- P pan tuple shape;
- no out-of-bounds Piano custom materialisation.

This is binary/structural validation. Actual game import/playback is still the strongest end-to-end test.

## 3. Conversion policy

Rules are divided into three classes.

### Exact / directly evidenced

Use these unconditionally:

- fixed BBP/GAK physical layout;
- BDX title/date/meter/lines/tempo ancestry;
- melody designation at P `+0x207`;
- P `+0x20B = 0` for DX migrations;
- ChannelInfo play type/instrument/volume/clone ancestry;
- 12-byte DX tone -> both 16-byte P tone blocks, zero-extended;
- normal note streams copied directly;
- volume-change stream ancestry;
- P-only stream sentinels/defaults;
- Guitar built-in/custom pair encoding;
- `P.guitarOrig = DX.guitarOrig & 0x3FFFFFFF`;
- Guitar per-note metadata generation;
- per-channel clef timeline expansion;
- Piano custom/original chord materialisation algorithm.

### Strong compatibility inference

These are used because they match all observed cases and preserve musical meaning:

- nonlinear pan quantiser between observed knots;
- direct DX range-state records -> P `rangeChanges` rather than trying to reproduce Nintendo's ergonomic/key recalculation;
- global DX key timeline direct expansion unless Nintendo-style canonicalisation is specifically desired.

### Optional Nintendo canonicalisation intentionally omitted

The converter does **not** attempt to reproduce:

- Nintendo's Guitar palette-window/button rearrangement;
- Nintendo's range/button/key ergonomic recalculation;
- difficulty/star recalculation;
- P-only derived ChannelInfo metadata;
- song-wide `GAK+0x20A` normalisation;
- rare instrument/tone preset aliases except one directly confirmed Guitar case;
- exact karaoke canonicalisation quirks beyond the safe compact P representation.

## 4. Top-level output

An ordinary BBP is written as:

```text
JbMgr.Item  0x138 bytes
PackHeader  0x44 bytes
gzip GAK    decompresses to 0x14E3C
```

The same title/song ID is written into the JbMgr item and the GAK. The converter generates a deterministic ID unless one is explicitly supplied.

## 5. Header mapping

Relevant source/destination fields:

```text
DX dateCreated       -> P GAK +0x0008
DX dateModified      -> P GAK +0x000C
DX title             -> P UTF-16 title field
DX mainInstrument    -> P +0x0207 signed melody channel
DX linesPlus6        -> P +0x0208
DX timeSignature     -> P +0x0209
DX masterVolume      -> P +0x020A (conservative fallback only)
                       P +0x020B is zero
```

### `+0x020A` migration policy

P `+0x020A` is a **signed song-wide gain/normalisation byte**. Later real-3DS editor ground truth establishes the P playback/display law `1 + (i8)raw/128`; the older provisional `(raw + 64)/64` projection is obsolete. Nintendo's own DX→P migrator does **not** simply copy the DX master-volume byte: paired migrations show the same DX source value producing different P gains, so Nintendo performs song-wide normalisation/canonicalisation.

**Recovered 2026-11-01** from 310 DX/P pairs (BDX files in Rubedo Mega Pack 3.0 whose note tracks are identical to an official DX-migration BBP):

```text
non-karaoke songs:  P master = min(32, 16 + DX[0xB7])     exact for all 38 clean pairs
karaoke songs:      Nintendo also generated Vocaloid vocals (154/154 pairs) and lowered
                    the master by a further 12..68, independent of every score/header
                    field tested, so probably measured from rendered audio
```

The converter applies the vocal-free rule to every song, since it never adds Vocaloid data. `BdxConversionOptions.NormaliseForP = false` restores the old raw copy, which only the Python-oracle comparisons use.

## 6. Channel mapping

For DX channels 0..7:

- play type -> P ChannelInfo `+0`;
- instrument -> `+1`;
- volume -> `+2`;
- clone -> `+3`;
- old difficulty bytes are carried into the known difficulty positions as a safe baseline;
- ChannelInfo `+7 = 8` for the native DX migration baseline;
- Nintendo's official migrations overwhelmingly use ChannelInfo `+8 = 8` for Standard/Guitar/Piano and `+8 = 0` for drums. The production converter now writes that recovered migration default alongside `+7 = 8`.
- DX 12-byte tone copied into both P tone blocks;
- new P tone-tail bytes are zero.

Channels 8 and 9 are blank P-only channels with canonical empty automation and pan sentinels.

## 7. Panning

Old Degausser used `DX_pan * 2`. This is wrong.

Nintendo's mapping, measured on 1,488 channels of 186 clean DX/P migration pairs (2026-11-01; the earlier inferred table was off by about two):

```text
abs(DX)   abs(P)
0..6       0
7..12      20
13..18     32
19..24     47
25..30     57
31+        62
```

Restore the sign after quantisation.

The P structure is:

```text
{ 0, mapped_pan, -1, 0 }
```

The exact observed corpus includes the original editor points and additional values such as `4`, `8`, `10`, `14`, `15`, `16`, `20`, `32`, `38` and `57`.

## 8. Automation/change tables

### Tempo

The first 32 DX tempo `TimeValuePair`s copy directly into P's larger 128-entry table.

### Volume

DX's combined per-channel change records contain a volume stream; these values are copied into P's dedicated 256-entry volume table.

### Range / playable layout

The same DX combined record contains the root/range state. The 16-bit state is valid P data and is copied into P's dedicated 128-entry `rangeChanges` table.

Nintendo often retimes/re-encodes these values because P calculates playable range/key/button layout differently from DX. That canonicalisation affects gameplay notation/button assignment, not the stored absolute score notes. The practical converter therefore keeps the DX state rather than trying to emulate an incompletely understood ergonomic algorithm.

### Tone A/B changes (`0xCE8C`)

P supports two full tone presets and up to 126 changes. The 128-entry region at `0xCE8C` fits that feature exactly and should be treated as a tone-preset selector timeline, not generic “effect changes”.

DX cannot change tone preset mid-song, so migration baseline is:

```text
(0, 0)     select tone A at time 0
(-1, 0)    terminator
```

## 9. Clef / key state (`0x12B0C`)

The old `int[150]` declaration was misleading. The region is:

```text
u8 state[11][600]
```

For each DX channel block 0..7:

1. copy its 512-byte state timeline;
2. force bit 7 on source byte 0 (explicit initial state);
3. determine the final state at the song's last used line;
4. fill P-only bytes 512..599 with that final low-7-bit state.

This reconstructed **all 384 per-channel blocks exactly** in the 48-pair targeted migration corpus.

P blocks 8 and 9 are blank extra channels (`0x80` first byte, then zero).

DX global block 8 maps to P block 10 similarly. Nintendo sometimes recalculates the global key timeline; direct expansion remains musically safe.

## 10. Guitar conversion

### 10.1 Why the converter keeps DX palettes

Nintendo's migration often rearranges Guitar button palettes and rewrites note selectors to match. That is a P editor/gameplay canonicalisation, not a requirement of the P storage format.

For practical conversion, keeping the DX palette has major advantages:

- all chord choices are preserved;
- note button codes remain valid and need no remap;
- custom chords remain attached to the expected button;
- the P per-note derived cache can still be generated exactly.

Therefore the default converter preserves DX Guitar palette order.

### 10.2 Chord pair encoding

DX and P use two-byte `{index, rawRoot}` pairs but P encodes built-ins differently.

```text
FF/FF                  -> FF/FF             empty
rawRoot == FF          -> index, EF         DX custom/original chord
otherwise:
  P.index              = index * 16 + 15
  P.rawRoot            = ROL8(rawRoot, 5)
```

The P Guitar timer record has ten pairs instead of DX's eleven; the final DX pair is unused/dropped.

### 10.3 Custom chord words

```text
P.guitarOrig[i] = DX.guitarOrig[i] & 0x3FFFFFFF
```

The low 30 bits are six 5-bit fret/mute fields. The cleared upper bit is a DX-era marker not needed in P.

### 10.4 Guitar per-note metadata (`0xE77C`)

This region is now solved as:

```text
u16 guitarNoteMeta[2400]
```

One entry corresponds to one byte in the 2400-byte Guitar note stream.

The P-side semantic interpretation, recovered later from native-P Studio/corpus work, is:

```text
0x8000        fresh strum / envelope retrigger
0x4000        P strum-direction bit (native P: 1 = down, 0 = up)
bits 0..5     per-strum string suppression mask
0x00FF        triplet-cell marker
0             rest / passive continuation position
```

The historical DX→P migration reconstruction remains byte-oriented: it reproduces Nintendo's migrated metadata by deriving bit 14 from the DX stroke-code bit and deriving the six low suppression bits from strings marked muted in the DX voicing. That rule matched **4,800/4,800 bytes in all 32 native-looking Guitar migrations tested**. It should not be generalized into the native-P claim that a chord-muted string is inherently silent: native P can physically hit a packed fret `16..31` and produce a short damped transient when the event suppression bit is clear. If all six DX strings decode muted in the migration special/sentinel case, the reconstructed mask remains `0` rather than `0x3F`.

### 10.5 Nintendo palette reconstruction (optional archaeology)

The migration corpus shows Nintendo does rebuild its own palette windows/button assignment. Useful observations:

- active P palettes correspond to forward-looking sets of up to eight distinct upcoming chords;
- P may preload the next palette before the first newly required chord;
- surviving chords often retain their previous P slot, but not always;
- source DX slot ancestry is weak;
- simple frequency or ergonomic-cost optimisation does not explain the ordering;
- altered/extended chord qualities show strong slot preferences.

This remains optional for the practical converter.

## 11. Piano conversion

DX has one shared Guitar-or-Piano chord system; P has separate Guitar and Piano systems.

As of 2026-09-20 the DX side is independently grounded in the Japanese AXBJ ARM9, not merely in historical Degausser behaviour or DX→P pairs. See [research/2026-09-20-bdx-dx-rom-chords.md](research/2026-09-20-bdx-dx-rom-chords.md) for addresses and the exact algorithm.

DX has **13** built-in Piano qualities (`M,m,7,M7,m7,dim,m7b5,aug,sus4,7sus4,6,add9,m6`) plus 32 custom chords. The song-wide `0x7F18/0x7F19` state selects the top note and one of six voicing modes: 3/4 voices × close/medium/wide. A newly recovered `u32` mask at `0x66C8` additionally selects which custom chords are fed through that global voicing machinery.

The production converter now resolves every DX Piano chord through that exact DX-ROM algorithm first. For a built-in quality 0..11 it then searches P's native same-root/same-quality descriptor space (ordinary rotations and both drop states) and uses a native descriptor **only when P produces exactly the same sounding pitch multiset**. Anything not exactly representable — including DX `m6`, wide layouts, and some 3-voice seventh/extended chords — is stored losslessly as a P `pianoOrig` literal voicing. Flagged DX custom chords are transformed according to `0x66C8` before literal storage.

`BdxPianoChordEncoding.ExactLiteral` disables native-P optimisation while retaining the exact DX pitches. `LegacyCompatibility` exists only to preserve the old 51-file archaeology hash oracle; it intentionally retains the pre-ROM converter's known Piano limitations.

If a malformed/nonstandard BDX claims both Guitar and Piano play types simultaneously, the converter currently refuses rather than corrupting the shared chord data.

## 12. Per-note strength (`0x061AC`)

P stores one four-bit value per 2,400 score positions:

```text
600 * 16 bits = 2400 * 4 bits
```

Inside each stored `u16`, score positions run from the high nibble to the low nibble. For melodic/Piano/Guitar parts, the low two bits are the recovered four strength codes (`0..3 = weakest..strongest`); `ChannelInfo +6` selects one of three native strength curves. Drums use the four bits as two independent two-bit lane strengths.

Nintendo DX migrations commonly fill this region with `0xFFFF`, and the practical converter retains that migration baseline. Native-P corpus data shows the upper two bits are unused on ordinary melodic attacks, so they should be preserved rather than assigned invented semantics.

## 13. Karaoke

Nintendo's migration (and P's own karaoke normaliser) also normalises lyric text:

- printable ASCII becomes full-width (`I love you` → `Ｉ　ｌｏｖｅ　ｙｏｕ`), except `?`, which stays ASCII, and `'`, which becomes `’` (U+2019);
- line breaks and spaces (now U+3000) get timer flags `0xC000`, other characters `0x8000`;
- lyrics always end with a line break whose timer is `0x7FFF`.

Every one of the 2,647 official BBPs with Latin letters in its karaoke uses full-width forms, migrated or native (corpus-inferred).

The converter's goal is readable karaoke on P, not a byte-exact copy of the migration, so with `NormaliseForP` (the default) it differs in two ways:

- **English lyrics stay half-width.** In cbf_std every full-width form is 14.4 px, while ASCII is proportional (3.6–13.8 px), so full-width English is about 1.7× wider: 141 Rubedo BDX songs would get lines over the 288 px scorefix budget. Lyrics with more ASCII letters than other letters (115 songs) keep ASCII and `'`, like the scorefixes. Japanese lyrics are converted as above. Their separators are flagged `0xC000` either way.
- **Page padding is dropped.** DX shows three lines per page, and arrangers pad stanzas out to a page with runs of blank lines. P scrolls. Runs are collapsed to one blank line, and the dropped line breaks' timers go with them. 40% of migrated BBPs keep such runs; only 6% of native ones have any.

Lines are otherwise kept as the arranger broke them. Native P lyric lines are short sung phrases too: median 144 px, against 115 px for DX. 55 Japanese songs keep a full-width English line wider than 288 px (at most 360 px). Nintendo itself ships lines of up to 389 px, so a 3DS check of such a song would also show whether 288 px is the real limit.

Before these two changes, 181/186 clean pairs had lyrics identical to Nintendo's.

The converter compacts the DX lyric encoding into P UTF-16 and copies the low-14-bit timing/grouping information into P's 3000-entry timer stream.

Known behaviour:

- P uses `0x3FFF` as an end/unused timer value;
- `0x8000` marks a valid/active timing entry;
- native P sets `0x4000` in addition for newline/ASCII-space/full-width-space separator entries;
- historical DX migration also uses bit 14 for same-tick grouping, so it is best treated as separator/grouping metadata rather than a literal whitespace bit;
- final P timer padding follows the native migration baseline closely.

These high bits are understood well enough for playable conversion and round-tripping; reproducing every editor canonicalisation is unnecessary.

The generated `JbMgr` catalogue flags also mirror DX meter: bit 17 is set for 3/4 songs (`IsThreeFourMeter`).

## 14. Instrument aliases / normalisation

Normal DX instrument IDs are directly representable in P.

One native Guitar migration directly confirms:

```text
DX Guitar instrument 56 -> P instrument 198
```

This corresponds to the old commented `GT_DX` alias table. The converter applies that one alias by default and exposes an option to disable it.

Other Nintendo instrument/tone transformations (e.g. one observed Flute -> Clarinet canonical preset) are treated as editor normalisation rather than conversion requirements and are intentionally not generalized without stronger evidence.

## 15. Native defaults that matter

P-created/migrated files contain non-zero defaults in dormant structures. The converter writes these rather than leaving the entire GAK zeroed.

Examples:

- `pianoPair` is `FF`-filled even when no Piano part exists;
- dormant Guitar timer has a specific 32-record DX-era initializer plus P-only zero tail;
- blank channels have explicit volume/range/tone-change terminators;
- blank pan entries are `(0,0,-1,0)`;
- P-only ChannelInfo performance defaults are `+07=8,+08=8` for Standard/Guitar/Piano and `+07=8,+08=0` for Drum;
- DX-migrated note-strength area is `FFFF`-filled;
- clef/key initial bytes have the explicit-state bit set.

These defaults are likely part of why historical converters sometimes needed “open then save unchanged” in P.

## 16. Known differences from Nintendo migration

Measured against 186 clean DX/P pairs on 2026-11-01:

| Region | Pairs differing | Why |
|---|---|---|
| master gain | 154 | karaoke songs only: Nintendo's extra reduction for its generated Vocaloid vocals |
| ChannelInfo `+04/+05/+09/+0A` | 186 | difficulty star ratings Nintendo computes from the notes; not reproduced |
| range changes | 186 | Nintendo's playable-range canonicalisation (below) |
| pan, channels 8–9 | 142 | Nintendo leaves non-zero values (78, 107, −62) in unused channels; harmless |
| title / author / `titleSimple` | most | migrated songs are credited `ＤＸいしょく＠<author>`; `titleSimple` is a hand-curated reading/sort key |
| guitar timer / chord tables | 50–75 | palette canonicalisation (§10.5) |
| karaoke | 5–16 (more now) | edge cases; since then, deliberately: English stays half-width and DX page padding is collapsed (§13) |


A generated BBP may differ from Nintendo's official DX transplant in these areas while still representing the same music:

- Guitar timer schedule/button ordering;
- Guitar note button codes (ours remain DX-relative because the palette also remains DX-relative);
- playable range/key/button canonicalisation;
- global key timeline in songs where Nintendo recalculates it;
- ChannelInfo P-only derived/difficulty fields;
- Nintendo's exact **derived value** for `GAK+0x20A` (the field semantics themselves are solved);
- rare instrument/tone preset substitutions;
- some lyric grouping flags.

These differences should be viewed as editor/gameplay metadata until demonstrated to affect import/playback.

## 17. Suggested path into Degausser

The Python file is reference code, not the intended final home.

For Degausser:

1. add corrected C# structures/offset names from `BBP_FORMAT.md`;
2. port BDX parsing/conversion rules from this script;
3. keep a validator that checks GAK size/IDs/sentinels;
4. preserve unknown raw fields when editing existing BBPs;
5. treat Nintendo canonicalisers as optional post-processing, not prerequisites;
6. add regression tests from paired BDX/P fixtures.

A useful test suite would have:

- one clean Standard-only song;
- one Drum-heavy song;
- one negative/intermediate-pan song;
- `TRAIN-TRAIN` as a simple Guitar palette case;
- `Sobakasu` as a complex native Guitar canonicalisation case;
- a custom-Guitar-chord song;
- a Piano song;
- a karaoke song;
- a 3/4 song;
- Beautiful World / Merry Christmas as instrument/tone canonicalisation controls.
