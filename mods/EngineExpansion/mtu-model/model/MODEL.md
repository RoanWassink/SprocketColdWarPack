# MTU-powerpack: OpenSCAD-model (0.3, 2026-11-03)
- **Bron en build**: geometrie in `mtu-lib.scad`, per mesh `mtu-part.scad -D part=… -D lod=…`. Alles opnieuw: `powershell -ExecutionPolicy Bypass -File build.ps1` (export, `stlnorm.py`, `stlcheck.py`, `render.py`). Viewer `view-mtu-pack.scad` (N, d, trans, turbo, place), close-up `view-pair.scad`, contactvellen `contact.py`.
- **Eenheden en assen**: mm. x = opzij, y = langs de krukas met de output aan +y, z = omhoog.
  - Motor op **eenheidsmaat k = 1** (1 L/cil.); de game schaalt met k = d^(1/3).
  - Transmissie in het **native frame** van de Sprocket-meshes (`MTU-MAP.md` §1.5).
- **Naar Unity**: `unity = (x, z, y) · 0,001` en de winding omdraaien (a,b,c → a,c,b), want y en z wisselen is een spiegeling. Zelfde regel als bij de goedgekeurde trunnion.

## Delen (maten in mm, k = 1)
| deel | oorsprong / bereik | driehoeken LOD0 / LOD1 |
|---|---|---|
| `start` (demper, waterpompen, dynamo, tandwielkastdeksel, steunen) | y −160..0 | 1270 / 658 |
| `pair` (carter, 2 losse koppen + deksels onder 90°, spruitstukken, laadluchtbuis, inspuitpomp) | y 0..120, x ±339, z 0..518 | 1440 / 492 |
| `output` (vliegwielhuis, flens r 262 bij y 245, startmotor, oliefilter, steunen) | y 0..245, flens om de krukas z 220 | 1546 / 680 |
| `turbo` (compact, EPP: 2 turbo's + laadluchtkoeler in de V) | outputframe, y −170..102, z ≤ 616 | 1276 / 640 |
| `turbo-outboard` (MB 873-stijl, cfg) | outputframe, x ±608 | 1196 / 544 |
| `tra-input` / `tra-shroud` / `tra-output-l` / `-r` (transversaal) | native frame, bijv. shroud x ±42,5, y ±292,5, z 0..402 | 688 / 488 / 956 / 956 |
| `lon-input` / `lon-shroud` / `lon-output` (Twin) | native frame, shroud x ±200, y 0..85, z 55..550 | 576 / 188 / 544 |
| `twin-bridge` (de code schaalt naar de spleet) | eenheidsblok x ±500, y 0..1000, z 0..1000 | 332 |

- **Assemblage** zoals native `Generate` met de gekloonde definitie: start op 0, paar i op i·120, output en turbo op n·120. Daarna −n·60 centreren en × k.
- **Definitiewaarden**: `cylinderDisplacement` 1,0, `cylinderSegmentLength` 0,120, `outputForwardOffset` 0,245 (flensvlak), `outputHeightOffset` 0,22, `centreOfMassHeight` 0,26.
- **Voorbeeld V12** (n = 6): 1270 + 6·1440 + 1546 + 1276 = **12 732** driehoeken LOD0, en 4 908 in LOD1. Native V12 LOD0: 3004 + 6·340 + 2761 = 7 805.
- **0.4.0: shadow = LOD1; collision = aparte omhullende** (`InteractionMesh`). Elke motor-hull heeft 100 driehoeken en omsluit LOD0 én LOD1. Native krijgt twee LOD's. De hieronder bewaarde 0.3-bronmaten blijven ongewijzigd.
- Transmissie: alleen LOD0, plus shadow = LOD0 (native heeft ook maar één LOD). De collider blijft de native doos.

**Materiaal**: één submesh per motor (`CombineMeshes` voegt alles samen) met het native motormateriaal (`materialGuid` van de V-definitie); de transmissie houdt het native materiaal per part (`inputMaterialGuid` enz.). `color()` alleen in de PNG's (aluminium blok, stalen turbo's, grijsgroene bak).

**Controle** (`logs\stlcheck.txt`: RESULT PASS): elke mesh binnen zijn frame of de native doos, manifold, onder budget. Naden (LOD0 en LOD1): het paarvlak bij y=0 is gelijk aan dat bij y=120, en de start- en outputvlakken liggen erbinnen. Reproduceerbaar: onveranderde delen gaven over drie builds byte-gelijke STL's (bijv. `turbo-lod0` e27f97f4…). De build test de ingebouwde meshdata tegen deze hashes.

