# Test plan: Expedition on UI Kit (0.2.0, 2026-10-31)

Requires Sprocket UI Kit 0.1.3 installed and `Diagnostics.DetailedLogging = true` in
`BepInEx\config\sprocket.expedition.cfg`. Afterwards the agent reads `BepInEx\LogOutput.log`.

## Main menu
1. Native Expedition button -> New Game: Kit panel "Expedition" opens with a dark backdrop.
2. Pick another starting era (button turns primary, "SELECTED:").
3. DIFFICULTY -> Difficulty sub-page (back arrow in the title bar); change budget and
   STRONGER LATER ERAS; OK returns to New Game with the same era. Repeat with the title-bar
   back and with Esc.
4. Esc on New Game = CANCEL (menu closes, main menu still works, no pause menu).
5. START EXPEDITION: "PREPARING NEW EXPEDITION" plus progress in the status line; Esc does nothing.

## Slots
6. DELETE / ARCHIVE -> archive sub-page; CANCEL, back and Esc return to the slot list;
   ARCHIVE & FREE SLOT frees the slot.
7. CLOSE and Esc on the slot list behave like the old CLOSE button.

## Designer
8. Offers: cards side by side; reward cards show the rarity colour (blue/red/purple/gold)
   as card edge and chip; choose an offer -> designer.
9. ITEMS / BONUSES -> armory: KEEP / ACTIVATE / DISCARD / MANAGE ACTIVE ITEMS (sub-page);
   BACK TO BUILDING returns to the same tank. Esc on the armory = back to the designer.
10. Click buttons and the dark backdrop above designer parts: nothing in the designer reacts.

## Battle
11. Battle HUD at the top centre: round / era (/ boss) and "Enemies alive ... Friendly tanks ...";
    counts drop when a tank is knocked out. The HUD stays visible when the cursor is freed.
12. Result screen after victory and defeat: HUD hidden, the world stands still, cursor free,
    no firing, scope or camera. Esc does nothing.
13. While the result screen is open, open the game's pause menu (if possible) and resume:
    the result screen comes back and the world is still frozen.
14. Continue / Retry / RETURN TO MENU work; after Continue time runs normally, the cursor
    locks for driving and the HUD shows again.
15. Emergency recovery (defeat with recovery charge): USE RECOVERY and ACCEPT DEFEAT.

## Return paths
16. Return to Designer and Return to Menu: panel and HUD close, no stuck cursor.

## Expected log lines
- `Expedition native menu opened.` and `[UI Kit] sprocket.expedition/menu opened in ...`
- `[Expedition menu] screen '...' -> '...'` on every screen change; `back from '...'`, `Esc/close on '...'`
- In battle: `campaign battle frozen ... previousTimeScale=1`, on Continue `time restored to 1`;
  `[Expedition HUD] battle counter shown/hidden.`
- After the pause menu: `Kit closed the panel while the flow wants it (pause menu?)`, then
  `Kit panel open (reopen after the pause menu): shown` (and possibly `battle re-frozen after the pause menu`).
- Not expected: `UI Kit panel creation failed`, `UI Kit HUD unavailable`, `Expedition optional battle counter:` warnings.

## Eras (dynamic era list, 2026-10-31)
Needs Expanded Ballistics with Coldwar2 (1960) and Coldwar3 (1975) installed.
17. Slot 4 (round 21, Coldwar): Continue and Play. Expect no "incompatible later era" error; the designer opens, Play starts the battle. Log: `Expedition normalised the run copy's date 9999.12.31 ...` once, then `saved-design load started: ... technology=1959.12.31`.
18. New Game, Options: the starting era buttons list every era with tanks in order, ending with Coldwar2 and Coldwar3 when they have tanks. Notes show "round 1 of 32" for eight eras at four battles each.
19. Log after opening New Game: `Expedition era availability: Era schedule: WWI, ..., Coldwar3 (8 era(s)).` and, for an era without tanks, `Skipped: <era> (no usable non-stationary tank)`.
20. Start a new run in Coldwar2 or Coldwar3: starter tank, budget and first offers appear; boss rounds use a design from the next era; Coldwar2/3 combat plays the Coldwar music cue.
21. Win all battles of an 8-era run (or start in Coldwar3): finite run ends after Coldwar3 and offers the endless choice.
22. Existing saves with a six-era schedule still load and keep six eras (the new eras are not added).
23. Veteran after an era victory (run copy with the open-ended date): it is added to the pool under the era the run recorded, not Coldwar3.
24. Not expected: `Expedition scene handoff failed`, `The starter era ... is not part of the era schedule`, `Era schedule unusable`.

