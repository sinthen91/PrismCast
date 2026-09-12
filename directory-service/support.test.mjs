import {test} from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {supportRoutes} from './support.mjs';
import {loadState,writeState} from './state-store.mjs';
const origin='https://support.example';
test('support isolates users, secures GitHub admin, persists conversations and protects screenshots',async()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'pc-support-')),file=path.join(dir,'state.json');let db={};let githubOwner='315238797';
 const get=k=>db[k]?.exp&&db[k].exp<Date.now()?null:db[k]?.v;
 const put=(k,v,ttl=0)=>{db[k]={v,exp:ttl?Date.now()+ttl*1000:0};writeState(file,db)};const del=k=>{delete db[k];writeState(file,db)};
 const fetcher=async url=>({ok:true,json:async()=>url.includes('conversions')?{owner:{id:githubOwner},client_id:'client',client_secret:'server-secret'}:url.includes('access_token')?{access_token:'only-in-memory'}:{id:githubOwner}});
 const route=supportRoutes({get,put,del,origin,ownerId:'315238797',fetcher});
 const server=http.createServer(async(req,res)=>{try{const u=new URL(req.url,origin);if(!await route(req,res,u,async()=>{let s='';for await(const c of req)s+=c;return JSON.parse(s||'{}')})){res.writeHead(404);res.end()}}catch{res.writeHead(500);res.end()}});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
 const user=crypto.randomBytes(32).toString('hex'),other=crypto.randomBytes(32).toString('hex');
 const call=async(p,{credential=user,data,cookie,method,requestOrigin}={})=>fetch(base+p,{redirect:'manual',method:method||(data?'POST':'GET'),headers:{...(credential?{Authorization:`Bearer ${credential}`} :{}),...(cookie?{Cookie:cookie}:{}),...(requestOrigin?{Origin:requestOrigin}:{})},body:data?JSON.stringify(data):undefined});
 try{
  assert.equal((await call('/support/admin/tickets')).status,401);
  assert.equal((await call('/support/tickets',{credential:null})).status,401);
  let r=await call('/support/tickets',{data:{subject:'A <script> bug',category:'Bug',message:'Help',secret:'must not persist',attachment:{data:Buffer.from([137,80,78,71,13,10,26,10]).toString('base64')}}});assert.equal(r.status,201);const t=(await r.json()).ticket;assert.equal(t.owner,undefined);assert.equal(t.attachment,undefined);assert.equal(t.hasAttachment,true);
  assert.equal((await (await call('/support/tickets',{credential:other})).json()).tickets.length,0);
  assert.equal((await call(`/support/tickets/${t.id}/reply`,{credential:other,data:{message:'attack'}})).status,404);
  assert.equal((await call(`/support/tickets/${t.id}/attachment`,{credential:other})).status,404);
  assert.equal((await call(`/support/tickets/${t.id}/attachment`)).status,200);
  assert.equal((await call(`/support/tickets/${t.id}/status`,{data:{status:'Resolved'}})).status,403);
  assert.equal((await call('/support/tickets',{data:{subject:'x',category:'Bad',message:'x'}})).status,400);
  assert.equal((await call('/support/tickets',{data:{subject:'x',category:'Bug',message:'x',attachment:{data:Buffer.from('<svg/>').toString('base64')}}})).status,400);
  assert.equal((await call('/support/tickets',{requestOrigin:'https://evil.example'})).status,403);
  assert.equal((await call('/support/setup/callback?state=bad&code=x')).status,403);
  async function start(p){const r=await call(p);return {cookie:r.headers.get('set-cookie').split(';')[0],state:r.headers.get('set-cookie').split(';')[0].split('=')[1]}}
  let state=await start('/support/setup');githubOwner='999';assert.equal((await call(`/support/setup/callback?state=${state.state}&code=x`,{cookie:state.cookie})).status,403);assert.equal(get('support:oauth'),undefined);
  githubOwner='315238797';state=await start('/support/setup');assert.equal((await call(`/support/setup/callback?state=${state.state}&code=x`,{cookie:state.cookie})).status,303);
  assert.equal((await call(`/support/setup/callback?state=${state.state}&code=x`,{cookie:state.cookie})).status,403);
  state=await start('/support/login');githubOwner='999';assert.equal((await call(`/support/callback?state=${state.state}&code=x`,{cookie:state.cookie})).status,403);
  githubOwner='315238797';state=await start('/support/login');r=await call(`/support/callback?state=${state.state}&code=x`,{cookie:state.cookie});assert.equal(r.status,303);const admin=r.headers.get('set-cookie').split(';')[0];assert.match(r.headers.get('set-cookie'),/HttpOnly; Secure; SameSite=Lax/);
  assert.equal((await call('/support/admin/tickets',{cookie:admin})).status,200);
  assert.equal((await call(`/support/admin/tickets/${t.id}/reply`,{cookie:admin,data:{message:'Fixed'}})).status,403);
  assert.equal((await call(`/support/admin/tickets/${t.id}/reply`,{cookie:admin,requestOrigin:origin,data:{message:'Fixed'}})).status,200);
  assert.equal((await call(`/support/admin/tickets/${t.id}/status`,{cookie:admin,requestOrigin:origin,data:{status:'Resolved'}})).status,200);
  db=loadState(file);let restored=(await (await call('/support/tickets')).json()).tickets[0];assert.equal(restored.status,'Resolved');assert.equal(restored.messages.at(-1).role,'developer');assert.equal(restored.messages.at(-1).text,'Fixed');assert.equal(JSON.stringify(db).includes('must not persist'),false);assert.equal(JSON.stringify(db).includes('only-in-memory'),false);
  assert.equal((await call(`/support/tickets/${t.id}/reply`,{data:{message:'Still happens'}})).status,200);
  assert.equal((await (await call('/support/tickets')).json()).tickets[0].status,'Open');
  await call('/support/admin/logout',{cookie:admin,requestOrigin:origin,data:{}});assert.equal((await call('/support/admin/tickets',{cookie:admin})).status,401);
 }finally{await new Promise(r=>server.close(r));fs.rmSync(dir,{recursive:true,force:true})}
});