## SHA256 (canonieke STL)
start-lod0 e09105b43d918c1a8ef34e69b993f1eb6bfe6ea0d4048cc6ceeb2ddfab5de481 · start-lod1 85f6a6dfb619fc17c61c5009266c1903af36e6075ce50f4dc93099ea37a76018
pair-lod0 e892c9a6908389ac9b9058573ede8e90b5515600a762bcaa002d75cfc8433f1f · pair-lod1 bc571343c4cac934f70ad2a978a71ec2b2046af43324ec7bc7c9098c00aa6f01
output-lod0 c48fe6f2c18aaea0457253cb89fb7e4bc7be6e699d815dd7dd7a2fb6e170839f · output-lod1 08512cfd354cb87cf87de29de18b45458c078d48b4a1d0a704902f2f82fff958
turbo-lod0 e27f97f41b6dec24d84490435b46abb4400da4393fce28dc1562d7446ddcb151 · turbo-lod1 0bcc58aa60c20c30cae743213c8ee739737d2b6ec15dd4e718e44426480caf9c
turbo-outboard-lod0 29404efd856ee00957aa412baae155437ca4c43f9b42994a2ea983357b3a777d · -lod1 0e9e8231410d7677b440ad813641b4d9163f1f1a34bd1cd81afabffc648f886b
tra-input f21108ab11e38490cdd40e0fa2b9fbca618d43c82c40a0a250cf9254bfae5be5 · tra-shroud 3571b772b855beb7afdacfe9c0b90d0b103874edd51a6745a96d85f6699ce399
tra-output-l ae11fcabf4c88b5a530d941c3bc1bf34a0a9940cc508be7dddea9699698aa074 · tra-output-r 7d00d91a5870b2bbc2d3d5787ab952cfee6e6f1622338fa67043c1d8c78d1fd1
lon-input 3141b09cddc620ddd4967f77b9581ba593bd28b664a8581b764da4fa976e5472 · lon-shroud 08455581b41b012b23767b472b08f75770ee177a50effcf5574a2ea794cb6b80
lon-output 4acf5f8f2f5d6e28727d9eda0495deee478a0b9d1befaa2139dd879d2d4da3ad · twin-bridge 38cc9de1e655bd145abe7f199baec089bcccfb1ca9a7bf7cf71e6b752c09e11c

## Integratie 0.4.0
- `build040.ps1`: basisexport (optioneel), conservatieve hulls, canonieke STL, DLL-resources; `render040.py` maakt inspecteerbare viewers en PNGs van de exacte export. `view-mtu040.scad` past LengthFactor=0,9 op de hele motor toe.
- `build_hulls.py` reduceert de exacte OpenSCAD-hull tot 52 convexe vertices en vergroot die conservatief. Per deel 100 triangles; alle LOD0/LOD1-punten liggen erin. Grootste afstand tot de exacte convexe omhullende: start 6,53, pair 3,32, output 7,20, turbo 6,26, outboard 9,79 mm (eenheidsmaat). De vorm vult concaviteiten, zoals `hull()` doet.
- `embed_meshes.py`: 22 STL-bestanden naar Unity `(x,z,y)*.001`, winding omgekeerd, met dubbele hash (STL en payload). `--check` vergelijkt ook de exacte payloadbytes. HookCheck controleert bovendien de resources in de **gebouwde DLL**.
- Brug: in 0.4.0 maakt `Core/PowerpackBridgeGeometry.cs` de layoutafhankelijke vorm (72 driehoeken) uit de echte doos-spleet en splitterpositie. De vaste 0.3-mesh `twin-bridge-lod0.stl` wordt **niet** ingebouwd. `bridge040-sample.scad/.stl` is de exacte uitvoer van die C#-functie voor een testspleet .55 × .50 × .70 m; `parts/view-bridge040.scad` is inspecteerbaar.
- `verify040.py`, `logs/geometry040.txt`, `logs/mesh-manifest.json`, `logs/dimensions040.md`, `ZELFREVIEW-0.4.0.md`: bewijs en actuele getallen; live native materiaal/X-ray/schade/LOD/plaatsing nog te testen.

**Niet in deze uitgave** (verwijzingen hierboven): `contact.py`, `verify040.py`, `ZELFREVIEW*.md`, `shots\`, `preview\` en de logs behalve `logs\mesh-manifest.json`. Het zijn bewijs- en reviewbestanden; de bron, de canonieke STL's (`parts\*.stl`) en de scripts om alles opnieuw te maken staan er wel. De hulpbestanden `parts\bridge040-sample.*`, `parts\view-*-040.scad`, `parts\*-hull.scad` en `parts\*-exact.*` worden door `build_hulls.py` en `render040.py` opnieuw gemaakt.
