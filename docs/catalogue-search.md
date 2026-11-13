# Official BBP Catalogue: search and browse

Degausser embeds a compact index generated from **BBP Catalogue v3.0**. The full
normalized JSON/CSV/SQLite exports remain the source of truth. The embedded
resource is only the subset the app needs to search and browse.

## UI

In the Avalonia desktop client the catalogue lives in the **Library** pane, with one search box for your files and the catalogue together:

- **My files** (the default) lists only your library files, but a local copy of an official arrangement is found by the catalogue's English title, artist and work too. Its **Title** and **Author** are always what the file says; the catalogue adds an **English title**, the artist and the work. An official arrangement you don't have has no file, so it shows the catalogue's original title. Searching "Gundam" finds the Gundam songs you already have.
- **Whole catalogue** also lists the official arrangements you don't have, greyed out (`Ctrl+Shift+F` switches to it and focuses the search box). They can't be imported, and they preview only if an open save has them.
- Every row is one file or one official arrangement. Two arrangements of the same song are two rows, told apart by author, parts and features. A row you don't have is greyed; its tooltip says whether it is in an open save. Authors migrated from DX (`ＤＸいしょく＠name`) are shown as "name (DX)". The catalogue calls the author the *arranger*; the game and the rest of Degausser say Author.

The catalogue used to be a separate tab, with a song list over an arrangements list. It moved into the Library pane because most people will have few or no official files: the catalogue gives the pane something to show from the first run, and it gives the library search the catalogue's English and franchise data.

Search covers:

- curated English/romanized and original titles;
- title aliases;
- canonical/original/alias artist names;
- works, games, game series and franchises;
- genres;
- official arrangement filenames, arrangers, submitted titles/artists and remarks.

The search normalization deliberately preserves Japanese dakuten/handakuten,
matching the v3.0 browser behaviour, while making Latin accents insensitive.
In the browser, where .NET can't run NFKC, `TextFolding` does it from a table. On
the catalogue's 41,906 search strings, it differs from real NFKC in 6, all involving a lone ゜.

### Browsing collections

Typing in the search box also shows up to five matching **collections** (with more than one song) as buttons above the results: franchises, game series, games, works (anime, drama, film, commercials…), theme categories and artists. Picking one browses it:

- A collection includes everything beneath it. *Rhythm Heaven* (franchise) covers Rhythm Tengoku, Rhythm Heaven (DS) and Rhythm Heaven Fever, and *BLEACH* includes the film *Bleach: Fade to Black*.
- Songs are ordered by sub-collection, then by usage (OP 1, OP 2, … ED 1, … theme, insert, CM), then by title. The **In** column shows the usage and sub-collection.
- The search box narrows the collection's own list. **My files** / **Whole catalogue** still applies.
- **Import N to save** imports one library copy of every song in the collection that isn't already in the open save. The copy is chosen by `CatalogueSong.PreferredArrangement`: English lyrics, then karaoke, then more parts, then native P before DX migrations, then Vocaloid data, then catalogue order. It asks for confirmation first.

**Scorefixes** is a collection Degausser adds itself (kind `feature`): the arrangements that have a scorefix, whether it gives English lyrics or, for a song with no karaoke, only an English title (so it is empty, and not offered, until a scorefix folder is loaded). Browsing it lists only those arrangements, not the other versions of the same songs. It isn't in the generated index, because scorefixes ship with the app rather than the catalogue. It is shown before anything is typed, and it is found by "scorefix", "English" or "romaji". Importing it with *Use English titles and lyrics* turned on gives a new save every song Degausser has English for.

Collections and their hierarchy are built by the index generator:

- **Duplicates are merged.** A game, a game series and a work with the same name are merged. A same-named work that is essentially the whole franchise (at least 90% of its songs) is folded into the franchise.
- **Parents come from containment.** A work/game/series sits under a franchise if all its songs belong to that franchise.
- **Anime and drama have no franchise records**, so a work is placed under another work whose name is a prefix of its own ("Gintama' ", "Gintama: The Movie" → "Gintama").

### English names

