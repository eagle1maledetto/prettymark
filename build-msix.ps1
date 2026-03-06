#Requires -Version 5.1
<#
.SYNOPSIS
    Build PrettyMark MSIX package for Microsoft Store or sideloading.

.DESCRIPTION
    1. Runs dotnet publish (self-contained, single-file, win-x64)
    2. Creates MSIX staging directory with AppxManifest.xml + visual assets
    3. Generates resources.pri via makepri.exe
    4. Creates .msix package via makeappx.exe
    5. Optionally signs with a self-signed certificate (for testing/sideloading)

.PARAMETER Version
    Package version (e.g., "1.0.0.0"). Must be four-part.

.PARAMETER SkipBuild
    Skip dotnet publish (use existing build output).

.PARAMETER Sign
    Create and apply a self-signed certificate for testing.

.EXAMPLE
    .\build-msix.ps1 -Version "1.0.0.0"
    .\build-msix.ps1 -Version "1.1.0.0" -Sign
    .\build-msix.ps1 -Version "1.0.0.0" -SkipBuild
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$SkipBuild,
    [switch]$Sign
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$PublishDir = "$ProjectDir\bin\Release\net8.0-windows\win-x64\publish"
$StagingDir = "$ProjectDir\bin\msix-staging"
$OutputDir = "$ProjectDir\bin\msix"
$OutputFile = "$OutputDir\PrettyMark-$Version-win-x64.msix"

# --- Configuration (update before Store submission) ---
$PackageName = "Eagle1.PrettyMark"
$PackageDisplayName = "PrettyMark"
$Publisher = "CN=Eagle1"  # Must match your Partner Center identity for Store
$PublisherDisplayName = "Eagle1"
$Description = "Markdown viewer with live reload and syntax highlighting"

# --- Find Windows SDK tools ---
function Find-SdkTool($name) {
    $sdkRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )
    foreach ($root in $sdkRoots) {
        if (Test-Path $root) {
            $found = Get-ChildItem -Path $root -Recurse -Filter $name -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -match "\\x64\\" } |
                     Sort-Object { $_.Directory.Name } -Descending |
                     Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    return $null
}

$makeappx = Find-SdkTool "makeappx.exe"
$makepri = Find-SdkTool "makepri.exe"
$signtool = Find-SdkTool "signtool.exe"

if (-not $makeappx) {
    Write-Error "makeappx.exe not found. Install Windows 10/11 SDK: https://developer.microsoft.com/windows/downloads/windows-sdk/"
}
if (-not $makepri) {
    Write-Error "makepri.exe not found. Install Windows 10/11 SDK."
}
Write-Host "Using SDK tools:" -ForegroundColor Cyan
Write-Host "  makeappx: $makeappx"
Write-Host "  makepri:  $makepri"
if ($signtool) { Write-Host "  signtool: $signtool" }

# --- Step 1: Build ---
if (-not $SkipBuild) {
    Write-Host "`n=== Building PrettyMark ===" -ForegroundColor Green
    Push-Location $ProjectDir
    dotnet publish -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Error "Build failed." }
    Pop-Location
}

if (-not (Test-Path "$PublishDir\PrettyMark.exe")) {
    Write-Error "PrettyMark.exe not found in $PublishDir. Run without -SkipBuild."
}

# --- Step 2: Check visual assets ---
$AssetsDir = "$ProjectDir\assets\msix"
$requiredAssets = @(
    "StoreLogo.png",          # 50x50
    "Square44x44Logo.png",    # 44x44
    "Square150x150Logo.png"   # 150x150
)

$missingAssets = $requiredAssets | Where-Object { -not (Test-Path "$AssetsDir\$_") }
if ($missingAssets) {
    Write-Host "`n=== Missing MSIX visual assets ===" -ForegroundColor Yellow
    Write-Host "Create the following PNG files in assets\msix\:" -ForegroundColor Yellow
    Write-Host "  StoreLogo.png          - 50x50 px (Store listing icon)" -ForegroundColor Yellow
    Write-Host "  Square44x44Logo.png    - 44x44 px (taskbar, Start menu small)" -ForegroundColor Yellow
    Write-Host "  Square150x150Logo.png  - 150x150 px (Start menu tile)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Use your app logo on a transparent background." -ForegroundColor Yellow
    Write-Error "Missing assets: $($missingAssets -join ', '). Create them in assets\msix\ and retry."
}

# --- Step 3: Create staging directory ---
Write-Host "`n=== Staging MSIX content ===" -ForegroundColor Green

