<#
.SYNOPSIS
    Bouwt SprocketSandboxTargets.dll (Release). Schrijft nooit in de spelmap en installeert niets.

.PARAMETER GameRoot
    Map met BepInEx\core en BepInEx\interop waartegen gebouwd wordt (alleen gelezen).
    Standaard de spelmap; voor een herpak-bouw: de map gameref naast src.
.PARAMETER WorkDir
    Waar het buildlog komt. Standaard een map onder de tijdelijke map van Windows (niet in de bron).

.EXAMPLE
    .\Build-SandboxTargets.ps1 -GameRoot C:\pad\naar\gameref
#>
[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'sandbox-targets-build')
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force $WorkDir | Out-Null
$log = Join-Path $WorkDir 'build.log'
& dotnet build (Join-Path $PSScriptRoot 'src\SprocketSandboxTargets.csproj') -c Release --ignore-failed-sources -p:UseSharedCompilation=false "-p:GameRoot=$GameRoot" *> $log
$code = $LASTEXITCODE
Get-Content -LiteralPath $log
if ($code -ne 0) { throw "Build failed (exit $code); see $log" }
Write-Output "DLL: $(Join-Path $PSScriptRoot 'src\bin\Release\net6.0\SprocketSandboxTargets.dll')"
