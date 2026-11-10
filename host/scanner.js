import { BrowserMultiFormatOneDReader } from '@zxing/browser';

// Camera frames stay on the device. Only the decoded ISBN is used for a lookup.
window.starfleetScanner={
  result:null,active:false,
  open(){
    if(this.active)return;this.active=true;this.result=null;
    const overlay=document.createElement('div');overlay.setAttribute('role','dialog');overlay.setAttribute('aria-modal','true');overlay.setAttribute('aria-label','Scan a book barcode');
    overlay.style.cssText='position:fixed;inset:0;z-index:2147483647;background:#101d32;color:white;padding:max(24px,env(safe-area-inset-top)) 20px max(24px,env(safe-area-inset-bottom));font:16px system-ui;display:flex;flex-direction:column;gap:18px;overflow:auto';
    const title=document.createElement('h1');title.textContent='Scan a book';title.style.cssText='font-size:28px;margin:0';
    const hint=document.createElement('p');hint.textContent='Hold the ISBN or UPC barcode inside the camera view. For books, look for a barcode beginning with 978 or 979.';hint.style.cssText='line-height:1.5;margin:0;color:#c8d8e8';
    const video=document.createElement('video');video.autoplay=true;video.muted=true;video.playsInline=true;video.style.cssText='width:100%;max-height:52vh;min-height:160px;object-fit:cover;border:2px solid #71d0ce;border-radius:16px;background:#000';
    const status=document.createElement('p');status.setAttribute('role','status');status.textContent='Starting camera…';
    const actions=document.createElement('div');actions.style.cssText='display:flex;gap:12px;flex-wrap:wrap';
    const button=(text,action)=>{const b=document.createElement('button');b.textContent=text;b.onclick=action;b.style.cssText='font:600 16px system-ui;min-height:48px;padding:12px 18px;border:0;border-radius:8px;background:#147077;color:white;cursor:pointer';return b;};
    const reader=new BrowserMultiFormatOneDReader(undefined,{delayBetweenScanAttempts:200,delayBetweenScanSuccess:800});let controls;let closed=false;let pickingPhoto=false;const lastFocus=document.activeElement;
    const stop=()=>{controls?.stop();video.srcObject?.getTracks().forEach(t=>t.stop());};
    const finish=(text='')=>{if(closed)return;closed=true;stop();overlay.remove();this.active=false;this.result=text;document.removeEventListener('visibilitychange',visibility);lastFocus?.focus?.();};
    const visibility=()=>{if(document.hidden){stop();if(!pickingPhoto)status.textContent='Camera paused. Choose a photo, enter the ISBN, or reopen the scanner.';}};document.addEventListener('visibilitychange',visibility);
    const file=document.createElement('input');file.type='file';file.accept='image/*';file.setAttribute('aria-label','Choose barcode photo');file.style.display='none';
    file.oncancel=()=>{pickingPhoto=false;status.textContent='Choose a barcode photo, or enter the ISBN.';};
    file.onchange=async()=>{pickingPhoto=false;if(!file.files?.[0])return;stop();status.textContent='Reading barcode photo…';const url=URL.createObjectURL(file.files[0]);try{const result=await reader.decodeFromImageUrl(url);finish(result.getText());}catch{status.textContent='No readable barcode found. Try a clearer photo with the full barcode visible, or enter the ISBN.';}finally{URL.revokeObjectURL(url);}};
    actions.append(button('Use a photo',()=>{pickingPhoto=true;file.click();}),button('Enter ISBN',()=>finish('manual')),button('Cancel',()=>finish()));
    overlay.append(title,hint,video,status,actions,file);overlay.addEventListener('keydown',event=>{if(event.key==='Escape')finish();});
    for(const name of ['pointerdown','pointerup','mousedown','mouseup','click','touchstart','touchend'])overlay.addEventListener(name,event=>event.stopPropagation());
    document.body.append(overlay);actions.lastChild.focus();
    if(!navigator.mediaDevices?.getUserMedia){status.textContent='Camera access needs a secure browser connection. You can choose a barcode photo or enter an ISBN.';return;}
    reader.decodeFromVideoDevice(undefined,video,(result,error,control)=>{if(result){control.stop();finish(result.getText());}}).then(control=>{controls=control;if(closed)stop();else status.textContent='Looking for a barcode…';}).catch(error=>{status.textContent=error.name==='NotAllowedError' ? 'Camera access was declined. Allow the camera in Safari settings, choose a photo, or enter the ISBN.' : 'The camera could not start. Choose a barcode photo or enter the ISBN.';});
  },
  take(){const result=this.result;this.result=null;return result===null ? '__waiting__' : result;}
};
