# ICF check: every patched method must own its native address.
# Patch forms read from Patches.cs / BuilderPatches.cs:
#   [HarmonyPatch(typeof(X),nameof(X.Y))]                 -> exactly one native method X::Y(...)
#   // ICF: Type::Name/N                                   -> the overload of Type::Name with N parameters (marker comment next to a patch that selects an overload)
#   // ICF-INFO: Type::Name/N                              -> NOT patched; the sharing is only reported (why the patch was left out)
#   // ICF-TWIN: Type::Name/N                              -> patched; the address may be shared ONLY with its own explicit-interface twin (IVehicleComponentDamageModelBuilder.Name, same logical method). Any other sharer = FAIL.
# usage: py -I checks\icf-check.py <src dir> <methods.json> <GameAssembly.dll> <out file>
import json,re,sys,hashlib,os
src,meth,ga,out=sys.argv[1:5]
text=''
for f in ('Patches.cs','BuilderPatches.cs'):
    p=os.path.join(src,f)
    if os.path.exists(p):text+=open(p,encoding='utf-8').read()+'\n'
patches=sorted(set(re.findall(r'HarmonyPatch\(typeof\((\w+)\),nameof\(\w+\.(\w+)\)\)',text)))
overloads=sorted(set((a,b,int(c)) for a,b,c in re.findall(r'//\s*ICF:\s*(\w+)::(\w+)/(\d+)',text)))
twins=sorted(set((a,b,int(c)) for a,b,c in re.findall(r'//\s*ICF-TWIN:\s*(\w+)::(\w+)/(\d+)',text)))
infos=sorted(set((a,b,int(c)) for a,b,c in re.findall(r'//\s*ICF-INFO:\s*(\w+)::(\w+)/(\d+)',text)))
d=json.load(open(meth,encoding='utf-8-sig'))
sha=hashlib.sha256(open(ga,'rb').read()).hexdigest().upper()
BASE='77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388'  # full SHA256 of the GameAssembly.dll the address list was made from (buildid 26757044, dump of 2026-11-18, all 225 assemblies); previous build 3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B (2026-10-04 dump)
lines=[f'GameAssembly.dll SHA256 {sha}',f'methods.json baseline: {BASE} (buildid 26757044 dump) -> {"MATCH" if sha==BASE else "MISMATCH, re-resolve before trusting the RVAs"}',f'[HarmonyPatch count in source: {len(re.findall(r"HarmonyPatch\(typeof",text))}; overload markers: {len(overloads)}; twin markers: {len(twins)}']
bad=0
if sha!=BASE:bad+=1
def nparams(name):
    inner=name[name.index('(')+1:name.rindex(')')]
    if not inner.strip():return 0
    depth=0;n=1
    for ch in inner:
        if ch in '<[(':depth+=1
        elif ch in '>])':depth-=1
        elif ch==',' and depth==0:n+=1
    return n
def sharing(m):
    return [x['Name'] for x in d if x['RVA']==m['RVA'] and x is not m]
for typ,name in patches:
    hits=[m for m in d if re.search(r'\.'+typ+'::'+name+r'\(',m['Name'])]
    if len(hits)!=1:
        lines.append(f'FAIL {typ}.{name}: {len(hits)} candidates in methods.json');bad+=1;continue
    m=hits[0];shared=sharing(m)
    ok=len(shared)==0
    lines.append(f"{'OK  ' if ok else 'FAIL'} {typ}.{name} RVA {m['RVA']} len {m['Length']} ({int(m['Length'],16)} B) shares its address with {len(shared)} other method(s){': '+'; '.join(shared[:3]) if shared else ''}")
    if not ok:bad+=1
for typ,name,n in overloads:
    hits=[m for m in d if re.search(r'[\.\/]'+typ+'::'+name+r'\(',m['Name']) and nparams(m['Name'])==n]
    if len(hits)!=1:
        lines.append(f'FAIL {typ}.{name}/{n} params: {len(hits)} candidates in methods.json');bad+=1;continue
    m=hits[0];shared=sharing(m)
    ok=len(shared)==0
    lines.append(f"{'OK  ' if ok else 'FAIL'} {typ}.{name}/{n} RVA {m['RVA']} len {m['Length']} ({int(m['Length'],16)} B) shares its address with {len(shared)} other method(s){': '+'; '.join(s.split('::')[-1][:110] for s in shared[:3]) if shared else ''}")
    if not ok:bad+=1
for typ,name,n in twins:
    hits=[m for m in d if re.search(r'[\.\/]'+typ+'::'+name+r'\(',m['Name']) and nparams(m['Name'])==n]
    if len(hits)!=1:
        lines.append(f'FAIL {typ}.{name}/{n} params: {len(hits)} candidates in methods.json');bad+=1;continue
    m=hits[0];shared=sharing(m)
    twin_re=re.compile(r'::(?:[\w.]*\.)?IVehicleComponentDamageModelBuilder\.'+name+r'\(')
    others=[x for x in shared if not twin_re.search(x)]
    twins_found=[x for x in shared if twin_re.search(x)]
    ok=len(others)==0
    lines.append(f"{'OK  ' if ok else 'FAIL'} {typ}.{name}/{n} RVA {m['RVA']} len {m['Length']} ({int(m['Length'],16)} B) shares its address with {len(shared)} other method(s): {len(twins_found)} explicit-interface twin(s) (same logical method, allowed) and {len(others)} other sharer(s){': '+'; '.join(s2.split('::')[-1][:110] for s2 in others[:3]) if others else ''}")
    if not ok:bad+=1
for typ,name,n in infos:
    hits=[m for m in d if re.search(r'[\.\/]'+typ+'::'+name+r'\(',m['Name']) and nparams(m['Name'])==n]
    for m in hits:
        shared=sharing(m)
        lines.append(f"INFO (not patched) {typ}.{name}/{n} RVA {m['RVA']} len {m['Length']} ({int(m['Length'],16)} B) shares its address with {len(shared)} other method(s){': '+'; '.join(s.split('::')[-1][:110] for s in shared[:3]) if shared else ''}")
lines.append('SUMMARY ICF '+('PASS' if bad==0 else 'FAIL'))
open(out,'w',encoding='utf-8').write('\n'.join(lines)+'\n');print('\n'.join(lines))
sys.exit(1 if bad else 0)
