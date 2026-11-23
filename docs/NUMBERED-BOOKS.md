# Numbered books and missing volumes

## Buying guide

**Missing books → Buying guide** is a compact shopping list in Windows, iPhone and the browser. It combines wishlist entries, missing numbered volumes and missing parts of story arcs under the same Series and Format filters. Each row contains the title, number/arc part, desired format and an ISBN reference when available. **Copy buying guide** produces a plain checklist for notes. Wishlist entries come first; duplicate suggestions are combined. The guide does not add wishlist records or mark purchases as owned.

Print books is the default. A specific hardcover wish remains even if you own a paperback. Story arcs default to sets already started in the chosen format. The first/last range applies only to numbered gaps; an empty or invalid range still allows wishlist and arc suggestions. Matching notes are collapsed to keep the list easy to scan.

Unlike the saved-number report, the guide also recognizes verified reference ISBNs, exact reference titles and known arc title variants without changing saved numbers. Unrecognized or ambiguous editions remain flagged for review. Reference ISBNs identify one known edition; a different printing or desired format may have a different ISBN. Supported arcs and numbered ranges are limited to the references below.

The **Series** and **Format** filters apply to both the gap report and the numbering review. The default format is **Print books (paperback + hardcover)**, so ebook and audiobook ownership does not fill a physical collection gap. You can also choose Paperback, Hardcover, E-book, Audiobook, or Any format separately.

Open **Missing books** on Windows or in the browser, or **Missing books / story arcs** from an iPhone shelf. Choose **Numbered series**, a series, a format (or Any format), and an inclusive first/last number. The view counts saved, positive whole-number `BookNumber` values from owned books. Multiple copies of a number count once. Comics and other series do not count. Wishlist entries are labeled **On wishlist**, but remain missing until owned. When a particular format is selected, an owned copy in another format does not fill that gap.

The default ranges are the classic Pocket Books numbered novel lines: TOS 1–97, TNG 1–63, DS9 1–27 and Voyager 1–21. They do not include every novel in those franchises. Other series need a range entered manually unless the catalog already contains a valid number. Adjust the range for the numbering scheme you use; do not mix a trilogy's internal numbers with the main series numbers.

Blank or invalid numbers are shown for review, not inferred from titles. They may be unnumbered releases or incomplete records, so the gap list is provisional until you have checked them. Numbers outside your selected range are reported separately. Reading a gap report never changes catalog data.

## Fill in existing records

- On Windows, choose **Review numbering**. Filter by series/format or search for a title or ISBN. **Set book number** opens a small editor with the saved series and number. Where available, **Use ISBN match** fills the verified main series and number. Changes are saved only when you select **Save**.
- Alternatively, open an edition's full editor and select **Fill details from ISBN** at the top. The iPhone editor provides **Look up ISBN** in the same position. Review the number and series before saving. This lookup also refreshes other edition metadata.
- Leave truly unnumbered books blank. Existing numbers are retained when lookup has no reliable numbering result. The browser remains read-only; save edits in a native app, sync, then refresh the browser.

## ISBN numbering sources

The shared library embeds `cloudflare/public/numbered-novels.tsv`, an ISBN-to-main-series-number reference containing 208 classic numbered novels. ISBN-10 and equivalent ISBN-13 values match the same record. It uses the numbered-novel tables from these bibliographies, checked November 23, 2026:

- [Star Trek novels — Original Series numbered novels](https://en.wikipedia.org/wiki/List_of_Star_Trek_novels)
- [The Next Generation novels](https://en.wikipedia.org/wiki/List_of_Star_Trek:_The_Next_Generation_novels)
- [Deep Space Nine novels](https://en.wikipedia.org/wiki/List_of_Star_Trek:_Deep_Space_Nine_novels)
- [Voyager novels](https://en.wikipedia.org/wiki/List_of_Star_Trek:_Voyager_novels)

The table stores factual title, series, number and ISBN fields, not source descriptions. Reference tests validate ISBN checksums, uniqueness and complete numbering ranges. Corrections and additional printing ISBNs should cite the edition or a bibliography and preserve exact ISBN matching.

For an ISBN absent from this reference, lookup checks the Open Library edition endpoint (`/isbn/{isbn}.json`) for explicit `series` metadata such as `Star Trek: The Next Generation #21`. This is separate from the [Books API](https://openlibrary.org/dev/docs/api/books) used for title, author, publication and cover metadata. The parser accepts only an unambiguous main-series number; it does not guess from title digits, dates or miniseries names. Many editions lack this field, and reprints/ebooks can have ISBNs absent from the reference. Enter the number manually in those cases.

Optional numbering requests have a short timeout. Missing, malformed or unavailable numbering metadata does not discard an otherwise successful title/author lookup. Reference data is local to the app; no new catalog-hosting service is involved.

## Multi-part story arcs

Select **Story arcs** in the same view to see named parts and owned/missing totals. Series and Format apply here too. **Only sets I own in this format** focuses on sets you have started; turn it off to browse all supported sets for that franchise. **Only missing parts** hides the owned rows without changing totals. Desktop and browser users can copy the missing titles.

This view uses a separate factual reference instead of the edition's main `BookNumber`. For example, *The First Virtue* is Double Helix part 6 and TNG #56. Neither report rewrites your catalog. An ebook omnibus covers its constituent stories under E-book or Any format, but does not count toward a print collection. Recognized omnibuses are labeled **Owned via omnibus** when they are the only owned editions covering a part. Collectors seeking individual volumes should also check those labels.

The initial reference contains 30 parts across seven complete sets: Double Helix (6), The Q Continuum (3), The Genesis Wave (3), Cold Equations (3), A Time to... (9), Millennium (3) and Destiny (3). Genesis Force is a subsequent standalone sequel, outside the three-volume Genesis Wave set tracked here. Crossover sets appear under their associated franchises; the whole story is counted even when its books are filed under different franchises.

`cloudflare/public/story-arcs.json` is the single reference used by the browser and embedded in the shared .NET library. Each set includes source links, known ISBNs, title variants, and explicitly recognized omnibus editions. Alongside the bibliographies above, publisher references include the [Double Helix contents](https://www.simonandschuster.com/books/Double-Helix-Omnibus/Peter-David/Star-Trek/9780743473934), [Q books](https://www.simonandschuster.com/p/star-trek-q-books), [Destiny contents](https://www.simonandschuster.net/books/Star-Trek-Destiny/David-Mack/Star-Trek-The-Next-Generation/9781451657241) and [Genesis Wave final volume](https://www.simonandschuster.net/books/The-Star-Trek-The-Next-Generation-The-Genesis-Wave-Book-3/John-Vornholt/Star-Trek-The-Next-Generation/9780743546591).

Matching prefers a verified ISBN, including equivalent ISBN-10/13 forms. Otherwise it accepts exact normalized title variants in the associated franchise, including common franchise prefixes and spelled-out/Roman part numbers. It does not use fuzzy substring matching to mark a part owned. Ambiguous titles mentioning a supported arc appear as unmatched for review. Unlisted arcs, foreign-language titles, and unrecognized collected editions may require extending the reference; this is not an exhaustive Star Trek bibliography. Keep each set's numbering contiguous, validate added ISBNs, and cite its contents when adding a set or omnibus.
