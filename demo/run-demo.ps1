# Massing-to-Wind demo runner (owner tooling, D-053). Not part of the build, the tests or CI.
#
#   powershell -ExecutionPolicy Bypass -File demo\run-demo.ps1                  pauses before each step (recording)
#   powershell -ExecutionPolicy Bypass -File demo\run-demo.ps1 -Case case_0105  another visible case
#   ... -NoPause -NoParaView -NoBrowser                                         run straight through (rehearsal, tests)
#   ... -Step 4                                                                 start at a later step
#
# Steps: 1 the problem, 2 repair, 3 independent check, 4 before/after in ParaView, 5 every visible case,
#        6 CI on GitHub.
# Writes only under out\demo (git-ignored). Needs: the .NET 8 SDK; ParaView 6.x for step 4 (found under
# C:\Program Files\ParaView*, or pass -ParaViewBin); the GitHub CLI for step 6 (optional).
param(
    [string]$Case = "case_0191",
    [int]$Step = 1,
    [switch]$NoPause,
    [switch]$NoParaView,
    [switch]$NoBrowser,
    [string]$ParaViewBin = ""
)

$ErrorActionPreference = "Continue"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).ProviderPath
Set-Location $repo
$demo = Join-Path $repo "out\demo"
$caseDir = Join-Path $repo "cases\$Case"
$outCase = Join-Path $demo $Case
$ciUrl = "https://github.com/eggzec/massing-to-wind/actions/workflows/ci.yml"

function Show-Step([int]$n, [string]$title, [string]$line) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor DarkCyan
    Write-Host ("  {0}. {1}" -f $n, $title) -ForegroundColor Cyan
    if ($line) { Write-Host ("     " + $line) -ForegroundColor Gray }
    Write-Host ("=" * 78) -ForegroundColor DarkCyan
}
function Wait-Next {
    if (-not $NoPause) { Write-Host ""; [void](Read-Host "  [Enter] next step") }
}
function Show-Cmd([string]$text) { Write-Host ""; Write-Host ("  > " + $text) -ForegroundColor Yellow }
function Show-Kv([string]$k, [string]$v, [string]$colour = "White") {
    Write-Host ("    {0,-22} " -f $k) -NoNewline -ForegroundColor Gray; Write-Host $v -ForegroundColor $colour
}
function Stop-Demo([string]$why) { Write-Host ""; Write-Host ("  demo stopped: " + $why) -ForegroundColor Red; exit 1 }

