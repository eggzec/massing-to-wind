# Windows version of validate.sh. The lock check needs Git Bash (installed with Git for Windows).
Set-Location (Join-Path $PSScriptRoot "..")
$status = 0
function Step($name) { Write-Host ""; Write-Host "== $name" }

Step "build"; dotnet build M2W.sln -nologo -v q; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "tests"; dotnet test M2W.sln -nologo -v q --no-build; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "cases"; dotnet run --project src/M2W.Cli --no-build -- run-all cases --out out; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "locks"; bash scripts/check-locks.sh; if ($LASTEXITCODE -ne 0) { $status = 1 }

Step "summary"
if (Test-Path out/summary.json) { Get-Content out/summary.json } else { Write-Host "no out/summary.json yet" }

Write-Host ""
if ($status -eq 0) { Write-Host "VALIDATE: GREEN" } else { Write-Host "VALIDATE: RED" }
exit $status
