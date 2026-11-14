"""Extract production flow/discovery/cannon/mast bodies; only native boundaries are doubled."""
from pathlib import Path
import hashlib, json, sys
root=Path(__file__).resolve().parents[1]
baseline=False  # a baseline run needs the sources of an older release; a fresh release has none
if '--baseline' in sys.argv: raise SystemExit('--baseline needs an older release tree; there is none in this release.')
source=root
namespace={'__file__':str(root/'tools/Generate-DiscoveryRepro.py')}
script=(root/'tools/Generate-DiscoveryRepro.py').read_text()
exec(script[:script.index('chunks=[];')],namespace)
spec=namespace['spec'];block=namespace['block']
out=root/'tests/PlayRepro/obj/RuntimeDependencies.cs';out.parent.mkdir(parents=True,exist_ok=True)
chunks=[];hashes={}
for file,markers in spec.items():
 text=(source/file).read_text(encoding='utf-8-sig');hashes[file]=hashlib.sha256((source/file).read_bytes()).hexdigest()
 for marker in markers:
  start=text.index(marker);brace=text.find('{',start);arrow=text.find('=>',start);semi=text.find(';',start)
  chunks.append(text[start:semi+1] if arrow>=0 and arrow<brace and semi<brace else block(text,marker))
using='using Sprocket.Vehicles; using SprocketTelescopicMast.Io; using UnityEngine; using Il2CppInterop.Runtime; using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;\n'
result=using+'namespace SprocketTelescopicMast { internal static partial class HingeRuntime {\n'+'\n'.join(chunks)+'\n}\n'
text=(source/'HingeRuntime.cs').read_text(encoding='utf-8-sig')
frame=block(text,'private static void TickVehicle(').split('        RotatorRuntime.TickVehicle')[0]
result+='internal static partial class HingeRuntime {\n'+frame+'        TickExternal(io);\n    }\n}\n'
text=(source/'HingeIo.cs').read_text(encoding='utf-8-sig')
result+='\n'.join(block(text,marker) for marker in ('public sealed class VehicleIoContext','internal static class HingeCannons','internal static class HingeFire'))
text=(source/'Plugin.cs').read_text(encoding='utf-8-sig')
result+='\ninternal static partial class MastRuntime {\n'+'\n'.join(block(text,m) if '=>' not in text[text.index(m):text.index('{',text.index(m))] else text[text.index(m):text.index(';',text.index(m))+1] for m in ('internal static bool IsFlowOutput(', 'internal static void FlowCommand(', 'internal static void UnbindFlow(', 'internal static void TickFlow('))+'\n}\n}\n'
out.write_text('using Sprocket.Vehicles.Cannons; using Sprocket.Vehicles.Weapons.Cannons; using Sprocket.WeaponFramework; using Sprocket.Vehicles.Control;\n'+result)
(root/'logs').mkdir(exist_ok=True)
(root/'logs'/'play-production-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
print('Extracted production methods:',source)
