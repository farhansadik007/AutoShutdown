# Store submission kit

Everything needed to publish AutoShutdown to the Microsoft Store as an MSIX.

```
store/
├── AppxManifest.xml        package manifest (identity placeholders inside)
├── pack.ps1                builds the MSIX (makeappx if present, else built-in packer)
├── AutoShutdown.ico        app icon (generated with --export-icon)
├── AutoShutdown.msix       the built package (recreated by pack.ps1)
├── assets/                 manifest logo battery (generated with --export-store-assets)
│   ├── Square150x150Logo.png            Start tile (teal + power glyph)
│   ├── Square310x310Logo.png
│   ├── Wide310x150Logo.png
│   ├── StoreLogo.png
│   ├── Square44x44Logo(.targetsize-*, .scale-*)   plate icons 16→256
│   └── SplashScreen.png
├── raw/                    2x window screenshots used to compose marketing art
├── screenshot-desktop-*.png         1368x920 Partner Center screenshots
└── hero-still.png                   1920x1080 Store hero still
```

## Building the MSIX

```powershell
cd store
.\pack.ps1
# optionally: .\pack.ps1 -Version 1.2.0.2
```

Requires the .NET 10 SDK. `pack.ps1` prefers `makeappx.exe` from the Windows
SDK when it is installed, and otherwise falls back to AutoShutdown's own
built-in OPC packer (`--pack-msix`), so **no Windows SDK is needed**.
The publish is **self-contained**, so the package runs on any Windows 10+
machine without the .NET Desktop Runtime.

The output is `store\AutoShutdown.msix` (~67 MB).

## Before you submit

Product identity is already registered in Partner Center and wired into
`AppxManifest.xml` (Store ID **9NSHBM3HQ515**; the store deep link is
`https://apps.microsoft.com/detail/9NSHBM3HQ515?mode=direct`).

1. Reserve the name **AutoShutdown** and fill in the store listing details.
2. Upload `AutoShutdown.msix` in *Packages* — the Store re-signs it, so it may
   be unsigned locally.
3. Upload the screenshots from this folder (1368×920) and `hero-still.png`
   to the listing *Images* section.

## Testing the package locally (sideload)

```powershell
# one-time: create a self-signed cert matching the registered publisher CN
New-SelfSignedCertificate -Type Custom -Subject "CN=E029A42B-BCDE-43A9-9E99-03026508BB24" `
  -KeyUsage DigitalSignature -FriendlyName "AutoShutdown" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  | Export-PfxCertificate -FilePath cert.pfx -Password (ConvertTo-SecureString "pass" -AsPlainText -Force)

Import-Certificate -FilePath cert.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
# then (with signing)
.\pack.ps1 -Sign -CertificatePath cert.pfx -CertificatePassword pass
Add-AppxPackage .\AutoShutdown.msix
```

## Regenerating the artwork

The kit is reproducible from the build itself - no image editors involved:

```powershell
$exe = "..\bin\Debug\net10.0-windows\AutoShutdown.exe"
& $exe --export-icon "$PSScriptRoot\AutoShutdown.ico"
& $exe --shot "$PSScriptRoot\raw\setup-raw.png" --shot-zoom 2
& $exe --demo --demo-seconds 60 --shot "$PSScriptRoot\raw\countdown-raw.png" --shot-zoom 2 --shot-delay 3200
& $exe --export-store-assets "$PSScriptRoot"
```
