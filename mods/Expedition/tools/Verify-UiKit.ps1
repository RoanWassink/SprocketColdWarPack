# Static checks for the Expedition menu port to the Sprocket UI Kit.
# Exit 0 = all checks passed. Read-only: changes no files.
param(
    [string]$BuildOutput = 'bin/UiKitPort'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'src\SprocketExpedition.Game'
$failures = New-Object System.Collections.Generic.List[string]
function Fail([string]$message) { $failures.Add($message) | Out-Null; Write-Host "FAIL $message" }
function Pass([string]$message) { Write-Host "ok   $message" }

# 1. Menu and battle HUD files: no own canvas, EventSystem, raycaster, TMP, Image or pointer hit-test.
$menuFiles = @(
    (Join-Path $game 'CampaignNativeMenu.cs'),
    (Join-Path $game 'CampaignNativeArmory.cs'),
    (Join-Path $game 'CampaignBattleHud.cs')
) + @(Get-ChildItem -LiteralPath $game -Filter 'CampaignKitMenu*.cs' | ForEach-Object { $_.FullName })
$forbidden = [ordered]@{
    'EventSystem' = 'EventSystem'
    'InputSystemUIInputModule' = 'InputSystemUIInputModule'
    'GraphicRaycaster' = 'GraphicRaycaster'
    'Mouse.current' = 'Mouse\.current'
    'Canvas' = '\bCanvas\b'
    'CanvasScaler' = 'CanvasScaler'
    'RectangleContainsScreenPoint' = 'RectangleContainsScreenPoint'
    'UnlockPointer' = 'UnlockPointer'
    'TMPro' = '\bTMPro\b|TextMeshProUGUI'
    'UnityEngine.UI' = 'UnityEngine\.UI\b'
    'AddComponent' = 'AddComponent<'
    'Image' = '\bImage\b'
    'GameObject' = '\bnew GameObject\b'
}
foreach ($file in $menuFiles) {
    if (-not (Test-Path -LiteralPath $file)) { Fail "missing menu file $file"; continue }
    $name = Split-Path $file -Leaf
    $lines = Get-Content -LiteralPath $file -Encoding UTF8
    foreach ($key in $forbidden.Keys) {
        $hits = @($lines | Select-String -Pattern $forbidden[$key] -CaseSensitive)
        if ($hits.Count -gt 0) { Fail "$name contains '$key' (line $($hits[0].LineNumber))" }
    }
    # 2. Cursor: only inside the single justified fallback block (see report).
    $inBlock = $false; $blocks = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'CURSOR-FALLBACK-BEGIN') { $inBlock = $true; $blocks++; continue }
        if ($line -match 'CURSOR-FALLBACK-END') { $inBlock = $false; continue }
        if ($line -cmatch '\bCursor\.' -and -not $inBlock) { Fail "$name line $($i + 1): Cursor. outside the justified fallback block" }
    }
    if ($name -eq 'CampaignKitMenu.cs' -and $blocks -ne 1) { Fail "$name must hold exactly one CURSOR-FALLBACK block (found $blocks)" }
    if ($name -ne 'CampaignKitMenu.cs' -and $blocks -ne 0) { Fail "$name must not hold a CURSOR-FALLBACK block" }
}
Pass "menu files checked: $(($menuFiles | ForEach-Object { Split-Path $_ -Leaf }) -join ', ')"
# 1b. Visual menu files (2026-10-31): they draw with Kit components (Canvas/Painter/Image are Kit API),
# so only the own-UI-plumbing patterns stay forbidden.
$visualFiles = @((Join-Path $game 'CampaignMenuVisuals.cs'), (Join-Path $game 'CampaignMapThumbnails.cs'))
$visualForbidden = [ordered]@{
    'EventSystem' = 'EventSystem'
    'GraphicRaycaster' = 'GraphicRaycaster'
    'Mouse.current' = 'Mouse\.current'
    'CanvasScaler' = 'CanvasScaler'
    'UnityEngine.UI' = 'UnityEngine\.UI\b'
    'TMPro' = '\bTMPro\b|TextMeshProUGUI'
    'AddComponent' = 'AddComponent<'
    'GameObject' = '\bnew GameObject\b'
    'Cursor.' = '\bCursor\.'
    'IMGUI' = '\bGUI\.|OnGUI'
}
foreach ($file in $visualFiles) {
    if (-not (Test-Path -LiteralPath $file)) { Fail "missing visual menu file $file"; continue }
    $name = Split-Path $file -Leaf
    $lines = Get-Content -LiteralPath $file -Encoding UTF8
    foreach ($key in $visualForbidden.Keys) {
        $hits = @($lines | Select-String -Pattern $visualForbidden[$key] -CaseSensitive)
        if ($hits.Count -gt 0) { Fail "$name contains '$key' (line $($hits[0].LineNumber))" }
    }
}
Pass "visual menu files checked: $(($visualFiles | ForEach-Object { Split-Path $_ -Leaf }) -join ', ')"

