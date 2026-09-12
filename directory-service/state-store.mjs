import fs from 'node:fs';
import path from 'node:path';

function parseState(raw) {
  const state = JSON.parse(raw);
  if (!state || typeof state !== 'object' || Array.isArray(state)
      || Object.values(state).some(x => !x || typeof x.v !== 'string' || !Number.isFinite(x.exp)))
    throw new Error('Invalid directory state');
  return state;
}

export function writeState(file, state) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file + '.tmp', JSON.stringify(state), { mode: 0o600 });
  fs.renameSync(file + '.tmp', file);
}

export function loadState(file, env = process.env) {
  try { return parseState(fs.readFileSync(file, 'utf8')); }
  catch (error) {
    if (error.code !== 'ENOENT')
      throw new Error('Cannot read directory state; refusing to replace existing data');
  }
  // Private deployment-only migration input. Never served or logged, and never
  // overwrites an existing database. Clear this variable after restoration.
  if (env.PRISMCAST_INITIAL_STATE) {
    let restored;
    try { restored = parseState(env.PRISMCAST_INITIAL_STATE); }
    catch { throw new Error('Invalid initial directory state; refusing to start'); }
    writeState(file, restored);
    return restored;
  }
  if (env.PRISMCAST_REQUIRE_STATE === '1')
    throw new Error('Required persistent directory state is missing; refusing to start empty');
  return {};
}
