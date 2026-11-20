# Changelog

## 0.5.4 — 2026-11-18

- Poort: GameAssembly van 18-11 (buildid 26757044) toegestaan na ICF/RVA-heraudit; hooks ongewijzigd.

## 0.5.3 — 2026-11-16

- Geweigerde kaartorders tonen 2 s een schermvast rood kruis met `Outside battle area` of `Select allies first`; een volgende klik wist de markering. Geen selectie gaat voor buiten het gebied, ook bij Attack. De statusregel blijft bestaan en weigeringen worden altijd gelogd met de actie en afstand tot de grens zonder de marge van 16 m.
- Geslaagde Move en Waypoint tonen 1 s een blauwe doelring naast de route.
- Terrein buiten het gedraaide/geschaalde gevechtsgebied wordt eenmaal per kaartopbouw met factor 0,5 verdonkerd. Een gewijzigd gebied herstart de gespreide opbouw; de grens is nu helder rood en 4 px dik.
- DetailedLogging toont WorldBounds-kandidaten, gekozen wereldmidden, geschaalde maat en y-hoek, kaart/canvas/zoom bij openen en de gedeelde M-toetsstatus met controle een frame later. Alleen de publieke Kit-API blijft in gebruik.
- Pure tests uitgebreid voor afstand, redenprioriteit, markeringstijd en gebiedswissels. Verify bewaakt precies drie ongewijzigde Harmony-patches. Niet geïnstalleerd; live kaartfeedback en de open 0.5.2-bewegingstests staan in TESTPLAN.md.

## 0.5.2 — 2026-10-31

- Fix: een tank die stopte (bij aankomst of na Halt) bleef toch met ongeveer 10 m/s doorrijden richting vijanden. Dat gebeurde in het Expedition-gevecht van de 0.5.1-test. AI Orders zette alleen een HaltTask op de driver, en die houdt de gevechtsbeweging van de commandant niet tegen. Elke halt stuurt nu ook de native Halt-order van de game (`CombatOrder(CombatOrderType.Halt)`); die zet de HaltReason van de commandant.
- De logregel `Order state` toont nu ook `haltReason`.

## 0.5.1 — 2026-10-31

- Fix: de game bevroor zodra er een route op de kaart verscheen (direct na de eerste geslaagde order). Oorzaak: de streepjeslijn van UI Kit 0.1.2 (`dashPx > 0`) kan door float-afronding eindeloos blijven rekenen; in een simulatie gebeurde dat bij ongeveer een kwart van de routes. AI Orders tekent streepjes nu zelf, met een begrensde lus van gewone lijnen (maximaal 200 per lijn). Verify verbiedt Kit-streepjeslijnen.

## 0.5.0 — 2026-10-31

- F9 is nu een niet-modaal HUD-snelmenu (`UiKit.CreateHud`, 340 px) in plaats van een Kit-paneel. Linksonder, 24 px van links en 150 px van onder, boven de eigen Sprocket-HUD; `HideWhenCursorFree = false`, dus geen cursor en geen inputblokkade.
- HUD toont `AI Orders` met `Selected n/m`, de root- en submenuregels met cijfers, paginanavigatie, `[x]` bij gekozen tanks en de laatste orderstatus. Geen bondgenotenlijst; alle labels hebben een outline.
- F9 pollt de bestaande keybind met `WasPressedThisFrame`. Cijfers 1–9/0 worden alleen bij een zichtbare HUD gelezen; 0 sluit in root, 9 opent de kaart en een geslaagde order verbergt de HUD. M sluit de HUD en houdt de cijfers voor kaartselectie. De M-kaart is verder ongewijzigd.
- Nieuwe pure `QuickMenu`-tests voor F9 aan/uit, 0 sluiten, een afgeronde order, M sluiten en de cijferpoort; Verify controleert `CreateHud("quick")`, geen `CreatePanel("menu")`, geen nieuwe inputblokkade en de cursorregels.
- Niet geïnstalleerd; live HUD- en toetsconflicttest blijven nodig.

## 0.4.1 — 2026-10-31

- Fix: in Expedition-gevechten gaf rechtsklik "Move: 0 unit(s)". De overdracht aan Expedition controleerde een vaste hash van de Expedition-DLL. Na elke Expedition-build blokkeerde dat alle orders. Nu wordt de signatuur van `PlayerCommand` gecontroleerd. Ontbreekt die, dan komt er één waarschuwing en gaan de orders gewoon door.
- Een geweigerde order logt nu per tank de reden (geen CommanderAI, CanReceiveOrder=false of handoff).
- In de hinttekst boven de kaart stond "?" in plaats van scheidingstekens; ook de verminkte tekens in de docs zijn hersteld.

## 0.4.0 — 2026-10-31

- Hele M-kaart in één UI Kit-paneel met vaste zijbalk en vullend Canvas; vereist Kit ≥0.1.2 <0.2.0.
- Topo als één statische Image met Kit-clipping; beperkt raster, gevechtsgrens, gestreepte routes, markers, selectiering en outlined labels via Painter.
- Eén camera via CanvasView.PanZoom; klikken met knop/modifiers via CanvasPointer. Center on me en Whole map gebruiken de publieke camera-API.
- IMGUI-kaart, eigen scherm/view-wiskunde, MapInputPolicy en PanelPlacement volledig verwijderd; geen Kit-reflection.
- Map.RedrawFps standaard 12; expliciete invalidatie en DetailedLogging met gemiddelde/maximale volledige drawtijd en Painter-aanroepen elke 5 s.
- Pure tests voor wereld/kaart-omrekening, zoomafhankelijke hit-test, klikbeslissing, rasterdichtheid en timingaggregatie; Verify uitgebreid met negatieve tests.
- F9, gameplay-orders, keybind-ID's en terreinopbouw behouden. Niet geïnstalleerd; live testplan blijft nodig.

## 0.3.0 — 2026-10-31

- Snelmenu (F9) en kaartzijbalk (M) gebruiken Sprocket UI Kit 0.1.1: klikbare knoppen, live orderlijsten en alleen de gamecontext.
- Cijfertoetsen en bestaande submenu's blijven via de Keybinds API werken.
- Kaartinput via het Input System, met y-flip en Kit-schaal; panelen krijgen voorrang op kaartinput.
- De bestaande topokaart, markers, routes en labels blijven IMGUI-tekeningen rechts van het Kit-paneel.
- Cursorbeheer, gameplay-inputblokkering, Esc en pauze/scènewissel verlopen via de Kit.
- Eigen inputpatches, cursorcode en IMGUI-knoppen verwijderd; orderbeleid, GUID en keybind-ID's behouden.
- `Diagnostics.DetailedLogging` aanwezig, standaard uit.
- Verify.ps1, voorbereide Install.ps1, pure regressietests en TESTPLAN.md toegevoegd.
