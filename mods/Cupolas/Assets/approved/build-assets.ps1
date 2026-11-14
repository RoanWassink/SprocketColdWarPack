$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:BLENDER_USER_CONFIG=$PSScriptRoot
$env:BLENDER_USER_SCRIPTS=$PSScriptRoot
$env:TEMP=$PSScriptRoot
$env:TMP=$PSScriptRoot
$scad='C:\Program Files\OpenSCAD\openscad.com'
& $scad -o t90a_cupola_base.stl base-export.scad
if($LASTEXITCODE -ne 0){throw 'Base export failed'}
& $scad -o t90a_cupola_door-raw.stl door-export.scad
if($LASTEXITCODE -ne 0){throw 'Door export failed'}
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python optimize-door.py
if($LASTEXITCODE -ne 0){throw 'Door optimization failed'}
py export_game_mesh.py
if($LASTEXITCODE -ne 0){throw 'OBJ export failed'}
py verify-cupola.py
if($LASTEXITCODE -ne 0){throw 'Mesh verification failed'}
