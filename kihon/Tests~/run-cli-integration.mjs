import { spawn, execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { once } from 'node:events'
import { readFile, writeFile, copyFile, mkdir, rm, lstat, rmdir } from 'node:fs/promises'
import { resolve, dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'

const exec = promisify(execFile)
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const project = resolve(process.argv[2] || join(root, '../AIAgentDemo'))
const version = /m_EditorVersion:\s*(\S+)/.exec(await readFile(join(project,'ProjectSettings/ProjectVersion.txt'),'utf8'))[1]
const unity = process.env.UNITY_EDITOR || `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`
const dotnet = process.env.LMBOX_DOTNET || join(process.env.HOME,'.dotnet/dotnet')
const script = join(project,'Assets/Editor/LmuCliIntegration.cs')
const prefix = join(project,'Library/LmuCliIntegration')
const log = prefix + '.log'
const dll = join(root,'WebAgent~/cli/lmu3d.dll')
const pause = ms => new Promise(r=>setTimeout(r,ms))
let child, exit, madeEditor = false, owns = false
let recipeIndex=0, checked=0
const recipes=[]
const env={...process.env}; for(const name of ['LMBOX_UNITY_BRIDGE_URL','LMBOX_UNITY_BRIDGE_TOKEN','LMBOX_UNITY_PROJECT','LMBOX_CLI_WORKSPACE'])delete env[name]
async function call(args,ok=true){
  let stdout
  try { ({stdout}=await exec(dotnet,[dll,...args,'--project',project],{env,timeout:130000,maxBuffer:4*1024*1024})) }
  catch(error){stdout=error.stdout;if(!stdout)throw error}
  const result=JSON.parse(stdout);assert.equal(result.ok,ok,JSON.stringify(result));checked++
  return result
}
async function recipe(operations,extra=[],ok=true){
  const path=prefix+`.recipe-${recipeIndex++}.json`;recipes.push(path)
  await writeFile(path,JSON.stringify({version:1,operations}))
  return call(['apply','--recipe',path,...extra],ok)
}
const data=reply=>reply.result.results[0].result
try {
  for(const path of [join(project,'Temp/UnityLockfile'),script,script+'.meta',prefix+'.ready',prefix+'.stop',prefix+'.result.json',join(project,'Assets/LmuCliIntegration')]) {
    try { await lstat(path);throw new Error(`Refusing to overwrite or run with an open project: ${path}`) } catch(e){if(e.code!=='ENOENT')throw e}
  }
  try{await lstat(dirname(script))}catch(e){if(e.code!=='ENOENT')throw e;madeEditor=true;await mkdir(dirname(script),{recursive:true})}
  await copyFile(join(root,'Tests~/CliIntegration.cs'),script);owns=true
  child=spawn(unity,['-batchmode','-projectPath',project,'-executeMethod','LmuCliIntegration.Run','-logFile',log],{stdio:['ignore','ignore','pipe'],env})
  child.stderr.resume();exit=once(child,'exit')
  let ready=false
  for(let i=0;i<240;i++){if(child.exitCode!==null)throw new Error('Unity exited before readiness');try{await readFile(prefix+'.ready');await readFile(join(project,'Library/LmBoxCli/connection.json'));ready=true;break}catch{}await pause(500)}
  assert.ok(ready,'Unity readiness timeout')
  console.log('Unity ready; testing actual C# CLI → authenticated HTTP → Unity editing')
  assert.equal(data(await call(['inspect'])).unityVersion,version)
  await call(['status']);await call(['help','terrain']);await call(['schema','terrain.paint'])
  await call(['vrchat','validate'],false)
  await call(['scripts','write','--path','Assets/LmuCliIntegration/Denied.txt','--content','must not write'],false)
  await call(['editor','menu','--path','Window/General/Console'],false)
  await call(['terrain','add','0'],false)
  await recipe([{op:'objects.add',name:'MustNotExist'},{op:'terrain.paint',target:'Missing',layer:0}],[],false)
  assert.equal(data(await call(['objects','list','--name','MustNotExist'])).items.length,0)
  await recipe([{op:'objects.add',name:'DryRunOnly'}],['--dry-run'])
  assert.equal(data(await call(['objects','list','--name','DryRunOnly'])).items.length,0)
  await call(['objects','add','--name','CliCube','--primitive','Cube','--pos=-1,0,0'])
  const id=randomUUID()
  await call(['objects','add','--name','Deduplicated','--request-id',id]);await call(['objects','add','--name','Deduplicated','--request-id',id])
  assert.equal(data(await call(['objects','list','--name','Deduplicated'])).items.length,1)
  await call(['objects','add','--name','Conflicting','--request-id',id],false)
  await recipe([{op:'objects.update',target:'CliCube',position:{x:1,y:1,z:2}},{op:'components.set',target:'CliCube',type:'BoxCollider',properties:{m_IsTrigger:true}}])
  const components=data(await call(['components','list','--target','CliCube'])).items
  assert.equal(components.find(c=>c.type==='UnityEngine.BoxCollider').properties.find(p=>p.path==='m_IsTrigger').value,true)
  await call(['components','add','--target','CliCube','--type','AudioSource'])
  await call(['components','remove','--target','CliCube','--type','AudioSource'])
  await call(['objects','duplicate','--target','CliCube','--name','Duplicate'])
  await call(['objects','remove','--target','Duplicate'])
  await recipe([{op:'objects.place',prefab:'Assets/LmuCliIntegration/Cube.prefab',name:'Placed',count:2,position:{x:1,y:0,z:1},step:{x:2,y:0,z:0}}])
  await call(['assets','prefab','--target','CliCube','--path','Assets/LmuCliIntegration/Saved.prefab'])
  await call(['assets','find','--type','Prefab']);await call(['assets','import','--path','Assets/LmuCliIntegration/texture0.png'])
  await recipe([{op:'materials.create',path:'Assets/LmuCliIntegration/Green.mat',shader:'Standard',properties:{_Color:'#00ff00',_Glossiness:0.2}}])
  await recipe([{op:'materials.update',path:'Assets/LmuCliIntegration/Green.mat',properties:{_Glossiness:0.4}},{op:'materials.apply',target:'CliCube',material:'Assets/LmuCliIntegration/Green.mat'}])
  await call(['terrain','add','1','--name','CliTerrain','--size=8,10,8','--resolution','33'])
  await recipe([0,1].map(i=>({op:'materials.layer',path:`Assets/LmuCliIntegration/Layer${i}.terrainlayer`,texture:`Assets/LmuCliIntegration/texture${i}.png`})))
  await recipe([{op:'terrain.layers',target:'CliTerrain',layers:[0,1].map(i=>`Assets/LmuCliIntegration/Layer${i}.terrainlayer`)}])
  const terrainRecipe=prefix+'.terrain.json';recipes.push(terrainRecipe)
  await writeFile(terrainRecipe,JSON.stringify({version:1,operations:[{op:'terrain.paint',target:'CliTerrain',layer:1}]}))
  await call(['terrain','paint','--recipe',terrainRecipe])
  await call(['objects','place','--recipe',terrainRecipe],false)
  await call(['terrain','height','--target','CliTerrain','--kind','noise','--seed','7','--amplitude','1','--frequency','4'])
  await call(['terrain','paint','--target','CliTerrain','--layer','0','--pos=2,0,2','--radius','1','--strength','0.5'])
  await call(['terrain','paint','--recipe',terrainRecipe])
  await call(['terrain','height','--target','CliTerrain','--elevation','2'])
  await recipe([{op:'terrain.trees',target:'CliTerrain',prefab:'Assets/LmuCliIntegration/Cube.prefab',instances:[{position:{x:0.5,y:0,z:0.5}}]}])
  await call(['terrain','details','--target','CliTerrain','--texture','Assets/LmuCliIntegration/texture0.png','--density','2'])
  await call(['lighting','add','--name','CliLamp','--type','Directional','--intensity','1'])
  await call(['lighting','apply','--target','CliLamp','--intensity','2'])
  await call(['camera','add','--name','Lakeside','--pos=0,4,-10','--background','#334455'])
  await call(['camera','update','--target','Lakeside','--fov','50'])
  const capture=data(await call(['scene','capture','--camera','Lakeside','--width','64','--height','64','--output','Assets/LmuCliIntegration/capture.png']))
  const png=await readFile(capture.path);assert.equal(png.subarray(1,4).toString(),'PNG');assert.equal(png.readUInt32BE(16),64)
  await call(['scene','inspect']);await call(['scene','save'])
  // Real bake submission, state query and cancellation; final rendering quality is not asserted.
  const bake=data(await call(['lighting','bake']));assert.ok(bake.requestId)
  const state=data(await call(['request','status',bake.requestId]));
  if(state.status==='running')await call(['request','cancel',bake.requestId])
  else assert.ok(['completed','failed','unknown'].includes(state.status))
  await call(['lighting','status']);await call(['logs','--level','error'])
  await call(['play','pause']);await call(['play','resume'])
  await call(['scene','new','--name','Extra']);await call(['scene','save','--path','Assets/LmuCliIntegration/Extra.unity'])
  await call(['scene','open','--path','Assets/LmuCliIntegration/Fixture.unity'])
  await writeFile(prefix+'.stop','ok')
  const [code]=await Promise.race([exit,pause(20000).then(()=>{throw new Error('Unity shutdown timeout')})]);assert.equal(code,0)
  const result=JSON.parse(await readFile(prefix+'.result.json','utf8'));assert.ok(result.ok,result.message)
  console.log(`PASS: ${checked} CLI checks, real scene/terrain/material/component/light/prefab edits, PNG capture, bake lifecycle, validation and request deduplication`)
} catch(error){console.error('Unity log:',log);throw error}
finally {
  if(child&&child.exitCode===null){await writeFile(prefix+'.stop','failed');await Promise.race([exit,pause(5000)]);if(child.exitCode===null){child.kill('SIGTERM');await Promise.race([exit,pause(5000)])}}
  if(owns){for(const p of [script,script+'.meta',prefix+'.ready',prefix+'.stop',prefix+'.result.json',...recipes])await rm(p,{force:true});if(madeEditor)try{await rmdir(dirname(script));await rm(dirname(script)+'.meta',{force:true})}catch{}}
}
