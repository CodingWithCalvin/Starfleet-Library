const SHELL='starfleet-shell-__VERSION__';
const COVERS='starfleet-covers-v1';
const FILES=__FILES__;
self.addEventListener('install',event=>event.waitUntil((async()=>{
  const cache=await caches.open(SHELL);let cursor=0;
  await Promise.all(Array.from({length:6},async()=>{while(cursor<FILES.length){const file=FILES[cursor++];const response=await fetch(file,{cache:'reload'});if(!response.ok || response.redirected)throw new Error('Offline installation incomplete');await cache.put(file,response);}}));
  await self.skipWaiting();
})()));
self.addEventListener('activate',event=>event.waitUntil((async()=>{
  for(const key of await caches.keys())if(key.startsWith('starfleet-shell-') && key!==SHELL)await caches.delete(key);
  await self.clients.claim();
})()));
self.addEventListener('fetch',event=>{
  const url=new URL(event.request.url);
  if(event.request.method!=='GET' || url.origin!==self.location.origin)return;
  if(/^\/api\/covers\/[a-f0-9]{64}$/.test(url.pathname)){
    event.respondWith((async()=>{const cache=await caches.open(COVERS);const saved=await cache.match(event.request);if(saved)return saved;const response=await fetch(event.request);if(response.ok && response.headers.get('Content-Type')?.startsWith('image/'))await cache.put(event.request,response.clone());return response;})());return;
  }
  if(url.pathname.startsWith('/api/') || url.pathname.startsWith('/signin') || url.pathname.startsWith('/signout') || url.pathname.startsWith('/callback'))return;
  event.respondWith((async()=>{const cache=await caches.open(SHELL);if(event.request.mode==='navigate'){try{return await fetch(event.request);}catch{return await cache.match('/index.html');}}const saved=await cache.match(event.request,{ignoreSearch:true});return saved ?? fetch(event.request);})());
});
