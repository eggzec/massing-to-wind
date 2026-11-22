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
    # git.exe may be Git\cmd, Git\bin or Git\mingw64\bin (PATH order decides). Walk up from its folder, at most
    # three levels, and take the first bin\bash.exe (then usr\bin\bash.exe): all three resolve to Git\bin\bash.exe.
    $dir = Split-Path $gitExe.Source -Parent
    for ($i = 0; $i -lt 3 -and $dir -and -not $gitBash; $i++) {
        foreach ($candidate in @((Join-Path $dir "bin\bash.exe"), (Join-Path $dir "usr\bin\bash.exe"))) {
            if (Test-Path $candidate) { $gitBash = $candidate; break }
        }
        $dir = Split-Path $dir -Parent
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
