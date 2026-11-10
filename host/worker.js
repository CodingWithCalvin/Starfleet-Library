const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const hashPattern = /^[0-9a-f]{64}$/;
const json = (body, status = 200) => Response.json(body, { status, headers: { 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff' } });
const entry = row => ({ id: row.id, revision: row.revision, book: row.book ? JSON.parse(row.book) : null, cover: row.cover });

export function validBook(book, id) {
  if (book === null) return true;
  if (!book || book.id !== id) return false;
  const strings = ['title','authors','bookNumber','isbn','publisher','publicationYear','edition','notes'];
  if (strings.some(k => typeof book[k] !== 'string' || book[k].length > 16000) || !book.title.trim()) return false;
  if (Object.entries({series:8, format:3, kind:1, ownership:1, readingStatus:2, rating:5}).some(([key,max]) => !Number.isInteger(book[key]) || book[key] < 0 || book[key] > max)) return false;
  if (![book.pageCount, book.currentPage].every(x => Number.isInteger(x) && x >= 0 && x <= 2147483647) || book.pageCount > 0 && book.currentPage > book.pageCount) return false;
  if (book.publicationYear && !/^\d{4}$/.test(book.publicationYear)) return false;
  const isbn = book.isbn.replace(/[\s-]/g,'').toUpperCase();
  if (isbn && !( /^\d{13}$/.test(isbn) && [...isbn].reduce((sum,c,i) => sum + Number(c) * (i % 2 ? 3 : 1), 0) % 10 === 0 || /^\d{9}[\dX]$/.test(isbn) && [...isbn].reduce((sum,c,i) => sum + (c === 'X' ? 10 : Number(c)) * (10-i), 0) % 11 === 0 )) return false;
  return typeof book.addedAt === 'string' && !Number.isNaN(Date.parse(book.addedAt)) && (book.finishedAt == null || !Number.isNaN(Date.parse(book.finishedAt))) && (book.coverUrl == null || typeof book.coverUrl === 'string' && book.coverUrl.length < 4096);
}

// Sites dispatch enforces owner-only access before this worker runs, including
// authenticated desktop service requests. Never publish this worker publicly.
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith('/api/')) return env.ASSETS.fetch(request);
    const origin = request.headers.get('Origin');
    if (origin && origin !== url.origin) return json({error:'Origin is not allowed.'}, 403);
    if (!['GET','HEAD'].includes(request.method) && request.headers.get('X-Starfleet-Client') !== '1') return json({error:'Client header is required.'}, 403);
    try {
      if (url.pathname === '/api/entries' && request.method === 'GET') {
        const rows = await env.DB.prepare('SELECT id, revision, book, cover FROM entries ORDER BY id').all();
        return json(rows.results.map(entry));
      }
      if (url.pathname === '/api/entries' && request.method === 'POST') {
        const text = await request.text();
        if (text.length > 48000) return json({error:'Edition is too large.'},413);
        const data = JSON.parse(text);
        if (!uuid.test(data.id) || !Number.isSafeInteger(data.expectedRevision) || data.expectedRevision < 0 || !validBook(data.book,data.id) || data.cover != null && !hashPattern.test(data.cover)) return json({error:'Invalid edition.'},400);
        if (data.cover && !await env.BUCKET.head(data.cover)) return json({error:'Upload the cover first.'},400);
        const payload = data.book === null ? null : JSON.stringify(data.book);
        // Conditional INSERT/UPDATE is a single atomic D1 statement. A stale
        // client receives 409, including the edition it needs to reconcile.
        const changed = await env.DB.prepare(`INSERT INTO entries (id,revision,book,cover)
          SELECT ?,1,?,? WHERE ? = 0
          ON CONFLICT(id) DO UPDATE SET revision=entries.revision+1,book=excluded.book,cover=excluded.cover
          WHERE entries.revision=? RETURNING id,revision,book,cover`)
          .bind(data.id,payload,data.cover ?? null,data.expectedRevision,data.expectedRevision).first();
        // Existing rows need an UPDATE when expectedRevision is nonzero.
        const updated = changed ?? (data.expectedRevision > 0 ? await env.DB.prepare('UPDATE entries SET revision=revision+1,book=?,cover=? WHERE id=? AND revision=? RETURNING id,revision,book,cover').bind(payload,data.cover ?? null,data.id,data.expectedRevision).first() : null);
        if (updated) return json(entry(updated));
        const current = await env.DB.prepare('SELECT id,revision,book,cover FROM entries WHERE id=?').bind(data.id).first();
        if (!current) return json({error:'Catalog was reset.'},409);
        return json(entry(current),409);
      }
      const match = url.pathname.match(/^\/api\/covers\/([a-f0-9]{64})$/);
      if (match && request.method === 'PUT') {
        if (Number(request.headers.get('Content-Length')) > 5 * 1024 * 1024) return json({error:'Cover is too large.'},413);
        const bytes = new Uint8Array(await request.arrayBuffer());
        if (bytes.length > 5 * 1024 * 1024 || bytes.length < 8) return json({error:'Invalid cover size.'},413);
        const type = bytes[0] === 255 && bytes[1] === 216 && bytes[2] === 255 ? 'image/jpeg' : bytes[0] === 137 && bytes[1] === 80 && bytes[2] === 78 && bytes[3] === 71 ? 'image/png' : bytes[0] === 71 && bytes[1] === 73 && bytes[2] === 70 ? 'image/gif' : null;
        if (!type) return json({error:'Unsupported image.'},400);
        const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(x=>x.toString(16).padStart(2,'0')).join('');
        if (digest !== match[1]) return json({error:'Image checksum does not match.'},400);
        if (!await env.BUCKET.head(digest)) await env.BUCKET.put(digest,bytes,{httpMetadata:{contentType:type}});
        return new Response(null,{status:204});
      }
      if (match && ['GET','HEAD'].includes(request.method)) {
        const object = await env.BUCKET.get(match[1]);
        if (!object) return new Response(null,{status:404});
        return new Response(request.method === 'HEAD' ? null : object.body,{headers:{'Content-Type':object.httpMetadata.contentType,'Cache-Control':'private, max-age=31536000, immutable','X-Content-Type-Options':'nosniff'}});
      }
      return json({error:'Not found.'},404);
    } catch (error) {
      if (error instanceof SyntaxError) return json({error:'Invalid JSON.'},400);
      console.error('Catalog request failed',error.name);
      return json({error:'Catalog service is temporarily unavailable.'},503);
    }
  }
};
