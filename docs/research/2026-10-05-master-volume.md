# Band Brothers P master / ALL volume — status 2026-10-05

## What is proven

- Packed GAK `+0x20A` is the signed ALL/master control byte.
- During P's runtime GAK expansion, packed `+0x209` (time signature) shifts to expanded `+0x20A`, and packed `+0x20A` (master) shifts to expanded `+0x20B`. This explains earlier misleading runtime xrefs to `+0x20A`.
- Real-3DS editor testing establishes the **visible/control mapping** `display = 1 + raw / 128.0`. Example: raw `+43` is displayed as `1.33x` (two-decimal truncation).
- ALL/master is conceptually a later gain stage and must not be folded into MIDI Note On velocity in the preferred SoundFont playback path.

## What is not proven

The visible `1 + raw/128` mapping has **not** yet been proven to be the final NintendoWare DSP amplitude law. Earlier documentation incorrectly described that equivalence as recovered/native-proven.

An external Japanese volume-investigation spreadsheet empirically measured nominal master settings approximately as:

| nominal | measured amplitude ratio |
| ---: | ---: |
| 1.50 | 1.5698 |
| 1.40 | 1.4686 |
| 1.30 | 1.3477 |
| 1.20 | 1.2288 |
| 1.10 | 1.1160 |
| 1.00 | 1.0000 |
| 0.90 | 0.8825 |
| 0.80 | 0.7628 |
| 0.70 | 0.6513 |
| 0.60 | 0.5330 |
| 0.50 | 0.4269 |

Those values fit `1 + raw/112` strikingly well if the nominal UI values correspond to the expected raw values, but the spreadsheet is external empirical evidence, not native ground truth. Its exact capture/measurement path is not guaranteed. **Do not implement `/112` as native behaviour until the ELF/NintendoWare path proves it.**

## Degausser policy for now

`BbpMidiDocument` exposes three distinct concepts:

- `MasterGainRaw`: packed signed GAK byte.
- `MasterDisplayMultiplier`: hardware-verified `1 + raw/128`.
- `MasterPlaybackGainApproximation`: currently equal to the display multiplier as a conservative fallback.

SoundFont playback applies `MasterPlaybackGainApproximation` after synthesis so the stage placement is faithful even though the exact transfer curve remains unresolved. Native-system MIDI playback can only approximate that later gain by scaling Note On velocity.

## Next reverse-engineering target

Trace expanded GAK master `+0x20B` from song initialisation into the actual NintendoWare mixer/final gain call, and recover the exact transfer function independently of the spreadsheet.
