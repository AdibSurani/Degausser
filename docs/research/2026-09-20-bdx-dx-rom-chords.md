# DX ROM chord recovery — 2026-09-20

This note records the independent verification of the BDX Piano/Guitar chord
model against the Japanese **Daigasso! Band Brothers DX** Nintendo DS ROM
(`BANDBROS DX`, game code `AXBJ`).

ROM SHA-256:

```text
324223ecdb1ddb2f793e2e82d898983d35242af899096a671090e04971176ffd
```

The ARM9 executable is BLZ-compressed in the ROM. After Nintendo-DS BLZ
expansion, the relevant chord routines and tables are in the main ARM9 image.
Addresses below are runtime ARM9 addresses.

## Independent confirmation of the old BDX tables

The historical 52-byte Piano chord table used by Degausser occurs **verbatim**
at ARM9 `0x021001D8`. It contains 13 qualities, each with four source tones:

```text
 0 major     3C 40 43 00   C E  G
 1 minor     3C 3F 43 00   C Eb G
 2 7         3C 40 46 43   C E  Bb G
 3 M7        3C 40 47 43   C E  B  G
 4 m7        3C 3F 46 43   C Eb Bb G
 5 dim       3F 42 45 3C   Eb Gb A  C
 6 m7b5      3F 42 46 3C   Eb Gb Bb C
 7 aug       3C 40 44 00   C E  G#
 8 sus4      3C 41 43 00   C F  G
 9 7sus4     3C 41 46 43   C F  Bb G
10 6         40 43 45 3C   E G  A  C
11 add9      3E 40 43 3C   D E  G  C
12 m6        3F 43 45 3C   Eb G A  C
```

The historical 624-byte DX Guitar chord table used by `BbpChordCatalog` also
occurs **verbatim** in the DX ARM9 at `0x021002B4`. This independently proves
that P's migrated-DX Guitar compatibility path is using the original DX voicing
catalogue, not a table reconstructed from P pairs.

## Root descriptor

DX routine `0x0203FE2C` decodes the BDX root byte:

```text
low nibble  = diatonic degree 0..6 = C D E F G A B
high nibble = accidental 0 natural, 1 sharp, 2 flat
```

The degree table in DX is `3C 3E 40 41 43 45 47` (C4..B4); accidental offsets
are `0,+1,-1`. This exactly confirms the historical Degausser interpretation.

## Built-in Piano generator

The main built-in generator is `0x0204F85C`; its shared voicing post-processor
is `0x0204F704`.

The song-wide settings are:

```text
BDX 0x7F18  highest/top note
BDX 0x7F19  six-state voicing mode
```

`highest == 0` means MIDI 70. DX then clamps the value to `25..127`.
Invalid voicing values outside `0..5` fall back to mode 0.

Voicing mode is **not** merely a 3/4-voice flag. It is:

```text
0  3 voices, close
1  3 voices, medium
2  3 voices, wide
3  4 voices, close
4  4 voices, medium
5  4 voices, wide
```

DX resolves a built-in chord as follows:

1. Add the decoded root to all four nonzero source tones from `0x021001D8`.
2. Octave-wrap every nonzero tone into the 12-note window ending at `highest`.
3. Sort all four results descending.
4. For 3-voice modes, discard the fourth/lowest result **after** that sort.
5. For 4-voice modes, if the quality was a triad with a zero fourth source
   tone, duplicate the top result one octave down to make a fourth voice.
6. Apply the spread transform below.

Spread transforms (indices refer to the descending temporary buffer):

```text
mode 0 / 3  close:   no further change
mode 1      3-med:   voice[1] -= 12
mode 2      3-wide:  voice[1] -= 24; voice[2] -= 12; sort voices 1..2
mode 4      4-med:   voice[1] -= 12; sort voices 1..3
mode 5      4-wide:  voice[1] -= 24; voice[3] -= 12; sort voices 1..2
```

This exposes a real limitation in the pre-ROM converter: it used
`3 + voicing/3` but ignored `voicing%3`, and for 3-voice four-tone qualities it
kept all four tones instead of letting DX sort then discard the lowest.

For example, at `highest=70`:

