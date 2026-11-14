# Changelog

## 0.3.12 (2026-11-06): Boss-kandidaten

- `[BossBlueprints]` accepteert namen of IDs gescheiden door `|`; ongeldige
  kandidaten waarschuwen en vallen weg, zonder geldige kandidaat geldt de terugval.
- Nieuwe v7-runs kiezen per seed, era en boss-ronde een stabiele kandidaat.
  Meerdere kandidaten worden optioneel gearchiveerd en gehasht; één kandidaat
  behoudt de oude bytes en hash. Bestaande runs en v6 blijven gelijk.
- Catalogus- en keuzelog toegevoegd; previews en kaarten gebruiken dezelfde keuze.
- Geen nieuwe hooks of wijzigingen aan PlayerCommand of de UI Kit.

## 0.3.12 (2026-11-05): Design caps

- `[Campaign] DesignCaps` pins per-design limits in new catalogues (default: Oplot WT-maten = 2). V7 rosters refill from their allowed pool; older runs and test pool retain their plans.
- Expedition Spawns skips Autosave*.blueprint before native card loading. No new hooks.


## 0.3.12 (2026-11-05): Slots

- Full slots show the run list directly; one confirmation archives a run and
  continues to native New Game options in that slot.
- Empty slots use native New Game; new fixture runs require the default-off
  Diagnostics.AllowFixtureCatalogue switch. Existing fixture saves still load.
- Campaign.SaveSlots defaults to 8 (4-12), using the existing scrolling Kit list.
- Save format, Core, PlayerCommand, Harmony hooks and UI Kit source unchanged.

# Changelog

## 0.3.12 (2026-11-03): Hotfix designer-crash

- Shrapnel-battle-stats-hook verwijderd: zijn lege `HitDamageModel` deelde via
  IL2CPP ICF een native adres met de mesh-edit en crashte bij het verwijderen van
  een face in de designer.
- Elke resterende battle-stats-hook leest vóór het patchen de native
  `methodPointer` en de eerste 16 bytes. Een triviale body (`ret`, `ret imm16`,
  `xor eax,eax; ret` of een `ret` in de eerste 8 bytes) wordt met één logregel
  overgeslagen.
- De `KillEventArgs`-postfix controleert de native klasse van `__instance` en
  doet niets bij een andere klasse.
- Nieuwe cfg `[Diagnostics] BattleStatsHooks` (default `true`). Op `false` wordt
  geen enkele battle-stats-hook geïnstalleerd; het defeat-scherm toont `—` en
  `cause unknown`.
- Pluginversie 0.3.12; testplan 0.3.12.

## 0.3.8 (2026-11-02): Meer verrassing — specials, hinderlagen en bosses met karakter

- Nieuwe regelversie/schema 7 voor nieuwe runs. Runs met schema 6 of ouder
  genereren byte-gelijke offers, formaties, beloningen en bosses; een vaste
  golden test vergelijkt 240 v6-combinaties met de 0.3.7-baseline.
- Schema 7: in een gewone ronde wordt met 25% kans één kaart een gele special
  (Rat Plague/Swarm, Steel Wall/Line of Crossfire/Split) met het v6-dreigingsdoel
  van die kaart. Haalt de special het doel niet, dan blijft de normale kaart.
  Nooit in een boss-ronde of de testpool; nooit Dangerous-only op een
  niet-Dangerous kaart.
- Schema 7: elke tactische groep met 2+ hulls kan een Ambush krijgen, 1 op 2 met
  flankers en 1 op 3 zonder. Zonder flankrollen gaat de helft van de groep naar
  de zijwaartse 3/9-uur-baan.
- Schema 7: boss-escortes per personage. Fortress houdt een compacte
  verdedigingsbox, Hunter krijgt lichte/recon-flankers in een Ambush, Battery
  spawnt zijn escorte in de diepste support-baan; Veteran blijft ongewijzigd.
- Gevechtsgedrag (elke run, niet in saves): Fortress houdt positie, draait de
  romp naar het doel en vuurt; de 0.3.5-terugval is een zijstap van maximaal
  20 m en na 90 s zonder contact volgt één langzame stap. Hunter jaagt zoals
  voorheen en zijn escorte flankeert. Battery stopt op elke afstand met een
  vrij schot en trekt zich terug zodra de speler binnen de helft van de
  beginafstand komt; zijn terugval is alleen zijwaarts.
