# Testplan Ambusher (0.3.3, max 8 stappen)

1. **Voorbereiden.** Sluit Sprocket. Zet in de pluginconfig `[Campaign] TestPool = true`.
   Controleer dat `BepInEx\config\SprocketExpedition\test-pool\` nog `FV-90.blueprint`,
   `Grille 17.blueprint` en `Grille 21.blueprint` bevat (en Wirbelwind/AMX als controle).
   Start daarna de game; de New Game-notitie moet het geaccepteerde poolaantal tonen.
2. **Run starten.** New Game → TestPool-run → eerste battle. De testpool toont per ronde
   drie verschillende tanks op naam; FV-90 en de Grilles komen in de rotatie voor. Kies een
   battle met FV-90, Grille 17 of Grille 21.
3. **Spawnregels lezen.** Lees in `BepInEx\LogOutput.log` vóór het eerste schot:
   `Expedition ambusher: enemy slot <n>; design='<naam>'; source=detected; height=<m> above player spawn; facing=<graden> deg to player.`
   en `Expedition ambusher placement: <k>/<n> design slots assigned the highest validated markers`.
   De `facing` hoort ongeveer 0° te zijn: de marker staat al op het friendly deployment-center.
4. **Profielregel lezen.** Zoek de bijbehorende
   `Expedition combat profile: enemy slot <n>; role=Ambusher; ...; reaction=1,00 s; ...`.
   Iedere Ambusher heeft precies 1,00 s, ongeacht prijs; niet-casemates houden hun oude
   `role=Assault|Line|FireSupport` en 1–5 s reactie.
5. **Gedrag bij contact.** Laat de vijand de speler zien. De romp hoort al te draaien terwijl
   de reactietimer loopt; het eerste schot volgt na ongeveer 1 s native aim, niet pas na de
   oude 1–5 s plus een aparte draaifase. De ambusher mag niet naar de spelerspawn oprukken
   zolang hij zicht heeft.
6. **45-secondenregel.** Breek het zicht (achter dekking/terugtrekken) en wacht. De ambusher
   blijft staan; pas na 45 s zonder zicht mag hij in korte stappen van ongeveer 40 m met
   tussenpozen oprukken. Daarna stopt hij weer zodra hij een doel ziet.
7. **Geen valse ambushers.** Controleer in dezelfde of een volgende testpoolbattle dat
   Wirbelwind en AMX-13-90 **geen** `role=Ambusher` krijgen. Zet eventueel
   `[Combat] AmbusherDesigns = Flakpanzer IV Wirbelwind` respectievelijk
   `NotAmbusherDesigns = FV-90` en herstart voor de override-check (`source=config`).
8. **Afsluiten.** Controleer dat vuren alleen op native zicht gebeurt (geen schot door
   onzichtbare dekking), dat er geen `Expedition ... failed`/`Ambusher`-uitzondering in de log
   staat, en dat een normale save/load en de return naar de editor ongewijzigd werken.
