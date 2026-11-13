# BBP.sf2 vs the Mosgis profile (2026-11-01)

Evidence level: **measured** for the bank-select behaviour (MeltySynth 2.4.1 probed directly), **name-inferred** for the profile corrections (BBP.sf2's own preset names). Not yet checked against the BCSAR.

## Bank select

`BbpToMidiConverter` writes a bank as CC0 = bank >> 7, CC32 = bank & 0x7F. BBP.sf2 numbers its variation banks 1, 2, 8, 16, 24 and 26 (GS-style MSB values) and its kits as bank 128. MeltySynth ignores CC32 and takes the bank from CC0 only (on the percussion channel it adds 128). Measured:

| Sent | MeltySynth bank |
|---|---|
| bank 1, 8 or 16 | 0 |
| bank 128 on channel 10 | 129 (no such bank) |

So every "faithful" bank override falls back to bank 0, and kits to whatever MeltySynth substitutes for a missing bank. FluidSynth's default `gs` bank-select mode also uses CC0 only. Fix: send the variation bank in CC0 with CC32 = 0, and on the percussion channel send CC0 = 0 and the kit as the program.

## Profile entries that point at the wrong preset

`Midi/Profiles/mosgis-bbp-sf2.json`, resolved with MSB banks against BBP.sf2 (sha256 8fc00a2e…):

| Instrument (ids) | Profile | Preset there | Preset named for it |
|---|---|---|---|
| NES A (41, 130) | 1/80 | NES B | 0/80 or 16/80 NES A |
| NES B (165, 176, 187) | 1/81 | none | 1/80 or 0/83 NES B |
| NES C (166, 177, 188) | 0/7 | E. Guitar | 26/80 or 0/63 NES C |
| NES Bass (167, 178, 189) | 0/82 | Pan Flute-2 | 2/80 NES Bass |
| Sine (186) | 8/81 | none | 8/80 Sine (ids 164 and 175 already use it) |
| Rock Drums (42) | kit 0 | P. Drums | kit 16 R. Drums |
| Electronic Drums (43) | kit 16 | R. Drums | kit 24 E. Drums |
| Noise Drums (194) | kit 24 | E. Drums | kit 26 N. Drums |
| Pop Drums (196) | kit 8 | none | kit 0 P. Drums |

Bongo Set and Conga Set use kit 0 and need a BCSAR check. Where BBP.sf2 has two presets with the same name (NES A at 0/80 and 16/80, NES B at 1/80 and 0/83, NES C at 26/80 and 0/63), the BCSAR decides which one matches the game.
