// PrismCast code/group directory worker.
//
// Bind a Cloudflare KV namespace named SESSIONS.
// End users never configure this service. Production PrismCast clients use one built-in endpoint.
// It supports short one-time Watch Party codes and persistent invite-only Groups.

const cors = {
  "access-control-allow-origin": "*",
  "access-control-allow-methods": "GET,POST,OPTIONS",
  "access-control-allow-headers": "content-type",
};

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json", ...cors },
  });
}

async function hostId(secret) {
  const bytes = new TextEncoder().encode(secret);
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  return [...digest.slice(0, 10)].map(x => x.toString(16).padStart(2, "0")).join("");
}

function randomCode(length = 8) {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return [...bytes].map(x => alphabet[x % alphabet.length]).join("");
}

async function getRoom(env, id) {
  const value = await env.SESSIONS.get(`room:${id}`);
  return value ? JSON.parse(value) : null;
}

async function putRoom(env, room) {
  await env.SESSIONS.put(`room:${room.roomId}`, JSON.stringify(room));
}

function roomView(room, live = null, includeInvite = false, includeMembers = false) {
  const members = includeMembers
    ? Object.entries(room.members || {}).map(([viewerId, value]) => ({
        viewerId,
        firstName: value?.firstName || "Viewer",
        isHost: !!value?.host,
      }))
    : [];
  return {
    roomId: room.roomId,
    name: room.name,
    isPrivate: !!room.isPrivate,
    hostId: room.hostId,
    inviteCode: includeInvite ? (room.inviteCode || "") : "",
    memberCount: Object.keys(room.members || {}).length,
    live: !!live,
    sessionInviteCode: live?.inviteCode || "",
    title: live?.title || "",
    hostName: live?.hostName || "",
    members,
  };
}

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: cors });

    const url = new URL(request.url);

    // Existing trusted-host rendezvous.
    if (request.method === "POST" && url.pathname === "/announce") {
      const body = await request.json();
      if (!body?.secret || !body?.inviteCode)
        return json({ error: "missing fields" }, 400);

      const id = await hostId(body.secret);
      await env.SESSIONS.put(
        id,
        JSON.stringify({ inviteCode: body.inviteCode, updated: Date.now() }),
        { expirationTtl: 180 }
      );
      return json({ ok: true, hostId: id });
    }

    if (request.method === "GET" && url.pathname.startsWith("/resolve/")) {
      const id = url.pathname.split("/").pop();
      const value = await env.SESSIONS.get(id);
      if (!value) return json({ error: "offline" }, 404);
      return new Response(value, { status: 200, headers: { "content-type": "application/json", ...cors } });
    }

    // One-time temporary session codes. The client generates a new code every hosted session.
    if (request.method === "POST" && url.pathname === "/temporary/announce") {
      const body = await request.json();
      if (!body?.secret || !body?.code || !body?.inviteCode)
        return json({ error: "missing fields" }, 400);
      const id = await hostId(body.secret);
      const code = String(body.code).toUpperCase();
      await env.SESSIONS.put(`temp:${code}`, JSON.stringify({
        inviteCode: body.inviteCode,
        hostId: id,
        hostName: body.hostName || "Host",
        title: body.title || "PrismCast session",
        updated: Date.now(),
      }), { expirationTtl: 180 });
      return json({ ok: true, code });
    }

    if (request.method === "GET" && url.pathname.startsWith("/temporary/resolve/")) {
      const code = url.pathname.split("/").pop().toUpperCase();
      const value = await env.SESSIONS.get(`temp:${code}`);
      if (!value) return json({ error: "offline" }, 404);
      return new Response(value, { status: 200, headers: { "content-type": "application/json", ...cors } });
    }

    if (request.method === "POST" && url.pathname === "/temporary/close") {
      const body = await request.json();
      if (!body?.secret || !body?.code) return json({ error: "missing fields" }, 400);
      const code = String(body.code).trim().toUpperCase();
      const raw = await env.SESSIONS.get(`temp:${code}`);
      if (!raw) return json({ ok: true });
      const current = JSON.parse(raw);
      if (current.hostId !== await hostId(body.secret)) return json({ error: "forbidden" }, 403);
      await env.SESSIONS.delete(`temp:${code}`);
      return json({ ok: true });
    }

    // Persistent invite-only groups. Internal storage retains the earlier room naming so
    // existing alpha data can migrate without throwing away memberships.
    if (request.method === "POST" && url.pathname === "/room/create") {
      const body = await request.json();
      if (!body?.secret || !body?.name || !body?.viewerId)
        return json({ error: "missing fields" }, 400);

      const ownerId = await hostId(body.secret);
      const roomId = randomCode(12);
      const inviteCode = randomCode(8);
      const room = {
        roomId,
        name: String(body.name).slice(0, 40),
        isPrivate: true,
        inviteCode,
        hostId: ownerId,
        members: {
          [body.viewerId]: { firstName: String(body.firstName || "Host").slice(0, 24), joined: Date.now(), host: true }
        },
        created: Date.now(),
      };
      await putRoom(env, room);
      await env.SESSIONS.put(`room-invite:${inviteCode}`, roomId);
      return json(roomView(room, null, true, true));
    }

    if (request.method === "POST" && url.pathname === "/room/join") {
      const body = await request.json();
      if (!body?.code || !body?.viewerId)
        return json({ error: "missing fields" }, 400);

      const supplied = String(body.code).trim().toUpperCase();
      const invitedRoomId = await env.SESSIONS.get(`room-invite:${supplied}`);
      const roomId = invitedRoomId || supplied;
      const room = await getRoom(env, roomId);
      if (!room) return json({ error: "room not found" }, 404);
      if (room.inviteCode !== supplied && !room.members?.[body.viewerId])
        return json({ error: "invite required" }, 403);

      room.members ||= {};
      room.members[body.viewerId] = {
        firstName: String(body.firstName || "Viewer").slice(0, 24),
        joined: room.members[body.viewerId]?.joined || Date.now(),
        host: !!room.members[body.viewerId]?.host,
      };
      await putRoom(env, room);
      const liveRaw = await env.SESSIONS.get(`room-live:${room.roomId}`);
      const live = liveRaw ? JSON.parse(liveRaw) : null;
      return json(roomView(room, live, false, true));
    }

    if (request.method === "GET" && url.pathname.startsWith("/room/resolve/")) {
      const roomId = url.pathname.split("/").pop().toUpperCase();
      const viewerId = url.searchParams.get("viewerId") || "";
      const room = await getRoom(env, roomId);
      if (!room) return json({ error: "room not found" }, 404);
      if (!room.members?.[viewerId])
        return json({ error: "not a member" }, 403);
      const liveRaw = await env.SESSIONS.get(`room-live:${room.roomId}`);
      const live = liveRaw ? JSON.parse(liveRaw) : null;
      return json(roomView(room, live, false, !!room.members?.[viewerId]));
    }

    if (request.method === "POST" && url.pathname === "/room/leave") {
      const body = await request.json();
      const room = await getRoom(env, String(body?.roomId || "").toUpperCase());
      if (!room || !body?.viewerId) return json({ error: "room not found" }, 404);
      if (room.hostId && room.members?.[body.viewerId]?.host)
        return json({ error: "host cannot leave own room" }, 400);
      if (room.members) delete room.members[body.viewerId];
      await putRoom(env, room);
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/room/kick") {
      const body = await request.json();
      if (!body?.secret || !body?.roomId || !body?.viewerId)
        return json({ error: "missing fields" }, 400);
      const room = await getRoom(env, String(body.roomId).toUpperCase());
      if (!room) return json({ error: "room not found" }, 404);
      if (await hostId(body.secret) !== room.hostId)
        return json({ error: "forbidden" }, 403);
      if (room.members) delete room.members[body.viewerId];
      await putRoom(env, room);
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/room/announce") {
      const body = await request.json();
      if (!body?.secret || !body?.roomId || !body?.inviteCode)
        return json({ error: "missing fields" }, 400);
      const room = await getRoom(env, String(body.roomId).toUpperCase());
      if (!room) return json({ error: "room not found" }, 404);
      if (await hostId(body.secret) !== room.hostId)
        return json({ error: "forbidden" }, 403);
      await env.SESSIONS.put(`room-live:${room.roomId}`, JSON.stringify({
        inviteCode: body.inviteCode,
        title: body.title || "PrismCast session",
        hostName: body.hostName || "Host",
        updated: Date.now(),
      }), { expirationTtl: 180 });
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/room/close-session") {
      const body = await request.json();
      if (!body?.secret || !body?.roomId) return json({ error: "missing fields" }, 400);
      const room = await getRoom(env, String(body.roomId).toUpperCase());
      if (!room) return json({ ok: true });
      if (await hostId(body.secret) !== room.hostId) return json({ error: "forbidden" }, 403);
      await env.SESSIONS.delete(`room-live:${room.roomId}`);
      return json({ ok: true });
    }

    // Nearby discovery endpoints are retained only for backward compatibility with earlier alphas.
    // for characters they can already see in their local FFXIV object table.
    if (request.method === "POST" && url.pathname === "/nearby/announce") {
      const body = await request.json();
      if (!body?.secret || !body?.characterKey || !body?.inviteCode)
        return json({ error: "missing fields" }, 400);
      await env.SESSIONS.put(`nearby:${body.characterKey}`, JSON.stringify({
        characterKey: body.characterKey,
        hostName: body.hostName || "Host",
        title: body.title || "PrismCast session",
        sessionKind: body.sessionKind || "temporary",
        inviteCode: body.inviteCode,
        roomId: body.roomId || "",
        roomName: body.roomName || "",
        updated: Date.now(),
      }), { expirationTtl: 120 });
      return json({ ok: true });
    }

    if (request.method === "GET" && url.pathname.startsWith("/nearby/resolve/")) {
      const key = url.pathname.split("/").pop();
      const value = await env.SESSIONS.get(`nearby:${key}`);
      if (!value) return json({ error: "offline" }, 404);
      return new Response(value, { status: 200, headers: { "content-type": "application/json", ...cors } });
    }

    return json({ service: "PrismCast directory", ok: true });
  },
};
