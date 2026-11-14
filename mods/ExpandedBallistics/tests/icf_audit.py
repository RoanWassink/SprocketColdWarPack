import os,pathlib,re,json,collections,hashlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=pathlib.Path(os.environ.get('EB_TEST_OUT') or ROOT/'bin'/'test-output')  # the audit's evidence goes to the test output folder
DOC=ROOT/'docs'  # ICF-AUDIT.md documents every reviewed shared-address exception
# Dumps of the game's native methods (derived from the game's own metadata, so not distributed with the source):
# set EB_NATIVE_METHODS and EB_NATIVE_METHODS_ALL to the two JSON files to run the audit; without them the audit is skipped.
DUMP=pathlib.Path(os.environ.get('EB_NATIVE_METHODS','native-methods.json'))
ALL=pathlib.Path(os.environ.get('EB_NATIVE_METHODS_ALL','native-methods-all-assemblies.json'))
def read(p):return p.read_text(encoding='utf-8-sig')
def strip(s):return re.sub(r'//[^\n]*|/\*.*?\*/','',s,flags=re.S)
def norm(t):
 t=t.replace('global::','').replace('/','.').replace('Il2CppInterop.Runtime.InteropTypes.Arrays.','').replace('Il2CppStringArray','System.String[]')
 for a,b in [('Single','float'),('Double','double'),('Int32','int'),('String','string'),('Boolean','bool')]:t=t.replace('System.'+a,b)
 return t.split('.')[-1]
def extract(root):
 rows=[];dynamic=[]
 for p in sorted(root.rglob('*.cs')):
  if any(x in p.parts for x in ('tests','tools','obj','bin')):continue
  s=strip(read(p)); aliases=dict(re.findall(r'using\s+(\w+)\s*=\s*([^;]+);',s))
  # Every attribute with a target must be understood, or the audit fails closed.
  matches=list(re.finditer(r'HarmonyPatch\s*\(\s*typeof\(([^)]+)\)\s*,\s*(?:nameof\(([^)]+)\)|"([^"]+)")',s))
  assert len(matches)==len(re.findall(r'HarmonyPatch\s*\(',s)),f'Unparsed target: {p}'
  for m in matches:
   typ=aliases.get(m[1],m[1]).replace('global::','').replace('/','.'); method=m[2].split('.')[-1] if m[2] else m[3]; i=m.end();n=1
   while i<len(s) and n:
    if s[i]=='(':n+=1
    elif s[i]==')':n-=1
    i+=1
   tail=s[m.end():i];params=re.findall(r'typeof\(([^)]+)\)',tail)
   if 'MethodType.Getter' in tail:method='get_'+method
   if method.startswith('Sprocket_'):method=method.replace('_','.')
   rows.append({'file':p.relative_to(root).as_posix(),'type':typ,'method':method,'params':params,'explicit':bool(params) or bool(re.search(r'new\s*Type\s*\[\]\s*\{\s*\}',tail)),'line':read(p).count('\n',0,read(p).find(m[0]))+1})
  for m in re.finditer(r'\bharmony\.Patch\((\w+)',s):dynamic.append((p.relative_to(root).as_posix(),m[1]))
 assert sorted(dynamic)==sorted([('RuntimeSightIntegration.cs',x) for x in ['fillMethod','fillMethod','forCannonMethod','configureZoomMethod']]),f'Unreviewed dynamic patches {dynamic}'
 return rows,dynamic
def audit(rows,primary,allrows):
 # Cross-assembly ICF collisions count too. Remove duplicate records from overlapping dumps.
 combined={d['Name']+'@'+str(d['RVA']):d for d in allrows+primary}
 byaddr=collections.defaultdict(list);byname=collections.defaultdict(list)
 for d in combined.values():
  if not d['RVA'] or int(d['RVA'],16)==0:continue
  byaddr[d['RVA'].lower()].append(d)
  signature=d['Name'];typename,method=signature.split('::',1);typename=typename.rsplit(' ',1)[-1].replace('/','.')
  byname[(typename,method.split('(',1)[0])].append(d)
 results=[];errors=[]
 for row in rows:
  typ=row['type'];candidates=[]
  for (t,m),ds in byname.items():
   if m==row['method'] and (t==typ or t.endswith('.'+typ)):candidates+=ds
  if row['explicit']:
   candidates=[d for d in candidates if [norm(t.strip()) for t in d['Name'].split('(',1)[1][:-1].split(',') if t.strip()]==[norm(t) for t in row['params']]]
  if len(candidates)!=1:
   errors.append(f"{row['file']} {typ}::{row['method']} {row['params']}: {len(candidates)} matches");continue
  d=candidates[0];shared=sorted(x['Name'] for x in byaddr[d['RVA'].lower()] if x['Name']!=d['Name'])
  reason=''
  if shared:
   if d['Name']=='System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::RequestMeshRefresh()' and shared==['System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::OnAssignedMeshModified(System.Object,System.EventArgs)']:
    reason='ALLOW-ICF-MESH: only the no-argument this trampoline; all three callbacks accept PlateStructure __instance only. Both callers have this exact class; instance/null guards restrict cache invalidation, queued refresh and restoration of tracked renderer visibility. No event arguments or game-data writes; 0.22.31 permits only reversible forceRenderingOff writes.'
   else:errors.append('UNDOCUMENTED SHARED: '+d['Name']+' '+d['RVA']+' with '+str(len(shared)))
  results.append({**row,**d,'shared':shared,'reason':reason})
 return results,errors

def main():
 if not DUMP.is_file() or not ALL.is_file():
  print('ICF audit SKIPPED: set EB_NATIVE_METHODS and EB_NATIVE_METHODS_ALL to the native-method dumps.');return False
 OUT.mkdir(parents=True,exist_ok=True)
 primary=json.loads(read(DUMP));allrows=json.loads(read(ALL));rows,dynamic=extract(ROOT);results,errors=audit(rows,primary,allrows)
 primary_map=collections.defaultdict(set)
 for d in primary:primary_map[d['Name']].add((d['RVA'],d['Length']))
 assert all(d['Name'] not in primary_map or (d['RVA'],d['Length']) in primary_map[d['Name']] for d in allrows if d['Assembly'].startswith('Sprocket')), 'Cpp2IL/primary native addresses differ'
 if '--probe' in sys.argv:
  print('\n'.join(errors));print('annotations',len(rows),'resolved',len(results),'shared',[(r['Name'],len(r['shared'])) for r in results if r['shared']]);return bool(errors)
 # Negative gate: the old empty-ret target must never be accepted again.
 _,bad=audit([{'file':'negative','type':'VehicleComponent','method':'Release','params':[],'explicit':False}],primary,allrows)
 assert any('UNDOCUMENTED SHARED' in e for e in bad),'Negative shared-address test failed'
 assert all(r['reason'] in read(DOC/'ICF-AUDIT.md') for r in results if r['shared']),'Shared exception absent from audit document'
 (OUT/'ICF-TARGETS.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
 if errors:raise AssertionError('\n'.join(errors))
 print(f'ICF PASS: {len(rows)} source annotations; {len(set(r["Name"] for r in results))} native targets; 3 optional managed targets; {len([r for r in results if r["shared"]])} documented shared annotations. Negative shared-address control PASS.')
 return False
if __name__=='__main__':sys.exit(main())
