#!/usr/bin/env node
// Minimal MCP stdio server. Local Windows named pipe is the sole editor transport.
import { createInterface } from 'node:readline';
import { connect } from 'node:net';
import { randomUUID } from 'node:crypto';

const tools = [
  ['act_ping','Check connection with the running ActEditor',{}],
  ['act_get_state','Read current open ACT/SPR and selection',{}],
  ['act_list_documents','List open editor tabs',{}],
  ['act_list_sprites','List SPR images and SHA256 metadata',{document_id:{type:'string'},offset:{type:'integer'},limit:{type:'integer'}}],
  ['act_get_selection','Read selected images and layers',{document_id:{type:'string'}}],
  ['act_set_sprite_selection','Set persistent sprite selection',{document_id:{type:'string'},ids:{type:'array',items:{type:'integer'}},mode:{type:'string',enum:['replace','add','remove']}}],
  ['act_export_sprites','Export selected/all/explicit sprites with manifest',{document_id:{type:'string'},scope:{type:'string',enum:['selected','all','ids']},ids:{type:'array',items:{type:'integer'}},output_dir:{type:'string'}}],
  ['act_plan_import','Plan modified sprite reimport using exported manifest',{document_id:{type:'string'},manifest_path:{type:'string'}}],
  ['act_apply_import','Apply reviewed import plan; modifies memory only',{document_id:{type:'string'},plan_id:{type:'string'},expected_revision:{type:'integer'},confirm:{type:'boolean'}}],
  ['act_undo','Undo editor transaction',{document_id:{type:'string'},expected_revision:{type:'integer'}}],
  ['act_redo','Redo editor transaction',{document_id:{type:'string'},expected_revision:{type:'integer'}}],
  ['act_save_document','Save to existing ACT/SPR files, with explicit confirmation',{document_id:{type:'string'},expected_revision:{type:'integer'},confirm:{type:'boolean'}}]
];
const methods = Object.fromEntries(tools.map(([name])=>[name,'_'+name.slice(4)]));
const descriptors = tools.map(([name,description,properties])=>({
  name,description,inputSchema:{type:'object',properties,additionalProperties:false}
}));
function callBridge(method,params) {
  return new Promise((resolve,reject)=>{
    const socket=connect('\\\\.\\pipe\\ActEditor.MCP');
    let data='';let done=false;
    const timer=setTimeout(()=>finish(new Error('ActEditor bridge timeout')),15000);
    function finish(error,value){if(done)return;done=true;clearTimeout(timer);socket.destroy();error?reject(error):resolve(value);}
    socket.on('connect',()=>socket.write(JSON.stringify({method,params})+'\n'));
    socket.on('data',chunk=>{
      data+=chunk.toString('utf8');
      if(data.length>8*1024*1024)return finish(new Error('Oversized bridge response'));
      const i=data.indexOf('\n');
      if(i<0)return;
      try{const answer=JSON.parse(data.slice(0,i));if(!answer.ok)finish(new Error(answer.error||'Bridge failed'));else finish(null,answer.result);}
      catch(err){finish(err)}
    });
    socket.on('error',err=>finish(err));
    socket.on('end',()=>{if(!done)finish(new Error('Bridge disconnected'))});
  });
}
function respond(id,result,error) {
  if(id===undefined)return;
  process.stdout.write(JSON.stringify(error?{jsonrpc:'2.0',id,error:{code:-32603,message:String(error.message||error)}}:{jsonrpc:'2.0',id,result})+'\n');
}
const input=createInterface({input:process.stdin,crlfDelay:Infinity});
input.on('line',async line=>{
  let id;
  try {
    const request=JSON.parse(line);id=request.id;
    if(request.method==='initialize')return respond(id,{protocolVersion:'2025-06-18',capabilities:{tools:{listChanged:false}},serverInfo:{name:'acteditor-mcp',version:'0.1.0'}});
    if(request.method==='notifications/initialized')return;
    if(request.method==='ping')return respond(id,{});
    if(request.method==='tools/list')return respond(id,{tools:descriptors});
    if(request.method==='tools/call'){
      const name=request.params?.name;
      if(!Object.hasOwn(methods,name))return respond(id,null,new Error('Unknown MCP tool '+name));
      const args=request.params?.arguments||{};
      const method=name.slice(4);
      try{
        const output=await callBridge(method,args);
        return respond(id,{content:[{type:'text',text:JSON.stringify(output,null,2)}]});
      }catch(err){
        return respond(id,{content:[{type:'text',text:String(err.message||err)}],isError:true});
      }
    }
    return respond(id,null,new Error('Unknown MCP method'));
  }catch(err){respond(id,null,err)}
});