1,586 works and 1,282 artists have only Japanese names in the catalogue. `english-names.json` supplies English display names for them, and the original Japanese stays searchable. Every entry has a `confidence`:

| confidence | meaning |
|---|---|
| `official` | established English title or name |
| `romanized` | no established English; Hepburn without macrons |
| `translated` | descriptive name translated (e.g. commercials, theme buckets) |
| `uncertain` | identity or reading unsure: **review these** (44 works, 114 artists) |

Fix wrong entries there, then regenerate the index.

These were machine-suggested on 2026-11-01 and have not been reviewed by a person. Works whose `kind` is `category` (e.g. 子守唄 → *Lullabies*) are theme buckets, not real works, and appear as "theme" collections.

### Corrections and umbrella groups

`corrections.json` records hand-curated fixes the generator applies on top of the source catalogue:

- `work_names`, `work_media`: renames (e.g. the 2006 *Journey to the West* drama, which would otherwise merge with the anime *Saiyuki*) and media-type fixes (Toho's *Rodan* and *Varan* were tagged anime).
- `not_works`: catalogue "works" that are really song titles or version labels.
- `distinct_artist_names`: artist names shared by different people, which must not be merged.
- `franchise_groups`: regex umbrellas for families without a catalogue franchise (Yu-Gi-Oh!, Macross, Naruto, Haruhi, Evangelion, Monogatari…).

Same-named works and artists are merged, so ONE PIECE and ワンピース become one work, and THE BLUE HEARTS and ザ・ブルーハーツ one artist. These corrections belong in the next release of the source catalogue; the overlay keeps them reproducible until then.

### Arrangement stats and remarks

- `arrangement-stats.json` holds facts measured from the official files with the maintenance tool `Degausser.Corpus bbp-stats`: length, parts used, lyrics, Piano/Guitar/drum parts, and attacks per minute. The Arrangements grid shows them.
- `CatalogueSong.PreferredArrangement` uses them to pick the best copy: lyrics, then more parts, then native P over DX migration, then Vocaloid data.
- `remarks-en.json` holds English translations of 5,420 Japanese arrangement remarks (e.g. *"TV Tokyo anime Gintama, insert song"*). 84 are marked uncertain. The Notes column shows them, and they are searchable.

### Arrangements

Selecting a logical song shows all of its official BBP arrangements. Degausser
matches those arrangements to the local Library and all currently open 3DS
content sets using the real `JbMgr.Item.titleID`, so an exact local copy can be
previewed without any filename convention.

The local synthetic BDX→BBP title-ID range is `0x80000001..0x8000FFFF`. No
v3.0 official arrangement occupies that range (official high IDs begin at
`0x80022710`), so catalogue/local title-ID matching cannot collide with the
converter's generated IDs in the current corpus.

## Embedded resource

`src/Degausser.Core/Catalogue/bbp_catalogue_v3.0.compact.json.gz`

Current contents:

- 7,952 logical songs;
- 9,722 official arrangements;
- arrangement title ID, filename, arranger, namespace/distribution class and
  Vocaloid-data flag;
- display/search metadata for each logical song;
- 5,048 collections (franchises, series, games, works, theme categories, artists)
  with parent links, plus each song's memberships and usages.

The compressed resource is about 1.6 MB (schema 2, with 5,048 collections, arrangement stats and English remarks). Degausser does **not** embed the ~55 MB full catalogue export, and that export
is not in the repository. It is the source of truth, and the index can't be
regenerated without it. The author keeps it next to the repository, outside the public tree. Regenerating from it reproduces the committed resource byte-for-byte.

## Regenerating

The generator and the overlay files named above (`english-names.json`, `corrections.json`, `arrangement-stats.json`, `remarks-en.json`) are maintainer tooling outside this repository. Given the v3.0 normalized export:

```bash
python build-catalogue-index.py /path/to/bbp_catalogue_normalized_v3.0.json
```

The generator is deterministic (gzip timestamp fixed to zero), so rebuilding
from the same normalized catalogue produces byte-identical resource output.

`CatalogueTests` verifies the 7,952/9,722 counts, title-ID lookup and regression
queries including Japanese dakuten searches.
