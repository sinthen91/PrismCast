import crypto from 'node:crypto';

const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const fail = (message, status = 400) => { const e = new Error(message); e.status = status; throw e; };
const str = (value, max = 120) => typeof value === 'string' ? value.trim().slice(0, max) : '';
const own = (object, key) => Object.hasOwn(object || {}, key);
const safeId = value => /^[a-zA-Z0-9_-]{1,80}$/.test(value || '') && !['__proto__','constructor','prototype'].includes(value);
const finite = (n, min, max) => typeof n === 'number' && Number.isFinite(n) && n >= min && n <= max;
const token = (r, id) => crypto.createHmac('sha256', r.accessKey).update(id + ':' + r.members[id].credentialHash).digest('hex');
export function can(r, id, action) {
  const member = r.members?.[id];
  if (!member) return false;
  if (member.host) return true;
  if (action === 'invite') return true;
  return ['pause', 'seek', 'start', 'queue-add', 'queue-remove', 'queue-move', 'queue-plex'].includes(action)
    && ((member.role === 'moderator' && r.moderatorsEnabled !== false) || r.remoteViewerId === id);
}
function screen(value) {
  if (!value || !value.position || !['x','y','z'].every(k => finite(value.position[k], -100000, 100000))
    || !['yaw','pitch','roll'].every(k => finite(value[k], -100000, 100000))
    || !finite(value.scale, .1, 8) || !finite(value.aspectRatio, 1, 3)
    || !Number.isInteger(value.frameStyle) || value.frameStyle < 0 || value.frameStyle > 2)
    fail('Invalid screen setup');
  return {position:{x:value.position.x,y:value.position.y,z:value.position.z}, yaw:value.yaw,
    pitch:value.pitch,roll:value.roll,scale:value.scale,aspectRatio:value.aspectRatio,
    curved:!!value.curved,frameStyle:value.frameStyle};
}

