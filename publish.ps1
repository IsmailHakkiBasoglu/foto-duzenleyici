$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$dist = Join-Path $PSScriptRoot 'dist'

function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what başarısız (çıkış $LASTEXITCODE)" } }

# 1) Taşınabilir zip: tek dosyalı uygulama.
$out = Join-Path $dist 'FotoDuzenleyici'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish FotoDuzenleyici.App -c Release -o $out; Check 'Uygulama yayını'
Copy-Item README.md, LICENSE, CHANGELOG.md $out
Get-ChildItem $out -Filter *.pdb | Remove-Item
$zip = Join-Path $dist 'FotoDuzenleyici-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Hazir: $zip"

# 2) Kurulum klasörü: uygulama ve komut satırı (fotoduzenle) aynı klasörde, .NET dosyalarını paylaşır.
$setup = Join-Path $dist 'setup'
if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
dotnet publish FotoDuzenleyici.App -c Release -o $setup -p:PublishSingleFile=false; Check 'Kurulum için uygulama yayını'
dotnet publish FotoDuzenleyici.Cli -c Release -o $setup -r win-x64 --self-contained true -p:DebugType=none; Check 'Komut satırı yayını'
Get-ChildItem $setup -Filter *.pdb | Remove-Item

# 3) Setup.exe (Inno Setup 6 kuruluysa).
$csproj = [xml](Get-Content FotoDuzenleyici.App\FotoDuzenleyici.App.csproj -Encoding utf8)
$ver = @($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning 'Inno Setup 6 bulunamadı; Setup.exe derlenmedi (zip hazır).'; return }
& $iscc /Qp "/DAppVersion=$ver" "/DSourceDir=$setup" "/DOutputDir=$dist" installer\FotoDuzenleyici.iss; Check 'Setup.exe derlemesi'
Write-Host "Hazir: $(Join-Path $dist "FotoDuzenleyici-Setup-$ver.exe")"
