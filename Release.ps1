# Erstellt ein Release - nur auf ausdruecklichen Wunsch ausfuehren.
# Zaehlt die Version hoch (neues Git-Tag vX.Y.Z = einzige Versionsquelle) und pusht das Tag.
# Der Release-Workflow (.github\workflows\release.yml) baut daraus EXE, MSI und MSIX mit genau
# dieser Version und veroeffentlicht sie. Lokale Builds uebernehmen die Version ab dann ebenfalls.
# Aufruf:  powershell -ExecutionPolicy Bypass -File Release.ps1 [-Version X.Y.Z]
# Ohne -Version: mittlere Stelle des letzten Tags + 1 (v1.109.0 -> v1.110.0).

param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# git.exe statt git: PowerShell unterscheidet keine Groß-/Kleinschreibung, "git" riefe die Funktion selbst auf.
function Git { git.exe @args; if ($LASTEXITCODE -ne 0) { throw "git $args fehlgeschlagen" } }

if (Git status --porcelain) { throw "Arbeitsverzeichnis nicht sauber - erst committen." }
if ((Git rev-parse --abbrev-ref HEAD) -ne 'main') { throw "Releases nur von main." }
Git fetch --tags origin
if ((Git rev-parse HEAD) -ne (Git rev-parse origin/main)) { throw "main ist nicht identisch mit origin/main - erst pushen bzw. pullen." }

$letztes = (Git describe --tags --abbrev=0 --match 'v*').TrimStart('v')
if (-not $Version) {
    $teile = $letztes.Split('.')
    $Version = "$($teile[0]).$([int]$teile[1] + 1).0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Ungueltige Version '$Version' - erwartet X.Y.Z." }
if ([version]$Version -le [version]$letztes) { throw "Version $Version ist nicht hoeher als das letzte Release $letztes (Store/MSI verlangen steigende Versionen)." }

# Vor dem Tag pruefen, dass der Stand baut.
dotnet build (Join-Path $PSScriptRoot 'PdfEditor.sln') -c Release "-p:VersionVorgabe=$Version"
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen - kein Release erstellt." }

Git tag "v$Version"
Git push origin "v$Version"
Write-Host "Release v$Version angestossen (vorher $letztes): https://github.com/StefanW86/perPDF/actions"
