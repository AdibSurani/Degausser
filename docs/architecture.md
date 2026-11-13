# Architecture

## Dependency direction

```text
Degausser.Avalonia  ─────►  Degausser.Core
```

Core must not reference Avalonia or OS-specific UI types. Other frontends can therefore reuse the same domain code:

```text
                    Degausser.Core
                 ▲         ▲        ▲
                 │         │        │
        Degausser.Avalonia web     CLI
```

Core compiles unchanged to WebAssembly (`browser-wasm`); a browser prototype (React + TypeScript, kept outside this repository) used it that way. The browser owns all file access (File System Access API), so Core's web-facing operations take and return bytes: `ExtDataSafeFile.Encode/Decode`, `SongImport.Add`, `BBPRecord.FromBbp/FromPack/FromBdx`. Path-based helpers (`ExtDataWorkspace`, `JbManager`) remain for the desktop. Core uses source-generated JSON so it stays safe to trim.

## Degausser.Core

Core owns knowledge that exists because Band Brothers was reverse engineered:

- BBP outer records and pack layout, GAK score layout;
- extdata workspace composition: JbMgr/JbMgr2, MvMgr, SingerMgr/SingerMgr2, SafeFile containers, BBPM/BBPV packages ([extdata-and-packages.md](extdata-and-packages.md));
- BDX → BBP conversion ([bdx-to-bbp.md](bdx-to-bbp.md));
- karaoke representation, timing and scorefix retiming;
- Band Brothers instrument/drum mappings and the BBP → MIDI projection ([midi-playback.md](midi-playback.md));
- the embedded BBP Catalogue index ([catalogue-search.md](catalogue-search.md)).

### Why the BDX converter came from the Python oracle

The repository's earlier C# BDX conversion was incomplete. It didn't read pan, left conversion paths as stubs, and misread some fields. Production conversion was instead ported from the audited Python reference used during the 2026 archaeology and then validated against the DX ARM9 ([research](research/2026-09-20-bdx-dx-rom-chords.md)). The oracle itself isn't in the repo. Its regression value is kept by the 51-entry hash manifest and the synthetic golden pair.

### Binary IO

All fixed little-endian game formats use EndianBinaryIO. Private disk DTOs describe property order, fixed arrays and UTF-16 regions, and the public models stay ordinary managed types. Small container fields use `EndianBinaryPrimitives`.

- The old `ByValTStr` capacity rule is kept explicitly: a historical `SizeConst=N` field holds at most `N-1` UTF-16 units plus a NUL.
- **Unknown bytes are preserved, never normalised.** The 68-byte pack header's eight unknown trailing words survive a recompile.
- EndianBinaryIO works by reflection over attributes. A trimmed build, whether desktop or WebAssembly, must therefore root `Degausser.Core` and `EndianBinaryIO` or annotate the DTOs.

### Test fixtures and copyright

The repository commits no commercial songs or real saves. The only binary song fixture is a purpose-built synthetic scale. Tests against real data run only when a `DEGAUSSER_*` environment variable points at a local copy, and otherwise report as skipped.

### Karaoke and scorefixes

Core owns lyric/timer interpretation and exposes UI-neutral highlight state (`PositionStart`, `PositionCount`, `PositionFraction`). The UI only renders it.

Scorefix retiming (`Karaoke.RetimeLyrics`) aligns pasted romaji to the song's existing Japanese karaoke:

- Source lyric characters and timers are walked together. **Whitespace is never a pronunciation anchor**, even when native P gave it a `0x8000` timer. Treating it as one broke historical fixes such as Tentai Kansoku, Chocolate Disco and God Knows.
- Alignment is **strict**. Text that doesn't match the source, or unmatched trailing text, throws rather than silently consuming the wrong anchor.
- Parenthesised chunks such as `(nijuu)(go)ji` are explicit manual overrides. The parentheses are stripped from display.
- New timers use the recovered 48-tick interpolation cap. JbMgr's title keeps the scorefix's line breaks (the top-screen song card draws them; the song list joins the lines) and is limited to 50 characters including them.
- Applying a scorefix to a song opened from 3DS Content writes its pack and JbMgr item back. Library and file previews are changed in memory only.
- Scorefixes are loose `.txt` files read from a folder (`ScorefixLibrary.FromDirectory`; the app picks the folder from Settings, else `scorefixes/` beside the executable, and assigns `ScorefixLibrary.Current`). They are an optional extra, not embedded, so people can share and replace them as plain files; Core never looks for the folder itself. The catalogue is still embedded, and `BbpCatalogue.RefreshScorefixes()` rebuilds its Scorefixes collection (every song with a scorefix, including title-only ones) when the folder changes. The web prototype has no folder and therefore no English lyrics. `SongImport.Add(..., englishLyrics: true)` applies one to a copy of the record, so the caller's record and the user's library file stay Japanese. A scorefix that doesn't align makes that one import fail, rather than silently importing Japanese.

### Unicode folding

