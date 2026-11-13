# MIDI playback and export

Degausser projects Band Brothers score semantics into a canonical DryWetMIDI `MidiFile` in `Degausser.Core`. The Avalonia desktop player and `.mid` export consume that same projection.

## Timing

Band Brothers score data uses 12 frames per quarter note. Degausser emits MIDI at 480 PPQ, so one Band Brothers frame is 40 MIDI ticks.

Guitar strings are **not** given artificial sub-frame spacing: native playback submits every string of one strum in the same sequencer update. Up/down stroke is retained as ordering/physical-direction metadata, not an authored arpeggiation delay. Repeated Guitar strums close the previous ringing chord and start the next one at the exact same tick; MIDI serialization orders Note Off before Note On at equal timestamps.

## Standard parts

Ordinary score bytes below `0x80` are note attacks/rests; high-bit bytes are held/continuation states according to the historical player semantics. Native intensity maps to MIDI velocity. Static volume uses CC7, live score automation uses CC11, with the special GM drum-channel compromise noted below.

GAK `+0x20A` is signed. Real-3DS editor ground truth shows the visible ALL/master multiplier is:

```text
master_multiplier = 1 + (i8)raw_master / 128
                  = (128 + (i8)raw_master) / 128
```

The previous Degausser `(raw + 64) / 64` projection is obsolete.

The packed `0x061AC` region is one four-bit strength value per score position. Melodic/Piano/Guitar playback uses the low two bits (`0..3 = weakest..strongest`), while drums use two independent two-bit lane strengths. `ChannelInfo +0x06` chooses one of three recovered strength curves:

```text
curve 0: 70, 85, 100, 115
curve 1: 50, 75, 100, 125
curve 2: 80, 90, 100, 110
```

Degausser now uses the **unmodified native note-strength values as MIDI velocity**. There is no configurable intensity flattening. For melodic parts static volume maps to MIDI CC7, and score-volume automation maps to live MIDI CC11. Universal Real-Time Master Volume SysEx is written at tick zero in **both** GM-compatible and Mosgis/BBP.sf2 modes:

```text
BBP_gain = clamp(1 + raw_master / 128, 0, 1.5)
MIDI_master_14bit = round(16383 * sqrt(BBP_gain / 1.5))
SysEx = F0 7F 7F 04 01 lsb msb F7
```

The square root and 1.5 headroom match the **SpessaSynth-specific** relative gain curve used by browser BBP2MIDI 1.2; other players may ignore SysEx or render it differently. In GM-compatible mode, all drums share channel 10, with CC7 neutral at 127, and both the static part volume and score automation folded into each hit's velocity (square root of the product of normalized volume factors). This is an approximation to avoid shared-channel controller collisions.

## Piano

Piano score bytes do **not** index `pianoOrig` directly. A nonzero score byte is a 1-based index into `pianoPair[120]`.

Each selected `IndexRootPair` has one of two runtime forms:

1. **Native P generated chord** — packed root/accidental/voice-count/quality/rotation/drop data is expanded by the recovered native P chord generator. The 12 qualities are major, minor, 7, M7, m7, dim, m7b5, aug, sus4, 7sus4, 6 and add9.
2. **Original/custom chord** — `rawRoot` top three bits are all set (`0xE0..0xFF`), and `pair.index` selects `pianoOrig[64]`, whose 32-bit value contains up to four literal MIDI pitches.

Generated and literal voices begin simultaneously and are released together at the next Piano chord/rest.

## Guitar

Guitar uses three chord dialects plus attack metadata:

- **native P built-ins** — a 16-bit chord ID resolved through the compact catalogue derived from `rom:/snd/gt_chord.xml`;
- **P original/custom** — any category-7 selector (`rawRoot & 0xE0 == 0xE0`) indexes `guitarOrig[32]` with the low byte;
- **migrated DX** — low-nibble-`F` escape encoding decoded through the legacy DX chord table.

