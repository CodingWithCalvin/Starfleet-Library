import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from '../host/worker.js';

function fixture(){
  const db=new DatabaseSync(':memory:');db.exec(readFileSync(new URL('../drizzle/0000_old_prowler.sql',import.meta.url),'utf8'));
  const storage=new Map();
  return {DB:{prepare(sql){let values=[];return {bind(...args){values=args;return this;},async first(){return db.prepare(sql).get(...values)??null;},async all(){return {results:db.prepare(sql).all(...values)};}};}},BUCKET:{async head(key){return storage.get(key);},async put(key,bytes,metadata){storage.set(key,{body:bytes,...metadata});},async get(key){return storage.get(key);}},ASSETS:{fetch:()=>new Response('Uno shell')}};
}
const book={id:'7e3fce9e-e1e3-4bdf-a7f5-5bf74d0d63cd',title:'Test edition',authors:'Author',bookNumber:'',isbn:'',publisher:'',publicationYear:'',edition:'',notes:'',series:0,format:2,kind:0,ownership:1,readingStatus:0,rating:0,pageCount:0,currentPage:0,addedAt:'2026-11-09T00:00:00Z'};
const post=(body,headers={})=>new Request('https://library.test/api/entries',{method:'POST',headers:{'X-Starfleet-Client':'1',...headers},body:JSON.stringify(body)});
test('revision checks prevent lost updates and preserve deletion tombstones',async()=>{
  const env=fixture();let res=await worker.fetch(post({id:book.id,expectedRevision:0,book,cover:null}),env);assert.equal(res.status,200);assert.equal((await res.json()).revision,1);
  res=await worker.fetch(post({id:book.id,expectedRevision:1,book:{...book,title:'Changed'},cover:null}),env);assert.equal((await res.json()).revision,2);
  res=await worker.fetch(post({id:book.id,expectedRevision:1,book:{...book,title:'Stale'},cover:null}),env);assert.equal(res.status,409);assert.equal((await res.json()).book.title,'Changed');
  res=await worker.fetch(post({id:book.id,expectedRevision:2,book:null,cover:null}),env);assert.equal((await res.json()).revision,3);
  res=await worker.fetch(new Request('https://library.test/api/entries'),env);assert.equal((await res.json())[0].book,null);
  res=await worker.fetch(post({id:book.id,expectedRevision:0,book,cover:null}),env);assert.equal(res.status,409);
});
test('malformed metadata, foreign origins and missing client headers are rejected',async()=>{
 const env=fixture();for(const change of [{title:''},{format:8},{isbn:'123'},{pageCount:-2},{ownership:9},{authors:null}])assert.equal((await worker.fetch(post({id:book.id,expectedRevision:0,book:{...book,...change}}),env)).status,400);
 assert.equal((await worker.fetch(post({}, {Origin:'https://other.test'}),env)).status,403);
 assert.equal((await worker.fetch(new Request('https://library.test/api/entries',{method:'POST',body:'{}'}),env)).status,403);
});
test('covers require image signatures and matching checksums',async()=>{
 const env=fixture();const bytes=new Uint8Array([255,216,255,224,0,1,2,3,4]);const hash=Buffer.from(await crypto.subtle.digest('SHA-256',bytes)).toString('hex');
 const put=(hash,data)=>new Request('https://library.test/api/covers/'+hash,{method:'PUT',headers:{'X-Starfleet-Client':'1'},body:data});
 assert.equal((await worker.fetch(put('0'.repeat(64),bytes),env)).status,400);
 assert.equal((await worker.fetch(put(hash,bytes),env)).status,204);
 const response=await worker.fetch(new Request('https://library.test/api/covers/'+hash),env);assert.equal(response.headers.get('Content-Type'),'image/jpeg');assert.deepEqual(new Uint8Array(await response.arrayBuffer()),bytes);
});
