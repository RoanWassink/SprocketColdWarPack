<#
.SYNOPSIS
    Draait de controles van de mod (config-parser, geometrie, blueprintpad, voorbeeldconfig).
    Raakt de spelmap niet aan en schrijft zijn testbestanden buiten de bron.

.PARAMETER WorkDir
    Map voor testbestanden en het log. Standaard een map onder de tijdelijke map van Windows.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'sandbox-targets-checks')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$scratch = Join-Path $WorkDir 'scratch'
New-Item -ItemType Directory -Force $scratch | Out-Null
$example = Join-Path $root 'config\sprocket.sandboxtargets.example.json'
$log = Join-Path $WorkDir 'tests.log'
& dotnet run --project (Join-Path $PSScriptRoot 'SandboxTargets.Checks.csproj') -c Release -p:UseSharedCompilation=false --no-launch-profile -- $scratch $example *> $log
$code = $LASTEXITCODE
Get-Content -LiteralPath $log
if ($code -ne 0) { throw "Checks failed (exit $code); see $log" }
Write-Output "Checks geslaagd; log: $log"
