# Testplan 0.3.6: defeat-scherm en gevechtsstatistieken (max 6 stappen)

Voorwaarde: 0.3.6 geïnstalleerd met het installatiescript van de uitgave
en de drie hashes van de uitgave. Start het spel normaal via Steam, laad een
bestaande run (of New Game), bouw een tank en kies een gevecht.

1. **Verlies met gevecht.** Vecht tot je eigen tanks vernietigd zijn (FriendlyForceDestroyed),
   of retourneer na contact (CombatRetreat). Op het resultaatscherm staan naast de
   bestaande banner twee blokken: **Losses** met per eigen tank één regel (max. 6, dan
   `+n more`) en **Battle stats** met de spelerstank apart en één `Team:`-regel. Controleer
   `destroyed by <ontwerp> at <m> m` of `destroyed (cause unknown)`.
   Lees in `BepInEx\LogOutput.log`: `Expedition battle stats: <outcome>; player '<naam>'
   fired=… hit=… acc=…% taken=… survived=… kills=… destroyedBy=…`, gevolgd door één
   `ally`-regel per AI-bondgenoot.
2. **Retry-reset.** Kies RETRY, verlies opnieuw en controleer dat `fired=` weer bij 0
   begint (geen opgetelde waarden) en dat de tweede loss-regel de nieuwe namen/uitkomst
   toont. Bij TestPool: `Expedition test pool: battle … perTank=ja/ja|ja/ja|…`, niet
   `nee/ja` voor alle vier.
3. **Overwinning.** Win een gevecht: het Victory-scherm is ongewijzigd (geen Losses- of
   Battle stats-blok) en de `Expedition battle stats:`-regels verschijnen toch in het log
   met `destroyedBy=alive` voor overlevende tanks.
4. **Afstand en onbekend.** Herhaal een verlies waarbij de kill-hit meetbaar was: de
   afstand in het log/de Losses-regel is een plausibele meterswaarde (> 0) en geen 0 of
   `—`. Forceer daarna een verlies zonder waarneembare vijandelijke treffer (bijv. dood
   door aanvaring/instorting): de regel wordt `destroyed (cause unknown)`, niet geraden.
5. **UI Kit en prestaties.** Open/sluit het resultaatscherm en de Details-sectie; geen
   eigen canvas, geen cursor- of invoerprobleem, geen framespikes tijdens het gevecht.
   `tools\Verify-UiKit.ps1` blijft groen (statische controle).
6. **Log- en save-integriteit.** Controleer dat `stats\battles.csv` en
   `stats\test-pool-results.csv` alleen hun normale regels/kolommen krijgen en dat een
   checkpoint/record van vóór 0.3.6 byte-gelijk blijft (geen statistieken in de save).
   Bewaar `LogOutput.log` en de resultaatscherm-screenshot voor Opus.
