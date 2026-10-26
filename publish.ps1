$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$out = Join-Path $PSScriptRoot 'dist\FotoDuzenleyici'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish FotoDuzenleyici.App -c Release -o $out
Copy-Item README.md, LICENSE, CHANGELOG.md $out
Get-ChildItem $out -Filter *.pdb | Remove-Item
$zip = Join-Path $PSScriptRoot 'dist\FotoDuzenleyici-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Hazir: $zip"
