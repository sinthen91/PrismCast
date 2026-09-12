import http from 'node:http';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const PORT=Number(process.env.PORT||3000);
const FILE=process.env.DATA_FILE||path.join(process.cwd(),'data','state.json');
const A='ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
let db={};
try{db=JSON.parse(fs.readFileSync(FILE,'utf8'))||{}}catch{}
const save=()=>{fs.mkdirSync(path.dirname(FILE),{recursive:true});fs.writeFileSync(FILE,JSON.stringify(db));};
const get=k=>{const r=db[k];if(!r)return null;if(r.exp&&r.exp<=Date.now()){delete db[k];save();return null}return r.v};
const put=(k,v,ttl=0)=>{db[k]={v,exp:ttl?Date.now()+ttl*1000:0};save()};
const del=k=>{if(k in db){delete db[k];save()}};
const hid=s=>crypto.createHash('sha256').update(String(s)).digest('hex').slice(0,20);
const code=n=>{const b=crypto.randomBytes(n);return [...b].map(x=>A[x%A.length]).join('')};
const headers={'content-type':'application/json; charset=utf-8','access-control-allow-origin':'*','access-control-allow-methods':'GET,POST,OPTIONS','access-control-allow-headers':'content-type','cache-control':'no-store'};
const send=(res,x,status=200)=>{res.writeHead(status,headers);res.end(typeof x==='string'?x:JSON.stringify(x))};
const body=async req=>{let a=[];for await(const c of req)a.push(c);return a.length?JSON.parse(Buffer.concat(a).toString('utf8')):{}};
const room=id=>{const x=get(`room:${id}`);return x?JSON.parse(x):null};
const putRoom=r=>put(`room:${r.roomId}`,JSON.stringify(r));
const view=(r,live=null,invite=false,members=false)=>({roomId:r.roomId,name:r.name,isPrivate:true,hostId:r.hostId,inviteCode:invite?r.inviteCode:'',memberCount:Object.keys(r.members||{}).length,live:!!live,sessionInviteCode:live?.inviteCode||'',title:live?.title||'',hostName:live?.hostName||'',members:members?Object.entries(r.members||{}).map(([viewerId,v])=>({viewerId,firstName:v?.firstName||'Viewer',isHost:!!v?.host})):[]});

