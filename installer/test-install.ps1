# Setup.exe'yi sessizce bu kullanıcıya kurar, denetler, kaldırır, yeniden denetler. CI'de çalışır.
# Elle: powershell -File installer\test-install.ps1 dist\FotoDuzenleyici-Setup-x.y.z.exe
# DİKKAT: Aynı makinede kurulu bir Foto Düzenleyici varsa onu kaldırır.
param([Parameter(Mandatory)][string]$Setup)
$ErrorActionPreference = 'Stop'

$guid = '{8F3C2A51-6B7E-4D2A-9C41-5E0D7A3B9F12}_is1'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$guid"
$menuKeys = 'HKCU:\Software\Classes\Directory\shell\FotoDuzenleyici', 'HKCU:\Software\Classes\Drive\shell\FotoDuzenleyici'
$settings = Join-Path $env:LOCALAPPDATA 'FotoDuzenleyici'

function Fail($m) { throw "KURULUM TESTİ: $m" }
function UserPath { (Get-ItemProperty HKCU:\Environment -ErrorAction SilentlyContinue).Path }

function Install {
    $p = Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER',
        '/TASKS=desktopicon,contextmenu,addtopath' -Wait -PassThru
    if ($p.ExitCode -ne 0) { Fail "kurulum çıkış kodu $($p.ExitCode)" }
}

# Kurulum iki kez: güncelleme senaryosu, PATH girdisi tek kalmalı.
Install
Install

$app = (Get-ItemProperty $uninstallKey).InstallLocation.TrimEnd('\')
if (-not $app) { Fail 'Program Ekle/Kaldır kaydı yok' }
Write-Host "Kuruldu: $app"
foreach ($f in 'FotoDuzenleyici.exe', 'fotoduzenle.exe', 'README.md', 'LICENSE', 'unins000.exe') {
    if (-not (Test-Path (Join-Path $app $f))) { Fail "$f yok" }
}
if ((Get-ItemProperty $uninstallKey).DisplayVersion -notmatch '^\d+\.\d+\.\d+') { Fail 'DisplayVersion yok' }

foreach ($k in $menuKeys) {
    $cmd = (Get-ItemProperty "$k\command").'(default)'
    if ($cmd -ne "`"$app\FotoDuzenleyici.exe`" `"%1`"") { Fail "sağ tık komutu yanlış: $cmd" }
}

$hits = @((UserPath) -split ';' | Where-Object { $_.TrimEnd('\') -ieq $app }).Count
if ($hits -ne 1) { Fail "PATH'te $hits kez" }

$desktop = [Environment]::GetFolderPath('Desktop')
if (-not (Get-ChildItem $desktop -Filter 'Foto*.lnk')) { Fail 'masaüstü kısayolu yok' }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) ''
if (-not (Get-ChildItem $menu -Filter 'Foto*.lnk')) { Fail 'Başlat menüsü kısayolu yok' }

& (Join-Path $app 'fotoduzenle.exe') --verify-backup (Join-Path $env:TEMP 'fd-olmayan-yedek') | Out-Null
if ($LASTEXITCODE -ne 2) { Fail "fotoduzenle çıkış kodu $LASTEXITCODE (2 beklenirdi)" }

# Kaldırma: ayar klasörü sessiz kaldırmada kalmalı.
New-Item -ItemType Directory -Force $settings | Out-Null
Set-Content (Join-Path $settings 'test-isareti.txt') 'kalmali'
Start-Process (Join-Path $app 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
# Kaldırıcı kendini geçici klasöre kopyalayıp oradan sürer: bitmesini bekle.
$end = (Get-Date).AddSeconds(90)
while ((Test-Path $uninstallKey) -and (Get-Date) -lt $end) { Start-Sleep 1 }
Start-Sleep 2

if (Test-Path $uninstallKey) { Fail 'kaldırma kaydı silinmedi' }
if (Test-Path (Join-Path $app 'FotoDuzenleyici.exe')) { Fail 'uygulama dosyası kaldı' }
foreach ($k in $menuKeys) { if (Test-Path $k) { Fail "sağ tık kaydı kaldı: $k" } }
if (@((UserPath) -split ';' | Where-Object { $_.TrimEnd('\') -ieq $app }).Count) { Fail 'PATH girdisi kaldı' }
if (Get-ChildItem $desktop -Filter 'Foto*.lnk') { Fail 'masaüstü kısayolu kaldı' }
if (-not (Test-Path (Join-Path $settings 'test-isareti.txt'))) { Fail 'sessiz kaldırma ayarları sildi' }
Remove-Item (Join-Path $settings 'test-isareti.txt')

Write-Host 'Kurulum testi geçti: kur (2 kez), denetle, kaldır, denetle.'