Search and the scorefix aligner compare text after NFKC, so `Ｌ`, `L` and half-width `ﾙ` → `ル` match. .NET's WebAssembly runtime can't do NFKC: with invariant globalization `Normalize` returns non-ASCII text unchanged, and its ICU build throws for the compatibility forms. `TextFolding` uses the runtime where a probe shows it works, and otherwise a table of what lyrics and titles contain: full-width ASCII and spaces, half-width katakana with (semi-)voiced marks, Roman numerals, ellipses, circled numbers, and accented Latin letters for search. The web build therefore stays invariant and doesn't download ICU data. `DEGAUSSER_TEXT_FOLDING=table` forces the table on desktop. All 218 checkable scorefixes align with it.

## Degausser.Avalonia

The desktop project owns XAML, dialogs, commands, settings, logging presentation, multiple open `ExtDataWorkspace` sessions, and the playback adapter. The Activity tab mirrors Serilog output. Log files (daily rolling, 7 files, 5 MB each, under the platform local-app-data `Degausser/logs` folder) are written only when the user has ticked "Keep these settings between sessions"; otherwise nothing is written to disk.

### Why Avalonia

The goal was one desktop app on Windows, Linux and macOS without forking format code. WPF was removed on 2026-09-20. The first port kept the WPF layout rather than redesigning it: a persistent library on the left, and open saves as documents on the right. DataGrid was chosen because it maps directly onto the old WPF grids, and it can be swapped for TreeDataGrid later without touching Core.

### Desktop playback

```text
BBP / GAK ─► BbpToMidiConverter ─► BbpMidiDocument (DryWetMIDI MidiFile)
                                        ├─ MeltySynth (SF2) ─► MiniAudioEx audio device   (all platforms)
                                        ├─ DryWetMIDI native MIDI output                  (Windows/macOS fallback; not on Linux)
                                        └─ .mid export
```

- **The SF2 path is preferred everywhere.** `BBP.sf2` beside the executable (or in the app-data `SoundFonts` folder) is auto-detected, or users can pick one in Settings (*Get BBP.sf2* opens its download page; the host is behind a Cloudflare challenge, so it can't be fetched in-app).
- **ALL/master and the UI volume slider are applied after synthesis**, so changing either affects notes that are already ringing instead of rewriting velocities. The native-MIDI fallback has no post-synth hook and approximates both at Note On, which is why it's lower fidelity.
- **Seeking** restarts the sequence and fast-forwards to the target without rendering notes (new notes are dropped while chasing), so tempo, program and controller state are correct at the target. A note that was already held when the target is reached is not restored; it starts again at its next Note On.
- Playback keeps the source parts on distinct channels until synthesis, so per-part mute works even for multiple drum parts that later share MIDI channel 10.
- MeltySynth has no MIDI ports, so `SoundFontPlayback` splits the playback MIDI per port and runs one synthesizer per port (sharing the SoundFont) and sums them. Each drum part therefore keeps its own percussion channel in preview, as in the exported multi-port MIDI.

### Karaoke rendering

- The active timed range is an Avalonia `Run` painted with a hard-edged red/white `LinearGradientBrush`, so the sweep can stop part-way through a glyph. WPF's `TextEffect` doesn't exist in Avalonia.
- `Run` boundaries are fixed for the lifetime of a line, and only the brushes change. Re-splitting runs every frame caused 1–2 px kerning jitter.
- The dark outline is eight offset copies of the line drawn behind it. Completed lines dim to white, as in the original player.
- Font chain: `Inter, Meiryo, Yu Gothic, Hiragino Sans, Noto Sans CJK JP`. Without explicit CJK fallbacks, Windows picked a narrow UI font for Japanese. No font is bundled.
- The ten parts are a fixed **2 × 5** grid matching the game, and they don't reflow on resize.

### Extdata writes

- Batch import/extract runs I/O on a worker thread behind an owner-modal progress dialog. UI state is snapshotted first and never touched from the worker.
- Items are processed **sequentially**, because manager state and ID allocation depend on the previous item. Each item can fail on its own, and the batch continues and reports a count.
- **Cancel stops between items, never inside one.** Writes are immediate and not transactional, so interrupting an item could leave a half-imported save or a half-written file. Cancel (or the window's close button) lets the current item finish, skips the rest, and still runs the batch's closing steps (saving the song manager, reloading), so every item done so far is kept and registered.
- Before it changes a save, Degausser keeps a restore point of the save's managers (**3DS Content → Restore Save from Backup…**). It still pays to work on a copy of the extdata.

### Deliberately not supported

- **Old `.bin` import**, which was a 3DS process-RAM dump workflow for capturing previewed shop songs. The shop is gone, so it wasn't restored.
- **MOFLEX movie playback.** See [extdata-and-packages.md](extdata-and-packages.md#playable-video-song-packs).

## Degausser3DS

The native 3DS app at `3ds/Degausser3DS` is a sibling product. It is not a .NET frontend or part of `Degausser.sln`, and it is versioned and released independently. Share knowledge through docs, test vectors and documented constants, never through a source or binary dependency.

## Namespace policy

Both assemblies keep the `Degausser` namespace. The project boundary is the architectural line, and a namespace rewrite would add noise without improving dependency direction.
