# Testplan 0.3.4: testpool schuift door na elk gevecht (max 6 stappen)

Voorwaarde: 0.3.4 geïnstalleerd, `[Campaign] TestPool = true`, Sprocket via Steam gestart.

1. **Slot 1 laden.** Load Game → slot 1 (ronde 24, Coldwar). Verwacht: geen foutmelding; de bosskaart toont
   HGS 105-13-1, HGS 105-2-1 Dunkleosteus, HGS 105-3-1 en HGS 105-4-1.
2. **Verliezen.** Speel dit gevecht en verlies het (of laat je tank vernietigen). Verwacht op het resultaatscherm:
   DEFEAT met RETRY; geen beloning. Klik RETRY en daarna Play in de editor (of CHOOSE ANOTHER BATTLE): de kaart toont
   nu de vier volgende testtanks, nog steeds ronde 24.
3. **Terugtrekken na contact.** Start dat gevecht, wacht tot er contact is (een dode, of 60 s gevecht) en kies in het
   pauzemenu Return to Editor. Verwacht: je komt in de editor; Play start ronde 24 met weer de vier tanks daarna.
4. **Afbreken zonder contact.** Start het gevecht en kies binnen 60 s, zonder doden, Return to Editor. Verwacht:
   dezelfde vier tanks blijven staan.
5. **Winnen en slot 2.** Win een gevecht (normaal doorschuiven). Laad daarna slot 2: het laadt zonder fout en toont
   zijn bestaande kaarten (AMX-13-90, bmpt terminator, Cruiser Mk VII Cavalier (A24), E(enphosian)MBT-120).
6. **Afsluiten.** Sluit Sprocket normaal af en geef Opus `BepInEx\LogOutput.log` en
   `BepInEx\config\SprocketExpedition\stats\test-pool-results.csv`.

## Wat Opus daarna leest
- Per gevochten testgevecht één regel `Expedition test pool: offset <oud> -> <nieuw> after <uitkomst>`:
  stap 2 `12 -> 16 after FriendlyForceDestroyed`, stap 3 `16 -> 20 after Retreat`, bij stap 4 géén regel,
  stap 5 `+4 after Victory`.
- `Expedition test pool: battle <ronde> enemies=<namen> outcome=<uitkomst> spawned=<n>/<m> contact=<ja|nee> perTank=<gespawnd>/<contact>|...`
  en dezelfde gegevens in de CSV: de header eindigt op `tank_gespawnd,contact,contact_gevecht`, oude regels zijn
  behouden en een geslaagde stap 4 heeft `contact_gevecht` = `nee`.
- Geen `Expedition test pool results not written`-warning, geen `Snapshot counters are invalid`, geen `Expedition update failed`.
- `[Expedition menu]`/`[Expedition HUD]`-traces alleen als de geïnstalleerde cfg `DetailedLogging = true` heeft
  (een bestaande cfg houdt zijn waarde; alleen een nieuwe cfg krijgt `false`).
