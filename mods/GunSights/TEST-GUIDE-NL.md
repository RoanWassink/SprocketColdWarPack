# Testgids Gun Sights 0.3.0

Vooraf: Sprocket dicht, `install-0.3.0.ps1` gedraaid. Log: `E:\SteamLibrary\steamapps\common\Sprocket\BepInEx\LogOutput.log`.
Bij het starten verwacht: `Sprocket Gun Sights 0.3.0 loaded: 33 sights for ... countries.` en geen fout over "Saving sight choices".

1. **Dak-sight, vinkje aan** (Bradley M3A3: sight op het torendak).
   - Designer: rechtsklik op de sight > sectie "Gun sight". Het vinkje "Sight follows gun" staat aan, met de regel "Default (follows gun)".
   - Test drive, in de sight: richt op en neer met de muis. Het beeld gaat mee met het kanon; het bewegende native richtcirkeltje is zichtbaar.
   - Log: `[Gun sights] Sight <vuid> active: follows gun (default, DefaultSightFollowsGun=True); variant ...`.
2. **Vinkje uit**: klik het vinkje uit. Log: `[Gun sights] Sight <vuid> set to stays static.`
   - In de sight: het beeld blijft langs het onderdeel (horizontaal bij een dak-sight); het richtcirkeltje is weg.
   - Log: `... active: stays static (saved choice); ...`. Wissel naar een sight met het vinkje aan: het cirkeltje komt terug.
3. **ATGM-guidance-sight**: geen vinkje, wel de grijze regel "not used here". Het blijft zijn vrije view houden.
4. **Opslaan, herstarten, laden**: kies op een sight ook een andere variant (Variant-dropdown) en zet het vinkje uit. Sla op.
   - Log bij opslaan: `[Gun sights] Saved <n> sight row(s) in sprocketGunSightsV1 (<m> object(s)).` (n = aantal sights met een keuze).
   - Game herstarten, voertuig laden. Log: `[Gun sights] Loaded <n> sight row(s) from sprocketGunSightsV1 (...)`.
   - Vinkje en variant zijn bewaard. Thermal: FCS-koppeling en thermal-profiel van dezelfde sight zijn ook nog bewaard.
   - Controle in het bestand (mag): het object van de sight heeft een sleutel `sprocketGunSightsV1` naast eventuele `sprocketFcsLinksV1` / `sprocketThermalProfilesV1`.
5. **Dupliceren**: kopieer de toren (of het sightonderdeel). De kopie houdt vinkje en variant, onder de nieuwe VUID (log: `Loaded ... row(s)`); de keuze op het origineel verandert niet.
6. **Logregels samengevat**: `active: ...` (max 40 per sessie), `set to ...` bij elke klik, `Saved ...` en `Loaded ...` (een regel die gelijk is aan de vorige wordt overgeslagen, bijv. bij designer-snapshots). Geen `[Gun sights] ... :` waarschuwingen verwacht.

Terug: `uninstall-0.3.0.ps1` (Sprocket dicht) zet de 0.2.4-DLL terug uit de back-up.
