# Telescopic Mast I/O API, contract v1 (ontwerp 2026-11-05)

Doel: de mast biedt het I/O-paneel als API aan, zodat iedere bouwer erop kan aansluiten. De eerste afnemers zijn Visual Effects (een RGB-bulb als output, met de input remmen) en Emitter (instellingen per part).
Status: gebouwd in mast 0.7.0. Vanaf 0.8.0 staat in §3 hoe de opslag werkt; de signatures zijn ongewijzigd. Plugin-GUID van de mast: `sprocket.telescopicmast`.

## 1. Vorm: een facade met alleen BCL- en Unity-typen, gevonden via reflectie
- De afnemer refereert de mast-DLL **niet**. Hij zet `[BepInDependency("sprocket.telescopicmast", BepInDependency.DependencyFlags.SoftDependency)]` op zijn plugin; dat regelt alleen de laadvolgorde.
- De afnemer zoekt de facade op: `IL2CPPChainloader.Instance.Plugins.TryGetValue(guid, out var info)` → `info.Instance.GetType().Assembly.GetType("SprocketTelescopicMast.Io.IoApi")`.
  - Ontbreekt de mast, of is `ApiVersion` kleiner dan 1, dan doet de afnemer niets.
- In de signatures staan alleen `string`, `bool`, `int`, `Func<>`/`Action<>` en `UnityEngine.GameObject`. Die typen deelt elke mod al, dus een delegate binden met `Delegate.CreateDelegate` werkt zonder mast-typen.
  - De mast levert `IoApiClient.cs` mee: een shim van ongeveer 80 regels die de afnemer kopieert en die dit binden doet.
- Waarom niet de bestaande `IoRegistry`/`IInputType`: dat zijn interne typen die mogen veranderen. De facade is het stabiele contract, en daar komt alleen iets bij.
- **Risico:** geen. Er komen geen nieuwe Harmony-hooks bij; de API is puur managed code. Een callback van een afnemer die een exception gooit, wordt gevangen en geeft één logregel. Die input of output staat daarna tot de volgende spawn van het voertuig uit.

## 2. Methodes (namespace `SprocketTelescopicMast.Io`, `public static class IoApi`)
```csharp
public const int ApiVersion = 1;   // 2 alleen bij een breaking change; nieuwe methodes houden 1

// Input op voertuigniveau. Wordt in de main thread één keer per I/O-tick gelezen.
// vehicle = de root-GameObject van het voertuig.
bool RegisterLevelInput(string id, string displayName, string group, Func<GameObject, bool> read);
// Event-input: het aantal events sinds de vorige read (bijvoorbeeld schoten).
bool RegisterEventInput(string id, string displayName, string group, Func<GameObject, int> readEvents);

// Output op een part. componentId = de ComponentID van jouw part; de mast zoekt die parts op het voertuig.
// apply(part, aan): de mast roept dit alleen aan bij een wijziging, en na een spawn of load één keer met de beginstand.
bool RegisterOutput(string id, string displayName, string componentId,
                    Action<GameObject, bool> apply,
                    Func<GameObject, string?> loadRules,      // jouw opgeslagen regel-JSON van dit part (null = geen)
                    Action<GameObject, string> saveRules,     // de mast geeft de nieuwe JSON; jij bewaart hem bij je part
                    string onLabel = "On", string offLabel = "Off");
```
- `id`: letters, cijfers, `.`, `_` en `-`; zet er je mod voor, bijvoorbeeld `vfx.light`. De returnwaarde is `false` bij een dubbele of ongeldige id; dat geeft één logregel en geen exception.
- Registreren gebeurt in de `Load()` van je plugin. Alle callbacks komen uit de main thread.
- Een regel stuurt de output met de bestaande commando's: Open, Close, Toggle, Pulse en Follow. Bij een output van buiten krijgen Open en Close de labels `onLabel` en `offLabel`. In v1 is de waarde alleen aan of uit; kleur en helderheid stel je in op je eigen part. Een `float`-output kan later als `RegisterValueOutput` komen, zonder breaking change.

## 3. Opslag en een ontbrekende mod (vanaf 0.8.0)
- De signatures blijven gelijk. Voor de afnemer is de string van `loadRules`/`saveRules` opaak: bewaar hem ongewijzigd bij je part-data.
  - De mast schrijft niets in andermans part-data, en er komt geen nieuwe hook.
