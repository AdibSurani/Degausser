# Scorefixes

English titles and romaji karaoke lyrics for Band Brothers P songs, one `.txt` file per song. Degausser uses them to give songs English titles (so they sort under the right letter on the console) and to show romaji lyrics in sing mode, timed to the song's own Japanese karaoke.

| Folder | What's in it |
|---|---|
| `J-Pop/` | 110 songs from the official catalogue, named after the artist |
| `Games/` | `Rhythm_Heaven/` and `SM3DW/`: English titles for every arrangement in the catalogue, with lyrics for the songs that have karaoke |
| `Movies_31/` | the 31 downloadable-movie songs that have karaoke |
| `Builtins_7/` | the 7 songs built into the game, named after their ID |
| `BDX/` | songs from Band Brothers DX with no official BBP; matched by a hash of their Japanese lyrics, since a converted BDX has no stable ID |

## Using them

Release downloads include this folder next to the program, and Degausser finds it there. To use another copy, point **Settings → Scorefixes** at it. Songs that have a scorefix can then be imported with their English title and lyrics (**3DS Content → Use English titles and lyrics**), and the catalogue lists them as the **Scorefixes** collection. To try a single file, use **Apply scorefix…** in Now Playing: it checks the file against the song's Japanese karaoke and refuses one that doesn't line up.

## Writing and fixing them

[docs/scorefix-authoring.md](../docs/scorefix-authoring.md) has the file format and the style rules: title limits, romanisation, line breaks, and the 313 px lyric width limit. Corrections are welcome. The romaji follows the Japanese kana by kana, so read it against the original before changing a reading.

You need your own copy of the songs. None are included here.
