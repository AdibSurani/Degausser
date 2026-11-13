# Documentation

Reference for the file formats and how Degausser works. The `research/` notes hold the reverse-engineering evidence behind them.

## Reference

| Doc | Covers |
|---|---|
| [architecture.md](architecture.md) | Project boundaries and the design rationale (why things are the way they are) |
| [bbp-format.md](bbp-format.md) | The `.bbp` / packed GAK / JbMgr format specification |
| [bdx-to-bbp.md](bdx-to-bbp.md) | BDX → BBP conversion rules and the evidence for each |
| [midi-playback.md](midi-playback.md) | BBP → MIDI projection, playback backends, limitations |
| [extdata-and-packages.md](extdata-and-packages.md) | SafeFile containers, extdata layout, BBPM/BBPV packages, DLC translations |
| [catalogue-search.md](catalogue-search.md) | The embedded BBP Catalogue v3.0 index and how to regenerate it |
| [scorefix-authoring.md](scorefix-authoring.md) | How to write a scorefix: file format, title and lyric rules, tools |
| [releasing.md](releasing.md) | Versioning, tags, and producing release artefacts |

Component READMEs: [Degausser3DS](../3ds/Degausser3DS/README.md), [scorefix corpus](../scorefixes/README.md), [synthetic test fixture](../tests/Degausser.Core.Tests/TestData/Synthetic/README.md).

## Research

Reverse-engineering write-ups: the evidence behind the reference docs.

- [2026-09-07 BBP archaeology closure](research/2026-09-07-bbp-archaeology-closure.md): 9,722-song corpus results and what was left open
- [2026-09-20 DX ROM chord recovery](research/2026-09-20-bdx-dx-rom-chords.md): DX ARM9 Piano/Guitar chord engine
- [2026-10-05 Master volume](research/2026-10-05-master-volume.md): what's proven about ALL/master gain and what isn't
- [2026-11-01 BBP.sf2 audit](research/2026-11-01-bbp-sf2-audit.md): BBP.sf2 vs the Mosgis profile (bank select, profile corrections)
- [2026-11-09 Karaoke line widths](research/2026-11-09-karaoke-line-widths.md): hardware-measured sing-mode and studio widths; the 313 px scorefix budget
