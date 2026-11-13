# BBP archaeology closure — 2026-09-07

This note closes the 2026 reverse-engineering pass on **Daigasso! Band Brothers P** `.bbp` / packed GAK for practical parser, conversion and MIDI-playback work.

The working conclusion is deliberately pragmatic: the fixed format and the playback-relevant score semantics are sufficiently understood for a high-fidelity BBP → MIDI implementation. Remaining unknowns are mostly Nintendo editor/catalogue terminology, exact DSP/sample-engine transfer functions, and optional Vocaloid internals. They should not block Tomo Jam or ordinary BBP tooling.

## Evidence base

The final pass combined:

- original Degausser source and later forks;
- paired BDX → official-P migration evidence;
- stripped Band Brothers P update ELF (`SHA-256 9e1aea3dee9038361f22b9ed56a976fa89c287ea19422ca099aaf6075493830`);
- `inst_def.xml` and `gt_chord.xml` resource evidence;
- a complete sweep of **9,722 official BBPs** containing **97,220 ChannelInfo records**;
- the historical RICKEY `bdx2midi.exe` as an independent DX-semantics oracle.

All 9,722 official BBPs decompressed to the expected `0x14E3C` GAK size with zero parse/decompression failures.

## Corpus results that closed dead ends

### Reserved/constant fields

```text
PackHeader words 9..16 : 0 in 9,722/9,722 files
GAK +0x0205            : 0 in 9,722/9,722 files
GAK +0x020B            : 0 in 9,722/9,722 files
ChannelInfo +0x0B      : 0 in 97,220/97,220 records
ChannelInfo +0x0C..0F  : 0 in 97,220/97,220 records
```

Lossless tools should still preserve arbitrary bytes, but these no longer deserve reverse-engineering time unless a real counterexample appears.

### `GAK +0x0206` = classic/public-domain catalogue flag

Cross-tab against outer `JbMgr.flags bit 10`:

```text
+206=0 / IsClassic=0 : 8872
+206=1 / IsClassic=1 :  849
+206=0 / IsClassic=1 :    1   (四季)
+206=1 / IsClassic=0 :    0
```

The 849 set songs are overwhelmingly Nintendo's classical/public-domain catalogue. Treat `+0x0206` as the inner classic flag; retain the single known official anomaly as a note rather than inventing another semantic.

### `GAK +0x020A` signed master gain

**Post-closure correction (2026-09-13):** real-3DS editor ground truth superseded the provisional `+64` projection used during the 2026-09-07 closure. The visible ALL/master law is:

```text
master_multiplier = 1 + (i8)raw / 128
                  = (128 + (i8)raw) / 128
```

The signed-field conclusion below remains valid; only the old MIDI scaling law was replaced. Official-corpus distribution as signed `i8`:

```text
all 9,722:  min -64, max +80, median +32, mean +30.52
native P:   min -64, max +64, median +64; 2,979/5,890 exactly +64
DX migrate: min -59, max +80, median  +9; 1,645/3,832 negative
```

**9,718/9,722** values are within `-64..+64`; only four DX migrations exceed `+64` (`68, 74, 80, 80`). This makes signed interpretation overwhelmingly more plausible than treating high-bit values as unsigned gains.

Nintendo's DX → P migrator does not simply copy DX master volume: paired files show the same DX source master value producing different P `+0x20A` values. The P field is therefore a derived song-wide normalisation/gain. The later real-3DS law above is the current MIDI projection basis.

### ChannelInfo `+07/+08`

These are live P/editor parameters but no audio dependency was recovered.

Across **72,603 active non-drum parts**, all but one satisfy:

```text
if +07 <= 10:  +08 = +07
else:          +08 = +07 / 2 + 5
```

The single official exception is part 9 of `ＭＩＬＫ (Ｎｉｎｔｅｎｄｏ)` with `+07=10,+08=5`.

Across **10,564 active drum parts**, `+08` is always zero.

Native-P songs use the full observed `+07=0..30` and `+08=0..20` domains. Nintendo DX migrations overwhelmingly use:

```text
Standard/Guitar/Piano: +07=8, +08=8
Drum:                  +07=8, +08=0
```

Their exact Nintendo UI name is not worth blocking playback work over.

## Playback-relevant conclusions

### Guitar strings are simultaneous

The earlier MIDI implementation introduced an artificial ~4 ms/string stagger. ELF playback disproved that model: all sounding Guitar strings are submitted during the same sequencer update. Down/up stroke affects string ordering and chord semantics, but **authored onset spacing is 0 ms**.

### Per-score note strength

`0x061AC` is exactly one four-bit nibble per score position:

```text
10 channels × 600 u16/channel × 4 nibbles/u16
= 10 × 2400 score positions
```

Within each stored `u16`, score positions use high nibble → low nibble.

For melodic/Piano/Guitar, low two bits are:

```text
0 weakest
1 weak
2 normal/reference
3 strong
```

`ChannelInfo +0x06` selects:

```text
curve 0:  70  85 100 115
curve 1:  50  75 100 125
curve 2:  80  90 100 110
```