- **Inhoud:** elk API-part bewaart een volledige kopie van de flow van het voertuig: `{"partId":"<GUID>","flow":{"v":1,"rev":<int>,"nodes":[…],"wires":[…]}}`.
  - `partId` is de vaste `ioPartId` van dat part, en het output-blok verwijst ernaar.
- **Lezen en schrijven:**
  - De mast roept `loadRules` aan als een part gevonden wordt of opnieuw spawnt, vanuit de eigen host en nooit binnen een native hook.
  - Bij elke wijziging van de flow krijgt `saveRules` de nieuwe string. Een string die gelijk is aan de vorige, wordt niet opnieuw aangeboden.
- **Laden:** de hoogste `rev` van alle kopieën (scharnieren, mastparts en API-parts) wint. Bij een gelijke `rev` geldt een vaste volgorde. Verschillen de kopieën, dan komt er één waarschuwing per voertuig.
- **Een gekopieerd part met een dubbele id** krijgt een nieuwe GUID. Het verschijnt als een part dat nog niet geplaatst is, met één waarschuwing.
- **Oude regels** (`hingeIoV1`) worden bij het eerste laden omgezet naar de flow. De oude data blijft staan, zodat terug naar 0.7 kan.
- **Wat er gebeurt als iets ontbreekt:**
  - **De mast ontbreekt:** de afnemer laat de string ongemoeid, en het part reageert dan alleen niet.
  - **De afnemer ontbreekt:** zijn part bestaat dan niet. Zijn blok blijft in de flow staan als "missing part" en wordt niet uitgevoerd.
  - **Een input van een ontbrekende mod:** het blok blijft letterlijk bewaard en geeft geen signaal.
- `apply(part, aan)` loopt één keer voor de beginstand, en daarna alleen bij een wijziging. Een callback die een fout gooit, staat voor dat voertuig uit tot de volgende spawn.

## 4. Input "remmen"
- Dit wordt een ingebouwde input van de mast, `vehicle.braking` (Level, groep "Vehicle"), omdat het een voertuigsignaal is dat elke afnemer kan gebruiken.
- **Bron:** alleen lezen, elke tick. Er komt geen patch voor en er wordt niets in native state geschreven. Kandidaten in de interop (`Sprocket.Vehicles.Powertrains`):
  - het veld `action`/`lastAction` (`DriveAction`, bijvoorbeeld de Brake-stand van `UpdateBrake`);
  - `leftBrakeFraction`/`rightBrakeFraction` van `ClutchBraking`.
- Zo gebouwd (zie README, `vehicle.braking`): de enumwaarde `Braking` is 7; bij `ClutchBraking` moeten beide remfracties actief en symmetrisch zijn, zodat sturen met één rem niet als remmen telt; een layout zonder bruikbaar veld laat de input uit en logt één regel.

## 6. v1.1: opslag per part, beheerde flow-opslag en iconen (gebouwd in mast 0.9.3)
Gevraagd door Visual Effects voor de tanklamp, zonder nieuwe hooks. Alles is additief: `ApiVersion` blijft 1, en er komt `public const int ApiRevision = 1` bij (oudere masts hebben dat veld niet, dus 0). Een afnemer bindt de nieuwe methodes via reflectie. Ontbreken ze, dan werkt hij zonder opslag en zonder icoon. Ze zitten in mast 0.9.3.

```csharp
// M1: korte data per part, door de mast bewaard in de eigen blueprint-state van dat part (één sleutel "ioPartDataV1" met een JSON-object).
bool SetPartData(GameObject part, string key, string? value);   // null = weghalen
string? GetPartData(GameObject part, string key);               // null = niet gezet of nog niet geladen
// Beheerde output: de mast bewaart ook de flow-kopie en het ioPartId op het part zelf; geen loadRules/saveRules nodig.
bool RegisterManagedOutput(string id, string displayName, string componentId,
                           Action<GameObject, bool> apply, string onLabel = "On", string offLabel = "Off");
// M3: icoon van een eigen part-definitie, op de bestaande CreateCard-postfix van de mast.
bool RegisterPartIcon(string partGuid, string pngPath);
```

- **`part`:**
  - het GameObject van de gevonden `VehicleComponent`, hetzelfde dat `apply` krijgt;
  - alleen voor parts waarvan het ComponentID via `RegisterOutput` of `RegisterManagedOutput` geregistreerd is. Anders `false` en één waarschuwing.
