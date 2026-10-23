#
# Packs AutoShutdown into an MSIX (Microsoft Store submission package).
#
# Usage:
#   .\pack.ps1                          # unsigned AutoShutdown.msix
#   .\pack.ps1 -Version 1.2.0.0
#   .\pack.ps1 -Sign -CertificatePath .\my.pfx -CertificatePassword 1234
#
# Signing is not required for the Microsoft Store (the Store re-signs), only
# for local sideloading / testing outside the Store.
#
param(
    [string]$Version = "",

    [switch]$Sign,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$layout = Join-Path $root "layout"
$msix = Join-Path $root "AutoShutdown.msix"

Write-Host "== AutoShutdown MSIX pack ==" -ForegroundColor Cyan

if ($Version -ne "") {
    $manifestPath = Join-Path $root "AppxManifest.xml"
    $manifest = Get-Content $manifestPath -Raw
    $manifest = $manifest -replace 'Version="[^"]*"', "Version=`"$Version`""
    Set-Content $manifestPath $manifest -NoNewline
    Write-Host "Manifest version set to $Version"
}

# 1. Build the release payload (self-contained: runs on any Windows 10/11 box).
$publish = Join-Path $root "publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Write-Host "Publishing (Release, self-contained)..." -ForegroundColor DarkGray
dotnet publish (Join-Path $root "..\AutoShutdown.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -o $publish -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2. Compose the package layout.
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
Copy-Item (Join-Path $root "AppxManifest.xml") (Join-Path $layout "AppxManifest.xml")
Copy-Item (Join-Path $root "assets") (Join-Path $layout "assets") -Recurse
Get-ChildItem $publish -Force | Copy-Item -Destination $layout -Recurse -Force
Write-Host "Layout ready: $layout" -ForegroundColor DarkGray

# 3. Locate makeappx.exe from the Windows 10/11 SDK (if installed).
$kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makeappx = Get-ChildItem $kits -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*x64*" } |
    Sort-Object DirectoryName -Descending |
    Select-Object -First 1

# 4. Pack - via makeappx when available, otherwise the app's built-in OPC packer.
$pubExe = Join-Path $publish "AutoShutdown.exe"

if ($makeappx) {
    Write-Host "Using makeappx: $($makeappx.FullName)" -ForegroundColor DarkGray
    if (Test-Path $msix) { Remove-Item $msix -Force }
    & $makeappx.FullName pack /o /d $layout /p $msix | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
} else {
    if (-not (Test-Path $pubExe)) { throw "publish output missing: $pubExe" }
    Write-Host "makeappx.exe not found (no Windows SDK) - packing with AutoShutdown's built-in OPC packer." -ForegroundColor Yellow
    if (Test-Path $msix) { Remove-Item $msix -Force }
    $proc = Start-Process -FilePath $pubExe -ArgumentList @("--pack-msix", $layout, $msix) -Wait -PassThru
    if ($proc.ExitCode -ne 0 -or -not (Test-Path $msix)) { throw "MSIX packing failed" }
}
Write-Host "MSIX written: $msix" -ForegroundColor Green

# 5. Optional signing for sideload installs.
if ($Sign) {
    if (-not $CertificatePath) { throw "-Sign needs -CertificatePath" }
    $signtool = Get-ChildItem $kits -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*x64*" } |
        Sort-Object DirectoryName -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found - install the Windows 10/11 SDK." }
    $args = @("sign", "/fd", "SHA256", "/f", $CertificatePath)
    if ($CertificatePassword -ne "") { $args += @("/p", $CertificatePassword) }
    $args += $msix
    & $signtool.FullName @args | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Write-Host "Signed." -ForegroundColor Green
}

Write-Host ""
Write-Host "Install locally:  add the cert to Trusted People, then Add-AppxPackage AutoShutdown.msix"
Write-Host "Store:            upload AutoShutdown.msix to Partner Center (no signature needed)."
