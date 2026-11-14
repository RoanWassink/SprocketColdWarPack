[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket', # only read: the installed Kit version
    [string]$BuildDir = '',  # folder with the built SprocketAIOrders.dll (default: <mod>\build)
    [string]$WorkDir = ''    # scratch folder for the fixtures (default: the Windows temp folder, never the source tree)
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$BuildDir) { $BuildDir = Join-Path $root 'build' }
if (!$WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) 'sprocket-aiorders-verify' }
$fixture = Join-Path $WorkDir ('verify-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$source = Join-Path $fixture 'src'
$output = Join-Path $fixture 'build'
New-Item -ItemType Directory -Path $source,$output -Force | Out-Null
Copy-Item -Path (Join-Path $root 'src\*.cs') -Destination $source
Copy-Item -LiteralPath (Join-Path $BuildDir 'SprocketAIOrders.dll') -Destination $output
$verify = Join-Path $root 'Verify.ps1'
function Check-Verify([string]$name, [bool]$success, [string]$game = $GameDir) {
    try {
        $ErrorActionPreference = 'Continue' # Expected native stderr must not abort negative tests in Windows PowerShell 5.1.
        $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verify -GameDir $game -BuildDir $output -SourceDir $source 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = 'Stop' }
    if (($code -eq 0) -ne $success) { throw "$name expected success=$success; exit=$code; $result" }
    Write-Output "PASS $name (exit $code)"
}
Check-Verify 'valid fixture' $true
$pluginPath = Join-Path $source 'Plugin.cs'
$original = Get-Content -LiteralPath $pluginPath -Raw
Set-Content -LiteralPath $pluginPath -Value ($original.Replace('>=0.1.2 <0.2.0','>=0.1.0 <0.2.0'))
Check-Verify 'wrong dependency range rejected' $false
Set-Content -LiteralPath $pluginPath -Value $original
Check-Verify 'missing installed Kit rejected' $false (Join-Path $fixture 'missing-game')
$fakeKit = Join-Path $fixture 'wrong-version-game\BepInEx\plugins\SprocketUiKit'
New-Item -ItemType Directory -Path $fakeKit -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $output 'SprocketAIOrders.dll') -Destination (Join-Path $fakeKit 'SprocketUiKit.dll')
Check-Verify 'incompatible installed Kit version rejected' $false (Join-Path $fixture 'wrong-version-game')
foreach ($name in @('SprocketUiKit.dll','SprocketKeybinds.dll')) {
    $bad = Join-Path $output $name
    Set-Content -LiteralPath $bad -Value 'dependency output fixture'
    Check-Verify "bundled $name rejected" $false
    Remove-Item -LiteralPath $bad
}
$badSource = Join-Path $source 'Bad.cs'
foreach ($snippet in @('GUI.Button(rect, "bad");','Cursor.lockState = lockMode;', 'Cursor.visible = true;',
    '[HarmonyPatch(typeof(VehicleInputSystemInputs), "Fire")] class Block {}', 'class MapInputBlock {}', 'bool MapOwnsMouse;', 'void OnGUI() {}', 'GUI.Label(rect, "bad");', 'GUIStyle style;',
    'class PanelPlacement {}', 'class MapInputPolicy {}', 'var mouse = Mouse.current;',
    'var panel = UiKit.CreatePanel(Plugin.Guid, "menu", "x", null, 10, 10);',
    'InputSystem.DisableDevice(Keyboard.current);',
    'using System.Reflection; class Bad {}',
    'typeof(SprocketUiKit.Panel).GetField("X");',
    'typeof(CanvasView).GetProperty("Camera");',
    'var kit = Type.GetType("SprocketUiKit.Panel, SprocketUiKit");')) {
    Set-Content -LiteralPath $badSource -Value $snippet
    Check-Verify "forbidden source rejected: $snippet" $false
}
Remove-Item -LiteralPath $badSource
$obsolete = Join-Path $source 'PanelPlacement.cs'
Set-Content -LiteralPath $obsolete -Value '// empty obsolete file'
Check-Verify 'obsolete PanelPlacement file rejected even without class' $false
Remove-Item -LiteralPath $obsolete
Check-Verify 'restored fixture' $true
Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'PASS fixtures removed.'
