# Karaoke line widths on hardware (2026-11-09, corrected 2026-11-10)

Hardware photos (New 3DS XL) of the built-in STARLIGHT (80010000, scorefixed), the official 360° (90002819) and 心絵 (80022721), in band play, sing mode (vocal part) and the studio lyric editor; on 2026-11-10, the scorefixed Ageha Chou (000029A3) in sing mode and the studio editor. Lyric text is described by width, not quoted, except the lines the measurements hang on.

## Result

**Sing mode is the narrowest place lyrics are shown, and Nintendo writes every line to fit it: at most 14 full-width characters.** It draws the line in BasicRim at about 27 px, with full-width Latin and U+3000 at their ASCII widths, and doesn't wrap or scroll. A full-width glyph is 22.34 px there, so 14 of them are 312.8 px; the lower row shows about 314 px. The scorefix lyric budget is therefore **313 px, BasicRim 27 px** (`basicrim_sing_27px_advances.csv` in the maintainer's karaoke-wordwrap tool).

The first version of this note (2026-11-09) set the budget at 360 px, the widest official line, on the assumption that every official line fits. The Ageha Chou photo disproved that: a 330 px line is cut on the lower row.

## Evidence

| Where | Renderer | Long line | Evidence level |
|---|---|---|---|
| Band play | not identified | fits | hardware: STARLIGHT looks fine |
| Sing mode | drawn in code (no layout pane): BasicRim ≈ 27 px, two rows | cut at the right edge; no wrap, no scroll | font and size: hardware photo fit; budget: hardware photo and corpus agree |
| Studio lyric editor | `Edit/Layout/LyrcInpt.arc#TxtLyrc` (264 px, BasicRim 20 px) and `ULCD/LyrcInpt_U.arc#TxtLyrc` (230 px, BasicRim 17 px), 2 px character spacing | wraps mid-word | **hardware-verified**, within about 0.6 % |

### Sing mode

- **Font and size.** 360° line 19 (full-width in the file: ｏｎｅ　ｌｉｔｔｌｅ…) shows as "one little kiss little kiss", 273 px wide, and the next line (kana with one U+3000) 140 px. Only BasicRim with full-width Latin and U+3000 drawn at their ASCII widths gives the same size from both rows (27.0 / 26.6 px); cbf_std (24.4 / 22.9) and Basic (23.3 / 22.9) are less consistent, and the text has BasicRim's outline. Extra spacing between characters would push the two estimates about a pixel apart per pixel of spacing (the lines have very different character counts), so there is none to speak of. STARLIGHT's line, cut at the screen edge after 381 px, fits the same size (27.2 px). Measured from photos: ± about half a pixel.
- **Full-width is narrow.** BasicRim's own full-width glyphs are 24 units wide, so the renderer (not the font) maps U+FF01–FF5E and U+3000 to ASCII widths. 心絵's widest lines are mostly leading U+3000 indentation, which this collapses.
- **Two rows.** The current line is on the upper row (x ≈ 51 for 360°, x ≈ 19 for STARLIGHT's over-wide line); the next one on a lower row, at x ≈ 170 for a short line. For Ageha Chou's 330 px "Asobu you ni sugata miseta" the lower row started only about 16 px right of the upper one, so it does move left for a long line, but the photo (calibrated on the upper line, 256 px in the model) shows only about 314 px of it, cut inside the last letter. The model's prefix widths match the cut ("…miset" 315.6 px), so the widths are right and the row is what limits.
- **The budget.** Official lines in this model: median 197 px. 4,409 official lines are exactly 312.8 px, fourteen full-width characters, and only 54 of 260,161 (0.02 %) are wider than 314 px, against 4,495 wider than 310 px. Nintendo's line length is fourteen characters; that is what the lower row shows.

### Studio editor

Width = Σ BasicRim advance × (size ÷ FINF width 29) + 2 px per character. The observed break points land where the game put them in both panes (the text before each break: 225.6 / 228.6 px in 230 px, 257.0 / 260.4 px in 264 px; one more character: 236.4 / 239.3 px, 269.3 / 272.8 px). Ageha Chou's line breaks one character earlier than the model in the 264 px pane ("…mis" is 262.6 px in the model), so real widths are about 0.5–0.6 % larger, which fits every observation. Official Japanese wraps there too (twenty kana are 321 px in the 230 px pane), so it is not a target.

## What changed

- 2026-11-09: `validate-scorefix-corpus.py` and `Degausser.Corpus scorefix-lines` measured lyric lines in the sing-mode model with a 360 px budget, and the lines over it were split.
- 2026-11-10: the budget is 313 px. `Degausser.Corpus scorefix-split` split the 884 lines over it, and only those: it breaks where the Japanese has a space, after a particle or punctuation, never so that a line starts with a particle or an auxiliary, and never between a demonstrative or adjective and its noun; each split was then read and 25 were moved by hand (idioms such as *ki ni naru*, compounds such as *hanabi taikai*).
- Titles keep the cbf_std song-info-panel check; the band-play renderer is still unidentified.