- Eén logregel per gevecht: `Expedition boss character: <naam> -> <gedrag>`.
- Boss-kaart: korte karakterregel onder de naam (Fortress/Hunter/Battery);
  specialkaarten tonen hun titel. Geen wijziging aan `PlayerCommand`, de UI Kit
  of de testpool.
- Pluginversie 0.3.8; README en testplan bijgewerkt.

## 0.3.7 (2026-11-02): Dangerous-only ontwerpen

- Nieuwe map `BepInEx\config\SprocketExpedition\community-blueprints-dangerous\`
  met dezelfde `<sha256>.blueprint`-markers en `content-library`-blobs als
  `community-blueprints`. Ontwerpen eruit krijgen `MinimumRisk = Dangerous`.
- Staat hetzelfde marker in beide mappen, dan wint Dangerous met één warning;
  ontbreekt de map, dan verandert er niets.
- `EnemyDesignEntry.MinimumRisk` is optioneel en wordt niet geschreven als het
  leeg is. Oude catalogi, archieven, hashes, checkpoints en records blijven
  byte-gelijk; alleen een catalogus met het veld krijgt een andere hash.
- Schema 6 gebruikt zo'n ontwerp alleen in een Dangerous-plan, nooit als boss,
  minion, beloning, starter of veteraan. Cautious en Standard plannen en hun
  prijs- en dreigingsdoel blijven gelijk; oudere rulesVersions en de testpool
  veranderen niet.
- Log bij de catalogusopbouw `Expedition dangerous-only designs: <era>=<n>; ...`
  en per Dangerous-plan `Expedition dangerous-only: round <r> uses '<naam>'`.
- Geen wijziging aan `CampaignEnemyHunt.PlayerCommand`, menu of HUD.

## 0.3.6 (2026-11-02): Defeat-scherm met destroyed-by en gevechtsstatistieken

- A lost battle shows two extra Kit blocks: **Losses** (per own tank, at most six
  rows then `+n more`, with `destroyed by <design> at <m> m` or an honest
  `destroyed (cause unknown)`) and **Battle stats** (the player's tank separately,
  then one team-total line: fired, hit, accuracy, taken, survived, kills, longest
  hit and battle duration). Victory, abort and technical failure are unchanged.
- Always-on log per own tank:
  `Expedition battle stats: <outcome>; <player|ally> '<naam>' fired=<n> hit=<n> acc=<%|—> taken=<n> survived=<n> kills=<n> destroyedBy=<'ontwerp' at <m> m|unknown|alive>`.
- Counters reset at every battle and every Retry; nothing is saved. Observing
  Harmony prefixes/postfixes on the native projectile launch, the three
  damage-model hit overrides and the kill event; an explicit killer wins, otherwise
  the last enemy hit within 15 s attributes; no distance or sight is guessed.
- Fix (0.3.4): the test-pool per-slot name cache survived a Retry that skipped the
  spawn-drop reset, so a second battle could report `tank_gespawnd=nee` for every
  tank. The ledger now owns the slot map, restarts with the battle and overwrites
  a changed slot name.
- Ambusher tests also read `test-pool-goedgekeurd` when `test-pool` lacks the
  approved blueprints. Offers, catalogue hash, checkpoints and records are unchanged.

## 0.3.5 (2026-11-02): Vijand-AI - vijanden blijven niet stilstaan zonder schot

- Stop-to-fire hulls get a pure fire-progress tracker: a shot, 0.5° better gun
  error within 2 s, or 2° hull turn within 2 s counts as progress; reloading
  pauses the 8 s stall clock.
- A stalled hull loses its clear shot for that target and repositions to a
  grounded flank point 25–40 m sideways and 15–30 m forward, alternating sides
  on every next stall; the leg ends on arrival, after 12 s or after 60 m, then
  the normal stop/fire decision resumes with a fresh tracker.
- Ambushers only log and hold. Yielded, player-controlled and immobile hulls
  never reposition; bosses follow the same rule outside `TryBossRetreat`.
- Always-on log line `Expedition team AI: ... no firing progress ...` and
  `fireStall=<s>` in every AI path sample.
- `E(enphosian)MBT-120` stays native/vehicle-side: its hull stayed at the spawn
  point with 0.0 m/s even with a valid grounded recovery waypoint and an active
  native move task; the native driver only yawed in place. No Expedition-code
  fix was made.
- Offers, catalogue hash, checkpoints and records are unchanged.

## 0.3.4 (2026-11-02): Testpool moves on after every fight

- Testpool: Victory, defeat and Retreat advance the cursor by the tanks fought
  (user: "Yes, after any fight"); aborted preparation and technical failure do not.
- After a lost test battle the same round is offered again from the next four tanks,
  without reward; the lost card's risk stays selected for RETRY. Ironman still ends.
- Saves load unchanged (no migration); the cursor check allows up to
  4 × (victories + defeats). Log line `Expedition test pool: offset <old> -> <new> after <outcome>`.
- `test-pool-results.csv` gains `tank_gespawnd`, `contact` and `contact_gevecht`;
  an old file gets only its header line replaced, atomically.
- `Diagnostics.DetailedLogging` defaults to `false`; installed cfg values are kept.
- Normal runs: offers, catalogue hash, checkpoints and records are unchanged.

## 0.3.3 (2026-11-01): Ambusher, zero reaction, seek and destroy

- Casemates are recognised per enemy design from the blueprint bytes (main calibre,
  laying-drive azimuth, rotating turret ring); `[Combat] AmbusherDesigns` and
  `NotAmbusherDesigns` override the detection.
- New enemy role `Ambusher`: holds its position while a target is visible, creeps in
  short steps only after 45 s without sight, receives the highest validated front-arc
  markers and spawns with its hull facing the friendly deployment centre.
- Reaction wait removed for every AI profile: units fire as soon as native laying is done.
- Seek and destroy: a clear shot on a visible target always stops to fire, at any range;
  without a clear shot the hull keeps moving for a better line. Bounded doctrine legs no
  longer move a hull away from a clear line.
- Offers, catalogue hash, checkpoints and records are unchanged.

## 0.3.2 (2026-11-01): Test pool fixes

- Test pool rejects blueprints whose part GUIDs the game does not know (instant VICTORY fix).
- Four distinct test tanks per battle; the cursor advances by tanks fought. Per-tank
  `stats/test-pool-results.csv`.
## 0.3.1 (2026-11-01): Test pool

- Opt-in `[Campaign] TestPool` with native card validation, per-file rejection reasons and CSV report.
- Immutable test-run flag and archived pool; all eras may face every test tank. Sorted three-card rotation, one-copy boss and advance only after victory.
- Existing notes/chips mark the mode, tank names and records with TEST. Test victories cannot unlock veterans; normal player economy, items, score and retry remain.
- Battle CSV gains numeric test_pool and readable enemy_designs; safe upgrades preserve old rows. Test battle logs use the native HUD roster's observed enemy spawns.
- Default off preserves 0.3.0 generation, hashes and saved bytes. Prepared only; no installation or game launch.

## 0.3.0 (in ontwikkeling): schema 6

- Fase 0: vier fights per era met 0.70/0.90/1.10 en expliciete bossstap 1.15; vierdelige endless-curve.
- Vaste offers per ronde, alleen Operational Freedom rerollt. Vijandbudget zonder speler-itembonussen; Contract geldt vanaf de volgende offers.
- Eerlijke difficulty-progressietekst, RETRY na afgebroken voorbereiding, veilig CSV-gevechtslogboek en persistente run-totalen.
- Pure-Core BalanceSim met vaste seeds, drie strategieën en vergelijking met ongewijzigd schema 5.
- Fase 1: onbeperkte normale retries met vaste kaarten, persistente RoundAttempts, Ironman zonder herstel en bevestigd Retire met terminal record.
- Contactbeleid op bevestigde roosterdoden of 60 s combat; na contact telt normale editor-retour als Retreat en is hij voor Ironman uitgeschakeld. Native Resume en generation-guards blijven behouden.
- Decimale score met era/risico/boss/flawless/retry/difficulty/Ironman-factoren; victory-tegel, echte records/slot-score en CSV-scorekolom met veilige headeruitbreiding.
- Echte vijandkosten/budget op kaarten, deterministische boss-preview, difficulty-labels Recruit/Regular/Veteran/Ace en benoemde bossfactor 1.35.
- EmergencyRecovery verdwijnt alleen uit schema-6-loot; schema 1–5 behouden hun opgeslagen bytes en regels. Nieuwe runs gebruiken schema 6 en pluginversie 0.3.0.

## 0.2.0 (2026-10-31, development build, not yet tested in game)

- The Expedition menu now runs on the shared Sprocket UI Kit (`sprocket.uikit`,
  hard dependency `>=0.1.3 <0.2.0` in the campaign build). Same screens, buttons, actions,
  open/close moments and battle freeze; new Kit look.
- Removed from the menu: own Canvas/CanvasScaler/GraphicRaycaster/backdrop, the dedicated
  EventSystem, the manual `Mouse.current` click dispatch, the per-frame cursor unlock and the
  uGUI/TMP helpers and colour constants. The Kit owns input, Esc, cursor and input blocking.
  One cursor fallback remains for a battle scene outside the Kit's Game context.
- Kit 0.1.2 features: dim backdrop (.97) behind a 1600x920 panel; Kit sub-pages (difficulty,
  archive confirmation, armory management); screen-dependent `Closable` with `OnCloseRequest`,
  so Esc does what the old CLOSE / CANCEL button did and nothing on battle result or
  preparation; reward rarity colours as card accent plus coloured chip.
- The battle freezes at `Time.timeScale = 0` (the Kit detects pause via the native pause menu).
  If the native pause menu closes the panel, it reopens once that menu is gone and the freeze
  is re-applied.
- Battle HUD (round/era/boss and roster counts) now uses the Kit HUD mode (`UiKit.CreateHud`,
  top centre); same text, 0.2 s refresh and visibility rules. No own canvas.
- Status messages (preparation progress, fatal slot errors, armory notices) use the panel
  status line.
- New `Diagnostics.DetailedLogging` switch (default `true`) with menu and HUD traces.
- New `tools/Verify-UiKit.ps1` static check; plugin version 0.1.0 -> 0.2.0 (the version was not
  used by saves, catalogues or the installer).
- Offer cards: the formation composition shows design names instead of raw blueprint ids, and long card lines wrap instead of ending in "…" (needs Kit 0.1.3, which fixes `Label.Wrap()` on labels built after the panel).
- Visual encounter menu and result screen (same actions and gameplay): the header shows Round / Era / Budget as chips and one instruction line; each offer card shows the map picture (the game's own splash PNG, decoded once per map and shrunk to 768 px), the title, kind and rarity chips, enemy count and Supply chips, an enemy-cost bar against your budget, one composition line per design (with roles), a reward block in the rarity colour and the remaining lines under a closed Details section. The Victory/Defeat screen has a coloured banner (round, era, map), tiles for next budget, earned funds and pending supply, the veteran notice, the received item as a rarity card, and the remaining lines under Details. Pure helpers (`UIMenuVisuals.cs`, `RewardRules.DescribeParts`) are covered by the Presentation tests.
- Unchanged: editor budget label, pause menu and native menu buttons.
- Dynamic eras: Expedition now uses the eras the game lists (`VehicleClassifications.eras`), ordered on start
  date, instead of six fixed names. The schedule of a new run is every playable era that has a usable,
  non-stationary tank (minimum two); a skipped era is logged with its reason. Eras added by mods
  (Coldwar2, Coldwar3 from Expanded Ballistics) work without a code change. Existing runs keep their pinned
  schedule; the budget table continues past the sixth era (x10, x12.5), era progression and spawn/combat ranges
  keep growing or saturate, boss choices fall back to the nearest earlier era, and eras without their own
  combat cue use the last known era cue (Coldwar2/3 play the Coldwar music).
- Fix: `EndDate(era)` is now always the last day inside that era. A run copy saved with the open-ended date
  (`TechDate.MaxValue`, written while Coldwar was the last era) is classified by the game in Coldwar3, which gave
  "The saved tank belongs to an incompatible later era" in slot 4. The run-owned copy is now moved to the end of
  its current era when its date is that sentinel or an era end date; other later-era designs keep a clearer error.
