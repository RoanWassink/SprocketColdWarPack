param([Parameter(Mandatory)][string]$MakePartIcon)  # path to Make-PartIcon.ps1 (skill sprocket-bepinex, scripts folder)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:TEMP=$PSScriptRoot
$env:TMP=$PSScriptRoot
& $MakePartIcon -Scad 'emitter_arrow.scad' -Label 'EMITTER' -Out 'emitter-icon.png' -Hidden -D @{colored='true'}
& $MakePartIcon -Check 'emitter-icon.png'
