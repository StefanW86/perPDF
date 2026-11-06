# Baut perPDF.msix fuer die Microsoft-Store-Einreichung (unsigniert - der Store signiert).
# Aufruf:  powershell -ExecutionPolicy Bypass -File Msix\MsixBauen.ps1 -Version X.Y.Z [-MakeAppx <Pfad>]
# -Version ist Pflicht und sollte dem Release-Tag entsprechen: der Store verlangt pro Einreichung
# eine hoehere Version, ein lokaler Buildzaehler wuerde sich mit den Tag-Versionen ueberholen.

param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
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
$manifest = (Get-Content (Join-Path $msix 'AppxManifest.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $paketVersion)
# Ohne BOM schreiben, damit PowerShell 5.1 und pwsh identische Bytes erzeugen.
[System.IO.File]::WriteAllText((Join-Path $inhalt 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))
Copy-Item (Join-Path $msix 'Assets') $inhalt -Recurse

if (-not $MakeAppx) {
    $MakeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object { $_.Directory.Parent.Name -as [version] } -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $MakeAppx) { throw "makeappx.exe nicht gefunden - Windows SDK installieren oder -MakeAppx angeben" }
}
$ziel = Join-Path $ausgabe 'perPDF.msix'
& $MakeAppx pack /o /d $inhalt /p $ziel
if ($LASTEXITCODE -ne 0) { throw "makeappx fehlgeschlagen" }
Write-Host "MSIX: $ziel (Version $paketVersion)"
