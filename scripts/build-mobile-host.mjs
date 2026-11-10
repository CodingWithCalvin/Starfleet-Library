import { spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, writeFile, readdir, realpath, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { build } from 'esbuild';

// Remove only verified generated outputs inside this checkout, including stale
// Uno package folders that would otherwise inflate the offline download.
const root=await realpath(process.cwd());
for(const relative of ['work/mobile-publish','dist/client']){
  const target=path.resolve(root,relative);
  let resolved;try{resolved=await realpath(target);}catch(error){if(error.code==='ENOENT')continue;throw error;}
  if(resolved.toLowerCase()!==target.toLowerCase() || !resolved.toLowerCase().startsWith((root+path.sep).toLowerCase()))throw new Error('Generated output is outside this checkout');
  for(const item of await readdir(resolved)) await rm(path.join(resolved,item),{recursive:true,force:true});
}

const result = spawnSync('dotnet',['publish','src/StarfleetLibrary.Mobile/StarfleetLibrary.Mobile.csproj','-c','Release','-f','net10.0-browserwasm','-o','work/mobile-publish'],{stdio:'inherit'});
if(result.status !== 0) process.exit(result.status ?? 1);
await mkdir('dist/client',{recursive:true});await mkdir('dist/server',{recursive:true});await mkdir('dist/.openai',{recursive:true});
await cp('work/mobile-publish/wwwroot','dist/client',{recursive:true,filter:source=>!(/\.(gz|br|map|pdb)$/.test(source) || /(?:web.config|staticwebapp.config.json)$/.test(source))});
await cp('host/worker.js','dist/server/index.js');
await cp('.openai/hosting.json','dist/.openai/hosting.json');await cp('drizzle','dist/.openai/drizzle',{recursive:true});
await writeFile('dist/server/wrangler.json',JSON.stringify({name:'starfleet-library',main:'index.js',compatibility_date:'2026-05-15',assets:{directory:'../client',binding:'ASSETS',run_worker_first:true},d1_databases:[{binding:'DB',database_name:'starfleet-library',database_id:'00000000-0000-4000-8000-000000000000'}],r2_buckets:[{binding:'BUCKET',bucket_name:'starfleet-covers'}]},null,2));
const scannerBuild=await build({entryPoints:['host/scanner.js'],bundle:true,minify:true,format:'iife',write:false,target:'safari16'});
const scannerBytes=scannerBuild.outputFiles[0].contents;
const offlineBytes=await readFile('host/mobile-offline.js');
const fingerprint=bytes=>createHash('sha256').update(bytes).digest('hex').slice(0,12);
const scannerName=`scanner-${fingerprint(scannerBytes)}.js`, offlineName=`mobile-offline-${fingerprint(offlineBytes)}.js`;
await writeFile('dist/client/'+scannerName,scannerBytes);await writeFile('dist/client/'+offlineName,offlineBytes);
let html = await readFile('dist/client/index.html','utf8');
const manifest=JSON.parse(await readFile('dist/client/manifest.webmanifest','utf8'));
const touchIcon=manifest.icons?.find(icon=>icon.sizes==='512x512')?.src;
if(touchIcon)html=html.replace('</head>',`<link rel="apple-touch-icon" href="/${touchIcon}"/></head>`);
html=html.replace('content="black-translucent"','content="default"');
html = html.replace('<head>',`<head><title>Starfleet Library</title><meta name="description" content="Your private Star Trek Collection and Wishlist."/><script src="/${offlineName}" defer></script><script src="/${scannerName}" defer></script>`);
await writeFile('dist/client/index.html',html);
const files=[];
async function walk(directory){for(const item of await readdir(directory,{withFileTypes:true})){const name=path.join(directory,item.name);if(item.isDirectory())await walk(name);else if(item.name !== 'service-worker.js')files.push('/'+path.relative('dist/client',name).replaceAll('\\','/'));}}
await walk('dist/client');
const template=await readFile('host/service-worker.js','utf8');
const version=createHash('sha256').update(await readFile('src/StarfleetLibrary.Mobile/bin/Release/net10.0-browserwasm/StarfleetLibrary.Mobile.dll')).update(template).update(JSON.stringify(files)).digest('hex').slice(0,16);
await writeFile('dist/client/service-worker.js',template.replace('__VERSION__',version).replace('__FILES__',JSON.stringify(files)));
console.log(`Prepared Uno mobile app: ${files.length} files, private D1 catalog, R2 covers.`);
