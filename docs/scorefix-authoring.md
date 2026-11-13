# Writing a scorefix

A scorefix is a plain `.txt` file that gives one song an English (romanised) title and, if the song has karaoke, romaji lyrics that follow the Japanese. Degausser reads them from a folder (see [architecture](architecture.md)); the files in `scorefixes/` are the shipped set. This page is the rulebook.

You need your own copy of the song (`.bbp`, or a movie's song). Nothing here includes songs, and none should be committed.

## The file

```
0001820A                      <- title ID, 8 hex digits (omit for custom songs and built-ins; see below)
――――――――――――                <- separator: 3+ of ― — - ─ (the tools write 12 ―)
The☆Bon Odori                 <- title, up to 3 lines
――――――――――――
Ha～ hanabi agareba～          <- romaji lyrics, aligned to the Japanese karaoke (empty for an instrumental)
DON DON PAN PAN
```

- **ID.** Official arrangements (and the movie songs) are matched by title ID, so a scorefix only applies to the song it was written for. Built-in ROM songs omit the header and are named after the ID (`80010000.txt`). BDX-converted and custom songs have no stable ID (it is a 16-bit checksum that can collide, and changes on import), so they are matched by a **lyrics fingerprint** instead: first line `lyrics:` plus 16 hex digits, then the separator, in place of the ID line. *Apply scorefix…* writes this header for you when the song is a custom one. The fingerprint is a SHA-256 of the Japanese karaoke (NFKC, whitespace removed), so different arrangements with the same lyrics share one scorefix, and a song with no lyrics has nothing to match (apply it by hand). Evidence level: design decision, tested on synthetic fixtures only; not yet checked against real BDX files.
- **Instrumentals.** A title-only file (nothing after the second separator) is fine: it changes the title and leaves the song as it is.
- **Where it goes.** `scorefixes/<group>/<English title> (<ID>).txt`. Folders nest to four levels; Degausser doesn't care what they're called. The ID in the name keeps different arrangements of one song apart. Drop characters Windows can't have in a file name (`? : * " < > | / \`).

## Title

- **Whose English title.** The catalogue's English title (`scorefix-template` prints it), or the game's own if the song has one. Don't invent a translation of a title that already has an official English name.
- **Limits** (ELF-proven layouts: `ListScore/Layout/Info_U.arc`, `TitlInpt_U.arc`): at most 3 lines, 50 characters in all, and each line must fit the song info panel (cbf_std at 14.4 px in 166 px). Break between words, and end the line before a break with a space: the song list shows the title on one line, joined without the line breaks, so `Bowser's Bullet` / `Bill Brigade` with no space reads "BulletBill". Editors that trim trailing whitespace remove it, so the validator rejects a break between two letters or digits that has no space. A break after a symbol can join with no space on purpose (`KAKAKATA☆` / `KATAOMOI-C`). `validate-scorefix-corpus.py` (maintenance tool, outside this repo) checks all of this for the shipped files; `scorefix-draft` already wraps for it.
- **Case.** Romaji titles use title case with particles in lowercase (`no ni wo o wa ga e to de mo ka yo ne ya kara made yori`): "Sekai ni Hitotsu Dake no Hana", "Hello, Again (Mukashi kara Aru Basho)". Katakana and English keep the game's own case (`ITAZURA na KISS`, `DEAR MY FRIEND`). File names follow the same romaji rule but use ordinary capitalisation, or the artist's own styling, instead of the game's all-caps (`Every Little Thing - Dear my friend.txt`, `LiSA - crossing field.txt`); `J-Pop/` files start with the artist.
- **Sorting.** Applying a scorefix also sets the song's `title_simple`, which the song list files it under, to the title in lowercase (see [bbp-format](bbp-format.md#2-jbmgritem--312-byte-catalogue-metadata)). You don't write it.

## Lyrics

Written for a reader who is singing along, so it follows the **Japanese karaoke line by line**:

- **Same line breaks as the Japanese.** One line of Japanese is one line of romaji; two or more Japanese line breaks in a row become one blank line. Don't join or split lines to make them tidier. Only where a line is wider than 313 px in sing mode (BasicRim 27 px, fourteen full-width characters, what the lower row shows; [research](research/2026-11-09-karaoke-line-widths.md)) is it split, between phrases: where the Japanese has a space, after a particle or punctuation, never so that a line starts with a particle, and never between a demonstrative or adjective and its noun or inside an idiom (*ki ni naru*). `scorefix-lines` does the breaking and casing for a new file; `scorefix-split` splits only the lines over the width in an existing one (below).
- **Kana-faithful romaji, no macrons.** `kyou`, `mou`, `doushite`; particles are `wa`, `wo`, `e` as written (`watashi wa`, `uta wo`, `mirai e`); づ is `zu`, ぢ `ji`; っち is `cchi`; ん before a vowel or y takes an apostrophe (`kon'ya`, `bon'odori`). Particles are separate words (`ni wa`, `ja nai`, `you ni`); one word stays one (`marude`, `honno`, `dakara`), and the same phrase is spaced the same way every time it comes back. Quotes are straight (`"`).
- **Katakana in UPPERCASE.** `DON DON PAN PAN`, `HAI`, and ordinary words the Japanese writes in katakana for effect (`MONO`, `KIMI`, `DOKIDOKI`). ー and ～ count as the character before (`DOKIIN`, `MUNYA～`), and so does ッ (`GYUTto`).
- **English spelling for clear loanwords and English phrases.** Each Japanese character gets one chunk. A chunk in parentheses is an override that covers exactly one Japanese character and is not displayed; an empty `()` covers a silent one. So `(PA)(RA)(D)(I)(SE)` over パラダイス displays PARADISE, and `(MA)(D)(E) IN (J)(A)(PA)(N)` over メイド イン ジャパン displays MADE IN JAPAN. English the Japanese already writes in letters (`Ｙｅａｈ`, `Ｔｏｎｉｇｈｔ`) is written as English, in the game's own case.
- **Casing.** Romaji from kana or kanji is lowercase, every line starts with a capital, and names keep theirs. A sentence continued over a wrapped line continues in lowercase.
- **Parentheses do two jobs.** A `(…)` standing where the Japanese has an ordinary character is an override for that one character and isn't shown (`(PA)(RA)…` above). But where the Japanese itself has （ or ）, a plain `(` or `)` in the romaji matches it literally and is shown, as in the game's own text. So backing vocals the Japanese puts in （ ） are written in `( )` too: `(HAI HAI HAI HAI!)` displays with its brackets. Leaving them out loses the line break there, because the aligner then has nothing to pin it to.
- **Alignment is the check.** Degausser retimes your romaji against the song's own karaoke timing, character by character, and refuses a file that doesn't line up. That proves the romaji follows the kana. It does not prove the romanisation is right; read it.

## Workflow

The helpers that draft, split and check scorefixes against a whole song collection are maintainer tools kept outside this repository, because they need a private corpus of songs. They:

1. list the official arrangements that match a song or franchise, with their Japanese lyrics line by line (`scorefix-template`);
2. write title-only drafts for a whole game, franchise or artist (`scorefix-draft`);
3. rebuild line breaks and casing from filled-in lyrics (`scorefix-lines`), or split only the lines wider than 313 px in an existing file (`scorefix-split`);
4. check that every file applies to its song (`scorefix-check`), and validate widths, limits and hashes for the files here (`validate-scorefix-corpus.py --update-manifest`).

Without them, one file can be tried in Now Playing with *Apply scorefix…*: it starts from the song's own title and Japanese lyrics, and refuses a file that doesn't align. Never put songs, `.bbp` files or personal paths in this repository.
