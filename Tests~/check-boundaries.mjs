import { readdir, readFile } from 'node:fs/promises'
import { resolve, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
const root=fileURLToPath(new URL('../',import.meta.url))
async function files(dir){const result=[];for(const e of await readdir(dir,{withFileTypes:true})){if(['node_modules','bin','obj','dist','.git'].includes(e.name))continue;const path=resolve(dir,e.name);if(e.isDirectory())result.push(...await files(path));else result.push(path)}return result}
const errors=[]
for(const path of await files(root)){
 const name=relative(root,path);if(!/\.(cs|ts|mjs)$/.test(name))continue
 const text=await readFile(path,'utf8')
 if(name.startsWith('Cli~/')&&/using\s+(UnityEngine|UnityEditor|Lmbox\.AIAgent)/.test(text))errors.push(name+': standalone CLI may not import Unity')
 if(name.startsWith('Editor/Commands/')&&/using\s+Lmbox\.AIAgent\.(Tools|WebAgent|Agent|LLM|Settings)/.test(text))errors.push(name+': command execution may not depend on host/agent modules')
 if(name.endsWith('.cs')&&!name.startsWith('Editor/Commands/')&&/Lmbox\.AIAgent\.Commands\.Internal/.test(text))errors.push(name+': private command implementation imported')
 if(name.startsWith('Editor/Commands/lib/')&&/public\s+(?:(?:static|sealed|abstract)\s+)*class/.test(text))errors.push(name+': private command class exposed')
 if(name.startsWith('WebAgent~/tests/')&&/from\s+['"][^'"]*agent\/dist\/lib\//.test(text))errors.push(name+': test deep-imports agent implementation')
 if(name.startsWith('WebAgent~/agent/')&&/from\s+['"][^'"]*(?:\/server\/|\/tests\/)/.test(text))errors.push(name+': agent depends on host/tests')
}
if(errors.length)throw new Error(errors.join('\n'))
console.log('Module boundaries passed (C# CLI / Unity commands / host / TS agent).')
