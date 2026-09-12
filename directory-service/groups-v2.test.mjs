import {test} from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import net from 'node:net';

test('authenticated roles, venue persistence, membership revocation and legacy isolation', async () => {
 const temp=await mkdtemp(path.join(tmpdir(),'prismcast-v2-test-'));
 const socket=net.createServer();await new Promise(r=>socket.listen(0,'127.0.0.1',r));
 const port=socket.address().port;await new Promise(r=>socket.close(r));
 const base=`http://127.0.0.1:${port}`;
 let child;
 async function start(){child=spawn(process.execPath,[fileURLToPath(new URL('./server.mjs',import.meta.url))],
  {env:{...process.env,PORT:String(port),DATA_FILE:path.join(temp,'state.json')},stdio:['ignore','pipe','pipe']});
  await new Promise((resolve,reject)=>{const t=setTimeout(()=>reject(new Error('server startup timeout')),5000);
   child.once('exit',()=>{clearTimeout(t);reject(new Error('server exited'));});
   child.stdout.once('data',()=>{clearTimeout(t);resolve();});});}
 async function stop(){if(child&&!child.killed){const done=new Promise(r=>child.once('exit',r));child.kill();await done;}}
 const owner='1'.repeat(64),mod='2'.repeat(64),viewer='3'.repeat(64),attacker='4'.repeat(64);
 async function req(action,secret,body,status=200){const res=await fetch(base+'/v2/room/'+action,{method:'POST',headers:{'content-type':'application/json',authorization:'Bearer '+secret},body:JSON.stringify(body)});
  const result=await res.json();assert.equal(res.status,status,JSON.stringify(result));return result;}
 async function legacy(route,body,status=200){const res=await fetch(base+route,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)});const result=await res.json();assert.equal(res.status,status,JSON.stringify(result));return result;}
 try {
  await start();
  const g=await req('create',owner,{viewerId:'owner',name:'Sinning Sprouts Theater',firstName:'Ryou'});
  const roomId=g.roomId,o={roomId,viewerId:'owner'},m={roomId,viewerId:'mod'},v={roomId,viewerId:'viewer'};
  await req('join',mod,{code:g.inviteCode,viewerId:'mod',firstName:'Mod'});
  const joined=await req('join',viewer,{code:g.inviteCode,viewerId:'viewer',firstName:'Viewer'});
  assert.equal(joined.inviteCode,g.inviteCode,'viewers can invite');
  await req('resolve',attacker,{...o},403);
  await req('join',attacker,{code:g.inviteCode,viewerId:'owner'},403);
  await req('join',attacker,{code:g.inviteCode,viewerId:'__proto__'},400);
  await legacy('/room/announce',{roomId,secret:owner,inviteCode:'test-session',title:'Alien',hostName:'Ryou'});
  await req('command',viewer,{...v,type:'pause',paused:true},403);
  await req('role',viewer,{...v,target:'mod',role:'moderator'},403);
  await req('role',owner,{...o,target:'owner',role:'viewer'},400);
  await req('role',owner,{...o,target:'mod',role:'moderator'});
  await req('command',mod,{...m,type:'pause',paused:true});
  await req('command',mod,{...m,type:'seek',seconds:-1},400);
  await req('remote',mod,{...m,target:'viewer'},403);
  await req('policy',owner,{...o,moderatorsEnabled:false});
  assert.equal((await req('poll',owner,o)).commands.length,0,'revoked pending command filtered');
  await req('command',mod,{...m,type:'seek',seconds:30},403);
  await req('policy',owner,{...o,moderatorsEnabled:true});
  await req('remote',owner,{...o,target:'viewer'});
  await req('command',viewer,{...v,type:'pause',paused:false});
  assert.equal((await req('poll',owner,o)).commands[0].viewerId,'viewer');
  await req('remote',owner,{...o,target:''});
  assert.equal((await req('validate-command',owner,{...o,actor:'viewer',type:'pause'})).allowed,false);
  await req('command',viewer,{...v,type:'pause',paused:true},403);
  const screen={position:{x:1,y:2,z:3},yaw:0,pitch:0,roll:0,scale:2.5,aspectRatio:16/9,frameStyle:2,curved:true};
  await req('venue',owner,{...o,name:'Sinning Sprouts Theater',screen,defaultVolume:65,hasIdlePoster:true});
  await req('venue',mod,{...m,name:'Hijack',screen,defaultVolume:50},403);
  await req('venue',owner,{...o,name:'Invalid',screen:{...screen,scale:99},defaultVolume:50},400);
  const catalog=await req('catalog',owner,{...o,items:[{id:'alien',title:'Alien',source:'C:/private.mkv',plexToken:'do-not-publish'}]});
  assert.deepEqual(catalog.catalog,[{id:'alien',title:'Alien'}]);
  await req('queue-add',viewer,{...v,catalogId:'alien'},403);
  await req('queue-add',mod,{...m,catalogId:'alien'});
  await req('command',mod,{...m,type:'start',catalogId:'unapproved-url'},400);
  await req('command',mod,{...m,type:'start',catalogId:'alien'});
  await req('resolve',viewer,{...v,watching:true});
  assert.equal((await req('resolve',owner,o)).viewerCount,1);
  assert.equal((await req('access',owner,{...o,accessToken:joined.accessToken})).allowed,true);
  await req('kick',owner,{...o,target:'owner'},400);
  await req('kick',owner,{...o,target:'viewer'});
  assert.equal((await req('access',owner,{...o,accessToken:joined.accessToken})).allowed,false);
  await req('resolve',viewer,v,403);
  await req('join',viewer,{code:g.inviteCode,viewerId:'different-id'},403);
  await legacy('/room/join',{code:g.inviteCode,viewerId:'owner'},426);
  await legacy('/room/kick',{roomId,secret:owner,viewerId:'mod'},426);
  assert.equal((await fetch(base+'/room/resolve/'+roomId+'?viewerId=owner')).status,426);
  await stop();await start();
  const restored=await req('resolve',owner,o);
  assert.equal(restored.venue.name,'Sinning Sprouts Theater');assert.deepEqual(restored.venue.screen,screen);
  assert.equal(restored.venue.defaultVolume,65);assert.equal(restored.queue.length,1);
  assert.equal(restored.members.find(x=>x.viewerId==='mod').role,'moderator');
  assert.equal((await req('poll',owner,o)).commands[0].catalogId,'alien');
  const stored=await readFile(path.join(temp,'state.json'),'utf8');
  assert.ok(!stored.includes('do-not-publish')&&!stored.includes('C:/private.mkv'));
  const legacyGroup=await legacy('/room/create',{secret:attacker,viewerId:'legacy-owner',name:'Old group'});
  await legacy('/room/join',{code:legacyGroup.inviteCode,viewerId:'legacy-viewer'});
  await req('resolve',viewer,{roomId:legacyGroup.roomId,viewerId:'legacy-viewer'},409);
  await req('resolve',attacker,{roomId:legacyGroup.roomId,viewerId:'legacy-owner'});
  await req('join',viewer,{code:legacyGroup.inviteCode,viewerId:'legacy-viewer'});
  await req('resolve',viewer,{roomId:legacyGroup.roomId,viewerId:'legacy-viewer'});
 } finally {await stop();await rm(temp,{recursive:true,force:true});}
});
