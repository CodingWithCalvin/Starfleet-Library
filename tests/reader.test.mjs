import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {parseConnection, vaultCrypto, fromBase64, toBase64, sealConnection, openConnection} from '../cloudflare/public/crypto.js';
import {filterBooks, catalogLabel} from '../cloudflare/public/catalog.js';
import {preparePasswordLogin, deriveLogin, unlockPasswordKey} from '../cloudflare/public/password.js';
const fixture=JSON.parse(readFileSync(new URL('./fixtures/browser-vault.json',import.meta.url),'utf8'));
const code='starfleet-v1:'+Buffer.from(JSON.stringify(fixture.settings)).toString('base64');

test('connection codes stay on their configured origin and reject malformed credentials',()=>{
 assert.deepEqual(parseConnection(code,'https://vault.test'),fixture.settings);
 assert.throws(()=>parseConnection(code,'https://another.test'),/different library address/);
 for(const value of ['bad','starfleet-v1:','starfleet-v1:'+Buffer.from('{}').toString('base64')])assert.throws(()=>parseConnection(value,'https://vault.test'));
 for(const serverUrl of ['https://vault.test/?key=x','https://vault.test/#key','https://user:pass@vault.test/','https://vault.test/private']) {
  const invalid='starfleet-v1:'+Buffer.from(JSON.stringify({...fixture.settings,serverUrl})).toString('base64');
  assert.throws(()=>parseConnection(invalid,'https://vault.test'));
 }
});
test('Web Crypto decrypts native .NET entries, tombstones and keyed covers',async()=>{
 const cipher=await vaultCrypto(fixture.settings);
 const entry=await cipher.entry(fixture.entry);
 assert.deepEqual(entry.book,fixture.book);assert.equal(entry.cover,fixture.coverHash);
 assert.equal((await cipher.entry(fixture.tombstone)).book,null);
 assert.equal(await cipher.coverId(fixture.coverHash),fixture.coverId);
 assert.equal(toBase64(await cipher.cover(fromBase64(fixture.cover),fixture.coverId,fixture.coverHash)),fixture.image);
 assert.equal(entry.book.series,8);assert.equal(entry.book.format,2);assert.match(entry.book.notes,/<script>/);
});
test('Web Crypto rejects modified ciphertext, revision, edition, vault, cover and key',async()=>{
 const cipher=await vaultCrypto(fixture.settings);
 await assert.rejects(cipher.entry({...fixture.entry,revision:8}));
 await assert.rejects(cipher.entry({...fixture.entry,id:fixture.settings.vaultId}));
 const damaged=fromBase64(fixture.entry.ciphertext);damaged[damaged.length-1]^=1;
 await assert.rejects(cipher.entry({...fixture.entry,ciphertext:toBase64(damaged)}));
 await assert.rejects(cipher.cover(fromBase64(fixture.cover),'a'.repeat(64),fixture.coverHash));
 await assert.rejects(cipher.cover(fromBase64(fixture.cover),fixture.coverId,'a'.repeat(64)));
 const wrongKey=await vaultCrypto({...fixture.settings,encryptionKey:Buffer.alloc(32,1).toString('base64')});
 await assert.rejects(wrongKey.entry(fixture.entry));
 const wrongVault=await vaultCrypto({...fixture.settings,vaultId:fixture.entry.id});
 await assert.rejects(wrongVault.entry(fixture.entry));
});
test('saved browser access is password-encrypted and bound to the hosting origin',async()=>{
 const settings={vaultId:fixture.settings.vaultId,encryptionKey:fixture.settings.encryptionKey,token:fixture.settings.deviceToken,expiresAt:9999999999};
 const password='synthetic fixture password';
 const stored=await sealConnection(settings,password,'https://vault.test');
 assert.ok(!stored.includes(settings.encryptionKey));assert.ok(!stored.includes(settings.vaultId));assert.ok(!stored.includes(password));
 assert.deepEqual(await openConnection(stored,password,'https://vault.test'),settings);
 await assert.rejects(openConnection(stored,'wrong password','https://vault.test'));
 await assert.rejects(openConnection(stored,password,'https://other.test'));
 await assert.rejects(sealConnection(settings,'short','https://vault.test'));
 const damaged=JSON.parse(stored);const bytes=fromBase64(damaged.ciphertext);bytes[0]^=1;damaged.ciphertext=toBase64(bytes);
 await assert.rejects(openConnection(JSON.stringify(damaged),password,'https://vault.test'));
});
test('catalog search and filters distinguish comics, SNW, ownership and duplicate formats',()=>{
 const entries=[
  {id:'a',book:{...fixture.book,title:'The High Country',isbn:'978-1-6680-0334-8',ownership:0,format:0}},
  {id:'b',book:{...fixture.book,title:'The High Country',ownership:0,format:2}},
  {id:'c',book:{...fixture.book,title:'The Illyrian Enigma',kind:1,ownership:0,format:2}},
  {id:'d',book:{...fixture.book,title:'A new adventure',ownership:1}},
  {id:'e',book:null},
 ];
 const filters={search:'',series:'',format:'',kind:'',reading:'',sort:'title',ownership:0};
 assert.equal(filterBooks(entries,filters).length,3);
 assert.deepEqual(filterBooks(entries,{...filters,format:'0'}).map(x=>x.id),['a']);
 assert.equal(filterBooks(entries,{...filters,kind:'0'}).length,2);
 assert.deepEqual(filterBooks(entries,{...filters,kind:'1'}).map(x=>x.id),['c']);
 assert.equal(filterBooks(entries,{...filters,search:'SNW'}).length,3);
 assert.deepEqual(filterBooks(entries,{...filters,search:'9781668003348'}).map(x=>x.id),['a']);
 assert.deepEqual(filterBooks(entries,{...filters,ownership:1}).map(x=>x.id),['d']);
 assert.equal(catalogLabel(entries[2].book),'COMIC · SNW');
 assert.equal(filterBooks(entries,{...filters,search:'missing'}).length,0);
});

