# Baut perPDF.msix fuer die Microsoft-Store-Einreichung (unsigniert - der Store signiert).
# Aufruf:  powershell -ExecutionPolicy Bypass -File Msix\MsixBauen.ps1 [-Version X.Y.Z] [-MakeAppx <Pfad>]
# Ohne -Version gilt wie bei jedem Build das juengste Git-Tag vX.Y.Z (PdfEditor\VersionSchreiben.ps1).

param(
    # Leer = Tag-Version; Format prueft PdfEditor\VersionSchreiben.ps1.
    [string]$Version,
    [string]$MakeAppx
)
$ErrorActionPreference = 'Stop'
$msix = $PSScriptRoot
$wurzel = Split-Path $msix -Parent
$ausgabe = Join-Path $msix 'bin'
$inhalt = Join-Path $ausgabe 'Inhalt'

# Self-contained veroeffentlichen: im MSIX-Paket gibt es keine installierte .NET-Runtime als Abhaengigkeit.
# Eigene Output-/Intermediate-Pfade: der self-contained Build darf die Runtime nicht in den normalen
# Ausgabeordner kopieren, aus dem das MSI per Wildcard alle Dateien uebernimmt.
if (Test-Path $ausgabe) { Remove-Item $ausgabe -Recurse -Force }
dotnet publish (Join-Path $wurzel 'PdfEditor\PdfEditor.csproj') -c Release --self-contained -o $inhalt `
    "-p:VersionVorgabe=$Version" '-p:OutputPath=bin/Msix/' '-p:IntermediateOutputPath=obj/Msix/'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

# Paketversion = Dateiversion der EXE (X.Y.Z.0; Store verlangt 0 im vierten Feld).
$paketVersion = (Get-Item (Join-Path $inhalt 'perPDF.exe')).VersionInfo.FileVersion
$manifest = (Get-Content (Join-Path $msix 'AppxManifest.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $paketVersion)
# Ohne BOM schreiben, damit PowerShell 5.1 und pwsh identische Bytes erzeugen.
[System.IO.File]::WriteAllText((Join-Path $inhalt 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))
Copy-Item (Join-Path $msix 'Assets') $inhalt -Recurse

if (-not $MakeAppx) {
    # Zuerst das NuGet-Paket Microsoft.Windows.SDK.BuildTools (vom publish oben wiederhergestellt), sonst ein installiertes Windows SDK.
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $MakeAppx = @(Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe') -ErrorAction SilentlyContinue) +
        @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue) |
        Sort-Object { $_.Directory.Parent.Name -as [version] } -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $MakeAppx) { throw "makeappx.exe nicht gefunden - Windows SDK installieren oder -MakeAppx angeben" }
}
$ziel = Join-Path $ausgabe 'perPDF.msix'
& $MakeAppx pack /o /d $inhalt /p $ziel
if ($LASTEXITCODE -ne 0) { throw "makeappx fehlgeschlagen" }
Write-Host "MSIX: $ziel (Version $paketVersion)"
