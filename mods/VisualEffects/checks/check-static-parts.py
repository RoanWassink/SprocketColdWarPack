# Executed by checks/check_contract.py with mod/check/json/re already defined.
# 0.4.5 (route B): VE no longer swaps a native mesh or material, so the 0.4.3 dynamic:true exception is gone.
# Every VE lamp part is a plain static stock-model part, and no source file may call a native part setter.
import copy
# Unity's own Mesh.MarkDynamic() on VE's own effect meshes (EffectDriver, PuffRenderer) is not a part setter.
FORBIDDEN=[r'\b(?![Mm]esh\.)\w+\.MarkDynamic\s*\(',r'\bSetMesh\s*\(',r'\bSetShadowProxy\s*\(',r'\bSetInteractionMesh\s*\(',r'\bSetMaterials\s*\(',
           r'\.Materials\s*=',r'\bSetCollisionMesh\s*\(',r'\bVehicleMaterial\b',r'\bExteriorPaint\b',r'\bLampMesh\b']
def assert_static(parts):
 count=0
 for name,part in parts:
  if any(c.get('fileID')=='vfxLamp' for c in part.get('components',[])):
   if part.get('transform',{}).get('dynamic') is not False:
    raise AssertionError(name+' VE lamp must be static (dynamic:false) like the stock lamp')
   count+=1
 return count
def assert_no_setter(name,text):
 for pattern in FORBIDDEN:
  if re.search(pattern,text):raise AssertionError(name+' uses forbidden native part setter/wrapper '+pattern)
parts=[(p.name,json.loads(p.read_text())) for p in (mod/'parts').rglob('*.json')]
check(assert_static(parts)==3,'all three VE lamp part definitions dynamic:false (static stock-model parts)')
for name,part in parts:
 for bad in [True,None]:
  mutant=copy.deepcopy(part)
  if bad is None:mutant['transform'].pop('dynamic')
  else:mutant['transform']['dynamic']=bad
  try:assert_static([(name,mutant)])
  except AssertionError:pass
  else:raise AssertionError('static guard accepted dynamic/missing '+name)
check(True,'static guard rejects dynamic:true and missing dynamic on each lamp (6 negative cases)')
for p in mod.glob('*.cs'):assert_no_setter(p.name,p.read_text(encoding='utf-8'))
check(True,'no source file calls MarkDynamic/SetMesh/SetShadowProxy/SetInteractionMesh/SetMaterials/Materials=/SetCollisionMesh or uses VehicleMaterial/ExteriorPaint/LampMesh')
for probe in ['native.SetMesh(mesh);','model.MarkDynamic();','owner.MarkDynamic(true);','native.SetMaterials(x);','r.Materials = m;','new VehicleMaterial(m)']:
 try:assert_no_setter('probe',probe)
 except AssertionError:pass
 else:raise AssertionError('setter scan missed '+probe)
check(True,'setter scan rejects 6 negative probes (SetMesh, MarkDynamic, SetMaterials, Materials=, VehicleMaterial)')
