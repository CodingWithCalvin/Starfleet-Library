import { spawn } from 'node:child_process';
process.stdin.setRawMode?.(true);process.stdin.resume();let input='';
console.log('Ready for private sync credential on stdin (input is hidden).');
process.stdin.on('data',chunk=>{
 input+=chunk.toString();
 if(!input.includes('\n') && !input.includes('\r'))return;
 process.stdin.setRawMode?.(false);process.stdin.pause();
 const child=spawn('dotnet',['run','--project','tools/StarfleetLibrary.SyncSetup/StarfleetLibrary.SyncSetup.csproj','--no-build','--','connect'],{stdio:['pipe','inherit','inherit']});
 child.stdin.end(input.trim()+'\n');input='';child.on('exit',code=>process.exit(code??1));
});
