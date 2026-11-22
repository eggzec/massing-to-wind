# Windows version of determinism.sh (D-045). Works in Windows PowerShell 5.1 and PowerShell 7.
#
#   scripts/determinism.ps1                  build, run `run-all cases` twice (out/determinism/run1, run2), compare,
#                                            write out/hashes.txt
#   scripts/determinism.ps1 -Compare A B     only compare two existing run-all output folders (no build, no hashes.txt)
#
# Compared by SHA-256, file by file: repaired.obj, result.json, summary.json as raw bytes; report.json with the value
# of "runtime_ms" set to 0 first (D-028). A file present in only one run is a difference. Exit 1 on any difference.
# out/hashes.txt: "caseId sha256(repaired.obj)" per line, ordinal sort, LF, UTF-8 without BOM: the same bytes that
# determinism.sh writes, so CI can compare Linux and Windows.
param(
    [switch]$Compare,
    [Parameter(Position = 0)][string]$A,
    [Parameter(Position = 1)][string]$B
)

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).ProviderPath
Set-Location $repo
$utf8 = New-Object System.Text.UTF8Encoding($false)
$sha = [System.Security.Cryptography.SHA256]::Create()
$compared = @("repaired.obj", "result.json", "report.json", "summary.json")
$script:manifestA = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)

function Get-Sha256Hex([byte[]]$bytes) {
    ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
}

# Folder -> ordinal dictionary "relative/path" -> sha256 hex.
function Get-Manifest([string]$dir) {
    $root = (Resolve-Path -LiteralPath $dir).ProviderPath.TrimEnd('\', '/')
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $compared -contains $_.Name }) {
        $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        if ($f.Name -eq "report.json") {
            $text = $utf8.GetString($bytes) -replace '"runtime_ms"[ \t]*:[ \t]*[-+0-9.eE]+', '"runtime_ms": 0'
            $bytes = $utf8.GetBytes($text)
        }
        $map[$rel] = Get-Sha256Hex $bytes
    }
    return , $map
}

function Test-SameOutput([string]$dirA, [string]$dirB) {
    foreach ($d in @($dirA, $dirB)) {
        if (-not (Test-Path -LiteralPath $d -PathType Container)) { Write-Host "determinism: FAIL - folder not found: $d"; return $false }
    }
    $ma = Get-Manifest $dirA
    $mb = Get-Manifest $dirB
    $nObj = @($ma.Keys | Where-Object { $_.EndsWith("/repaired.obj") }).Count
    if ($nObj -eq 0) { Write-Host "determinism: FAIL - no repaired.obj under $dirA, nothing to compare"; return $false }

    $keys = New-Object 'System.Collections.Generic.List[string]'
    $keys.AddRange([string[]]@($ma.Keys)); foreach ($k in $mb.Keys) { if (-not $ma.ContainsKey($k)) { $keys.Add($k) } }
    $keys.Sort([StringComparer]::Ordinal)
    $problems = New-Object 'System.Collections.Generic.List[string]'
    foreach ($k in $keys) {
        if (-not $mb.ContainsKey($k)) { $problems.Add("  only in ${dirA}: $k") }
        elseif (-not $ma.ContainsKey($k)) { $problems.Add("  only in ${dirB}: $k") }
        elseif ($ma[$k] -ne $mb[$k]) { $problems.Add("  differs:      $k") }
    }
    if ($problems.Count -eq 0) {
        Write-Host "determinism: OK - $($ma.Count) files identical ($nObj repaired.obj; report.json without runtime_ms)"
        $script:manifestA = $ma
        return $true
    }
    Write-Host "determinism: FAIL - the two runs differ:"
    $problems | ForEach-Object { Write-Host $_ }
    $script:manifestA = $ma
    return $false
}

if ($Compare) {
    if (-not $A -or -not $B) { Write-Host "usage: scripts/determinism.ps1 [-Compare DIR_A DIR_B]"; exit 2 }
    if (Test-SameOutput $A $B) { exit 0 } else { exit 1 }
}
if ($A -or $B) { Write-Host "usage: scripts/determinism.ps1 [-Compare DIR_A DIR_B]"; exit 2 }

Write-Host "determinism: build"
$log = & dotnet build M2W.sln -nologo -v q 2>&1
if ($LASTEXITCODE -ne 0) { $log | ForEach-Object { Write-Host $_ }; Write-Host "determinism: FAIL - build failed"; exit 1 }

$detDir = Join-Path $repo "out/determinism"
if (Test-Path -LiteralPath $detDir) { Remove-Item -LiteralPath $detDir -Recurse -Force }
New-Item -ItemType Directory -Force $detDir | Out-Null
foreach ($run in @("run1", "run2")) {
    Write-Host "determinism: run-all cases --out out/determinism/$run"
    $log = & dotnet run --project src/M2W.Cli --no-build -- run-all cases --out "out/determinism/$run" 2>&1
    if ($LASTEXITCODE -ne 0) {
        $log | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
        Write-Host "determinism: FAIL - run-all failed ($run)"; exit 1
    }
}

$ok = Test-SameOutput "out/determinism/run1" "out/determinism/run2"

# hashes.txt from run1 (equal to run2 when the check passed): "caseId sha256", caseId = output folder of the case.
$lines = New-Object 'System.Collections.Generic.List[string]'
foreach ($k in $script:manifestA.Keys) {
    if ($k.EndsWith("/repaired.obj")) { $lines.Add($k.Substring(0, $k.Length - "/repaired.obj".Length) + " " + $script:manifestA[$k]) }
}
$lines.Sort([StringComparer]::Ordinal)
$text = ""; if ($lines.Count -gt 0) { $text = ($lines -join "`n") + "`n" }
[System.IO.File]::WriteAllText((Join-Path $repo "out/hashes.txt"), $text, $utf8)
Write-Host "determinism: wrote out/hashes.txt ($($lines.Count) cases)"
if ($ok) { exit 0 } else { exit 1 }
