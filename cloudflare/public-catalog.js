import {json, securityHeaders} from './reader.js';

export const publicEnabled = env => env.PUBLIC_READER_ENABLED === 'true';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const hash = /^[a-f0-9]{64}$/;
const fields = ['id','title','authors','series','bookNumber','isbn','publisher','publicationYear','edition','format','kind','ownership','pageCount'];
const current = ' FROM public_editions p JOIN vault_entries v ON p.id=v.id AND p.revision=v.revision ';

function imageType(bytes) {
  if(bytes[0]===255 && bytes[1]===216 && bytes[2]===255)return 'image/jpeg';
  if(bytes[0]===137 && bytes[1]===80 && bytes[2]===78 && bytes[3]===71)return 'image/png';
  if(/^GIF8[79]a$/.test(String.fromCharCode(...bytes.slice(0,6))))return 'image/gif';
  if(String.fromCharCode(...bytes.slice(0,4))==='RIFF' && String.fromCharCode(...bytes.slice(8,12))==='WEBP')return 'image/webp';
  return null;
}

export async function readPublic(request, env) {
  if(!publicEnabled(env))return json({error:'Not found'},404);
  if(request.method!=='GET')return new Response(JSON.stringify({error:'Public access is read-only'}),{status:405,headers:{...securityHeaders,'Content-Type':'application/json',Allow:'GET'}});
  const path=new URL(request.url).pathname;
  if(path==='/public/entries') {
    const rows=(await env.DB.prepare('SELECT p.id,p.revision,p.book_json,p.cover'+current+'WHERE p.book_json IS NOT NULL ORDER BY p.id').all()).results;
    return json(rows.map(row=>({id:row.id,revision:row.revision,book:JSON.parse(row.book_json),cover:row.cover})));
  }
  const match=path.match(/^\/public\/covers\/([a-f0-9]{64})$/);
  if(!match)return json({error:'Not found'},404);
  const referenced=await env.DB.prepare('SELECT p.id'+current+'WHERE p.cover=? AND p.book_json IS NOT NULL LIMIT 1').bind(match[1]).first();
  if(!referenced)return json({error:'Not found'},404);
  const object=await env.BUCKET.get('public/covers/'+match[1]);
  if(!object)return json({error:'Not found'},404);
  return new Response(object.body,{headers:{...securityHeaders,'Content-Type':object.httpMetadata?.contentType??'application/octet-stream'}});
}

// Called only after the existing native device-token authorization check.
export async function publishPublic(request, env, readBounded) {
  const path=new URL(request.url).pathname;
  if(path==='/v1/publication' && request.method==='GET') {
    if(!publicEnabled(env))return json({enabled:false,entries:[]});
    return json({enabled:true,entries:(await env.DB.prepare('SELECT id,revision FROM public_editions ORDER BY id').all()).results});
  }
  if(!publicEnabled(env))return json({error:'Not found'},404);
  if(path==='/v1/public/entries' && request.method==='POST') {
    const data=JSON.parse(new TextDecoder().decode(await readBounded(request,32*1024)));
    if(!data || Object.keys(data).sort().join(',')!=='book,cover,id,revision' || !uuid.test(data.id??'') || !Number.isSafeInteger(data.revision) || data.revision<1 || (data.cover!==null && !hash.test(data.cover??'')))return json({error:'Invalid public edition'},400);
    if(data.book!==null) {
      const book=data.book;
      if(typeof book!=='object' || Object.keys(book).sort().join(',')!==[...fields].sort().join(',') || book.id!==data.id ||
        ['title','authors','bookNumber','isbn','publisher','publicationYear','edition'].some(key=>typeof book[key]!=='string' || book[key].length>4096) || !book.title.trim() ||
        ![book.series,book.format,book.kind,book.ownership,book.pageCount].every(Number.isInteger) || book.series<0 || book.series>8 || book.format<0 || book.format>3 || book.kind<0 || book.kind>1 || book.ownership<0 || book.ownership>1 || book.pageCount<0)return json({error:'Invalid public book fields'},400);
    } else if(data.cover!==null)return json({error:'Deleted editions cannot reference a cover'},400);
    const result=await env.DB.prepare('INSERT INTO public_editions(id,revision,book_json,cover) SELECT ?,?,?,? FROM vault_entries WHERE id=? AND revision=? ON CONFLICT(id) DO UPDATE SET revision=excluded.revision,book_json=excluded.book_json,cover=excluded.cover WHERE public_editions.revision<=excluded.revision RETURNING id')
      .bind(data.id,data.revision,data.book===null?null:JSON.stringify(data.book),data.cover,data.id,data.revision).first();
    return result?json({published:true}):json({error:'Edition changed; retry after syncing'},409);
  }
  const match=path.match(/^\/v1\/public\/covers\/([a-f0-9]{64})$/);
  if(match && request.method==='PUT') {
    const bytes=await readBounded(request,5*1024*1024);
    const type=imageType(bytes);
    const actual=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
    if(!type || actual!==match[1])return json({error:'Invalid public cover'},400);
    const key='public/covers/'+match[1];
    if(!await env.BUCKET.head(key))await env.BUCKET.put(key,bytes,{httpMetadata:{contentType:type}});
    return new Response(null,{status:204,headers:securityHeaders});
  }
  return json({error:'Not found'},404);
}