## Visual menu (encounter cards and result screen, 2026-10-31)
Same Kit 0.1.3, same `DetailedLogging`. Expect no change in what the buttons do; only the look changed.

### Encounter menu
25. Offers screen: under the title one row of chips (Round, Era, Budget in amber) and one grey instruction line. The old lines "Round / Era / Available budget" and the two instruction lines are gone. Hover the Budget chip: tooltip with the budget unit.
26. Each card, top to bottom: map picture (about 150 px, full card width) with the map name bottom left; title only (e.g. "Leopard battlegroup · recon flanks" or the boss name); chips FORMATION / SPECIAL / BOSS plus the rarity in the rarity colour; chips "N enemies" and "Supply +N"; cost line "Enemy 287 942 · your budget 267 388" with a bar; one line per design ("4× Leopard 2A7", with "· flanks" in grey when there is a role); a reward block with a coloured edge (name in bold, one effect line with the duration, for a boss also "Choose 1 of 3 purple items..."); a closed "Details" section; the CHOOSE & BUILD button.
27. The bar is the Kit bar while the enemy force fits the budget. When it costs more than your budget the bar is full in the red-orange Kit error colour with a white tick where the budget ends. The numbers must equal the old "Total enemy cost" and "Available budget".
28. Open Details on a card: Era and map, Risk (not on a curated boss), Spawn layout, boss line, formation line and Intent, "Target enemy cost ... actual ...", the Supply line ("for next era, win funds +5% base") and one line per design with era and price each. Nothing from the old 12 to 15 lines may be missing.
29. Map pictures: Defence, Fields and Dunes show their game splash picture, not stretched (cropped to the card). Open the menu repeatedly, return to the main menu and open it again: no stutter after the first time. With a map picture temporarily unreadable the card shows a flat grey panel with the map name, and the log has one warning `Expedition map picture '<map>' unavailable` for that map.
30. Card widths: three cards side by side at 1080p and 1440p; long titles and design names wrap instead of being cut off; clicking the picture or a chip does nothing; clicking "Details" opens and closes it.
31. CHOOSE & BUILD still selects the offer and opens the designer (same as before); the selected card keeps the primary button.

### Result screen
32. Victory: large green VICTORY banner with "Round N · era · map". Tiles: Next budget, Earned funds · total (only with a reward), Pending supply. When a veteran was unlocked a green notice card appears. The received item is a card in its rarity colour with ITEM RECEIVED, the rarity chip, the name, the effect and "Open ITEMS to activate your reward.". Open Details: Detail, "The encounter was won.", Next phase.
33. Defeat (destroyed or retreat): red-orange DEFEAT banner with a chip naming the outcome ("Friendly force destroyed" / "Combat retreat") and the sentence "... records a loss."; no Continue; emergency recovery screen unchanged.
34. Aborted / technical failure: amber banner, the messages "No combat loss was recorded." and "No supply reward was granted." stay visible; RETRY appears where it did before.
35. A missing number gives no tile (no budget, no reward, no invented zeros). Continue, Retry, SLOTS and RETURN TO MENU behave as before.

### Log and performance
36. Detailed log: `[Expedition menu] screen '...' -> '...'` still once per screen. No `visual offer card failed` warnings, no `UI Kit panel creation failed`. The first open of an offers screen may take a short moment (three PNG decodes on the main thread); afterwards opening is instant. Thumbnails stay in memory for the session (about 4 MB for three maps).
