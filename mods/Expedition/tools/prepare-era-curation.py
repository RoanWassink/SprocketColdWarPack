"""Prepare an explicit active/reserve marker selection; never edits game files.
Native stock/faction entries, starter, approved additions and veterans stay owned
by their existing sources. Old runs use immutable library blobs, not pool markers.
"""
import argparse,base64,collections,csv,decimal,hashlib,importlib.util,json,pathlib,re

def main():
    ap=argparse.ArgumentParser();ap.add_argument('game');ap.add_argument('approved_import');ap.add_argument('output');a=ap.parse_args()
    game,approved_path,out=map(pathlib.Path,(a.game,a.approved_import,a.output));out.mkdir(parents=True,exist_ok=True)
    mod=game/'BepInEx/config/SprocketExpedition';pool=mod/'community-blueprints';reserve=mod/'community-blueprints-reserve'
    eras=sorted([json.loads(re.sub(r',\s*([}\]])',r'\1',p.read_text(encoding='utf-8-sig'))) for p in (game/'Sprocket_Data/StreamingAssets/Eras').glob('*.json')],key=lambda e:e['start'])
    era_names=[e['name'] for e in eras]
    def era_for(date):return next((e['name'] for e in reversed(eras) if e['start']<=date),'')
    spec=importlib.util.spec_from_file_location('role_audit',pathlib.Path(__file__).with_name('report-spawn-pool.py'));roles=importlib.util.module_from_spec(spec);spec.loader.exec_module(roles)
    catalog_path=max((p for p in (mod/'slots').glob('slot-*/catalogues/*.json') if '.pre-encounter' not in str(p)),key=lambda p:p.stat().st_mtime)
    envelope=json.loads(catalog_path.read_bytes());assert hashlib.sha256(envelope['Payload'].encode()).hexdigest()==envelope['Hash']
    catalog=json.loads(envelope['Payload']);old={t['Hash']:t for t in catalog['Tanks']}
    pool_paths={p.stem:p for p in pool.glob('*.blueprint')}
    reserve_paths={p.stem:p for p in reserve.glob('*.blueprint')} if reserve.exists() else {}
    marker_paths=dict(reserve_paths);marker_paths.update(pool_paths)
    approved=json.loads(approved_path.read_text(encoding='utf-8'))
    approved_hashes={e['hash'] for e in approved['entries']}
    rows={};native_required=set();veterans=set()
    def make(sha,name,era,price,mass,source,marker=False,veteran=False):
        role,confidence,reason=roles.classify(name);family=roles.family(name)
        # Auditable role proposals for the approved fictional support designs.
        # These do not alter runtime AI roles or native mass classifications.
        proposed={'vrrc-155':'Mobile artillery','pzh6000':'Mobile artillery',
            'variant13':'Armoured / support'}
        if roles.norm(name) in proposed:
            role=proposed[roles.norm(name)];confidence='Role proposal';reason='Approved name/cannon-based support proposal; not live role verification'
        if 'stormgaupe' in roles.norm(name):
            role='Armoured / support';confidence='Role proposal';reason='Light multi-cannon support proposal'
        if re.search(r'karl[- ]?ger[aä]t',name,re.I):
            role='Mobile artillery';confidence='Inferred';reason='Named self-propelled mortar family; native movement still requires gameplay check'
        native=next(e for e in eras if e['name']==era)
        mass_class=None if mass is None else ('Heavy' if mass>=native['heavyVehicleMass'] else 'Medium' if mass>=native['mediumVehicleMass'] else 'Light')
        if role=='Unknown' and mass_class:
            role=mass_class;confidence='Native mass';reason='Saved mass bucket; tactical properties still need gameplay review'
        return dict(Hash=sha,Id='native.'+sha[:32],Name=name,Era=era,Price=price,Role=role,Confidence=confidence,
            Reason=reason,Family=family,MassKg=mass,Marker=marker,Veteran=veteran,Approved=sha in approved_hashes,
            Required=False,Selected=False,Eligible=True,Source=source)
    def components(path):
        required={b'engine',b'transmission',b'track',b'cannon'};found=set();tail=b''
        pattern=re.compile(rb'"type"\s*:\s*"(engine|transmission|track|cannon)"')
        with path.open('rb') as f:
            while block:=f.read(65536):
                text=tail+block;found.update(pattern.findall(text));tail=text[-64:]
                if required<=found:return True
        return False
    def header(path):
        # The current vehicle header precedes meshes. Read just that object,
        # rather than decoding multi-GB mesh arrays for a name-role audit.
        with path.open('rb') as f:raw=f.read(65536)
        text=raw.decode('utf-8-sig',errors='strict');match=re.search(r'"header"\s*:\s*',text)
        if not match:return None
        try:return json.JSONDecoder().raw_decode(text,match.end())[0]
        except json.JSONDecodeError:return None
    for sha,p in marker_paths.items():
        h=header(p)
        if not h or not h.get('creationDate') or not h.get('name') or not isinstance(h.get('cost'),(int,float)) or h['cost']<=0:continue
        era=era_for(h['creationDate'])
        if era not in era_names:continue
        # Previously native-measured headers retain their exact saved scalar.
        t=old.get(sha);price=t['Price'] if t else h['cost'];name=t['Name'] if t else h['name'];era=t['Era'] if t else era
        rows[sha]=make(sha,name,era,price,h.get('mass'),str(p),marker=True)
        if not components(p):
            rows[sha]['Eligible']=False
            rows[sha]['Reason']='Missing engine/transmission/track/cannon component; excluded from mobile tank selection'
    for sha,t in old.items():
        if sha in marker_paths or t['Name'].endswith(' (Veteran)'):continue
        mass=None
        blob=mod/'content-library/blueprint-blobs'/(sha+'.blueprint')
        if blob.exists():mass=(header(blob) or {}).get('mass')
        rows[sha]=make(sha,t['Name'],t['Era'],t['Price'],mass,'native stock / Expedition Spawns')
        native_required.add(sha)
    for p in (mod/'veterans').glob('*.json'):
        env=json.loads(p.read_bytes());assert hashlib.sha256(env['Payload'].encode()).hexdigest()==env['Hash']
        t=json.loads(env['Payload']);raw=base64.b64decode(t['Bytes']);assert hashlib.sha256(raw).hexdigest()==t['Hash']
        mass=json.loads(raw).get('header',{}).get('mass')
        rows[t['Hash']]=make(t['Hash'],t['Name']+' (Veteran)',t['Era'],t['Price'],mass,str(p),veteran=True)
        veterans.add(t['Hash'])
    assert approved_hashes<=rows.keys(),'Approved design missing from current library'
    assert not (native_required|approved_hashes)&{h for h,r in rows.items() if r['Role']=='Possible stationary gun' or not r['Eligible']},'A required design needs explicit role/mobility review'
    assert set(era_names)=={r['Era'] for r in rows.values()}
    assert catalog['StarterId'] in {rows[h]['Id'] for h in native_required},'Original starter is not protected'
    quota={'Light':5,'Medium':7,'Heavy':4,'TD / assault gun':5,'Mobile artillery':2,'Armoured / support':2}
    summaries=[]
    for era in era_names:
        candidates=[r for r in rows.values() if r['Era']==era and not r['Veteran'] and r['Role']!='Possible stationary gun' and r['Eligible']]
        required=[r for r in candidates if r['Hash'] in native_required or r['Approved']]
        # Protect a playable low-cost option and the existing max-cost boss rule.
        cheapest=min(candidates,key=lambda r:(r['Price'],r['Hash']));priciest=max(candidates,key=lambda r:(r['Price'],r['Hash']))
        required += [cheapest,priciest]
        selected={r['Hash']:r for r in required}
        for r in selected.values():r['Required']=True
        target=20 if era==era_names[0] else 25
        # Only reviewed, imported Coldwar designs; unapproved tests/outliers are
        # not filler for the desired 20-30 target.
        if era==era_names[-1]:candidates=[r for r in candidates if r['Hash'] in old or r['Approved']]
        target=min(target,len(candidates))
        prices=sorted({r['Price'] for r in candidates})
        def band(r):return min(2,prices.index(r['Price'])*3//len(prices))
        def choose(candidates,needed,cap=2):
            while needed>0 and len(selected)<target:
                families=collections.Counter(r['Family'] for r in selected.values());names=collections.Counter(roles.norm(r['Name']) for r in selected.values());bands=collections.Counter(band(r) for r in selected.values())
                remaining=[r for r in candidates if r['Hash'] not in selected and families[r['Family']]<cap]
                if not remaining:break
                row=min(remaining,key=lambda r:(names[roles.norm(r['Name'])]>0,families[r['Family']],bands[band(r)],r['Confidence']=='Review',r['Hash']))
                selected[row['Hash']]=row;needed-=1
        for role,count in quota.items():choose([r for r in candidates if r['Role']==role],max(0,count-sum(r['Role']==role for r in selected.values())))
        choose(candidates,target-len(selected))
        # WWI has fewer families than the requested file count. Fill with
        # available distinct snapshots, and report repeated families honestly.
        if len(selected)<target:choose(candidates,target-len(selected),cap=100)
        assert len(selected)<=30 and all(r['Role']!='Possible stationary gun' for r in selected.values())
        for r in selected.values():r['Selected']=True
        earned=[r for r in rows.values() if r['Era']==era and r['Veteran']]
        for r in earned:r['Selected']=True;r['Required']=True
        summaries.append(dict(Era=era,BaseDesigns=len(selected),Veterans=len(earned),Total=len(selected)+len(earned),
            Families=len({r['Family'] for r in selected.values()}),Roles=dict(collections.Counter(r['Role'] for r in selected.values())),
            LowCost=cheapest['Price'],BossCandidate=priciest['Name'],BossCost=priciest['Price']))
    active={r['Hash'] for r in rows.values() if r['Selected'] and r['Marker']}
    keep=sorted(active&pool_paths.keys());disable=sorted(pool_paths.keys()-active);enable=sorted(active&reserve_paths.keys()-pool_paths.keys())
    selected_rows=[r for r in rows.values() if r['Selected']]
    plan=dict(Schema=1,Status='Prepared',GameRoot=str(game),ActiveDirectory=str(pool),ReserveDirectory=str(reserve),
        CatalogueSource=str(catalog_path),InitialBaseBudget=catalog['InitialBudget'],Definition=catalog['Definition'],
        BeforePool=sorted(pool_paths),Keep=keep,Disable=disable,Enable=enable,Selected=selected_rows,Summaries=summaries,
        MarkerSizes={sha:marker_paths[sha].stat().st_size for sha in marker_paths})
    plan['ExcludedIncomplete']=[dict(Hash=r['Hash'],Name=r['Name'],Era=r['Era'],Reason=r['Reason']) for r in rows.values() if not r['Eligible']]
    (out/'curation-plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
    with (out/'selected-designs.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(selected_rows[0]));w.writeheader();w.writerows(sorted(selected_rows,key=lambda r:(era_names.index(r['Era']),r['Role'],r['Price'],r['Name'])))
    lines=['# Actieve selectie voor alle era’s — 25 oktober 2026','',
        'Dit plan selecteert de actieve communitymarkers. De volledige blueprintbibliotheek en persoonlijke bestanden blijven behouden. Overige markers gaan naar een reserve-map; opgeslagen runs lezen hun eigen immutable blobs en catalogi. Native starters/stock, expliciet goedgekeurde toevoegingen en verdiende veteranen blijven beschikbaar.', '',
        '| Era | Basisdesigns | Veterans | Totaal | Naamsfamilies in basis |','|---|---:|---:|---:|---:|']
    for s in summaries:lines.append(f"| {s['Era']} | {s['BaseDesigns']} | {s['Veterans']} | {s['Total']} | {s['Families']} |")
    lines += ['', 'De selectie spreidt voorlopige naamrollen en native kostenniveaus. Onbekende naamrollen kunnen de opgeslagen native massaklasse als hulpmiddel gebruiken; dit bewijst geen pantserkwaliteit of gevechtsrol. De goedkoopste optie en duurste mobiele boss-kandidaat blijven behouden. Coldwar gebruikt alleen de goedgekeurde uitbreiding plus de bestaande pool, zonder tests of extreme extra imports.', '',
        'WWI haalt 20 basisbestanden met enkele varianten van dezelfde familie. Coldwar heeft 16 beschikbare goedgekeurde designs en mist dus nog vier om 20 te halen. Extra veteranen mogen de basis later uitbreiden, zoals eerder afgesproken.', '',
        f"Actieve communitymarkers: {len(active)}; reserve: {len(disable)}. Totale actieve pool inclusief native bronnen en huidige veteranen: {len(selected_rows)}.", '',
        '## Selectie per era','']
    for era in era_names:
        lines += ['### '+era,'','| Naam | Voorlopige rol | Kosten | Bron |','|---|---|---:|---|']
        for r in sorted([r for r in selected_rows if r['Era']==era],key=lambda r:(r['Role'],r['Price'])):
            lines.append(f"| {r['Name'].replace('|','/')} | {r['Role']} | {r['Price']:,.2f} | {'Veteran' if r['Veteran'] else 'Goedgekeurde faction' if r['Approved'] else 'Community' if r['Marker'] else 'Native/faction'} |")
        lines.append('')
    (out/'era-selectie.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(Summaries=summaries,Keep=len(keep),Reserve=len(disable),Enable=len(enable),Total=len(selected_rows)),ensure_ascii=True,indent=2))

if __name__=='__main__':main()