if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null
New-Item -ItemType Directory -Path "$StagingDir\Assets" -Force | Out-Null

# Copy exe
Copy-Item "$PublishDir\PrettyMark.exe" "$StagingDir\"

# Copy visual assets
foreach ($asset in $requiredAssets) {
    Copy-Item "$AssetsDir\$asset" "$StagingDir\Assets\"
}

# Copy app icon for file association
if (Test-Path "$ProjectDir\assets\favicon.ico") {
    Copy-Item "$ProjectDir\assets\favicon.ico" "$StagingDir\Assets\"
}

# --- Step 4: Generate AppxManifest.xml ---
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap3 rescap">

  <Identity
    Name="$PackageName"
    Version="$Version"
    Publisher="$Publisher"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>$PackageDisplayName</DisplayName>
    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <Description>$Description</Description>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-US" />
  </Resources>

  <Applications>
    <Application Id="PrettyMark"
      Executable="PrettyMark.exe"
      EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="$PackageDisplayName"
        Description="$Description"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <uap:Extension Category="windows.fileTypeAssociation">
          <uap:FileTypeAssociation Name="markdown">
            <uap:SupportedFileTypes>
              <uap:FileType>.md</uap:FileType>
              <uap:FileType>.markdown</uap:FileType>
              <uap:FileType>.txt</uap:FileType>
            </uap:SupportedFileTypes>
            <uap:DisplayName>Markdown File</uap:DisplayName>
            <uap:Logo>Assets\Square44x44Logo.png</uap:Logo>
          </uap:FileTypeAssociation>
        </uap:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

$manifest | Out-File -FilePath "$StagingDir\AppxManifest.xml" -Encoding utf8

# --- Step 5: Generate resources.pri ---
Write-Host "`n=== Generating resources.pri ===" -ForegroundColor Green

# Create priconfig.xml
Push-Location $StagingDir
& $makepri createconfig /cf "$StagingDir\priconfig.xml" /dq en-US /o
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Error "makepri createconfig failed." }

& $makepri new /pr "$StagingDir" /cf "$StagingDir\priconfig.xml" /of "$StagingDir\resources.pri" /o
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Error "makepri new failed." }
Pop-Location

# Clean up config file
Remove-Item "$StagingDir\priconfig.xml" -ErrorAction SilentlyContinue

# --- Step 6: Create MSIX ---
Write-Host "`n=== Creating MSIX package ===" -ForegroundColor Green

if (Test-Path $OutputDir) { Remove-Item "$OutputDir\*.msix" -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

& $makeappx pack /d "$StagingDir" /p "$OutputFile" /o
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx pack failed." }

Write-Host "  Created: $OutputFile" -ForegroundColor Cyan

# --- Step 7: Sign (optional) ---
if ($Sign) {
    if (-not $signtool) {
        Write-Warning "signtool.exe not found. Cannot sign. Install Windows SDK."
    } else {
        Write-Host "`n=== Signing MSIX (self-signed, for testing) ===" -ForegroundColor Green

        $certSubject = $Publisher
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $certSubject `
            -KeyUsage DigitalSignature `
            -FriendlyName "PrettyMark Test Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

        $thumbprint = $cert.Thumbprint
        Write-Host "  Certificate thumbprint: $thumbprint"

        & $signtool sign /fd SHA256 /sha1 $thumbprint /td SHA256 "$OutputFile"
        if ($LASTEXITCODE -ne 0) { Write-Error "signtool sign failed." }

        Write-Host ""
        Write-Host "  To install the signed MSIX, you must first trust the certificate:" -ForegroundColor Yellow
        Write-Host "  1. Export: Export-Certificate -Cert Cert:\CurrentUser\My\$thumbprint -FilePath PrettyMark-test.cer" -ForegroundColor Yellow
        Write-Host "  2. Install: certutil -addstore TrustedPeople PrettyMark-test.cer" -ForegroundColor Yellow
        Write-Host "  3. Then double-click the .msix to install" -ForegroundColor Yellow
    }
}

# --- Done ---
$size = [math]::Round((Get-Item $OutputFile).Length / 1MB, 1)
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "  Output:  $OutputFile ($size MB)"
Write-Host ""
if (-not $Sign) {
    Write-Host "  For testing (sideload): re-run with -Sign" -ForegroundColor Cyan
    Write-Host "  For Microsoft Store: upload the .msix to Partner Center" -ForegroundColor Cyan
    Write-Host "    (Microsoft signs it for you; no self-signed cert needed)" -ForegroundColor Cyan
}
