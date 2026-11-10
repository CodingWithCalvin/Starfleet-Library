import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

function environment(){
  const listeners=new Map(),stores=new Map();let online=true;
  const key=input=>typeof input==='string'?input:new URL(input.url).pathname;
  const caches={async open(name){if(!stores.has(name))stores.set(name,new Map());const entries=stores.get(name);return {async put(request,response){entries.set(key(request),response.clone());},async match(request){return entries.get(key(request))?.clone();}};},async keys(){return [...stores.keys()];},async delete(name){return stores.delete(name);}};
  const self={location:{origin:'https://app.test'},addEventListener(name,callback){listeners.set(name,callback);},async skipWaiting(){},clients:{async claim(){}}};
  const fetch=async request=>{
    if(!online)throw new TypeError('Offline');const path=key(request);
    if(path==='/index.html')return {ok:true,redirected:true};
    return new Response(path.startsWith('/api/covers/')?'cover bytes':path==='/'?'Uno shell':'application code',{headers:{'Content-Type':path.startsWith('/api/covers/')?'image/jpeg':'text/html'}});
  };
  vm.runInNewContext(readFileSync(new URL('../host/service-worker.js',import.meta.url),'utf8').replace('__VERSION__','test').replace('__FILES__',JSON.stringify(['/index.html','/app.js'])),{self,caches,fetch,URL});
  return {offline(){online=false;},async install(){let waiting;listeners.get('install')({waitUntil(promise){waiting=promise;}});await waiting;},async request(path,mode='cors'){let response;listeners.get('fetch')({request:{method:'GET',url:'https://app.test'+path,mode},respondWith(promise){response=promise;}});return response;}};
}
test('offline installation handles canonical index redirects and reloads the Uno shell',async()=>{const app=environment();await app.install();app.offline();assert.equal(await (await app.request('/','navigate')).text(),'Uno shell');assert.equal(await (await app.request('/app.js')).text(),'application code');});
test('cover images survive network loss while catalog requests remain live',async()=>{const app=environment();await app.install();const path='/api/covers/'+'a'.repeat(64);await app.request(path);app.offline();assert.equal(await (await app.request(path)).text(),'cover bytes');assert.equal(await app.request('/api/entries'),undefined);});
