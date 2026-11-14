[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    [string]$BuildDir = '',
    [string]$SourceDir = ''
)
$ErrorActionPreference = 'Stop'
try {
    if (!$BuildDir) { $BuildDir = Join-Path $PSScriptRoot 'build' }
    if (!$SourceDir) { $SourceDir = Join-Path $PSScriptRoot 'src' }
    $pluginSource = Get-Content -LiteralPath (Join-Path $SourceDir 'Plugin.cs') -Raw
    $dependency = '\[BepInDependency\(\s*(?:UiKit\.PluginGuid|"sprocket\.uikit")\s*,\s*">=0\.1\.2 <0\.2\.0"\s*\)\]'
    if ($pluginSource -notmatch $dependency -or $pluginSource -notmatch 'using SprocketUiKit;') {
        throw 'Missing UI Kit dependency sprocket.uikit with range >=0.1.2 <0.2.0 in Plugin.cs.'
    }
    Write-Output 'PASS UI Kit dependency: sprocket.uikit >=0.1.2 <0.2.0 (source).'

    $kit = Join-Path $GameDir 'BepInEx\plugins\SprocketUiKit\SprocketUiKit.dll'
    if (!(Test-Path -LiteralPath $kit -PathType Leaf)) { throw "UI Kit not installed: $kit" }
    $kitVersion = [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($kit).FileVersion)
    if ($kitVersion -lt [version]'0.1.2.0' -or $kitVersion -ge [version]'0.2.0.0') {
        throw "Installed UI Kit version $kitVersion is outside >=0.1.2 <0.2.0."
    }
    Write-Output "PASS installed UI Kit $kitVersion."

    $dll = Join-Path $BuildDir 'SprocketAIOrders.dll'
    if (!(Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Build missing: $dll" }
    $version = [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion)
    if ($version -ne [version]'0.5.2.0') { throw "Expected AI Orders 0.5.2; got $version." }
    $bundled = @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File | Where-Object {
        $_.Name -in @('SprocketUiKit.dll', 'SprocketKeybinds.dll')
    })
    if ($bundled.Count) { throw "Shared dependency bundled in output: $($bundled.FullName -join ', ')" }
    Write-Output 'PASS build 0.5.2; no shared Kit/Keybinds DLL bundled.'

    $files = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Filter '*.cs' -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    })
    if (!$files.Count) { throw 'No C# source files found.' }
    $allSource = ($files | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    if ($allSource -match 'CreatePanel\([^;\r\n]*"menu"') {
        throw 'The F9 menu must not be a Kit panel (CreatePanel ... "menu" is forbidden).'
    }
    if ($allSource -notmatch 'CreateHud\(\s*Plugin\.Guid\s*,\s*"quick"\s*,\s*340\s*\)') {
        throw 'Missing UiKit.CreateHud(Plugin.Guid, "quick", 340) for the F9 HUD.'
    }
    if ($allSource -notmatch 'quickHud\.HideWhenCursorFree\s*=\s*false') {
        throw 'The F9 HUD must set quickHud.HideWhenCursorFree = false.'
    }
    Write-Output 'PASS F9 uses a non-modal CreateHud("quick") and no CreatePanel("menu").'
    # Kit 0.1.2 dashed lines can loop forever (froze the game in 0.5.0); only solid Kit lines allowed.
    # Pattern: a (Poly)Line call ending in "<width>, <dash>);" or using the named dashPx argument.
    if ($allSource -match '\.(?:Polyline|Line|Rect)\([^;]*,\s*[0-9.]+f?\s*,\s*[0-9.]+f?\s*\)\s*;' -or $allSource -match 'dashPx\s*:') {
        throw 'Kit dashed lines (dashPx > 0) are forbidden; use DashedLine.'
    }
    Write-Output 'PASS no Kit dashed lines.'
    $forbidden = @{
        'IMGUI map code' = '\b(?:OnGUI|GUIStyle)\b|\bGUI\s*\.'
        'obsolete map layout/input' = '\b(?:PanelPlacement|MapInputPolicy|MapView)\b|\bMouse\s*\.\s*current\b'
        'cursor writes' = '\bCursor\s*\.\s*(?:lockState|visible)\s*(?:=(?!=)|[+*/%-]=|\+\+|--)'
        'new input blocking' = '\b(?:BlockGameplayInput|DisableDevice)\b'
        'per-mod vehicle input prefixes' = '\bVehicleInputSystemInputs\b'
        'old map input ownership' = '\b(?:MapInputBlock|MapOwnsMouse)\b'
    }
    foreach ($rule in $forbidden.GetEnumerator()) {
        foreach ($file in $files) {
            if ((Get-Content -LiteralPath $file.FullName -Raw) -match $rule.Value) {
                throw "Forbidden $($rule.Key) in $($file.FullName)."
            }
        }
        Write-Output "PASS no $($rule.Key)."
    }
    if (Test-Path -LiteralPath (Join-Path $SourceDir 'PanelPlacement.cs')) { throw 'PanelPlacement.cs must be removed.' }
    # Keep only the existing Expedition bridge reflection, whose target is explicit.
    # No other reflective code, aliases or dynamic Kit lookup are allowed in consumer source.
    foreach ($file in $files) {
        $code = Get-Content -LiteralPath $file.FullName -Raw
        if ($file.Name -eq 'OrdersHost.cs') {
            $code = $code -replace 'using System\.Reflection;', ''
            $code = $code -replace 'private MethodInfo\? expeditionHandoff;', ''
            $code = $code -replace 'AppDomain\.CurrentDomain\.GetAssemblies\(\)\.FirstOrDefault\(a => a\.GetType\("SprocketExpedition\.Game\.CampaignEnemyHunt"\) != null\)', 'null'
            $code = $code -replace 'assembly\.GetType\("SprocketExpedition\.Game\.CampaignEnemyHunt"\)!\s*\.GetMethod\("PlayerCommand", BindingFlags\.Static \| BindingFlags\.NonPublic\)', 'null'
            $code = $code -replace 'expeditionHandoff\.Invoke\(null, new object\?\[\] \{ player!\.TryCast<IVehicleBehaviour>\(\), receivers \}\)', 'null'
        }
        if ($code -match '\b(?:System\s*\.\s*Reflection|BindingFlags|MethodInfo|FieldInfo|PropertyInfo|Activator|GetType|GetTypes|GetExportedTypes|GetAssemblies|GetAssembly|GetMethod|GetField|GetProperty|InvokeMember|Invoke)\b|"SprocketUiKit(?:\.|"|,)') {
            throw "Reflection outside the existing explicit Expedition bridge is forbidden: $($file.FullName)."
        }
    }
    Write-Output 'PASS no reflection on UI Kit; only the existing explicit Expedition bridge is allowed.'
    Write-Output 'VERIFY PASSED'
    exit 0
} catch {
    Write-Error "VERIFY FAILED: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
