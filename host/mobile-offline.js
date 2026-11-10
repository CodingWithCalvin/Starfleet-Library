window.starfleetOffline={
  warming:false, ready:false,
  async warm(paths){
    if(this.warming || !('caches' in window))return;
    this.warming=true;this.ready=false;
    try{
      await navigator.serviceWorker.ready;
      const cache=await caches.open('starfleet-covers-v1');let next=0;
      await Promise.all(Array.from({length:4},async()=>{while(next<paths.length){const path=paths[next++];if(await cache.match(path))continue;const response=await fetch(path);if(!response.ok || !response.headers.get('Content-Type')?.startsWith('image/'))throw new Error('Cover unavailable');await cache.put(path,response);}}));
      this.ready=true;
      navigator.storage?.persist?.().catch(()=>{});
    }catch{}finally{this.warming=false;}
  }
};
