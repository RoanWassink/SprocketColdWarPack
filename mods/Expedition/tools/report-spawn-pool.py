"""Read-only native catalogue audit. Local names/metadata only; no blueprint uploads.
Role/family tags are name heuristics for human curation, never gameplay authority.
"""
import argparse, collections, csv, hashlib, json, pathlib, re, unicodedata

ERAS = ['WWI', 'Interwar', 'Earlywar', 'Midwar', 'Latewar', 'Coldwar']
ROLES = ['Light', 'Medium', 'Heavy', 'MBT', 'TD / assault gun', 'Mobile artillery', 'Armoured / support', 'Unknown', 'Possible stationary gun']

def norm(value):
    prefixes={'ИСУ':'ISU','СУ':'SU','КВ':'KV','ИС':'IS','БТ':'BT','Т':'T'}
    value=re.sub(r'(?<!\w)(ИСУ|СУ|КВ|ИС|БТ|Т)(?=\s*[- .]?\d)',
        lambda m:prefixes[m.group(1).upper()],value,flags=re.I)
    return unicodedata.normalize('NFKD', value).encode('ascii', 'ignore').decode().lower()

def rx(pattern, text):
    return re.search(pattern, text, re.I) is not None

def classify(name):
    s = norm(name)
    # Mobile indicators take priority over the word gun/howitzer.
    mobile = rx(r'self[ -]?propelled|spg|gun.carrier|gun.carriage|selbstfahrlafette|\bauf\b|haubitzen\s+au|rz[- ]?25', s)
    stationary = rx(r'\bstationary\b|\btowed\b|\b(?:pak|flak|pstk|ordnance qf)\b|\b(?:armata|houfnice|pansarvarnskanon|infanterikanon|haubica|obice|feldhaubitze)\b|\b(?:canon|cannone|canone)\s+(?:de|da|d\b)|\b(?:at|aa|field)[ -]?gun\b|^\s*\d+(?:[.,]\d+)?\s*(?:mm|cm)\s+(?:(?:at|aa|field|pack|mountain|infantry)\s+)?(?:gun|howitzer|type\s*\d+|[a-z]-?\d+\s+m19)|^\d+\s*k[- ]?\d+', s)
    if stationary and not mobile:
        return 'Possible stationary gun', 'Review', 'Gun-like name; mobility is NOT verified'
    rules = [
        ('Mobile artillery', r'howitzer|haubitz|\bsu[- ]?(?:5|26)\b|\b(?:wespe|hummel|sexton|bishop|grille|m7 priest|priest)\b', 'Name indicates mobile artillery'),
        ('TD / assault gun', r'tank[ -]?destroyer|assault[ -]?gun|\b(?:stug|stuh|jagd\w*|hetzer|marder|nashorn|elefant|ferdinand|archer|achilles|hellcat|jackson|semovente)\b|\b(?:isu|su)[- .]?(?:76|85|100|122|152)(?:[a-z]|\b)|\bm(?:10|18|36)\b|\bfoch\b', 'Role/model-family clue; check actual design'),
        ('MBT', r'\bmbt\b|main[ -]?battle|\b(?:centurion|chieftain|challenger|leopard|abrams|oplot)\b|\bt[- ]?(?:54|55|62|64|72|80|90)\b|\bm(?:48|60)\b', 'Role/model-family clue; check actual design'),
        ('Heavy', r'heavy|schwer|\b(?:tiger|churchill|maus|conqueror|black prince|tog|arl[- ]?44)\b|\bchar\s*(?:2c|b1)\b|\b(?:kv|is)[- .]?[1-8](?:\b|[a-z])|\bt[- ]?(?:29|30|32|35)\b|\be[- ]?100\b', 'Role/model-family clue; check actual design'),
        ('Light', r'light|\bleicht\w*|\b(?:toldi|hotchkiss|amr|amc|stuart|chaffee|locust|luchs|tetrarch|valentine|landsverk|vickers)\b|\bft[- ]?17\b|\bt[- ]?(?:26|37|38|40|50|60|70)\b|\bbt[- ]?[257]\b|\b(?:kh|kht)[- ]?\d+|\b(?:l5|l6|l9|l3)[- /]?\d*|\blt\s*vz|\b(?:35|38)\s*\(?t\)?|\b(?:m3|m5)\s*stuart|\b4tp\b|\bstridsvagn\b|\b(?:p[zs]\.?\s*(?:kpfw\.?)?\s*(?:i|ii))\b', 'Role/model-family clue; check actual design'),
        ('Medium', r'medium|\b(?:sherman|cromwell|comet|crusader|cruiser|grant|lee|panther|pershing|ram|somua|lago)\b|\bm(?:4|13|14|15|26|46|47)\b|\bt[- ]?(?:28|34|44)\b|\bpz[- .]?4\b|\bpz.*\b(?:iii|iv)\b|\bpanzerkampfwagen\s*(?:iii|iv)\b', 'Role/model-family clue; check actual design'),
        ('Armoured / support', r'armou?red[ -]?(?:car|carrier)|\b(?:apc|ifv|halftrack|half-track|sdkfz|ba[- ]?\d+)\b', 'Support/armoured-car name clue'),
    ]
    for role, pattern, reason in rules:
        if rx(pattern, s):
            confidence = 'Explicit' if rx(r'\b(?:heavy|medium|light|mbt|tank.destroyer|assault.gun|self.propelled)\b', s) else 'Inferred'
            return role, confidence, reason
    if mobile:
        return 'TD / assault gun', 'Review', 'Mobile gun named, exact tactical role unknown'
    return 'Unknown', 'Review', 'Name alone does not establish tactical class'

