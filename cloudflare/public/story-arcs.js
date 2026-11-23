const words = ['', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine'];
const roman = ['', 'i', 'ii', 'iii', 'iv', 'v', 'vi', 'vii', 'viii', 'ix'];
const key = value => value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()
  .replace(/^(?:star trek\s+)?(?:(?:the\s+)?(?:next generation|original series)|deep space (?:nine|9)|voyager|enterprise)\s+/, '').replace(/^star trek\s+/, '').replace(/^the\s+/, '');
export function arcIsbnKey(value) {
  let isbn = String(value ?? '').replace(/[\s-]/g, '').toUpperCase();
  if (/^[0-9]{9}[0-9X]$/.test(isbn)) {
    if ([...isbn].reduce((sum, digit, i) => sum + (10 - i) * (digit === 'X' ? 10 : Number(digit)), 0) % 11) return null;
    isbn = '978' + isbn.slice(0, 9);
    isbn += (10 - [...isbn].reduce((sum, digit, i) => sum + Number(digit) * (i % 2 ? 3 : 1), 0) % 10) % 10;
  }
  return /^97[89][0-9]{10}$/.test(isbn) && [...isbn].reduce((sum, digit, i) => sum + Number(digit) * (i % 2 ? 3 : 1), 0) % 10 === 0 ? isbn : null;
}
const sameIsbn = (values, isbn) => isbn != null && values.some(value => arcIsbnKey(value) === isbn);
export const isArcOmnibus = (arc, book) => (book.kind ?? 0) === 0 && (sameIsbn(arc.omnibusIsbns, arcIsbnKey(book.isbn)) || arc.series.includes(book.series) && arc.omnibusTitles.some(title => key(title) === key(book.title)));
export function arcMatches(reference, arc, part, book) {
  if ((book.kind ?? 0) !== 0) return false;
  const isbn = arcIsbnKey(book.isbn);
  if (isbn) {
    for (const set of reference) for (const volume of set.parts) if (sameIsbn(volume.isbns, isbn)) return set.id === arc.id && volume.number === part.number;
    const omnibus = reference.find(set => sameIsbn(set.omnibusIsbns, isbn));
    if (omnibus) return omnibus.id === arc.id;
  }
  if (!arc.series.includes(book.series)) return false;
  if (isArcOmnibus(arc, book)) return true;
  const title = key(book.title);
  if ([part.title, `${arc.name} ${part.title}`, ...part.aliases].some(candidate => key(candidate) === title)) return true;
  for (const number of [String(part.number), words[part.number], roman[part.number]].filter(Boolean)) for (const label of ['', 'book ', 'part '])
    if ([`${arc.name} ${label}${number} ${part.title}`, `${arc.name} ${part.title} ${label}${number}`, `${part.title} ${arc.name} ${label}${number}`].some(candidate => key(candidate) === title)) return true;
  return false;
}
export function storyArcReport(entries, reference, arc, format, physicalOnly = false) {
  const inFormat = ({book}) => (format == null || book.format === format) && (!physicalOnly || book.format === 0 || book.format === 1);
  const slots = arc.parts.map(part => {
    const matches = entries.filter(({book}) => arcMatches(reference, arc, part, book));
    const owned = matches.filter(entry => entry.book.ownership === 0 && inFormat(entry));
    const wishlist = matches.filter(entry => entry.book.ownership === 1 && inFormat(entry));
    const otherFormats = matches.filter(entry => entry.book.ownership === 0 && !inFormat(entry));
    return {part, owned, wishlist, otherFormats, missing: !owned.length, status: owned.length ? owned.every(({book}) => isArcOmnibus(arc, book)) ? 'Owned via omnibus' : 'Owned' : wishlist.length ? 'On wishlist' : 'Missing'};
  });
  const matched = new Set(slots.flatMap(slot => slot.owned));
  const unmatched = entries.filter(entry => (entry.book.kind ?? 0) === 0 && entry.book.ownership === 0 && inFormat(entry) && arc.series.includes(entry.book.series) && (' ' + key(entry.book.title) + ' ').includes(' ' + key(arc.name) + ' ') && !matched.has(entry));
  return {arc, slots, unmatched, ownedCount: slots.filter(slot => !slot.missing).length};
}
