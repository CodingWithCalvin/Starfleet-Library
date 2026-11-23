import {parseBookNumber, seriesCodes, formats} from './catalog.js';
import {arcIsbnKey, arcMatches, isArcOmnibus, storyArcReport} from './story-arcs.js';
const titleKey = value => value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()
  .replace(/^(?:star trek\s+)?(?:(?:the\s+)?(?:next generation|original series)|deep space (?:nine|9)|voyager|enterprise)\s+/, '').replace(/^star trek\s+/, '').replace(/^the\s+/, '');
export function parseNumberedReference(tsv) {
  return tsv.trim().split(/\r?\n/).map(line => {
    const [series, number, title, isbn] = line.split('\t');
    if (!title || !arcIsbnKey(isbn) || !parseBookNumber(number) || !/^[0-8]$/.test(series)) throw new Error('Invalid numbered reference');
    return {series:Number(series), number:Number(number), title, isbn:arcIsbnKey(isbn)};
  });
}
export function buyingGuide(entries, novels, arcs, series, format, first, last, physicalOnly = false, startedArcsOnly = true) {
  const books = entries.map(entry => entry.book);
  const inFormat = b => (format == null || b.format === format) && (!physicalOnly || b.format === 0 || b.format === 1);
  const findIsbn = isbn => { const key = arcIsbnKey(isbn); return key ? novels.find(n => n.isbn === key) : undefined; };
  const identifyNumber = book => {
    if ((book.kind ?? 0) !== 0) return null;
    const known = findIsbn(book.isbn); if (known) return known;
    const number = parseBookNumber(book.bookNumber);
    if (number != null) return novels.find(n => n.series === book.series && n.number === number) ?? {series:book.series, number, title:book.title, isbn:arcIsbnKey(book.isbn) ?? ''};
    const exact = novels.filter(n => n.series === book.series && titleKey(n.title) === titleKey(book.title));
    if (exact.length === 1) return exact[0];
    for (const arc of arcs) {
      if (isArcOmnibus(arc, book)) continue;
      for (const part of arc.parts.filter(p => arcMatches(arcs, arc, p, book)))
        for (const isbn of part.isbns) { const match = findIsbn(isbn); if (match) return match; }
    }
    return null;
  };
  const identity = book => {
    const n = identifyNumber(book); if (n) return `number:${n.series}:${n.number}`;
    for (const arc of arcs) {
      if (isArcOmnibus(arc, book)) continue;
      for (const part of arc.parts) if (arcMatches(arcs, arc, part, book)) return `arc:${arc.id}:${part.number}`;
    }
    return `title:${book.kind ?? 0}:${book.series}:${titleKey(book.title)}`;
  };
  const identities = new Map(books.map(b => [b, identity(b)]));
  const keyOf = b => identities.get(b) ?? identity(b);
  const owned = books.filter(b => b.ownership === 0), items = new Map(), warnings = [];
  const covered = new Set(owned.filter(inFormat).map(keyOf));
  for (const arc of arcs) for (const part of arc.parts.filter(p => owned.some(b => inFormat(b) && arcMatches(arcs,arc,p,b))))
    for (const isbn of part.isbns) { const n = findIsbn(isbn); if (n) covered.add(`number:${n.series}:${n.number}`); }
  const selectedFormat = physicalOnly ? 'Print' : formats[format] ?? 'Any format';
  const add = (key, title, isbn, reason, formatLabel, onWishlist = false) => {
    const old = items.get(key);
    if (old) { if (!old.onWishlist) old.title = title; old.reasons = [...new Set([...old.reasons, reason])]; old.formats = [...new Set([...old.formats, formatLabel])]; old.onWishlist ||= onWishlist; old.isbn ||= isbn; }
    else items.set(key, {key, title, isbn, reasons:[reason], formats:[formatLabel], onWishlist});
  };
  for (const book of books.filter(b => b.series === series && b.ownership === 1 && inFormat(b))) {
    const key = keyOf(book);
    if (owned.some(b => b.format === book.format && keyOf(b) === key)) continue;
    const number = identifyNumber(book);
    add(key, book.title, arcIsbnKey(book.isbn) ?? '', book.kind === 1 ? 'Wishlist · COMIC' : 'Wishlist', formats[book.format], true);
    if (number) add(key, book.title, number.isbn, `${seriesCodes[number.series]} #${number.number}`, formats[book.format]);
  }
  if (Number.isInteger(first) && Number.isInteger(last) && first >= 1 && last >= first && last <= 1000) {
    for (let number = first; number <= last; number++) {
      const key = `number:${series}:${number}`;
      if (covered.has(key)) continue;
      const novel = novels.find(n => n.series === series && n.number === number);
      add(key, novel?.title ?? `${seriesCodes[series]} #${number} (title unknown)`, novel?.isbn ?? '', `${seriesCodes[series]} #${number}`, selectedFormat);
    }
  } else warnings.push('Numbered gaps omitted: choose a valid first and last number. Some series have no standard numbered run.');
  for (const arc of arcs.filter(a => a.series.includes(series))) {
    const report = storyArcReport(entries, arcs, arc, format, physicalOnly);
    if (startedArcsOnly && !report.ownedCount && !report.unmatched.length) continue;
    for (const slot of report.slots.filter(s => s.missing)) {
      const isbn = slot.part.isbns.map(arcIsbnKey).find(Boolean) ?? '';
      const key = identity({title:slot.part.title, isbn, series});
      if (owned.some(b => inFormat(b) && keyOf(b) === key)) continue;
      add(key, slot.part.title, isbn, `${arc.name} ${slot.part.number}/${arc.parts.length}`, selectedFormat);
    }
    if (report.unmatched.length) warnings.push(`${arc.name}: review ${report.unmatched.length} unmatched owned titles.`);
  }
  const uncertain = owned.filter(b => b.series === series && (b.kind ?? 0) === 0 && inFormat(b) && !identifyNumber(b)).length;
  if (uncertain) warnings.push(`${uncertain} owned editions have no recognized series number. Review numbering; some may be unnumbered releases.`);
  for (const item of items.values()) if (item.formats.length > 1 && item.onWishlist && ['Print', 'Any format'].includes(selectedFormat)) item.formats = item.formats.filter(f => f !== selectedFormat);
  return {items:[...items.values()].sort((a,b) => Number(b.onWishlist) - Number(a.onWishlist) || a.title.localeCompare(b.title)), warnings};
}
export const shoppingList = report => report.items.map(item => `☐ ${item.title} — ${item.reasons.join(' · ')} · ${item.formats.join(', ')}` + (item.isbn ? ` · ISBN ${item.isbn}` : '')).join('\n');