def family(name):
    s = norm(name)
    groups = [
        ('T-80', r'\bt[- ]?80'), ('Leopard 2', r'\bleopard\s*2'), ('Leopard 1', r'\bleopard\s*1'),
        ('Oplot / T-84', r'\boplot\b|\bt[- ]?84\b'),
        ('FT-17', r'\bft[- ]?17\b'), ('T-26 / KHT', r'\b(?:t[- ]?26|kht[- ]?(?:26|130))\b'),
        ('T-34', r'\bt[- ]?34\b'), ('KV', r'\bkv[- .]?[1-8]'), ('IS', r'\bis[- .]?[1-8]'),
        ('Sherman', r'\bsherman\b|\bm4\b'), ('Tiger', r'\btiger\b'), ('Churchill', r'\bchurchill\b'),
        ('Panther', r'\bpanther\b'), ('Pz IV / PZ4', r'\bpz[- .]?4\b|\bpz.*\biv\b'),
        ('Char 2C', r'\bchar\s*2c\b'),
        ('Pz III', r'\bpz.*\biii\b'), ('Stug', r'\bstug\b'), ('Hetzer', r'\bhetzer\b'),
        ('SU-76', r'\bsu[- .]?76'), ('SU-85', r'\bsu[- .]?85'), ('SU-100', r'\bsu[- .]?100'),
        ('SU/ISU-122', r'\b(?:isu|su)[- .]?122'), ('SU/ISU-152', r'\b(?:isu|su)[- .]?152'),
        ('Toldi', r'\btoldi\b'), ('Hotchkiss H35/H39', r'\b(?:hotchkiss|35h|38h|39h)\b'),
        ('Stuart', r'\bstuart\b'), ('Cromwell', r'\bcromwell\b'), ('Centurion', r'\bcenturion\b'),
    ]
    for label, pattern in groups:
        if rx(pattern, s): return label
    s = re.sub(r'\(veteran\)|\([^)]*\)|\bm19\d\d\b', '', s)
    return re.sub(r'\s+', ' ', s).strip() or norm(name)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('slots'); ap.add_argument('output'); args = ap.parse_args()
    root, output = pathlib.Path(args.slots), pathlib.Path(args.output); output.mkdir(parents=True, exist_ok=True)
    paths = [p for p in root.glob('slot-*/catalogues/*.json') if '.pre-encounter' not in str(p)]
    source = max(paths, key=lambda p:p.stat().st_mtime)
    envelope = json.loads(source.read_text(encoding='utf-8-sig'))
    assert hashlib.sha256(envelope['Payload'].encode()).hexdigest() == envelope['Hash'], 'Catalogue hash mismatch'
    data = json.loads(envelope['Payload']); rows=[]
    eligible = {e['Blueprint']['LocalId'] for era in data['Definition']['Eras'] for e in era['Enemies']}
    for tank in data['Tanks']:
        if tank['Id'] not in eligible: continue
        role, confidence, reason = classify(tank['Name'])
        rows.append(dict(Era=tank['Era'], Name=tank['Name'], Role=role, Confidence=confidence,
            Reason=reason, Family=family(tank['Name']), Cost=tank['Price'],
            Veteran=tank['Name'].endswith(' (Veteran)'), Hash=tank['Hash'], Suggested=False))
    assert len({r['Hash'] for r in rows}) == len(rows), 'Duplicate exact identities'
    # A diverse shortlist, NOT a change to the spawn pool. Limit common families
    # before filling gaps; spread native prices within each suggested name role.
    summaries=[]
    for era in ERAS:
        group=[r for r in rows if r['Era']==era]; counts=collections.Counter(r['Role'] for r in group)
        usable=[r for r in group if r['Role']!='Possible stationary gun' and not r['Veteran']]
        picked=[]; families=collections.Counter()
        def take(candidates, count, cap=2):
            candidates=sorted(candidates,key=lambda r:(r['Cost'],r['Hash']))
            # Alternate low/high/middle costs to avoid choosing only costly variants.
            ordered=[]
            while candidates:
                ordered.append(candidates.pop(0))
                if candidates: ordered.append(candidates.pop())
                if candidates: ordered.append(candidates.pop(len(candidates)//2))
            for row in ordered:
                if len(picked)>=25 or count<=0: break
                if row in picked or families[row['Family']]>=cap: continue
                picked.append(row); families[row['Family']]+=1; count-=1
        quotas={'Light':5,'Medium':7,'Heavy':5,'MBT':7 if era=='Coldwar' else 0,'TD / assault gun':5,'Mobile artillery':2,'Armoured / support':1}
        for role, quota in quotas.items(): take([r for r in usable if r['Role']==role],quota)
        take([r for r in usable if r['Role']!='Unknown'],25-len(picked))
        take([r for r in usable if r['Role']=='Unknown'],25-len(picked),cap=1)
        for row in picked: row['Suggested']=True
        summaries.append(dict(Era=era, Total=len(group), UniqueNames=len({norm(r['Name']) for r in group}),
            ApproxFamilies=len({r['Family'] for r in usable}), NameRoles=dict(counts),
            Veterans=sum(r['Veteran'] for r in group), Suggested=len(picked),
            SuggestedFamilies=len({r['Family'] for r in picked})))
    rows.sort(key=lambda r:(ERAS.index(r['Era']),ROLES.index(r['Role']),r['Family'],r['Cost'],r['Name']))
    with (output/'tank-inventory.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    (output/'summary.json').write_text(json.dumps(dict(Catalogue=data['Definition']['Version'],Source=str(source),Summaries=summaries),indent=2),encoding='utf-8')
    lines=['# Expedition tankpool — 25 oktober 2026','',
        'Dit is de nieuwste opgeslagen **native spawncatalogus**, met era en prijs zoals het spel die heeft toegekend. Er zijn geen blueprintdatums aangepast. Unieke bestanden zijn op inhoudshash geteld. Bestaande saves kunnen een oudere vastgezette pool gebruiken.',
        '', '**Klassen zijn een voorlopige indeling op naam.** Explicit = rolwoord in de naam; Inferred = aanwijzing uit model/familienaam; Review = nakijken. Een hoge prijs bewijst geen zware tank. Massa, pantser, mobiliteit en bewapening zijn hier niet gemeten. Ook een bekende naam bewijst niet dat een communitymodel die eigenschappen heeft.', '',
        '| Era | Designs | Unieke namen | Licht | Middel | Zwaar | MBT | TD/assault | Mob. art. | Support | Onbekend | Mogelijk statisch |',
        '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for s in summaries:
        c=s['NameRoles']; lines.append('| '+s['Era']+' | '+str(s['Total'])+' | '+str(s['UniqueNames'])+' | '+' | '.join(str(c.get(role,0)) for role in ROLES)+' |')
    lines += ['',f"Totaal: **{len(rows)}** exacte designs. Bron: `{data['Definition']['Version']}`.", '',
        'De kolom mogelijk statisch bevat ontwerpen die nu WEL in de pool zitten maar op naam een kanon lijken. Dit zijn kandidaten voor handmatige controle, geen bewezen defecte tanks. Ze zijn voor de shortlist niet meegeteld. Namen als FT-17, T-26 en Sherman hebben veel varianten; meer bestanden betekenen dus niet automatisch meer speelvariatie.', '',
        '## Waar ontwerpen of controle het meeste oplevert','',
        'Doelverdeling voor ongeveer 25 ontwerpen: 5 lichte/verkenners, 7 middelzware, 4 zware, 5 tankjagers/assault guns, 2 mobiele artillerie en 2 experimentele/supportrollen. Pas dit per era aan: Coldwar mag een groter MBT-aandeel hebben. Dit zijn spelontwerpdoelen, geen historische quota.', '']
    for s in summaries:
        c=s['NameRoles']; gaps=[]
        targets=[('Light',5),('MBT',10),('TD / assault gun',5),('Mobile artillery',2),('Armoured / support',3)] if s['Era']=='Coldwar' else [('Light',5),('Medium',7),('Heavy',4),('TD / assault gun',5),('Mobile artillery',2)]
        for role, target in targets:
            if c.get(role,0)<target: gaps.append(f"{role}: {c.get(role,0)} naamkandidaten, doel {target}")
        text='; '.join(gaps) if gaps else 'geen duidelijk numeriek gat in de hoofdrollen op basis van namen'
        lines.append(f"- **{s['Era']}**: {text}. Onbekend: {c.get('Unknown',0)}; eerst nakijken voordat je concludeert dat een rol ontbreekt.")
    lines += ['', '## Voorstel voor een gevarieerde shortlist', '',
        'Onderstaande selectie is alleen een werkvoorstel; de modpool is niet aangepast. Maximaal twee varianten per herkenbare familie, spreiding over naamrollen en native prijzen. Ontwerpen met onbekende rol blijven duidelijk gemarkeerd. Niet iedere era heeft 25 passende kandidaten.', '']
    for era in ERAS:
        selected=[r for r in rows if r['Era']==era and r['Suggested']]
        lines += [f'### {era} — {len(selected)} voorstellen','', '| Naam | Voorlopige rol | Zekerheid | Native kosten |','|---|---|---|---:|']
        for row in selected:
            name=row['Name'].replace('|','/')
            lines.append(f"| {name} | {row['Role']} | {row['Confidence']} | {row['Cost']:,.0f} |")
        lines.append('')
    lines += ['## Alle ontwerpen', '', 'Het bijgevoegde `tank-inventory.csv` bevat ieder design met era, naam, rol, onderbouwing, zekerheid, familie, prijs, veteran-status en shortlistmarkering. Filter eerst op Unknown en Possible stationary gun. Era komt uitsluitend uit de native catalogus; de naam wordt daarvoor niet gebruikt.', '', 'Deze audit leest alleen opgeslagen metadata. Geen gamecode, logs, saves of persoonlijke tanks zijn naar een externe dienst verstuurd.']
    (output/'tankpool-overzicht.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(summaries,ensure_ascii=True,indent=2))

if __name__=='__main__': main()
