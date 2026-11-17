import {parseConnection, vaultCrypto, openConnection} from './crypto.js';
import {deriveLogin, preparePasswordLogin, unlockPasswordKey} from './password.js';
import {seriesNames, formats, readingStates, catalogLabel, filterBooks} from './catalog.js';

const $ = id => document.getElementById(id);
const storageKey = 'starfleet-reader-connection-v1';
let active = null;
let entries = [];
let ownership = 0;
let layout = 'covers';
let refreshing = false;
let lastUpdated = 0;
let saved = null;
let login = null;
let gateVersion = 0;
try { saved = localStorage.getItem(storageKey); } catch { /* Session-only access still works. */ }
const coverUrls = new Map();
const coverRequests = new Map();
const coverTargets = new WeakMap();
let coverQueue = [];
let coverJobs = 0;
const observer = new IntersectionObserver(changes => {
  for (const change of changes) if (change.isIntersecting) {
    observer.unobserve(change.target);
    coverQueue.push({element: change.target, entry: coverTargets.get(change.target), context: active});
  }
  drainCovers();
}, {rootMargin: '250px'});

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}
async function showGate(message = '') {
  const version = ++gateVersion;
  $('gate').hidden = false;
  $('library').hidden = true;
  for (const id of ['pair-form', 'unlock-form', 'login-form', 'legacy-unlock', 'retry-login']) $(id).hidden = true;
  $('gate-title').textContent = 'Sign in to your library.';
  $('gate-status').textContent = message || 'Connecting to your library…';
  try {
    const response = await request('/reader/login-info', null);
    const info = await response.json();
    if (version !== gateVersion || active) return;
    login = info;
    $('login-form').hidden = !login.enabled;
    $('pair-form').hidden = !!login.enabled;
    $('legacy-unlock').hidden = !saved;
    $('cancel-setup').hidden = !login.enabled;
    $('gate-title').textContent = login.enabled ? 'Sign in to your library.' : 'Set up your library password.';
    $('gate-status').textContent = message;
  } catch (error) {
    if (version !== gateVersion || active) return;
    $('gate-status').textContent = error.message;
    $('retry-login').hidden = false;
  }
}
function authHeaders(context) {
  return context ? {Authorization: 'Bearer ' + context.token, 'X-Starfleet-Vault': context.vaultId} : {};
}
async function request(path, context, options = {}) {
  let response;
  try {
    response = await fetch(path, {cache: 'no-store', credentials: 'omit', redirect: 'error', signal: context?.controller?.signal,
      ...options, headers: {...options.headers, ...authHeaders(context)}});
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new Error('Could not reach your library. Check your connection and try again.');
  }
  if (response.status === 401) {
    if (context && context === active) lock('Your sign-in has expired or been revoked. Sign in again.');
    throw new Error(path === '/reader/login' ? 'Incorrect library password. Try again.' : 'This connection has expired or been revoked. Sign in again or check your recovery connection code.');
  }
  if (response.status === 429) throw new Error('Too many sign-in attempts. Wait a minute and try again.');
  if (response.status === 409) throw new Error('Password setup changed in another browser. Reload and try again.');
  if (!response.ok) throw new Error('Your library is temporarily unavailable. Please try again.');
  return response;
}
async function readEntries(context) {
  const response = await request('/reader/v1/entries', context);
  const wire = await response.json();
  if (!Array.isArray(wire)) throw new Error('The service returned an invalid catalog.');
  try {
    const decrypted = [];
    for (let i = 0; i < wire.length; i += 32) decrypted.push(...await Promise.all(wire.slice(i, i + 32).map(row => context.crypto.entry(row))));
    return decrypted.filter(row => row.book != null);
  } catch {
    throw new Error('Could not decrypt the catalog. Check that your connection code belongs to this library.');
  }
}
async function connect(settings) {
  const context = {token: settings.token, vaultId: settings.vaultId, controller: new AbortController(), crypto: await vaultCrypto(settings)};
  const books = await readEntries(context);
  active = context;
  entries = books;
  lastUpdated = Date.now();
  $('gate').hidden = true;
  $('library').hidden = false;
  $('gate-status').textContent = '';
  $('library-status').textContent = '';
  render();
  $('main').focus();
}
function busy(form, value) {
  for (const field of form.elements) field.disabled = value;
}
$('retry-login').addEventListener('click', () => showGate());
$('show-setup').addEventListener('click', () => {
  $('login-form').hidden = $('unlock-form').hidden = true;
  $('pair-form').hidden = false;
  $('gate-title').textContent = 'Set up or reset your password.';
  $('gate-status').textContent = '';
});
$('cancel-setup').addEventListener('click', () => showGate());
$('legacy-unlock').addEventListener('click', () => {
  $('login-form').hidden = $('pair-form').hidden = true;
  $('unlock-form').hidden = false;
  $('gate-title').textContent = 'Unlock saved browser access.';
  $('gate-status').textContent = '';
});
async function signIn(info, keys) {
  const response = await request('/reader/login', null, {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({id: info.id, auth: keys.auth})});
  const session = await response.json();
  const credential = {token: session.token, vaultId: info.vaultId};
  try {
    const settings = await unlockPasswordKey(session.wrappedKey, keys, info, location.origin);
    await connect({...settings, ...credential});
  } catch (error) {
    await request('/reader/session/revoke', credential, {method: 'POST'}).catch(() => {});
    throw error;
  }
}
$('login-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  busy(form, true);
  $('gate-status').textContent = 'Unlocking your private library…';
  try {
    // Refresh metadata so a password reset in another browser cannot leave stale derivation parameters.
    login = await (await request('/reader/login-info', null)).json();
    if (!login.enabled) throw new Error('Password sign-in needs to be set up again with your recovery connection code.');
    const keys = await deriveLogin($('library-password').value, login, location.origin);
    await signIn(login, keys);
  } catch (error) { $('gate-status').textContent = error.message; }
  finally { $('library-password').value = ''; busy(form, false); }
});
$('pair-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  let temporary;
  let configured = false;
  try {
    const native = parseConnection($('connection').value, location.origin);
    const password = $('new-password').value;
    if (password.length < 14 || password !== $('confirm-password').value) throw new Error('Use at least 14 characters and enter the same password twice.');
    busy(form, true);
    $('gate-status').textContent = 'Connecting to your private library…';
    const response = await request('/reader/session', {token: native.deviceToken, vaultId: native.vaultId}, {method: 'POST'});
    const session = await response.json();
    temporary = {token: session.token, vaultId: native.vaultId, crypto: await vaultCrypto(native)};
    // Verify the recovery key against the catalog before changing password access.
    await readEntries(temporary);
    const prepared = await preparePasswordLogin(native, password, location.origin, login?.id ?? null);
    await request('/reader/password', {token: native.deviceToken, vaultId: native.vaultId}, {
      method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(prepared.setup),
    });
    configured = true;
    native.deviceToken = native.encryptionKey = '';
    $('connection').value = '';
    login = {...prepared.info, enabled: true};
    await signIn(login, prepared.keys);
    form.reset();
  } catch (error) {
    if (configured) await showGate('Your password was saved. ' + error.message);
    else $('gate-status').textContent = error.message;
  } finally {
    $('new-password').value = $('confirm-password').value = '';
    busy(form, false);
    if (temporary) await request('/reader/session/revoke', temporary, {method: 'POST'}).catch(() => {});
  }
});
$('unlock-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  busy(form, true);
  $('gate-status').textContent = 'Unlocking your library…';
  try {
    const settings = await openConnection(saved, $('password').value, location.origin);
    if (settings.expiresAt * 1000 <= Date.now()) throw new Error('This browser connection has expired. Forget this browser and pair again.');
    await connect(settings);
  } catch (error) { $('gate-status').textContent = error.message; }
  finally { $('password').value = ''; busy(form, false); }
});
function lock(message = '') {
  active?.controller.abort();
  active = null;
  entries = [];
  observer.disconnect();
  coverQueue = [];
  for (const url of coverUrls.values()) URL.revokeObjectURL(url);
  coverUrls.clear();
  coverRequests.clear();
  $('results').replaceChildren();
  $('detail-content').replaceChildren();
  $('details').close();
  $('library-password').value = $('password').value = $('new-password').value = $('confirm-password').value = $('connection').value = '';
  $('search').value = '';
  for (const id of ['series', 'format', 'kind', 'reading']) $(id).value = '';
  history.replaceState(null, '', location.pathname);
  showGate(message);
}
$('lock').addEventListener('click', async () => {
  const context = active;
  lock();
  if (context) await request('/reader/session/revoke', {token: context.token, vaultId: context.vaultId}, {method: 'POST'}).catch(() => {});
});
async function forget() {
  let message = 'This browser has been disconnected. Paste your connection code to pair again.';
  if (active) {
    try { await request('/reader/session/revoke', active, {method: 'POST'}); }
    catch { message = 'Saved access was removed from this browser. The server could not be reached to revoke its token; it will expire automatically.'; }
  }
  try { localStorage.removeItem(storageKey); } catch { /* Storage may be disabled. */ }
  saved = null;
  lock(message);
}
$('forget').addEventListener('click', forget);
$('forget-locked').addEventListener('click', forget);
for (const [id, values] of [['series', seriesNames], ['format', formats], ['reading', readingStates]]) {
  values.forEach((name, value) => $(id).append(new Option(name, value)));
}
function filters() {
  return Object.fromEntries([['ownership', ownership], ...['search', 'series', 'format', 'kind', 'reading', 'sort'].map(id => [id, $(id).value])]);
}
function cover(entry) {
  const box = element('span', 'cover format-' + entry.book.format);
  const placeholder = element('span', 'placeholder');
  placeholder.append(element('span', 'mark', '✧'), element('strong', '', entry.book.title), element('small', '', entry.cover ? 'LOADING COVER' : 'NO COVER'));
  box.append(placeholder);
  const ribbon = element('span', 'ribbon');
  ribbon.append(element('span', '', formats[entry.book.format]?.toUpperCase() ?? 'EDITION'));
  box.append(ribbon);
  if (entry.cover) { coverTargets.set(box, entry); observer.observe(box); }
  return box;
}
function imageType(bytes) {
  if (bytes[0] === 255 && bytes[1] === 216 && bytes[2] === 255) return 'image/jpeg';
  if (bytes[0] === 137 && bytes[1] === 80 && bytes[2] === 78 && bytes[3] === 71) return 'image/png';
  if (String.fromCharCode(...bytes.slice(0, 6)).match(/^GIF8[79]a$/)) return 'image/gif';
  if (String.fromCharCode(...bytes.slice(0, 4)) === 'RIFF' && String.fromCharCode(...bytes.slice(8, 12)) === 'WEBP') return 'image/webp';
  throw new Error('Unsupported cover image.');
}
async function coverUrl(hash, context) {
  if (coverUrls.has(hash)) return coverUrls.get(hash);
  if (!coverRequests.has(hash)) {
    const pending = (async () => {
      const id = await context.crypto.coverId(hash);
      const response = await request('/reader/v1/covers/' + id, context);
      const bytes = await context.crypto.cover(new Uint8Array(await response.arrayBuffer()), id, hash);
      if (active !== context) return null;
      const url = URL.createObjectURL(new Blob([bytes], {type: imageType(bytes)}));
      coverUrls.set(hash, url);
      return url;
    })();
    coverRequests.set(hash, pending);
    // Both handlers are supplied so failed images never create unhandled rejections.
    pending.then(() => { if (active === context) coverRequests.delete(hash); }, () => { if (active === context) coverRequests.delete(hash); });
  }
  return coverRequests.get(hash);
}
function unavailable(box) {
  box.classList.add('error-cover');
  box.querySelector('.placeholder small').textContent = 'COVER UNAVAILABLE';
}
function drainCovers() {
  while (coverJobs < 4 && coverQueue.length) {
    const {element: box, entry, context} = coverQueue.shift();
    if (!box.isConnected || !context || context !== active) continue;
    coverJobs++;
    coverUrl(entry.cover, context).then(url => {
      if (!url || !box.isConnected || context !== active) return;
      const img = element('img');
      img.alt = '';
      img.decoding = 'async';
      img.addEventListener('load', () => { box.querySelector('.placeholder').hidden = true; });
      img.addEventListener('error', () => { img.remove(); unavailable(box); });
      img.src = url;
      box.append(img);
    }).catch(() => { if (box.isConnected && context === active) unavailable(box); })
      .finally(() => { coverJobs--; drainCovers(); });
  }
}
function bookButton(entry, className = '') {
  const button = element('button', 'book-button ' + className);
  button.setAttribute('aria-label', `${entry.book.title}, ${formats[entry.book.format]}, ${catalogLabel(entry.book)}`);
  button.title = `${entry.book.title} · ${formats[entry.book.format]}`;
  button.addEventListener('click', () => openDetails(entry));
  return button;
}
function render() {
  if (!active) return;
  observer.disconnect();
  coverQueue = [];
  const currentFilters = filters();
  const filterCount = ['series', 'format', 'kind', 'reading'].filter(id => currentFilters[id] !== '').length;
  $('filter-toggle').textContent = filterCount ? `Filters (${filterCount})` : 'Filters';
  const visible = filterBooks(entries, currentFilters);
  const count = entries.filter(row => row.book.ownership === ownership).length;
  const name = ownership === 0 ? 'Collection' : 'Wishlist';
  $('collection-count').textContent = entries.filter(row => row.book.ownership === 0).length;
  $('wishlist-count').textContent = entries.filter(row => row.book.ownership === 1).length;
  $('section-title').textContent = name;
  $('section-description').textContent = ownership === 0 ? 'Every edition. Every adventure. All in one place.' : 'The next chapters of your collection.';
  for (const [id, selected] of [['collection-tab', ownership === 0], ['wishlist-tab', ownership === 1]]) {
    $(id).classList.toggle('active', selected);
    if (selected) $(id).setAttribute('aria-current', 'page'); else $(id).removeAttribute('aria-current');
  }
  $('result-count').textContent = `${visible.length} of ${count} editions`;
  $('updated').textContent = 'Updated ' + new Date(lastUpdated).toLocaleTimeString([], {hour: 'numeric', minute: '2-digit'});
  const target = $('results');
  target.className = layout;
  target.replaceChildren();
  $('empty').hidden = visible.length > 0;
  target.hidden = visible.length === 0;
  $('empty-title').textContent = count ? 'No matching editions.' : `Your ${name.toLowerCase()} is ready for its next adventure.`;
  $('empty-description').textContent = count ? 'Try a different search or reset the filters.' : 'Add books in the Windows or iPhone app, then refresh here.';
  const fragment = document.createDocumentFragment();
  if (layout === 'table') {
    const table = element('table');
    const head = element('thead');
    const row = element('tr');
    for (const title of ['Title', 'Author', 'Series', 'Format', 'ISBN', 'Reading']) row.append(element('th', '', title));
    head.append(row); table.append(head);
    const body = element('tbody');
    for (const entry of visible) {
      const row = element('tr');
      const title = element('td');
      const button = bookButton(entry, 'title-link'); button.textContent = entry.book.title; title.append(button); row.append(title);
      for (const value of [entry.book.authors, catalogLabel(entry.book), formats[entry.book.format], entry.book.isbn || '—', readingStates[entry.book.readingStatus]]) row.append(element('td', '', value));
      body.append(row);
    }
    table.append(body); fragment.append(table);
  } else {
    for (const entry of visible) {
      const button = bookButton(entry, layout === 'cards' ? 'card' : '');
      button.append(cover(entry));
      if (layout === 'cards') {
        const info = element('span');
        info.append(element('span', 'book-label', catalogLabel(entry.book)), element('h3', '', entry.book.title), element('p', '', entry.book.authors),
          element('span', 'metadata', [formats[entry.book.format], entry.book.publicationYear, readingStates[entry.book.readingStatus]].filter(Boolean).join(' · ')));
        button.append(info);
      }
      fragment.append(button);
    }
  }
  target.append(fragment);
}
for (const id of ['search', 'series', 'format', 'kind', 'reading', 'sort']) $(id).addEventListener(id === 'search' ? 'input' : 'change', render);
$('filter-toggle').addEventListener('click', () => {
  const expanded = $('filters').classList.toggle('expanded');
  $('filter-toggle').setAttribute('aria-expanded', String(expanded));
});
$('clear').addEventListener('click', () => {
  for (const id of ['search', 'series', 'format', 'kind', 'reading']) $(id).value = '';
  $('sort').value = 'title'; render();
});
for (const [id, value] of [['collection-tab', 0], ['wishlist-tab', 1]]) $(id).addEventListener('click', () => { ownership = value; render(); });
for (const button of document.querySelectorAll('[data-layout]')) button.addEventListener('click', () => {
  layout = button.dataset.layout;
  for (const item of document.querySelectorAll('[data-layout]')) item.setAttribute('aria-pressed', String(item === button));
  render();
});
async function refresh() {
  if (!active || refreshing) return;
  const context = active;
  refreshing = true;
  $('refresh').disabled = true;
  $('library-status').textContent = 'Checking for updates…';
  try {
    const updated = await readEntries(context);
    if (active !== context) return;
    entries = updated;
    lastUpdated = Date.now();
    $('library-status').textContent = '';
    render();
  } catch (error) {
    if (active === context) $('library-status').textContent = error.message + ' The last loaded catalog is still displayed.';
  } finally { refreshing = false; $('refresh').disabled = false; }
}
$('refresh').addEventListener('click', refresh);
document.addEventListener('visibilitychange', () => { if (!document.hidden && ! $('details').open && Date.now() - lastUpdated > 60000) refresh(); });

