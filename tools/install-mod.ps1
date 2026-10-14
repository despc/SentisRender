# Copies the client mod (ClientMod\) into the game's local mods folder, %APPDATA%\SpaceEngineers\Mods\SentisRender,
# from where the game publishes it to the workshop (Mods > the mod > Publish) or a local world loads it.
#   powershell -ExecutionPolicy Bypass -File tools\install-mod.ps1
# The folder's old scripts are replaced; modinfo.sbmi the game writes on publish (the workshop id) is kept.
param([string]$Name = 'SentisRender')
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $env:APPDATA "SpaceEngineers\Mods\$Name"
New-Item -ItemType Directory -Force $target | Out-Null
$scripts = Join-Path $target 'Data\Scripts'
if (Test-Path $scripts) { Remove-Item $scripts -Recurse -Force }
Copy-Item (Join-Path $repo 'ClientMod\*') $target -Recurse -Force
Write-Host "The mod is in $target"
Get-ChildItem $target -Recurse -File | ForEach-Object { Write-Host ("  {0,8}  {1}" -f $_.Length, $_.FullName.Substring($target.Length + 1)) }
