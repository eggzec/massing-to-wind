# One-time setup on Windows: create the solution, add every project, init git with role branches.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error "Install the .NET 8 SDK first: https://dotnet.microsoft.com/download"
}

if (-not (Test-Path M2W.sln)) { dotnet new sln -n M2W }
Get-ChildItem -Path src, tests -Recurse -Filter *.csproj | ForEach-Object { dotnet sln M2W.sln add $_.FullName }
dotnet build M2W.sln

if (-not (Test-Path .git)) {
  git init -b main
  git add -A
  git commit -m "chore: initial skeleton and interface agreement v0.1"
  git tag lock-v0.1
  foreach ($b in "gen/m2", "check/m2", "solve/m3") { git branch $b }
}
Write-Host "Setup done. Next: scripts/validate.ps1"