function openDetails(entry, push = true) {
  const book = entry.book;
  const content = $('detail-content');
  content.replaceChildren();
  content.className = 'detail-grid';
  content.append(cover(entry));
  const info = element('div', 'detail-info');
  const heading = element('div', 'detail-heading');
  const title = element('h2', '', book.title); title.id = 'detail-title';
  heading.append(element('span', 'book-label', catalogLabel(book)), title, element('p', 'detail-authors', book.authors), element('span', 'pill', book.ownership === 0 ? 'IN YOUR COLLECTION' : 'ON YOUR WISHLIST'));
  info.append(heading);
  const facts = element('dl', 'detail-facts');
  for (const [label, value] of [['Format', formats[book.format]], ['Series', seriesNames[book.series]], ['ISBN', book.isbn], ['Book number', book.bookNumber], ['Publisher', book.publisher], ['Published', book.publicationYear], ['Edition', book.edition], ['Reading', readingStates[book.readingStatus]], ['Pages', book.pageCount ? `${book.currentPage} / ${book.pageCount}` : ''], ['Rating', book.rating ? `${book.rating} / 5` : '']]) {
    if (!value) continue;
    const group = element('div'); group.append(element('dt', '', label), element('dd', '', value)); facts.append(group);
  }
  info.append(facts); content.append(info);
  const others = entries.filter(row => row.id !== entry.id && row.book.title.trim().toLowerCase() === book.title.trim().toLowerCase());
  if (others.length) content.append(element('p', 'detail-other', 'Other editions: ' + others.map(row => `${formats[row.book.format]} (${row.book.ownership === 0 ? 'owned' : 'wishlist'})`).join(' · ')));
  if (book.notes) { const notes = element('div', 'detail-notes'); notes.append(element('h3', '', 'YOUR NOTES'), element('p', '', book.notes)); content.append(notes); }
  $('back').textContent = '← Back to ' + (ownership === 0 ? 'collection' : 'wishlist');
  if (push) history.pushState({bookId: entry.id}, '', '#book-' + entry.id);
  if (!$('details').open) $('details').showModal();
  $('back').focus();
}
function closeDetails() { if ($('details').open) history.back(); }
$('back').addEventListener('click', closeDetails);
$('details').addEventListener('cancel', event => { event.preventDefault(); closeDetails(); });
window.addEventListener('popstate', event => {
  const entry = active && entries.find(row => row.id === event.state?.bookId);
  if (entry) openDetails(entry, false); else $('details').close();
});
// A fresh page always starts locked, including restoration from the back/forward cache.
window.addEventListener('pagehide', () => lock());
history.replaceState(null, '', location.pathname);
showGate();