export function groupRoutes({room, putRoom, get, put, del, code, hid}) {
  const live = r => { const x = get(`room-live:${r.roomId}`); return x ? JSON.parse(x) : null; };
  function authenticate(r, b, secret) {
    const id = str(b.viewerId, 80);
    if (!safeId(id)) fail('Invalid viewer identity');
    if (hid(secret) === r.hostId && r.members[id]?.host && (!r.secure || r.members[id].credentialHash !== hash(secret))) {
      // Only the original owner can migrate a legacy group. Other members must
      // rejoin with its permanent invite once, binding their private credential.
      if (!r.secure) { r.secure = true; r.accessKey = crypto.randomBytes(32).toString('hex'); }
      r.members[id].credentialHash = hash(secret);
      putRoom(r);
    }
    if (!r.secure) fail('Ask the owner to open this group in the updated plugin first', 409);
    if (!own(r.members, id) || r.members[id].credentialHash !== hash(secret)) fail('Membership authentication required', 403);
    return id;
  }
  const snapshot = (r,id) => {
    const session = live(r);
    const active = Object.entries(r.members).filter(([,m]) => m.seen > Date.now()-30000 && m.watching);
    return {roomId:r.roomId,name:r.name,isPrivate:true,hostId:r.hostId,secure:true,
      inviteCode:r.inviteCode,memberCount:Object.keys(r.members).length,live:!!session,
      sessionInviteCode:session?.inviteCode||'', title:session?.title||'',hostName:session?.hostName||'',
      viewerCount:session ? active.length : 0, moderatorsEnabled:r.moderatorsEnabled!==false,
      remoteViewerId:r.remoteViewerId||'',accessToken:token(r,id),
      mediaVersion:1,plexServer:r.plexServer||'',venue:r.venue||null,catalog:r.catalog||[],queue:r.queue||[],
      members:Object.entries(r.members).map(([viewerId,m])=>({viewerId,firstName:m.firstName,isHost:!!m.host,
        role:m.host?'owner':m.role||'viewer'}))};
  };
  return async function route(req, res, url, readBody, send) {
    if (!url.pathname.startsWith('/v2/room/')) return false;
    try {
      if (req.method !== 'POST') fail('POST required',405);
      const secret = req.headers.authorization?.replace(/^Bearer /, '') || '';
      if (!/^[a-fA-F0-9]{64}$/.test(secret)) fail('Private device credential required',401);
      const b = await readBody(req);
      const action = url.pathname.slice('/v2/room/'.length);
      if (action === 'create') {
        if (!safeId(b.viewerId) || !str(b.name,40)) fail('Name and viewer identity required');
        const r={roomId:code(12),name:str(b.name,40),isPrivate:true,inviteCode:code(8),hostId:hid(secret),secure:true,
          accessKey:crypto.randomBytes(32).toString('hex'),members:{[b.viewerId]:{host:true,role:'owner',
            firstName:str(b.firstName,24)||'Owner',credentialHash:hash(secret),joined:Date.now()}},
          moderatorsEnabled:true,remoteViewerId:'',catalog:[],queue:[],commands:[],banned:[],created:Date.now()};
        putRoom(r);put(`room-invite:${r.inviteCode}`,r.roomId);send(res,snapshot(r,b.viewerId));return true;
      }
      const invite = str(b.code,80).toUpperCase();
      const roomId = action === 'join' ? get(`room-invite:${invite}`)||invite : str(b.roomId,80).toUpperCase();
      const r=room(roomId);if(!r)fail('Group not found',404);
      if(action==='join') {
        if(!r.secure)fail('Ask the owner to open this group in the updated plugin first',409);
        if(!safeId(b.viewerId))fail('Invalid viewer identity');
        if((r.banned||[]).includes(hash(secret)))fail('Removed from this group',403);
        const existing=own(r.members,b.viewerId)?r.members[b.viewerId]:null;
        if(existing?.credentialHash && existing.credentialHash!==hash(secret))fail('Identity already registered',403);
        if(!existing?.credentialHash && invite!==r.inviteCode)fail('Permanent invite required',403);
        if(existing?.host && hid(secret)!==r.hostId)fail('Owner credential required',403);
        r.members[b.viewerId]={...existing,firstName:str(b.firstName,24)||'Viewer',credentialHash:hash(secret),
          role:existing?.role||'viewer',host:!!existing?.host,joined:existing?.joined||Date.now()};
        putRoom(r);send(res,snapshot(r,b.viewerId));return true;
      }
      const id=authenticate(r,b,secret);
      if(action==='resolve') {
        r.members[id].seen=Date.now();if(typeof b.watching==='boolean')r.members[id].watching=b.watching;putRoom(r);
        send(res,snapshot(r,id));return true;
      }
      if(action==='access') {
        if(!r.members[id].host)fail('Owner required',403);
        const found=Object.entries(r.members).find(([v,m])=>m.credentialHash && token(r,v)===b.accessToken);
        send(res,{allowed:!!found});return true;
      }
      if(action==='leave') {
        if(r.members[id].host)fail('Owner cannot leave own group');
        delete r.members[id];if(r.remoteViewerId===id)r.remoteViewerId='';putRoom(r);send(res,{ok:true});return true;
      }
      if(action==='command') {
        if(!can(r,id,b.type))fail('This role cannot perform that action',403);
        if(!live(r))fail('Venue is offline',409);
        if(!['pause','seek','start','queue-plex'].includes(b.type))fail('Invalid command');
        if(b.type==='pause' && typeof b.paused!=='boolean')fail('Invalid pause state');
        if(b.type==='seek' && !finite(b.seconds,0,864000))fail('Invalid position');
        if(b.type==='start' && !(r.catalog||[]).some(x=>x.id===b.catalogId))fail('Choose theater media');
        if(b.type==='start' && b.entryId && !(r.queue||[]).some(q=>q.id===b.entryId && q.catalogId===b.catalogId))fail('Queue entry no longer exists',409);
        if(b.type==='queue-plex' && (!/^[a-f0-9]{64}$/.test(b.plexServer||'') || b.plexServer!==r.plexServer || !/^[0-9]{1,20}$/.test(b.ratingKey||'')))fail('Choose media from the host Plex server');
        r.commands||=[];if(r.commands.length>=100)fail('Command queue full',429);
        r.commands.push({id:crypto.randomUUID(),viewerId:id,type:b.type,paused:b.paused===true,
          seconds:b.seconds||0,catalogId:str(b.catalogId,80),entryId:str(b.entryId,80),...(b.type==='queue-plex'?{plexServer:b.plexServer,ratingKey:b.ratingKey}:{}),created:Date.now()});
        putRoom(r);send(res,{ok:true});return true;
      }
      if(action==='poll') {
        if(!r.members[id].host)fail('Owner required',403);
        r.commands=(r.commands||[]).filter(c=>Date.now()-c.created<30000 && can(r,c.viewerId,c.type));
        const commands=r.commands;r.commands=[];putRoom(r);
        send(res,{commands,group:snapshot(r,id)});return true;
      }
      if(action==='validate-command') {
        if(!r.members[id].host)fail('Owner required',403);
        send(res,{allowed:can(r,str(b.actor,80),b.type)});return true;
      }
      if(action==='queue-add'||action==='queue-remove'||action==='queue-move') {
        if(!can(r,id,action))fail('This role cannot edit the queue',403);
        r.queue||=[];
        if(action==='queue-add') {
          if(r.queue.length>=100)fail('Queue is full');
          if(!(r.catalog||[]).some(x=>x.id===b.catalogId))fail('Choose media from the owner catalog');
          r.queue.push({id:crypto.randomUUID(),catalogId:b.catalogId});
        }else if(action==='queue-move') {
          if(![-1,1].includes(b.direction))fail('Invalid queue direction');
          const index=r.queue.findIndex(x=>x.id===b.entryId);if(index<0)fail('Queue entry no longer exists',409);
          const target=Math.max(0,Math.min(r.queue.length-1,index+b.direction));
          const [entry]=r.queue.splice(index,1);r.queue.splice(target,0,entry);
        }else {
          r.queue=r.queue.filter(x=>x.id!==b.entryId);
          r.catalog=(r.catalog||[]).filter(x=>x.saved!==false || r.queue.some(q=>q.catalogId===x.id));
        }
        putRoom(r);send(res,snapshot(r,id));return true;
      }
      if(!r.members[id].host)fail('Owner required',403);
      if(action==='media-upsert') {
        const x=b.item;
        if(!x || !safeId(x.id) || !str(x.title,160) || !['plex','local','url'].includes(x.kind) || typeof x.saved!=='boolean')fail('Invalid media');
        if(typeof b.queue!=='boolean')fail('Invalid queue flag');
        r.catalog||=[];r.queue||=[];
        if(b.queue && r.queue.length>=100)fail('Queue is full');
        const old=r.catalog.find(y=>y.id===x.id);
        const keepIds=new Set(r.queue.map(q=>q.catalogId));
        r.catalog=r.catalog.filter(y=>y.saved!==false || keepIds.has(y.id) || y.id===x.id);
        if(!old && r.catalog.length>=200)fail('Saved media is full');
        if(x.kind==='plex' && b.plexServer!=null) {
          if(!/^[a-f0-9]{64}$/.test(b.plexServer))fail('Invalid Plex server');
          r.plexServer=b.plexServer;
        }
        // Source paths, URLs and Plex credentials always remain on the owner's device.
        const item={id:x.id,title:str(x.title,160),kind:x.kind,saved:!!(x.saved || (old && old.saved!==false))};
        r.catalog=r.catalog.filter(y=>y.id!==x.id);r.catalog.push(item);
        if(b.queue)r.queue.push({id:crypto.randomUUID(),catalogId:x.id});
      }else if(action==='media-unsave') {
        const item=(r.catalog||[]).find(x=>x.id===b.catalogId);if(!item)fail('Media no longer exists',409);
        if((r.queue||[]).some(q=>q.catalogId===item.id)) item.saved=false;
        else r.catalog=r.catalog.filter(x=>x.id!==item.id);
      }else if(action==='plex-source') {
        if(!/^[a-f0-9]{64}$/.test(b.plexServer||''))fail('Invalid Plex server');
        r.plexServer=b.plexServer;
      }else if(action==='role') {
        const target=own(r.members,str(b.target,80))?r.members[str(b.target,80)]:null;
        if(!target||target.host||!['moderator','viewer'].includes(b.role))fail('Invalid role target');
        target.role=b.role;
        if(r.remoteViewerId===b.target)r.remoteViewerId='';
      }else if(action==='policy') {
        if(typeof b.moderatorsEnabled!=='boolean')fail('Invalid moderator policy');
        r.moderatorsEnabled=b.moderatorsEnabled;
      }else if(action==='remote') {
        const target=str(b.target,80);
        if(target && (!own(r.members,target)||r.members[target].host||r.members[target].role==='moderator'))fail('Select a regular viewer');
        r.remoteViewerId=target;
      }else if(action==='kick') {
        const target=own(r.members,str(b.target,80))?r.members[str(b.target,80)]:null;if(!target||target.host)fail('Cannot remove owner');
        if(target.credentialHash)(r.banned||=[]).push(target.credentialHash);
        delete r.members[b.target];if(r.remoteViewerId===b.target)r.remoteViewerId='';
      }else if(action==='venue') {
        if(!str(b.name,40)||!finite(b.defaultVolume,0,100))fail('Venue name and volume required');
        r.venue={name:str(b.name,40),screen:screen(b.screen),defaultVolume:Math.round(b.defaultVolume),hasIdlePoster:!!b.hasIdlePoster};
      }else if(action==='catalog') {
        if(!Array.isArray(b.items)||b.items.length>200)fail('Invalid catalog');
        if(b.items.some(x=>!safeId(x.id)||!str(x.title,160))||new Set(b.items.map(x=>x.id)).size!==b.items.length)fail('Invalid catalog entries');
        // Intentionally whitelist fields: no file paths, source URLs or Plex tokens.
        r.catalog=b.items.map(x=>({id:x.id,title:str(x.title,160)}));
        r.queue=(r.queue||[]).filter(q=>r.catalog.some(x=>x.id===q.catalogId));
      }else if(action==='delete') {
        del(`room-invite:${r.inviteCode}`);del(`room-live:${r.roomId}`);del(`room:${r.roomId}`);send(res,{ok:true});return true;
      }else fail('Unknown group action',404);
      putRoom(r);send(res,snapshot(r,id));
    }catch(e){send(res,{error:e.status?e.message:'Group request failed'},e.status||500)}
    return true;
  };
}
