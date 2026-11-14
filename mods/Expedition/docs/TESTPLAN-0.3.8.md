# Testplan 0.3.8: Meer verrassing (max 6 stappen)

Vooraf: `artifacts\HASHES.txt` noemt de twee DLL-hashes, de MusicManifest-sha en de
installatieregel. Installeer alleen met Sprocket gesloten en start daarna via Steam.
Lees alles in `BepInEx\LogOutput.log`.

1. **Bestaande runs.** Laad slot 1 en slot 2 (schema 6). Controleer dat de offers,
   maps, formaties, beloningen en boss dezelfde zijn als vóór 0.3.8 en dat er geen
   gele special verschijnt. Lees geen `Expedition boss character`-regel bij een
   gewone ronde.
2. **Nieuwe run / specials.** Start een nieuwe gewone run (schema 7). Speel tot een
   kaart met chip SPECIAL en titel Rat Plague, Steel Wall of Crossfire. Controleer
   de gele beloning en dat de kaart geen Dangerous-only ontwerp op Cautious/Standard
   toont. Lees `Expedition dangerous-only: round <r> uses '<naam>'` alleen bij
   Dangerous.
3. **Ambush.** Zoek in de spawnlog `formation=<Ambush>` en
   `Expedition <map> formation roles: ... flank=<n> ...`. Zonder flankrollen moet de
   helft van de groep op de zijbaan staan; met flankrollen op 3/9 uur. Herhaal een
   ronde niet handmatig: dezelfde seed/ronde/roll geeft dezelfde formatie.
4. **Boss-escortes.** Bij een boss-ronde: Fortress = compacte box (Defensive),
   Hunter = lichte flankers (Ambush), Battery = support-diepte (CombinedArms),
   Veteran = ongewijzigd. Lees `Expedition <map> front-arc APPLIED: ... formation=...`.
5. **Boss-gevecht.** Lees één regel `Expedition boss character: <naam> -> <gedrag>`.
   Fortress: houdt positie, draait de romp en vuurt; geen opmars (max. 20 m zijstap;
   na 90 s zonder contact één langzame stap). Hunter: jaagt op de speler, escorte
   flankeert. Battery: vuurt op afstand en trekt zich terug zodra je binnen de helft
   van de beginafstand komt, alleen zijwaarts.
6. **Herstel en UI.** Sluit en hervat de run: dezelfde pinned offers en dezelfde
   special/boss-kaart. Controleer dat de boss-kaart onder de naam respectievelijk
   "Dug in — you must attack", "Hunts you with flankers" of "Long-range battery"
   toont en dat de rest van het menu en de HUD onveranderd zijn.
