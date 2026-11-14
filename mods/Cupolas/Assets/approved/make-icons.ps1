# Re-makes the approved icon. -MakePartIcon: path of Make-PartIcon.ps1 (house-style icon tool of the
# sprocket-bepinex skill; not part of this folder).
param([Parameter(Mandatory)][string]$MakePartIcon)
$ErrorActionPreference='Stop'
$MakePartIcon=(Resolve-Path -LiteralPath $MakePartIcon).Path
Set-Location $PSScriptRoot
$env:TEMP=$PSScriptRoot
$env:TMP=$PSScriptRoot
& $MakePartIcon -Scad 'icon-cupola.scad' -Label 'CUPOLA T-90A' -Out 't90a-cupola-icon.png'
& $MakePartIcon -Check 't90a-cupola-icon.png'
