<#
.SYNOPSIS
    Builds Sprocket Visual Effects and runs all its checks once. Writes nothing into the game folder.
.DESCRIPTION
    -GamePath  the real Sprocket folder, READ ONLY (stock lamp part JSON, interop signature, stock mesh bundles,
               interop scan with the Cpp2IL dump).
    -GameRef   folder with BepInEx\core and BepInEx\interop to build against (default: -GamePath).
    -Out       scratch folder for the build output and the interop scan report (default: a new folder under temp).
    -IconChecker  Make-PartIcon.ps1 of the sprocket-bepinex skill, for the house-style check of the three icons
               (or set SPROCKET_MAKE_PARTICON). Without it that one check is reported as SKIP.
    -NativeMethods  optional all-native-methods.json for the "OnGUI RVA is unique" check (otherwise SKIP).
    -AtlasDir  optional folder with the S1 atlas set (<name>_A.png / <name>_B.png) for the FlipbookTables check (otherwise SKIP).
#>
[CmdletBinding()]
param(
    [string]$GamePath='E:\SteamLibrary\steamapps\common\Sprocket',
    [string]$GameRef,
    [string]$Out,
    [string]$IconChecker=$env:SPROCKET_MAKE_PARTICON,
    [string]$NativeMethods,
    [string]$AtlasDir,
    [string]$Cpp2Il
)
$ErrorActionPreference='Stop'
if(-not $GameRef){$GameRef=$GamePath}
if(-not $Out){$Out=Join-Path ([IO.Path]::GetTempPath()) ('sprocketvfx-verify-'+[Guid]::NewGuid().ToString('N'))}
$out=[IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $out -Force | Out-Null
function Step([string]$Name,[scriptblock]$Body){Write-Output "== $Name";& $Body;if($LASTEXITCODE){throw "$Name failed (exit $LASTEXITCODE)."}}
Push-Location $PSScriptRoot
try{
    if((dotnet --version) -ne '8.0.425'){throw 'Build requires the pinned SDK 8.0.425 (global.json).'}
    Step 'Build SprocketVisualEffects (Release)' {dotnet build SprocketVisualEffects.csproj -c Release -o (Join-Path $out 'build') --nologo --disable-build-servers "-p:GameDir=$GameRef" -p:ContinuousIntegrationBuild=true}
    Step 'EffectMathChecks' {dotnet build checks/EffectMathChecks/EffectMathChecks.csproj -c Release --nologo --disable-build-servers -v q;if($LASTEXITCODE){return};dotnet checks/EffectMathChecks/bin/Release/net8.0/EffectMathChecks.dll}
    Step 'Checks (assets, interop signature, built DLL allowlist)' {
        dotnet build checks/Checks.csproj -c Release --nologo --disable-build-servers -v q;if($LASTEXITCODE){return}
        $checkArgs=@((Join-Path $PSScriptRoot 'assets'),$GamePath,(Join-Path $out 'build/SprocketVisualEffects.dll'))
        if($AtlasDir){$checkArgs+=$AtlasDir}
        dotnet checks/bin/Release/net8.0/Checks.dll @checkArgs
    }
    Step 'LampChecks' {dotnet build checks/LampChecks/LampChecks.csproj -c Release --nologo --disable-build-servers -v q;if($LASTEXITCODE){return};dotnet checks/LampChecks/bin/Release/net8.0/LampChecks.dll}
    Step 'EligibilityChecks' {dotnet build checks/EligibilityChecks/EligibilityChecks.csproj -c Release --nologo --disable-build-servers -v q;if($LASTEXITCODE){return};dotnet checks/EligibilityChecks/bin/Release/net8.0/EligibilityChecks.dll}
    Step 'Contract (parts, localization, assets, hooks, Mast link)' {
        $contractArgs=@('--game',$GamePath)
        if($NativeMethods){$contractArgs+=@('--native-methods',$NativeMethods)}
        py -I (Join-Path $PSScriptRoot 'checks/check_contract.py') @contractArgs
    }
    Write-Output '== Icon house style'
    if($IconChecker -and (Test-Path -LiteralPath $IconChecker)){
        foreach($form in @('round','rect','marker')){& $IconChecker -Check (Join-Path $PSScriptRoot "lamp-assets/lamp-$form-icon.png")}
    }else{Write-Output 'SKIP icon house-style check: pass -IconChecker (Make-PartIcon.ps1 of the sprocket-bepinex skill) or set SPROCKET_MAKE_PARTICON'}
    Step 'Interop scan of the built DLL' {
        $scanParams=@{ModDll=(Join-Path $out 'build/SprocketVisualEffects.dll');GamePath=$GamePath;Report=(Join-Path $out 'interop-scan.txt')}
        if($Cpp2Il){$scanParams['Cpp2Il']=$Cpp2Il}
        & (Join-Path $PSScriptRoot 'checks/Scan-InteropSpans.ps1') @scanParams
    }
    Write-Output "ALL CHECKS PASSED. Output: $out"
}finally{Pop-Location}
