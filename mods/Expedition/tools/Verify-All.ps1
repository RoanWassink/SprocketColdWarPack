# Builds both configurations and runs every test suite plus Verify-UiKit.ps1.
# -GameRoot: a Sprocket folder or a copy of its BepInEx\core, BepInEx\interop and BepInEx\plugins\SprocketUiKit\SprocketUiKit.dll.
# Environment (optional): SPROCKET_BLUEPRINT_DIR = folder with the five reference blueprints of the casemate check,
# EXPEDITION_FIXTURE_LIBRARY = content library folder for the saved-slot fixtures (an empty folder is enough).
param([Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'SprocketExpedition-verify'), [switch]$SkipBuilds,
    [string[]]$TestNames = @())
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out | Out-Null
Set-Location $project
$results = @()
function RunCheck([string]$name, [scriptblock]$command) {
    $log = Join-Path $out ($name + '.log')
    # Windows PowerShell wraps native stderr as ErrorRecords. Keep the full
    # stack trace and continue to the next check; the exit code is authoritative.
    $ErrorActionPreference = 'Continue'
    & $command *> $log
    $code = $LASTEXITCODE
    $script:results += [pscustomobject]@{Name=$name;Exit=$code;Log=$log}
    Write-Output "$name exit=$code log=$log"
}
if (-not $SkipBuilds) {
    RunCheck 'build-proof' { dotnet build src/SprocketExpedition.Game -c Release -p:UseSharedCompilation=false -p:GameRoot=$GameRoot }
    RunCheck 'build-campaign' { dotnet build src/SprocketExpedition.Game -c Release -p:UseSharedCompilation=false -p:CampaignBuild=true -p:OutputPath=bin/V0312 -p:GameRoot=$GameRoot }
}
foreach ($test in (Get-ChildItem tests -Directory)) {
    if ($TestNames.Count -gt 0 -and $test.Name -notin $TestNames) { continue }
    RunCheck ('test-' + $test.Name) {
        dotnet build $test.FullName -c Release -p:UseSharedCompilation=false -p:CheckEolTargetFramework=false
        if ($LASTEXITCODE -ne 0) { return }
        $runtime = Get-ChildItem (Join-Path $test.FullName 'bin/Release') -Recurse -Filter '*.runtimeconfig.json' | Select-Object -First 1
        if ($null -eq $runtime) { throw "No runtimeconfig found for $test" }
        & 'C:/Program Files/dotnet/dotnet.exe' ($runtime.FullName.Replace('.runtimeconfig.json','.dll'))
    }
}
RunCheck 'uikit' { powershell -ExecutionPolicy Bypass -File tools/Verify-UiKit.ps1 -BuildOutput bin/V0312 }
$results | Export-Csv -NoTypeInformation (Join-Path $out 'checks.csv')
if (@($results | Where-Object Exit -ne 0).Count) { exit 1 }
