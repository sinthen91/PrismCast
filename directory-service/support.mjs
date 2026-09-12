import crypto from 'node:crypto';
import fs from 'node:fs';
const hash=s=>crypto.createHash('sha256').update(s).digest('hex');
const token=()=>crypto.randomBytes(32).toString('hex');
const valid=s=>typeof s==='string'&&/^[a-f0-9]{64}$/.test(s);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export function supportRoutes({get:read,put:write,del,origin,ownerId,fetcher=fetch}) {
 const get=k=>{const raw=read(k);return raw?JSON.parse(raw):null};
 const put=(k,v,ttl=0)=>write(k,JSON.stringify(v),ttl);
 const limits=new Map();
 const limited=(key,max)=>{const now=Date.now();let r=limits.get(key);if(!r||r.end<now){r={n:0,end:now+60000};limits.set(key,r)}if(limits.size>10000)for(const [k,v]of limits)if(v.end<now)limits.delete(k);return ++r.n>max};
 const json=(res,data,status=200)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(data));return true};
 const page=(res,content,status=200)=>{res.writeHead(status,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','Referrer-Policy':'no-referrer','X-Content-Type-Options':'nosniff','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self' https://github.com; base-uri 'none'"});res.end(content);return true};
 const shell=body=>`<!doctype html><meta name="viewport" content="width=device-width"><title>PrismCast Support</title><link rel="stylesheet" href="/support/style.css"><main><h1>PrismCast Support</h1>${body}</main>`;
 const cookies=req=>Object.fromEntries((req.headers.cookie||'').split(';').map(s=>s.trim().split('=')));
 const cookie=(res,name,value,age)=>res.setHeader('Set-Cookie',`${name}=${value}; Path=/support; HttpOnly; Secure; SameSite=Lax; Max-Age=${age}`);
 const redirect=(res,url)=>{res.writeHead(303,{Location:url,'Cache-Control':'no-store','Referrer-Policy':'no-referrer'});res.end();return true};
 const gh=async(url,opts={})=>{const r=await fetcher(url,{...opts,signal:AbortSignal.timeout(15000),headers:{Accept:'application/json','User-Agent':'PrismCast-Support',...opts.headers}});if(!r.ok)throw Error('GitHub request failed');return r.json()};
 const index=()=>get('support:index')||[];
 const view=t=>({...t,owner:undefined,attachment:undefined,hasAttachment:!!t.attachment});
 return async(req,res,u,body)=>{
  const p=u.pathname;if(!p.startsWith('/support'))return false;
  if(req.headers.origin&&req.headers.origin!==origin)return json(res,{error:'Forbidden origin'},403);
  if(limited(req.socket.remoteAddress||'unknown',180))return json(res,{error:'Please wait a minute before trying again.'},429);
  const config=get('support:oauth');
  if(req.method==='GET'&&['/support/style.css','/support/dashboard.js'].includes(p)){
   res.writeHead(200,{'Content-Type':p.endsWith('.css')?'text/css':'text/javascript','X-Content-Type-Options':'nosniff','Cache-Control':'no-store'});res.end(fs.readFileSync(new URL(p.endsWith('.css')?'./support.css':'./support-dashboard.js',import.meta.url)));return true;
  }
  if(req.method==='GET'&&p==='/support')return page(res,shell(config?'<p>Private developer inbox.</p><a class="button" href="/support/login">Sign in with GitHub</a>':'<p>Developer setup: register the private GitHub sign-in app. Only the configured owner can complete setup.</p><a class="button" href="/support/setup">Set up GitHub sign-in</a>'));
  if(req.method==='GET'&&p==='/support/setup'&&!config){
   if(!ownerId||!origin?.startsWith('https://'))return json(res,{error:'Owner configuration required'},503);
   const state=token();put(`support:state:${hash(state)}`,{kind:'setup'},600);cookie(res,'pc_state',state,600);
   const manifest={name:`PrismCast Support ${ownerId}`,url:origin+'/support',redirect_url:origin+'/support/setup/callback',callback_urls:[origin+'/support/callback'],public:false,default_permissions:{},default_events:[],hook_attributes:{url:origin+'/support/webhook',active:false}};
   return page(res,shell(`<p>Confirm creation on GitHub. No repository permissions are requested.</p><form method="post" action="https://github.com/settings/apps/new?state=${state}"><input type="hidden" name="manifest" value="${esc(JSON.stringify(manifest))}"><button>Create private sign-in app</button></form>`));
  }
  if(req.method==='GET'&&p==='/support/login'&&config){
   const state=token();put(`support:state:${hash(state)}`,{kind:'login'},600);cookie(res,'pc_state',state,600);
   return redirect(res,`https://github.com/login/oauth/authorize?client_id=${encodeURIComponent(config.clientId)}&redirect_uri=${encodeURIComponent(origin+'/support/callback')}&state=${state}`);
  }
  if(req.method==='GET'&&['/support/setup/callback','/support/callback'].includes(p)){
   const state=u.searchParams.get('state')||'',c=cookies(req);if(!valid(state)||c.pc_state!==state)return json(res,{error:'Invalid or expired sign-in. Start again.'},403);
   const key=`support:state:${hash(state)}`,pending=get(key);del(key);cookie(res,'pc_state','',0);
   const setup=p.includes('/setup/');if(!pending||pending.kind!==(setup?'setup':'login'))return json(res,{error:'Sign-in expired'},403);
   const code=u.searchParams.get('code');if(!code||code.length>300)return json(res,{error:'Sign-in cancelled'},400);
   if(setup){
    if(config)return redirect(res,'/support');
    const app=await gh(`https://api.github.com/app-manifests/${encodeURIComponent(code)}/conversions`,{method:'POST'});
    if(String(app.owner?.id)!==String(ownerId)||!app.client_id||!app.client_secret)return json(res,{error:'Only the configured developer may register this dashboard.'},403);
    put('support:oauth',{clientId:app.client_id,clientSecret:app.client_secret});return redirect(res,'/support/login');
   }
   if(!config)return json(res,{error:'Setup required'},503);
   const auth=await gh('https://github.com/login/oauth/access_token',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({client_id:config.clientId,client_secret:config.clientSecret,code,redirect_uri:origin+'/support/callback'})});
   if(!auth.access_token)return json(res,{error:'GitHub authorization failed'},403);
   const user=await gh('https://api.github.com/user',{headers:{Authorization:`Bearer ${auth.access_token}`}});
   if(String(user.id)!==String(ownerId))return json(res,{error:'This inbox is private to the developer.'},403);
   const session=token();put(`support:admin:${hash(session)}`,{ownerId:String(ownerId)},604800);cookie(res,'pc_admin',session,604800);return redirect(res,'/support/inbox');
  }
  const adminToken=cookies(req).pc_admin||'';const admin=valid(adminToken)&&get(`support:admin:${hash(adminToken)}`)?.ownerId===String(ownerId);
  if(p==='/support/inbox'&&req.method==='GET'){if(!admin)return redirect(res,'/support');return page(res,shell('<div id="app">Loading inbox…</div><script src="/support/dashboard.js" defer></script>'))}
  if(p.startsWith('/support/admin/')){
   if(!admin)return json(res,{error:'Authentication required'},401);
   if(req.method==='POST'&&req.headers.origin!==origin)return json(res,{error:'Forbidden origin'},403);
   if(p==='/support/admin/logout'&&req.method==='POST'){del(`support:admin:${hash(adminToken)}`);cookie(res,'pc_admin','',0);return json(res,{ok:true})}
   if(p==='/support/admin/tickets'&&req.method==='GET')return json(res,{tickets:index().map(id=>get(`support:ticket:${id}`)).filter(Boolean).map(view).sort((a,b)=>b.updated-a.updated)});
  }
  const credential=(req.headers.authorization||'').replace(/^Bearer /,'');
  const userId=valid(credential)?hash('support:'+credential):null;
  const isAdmin=p.startsWith('/support/admin/');
  if(!isAdmin&&!userId)return json(res,{error:'Support identity required'},401);
  if(!isAdmin&&req.method==='GET'&&p==='/support/tickets')return json(res,{tickets:index().map(id=>get(`support:ticket:${id}`)).filter(t=>t&&t.owner===userId).map(view).sort((a,b)=>b.updated-a.updated)});
  const imageMatch=p.match(/^\/support\/(?:admin\/)?tickets\/([a-f0-9-]{36})\/attachment$/);
  if(req.method==='GET'&&imageMatch){const t=get(`support:ticket:${imageMatch[1]}`);if(!t||(!isAdmin&&t.owner!==userId)||!t.attachment)return json(res,{error:'Not found'},404);res.writeHead(200,{'Content-Type':t.attachment.type,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Content-Security-Policy':"default-src 'none'"});res.end(Buffer.from(t.attachment.data,'base64'));return true}
  if(req.method!=='POST')return json(res,{error:'Not found'},404);
  if(limited(isAdmin?'admin':userId,12))return json(res,{error:'Please wait a minute before sending again.'},429);
  const b=await body(req);
  const requestKey=typeof b.requestId==='string'&&/^[a-f0-9-]{36}$/.test(b.requestId)?`support:request:${isAdmin?'admin':userId}:${b.requestId}`:null;
  if(requestKey){const id=get(requestKey);if(id){const t=get(`support:ticket:${id}`);if(t&&(isAdmin||t.owner===userId))return json(res,{ticket:view(t)})}}
  if(!isAdmin&&p==='/support/tickets'){
   const ids=index();if(ids.length>=5000)return json(res,{error:'Support inbox is full. Please try later.'},503);
   if(ids.map(id=>get(`support:ticket:${id}`)).filter(t=>t?.owner===userId&&t.status!=='Resolved').length>=10)return json(res,{error:'You already have 10 open requests. Reply to an existing request.'},409);
   if(typeof b.subject!=='string'||!b.subject.trim()||b.subject.length>120||!['Bug','Suggestion','Contact'].includes(b.category))return json(res,{error:'Choose a category and enter a subject (120 characters maximum).'},400);
   if(typeof b.message!=='string'||!b.message.trim()||b.message.length>8000)return json(res,{error:'Enter a message (8000 characters maximum).'},400);
   let attachment;
   if(b.attachment){const data=b.attachment.data;if(typeof data!=='string'||data.length>1400000||!/^[A-Za-z0-9+/]*={0,2}$/.test(data))return json(res,{error:'Screenshot must be a PNG or JPEG under 1 MB.'},400);const bytes=Buffer.from(data,'base64');const png=bytes.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]));const jpg=bytes[0]===255&&bytes[1]===216&&bytes[2]===255;if(bytes.length>1048576||(!png&&!jpg))return json(res,{error:'Screenshot must be a PNG or JPEG under 1 MB.'},400);attachment={type:png?'image/png':'image/jpeg',data}}
   if(ids.reduce((n,id)=>n+Buffer.byteLength(JSON.stringify(get(`support:ticket:${id}`)||{})),0)+Buffer.byteLength(JSON.stringify(b))>40000000)return json(res,{error:'Support storage is full. Please try later.'},503);
   const id=crypto.randomUUID(),now=Date.now();const t={id,attachment,owner:userId,subject:b.subject.trim(),category:b.category,status:'Open',created:now,updated:now,messages:[{role:'user',text:b.message.trim(),time:now}],diagnostics:typeof b.diagnostics==='string'?b.diagnostics.slice(0,200):''};
   put(`support:ticket:${id}`,t);put('support:index',[...ids,id]);if(requestKey)put(requestKey,id,86400);return json(res,{ticket:view(t)},201);
  }
  const match=p.match(/^\/support\/(?:admin\/)?tickets\/([a-f0-9-]{36})\/(reply|status)$/);if(!match)return json(res,{error:'Not found'},404);
  const t=get(`support:ticket:${match[1]}`);if(!t||(!isAdmin&&t.owner!==userId))return json(res,{error:'Request not found'},404);
  if(match[2]==='status'){if(!isAdmin)return json(res,{error:'Forbidden'},403);if(!['Open','In Progress','Resolved'].includes(b.status))return json(res,{error:'Invalid status'},400);t.status=b.status}
  else {if(typeof b.message!=='string'||!b.message.trim()||b.message.length>8000)return json(res,{error:'Enter a message (8000 characters maximum).'},400);if(t.messages.length>=200)return json(res,{error:'Conversation is full; create a new request.'},409);t.messages.push({role:isAdmin?'developer':'user',text:b.message.trim(),time:Date.now()});if(!isAdmin)t.status='Open'}
  t.updated=Date.now();if(index().reduce((n,id)=>n+Buffer.byteLength(JSON.stringify(id===t.id?t:get(`support:ticket:${id}`)||{})),0)>40000000)return json(res,{error:'Support storage is full. Please try later.'},503);put(`support:ticket:${t.id}`,t);if(requestKey)put(requestKey,t.id,86400);return json(res,{ticket:view(t)});
 };
}
