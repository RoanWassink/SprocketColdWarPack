# Sprocket Thermal Sight

Versie 0.4.0. Auteur: Nero. Plugin-GUID en Harmony-ID: `sprocket.thermalsight`; configuratie: `BepInEx\config\sprocket.thermalsight.cfg`.

Nachtzicht en afstandsmeting voor gunner-sights in Sprocket: sensorprofielen (thermal, image intensifier, active IR) uit `thermal-models.json`, een warmtemodel per voertuig, een IR-zoeklicht, de laser box sight en handmatige/automatische laser-afstandsmeting. Zie `CHANGELOG.md` voor wat er per versie bij kwam en `TEST-GUIDE-NL.md` voor het testen in de game.

Afhankelijkheden: Sprocket Keybinds (`sprocket.keybinds`, versie 0.1.6 of hoger, vereist). Sprocket Expanded Ballistics (assembly `SprocketExpandedBallistics`) is optioneel: is het geladen, dan leest de afstandsmeting daaruit welk schot geladen is (`RangefinderBallistics.cs`).

## Indeling

| Map | Inhoud |
|---|---|
| `src\` | plugin-bron (`SprocketThermalSight.csproj`) |
| `src\tests\` | testprojecten `Sensor.Tests` (net6.0) en `ConsoleLink.Tests` (net8.0) |
| `package\` | wat de mod in het spel zet, in de indeling van de spelmap: `BepInEx\plugins\SprocketThermalSight\` (`thermal-models.json`, `assets\` met OBJ's en iconen) en `Sprocket_Data\StreamingAssets\Parts\` (tien onderdeel-JSON's). De DLL staat hier niet in: die komt uit de build. |
| `data\` | 8 onderdelen en 16 omschrijvingen uit eerdere pakketten (zie Let op), in de indeling van de spelmap: `Sprocket_Data\StreamingAssets\Parts\` en `...\Localization\en-UK\Parts\` |
| `assets-ir\` | `make_searchlight.py` maakt het IR-zoeklicht (OBJ en icoon) en een kijker (`viewer\index.html`) |
| `box-sight\` | laser box sight: creatorbron in `source\`, `convert_box_sight.py` (OBJ en STL), OpenSCAD-bronnen en `render-box-sight.ps1` (OpenSCAD 2021.01 in `C:\Program Files\OpenSCAD`) |
| `uv_tools\` | `add_uv.py` zet box-geprojecteerde UV's op vijf OBJ's (originelen in `original\`), `camo-surfaces.json` zegt welke vlakken camo volgen |
| `HEAT-MAP.md` | welke native velden het warmtemodel leest |

## Bouwen

Standaard zoekt de csproj de spelmap `C:\Program Files (x86)\Steam\steamapps\common\Sprocket`. Een andere map of Keybinds-DLL geef je op de opdrachtregel mee (de bouw schrijft niets in die map):

```
dotnet build src\SprocketThermalSight.csproj -c Release -p:GameDir=<map met BepInEx\core en BepInEx\interop> -p:KeybindsDll=<pad naar SprocketKeybinds.dll>
```

De build is deterministisch (geen pdb, `PathMap`); uitvoer: `src\bin\Release\net6.0\SprocketThermalSight.dll`.

## Testen

Vanuit deze map. Met een hostprobleem bij het starten (apphost) helpt `UseAppHost=false` in de omgeving en `TMP`/`TEMP` naar een map in de werkruimte:

```
dotnet run --project src\tests\Sensor.Tests.csproj -c Release
dotnet run --project src\tests\ConsoleLink.Tests.csproj -c Release
```

Let op bij `Sensor.Tests`:
- De tests lezen `package\`, `uv_tools\` en `box-sight\` in deze map en draaien `py -I box-sight\convert_box_sight.py` en `py -I uv_tools\add_uv.py --check`; de uitvoer van die scripts moet byte-gelijk zijn aan wat er staat.
- `BoxSightChecks` leest (alleen lezen) `t72StyleSightPart.json` en de andere onderdeel-JSON's uit de spelmap: `SPROCKET_GAME_DIR` of de standaardmap.
- De tests schrijven drie resultaatbestanden in de bron: `PERF-0.4.0.txt`, `uv_tools\UV-CHECK.txt` en `box-sight\CONVERTER-CHECK.txt`. Ze horen niet bij de bron.

## Let op

Behalve de tien onderdelen in `package\` stuurt de code acht onderdelen aan die oorspronkelijk uit eerdere pakketten komen: `t72StyleSight`, `sightConsole`, `laserRangefinderSight`, `automaticLaserRangefinder`, `opticalCoincidenceRangefinder`, `clearWindowPane`, `tintedWindowPane` en `yellowWindowPane`. Hun onderdeel-JSON's en de Engelse omschrijvingen van die acht en van de thermal-onderdelen (24 bestanden) staan onder `data\Sprocket_Data\StreamingAssets\` (`Parts\` en `Localization\en-UK\Parts\`), zodat een schone spelmap volledig is. Ze zijn byte-voor-byte uit de spelmap overgenomen en worden niet door een script gemaakt; de mod voegt er modellen en gedrag aan toe. `package\` en `data\` samen vormen de onderdeel- en tekstbestanden van de uitlevering. `package\` blijft apart omdat de tests de lijst onderdelen daar aan de camo-tabel toetsen.
