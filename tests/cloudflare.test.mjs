import test from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import worker from '../cloudflare/worker.js';
const token=Buffer.alloc(32,7).toString('base64');
const vault='a1d3787e-8d62-42c5-896f-75df6617911a';
const id='7e3fce9e-e1e3-4bdf-a7f5-5bf74d0d63cd';
const bytes=Buffer.concat([Buffer.from([1]),Buffer.alloc(40,8)]);
async function fixture(){
 const db=new DatabaseSync(':memory:');
 for(const name of ['0001_encrypted_vault.sql','0002_reader_sessions.sql'])db.exec(readFileSync(new URL('../cloudflare/migrations/'+name,import.meta.url),'utf8'));
 const blobs=new Map();
 return {VAULT_ID:vault,DEVICE_TOKEN_HASHES:Buffer.from(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(token))).toString('hex'),
 DB:{prepare(sql){let values=[];return {bind(...args){values=args;return this;},async first(){return db.prepare(sql).get(...values)??null;},async run(){return db.prepare(sql).run(...values);},async all(){return {results:db.prepare(sql).all(...values)};}};}},
 BUCKET:{async head(k){return blobs.get(k);},async get(k){return blobs.get(k);},async put(k,body,metadata){blobs.set(k,{body,...metadata});}}};
}
const request=(path,method='GET',body,headers={})=>new Request('https://vault.test'+path,{method,body,headers:{Authorization:'Bearer '+token,'X-Starfleet-Vault':vault,...headers}});
test('all catalog routes fail closed without valid credentials and vault ID',async()=>{
 const env=await fixture();
 for(const path of ['/v1/entries','/v1/covers/'+'a'.repeat(64),'/reader/v1/entries','/reader/v1/covers/'+'a'.repeat(64)])assert.equal((await worker.fetch(new Request('https://vault.test'+path),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries','GET',undefined,{'X-Starfleet-Vault':id}),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries'),{...env,DEVICE_TOKEN_HASHES:''})).status,401);
 assert.equal((await worker.fetch(request('/v1/entries','GET',undefined,{Origin:'https://other.test'}),env)).status,403);
});

const pair=async env=>{
 const response=await worker.fetch(request('/reader/session','POST',undefined,{Origin:'https://vault.test','Sec-Fetch-Site':'same-origin'}),env);
 assert.equal(response.status,200);return response.json();
};
const browserRequest=(session,path,method='GET',body,extra={})=>request(path,method,body,{Authorization:'Bearer '+session.token,Origin:'https://vault.test','Sec-Fetch-Site':'same-origin',...extra});

test('public shell contains no catalog and sends restrictive browser headers',async()=>{
 const env=await fixture();env.ASSETS={async fetch(){return new Response('<title>Starfleet Library</title>',{headers:{'Content-Type':'text/html'}});}};
 const response=await worker.fetch(new Request('https://vault.test/'),{...env,DEVICE_TOKEN_HASHES:''});
 assert.equal(response.status,200);assert.match(await response.text(),/Starfleet Library/);
 assert.equal(response.headers.get('Cache-Control'),'no-store');
 assert.match(response.headers.get('Content-Security-Policy'),/connect-src 'self'/);
 assert.match(response.headers.get('Content-Security-Policy'),/frame-ancestors 'none'/);
 assert.equal(response.headers.get('Access-Control-Allow-Origin'),null);
 assert.equal((await worker.fetch(new Request('https://vault.test/private-sync-access.bin'),env)).status,401);
 assert.equal((await worker.fetch(new Request('https://vault.test/',{method:'POST'}),env)).status,405);
});

test('browser pairing requires a valid native token, configured vault and exact origin',async()=>{
 const env=await fixture();
 for(const origin of [undefined,'null','https://other.test']) {
  const headers=origin?{Origin:origin}:{};
  assert.equal((await worker.fetch(request('/reader/session','POST',undefined,headers),env)).status,403);
 }
 assert.equal((await worker.fetch(request('/reader/session','POST',undefined,{Origin:'https://vault.test',Authorization:'Bearer invalid'}),env)).status,401);
 assert.equal((await worker.fetch(request('/reader/session','POST',undefined,{Origin:'https://vault.test'}),{...env,VAULT_ID:''})).status,401);
 const session=await pair(env);
 assert.match(session.token,/^[A-Za-z0-9+/]{43}=$/);assert.notEqual(session.token,token);
 assert.ok(session.expiresAt>Date.now()/1000+29*86400);
 const rows=(await env.DB.prepare('SELECT * FROM reader_sessions').all()).results;
 assert.equal(rows.length,1);assert.match(rows[0].token_hash,/^[a-f0-9]{64}$/);
 assert.ok(!JSON.stringify(rows).includes(session.token));assert.ok(!JSON.stringify(rows).includes(token));
});

