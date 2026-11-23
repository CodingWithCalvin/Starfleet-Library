import {seriesNames, formats, numberedReport, suggestedNumberEnd} from './catalog.js';
import {storyArcReport} from './story-arcs.js';
const $ = id => document.getElementById(id);
function text(tag, content, className = '') { const node = document.createElement(tag); node.textContent = content; node.className = className; return node; }
let entries = [], bookButton;
let arcReference = null, arcError = '';
const isArcView = () => $('missing-view-mode').value === 'arcs';
function arcReports() {
  const [series, format, , , physicalOnly] = selection();
  return arcReference.filter(arc => arc.series.includes(series)).map(arc => storyArcReport(entries, arcReference, arc, format, physicalOnly))
    .filter(report => !$('arc-started').checked || report.ownedCount > 0 || report.unmatched.length > 0);
}
function selection() {
  const format = $('numbered-format').value;
  return [Number($('numbered-series').value), format === '' || format === 'print' ? null : Number(format), Number($('numbered-first').value), Number($('numbered-last').value), format === 'print'];
}
export function initializeNumbering(makeBookButton) {
  bookButton = makeBookButton;
  fetch(new URL('./story-arcs.json', import.meta.url)).then(response => { if (!response.ok) throw new Error(); return response.json(); })
    .then(reference => { if (!Array.isArray(reference)) throw new Error(); arcReference = reference; if (isArcView()) renderNumbering(entries); })
    .catch(() => { arcError = 'Could not load the story-arc reference. Reload the page to retry.'; if (isArcView()) renderNumbering(entries); });
  seriesNames.forEach((name, index) => $('numbered-series').add(new Option(name, index)));
  formats.forEach((name, index) => $('numbered-format').add(new Option(name, index)));
  $('numbered-series').value = '1';
  $('numbered-series').addEventListener('change', () => {
    $('numbered-first').value = '1';
    $('numbered-last').value = suggestedNumberEnd(entries, Number($('numbered-series').value)) || '';
    renderNumbering(entries);
  });
  for (const id of ['numbered-format', 'numbered-only-missing', 'numbered-first', 'numbered-last', 'missing-view-mode', 'arc-started'])
    $(id).addEventListener(id.endsWith('first') || id.endsWith('last') ? 'input' : 'change', () => renderNumbering(entries));
  $('numbered-copy').addEventListener('click', async () => {
    try {
      const [series, format, first, last, physicalOnly] = selection();
      if (isArcView()) {
        const titles = arcReports().map(report => `${report.arc.name}\n${report.slots.filter(slot => slot.missing).map(slot => `Part ${slot.part.number}: ${slot.part.title}`).join('\n') || 'Complete'}`).join('\n\n');
        await navigator.clipboard.writeText(`${seriesNames[series]} · ${physicalOnly ? 'Print books' : formats[format] ?? 'Any format'}\n\n${titles}`);
        $('library-status').textContent = 'Missing titles copied.'; return;
      }
      const report = numberedReport(entries, series, format, first, last, physicalOnly);
      await navigator.clipboard.writeText(`${seriesNames[series]} · ${physicalOnly ? 'Print books' : formats[format] ?? 'Any format'} · #${first}–#${last}\nNot recorded as owned: ${missingNumbers(report) || 'None'}\nBased on saved book numbers; ${report.needsNumber.length} editions have blank or invalid numbers.`);
      $('library-status').textContent = 'Missing numbers copied.';
    } catch { $('library-status').textContent = 'Could not copy. You can select the missing-number list below and copy it.'; }
  });
}
const missingNumbers = report => report.slots.filter(slot => slot.missing).map(slot => '#' + slot.number).join(', ');
export function clearNumbering() {
  entries = [];
  for (const id of ['numbered-summary', 'numbered-list', 'numbered-tiles', 'numbered-review-list']) $(id).replaceChildren();
  $('numbered-review-title').textContent = 'Review incomplete numbering';
}
export function renderNumbering(current) {
  clearNumbering(); entries = current;
  $('numbered-copy').disabled = true;
  $('numbered-review').hidden = true;
  $('numbered-first').closest('label').hidden = $('numbered-last').closest('label').hidden = isArcView();
  $('arc-started-label').hidden = !isArcView();
  $('numbered-only-label').textContent = isArcView() ? 'Only missing parts' : 'Only missing numbers';
  $('numbered-copy').textContent = isArcView() ? 'Copy missing titles' : 'Copy missing numbers';
  $('numbered-hint').textContent = isArcView() ? "Complete your multi-part stories. Print books counts paperbacks and hardcovers; an ebook omnibus won't fill a print gap." : 'Print books counts paperbacks and hardcovers. Classic Pocket Books ranges are suggested; adjust for your numbering scheme. Only saved book numbers count; comics are excluded.';
  $('numbered-list').hidden = isArcView();
  $('numbered-tiles').className = isArcView() ? 'arc-reports' : 'numbered-tiles';
  if (isArcView()) { renderArcs(); return; }
  let report;
  try { report = numberedReport(entries, ...selection()); }
  catch (error) { $('numbered-summary').append(text('p', error.message + ' Some series have no standard numbered run.', 'numbered-warning')); return; }
  $('numbered-copy').disabled = false;
  const missing = report.slots.filter(slot => slot.missing).length;
  $('numbered-summary').append(text('h2', `${missing} missing · ${report.slots.length - missing} of ${report.slots.length} numbers recorded as owned`));
  if (report.needsNumber.length) $('numbered-summary').append(text('p', `${report.needsNumber.length} owned editions have blank or invalid numbers. Review numbering before treating this as a complete shopping list.`, 'numbered-warning'));
  if (report.outsideRange.length) $('numbered-summary').append(text('p', `${report.outsideRange.length} numbered ${report.outsideRange.length === 1 ? 'copy is' : 'copies are'} outside this range.`, 'hint'));
  $('numbered-list').textContent = missingNumbers(report) || 'Every number in this range is recorded as owned.';
  for (const slot of report.slots.filter(slot => !$('numbered-only-missing').checked || slot.missing)) {
    const tile = text('div', '', 'numbered-tile ' + (slot.missing ? 'missing' : 'owned'));
    tile.append(text('strong', '#' + slot.number), text('span', slot.status));
    if (slot.otherFormats.length) tile.append(text('small', 'Owned in another format'));
    tile.title = [...new Set([...slot.owned, ...slot.wishlist, ...slot.otherFormats].map(({book}) => book.title))].join(' · ');
    $('numbered-tiles').append(tile);
  }
  $('numbered-review').hidden = !report.needsNumber.length;
  $('numbered-review-title').textContent = `Review ${report.needsNumber.length} editions with blank or invalid numbers`;
  for (const entry of report.needsNumber.toSorted((a, b) => a.book.title.localeCompare(b.book.title))) {
    const row = text('div', '', 'numbered-review-row');
    const button = bookButton(entry, 'title-link'); button.textContent = entry.book.title;
    row.append(button, text('p', `${formats[entry.book.format]} · ${entry.book.isbn || 'No ISBN'} · Saved number: ${entry.book.bookNumber || 'blank'}`, 'hint'));
    $('numbered-review-list').append(row);
  }
}