if (-not (Test-Path (Join-Path $caseDir "case.json"))) { Stop-Demo "no visible case $Case under cases\" }

# ---------------------------------------------------------------- build (Release, quiet)
Write-Host ""
Write-Host "  Massing-to-Wind  |  generative massings -> closed, wind-ready shells  |  pure C# (.NET 8)" -ForegroundColor White
Write-Host "  building..." -ForegroundColor DarkGray
$log = & dotnet build src/M2W.Cli -c Release -nologo -v q 2>&1
if ($LASTEXITCODE -ne 0) { $log | ForEach-Object { Write-Host $_ }; Stop-Demo "build failed" }
$m2w = Join-Path $repo "src\M2W.Cli\bin\Release\net8.0\m2w.exe"
New-Item -ItemType Directory -Force $demo | Out-Null

$caseJson = Get-Content (Join-Path $caseDir "case.json") -Raw | ConvertFrom-Json
$tol = $caseJson.tolerances

# ---------------------------------------------------------------- 1. the problem
if ($Step -le 1) {
    Show-Step 1 "The problem" "A massing from a generative design tool, as it comes out: $Case"
    Show-Cmd "m2w render cases\$Case\input.obj"
    $msg = & $m2w render (Join-Path $caseDir "input.obj") --out (Join-Path $demo "$Case-input.svg") 2>&1
    $m = [regex]::Match(($msg -join " "), "(\d+) object\(s\), (\d+) polygon\(s\), (\d+) free edge\(s\), (\d+) flipped face\(s\)")
    if ($m.Success) {
        Show-Kv "input objects" $m.Groups[1].Value
        Show-Kv "free edges" $m.Groups[3].Value $(if ([int]$m.Groups[3].Value -gt 0) { "Red" } else { "White" })
        Show-Kv "flipped faces" $m.Groups[4].Value $(if ([int]$m.Groups[4].Value -gt 0) { "Magenta" } else { "White" })
    }
    $expected = Join-Path $caseDir "expected.json"
    if (Test-Path $expected) {
        $faults = (Get-Content $expected -Raw | ConvertFrom-Json).faultsInjected -join ", "
        Show-Kv "faults injected" "$faults   (generator's answer key; the repair never reads it)" "DarkYellow"
    }
    Show-Kv "tolerances" ("minGap {0} m, minEdge {1} m, weld {2} m, groundSnap {3} m" -f $tol.minGap, $tol.minEdge, $tol.weld, $tol.groundSnap)
    Write-Host ""
    Write-Host "    A CFD mesher needs closed, non-intersecting shells. This is not one." -ForegroundColor Gray
    Wait-Next
}

# ---------------------------------------------------------------- 2. repair
if ($Step -le 2) {
    Show-Step 2 "Repair" "Reads only input.obj and case.json."
    Show-Cmd "m2w repair cases\$Case --out out\demo"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $m2w repair $caseDir --out $demo 2>$null | Out-Null
    $code = $LASTEXITCODE; $sw.Stop()
    if (-not (Test-Path (Join-Path $outCase "report.json"))) { Stop-Demo "repair wrote no report.json (exit $code)" }
    $report = Get-Content (Join-Path $outCase "report.json") -Raw | ConvertFrom-Json
    $statusColour = "Green"; if ($report.status -ne "repaired") { $statusColour = "Yellow" }
    Show-Kv "status" $report.status $statusColour
    Show-Kv "closed shells" $report.shellCount
    Show-Kv "time" ("{0} ms in the engine ({1} ms with start-up)" -f $report.runtime_ms, $sw.ElapsedMilliseconds)
    Write-Host ""
    Write-Host "    operations, in order:" -ForegroundColor Gray
    $seen = New-Object 'System.Collections.Generic.List[string]'
    foreach ($op in $report.operations) { if (-not $seen.Contains($op.op)) { $seen.Add($op.op) } }
    foreach ($name in $seen) {
        $ops = @($report.operations | Where-Object { $_.op -eq $name })
        $n = ($ops | Measure-Object -Property count -Sum).Sum
        $times = ""; if ($ops.Count -gt 1) { $times = " ({0} times)" -f $ops.Count }
        Write-Host ("      {0,-22} {1}{2}" -f $name, $n, $times)
    }
    if ($report.warnings.Count -gt 0) {
        Write-Host ""
        Write-Host ("    warnings: {0} (located; see report.json)" -f $report.warnings.Count) -ForegroundColor DarkYellow
    }
    Wait-Next
}

# ---------------------------------------------------------------- 3. check
if ($Step -le 3) {
    Show-Step 3 "Independent check" "A separate checker grades the output against the answer key and the validity rules."
    Show-Cmd "m2w check cases\$Case --out out\demo"
    & $m2w check $caseDir --out $demo 2>$null | Out-Null
    if (-not (Test-Path (Join-Path $outCase "result.json"))) { Stop-Demo "check wrote no result.json" }
    $result = Get-Content (Join-Path $outCase "result.json") -Raw | ConvertFrom-Json
    $inv = [Globalization.CultureInfo]::InvariantCulture
    foreach ($c in $result.checks) {
        $mark = "PASS"; $colour = "Green"; if (-not $c.pass) { $mark = "FAIL"; $colour = "Red" }
        Write-Host ("    {0}  " -f $mark) -NoNewline -ForegroundColor $colour
        # Volume, footprint and height compare each shell with the answer key: the check's "limit" is then the
        # expected value, and the tolerance is only in the detail ("shell_000: 123.4 vs 123.0 (tol 1.2); ...").
        # Show the shell that uses the largest share of its tolerance.
        $detail = ""; if ($c.PSObject.Properties["detail"]) { $detail = [string]$c.detail }
        $shells = [regex]::Matches($detail, "shell_\d+: (-?[0-9.eE+-]+) vs (-?[0-9.eE+-]+) \(tol ([0-9.eE+-]+)\)")
        if ($shells.Count -gt 0) {
            $worst = $null; $worstUse = -1.0
            foreach ($s in $shells) {
                $rep = [double]::Parse($s.Groups[1].Value, $inv); $exp = [double]::Parse($s.Groups[2].Value, $inv)
                $tolV = [double]::Parse($s.Groups[3].Value, $inv)
                $use = 0.0; if ($tolV -gt 0) { $use = [Math]::Abs($rep - $exp) / $tolV }
                if ($use -gt $worstUse) { $worstUse = $use; $worst = @($rep, $exp, $tolV) }
            }
            $line = "{0,-20} repaired {1,-11} expected {2,-11} +/- {3,-9} uses {4,5:0.0} % of tolerance" -f `
                $c.name, $worst[0].ToString("0.##", $inv), $worst[1].ToString("0.##", $inv), $worst[2].ToString("0.##", $inv), (100 * $worstUse)
            if ($shells.Count -gt 1) { $line += "  (worst of $($shells.Count) shells)" }
            Write-Host $line
        } else {
            Write-Host ("{0,-20} value {1,-16} limit {2}" -f $c.name, $c.value, $c.limit)
        }
    }
    Write-Host ""
    if ($result.pass) { Write-Host "    case passes" -ForegroundColor Green } else { Write-Host "    case fails" -ForegroundColor Red }
    Wait-Next
}

# ---------------------------------------------------------------- 4. before / after
if ($Step -le 4) {
    Show-Step 4 "Before and after" "Same camera on both sides. Red: free edges. Magenta: flipped faces."
    $inputObj = Join-Path $caseDir "input.obj"
    $repaired = Join-Path $outCase "repaired.obj"
    if (-not (Test-Path $repaired)) { Stop-Demo "no repaired.obj; run from step 2" }
    & $m2w render $inputObj --out (Join-Path $demo "$Case-before.svg") --frame $repaired --title "$Case : input" 2>$null | Out-Null
    & $m2w render $repaired --out (Join-Path $demo "$Case-after.svg") --frame $inputObj --title "$Case : repaired" 2>$null | Out-Null

    $pvbin = $ParaViewBin
    if (-not $pvbin) {
        $found = Get-ChildItem "C:\Program Files" -Directory -Filter "ParaView*" -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($found) { $pvbin = Join-Path $found.FullName "bin" }
    }
    if (-not $pvbin -or -not (Test-Path (Join-Path $pvbin "pvbatch.exe"))) {
        Write-Host "    ParaView not found: showing the SVGs instead (pass -ParaViewBin <ParaView\bin>)" -ForegroundColor DarkYellow
        if (-not $NoParaView) {
            Start-Process (Join-Path $demo "$Case-before.svg"); Start-Process (Join-Path $demo "$Case-after.svg")
        }
    } else {
        $scene = Join-Path $demo "$Case-paraview"
        Show-Cmd "pvbatch demo\paraview_scene.py   (builds the ParaView scene)"
        $lines = & (Join-Path $pvbin "pvbatch.exe") (Join-Path $repo "demo\paraview_scene.py") $inputObj $repaired $scene `
            --title-before "$Case : input" --title-after "$Case : repaired" 2>$null
        $lines | Where-Object { $_ -match "^(before|after):" } | ForEach-Object { Write-Host ("    " + $_) }
        $state = Join-Path $scene "scene.pvsm"
        if (-not (Test-Path $state)) { Stop-Demo "ParaView scene was not written" }
        if (-not $NoParaView) {
            Write-Host ""
            Write-Host "    opening ParaView: drag to orbit (both views follow), scroll to zoom" -ForegroundColor Gray
            Start-Process (Join-Path $pvbin "paraview.exe") -ArgumentList "--state=`"$state`""
        }
    }
    Wait-Next
}

# ---------------------------------------------------------------- 5. all visible cases
if ($Step -le 5) {
    Show-Step 5 "Every visible case" "Each one repaired, then graded by the independent checker."
    Show-Cmd "m2w run-all cases --out out\demo\all"
    $all = Join-Path $demo "all"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $m2w run-all (Join-Path $repo "cases") --out $all 2>$null | ForEach-Object {
        if ($_ -match "^\s*PASS") { Write-Host $_ -ForegroundColor DarkGreen }
        elseif ($_ -match "^\s*FAIL") { Write-Host $_ -ForegroundColor Red }
    }
    $sw.Stop()
    $summary = Get-Content (Join-Path $all "summary.json") -Raw | ConvertFrom-Json
    Write-Host ""
    $colour = "Green"; if ($summary.casesPassed -ne $summary.casesTotal) { $colour = "Red" }
    Write-Host ("    {0}/{1} cases pass in {2:0.0} s" -f $summary.casesPassed, $summary.casesTotal, $sw.Elapsed.TotalSeconds) -ForegroundColor $colour
    Write-Host ""
    $faultRows = $summary.byFault.PSObject.Properties | ForEach-Object { "{0} {1}/{2}" -f $_.Name, $_.Value.passed, $_.Value.total }
    for ($i = 0; $i -lt $faultRows.Count; $i += 4) {
        Write-Host ("    " + (($faultRows[$i..([Math]::Min($i + 3, $faultRows.Count - 1))] | ForEach-Object { "{0,-24}" -f $_ }) -join ""))
    }
    Wait-Next
}

# ---------------------------------------------------------------- 6. CI
if ($Step -le 6) {
    Show-Step 6 "Checked on every push" "GitHub Actions: Linux and Windows, run twice each, outputs compared byte for byte."
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    $ghPath = $null
    if ($gh) { $ghPath = $gh.Source } elseif (Test-Path "C:\Program Files\GitHub CLI\gh.exe") { $ghPath = "C:\Program Files\GitHub CLI\gh.exe" }
    if ($ghPath) {
        Show-Cmd "gh run list --workflow ci.yml"
        & $ghPath run list --workflow ci.yml -L 3 2>$null | ForEach-Object { Write-Host ("    " + $_) }
    }
    Write-Host ""
    Write-Host ("    " + $ciUrl) -ForegroundColor Gray
    if (-not $NoBrowser) { Start-Process $ciUrl }
}

Write-Host ""
Write-Host "  end of demo   |   outputs in out\demo" -ForegroundColor DarkCyan
Write-Host ""