- **Sleutels:**
  - de vorm is `<prefix>.<naam>`, met de tekens `[a-z0-9._-]` en hoogstens 64 tekens;
  - het prefix is van de eigen mod (`visualeffects.`); `io.` is voor de mast;
  - een waarde is hoogstens 4 KB, en alles samen per part hoogstens 16 KB.
- **Wanneer:**
  - bewaard bij de volgende opslag (de ToBlueprint-postfix) en gelezen in de LoadData-postfix;
  - beschikbaar zodra het part gevonden is, en uiterlijk bij de eerste `apply`;
  - kopiëren of spiegelen in de designer volgt de native blueprintkopie.
- **M2:** één registratie per ComponentID. Drie part-definities die hetzelfde ComponentID (`vfxLamp`) delen, gebruiken één `RegisterManagedOutput`. De editor noemt elk part met zijn eigen partnaam plus een nummer.
- **M3:** `pngPath` is een absoluut pad naar een PNG onder `BepInEx\plugins`, in de huisstijl (256²). Hij wordt geladen bij de eerste card. Per GUID wint de eerste registratie; een dubbele geeft een waarschuwing.
- **M4:** `apply(part, aan)`:
  - loopt op de main thread, alleen in de speelscène (VehicleControlUI, niet gepauzeerd);
  - één keer met de beginstand na een spawn of load, en daarna bij een wijziging.
  - In de designer loopt de flow niet. Nieuw in v1.1: bij de terugkeer naar de designer en bij het vrijgeven van het part volgt één keer `apply(part, false)`, zodat een lamp niet blijft branden. Een preview in de designer regelt de afnemer zelf.
- **Zonder de mast:** geen opslag. De afnemer valt terug op zijn standaardwaarden, en de extra state-sleutel wordt door de game genegeerd.
- **Risico** (laag): de mast schrijft één extra sleutel in de state van een vreemd part. Dat gebeurt met dezelfde merge als bij de eigen parts sinds 0.7.1, en de bestaande entries blijven gelijk. Er komt geen nieuwe hook, en geen werk op de schiet-, treffer- of physicspaden.
- **Hoe het in 0.9.3 gebouwd is:**
  - Een geregistreerd extern ComponentID gaat door de bestaande LoadData-filter, via `IoApiCore.IsRegisteredComponent`, en door de LoadState-backup.
  - `RegisterOutput` met `loadRules`/`saveRules` werkt ongewijzigd en accepteert ook per-part-data.
  - Een beheerde output schrijft `ioFlowV1`, `ioPartId` en `ioPartDataV1` op het part zelf.
  - De naam bij M2 komt uit `IVehiclePartInfoSource.Instance.GetInfo<PartDefinition>(VehicleObject.GUID)`. Dat gebeurt één keer per gevonden part; als het mislukt, wordt `displayName` gebruikt.

## 7. v1.2: instellingen per part in het I/O-paneel (gebouwd in mast 0.9.9)

**Zo gebouwd** (aanvullingen op het ontwerp hieronder):
1. **`int`:**
   - `min` en `max` mogen niet-geheel zijn. De mast klemt op ceil(min)/floor(max), en het bereik moet minstens één geheel getal bevatten.
   - Zonder `step` geldt 1 voor int en 0,1 voor float.
2. **Enum:** `value` en `label` hebben geen eigen lengtegrens; alleen de schemagrens van 4 KB geldt.
3. **v1-afnemer:** een part van een v1-afnemer (`RegisterOutput`) met settings wordt in de flow-opslag "managed". De mast schrijft dan ook `ioFlowV1` en `ioPartId` op het part; de eigen loadRules/saveRules blijven werken.
4. **Defaults worden bevroren.** De mast schrijft het genormaliseerde object (defaults en geklemde waarden) naar `ioPartDataV1` zodra het part in de flow komt, ook zonder bewerking. **Gevolg voor de afnemer:** een latere wijziging van een default in je schema geldt niet voor parts die al opgeslagen zijn. Wil je dat wel, voeg dan zelf een schemaversie toe aan je waarden.
5. **Opslaan en `onChanged`:**
   - een bewerking zonder nieuwe waarde slaat niets op, en er volgt geen `onChanged`;
   - een tweelingbewerking is atomair: past één part niet binnen 16 KB, dan wordt alles geweigerd;
   - een fout in `onChanged` geeft één waarschuwing per mislukte aanroep.