`guitarTimer` is a sparse palette timeline. Playback stops reading it at the first negative-time terminator; zero-filled data after that terminator is padding, not another palette.

The parallel `guitarNoteMetadata[2400]` is attack-level state:

```text
bit 15      fresh strum / envelope retrigger
bit 14      direction: 1 = down, 0 = up
bits 0..5   string suppression mask (bit 0 = high E ... bit 5 = low E)
```

A high-bit Guitar score byte can therefore still be a **new strum** when metadata bit 15 is set. This is how native P represents repeated attacks of the same held chord. Older/synthetic files without the derived metadata retain a low-byte score-code fallback.

The six-bit event mask means “do not hit this physical string”. It is distinct from a muted/dead string encoded inside the chord voicing itself.

Each chord word contains six 5-bit packed fret values in high-E → low-E string order. Values `<16` are ringing frets. Real-3DS listening established that values `16..31` are not silent: the string still emits a short, pitch-dependent, instrument-specific damped transient. Degausser currently uses the listening-derived approximation:

```text
underlying_pitch = open_string_pitch + (packed_fret & 0x0F)
muted_duration   = 1 BBP frame = 40 MIDI ticks
instrument       = the part's normal instrument
```

The muted attack is attenuated as P does it: `velocity = floor(normal × 47 / 128)`. Native P also forces `Decay ≥ 99, Sustain = 0` for these voices. Plain MIDI can't express that per note, hence the gate.

This low-nibble fret interpretation and one-frame gate remain empirical approximations rather than an ELF-proven envelope law. Event-mask-suppressed strings remain silent.

If a muted transient duplicates a ringing pitch in the same strum, Degausser omits the short duplicate rather than allowing its same-channel Note Off to choke the ringing note.

## Pan

The recovered P pan field is signed and centred at zero. MIDI CC 10 is centred on 64, so Degausser maps it directly as:

```text
midi_pan = clamp(64 + p_pan, 0, 127)
```

Per-part drum panning is not currently exported because canonical General MIDI merges drum parts onto percussion channel 10.

## Instruments and GT_DX aliases

Embedded 213-entry BBP2MIDI JSON profiles (`Midi/Profiles`) are the active MIDI mapping source; `Instruments.xml` remains a legacy metadata reference. P IDs `197..212` are represented as `Type="GT_DX" ReplaceWith="..."` aliases; `BbpInstrumentCatalog` now resolves those entries directly instead of maintaining a hard-coded subset. The original BBP ID is preserved while MIDI program/drum mapping comes from the replacement instrument.

## Export

The Avalonia client exposes **Export MIDI...** (`Ctrl+M`) for the currently playing song. It writes the canonical multi-track `MidiFile` produced by `BbpToMidiConverter`; both modes use the same score/chord/dynamics projection. Mosgis / BBP.sf2 is the default, with GM or custom v1/v2 JSON profiles selectable in Settings.

## Desktop playback backend

Degausser uses MeltySynth / BBP.sf2 for desktop preview, with a native MIDI fallback when no SoundFont can be loaded. MeltySynth has no MIDI ports, so the player splits the playback MIDI by port (port-prefix meta events) and runs one synthesizer per port on a shared SoundFont, summing the outputs; each drum part therefore keeps its own percussion channel, kit and mute. Bank select is read as the MIDI's 14-bit CC0/CC32 pair and passed to MeltySynth, which only reads CC0. Relative master gain is applied after synthesis in desktop preview; an exact match to other MIDI receivers has not been proved.

## Known limitations

- General MIDI/Windows synth timbres are approximations of Band Brothers instruments.
- BBP.sf2 bank/program overrides are included via embedded profiles. Multi-port preview is rendered per port (offline-verified, not yet checked by ear).
- Drum mappings come from the selected 213-entry JSON profile; in GM mode all drum parts share channel 10.
- Band Brothers tone/envelope/effect data is not yet projected into MIDI synth controls.
- Muted-Guitar one-frame duration and low-nibble fret interpretation are listening-derived approximations, not yet native-envelope archaeology.
