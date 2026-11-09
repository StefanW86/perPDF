# Wird automatisch vor jedem Build ausgefuehrt.
# Einzige Quelle der Wahrheit fuer die Version ist das Git-Tag vX.Y.Z. Das Skript schreibt
# BuildInfo.g.cs (Anzeige in der App + Assembly-/Dateiversion der perPDF.exe, daraus die
# MSIX-Paketversion). Die Datei ist generiert und
# nicht eingecheckt, damit im Repo keine abweichende Versionsnummer stehen kann.
# Ohne -Version: juengstes von HEAD erreichbares Tag (git describe).
# Mit -Version (CI aus dem ausgeloesten Tag, MsixBauen.ps1): genau diese Version.

param(
    [string]$ProjektVerzeichnis,
    [string]$Version
)
$ErrorActionPreference = 'Stop'

if (-not $Version) {
    $tag = git -C $ProjektVerzeichnis describe --tags --abbrev=0 --match 'v*' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $tag) {
        throw "Keine Version ermittelbar: kein Git-Tag vX.Y.Z gefunden. Version per -p:VersionVorgabe=X.Y.Z angeben."
    }
    $Version = $tag.Trim().TrimStart('v')
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Ungueltige Version '$Version' - erwartet wird X.Y.Z (z. B. 1.109.0)."
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
