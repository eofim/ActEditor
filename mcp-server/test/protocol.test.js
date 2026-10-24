import { spawn } from 'node:child_process';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createInterface } from 'node:readline';

function session() {
  const proc=spawn(process.execPath,['src/index.js'],{cwd:new URL('../',import.meta.url).pathname,stdio:['pipe','pipe','pipe']});
  const output=createInterface({input:proc.stdout});
  const responses=new Map();
  output.on('line',line=>{const msg=JSON.parse(line);const cb=responses.get(msg.id);if(cb){responses.delete(msg.id);cb(msg)}});
  let id=0;
  function request(method,params={}) {
    return new Promise((resolve,reject)=>{
      const next=++id;
      const timeout=setTimeout(()=>{responses.delete(next);reject(new Error('request timeout'))},3000);
      responses.set(next,msg=>{clearTimeout(timeout);resolve(msg)});
      proc.stdin.write(JSON.stringify({jsonrpc:'2.0',method,params,id:next})+'\n');
    });
  }
  return {proc,request,close:()=>{proc.kill();output.close()}};
}

test('MCP initializes and lists the editor tools',async()=>{
  const s=session();
  try {
    const init=await s.request('initialize',{protocolVersion:'2025-06-18',capabilities:{},clientInfo:{name:'test',version:'0'}});
    assert.equal(init.result.serverInfo.name,'acteditor-mcp');
    const list=await s.request('tools/list');
    const names=list.result.tools.map(x=>x.name);
    for(const name of ['act_get_state','act_export_sprites','act_plan_import','act_apply_import','act_undo'])assert.ok(names.includes(name));
    assert.equal(names.length,new Set(names).size);
  }finally{s.close()}
});

test('MCP tools fail cleanly when the editor is not running',async()=>{
  const s=session();
  try{
    const response=await s.request('tools/call',{name:'act_ping',arguments:{}});
    assert.equal(response.result.isError,true);
    assert.ok(response.result.content[0].text.length>0);
  }finally{s.close()}
});

test('MCP rejects unknown tools',async()=>{
  const s=session();
  try{
    const response=await s.request('tools/call',{name:'invalid',arguments:{}});
    assert.ok(response.error);
  }finally{s.close()}
});
