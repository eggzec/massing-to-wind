# Windows version of install-tool.sh: packs this checkout as the m2w .NET tool and installs it for the current user,
# replacing any installed m2w. Run again after changing the code.
# Remove with: dotnet tool uninstall -g MassingToWind.Tool
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

# A new version each time, so NuGet never reuses a cached older build.
$suffix = "local." + (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
if (Test-Path out/pkg) { Remove-Item -Recurse -Force out/pkg }
dotnet pack src/M2W.Cli -c Release -nologo -v q -o out/pkg --version-suffix $suffix
if ($LASTEXITCODE -ne 0) { exit 1 }
$version = (Get-Item out/pkg/MassingToWind.Tool.*.nupkg).BaseName -replace '^MassingToWind\.Tool\.', ''

if (dotnet tool list -g | Select-String '^massingtowind\.tool ') {
    dotnet tool uninstall -g MassingToWind.Tool
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
dotnet tool install -g MassingToWind.Tool --version $version --add-source out/pkg
if ($LASTEXITCODE -ne 0) { exit 1 }
Write-Host "Try: m2w --help   (if m2w is not found, open a new terminal)"
