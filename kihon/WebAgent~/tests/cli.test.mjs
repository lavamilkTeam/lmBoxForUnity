import test from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { once } from 'node:events'
import { mkdir, writeFile, rm } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { connectUnityCli } from '../agent/dist/cli.js'

const packageRoot=fileURLToPath(new URL('../../',import.meta.url))
const workspace=resolve(packageRoot,'Cli~/obj/cli-test-workspace')
async function fixture(run){
 const calls=[]
 const server=createServer(async(req,res)=>{
  if(req.method==='DELETE'){calls.push({cancel:req.url});res.end('{"ok":true}');return}
  assert.equal(req.headers.authorization,'Bearer '+'a'.repeat(64))
  let body='';for await(const chunk of req)body+=chunk
  const data=JSON.parse(body);calls.push(data)
  const request=JSON.parse(data.arguments.request)
  if(request.operations[0].op==='lighting.bake')return
  res.setHeader('content-type','application/json');res.end(JSON.stringify({ok:true,result:request}))
 })
 server.listen(0,'127.0.0.1');await once(server,'listening')
 const env={...process.env,LMBOX_UNITY_BRIDGE_URL:`http://127.0.0.1:${server.address().port}/`,LMBOX_UNITY_BRIDGE_TOKEN:'a'.repeat(64),LMBOX_UNITY_PROJECT:resolve(packageRoot,'../AIAgentDemo'),LMBOX_UNITY_CLI_DLL:resolve(packageRoot,'WebAgent~/cli/lmu3d.dll')}
 await mkdir(workspace,{recursive:true})
 try{const host=await connectUnityCli(env,new AbortController().signal,workspace);await run(host,calls,env)}
 finally{server.closeAllConnections();await new Promise(r=>server.close(r));await rm(workspace,{recursive:true,force:true})}
}
test('C# CLI receives argv and AI advertises one compact tool',()=>fixture(async(host,calls)=>{
 assert.deepEqual(host.tools.map(t=>t.name),['unity_cli'])
 const reply=await host.execute('unity_cli',{args:['terrain','add','2','--pos=-1,0,3']},new AbortController().signal)
 assert.equal(reply.ok,true);assert.deepEqual(reply.result.operations,[{op:'terrain.add',count:2,position:{x:-1,y:0,z:3}}]);assert.equal(calls.length,1)
}))
test('recipe is read from task workspace; mismatched operations, path escapes and project overrides fail before HTTP',()=>fixture(async(host,calls)=>{
 await writeFile(resolve(workspace,'garden.json'),JSON.stringify({version:1,operations:[{op:'objects.place',prefab:'Assets/Cube.prefab'}]}))
 const execute=args=>host.execute('unity_cli',{args},new AbortController().signal)
 assert.equal((await execute(['objects','place','--recipe','garden.json'])).ok,true)
 for(const args of [['terrain','paint','--recipe','garden.json'],['apply','--recipe','../outside.json'],['inspect','--project','/another/project'],['inspect','--request-id','manual']])assert.equal((await execute(args)).ok,false)
 assert.equal(calls.length,1)
}))
test('unknown argv schema does not start a process; cancellation reaches Unity without replay',()=>fixture(async(host,calls)=>{
 assert.equal((await host.execute('unity_cli',{args:'inspect'},new AbortController().signal)).ok,false)
 const abort=new AbortController()
 const pending=host.execute('unity_cli',{args:['lighting','bake']},abort.signal)
 for(let i=0;i<100&&!calls.length;i++)await new Promise(r=>setTimeout(r,20))
 assert.equal(calls.length,1);abort.abort()
 assert.equal((await pending).error.code,'cancelled')
 for(let i=0;i<100&&!calls.some(c=>c.cancel);i++)await new Promise(r=>setTimeout(r,20))
 assert.ok(calls.some(c=>c.cancel==='/execute/'+calls[0].id));assert.equal(calls.filter(c=>!c.cancel).length,1)
}))
