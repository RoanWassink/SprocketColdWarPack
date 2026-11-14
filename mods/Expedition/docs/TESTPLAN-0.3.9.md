# Testplan 0.3.12 — hotfix designer-crash

Installeer 0.3.12 met de game dicht (regisseur). Start daarna de game en doorloop:

1. **Designer, faces verwijderen.** Open een ontwerp in de designer, selecteer een
   face en verwijder die (meerdere keren, ook op een plaat met een gat). Verwacht:
   geen crash en geen `AccessViolationException` in `ErrorLog.log`.
2. **Logregels per hook.** Open `BepInEx\LogOutput.log` na de start en zoek
   `Expedition battle stats:`. Verwacht één regel per hook (`launch`, `armour hit`,
   `explosive hit`, `kill event`) in de vorm
   `... methodPointer=0x… bytes=<32 hex> -> patched` of `-> skipped (<reden>)`.
   `shrapnel hit` mag niet voorkomen.
3. **Gevecht met stats.** Vecht een campagnegevecht, laat een eigen tank
   vernietigen en open het defeat-scherm. Verwacht: Losses met
   `destroyed by <naam> at <m> m` of `destroyed (cause unknown)`, Battle stats met
   de tellingen, en één `Expedition battle stats: <outcome>; ...`-logregel per
   eigen tank.
4. **Herhaling met de schakelaar uit.** Sluit de game, zet in
   `BepInEx\config\sprocket.expedition.cfg` onder `[Diagnostics]`
   `BattleStatsHooks = false`, start opnieuw en herhaal stap 1 en 3. Verwacht: één
   regel `Expedition battle stats: hooks disabled (...)`, geen losse hookregels,
   geen crash, en op het defeat-scherm `—` bij de statistieken en
   `destroyed (cause unknown)` bij de verliezen.
