# Wird automatisch vor jedem Build ausgefuehrt.
# Einzige Quelle der Wahrheit fuer die Version: schreibt BuildInfo.g.cs (Anzeige in der App +
# Assembly-/Dateiversion der perPDF.exe) und Setup\Version.wxi (MSI-Version).
# Ohne -Version (lokaler Build): BuildNummer.txt inkrementieren, Version = 1.<Nummer>.0.
# Mit -Version (Release-Build in CI, aus dem Git-Tag vX.Y.Z): genau diese Version verwenden.

param(
    [string]$ProjektVerzeichnis,
    [string]$Version
)

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Ungueltige Version '$Version' - erwartet wird X.Y.Z (z. B. 1.109.0)."
    }
} else {
    $zaehler = 0
    $datei = Join-Path $ProjektVerzeichnis 'BuildNummer.txt'
    if (Test-Path $datei) {
        $zaehler = [int](Get-Content $datei -Raw).Trim()
    }
    $zaehler++
    Set-Content -Path $datei -Value $zaehler -NoNewline -Encoding UTF8
    $Version = "1.$zaehler.0"
}

$jahr = (Get-Date).Year
$inhalt = @"
// Diese Datei wird automatisch vor jedem Build generiert - nicht manuell bearbeiten.
[assembly: System.Reflection.AssemblyVersion("$Version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$Version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$Version")]
namespace PdfEditor;
internal static class BuildInfo
{
    public const string Version = "$Version";
    public const string Jahr = "$jahr";
}
"@
$ziel = Join-Path $ProjektVerzeichnis 'BuildInfo.g.cs'
Set-Content -Path $ziel -Value $inhalt -Encoding UTF8
Write-Host "BuildInfo: Version $Version"

# MSI-Version fuer das WiX-Setup (drei Felder, da MSI major.minor.build vergleicht).
$setupVerzeichnis = Join-Path $ProjektVerzeichnis '..\Setup'
if (Test-Path $setupVerzeichnis) {
    # WiX-Include-Dateien benoetigen ein <Include>-Wurzelelement.
    $wxiInhalt = "<Include><?define ProduktVersion = `"$Version`" ?></Include>"
    # Ohne BOM schreiben, damit der WiX-Praeprozessor sauber liest.
    [System.IO.File]::WriteAllText((Resolve-Path $setupVerzeichnis).Path + '\Version.wxi', $wxiInhalt, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Setup: MSI-Version $Version"
}
