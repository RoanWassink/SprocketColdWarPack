"""Generate the review inventory from package parts plus code-routed native parts."""
from pathlib import Path
import json

ROOT=Path(__file__).resolve().parents[1]
PARTS=ROOT/'package/Sprocket_Data/StreamingAssets/Parts'
ROWS=[]


def add(part, asset, paint, fixed, reason, source):
    ROWS.append(dict(part=part, asset=asset, paint=paint, fixed=fixed, reason=reason, source=source))


for file in sorted(PARTS.glob('*.json')):
    part=json.loads(file.read_text())['name']
    source=file.relative_to(ROOT).as_posix()
    if part=='irSearchlight':
        add(part,'ir-searchlight.obj',['housing'],['lens'],'Behuizing in exterior paint; IR-filterglas vast.',source)
    elif part=='laserBoxSight':
        add(part,'box-sight.obj',['paint'],['steel','dark','lens'],'Bestaande 0.3.6-verdeling behouden: kast/klep/lensranden camo; donker blankstaal/beslag, donkere details en optiek vast.',source)
    elif part=='thermalSightLens':
        add(part,'LensAssets procedural',[''],['glass','nativeRim'],'Geen behuizing: front/achtervlak zijn glas; nativeRim is de donkere optische bevelrand, geen structureel frame.',source)
        ROWS[-1]['paint']=[]
    else:
        assert part.startswith('thermalSight'),part
        add(part,'thermal-head-vertical.obj' if part=='thermalSightVertical' else 'thermal-head.obj',['Paint'],['Dark','Lens'],'Behuizing camo; donkere apertuurranden en IR-lens vast. Ook legacy-profielonderdelen gebruiken dit model.',source)

native={
    'sightConsole':('fcs-console.obj',['Housing'],['Glass (lege submesh)'],'Behuizing camo; de gereserveerde optische submesh en eventueel scherm blijven vast.'),
    't72StyleSight':('t72-style-sight.obj',['Housing'],['Glass','AmberGlass'],'Behuizing inclusief montageframe camo; beide optische materialen vast.'),
    'laserRangefinderSight':('lrf-concept.obj',['Paint','PaintLight','Mount'],['Glass'],'Alle kastdelen en montagepoten zijn structureel; alle glasvlakken vast.'),
    'automaticLaserRangefinder':('automatic-lrf.obj',['Paint'],['Integrated rangefinder glass'],'Kast, voet en vensterrand camo; los geïntegreerd optisch vlak vast.'),
    'opticalCoincidenceRangefinder':('optical-rangefinder.obj',['Paint','Tube'],['Glass'],'Kast, structurele dwarsbuis en eindhuizen camo; optische vensters vast.'),
    'clearWindowPane':('LensAssets procedural',[],['glass'],'Frameloze heldere ruit, uitsluitend glas.'),
    'tintedWindowPane':('LensAssets procedural',[],['glass'],'Frameloze getinte ruit, uitsluitend glas.'),
    'yellowWindowPane':('LensAssets procedural',[],['glass'],'Frameloze gele ruit, uitsluitend glas.')
}
game=Path('C:/Program Files (x86)/Steam/steamapps/common/Sprocket/Sprocket_Data/StreamingAssets/Parts')
code='\n'.join(p.read_text() for p in (ROOT/'src').glob('*.cs'))
for part,(asset,paint,fixed,reason) in native.items():
    assert part in code,part+' not routed by source'
    path=game/(part+'Part.json')
    assert path.exists(),path
    add(part,asset,paint,fixed,reason,'Native part (alleen gelezen); routing in src/')
assets={p.name for p in (ROOT/'package/BepInEx/plugins/SprocketThermalSight/assets').glob('*.obj')}
assert assets=={r['asset'] for r in ROWS if r['asset'].endswith('.obj')}
(ROOT/'uv_tools/camo-surfaces.json').write_text(json.dumps(ROWS,indent=2)+'\n')
lines=['# Camo-audit Thermal Sight 0.3.7','',
       'Bronnen: alle 10 package-Parts plus acht via de code vervangen native Parts. Alle negen OBJ-bestanden zijn hieronder afgedekt.',
       'Native game-Parts en andere mod-bronnen zijn uitsluitend gelezen; geen nieuwe onderdelen of geometrie.', '',
       '| Onderdeel | Mesh | Geverfd | Vast | Besluit / reden |','|---|---|---|---|---|']
for r in ROWS:
    lines.append('| '+' | '.join([r['part'],r['asset'],', '.join(r['paint']) or '—',', '.join(r['fixed']),r['reason']])+' |')
lines+=['','Ook de geïntegreerde optische vlakken (`LensAssets.IntegratedWindow`) en het eventuele console-scherm blijven vaste materialen. Het schermpad is momenteel niet aangeroepen; de automatic LRF gebruikt alleen het glas-pad.',
        '', '## Materiaalroute en fallback','',
        '`src/ExteriorPaint.cs` bevat de enige factory/cache/ownership-lijst: `NewMaterial`, flags `None`, slot `Exterior`, blauwe DPM-mask. Na SetMaterials volgen MarkMaterialModified en MaterialProcessor.Process. Alle createdPaints worden bij plugin-unload vernietigd, ook na cache-evictie; factory en vier textures eveneens.',
        'Ontbreken vehicle, transform of material-controller, dan retourneert Get direct het vaste oude console/T72-materiaal of het bestaande native materiaal (thermal, rangefinder, IR, box); geen exception voor ontbrekende context.',
        '', '## UV-contract en grenzen','',
        'De vijf oorspronkelijke OBJs staan in `uv_tools/original/`; `py -I uv_tools/add_uv.py` reproduceert de package-bestanden. `--check` vergelijkt alle bytes, bron-/uitvoerhashes, geometrie, bounds en usemtl-volgorde. Per driehoek: grootste normaalcomponent, cyclische projectiebasis, teken gecorrigeerd voor winding, 1 UV-eenheid per meter; face-normaal toegevoegd. Ook de vaste submeshes hebben volledige v/vt/vn-tuples.',
        'Box-projectie geeft naden tussen projectierichtingen en kan schuine vlakken tot factor √3 rekken. Op de ronde optical-rangefinder-buis zijn projectienaden mogelijk. Native part-scaling kan de zichtbare patroondichtheid veranderen; dit blijft live te beoordelen. Console/T72/IR/box behouden hun bestaande UVs.',
        'nativeRim blijft een donkere optische rand (vast), het box-sight steel-materiaal blijft donker blankstaal/beslag volgens het bestaande 0.3.6-besluit. Geen glas wordt geverfd.',
        '', '## Verificatie','',
        'Machineleesbare onderdelenlijst: `uv_tools/camo-surfaces.json`. Runtime-contract voor de vijf UV-meshes: `src/PaintedObj.cs` (`CamoSurfaces`). Pure reader-/rejectie-/UV-tests: `src/tests/CamoChecks.cs`. Scope, patchlijst en overige materiaaltoewijzingen: `uv_tools/verify_delivery.py`.',
        'Geen nieuwe Harmony-patches. Rangefinder-, aim-, ammo- en console-linkgedrag blijven bytegelijk aan src-0.3.6; alleen visual assets en versie zijn gewijzigd.',
        'Niet geïnstalleerd; native rendering, dubbelezijdigheid, camo-verversing en save/reload zijn nog niet in de game gevalideerd. Zie TEST-GUIDE-NL.md.']
(ROOT/'CAMO-ALL-0.3.7.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(f'{len(ROWS)} onderdelen; {len(assets)} OBJ-assets; inventaris geschreven.')
