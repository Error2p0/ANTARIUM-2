<#
.SYNOPSIS
  Uploads Builds/Windows/Release to Steam with SteamPipe (steamcmd) and sets it live on a branch.

.DESCRIPTION
  Build first: Unity menu Antarium -> Build -> Windows Release.
  Needs steamcmd (tools/steamcmd/steamcmd.exe, or -SteamCmd / $env:STEAMCMD) and a Steam account with
  upload rights for the app (-User or $env:STEAM_BUILDER). The first login asks for the password and the
  Steam Guard code in this window; steamcmd remembers the login afterwards.
  The 'default' branch cannot be set live from a script: do that on the Steamworks site (SteamPipe -> Builds).

.EXAMPLE
  ./steam/upload.ps1 -User mybuilder                 # upload and set live on 'beta'
  ./steam/upload.ps1 -User mybuilder -Preview        # check what would be uploaded, upload nothing
  ./steam/upload.ps1 -User mybuilder -Branch ""      # upload only, set live by hand
#>
param(
    [string]$User = $env:STEAM_BUILDER,
    [string]$Branch = "beta",
    [string]$Description = "",
    [string]$SteamCmd = $env:STEAMCMD,
    [switch]$Preview
)
$ErrorActionPreference = "Stop"

# Must match PlatformServices.SteamAppId and the depot on Steamworks (SteamPipe -> Depots).
$AppId = 4453940
$DepotId = 4453941
$Exe = "Antarium2.exe"

$root = Split-Path -Parent $PSScriptRoot
$content = Join-Path $root "Builds\Windows\Release"
if (-not (Test-Path (Join-Path $content $Exe))) { throw "No release build in $content. Build it in Unity: Antarium -> Build -> Windows Release." }
if (Test-Path (Join-Path $content "steam_appid.txt")) { throw "steam_appid.txt is in the release build: rebuild with Antarium -> Build -> Windows Release." }
if (-not $SteamCmd) { $SteamCmd = Join-Path $root "tools\steamcmd\steamcmd.exe" }
if (-not (Test-Path $SteamCmd)) { throw "steamcmd not found at $SteamCmd. See docs/STEAM.md (Upload a build)." }
if (-not $User) { throw "Give the Steam account with -User (or set STEAM_BUILDER)." }
if ($Branch -eq "default") { throw "The default branch can only be set live on the Steamworks site." }

$version = (Select-String -Path (Join-Path $root "ProjectSettings\ProjectSettings.asset") -Pattern '^\s*bundleVersion:\s*(.+)$').Matches[0].Groups[1].Value.Trim()
$commit = (git -C $root rev-parse --short HEAD 2>$null)
if (-not $Description) { $Description = "v$version ($commit)" }

$out = Join-Path $PSScriptRoot "output"
New-Item -ItemType Directory -Force $out | Out-Null
$depotVdf = Join-Path $out "depot_build_$DepotId.vdf"
$appVdf = Join-Path $out "app_build_$AppId.vdf"

@"
"DepotBuild"
{
	"DepotID" "$DepotId"
	"ContentRoot" "$content"
	"FileMapping"
	{
		"LocalPath" "*"
		"DepotPath" "."
		"Recursive" "1"
	}
	"FileExclusion" "*.pdb"
	"FileExclusion" "*DoNotShip*"
	"FileExclusion" "*DontShip*"
	"FileExclusion" "steam_appid.txt"
}
"@ | Set-Content -Encoding ascii $depotVdf

$setLive = if ($Branch) { "`"SetLive`" `"$Branch`"" } else { "" }
$previewLine = if ($Preview) { "`"Preview`" `"1`"" } else { "" }
@"
"AppBuild"
{
	"AppID" "$AppId"
	"Desc" "$Description"
	$previewLine
	$setLive
	"ContentRoot" "$content"
	"BuildOutput" "$(Join-Path $out 'log')"
	"Depots"
	{
		"$DepotId" "$depotVdf"
	}
}
"@ | Set-Content -Encoding ascii $appVdf

Write-Host "Uploading v$version from $content as '$Description'$(if ($Branch) { " -> branch $Branch" })$(if ($Preview) { ' (preview only)' })"
& $SteamCmd +login $User +run_app_build $appVdf +quit
if ($LASTEXITCODE -ne 0) { throw "steamcmd failed with exit code $LASTEXITCODE (log: $out\log)" }
Write-Host "Done. Check SteamPipe -> Builds on partner.steamgames.com."
