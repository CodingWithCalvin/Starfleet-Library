export const seriesNames = ['The Original Series', 'The Next Generation', 'Deep Space Nine', 'Voyager', 'Enterprise', 'Discovery', 'Picard', 'Other / Crossover', 'Strange New Worlds'];
export const seriesCodes = ['TOS', 'TNG', 'DS9', 'VOY', 'ENT', 'DIS', 'PIC', 'OTHER', 'SNW'];
export const formats = ['Paperback', 'Hardcover', 'E-book', 'Audiobook'];
export const readingStates = ['Unread', 'Reading', 'Finished'];
export function parseBookNumber(value) {
  const text = String(value ?? '').trim().replace(/^#\s*/, '');
  const number = Number(text);
  return /^[0-9]+$/.test(text) && Number.isInteger(number) && number >= 1 && number <= 1000 ? number : null;
}
export function suggestedNumberEnd(entries, series) {
  return Math.max([97, 63, 27, 21][series] ?? 0, ...entries.filter(({book}) => book.series === series && (book.kind ?? 0) === 0).map(({book}) => parseBookNumber(book.bookNumber) ?? 0));
}
export function numberedReport(entries, series, format, first, last, physicalOnly = false) {
  if (!Number.isInteger(first) || !Number.isInteger(last) || first < 1 || last < first || last > 1000) throw new RangeError('Choose a whole-number range between 1 and 1000.');
  const selected = entries.filter(({book}) => book.series === series && (book.kind ?? 0) === 0);
  const inFormat = ({book}) => (format == null || book.format === format) && (!physicalOnly || book.format === 0 || book.format === 1);
  const owned = selected.filter(entry => entry.book.ownership === 0 && inFormat(entry));
  const slots = Array.from({length: last - first + 1}, (_, index) => {
    const number = first + index;
    const matches = selected.filter(({book}) => parseBookNumber(book.bookNumber) === number);
    const copies = matches.filter(entry => entry.book.ownership === 0 && inFormat(entry));
    const wishlist = matches.filter(entry => entry.book.ownership === 1 && inFormat(entry));
    const otherFormats = matches.filter(entry => entry.book.ownership === 0 && !inFormat(entry));
    return {number, owned: copies, wishlist, otherFormats, missing: copies.length === 0, status: copies.length ? 'Owned' : wishlist.length ? 'On wishlist' : 'Missing'};
  });
  return {slots, needsNumber: owned.filter(({book}) => parseBookNumber(book.bookNumber) == null), outsideRange: owned.filter(({book}) => {const number = parseBookNumber(book.bookNumber); return number != null && (number < first || number > last);})};
}
export const catalogLabel = book => [book.kind === 1 ? 'COMIC' : '', seriesCodes[book.series] ?? 'OTHER'].filter(Boolean).join(' · ');
const collator = new Intl.Collator(undefined, {numeric: true, sensitivity: 'base'});
const normalize = value => String(value ?? '').normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase();
const titleKey = title => title.normalize('NFC').trim().replace(/\s+/gu, ' ').toUpperCase();
export const sameCoverTitle = (first, second) => titleKey(first.title) === titleKey(second.title);
export const sameCoverGroup = (first, second) => first.format === second.format && first.ownership === second.ownership && sameCoverTitle(first, second);
// Group the filtered, sorted view without modifying individual edition records.
export function groupCovers(entries) {
  const groups = new Map();
  for (const entry of entries) {
    const key = JSON.stringify([titleKey(entry.book.title), entry.book.format, entry.book.ownership]);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(entry);
  }
  return [...groups.values()];
}
export function filterBooks(entries, filters) {
  const query = normalize(filters.search).trim();
  const isbn = query.replace(/[^0-9x]/g, '');
  const isbnQuery = /^[0-9x\s-]+$/.test(query) && isbn.length > 0;
  const results = entries.filter(({book}) => book && book.ownership === filters.ownership &&
    (filters.series === '' || book.series === Number(filters.series)) &&
    (filters.format === '' || book.format === Number(filters.format)) &&
    (filters.kind === '' || (book.kind ?? 0) === Number(filters.kind)) &&
    (filters.reading === '' || book.readingStatus === Number(filters.reading)) &&
    (!query || normalize([book.title, book.authors, book.isbn, book.bookNumber, seriesNames[book.series], catalogLabel(book)].join(' ')).includes(query) ||
      (isbnQuery && normalize(book.isbn).replace(/[^0-9x]/g, '').includes(isbn))));
  return results.sort((a, b) => {
    let order = 0;
    if (filters.sort === 'newest') order = (Date.parse(b.book.addedAt) || 0) - (Date.parse(a.book.addedAt) || 0);
    if (filters.sort === 'author') order = collator.compare(a.book.authors, b.book.authors);
    if (filters.sort === 'series') order = collator.compare(seriesNames[a.book.series], seriesNames[b.book.series]) || collator.compare(a.book.bookNumber, b.book.bookNumber);
    return order || collator.compare(a.book.title, b.book.title) || (a.book.format - b.book.format) || collator.compare(a.id, b.id);
  });
}
