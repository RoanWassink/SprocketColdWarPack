"""Local, read-only faction inventory; no imports, migrations or date rewrites."""
import argparse, collections, csv, hashlib, json, pathlib, re

def load_json(raw):
    # Native Era data permits trailing commas. Blueprint JSON remains strict.
    return json.loads(re.sub(r',\s*([}\]])',r'\1',raw))

def main():
    ap=argparse.ArgumentParser();ap.add_argument('factions');ap.add_argument('game');ap.add_argument('output');args=ap.parse_args()
    root,game,out=map(pathlib.Path,(args.factions,args.game,args.output));out.mkdir(parents=True,exist_ok=True)
    eras=sorted((load_json(p.read_text(encoding='utf-8-sig')) for p in (game/'Sprocket_Data/StreamingAssets/Eras').glob('*.json')),key=lambda e:e['start'])
    assert all(e.get('name') and e.get('start') for e in eras)
    def era_for(date):
        matches=[e for e in eras if isinstance(date,str) and e['start']<=date]
        return matches[-1]['name'] if matches else ''
    slots=game/'BepInEx/config/SprocketExpedition/slots'
    catalogue_path=max((p for p in slots.glob('slot-*/catalogues/*.json') if '.pre-encounter' not in str(p)),key=lambda p:p.stat().st_mtime)
    envelope=json.loads(catalogue_path.read_text(encoding='utf-8-sig'))
    assert hashlib.sha256(envelope['Payload'].encode()).hexdigest()==envelope['Hash']
    catalogue=json.loads(envelope['Payload']);pool_hashes={t['Hash'] for t in catalogue['Tanks']}
    # Cache canonical JSON hashes only for source files that also occur in the
    # pinned pool, avoiding a scan through GBs of unrelated immutable designs.
    rows=[];modern=[];legacy=[];parts=[];errors=[]
    for p in sorted(root.rglob('*.blueprint')):
        if not ('Blueprints' in p.parts and 'Vehicles' in p.parts): continue
        raw=p.read_bytes();sha=hashlib.sha256(raw).hexdigest();relative=str(p.relative_to(root));faction=str(p.parent.parent.parent.relative_to(root))
        try: d=json.loads(raw)
        except Exception as ex: errors.append(dict(Source=relative,Reason=str(ex)));continue
        h=d.get('header')
        if not isinstance(h,dict) or not h.get('name') or not h.get('creationDate'):
            target=legacy if isinstance(d.get('blueprints'),list) and d['blueprints'] else parts
            target.append(dict(Source=relative,Faction=faction,Name=d.get('Name') or d.get('name') or p.stem,
                LegacyEra=h.get('era') if isinstance(h,dict) else None,
                MassKg=h.get('mass') if isinstance(h,dict) else d.get('fullMass'),Hash=sha,
                Reason='Legacy vehicle, no modern native date/cost header' if target is legacy else 'No native vehicle header; component or unsupported format'))
            continue
        era=era_for(h['creationDate']);definition=next((e for e in eras if e['name']==era),None);mass=h.get('mass')
        mass_class='' if not definition or not isinstance(mass,(int,float)) else ('Heavy' if mass>=definition['heavyVehicleMass'] else 'Medium' if mass>=definition['mediumVehicleMass'] else 'Light')
        blocks=d.get('blueprints',[]);counts=collections.Counter(b.get('type') for b in blocks if isinstance(b,dict))
        calibers=[b.get('blueprint',{}).get('caliber') for b in blocks if b.get('type')=='cannon']
        canon=hashlib.sha256(json.dumps(d,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode()).hexdigest()
        notes=[]
        if p.stem.lower()=='autosave':notes.append('Autosave; prefer named source')
        if re.search(r'\btest\b|baseline|fr005|isolation|era fix',p.stem,re.I):notes.append('Test/repair variant; review separately')
        if not calibers:notes.append('No cannon component found')
        if not counts['engine'] or not counts['transmission'] or not counts['track']:notes.append('Mobility components incomplete/unverified')
        if isinstance(mass,(int,float)) and mass>100000:notes.append('Over 100 t; balance outlier')
        if h['creationDate'][:4]>'2026' and h['creationDate']!='9999.12.31':notes.append('Future authored date; still native final-era range')
        row=dict(Faction=faction,File=p.name,Name=h['name'],Date=h['creationDate'],Era=era,
            Cost=h.get('cost'),MassTonnes=round(mass/1000,3) if isinstance(mass,(int,float)) else '',
            NativeMassClass=mass_class,CalibersMm='/'.join(str(v) for v in calibers if v is not None),
            CrewSeats=counts['crewSeat'],InPinnedPool=sha in pool_hashes,Notes='; '.join(notes),
            ImportedToLibrary=(game/'BepInEx/config/SprocketExpedition/community-blueprints'/(sha+'.blueprint')).exists(),
            Hash=sha,CanonicalJsonHash=canon,Source=relative,Bytes=len(raw))
        rows.append(row)
        if era==eras[-1]['name']:modern.append(row)
    for group_rows in (rows,modern):
        groups=collections.defaultdict(list)
        for row in group_rows:groups[row['CanonicalJsonHash']].append(row)
        for group in groups.values():
            sources=' | '.join(r['Source'] for r in group)
            for row in group:row['EquivalentSources']=sources
    for filename,items in [('all-faction-vehicles.csv',rows),('coldwar-candidates.csv',modern)]:
        with (out/filename).open('w',encoding='utf-8-sig',newline='') as f:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(items)
    summary=dict(ModernHeaderFiles=len(rows),LegacyFiles=len(legacy),ComponentFiles=len(parts),Errors=errors,
        FilesPerFaction=dict(collections.Counter(r['Faction'] for r in rows)),EraCounts=dict(collections.Counter(r['Era'] for r in rows)),
        ColdwarFiles=len(modern),ColdwarExactIdentities=len({r['Hash'] for r in modern}),
        ColdwarEquivalentJsonDesigns=len({r['CanonicalJsonHash'] for r in modern}),
        PinnedColdwar=sum(t['Era']==eras[-1]['name'] for t in catalogue['Tanks']),NativeEraSources=[dict(Name=e['name'],Start=e['start'],Playable=e.get('playable'),MediumKg=e['mediumVehicleMass'],HeavyKg=e['heavyVehicleMass']) for e in eras],
        Legacy=legacy,Components=parts,Catalogue=catalogue['Definition']['Version'])
    (out/'audit.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# Coldwar-kandidaten uit je factions — 25 oktober 2026','',
        f"Gelezen: **{len(rows)}** voertuigbestanden met modern headerformaat, **{len(legacy)}** legacy-voertuigbestanden en **{len(parts)}** bestand zonder voertuigheader. De scan omvat alle geneste factions, dus ook de dubbele map `Factions/Factions`.", '',
        f"**{len(modern)} bestanden** vallen op hun opgeslagen datum binnen de laatste native era. Dat zijn **{summary['ColdwarExactIdentities']} exacte bestandsversies** of **{summary['ColdwarEquivalentJsonDesigns']} verschillende JSON-inhouden** na samenvoegen van opmaakverschillen. Dit zijn nog geen evenveel tankfamilies: Oplot- en T-80-varianten, tests en autosaves staan ertussen.", '',
        'Era komt uit de huidige geïnstalleerde `StreamingAssets/Eras`-definities. Coldwar start daar op **'+eras[-1]['start']+'** en is open-ended. `9999.12.31` is de native eind-era-sentinel, die ook voorkomt op de reeds native geclassificeerde Oplot in de gepinde pool. Datums worden nergens aangepast. Dit is een offline inventaris; laden, rijden en schieten zijn niet live getest.', '',
        'Gewicht en kosten zijn opgeslagen headerwaarden; kalibers zijn uit cannon-componenten gelezen. De native massaklasse is een gewichtsindeling uit dezelfde era-JSON, geen uitspraak over tactische rol of bepantsering.', '',
        '| Faction | Naam | Kosten | Ton | Kalibers mm | Opmerking |', '|---|---|---:|---:|---|---|']
    for r in sorted(modern,key=lambda r:(r['Faction'],r['Name'],r['Source'])):
        note=r['Notes'] or ('Exact in huidige pool' if r['InPinnedPool'] else 'Nieuwe kandidaat; runtimecontrole nodig')
        lines.append(f"| {r['Faction']} | {r['Name'].replace('|','/')} | {r['Cost']:,.0f} | {r['MassTonnes']} | {r['CalibersMm']} | {note} |")
    suggestions=[
        ('Nederland/Blueprints/Vehicles/T-80BVM.blueprint','MBT'),
        ('Nederland/Blueprints/Vehicles/T-80B v2.blueprint','Goedkopere MBT-variant'),
        ('Nederland/Blueprints/Vehicles/Leopard 2a6-AMAP.blueprint','MBT'),
        ('Nederland/Blueprints/Vehicles/T-84 Oplot-S.blueprint','MBT-variant'),
        ('Nederland/Blueprints/Vehicles/C42 Falcon-S.blueprint','Middelzware tank / 85 mm vuursteun'),
        ('Nederland/Blueprints/Vehicles/VRRC-155.blueprint','Mobiele 155 mm vuursteun (voorstel)'),
        ('Nederland/Blueprints/Vehicles/PzH6000.blueprint','Zware 130 mm vuursteun (voorstel)'),
        ('Nederland/Blueprints/Vehicles/Variant13.blueprint','Lichte experimentele steun (35/149 mm)'),
        ('downloads/Blueprints/Vehicles/(LDN-4) LSTPV-1-1 Var.D2 (Stormgaupe Modell 4).blueprint','Lichte multifunctionele vuursteun (152/20 mm)'),
    ]
    selection=[];lines += ['', '## Voorstel voor de eerste aanvulling', '',
        'Nieuwe kandidaten uit de oorspronkelijke shortlist met spreiding in gewicht, prijs en bewapening. Al geïmporteerde, verplaatste of ontbrekende versies worden overgeslagen. Dit zijn rolvoorstellen op basis van naam en opgeslagen componenten; componentdefinities bewijzen niet dat elk kanon daadwerkelijk gemonteerd of operationeel is. De importlijst is alleen voorbereid.', '',
        '| Naam | Rolvoorstel | Ton | Opgeslagen kosten |','|---|---|---:|---:|']
    for source,role in suggestions:
        row=next((r for r in modern if pathlib.PureWindowsPath(r['Source'])==pathlib.PureWindowsPath(source)),None)
        if row is None or row['InPinnedPool'] or row['ImportedToLibrary']: continue
        assert row['CalibersMm'] and 'Test/repair' not in row['Notes']
        selection.append(dict(hash=row['Hash'],name=row['Name'],date=row['Date'],cost=row['Cost'],bytes=row['Bytes'],
            sources=[row['Source']],proposedRole=role))
        lines.append(f"| {row['Name']} | {role} | {row['MassTonnes']} | {row['Cost']:,.0f} |")
    assert len({r['hash'] for r in selection})==len(selection)
    (out/'proposed-import.json').write_text(json.dumps(dict(Status='PreparedOnlyNotImported',SourceDirectory=str(root),
        NativeEra=eras[-1]['name'],entries=selection),ensure_ascii=False,indent=2),encoding='utf-8')
    lines += ['', '## Moderne namen met een eerdere opgeslagen era','',
        'Deze ontwerpen worden niet op hun naam naar Coldwar verplaatst. Selecteer en bewaar ze in het spel in de gewenste native era voordat ze als Coldwar-kandidaat kunnen worden beoordeeld.', '',
        '| Faction | Naam | Datum | Era uit gegevens |','|---|---|---|---|']
    for r in rows:
        if r['Era']!=eras[-1]['name'] and re.search(r't[- ]?80|t[- ]?84|oplot|leopard|vrrc|pzh',r['Name'],re.I):
            lines.append(f"| {r['Faction']} | {r['Name']} | {r['Date']} | {r['Era']} |")
    lines += ['', '## Oud formaat / onderdelen', '',
        'Centurion en T95 in Default (ook onder Factions/Default) zijn legacy-voertuigen zonder modern datum-/kostenheader. Zij zijn geen rechtstreeks geverifieerde Coldwar-import. `downloads/t62m.blueprint` is een freeform-mesh/onderdeel zonder voertuigheader, ondanks de bestandsnaam.', '',
        'De CSV-bestanden bevatten alle bronnen, hashes, equivalenten en reviewlabels. Er is niets geïmporteerd, overschreven of aan je spawnpool toegevoegd; persoonlijke originelen en bestaande saves zijn intact. Alle analyse bleef lokaal.']
    (out/'coldwar-factions.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k not in ('Legacy','Components','NativeEraSources')},ensure_ascii=True,indent=2))

if __name__=='__main__':main()