```text
DX C major, mode 0 -> G4 E4 C4
DX C major, mode 1 -> G4 E3 C4       (pitch set G4 C4 E3)
DX C major, mode 2 -> G4 C3 E2
DX C major, mode 3 -> G4 E4 C4 G3
DX C major, mode 4 -> G4 C4 G3 E3
DX C major, mode 5 -> G4 C4 E2 G2

DX C7, mode 0 -> Bb4 G4 E4
```

That last example is important: DX's three-voice C7 is produced by making the
four-tone C7 set, voicing/sorting it, then discarding the lowest tone. At this
top-note setting the root C is therefore omitted. P's native three-voice C7
template has a different three-tone subset. A quality-name-only mapping would
be wrong.

## Custom/original Piano chords — newly recovered flag mask

DX routine `0x0204F468` copies the Piano resolver state out of the song:

```text
BDX 0x6608  32 x 2-byte Piano pair table
BDX 0x6648  32 x 4-byte custom/original chord table
BDX 0x66C8  u32 per-custom-chord transformation bit mask
```

`0x66C8` was not modelled by the previous converter.

Custom resolver `0x0204F8F8` behaves differently according to bit `1<<index`:

- bit clear: the four stored literal pitches are simply sorted for playback;
- bit set: the chord is fed through the song-wide `highest` / voice-count /
  close-medium-wide machinery described above.

For a flagged custom in a 3-voice mode, DX clears the fourth source byte before
running the common voicing processor.

This is not theoretical. Rubedo's corpus contains real official Nintendo files
using the mask, including:

```text
The Final Countdown   0x001FFFFF  (first 21 custom chords)
Gonna Fly Now         0x000FFFFF  (first 20)
Y.M.C.A.              0x000000FF  (first 8)
Yuki no Hana          0x00000003  (first 2)
```

The updated converter now applies these transformations exactly before storing
the resulting P literal chord.

## DX-compatible Guitar vs P-native Guitar

Guitar is the straightforward compatibility case.

The P migrated-DX representation is produced by:

```text
p.index   = dx.index * 16 + 0x0F
p.rawRoot = ROL8(dx.rawRoot, 5)
```

The low-nibble `0xF` selects P's DX-compatibility path. P reverses the packed
root and resolves the chord through the same 624-byte voicing table that exists
in the original DX executable.

P-native Guitar instead uses P's own packed 16-bit chord ID / native voicing
catalogue. Thus Guitar really has two runtime dialects: native P and embedded
legacy DX.

## DX Piano vs P-native Piano

Piano is fundamentally different. No analogous DX symbolic escape dialect has
been identified in P.

DX stores:

```text
per chord:     root + one of 13 qualities
per song:      highest + 3/4 voices + close/medium/wide
```

P-native stores per chord:

```text
root + one of 12 qualities
3/4 voices
rotation (-6..+6 in the editor range)
drop/spread bit
```

P therefore has richer **per-chord voicing controls**, but a slightly smaller
native quality vocabulary (no DX `m6`). Its three-voice quality templates also
do not always choose the same three tones that DX obtains by sort-and-drop.

The updated converter therefore does **not** map by chord name. It:

1. runs the exact DX ROM algorithm to obtain the sounding DX pitches;
2. for DX qualities 0..11, enumerates P's ordinary native rotation range and
   both drop states while preserving the same root, quality and voice count;
3. accepts a native P descriptor only if the generated pitch multiset is exact;
4. otherwise stores the exact DX pitches as a P `pianoOrig` literal chord;
5. DX `m6` (quality 12) necessarily takes the literal path because P has no
   corresponding native quality.

This keeps playback exact while yielding editable native-P chords wherever the
two voicing systems genuinely intersect.

## Rubedo corpus impact

Across the 3,640 raw BDX files in Rubedo Mega Pack 2.4, 733 use a Piano play
part. Their actual DX Piano voicing modes are:

```text
mode 0  598
mode 1   26
mode 2    7
mode 3   76
mode 4   23
mode 5    3
```

Among 5,067 built-in Piano pair entries in those Piano files:

```text
4,338  can be represented exactly as same-quality native P descriptors
  729  require literal fallback
1,478  would sound differently under the pre-ROM converter
```

251 of the 733 Piano BDX files contain at least one built-in chord for which the
pre-ROM converter's materialised pitch output differs from the real DX engine.
The dominant reason is DX's sort-then-drop behaviour on 3-voice four-tone
qualities; medium/wide spread accounts for additional differences.