test('read-only credentials read encrypted editions and covers but cannot write or mint credentials',async()=>{
 const env=await fixture();const session=await pair(env);
 await worker.fetch(request('/v1/entries','POST',JSON.stringify({id,expectedRevision:0,ciphertext:bytes.toString('base64')})),env);
 const cover='/v1/covers/'+'a'.repeat(64);await worker.fetch(request(cover,'PUT',bytes),env);
 const rows=await worker.fetch(browserRequest(session,'/reader/v1/entries'),env);
 assert.equal(rows.status,200);assert.deepEqual(Object.keys((await rows.json())[0]).sort(),['ciphertext','id','revision']);
 const image=await worker.fetch(browserRequest(session,'/reader'+cover),env);assert.equal(image.status,200);assert.deepEqual(Buffer.from(await image.arrayBuffer()),bytes);
 for(const [path,method] of [['/reader/v1/entries','POST'],['/reader'+cover,'PUT'],['/reader/v1/entries','DELETE']]) {
  assert.equal((await worker.fetch(browserRequest(session,path,method,'{}'),env)).status,405);
 }
 // A browser token cannot authenticate native writes, even if Origin is omitted.
 for(const [path,method] of [['/v1/entries','POST'],[cover,'PUT']]) {
  assert.equal((await worker.fetch(request(path,method,'{}',{Authorization:'Bearer '+session.token}),env)).status,401);
 }
 assert.equal((await worker.fetch(browserRequest(session,'/reader/session','POST'),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries','POST','{}',{Origin:'https://vault.test'}),env)).status,403);
 assert.equal((await worker.fetch(request('/v1/entries','POST','{}',{'Sec-Fetch-Site':'same-origin'}),env)).status,403);
 assert.equal((await worker.fetch(browserRequest(session,'/reader/v1/entries','GET',undefined,{Origin:'https://other.test'}),env)).status,403);
 assert.equal((await worker.fetch(browserRequest(session,'/reader/v1/entries','GET',undefined,{'Sec-Fetch-Site':'same-site'}),env)).status,403);
});

test('reader sessions expire, can be revoked and follow native token revocation and vault changes',async()=>{
 const env=await fixture();const session=await pair(env);
 assert.equal((await worker.fetch(browserRequest(session,'/reader/v1/entries'),{...env,DEVICE_TOKEN_HASHES:'b'.repeat(64)})).status,401);
 assert.equal((await worker.fetch(browserRequest(session,'/reader/v1/entries','GET',undefined,{'X-Starfleet-Vault':id}),{...env,VAULT_ID:id})).status,401);
 await env.DB.prepare('UPDATE reader_sessions SET expires_at=0').run();
 assert.equal((await worker.fetch(browserRequest(session,'/reader/v1/entries'),env)).status,401);
 const next=await pair(env);assert.equal((await env.DB.prepare('SELECT * FROM reader_sessions').all()).results.length,1);
 assert.equal((await worker.fetch(browserRequest(next,'/reader/session/revoke','POST'),env)).status,204);
 assert.equal((await worker.fetch(browserRequest(next,'/reader/v1/entries'),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries'),env)).status,200);
});
test('opaque editions use atomic revisions and return encrypted conflicts',async()=>{
 const env=await fixture();const post=revision=>request('/v1/entries','POST',JSON.stringify({id,expectedRevision:revision,ciphertext:bytes.toString('base64')}));
 assert.equal((await worker.fetch(post(0),env)).status,200);
 assert.equal((await (await worker.fetch(post(1),env)).json()).revision,2);
 const conflict=await worker.fetch(post(1),env);assert.equal(conflict.status,409);assert.equal((await conflict.json()).revision,2);
 const rows=await (await worker.fetch(request('/v1/entries'),env)).json();assert.deepEqual(Object.keys(rows[0]).sort(),['ciphertext','id','revision']);
});
test('unbounded bodies and plaintext catalog writes are rejected',async()=>{
 const env=await fixture();
 assert.equal((await worker.fetch(request('/v1/entries','POST',JSON.stringify({id,expectedRevision:0,book:{title:'plaintext'}})),env)).status,400);
 assert.equal((await worker.fetch(request('/v1/entries','POST','x'.repeat(256*1024+1)),env)).status,413);
});
test('covers stay opaque and require authorization for reads and writes',async()=>{
 const env=await fixture();const path='/v1/covers/'+'a'.repeat(64);
 assert.equal((await worker.fetch(request(path,'PUT',bytes),env)).status,204);
 const response=await worker.fetch(request(path),env);assert.equal(response.headers.get('Content-Type'),'application/octet-stream');assert.deepEqual(Buffer.from(await response.arrayBuffer()),bytes);
 assert.equal((await worker.fetch(request(path,'PUT',Buffer.from('plain image')),env)).status,400);
 assert.equal((await worker.fetch(request(path,'GET',undefined,{Authorization:'Bearer invalid'}),env)).status,401);
});