async function app(req,res){
 if(req.method==='OPTIONS'){res.writeHead(204,headers);return res.end()}
 const u=new URL(req.url||'/',`http://${req.headers.host||'localhost'}`),p=u.pathname;
 if(req.method==='GET'&&(p==='/'||p==='/health'))return send(res,{service:'PrismCast directory',ok:true,version:1});
 if(req.method==='POST'&&p==='/temporary/announce'){
  const b=await body(req);if(!b?.secret||!b?.code||!b?.inviteCode)return send(res,{error:'missing fields'},400);
  const c=String(b.code).trim().toUpperCase();put(`temp:${c}`,JSON.stringify({inviteCode:b.inviteCode,hostId:hid(b.secret),hostName:b.hostName||'Host',title:b.title||'PrismCast session',updated:Date.now()}),180);return send(res,{ok:true,code:c});
 }
 if(req.method==='GET'&&p.startsWith('/temporary/resolve/')){const c=p.split('/').pop().toUpperCase(),x=get(`temp:${c}`);return x?send(res,x):send(res,{error:'offline'},404)}
 if(req.method==='POST'&&p==='/temporary/close'){
  const b=await body(req);if(!b?.secret||!b?.code)return send(res,{error:'missing fields'},400);const c=String(b.code).trim().toUpperCase(),x=get(`temp:${c}`);if(!x)return send(res,{ok:true});if(JSON.parse(x).hostId!==hid(b.secret))return send(res,{error:'forbidden'},403);del(`temp:${c}`);return send(res,{ok:true});
 }
 if(req.method==='POST'&&p==='/room/create'){
  const b=await body(req);if(!b?.secret||!b?.name||!b?.viewerId)return send(res,{error:'missing fields'},400);
  const r={roomId:code(12),name:String(b.name).slice(0,40),isPrivate:true,inviteCode:code(8),hostId:hid(b.secret),members:{[b.viewerId]:{firstName:String(b.firstName||'Host').slice(0,24),joined:Date.now(),host:true}},created:Date.now()};putRoom(r);put(`room-invite:${r.inviteCode}`,r.roomId);return send(res,view(r,null,true,true));
 }
 if(req.method==='POST'&&p==='/room/join'){
  const b=await body(req);if(!b?.code||!b?.viewerId)return send(res,{error:'missing fields'},400);const c=String(b.code).trim().toUpperCase(),id=get(`room-invite:${c}`)||c,r=room(id);if(!r)return send(res,{error:'room not found'},404);if(r.inviteCode!==c&&!r.members?.[b.viewerId])return send(res,{error:'invite required'},403);r.members||={};r.members[b.viewerId]={firstName:String(b.firstName||'Viewer').slice(0,24),joined:r.members[b.viewerId]?.joined||Date.now(),host:!!r.members[b.viewerId]?.host};putRoom(r);const x=get(`room-live:${r.roomId}`);return send(res,view(r,x?JSON.parse(x):null,false,true));
 }
 if(req.method==='GET'&&p.startsWith('/room/resolve/')){
  const id=p.split('/').pop().toUpperCase(),v=u.searchParams.get('viewerId')||'',r=room(id);if(!r)return send(res,{error:'room not found'},404);if(!r.members?.[v])return send(res,{error:'not a member'},403);const x=get(`room-live:${r.roomId}`);return send(res,view(r,x?JSON.parse(x):null,false,true));
 }
 if(req.method==='POST'&&p==='/room/leave'){
  const b=await body(req),r=room(String(b?.roomId||'').toUpperCase());if(!r||!b?.viewerId)return send(res,{error:'room not found'},404);if(r.members?.[b.viewerId]?.host)return send(res,{error:'host cannot leave own room'},400);delete r.members?.[b.viewerId];putRoom(r);return send(res,{ok:true});
 }
 if(req.method==='POST'&&p==='/room/delete'){
  const b=await body(req);if(!b?.secret||!b?.roomId)return send(res,{error:'missing fields'},400);const r=room(String(b.roomId).toUpperCase());if(!r)return send(res,{error:'room not found'},404);if(hid(b.secret)!==r.hostId)return send(res,{error:'forbidden'},403);del(`room-invite:${r.inviteCode}`);del(`room-live:${r.roomId}`);del(`room:${r.roomId}`);return send(res,{ok:true});
 }
 if(req.method==='POST'&&p==='/room/kick'){
  const b=await body(req);if(!b?.secret||!b?.roomId||!b?.viewerId)return send(res,{error:'missing fields'},400);const r=room(String(b.roomId).toUpperCase());if(!r)return send(res,{error:'room not found'},404);if(hid(b.secret)!==r.hostId)return send(res,{error:'forbidden'},403);delete r.members?.[b.viewerId];putRoom(r);return send(res,{ok:true});
 }
 if(req.method==='POST'&&p==='/room/announce'){
  const b=await body(req);if(!b?.secret||!b?.roomId||!b?.inviteCode)return send(res,{error:'missing fields'},400);const r=room(String(b.roomId).toUpperCase());if(!r)return send(res,{error:'room not found'},404);if(hid(b.secret)!==r.hostId)return send(res,{error:'forbidden'},403);put(`room-live:${r.roomId}`,JSON.stringify({inviteCode:b.inviteCode,title:b.title||'PrismCast session',hostName:b.hostName||'Host',updated:Date.now()}),180);return send(res,{ok:true});
 }
 if(req.method==='POST'&&p==='/room/close-session'){
  const b=await body(req);if(!b?.secret||!b?.roomId)return send(res,{error:'missing fields'},400);const r=room(String(b.roomId).toUpperCase());if(!r)return send(res,{ok:true});if(hid(b.secret)!==r.hostId)return send(res,{error:'forbidden'},403);del(`room-live:${r.roomId}`);return send(res,{ok:true});
 }
 return send(res,{error:'not found'},404);
}

setInterval(()=>{for(const k of Object.keys(db))get(k)},60000).unref();
http.createServer((req,res)=>app(req,res).catch(e=>{console.error(e);if(!res.headersSent)send(res,{error:'internal error'},500);else res.end()})).listen(PORT,'0.0.0.0',()=>console.log(`PrismCast directory listening on :${PORT}`));
