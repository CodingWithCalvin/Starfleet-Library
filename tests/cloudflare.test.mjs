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
 const db=new DatabaseSync(':memory:');db.exec(readFileSync(new URL('../cloudflare/migrations/0001_encrypted_vault.sql',import.meta.url),'utf8'));
 const blobs=new Map();
 return {VAULT_ID:vault,DEVICE_TOKEN_HASHES:Buffer.from(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(token))).toString('hex'),
 DB:{prepare(sql){let values=[];return {bind(...args){values=args;return this;},async first(){return db.prepare(sql).get(...values)??null;},async all(){return {results:db.prepare(sql).all(...values)};}};}},
 BUCKET:{async head(k){return blobs.get(k);},async get(k){return blobs.get(k);},async put(k,body,metadata){blobs.set(k,{body,...metadata});}}};
}
const request=(path,method='GET',body,headers={})=>new Request('https://vault.test'+path,{method,body,headers:{Authorization:'Bearer '+token,'X-Starfleet-Vault':vault,...headers}});
test('all routes fail closed without valid credentials and vault ID',async()=>{
 const env=await fixture();
 for(const path of ['/','/v1/entries','/v1/covers/'+'a'.repeat(64)])assert.equal((await worker.fetch(new Request('https://vault.test'+path),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries','GET',undefined,{'X-Starfleet-Vault':id}),env)).status,401);
 assert.equal((await worker.fetch(request('/v1/entries'),{...env,DEVICE_TOKEN_HASHES:''})).status,401);
 assert.equal((await worker.fetch(request('/v1/entries','GET',undefined,{Origin:'https://other.test'}),env)).status,403);
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