test('password login separates authentication from key wrapping and binds ciphertext to origin and login',async()=>{
 const prepared=await preparePasswordLogin(fixture.settings,'synthetic private library password','https://vault.test',null);
 const settings=await unlockPasswordKey(prepared.setup.wrappedKey,prepared.keys,prepared.info,'https://vault.test');
 assert.equal(settings.encryptionKey,fixture.settings.encryptionKey);
 assert.equal(Object.hasOwn(settings,'deviceToken'),false);
 await assert.rejects(preparePasswordLogin(fixture.settings,'short','https://vault.test',null));
 const wrong=await deriveLogin('wrong synthetic password',prepared.info,'https://vault.test');
 assert.notEqual(wrong.auth,prepared.keys.auth);
 await assert.rejects(unlockPasswordKey(prepared.setup.wrappedKey,wrong,prepared.info,'https://vault.test'));
 await assert.rejects(unlockPasswordKey(prepared.setup.wrappedKey,prepared.keys,prepared.info,'https://other.test'));
 await assert.rejects(unlockPasswordKey(prepared.setup.wrappedKey,prepared.keys,{...prepared.info,id:fixture.entry.id},'https://vault.test'));
 const authAsKey=await crypto.subtle.importKey('raw',fromBase64(prepared.keys.auth),'AES-GCM',false,['decrypt']);
 await assert.rejects(unlockPasswordKey(prepared.setup.wrappedKey,{wrappingKey:authAsKey},prepared.info,'https://vault.test'));
 const damaged=fromBase64(prepared.setup.wrappedKey);damaged[damaged.length-1]^=1;
 await assert.rejects(unlockPasswordKey(toBase64(damaged),prepared.keys,prepared.info,'https://vault.test'));
});
