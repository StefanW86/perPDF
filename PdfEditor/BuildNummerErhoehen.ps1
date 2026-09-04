# Wird automatisch vor jedem Build ausgefuehrt.
# Inkrementiert BuildNummer.txt und schreibt BuildInfo.g.cs.

param(
    [string]$ProjektVerzeichnis
)

$zaehler = 0
$datei = Join-Path $ProjektVerzeichnis 'BuildNummer.txt'
if (Test-Path $datei) {
    $zaehler = [int](Get-Content $datei -Raw).Trim()
}
$zaehler++
Set-Content -Path $datei -Value $zaehler -NoNewline -Encoding UTF8

$version = "1.$zaehler"
$jahr = (Get-Date).Year
$inhalt = @"
// Diese Datei wird automatisch vor jedem Build generiert - nicht manuell bearbeiten.
namespace PdfEditor;
internal static class BuildInfo
{
    public const string Version = "$version";
    public const string Jahr = "$jahr";
}
"@
$ziel = Join-Path $ProjektVerzeichnis 'BuildInfo.g.cs'
Set-Content -Path $ziel -Value $inhalt -Encoding UTF8
Write-Host "BuildInfo: Version $version"

# MSI-Version fuer das WiX-Setup erzeugen (drei Felder, da MSI major.minor.build vergleicht).
# Einzige Quelle der Wahrheit fuer die Setup-Version - vom Setup-Projekt per <?include ?> eingebunden.
$setupVerzeichnis = Join-Path $ProjektVerzeichnis '..\Setup'
if (Test-Path $setupVerzeichnis) {
    $msiVersion = "1.$zaehler.0"
    # WiX-Include-Dateien benoetigen ein <Include>-Wurzelelement.
    $wxiInhalt = "<Include><?define ProduktVersion = `"$msiVersion`" ?></Include>"
    $wxiZiel = Join-Path $setupVerzeichnis 'Version.wxi'
    # Ohne BOM schreiben, damit der WiX-Praeprozessor sauber liest.
    [System.IO.File]::WriteAllText((Resolve-Path $setupVerzeichnis).Path + '\Version.wxi', $wxiInhalt, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Setup: MSI-Version $msiVersion"
}
