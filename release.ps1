# Builds a new version and prepares the two files clients' apps look for.
#
#   .\release.ps1 -Version 0.2.0 -Notes "Added weapon levels" -Repo yourname/mastery-camo-tracker
#   .\release.ps1 -Version 0.3.0 -Notes "Security fix" -Repo yourname/mastery-camo-tracker -Required
#
# Output: .\release\MasteryCamoTracker.exe and .\release\update.json
# Upload both to a GitHub Release (any tag, e.g. v0.2.0). Clients update the next time they open the app.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Notes = "",
    [switch]$Required
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here "MasteryTracker.csproj"
$out = Join-Path $here "release"

# 1. stamp the version into the project
$xml = Get-Content -Raw -Encoding UTF8 $proj
$xml = [regex]::Replace($xml, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($proj, $xml, (New-Object Text.UTF8Encoding($false)))

# 2. build the single-file exe
dotnet publish $proj -c Release -r win-x64 -o $out
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Get-ChildItem $out | Where-Object { $_.Name -ne "MasteryCamoTracker.exe" } | Remove-Item -Force -ErrorAction SilentlyContinue

# 3. the manifest the apps read. "latest/download" always points at the newest release.
$manifest = [ordered]@{
    version  = $Version
    url      = "https://github.com/$Repo/releases/latest/download/MasteryCamoTracker.exe"
    notes    = $Notes
    required = [bool]$Required
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $out "update.json"), $manifest, (New-Object Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Ready in $out" -ForegroundColor Green
Write-Host "Upload MasteryCamoTracker.exe and update.json to a new GitHub Release for tag v$Version."
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Or run:  gh release create v$Version `"$out\MasteryCamoTracker.exe`" `"$out\update.json`" --repo $Repo --title v$Version --notes `"$Notes`""
}
