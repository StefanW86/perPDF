# Baut perPDF.msix fuer die Microsoft-Store-Einreichung (unsigniert - der Store signiert).
# Aufruf:  powershell -ExecutionPolicy Bypass -File Msix\MsixBauen.ps1 [-Version X.Y.Z] [-MakeAppx <Pfad>]
# Ohne -Version zaehlt BuildNummerErhoehen.ps1 wie bei jedem lokalen Build hoch.

param(
    [string]$Version,
    [string]$MakeAppx
)
$ErrorActionPreference = 'Stop'
$msix = $PSScriptRoot
$wurzel = Split-Path $msix -Parent
$ausgabe = Join-Path $msix 'bin'
$inhalt = Join-Path $ausgabe 'Inhalt'

# Self-contained veroeffentlichen: im MSIX-Paket gibt es keine installierte .NET-Runtime als Abhaengigkeit.
if (Test-Path $ausgabe) { Remove-Item $ausgabe -Recurse -Force }
dotnet publish (Join-Path $wurzel 'PdfEditor\PdfEditor.csproj') -c Release --self-contained -o $inhalt "-p:VersionVorgabe=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

# Paketversion = Dateiversion der EXE (X.Y.Z.0; Store verlangt 0 im vierten Feld).
$paketVersion = (Get-Item (Join-Path $inhalt 'perPDF.exe')).VersionInfo.FileVersion
(Get-Content (Join-Path $msix 'AppxManifest.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $paketVersion) |
    Set-Content (Join-Path $inhalt 'AppxManifest.xml') -Encoding UTF8
Copy-Item (Join-Path $msix 'Assets') $inhalt -Recurse

if (-not $MakeAppx) {
    $MakeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $MakeAppx) { throw "makeappx.exe nicht gefunden - Windows SDK installieren oder -MakeAppx angeben" }
}
$ziel = Join-Path $ausgabe 'perPDF.msix'
& $MakeAppx pack /o /d $inhalt /p $ziel
if ($LASTEXITCODE -ne 0) { throw "makeappx fehlgeschlagen" }
Write-Host "MSIX: $ziel (Version $paketVersion)"