Recovered native first-stage calculation:

```text
floor(curve[strength] * channelVolume / 127)
```

Native-P melodic/Guitar/Piano occupied attacks use no upper-nibble-bit feature beyond those two strength bits. DX migrations commonly contain `F` nibbles, so lossless tools should preserve the full nibble even if a renderer consumes only the recovered low two bits.

Drums use all four bits as two independent two-bit lane strengths, matching their two simultaneous score-nibble lanes.

### Karaoke high bits

Native P normalisation uses:

```text
ordinary lyric character             -> time | 0x8000
LF / ASCII space / U+3000 full space -> time | 0xC000
```

Thus bit 15 is the valid/active-entry marker and bit 14 is additional separator/grouping metadata. Historical DX conversion also uses bit 14 for same-tick grouping, so it should not be named literally `isWhitespace` in a lossless model.

`0x3FFF` remains the unset/unused timing sentinel.

## RICKEY `bdx2midi` side quest

RICKEY's original release described `bdx2midi` as the open-source MIDI-output portion extracted from `bdx Maker`; the historical source archive was not recovered, but the supplied 40 KB .NET 2.0 executable is small enough to reconstruct effectively source-for-source.

It understands BDX notes, timing, volume, drums, keyboard chords, built-in/custom Guitar chords and muted strings. It does **not** reveal a hidden BDX musical mechanism missing from the current BDX → BBP → MIDI route. Its main independent value is an alternate historical General-MIDI instrument/drum mapping. It also emits Guitar strings simultaneously, consistent with the later P ELF result.

## Repository closure changes

The 2026 closure pass updates Degausser itself to match the recovered format rather than leaving solved fields behind old archaeology names:

- Guitar MIDI attacks no longer receive an artificial inter-string delay;
- `GAK +0x20A` is modeled as signed `sbyte`, preventing the old unsigned bug before Degausser's historical `+64` MIDI gain bias;
- ChannelInfo exposes `+08`, `+09`, `+0A`, reserved `+0B/+0C..0F`, and both 16-byte tone blocks explicitly;
- the former `SomeMask` model is now a packed-note-strength structure with high-to-low nibble indexing;
- BBP→MIDI applies the recovered note-strength curve as the native attack stage, then feeds that attack value through Degausser's existing volume-automation/master projection;
- `JbFlags.Parts` uses all five native bits and bit 17 is named for 3/4 meter;
- BDX conversion writes Nintendo's observed P-only `+07/+08` migration defaults (`8/8` for non-drums, `8/0` for drums);
- `audit-bbp-corpus.py` (a maintenance script outside this repo) reproduces the key 9,722-song invariants without external dependencies.

The exact downstream Nintendo DSP composition after the recovered attack stage is still intentionally not claimed. Degausser's exporter policy is now explicit: use the native first-stage attack calculation, then feed that value through the already-established automation/master MIDI projection. This closes the practical MIDI-fidelity gap without pretending to have reconstructed Nintendo's sample engine.


## Repository/notes parity sweep — 2026-09-08

A final no-new-research sweep treated these archaeology notes as the source of truth and brought the checked-in model, converter, tests and documentation into parity. In addition to the playback fixes above:

- the `0x12B0C` block is exposed as eleven 600-byte notation/key-state timelines rather than the historical `int[150]` alias;
- `0x0CE8C` and `0x0E28C` use the recovered tone-preset and pan terminology in the public model;
- karaoke creation/retiming emits the recovered `0x8000` active and `0xC000` active-separator forms, and `HasKaraoke` follows the native lyric-plus-first-timer rule;
- `JbMgr2.CreateEmpty()` uses recovered magic `0x4A425632` and unsigned 16-bit version/count fields;
- the synthetic golden BBP was refreshed for Nintendo's recovered `+07/+08` DX-migration default;
- the 51-file historical Python-oracle hash test now validates the corrected `+07/+08` bytes independently, then normalises only the old-oracle `+08` difference before checking every other GAK byte;
- stale documentation references to non-checked-in archaeology scripts and old field names were removed or explicitly marked historical.

The full 9,722-official-BBP audit still parses 9,722/9,722 after these model refactors and reproduces the closure invariants. No additional BBP semantics were researched during this parity pass.
This runtime did not provide `dotnet`, `csc`, or `mcs`, so the .NET test suite could not be executed here; fixture hashes, binary payload integrity, Python tooling, delimiter/static checks and corpus invariants were verified before packaging.

## Deliberately residual work

These remain interesting but are no longer BBP-format blockers:

- exact names for ChannelInfo `+07/+08/+09` and high `+0A` metadata;
- exact Nintendo DSP/tone/sample-engine transfer functions;
- exhaustive P-only instrument and drum timbre mapping;
- exact Nintendo editor canonicalisation of Guitar palettes/range/button layouts;
- optional Vocaloid payload internals;
- obscure editor-only Piano/control edge cases.

The policy going forward is: **preserve unknown bytes, document real counterexamples, and do not reopen archaeology solely to name metadata that does not affect playback or conversion fidelity.**