# 3. Dependency (inside #if CAMPAIGN) and csproj reference with Private=false.
$plugin = Get-Content -LiteralPath (Join-Path $game 'Plugin.cs') -Raw -Encoding UTF8
if ($plugin -match '#if CAMPAIGN\s*\r?\n\s*\[BepInDependency\(SprocketUiKit\.UiKit\.PluginGuid,\s*">=0\.1\.3 <0\.2\.0"\)\]\s*\r?\n\s*#endif') { Pass 'BepInDependency UiKit >=0.1.3 <0.2.0 inside #if CAMPAIGN' }
else { Fail 'Plugin.cs lacks [BepInDependency(SprocketUiKit.UiKit.PluginGuid, ">=0.1.3 <0.2.0")] inside #if CAMPAIGN' }
if ($plugin -match 'Config\.Bind\("Diagnostics",\s*"DetailedLogging"') { Pass 'Diagnostics.DetailedLogging switch present' }
else { Fail 'Plugin.cs lacks the Diagnostics.DetailedLogging switch' }
[xml]$project = Get-Content -LiteralPath (Join-Path $game 'SprocketExpedition.Game.csproj') -Raw
$kitRefs = @($project.Project.ItemGroup | ForEach-Object { $_.Reference } | Where-Object { $_ -and $_.Include -match 'SprocketUiKit[\\/]SprocketUiKit\.dll$' })
if ($kitRefs.Count -ne 1) { Fail "csproj must reference SprocketUiKit.dll exactly once (found $($kitRefs.Count))" }
elseif ("$($kitRefs[0].Private)".Trim() -ne 'false') { Fail 'csproj SprocketUiKit.dll reference is not Private=false' }
else { Pass 'csproj references SprocketUiKit.dll with Private=false' }

# 4. Phase B (Kit 0.1.2): no TEMP Kit workaround may remain in the game source.
$temps = 0
foreach ($file in Get-ChildItem -LiteralPath $game -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $n = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
        $n++
        if ($line -match 'TEMP: vervalt met Kit 0\.1\.[12]\b' -or ($line -match 'TEMP: vervalt met Kit' -and $line -notmatch 'TEMP: vervalt met Kit \d+\.\d+\.\d+ #\d+') -or $line -cmatch '\bFrozenTimeScale\b') { $temps++; Fail "$($file.Name) line ${n}: Kit 0.1.1 workaround left: $($line.Trim())" }
    }
}
if ($temps -eq 0) { Pass 'no TEMP Kit markers or FrozenTimeScale left' }

# 7. The Kit DLL is never part of the build output.
$output = Join-Path $game $BuildOutput
if (Test-Path -LiteralPath $output) {
    $shipped = @(Get-ChildItem -LiteralPath $output -Recurse -Filter 'SprocketUiKit*.dll')
    if ($shipped.Count -gt 0) { Fail "build output contains $($shipped[0].FullName)" } else { Pass "no SprocketUiKit.dll in $BuildOutput" }
} else { Write-Host "skip build output check ($BuildOutput not built)" }

if ($failures.Count -gt 0) { Write-Host "Verify-UiKit: $($failures.Count) failure(s)."; exit 1 }
Write-Host 'Verify-UiKit: all checks passed.'
exit 0
