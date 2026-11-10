const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const hash = /^[a-f0-9]{64}$/;
const json = (body, status=200) => Response.json(body, {status, headers:{'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'}});
const equal = (a,b) => { if(a.length!==b.length)return false; let diff=0;for(let i=0;i<a.length;i++)diff|=a.charCodeAt(i)^b.charCodeAt(i);return diff===0; };
async function readBounded(request,limit) {
  if(Number(request.headers.get('Content-Length'))>limit)throw new RangeError();
  const reader=request.body?.getReader();if(!reader)return new Uint8Array();
  const parts=[];let size=0;
  while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>limit){await reader.cancel();throw new RangeError();}parts.push(value);}
  const result=new Uint8Array(size);let at=0;for(const part of parts){result.set(part,at);at+=part.length;}return result;
}
async function authorized(request,env) {
  const hashes=(env.DEVICE_TOKEN_HASHES??'').split(',').map(x=>x.trim()).filter(x=>hash.test(x));
  if(!hashes.length || !uuid.test(env.VAULT_ID??''))return false;
  const token=request.headers.get('Authorization')?.match(/^Bearer ([A-Za-z0-9+/]{43}=)$/)?.[1];
  if(!token || request.headers.get('X-Starfleet-Vault')!==env.VAULT_ID)return false;
  const digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(token))),x=>x.toString(16).padStart(2,'0')).join('');
  let accepted=false;for(const allowed of hashes)accepted=equal(digest,allowed)||accepted;return accepted;
}
export default {async fetch(request,env) {
  if(!await authorized(request,env))return json({error:'Unauthorized'},401);
  if(request.headers.has('Origin'))return json({error:'Native clients only'},403);
  const path=new URL(request.url).pathname;
  try {
    if(path==='/v1/entries' && request.method==='GET')return json((await env.DB.prepare('SELECT id, revision, ciphertext FROM vault_entries ORDER BY id').all()).results);
    if(path==='/v1/entries' && request.method==='POST') {
      const data=JSON.parse(new TextDecoder().decode(await readBounded(request,256*1024)));
      if(!uuid.test(data.id??'') || !Number.isSafeInteger(data.expectedRevision) || data.expectedRevision<0 || data.expectedRevision>=Number.MAX_SAFE_INTEGER || typeof data.ciphertext!=='string' || !/^[A-Za-z0-9+/]+={0,2}$/.test(data.ciphertext))return json({error:'Invalid encrypted edition'},400);
      const envelope=atob(data.ciphertext);if(envelope.length<29 || envelope.charCodeAt(0)!==1)return json({error:'Invalid encryption envelope'},400);
      const updated=data.expectedRevision===0
        ? await env.DB.prepare('INSERT INTO vault_entries(id,revision,ciphertext) VALUES(?,1,?) ON CONFLICT(id) DO NOTHING RETURNING id,revision,ciphertext').bind(data.id,data.ciphertext).first()
        : await env.DB.prepare('UPDATE vault_entries SET revision=revision+1,ciphertext=? WHERE id=? AND revision=? RETURNING id,revision,ciphertext').bind(data.ciphertext,data.id,data.expectedRevision).first();
      if(updated)return json(updated);
      return json(await env.DB.prepare('SELECT id,revision,ciphertext FROM vault_entries WHERE id=?').bind(data.id).first()??{error:'Vault was reset'},409);
    }
    const match=path.match(/^\/v1\/covers\/([a-f0-9]{64})$/);
    if(match && request.method==='PUT') {
      const bytes=await readBounded(request,5*1024*1024+29);
      if(bytes.length<29 || bytes[0]!==1)return json({error:'Invalid encryption envelope'},400);
      if(!await env.BUCKET.head(match[1]))await env.BUCKET.put(match[1],bytes,{httpMetadata:{contentType:'application/octet-stream'}});
      return new Response(null,{status:204});
    }
    if(match && request.method==='GET') {
      const object=await env.BUCKET.get(match[1]);if(!object)return json({error:'Not found'},404);
      return new Response(object.body,{headers:{'Content-Type':'application/octet-stream','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'}});
    }
    return json({error:'Not found'},404);
  } catch(error) {
    if(error instanceof RangeError)return json({error:'Request too large'},413);
    if(error instanceof SyntaxError || error.name==='InvalidCharacterError')return json({error:'Invalid request'},400);
    return json({error:'Service unavailable'},503);
  }
}};
