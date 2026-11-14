"""Compile exact production method bodies against native doubles; no reimplemented discovery.

The focused partial and PanelFlow are linked directly. This extracts their existing runtime
dependencies verbatim, avoiding mesh/pose/Harmony emulation. Hashes make the boundary reviewable.
"""
from pathlib import Path
import hashlib, json
root = Path(__file__).resolve().parents[1]
out = root / 'tests/DiscoveryRepro/obj/RuntimeDependencies.cs'
def block(text, marker):
    start = text.index(marker)
    pos = text.index('{', start); depth = 1; i = pos + 1
    # These selected methods have balanced braces even inside their string literals.
    while depth:
        depth += (text[i] == '{') - (text[i] == '}'); i += 1
    return text[start:i]
spec = {
 'HingeRuntime.cs': ['internal sealed class ExternalOutput', 'internal sealed class VehicleIo',
  'private static VehicleComponent? ExternalComponent(', 'private static string ExternalComponentId(', 'private static GameObject? ExternalPartObject(',
  'internal static bool IsExternalPart(', 'internal static bool IsManagedPart(',
  'internal static void PrepareExternal(', 'private static void LoadExternal(',
  'private static void ApplyExternal(', 'private static GameObject? VehicleGo(', 'private static void TickExternal(',
  'private sealed class SerializationStateBag'],
 'FlowRuntime.cs': ['private sealed class NativeFlowPart', 'private sealed class FlowSession',
  'internal static bool FlowComponent(', 'private static bool IsFlowPart(',
  'internal static void RegisterFlowPart(', 'private static string StablePartKey(',
  'private static FlowSession? SessionFor(VehicleObject?', 'private static FlowSession? SessionFor(VehicleIo',
  'internal static bool HasFlowParts(', 'internal static void PrepareFlows(',
  'private static void TraceFlow(', 'private static void AdmitStored(', 'private static void TickFlow(',
  'private static bool FlowTarget(VehicleIo io, ExternalOutput', 'private static void SaveFlow(', 'private static void LoadFlow(']}
chunks=[]; hashes={}
assert '        TickExternal(io);' in (root/'HingeRuntime.cs').read_text(), 'production TickVehicle must dispatch external outputs'
for file, markers in spec.items():
    text=(root/file).read_text(encoding='utf-8-sig')
    hashes[file]=hashlib.sha256((root/file).read_bytes()).hexdigest()
    for marker in markers:
        start=text.index(marker)
        # expression-bodied members end at the first semicolon
        brace=text.find('{',start); arrow=text.find('=>',start); semi=text.find(';',start)
        if arrow >= 0 and arrow < brace and semi < brace:
            chunks.append(text[start:semi+1])
        else: chunks.append(block(text,marker))
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('using Sprocket.Vehicles;\nusing SprocketTelescopicMast.Io;\nusing UnityEngine;\nusing Il2CppInterop.Runtime;\nusing SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;\nnamespace SprocketTelescopicMast;\ninternal static partial class HingeRuntime {\n'+'\n'.join(chunks)+'\n}\n',encoding='utf-8')
for file in ('ExternalDiscoveryRuntime.cs','FlowPanelRuntime.cs'):
    hashes[file]=hashlib.sha256((root/file).read_bytes()).hexdigest()
(root/'logs').mkdir(exist_ok=True)
(root/'logs/discovery-production-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
print('PASS generated exact runtime dependencies; discovery and PanelFlow linked directly')
