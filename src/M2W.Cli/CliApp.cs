using System.Diagnostics;
using M2W.Geometry;
using M2W.Geometry.Json;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;

namespace M2W.Cli;

/// <summary>
/// The m2w command line. Exit codes follow AGREEMENT.md:
/// <list type="bullet">
/// <item><c>repair</c>: 0 repaired, 1 repair failed (or partial), 2 bad input.</item>
/// <item><c>check</c>: 0 all checks pass, 1 a check failed, 2 missing file (or no checker yet).</item>
/// <item><c>run-all</c>: 0 every case was run and summary.json written (whatever the pass rate), 2 bad input.</item>
/// <item><c>generate</c>: 0 cases written, 2 bad arguments or no generator yet.</item>
/// </list>
/// </summary>
public sealed class CliApp
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int BadInput = 2;
    public const long DefaultSeed = 1;
    public const string DefaultOut = "out";

    private readonly StageSet _stages;
    private readonly TextWriter _out;
    private readonly TextWriter _err;

    public CliApp(StageSet stages, TextWriter stdout, TextWriter stderr)
    {
        _stages = stages;
        _out = stdout;
        _err = stderr;
    }

    public static int RunConsole(string[] args) =>
        new CliApp(StageSet.Discover(), Console.Out, Console.Error).Run(args);

    public int Run(string[] args)
    {
        if (!TryParse(args, out var cmd, out var dir, out var outDir, out var seed, out var problem))
        {
            if (problem is not null) _err.WriteLine($"m2w: {problem}");
            return Usage();
        }

        try
        {
            return cmd switch
            {
                "generate" => Generate(dir, seed),
                "repair" => Repair(dir, outDir),
                "check" => Check(dir, outDir),
                "run-all" => RunAll(dir, outDir),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            // Not hidden: printed in full and reported as a failure.
            _err.WriteLine($"m2w {cmd}: unexpected error: {ex}");
            return Failed;
        }
    }

    private int Usage()
    {
        _err.WriteLine("""
            usage:
              m2w generate <casesDir> [--seed N]     build test cases (generator)
              m2w repair   <caseDir>  [--out DIR]    repair one case (solver)
              m2w check    <caseDir>  [--out DIR]    grade one case (checker)
              m2w run-all  <casesDir> [--out DIR]    repair + check every folder containing case.json, write DIR/summary.json
            """);
        return BadInput;
    }

    private static bool TryParse(string[] args, out string cmd, out string dir, out string outDir, out long seed, out string? problem)
    {
        cmd = dir = "";
        outDir = DefaultOut;
        seed = DefaultSeed;
        problem = null;
        if (args.Length < 2) return false;
        cmd = args[0];
        dir = args[1];
        for (var i = 2; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--out" && i + 1 < args.Length && cmd is "repair" or "check" or "run-all")
                outDir = args[++i];
            else if (a is "--seed" && i + 1 < args.Length && cmd is "generate")
            {
                if (!long.TryParse(args[++i], System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out seed))
                {
                    problem = $"--seed needs an integer, got '{args[i]}'";
                    return false;
                }
            }
            else
            {
                problem = $"unexpected argument '{a}' for '{cmd}'";
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- generate

    private int Generate(string casesDir, long seed)
    {
        if (_stages.Generator is null)
        {
            _err.WriteLine($"m2w generate: not implemented yet: no ICaseGenerator in {StageSet.GeneratorAssembly} (milestone M2).");
            return BadInput;
        }
        Directory.CreateDirectory(casesDir);
        var n = _stages.Generator.Generate(new GenerateRequest { CasesDir = Path.GetFullPath(casesDir), Seed = seed });
        _out.WriteLine($"generate: wrote {n} cases to {casesDir} (seed {seed})");
        return Ok;
    }

    // ---------------------------------------------------------------- repair

    private int Repair(string caseDir, string outDir)
    {
        CaseSpec spec;
        Mesh input;
        try
        {
            spec = CaseSpec.Load(Path.Combine(caseDir, CaseFiles.Case));
            var inputPath = Path.Combine(caseDir, spec.Files.Input);
            input = ObjReader.ReadFile(inputPath);
        }
        catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException or DirectoryNotFoundException or ObjFormatException)
        {
            _err.WriteLine($"m2w repair {caseDir}: bad input: {ex.Message}");
            return BadInput;
        }

        var caseOut = Path.Combine(outDir, spec.CaseId);
        Directory.CreateDirectory(caseOut);
        // Remove the previous run's output so a stale repaired.obj or result.json can never be graded or counted.
        foreach (var stale in new[] { CaseFiles.Repaired, CaseFiles.Report, CaseFiles.Result })
            File.Delete(Path.Combine(caseOut, stale));

        var repairer = _stages.Repairer ?? new StubRepairer();
        var outcome = repairer.Repair(new RepairRequest { Case = spec, Input = input });

        var problems = CheckOutcome(spec, outcome);
        if (problems.Count > 0)
        {
            _err.WriteLine($"m2w repair {spec.CaseId}: the solver broke the output contract: {string.Join("; ", problems)}");
            return Failed;
        }

        if (outcome.Repaired is not null) ObjWriter.WriteFile(outcome.Repaired, Path.Combine(caseOut, CaseFiles.Repaired));
        M2WJson.WriteFile(outcome.Report, Path.Combine(caseOut, CaseFiles.Report));

        foreach (var w in outcome.Report.Warnings) _err.WriteLine($"m2w repair {spec.CaseId}: warning: {w}");
        return outcome.Report.Status == RepairStatus.Repaired ? Ok : Failed;
    }

    private static List<string> CheckOutcome(CaseSpec spec, RepairOutcome? outcome)
    {
        var p = new List<string>();
        if (outcome?.Report is null) { p.Add("no report"); return p; }
        p.AddRange(outcome.Report.Validate());
        if (outcome.Report.CaseId != spec.CaseId) p.Add($"report caseId '{outcome.Report.CaseId}' is not '{spec.CaseId}'");
        if (outcome.Report.Status != RepairStatus.Failed && outcome.Repaired is null)
            p.Add($"status is {outcome.Report.Status} but no repaired mesh was returned");
        if (outcome.Repaired is not null)
        {
            try { outcome.Repaired.Validate(); }
            catch (InvalidOperationException ex) { p.Add($"repaired mesh is invalid: {ex.Message}"); }
        }
        return p;
    }

    // ---------------------------------------------------------------- check

    private int Check(string caseDir, string outDir) => CheckCase(caseDir, outDir, out _);

    private int CheckCase(string caseDir, string outDir, out CheckResult? result)
    {
        result = null;
        CaseSpec spec;
        ExpectedSpec expected;
        try
        {
            spec = CaseSpec.Load(Path.Combine(caseDir, CaseFiles.Case));
            expected = ExpectedSpec.Load(Path.Combine(caseDir, CaseFiles.Expected));
        }
        catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException or DirectoryNotFoundException)
        {
            _err.WriteLine($"m2w check {caseDir}: {ex.Message}");
            return BadInput;
        }

        var caseOut = Path.Combine(outDir, spec.CaseId);
        var repaired = Path.Combine(caseOut, CaseFiles.Repaired);
        var resultPath = Path.Combine(caseOut, CaseFiles.Result);
        if (File.Exists(resultPath)) File.Delete(resultPath);

        if (!File.Exists(repaired))
        {
            _err.WriteLine($"m2w check {spec.CaseId}: missing file: {repaired}");
            return BadInput;
        }
        if (_stages.Checker is null)
        {
            _err.WriteLine($"m2w check {spec.CaseId}: not implemented yet: no ICaseChecker in {StageSet.CheckerAssembly} (milestone M2).");
            return BadInput;
        }

        var report = Path.Combine(caseOut, CaseFiles.Report);
        result = _stages.Checker.Check(new CheckRequest
        {
            CaseDir = Path.GetFullPath(caseDir),
            Case = spec,
            Expected = expected,
            RepairedObjPath = Path.GetFullPath(repaired),
            ReportPath = File.Exists(report) ? Path.GetFullPath(report) : null,
        });

        if (result.CaseId != spec.CaseId)
            throw new InvalidOperationException($"checker returned caseId '{result.CaseId}' for case '{spec.CaseId}'");
        M2WJson.WriteFile(result, resultPath); // validates, including pass == all checks pass
        return result.Pass ? Ok : Failed;
    }

    // ---------------------------------------------------------------- run-all

    private int RunAll(string casesDir, string outDir)
    {
        if (!Directory.Exists(casesDir))
        {
            _err.WriteLine($"m2w run-all: cases folder not found: {casesDir}");
            return BadInput;
        }
        var root = Path.GetFullPath(casesDir);
        var dirs = CaseDiscovery.FindCaseDirs(root);
        if (dirs.Count == 0)
        {
            _err.WriteLine($"m2w run-all: no folder under {casesDir} contains {CaseFiles.Case}");
            return BadInput;
        }

        // Two folders with the same caseId would write to the same out/<caseId>/ and grade each other's output.
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            string id;
            try { id = CaseSpec.Load(Path.Combine(d, CaseFiles.Case)).CaseId; }
            catch (ModelValidationException) { continue; } // reported as bad input when the case runs
            if (ids.TryGetValue(id, out var other))
            {
                _err.WriteLine($"m2w run-all: caseId '{id}' is used by both {CaseDiscovery.RelativePath(root, other)} and {CaseDiscovery.RelativePath(root, d)}");
                return BadInput;
            }
            ids[id] = d;
        }

        Directory.CreateDirectory(outDir);
        _out.WriteLine($"run-all: {dirs.Count} case(s) under {casesDir}; repair: {Describe(_stages.Repairer, "stub")}; check: {Describe(_stages.Checker, "not implemented")}");

        var watch = Stopwatch.StartNew();
        var cases = new List<CaseSummary>();
        foreach (var d in dirs)
        {
            var rel = CaseDiscovery.RelativePath(root, d);
            var repairExit = Repair(d, outDir);
            var checkExit = CheckCase(d, outDir, out var result);

            string caseId;
            IReadOnlyList<string> faults;
            try { caseId = CaseSpec.Load(Path.Combine(d, CaseFiles.Case)).CaseId; }
            catch (ModelValidationException) { caseId = Path.GetFileName(d); }
            try { faults = ExpectedSpec.Load(Path.Combine(d, CaseFiles.Expected)).FaultsInjected; }
            catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException) { faults = new[] { RunSummary.UnknownKey }; }

            var pass = checkExit == Ok && result is { Pass: true };
            var failed = result?.Checks.Where(c => !c.Pass).Select(c => c.Name).ToList() ?? new List<string>();
            cases.Add(new CaseSummary
            {
                CaseId = caseId,
                Path = rel,
                Faults = faults,
                RepairExit = repairExit,
                CheckExit = checkExit,
                Pass = pass,
                FailedChecks = failed,
            });
            _out.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {caseId,-12} repair={repairExit} check={checkExit}  {rel}");
        }

        var byFault = new SortedDictionary<string, FaultTally>(StringComparer.Ordinal);
        foreach (var c in cases)
            foreach (var f in c.Faults.Count == 0 ? new[] { RunSummary.CleanKey } : c.Faults)
            {
                byFault.TryGetValue(f, out var t);
                byFault[f] = new FaultTally { Total = (t?.Total ?? 0) + 1, Passed = (t?.Passed ?? 0) + (c.Pass ? 1 : 0) };
            }

        var summary = new RunSummary
        {
            SchemaVersion = SchemaVersions.Current,
            CasesTotal = cases.Count,
            CasesPassed = cases.Count(c => c.Pass),
            ByFault = byFault,
            Cases = cases,
        };
        var summaryPath = Path.Combine(outDir, CaseFiles.Summary);
        M2WJson.WriteFile(summary, summaryPath);
        _out.WriteLine($"run-all: {summary.CasesPassed}/{summary.CasesTotal} cases pass; wrote {summaryPath} ({watch.ElapsedMilliseconds} ms)");
        return Ok;
    }

    private static string Describe(object? stage, string fallback) => stage?.GetType().FullName ?? fallback;
}
