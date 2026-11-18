# Windows version of validate.sh. The lock check needs Git Bash (installed with Git for Windows).
Set-Location (Join-Path $PSScriptRoot "..")
$status = 0
function Step($name) { Write-Host ""; Write-Host "== $name" }

Step "build"; dotnet build M2W.sln -nologo -v q; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "tests"; dotnet test M2W.sln -nologo -v q --no-build; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "cases"; dotnet run --project src/M2W.Cli --no-build -- run-all cases --out out; if ($LASTEXITCODE -ne 0) { $status = 1 }
Step "locks"
# Use Git for Windows' bash explicitly: plain "bash" may resolve to WSL, whose git cannot read Windows worktrees.
$gitBash = $null
$gitExe = Get-Command git -ErrorAction SilentlyContinue
if ($gitExe) {
    $gitRoot = Split-Path (Split-Path $gitExe.Source -Parent) -Parent   # ...\Git\cmd\git.exe -> ...\Git
    foreach ($candidate in @((Join-Path $gitRoot "bin\bash.exe"), (Join-Path $gitRoot "usr\bin\bash.exe"))) {
        if (Test-Path $candidate) { $gitBash = $candidate; break }
    }
}
if ($gitBash) {
    & $gitBash scripts/check-locks.sh; if ($LASTEXITCODE -ne 0) { $status = 1 }
} else {
    Write-Host "locks: FAIL - Git Bash not found next to git.exe; install Git for Windows or run scripts/validate.sh in Git Bash"
    $status = 1
}

Step "summary"
if (Test-Path out/summary.json) { Get-Content out/summary.json } else { Write-Host "no out/summary.json yet" }

Write-Host ""
if ($status -eq 0) { Write-Host "VALIDATE: GREEN" } else { Write-Host "VALIDATE: RED" }
exit $status
