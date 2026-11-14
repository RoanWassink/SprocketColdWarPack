<#
.SYNOPSIS
    Runs all offline checks of Sprocket Cupolas: clean build, managed checks, contract checks, icon style, interop scan, staging.
.DESCRIPTION
    Reads the game folder (-GamePath) but never writes to it. Everything that is written goes to -Out
    (default: a scratch folder under the temp folder) and to bin/obj next to this script.
.PARAMETER GamePath
    Sprocket folder, READ ONLY: stock part JSON for the contract checks, interop/GameAssembly for the interop scan.
.PARAMETER GameRef
    Folder with BepInEx\core and BepInEx\interop that the build references (for example a copy of the game's
    BepInEx folders). Default: GamePath.
.PARAMETER Out
    Folder for logs and generated evidence (embedded-mesh JSON, interop report, staged layout).
.PARAMETER IconChecker
    Path of Make-PartIcon.ps1 (house-style icon tool of the sprocket-bepinex skill). Default: environment variable
    SPROCKET_MAKE_PARTICON. Without it the icon style check is skipped and reported as SKIP.
#>
[CmdletBinding()]
param(
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    [string]$GameRef='',
    [string]$Out=(Join-Path ([IO.Path]::GetTempPath()) 'SprocketCupolas-verify'),
    [string]$IconChecker=$env:SPROCKET_MAKE_PARTICON
)
$ErrorActionPreference='Stop'
if(-not $GameRef){$GameRef=$GamePath}
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out=(Resolve-Path -LiteralPath $Out).Path
$transcript=Join-Path $Out 'verify-transcript.txt'
Start-Transcript -LiteralPath $transcript -Force | Out-Null
Push-Location $PSScriptRoot
try {
    & dotnet clean SprocketCupolas.csproj -c Release --nologo -v q "-p:GameDir=$GameRef" *> (Join-Path $Out 'clean.txt')
    if($LASTEXITCODE){throw 'Clean failed.'}
    & dotnet build SprocketCupolas.csproj -c Release --nologo -v minimal "-p:GameDir=$GameRef" *> (Join-Path $Out 'build.txt')
    if($LASTEXITCODE){throw "Build failed; see $Out\build.txt."}
    Write-Output 'PASS clean SDK 8/net6.0/x64 production build'
    & py -I Tools/obj_to_cs.py --check *> (Join-Path $Out 'generator-check.txt')
    if($LASTEXITCODE){throw 'Generated mesh data stale.'}
    & dotnet run --project checks/Checks.csproj -c Release -- $Out *> (Join-Path $Out 'managed-checks.txt')
    if($LASTEXITCODE){throw 'Managed mesh/policy/fake driver checks failed.'}
    Write-Output 'PASS managed mesh validation, actual DLL data, ghost/mirror/release/wrapper fake driver'
    & py -I checks/check_contract.py --out $Out --game $GamePath *> (Join-Path $Out 'contract-checks.txt')
    if($LASTEXITCODE){throw 'Schema/export/hash/identity/hook checks failed.'}
    Write-Output 'PASS independent OBJ reader, schemas and negative cases, approved hashes, zero hooks'
    if($IconChecker -and (Test-Path -LiteralPath $IconChecker)){
        & $IconChecker -Check (Join-Path $PSScriptRoot 'Assets/approved/t90a-cupola-icon.png') *> (Join-Path $Out 'icon-check.txt')
        Write-Output 'PASS approved icon house-style validation'
    } else {
        Write-Output 'SKIP approved icon house-style validation (pass -IconChecker <Make-PartIcon.ps1>)'
    }
    & (Join-Path $PSScriptRoot 'checks/Scan-InteropSpans.ps1') -ModDll (Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketCupolas.dll') -GamePath $GamePath -Report (Join-Path $Out 'interop-scan.txt') *> (Join-Path $Out 'interop-console.txt')
    Write-Output 'PASS recursive static interop scan and positive/negative probes'
    $staged=Join-Path $Out 'staged'
    if(Test-Path -LiteralPath $staged){Remove-Item -LiteralPath $staged -Recurse -Force}
    & (Join-Path $PSScriptRoot 'Build-Package.ps1') -Out $staged *> (Join-Path $Out 'package-build.txt')
    Write-Output 'PASS staged layout of the five shipped files'
    & py -I Tools/report_artifacts.py --out $Out --staged $staged *> (Join-Path $Out 'artifact-summary.txt')
    if($LASTEXITCODE){throw 'Artifact inventory failed.'}
    Write-Output 'PASS Verify.ps1; prepared only, no live test or installation.'
    Set-Content -LiteralPath (Join-Path $Out 'verification-result.txt') -Value 'PASS Verify.ps1: clean build, 0 warnings/0 errors, managed/contract/icon/interop/staging checks passed. No game installation or live testing.' -Encoding UTF8
} catch {
    Set-Content -LiteralPath (Join-Path $Out 'verification-result.txt') -Value ('FAIL Verify.ps1: '+$_.Exception.Message) -Encoding UTF8
    throw
} finally {Pop-Location;Stop-Transcript | Out-Null}