6. **Plaats in het paneel:** de sectie "Settings" staat in de **inspector** van de geselecteerde output-node.
7. **Tweelingen van API-parts:** type `api:<componentId>`. De base is de directe parent of PlaceTarget.

**Ontwerp:**
- **Gevraagd door:** Sprocket Emitter. De instellingen staan in het I/O-paneel van de mast.
- **Additief:** `ApiVersion` blijft 1, en `ApiRevision` wordt **2**. Een afnemer bindt via reflectie. Ontbreekt de methode, of is `ApiRevision < 2`, dan gebruikt hij zijn eigen standaardwaarden.

```csharp
// Toont de velden van schemaJson als sectie "Settings" in het blok van elk part met dit ComponentID,
// en bewaart de waarden als één JSON-object onder dataKey via dezelfde opslag als SetPartData (ioPartDataV1).
bool RegisterPartSettings(string componentId, string dataKey, string schemaJson, Action<GameObject>? onChanged = null);
```

- **Voorwaarden:**
  - `componentId` is al geregistreerd met `RegisterOutput` of `RegisterManagedOutput`;
  - er is één `RegisterPartSettings` per ComponentID;
  - `dataKey` volgt de sleutelregels van §6 (`<eigen prefix>.<naam>`, niet `io.`).
  - Een fout geeft `false` en één waarschuwing met de reden.
- **Schema:** een JSON-array van velden, hoogstens 16 velden en 4 KB.
  - Elk veld heeft `key` (`[a-z0-9_]`, hoogstens 32 tekens, uniek), `label` (hoogstens 40 tekens), `type`, en eventueel `unit` (hoogstens 8 tekens) en `showIf`.
  - `float` en `int` hebben `min`, `max` en `default`, en eventueel `step`;
  - `bool` heeft `default`;
  - `enum` heeft `options` (hoogstens 16 stuks, elk `{value,label}`) en `default` (een bestaande value).
  - `showIf` heeft de vorm `{"key":"<bool- of enum-veld>","value":<waarde>}` en verbergt het veld als die waarde niet geldt. Het verandert de opslag niet.
  - Er zijn geen andere typen en geen vrije tekst. Een onbekend type maakt het hele schema ongeldig.
- **Waarden:**
  - opgeslagen als een JSON-object `{"<key>":<waarde>,…}`, onder `dataKey`;
  - een ontbrekende sleutel krijgt de `default`;
  - getallen worden op `min`/`max` geklemd, en `int` wordt afgerond;
  - een onbekende enum-waarde wordt de `default`;
  - een onbekende sleutel blijft bewaard (vooruit-compatibel).
  - De afnemer leest de waarden met `GetPartData(part, dataKey)` en moet dezelfde defaults en grenzen zelf ook toepassen.
- **Paneel:**
  - In de designer toont het blok van dat part (de output-node) een sectie "Settings", met Kit-velden: een getal met eenheid, een toggle of een keuzelijst.
  - Een wijziging wordt meteen bewaard via de per-part-opslag, en daarna volgt `onChanged(part)` op de main thread, buiten elke hook. Een fout in `onChanged` wordt gevangen; er volgt één waarschuwing en de waarde blijft staan.
  - In Play is de sectie alleen-lezen.
  - Wijzigingen aan instellingen vallen **niet** onder de undo-historie van de flow (die geldt voor blokken en draden). Het paneel zegt dat met een korte hint.
- **Kopiëren, spiegelen en tweelingen:**
  - Een native kopie of spiegeling neemt de waarden mee: het is dezelfde `ioPartDataV1`.
  - Een gespiegeld paar buiten het midden heeft twee blokken en dus twee eigen sets instellingen. Een richting in lokale ruimte is de zaak van de afnemer.
  - Een **samengevoegde tweeling** (0.9.8: dezelfde base, positie en GUID, zonder eigen node) is voor de gebruiker één part. Een wijziging in het blok van de primaire wordt naar **beide** parts geschreven, en `onChanged` volgt voor beide.
  - Een tweeling die nog een eigen bedrade node heeft, houdt zijn eigen instellingen.
- **Risico** (**laag**):
  - er komt geen nieuwe hook en geen werk op de schiet-, treffer- of physicspaden;
  - er komt alleen paneelcode op de Kit, plus een sleutel in de bestaande per-part-opslag, met dezelfde merge als in v1.1;
  - een ongeldig of te groot schema wordt geweigerd voordat er iets getoond wordt.
