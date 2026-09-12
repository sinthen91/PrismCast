import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {loadState,writeState} from './state-store.mjs';

test('one-time state restoration preserves existing data and fails closed',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'prismcast-state-'));
  const file=path.join(dir,'data','state.json');
  const state={'room:test':{v:JSON.stringify({members:{owner:{host:true}}}),exp:0}};
  try {
    assert.throws(()=>loadState(file,{PRISMCAST_REQUIRE_STATE:'1'}),/missing/);
    assert.throws(()=>loadState(file,{PRISMCAST_INITIAL_STATE:'invalid'}),/Invalid initial/);
    assert.equal(fs.existsSync(file),false);
    assert.deepEqual(loadState(file,{PRISMCAST_INITIAL_STATE:JSON.stringify(state)}),state);
    assert.deepEqual(loadState(file,{PRISMCAST_INITIAL_STATE:'{}'}),state);
    assert.deepEqual(loadState(file,{PRISMCAST_REQUIRE_STATE:'1'}),state);
    const newer={...state,'room:second':{v:'{}',exp:0}};
    writeState(file,newer);
    assert.deepEqual(loadState(file,{PRISMCAST_INITIAL_STATE:JSON.stringify(state)}),newer);
    fs.writeFileSync(file,'broken');
    assert.throws(()=>loadState(file,{PRISMCAST_INITIAL_STATE:JSON.stringify(state)}),/refusing to replace/);
    assert.equal(fs.readFileSync(file,'utf8'),'broken');
  } finally {fs.rmSync(dir,{recursive:true,force:true});}
});