function renderArcs() {
  if (!arcReference) { $('numbered-summary').append(text('p', arcError || 'Loading story-arc reference…', 'hint')); return; }
  const reports = arcReports();
  $('numbered-copy').disabled = reports.length === 0;
  const missing = reports.reduce((count, report) => count + report.slots.filter(slot => slot.missing).length, 0);
  $('numbered-summary').append(text('h2', `${missing} missing ${missing === 1 ? 'part' : 'parts'} across ${reports.length} story ${reports.length === 1 ? 'arc' : 'arcs'}`), text('p', 'Matches use known ISBNs or exact title variants. Reference coverage currently includes seven sets; unmatched editions are shown for review.', 'hint'));
  if (!reports.length) $('numbered-summary').append(text('p', "No supported sets match these filters. Turn off 'Only sets I own in this format' or choose another series or format."));
  for (const report of reports) {
    const panel = text('article', '', 'arc-report');
    panel.append(text('h3', report.arc.name), text('p', `${report.ownedCount} of ${report.slots.length} parts owned · ${report.slots.length - report.ownedCount} missing`, 'arc-progress'));
    for (const slot of report.slots.filter(slot => !$('numbered-only-missing').checked || slot.missing)) {
      const row = text('div', '', 'arc-part');
      row.append(text('strong', `Part ${slot.part.number}: ${slot.part.title}`), text('p', slot.status + (slot.otherFormats.length ? ' · Also owned in ' + [...new Set(slot.otherFormats.map(({book}) => formats[book.format]))].join(', ') : ''), slot.missing ? 'arc-missing' : 'arc-progress'));
      panel.append(row);
    }
    if (report.ownedCount === report.slots.length) panel.append(text('p', 'Complete in this format.', 'arc-progress'));
    if (report.unmatched.length) panel.append(text('p', 'Review unmatched titles: ' + report.unmatched.map(({book}) => book.title).join(' · '), 'numbered-warning'));
    $('numbered-tiles').append(panel);
  }
}
