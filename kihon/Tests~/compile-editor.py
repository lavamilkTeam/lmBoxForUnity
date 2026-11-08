"""Compile the package against the installed Unity editor, without copying a project."""
from pathlib import Path
import os, subprocess
root=Path(__file__).resolve().parent.parent
unity=Path(os.environ.get('UNITY_CONTENTS','/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents'))
profile=unity/'MonoBleedingEdge/lib/mono/4.7.1-api'
sdk=sorted((Path.home()/'.dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
output=root/'Cli~/obj/UnityEditor.validation.dll'
output.parent.mkdir(parents=True,exist_ok=True)
refs=list((unity/'NetStandard/ref/2.1.0').glob('*.dll'))+list((unity/'NetStandard/compat/2.1.0/shims/netfx').glob('*.dll'))
refs+=list((unity/'Managed').glob('*.dll'))+list((unity/'Managed/UnityEngine').glob('*.dll'))
newton=list((root.parent/'AIAgentDemo/Library/PackageCache').glob('com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll'))
if not newton: raise RuntimeError('Install the package in the existing AIAgentDemo before compiling')
refs+=newton
sources=list((root/'Editor').rglob('*.cs'))
cmd=[str(Path.home()/'.dotnet/dotnet'),str(sdk),'-nologo','-noconfig','-nostdlib+','-target:library','-langversion:9.0',f'-out:{output}']+[f'-reference:{p}' for p in refs]+[str(p) for p in sources]
try: result=subprocess.run(cmd,cwd=root)
finally: output.unlink(missing_ok=True)
raise SystemExit(result.returncode)
